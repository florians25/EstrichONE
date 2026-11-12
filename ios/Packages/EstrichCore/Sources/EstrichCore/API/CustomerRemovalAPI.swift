import Foundation

/// Beispiel eines verknüpften Datensatzes („B-2026-0004 · EFH Huber“) mit Pfad in der Weboberfläche.
public struct CustomerReferenceItem: Decodable, Sendable, Equatable, Identifiable {
    public let id: UUID
    public let text: String
    public let link: String?
}

/// Verknüpfung eines Kunden mit Daten anderer Bereiche, z. B. „3 Bauakten“.
public struct CustomerReference: Decodable, Sendable, Equatable {
    public let kind: String
    public let label: String
    public let count: Int
    public let link: String?
    public let items: [CustomerReferenceItem]
}

/// Antwort von `GET contacts/{id}/references`: darf der Kunde gelöscht werden, und was hängt an ihm?
public struct CustomerReferences: Decodable, Sendable, Equatable {
    public let id: UUID
    public let name: String
    public let customerNumber: String?
    public let deletable: Bool
    /// Zum Bestätigen einzutippen: Kundennummer, ohne Nummer der Name.
    public let confirmText: String
    public let archived: Bool
    public let anonymized: Bool
    public let references: [CustomerReference]

    /// „1 Bauakte und 2 Anrufe und Anfragen“.
    public var summary: String { CustomerRemoval.describe(references) }
}

/// Kunden löschen bzw. archivieren: Logik ohne Oberfläche.
public enum CustomerRemoval {
    public static func describe(_ references: [CustomerReference]) -> String {
        let parts = references.filter { $0.count > 0 }.map { "\($0.count) \($0.label)" }
        guard parts.count > 1, let last = parts.last else { return parts.joined() }
        return parts.dropLast().joined(separator: ", ") + " und " + last
    }

    /// Eingetippte Bestätigung; Groß-/Kleinschreibung und Leerzeichen am Rand egal.
    public static func confirms(_ input: String, _ expected: String) -> Bool {
        let value = input.trimmingCharacters(in: .whitespaces)
        return !value.isEmpty && value.lowercased() == expected.trimmingCharacters(in: .whitespaces).lowercased()
    }
}

extension AuthorizedClient {
    /// Bezüge des Kunden (Bauakten, Projektbeteiligungen, Anrufe …); nur online.
    public func customerReferences(_ id: UUID) async throws(APIError) -> CustomerReferences {
        try await get("contacts/\(id.key)/references")
    }

    /// Kunden ohne Bezüge endgültig löschen (`confirm` = Kundennummer bzw. Name). Der Abgleich entfernt ihn danach vom Gerät.
    public func deleteCustomer(_ id: UUID, confirm: String) async throws(APIError) {
        let _: NoContent = try await send(APIRequest(method: "DELETE", path: "contacts/\(id.key)", query: [URLQueryItem(name: "confirm", value: confirm)]))
    }

    /// Archivieren: der Kunde bleibt erhalten, erscheint aber nicht mehr in Auswahllisten.
    public func archiveCustomer(_ id: UUID) async throws(APIError) {
        let _: JSONValue = try await post("contacts/\(id.key)/archive")
    }

    public func reactivateCustomer(_ id: UUID) async throws(APIError) {
        let _: JSONValue = try await post("contacts/\(id.key)/reactivate")
    }
}
