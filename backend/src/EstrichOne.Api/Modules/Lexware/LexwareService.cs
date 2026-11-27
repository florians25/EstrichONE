using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Contacts.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EstrichOne.Api.Modules.Lexware;

/// <summary>
/// Kontaktliste aus Lexware Office, kurz zwischengespeichert: Dublettenprüfung und Abgleichübersicht brauchen alle Kunden,
/// Lexware erlaubt aber nur zwei Anfragen je Sekunde. Der Stempel bindet den Stand an Schlüssel und Basis-URL.
/// </summary>
internal sealed class LexwareDirectory : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (string Stamp, DateTimeOffset LoadedAt, List<LexwareContact> Contacts)? _state;

    public async Task<(IReadOnlyList<LexwareContact> Contacts, DateTimeOffset LoadedAt)> GetAsync(string stamp, DateTimeOffset now, TimeSpan maxAge,
        Func<CancellationToken, Task<List<LexwareContact>>> load, CancellationToken cancellationToken)
    {
        if (_state is { } fresh && fresh.Stamp == stamp && now - fresh.LoadedAt < maxAge)
        {
            return (fresh.Contacts, fresh.LoadedAt);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_state is { } current && current.Stamp == stamp && now - current.LoadedAt < maxAge)
            {
                return (current.Contacts, current.LoadedAt);
            }

            var contacts = await load(cancellationToken);
            _state = (stamp, now, contacts);
            return (contacts, now);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Neu angelegten Kontakt sofort bekannt machen (ohne die Liste neu zu laden).</summary>
    public void Add(string stamp, LexwareContact contact)
    {
        if (_state is { } state && state.Stamp == stamp)
        {
            _state = (state.Stamp, state.LoadedAt, [.. state.Contacts.Where(c => c.Id != contact.Id), contact]);
        }
    }

    public void Clear()
    {
        _state = null;
        _customerVouchers.Clear();
        _voucherTexts.Clear();
        _documents.Clear();
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTimeOffset LoadedAt, IReadOnlyList<LexwareVoucher> Vouchers)> _customerVouchers = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTimeOffset LoadedAt, string Text)> _voucherTexts = new();

    /// <summary>Belege eines Kontakts, kurz zwischengespeichert (Schlüssel: Stempel und Kontakt).</summary>
    public async Task<(IReadOnlyList<LexwareVoucher> Vouchers, DateTimeOffset LoadedAt)> VouchersAsync(string key, DateTimeOffset now, TimeSpan maxAge,
        bool refresh, Func<Task<IReadOnlyList<LexwareVoucher>>> load)
    {
        if (!refresh && _customerVouchers.TryGetValue(key, out var cached) && now - cached.LoadedAt < maxAge)
        {
            return (cached.Vouchers, cached.LoadedAt);
        }

        var vouchers = await load();
        _customerVouchers[key] = (now, vouchers);
        return (vouchers, now);
    }

    /// <summary>Texte eines Belegs (Titel, Einleitung, Schlusstext) für den Zuordnungsvorschlag, zwischengespeichert.</summary>
    public async Task<string> VoucherTextAsync(string key, DateTimeOffset now, TimeSpan maxAge, Func<Task<string>> load)
    {
        if (_voucherTexts.TryGetValue(key, out var cached) && now - cached.LoadedAt < maxAge)
        {
            return cached.Text;
        }

        var text = await load();
        _voucherTexts[key] = (now, text);
        return text;
    }

    /// <summary>Ein Änderungsabgleich zur selben Zeit (Hintergrunddienst und Aufrufe aus der Oberfläche).</summary>
    public SemaphoreSlim SyncGate { get; } = new(1, 1);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTimeOffset LoadedAt, LexwareDocument Document)> _documents = new();

    /// <summary>Beleg-PDF, kurz im Arbeitsspeicher gehalten (höchstens acht Belege); nichts davon wird dauerhaft gespeichert.</summary>
    public async Task<LexwareDocument> DocumentAsync(string key, DateTimeOffset now, TimeSpan maxAge, Func<Task<LexwareDocument>> load)
    {
        foreach (var (old, entry) in _documents)
        {
            if (now - entry.LoadedAt >= maxAge)
            {
                _documents.TryRemove(old, out _);
            }
        }

        if (_documents.TryGetValue(key, out var cached))
        {
            return cached.Document;
        }

        var document = await load();
        if (maxAge > TimeSpan.Zero)
        {
            foreach (var oldest in _documents.OrderBy(d => d.Value.LoadedAt).Take(Math.Max(0, _documents.Count - 7)).ToList())
            {
                _documents.TryRemove(oldest.Key, out _);
            }

            _documents[key] = (now, document);
        }

        return document;
    }

    public void Dispose()
    {
        _gate.Dispose();
        SyncGate.Dispose();
    }
}

/// <summary>
/// Fachlogik der Lexware-Office-Anbindung: Einstellungen, Kontaktverknüpfung mit Dublettenprüfung und die lesende Anzeige
/// der Belege (Rechnungen, Angebote) mit Statusabgleich und PDF. Belege werden nie angelegt oder geändert. Den Abgleich
/// späterer Änderungen an Kunden führt <see cref="LexwareChangeSyncService"/>.
/// </summary>
public sealed class LexwareService(
    EstrichOneDbContext db, LexwareClient client, IDataProtectionProvider protection, IOptions<LexwareOptions> options, TimeProvider clock,
    IServiceProvider services)
{
    public const string ProtectorPurpose = "EstrichOne.Lexware.ApiKey.v1";

    /// <summary>Wo der Schlüssel in Lexware Office erzeugt wird.</summary>
    public const string KeyHelpUrl = "https://app.lexware.de/addons/public-api";

    private LexwareDirectory Directory => services.GetRequiredService<LexwareDirectory>();

    /// <summary>Ein Änderungsabgleich zur selben Zeit je Server.</summary>
    public SemaphoreSlim SyncGate => Directory.SyncGate;

    public EstrichOneDbContext Db => db;

    public LexwareClient Client => client;

    public TimeProvider Clock => clock;

    public LexwareOptions Options => options.Value;

    private IDataProtector Protector => protection.CreateProtector(ProtectorPurpose);

    public string Protect(string value) => Protector.Protect(value);

    // ---------- Einstellungen und Verbindung ----------

    public async Task<LexwareSettingsRow> SettingsAsync(CancellationToken cancellationToken) =>
        await db.Set<LexwareSettingsRow>().AsNoTracking().SingleOrDefaultAsync(cancellationToken) ?? new LexwareSettingsRow();

    public async Task<LexwareSettingsRow> SettingsForUpdateAsync(CancellationToken cancellationToken)
    {
        var row = await db.Set<LexwareSettingsRow>().SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            row = new LexwareSettingsRow();
            db.Add(row);
        }

        return row;
    }

    /// <summary>Basis-URL prüfen: nur https und nur die bekannten Lexware-Hosts, damit der Schlüssel nicht an fremde Server geht.</summary>
    public Uri ResolveBaseUri(string? configured)
    {
        var text = string.IsNullOrWhiteSpace(configured) ? LexwareOptions.DefaultBaseUrl : configured.Trim().TrimEnd('/');
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || uri.UserInfo.Length > 0)
        {
            throw new LexwareException("INVALID_BASE_URL", "Die Basis-URL ist ungültig. Vorgabe ist https://api.lexware.io.");
        }

        if (!Options.AllowAnyBaseUrl
            && (uri.Scheme != "https" || !Options.AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase) || uri.AbsolutePath != "/"))
        {
            throw new LexwareException("INVALID_BASE_URL",
                $"Als Basis-URL sind nur die Adressen von Lexware Office zulässig ({string.Join(", ", Options.AllowedHosts.Select(h => "https://" + h))}).");
        }

        return new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");
    }

    public LexwareConnection Connection(LexwareSettingsRow row)
    {
        if (!row.Configured)
        {
            throw NotConfigured();
        }

        string key;
        try
        {
            key = Protector.Unprotect(row.ProtectedApiKey!);
        }
        catch (CryptographicException)
        {
            throw new LexwareException("KEY_UNREADABLE",
                "Der gespeicherte API-Schlüssel ist nicht mehr lesbar. Bitte hinterlegen Sie den Schlüssel in den Einstellungen erneut.");
        }

        return new LexwareConnection(ResolveBaseUri(row.BaseUrl), key);
    }

    public async Task<LexwareConnection> ConnectionAsync(CancellationToken cancellationToken) => Connection(await SettingsAsync(cancellationToken));

    /// <summary>„Mit Lexware Office abgleichen“ ist für diesen Kunden aus: weder verknüpfen noch in Lexware anlegen.</summary>
    public static LexwareException SyncDisabled(string customerName) => new("SYNC_DISABLED",
        $"Der Kunde „{customerName}“ bleibt nur in EstrichONE: „Mit Lexware Office abgleichen“ ist ausgeschaltet. Schalten Sie den Abgleich für diesen Kunden ein, um ihn zu verknüpfen.");

    public static LexwareException NotConfigured() => new("NOT_CONFIGURED",
        "Lexware Office ist noch nicht eingerichtet. Hinterlegen Sie den API-Schlüssel unter Verwaltung › Integrationen › Lexware Office.");

    /// <summary>Adresse der Lexware-Oberfläche (für „In Lexware öffnen“), abgeleitet aus der Basis-URL oder fest eingestellt.</summary>
    public string AppBaseUrl(LexwareSettingsRow row)
    {
        if (!string.IsNullOrWhiteSpace(Options.AppBaseUrl))
        {
            return Options.AppBaseUrl.TrimEnd('/');
        }

        return (row.BaseUrl ?? string.Empty).Contains("lexoffice", StringComparison.OrdinalIgnoreCase) ? "https://app.lexoffice.de" : "https://app.lexware.de";
    }

    public string ContactUrl(LexwareSettingsRow row, string lexwareContactId) =>
        $"{AppBaseUrl(row)}/permalink/contacts/view/{Uri.EscapeDataString(lexwareContactId)}";

    /// <summary>Link zum Beleg in der Lexware-Oberfläche: Entwürfe im Bearbeitungsmodus, alle anderen in der Ansicht.</summary>
    public string VoucherUrl(LexwareSettingsRow row, string type, string lexwareId, string status) =>
        $"{AppBaseUrl(row)}/permalink/{LexwareVoucherTypes.Segment(type) ?? "invoices"}/{(status == "draft" ? "edit" : "view")}/{Uri.EscapeDataString(lexwareId)}";

    private static string Stamp(LexwareSettingsRow row) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{row.BaseUrl}|{row.ProtectedApiKey}")))[..16];

    public void ForgetContacts() => Directory.Clear();

    // ---------- Kontakte ----------

    /// <summary>Alle Kunden aus Lexware Office (seitenweise geladen, kurz zwischengespeichert).</summary>
    public async Task<(IReadOnlyList<LexwareContact> Contacts, DateTimeOffset LoadedAt)> ContactsAsync(LexwareSettingsRow row, bool refresh,
        CancellationToken cancellationToken)
    {
        var connection = Connection(row);
        if (refresh)
        {
            Directory.Clear();
        }

        return await Directory.GetAsync(Stamp(row), clock.GetUtcNow(), Options.ContactCacheDuration, async ct =>
        {
            var all = new List<LexwareContact>();
            for (var page = 0; page < Options.MaxContactPages; page++)
            {
                var result = await client.ListCustomersAsync(connection, page, Options.ContactPageSize, null, null, null, ct);
                all.AddRange(result.Items.Where(c => !c.Archived));
                if (result.Last || result.Items.Count == 0)
                {
                    break;
                }
            }

            return all;
        }, cancellationToken);
    }

    public void Remember(LexwareSettingsRow row, LexwareContact contact) => Directory.Add(Stamp(row), contact);

    /// <summary>
    /// Kontakte für die verbindliche Prüfung vor dem Anlegen eines Kunden: die vollständige Kontaktliste (alle Seiten, kurz
    /// zwischengespeichert) und dazu frische, gezielte Abfragen nach Name und E-Mail-Adresse, jeweils über alle Seiten der
    /// Treffer. So fällt auch ein Kontakt auf, der erst vor wenigen Minuten in Lexware Office entstanden ist.
    /// </summary>
    public async Task<IReadOnlyList<LexwareContact>> PreciseContactsAsync(LexwareSettingsRow row, MatchParty subject, CancellationToken cancellationToken)
    {
        var connection = Connection(row);
        var (cached, _) = await ContactsAsync(row, refresh: false, cancellationToken);
        var byId = cached.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var name = subject.Name.Trim();
        var word = LexwareMatching.Normalize(name).Split(' ', StringSplitOptions.RemoveEmptyEntries).OrderByDescending(w => w.Length).FirstOrDefault();
        var queries = new List<(string? Name, string? Email)>();
        if (name.Length >= 3)
        {
            queries.Add((name, null));
        }

        if (word is { Length: >= 3 } && !string.Equals(word, name, StringComparison.OrdinalIgnoreCase))
        {
            queries.Add((word, null));
        }

        queries.AddRange(subject.Emails.Where(e => !string.IsNullOrWhiteSpace(e)).Distinct(StringComparer.OrdinalIgnoreCase).Take(3)
            .Select(e => ((string?)null, (string?)e.Trim())));
        foreach (var (byName, byEmail) in queries)
        {
            for (var page = 0; page < Options.MaxContactPages; page++)
            {
                var result = await client.ListCustomersAsync(connection, page, 100, byName, byEmail, null, cancellationToken);
                foreach (var contact in result.Items.Where(c => !c.Archived))
                {
                    byId[contact.Id] = contact;
                }

                if (result.Last || result.Items.Count == 0)
                {
                    break;
                }
            }
        }

        return byId.Values.ToList();
    }

    /// <summary>Gezielte Suche in Lexware Office: E-Mail-Adresse, Kundennummer (nur Ziffern) oder Name (mindestens drei Zeichen).</summary>
    public async Task<IReadOnlyList<LexwareContact>> SearchAsync(LexwareSettingsRow row, string query, CancellationToken cancellationToken)
    {
        var text = query.Trim();
        var connection = Connection(row);
        if (text.Contains('@', StringComparison.Ordinal))
        {
            return (await client.ListCustomersAsync(connection, 0, 25, null, text, null, cancellationToken)).Items;
        }

        if (text.All(char.IsAsciiDigit))
        {
            return (await client.ListCustomersAsync(connection, 0, 25, null, null, text, cancellationToken)).Items;
        }

        return text.Length < 3 ? [] : (await client.ListCustomersAsync(connection, 0, 25, text, null, null, cancellationToken)).Items;
    }

    public static MatchParty Party(Contact contact) => new(
        contact.Id.ToString(), contact.Name, contact.Kind == ContactKind.Company,
        contact.Emails.Select(e => e.Address).Append(contact.Email).Concat(contact.Persons.Select(p => p.Email)).Where(e => e is not null).Select(e => e!).ToList(),
        contact.Phones.Select(p => p.Number).Append(contact.Phone).Concat(contact.Persons.Select(p => p.Phone)).Where(p => p is not null).Select(p => p!).ToList(),
        contact.BillingAddress?.Street ?? contact.Street, contact.BillingAddress?.PostalCode ?? contact.PostalCode,
        contact.BillingAddress?.City ?? contact.City, contact.CustomerNumber, contact.VatId);

    public static MatchParty Party(LexwareContact contact) => new(
        contact.Id, contact.Name, contact.IsCompany, contact.Emails, contact.Phones, contact.Street, contact.PostalCode, contact.City,
        contact.CustomerNumber, contact.VatId);

    /// <summary>Kunde als neuer Lexware-Kontakt: Firma oder Person, Rechnungsanschrift, erste E-Mail-Adresse und Telefonnummer.</summary>
    public static LexwareContactDraft Draft(Contact contact)
    {
        var billing = contact.BillingAddress;
        var street = billing?.Street ?? contact.Street;
        var postal = billing?.PostalCode ?? contact.PostalCode;
        var city = billing?.City ?? contact.City;
        var mobile = contact.Phones.FirstOrDefault(p => p.Kind == ContactPhoneKind.Mobile)?.Number;
        var phone = contact.Phones.FirstOrDefault(p => p.Kind is not (ContactPhoneKind.Mobile or ContactPhoneKind.Fax))?.Number
            ?? (contact.Phone != mobile ? contact.Phone : null);
        var note = string.IsNullOrWhiteSpace(contact.CustomerNumber) ? null : $"EstrichONE Kundennummer {contact.CustomerNumber}";
        if (contact.Kind == ContactKind.Company)
        {
            return new LexwareContactDraft(true, billing?.Name ?? contact.Name, null, null, null, street, postal, city, contact.Email, phone, mobile,
                contact.VatId, contact.TaxNumber, note);
        }

        var (first, last) = SplitPersonName(contact.Name);
        return new LexwareContactDraft(false, contact.Name, contact.Salutation, first, last, street, postal, city, contact.Email, phone, mobile, null, null, note);
    }

    /// <summary>„Hans Müller“ und „Müller, Hans“ ergeben Vorname „Hans“, Nachname „Müller“; ein einzelnes Wort ist der Nachname.</summary>
    internal static (string? First, string Last) SplitPersonName(string name)
    {
        var text = name.Trim();
        var comma = text.IndexOf(',', StringComparison.Ordinal);
        if (comma > 0 && comma < text.Length - 1)
        {
            return (text[(comma + 1)..].Trim(), text[..comma].Trim());
        }

        var space = text.LastIndexOf(' ');
        return space <= 0 ? (null, text) : (text[..space].Trim(), text[(space + 1)..].Trim());
    }

    /// <summary>Verknüpfung speichern; je Seite ist höchstens eine erlaubt (eindeutiger Index als letzte Sicherung).</summary>
    public async Task<LexwareContactLink> LinkAsync(Contact contact, LexwareContact lexware, LexwareLinkOrigin origin, Guid userId, CancellationToken cancellationToken)
    {
        if (await db.Set<LexwareContactLink>().AnyAsync(l => l.ContactId == contact.Id, cancellationToken))
        {
            throw new LexwareException("ALREADY_LINKED",
                $"„{contact.Name}“ ist bereits mit einem Kontakt in Lexware Office verknüpft. Lösen Sie zuerst die bestehende Verknüpfung.");
        }

        if (await db.Set<LexwareContactLink>().Where(l => l.LexwareContactId == lexware.Id).Select(l => (Guid?)l.ContactId).FirstOrDefaultAsync(cancellationToken)
            is { } otherId)
        {
            var other = await db.Set<Contact>().AsNoTracking().Where(c => c.Id == otherId).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken);
            throw new LexwareException("ALREADY_LINKED",
                $"Der Lexware-Kontakt „{lexware.Name}“ ist bereits mit dem Kunden „{other ?? "unbekannt"}“ verknüpft. Ein Kontakt kann nur zu einem Kunden gehören.");
        }

        var link = new LexwareContactLink
        {
            ContactId = contact.Id, LexwareContactId = lexware.Id, LexwareName = Cut(lexware.Name, LexwareContactLink.MaxName),
            LexwareNumber = Cut(lexware.CustomerNumber, 50), Origin = origin, LinkedByUserId = userId, LinkedAt = clock.GetUtcNow(),
        };
        db.Add(link);
        // Mit der Verknüpfung ist ein Vermerk zur automatischen Anlage erledigt.
        if (await db.Set<LexwareContactCreation>().SingleOrDefaultAsync(x => x.ContactId == contact.Id, cancellationToken) is { } creation)
        {
            db.Remove(creation);
        }

        return link;
    }

    /// <summary>Eindeutigkeitsverletzung beim Speichern (gleichzeitige Verknüpfung) als verständliche Meldung.</summary>
    public static LexwareException? UniqueViolation(DbUpdateException exception) =>
        exception.InnerException is Npgsql.PostgresException { SqlState: "23505" } unique
        && unique.ConstraintName?.Contains("lexware", StringComparison.Ordinal) == true
            ? new LexwareException("ALREADY_LINKED",
                unique.ConstraintName.Contains("voucher", StringComparison.Ordinal)
                    ? "Dieser Beleg ist bereits einer Bauakte zugeordnet."
                    : "Dieser Eintrag ist bereits verknüpft. Ein Kunde und ein Lexware-Kontakt können nur einmal miteinander verknüpft sein.")
            : null;

    internal static string? Cut(string? value, int length) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length > length ? value.Trim()[..length] : value.Trim();

    // ---------- Belege ----------

    public DateOnly Today()
    {
        var now = clock.GetUtcNow();
        try
        {
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin")).DateTime);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return DateOnly.FromDateTime(now.UtcDateTime);
        }
    }

    /// <summary>Status für die Anzeige: offene Rechnungen nach Ablauf der Fälligkeit gelten als überfällig.</summary>
    public string DisplayStatus(string type, string status, DateOnly? dueDate) =>
        type == LexwareVoucherTypes.Invoice && status == "open" && dueDate is { } due && due < Today() ? "overdue" : status;

    public void Apply(LexwareVoucherLink link, LexwareVoucher voucher)
    {
        link.Status = Cut(voucher.Status, 20) ?? link.Status;
        link.Number = Cut(voucher.Number, 100) ?? link.Number;
        link.Title = Cut(voucher.Title, 300) ?? link.Title;
        link.ContactName = Cut(voucher.ContactName, LexwareContactLink.MaxName) ?? link.ContactName;
        link.VoucherDate = voucher.VoucherDate ?? link.VoucherDate;
        link.DueDate = voucher.DueDate ?? link.DueDate;
        link.NetAmount = voucher.NetAmount ?? link.NetAmount;
        link.GrossAmount = voucher.GrossAmount ?? link.GrossAmount;
        link.Currency = Cut(voucher.Currency, 3) ?? link.Currency;
        link.SyncedAt = clock.GetUtcNow();
        link.SyncError = null;
    }

    /// <summary>Belege eines Lexware-Kontakts (Belegliste), kurz zwischengespeichert; <paramref name="refresh"/> lädt neu.</summary>
    public Task<(IReadOnlyList<LexwareVoucher> Vouchers, DateTimeOffset LoadedAt)> ContactVouchersAsync(LexwareSettingsRow row, string lexwareContactId,
        bool refresh, CancellationToken cancellationToken)
    {
        var connection = Connection(row);
        return Directory.VouchersAsync($"{Stamp(row)}|{lexwareContactId}", clock.GetUtcNow(), Options.StatusCacheDuration, refresh,
            () => client.ListVouchersAsync(connection, lexwareContactId, null, 100, cancellationToken));
    }

    /// <summary>PDF eines Belegs aus Lexware Office (nur lesend, kurz im Arbeitsspeicher gehalten).</summary>
    public Task<LexwareDocument> VoucherDocumentAsync(LexwareSettingsRow row, string type, string lexwareId, CancellationToken cancellationToken)
    {
        var connection = Connection(row);
        return Directory.DocumentAsync($"{Stamp(row)}|{type}|{lexwareId}", clock.GetUtcNow(), Options.DocumentCacheDuration,
            () => client.GetVoucherDocumentAsync(connection, type, lexwareId, cancellationToken));
    }

    /// <summary>
    /// Kommt die Bauaktennummer in Titel, Einleitung oder Schlusstext des Belegs vor? Dafür wird der Beleg einzeln gelesen
    /// (zwischengespeichert). Nur ein Vorschlag: zugeordnet wird erst nach Bestätigung.
    /// </summary>
    public async Task<bool> MentionsAsync(LexwareSettingsRow row, LexwareVoucher voucher, string bauakteNumber, CancellationToken cancellationToken)
    {
        var connection = Connection(row);
        var text = await Directory.VoucherTextAsync($"{Stamp(row)}|{voucher.Id}", clock.GetUtcNow(), Options.ContactCacheDuration,
            async () => (await client.GetVoucherAsync(connection, voucher.Type, voucher.Id, cancellationToken)).Text ?? string.Empty);
        return text.Contains(bauakteNumber, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Status der Belege mit Lexware Office abgleichen: bei der Anzeige nur, was länger als der kurze Cache nicht geprüft
    /// wurde (stornierte Belege ändern sich nicht mehr), auf Wunsch alle. Fehler bleiben am Beleg stehen; die
    /// gespeicherten Werte werden weiter angezeigt.
    /// </summary>
    public async Task<string?> SyncAsync(LexwareSettingsRow row, IReadOnlyList<LexwareVoucherLink> links, bool force, CancellationToken cancellationToken)
    {
        if (!row.Configured || links.Count == 0)
        {
            return null;
        }

        var stale = clock.GetUtcNow() - Options.StatusCacheDuration;
        var due = links.Where(l => force || (l.Status != "voided" && (l.SyncedAt is null || l.SyncedAt < stale))).Take(20).ToList();
        if (due.Count == 0)
        {
            return null;
        }

        LexwareConnection connection;
        try
        {
            connection = Connection(row);
        }
        catch (LexwareException exception)
        {
            return exception.Message;
        }

        string? failure = null;
        foreach (var link in due)
        {
            try
            {
                Apply(link, await client.GetVoucherAsync(connection, link.VoucherType, link.LexwareId, cancellationToken));
            }
            catch (LexwareException exception) when (exception.Code == "NOT_FOUND")
            {
                link.SyncedAt = clock.GetUtcNow();
                link.SyncError = "In Lexware Office nicht mehr vorhanden.";
            }
            catch (LexwareException exception)
            {
                // Schlüssel ungültig, kein Netz, Drosselung: betrifft alle weiteren Belege genauso.
                failure = exception.Message;
                break;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return failure;
    }

}

/// <summary>
/// Vorgabe „Mit Lexware Office abgleichen“ für neue Kunden. Solange das Modul aus ist, gilt „aus“: Dann wird kein Kunde
/// unbemerkt für die Übertragung vorgemerkt, auch nicht, wenn das Modul später eingeschaltet wird.
/// </summary>
internal sealed class LexwareCustomerSyncDefault(EstrichOneDbContext db, ISystemSettings system) : ICustomerSyncDefault
{
    public async Task<bool> NewCustomersAsync(CancellationToken cancellationToken) =>
        (await system.GetAsync(cancellationToken)).Modules.IsEnabled(AppModules.Lexware) && await PreferenceAsync(cancellationToken);

    public async Task<bool> PreferenceAsync(CancellationToken cancellationToken) =>
        await db.Set<LexwareSettingsRow>().AsNoTracking().Select(s => (bool?)s.NewCustomersSync).SingleOrDefaultAsync(cancellationToken) ?? true;
}

/// <summary>
/// Verbindliche Prüfung vor dem Anlegen eines Kunden: Gibt es ihn in Lexware Office schon? Läuft mit eingeschaltetem
/// Modul und hinterlegtem Schlüssel, unabhängig vom Schalter „Mit Lexware Office abgleichen“.
/// </summary>
internal sealed class LexwareCustomerCreationCheck(LexwareService lexware, ISystemSettings system, Access.Exports.IUserPermissions permissions)
    : ICustomerCreationCheck
{
    public async Task<CustomerCreationCheck> CheckAsync(Contact draft, CancellationToken cancellationToken)
    {
        if (!(await system.GetAsync(cancellationToken)).Modules.IsEnabled(AppModules.Lexware))
        {
            return CustomerCreationCheck.Inactive;
        }

        var row = await lexware.SettingsAsync(cancellationToken);
        if (!row.Configured)
        {
            return CustomerCreationCheck.Inactive;
        }

        var canImport = await permissions.HasAsync(Access.Exports.Permissions.InvoicesAssign, cancellationToken);
        try
        {
            var candidates = await LexwareEndpoints.CheckCandidatesAsync(lexware, row, LexwareService.Party(draft), cancellationToken);
            return new CustomerCreationCheck(true, true, null, candidates, canImport);
        }
        catch (LexwareException exception)
        {
            return new CustomerCreationCheck(true, false, "Die Prüfung in Lexware Office ist derzeit nicht möglich. " + exception.Message, [], canImport);
        }
    }
}

/// <summary>Beim Löschen bzw. Anonymisieren eines Kunden endet die Verknüpfung (in Lexware Office bleibt der Kontakt unverändert).</summary>
internal sealed class LexwareCustomerReferences(EstrichOneDbContext db) : ICustomerReferenceSource
{
    public Task<IReadOnlyList<CustomerReference>> FindAsync(Guid contactId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CustomerReference>>([]);

    public async Task AnonymizeAsync(Guid contactId, string replacementName, CancellationToken cancellationToken)
    {
        if (await db.Set<LexwareContactLink>().SingleOrDefaultAsync(l => l.ContactId == contactId, cancellationToken) is { } link)
        {
            db.Remove(link);
        }

        if (await db.Set<LexwareContactCreation>().SingleOrDefaultAsync(x => x.ContactId == contactId, cancellationToken) is { } creation)
        {
            db.Remove(creation);
        }
    }
}
