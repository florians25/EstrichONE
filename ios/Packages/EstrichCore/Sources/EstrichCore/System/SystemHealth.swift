import Foundation

/// Zustand von Backup- und Archivserver für alle Clients (`GET /system/health-banner`, docs/backup.md).
/// Daraus entstehen die Hinweise oben in der App: rot bei Ausfall des Backup-Ziels, gelb beim Archivserver –
/// jeweils nur, wenn das Ziel eingerichtet ist.
public struct SystemHealth: Decodable, Sendable, Equatable {
    public struct Target: Decodable, Sendable, Equatable {
        public let configured: Bool
        public let reachable: Bool
        public let lastSuccessAt: Date?
        /// Bewertung des Servers (`ok`, `warning`, `critical` …); fehlt sie, bewertet die App selbst.
        public let severity: String?
        /// Fertiger Hinweistext des Servers (hat Vorrang vor dem Standardtext).
        public let message: String?

        public init(configured: Bool, reachable: Bool, lastSuccessAt: Date?, severity: String? = nil, message: String? = nil) {
            self.configured = configured
            self.reachable = reachable
            self.lastSuccessAt = lastSuccessAt
            self.severity = severity
            self.message = message
        }

        public init(from decoder: any Decoder) throws {
            let c = try decoder.container(keyedBy: CodingKeys.self)
            configured = try c.decodeIfPresent(Bool.self, forKey: .configured) ?? false
            reachable = try c.decodeIfPresent(Bool.self, forKey: .reachable) ?? true
            lastSuccessAt = (try? c.decodeIfPresent(String.self, forKey: .lastSuccessAt))?.flatMap(APIDate.parse)
            severity = try? c.decodeIfPresent(String.self, forKey: .severity)?.lowercased()
            message = (try? c.decodeIfPresent(String.self, forKey: .message)) ?? (try? c.decodeIfPresent(String.self, forKey: .text))
        }

        private enum CodingKeys: String, CodingKey {
            case configured, reachable, lastSuccessAt, severity, message, text
        }

        /// Gestört: eingerichtet und (laut Server) nicht in Ordnung, nicht erreichbar oder letzte Sicherung zu alt.
        func isFailing(at now: Date, maxAge: TimeInterval) -> Bool {
            guard configured else { return false }
            if let severity {
                return !["ok", "none", "info", "healthy", "green"].contains(severity)
            }
            if !reachable { return true }
            guard let last = lastSuccessAt else { return true }
            return now.timeIntervalSince(last) > maxAge
        }
    }

    public let backupTarget: Target?
    public let archive: Target?
    /// Fertige Hinweise des Servers (Reihenfolge = Wichtigkeit, Text unverändert anzeigen). `nil` bei älteren Servern.
    public let serverBanners: [HealthBanner]?

    public init(backupTarget: Target?, archive: Target?, serverBanners: [HealthBanner]? = nil) {
        self.backupTarget = backupTarget
        self.archive = archive
        self.serverBanners = serverBanners
    }

    private struct BannerDTO: Decodable {
        let key: String?
        let severity: String?
        let text: String?
    }

    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        backupTarget = try c.decodeIfPresent(Target.self, forKey: .backupTarget) ?? c.decodeIfPresent(Target.self, forKey: .backup)
        archive = try c.decodeIfPresent(Target.self, forKey: .archive) ?? c.decodeIfPresent(Target.self, forKey: .archiveTarget)
        serverBanners = (try? c.decodeIfPresent([BannerDTO].self, forKey: .banners))?.compactMap { item in
            guard let text = item.text, !text.isEmpty else { return nil }
            let critical = ["critical", "error", "danger", "red"].contains(item.severity?.lowercased() ?? "")
            return HealthBanner(level: critical ? .critical : .warning, text: text, key: item.key)
        }
    }

    private enum CodingKeys: String, CodingKey {
        case backupTarget, backup, archive, archiveTarget, banners
    }

    /// Letztes Backup älter als 26 Stunden gilt als Ausfall (mindestens ein Voll-Backup täglich).
    public static let maxBackupAge: TimeInterval = 26 * 3600

    /// Hinweise, die jetzt gelten: zuerst rot (Backup), dann gelb (Archiv).
    public func banners(at now: Date = Date(), timeZone: TimeZone = .current) -> [HealthBanner] {
        // Der Server entscheidet (docs/backup.md): genau seine Hinweise zeigen, leer = keiner.
        if let serverBanners { return serverBanners }
        var result: [HealthBanner] = []
        if let backupTarget, backupTarget.isFailing(at: now, maxAge: Self.maxBackupAge) {
            let when = Self.format(backupTarget.lastSuccessAt, timeZone: timeZone)
            result.append(HealthBanner(level: .critical, text: backupTarget.message
                ?? "ACHTUNG: Backup-Server ausgefallen oder nicht erreichbar (Letztes Backup \(when)). Bitte geben Sie umgehend Ihrem Admin Bescheid."))
        }
        if let archive, archive.isFailing(at: now, maxAge: 8 * 24 * 3600) {
            let when = Self.format(archive.lastSuccessAt, timeZone: timeZone)
            result.append(HealthBanner(level: .warning, text: archive.message
                ?? "Hinweis: Archiv-Server ausgefallen oder nicht erreichbar (Letzte Archivierung \(when)). Bitte informieren Sie Ihren Admin."))
        }
        return result
    }

    /// `27.10.2026 03:15 Uhr` (wie der Server) bzw. `unbekannt`.
    static func format(_ date: Date?, timeZone: TimeZone) -> String {
        guard let date else { return "unbekannt" }
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "de_DE")
        formatter.timeZone = timeZone
        formatter.dateFormat = "dd.MM.yyyy HH:mm 'Uhr'"
        return formatter.string(from: date)
    }
}

/// Hinweis oben in der App (rot: Backup, gelb: Archiv).
public struct HealthBanner: Sendable, Equatable, Identifiable {
    public enum Level: Sendable, Equatable {
        case critical
        case warning
    }

    public let level: Level
    public let text: String
    /// `backup` oder `archive`.
    public let key: String

    public var id: String { key }

    public init(level: Level, text: String, key: String? = nil) {
        self.level = level
        self.text = text
        self.key = key ?? (level == .critical ? "backup" : "archive")
    }
}

extension AuthorizedClient {
    /// Zustand von Backup- und Archivserver; `nil`, wenn der Server den Endpunkt (noch) nicht kennt.
    public func systemHealth() async throws(APIError) -> SystemHealth? {
        do throws(APIError) {
            let health: SystemHealth = try await get("system/health-banner")
            return health
        } catch {
            if error.status == 404 { return nil }
            throw error
        }
    }
}
