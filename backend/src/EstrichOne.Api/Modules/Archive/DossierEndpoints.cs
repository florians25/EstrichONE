using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.AspNetCore.Http.HttpResults;

namespace EstrichOne.Api.Modules.Archive;

public sealed record UnlockDossierRequest(string Password, int? Hours);

internal static class DossierEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var dossiers = api.MapGroup("/archive/dossiers").WithTags("Aktenarchiv");

        dossiers.MapGet("/", async (DossierService service, string? ids, CancellationToken ct) =>
                TypedResults.Ok(await service.ListAsync(
                    (ids ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(v => Guid.TryParse(v, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).ToList(), ct)))
            .WithName("GetDossiers").WithSummary("Archivierte Akten (Platzhalter für Listen; optional ids=a,b)")
            .RequirePermission(Permissions.BauaktenRead);

        dossiers.MapGet("/{bauakteId:guid}", async Task<Results<Ok<DossierDto>, NoContent, ProblemHttpResult>> (Guid bauakteId,
                DossierService service, IBauakteAccess access, CancellationToken ct) =>
            {
                if (!await access.CanReadAsync(bauakteId, ct))
                {
                    return NotFound();
                }

                var list = await service.ListAsync([bauakteId], ct);
                return list.Count == 0 ? TypedResults.NoContent() : TypedResults.Ok(list[0]);
            })
            .WithName("GetDossier").RequirePermission(Permissions.BauaktenRead);

        dossiers.MapPost("/{bauakteId:guid}", async Task<Results<Ok<DossierDto>, ProblemHttpResult>> (Guid bauakteId, DossierService service,
                ArchiveService archive, EstrichOneDbContext db, IAuditLog audit, ICurrentUser user, ArchiveSignal signal, CancellationToken ct) =>
            {
                if (await archive.ConnectionAsync(ct) is null)
                {
                    return TypedResults.Problem(statusCode: 409, title: "Kein Archivserver gekoppelt",
                        extensions: new Dictionary<string, object?> { ["code"] = "ARCHIVE_NOT_CONNECTED" });
                }

                await service.StartAsync(bauakteId, user.UserId, ct);
                audit.Record("archive.dossier.archive", user.UserId, "bauakte", bauakteId.ToString());
                await db.SaveChangesAsync(ct);
                signal.Wake();
                return TypedResults.Ok((await service.ListAsync([bauakteId], ct))[0]);
            })
            .WithName("ArchiveDossier").WithSummary("Akte komplett archivieren und aus dem Produktivsystem auslagern (bzw. neue Fassung)")
            .RequirePermission(Permissions.SettingsManage);

        dossiers.MapPost("/{bauakteId:guid}/restore", async Task<Results<Ok<DossierDto>, ProblemHttpResult>> (Guid bauakteId,
                DossierService service, IBauakteAccess access, EstrichOneDbContext db, IAuditLog audit, ICurrentUser user, ArchiveSignal signal,
                CancellationToken ct) =>
            {
                var dossier = await access.CanReadAsync(bauakteId, ct) ? await service.FindAsync(bauakteId, ct) : null;
                if (dossier is null)
                {
                    return NotFound();
                }

                dossier.Restore();
                audit.Record("archive.dossier.restore", user.UserId, "bauakte", bauakteId.ToString());
                await db.SaveChangesAsync(ct);
                signal.Wake();
                return TypedResults.Ok((await service.ListAsync([bauakteId], ct))[0]);
            })
            .WithName("RestoreDossier").WithSummary("Akte vom Archivserver holen; danach 5 Tage im Produktivsystem (nur Ansicht)")
            .RequirePermission(Permissions.BauaktenRead);

        dossiers.MapPost("/{bauakteId:guid}/unlock", async Task<Results<Ok<DossierDto>, ProblemHttpResult>> (Guid bauakteId,
                UnlockDossierRequest request, DossierService service, IUserAccounts accounts, EstrichOneDbContext db, IAuditLog audit,
                ICurrentUser user, TimeProvider clock, CancellationToken ct) =>
            {
                var dossier = await service.FindAsync(bauakteId, ct);
                if (dossier is null)
                {
                    return NotFound();
                }

                if (dossier.Status is not (DossierStatus.Available or DossierStatus.Unlocked))
                {
                    return TypedResults.Problem(statusCode: 409, title: "Erst abrufen",
                        detail: "Die Akte muss zuerst vom Archivserver abgerufen werden.",
                        extensions: new Dictionary<string, object?> { ["code"] = "ARCHIVE_NOT_RESTORED" });
                }

                if (!await accounts.VerifyPasswordAsync(user.UserId, request.Password ?? string.Empty, ct))
                {
                    audit.Record("archive.dossier.unlock_failed", user.UserId, "bauakte", bauakteId.ToString());
                    await db.SaveChangesAsync(ct);
                    return TypedResults.Problem(statusCode: 403, title: "Passwort falsch",
                        extensions: new Dictionary<string, object?> { ["code"] = "PASSWORD_WRONG" });
                }

                var hours = Math.Clamp(request.Hours ?? 8, 1, 72);
                dossier.Unlock(user.UserId, clock.GetUtcNow().AddHours(hours));
                audit.Record("archive.dossier.unlocked", user.UserId, "bauakte", bauakteId.ToString(),
                    new Dictionary<string, string?> { ["hours"] = hours.ToString(System.Globalization.CultureInfo.InvariantCulture) });
                await db.SaveChangesAsync(ct);
                return TypedResults.Ok((await service.ListAsync([bauakteId], ct))[0]);
            })
            .WithName("UnlockDossier").WithSummary("Archivierte Akte mit Passwort zum Bearbeiten entsperren; danach neue Fassung im Archiv")
            .RequirePermission(Permissions.ArchiveUnlock);
    }

    private static ProblemHttpResult NotFound() =>
        TypedResults.Problem(statusCode: 404, title: "Nicht gefunden", extensions: new Dictionary<string, object?> { ["code"] = "NOT_FOUND" });
}
