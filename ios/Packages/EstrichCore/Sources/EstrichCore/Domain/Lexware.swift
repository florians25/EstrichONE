import Foundation

/// Lexware Office in der App (nur lesend): Daten des verknüpften Lexware-Kontakts und zugeordnete Belege in der Bauakte,
/// Beleg als PDF und die Prüfung beim Anlegen eines Kunden. Rechnungen und Angebote entstehen ausschließlich in Lexware
/// Office; die App ordnet nichts zu und bearbeitet nichts.
public struct LexwareDeviationField: Decodable, Equatable, Sendable {
    public let field: String
    public let label: String?
    public let estrichOne: String?
    public let lexware: String?
}

public struct LexwareSyncState: Decodable, Equatable, Sendable {
    public let state: String
    public let syncedAt: String?
    public let deviations: [LexwareDeviationField]?
    public let error: String?

    public init(state: String, syncedAt: String? = nil, deviations: [LexwareDeviationField]? = nil, error: String? = nil) {
        self.state = state
        self.syncedAt = syncedAt
        self.deviations = deviations
        self.error = error
    }
}

public struct LexwareBauakteContact: Decodable, Equatable, Sendable {
    public let lexwareContactId: String
    public let name: String?
    public let number: String?
    public let url: String?
    public let street: String?
    public let postalCode: String?
    public let city: String?
    public let email: String?
    public let differingFields: [String]?
    public let sync: LexwareSyncState?
}

public struct LexwareVoucher: Decodable, Equatable, Identifiable, Sendable {
    public let id: String
    public let type: String
    public let number: String?
    public let voucherDate: String?
    public let grossAmount: Double?
    public let status: String

    public init(id: String, type: String, number: String?, voucherDate: String?, grossAmount: Double?, status: String) {
        self.id = id
        self.type = type
        self.number = number
        self.voucherDate = voucherDate
        self.grossAmount = grossAmount
        self.status = status
    }
}

/// Rechnungsplan der Bauakte (Modul „Verkauf“, nur lesend): `GET sales/bauakten/{id}/plan`.
public struct SalesPlan: Decodable, Equatable, Sendable {
    public let orderNet: Double?
    public let invoicedNet: Double
    public let text: String
    public let progressPercent: Double?

    public init(orderNet: Double?, invoicedNet: Double, text: String, progressPercent: Double?) {
        self.orderNet = orderNet
        self.invoicedNet = invoicedNet
        self.text = text
        self.progressPercent = progressPercent
    }

    /// Anteil der Auftragssumme, der berechnet ist (0 bis 1), für einen schlichten Balken; ohne Auftragssumme nichts.
    public var billedFraction: Double? {
        guard let orderNet, orderNet > 0 else { return nil }
        return min(1, max(0, invoicedNet / orderNet))
    }
}

/// Antwort von `GET bauakten/{id}/lexware`.
public struct BauakteLexware: Decodable, Equatable, Sendable {
    public let vouchers: [LexwareVoucher]
    public let contact: LexwareBauakteContact?
    public let summary: String?
}

public struct LexwareContactSummary: Decodable, Equatable, Sendable {
    public let id: String
    public let name: String
    public let customerNumber: String?
    public let street: String?
    public let postalCode: String?
    public let city: String?
    public let linkedContactId: String?
    public let linkedContactName: String?
}

public struct LexwareContactCandidate: Decodable, Equatable, Identifiable, Sendable {
    public let contact: LexwareContactSummary
    public let reasons: [String]

    public var id: String { contact.id }
}

/// Antwort von `POST lexware/contacts/check`.
public struct LexwareContactCheck: Decodable, Equatable, Sendable {
    public let checked: Bool
    public let message: String?
    public let candidates: [LexwareContactCandidate]
    public let canImport: Bool
}

public struct LexwareCheckRequest: Encodable, Equatable, Sendable {
    public let name: String?
    public let kind: String
    public let phone: String?
    public let street: String?
    public let postalCode: String?
    public let city: String?
}

public struct LexwareImportRequest: Encodable, Sendable {
    public let createAnyway: Bool

    public init(createAnyway: Bool = false) {
        self.createAnyway = createAnyway
    }
}

public struct LexwareImportResult: Decodable, Equatable, Sendable {
    public let imported: Bool
    public let contactId: String?
}

/// Farbton eines Status in der Oberfläche.
public enum LexwareTone: Equatable, Sendable {
    case neutral
    case info
    case success
    case warning
    case danger
}

/// Texte und Regeln der Lexware-Anzeige, gleich auf iOS und Android.
public enum Lexware {
    public static let existsWarning = "Achtung: Dieser Kunde ist bereits in Lexware Office vorhanden."
    public static let draftHint = "Für Entwürfe stellt Lexware Office kein PDF bereit."
    public static let offlineHint = "Das PDF ist nur mit Verbindung zum Server verfügbar."
    public static let differsText = "weicht von EstrichONE ab"

    public static func typeText(_ type: String) -> String {
        switch type {
        case "invoice": "Rechnung"
        case "quotation": "Angebot"
        case "creditnote": "Gutschrift"
        case "orderconfirmation": "Auftragsbestätigung"
        case "deliverynote": "Lieferschein"
        default: "Beleg"
        }
    }

    /// Belege ohne Zahlungsbedeutung: Auftragsbestätigung und Lieferschein sind festgeschrieben „erstellt“, nie „offen“.
    public static func isPayable(_ type: String) -> Bool { type == "invoice" || type == "creditnote" }

    /// Status je Belegart: „Offen“ nur bei Rechnung, Gutschrift und Angebot (dort: noch nicht angenommen).
    public static func statusText(_ status: String, type: String) -> String {
        if (type == "orderconfirmation" || type == "deliverynote") && status == "open" { return "Erstellt" }
        if !isPayable(type) && type != "quotation" && (status == "overdue" || status == "paid" || status == "paidoff") { return "Erstellt" }
        return statusText(status)
    }

    public static func statusTone(_ status: String, type: String) -> LexwareTone {
        statusText(status, type: type) == "Erstellt" ? .neutral : statusTone(status)
    }

    public static func statusText(_ status: String) -> String {
        switch status {
        case "draft": "Entwurf"
        case "open": "Offen"
        case "overdue": "Überfällig"
        case "paid", "paidoff": "Bezahlt"
        case "accepted": "Angenommen"
        case "rejected": "Abgelehnt"
        case "voided": "Storniert"
        default: "Unbekannt"
        }
    }

    public static func statusTone(_ status: String) -> LexwareTone {
        switch status {
        case "open": .info
        case "overdue", "rejected": .danger
        case "paid", "paidoff", "accepted": .success
        case "voided": .warning
        default: .neutral
        }
    }

    public static func pdfAvailable(_ voucher: LexwareVoucher) -> Bool {
        voucher.status != "draft"
    }

    /// Titel einer Belegzeile, z. B. „Rechnung RE1042“; Entwürfe haben noch keine Nummer.
    public static func title(_ voucher: LexwareVoucher) -> String {
        let number = voucher.number ?? (voucher.status == "draft" ? "wird automatisch vergeben" : "ohne Nummer")
        return "\(typeText(voucher.type)) \(number)"
    }

    public static func fileName(_ voucher: LexwareVoucher) -> String {
        let allowed = Set("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_")
        let number = String((voucher.number ?? "").filter { allowed.contains($0) })
        return number.isEmpty ? "\(typeText(voucher.type)).pdf" : "\(typeText(voucher.type))-\(number).pdf"
    }

    /// Betrag in deutscher Schreibweise, z. B. „1.190,00 €“.
    public static func euro(_ value: Double?) -> String {
        guard let value else { return "" }
        let cents = Int((abs(value) * 100).rounded())
        var whole = String(cents / 100)
        var grouped = ""
        while whole.count > 3 {
            grouped = "." + String(whole.suffix(3)) + grouped
            whole = String(whole.dropLast(3))
        }
        let fraction = cents % 100
        return "\(value < 0 ? "-" : "")\(whole)\(grouped),\(fraction < 10 ? "0" : "")\(fraction) €"
    }

    /// Datum `2026-10-01` als „01.10.2026“.
    public static func date(_ iso: String?) -> String {
        guard let iso else { return "" }
        let parts = iso.prefix(10).split(separator: "-")
        guard parts.count == 3 else { return "" }
        return "\(parts[2]).\(parts[1]).\(parts[0])"
    }

    /// Zweite Zeile eines Belegs: Datum und Betrag.
    public static func detail(_ voucher: LexwareVoucher) -> String {
        [date(voucher.voucherDate), euro(voucher.grossAmount)].filter { !$0.isEmpty }.joined(separator: ", ")
    }

    public static func address(_ contact: LexwareBauakteContact) -> String {
        let place = [contact.postalCode, contact.city].compactMap { $0 }.filter { !$0.isEmpty }.joined(separator: " ")
        return [contact.street ?? "", place].filter { !$0.isEmpty }.joined(separator: ", ")
    }

    public static func differs(_ contact: LexwareBauakteContact, _ fields: String...) -> Bool {
        let differing = contact.differingFields ?? []
        return fields.contains { differing.contains($0) }
    }

    /// Stand des Abgleichs als Text.
    public static func syncText(_ sync: LexwareSyncState?) -> String {
        guard let sync else { return "" }
        let count = sync.deviations?.count ?? 0
        switch sync.state {
        case "synced": return "Abgeglichen"
        case "deviations": return count == 1 ? "1 Abweichung offen" : "\(count) Abweichungen offen"
        case "pending": return "Abgleich steht aus"
        case "error": return "Abgleich fehlgeschlagen"
        case "missing": return "Kontakt in Lexware Office nicht mehr vorhanden"
        case "archived": return "Kontakt in Lexware Office archiviert"
        case "paused": return "Abgleich ruht"
        case "disabled": return "Abgleich ausgeschaltet"
        case "off": return "Änderungsabgleich ausgeschaltet"
        default: return ""
        }
    }

    public static func syncTone(_ sync: LexwareSyncState?) -> LexwareTone {
        switch sync?.state {
        case "synced": .success
        case "deviations", "archived": .warning
        case "pending": .info
        case "error", "missing": .danger
        default: .neutral
        }
    }

    /// „Zuletzt abgeglichen: 26.11.2026“ aus dem Zeitpunkt des Servers.
    public static func syncedAt(_ sync: LexwareSyncState?) -> String? {
        guard let at = sync?.syncedAt, at.count >= 10 else { return nil }
        let day = date(at)
        return day.isEmpty ? nil : "Zuletzt abgeglichen: \(day)"
    }

    /// Anfrage der Prüfung beim Anlegen eines Kunden; ohne Name ab drei Zeichen wird nicht geprüft.
    public static func checkRequest(name: String, company: Bool, street: String = "", postalCode: String = "", city: String = "",
                                    phone: String = "") -> LexwareCheckRequest? {
        func text(_ value: String) -> String? {
            let trimmed = value.trimmingCharacters(in: .whitespacesAndNewlines)
            return trimmed.isEmpty ? nil : trimmed
        }
        guard let trimmed = text(name), trimmed.count >= 3 else { return nil }
        return LexwareCheckRequest(name: trimmed, kind: company ? "company" : "person", phone: text(phone), street: text(street),
                                   postalCode: text(postalCode), city: text(city))
    }

    /// Zeile zu einem Treffer: Kundennummer, Anschrift und Grund der Übereinstimmung.
    public static func candidateLine(_ candidate: LexwareContactCandidate) -> String {
        let contact = candidate.contact
        let place = [contact.postalCode, contact.city].compactMap { $0 }.filter { !$0.isEmpty }.joined(separator: " ")
        let address = [contact.street ?? "", place].filter { !$0.isEmpty }.joined(separator: ", ")
        let number = contact.customerNumber.map { "Kundennummer \($0)" } ?? "ohne Kundennummer"
        return [number, address, candidate.reasons.joined(separator: ", ")].filter { !$0.isEmpty }.joined(separator: ", ")
    }

    /// Übernehmen geht nur mit dem Recht dazu und wenn der Kontakt noch zu keinem Kunden gehört.
    public static func canImport(_ check: LexwareContactCheck, _ candidate: LexwareContactCandidate) -> Bool {
        check.canImport && candidate.contact.linkedContactId == nil
    }
}

public extension AuthorizedClient {
    /// Rechnungsplan der Bauakte (Modul „Verkauf“); ohne Modul oder Recht liefert der Server einen Fehler.
    func salesPlan(_ bauakteId: UUID) async throws(APIError) -> SalesPlan {
        try await get("sales/bauakten/\(bauakteId.uuidString.lowercased())/plan")
    }

    /// Lexware-Daten der Bauakte: Kontakt des verknüpften Kunden, Abgleichstand und zugeordnete Belege.
    func bauakteLexware(_ bauakteId: UUID) async throws(APIError) -> BauakteLexware {
        try await get("bauakten/\(bauakteId.uuidString.lowercased())/lexware")
    }

    /// Beleg-PDF, vom Server aus Lexware Office durchgereicht; für Entwürfe meldet der Server, dass es kein PDF gibt.
    func lexwareVoucherPDF(bauakteId: UUID, linkId: String) async throws(APIError) -> Data {
        try await get("bauakten/\(bauakteId.uuidString.lowercased())/lexware/vouchers/\(linkId.lowercased())/document")
    }

    func checkLexwareContact(_ request: LexwareCheckRequest) async throws(APIError) -> LexwareContactCheck {
        try await post("lexware/contacts/check", body: request)
    }

    /// Kontakt aus Lexware Office als Kunde übernehmen: Der Server legt ihn mit den Lexware-Daten an und verknüpft ihn.
    func importLexwareContact(_ lexwareContactId: String) async throws(APIError) -> LexwareImportResult {
        try await post("lexware/contacts/\(lexwareContactId)/import", body: LexwareImportRequest())
    }
}
