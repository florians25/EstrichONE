import EstrichCore
import SwiftUI

/// Reiter „Mehr“: Person, Synchronisation, Hilfe, Gerät, Server, Benutzerwechsel, Abmelden (07, Abschnitt 2).
struct MoreView: View {
    @Environment(AppModel.self) private var model
    @State private var confirmSignOut = false
    @State private var confirmReset = false
    @State private var unsentBeforeSignOut = 0
    @State private var reporting = false

    var body: some View {
        NavigationStack {
            List {
                if let sync = model.active?.sync {
                    Section {
                        SyncStatusBar(sync: sync)
                            .listRowInsets(EdgeInsets())
                        NavigationLink {
                            SyncView(sync: sync)
                        } label: {
                            Label("Synchronisation", systemImage: "arrow.triangle.2.circlepath")
                        }
                        .accessibilityIdentifier("syncLink")
                    }
                }

                Section {
                    NavigationLink { MyTasksView() } label: { Label("Meine Aufgaben", systemImage: "checklist") }
                        .accessibilityIdentifier("myTasksTile")
                    NavigationLink { DispatchView() } label: { Label("Wer fährt wohin", systemImage: "car.2") }
                        .accessibilityIdentifier("dispatchRow")
                    NavigationLink { MyWeekView() } label: { Label("Meine Woche", systemImage: "calendar") }
                        .accessibilityIdentifier("myWeekTile")
                } header: {
                    Text("Planung und Aufgaben")
                }

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
                    Button {
                        reporting = true
                    } label: {
                        Label("Problem melden", systemImage: "exclamationmark.bubble")
                    }
                    .accessibilityIdentifier("reportProblemButton")
                    NavigationLink {
                        MyTicketsView()
                    } label: {
                        Label("Meine Meldungen", systemImage: "tray.full")
                    }
                } header: {
                    Text("Hilfe")
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
                        Task {
                            unsentBeforeSignOut = await model.unsentItems()
                            confirmSignOut = true
                        }
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
            .navigationTitle("Mehr")
            .navigationDestination(for: HomeRoute.self) { route in
                switch route {
                case let .bauakte(id): BauakteDetailView(bauakteId: id)
                case .tasks: MyTasksView()
                case .week: MyWeekView()
                case .sync: if let sync = model.active?.sync { SyncView(sync: sync) }
                case .dispatch: DispatchView()
                }
            }
            .confirmationDialog("Abmelden?", isPresented: $confirmSignOut, titleVisibility: .visible) {
                Button(unsentBeforeSignOut > 0 ? "Trotzdem abmelden und Daten löschen" : "Abmelden", role: .destructive) {
                    Task { await model.signOut() }
                }
            } message: {
                if unsentBeforeSignOut > 0 {
                    Text("\(unsentBeforeSignOut) Änderungen sind noch nicht übertragen. Beim Abmelden gehen sie verloren. Besser: Verbindung herstellen und warten, bis alles übertragen ist.")
                }
            }
            .confirmationDialog("App zurücksetzen?", isPresented: $confirmReset, titleVisibility: .visible) {
                Button("Alle Daten auf diesem Gerät löschen", role: .destructive) { model.resetApp() }
            } message: {
                Text("Die App vergisst Server, Gerät und alle Personen. Das Gerät muss danach neu eingerichtet werden.")
            }
            .refreshable { await model.refreshDeviceStatus() }
            .sheet(isPresented: $reporting) {
                ReportProblemView { reporting = false }
            }
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
