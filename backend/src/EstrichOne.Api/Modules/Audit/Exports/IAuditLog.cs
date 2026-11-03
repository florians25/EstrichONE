namespace EstrichOne.Api.Modules.Audit.Exports;

/// <summary>
/// Schreibt nachvollziehbare Ereignisse (wer, was, wann, woran). Inhalte wie Passwörter, Tokens oder Notiztexte
/// gehören nie in <paramref name="details"/>.
/// </summary>
public interface IAuditLog
{
    /// <summary>Wird mit dem nächsten <c>SaveChanges</c> des aktuellen DbContext gespeichert (gleiche Transaktion).</summary>
    void Record(string action, Guid? actorUserId, string? entityType = null, string? entityId = null,
        IReadOnlyDictionary<string, string?>? details = null);
}

/// <summary>Stabile Ereignisnamen.</summary>
public static class AuditActions
{
    public const string LoginSucceeded = "auth.login.succeeded";
    public const string LoginFailed = "auth.login.failed";
    public const string LoginLockedOut = "auth.login.locked_out";
    public const string Logout = "auth.logout";
    public const string RefreshTokenReused = "auth.refresh.reused";
    public const string RefreshTokenRecovered = "auth.refresh.recovered";
    public const string PasswordChanged = "auth.password.changed";
    public const string SessionRevoked = "auth.session.revoked";
    public const string SessionsRevokedAll = "auth.session.revoked_all";
    public const string BruteForceDetected = "auth.login.attack_detected";
    public const string TwoFactorEnabled = "auth.two_factor.enabled";
    public const string TwoFactorDisabled = "auth.two_factor.disabled";
    public const string TwoFactorReset = "auth.two_factor.reset";
    public const string RecoveryCodesRenewed = "auth.two_factor.recovery_codes_renewed";
    public const string RecoveryCodeUsed = "auth.two_factor.recovery_code_used";
}

/// <summary>Ein gespeichertes Ereignis (Lesesicht, z. B. für den Verlauf einer Bauakte).</summary>
public sealed record AuditEntry(DateTimeOffset OccurredAt, string Action, Guid? ActorUserId, IReadOnlyDictionary<string, string?> Details);

public interface IAuditQueries
{
    /// <summary>Neueste zuerst.</summary>
    Task<IReadOnlyList<AuditEntry>> ForEntityAsync(string entityType, string entityId, int limit, CancellationToken cancellationToken);
}
