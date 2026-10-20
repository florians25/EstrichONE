using EstrichOne.Api.Infrastructure.Hosting;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using Microsoft.EntityFrameworkCore;
using P = EstrichOne.Api.Modules.Access.Exports.Permissions;

namespace EstrichOne.Api.Modules.Access;

/// <summary>
/// Standardrollen (docs/reengineering/03, Abschnitt 6). Sie werden angelegt, wenn sie fehlen, und danach nicht
/// mehr verändert – mit einer Ausnahme: Die Administratorrolle erhält immer alle Rechte des Katalogs.
/// </summary>
internal sealed class StandardRoles(EstrichOneDbContext db) : IDataInitializer
{
    public static readonly Guid AdministratorId = Guid.Parse("01a00000-0000-7000-8000-000000000001");
    public static readonly Guid OfficeId = Guid.Parse("01a00000-0000-7000-8000-000000000002");
    public static readonly Guid SiteWorkerId = Guid.Parse("01a00000-0000-7000-8000-000000000003");
    public static readonly Guid ItAdministratorId = Guid.Parse("01a00000-0000-7000-8000-000000000004");

    public int Order => 10;

    public static IReadOnlyList<(Guid Id, string Name, string Description, string[] Permissions)> Definitions { get; } =
    [
        (AdministratorId, "Administrator", "Alle Rechte. Mindestens ein aktiver Benutzer muss diese Rolle haben.",
            P.All.Select(p => p.Code).ToArray()),
        (OfficeId, "Büro und Bauleitung", "Bauakten, Kontakte, Planung und Aufgaben vollständig bearbeiten.",
        [
            P.BauaktenRead, P.BauaktenReadAll, P.BauaktenCreate, P.BauaktenEdit, P.BauaktenDelete, P.BauaktenAssign,
            P.ContactsRead, P.ContactsEdit, P.FilesUpload, P.FilesDelete, P.NotesWrite, P.DiaryWrite, P.InspectionsWrite, P.WorkRead, P.WorkEdit,
            P.PlanningRead, P.PlanningManage, P.DevicesRead, P.DevicesSelfEnroll,
        ]),
        (SiteWorkerId, "Baustelle", "Eigene Bauakten sehen, anlegen und dokumentieren.",
        [
            P.BauaktenRead, P.BauaktenCreate, P.BauaktenEdit, P.ContactsRead, P.FilesUpload, P.NotesWrite, P.DiaryWrite, P.InspectionsWrite,
            P.WorkRead, P.WorkEdit, P.PlanningRead, P.DevicesSelfEnroll,
        ]),
        (ItAdministratorId, "IT-Administration", "Geräte, Support und Backups verwalten.",
        [
            P.DevicesRead, P.DevicesManage, P.DevicesWipe, P.DevicesSelfEnroll, P.HelpdeskAgent, P.BackupManage,
            P.UsersManage, P.AuditRead,
        ]),
    ];

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var existing = await db.Set<Role>().Include(role => role.Permissions)
            .Where(role => Definitions.Select(d => d.Id).Contains(role.Id))
            .ToDictionaryAsync(role => role.Id, cancellationToken);

        foreach (var (id, name, description, permissions) in Definitions)
        {
            if (!existing.TryGetValue(id, out var role))
            {
                db.Add(Role.Create(id, name, description, isSystem: true, permissions));
            }
            else if (id == AdministratorId)
            {
                role.SetPermissions(permissions);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
