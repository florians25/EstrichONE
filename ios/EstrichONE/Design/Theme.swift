import SwiftUI
import UIKit

/// Farb- und Maßtokens aus docs/reengineering/05-designsystem.md.
///
/// Normale Schrift steht nie in `brandBlue` (3,98 : 1 auf Weiß), sondern in `actionPrimary`.
enum Theme {
    static let navy = Color(hex: 0x0B2D5B)
    static let brandBlue = Color(hex: 0x007BFF)
    static let actionPrimary = Color.dynamic(light: 0x0062CC, dark: 0x4DA3FF)

    static let textSecondary = Color.dynamic(light: 0x4B5563, dark: 0xD1D5DB)
    static let surfaceAlt = Color.dynamic(light: 0xF3F4F6, dark: 0x111827)
    static let line = Color.dynamic(light: 0xD1D5DB, dark: 0x374151)

    static let success = Color.dynamic(light: 0x15803D, dark: 0x86EFAC)
    static let successBackground = Color.dynamic(light: 0xDCFCE7, dark: 0x052E16)
    static let error = Color.dynamic(light: 0xB91C1C, dark: 0xFCA5A5)
    static let errorBackground = Color.dynamic(light: 0xFEE2E2, dark: 0x450A0A)
    static let hint = Color.dynamic(light: 0xB45309, dark: 0xFCD34D)
    static let hintBackground = Color.dynamic(light: 0xFEF3C7, dark: 0x451A03)
    static let info = Color.dynamic(light: 0x0062CC, dark: 0x93C5FD)
    static let infoBackground = Color.dynamic(light: 0xE0EFFF, dark: 0x172554)
    static let neutral = Color.dynamic(light: 0x4B5563, dark: 0xD1D5DB)
    static let neutralBackground = Color.dynamic(light: 0xF3F4F6, dark: 0x1F2937)
    static let navyBackground = Color.dynamic(light: 0xDCE6F5, dark: 0x0B2D5B)
    static let navyText = Color.dynamic(light: 0x0B2D5B, dark: 0xDCE6F5)

    /// Primäraktionen auf der Baustelle: 56 pt hoch, gut mit Handschuhen zu treffen (05, Abschnitt 4).
    static let primaryActionHeight: CGFloat = 56
    static let spacing: CGFloat = 16
}

extension Color {
    init(hex: UInt32) {
        self.init(uiColor: UIColor(hex: hex))
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
