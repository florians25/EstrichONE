import EstrichCore
import SwiftUI
import UIKit

/// Postfach: Benachrichtigungen des Servers (Plan freigegeben/geändert, Antwort im Helpdesk, Zuweisung, Frist, Nachricht
/// aus dem Büro). Ungelesene sind markiert; Tippen öffnet das Objekt und markiert als gelesen. Oben der Zustand der
/// Pushnachrichten auf diesem Gerät mit Aktivieren bzw. Weg in die Einstellungen.
struct NotificationInboxView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.openURL) private var openURL
    @State private var push = PushCenter.shared
    @State private var items: [InboxNotification] = []
    @State private var loaded = false
    @State private var offline = false
    @State private var target: NotificationRoute?
    @State private var toast: String?

    var body: some View {
        List {
            Section { pushStatus }
                .listRowInsets(EdgeInsets())
                .listRowBackground(Color.clear)
                .listRowSeparator(.hidden)
            if loaded, items.isEmpty {
                Section {
                    EmptyStateView(symbol: "bell", title: offline ? "Ohne Verbindung" : "Keine Benachrichtigungen",
                                   message: offline ? "Das Postfach wird geladen, sobald wieder Verbindung besteht."
                                                    : "Hier erscheinen Freigaben der Planung, Antworten im Helpdesk und Nachrichten aus dem Büro.")
                }
            }
            if !items.isEmpty {
                Section {
                    ForEach(items) { item in
                        Button { open(item) } label: { NotificationRow(item: item) }
                            .buttonStyle(.plain)
                            .swipeActions(edge: .leading) {
                                if !item.isRead {
                                    Button("Gelesen", systemImage: "envelope.open") { Task { await markRead(item) } }
                                        .tint(Theme.accentFill)
                                }
                            }
                    }
                } header: {
                    Text(push.unreadCount > 0 ? "\(push.unreadCount) ungelesen" : "Alle gelesen")
                }
            }
        }
        .listStyle(.insetGrouped)
        .overlay { if !loaded { ProgressView() } }
        .navigationTitle("Benachrichtigungen")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            if push.unreadCount > 0 {
                ToolbarItem(placement: .primaryAction) {
                    Button("Alle gelesen") { Task { await markAllRead() } }
                        .accessibilityIdentifier("markAllRead")
                }
            }
        }
        .navigationDestination(item: $target) { route in NotificationTargetView(route: route) }
        .task { push.refreshPermission() }
        .task(id: model.active?.sync?.revision) { await load() }
        .refreshable { await load() }
        .toast($toast)
    }

    @ViewBuilder
    private var pushStatus: some View {
        switch push.permission {
        case .allowed:
            if let error = push.registrationError, !model.isDemo {
                NoticeLine(tone: .warning, text: "Pushnachrichten aktiv, Registrierung bei Apple fehlgeschlagen: \(error)", symbol: "bell.badge")
            } else {
                NoticeLine(tone: .success, text: "Pushnachrichten sind auf diesem Gerät aktiv.", symbol: "bell.badge.fill",
                           actionTitle: model.isDemo ? "Beispiel senden" : nil,
                           action: model.isDemo ? { sendDemo() } : nil)
            }
        case .denied:
            NoticeLine(tone: .neutral, text: "Pushnachrichten sind ausgeschaltet – Benachrichtigungen stehen nur hier im Postfach.",
                       symbol: "bell.slash", actionTitle: "Einstellungen") {
                if let url = URL(string: UIApplication.openNotificationSettingsURLString) { openURL(url) }
            }
        case .notDetermined:
            Callout(tone: .info, title: "Pushnachrichten aktivieren",
                    message: "Erfahren Sie sofort, wenn der Plan freigegeben oder geändert wird oder das Büro Ihnen schreibt.",
                    symbol: "bell.badge", actionTitle: "Aktivieren") { push.requestPermission() }
        case .unknown:
            EmptyView()
        }
    }

    private func sendDemo() {
        push.scheduleDemoNotification()
        toast = "Beispiel kommt in wenigen Sekunden"
    }

    private func load() async {
        guard let client = model.active?.client else { return }
        do throws(APIError) {
            let inbox = try await client.notifications()
            items = inbox.items.sorted { $0.createdAt > $1.createdAt }
            push.setUnread(inbox.unreadCount)
            offline = false
        } catch {
            offline = error == .offline
            if !offline, error.status != 404 { toast = error.serverMessage ?? error.message }
        }
        loaded = true
    }

    private func open(_ item: InboxNotification) {
        Task { await markRead(item) }
        if let route = item.route, route != .inbox { target = route }
    }

    private func markRead(_ item: InboxNotification) async {
        guard !item.isRead, let index = items.firstIndex(where: { $0.id == item.id }) else { return }
        items[index].readAt = .now
        push.setUnread(max(0, push.unreadCount - 1))
        try? await model.active?.client.markNotificationRead(item.id)
    }

    private func markAllRead() async {
        for index in items.indices where items[index].readAt == nil { items[index].readAt = .now }
        push.setUnread(0)
        try? await model.active?.client.markAllNotificationsRead()
    }
}

/// Eintrag im Postfach: Symbol nach Art, Titel, Text, Zeit; ungelesen mit Punkt und kräftigem Titel.
struct NotificationRow: View {
    let item: InboxNotification

    var body: some View {
        HStack(alignment: .top, spacing: Space.m) {
            IconBadge(symbol: symbol, tint: item.isUrgent ? Theme.warning : Theme.accent,
                      background: item.isUrgent ? Theme.warningSoft : Theme.accentSoft, size: 34)
            VStack(alignment: .leading, spacing: 3) {
                HStack(alignment: .firstTextBaseline, spacing: Space.s) {
                    Text(item.title)
                        .font(item.isRead ? Typo.body : Typo.bodyStrong)
                        .foregroundStyle(Theme.textPrimary)
                        .fixedSize(horizontal: false, vertical: true)
                    Spacer(minLength: Space.s)
                    Text(item.createdAt.formatted(.relative(presentation: .named, unitsStyle: .abbreviated)))
                        .font(Typo.meta)
                        .foregroundStyle(Theme.textSecondary)
                }
                if let body = item.body {
                    Text(body)
                        .font(Typo.subhead)
                        .foregroundStyle(Theme.textSecondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
                if item.isUrgent { Chip(text: "Dringend", symbol: "exclamationmark", tone: .warning) }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            Circle()
                .fill(item.isRead ? Color.clear : Theme.accentFill)
                .frame(width: 9, height: 9)
                .padding(.top, 6)
                .accessibilityHidden(true)
        }
        .padding(.vertical, Space.xs)
        .contentShape(Rectangle())
        .accessibilityElement(children: .combine)
        .accessibilityValue(item.isRead ? "" : "ungelesen")
    }

    private var symbol: String {
        switch item.category {
        case "plan", "planning": "calendar.badge.clock"
        case "ticket", "helpdesk": "bubble.left.and.text.bubble.right"
        case "approval": "checkmark.seal"
        case "assignment": "folder.badge.person.crop"
        case "deadline": "clock.badge.exclamationmark"
        default: "megaphone"
        }
    }
}

/// Ziel einer Benachrichtigung: Bauakte, Meldung, Planung des Tages, Aufgaben oder Postfach.
struct NotificationTargetView: View {
    let route: NotificationRoute

    var body: some View {
        switch route {
        case let .bauakte(id): BauakteDetailView(bauakteId: id)
        case let .ticket(id): TicketDetailView(ticketId: id)
        case let .planning(day): DispatchView(initialDay: Self.date(day))
        case .tasks: MyTasksView()
        case .inbox: NotificationInboxView()
        }
    }

    static func date(_ text: String?) -> Date {
        let today = Calendar.current.startOfDay(for: .now)
        switch text?.lowercased() {
        case nil, "heute": return today
        case "morgen": return Calendar.current.date(byAdding: .day, value: 1, to: today) ?? today
        case let value?: return CalendarDay.date(value) ?? today
        }
    }
}

/// Glocke mit Zahl der ungelesenen Benachrichtigungen (Werkzeugleiste der Heute-Seite).
struct NotificationBell: View {
    let count: Int
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Image(systemName: "bell")
                .foregroundStyle(Theme.accent)
                .overlay(alignment: .topTrailing) {
                    if count > 0 {
                        Text(count > 99 ? "99+" : "\(count)")
                            .font(.caption2.weight(.bold).monospacedDigit())
                            .foregroundStyle(.white)
                            .padding(.horizontal, 4)
                            .frame(minWidth: 17, minHeight: 17)
                            .background(Theme.danger, in: Capsule())
                            .offset(x: 10, y: -8)
                            .fixedSize()
                    }
                }
        }
        .accessibilityLabel(count > 0 ? "Benachrichtigungen, \(count) ungelesen" : "Benachrichtigungen")
        .accessibilityIdentifier("notificationBell")
    }
}
