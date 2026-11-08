using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Documentation.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Feedback;

/// <summary>
/// Anhänge von Meldungen: Die IT (feedback.manage) sieht alle Anhänge der Meldungen und Antworten, der Einreicher
/// zusätzlich die Anhänge der (nicht internen) Antworten auf seine Meldungen.
/// </summary>
internal sealed class FeedbackAttachmentGrant(EstrichOneDbContext db, ICurrentUser currentUser, IUserPermissions permissions) : IFileReadGrant
{
    public async Task<bool> CanReadAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var items = db.Set<FeedbackItem>();
        var comments = db.Set<FeedbackComment>();
        if (await permissions.HasAsync(Permissions.FeedbackManage, cancellationToken))
        {
            return await items.AnyAsync(f => f.AttachmentFileIds.Contains(fileId), cancellationToken)
                || await comments.AnyAsync(c => c.AttachmentFileIds.Contains(fileId), cancellationToken);
        }

        var userId = currentUser.UserId;
        return await comments.AnyAsync(c => !c.IsInternal && c.AttachmentFileIds.Contains(fileId)
            && items.Any(f => f.Id == c.FeedbackId && f.ReporterUserId == userId), cancellationToken);
    }
}
