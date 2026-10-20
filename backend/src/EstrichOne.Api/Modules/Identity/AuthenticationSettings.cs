namespace EstrichOne.Api.Modules.Identity;

/// <summary>Abschnitt <c>EstrichOne:Authentication</c>. Standardwerte nach docs/reengineering/17.</summary>
public sealed class AuthenticationSettings
{
    public const string SectionName = "EstrichOne:Authentication";

    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Gleitend: jede Verwendung verlängert bis zu dieser Dauer ab jetzt.</summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Spätestens danach ist eine neue Anmeldung nötig, unabhängig von der Nutzung.</summary>
    public TimeSpan SessionMaxLifetime { get; set; } = TimeSpan.FromDays(90);

    /// <summary>Zeitfenster, in dem ein bereits verbrauchter Refresh-Token (parallele Anfragen) nicht als Angriff gilt.</summary>
    public TimeSpan RefreshReuseGracePeriod { get; set; } = TimeSpan.FromSeconds(30);

    public int MaxFailedLoginsPerAccount { get; set; } = 5;
    public int MaxFailedLoginsPerAddress { get; set; } = 20;
    public TimeSpan FailedLoginWindow { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Anfragen an /auth je IP-Adresse und Minute (Bremse vor der Kontosperre). Nur für Testumgebungen erhöhen.</summary>
    public int RequestsPerMinutePerAddress { get; set; } = 20;
}
