import EstrichCore
import SwiftUI

/// Startseite nach der Anmeldung. „Heute“, Bauakten und Kamera folgen mit AP-12/13; bis dahin zeigt die App
/// ehrlich, wer angemeldet ist und wie das Gerät eingerichtet ist.
struct HomeView: View {
    @Environment(AppModel.self) private var model
    @State private var confirmSignOut = false
    @State private var confirmReset = false

    var body: some View {
        NavigationStack {
            List {
                if let user = model.active?.user {
                    Section {
                        HStack(spacing: 14) {
                            Initials(text: LocalUser.initials(of: user.displayName))
                            VStack(alignment: .leading, spacing: 2) {
                                Text(user.displayName)
                                    .font(.headline)
                                    .accessibilityIdentifier("currentUserName")
                                Text(user.userName).font(.footnote).foregroundStyle(Theme.textSecondary)
                            }
                        }
                        .padding(.vertical, 4)
                    } header: {
                        Text("Angemeldet")
                    }
                }

                Section {
                    Label {
                        Text("Bauakten, Fotos und Synchronisation folgen im nächsten Ausbauschritt der App.")
                            .fixedSize(horizontal: false, vertical: true)
                    } icon: {
                        Image(systemName: "hammer").foregroundStyle(Theme.actionPrimary)
                    }
                }

                if let registration = model.deviceRegistration {
                    Section {
                        LabeledContent("Name", value: registration.name)
                        LabeledContent("Nutzung", value: registration.kind == .pool ? "Poolgerät" : "Persönliches Gerät")
                        LabeledContent("Status", value: Self.describe(registration.status))
                    } header: {
                        Text("Dieses Gerät")
                    }
                }

                if let server = model.server {
                    Section {
                        ServerDetails(server: server)
                            .listRowInsets(EdgeInsets())
                    } header: {
                        Text("Server")
                    }
                }

                Section {
                    if model.isPoolDevice {
                        Button {
                            model.lock()
                        } label: {
                            Label("Benutzer wechseln", systemImage: "person.2")
                        }
                        .accessibilityIdentifier("switchUserButton")
                    } else {
                        Button {
                            model.lock()
                        } label: {
                            Label("Sperren", systemImage: "lock")
                        }
                        .accessibilityIdentifier("lockButton")
                    }
                    Button(role: .destructive) {
                        confirmSignOut = true
                    } label: {
                        Label("Abmelden", systemImage: "rectangle.portrait.and.arrow.right")
                    }
                    .accessibilityIdentifier("signOutButton")
                } footer: {
                    Text("Abmelden beendet die Sitzung auf dem Server und löscht Ihre Daten auf diesem Gerät.")
                }

                Section {
                    Button("App zurücksetzen", role: .destructive) { confirmReset = true }
                } footer: {
                    Text("EstrichONE \(AppEnvironment.appVersionText)")
                }
            }
            .navigationTitle("EstrichONE")
            .confirmationDialog("Abmelden?", isPresented: $confirmSignOut, titleVisibility: .visible) {
                Button("Abmelden", role: .destructive) { Task { await model.signOut() } }
            }
            .confirmationDialog("App zurücksetzen?", isPresented: $confirmReset, titleVisibility: .visible) {
                Button("Alle Daten auf diesem Gerät löschen", role: .destructive) { model.resetApp() }
            } message: {
                Text("Die App vergisst Server, Gerät und alle Personen. Das Gerät muss danach neu eingerichtet werden.")
            }
            .refreshable { await model.refreshDeviceStatus() }
        }
    }

    static func describe(_ status: DeviceStatus) -> String {
        switch status {
        case .pending: "wartet auf Freigabe"
        case .active: "freigegeben"
        case .locked: "gesperrt"
        case .retired: "entfernt"
        }
    }
}
