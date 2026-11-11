namespace EstrichOne.Api.Modules.AppUpdates;

/// <summary>Abschnitt <c>EstrichOne:AppUpdates</c> (docs/app-updates.md).</summary>
public sealed class AppUpdateOptions
{
    public const string SectionName = "EstrichOne:AppUpdates";

    /// <summary>
    /// Verzeichnis mit dem Android-Paket und <c>android.json</c> (Version, SHA-256, Signatur). Im Docker-Image
    /// <c>/app/app-packages</c>; das Release legt dort das signierte APK derselben Version ab.
    /// </summary>
    public string PackagePath { get; set; } = "/app/app-packages";

    /// <summary>Versionshinweise (<c>v&lt;version&gt;.md</c> aus docs/releases) für „Neu in dieser Version“.</summary>
    public string ReleaseNotesPath { get; set; } = "/app/releases";

    /// <summary>
    /// Download, wenn kein Paket im Image liegt (z. B. eigener Build): Platzhalter <c>{version}</c>. Leer = kein Download.
    /// </summary>
    public string? AndroidFallbackUrl { get; set; } = "https://github.com/florians25/EstrichONE/releases/download/v{version}/EstrichONE-Android-v{version}.apk";

    /// <summary>Nur für Tests: Serverversion statt der Programmversion.</summary>
    public string? ServerVersion { get; set; }
}
