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

    private var symbol: String { NotificationKind.symbol(for: item.category) }
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
        case let .chat(id?): ChatConversationView(conversationId: id)
        case .chat(nil): ChatListView()
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
///
/// Der Zähler sitzt innerhalb der Schaltfläche (oben rechts an der Glocke), damit ihn die Werkzeugleiste nicht
/// abschneidet: rund bei einer Ziffer, Kapsel bei „12“ oder „99+“, mit Rand in Hintergrundfarbe für Kontrast hell
/// und dunkel. Gleiche Zahl wie am App-Symbol (`PushCenter.setUnread`).
struct NotificationBell: View {
    let count: Int
    let action: () -> Void

    /// Breite der Schaltfläche: Glocke plus Platz für den Zähler („99+“ ragt weiter nach rechts).
    private var width: CGFloat {
        switch UnreadBadge.text(count)?.count ?? 0 {
        case 0: 22
        case 1: 28
        case 2: 32
        default: 38
        }
    }

    var body: some View {
        Button(action: action) {
            // Alles innerhalb von 22 pt Höhe: Die Werkzeugleiste (Liquid Glass) beschneidet höhere Inhalte.
            ZStack(alignment: .topTrailing) {
                Image(systemName: "bell")
                    .font(.system(size: 16, weight: .medium))
                    .foregroundStyle(Theme.accent)
                    .frame(width: 20, height: 20)
                    .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: count > 0 ? .bottomLeading : .center)
                if let text = UnreadBadge.text(count) {
                    CountBadge(text: text)
                }
            }
            .frame(width: width, height: 22)
            .contentShape(Rectangle())
            .animation(.snappy, value: count)
        }
        .accessibilityLabel(count > 0 ? UnreadBadge.accessibilityLabel(count) : "Benachrichtigungen")
        .accessibilityHint("Öffnet das Postfach")
        .accessibilityIdentifier("notificationBell")
    }
}

/// Runder Zähler (Glocke, Listen): weiße Ziffern auf kräftigem Rot, Rand in Hintergrundfarbe, feste Größe auch bei
/// großer Schrift (die Zahl steht zusätzlich im Bedienungshilfen-Label).
struct CountBadge: View {
    let text: String

    var body: some View {
        Text(text)
            .font(.system(size: 9.5, weight: .bold, design: .rounded).monospacedDigit())
            .foregroundStyle(.white)
            .lineLimit(1)
            .fixedSize()
            .padding(.horizontal, text.count > 1 ? 3.5 : 0)
            .frame(minWidth: 15, minHeight: 15)
            .background(Capsule().fill(Theme.badge))
            .overlay(Capsule().strokeBorder(Color(uiColor: .systemBackground), lineWidth: 1.2).padding(-1.2))
            .accessibilityHidden(true)
            .transition(.scale.combined(with: .opacity))
    }
}

/// Symbol je Art der Benachrichtigung (docs/benachrichtigungen.md). Unbekannte Arten zeigen das Megafon.
enum NotificationKind {
    static func symbol(for category: String?) -> String {
        let key = (category ?? "").lowercased()
        switch key {
        case "plan", "planning", "planchange", "planning.changed", "planning.released": return "calendar.badge.clock"
        case "ticket", "helpdesk": return "bubble.left.and.text.bubble.right"
        case "chat", "chatmessage", "chat.message": return "bubble.left.and.bubble.right"
        case "approval", "approval.requested", "approval.decided", "document.approval": return "checkmark.seal"
        case "assignment", "bauakte", "bauakte.assigned": return "folder.badge.person.crop"
        case "defect", "defect.assigned", "mangel": return "exclamationmark.triangle"
        case "comment", "reply", "bauakte.comment": return "text.bubble"
        case "acceptance", "acceptance.signed", "abnahme": return "signature"
        case "leave", "absence", "vacation", "leave.decided": return "sun.max"
        case "maintenance", "maintenance.announced": return "wrench.and.screwdriver"
        case "backup", "backup.failed", "backup.succeeded", "archive", "archive.failed", "system": return "externaldrive.badge.checkmark"
        case "deadline": return "clock.badge.exclamationmark"
        default:
            if key.hasPrefix("backup") || key.hasPrefix("archive") { return "externaldrive" }
            if key.hasPrefix("chat") { return "bubble.left.and.bubble.right" }
            if key.hasPrefix("defect") { return "exclamationmark.triangle" }
            if key.hasPrefix("leave") { return "sun.max" }
            return "megaphone"
        }
    }
}
