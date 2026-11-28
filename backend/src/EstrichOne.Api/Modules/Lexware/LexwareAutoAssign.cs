using System.Text.RegularExpressions;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Lexware;

/// <summary>Bauakte eines verknüpften Kunden als mögliches Ziel der automatischen Zuordnung.</summary>
/// <param name="Open">Weder abgeschlossen noch storniert.</param>
/// <param name="From">Anlage der Bauakte (Beginn des Zeitraums für Regel „einzige offene Bauakte“).</param>
public sealed record AutoAssignBauakte(Guid Id, string? Number, bool Open, DateOnly From);

/// <summary>Beleg aus Lexware Office, wie ihn die automatische Zuordnung prüft.</summary>
/// <param name="Text">Titel, Einleitung, Schlusstext und Positionstexte.</param>
/// <param name="Preceding">IDs vorhergehender Belege (Belegkette), soweit Lexware Office sie nennt.</param>
public sealed record AutoAssignVoucher(string LexwareId, string Type, DateOnly? Date, string? Number, string Text, IReadOnlyList<string> Preceding);

/// <summary>Entscheidung: Bauakte und Grund (<see cref="LexwareAutoAssignRules"/>).</summary>
public sealed record AutoAssignDecision(Guid BauakteId, string Reason);

/// <summary>
/// Regeln der automatischen Belegzuordnung als reine Funktionen. Sicher heißt eindeutig: eigener Beleg aus EstrichONE,
/// Bauaktennummer im Beleg bei verknüpftem Kunden dieser Bauakte, Belegkette über einen bereits zugeordneten Beleg oder
/// (eigener Schalter, Vorgabe aus) die einzige offene Bauakte des Kunden mit Belegdatum im Zeitraum. Mehrere mögliche
/// Bauakten oder Widersprüche führen nie zu einer Zuordnung.
/// </summary>
public static partial class LexwareAutoAssignRules
{
    public const string ReasonOwn = "own";
    public const string ReasonNumber = "number";
    public const string ReasonChain = "chain";
    public const string ReasonSingleOpen = "singleOpen";

    public static string ReasonText(string? reason) => reason switch
    {
        ReasonOwn => "automatisch zugeordnet: in EstrichONE erstellt",
        ReasonNumber => "automatisch zugeordnet: Bauaktennummer im Beleg",
        ReasonChain => "automatisch zugeordnet: Belegkette",
        ReasonSingleOpen => "automatisch zugeordnet: einzige offene Bauakte des Kunden",
        _ => "zugeordnet",
    };

    /// <summary>Bauaktennummer als eigenes Wort im Text (nicht Teil einer längeren Nummer).</summary>
    public static bool Mentions(string text, string? number) =>
        !string.IsNullOrWhiteSpace(number)
        && Regex.IsMatch(text, $@"(?<![\p{{L}}\p{{N}}-]){Regex.Escape(number.Trim())}(?![\p{{L}}\p{{N}}-]|-\p{{N}})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(200));

    /// <param name="bauakten">Bauakten des Kunden, mit dem der Lexware-Kontakt des Belegs verknüpft ist (leer: Kontakt nicht verknüpft).</param>
    /// <param name="linked">Bereits zugeordnete Belege: Lexware-ID zu Bauakte.</param>
    /// <param name="own">Bauakte, wenn der Beleg in EstrichONE erstellt wurde.</param>
    public static AutoAssignDecision? Decide(AutoAssignVoucher voucher, IReadOnlyList<AutoAssignBauakte> bauakten, IReadOnlyDictionary<string, Guid> linked,
        Guid? own, bool singleOpenRule, DateOnly today)
    {
        if (own is { } ownBauakte)
        {
            return new AutoAssignDecision(ownBauakte, ReasonOwn);
        }

        var chain = voucher.Preceding.Select(p => linked.TryGetValue(p, out var b) ? b : (Guid?)null).OfType<Guid>().Distinct().ToList();
        var named = bauakten.Where(b => Mentions(voucher.Text + " " + voucher.Number, b.Number)).Select(b => b.Id).Distinct().ToList();
        if (chain.Count > 1 || named.Count > 1)
        {
            return null; // mehrdeutig
        }

        if (chain.Count == 1)
        {
            return named.Count == 0 || named[0] == chain[0] ? new AutoAssignDecision(chain[0], ReasonChain) : null; // Widerspruch
        }

        if (named.Count == 1)
        {
            return new AutoAssignDecision(named[0], ReasonNumber);
        }

        if (singleOpenRule && bauakten.Count(b => b.Open) == 1 && bauakten.Single(b => b.Open) is var single
            && voucher.Date is { } date && date >= single.From && date <= today)
        {
            return new AutoAssignDecision(single.Id, ReasonSingleOpen);
        }

        return null;
    }
}

/// <summary>Von Hand aufgehobene automatische Zuordnung: derselbe Beleg wird nicht erneut automatisch zugeordnet.</summary>
public sealed class LexwareAutoAssignBlock
{
    public string LexwareId { get; set; } = string.Empty;
    public Guid BauakteId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset BlockedAt { get; set; }
}

internal sealed class LexwareAutoAssignBlockConfiguration : IEntityTypeConfiguration<LexwareAutoAssignBlock>
{
    public void Configure(EntityTypeBuilder<LexwareAutoAssignBlock> builder)
    {
        builder.ToTable("lexware_auto_assign_blocks");
        builder.HasKey(b => b.LexwareId);
        builder.Property(b => b.LexwareId).HasMaxLength(100);
    }
}

/// <summary>Ergebnis eines Laufs der automatischen Zuordnung.</summary>
public sealed record AutoAssignRun(int Checked, int Assigned, string? Error);

/// <summary>
/// Automatische Belegzuordnung: prüft nicht zugeordnete Belege aus Lexware Office (neueste zuerst, je Lauf begrenzt) und
/// ordnet nur sichere zu. Vorhandene Zuordnungen werden nie geändert; aufgehobene merkt sich EstrichONE.
/// </summary>
public sealed class LexwareAutoAssignService(LexwareService lexware, ISystemSettings system, IAuditLog audit)
{
    /// <summary>Höchstens so viele Belege je Lauf einzeln lesen (Texte für die Bauaktennummer), wegen der Drosselung von Lexware Office.</summary>
    public const int MaxReadsPerRun = 20;

    public async Task<AutoAssignRun> RunAsync(string? onlyLexwareContactId, CancellationToken cancellationToken)
    {
        var row = await lexware.SettingsAsync(cancellationToken);
        if (!row.Configured || !row.AutoAssign || !(await system.GetAsync(cancellationToken)).Modules.IsEnabled(AppModules.Lexware))
        {
            return new AutoAssignRun(0, 0, null);
        }

        var db = lexware.Db;
        var connection = lexware.Connection(row);
        IReadOnlyList<LexwareVoucher> list;
        try
        {
            list = await lexware.Client.ListVouchersAsync(connection, onlyLexwareContactId, null, 100, cancellationToken);
        }
        catch (LexwareException exception)
        {
            return new AutoAssignRun(0, 0, exception.Message);
        }

        var linked = await db.Set<LexwareVoucherLink>().AsNoTracking().ToDictionaryAsync(l => l.LexwareId, l => l.BauakteId, cancellationToken);
        var blocked = (await db.Set<LexwareAutoAssignBlock>().AsNoTracking().Select(b => b.LexwareId).ToListAsync(cancellationToken)).ToHashSet();
        var contacts = await db.Set<LexwareContactLink>().AsNoTracking().ToDictionaryAsync(l => l.LexwareContactId, l => l.ContactId, cancellationToken);
        var own = await db.Set<SalesVoucher>().AsNoTracking().Where(v => v.LexwareId != null && v.BauakteId != null)
            .ToDictionaryAsync(v => v.LexwareId!, v => v.BauakteId!.Value, cancellationToken);
        var open = list.Where(v => !linked.ContainsKey(v.Id) && !blocked.Contains(v.Id) && v.ContactId is { } c && contacts.ContainsKey(c)).ToList();
        if (open.Count == 0)
        {
            return new AutoAssignRun(0, 0, null);
        }

        var customerIds = open.Select(v => contacts[v.ContactId!]).Distinct().ToList();
        var bauakten = (await db.Set<Bauakte>().AsNoTracking().Where(b => b.CustomerId != null && customerIds.Contains(b.CustomerId.Value))
                .Select(b => new { b.Id, b.Number, b.Status, b.CreatedAt, CustomerId = b.CustomerId!.Value }).ToListAsync(cancellationToken))
            .GroupBy(b => b.CustomerId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<AutoAssignBauakte>)g.Select(b => new AutoAssignBauakte(b.Id, b.Number,
                b.Status is not (BauakteStatus.Completed or BauakteStatus.Cancelled), DateOnly.FromDateTime(b.CreatedAt.UtcDateTime))).ToList());
        var today = lexware.Today();
        var reads = 0;
        var assigned = 0;
        foreach (var item in open)
        {
            var customer = contacts[item.ContactId!];
            var candidates = bauakten.GetValueOrDefault(customer) ?? [];
            if (candidates.Count == 0 && !own.ContainsKey(item.Id))
            {
                continue;
            }

            LexwareVoucher full;
            try
            {
                if (own.ContainsKey(item.Id))
                {
                    full = item;
                }
                else
                {
                    if (reads++ >= MaxReadsPerRun)
                    {
                        break;
                    }

                    full = await lexware.Client.GetVoucherAsync(connection, item.Type, item.Id, cancellationToken);
                }
            }
            catch (LexwareException)
            {
                continue;
            }

            var voucher = new AutoAssignVoucher(item.Id, item.Type, full.VoucherDate ?? item.VoucherDate, full.Number ?? item.Number, full.Text ?? string.Empty,
                full.Preceding ?? []);
            if (LexwareAutoAssignRules.Decide(voucher, candidates, linked, own.TryGetValue(item.Id, out var ownBauakte) ? ownBauakte : null,
                    row.AutoAssignSingleOpen, today) is not { } decision)
            {
                continue;
            }

            var link = new LexwareVoucherLink
            {
                Id = Guid.CreateVersion7(), BauakteId = decision.BauakteId, LexwareId = item.Id, VoucherType = item.Type, LinkedAt = lexware.Clock.GetUtcNow(),
                Origin = LexwareVoucherLink.OriginAuto, AutoReason = decision.Reason,
            };
            lexware.Apply(link, full);
            db.Add(link);
            linked[item.Id] = decision.BauakteId;
            var details = new Dictionary<string, string?>
            {
                ["kind"] = item.Type, ["number"] = link.Number, ["reason"] = LexwareAutoAssignRules.ReasonText(decision.Reason),
            };
            // Verlauf der Bauakte (aus dem Protokoll abgeleitet), ohne Benutzer: „automatisch zugeordnet: …“.
            audit.Record("bauakte.lexware_auto_assigned", null, "bauakte", decision.BauakteId.ToString(), details);
            assigned++;
        }

        if (assigned > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return new AutoAssignRun(open.Count, assigned, null);
    }
}
