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

    @State private var selection: Tab = .today

    var body: some View {
        TabView(selection: $selection) {
            HomeView { selection = $0 }
                .tabItem { Label("Start", systemImage: "house") }
                .tag(Tab.today)
            BauaktenListView()
                .tabItem { Label("Bauakten", systemImage: "folder") }
                .tag(Tab.bauakten)
            CaptureTabView()
                .tabItem { Label("Aufnehmen", systemImage: "camera") }
                .tag(Tab.capture)
            MoreView()
                .tabItem { Label("Mehr", systemImage: "ellipsis.circle") }
                .tag(Tab.more)
        }
    }
}

/// Kopfzeile mit der angemeldeten Person – immer sichtbar und deckend (11, Abschnitt 4.1).
struct CurrentUserHeader: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        if let user = model.active?.user {
            HStack(spacing: 10) {
                Initials(text: LocalUser.initials(of: user.displayName))
                    .scaleEffect(0.8)
                    .frame(width: 36, height: 36)
                VStack(alignment: .leading, spacing: 0) {
                    Text(user.displayName).font(.subheadline.weight(.semibold))
                        .accessibilityIdentifier("currentUserName")
                    if let device = model.deviceRegistration {
                        Text(device.name).font(.caption).foregroundStyle(Theme.textSecondary)
                    }
                }
                Spacer()
                if model.isPoolDevice {
                    Button {
                        model.lock()
                    } label: {
                        Label("Wechseln", systemImage: "person.2")
                            .font(.subheadline)
                    }
                    .buttonStyle(.bordered)
                    .accessibilityIdentifier("switchUserButton")
                }
            }
            .accessibilityElement(children: .contain)
        }
    }
}

/// Schwebende Hauptaktion „Neue Bauakte“ unten rechts im Daumenbereich (07, Abschnitt 1: Glas, Tönung Navy).
struct NewBauakteButton: View {
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Label("Neue Bauakte", systemImage: "plus")
                .font(.headline)
                .padding(.horizontal, 6)
                .frame(minHeight: 44)
        }
        .primaryActionStyle()
        .padding(.trailing, 20)
        .padding(.bottom, 16)
        .accessibilityIdentifier("newBauakteButton")
    }
}
