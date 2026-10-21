import EstrichCore
import SwiftUI

/// Hauptnavigation (07, Abschnitt 2). Ab iOS 26 erscheint die Tab-Leiste automatisch in Liquid Glass.
struct MainTabView: View {
    enum Tab: Hashable {
        case today
        case bauakten
        case customers
        case capture
        case more
    }

    @Environment(AppModel.self) private var model
    @State private var selection: Tab = .today
    @State private var captureMode: CaptureMode = .photo

    var body: some View {
        TabView(selection: $selection) {
            HomeView(openTab: { selection = $0 }, openCapture: { mode in
                captureMode = mode
                selection = .capture
            })
                .tabItem { Label("Start", systemImage: "house") }
                .tag(Tab.today)
            BauaktenListView()
                .tabItem { Label("Bauakten", systemImage: "folder") }
                .tag(Tab.bauakten)
            CustomerDirectoryView()
                .tabItem { Label("Kunden", systemImage: "person.2") }
                .tag(Tab.customers)
            CaptureTabView(mode: $captureMode)
                .tabItem { Label("Aufnehmen", systemImage: "camera") }
                .tag(Tab.capture)
            MoreView()
                .tabItem { Label("Mehr", systemImage: "ellipsis.circle") }
                .tag(Tab.more)
        }
        .modifier(SidebarAdaptable())
        .safeAreaInset(edge: .top, spacing: 0) {
            if model.isDemo { DemoBanner { model.requestDemo(false) } }
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
