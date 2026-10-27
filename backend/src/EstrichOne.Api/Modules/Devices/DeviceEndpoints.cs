using System.ComponentModel.DataAnnotations;
using EstrichOne.Api.Infrastructure.Hosting;
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
    [property: MaxLength(50)] string? AppVersion = null,
    [property: MaxLength(20)] string? EnrollmentCode = null);

/// <param name="Signature">Base64, ECDSA P-256/SHA-256 über <c>"EstrichONE attach v1\n" + Zugriffstoken dieser Sitzung</c>.</param>
public sealed record AttachDeviceRequest(Guid DeviceId, [property: Required, MaxLength(200)] string Signature);

public sealed record CreateEnrollmentCodeRequest(DeviceKind Kind, [property: MaxLength(100)] string? DeviceName = null);

/// <param name="Link">Inhalt des QR-Codes; <c>null</c>, wenn keine öffentliche Serveradresse konfiguriert ist.</param>
public sealed record EnrollmentCodeResponse(string Code, DeviceKind Kind, string? DeviceName, DateTimeOffset ExpiresAt, string? Link);

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
            .AllowPendingDevice()
            .RequireRateLimiting(DeviceSettings.RateLimitPolicy);

        devices.MapPost("/attach", AttachAsync).WithName("AttachDevice")
            .WithSummary("Weitere Anmeldung auf einem bereits registrierten Gerät (Poolgerät, Neuanmeldung) an das Gerät binden")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .AllowPendingDevice();

        devices.MapPost("/enrollment-codes", CreateEnrollmentCodeAsync).WithName("CreateEnrollmentCode")
            .WithSummary("Einmal-Einrichtungscode (15 Minuten gültig) für ein neues Gerät erzeugen")
            .RequirePermission(Permissions.DevicesManage);

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

        byte[] publicKey;
        try
        {
            publicKey = Convert.FromBase64String(request.PublicKey);
        }
        catch (FormatException)
        {
            return InvalidKey();
        }

        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Ein eingelöster Code ersetzt die Freigabe: Der Administrator hat das Gerät bewusst eingerichtet (11, Abschnitt 2).
        RedeemedCode? code = null;
        if (!string.IsNullOrWhiteSpace(request.EnrollmentCode))
        {
            var hash = EnrollmentCode.Hash(request.EnrollmentCode);
            code = (await db.Database.SqlQuery<RedeemedCode>($"""
                UPDATE device_enrollment_codes SET used_at = {now}
                WHERE code_hash = {hash} AND used_at IS NULL AND expires_at > {now}
                RETURNING id, kind, device_name, created_by_user_id
                """).ToListAsync(cancellationToken)).SingleOrDefault();
            if (code is null)
            {
                audit.Record("devices.enrollment_code_rejected", currentUser.UserId, "device", null);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return Problem(StatusCodes.Status422UnprocessableEntity, "ENROLLMENT_CODE_INVALID",
                    "Der Einrichtungscode ist abgelaufen oder wurde bereits verwendet. Bitte im IT Center einen neuen Code erzeugen.");
            }
        }

        var kind = code is null ? request.Kind : Enum.Parse<DeviceKind>(code.Kind);
        Device device;
        try
        {
            device = Device.Register(code?.DeviceName ?? request.Name, request.Platform, kind, publicKey,
                currentUser.UserId, request.Model, request.OsVersion, request.AppVersion, now);
        }
        catch (ArgumentException)
        {
            return InvalidKey();
        }

        if (code is not null)
        {
            device.Approve(code.CreatedByUserId, now);
        }
        else if (kind == DeviceKind.Personal
            && settings.Value.AutoApprovePersonalDevices
            && await permissions.HasAsync(Permissions.DevicesSelfEnroll, cancellationToken))
        {
            // Ein Gerät stuft sich nie selbst als vertrauenswürdig ein: Poolgeräte warten immer auf einen Administrator.
            device.Approve(approvedByUserId: null, now);
        }

        db.Add(device);
        await sessionDevices.BindCurrentSessionAsync(device.Id, cancellationToken);
        audit.Record("devices.registered", currentUser.UserId, "device", device.Id.ToString(), new Dictionary<string, string?>
        {
            ["kind"] = device.Kind.ToString(),
            ["platform"] = device.Platform.ToString(),
            ["status"] = device.Status.ToString(),
            ["enrollmentCode"] = code?.Id.ToString(),
        });
        await db.SaveChangesAsync(cancellationToken);
        if (code is not null)
        {
            await db.Database.ExecuteSqlAsync(
                $"UPDATE device_enrollment_codes SET used_by_device_id = {device.Id} WHERE id = {code.Id}", cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return TypedResults.Created($"/api/v1/devices/{device.Id}", ToResponse(device));

        static ProblemHttpResult InvalidKey() => Problem(StatusCodes.Status422UnprocessableEntity, "INVALID_DEVICE_KEY",
            "Der Geräteschlüssel muss ein öffentlicher P-256-Schlüssel (SubjectPublicKeyInfo, Base64) sein.");
    }

    /// <summary>
    /// Bindet eine neue Sitzung an ein vorhandenes Gerät. Der Nachweis ist eine Signatur des Geräteschlüssels über
    /// das Zugriffstoken dieser Sitzung: Sie gilt nur für genau diese Sitzung und lässt sich nicht wiederverwenden.
    /// </summary>
    private static async Task<Results<Ok<DeviceResponse>, ProblemHttpResult>> AttachAsync(
        AttachDeviceRequest request, HttpContext http, EstrichOneDbContext db, ICurrentUser currentUser,
        ISessionDevices sessionDevices, IAuditLog audit, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (await sessionDevices.GetCurrentSessionDeviceAsync(cancellationToken) is not null)
        {
            return Problem(StatusCodes.Status409Conflict, "DEVICE_ALREADY_REGISTERED", "Diese Sitzung ist bereits einem Gerät zugeordnet.");
        }

        var device = await db.Set<Device>().SingleOrDefaultAsync(d => d.Id == request.DeviceId, cancellationToken);
        if (device is null || device.Status == DeviceStatus.Retired)
        {
            return Problem(StatusCodes.Status404NotFound, "DEVICE_NOT_FOUND", "Dieses Gerät ist nicht (mehr) registriert. Bitte die App neu einrichten.");
        }

        var accessToken = http.Request.Headers.Authorization.ToString() is { } header && header.StartsWith("Bearer ", StringComparison.Ordinal)
            ? header["Bearer ".Length..].Trim()
            : string.Empty;
        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(request.Signature);
        }
        catch (FormatException)
        {
            signature = [];
        }

        if (accessToken.Length == 0 || !device.VerifyAttachProof(accessToken, signature))
        {
            return Problem(StatusCodes.Status403Forbidden, "DEVICE_PROOF_INVALID", "Die Gerätesignatur ist ungültig.");
        }

        if (device.Kind == DeviceKind.Personal && device.AssignedUserId != currentUser.UserId)
        {
            return Problem(StatusCodes.Status403Forbidden, "DEVICE_NOT_ASSIGNED",
                "Dieses Gerät ist einer anderen Person zugeordnet. Für mehrere Personen muss es als Poolgerät eingerichtet sein.");
        }

        device.ReportVersions(device.OsVersion, device.AppVersion, clock.GetUtcNow());
        await sessionDevices.BindCurrentSessionAsync(device.Id, cancellationToken);
        audit.Record("devices.attached", currentUser.UserId, "device", device.Id.ToString());
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ToResponse(device));
    }

    private static async Task<Ok<EnrollmentCodeResponse>> CreateEnrollmentCodeAsync(
        CreateEnrollmentCodeRequest request, EstrichOneDbContext db, ICurrentUser currentUser, IAuditLog audit,
        IOptions<EstrichOneOptions> options, TimeProvider clock, HttpContext http, CancellationToken cancellationToken)
    {
        var (entity, code) = EnrollmentCode.Create(request.Kind, request.DeviceName, currentUser.UserId, clock.GetUtcNow());
        db.Add(entity);
        audit.Record("devices.enrollment_code_created", currentUser.UserId, "enrollment_code", entity.Id.ToString(),
            new Dictionary<string, string?> { ["kind"] = entity.Kind.ToString() });
        await db.SaveChangesAsync(cancellationToken);

        var kind = entity.Kind == DeviceKind.Pool ? "pool" : "personal";
        var server = PublicAddress.Resolve(options.Value, http.Request);
        var link = $"estrichone://enroll?v=1&server={Uri.EscapeDataString(server)}&code={code}&type={kind}";
        return TypedResults.Ok(new EnrollmentCodeResponse(code, entity.Kind, entity.DeviceName, entity.ExpiresAt, link));
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

internal sealed record RedeemedCode(Guid Id, string Kind, string? DeviceName, Guid CreatedByUserId);

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
