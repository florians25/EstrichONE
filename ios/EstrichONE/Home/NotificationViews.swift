import EstrichCore
import SwiftUI
import UIKit

/// Postfach wie die Mitteilungszentrale bzw. Mail: Filter (Alle, Ungelesen, je Art), Suche, Abschnitte nach Zeit
/// (Heute, Gestern, Diese Woche, Älter) oder nach Art, kompakte Zeilen mit Symbol in der Farbe der Art, zwei Zeilen
/// Text, Zeit und Ungelesen-Punkt. Tippen öffnet das Ziel (Bauakte, Chat, Meldung, Planung, Aufgaben) bzw. – ohne Ziel in
/// der App – die Einzelansicht. Wischen: gelesen/ungelesen, löschen. Mitteilungseinstellungen über das Menü und unten.
struct NotificationInboxView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.openURL) private var openURL
    @Environment(\.shotScene) private var shotScene
    @State private var push = PushCenter.shared
    @State private var items: [InboxNotification] = []
    @State private var loaded = false
    @State private var offline = false
    @State private var hasMore = false
    @State private var loadingMore = false
    @State private var filter = NotificationFilter()
    @State private var grouping = NotificationGrouping(rawValue: UserDefaults.standard.string(forKey: Self.groupingKey) ?? "") ?? .time
    @State private var target: NotificationRoute?
    @State private var detail: UUID?
    @State private var showsPreferences = false
    @State private var toast: String?
    @State private var width: CGFloat = 0

    private static let groupingKey = "inboxGrouping"
    private static let pageSize = 100

    private var unreadTotal: Int { max(push.unreadCount, items.filter { !$0.isRead }.count) }
    private var visible: [InboxNotification] { NotificationFeed.filter(items, filter) }
    private var sections: [NotificationSection] { NotificationFeed.sections(visible, grouping: grouping) }
    /// Breite Fenster (iPad): Liste mittig in lesbarer Breite statt über die ganze Fläche.
    private var sideMargin: CGFloat { max(0, (width - 860) / 2) }

    var body: some View {
        List {
            if showsPushStatus {
                Section { pushStatus }
                    .listRowInsets(EdgeInsets())
                    .listRowBackground(Color.clear)
                    .listRowSeparator(.hidden)
            }
            if loaded, !items.isEmpty {
                Section { filterBar }
                    .listRowInsets(EdgeInsets())
                    .listRowBackground(Color.clear)
                    .listRowSeparator(.hidden)
            }
            if loaded, visible.isEmpty {
                Section { emptyState }
            }
            ForEach(sections) { section in
                Section {
                    ForEach(section.items) { item in row(item) }
                } header: {
                    sectionHeader(section)
                }
            }
            if hasMore, !visible.isEmpty {
                Section {
                    Button { Task { await loadMore() } } label: {
                        HStack {
                            Label("Ältere laden", systemImage: "clock.arrow.circlepath")
                            Spacer()
                            if loadingMore { ProgressView() }
                        }
                    }
                    .disabled(loadingMore)
                }
            }
            if loaded {
                Section {
                    Button { showsPreferences = true } label: {
                        HStack(spacing: Space.m) {
                            IconBadge(symbol: "slider.horizontal.3", tint: Theme.textSecondary, background: Theme.fill, size: 30)
                            Text("Mitteilungseinstellungen").font(Typo.body).foregroundStyle(Theme.textPrimary)
                            Spacer(minLength: Space.s)
                            Chevron()
                        }
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                } footer: {
                    Text("Welche Ereignisse ins Postfach kommen und welche zusätzlich als Pushnachricht.")
                }
            }
        }
        .listStyle(.insetGrouped)
        .contentMargins(.horizontal, sideMargin, for: .scrollContent)
        .onGeometryChange(for: CGFloat.self) { $0.size.width } action: { width = $0 }
        .overlay { if !loaded { ProgressView() } }
        .animation(.snappy, value: filter)
        .animation(.snappy, value: grouping)
        .searchable(text: $filter.query, placement: .navigationBarDrawer(displayMode: .automatic), prompt: "Benachrichtigungen durchsuchen")
        .navigationTitle("Benachrichtigungen")
        .navigationBarTitleDisplayMode(.large)
        .toolbar {
            if unreadTotal > 0 {
                ToolbarItem(placement: .primaryAction) {
                    // Als Symbol, damit der Titel in der Leiste nicht abgeschnitten wird.
                    Button { Task { await markAllRead() } } label: {
                        Label("Alle als gelesen markieren", systemImage: "checkmark.circle")
                    }
                    .accessibilityIdentifier("markAllRead")
                }
            }
            ToolbarItem(placement: .primaryAction) { optionsMenu }
        }
        .navigationDestination(item: $target) { route in NotificationTargetView(route: route) }
        .navigationDestination(item: $detail) { id in
            if let item = items.first(where: { $0.id == id }) {
                NotificationDetailView(item: item, webURL: webURL(for: item),
                                       open: { route in detail = nil; Task { try? await Task.sleep(for: .milliseconds(350)); target = route } },
                                       toggleRead: { Task { await setRead(item, read: !item.isRead) } },
                                       delete: { detail = nil; Task { await delete(item) } })
            }
        }
        .navigationDestination(isPresented: $showsPreferences) { NotificationPreferencesView() }
        .task { push.refreshPermission() }
        .task(id: model.active?.sync?.revision) { await load() }
        .refreshable { await load() }
        .onChange(of: grouping) { _, new in
            if shotScene == nil { UserDefaults.standard.set(new.rawValue, forKey: Self.groupingKey) }
        }
        .toast($toast)
    }

    // MARK: Teile

    private var optionsMenu: some View {
        Menu {
            Picker("Gruppieren", selection: $grouping) {
                ForEach(NotificationGrouping.allCases) { option in
                    Label(option.title, systemImage: option == .time ? "clock" : "square.grid.2x2").tag(option)
                }
            }
            Section {
                Button { Task { await markAllRead() } } label: { Label("Alle als gelesen markieren", systemImage: "checkmark.circle") }
                    .disabled(unreadTotal == 0)
                if filter.isActive {
                    Button { filter = NotificationFilter() } label: { Label("Filter zurücksetzen", systemImage: "line.3.horizontal.decrease.circle") }
                }
            }
            Section {
                Button { showsPreferences = true } label: { Label("Mitteilungseinstellungen", systemImage: "slider.horizontal.3") }
                    .accessibilityIdentifier("notificationPreferences")
                if model.isDemo, push.permission == .allowed {
                    Button { sendDemo() } label: { Label("Beispiel-Pushnachricht senden", systemImage: "bell.badge") }
                }
            }
        } label: {
            Label("Optionen", systemImage: "ellipsis.circle")
        }
        .accessibilityIdentifier("inboxOptions")
    }

    /// Filter-Chips: Alle, Ungelesen (mit Zahl) und je vorhandener Art (mit Zahl der ungelesenen).
    private var filterBar: some View {
        let counts = NotificationFeed.unreadCounts(items)
        return ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: Space.s) {
                FilterChip(title: "Alle", symbol: "tray", count: 0, isSelected: !filter.unreadOnly && filter.kind == nil) {
                    filter.unreadOnly = false
                    filter.kind = nil
                }
                FilterChip(title: "Ungelesen", symbol: "circle.fill", count: unreadTotal, isSelected: filter.unreadOnly) {
                    filter.unreadOnly.toggle()
                }
                .accessibilityIdentifier("filterUnread")
                Divider().frame(height: 22)
                ForEach(NotificationFeed.kinds(in: items)) { kind in
                    FilterChip(title: kind.title, symbol: kind.symbol, count: counts[kind] ?? 0, tint: kind.tint, isSelected: filter.kind == kind) {
                        filter.kind = filter.kind == kind ? nil : kind
                    }
                    .accessibilityIdentifier("filter-\(kind.rawValue)")
                }
            }
            .padding(.horizontal, Space.l)
            .padding(.vertical, Space.xxs)
        }
        .scrollClipDisabled()
        .accessibilityElement(children: .contain)
        .accessibilityLabel("Filter")
    }

    private func sectionHeader(_ section: NotificationSection) -> some View {
        HStack(spacing: Space.s) {
            if let symbol = section.symbol {
                Image(systemName: symbol).imageScale(.small).accessibilityHidden(true)
            }
            Text(section.title)
            Spacer()
            if section.unreadCount > 0 {
                Text("\(section.unreadCount) neu")
                    .font(Typo.metaStrong)
                    .foregroundStyle(Theme.accent)
                    .textCase(nil)
            }
        }
        .accessibilityElement(children: .combine)
    }

    private func row(_ item: InboxNotification) -> some View {
        Button { open(item) } label: { NotificationRow(item: item) }
            .buttonStyle(.plain)
            .accessibilityIdentifier("notification-\(item.id.uuidString.lowercased())")
            .accessibilityHint(item.hasTarget ? "Öffnet das Objekt" : "Zeigt die Benachrichtigung")
            .swipeActions(edge: .leading, allowsFullSwipe: true) {
                Button { Task { await setRead(item, read: !item.isRead) } } label: {
                    Label(item.isRead ? "Ungelesen" : "Gelesen", systemImage: item.isRead ? "envelope.badge" : "envelope.open")
                }
                .tint(Theme.accentFill)
            }
            .swipeActions(edge: .trailing, allowsFullSwipe: true) {
                Button(role: .destructive) { Task { await delete(item) } } label: { Label("Löschen", systemImage: "trash") }
            }
            .contextMenu {
                if item.hasTarget, let route = item.route {
                    Button { Task { await setRead(item, read: true) }; target = route } label: { Label("Öffnen", systemImage: "arrow.up.forward.app") }
                }
                Button { detail = item.id } label: { Label("Details", systemImage: "text.alignleft") }
                Button { Task { await setRead(item, read: !item.isRead) } } label: {
                    Label(item.isRead ? "Als ungelesen markieren" : "Als gelesen markieren", systemImage: item.isRead ? "envelope.badge" : "envelope.open")
                }
                if filter.kind != item.kind {
                    Button { filter.kind = item.kind } label: { Label("Nur „\(item.kind.title)“ zeigen", systemImage: item.kind.symbol) }
                }
                Divider()
                Button(role: .destructive) { Task { await delete(item) } } label: { Label("Löschen", systemImage: "trash") }
            }
    }

    @ViewBuilder
    private var emptyState: some View {
        let text = offline && items.isEmpty
            ? (title: "Ohne Verbindung", message: "Das Postfach wird geladen, sobald wieder Verbindung besteht.")
            : NotificationFeed.emptyMessage(filter)
        if filter.isActive {
            EmptyStateView(symbol: filter.unreadOnly ? "checkmark.circle" : "magnifyingglass", title: text.title, message: text.message,
                           actionTitle: "Filter zurücksetzen", actionSymbol: "line.3.horizontal.decrease.circle") { filter = NotificationFilter() }
        } else {
            EmptyStateView(symbol: offline ? "wifi.slash" : "bell", title: text.title, message: text.message)
        }
    }

    /// Push-Zustand nur, wenn etwas zu tun ist (nicht erlaubt, nicht gefragt, Fehler) – sonst bleibt das Postfach ruhig.
    private var showsPushStatus: Bool {
        switch push.permission {
        case .allowed: push.registrationError != nil && !model.isDemo
        case .denied, .notDetermined: true
        case .unknown: false
        }
    }

    @ViewBuilder
    private var pushStatus: some View {
        switch push.permission {
        case .allowed:
            if let error = push.registrationError {
                NoticeLine(tone: .warning, text: "Pushnachrichten aktiv, Registrierung bei Apple fehlgeschlagen: \(error)", symbol: "bell.badge")
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

    // MARK: Aktionen

    private func webURL(for item: InboxNotification) -> URL? {
        guard !model.isDemo, let path = item.webPath, let base = model.server?.address.baseURL else { return nil }
        return URL(string: path, relativeTo: base)?.absoluteURL
    }

    private func sendDemo() {
        push.scheduleDemoNotification()
        toast = "Beispiel kommt in wenigen Sekunden"
    }

    private func load() async {
        guard let client = model.active?.client else { return }
        do throws(APIError) {
            let inbox = try await client.notifications(limit: Self.pageSize)
            items = inbox.items.sorted { $0.createdAt > $1.createdAt }
            hasMore = inbox.items.count >= Self.pageSize
            push.setUnread(inbox: inbox)
            offline = false
        } catch {
            offline = error == .offline
            if !offline, error.status != 404 { toast = error.serverMessage ?? error.message }
        }
        if !loaded { applyShotScene() }
        loaded = true
    }

    private func loadMore() async {
        guard let client = model.active?.client, let oldest = items.last?.createdAt else { return }
        loadingMore = true
        defer { loadingMore = false }
        do throws(APIError) {
            let page = try await client.notifications(limit: Self.pageSize, before: oldest)
            let known = Set(items.map(\.id))
            items += page.items.filter { !known.contains($0.id) }.sorted { $0.createdAt > $1.createdAt }
            hasMore = page.items.count >= Self.pageSize
        } catch {
            toast = error.serverMessage ?? error.message
        }
    }

    private func open(_ item: InboxNotification) {
        Task { await setRead(item, read: true) }
        if item.hasTarget, let route = item.route {
            target = route
        } else {
            detail = item.id
        }
    }

    private func setRead(_ item: InboxNotification, read: Bool) async {
        guard let index = items.firstIndex(where: { $0.id == item.id }), items[index].isRead != read else { return }
        items[index].readAt = read ? .now : nil
        push.setUnread(max(0, push.unreadCount + (read ? -1 : 1)))
        guard let client = model.active?.client else { return }
        do throws(APIError) {
            if read { try await client.markNotificationRead(item.id) } else { try await client.markNotificationUnread(item.id) }
        } catch {
            if error != .offline { toast = error.serverMessage ?? error.message }
        }
    }

    private func delete(_ item: InboxNotification) async {
        guard let index = items.firstIndex(where: { $0.id == item.id }) else { return }
        let removed = items.remove(at: index)
        if !removed.isRead { push.setUnread(max(0, push.unreadCount - 1)) }
        do throws(APIError) {
            try await model.active?.client.deleteNotification(item.id)
        } catch {
            // Nicht gelöscht (z. B. offline): wieder einsortieren.
            if error.status != 404 {
                items.insert(removed, at: min(index, items.count))
                if !removed.isRead { push.setUnread(push.unreadCount + 1) }
                toast = error == .offline ? "Ohne Verbindung – nicht gelöscht" : (error.serverMessage ?? error.message)
            }
        }
    }

    private func markAllRead() async {
        for index in items.indices where items[index].readAt == nil { items[index].readAt = .now }
        push.setUnread(0)
        try? await model.active?.client.markAllNotificationsRead()
    }

    /// Screenshot-Modus: Filter, Gruppierung oder Einzelansicht zeigen.
    private func applyShotScene() {
        switch shotScene {
        case "postfach-ungelesen": filter.unreadOnly = true
        case "postfach-art": grouping = .kind
        case "postfach-suche": filter.query = "Praxis"
        case "postfach-detail": detail = items.first { !$0.hasTarget && $0.body != nil }?.id
        case let scene? where scene.hasPrefix("postfach"): grouping = .time
        default: break
        }
    }
}

/// Filter-Chip im Postfach: Symbol, Name und – falls vorhanden – Zahl der ungelesenen.
private struct FilterChip: View {
    let title: String
    let symbol: String
    let count: Int
    var tint: Color = Theme.accent
    let isSelected: Bool
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            HStack(spacing: 6) {
                Image(systemName: symbol)
                    .imageScale(.small)
                    .foregroundStyle(isSelected ? Theme.onAccent : tint)
                    .accessibilityHidden(true)
                Text(title).lineLimit(1)
                if count > 0 {
                    Text(UnreadBadge.text(count) ?? "")
                        .font(Typo.labelStrong.monospacedDigit())
                        .padding(.horizontal, 6)
                        .padding(.vertical, 1)
                        .background(isSelected ? Color.white.opacity(0.25) : Theme.fillStrong, in: Capsule())
                }
            }
            .font(Typo.subheadStrong)
            .foregroundStyle(isSelected ? Theme.onAccent : Theme.textPrimary)
            .padding(.horizontal, Space.m)
            .frame(minHeight: 36)
            .background(isSelected ? AnyShapeStyle(Theme.accentFill) : AnyShapeStyle(Theme.surface), in: Capsule())
            .overlay(Capsule().strokeBorder(Theme.separator, lineWidth: isSelected ? 0 : 1))
            .contentShape(Capsule())
        }
        .buttonStyle(.plain)
        .frame(minHeight: Size.minTap)
        .accessibilityLabel(count > 0 ? "\(title), \(count) ungelesen" : title)
        .accessibilityAddTraits(isSelected ? .isSelected : [])
    }
}

extension NotificationKind {
    /// Farbe der Art (Symbol in der Zeile, Chip) – Systemfarben, hell und dunkel passend.
    var tint: Color {
        switch self {
        case .planning: Theme.accent
        case .bauakte: Color(uiColor: .systemTeal)
        case .work: Color(uiColor: .systemOrange)
        case .chat: Color(uiColor: .systemGreen)
        case .ticket: Color(uiColor: .systemPurple)
        case .office: Color(uiColor: .systemIndigo)
        case .personal: Color(uiColor: .systemPink)
        case .system: Color(uiColor: .systemGray)
        }
    }
}

/// Eintrag im Postfach (wie Mail): Ungelesen-Punkt, Symbol in der Farbe der Art, Titel (fett, solange ungelesen),
/// Zeit, zwei Zeilen Text, darunter Art bzw. Absender und „Dringend“.
struct NotificationRow: View {
    let item: InboxNotification
    @Environment(\.dynamicTypeSize) private var typeSize

    var body: some View {
        HStack(alignment: .top, spacing: Space.s) {
            Circle()
                .fill(item.isRead ? Color.clear : Theme.accentFill)
                .frame(width: 8, height: 8)
                .padding(.top, 13)
                .accessibilityHidden(true)
            IconBadge(symbol: NotificationKind.symbol(for: item.category), tint: item.isUrgent ? Theme.warning : item.kind.tint,
                      background: (item.isUrgent ? Theme.warning : item.kind.tint).opacity(0.14), size: 34)
            VStack(alignment: .leading, spacing: 2) {
                HStack(alignment: .firstTextBaseline, spacing: Space.s) {
                    Text(item.title)
                        .font(item.isRead ? Typo.body : Typo.bodyStrong)
                        .foregroundStyle(Theme.textPrimary)
                        .lineLimit(typeSize.isAccessibilitySize ? 4 : 2)
                    Spacer(minLength: Space.xs)
                    Text(NotificationFeed.timeText(item.createdAt))
                        .font(Typo.meta)
                        .foregroundStyle(item.isRead ? Theme.textTertiary : Theme.accent)
                        .lineLimit(1)
                        .fixedSize()
                }
                if let body = item.body, !body.isEmpty {
                    Text(body)
                        .font(Typo.subhead)
                        .foregroundStyle(Theme.textSecondary)
                        .lineLimit(typeSize.isAccessibilitySize ? 4 : 2)
                }
                HStack(spacing: Space.s) {
                    Text(item.senderName.map { "\(item.kind.title) · \($0)" } ?? item.kind.title)
                        .font(Typo.label)
                        .foregroundStyle(item.kind.tint)
                        .lineLimit(1)
                    if item.isUrgent {
                        Label("Dringend", systemImage: "exclamationmark.circle.fill")
                            .font(Typo.labelStrong)
                            .foregroundStyle(Theme.warning)
                            .labelStyle(.titleAndIcon)
                    }
                    Spacer(minLength: 0)
                    if item.hasTarget {
                        Image(systemName: "chevron.right")
                            .font(.caption2.weight(.semibold))
                            .foregroundStyle(Theme.textTertiary)
                            .accessibilityHidden(true)
                    }
                }
                .padding(.top, 1)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .padding(.vertical, 3)
        .padding(.leading, -Space.xs)
        .contentShape(Rectangle())
        .accessibilityElement(children: .combine)
        .accessibilityValue([item.isRead ? nil : "ungelesen", item.isUrgent ? "dringend" : nil].compactMap { $0 }.joined(separator: ", "))
    }
}

/// Einzelansicht: ganzer Text, Art, Absender, Zeitpunkt; Öffnen, im Browser öffnen, gelesen/ungelesen, löschen.
struct NotificationDetailView: View {
    let item: InboxNotification
    let webURL: URL?
    let open: (NotificationRoute) -> Void
    let toggleRead: () -> Void
    let delete: () -> Void
    @Environment(\.openURL) private var openURL
    @State private var confirmDelete = false

    var body: some View {
        List {
            Section {
                VStack(alignment: .leading, spacing: Space.m) {
                    HStack(spacing: Space.m) {
                        IconBadge(symbol: NotificationKind.symbol(for: item.category), tint: item.kind.tint,
                                  background: item.kind.tint.opacity(0.14), size: 44)
                        VStack(alignment: .leading, spacing: 2) {
                            Text(item.kind.title).font(Typo.subheadStrong).foregroundStyle(item.kind.tint)
                            Text(NotificationFeed.fullTimeText(item.createdAt)).font(Typo.meta).foregroundStyle(Theme.textSecondary)
                        }
                    }
                    Text(item.title)
                        .font(Typo.title)
                        .foregroundStyle(Theme.textPrimary)
                        .fixedSize(horizontal: false, vertical: true)
                        .accessibilityAddTraits(.isHeader)
                    if let body = item.body, !body.isEmpty {
                        Text(body)
                            .font(Typo.body)
                            .foregroundStyle(Theme.textPrimary)
                            .textSelection(.enabled)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                    HStack(spacing: Space.s) {
                        if let sender = item.senderName { Chip(text: sender, symbol: "person.fill") }
                        if item.isUrgent { Chip(text: "Dringend", symbol: "exclamationmark", tone: .warning) }
                        Chip(text: item.isRead ? "Gelesen" : "Ungelesen", symbol: item.isRead ? "envelope.open" : "envelope.badge",
                             tone: item.isRead ? .neutral : .accent)
                    }
                }
                .padding(.vertical, Space.xs)
            }
            Section {
                if item.hasTarget, let route = item.route {
                    Button { open(route) } label: { Label(Self.openTitle(route), systemImage: "arrow.up.forward.app") }
                }
                if let webURL {
                    Button { openURL(webURL) } label: { Label("Im Browser öffnen", systemImage: "safari") }
                }
                Button(action: toggleRead) {
                    Label(item.isRead ? "Als ungelesen markieren" : "Als gelesen markieren", systemImage: item.isRead ? "envelope.badge" : "envelope.open")
                }
                Button(role: .destructive) { confirmDelete = true } label: { Label("Löschen", systemImage: "trash") }
            } footer: {
                if !item.hasTarget, webURL == nil {
                    Text("Zu dieser Benachrichtigung gibt es in der App keine eigene Seite.")
                }
            }
        }
        .listStyle(.insetGrouped)
        .navigationTitle(item.kind.title)
        .navigationBarTitleDisplayMode(.inline)
        .confirmationDialog("Benachrichtigung löschen?", isPresented: $confirmDelete, titleVisibility: .visible) {
            Button("Löschen", role: .destructive, action: delete)
        }
        .accessibilityIdentifier("notificationDetail")
    }

    static func openTitle(_ route: NotificationRoute) -> String {
        switch route {
        case .bauakte: "Bauakte öffnen"
        case .ticket: "Meldung öffnen"
        case .planning: "Planung öffnen"
        case .tasks: "Aufgaben öffnen"
        case .chat: "Chat öffnen"
        case .inbox: "Postfach öffnen"
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
