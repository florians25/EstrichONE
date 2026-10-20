import Foundation

/// Regeln für die Geräte-PIN auf Poolgeräten (docs/reengineering/11, Abschnitt 4.2).
public enum PINPolicy {
    public static let length = 6
    public static let maxAttempts = 5

    public enum Violation: Error, Equatable, Sendable {
        case wrongLength
        case notDigits
        case trivial

        public var message: String {
            switch self {
            case .wrongLength, .notDigits: "Die PIN besteht aus genau 6 Ziffern."
            case .trivial: "Diese PIN ist zu leicht zu erraten. Bitte keine Folgen wie 123456 oder 111111 verwenden."
            }
        }
    }

    public static func validate(_ pin: String) throws(Violation) {
        guard pin.count == length else { throw .wrongLength }
        let digits = pin.compactMap { $0.isASCII ? $0.wholeNumberValue : nil }
        guard digits.count == length else { throw .notDigits }
        if isTrivial(digits) { throw .trivial }
    }

    /// Gleiche Ziffern, auf- oder absteigende Folgen, Wiederholungen kurzer Muster (121212, 123123).
    static func isTrivial(_ digits: [Int]) -> Bool {
        let steps = zip(digits.dropFirst(), digits).map { ($0 - $1 + 10) % 10 }
        if Set(steps).count == 1 { return true }
        for period in [2, 3] where digits.count % period == 0 {
            let pattern = Array(digits.prefix(period))
            if digits.indices.allSatisfy({ digits[$0] == pattern[$0 % period] }) { return true }
        }
        return false
    }
}
