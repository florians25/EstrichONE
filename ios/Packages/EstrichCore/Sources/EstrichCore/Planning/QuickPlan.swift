import Foundation

/// Schnelles Einplanen vom Handy (Recht `planning.manage`): Vorgaben und Texte für das kompakte Blatt „Einplanen“ in der
/// Bauakte, in der Baustellenliste und in der Disposition. Gesendet wird als `PlanChange.assign` über dieselben Endpunkte
/// wie „Baustelle zuweisen“.
public enum QuickPlan {
    /// Tätigkeiten zur Auswahl (wie in der Disposition).
    public static let activities = ["Estrich einbringen", "Randdämmung und Folie", "Erstbegehung", "CM-Messung und Belegreife", "Nacharbeiten",
                                    "Schleifen und Grundieren"]

    /// Vorgabe der Tätigkeit nach dem Stand der Bauakte: Anfrage beginnt mit der Erstbegehung, in der Trocknung folgt die
    /// Messung, nach dem Abschluss bleiben Nacharbeiten; sonst wird Estrich eingebracht.
    public static func defaultActivity(for status: BauakteStatus?) -> String {
        switch status {
        case .inquiry?: "Erstbegehung"
        case .drying?: "CM-Messung und Belegreife"
        case .completed?: "Nacharbeiten"
        default: "Estrich einbringen"
        }
    }

    /// Auslastung einer Kolonne am Tag: „frei“, „1 Einsatz“, „2 Einsätze“.
    public static func loadText(_ stops: Int) -> String {
        switch stops {
        case ...0: "frei"
        case 1: "1 Einsatz"
        default: "\(stops) Einsätze"
        }
    }

    /// Beginn nach der letzten Station der Kolonne, sonst 07:00 (`HH:mm:ss`).
    public static func suggestedStart(for crew: DayPlan.Crew?) -> String {
        crew?.stops.compactMap { $0.endTime ?? $0.startTime }.max() ?? "07:00:00"
    }

    /// Vorbelegte Kolonne: die gewünschte (Disposition), sonst die einzige, sonst die eigene. Ohne eindeutige Vorgabe
    /// wählt die Planung selbst.
    public static func preselectedCrew(in plan: DayPlan, wanted: UUID?) -> UUID? {
        if let wanted, plan.crew(wanted) != nil { return wanted }
        if plan.crews.count == 1 { return plan.crews[0].id }
        let mine = plan.crews.filter(\.isMine)
        return mine.count == 1 ? mine[0].id : nil
    }

    /// Einsatz einer Bauakte im Tagesplan (der früheste), mit seiner Kolonne.
    public struct Planned: Equatable, Sendable {
        public let day: String
        public let stop: DayPlan.Stop
        public let crew: DayPlan.Crew?
    }

    public static func planned(_ bauakteId: UUID, in plan: DayPlan) -> Planned? {
        var found: [Planned] = []
        for crew in plan.crews {
            for stop in crew.stops where stop.bauakteId == bauakteId {
                found.append(Planned(day: plan.day, stop: stop, crew: crew))
            }
        }
        for stop in plan.withoutCrew where stop.bauakteId == bauakteId {
            found.append(Planned(day: plan.day, stop: stop, crew: nil))
        }
        return found.min { ($0.stop.startTime ?? "99") < ($1.stop.startTime ?? "99") }
    }

    /// Bestätigung nach dem Einplanen: „Eingeplant: Morgen, 07:00, Kolonne Huber“.
    public static func confirmation(day: String, today: String, start: String?, crewName: String?, calendar: Calendar = .current) -> String {
        "Eingeplant: " + TodayGlance.appointmentText(day: day, start: start.map { String($0.prefix(5)) }, detail: crewName, today: today,
                                                     calendar: calendar)
    }
}

extension DayPlan {
    /// Nur die Kolonnen dieses Plans für einen anderen Tag, ohne Einsätze, Abwesenheiten und Freigabe. Grundlage für das
    /// Einplanen ohne Verbindung, wenn der gewählte Tag noch nie geladen wurde.
    public func crewsOnly(day: String) -> DayPlan {
        DayPlan(day: day, holiday: nil, release: nil, canPlan: canPlan, myCrewIds: myCrewIds,
                crews: crews.map { crew in
                    Crew(id: crew.id, name: crew.name, color: crew.color, leaderName: crew.leaderName,
                         members: crew.members.map { Member(id: $0.id, name: $0.name) }, vehicles: crew.vehicles, rosterChanged: false,
                         rosterNote: nil, isMine: crew.isMine, stops: [], warnings: [])
                },
                withoutCrew: [], absent: [], free: [])
    }
}
