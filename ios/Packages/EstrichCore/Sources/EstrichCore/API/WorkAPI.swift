import Foundation

extension AuthorizedClient {
    /// Verlauf einer Bauakte, neueste zuerst. Nur online verfügbar.
    public func history(of bauakteId: UUID) async throws(APIError) -> [BauakteHistoryEntry] {
        try await get("bauakten/\(bauakteId.key)/history")
    }

    public func directoryUsers() async throws(APIError) -> [DirectoryUser] {
        try await get("directory/users")
    }

    /// Kundensuche auf dem Server (Name, Kundennummer, Ort) – online ergänzend zur lokalen Suche, z. B. für die
    /// Dublettenprüfung bei der Kundenanlage. Höchstens `limit` Treffer.
    public func searchContacts(_ search: String, limit: Int = 10) async throws(APIError) -> [Contact] {
        let rows: [JSONValue] = try await get("contacts", query: [URLQueryItem(name: "search", value: search),
                                                                  URLQueryItem(name: "limit", value: String(limit))])
        return rows.compactMap(Contact.init(remote:))
    }

    /// „Mir zuweisen“: sich selbst als Mitarbeiter der Bauakte eintragen. Danach mit `SyncEngine.load(bauakte:)` laden.
    public func assignToMe(_ bauakteId: UUID) async throws(APIError) {
        let _: JSONValue = try await post("bauakten/\(bauakteId.key)/members/me")
    }

    /// Mögliche Dubletten vor dem Anlegen eines Kunden (Server-Prüfung über Name, Telefon, E-Mail und Adresse).
    public func contactDuplicates(of customer: NewCustomer) async throws(APIError) -> [Contact] {
        var query: [URLQueryItem] = [URLQueryItem(name: "name", value: customer.name.trimmingCharacters(in: .whitespaces))]
        for (name, value) in [("phone", customer.phone), ("email", customer.email), ("street", customer.street),
                              ("postalCode", customer.postalCode), ("city", customer.city)] {
            if let value, !value.trimmingCharacters(in: .whitespaces).isEmpty { query.append(URLQueryItem(name: name, value: value)) }
        }
        let rows: [JSONValue] = try await get("contacts/duplicates", query: query)
        return rows.compactMap { row in row["contact"].flatMap(Contact.init(remote:)) }
    }

    /// Alle Einsätze eines Zeitraums („Wer fährt wohin“), Tage im Format `yyyy-MM-dd`.
    public func planning(from: String, to: String) async throws(APIError) -> PlanningRange {
        try await get("planning/range", query: [URLQueryItem(name: "from", value: from), URLQueryItem(name: "to", value: to)])
    }
}

extension AuthorizedClient {
    /// Rechte des angemeldeten Benutzers (steuert nur die Oberfläche; der Server prüft immer selbst).
    public func myPermissions() async throws(APIError) -> [String] {
        try await get("access/me/permissions")
    }
}

/// Rechte mit Zwischenspeicher für den Offline-Betrieb.
public struct MyPermissions: Sendable {
    public let store: LocalStore

    public init(store: LocalStore) { self.store = store }

    public func load(using client: AuthorizedClient?) async -> Set<String> {
        if let client, let fresh = try? await client.myPermissions() {
            try? await store.setMeta("my_permissions", fresh.joined(separator: ","))
            return Set(fresh)
        }
        let cached = (try? await store.meta("my_permissions")) ?? nil
        return Set((cached ?? "").split(separator: ",").map(String.init))
    }
}
