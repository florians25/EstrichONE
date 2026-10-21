using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Documentation.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Helpdesk;

/// <summary>Der Melder sieht Anhänge der (nicht internen) Antworten auf seine Tickets, z. B. eine Anleitung als Bildschirmfoto.</summary>
internal sealed class TicketAttachmentGrant(EstrichOneDbContext db, ICurrentUser currentUser) : IFileReadGrant
{
    public Task<bool> CanReadAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        return db.Set<TicketComment>().AnyAsync(c => !c.IsInternal && c.AttachmentFileIds.Contains(fileId)
            && db.Set<Ticket>().Any(t => t.Id == c.TicketId && t.ReporterUserId == userId), cancellationToken);
    }
}
