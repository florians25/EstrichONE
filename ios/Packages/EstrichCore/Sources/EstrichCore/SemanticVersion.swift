import Foundation

/// Versionsnummer `Haupt.Neben.Patch` für den Kompatibilitätsabgleich zwischen App und Server.
public struct SemanticVersion: Comparable, Hashable, Sendable, CustomStringConvertible {
    public let major: Int
    public let minor: Int
    public let patch: Int

    public init(major: Int, minor: Int, patch: Int) {
        self.major = major
        self.minor = minor
        self.patch = patch
    }

    /// Akzeptiert `1`, `1.2`, `1.2.3` sowie Zusätze wie `1.2.3-beta+45` (werden ignoriert).
    public init?(_ text: String) {
        let core = text.prefix { $0 != "-" && $0 != "+" }
        let parts = core.split(separator: ".", omittingEmptySubsequences: false)
        guard (1...3).contains(parts.count),
              parts.allSatisfy({ !$0.isEmpty && $0.allSatisfy(\.isASCII) && $0.allSatisfy(\.isNumber) })
        else { return nil }
        let numbers = parts.compactMap { Int($0) } + Array(repeating: 0, count: 3 - parts.count)
        guard numbers.count == 3 else { return nil }
        self.init(major: numbers[0], minor: numbers[1], patch: numbers[2])
    }

    public var description: String { "\(major).\(minor).\(patch)" }

    public static func < (lhs: SemanticVersion, rhs: SemanticVersion) -> Bool {
        (lhs.major, lhs.minor, lhs.patch) < (rhs.major, rhs.minor, rhs.patch)
    }
}
