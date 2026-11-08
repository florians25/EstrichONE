import EstrichCore
import Foundation

/// Störungen im Demomodus vorführen (Menü „Demo“): Backup- und Archivserver ausgefallen, Wartung, Server nicht
/// erreichbar. Der Demoserver im App-Prozess antwortet dann wie der echte Server in dieser Lage.
final class DemoSwitches: @unchecked Sendable {
    static let shared = DemoSwitches()

    private let lock = NSLock()
    private var _backupDown = false
    private var _archiveDown = false
    private var _maintenance = false
    private var _outage = false

    var backupDown: Bool {
        get { lock.withLock { _backupDown } }
        set { lock.withLock { _backupDown = newValue } }
    }

    var archiveDown: Bool {
        get { lock.withLock { _archiveDown } }
        set { lock.withLock { _archiveDown = newValue } }
    }

    var maintenance: Bool {
        get { lock.withLock { _maintenance } }
        set { lock.withLock { _maintenance = newValue } }
    }

    var outage: Bool {
        get { lock.withLock { _outage } }
        set { lock.withLock { _outage = newValue } }
    }

    func reset() {
        lock.withLock {
            _backupDown = false
            _archiveDown = false
            _maintenance = false
            _outage = false
        }
    }

    /// Ende der vorgeführten Wartung: 20 Minuten nach dem Einschalten.
    private(set) var maintenanceEndsAt = Date()

    func startMaintenance() {
        maintenanceEndsAt = Date().addingTimeInterval(20 * 60)
        maintenance = true
    }

    /// `GET /system/health-banner` im Demomodus (Aufbau wie docs/backup.md).
    func health() -> [String: Any] {
        let now = Date()
        let lastBackup = now.addingTimeInterval(backupDown ? -31 * 3600 : -5 * 3600)
        let lastArchive = now.addingTimeInterval(archiveDown ? -9 * 86400 : -86400)
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "de_DE")
        formatter.dateFormat = "dd.MM.yyyy HH:mm 'Uhr'"
        var banners: [[String: Any]] = []
        if backupDown {
            banners.append(["key": "backup", "severity": "critical",
                            "text": "ACHTUNG: Backup-Server ausgefallen oder nicht erreichbar (Letztes Backup \(formatter.string(from: lastBackup))). Bitte geben Sie umgehend Ihrem Admin Bescheid."])
        }
        if archiveDown {
            banners.append(["key": "archive", "severity": "warning",
                            "text": "Hinweis: Archiv-Server ausgefallen oder nicht erreichbar (Letzte Archivierung \(formatter.string(from: lastArchive))). Bitte informieren Sie Ihren Admin."])
        }
        return [
            "backup": ["configured": true, "reachable": !backupDown, "lastSuccessAt": APIDate.format(lastBackup), "severity": backupDown ? "critical" : "ok"],
            "archive": ["configured": true, "reachable": !archiveDown, "lastSuccessAt": APIDate.format(lastArchive), "severity": archiveDown ? "warning" : "ok"],
            "banners": banners,
            "checkedAt": APIDate.format(now),
        ]
    }
}

/// Chat im Demomodus mit allen Funktionen: Baustellengruppe mit Fotos, Dokument, Standort, Zitat, Reaktionen und
/// angehefteter Nachricht, Kolonnengruppe mit Gruppenbild, Ankündigungsgruppe („nur Admins schreiben“), Einzelchats.
/// Eigene Nachrichten gelten nach kurzer Zeit als gelesen; das Büro antwortet einmal. Kein Echtzeit-Kanal – die App
/// fragt im Demomodus regelmäßig nach (wie ohne Server-Push). Aufruf nur unter der Sperre des FakeServers.
final class DemoChat {
    private struct Conversation {
        let id: String
        var title: String
        let kind: String
        var description: String?
        var members: [String]
        var admins: [String]
        var unread: Int
        var mutedUntil: Date?
        var onlyAdminsWrite = false
        var bauakteId: String?
        var bauakteTitle: String?
        var crewId: String?
        var crewName: String?
        var avatarFileId: String?
        var mentioned = false
    }

    private struct Message {
        let id: String
        let conversationId: String
        let senderId: String
        let senderName: String
        var text: String
        let createdAt: Date
        var changedAt: Date
        var readAt: Date?
        var attachments: [[String: Any]] = []
        var replyTo: [String: Any]?
        var reactions: [String: String] = [:]
        var editedAt: Date?
        var pinned = false
        var mentions: [String] = []
        var location: [String: Any]?
        var forwarded = false
        var deleted = false
        var system = false
        var clientMessageId: String?
    }

    static let siteChat = "0193a1b2-de00-7000-8000-00000000d005"
    static let crewChat = "0193a1b2-de00-7000-8000-00000000d002"
    static let allStaff = "0193a1b2-de00-7000-8000-00000000d006"
    static let office = "0193a1b2-de00-7000-8000-00000000d001"
    static let colleague = "0193a1b2-de00-7000-8000-00000000d003"
    /// Foto „Behandlungsraum 1, verlegt“ der Beispieldaten (Praxis Dr. Maier).
    static let samplePhoto = "0193a1b2-de00-7000-8000-00000000a007"
    private static let officeUser = "0193a1b2-de00-7000-8000-00000000a0a2"
    private static let lukas = "0193a1b2-0000-7000-8000-00000000bbbb"
    private static let thomas = "0193a1b2-de00-7000-8000-00000000a0a3"
    private static let anna = "0193a1b2-de00-7000-8000-00000000a0a4"
    private static let jonas = "0193a1b2-de00-7000-8000-00000000a0a5"
    private static let crewHuber = "0193a1b2-de00-7000-8000-0000000e0001"
    private static let crewBrandl = "0193a1b2-de00-7000-8000-0000000e0002"

    private let userId: String
    private let names: [String: String]
    private var conversations: [Conversation]
    private var messages: [Message]
    private var replied = false
    /// Angekündigte und hochgeladene Anhänge (nur im Arbeitsspeicher).
    private var files: [String: [String: Any]] = [:]
    private var contents: [String: Data] = [:]
    private var demoContent: DemoContent?
    private var imageStamp = Date().addingTimeInterval(-86400)

    init(userId: String) {
        self.userId = userId
        let nameMap = [userId: "Max Huber", Self.officeUser: "Sabine Wagner", Self.lukas: "Lukas Brandl", Self.thomas: "Thomas Gruber",
                       Self.anna: "Anna Keller", Self.jonas: "Jonas Wimmer"]
        names = nameMap
        let now = Date()
        func at(_ minutes: Double) -> Date { now.addingTimeInterval(-minutes * 60) }
        conversations = [
            Conversation(id: Self.siteChat, title: "Praxis Dr. Maier, Sanierung", kind: "site",
                         description: "Baustellengruppe zur Bauakte 2026-038 mit Team und Bauleitung.",
                         members: [userId, Self.officeUser, Self.lukas, Self.anna], admins: [Self.anna, Self.officeUser], unread: 2,
                         bauakteId: DemoContent.praxis, bauakteTitle: "Praxis Dr. Maier, Sanierung", mentioned: true),
            Conversation(id: Self.crewChat, title: "Kolonne Huber", kind: "crew", description: "Alles rund um Einsätze, Material und Fahrzeuge der Kolonne.",
                         members: [userId, Self.lukas, Self.jonas, Self.officeUser], admins: [Self.officeUser, userId], unread: 1,
                         crewId: Self.crewHuber, crewName: "Kolonne Huber", avatarFileId: "0193a1b2-de00-7000-8000-00000000a005"),
            Conversation(id: Self.allStaff, title: "Alle Mitarbeiter", kind: "announcement", description: "Ankündigungen der Geschäftsleitung. Nur Admins schreiben.",
                         members: [userId, Self.officeUser, Self.lukas, Self.thomas, Self.anna, Self.jonas], admins: [Self.officeUser], unread: 0,
                         onlyAdminsWrite: true),
            Conversation(id: Self.office, title: "Sabine Wagner (Büro)", kind: "direct", members: [userId, Self.officeUser], admins: [], unread: 1),
            Conversation(id: Self.colleague, title: "Lukas Brandl", kind: "direct", members: [userId, Self.lukas], admins: [], unread: 0,
                         mutedUntil: now.addingTimeInterval(8 * 3600)),
        ]
        func message(_ id: String, _ conversation: String, _ sender: String, _ text: String, _ minutes: Double, read: Bool = true) -> Message {
            Message(id: "0193a1b2-de00-7000-8000-0000000\(id)", conversationId: conversation, senderId: sender, senderName: nameMap[sender] ?? "",
                    text: text, createdAt: at(minutes), changedAt: at(minutes), readAt: read ? at(minutes - 1) : nil)
        }
        let pdf = ["id": "0193a1b2-de00-7000-8000-00000000f001", "kind": "document", "fileName": "Lieferschein 4711.pdf",
                   "contentType": "application/pdf", "sizeBytes": 184_320, "hasThumbnail": false] as [String: Any]
        func photo(_ id: String) -> [String: Any] {
            ["id": id, "kind": "photo", "fileName": "Foto.jpg", "contentType": "image/jpeg", "sizeBytes": 412_000, "width": 1600, "height": 1200,
             "hasThumbnail": true]
        }
        var site = [
            message("0e101", Self.siteChat, Self.anna, "Guten Morgen zusammen! Schlüssel liegt ab 6:45 beim Empfang, Zufahrt über den Hof.", 60 * 20),
            message("0e102", Self.siteChat, Self.officeUser, "Lieferschein vom Fließestrich für heute.", 60 * 19.5),
            message("0e103", Self.siteChat, userId, "Behandlungsraum 1 ist verlegt.", 95),
            message("0e104", Self.siteChat, Self.anna, "Sieht sauber aus 👍 Bitte Raum 2 genauso, Randstreifen erst nach dem Abziehen kürzen.", 80),
            message("0e105", Self.siteChat, Self.lukas, "Ich stehe mit dem Silo hier:", 40),
            message("0e106", Self.siteChat, Self.anna, "@Max Huber kannst du heute noch die CM-Messung in Raum 2 eintragen?", 12, read: false),
        ]
        site[1].attachments = [pdf]
        site[0].pinned = true
        site[2].attachments = [photo(Self.samplePhoto), photo("0193a1b2-de00-7000-8000-00000000a003")]
        site[2].reactions = [Self.anna: "👍", Self.officeUser: "👍"]
        site[3].replyTo = ["id": site[2].id, "senderUserId": userId, "senderName": "Max Huber", "text": "Behandlungsraum 1 ist verlegt.",
                           "deleted": false, "attachment": "photo"]
        site[3].editedAt = at(78)
        site[4].location = ["latitude": 48.3705, "longitude": 10.8978, "label": "Silo am Hof", "address": "Altstadt 12, Augsburg"]
        site[5].mentions = [userId]
        var crew = [
            message("0e201", Self.crewChat, Self.lukas, "Pumpe 2 ist getankt und steht am Lager.", 240),
            message("0e202", Self.crewChat, userId, "Super, ich hole sie um 6:45 ab.", 236),
            message("0e203", Self.crewChat, Self.officeUser, "Morgen bitte zwei Sack Randdämmstreifen extra mitnehmen.", 32, read: false),
        ]
        crew[1].reactions = [Self.lukas: "🙏"]
        var announcement = [
            message("0e301", Self.allStaff, Self.officeUser, "Betriebsversammlung am Freitag um 14 Uhr in der Halle. Danach gibt es Leberkäs 🙂", 60 * 26),
        ]
        announcement[0].reactions = [Self.lukas: "👍", Self.jonas: "😂", userId: "👍"]
        messages = site + crew + announcement + [
            message("0e001", Self.office, userId, "Guten Morgen, wir sind um 7:20 an der Praxis Dr. Maier.", 95),
            message("0e002", Self.office, Self.officeUser, "Danke! Der Hausmeister ist ab 7 Uhr da.", 88),
            message("0e003", Self.office, Self.officeUser, "Kurze Info: Die Rechnung für den Kran ist da, liegt im Büro.", 14, read: false),
            message("0e007", Self.colleague, Self.lukas, "Hast du den Laser noch im Wagen?", 60 * 26),
            message("0e008", Self.colleague, userId, "Ja, bringe ich morgen mit.", 60 * 25.5),
        ]
    }

    // MARK: Anfragen

    /// `/api/v1/chat/…` – liefert Status und JSON (oder Rohdaten für Inhalte von Anhängen).
    func handle(method: String, path: String, query: [URLQueryItem], body: [String: Any], raw: Data?, demo: DemoContent?) -> (Int, Any?, Data?) {
        settle()
        demoContent = demo
        let parts = path.split(separator: "/").map(String.init)
        func value(_ name: String) -> String? { query.first { $0.name == name }?.value }
        switch (method, parts.first ?? "", parts.count) {
        case ("GET", "status", 1): return (200, status(), nil)
        case ("GET", "contacts", 1): return (200, contacts(), nil)
        case ("GET", "crews", 1): return (200, [["id": Self.crewHuber, "name": "Kolonne Huber"], ["id": Self.crewBrandl, "name": "Kolonne Brandl"]], nil)
        case ("GET", "search", 1):
            let items = search(value("q") ?? "", in: value("conversationId"))
            return (200, ["items": items, "searched": messages.count, "truncated": false], nil)
        case ("POST", "bauakten", 3) where parts[2] == "group": return bauakteGroup(parts[1].lowercased())
        case ("POST", "attachments", 3) where parts[2] == "bauakte":
            return (200, ["fileId": UUID.v7().uuidString.lowercased(), "bauakteId": orNull(body["bauakteId"])], nil)
        case ("POST", "privacy-notice", 1): return (204, nil, nil)
        case ("GET", "events", 1): return (404, problem("NOT_FOUND"), nil)
        case ("GET", "conversations", 1): return (200, list(), nil)
        case ("POST", "conversations", 1): return create(body)
        case ("POST", "conversations", 2) where parts[1] == "direct": return direct(with: body["userId"] as? String ?? "")
        case (_, "conversations", 2): return conversation(parts[1].lowercased(), method: method, body: body)
        case (_, "conversations", 3): return conversationAction(parts[1].lowercased(), parts[2], method: method, body: body, query: query, raw: raw)
        case ("GET", "attachments", 3) where parts[2] == "content":
            let id = parts[1].lowercased()
            guard let data = demo?.content(of: id) ?? contents[id] else { return (404, problem("NOT_FOUND"), nil) }
            return (200, nil, data)
        case (_, "messages", 2), (_, "messages", 3): return messageAction(parts[1].lowercased(), parts.count == 3 ? parts[2] : nil, method: method, body: body)
        default: return (404, problem("NOT_FOUND"), nil)
        }
    }

    private func orNull(_ value: Any?) -> Any { value ?? NSNull() }

    private func problem(_ code: String, _ detail: String = "Nicht gefunden") -> [String: Any] {
        ["status": 404, "title": "Fehler", "detail": detail, "code": code]
    }

    private var mine: [Conversation] { conversations.filter { $0.members.contains(userId) } }

    var unreadTotal: Int { mine.reduce(0) { $0 + $1.unread } }

    func status() -> [String: Any] {
        ["enabled": true, "canUse": true, "canAdmin": true, "unreadCount": unreadTotal, "unreadConversations": mine.filter { $0.unread > 0 }.count,
         "retentionDays": 90, "showPreview": true, "editWindowMinutes": 15, "privacyNoticeVersion": 1]
    }

    private func last(_ conversation: Conversation) -> Message? {
        messages.filter { $0.conversationId == conversation.id }.max { $0.createdAt < $1.createdAt }
    }

    func list() -> [String: Any] {
        let ordered = mine.sorted { (last($0)?.createdAt ?? .distantPast) > (last($1)?.createdAt ?? .distantPast) }
        return ["items": ordered.map(summary)]
    }

    private func summary(_ conversation: Conversation) -> [String: Any] {
        // Form wie ChatConversationSummary (docs/chat.md): kind nur direct|group, Art über crewId/bauakteId/announcementOnly.
        var item: [String: Any] = [
            "id": conversation.id, "title": conversation.title, "kind": conversation.kind == "direct" ? "direct" : "group",
            "unreadCount": conversation.unread, "unreadMentions": conversation.mentioned ? min(1, conversation.unread) : 0,
            "canPost": canWrite(conversation), "isAdmin": conversation.admins.contains(userId), "announcementOnly": conversation.onlyAdminsWrite,
            "members": conversation.members.map { ["id": $0, "name": names[$0] ?? ""] }, "description": orNull(conversation.description),
            "crewId": orNull(conversation.crewId), "bauakteId": orNull(conversation.bauakteId),
            "muted": (conversation.mutedUntil ?? .distantPast) > Date(), "lastActivityAt": APIDate.format(last(conversation)?.createdAt ?? Date()),
        ]
        if let message = last(conversation) { item["lastMessage"] = json(message) }
        if let until = conversation.mutedUntil, until > Date() { item["mutedUntil"] = APIDate.format(until) }
        if conversation.avatarFileId != nil { item["imageUpdatedAt"] = APIDate.format(imageStamp) }
        return item
    }

    private func canWrite(_ conversation: Conversation) -> Bool {
        !conversation.onlyAdminsWrite || conversation.admins.contains(userId)
    }

    /// Form wie ChatConversationDetail: `{ conversation, members, canManage, pinned, bauakteTitle }`.
    private func detail(_ conversation: Conversation) -> [String: Any] {
        let lastRead: [String: Date] = [Self.anna: Date(), Self.officeUser: Date(), Self.lukas: Date().addingTimeInterval(-3 * 3600),
                                        Self.jonas: Date().addingTimeInterval(-90 * 60), Self.thomas: Date().addingTimeInterval(-86400)]
        let members = conversation.members.map { id -> [String: Any] in
            ["userId": id, "name": names[id] ?? "", "isAdmin": conversation.admins.contains(id),
             "fromCrew": (conversation.kind == "crew" || conversation.kind == "site") && !conversation.admins.contains(id),
             "readUpTo": orNull(lastRead[id].map(APIDate.format)), "deliveredUpTo": APIDate.format(Date())]
        }
        let pinned = messages.filter { $0.conversationId == conversation.id && $0.pinned && !$0.deleted }.map(json)
        return ["conversation": summary(conversation), "members": members, "canManage": conversation.kind != "direct",
                "pinned": pinned, "bauakteTitle": orNull(conversation.bauakteTitle)]
    }

    private func conversation(_ id: String, method: String, body: [String: Any]) -> (Int, Any?, Data?) {
        guard let index = conversations.firstIndex(where: { $0.id == id && $0.members.contains(userId) }) else { return (404, problem("NOT_FOUND"), nil) }
        if method == "PUT" {
            var conversation = conversations[index]
            if let title = body["title"] as? String, !title.isEmpty, title != conversation.title {
                conversation.title = title
                system(in: id, "Max Huber hat den Gruppennamen in „\(title)“ geändert.")
            }
            if let description = body["description"] as? String { conversation.description = description.isEmpty ? nil : description }
            if let members = body["memberIds"] as? [String] {
                let set = Set(members.map { $0.lowercased() } + [userId])
                for added in set.subtracting(conversation.members) { system(in: id, "Max Huber hat \(names[added] ?? "eine Person") hinzugefügt.") }
                for removed in Set(conversation.members).subtracting(set) { system(in: id, "Max Huber hat \(names[removed] ?? "eine Person") entfernt.") }
                conversation.members = conversation.members.filter { set.contains($0) } + set.subtracting(conversation.members).sorted()
            }
            if let admins = body["adminIds"] as? [String] { conversation.admins = admins.map { $0.lowercased() } }
            if let only = body["announcementOnly"] as? Bool { conversation.onlyAdminsWrite = only }
            if let avatar = body["avatarFileId"] as? String { conversation.avatarFileId = avatar.lowercased() }
            conversations[index] = conversation
        }
        return (200, detail(conversations[index]), nil)
    }

    private func conversationAction(_ id: String, _ action: String, method: String, body: [String: Any], query: [URLQueryItem], raw: Data?) -> (Int, Any?, Data?) {
        guard let index = conversations.firstIndex(where: { $0.id == id && $0.members.contains(userId) }) else { return (404, problem("NOT_FOUND"), nil) }
        switch (method, action) {
        case ("POST", "messages"): return send(body, to: index)
        case ("GET", "messages"):
            func date(_ name: String) -> Date? { query.first { $0.name == name }?.value.flatMap(APIDate.parse) }
            let limit = query.first { $0.name == "limit" }?.value.flatMap(Int.init) ?? 50
            var list = messages.filter { $0.conversationId == id }.sorted { $0.createdAt < $1.createdAt }
            if let after = date("after") { list = list.filter { $0.createdAt > after || $0.changedAt > after } }
            if let before = date("before") { list = list.filter { $0.createdAt < before } }
            let hasMore = list.count > limit
            return (200, ["items": list.suffix(limit).map(json), "hasMore": hasMore, "members": detail(conversations[index])["members"] ?? []], nil)
        case ("POST", "attachments"):
            let id = UUID.v7().uuidString.lowercased()
            let name = query.first { $0.name == "fileName" }?.value ?? "Datei"
            let isPDF = raw?.starts(with: Data("%PDF".utf8)) == true || name.lowercased().hasSuffix(".pdf")
            let file: [String: Any] = ["id": id, "kind": isPDF ? "document" : "photo", "fileName": name,
                                       "contentType": isPDF ? "application/pdf" : "image/jpeg", "sizeBytes": raw?.count ?? 0,
                                       "hasThumbnail": !isPDF, "createdAt": APIDate.format(Date()), "uploadedByUserId": userId]
            files[id] = file
            contents[id] = raw ?? Data()
            return (201, file, nil)
        case ("PUT", "image"):
            conversations[index].avatarFileId = "upload-\(id)"
            contents["image-\(id)"] = raw ?? Data()
            imageStamp = Date()
            return (200, summary(conversations[index]), nil)
        case ("DELETE", "image"):
            conversations[index].avatarFileId = nil
            return (204, nil, nil)
        case ("GET", "image"):
            guard let avatar = conversations[index].avatarFileId else { return (404, problem("NOT_FOUND"), nil) }
            return (200, nil, contents["image-\(id)"] ?? demoContent?.content(of: avatar))
        case ("POST", "read"):
            conversations[index].unread = 0
            conversations[index].mentioned = false
            return (204, nil, nil)
        case ("POST", "mute"):
            if body["muted"] as? Bool == true {
                conversations[index].mutedUntil = (body["until"] as? String).flatMap(APIDate.parse) ?? .distantFuture
            } else {
                conversations[index].mutedUntil = nil
            }
            return (200, summary(conversations[index]), nil)
        case ("POST", "leave"):
            conversations[index].members.removeAll { $0 == userId }
            system(in: id, "Max Huber hat die Gruppe verlassen.")
            return (204, nil, nil)
        case ("POST", "typing"): return (204, nil, nil)
        case ("GET", "attachments"):
            let items = messages.filter { $0.conversationId == id && !$0.deleted }.sorted { $0.createdAt > $1.createdAt }.flatMap { message in
                message.attachments.map { attachment -> [String: Any] in
                    var item = attachment
                    item["messageId"] = message.id
                    item["createdAt"] = APIDate.format(message.createdAt)
                    item["uploadedByUserId"] = message.senderId
                    return item
                }
            }
            return (200, items, nil)
        default: return (404, problem("NOT_FOUND"), nil)
        }
    }

    private func send(_ body: [String: Any], to index: Int) -> (Int, Any?, Data?) {
        let conversation = conversations[index]
        guard canWrite(conversation) else { return (403, ["status": 403, "code": "PERMISSION_DENIED", "detail": "In dieser Gruppe schreiben nur Admins."], nil) }
        let text = (body["text"] as? String ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        let fileIds = (body["attachmentFileIds"] as? [String] ?? body["attachmentIds"] as? [String] ?? []).map { $0.lowercased() }
        let location = body["location"] as? [String: Any]
        guard !text.isEmpty || !fileIds.isEmpty || location != nil else {
            return (422, ["status": 422, "code": "VALIDATION_FAILED", "detail": "Bitte einen Text eingeben oder etwas anhängen."], nil)
        }
        if let client = body["clientMessageId"] as? String, let existing = messages.first(where: { $0.clientMessageId == client }) {
            return (200, json(existing), nil)
        }
        let now = Date()
        var message = Message(id: UUID.v7().uuidString.lowercased(), conversationId: conversation.id, senderId: userId, senderName: "Max Huber",
                              text: String(text.prefix(ChatLimits.maxLength)), createdAt: now, changedAt: now)
        message.clientMessageId = body["clientMessageId"] as? String
        message.attachments = fileIds.compactMap { id in
            files[id]
        }
        if let replyId = (body["replyToId"] as? String)?.lowercased(), let quoted = messages.first(where: { $0.id == replyId }) {
            message.replyTo = ["id": quoted.id, "senderUserId": quoted.senderId, "senderName": quoted.senderName, "text": quoted.text,
                               "deleted": quoted.deleted, "attachment": orNull(quoted.attachments.first?["kind"])]
        }
        message.mentions = (body["mentionUserIds"] as? [String] ?? body["mentionIds"] as? [String] ?? []).map { $0.lowercased() }
        message.location = location
        messages.append(message)
        return (201, json(message), nil)
    }

    private func messageAction(_ id: String, _ action: String?, method: String, body: [String: Any]) -> (Int, Any?, Data?) {
        guard let index = messages.firstIndex(where: { $0.id == id }) else { return (404, problem("NOT_FOUND"), nil) }
        let now = Date()
        switch (method, action) {
        case ("PUT", nil):
            guard messages[index].senderId == userId else { return (403, ["status": 403, "code": "PERMISSION_DENIED", "detail": "Nur eigene Nachrichten."], nil) }
            guard now.timeIntervalSince(messages[index].createdAt) <= 15 * 60 else {
                return (409, ["status": 409, "code": "EDIT_WINDOW_EXPIRED", "detail": "Bearbeiten ist nur 15 Minuten nach dem Senden möglich."], nil)
            }
            messages[index].text = (body["text"] as? String ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
            messages[index].editedAt = now
        case ("DELETE", nil):
            messages[index].deleted = true
            messages[index].text = ""
            messages[index].attachments = []
            messages[index].location = nil
            messages[index].pinned = false
            messages[index].changedAt = now
            return (204, nil, nil)
        case ("PUT", "reaction"):
            if let emoji = body["emoji"] as? String, !emoji.isEmpty { messages[index].reactions[userId] = emoji } else { messages[index].reactions[userId] = nil }
        case ("GET", nil):
            return (200, json(messages[index]), nil)
        case ("POST", "pin"):
            messages[index].pinned = body["pinned"] as? Bool ?? true
            messages[index].changedAt = now
            return (204, nil, nil)
        case ("POST", "forward"):
            for target in (body["conversationIds"] as? [String] ?? []).map({ $0.lowercased() }) {
                guard let conversation = conversations.first(where: { $0.id == target }), canWrite(conversation) else { continue }
                var copy = Message(id: UUID.v7().uuidString.lowercased(), conversationId: target, senderId: userId, senderName: "Max Huber",
                                   text: messages[index].text, createdAt: now, changedAt: now)
                copy.attachments = messages[index].attachments
                copy.location = messages[index].location
                copy.forwarded = true
                messages.append(copy)
            }
            return (200, ["forwarded": true], nil)
        default: return (404, problem("NOT_FOUND"), nil)
        }
        messages[index].changedAt = now
        return (200, json(messages[index]), nil)
    }

    private func search(_ term: String, in conversationId: String?) -> [[String: Any]] {
        let needle = term.trimmingCharacters(in: .whitespaces).lowercased()
        guard needle.count >= 2 else { return [] }
        let allowed = Set(mine.map(\.id))
        return messages.filter { message in
            allowed.contains(message.conversationId) && !message.deleted && !message.system
                && (conversationId == nil || message.conversationId == conversationId?.lowercased())
                && (message.text.lowercased().contains(needle)
                    || message.attachments.contains { ($0["fileName"] as? String)?.lowercased().contains(needle) == true })
        }
        .sorted { $0.createdAt > $1.createdAt }
        .map { message in
            ["messageId": message.id, "conversationId": message.conversationId, "senderName": message.senderName,
             "snippet": message.text.isEmpty ? (message.attachments.first?["fileName"] as? String ?? "") : message.text,
             "createdAt": APIDate.format(message.createdAt),
             "conversationTitle": conversations.first { $0.id == message.conversationId }?.title ?? ""]
        }
    }

    private func create(_ body: [String: Any]) -> (Int, Any?, Data?) {
        let title = (body["title"] as? String ?? "").trimmingCharacters(in: .whitespaces)
        guard !title.isEmpty else { return (422, ["status": 422, "code": "VALIDATION_FAILED", "detail": "Bitte einen Namen eingeben."], nil) }
        let kind = body["crewId"] is String ? "crew" : (body["announcementOnly"] as? Bool == true ? "announcement" : "group")
        var members = [userId] + (body["memberIds"] as? [String] ?? []).map { $0.lowercased() }
        if kind == "crew" { members += [Self.lukas, Self.jonas] }
        if kind == "site" { members += [Self.anna, Self.officeUser] }
        if kind == "announcement" { members = [userId, Self.officeUser, Self.lukas, Self.thomas, Self.anna, Self.jonas] }
        var unique: [String] = []
        for member in members where !unique.contains(member) { unique.append(member) }
        let id = UUID.v7().uuidString.lowercased()
        let bauakteId = (body["bauakteId"] as? String)?.lowercased()
        let conversation = Conversation(id: id, title: title, kind: kind, description: body["description"] as? String, members: unique,
                                        admins: [userId], unread: 0, onlyAdminsWrite: kind == "announcement",
                                        bauakteId: bauakteId, bauakteTitle: bauakteId == nil ? nil : title,
                                        crewId: (body["crewId"] as? String)?.lowercased(), crewName: kind == "crew" ? title : nil)
        conversations.append(conversation)
        system(in: id, "Max Huber hat die Gruppe „\(title)“ erstellt.")
        return (201, summary(conversation), nil)
    }

    /// Der Server kennt keine Systemnachrichten; Änderungen an der Gruppe zeigt die Gruppeninfo.
    private func system(in conversationId: String, _ text: String) {}

    /// `POST /chat/bauakten/{id}/group`: Baustellen-Gruppe anlegen bzw. die vorhandene.
    private func bauakteGroup(_ bauakteId: String) -> (Int, Any?, Data?) {
        if let existing = conversations.first(where: { $0.bauakteId == bauakteId }) { return (200, summary(existing), nil) }
        let conversation = Conversation(id: UUID.v7().uuidString.lowercased(), title: "Baustelle", kind: "site", members: [userId, Self.anna, Self.officeUser],
                                        admins: [userId], unread: 0, bauakteId: bauakteId, bauakteTitle: "Baustelle")
        conversations.append(conversation)
        return (201, summary(conversation), nil)
    }

    /// Eigene Nachrichten gelten nach 4 Sekunden als gelesen; das Büro antwortet einmal nach 6 Sekunden.
    private func settle() {
        let now = Date()
        for index in messages.indices where messages[index].senderId == userId && messages[index].readAt == nil
            && now.timeIntervalSince(messages[index].createdAt) > 4 {
            messages[index].readAt = now
            messages[index].changedAt = now
        }
        if !replied, let mine = messages.last(where: { $0.conversationId == Self.office && $0.senderId == userId && $0.createdAt > now.addingTimeInterval(-600) }),
           now.timeIntervalSince(mine.createdAt) > 6 {
            replied = true
            messages.append(Message(id: UUID.v7().uuidString.lowercased(), conversationId: Self.office, senderId: Self.officeUser, senderName: "Sabine Wagner",
                                    text: "Danke, ist notiert.", createdAt: now, changedAt: now))
            if let index = conversations.firstIndex(where: { $0.id == Self.office }) { conversations[index].unread += 1 }
        }
    }

    private func json(_ message: Message) -> [String: Any] {
        let mine = message.senderId == userId
        let recipients = max(1, (conversations.first { $0.id == message.conversationId }?.members.count ?? 2) - 1)
        var item: [String: Any] = [
            "id": message.id, "conversationId": message.conversationId, "senderUserId": message.senderId, "senderName": message.senderName,
            "text": message.deleted ? NSNull() as Any : message.text as Any, "createdAt": APIDate.format(message.createdAt), "mine": mine && !message.system,
            "deleted": message.deleted,
            "status": mine ? (message.readAt != nil ? "read" : "delivered") as Any : NSNull() as Any,
            "readBy": mine && message.readAt != nil ? recipients : 0, "recipients": mine ? recipients : 0,
            "attachments": message.attachments, "pinnedAt": message.pinned ? APIDate.format(message.changedAt) as Any : NSNull() as Any,
            "forwarded": message.forwarded, "mentionedUserIds": message.mentions, "clientMessageId": orNull(message.clientMessageId),
            "canEdit": mine && Date().timeIntervalSince(message.createdAt) <= 15 * 60,
        ]
        var counts: [String: (Int, Bool, [String])] = [:]
        for (person, emoji) in message.reactions {
            var entry = counts[emoji] ?? (0, false, [])
            entry.0 += 1
            entry.1 = entry.1 || person == userId
            entry.2.append(names[person] ?? "")
            counts[emoji] = entry
        }
        item["reactions"] = counts.sorted { $0.value.0 > $1.value.0 || ($0.value.0 == $1.value.0 && $0.key < $1.key) }
            .map { ["emoji": $0.key, "count": $0.value.0, "mine": $0.value.1, "names": $0.value.2] as [String: Any] }
        if let replyTo = message.replyTo { item["replyTo"] = replyTo }
        if let editedAt = message.editedAt { item["editedAt"] = APIDate.format(editedAt) }
        if let location = message.location { item["location"] = location }
        return item
    }

    /// `GET /chat/contacts`
    func contacts() -> [[String: Any]] {
        [["id": Self.anna, "name": "Anna Keller", "label": "Bauleitung"], ["id": Self.officeUser, "name": "Sabine Wagner", "label": "Büro"],
         ["id": Self.lukas, "name": "Lukas Brandl"], ["id": Self.jonas, "name": "Jonas Wimmer"], ["id": Self.thomas, "name": "Thomas Gruber"]]
    }

    /// `POST /chat/conversations/direct`: vorhandenen Einzelchat oder neuen.
    func direct(with personId: String) -> (Int, Any?, Data?) {
        let person = personId.lowercased()
        if let existing = mine.first(where: { $0.kind == "direct" && $0.members.contains(person) }) { return (200, summary(existing), nil) }
        guard let name = names[person] else { return (404, problem("NOT_FOUND"), nil) }
        let conversation = Conversation(id: UUID.v7().uuidString.lowercased(), title: name, kind: "direct", members: [userId, person], admins: [], unread: 0)
        conversations.append(conversation)
        return (200, summary(conversation), nil)
    }
}
