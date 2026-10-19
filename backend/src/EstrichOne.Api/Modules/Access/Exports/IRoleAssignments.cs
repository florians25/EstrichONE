namespace EstrichOne.Api.Modules.Access.Exports;

public interface IRoleAssignments
{
    /// <summary>Setzt die Rollen eines Benutzers. Speichert noch nicht (gleiche Transaktion wie der Aufrufer).</summary>
    /// <exception cref="LastAdministratorException">Der letzte aktive Administrator würde seine Rolle verlieren.</exception>
    Task SetRolesAsync(Guid userId, IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken);

    /// <summary>ID der Standardrolle „Administrator“ (für die Ersteinrichtung).</summary>
    Guid AdministratorRoleId { get; }
}

public sealed class LastAdministratorException()
    : Exception("Mindestens ein aktiver Benutzer muss die Rolle Administrator behalten.");

public sealed class UnknownRoleException(Guid roleId) : Exception($"Die Rolle {roleId} existiert nicht.");
