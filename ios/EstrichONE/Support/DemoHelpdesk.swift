import EstrichCore
import Foundation

/// Helpdesk im Demomodus: eigene Meldungen und – als IT-Bearbeitung – alle Tickets, mit Verlauf, Antworten (auch mit
/// Bild), Status, Priorität und Zuweisung. Aufruf nur unter der Sperre des FakeServers.
final class DemoHelpdesk {
    private struct Entry {
        let id: String
        let number: String
        var title: String
        var description: String?
        var category: String
        var priority: String
        var status: String
        let reporterId: String
        let reporterName: String
        var assigneeId: String?
        var assigneeName: String?
        let createdAt: Date
        var lastActivityAt: Date
        var timeline: [[String: Any]]
    }

    private let userId: String
    private var tickets: [Entry] = []
    private var counter = 0

    init(userId: String) {
        self.userId = userId
        let now = Date()
        func ago(_ minutes: Double) -> Date { now.addingTimeInterval(-minutes * 60) }
        tickets = [
            Entry(id: DemoContent.ticketId, number: "T-2026-0007", title: "Fotos werden nicht übertragen",
                  description: "Seit gestern bleiben Fotos auf „Wird übertragen“ stehen.", category: "sync", priority: "normal",
                  status: "waitingForReporter", reporterId: userId, reporterName: "Max Huber", assigneeId: "it", assigneeName: "IT-Support",
                  createdAt: ago(60 * 26), lastActivityAt: ago(35), timeline: []),
            Entry(id: "0193a1b2-de00-7000-8000-00000000c002", number: "T-2026-0009", title: "Drucker im Büro druckt nur leere Seiten",
                  description: "Der Etagendrucker im 1. OG zieht Papier ein, druckt aber nichts.", category: "officeIt", priority: "high",
                  status: "inProgress", reporterId: "office", reporterName: "Sabine Wagner", assigneeId: userId, assigneeName: "Max Huber",
                  createdAt: ago(60 * 3), lastActivityAt: ago(50), timeline: []),
            Entry(id: "0193a1b2-de00-7000-8000-00000000c003", number: "T-2026-0010", title: "Tablet Kolonne Kaya: Display gesprungen",
                  description: "Beim Abladen heruntergefallen, Touch reagiert unten nicht mehr.", category: "deviceDefect", priority: "urgent",
                  status: "new", reporterId: "kaya", reporterName: "Emre Kaya", assigneeId: nil, assigneeName: nil,
                  createdAt: ago(18), lastActivityAt: ago(18), timeline: []),
            Entry(id: "0193a1b2-de00-7000-8000-00000000c004", number: "T-2026-0004", title: "Zugang für neuen Mitarbeiter",
                  description: "Jonas Wirth braucht einen Zugang für die App.", category: "access", priority: "normal", status: "resolved",
                  reporterId: userId, reporterName: "Max Huber", assigneeId: "it", assigneeName: "IT-Support",
                  createdAt: ago(60 * 24 * 6), lastActivityAt: ago(60 * 24 * 5), timeline: []),
        ]
        for index in tickets.indices {
            let ticket = tickets[index]
            tickets[index].timeline = [event(ticket.createdAt, "created", ticket.reporterName, "\(ticket.reporterName) hat die Meldung erstellt.",
                                             reporter: true)]
        }
        tickets[0].timeline += [
            event(ago(60 * 25), "status", "IT-Support", "IT-Support hat den Status auf „In Bearbeitung“ gesetzt."),
            comment(ago(60 * 24), "IT-Support", "Danke für die Meldung! Welche App-Version ist installiert? Steht unter Mehr → Info.", reporter: false),
            comment(ago(60 * 23), "Max Huber", "Version 0.1.0, iPhone 15. Betroffen sind nur Fotos von heute früh.", reporter: true),
            comment(ago(35), "IT-Support", "Bitte einmal die App öffnen und im WLAN warten, bis die Synchronisation fertig ist. Klappt es dann?",
                    reporter: false),
            event(ago(35), "status", "IT-Support", "IT-Support hat den Status auf „Rückfrage an Sie“ gesetzt."),
        ]
        tickets[1].timeline += [
            event(ago(60 * 2), "assignee", "IT-Support", "IT-Support hat die Meldung Max Huber zugewiesen."),
            comment(ago(50), "Max Huber", "Toner ist neu. Ich prüfe vor Ort die Treiber.", reporter: false),
        ]
        tickets[3].timeline += [
            comment(ago(60 * 24 * 5), "IT-Support", "Zugang ist eingerichtet, Einladung ging per E-Mail raus.", reporter: false),
            event(ago(60 * 24 * 5), "status", "IT-Support", "IT-Support hat den Status auf „Gelöst“ gesetzt."),
        ]
    }

    private func event(_ at: Date, _ kind: String, _ actor: String, _ summary: String, reporter: Bool = false) -> [String: Any] {
        ["id": UUID().uuidString.lowercased(), "at": APIDate.format(at), "kind": kind, "actorUserId": NSNull(), "actorName": actor,
         "summary": summary, "text": NSNull(), "fromValue": NSNull(), "toValue": NSNull(), "isInternal": false, "isReporter": reporter,
         "attachmentFileIds": [String]()]
    }

    private func comment(_ at: Date, _ actor: String, _ text: String, reporter: Bool, attachments: [String] = []) -> [String: Any] {
        ["id": UUID().uuidString.lowercased(), "at": APIDate.format(at), "kind": "comment", "actorUserId": NSNull(), "actorName": actor,
         "summary": "\(actor) hat geantwortet.", "text": text, "fromValue": NSNull(), "toValue": NSNull(), "isInternal": false,
         "isReporter": reporter, "attachmentFileIds": attachments]
    }

    private func summary(_ ticket: Entry) -> [String: Any] {
        ["id": ticket.id, "number": ticket.number, "title": ticket.title, "category": ticket.category, "priority": ticket.priority,
         "status": ticket.status, "reporterBlocked": ticket.priority == "urgent", "reporterUserId": ticket.reporterId,
         "reporterDisplayName": ticket.reporterName, "assigneeUserId": ticket.assigneeId ?? NSNull(), "assigneeName": ticket.assigneeName ?? NSNull(),
         "source": "ios", "createdAt": APIDate.format(ticket.createdAt), "lastActivityAt": APIDate.format(ticket.lastActivityAt), "version": 1]
    }

    func list(all: Bool) -> [[String: Any]] {
        tickets.filter { all || $0.reporterId == userId }.sorted { $0.lastActivityAt > $1.lastActivityAt }.map(summary)
    }

    func detail(_ id: String) -> [String: Any]? {
        guard let ticket = tickets.first(where: { $0.id == id }) else { return nil }
        return ["summary": summary(ticket), "description": ticket.description ?? NSNull(), "resolution": NSNull(), "comments": [[String: Any]](),
                "events": [[String: Any]](), "attachmentFileIds": [String](), "timeline": ticket.timeline]
    }

    /// Meldung aus der App (Sync `ticket.create`).
    func add(id: String, payload: [String: Any]) {
        guard !tickets.contains(where: { $0.id == id }) else { return }
        counter += 1
        let now = Date()
        var ticket = Entry(id: id, number: "T-2026-00\(20 + counter)", title: payload["title"] as? String ?? "Meldung",
                           description: payload["description"] as? String, category: payload["category"] as? String ?? "other",
                           priority: payload["priority"] as? String ?? "normal", status: "new", reporterId: userId, reporterName: "Max Huber",
                           assigneeId: nil, assigneeName: nil, createdAt: now, lastActivityAt: now, timeline: [])
        ticket.timeline = [event(now, "created", "Max Huber", "Max Huber hat die Meldung erstellt.", reporter: true)]
        tickets.append(ticket)
    }

    func comment(_ id: String, body: [String: Any]) -> [String: Any]? {
        guard let index = tickets.firstIndex(where: { $0.id == id }) else { return nil }
        let now = Date()
        let mine = tickets[index].reporterId == userId
        tickets[index].timeline.append(comment(now, "Max Huber", body["text"] as? String ?? "", reporter: mine,
                                               attachments: (body["attachmentFileIds"] as? [String]) ?? []))
        if mine, tickets[index].status == "waitingForReporter" {
            tickets[index].status = "inProgress"
            tickets[index].timeline.append(event(now, "status", "Max Huber", "Status: In Bearbeitung"))
        }
        tickets[index].lastActivityAt = now
        return detail(id)
    }

    func update(_ id: String, body: [String: Any]) -> [String: Any]? {
        guard let index = tickets.firstIndex(where: { $0.id == id }) else { return nil }
        let now = Date()
        if let status = body["status"] as? String {
            tickets[index].status = status
            let title = TicketStatus(rawValue: status)?.title ?? status
            tickets[index].timeline.append(event(now, "status", "Max Huber", "Max Huber hat den Status auf „\(title)“ gesetzt."))
        }
        if let priority = body["priority"] as? String {
            tickets[index].priority = priority
            tickets[index].timeline.append(event(now, "priority", "Max Huber",
                                                 "Max Huber hat die Priorität auf „\(TicketPriority(rawValue: priority)?.title ?? priority)“ gesetzt."))
        }
        if let assignee = (body["assigneeUserId"] as? String)?.lowercased() {
            tickets[index].assigneeId = assignee
            tickets[index].assigneeName = assignee == userId ? "Max Huber" : "Lukas Brandl"
            tickets[index].timeline.append(event(now, "assignee", "Max Huber", "Max Huber hat die Meldung \(tickets[index].assigneeName ?? "") zugewiesen."))
        } else if body["unassign"] as? Bool == true {
            tickets[index].assigneeId = nil
            tickets[index].assigneeName = nil
        }
        tickets[index].lastActivityAt = now
        return detail(id)
    }
}
