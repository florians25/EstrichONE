import EstrichCore
import SwiftUI

/// Zusätzliches zur Baustelle eines Einsatzes (lokal bzw. zuletzt geladen): Leistung laut Basisdaten, Zugang und
/// angeheftete Notizen, letzte Nachricht im Chat der Baustelle.
struct TodaySiteExtras: Equatable {
    /// „120 m², Zementestrich CT, 65 mm“
    var facts: String?
    /// Zugang und Besonderheiten (Basisdaten).
    var access: String?
    /// Angeheftete Notizen (Schlüssel, Absprachen).
    var pinned: [String] = []
    /// Unterhaltung der Baustelle im Chat (mit letzter Nachricht).
    var chat: ChatConversation?

    var isEmpty: Bool { facts == nil && access == nil && pinned.isEmpty && chat == nil }
}
