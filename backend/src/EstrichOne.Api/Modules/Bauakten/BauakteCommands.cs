using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Contacts.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Bauakten;

public enum CreateBauakteOutcome
{
    Created,
    AlreadyExists,
    IdConflict,
    UnknownCustomer,
}

public sealed record NewBauakte(
    Guid Id, string Title, string? Street = null, string? PostalCode = null, string? City = null, double? Latitude = null,
    double? Longitude = null, string? Description = null, Guid? CustomerId = null, string? TemplateKey = null,
    BauakteStatus InitialStatus = BauakteStatus.Planned, BauakteProject? Project = null, DateOnly? PlannedStart = null,
    DateOnly? PlannedEnd = null);

/// <summary>Anlage einer Bauakte – gemeinsam für REST-API und Synchronisation.</summary>
public sealed class BauakteCommands(EstrichOneDbContext db, ICurrentUser currentUser, BauakteNumberGenerator numbers, IBauakteHistory history)
{
    /// <summary>Erwartet eine offene Transaktion des Aufrufers oder legt selbst eine an.</summary>
    public async Task<(Bauakte? Bauakte, CreateBauakteOutcome Outcome)> CreateAsync(NewBauakte request, CancellationToken cancellationToken)
    {
        if (request.Id == Guid.Empty)
        {
            throw new ArgumentException("Die ID fehlt.", nameof(request));
        }

        var existing = await db.Set<Bauakte>().IgnoreQueryFilters([QueryFilters.NotDeleted]).AsNoTracking()
            .SingleOrDefaultAsync(b => b.Id == request.Id, cancellationToken);
        if (existing is not null)
        {
            // Wiederholte Übertragung derselben Anlage (z. B. nach Verbindungsabbruch).
            return existing.CreatedByUserId == currentUser.UserId && existing.Title == request.Title.Trim() && existing.DeletedAt is null
                ? (existing, CreateBauakteOutcome.AlreadyExists)
                : (null, CreateBauakteOutcome.IdConflict);
        }

        if (request.CustomerId is { } customerId && !await db.Set<Contact>().AnyAsync(c => c.Id == customerId, cancellationToken))
        {
            return (null, CreateBauakteOutcome.UnknownCustomer);
        }

        var bauakte = Bauakte.Create(request.Id, request.Title, currentUser.UserId);
        bauakte.ChangeAddress(request.Street, request.PostalCode, request.City, request.Latitude, request.Longitude);
        bauakte.Describe(request.Description);
        bauakte.AssignCustomer(request.CustomerId);
        bauakte.ApplyTemplate(request.TemplateKey);
        if (request.Project is { } project)
        {
            bauakte.ChangeProject(project);
        }

        if (request.PlannedStart is not null || request.PlannedEnd is not null)
        {
            bauakte.Schedule(request.PlannedStart, request.PlannedEnd);
        }
        if (request.InitialStatus == BauakteStatus.Inquiry)
        {
            // Anfrage aus dem Büro: wird später per Statuswechsel zum Auftrag (02: „Anfragen → Bauakte“).
            bauakte.ChangeStatus(BauakteStatus.Inquiry);
        }

        var ownTransaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            bauakte.AssignNumber(await numbers.NextAsync(cancellationToken));
            db.Add(bauakte);
            history.Record(bauakte.Id, BauakteHistoryActions.Created);
            await db.SaveChangesAsync(cancellationToken);
            if (ownTransaction is not null)
            {
                await ownTransaction.CommitAsync(cancellationToken);
            }
        }
        finally
        {
            if (ownTransaction is not null)
            {
                await ownTransaction.DisposeAsync();
            }
        }

        return (bauakte, CreateBauakteOutcome.Created);
    }
}
