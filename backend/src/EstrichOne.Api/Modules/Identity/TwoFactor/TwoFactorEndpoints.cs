using System.ComponentModel.DataAnnotations;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Identity.Passwords;
using EstrichOne.Api.Modules.Identity.Sessions;
using EstrichOne.Api.Modules.Organization.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Identity.TwoFactor;

/// <param name="RequiredByPolicy">Pflicht für dieses Konto (Verwaltungsrechte und Einstellung „Zwei-Faktor für Administratoren“).</param>
/// <param name="Restricted">Pflicht, aber noch nicht eingerichtet: Verwaltungsrechte ruhen bis zur Einrichtung.</param>
public sealed record TwoFactorStatus(bool Enabled, DateTimeOffset? EnabledAt, int RecoveryCodesRemaining, bool RequiredByPolicy, bool Restricted);

public sealed record TwoFactorSetupRequest([property: Required, MaxLength(PasswordPolicy.MaxLength)] string CurrentPassword);

/// <param name="Secret">Base32 zum Abtippen, falls der QR-Code nicht gescannt werden kann.</param>
/// <param name="OtpAuthUri">Inhalt des QR-Codes (<c>otpauth://totp/…</c>).</param>
public sealed record TwoFactorSetupResponse(string Secret, string OtpAuthUri, int Digits, int PeriodSeconds);

public sealed record TwoFactorEnableRequest([property: Required, MaxLength(32)] string Code);

/// <param name="RecoveryCodes">Nur in dieser Antwort sichtbar – ausdrucken oder sicher ablegen.</param>
public sealed record TwoFactorRecoveryCodes(IReadOnlyList<string> RecoveryCodes);

public sealed record TwoFactorConfirmRequest(
    [property: Required, MaxLength(PasswordPolicy.MaxLength)] string CurrentPassword,
    [property: Required, MaxLength(32)] string Code);

/// <summary>Zwei-Faktor-Anmeldung (TOTP) im eigenen Konto einrichten, Codes erneuern, ausschalten.</summary>
internal static class TwoFactorEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/auth/two-factor").WithTags("Anmeldung");
        group.MapGet("/", StatusAsync).WithName("GetTwoFactorStatus").WithSummary("Zwei-Faktor-Anmeldung des eigenen Kontos")
            .AllowPendingDevice().AllowDuringPasswordChange();
        group.MapPost("/setup", SetupAsync).WithName("BeginTwoFactorSetup")
            .WithSummary("Einrichtung beginnen: neues Geheimnis (QR-Code); wirksam erst nach Bestätigung mit einem Code")
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status409Conflict)
            .RequireRateLimiting(AuthEndpoints.RateLimitPolicy).AllowPendingDevice().AllowDuringPasswordChange();
        group.MapPost("/enable", EnableAsync).WithName("EnableTwoFactor")
            .WithSummary("Einrichtung mit dem ersten Code aus der App bestätigen; liefert einmalig 10 Wiederherstellungscodes")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequireRateLimiting(AuthEndpoints.RateLimitPolicy).AllowPendingDevice().AllowDuringPasswordChange();
        group.MapPost("/recovery-codes", RenewCodesAsync).WithName("RenewTwoFactorRecoveryCodes")
            .WithSummary("Neue Wiederherstellungscodes (alte werden ungültig); Passwort und aktueller Code nötig")
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status409Conflict)
            .RequireRateLimiting(AuthEndpoints.RateLimitPolicy);
        group.MapPost("/disable", DisableAsync).WithName("DisableTwoFactor")
            .WithSummary("Zwei-Faktor-Anmeldung ausschalten; Passwort und aktueller Code nötig (nicht bei Pflicht)")
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status409Conflict)
            .RequireRateLimiting(AuthEndpoints.RateLimitPolicy);
    }

    private static async Task<Ok<TwoFactorStatus>> StatusAsync(ICurrentUser current, TwoFactorService twoFactor, ITwoFactorPolicy policy,
        CancellationToken cancellationToken)
    {
        var entry = await twoFactor.FindAsync(current.UserId, cancellationToken);
        var enabled = entry?.IsEnabled == true;
        var required = await policy.IsRequiredAsync(current.UserId, cancellationToken);
        var remaining = enabled ? await twoFactor.RemainingRecoveryCodesAsync(current.UserId, cancellationToken) : 0;
        return TypedResults.Ok(new TwoFactorStatus(enabled, entry?.EnabledAt, remaining, required, required && !enabled));
    }

    private static async Task<Results<Ok<TwoFactorSetupResponse>, ProblemHttpResult>> SetupAsync(
        TwoFactorSetupRequest request, ICurrentUser current, IUserAccounts accounts, TwoFactorService twoFactor, EstrichOneDbContext db,
        ICompanyProfiles companies, CancellationToken cancellationToken)
    {
        if (!await accounts.VerifyPasswordAsync(current.UserId, request.CurrentPassword, cancellationToken))
        {
            return WrongPassword();
        }

        var user = await db.Set<User>().SingleAsync(u => u.Id == current.UserId, cancellationToken);
        var company = await companies.GetDisplayNameAsync(cancellationToken);
        var issuer = company == "EstrichONE" ? "EstrichONE" : $"EstrichONE {company}";
        if (await twoFactor.BeginSetupAsync(user, issuer.Replace(":", " ", StringComparison.Ordinal), cancellationToken) is not { } setup)
        {
            return AuthEndpoints.Problem(StatusCodes.Status409Conflict, "TWO_FACTOR_ALREADY_ENABLED", "Bereits eingerichtet",
                "Die Zwei-Faktor-Anmeldung ist schon eingeschaltet. Zum Wechsel des Handys erst ausschalten.");
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(new TwoFactorSetupResponse(setup.Secret, setup.Uri, Totp.Digits, Totp.PeriodSeconds));
    }

    private static async Task<Results<Ok<TwoFactorRecoveryCodes>, ProblemHttpResult>> EnableAsync(
        TwoFactorEnableRequest request, ICurrentUser current, TwoFactorService twoFactor, EstrichOneDbContext db, IAuditLog audit,
        CancellationToken cancellationToken)
    {
        if (await twoFactor.EnableAsync(current.UserId, request.Code, cancellationToken) is not { } codes)
        {
            return AuthEndpoints.Problem(StatusCodes.Status422UnprocessableEntity, "TWO_FACTOR_INVALID", "Code nicht korrekt",
                "Der Code passt nicht (oder die Einrichtung ist älter als 30 Minuten). Bitte den aktuellen Code aus der App eingeben bzw. neu beginnen.");
        }

        audit.Record(AuditActions.TwoFactorEnabled, current.UserId, "user", current.UserId.ToString());
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(new TwoFactorRecoveryCodes(codes));
    }

    private static async Task<Results<Ok<TwoFactorRecoveryCodes>, ProblemHttpResult>> RenewCodesAsync(
        TwoFactorConfirmRequest request, ICurrentUser current, IUserAccounts accounts, TwoFactorService twoFactor, EstrichOneDbContext db,
        IAuditLog audit, CancellationToken cancellationToken)
    {
        if (await ConfirmAsync(request, current, accounts, twoFactor, cancellationToken) is { } problem)
        {
            return problem;
        }

        var codes = await twoFactor.ReplaceRecoveryCodesAsync(current.UserId, cancellationToken);
        audit.Record(AuditActions.RecoveryCodesRenewed, current.UserId, "user", current.UserId.ToString());
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(new TwoFactorRecoveryCodes(codes));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DisableAsync(
        TwoFactorConfirmRequest request, ICurrentUser current, IUserAccounts accounts, TwoFactorService twoFactor, ITwoFactorPolicy policy,
        EstrichOneDbContext db, IAuditLog audit, CancellationToken cancellationToken)
    {
        if (await policy.IsRequiredAsync(current.UserId, cancellationToken))
        {
            return AuthEndpoints.Problem(StatusCodes.Status409Conflict, "TWO_FACTOR_REQUIRED_BY_POLICY", "Pflicht",
                "Für Konten mit Verwaltungsrechten ist die Zwei-Faktor-Anmeldung vorgeschrieben. Neues Handy? Erst neu einrichten lassen (Benutzerverwaltung).");
        }

        if (await ConfirmAsync(request, current, accounts, twoFactor, cancellationToken) is { } problem)
        {
            return problem;
        }

        await twoFactor.DisableAsync(current.UserId, cancellationToken);
        audit.Record(AuditActions.TwoFactorDisabled, current.UserId, "user", current.UserId.ToString());
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<ProblemHttpResult?> ConfirmAsync(TwoFactorConfirmRequest request, ICurrentUser current, IUserAccounts accounts,
        TwoFactorService twoFactor, CancellationToken cancellationToken)
    {
        if (!await accounts.VerifyPasswordAsync(current.UserId, request.CurrentPassword, cancellationToken))
        {
            return WrongPassword();
        }

        return await twoFactor.VerifyAsync(current.UserId, request.Code, cancellationToken) switch
        {
            TwoFactorCheck.Valid or TwoFactorCheck.RecoveryCodeUsed => null,
            TwoFactorCheck.NotEnabled => AuthEndpoints.Problem(StatusCodes.Status409Conflict, "TWO_FACTOR_NOT_ENABLED", "Nicht eingerichtet",
                "Die Zwei-Faktor-Anmeldung ist für dieses Konto nicht eingeschaltet."),
            _ => AuthEndpoints.Problem(StatusCodes.Status403Forbidden, "TWO_FACTOR_INVALID", "Code nicht korrekt",
                "Der Bestätigungscode ist falsch oder abgelaufen."),
        };
    }

    private static ProblemHttpResult WrongPassword() => AuthEndpoints.Problem(StatusCodes.Status403Forbidden, "INVALID_CURRENT_PASSWORD",
        "Passwort nicht korrekt", "Das aktuelle Passwort ist nicht korrekt.");
}
