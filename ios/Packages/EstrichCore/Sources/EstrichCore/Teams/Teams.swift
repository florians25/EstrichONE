import Foundation

/// Microsoft Teams (docs/microsoft-teams.md): Wo Unterhaltungen stattfinden und wie die App „In Teams öffnen“ anbietet.
/// Ohne Teams-Anbindung (älterer Server, 404) ist `TeamsStatus` `nil` und alles bleibt wie bisher.
public enum TeamsChatMode: String, Sendable, Equatable, Decodable {
    /// Nur der eingebaute Chat (Standard).
    case estrichOne
    /// Nur Teams: der eingebaute Chat ist ein Archiv (lesen, exportieren).
    case teams
    /// Beides.
    case both

    public init(from decoder: any Decoder) throws {
        let raw = try decoder.singleValueContainer().decode(String.self)
        self = TeamsChatMode(rawValue: raw) ?? .estrichOne
    }
}

/// `GET /api/v1/teams/status`
public struct TeamsStatus: Decodable, Sendable, Equatable {
    public let chatMode: TeamsChatMode
    /// Eingebauter Chat nur noch als Archiv.
    public let chatReadOnly: Bool
    /// „In Teams öffnen“ anbieten (Modus „Teams“ oder „beides“).
    public let available: Bool
    public let siteTeamName: String?
    public let siteTeamUrl: String?
    public let siteTeamAppUrl: String?
    public let teamsWebUrl: String

    public init(chatMode: TeamsChatMode, chatReadOnly: Bool? = nil, available: Bool? = nil, siteTeamName: String? = nil, siteTeamUrl: String? = nil,
                siteTeamAppUrl: String? = nil, teamsWebUrl: String = "https://teams.microsoft.com") {
        self.chatMode = chatMode
        self.chatReadOnly = chatReadOnly ?? (chatMode == .teams)
        self.available = available ?? (chatMode != .estrichOne)
        self.siteTeamName = siteTeamName
        self.siteTeamUrl = siteTeamUrl
        self.siteTeamAppUrl = siteTeamAppUrl
        self.teamsWebUrl = teamsWebUrl
    }

    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        let mode = (try? c.decodeIfPresent(TeamsChatMode.self, forKey: .chatMode)) ?? .estrichOne
        self.init(chatMode: mode, chatReadOnly: try? c.decodeIfPresent(Bool.self, forKey: .chatReadOnly),
                  available: try? c.decodeIfPresent(Bool.self, forKey: .available),
                  siteTeamName: try? c.decodeIfPresent(String.self, forKey: .siteTeamName),
                  siteTeamUrl: try? c.decodeIfPresent(String.self, forKey: .siteTeamUrl),
                  siteTeamAppUrl: try? c.decodeIfPresent(String.self, forKey: .siteTeamAppUrl),
                  teamsWebUrl: (try? c.decodeIfPresent(String.self, forKey: .teamsWebUrl)) ?? "https://teams.microsoft.com")
    }

    private enum CodingKeys: String, CodingKey {
        case chatMode, chatReadOnly, available, siteTeamName, siteTeamUrl, siteTeamAppUrl, teamsWebUrl
    }

    /// Ziel für „Microsoft Teams“ (Team „Baustellen“, sonst Teams allgemein).
    public var homeTargets: TeamsOpenTargets {
        TeamsOpenTargets(app: siteTeamAppUrl, web: siteTeamUrl ?? teamsWebUrl)
    }

    /// Bezeichnung des eingebauten Chats in Navigation und Startseite.
    public var chatTitle: String { chatReadOnly ? "Chat-Archiv" : "Chat" }
}

/// Verknüpfte Teams-Unterhaltung einer Bauakte (Auszug aus `TeamsLinkResponse`).
public struct TeamsLink: Decodable, Sendable, Equatable {
    /// `channel` oder `groupChat`.
    public let kind: String
    public let displayName: String?
    public let webUrl: String
    public let appUrl: String
    public let lastSyncError: String?

    public init(kind: String, displayName: String?, webUrl: String, appUrl: String, lastSyncError: String? = nil) {
        self.kind = kind
        self.displayName = displayName
        self.webUrl = webUrl
        self.appUrl = appUrl
        self.lastSyncError = lastSyncError
    }

    public var targets: TeamsOpenTargets { TeamsOpenTargets(app: appUrl, web: webUrl) }

    /// „Kanal „…““ bzw. „Gruppenchat „…““ für Untertitel und Bedienungshilfen.
    public var summary: String {
        let type = kind == "groupChat" ? "Gruppenchat" : "Kanal"
        guard let displayName, !displayName.isEmpty else { return type }
        return "\(type) „\(displayName)“"
    }
}

/// `GET /api/v1/bauakten/{id}/teams` (Auszug aus `BauakteTeamsResponse`).
public struct BauakteTeamsResponse: Decodable, Sendable, Equatable {
    public let chatMode: TeamsChatMode
    public let link: TeamsLink?
    public let canManage: Bool

    public init(chatMode: TeamsChatMode, link: TeamsLink?, canManage: Bool) {
        self.chatMode = chatMode
        self.link = link
        self.canManage = canManage
    }
}

/// Wohin „In Teams öffnen“ führt: zuerst die Teams-App (`msteams:`), sonst der Browser (nur echte Teams-Adressen).
public struct TeamsOpenTargets: Sendable, Equatable {
    public let app: URL?
    public let web: URL?

    public init(app: String?, web: String?) {
        let appURL = app.flatMap { URL(string: $0) }
        self.app = appURL?.scheme?.lowercased() == "msteams" ? appURL : nil
        self.web = web.flatMap { URL(string: $0) }.flatMap { TeamsOpenTargets.isTeamsWeb($0) }
    }

    /// Nur https-Adressen von Microsoft Teams (keine fremden Links aus der Datenbank öffnen).
    static func isTeamsWeb(_ url: URL) -> URL? {
        guard url.scheme?.lowercased() == "https", let host = url.host?.lowercased(),
              ["teams.microsoft.com", "teams.cloud.microsoft", "teams.live.com"].contains(host) else { return nil }
        return url
    }

    /// Reihenfolge der Versuche.
    public var candidates: [URL] { [app, web].compactMap { $0 } }
}

extension AuthorizedClient {
    /// Chat-Modus und Team „Baustellen“; `nil`, wenn der Server keine Teams-Anbindung kennt oder nicht erreichbar ist.
    public func teamsStatus() async -> TeamsStatus? {
        do throws(APIError) {
            let status: TeamsStatus = try await get("teams/status")
            return status
        } catch {
            return nil
        }
    }

    /// Verknüpfung der Bauakte (nur online).
    public func bauakteTeams(_ bauakteId: UUID) async throws(APIError) -> BauakteTeamsResponse {
        try await get("bauakten/\(bauakteId.uuidString.lowercased())/teams")
    }
}
