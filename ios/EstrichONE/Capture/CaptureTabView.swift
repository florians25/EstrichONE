import EstrichCore
import SwiftUI

/// Reiter „Aufnehmen“ (09, W-2): Bauakte ist vorausgewählt (zuletzt geöffnet) → Kamera → „Fertig“. 3 Tipps.
struct CaptureTabView: View {
    @Environment(AppModel.self) private var model
    @State private var recent: [Bauakte] = []
    @State private var all: [Bauakte] = []
    @State private var search = ""
    @State private var captureFor: Bauakte?
    @State private var lastResult: (title: String, count: Int)?

    var body: some View {
        NavigationStack {
            List {
                if let lastResult, lastResult.count > 0 {
                    StatusMessage(kind: .success, text: "\(lastResult.count) \(lastResult.count == 1 ? "Foto" : "Fotos") für „\(lastResult.title)“ gespeichert.")
                        .listRowInsets(EdgeInsets())
                }
                if let first = recent.first, search.isEmpty {
                    Section("Vorausgewählt") {
                        Button {
                            captureFor = first
                        } label: {
                            HStack {
                                BauakteRow(bauakte: first)
                                Image(systemName: "camera.fill").font(.title2).foregroundStyle(Theme.actionPrimary)
                            }
                        }
                        .accessibilityIdentifier("captureSuggested")
                    }
                }
                Section(search.isEmpty ? "Andere Bauakte" : "Treffer") {
                    ForEach(all) { bauakte in
                        Button {
                            captureFor = bauakte
                        } label: {
                            BauakteRow(bauakte: bauakte)
                        }
                        .buttonStyle(.plain)
                    }
                }
            }
            .navigationTitle("Aufnehmen")
            .searchable(text: $search, prompt: "Bauakte suchen")
            .fullScreenCover(item: $captureFor) { bauakte in
                CameraView(bauakte: bauakte) { count in
                    lastResult = (bauakte.title, count)
                    captureFor = nil
                }
            }
            .task(id: "\(search)|\(model.active?.sync?.revision ?? 0)") {
                guard let service = model.active?.service else { return }
                recent = (try? await service.recentBauakten(limit: 1)) ?? []
                all = (try? await service.bauakten(search: search)) ?? []
            }
        }
    }
}
