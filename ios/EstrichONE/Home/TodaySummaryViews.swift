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

/// „Ihr Tag“ oben auf „Heute“: Einsätze, Beginn der ersten Baustelle und Fläche als drei Kennzahlen, darunter Kolonne
/// mit Besetzung, Fahrzeuge und Geräte und wer heute fehlt. Ruhig gesetzt, nur was bekannt ist.
struct TodaySummaryCard: View {
    let overview: TodayOverview
    /// Kolonne, Fahrzeuge und Besetzung zeigen (bei nur einem Einsatz stehen sie schon auf dessen Karte).
    var showsCrew = true

    @Environment(\.dynamicTypeSize) private var typeSize

    var body: some View {
        VStack(alignment: .leading, spacing: Space.m) {
            figures
            if showsCrew, overview.crewText != nil || !overview.vehicles.isEmpty {
                InsetDivider()
                if let crew = overview.crewText {
                    row(symbol: "person.3", tint: Color(hexString: overview.crewColor) ?? Theme.textSecondary, title: crew,
                        detail: overview.people.isEmpty ? nil : overview.people.joined(separator: ", "))
                        .accessibilityIdentifier("todaySummaryCrew")
                }
                if !overview.vehicles.isEmpty {
                    row(symbol: "truck.box", tint: Theme.textSecondary, title: overview.vehicles.joined(separator: ", "), detail: nil)
                }
            }
            if !overview.absent.isEmpty {
                row(symbol: "person.crop.circle.badge.xmark", tint: Theme.warning,
                    title: overview.absent.count == 1 ? "Heute abwesend" : "Heute abwesend (\(overview.absent.count))",
                    detail: overview.absent.joined(separator: "; "))
                    .accessibilityIdentifier("todaySummaryAbsent")
            }
        }
        .padding(Space.l)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card()
        .accessibilityElement(children: .contain)
        .accessibilityIdentifier("todaySummary")
    }

    private var figures: some View {
        let third = overview.areaText.map { Figure(value: $0, label: "Fläche") }
            ?? Figure(value: "\(overview.siteCount)", label: overview.siteCount == 1 ? "Baustelle" : "Baustellen")
        let items = [
            Figure(value: "\(overview.jobCount)", label: overview.jobCount == 1 ? "Einsatz" : "Einsätze"),
            Figure(value: overview.firstStart ?? "offen", label: "Beginn"),
            third,
        ]
        let layout = typeSize.isAccessibilitySize ? AnyLayout(VStackLayout(alignment: .leading, spacing: Space.s))
                                                  : AnyLayout(HStackLayout(alignment: .top, spacing: Space.s))
        return layout {
            ForEach(items) { item in
                VStack(alignment: .leading, spacing: 1) {
                    Text(item.value)
                        .font(.title3.weight(.semibold).monospacedDigit())
                        .foregroundStyle(Theme.textPrimary)
                        .lineLimit(1)
                        .minimumScaleFactor(0.75)
                    Text(item.label)
                        .font(Typo.label)
                        .foregroundStyle(Theme.textSecondary)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .accessibilityElement(children: .combine)
            }
        }
    }

    private struct Figure: Identifiable {
        let value: String
        let label: String
        var id: String { label }
    }

    private func row(symbol: String, tint: Color, title: String, detail: String?) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: Space.m) {
            Image(systemName: symbol)
                .font(.subheadline.weight(SymbolWeight.content))
                .foregroundStyle(tint)
                .frame(width: 22)
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 2) {
                Text(title)
                    .font(Typo.subheadStrong)
                    .foregroundStyle(Theme.textPrimary)
                    .fixedSize(horizontal: false, vertical: true)
                if let detail {
                    Text(detail)
                        .font(Typo.subhead)
                        .foregroundStyle(Theme.textSecondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
        }
        .accessibilityElement(children: .combine)
    }
}
