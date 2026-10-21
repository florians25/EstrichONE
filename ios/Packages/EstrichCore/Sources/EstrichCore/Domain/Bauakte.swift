import Foundation

/// Status einer Bauakte (Server: `BauakteStatus`, docs/reengineering/05, Abschnitt 2.3).
public enum BauakteStatus: String, CaseIterable, Sendable {
    case inquiry
    case planned
    case inProgress
    case drying
    case completed
    case cancelled

    public var title: String {
        switch self {
        case .inquiry: "Anfrage"
        case .planned: "Geplant"
        case .inProgress: "In Ausführung"
        case .drying: "Belegreif / Trocknung"
        case .completed: "Abgeschlossen"
        case .cancelled: "Storniert"
        }
    }
}

/// Art der Bauakte bei der Schnellanlage (09, Abschnitt 2). Wird als `templateKey` gespeichert.
public enum BauakteTemplate: String, CaseIterable, Sendable {
    case standard
    case newBuild = "neubau"
    case renovation = "sanierung"
    case commercial = "gewerbe"

    public var title: String {
        switch self {
        case .standard: "Standard"
        case .newBuild: "Neubau"
        case .renovation: "Sanierung"
        case .commercial: "Gewerbe"
        }
    }
}

/// Bauakte für die Anzeige, gelesen aus dem lokalen Stand.
public struct Bauakte: Identifiable, Equatable, Sendable {
    public let id: UUID
    /// Vergibt der Server bei der Synchronisation; vorher `nil` („Neu · noch ohne Nummer“).
    public let number: String?
    public let title: String
    public let status: BauakteStatus
    public let street: String?
    public let postalCode: String?
    public let city: String?
    public let latitude: Double?
    public let longitude: Double?
    public let description: String?
    public let customerId: UUID?
    public let templateKey: String?
    /// Ansprechpartner vor Ort (z. B. Polier), vom Büro gepflegt.
    public let siteContactName: String?
    public let siteContactPhone: String?
    /// Begründung, falls storniert.
    public let cancellationReason: String?
    public let updatedAt: Date?
    public let syncState: SyncState

    public init(_ entity: LocalEntity) {
        let data = entity.data
        id = entity.id
        number = data["number"]?.string
        title = data["title"]?.string ?? ""
        status = data["status"]?.string.flatMap(BauakteStatus.init(rawValue:)) ?? .planned
        street = data["street"]?.string
        postalCode = data["postalCode"]?.string
        city = data["city"]?.string
        latitude = data["latitude"]?.number
        longitude = data["longitude"]?.number
        description = data["description"]?.string
        customerId = data["customerId"]?.string.flatMap(UUID.init(uuidString:))
        templateKey = data["templateKey"]?.string
        siteContactName = data["siteContactName"]?.string
        siteContactPhone = data["siteContactPhone"]?.string
        cancellationReason = data["cancellationReason"]?.string
        updatedAt = data["updatedAt"]?.string.flatMap(APIDate.parse)
        syncState = entity.syncState
    }

    /// „Musterstr. 12, 84028 Landshut“ – nur vorhandene Teile.
    public var addressLine: String? {
        let place = [postalCode, city].compactMap { $0?.isEmpty == false ? $0 : nil }.joined(separator: " ")
        let parts = [street, place].compactMap { $0?.isEmpty == false ? $0 : nil }
        return parts.isEmpty ? nil : parts.joined(separator: ", ")
    }

    public var hasLocation: Bool { latitude != nil && longitude != nil }
}

/// Kunde bzw. Kontakt (nur mit Recht `contacts.read` auf dem Gerät).
public struct Contact: Identifiable, Equatable, Sendable {
    public let id: UUID
    public let name: String
    public let isCompany: Bool
    public let customerNumber: String?
    public let street: String?
    public let postalCode: String?
    public let city: String?
    public let phone: String?
    public let email: String?
    public let syncState: SyncState

    public init(_ entity: LocalEntity) {
        let data = entity.data
        id = entity.id
        name = data["name"]?.string ?? ""
        isCompany = data["kind"]?.string == "company"
        customerNumber = data["customerNumber"]?.string
        street = data["street"]?.string
        postalCode = data["postalCode"]?.string
        city = data["city"]?.string
        phone = data["phone"]?.string
        email = data["email"]?.string
        syncState = entity.syncState
    }

    /// Kontakt aus einer Serverantwort (`ContactResponse`); `nil` ohne gültige ID.
    public init?(remote data: JSONValue) {
        guard let id = data["id"]?.string.flatMap(UUID.init(uuidString:)) else { return nil }
        self.init(LocalEntity(type: .contact, id: id, data: data, syncState: .synced))
    }

    /// „Ahornweg 3, 84028 Landshut“ – nur vorhandene Teile.
    public var addressLine: String? {
        let place = [postalCode, city].compactMap { $0?.isEmpty == false ? $0 : nil }.joined(separator: " ")
        let parts = [street, place].compactMap { $0?.isEmpty == false ? $0 : nil }
        return parts.isEmpty ? nil : parts.joined(separator: ", ")
    }

    /// Kurzinfo für Listen: Kundennummer · Ort (oder „Neu“, solange das Büro noch keine Nummer vergeben hat).
    public var subtitle: String {
        [customerNumber, city].compactMap { $0?.isEmpty == false ? $0 : nil }.joined(separator: " · ")
    }
}

/// Dokumentarten wie im Büro (Server: `DocumentCategories`).
public enum DocumentCategory: String, CaseIterable, Sendable, Identifiable {
    case delivery
    case acceptance
    case plan
    case order
    case offer
    case invoice
    case customer
    case office
    case other

    public var id: String { rawValue }

    public var title: String {
        switch self {
        case .offer: "Angebot"
        case .order: "Auftrag"
        case .invoice: "Rechnung"
        case .plan: "Plan"
        case .delivery: "Lieferschein"
        case .acceptance: "Abnahme"
        case .customer: "Kundenunterlagen"
        case .office: "Büro"
        case .other: "Sonstiges"
        }
    }

    public var symbol: String {
        switch self {
        case .offer: "doc.text"
        case .order: "doc.badge.gearshape"
        case .invoice: "eurosign.circle"
        case .plan: "ruler"
        case .delivery: "shippingbox"
        case .acceptance: "checkmark.seal"
        case .customer: "person.text.rectangle"
        case .office: "building.2"
        case .other: "doc"
        }
    }
}

/// Foto, Video oder Dokument einer Bauakte.
public struct FileItem: Identifiable, Equatable, Sendable {
    public let id: UUID
    public let bauakteId: UUID?
    public let kind: String
    public let originalName: String
    public let contentType: String
    public let sizeBytes: Int64
    public let caption: String?
    /// Nur bei Dokumenten (`DocumentCategory`).
    public let category: DocumentCategory?
    public let capturedAt: Date?
    public let createdAt: Date?
    /// `stored`, sobald der Server den Inhalt vollständig hat.
    public let isStoredOnServer: Bool
    public let syncState: SyncState

    public init(_ entity: LocalEntity) {
        let data = entity.data
        id = entity.id
        bauakteId = data["bauakteId"]?.string.flatMap(UUID.init(uuidString:))
        kind = data["kind"]?.string ?? "photo"
        originalName = data["originalName"]?.string ?? ""
        contentType = data["contentType"]?.string ?? "application/octet-stream"
        sizeBytes = Int64(data["sizeBytes"]?.number ?? 0)
        caption = data["caption"]?.string
        category = data["category"]?.string.flatMap(DocumentCategory.init(rawValue:))
        capturedAt = data["capturedAt"]?.string.flatMap(APIDate.parse)
        createdAt = data["createdAt"]?.string.flatMap(APIDate.parse)
        isStoredOnServer = data["state"]?.string == "stored"
        syncState = entity.syncState
    }

    public var isPhoto: Bool { kind == "photo" }
    public var isDocument: Bool { kind == "document" }
    public var isVideo: Bool { kind == "video" }
    public var isPDF: Bool { contentType == "application/pdf" || originalName.lowercased().hasSuffix(".pdf") }

    /// Anzeigename: Beschriftung, sonst Dateiname ohne Endung.
    public var displayName: String {
        if let caption, !caption.isEmpty { return caption }
        let name = Self.splitName(originalName).base
        return name.isEmpty ? "Datei" : name
    }

    /// Endung für eine temporäre Kopie (Vorschau, Teilen).
    public var fileExtension: String {
        let ext = Self.splitName(originalName).ext.lowercased()
        if !ext.isEmpty { return ext }
        return Self.fileExtension(forContentType: contentType)
    }

    /// Endung zum Inhaltstyp (ohne Endung im Namen erkennt QuickLook die Datei sonst nicht).
    static func fileExtension(forContentType contentType: String) -> String {
        switch contentType.lowercased().split(separator: ";").first.map(String.init) ?? "" {
        case "application/pdf": return "pdf"
        case "image/png": return "png"
        case "image/heic", "image/heif": return "heic"
        case "image/webp": return "webp"
        case "image/gif": return "gif"
        case "image/tiff": return "tiff"
        case "video/mp4": return "mp4"
        case "video/quicktime": return "mov"
        case "text/plain": return "txt"
        case "text/csv": return "csv"
        case "application/vnd.openxmlformats-officedocument.wordprocessingml.document": return "docx"
        case "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet": return "xlsx"
        case "application/msword": return "doc"
        case "application/vnd.ms-excel": return "xls"
        default: return contentType.hasPrefix("video/") ? "mp4" : (contentType.hasPrefix("image/") ? "jpg" : "bin")
        }
    }

    /// „Aufmaß EG.pdf“ → („Aufmaß EG“, „pdf“).
    static func splitName(_ name: String) -> (base: String, ext: String) {
        guard let dot = name.lastIndex(of: "."), dot != name.startIndex else { return (name, "") }
        return (String(name[..<dot]), String(name[name.index(after: dot)...]))
    }
}
