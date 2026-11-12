import EstrichCore
import SwiftUI
import UIKit

/// Microsoft Teams (docs/microsoft-teams.md): „In Teams öffnen“ öffnet die Teams-App (`msteams:`), wenn sie installiert ist,
/// sonst Teams im Browser. Ohne Teams-Anbindung erscheint nichts davon.
enum TeamsOpener {
    @MainActor
    static func open(_ targets: TeamsOpenTargets) async {
        for url in targets.candidates {
            let opened = await UIApplication.shared.open(url)
            if opened { return }
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
