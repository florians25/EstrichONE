import Foundation

/// Eingaben der Schnellanlage (09). Pflicht ist nur die Bezeichnung.
public struct NewBauakte: Sendable {
    public var title: String
    public var street: String?
    public var postalCode: String?
    public var city: String?
    public var latitude: Double?
    public var longitude: Double?
    public var description: String?
    public var customerId: UUID?
    public var template: BauakteTemplate

    public init(title: String, street: String? = nil, postalCode: String? = nil, city: String? = nil, latitude: Double? = nil,
                longitude: Double? = nil, description: String? = nil, customerId: UUID? = nil, template: BauakteTemplate = .standard) {
        self.title = title
        self.street = street
        self.postalCode = postalCode
        self.city = city
        self.latitude = latitude
        self.longitude = longitude
        self.description = description
        self.customerId = customerId
        self.template = template
    }
}

public enum BauakteError: Error, Equatable, Sendable {
    case titleRequired
    case storage

    public var message: String {
        switch self {
        case .titleRequired: "Bitte eine Bezeichnung für das Bauvorhaben eingeben."
        case .storage: "Die Daten konnten auf diesem Gerät nicht gespeichert werden."
        }
    }
}

/// Fachliche Aktionen der App auf dem lokalen Stand (lokal zuerst, Synchronisation folgt).
public struct BauakteService: Sendable {
    public let store: LocalStore
    public let files: FileVault
    public let userId: UUID
    private let now: @Sendable () -> Date

    public init(store: LocalStore, files: FileVault, userId: UUID, now: @escaping @Sendable () -> Date = Date.init) {
        self.store = store
        self.files = files
        self.userId = userId
        self.now = now
    }

    // MARK: Bauakten

    public func bauakten(search: String? = nil) async throws -> [Bauakte] {
        try await store.entities(.bauakte, search: search).map(Bauakte.init)
    }

    public func bauakte(_ id: UUID) async throws -> Bauakte? {
        try await store.entity(.bauakte, id).map(Bauakte.init)
    }

    /// Legt eine Bauakte lokal an (eine Transaktion: Datensatz + Outbox). Die Nummer vergibt der Server.
    @discardableResult
    public func create(_ input: NewBauakte) async throws(BauakteError) -> UUID {
        let title = input.title.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !title.isEmpty else { throw .titleRequired }
        let id = UUID.v7(now: now())
        func clean(_ text: String?) -> JSONValue {
            JSONValue(text.flatMap { $0.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? nil : $0.trimmingCharacters(in: .whitespacesAndNewlines) })
        }
        let payload: [String: JSONValue] = [
            "title": .string(title), "street": clean(input.street), "postalCode": clean(input.postalCode), "city": clean(input.city),
            "latitude": JSONValue(input.latitude), "longitude": JSONValue(input.longitude), "description": clean(input.description),
            "customerId": JSONValue(input.customerId), "templateKey": .string(input.template.rawValue),
        ]
        let timestamp = JSONValue.string(APIDate.format(now()))
        let local = JSONValue.object(payload).merging([
            "id": JSONValue(id), "number": .null, "status": .string(BauakteStatus.planned.rawValue),
            "responsibleUserId": JSONValue(userId), "createdByUserId": JSONValue(userId), "createdAt": timestamp, "updatedAt": timestamp,
            "version": 0,
        ])
        do {
            try await store.create(.bauakte, id: id, data: local, payload: payload)
            try await markOpened(id)
            if let customer = input.customerId {
                try await remember(customer, key: "recent_customers")
            }
        } catch {
            throw .storage
        }
        return id
    }

    public func update(_ id: UUID, changes: [String: JSONValue]) async throws {
        try await store.update(.bauakte, id: id, changes: changes)
    }

    public func similar(to candidate: DuplicateFinder.Candidate) async throws -> [Bauakte] {
        DuplicateFinder.matches(candidate, in: try await bauakten())
    }

    // MARK: Kunden

    public func contacts(search: String? = nil) async throws -> [Contact] {
        try await store.entities(.contact, search: search).map(Contact.init).sorted { $0.name.localizedCompare($1.name) == .orderedAscending }
    }

    public func contact(_ id: UUID) async throws -> Contact? {
        try await store.entity(.contact, id).map(Contact.init)
    }

    /// Die drei zuletzt bei einer Anlage gewählten Kunden (09, Abschnitt 2: Chips „Zuletzt“).
    public func recentCustomers() async throws -> [Contact] {
        var result: [Contact] = []
        for id in try await ids(forKey: "recent_customers") {
            if let contact = try await contact(id) { result.append(contact) }
        }
        return Array(result.prefix(3))
    }

    // MARK: Zuletzt geöffnet (Startseite „Heute“)

    public func markOpened(_ id: UUID) async throws {
        try await remember(id, key: "recent_bauakten")
    }

    public func recentBauakten(limit: Int = 5) async throws -> [Bauakte] {
        var result: [Bauakte] = []
        for id in try await ids(forKey: "recent_bauakten") {
            if let bauakte = try await bauakte(id) { result.append(bauakte) }
            if result.count == limit { break }
        }
        return result
    }

    private func ids(forKey key: String) async throws -> [UUID] {
        guard let text = try await store.meta(key), case let .array(values) = try JSONValue.decode(text) else { return [] }
        return values.compactMap { $0.string.flatMap(UUID.init(uuidString:)) }
    }

    private func remember(_ id: UUID, key: String) async throws {
        var ids = try await ids(forKey: key).filter { $0 != id }
        ids.insert(id, at: 0)
        try await store.setMeta(key, try JSONValue.array(ids.prefix(10).map { JSONValue($0) }).encoded())
    }

    // MARK: Fotos

    public func files(of bauakteId: UUID) async throws -> [FileItem] {
        try await store.entities(.file, parent: bauakteId).map(FileItem.init)
    }

    /// Speichert ein Foto **zuerst** verschlüsselt auf dem Gerät, dann Ankündigung und Upload-Warteschlange
    /// (09, Abschnitt 5, Schritt 3). Erst danach gilt es als gespeichert.
    @discardableResult
    public func addPhoto(_ jpeg: Data, to bauakteId: UUID, capturedAt: Date, latitude: Double? = nil, longitude: Double? = nil,
                         contentType: String = "image/jpeg") async throws -> UUID {
        let id = UUID.v7(now: now())
        let sha = try files.store(jpeg, for: id)
        let fileExtension = contentType == "image/png" ? "png" : (contentType == "image/heic" ? "heic" : "jpg")
        let stamp = Self.fileNameFormatter.string(from: capturedAt)
        let payload: [String: JSONValue] = [
            "bauakteId": JSONValue(bauakteId), "kind": "photo", "originalName": .string("Foto_\(stamp).\(fileExtension)"),
            "contentType": .string(contentType), "sizeBytes": .number(Double(jpeg.count)), "sha256": .string(sha),
            "capturedAt": .string(APIDate.format(capturedAt)), "latitude": JSONValue(latitude), "longitude": JSONValue(longitude),
            "caption": .null,
        ]
        let local = JSONValue.object(payload).merging([
            "id": JSONValue(id), "state": "awaitingUpload", "uploadedByUserId": JSONValue(userId),
            "createdAt": .string(APIDate.format(now())), "version": 0,
        ])
        do {
            try await store.create(.file, id: id, data: local, payload: payload, operation: "file.announce")
            try await store.enqueueUpload(fileId: id, size: Int64(jpeg.count), sha256: sha, contentType: contentType)
        } catch {
            files.remove(id)
            throw error
        }
        return id
    }

    /// Inhalt eines Fotos, solange es auf dem Gerät liegt (eigene Aufnahmen, 7 Tage nach dem Upload).
    public func localContent(of fileId: UUID) -> Data? {
        files.contains(fileId) ? try? files.read(fileId) : nil
    }

    private static let fileNameFormatter: DateFormatter = {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.dateFormat = "yyyyMMdd_HHmmss"
        return formatter
    }()
}
