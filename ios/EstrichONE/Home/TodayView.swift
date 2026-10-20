import EstrichCore
import SwiftUI

/// „Heute“ (07, Abschnitt 2): meine offenen Aufgaben, zuletzt geöffnete und zuletzt geänderte Bauakten,
/// Hauptaktion „Neue Bauakte“.
/// „Mein Einsatz heute“ kommt mit der Einsatzplanung (AP-19).
struct TodayView: View {
    @Environment(AppModel.self) private var model
    @State private var recent: [Bauakte] = []
    @State private var changed: [Bauakte] = []
    @State private var tasks: [WorkItem] = []
    @State private var titles: [UUID: String] = [:]
    @State private var path: [UUID] = []
    @State private var creating = false
    @State private var captureFor: Bauakte?

    var body: some View {
        NavigationStack(path: $path) {
            List {
                Section {
                    CurrentUserHeader()
                }
                if let sync = model.active?.sync {
                    SyncStatusBar(sync: sync)
                        .listRowInsets(EdgeInsets())
                }
                if !tasks.isEmpty {
                    Section {
                        ForEach(tasks.prefix(5)) { item in
                            let row = WorkItemRow(item: item, showsBauakte: item.bauakteId.flatMap { titles[$0] }) {
                                Task { await toggle(item) }
                            }
                            if let bauakteId = item.bauakteId {
                                NavigationLink(value: bauakteId) { row }
                            } else {
                                row
                            }
                        }
                    } header: {
                        Text("Meine Aufgaben (\(tasks.count))")
                    }
                    .accessibilityIdentifier("myTasksSection")
                }
                if !recent.isEmpty {
                    Section("Zuletzt geöffnet") {
                        ForEach(recent) { bauakte in
                            NavigationLink(value: bauakte.id) { BauakteRow(bauakte: bauakte) }
                        }
                    }
                }
                Section("Zuletzt geändert") {
                    if changed.isEmpty {
                        Text("Noch keine Bauakten auf diesem Gerät. Neue Bauakten legen Sie mit „Neue Bauakte“ an; vorhandene erscheinen nach der Synchronisation.")
                            .foregroundStyle(Theme.textSecondary)
                    }
                    ForEach(changed) { bauakte in
                        NavigationLink(value: bauakte.id) { BauakteRow(bauakte: bauakte) }
                    }
                }
            }
            .navigationTitle("Heute")
            .navigationDestination(for: UUID.self) { BauakteDetailView(bauakteId: $0) }
            .safeAreaInset(edge: .bottom, alignment: .trailing) {
                NewBauakteButton { creating = true }
            }
            .sheet(isPresented: $creating) {
                NewBauakteView { id, photograph in
                    creating = false
                    path.append(id)
                    if photograph {
                        Task {
                            // Erst wenn das Anlageblatt geschlossen ist, die Kamera öffnen.
                            try? await Task.sleep(for: .milliseconds(600))
                            captureFor = try? await model.active?.service?.bauakte(id)
                        }
                    }
                }
            }
            .fullScreenCover(item: $captureFor) { bauakte in
                CameraView(bauakte: bauakte) { _ in captureFor = nil }
            }
            .task(id: model.active?.sync?.revision) { await reload() }
            .refreshable { await model.active?.sync?.run() }
        }
    }

    private func reload() async {
        guard let service = model.active?.service else { return }
        recent = (try? await service.recentBauakten()) ?? []
        let recentIds = Set(recent.map(\.id))
        let all = (try? await service.bauakten()) ?? []
        changed = all.filter { !recentIds.contains($0.id) }.prefix(10).map { $0 }
        titles = Dictionary(all.map { ($0.id, [$0.number, $0.title].compactMap { $0 }.joined(separator: " · ")) },
                            uniquingKeysWith: { first, _ in first })
        tasks = (try? await service.myWorkItems()) ?? []
    }

    private func toggle(_ item: WorkItem) async {
        try? await model.active?.service?.setStatus(item.isDone ? .open : .done, of: item.id)
        model.active?.sync?.localChangeMade()
        await reload()
    }
}
