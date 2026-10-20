import EstrichCore
import SwiftUI

/// Reiter „Bauakten“ mit lokaler Suche über Nummer, Bezeichnung und Adresse (funktioniert offline).
struct BauaktenListView: View {
    @Environment(AppModel.self) private var model
    @State private var search = ""
    @State private var bauakten: [Bauakte] = []
    @State private var path: [UUID] = []
    @State private var creating = false
    @State private var captureFor: Bauakte?

    var body: some View {
        NavigationStack(path: $path) {
            List {
                if let sync = model.active?.sync {
                    SyncStatusBar(sync: sync).listRowInsets(EdgeInsets())
                }
                if bauakten.isEmpty {
                    Text(search.isEmpty ? "Noch keine Bauakten auf diesem Gerät." : "Keine Bauakte gefunden.")
                        .foregroundStyle(Theme.textSecondary)
                }
                ForEach(bauakten) { bauakte in
                    NavigationLink(value: bauakte.id) { BauakteRow(bauakte: bauakte) }
                        .contextMenu {
                            Button("Foto aufnehmen", systemImage: "camera") { captureFor = bauakte }
                            if let url = MapsLink.url(for: bauakte) {
                                Link(destination: url) { Label("Navigation starten", systemImage: "car") }
                            }
                        }
                }
            }
            .navigationTitle("Bauakten")
            .searchable(text: $search, prompt: "Nummer, Bauvorhaben oder Adresse")
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
            .task(id: "\(search)|\(model.active?.sync?.revision ?? 0)") {
                bauakten = (try? await model.active?.service?.bauakten(search: search)) ?? []
            }
            .refreshable { await model.active?.sync?.run() }
        }
    }
}

/// Navigation in der Karten-App des Systems statt eingebetteter Fremdkarte (02, Abschnitt 4).
enum MapsLink {
    static func url(for bauakte: Bauakte) -> URL? {
        var components = URLComponents(string: "https://maps.apple.com/")
        if let latitude = bauakte.latitude, let longitude = bauakte.longitude {
            components?.queryItems = [URLQueryItem(name: "daddr", value: "\(latitude),\(longitude)")]
        } else if let address = bauakte.addressLine {
            components?.queryItems = [URLQueryItem(name: "daddr", value: address)]
        } else {
            return nil
        }
        return components?.url
    }
}
