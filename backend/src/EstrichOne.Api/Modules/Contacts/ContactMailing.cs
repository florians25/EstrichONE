using System.ComponentModel.DataAnnotations;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Contacts.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Mail.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Contacts;

/// <summary>Serien-E-Mail an ausgewählte Kunden (B10); Platzhalter {Name} und {Anrede}. Jede Mail steht im Versandprotokoll.</summary>
public sealed record ContactMailingRequest(
    [property: Required, MinLength(1), MaxLength(500)] IReadOnlyList<Guid> ContactIds,
    [property: Required, MaxLength(200)] string Subject,
    [property: Required, MaxLength(20_000)] string Body);

public sealed record ContactMailingResult(int Queued, IReadOnlyList<string> WithoutEmail, bool MailConfigured);

internal static class ContactMailingEndpoints
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost("/contacts/mailing", SendAsync).WithTags("Kontakte").WithName("SendContactMailing")
            .WithSummary("Serien-E-Mail an ausgewählte Kunden (je Kunde eine eigene Mail an die erste E-Mail-Adresse)")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.ContactsEdit);

    private static async Task<Results<Ok<ContactMailingResult>, ProblemHttpResult>> SendAsync(
        ContactMailingRequest request, EstrichOneDbContext db, IMailService mail, IAuditLog audit, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Subject) || string.IsNullOrWhiteSpace(request.Body) || request.ContactIds.Count is 0 or > 500)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Ungültige Angaben",
                detail: "Betreff, Text und 1 bis 500 Empfänger sind erforderlich.");
        }

        var contacts = await db.Set<Contact>().AsNoTracking().Where(c => request.ContactIds.Contains(c.Id)).ToListAsync(cancellationToken);
        var queued = 0;
        var without = new List<string>();
        var configured = true;
        foreach (var c in contacts.OrderBy(c => c.Name))
        {
            var to = c.Emails.FirstOrDefault()?.Address ?? c.Email;
            if (string.IsNullOrWhiteSpace(to))
            {
                without.Add(c.Name);
                continue;
            }

            var salutation = c.Salutation switch
            {
                "Herr" => $"Sehr geehrter Herr {c.Name}",
                "Frau" => $"Sehr geehrte Frau {c.Name}",
                _ => "Sehr geehrte Damen und Herren",
            };
            string Fill(string text) => text.Replace("{Name}", c.Name, StringComparison.Ordinal).Replace("{Anrede}", salutation, StringComparison.Ordinal);
            var id = await mail.QueueAsync(new MailRequest(to, Fill(request.Subject.Trim()), Fill(request.Body.Trim()), "mailing", $"contact:{c.Id}"), cancellationToken);
            if (id is null)
            {
                configured = false;
                break;
            }

            queued++;
        }

        audit.Record("contacts.mailing", currentUser.UserId, "contact", null, new Dictionary<string, string?>
        {
            ["subject"] = request.Subject, ["queued"] = queued.ToString(System.Globalization.CultureInfo.InvariantCulture),
        });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(new ContactMailingResult(queued, without, configured));
    }
}
