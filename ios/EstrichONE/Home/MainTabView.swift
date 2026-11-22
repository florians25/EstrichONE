import EstrichCore
import SwiftUI

/// Hauptnavigation: Heute, Baustellen, Chat, Kunden, Mehr. Erfasst wird ausschließlich in der Bauakte (Knopf „Erfassen“
/// unten in der Akte); „Heute“ bietet für den Einsatz Foto und Bautagebuch direkt an. Der Chat ist ein eigener Reiter mit
/// Zähler der ungelesenen Nachrichten. Ab iOS 26 erscheint die Leiste in Liquid Glass; auf dem iPad als einblendbare Seitenleiste.
struct MainTabView: View {
    enum Tab: Hashable {
        case today
        case bauakten
        case chat
        case customers
        case more
    }

    @Environment(AppModel.self) private var model
    @Environment(\.shotScene) private var shotScene
    @State private var selection: Tab = .today
    /// Meldungen mit neuer Antwort der IT (Abzeichen am Reiter „Mehr“).
    @State private var unreadTickets = 0
    @State private var push = PushCenter.shared
    @State private var chatNavigation = ChatNavigation.shared
    /// Erfassen im Modus „Nur Ansicht“: erst bestätigen lassen.
    @State private var confirmsOverride = false
    /// Schnellaktion vom App-Symbol, aus Kurzbefehlen oder per Siri.
    @State private var quick = QuickActionCenter.shared
    @State private var quickCamera: Bauakte?
    @State private var quickDiary: Bauakte?
    /// Schnellaktion ohne heutigen Einsatz und ohne zuletzt geöffnete Bauakte: erst die Baustelle wählen.
    @State private var quickPick: QuickAction?
    @State private var quickTodayIds: [UUID] = []
    @State private var toast: String?

    /// Reiter „Chat“: solange der Chat nicht abgeschaltet ist (vor dem ersten Abgleich angenommen).
    private var showsChat: Bool { model.active?.sync?.chat?.isAvailable ?? true }

    private var chatUnread: Int {
        guard let chat = model.active?.sync?.chat, chat.isAvailable, !chat.readOnly else { return 0 }
        return chat.unreadCount
    }

    var body: some View {
        // Backup-/Archiv-Störung und „Nur Ansicht“ über allen Reitern (Navigationsleisten darunter).
        VStack(spacing: 0) {
        if let sync = model.active?.sync { UpdateAnnounceBanner(sync: sync) }
        if let sync = model.active?.sync { SystemStatusStrip(sync: sync, showsNotices: selection != .today) }
        TabView(selection: $selection) {
            HomeView { conversation in openChat(conversation) }
                .tabItem { Label("Heute", systemImage: "sun.max") }
                .tag(Tab.today)
            BauaktenListView()
                .tabItem { Label("Baustellen", systemImage: "building.2") }
                .tag(Tab.bauakten)
            if showsChat {
                ChatTabView()
                    .tabItem { Label("Chat", systemImage: "bubble.left.and.bubble.right") }
                    .tag(Tab.chat)
                    .badge(chatUnread)
                    .accessibilityIdentifier("chatTab")
            }
            CustomerDirectoryView()
                .tabItem { Label("Kunden", systemImage: "person.2") }
                .tag(Tab.customers)
            MoreView()
                .tabItem { Label("Mehr", systemImage: "ellipsis.circle") }
                .tag(Tab.more)
                .badge(unreadTickets)
        }
        .modifier(SidebarAdaptable())
        }
        // Update-Ablauf: Wartungsbildschirm über allem, solange das Update läuft (ohne Fehlermeldungen oder Abmelde-Schleifen).
        .overlay { if let sync = model.active?.sync { UpdateMaintenanceScreen(sync: sync) } }
        .readOnlyOverrideDialog(isPresented: $confirmsOverride, sync: model.active?.sync)
        .onChange(of: showsChat) { _, visible in
            if !visible, selection == .chat { selection = .today }
        }
        .task { applyShotScene() }
        .task(id: quick.pending) { await runQuickAction() }
        .fullScreenCover(item: $quickCamera) { bauakte in
            CameraView(bauakte: bauakte) { count in
                quickCamera = nil
                if count > 0 { toast = count == 1 ? "Foto gespeichert" : "\(count) Fotos gespeichert" }
            }
        }
        .sheet(item: $quickDiary) { bauakte in
            NewDiaryEntryView(bauakteId: bauakte.id) { quickDiary = nil }
        }
        .sheet(item: $quickPick) { action in
            BauakteQuickPicker(title: action == .photo ? "Foto: Baustelle wählen" : "Bautagebuch: Baustelle wählen", todayIds: quickTodayIds) { bauakte in
                quickPick = nil
                guard let bauakte else { return }
                Task {
                    // Erst wenn die Auswahl geschlossen ist, Kamera bzw. Formular öffnen.
                    try? await Task.sleep(for: .milliseconds(450))
                    if action == .photo { quickCamera = bauakte } else { quickDiary = bauakte }
                }
            }
        }
        .toast($toast)
        .task(id: model.active?.sync?.revision) {
            unreadTickets = await TicketReadState.unreadCount(model: model)
            if let sync = model.active?.sync, sync.chat == nil { await sync.refreshChat() }
            if let inbox = try? await model.active?.client.notifications() { push.setUnread(inbox: inbox) }
        }
        .task { push.refreshPermission() }
        // Neues APNs-Token (oder nach der Anmeldung) beim Server registrieren.
        .task(id: push.token) { await push.upload(model: model) }
        .task(id: push.pendingReadId) {
            guard let id = push.pendingReadId, let client = model.active?.client else { return }
            try? await client.markNotificationRead(id)
            push.pendingReadId = nil
            if let inbox = try? await client.notifications() { push.setUnread(inbox: inbox) }
        }
        // Chat-Mitteilung angetippt: im Reiter „Chat“ öffnen (nicht als Blatt über einem anderen Reiter).
        .task(id: push.pendingRoute) {
            guard showsChat, case let .chat(conversation) = push.pendingRoute else { return }
            push.pendingRoute = nil
            openChat(conversation)
        }
        // Übrige Benachrichtigungen: Objekt als Blatt über dem aktuellen Reiter öffnen.
        .sheet(item: Binding(get: { sheetRoute }, set: { push.pendingRoute = $0 })) { route in
            NavigationStack {
                NotificationTargetView(route: route)
                    .toolbar {
                        ToolbarItem(placement: .cancellationAction) { Button("Schließen") { push.pendingRoute = nil } }
                    }
            }
        }
    }

    /// Benachrichtigung als Blatt; Chats öffnet der Reiter „Chat“.
    private var sheetRoute: NotificationRoute? {
        if showsChat, case .chat = push.pendingRoute { return nil }
        return push.pendingRoute
    }

    /// Reiter „Chat“ zeigen und dort die Unterhaltung öffnen (`nil` = Liste).
    private func openChat(_ conversation: UUID?) {
        guard showsChat else { return }
        selection = .chat
        chatNavigation.open(conversation)
    }

    /// Schnellaktion ausführen: Foto bzw. Bautagebuch für den heutigen Einsatz, sonst die zuletzt geöffnete Bauakte;
    /// ohne beides wird die Baustelle gewählt. Erfasst wird immer zu einer Bauakte.
    private func runQuickAction() async {
        guard let action = quick.pending, let service = model.active?.service else { return }
        quick.pending = nil
        if action == .search {
            selection = .bauakten
            return
        }
        if model.active?.sync?.isReadOnly == true {
            confirmsOverride = true
            return
        }
        if action == .diary, model.active?.sync?.modules.isOn(AppModules.diary) == false {
            toast = "Das Bautagebuch ist in Ihrem Betrieb nicht eingeschaltet."
            return
        }
        quickTodayIds = await TodayContext.bauakteIds(model: model)
        var target: Bauakte?
        if let id = quickTodayIds.first {
            target = try? await service.bauakte(id)
        }
        if target == nil {
            target = try? await service.recentBauakten(limit: 1).first
        }
        guard let target else {
            quickPick = action
            return
        }
        if action == .photo { quickCamera = target } else { quickDiary = target }
    }

    /// Screenshot-Modus: passenden Reiter wählen.
    private func applyShotScene() {
        guard let scene = shotScene else { return }
        let tab = ShotRoute.tab(for: scene)
        selection = tab == .chat && !showsChat ? .more : tab
    }
}

/// Reiter „Chat“: Unterhaltungen als Startseite des Reiters (mit „Neuer Chat“ und Microsoft Teams in der Leiste).
private struct ChatTabView: View {
    var body: some View {
        NavigationStack {
            ChatListView(isTab: true)
                .rootToolbar()
        }
    }
}

/// Öffnet eine Unterhaltung im Reiter „Chat“ (aus „Heute“ oder einer Mitteilung).
@MainActor
@Observable
final class ChatNavigation {
    static let shared = ChatNavigation()

    /// Unterhaltung, die der Reiter als Nächstes öffnet.
    var pendingConversation: UUID?
    /// Zähler, damit dieselbe Unterhaltung auch ein zweites Mal geöffnet wird.
    private(set) var request = 0

    func open(_ conversation: UUID?) {
        pendingConversation = conversation
        request += 1
    }
}

/// Zuordnung der Screenshot-Bildschirme zu den Reitern.
enum ShotRoute {
    static func tab(for scene: String) -> MainTabView.Tab {
        let prefix = scene.split(separator: "-").first.map(String.init) ?? scene
        switch prefix {
        case "baustellen", "bauakte", "bereich", "neu", "erstbegehung", "abnahme", "foto", "kamera", "raum", "tagebuch", "mangel", "basisdaten", "protokoll", "kontakt": return .bauakten
        case "kunden", "kundenkarte": return .customers
        case "chat": return .chat
        case "mehr", "sicherheit", "sync", "meldung", "aufgaben", "woche", "abwesenheit", "disposition", "dispo", "postfach", "tankbelege", "tankbeleg": return .more
        default: return .today
        }
    }
}

/// iPad ab iOS 18: Reiter als einblendbare Seitenleiste.
private struct SidebarAdaptable: ViewModifier {
    func body(content: Content) -> some View {
        if #available(iOS 18, *) {
            // Standard ist die Reiterleiste oben (volle Breite für Listen und Details, auch quer); die Seitenleiste
            // lässt sich über die Schaltfläche links in der Leiste einblenden.
            content.tabViewStyle(.sidebarAdaptable)
                .defaultAdaptableTabBarPlacement(.tabBar)
        } else {
            content
        }
    }
}

/// Gemeinsame Werkzeugleiste der Reiter-Startseiten: im Demomodus eine Kennzeichnung mit „Beenden“, bei Wartung ein
/// Hinweis. Nichts liegt über der Statusleiste oder verdeckt Inhalte.
struct RootToolbar: ViewModifier {
    /// Wartungssymbol oben links; die Heute-Seite zeigt die Wartung selbst (Hinweis und Abgleich-Symbol).
    var showsMaintenance = true
    @Environment(AppModel.self) private var model
    @State private var explainsMaintenance = false

    func body(content: Content) -> some View {
        content
            .toolbar {
                if model.isDemo, !ShotOptions.hidesDemoMarks {
                    ToolbarItem(placement: .topBarLeading) {
                        Menu {
                            Section("Demomodus mit Beispieldaten. Es werden keine Daten gesendet.") {
                                Button("Demo beenden", systemImage: "xmark.circle", role: .destructive) { model.requestDemo(false) }
                                    .accessibilityIdentifier("endDemoButton")
                            }
                            DemoDisruptionMenu()
                        } label: {
                            Text("Demo")
                                .font(.subheadline.weight(.semibold))
                                .foregroundStyle(Theme.accent)
                        }
                        .accessibilityLabel("Demomodus")
                        .accessibilityIdentifier("demoBanner")
                    }
                }
                if showsMaintenance, let message = maintenanceMessage {
                    ToolbarItem(placement: .topBarLeading) {
                        Button { explainsMaintenance = true } label: {
                            Label("Wartung", systemImage: "wrench.and.screwdriver.fill")
                                .foregroundStyle(Theme.warning)
                        }
                        .accessibilityIdentifier("maintenanceBanner")
                        .accessibilityHint(message)
                    }
                }
            }
            .alert("Wartung des Servers", isPresented: $explainsMaintenance) {
                Button("OK", role: .cancel) {}
            } message: {
                Text("\(maintenanceMessage ?? "Der Server wird gewartet.")\n\nAls Administrator können Sie weiterarbeiten; für alle anderen ist die App bis zum Ende der Wartung gesperrt.")
            }
    }

    private var maintenanceMessage: String? {
        guard let sync = model.active?.sync else { return nil }
        if let message = sync.maintenanceMessage { return message }
        if let maintenance = sync.notices?.maintenance, maintenance.active { return maintenance.message ?? "Der Server wird gewartet." }
        return nil
    }
}

extension View {
    func rootToolbar(showsMaintenance: Bool = true) -> some View { modifier(RootToolbar(showsMaintenance: showsMaintenance)) }
}

/// Demomodus: Störungen vorführen, wie sie im Betrieb auftreten können. Der Demoserver antwortet dann wie der echte.
private struct DemoDisruptionMenu: View {
    @Environment(AppModel.self) private var model
    @State private var state = DemoSwitchState()

    var body: some View {
        Section("Störungen vorführen") {
            Toggle("Backup-Server ausgefallen", systemImage: "externaldrive.badge.exclamationmark", isOn: binding(.backup))
            Toggle("Archiv-Server ausgefallen", systemImage: "archivebox", isOn: binding(.archive))
            Toggle("Server nicht erreichbar (Nur Ansicht)", systemImage: "wifi.slash", isOn: binding(.outage))
            Button("Wartung starten (sperrt die App)", systemImage: "wrench.and.screwdriver") {
                DemoSwitches.shared.startMaintenance()
                refresh()
            }
        }
    }

    private enum Switch {
        case backup, archive, outage
    }

    private func binding(_ which: Switch) -> Binding<Bool> {
        Binding(get: {
            _ = state.tick
            switch which {
            case .backup: return DemoSwitches.shared.backupDown
            case .archive: return DemoSwitches.shared.archiveDown
            case .outage: return DemoSwitches.shared.outage
            }
        }, set: { value in
            switch which {
            case .backup: DemoSwitches.shared.backupDown = value
            case .archive: DemoSwitches.shared.archiveDown = value
            case .outage: DemoSwitches.shared.outage = value
            }
            state.tick += 1
            refresh(outageStarted: which == .outage && value)
        })
    }

    private func refresh(outageStarted: Bool = false) {
        guard let sync = model.active?.sync else { return }
        sync.forceHealthRefresh()
        Task {
            await sync.run()
            // „Nur Ansicht“ greift nach 5 Minuten ohne Server; für die Vorführung wird die Zeit vorgespult.
            if outageStarted { sync.backdateLastContact(minutes: 6) }
        }
    }
}

@MainActor
@Observable
private final class DemoSwitchState {
    var tick = 0
}
