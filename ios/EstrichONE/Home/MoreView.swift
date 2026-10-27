import EstrichCore
import SwiftUI

/// Reiter „Mehr“: Person und Abgleich oben, dann Planung, Hilfe, Gerät und Server, Sperren und Abmelden (07, Abschnitt 2).
struct MoreView: View {
    enum Route: String, Identifiable, Hashable {
        case sync, tasks, dispatch, week, tickets
        var id: String { rawValue }
    }

    @Environment(AppModel.self) private var model
    @Environment(\.shotScene) private var shotScene
    @State private var route: Route?
    @State private var confirmSignOut = false
    @State private var confirmReset = false
    @State private var unsentBeforeSignOut = 0
    @State private var reporting = false

    var body: some View {
        NavigationStack {
            List {
                if let user = model.active?.user {
                    Section {
                        HStack(spacing: Space.m) {
                            Avatar(name: user.displayName, size: 56)
                            VStack(alignment: .leading, spacing: 2) {
                                Text(user.displayName)
                                    .font(Typo.headline)
                                    .accessibilityIdentifier("currentUserName")
                                Text([user.userName, model.deviceRegistration?.name].compactMap { $0 }.joined(separator: " · "))
                                    .font(Typo.subhead)
                                    .foregroundStyle(Theme.textSecondary)
                            }
                        }
                        .padding(.vertical, Space.xs)
                        if let sync = model.active?.sync {
                            Button { route = .sync } label: {
                                row("Synchronisation", symbol: "arrow.triangle.2.circlepath", detail: syncDetail(sync), detailTint: syncTint(sync))
                            }
                            .accessibilityIdentifier("syncLink")
                        }
                    }
                }

                Section("Planung und Aufgaben") {
                    Button { route = .tasks } label: { row("Meine Aufgaben", symbol: "checklist") }
                        .accessibilityIdentifier("myTasksTile")
                    Button { route = .dispatch } label: { row("Wer fährt wohin", symbol: "car.2") }
                        .accessibilityIdentifier("dispatchRow")
                    Button { route = .week } label: { row("Meine Woche", symbol: "calendar") }
                        .accessibilityIdentifier("myWeekTile")
                }

                Section("Hilfe") {
                    Button { reporting = true } label: { row("Problem melden", symbol: "exclamationmark.bubble", chevron: false) }
                        .accessibilityIdentifier("reportProblemButton")
                    Button { route = .tickets } label: { row("Meine Meldungen", symbol: "tray.full") }
                }

                if model.isDemo {
                    Section {
                        Button(role: .destructive) { model.requestDemo(false) } label: {
                            row("Demo beenden", symbol: "xmark.circle", tint: Theme.danger, titleColor: Theme.danger, chevron: false)
                        }
                        .accessibilityIdentifier("endDemoButton")
                    } header: {
                        Text("Demomodus")
                    } footer: {
                        Text("Beispieldaten – es wird nichts gesendet. Beenden löscht die Demodaten und führt zurück zur Einrichtung.")
                    }
                }

                if let registration = model.deviceRegistration {
                    Section("Dieses Gerät") {
                        LabeledContent("Name", value: registration.name)
                        LabeledContent("Nutzung", value: registration.kind == .pool ? "Poolgerät" : "Persönliches Gerät")
                        LabeledContent("Status", value: Self.describe(registration.status))
                    }
                }

                if let server = model.server {
                    Section("Server") {
                        ServerDetails(server: server)
                            .listRowInsets(EdgeInsets())
                    }
                }

                Section {
                    if model.isPoolDevice {
                        Button { model.lock() } label: { row("Benutzer wechseln", symbol: "person.2", chevron: false) }
                            .accessibilityIdentifier("switchUserButton")
                    } else {
                        Button { model.lock() } label: { row("Sperren", symbol: "lock", chevron: false) }
                            .accessibilityIdentifier("lockButton")
                    }
                    Button(role: .destructive) {
                        Task {
                            unsentBeforeSignOut = await model.unsentItems()
                            confirmSignOut = true
                        }
                    } label: {
                        row("Abmelden", symbol: "rectangle.portrait.and.arrow.right", tint: Theme.danger, titleColor: Theme.danger, chevron: false)
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
            .listStyle(.insetGrouped)
            .navigationTitle("Mehr")
            .rootToolbar()
            .navigationDestination(item: $route) { route in
                switch route {
                case .sync: if let sync = model.active?.sync { SyncView(sync: sync) }
                case .tasks: MyTasksView()
                case .dispatch: DispatchView()
                case .week: MyWeekView()
                case .tickets: MyTicketsView()
                }
            }
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
            .task { applyShotScene() }
        }
    }

    private func row(_ title: String, symbol: String, detail: String? = nil, detailTint: Color = Theme.textSecondary,
                     tint: Color = Theme.accent, titleColor: Color = Theme.textPrimary, chevron: Bool = true) -> some View {
        HStack(spacing: Space.m) {
            IconBadge(symbol: symbol, tint: tint, background: tint.opacity(0.12), size: 30)
            Text(title).font(Typo.body).foregroundStyle(titleColor)
            Spacer(minLength: Space.s)
            if let detail {
                Text(detail).font(Typo.subhead).foregroundStyle(detailTint).lineLimit(1)
            }
            if chevron { Chevron() }
        }
        .frame(minHeight: 36)
        .contentShape(Rectangle())
    }

    private func syncDetail(_ sync: SyncController) -> String {
        if sync.isRunning { return "läuft …" }
        if sync.maintenanceMessage != nil { return "Wartung" }
        if sync.isOffline { return "offline" }
        if sync.summary?.hasProblems == true { return "bitte prüfen" }
        if let summary = sync.summary, !summary.isSettled { return "\(summary.pendingOperations + summary.pendingUploads) ausstehend" }
        return "aktuell"
    }

    private func syncTint(_ sync: SyncController) -> Color {
        if sync.summary?.hasProblems == true { return Theme.danger }
        if sync.maintenanceMessage != nil { return Theme.warning }
        return Theme.textSecondary
    }

    /// Screenshot-Modus: Unterseite öffnen.
    private func applyShotScene() {
        switch shotScene {
        case "sync": route = .sync
        case "disposition": route = .dispatch
        case "aufgaben": route = .tasks
        case "woche": route = .week
        case "meldung", "meldung-detail": route = .tickets
        default: break
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
