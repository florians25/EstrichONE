import EstrichCore
import SwiftUI
import UIKit

/// Microsoft Teams (docs/microsoft-teams.md): „In Teams öffnen“ führt immer in die Teams-App. Ohne Teams-Anbindung erscheint nichts davon.
enum TeamsOpener {
    /// 1. https-Link nur als Universal Link (öffnet die Teams-App, nie Safari), 2. `msteams:` (LSApplicationQueriesSchemes),
    /// 3. Hinweis „Microsoft Teams installieren“ (App Store) – der Browser nur, wenn man ihn dort ausdrücklich wählt.
    /// Früher: https-Link ohne `universalLinksOnly` – hatte jemand Teams-Links einmal in Safari geöffnet (bzw. war die Teams-App
    /// nur über `msteams:` nicht erreichbar), landete man im Browser statt in der App.
    @MainActor
    @discardableResult
    static func open(_ targets: TeamsOpenTargets) async -> Bool {
        if let web = targets.web, await UIApplication.shared.open(web, options: [.universalLinksOnly: true]) {
            return true
        }
        if let app = targets.app, UIApplication.shared.canOpenURL(app), await UIApplication.shared.open(app) {
            return true
        }
        offerInstall(web: targets.web)
        return false
    }

    @MainActor
    static var isTeamsInstalled: Bool {
        guard let url = URL(string: "msteams:/") else { return false }
        return UIApplication.shared.canOpenURL(url)
    }

    /// Teams-App fehlt: App Store anbieten (Standardaktion), Browser nur auf ausdrücklichen Wunsch.
    @MainActor
    private static func offerInstall(web: URL?) {
        let alert = UIAlertController(title: "Microsoft Teams", message: "Die Teams-App ist auf diesem Gerät nicht installiert.", preferredStyle: .alert)
        let install = UIAlertAction(title: "Microsoft Teams installieren", style: .default) { _ in
            Task { @MainActor in _ = await UIApplication.shared.open(TeamsChatLinks.appStoreURL) }
        }
        alert.addAction(install)
        if let web {
            alert.addAction(UIAlertAction(title: "Im Browser öffnen", style: .default) { _ in
                Task { @MainActor in _ = await UIApplication.shared.open(web) }
            })
        }
        alert.addAction(UIAlertAction(title: "Abbrechen", style: .cancel))
        alert.preferredAction = install
        let scene = UIApplication.shared.connectedScenes.compactMap { $0 as? UIWindowScene }.first { $0.activationState == .foregroundActive }
        var top = scene?.keyWindow?.rootViewController
        while let presented = top?.presentedViewController { top = presented }
        top?.present(alert, animated: true)
    }
}

/// „Chat in Teams“: Kollegen mit Microsoft-Konto; Tippen öffnet den Einzelchat direkt in der Teams-App. Wer kein Microsoft-Konto hat,
/// erscheint nicht (keine deaktivierten Einträge).
struct TeamsPeopleSheet: View {
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var people: [TeamsPerson] = []
    @State private var loaded = false
    @State private var search = ""

    private var reachable: [TeamsPerson] {
        people.filter { $0.upn != nil && $0.userId != model.active?.user.id }
            .filter { search.isEmpty || $0.name.localizedCaseInsensitiveContains(search) }
    }

    var body: some View {
        NavigationStack {
            List {
                if !TeamsOpener.isTeamsInstalled {
                    Section {
                        Link(destination: TeamsChatLinks.appStoreURL) {
                            Label("Microsoft Teams installieren", systemImage: "arrow.down.app")
                        }
                    } footer: {
                        Text("Chats öffnen sich in der Teams-App.")
                    }
                }
                ForEach(reachable) { person in
                    Button {
                        if let targets = TeamsChatLinks.targets(for: [person]) {
                            Task {
                                await TeamsOpener.open(targets)
                                dismiss()
                            }
                        }
                    } label: {
                        Label(person.name, systemImage: "person.crop.circle")
                    }
                    .accessibilityIdentifier("teamsPerson")
                }
            }
            .overlay {
                if !loaded {
                    ProgressView()
                } else if reachable.isEmpty {
                    ContentUnavailableView("Niemand gefunden", systemImage: "person.2.slash",
                                           description: Text("Kollegen erscheinen hier, sobald ihnen ein Microsoft-Konto zugeordnet ist."))
                }
            }
            .searchable(text: $search, prompt: "Name")
            .navigationTitle("Chat in Teams")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Abbrechen") { dismiss() } }
            }
            .task {
                people = await model.active?.client.teamsPeople() ?? []
                loaded = true
            }
        }
    }
}

/// Einmaliger Hinweis „Ab sofort chatten wir in Teams“ (wegklickbar, je Text gemerkt).
struct TeamsRolloutNotice: View {
    let text: String
    @SwiftUI.AppStorage("teamsNoticeDismissed") private var dismissed = ""

    var body: some View {
        if dismissed != text {
            VStack(alignment: .leading, spacing: Space.s) {
                HStack(alignment: .top) {
                    Label("Microsoft Teams", systemImage: "person.2.wave.2")
                        .font(.headline)
                    Spacer()
                    Button("Schließen", systemImage: "xmark") { dismissed = text }
                        .labelStyle(.iconOnly)
                        .accessibilityIdentifier("teamsNoticeClose")
                }
                Text(text)
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                if !TeamsOpener.isTeamsInstalled {
                    Link("Microsoft Teams installieren", destination: TeamsChatLinks.appStoreURL)
                        .font(.subheadline.weight(.semibold))
                }
            }
            .accessibilityElement(children: .contain)
            .accessibilityIdentifier("teamsRolloutNotice")
        }
    }
}

struct OpenInTeamsButton: View {
    let targets: TeamsOpenTargets
    var title = "In Teams öffnen"

    var body: some View {
        Button(title, systemImage: "person.2.wave.2") { Task { await TeamsOpener.open(targets) } }
            .disabled(targets.candidates.isEmpty)
            .accessibilityIdentifier("openInTeamsButton")
    }
}

/// Hinweis im Chat-Archiv (Modus „Microsoft Teams“): lesen ja, schreiben nur noch in Teams.
struct ChatArchiveNotice: View {
    let targets: TeamsOpenTargets?

    var body: some View {
        VStack(alignment: .leading, spacing: Space.s) {
            Label("Chat-Archiv", systemImage: "archivebox")
                .font(.headline)
            Text("Unterhaltungen finden jetzt in Microsoft Teams statt. Bisherige Verläufe bleiben hier lesbar; Ihre Daten können Sie unter „Meine Chat-Einstellungen“ bzw. in der Weboberfläche exportieren.")
                .font(.subheadline)
                .foregroundStyle(.secondary)
            if let targets, !targets.candidates.isEmpty {
                OpenInTeamsButton(targets: targets)
                    .buttonStyle(.borderedProminent)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .accessibilityElement(children: .contain)
        .accessibilityIdentifier("chatArchiveNotice")
    }
}

/// Hinweis unter der Eingabezeile des Chats: In der App keine Dateien und Fotos mehr verschicken, dafür Microsoft Teams
/// (`ChatComposerPolicy`). Mit Teams-Verknüpfung tippbar („In Teams öffnen“), sonst reiner Text.
struct ChatFileExchangeHint: View {
    let targets: TeamsOpenTargets?

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: Space.xs) {
            Image(systemName: "info.circle")
                .accessibilityHidden(true)
            Text(ChatComposerPolicy.fileExchangeHint)
                .fixedSize(horizontal: false, vertical: true)
            Spacer(minLength: 0)
            if let targets, !targets.candidates.isEmpty {
                Button("In Teams öffnen") { Task { await TeamsOpener.open(targets) } }
                    .font(Typo.labelStrong)
                    .buttonStyle(.borderless)
                    .foregroundStyle(Theme.accent)
                    .accessibilityIdentifier("chatFileHintTeams")
            }
        }
        .font(Typo.label)
        .foregroundStyle(Theme.textSecondary)
        .padding(.horizontal, Space.xs)
        .accessibilityElement(children: .contain)
        .accessibilityIdentifier("chatFileExchangeHint")
    }
}
