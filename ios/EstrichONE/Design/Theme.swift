import SwiftUI
import UIKit

/// Design-Tokens von EstrichONE: ruhige, neutrale Flächen, **eine** Akzentfarbe (Markenblau) für alles Bedienbare,
/// gedämpfte Statusfarben nur dort, wo sie etwas bedeuten. Marke: Nachtblau #061536 / #0B2E66, Markenblau #0062CC
/// (auf dunklem Grund #4DA3FF), Eis #CFE5FF.
///
/// Kontrast: Texte erfüllen mindestens 4,5 : 1 (auch Nebentexte – Baustelle, Sonnenlicht), Bedienelemente 3 : 1.
/// Alle Farben haben eine Variante für den Dunkelmodus. Maße folgen einem 4-/8-Punkt-Raster.
enum Theme {
    // MARK: Marke

    static let navy = Color(hex: 0x061536)
    static let navyLight = Color(hex: 0x0B2E66)
    static let ice = Color(hex: 0xCFE5FF)

    // MARK: Flächen (von hinten nach vorn)

    /// Seitenhintergrund (wie gruppierte Listen des Systems).
    static let canvas = Color(uiColor: .systemGroupedBackground)
    /// Karten und Listenzeilen.
    static let surface = Color(uiColor: .secondarySystemGroupedBackground)
    /// Flächen auf Karten: Kacheln, Chips, Eingaben.
    static let fill = Color.dynamic(light: 0xF0F2F6, dark: 0x2A2B30)
    /// Gedrückt bzw. kräftiger.
    static let fillStrong = Color.dynamic(light: 0xE2E6EC, dark: 0x3A3B41)
    /// Linien und Trenner.
    static let separator = Color.dynamic(light: 0xE0E3E9, dark: 0x3A3A3E)

    // MARK: Text

    static let textPrimary = Color.primary
    /// Nebentext mit hohem Kontrast (7 : 1 hell, 9 : 1 dunkel).
    static let textSecondary = Color.dynamic(light: 0x4A5361, dark: 0xAEB5C0)
    /// Beschriftungen und Hinweise (≥ 4,5 : 1).
    static let textTertiary = Color.dynamic(light: 0x667080, dark: 0x8C939E)

    // MARK: Akzent (Markenblau)

    /// Schrift, Symbole und Steuerelemente in Akzentfarbe.
    static let accent = Color.dynamic(light: 0x0062CC, dark: 0x4DA3FF)
    /// Gefüllte Hauptschaltfläche (weiße Schrift).
    static let accentFill = Color.dynamic(light: 0x0062CC, dark: 0x1F7AE6)
    static let accentPressed = Color.dynamic(light: 0x004FA6, dark: 0x1767C4)
    /// Zart getönte Fläche für Nebenaktionen und Symbole.
    static let accentSoft = Color.dynamic(light: 0xE7F0FC, dark: 0x18283C)
    static let onAccent = Color.white

    // MARK: Status (gedämpft)

    static let success = Color.dynamic(light: 0x1C7A45, dark: 0x5FCF8C)
    static let successSoft = Color.dynamic(light: 0xE5F4EB, dark: 0x13291C)
    static let warning = Color.dynamic(light: 0x9A5800, dark: 0xF3B54A)
    static let warningSoft = Color.dynamic(light: 0xFFF3DD, dark: 0x2E2410)
    static let danger = Color.dynamic(light: 0xC0262D, dark: 0xFF7B7B)
    static let dangerSoft = Color.dynamic(light: 0xFDECEC, dark: 0x351617)

    // MARK: Übergang – alte Namen, auf die neuen Tokens abgebildet (werden mit der Umstellung entfernt)

    static let actionPrimary = accent
    static let card = surface
    static let surfaceAlt = canvas
    static let line = separator
    static let hint = warning
    static let hintBackground = warningSoft
    static let error = danger
    static let errorBackground = dangerSoft
    static let successBackground = successSoft
    static let info = accent
    static let infoBackground = accentSoft
    static let neutral = textSecondary
    static let neutralBackground = fill
    static let navyBackground = accentSoft
    static let navyText = Color.dynamic(light: 0x0B2E66, dark: 0xCFE5FF)
    static let secondaryFill = fill
    static let secondaryFillPressed = fillStrong
    static let brandFillTop = accentFill
    static let brandFillBottom = accentFill
    static let spacing: CGFloat = Space.l
    static let cornerRadius: CGFloat = Radius.m
    static let cardRadius: CGFloat = Radius.xl
    static let controlHeight: CGFloat = Size.button
    static let compactControlHeight: CGFloat = Size.minTap
}

/// Abstände im 4-/8-Punkt-Raster.
enum Space {
    static let xxs: CGFloat = 2
    static let xs: CGFloat = 4
    static let s: CGFloat = 8
    static let m: CGFloat = 12
    static let l: CGFloat = 16
    static let xl: CGFloat = 24
    static let xxl: CGFloat = 32
    /// Abstand zwischen Abschnitten einer Seite.
    static let section: CGFloat = 28
}

/// Eckradien (immer `.continuous`).
enum Radius {
    static let xs: CGFloat = 6
    static let s: CGFloat = 10
    static let m: CGFloat = 14
    static let l: CGFloat = 18
    /// Karten.
    static let xl: CGFloat = 22
}

/// Feste Größen: Tippflächen nie unter 44 pt, Hauptschaltflächen 52 pt (Handschuhe).
enum Size {
    static let minTap: CGFloat = 44
    static let button: CGFloat = 52
    static let row: CGFloat = 56
    static let iconBadge: CGFloat = 32
    /// Breite des Inhalts auf großen Bildschirmen.
    static let readableWidth: CGFloat = 1180
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

extension String {
    /// Für die Anzeige: ohne Leerzeichen und Leerzeilen am Anfang und Ende, höchstens eine Leerzeile am Stück;
    /// `nil`, wenn nichts übrig bleibt (dann wird kein Platz reserviert).
    var compactText: String? {
        var text = trimmingCharacters(in: .whitespacesAndNewlines)
        while text.contains("\n\n\n") { text = text.replacingOccurrences(of: "\n\n\n", with: "\n\n") }
        return text.isEmpty ? nil : text
    }
}
