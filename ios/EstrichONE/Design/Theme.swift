import SwiftUI
import UIKit

/// Farb- und Maßtokens (docs/reengineering/05-designsystem.md), Markenfarben Navy #0B2D5B / #174A8B, Akzent #3CCB7F.
///
/// Normale Schrift steht nie in `brandBlue` (3,98 : 1 auf Weiß), sondern in `actionPrimary`.
/// Alle Farben haben eine Variante für den Dunkelmodus; Maße folgen den iOS-Standardgrößen (Tippfläche ≥ 44 pt).
enum Theme {
    static let navy = Color(hex: 0x0B2D5B)
    static let navyLight = Color(hex: 0x174A8B)
    static let brandBlue = Color(hex: 0x007BFF)
    static let actionPrimary = Color.dynamic(light: 0x174A8B, dark: 0x6FA8F5)

    /// Gefüllte Hauptschaltfläche: Verlauf von oben nach unten.
    // Ruhiges Farbsystem: eine Akzentfarbe (Markenblau) für Aktionen, sonst neutrale Flächen.
    static let brandFillTop = Color.dynamic(light: 0x174A8B, dark: 0x2B62AD)
    static let brandFillBottom = Color.dynamic(light: 0x174A8B, dark: 0x2B62AD)
    /// Akzent für „Aufnehmen“ (Foto, Scan) – Text darauf immer dunkel (`onAccent`, Kontrast > 7 : 1).
    static let accent = Color.dynamic(light: 0x174A8B, dark: 0x3B7DD8)
    static let accentPressed = Color.dynamic(light: 0x0B2D5B, dark: 0x2B62AD)
    static let onAccent = Color.white
    /// Nebenaktion: neutrale Fläche.
    static let secondaryFill = Color.dynamic(light: 0xEEF0F3, dark: 0x2A2C31)
    static let secondaryFillPressed = Color.dynamic(light: 0xE0E3E8, dark: 0x36393F)

    static let textSecondary = Color.dynamic(light: 0x4B5563, dark: 0xC3CAD4)
    static let surfaceAlt = Color(uiColor: .systemGroupedBackground)
    static let card = Color(uiColor: .secondarySystemGroupedBackground)
    static let line = Color.dynamic(light: 0xD1D5DB, dark: 0x374151)

    static let success = Color.dynamic(light: 0x15803D, dark: 0x86EFAC)
    static let successBackground = Color.dynamic(light: 0xDCFCE7, dark: 0x052E16)
    static let error = Color.dynamic(light: 0xB91C1C, dark: 0xFCA5A5)
    static let errorBackground = Color.dynamic(light: 0xFEE2E2, dark: 0x450A0A)
    static let hint = Color.dynamic(light: 0xB45309, dark: 0xFCD34D)
    static let hintBackground = Color.dynamic(light: 0xFEF3C7, dark: 0x451A03)
    static let info = Color.dynamic(light: 0x174A8B, dark: 0x93C5FD)
    static let infoBackground = Color.dynamic(light: 0xE0EFFF, dark: 0x172554)
    static let neutral = Color.dynamic(light: 0x4B5563, dark: 0xD1D5DB)
    static let neutralBackground = Color.dynamic(light: 0xF3F4F6, dark: 0x1F2937)
    static let navyBackground = Color.dynamic(light: 0xDCE6F5, dark: 0x1B2E4D)
    static let navyText = Color.dynamic(light: 0x0B2D5B, dark: 0xDCE6F5)

    /// Standardhöhe von Schaltflächen (iOS „large“).
    static let controlHeight: CGFloat = 48
    /// Kompakte Schaltflächen in Zeilen und Leisten – nie unter der Mindest-Tippfläche.
    static let compactControlHeight: CGFloat = 44
    static let cornerRadius: CGFloat = 12
    static let cardRadius: CGFloat = 14
    static let spacing: CGFloat = 16
}

extension Color {
    init(hex: UInt32) {
        self.init(uiColor: UIColor(hex: hex))
    }

    /// Farbe aus `#RRGGBB` (z. B. Kolonnenfarbe aus der Planung); ungültige Angaben ergeben `nil`.
    init?(hexString: String?) {
        guard var text = hexString?.trimmingCharacters(in: .whitespaces), !text.isEmpty else { return nil }
        if text.hasPrefix("#") { text.removeFirst() }
        guard text.count == 6, let value = UInt32(text, radix: 16) else { return nil }
        self.init(hex: value)
    }

    static func dynamic(light: UInt32, dark: UInt32) -> Color {
        Color(uiColor: UIColor { traits in
            UIColor(hex: traits.userInterfaceStyle == .dark ? dark : light)
        })
    }
}

extension UIColor {
    convenience init(hex: UInt32) {
        self.init(
            red: CGFloat((hex >> 16) & 0xFF) / 255,
            green: CGFloat((hex >> 8) & 0xFF) / 255,
            blue: CGFloat(hex & 0xFF) / 255,
            alpha: 1)
    }
}

/// Karte auf gruppiertem Hintergrund (Startseite).
struct CardBackground: ViewModifier {
    func body(content: Content) -> some View {
        content
            .background(Theme.card, in: RoundedRectangle(cornerRadius: Theme.cardRadius, style: .continuous))
    }
}

extension View {
    func card() -> some View { modifier(CardBackground()) }
}

extension String {
    /// Für die Anzeige: ohne Leerzeichen und Leerzeilen am Anfang und Ende, höchstens eine Leerzeile am Stück;
    /// `nil`, wenn nichts übrig bleibt (dann wird kein Platz reserviert).
    var compactText: String? {
        var text = trimmingCharacters(in: .whitespacesAndNewlines)
        while text.contains("\n\n\n") { text = text.replacingOccurrences(of: "\n\n\n", with: "\n\n") }
        return text.isEmpty ? nil : text
    }
}
