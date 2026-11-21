import EstrichCore
import SwiftUI

/// „Erfassen“: alles, was auf der Baustelle neu entsteht, an einer Stelle. Oben die Baustelle (heutiger Einsatz bzw.
/// zuletzt geöffnet, jederzeit änderbar), darunter eine Standardliste in Gruppen – ein Tipp, und es geht los. Ohne gewählte Baustelle
/// wird sie vor der Aktion abgefragt.
struct CaptureHub: View {
    let onDone: () -> Void

    @Environment(AppModel.self) private var model
    @State private var site: Bauakte?
    @State private var todayIds: [UUID] = []
    @State private var isToday = false
    @State private var picking: SiteTask?
    @State private var sheet: Activity?
    @State private var cover: Activity?
    @State private var creating = false
    @State private var created: UUID?
    @State private var toast: String?

    /// Aktion für eine Baustelle.
    private struct Activity: Identifiable {
        let task: SiteTask
        let bauakte: Bauakte
        var id: String { "\(task.rawValue)-\(bauakte.id)" }
    }

    private struct OpenTarget: Identifiable {
        let id: UUID
    }

    /// Module der Suite (zuletzt bekannter Stand; ohne Angabe alles an).
    private var modules: AppModules { model.active?.sync?.modules ?? .allOn }

    var body: some View {
        NavigationStack {
            // Standardliste in Gruppen (wie Android): ein Tipp, und es geht los.
            List {
                Section("Baustelle") { siteCard }
                Section("Fotos und Dokumente") {
                    tile(.photo, symbol: "camera", title: "Foto", subtitle: "Serie mit Beschriftung", id: "hubPhoto")
                    tile(.video, symbol: "video", title: "Video", subtitle: "kurze Aufnahme", id: "hubVideo")
                    tile(.scan, symbol: "doc.viewfinder", title: "Dokument scannen", subtitle: "Lieferschein, Plan", id: "hubScan")
                }
                // Systemeinstellungen › Module: Einträge abgeschalteter Module entfallen.
                Section("Festhalten") {
                    if modules.isOn(AppModules.diary) {
                        tile(.diary, symbol: "book", title: "Bautagebuch", subtitle: "Eintrag für heute", id: "hubDiary")
                    }
                    if modules.isOn(AppModules.defects) {
                        tile(.defect, symbol: "exclamationmark.triangle", title: "Mangel melden", subtitle: "mit Frist", id: "hubDefect")
                    }
                    if modules.isOn(AppModules.tasks) {
                        tile(.task, symbol: "checklist.unchecked", title: "Aufgabe", subtitle: "für mich oder andere", id: "hubTask")
                    }
                    tile(.note, symbol: "square.and.pencil", title: "Notiz", subtitle: "für alle in der Akte", id: "hubNote")
                }
                if modules.anyOn(AppModules.inspections, AppModules.acceptance) {
                    Section("Prüfen") {
                        if modules.isOn(AppModules.inspections) {
                            tile(.inspection, symbol: "checklist", title: "Erstbegehung", subtitle: "Prüfpunkte vor Ort", id: "hubInspection")
                            tile(.measurement, symbol: "drop", title: "CM-Messung", subtitle: "Belegreife prüfen", id: "hubMeasurement")
                        }
                        if modules.isOn(AppModules.acceptance) {
                            tile(.acceptance, symbol: "signature", title: "Abnahme", subtitle: "mit Unterschrift", id: "hubAcceptance")
                        }
                    }
                }
                Section {
                    Button { creating = true } label: { Label("Neue Bauakte anlegen", systemImage: "folder.badge.plus") }
                        .frame(minHeight: Size.minTap)
                        .accessibilityIdentifier("newBauakteTile")
                }
            }
            .listStyle(.insetGrouped)
            .navigationTitle("Erfassen")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar { ToolbarItem(placement: .confirmationAction) { Button("Fertig", action: onDone) } }
            .task { await preselect() }
            .sheet(item: $picking) { task in
                BauakteQuickPicker(title: task == .open ? "Baustelle wählen" : "\(task.title): Baustelle", todayIds: todayIds) { bauakte in
                    picking = nil
                    guard let bauakte else { return }
                    site = bauakte
                    isToday = todayIds.contains(bauakte.id)
                    guard task != .open else { return }
                    Task {
                        // Erst wenn die Auswahl geschlossen ist, das nächste Blatt öffnen.
                        try? await Task.sleep(for: .milliseconds(450))
                        start(task, for: bauakte)
                    }
                }
            }
            .fullScreenCover(item: $cover) { activity in
                switch activity.task {
                case .scan:
                    ScanFlowView(bauakte: activity.bauakte) { saved in
                        cover = nil
                        if saved { toast = "Dokument gespeichert" }
                    }
                default:
                    CameraView(bauakte: activity.bauakte) { count in
                        cover = nil
                        if count > 0 { toast = count == 1 ? "Foto gespeichert" : "\(count) Fotos gespeichert" }
                    }
                }
            }
            .sheet(item: $sheet) { activity in
                form(activity)
            }
            .sheet(isPresented: $creating) {
                NewBauakteView { id, next in
                    creating = false
                    Task {
                        try? await Task.sleep(for: .milliseconds(450))
                        await didCreate(id, next: next)
                    }
                }
            }
            .sheet(item: Binding(get: { created.map(OpenTarget.init) }, set: { created = $0?.id })) { target in
                NavigationStack {
                    BauakteDetailView(bauakteId: target.id)
                        .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Fertig") { created = nil } } }
                }
            }
            .toast($toast)
        }
        .presentationDetents([.fraction(0.8), .large])
    }

    // MARK: Baustelle

    private var siteCard: some View {
        Button { picking = .open } label: {
            HStack(spacing: Space.m) {
                VStack(alignment: .leading, spacing: 2) {
                    Text(site?.title ?? "Noch keine gewählt")
                        .foregroundStyle(Theme.textPrimary)
                        .lineLimit(2)
                        .multilineTextAlignment(.leading)
                    if let site {
                        Text([isToday ? "Heutiger Einsatz" : nil, site.number, site.addressLine].compactMap { $0 }.joined(separator: ", "))
                            .font(Typo.subhead)
                            .foregroundStyle(Theme.textSecondary)
                            .lineLimit(1)
                    }
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                Text(site == nil ? "Wählen" : "Ändern")
                    .foregroundStyle(Theme.accent)
            }
            .frame(minHeight: Size.minTap)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityIdentifier("hubSite")
    }

    private func tile(_ task: SiteTask, symbol: String, title: String, subtitle: String, id: String) -> some View {
        Button {
            if let site { start(task, for: site) } else { picking = task }
        } label: {
            Label {
                VStack(alignment: .leading, spacing: 1) {
                    Text(title).foregroundStyle(Theme.textPrimary)
                    Text(subtitle).font(Typo.subhead).foregroundStyle(Theme.textSecondary)
                }
            } icon: {
                Image(systemName: symbol).foregroundStyle(Theme.accent)
            }
            .frame(maxWidth: .infinity, minHeight: Size.minTap, alignment: .leading)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityIdentifier(id)
    }

    // MARK: Ablauf

    private func start(_ task: SiteTask, for bauakte: Bauakte) {
        switch task {
        case .photo, .scan: cover = Activity(task: task, bauakte: bauakte)
        case .open: site = bauakte
        default: sheet = Activity(task: task, bauakte: bauakte)
        }
    }

    @ViewBuilder
    private func form(_ activity: Activity) -> some View {
        let bauakte = activity.bauakte
        switch activity.task {
        case .diary: NewDiaryEntryView(bauakteId: bauakte.id) { finish("Eintrag gespeichert") }
        case .defect: NewWorkItemView(bauakteId: bauakte.id, initialKind: .defect) { finish(nil) }
        case .task: NewWorkItemView(bauakteId: bauakte.id, initialKind: .task) { finish(nil) }
        case .note: NewNoteView(bauakteId: bauakte.id) { finish(nil) }
        case .inspection: InspectionEntryView(bauakte: bauakte) { finish(nil) }
        case .acceptance: NewAcceptanceView(bauakte: bauakte) { finish(nil) }
        case .measurement: NewCmMeasurementView(bauakteId: bauakte.id) { finish(nil) }
        case .video: VideoCaptureView(bauakte: bauakte) { saved in finish(saved ? "Video gespeichert" : nil) }
        case .photo, .scan, .open: EmptyView()
        }
    }

    /// Neue Bauakte: Sie wird die gewählte Baustelle (sonst landeten die nächsten Fotos in der zuvor gewählten), dann
    /// startet, was im Anlageblatt unter „gleich danach“ gewählt wurde – Kamera, Erstbegehung oder die Akte.
    private func didCreate(_ id: UUID, next: NewBauakteView.Next) async {
        guard let bauakte = try? await model.active?.service?.bauakte(id) else {
            created = id
            return
        }
        site = bauakte
        isToday = todayIds.contains(id)
        if next.followUp == .open {
            created = id
        } else {
            start(next.followUp, for: bauakte)
        }
    }

    private func finish(_ message: String?) {
        sheet = nil
        if let message { toast = message }
    }

    /// Heutiger Einsatz, sonst die zuletzt geöffnete Baustelle.
    private func preselect() async {
        guard site == nil, let service = model.active?.service else { return }
        todayIds = await TodayContext.bauakteIds(model: model)
        if let id = todayIds.first, let bauakte = try? await service.bauakte(id) {
            site = bauakte
            isToday = true
        } else if let recent = try? await service.recentBauakten(limit: 1).first {
            site = recent
        }
    }
}
