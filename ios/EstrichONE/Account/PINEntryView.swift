import EstrichCore
import SwiftUI

/// Poolgerät: Tresor einer Person mit ihrer Geräte-PIN öffnen.
struct PINEntryView: View {
    let userId: UUID

    @Environment(AppModel.self) private var model
    @State private var pin = ""
    @State private var errorMessage: String?
    @State private var isWorking = false

    private var user: LocalUser? { model.deviceState.users.first { $0.id == userId } }

    var body: some View {
        NavigationStack {
            VStack(spacing: 24) {
                Spacer(minLength: 12)
                if let user {
                    VStack(spacing: 8) {
                        Initials(text: user.initials)
                        Text(user.displayName).font(Typo.title)
                        Text("Geräte-PIN eingeben").font(.callout).foregroundStyle(Theme.textSecondary)
                    }
                }
                if let errorMessage {
                    StatusMessage(kind: .error, text: errorMessage)
                        .padding(.horizontal, 24)
                        .accessibilityIdentifier("pinError")
                }
                PINPad(pin: $pin, isDisabled: isWorking, onComplete: { value in Task { await unlock(value) } })
                Spacer(minLength: 12)
            }
            .frame(maxWidth: 560)
            .frame(maxWidth: .infinity)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Zurück") { model.cancelSignIn() }
                }
                ToolbarItem(placement: .primaryAction) {
                    Button("Mit Passwort") {
                        if let user { model.selectUserWithPassword(user) }
                    }
                }
            }
            .sensoryFeedback(.error, trigger: errorMessage) { _, new in new != nil }
        }
    }

    private func unlock(_ value: String) async {
        isWorking = true
        defer {
            isWorking = false
            pin = ""
        }
        do throws(AccountError) {
            try await model.unlock(userId: userId, pin: value)
        } catch .pinLocked {
            if let user { model.selectUser(user) }
        } catch .vault(.wrongPIN) {
            let remaining = PINPolicy.maxAttempts - (user?.failedPINAttempts ?? 0)
            errorMessage = "Die PIN ist falsch. Noch \(remaining) \(remaining == 1 ? "Versuch" : "Versuche")."
        } catch {
            errorMessage = error.message
        }
    }
}
