using System.ComponentModel.DataAnnotations;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Access;

public sealed record RoleResponse(Guid Id, string Name, string? Description, bool IsSystem, IReadOnlyList<string> Permissions, long Version);

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

    private static ProblemHttpResult Problem(int status, string code, string title) =>
        TypedResults.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });
}
