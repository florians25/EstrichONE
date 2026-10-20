import EstrichCore
import SwiftUI

/// „Heute“ (07, Abschnitt 2): zuletzt geöffnete und zuletzt geänderte Bauakten, Hauptaktion „Neue Bauakte“.
/// „Mein Einsatz heute“ kommt mit der Einsatzplanung (AP-19).
struct TodayView: View {
    @Environment(AppModel.self) private var model
    @State private var recent: [Bauakte] = []
    @State private var changed: [Bauakte] = []
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
            .overlay(alignment: .bottomTrailing) {
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
        changed = ((try? await service.bauakten()) ?? []).filter { !recentIds.contains($0.id) }.prefix(10).map { $0 }
    }
}
