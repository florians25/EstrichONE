using System.Security.Claims;
using EstrichOne.Hosting.Data;

namespace EstrichOne.Hosting.Infrastructure;

/// <summary>Ereignis-/Audit-Protokoll aller Konsolenaktionen (wer, was, wann, Ergebnis, Adresse).</summary>
public sealed class AuditLog(HostingDbContext db, IHttpContextAccessor http, TimeProvider clock)
{
    public const string System = "System";

    public string CurrentActor =>
        http.HttpContext?.User.FindFirstValue(ClaimTypes.Name) ?? System;

    public async Task WriteAsync(string action, Tenant? tenant = null, string? details = null, bool success = true,
        string? actor = null, CancellationToken cancellationToken = default)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            At = clock.GetUtcNow().UtcDateTime,
            Actor = actor ?? CurrentActor,
            Action = action,
            TenantId = tenant?.Id,
            TenantSlug = tenant?.Slug,
            Details = details is { Length: > 2000 } ? details[..2000] : details,
            Success = success,
            IpAddress = http.HttpContext?.Connection.RemoteIpAddress?.ToString(),
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
