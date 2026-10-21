import EstrichCore
import SwiftUI

/// Reiter „Bauakten“ mit lokaler Suche über Nummer, Bezeichnung und Adresse (funktioniert offline).
struct BauaktenListView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.horizontalSizeClass) private var sizeClass
    @State private var search = ""
    @State private var bauakten: [Bauakte] = []
    @State private var customers: [UUID: String] = [:]
    @State private var path: [UUID] = []
    @State private var selection: UUID?
    @State private var creating = false
    @State private var captureFor: Bauakte?
    @State private var inspectFor: Bauakte?

    var body: some View {
        Group {
            if sizeClass == .regular {
                // iPad: Liste links, Bauakte rechts (Querformat zweispaltig, Hochformat einblendbar).
                NavigationSplitView {
                    list
                        .navigationSplitViewColumnWidth(min: 300, ideal: 360, max: 440)
                } detail: {
                    NavigationStack {
                        if let selection {
                            BauakteDetailView(bauakteId: selection).id(selection)
                        } else {
                            ContentUnavailableView("Bauakte wählen", systemImage: "folder",
                                                   description: Text("Links eine Bauakte antippen oder mit ⌘N eine neue anlegen."))
                        }
                    }
                }
                .navigationSplitViewStyle(.balanced)
            } else {
                NavigationStack(path: $path) {
                    list
                        .navigationDestination(for: UUID.self) { BauakteDetailView(bauakteId: $0) }
                }
            }
        }
        .sheet(isPresented: $creating) {
            NewBauakteView { id, next in
                creating = false
                if sizeClass == .regular { selection = id } else { path.append(id) }
                if next != .open {
                    Task {
                        // Erst wenn das Anlageblatt geschlossen ist, Kamera bzw. Erstbegehung öffnen.
                        try? await Task.sleep(for: .milliseconds(600))
                        let created = try? await model.active?.service?.bauakte(id)
                        if next == .photograph { captureFor = created } else { inspectFor = created }
                    }
                }
            }
        }
        .fullScreenCover(item: $captureFor) { bauakte in
            CameraView(bauakte: bauakte) { _ in captureFor = nil }
        }
        .sheet(item: $inspectFor) { bauakte in
            InitialInspectionView(bauakte: bauakte) { inspectFor = nil }
        }
        .task(id: "\(search)|\(model.active?.sync?.revision ?? 0)") {
            guard let service = model.active?.service else { return }
            bauakten = (try? await service.bauakten(search: search)) ?? []
            let contacts = (try? await service.contacts()) ?? []
            customers = Dictionary(contacts.map { ($0.id, $0.name) }, uniquingKeysWith: { first, _ in first })
        }
    }

    private var list: some View {
        List(selection: sizeClass == .regular ? $selection : .constant(nil)) {
            if let sync = model.active?.sync {
                SyncStatusBar(sync: sync).listRowInsets(EdgeInsets())
            }
            if bauakten.isEmpty {
                Text(search.isEmpty ? "Noch keine Bauakten auf diesem Gerät." : "Keine Bauakte gefunden.")
                    .foregroundStyle(Theme.textSecondary)
            }
            ForEach(bauakten) { bauakte in
                NavigationLink(value: bauakte.id) {
                    BauakteRow(bauakte: bauakte, customerName: bauakte.customerId.flatMap { customers[$0] })
                }
                .swipeActions(edge: .leading) {
                    Button { captureFor = bauakte } label: { Label("Foto", systemImage: "camera.fill") }
                        .tint(Theme.accentPressed)
                }
                .contextMenu {
                    Button("Foto aufnehmen", systemImage: "camera") { captureFor = bauakte }
                    if let url = MapsLink.url(for: bauakte) {
                        Link(destination: url) { Label("Route starten", systemImage: "car") }
                    }
                }
            }
        }
        .listStyle(.plain)
        .navigationTitle("Bauakten")
        .searchable(text: $search, prompt: "Nummer, Bauvorhaben oder Adresse")
        .safeAreaInset(edge: .bottom, alignment: .trailing) {
            NewBauakteButton { creating = true }
        }
        .refreshable { await model.active?.sync?.run() }
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

/// Telefon und E-Mail als Systemlinks (Anruf über die Telefon-App, E-Mail über die Mail-App).
enum ContactLink {
    static func phone(_ number: String?) -> URL? {
        guard let number else { return nil }
        let digits = number.filter { $0.isNumber || $0 == "+" }
        return digits.count >= 3 ? URL(string: "tel:\(digits)") : nil
    }

    static func email(_ address: String?) -> URL? {
        guard let address = address?.trimmingCharacters(in: .whitespaces), address.contains("@") else { return nil }
        return URL(string: "mailto:\(address.addingPercentEncoding(withAllowedCharacters: .urlUserAllowed) ?? address)")
    }
}
