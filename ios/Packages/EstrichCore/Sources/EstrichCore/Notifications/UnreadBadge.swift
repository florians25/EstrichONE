import Foundation

/// Zähler für ungelesene Benachrichtigungen – gleich an der Glocke, im Reiter und am App-Symbol.
public enum UnreadBadge {
    /// Größte Zahl, die ausgeschrieben wird; darüber „99+“.
    public static let maximum = 99

    /// Text des Zählers; ohne ungelesene Einträge kein Zähler.
    public static func text(_ count: Int) -> String? {
        guard count > 0 else { return nil }
        return count > maximum ? "\(maximum)+" : String(count)
    }

    /// Ansage für Bedienungshilfen, z. B. „Benachrichtigungen, 3 ungelesen“.
    public static func accessibilityLabel(_ count: Int) -> String {
        switch count {
        case ..<1: "Benachrichtigungen, keine ungelesen"
        case 1: "Benachrichtigungen, 1 ungelesen"
        default: "Benachrichtigungen, \(count) ungelesen"
        }
    }

    /// Zahl am App-Symbol (das System kürzt selbst; negative Werte gibt es nicht).
    public static func appBadge(_ count: Int) -> Int { max(0, count) }
}
