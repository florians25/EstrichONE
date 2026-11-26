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

    public void Clear() => _state = null;

    public void Dispose() => _gate.Dispose();
}

/// <summary>Fachlogik der Lexware-Office-Anbindung: Einstellungen, Kontaktverknüpfung mit Dublettenprüfung, Rechnungsentwürfe und Belegstatus.</summary>
public sealed class LexwareService(
    EstrichOneDbContext db, LexwareClient client, IDataProtectionProvider protection, IOptions<LexwareOptions> options, TimeProvider clock,
    IServiceProvider services)
{
    public const string ProtectorPurpose = "EstrichOne.Lexware.ApiKey.v1";

    /// <summary>Wo der Schlüssel in Lexware Office erzeugt wird.</summary>
    public const string KeyHelpUrl = "https://app.lexware.de/addons/public-api";

    private LexwareDirectory Directory => services.GetRequiredService<LexwareDirectory>();

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

    /// <summary>Entwürfe öffnen im Bearbeitungsmodus, alle anderen Rechnungen in der Ansicht.</summary>
    public string InvoiceUrl(LexwareSettingsRow row, string lexwareId, string status) =>
        $"{AppBaseUrl(row)}/permalink/invoices/{(status == "draft" ? "edit" : "view")}/{Uri.EscapeDataString(lexwareId)}";

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
        return link;
    }

    /// <summary>Eindeutigkeitsverletzung beim Speichern (gleichzeitige Verknüpfung) als verständliche Meldung.</summary>
    public static LexwareException? UniqueViolation(DbUpdateException exception) =>
        exception.InnerException is Npgsql.PostgresException { SqlState: "23505" } unique
        && unique.ConstraintName?.Contains("lexware", StringComparison.Ordinal) == true
            ? new LexwareException("ALREADY_LINKED",
                unique.ConstraintName.Contains("voucher", StringComparison.Ordinal)
                    ? "Diese Rechnung ist bereits einer Bauakte zugeordnet."
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
    public string DisplayStatus(LexwareVoucherLink link) =>
        link.Status == "open" && link.DueDate is { } due && due < Today() ? "overdue" : link.Status;

    public void Apply(LexwareVoucherLink link, LexwareInvoice invoice)
    {
        link.Status = Cut(invoice.Status, 20) ?? link.Status;
        link.Number = Cut(invoice.Number, 100) ?? link.Number;
        link.Title = Cut(invoice.Title, 300) ?? link.Title;
        link.VoucherDate = invoice.VoucherDate ?? link.VoucherDate;
        link.DueDate = invoice.DueDate ?? link.DueDate;
        link.NetAmount = invoice.NetAmount ?? link.NetAmount;
        link.GrossAmount = invoice.GrossAmount ?? link.GrossAmount;
        link.Currency = Cut(invoice.Currency, 3) ?? link.Currency;
        link.SyncedAt = clock.GetUtcNow();
        link.SyncError = null;
    }

    /// <summary>
    /// Status der Belege mit Lexware Office abgleichen: bei der Anzeige nur, was länger als der kurze Cache nicht geprüft
    /// wurde (stornierte Rechnungen ändern sich nicht mehr), auf Wunsch alle. Fehler bleiben am Beleg stehen; die
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
                Apply(link, await client.GetInvoiceAsync(connection, link.LexwareId, cancellationToken));
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

    /// <summary>Vorschlag für den Rechnungsentwurf aus den Kennwerten der Bauakte.</summary>
    public async Task<(string Introduction, IReadOnlyList<LexwareLineDraft> Lines)> ProposalAsync(Bauakte bauakte, CancellationToken cancellationToken)
    {
        var project = bauakte.Project;
        var lists = await services.GetRequiredService<IValueLists>().GetAllAsync(cancellationToken);
        string? Label(string list, string? key) =>
            key is null ? null : lists.TryGetValue(list, out var entries) ? entries.FirstOrDefault(e => e.Key == key)?.Label ?? key : key;

        var site = string.Join(", ", new[] { bauakte.Street, string.Join(' ', new[] { bauakte.PostalCode, bauakte.City }.Where(p => !string.IsNullOrWhiteSpace(p))) }
            .Where(p => !string.IsNullOrWhiteSpace(p)));
        var introduction = new StringBuilder("wir berechnen Ihnen unsere Leistungen für folgendes Bauvorhaben:\n");
        introduction.Append("Bauvorhaben: ").Append(bauakte.Title).Append('\n');
        if (bauakte.Number is not null)
        {
            introduction.Append("Bauakte: ").Append(bauakte.Number).Append('\n');
        }

        if (site.Length > 0)
        {
            introduction.Append("Leistungsort: ").Append(site).Append('\n');
        }

        var screed = Label(ValueLists.ScreedType, project.ScreedType);
        var name = screed ?? "Estricharbeiten";
        if (project.StrengthClass is not null && !name.Contains(project.StrengthClass, StringComparison.OrdinalIgnoreCase))
        {
            name += " " + project.StrengthClass;
        }

        var facts = new List<string>();
        if (project.NominalThicknessMm is { } thickness)
        {
            facts.Add($"Nenndicke {thickness.ToString(CultureInfo.InvariantCulture)} mm");
        }

        if (Label(ValueLists.Construction, project.Construction) is { } construction)
        {
            facts.Add($"Bauart {construction}");
        }

        if (project.UnderfloorHeating == true)
        {
            facts.Add("als Heizestrich");
        }

        facts.Add("liefern und einbauen");
        var lines = new List<LexwareLineDraft>
        {
            new("custom", name, string.Join(", ", facts), project.TotalArea ?? 1m, project.TotalArea is null ? "Stück" : "m²", 0m, 19m),
        };
        if (project.Insulation is not null)
        {
            var insulation = project.InsulationThicknessMm is { } mm
                ? $"{project.Insulation}, {mm.ToString(CultureInfo.InvariantCulture)} mm" : project.Insulation;
            lines.Add(new LexwareLineDraft("custom", "Dämmung", insulation + ", liefern und verlegen", project.TotalArea ?? 1m,
                project.TotalArea is null ? "Stück" : "m²", 0m, 19m));
        }

        return (introduction.ToString().TrimEnd(), lines);
    }

    public static string PaymentTermLabel(LexwareSettingsRow row, int days) =>
        !string.IsNullOrWhiteSpace(row.PaymentTermLabel) && days == row.PaymentTermDays
            ? row.PaymentTermLabel
            : days == 0 ? "Zahlbar sofort ohne Abzug." : $"Zahlbar innerhalb von {days.ToString(CultureInfo.InvariantCulture)} Tagen ohne Abzug.";
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
    }
}
