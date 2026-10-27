using System.ComponentModel.DataAnnotations;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Notifications.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Notifications;

public sealed record NotificationResponse(
    Guid Id, NotificationCategory Category, NotificationUrgency Urgency, string Title, string? Body, string? Link, string? ObjectType, Guid? ObjectId,
    string? SenderName, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

public sealed record NotificationInbox(IReadOnlyList<NotificationResponse> Items, int UnreadCount);

public sealed record UnreadCountResponse(int Count);

/// <param name="Token">APNs-/FCM-Gerätetoken bzw. Endpunkt-URL des Web-Push-Abos.</param>
/// <param name="P256dh">Nur Web: öffentlicher Schlüssel des Abos (Base64url).</param>
/// <param name="Auth">Nur Web: Auth-Geheimnis des Abos (Base64url).</param>
/// <param name="Sandbox">Nur iOS: Token aus einem Entwicklungs-Build (APNs-Sandbox).</param>
public sealed record RegisterPushDeviceRequest(
    PushPlatform Platform,
    [property: Required, MaxLength(2000)] string Token,
    [property: MaxLength(200)] string? P256dh = null,
    [property: MaxLength(100)] string? Auth = null,
    bool Sandbox = false,
    [property: MaxLength(200)] string? BundleId = null,
    [property: MaxLength(200)] string? DeviceName = null,
    [property: MaxLength(50)] string? AppVersion = null);

public sealed record PushDeviceResponse(
    Guid Id, PushPlatform Platform, string? DeviceName, string? AppVersion, bool Sandbox, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt,
    DateTimeOffset? LastSuccessAt, string? LastError);

/// <summary>Was Clients zum Einrichten brauchen (für alle Angemeldeten).</summary>
public sealed record PushClientConfig(bool WebPushEnabled, string? VapidPublicKey, bool ApnsEnabled, bool ShowContent);

/// <param name="All">An alle aktiven Benutzer.</param>
public sealed record SendNotificationRequest(
    [property: Required, MaxLength(200)] string Title,
    [property: MaxLength(2000)] string? Body = null,
    [property: MaxLength(500)] string? Link = null,
    NotificationUrgency Urgency = NotificationUrgency.Normal,
    IReadOnlyList<Guid>? UserIds = null,
    IReadOnlyList<Guid>? RoleIds = null,
    IReadOnlyList<Guid>? CrewIds = null,
    bool All = false);

public sealed record BroadcastResponse(
    Guid Id, string Title, string? Body, string? Link, NotificationUrgency Urgency, string Audience, string SenderName, DateTimeOffset CreatedAt,
    int Recipients, int Read, int Pushed, int InAppOnly, int Failed, int Pending);

/// <summary>Auswahl beim Senden: Rollen und Kolonnen (Personen über /directory/users).</summary>
public sealed record NotificationAudiences(IReadOnlyList<AudienceOption> Roles, IReadOnlyList<AudienceOption> Crews, int ActiveUsers);

public sealed record BroadcastRecipient(Guid UserId, string Name, DateTimeOffset? ReadAt, PushState PushState, DateTimeOffset? PushedAt, string? PushError);

public sealed record BroadcastDetail(BroadcastResponse Broadcast, IReadOnlyList<BroadcastRecipient> Recipients);

public sealed record PushSettingsResponse(
    bool WebPushEnabled, string? VapidPublicKey, string VapidSubject, bool ApnsEnabled, string? ApnsKeyId, string? ApnsTeamId, string? ApnsBundleId,
    bool ApnsProduction, bool ApnsKeyStored, bool ApnsFromEnvironment, bool ApnsReady, bool ShowContent, int WebDevices, int IosDevices,
    int AndroidDevices, long Version);

/// <param name="ApnsKey">Inhalt der .p8-Datei (PEM); leer = gespeicherten Schlüssel behalten.</param>
public sealed record SavePushSettingsRequest(
    bool WebPushEnabled = true,
    [property: MaxLength(200)] string? VapidSubject = null,
    bool ApnsEnabled = false,
    [property: MaxLength(20)] string? ApnsKeyId = null,
    [property: MaxLength(20)] string? ApnsTeamId = null,
    [property: MaxLength(200)] string? ApnsBundleId = null,
    bool ApnsProduction = true,
    [property: MaxLength(10_000)] string? ApnsKey = null,
    bool RemoveApnsKey = false,
    bool ShowContent = true,
    long? Version = null);

/// <summary>
/// Postfach je Benutzer, Geräte für Pushnachrichten, Versand durch das Büro (Recht „notifications.send“) und die
/// Push-Einrichtung (Recht „settings.manage“). Siehe docs/benachrichtigungen.md.
/// </summary>
internal static class NotificationEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/notifications").WithTags("Benachrichtigungen");
        group.MapGet("/", InboxAsync).WithName("ListNotifications").WithSummary("Eigenes Postfach, neueste zuerst; unread=true nur ungelesene");
        group.MapGet("/unread-count", UnreadAsync).WithName("GetUnreadNotificationCount");
        group.MapPost("/{id:guid}/read", (Guid id, EstrichOneDbContext db, ICurrentUser user, TimeProvider clock, CancellationToken ct) =>
            MarkAsync(id, true, db, user, clock, ct)).WithName("MarkNotificationRead").ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/{id:guid}/unread", (Guid id, EstrichOneDbContext db, ICurrentUser user, TimeProvider clock, CancellationToken ct) =>
            MarkAsync(id, false, db, user, clock, ct)).WithName("MarkNotificationUnread").ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/read-all", ReadAllAsync).WithName("MarkAllNotificationsRead");
        group.MapDelete("/{id:guid}", DeleteAsync).WithName("DeleteNotification").ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/push-config", ClientConfigAsync).WithName("GetPushClientConfig")
            .WithSummary("Öffentlicher VAPID-Schlüssel und ob APNs eingerichtet ist");
        group.MapGet("/devices", DevicesAsync).WithName("ListPushDevices").WithSummary("Eigene Geräte und Browser mit Pushnachrichten");
        group.MapPost("/devices", RegisterAsync).WithName("RegisterPushDevice")
            .WithSummary("Gerätetoken (iOS/Android) bzw. Web-Push-Abo registrieren oder auffrischen")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity).RequireRateLimiting(NotificationsModule.DeviceRateLimitPolicy);
        group.MapDelete("/devices/{id:guid}", RemoveDeviceAsync).WithName("RemovePushDevice").ProducesProblem(StatusCodes.Status404NotFound);
        group.MapDelete("/devices", RemoveDeviceByTokenAsync).WithName("RemovePushDeviceByToken")
            .WithSummary("Abmelden per Token (z. B. beim Abmelden in der App)");
        group.MapPost("/test", TestAsync).WithName("SendTestNotification").WithSummary("Testnachricht an sich selbst (Postfach und Push)")
            .RequireRateLimiting(NotificationsModule.SendRateLimitPolicy);

        group.MapPost("/send", SendAsync).WithName("SendNotification")
            .WithSummary("Nachricht an Personen, Rollen, Kolonnen oder alle senden (höchstens 20 je 10 Minuten)")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity).ProducesProblem(StatusCodes.Status429TooManyRequests)
            .RequirePermission(Permissions.NotificationsSend).RequireRateLimiting(NotificationsModule.SendRateLimitPolicy);
        group.MapGet("/audiences", AudiencesAsync).WithName("GetNotificationAudiences").WithSummary("Rollen und Kolonnen als Empfänger")
            .RequirePermission(Permissions.NotificationsSend);
        group.MapGet("/sent", SentAsync).WithName("ListSentNotifications").WithSummary("Verlauf der gesendeten Nachrichten mit Zustellstatus")
            .RequirePermission(Permissions.NotificationsSend);
        group.MapGet("/sent/{id:guid}", SentDetailAsync).WithName("GetSentNotification").ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(Permissions.NotificationsSend);

        group.MapGet("/settings", SettingsAsync).WithName("GetPushSettings").RequirePermission(Permissions.SettingsManage);
        group.MapPut("/settings", SaveSettingsAsync).WithName("SavePushSettings")
            .ProducesProblem(StatusCodes.Status412PreconditionFailed).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.SettingsManage);
    }

    private static async Task<Ok<NotificationInbox>> InboxAsync(
        EstrichOneDbContext db, ICurrentUser user, CancellationToken cancellationToken,
        [FromQuery] bool unread = false, [FromQuery] string? category = null, [FromQuery] int? limit = null, [FromQuery] DateTimeOffset? before = null)
    {
        var query = db.Set<Notification>().AsNoTracking().Where(n => n.UserId == user.UserId);
        if (unread)
        {
            query = query.Where(n => n.ReadAt == null);
        }

        if (Enum.TryParse<NotificationCategory>(category, ignoreCase: true, out var c))
        {
            query = query.Where(n => n.Category == c);
        }

        if (before is { } b)
        {
            query = query.Where(n => n.CreatedAt < b);
        }

        var rows = await query.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id).Take(Math.Clamp(limit ?? 50, 1, 200)).ToListAsync(cancellationToken);
        var senders = rows.Where(r => r.SenderUserId is not null).Select(r => r.SenderUserId!.Value).Distinct().ToList();
        var names = await db.Set<User>().AsNoTracking().Where(u => senders.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
        var count = await db.Set<Notification>().CountAsync(n => n.UserId == user.UserId && n.ReadAt == null, cancellationToken);
        return TypedResults.Ok(new NotificationInbox(rows.Select(r => ToResponse(r, r.SenderUserId is { } s ? names.GetValueOrDefault(s) : null)).ToList(), count));
    }

    internal static NotificationResponse ToResponse(Notification n, string? sender) =>
        new(n.Id, n.Category, n.Urgency, n.Title, n.Body, n.Link, n.ObjectType, n.ObjectId, sender, n.CreatedAt, n.ReadAt);

    private static async Task<Ok<UnreadCountResponse>> UnreadAsync(EstrichOneDbContext db, ICurrentUser user, CancellationToken cancellationToken) =>
        TypedResults.Ok(new UnreadCountResponse(await db.Set<Notification>().CountAsync(n => n.UserId == user.UserId && n.ReadAt == null, cancellationToken)));

    private static async Task<Results<NoContent, ProblemHttpResult>> MarkAsync(
        Guid id, bool read, EstrichOneDbContext db, ICurrentUser user, TimeProvider clock, CancellationToken cancellationToken)
    {
        var row = await db.Set<Notification>().SingleOrDefaultAsync(n => n.Id == id && n.UserId == user.UserId, cancellationToken);
        if (row is null)
        {
            return NotFound();
        }

        if (read)
        {
            row.MarkRead(clock.GetUtcNow());
        }
        else
        {
            row.MarkUnread();
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<UnreadCountResponse>> ReadAllAsync(EstrichOneDbContext db, ICurrentUser user, TimeProvider clock, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        await db.Set<Notification>().Where(n => n.UserId == user.UserId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), cancellationToken);
        return TypedResults.Ok(new UnreadCountResponse(0));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(Guid id, EstrichOneDbContext db, ICurrentUser user, CancellationToken cancellationToken)
    {
        var deleted = await db.Set<Notification>().Where(n => n.Id == id && n.UserId == user.UserId).ExecuteDeleteAsync(cancellationToken);
        return deleted == 0 ? NotFound() : TypedResults.NoContent();
    }

    private static async Task<Ok<PushClientConfig>> ClientConfigAsync(PushKeys keys, CancellationToken cancellationToken)
    {
        var config = await keys.ConfigAsync(cancellationToken);
        return TypedResults.Ok(new PushClientConfig(config.WebPushEnabled, config.WebPushEnabled ? config.VapidPublicKey : null, config.ApnsReady,
            config.ShowContent));
    }

    private static async Task<Ok<List<PushDeviceResponse>>> DevicesAsync(EstrichOneDbContext db, ICurrentUser user, CancellationToken cancellationToken)
    {
        var rows = await db.Set<PushDevice>().AsNoTracking().Where(d => d.UserId == user.UserId).OrderByDescending(d => d.LastSeenAt).ToListAsync(cancellationToken);
        return TypedResults.Ok(rows.Select(ToResponse).ToList());
    }

    private static PushDeviceResponse ToResponse(PushDevice d) =>
        new(d.Id, d.Platform, d.DeviceName, d.AppVersion, d.Sandbox, d.CreatedAt, d.LastSeenAt, d.LastSuccessAt, d.LastError);

    private static async Task<Results<Ok<PushDeviceResponse>, ProblemHttpResult>> RegisterAsync(
        RegisterPushDeviceRequest request, EstrichOneDbContext db, ICurrentUser user, PushKeys keys, TimeProvider clock, CancellationToken cancellationToken)
    {
        var token = request.Token.Trim();
        switch (request.Platform)
        {
            case PushPlatform.Web:
                if (!keys.IsAllowedWebPushEndpoint(token))
                {
                    return Validation("Der Web-Push-Endpunkt gehört zu keinem bekannten Push-Dienst (nur HTTPS).");
                }

                if (!ValidSubscription(request.P256dh, request.Auth))
                {
                    return Validation("Schlüssel des Web-Push-Abos fehlen oder sind ungültig.");
                }

                break;
            case PushPlatform.Ios:
                if (token.Length is < 32 or > 200 || !token.All(Uri.IsHexDigit))
                {
                    return Validation("Das APNs-Gerätetoken ist ungültig (hexadezimal erwartet).");
                }

                token = token.ToLowerInvariant();
                break;
            default:
                if (token.Length is < 16 or > 4096 || token.Any(char.IsWhiteSpace))
                {
                    return Validation("Das Gerätetoken ist ungültig.");
                }

                break;
        }

        var hash = PushGateway.Hash(token);
        var now = clock.GetUtcNow();
        var device = await db.Set<PushDevice>().SingleOrDefaultAsync(d => d.TokenHash == hash, cancellationToken);
        if (device is null)
        {
            // Ein Benutzer hat selten mehr als ein paar Geräte; sehr alte Einträge dürfen weichen.
            var mine = await db.Set<PushDevice>().Where(d => d.UserId == user.UserId).OrderBy(d => d.LastSeenAt).ToListAsync(cancellationToken);
            db.RemoveRange(mine.Take(Math.Max(0, mine.Count - 19)));
            device = PushDevice.Create(user.UserId, request.Platform, token, hash, now);
            db.Add(device);
        }

        device.Refresh(user.UserId, request.P256dh, request.Auth, request.Sandbox, request.BundleId, request.DeviceName, request.AppVersion, now);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ToResponse(device));
    }

    private static bool ValidSubscription(string? p256dh, string? auth)
    {
        try
        {
            return p256dh is not null && auth is not null && PushCrypto.FromBase64Url(p256dh) is { Length: 65 } key && key[0] == 0x04
                && PushCrypto.FromBase64Url(auth).Length >= 16;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> RemoveDeviceAsync(Guid id, EstrichOneDbContext db, ICurrentUser user, CancellationToken cancellationToken)
    {
        var deleted = await db.Set<PushDevice>().Where(d => d.Id == id && d.UserId == user.UserId).ExecuteDeleteAsync(cancellationToken);
        return deleted == 0 ? NotFound() : TypedResults.NoContent();
    }

    private static async Task<NoContent> RemoveDeviceByTokenAsync([FromQuery] string token, EstrichOneDbContext db, ICurrentUser user, CancellationToken cancellationToken)
    {
        var trimmed = token.Trim();
        string[] hashes = [PushGateway.Hash(trimmed), PushGateway.Hash(trimmed.ToLowerInvariant())];
        await db.Set<PushDevice>().Where(d => hashes.Contains(d.TokenHash) && d.UserId == user.UserId).ExecuteDeleteAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<NotificationResponse>> TestAsync(EstrichOneDbContext db, ICurrentUser user, TimeProvider clock, PushSignal signal,
        CancellationToken cancellationToken)
    {
        var row = Notification.Create(user.UserId, new NotificationRequest(NotificationCategory.System, "Testnachricht",
            "Pushnachrichten sind auf diesem Gerät eingerichtet.", "/start"), clock.GetUtcNow(), null);
        db.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        signal.Wake();
        return TypedResults.Ok(ToResponse(row, null));
    }

    private static async Task<Results<Ok<BroadcastResponse>, ProblemHttpResult>> SendAsync(
        SendNotificationRequest request, EstrichOneDbContext db, ICurrentUser user, IRoleAssignments roles, IEnumerable<INotificationAudience> audiences,
        IAuditLog audit, TimeProvider clock, PushSignal signal, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return Validation("Bitte einen Titel angeben.");
        }

        if (!string.IsNullOrWhiteSpace(request.Link) && NotificationLinks.Normalize(request.Link) is null)
        {
            return Validation("Der Link muss ein Pfad in EstrichONE sein (beginnt mit „/“).");
        }

        var parts = new List<string>();
        var recipients = new HashSet<Guid>();
        if (request.All)
        {
            recipients.UnionWith(await db.Set<User>().Where(u => u.IsActive).Select(u => u.Id).ToListAsync(cancellationToken));
            parts.Add("Alle Benutzer");
        }
        else
        {
            var userIds = (request.UserIds ?? []).Distinct().ToList();
            if (userIds.Count > 0)
            {
                recipients.UnionWith(userIds);
                parts.Add(userIds.Count == 1 ? "1 Person" : $"{userIds.Count} Personen");
            }

            var roleIds = (request.RoleIds ?? []).Distinct().ToList();
            if (roleIds.Count > 0)
            {
                var names = await roles.GetRoleNamesAsync(cancellationToken);
                foreach (var role in roleIds)
                {
                    recipients.UnionWith(await roles.GetUserIdsInRoleAsync(role, cancellationToken));
                    parts.Add($"Rolle {names.GetValueOrDefault(role, "?")}");
                }
            }

            var crewIds = (request.CrewIds ?? []).Distinct().ToList();
            if (crewIds.Count > 0)
            {
                foreach (var audience in audiences)
                {
                    recipients.UnionWith(await audience.CrewMembersAsync(crewIds, cancellationToken));
                }

                parts.Add(crewIds.Count == 1 ? "1 Kolonne" : $"{crewIds.Count} Kolonnen");
            }
        }

        var active = await db.Set<User>().AsNoTracking().Where(u => recipients.Contains(u.Id) && u.IsActive).Select(u => u.Id).ToListAsync(cancellationToken);
        if (active.Count == 0)
        {
            return Validation("Keine aktiven Empfänger ausgewählt.");
        }

        var senderName = await db.Set<User>().AsNoTracking().Where(u => u.Id == user.UserId).Select(u => u.DisplayName).SingleOrDefaultAsync(cancellationToken)
            ?? "Unbekannt";
        var now = clock.GetUtcNow();
        var message = new NotificationRequest(NotificationCategory.Message, request.Title, request.Body, request.Link, Urgency: request.Urgency);
        var broadcast = NotificationBroadcast.Create(user.UserId, senderName, message, string.Join(", ", parts), active.Count, now);
        db.Add(broadcast);
        db.AddRange(active.Select(id => Notification.Create(id, message, now, user.UserId, broadcast.Id)));
        audit.Record("notifications.sent", user.UserId, "notification_broadcast", broadcast.Id.ToString(),
            new Dictionary<string, string?> { ["recipients"] = active.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), ["audience"] = broadcast.Audience });
        await db.SaveChangesAsync(cancellationToken);
        signal.Wake();
        return TypedResults.Ok(new BroadcastResponse(broadcast.Id, broadcast.Title, broadcast.Body, broadcast.Link, broadcast.Urgency, broadcast.Audience,
            senderName, now, active.Count, 0, 0, 0, 0, active.Count));
    }

    private static async Task<Ok<NotificationAudiences>> AudiencesAsync(EstrichOneDbContext db, IRoleAssignments roles,
        IEnumerable<INotificationAudience> audiences, CancellationToken cancellationToken)
    {
        var roleList = new List<AudienceOption>();
        foreach (var (id, name) in (await roles.GetRoleNamesAsync(cancellationToken)).OrderBy(r => r.Value, StringComparer.CurrentCulture))
        {
            roleList.Add(new AudienceOption(id, name, (await roles.GetUserIdsInRoleAsync(id, cancellationToken)).Count));
        }

        var crews = new List<AudienceOption>();
        foreach (var audience in audiences)
        {
            crews.AddRange(await audience.CrewsAsync(cancellationToken));
        }

        return TypedResults.Ok(new NotificationAudiences(roleList, crews, await db.Set<User>().CountAsync(u => u.IsActive, cancellationToken)));
    }

    private static async Task<Ok<List<BroadcastResponse>>> SentAsync(EstrichOneDbContext db, CancellationToken cancellationToken, [FromQuery] int? limit = null)
    {
        var rows = await db.Set<NotificationBroadcast>().AsNoTracking().OrderByDescending(b => b.CreatedAt).Take(Math.Clamp(limit ?? 50, 1, 200))
            .ToListAsync(cancellationToken);
        var ids = rows.Select(r => r.Id).ToList();
        var stats = await db.Set<Notification>().AsNoTracking().Where(n => n.BroadcastId != null && ids.Contains(n.BroadcastId.Value))
            .GroupBy(n => n.BroadcastId!.Value)
            .Select(g => new
            {
                g.Key,
                Read = g.Count(n => n.ReadAt != null),
                Pushed = g.Count(n => n.PushState == PushState.Sent),
                InApp = g.Count(n => n.PushState == PushState.NoDevice || n.PushState == PushState.InAppOnly),
                Failed = g.Count(n => n.PushState == PushState.Failed || n.PushState == PushState.Expired),
                Pending = g.Count(n => n.PushState == PushState.Pending),
            })
            .ToDictionaryAsync(s => s.Key, cancellationToken);
        return TypedResults.Ok(rows.Select(b =>
        {
            var s = stats.GetValueOrDefault(b.Id);
            return new BroadcastResponse(b.Id, b.Title, b.Body, b.Link, b.Urgency, b.Audience, b.SenderName, b.CreatedAt, b.RecipientCount,
                s?.Read ?? 0, s?.Pushed ?? 0, s?.InApp ?? 0, s?.Failed ?? 0, s?.Pending ?? 0);
        }).ToList());
    }

    private static async Task<Results<Ok<BroadcastDetail>, ProblemHttpResult>> SentDetailAsync(Guid id, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var b = await db.Set<NotificationBroadcast>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (b is null)
        {
            return NotFound();
        }

        var rows = await db.Set<Notification>().AsNoTracking().Where(n => n.BroadcastId == id).ToListAsync(cancellationToken);
        var userIds = rows.Select(r => r.UserId).ToList();
        var names = await db.Set<User>().AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
        var recipients = rows.Select(r => new BroadcastRecipient(r.UserId, names.GetValueOrDefault(r.UserId, "Gelöschter Benutzer"), r.ReadAt, r.PushState,
            r.PushedAt, r.PushError)).OrderBy(r => r.Name, StringComparer.CurrentCulture).ToList();
        return TypedResults.Ok(new BroadcastDetail(new BroadcastResponse(b.Id, b.Title, b.Body, b.Link, b.Urgency, b.Audience, b.SenderName, b.CreatedAt,
            b.RecipientCount, rows.Count(r => r.ReadAt != null), rows.Count(r => r.PushState == PushState.Sent),
            rows.Count(r => r.PushState is PushState.NoDevice or PushState.InAppOnly), rows.Count(r => r.PushState is PushState.Failed or PushState.Expired),
            rows.Count(r => r.PushState == PushState.Pending)), recipients));
    }

    private static async Task<Ok<PushSettingsResponse>> SettingsAsync(PushKeys keys, EstrichOneDbContext db, CancellationToken cancellationToken) =>
        TypedResults.Ok(await SettingsResponseAsync(keys, db, cancellationToken));

    private static async Task<PushSettingsResponse> SettingsResponseAsync(PushKeys keys, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var config = await keys.ConfigAsync(cancellationToken);
        var row = await keys.RowAsync(cancellationToken);
        var counts = await db.Set<PushDevice>().AsNoTracking().GroupBy(d => d.Platform).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        return new PushSettingsResponse(row.WebPushEnabled, row.VapidPublicKey, config.VapidSubject, row.ApnsEnabled || config.ApnsFromEnvironment,
            config.ApnsKeyId, config.ApnsTeamId, config.ApnsBundleId, config.ApnsProduction, row.ProtectedApnsKey is not null || config.ApnsFromEnvironment,
            config.ApnsFromEnvironment, config.ApnsReady, row.ShowContent, counts.GetValueOrDefault(PushPlatform.Web), counts.GetValueOrDefault(PushPlatform.Ios),
            counts.GetValueOrDefault(PushPlatform.Android), row.Version);
    }

    private static async Task<Results<Ok<PushSettingsResponse>, ProblemHttpResult>> SaveSettingsAsync(
        SavePushSettingsRequest request, PushKeys keys, EstrichOneDbContext db, ICurrentUser user, IAuditLog audit, CancellationToken cancellationToken)
    {
        var row = await keys.RowAsync(cancellationToken);
        if (request.Version is { } version && version != row.Version)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "Geändert",
                detail: "Die Einstellungen wurden inzwischen geändert. Bitte neu laden.", extensions: new Dictionary<string, object?> { ["code"] = "VERSION_CONFLICT" });
        }

        var subject = Notification.Clip(request.VapidSubject, 200);
        if (subject is not null && !subject.StartsWith("mailto:", StringComparison.Ordinal) && !subject.StartsWith("https://", StringComparison.Ordinal))
        {
            return Validation("Der Kontakt für Push-Dienste beginnt mit „mailto:“ oder „https://“.");
        }

        var pem = request.ApnsKey?.Trim();
        if (!string.IsNullOrEmpty(pem) && !PushCrypto.IsValidP8(pem))
        {
            return Validation("Der APNs-Schlüssel ist keine gültige .p8-Datei (PKCS#8, P-256).");
        }

        var keyId = Notification.Clip(request.ApnsKeyId, 20);
        var teamId = Notification.Clip(request.ApnsTeamId, 20);
        if ((keyId is not null && !keyId.All(char.IsLetterOrDigit)) || (teamId is not null && !teamId.All(char.IsLetterOrDigit)))
        {
            return Validation("Key-ID und Team-ID bestehen nur aus Buchstaben und Ziffern (10 Zeichen).");
        }

        row.WebPushEnabled = request.WebPushEnabled;
        row.VapidSubject = subject;
        row.ApnsEnabled = request.ApnsEnabled;
        row.ApnsKeyId = keyId?.ToUpperInvariant();
        row.ApnsTeamId = teamId?.ToUpperInvariant();
        row.ApnsBundleId = Notification.Clip(request.ApnsBundleId, 200);
        row.ApnsProduction = request.ApnsProduction;
        row.ShowContent = request.ShowContent;
        if (request.RemoveApnsKey)
        {
            row.ProtectedApnsKey = null;
        }
        else if (!string.IsNullOrEmpty(pem))
        {
            row.ProtectedApnsKey = keys.Protect(pem);
        }

        audit.Record("notifications.settings_saved", user.UserId, "settings", "push",
            new Dictionary<string, string?> { ["apns"] = request.ApnsEnabled ? "an" : "aus", ["webPush"] = request.WebPushEnabled ? "an" : "aus" });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await SettingsResponseAsync(keys, db, cancellationToken));
    }

    private static ProblemHttpResult NotFound() => TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Nicht gefunden",
        extensions: new Dictionary<string, object?> { ["code"] = "NOTIFICATION_NOT_FOUND" });

    private static ProblemHttpResult Validation(string detail) => TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity,
        title: "Ungültige Angaben", detail: detail, extensions: new Dictionary<string, object?> { ["code"] = "VALIDATION_FAILED" });
}
