import EstrichCore
import SwiftUI

/// Hauptnavigation: Heute · Baustellen · Erfassen · Kunden · Mehr. „Erfassen“ öffnet ein Aktionsblatt und bleibt auf dem
/// bisherigen Reiter. Ab iOS 26 erscheint die Leiste in Liquid Glass; auf dem iPad als einblendbare Seitenleiste.
struct MainTabView: View {
    enum Tab: Hashable {
        case today
        case bauakten
        case capture
        case customers
        case more
    }

    @Environment(AppModel.self) private var model
    @Environment(\.shotScene) private var shotScene
    @State private var selection: Tab = .today
    @State private var showsCaptureHub = false
    /// Meldungen mit neuer Antwort der IT (Abzeichen am Reiter „Mehr“).
    @State private var unreadTickets = 0
    @State private var push = PushCenter.shared

    var body: some View {
        TabView(selection: Binding(get: { selection }, set: { tab in
            if tab == .capture {
                showsCaptureHub = true
            } else {
                selection = tab
            }
        })) {
            HomeView { showsCaptureHub = true }
                .tabItem { Label("Heute", systemImage: "sun.max") }
                .tag(Tab.today)
            BauaktenListView()
                .tabItem { Label("Baustellen", systemImage: "building.2") }
                .tag(Tab.bauakten)
            Color.clear
                .tabItem { Label("Erfassen", systemImage: "plus.circle") }
                .tag(Tab.capture)
                .accessibilityIdentifier("captureTab")
            CustomerDirectoryView()
                .tabItem { Label("Kunden", systemImage: "person.2") }
                .tag(Tab.customers)
            MoreView()
                .tabItem { Label("Mehr", systemImage: "ellipsis.circle") }
                .tag(Tab.more)
                .badge(unreadTickets)
        }
        .modifier(SidebarAdaptable())
        .sheet(isPresented: $showsCaptureHub) {
            CaptureHub { showsCaptureHub = false }
                .presentationDragIndicator(.visible)
        }
        .task { applyShotScene() }
        .task(id: model.active?.sync?.revision) {
            unreadTickets = await TicketReadState.unreadCount(model: model)
            if let inbox = try? await model.active?.client.notifications() { push.setUnread(inbox.unreadCount) }
        }
        .task { push.refreshPermission() }
        // Neues APNs-Token (oder nach der Anmeldung) beim Server registrieren.
        .task(id: push.token) { await push.upload(model: model) }
        .task(id: push.pendingReadId) {
            guard let id = push.pendingReadId, let client = model.active?.client else { return }
            try? await client.markNotificationRead(id)
            push.pendingReadId = nil
            if let inbox = try? await client.notifications() { push.setUnread(inbox.unreadCount) }
        }
        // Angetippte Benachrichtigung: Objekt als Blatt über dem aktuellen Reiter öffnen.
        .sheet(item: $push.pendingRoute) { route in
            NavigationStack {
                NotificationTargetView(route: route)
                    .toolbar {
                        ToolbarItem(placement: .cancellationAction) { Button("Schließen") { push.pendingRoute = nil } }
                    }
            }
        }
    }

    /// Screenshot-Modus: passenden Reiter wählen bzw. „Erfassen“ öffnen.
    private func applyShotScene() {
        guard let scene = shotScene else { return }
        switch ShotRoute.tab(for: scene) {
        case .capture: showsCaptureHub = true
        case let tab: selection = tab
        }
    }
}

/// Zuordnung der Screenshot-Bildschirme zu den Reitern.
enum ShotRoute {
    static func tab(for scene: String) -> MainTabView.Tab {
        let prefix = scene.split(separator: "-").first.map(String.init) ?? scene
        switch prefix {
        case "baustellen", "bauakte", "bereich", "neu", "erstbegehung", "abnahme", "foto", "raum", "tagebuch", "mangel", "basisdaten", "protokoll", "kontakt": return .bauakten
        case "kunden", "kundenkarte": return .customers
        case "mehr", "sync", "meldung", "aufgaben", "woche", "disposition", "dispo", "postfach": return .more
        case "erfassen": return .capture
        default: return .today
        }
    }
}

/// iPad ab iOS 18: Reiter als einblendbare Seitenleiste.
private struct SidebarAdaptable: ViewModifier {
    func body(content: Content) -> some View {
        if #available(iOS 18, *) {
            content.tabViewStyle(.sidebarAdaptable)
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
                            Section("Demomodus – Beispieldaten, es wird nichts gesendet.") {
                                Button("Demo beenden", systemImage: "xmark.circle", role: .destructive) { model.requestDemo(false) }
                                    .accessibilityIdentifier("endDemoButton")
                            }
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
                Text("\(maintenanceMessage ?? "Der Server wird gewartet.")\n\nSie können weiterarbeiten – alles bleibt auf dem Gerät und wird danach übertragen.")
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
