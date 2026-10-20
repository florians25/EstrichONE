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
}
