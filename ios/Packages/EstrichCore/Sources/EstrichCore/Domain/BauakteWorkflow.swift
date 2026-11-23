import Foundation

/// Ablauf einer Baustelle als Fortschritt (Bauakte, Abschnitt „Ablauf“): Erstbegehung, Estrich einbringen, Belegreife und
/// Abnahme mit Stand erledigt, nicht erforderlich oder offen. Der erste offene Schritt ist der nächste; die App bietet
/// für ihn das passende Erfassen an. Nur aus Daten, die schon auf dem Gerät liegen.
public struct BauakteWorkflow: Equatable, Sendable {
    public enum StepKind: String, Sendable, Equatable {
        case initialInspection, laying, readiness, acceptance

        public var title: String {
            switch self {
            case .initialInspection: "Erstbegehung"
            case .laying: "Estrich einbringen"
            case .readiness: "Belegreife"
            case .acceptance: "Abnahme"
            }
        }
    }

    public enum State: String, Sendable, Equatable {
        /// Erledigt.
        case done
        /// Nicht erforderlich (zählt wie erledigt).
        case skipped
        /// Der nächste offene Schritt.
        case next
        /// Später.
        case open

        public var isFinished: Bool { self == .done || self == .skipped }
    }

    public struct Step: Identifiable, Equatable, Sendable {
        public let kind: StepKind
        public let state: State
        /// „Durchgeführt“, „5 von 11 Räumen eingebracht“, „Noch keine CM-Messung“.
        public let detail: String
        public var id: String { kind.rawValue }
        public var title: String { kind.title }
    }

    /// Was über die Baustelle bekannt ist.
    public struct Input: Sendable {
        public var status: BauakteStatus
        public var initialInspection: InitialInspectionOverview.Status
        /// „Do., 26.11.2026, 08:00“, falls die Erstbegehung geplant ist.
        public var inspectionAppointment: String?
        public var roomCount: Int
        /// Räume eingebracht oder schon belegreif.
        public var roomsLaid: Int
        public var roomsReady: Int
        /// Letzte CM-Messung: bestanden, nicht bestanden oder keine.
        public var lastMeasurementReady: Bool?
        /// „Belegreif (1,6 CM-%)“
        public var readinessText: String?
        public var acceptanceCount: Int
        /// „Ohne Vorbehalt“
        public var acceptanceResult: String?
        /// Module „Prüfungen“ (Erstbegehung, CM-Messung) und „Abnahme“.
        public var showsInspections: Bool
        public var showsAcceptance: Bool

        public init(status: BauakteStatus, initialInspection: InitialInspectionOverview.Status, inspectionAppointment: String? = nil,
                    roomCount: Int = 0, roomsLaid: Int = 0, roomsReady: Int = 0, lastMeasurementReady: Bool? = nil,
                    readinessText: String? = nil, acceptanceCount: Int = 0, acceptanceResult: String? = nil,
                    showsInspections: Bool = true, showsAcceptance: Bool = true) {
            self.status = status
            self.initialInspection = initialInspection
            self.inspectionAppointment = inspectionAppointment
            self.roomCount = roomCount
            self.roomsLaid = roomsLaid
            self.roomsReady = roomsReady
            self.lastMeasurementReady = lastMeasurementReady
            self.readinessText = readinessText
            self.acceptanceCount = acceptanceCount
            self.acceptanceResult = acceptanceResult
            self.showsInspections = showsInspections
            self.showsAcceptance = showsAcceptance
        }
    }

    public let steps: [Step]

    /// Nächster offener Schritt (zum direkten Erfassen).
    public var next: Step? { steps.first { $0.state == .next } }
    public var finishedCount: Int { steps.filter(\.state.isFinished).count }
    /// „2 von 4 erledigt“
    public var progressText: String { "\(finishedCount) von \(steps.count) erledigt" }

    public init(_ input: Input) {
        var list: [(StepKind, Bool, Bool, String)] = []   // Art, erledigt, nicht erforderlich, Text
        let finishedStatus = input.status == .completed
        if input.showsInspections {
            switch input.initialInspection {
            case .done: list.append((.initialInspection, true, false, "Durchgeführt"))
            case .waived: list.append((.initialInspection, true, true, "Nicht erforderlich"))
            case .planned: list.append((.initialInspection, false, false, input.inspectionAppointment.map { "Geplant: \($0)" } ?? "Geplant"))
            case .open: list.append((.initialInspection, false, false, "Noch kein Termin"))
            }
        }
        let laid = input.roomsLaid + input.roomsReady
        let allLaid = input.roomCount > 0 && laid >= input.roomCount
        let layingDone = allLaid || input.status == .drying || finishedStatus
        let layingText: String
        if allLaid {
            layingText = input.roomCount == 1 ? "1 Raum eingebracht" : "Alle \(input.roomCount) Räume eingebracht"
        } else if input.roomCount > 0, laid > 0 {
            layingText = "\(laid) von \(input.roomCount) Räumen eingebracht"
        } else {
            layingText = layingDone ? "Eingebracht" : "Noch nicht eingebracht"
        }
        list.append((.laying, layingDone, false, layingText))
        if input.showsInspections {
            let allReady = input.roomCount > 0 && input.roomsReady >= input.roomCount
            let ready = input.lastMeasurementReady == true || allReady || finishedStatus
            let text = input.readinessText ?? (ready ? "Belegreif" : "Noch keine CM-Messung")
            list.append((.readiness, ready, false, text))
        }
        if input.showsAcceptance {
            let done = input.acceptanceCount > 0 || finishedStatus
            let text = input.acceptanceCount > 0 ? (input.acceptanceResult.map { "Abgenommen, \($0.lowercased())" } ?? "Abgenommen")
                : (done ? "Abgeschlossen" : "Noch keine Abnahme")
            list.append((.acceptance, done, false, text))
        }
        var marked = false
        var result: [Step] = []
        for (kind, done, skipped, text) in list {
            if done {
                result.append(Step(kind: kind, state: skipped ? .skipped : .done, detail: text))
            } else if !marked, input.status != .cancelled {
                marked = true
                result.append(Step(kind: kind, state: .next, detail: text))
            } else {
                result.append(Step(kind: kind, state: .open, detail: text))
            }
        }
        steps = result
    }
}
