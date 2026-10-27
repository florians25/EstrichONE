import EstrichCore
import SwiftUI

extension BauakteStatus {
    /// Gedämpfte Statusfarbe: grau (Anfrage, geplant), Markenblau (in Ausführung), Bernstein (Trocknung),
    /// Grün (abgeschlossen), Rot (storniert). Immer zusammen mit dem Text.
    var tint: Color {
        switch self {
        case .inquiry: Theme.textTertiary
        case .planned: Theme.textSecondary
        case .inProgress: Theme.accent
        case .drying: Theme.warning
        case .completed: Theme.success
        case .cancelled: Theme.danger
        }
    }

    var symbol: String {
        switch self {
        case .inquiry: "questionmark.circle"
        case .planned: "calendar"
        case .inProgress: "hammer"
        case .drying: "hourglass"
        case .completed: "checkmark.circle"
        case .cancelled: "xmark.circle"
        }
    }

    /// Kurz für Listen und Kapseln.
    var shortTitle: String {
        self == .drying ? "Trocknung" : title
    }

    /// Reihenfolge in Listen: was gerade läuft, zuerst.
    var sortRank: Int {
        switch self {
        case .inProgress: 0
        case .drying: 1
        case .planned: 2
        case .inquiry: 3
        case .completed: 4
        case .cancelled: 5
        }
    }
}

/// Status als Punkt und Text (ohne Fläche) – für Zeilen.
struct StatusLabel: View {
    let status: BauakteStatus
    /// Ausgewählte Zeile (iPad-Seitenleiste): Text und Punkt weiß auf Akzentfarbe.
    @Environment(\.backgroundProminence) private var prominence

    var body: some View {
        let selected = prominence == .increased
        HStack(spacing: 6) {
            StatusDot(color: selected ? .white : status.tint)
            Text(status.shortTitle)
                .strikethrough(status == .cancelled)
        }
        .font(Typo.metaStrong)
        .foregroundStyle(selected ? Color.white.opacity(0.9) : Theme.textSecondary)
        .lineLimit(1)
        .accessibilityElement(children: .combine)
        .accessibilityLabel("Status: \(status.title)")
    }
}

/// Übertragungszustand eines Datensatzes (15, Abschnitt 4): wartet · übertragen · Problem.
struct SyncStateBadge: View {
    let state: SyncState
    var showsSynced = false

    var body: some View {
        switch state {
        case .pending:
            Image(systemName: "arrow.up.circle")
                .font(.footnote.weight(.semibold))
                .foregroundStyle(Theme.textTertiary)
                .accessibilityLabel("Wird übertragen")
        case .problem:
            Label("Problem bei der Übertragung", systemImage: "exclamationmark.triangle.fill")
                .labelStyle(TightLabelStyle())
                .font(Typo.metaStrong)
                .foregroundStyle(Theme.danger)
        case .synced:
            if showsSynced {
                Image(systemName: "checkmark.icloud")
                    .font(.footnote.weight(.semibold))
                    .foregroundStyle(Theme.success)
                    .accessibilityLabel("Übertragen")
            }
        }
    }
}

/// Zeile einer Bauakte: Bezeichnung, darunter Nummer und Ort, dann Status und Kunde; optional der heutige Einsatz.
struct BauakteRow: View {
    let bauakte: Bauakte
    var customerName: String?
    /// „Heute 07:00 · Kolonne Huber“
    var today: String?
    @Environment(\.backgroundProminence) private var prominence

    /// Ausgewählte Zeile (iPad-Seitenleiste): weiß auf Akzentfarbe statt Grau auf Blau.
    private var selected: Bool { prominence == .increased }
    private var secondary: Color { selected ? Color.white.opacity(0.85) : Theme.textSecondary }

    var body: some View {
        VStack(alignment: .leading, spacing: 5) {
            HStack(alignment: .firstTextBaseline, spacing: Space.s) {
                Text(bauakte.title)
                    .font(Typo.headline)
                    .foregroundStyle(selected ? Color.white : Theme.textPrimary)
                    .lineLimit(2)
                    .multilineTextAlignment(.leading)
                Spacer(minLength: Space.xs)
                if bauakte.syncState != .synced {
                    SyncStateBadge(state: bauakte.syncState)
                }
            }
            Text([bauakte.number ?? "Neu", bauakte.addressLine ?? "ohne Adresse"].joined(separator: " · "))
                .font(Typo.subhead)
                .monospacedDigit()
                .foregroundStyle(secondary)
                .lineLimit(1)
            HStack(spacing: Space.s) {
                StatusLabel(status: bauakte.status)
                if let customerName {
                    Text(customerName)
                        .font(Typo.meta)
                        .foregroundStyle(secondary)
                        .lineLimit(1)
                }
            }
            if let today {
                Label(today, systemImage: "calendar.badge.clock")
                    .labelStyle(TightLabelStyle())
                    .font(Typo.metaStrong)
                    .foregroundStyle(selected ? Color.white : Theme.accent)
                    .lineLimit(1)
            }
        }
        .padding(.vertical, Space.xs)
        .frame(maxWidth: .infinity, alignment: .leading)
        .accessibilityElement(children: .combine)
    }
}

/// Abschnittsüberschrift in Listen mit Anzahl und optionaler Aktion rechts (z. B. „+ Eintrag“).
struct SectionHeader<Trailing: View>: View {
    let title: String
    let count: Int?
    @ViewBuilder let trailing: () -> Trailing

    init(_ title: String, count: Int? = nil, @ViewBuilder trailing: @escaping () -> Trailing) {
        self.title = title
        self.count = count
        self.trailing = trailing
    }

    var body: some View {
        HStack(alignment: .firstTextBaseline) {
            Text(count.map { "\(title) (\($0))" } ?? title)
            Spacer()
            trailing()
                .textCase(nil)
        }
    }
}

extension SectionHeader where Trailing == EmptyView {
    init(_ title: String, count: Int? = nil) {
        self.init(title, count: count) { EmptyView() }
    }
}

/// Fortschritt eines Assistenten (ältere Aufrufer); Darstellung wie `StepProgress`.
struct WizardProgress: View {
    let steps: [String]
    let current: Int

    var body: some View {
        StepProgress(steps: steps, current: current)
    }
}
