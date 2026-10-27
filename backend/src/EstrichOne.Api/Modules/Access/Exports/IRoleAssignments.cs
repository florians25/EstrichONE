namespace EstrichOne.Api.Modules.Access.Exports;

public interface IRoleAssignments
{
    /// <summary>Setzt die Rollen eines Benutzers. Speichert noch nicht (gleiche Transaktion wie der Aufrufer).</summary>
    /// <exception cref="LastAdministratorException">Der letzte aktive Administrator würde seine Rolle verlieren.</exception>
    Task SetRolesAsync(Guid userId, IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken);

    /// <summary>Rollen eines Benutzers (z. B. für Freigaben, die an eine Rolle gehen).</summary>
    Task<IReadOnlyList<Guid>> GetRoleIdsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Aktive Benutzer einer Rolle (z. B. Benachrichtigung bei Freigaben an eine Rolle).</summary>
    Task<IReadOnlyList<Guid>> GetUserIdsInRoleAsync(Guid roleId, CancellationToken cancellationToken);

    /// <summary>Aktive Benutzer mit einem Recht über irgendeine ihrer Rollen (z. B. Backup-Meldungen an alle mit backup.manage).</summary>
    Task<IReadOnlyList<Guid>> GetUserIdsWithPermissionAsync(string permission, CancellationToken cancellationToken);

    /// <summary>Recht einer Rolle hinzufügen, falls es fehlt (z. B. Ersteinrichtung eines Moduls). Speichert sofort.</summary>
    Task GrantAsync(Guid roleId, string permission, CancellationToken cancellationToken);

    /// <summary>Namen aller Rollen (Anzeige).</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetRoleNamesAsync(CancellationToken cancellationToken);

    /// <summary>ID der Standardrolle „Administrator“ (für die Ersteinrichtung).</summary>
    Guid AdministratorRoleId { get; }
}

public sealed class LastAdministratorException()
    : Exception("Mindestens ein aktiver Benutzer muss die Rolle Administrator behalten.");

public sealed class UnknownRoleException(Guid roleId) : Exception($"Die Rolle {roleId} existiert nicht.");
