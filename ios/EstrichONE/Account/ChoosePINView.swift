import EstrichCore
import SwiftUI

/// Poolgerät: Geräte-PIN bei der ersten Anmeldung auf diesem Gerät festlegen (11, Abschnitt 4.2).
struct ChoosePINView: View {
    @Environment(AppModel.self) private var model
    @State private var pin = ""
    @State private var firstEntry: String?
    @State private var errorMessage: String?
    @State private var isWorking = false

    var body: some View {
        VStack(spacing: 24) {
            Spacer(minLength: 12)
            VStack(spacing: 8) {
                Text(firstEntry == nil ? "Geräte-PIN festlegen" : "PIN wiederholen")
                    .font(Typo.title)
                    .accessibilityIdentifier("choosePINTitle")
                Text("Mit dieser PIN melden Sie sich künftig auf diesem Gerät an. Sie gilt nur hier und wird nicht an den Server übertragen.")
                    .font(.callout)
                    .foregroundStyle(Theme.textSecondary)
                    .multilineTextAlignment(.center)
                    .fixedSize(horizontal: false, vertical: true)
            }
            .padding(.horizontal, 24)

            if let errorMessage {
                StatusMessage(kind: .error, text: errorMessage)
                    .padding(.horizontal, 24)
                    .accessibilityIdentifier("pinError")
            }

            PINPad(pin: $pin, isDisabled: isWorking, onComplete: { value in Task { await entered(value) } })
            Spacer(minLength: 12)
        }
        .frame(maxWidth: 560)
        .frame(maxWidth: .infinity)
        .sensoryFeedback(.error, trigger: errorMessage) { _, new in new != nil }
    }

    private func entered(_ value: String) async {
        defer { pin = "" }
        guard let first = firstEntry else {
            do throws(PINPolicy.Violation) {
                try PINPolicy.validate(value)
                errorMessage = nil
                firstEntry = value
            } catch {
                errorMessage = error.message
            }
            return
        }
        guard first == value else {
            firstEntry = nil
            errorMessage = "Die beiden Eingaben stimmen nicht überein. Bitte erneut festlegen."
            return
        }
        isWorking = true
        defer { isWorking = false }
        do throws(AccountError) {
            try await model.choosePIN(value)
        } catch {
            firstEntry = nil
            errorMessage = error.message
        }
    }
}
