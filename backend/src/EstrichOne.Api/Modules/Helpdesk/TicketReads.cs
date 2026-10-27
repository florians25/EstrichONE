using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Helpdesk;

/// <summary>Wann ein Benutzer ein Ticket zuletzt angesehen hat (für „neue Antworten“ in Web und App).</summary>
public sealed class TicketRead
{
    public Guid TicketId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTimeOffset SeenAt { get; private set; }

    private TicketRead()
    {
    }
}

internal sealed class TicketReadConfiguration : IEntityTypeConfiguration<TicketRead>
{
    public void Configure(EntityTypeBuilder<TicketRead> builder)
    {
        builder.ToTable("ticket_reads");
        builder.HasKey(r => new { r.TicketId, r.UserId });
        builder.HasOne<Ticket>().WithMany().HasForeignKey(r => r.TicketId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(r => r.UserId);
    }
}

/// <param name="Count">Anzahl Tickets mit Neuigkeiten (Zähler in der Kopfleiste bzw. am App-Symbol).</param>
public sealed record TicketUnreadResponse(int Count, IReadOnlyList<TicketUnreadItem> Items);

/// <param name="LastActivityAt">Zeitpunkt der jüngsten Neuigkeit eines anderen (Antwort, Statuswechsel, Meldung).</param>
public sealed record TicketUnreadItem(Guid Id, string Number, string Title, TicketStatus Status, DateTimeOffset LastActivityAt);

/// <summary>
/// Gelesen-Status je Benutzer. Neu ist, was ein anderer nach dem letzten Ansehen getan hat: Antwort (interne Notizen nur
/// für die Bearbeitung), Status-/Zuweisungs-/Prioritätswechsel und – für die Bearbeitung – die Meldung selbst.
/// Der Melder sieht seine Tickets, die Bearbeitung ihr zugewiesene und noch nicht zugewiesene offene Tickets.
/// </summary>
internal sealed class TicketReadState(EstrichOneDbContext db, TimeProvider clock)
{
    public async Task MarkSeenAsync(Guid ticketId, Guid userId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO ticket_reads (ticket_id, user_id, seen_at) VALUES ({ticketId}, {userId}, {now})
            ON CONFLICT (ticket_id, user_id) DO UPDATE SET seen_at = GREATEST(ticket_reads.seen_at, EXCLUDED.seen_at)
            """, cancellationToken);
    }

    /// <summary>Tickets mit Neuigkeiten und Zeitpunkt der jüngsten; optional nur unter <paramref name="among"/>.</summary>
    public async Task<Dictionary<Guid, DateTimeOffset>> UnreadAsync(
        Guid userId, bool isAgent, IReadOnlyCollection<Guid>? among, CancellationToken cancellationToken)
    {
        var since = clock.GetUtcNow().AddDays(-90);
        var tickets = db.Set<Ticket>().AsNoTracking().Where(t => t.LastActivityAt >= since);
        tickets = isAgent
            ? tickets.Where(t => t.ReporterUserId == userId
                || (t.Status != TicketStatus.Closed && (t.AssigneeUserId == userId || t.AssigneeUserId == null)))
            : tickets.Where(t => t.ReporterUserId == userId);
        if (among is not null)
        {
            tickets = tickets.Where(t => among.Contains(t.Id));
        }

        var rows = await tickets.Select(t => new
        {
            t.Id,
            Seen = db.Set<TicketRead>().Where(r => r.TicketId == t.Id && r.UserId == userId).Select(r => (DateTimeOffset?)r.SeenAt).FirstOrDefault(),
            Created = t.ReporterUserId != userId ? (DateTimeOffset?)t.CreatedAt : null,
            Comment = db.Set<TicketComment>().Where(c => c.TicketId == t.Id && c.AuthorUserId != userId && (isAgent || !c.IsInternal))
                .Max(c => (DateTimeOffset?)c.CreatedAt),
            Event = db.Set<TicketEvent>().Where(e => e.TicketId == t.Id && e.ActorUserId != userId).Max(e => (DateTimeOffset?)e.At),
        }).ToListAsync(cancellationToken);

        var result = new Dictionary<Guid, DateTimeOffset>();
        foreach (var row in rows)
        {
            var latest = new[] { isAgent ? row.Created : null, row.Comment, row.Event }.Max();
            if (latest is { } at && (row.Seen is null || at > row.Seen))
            {
                result[row.Id] = at;
            }
        }

        return result;
    }
}
