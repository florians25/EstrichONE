using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Contacts.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Lexware;

/// <summary>
/// Stand der automatischen Anlage eines Kunden in Lexware Office. Eine Zeile gibt es nur, wenn nicht (oder noch nicht)
/// angelegt wurde: Ohne Zeile und ohne Verknüpfung steht die Anlage aus.
/// </summary>
public sealed class LexwareContactCreation
{
    /// <summary>Es gibt ähnliche Kontakte in Lexware Office: nicht automatisch angelegt, der Kunde steht unter „Mögliche Dubletten“.</summary>
    public const string Duplicates = "duplicates";

    /// <summary>Anlage fehlgeschlagen; der nächste Versuch folgt später oder über „Erneut versuchen“.</summary>
    public const string Failed = "failed";

    /// <summary>Bestandskunde bzw. automatische Anlage war ausgeschaltet: wird nicht ungefragt angelegt.</summary>
    public const string Skipped = "skipped";

    public Guid ContactId { get; set; }
    public string State { get; set; } = Skipped;
    public string? Error { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed class LexwareContactCreationConfiguration : IEntityTypeConfiguration<LexwareContactCreation>
{
    public void Configure(EntityTypeBuilder<LexwareContactCreation> builder)
    {
        builder.ToTable("lexware_contact_creations");
        builder.HasKey(c => c.ContactId);
        builder.Property(c => c.State).HasMaxLength(20);
        builder.Property(c => c.Error).HasMaxLength(2000);
    }
}

/// <summary>Ergebnis eines Anlageversuchs.</summary>
/// <param name="State"><c>created</c>, <c>duplicates</c>, <c>failed</c> oder <c>none</c> (nichts zu tun: verknüpft, Schalter aus, archiviert).</param>
public sealed record LexwareCreationResult(string State, string? Error = null);

/// <summary>
/// Neue Kunden automatisch in Lexware Office anlegen: nur mit eingeschaltetem „Mit Lexware Office abgleichen“, nur ohne
/// ähnlichen Kontakt in Lexware Office (Dublettenprüfung vor jedem Versuch) und nie für Bestandskunden. Gibt es Treffer,
/// wird nichts angelegt; der Kunde steht dann in der Abgleichübersicht unter „Mögliche Dubletten“. Läuft im Hintergrund,
/// das Speichern eines Kunden wartet nie darauf.
/// </summary>
public sealed class LexwareAutoCreateService(LexwareService lexware, IAuditLog audit, ISystemSettings system, IServiceScopeFactory scopes,
    ILogger<LexwareAutoCreateService> logger)
{
    private const int BatchSize = 20;

    /// <summary>Kunden, deren Anlage aussteht: Abgleich an, nicht verknüpft, nicht archiviert, noch kein Vermerk.</summary>
    public static IQueryable<Contact> Pending(EstrichOneDbContext db) => db.Set<Contact>().Where(c => c.LexwareSync && c.ArchivedAt == null && c.AnonymizedAt == null
        && !db.Set<LexwareContactLink>().Any(l => l.ContactId == c.Id) && !db.Set<LexwareContactCreation>().Any(x => x.ContactId == c.Id));

    public async Task<bool> ActiveAsync(LexwareSettingsRow row, CancellationToken cancellationToken) =>
        row.Configured && row.AutoCreate && (await system.GetAsync(cancellationToken)).Modules.IsEnabled(AppModules.Lexware);

    /// <summary>Ein Lauf: ausstehende und fällige fehlgeschlagene Anlagen abarbeiten. Liefert die Zahl der angelegten Kontakte.</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var db = lexware.Db;
        var row = await lexware.SettingsAsync(cancellationToken);
        if (!await ActiveAsync(row, cancellationToken))
        {
            return 0;
        }

        var now = lexware.Clock.GetUtcNow();
        var pending = await Pending(db).OrderBy(c => c.CreatedAt).Select(c => c.Id).Take(BatchSize).ToListAsync(cancellationToken);
        var retries = await db.Set<LexwareContactCreation>().Where(x => x.State == LexwareContactCreation.Failed && x.NextAttemptAt != null && x.NextAttemptAt <= now)
            .Select(x => x.ContactId).Take(BatchSize).ToListAsync(cancellationToken);
        if (pending.Count + retries.Count == 0)
        {
            return 0;
        }

        // Nach einem Fehlversuch ist offen, ob Lexware Office den Kontakt doch angelegt hat: Liste neu lesen, damit die
        // Dublettenprüfung ihn findet und nichts doppelt entsteht.
        IReadOnlyList<LexwareContact> remote;
        try
        {
            (remote, _) = await lexware.ContactsAsync(row, refresh: retries.Count > 0, cancellationToken);
        }
        catch (LexwareException exception)
        {
            logger.LogWarning("Automatische Anlage in Lexware Office verschoben: {Code}", exception.Code);
            return 0;
        }

        var created = 0;
        foreach (var contactId in pending.Concat(retries))
        {
            using var scope = scopes.CreateScope();
            var worker = scope.ServiceProvider.GetRequiredService<LexwareAutoCreateService>();
            var result = await worker.TryAsync(contactId, null, cancellationToken);
            if (result.State == "created")
            {
                created++;
            }
            else if (result.State == "unavailable")
            {
                break; // Lexware Office ist nicht erreichbar: Der nächste Lauf macht weiter.
            }
        }

        return created;
    }

    /// <summary>
    /// Einen Kunden anlegen, wenn es in Lexware Office keinen ähnlichen Kontakt gibt. Die Dublettenprüfung läuft vor jedem
    /// Versuch erneut; ein bereits angelegter Kontakt wird so gefunden und nicht doppelt angelegt.
    /// </summary>
    public async Task<LexwareCreationResult> TryAsync(Guid contactId, Guid? actor, CancellationToken cancellationToken)
    {
        var gate = lexware.SyncGate;
        await gate.WaitAsync(cancellationToken);
        try
        {
            var db = lexware.Db;
            var row = await lexware.SettingsAsync(cancellationToken);
            if (await db.Set<Contact>().Include(c => c.Persons).SingleOrDefaultAsync(c => c.Id == contactId, cancellationToken) is not { } contact
                || !contact.LexwareSync || contact.ArchivedAt is not null || contact.AnonymizedAt is not null
                || await db.Set<LexwareContactLink>().AnyAsync(l => l.ContactId == contactId, cancellationToken))
            {
                return new LexwareCreationResult("none");
            }

            var note = await db.Set<LexwareContactCreation>().SingleOrDefaultAsync(x => x.ContactId == contactId, cancellationToken);
            LexwareContactCreation Note()
            {
                if (note is null)
                {
                    db.Add(note = new LexwareContactCreation { ContactId = contactId });
                }

                note.UpdatedAt = lexware.Clock.GetUtcNow();
                return note;
            }

            try
            {
                var connection = lexware.Connection(row);
                var (remote, _) = await lexware.ContactsAsync(row, refresh: note?.State == LexwareContactCreation.Failed, cancellationToken);
                var matches = LexwareMatching.Find(LexwareService.Party(contact), remote.Select(LexwareService.Party), 5);
                if (matches.Count > 0)
                {
                    var marked = Note();
                    marked.State = LexwareContactCreation.Duplicates;
                    marked.Error = null;
                    marked.NextAttemptAt = null;
                    await db.SaveChangesAsync(cancellationToken);
                    return new LexwareCreationResult(LexwareContactCreation.Duplicates);
                }

                var id = await lexware.Client.CreateContactAsync(connection, LexwareService.Draft(contact), cancellationToken);
                LexwareContact created;
                try
                {
                    created = await lexware.Client.GetContactAsync(connection, id, cancellationToken);
                }
                catch (LexwareException)
                {
                    var party = LexwareService.Party(contact);
                    created = new LexwareContact(id, party.IsCompany, contact.Name, null, null, null, null, true, party.Emails, party.Phones, party.Street,
                        party.PostalCode, party.City, party.VatId, null, false);
                }

                lexware.Remember(row, created);
                await lexware.LinkAsync(contact, created, LexwareLinkOrigin.CreatedInLexware, actor ?? Guid.Empty, cancellationToken);
                audit.Record("lexware.contact_created", actor, "contact", contactId.ToString(), new Dictionary<string, string?>
                {
                    ["name"] = contact.Name, ["lexwareNumber"] = created.CustomerNumber, ["auto"] = "true",
                });
                await db.SaveChangesAsync(cancellationToken);
                return new LexwareCreationResult("created");
            }
            catch (LexwareException exception)
            {
                db.ChangeTracker.Clear();
                note = await db.Set<LexwareContactCreation>().SingleOrDefaultAsync(x => x.ContactId == contactId, cancellationToken);
                var failed = Note();
                failed.State = LexwareContactCreation.Failed;
                failed.Error = exception.Message.Length > 2000 ? exception.Message[..2000] : exception.Message;
                failed.Attempts++;
                failed.NextAttemptAt = lexware.Clock.GetUtcNow() + TimeSpan.FromMinutes(Math.Min(60, Math.Pow(2, Math.Min(failed.Attempts, 6))));
                await db.SaveChangesAsync(cancellationToken);
                return new LexwareCreationResult(LexwareChangeSyncService.Global(exception) ? "unavailable" : LexwareContactCreation.Failed, failed.Error);
            }
        }
        finally
        {
            gate.Release();
        }
    }
}
