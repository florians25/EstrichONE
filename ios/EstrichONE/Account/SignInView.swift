import EstrichCore
import SwiftUI

/// Schritt 4: Anmeldung mit Anmeldename oder E-Mail und Passwort.
struct SignInView: View {
    let prefilledUserName: String?

    @Environment(AppModel.self) private var model
    @State private var login = ""
    @State private var password = ""
    @State private var errorMessage: String?
    @State private var isWorking = false
    @State private var showsPasswordHelp = false
    @FocusState private var focus: Field?

    private enum Field {
        case login
        case password
    }

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: Theme.spacing) {
                    VStack(alignment: .leading, spacing: 4) {
                        Text("Anmelden")
                            .font(.largeTitle.weight(.bold))
                            .accessibilityAddTraits(.isHeader)
                        if let server = model.server {
                            Text(server.info.serverName)
                                .font(.callout)
                                .foregroundStyle(Theme.textSecondary)
                        }
                    }

                    if model.hasEnrollmentCode {
                        Label("Einrichtungscode erkannt – das Gerät wird nach der Anmeldung sofort freigegeben.", systemImage: "qrcode")
                            .font(.callout)
                            .foregroundStyle(Theme.textSecondary)
                    }

                    if let server = model.server, !server.info.authMethods.contains("local") {
                        StatusMessage(kind: .error, text: "Dieser Server erlaubt nur die Anmeldung mit Microsoft. Diese Anmeldung folgt in einer späteren App-Version.")
                    }

                    field("Anmeldename oder E-Mail") {
                        TextField("m.huber", text: $login)
                            .textContentType(.username)
                            .textInputAutocapitalization(.never)
                            .autocorrectionDisabled()
                            .submitLabel(.next)
                            .focused($focus, equals: .login)
                            .onSubmit { focus = .password }
                            .accessibilityIdentifier("loginField")
                    }

                    field("Passwort") {
                        SecureField("", text: $password)
                            .textContentType(.password)
                            .submitLabel(.go)
                            .focused($focus, equals: .password)
                            .onSubmit { Task { await signIn() } }
                            .accessibilityIdentifier("passwordField")
                    }

                    if let errorMessage {
                        StatusMessage(kind: .error, text: errorMessage)
                            .accessibilityIdentifier("signInError")
                    }

                    Button {
                        Task { await signIn() }
                    } label: {
                        ActionLabel(isWorking ? "Anmeldung läuft …" : "Anmelden", systemImage: "person.badge.key", isBusy: isWorking)
                    }
                    .primaryActionStyle()
                    .disabled(isWorking || login.trimmingCharacters(in: .whitespaces).isEmpty || password.isEmpty)
                    .accessibilityIdentifier("signInButton")

                    Button("Passwort vergessen?") { showsPasswordHelp = true }
                        .font(.callout)
                        .tint(Theme.actionPrimary)
                        .frame(maxWidth: .infinity)
                        .frame(minHeight: 44)
                }
                .padding(24)
                .frame(maxWidth: 560)
                .frame(maxWidth: .infinity)
            }
            .scrollDismissesKeyboard(.interactively)
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                if model.deviceRegistration == nil {
                    ToolbarItem(placement: .cancellationAction) {
                        Button("Anderer Server") { model.resetApp() }
                    }
                } else if !model.deviceState.users.isEmpty {
                    ToolbarItem(placement: .cancellationAction) {
                        Button("Abbrechen") { model.cancelSignIn() }
                    }
                }
            }
            .alert("Passwort vergessen", isPresented: $showsPasswordHelp) {
                Button("OK", role: .cancel) {}
            } message: {
                Text("Ihre Administration kann das Passwort in EstrichONE zurücksetzen. Sie erhalten dann ein Einmalpasswort.")
            }
            .sensoryFeedback(.error, trigger: errorMessage) { _, new in new != nil }
            .onAppear {
                if let prefilledUserName, login.isEmpty {
                    login = prefilledUserName
                    focus = .password
                } else {
                    focus = .login
                }
            }
        }
    }

    private func field(_ title: LocalizedStringKey, @ViewBuilder content: () -> some View) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(title)
                .font(.subheadline.weight(.medium))
                .foregroundStyle(Theme.textSecondary)
            content()
                .padding(.horizontal, 14)
                .frame(minHeight: 52)
                .background(Color(uiColor: .secondarySystemBackground), in: RoundedRectangle(cornerRadius: 10, style: .continuous))
                .overlay {
                    RoundedRectangle(cornerRadius: 10, style: .continuous).strokeBorder(Theme.line, lineWidth: 1)
                }
                .disabled(isWorking)
        }
    }

    private func signIn() async {
        guard !isWorking else { return }
        errorMessage = nil
        // Tastatur schließen, bevor der nächste Bildschirm (z. B. die PIN-Tastatur) erscheint.
        focus = nil
        isWorking = true
        defer { isWorking = false }
        do throws(AccountError) {
            try await model.signIn(login: login, password: password)
        } catch {
            password = ""
            errorMessage = error.message
        }
    }
}
