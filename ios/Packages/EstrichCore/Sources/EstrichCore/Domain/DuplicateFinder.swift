import Foundation

/// Hinweis auf eine mögliche Doppelanlage (09, Abschnitt 3). Ein Treffer blockiert nie, er bietet nur
/// „Öffnen statt neu anlegen“ an.
public enum DuplicateFinder {
    public struct Candidate: Sendable {
        public let title: String
        public let street: String?
        public let city: String?
        public let latitude: Double?
        public let longitude: Double?

        public init(title: String, street: String?, city: String?, latitude: Double?, longitude: Double?) {
            self.title = title
            self.street = street
            self.city = city
            self.latitude = latitude
            self.longitude = longitude
        }
    }

    /// Gleiche Adresse (normalisiert) oder sehr ähnliche Bezeichnung in weniger als 100 m Entfernung.
    public static func matches(_ candidate: Candidate, in existing: [Bauakte]) -> [Bauakte] {
        let street = normalizeStreet(candidate.street)
        let city = normalize(candidate.city)
        let words = significantWords(candidate.title)
        return existing.filter { bauakte in
            if let street, !street.isEmpty, street == normalizeStreet(bauakte.street),
               city == nil || city == normalize(bauakte.city) {
                return true
            }
            guard words.count > 0, similarity(words, significantWords(bauakte.title)) >= 0.8 else { return false }
            guard let distance = distance(candidate, bauakte) else { return false }
            return distance < 100
        }
    }

    static func normalize(_ text: String?) -> String? {
        guard let text else { return nil }
        let folded = text.folding(options: [.caseInsensitive, .diacriticInsensitive], locale: Locale(identifier: "de_DE"))
            .replacingOccurrences(of: "ß", with: "ss")
        let cleaned = folded.unicodeScalars.map { CharacterSet.alphanumerics.contains($0) ? Character($0) : " " }
        let result = String(cleaned).split(separator: " ").joined(separator: " ")
        return result.isEmpty ? nil : result
    }

    /// „Musterstr. 12“ = „Musterstraße 12“ = „Muster-Strasse 12“.
    static func normalizeStreet(_ street: String?) -> String? {
        guard var text = normalize(street) else { return nil }
        text = text.replacingOccurrences(of: #"\bstr\b"#, with: "strasse", options: .regularExpression)
        text = text.replacingOccurrences(of: "strasse", with: " strasse")
        return text.split(separator: " ").joined()
    }

    static func significantWords(_ text: String) -> Set<String> {
        Set((normalize(text) ?? "").split(separator: " ").map(String.init).filter { $0.count > 1 })
    }

    static func similarity(_ lhs: Set<String>, _ rhs: Set<String>) -> Double {
        let union = lhs.union(rhs).count
        return union == 0 ? 0 : Double(lhs.intersection(rhs).count) / Double(union)
    }

    /// Entfernung in Metern (Haversine).
    static func distance(_ candidate: Candidate, _ bauakte: Bauakte) -> Double? {
        guard let lat1 = candidate.latitude, let lon1 = candidate.longitude,
              let lat2 = bauakte.latitude, let lon2 = bauakte.longitude else { return nil }
        let radius = 6_371_000.0
        let dLat = (lat2 - lat1) * .pi / 180
        let dLon = (lon2 - lon1) * .pi / 180
        let a = sin(dLat / 2) * sin(dLat / 2) + cos(lat1 * .pi / 180) * cos(lat2 * .pi / 180) * sin(dLon / 2) * sin(dLon / 2)
        return radius * 2 * atan2(sqrt(a), sqrt(1 - a))
    }
}
