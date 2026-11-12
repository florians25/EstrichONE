using EstrichOne.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Contacts.Exports;

/// <summary>
/// Prüft, ob ein Kunde neu zugeordnet werden darf (Bauakte, Anfrage/Anruf, Projektbeteiligter). Archivierte und gelöschte
/// Kunden (Tombstone) werden abgelehnt; bleibt die bisherige Zuordnung unverändert, ist sie erlaubt – sonst ließen sich
/// Altakten eines archivierten Kunden nicht mehr bearbeiten.
/// </summary>
public static class CustomerAssignment
{
    public const string ArchivedCode = "CUSTOMER_ARCHIVED";
    public const string DeletedCode = "CUSTOMER_DELETED";
    public const string UnknownCode = "UNKNOWN_CUSTOMER";
    public const string ArchivedMessage = "Kunde ist archiviert – zuerst reaktivieren.";
    public const string DeletedMessage = "Der Kunde wurde gelöscht und kann nicht mehr zugeordnet werden.";
    public const string UnknownMessage = "Der angegebene Kunde existiert nicht.";

    /// <summary>Fehler (Code und Meldung) oder <c>null</c>, wenn <paramref name="customerId"/> zugeordnet werden darf.</summary>
    public static async Task<(string Code, string Message)?> CheckAsync(
        DbContext db, Guid? customerId, Guid? currentId, CancellationToken cancellationToken)
    {
        if (customerId is not { } id || id == currentId)
        {
            return null;
        }

        var state = await db.Set<Contact>().IgnoreQueryFilters([QueryFilters.NotDeleted]).AsNoTracking().Where(c => c.Id == id)
            .Select(c => new { Deleted = c.DeletedAt != null, Archived = c.ArchivedAt != null }).SingleOrDefaultAsync(cancellationToken);
        return state is null ? (UnknownCode, UnknownMessage)
            : state.Deleted ? (DeletedCode, DeletedMessage)
            : state.Archived ? (ArchivedCode, ArchivedMessage)
            : null;
    }
}
