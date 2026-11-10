import EstrichCore
import SwiftUI

/// Bestätigung „Erstbegehung nicht erforderlich“ mit optionaler Begründung (gleiche Texte wie im Büro).
/// Wirkt sofort lokal, die Übertragung folgt über die Synchronisation.
struct WaiveInitialInspectionAlert: ViewModifier {
    @Binding var isPresented: Bool
    let bauakteId: UUID
    /// Meldung für die Rückmeldung (Toast); danach neu laden.
    let done: (String) -> Void

    @Environment(AppModel.self) private var model
    @State private var reason = ""

    func body(content: Content) -> some View {
        content.alert(InitialInspectionState.waiveButtonTitle, isPresented: $isPresented) {
            TextField("Begründung (optional)", text: $reason)
                .accessibilityIdentifier("waiveReasonField")
            Button("Abbrechen", role: .cancel) { reason = "" }
            Button("Bestätigen") {
                let text = reason
                reason = ""
                Task { await waive(text) }
            }
            .accessibilityIdentifier("waiveConfirmButton")
        } message: {
            Text(InitialInspectionState.confirmMessage)
        }
    }

    private func waive(_ text: String) async {
        guard let service = model.active?.service else { return }
        do throws(BauakteError) {
            try await service.waiveInitialInspection(of: bauakteId, reason: text)
            model.active?.sync?.localChangeMade()
            done("Vermerkt: Keine Erstbegehung erforderlich")
        } catch {
            done(error.message)
        }
    }
}

extension View {
    func waiveInitialInspectionAlert(isPresented: Binding<Bool>, bauakteId: UUID, done: @escaping (String) -> Void) -> some View {
        modifier(WaiveInitialInspectionAlert(isPresented: isPresented, bauakteId: bauakteId, done: done))
    }
}

/// Vermerk „Keine Erstbegehung erforderlich (Name, Datum, Begründung)“ mit „Rückgängig“.
struct InitialInspectionWaiverRow: View {
    let bauakte: Bauakte
    /// `nil` ohne Recht „Bauakte bearbeiten“: nur anzeigen.
    let undo: (() -> Void)?

    var body: some View {
        VStack(alignment: .leading, spacing: Space.s) {
            Label {
                Text(bauakte.initialInspectionWaiverText ?? InitialInspectionState.waivedTitle)
                    .font(Typo.body)
                    .foregroundStyle(Theme.textPrimary)
                    .fixedSize(horizontal: false, vertical: true)
            } icon: {
                Image(systemName: "checkmark.circle")
                    .foregroundStyle(Theme.textSecondary)
            }
            .accessibilityIdentifier("initialInspectionWaiver")
            if let undo {
                Button(InitialInspectionState.undoTitle, systemImage: "arrow.uturn.backward", action: undo)
                    .font(Typo.subheadStrong)
                    .foregroundStyle(Theme.accent)
                    .accessibilityIdentifier("undoWaiverButton")
            }
        }
        .padding(.vertical, Space.xs)
    }
}
