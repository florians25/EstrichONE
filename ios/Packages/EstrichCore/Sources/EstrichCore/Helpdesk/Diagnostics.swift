import Foundation

/// Technisches Protokoll für Supportfälle (14, Abschnitt 3). Es nimmt **nur** feste Ereigniscodes und Zahlen an:
/// Codes und Schlüssel sind `StaticString`, also im Programm festgeschrieben. Eine Schnittstelle für Texte zur
/// Laufzeit – Bauakten, Notizen, Namen, Tokens – gibt es nicht. Fehlercodes vom Server werden gegen das Codeformat
/// geprüft. Gehalten werden die letzten 24 Stunden, höchstens 500 Ereignisse, nur im Speicher.
public final class DiagnosticsLog: @unchecked Sendable {
    public struct Event: Equatable, Sendable {
        public let at: Date
        public let code: String
        public let values: [(key: String, value: Int)]

        public static func == (lhs: Event, rhs: Event) -> Bool {
            lhs.at == rhs.at && lhs.code == rhs.code && lhs.values.map(\.key) == rhs.values.map(\.key) && lhs.values.map(\.value) == rhs.values.map(\.value)
        }

        /// „2026-10-19T07:12:03Z SYNC_PUSH ops=3“
        public var line: String {
            ([Self.timestamp(at), code] + values.map { "\($0.key)=\($0.value)" }).joined(separator: " ")
        }

        static func timestamp(_ date: Date) -> String {
            date.formatted(.iso8601.year().month().day().time(includingFractionalSeconds: false).timeZone(separator: .omitted))
        }
    }

    public static let shared = DiagnosticsLog()

    private let lock = NSLock()
    private var events: [Event] = []
    private var errors: [(code: String, at: Date)] = []
    private let now: @Sendable () -> Date

    public init(now: @escaping @Sendable () -> Date = Date.init) {
        self.now = now
    }

    public func record(_ code: StaticString, _ values: KeyValuePairs<StaticString, Int> = [:]) {
        let event = Event(at: now(), code: code.description, values: values.map { ($0.key.description, $0.value) })
        lock.withLock {
            events.append(event)
            trim()
        }
    }

    /// Fehlercode, z. B. vom Server (`SYNC_UPLOAD_FAILED`). Alles, was kein Code ist, wird zu `UNKNOWN_ERROR`.
    public func recordError(code: String) {
        let valid = code.range(of: "^[A-Z][A-Z0-9_]{1,63}$", options: .regularExpression) != nil
        lock.withLock {
            errors.append((valid ? code : "UNKNOWN_ERROR", now()))
            trim()
        }
    }

    public var recentEvents: [Event] { lock.withLock { events } }
    public var recentErrors: [(code: String, at: Date)] { lock.withLock { errors } }

    private func trim() {
        let cutoff = now().addingTimeInterval(-24 * 3600)
        events.removeAll { $0.at < cutoff }
        errors.removeAll { $0.at < cutoff }
        if events.count > 500 { events.removeFirst(events.count - 500) }
        if errors.count > 50 { errors.removeFirst(errors.count - 50) }
    }
}

/// Was beim Melden mitgeschickt wird. Vor dem Senden sieht der Benutzer genau diese Zeilen (`displayLines`).
public struct DiagnosticsReport: Equatable, Sendable {
    public var appVersion: String
    public var build: String
    public var platform: String
    public var osVersion: String
    public var deviceModel: String
    public var deviceKind: DeviceKind?
    /// Nur die ersten 8 Zeichen der internen Geräte-ID.
    public var deviceIdShort: String?
    public var sync: SyncSummary?
    public var network: String?
    public var freeStorageBytes: Int64?
    public var errors: [(code: String, at: Date)]
    public var log: [String]

    public init(appVersion: String, build: String, platform: String, osVersion: String, deviceModel: String, deviceKind: DeviceKind?,
                deviceId: UUID?, sync: SyncSummary?, network: String?, freeStorageBytes: Int64?, log: DiagnosticsLog) {
        self.appVersion = appVersion
        self.build = build
        self.platform = platform
        self.osVersion = osVersion
        self.deviceModel = deviceModel
        self.deviceKind = deviceKind
        deviceIdShort = deviceId.map { String($0.key.prefix(8)) }
        self.sync = sync
        self.network = network
        self.freeStorageBytes = freeStorageBytes
        errors = log.recentErrors
        self.log = log.recentEvents.map(\.line)
    }

    public static func == (lhs: DiagnosticsReport, rhs: DiagnosticsReport) -> Bool { lhs.payload == rhs.payload }

    /// Genau die Felder, die der Server annimmt (`TicketDiagnostics`).
    public var payload: [String: JSONValue] {
        var data: [String: JSONValue] = [
            "appVersion": .string(appVersion), "build": .string(build), "platform": .string(platform), "osVersion": .string(osVersion),
            "deviceModel": .string(deviceModel),
            "errorCodes": .array(errors.map { .object(["code": .string($0.code), "at": .string(DiagnosticsLog.Event.timestamp($0.at))]) }),
            "log": .array(log.map(JSONValue.string)),
        ]
        if let deviceKind { data["deviceKind"] = .string(deviceKind.rawValue) }
        if let deviceIdShort { data["deviceId"] = .string(deviceIdShort) }
        if let network { data["network"] = .string(network) }
        if let freeStorageBytes { data["freeStorageBytes"] = .number(Double(freeStorageBytes)) }
        if let sync {
            data["syncPendingOperations"] = .number(Double(sync.pendingOperations))
            data["syncPendingUploads"] = .number(Double(sync.pendingUploads))
            data["syncFailedUploads"] = .number(Double(sync.failedUploads))
            data["syncConflicts"] = .number(Double(sync.conflicts))
            if let last = sync.lastSuccessfulSync { data["syncLastSuccess"] = .string(DiagnosticsLog.Event.timestamp(last)) }
        }
        return data
    }

    /// Lesbar für „Anzeigen, was gesendet wird“.
    public var displayLines: [(label: String, value: String)] {
        var lines: [(String, String)] = [
            ("App", "EstrichONE \(appVersion) (\(build))"), ("System", "\(platform) \(osVersion) · \(deviceModel)"),
        ]
        if let deviceKind { lines.append(("Gerät", (deviceKind == .pool ? "Poolgerät" : "Persönlich") + (deviceIdShort.map { " · \($0)…" } ?? ""))) }
        if let sync {
            lines.append(("Synchronisation", "ausstehend: \(sync.pendingOperations) Vorgänge, \(sync.pendingUploads) Dateien; Probleme: \(sync.rejectedOperations + sync.failedUploads)"))
        }
        if let network { lines.append(("Netzwerk", network)) }
        if let freeStorageBytes { lines.append(("Speicher frei", ByteCountFormatter.string(fromByteCount: freeStorageBytes, countStyle: .file))) }
        lines.append(("Fehlercodes", errors.isEmpty ? "keine" : errors.map(\.code).joined(separator: ", ")))
        lines.append(("Protokoll", "\(log.count) Einträge (nur Codes und Zeiten)"))
        return lines
    }
}
