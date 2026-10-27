import Foundation

/// Rolle eines Beteiligten am Bauvorhaben (Server: `BauaktePartyRole`), in der Reihenfolge der Anzeige.
public enum PartyRole: String, CaseIterable, Sendable {
    case client
    case siteContact
    case siteManager
    case architect
    case planner
    case generalContractor
    case supplier
    case subcontractor
    case other

    /// Bezeichnungen wie im Büro (Verlauf der Bauakte).
    public var title: String {
        switch self {
        case .client: "Bauherr/Kunde"
        case .siteContact: "Ansprechpartner vor Ort"
        case .siteManager: "Bauleiter"
        case .architect: "Architekt"
        case .planner: "Planer"
        case .generalContractor: "Generalunternehmer"
        case .supplier: "Lieferant"
        case .subcontractor: "Nachunternehmer"
        case .other: "Beteiligter"
        }
    }

    var rank: Int { Self.allCases.firstIndex(of: self) ?? Self.allCases.count }
}

/// Beteiligter einer Bauakte (Sync-Entität `bauakteParty`): Bauherr, Bauleiter, Architekt, Lieferant … In der App
/// anlegen, ändern und entfernen (`bauakteParty.create/update/delete`, offline über die Outbox). Ist er mit dem
/// Kundenstamm verknüpft, gelten Name sowie fehlendes Telefon, fehlende E-Mail und die Anschrift des Kontakts.
public struct BauakteParty: Identifiable, Equatable, Sendable {
    public let id: UUID
    public let bauakteId: UUID?
    public let role: PartyRole
    public let contactId: UUID?
    public private(set) var name: String
    public let company: String?
    public private(set) var phone: String?
    public private(set) var email: String?
    public let note: String?
    /// Zweite Nummer (meist Mobil).
    public let mobile: String?
    public let street: String?
    public let postalCode: String?
    public let city: String?
    /// Hauptkontakt der Bauakte (höchstens einer).
    public let isPrimary: Bool
    /// Anschrift aus dem Kundenstamm (nur verknüpfte Kontakte).
    public private(set) var address: String?
    /// Weitere Telefonnummern aus dem Kundenstamm (ohne die Hauptnummer).
    public private(set) var morePhones: [ContactPhone] = []
    public let syncState: SyncState

    /// Initialen für den Avatar („AL“ für „Anna Lechner“).
    public var initials: String {
        let words = name.split(whereSeparator: { $0 == " " || $0 == "-" }).filter { $0.first?.isLetter == true }
        let letters = words.count >= 2 ? [words.first!, words.last!].compactMap(\.first) : Array(words.first?.prefix(2) ?? "")
        return String(letters).uppercased()
    }

    public init(_ entity: LocalEntity) {
        let data = entity.data
        func text(_ key: String) -> String? { data[key]?.string.flatMap { $0.isEmpty ? nil : $0 } }
        id = entity.id
        bauakteId = text("bauakteId").flatMap(UUID.init(uuidString:))
        role = text("role").flatMap(PartyRole.init(rawValue:)) ?? .other
        contactId = text("contactId").flatMap(UUID.init(uuidString:))
        name = text("name") ?? ""
        company = text("company")
        phone = text("phone")
        email = text("email")
        note = text("note")
        isPrimary = data["isPrimary"]?.bool ?? false
        mobile = text("mobile")
        street = text("street")
        postalCode = text("postalCode")
        city = text("city")
        address = Self.line(street, postalCode, city)
        syncState = entity.syncState
    }

    static func line(_ street: String?, _ postalCode: String?, _ city: String?) -> String? {
        let place = [postalCode, city].compactMap { $0?.isEmpty == false ? $0 : nil }.joined(separator: " ")
        let line = [street, place].compactMap { $0?.isEmpty == false ? $0 : nil }.joined(separator: ", ")
        return line.isEmpty ? nil : line
    }

    /// Alle Nummern zum Anrufen: eigene (Telefon, Mobil) zuerst, dann weitere aus dem Kundenstamm, ohne Doppelte.
    public var allPhones: [ContactPhone] {
        var result: [ContactPhone] = []
        if let phone { result.append(ContactPhone(kind: .office, number: phone)) }
        if let mobile { result.append(ContactPhone(kind: .mobile, number: mobile)) }
        for extra in morePhones where !result.contains(where: { $0.number.filter(\.isNumber) == extra.number.filter(\.isNumber) }) {
            result.append(extra)
        }
        return result
    }

    /// Mit dem verknüpften Kontakt ergänzt – wie die Liste im Büro.
    public func completed(with contact: Contact?) -> BauakteParty {
        guard let contact else { return self }
        var party = self
        if !contact.name.isEmpty { party.name = contact.name }
        party.phone = phone ?? contact.phone
        party.email = email ?? contact.email
        if party.address == nil { party.address = Self.line(contact.street, contact.postalCode, contact.city) }
        party.morePhones = contact.phones
        return party
    }
}

extension BauakteService {
    /// Beteiligte einer Bauakte nach Rolle und Name, ergänzt aus dem Kundenstamm.
    public func parties(of bauakteId: UUID) async throws -> [BauakteParty] {
        var result: [BauakteParty] = []
        for entity in try await store.entities(.bauakteParty, parent: bauakteId) {
            let party = BauakteParty(entity)
            var contact: Contact?
            if let contactId = party.contactId { contact = try await self.contact(contactId) }
            result.append(party.completed(with: contact))
        }
        return result.sorted { ($0.isPrimary ? 0 : 1, $0.role.rank, $0.name.lowercased()) < ($1.isPrimary ? 0 : 1, $1.role.rank, $1.name.lowercased()) }
    }
}

/// Bearbeitbare Angaben eines Beteiligten (Anlage und Änderung in der App).
public struct PartyEdit: Equatable, Sendable {
    public var role: PartyRole
    public var contactId: UUID?
    public var name: String
    public var company: String
    public var phone: String
    public var mobile: String
    public var email: String
    public var street: String
    public var postalCode: String
    public var city: String
    public var note: String
    public var isPrimary: Bool

    public init(role: PartyRole = .siteContact, contactId: UUID? = nil, name: String = "", company: String = "", phone: String = "",
                mobile: String = "", email: String = "", street: String = "", postalCode: String = "", city: String = "", note: String = "",
                isPrimary: Bool = false) {
        self.role = role
        self.contactId = contactId
        self.name = name
        self.company = company
        self.phone = phone
        self.mobile = mobile
        self.email = email
        self.street = street
        self.postalCode = postalCode
        self.city = city
        self.note = note
        self.isPrimary = isPrimary
    }

    /// Eigene Angaben des Eintrags (ohne die aus dem Kundenstamm ergänzten).
    public init(_ entity: LocalEntity) {
        let party = BauakteParty(entity)
        self.init(role: party.role, contactId: party.contactId, name: party.name, company: party.company ?? "", phone: party.phone ?? "",
                  mobile: party.mobile ?? "", email: party.email ?? "", street: party.street ?? "", postalCode: party.postalCode ?? "",
                  city: party.city ?? "", note: party.note ?? "", isPrimary: party.isPrimary)
    }

    /// Aus dem Kundenstamm übernehmen (Name, Firma, Telefon, E-Mail bleiben am Kontakt).
    public init(contact: Contact, role: PartyRole) {
        self.init(role: role, contactId: contact.id, name: contact.name)
    }

    public func validate() throws(BauakteError) {
        let trimmed = name.trimmingCharacters(in: .whitespacesAndNewlines)
        if trimmed.isEmpty, contactId == nil { throw .nameRequired }
        if trimmed.count > 200 { throw .tooLong("Name") }
        if company.count > 200 { throw .tooLong("Firma") }
        if phone.count > 50 { throw .tooLong("Telefon") }
        if mobile.count > 50 { throw .tooLong("Mobil") }
        if street.count > 200 { throw .tooLong("Straße") }
        if postalCode.count > 10 { throw .tooLong("PLZ") }
        if city.count > 100 { throw .tooLong("Ort") }
        if note.count > 1000 { throw .tooLong("Notiz") }
        let mail = email.trimmingCharacters(in: .whitespaces)
        if mail.count > 320 { throw .tooLong("E-Mail") }
        if !mail.isEmpty {
            try NewCustomer(name: "x", email: mail).validate()
        }
    }

    /// Alle Felder wie `SaveBauaktePartyRequest` (Operation `bauakteParty.save` ersetzt vollständig); leere Texte als `null`.
    var fields: [String: JSONValue] {
        func text(_ value: String) -> JSONValue {
            let trimmed = value.trimmingCharacters(in: .whitespacesAndNewlines)
            return trimmed.isEmpty ? .null : .string(trimmed)
        }
        return [
            "role": .string(role.rawValue), "contactId": JSONValue(contactId), "name": text(name), "company": text(company),
            "phone": text(phone), "mobile": text(mobile), "email": text(email), "street": text(street), "postalCode": text(postalCode),
            "city": text(city), "note": text(note), "isPrimary": .bool(isPrimary),
        ]
    }
}

extension BauakteService {
    /// Beteiligten anlegen oder ändern (`bauakteParty.save` mit allen Feldern, offline über die Outbox). Wird er
    /// Hauptkontakt, verliert ein bisheriger Hauptkontakt der Bauakte die Markierung (wie im Büro).
    @discardableResult
    public func saveParty(_ edit: PartyEdit, id: UUID? = nil, bauakteId: UUID) async throws(BauakteError) -> UUID {
        try edit.validate()
        do {
            let partyId = id ?? UUID.v7(now: currentDate())
            if let id, let existing = try await store.entity(.bauakteParty, id), PartyEdit(existing) == edit { return id }
            if edit.isPrimary {
                for other in try await store.entities(.bauakteParty, parent: bauakteId)
                where other.id != partyId && other.data["isPrimary"]?.bool == true {
                    var cleared = PartyEdit(other)
                    cleared.isPrimary = false
                    try await store.save(.bauakteParty, id: other.id, payload: cleared.fields.merging(["bauakteId": JSONValue(bauakteId)]) { $1 })
                }
            }
            try await store.save(.bauakteParty, id: partyId, payload: edit.fields.merging(["bauakteId": JSONValue(bauakteId)]) { $1 })
            return partyId
        } catch {
            throw .from(error)
        }
    }

    /// Beteiligten entfernen (`bauakteParty.delete`); der Kontakt im Kundenstamm bleibt.
    public func deleteParty(_ id: UUID) async throws(BauakteError) {
        do {
            try await store.delete(.bauakteParty, id: id)
        } catch {
            throw .from(error)
        }
    }

    /// Kontakte für „Bestehenden hinzufügen“: Suche im Kundenstamm des Geräts, bereits beteiligte ausgeblendet.
    public func partyCandidates(search: String, bauakteId: UUID) async throws -> [Contact] {
        let linked = Set(try await parties(of: bauakteId).compactMap(\.contactId))
        let text = search.trimmingCharacters(in: .whitespaces)
        return try await contacts(search: text.isEmpty ? nil : text.lowercased()).filter { !linked.contains($0.id) }
    }
}
