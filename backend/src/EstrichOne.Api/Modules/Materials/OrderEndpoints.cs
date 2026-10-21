using System.ComponentModel.DataAnnotations;
using System.Globalization;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Materials;

public sealed record OrderLineResponse(
    Guid Id, int Position, Guid? MaterialId, string? ArticleNumber, string Description, decimal Quantity, string Unit, decimal? UnitPrice,
    decimal? Total);

public sealed record OrderResponse(
    Guid Id, string Number, OrderStatus Status, Guid SupplierId, string SupplierName, string? SupplierCustomerNumber, string? SupplierEmail,
    string? SupplierPhone, Guid? BauakteId, string? BauakteNumber, string? BauakteTitle, DateOnly? DeliveryDate, string? DeliveryTime,
    string? DeliveryStreet, string? DeliveryPostalCode, string? DeliveryCity, string? SiteContact, string? Notes, IReadOnlyList<OrderLineResponse> Lines,
    decimal? Total, Guid CreatedByUserId, string? CreatedByName, DateTimeOffset? OrderedAt, DateTimeOffset? DeliveredAt, DateTimeOffset UpdatedAt,
    long Version);

public sealed record SaveOrderLineRequest(
    [property: Required] Guid Id,
    Guid? MaterialId,
    [property: Required, MaxLength(300)] string Description,
    [property: Range(0.001, 1_000_000)] decimal Quantity,
    [property: Required, MaxLength(20)] string Unit,
    [property: Range(0, 1_000_000)] decimal? UnitPrice = null);

/// <param name="DeliveryStreet">Leer und mit Bauakte: Baustellenadresse der Bauakte.</param>
public sealed record SaveOrderRequest(
    [property: Required] Guid Id,
    [property: Required] Guid SupplierId,
    Guid? BauakteId = null,
    DateOnly? DeliveryDate = null,
    [property: MaxLength(50)] string? DeliveryTime = null,
    [property: MaxLength(200)] string? DeliveryStreet = null,
    [property: MaxLength(10)] string? DeliveryPostalCode = null,
    [property: MaxLength(100)] string? DeliveryCity = null,
    [property: MaxLength(200)] string? SiteContact = null,
    [property: MaxLength(4000)] string? Notes = null,
    IReadOnlyList<SaveOrderLineRequest>? Lines = null);

public sealed record SetOrderStatusRequest(OrderStatus Status);

/// <summary>Materialbestellungen (AP-20): Entwurf, Bestellt, Bestätigt, Geliefert, Storniert.</summary>
internal static class OrderEndpoints
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    public static void Map(IEndpointRouteBuilder api)
    {
        var orders = api.MapGroup("/material-orders").WithTags("Material").RequireMaterialsModule();
        orders.MapGet("/", ListAsync).WithName("ListMaterialOrders")
            .WithSummary("Bestellungen; open=true nur Bestellt/Bestätigt; from/to auf den Liefertag")
            .RequirePermission(Permissions.MaterialsRead);
        orders.MapGet("/{id:guid}", GetAsync).WithName("GetMaterialOrder").ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(Permissions.MaterialsRead);
        orders.MapPut("/{id:guid}", SaveAsync).WithName("SaveMaterialOrder").WithSummary("Bestellung anlegen oder ändern (mit allen Positionen)")
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.MaterialsManage);
        orders.MapPost("/{id:guid}/status", SetStatusAsync).WithName("SetMaterialOrderStatus")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict).RequirePermission(Permissions.MaterialsManage);
        orders.MapDelete("/{id:guid}", DeleteAsync).WithName("DeleteMaterialOrder").WithSummary("Entwurf oder stornierte Bestellung löschen")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict).RequirePermission(Permissions.MaterialsManage);
    }

    private static async Task<Ok<List<OrderResponse>>> ListAsync(
        EstrichOneDbContext db, IBauakteAccess access, CancellationToken cancellationToken,
        [FromQuery] string? status = null, [FromQuery] bool open = false, [FromQuery] Guid? supplierId = null, [FromQuery] Guid? bauakteId = null,
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, [FromQuery] string? search = null, [FromQuery] int? limit = null)
    {
        var visible = (await access.VisibleAsync(db.Set<Bauakte>(), cancellationToken)).Select(b => b.Id);
        var query = db.Set<MaterialOrder>().AsNoTracking().Where(o => o.BauakteId == null || visible.Contains(o.BauakteId.Value));
        if (Enum.TryParse<OrderStatus>(status, ignoreCase: true, out var s))
        {
            query = query.Where(o => o.Status == s);
        }

        if (open)
        {
            query = query.Where(o => o.Status == OrderStatus.Ordered || o.Status == OrderStatus.Confirmed);
        }

        if (supplierId is { } supplier)
        {
            query = query.Where(o => o.SupplierId == supplier);
        }

        if (bauakteId is { } bauakte)
        {
            query = query.Where(o => o.BauakteId == bauakte);
        }

        if (from is { } f)
        {
            query = query.Where(o => o.DeliveryDate >= f);
        }

        if (to is { } t)
        {
            query = query.Where(o => o.DeliveryDate <= t);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(o => EF.Functions.ILike(o.Number, pattern)
                || (o.Notes != null && EF.Functions.ILike(o.Notes, pattern))
                || (o.DeliveryCity != null && EF.Functions.ILike(o.DeliveryCity, pattern))
                || o.Lines.Any(l => EF.Functions.ILike(l.Description, pattern)));
        }

        var rows = await query.OrderBy(o => o.DeliveryDate == null).ThenByDescending(o => o.DeliveryDate).ThenByDescending(o => o.Number)
            .Take(Math.Clamp(limit ?? 200, 1, 500)).ToListAsync(cancellationToken);
        return TypedResults.Ok(await ToResponsesAsync(db, rows, cancellationToken));
    }

    private static async Task<Results<Ok<OrderResponse>, ProblemHttpResult>> GetAsync(
        Guid id, EstrichOneDbContext db, IBauakteAccess access, CancellationToken cancellationToken)
    {
        var order = await db.Set<MaterialOrder>().AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null || (order.BauakteId is { } b && !await access.CanReadAsync(b, cancellationToken)))
        {
            return Problems.NotFound("ORDER_NOT_FOUND");
        }

        return TypedResults.Ok((await ToResponsesAsync(db, [order], cancellationToken))[0]);
    }

    private static async Task<Results<Ok<OrderResponse>, ProblemHttpResult>> SaveAsync(
        Guid id, SaveOrderRequest request, EstrichOneDbContext db, IBauakteAccess access, ICurrentUser currentUser, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (request.Id != id)
        {
            return Problems.Validation("Die ID passt nicht zur Adresse.");
        }

        if (!await db.Set<Supplier>().AnyAsync(s => s.Id == request.SupplierId, cancellationToken))
        {
            return Problems.Validation("Bitte einen Lieferanten wählen.");
        }

        Bauakte? bauakte = null;
        if (request.BauakteId is { } bauakteId)
        {
            bauakte = await db.Set<Bauakte>().AsNoTracking().SingleOrDefaultAsync(b => b.Id == bauakteId, cancellationToken);
            if (bauakte is null || !await access.CanReadAsync(bauakteId, cancellationToken))
            {
                return Problems.Validation("Die Bauakte existiert nicht.");
            }
        }

        var lineRequests = request.Lines ?? [];
        var materialIds = lineRequests.Select(l => l.MaterialId).OfType<Guid>().Distinct().ToList();
        if (materialIds.Count > 0 && await db.Set<Material>().CountAsync(m => materialIds.Contains(m.Id), cancellationToken) != materialIds.Count)
        {
            return Problems.Validation("Ein Artikel existiert nicht mehr.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var order = await db.Set<MaterialOrder>().SingleOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            order = MaterialOrder.Create(id, await NextNumberAsync(db, clock, cancellationToken), currentUser.UserId);
            db.Add(order);
        }
        else if (order.BauakteId is { } previous && previous != request.BauakteId && !await access.CanReadAsync(previous, cancellationToken))
        {
            return Problems.NotFound("ORDER_NOT_FOUND");
        }

        // Ohne eigene Lieferadresse wird auf die Baustelle geliefert.
        var useSite = bauakte is not null && string.IsNullOrWhiteSpace(request.DeliveryStreet) && string.IsNullOrWhiteSpace(request.DeliveryCity);
        var siteContact = request.SiteContact ?? (bauakte?.SiteContactName is { } name
            ? bauakte.SiteContactPhone is { } phone ? $"{name}, {phone}" : name
            : null);
        try
        {
            var lines = lineRequests.Select((l, i) => new MaterialOrderLine(l.Id, id, i + 1, l.MaterialId, l.Description, l.Quantity, l.Unit, l.UnitPrice))
                .ToList();
            order.Update(request.SupplierId, request.BauakteId, request.DeliveryDate, request.DeliveryTime,
                useSite ? bauakte!.Street : request.DeliveryStreet, useSite ? bauakte!.PostalCode : request.DeliveryPostalCode,
                useSite ? bauakte!.City : request.DeliveryCity, siteContact, request.Notes, lines);
        }
        catch (ArgumentException exception)
        {
            return Problems.Validation(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return Problems.Conflict("ORDER_NOT_EDITABLE", exception.Message);
        }

        // Positionen allein ändern den Kopf nicht – trotzdem soll die Version steigen.
        if (db.Entry(order).State == EntityState.Unchanged)
        {
            db.Entry(order).Property(o => o.UpdatedAt).IsModified = true;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TypedResults.Ok((await ToResponsesAsync(db, [order], cancellationToken))[0]);
    }

    private static async Task<Results<Ok<OrderResponse>, ProblemHttpResult>> SetStatusAsync(
        Guid id, SetOrderStatusRequest request, EstrichOneDbContext db, IBauakteAccess access, TimeProvider clock, OrderPdf pdf, ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var order = await db.Set<MaterialOrder>().Include(o => o.Lines).SingleOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null || (order.BauakteId is { } b && !await access.CanReadAsync(b, cancellationToken)))
        {
            return Problems.NotFound("ORDER_NOT_FOUND");
        }

        try
        {
            order.ChangeStatus(request.Status, clock.GetUtcNow());
        }
        catch (InvalidOperationException exception)
        {
            return Problems.Conflict("ORDER_STATUS_CONFLICT", exception.Message);
        }

        if (request.Status == OrderStatus.Ordered)
        {
            // Bestellt: Bestellschein automatisch zur Bauakte.
            await pdf.StoreAsync(order, await pdf.RenderAsync(order, cancellationToken), currentUser.UserId, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok((await ToResponsesAsync(db, [order], cancellationToken))[0]);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id, EstrichOneDbContext db, IBauakteAccess access, TimeProvider clock, CancellationToken cancellationToken)
    {
        var order = await db.Set<MaterialOrder>().SingleOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null || (order.BauakteId is { } b && !await access.CanReadAsync(b, cancellationToken)))
        {
            return Problems.NotFound("ORDER_NOT_FOUND");
        }

        try
        {
            order.Delete(clock.GetUtcNow());
        }
        catch (InvalidOperationException exception)
        {
            return Problems.Conflict("ORDER_STATUS_CONFLICT", exception.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>Wie bei Bauaktennummern: atomares UPSERT in derselben Transaktion, damit keine Lücken entstehen.</summary>
    private static async Task<string> NextNumberAsync(EstrichOneDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var year = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Berlin).Year;
        var rows = await db.Database
            .SqlQuery<int>($"""
                INSERT INTO order_number_sequences (year, last_number) VALUES ({year}, 1)
                ON CONFLICT (year) DO UPDATE SET last_number = order_number_sequences.last_number + 1
                RETURNING last_number AS "Value"
                """)
            .ToListAsync(cancellationToken);
        return string.Create(CultureInfo.InvariantCulture, $"B-{year}-{rows.Single():0000}");
    }

    private static async Task<List<OrderResponse>> ToResponsesAsync(EstrichOneDbContext db, IReadOnlyList<MaterialOrder> orders, CancellationToken cancellationToken)
    {
        var supplierIds = orders.Select(o => o.SupplierId).Distinct().ToList();
        var bauakteIds = orders.Select(o => o.BauakteId).OfType<Guid>().Distinct().ToList();
        var userIds = orders.Select(o => o.CreatedByUserId).Distinct().ToList();
        var materialIds = orders.SelectMany(o => o.Lines).Select(l => l.MaterialId).OfType<Guid>().Distinct().ToList();
        var suppliers = await db.Set<Supplier>().AsNoTracking().IgnoreQueryFilters([QueryFilters.NotDeleted]).Where(s => supplierIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, cancellationToken);
        var bauakten = await db.Set<Bauakte>().AsNoTracking().Where(b => bauakteIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => (b.Number, b.Title), cancellationToken);
        var users = await db.Set<User>().AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
        var articles = await db.Set<Material>().AsNoTracking().IgnoreQueryFilters([QueryFilters.NotDeleted]).Where(m => materialIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m.ArticleNumber, cancellationToken);

        return orders.Select(o =>
        {
            var supplier = suppliers[o.SupplierId];
            var lines = o.Lines.OrderBy(l => l.Position).Select(l => new OrderLineResponse(l.Id, l.Position, l.MaterialId,
                l.MaterialId is { } mid ? articles.GetValueOrDefault(mid) : null, l.Description, l.Quantity, l.Unit, l.UnitPrice,
                l.UnitPrice is { } price ? Math.Round(price * l.Quantity, 2) : null)).ToList();
            var priced = lines.Where(l => l.Total is not null).ToList();
            var bauakte = o.BauakteId is { } bid && bauakten.TryGetValue(bid, out var x) ? x : ((string?)null, (string?)null);
            return new OrderResponse(o.Id, o.Number, o.Status, o.SupplierId, supplier.Name, supplier.CustomerNumber, supplier.Email, supplier.Phone,
                o.BauakteId, bauakte.Item1, bauakte.Item2, o.DeliveryDate, o.DeliveryTime, o.DeliveryStreet, o.DeliveryPostalCode, o.DeliveryCity,
                o.SiteContact, o.Notes, lines, priced.Count == 0 ? null : priced.Sum(l => l.Total!.Value), o.CreatedByUserId,
                users.GetValueOrDefault(o.CreatedByUserId), o.OrderedAt, o.DeliveredAt, o.UpdatedAt, o.Version);
        }).ToList();
    }
}
