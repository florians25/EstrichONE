using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Materials.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Lexware;

/// <summary>Eingangsbeleg der Übersicht (aus der Belegliste von Lexware Office).</summary>
/// <param name="Type"><c>purchaseinvoice</c> oder <c>purchasecreditnote</c>.</param>
/// <param name="Status">open, overdue, paid, paidoff, voided, draft (wie in Lexware Office).</param>
/// <param name="SupplierId">Verknüpfter Lieferant im Materialstamm (von Hand oder gleicher Name).</param>
public sealed record PurchaseListItem(
    string LexwareId, string Type, string TypeName, string? Number, DateOnly? VoucherDate, DateOnly? DueDate, decimal? Gross, decimal? OpenAmount,
    string Status, string? ContactName, string? LexwareContactId, Guid? SupplierId, string? SupplierName, bool SupplierLinkedByHand, Guid? BauakteId,
    string? BauakteNumber, string? BauakteTitle, string? LexwareUrl);

/// <summary>Hochgeladener Eingangsbeleg in Kurzform.</summary>
/// <param name="State">uploaded, sending, sent oder failed.</param>
/// <param name="Format">xrechnung-ubl, xrechnung-cii, zugferd oder none.</param>
public sealed record PurchaseDocumentItem(
    Guid Id, string FileName, string ContentType, long SizeBytes, string Format, string FormatName, string? SupplierName, string? InvoiceNumber,
    DateOnly? InvoiceDate, decimal? GrossAmount, Guid? BauakteId, string? BauakteNumber, string? BauakteTitle, string State, string? SendError,
    DateTimeOffset UploadedAt, string? UploadedBy, DateTimeOffset? SentAt, int ProblemCount, string Source);

/// <summary>Hochgeladener Eingangsbeleg mit den Daten der E-Rechnung und der Prüfung der Pflichtangaben.</summary>
public sealed record PurchaseDocumentResponse(PurchaseDocumentItem Document, string? Note, EInvoiceData Data, bool CanEdit, bool Duplicate = false);

/// <param name="LexwareError">Lexware Office war nicht erreichbar: gezeigt werden die hochgeladenen Belege.</param>
public sealed record PurchaseListResponse(
    IReadOnlyList<PurchaseListItem> Items, IReadOnlyList<PurchaseDocumentItem> Documents, IReadOnlyList<SupplierSummary> Suppliers, bool CanEdit,
    DateTimeOffset LoadedAt, string? LexwareError);

public sealed record UpdatePurchaseDocumentRequest(Guid? BauakteId, [property: MaxLength(1000)] string? Note);

public sealed record AssignPurchaseVoucherRequest(Guid? BauakteId);

public sealed record LinkPurchaseSupplierRequest(Guid? SupplierId);

/// <summary>Erlöse, Kosten und Differenz der Bauakte (brutto laut Lexware Office).</summary>
/// <param name="Revenue">Zugeordnete Rechnungen abzüglich Gutschriften (ohne Entwürfe und stornierte).</param>
/// <param name="Costs">Zugeordnete Eingangsrechnungen abzüglich Gutschriften der Lieferanten (ohne stornierte).</param>
public sealed record BauakteCostsResponse(
    Guid BauakteId, decimal Revenue, decimal Costs, decimal Difference, string Text, IReadOnlyList<PurchaseListItem> Vouchers,
    IReadOnlyList<PurchaseDocumentItem> Documents, bool CanEdit);

/// <summary>
/// Modul „Eingangsbelege“: Eingangsrechnungen und Gutschriften von Lieferanten aus Lexware Office (nur lesend), Zuordnung zu
/// Bauakten (Kosten der Bauakte), Verknüpfung mit Lieferanten des Materialstamms und Belegeingang: Dateien hochladen (PDF,
/// Foto, XRechnung, ZUGFeRD), E-Rechnung lesen und prüfen, an Lexware Office übergeben (<c>POST /v1/files</c>, einmalig).
/// </summary>
internal static class PurchaseEndpoints
{
    private const string Tag = "Eingangsbelege";
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public static void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/purchases").WithTags(Tag).RequireModule(AppModules.Lexware).RequireModule(AppModules.Purchases)
            .RequireAnyPermission(Permissions.PurchasesRead, Permissions.PurchasesEdit).AddEndpointFilter(LexwareEndpoints.GuardAsync);
        group.MapGet("/vouchers", ListAsync).WithName("ListPurchaseVouchers")
            .WithSummary("Eingangsbelege aus Lexware Office (Belegliste) mit Bauakte und Lieferant, dazu die hochgeladenen Belege");
        group.MapPut("/vouchers/{lexwareId}/bauakte", AssignAsync).WithName("AssignPurchaseVoucher")
            .WithSummary("Eingangsbeleg einer Bauakte zuordnen oder die Zuordnung lösen (ohne bauakteId)")
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPut("/suppliers/{lexwareContactId}", LinkSupplierAsync).WithName("LinkPurchaseSupplier")
            .WithSummary("Lieferant aus Lexware Office mit einem Lieferanten des Materialstamms verknüpfen (ohne supplierId lösen)")
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/documents", UploadAsync).WithName("UploadPurchaseDocument")
            .WithSummary("Eingangsbeleg hochladen: Inhalt der Datei als Rohdaten im Body (PDF, JPEG, PNG, XRechnung als XML; höchstens 5 MB). Gleiche Datei ein zweites Mal liefert den vorhandenen Beleg")
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge).ProducesProblem(StatusCodes.Status415UnsupportedMediaType);
        group.MapGet("/documents/{id:guid}", GetAsync).WithName("GetPurchaseDocument")
            .WithSummary("Hochgeladener Eingangsbeleg mit den Daten der E-Rechnung und der Prüfung der Pflichtangaben")
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("/documents/{id:guid}/file", FileAsync).WithName("GetPurchaseDocumentFile")
            .WithSummary("Datei des hochgeladenen Eingangsbelegs").ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPut("/documents/{id:guid}", UpdateAsync).WithName("UpdatePurchaseDocument")
            .WithSummary("Bauakte und Notiz des hochgeladenen Eingangsbelegs ändern")
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound);
        group.MapDelete("/documents/{id:guid}", DeleteAsync).WithName("DiscardPurchaseDocument")
            .WithSummary("Hochgeladenen Eingangsbeleg verwerfen (nur vor der Übergabe an Lexware Office)")
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPost("/documents/{id:guid}/send", SendAsync).WithName("SendPurchaseDocumentToLexware")
            .WithSummary("An Lexware Office übergeben (POST /v1/files, type voucher). Ein übergebener Beleg wird nicht ein zweites Mal gesendet")
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status424FailedDependency).ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        group.MapGet("/bauakten/{id:guid}/costs", CostsAsync).WithName("GetBauakteCosts")
            .WithSummary("Erlöse, Kosten und Differenz der Bauakte mit den zugeordneten und hochgeladenen Eingangsbelegen")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    // ---------- Abbildung ----------

    internal static string TypeName(string type) => type == LexwareVoucherTypes.PurchaseCreditNote ? "Gutschrift (Lieferant)" : "Eingangsrechnung";

    internal static string FormatName(string format) => format switch
    {
        "xrechnung-ubl" => "XRechnung (UBL)",
        "xrechnung-cii" => "XRechnung (CII)",
        "zugferd" => "ZUGFeRD",
        _ => "Datei ohne E-Rechnung",
    };

    private static string StateKey(PurchaseDocumentState state) => state switch
    {
        PurchaseDocumentState.Sending => "sending",
        PurchaseDocumentState.Sent => "sent",
        PurchaseDocumentState.Failed => "failed",
        _ => "uploaded",
    };

    internal static string Money(decimal value) => value.ToString("#,##0.00", German) + " €";

    private static EInvoiceData Data(PurchaseDocument d)
    {
        try
        {
            return JsonSerializer.Deserialize<EInvoiceData>(d.DataJson, JsonSerializerOptions.Web) ?? EInvoiceData.None();
        }
        catch (JsonException)
        {
            return EInvoiceData.None();
        }
    }

    private static async Task<List<PurchaseDocumentItem>> DocumentItemsAsync(LexwareService lexware, IBauakteAccess bauakten, IQueryable<PurchaseDocument> query,
        CancellationToken cancellationToken)
    {
        var db = lexware.Db;
        var docs = await query.AsNoTracking().OrderByDescending(d => d.UploadedAt).Take(500)
            .Select(d => new
            {
                d.Id, d.FileName, d.ContentType, d.SizeBytes, d.Format, d.SupplierName, d.InvoiceNumber, d.InvoiceDate, d.GrossAmount, d.BauakteId, d.State,
                d.SendError, d.UploadedAt, d.UploadedByUserId, d.SentAt, d.DataJson, d.Source,
            }).ToListAsync(cancellationToken);
        var bauakteIds = docs.Select(d => d.BauakteId).OfType<Guid>().Distinct().ToList();
        var info = await db.Set<Bauakte>().AsNoTracking().Where(b => bauakteIds.Contains(b.Id)).Select(b => new { b.Id, b.Number, b.Title })
            .ToDictionaryAsync(b => b.Id, cancellationToken);
        var visible = new Dictionary<Guid, bool>();
        foreach (var id in bauakteIds)
        {
            visible[id] = await bauakten.CanReadAsync(id, cancellationToken);
        }

        var userIds = docs.Select(d => d.UploadedByUserId).Distinct().ToList();
        var users = await db.Set<User>().AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
        return docs.Where(d => d.BauakteId is not { } b || visible[b]).Select(d =>
        {
            var bauakte = d.BauakteId is { } b && info.TryGetValue(b, out var x) ? x : null;
            var problems = 0;
            try
            {
                problems = JsonSerializer.Deserialize<EInvoiceData>(d.DataJson, JsonSerializerOptions.Web)?.Problems.Count ?? 0;
            }
            catch (JsonException)
            {
            }

            return new PurchaseDocumentItem(d.Id, d.FileName, d.ContentType, d.SizeBytes, d.Format, FormatName(d.Format), d.SupplierName, d.InvoiceNumber,
                d.InvoiceDate, d.GrossAmount, d.BauakteId, bauakte?.Number, bauakte?.Title, StateKey(d.State), d.SendError, d.UploadedAt,
                users.GetValueOrDefault(d.UploadedByUserId), d.SentAt, problems, d.Source);
        }).ToList();
    }

    private static string Normalize(string? name) => new string((name ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private static async Task<List<PurchaseListItem>> VoucherItemsAsync(LexwareService lexware, LexwareSettingsRow row, IBauakteAccess bauakten,
        IReadOnlyList<LexwareListVoucher> remote, IReadOnlyList<SupplierSummary> suppliers, CancellationToken cancellationToken)
    {
        var db = lexware.Db;
        var ids = remote.Select(r => r.Id).ToList();
        var links = await db.Set<PurchaseVoucherLink>().AsNoTracking().Where(l => ids.Contains(l.LexwareId)).ToDictionaryAsync(l => l.LexwareId, cancellationToken);
        var bauakteIds = links.Values.Select(l => l.BauakteId).Distinct().ToList();
        var info = await db.Set<Bauakte>().AsNoTracking().Where(b => bauakteIds.Contains(b.Id)).Select(b => new { b.Id, b.Number, b.Title })
            .ToDictionaryAsync(b => b.Id, cancellationToken);
        var visible = new Dictionary<Guid, bool>();
        foreach (var id in bauakteIds)
        {
            visible[id] = await bauakten.CanReadAsync(id, cancellationToken);
        }

        var byHand = await db.Set<PurchaseSupplierLink>().AsNoTracking().ToDictionaryAsync(l => l.LexwareContactId, l => l.SupplierId, cancellationToken);
        var supplierById = suppliers.ToDictionary(s => s.Id);
        var byName = suppliers.GroupBy(s => Normalize(s.Name)).Where(g => g.Key.Length > 0 && g.Count() == 1).ToDictionary(g => g.Key, g => g.Single());
        var items = new List<PurchaseListItem>();
        foreach (var v in remote)
        {
            links.TryGetValue(v.Id, out var link);
            if (link is not null && !visible[link.BauakteId])
            {
                continue;
            }

            var bauakte = link is not null && info.TryGetValue(link.BauakteId, out var b) ? b : null;
            SupplierSummary? supplier = null;
            var hand = v.ContactId is { } contact && byHand.TryGetValue(contact, out var linked) && supplierById.TryGetValue(linked, out supplier);
            if (!hand)
            {
                byName.TryGetValue(Normalize(v.ContactName), out supplier);
            }

            var status = lexware.DisplayStatus(v.Type, v.Status, v.DueDate);
            items.Add(new PurchaseListItem(v.Id, v.Type, TypeName(v.Type), v.Number, v.VoucherDate, v.DueDate, v.GrossAmount, v.OpenAmount, status, v.ContactName,
                v.ContactId, supplier?.Id, supplier?.Name, hand, link?.BauakteId, bauakte?.Number, bauakte?.Title, lexware.VoucherUrl(row, v.Type, v.Id, status)));
        }

        return items;
    }

    private static PurchaseListItem LinkItem(LexwareService lexware, LexwareSettingsRow row, PurchaseVoucherLink l, string? number, string? title) =>
        new(l.LexwareId, l.VoucherType, TypeName(l.VoucherType), l.Number, l.VoucherDate, null, l.GrossAmount, null, l.Status ?? "open", l.ContactName,
            l.LexwareContactId, null, null, false, l.BauakteId, number, title, lexware.VoucherUrl(row, l.VoucherType, l.LexwareId, l.Status ?? "open"));

    private static async Task<bool> CanEditAsync(IUserPermissions permissions, CancellationToken cancellationToken) =>
        (await permissions.GetAsync(cancellationToken)).Contains(Permissions.PurchasesEdit);

    private static ProblemHttpResult Denied() => TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Keine Berechtigung",
        detail: "Ihnen fehlt das Recht „Eingangsbelege hochladen, an Lexware Office weitergeben und Bauakten zuordnen“.", extensions: Code("PERMISSION_DENIED"));

    private static ProblemHttpResult NotFound(string what) => TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Nicht gefunden",
        detail: what, extensions: Code("NOT_FOUND"));

    private static Dictionary<string, object?> Code(string code) => new() { ["code"] = code };

    private static async Task<ProblemHttpResult?> BauakteProblemAsync(LexwareService lexware, IBauakteAccess bauakten, Guid? bauakteId,
        CancellationToken cancellationToken) =>
        bauakteId is { } id && (!await lexware.Db.Set<Bauakte>().AnyAsync(b => b.Id == id, cancellationToken) || !await bauakten.CanReadAsync(id, cancellationToken))
            ? NotFound("Die Bauakte gibt es nicht oder Sie dürfen sie nicht sehen.")
            : null;

    // ---------- Übersicht ----------

    private static async Task<Ok<PurchaseListResponse>> ListAsync(LexwareService lexware, IBauakteAccess bauakten, IMaterialDirectory materials,
        IUserPermissions permissions, CancellationToken cancellationToken, [FromQuery] bool refresh = false)
    {
        var row = await lexware.SettingsAsync(cancellationToken);
        var suppliers = await materials.SuppliersAsync(cancellationToken);
        IReadOnlyList<LexwareListVoucher> remote = [];
        string? error = null;
        if (row.Configured)
        {
            try
            {
                remote = await lexware.SalesListAsync(row, LexwareVoucherTypes.Purchase, refresh, cancellationToken);
            }
            catch (LexwareException exception)
            {
                error = exception.Message;
            }
        }
        else
        {
            error = LexwareService.NotConfigured().Message;
        }

        var items = await VoucherItemsAsync(lexware, row, bauakten, remote, suppliers, cancellationToken);
        var documents = await DocumentItemsAsync(lexware, bauakten, lexware.Db.Set<PurchaseDocument>(), cancellationToken);
        return TypedResults.Ok(new PurchaseListResponse(items, documents, suppliers, await CanEditAsync(permissions, cancellationToken), lexware.Clock.GetUtcNow(),
            error));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> AssignAsync(string lexwareId, AssignPurchaseVoucherRequest request, LexwareService lexware,
        IBauakteAccess bauakten, IUserPermissions permissions, ICurrentUser currentUser, IAuditLog audit, CancellationToken cancellationToken)
    {
        if (!await CanEditAsync(permissions, cancellationToken))
        {
            return Denied();
        }

        var db = lexware.Db;
        var link = await db.Set<PurchaseVoucherLink>().SingleOrDefaultAsync(l => l.LexwareId == lexwareId, cancellationToken);
        if (link is not null && !await bauakten.CanReadAsync(link.BauakteId, cancellationToken))
        {
            return NotFound("Der Eingangsbeleg ist einer Bauakte zugeordnet, die Sie nicht sehen dürfen.");
        }

        if (request.BauakteId is not { } bauakteId)
        {
            if (link is not null)
            {
                db.Remove(link);
                audit.Record("bauakte.purchase_unassigned", currentUser.UserId, "bauakte", link.BauakteId.ToString(),
                    new Dictionary<string, string?> { ["number"] = link.Number, ["supplier"] = link.ContactName });
                await db.SaveChangesAsync(cancellationToken);
            }

            return TypedResults.NoContent();
        }

        if (await BauakteProblemAsync(lexware, bauakten, bauakteId, cancellationToken) is { } problem)
        {
            return problem;
        }

        var row = await lexware.SettingsAsync(cancellationToken);
        var voucher = (await lexware.SalesListAsync(row, LexwareVoucherTypes.Purchase, false, cancellationToken)).FirstOrDefault(v => v.Id == lexwareId)
            ?? (await lexware.SalesListAsync(row, LexwareVoucherTypes.Purchase, true, cancellationToken)).FirstOrDefault(v => v.Id == lexwareId);
        if (voucher is null)
        {
            return NotFound("Den Eingangsbeleg gibt es in Lexware Office nicht (mehr).");
        }

        link ??= db.Add(new PurchaseVoucherLink { Id = Guid.CreateVersion7(), LexwareId = lexwareId }).Entity;
        link.BauakteId = bauakteId;
        link.VoucherType = voucher.Type;
        link.Number = voucher.Number;
        link.VoucherDate = voucher.VoucherDate;
        link.GrossAmount = voucher.GrossAmount;
        link.ContactName = voucher.ContactName?.Length > 255 ? voucher.ContactName[..255] : voucher.ContactName;
        link.LexwareContactId = voucher.ContactId;
        link.Status = voucher.Status;
        link.AssignedByUserId = currentUser.UserId;
        link.AssignedAt = lexware.Clock.GetUtcNow();
        audit.Record("bauakte.purchase_assigned", currentUser.UserId, "bauakte", bauakteId.ToString(),
            new Dictionary<string, string?> { ["number"] = voucher.Number, ["supplier"] = voucher.ContactName });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> LinkSupplierAsync(string lexwareContactId, LinkPurchaseSupplierRequest request,
        LexwareService lexware, IMaterialDirectory materials, IUserPermissions permissions, CancellationToken cancellationToken)
    {
        if (!await CanEditAsync(permissions, cancellationToken))
        {
            return Denied();
        }

        var db = lexware.Db;
        var link = await db.Set<PurchaseSupplierLink>().SingleOrDefaultAsync(l => l.LexwareContactId == lexwareContactId, cancellationToken);
        if (request.SupplierId is not { } supplierId)
        {
            if (link is not null)
            {
                db.Remove(link);
                await db.SaveChangesAsync(cancellationToken);
            }

            return TypedResults.NoContent();
        }

        if ((await materials.SuppliersAsync(cancellationToken)).All(s => s.Id != supplierId))
        {
            return NotFound("Den Lieferanten gibt es im Materialstamm nicht oder er ist inaktiv.");
        }

        link ??= db.Add(new PurchaseSupplierLink { LexwareContactId = lexwareContactId }).Entity;
        link.SupplierId = supplierId;
        link.LinkedAt = lexware.Clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    // ---------- Belegeingang ----------

    /// <summary>Art der Datei aus dem Inhalt (nicht aus der Endung): PDF, JPEG, PNG oder XML.</summary>
    internal static string? Sniff(byte[] content)
    {
        if (content.Length >= 5 && content[0] == '%' && content[1] == 'P' && content[2] == 'D' && content[3] == 'F')
        {
            return "application/pdf";
        }

        if (content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (content.Length >= 8 && content[0] == 0x89 && content[1] == 'P' && content[2] == 'N' && content[3] == 'G')
        {
            return "image/png";
        }

        var start = content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF ? 3 : 0;
        while (start < content.Length && content[start] is (byte)' ' or (byte)'\r' or (byte)'\n' or (byte)'\t')
        {
            start++;
        }

        return start < content.Length && content[start] == '<' ? "application/xml" : null;
    }

    private static string SafeName(string? name, string contentType)
    {
        var clean = new string((Path.GetFileName(name ?? string.Empty)).Where(c => !char.IsControl(c) && c is not '/' and not '\\' and not '"').ToArray()).Trim();
        if (clean.Length == 0)
        {
            clean = "Eingangsbeleg";
        }

        var extension = contentType switch
        {
            "application/pdf" => ".pdf",
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            _ => ".xml",
        };
        if (!clean.EndsWith(extension, StringComparison.OrdinalIgnoreCase) && !(extension == ".jpg" && clean.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)))
        {
            clean = Path.GetFileNameWithoutExtension(clean) + extension;
        }

        return clean.Length > PurchaseDocument.MaxName ? clean[^PurchaseDocument.MaxName..] : clean;
    }

    private static async Task<Results<Created<PurchaseDocumentResponse>, Ok<PurchaseDocumentResponse>, ProblemHttpResult>> UploadAsync(HttpRequest http,
        LexwareService lexware, IBauakteAccess bauakten, IUserPermissions permissions, ICurrentUser currentUser, IAuditLog audit, CancellationToken cancellationToken,
        [FromQuery] string? fileName = null, [FromQuery] Guid? bauakteId = null, [FromQuery] string? source = null)
    {
        if (!await CanEditAsync(permissions, cancellationToken))
        {
            return Denied();
        }

        if (await BauakteProblemAsync(lexware, bauakten, bauakteId, cancellationToken) is { } problem)
        {
            return problem;
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await http.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > PurchaseDocument.MaxBytes)
            {
                return TypedResults.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "Datei zu groß",
                    detail: "Lexware Office nimmt Belege bis 5 MB an. Verkleinern Sie das Foto oder das PDF und laden Sie es erneut hoch.", extensions: Code("FILE_TOO_LARGE"));
            }

            buffer.Write(chunk, 0, read);
        }

        var content = buffer.ToArray();
        if (content.Length == 0 || Sniff(content) is not { } contentType)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status415UnsupportedMediaType, title: "Dateiart nicht unterstützt",
                detail: "Laden Sie den Beleg als PDF, Foto (JPEG oder PNG) oder als XRechnung (XML) hoch.", extensions: Code("UNSUPPORTED_FILE"));
        }

        var db = lexware.Db;
        var hash = Convert.ToHexStringLower(SHA256.HashData(content));
        var canEdit = true;
        if (await db.Set<PurchaseDocument>().AsNoTracking().SingleOrDefaultAsync(d => d.Sha256 == hash, cancellationToken) is { } existing)
        {
            return TypedResults.Ok(await ResponseAsync(lexware, bauakten, existing, canEdit, cancellationToken) with { Duplicate = true });
        }

        var data = EInvoiceReader.Read(content);
        if (data.Format == "none" && contentType != "application/xml")
        {
            // Fotos und PDF ohne eingebettete Rechnung: keine Prüfung der Pflichtangaben (kein strukturierter Inhalt).
            data = data with { Problems = [] };
        }

        var document = new PurchaseDocument
        {
            Id = Guid.CreateVersion7(), FileName = SafeName(fileName, contentType), ContentType = contentType, SizeBytes = content.Length, Sha256 = hash,
            Content = content, BauakteId = bauakteId, Format = data.Format, DataJson = JsonSerializer.Serialize(data, JsonSerializerOptions.Web),
            SupplierName = Cut(data.SellerName, 255), InvoiceNumber = Cut(data.Number, 100), InvoiceDate = data.IssueDate, GrossAmount = data.GrossTotal,
            Source = source == "app" ? "app" : "web", UploadedByUserId = currentUser.UserId, UploadedAt = lexware.Clock.GetUtcNow(),
        };
        db.Add(document);
        if (bauakteId is { } b)
        {
            audit.Record("bauakte.purchase_uploaded", currentUser.UserId, "bauakte", b.ToString(),
                new Dictionary<string, string?> { ["file"] = document.FileName, ["supplier"] = document.SupplierName });
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/v1/purchases/documents/{document.Id}", await ResponseAsync(lexware, bauakten, document, canEdit, cancellationToken));
    }

    private static string? Cut(string? value, int max) => value is null ? null : value.Length > max ? value[..max] : value;

    private static async Task<PurchaseDocumentResponse> ResponseAsync(LexwareService lexware, IBauakteAccess bauakten, PurchaseDocument document, bool canEdit,
        CancellationToken cancellationToken)
    {
        var item = (await DocumentItemsAsync(lexware, bauakten, lexware.Db.Set<PurchaseDocument>().Where(d => d.Id == document.Id), cancellationToken)).Single();
        return new PurchaseDocumentResponse(item, document.Note, Data(document), canEdit);
    }

    private static async Task<PurchaseDocument?> FindAsync(LexwareService lexware, IBauakteAccess bauakten, Guid id, CancellationToken cancellationToken)
    {
        var document = await lexware.Db.Set<PurchaseDocument>().SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        return document is null || (document.BauakteId is { } b && !await bauakten.CanReadAsync(b, cancellationToken)) ? null : document;
    }

    private static async Task<Results<Ok<PurchaseDocumentResponse>, ProblemHttpResult>> GetAsync(Guid id, LexwareService lexware, IBauakteAccess bauakten,
        IUserPermissions permissions, CancellationToken cancellationToken) =>
        await FindAsync(lexware, bauakten, id, cancellationToken) is { } document
            ? TypedResults.Ok(await ResponseAsync(lexware, bauakten, document, await CanEditAsync(permissions, cancellationToken), cancellationToken))
            : NotFound("Den Eingangsbeleg gibt es nicht oder Sie dürfen ihn nicht sehen.");

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> FileAsync(Guid id, LexwareService lexware, IBauakteAccess bauakten,
        CancellationToken cancellationToken) =>
        await FindAsync(lexware, bauakten, id, cancellationToken) is { } document
            ? TypedResults.File(document.Content, document.ContentType, document.FileName)
            : NotFound("Den Eingangsbeleg gibt es nicht oder Sie dürfen ihn nicht sehen.");

    private static async Task<Results<Ok<PurchaseDocumentResponse>, ProblemHttpResult>> UpdateAsync(Guid id, UpdatePurchaseDocumentRequest request,
        LexwareService lexware, IBauakteAccess bauakten, IUserPermissions permissions, ICurrentUser currentUser, IAuditLog audit, CancellationToken cancellationToken)
    {
        if (!await CanEditAsync(permissions, cancellationToken))
        {
            return Denied();
        }

        if (await FindAsync(lexware, bauakten, id, cancellationToken) is not { } document)
        {
            return NotFound("Den Eingangsbeleg gibt es nicht oder Sie dürfen ihn nicht sehen.");
        }

        if (await BauakteProblemAsync(lexware, bauakten, request.BauakteId, cancellationToken) is { } problem)
        {
            return problem;
        }

        if (document.BauakteId != request.BauakteId && request.BauakteId is { } b)
        {
            audit.Record("bauakte.purchase_uploaded", currentUser.UserId, "bauakte", b.ToString(),
                new Dictionary<string, string?> { ["file"] = document.FileName, ["supplier"] = document.SupplierName });
        }

        document.BauakteId = request.BauakteId;
        document.Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        await lexware.Db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await ResponseAsync(lexware, bauakten, document, true, cancellationToken));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(Guid id, LexwareService lexware, IBauakteAccess bauakten,
        IUserPermissions permissions, CancellationToken cancellationToken)
    {
        if (!await CanEditAsync(permissions, cancellationToken))
        {
            return Denied();
        }

        if (await FindAsync(lexware, bauakten, id, cancellationToken) is not { } document)
        {
            return NotFound("Den Eingangsbeleg gibt es nicht oder Sie dürfen ihn nicht sehen.");
        }

        if (document.State is PurchaseDocumentState.Sent or PurchaseDocumentState.Sending)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: Tag,
                detail: "Der Beleg ist bereits an Lexware Office übergeben und bleibt hier als Nachweis erhalten.", extensions: Code("PURCHASE_SENT"));
        }

        lexware.Db.Remove(document);
        await lexware.Db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Übergabe an Lexware Office, höchstens einmal je Beleg: Der Stand wechselt vorher in einer Abfrage auf „sending“
    /// (gleichzeitige Aufrufe gehen leer aus). Bei einer Zeitüberschreitung bleibt offen, ob Lexware Office die Datei erhalten
    /// hat; der Hinweis nennt, wo das zu prüfen ist.
    /// </summary>
    private static async Task<Results<Ok<PurchaseDocumentResponse>, ProblemHttpResult>> SendAsync(Guid id, LexwareService lexware, IBauakteAccess bauakten,
        IUserPermissions permissions, ICurrentUser currentUser, IAuditLog audit, CancellationToken cancellationToken)
    {
        if (!await CanEditAsync(permissions, cancellationToken))
        {
            return Denied();
        }

        if (await FindAsync(lexware, bauakten, id, cancellationToken) is not { } document)
        {
            return NotFound("Den Eingangsbeleg gibt es nicht oder Sie dürfen ihn nicht sehen.");
        }

        if (document.State == PurchaseDocumentState.Sent)
        {
            return TypedResults.Ok(await ResponseAsync(lexware, bauakten, document, true, cancellationToken));
        }

        var db = lexware.Db;
        var now = lexware.Clock.GetUtcNow();
        var stale = now.AddMinutes(-5);
        var claimed = await db.Set<PurchaseDocument>().Where(d => d.Id == id && d.State != PurchaseDocumentState.Sent
                && (d.State != PurchaseDocumentState.Sending || d.SendStartedAt < stale))
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.State, PurchaseDocumentState.Sending).SetProperty(d => d.SendStartedAt, now), cancellationToken);
        if (claimed == 0)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: Tag, detail: "Der Beleg wird gerade an Lexware Office übergeben.",
                extensions: Code("PURCHASE_SENDING"));
        }

        var row = await lexware.SettingsAsync(cancellationToken);
        string? error = null;
        LexwareException? failure = null;
        string? fileId = null;
        try
        {
            if (!row.Configured)
            {
                throw LexwareService.NotConfigured();
            }

            fileId = await lexware.Client.UploadVoucherFileAsync(lexware.Connection(row), document.Content, document.FileName, document.ContentType,
                cancellationToken);
        }
        catch (LexwareException exception)
        {
            failure = exception;
            error = exception.Code is "TIMEOUT" or "OFFLINE"
                ? exception.Message + " Ob Lexware Office den Beleg erhalten hat, sehen Sie dort unter „Belege“, „zu prüfen“. Senden Sie erst danach erneut."
                : exception.Message;
        }

        await db.Set<PurchaseDocument>().Where(d => d.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(d => d.State, fileId is null ? PurchaseDocumentState.Failed : PurchaseDocumentState.Sent)
            .SetProperty(d => d.LexwareFileId, fileId)
            .SetProperty(d => d.SentAt, fileId is null ? null : lexware.Clock.GetUtcNow())
            .SetProperty(d => d.SentByUserId, fileId is null ? null : currentUser.UserId)
            .SetProperty(d => d.SendError, Cut(error, 1000)), cancellationToken);
        if (failure is not null)
        {
            return LexwareEndpoints.Problem(failure);
        }

        audit.Record("lexware.purchase_sent", currentUser.UserId, document.BauakteId is null ? "purchase_document" : "bauakte",
            (document.BauakteId ?? document.Id).ToString(), new Dictionary<string, string?> { ["file"] = document.FileName, ["supplier"] = document.SupplierName });
        await db.SaveChangesAsync(cancellationToken);
        await db.Entry(document).ReloadAsync(cancellationToken);
        return TypedResults.Ok(await ResponseAsync(lexware, bauakten, document, true, cancellationToken));
    }

    // ---------- Bauakte ----------

    /// <summary>Erlöse: Rechnungen abzüglich Gutschriften (ohne Entwürfe und stornierte); Kosten: Eingangsbelege ohne stornierte.</summary>
    internal static (decimal Revenue, decimal Costs) Sums(IEnumerable<(string Type, string? Status, decimal? Gross)> sales,
        IEnumerable<(string Type, string? Status, decimal? Gross)> purchases)
    {
        var revenue = sales.Where(s => s.Status is not ("voided" or "draft") && s.Type is LexwareVoucherTypes.Invoice or LexwareVoucherTypes.CreditNote)
            .Sum(s => (s.Type == LexwareVoucherTypes.CreditNote ? -1 : 1) * (s.Gross ?? 0m));
        var costs = purchases.Where(p => p.Status != "voided").Sum(p => (p.Type == LexwareVoucherTypes.PurchaseCreditNote ? -1 : 1) * (p.Gross ?? 0m));
        return (revenue, costs);
    }

    internal static string CostsText(decimal revenue, decimal costs, bool any) => any
        ? $"Erlöse {Money(revenue)}, Kosten {Money(costs)}, Differenz {Money(revenue - costs)} (brutto laut Lexware Office)."
        : "Noch keine Rechnungen oder Eingangsbelege zugeordnet.";

    private static async Task<Results<Ok<BauakteCostsResponse>, ProblemHttpResult>> CostsAsync(Guid id, LexwareService lexware, IBauakteAccess bauakten,
        IUserPermissions permissions, CancellationToken cancellationToken)
    {
        if (await BauakteProblemAsync(lexware, bauakten, id, cancellationToken) is { } problem)
        {
            return problem;
        }

        var db = lexware.Db;
        var row = await lexware.SettingsAsync(cancellationToken);
        var sales = await db.Set<LexwareVoucherLink>().AsNoTracking().Where(l => l.BauakteId == id)
            .Select(l => new { l.VoucherType, l.Status, l.GrossAmount }).ToListAsync(cancellationToken);
        var links = await db.Set<PurchaseVoucherLink>().AsNoTracking().Where(l => l.BauakteId == id).OrderByDescending(l => l.VoucherDate)
            .ToListAsync(cancellationToken);
        var bauakte = await db.Set<Bauakte>().AsNoTracking().Where(b => b.Id == id).Select(b => new { b.Number, b.Title }).SingleAsync(cancellationToken);
        var (revenue, costs) = Sums(sales.Select(s => (s.VoucherType, (string?)s.Status, s.GrossAmount)), links.Select(l => (l.VoucherType, l.Status, l.GrossAmount)));
        var documents = await DocumentItemsAsync(lexware, bauakten, db.Set<PurchaseDocument>().Where(d => d.BauakteId == id), cancellationToken);
        return TypedResults.Ok(new BauakteCostsResponse(id, revenue, costs, revenue - costs, CostsText(revenue, costs, sales.Count + links.Count > 0),
            links.Select(l => LinkItem(lexware, row, l, bauakte.Number, bauakte.Title)).ToList(), documents, await CanEditAsync(permissions, cancellationToken)));
    }
}
