import Foundation

/// Freigabe der Planung (`POST /planning/releases`, `DELETE /planning/releases/{day}`, docs/planung-api.md): hält den
/// Stand der Einsätze je Tag fest. Danach geänderte Einsätze tragen `change`, entfernte stehen in `release.removed`.
/// Nur mit Recht `planning.manage`; Freigaben gehen nur online (keine Vormerkung – sie sollen den aktuellen Stand festhalten).
public struct PlanReleaseInfo: Decodable, Equatable, Sendable {
    public let day: String
    public let releasedAt: Date?
    public let releasedByName: String?
    public let note: String?
    public let changedCount: Int

    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        day = try c.decode(String.self, forKey: .day)
        releasedAt = try c.decodeIfPresent(Date.self, forKey: .releasedAt)
        releasedByName = try c.decodeIfPresent(String.self, forKey: .releasedByName)
        note = try c.decodeIfPresent(String.self, forKey: .note)
        changedCount = try c.decodeIfPresent(Int.self, forKey: .changedCount) ?? 0
    }

    private enum CodingKeys: String, CodingKey {
        case day, releasedAt, releasedByName, note, changedCount
    }
}

private struct ReleaseBody: Encodable, Sendable {
    let from: String
    let to: String
    let note: String?
}

extension AuthorizedClient {
    /// Gibt die Planung für die Tage `from`…`to` (je `JJJJ-MM-TT`) frei; erneutes Freigeben setzt den Stand neu.
    public func releasePlan(from: String, to: String, note: String?) async throws(APIError) -> [PlanReleaseInfo] {
        let text = note?.trimmingCharacters(in: .whitespacesAndNewlines)
        return try await post("planning/releases", body: ReleaseBody(from: from, to: to, note: text?.isEmpty == false ? text : nil))
    }

    /// Nimmt die Freigabe eines Tages zurück (der Plan gilt wieder als Entwurf).
    public func withdrawRelease(day: String) async throws(APIError) {
        let _: NoContent = try await send(APIRequest(method: "DELETE", path: "planning/releases/\(day)"))
    }
}
