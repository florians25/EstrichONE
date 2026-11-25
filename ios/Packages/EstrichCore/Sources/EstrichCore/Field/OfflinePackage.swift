import Foundation

/// „Für unterwegs“: Welche Bauakten vorab vollständig auf das Gerät geladen werden und welche Dateien dazugehören.
/// Die Daten einer Bauakte (Kontakte, Hinweise, Räume, Protokolle) liegen mit der Synchronisation ohnehin auf dem Gerät;
/// hier kommen die Dokumente (Pläne, Lieferscheine, Protokolle als PDF) und die letzten Fotos in Bildschirmgröße dazu,
/// damit sie auf der Baustelle auch ohne Netz geöffnet werden können.
public enum OfflinePlan {
    /// Einzelne Dateien bis zu dieser Größe werden vorab geladen.
    public static let maxFileBytes: Int64 = 30 * 1024 * 1024
    /// Obergrenze für alle vorab geladenen Dateien zusammen.
    public static let maxTotalBytes: Int64 = 500 * 1024 * 1024
    /// So viele der neuesten Fotos je Bauakte werden in Bildschirmgröße bereitgehalten.
    public static let photosPerBauakte = 40
    /// Angenommene Größe eines Fotos in Bildschirmgröße (für die Obergrenze).
    public static let photoEstimateBytes: Int64 = 400 * 1024
    /// Kopien von Bauakten, die nicht mehr geplant sind, bleiben noch so lange liegen.
    public static let keepAfterPlan: TimeInterval = 3 * 86_400

    /// Bauakten der heutigen und kommenden Einsätze in der Reihenfolge der Planung (heute zuerst), danach die von Hand
    /// gewählten. Vergangene Tage zählen nicht.
    public static func bauakteIds(assignments: [Assignment], today: String, todayExtra: [UUID] = [], pinned: [UUID] = []) -> [UUID] {
        let planned = assignments.filter { $0.day >= today }
            .sorted { ($0.day, $0.startTime ?? "99") < ($1.day, $1.startTime ?? "99") }
            .map(\.bauakteId)
        var seen = Set<UUID>()
        return (todayExtra + planned + pinned).filter { seen.insert($0).inserted }
    }

    /// Dateien einer Bauakte, die vorab geladen werden: alle Dokumente bis zur Größengrenze und die neuesten Fotos.
    /// Nur was schon beim Server liegt; eigene, noch nicht übertragene Aufnahmen sind ohnehin auf dem Gerät.
    public static func wanted(files: [FileItem]) -> [FileItem] {
        let documents = files.filter { $0.isDocument && $0.isStoredOnServer && $0.sizeBytes <= maxFileBytes }
            .sorted { ($0.createdAt ?? .distantPast) > ($1.createdAt ?? .distantPast) }
        let photos = files.filter { $0.isPhoto && $0.isStoredOnServer }
            .sorted { ($0.capturedAt ?? $0.createdAt ?? .distantPast) > ($1.capturedAt ?? $1.createdAt ?? .distantPast) }
            .prefix(photosPerBauakte)
        return documents + photos
    }

    /// Speicherbedarf einer vorab geladenen Datei: Dokumente im Original, Fotos in Bildschirmgröße.
    public static func cost(of file: FileItem) -> Int64 {
        file.isPhoto ? min(file.sizeBytes, photoEstimateBytes) : file.sizeBytes
    }

    /// Was davon noch zu laden ist, solange die Obergrenze reicht. `present` sind die Dateien, die schon auf dem Gerät liegen.
    public static func downloads(wanted: [FileItem], present: Set<UUID>, usedBytes: Int64, limit: Int64 = maxTotalBytes) -> [FileItem] {
        var used = usedBytes
        var result: [FileItem] = []
        for file in wanted where !present.contains(file.id) {
            let bytes = Self.cost(of: file)
            guard used + bytes <= limit else { continue }
            used += bytes
            result.append(file)
        }
        return result
    }
}

/// Stand einer Bauakte auf dem Gerät, für die Anzeige „Auf dem Gerät verfügbar“.
public struct OfflineAvailability: Equatable, Sendable {
    /// Dokumente der Bauakte beim Server und wie viele davon auf dem Gerät liegen.
    public let documents: Int
    public let documentsOnDevice: Int
    /// Fotos, die bereitgehalten werden sollen, und wie viele davon auf dem Gerät liegen.
    public let photos: Int
    public let photosOnDevice: Int
    /// Dokumente über der Größengrenze (werden bei Bedarf geladen).
    public let largeDocuments: Int
    /// Die Bauakte gehört zu den vorab geladenen (Einsatz heute oder in den nächsten Tagen, oder von Hand gewählt).
    public let isPlanned: Bool

    public init(documents: Int, documentsOnDevice: Int, photos: Int, photosOnDevice: Int, largeDocuments: Int, isPlanned: Bool) {
        self.documents = documents
        self.documentsOnDevice = documentsOnDevice
        self.photos = photos
        self.photosOnDevice = photosOnDevice
        self.largeDocuments = largeDocuments
        self.isPlanned = isPlanned
    }

    /// Aus den Dateien der Bauakte und dem, was auf dem Gerät liegt.
    public init(files: [FileItem], present: Set<UUID>, isPlanned: Bool) {
        let stored = files.filter(\.isStoredOnServer)
        let documents = stored.filter(\.isDocument)
        let wantedDocuments = documents.filter { $0.sizeBytes <= OfflinePlan.maxFileBytes }
        let wantedPhotos = OfflinePlan.wanted(files: files).filter(\.isPhoto)
        self.init(documents: wantedDocuments.count, documentsOnDevice: wantedDocuments.filter { present.contains($0.id) }.count,
                  photos: wantedPhotos.count, photosOnDevice: wantedPhotos.filter { present.contains($0.id) }.count,
                  largeDocuments: documents.count - wantedDocuments.count, isPlanned: isPlanned)
    }

    public enum State: Equatable, Sendable {
        /// Daten, Dokumente und Fotos liegen auf dem Gerät.
        case complete
        /// Daten liegen auf dem Gerät, Dateien werden noch geladen.
        case loading
        /// Nur die Daten; Dokumente und Fotos werden bei Bedarf geladen (nicht vorab geplant).
        case dataOnly
    }

    public var state: State {
        if documentsOnDevice >= documents, photosOnDevice >= photos { return .complete }
        return isPlanned ? .loading : .dataOnly
    }

    public var title: String {
        switch state {
        case .complete: "Auf dem Gerät verfügbar"
        case .loading: "Wird für unterwegs geladen"
        case .dataOnly: "Daten auf dem Gerät"
        }
    }

    /// „Daten, 3 Dokumente und 12 Fotos“ bzw. „2 von 5 Dokumenten geladen“.
    public var detail: String {
        switch state {
        case .complete:
            var parts = ["Daten und Kontakte"]
            if documents > 0 { parts.append(documents == 1 ? "1 Dokument" : "\(documents) Dokumente") }
            if photos > 0 { parts.append(photos == 1 ? "1 Foto" : "\(photos) Fotos") }
            let text = parts.count == 1 ? parts[0] : parts.dropLast().joined(separator: ", ") + " und " + parts[parts.count - 1]
            return largeDocuments > 0 ? "\(text). \(largeDocuments == 1 ? "1 großes Dokument wird" : "\(largeDocuments) große Dokumente werden") bei Bedarf geladen." : text
        case .loading:
            let total = documents + photos
            let done = min(documentsOnDevice, documents) + min(photosOnDevice, photos)
            return "\(done) von \(total) \(total == 1 ? "Datei" : "Dateien") geladen. Daten und Kontakte sind schon da."
        case .dataOnly:
            return "Dokumente und Fotos werden beim Öffnen geladen."
        }
    }
}

/// Verzeichnis der vorab geladenen Dateien (für die Obergrenze und das Aufräumen), in der Datenbank des Geräts.
public struct OfflineIndex: Codable, Equatable, Sendable {
    public struct Entry: Codable, Equatable, Sendable {
        public var bauakteId: UUID
        public var bytes: Int64
        public var storedAt: Date

        public init(bauakteId: UUID, bytes: Int64, storedAt: Date) {
            self.bauakteId = bauakteId
            self.bytes = bytes
            self.storedAt = storedAt
        }
    }

    public var files: [String: Entry] = [:]
    /// Von Hand gewählte Bauakten („Auf dem Gerät bereithalten“).
    public var pinned: [UUID] = []
    /// Wann eine Bauakte zuletzt zu den geplanten gehörte.
    public var lastPlanned: [String: Date] = [:]

    public init() {}

    public var totalBytes: Int64 { files.values.reduce(0) { $0 + $1.bytes } }

    public func contains(_ fileId: UUID) -> Bool { files[fileId.uuidString.lowercased()] != nil }

    public mutating func add(_ fileId: UUID, bauakteId: UUID, bytes: Int64, at date: Date) {
        files[fileId.uuidString.lowercased()] = Entry(bauakteId: bauakteId, bytes: bytes, storedAt: date)
    }

    public mutating func markPlanned(_ ids: [UUID], at date: Date) {
        for id in ids { lastPlanned[id.uuidString.lowercased()] = date }
    }

    /// Dateien, die entfernt werden können: ihre Bauakte ist seit `keep` nicht mehr geplant. Entfernt sie aus dem Verzeichnis.
    public mutating func removeExpired(planned: Set<UUID>, now: Date, keep: TimeInterval = OfflinePlan.keepAfterPlan) -> [UUID] {
        var removed: [UUID] = []
        for (key, entry) in files where !planned.contains(entry.bauakteId) {
            let last = lastPlanned[entry.bauakteId.uuidString.lowercased()] ?? entry.storedAt
            if now.timeIntervalSince(last) > keep, let id = UUID(uuidString: key) {
                removed.append(id)
            }
        }
        for id in removed { files[id.uuidString.lowercased()] = nil }
        lastPlanned = lastPlanned.filter { key, date in
            planned.contains { $0.uuidString.lowercased() == key } || now.timeIntervalSince(date) <= keep
        }
        return removed
    }

    /// Alles entfernen („Cache leeren“); liefert die Dateien, die gelöscht werden.
    public mutating func removeAll() -> [UUID] {
        let ids = files.keys.compactMap { UUID(uuidString: $0) }
        files = [:]
        return ids
    }

    static let metaKey = "offline_index"

    public static func load(from store: LocalStore) async -> OfflineIndex {
        guard let text = try? await store.meta(metaKey), !text.isEmpty,
              let index = try? JSONCoding.decoder.decode(OfflineIndex.self, from: Data(text.utf8)) else { return OfflineIndex() }
        return index
    }

    public func save(to store: LocalStore) async {
        guard let data = try? JSONCoding.encoder.encode(self) else { return }
        try? await store.setMeta(Self.metaKey, String(decoding: data, as: UTF8.self))
    }
}

extension BauakteService {
    /// Ob der Inhalt einer Datei auf dem Gerät liegt (eigene Aufnahme oder vorab geladen), ohne ihn zu lesen.
    public func hasLocalCopy(of fileId: UUID) -> Bool {
        files.contains(fileId)
    }

    /// Vom Server geladenen Inhalt verschlüsselt auf dem Gerät ablegen, damit er ohne Netz geöffnet werden kann.
    /// Eigene Aufnahmen, die noch übertragen werden, bleiben unberührt.
    public func storeOfflineCopy(_ content: Data, of fileId: UUID) throws {
        guard !files.contains(fileId) else { return }
        try files.store(content, for: fileId)
    }

    /// Vorab geladene Kopie wieder entfernen.
    public func removeOfflineCopy(of fileId: UUID) {
        files.remove(fileId)
    }
}
