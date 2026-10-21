using EstrichOne.Api.Infrastructure.Pdf;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Documentation.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using MigraDoc.DocumentObjectModel;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Materials;

/// <summary>Bestellschein an den Lieferanten als Geschäftsbrief nach DIN 5008 (Form B), mit Positionen und Lieferangaben.</summary>
internal sealed class OrderPdf(EstrichOneDbContext db, IPdfBranding branding, IBauakteReportSource bauakten, IGeneratedDocuments generated,
    TimeProvider clock)
{
    public static string FileName(MaterialOrder order) => $"Bestellung {order.Number}.pdf";

    public async Task<byte[]> RenderAsync(MaterialOrder order, CancellationToken cancellationToken)
    {
        var company = await branding.GetAsync(cancellationToken);
        var supplier = await db.Set<Supplier>().AsNoTracking().IgnoreQueryFilters([QueryFilters.NotDeleted]).SingleAsync(s => s.Id == order.SupplierId, cancellationToken);
        var creator = await db.Set<User>().AsNoTracking().Where(u => u.Id == order.CreatedByUserId).Select(u => new { u.DisplayName, u.Phone, u.Email })
            .SingleOrDefaultAsync(cancellationToken);
        var akte = order.BauakteId is { } b ? await bauakten.GetAsync(b, cancellationToken) : null;

        var (document, section) = PdfLetter.Create($"Bestellung {order.Number}", company,
            [supplier.Name, supplier.ContactName is null ? "" : $"z. Hd. {supplier.ContactName}", supplier.Street ?? "", $"{supplier.PostalCode} {supplier.City}".Trim()],
            [
                ("Bestellnummer", order.Number), ("Datum", PdfLetter.Today(clock)), ("Unsere Kundennummer", supplier.CustomerNumber),
                ("Ansprechpartner", creator?.DisplayName), ("Telefon", creator?.Phone ?? company.Phone), ("E-Mail", creator?.Email ?? company.Email),
            ],
            $"Bestellung {order.Number}{(akte is null ? "" : $" – Bauvorhaben {akte.Number} {akte.Title}")}");
        section.AddParagraph(supplier.ContactName is null ? "Sehr geehrte Damen und Herren," : $"Guten Tag {supplier.ContactName},").Format.SpaceAfter = Unit.FromMillimeter(3);
        section.AddParagraph("hiermit bestellen wir verbindlich:");

        var table = PdfReport.AddTable(section, ("Pos.", 1.0, ParagraphAlignment.Right), ("Menge", 2.0, ParagraphAlignment.Right),
            ("Einheit", 1.6, ParagraphAlignment.Left), ("Bezeichnung", 7.2, ParagraphAlignment.Left), ("Einzelpreis", 2.2, ParagraphAlignment.Right),
            ("Gesamt", 2.5, ParagraphAlignment.Right));
        decimal total = 0;
        var priced = false;
        foreach (var line in order.Lines.OrderBy(l => l.Position))
        {
            var sum = line.UnitPrice is { } price ? price * line.Quantity : (decimal?)null;
            if (sum is { } s)
            {
                total += s;
                priced = true;
            }

            PdfReport.AddRow(table, line.Position.ToString(PdfReport.German), PdfReport.Number(line.Quantity, line.Quantity % 1 == 0 ? 0 : 2), line.Unit,
                line.Description, line.UnitPrice is { } u ? $"{PdfReport.Number(u)} €" : "", sum is { } t ? $"{PdfReport.Number(t)} €" : "");
        }

        if (priced)
        {
            var row = PdfReport.AddRow(table, "", "", "", "Summe (netto, laut Ihren Preisen)", "", $"{PdfReport.Number(total)} €");
            row.Format.Font.Bold = true;
        }

        section.AddParagraph("Lieferung").Format.SpaceBefore = Unit.FromMillimeter(6);
        section.LastParagraph!.Format.Font.Bold = true;
        var delivery = string.Join(", ", new[] { order.DeliveryStreet, $"{order.DeliveryPostalCode} {order.DeliveryCity}".Trim() }.Where(v => !string.IsNullOrWhiteSpace(v)));
        PdfReport.AddFacts(section, 16.5,
            ("Termin", order.DeliveryDate is { } d ? $"{PdfReport.Date(d)}{(string.IsNullOrWhiteSpace(order.DeliveryTime) ? "" : $", {order.DeliveryTime}")}" : "nach Absprache"),
            ("Lieferadresse", string.IsNullOrEmpty(delivery) ? "–" : delivery),
            ("Ansprechpartner vor Ort", order.SiteContact),
            ("Bauvorhaben", akte is null ? null : $"{akte.Number} {akte.Title}".Trim()),
            ("Hinweise", order.Notes ?? supplier.OrderNotes));
        section.AddParagraph("Bitte bestätigen Sie uns die Bestellung und den Liefertermin.").Format.SpaceBefore = Unit.FromMillimeter(5);
        section.AddParagraph("Mit freundlichen Grüßen").Format.SpaceBefore = Unit.FromMillimeter(6);
        section.AddParagraph(company.Name).Format.SpaceBefore = Unit.FromMillimeter(5);
        if (creator is not null)
        {
            section.AddParagraph(creator.DisplayName);
        }

        return PdfReport.Render(document);
    }

    /// <summary>Bestellschein in der Bauakte ablegen (Büro / Bestellungen; erneute Ausgabe = neue Fassung).</summary>
    public async Task StoreAsync(MaterialOrder order, byte[] pdf, Guid userId, CancellationToken cancellationToken)
    {
        if (order.BauakteId is not { } bauakteId)
        {
            return;
        }

        await generated.StoreAsync(bauakteId, userId, FileName(order), pdf, "office", "Bestellungen", $"Bestellung bei Lieferant ({order.Status})", cancellationToken);
    }
}

internal static class OrderPdfEndpoints
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet("/material-orders/{id:guid}/pdf", PdfAsync).WithTags("Material").WithName("MaterialOrderPdf")
            .WithSummary("Bestellschein als PDF (DIN 5008); mit Bezug zur Bauakte wird er dort unter Büro / Bestellungen abgelegt")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf").ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(Permissions.MaterialsRead).RequireMaterialsModule();

    private static RouteHandlerBuilder RequireMaterialsModule(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var settings = await context.HttpContext.RequestServices.GetRequiredService<ISystemSettings>().GetAsync(context.HttpContext.RequestAborted);
            return settings.Modules.Materials ? await next(context) : MaterialsGate.Disabled();
        });

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> PdfAsync(
        Guid id, EstrichOneDbContext db, IBauakteAccess access, OrderPdf pdf, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var order = await db.Set<MaterialOrder>().AsNoTracking().Include(o => o.Lines).SingleOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null || (order.BauakteId is { } b && !await access.CanReadAsync(b, cancellationToken)))
        {
            return Problems.NotFound("ORDER_NOT_FOUND");
        }

        var bytes = await pdf.RenderAsync(order, cancellationToken);
        await pdf.StoreAsync(order, bytes, currentUser.UserId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.File(bytes, "application/pdf", OrderPdf.FileName(order));
    }
}
