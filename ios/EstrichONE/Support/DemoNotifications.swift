import EstrichCore
import Foundation

/// Postfach im Demomodus: Beispiel-Benachrichtigungen, wie sie der Server schickt (Plan freigegeben, Antwort im Helpdesk,
/// Zuweisung, Frist, Nachricht aus dem Büro). Aufruf nur unter der Sperre des FakeServers.
final class DemoNotifications {
    private struct Item {
        let id: String
        let category: String
        let urgency: String
        let title: String
        let body: String
        let link: String
        let minutesAgo: Int
        var read: Bool
    }

    private var items: [Item] = [
        Item(id: "0193a1b2-de00-7000-8000-00000000a001", category: "plan", urgency: "high", title: "Plan für morgen freigegeben",
             body: "Kolonne Huber: 07:30 Praxis Dr. Maier, 10:30 EFH Schneider.", link: "/planung/wer-faehrt-wohin?tag=morgen", minutesAgo: 12, read: false),
        Item(id: "0193a1b2-de00-7000-8000-00000000a002", category: "ticket", urgency: "normal", title: "Antwort zu T-2026-0007",
             body: "IT-Support: Bitte einmal die App öffnen und im WLAN warten.", link: "/helpdesk/\(DemoContent.ticketId)", minutesAgo: 47, read: false),
        Item(id: "0193a1b2-de00-7000-8000-00000000a003", category: "broadcast", urgency: "normal", title: "Nachricht aus dem Büro",
             body: "Ab Montag Abholung der Pumpe 2 erst ab 6:45 Uhr am Lager.", link: "", minutesAgo: 180, read: false),
        Item(id: "0193a1b2-de00-7000-8000-00000000a004", category: "assignment", urgency: "normal", title: "Neue Bauakte zugewiesen",
             body: "2026-044 · MFH Isarpark, Haus B", link: "/bauakten/\(DemoContent.isarpark)", minutesAgo: 60 * 26, read: true),
        Item(id: "0193a1b2-de00-7000-8000-00000000a005", category: "deadline", urgency: "low", title: "Frist morgen",
             body: "CM-Messung Behandlungsraum 2 · Praxis Dr. Maier", link: "/bauakten/\(DemoContent.praxis)", minutesAgo: 60 * 30, read: true),
    ]

    func inbox() -> [String: Any] {
        let now = Date()
        let list: [[String: Any]] = items.map { item in
            [
                "id": item.id, "category": item.category, "urgency": item.urgency, "title": item.title, "body": item.body,
                "link": item.link.isEmpty ? NSNull() : item.link, "objectType": NSNull(), "objectId": NSNull(),
                "createdAt": APIDate.format(now.addingTimeInterval(TimeInterval(-60 * item.minutesAgo))),
                "readAt": item.read ? APIDate.format(now.addingTimeInterval(TimeInterval(-60 * item.minutesAgo + 300))) : NSNull(),
            ]
        }
        return ["items": list, "unreadCount": items.filter { !$0.read }.count]
    }

    func markRead(_ id: String) {
        if let index = items.firstIndex(where: { $0.id == id }) { items[index].read = true }
    }

    func markAllRead() {
        for index in items.indices { items[index].read = true }
    }
}
