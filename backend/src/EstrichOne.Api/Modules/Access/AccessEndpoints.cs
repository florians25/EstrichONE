using System.ComponentModel.DataAnnotations;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Access;

public sealed record RoleResponse(Guid Id, string Name, string? Description, bool IsSystem, IReadOnlyList<string> Permissions, long Version);

public sealed record SaveRoleRequest(
    [property: Required, MaxLength(100)] string Name,
    [property: MaxLength(500)] string? Description,
    [property: Required] IReadOnlyList<string> Permissions,
    long? Version = null);

public sealed record SetUserRolesRequest([property: Required] IReadOnlyList<Guid> RoleIds);

internal static class AccessEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var access = api.MapGroup("/access").WithTags("Rechte und Rollen");

        access.MapGet("/me/permissions", async (IUserPermissions permissions, CancellationToken cancellationToken) =>
                TypedResults.Ok((await permissions.GetAsync(cancellationToken)).Order(StringComparer.Ordinal).ToList()))
            .WithName("GetMyPermissions").WithSummary("Rechte des angemeldeten Benutzers (steuert die Oberfläche; geprüft wird immer serverseitig)")
            .AllowPendingDevice();

        access.MapGet("/permissions", () => TypedResults.Ok(Permissions.All))
            .WithName("GetPermissionCatalog").WithSummary("Alle verfügbaren Rechte")
            .RequirePermission(Permissions.RolesManage);

        access.MapGet("/roles", GetRolesAsync)
            .WithName("GetRoles").WithSummary("Alle Rollen mit Rechten")
            .RequirePermission(Permissions.UsersManage);

        access.MapPut("/roles/{id:guid}", SaveRoleAsync)
            .WithName("SaveRole").WithSummary("Rolle anlegen oder ändern (Name, Beschreibung, Rechte). Die Administratorrolle behält immer alle Rechte.")
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.RolesManage);

        access.MapDelete("/roles/{id:guid}", DeleteRoleAsync)
            .WithName("DeleteRole").WithSummary("Eigene Rolle löschen (Standardrollen nicht; zugeordnete Benutzer verlieren die Rolle)")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(Permissions.RolesManage);

        access.MapGet("/roles/{id:guid}/users", async (Guid id, EstrichOneDbContext db, CancellationToken ct) =>
                TypedResults.Ok(await db.Set<UserRole>().Where(ur => ur.RoleId == id)
                    .Join(db.Set<User>(), ur => ur.UserId, u => u.Id, (_, u) => new { u.Id, u.DisplayName, u.IsActive })
                    .OrderBy(u => u.DisplayName).ToListAsync(ct)))
            .WithName("GetRoleUsers").WithSummary("Benutzer mit dieser Rolle")
            .RequirePermission(Permissions.UsersManage);

        access.MapPut("/users/{userId:guid}/roles", SetUserRolesAsync)
            .WithName("SetUserRoles").WithSummary("Rollen eines Benutzers festlegen")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.UsersManage);
    }

    private static async Task<Ok<List<RoleResponse>>> GetRolesAsync(EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var roles = await db.Set<Role>().AsNoTracking().Include(role => role.Permissions).OrderBy(role => role.Name).ToListAsync(cancellationToken);
        return TypedResults.Ok(roles.Select(role => new RoleResponse(role.Id, role.Name, role.Description, role.IsSystem,
            role.Permissions.Select(p => p.Permission).Order(StringComparer.Ordinal).ToList(), role.Version)).ToList());
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> SetUserRolesAsync(
        Guid userId, SetUserRolesRequest request, EstrichOneDbContext db, IRoleAssignments assignments,
        IAuditLog audit, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        if (!await db.Set<User>().AnyAsync(user => user.Id == userId, cancellationToken))
        {
            return Problem(StatusCodes.Status404NotFound, "USER_NOT_FOUND", "Benutzer nicht gefunden");
        }

        try
        {
            await assignments.SetRolesAsync(userId, request.RoleIds.Distinct().ToList(), cancellationToken);
        }
        catch (LastAdministratorException exception)
        {
            return Problem(StatusCodes.Status409Conflict, "LAST_ADMINISTRATOR", exception.Message);
        }
        catch (UnknownRoleException exception)
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, "UNKNOWN_ROLE", exception.Message);
        }

        audit.Record("access.user_roles.changed", currentUser.UserId, "user", userId.ToString(),
            new Dictionary<string, string?> { ["roles"] = string.Join(",", request.RoleIds) });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<RoleResponse>, ProblemHttpResult>> SaveRoleAsync(
        Guid id, SaveRoleRequest request, EstrichOneDbContext db, IAuditLog audit, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        if (name.Length == 0)
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, "NAME_REQUIRED", "Bitte einen Namen für die Rolle angeben.");
        }

        var unknown = request.Permissions.FirstOrDefault(p => !Permissions.Exists(p));
        if (unknown is not null)
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, "UNKNOWN_PERMISSION", $"Unbekanntes Recht: {unknown}");
        }

        if (await db.Set<Role>().AnyAsync(r => r.Id != id && EF.Functions.ILike(r.Name, name.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal)), cancellationToken))
        {
            return Problem(StatusCodes.Status409Conflict, "ROLE_NAME_TAKEN", "Eine Rolle mit diesem Namen gibt es bereits.");
        }

        var role = await db.Set<Role>().Include(r => r.Permissions).SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        var before = role?.Permissions.Select(p => p.Permission).ToHashSet(StringComparer.Ordinal) ?? [];
        if (role is null)
        {
            role = Role.Create(id, name, request.Description?.Trim(), isSystem: false, request.Permissions.Distinct());
            db.Add(role);
        }
        else
        {
            if (request.Version is { } version && version != role.Version)
            {
                return Problem(StatusCodes.Status412PreconditionFailed, "VERSION_CONFLICT", "Die Rolle wurde inzwischen geändert. Bitte neu laden.");
            }

            role.Rename(name, string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim());
            // Die Administratorrolle behält alle Rechte (sonst könnte sich niemand mehr selbst helfen).
            role.SetPermissions(id == StandardRoles.AdministratorId ? Permissions.All.Select(p => p.Code) : request.Permissions.Distinct());
        }

        var after = role.Permissions.Select(p => p.Permission).ToHashSet(StringComparer.Ordinal);
        audit.Record("access.role.saved", currentUser.UserId, "role", id.ToString(), new Dictionary<string, string?>
        {
            ["name"] = name,
            ["added"] = string.Join(",", after.Except(before).Order(StringComparer.Ordinal)),
            ["removed"] = string.Join(",", before.Except(after).Order(StringComparer.Ordinal)),
        });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(new RoleResponse(role.Id, role.Name, role.Description, role.IsSystem,
            role.Permissions.Select(p => p.Permission).Order(StringComparer.Ordinal).ToList(), role.Version));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteRoleAsync(
        Guid id, EstrichOneDbContext db, IAuditLog audit, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var role = await db.Set<Role>().SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (role is null)
        {
            return Problem(StatusCodes.Status404NotFound, "ROLE_NOT_FOUND", "Rolle nicht gefunden");
        }

        if (role.IsSystem)
        {
            return Problem(StatusCodes.Status409Conflict, "SYSTEM_ROLE", "Standardrollen können nicht gelöscht werden.");
        }

        db.RemoveRange(await db.Set<UserRole>().Where(ur => ur.RoleId == id).ToListAsync(cancellationToken));
        db.Remove(role);
        audit.Record("access.role.deleted", currentUser.UserId, "role", id.ToString(), new Dictionary<string, string?> { ["name"] = role.Name });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static ProblemHttpResult Problem(int status, string code, string title) =>
        TypedResults.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });
}
