import Foundation
import Testing
@testable import EstrichCore

@Suite("Postfach: Gruppierung, Filter, Ziele")
struct NotificationFeedTests {
    /// Fester Zeitpunkt: Mittwoch, 4.11.2026, 10:00 Uhr in Berlin.
    private var calendar: Calendar {
        var calendar = NotificationFeed.calendar
        calendar.timeZone = TimeZone(identifier: "Europe/Berlin")!
        return calendar
    }

    private var now: Date { calendar.date(from: DateComponents(year: 2026, month: 11, day: 4, hour: 10))! }

    private func item(_ n: Int, _ category: String?, _ title: String, body: String? = nil, link: String? = nil, objectType: String? = nil,
                      sender: String? = nil, hoursAgo: Double, read: Bool = false) -> InboxNotification {
        InboxNotification(id: UUID(uuidString: String(format: "0193a1b2-0000-7000-8000-%012d", n))!, category: category, urgency: "normal",
                          title: title, body: body, link: link, objectType: objectType, senderName: sender,
                          createdAt: now.addingTimeInterval(-hoursAgo * 3600), readAt: read ? now : nil)
    }

    private var sample: [InboxNotification] {
        [
            item(1, "planning", "Plan für morgen freigegeben", hoursAgo: 0.2),
            item(2, "chat", "Neue Nachricht von Sabine Wagner", hoursAgo: 1),
            item(3, "work", "Mangel zugewiesen", body: "Randfuge Flur · Praxis Dr. Maier", hoursAgo: 9, read: true),
            item(4, "ticket", "Antwort zu T-2026-0007", hoursAgo: 14),          // gestern 20:00
            item(5, "bauakte", "Abnahme unterschrieben", hoursAgo: 40, read: true), // Montag 18:00
            item(6, "message", "Nachricht aus dem Büro", body: "Pumpe 2 erst ab 6:45 Uhr", sender: "Sabine Wagner", hoursAgo: 60), // Sonntag
            item(7, "system", "Wartung angekündigt", hoursAgo: 24 * 9, read: true),
        ]
    }

    @Test("Zeiträume: Heute, Gestern, Diese Woche (ab Montag), Älter")
    func periods() {
        #expect(NotificationPeriod(now.addingTimeInterval(-3600), now: now, calendar: calendar) == .today)
        #expect(NotificationPeriod(now.addingTimeInterval(60), now: now, calendar: calendar) == .today)
        #expect(NotificationPeriod(now.addingTimeInterval(-14 * 3600), now: now, calendar: calendar) == .yesterday)
        #expect(NotificationPeriod(now.addingTimeInterval(-40 * 3600), now: now, calendar: calendar) == .thisWeek)
        // Sonntag gehört zur Vorwoche (Woche beginnt am Montag).
        #expect(NotificationPeriod(now.addingTimeInterval(-60 * 3600), now: now, calendar: calendar) == .older)

        let sections = NotificationFeed.sections(sample, now: now, calendar: calendar)
        #expect(sections.map(\.title) == ["Heute", "Gestern", "Diese Woche", "Älter"])
        #expect(sections[0].items.map(\.title) == ["Plan für morgen freigegeben", "Neue Nachricht von Sabine Wagner", "Mangel zugewiesen"])
        #expect(sections[0].unreadCount == 2)
        #expect(sections[3].items.count == 2)
    }

    @Test("Zeit in der Zeile wie in Mail")
    func times() {
        #expect(NotificationFeed.timeText(now.addingTimeInterval(-20), now: now, calendar: calendar) == "jetzt")
        #expect(NotificationFeed.timeText(now.addingTimeInterval(-12 * 60), now: now, calendar: calendar) == "vor 12 Min.")
        #expect(NotificationFeed.timeText(now.addingTimeInterval(-3 * 3600), now: now, calendar: calendar) == "07:00")
        #expect(NotificationFeed.timeText(now.addingTimeInterval(-14 * 3600), now: now, calendar: calendar) == "Gestern")
        #expect(NotificationFeed.timeText(now.addingTimeInterval(-40 * 3600), now: now, calendar: calendar) == "Montag")
        #expect(NotificationFeed.timeText(now.addingTimeInterval(-9 * 86_400), now: now, calendar: calendar) == "26.10.")
        #expect(NotificationFeed.timeText(now.addingTimeInterval(-400 * 86_400), now: now, calendar: calendar) == "30.9.25")
        #expect(NotificationFeed.fullTimeText(now, calendar: calendar) == "Mittwoch, 4. November 2026, 10:00 Uhr")
    }

    @Test("Gruppiert nach Art: ungelesene Arten zuerst")
    func byKind() {
        let sections = NotificationFeed.sections(sample, grouping: .kind, now: now, calendar: calendar)
        #expect(sections.first?.title == "Planung")
        #expect(sections.last?.title == "System")
        #expect(Set(sections.map(\.id)).count == sections.count)
    }

    @Test("Filter: Ungelesen, Art und Suche ohne Akzente")
    func filters() {
        #expect(NotificationFeed.filter(sample, NotificationFilter(unreadOnly: true)).count == 4)
        #expect(NotificationFeed.filter(sample, NotificationFilter(kind: .work)).map(\.title) == ["Mangel zugewiesen"])
        #expect(NotificationFeed.filter(sample, NotificationFilter(query: "praxis maier")).count == 1)
        #expect(NotificationFeed.filter(sample, NotificationFilter(query: "SABINE")).count == 2) // Titel und Absender
        #expect(NotificationFeed.filter(sample, NotificationFilter(query: "buro")).count == 1)   // Art „Büro“
        #expect(NotificationFeed.filter(sample, NotificationFilter(unreadOnly: true, kind: .system)).isEmpty)
        #expect(NotificationFilter().isActive == false && NotificationFilter(query: " x ").isActive)
        #expect(NotificationFeed.kinds(in: sample) == [.planning, .bauakte, .work, .chat, .ticket, .office, .system])
        #expect(NotificationFeed.unreadCounts(sample)[.chat] == 1 && NotificationFeed.unreadCounts(sample)[.work] == nil)
        #expect(NotificationFeed.emptyMessage(NotificationFilter(unreadOnly: true)).title == "Alles gelesen")
        #expect(NotificationFeed.emptyMessage(NotificationFilter(query: "xy")).message.contains("„xy“"))
    }

    @Test("Kategorien des Servers und ältere Schreibweisen")
    func kinds() {
        #expect(NotificationKind(category: "approval") == .bauakte)
        #expect(NotificationKind(category: "deadline") == .work)
        #expect(NotificationKind(category: "absence") == .personal)
        #expect(NotificationKind(category: "planning.released") == .planning)
        #expect(NotificationKind(category: "plan") == .planning)
        #expect(NotificationKind(category: "broadcast") == .office)
        #expect(NotificationKind(category: nil) == .office)
        #expect(NotificationKind.symbol(for: "approval") == "checkmark.seal")
        #expect(NotificationKind.symbol(for: "backup.failed") == "externaldrive")
        #expect(NotificationKind.symbol(for: "chat") == NotificationKind.chat.symbol)
    }

    @Test("Ziele aller Links des Servers")
    func targets() {
        let id = UUID(uuidString: "0193a1b2-0000-7000-8000-0000000b0001")!
        let key = id.uuidString.lowercased()
        #expect(NotificationRoute(link: "/bauakten/\(key)/maengel") == .bauakte(id))
        #expect(NotificationRoute(link: "/api/v1/bauakten/\(key)/notes") == .bauakte(id))
        #expect(NotificationRoute(link: "/chat/\(key)") == .chat(id))
        #expect(NotificationRoute(link: "/start", objectType: "workItem", objectId: id) == .workItem(id, bauakteId: nil))
        let task = UUID()
        #expect(NotificationRoute(link: "/bauakten/\(key)", objectType: "workItem", objectId: task) == .workItem(task, bauakteId: id))
        #expect(NotificationRoute(link: "/bauakten/\(key)", objectType: "approval", objectId: task) == .approval(bauakteId: id))
        #expect(NotificationRoute(link: "/personal/urlaub", objectType: "absenceRequest", objectId: task) == .absences(request: task, approvals: false))
        #expect(NotificationRoute(link: "/personal/urlaub?reiter=antraege", objectType: "absenceRequest", objectId: task)
            == .absences(request: task, approvals: true))
        #expect(NotificationRoute(link: "/personal/urlaub?reiter=antraege") == .absences(request: nil, approvals: true))
        // Meldungen im IT-Center sind keine Helpdesk-Tickets.
        #expect(NotificationRoute(link: "/it-center/meldungen/\(key)", objectType: "feedback", objectId: id) == .inbox)
        #expect(NotificationRoute(link: "/personal/urlaub") == .absences(request: nil, approvals: false))
        #expect(NotificationRoute(link: "/system/backup-ziele") == .inbox)

        let system = item(9, "system", "Sicherung fehlgeschlagen", link: "/system/backup-ziele", hoursAgo: 1)
        #expect(system.hasTarget == false && system.webPath == "/system/backup-ziele")
        let bauakte = item(10, "bauakte", "Notiz", link: "/api/v1/bauakten/\(key)/notes", hoursAgo: 1)
        #expect(bauakte.hasTarget && bauakte.webPath == nil)
        #expect(item(11, "message", "Hallo", hoursAgo: 1).hasTarget == false)
    }

    @Test("Absender, Ungelesen und Löschen an die Endpunkte des Servers")
    func requests() async throws {
        let id = UUID(uuidString: "0193a1b2-0000-7000-8000-000000000001")!
        let decoded = try JSONCoding.decoder.decode(InboxNotification.self, from: Data(
            #"{"id":"\#(id.key)","category":"message","title":"Hallo","senderName":"Sabine Wagner","createdAt":"2026-11-03T16:00:00Z"}"#.utf8))
        #expect(decoded.senderName == "Sabine Wagner" && decoded.kind == .office)

        let transport = RoutingTransport()
        transport.on("POST", "/api/v1/notifications/\(id.key)/unread", reply: .noContent)
        transport.on("DELETE", "/api/v1/notifications/\(id.key)", reply: .noContent)
        transport.on("GET", "/api/v1/notifications", reply: .json(#"{"items":[],"unreadCount":0}"#))
        let client = AuthorizedClient(api: APIClient(server: Samples.server, transport: transport), session: StoredSession(Samples.tokens()),
                                      keys: nil, persist: { _ in }, ended: { _ in })
        try await client.markNotificationUnread(id)
        try await client.deleteNotification(id)
        let page = try await client.notifications(limit: 100, before: Date(timeIntervalSince1970: 0))
        #expect(page.items.isEmpty)
        #expect(transport.requests.map(\.httpMethod) == ["POST", "DELETE", "GET"])
        #expect(transport.requests[2].url?.query()?.contains("limit=100") == true)
        #expect(transport.requests[2].url?.query()?.contains("before=1970-01-01") == true)
    }
}
