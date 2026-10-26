import EstrichCore
import SwiftUI

/// Hauptnavigation (07, Abschnitt 2). Ab iOS 26 erscheint die Tab-Leiste automatisch in Liquid Glass.
struct MainTabView: View {
    enum Tab: Hashable {
        case today
        case bauakten
        case capture
        case customers
        case more
    }

    @Environment(AppModel.self) private var model
    @State private var selection: Tab = .today
    @State private var previous: Tab = .today
    @State private var captureMode: CaptureMode = .photo
    @State private var showsCaptureHub = false

    var body: some View {
        TabView(selection: Binding(get: { selection }, set: { tab in
            // „Erfassen“ öffnet das Aktionsblatt und bleibt auf dem bisherigen Reiter.
            if tab == .capture {
                showsCaptureHub = true
            } else {
                selection = tab
                previous = tab
            }
        })) {
            HomeView(openTab: { selection = $0 }, openCapture: { mode in
                captureMode = mode
                showsCaptureHub = true
            })
                .tabItem { Label("Heute", systemImage: "sun.max") }
                .tag(Tab.today)
            BauaktenListView()
                .tabItem { Label("Baustellen", systemImage: "building.2") }
                .tag(Tab.bauakten)
            Color.clear
                .tabItem { Label("Erfassen", systemImage: "plus.circle.fill") }
                .tag(Tab.capture)
                .accessibilityIdentifier("captureTab")
            CustomerDirectoryView()
                .tabItem { Label("Kunden", systemImage: "person.2") }
                .tag(Tab.customers)
            MoreView()
                .tabItem { Label("Mehr", systemImage: "ellipsis.circle") }
                .tag(Tab.more)
        }
        .modifier(SidebarAdaptable())
        .sheet(isPresented: $showsCaptureHub) {
            CaptureHub { showsCaptureHub = false }
                .presentationDetents([.medium, .large])
        }
        .safeAreaInset(edge: .top, spacing: 0) {
            VStack(spacing: 0) {
                if model.isDemo { DemoBanner { model.requestDemo(false) } }
                if let sync = model.active?.sync, let message = sync.maintenanceMessage ?? sync.notices?.maintenance.flatMap({ $0.active ? ($0.message ?? "Der Server wird gewartet.") : nil }) {
                    MaintenanceBanner(message: message)
                }
            }
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

/// Schwebende Hauptaktion „Neue Bauakte“ unten rechts im Daumenbereich.
struct NewBauakteButton: View {
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Label("Neue Bauakte", systemImage: "plus")
        }
        .buttonStyle(EstrichButtonStyle(rank: .primary, size: .regular))
        .keyboardShortcut("n", modifiers: .command)
        .padding(.trailing, 16)
        .padding(.bottom, 12)
        .accessibilityIdentifier("newBauakteButton")
    }
}

/// Wartung des Servers deutlich über allen Seiten: Erfasstes bleibt auf dem Gerät und wird danach übertragen.
struct MaintenanceBanner: View {
    let message: String

    var body: some View {
        HStack(alignment: .top, spacing: 10) {
            Image(systemName: "wrench.and.screwdriver.fill").font(.title3).accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 2) {
                Text("Wartung").font(.subheadline.weight(.bold))
                Text("\(message) Sie können weiterarbeiten – alles bleibt auf dem Gerät und wird danach übertragen.")
                    .font(.footnote)
                    .fixedSize(horizontal: false, vertical: true)
            }
            Spacer(minLength: 0)
        }
        .foregroundStyle(Color(hex: 0x3B2300))
        .padding(12)
        .background(Color(hex: 0xFCD34D), in: RoundedRectangle(cornerRadius: 16, style: .continuous))
        .padding(.horizontal, 16)
        .padding(.vertical, 4)
        .accessibilityElement(children: .combine)
        .accessibilityIdentifier("maintenanceBanner")
    }
}

/// Kennzeichnung des Demomodus als schmale Kapsel mit „Beenden“.
struct DemoBanner: View {
    let end: () -> Void

    var body: some View {
        HStack(spacing: 8) {
            Image(systemName: "play.rectangle.fill").accessibilityHidden(true)
            Text("Demo · Beispieldaten, nichts wird gesendet")
                .font(.footnote.weight(.semibold))
                .lineLimit(1)
                .minimumScaleFactor(0.85)
                .accessibilityIdentifier("demoBanner")
            Spacer(minLength: 4)
            Button("Beenden", action: end)
                .font(.footnote.weight(.bold))
                .padding(.horizontal, 10)
                .frame(minHeight: 30)
                .background(Theme.onAccent.opacity(0.12), in: Capsule())
                .accessibilityIdentifier("endDemoButton")
        }
        .foregroundStyle(Theme.onAccent)
        .padding(.leading, 14).padding(.trailing, 4)
        .padding(.vertical, 4)
        .background(Theme.accent, in: Capsule())
        .padding(.horizontal, 16)
        .padding(.vertical, 4)
    }
}
