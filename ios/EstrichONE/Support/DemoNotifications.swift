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
        Item(id: "0193a1b2-de00-7000-8000-00000000a010", category: "chat", urgency: "normal", title: "Neue Nachricht von Sabine Wagner",
             body: "Sabine Wagner (Büro)", link: "/chat/\(DemoChat.office)", minutesAgo: 14, read: false),
        Item(id: "0193a1b2-de00-7000-8000-00000000a011", category: "work", urgency: "high", title: "Mangel Ihnen zugewiesen",
             body: "Randfuge Flur EG offen · Praxis Dr. Maier", link: "/bauakten/\(DemoContent.praxis)/maengel", minutesAgo: 25, read: false),
        Item(id: "0193a1b2-de00-7000-8000-00000000a012", category: "bauakte", urgency: "normal", title: "Kommentar in Ihrer Bauakte",
             body: "Sabine Wagner: Bitte Fotos der Dämmung vor dem Einbau.", link: "/bauakten/\(DemoContent.efh)", minutesAgo: 70, read: false),
        Item(id: "0193a1b2-de00-7000-8000-00000000a013", category: "approval", urgency: "normal", title: "Dokument zur Genehmigung",
             body: "Aufmaß Haus B · MFH Isarpark", link: "/bauakten/\(DemoContent.isarpark)", minutesAgo: 110, read: true),
        Item(id: "0193a1b2-de00-7000-8000-00000000a014", category: "bauakte", urgency: "normal", title: "Abnahme unterschrieben",
             body: "EFH Schneider · ohne Mängel", link: "/bauakten/\(DemoContent.efh)", minutesAgo: 60 * 5, read: true),
        Item(id: "0193a1b2-de00-7000-8000-00000000a015", category: "absence", urgency: "low", title: "Urlaubsantrag genehmigt",
             body: "Mo 16.11. bis Fr 20.11.", link: "/urlaub", minutesAgo: 60 * 8, read: true),
        Item(id: "0193a1b2-de00-7000-8000-00000000a016", category: "system", urgency: "normal", title: "Wartung angekündigt",
             body: "Samstag 6:00 bis 7:30 Uhr – EstrichONE ist dann nicht erreichbar.", link: "", minutesAgo: 60 * 20, read: true),
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

    /// Auswahl je Ereignis (`/notifications/preferences`).
    private static func basePreferences() -> [[String: Any]] { [
        ["event": "bauakte.assigned", "group": "Bauakten", "label": "Bauakte zugewiesen", "description": "Sie wurden einer Bauakte zugeordnet.", "mandatory": false],
        ["event": "bauakte.comment", "group": "Bauakten", "label": "Neue Notiz in meiner Bauakte", "description": "Höchstens eine je Bauakte, Autor und Tag.", "mandatory": false],
        ["event": "acceptance.signed", "group": "Bauakten", "label": "Abnahme unterschrieben", "description": "Kunde hat unterschrieben oder abgelehnt.", "mandatory": false],
        ["event": "work.assigned", "group": "Aufgaben", "label": "Aufgabe oder Mangel zugewiesen", "description": "Zuweisung im Büro.", "mandatory": false],
        ["event": "deadline.due", "group": "Aufgaben", "label": "Fristen", "description": "Heute oder morgen fällig, überfällig.", "mandatory": false],
        ["event": "planning.released", "group": "Planung", "label": "Plan freigegeben", "description": "Eine Nachricht je Freigabe.", "mandatory": false],
        ["event": "planning.changed", "group": "Planung", "label": "Planänderung für mich", "description": "Nach der Freigabe geändert.", "mandatory": false],
        ["event": "absence.decided", "group": "Personal", "label": "Urlaubsantrag entschieden", "description": "Genehmigt oder abgelehnt.", "mandatory": false],
        ["event": "chat.message", "group": "Chat", "label": "Neue Chatnachricht", "description": "Ohne Inhalt, höchstens alle 10 Minuten je Unterhaltung.", "mandatory": false],
        ["event": "office.message", "group": "System", "label": "Nachricht aus dem Büro", "description": "Pflicht.", "mandatory": true],
        ["event": "system.maintenance", "group": "System", "label": "Wartung angekündigt", "description": "Pflicht.", "mandatory": true],
    ] }

    private var preferences: [[String: Any]] = DemoNotifications.basePreferences().map { item in
        var copy = item
        copy["delivery"] = "push"
        copy["adminOnly"] = false
        return copy
    }

    func preferenceList() -> [String: Any] { ["items": preferences] }

    func savePreferences(_ body: [String: Any]) -> [String: Any] {
        for change in body["items"] as? [[String: Any]] ?? [] {
            guard let event = change["event"] as? String, let delivery = change["delivery"] as? String,
                  let index = preferences.firstIndex(where: { $0["event"] as? String == event }) else { continue }
            if delivery == "off", preferences[index]["mandatory"] as? Bool == true { continue }
            preferences[index]["delivery"] = delivery
        }
        return preferenceList()
    }

    func markRead(_ id: String) {
        if let index = items.firstIndex(where: { $0.id == id }) { items[index].read = true }
    }

    func markAllRead() {
        for index in items.indices { items[index].read = true }
    }
}
