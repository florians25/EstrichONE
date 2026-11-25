import EstrichCore
import Foundation
import Observation
import SwiftUI

/// „Für unterwegs“: lädt die Dokumente und die neuesten Fotos der heutigen und kommenden Einsätze vorab auf das Gerät
/// (verschlüsselt, in der Ablage der Bauakten), damit sie auf der Baustelle ohne Netz geöffnet werden können. Die Daten
/// der Bauakten selbst (Kontakte, Hinweise, Räume, Protokolle) bringt die Synchronisation ohnehin mit.
/// Geladen wird nach einem Abgleich, höchstens alle paar Minuten, in Ruhe im Hintergrund der laufenden App; was nicht
/// mehr geplant ist, wird nach einigen Tagen wieder entfernt. „Cache leeren“ entfernt alles Vorabgeladene.
@MainActor
@Observable
final class OfflineCenter {
    private let store: LocalStore
    private let client: AuthorizedClient
    private let service: BauakteService

    /// Wird erhöht, sobald sich der Stand auf dem Gerät ändert (Ansichten laden ihre Anzeige neu).
    private(set) var revision = 0
    /// Es wird gerade geladen.
    private(set) var isLoading = false
    /// Bauakten, die vorab geladen werden (Einsatz heute oder in den nächsten Tagen, oder von Hand gewählt).
    private(set) var planned: Set<UUID> = []
    private(set) var pinned: Set<UUID> = []

    private var index: OfflineIndex?
    private var lastRun: Date?
    private var todayExtra: [UUID] = []

    init(store: LocalStore, client: AuthorizedClient, service: BauakteService) {
        self.store = store
        self.client = client
        self.service = service
    }

    private func loadedIndex() async -> OfflineIndex {
        if let index { return index }
        let loaded = await OfflineIndex.load(from: store)
        index = loaded
        pinned = Set(loaded.pinned)
        return loaded
    }

    private func keep(_ changed: OfflineIndex) async {
        index = changed
        pinned = Set(changed.pinned)
        await changed.save(to: store)
    }

    /// Nach einem Abgleich: Planung auswerten und Fehlendes laden. Höchstens alle 3 Minuten, außer mit `force`.
    /// `todayExtra` sind die heutigen Baustellen der eigenen Kolonne (wenn die persönliche Planung leer ist).
    func refresh(todayExtra: [UUID], force: Bool = false) async {
        self.todayExtra = todayExtra
        if isLoading { return }
        if !force, let lastRun, Date.now.timeIntervalSince(lastRun) < 180 { return }
        isLoading = true
        defer { isLoading = false }
        lastRun = .now

        var current = await loadedIndex()
        let today = CalendarDay.string(.now)
        // Planung frisch vom Server (heute und die nächsten sechs Tage); ohne Verbindung der zuletzt geladene Stand.
        let assignments = (await MyPlanning(store: store).load(using: client))?.range.assignments ?? []
        let ids = OfflinePlan.bauakteIds(assignments: assignments, today: today, todayExtra: todayExtra, pinned: current.pinned)
        if Set(ids) != planned { planned = Set(ids) }
        // Noch keine Planung bekannt: beim nächsten Abgleich gleich wieder nachsehen.
        if ids.isEmpty { lastRun = nil }
        current.markPlanned(ids, at: .now)

        // Aufräumen: Kopien von Bauakten, die seit einigen Tagen nicht mehr geplant sind.
        for fileId in current.removeExpired(planned: Set(ids), now: .now) {
            service.removeOfflineCopy(of: fileId)
        }
        await keep(current)

        var changed = false
        for bauakteId in ids {
            let files = (try? await service.files(of: bauakteId)) ?? []
            let wanted = OfflinePlan.wanted(files: files)
            let present = Set(wanted.filter { service.hasLocalCopy(of: $0.id) }.map(\.id))
            for file in OfflinePlan.downloads(wanted: wanted, present: present, usedBytes: current.totalBytes) {
                guard let content = await download(file) else {
                    if offline {
                        // Ohne Verbindung: beim nächsten Abgleich weitermachen.
                        await keep(current)
                        if changed { revision += 1 }
                        lastRun = nil
                        return
                    }
                    continue
                }
                let service = service
                let stored = await Task.detached(priority: .utility) { (try? service.storeOfflineCopy(content, of: file.id)) != nil }.value
                if stored {
                    current.add(file.id, bauakteId: bauakteId, bytes: Int64(content.count), at: .now)
                    changed = true
                }
            }
            if changed {
                await keep(current)
                revision += 1
                changed = false
            }
        }
        await keep(current)
        revision += 1
    }

    private var offline = false

    /// Dokumente im Original, Fotos in Bildschirmgröße. `nil`, wenn es gerade nicht geht.
    private func download(_ file: FileItem) async -> Data? {
        offline = false
        let path = "files/\(file.id.uuidString.lowercased())/content"
        do throws(APIError) {
            let data: Data = try await client.get(path, query: file.isPhoto ? [URLQueryItem(name: "variant", value: "display")] : [])
            return data.isEmpty ? nil : data
        } catch {
            if error == .offline { offline = true }
            return nil
        }
    }

    /// Stand einer Bauakte auf dem Gerät für die Anzeige „Auf dem Gerät verfügbar“.
    func availability(of bauakteId: UUID) async -> OfflineAvailability {
        _ = await loadedIndex()
        let files = (try? await service.files(of: bauakteId)) ?? []
        let wanted = OfflinePlan.wanted(files: files)
        let present = Set(wanted.filter { service.hasLocalCopy(of: $0.id) }.map(\.id))
        return OfflineAvailability(files: files, present: present, isPlanned: planned.contains(bauakteId) || pinned.contains(bauakteId))
    }

    /// „Auf dem Gerät bereithalten“ für eine Bauakte ein- oder ausschalten und gleich laden.
    func setPinned(_ bauakteId: UUID, _ value: Bool) async {
        var current = await loadedIndex()
        current.pinned.removeAll { $0 == bauakteId }
        if value { current.pinned.append(bauakteId) }
        await keep(current)
        revision += 1
        await refresh(todayExtra: todayExtra, force: true)
    }

    /// „Cache leeren“: alles Vorabgeladene entfernen. Beim nächsten Abgleich wird das Nötige wieder geladen.
    func clear() async {
        var current = await loadedIndex()
        for fileId in current.removeAll() {
            service.removeOfflineCopy(of: fileId)
        }
        await keep(current)
        lastRun = nil
        revision += 1
    }
}

/// Zeile im Kopf der Bauakte: „Auf dem Gerät verfügbar“ mit dem, was für die Arbeit ohne Netz bereitliegt.
struct OfflineAvailabilityLine: View {
    let availability: OfflineAvailability

    @State private var showsDetail = false

    private var symbol: String {
        switch availability.state {
        case .complete: "checkmark.circle.fill"
        case .loading: "arrow.down.circle"
        case .dataOnly: "internaldrive"
        }
    }

    var body: some View {
        Button { showsDetail = true } label: {
            Label {
                Text(availability.title)
            } icon: {
                Image(systemName: symbol)
            }
            .labelStyle(TightLabelStyle())
            .font(Typo.subhead)
            .foregroundStyle(availability.state == .complete ? Theme.success : Theme.textSecondary)
            .frame(minHeight: Size.minTap, alignment: .leading)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .padding(.vertical, -Space.m)
        .accessibilityHint("Zeigt, was ohne Netz verfügbar ist")
        .accessibilityIdentifier("offlineAvailability")
        .alert(availability.title, isPresented: $showsDetail) {
            Button("OK", role: .cancel) {}
        } message: {
            Text(availability.detail + " Neue Fotos, Einträge und Messungen werden immer zuerst auf dem Gerät gespeichert.")
        }
    }
}
