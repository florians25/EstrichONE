import EstrichCore
import SwiftUI

/// Pflichtwechsel nach einem Einmalpasswort der Administration.
struct ChangePasswordView: View {
    @Environment(AppModel.self) private var model
    @State private var current = ""
    @State private var new = ""
    @State private var repeated = ""
    @State private var errorMessage: String?
    @State private var isWorking = false

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    Text("Bitte legen Sie ein eigenes Passwort fest. Es muss mindestens 10 Zeichen lang sein.")
                }
                Section {
                    SecureField("Bisheriges Passwort", text: $current).textContentType(.password)
                    SecureField("Neues Passwort", text: $new).textContentType(.newPassword)
                    SecureField("Neues Passwort wiederholen", text: $repeated).textContentType(.newPassword)
                }
                if let errorMessage {
                    Section {
                        StatusMessage(kind: .error, text: errorMessage)
                    }
                }
                Section {
                    Button {
                        Task { await save() }
                    } label: {
                        ActionLabel("Passwort ändern", isBusy: isWorking)
                    }
                    .primaryActionStyle()
                    .disabled(isWorking || current.isEmpty || new.isEmpty)
                    .listRowBackground(Color.clear)
                    .listRowInsets(EdgeInsets())
                }
            }
            .navigationTitle("Neues Passwort")
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Abbrechen") { model.cancelSignIn() }
                }
            }
        }
    }

    private func save() async {
        guard new == repeated else {
            errorMessage = "Die beiden neuen Passwörter stimmen nicht überein."
            return
        }
        errorMessage = nil
        isWorking = true
        defer { isWorking = false }
        do throws(AccountError) {
            try await model.changePassword(current: current, new: new)
        } catch {
            errorMessage = error.message
        }
    }
}
