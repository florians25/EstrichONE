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
    /// Werte des Bauvorhabens (Fläche, Estrichart, Aufbau …).
    public var project: BauakteProject
    /// Geplanter Zeitraum im Format `JJJJ-MM-TT`.
    public var plannedStart: String?
    public var plannedEnd: String?

    public init(title: String, street: String? = nil, postalCode: String? = nil, city: String? = nil, latitude: Double? = nil,
                longitude: Double? = nil, description: String? = nil, customerId: UUID? = nil, template: BauakteTemplate = .standard,
                project: BauakteProject = BauakteProject(), plannedStart: String? = nil, plannedEnd: String? = nil) {
        self.project = project
        self.plannedStart = plannedStart
        self.plannedEnd = plannedEnd
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

/// Neuer Kunde, direkt bei der Anlage einer Bauakte erfasst (offline möglich, `contact.create`).
public struct NewCustomer: Sendable, Equatable {
    public var name: String
    public var isCompany: Bool
    public var phone: String?
    public var email: String?
    public var street: String?
    public var postalCode: String?
    public var city: String?

    public init(name: String = "", isCompany: Bool = false, phone: String? = nil, email: String? = nil, street: String? = nil,
                postalCode: String? = nil, city: String? = nil) {
        self.name = name
        self.isCompany = isCompany
        self.phone = phone
        self.email = email
        self.street = street
        self.postalCode = postalCode
        self.city = city
    }

    /// Prüft die Eingaben wie der Server, damit nichts erst bei der Übertragung abgelehnt wird.
    public func validate() throws(BauakteError) {
        let name = name.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !name.isEmpty else { throw .customerNameRequired }
        guard name.count <= 200 else { throw .tooLong("Name") }
        if let email = email?.trimmingCharacters(in: .whitespacesAndNewlines), !email.isEmpty {
            let parts = email.split(separator: "@", omittingEmptySubsequences: false)
            guard parts.count == 2, !parts[0].isEmpty, parts[1].contains("."), !email.contains(" "), email.count <= 320 else {
                throw .invalidEmail
            }
        }
        if let phone, phone.count > 50 { throw .tooLong("Telefon") }
        if let street, street.count > 200 { throw .tooLong("Straße") }
        if let postalCode, postalCode.count > 10 { throw .tooLong("PLZ") }
        if let city, city.count > 100 { throw .tooLong("Ort") }
    }
}

public enum BauakteError: Error, Equatable, Sendable {
    case titleRequired
    case textRequired
    case customerNameRequired
    case invalidEmail
    case tooLong(String)
    case cancellationReasonRequired
    case invalidPeriod
    case storage

    public var message: String {
        switch self {
        case .titleRequired: "Bitte eine Bezeichnung eingeben."
        case .invalidPeriod: "Das geplante Ende liegt vor dem Beginn."
        case .textRequired: "Bitte einen Text eingeben."
        case .customerNameRequired: "Bitte den Namen des Kunden eingeben."
        case .invalidEmail: "Die E-Mail-Adresse ist ungültig."
        case let .tooLong(field): "Die Angabe „\(field)“ ist zu lang."
        case .cancellationReasonRequired: "Bitte einen Stornogrund angeben."
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

    /// Aktuelle Zeit (im Test steuerbar); auch für Erweiterungen in anderen Dateien.
    func currentDate() -> Date { now() }

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
    /// Mit `newCustomer` wird zuerst der Kunde angelegt und verknüpft; seine Anlage steht in der Outbox vor der Bauakte.
    @discardableResult
    public func create(_ input: NewBauakte, newCustomer: NewCustomer? = nil) async throws(BauakteError) -> UUID {
        let title = input.title.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !title.isEmpty else { throw .titleRequired }
        if let start = input.plannedStart, let end = input.plannedEnd, end < start { throw .invalidPeriod }
        var input = input
        if let newCustomer {
            input.customerId = try await createCustomer(newCustomer)
        }
        let id = UUID.v7(now: now())
        func clean(_ text: String?) -> JSONValue {
            JSONValue(text.flatMap { $0.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? nil : $0.trimmingCharacters(in: .whitespacesAndNewlines) })
        }
        let payload: [String: JSONValue] = [
            "title": .string(title), "street": clean(input.street), "postalCode": clean(input.postalCode), "city": clean(input.city),
            "latitude": JSONValue(input.latitude), "longitude": JSONValue(input.longitude), "description": clean(input.description),
            "customerId": JSONValue(input.customerId), "templateKey": .string(input.template.rawValue),
            "project": input.project.isEmpty ? .null : input.project.json,
            "plannedStart": JSONValue(input.plannedStart), "plannedEnd": JSONValue(input.plannedEnd),
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

    /// Status ändern; Stornieren verlangt eine Begründung (wie im Büro). Offline möglich, der Server prüft Konflikte.
    public func changeStatus(_ status: BauakteStatus, reason: String? = nil, of id: UUID) async throws(BauakteError) {
        let reason = reason?.trimmingCharacters(in: .whitespacesAndNewlines)
        if status == .cancelled, reason?.isEmpty ?? true { throw .cancellationReasonRequired }
        do {
            try await store.setStatus(status.rawValue, reason: status == .cancelled ? reason.map { String($0.prefix(500)) } : nil, of: id)
        } catch {
            throw .storage
        }
    }

    /// Werte des Bauvorhabens ändern (`bauakte.update` mit dem ganzen Objekt `project`).
    public func updateProject(_ project: BauakteProject, of id: UUID) async throws(BauakteError) {
        do {
            try await store.update(.bauakte, id: id, changes: ["project": project.json])
        } catch {
            throw .storage
        }
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

    /// Legt einen Kunden lokal an (`contact.create`). Die Kundennummer vergibt später das Büro.
    @discardableResult
    public func createCustomer(_ input: NewCustomer) async throws(BauakteError) -> UUID {
        try input.validate()
        func clean(_ text: String?) -> JSONValue {
            let trimmed = text?.trimmingCharacters(in: .whitespacesAndNewlines)
            return JSONValue(trimmed?.isEmpty == false ? trimmed : nil)
        }
        let id = UUID.v7(now: now())
        let payload: [String: JSONValue] = [
            "name": .string(input.name.trimmingCharacters(in: .whitespacesAndNewlines)), "kind": .string(input.isCompany ? "company" : "person"),
            "phone": clean(input.phone), "email": clean(input.email), "street": clean(input.street),
            "postalCode": clean(input.postalCode), "city": clean(input.city),
        ]
        let local = JSONValue.object(payload).merging(["id": JSONValue(id), "customerNumber": .null, "notes": .null, "version": 0])
        do {
            try await store.create(.contact, id: id, data: local, payload: payload)
            try await remember(id, key: "recent_customers")
        } catch {
            throw .storage
        }
        return id
    }

    /// Ordnet einer bestehenden Bauakte einen Kunden zu (feldweise Änderung `customerId`).
    public func assignCustomer(_ customerId: UUID?, to bauakteId: UUID) async throws {
        try await store.update(.bauakte, id: bauakteId, changes: ["customerId": JSONValue(customerId)])
        if let customerId { try await remember(customerId, key: "recent_customers") }
    }

    /// Mögliche Dubletten beim Erfassen eines neuen Kunden: gleicher Name (ohne Groß-/Kleinschreibung, Leerzeichen),
    /// gleiche Telefonnummer oder E-Mail.
    public func similarCustomers(to input: NewCustomer) async throws -> [Contact] {
        func normalized(_ text: String?) -> String {
            (text ?? "").lowercased().split(whereSeparator: { $0 == " " }).joined(separator: " ")
        }
        func digits(_ text: String?) -> String { (text ?? "").filter(\.isNumber) }
        let name = normalized(input.name)
        let phone = digits(input.phone)
        let email = normalized(input.email)
        guard name.count >= 3 || phone.count >= 6 || !email.isEmpty else { return [] }
        return try await contacts().filter { contact in
            (name.count >= 3 && (normalized(contact.name) == name || normalized(contact.name).contains(name)))
                || (phone.count >= 6 && digits(contact.phone).hasSuffix(phone))
                || (!email.isEmpty && normalized(contact.email) == email)
        }
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

    func remember(_ id: UUID, key: String) async throws {
        var ids = try await ids(forKey: key).filter { $0 != id }
        ids.insert(id, at: 0)
        try await store.setMeta(key, try JSONValue.array(ids.prefix(10).map { JSONValue($0) }).encoded())
    }

    // MARK: Notizen

    public func notes(of bauakteId: UUID) async throws -> [Note] {
        try await store.entities(.note, parent: bauakteId).map(Note.init)
            .sorted { ($0.isPinned ? 1 : 0, $0.createdAt ?? .distantPast) > ($1.isPinned ? 1 : 0, $1.createdAt ?? .distantPast) }
    }

    @discardableResult
    public func addNote(_ text: String, to bauakteId: UUID) async throws(BauakteError) -> UUID {
        let text = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !text.isEmpty else { throw .textRequired }
        let id = UUID.v7(now: now())
        let payload: [String: JSONValue] = ["bauakteId": JSONValue(bauakteId), "text": .string(text)]
        let local = JSONValue.object(payload).merging([
            "id": JSONValue(id), "authorUserId": JSONValue(userId), "isPinned": false, "color": .null,
            "createdAt": .string(APIDate.format(now())), "editedAt": .null, "version": 0,
        ])
        do {
            try await store.create(.note, id: id, data: local, payload: payload)
        } catch {
            throw .storage
        }
        return id
    }

    // MARK: Aufgaben und Mängel

    public func workItems(of bauakteId: UUID) async throws -> [WorkItem] {
        Self.ordered(try await store.entities(.workItem, parent: bauakteId).map(WorkItem.init))
    }

    /// Reiter „Aufgaben“: mir zugewiesen oder von mir angelegt und ohne Zuständigen.
    public func myWorkItems(includeDone: Bool = false) async throws -> [WorkItem] {
        Self.ordered(try await store.entities(.workItem).map(WorkItem.init).filter { item in
            (includeDone || !item.isDone)
                && (item.assigneeUserId == userId || (item.assigneeUserId == nil && item.createdByUserId == userId))
        })
    }

    /// Offen vor erledigt, dann nach Fälligkeit (ohne Datum zuletzt), dann Titel.
    static func ordered(_ items: [WorkItem]) -> [WorkItem] {
        items.sorted { a, b in
            if a.isDone != b.isDone { return !a.isDone }
            switch (a.dueDate, b.dueDate) {
            case let (x?, y?) where x != y: return x < y
            case (_?, nil): return true
            case (nil, _?): return false
            default: return a.title.localizedCompare(b.title) == .orderedAscending
            }
        }
    }

    @discardableResult
    public func addWorkItem(_ input: NewWorkItem) async throws(BauakteError) -> UUID {
        let title = input.title.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !title.isEmpty else { throw .titleRequired }
        let id = UUID.v7(now: now())
        let description = input.description?.trimmingCharacters(in: .whitespacesAndNewlines)
        let payload: [String: JSONValue] = [
            "bauakteId": JSONValue(input.bauakteId), "kind": .string(input.kind.rawValue), "title": .string(title),
            "description": JSONValue(description?.isEmpty == false ? description : nil), "dueDate": JSONValue(input.dueDate),
            "assigneeUserId": JSONValue(input.assigneeUserId),
        ]
        let timestamp = JSONValue.string(APIDate.format(now()))
        let local = JSONValue.object(payload).merging([
            "id": JSONValue(id), "status": .string(WorkItemStatus.open.rawValue), "createdByUserId": JSONValue(userId),
            "completedAt": .null, "updatedAt": timestamp, "version": 0,
        ])
        do {
            try await store.create(.workItem, id: id, data: local, payload: payload)
        } catch {
            throw .storage
        }
        return id
    }

    public func setStatus(_ status: WorkItemStatus, of itemId: UUID) async throws {
        try await store.update(.workItem, id: itemId, changes: ["status": .string(status.rawValue)])
    }

    // MARK: Verzeichnis (für Namen offline zwischengespeichert)

    public func cachedDirectory() async throws -> [DirectoryUser] {
        guard let text = try await store.meta("directory") else { return [] }
        return (try? JSONCoding.decoder.decode([DirectoryUser].self, from: Data(text.utf8))) ?? []
    }

    public func saveDirectory(_ users: [DirectoryUser]) async throws {
        let data = try JSONCoding.encoder.encode(users)
        try await store.setMeta("directory", String(decoding: data, as: UTF8.self))
    }

    // MARK: Fotos

    public func files(of bauakteId: UUID) async throws -> [FileItem] {
        try await store.entities(.file, parent: bauakteId).map(FileItem.init)
    }

    /// Speichert ein Foto **zuerst** verschlüsselt auf dem Gerät, dann Ankündigung und Upload-Warteschlange
    /// (09, Abschnitt 5, Schritt 3). Erst danach gilt es als gespeichert.
    @discardableResult
    public func addPhoto(_ jpeg: Data, to bauakteId: UUID?, capturedAt: Date, latitude: Double? = nil, longitude: Double? = nil,
                         contentType: String = "image/jpeg", caption: String? = nil) async throws -> UUID {
        let fileExtension = contentType == "image/png" ? "png" : (contentType == "image/heic" ? "heic" : "jpg")
        return try await addMedia(jpeg, kind: "photo", prefix: "Foto", fileExtension: fileExtension, to: bauakteId, capturedAt: capturedAt,
                                  latitude: latitude, longitude: longitude, contentType: contentType, caption: caption)
    }

    /// Video (MP4 oder QuickTime): wie Fotos zuerst verschlüsselt auf dem Gerät, dann Ankündigung und Upload in Teilen.
    @discardableResult
    public func addVideo(_ movie: Data, to bauakteId: UUID?, capturedAt: Date, contentType: String = "video/mp4",
                         caption: String? = nil) async throws -> UUID {
        try await addMedia(movie, kind: "video", prefix: "Video", fileExtension: contentType == "video/quicktime" ? "mov" : "mp4",
                           to: bauakteId, capturedAt: capturedAt, latitude: nil, longitude: nil, contentType: contentType, caption: caption)
    }

    private func addMedia(_ content: Data, kind: String, prefix: String, fileExtension: String, to bauakteId: UUID?, capturedAt: Date,
                          latitude: Double?, longitude: Double?, contentType: String, caption: String?) async throws -> UUID {
        let id = UUID.v7(now: now())
        let sha = try files.store(content, for: id)
        let stamp = Self.fileNameFormatter.string(from: capturedAt)
        let payload: [String: JSONValue] = [
            "bauakteId": JSONValue(bauakteId), "kind": .string(kind), "originalName": .string("\(prefix)_\(stamp).\(fileExtension)"),
            "contentType": .string(contentType), "sizeBytes": .number(Double(content.count)), "sha256": .string(sha),
            "capturedAt": .string(APIDate.format(capturedAt)), "latitude": JSONValue(latitude), "longitude": JSONValue(longitude),
            "caption": JSONValue(Self.cleanCaption(caption)),
        ]
        let local = JSONValue.object(payload).merging([
            "id": JSONValue(id), "state": "awaitingUpload", "uploadedByUserId": JSONValue(userId),
            "createdAt": .string(APIDate.format(now())), "version": 0,
        ])
        do {
            try await store.create(.file, id: id, data: local, payload: payload, operation: "file.announce")
            try await store.enqueueUpload(fileId: id, size: Int64(content.count), sha256: sha, contentType: contentType)
        } catch {
            files.remove(id)
            throw error
        }
        return id
    }

    /// Legt ein Dokument (z. B. einen Scan als PDF) zuerst verschlüsselt auf dem Gerät ab, dann Ankündigung mit Titel
    /// und Dokumentart und Upload wie bei Fotos – offline möglich.
    @discardableResult
    public func addDocument(_ content: Data, title: String, category: DocumentCategory, to bauakteId: UUID,
                            contentType: String = "application/pdf") async throws(BauakteError) -> UUID {
        let title = title.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !title.isEmpty else { throw .titleRequired }
        guard title.count <= 500 else { throw .tooLong("Titel") }
        let id = UUID.v7(now: now())
        let sha: String
        do {
            sha = try files.store(content, for: id)
        } catch {
            throw .storage
        }
        let fileExtension = contentType == "application/pdf" ? "pdf" : (contentType == "image/png" ? "png" : "jpg")
        let payload: [String: JSONValue] = [
            "bauakteId": JSONValue(bauakteId), "kind": "document", "originalName": .string("\(Self.fileName(for: title)).\(fileExtension)"),
            "contentType": .string(contentType), "sizeBytes": .number(Double(content.count)), "sha256": .string(sha),
            "capturedAt": .string(APIDate.format(now())), "caption": .string(title), "category": .string(category.rawValue),
        ]
        let local = JSONValue.object(payload).merging([
            "id": JSONValue(id), "state": "awaitingUpload", "uploadedByUserId": JSONValue(userId),
            "createdAt": .string(APIDate.format(now())), "version": 0,
        ])
        do {
            try await store.create(.file, id: id, data: local, payload: payload, operation: "file.announce")
            try await store.enqueueUpload(fileId: id, size: Int64(content.count), sha256: sha, contentType: contentType)
        } catch {
            files.remove(id)
            throw .storage
        }
        return id
    }

    /// Beschriftung eines Fotos oder Titel eines Dokuments ändern. Vor der Übertragung fließt sie in die Ankündigung ein,
    /// danach als `file.update`.
    public func setCaption(_ caption: String?, of fileId: UUID) async throws {
        try await store.update(.file, id: fileId, changes: ["caption": JSONValue(Self.cleanCaption(caption))])
    }

    public func setCategory(_ category: DocumentCategory, of fileId: UUID) async throws {
        try await store.update(.file, id: fileId, changes: ["category": .string(category.rawValue)])
    }

    /// Noch nicht übertragene eigene Aufnahme verwerfen (z. B. verwackelt). Übertragene Dateien löscht das Büro.
    /// - Returns: `false`, wenn die Datei bereits beim Server ist.
    @discardableResult
    public func discardUnsentFile(_ fileId: UUID) async throws -> Bool {
        guard try await store.entity(.file, fileId)?.syncState == .pending,
              try await store.operations().contains(where: { $0.entityId == fileId && $0.type == "file.announce" && $0.state == .pending })
        else { return false }
        try await store.delete(.file, id: fileId)
        files.remove(fileId)
        return true
    }

    /// Dokumente der Bauakte, neueste zuerst.
    public func documents(of bauakteId: UUID) async throws -> [FileItem] {
        try await files(of: bauakteId).filter(\.isDocument)
            .sorted { ($0.createdAt ?? .distantPast) > ($1.createdAt ?? .distantPast) }
    }

    static func cleanCaption(_ caption: String?) -> String? {
        guard let trimmed = caption?.trimmingCharacters(in: .whitespacesAndNewlines), !trimmed.isEmpty else { return nil }
        return String(trimmed.prefix(500))
    }

    /// Dateiname aus einem Titel: ohne Pfadzeichen, höchstens 80 Zeichen.
    static func fileName(for title: String) -> String {
        let forbidden = CharacterSet(charactersIn: "/\\:*?\"<>|").union(.newlines).union(.controlCharacters)
        let cleaned = title.unicodeScalars.map { forbidden.contains($0) ? "_" : String($0) }.joined()
            .trimmingCharacters(in: .whitespaces)
        return cleaned.isEmpty ? "Dokument" : String(cleaned.prefix(80))
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
