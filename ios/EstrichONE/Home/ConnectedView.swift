import EstrichCore
import SwiftUI

/// Startzustand nach der Servereinrichtung. Die Anmeldung (Schritt 4) folgt mit AP-11;
/// bis dahin zeigt die App ehrlich, was eingerichtet ist, und erlaubt das Zurücksetzen.
struct ConnectedView: View {
    let server: VerifiedServer

    @Environment(AppModel.self) private var model
    @State private var confirmReset = false

    var body: some View {
        NavigationStack {
            List {
                Section {
                    VStack(alignment: .leading, spacing: 4) {
                        Text(server.info.serverName)
                            .font(.title3.weight(.semibold))
                            .accessibilityIdentifier("connectedServerName")
                        Text(server.address.host)
                            .font(.callout)
                            .foregroundStyle(Theme.textSecondary)
                    }
                    .padding(.vertical, 4)
                } header: {
                    Text("Verbunden mit")
                }

                Section {
                    Label {
                        Text("Die Anmeldung und die Geräteregistrierung folgen im nächsten Ausbauschritt der App.")
                            .fixedSize(horizontal: false, vertical: true)
                    } icon: {
                        Image(systemName: "person.badge.key")
                            .foregroundStyle(Theme.actionPrimary)
                    }
                } header: {
                    Text("Anmeldung")
                }

                Section {
                    ServerDetails(server: server)
                        .listRowInsets(EdgeInsets())
                } header: {
                    Text("Server")
                }

                Section {
                    Button("Server wechseln", role: .destructive) {
                        confirmReset = true
                    }
                } footer: {
                    Text("EstrichONE \(AppEnvironment.appVersionText)")
                }
            }
            .navigationTitle("EstrichONE")
            .confirmationDialog("Server wechseln?", isPresented: $confirmReset, titleVisibility: .visible) {
                Button("Server wechseln", role: .destructive) { model.resetServer() }
            } message: {
                Text("Die App vergisst den Server \(server.info.serverName) und startet die Einrichtung neu.")
            }
        }
    }
}
