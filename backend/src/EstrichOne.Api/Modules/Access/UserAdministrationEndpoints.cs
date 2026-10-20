using System.ComponentModel.DataAnnotations;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Access;

public sealed record UserListItem(
    Guid Id, string DisplayName, string UserName, string? Email, string? Phone, bool IsActive, DateTimeOffset? LastLoginAt,
    IReadOnlyList<Guid> RoleIds, long Version);

public sealed record CreateUserRequest(
    [property: Required, MaxLength(User.MaxNameLength)] string DisplayName,
    [property: Required, MaxLength(User.MaxUserNameLength)] string UserName,
    [property: MaxLength(User.MaxEmailLength), EmailAddress] string? Email,
    [property: MaxLength(50)] string? Phone,
    [property: Required] IReadOnlyList<Guid> RoleIds);

public sealed record UpdateUserRequest(
    [property: Required, MaxLength(User.MaxNameLength)] string DisplayName,
    [property: MaxLength(User.MaxEmailLength), EmailAddress] string? Email,
    [property: MaxLength(50)] string? Phone);

/// <param name="OneTimePassword">Nur in dieser Antwort sichtbar; muss bei der ersten Anmeldung ersetzt werden.</param>
public sealed record UserWithPassword(UserListItem User, string OneTimePassword);

/// <summary>Benutzerverwaltung im Web (docs/reengineering/17, Abschnitt 2: Einmalpasswort + Zwangswechsel).</summary>
internal static class UserAdministrationEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var users = api.MapGroup("/users").WithTags("Benutzer").RequirePermission(Permissions.UsersManage);

        users.MapGet("/", ListAsync).WithName("ListUsers");
        users.MapPost("/", CreateAsync).WithName("CreateUser")
            .WithSummary("Benutzer mit Einmalpasswort anlegen")
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        users.MapPut("/{id:guid}", UpdateAsync).WithName("UpdateUser").ProducesProblem(StatusCodes.Status404NotFound);
        users.MapPost("/{id:guid}/reset-password", ResetPasswordAsync).WithName("ResetUserPassword")
            .WithSummary("Einmalpasswort vergeben; beendet alle Sitzungen des Benutzers")
            .ProducesProblem(StatusCodes.Status404NotFound);
        users.MapPost("/{id:guid}/deactivate", (Guid id, UserAdministration admin, CancellationToken ct) => admin.SetActiveAsync(id, false, ct))
            .WithName("DeactivateUser").WithSummary("Konto sperren; alle Sitzungen enden sofort")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        users.MapPost("/{id:guid}/activate", (Guid id, UserAdministration admin, CancellationToken ct) => admin.SetActiveAsync(id, true, ct))
            .WithName("ActivateUser").ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<Ok<List<UserListItem>>> ListAsync(UserAdministration admin, CancellationToken cancellationToken) =>
        TypedResults.Ok(await admin.ListAsync(null, cancellationToken));

    private static async Task<Results<Created<UserWithPassword>, ProblemHttpResult>> CreateAsync(
        CreateUserRequest request, IUserAccounts accounts, IRoleAssignments roles, UserAdministration admin, EstrichOneDbContext db,
        ICurrentUser current, IAuditLog audit, CancellationToken cancellationToken)
    {
        var roleIds = request.RoleIds.Distinct().ToList();
        if (await db.Set<Role>().CountAsync(r => roleIds.Contains(r.Id), cancellationToken) != roleIds.Count)
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, "UNKNOWN_ROLE", "Mindestens eine der Rollen existiert nicht.");
        }

        var password = accounts.GenerateOneTimePassword();
        User user;
        try
        {
            user = await accounts.CreateLocalUserAsync(request.DisplayName, request.UserName, request.Email, password,
                mustChangePassword: true, cancellationToken);
        }
        catch (DuplicateUserException exception)
        {
            return Problem(StatusCodes.Status409Conflict, "USER_EXISTS", exception.Message);
        }
        catch (PasswordRejectedException)
        {
            // Das Einmalpasswort enthält zufällig den Anmeldenamen: sehr selten, einfach neu würfeln.
            password = accounts.GenerateOneTimePassword();
            user = await accounts.CreateLocalUserAsync(request.DisplayName, request.UserName, request.Email, password, true, cancellationToken);
        }

        await roles.SetRolesAsync(user.Id, roleIds, cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.Phone))
        {
            user.ChangeContact(user.Email, request.Phone);
        }

        audit.Record("users.created", current.UserId, "user", user.Id.ToString());
        await db.SaveChangesAsync(cancellationToken);
        var item = (await admin.ListAsync(user.Id, cancellationToken)).Single();
        return TypedResults.Created($"/api/v1/users/{user.Id}", new UserWithPassword(item, password));
    }

    private static async Task<Results<Ok<UserListItem>, ProblemHttpResult>> UpdateAsync(
        Guid id, UpdateUserRequest request, UserAdministration admin, EstrichOneDbContext db, ICurrentUser current, IAuditLog audit,
        CancellationToken cancellationToken)
    {
        var user = await db.Set<User>().SingleOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        var normalizedEmail = string.IsNullOrWhiteSpace(request.Email) ? null : User.Normalize(request.Email);
        if (normalizedEmail is not null && await db.Set<User>().AnyAsync(u => u.Id != id && u.NormalizedEmail == normalizedEmail, cancellationToken))
        {
            return Problem(StatusCodes.Status409Conflict, "USER_EXISTS", "Diese E-Mail-Adresse ist bereits vergeben.");
        }

        user.Rename(request.DisplayName);
        user.ChangeContact(request.Email, request.Phone);
        audit.Record("users.updated", current.UserId, "user", id.ToString());
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok((await admin.ListAsync(id, cancellationToken)).Single());
    }

    private static async Task<Results<Ok<UserWithPassword>, ProblemHttpResult>> ResetPasswordAsync(
        Guid id, IUserAccounts accounts, UserAdministration admin, EstrichOneDbContext db, ICurrentUser current, IAuditLog audit,
        CancellationToken cancellationToken)
    {
        if (!await db.Set<User>().AnyAsync(u => u.Id == id, cancellationToken))
        {
            return NotFound();
        }

        var password = await accounts.ResetPasswordAsync(id, cancellationToken);
        audit.Record("users.password_reset", current.UserId, "user", id.ToString());
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(new UserWithPassword((await admin.ListAsync(id, cancellationToken)).Single(), password));
    }

    internal static ProblemHttpResult NotFound() => Problem(StatusCodes.Status404NotFound, "USER_NOT_FOUND", "Benutzer nicht gefunden.");

    internal static ProblemHttpResult Problem(int status, string code, string title) =>
        TypedResults.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });
}

internal sealed class UserAdministration(EstrichOneDbContext db, IUserAccounts accounts, ICurrentUser current, IAuditLog audit)
{
    public async Task<List<UserListItem>> ListAsync(Guid? onlyId, CancellationToken cancellationToken)
    {
        var users = await db.Set<User>().AsNoTracking()
            .Where(u => onlyId == null || u.Id == onlyId)
            .OrderBy(u => u.DisplayName)
            .ToListAsync(cancellationToken);
        var ids = users.Select(u => u.Id).ToList();
        var roles = (await db.Set<UserRole>().AsNoTracking().Where(r => ids.Contains(r.UserId)).ToListAsync(cancellationToken))
            .ToLookup(r => r.UserId, r => r.RoleId);
        return users.Select(u => new UserListItem(u.Id, u.DisplayName, u.UserName, u.Email, u.Phone, u.IsActive, u.LastLoginAt,
            roles[u.Id].ToList(), u.Version)).ToList();
    }

    public async Task<Results<Ok<UserListItem>, ProblemHttpResult>> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken)
    {
        if (!await db.Set<User>().AnyAsync(u => u.Id == id, cancellationToken))
        {
            return UserAdministrationEndpoints.NotFound();
        }

        if (!active)
        {
            if (id == current.UserId)
            {
                return UserAdministrationEndpoints.Problem(StatusCodes.Status409Conflict, "CANNOT_DEACTIVATE_SELF",
                    "Das eigene Konto kann nicht gesperrt werden.");
            }

            var isAdministrator = await db.Set<UserRole>().AnyAsync(r => r.UserId == id && r.RoleId == StandardRoles.AdministratorId, cancellationToken);
            var otherAdministrators = await db.Set<UserRole>()
                .Where(r => r.RoleId == StandardRoles.AdministratorId && r.UserId != id)
                .Join(db.Set<User>().Where(u => u.IsActive), r => r.UserId, u => u.Id, (r, _) => r)
                .AnyAsync(cancellationToken);
            if (isAdministrator && !otherAdministrators)
            {
                return UserAdministrationEndpoints.Problem(StatusCodes.Status409Conflict, "LAST_ADMINISTRATOR",
                    "Mindestens ein aktiver Benutzer muss die Rolle Administrator behalten.");
            }
        }

        await accounts.SetActiveAsync(id, active, cancellationToken);
        audit.Record(active ? "users.activated" : "users.deactivated", current.UserId, "user", id.ToString());
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok((await ListAsync(id, cancellationToken)).Single());
    }
}
