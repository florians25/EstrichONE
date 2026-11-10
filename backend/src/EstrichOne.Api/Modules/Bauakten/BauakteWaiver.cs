using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Bauakten;

/// <summary>„Erstbegehung nicht erforderlich“ setzen oder aufheben – gemeinsam für REST-API und Synchronisation.</summary>
internal static class BauakteWaiver
{
    /// <summary>Ändert die Bauakte und schreibt den Verlaufseintrag; gespeichert wird mit dem nächsten <c>SaveChanges</c>.</summary>
    public static async Task ApplyAsync(EstrichOneDbContext db, Bauakte bauakte, bool waived, string? reason, ICurrentUser currentUser,
        TimeProvider clock, IBauakteHistory history, CancellationToken cancellationToken)
    {
        var wasWaived = bauakte.InitialInspectionWaived;
        var oldReason = bauakte.InitialInspectionWaivedReason;
        if (waived)
        {
            var name = await db.Set<User>().AsNoTracking().Where(u => u.Id == currentUser.UserId).Select(u => u.DisplayName)
                .SingleOrDefaultAsync(cancellationToken);
            bauakte.WaiveInitialInspection(currentUser.UserId, name, clock.GetUtcNow(), reason);
        }
        else
        {
            bauakte.ResumeInitialInspection();
        }

        if (wasWaived != bauakte.InitialInspectionWaived || oldReason != bauakte.InitialInspectionWaivedReason)
        {
            history.Record(bauakte.Id, BauakteHistoryActions.InitialInspectionWaiver, new Dictionary<string, string?>
            {
                ["waived"] = bauakte.InitialInspectionWaived ? "true" : "false",
                ["reason"] = bauakte.InitialInspectionWaivedReason,
            });
        }
    }
}
