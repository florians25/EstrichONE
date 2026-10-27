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

/// Beteiligter einer Bauakte (Sync-Entität `bauakteParty`, nur lesend – gepflegt im Büro): Bauherr, Bauleiter, Architekt,
/// Lieferant … Ist er mit dem Kundenstamm verknüpft, gelten Name sowie fehlendes Telefon und fehlende E-Mail des Kontakts.
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
    }

    /// Mit dem verknüpften Kontakt ergänzt – wie die Liste im Büro.
    public func completed(with contact: Contact?) -> BauakteParty {
        guard let contact else { return self }
        var party = self
        if !contact.name.isEmpty { party.name = contact.name }
        party.phone = phone ?? contact.phone
        party.email = email ?? contact.email
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
        return result.sorted { ($0.role.rank, $0.name.lowercased()) < ($1.role.rank, $1.name.lowercased()) }
    }
}
