import EstrichCore
import SwiftUI

/// Status einer Bauakte: Farbe, Symbol und Text gemeinsam (05, Abschnitt 2.3).
struct BauakteStatusChip: View {
    let status: BauakteStatus

    var body: some View {
        Label(status.title, systemImage: symbol)
            .labelStyle(TightLabelStyle())
            .font(.caption2.weight(.semibold))
            .imageScale(.small)
            .strikethrough(status == .cancelled)
            .lineLimit(1)
            .foregroundStyle(colors.foreground)
            .padding(.horizontal, 7)
            .padding(.vertical, 3)
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
            Label(showsSynced ? "Auf diesem Gerät gespeichert · wird übertragen" : "Wird übertragen", systemImage: "arrow.up.circle")
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

/// Zeile einer Bauakte in Listen: Bezeichnung und Status, darunter Nummer und Adresse – zwei ruhige Zeilen.
struct BauakteRow: View {
    let bauakte: Bauakte
    var customerName: String?

    var body: some View {
        VStack(alignment: .leading, spacing: 3) {
            HStack(alignment: .firstTextBaseline, spacing: 8) {
                Text(bauakte.title)
                    .font(.body.weight(.semibold))
                    .foregroundStyle(.primary)
                    .lineLimit(2)
                Spacer(minLength: 4)
                BauakteStatusChip(status: bauakte.status)
            }
            HStack(spacing: 4) {
                Text(bauakte.number ?? "Neu · noch ohne Nummer")
                    .monospacedDigit()
                    .fontWeight(.medium)
                if let address = bauakte.addressLine {
                    Text("·")
                    Text(address).lineLimit(1)
                }
            }
            .font(.caption)
            .foregroundStyle(Theme.textSecondary)
            if let customerName {
                Label(customerName, systemImage: "person")
                    .labelStyle(TightLabelStyle())
                    .font(.caption)
                    .foregroundStyle(Theme.textSecondary)
                    .lineLimit(1)
            }
            SyncStateBadge(state: bauakte.syncState)
        }
        .padding(.vertical, 2)
        .accessibilityElement(children: .combine)
    }
}

/// Abschnittsüberschrift mit Anzahl und optionaler Aktion rechts (z. B. „+ Eintrag“).
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
