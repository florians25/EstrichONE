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
    bool Archived);

/// <summary>Neuer Kontakt (Rolle Kunde).</summary>
public sealed record LexwareContactDraft(
    bool IsCompany, string Name, string? Salutation, string? FirstName, string? LastName, string? Street, string? PostalCode, string? City,
    string? Email, string? Phone, string? MobilePhone, string? VatId, string? TaxNumber, string? Note);

public sealed record LexwarePage<T>(IReadOnlyList<T> Items, int Page, int TotalPages, int TotalElements, bool Last);

/// <param name="Kind"><c>custom</c> (Position mit Menge und Preis) oder <c>text</c> (reine Textzeile).</param>
public sealed record LexwareLineDraft(string Kind, string Name, string? Description, decimal Quantity, string? UnitName, decimal NetUnitPrice,
    decimal TaxRatePercentage);

public sealed record LexwareInvoiceDraft(
    string ContactId, DateOnly VoucherDate, string? Title, string? Introduction, string? Remark, IReadOnlyList<LexwareLineDraft> Lines,
    int PaymentTermDays, string PaymentTermLabel, DateOnly? ServiceDate, DateOnly? ServiceEndDate);

public sealed record LexwareInvoice(
    string Id, string Status, string? Number, string? Title, DateOnly? VoucherDate, DateOnly? DueDate, decimal? NetAmount, decimal? GrossAmount,
    string Currency, string? ContactId, string? ContactName);

/// <summary>
/// Gekapselter Zugang zur Public API von Lexware Office (früher lexoffice): Bearer-Schlüssel, höchstens zwei Anfragen je
/// Sekunde, Wiederholung bei 429 mit wachsender Wartezeit, Zeitlimit je Versuch und verständliche Fehlermeldungen.
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

    /// <summary>Legt eine Rechnung als Entwurf an (ohne <c>finalize</c>) und liefert ihre ID.</summary>
    public async Task<string> CreateInvoiceDraftAsync(LexwareConnection connection, LexwareInvoiceDraft draft, CancellationToken cancellationToken)
    {
        var json = await SendAsync(connection, HttpMethod.Post, "/v1/invoices", InvoiceBody(draft), cancellationToken);
        return Str(json?["id"]) ?? throw new LexwareException("UNEXPECTED_RESPONSE", $"{Service} hat keine Rechnungs-ID geliefert.");
    }

    internal static JsonObject InvoiceBody(LexwareInvoiceDraft draft)
    {
        var lines = new JsonArray();
        foreach (var line in draft.Lines)
        {
            var item = new JsonObject { ["type"] = line.Kind, ["name"] = line.Name };
            Put(item, "description", line.Description);
            if (line.Kind != "text")
            {
                item["quantity"] = line.Quantity;
                Put(item, "unitName", line.UnitName);
                item["unitPrice"] = new JsonObject
                {
                    ["currency"] = "EUR", ["netAmount"] = line.NetUnitPrice, ["taxRatePercentage"] = line.TaxRatePercentage,
                };
                item["discountPercentage"] = 0;
            }

            lines.Add(item);
        }

        var shipping = new JsonObject();
        if (draft.ServiceDate is { } from)
        {
            shipping["shippingDate"] = Timestamp(from);
            if (draft.ServiceEndDate is { } to && to > from)
            {
                shipping["shippingEndDate"] = Timestamp(to);
                shipping["shippingType"] = "serviceperiod";
            }
            else
            {
                shipping["shippingType"] = "service";
            }
        }
        else
        {
            shipping["shippingType"] = "none";
        }

        var body = new JsonObject
        {
            ["archived"] = false,
            ["voucherDate"] = Timestamp(draft.VoucherDate),
            ["address"] = new JsonObject { ["contactId"] = draft.ContactId },
            ["lineItems"] = lines,
            ["totalPrice"] = new JsonObject { ["currency"] = "EUR" },
            ["taxConditions"] = new JsonObject { ["taxType"] = "net" },
            ["paymentConditions"] = new JsonObject
            {
                ["paymentTermLabel"] = draft.PaymentTermLabel, ["paymentTermDuration"] = draft.PaymentTermDays,
            },
            ["shippingConditions"] = shipping,
        };
        Put(body, "title", draft.Title);
        Put(body, "introduction", draft.Introduction);
        Put(body, "remark", draft.Remark);
        return body;
    }

    public async Task<LexwareInvoice> GetInvoiceAsync(LexwareConnection connection, string id, CancellationToken cancellationToken)
    {
        var json = await SendAsync(connection, HttpMethod.Get, $"/v1/invoices/{Uri.EscapeDataString(id)}", null, cancellationToken);
        var total = json?["totalPrice"];
        var address = json?["address"];
        return new LexwareInvoice(Str(json?["id"]) ?? id, Str(json?["voucherStatus"])?.ToLowerInvariant() ?? "draft", Str(json?["voucherNumber"]),
            Str(json?["title"]), Date(json?["voucherDate"]), Date(json?["dueDate"]) ?? DueDate(json), Decimal(total?["totalNetAmount"]),
            Decimal(total?["totalGrossAmount"]), Str(total?["currency"]) ?? "EUR", Str(address?["contactId"]), Str(address?["name"]));
    }

    /// <summary>Rechnungen über die Belegnummer finden (Belegliste; ohne Entwürfe, die noch keine Nummer haben).</summary>
    public async Task<IReadOnlyList<LexwareInvoice>> FindInvoicesByNumberAsync(LexwareConnection connection, string number, CancellationToken cancellationToken)
    {
        var path = "/v1/voucherlist?voucherType=invoice&voucherStatus=open,paid,paidoff,voided,overdue&size=25&voucherNumber=" + Uri.EscapeDataString(number);
        var json = await SendAsync(connection, HttpMethod.Get, path, null, cancellationToken);
        return (json?["content"] as JsonArray ?? []).OfType<JsonObject>().Where(v => Str(v["id"]) is not null).Select(v =>
        {
            // Die Belegliste kennt zusätzlich „overdue“; gespeichert wird der Status der Rechnung selbst.
            var status = Str(v["voucherStatus"])?.ToLowerInvariant() ?? "open";
            return new LexwareInvoice(Str(v["id"])!, status == "overdue" ? "open" : status, Str(v["voucherNumber"]), null, Date(v["voucherDate"]),
                Date(v["dueDate"]), null, Decimal(v["totalAmount"]), Str(v["currency"]) ?? "EUR", Str(v["contactId"]), Str(v["contactName"]));
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
        var billing = (node["addresses"]?["billing"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault()
            ?? (node["addresses"]?["shipping"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault();
        return new LexwareContact(id, company is not null, string.IsNullOrWhiteSpace(name) ? "(ohne Namen)" : name!, Str(person?["salutation"]), first, last,
            Str(customer?["number"]), customer is not null, Flatten(node["emailAddresses"]), Flatten(node["phoneNumbers"]), Str(billing?["street"]),
            Str(billing?["zip"]), Str(billing?["city"]), Str(company?["vatRegistrationId"]), Str(company?["taxNumber"]),
            node["archived"] is JsonValue archived && archived.TryGetValue<bool>(out var isArchived) && isArchived);
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

    private static readonly TimeZoneInfo Berlin = FindBerlin();

    private static TimeZoneInfo FindBerlin()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    internal static string Timestamp(DateOnly date)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, Berlin.GetUtcOffset(local)).ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture);
    }

    // ---------- Transport ----------

    private async Task<JsonNode?> SendAsync(LexwareConnection connection, HttpMethod method, string pathAndQuery, JsonObject? body,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(connection.BaseUri, pathAndQuery);
        var path = uri.AbsolutePath; // ohne Abfrage: dort können Namen und Adressen stehen
        var payload = body?.ToJsonString();
        var attempts = Math.Max(1, Options.MaxAttempts);
        for (var attempt = 1; ; attempt++)
        {
            await ThrottleAsync(cancellationToken);
            using var request = new HttpRequestMessage(method, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.ApiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
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

                var text = await response.Content.ReadAsStringAsync(cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return Parse(text);
                }

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
