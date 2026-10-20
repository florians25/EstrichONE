import EstrichCore
import SwiftUI

/// Was im Reiter „Aufnehmen“ entsteht.
enum CaptureMode: String, CaseIterable, Identifiable {
    case photo
    case video
    case document

    var id: String { rawValue }

    var title: String {
        switch self {
        case .photo: "Fotos"
        case .video: "Video"
        case .document: "Scannen"
        }
    }

    var symbol: String {
        switch self {
        case .photo: "camera.fill"
        case .video: "video.fill"
        case .document: "doc.viewfinder"
        }
    }
}

/// Reiter „Aufnehmen“ (09, W-2): Fotos oder Dokument, Bauakte ist vorausgewählt (zuletzt geöffnet). 2–3 Tipps.
struct CaptureTabView: View {
    @Binding var mode: CaptureMode

    @Environment(AppModel.self) private var model
    @State private var recent: [Bauakte] = []
    @State private var all: [Bauakte] = []
    @State private var search = ""
    @State private var captureFor: Bauakte?
    @State private var scanFor: Bauakte?
    @State private var videoFor: Bauakte?
    @State private var lastResult: String?

    var body: some View {
        NavigationStack {
            List {
                Section {
                    Picker("Aufnahme", selection: $mode) {
                        ForEach(CaptureMode.allCases) { Label($0.title, systemImage: $0.symbol).tag($0) }
                    }
                    .pickerStyle(.segmented)
                    .accessibilityIdentifier("captureModePicker")
                    .listRowBackground(Color.clear)
                    .listRowInsets(EdgeInsets(top: 4, leading: 0, bottom: 4, trailing: 0))
                }
                if let lastResult {
                    StatusMessage(kind: .success, text: lastResult)
                        .listRowInsets(EdgeInsets())
                }
                if let first = recent.first, search.isEmpty {
                    Section("Vorausgewählt") {
                        HStack(spacing: 12) {
                            BauakteRow(bauakte: first)
                            Button {
                                start(first)
                            } label: {
                                Label(mode.title, systemImage: mode.symbol)
                            }
                            .estrichButton(mode == .photo ? .accent : .primary)
                            .accessibilityIdentifier("captureSuggested")
                        }
                    }
                }
                Section(search.isEmpty ? "Andere Bauakte" : "Treffer") {
                    if all.isEmpty {
                        Text("Keine Bauakte gefunden.").foregroundStyle(Theme.textSecondary)
                    }
                    ForEach(all) { bauakte in
                        Button {
                            start(bauakte)
                        } label: {
                            HStack {
                                BauakteRow(bauakte: bauakte)
                                Image(systemName: mode.symbol)
                                    .foregroundStyle(Theme.actionPrimary)
                                    .accessibilityHidden(true)
                            }
                        }
                        .buttonStyle(.plain)
                    }
                }
            }
            .navigationTitle("Aufnehmen")
            .searchable(text: $search, prompt: "Bauakte suchen")
            .fullScreenCover(item: $captureFor) { bauakte in
                CameraView(bauakte: bauakte) { count in
                    lastResult = count > 0 ? "\(count) \(count == 1 ? "Foto" : "Fotos") gespeichert." : nil
                    captureFor = nil
                }
            }
            .sheet(item: $videoFor) { bauakte in
                VideoCaptureView(bauakte: bauakte) { saved in
                    lastResult = saved ? "Video für „\(bauakte.title)“ gespeichert." : nil
                    videoFor = nil
                }
            }
            .fullScreenCover(item: $scanFor) { bauakte in
                ScanFlowView(bauakte: bauakte) { saved in
                    lastResult = saved ? "Dokument für „\(bauakte.title)“ gespeichert." : nil
                    scanFor = nil
                }
            }
            .task(id: "\(search)|\(model.active?.sync?.revision ?? 0)") {
                guard let service = model.active?.service else { return }
                recent = (try? await service.recentBauakten(limit: 1)) ?? []
                all = (try? await service.bauakten(search: search)) ?? []
            }
        }
    }

    private func start(_ bauakte: Bauakte) {
        lastResult = nil
        switch mode {
        case .photo: captureFor = bauakte
        case .video: videoFor = bauakte
        case .document: scanFor = bauakte
        }
    }
}

/// Bauakte wählen (z. B. Zuordnung der Fotos in der Kamera ändern).
struct BauaktePickerView: View {
    let selected: UUID?
    let onSelect: (Bauakte) -> Void

    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var search = ""
    @State private var bauakten: [Bauakte] = []

    var body: some View {
        NavigationStack {
            List(bauakten) { bauakte in
                Button {
                    onSelect(bauakte)
                } label: {
                    HStack {
                        BauakteRow(bauakte: bauakte)
                        if bauakte.id == selected {
                            Image(systemName: "checkmark").foregroundStyle(Theme.actionPrimary).fontWeight(.semibold)
                        }
                    }
                }
                .buttonStyle(.plain)
            }
            .searchable(text: $search, placement: .navigationBarDrawer(displayMode: .always), prompt: "Nummer, Bauvorhaben oder Adresse")
            .navigationTitle("Fotos zuordnen")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Abbrechen") { dismiss() } } }
            .task(id: search) { bauakten = (try? await model.active?.service?.bauakten(search: search)) ?? [] }
        }
    }
}
