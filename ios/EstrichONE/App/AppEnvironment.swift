import EstrichCore
import Foundation

/// Bündelt alles, was sich zwischen Debug-, Release- und UI-Test-Builds unterscheidet.
enum AppEnvironment {
    static var appVersion: SemanticVersion {
        let text = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? ""
        return SemanticVersion(text) ?? SemanticVersion(major: 0, minor: 0, patch: 0)
    }

    static var appVersionText: String {
        let version = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "?"
        let build = Bundle.main.object(forInfoDictionaryKey: "CFBundleVersion") as? String ?? "?"
        return "\(version) (\(build))"
    }

    /// `http://` zu lokalen Testservern gibt es nur in Debug-Builds (10, Abschnitt 4).
    static var allowsInsecureLocalServers: Bool {
        #if DEBUG
        true
        #else
        false
        #endif
    }

    static func makeTransport() -> HTTPTransport {
        #if DEBUG
        if let stub = UITestSupport.transport() {
            return stub
        }
        #endif
        // Ohne Cookies und ohne Zwischenspeicher: Die Prüfung soll immer den aktuellen Serverstand sehen.
        return URLSession(configuration: .ephemeral)
    }

    static func makeServerStore() -> ServerStore {
        #if DEBUG
        if let defaults = UITestSupport.isolatedDefaults() {
            return ServerStore(defaults: defaults)
        }
        #endif
        return ServerStore(defaults: .standard)
    }
}
