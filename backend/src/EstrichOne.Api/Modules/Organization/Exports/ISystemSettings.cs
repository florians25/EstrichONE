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
        Modules = Modules ?? new(),
    };

    internal static string? Text(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length > max ? value.Trim()[..max] : value.Trim();
}

/// <summary>Web-Sitzung: gleitende Verlängerung bei Aktivität; „angemeldet bleiben“ hält länger.</summary>
public sealed record SessionSettings
{
    public int IdleHours { get; init; } = 12;
    public int RememberDays { get; init; } = 30;

    public SessionSettings Normalize() => this with { IdleHours = Math.Clamp(IdleHours, 1, 168), RememberDays = Math.Clamp(RememberDays, 1, 90) };
}

/// <summary>Kartenkacheln. Der Standard ist der OpenStreetMap-Server (Nutzungsbedingungen beachten).</summary>
public sealed record MapSettings
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
            Attribution = SystemSettings.Text(Attribution, 300) ?? DefaultAttribution,
            MaxZoom = Math.Clamp(MaxZoom, 10, 22),
        };
    }
}

/// <summary>Nummernkreis der Bauakten: {JJJJ} Jahr, {JJ} Jahr zweistellig, {N…} laufende Nummer mit Mindeststellen.</summary>
public sealed record NumberingSettings
{
    public const string DefaultPattern = "{JJJJ}-{NNN}";

    public string BauaktePattern { get; init; } = DefaultPattern;

    public NumberingSettings Normalize()
    {
        var pattern = SystemSettings.Text(BauaktePattern, 40);
        return this with { BauaktePattern = pattern is not null && pattern.Contains("{N", StringComparison.Ordinal) ? pattern : DefaultPattern };
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

/// <summary>Abschaltbare Module. Daten bleiben beim Abschalten erhalten.</summary>
public sealed record ModuleSettings
{
    public bool Materials { get; init; } = true;
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
