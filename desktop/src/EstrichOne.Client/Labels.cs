using System.Globalization;

namespace EstrichOne.Client;

/// <summary>Deutsche Bezeichnungen und Formate – gleiche Texte wie Web und Apps.</summary>
public static class Labels
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public static string Of(BauakteStatus status) => status switch
    {
        BauakteStatus.Inquiry => "Anfrage",
        BauakteStatus.Planned => "Geplant",
        BauakteStatus.InProgress => "In Ausführung",
        BauakteStatus.Drying => "Belegreif / Trocknung",
        BauakteStatus.Completed => "Abgeschlossen",
        BauakteStatus.Cancelled => "Storniert",
        _ => status.ToString(),
    };

    public static string Of(TicketStatus status) => status switch
    {
        TicketStatus.New => "Neu",
        TicketStatus.InProgress => "In Bearbeitung",
        TicketStatus.WaitingForReporter => "Rückfrage",
        TicketStatus.Resolved => "Gelöst",
        TicketStatus.Closed => "Geschlossen",
        _ => status.ToString(),
    };

    public static string Of(TicketCategory category) => category switch
    {
        TicketCategory.SignIn => "Anmeldung",
        TicketCategory.Sync => "Synchronisation",
        TicketCategory.AppError => "App-Fehler",
        TicketCategory.DeviceDefect => "Gerät defekt",
        TicketCategory.OfficeIt => "Drucker/Büro-IT",
        TicketCategory.Access => "Zugang/Rechte",
        _ => "Sonstiges",
    };

    public static string Of(TicketPriority priority) => priority switch
    {
        TicketPriority.Low => "Niedrig",
        TicketPriority.High => "Hoch",
        TicketPriority.Urgent => "Dringend",
        _ => "Normal",
    };

    public static string Of(WorkItemKind kind) => kind == WorkItemKind.Defect ? "Mangel" : "Aufgabe";

    public static string Of(RoomStatus status) => status switch
    {
        RoomStatus.Laid => "Eingebracht",
        RoomStatus.Ready => "Belegreif",
        _ => "Geplant",
    };

    public static string Of(AbsenceKind kind) => kind switch
    {
        AbsenceKind.Vacation => "Urlaub",
        AbsenceKind.Sick => "Krank",
        AbsenceKind.Training => "Schulung",
        _ => "Abwesend",
    };

    public static string Area(decimal squareMeters) => squareMeters.ToString("N2", German) + " m²";

    public static string DateTime(DateTimeOffset? value) =>
        value is { } v ? v.ToLocalTime().ToString("dd.MM.yyyy, HH:mm", German) : "–";

    public static string Date(DateOnly? value) => value is { } v ? v.ToString("dd.MM.yyyy", German) : "–";

    public static string Weekday(DateOnly value) => value.ToString("dddd, dd.MM.", German);

    public static string Address(string? street, string? postalCode, string? city)
    {
        var place = string.Join(' ', new[] { postalCode, city }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var parts = new[] { street, place }.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        return parts.Count == 0 ? "–" : string.Join(", ", parts);
    }

    /// <summary>ISO-Kalenderwoche.</summary>
    public static int Week(DateOnly day) => ISOWeek.GetWeekOfYear(day.ToDateTime(TimeOnly.MinValue));
}
