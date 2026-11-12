import Foundation

/// Update-Ablauf (deploy/README.md, „Update-Ablauf“) – Feld `update` in `GET /system/notices`:
/// `announce` (Countdown, Arbeit speichern, Abgleich), `update` (Sitzungen beendet), `finish` (Freigabe folgt),
/// `rollback`, `failed`. Texte wie im Web (util/updateView.ts) und auf Android (UpdatePresentation).
public struct UpdateMaintenance: Decodable, Sendable, Equatable {
    public let id: String
    public let phase: String
    public let fromVersion: String?
    public let targetVersion: String?
    public let startedAt: String?
    public let phaseStartedAt: String?
    public let phaseEndsAt: String?
    public let message: String?

    public init(id: String, phase: String, fromVersion: String? = nil, targetVersion: String? = nil, startedAt: String? = nil,
                phaseStartedAt: String? = nil, phaseEndsAt: String? = nil, message: String? = nil) {
        self.id = id
        self.phase = phase
        self.fromVersion = fromVersion
        self.targetVersion = targetVersion
        self.startedAt = startedAt
        self.phaseStartedAt = phaseStartedAt
        self.phaseEndsAt = phaseEndsAt
        self.message = message
    }

    /// Nachsichtig: Fehlt etwas, wird das Update ignoriert, statt die ganzen Hinweise zu verwerfen.
    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = (try? c.decodeIfPresent(String.self, forKey: .id)) ?? ""
        phase = (try? c.decodeIfPresent(String.self, forKey: .phase)) ?? ""
        fromVersion = try? c.decodeIfPresent(String.self, forKey: .fromVersion)
        targetVersion = try? c.decodeIfPresent(String.self, forKey: .targetVersion)
        startedAt = try? c.decodeIfPresent(String.self, forKey: .startedAt)
        phaseStartedAt = try? c.decodeIfPresent(String.self, forKey: .phaseStartedAt)
        phaseEndsAt = try? c.decodeIfPresent(String.self, forKey: .phaseEndsAt)
        message = try? c.decodeIfPresent(String.self, forKey: .message)
    }

    private enum CodingKeys: String, CodingKey {
        case id, phase, fromVersion, targetVersion, startedAt, phaseStartedAt, phaseEndsAt, message
    }

    public var phaseStart: Date? { phaseStartedAt.flatMap(APIDate.parse) }
    public var phaseEnd: Date? { phaseEndsAt.flatMap(APIDate.parse) }
    public var isKnownPhase: Bool { !id.isEmpty && UpdatePresentation.phases.contains(phase) }
}

/// Aufbereitet für Leiste und Wartungsbildschirm.
public struct UpdateScreen: Sendable, Equatable {
    public enum StepState: String, Sendable { case done, now, todo }
    public struct Step: Sendable, Equatable, Identifiable {
        public let label: String
        public let state: StepState
        public var id: String { label }
    }

    public let banner: Bool
    public let bannerText: String
    public let page: Bool
    public let title: String
    public let text: String
    public let countdown: String
    public let countdownLabel: String
    /// 0…1 innerhalb der Phase.
    public let progress: Double
    public let steps: [Step]
    public let failed: Bool
    public let version: String
}

public enum UpdatePresentation {
    public static let phases: Set<String> = ["announce", "update", "finish", "rollback", "failed"]

    public static func clock(_ seconds: Double) -> String {
        let s = max(0, Int(seconds.rounded(.up)))
        return "\(s / 60):" + String(format: "%02d", s % 60)
    }

    public static func secondsLeft(_ state: UpdateMaintenance, now: Date) -> Double? {
        state.phaseEnd.map { $0.timeIntervalSince(now) }
    }

    public static func screen(_ state: UpdateMaintenance, now: Date) -> UpdateScreen {
        let target = (state.targetVersion?.isEmpty == false) ? "Version \(state.targetVersion!)" : "die neue Version"
        let left = secondsLeft(state, now: now)
        let running = (left ?? 0) > 0
        var progress: Double = (state.phase == "update" || state.phase == "rollback") ? 1 : 0
        if let start = state.phaseStart, let end = state.phaseEnd, end > start {
            progress = min(1, max(0, now.timeIntervalSince(start) / end.timeIntervalSince(start)))
        }
        let announcing = state.phase == "announce" && (left == nil || left! > 0)
        let third = state.phase == "rollback" ? "Rücknahme" : state.phase == "failed" ? "Fehlgeschlagen" : "Abschluss"
        func steps(_ index: Int) -> [UpdateScreen.Step] {
            ["Ankündigung", "Update", third].enumerated().map { n, label in
                UpdateScreen.Step(label: label, state: n < index ? .done : n == index ? .now : .todo)
            }
        }
        let version = state.targetVersion ?? ""
        let countdown = running ? clock(left!) : ""
        if announcing {
            let text = running
                ? "Update auf \(target) beginnt in \(clock(left!)) – bitte Arbeit speichern. Offene Änderungen werden jetzt übertragen."
                : "Update auf \(target) beginnt gleich – bitte Arbeit speichern."
            return UpdateScreen(banner: true, bannerText: text, page: false, title: "Update angekündigt", text: text, countdown: countdown,
                                countdownLabel: "Beginnt in", progress: progress, steps: steps(0), failed: false, version: version)
        }
        switch state.phase {
        case "announce", "update":
            return UpdateScreen(banner: false, bannerText: "", page: true, title: "Update wird eingespielt",
                                text: "EstrichONE wird auf \(target) aktualisiert. Erfasste Daten bleiben auf dem Gerät und werden danach übertragen.",
                                countdown: countdown, countdownLabel: running ? "Mindestens noch" : "Läuft …", progress: progress,
                                steps: steps(1), failed: false, version: version)
        case "finish":
            return UpdateScreen(banner: false, bannerText: "", page: true, title: "Update wird abgeschlossen",
                                text: "\(target) läuft. Letzte Prüfungen – danach geht es mit einer neuen Anmeldung weiter.",
                                countdown: countdown, countdownLabel: running ? "Freigabe in" : "Gleich geschafft …", progress: progress,
                                steps: steps(2), failed: false, version: version)
        case "rollback":
            return UpdateScreen(banner: false, bannerText: "", page: true, title: "Update wird zurückgenommen",
                                text: "Das Update auf \(target) hat nicht geklappt. Der bisherige Stand wird wiederhergestellt.",
                                countdown: "", countdownLabel: "Läuft …", progress: progress, steps: steps(2), failed: false, version: version)
        default:
            let message = (state.message?.isEmpty == false) ? state.message! : "Das Update auf \(target) konnte nicht eingespielt werden."
            return UpdateScreen(banner: false, bannerText: "", page: true, title: "Update fehlgeschlagen",
                                text: "\(message) Der Administrator ist informiert.", countdown: countdown,
                                countdownLabel: running ? "Freigabe in" : "Gesperrt, bis das Problem behoben ist", progress: progress,
                                steps: steps(2), failed: true, version: version)
        }
    }

    /// Hinweis nach der Freigabe (Anmeldung).
    public static func doneText(version: String?, failed: Bool) -> String {
        if failed { return "Das Update konnte nicht eingespielt werden – EstrichONE läuft mit dem bisherigen Stand weiter. Bitte erneut anmelden." }
        if let version, !version.isEmpty { return "EstrichONE wurde auf Version \(version) aktualisiert. Bitte erneut anmelden." }
        return "EstrichONE wurde aktualisiert. Bitte erneut anmelden."
    }
}
