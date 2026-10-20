using EstrichOne.Client;

namespace EstrichOne.Desktop;

/// <summary>
/// Farbsystem der Windows-App (aus dem Logo): Markenblau, Akzent-Grün und Akzent-Hellblau; dazu feste Signalfarben je Status,
/// damit ein Status überall gleich aussieht (Liste, Bauakte, Planung, Berichte). Werte als #RRGGBB.
/// </summary>
public static class Palette
{
    public const string Brand = "#0B2D5B";
    public const string BrandLight = "#174A8B";
    public const string Green = "#3CCB7F";
    public const string Sky = "#8EC5FF";

    public const string Blue = "#2563EB";
    public const string Teal = "#0891B2";
    public const string Violet = "#7C3AED";
    public const string Amber = "#D97706";
    public const string Orange = "#EA580C";
    public const string Red = "#DC2626";
    public const string Pink = "#DB2777";
    public const string Slate = "#64748B";
    public const string Success = "#16A34A";

    public static string Of(BauakteStatus status) => status switch
    {
        BauakteStatus.Inquiry => Violet,
        BauakteStatus.Planned => BrandLight,
        BauakteStatus.InProgress => Amber,
        BauakteStatus.Drying => Teal,
        BauakteStatus.Completed => Success,
        _ => Red,
    };

    public static string Of(OrderStatus status) => status switch
    {
        OrderStatus.Draft => Slate,
        OrderStatus.Ordered => Blue,
        OrderStatus.Confirmed => Teal,
        OrderStatus.Delivered => Success,
        _ => Red,
    };

    public static string Of(TicketStatus status) => status switch
    {
        TicketStatus.New => Blue,
        TicketStatus.InProgress => Amber,
        TicketStatus.WaitingForReporter => Violet,
        TicketStatus.Resolved => Success,
        _ => Slate,
    };

    public static string Of(TicketPriority priority) => priority switch
    {
        TicketPriority.Urgent => Red,
        TicketPriority.High => Orange,
        TicketPriority.Low => Slate,
        _ => Blue,
    };

    public static string Of(DeviceStatus status) => status switch
    {
        DeviceStatus.Pending => Amber,
        DeviceStatus.Active => Success,
        DeviceStatus.Locked => Red,
        _ => Slate,
    };

    public static string Of(RoomStatus status) => status switch
    {
        RoomStatus.Laid => Orange,
        RoomStatus.Ready => Success,
        _ => Blue,
    };

    public static string Of(InquiryStatus status) => status switch
    {
        InquiryStatus.Open => Blue,
        InquiryStatus.InProgress => Amber,
        InquiryStatus.Converted => Success,
        _ => Slate,
    };

    public static string Of(WorkItemKind kind) => kind == WorkItemKind.Defect ? Orange : BrandLight;

    /// <summary>Terminarten der Planung (wie im Web).</summary>
    public static string Of(AssignmentKind kind) => kind switch
    {
        AssignmentKind.Screed => Brand,
        AssignmentKind.InitialInspection => Violet,
        AssignmentKind.SecondInspection => "#9F67FF",
        AssignmentKind.CmMeasurement => Teal,
        AssignmentKind.Acceptance => Success,
        AssignmentKind.Survey => "#CA8A04",
        AssignmentKind.Check => Orange,
        AssignmentKind.CustomerAppointment => Pink,
        AssignmentKind.Delivery => Slate,
        _ => "#94A3B8",
    };

    public static string Of(FileKind kind) => kind switch
    {
        FileKind.Photo => Teal,
        FileKind.Video => Violet,
        _ => BrandLight,
    };

    public static string Of(MaterialCategory category) => category switch
    {
        MaterialCategory.Screed => Brand,
        MaterialCategory.Binder => Blue,
        MaterialCategory.Aggregate => "#CA8A04",
        MaterialCategory.Insulation => Teal,
        MaterialCategory.Membrane => Violet,
        MaterialCategory.EdgeStrip => Pink,
        MaterialCategory.Additive => "#65A30D",
        _ => Slate,
    };

    public static string Of(AbsenceKind kind) => kind switch
    {
        AbsenceKind.Vacation => Teal,
        AbsenceKind.Sick => Red,
        AbsenceKind.Training => Violet,
        _ => Amber,
    };

    /// <summary>Dokumentarten als farbige Ordner.</summary>
    public static string DocumentCategory(string? key) => key switch
    {
        "offer" => Blue,
        "order" => BrandLight,
        "invoice" => Violet,
        "plan" => Teal,
        "delivery" => Orange,
        "acceptance" => Success,
        "customer" => Pink,
        "office" => "#CA8A04",
        _ => Slate,
    };

    /// <summary>Dateityp-Farbe (PDF rot, Office blau/grün, Bilder türkis).</summary>
    public static string ContentType(string contentType) => contentType switch
    {
        "application/pdf" => Red,
        _ when contentType.Contains("wordprocessing", StringComparison.Ordinal) || contentType.Contains("opendocument.text", StringComparison.Ordinal) => Blue,
        _ when contentType.Contains("spreadsheet", StringComparison.Ordinal) => Success,
        _ when contentType.Contains("presentation", StringComparison.Ordinal) => Orange,
        _ when contentType.StartsWith("image/", StringComparison.Ordinal) => Teal,
        _ when contentType.StartsWith("video/", StringComparison.Ordinal) => Violet,
        _ => Slate,
    };

    public static string Audit(string action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (action.Contains("failed", StringComparison.Ordinal) || action.Contains("reused", StringComparison.Ordinal)
            || action.Contains("locked", StringComparison.Ordinal) || action.Contains("deleted", StringComparison.Ordinal))
        {
            return Red;
        }

        return action switch
        {
            _ when action.StartsWith("auth.", StringComparison.Ordinal) => Blue,
            _ when action.StartsWith("bauakte.", StringComparison.Ordinal) => BrandLight,
            _ when action.StartsWith("devices.", StringComparison.Ordinal) => Violet,
            _ when action.StartsWith("users.", StringComparison.Ordinal) || action.StartsWith("access.", StringComparison.Ordinal) => Amber,
            _ => Slate,
        };
    }

    /// <summary>Notizfarben (Schlüssel der API) als Hintergrund.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> NoteColors { get; } =
        [new("yellow", "Gelb"), new("red", "Rot"), new("green", "Grün"), new("blue", "Blau")];

    public static string NoteBackground(string? color, bool dark) => (color, dark) switch
    {
        ("red", false) => "#FFE4E1",
        ("green", false) => "#E3F6E5",
        ("blue", false) => "#E3EEFB",
        ("yellow", false) => "#FFF7D6",
        ("red", true) => "#4A2626",
        ("green", true) => "#1F3A27",
        ("blue", true) => "#1E2F48",
        ("yellow", true) => "#463D1C",
        _ => string.Empty,
    };
}
