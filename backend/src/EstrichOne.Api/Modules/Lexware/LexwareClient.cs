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

    /// <summary>Einträge je Seite beim Abruf der Artikel (<c>GET /v1/articles</c>).</summary>
    public int ArticlePageSize { get; set; } = 250;

    /// <summary>Obergrenze der Seiten beim Abruf der Artikel.</summary>
    public int MaxArticlePages { get; set; } = 40;

    /// <summary>Abstand des regelmäßigen Artikelabgleichs im Hintergrund.</summary>
    public TimeSpan ArticleSyncInterval { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Wie lange die Zahlungsbedingungen aus Lexware Office zwischengespeichert werden.</summary>
    public TimeSpan PaymentConditionCacheDuration { get; set; } = TimeSpan.FromMinutes(30);
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

    /// <summary>Einzelne Beanstandungen, übersetzt (bei 400, 406, 422).</summary>
    public IReadOnlyList<LexwareIssue> Issues { get; init; } = [];

    /// <summary>Originaltext von Lexware Office (englisch) für „Technische Einzelheiten“.</summary>
    public string? Technical { get; init; }

    /// <summary>Von Lexware Office abgelehnte Steuerart (Code <see cref="LexwareErrors.TaxTypeNotAllowed"/>).</summary>
    public string? RejectedTaxType { get; init; }

    /// <summary>Bekannte Behebung der ersten Beanstandung, falls es eine gibt.</summary>
    public string? Fix => Issues.Select(i => i.Fix).FirstOrDefault(f => f is not null);
}

/// <summary>Zugangsdaten eines Aufrufs. Der Schlüssel erscheint nie in Protokollen oder Meldungen.</summary>
public sealed record LexwareConnection(Uri BaseUri, string ApiKey);

/// <summary>Profil der Organisation (<c>GET /v1/profile</c>).</summary>
/// <param name="TaxType">Besteuerungsart der Organisation laut Profil: <c>net</c>, <c>gross</c> oder <c>vatfree</c> (falls geliefert).</param>
/// <param name="SmallBusiness">Kleinunternehmer nach § 19 UStG (falls geliefert).</param>
public sealed record LexwareProfile(string? OrganizationId, string? CompanyName, string? TaxType = null, bool? SmallBusiness = null);

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

/// <summary>Artikel aus Lexware Office (<c>/v1/articles</c>), auf die Felder reduziert, die EstrichONE abgleicht.</summary>
/// <param name="Type"><c>PRODUCT</c> oder <c>SERVICE</c>.</param>
/// <param name="LeadingPrice"><c>NET</c> oder <c>GROSS</c>: welcher Preis in Lexware Office führt.</param>
public sealed record LexwareArticle(
    string Id, string Title, string? Description, string Type, string? ArticleNumber, string? Gtin, string? UnitName, decimal? NetPrice,
    decimal? GrossPrice, string LeadingPrice, decimal? TaxRate, bool Archived, int? Version, DateTimeOffset? UpdatedAt);

/// <summary>Neuer Artikel bzw. Werte für die Änderung eines Artikels. Der Preis führt netto.</summary>
public sealed record LexwareArticleDraft(
    string Title, string? Description, string Type, string? ArticleNumber, string? Gtin, string? UnitName, decimal NetPrice, decimal TaxRate);

/// <summary>Zahlungsbedingung aus Lexware Office (<c>GET /v1/payment-conditions</c>).</summary>
/// <param name="Label">Text der Zahlungsbedingung, wie er im Angebot steht.</param>
/// <param name="Duration">Zahlungsziel in Tagen.</param>
public sealed record LexwarePaymentCondition(
    string? Id, string Label, int? Duration, decimal? DiscountPercent, int? DiscountRange, bool IsDefault);

/// <summary>Position eines Verkaufsbelegs für <c>POST /v1/{quotations|order-confirmations|invoices|credit-notes}</c>.</summary>
/// <param name="Type"><c>custom</c> (freie Position, auch Abzug mit negativem Preis), <c>material</c> bzw. <c>service</c> (Artikel aus Lexware Office mit <paramref name="ArticleId"/>) oder <c>text</c>.</param>
public sealed record LexwareSalesLine(
    string Type, string? ArticleId, string Name, string? Description, decimal? Quantity, string? UnitName, decimal? NetPrice, decimal? TaxRate,
    decimal? DiscountPercent);

/// <summary>Verkaufsbeleg, den EstrichONE in Lexware Office anlegt (Entwurf, festgeschrieben nur ausdrücklich).</summary>
/// <param name="Type">Belegart (<see cref="LexwareVoucherTypes"/>): quotation, orderconfirmation, invoice, creditnote.</param>
/// <param name="TaxType"><c>net</c>, <c>vatfree</c> oder <c>constructionService13b</c> (Bauleistung nach § 13b UStG).</param>
/// <param name="ExpirationDate">Angebot: gültig bis.</param>
/// <param name="ServiceDate">Leistungsdatum bzw. Beginn des Leistungszeitraums (<c>shippingConditions</c>; Rechnung, Auftragsbestätigung, Gutschrift).</param>
public sealed record LexwareSalesVoucher(
    string ContactId, DateOnly VoucherDate, DateOnly? ExpirationDate, string? Title, string? Introduction, string? Remark, string TaxType,
    LexwarePaymentCondition? Payment, IReadOnlyList<LexwareSalesLine> Lines, string Type = LexwareVoucherTypes.Quotation, DateOnly? ServiceDate = null,
    DateOnly? ServiceEndDate = null);

/// <summary>Zahlstand einer Rechnung (<c>GET /v1/payments/{id}</c>).</summary>
/// <param name="PaymentStatus">Laut Lexware z. B. <c>openRevenue</c>, <c>balanced</c>.</param>
public sealed record LexwarePayment(decimal? OpenAmount, string? PaymentStatus, DateOnly? PaidDate);

/// <summary>Eintrag der Belegliste für die Übersicht „Verkauf“.</summary>
/// <param name="Status">Wie in der Belegliste, einschließlich <c>overdue</c>.</param>
public sealed record LexwareListVoucher(
    string Id, string Type, string Status, string? Number, DateOnly? VoucherDate, DateOnly? DueDate, decimal? GrossAmount, decimal? OpenAmount,
    string? ContactId, string? ContactName);

/// <summary>Antwort auf eine Anlage in Lexware Office.</summary>
public sealed record LexwareCreated(string Id, int? Version);

/// <summary>Beleg aus Lexware Office (nur gelesen).</summary>
/// <param name="Type"><c>invoice</c>, <c>quotation</c>, <c>creditnote</c>, <c>orderconfirmation</c> oder <c>deliverynote</c>.</param>
/// <param name="DueDate">Fälligkeit der Rechnung bzw. „gültig bis“ des Angebots.</param>
/// <param name="Text">Titel, Einleitung und Schlusstext (nur beim Einzelabruf), für den Zuordnungsvorschlag über die Bauaktennummer.</param>
public sealed record LexwareVoucher(
    string Id, string Type, string Status, string? Number, string? Title, DateOnly? VoucherDate, DateOnly? DueDate, decimal? NetAmount,
    decimal? GrossAmount, string Currency, string? ContactId, string? ContactName, string? Text = null, IReadOnlyList<string>? Preceding = null);

/// <summary>Belegarten, die EstrichONE anzeigt, mit ihrem Pfad in der API und in den Links der Lexware-Oberfläche.</summary>
public static class LexwareVoucherTypes
{
    public const string Invoice = "invoice";
    public const string Quotation = "quotation";
    public const string CreditNote = "creditnote";
    public const string OrderConfirmation = "orderconfirmation";

    /// <summary>Lieferschein (nur lesend angezeigt).</summary>
    public const string DeliveryNote = "deliverynote";

    public static IReadOnlyList<string> All { get; } = [Invoice, Quotation, CreditNote, OrderConfirmation, DeliveryNote];

    /// <summary>Belege mit Zahlungsbedeutung (offen, überfällig, bezahlt): Rechnung und Gutschrift.</summary>
    public static bool Payable(string? type) => type is Invoice or CreditNote;

    /// <summary>Pfadteil der API und der Permalinks; <c>null</c> für unbekannte Arten.</summary>
    public static string? Segment(string? type) => type switch
    {
        Invoice => "invoices",
        Quotation => "quotations",
        CreditNote => "credit-notes",
        OrderConfirmation => "order-confirmations",
        DeliveryNote => "delivery-notes",
        _ => null,
    };
}

/// <summary>
/// Gekapselter Zugang zur Public API von Lexware Office (früher lexoffice): Bearer-Schlüssel, höchstens zwei Anfragen je
/// Sekunde, Wiederholung bei 429 mit wachsender Wartezeit, Zeitlimit je Versuch und verständliche Fehlermeldungen.
/// Geschrieben wird nur an genau benannten Stellen (<see cref="WriteAllowed"/>): Kontakte anlegen und ändern
/// (<c>POST /v1/contacts</c>, <c>PUT /v1/contacts/{id}</c>), Artikel anlegen und ändern (<c>POST /v1/articles</c>,
/// <c>PUT /v1/articles/{id}</c>) und Verkaufsbelege anlegen (Angebot, Auftragsbestätigung, Rechnung, Gutschrift; als Entwurf,
/// festgeschrieben nur mit ausdrücklicher Freigabe). Belege werden nie geändert, gelöscht oder archiviert.
/// <see cref="WriteAllowed"/> weist jeden anderen schreibenden Aufruf schon vor dem Senden ab.
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
        return new LexwareProfile(Str(json?["organizationId"]), Str(json?["companyName"]), Str(json?["taxType"]),
            json?["smallBusiness"] is JsonValue small && small.TryGetValue<bool>(out var isSmall) ? isSmall : null);
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
        var lineTexts = (json?["lineItems"] as JsonArray ?? []).OfType<JsonObject>().SelectMany(l => new[] { Str(l["name"]), Str(l["description"]) });
        var text = string.Join('\n', new[] { Str(json?["title"]), Str(json?["introduction"]), Str(json?["remark"]) }.Concat(lineTexts).Where(p => p is not null));
        // Belegkette: Vorgänger laut Lexware Office (Feldnamen nach bisherigem Wissen, siehe Prüfliste).
        var preceding = new[] { Str(json?["precedingSalesVoucherId"]) }
            .Concat((json?["relatedVouchers"] as JsonArray ?? []).OfType<JsonObject>().Select(r => Str(r["id"])))
            .Where(p => p is not null && p != id).Select(p => p!).Distinct().ToList();
        return new LexwareVoucher(Str(json?["id"]) ?? id, type, Str(json?["voucherStatus"])?.ToLowerInvariant() ?? "draft", Str(json?["voucherNumber"]),
            Str(json?["title"]), Date(json?["voucherDate"]), Date(json?["dueDate"]) ?? Date(json?["expirationDate"]) ?? DueDate(json),
            Decimal(total?["totalNetAmount"]), Decimal(total?["totalGrossAmount"]), Str(total?["currency"]) ?? "EUR", Str(address?["contactId"]),
            Str(address?["name"]), text, preceding);
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

    // ---------- Artikel ----------

    /// <summary>Artikel, seitenweise (<c>GET /v1/articles?page=&amp;size=</c>).</summary>
    public async Task<LexwarePage<LexwareArticle>> ListArticlesAsync(LexwareConnection connection, int page, int size, CancellationToken cancellationToken)
    {
        var json = await SendAsync(connection, HttpMethod.Get,
            $"/v1/articles?page={page.ToString(CultureInfo.InvariantCulture)}&size={size.ToString(CultureInfo.InvariantCulture)}", null, cancellationToken);
        var items = (json?["content"] as JsonArray ?? []).OfType<JsonObject>().Select(ReadArticle).Where(a => a is not null).Select(a => a!).ToList();
        var totalPages = Int(json?["totalPages"]) ?? 1;
        var last = json?["last"] is JsonValue flag && flag.TryGetValue<bool>(out var isLast) ? isLast : page + 1 >= totalPages;
        return new LexwarePage<LexwareArticle>(items, page, totalPages, Int(json?["totalElements"]) ?? items.Count, last);
    }

    public async Task<LexwareArticle> GetArticleAsync(LexwareConnection connection, string id, CancellationToken cancellationToken)
    {
        var json = await SendAsync(connection, HttpMethod.Get, $"/v1/articles/{Uri.EscapeDataString(id)}", null, cancellationToken);
        return (json as JsonObject is { } node ? ReadArticle(node) : null)
            ?? throw new LexwareException("UNEXPECTED_RESPONSE", $"Die Antwort von {Service} konnte nicht verarbeitet werden.");
    }

    /// <summary>Legt einen Artikel an (<c>POST /v1/articles</c>) und liefert ID und Version. Wird bei Fehlern nicht wiederholt (keine Doppelanlage).</summary>
    public async Task<LexwareCreated> CreateArticleAsync(LexwareConnection connection, LexwareArticleDraft draft, CancellationToken cancellationToken)
    {
        var body = new JsonObject();
        WriteArticle(body, draft);
        var json = await SendAsync(connection, HttpMethod.Post, "/v1/articles", body, cancellationToken);
        return new LexwareCreated(Str(json?["id"]) ?? throw new LexwareException("UNEXPECTED_RESPONSE", $"{Service} hat keine Artikel-ID geliefert."),
            Int(json?["version"]));
    }

    /// <summary>
    /// Ändert einen Artikel (<c>PUT /v1/articles/{id}</c>): frisch lesen, <paramref name="decide"/> bestimmt aus diesem
    /// Stand die neuen Werte (<c>null</c> = nichts schreiben), zurückgeschrieben wird der gelesene Artikel mit seiner
    /// <c>version</c>. Bei Versionskonflikt (409) wird neu gelesen und neu entschieden.
    /// </summary>
    public async Task<(LexwareArticle Article, bool Written)> UpdateArticleAsync(LexwareConnection connection, string id,
        Func<LexwareArticle, LexwareArticleDraft?> decide, CancellationToken cancellationToken)
    {
        var path = $"/v1/articles/{Uri.EscapeDataString(id)}";
        var attempts = Math.Max(1, Options.UpdateAttempts);
        for (var attempt = 1; ; attempt++)
        {
            if (await SendAsync(connection, HttpMethod.Get, path, null, cancellationToken) is not JsonObject raw || ReadArticle(raw) is not { } current)
            {
                throw new LexwareException("UNEXPECTED_RESPONSE", $"Die Antwort von {Service} konnte nicht verarbeitet werden.");
            }

            if (decide(current) is not { } values)
            {
                return (current, false);
            }

            if (current.Version is null)
            {
                throw new LexwareException("UNEXPECTED_RESPONSE", $"{Service} hat keine Version des Artikels geliefert; ohne sie wird nichts geändert.");
            }

            var body = (JsonObject)raw.DeepClone();
            foreach (var field in ReadOnlyArticleFields)
            {
                body.Remove(field);
            }

            WriteArticle(body, values);
            JsonNode? answer;
            try
            {
                answer = await SendAsync(connection, HttpMethod.Put, path, body, cancellationToken);
            }
            catch (LexwareException exception) when (exception.Code == "CONFLICT" && attempt < attempts)
            {
                continue;
            }

            body["id"] = current.Id;
            body["version"] = Int(answer?["version"]) ?? current.Version.Value + 1;
            if (Str(answer?["updatedDate"]) is { } updated)
            {
                body["updatedDate"] = updated;
            }

            return (ReadArticle(body) ?? current, true);
        }
    }

    /// <summary>Felder der Antwort, die Lexware Office selbst führt und die beim Zurückschreiben eines Artikels nicht mitgehen.</summary>
    private static readonly string[] ReadOnlyArticleFields = ["id", "organizationId", "createdDate", "updatedDate", "archived"];

    /// <summary>Artikelwerte schreiben; Brutto wird aus Netto und Steuersatz berechnet, der Preis führt netto.</summary>
    internal static void WriteArticle(JsonObject body, LexwareArticleDraft draft)
    {
        body["title"] = draft.Title.Trim();
        Set(body, "description", Blank(draft.Description));
        body["type"] = draft.Type;
        Set(body, "articleNumber", Blank(draft.ArticleNumber));
        Set(body, "gtin", Blank(draft.Gtin));
        Set(body, "unitName", Blank(draft.UnitName));
        body["price"] = new JsonObject
        {
            ["netPrice"] = draft.NetPrice, ["grossPrice"] = Math.Round(draft.NetPrice * (1 + draft.TaxRate / 100m), 2, MidpointRounding.AwayFromZero),
            ["leadingPrice"] = "NET", ["taxRate"] = draft.TaxRate,
        };
        body["version"] ??= 0;
    }

    internal static LexwareArticle? ReadArticle(JsonObject node)
    {
        if (Str(node["id"]) is not { } id)
        {
            return null;
        }

        var price = node["price"] as JsonObject;
        var type = Str(node["type"])?.ToUpperInvariant() == "SERVICE" ? "SERVICE" : "PRODUCT";
        return new LexwareArticle(id, Str(node["title"]) ?? "(ohne Bezeichnung)", Str(node["description"]), type, Str(node["articleNumber"]), Str(node["gtin"]),
            Str(node["unitName"]), Decimal(price?["netPrice"]), Decimal(price?["grossPrice"]), Str(price?["leadingPrice"])?.ToUpperInvariant() ?? "NET",
            Decimal(price?["taxRate"]), node["archived"] is JsonValue archived && archived.TryGetValue<bool>(out var isArchived) && isArchived, Int(node["version"]),
            Str(node["updatedDate"]) is { } updated && DateTimeOffset.TryParse(updated, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? at.ToUniversalTime() : null);
    }

    // ---------- Zahlungsbedingungen und Angebote ----------

    /// <summary>Zahlungsbedingungen der Organisation (<c>GET /v1/payment-conditions</c>, nur lesend).</summary>
    public async Task<IReadOnlyList<LexwarePaymentCondition>> ListPaymentConditionsAsync(LexwareConnection connection, CancellationToken cancellationToken)
    {
        var json = await SendAsync(connection, HttpMethod.Get, "/v1/payment-conditions", null, cancellationToken);
        var list = json as JsonArray ?? json?["content"] as JsonArray ?? [];
        return list.OfType<JsonObject>().Select(c =>
        {
            var discount = c["paymentDiscountConditions"] as JsonObject;
            var label = Str(c["paymentTermLabelTemplate"]) ?? Str(c["paymentTermLabel"]) ?? string.Empty;
            return new LexwarePaymentCondition(Str(c["id"]), label, Int(c["paymentTermDuration"]), Decimal(discount?["discountPercentage"]),
                Int(discount?["discountRange"]), c["organizationDefault"] is JsonValue flag && flag.TryGetValue<bool>(out var isDefault) && isDefault);
        }).Where(c => c.Label.Length > 0).ToList();
    }

    /// <summary>
    /// Legt ein Angebot als Entwurf an (<c>POST /v1/quotations</c> ohne <c>finalize</c>): Nummer, Festschreiben und Versand
    /// bleiben in Lexware Office. Wird bei Fehlern nicht wiederholt, damit kein Angebot doppelt entsteht.
    /// </summary>
    public async Task<LexwareCreated> CreateSalesVoucherAsync(LexwareConnection connection, LexwareSalesVoucher draft, string? precedingLexwareId, bool finalize,
        CancellationToken cancellationToken)
    {
        var segment = SalesSegment(draft.Type);
        var query = new List<string>();
        if (finalize)
        {
            query.Add("finalize=true");
        }

        if (!string.IsNullOrWhiteSpace(precedingLexwareId))
        {
            query.Add("precedingSalesVoucherId=" + Uri.EscapeDataString(precedingLexwareId.Trim()));
        }

        var path = $"/v1/{segment}" + (query.Count > 0 ? "?" + string.Join('&', query) : string.Empty);
        var json = Parse(Encoding.UTF8.GetString((await SendRawAsync(connection, HttpMethod.Post, path, SalesBody(draft), "application/json", cancellationToken,
            finalize)).Content));
        return new LexwareCreated(Str(json?["id"]) ?? throw new LexwareException("UNEXPECTED_RESPONSE", $"{Service} hat keine ID des Belegs geliefert."),
            Int(json?["version"]));
    }

    private static string SalesSegment(string type) => type is LexwareVoucherTypes.Quotation or LexwareVoucherTypes.OrderConfirmation or LexwareVoucherTypes.Invoice
        or LexwareVoucherTypes.CreditNote
        ? LexwareVoucherTypes.Segment(type)!
        : throw new LexwareException("UNKNOWN_VOUCHER_TYPE", "Diese Belegart kann EstrichONE nicht anlegen.");

    /// <summary>Zahlstand einer Rechnung (<c>GET /v1/payments/{id}</c>, nur lesend).</summary>
    public async Task<LexwarePayment> GetPaymentAsync(LexwareConnection connection, string voucherId, CancellationToken cancellationToken)
    {
        var json = await SendAsync(connection, HttpMethod.Get, $"/v1/payments/{Uri.EscapeDataString(voucherId)}", null, cancellationToken);
        return new LexwarePayment(Decimal(json?["openAmount"]), Str(json?["paymentStatus"]), Date(json?["paidDate"]));
    }

    /// <summary>
    /// Verkaufsbelege der Belegliste, neueste zuerst (<c>GET /v1/voucherlist</c>), mit offenem Betrag (<c>openAmount</c>) und
    /// dem Status der Liste (auch <c>overdue</c>). Seitenweise bis <paramref name="maxPages"/>.
    /// </summary>
    public async Task<IReadOnlyList<LexwareListVoucher>> ListSalesVouchersAsync(LexwareConnection connection, IReadOnlyList<string> types, int size, int maxPages,
        CancellationToken cancellationToken)
    {
        var result = new List<LexwareListVoucher>();
        for (var page = 0; page < Math.Max(1, maxPages); page++)
        {
            var query = new StringBuilder("/v1/voucherlist?voucherType=").Append(string.Join(',', types))
                .Append("&voucherStatus=any&sort=voucherDate,DESC&size=").Append(size.ToString(CultureInfo.InvariantCulture))
                .Append("&page=").Append(page.ToString(CultureInfo.InvariantCulture));
            var json = await SendAsync(connection, HttpMethod.Get, query.ToString(), null, cancellationToken);
            foreach (var v in (json?["content"] as JsonArray ?? []).OfType<JsonObject>())
            {
                if (Str(v["id"]) is { } id && Str(v["voucherType"])?.ToLowerInvariant() is { } type && types.Contains(type))
                {
                    result.Add(new LexwareListVoucher(id, type, Str(v["voucherStatus"])?.ToLowerInvariant() ?? "open", Str(v["voucherNumber"]), Date(v["voucherDate"]),
                        Date(v["dueDate"]), Decimal(v["totalAmount"]), Decimal(v["openAmount"]), Str(v["contactId"]), Str(v["contactName"])));
                }
            }

            if (json?["last"] is not JsonValue last || !last.TryGetValue<bool>(out var isLast) || isLast)
            {
                break;
            }
        }

        return result;
    }

    /// <summary>Datum als Zeitpunkt um Mitternacht in deutscher Zeit, wie Lexware Office es erwartet (<c>2026-11-27T00:00:00.000+01:00</c>).</summary>
    internal static string LexwareDate(DateOnly date)
    {
        TimeSpan offset;
        try
        {
            offset = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin").GetUtcOffset(date.ToDateTime(new TimeOnly(12, 0)));
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            offset = TimeSpan.FromHours(1);
        }

        return new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), offset).ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Inhalt für <c>POST /v1/{belegart}</c>. Bei § 13b UStG und steuerfrei tragen die Positionen den Steuersatz 0. Angebote
    /// tragen <c>expirationDate</c>, Rechnungen, Auftragsbestätigungen und Gutschriften <c>shippingConditions</c> (Leistungsdatum
    /// bzw. Leistungszeitraum). Gutschriften haben keine Zahlungsbedingung.
    /// </summary>
    internal static JsonObject SalesBody(LexwareSalesVoucher draft)
    {
        var reverse = LexwareTaxTypes.WithoutTax(draft.TaxType);
        var lines = new JsonArray();
        foreach (var line in draft.Lines)
        {
            var item = new JsonObject { ["type"] = line.Type, ["name"] = line.Name.Trim() };
            Set(item, "description", Blank(line.Description));
            if (line.Type != "text")
            {
                if (line.ArticleId is not null && line.Type is "material" or "service")
                {
                    item["id"] = line.ArticleId;
                }

                item["quantity"] = line.Quantity ?? 1m;
                item["unitName"] = Blank(line.UnitName) ?? "Stück";
                item["unitPrice"] = new JsonObject
                {
                    ["currency"] = "EUR", ["netAmount"] = line.NetPrice ?? 0m, ["taxRatePercentage"] = reverse ? 0m : line.TaxRate ?? 19m,
                };
                if (line.DiscountPercent is > 0)
                {
                    item["discountPercentage"] = line.DiscountPercent.Value;
                }
            }

            lines.Add(item);
        }

        var body = new JsonObject
        {
            ["voucherDate"] = LexwareDate(draft.VoucherDate),
            ["address"] = new JsonObject { ["contactId"] = draft.ContactId },
            ["lineItems"] = lines,
            ["totalPrice"] = new JsonObject { ["currency"] = "EUR" },
            ["taxConditions"] = new JsonObject { ["taxType"] = draft.TaxType },
        };
        if (draft.Type == LexwareVoucherTypes.Quotation && draft.ExpirationDate is { } expires)
        {
            body["expirationDate"] = LexwareDate(expires);
        }

        if (draft.Type != LexwareVoucherTypes.Quotation && draft.ServiceDate is { } start)
        {
            body["shippingConditions"] = draft.ServiceEndDate is { } end && end > start
                ? new JsonObject { ["shippingType"] = "serviceperiod", ["shippingDate"] = LexwareDate(start), ["shippingEndDate"] = LexwareDate(end) }
                : new JsonObject { ["shippingType"] = "service", ["shippingDate"] = LexwareDate(start) };
        }

        Set(body, "title", Blank(draft.Title));
        Set(body, "introduction", Blank(draft.Introduction));
        Set(body, "remark", Blank(draft.Remark));
        if (draft.Payment is { } payment && draft.Type != LexwareVoucherTypes.CreditNote)
        {
            var conditions = new JsonObject { ["paymentTermLabel"] = payment.Label };
            if (payment.Duration is { } days)
            {
                conditions["paymentTermDuration"] = days;
            }

            if (payment.DiscountPercent is { } percent && payment.DiscountRange is { } range)
            {
                conditions["paymentDiscountConditions"] = new JsonObject { ["discountPercentage"] = percent, ["discountRange"] = range };
            }

            body["paymentConditions"] = conditions;
        }

        return body;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
    /// Schreibende Aufrufe sind nur an genau diesen Stellen erlaubt: Kontakt anlegen (<c>POST /v1/contacts</c>) und einen
    /// einzelnen ändern (<c>PUT /v1/contacts/{id}</c>), Artikel anlegen (<c>POST /v1/articles</c>) und einen einzelnen
    /// ändern (<c>PUT /v1/articles/{id}</c>) sowie Verkaufsbelege anlegen (<c>POST /v1/quotations</c>,
    /// <c>/v1/order-confirmations</c>, <c>/v1/invoices</c>, <c>/v1/credit-notes</c>). Als Abfrage ist bei Verkaufsbelegen nur
    /// <c>precedingSalesVoucherId</c> (Belegkette) erlaubt und <c>finalize=true</c> nur, wenn <paramref name="finalize"/>
    /// ausdrücklich gesetzt ist (Modul, Recht und Bestätigung geprüft). Alle anderen schreibenden Aufrufe tragen keine
    /// Abfrage. Belege werden nie geändert, gelöscht oder archiviert, Kontakte und Artikel nie gelöscht.
    /// </summary>
    internal static bool WriteAllowed(HttpMethod method, string path, string? query = null, bool finalize = false)
    {
        if (method == HttpMethod.Get)
        {
            return true;
        }

        var hasQuery = !string.IsNullOrEmpty(query) && query != "?";
        if (method == HttpMethod.Post && path is "/v1/quotations" or "/v1/order-confirmations" or "/v1/invoices" or "/v1/credit-notes")
        {
            return !hasQuery || SalesQueryAllowed(query!, finalize);
        }

        if (hasQuery)
        {
            return false;
        }

        return (method == HttpMethod.Post && path is "/v1/contacts" or "/v1/articles")
            || (method == HttpMethod.Put && (Single(path, ContactPath) || Single(path, ArticlePath)));
    }

    /// <summary>Abfrage beim Anlegen eines Verkaufsbelegs: höchstens <c>precedingSalesVoucherId</c> und, nur mit Freigabe, <c>finalize=true</c>.</summary>
    private static bool SalesQueryAllowed(string query, bool finalize)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in query.TrimStart('?').Split('&'))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2 || !seen.Add(pair[0]))
            {
                return false;
            }

            var ok = pair[0] switch
            {
                "finalize" => finalize && pair[1] == "true",
                "precedingSalesVoucherId" => pair[1].Length is > 0 and <= 100 && pair[1].All(c => char.IsAsciiLetterOrDigit(c) || c == '-'),
                _ => false,
            };
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }

    private static bool Single(string path, string prefix) =>
        path.StartsWith(prefix, StringComparison.Ordinal) && path.Length > prefix.Length && path.IndexOf('/', prefix.Length) < 0;

    private const string ArticlePath = "/v1/articles/";

    private const string ContactPath = "/v1/contacts/";

    // ---------- Transport ----------

    private async Task<JsonNode?> SendAsync(LexwareConnection connection, HttpMethod method, string pathAndQuery, JsonObject? body,
        CancellationToken cancellationToken) =>
        Parse(Encoding.UTF8.GetString((await SendRawAsync(connection, method, pathAndQuery, body, "application/json", cancellationToken)).Content));

    private async Task<(byte[] Content, string? ContentType)> SendRawAsync(LexwareConnection connection, HttpMethod method, string pathAndQuery,
        JsonObject? body, string accept, CancellationToken cancellationToken, bool finalize = false)
    {
        var uri = new Uri(connection.BaseUri, pathAndQuery);
        var path = uri.AbsolutePath; // ohne Abfrage: dort können Namen und Adressen stehen
        if (!WriteAllowed(method, path, uri.Query, finalize))
        {
            throw new InvalidOperationException(
                $"EstrichONE schreibt in Lexware Office nur Kontakte und Artikel (anlegen, ändern) und Verkaufsbelege (anlegen); {method.Method} {path} ist so nicht vorgesehen.");
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

    /// <summary>Abgelehnte Angaben: deutsche Meldung mit Bezug auf Feld bzw. Position, Originaltext als technische Einzelheit.</summary>
    private static LexwareException Invalid(int status, string? body)
    {
        var translation = LexwareErrors.Translate(body);
        return new LexwareException(translation.Code, translation.Message, status)
        {
            Issues = translation.Issues, Technical = translation.Technical, RejectedTaxType = translation.RejectedTaxType,
        };
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
            400 or 406 or 422 => Invalid(status, body),
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
