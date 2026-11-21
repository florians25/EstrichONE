import Foundation

/// Bauakte aus einer Serverantwort (`GET bauakten`, `GET bauakten/{id}`) mit der Angabe, ob man ihr zugeteilt ist.
/// Alle sehen alle Bauakten; auf dem Gerät liegen aber nur die zugeteilten (Datenmenge, Offline-Speicher) – die übrigen
/// zeigt die App online als Kopf mit „Mich als Beteiligten eintragen“.
public struct RemoteBauakte: Identifiable, Equatable, Sendable {
    public let bauakte: Bauakte
    /// Zuständig, als Beteiligter eingetragen oder eingeplant (Einsatz, Kolonne).
    public let isAssigned: Bool
    /// Darf ändern (zugeteilt oder „Alle Bauakten bearbeiten“). Ältere Server ohne Angabe: `true`.
    public let canEdit: Bool

    public var id: UUID { bauakte.id }

    public init?(remote data: JSONValue) {
        guard let id = data["id"]?.string.flatMap(UUID.init(uuidString:)) else { return nil }
        bauakte = Bauakte(LocalEntity(type: .bauakte, id: id, data: data, syncState: .synced))
        isAssigned = data["isAssigned"]?.bool ?? false
        canEdit = data["canEdit"]?.bool ?? true
    }
}

/// Hinweis-Streifen „Nur ansehen“ – gleicher Wortlaut auf allen Plattformen.
public enum BauakteViewOnly {
    public static let banner = "Nur ansehen: Tragen Sie sich als Beteiligter ein, um zu bearbeiten."
    public static let join = "Mich als Beteiligten eintragen"
    public static let leave = "Als Beteiligter austragen"

    /// Verständliche Meldung zu einer abgelehnten Operation bzw. Antwort mit Code BAUAKTE_NOT_ASSIGNED.
    public static func message(forCode code: String?) -> String? {
        code == BauakteNotAssignedError.code
            ? "Nicht übertragen: Sie sind dieser Bauakte nicht (mehr) zugeteilt. Tragen Sie sich als Beteiligter ein und senden Sie die Änderung erneut."
            : nil
    }
}

extension AuthorizedClient {
    /// Alle Bauakten online (auch nicht zugeteilte), neueste Änderung zuerst. `assigned`: nur zugeteilte.
    public func onlineBauakten(search: String?, includeOld: Bool = false, limit: Int = 50) async throws(APIError) -> [RemoteBauakte] {
        var query = [URLQueryItem(name: "limit", value: String(limit)), URLQueryItem(name: "status", value: includeOld ? "all" : "active")]
        if let search = search?.trimmingCharacters(in: .whitespaces), !search.isEmpty {
            query.append(URLQueryItem(name: "search", value: search))
        }
        let page: JSONValue = try await get("bauakten", query: query)
        return (page["items"]?.array ?? []).compactMap(RemoteBauakte.init(remote:))
    }

    /// Kopf einer Bauakte online (auch wenn sie nicht auf dem Gerät liegt).
    public func onlineBauakte(_ id: UUID) async throws(APIError) -> RemoteBauakte? {
        let data: JSONValue = try await get("bauakten/\(id.key)")
        return RemoteBauakte(remote: data)
    }

    /// „Als Beteiligter austragen“ (nicht als zuständige Person). Danach mit `SyncEngine.synchronize(includeScope: true)` abgleichen.
    public func leaveBauakte(_ bauakteId: UUID) async throws(APIError) {
        let _: JSONValue = try await send(APIRequest(method: "DELETE", path: "bauakten/\(bauakteId.key)/members/me"))
    }
}
