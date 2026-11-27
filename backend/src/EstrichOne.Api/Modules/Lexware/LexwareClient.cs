using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace EstrichOne.Api.Modules.Lexware;

/// <summary>Adressen und Verhalten der Lexware-Office-Anbindung (<c>EstrichOne:Lexware</c>; Tests zeigen auf einen nachgebildeten Server).</summary>
public sealed class LexwareOptions
{
    public const string SectionName = "EstrichOne:Lexware";
    public const string DefaultBaseUrl = "https://api.lexware.io";

    /// <summary>Hosts, die als Basis-URL erlaubt sind (Schutz davor, den Schlüssel an fremde Server zu senden).</summary>
    public string[] AllowedHosts { get; set; } = ["api.lexware.io", "api.lexoffice.io"];

    /// <summary>Nur für Tests und lokale Nachbildungen: jede Basis-URL zulassen, auch http.</summary>
    public bool AllowAnyBaseUrl { get; set; }

    /// <summary>Adresse der Lexware-Oberfläche für „In Lexware öffnen“; leer = aus der Basis-URL abgeleitet.</summary>
    public string? AppBaseUrl { get; set; }

    /// <summary>Mindestabstand zwischen zwei Aufrufen (Lexware erlaubt 2 Anfragen je Sekunde).</summary>
    public TimeSpan MinRequestInterval { get; set; } = TimeSpan.FromMilliseconds(550);

    /// <summary>Versuche je Aufruf bei 429 bzw. bei lesenden Aufrufen auch bei 502/503/504 und Netzwerkfehlern.</summary>
    public int MaxAttempts { get; set; } = 4;

    /// <summary>Erste Wartezeit nach 429; verdoppelt sich je Versuch.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Längste Wartezeit zwischen zwei Versuchen (auch wenn Retry-After mehr verlangt).</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(8);

    /// <summary>Höchstdauer eines einzelnen Versuchs.</summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Wie lange die Kontaktliste aus Lexware zwischengespeichert wird.</summary>
    public TimeSpan ContactCacheDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Wie lange der Status eines Belegs als aktuell gilt, bevor die Anzeige ihn neu abruft.</summary>
    public TimeSpan StatusCacheDuration { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Einträge je Seite beim Abruf der Kontaktliste.</summary>
    public int ContactPageSize { get; set; } = 250;

    /// <summary>Obergrenze der Seiten beim Abruf der Kontaktliste.</summary>
    public int MaxContactPages { get; set; } = 80;

    /// <summary>Abstand, in dem der Hintergrunddienst nach in EstrichONE geänderten, verknüpften Kunden sieht.</summary>
    public TimeSpan ChangePollInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Abstand des vollständigen Änderungsabgleichs (Kontaktliste aus Lexware Office neu lesen und vergleichen).</summary>
    public TimeSpan ChangeSyncInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Pause des Hintergrunddienstes, wenn Lexware Office nicht erreichbar ist oder den Schlüssel ablehnt.</summary>
    public TimeSpan ChangeSyncPause { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Hintergrunddienst des Änderungsabgleichs starten (Tests schalten ihn ab und stoßen die Läufe selbst an).</summary>
    public bool BackgroundChangeSync { get; set; } = true;

    /// <summary>Versuche beim Ändern eines Kontakts, wenn Lexware Office einen Versionskonflikt (409) meldet.</summary>
    public int UpdateAttempts { get; set; } = 3;

    /// <summary>Weg zum Beleg-PDF: <see cref="LexwareDocumentRoute.Auto"/> versucht zuerst <c>/file</c>, dann <c>/document</c> mit <c>/v1/files</c>.</summary>
    public LexwareDocumentRoute VoucherDocumentRoute { get; set; } = LexwareDocumentRoute.Auto;

    /// <summary>Wie lange ein abgerufenes Beleg-PDF im Arbeitsspeicher bleibt (Anzeigen und danach Herunterladen ohne zweiten Abruf).</summary>
    public TimeSpan DocumentCacheDuration { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Größtes Beleg-PDF, das durchgereicht wird.</summary>
    public int MaxDocumentBytes { get; set; } = 30 * 1024 * 1024;
}

/// <summary>Weg, auf dem das PDF eines Belegs aus Lexware Office geholt wird.</summary>
public enum LexwareDocumentRoute
{
    /// <summary>Zuerst <c>GET /v1/{belegart}/{id}/file</c>; kennt Lexware den Pfad nicht, der ältere Weg.</summary>
    Auto,

    /// <summary>Nur <c>GET /v1/{belegart}/{id}/file</c> mit <c>Accept: application/pdf</c>.</summary>
    File,

    /// <summary>Nur der ältere Weg: <c>GET /v1/{belegart}/{id}/document</c> liefert <c>documentFileId</c>, danach <c>GET /v1/files/{documentFileId}</c>.</summary>
    Document,
}

/// <summary>Fehler bei Lexware Office mit verständlicher Ursache; <see cref="Code"/> geht als Problem-Code an die Oberfläche.</summary>
public sealed class LexwareException(string code, string message, int? status = null) : Exception(message)
{
    public string Code { get; } = code;
    public int? Status { get; } = status;
}

/// <summary>Zugangsdaten eines Aufrufs. Der Schlüssel erscheint nie in Protokollen oder Meldungen.</summary>
public sealed record LexwareConnection(Uri BaseUri, string ApiKey);

public sealed record LexwareProfile(string? OrganizationId, string? CompanyName);

/// <summary>Kontakt aus Lexware Office, auf die Felder reduziert, die EstrichONE verwendet.</summary>
public sealed record LexwareContact(
    string Id, bool IsCompany, string Name, string? Salutation, string? FirstName, string? LastName, string? CustomerNumber, bool IsCustomer,
    IReadOnlyList<string> Emails, IReadOnlyList<string> Phones, string? Street, string? PostalCode, string? City, string? VatId, string? TaxNumber,
    bool Archived, int? Version = null, DateTimeOffset? UpdatedAt = null, bool HasBillingAddress = false);

/// <summary>
/// Änderung eines abgeglichenen Felds am Lexware-Kontakt (<see cref="LexwareSyncFields"/>). <paramref name="Previous"/> ist
/// der bisherige Wert in Lexware Office: bei E-Mail-Adresse und Telefonnummer wird genau dieser Eintrag ersetzt.
/// </summary>
public sealed record LexwareContactChange(string Field, string? Value, string? Previous = null);

/// <summary>Ergebnis von <see cref="LexwareClient.UpdateContactAsync"/>: Stand des Kontakts danach und ob geschrieben wurde.</summary>
public sealed record LexwareContactUpdate(LexwareContact Contact, bool Written, IReadOnlyList<LexwareContactChange> Changes);

/// <summary>PDF eines Belegs.</summary>
public sealed record LexwareDocument(byte[] Content, string ContentType);

/// <summary>Neuer Kontakt (Rolle Kunde).</summary>
public sealed record LexwareContactDraft(
    bool IsCompany, string Name, string? Salutation, string? FirstName, string? LastName, string? Street, string? PostalCode, string? City,
    string? Email, string? Phone, string? MobilePhone, string? VatId, string? TaxNumber, string? Note);

public sealed record LexwarePage<T>(IReadOnlyList<T> Items, int Page, int TotalPages, int TotalElements, bool Last);

/// <summary>Beleg aus Lexware Office (nur gelesen).</summary>
/// <param name="Type"><c>invoice</c>, <c>quotation</c>, <c>creditnote</c> oder <c>orderconfirmation</c>.</param>
/// <param name="DueDate">Fälligkeit der Rechnung bzw. „gültig bis“ des Angebots.</param>
/// <param name="Text">Titel, Einleitung und Schlusstext (nur beim Einzelabruf), für den Zuordnungsvorschlag über die Bauaktennummer.</param>
public sealed record LexwareVoucher(
    string Id, string Type, string Status, string? Number, string? Title, DateOnly? VoucherDate, DateOnly? DueDate, decimal? NetAmount,
    decimal? GrossAmount, string Currency, string? ContactId, string? ContactName, string? Text = null);

/// <summary>Belegarten, die EstrichONE anzeigt, mit ihrem Pfad in der API und in den Links der Lexware-Oberfläche.</summary>
public static class LexwareVoucherTypes
{
    public const string Invoice = "invoice";
    public const string Quotation = "quotation";
    public const string CreditNote = "creditnote";
    public const string OrderConfirmation = "orderconfirmation";

    public static IReadOnlyList<string> All { get; } = [Invoice, Quotation, CreditNote, OrderConfirmation];

    /// <summary>Pfadteil der API und der Permalinks; <c>null</c> für unbekannte Arten.</summary>
    public static string? Segment(string? type) => type switch
    {
        Invoice => "invoices",
        Quotation => "quotations",
        CreditNote => "credit-notes",
        OrderConfirmation => "order-confirmations",
        _ => null,
    };
}

/// <summary>
/// Gekapselter Zugang zur Public API von Lexware Office (früher lexoffice): Bearer-Schlüssel, höchstens zwei Anfragen je
/// Sekunde, Wiederholung bei 429 mit wachsender Wartezeit, Zeitlimit je Versuch und verständliche Fehlermeldungen.
/// Geschrieben wird ausschließlich an Kontakten: beim Anlegen (<c>POST /v1/contacts</c>) und beim Abgleich geänderter
/// Kundendaten (<c>PUT /v1/contacts/{id}</c>). Belege (Rechnungen, Angebote, Gutschriften, Auftragsbestätigungen) werden
/// nur gelesen, auch als PDF: Es gibt keine Methode, die einen Beleg anlegt oder ändert, und <see cref="WriteAllowed"/>
/// weist jeden anderen schreibenden Aufruf schon vor dem Senden ab.
/// Alle Feldnamen der API stehen nur in dieser Datei (docs/lexware-office.md), damit Anpassungen an einer Stelle bleiben.
/// </summary>
public sealed class LexwareClient(IHttpClientFactory http, IOptions<LexwareOptions> options, ILogger<LexwareClient> logger) : IDisposable
{
    public const string HttpClientName = "lexware-office";
    private const string Service = "Lexware Office";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _lastRequest;

    private LexwareOptions Options => options.Value;

    public void Dispose() => _gate.Dispose();

    // ---------- Aufrufe ----------

    public async Task<LexwareProfile> ProfileAsync(LexwareConnection connection, CancellationToken cancellationToken)
    {
        var json = await SendAsync(connection, HttpMethod.Get, "/v1/profile", null, cancellationToken);
        return new LexwareProfile(Str(json?["organizationId"]), Str(json?["companyName"]));
    }

    /// <summary>Kontakte mit Rolle Kunde, seitenweise. Filter laut API: <c>name</c> (mindestens drei Zeichen), <c>email</c>, <c>number</c>.</summary>
    public async Task<LexwarePage<LexwareContact>> ListCustomersAsync(LexwareConnection connection, int page, int size, string? name, string? email,
        string? number, CancellationToken cancellationToken)
    {
        var query = new StringBuilder($"/v1/contacts?customer=true&page={page.ToString(CultureInfo.InvariantCulture)}&size={size.ToString(CultureInfo.InvariantCulture)}");
        Append(query, "name", name);
        Append(query, "email", email);
        Append(query, "number", number);
        var json = await SendAsync(connection, HttpMethod.Get, query.ToString(), null, cancellationToken);
        var items = (json?["content"] as JsonArray ?? []).OfType<JsonObject>().Select(ReadContact).Where(c => c is not null).Select(c => c!).ToList();
        var totalPages = Int(json?["totalPages"]) ?? 1;
        var last = json?["last"] is JsonValue flag && flag.TryGetValue<bool>(out var isLast) ? isLast : page + 1 >= totalPages;
        return new LexwarePage<LexwareContact>(items, page, totalPages, Int(json?["totalElements"]) ?? items.Count, last);
    }

    public async Task<LexwareContact> GetContactAsync(LexwareConnection connection, string id, CancellationToken cancellationToken)
    {
        var json = await SendAsync(connection, HttpMethod.Get, $"/v1/contacts/{Uri.EscapeDataString(id)}", null, cancellationToken);
        return (json as JsonObject is { } node ? ReadContact(node) : null)
            ?? throw new LexwareException("UNEXPECTED_RESPONSE", $"Die Antwort von {Service} konnte nicht verarbeitet werden.");
    }

    /// <summary>Legt einen Kontakt mit der Rolle Kunde an und liefert seine ID.</summary>
    public async Task<string> CreateContactAsync(LexwareConnection connection, LexwareContactDraft draft, CancellationToken cancellationToken)
    {
        var body = new JsonObject { ["version"] = 0, ["roles"] = new JsonObject { ["customer"] = new JsonObject() } };
        if (draft.IsCompany)
        {
            var company = new JsonObject { ["name"] = draft.Name };
            Put(company, "vatRegistrationId", draft.VatId);
            Put(company, "taxNumber", draft.TaxNumber);
            body["company"] = company;
        }
        else
        {
            var person = new JsonObject { ["lastName"] = draft.LastName ?? draft.Name };
            Put(person, "salutation", draft.Salutation);
            Put(person, "firstName", draft.FirstName);
            body["person"] = person;
        }

        if (draft.Street is not null || draft.PostalCode is not null || draft.City is not null)
        {
            var address = new JsonObject { ["countryCode"] = "DE" };
            Put(address, "street", draft.Street);
            Put(address, "zip", draft.PostalCode);
            Put(address, "city", draft.City);
            body["addresses"] = new JsonObject { ["billing"] = new JsonArray(address) };
        }

        if (draft.Email is not null)
        {
            body["emailAddresses"] = new JsonObject { ["business"] = new JsonArray(draft.Email) };
        }

        if (draft.Phone is not null || draft.MobilePhone is not null)
        {
            var phones = new JsonObject();
            if (draft.Phone is not null)
            {
                phones["business"] = new JsonArray(draft.Phone);
            }

            if (draft.MobilePhone is not null)
            {
                phones["mobile"] = new JsonArray(draft.MobilePhone);
            }

            body["phoneNumbers"] = phones;
        }

        Put(body, "note", draft.Note);
        var json = await SendAsync(connection, HttpMethod.Post, "/v1/contacts", body, cancellationToken);
        return Str(json?["id"]) ?? throw new LexwareException("UNEXPECTED_RESPONSE", $"{Service} hat keine Kontakt-ID geliefert.");
    }

    /// <summary>
    /// Ändert abgeglichene Felder eines Kontakts (<c>PUT /v1/contacts/{id}</c>). Der Kontakt wird dafür frisch gelesen;
    /// <paramref name="decide"/> bestimmt aus diesem Stand die Änderungen (leer = nichts schreiben). Zurückgeschrieben wird
    /// der gelesene Kontakt mit seiner <c>version</c> (optimistische Sperre), geändert nur in den genannten Feldern. Meldet
    /// Lexware Office einen Versionskonflikt (409), wird neu gelesen und neu entschieden.
    /// </summary>
    public async Task<LexwareContactUpdate> UpdateContactAsync(LexwareConnection connection, string id,
        Func<LexwareContact, IReadOnlyList<LexwareContactChange>> decide, CancellationToken cancellationToken)
    {
        var path = $"/v1/contacts/{Uri.EscapeDataString(id)}";
        var attempts = Math.Max(1, Options.UpdateAttempts);
        for (var attempt = 1; ; attempt++)
        {
            if (await SendAsync(connection, HttpMethod.Get, path, null, cancellationToken) is not JsonObject raw || ReadContact(raw) is not { } current)
            {
                throw new LexwareException("UNEXPECTED_RESPONSE", $"Die Antwort von {Service} konnte nicht verarbeitet werden.");
            }

            var changes = decide(current);
            if (changes.Count == 0)
            {
                return new LexwareContactUpdate(current, false, changes);
            }

            if (current.Version is null)
            {
                throw new LexwareException("UNEXPECTED_RESPONSE", $"{Service} hat keine Version des Kontakts geliefert; ohne sie wird nichts geändert.");
            }

            var body = ApplyChanges(raw, changes);
            JsonNode? answer;
            try
            {
                answer = await SendAsync(connection, HttpMethod.Put, path, body, cancellationToken);
            }
            catch (LexwareException exception) when (exception.Code == "CONFLICT" && attempt < attempts)
            {
                continue; // inzwischen in Lexware Office geändert: neu lesen, neu entscheiden
            }

            // Die Antwort nennt nur ID, Zeitpunkte und die neue Version.
            body["id"] = current.Id;
            body["version"] = Int(answer?["version"]) ?? current.Version.Value + 1;
            if (Str(answer?["updatedDate"]) is { } updated)
            {
                body["updatedDate"] = updated;
            }

            return new LexwareContactUpdate(ReadContact(body) ?? current, true, changes);
        }
    }

    /// <summary>
    /// PDF eines Belegs (nur lesend). Der Weg ist einstellbar (<see cref="LexwareOptions.VoucherDocumentRoute"/>):
    /// <c>GET /v1/{belegart}/{id}/file</c> oder der ältere Weg über <c>/document</c> und <c>/v1/files/{documentFileId}</c>.
    /// Für Entwürfe stellt Lexware Office kein PDF bereit (<c>NO_DOCUMENT</c>).
    /// </summary>
    public async Task<LexwareDocument> GetVoucherDocumentAsync(LexwareConnection connection, string type, string id, CancellationToken cancellationToken)
    {
        var segment = LexwareVoucherTypes.Segment(type)
            ?? throw new LexwareException("UNKNOWN_VOUCHER_TYPE", "Diese Belegart zeigt EstrichONE nicht an.");
        var resource = $"/v1/{segment}/{Uri.EscapeDataString(id)}";
        try
        {
            if (Options.VoucherDocumentRoute != LexwareDocumentRoute.Document)
            {
                try
                {
                    return Pdf(await SendRawAsync(connection, HttpMethod.Get, resource + "/file", null, "application/pdf", cancellationToken));
                }
                catch (LexwareException exception) when (Options.VoucherDocumentRoute == LexwareDocumentRoute.Auto && exception.Status is 404 or 405 or 501)
                {
                    // Der Pfad ist nicht bekannt oder der Beleg fehlt: Der ältere Weg klärt das.
                }
            }

            var document = await SendAsync(connection, HttpMethod.Get, resource + "/document", null, cancellationToken);
            var fileId = Str(document?["documentFileId"])
                ?? throw new LexwareException("NO_DOCUMENT", "Lexware Office hat für diesen Beleg kein PDF bereitgestellt.");
            return Pdf(await SendRawAsync(connection, HttpMethod.Get, $"/v1/files/{Uri.EscapeDataString(fileId)}", null, "application/pdf", cancellationToken));
        }
        catch (LexwareException exception) when (exception.Status == 406)
        {
            throw new LexwareException("NO_DOCUMENT", "Für Entwürfe stellt Lexware Office kein PDF bereit.", 406);
        }
    }

    private LexwareDocument Pdf((byte[] Content, string? ContentType) response)
    {
        var content = response.Content;
        if (content.Length > Options.MaxDocumentBytes)
        {
            throw new LexwareException("DOCUMENT_TOO_LARGE", "Das PDF dieses Belegs ist zu groß für die Anzeige in EstrichONE. Bitte öffnen Sie den Beleg in Lexware Office.");
        }

        if (content.Length < 5 || content[0] != (byte)'%' || content[1] != (byte)'P' || content[2] != (byte)'D' || content[3] != (byte)'F')
        {
            throw new LexwareException("NO_DOCUMENT", "Lexware Office hat für diesen Beleg kein PDF geliefert.");
        }

        return new LexwareDocument(content, "application/pdf");
    }

    /// <summary>Beleg lesen (Status, Nummer, Beträge, Texte).</summary>
    public async Task<LexwareVoucher> GetVoucherAsync(LexwareConnection connection, string type, string id, CancellationToken cancellationToken)
    {
        var segment = LexwareVoucherTypes.Segment(type)
            ?? throw new LexwareException("UNKNOWN_VOUCHER_TYPE", "Diese Belegart zeigt EstrichONE nicht an.");
        var json = await SendAsync(connection, HttpMethod.Get, $"/v1/{segment}/{Uri.EscapeDataString(id)}", null, cancellationToken);
        var total = json?["totalPrice"];
        var address = json?["address"];
        var text = string.Join('\n', new[] { Str(json?["title"]), Str(json?["introduction"]), Str(json?["remark"]) }.Where(p => p is not null));
        return new LexwareVoucher(Str(json?["id"]) ?? id, type, Str(json?["voucherStatus"])?.ToLowerInvariant() ?? "draft", Str(json?["voucherNumber"]),
            Str(json?["title"]), Date(json?["voucherDate"]), Date(json?["dueDate"]) ?? Date(json?["expirationDate"]) ?? DueDate(json),
            Decimal(total?["totalNetAmount"]), Decimal(total?["totalGrossAmount"]), Str(total?["currency"]) ?? "EUR", Str(address?["contactId"]),
            Str(address?["name"]), text);
    }

    /// <summary>
    /// Belegliste (Rechnungen, Angebote, Gutschriften, Auftragsbestätigungen), neueste zuerst: alle Belege eines Kontakts
    /// (<paramref name="contactId"/>) oder Suche über die Belegnummer (<paramref name="number"/>).
    /// </summary>
    public async Task<IReadOnlyList<LexwareVoucher>> ListVouchersAsync(LexwareConnection connection, string? contactId, string? number, int size,
        CancellationToken cancellationToken)
    {
        var query = new StringBuilder("/v1/voucherlist?voucherType=").Append(string.Join(',', LexwareVoucherTypes.All))
            .Append("&voucherStatus=any&sort=voucherDate,DESC&size=").Append(size.ToString(CultureInfo.InvariantCulture));
        Append(query, "contactId", contactId);
        Append(query, "voucherNumber", number);
        var json = await SendAsync(connection, HttpMethod.Get, query.ToString(), null, cancellationToken);
        return (json?["content"] as JsonArray ?? []).OfType<JsonObject>()
            .Where(v => Str(v["id"]) is not null && LexwareVoucherTypes.Segment(Str(v["voucherType"])?.ToLowerInvariant()) is not null).Select(v =>
            {
                // Die Belegliste kennt zusätzlich „overdue“; gespeichert wird der Status des Belegs selbst.
                var status = Str(v["voucherStatus"])?.ToLowerInvariant() ?? "open";
                return new LexwareVoucher(Str(v["id"])!, Str(v["voucherType"])!.ToLowerInvariant(), status == "overdue" ? "open" : status,
                    Str(v["voucherNumber"]), null, Date(v["voucherDate"]), Date(v["dueDate"]), null, Decimal(v["totalAmount"]), Str(v["currency"]) ?? "EUR",
                    Str(v["contactId"]), Str(v["contactName"]));
            }).ToList();
    }

    // ---------- Abbildung ----------

    internal static LexwareContact? ReadContact(JsonObject node)
    {
        if (Str(node["id"]) is not { } id)
        {
            return null;
        }

        var company = node["company"] as JsonObject;
        var person = node["person"] as JsonObject;
        var first = Str(person?["firstName"]);
        var last = Str(person?["lastName"]);
        var name = company is not null ? Str(company["name"]) : string.Join(' ', new[] { first, last }.Where(p => p is not null));
        var customer = node["roles"]?["customer"] as JsonObject;
        var ownBilling = (node["addresses"]?["billing"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault();
        var billing = ownBilling ?? (node["addresses"]?["shipping"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault();
        return new LexwareContact(id, company is not null, string.IsNullOrWhiteSpace(name) ? "(ohne Namen)" : name!, Str(person?["salutation"]), first, last,
            Str(customer?["number"]), customer is not null, Flatten(node["emailAddresses"]), Flatten(node["phoneNumbers"]), Str(billing?["street"]),
            Str(billing?["zip"]), Str(billing?["city"]), Str(company?["vatRegistrationId"]), Str(company?["taxNumber"]),
            node["archived"] is JsonValue archived && archived.TryGetValue<bool>(out var isArchived) && isArchived, Int(node["version"]),
            Str(node["updatedDate"]) is { } updated && DateTimeOffset.TryParse(updated, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? at.ToUniversalTime() : null,
            ownBilling is not null);
    }

    /// <summary>Felder der Antwort, die Lexware Office selbst führt und die beim Zurückschreiben nicht mitgehen.</summary>
    private static readonly string[] ReadOnlyContactFields = ["id", "organizationId", "createdDate", "updatedDate", "archived"];

    /// <summary>
    /// Kontakt für <c>PUT /v1/contacts/{id}</c>: der gelesene Kontakt mit seiner <c>version</c>, geändert nur in den
    /// abgeglichenen Feldern. Alle anderen Angaben (Rollen mit Kundennummer, weitere Anschriften, Ansprechpartner, Notiz,
    /// weitere E-Mail-Adressen und Telefonnummern) gehen unverändert zurück.
    /// </summary>
    internal static JsonObject ApplyChanges(JsonObject contact, IEnumerable<LexwareContactChange> changes)
    {
        var body = (JsonObject)contact.DeepClone();
        foreach (var field in ReadOnlyContactFields)
        {
            body.Remove(field);
        }

        foreach (var change in changes)
        {
            var value = string.IsNullOrWhiteSpace(change.Value) ? null : change.Value.Trim();
            switch (change.Field)
            {
                case LexwareSyncFields.Name when value is not null && body["company"] is JsonObject company:
                    company["name"] = value;
                    break;
                case LexwareSyncFields.Name when value is not null && body["person"] is JsonObject person:
                    var (first, last) = LexwareService.SplitPersonName(value);
                    Set(person, "firstName", first);
                    person["lastName"] = last;
                    break;
                case LexwareSyncFields.Salutation when body["person"] is JsonObject person:
                    Set(person, "salutation", value);
                    break;
                case LexwareSyncFields.VatId when body["company"] is JsonObject company:
                    Set(company, "vatRegistrationId", value);
                    break;
                case LexwareSyncFields.Street:
                    SetBilling(body, "street", value);
                    break;
                case LexwareSyncFields.PostalCode:
                    SetBilling(body, "zip", value);
                    break;
                case LexwareSyncFields.City:
                    SetBilling(body, "city", value);
                    break;
                case LexwareSyncFields.Email:
                    Replace(body, "emailAddresses", change.Previous, value);
                    break;
                case LexwareSyncFields.Phone:
                    Replace(body, "phoneNumbers", change.Previous, value);
                    break;
            }
        }

        return body;
    }

    private static void Set(JsonObject target, string key, string? value)
    {
        if (value is null)
        {
            target.Remove(key);
        }
        else
        {
            target[key] = value;
        }
    }

    /// <summary>Rechnungsanschrift ist die erste unter <c>addresses.billing</c>; fehlt sie, wird sie mit Land Deutschland angelegt.</summary>
    private static void SetBilling(JsonObject contact, string key, string? value)
    {
        if (contact["addresses"] is not JsonObject addresses)
        {
            contact["addresses"] = addresses = [];
        }

        if (addresses["billing"] is not JsonArray list)
        {
            addresses["billing"] = list = [];
        }

        if (list.OfType<JsonObject>().FirstOrDefault() is not { } billing)
        {
            if (value is null)
            {
                return;
            }

            list.Insert(0, billing = new JsonObject { ["countryCode"] = "DE" });
        }

        Set(billing, key, value);
    }

    /// <summary>
    /// Eintrag unter <c>emailAddresses</c> bzw. <c>phoneNumbers</c> ersetzen: genau der bisherige Wert, in seiner Art
    /// (business, office, mobile, private, other). Gibt es ihn nicht, kommt der neue Wert unter <c>business</c> dazu.
    /// </summary>
    private static void Replace(JsonObject contact, string groupsKey, string? previous, string? value)
    {
        if (contact[groupsKey] is not JsonObject groups)
        {
            contact[groupsKey] = groups = [];
        }

        if (previous is not null)
        {
            foreach (var (kind, node) in groups.ToList())
            {
                if (kind == "fax" || node is not JsonArray list)
                {
                    continue;
                }

                for (var index = 0; index < list.Count; index++)
                {
                    if (!string.Equals(Str(list[index]), previous.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (value is null)
                    {
                        list.RemoveAt(index);
                    }
                    else
                    {
                        list[index] = value;
                    }

                    return;
                }
            }
        }

        if (value is null)
        {
            return;
        }

        if (groups["business"] is not JsonArray business)
        {
            groups["business"] = business = [];
        }

        business.Insert(0, value);
    }

    /// <summary>E-Mail-Adressen bzw. Telefonnummern stehen je Art (business, office, mobile, private, fax, other) in Listen.</summary>
    private static List<string> Flatten(JsonNode? groups) =>
        groups is JsonObject map
            ? map.Where(g => g.Key != "fax").SelectMany(g => g.Value as JsonArray ?? []).Select(Str).Where(v => v is not null).Select(v => v!)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : [];

    private static DateOnly? DueDate(JsonNode? invoice) =>
        Date(invoice?["voucherDate"]) is { } date && Int(invoice?["paymentConditions"]?["paymentTermDuration"]) is { } days ? date.AddDays(days) : null;

    private static void Append(StringBuilder query, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            query.Append('&').Append(key).Append('=').Append(Uri.EscapeDataString(value.Trim()));
        }
    }

    private static void Put(JsonObject target, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            target[key] = value.Trim();
        }
    }

    private static string? Str(JsonNode? node) => node switch
    {
        JsonValue value when value.TryGetValue<string>(out var text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim(),
        JsonValue value when value.GetValueKind() == JsonValueKind.Number => value.ToJsonString(),
        _ => null,
    };

    private static int? Int(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.Number && value.TryGetValue<int>(out var number) ? number : null;

    private static decimal? Decimal(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.Number && value.TryGetValue<decimal>(out var number) ? number : null;

    /// <summary>Lexware liefert Zeitpunkte mit Zeitzone (<c>2023-02-22T00:00:00.000+01:00</c>); maßgeblich ist das Datum in dieser Zone.</summary>
    private static DateOnly? Date(JsonNode? node) =>
        Str(node) is { } text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
            ? DateOnly.FromDateTime(value.DateTime) : null;

    /// <summary>
    /// Schreibende Aufrufe sind nur an Kontakten erlaubt: Anlegen (<c>POST /v1/contacts</c>) und Ändern eines einzelnen
    /// Kontakts (<c>PUT /v1/contacts/{id}</c>). Alles andere, insbesondere jeder Beleg, ist lesend: Rechnungen und Angebote
    /// entstehen ausschließlich in Lexware Office. Gelöscht oder archiviert wird dort nie.
    /// </summary>
    internal static bool WriteAllowed(HttpMethod method, string path) =>
        method == HttpMethod.Get || (method == HttpMethod.Post && path == "/v1/contacts")
        || (method == HttpMethod.Put && path.StartsWith(ContactPath, StringComparison.Ordinal) && path.Length > ContactPath.Length
            && path.IndexOf('/', ContactPath.Length) < 0);

    private const string ContactPath = "/v1/contacts/";

    // ---------- Transport ----------

    private async Task<JsonNode?> SendAsync(LexwareConnection connection, HttpMethod method, string pathAndQuery, JsonObject? body,
        CancellationToken cancellationToken) =>
        Parse(Encoding.UTF8.GetString((await SendRawAsync(connection, method, pathAndQuery, body, "application/json", cancellationToken)).Content));

    private async Task<(byte[] Content, string? ContentType)> SendRawAsync(LexwareConnection connection, HttpMethod method, string pathAndQuery,
        JsonObject? body, string accept, CancellationToken cancellationToken)
    {
        var uri = new Uri(connection.BaseUri, pathAndQuery);
        var path = uri.AbsolutePath; // ohne Abfrage: dort können Namen und Adressen stehen
        if (!WriteAllowed(method, path))
        {
            throw new InvalidOperationException($"EstrichONE schreibt in Lexware Office nur Kontakte (anlegen, ändern); {method.Method} {path} ist nicht vorgesehen.");
        }

        var payload = body?.ToJsonString();
        var attempts = Math.Max(1, Options.MaxAttempts);
        for (var attempt = 1; ; attempt++)
        {
            await ThrottleAsync(cancellationToken);
            using var request = new HttpRequestMessage(method, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.ApiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
            if (payload is not null)
            {
                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Options.AttemptTimeout);
            HttpResponseMessage response;
            var started = Stopwatch.GetTimestamp();
            try
            {
                response = await http.CreateClient(HttpClientName).SendAsync(request, timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (method == HttpMethod.Get && attempt < attempts)
                {
                    continue;
                }

                throw new LexwareException("TIMEOUT", $"{Service} hat nicht rechtzeitig geantwortet. Bitte versuchen Sie es in einigen Minuten erneut.");
            }
            catch (HttpRequestException exception)
            {
                if (method == HttpMethod.Get && attempt < attempts)
                {
                    await Task.Delay(Backoff(attempt, null), cancellationToken);
                    continue;
                }

                logger.LogWarning("Lexware {Method} {Path}: keine Verbindung ({Error})", method.Method, path, exception.HttpRequestError);
                throw new LexwareException("OFFLINE",
                    $"{Service} ist vom Server aus nicht erreichbar ({uri.Host}). Bitte prüfen Sie die Internetverbindung und die Firewall des Servers.");
            }

            using (response)
            {
                var status = (int)response.StatusCode;
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug("Lexware {Method} {Path}: HTTP {Status} nach {Elapsed} ms", method.Method, path, status,
                        (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                }

                if (response.IsSuccessStatusCode)
                {
                    return (await response.Content.ReadAsByteArrayAsync(cancellationToken), response.Content.Headers.ContentType?.MediaType);
                }

                var text = await response.Content.ReadAsStringAsync(cancellationToken);

                // 429 heißt: nicht verarbeitet, auch schreibende Aufrufe dürfen wiederholt werden.
                var retryable = status == 429 || (method == HttpMethod.Get && status is 502 or 503 or 504);
                if (retryable && attempt < attempts)
                {
                    await Task.Delay(Backoff(attempt, response.Headers.RetryAfter), cancellationToken);
                    continue;
                }

                logger.LogWarning("Lexware {Method} {Path}: HTTP {Status}", method.Method, path, status);
                throw Failure(status, text);
            }
        }
    }

    private async Task ThrottleAsync(CancellationToken cancellationToken)
    {
        var interval = Options.MinRequestInterval;
        if (interval <= TimeSpan.Zero)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var last = Interlocked.Read(ref _lastRequest);
            if (last != 0)
            {
                var wait = interval - Stopwatch.GetElapsedTime(last);
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, cancellationToken);
                }
            }

            Interlocked.Exchange(ref _lastRequest, Stopwatch.GetTimestamp());
        }
        finally
        {
            _gate.Release();
        }
    }

    private TimeSpan Backoff(int attempt, RetryConditionHeaderValue? retryAfter)
    {
        var delay = retryAfter?.Delta ?? (retryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : (TimeSpan?)null)
            ?? TimeSpan.FromTicks(Options.RetryDelay.Ticks * (1L << Math.Min(attempt - 1, 6)));
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay > Options.MaxRetryDelay ? Options.MaxRetryDelay : delay;
    }

    private static JsonNode? Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            throw new LexwareException("UNEXPECTED_RESPONSE", $"Die Antwort von {Service} konnte nicht verarbeitet werden.");
        }
    }

    /// <summary>HTTP-Status der API als verständliche Meldung.</summary>
    internal static LexwareException Failure(int status, string? body)
    {
        var details = Details(body);
        var suffix = details.Length > 0 ? " Einzelheiten: " + details : string.Empty;
        return status switch
        {
            401 => new LexwareException("UNAUTHORIZED",
                "Der API-Schlüssel wurde von Lexware Office abgelehnt. Bitte erzeugen Sie in Lexware Office einen neuen Schlüssel und hinterlegen Sie ihn.", status),
            402 or 403 => new LexwareException("NO_API_ACCESS",
                "Lexware Office verweigert den Zugriff. Die Public API ist im gebuchten Tarif nicht enthalten oder der Schlüssel hat dafür keine Berechtigung.", status),
            404 => new LexwareException("NOT_FOUND", "Der Eintrag wurde in Lexware Office nicht gefunden. Möglicherweise wurde er dort gelöscht.", status),
            409 => new LexwareException("CONFLICT", "Lexware Office meldet einen Konflikt. Der Eintrag wurde dort inzwischen geändert." + suffix, status),
            400 or 406 or 422 => new LexwareException("INVALID", "Lexware Office hat die Angaben nicht angenommen." + suffix, status),
            429 => new LexwareException("RATE_LIMITED",
                "Lexware Office nimmt im Moment keine weiteren Anfragen an (höchstens zwei je Sekunde). Bitte versuchen Sie es in einer Minute erneut.", status),
            >= 500 => new LexwareException("UNAVAILABLE", "Lexware Office ist im Moment nicht verfügbar. Bitte versuchen Sie es später erneut.", status),
            _ => new LexwareException("FAILED", $"Lexware Office hat mit dem Status {status.ToString(CultureInfo.InvariantCulture)} geantwortet." + suffix, status),
        };
    }

    /// <summary>
    /// Fehlertexte der API: älteres Format (<c>message</c>, <c>details[].field/message/violation</c>) und neueres
    /// (<c>IssueList[].source/type/i18nKey</c>). Höchstens 400 Zeichen.
    /// </summary>
    internal static string Details(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        JsonNode? json;
        try
        {
            json = JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (var detail in (json?["details"] as JsonArray ?? []).OfType<JsonObject>())
        {
            parts.Add(string.Join(' ', new[] { Str(detail["field"]), Str(detail["message"]) ?? Str(detail["violation"]) }.Where(p => p is not null)));
        }

        foreach (var issue in (json?["IssueList"] as JsonArray ?? []).OfType<JsonObject>())
        {
            parts.Add(string.Join(' ', new[] { Str(issue["source"]), Str(issue["i18nKey"]) ?? Str(issue["type"]) }.Where(p => p is not null)));
        }

        if (parts.Count == 0 && Str(json?["message"]) is { } message)
        {
            parts.Add(message);
        }

        var text = string.Join("; ", parts.Where(p => p.Length > 0).Distinct());
        return text.Length > 400 ? text[..400] : text;
    }
}
