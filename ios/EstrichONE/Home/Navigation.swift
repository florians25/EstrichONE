import EstrichCore
import SwiftUI

/// Ziele innerhalb der Reiter „Heute“ und „Mehr“.
enum HomeRoute: Hashable {
    case bauakte(UUID)
    case tasks
    case week
    case sync
    case dispatch
    case inbox
    case chat
}

/// Aktion, für die noch die Baustelle gewählt wird.
enum SiteTask: String, Identifiable {
    case open, photo, video, scan, diary, inspection, acceptance, defect, task, note, measurement

    var id: String { rawValue }

    var title: String {
        switch self {
        case .open: "Baustelle öffnen"
        case .photo: "Fotos aufnehmen"
        case .video: "Video aufnehmen"
        case .scan: "Dokument scannen"
        case .diary: "Bautagebuch"
        case .inspection: "Erstbegehung"
        case .acceptance: "Abnahme"
        case .defect: "Mangel melden"
        case .task: "Aufgabe anlegen"
        case .note: "Notiz schreiben"
        case .measurement: "CM-Messung"
        }
    }
}

/// Baustelle wählen: heutige Einsätze zuerst, dann die laufenden, mit Suche (offline auf dem Gerät).
struct BauakteQuickPicker: View {
    let title: String
    let todayIds: [UUID]
    let onDone: (Bauakte?) -> Void

    @Environment(AppModel.self) private var model
    @State private var search = ""
    @State private var today: [Bauakte] = []
    @State private var others: [Bauakte] = []
    @State private var loaded = false

    init(task: SiteTask, todayIds: [UUID], onDone: @escaping (Bauakte?) -> Void) {
        self.init(title: "\(task.title) – Baustelle wählen", todayIds: todayIds, onDone: onDone)
    }

    init(title: String, todayIds: [UUID], onDone: @escaping (Bauakte?) -> Void) {
        self.title = title
        self.todayIds = todayIds
        self.onDone = onDone
    }

    var body: some View {
        NavigationStack {
            List {
                if !today.isEmpty, search.isEmpty {
                    Section("Heute im Einsatz") { rows(today) }
                }
                Section(search.isEmpty ? "Laufende Baustellen" : "Treffer") {
                    if loaded, others.isEmpty {
                        Text(search.isEmpty ? "Keine weiteren laufenden Baustellen." : "Keine Bauakte gefunden.")
                            .foregroundStyle(Theme.textSecondary)
                    }
                    rows(others)
                }
            }
            .listStyle(.insetGrouped)
            .searchable(text: $search, placement: .navigationBarDrawer(displayMode: .always), prompt: "Nummer, Bauvorhaben oder Adresse")
            .navigationTitle(title)
            .navigationBarTitleDisplayMode(.inline)
            .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Abbrechen") { onDone(nil) } } }
            .task(id: search) { await load() }
        }
    }

    private func rows(_ list: [Bauakte]) -> some View {
        ForEach(list) { bauakte in
            Button { onDone(bauakte) } label: {
                HStack(spacing: Space.m) {
                    BauakteRow(bauakte: bauakte)
                    Chevron()
                }
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .accessibilityIdentifier("pick-\(bauakte.title)")
        }
    }

    private func load() async {
        guard let service = model.active?.service else { return }
        let all = (try? await service.bauakten(search: search.isEmpty ? nil : search)) ?? []
        let active = all.filter { $0.status != .cancelled && $0.status != .completed }
        let todaySet = Set(todayIds)
        var seen = Set<UUID>()
        today = todayIds.filter { seen.insert($0).inserted }.compactMap { id in all.first { $0.id == id } }
        others = active.filter { !todaySet.contains($0.id) || !search.isEmpty }
            .sorted { ($0.status.sortRank, $0.title) < ($1.status.sortRank, $1.title) }
        loaded = true
    }
}

/// Planung und Kolonne für heute aus dem zuletzt geladenen Stand (offline sofort verfügbar).
enum TodayContext {
    /// Bauakten mit einem Einsatz heute (meine Planung, sonst die Baustellen meiner Kolonne).
    @MainActor
    static func bauakteIds(model: AppModel) async -> [UUID] {
        guard let store = model.active?.sync?.store else { return [] }
        let day = CalendarDay.string(.now)
        let planned = (await MyPlanning(store: store).cached())?.assignments(on: day).map(\.bauakteId) ?? []
        if !planned.isEmpty { return planned }
        let board = await DispatchData.cached(day: day, model: model)?.board
        return board.map { board in board.sortedSites.filter { board.mySiteIds.contains($0.bauakteId) }.map(\.bauakteId) } ?? []
    }
}
