import EstrichCore
import SwiftUI

/// Status einer Bauakte: Farbe, Symbol und Text gemeinsam (05, Abschnitt 2.3).
struct BauakteStatusChip: View {
    let status: BauakteStatus

    var body: some View {
        Label(status.title, systemImage: symbol)
            .font(.caption.weight(.semibold))
            .strikethrough(status == .cancelled)
            .foregroundStyle(colors.foreground)
            .padding(.horizontal, 8)
            .padding(.vertical, 4)
            .background(colors.background, in: Capsule())
    }

    private var symbol: String {
        switch status {
        case .inquiry: "questionmark.circle"
        case .planned: "calendar"
        case .inProgress: "hammer.fill"
        case .drying: "hourglass"
        case .completed: "checkmark.circle.fill"
        case .cancelled: "xmark.circle"
        }
    }

    private var colors: (foreground: Color, background: Color) {
        switch status {
        case .inquiry, .cancelled: (Theme.neutral, Theme.neutralBackground)
        case .planned: (Theme.info, Theme.infoBackground)
        case .inProgress: (Theme.navyText, Theme.navyBackground)
        case .drying: (Theme.hint, Theme.hintBackground)
        case .completed: (Theme.success, Theme.successBackground)
        }
    }
}

/// Übertragungszustand eines Datensatzes (15, Abschnitt 4): ● gespeichert · ✓ übertragen · ⚠ Problem.
struct SyncStateBadge: View {
    let state: SyncState
    var showsSynced = false

    var body: some View {
        switch state {
        case .pending:
            Label("Auf diesem Gerät gespeichert · wird übertragen", systemImage: "arrow.up.circle")
                .font(.caption)
                .foregroundStyle(Theme.textSecondary)
        case .problem:
            Label("Problem bei der Übertragung", systemImage: "exclamationmark.triangle.fill")
                .font(.caption.weight(.semibold))
                .foregroundStyle(Theme.error)
        case .synced:
            if showsSynced {
                Label("Übertragen", systemImage: "checkmark.circle")
                    .font(.caption)
                    .foregroundStyle(Theme.success)
            }
        }
    }
}

/// Zeile einer Bauakte in Listen.
struct BauakteRow: View {
    let bauakte: Bauakte

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack(alignment: .firstTextBaseline) {
                Text(bauakte.number ?? "Neu · noch ohne Nummer")
                    .font(.caption.monospacedDigit().weight(.semibold))
                    .foregroundStyle(Theme.textSecondary)
                Spacer()
                BauakteStatusChip(status: bauakte.status)
            }
            Text(bauakte.title)
                .font(.headline)
                .foregroundStyle(.primary)
            if let address = bauakte.addressLine {
                Text(address).font(.subheadline).foregroundStyle(Theme.textSecondary)
            }
            SyncStateBadge(state: bauakte.syncState)
        }
        .padding(.vertical, 4)
        .accessibilityElement(children: .combine)
    }
}
