import Foundation

/// Art einer Telefonnummer (wie im Büro).
public enum ContactPhoneKind: String, CaseIterable, Identifiable, Sendable {
    case mobile, landline, office, fax, `private`
    public var id: String { rawValue }
    public var title: String {
        switch self {
        case .mobile: "Mobil"
        case .landline: "Festnetz"
        case .office: "Büro"
        case .fax: "Fax"
        case .private: "Privat"
        }
    }
}

/// Bevorzugter Kontaktweg.
public enum ContactChannel: String, CaseIterable, Identifiable, Sendable {
    case phone, mobile, email, letter
    public var id: String { rawValue }
    public var title: String {
        switch self {
        case .phone: "Telefon"
        case .mobile: "Mobil"
        case .email: "E-Mail"
        case .letter: "Brief"
        }
    }
}

public struct ContactPhone: Equatable, Sendable, Identifiable {
    public var id = UUID()
    public var kind: ContactPhoneKind
    public var number: String
    public var label: String?

    public init(kind: ContactPhoneKind = .mobile, number: String, label: String? = nil) {
        self.kind = kind
        self.number = number
        self.label = label
    }

    init?(json: JSONValue) {
        guard let number = json["number"]?.string, !number.isEmpty else { return nil }
        self.init(kind: json["kind"]?.string.flatMap(ContactPhoneKind.init(rawValue:)) ?? .landline, number: number, label: json["label"]?.string)
    }

    var json: JSONValue {
        .object(["kind": .string(kind.rawValue), "number": .string(number.trimmingCharacters(in: .whitespaces)), "label": Self.clean(label, 50)])
    }

    public static func == (lhs: ContactPhone, rhs: ContactPhone) -> Bool {
        lhs.kind == rhs.kind && lhs.number == rhs.number && lhs.label == rhs.label
    }

    static func clean(_ text: String?, _ max: Int) -> JSONValue {
        let trimmed = text?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        return trimmed.isEmpty ? .null : .string(String(trimmed.prefix(max)))
    }
}

public struct ContactEmail: Equatable, Sendable, Identifiable {
    public var id = UUID()
    public var address: String
    public var label: String?

    public init(address: String, label: String? = nil) {
        self.address = address
        self.label = label
    }

    init?(json: JSONValue) {
        guard let address = json["address"]?.string, !address.isEmpty else { return nil }
        self.init(address: address, label: json["label"]?.string)
    }

    var json: JSONValue {
        .object(["address": .string(address.trimmingCharacters(in: .whitespaces)), "label": ContactPhone.clean(label, 50)])
    }

    public static func == (lhs: ContactEmail, rhs: ContactEmail) -> Bool {
        lhs.address == rhs.address && lhs.label == rhs.label
    }
}

/// Abweichende Anschrift (Rechnung, Lieferung) – nur Anzeige, gepflegt im Büro.
public struct ContactAddress: Equatable, Sendable {
    public let name: String?
    public let street: String?
    public let postalCode: String?
    public let city: String?

    init?(json: JSONValue?) {
        guard let json, json.object != nil else { return nil }
        name = json["name"]?.string
        street = json["street"]?.string
        postalCode = json["postalCode"]?.string
        city = json["city"]?.string
        if line == nil { return nil }
    }

    public var line: String? {
        let place = [postalCode, city].compactMap { $0?.isEmpty == false ? $0 : nil }.joined(separator: " ")
        let parts = [name, street, place].compactMap { $0?.isEmpty == false ? $0 : nil }
        return parts.isEmpty ? nil : parts.joined(separator: ", ")
    }
}

/// Bearbeitbare Angaben eines Kunden in der App (Anlage und Änderung).
public struct CustomerEdit: Equatable, Sendable {
    public var name: String
    public var isCompany: Bool
    public var salutation: String
    public var title: String
    public var street: String
    public var postalCode: String
    public var city: String
    public var phones: [ContactPhone]
    public var emails: [ContactEmail]
    public var preferredChannel: ContactChannel?
    public var website: String
    public var notes: String

    public init(name: String = "", isCompany: Bool = false) {
        self.name = name
        self.isCompany = isCompany
        salutation = ""
        title = ""
        street = ""
        postalCode = ""
        city = ""
        phones = []
        emails = []
        preferredChannel = nil
        website = ""
        notes = ""
    }

    public init(_ contact: Contact) {
        self.init(name: contact.name, isCompany: contact.isCompany)
        salutation = contact.salutation ?? ""
        title = contact.title ?? ""
        street = contact.street ?? ""
        postalCode = contact.postalCode ?? ""
        city = contact.city ?? ""
        phones = contact.phones
        emails = contact.emails
        preferredChannel = contact.preferredChannel
        website = contact.website ?? ""
        notes = contact.notes ?? ""
    }

    /// Für die Dublettenprüfung des Servers.
    public var asNewCustomer: NewCustomer {
        NewCustomer(name: name, isCompany: isCompany, phone: phones.first?.number, email: emails.first?.address,
                    street: street, postalCode: postalCode, city: city)
    }

    public func validate() throws(BauakteError) {
        try asNewCustomer.validate()
        let numbers = phones.filter { !$0.number.trimmingCharacters(in: .whitespaces).isEmpty }
        let addresses = emails.filter { !$0.address.trimmingCharacters(in: .whitespaces).isEmpty }
        if numbers.count > 10 || addresses.count > 10 { throw .tooLong("Telefonnummern/E-Mail-Adressen (höchstens 10)") }
        if numbers.contains(where: { $0.number.count > 50 }) { throw .tooLong("Telefon") }
        for email in addresses {
            try NewCustomer(name: name, email: email.address).validate()
        }
        if notes.count > 4000 { throw .tooLong("Notiz") }
        if website.count > 300 { throw .tooLong("Website") }
    }

    /// Alle Felder für `contact.create` bzw. die geänderten für `contact.update`.
    var fields: [String: JSONValue] {
        func text(_ value: String, _ max: Int) -> JSONValue { ContactPhone.clean(value, max) }
        let numbers = phones.filter { !$0.number.trimmingCharacters(in: .whitespaces).isEmpty }
        let addresses = emails.filter { !$0.address.trimmingCharacters(in: .whitespaces).isEmpty }
        return [
            "name": .string(name.trimmingCharacters(in: .whitespacesAndNewlines)), "kind": .string(isCompany ? "company" : "person"),
            "salutation": text(salutation, 50), "title": text(title, 50), "street": text(street, 200), "postalCode": text(postalCode, 10),
            "city": text(city, 100), "phones": .array(numbers.map(\.json)), "emails": .array(addresses.map(\.json)),
            "preferredChannel": JSONValue(preferredChannel?.rawValue), "website": text(website, 300), "notes": text(notes, 4000),
        ]
    }
}

extension BauakteService {
    /// Kunden anlegen (`contact.create`, offline) oder ändern (`contact.update` mit den geänderten Feldern).
    @discardableResult
    public func saveCustomer(_ edit: CustomerEdit, id: UUID? = nil) async throws(BauakteError) -> UUID {
        try edit.validate()
        var fields = edit.fields
        // Einzelfelder für ältere Server und die Suche.
        fields["phone"] = edit.phones.first.map { .string($0.number) } ?? .null
        fields["email"] = edit.emails.first.map { .string($0.address) } ?? .null
        do {
            if let id, let existing = try await store.entity(.contact, id) {
                let current = CustomerEdit(Contact(existing)).fields
                let changes = edit.fields.filter { current[$0.key] != $0.value }
                guard !changes.isEmpty else { return id }
                try await store.update(.contact, id: id, changes: changes)
                return id
            }
            let newId = UUID.v7(now: currentDate())
            let local = JSONValue.object(fields).merging(["id": JSONValue(newId), "customerNumber": .null, "version": 0])
            try await store.create(.contact, id: newId, data: local, payload: fields)
            try await remember(newId, key: "recent_customers")
            return newId
        } catch {
            throw .from(error)
        }
    }

    /// Bauakten eines Kunden auf diesem Gerät, neueste zuerst.
    public func bauakten(ofCustomer customerId: UUID) async throws -> [Bauakte] {
        try await bauakten(search: nil).filter { $0.customerId == customerId }
            .sorted { ($0.updatedAt ?? .distantPast) > ($1.updatedAt ?? .distantPast) }
    }
}
