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
        var resolution: String?
        var rating: Int?
        var groupId: String? = DemoHelpdesk.groupIt
        var subcategoryId: String?
        var channel = "selfService"
        var createdByName: String?
    }

    static let groupIt = "0193a1b2-de00-7000-8000-00000000f101"
    static let groupOffice = "0193a1b2-de00-7000-8000-00000000f102"
    static let articleId = "0193a1b2-de00-7000-8000-00000000e001"
    private static let groups = [groupIt: "IT", groupOffice: "Büro"]
    private static let subcategories = ["0193a1b2-de00-7000-8000-00000000e102": "Drucker", "0193a1b2-de00-7000-8000-00000000e103": "Telefon"]
    private static let agentNames = ["0193a1b2-de00-7000-8000-00000000a0a1": "IT-Support", "0193a1b2-de00-7000-8000-00000000a0a4": "Anna Lechner"]

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
                  status: "waitingForReporter", reporterId: userId, reporterName: "Max Huber", assigneeId: "0193a1b2-de00-7000-8000-00000000a0a1", assigneeName: "IT-Support",
                  createdAt: ago(60 * 26), lastActivityAt: ago(35), timeline: [], resolution: nil, rating: nil),
            Entry(id: "0193a1b2-de00-7000-8000-00000000c002", number: "T-2026-0009", title: "Drucker im Büro druckt nur leere Seiten",
                  description: "Der Etagendrucker im 1. OG zieht Papier ein, druckt aber nichts.", category: "officeIt", priority: "high",
                  status: "waitingForSupport", reporterId: "0193a1b2-de00-7000-8000-00000000a0a2", reporterName: "Sabine Wagner", assigneeId: userId, assigneeName: "Max Huber",
                  createdAt: ago(60 * 3), lastActivityAt: ago(50), timeline: [], resolution: nil, rating: nil),
            Entry(id: "0193a1b2-de00-7000-8000-00000000c003", number: "T-2026-0010", title: "Tablet Kolonne Kaya: Display gesprungen",
                  description: "Beim Abladen heruntergefallen, Touch reagiert unten nicht mehr.", category: "deviceDefect", priority: "urgent",
                  status: "new", reporterId: "0193a1b2-de00-7000-8000-000000004005", reporterName: "Emre Kaya", assigneeId: nil, assigneeName: nil,
                  createdAt: ago(18), lastActivityAt: ago(18), timeline: [], resolution: nil, rating: nil),
            Entry(id: "0193a1b2-de00-7000-8000-00000000c004", number: "T-2026-0004", title: "Zugang für neuen Mitarbeiter",
                  description: "Jonas Wirth braucht einen Zugang für die App.", category: "access", priority: "normal", status: "resolved",
                  reporterId: userId, reporterName: "Max Huber", assigneeId: "0193a1b2-de00-7000-8000-00000000a0a1", assigneeName: "IT-Support",
                  createdAt: ago(60 * 24 * 6), lastActivityAt: ago(60 * 24 * 5), timeline: [], resolution: nil, rating: nil),
        ]
        for index in tickets.indices {
            let ticket = tickets[index]
            tickets[index].timeline = [event(ticket.createdAt, "created", ticket.reporterName, "\(ticket.reporterName) hat die Meldung erstellt.",
                                             reporter: true)]
        }
        tickets[0].timeline += [
            event(ago(60 * 25), "status", "IT-Support", "IT-Support hat den Status auf „In Bearbeitung“ gesetzt."),
            comment(ago(60 * 24), "IT-Support", "Danke für die Meldung. Welche App-Version ist installiert? Sie steht unter Mehr > Info.", reporter: false),
            comment(ago(60 * 23), "Max Huber", "Version 0.1.0, iPhone 15. Betroffen sind nur Fotos von heute früh.", reporter: true),
            comment(ago(35), "IT-Support", "Bitte einmal die App öffnen und im WLAN warten, bis die Synchronisation fertig ist. Klappt es dann?",
                    reporter: false),
            event(ago(35), "status", "IT-Support", "IT-Support hat den Status auf „Rückfrage an Sie“ gesetzt."),
        ]
        tickets[1].timeline += [
            event(ago(60 * 2), "assignee", "IT-Support", "IT-Support hat die Meldung Max Huber zugewiesen."),
            comment(ago(50), "Max Huber", "Welche Meldung zeigt das Display des Druckers?", reporter: false),
            event(ago(50), "status", "Max Huber", "Status: In Bearbeitung → Warten auf Anwenderinfo"),
            comment(ago(12), "Sabine Wagner", "„Toner fast leer“ – neuer Toner ist aber drin.", reporter: true),
            event(ago(12), "status", "Sabine Wagner", "Status: Warten auf Anwenderinfo → Warten auf Support"),
        ]
        tickets[1].subcategoryId = "0193a1b2-de00-7000-8000-00000000e102"
        tickets[0].timeline.insert(note(ago(60 * 24 + 5), "IT-Support", "Betrifft auch zwei andere Geräte der Kolonne – vermutlich der Upload-Server."), at: 3)
        tickets[3].resolution = "Konto angelegt, Zugangsbrief per E-Mail verschickt."
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

    private func note(_ at: Date, _ actor: String, _ text: String) -> [String: Any] {
        var entry = comment(at, actor, text, reporter: false)
        entry["kind"] = "note"
        entry["isInternal"] = true
        entry["summary"] = "\(actor) hat eine interne Notiz geschrieben"
        return entry
    }

    private func summary(_ ticket: Entry) -> [String: Any] {
        ["id": ticket.id, "number": ticket.number, "title": ticket.title, "category": ticket.category, "priority": ticket.priority,
         "status": ticket.status, "reporterBlocked": ticket.priority == "urgent", "reporterUserId": ticket.reporterId,
         "reporterDisplayName": ticket.reporterName, "assigneeUserId": ticket.assigneeId ?? NSNull(), "assigneeName": ticket.assigneeName ?? NSNull(),
         "source": "ios", "createdAt": APIDate.format(ticket.createdAt), "lastActivityAt": APIDate.format(ticket.lastActivityAt), "version": 1,
         "slaState": ticket.status == "new" && ticket.priority == "urgent" ? "overdue" : (ticket.status == "waitingForReporter" ? "paused" : "ok"),
         "dueKind": ticket.status == "new" ? "response" : "resolution", "rating": ticket.rating ?? NSNull(),
         "subcategoryId": ticket.subcategoryId ?? NSNull(),
         "subcategoryName": ticket.subcategoryId.flatMap { Self.subcategories[$0] } ?? NSNull(),
         "groupId": ticket.groupId ?? NSNull(), "groupName": ticket.groupId.flatMap { Self.groups[$0] } ?? NSNull(),
         "channel": ticket.channel, "createdByName": ticket.createdByName ?? NSNull()]
    }

    func list(all: Bool) -> [[String: Any]] {
        tickets.filter { all || $0.reporterId == userId }.sorted { $0.lastActivityAt > $1.lastActivityAt }.map(summary)
    }

    /// Ansichten der Bearbeitung (filter=mine, unassigned, overdue, support, waiting, open).
    func list(filter: String?) -> [[String: Any]] {
        tickets.filter { ticket in
            let open = ticket.status != "resolved" && ticket.status != "closed"
            switch filter {
            case "mine": return open && ticket.assigneeId == userId
            case "unassigned": return open && ticket.assigneeId == nil
            case "overdue": return open && ticket.priority == "urgent"
            case "support": return ticket.status == "waitingForSupport"
            case "waiting": return ticket.status == "waitingForReporter"
            case "open": return open
            default: return true
            }
        }.sorted { $0.lastActivityAt > $1.lastActivityAt }.map(summary)
    }

    func detail(_ id: String) -> [String: Any]? {
        guard let ticket = tickets.first(where: { $0.id == id }) else { return nil }
        let done = ticket.status == "resolved" || ticket.status == "closed"
        let articles: [[String: Any]] = ticket.category == "sync"
            ? [["id": "0193a1b2-de00-7000-8000-00000000e001", "title": "Fotos werden nicht übertragen", "isPublished": true]] : []
        return ["summary": summary(ticket), "description": ticket.description ?? NSNull(), "resolution": ticket.resolution ?? NSNull(),
                "comments": [[String: Any]](), "events": [[String: Any]](), "attachmentFileIds": [String](), "timeline": ticket.timeline,
                "canRate": done && ticket.reporterId == userId, "rating": ticket.rating ?? NSNull(), "articles": articles]
    }

    /// Bewertung des Melders nach der Lösung.
    func rate(_ id: String, body: [String: Any]) -> [String: Any]? {
        guard let index = tickets.firstIndex(where: { $0.id == id }) else { return nil }
        let value = min(max(body["rating"] as? Int ?? 0, 1), 5)
        tickets[index].rating = value
        tickets[index].timeline.append(event(Date(), "rating", "Max Huber", "Bewertung: \(value) von 5 Sternen"))
        return detail(id)
    }

    /// Kategorien mit Unterkategorien (Einstellungen im Web).
    func options() -> [String: Any] {
        ["ratingEnabled": true, "suggestArticles": true, "slaEnabled": true, "timeTrackingEnabled": true,
         "categories": TicketCategory.allCases.map { category -> [String: Any] in
             let subs: [[String: Any]] = category == .officeIt
                 ? [["id": "0193a1b2-de00-7000-8000-00000000e102", "category": "officeIt", "name": "Drucker", "isActive": true, "sortOrder": 0, "version": 1],
                    ["id": "0193a1b2-de00-7000-8000-00000000e103", "category": "officeIt", "name": "Telefon", "isActive": true, "sortOrder": 1, "version": 1]]
                 : []
             return ["key": category.rawValue, "name": category.title, "subcategories": subs]
         }]
    }

    /// Meldung aus der App (Sync `ticket.create`).
    func add(id: String, payload: [String: Any]) {
        guard !tickets.contains(where: { $0.id == id }) else { return }
        counter += 1
        let now = Date()
        var ticket = Entry(id: id, number: "T-2026-00\(20 + counter)", title: payload["title"] as? String ?? "Meldung",
                           description: payload["description"] as? String, category: payload["category"] as? String ?? "other",
                           priority: payload["priority"] as? String ?? "normal", status: "new", reporterId: userId, reporterName: "Max Huber",
                           assigneeId: nil, assigneeName: nil, createdAt: now, lastActivityAt: now, timeline: [], resolution: nil, rating: nil)
        ticket.timeline = [event(now, "created", "Max Huber", "Max Huber hat die Meldung erstellt.", reporter: true)]
        tickets.append(ticket)
    }

    func comment(_ id: String, body: [String: Any]) -> [String: Any]? {
        guard let index = tickets.firstIndex(where: { $0.id == id }) else { return nil }
        let now = Date()
        let mine = tickets[index].reporterId == userId
        if body["isInternal"] as? Bool == true {
            tickets[index].timeline.append(note(now, "Max Huber", body["text"] as? String ?? ""))
            tickets[index].lastActivityAt = now
            return detail(id)
        }
        tickets[index].timeline.append(comment(now, "Max Huber", body["text"] as? String ?? "", reporter: mine,
                                               attachments: (body["attachmentFileIds"] as? [String]) ?? []))
        if mine, tickets[index].status == "waitingForReporter" {
            tickets[index].status = "waitingForSupport"
            tickets[index].timeline.append(event(now, "status", "Max Huber", "Status: Warten auf Anwenderinfo → Warten auf Support"))
        } else if !mine, tickets[index].status == "waitingForSupport" {
            tickets[index].status = "inProgress"
            tickets[index].timeline.append(event(now, "status", "Max Huber", "Status: Warten auf Support → In Bearbeitung"))
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
        if let category = body["category"] as? String, category != tickets[index].category {
            tickets[index].category = category
            tickets[index].subcategoryId = nil
            tickets[index].timeline.append(event(now, "category", "Max Huber", "Kategorie: \(TicketCategory(rawValue: category)?.title ?? category)"))
        }
        if let sub = (body["subcategoryId"] as? String)?.lowercased() { tickets[index].subcategoryId = sub }
        if body["clearSubcategory"] as? Bool == true { tickets[index].subcategoryId = nil }
        if let group = (body["groupId"] as? String)?.lowercased() {
            tickets[index].groupId = group
            tickets[index].timeline.append(event(now, "group", "Max Huber", "Gruppe: \(Self.groups[group] ?? "–")"))
        }
        if body["clearGroup"] as? Bool == true { tickets[index].groupId = nil }
        if let assignee = (body["assigneeUserId"] as? String)?.lowercased() {
            tickets[index].assigneeId = assignee
            tickets[index].assigneeName = assignee == userId ? "Max Huber" : (Self.agentNames[assignee] ?? "Lukas Brandl")
            tickets[index].timeline.append(event(now, "assignee", "Max Huber", "Max Huber hat die Meldung \(tickets[index].assigneeName ?? "") zugewiesen."))
        } else if body["unassign"] as? Bool == true {
            tickets[index].assigneeId = nil
            tickets[index].assigneeName = nil
        }
        tickets[index].lastActivityAt = now
        return detail(id)
    }

    // MARK: Bearbeitung

    func agents() -> [[String: Any]] {
        [["id": userId, "displayName": "Max Huber", "openTickets": 1]]
            + Self.agentNames.sorted { $0.value < $1.value }.map { ["id": $0.key, "displayName": $0.value, "openTickets": 3] }
    }

    func groupList() -> [[String: Any]] {
        Self.groups.sorted { $0.value < $1.value }.map { ["id": $0.key, "name": $0.value, "isActive": true, "memberUserIds": [String]()] }
    }

    func responses() -> [[String: Any]] {
        [["id": "0193a1b2-de00-7000-8000-00000000f201", "name": "Begrüßung", "isPersonal": false,
          "text": "Hallo {Vorname},\n\ndanke für Ihre Meldung {Ticket}. Wir kümmern uns darum."],
         ["id": "0193a1b2-de00-7000-8000-00000000f202", "name": "Abgleich im WLAN", "isPersonal": false,
          "text": "Bitte die App im WLAN öffnen und unter Mehr › Synchronisation „Jetzt abgleichen“ tippen."]]
    }

    func reporters(_ query: String?) -> [[String: Any]] {
        let people = [("0193a1b2-de00-7000-8000-00000000a0a2", "Sabine Wagner", "s.wagner"), ("0193a1b2-de00-7000-8000-000000004005", "Emre Kaya", "e.kaya"),
                      ("0193a1b2-de00-7000-8000-00000000a0a5", "Jonas Wirth", "j.wirth"), ("0193a1b2-0000-7000-8000-00000000bbbb", "Lukas Brandl", "l.brandl")]
        let text = (query ?? "").lowercased()
        return people.filter { text.isEmpty || $0.1.lowercased().contains(text) || $0.2.contains(text) }
            .map { ["id": $0.0, "displayName": $0.1, "userName": $0.2] }
    }

    func article(_ id: String) -> [String: Any]? {
        guard id == Self.articleId else { return nil }
        return ["id": id, "title": "Fotos werden nicht übertragen", "category": "sync", "isPublished": true,
                "body": "So bringen Sie wartende Fotos auf den Server:\n\n1. WLAN oder mobile Daten einschalten.\n\n2. In der App Mehr › Synchronisation öffnen und „Jetzt abgleichen“ tippen.\n\n3. Warten, bis „Alles übertragen“ erscheint.\n\nHilft das nicht, bitte die Meldung beantworten – die IT sieht dann die technischen Angaben."]
    }

    /// „Meldung aufnehmen“: Ticket im Namen eines Melders (Telefon, E-Mail, vor Ort).
    func record(_ body: [String: Any]) -> [String: Any] {
        counter += 1
        let now = Date()
        let id = (body["id"] as? String)?.lowercased() ?? UUID().uuidString.lowercased()
        let reporterId = (body["reporterUserId"] as? String)?.lowercased()
        let reporterName = reporterId.flatMap { rid in reporters(nil).first { ($0["id"] as? String) == rid }?["displayName"] as? String }
            ?? (body["reporterName"] as? String) ?? "Melder"
        let assignee = (body["assigneeUserId"] as? String)?.lowercased()
        let channel = body["channel"] as? String ?? "phone"
        var ticket = Entry(id: id, number: "T-2026-00\(20 + counter)", title: body["title"] as? String ?? "Meldung",
                           description: body["description"] as? String, category: body["category"] as? String ?? "other",
                           priority: body["priority"] as? String ?? "normal", status: assignee == userId ? "inProgress" : "new",
                           reporterId: reporterId ?? "", reporterName: reporterName, assigneeId: assignee, assigneeName: assignee == userId ? "Max Huber" : nil,
                           createdAt: now, lastActivityAt: now, timeline: [], resolution: nil, rating: nil)
        ticket.channel = channel
        ticket.createdByName = "Max Huber"
        ticket.groupId = nil
        ticket.timeline = [event(now, "created", "Max Huber",
                                 "Max Huber hat die Meldung von \(reporterName) aufgenommen (\(TicketChannel.label(channel) ?? "Telefon"))", reporter: true)]
        tickets.append(ticket)
        return detail(id) ?? [:]
    }
}
