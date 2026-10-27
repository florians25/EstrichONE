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

    /// <summary>Weitere Nummer (meist Mobil) neben <see cref="Phone"/>.</summary>
    public string? Mobile { get; private set; }
    public string? Street { get; private set; }
    public string? PostalCode { get; private set; }
    public string? City { get; private set; }

    /// <summary>Hauptkontakt der Bauakte (höchstens einer je Bauakte).</summary>
    public bool IsPrimary { get; private set; }

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

    public void UpdateDetails(string? mobile, string? street, string? postalCode, string? city)
    {
        Mobile = mobile;
        Street = street;
        PostalCode = postalCode;
        City = city;
    }

    public void SetPrimary(bool primary) => IsPrimary = primary;
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
        builder.Property(p => p.Mobile).HasMaxLength(50);
        builder.Property(p => p.Street).HasMaxLength(200);
        builder.Property(p => p.PostalCode).HasMaxLength(10);
        builder.Property(p => p.City).HasMaxLength(100);
        builder.HasOne<Bauakte>().WithMany().HasForeignKey(p => p.BauakteId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Contact>().WithMany().HasForeignKey(p => p.ContactId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(p => p.BauakteId);
    }
}

/// <param name="Name">Bei verknüpften Kontakten der aktuelle Name aus dem Kundenstamm.</param>
/// <param name="Phone">Eigene Nummer des Eintrags, sonst die erste des verknüpften Kontakts.</param>
/// <param name="Address">Anschrift als eine Zeile (eigene Angaben, sonst die des verknüpften Kontakts).</param>
/// <param name="Phones">Alle Nummern: eigene (Telefon, Mobil) zuerst, dann die des verknüpften Kontakts, ohne Doppelte.</param>
/// <param name="Emails">Alle E-Mail-Adressen, eigene zuerst.</param>
/// <param name="IsPrimary">Hauptkontakt der Bauakte (höchstens einer).</param>
/// <param name="ContactKind">Art des verknüpften Kontakts (Firma/Person), sonst <c>null</c>.</param>
public sealed record BauaktePartyResponse(
    Guid Id, BauaktePartyRole Role, Guid? ContactId, string Name, string? Company, string? Phone, string? Email, string? Note,
    string? Address, long Version, string? Mobile = null, string? Street = null, string? PostalCode = null, string? City = null,
    bool IsPrimary = false, IReadOnlyList<ContactPhone>? Phones = null, IReadOnlyList<ContactEmail>? Emails = null,
    ContactKind? ContactKind = null, string? CustomerNumber = null);

public sealed record SaveBauaktePartyRequest(
    BauaktePartyRole Role,
    Guid? ContactId,
    [property: MaxLength(200)] string? Name,
    [property: MaxLength(200)] string? Company = null,
    [property: MaxLength(50)] string? Phone = null,
    [property: MaxLength(320), EmailAddress] string? Email = null,
    [property: MaxLength(1000)] string? Note = null,
    long? Version = null,
    [property: MaxLength(50)] string? Mobile = null,
    [property: MaxLength(200)] string? Street = null,
    [property: MaxLength(10)] string? PostalCode = null,
    [property: MaxLength(100)] string? City = null,
    bool? IsPrimary = null);

internal static class BauaktePartyEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/bauakten/{id:guid}/parties").WithTags("Bauakten");
        group.MapGet("/", ListAsync).WithName("ListBauakteParties")
            .WithSummary("Beteiligte (Bauherr, Bauleiter, Architekt, Lieferant …): Hauptkontakt zuerst, dann nach Rolle sortiert")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.BauaktenRead);
        group.MapPut("/{partyId:guid}", SaveAsync).WithName("SaveBauakteParty")
            .WithSummary("Beteiligten anlegen oder ändern (aus dem Kundenstamm verknüpft oder frei erfasst); auch Rolle und Hauptkontakt")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity).RequirePermission(Permissions.BauaktenEdit);
        group.MapPost("/{partyId:guid}/primary", SetPrimaryAsync).WithName("SetBauaktePartyPrimary")
            .WithSummary("Als Hauptkontakt der Bauakte markieren (ein bisheriger Hauptkontakt verliert die Markierung)")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.BauaktenEdit);
        group.MapDelete("/{partyId:guid}/primary", ClearPrimaryAsync).WithName("ClearBauaktePartyPrimary")
            .WithSummary("Markierung als Hauptkontakt entfernen")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.BauaktenEdit);
        group.MapDelete("/{partyId:guid}", DeleteAsync).WithName("DeleteBauakteParty")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.BauaktenEdit);
    }

    internal static async Task<bool> VisibleAsync(Guid id, IBauakteAccess access, EstrichOneDbContext db, CancellationToken cancellationToken) =>
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
            .Select(p => ToResponse(p, p.ContactId is { } cid ? contacts.GetValueOrDefault(cid) : null))
            .OrderByDescending(p => p.IsPrimary).ThenBy(p => p.Role).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    internal static BauaktePartyResponse ToResponse(BauakteParty p, Contact? c)
    {
        static string? Line(string? street, string? postalCode, string? city)
        {
            var line = string.Join(", ", new[] { street, string.Join(' ', new[] { postalCode, city }.Where(v => !string.IsNullOrWhiteSpace(v))) }
                .Where(v => !string.IsNullOrWhiteSpace(v)));
            return line.Length == 0 ? null : line;
        }

        var own = p.Street is not null || p.PostalCode is not null || p.City is not null;
        var phones = new List<ContactPhone>();
        if (p.Phone is not null)
        {
            phones.Add(new ContactPhone(ContactPhoneKind.Office, p.Phone));
        }

        if (p.Mobile is not null)
        {
            phones.Add(new ContactPhone(ContactPhoneKind.Mobile, p.Mobile));
        }

        phones.AddRange(c?.Phones ?? []);
        var emails = new List<ContactEmail>();
        if (p.Email is not null)
        {
            emails.Add(new ContactEmail(p.Email));
        }

        emails.AddRange(c?.Emails ?? []);
        static string Digits(string number) => new([.. number.Where(ch => char.IsDigit(ch) || ch == '+')]);
        return new BauaktePartyResponse(p.Id, p.Role, p.ContactId, c?.Name ?? p.Name, p.Company, p.Phone ?? c?.Phone, p.Email ?? c?.Email,
            p.Note, own ? Line(p.Street, p.PostalCode, p.City) : c is null ? null : Line(c.Street, c.PostalCode, c.City), p.Version,
            p.Mobile, own ? p.Street : c?.Street, own ? p.PostalCode : c?.PostalCode, own ? p.City : c?.City, p.IsPrimary,
            phones.DistinctBy(x => Digits(x.Number)).ToList(), emails.DistinctBy(x => x.Address, StringComparer.OrdinalIgnoreCase).ToList(),
            c?.Kind, c?.CustomerNumber);
    }

    /// <summary>Gemeinsame Logik für Web (PUT) und App (Sync <c>bauakteParty.save</c>). Fehlertext oder <c>null</c>.</summary>
    internal static async Task<(BauakteParty? Party, string? Error, bool Conflict)> ApplyAsync(
        EstrichOneDbContext db, IBauakteHistory history, Guid bauakteId, Guid partyId, SaveBauaktePartyRequest request, CancellationToken cancellationToken)
    {
        static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        var name = Clean(request.Name);
        if (request.ContactId is { } contactId)
        {
            var contact = await db.Set<Contact>().AsNoTracking().SingleOrDefaultAsync(c => c.Id == contactId, cancellationToken);
            if (contact is null)
            {
                return (null, "Der Kontakt aus dem Kundenstamm existiert nicht (mehr).", false);
            }

            name ??= contact.Name;
        }

        if (name is null)
        {
            return (null, "Bitte einen Namen angeben.", false);
        }

        if (!Enum.IsDefined(request.Role))
        {
            return (null, "Unbekannte Rolle.", false);
        }

        var email = Clean(request.Email);
        if (email is not null && (!email.Contains('@', StringComparison.Ordinal) || email.Contains(' ', StringComparison.Ordinal)))
        {
            return (null, $"Ungültige E-Mail-Adresse „{email}“.", false);
        }

        var party = await db.Set<BauakteParty>().SingleOrDefaultAsync(p => p.Id == partyId, cancellationToken);
        if (party is not null && party.BauakteId != bauakteId)
        {
            return (null, null, false);
        }

        if (party is not null && request.Version is { } version && version != party.Version)
        {
            return (party, null, true);
        }

        var created = party is null;
        var roleChanged = party is not null && party.Role != request.Role;
        if (party is null)
        {
            party = new BauakteParty(partyId, bauakteId);
            db.Add(party);
        }

        party.Update(request.Role, request.ContactId, name, Clean(request.Company), Clean(request.Phone), email, Clean(request.Note));
        party.UpdateDetails(Clean(request.Mobile), Clean(request.Street), Clean(request.PostalCode), Clean(request.City));
        if (request.IsPrimary is { } primary)
        {
            await MarkPrimaryAsync(db, party, primary, cancellationToken);
        }

        history.Record(bauakteId, BauakteHistoryActions.PartiesChanged, new Dictionary<string, string?>
        {
            ["name"] = name, ["role"] = request.Role.ToString(), ["change"] = created ? "added" : roleChanged ? "roleChanged" : "updated",
        });
        await db.SaveChangesAsync(cancellationToken);
        return (party, null, false);
    }

    private static async Task MarkPrimaryAsync(EstrichOneDbContext db, BauakteParty party, bool primary, CancellationToken cancellationToken)
    {
        if (primary)
        {
            var others = await db.Set<BauakteParty>().Where(p => p.BauakteId == party.BauakteId && p.Id != party.Id && p.IsPrimary).ToListAsync(cancellationToken);
            others.ForEach(o => o.SetPrimary(false));
        }

        party.SetPrimary(primary);
    }

    private static async Task<Results<Ok<List<BauaktePartyResponse>>, ProblemHttpResult>> SaveAsync(
        Guid id, Guid partyId, SaveBauaktePartyRequest request, IBauakteAccess access, IBauakteHistory history, EstrichOneDbContext db,
        CancellationToken cancellationToken)
    {
        if (!await VisibleAsync(id, access, db, cancellationToken))
        {
            return NotFound();
        }

        var (party, error, conflict) = await ApplyAsync(db, history, id, partyId, request, cancellationToken);
        if (error is not null)
        {
            return Validation(error);
        }

        if (conflict)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "Geändert",
                detail: "Der Eintrag wurde inzwischen geändert. Bitte neu laden.", extensions: new Dictionary<string, object?> { ["code"] = "VERSION_CONFLICT" });
        }

        return party is null ? NotFound() : TypedResults.Ok(await ListOfAsync(db, id, cancellationToken));
    }

    private static Task<Results<Ok<List<BauaktePartyResponse>>, ProblemHttpResult>> SetPrimaryAsync(
        Guid id, Guid partyId, IBauakteAccess access, IBauakteHistory history, EstrichOneDbContext db, CancellationToken cancellationToken) =>
        PrimaryAsync(id, partyId, true, access, history, db, cancellationToken);

    private static Task<Results<Ok<List<BauaktePartyResponse>>, ProblemHttpResult>> ClearPrimaryAsync(
        Guid id, Guid partyId, IBauakteAccess access, IBauakteHistory history, EstrichOneDbContext db, CancellationToken cancellationToken) =>
        PrimaryAsync(id, partyId, false, access, history, db, cancellationToken);

    private static async Task<Results<Ok<List<BauaktePartyResponse>>, ProblemHttpResult>> PrimaryAsync(
        Guid id, Guid partyId, bool primary, IBauakteAccess access, IBauakteHistory history, EstrichOneDbContext db, CancellationToken cancellationToken)
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

        if (party.IsPrimary != primary)
        {
            await MarkPrimaryAsync(db, party, primary, cancellationToken);
            history.Record(id, BauakteHistoryActions.PartiesChanged, new Dictionary<string, string?>
            {
                ["name"] = party.Name, ["role"] = party.Role.ToString(), ["change"] = primary ? "primary" : "updated",
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.Ok(await ListOfAsync(db, id, cancellationToken));
    }

    /// <summary>Entfernt den Eintrag; <c>false</c>, wenn er nicht (mehr) zur Bauakte gehört.</summary>
    internal static async Task<bool> RemoveAsync(EstrichOneDbContext db, IBauakteHistory history, Guid bauakteId, Guid partyId, CancellationToken cancellationToken)
    {
        var party = await db.Set<BauakteParty>().SingleOrDefaultAsync(p => p.Id == partyId && p.BauakteId == bauakteId, cancellationToken);
        if (party is null)
        {
            return false;
        }

        db.Remove(party);
        history.Record(bauakteId, BauakteHistoryActions.PartiesChanged, new Dictionary<string, string?>
        {
            ["name"] = party.Name, ["role"] = party.Role.ToString(), ["change"] = "removed",
        });
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static async Task<Results<Ok<List<BauaktePartyResponse>>, ProblemHttpResult>> DeleteAsync(
        Guid id, Guid partyId, IBauakteAccess access, IBauakteHistory history, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        if (!await VisibleAsync(id, access, db, cancellationToken) || !await RemoveAsync(db, history, id, partyId, cancellationToken))
        {
            return NotFound();
        }

        return TypedResults.Ok(await ListOfAsync(db, id, cancellationToken));
    }

    private static ProblemHttpResult NotFound() => TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Nicht gefunden",
        detail: "Die Bauakte oder der Eintrag existiert nicht (mehr).", extensions: new Dictionary<string, object?> { ["code"] = "NOT_FOUND" });

    private static ProblemHttpResult Validation(string detail) => TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity,
        title: "Ungültige Angaben", detail: detail, extensions: new Dictionary<string, object?> { ["code"] = "VALIDATION_FAILED" });
}
