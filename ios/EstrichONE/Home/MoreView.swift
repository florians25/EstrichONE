import EstrichCore
import SwiftUI

/// Reiter „Mehr“: Person und Abgleich oben, dann Planung, Hilfe, Gerät und Server, Sperren und Abmelden (07, Abschnitt 2).
struct MoreView: View {
    enum Route: String, Identifiable, Hashable {
        case sync, tasks, dispatch, week, tickets, inbox
        var id: String { rawValue }
    }

    @Environment(AppModel.self) private var model
    @Environment(\.shotScene) private var shotScene
    @State private var route: Route?
    @State private var confirmSignOut = false
    @State private var confirmReset = false
    @State private var unsentBeforeSignOut = 0
    @State private var reporting = false
    @State private var unreadTickets = 0
    @State private var push = PushCenter.shared
    @State private var confirmDeletion = false
    @State private var deletionRequested = false

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

                Section {
                    Button { route = .inbox } label: {
                        row("Benachrichtigungen", symbol: "bell", detail: push.unreadCount > 0 ? "\(push.unreadCount) neu" : pushDetail,
                            detailTint: push.unreadCount > 0 ? Theme.accent : Theme.textSecondary)
                    }
                    .accessibilityIdentifier("inboxRow")
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
                    Button { route = .tickets } label: {
                        row("Helpdesk", symbol: "tray.full", detail: unreadTickets > 0 ? "\(unreadTickets) neu" : nil,
                            detailTint: Theme.accent)
                    }
                    .accessibilityIdentifier("helpdeskRow")
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
                    if model.active != nil {
                        Button(role: .destructive) { confirmDeletion = true } label: {
                            row("Konto löschen beantragen", symbol: "person.crop.circle.badge.xmark", tint: Theme.danger, titleColor: Theme.danger,
                                chevron: false)
                        }
                        .accessibilityIdentifier("requestAccountDeletion")
                    }
                } footer: {
                    Text("Abmelden beendet die Sitzung auf dem Server und löscht Ihre Daten auf diesem Gerät. Ihr Benutzerkonto verwaltet Ihr Betrieb – die Löschung beantragen Sie hier, die Administration führt sie aus.")
                }

                Section {
                    Button("App zurücksetzen", role: .destructive) { confirmReset = true }
                        .accessibilityIdentifier("resetAppButton")
                } footer: {
                    Text("Löscht alles auf diesem Gerät (Server, Gerät, Personen, Fotos, Einstellungen) und startet bei der Einrichtung. Startet die App nicht mehr richtig: Einstellungen-App › EstrichONE › „Beim nächsten Start zurücksetzen“.\n\nEstrichONE \(AppEnvironment.appVersionText)")
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
                case .inbox: NotificationInboxView()
                }
            }
            .navigationDestination(for: HomeRoute.self) { route in
                switch route {
                case let .bauakte(id): BauakteDetailView(bauakteId: id)
                case .tasks: MyTasksView()
                case .week: MyWeekView()
                case .sync: if let sync = model.active?.sync { SyncView(sync: sync) }
                case .dispatch: DispatchView()
                case .inbox: NotificationInboxView()
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
            .confirmationDialog("Konto löschen beantragen?", isPresented: $confirmDeletion, titleVisibility: .visible) {
                Button("Löschung beantragen", role: .destructive) { Task { await requestDeletion() } }
            } message: {
                Text("Die Administration Ihres Betriebs erhält einen Antrag über den Helpdesk und löscht Ihr Benutzerkonto samt persönlicher Daten (gesetzliche Aufbewahrungspflichten ausgenommen). Bis dahin können Sie die App weiter nutzen.")
            }
            .alert("Antrag gestellt", isPresented: $deletionRequested) {
                Button("OK", role: .cancel) {}
            } message: {
                Text("Der Antrag liegt im Helpdesk. Zum sofortigen Entfernen Ihrer Daten von diesem Gerät: Abmelden.")
            }
            .resetAppConfirmation(isPresented: $confirmReset)
            .refreshable { await model.refreshDeviceStatus() }
            .sheet(isPresented: $reporting) {
                ReportProblemView { reporting = false }
            }
            .task { applyShotScene() }
            .task(id: "\(model.active?.sync?.revision ?? 0)|\(route == nil)") {
                if route == nil { unreadTickets = await TicketReadState.unreadCount(model: model) }
            }
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
        case "mehr-zuruecksetzen": confirmReset = true
        case "postfach": route = .inbox
        case "disposition": route = .dispatch
        case "aufgaben": route = .tasks
        case "woche": route = .week
        case "meldung", "meldung-detail", "meldung-bearbeitung": route = .tickets
        case "dispo-zuweisen", "dispo-verschieben", "dispo-tauschen", "dispo-fahrzeug", "dispo-kolonnen", "dispo-freigabe": route = .dispatch
        default: break
        }
    }

    private var pushDetail: String? {
        switch push.permission {
        case .allowed: "Push an"
        case .denied: "Push aus"
        default: nil
        }
    }

    /// Apple-Vorgabe 5.1.1(v): Löschung des Kontos aus der App heraus anstoßen. Konten legt der Betrieb an; der Antrag
    /// geht als Meldung an die Administration (offline vorgemerkt), die das Konto im Web löscht.
    private func requestDeletion() async {
        let name = model.active?.user.displayName ?? ""
        _ = try? await model.report(NewTicket(title: "Antrag auf Löschung meines Benutzerkontos",
                                              description: "\(name) beantragt in der App die Löschung des eigenen Benutzerkontos und der persönlichen Daten.",
                                              category: .access), includeDiagnostics: false, attachments: [])
        deletionRequested = true
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
