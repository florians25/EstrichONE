using System.ComponentModel.DataAnnotations;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Contacts.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Bauakten;

/// <summary>Rolle eines Beteiligten am Bauvorhaben (Reihenfolge = Sortierung in der Anzeige).</summary>
public enum BauaktePartyRole
{
    Client,
    SiteContact,
    SiteManager,
    Architect,
    Planner,
    GeneralContractor,
    Supplier,
    Subcontractor,
    Other,
}

/// <summary>
/// Beteiligter einer Bauakte (Bauherr, Bauleiter, Architekt, Lieferant …): aus dem Kundenstamm verknüpft
/// (<see cref="ContactId"/>) oder frei erfasst. Kunde und Ansprechpartner vor Ort bleiben zusätzlich an der Bauakte.
/// </summary>
public sealed class BauakteParty : IVersionedEntity
{
    public Guid Id { get; private set; }
    public Guid BauakteId { get; private set; }
    public BauaktePartyRole Role { get; private set; }
    public Guid? ContactId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Company { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? Note { get; private set; }

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    private BauakteParty()
    {
    }

    public BauakteParty(Guid id, Guid bauakteId)
    {
        Id = id;
        BauakteId = bauakteId;
    }

    public void Update(BauaktePartyRole role, Guid? contactId, string name, string? company, string? phone, string? email, string? note)
    {
        Role = role;
        ContactId = contactId;
        Name = name;
        Company = company;
        Phone = phone;
        Email = email;
        Note = note;
    }
}

internal sealed class BauaktePartyConfiguration : IEntityTypeConfiguration<BauakteParty>
{
    public void Configure(EntityTypeBuilder<BauakteParty> builder)
    {
        builder.ToTable("bauakte_parties");
        builder.ConfigureVersioned();
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Role).HasConversion<string>().HasMaxLength(30);
        builder.Property(p => p.Name).HasMaxLength(200);
        builder.Property(p => p.Company).HasMaxLength(200);
        builder.Property(p => p.Phone).HasMaxLength(50);
        builder.Property(p => p.Email).HasMaxLength(320);
        builder.Property(p => p.Note).HasMaxLength(1000);
        builder.HasOne<Bauakte>().WithMany().HasForeignKey(p => p.BauakteId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Contact>().WithMany().HasForeignKey(p => p.ContactId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(p => p.BauakteId);
    }
}

/// <param name="Name">Bei verknüpften Kontakten der aktuelle Name aus dem Kundenstamm.</param>
public sealed record BauaktePartyResponse(
    Guid Id, BauaktePartyRole Role, Guid? ContactId, string Name, string? Company, string? Phone, string? Email, string? Note,
    string? Address, long Version);

public sealed record SaveBauaktePartyRequest(
    BauaktePartyRole Role,
    Guid? ContactId,
    [property: MaxLength(200)] string? Name,
    [property: MaxLength(200)] string? Company = null,
    [property: MaxLength(50)] string? Phone = null,
    [property: MaxLength(320), EmailAddress] string? Email = null,
    [property: MaxLength(1000)] string? Note = null,
    long? Version = null);

internal static class BauaktePartyEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/bauakten/{id:guid}/parties").WithTags("Bauakten");
        group.MapGet("/", ListAsync).WithName("ListBauakteParties")
            .WithSummary("Beteiligte (Bauherr, Bauleiter, Architekt, Lieferant …), nach Rolle sortiert")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.BauaktenRead);
        group.MapPut("/{partyId:guid}", SaveAsync).WithName("SaveBauakteParty")
            .WithSummary("Beteiligten anlegen oder ändern (aus dem Kundenstamm verknüpft oder frei erfasst)")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity).RequirePermission(Permissions.BauaktenEdit);
        group.MapDelete("/{partyId:guid}", DeleteAsync).WithName("DeleteBauakteParty")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.BauaktenEdit);
    }

    private static async Task<bool> VisibleAsync(Guid id, IBauakteAccess access, EstrichOneDbContext db, CancellationToken cancellationToken) =>
        await (await access.VisibleAsync(db.Set<Bauakte>().AsNoTracking(), cancellationToken)).AnyAsync(b => b.Id == id, cancellationToken);

    private static async Task<Results<Ok<List<BauaktePartyResponse>>, ProblemHttpResult>> ListAsync(
        Guid id, IBauakteAccess access, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        if (!await VisibleAsync(id, access, db, cancellationToken))
        {
            return NotFound();
        }

        return TypedResults.Ok(await ListOfAsync(db, id, cancellationToken));
    }

    internal static async Task<List<BauaktePartyResponse>> ListOfAsync(EstrichOneDbContext db, Guid bauakteId, CancellationToken cancellationToken)
    {
        var parties = await db.Set<BauakteParty>().AsNoTracking().Where(p => p.BauakteId == bauakteId).ToListAsync(cancellationToken);
        var contactIds = parties.Where(p => p.ContactId != null).Select(p => p.ContactId!.Value).ToList();
        var contacts = await db.Set<Contact>().AsNoTracking().Where(c => contactIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken);
        return parties
            .Select(p =>
            {
                var c = p.ContactId is { } cid ? contacts.GetValueOrDefault(cid) : null;
                var address = c is null ? null : string.Join(", ", new[] { c.Street, string.Join(' ', new[] { c.PostalCode, c.City }.Where(v => !string.IsNullOrWhiteSpace(v))) }
                    .Where(v => !string.IsNullOrWhiteSpace(v)));
                return new BauaktePartyResponse(p.Id, p.Role, p.ContactId, c?.Name ?? p.Name, p.Company, p.Phone ?? c?.Phone, p.Email ?? c?.Email,
                    p.Note, string.IsNullOrEmpty(address) ? null : address, p.Version);
            })
            .OrderBy(p => p.Role).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static async Task<Results<Ok<List<BauaktePartyResponse>>, ProblemHttpResult>> SaveAsync(
        Guid id, Guid partyId, SaveBauaktePartyRequest request, IBauakteAccess access, IBauakteHistory history, EstrichOneDbContext db,
        CancellationToken cancellationToken)
    {
        if (!await VisibleAsync(id, access, db, cancellationToken))
        {
            return NotFound();
        }

        string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        var name = Clean(request.Name);
        if (request.ContactId is { } contactId)
        {
            var contact = await db.Set<Contact>().AsNoTracking().SingleOrDefaultAsync(c => c.Id == contactId, cancellationToken);
            if (contact is null)
            {
                return Validation("Der Kontakt aus dem Kundenstamm existiert nicht (mehr).");
            }

            name ??= contact.Name;
        }

        if (name is null)
        {
            return Validation("Bitte einen Namen angeben.");
        }

        if (!Enum.IsDefined(request.Role))
        {
            return Validation("Unbekannte Rolle.");
        }

        var party = await db.Set<BauakteParty>().SingleOrDefaultAsync(p => p.Id == partyId, cancellationToken);
        if (party is not null && party.BauakteId != id)
        {
            return NotFound();
        }

        if (party is not null && request.Version is { } version && version != party.Version)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "Geändert",
                detail: "Der Eintrag wurde inzwischen geändert. Bitte neu laden.", extensions: new Dictionary<string, object?> { ["code"] = "VERSION_CONFLICT" });
        }

        var created = party is null;
        if (party is null)
        {
            party = new BauakteParty(partyId, id);
            db.Add(party);
        }

        party.Update(request.Role, request.ContactId, name, Clean(request.Company), Clean(request.Phone), Clean(request.Email), Clean(request.Note));
        history.Record(id, BauakteHistoryActions.PartiesChanged, new Dictionary<string, string?>
        {
            ["name"] = name, ["role"] = request.Role.ToString(), ["change"] = created ? "added" : "updated",
        });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await ListOfAsync(db, id, cancellationToken));
    }

    private static async Task<Results<Ok<List<BauaktePartyResponse>>, ProblemHttpResult>> DeleteAsync(
        Guid id, Guid partyId, IBauakteAccess access, IBauakteHistory history, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        if (!await VisibleAsync(id, access, db, cancellationToken))
        {
            return NotFound();
        }

        var party = await db.Set<BauakteParty>().SingleOrDefaultAsync(p => p.Id == partyId && p.BauakteId == id, cancellationToken);
        if (party is null)
        {
            return NotFound();
        }

        db.Remove(party);
        history.Record(id, BauakteHistoryActions.PartiesChanged, new Dictionary<string, string?>
        {
            ["name"] = party.Name, ["role"] = party.Role.ToString(), ["change"] = "removed",
        });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await ListOfAsync(db, id, cancellationToken));
    }

    private static ProblemHttpResult NotFound() => TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Nicht gefunden",
        detail: "Die Bauakte oder der Eintrag existiert nicht (mehr).", extensions: new Dictionary<string, object?> { ["code"] = "NOT_FOUND" });

    private static ProblemHttpResult Validation(string detail) => TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity,
        title: "Ungültige Angaben", detail: detail, extensions: new Dictionary<string, object?> { ["code"] = "VALIDATION_FAILED" });
}
