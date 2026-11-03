using EstrichOne.Hosting.Api;
using EstrichOne.Hosting.Data;
using EstrichOne.Hosting.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Hosting.Notifications;

public static class NotificationEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var notifications = app.MapGroup("/api/notifications").WithTags("Benachrichtigungen").RequireAuthorization();
        notifications.MapGet("/", async (bool? all, HostingDbContext db, CancellationToken ct) =>
        {
            var query = db.Notifications.AsNoTracking();
            if (all != true)
            {
                query = query.Where(n => n.ResolvedAt == null);
            }

            var items = await query.OrderByDescending(n => n.RaisedAt).Take(200).ToListAsync(ct);
            var open = await db.Notifications.CountAsync(n => n.ResolvedAt == null, ct);
            var unread = await db.Notifications.CountAsync(n => n.ResolvedAt == null && n.ReadAt == null, ct);
            return Results.Ok(new { items, open, unread });
        });
        notifications.MapPost("/{id:long}/read", async (long id, HostingDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var notification = await db.Notifications.FindAsync([id], ct);
            if (notification is null)
            {
                return ApiErrors.NotFound();
            }

            notification.ReadAt ??= clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
        notifications.MapPost("/read-all", async (HostingDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            await db.Notifications.Where(n => n.ReadAt == null).ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
            return Results.NoContent();
        });

        var settings = app.MapGroup("/api/settings/notifications").WithTags("Benachrichtigungen").RequireAuthorization();
        settings.MapGet("/", async (ConsoleSettings console, CancellationToken ct) => Results.Ok((await NotificationSettings.LoadAsync(console, ct)).ToView()));
        settings.MapPut("/", async (NotificationSettings.Input input, ConsoleSettings console, AuditLog audit, CancellationToken ct) =>
        {
            var current = await NotificationSettings.LoadAsync(console, ct);
            var (value, errors) = NotificationSettings.Validate(input, current);
            if (value is null)
            {
                return ApiErrors.Validation(errors);
            }

            await value.SaveAsync(console, ct);
            await audit.WriteAsync("Benachrichtigungen eingestellt",
                details: $"E-Mail: {(value.EmailConfigured ? value.EmailTo : "aus")}, Webhook: {(value.WebhookConfigured ? "an" : "aus")}", cancellationToken: ct);
            return Results.Ok(value.ToView());
        });
        settings.MapPost("/test", async (ConsoleSettings console, NotificationSender sender, AuditLog audit, CancellationToken ct) =>
        {
            var current = await NotificationSettings.LoadAsync(console, ct);
            if (!current.EmailConfigured && !current.WebhookConfigured)
            {
                return ApiErrors.BadRequest("Bitte zuerst E-Mail oder Webhook einrichten und speichern.");
            }

            var result = await sender.SendAsync(current, "[EstrichONE Hosting] Testnachricht",
                "Diese Testnachricht bestätigt, dass Benachrichtigungen der Hosting-Konsole ankommen.", "info", null, resolved: false, ct);
            await audit.WriteAsync("Testbenachrichtigung", details: result.Summary, success: result.Success, cancellationToken: ct);
            return result.Success ? Results.Ok(new { message = result.Summary }) : ApiErrors.BadRequest(result.Summary);
        });
    }
}
