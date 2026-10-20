namespace EstrichOne.Api.Modules.Access.Exports;

/// <summary>
/// Rechtekatalog. Rechte sind feste Codes im Programm; Rollen bündeln sie frei konfigurierbar.
/// Neue Rechte hier eintragen – die Administratorrolle erhält sie beim nächsten <c>migrate</c> automatisch.
/// </summary>
public static class Permissions
{
    public const string BauaktenRead = "bauakten.read";
    public const string BauaktenReadAll = "bauakten.read_all";
    public const string BauaktenCreate = "bauakten.create";
    public const string BauaktenEdit = "bauakten.edit";
    public const string BauaktenDelete = "bauakten.delete";
    public const string BauaktenAssign = "bauakten.assign";
    public const string ContactsRead = "contacts.read";
    public const string ContactsEdit = "contacts.edit";
    public const string FilesUpload = "files.upload";
    public const string FilesDelete = "files.delete";
    public const string NotesWrite = "notes.write";
    public const string DiaryWrite = "diary.write";
    public const string InspectionsWrite = "inspections.write";
    public const string WorkRead = "work.read";
    public const string WorkEdit = "work.edit";
    public const string PlanningRead = "planning.read";
    public const string PlanningManage = "planning.manage";
    public const string MaterialsRead = "materials.read";
    public const string MaterialsManage = "materials.manage";
    public const string UsersManage = "users.manage";
    public const string RolesManage = "roles.manage";
    public const string DevicesRead = "devices.read";
    public const string DevicesManage = "devices.manage";
    public const string DevicesWipe = "devices.wipe";
    public const string DevicesSelfEnroll = "devices.self_enroll";
    public const string HelpdeskAgent = "helpdesk.agent";
    public const string BackupManage = "backup.manage";
    public const string SettingsManage = "settings.manage";
    public const string AuditRead = "audit.read";

    public static IReadOnlyList<PermissionInfo> All { get; } =
    [
        new(BauaktenRead, "Bauakten", "Eigene Bauakten (Mitglied, zuständig) sehen"),
        new(BauaktenReadAll, "Bauakten", "Alle Bauakten sehen"),
        new(BauaktenCreate, "Bauakten", "Bauakten anlegen"),
        new(BauaktenEdit, "Bauakten", "Bauakten bearbeiten"),
        new(BauaktenDelete, "Bauakten", "Bauakten löschen und wiederherstellen"),
        new(BauaktenAssign, "Bauakten", "Kollegen einer Bauakte zuordnen"),
        new(ContactsRead, "Kontakte", "Kunden und Projektbeteiligte sehen"),
        new(ContactsEdit, "Kontakte", "Kunden und Projektbeteiligte bearbeiten"),
        new(FilesUpload, "Dokumentation", "Fotos, Videos und Dokumente hochladen"),
        new(FilesDelete, "Dokumentation", "Fotos, Videos und Dokumente löschen"),
        new(NotesWrite, "Dokumentation", "Notizen schreiben"),
        new(DiaryWrite, "Dokumentation", "Bautagebuch führen"),
        new(InspectionsWrite, "Dokumentation", "Prüfungen erfassen (Erstbegehung, CM-Messung)"),
        new(WorkRead, "Aufgaben", "Aufgaben und Mängel sehen"),
        new(WorkEdit, "Aufgaben", "Aufgaben und Mängel anlegen und bearbeiten"),
        new(PlanningRead, "Planung", "Einsatzplanung sehen"),
        new(PlanningManage, "Planung", "Einsätze planen"),
        new(MaterialsRead, "Material", "Material, Lieferanten und Bestellungen sehen"),
        new(MaterialsManage, "Material", "Materialstamm pflegen und Material bestellen"),
        new(UsersManage, "Verwaltung", "Benutzer verwalten"),
        new(RolesManage, "Verwaltung", "Rollen und Rechte verwalten"),
        new(DevicesRead, "IT Center", "Geräte sehen"),
        new(DevicesManage, "IT Center", "Geräte freigeben, sperren, entfernen"),
        new(DevicesWipe, "IT Center", "EstrichONE-Daten auf Geräten löschen"),
        new(DevicesSelfEnroll, "IT Center", "Eigenes persönliches Gerät ohne Freigabe einrichten"),
        new(HelpdeskAgent, "IT Center", "Supporttickets bearbeiten"),
        new(BackupManage, "System", "Backups verwalten"),
        new(SettingsManage, "System", "Firmendaten und Einstellungen ändern"),
        new(AuditRead, "System", "Audit-Log einsehen"),
    ];

    public static bool Exists(string code) => All.Any(permission => permission.Code == code);
}

public sealed record PermissionInfo(string Code, string Group, string Description);

/// <summary>Rechte des angemeldeten Benutzers (je Anfrage einmal geladen).</summary>
public interface IUserPermissions
{
    Task<IReadOnlySet<string>> GetAsync(CancellationToken cancellationToken);

    async Task<bool> HasAsync(string permission, CancellationToken cancellationToken) =>
        (await GetAsync(cancellationToken)).Contains(permission);
}
