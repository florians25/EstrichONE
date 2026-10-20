using System.ComponentModel.DataAnnotations;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Contacts.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Calls;

public sealed record CallResponse(
    Guid Id, CallKind Kind, CallDirection Direction, CallSource Source, CallPriority Priority, string? CallerName, string? CallerPhone,
    string? CallerEmail, string? Company, Guid? CustomerId, string? CustomerName, Guid? BauakteId, string? BauakteLabel, string? Subject,
    string? Notes, bool FollowUp, DateOnly? DueDate, string? NextStep, InquiryStatus InquiryStatus, decimal? AreaSquareMeters, string? ScreedType,
    DateOnly? DesiredStart, string? SiteStreet, string? SitePostalCode, string? SiteCity, Guid? ConvertedBauakteId, string? ConvertedBauakteNumber,
    Guid HandledByUserId, string? HandledByName, DateTimeOffset CalledAt, DateTimeOffset UpdatedAt, long Version);

public sealed record SaveCallRequest(
    [property: Required] Guid Id,
    CallKind Kind,
    CallDirection Direction = CallDirection.Inbound,
    CallSource Source = CallSource.Phone,
    CallPriority Priority = CallPriority.Normal,
    [property: MaxLength(200)] string? CallerName = null,
    [property: MaxLength(80)] string? CallerPhone = null,
    [property: MaxLength(320)] string? CallerEmail = null,
    [property: MaxLength(200)] string? Company = null,
    Guid? CustomerId = null,
    Guid? BauakteId = null,
    [property: MaxLength(300)] string? Subject = null,
    [property: MaxLength(10_000)] string? Notes = null,
    bool FollowUp = false,
    DateOnly? DueDate = null,
    [property: MaxLength(300)] string? NextStep = null,
    [property: Range(0, 100_000)] decimal? AreaSquareMeters = null,
    [property: MaxLength(100)] string? ScreedType = null,
    DateOnly? DesiredStart = null,
    [property: MaxLength(200)] string? SiteStreet = null,
    [property: MaxLength(10)] string? SitePostalCode = null,
    [property: MaxLength(100)] string? SiteCity = null,
    DateTimeOffset? CalledAt = null)
{
    internal CallDetails ToDetails() => new(Direction, Source, Priority, CallerName, CallerPhone, CallerEmail, Company, CustomerId, BauakteId,
        Subject, Notes, FollowUp, DueDate, NextStep, AreaSquareMeters, ScreedType, DesiredStart, SiteStreet, SitePostalCode, SiteCity, CalledAt);
}

public sealed record SetInquiryStatusRequest(InquiryStatus Status);

/// <param name="Title">Bauvorhaben; leer = aus Betreff, Firma oder Name der Anfrage.</param>
/// <param name="CreateCustomer">Ohne verknüpften Kunden einen Kunden aus Name/Firma der Anfrage anlegen.</param>
public sealed record ConvertInquiryRequest([property: MaxLength(200)] string? Title = null, bool CreateCustomer = true);

public sealed record ConvertInquiryResponse(CallResponse Inquiry, Guid BauakteId, string? BauakteNumber);

/// <summary>Anrufe und Anfragen (Büro). Lesen mit „Kontakte lesen“, Erfassen mit „Kontakte bearbeiten“.</summary>
internal static class CallEndpoints
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
    private static readonly System.Globalization.CultureInfo German = System.Globalization.CultureInfo.GetCultureInfo("de-DE");

    public static void Map(IEndpointRouteBuilder api)
    {
        var calls = api.MapGroup("/calls").WithTags("Anrufe und Anfragen");
        calls.MapGet("/", ListAsync).WithName("ListCalls")
            .WithSummary("Anrufe oder Anfragen, neueste zuerst; due=true nur fällige Wiedervorlagen")
            .RequirePermission(Permissions.ContactsRead);
        calls.MapGet("/{id:guid}", GetAsync).WithName("GetCall").ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.ContactsRead);
        calls.MapPut("/{id:guid}", SaveAsync).WithName("SaveCall").WithSummary("Anruf oder Anfrage anlegen bzw. ändern")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity).RequirePermission(Permissions.ContactsEdit);
        calls.MapPost("/{id:guid}/status", SetStatusAsync).WithName("SetInquiryStatus")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict).RequirePermission(Permissions.ContactsEdit);
        calls.MapPost("/{id:guid}/follow-up-done", FollowUpDoneAsync).WithName("CompleteFollowUp")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.ContactsEdit);
        calls.MapPost("/{id:guid}/convert", ConvertAsync).WithName("ConvertInquiry")
            .WithSummary("Anfrage in eine Bauakte umwandeln (legt bei Bedarf den Kunden an)")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(Permissions.ContactsEdit).RequirePermission(Permissions.BauaktenCreate);
        calls.MapDelete("/{id:guid}", DeleteAsync).WithName("DeleteCall").ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(Permissions.ContactsEdit);
    }

    private static async Task<Ok<List<CallResponse>>> ListAsync(
        EstrichOneDbContext db, TimeProvider clock, CancellationToken cancellationToken,
        [FromQuery] string? kind = null, [FromQuery] string? status = null, [FromQuery] bool due = false, [FromQuery] bool open = false,
        [FromQuery] Guid? customerId = null, [FromQuery] Guid? bauakteId = null, [FromQuery] string? search = null, [FromQuery] int? limit = null)
    {
        var query = db.Set<CallRecord>().AsNoTracking();
        if (Enum.TryParse<CallKind>(kind, ignoreCase: true, out var k))
        {
            query = query.Where(c => c.Kind == k);
        }

        if (Enum.TryParse<InquiryStatus>(status?.Replace("_", string.Empty, StringComparison.Ordinal), ignoreCase: true, out var s))
        {
            query = query.Where(c => c.InquiryStatus == s);
        }

        if (open)
        {
            query = query.Where(c => c.InquiryStatus == InquiryStatus.Open || c.InquiryStatus == InquiryStatus.InProgress);
        }

        if (due)
        {
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Berlin).DateTime);
            query = query.Where(c => c.FollowUp && (c.DueDate == null || c.DueDate <= today));
        }

        if (customerId is { } customer)
        {
            query = query.Where(c => c.CustomerId == customer);
        }

        if (bauakteId is { } bauakte)
        {
            query = query.Where(c => c.BauakteId == bauakte || c.ConvertedBauakteId == bauakte);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(c => (c.CallerName != null && EF.Functions.ILike(c.CallerName, pattern))
                || (c.Company != null && EF.Functions.ILike(c.Company, pattern))
                || (c.CallerPhone != null && EF.Functions.ILike(c.CallerPhone, pattern))
                || (c.Subject != null && EF.Functions.ILike(c.Subject, pattern))
                || (c.Notes != null && EF.Functions.ILike(c.Notes, pattern)));
        }

        var rows = await query.OrderByDescending(c => c.CalledAt).Take(Math.Clamp(limit ?? 200, 1, 500)).ToListAsync(cancellationToken);
        return TypedResults.Ok(await ToResponsesAsync(db, rows, cancellationToken));
    }

    private static async Task<Results<Ok<CallResponse>, ProblemHttpResult>> GetAsync(Guid id, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var call = await db.Set<CallRecord>().AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        return call is null ? NotFound() : TypedResults.Ok((await ToResponsesAsync(db, [call], cancellationToken))[0]);
    }

    private static async Task<Results<Ok<CallResponse>, ProblemHttpResult>> SaveAsync(
        Guid id, SaveCallRequest request, EstrichOneDbContext db, ICurrentUser currentUser, IBauakteAccess access, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (request.Id != id)
        {
            return Validation("Die ID passt nicht zur Adresse.");
        }

        if (request.CustomerId is { } customerId && !await db.Set<Contact>().AnyAsync(c => c.Id == customerId, cancellationToken))
        {
            return Validation("Der Kunde existiert nicht.");
        }

        if (request.BauakteId is { } bauakteId && !await access.CanReadAsync(bauakteId, cancellationToken))
        {
            return Validation("Die Bauakte existiert nicht.");
        }

        var call = await db.Set<CallRecord>().SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (call is null)
        {
            call = CallRecord.Create(id, request.Kind, currentUser.UserId, request.CalledAt ?? clock.GetUtcNow());
            db.Add(call);
        }
        else if (call.Kind != request.Kind)
        {
            return Validation("Ein Anruf kann nicht nachträglich zur Anfrage werden – bitte eine neue Anfrage anlegen.");
        }

        try
        {
            call.Update(request.ToDetails());
        }
        catch (ArgumentException exception)
        {
            return Validation(exception.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok((await ToResponsesAsync(db, [call], cancellationToken))[0]);
    }

    private static async Task<Results<Ok<CallResponse>, ProblemHttpResult>> SetStatusAsync(
        Guid id, SetInquiryStatusRequest request, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var call = await db.Set<CallRecord>().SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (call is null)
        {
            return NotFound();
        }

        if (request.Status == InquiryStatus.Converted)
        {
            return Conflict("Zur Bauakte wird eine Anfrage über „In Bauakte umwandeln“.");
        }

        try
        {
            call.SetStatus(request.Status);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(exception.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok((await ToResponsesAsync(db, [call], cancellationToken))[0]);
    }

    private static async Task<Results<Ok<CallResponse>, ProblemHttpResult>> FollowUpDoneAsync(Guid id, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var call = await db.Set<CallRecord>().SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (call is null)
        {
            return NotFound();
        }

        call.CompleteFollowUp();
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok((await ToResponsesAsync(db, [call], cancellationToken))[0]);
    }

    private static async Task<Results<Ok<ConvertInquiryResponse>, ProblemHttpResult>> ConvertAsync(
        Guid id, ConvertInquiryRequest request, EstrichOneDbContext db, IBauakteFactory bauakten, CancellationToken cancellationToken)
    {
        var call = await db.Set<CallRecord>().SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (call is null || call.Kind != CallKind.Inquiry)
        {
            return NotFound();
        }

        if (call.InquiryStatus == InquiryStatus.Converted)
        {
            return Conflict("Die Anfrage ist bereits eine Bauakte.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var customerId = call.CustomerId;
        if (customerId is null && request.CreateCustomer && (call.Company ?? call.CallerName) is { } customerName)
        {
            var contact = Contact.Create(Guid.CreateVersion7(), call.Company is null ? ContactKind.Person : ContactKind.Company, customerName);
            contact.Update(contact.Kind, customerName, null, null, null, null, call.CallerPhone, call.CallerEmail,
                call.Company is not null && call.CallerName is not null ? $"Ansprechpartner: {call.CallerName}" : null);
            db.Add(contact);
            await db.SaveChangesAsync(cancellationToken);
            customerId = contact.Id;
        }

        var title = new[] { request.Title, call.Subject, call.Company, call.CallerName }.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t))!.Trim();
        var description = string.Join(Environment.NewLine, new[]
        {
            call.Notes,
            call.AreaSquareMeters is { } area ? $"Fläche laut Anfrage: {area.ToString("0.##", German)} m²" : null,
            call.ScreedType is { } screed ? $"Estrich laut Anfrage: {screed}" : null,
            call.DesiredStart is { } start ? $"Wunschtermin: {start.ToString("dd.MM.yyyy", German)}" : null,
        }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var created = await bauakten.CreateAsync(Guid.CreateVersion7(), title, call.SiteStreet, call.SitePostalCode, call.SiteCity,
            string.IsNullOrWhiteSpace(description) ? null : description, customerId, BauakteStatus.Planned, cancellationToken);
        if (created is not { } bauakte)
        {
            return Validation("Die Bauakte konnte nicht angelegt werden.");
        }

        if (call.CustomerId is null && customerId is not null)
        {
            call.Update(new CallDetails(call.Direction, call.Source, call.Priority, call.CallerName, call.CallerPhone, call.CallerEmail, call.Company,
                customerId, null, call.Subject, call.Notes, call.FollowUp, call.DueDate, call.NextStep, call.AreaSquareMeters, call.ScreedType,
                call.DesiredStart, call.SiteStreet, call.SitePostalCode, call.SiteCity, null));
        }

        call.MarkConverted(bauakte.Id);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TypedResults.Ok(new ConvertInquiryResponse((await ToResponsesAsync(db, [call], cancellationToken))[0], bauakte.Id, bauakte.Number));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(Guid id, EstrichOneDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var call = await db.Set<CallRecord>().SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (call is null)
        {
            return NotFound();
        }

        call.Delete(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<List<CallResponse>> ToResponsesAsync(EstrichOneDbContext db, IReadOnlyList<CallRecord> calls, CancellationToken cancellationToken)
    {
        var customerIds = calls.Select(c => c.CustomerId).OfType<Guid>().Distinct().ToList();
        var bauakteIds = calls.SelectMany(c => new[] { c.BauakteId, c.ConvertedBauakteId }).OfType<Guid>().Distinct().ToList();
        var userIds = calls.Select(c => c.HandledByUserId).Distinct().ToList();
        var customers = await db.Set<Contact>().AsNoTracking().Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        var bauakten = await db.Set<Bauakte>().AsNoTracking().Where(b => bauakteIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => (b.Number, b.Title), cancellationToken);
        var users = await db.Set<User>().AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);

        return calls.Select(c =>
        {
            var linked = c.BauakteId is { } b && bauakten.TryGetValue(b, out var x) ? $"{x.Number ?? "neu"} · {x.Title}" : null;
            var converted = c.ConvertedBauakteId is { } cb && bauakten.TryGetValue(cb, out var y) ? y.Number : null;
            return new CallResponse(c.Id, c.Kind, c.Direction, c.Source, c.Priority, c.CallerName, c.CallerPhone, c.CallerEmail, c.Company,
                c.CustomerId, c.CustomerId is { } cid ? customers.GetValueOrDefault(cid) : null, c.BauakteId, linked, c.Subject, c.Notes,
                c.FollowUp, c.DueDate, c.NextStep, c.InquiryStatus, c.AreaSquareMeters, c.ScreedType, c.DesiredStart, c.SiteStreet, c.SitePostalCode,
                c.SiteCity, c.ConvertedBauakteId, converted, c.HandledByUserId, users.GetValueOrDefault(c.HandledByUserId), c.CalledAt, c.UpdatedAt,
                c.Version);
        }).ToList();
    }

    private static ProblemHttpResult NotFound() => TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Nicht gefunden",
        extensions: new Dictionary<string, object?> { ["code"] = "CALL_NOT_FOUND" });

    private static ProblemHttpResult Conflict(string detail) => TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Nicht möglich",
        detail: detail, extensions: new Dictionary<string, object?> { ["code"] = "INQUIRY_STATE_CONFLICT" });

    private static ProblemHttpResult Validation(string detail) => TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity,
        title: "Ungültige Angaben", detail: detail, extensions: new Dictionary<string, object?> { ["code"] = "VALIDATION_FAILED" });
}
