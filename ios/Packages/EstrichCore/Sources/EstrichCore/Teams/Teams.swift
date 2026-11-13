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
    /// Hinweis „Ab sofort chatten wir in Teams“ (nur wenn eingeschaltet).
    public let rolloutNotice: String?
    /// „Chat in Teams“ mit Kollegen anbieten (Teams im Einsatz, Recht chat.use).
    public let canStartChats: Bool

    public init(chatMode: TeamsChatMode, chatReadOnly: Bool? = nil, available: Bool? = nil, siteTeamName: String? = nil, siteTeamUrl: String? = nil,
                siteTeamAppUrl: String? = nil, teamsWebUrl: String = "https://teams.microsoft.com", rolloutNotice: String? = nil,
                canStartChats: Bool = false) {
        self.chatMode = chatMode
        self.chatReadOnly = chatReadOnly ?? (chatMode == .teams)
        self.available = available ?? (chatMode != .estrichOne)
        self.siteTeamName = siteTeamName
        self.siteTeamUrl = siteTeamUrl
        self.siteTeamAppUrl = siteTeamAppUrl
        self.teamsWebUrl = teamsWebUrl
        self.rolloutNotice = rolloutNotice
        self.canStartChats = canStartChats
    }

    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        let mode = (try? c.decodeIfPresent(TeamsChatMode.self, forKey: .chatMode)) ?? .estrichOne
        self.init(chatMode: mode, chatReadOnly: try? c.decodeIfPresent(Bool.self, forKey: .chatReadOnly),
                  available: try? c.decodeIfPresent(Bool.self, forKey: .available),
                  siteTeamName: try? c.decodeIfPresent(String.self, forKey: .siteTeamName),
                  siteTeamUrl: try? c.decodeIfPresent(String.self, forKey: .siteTeamUrl),
                  siteTeamAppUrl: try? c.decodeIfPresent(String.self, forKey: .siteTeamAppUrl),
                  teamsWebUrl: (try? c.decodeIfPresent(String.self, forKey: .teamsWebUrl)) ?? "https://teams.microsoft.com",
                  rolloutNotice: try? c.decodeIfPresent(String.self, forKey: .rolloutNotice),
                  canStartChats: (try? c.decodeIfPresent(Bool.self, forKey: .canStartChats)) ?? false)
    }

    private enum CodingKeys: String, CodingKey {
        case chatMode, chatReadOnly, available, siteTeamName, siteTeamUrl, siteTeamAppUrl, teamsWebUrl, rolloutNotice, canStartChats
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
    /// Gruppenchat mit dem ganzen Bauakten-Team (ohne die eigene Person); `nil` ohne Microsoft-Konten.
    public let teamChat: TeamsChatLink?

    public init(chatMode: TeamsChatMode, link: TeamsLink?, canManage: Bool, teamChat: TeamsChatLink? = nil) {
        self.chatMode = chatMode
        self.link = link
        self.canManage = canManage
        self.teamChat = teamChat
    }
}

/// Link zu einem Einzel- bzw. Gruppenchat in Teams (`TeamsChatLinkResponse`).
public struct TeamsChatLink: Decodable, Sendable, Equatable {
    public let webUrl: String
    public let appUrl: String
    public let included: [String]

    public init(webUrl: String, appUrl: String, included: [String] = []) {
        self.webUrl = webUrl
        self.appUrl = appUrl
        self.included = included
    }

    public var targets: TeamsOpenTargets { TeamsOpenTargets(app: appUrl, web: webUrl) }
}

/// Teams-Chat einer eigenen Kolonne (`GET /api/v1/teams/crews/mine`).
public struct TeamsCrewChat: Decodable, Sendable, Equatable, Identifiable {
    public let crewId: UUID
    public let crewName: String
    public let displayName: String?
    public let webUrl: String
    public let appUrl: String

    public init(crewId: UUID, crewName: String, displayName: String?, webUrl: String, appUrl: String) {
        self.crewId = crewId
        self.crewName = crewName
        self.displayName = displayName
        self.webUrl = webUrl
        self.appUrl = appUrl
    }

    public var id: UUID { crewId }
    public var title: String { displayName ?? (crewName.lowercased().hasPrefix("kolonne") ? crewName : "Kolonne \(crewName)") }
    public var targets: TeamsOpenTargets { TeamsOpenTargets(app: appUrl, web: webUrl) }
}

/// Kollege für „Chat in Teams“ (`GET /api/v1/teams/people`).
public struct TeamsPerson: Decodable, Sendable, Equatable, Identifiable {
    public let userId: UUID
    public let name: String
    /// Microsoft-Anmeldename; `nil` = kein Microsoft-Konto (dann keine Aktion anbieten).
    public let upn: String?

    public init(userId: UUID, name: String, upn: String?) {
        self.userId = userId
        self.name = name
        self.upn = upn
    }

    public var id: UUID { userId }
}

/// Deep Links für Chats in Teams (wie Server und Web): `https://teams.microsoft.com/l/chat/0/0?users=a@x,b@x&topicName=…&message=…`,
/// in der Teams-App `msteams:/l/chat/0/0?…`. Ein Empfänger = Einzelchat, mehrere = Gruppenchat (nur dann mit Thema).
public enum TeamsChatLinks {
    /// Nur ASCII-Buchstaben, Ziffern und „-._~“ unkodiert (wie encodeURIComponent bzw. Uri.EscapeDataString).
    private static let component = CharacterSet(charactersIn: "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~")

    static func encode(_ text: String) -> String {
        text.addingPercentEncoding(withAllowedCharacters: component) ?? text
    }

    public static func webURL(upns: [String], topic: String? = nil, message: String? = nil) -> String {
        let users = upns.map { encode($0.trimmingCharacters(in: .whitespaces)).replacingOccurrences(of: "%40", with: "@") }.joined(separator: ",")
        var url = "https://teams.microsoft.com/l/chat/0/0?users=\(users)"
        if let topic = topic?.trimmingCharacters(in: .whitespacesAndNewlines), !topic.isEmpty, upns.count > 1 {
            url += "&topicName=" + encode(String(topic.prefix(250)))
        }
        if let message = message?.trimmingCharacters(in: .whitespacesAndNewlines), !message.isEmpty {
            url += "&message=" + encode(String(message.prefix(1000)))
        }
        return url
    }

    /// `https://teams.microsoft.com/l/…` → `msteams:/l/…`
    public static func appURL(fromWeb web: String) -> String? {
        guard let url = URL(string: web), url.scheme == "https", let components = URLComponents(url: url, resolvingAgainstBaseURL: false),
              components.percentEncodedPath.count > 1 else { return nil }
        return "msteams:" + components.percentEncodedPath + (components.percentEncodedQuery.map { "?" + $0 } ?? "")
    }

    /// Ziele (Teams-App zuerst, dann Browser) für einen Chat mit diesen Personen; leer, wenn niemand ein Microsoft-Konto hat.
    public static func targets(for people: [TeamsPerson], topic: String? = nil) -> TeamsOpenTargets? {
        let upns = people.compactMap(\.upn).filter { !$0.isEmpty }
        guard !upns.isEmpty else { return nil }
        let web = webURL(upns: upns, topic: topic)
        return TeamsOpenTargets(app: appURL(fromWeb: web), web: web)
    }

    /// App Store: Microsoft Teams (wenn die App fehlt).
    public static let appStoreURL = URL(string: "https://apps.apple.com/app/microsoft-teams/id1113153706")!
}

/// Wohin „In Teams öffnen“ führt – immer in die Teams-App: zuerst der https-Link als Universal Link (nur App, nie Safari),
/// dann `msteams:` (gleicher Pfad wie `teams.microsoft.com/l/…`). Nur echte Teams-Adressen.
public struct TeamsOpenTargets: Sendable, Equatable {
    public let app: URL?
    public let web: URL?

    public init(app: String?, web: String?) {
        let webURL = web.flatMap { URL(string: $0) }.flatMap { TeamsOpenTargets.isTeamsWeb($0) }
        // Fehlt `msteams:` (z. B. ältere Server), aus dem https-Link bilden.
        let appURL = ((app?.isEmpty == false ? app : nil) ?? webURL.flatMap { TeamsChatLinks.appURL(fromWeb: $0.absoluteString) }).flatMap { URL(string: $0) }
        self.app = appURL?.scheme?.lowercased() == "msteams" ? appURL : nil
        self.web = webURL
    }

    /// Nur https-Adressen von Microsoft Teams (keine fremden Links aus der Datenbank öffnen).
    static func isTeamsWeb(_ url: URL) -> URL? {
        guard url.scheme?.lowercased() == "https", let host = url.host?.lowercased(),
              ["teams.microsoft.com", "teams.cloud.microsoft", "teams.live.com"].contains(host) else { return nil }
        return url
    }

    /// Reihenfolge der Versuche: Universal Link (Teams-App), dann `msteams:`.
    public var candidates: [URL] { [web, app].compactMap { $0 } }
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

    /// Microsoft-Konten der Kollegen für „Chat in Teams“ (leer ohne Teams-Anbindung bzw. Recht).
    public func teamsPeople() async -> [TeamsPerson] {
        do throws(APIError) {
            let people: [TeamsPerson] = try await get("teams/people")
            return people
        } catch {
            return []
        }
    }

    /// Teams-Chats der eigenen Kolonnen (leer ohne Teams bzw. ältere Server).
    public func myTeamsCrewChats() async -> [TeamsCrewChat] {
        do throws(APIError) {
            let chats: [TeamsCrewChat] = try await get("teams/crews/mine")
            return chats
        } catch {
            return []
        }
    }

    /// Verknüpfung der Bauakte (nur online).
    public func bauakteTeams(_ bauakteId: UUID) async throws(APIError) -> BauakteTeamsResponse {
        try await get("bauakten/\(bauakteId.uuidString.lowercased())/teams")
    }
}
