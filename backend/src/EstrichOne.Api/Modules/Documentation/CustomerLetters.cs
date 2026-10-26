using System.ComponentModel.DataAnnotations;
using EstrichOne.Api.Infrastructure.Pdf;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Documentation.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Mail.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using MigraDoc.DocumentObjectModel;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Documentation;

/// <summary>Schreiben an den Kunden nach DIN 5008 (B10): Text frei oder aus Textbausteinen, Platzhalter {Kunde}, {Bauvorhaben} …</summary>
public sealed record CustomerLetterRequest(
    [property: Required, MaxLength(200)] string Subject,
    [property: Required, MaxLength(20_000)] string Body,
    [property: MaxLength(200)] string? Salutation = null,
    bool SendEmail = false,
    [property: MaxLength(320), EmailAddress] string? EmailTo = null);

public sealed record CustomerLetterResult(Guid FileId, string FileName, bool Emailed, string? EmailTo);

internal static class CustomerLetterEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapPost("/bauakten/{id:guid}/letters", CreateAsync).WithTags("Bautagebuch und Prüfungen").WithName("CreateCustomerLetter")
            .WithSummary("Schreiben an den Kunden als PDF (DIN 5008) in der Bauakte ablegen (Kundendokumente/Schreiben), optional per E-Mail senden")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.BauaktenEdit);
    }

    /// <summary>Platzhalter im Text ersetzen.</summary>
    internal static string Fill(string text, BauakteReportInfo akte) => text
        .Replace("{Kunde}", akte.Customer?.Name ?? "", StringComparison.Ordinal)
        .Replace("{Bauvorhaben}", akte.Title, StringComparison.Ordinal)
        .Replace("{Nummer}", akte.Number ?? "", StringComparison.Ordinal)
        .Replace("{Baustelle}", string.Join(", ", new[] { akte.Street, $"{akte.PostalCode} {akte.City}".Trim() }.Where(v => !string.IsNullOrWhiteSpace(v))), StringComparison.Ordinal)
        .Replace("{Beginn}", akte.PlannedStart is { } s ? PdfReport.Date(s) : "", StringComparison.Ordinal);

    private static async Task<Results<Ok<CustomerLetterResult>, ProblemHttpResult>> CreateAsync(
        Guid id, CustomerLetterRequest request, IBauakteAccess access, IBauakteReportSource source, IPdfBranding branding, IGeneratedDocuments generated,
        IMailService mail, EstrichOneDbContext db, ICurrentUser currentUser, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!await access.CanReadAsync(id, cancellationToken) || await source.GetAsync(id, cancellationToken) is not { } akte)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Bauakte nicht gefunden");
        }

        if (string.IsNullOrWhiteSpace(request.Subject) || string.IsNullOrWhiteSpace(request.Body))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Ungültige Angaben", detail: "Betreff und Text sind erforderlich.");
        }

        var company = await branding.GetAsync(cancellationToken);
        var author = await db.Set<User>().AsNoTracking().Where(u => u.Id == currentUser.UserId).Select(u => new { u.DisplayName, u.Phone, u.Email })
            .SingleOrDefaultAsync(cancellationToken);
        var customer = akte.Customer;
        var subject = Fill(request.Subject.Trim(), akte);
        var (document, section) = PdfLetter.Create(subject, company,
            [customer?.Name ?? "", customer?.Street ?? "", $"{customer?.PostalCode} {customer?.City}".Trim()],
            [
                ("Datum", PdfLetter.Today(clock)), ("Bauvorhaben", $"{akte.Number} {akte.Title}".Trim()), ("Ihre Kundennummer", customer?.CustomerNumber),
                ("Ansprechpartner", author?.DisplayName), ("Telefon", author?.Phone ?? company.Phone), ("E-Mail", author?.Email ?? company.Email),
            ],
            subject);
        var salutation = string.IsNullOrWhiteSpace(request.Salutation) ? "Sehr geehrte Damen und Herren," : Fill(request.Salutation.Trim(), akte);
        section.AddParagraph(salutation).Format.SpaceAfter = Unit.FromMillimeter(3);
        foreach (var paragraph in Fill(request.Body.Trim(), akte).Replace("\r\n", "\n", StringComparison.Ordinal).Split("\n\n"))
        {
            var p = section.AddParagraph(paragraph.Trim());
            p.Format.SpaceAfter = Unit.FromMillimeter(3);
        }

        section.AddParagraph("Mit freundlichen Grüßen").Format.SpaceBefore = Unit.FromMillimeter(5);
        section.AddParagraph(company.Name).Format.SpaceBefore = Unit.FromMillimeter(5);
        if (author is not null)
        {
            section.AddParagraph(author.DisplayName);
        }

        var pdf = PdfReport.Render(document);
        var fileName = $"Schreiben {PdfLetter.Today(clock)} {string.Concat(subject.Take(60).Where(c => !Path.GetInvalidFileNameChars().Contains(c)))}.pdf";
        var fileId = await generated.StoreAsync(id, currentUser.UserId, fileName, pdf, "customer", "Schreiben", subject, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        var to = string.IsNullOrWhiteSpace(request.EmailTo) ? customer?.Email : request.EmailTo.Trim();
        var emailed = false;
        if (request.SendEmail && !string.IsNullOrWhiteSpace(to))
        {
            var text = $"{salutation}\n\nanbei erhalten Sie unser Schreiben „{subject}“ zum Bauvorhaben {akte.Number} {akte.Title}.\n\nMit freundlichen Grüßen\n{author?.DisplayName}\n{company.Name}";
            emailed = await mail.QueueAsync(new MailRequest(to, subject, text, "letter", $"bauakte:{id}",
                [new MailAttachment(fileName, "application/pdf", pdf)]), cancellationToken) is not null;
            await db.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.Ok(new CustomerLetterResult(fileId, fileName, emailed, emailed ? to : null));
    }
}
