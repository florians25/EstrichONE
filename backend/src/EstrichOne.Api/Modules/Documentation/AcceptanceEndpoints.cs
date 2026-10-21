using System.ComponentModel.DataAnnotations;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using EstrichOne.Api.Modules.Work.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Documentation;

public sealed record AcceptanceLinkRequest([property: MaxLength(320), EmailAddress] string? Email = null);

public sealed record AcceptanceLinkResponse(string Url, DateTimeOffset ExpiresAt, bool Emailed);

public sealed record PublicAcceptanceRoom(string Name, string? Floor, decimal? AreaSquareMeters, string? ScreedType);

public sealed record PublicAcceptanceDefect(string Title, DateOnly? DueDate);

/// <summary>Was der Kunde über den Freigabelink sieht – nur dieses Protokoll.</summary>
public sealed record PublicAcceptanceView(
    string CompanyName, bool HasCompanyLogo, string? BauakteNumber, string BauakteTitle, string? Address, DateTimeOffset PerformedAt,
    string? Location, string? ClientName, string? ContractorName, string? Scope, IReadOnlyList<PublicAcceptanceRoom> Rooms, AcceptanceResult Result,
    IReadOnlyList<PublicAcceptanceDefect> Defects, string? Notes, IReadOnlyList<Guid> PhotoFileIds, bool HasContractorSignature,
    DateTimeOffset ExpiresAt, AcceptanceState State, CustomerDecision? Decision);

public sealed record PublicAcceptanceDecision(
    bool Approve,
    [property: Required, MaxLength(200)] string Name,
    [property: MaxLength(2000)] string? Comment = null,
    string? SignaturePng = null);

internal static class AcceptanceEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/").WithTags("Abnahme");
        group.MapGet("/bauakten/{id:guid}/acceptances", ListAsync).WithName("ListAcceptances").WithSummary("Abnahmeprotokolle der Bauakte (neueste zuerst)")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.BauaktenRead);
        group.MapGet("/bauakten/{id:guid}/acceptance-form.pdf", BlankAsync).WithName("AcceptanceFormPdf")
            .WithSummary("Leerer Abnahme-Vordruck als PDF (zum Ausfüllen von Hand)").Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.BauaktenRead);
        group.MapGet("/acceptances/{id:guid}/signatures/{side}", SignatureAsync).WithName("GetAcceptanceSignature")
            .WithSummary("Unterschrift als PNG (side = client | contractor)")
            .Produces(StatusCodes.Status200OK, contentType: "image/png").ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(Permissions.BauaktenRead);
        group.MapPost("/acceptances/{id:guid}/customer-link", LinkAsync).WithName("CreateAcceptanceCustomerLink")
            .WithSummary("Freigabelink für den Kunden (ohne Anmeldung, befristet); mit E-Mail-Adresse wird er verschickt")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(Permissions.InspectionsWrite);

        var open = api.MapGroup("/public/acceptances/{token}").WithTags("Abnahme (öffentlich)").AllowAnonymous()
            .RequireRateLimiting("auth-refresh"); // Grenze je Adresse wie beim Erneuern der Sitzung
        open.MapGet("/", PublicViewAsync).WithName("GetPublicAcceptance").ProducesProblem(StatusCodes.Status404NotFound);
        open.MapGet("/photos/{fileId:guid}", PublicPhotoAsync).WithName("GetPublicAcceptancePhoto").ProducesProblem(StatusCodes.Status404NotFound);
        open.MapGet("/signature", PublicSignatureAsync).WithName("GetPublicAcceptanceSignature").ProducesProblem(StatusCodes.Status404NotFound);
        open.MapPost("/decision", PublicDecisionAsync).WithName("DecidePublicAcceptance")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<Results<Ok<List<AcceptanceDto>>, ProblemHttpResult>> ListAsync(
        Guid id, IBauakteAccess access, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        if (!await access.CanReadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        var list = await db.Set<AcceptanceProtocol>().AsNoTracking().Where(a => a.BauakteId == id)
            .OrderByDescending(a => a.PerformedAt).ThenByDescending(a => a.Id).ToListAsync(cancellationToken);
        return TypedResults.Ok(list.Select(AcceptanceDto.From).ToList());
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> BlankAsync(
        Guid id, IBauakteAccess access, AcceptanceService service, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        if (!await access.CanReadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        var pdf = await service.RenderBlankAsync(id, currentUser.UserId, cancellationToken);
        return pdf is null ? NotFound() : TypedResults.File(pdf, "application/pdf", "Abnahmeprotokoll (Vordruck).pdf");
    }

    private static async Task<AcceptanceProtocol?> VisibleAsync(Guid id, IBauakteAccess access, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var protocol = await db.Set<AcceptanceProtocol>().SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        return protocol is not null && await access.CanReadAsync(protocol.BauakteId, cancellationToken) ? protocol : null;
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> SignatureAsync(
        Guid id, string side, IBauakteAccess access, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var protocol = await VisibleAsync(id, access, db, cancellationToken);
        var png = side switch { "client" => protocol?.ClientSignature, "contractor" => protocol?.ContractorSignature, _ => null };
        return png is null ? NotFound() : TypedResults.File(png, "image/png");
    }

    private static async Task<Results<Ok<AcceptanceLinkResponse>, ProblemHttpResult>> LinkAsync(
        Guid id, AcceptanceLinkRequest request, IBauakteAccess access, EstrichOneDbContext db, AcceptanceService service, ICurrentUser currentUser,
        HttpContext http, CancellationToken cancellationToken)
    {
        var protocol = await VisibleAsync(id, access, db, cancellationToken);
        if (protocol is null)
        {
            return NotFound();
        }

        if (protocol.State == AcceptanceState.Completed)
        {
            return Problem(StatusCodes.Status409Conflict, "ALREADY_COMPLETED", "Das Protokoll ist bereits abgeschlossen.");
        }

        try
        {
            var (url, expires, emailed) = await service.IssueLinkAsync(protocol, request.Email, $"{http.Request.Scheme}://{http.Request.Host}",
                currentUser.UserId, cancellationToken);
            return TypedResults.Ok(new AcceptanceLinkResponse(url, expires, emailed));
        }
        catch (ArgumentException exception)
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, "VALIDATION_FAILED", exception.Message);
        }
    }

    private static Task<AcceptanceProtocol?> ByTokenAsync(string token, EstrichOneDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (token.Length is < 20 or > 100)
        {
            return Task.FromResult<AcceptanceProtocol?>(null);
        }

        var hash = AcceptanceProtocol.HashToken(token);
        var now = clock.GetUtcNow();
        return db.Set<AcceptanceProtocol>().SingleOrDefaultAsync(a => a.CustomerTokenHash == hash && a.CustomerLinkExpiresAt > now, cancellationToken);
    }

    private static async Task<Results<Ok<PublicAcceptanceView>, ProblemHttpResult>> PublicViewAsync(
        string token, EstrichOneDbContext db, IBauakteReportSource bauakten, IWorkReportSource work, ICompanyProfiles companies, IPdfBranding branding, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var p = await ByTokenAsync(token, db, clock, cancellationToken);
        if (p is null)
        {
            return LinkInvalid();
        }

        var info = await bauakten.GetAsync(p.BauakteId, cancellationToken);
        var rows = await work.ListAsync(p.BauakteId, cancellationToken);
        var company = await companies.GetProfileAsync(cancellationToken);
        var hasLogo = await branding.HasLogoAsync(cancellationToken);
        var address = info is null ? null : string.Join(", ", new[] { info.Street, string.Join(' ', new[] { info.PostalCode, info.City }.Where(v => !string.IsNullOrWhiteSpace(v))) }
            .Where(v => !string.IsNullOrWhiteSpace(v)));
        var rooms = (info?.Rooms ?? []).Where(r => p.RoomIds.Count == 0 || p.RoomIds.Contains(r.Id))
            .Select(r => new PublicAcceptanceRoom(r.Name, r.Floor, r.AreaSquareMeters, r.ScreedType)).ToList();
        return TypedResults.Ok(new PublicAcceptanceView(company?.Name ?? "EstrichONE", hasLogo, info?.Number, info?.Title ?? "Bauvorhaben", address,
            p.PerformedAt, p.Location, p.ClientName, p.ContractorName, p.Scope, rooms, p.Result,
            rows.Where(r => p.DefectWorkItemIds.Contains(r.Id)).Select(r => new PublicAcceptanceDefect(r.Title, r.DueDate)).ToList(),
            p.Notes, p.PhotoFileIds, p.ContractorSignature is not null, p.CustomerLinkExpiresAt!.Value, p.State, p.CustomerDecision));
    }

    private static async Task<Results<PhysicalFileHttpResult, ProblemHttpResult>> PublicPhotoAsync(
        string token, Guid fileId, EstrichOneDbContext db, LocalFileStorage storage, TimeProvider clock, CancellationToken cancellationToken)
    {
        var p = await ByTokenAsync(token, db, clock, cancellationToken);
        if (p is null || !p.PhotoFileIds.Contains(fileId))
        {
            return LinkInvalid();
        }

        var file = await db.Set<FileRecord>().AsNoTracking().SingleOrDefaultAsync(f => f.Id == fileId && f.State == FileState.Stored, cancellationToken);
        if (file?.StorageKey is null)
        {
            return NotFound();
        }

        var variant = storage.VariantPath(file.Id, "display");
        return File.Exists(variant)
            ? TypedResults.PhysicalFile(variant, "image/webp")
            : TypedResults.PhysicalFile(storage.PathOf(file.StorageKey), file.DetectedContentType ?? "application/octet-stream");
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> PublicSignatureAsync(
        string token, EstrichOneDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var p = await ByTokenAsync(token, db, clock, cancellationToken);
        return p?.ContractorSignature is { } png ? TypedResults.File(png, "image/png") : LinkInvalid();
    }

    private static async Task<Results<Ok<PublicAcceptanceView>, ProblemHttpResult>> PublicDecisionAsync(
        string token, PublicAcceptanceDecision request, EstrichOneDbContext db, AcceptanceService service, IBauakteReportSource bauakten,
        IWorkReportSource work, ICompanyProfiles companies, IPdfBranding branding, TimeProvider clock, HttpContext http, CancellationToken cancellationToken)
    {
        var p = await ByTokenAsync(token, db, clock, cancellationToken);
        if (p is null)
        {
            return LinkInvalid();
        }

        byte[]? signature = null;
        if (!string.IsNullOrWhiteSpace(request.SignaturePng))
        {
            var text = request.SignaturePng;
            var comma = text.IndexOf(',', StringComparison.Ordinal);
            try
            {
                signature = Convert.FromBase64String(comma >= 0 ? text[(comma + 1)..] : text);
            }
            catch (FormatException)
            {
                return Problem(StatusCodes.Status422UnprocessableEntity, "VALIDATION_FAILED", "Die Unterschrift ist ungültig.");
            }
        }

        try
        {
            var view = (await PublicViewAsync(token, db, bauakten, work, companies, branding, clock, cancellationToken)).Result as Ok<PublicAcceptanceView>;
            await service.DecideAsync(p, request.Approve, request.Name, request.Comment, http.Connection.RemoteIpAddress?.ToString(), signature, cancellationToken);
            return TypedResults.Ok(view!.Value! with { State = p.State, Decision = p.CustomerDecision });
        }
        catch (ArgumentException exception)
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, "VALIDATION_FAILED", exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return Problem(StatusCodes.Status409Conflict, "LINK_INVALID", exception.Message);
        }
    }

    private static ProblemHttpResult NotFound() => Problem(StatusCodes.Status404NotFound, "NOT_FOUND", "Das Protokoll existiert nicht (mehr) oder ist nicht sichtbar.");

    private static ProblemHttpResult LinkInvalid() => Problem(StatusCodes.Status404NotFound, "LINK_INVALID",
        "Der Link ist ungültig oder abgelaufen. Bitte wenden Sie sich an den Estrichbetrieb.");

    private static ProblemHttpResult Problem(int status, string code, string detail) => TypedResults.Problem(statusCode: status,
        title: status == 404 ? "Nicht gefunden" : "Nicht möglich", detail: detail, extensions: new Dictionary<string, object?> { ["code"] = code });
}

/// <summary>Hinweis in der Bauakte nach einer Abnahme mit Vorbehalt oder offenen Mängeln.</summary>
internal sealed class AcceptanceHints(EstrichOneDbContext db, IWorkReportSource work) : IBauakteHintSource
{
    public async Task<IReadOnlyList<BauakteHint>> GetHintsAsync(Bauakte bauakte, DateOnly today, CancellationToken cancellationToken)
    {
        var latest = await db.Set<AcceptanceProtocol>().AsNoTracking().Where(a => a.BauakteId == bauakte.Id)
            .OrderByDescending(a => a.PerformedAt).FirstOrDefaultAsync(cancellationToken);
        if (latest is null || bauakte.Status is BauakteStatus.Completed or BauakteStatus.Cancelled)
        {
            return [];
        }

        if (latest.State == AcceptanceState.AwaitingCustomer)
        {
            return [new BauakteHint("acceptance", BauakteHintSeverity.Info, "Abnahmeprotokoll wartet auf die Freigabe durch den Kunden.")];
        }

        var open = (await work.ListAsync(bauakte.Id, cancellationToken)).Count(r => r.IsDefect && r.IsOpen);
        if (latest.State == AcceptanceState.Completed && (latest.Result != AcceptanceResult.Accepted || open > 0))
        {
            return [new BauakteHint("acceptance", BauakteHintSeverity.Warning, latest.Result == AcceptanceResult.Refused
                ? "Abnahme verweigert – Mängel beheben und erneut abnehmen."
                : $"Abnahme mit Vorbehalt – Akte bleibt offen bis die Mängel erledigt sind ({open} offen).")];
        }

        return [];
    }
}
