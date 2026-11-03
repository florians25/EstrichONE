import EstrichCore
import SwiftUI

/// Schritt 4: Anmeldung mit Anmeldename oder E-Mail und Passwort; bei Zwei-Faktor-Konten zweiter Schritt mit
/// Bestätigungscode (Authenticator-App oder Wiederherstellungscode). Gilt für persönliche Geräte und Poolgeräte.
struct SignInView: View {
    let prefilledUserName: String?

    init(prefilledUserName: String?, showsTwoFactorStep: Bool = false) {
        self.prefilledUserName = prefilledUserName
        // Screenshot-Modus: Code-Abfrage direkt zeigen.
        _twoFactor = State(initialValue: showsTwoFactorStep)
        _password = State(initialValue: showsTwoFactorStep ? "demo" : "")
    }

    @Environment(AppModel.self) private var model
    @State private var login = ""
    @State private var password = ""
    @State private var errorMessage: String?
    @State private var isWorking = false
    @State private var showsPasswordHelp = false
    /// Der Server verlangt einen Bestätigungscode (401 TWO_FACTOR_REQUIRED).
    @State private var twoFactor = false
    @State private var code = ""
    @State private var usesRecoveryCode = false
    @State private var reporting = false
    @FocusState private var focus: Field?

    private enum Field {
        case login
        case password
        case code
    }

    // Für echte Benutzer: Passwort-Manager und „Passwort sichern?“. In UI-Tests nicht, weil das Systemblatt
    // zeitversetzt erscheint und Tests zufällig verdeckt.
    private static var isUITest: Bool {
        #if DEBUG
        UITestSupport.isActive
        #else
        false
        #endif
    }

    private static var loginContentType: UITextContentType? { isUITest ? nil : .username }
    private static var passwordContentType: UITextContentType? { isUITest ? nil : .password }
    private static var codeContentType: UITextContentType? { isUITest ? nil : .oneTimeCode }

    private var canSubmit: Bool {
        if isWorking || login.trimmingCharacters(in: .whitespaces).isEmpty || password.isEmpty { return false }
        return !twoFactor || TwoFactorLogin.isComplete(code, recovery: usesRecoveryCode)
    }

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: Space.l) {
                    VStack(alignment: .leading, spacing: Space.s) {
                        BrandMark()
                            .frame(width: 52, height: 52)
                            .accessibilityHidden(true)
                        Text("Anmelden")
                            .font(Typo.display)
                            .foregroundStyle(Theme.textPrimary)
                            .accessibilityAddTraits(.isHeader)
                        if let server = model.server {
                            Label(server.info.serverName, systemImage: "building.2")
                                .labelStyle(TightLabelStyle())
                                .font(Typo.subheadStrong)
                                .foregroundStyle(Theme.textSecondary)
                        }
                    }
                    .padding(.bottom, Space.s)

                    if model.isDemo, !twoFactor {
                        Label("Demo: „m.huber“ oder mit Zwei-Faktor-Anmeldung „s.berger“, Passwort „demo“, Code 246810.",
                              systemImage: "play.circle")
                            .font(.callout)
                            .foregroundStyle(Theme.textSecondary)
                            .accessibilityIdentifier("demoSignInHint")
                    }

                    if model.hasEnrollmentCode {
                        Label("Einrichtungscode erkannt – das Gerät wird nach der Anmeldung sofort freigegeben.", systemImage: "qrcode")
                            .font(.callout)
                            .foregroundStyle(Theme.textSecondary)
                    }

                    field("Anmeldename oder E-Mail") {
                        TextField("m.huber", text: $login)
                            .textContentType(Self.loginContentType)
                            .textInputAutocapitalization(.never)
                            .autocorrectionDisabled()
                            .submitLabel(.next)
                            .focused($focus, equals: .login)
                            .onSubmit { focus = .password }
                            .accessibilityIdentifier("loginField")
                    }
                    .disabled(twoFactor)

                    field("Passwort") {
                        SecureField("", text: $password)
                            .textContentType(Self.passwordContentType)
                            .submitLabel(.go)
                            .focused($focus, equals: .password)
                            .onSubmit { Task { await signIn() } }
                            .accessibilityIdentifier("passwordField")
                    }
                    .disabled(twoFactor)

                    if twoFactor {
                        twoFactorStep
                    }

                    if let errorMessage {
                        StatusMessage(kind: .error, text: errorMessage)
                            .accessibilityIdentifier("signInError")
                    }

                    Button {
                        Task { await signIn() }
                    } label: {
                        ActionLabel(isWorking ? (twoFactor ? "Wird geprüft …" : "Anmeldung läuft …") : (twoFactor ? "Bestätigen" : "Anmelden"),
                                    systemImage: twoFactor ? "checkmark.shield" : "person.badge.key", isBusy: isWorking)
                    }
                    .primaryActionStyle()
                    .disabled(!canSubmit)
                    .accessibilityIdentifier("signInButton")

                    if twoFactor {
                        Button("Andere Anmeldung") { leaveTwoFactor() }
                            .font(Typo.subheadStrong)
                            .tint(Theme.accent)
                            .frame(maxWidth: .infinity)
                            .frame(minHeight: 44)
                            .disabled(isWorking)
                            .accessibilityIdentifier("twoFactorBackButton")
                    }

                    Button("Passwort vergessen?") { showsPasswordHelp = true }
                        .font(Typo.subheadStrong)
                        .tint(Theme.accent)
                        .frame(maxWidth: .infinity)
                        .frame(minHeight: 44)

                    if model.canReportWithoutSignIn {
                        Button("Anmeldung klappt nicht? Problem melden") { reporting = true }
                            .font(Typo.subheadStrong)
                            .tint(Theme.accent)
                            .frame(maxWidth: .infinity)
                            .frame(minHeight: 44)
                            .accessibilityIdentifier("reportWithoutSignInButton")
                    }

                    // Notausgang: Anmeldung klappt gar nicht (falscher Server, kaputte Daten) – alles löschen, neu einrichten.
                    if model.deviceRegistration != nil {
                        ResetAppButton()
                            .frame(maxWidth: .infinity)
                    }
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
            .sheet(isPresented: $reporting) {
                ReportProblemView(initialCategory: .signIn, withoutSignIn: true) { reporting = false }
            }
            .onAppear {
                if twoFactor {
                    login = prefilledUserName ?? login
                    focus = .code
                } else if let prefilledUserName, login.isEmpty {
                    login = prefilledUserName
                    focus = .password
                } else {
                    focus = .login
                }
            }
        }
    }

    /// Zweiter Schritt: Code aus der Authenticator-App (Ziffern, iOS schlägt Codes aus „Passwörter“ vor) oder
    /// Wiederherstellungscode `xxxxx-xxxxx`.
    @ViewBuilder
    private var twoFactorStep: some View {
        VStack(alignment: .leading, spacing: Space.s) {
            Label("Zwei-Faktor-Anmeldung", systemImage: "lock.shield")
                .labelStyle(TightLabelStyle())
                .font(Typo.headline)
                .foregroundStyle(Theme.textPrimary)
            Text(usesRecoveryCode
                 ? "Geben Sie einen Ihrer Wiederherstellungscodes ein (Format xxxxx-xxxxx). Jeder Code gilt nur einmal."
                 : "Ihr Konto ist zusätzlich geschützt. Geben Sie den 6-stelligen Code aus Ihrer Authenticator-App ein.")
                .font(Typo.callout)
                .foregroundStyle(Theme.textSecondary)
                .fixedSize(horizontal: false, vertical: true)
                .accessibilityIdentifier("twoFactorHint")
        }
        .padding(.top, Space.xs)

        field(usesRecoveryCode ? "Wiederherstellungscode" : "Bestätigungscode") {
            TextField(usesRecoveryCode ? "abcde-12345" : "123456", text: $code)
                .textContentType(usesRecoveryCode ? nil : Self.codeContentType)
                .keyboardType(usesRecoveryCode ? .asciiCapable : .numberPad)
                .textInputAutocapitalization(.never)
                .autocorrectionDisabled()
                .font(.title3.monospacedDigit().weight(.semibold))
                .submitLabel(.go)
                .focused($focus, equals: .code)
                .onSubmit { Task { await signIn() } }
                .onChange(of: code) { _, new in
                    guard !new.isEmpty else { return }
                    errorMessage = nil
                    // Vorschlag der Tastatur bzw. sechste Ziffer: sofort prüfen.
                    if !usesRecoveryCode, !isWorking, TwoFactorLogin.isComplete(new, recovery: false) {
                        Task { await signIn() }
                    }
                }
                .accessibilityIdentifier("twoFactorCodeField")
        }

        Button(usesRecoveryCode ? "Code aus der Authenticator-App verwenden" : "Wiederherstellungscode verwenden") {
            usesRecoveryCode.toggle()
            code = ""
            errorMessage = nil
            focus = .code
        }
        .font(Typo.subheadStrong)
        .tint(Theme.accent)
        .frame(minHeight: 44)
        .disabled(isWorking)
        .accessibilityIdentifier("twoFactorModeButton")
    }

    private func leaveTwoFactor() {
        twoFactor = false
        usesRecoveryCode = false
        code = ""
        password = ""
        errorMessage = nil
        focus = .password
    }

    private func field(_ title: LocalizedStringKey, @ViewBuilder content: () -> some View) -> some View {
        VStack(alignment: .leading, spacing: Space.s - 2) {
            Text(title)
                .font(Typo.subheadStrong)
                .foregroundStyle(Theme.textSecondary)
            content()
                .font(Typo.body)
                .padding(.horizontal, Space.m + 2)
                .frame(minHeight: Size.button)
                .background(Theme.surface, in: RoundedRectangle(cornerRadius: Radius.m, style: .continuous))
                .overlay {
                    RoundedRectangle(cornerRadius: Radius.m, style: .continuous).strokeBorder(Theme.separator, lineWidth: 1)
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
            try await model.signIn(login: login, password: password, twoFactorCode: twoFactor ? code : nil)
        } catch {
            if case let .api(apiError) = error, apiError.requiresTwoFactorCode {
                // Passwort stimmt, das Konto hat Zwei-Faktor-Anmeldung: Code abfragen, dieselbe Anmeldung wiederholen.
                twoFactor = true
                code = ""
                focus = .code
            } else if twoFactor, case let .api(apiError) = error, apiError.isInvalidTwoFactorCode {
                code = ""
                errorMessage = usesRecoveryCode
                    ? "Der Wiederherstellungscode ist ungültig oder wurde bereits verwendet."
                    : apiError.message
                focus = .code
            } else if twoFactor, case let .api(apiError) = error, apiError.code == "LOGIN_LOCKED" || apiError.status == nil {
                // Gesperrt bzw. offline: im Code-Schritt bleiben, später erneut versuchen.
                errorMessage = error.message
            } else {
                leaveTwoFactor()
                errorMessage = error.message
            }
        }
    }
}
