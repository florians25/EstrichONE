using System.ComponentModel.DataAnnotations;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Devices.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EstrichOne.Api.Modules.Devices;

public sealed record RegisterDeviceRequest(
    [property: Required, MaxLength(100)] string Name,
    DevicePlatform Platform,
    DeviceKind Kind,
    [property: Required, MaxLength(300)] string PublicKey,
    [property: MaxLength(100)] string? Model = null,
    [property: MaxLength(50)] string? OsVersion = null,
    [property: MaxLength(50)] string? AppVersion = null);

public sealed record DeviceResponse(
    Guid Id, string Name, DevicePlatform Platform, DeviceKind Kind, DeviceStatus Status, string? Model,
    string? OsVersion, string? AppVersion, Guid? AssignedUserId, DateTimeOffset RegisteredAt,
    DateTimeOffset? ApprovedAt, DateTimeOffset? LastSeenAt, long Version);

internal static class DeviceEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var devices = api.MapGroup("/devices").WithTags("Geräte");

        devices.MapPost("/register", RegisterAsync).WithName("RegisterDevice")
            .WithSummary("Gerät für die aktuelle Sitzung registrieren (nach der ersten Anmeldung in der App)")
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .AllowPendingDevice();

        devices.MapGet("/me", GetMineAsync).WithName("GetMyDevice")
            .WithSummary("Status des Geräts dieser Sitzung (z. B. für „Wartet auf Freigabe“)")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .AllowPendingDevice();

        devices.MapGet("/", ListAsync).WithName("ListDevices").RequirePermission(Permissions.DevicesRead);

        devices.MapPost("/{id:guid}/approve", (Guid id, DeviceActions actions, CancellationToken ct) => actions.ApplyAsync(id, DeviceAction.Approve, ct))
            .WithName("ApproveDevice").ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.DevicesManage);
        devices.MapPost("/{id:guid}/lock", (Guid id, DeviceActions actions, CancellationToken ct) => actions.ApplyAsync(id, DeviceAction.Lock, ct))
            .WithName("LockDevice").ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.DevicesManage);
        devices.MapPost("/{id:guid}/unlock", (Guid id, DeviceActions actions, CancellationToken ct) => actions.ApplyAsync(id, DeviceAction.Unlock, ct))
            .WithName("UnlockDevice").ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.DevicesManage);
        devices.MapPost("/{id:guid}/retire", (Guid id, DeviceActions actions, CancellationToken ct) => actions.ApplyAsync(id, DeviceAction.Retire, ct))
            .WithName("RetireDevice").WithSummary("Aus der Verwaltung entfernen; beendet alle Sitzungen des Geräts")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.DevicesManage);
    }

    private static async Task<Results<Created<DeviceResponse>, ProblemHttpResult>> RegisterAsync(
        RegisterDeviceRequest request, EstrichOneDbContext db, ICurrentUser currentUser, ISessionDevices sessionDevices,
        IUserPermissions permissions, IOptions<DeviceSettings> settings, IAuditLog audit, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (await sessionDevices.GetCurrentSessionDeviceAsync(cancellationToken) is not null)
        {
            return Problem(StatusCodes.Status409Conflict, "DEVICE_ALREADY_REGISTERED",
                "Diese Sitzung ist bereits einem Gerät zugeordnet.");
        }

        Device device;
        try
        {
            device = Device.Register(request.Name, request.Platform, request.Kind, Convert.FromBase64String(request.PublicKey),
                currentUser.UserId, request.Model, request.OsVersion, request.AppVersion, clock.GetUtcNow());
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, "INVALID_DEVICE_KEY",
                "Der Geräteschlüssel muss ein öffentlicher P-256-Schlüssel (SubjectPublicKeyInfo, Base64) sein.");
        }

        // Ein Gerät stuft sich nie selbst als vertrauenswürdig ein: Poolgeräte warten immer auf einen Administrator.
        var autoApprove = request.Kind == DeviceKind.Personal
            && settings.Value.AutoApprovePersonalDevices
            && await permissions.HasAsync(Permissions.DevicesSelfEnroll, cancellationToken);
        if (autoApprove)
        {
            device.Approve(approvedByUserId: null, clock.GetUtcNow());
        }

        db.Add(device);
        await sessionDevices.BindCurrentSessionAsync(device.Id, cancellationToken);
        audit.Record("devices.registered", currentUser.UserId, "device", device.Id.ToString(), new Dictionary<string, string?>
        {
            ["kind"] = device.Kind.ToString(),
            ["platform"] = device.Platform.ToString(),
            ["status"] = device.Status.ToString(),
        });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/v1/devices/{device.Id}", ToResponse(device));
    }

    private static async Task<Results<Ok<DeviceResponse>, ProblemHttpResult>> GetMineAsync(
        EstrichOneDbContext db, ISessionDevices sessionDevices, CancellationToken cancellationToken)
    {
        var device = await sessionDevices.GetCurrentSessionDeviceAsync(cancellationToken) is { } id
            ? await db.Set<Device>().AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, cancellationToken)
            : null;
        return device is null
            ? Problem(StatusCodes.Status404NotFound, "DEVICE_NOT_REGISTERED", "Diese Sitzung hat noch kein registriertes Gerät.")
            : TypedResults.Ok(ToResponse(device));
    }

    private static async Task<Ok<List<DeviceResponse>>> ListAsync(EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var devices = await db.Set<Device>().AsNoTracking()
            .OrderBy(d => d.Status == DeviceStatus.Pending ? 0 : 1).ThenBy(d => d.Name)
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(devices.Select(ToResponse).ToList());
    }

    internal static DeviceResponse ToResponse(Device d) => new(d.Id, d.Name, d.Platform, d.Kind, d.Status, d.Model,
        d.OsVersion, d.AppVersion, d.AssignedUserId, d.CreatedAt, d.ApprovedAt, d.LastSeenAt, d.Version);

    internal static ProblemHttpResult Problem(int status, string code, string title) =>
        TypedResults.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });
}

internal enum DeviceAction
{
    Approve,
    Lock,
    Unlock,
    Retire,
}

internal sealed class DeviceActions(EstrichOneDbContext db, ISessionDevices sessionDevices, ICurrentUser currentUser,
    IAuditLog audit, TimeProvider clock)
{
    public async Task<Results<Ok<DeviceResponse>, ProblemHttpResult>> ApplyAsync(Guid id, DeviceAction action, CancellationToken cancellationToken)
    {
        var device = await db.Set<Device>().SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (device is null)
        {
            return DeviceEndpoints.Problem(StatusCodes.Status404NotFound, "DEVICE_NOT_FOUND", "Gerät nicht gefunden");
        }

        switch (action)
        {
            case DeviceAction.Approve when device.Status == DeviceStatus.Retired:
                return DeviceEndpoints.Problem(StatusCodes.Status409Conflict, "DEVICE_RETIRED", "Ein entferntes Gerät kann nicht freigegeben werden.");
            case DeviceAction.Approve:
                device.Approve(currentUser.UserId, clock.GetUtcNow());
                break;
            case DeviceAction.Lock:
                device.Lock();
                break;
            case DeviceAction.Unlock:
                device.Unlock();
                break;
            case DeviceAction.Retire:
                device.Retire();
                await sessionDevices.RevokeDeviceSessionsAsync(device.Id, "device_retired", cancellationToken);
                break;
        }

        audit.Record($"devices.{action.ToString().ToLowerInvariant()}", currentUser.UserId, "device", device.Id.ToString());
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(DeviceEndpoints.ToResponse(device));
    }
}
