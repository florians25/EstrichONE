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

    /// `GET /system/health-banner` im Demomodus.
    func health() -> [String: Any] {
        let now = Date()
        return [
            "backupTarget": [
                "configured": true, "reachable": !backupDown,
                "lastSuccessAt": APIDate.format(now.addingTimeInterval(backupDown ? -31 * 3600 : -5 * 3600)),
                "severity": backupDown ? "critical" : "ok",
            ],
            "archive": [
                "configured": true, "reachable": !archiveDown,
                "lastSuccessAt": APIDate.format(now.addingTimeInterval(archiveDown ? -9 * 86400 : -86400)),
                "severity": archiveDown ? "warning" : "ok",
            ],
        ]
    }
}

/// Chat im Demomodus: drei Unterhaltungen mit Büro, Kolonne und einem Kollegen. Gesendete Nachrichten gelten nach
/// kurzer Zeit als gelesen; das Büro bestätigt die erste eigene Nachricht. Aufruf nur unter der Sperre des FakeServers.
final class DemoChat {
    private struct Conversation {
        let id: String
        let title: String
        let kind: String
        var unread: Int
    }

    private struct Message {
        let id: String
        let conversationId: String
        let senderId: String
        let senderName: String
        let text: String
        let createdAt: Date
        var readAt: Date?
    }

    static let office = "0193a1b2-de00-7000-8000-00000000d001"
    static let crew = "0193a1b2-de00-7000-8000-00000000d002"
    static let colleague = "0193a1b2-de00-7000-8000-00000000d003"
    private static let officeUser = "0193a1b2-de00-7000-8000-00000000a0a2"
    private static let lukas = "0193a1b2-0000-7000-8000-00000000bbbb"

    private let userId: String
    private var conversations: [Conversation]
    private var messages: [Message]
    private var replied = false

    init(userId: String) {
        self.userId = userId
        conversations = [
            Conversation(id: Self.office, title: "Büro – Sabine Wagner", kind: "direct", unread: 2),
            Conversation(id: Self.crew, title: "Kolonne Huber", kind: "group", unread: 1),
            Conversation(id: Self.colleague, title: "Lukas Brandl", kind: "direct", unread: 0),
        ]
        let now = Date()
        func at(_ minutes: Double) -> Date { now.addingTimeInterval(-minutes * 60) }
        messages = [
            Message(id: "0193a1b2-de00-7000-8000-00000000e001", conversationId: Self.office, senderId: userId, senderName: "Max Huber",
                    text: "Guten Morgen, wir sind um 7:20 an der Praxis Dr. Maier.", createdAt: at(95), readAt: at(90)),
            Message(id: "0193a1b2-de00-7000-8000-00000000e002", conversationId: Self.office, senderId: Self.officeUser, senderName: "Sabine Wagner",
                    text: "Danke! Der Hausmeister ist ab 7 Uhr da, Schlüssel liegt beim Empfang.", createdAt: at(88)),
            Message(id: "0193a1b2-de00-7000-8000-00000000e003", conversationId: Self.office, senderId: Self.officeUser, senderName: "Sabine Wagner",
                    text: "Kurze Info: Die CM-Messung für Behandlungsraum 2 bitte heute noch eintragen.", createdAt: at(14)),
            Message(id: "0193a1b2-de00-7000-8000-00000000e004", conversationId: Self.crew, senderId: Self.lukas, senderName: "Lukas Brandl",
                    text: "Pumpe 2 ist getankt und steht am Lager.", createdAt: at(240), readAt: at(230)),
            Message(id: "0193a1b2-de00-7000-8000-00000000e005", conversationId: Self.crew, senderId: userId, senderName: "Max Huber",
                    text: "Super, ich hole sie um 6:45 ab.", createdAt: at(236), readAt: at(220)),
            Message(id: "0193a1b2-de00-7000-8000-00000000e006", conversationId: Self.crew, senderId: Self.officeUser, senderName: "Sabine Wagner",
                    text: "Morgen bitte zwei Sack Randdämmstreifen extra mitnehmen.", createdAt: at(32)),
            Message(id: "0193a1b2-de00-7000-8000-00000000e007", conversationId: Self.colleague, senderId: Self.lukas, senderName: "Lukas Brandl",
                    text: "Hast du den Laser noch im Wagen?", createdAt: at(60 * 26), readAt: at(60 * 26)),
            Message(id: "0193a1b2-de00-7000-8000-00000000e008", conversationId: Self.colleague, senderId: userId, senderName: "Max Huber",
                    text: "Ja, bringe ich morgen mit.", createdAt: at(60 * 25.5), readAt: at(60 * 25)),
        ]
    }

    var unreadTotal: Int { conversations.reduce(0) { $0 + $1.unread } }

    func status() -> [String: Any] {
        ["enabled": true, "canUse": true, "unreadCount": unreadTotal]
    }

    func list() -> [String: Any] {
        settle()
        let items: [[String: Any]] = conversations.map { conversation in
            var item: [String: Any] = ["id": conversation.id, "title": conversation.title, "kind": conversation.kind, "unreadCount": conversation.unread]
            if let last = messages.filter({ $0.conversationId == conversation.id }).max(by: { $0.createdAt < $1.createdAt }) {
                item["lastMessage"] = json(last)
            }
            return item
        }.sorted { (($0["lastMessage"] as? [String: Any])?["createdAt"] as? String ?? "") > (($1["lastMessage"] as? [String: Any])?["createdAt"] as? String ?? "") }
        return ["items": items]
    }

    func messages(of conversationId: String) -> [String: Any] {
        settle()
        return ["items": messages.filter { $0.conversationId == conversationId }.sorted { $0.createdAt < $1.createdAt }.map(json)]
    }

    func send(_ text: String, to conversationId: String) -> (Int, [String: Any]) {
        guard let clean = ChatLimits.clean(text) else { return (400, ["code": "TEXT_REQUIRED", "detail": "Bitte einen Text eingeben."]) }
        guard conversations.contains(where: { $0.id == conversationId }) else { return (404, ["code": "NOT_FOUND", "detail": "Nicht gefunden"]) }
        let message = Message(id: UUID.v7().uuidString.lowercased(), conversationId: conversationId, senderId: userId, senderName: "Max Huber", text: clean, createdAt: Date())
        messages.append(message)
        return (201, json(message))
    }

    func markRead(_ conversationId: String) {
        if let index = conversations.firstIndex(where: { $0.id == conversationId }) { conversations[index].unread = 0 }
    }

    /// Eigene Nachrichten gelten nach 4 Sekunden als gelesen; das Büro antwortet einmal nach 6 Sekunden.
    private func settle() {
        let now = Date()
        for index in messages.indices where messages[index].senderId == userId && messages[index].readAt == nil
            && now.timeIntervalSince(messages[index].createdAt) > 4 {
            messages[index].readAt = now
        }
        if !replied, let mine = messages.last(where: { $0.conversationId == Self.office && $0.senderId == userId && $0.createdAt > now.addingTimeInterval(-600) }),
           now.timeIntervalSince(mine.createdAt) > 6 {
            replied = true
            messages.append(Message(id: UUID.v7().uuidString.lowercased(), conversationId: Self.office, senderId: Self.officeUser, senderName: "Sabine Wagner",
                                    text: "Danke, ist notiert.", createdAt: now))
            if let index = conversations.firstIndex(where: { $0.id == Self.office }) { conversations[index].unread += 1 }
        }
    }

    private func json(_ message: Message) -> [String: Any] {
        [
            "id": message.id, "conversationId": message.conversationId, "senderId": message.senderId, "senderName": message.senderName,
            "text": message.text, "createdAt": APIDate.format(message.createdAt),
            "state": message.readAt != nil ? "read" : (message.senderId == userId ? "delivered" : "read"),
        ]
    }
}
