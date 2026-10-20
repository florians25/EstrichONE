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

    public static string Of(AssignmentKind kind) => kind switch
    {
        AssignmentKind.Screed => "Estrich / Ausführung",
        AssignmentKind.InitialInspection => "Erstbegehung",
        AssignmentKind.SecondInspection => "Zweitbegehung",
        AssignmentKind.CmMeasurement => "CM-Messung",
        AssignmentKind.Acceptance => "Abnahme",
        AssignmentKind.Survey => "Aufmaß",
        AssignmentKind.Check => "Kontrolle",
        AssignmentKind.CustomerAppointment => "Kundentermin",
        AssignmentKind.Delivery => "Lieferung",
        _ => "Sonstiges",
    };

    public static string Of(InquiryStatus status) => status switch
    {
        InquiryStatus.Open => "Offen",
        InquiryStatus.InProgress => "In Bearbeitung",
        InquiryStatus.Converted => "Bauakte erstellt",
        _ => "Abgebrochen",
    };

    public static string Of(OrderStatus status) => status switch
    {
        OrderStatus.Draft => "Entwurf",
        OrderStatus.Ordered => "Bestellt",
        OrderStatus.Confirmed => "Bestätigt",
        OrderStatus.Delivered => "Geliefert",
        _ => "Storniert",
    };

    public static string Of(FileKind kind) => kind switch
    {
        FileKind.Photo => "Foto",
        FileKind.Video => "Video",
        _ => "Dokument",
    };

    /// <summary>Dokumentarten (Ordner) wie im Web; Schlüssel = Wert der API.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> DocumentCategories { get; } =
    [
        new("offer", "Angebot"), new("order", "Auftrag"), new("invoice", "Rechnung"), new("plan", "Vordruck / Plan"), new("delivery", "Lieferung"),
        new("acceptance", "Abnahme"), new("customer", "Kundendokument"), new("office", "Büro-Ordner"), new("other", "Sonstiges"),
    ];

    public static string DocumentCategory(string? key) =>
        DocumentCategories.FirstOrDefault(c => c.Key == (key ?? "other")).Value ?? "Sonstiges";

    /// <summary>Prüfpunkte der Erstbegehung (Schlüssel der API).</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> InspectionChecks { get; } =
    [
        new("substrate", "Untergrund tragfähig, eben und sauber"), new("edgeInsulation", "Randdämmstreifen vorhanden"),
        new("heatingPressureTest", "Fußbodenheizung abgedrückt"), new("access", "Zufahrt und Pumpenstandort"),
        new("utilities", "Wasser und Strom vorhanden"),
    ];

    public static string Of(CheckResult result) => result switch
    {
        CheckResult.Ok => "In Ordnung",
        CheckResult.NotOk => "Mangel",
        _ => "Entfällt",
    };

    public static IReadOnlyList<KeyValuePair<string, string>> Weathers { get; } =
    [
        new("sunny", "sonnig"), new("cloudy", "bewölkt"), new("rain", "Regen"), new("snow", "Schnee"), new("frost", "Frost"), new("wind", "Wind"),
    ];

    public static string Weather(string? key) => key is null ? string.Empty : Weathers.FirstOrDefault(w => w.Key == key).Value ?? key;

    public static IReadOnlyList<KeyValuePair<string, string>> Binders { get; } =
        [new("cement", "Zementestrich (CT)"), new("calciumSulfate", "Calciumsulfatestrich (CA)")];

    public static string Binder(string? key) => key is null ? "–" : Binders.FirstOrDefault(b => b.Key == key).Value ?? key;

    /// <summary>Ergebnis der Erstbegehung in einem Wort, wie im Protokoll.</summary>
    public static string InspectionResult(Inspection inspection)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        if (inspection.NotRequired)
        {
            return "Nicht notwendig";
        }

        return inspection.Checks.Values.Contains(CheckResult.NotOk) ? "Mit Mängeln – Klärung nötig" : "Bestanden / freigegeben";
    }

    public static string Cm(decimal? value) => value is { } v ? v.ToString("0.0#", German) : "–";

    public static string Of(ContactKind kind) => kind == ContactKind.Company ? "Firma" : "Person";

    public static string Of(ResourceKind kind) => kind switch
    {
        ResourceKind.Vehicle => "Fahrzeug",
        ResourceKind.Pump => "Pumpe",
        _ => "Maschine",
    };

    public static string Of(MaterialCategory category) => category switch
    {
        MaterialCategory.Screed => "Estrichmörtel",
        MaterialCategory.Binder => "Bindemittel",
        MaterialCategory.Aggregate => "Zuschlag (Sand/Kies)",
        MaterialCategory.Insulation => "Dämmung",
        MaterialCategory.Membrane => "Folie / Abdeckung",
        MaterialCategory.EdgeStrip => "Randdämmstreifen",
        MaterialCategory.Additive => "Zusatzmittel / Fasern",
        _ => "Sonstiges",
    };

    /// <summary>Übliche Einheiten im Estrichbau; frei erweiterbar.</summary>
    public static IReadOnlyList<string> Units { get; } = ["t", "m³", "Sack", "Palette", "m²", "lfm", "Rolle", "Stk", "kg", "l", "Eimer"];

    public static IReadOnlyList<string> DeliveryWindows { get; } =
        ["6:00–7:00", "7:00–8:00", "8:00–10:00", "vormittags", "nachmittags", "nach telefonischer Absprache"];

    public static string Of(CallSource source) => source switch
    {
        CallSource.Phone => "Telefon",
        CallSource.Email => "E-Mail",
        CallSource.Website => "Website",
        CallSource.Personal => "Persönlich",
        _ => "Sonstiges",
    };

    public static string Of(CallPriority priority) => priority switch
    {
        CallPriority.Low => "Niedrig",
        CallPriority.High => "Hoch",
        _ => "Normal",
    };

    public static string Of(CallDirection direction) => direction == CallDirection.Outbound ? "Ausgehend" : "Eingehend";

    public static string Of(DeviceStatus status) => status switch
    {
        DeviceStatus.Pending => "Wartet auf Freigabe",
        DeviceStatus.Active => "Aktiv",
        DeviceStatus.Locked => "Gesperrt",
        _ => "Entfernt",
    };

    public static string Of(DeviceKind kind) => kind == DeviceKind.Pool ? "Poolgerät" : "Persönlich";

    public static string Of(DevicePlatform platform) => platform switch
    {
        DevicePlatform.Ios => "iOS",
        DevicePlatform.Android => "Android",
        DevicePlatform.Windows => "Windows",
        _ => "Sonstiges",
    };

    public static string Of(WorkItemStatus status) => status switch
    {
        WorkItemStatus.Open => "Offen",
        WorkItemStatus.InProgress => "In Arbeit",
        _ => "Erledigt",
    };

    /// <summary>Gruppen des Protokolls (Präfix der Aktion).</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> AuditGroups { get; } =
    [
        new("auth.", "Anmeldung und Sitzungen"), new("bauakte.", "Bauakten"), new("users.", "Benutzer"), new("access.", "Rollen und Rechte"),
        new("devices.", "Geräte"), new("backup.", "Sicherung"), new("setup.", "Einrichtung"),
    ];

    private static readonly Dictionary<string, string> AuditActions = new()
    {
        ["auth.login.succeeded"] = "Anmeldung", ["auth.login.failed"] = "Anmeldung fehlgeschlagen", ["auth.login.locked_out"] = "Konto vorübergehend gesperrt",
        ["auth.logout"] = "Abmeldung", ["auth.refresh.reused"] = "Wiederverwendetes Sitzungs-Token (Sitzung beendet)",
        ["auth.password.changed"] = "Passwort geändert", ["auth.session.revoked"] = "Sitzung beendet",
        ["users.created"] = "Benutzer angelegt", ["users.updated"] = "Benutzer geändert", ["users.password_reset"] = "Passwort zurückgesetzt",
        ["users.activated"] = "Benutzer entsperrt", ["users.deactivated"] = "Benutzer gesperrt", ["access.user_roles.changed"] = "Rollen geändert",
        ["devices.registered"] = "Gerät registriert", ["devices.attached"] = "Gerät verknüpft", ["devices.enrollment_code_created"] = "Einrichtungscode erzeugt",
        ["devices.enrollment_code_rejected"] = "Einrichtungscode abgelehnt", ["devices.approved"] = "Gerät freigegeben", ["devices.locked"] = "Gerät gesperrt",
        ["devices.unlocked"] = "Gerät entsperrt", ["devices.retired"] = "Gerät entfernt",
        ["backup.run_requested"] = "Sicherung angefordert", ["setup.completed"] = "Ersteinrichtung abgeschlossen",
        ["bauakte.created"] = "Bauakte angelegt", ["bauakte.updated"] = "Bauakte geändert", ["bauakte.deleted"] = "Bauakte gelöscht",
    };

    public static string AuditAction(string action) => AuditActions.GetValueOrDefault(action ?? string.Empty, action ?? string.Empty);

    public static string Money(decimal? value) => value is { } v ? v.ToString("C", German) : "–";

    public static string Quantity(decimal value) => value.ToString("0.###", German);

    public static string Time(TimeOnly? value) => value is { } t ? t.ToString("HH:mm", German) : string.Empty;

    public static string Area(decimal squareMeters) => squareMeters.ToString("N2", German) + " m²";

    public static string DateTime(DateTimeOffset? value) =>
        value is { } v ? v.ToLocalTime().ToString("dd.MM.yyyy, HH:mm", German) : "–";

    public static string Date(DateOnly? value) => value is { } v ? v.ToString("dd.MM.yyyy", German) : "–";

    public static string ShortDay(DateOnly value) => value.ToString("ddd, dd.MM.", German);

    public static string LongDay(DateOnly value) => value.ToString("dddd, dd. MMMM yyyy", German);

    public static string Month(DateOnly value) => value.ToString("MMMM yyyy", German);

    public static string Number(decimal value, int decimals = 2) => value.ToString("N" + decimals.ToString(CultureInfo.InvariantCulture), German);

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
