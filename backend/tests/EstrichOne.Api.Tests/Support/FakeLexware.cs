using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace EstrichOne.Api.Tests.Support;

/// <summary>
/// Nachgebildete Public API von Lexware Office als <see cref="HttpMessageHandler"/> (kein echter Aufruf): Profil, Kontakte
/// (Liste mit Filtern, Abruf, Anlage), Belege (Rechnungen, Angebote, Gutschriften, Auftragsbestätigungen: nur Abruf) und
/// Belegliste, dazu das Ändern eines Kontakts (<c>PUT</c> mit <c>version</c>, sonst 409) und das Beleg-PDF (<c>/file</c> sowie
/// <c>/document</c> mit <c>/v1/files</c>; für Entwürfe 406), Artikel (Liste, Abruf, Anlage, Ändern mit <c>version</c>),
/// Zahlungsbedingungen und das Anlegen eines Angebotsentwurfs (<c>POST /v1/quotations</c>, mit Abfrage abgelehnt). Andere
/// schreibende Aufrufe für Belege kennt sie bewusst nicht (404).
/// Zeichnet jede Anfrage auf; Schlüssel, Drosselung (429) und feste Fehlerantworten sind steuerbar.
/// </summary>
public sealed class FakeLexware : HttpMessageHandler
{
    public const string Key = "test-schluessel-nicht-echt";

    public sealed record LexwareCall(string Method, string Path, string Query, string? Body, string? Authorization, string? Accept);

    public ConcurrentQueue<LexwareCall> Calls { get; } = new();

    public ConcurrentDictionary<string, JsonObject> Contacts { get; } = new();

    /// <summary>Belege aller Arten: ID → Beleg (mit <c>voucherType</c> und <c>address.contactId</c>).</summary>
    public ConcurrentDictionary<string, JsonObject> Vouchers { get; } = new();

    /// <summary>Nächste N Aufrufe auf Pfade mit diesem Anfang mit 429 beantworten.</summary>
    public ConcurrentDictionary<string, int> Throttle { get; } = new();

    /// <summary>Pfade mit diesem Anfang antworten mit festem Status und Text.</summary>
    public ConcurrentDictionary<string, (int Status, string Text)> Raw { get; } = new();

    public string CompanyName { get; set; } = "Estrichbau Muster GmbH";

    /// <summary>Angaben im Profil: Kleinunternehmer und Besteuerungsart (<c>null</c> = nicht geliefert).</summary>
    public bool? SmallBusiness { get; set; }

    public string? ProfileTaxType { get; set; }

    /// <summary>
    /// Steuerarten, die diese Organisation beim Anlegen eines Belegs ablehnt, mit genau der Antwort, die ein echtes Konto
    /// geliefert hat (HTTP 400, „Tax type 'constructionService13b' not allowed for this organization“).
    /// </summary>
    public HashSet<string> RejectedTaxTypes { get; } = [];

    /// <summary>Artikel: ID → Artikel wie in <c>/v1/articles</c>.</summary>
    public ConcurrentDictionary<string, JsonObject> Articles { get; } = new();

    /// <summary>Zahlungsbedingungen wie in <c>GET /v1/payment-conditions</c>.</summary>
    public JsonArray PaymentConditions { get; set; } = new(
        new JsonObject
        {
            ["id"] = "pc-14", ["organizationDefault"] = true, ["paymentTermLabelTemplate"] = "Zahlbar innerhalb von 14 Tagen ohne Abzug.", ["paymentTermDuration"] = 14,
        },
        new JsonObject
        {
            ["id"] = "pc-30", ["organizationDefault"] = false, ["paymentTermLabelTemplate"] = "Zahlbar innerhalb von 30 Tagen, bei Zahlung innerhalb von 8 Tagen 2 % Skonto.",
            ["paymentTermDuration"] = 30, ["paymentDiscountConditions"] = new JsonObject { ["discountPercentage"] = 2, ["discountRange"] = 8 },
        });

    /// <summary>Nächste Anlage eines Angebots: angelegt, aber mit diesem Status beantwortet (Antwort geht verloren).</summary>
    public int? QuotationLostAnswer { get; set; }

    public string AddArticle(string title, string type = "SERVICE", string? number = null, decimal net = 10m, decimal tax = 19m, string unit = "m²",
        bool archived = false, string? description = null)
    {
        var id = Guid.NewGuid().ToString();
        Articles[id] = new JsonObject
        {
            ["id"] = id, ["organizationId"] = "aa93e8a8-2aa3-470b-b914-caad8a255dd8", ["createdDate"] = "2026-01-10T09:00:00.000+01:00",
            ["updatedDate"] = "2026-01-10T09:00:00.000+01:00", ["archived"] = archived, ["title"] = title, ["description"] = description, ["type"] = type,
            ["articleNumber"] = number, ["unitName"] = unit, ["note"] = "intern",
            ["price"] = new JsonObject { ["netPrice"] = net, ["grossPrice"] = Math.Round(net * (1 + tax / 100), 2), ["leadingPrice"] = "NET", ["taxRate"] = tax },
            ["version"] = 1,
        };
        return id;
    }

    /// <summary>Artikel wie in der Lexware-Oberfläche ändern: Version steigt.</summary>
    public void EditArticle(string id, Action<JsonObject> change)
    {
        var article = Articles[id];
        lock (article)
        {
            change(article);
            article["version"] = article["version"]!.GetValue<int>() + 1;
        }
    }

    /// <summary>Kennt die nachgebildete API den Pfad <c>/file</c>? Sonst nur den älteren Weg über <c>/document</c>.</summary>
    public bool FileRoute { get; set; } = true;

    /// <summary>Vor dem nächsten <c>PUT</c> auf einen Kontakt ausgeführt (z. B. zwischenzeitliche Änderung in Lexware Office).</summary>
    public Action<JsonObject>? BeforePut { get; set; }

    /// <summary>Jedes <c>PUT</c> auf einen Kontakt mit diesem Status und Text ablehnen.</summary>
    public (int Status, string Text)? PutFailure { get; set; }

    public static byte[] Pdf(string id) => Encoding.ASCII.GetBytes($"%PDF-1.4\n% Beleg {id}\n%%EOF\n");

    /// <summary>Kontakt wie in der Lexware-Oberfläche ändern: Version und Änderungszeitpunkt steigen.</summary>
    public void Edit(string id, Action<JsonObject> change)
    {
        var contact = Contacts[id];
        lock (contact)
        {
            change(contact);
            contact["version"] = contact["version"]!.GetValue<int>() + 1;
            contact["updatedDate"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        }
    }

    private int _customerNumber = 10000;
    private int _voucherNumber;

    /// <summary>Gesendete Angebote (Inhalt von <c>POST /v1/quotations</c>).</summary>
    public ConcurrentBag<JsonObject> Quotations { get; } = [];

    /// <summary>Übergebene Dateien (<c>POST /v1/files</c>): ID → Inhalt des Formulars als Text.</summary>
    public ConcurrentDictionary<string, string> Files { get; } = new();

    /// <summary>Offener Betrag je Beleg für <c>GET /v1/payments/{id}</c> und die Belegliste.</summary>
    public ConcurrentDictionary<string, decimal> OpenAmounts { get; } = new();

    public IEnumerable<LexwareCall> CallsTo(string method, string pathStart) =>
        Calls.Where(c => c.Method == method && c.Path.StartsWith(pathStart, StringComparison.Ordinal));

    public string AddContact(string name, bool company = true, string? email = null, string? phone = null, string? street = null, string? zip = null,
        string? city = null, string? vatId = null, bool customer = true)
    {
        var id = Guid.NewGuid().ToString();
        var contact = new JsonObject
        {
            ["id"] = id, ["organizationId"] = "aa93e8a8-2aa3-470b-b914-caad8a255dd8", ["version"] = 1, ["archived"] = false,
            ["createdDate"] = "2026-01-10T09:00:00.000+01:00", ["updatedDate"] = "2026-01-10T09:00:00.000+01:00", ["note"] = "Stammkunde",
        };
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

    public string AddVoucher(string type, string status, string? number, string voucherDate, string? dueDate, decimal net, decimal gross,
        string? contactId = null, string? contactName = null, string? title = null, string? introduction = null)
    {
        var id = Guid.NewGuid().ToString();
        Vouchers[id] = new JsonObject
        {
            ["id"] = id, ["voucherType"] = type, ["voucherStatus"] = status, ["voucherNumber"] = number, ["voucherDate"] = voucherDate + "T00:00:00.000+01:00",
            [type == "quotation" ? "expirationDate" : "dueDate"] = dueDate is null ? null : dueDate + "T00:00:00.000+01:00",
            ["title"] = title ?? (type == "quotation" ? "Angebot" : "Rechnung"), ["introduction"] = introduction,
            ["address"] = new JsonObject { ["contactId"] = contactId, ["name"] = contactName },
            ["totalPrice"] = new JsonObject { ["currency"] = "EUR", ["totalNetAmount"] = net, ["totalGrossAmount"] = gross },
        };
        return id;
    }

    private static readonly Dictionary<string, string> Segments = new(StringComparer.Ordinal)
    {
        ["invoices"] = "invoice", ["quotations"] = "quotation", ["credit-notes"] = "creditnote", ["order-confirmations"] = "orderconfirmation",
        ["delivery-notes"] = "deliverynote",
    };

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
            {
                var profile = new JsonObject { ["organizationId"] = "aa93e8a8-2aa3-470b-b914-caad8a255dd8", ["companyName"] = CompanyName };
                if (SmallBusiness is { } small)
                {
                    profile["smallBusiness"] = small;
                }

                if (ProfileTaxType is { } taxType)
                {
                    profile["taxType"] = taxType;
                }

                return Json(HttpStatusCode.OK, profile);
            }

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

            case ("POST", "/v1/files"):
            {
                // Belegeingang: multipart/form-data mit „file“ und „type=voucher“.
                if (request.Content is not MultipartFormDataContent || body is null || !body.Contains("name=type", StringComparison.Ordinal)
                    || !body.Contains("voucher", StringComparison.Ordinal))
                {
                    return Json(HttpStatusCode.BadRequest, new JsonObject { ["status"] = 400, ["message"] = "type must be voucher" });
                }

                var fileId = Guid.NewGuid().ToString();
                Files[fileId] = body;
                return Json(HttpStatusCode.Accepted, new JsonObject { ["id"] = fileId });
            }

            case ("POST", "/v1/contacts"):
            {
                var contact = JsonNode.Parse(body!)!.AsObject();
                var id = Guid.NewGuid().ToString();
                contact["id"] = id;
                contact["version"] = 1;
                contact["archived"] = false;
                contact["roles"] = new JsonObject { ["customer"] = new JsonObject { ["number"] = Interlocked.Increment(ref _customerNumber) } };
                Contacts[id] = contact;
                return Json(HttpStatusCode.OK, new JsonObject { ["id"] = id, ["resourceUri"] = $"https://api.lexware.io/v1/contacts/{id}", ["version"] = 1 });
            }

            case ("GET", "/v1/articles"):
            {
                var all = Articles.Values.OrderBy(a => a["id"]!.GetValue<string>(), StringComparer.Ordinal).ToList();
                var page = int.Parse(parameters.GetValueOrDefault("page", "0"), CultureInfo.InvariantCulture);
                var size = int.Parse(parameters.GetValueOrDefault("size", "25"), CultureInfo.InvariantCulture);
                var content = new JsonArray(all.Skip(page * size).Take(size).Select(a => a.DeepClone()).ToArray());
                var totalPages = Math.Max(1, (int)Math.Ceiling(all.Count / (double)size));
                return Json(HttpStatusCode.OK, new JsonObject
                {
                    ["content"] = content, ["first"] = page == 0, ["last"] = page + 1 >= totalPages, ["totalPages"] = totalPages, ["totalElements"] = all.Count,
                });
            }

            case ("POST", "/v1/articles"):
            {
                var article = JsonNode.Parse(body!)!.AsObject();
                var id = Guid.NewGuid().ToString();
                article["id"] = id;
                article["version"] = 1;
                article["archived"] = false;
                Articles[id] = article;
                return Json(HttpStatusCode.OK, new JsonObject { ["id"] = id, ["resourceUri"] = $"https://api.lexware.io/v1/articles/{id}", ["version"] = 1 });
            }

            case ("GET", "/v1/payment-conditions"):
                return Json(HttpStatusCode.OK, PaymentConditions.DeepClone());

            case ("POST", "/v1/quotations"):
            case ("POST", "/v1/order-confirmations"):
            case ("POST", "/v1/invoices"):
            case ("POST", "/v1/credit-notes"):
            {
                // Erlaubt sind nur finalize=true und precedingSalesVoucherId (wie die echte API, so weit bekannt).
                if (parameters.Keys.Any(k => k is not ("finalize" or "precedingSalesVoucherId")) || (parameters.TryGetValue("finalize", out var fin) && fin != "true"))
                {
                    return Json(HttpStatusCode.BadRequest, new JsonObject { ["status"] = 400, ["message"] = "Unexpected query" });
                }

                var voucherType = Segments[path["/v1/".Length..]];
                var sent = JsonNode.Parse(body!)!.AsObject();
                if (sent["taxConditions"]?["taxType"]?.GetValue<string>() is { } requested && RejectedTaxTypes.Contains(requested))
                {
                    return Json(HttpStatusCode.BadRequest, new JsonObject
                    {
                        ["requestId"] = Guid.NewGuid().ToString(), ["status"] = 400, ["error"] = "Bad Request", ["path"] = path,
                        ["timestamp"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                        ["message"] = $"Tax type '{requested}' not allowed for this organization",
                    });
                }

                if (voucherType == "invoice" && sent["shippingConditions"] is null)
                {
                    return Json(HttpStatusCode.NotAcceptable, new JsonObject
                    {
                        ["status"] = 406, ["message"] = "Validation failed",
                        ["details"] = new JsonArray(new JsonObject { ["field"] = "shippingConditions", ["violation"] = "NOTNULL", ["message"] = "must not be null" }),
                    });
                }

                if (parameters.TryGetValue("precedingSalesVoucherId", out var precedingId) && !Vouchers.ContainsKey(precedingId))
                {
                    return Json(HttpStatusCode.NotAcceptable, new JsonObject { ["status"] = 406, ["message"] = "Preceding sales voucher not found" });
                }

                var id = Guid.NewGuid().ToString();
                var net = 0m;
                var gross = 0m;
                foreach (var line in (sent["lineItems"] as JsonArray ?? []).OfType<JsonObject>().Where(l => l["type"]!.GetValue<string>() != "text"))
                {
                    var amount = Math.Round(line["quantity"]!.GetValue<decimal>() * line["unitPrice"]!["netAmount"]!.GetValue<decimal>()
                        * (1 - (line["discountPercentage"]?.GetValue<decimal>() ?? 0) / 100), 2);
                    net += amount;
                    gross += Math.Round(amount * (1 + line["unitPrice"]!["taxRatePercentage"]!.GetValue<decimal>() / 100), 2);
                }

                var finalized = parameters.ContainsKey("finalize");
                var voucher = (JsonObject)sent.DeepClone();
                voucher["id"] = id;
                voucher["voucherType"] = voucherType;
                voucher["voucherStatus"] = finalized ? "open" : "draft";
                voucher["voucherNumber"] = finalized ? $"{voucherType[..2].ToUpperInvariant()}-{Interlocked.Increment(ref _voucherNumber).ToString(CultureInfo.InvariantCulture)}" : null;
                voucher["totalPrice"] = new JsonObject { ["currency"] = "EUR", ["totalNetAmount"] = net, ["totalGrossAmount"] = gross };
                voucher["precedingSalesVoucherId"] = precedingId;
                Vouchers[id] = voucher;
                if (voucherType == "quotation")
                {
                    Quotations.Add(sent);
                }

                if (QuotationLostAnswer is { } lost)
                {
                    QuotationLostAnswer = null;
                    return Json((HttpStatusCode)lost, new JsonObject { ["status"] = lost, ["message"] = "Gateway Timeout" });
                }

                return Json(HttpStatusCode.OK, new JsonObject
                {
                    ["id"] = id, ["resourceUri"] = $"https://api.lexware.io{path}/{id}", ["createdDate"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    ["updatedDate"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture), ["version"] = 1,
                });
            }

            case ("GET", "/v1/voucherlist"):
            {
                var types = parameters.GetValueOrDefault("voucherType", string.Empty).Split(',');
                var hits = Vouchers.Values.Where(v => types.Contains(v["voucherType"]!.GetValue<string>()))
                    .Where(v => !parameters.TryGetValue("voucherNumber", out var number) || v["voucherNumber"]?.GetValue<string>() == number)
                    .Where(v => !parameters.TryGetValue("contactId", out var contact) || v["address"]?["contactId"]?.GetValue<string>() == contact)
                    .OrderByDescending(v => v["voucherDate"]!.GetValue<string>(), StringComparer.Ordinal)
                    .Select(v => (JsonNode)new JsonObject
                    {
                        ["id"] = v["id"]!.GetValue<string>(), ["voucherType"] = v["voucherType"]!.GetValue<string>(),
                        ["voucherStatus"] = v["voucherStatus"]!.GetValue<string>(), ["voucherNumber"] = v["voucherNumber"]?.DeepClone(),
                        ["voucherDate"] = v["voucherDate"]?.DeepClone(), ["dueDate"] = (v["dueDate"] ?? v["expirationDate"])?.DeepClone(),
                        ["contactId"] = v["address"]?["contactId"]?.DeepClone(), ["contactName"] = v["address"]?["name"]?.DeepClone(),
                        ["totalAmount"] = v["totalPrice"]!["totalGrossAmount"]!.DeepClone(), ["currency"] = "EUR",
                        ["openAmount"] = OpenAmounts.TryGetValue(v["id"]!.GetValue<string>(), out var openAmount) ? openAmount : v["totalPrice"]!["totalGrossAmount"]!.DeepClone(),
                    }).ToArray();
                return Json(HttpStatusCode.OK, new JsonObject { ["content"] = new JsonArray(hits), ["last"] = true, ["totalPages"] = 1 });
            }
        }

        if (method == "GET" && path.StartsWith("/v1/payments/", StringComparison.Ordinal))
        {
            var voucherId = path["/v1/payments/".Length..];
            if (!Vouchers.TryGetValue(voucherId, out var paid))
            {
                return NotFound();
            }

            var open = OpenAmounts.TryGetValue(voucherId, out var amount) ? amount : paid["totalPrice"]!["totalGrossAmount"]!.GetValue<decimal>();
            return Json(HttpStatusCode.OK, new JsonObject
            {
                ["openAmount"] = open, ["currency"] = "EUR", ["paymentStatus"] = open == 0 ? "balanced" : "openRevenue", ["voucherType"] = paid["voucherType"]!.DeepClone(),
                ["voucherStatus"] = paid["voucherStatus"]!.DeepClone(),
            });
        }

        if (method == "GET" && path.StartsWith("/v1/articles/", StringComparison.Ordinal))
        {
            return Articles.TryGetValue(path["/v1/articles/".Length..], out var article) ? Json(HttpStatusCode.OK, article.DeepClone()) : NotFound();
        }

        if (method == "PUT" && path.StartsWith("/v1/articles/", StringComparison.Ordinal))
        {
            if (!Articles.TryGetValue(path["/v1/articles/".Length..], out var stored))
            {
                return NotFound();
            }

            var sent = JsonNode.Parse(body!)!.AsObject();
            var version = stored["version"]!.GetValue<int>();
            if (sent["version"]?.GetValue<int>() != version)
            {
                return Json(HttpStatusCode.Conflict, new JsonObject { ["status"] = 409, ["message"] = "Resource was modified" });
            }

            sent["id"] = stored["id"]!.DeepClone();
            sent["archived"] = stored["archived"]?.DeepClone();
            sent["version"] = version + 1;
            Articles[path["/v1/articles/".Length..]] = sent;
            return Json(HttpStatusCode.OK, new JsonObject { ["id"] = sent["id"]!.DeepClone(), ["version"] = version + 1 });
        }

        if (method == "GET" && path.StartsWith("/v1/contacts/", StringComparison.Ordinal))
        {
            return Contacts.TryGetValue(path["/v1/contacts/".Length..], out var contact) ? Json(HttpStatusCode.OK, contact.DeepClone()) : NotFound();
        }

        if (method == "PUT" && path.StartsWith("/v1/contacts/", StringComparison.Ordinal))
        {
            if (!Contacts.TryGetValue(path["/v1/contacts/".Length..], out var stored))
            {
                return NotFound();
            }

            if (PutFailure is { } failure)
            {
                return new HttpResponseMessage((HttpStatusCode)failure.Status) { Content = new StringContent(failure.Text, Encoding.UTF8, "application/json") };
            }

            var before = BeforePut;
            BeforePut = null;
            before?.Invoke(stored);
            var sent = JsonNode.Parse(body!)!.AsObject();
            var version = stored["version"]!.GetValue<int>();
            if (sent["version"]?.GetValue<int>() != version)
            {
                return Json(HttpStatusCode.Conflict, new JsonObject { ["status"] = 409, ["message"] = "Resource was modified" });
            }

            // Wie die echte API: Der gesendete Kontakt ersetzt den gespeicherten; ID, Organisation und Zeitpunkte führt der Server.
            sent["id"] = stored["id"]!.DeepClone();
            sent["organizationId"] = stored["organizationId"]?.DeepClone();
            sent["createdDate"] = stored["createdDate"]?.DeepClone();
            sent["archived"] = stored["archived"]?.DeepClone();
            sent["version"] = version + 1;
            sent["updatedDate"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            Contacts[path["/v1/contacts/".Length..]] = sent;
            return Json(HttpStatusCode.OK, new JsonObject
            {
                ["id"] = sent["id"]!.DeepClone(), ["resourceUri"] = $"https://api.lexware.io{path}", ["createdDate"] = sent["createdDate"]?.DeepClone(),
                ["updatedDate"] = sent["updatedDate"]!.DeepClone(), ["version"] = version + 1,
            });
        }

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (method == "GET" && parts.Length == 4 && Segments.ContainsKey(parts[1]) && parts[3] is "file" or "document")
        {
            if (parts[3] == "file" && !FileRoute)
            {
                return NotFound();
            }

            if (!Vouchers.TryGetValue(parts[2], out var owner) || owner["voucherType"]!.GetValue<string>() != Segments[parts[1]])
            {
                return NotFound();
            }

            if (owner["voucherStatus"]!.GetValue<string>() == "draft")
            {
                return Json(HttpStatusCode.NotAcceptable, new JsonObject { ["status"] = 406, ["message"] = "No document for drafts" });
            }

            return parts[3] == "file"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Pdf(parts[2])) { Headers = { ContentType = new("application/pdf") } } }
                : Json(HttpStatusCode.OK, new JsonObject { ["documentFileId"] = "file-" + parts[2] });
        }

        if (method == "GET" && parts.Length == 3 && parts[1] == "files" && parts[2].StartsWith("file-", StringComparison.Ordinal))
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Pdf(parts[2]["file-".Length..])) { Headers = { ContentType = new("application/pdf") } },
            };
        }

        if (method == "GET" && parts.Length == 3 && Segments.TryGetValue(parts[1], out var type))
        {
            return Vouchers.TryGetValue(parts[2], out var voucher) && voucher["voucherType"]!.GetValue<string>() == type
                ? Json(HttpStatusCode.OK, voucher.DeepClone()) : NotFound();
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
