import SwiftUI

/// Deckende Statusmeldung: Farbe, Symbol und Text gemeinsam (05, Abschnitt 2.3), nie auf Glas.
struct StatusMessage: View {
    enum Kind {
        case error
        case success

        var symbol: String {
            switch self {
            case .error: "exclamationmark.triangle.fill"
            case .success: "checkmark.circle.fill"
            }
        }

        var foreground: Color {
            switch self {
            case .error: Theme.error
            case .success: Theme.success
            }
        }

        var background: Color {
            switch self {
            case .error: Theme.errorBackground
            case .success: Theme.successBackground
            }
        }
    }

    let kind: Kind
    let text: String

    var body: some View {
        Label {
            Text(text)
                .fixedSize(horizontal: false, vertical: true)
        } icon: {
            Image(systemName: kind.symbol)
                .accessibilityHidden(true)
        }
        .font(.callout.weight(.medium))
        .foregroundStyle(kind.foreground)
        .padding(12)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(kind.background, in: RoundedRectangle(cornerRadius: 10, style: .continuous))
        .accessibilityElement(children: .combine)
    }
}
