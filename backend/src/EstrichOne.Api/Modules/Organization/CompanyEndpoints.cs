using System.ComponentModel.DataAnnotations;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Organization;

public sealed record CompanyResponse(
    string Name, string? Street, string? PostalCode, string? City, string? Phone, string? Email, string? Website, long Version);

public sealed record SaveCompanyRequest(
    [property: Required, MaxLength(200)] string Name,
    [property: MaxLength(200)] string? Street = null,
    [property: MaxLength(10)] string? PostalCode = null,
    [property: MaxLength(100)] string? City = null,
    [property: MaxLength(50)] string? Phone = null,
    [property: MaxLength(320), EmailAddress] string? Email = null,
    [property: MaxLength(200)] string? Website = null);

/// <summary>Firmendaten für Briefkopf (Bestellschein, Bauaktenblatt). Lesen darf jeder Angemeldete.</summary>
internal static class CompanyEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var company = api.MapGroup("/company").WithTags("Firmendaten");
        company.MapGet("/", GetAsync).WithName("GetCompany").ProducesProblem(StatusCodes.Status404NotFound);
        company.MapPut("/", SaveAsync).WithName("SaveCompany").ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity).RequirePermission(Permissions.SettingsManage);
    }

    private static async Task<Results<Ok<CompanyResponse>, ProblemHttpResult>> GetAsync(EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var profile = await db.Set<CompanyProfile>().AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return profile is null ? NotFound() : TypedResults.Ok(ToResponse(profile));
    }

    private static async Task<Results<Ok<CompanyResponse>, ProblemHttpResult>> SaveAsync(
        SaveCompanyRequest request, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Ungültige Angaben",
                detail: "Bitte den Firmennamen angeben.", extensions: new Dictionary<string, object?> { ["code"] = "VALIDATION_FAILED" });
        }

        var profile = await db.Set<CompanyProfile>().SingleOrDefaultAsync(cancellationToken);
        if (profile is null)
        {
            return NotFound();
        }

        static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        profile.Update(request.Name, Blank(request.Street), Blank(request.PostalCode), Blank(request.City), Blank(request.Phone), Blank(request.Email),
            Blank(request.Website));
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ToResponse(profile));
    }

    private static CompanyResponse ToResponse(CompanyProfile p) => new(p.Name, p.Street, p.PostalCode, p.City, p.Phone, p.Email, p.Website, p.Version);

    private static ProblemHttpResult NotFound() => TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Nicht gefunden",
        detail: "Die Ersteinrichtung ist noch nicht abgeschlossen.", extensions: new Dictionary<string, object?> { ["code"] = "COMPANY_NOT_FOUND" });
}
