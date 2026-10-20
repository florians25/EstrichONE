using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using EstrichOne.Hosting.Api;
using EstrichOne.Hosting.Data;
using EstrichOne.Hosting.Domain;
using EstrichOne.Hosting.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QRCoder;

namespace EstrichOne.Hosting.Auth;

public sealed record LoginRequest(string? UserName, string? Password, string? Code);
public sealed record SetupRequest(string? SetupCode, string? UserName, string? DisplayName, string? Password);
public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
public sealed record TotpCodeRequest(string? Code);
public sealed record PasswordConfirmRequest(string? Password);
public sealed record CreateAdminRequest(string? UserName, string? DisplayName, string? Password);
public sealed record UserInfo(Guid Id, string UserName, string? DisplayName, bool TotpEnabled, JsonElement? Preferences);
public sealed record SessionState(bool SetupRequired, bool Authenticated, UserInfo? User, string? CsrfToken, bool Simulation, string ConsoleVersion);
public sealed record TotpSetup(string Secret, string Uri, string QrSvg);
public sealed record AdminListItem(Guid Id, string UserName, string? DisplayName, bool TotpEnabled, DateTime CreatedAt, DateTime? LastLoginAt, bool Locked);

public static class AuthEndpoints
{
    public const string CsrfClaim = "csrf";
    public const string StampClaim = "stamp";
    public const string CsrfHeader = "X-CSRF-Token";
    public const string LoginRateLimit = "login";
    public const int MaxFailedLogins = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private static readonly PasswordHasher<AdminUser> Hasher = new();

    /// <summary>Hash eines Zufallspassworts: gleiche Rechenzeit bei unbekanntem Benutzer (keine Benutzer-Aufzählung über die Antwortzeit).</summary>
    private static readonly string DummyHash = Hasher.HashPassword(null!, Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)));

    public static string HashPassword(AdminUser user, string password) => Hasher.HashPassword(user, password);

    public static bool VerifyPassword(AdminUser user, string password) =>
        Hasher.VerifyHashedPassword(user, user.PasswordHash, password) != PasswordVerificationResult.Failed;

    public static void Map(IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth").WithTags("Anmeldung");
        auth.MapGet("/state", GetStateAsync).AllowAnonymous();
        auth.MapPost("/login", LoginAsync).AllowAnonymous().RequireRateLimiting(LoginRateLimit);
        auth.MapPost("/setup", SetupAsync).AllowAnonymous().RequireRateLimiting(LoginRateLimit);
        auth.MapPost("/logout", async (HttpContext context, AuditLog audit) =>
        {
            await audit.WriteAsync("Abmeldung");
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        }).RequireAuthorization();

        var account = app.MapGroup("/api/account").WithTags("Konto").RequireAuthorization();
        account.MapPost("/password", ChangePasswordAsync);
        account.MapPost("/totp/begin", BeginTotpAsync);
        account.MapPost("/totp/enable", EnableTotpAsync);
        account.MapPost("/totp/disable", DisableTotpAsync);
        account.MapPut("/preferences", SavePreferencesAsync);

        var admins = app.MapGroup("/api/admins").WithTags("Administratoren").RequireAuthorization();
        admins.MapGet("/", async (HostingDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var list = await db.Admins.AsNoTracking().OrderBy(a => a.UserName).ToListAsync(ct);
            return Results.Ok(list.Select(a => new AdminListItem(a.Id, a.UserName, a.DisplayName, a.TotpEnabled, a.CreatedAt, a.LastLoginAt,
                a.LockedUntil > now)));
        });
        admins.MapPost("/", CreateAdminAsync);
        admins.MapDelete("/{id:guid}", DeleteAdminAsync);
        admins.MapPost("/{id:guid}/unlock", async (Guid id, HostingDbContext db, AuditLog audit, CancellationToken ct) =>
        {
            var admin = await db.Admins.FindAsync([id], ct);
            if (admin is null)
            {
                return ApiErrors.NotFound();
            }

            admin.LockedUntil = null;
            admin.FailedLogins = 0;
            await audit.WriteAsync("Administrator entsperrt", details: admin.UserName, cancellationToken: ct);
            return Results.NoContent();
        });
    }

    private static async Task<IResult> GetStateAsync(HttpContext context, HostingDbContext db, SetupCodeService setup,
        IOptions<HostingOptions> options, CancellationToken cancellationToken)
    {
        var setupRequired = await setup.IsSetupRequiredAsync(cancellationToken);
        UserInfo? user = null;
        if (context.User.Identity?.IsAuthenticated == true && Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
        {
            var admin = await db.Admins.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
            if (admin is not null)
            {
                user = ToInfo(admin);
            }
        }

        return Results.Ok(new SessionState(setupRequired, user is not null, user,
            user is null ? null : context.User.FindFirstValue(CsrfClaim), options.Value.IsFake, ConsoleVersion.Current));
    }

    private static UserInfo ToInfo(AdminUser admin)
    {
        JsonElement? preferences = null;
        if (!string.IsNullOrWhiteSpace(admin.Preferences))
        {
            try
            {
                preferences = JsonDocument.Parse(admin.Preferences).RootElement.Clone();
            }
            catch (JsonException)
            {
                preferences = null;
            }
        }

        return new UserInfo(admin.Id, admin.UserName, admin.DisplayName, admin.TotpEnabled, preferences);
    }

    private static async Task<IResult> LoginAsync(LoginRequest request, HttpContext context, HostingDbContext db, AuditLog audit,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        var userName = (request.UserName ?? string.Empty).Trim().ToLowerInvariant();
        var admin = await db.Admins.FirstOrDefaultAsync(a => a.UserName == userName, cancellationToken);
        var now = clock.GetUtcNow();
        if (admin is null)
        {
            Hasher.VerifyHashedPassword(null!, DummyHash, request.Password ?? string.Empty);
            await audit.WriteAsync("Anmeldung fehlgeschlagen", details: $"Unbekannter Benutzer „{Truncate(userName)}“", success: false,
                actor: AuditLog.System, cancellationToken: cancellationToken);
            return ApiErrors.Unauthorized("Benutzername oder Passwort ist falsch.");
        }

        if (admin.LockedUntil > now.UtcDateTime)
        {
            await audit.WriteAsync("Anmeldung abgewiesen", details: "Konto vorübergehend gesperrt", success: false, actor: admin.UserName,
                cancellationToken: cancellationToken);
            return Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Konto vorübergehend gesperrt",
                detail: $"Zu viele Fehlversuche. Bitte nach {admin.LockedUntil.Value.ToLocalTime():HH:mm} Uhr erneut versuchen.");
        }

        if (!VerifyPassword(admin, request.Password ?? string.Empty))
        {
            return await FailAsync(admin, "Falsches Passwort", "Benutzername oder Passwort ist falsch.");
        }

        if (admin.TotpEnabled)
        {
            if (string.IsNullOrWhiteSpace(request.Code))
            {
                return ApiErrors.Unauthorized("Bitte den Code aus der Authenticator-App eingeben.", "TOTP_REQUIRED");
            }

            var step = Totp.Verify(admin.TotpSecret!, request.Code, now, admin.TotpLastStep);
            if (step is null)
            {
                return await FailAsync(admin, "Falscher Zwei-Faktor-Code", "Der Code ist ungültig oder abgelaufen.", "TOTP_INVALID");
            }

            admin.TotpLastStep = step.Value;
        }

        admin.FailedLogins = 0;
        admin.LockedUntil = null;
        admin.LastLoginAt = now.UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        await SignInAsync(context, admin);
        await audit.WriteAsync("Anmeldung", actor: admin.UserName, details: admin.TotpEnabled ? "mit Zwei-Faktor-Code" : null,
            cancellationToken: cancellationToken);
        return Results.NoContent();

        async Task<IResult> FailAsync(AdminUser user, string reason, string message, string? code = null)
        {
            user.FailedLogins++;
            var locked = user.FailedLogins >= MaxFailedLogins;
            if (locked)
            {
                user.LockedUntil = now.Add(LockoutDuration).UtcDateTime;
                user.FailedLogins = 0;
            }

            await audit.WriteAsync("Anmeldung fehlgeschlagen", details: reason + (locked ? " – Konto für 15 Minuten gesperrt" : string.Empty),
                success: false, actor: user.UserName, cancellationToken: cancellationToken);
            return ApiErrors.Unauthorized(message, code);
        }
    }

    private static async Task<IResult> SetupAsync(SetupRequest request, HttpContext context, HostingDbContext db, SetupCodeService setup,
        AuditLog audit, CancellationToken cancellationToken)
    {
        if (!await setup.IsSetupRequiredAsync(cancellationToken))
        {
            return ApiErrors.Conflict("Die Konsole ist bereits eingerichtet.");
        }

        if (!await setup.VerifyAsync(request.SetupCode, cancellationToken))
        {
            await audit.WriteAsync("Ersteinrichtung abgewiesen", details: "Ungültiger Einrichtungscode", success: false,
                actor: AuditLog.System, cancellationToken: cancellationToken);
            return ApiErrors.Unauthorized("Der Einrichtungscode ist ungültig oder abgelaufen. Den aktuellen Code zeigt: docker logs estrichone-hosting-console");
        }

        var userName = (request.UserName ?? string.Empty).Trim().ToLowerInvariant();
        var errors = new Dictionary<string, string>();
        if (Validation.CheckUserName(userName) is { } userError) errors["userName"] = userError;
        if (Validation.CheckPassword(request.Password) is { } passwordError) errors["password"] = passwordError;
        if (Validation.CheckText(request.DisplayName, "Der Name", 80, false) is { } nameError) errors["displayName"] = nameError;
        if (errors.Count > 0)
        {
            return ApiErrors.Validation(errors);
        }

        var admin = new AdminUser { UserName = userName, DisplayName = request.DisplayName?.Trim(), PasswordHash = string.Empty };
        admin.PasswordHash = HashPassword(admin, request.Password!);
        admin.LastLoginAt = DateTime.UtcNow;
        db.Admins.Add(admin);
        await db.SaveChangesAsync(cancellationToken);
        await setup.ConsumeAsync(cancellationToken);
        await SignInAsync(context, admin);
        await audit.WriteAsync("Ersteinrichtung abgeschlossen", details: $"Administrator {userName} angelegt", actor: userName,
            cancellationToken: cancellationToken);
        return Results.NoContent();
    }

    private static async Task<AdminUser?> CurrentAsync(HttpContext context, HostingDbContext db, CancellationToken cancellationToken) =>
        Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? await db.Admins.FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
            : null;

    private static async Task<IResult> ChangePasswordAsync(ChangePasswordRequest request, HttpContext context, HostingDbContext db,
        AuditLog audit, CancellationToken cancellationToken)
    {
        var admin = await CurrentAsync(context, db, cancellationToken);
        if (admin is null)
        {
            return Results.Unauthorized();
        }

        if (!VerifyPassword(admin, request.CurrentPassword ?? string.Empty))
        {
            await audit.WriteAsync("Passwortänderung abgewiesen", details: "Aktuelles Passwort falsch", success: false, cancellationToken: cancellationToken);
            return ApiErrors.BadRequest("Das aktuelle Passwort ist falsch.");
        }

        if (Validation.CheckPassword(request.NewPassword) is { } error)
        {
            return ApiErrors.BadRequest(error);
        }

        admin.PasswordHash = HashPassword(admin, request.NewPassword!);
        admin.SecurityStamp++;
        await db.SaveChangesAsync(cancellationToken);
        await SignInAsync(context, admin);
        await audit.WriteAsync("Passwort geändert", details: "Andere Sitzungen wurden beendet.", cancellationToken: cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> BeginTotpAsync(HttpContext context, HostingDbContext db, CancellationToken cancellationToken)
    {
        var admin = await CurrentAsync(context, db, cancellationToken);
        if (admin is null)
        {
            return Results.Unauthorized();
        }

        if (admin.TotpEnabled)
        {
            return ApiErrors.Conflict("Die Zwei-Faktor-Anmeldung ist bereits eingeschaltet.");
        }

        admin.TotpSecret = Totp.NewSecret();
        await db.SaveChangesAsync(cancellationToken);
        var uri = Totp.ProvisioningUri("EstrichONE Hosting", admin.UserName, admin.TotpSecret);
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.M);
        var svg = new SvgQRCode(data).GetGraphic(5, "#000000", "#ffffff", drawQuietZones: true, SvgQRCode.SizingMode.ViewBoxAttribute);
        return Results.Ok(new TotpSetup(admin.TotpSecret, uri, svg));
    }

    private static async Task<IResult> EnableTotpAsync(TotpCodeRequest request, HttpContext context, HostingDbContext db, AuditLog audit,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        var admin = await CurrentAsync(context, db, cancellationToken);
        if (admin?.TotpSecret is null)
        {
            return ApiErrors.BadRequest("Bitte die Einrichtung zuerst starten.");
        }

        var step = Totp.Verify(admin.TotpSecret, request.Code, clock.GetUtcNow(), admin.TotpLastStep);
        if (step is null)
        {
            return ApiErrors.BadRequest("Der Code stimmt nicht. Bitte Uhrzeit des Smartphones prüfen und den aktuellen Code eingeben.");
        }

        admin.TotpEnabled = true;
        admin.TotpLastStep = step.Value;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync("Zwei-Faktor-Anmeldung eingeschaltet", cancellationToken: cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> DisableTotpAsync(PasswordConfirmRequest request, HttpContext context, HostingDbContext db,
        AuditLog audit, CancellationToken cancellationToken)
    {
        var admin = await CurrentAsync(context, db, cancellationToken);
        if (admin is null)
        {
            return Results.Unauthorized();
        }

        if (!VerifyPassword(admin, request.Password ?? string.Empty))
        {
            return ApiErrors.BadRequest("Das Passwort ist falsch.");
        }

        admin.TotpEnabled = false;
        admin.TotpSecret = null;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync("Zwei-Faktor-Anmeldung ausgeschaltet", cancellationToken: cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> SavePreferencesAsync(JsonElement preferences, HttpContext context, HostingDbContext db,
        CancellationToken cancellationToken)
    {
        var admin = await CurrentAsync(context, db, cancellationToken);
        if (admin is null)
        {
            return Results.Unauthorized();
        }

        var json = preferences.GetRawText();
        if (preferences.ValueKind != JsonValueKind.Object || json.Length > 4000)
        {
            return ApiErrors.BadRequest("Ungültige Einstellungen.");
        }

        admin.Preferences = json;
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> CreateAdminAsync(CreateAdminRequest request, HostingDbContext db, AuditLog audit, CancellationToken cancellationToken)
    {
        var userName = (request.UserName ?? string.Empty).Trim().ToLowerInvariant();
        var errors = new Dictionary<string, string>();
        if (Validation.CheckUserName(userName) is { } userError) errors["userName"] = userError;
        if (Validation.CheckPassword(request.Password) is { } passwordError) errors["password"] = passwordError;
        if (Validation.CheckText(request.DisplayName, "Der Name", 80, false) is { } nameError) errors["displayName"] = nameError;
        if (errors.Count > 0)
        {
            return ApiErrors.Validation(errors);
        }

        if (await db.Admins.AnyAsync(a => a.UserName == userName, cancellationToken))
        {
            return ApiErrors.Conflict($"Den Benutzer „{userName}“ gibt es bereits.");
        }

        var admin = new AdminUser { UserName = userName, DisplayName = request.DisplayName?.Trim(), PasswordHash = string.Empty };
        admin.PasswordHash = HashPassword(admin, request.Password!);
        db.Admins.Add(admin);
        await audit.WriteAsync("Administrator angelegt", details: userName, cancellationToken: cancellationToken);
        return Results.Created($"/api/admins/{admin.Id}", new AdminListItem(admin.Id, admin.UserName, admin.DisplayName, false, admin.CreatedAt, null, false));
    }

    private static async Task<IResult> DeleteAdminAsync(Guid id, HttpContext context, HostingDbContext db, AuditLog audit, CancellationToken cancellationToken)
    {
        var admin = await db.Admins.FindAsync([id], cancellationToken);
        if (admin is null)
        {
            return ApiErrors.NotFound();
        }

        if (context.User.FindFirstValue(ClaimTypes.NameIdentifier) == id.ToString())
        {
            return ApiErrors.Conflict("Das eigene Konto kann nicht gelöscht werden.");
        }

        if (await db.Admins.CountAsync(cancellationToken) <= 1)
        {
            return ApiErrors.Conflict("Der letzte Administrator kann nicht gelöscht werden.");
        }

        db.Admins.Remove(admin);
        await audit.WriteAsync("Administrator gelöscht", details: admin.UserName, cancellationToken: cancellationToken);
        return Results.NoContent();
    }

    public static async Task SignInAsync(HttpContext context, AdminUser admin)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, admin.Id.ToString()),
            new Claim(ClaimTypes.Name, admin.UserName),
            new Claim(StampClaim, admin.SecurityStamp.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim(CsrfClaim, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))),
        ], CookieAuthenticationDefaults.AuthenticationScheme);
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = false, AllowRefresh = true });
    }

    private static string Truncate(string text) => text.Length > 40 ? text[..40] : text;
}
