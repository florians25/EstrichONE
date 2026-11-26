using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace EstrichOne.Api.Tests.Support;

/// <summary>
/// Nachgebildete Public API von Lexware Office als <see cref="HttpMessageHandler"/> (kein echter Aufruf): Profil, Kontakte
/// (Liste mit Filtern, Abruf, Anlage), Rechnungen (Entwurf, Abruf) und Belegliste. Zeichnet jede Anfrage auf; Schlüssel,
/// Drosselung (429) und feste Fehlerantworten sind steuerbar.
/// </summary>
public sealed class FakeLexware : HttpMessageHandler
{
    public const string Key = "test-schluessel-nicht-echt";

    public sealed record LexwareCall(string Method, string Path, string Query, string? Body, string? Authorization, string? Accept);

    public ConcurrentQueue<LexwareCall> Calls { get; } = new();

    public ConcurrentDictionary<string, JsonObject> Contacts { get; } = new();

    public ConcurrentDictionary<string, JsonObject> Invoices { get; } = new();

    /// <summary>Nächste N Aufrufe auf Pfade mit diesem Anfang mit 429 beantworten.</summary>
    public ConcurrentDictionary<string, int> Throttle { get; } = new();

    /// <summary>Pfade mit diesem Anfang antworten mit festem Status und Text.</summary>
    public ConcurrentDictionary<string, (int Status, string Text)> Raw { get; } = new();

    public string CompanyName { get; set; } = "Estrichbau Muster GmbH";

    private int _customerNumber = 10000;

    public IEnumerable<LexwareCall> CallsTo(string method, string pathStart) =>
        Calls.Where(c => c.Method == method && c.Path.StartsWith(pathStart, StringComparison.Ordinal));

    public string AddContact(string name, bool company = true, string? email = null, string? phone = null, string? street = null, string? zip = null,
        string? city = null, string? vatId = null, bool customer = true)
    {
        var id = Guid.NewGuid().ToString();
        var contact = new JsonObject { ["id"] = id, ["version"] = 1, ["archived"] = false };
        contact["roles"] = customer
            ? new JsonObject { ["customer"] = new JsonObject { ["number"] = Interlocked.Increment(ref _customerNumber) } }
            : new JsonObject { ["vendor"] = new JsonObject { ["number"] = 70001 } };
        if (company)
        {
            contact["company"] = new JsonObject { ["name"] = name, ["vatRegistrationId"] = vatId };
        }
        else
        {
            var space = name.LastIndexOf(' ');
            contact["person"] = new JsonObject
            {
                ["salutation"] = "Herr", ["firstName"] = space > 0 ? name[..space] : null, ["lastName"] = space > 0 ? name[(space + 1)..] : name,
            };
        }

        if (street is not null || zip is not null)
        {
            contact["addresses"] = new JsonObject
            {
                ["billing"] = new JsonArray(new JsonObject { ["street"] = street, ["zip"] = zip, ["city"] = city, ["countryCode"] = "DE" }),
            };
        }

        if (email is not null)
        {
            contact["emailAddresses"] = new JsonObject { ["business"] = new JsonArray(email) };
        }

        if (phone is not null)
        {
            contact["phoneNumbers"] = new JsonObject { ["business"] = new JsonArray(phone) };
        }

        Contacts[id] = contact;
        return id;
    }

    public string AddInvoice(string status, string? number, string voucherDate, string? dueDate, decimal net, decimal gross, string? contactName = null)
    {
        var id = Guid.NewGuid().ToString();
        Invoices[id] = new JsonObject
        {
            ["id"] = id, ["voucherStatus"] = status, ["voucherNumber"] = number, ["voucherDate"] = voucherDate + "T00:00:00.000+01:00",
            ["dueDate"] = dueDate is null ? null : dueDate + "T00:00:00.000+01:00", ["title"] = "Rechnung",
            ["address"] = new JsonObject { ["name"] = contactName },
            ["totalPrice"] = new JsonObject { ["currency"] = "EUR", ["totalNetAmount"] = net, ["totalGrossAmount"] = gross },
        };
        return id;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        var query = Uri.UnescapeDataString(request.RequestUri.Query.TrimStart('?'));
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var method = request.Method.Method;
        Calls.Enqueue(new LexwareCall(method, path, query, body, request.Headers.Authorization?.ToString(), request.Headers.Accept.ToString()));

        foreach (var (start, _) in Throttle.Where(t => path.StartsWith(t.Key, StringComparison.Ordinal) && t.Value > 0))
        {
            Throttle.AddOrUpdate(start, 0, (_, left) => left - 1);
            var throttled = Json(HttpStatusCode.TooManyRequests, new JsonObject { ["status"] = 429, ["message"] = "Rate limit exceeded" });
            return throttled;
        }

        if (Raw.FirstOrDefault(r => path.StartsWith(r.Key, StringComparison.Ordinal)) is { Key: not null } raw)
        {
            return new HttpResponseMessage((HttpStatusCode)raw.Value.Status) { Content = new StringContent(raw.Value.Text, Encoding.UTF8, "application/json") };
        }

        if (request.Headers.Authorization?.ToString() != "Bearer " + Key)
        {
            return Json(HttpStatusCode.Unauthorized, new JsonObject { ["status"] = 401, ["error"] = "Unauthorized" });
        }

        var parameters = query.Split('&', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0], p => p.Length > 1 ? p[1] : string.Empty, StringComparer.Ordinal);
        switch (method, path)
        {
            case ("GET", "/v1/profile"):
                return Json(HttpStatusCode.OK, new JsonObject { ["organizationId"] = "aa93e8a8-2aa3-470b-b914-caad8a255dd8", ["companyName"] = CompanyName });

            case ("GET", "/v1/contacts"):
            {
                var all = Contacts.Values.Where(c => c["roles"]?["customer"] is not null).OrderBy(c => c["id"]!.GetValue<string>(), StringComparer.Ordinal).ToList();
                if (parameters.TryGetValue("name", out var name))
                {
                    all = all.Where(c => DisplayName(c).Contains(name, StringComparison.OrdinalIgnoreCase)).ToList();
                }

                if (parameters.TryGetValue("email", out var email))
                {
                    all = all.Where(c => c["emailAddresses"]?.ToJsonString().Contains(email, StringComparison.OrdinalIgnoreCase) == true).ToList();
                }

                if (parameters.TryGetValue("number", out var number))
                {
                    all = all.Where(c => c["roles"]!["customer"]!["number"]?.ToJsonString() == number).ToList();
                }

                var page = int.Parse(parameters.GetValueOrDefault("page", "0"), CultureInfo.InvariantCulture);
                var size = int.Parse(parameters.GetValueOrDefault("size", "25"), CultureInfo.InvariantCulture);
                var content = new JsonArray(all.Skip(page * size).Take(size).Select(c => c.DeepClone()).ToArray());
                var totalPages = Math.Max(1, (int)Math.Ceiling(all.Count / (double)size));
                return Json(HttpStatusCode.OK, new JsonObject
                {
                    ["content"] = content, ["first"] = page == 0, ["last"] = page + 1 >= totalPages, ["totalPages"] = totalPages,
                    ["totalElements"] = all.Count, ["numberOfElements"] = content.Count, ["size"] = size, ["number"] = page,
                });
            }

            case ("POST", "/v1/contacts"):
            {
                var contact = JsonNode.Parse(body!)!.AsObject();
                var id = Guid.NewGuid().ToString();
                contact["id"] = id;
                contact["roles"] = new JsonObject { ["customer"] = new JsonObject { ["number"] = Interlocked.Increment(ref _customerNumber) } };
                Contacts[id] = contact;
                return Json(HttpStatusCode.OK, new JsonObject { ["id"] = id, ["resourceUri"] = $"https://api.lexware.io/v1/contacts/{id}", ["version"] = 1 });
            }

            case ("POST", "/v1/invoices"):
            {
                var invoice = JsonNode.Parse(body!)!.AsObject();
                var id = Guid.NewGuid().ToString();
                decimal net = 0, gross = 0;
                foreach (var line in invoice["lineItems"]!.AsArray().OfType<JsonObject>().Where(l => l["unitPrice"] is not null))
                {
                    var amount = line["quantity"]!.GetValue<decimal>() * line["unitPrice"]!["netAmount"]!.GetValue<decimal>();
                    net += amount;
                    gross += amount * (1 + (line["unitPrice"]!["taxRatePercentage"]!.GetValue<decimal>() / 100m));
                }

                invoice["id"] = id;
                invoice["voucherStatus"] = "draft";
                invoice["totalPrice"] = new JsonObject
                {
                    ["currency"] = "EUR", ["totalNetAmount"] = decimal.Round(net, 2), ["totalGrossAmount"] = decimal.Round(gross, 2),
                };
                Invoices[id] = invoice;
                return Json(HttpStatusCode.Created, new JsonObject { ["id"] = id, ["resourceUri"] = $"https://api.lexware.io/v1/invoices/{id}", ["version"] = 0 });
            }

            case ("GET", "/v1/voucherlist"):
            {
                var number = parameters.GetValueOrDefault("voucherNumber");
                var hits = Invoices.Values.Where(i => i["voucherNumber"]?.GetValue<string>() == number).Select(i => (JsonNode)new JsonObject
                {
                    ["id"] = i["id"]!.GetValue<string>(), ["voucherType"] = "invoice", ["voucherStatus"] = i["voucherStatus"]!.GetValue<string>(),
                    ["voucherNumber"] = number, ["voucherDate"] = i["voucherDate"]?.DeepClone(), ["dueDate"] = i["dueDate"]?.DeepClone(),
                    ["contactName"] = i["address"]?["name"]?.DeepClone(), ["totalAmount"] = i["totalPrice"]!["totalGrossAmount"]!.DeepClone(), ["currency"] = "EUR",
                }).ToArray();
                return Json(HttpStatusCode.OK, new JsonObject { ["content"] = new JsonArray(hits), ["last"] = true, ["totalPages"] = 1 });
            }
        }

        if (method == "GET" && path.StartsWith("/v1/contacts/", StringComparison.Ordinal))
        {
            return Contacts.TryGetValue(path["/v1/contacts/".Length..], out var contact) ? Json(HttpStatusCode.OK, contact.DeepClone()) : NotFound();
        }

        if (method == "GET" && path.StartsWith("/v1/invoices/", StringComparison.Ordinal))
        {
            return Invoices.TryGetValue(path["/v1/invoices/".Length..], out var invoice) ? Json(HttpStatusCode.OK, invoice.DeepClone()) : NotFound();
        }

        return NotFound();
    }

    private static string DisplayName(JsonObject contact) =>
        contact["company"]?["name"]?.GetValue<string>()
        ?? $"{contact["person"]?["firstName"]?.GetValue<string>()} {contact["person"]?["lastName"]?.GetValue<string>()}";

    private static HttpResponseMessage NotFound() => Json(HttpStatusCode.NotFound, new JsonObject { ["status"] = 404, ["error"] = "Not Found" });

    private static HttpResponseMessage Json(HttpStatusCode status, JsonNode body) =>
        new(status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
}
