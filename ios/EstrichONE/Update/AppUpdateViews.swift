import EstrichCore
import SwiftUI

// App-Updates (docs/app-updates.md): Hinweis auf „Heute“, Sperrbildschirm unter der Mindestversion, „Neu in dieser Version“
// nach dem Update und Abschnitt unter Mehr. Eingebunden mit `.appUpdates(model)` in RootView.

extension View {
    /// Hinweis, Sperre und Neuerungen über der ganzen App; prüft beim Start und beim Zurückkehren (gedrosselt).
    func appUpdates(_ model: AppModel) -> some View {
        modifier(AppUpdateModifier(model: model, center: model.updates))
    }
}

private struct AppUpdateModifier: ViewModifier {
    let model: AppModel
    let center: AppUpdateCenter
    @Environment(\.scenePhase) private var scenePhase

    func body(content: Content) -> some View {
        content
            .overlay {
                if center.isBlocked {
                    AppUpdateRequiredView(center: center, pending: model.active?.sync?.summary.map { $0.pendingOperations + $0.pendingUploads } ?? 0)
                        .transition(.opacity)
                }
            }
            .animation(.default, value: center.isBlocked)
            .sheet(isPresented: Binding(get: { !center.whatsNew.isEmpty && !center.isBlocked }, set: { if !$0 { center.whatsNewShown() } })) {
                WhatsNewSheet(notes: center.whatsNew) { center.whatsNewShown() }
            }
            .alert("Nicht alles übertragen", isPresented: Binding(get: { center.unsentBeforeLeaving != nil }, set: { if !$0 { center.unsentBeforeLeaving = nil } })) {
                Button("Trotzdem aktualisieren") { center.openStore() }
                Button("Abbrechen", role: .cancel) {}
            } message: {
                Text("\(center.unsentBeforeLeaving ?? 0) Änderungen konnten gerade nicht übertragen werden. Sie bleiben beim Update auf dem Gerät gespeichert und werden danach automatisch gesendet.")
            }
            .task {
                center.refresh(.launch)
                center.loadWhatsNew()
            }
            .onChange(of: scenePhase) { _, phase in
                if phase == .active { center.refresh(.resume) }
            }
            .onChange(of: model.server?.address) { _, address in
                if address != nil {
                    center.refresh(.launch)
                    center.loadWhatsNew()
                }
            }
    }
}

/// Hinweis auf „Heute“: neuere Version verfügbar, mit „Aktualisieren“ und „Später“.
struct AppUpdateNotice: View {
    let center: AppUpdateCenter

    var body: some View {
        VStack(alignment: .leading, spacing: Space.s) {
            NoticeLine(tone: .info, text: text, symbol: "arrow.down.app.fill", actionTitle: "Aktualisieren") {
                Task { await center.prepareAndOpen() }
            }
            .accessibilityIdentifier("updateNotice")
            Button("Später erinnern") { center.snooze() }
                .font(Typo.meta)
                .padding(.leading, Space.xl)
        }
    }

    private var text: String {
        var text = "Version \(center.check?.latestVersion ?? "") ist verfügbar."
        if let message = center.check?.message { text += " \(message)" }
        return text
    }
}

/// Sperrbildschirm: App unter der Mindestversion. Daten bleiben, Arbeiten erst nach dem Update.
struct AppUpdateRequiredView: View {
    let center: AppUpdateCenter
    let pending: Int

    var body: some View {
        ZStack {
            LinearGradient(colors: [Theme.navy, Theme.navyLight], startPoint: .top, endPoint: .bottom)
                .ignoresSafeArea()
            ScrollView {
                VStack(spacing: Space.l) {
                    BrandMark(onDark: true)
                        .frame(width: 88, height: 88)
                        .padding(.top, Space.xxl)
                    Text("Update erforderlich")
                        .font(Typo.title)
                        .foregroundStyle(.white)
                    Text(summary)
                        .font(Typo.body)
                        .foregroundStyle(Theme.ice)
                        .multilineTextAlignment(.center)
                    if let message = center.check?.message {
                        Text(message)
                            .font(Typo.callout)
                            .foregroundStyle(.white)
                            .multilineTextAlignment(.center)
                            .padding(Space.m)
                            .background(.white.opacity(0.1), in: RoundedRectangle(cornerRadius: Radius.m, style: .continuous))
                    }
                    VStack(alignment: .leading, spacing: Space.m) {
                        if pending > 0 {
                            Label("\(pending) Änderungen warten auf Übertragung. Sie bleiben gespeichert und werden nach dem Update gesendet.",
                                  systemImage: "icloud.and.arrow.up")
                                .font(Typo.callout)
                                .foregroundStyle(Theme.warning)
                        }
                        Button {
                            Task { await center.prepareAndOpen() }
                        } label: {
                            if center.isPreparing {
                                ProgressView()
                            } else {
                                Text(center.check?.download.url == nil ? "App Store öffnen" : "Jetzt aktualisieren")
                            }
                        }
                        .buttonStyle(EstrichButtonStyle(rank: .primary, fullWidth: true))
                        .disabled(center.isPreparing)
                        .accessibilityIdentifier("updateRequiredAction")
                        if center.check?.download.url == nil {
                            Text("Ihr Betrieb hat noch keinen Link hinterlegt. Die App über den App Store, TestFlight oder die Geräteverwaltung der Firma aktualisieren.")
                                .font(Typo.meta)
                                .foregroundStyle(Theme.textSecondary)
                        }
                        Button("Erneut prüfen") { center.refresh(.manual) }
                            .buttonStyle(EstrichButtonStyle(rank: .secondary, fullWidth: true))
                    }
                    .padding(Space.l)
                    .background(Theme.surface, in: RoundedRectangle(cornerRadius: Radius.xl, style: .continuous))
                    if let notes = center.check?.releaseNotes, !notes.isEmpty {
                        ReleaseNotesList(notes: notes, limit: 6)
                            .padding(Space.l)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .background(Theme.surface, in: RoundedRectangle(cornerRadius: Radius.xl, style: .continuous))
                    }
                }
                .padding(Space.xl)
                .frame(maxWidth: 560)
                .frame(maxWidth: .infinity)
            }
        }
        .accessibilityIdentifier("updateRequired")
        // Heller Text in der Statusleiste auf dem dunklen Hintergrund.
        .preferredColorScheme(.dark)
    }

    private var summary: String {
        var text = "Diese App-Version (\(center.installedVersion)) wird vom Server nicht mehr unterstützt."
        if let target = center.check?.targetVersion { text += " Bitte auf Version \(target) aktualisieren." }
        return text
    }
}

/// Neuerungen je Version, Abschnitte und Punkte.
struct ReleaseNotesList: View {
    let notes: [AppReleaseNote]
    var limit = Int.max

    var body: some View {
        VStack(alignment: .leading, spacing: Space.s) {
            ForEach(Array(visible.enumerated()), id: \.offset) { _, line in
                switch line {
                case let .version(version):
                    Text("Version \(version)").font(Typo.headline).padding(.top, Space.xs)
                case let .section(title):
                    Text(title).font(Typo.subheadStrong).foregroundStyle(Theme.textSecondary)
                case let .item(text):
                    HStack(alignment: .firstTextBaseline, spacing: Space.s) {
                        Text("•").accessibilityHidden(true)
                        Text(text).font(Typo.callout).fixedSize(horizontal: false, vertical: true)
                    }
                }
            }
        }
    }

    private enum Line {
        case version(String), section(String), item(String)
    }

    private var visible: [Line] {
        var lines: [Line] = []
        var items = 0
        for note in notes where items < limit {
            lines.append(.version(note.version))
            for section in note.sections where items < limit {
                lines.append(.section(section.title))
                for item in section.items where items < limit {
                    lines.append(.item(item))
                    items += 1
                }
            }
        }
        return lines
    }
}

/// „Neu in dieser Version“ nach einem Update.
struct WhatsNewSheet: View {
    let notes: [AppReleaseNote]
    let done: () -> Void

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: Space.l) {
                    Text("EstrichONE \(notes.first?.version ?? "")")
                        .font(Typo.subhead)
                        .foregroundStyle(Theme.textSecondary)
                    ReleaseNotesList(notes: notes)
                        .padding(Space.l)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .background(Theme.surface, in: RoundedRectangle(cornerRadius: Radius.xl, style: .continuous))
                    Button("Weiter", action: done)
                        .buttonStyle(EstrichButtonStyle(rank: .primary, fullWidth: true))
                        .accessibilityIdentifier("whatsNewDone")
                }
                .padding(Space.l)
            }
            .background(Theme.canvas)
            .navigationTitle("Neu in dieser Version")
        }
        .presentationDetents([.large])
    }
}

/// Mehr: Stand der App und „Nach Updates suchen“.
struct AppUpdateSection: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        let center = model.updates
        Section {
            HStack {
                Label("App-Update", systemImage: "arrow.down.app")
                Spacer()
                if center.isChecking {
                    ProgressView()
                } else {
                    Text(status(center)).foregroundStyle(Theme.textSecondary)
                }
            }
            if center.check?.status == .recommended || center.check?.status == .required {
                Button("Jetzt aktualisieren") { Task { await center.prepareAndOpen() } }
            } else {
                Button("Nach Updates suchen") { center.refresh(.manual) }
                    .accessibilityIdentifier("checkForUpdates")
            }
        } footer: {
            if center.check?.status == .newerThanServer {
                Text("Diese App ist neuer als der Server (\(center.check?.serverVersion ?? "")). Sie funktioniert weiter; die IT sollte den Server aktualisieren (sudo /opt/estrichone/update.sh).")
            }
        }
    }

    private func status(_ center: AppUpdateCenter) -> String {
        switch center.check?.status {
        case .recommended?, .required?: "Version \(center.check?.latestVersion ?? "") verfügbar"
        case .newerThanServer?: "Neuer als der Server"
        case .current?: "Aktuell"
        case nil: center.checkFailed ? "Prüfung nicht möglich" : "Noch nicht geprüft"
        }
    }
}
