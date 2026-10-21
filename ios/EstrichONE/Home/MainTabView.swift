import EstrichCore
import SwiftUI

/// Hauptnavigation (07, Abschnitt 2). Ab iOS 26 erscheint die Tab-Leiste automatisch in Liquid Glass.
struct MainTabView: View {
    enum Tab: Hashable {
        case today
        case bauakten
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
            CaptureTabView(mode: $captureMode)
                .tabItem { Label("Aufnehmen", systemImage: "camera") }
                .tag(Tab.capture)
            MoreView()
                .tabItem { Label("Mehr", systemImage: "ellipsis.circle") }
                .tag(Tab.more)
        }
        .safeAreaInset(edge: .top, spacing: 0) {
            if model.isDemo { DemoBanner { model.requestDemo(false) } }
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

/// Deutliche Kennzeichnung des Demomodus mit „Beenden“.
struct DemoBanner: View {
    let end: () -> Void

    var body: some View {
        HStack(spacing: 10) {
            Image(systemName: "play.rectangle.fill").accessibilityHidden(true)
            Text("Demo · Beispieldaten, nichts wird gesendet").font(.footnote.weight(.semibold))
                .accessibilityIdentifier("demoBanner")
            Spacer()
            Button("Beenden", action: end)
                .font(.footnote.weight(.bold))
                .foregroundStyle(Theme.onAccent)
                .frame(minHeight: 32)
                .accessibilityIdentifier("endDemoButton")
        }
        .foregroundStyle(Theme.onAccent)
        .padding(.horizontal, 16)
        .padding(.vertical, 6)
        .background(Theme.accent)
    }
}
