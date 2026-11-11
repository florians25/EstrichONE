namespace EstrichOne.Api.Modules.Organization.Exports;

/// <summary>Einstellungen der Installation (Einstellungen → System). Fehlende Werte haben sinnvolle Standards.</summary>
public interface ISystemSettings
{
    Task<SystemSettings> GetAsync(CancellationToken cancellationToken);
}

public sealed record SystemSettings
{
    public SessionSettings Session { get; init; } = new();
    public MapSettings Map { get; init; } = new();
    public NumberingSettings Numbering { get; init; } = new();
    public BauakteDefaults Bauakte { get; init; } = new();
    public ApprovalSettings Approvals { get; init; } = new();
    public NotificationSettings Notifications { get; init; } = new();
    public AppearanceSettings Appearance { get; init; } = new();
    public PdfSettings Pdf { get; init; } = new();
    public ModuleSettings Modules { get; init; } = new();
    public MediaSettings Media { get; init; } = new();
    public AppUpdateSettings AppUpdates { get; init; } = new();
    public MonitoringSettings Monitoring { get; init; } = new();

    /// <summary>Werte auf erlaubte Bereiche begrenzen (auch für alte oder von Hand geänderte Daten).</summary>
    public SystemSettings Normalize() => this with
    {
        Session = (Session ?? new()).Normalize(),
        Map = (Map ?? new()).Normalize(),
        Numbering = (Numbering ?? new()).Normalize(),
        Bauakte = (Bauakte ?? new()).Normalize(),
        Approvals = (Approvals ?? new()).Normalize(),
        Notifications = Notifications ?? new(),
        Appearance = (Appearance ?? new()).Normalize(),
        Pdf = (Pdf ?? new()).Normalize(),
        Modules = (Modules ?? new()).Normalize(),
        Media = (Media ?? new()).Normalize(),
        AppUpdates = (AppUpdates ?? new()).Normalize(),
        Monitoring = (Monitoring ?? new()).Normalize(),
    };

    internal static string? Text(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length > max ? value.Trim()[..max] : value.Trim();
}

/// <summary>
/// Web-Sitzung: gleitende Verlängerung bei Aktivität; „angemeldet bleiben“ hält länger.
/// Apps auf registrierten Geräten (iOS/Android): gleitende Gerätesitzung – erst nach <see cref="DeviceIdleDays"/> Tagen
/// ohne Nutzung ist wieder das Passwort nötig (Entsperren dazwischen per Face ID/Touch ID/Gerätecode bzw. Pool-PIN).
/// </summary>
public sealed record SessionSettings
{
    public int IdleHours { get; init; } = 12;
    public int RememberDays { get; init; } = 30;
    public int DeviceIdleDays { get; init; } = 90;

    /// <summary>
    /// Konten mit Verwaltungsrechten (Benutzer, Rollen, Einstellungen, Backup, Geräte, Protokoll, Archiv) erhalten diese
    /// Rechte erst mit eingerichteter Zwei-Faktor-Anmeldung. Anmelden und Einrichten bleibt immer möglich.
    /// </summary>
    public bool RequireTwoFactorForAdmins { get; init; }

    public SessionSettings Normalize() => this with
    {
        IdleHours = Math.Clamp(IdleHours, 1, 168),
        RememberDays = Math.Clamp(RememberDays, 1, 90),
        DeviceIdleDays = Math.Clamp(DeviceIdleDays is 0 ? 90 : DeviceIdleDays, 7, 365),
    };
}

/// <summary>Kartenkacheln. Der Standard ist der OpenStreetMap-Server (Nutzungsbedingungen beachten).</summary>
public sealed partial record MapSettings
{
    public const string DefaultTileUrl = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";
    public const string DefaultAttribution = "© <a href=\"https://www.openstreetmap.org/copyright\">OpenStreetMap</a>-Mitwirkende";

    public string TileUrl { get; init; } = DefaultTileUrl;
    public string Attribution { get; init; } = DefaultAttribution;
    public int MaxZoom { get; init; } = 19;

    public MapSettings Normalize()
    {
        var url = SystemSettings.Text(TileUrl, 300);
        var valid = url is not null && url.StartsWith("https://", StringComparison.Ordinal)
            && url.Contains("{z}", StringComparison.Ordinal) && url.Contains("{x}", StringComparison.Ordinal) && url.Contains("{y}", StringComparison.Ordinal);
        return this with
        {
            TileUrl = valid ? url! : DefaultTileUrl,
            Attribution = SafeAttribution(SystemSettings.Text(Attribution, 300)) ?? DefaultAttribution,
            MaxZoom = Math.Clamp(MaxZoom, 10, 22),
        };
    }

    /// <summary>
    /// Die Quellenangabe erscheint als HTML in der Karte (Leaflet). Erlaubt sind nur Text und Links
    /// <c>&lt;a href="https://…"&gt;…&lt;/a&gt;</c>; alles andere wird maskiert (kein gespeichertes XSS über die Einstellungen).
    /// Idempotent: bereits maskierte Zeichen bleiben unverändert.
    /// </summary>
    internal static string? SafeAttribution(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var builder = new System.Text.StringBuilder();
        var last = 0;
        foreach (System.Text.RegularExpressions.Match match in AttributionLink().Matches(value))
        {
            builder.Append(Escape(value[last..match.Index]));
            builder.Append("<a href=\"").Append(Escape(match.Groups[1].Value)).Append("\">").Append(Escape(match.Groups[2].Value)).Append("</a>");
            last = match.Index + match.Length;
        }

        builder.Append(Escape(value[last..]));
        return builder.ToString();
    }

    private static string Escape(string text) => EntityOrSpecial().Replace(text, m => m.Value switch
    {
        "<" => "&lt;",
        ">" => "&gt;",
        "\"" => "&quot;",
        "'" => "&#39;",
        "&" => "&amp;",
        _ => m.Value, // vorhandene Entität (&copy; &amp; &#169;) unverändert
    });

    [System.Text.RegularExpressions.GeneratedRegex("<a\\s+href=\"(https://[^\"<>\\s]+)\"\\s*>([^<>]*)</a>", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex AttributionLink();

    [System.Text.RegularExpressions.GeneratedRegex("&(#[0-9]{1,7}|#x[0-9a-fA-F]{1,6}|[a-zA-Z][a-zA-Z0-9]{1,31});|[<>\"'&]")]
    private static partial System.Text.RegularExpressions.Regex EntityOrSpecial();
}

/// <summary>
/// Aktualisierung der iOS- und Android-App (Systemeinstellungen › App-Updates, docs/app-updates.md). Die neueste
/// Android-App liefert der Server selbst aus (im Server-Update enthalten); iOS-Geräte aktualisieren über App Store,
/// TestFlight oder die Geräteverwaltung (MDM).
/// </summary>
public sealed record AppUpdateSettings
{
    /// <summary>Hinweis „Update verfügbar“ in den Apps, sobald eine neuere Version bereitsteht.</summary>
    public bool RecommendUpdates { get; init; } = true;

    /// <summary>Apps unter <see cref="MinimumVersion"/> sperren (Update-Bildschirm statt Arbeit mit veralteter App).</summary>
    public bool EnforceMinimum { get; init; }

    /// <summary>Mindestversion bei <see cref="EnforceMinimum"/>; leer = Version dieses Servers.</summary>
    public string? MinimumVersion { get; init; }

    /// <summary>Eigener Hinweis an die Benutzer, z. B. „Bitte bis Freitag aktualisieren“.</summary>
    public string? Message { get; init; }

    /// <summary>iOS: Link zu App Store, TestFlight oder MDM-Portal (https).</summary>
    public string? IosUpdateUrl { get; init; }

    /// <summary>iOS: Version, die über <see cref="IosUpdateUrl"/> bereitsteht; leer = Version dieses Servers.</summary>
    public string? IosLatestVersion { get; init; }

    /// <summary>Android: eigener Download (z. B. MDM) statt des vom Server ausgelieferten APK.</summary>
    public string? AndroidUpdateUrl { get; init; }

    public AppUpdateSettings Normalize() => this with
    {
        MinimumVersion = Version(MinimumVersion),
        Message = SystemSettings.Text(Message, 500),
        IosUpdateUrl = Url(IosUpdateUrl),
        IosLatestVersion = Version(IosLatestVersion),
        AndroidUpdateUrl = Url(AndroidUpdateUrl),
    };

    private static string? Version(string? value) =>
        SystemSettings.Text(value, 50) is { } text && Infrastructure.Hosting.VersionComparer.IsValid(text)
            ? Infrastructure.Hosting.VersionComparer.Normalize(text)
            : null;

    private static string? Url(string? value) =>
        SystemSettings.Text(value, 500) is { } text && Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? text
            : null;
}

/// <summary>Nummernkreis der Bauakten: {JJJJ} Jahr, {JJ} Jahr zweistellig, {N…} laufende Nummer mit Mindeststellen.</summary>
public sealed record NumberingSettings
{
    public const string DefaultPattern = "{JJJJ}-{NNN}";

    /// <summary>Muster der Kundennummern (eigener Nummernkreis, Zähler über alle Jahre): 600001 bis 609999.</summary>
    public const string DefaultCustomerPattern = "60{NNNN}";

    public string BauaktePattern { get; init; } = DefaultPattern;

    public string CustomerPattern { get; init; } = DefaultCustomerPattern;

    public NumberingSettings Normalize()
    {
        var pattern = SystemSettings.Text(BauaktePattern, 40);
        var customer = SystemSettings.Text(CustomerPattern, 30);
        return this with
        {
            BauaktePattern = pattern is not null && pattern.Contains("{N", StringComparison.Ordinal) ? pattern : DefaultPattern,
            CustomerPattern = customer is not null && System.Text.RegularExpressions.Regex.IsMatch(customer, "\\{N+\\}") ? customer : DefaultCustomerPattern,
        };
    }

    /// <summary>Höchster Zählerwert, der in die Stellen des Musters passt (z. B. 9999 bei „60{NNNN}“).</summary>
    public static long Capacity(string pattern)
    {
        var digits = System.Text.RegularExpressions.Regex.Matches(pattern, "\\{(N+)\\}").Select(m => m.Groups[1].Value.Length).DefaultIfEmpty(9).Min();
        return (long)Math.Pow(10, Math.Min(digits, 15)) - 1;
    }

    /// <summary>Nummer nach Muster: {JJJJ} Jahr, {JJ} zweistellig, {N…} laufende Nummer mit Mindeststellen.</summary>
    public static string Format(string pattern, int year, long number)
    {
        var text = pattern.Replace("{JJJJ}", year.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{JJ}", (year % 100).ToString("00", System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        return System.Text.RegularExpressions.Regex.Replace(text, "\\{(N+)\\}",
            match => number.ToString(new string('0', match.Groups[1].Value.Length), System.Globalization.CultureInfo.InvariantCulture));
    }
}

public sealed record BauakteDefaults
{
    /// <summary>Neue Bauakten der anlegenden Person zuweisen.</summary>
    public bool AssignCreator { get; init; } = true;

    /// <summary>Hinweis im Kopf, solange die Erstbegehung fehlt.</summary>
    public bool WarnMissingInspection { get; init; } = true;

    /// <summary>Vorgeschlagene Bauakten-Vorlage im Anlage-Assistenten (Stammdaten).</summary>
    public Guid? DefaultTemplateId { get; init; }

    /// <summary>„Geplant“ automatisch auf „In Ausführung“ setzen – Auslöser siehe <see cref="AutoStartTrigger"/>.</summary>
    public AutoStartTrigger AutoStart { get; init; } = AutoStartTrigger.Both;

    /// <summary>Nach freigegebener Abnahme ohne Vorbehalt und ohne offene Mängel automatisch abschließen.</summary>
    public bool AutoCompleteAfterAcceptance { get; init; } = true;

    /// <summary>Gültigkeit des Freigabelinks für Kunden (Abnahmeprotokoll) in Tagen.</summary>
    public int AcceptanceLinkDays { get; init; } = 14;

    public BauakteDefaults Normalize() => this with { AcceptanceLinkDays = Math.Clamp(AcceptanceLinkDays, 1, 60) };
}

/// <summary>Wann eine geplante Bauakte automatisch „In Ausführung“ wird.</summary>
public enum AutoStartTrigger
{
    /// <summary>Nur von Hand.</summary>
    Manual,

    /// <summary>Am ersten Tag mit einem geplanten Einsatz.</summary>
    FirstAssignmentDay,

    /// <summary>Bei der ersten Aktivität vor Ort (Foto, Bautagebuch, Erstbegehung).</summary>
    FirstSiteActivity,

    /// <summary>Was zuerst eintritt.</summary>
    Both,
}

/// <summary>
/// Speicherschonende Ablage (docs/speicher.md): Fotos als JPEG (mozjpeg, Trellis-Quantisierung) mit begrenzter langer Kante,
/// EXIF-Ausrichtung angewendet, Aufnahmedaten und GPS bleiben erhalten; PDFs verlustfrei verdichtet. Gilt für Web und Apps.
/// </summary>
public sealed record MediaSettings
{
    /// <summary>JPEG-Qualität für Fotos (60–95). 82 ist am Bildschirm und im Ausdruck nicht vom Original zu unterscheiden.</summary>
    public int PhotoQuality { get; init; } = 82;

    /// <summary>Längste Kante gespeicherter Fotos in Pixeln (2048–8192); größere Fotos werden verkleinert.</summary>
    public int PhotoMaxEdge { get; init; } = 4096;

    /// <summary>Fotos unverändert speichern (keine Verkleinerung, keine Neukodierung).</summary>
    public bool KeepOriginalPhotos { get; init; }

    /// <summary>PDFs verlustfrei verdichten (Streams komprimieren, Unbenutztes entfernen; signierte PDFs bleiben unverändert).</summary>
    public bool OptimizePdfs { get; init; } = true;

    public MediaSettings Normalize() => this with { PhotoQuality = Math.Clamp(PhotoQuality, 60, 95), PhotoMaxEdge = Math.Clamp(PhotoMaxEdge, 2048, 8192) };
}

/// <summary>
/// Abschaltbare Module (Einstellungen › Module, Katalog in <see cref="AppModules"/>). Daten bleiben beim Abschalten erhalten;
/// die Endpunkte eines abgeschalteten Moduls antworten mit 409 <c>MODULE_DISABLED</c>, Clients blenden es aus.
/// Fehlende Werte (ältere Einstellungen) gelten als eingeschaltet.
/// </summary>
public sealed record ModuleSettings
{
    /// <summary>Anrufe und Anfragen.</summary>
    public bool Calls { get; init; } = true;

    /// <summary>Disposition und Kolonnenplanung (Cockpit, Kolonnenplanung, Teamplaner, Kalender, Arbeitsplan, Kolonnen).</summary>
    public bool Planning { get; init; } = true;

    /// <summary>Maschinen und Fahrzeuge (Stammdaten der Geräte; setzt die Planung voraus).</summary>
    public bool Machines { get; init; } = true;

    /// <summary>Baustellenkarte.</summary>
    public bool Map { get; init; } = true;

    /// <summary>Material und Lieferanten.</summary>
    public bool Materials { get; init; } = true;

    /// <summary>Bestellungen (setzt Material voraus).</summary>
    public bool Orders { get; init; } = true;

    /// <summary>Personal (Mitarbeiter, Qualifikationen, Fristen).</summary>
    public bool Staff { get; init; } = true;

    /// <summary>Urlaub und Abwesenheit (setzt Personal voraus).</summary>
    public bool Absence { get; init; } = true;

    /// <summary>Erstbegehung und Prüfprotokolle.</summary>
    public bool Inspections { get; init; } = true;

    /// <summary>Bautagebuch.</summary>
    public bool Diary { get; init; } = true;

    /// <summary>Abnahmen (Abnahmeprotokoll, Kundenfreigabe).</summary>
    public bool Acceptance { get; init; } = true;

    /// <summary>Aufgaben.</summary>
    public bool Tasks { get; init; } = true;

    /// <summary>Mängel.</summary>
    public bool Defects { get; init; } = true;

    /// <summary>Dokumentfreigaben.</summary>
    public bool Approvals { get; init; } = true;

    /// <summary>Aktenarchiv (abgeschlossene Bauakten als Archivakte).</summary>
    public bool Dossiers { get; init; } = true;

    /// <summary>Helpdesk (Supporttickets an die IT).</summary>
    public bool Helpdesk { get; init; } = true;

    /// <summary>Feedback (Fehlermeldungen und Funktionswünsche).</summary>
    public bool Feedback { get; init; } = true;

    /// <summary>Eingeschaltet? Kernmodule und unbekannte Schlüssel gelten als eingeschaltet.</summary>
    public bool IsEnabled(string key) => key switch
    {
        AppModules.Calls => Calls,
        AppModules.Planning => Planning,
        AppModules.Machines => Machines,
        AppModules.Map => Map,
        AppModules.Materials => Materials,
        AppModules.Orders => Orders,
        AppModules.Staff => Staff,
        AppModules.Absence => Absence,
        AppModules.Inspections => Inspections,
        AppModules.Diary => Diary,
        AppModules.Acceptance => Acceptance,
        AppModules.Tasks => Tasks,
        AppModules.Defects => Defects,
        AppModules.Approvals => Approvals,
        AppModules.Dossiers => Dossiers,
        AppModules.Helpdesk => Helpdesk,
        AppModules.Feedback => Feedback,
        _ => true,
    };

    /// <summary>Einen Schalter setzen (unbekannte Schlüssel und Kernmodule bleiben unverändert).</summary>
    public ModuleSettings With(string key, bool on) => key switch
    {
        AppModules.Calls => this with { Calls = on },
        AppModules.Planning => this with { Planning = on },
        AppModules.Machines => this with { Machines = on },
        AppModules.Map => this with { Map = on },
        AppModules.Materials => this with { Materials = on },
        AppModules.Orders => this with { Orders = on },
        AppModules.Staff => this with { Staff = on },
        AppModules.Absence => this with { Absence = on },
        AppModules.Inspections => this with { Inspections = on },
        AppModules.Diary => this with { Diary = on },
        AppModules.Acceptance => this with { Acceptance = on },
        AppModules.Tasks => this with { Tasks = on },
        AppModules.Defects => this with { Defects = on },
        AppModules.Approvals => this with { Approvals = on },
        AppModules.Dossiers => this with { Dossiers = on },
        AppModules.Helpdesk => this with { Helpdesk = on },
        AppModules.Feedback => this with { Feedback = on },
        _ => this,
    };

    /// <summary>Abhängigkeiten durchsetzen: Ist eine Voraussetzung aus, ist auch das abhängige Modul aus (z. B. Urlaub ohne Personal).</summary>
    public ModuleSettings Normalize()
    {
        var result = this;
        bool changed;
        do
        {
            changed = false;
            foreach (var module in AppModules.Switchable)
            {
                if (result.IsEnabled(module.Key) && module.Requires.Any(r => !result.IsEnabled(r)))
                {
                    result = result.With(module.Key, false);
                    changed = true;
                }
            }
        }
        while (changed);

        return result;
    }
}

public sealed record ApprovalSettings
{
    /// <summary>Vorgeschlagene Frist in Tagen (0 = keine).</summary>
    public int DefaultDueDays { get; init; } = 5;

    /// <summary>Vorgeschlagene Rolle für Freigaben.</summary>
    public Guid? DefaultRoleId { get; init; }

    /// <summary>Kommentar auch beim Freigeben verlangen.</summary>
    public bool RequireCommentOnApprove { get; init; }

    public ApprovalSettings Normalize() => this with { DefaultDueDays = Math.Clamp(DefaultDueDays, 0, 90) };
}

/// <summary>Benachrichtigungen per E-Mail (wirksam, sobald ein E-Mail-Konto eingerichtet ist).</summary>
public sealed record NotificationSettings
{
    public bool ApprovalRequested { get; init; } = true;
    public bool ApprovalDecided { get; init; } = true;
    public bool HelpdeskUpdates { get; init; } = true;
    public bool NewUserLetterByEmail { get; init; }
}

/// <summary>Überwachung von Archivserver und Backup-Zielen (Einstellungen › Benachrichtigungen).</summary>
public sealed record MonitoringSettings
{
    public const int DefaultOutageGraceMinutes = 3;

    /// <summary>
    /// Ein Ausfall von Archivserver bzw. Backup-Ziel wird erst gemeldet (Benachrichtigung, Banner, Warnstatus), wenn der Server
    /// ununterbrochen mindestens so viele Minuten nicht erreichbar ist (0 = sofort, höchstens 60). Kurze Aussetzer bleiben still.
    /// </summary>
    public int OutageGraceMinutes { get; init; } = DefaultOutageGraceMinutes;

    public MonitoringSettings Normalize() => this with { OutageGraceMinutes = Math.Clamp(OutageGraceMinutes, 0, 60) };
}

public sealed record AppearanceSettings
{
    /// <summary>„system“ = hell/dunkel nach dem Betriebssystem.</summary>
    public string DefaultTheme { get; init; } = "system";
    public string Density { get; init; } = "compact";
    public bool ShowCompanyLogo { get; init; } = true;

    public AppearanceSettings Normalize() => this with
    {
        DefaultTheme = SystemSettings.Text(DefaultTheme, 40) ?? "system",
        Density = Density is "cozy" ? "cozy" : "compact",
    };
}

/// <summary>Gestaltung der PDF-Ausdrucke (Bauakte, Planung, Benutzerbrief).</summary>
public sealed record PdfSettings
{
    public bool ShowCompanyLogo { get; init; } = true;
    public bool ShowEstrichOneLogo { get; init; } = true;
    public string AccentColor { get; init; } = "#1F4E79";
    public string? FooterText { get; init; }
    public int PhotosPerRow { get; init; } = 2;

    public PdfSettings Normalize() => this with
    {
        AccentColor = AccentColor is { Length: 7 } c && c[0] == '#' && c[1..].All(Uri.IsHexDigit) ? c.ToUpperInvariant() : "#1F4E79",
        FooterText = SystemSettings.Text(FooterText, 200),
        PhotosPerRow = Math.Clamp(PhotosPerRow, 1, 3),
    };
}
