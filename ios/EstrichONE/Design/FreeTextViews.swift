import EstrichCore
import SwiftUI

/// Freitext (Beschreibung, Zugang und Besonderheiten, Notizen, Hinweise der Disposition) als gut lesbarer Textblock:
/// Überschrift mit Symbol, Zeilenumbrüche bleiben erhalten, Links, Telefonnummern und E-Mail-Adressen sind antippbar,
/// lange Texte zunächst gekürzt mit „Mehr anzeigen“. Text lässt sich markieren und kopieren.
struct FreeTextBlock: View {
    let title: String
    let text: String
    var symbol: String = "text.alignleft"
    /// Zeilen, die gekürzt sichtbar sind.
    var collapsedLines = 6
    var tint: Color = Theme.accent

    @State private var expanded = false

    var body: some View {
        let shown = FreeText.clean(text) ?? ""
        let long = FreeText.isLong(shown, lines: collapsedLines)
        VStack(alignment: .leading, spacing: Space.s) {
            Label {
                Text(title)
            } icon: {
                Image(systemName: symbol).foregroundStyle(tint)
            }
            .font(Typo.subheadStrong)
            .foregroundStyle(Theme.textSecondary)
            .accessibilityAddTraits(.isHeader)
            Text(FreeTextLinks.attributed(shown))
                .font(Typo.body)
                .foregroundStyle(Theme.textPrimary)
                .lineSpacing(2)
                .lineLimit(long && !expanded ? collapsedLines : nil)
                .fixedSize(horizontal: false, vertical: true)
                .frame(maxWidth: .infinity, alignment: .leading)
                .textSelection(.enabled)
                .tint(Theme.accent)
            if long {
                Button(expanded ? "Weniger anzeigen" : "Mehr anzeigen") {
                    withAnimation(.snappy) { expanded.toggle() }
                }
                .font(Typo.subheadStrong)
                .buttonStyle(.borderless)
                .foregroundStyle(Theme.accent)
                .frame(minHeight: Size.minTap, alignment: .leading)
                .accessibilityIdentifier("freeTextMore")
            }
        }
        .padding(.vertical, Space.xs)
        .accessibilityElement(children: .contain)
    }
}

/// Mehrzeiliger Editor für Freitexte: Überschrift über dem Feld, ausreichend hoch (mindestens vier Zeilen), wächst mit
/// dem Text, Beispiel als Platzhalter. Für Formulare (`Form`/`List`).
struct FreeTextEditor: View {
    let title: String
    var prompt: String = ""
    @Binding var text: String
    var minLines = 4
    var maxLines = 14
    var identifier: String?

    var body: some View {
        VStack(alignment: .leading, spacing: Space.xs) {
            Text(title)
                .font(Typo.metaStrong)
                .foregroundStyle(Theme.textSecondary)
            TextField(prompt.isEmpty ? title : prompt, text: $text, axis: .vertical)
                .lineLimit(minLines...maxLines)
                .font(Typo.body)
                .accessibilityLabel(title)
                .accessibilityIdentifier(identifier ?? "freeTextEditor")
        }
        .padding(.vertical, Space.xs)
    }
}

/// Erkennt Links, Telefonnummern und E-Mail-Adressen im Freitext und macht sie antippbar.
enum FreeTextLinks {
    static func attributed(_ text: String) -> AttributedString {
        var result = AttributedString(text)
        let types = NSTextCheckingResult.CheckingType.link.rawValue | NSTextCheckingResult.CheckingType.phoneNumber.rawValue
        guard !text.isEmpty, let detector = try? NSDataDetector(types: types) else { return result }
        let range = NSRange(text.startIndex..., in: text)
        for match in detector.matches(in: text, range: range) {
            guard let swiftRange = Range(match.range, in: text),
                  let lower = AttributedString.Index(swiftRange.lowerBound, within: result),
                  let upper = AttributedString.Index(swiftRange.upperBound, within: result) else { continue }
            let url: URL? = switch match.resultType {
            case .phoneNumber: match.phoneNumber.flatMap { ContactLink.phone($0) }
            default: match.url
            }
            guard let url else { continue }
            result[lower..<upper].link = url
        }
        return result
    }
}
