using System.ComponentModel.DataAnnotations;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Materials;

public sealed record SupplierResponse(
    Guid Id, string Name, string? CustomerNumber, string? ContactName, string? Phone, string? Email, string? Street, string? PostalCode,
    string? City, string? OrderNotes, bool IsActive, long Version);

public sealed record SaveSupplierRequest(
    [property: Required] Guid Id,
    [property: Required, MaxLength(200)] string Name,
    [property: MaxLength(50)] string? CustomerNumber = null,
    [property: MaxLength(200)] string? ContactName = null,
    [property: MaxLength(80)] string? Phone = null,
    [property: MaxLength(320), EmailAddress] string? Email = null,
    [property: MaxLength(200)] string? Street = null,
    [property: MaxLength(10)] string? PostalCode = null,
    [property: MaxLength(100)] string? City = null,
    [property: MaxLength(2000)] string? OrderNotes = null,
    bool IsActive = true);

public sealed record MaterialResponse(
    Guid Id, string Name, string? ArticleNumber, MaterialCategory Category, string Unit, Guid? SupplierId, string? SupplierName, decimal? Price,
    string? Notes, bool IsActive, long Version);

public sealed record SaveMaterialRequest(
    [property: Required] Guid Id,
    [property: Required, MaxLength(200)] string Name,
    MaterialCategory Category,
    [property: Required, MaxLength(20)] string Unit,
    [property: MaxLength(50)] string? ArticleNumber = null,
    Guid? SupplierId = null,
    [property: Range(0, 1_000_000)] decimal? Price = null,
    [property: MaxLength(2000)] string? Notes = null,
    bool IsActive = true);

/// <summary>Lieferanten und Materialstamm. Lesen mit „Material sehen“, Pflegen mit „Material bestellen“.</summary>
internal static class MaterialEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var suppliers = api.MapGroup("/suppliers").WithTags("Material").RequireMaterialsModule();
        suppliers.MapGet("/", ListSuppliersAsync).WithName("ListSuppliers").RequirePermission(Permissions.MaterialsRead);
        suppliers.MapPut("/{id:guid}", SaveSupplierAsync).WithName("SaveSupplier").WithSummary("Lieferant anlegen oder ändern")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity).RequirePermission(Permissions.MaterialsManage);
        suppliers.MapDelete("/{id:guid}", DeleteSupplierAsync).WithName("DeleteSupplier")
            .WithSummary("Lieferant entfernen; mit offenen Bestellungen nicht möglich")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict).RequirePermission(Permissions.MaterialsManage);

        var materials = api.MapGroup("/materials").WithTags("Material").RequireMaterialsModule();
        materials.MapGet("/", ListMaterialsAsync).WithName("ListMaterials").RequirePermission(Permissions.MaterialsRead);
        materials.MapPut("/{id:guid}", SaveMaterialAsync).WithName("SaveMaterial").WithSummary("Artikel anlegen oder ändern")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity).RequirePermission(Permissions.MaterialsManage);
        materials.MapDelete("/{id:guid}", DeleteMaterialAsync).WithName("DeleteMaterial")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.MaterialsManage);
    }

    private static async Task<Ok<List<SupplierResponse>>> ListSuppliersAsync(
        EstrichOneDbContext db, CancellationToken cancellationToken, [FromQuery] bool includeInactive = false)
    {
        var query = db.Set<Supplier>().AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(s => s.IsActive);
        }

        var rows = await query.OrderBy(s => s.Name).ToListAsync(cancellationToken);
        return TypedResults.Ok(rows.Select(ToResponse).ToList());
    }

    private static async Task<Results<Ok<SupplierResponse>, ProblemHttpResult>> SaveSupplierAsync(
        Guid id, SaveSupplierRequest request, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        if (request.Id != id)
        {
            return Problems.Validation("Die ID passt nicht zur Adresse.");
        }

        var supplier = await db.Set<Supplier>().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (supplier is null)
        {
            supplier = Supplier.Create(id);
            db.Add(supplier);
        }

        try
        {
            supplier.Update(request.Name, request.CustomerNumber, request.ContactName, request.Phone, request.Email, request.Street,
                request.PostalCode, request.City, request.OrderNotes, request.IsActive);
        }
        catch (ArgumentException exception)
        {
            return Problems.Validation(exception.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ToResponse(supplier));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteSupplierAsync(
        Guid id, EstrichOneDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var supplier = await db.Set<Supplier>().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (supplier is null)
        {
            return Problems.NotFound("SUPPLIER_NOT_FOUND");
        }

        if (await db.Set<MaterialOrder>().AnyAsync(o => o.SupplierId == id && (o.Status == OrderStatus.Ordered || o.Status == OrderStatus.Confirmed),
                cancellationToken))
        {
            return Problems.Conflict("SUPPLIER_HAS_OPEN_ORDERS", "Beim Lieferanten sind noch Bestellungen offen.");
        }

        supplier.Delete(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<List<MaterialResponse>>> ListMaterialsAsync(
        EstrichOneDbContext db, CancellationToken cancellationToken,
        [FromQuery] string? search = null, [FromQuery] string? category = null, [FromQuery] Guid? supplierId = null,
        [FromQuery] bool includeInactive = false)
    {
        var query = db.Set<Material>().AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(m => m.IsActive);
        }

        if (Enum.TryParse<MaterialCategory>(category, ignoreCase: true, out var c))
        {
            query = query.Where(m => m.Category == c);
        }

        if (supplierId is { } supplier)
        {
            query = query.Where(m => m.SupplierId == supplier);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(m => EF.Functions.ILike(m.Name, pattern) || (m.ArticleNumber != null && EF.Functions.ILike(m.ArticleNumber, pattern)));
        }

        var rows = await query.OrderBy(m => m.Category).ThenBy(m => m.Name).ToListAsync(cancellationToken);
        return TypedResults.Ok(await ToResponsesAsync(db, rows, cancellationToken));
    }

    private static async Task<Results<Ok<MaterialResponse>, ProblemHttpResult>> SaveMaterialAsync(
        Guid id, SaveMaterialRequest request, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        if (request.Id != id)
        {
            return Problems.Validation("Die ID passt nicht zur Adresse.");
        }

        if (request.SupplierId is { } supplierId && !await db.Set<Supplier>().AnyAsync(s => s.Id == supplierId, cancellationToken))
        {
            return Problems.Validation("Der Lieferant existiert nicht.");
        }

        var material = await db.Set<Material>().SingleOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (material is null)
        {
            material = Material.Create(id);
            db.Add(material);
        }

        try
        {
            material.Update(request.Name, request.ArticleNumber, request.Category, request.Unit, request.SupplierId, request.Price, request.Notes,
                request.IsActive);
        }
        catch (ArgumentException exception)
        {
            return Problems.Validation(exception.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok((await ToResponsesAsync(db, [material], cancellationToken))[0]);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteMaterialAsync(
        Guid id, EstrichOneDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var material = await db.Set<Material>().SingleOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (material is null)
        {
            return Problems.NotFound("MATERIAL_NOT_FOUND");
        }

        // Bestellpositionen behalten Bezeichnung, Menge und Preis; der Verweis auf den Artikel bleibt bestehen (Tombstone).
        material.Delete(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static SupplierResponse ToResponse(Supplier s) => new(s.Id, s.Name, s.CustomerNumber, s.ContactName, s.Phone, s.Email, s.Street,
        s.PostalCode, s.City, s.OrderNotes, s.IsActive, s.Version);

    private static async Task<List<MaterialResponse>> ToResponsesAsync(EstrichOneDbContext db, IReadOnlyList<Material> rows, CancellationToken cancellationToken)
    {
        var ids = rows.Select(m => m.SupplierId).OfType<Guid>().Distinct().ToList();
        var names = await db.Set<Supplier>().AsNoTracking().IgnoreQueryFilters([QueryFilters.NotDeleted]).Where(s => ids.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);
        return rows.Select(m => new MaterialResponse(m.Id, m.Name, m.ArticleNumber, m.Category, m.Unit, m.SupplierId,
            m.SupplierId is { } sid ? names.GetValueOrDefault(sid) : null, m.Price, m.Notes, m.IsActive, m.Version)).ToList();
    }
}

internal static class Problems
{
    public static ProblemHttpResult NotFound(string code) => TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Nicht gefunden",
        extensions: new Dictionary<string, object?> { ["code"] = code });

    public static ProblemHttpResult Conflict(string code, string detail) => TypedResults.Problem(statusCode: StatusCodes.Status409Conflict,
        title: "Nicht möglich", detail: detail, extensions: new Dictionary<string, object?> { ["code"] = code });

    public static ProblemHttpResult Validation(string detail) => TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity,
        title: "Ungültige Angaben", detail: detail, extensions: new Dictionary<string, object?> { ["code"] = "VALIDATION_FAILED" });
}
