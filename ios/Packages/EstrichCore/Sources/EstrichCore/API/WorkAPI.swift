import Foundation

extension AuthorizedClient {
    /// Verlauf einer Bauakte, neueste zuerst. Nur online verfügbar.
    public func history(of bauakteId: UUID) async throws(APIError) -> [BauakteHistoryEntry] {
        try await get("bauakten/\(bauakteId.key)/history")
    }

    public func directoryUsers() async throws(APIError) -> [DirectoryUser] {
        try await get("directory/users")
    }
}
