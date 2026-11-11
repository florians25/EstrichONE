import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

// App-Updates (docs/app-updates.md): Der Server nennt neueste und Mindestversion, Bezugsquelle (App Store, TestFlight
// oder MDM) und Neuerungen. iOS installiert nicht selbst – die App weist hin, sperrt unter der Mindestversion und zeigt
// nach dem Update „Neu in dieser Version“.

/// Stand der App gegenüber dem Server.
public enum AppUpdateStatus: String, Codable, Sendable {
    case current
    case recommended
    case required
    case newerThanServer
}

/// Bezugsquelle: `store` (App Store, TestFlight, MDM-Portal; `url` kann fehlen), `link`, `package` (nur Android), `none`.
public struct AppUpdateDownload: Codable, Equatable, Sendable {
    public var kind: String
    public var url: String?

    public init(kind: String, url: String?) {
        self.kind = kind
        self.url = url
    }
}

public struct AppReleaseSection: Codable, Equatable, Sendable, Identifiable {
    public var title: String
    public var items: [String]
    public var id: String { title }

    public init(title: String, items: [String]) {
        self.title = title
        self.items = items
    }
}

public struct AppReleaseNote: Codable, Equatable, Sendable, Identifiable {
    public var version: String
    public var title: String
    public var sections: [AppReleaseSection]
    public var id: String { version }

    public init(version: String, title: String, sections: [AppReleaseSection]) {
        self.version = version
        self.title = title
        self.sections = sections
    }
}

/// Antwort von `GET app-updates/check`.
public struct AppUpdateCheck: Codable, Equatable, Sendable {
    public var platform: String
    public var installedVersion: String
    public var serverVersion: String
    public var latestVersion: String?
    public var minimumVersion: String
    public var status: AppUpdateStatus
    public var message: String?
    public var download: AppUpdateDownload
    public var releaseNotes: [AppReleaseNote]

    public init(platform: String = "ios", installedVersion: String, serverVersion: String, latestVersion: String?, minimumVersion: String,
                status: AppUpdateStatus, message: String? = nil, download: AppUpdateDownload = AppUpdateDownload(kind: "store", url: nil),
                releaseNotes: [AppReleaseNote] = []) {
        self.platform = platform
        self.installedVersion = installedVersion
        self.serverVersion = serverVersion
        self.latestVersion = latestVersion
        self.minimumVersion = minimumVersion
        self.status = status
        self.message = message
        self.download = download
        self.releaseNotes = releaseNotes
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        platform = try c.decodeIfPresent(String.self, forKey: .platform) ?? "ios"
        installedVersion = try c.decode(String.self, forKey: .installedVersion)
        serverVersion = try c.decode(String.self, forKey: .serverVersion)
        latestVersion = try c.decodeIfPresent(String.self, forKey: .latestVersion)
        minimumVersion = try c.decodeIfPresent(String.self, forKey: .minimumVersion) ?? "0.0.0"
        // Unbekannter Stand (neuerer Server): wie „aktuell“ behandeln, nie sperren.
        status = (try? c.decode(AppUpdateStatus.self, forKey: .status)) ?? .current
        message = try c.decodeIfPresent(String.self, forKey: .message)
        download = try c.decodeIfPresent(AppUpdateDownload.self, forKey: .download) ?? AppUpdateDownload(kind: "none", url: nil)
        releaseNotes = try c.decodeIfPresent([AppReleaseNote].self, forKey: .releaseNotes) ?? []
    }

    /// Ziel des Updates (neueste bzw. Mindestversion).
    public var targetVersion: String? { latestVersion ?? (minimumVersion == "0.0.0" ? nil : minimumVersion) }
}

/// Ablehnung durch den Server (426 `CLIENT_UPDATE_REQUIRED`).
public struct AppUpdateRejection: Equatable, Sendable {
    public static let code = "CLIENT_UPDATE_REQUIRED"
    public var minimumVersion: String?
    public var latestVersion: String?
    public var message: String?

    public init(minimumVersion: String?, latestVersion: String?, message: String?) {
        self.minimumVersion = minimumVersion
        self.latestVersion = latestVersion
        self.message = message
    }

    /// Aus der Antwort (Problem-JSON); `nil`, wenn es keine solche Ablehnung ist.
    public init?(status: Int, data: Data) {
        guard status == 426, let problem = try? JSONDecoder().decode(Problem.self, from: data), problem.code == Self.code else { return nil }
        self.init(minimumVersion: problem.minimumVersion, latestVersion: problem.latestVersion, message: problem.detail)
    }

    private struct Problem: Decodable {
        let code: String?
        let detail: String?
        let minimumVersion: String?
        let latestVersion: String?
    }
}

extension APIError {
    /// Die App ist zu alt für diesen Server: Update-Bildschirm statt Fehlermeldung.
    public var isUpdateRequired: Bool { status == 426 && code == AppUpdateRejection.code }
}

/// Versionsvergleich nach Semantic Versioning wie auf dem Server (mit Vorabkennung: 1.1.0-preview.9 < 1.1.0).
public enum AppVersionOrder {
    public static func compare(_ a: String, _ b: String) -> Int {
        let (ca, pa) = split(a)
        let (cb, pb) = split(b)
        for index in 0..<3 where ca[index] != cb[index] {
            return ca[index] < cb[index] ? -1 : 1
        }
        if pa.isEmpty || pb.isEmpty {
            return pa.count == pb.count ? 0 : (pa.isEmpty ? 1 : -1)
        }
        for (x, y) in zip(pa, pb) {
            let result: Int
            switch (Int(x), Int(y)) {
            case let (nx?, ny?): result = nx == ny ? 0 : (nx < ny ? -1 : 1)
            case (.some, nil): result = -1
            case (nil, .some): result = 1
            default: result = x == y ? 0 : (x < y ? -1 : 1)
            }
            if result != 0 { return result }
        }
        return pa.count == pb.count ? 0 : (pa.count < pb.count ? -1 : 1)
    }

    public static func isValid(_ text: String) -> Bool {
        let core = text.trimmingCharacters(in: .whitespaces).drop { $0 == "v" }.prefix { $0 != "-" && $0 != "+" }
        let parts = core.split(separator: ".", omittingEmptySubsequences: false)
        return (1...3).contains(parts.count) && parts.allSatisfy { !$0.isEmpty && $0.allSatisfy { $0.isASCII && $0.isNumber } }
    }

    private static func split(_ value: String) -> ([Int], [String]) {
        let text = String(value.trimmingCharacters(in: .whitespaces).drop { $0 == "v" }.prefix { $0 != "+" })
        let dash = text.firstIndex(of: "-")
        let core = (dash.map { String(text[..<$0]) } ?? text).split(separator: ".").map { Int($0) ?? 0 }
        let numbers = Array((core + [0, 0, 0]).prefix(3))
        let pre = dash.map { text[text.index(after: $0)...].split(separator: ".").map(String.init) } ?? []
        return (numbers, pre)
    }
}

/// Wann die App prüft: beim Start nach 30 Minuten, beim Zurückkehren nach 6 Stunden, von Hand und nach einer
/// Ablehnung durch den Server sofort.
public enum AppUpdateSchedule {
    public enum Trigger: Sendable {
        case launch, resume, manual, rejected
    }

    public static let launchInterval: TimeInterval = 30 * 60
    public static let resumeInterval: TimeInterval = 6 * 3600

    public static func isDue(lastCheck: Date?, now: Date, trigger: Trigger) -> Bool {
        guard let lastCheck, lastCheck <= now else { return true }
        switch trigger {
        case .manual, .rejected: return true
        case .launch: return now.timeIntervalSince(lastCheck) >= launchInterval
        case .resume: return now.timeIntervalSince(lastCheck) >= resumeInterval
        }
    }
}

/// „Neu in dieser Version“: einmal nach einem Update, nicht nach der Erstinstallation.
public enum WhatsNewPolicy {
    /// Version, ab der die Neuerungen zu zeigen sind, oder `nil`.
    public static func since(lastRun: String?, current: String) -> String? {
        guard let lastRun, AppVersionOrder.isValid(lastRun), AppVersionOrder.isValid(current),
              AppVersionOrder.compare(current, lastRun) > 0 else { return nil }
        return lastRun
    }
}

/// Meldet Ablehnungen (426) an die App, egal aus welcher Anfrage sie stammen.
public final class AppUpdateSignal: @unchecked Sendable {
    private let lock = NSLock()
    private var handler: (@Sendable (AppUpdateRejection) -> Void)?

    public init() {}

    public func onRejection(_ handler: @escaping @Sendable (AppUpdateRejection) -> Void) {
        lock.lock()
        self.handler = handler
        lock.unlock()
    }

    public func fire(_ rejection: AppUpdateRejection) {
        lock.lock()
        let handler = self.handler
        lock.unlock()
        handler?(rejection)
    }
}

/// Transport, der jede Anfrage mit der App-Version kennzeichnet (`X-EstrichONE-Client: ios/1.0.5`) und Ablehnungen
/// wegen zu alter App meldet. Der Server prüft damit die Mindestversion und führt die Geräteübersicht.
public struct ClientIdentifyingTransport: HTTPTransport {
    public static let header = "X-EstrichONE-Client"

    public let base: HTTPTransport
    public let identity: String
    public let signal: AppUpdateSignal

    public init(base: HTTPTransport, version: String, signal: AppUpdateSignal) {
        self.base = base
        identity = "ios/\(version)"
        self.signal = signal
    }

    public func send(_ request: URLRequest) async throws -> (Data, HTTPURLResponse) {
        var request = request
        request.setValue(identity, forHTTPHeaderField: Self.header)
        let (data, response) = try await base.send(request)
        if let rejection = AppUpdateRejection(status: response.statusCode, data: data) {
            signal.fire(rejection)
        }
        return (data, response)
    }
}

extension APIClient {
    /// Update-Prüfung (ohne Anmeldung, auch für gesperrte oder zu alte Apps).
    public func checkAppUpdate(version: String) async throws(APIError) -> AppUpdateCheck {
        try await get("app-updates/check", query: [URLQueryItem(name: "platform", value: "ios"), URLQueryItem(name: "version", value: version)])
    }

    /// Neuerungen nach `from` bis einschließlich `to`, neueste zuerst.
    public func appReleaseNotes(from: String?, to: String) async throws(APIError) -> [AppReleaseNote] {
        var query = [URLQueryItem(name: "platform", value: "ios"), URLQueryItem(name: "to", value: to)]
        if let from { query.append(URLQueryItem(name: "from", value: from)) }
        return try await get("app-updates/release-notes", query: query)
    }
}
