import EstrichCore
import Foundation
import Observation

/// Bildschirm, den die App gerade zeigt. Der Ablauf folgt docs/reengineering/10 (Schritte 1–5) und 11.
enum Screen: Equatable {
    /// Schritt 1–2: Server wählen und bestätigen.
    case onboarding
    /// Schritt 3: persönliches Gerät oder Poolgerät (entfällt bei einem QR-Code mit festgelegtem Typ).
    case deviceKind
    /// Schritt 4: Anmeldung. `userName` ist vorbelegt, wenn eine bekannte Person sich erneut anmeldet.
    case signIn(userName: String?)
    case changePassword
    /// Poolgerät: Geräte-PIN festlegen.
    case choosePIN
    /// Poolgerät gesperrt: Wer arbeitet mit dem Gerät?
    case userPicker
    case pinEntry(UUID)
    /// Persönliches Gerät gesperrt: Face ID / Touch ID / Gerätecode.
    case appLocked
    case waitingForApproval
    case home
    /// Der Server hat das Gerät oder die Person gesperrt bzw. entfernt.
    case blocked(SessionEnd)
}

/// Angemeldete Person mit ihrem API-Zugang.
struct ActiveAccount {
    let account: UnlockedAccount
    let client: AuthorizedClient
    /// Lokale Daten und Synchronisation; `nil`, wenn die Datenbank nicht geöffnet werden konnte.
    let sync: SyncController?
    let service: BauakteService?
    let thumbnails: ThumbnailLoader?
    var user: UserSummary { account.user }
}

@MainActor
@Observable
final class AppModel {
    private(set) var screen: Screen = .onboarding
    private(set) var server: VerifiedServer?
    private(set) var deviceState = DeviceState()
    private(set) var active: ActiveAccount?

    /// Per Link oder QR-Code übergebener Einrichtungscode, der auf die Verbindungsprüfung wartet.
    var pendingEnrollment: EnrollmentLink?
    /// Hinweis für den Benutzer, etwa wenn ein Einrichtungslink nicht angewendet werden kann.
    var notice: String?

    var hasNotice: Bool {
        get { notice != nil }
        set { if !newValue { notice = nil } }
    }

    let checker: ServerConnectionChecker
    private let environment: AppEnvironment
    private let serverStore: ServerStore
    private(set) var accounts: AccountService?

    /// Aus dem Einrichtungscode bzw. der Auswahl in Schritt 3; gilt bis zur Registrierung.
    private var requestedKind: DeviceKind?
    private var enrollmentCode: String?
    /// Frisch angemeldet, Tresor noch nicht angelegt (Passwortwechsel, PIN-Wahl).
    private var pendingTokens: TokenResponse?
    private var backgroundSince: Date?
    /// Ein API-Zugang je Person, auch über Sperren und Entsperren hinweg: Zwei Zugänge mit derselben Sitzung würden
    /// denselben Refresh-Token einlösen (der Server wertete das früher als Diebstahl und meldete ab).
    private var clients: [UUID: AuthorizedClient] = [:]
    /// Sperre persönlicher Geräte (Mehr › Sperre und Face ID).
    private(set) var lockPreferences: AppLockPreferences
    /// Hinweis auf dem Sperrbildschirm (z. B. nach geänderter Biometrie).
    private(set) var lockMessage: String?
    private(set) var isUnlocking = false
    private var unlockKeys: UnlockKeychain { UnlockKeychain(defaults: environment.defaults) }

    /// Wechsel in den Demomodus (`true`) bzw. zurück (`false`); setzt die App-Sitzung.
    var onDemoRequest: ((Bool) -> Void)?
    /// „App zurücksetzen“: Die App-Sitzung löscht alles (`AppWipe`) und baut das Modell neu auf.
    var onResetRequest: (() -> Void)?

    var isDemo: Bool { environment.isDemo }

    /// „Demo ausprobieren“: richtet das Gerät ohne Eingaben mit dem Demoserver ein und meldet „Max Huber“ an.
    func startDemo() async {
        guard environment.isDemo else { return }
        if active != nil { return }
        guard let address = try? ServerAddress.parse(AppEnvironment.demoServerAddress),
              case let .success(server) = await checker.check(address) else { return }
        do {
            try confirm(server, enrollment: nil)
            chooseKind(.personal)
            try await signIn(login: "m.huber", password: "demo")
        } catch {
            notice = "Der Demomodus konnte nicht gestartet werden."
        }
    }

    func requestDemo(_ start: Bool) {
        onDemoRequest?(start)
    }

    init(environment: AppEnvironment) {
        self.environment = environment
        serverStore = ServerStore(defaults: environment.defaults)
        lockPreferences = AppLockPreferences.load(from: environment.defaults)
        checker = ServerConnectionChecker(transport: environment.transport, appVersion: environment.appVersion)
        if let server = serverStore.load() {
            open(server)
        }
    }

    var deviceRegistration: DeviceRegistration? { deviceState.registration }
    var poolInactivityTimeout: TimeInterval { environment.poolInactivityTimeout }
    var isPoolDevice: Bool { deviceState.registration?.kind == .pool }

    // MARK: Schritt 2–3

    /// Der Benutzer hat den geprüften Server sichtbar bestätigt (10, Schritt 2).
    func confirm(_ server: VerifiedServer, enrollment: EnrollmentLink?) throws {
        if let previous = self.server, previous.address != server.address {
            // Neueinrichtung an einem anderen Server: Daten des bisherigen dürfen nicht dorthin gelangen.
            accounts?.eraseDevice()
            deviceState = DeviceState()
        }
        try serverStore.save(server)
        let fromSameServer = enrollment?.server == server.address
        requestedKind = fromSameServer ? enrollment?.deviceKind : nil
        enrollmentCode = fromSameServer ? enrollment?.enrollmentCode : nil
        open(server)
    }

    func chooseKind(_ kind: DeviceKind) {
        requestedKind = kind
        screen = .signIn(userName: nil)
    }

    /// Einrichtungscode eines bestätigten Servers (nur bis zur Registrierung im Speicher, nie gespeichert).
    var hasEnrollmentCode: Bool { enrollmentCode != nil }

    // MARK: Schritt 4–5

    func signIn(login: String, password: String) async throws(AccountError) {
        let accounts = try requireAccounts()
        let tokens = try await accounts.signIn(login: login, password: password, clientName: environment.deviceName(for: nil))
        if tokens.mustChangePassword {
            pendingTokens = tokens
            screen = .changePassword
        } else {
            try await finishSignIn(tokens)
        }
    }

    func changePassword(current: String, new: String) async throws(AccountError) {
        guard let tokens = pendingTokens else { return }
        try await requireAccounts().changePassword(tokens: tokens, current: current, new: new)
        try await finishSignIn(tokens)
    }

    private func finishSignIn(_ tokens: TokenResponse) async throws(AccountError) {
        let accounts = try requireAccounts()
        let registration = try await accounts.bindDevice(
            tokens: tokens, kind: requestedKind ?? .personal, enrollmentCode: enrollmentCode,
            description: environment.deviceDescription(for: tokens.user))
        enrollmentCode = nil
        deviceState = accounts.state

        if registration.kind == .pool {
            pendingTokens = tokens
            screen = .choosePIN
        } else {
            pendingTokens = nil
            activate(try accounts.createVault(for: tokens, pin: nil), freshSession: true)
        }
    }

    func choosePIN(_ pin: String) async throws(AccountError) {
        guard let tokens = pendingTokens else { return }
        let accounts = try requireAccounts()
        let account = try await Self.offMain { () throws(AccountError) in try accounts.createVault(for: tokens, pin: pin) }
        pendingTokens = nil
        activate(account, freshSession: true)
    }

    // MARK: Entsperren

    func unlock(userId: UUID, pin: String) async throws(AccountError) {
        let accounts = try requireAccounts()
        do throws(AccountError) {
            activate(try await Self.offMain { () throws(AccountError) in try accounts.unlock(userId: userId, pin: pin) })
        } catch {
            deviceState = accounts.state
            throw error
        }
    }

    /// Persönliches Gerät: Face ID/Touch ID bzw. Gerätecode geben den Datenschlüssel im Schlüsselbund frei
    /// (`UnlockKeychain`); damit öffnet sich die gespeicherte Sitzung – ohne Passwort.
    func unlockPersonal() async {
        guard let user = deviceState.users.first else {
            screen = .signIn(userName: nil)
            return
        }
        // Nur ein Systemdialog zur Zeit (automatisch beim Erscheinen und per Schaltfläche).
        guard !isUnlocking else { return }
        isUnlocking = true
        defer { isUnlocking = false }
        let accounts: AccountService
        do throws(AccountError) {
            accounts = try requireAccounts()
        } catch {
            notice = error.message
            return
        }
        if !environment.skipsDeviceOwnerAuthentication {
            let biometryChanged = lockPreferences.biometricUnlock && unlockKeys.biometryChanged(userId: user.id)
            switch await UnlockKeychain.load(userId: user.id, reason: "EstrichONE entsperren") {
            case let .key(dataKey):
                if let account = try? accounts.unlock(userId: user.id, dataKey: dataKey) {
                    lockMessage = nil
                    activate(account)
                    if biometryChanged {
                        // Nach geänderten Gesichtern/Fingern gilt Face ID erst wieder nach dem Gerätecode – der ist jetzt
                        // eingegeben: Freigabe mit den aktuellen Merkmalen neu anlegen.
                        unlockKeys.save(dataKey, userId: user.id, biometric: true)
                        notice = "\(BiometryKind.current.name) wurde auf diesem Gerät geändert. EstrichONE hat deshalb einmal den Gerätecode verlangt; ab jetzt genügt wieder \(BiometryKind.current.name)."
                    }
                    return
                }
                // Freigabe passt nicht mehr zum Tresor: verwerfen und einmal über den Geräteschlüssel öffnen.
                unlockKeys.remove(userId: user.id)
            case .cancelled:
                return
            case .failed:
                lockMessage = "Entsperren nicht möglich. Bitte erneut versuchen."
                return
            case .missing:
                break
            }
            guard await environment.authenticateDeviceOwner() else { return }
        }
        do throws(AccountError) {
            lockMessage = nil
            activate(try accounts.unlock(userId: user.id, pin: nil))
        } catch {
            notice = error.message
            screen = .signIn(userName: user.userName)
        }
    }

    /// Mehr › Sperre: Face ID an/aus. Legt die Freigabe im Schlüsselbund mit der passenden Zugriffsregel neu an.
    func setBiometricUnlock(_ enabled: Bool) {
        lockPreferences.biometricUnlock = enabled
        lockPreferences.save(to: environment.defaults)
        if let active, !isPoolDevice, !environment.skipsDeviceOwnerAuthentication {
            unlockKeys.save(active.account.dataKey, userId: active.user.id, biometric: enabled)
        }
    }

    func setLockDelay(_ delay: LockDelay) {
        lockPreferences.delay = delay
        lockPreferences.save(to: environment.defaults)
    }

    func selectUser(_ user: LocalUser) {
        screen = user.requiresPasswordLogin ? .signIn(userName: user.userName) : .pinEntry(user.id)
    }

    /// Anmeldung mit Passwort statt PIN (z. B. PIN vergessen); legt den Tresor danach neu an.
    func selectUserWithPassword(_ user: LocalUser) {
        screen = .signIn(userName: user.userName)
    }

    func addPerson() {
        screen = .signIn(userName: nil)
    }

    func cancelSignIn() {
        pendingTokens = nil
        screen = lockedScreen()
    }

    // MARK: Sperren, Abmelden, Zurücksetzen

    /// Sperrt den Tresor: Poolgerät zeigt die Benutzerauswahl, nie die letzte Sitzung (11, AK-2).
    func lock() {
        guard let active else { return }
        // Vor dem Wechsel noch einmal übertragen; was offline bleibt, wartet verschlüsselt auf die nächste
        // Anmeldung derselben Person (11, Abschnitt 4.4; einfache Variante für 1.0).
        if let sync = active.sync {
            sync.stop()
            Task { await sync.run() }
        }
        self.active = nil
        screen = lockedScreen()
    }

    /// Anzahl noch nicht übertragener Vorgänge und Dateien (Warnung vor dem Abmelden).
    func unsentItems() async -> Int {
        guard let sync = active?.sync else { return 0 }
        await sync.run()
        guard let summary = sync.summary else { return 0 }
        return summary.pendingOperations + summary.pendingUploads
    }

    func signOut() async {
        guard let active, let accounts else { return }
        active.sync?.stop()
        await PushCenter.shared.unregister(client: active.client)
        self.active = nil
        clients[active.user.id] = nil
        unlockKeys.remove(userId: active.user.id)
        await accounts.signOut(active.account, client: active.client)
        deviceState = accounts.state
        screen = lockedScreen()
    }

    /// Alles löschen: Server, Gerät, Benutzer (Einstellungen › App zurücksetzen; Gerät entfernt).
    /// Entferntes Gerät neu einrichten: Server bestätigen bzw. neuen Einrichtungscode scannen oder eingeben, anmelden.
    /// Server und Daten bleiben bis dahin erhalten; wird ein anderer Server gewählt, werden die alten Daten gelöscht.
    func startReEnrollment() {
        active = nil
        clients = [:]
        requestedKind = nil
        enrollmentCode = nil
        pendingTokens = nil
        pendingEnrollment = nil
        screen = .onboarding
    }

    /// „App zurücksetzen“ von jedem Bildschirm: alles löschen und neu einrichten (`AppSession.reset`).
    /// Ohne App-Sitzung (Tests) nur der Zustand im Modell.
    func requestReset() {
        if let onResetRequest {
            onResetRequest()
        } else {
            resetApp()
        }
    }

    /// Vor dem Löschen: Synchronisation stoppen, Push beim Server abmelden (höchstens 3 Sekunden), alles Offene
    /// freigeben. Danach hält das Modell keine Datenbank und keinen Schlüssel mehr.
    func shutdownForWipe() async {
        let current = active
        active = nil
        clients = [:]
        current?.sync?.stop()
        if let client = current?.client, !isDemo {
            await PushCenter.shared.unregister(client: client, timeout: .seconds(3))
        }
        accounts = nil
        server = nil
        deviceState = DeviceState()
        requestedKind = nil
        enrollmentCode = nil
        pendingTokens = nil
        pendingEnrollment = nil
        screen = .onboarding
    }

    /// Nur der Zustand im Modell und die Daten des aktuellen Bereichs (Server ändern vor der Registrierung, Demo beenden).
    func resetApp() {
        for user in deviceState.users {
            unlockKeys.remove(userId: user.id)
        }
        clients = [:]
        accounts?.eraseDevice()
        serverStore.clear()
        accounts = nil
        active = nil
        server = nil
        deviceState = DeviceState()
        requestedKind = nil
        enrollmentCode = nil
        pendingTokens = nil
        pendingEnrollment = nil
        screen = .onboarding
    }

    /// Fragt den Gerätestatus ab (Warten auf Freigabe, Prüfung beim Start).
    func refreshDeviceStatus() async {
        guard let active else { return }
        do {
            let device: DeviceResponse = try await active.client.get("devices/me")
            try accounts?.updateDeviceStatus(device.status)
            deviceState = accounts?.state ?? deviceState
            if device.status == .active, screen == .waitingForApproval {
                screen = .home
            }
        } catch {
            // Ohne Verbindung bleibt der letzte bekannte Stand; Sitzungsende meldet `handleSessionEnd`.
        }
    }

    // MARK: Lebenszyklus

    func didEnterBackground(at date: Date) {
        backgroundSince = date
        // Noch nicht übertragene Fotos und Änderungen in der Hintergrundzeit des Systems senden.
        active?.sync?.runInBackground()
        // Poolgeräte sperren beim Bildschirm-Aus sofort (11, Abschnitt 4.1); persönliche Geräte, wenn „Sofort“ gewählt ist.
        if isPoolDevice || lockPreferences.locksOnBackground { lock() }
    }

    func didBecomeActive(at date: Date) {
        defer { backgroundSince = nil }
        guard let since = backgroundSince, active != nil else { return }
        if lockPreferences.shouldLock(backgroundSince: since, now: date) {
            lock()
        } else if let sync = active?.sync {
            Task { await sync.run() }
        }
    }

    /// Inaktivität im Vordergrund (Poolgerät: 5 Minuten).
    func inactivityTimeoutElapsed() {
        if isPoolDevice { lock() }
    }

    /// Verarbeitet `estrichone://enroll?…` aus der Kamera-App, einer E-Mail oder einem anderen Programm.
    func handle(url: URL) {
        let link: EnrollmentLink
        do throws(EnrollmentLinkError) {
            link = try EnrollmentLink.parse(url.absoluteString, allowInsecureLocal: AppEnvironment.allowsInsecureLocalServers)
        } catch {
            notice = error.message
            return
        }

        guard server == nil else {
            // Ein Serverwechsel darf nie nebenbei über einen Link passieren (10, Abschnitt 2).
            notice = "Diese App ist bereits mit einem Server verbunden. Zum Wechsel unter „Mehr“ › „App zurücksetzen“ alles löschen und neu einrichten."
            return
        }
        pendingEnrollment = link
    }

    // MARK: Intern

    private func open(_ server: VerifiedServer) {
        self.server = server
        let accounts = environment.makeAccountService(for: server)
        self.accounts = accounts
        deviceState = accounts?.state ?? DeviceState()
        if accounts == nil {
            notice = "Der Geräteschlüssel konnte nicht angelegt werden."
        }
        if deviceState.registration == nil {
            screen = requestedKind == nil ? .deviceKind : .signIn(userName: nil)
        } else {
            screen = lockedScreen()
        }
    }

    private func lockedScreen() -> Screen {
        guard let registration = deviceState.registration else {
            return requestedKind == nil ? .deviceKind : .signIn(userName: nil)
        }
        if registration.kind == .pool {
            return deviceState.users.isEmpty ? .signIn(userName: nil) : .userPicker
        }
        return deviceState.users.isEmpty ? .signIn(userName: nil) : .appLocked
    }

    /// - Parameter freshSession: nach einer Anmeldung mit Passwort (neue Sitzung, neuer Zugang).
    private func activate(_ account: UnlockedAccount, freshSession: Bool = false) {
        guard let accounts else { return }
        let dataKey = account.dataKey
        // Derselbe Zugang wie vor dem Sperren (er kennt den neuesten Refresh-Token, auch wenn der Abgleich im
        // Hintergrund ihn gerade erneuert). Nur nach einer neuen Anmeldung ein neuer.
        let reusable = freshSession ? nil : clients[account.user.id]
        let client = reusable ?? AuthorizedClient(
            api: accounts.api, session: account.session, keys: accounts.keys,
            persist: { session in _ = try? accounts.store(session, dataKey: dataKey) },
            ended: { [weak self, userId = account.user.id] end in await self?.handleSessionEnd(end, userId: userId) })
        clients[account.user.id] = client
        if deviceState.registration?.kind == .personal, !environment.skipsDeviceOwnerAuthentication,
           !unlockKeys.exists(userId: account.user.id) {
            unlockKeys.save(dataKey, userId: account.user.id, biometric: lockPreferences.biometricUnlock)
        }
        let sync = try? SyncController(account: account, client: client, storage: AppStorage(root: environment.storageRoot))
        if sync == nil {
            notice = "Die Daten auf diesem Gerät konnten nicht geöffnet werden. Bitte die App zurücksetzen oder die IT-Administration informieren."
        }
        let service = sync.map { BauakteService(store: $0.store, files: $0.files, userId: account.user.id) }
        active = ActiveAccount(account: account, client: client, sync: sync, service: service,
                               thumbnails: service.map { ThumbnailLoader(service: $0, client: client) })
        deviceState = accounts.state
        sync?.start()
        screen = deviceState.registration?.status == .pending ? .waitingForApproval : .home
        Task { await refreshDeviceStatus() }
    }

    private func handleSessionEnd(_ end: SessionEnd, userId: UUID) {
        clients[userId] = nil
        // Endet die Sitzung einer anderen (gesperrten) Person auf einem Poolgerät, bleibt die aktuelle angemeldet.
        if let active, active.user.id != userId { return }
        // Poolgerät gesperrt: Die Benutzerauswahl bleibt; die Person merkt das Ende beim nächsten Entsperren.
        if active == nil, isPoolDevice { return }
        let userName = active?.user.userName ?? deviceState.users.first(where: { $0.id == userId })?.userName
        active?.sync?.stop()
        active = nil
        if end.requiresPassword {
            // Nur hier ist das Passwort nötig – mit Grund. Lokale Daten (auch noch nicht übertragene) bleiben im Tresor
            // und werden nach der Anmeldung gesendet; Face ID gilt danach weiter.
            notice = end.message + " Ihre Daten auf diesem Gerät bleiben erhalten."
            screen = .signIn(userName: userName)
            return
        }
        switch end {
        case .deviceRevoked:
            // Das Gerät wurde im Büro entfernt: Registrierung vergessen, verschlüsselte Daten behalten. Nach der
            // Neueinrichtung (gleicher Server, gleiche Person) wird noch nicht Übertragenes gesendet.
            accounts?.forgetRegistration()
            deviceState = accounts?.state ?? DeviceState()
            screen = .blocked(end)
        default:
            screen = .blocked(end)
        }
    }

    func leaveBlockedScreen() {
        if case .blocked(.deviceRevoked) = screen {
            startReEnrollment()
        } else {
            screen = lockedScreen()
        }
    }

    /// Schlüsselableitung (PBKDF2) und Dateizugriffe laufen nicht auf dem Hauptthread.
    private static func offMain<T: Sendable>(_ work: @escaping @Sendable () throws(AccountError) -> T) async throws(AccountError) -> T {
        let result = await Task.detached(priority: .userInitiated) { () -> Result<T, AccountError> in
            do throws(AccountError) {
                return .success(try work())
            } catch {
                return .failure(error)
            }
        }.value
        return try result.get()
    }

    private func requireAccounts() throws(AccountError) -> AccountService {
        guard let accounts else { throw .notRegistered }
        return accounts
    }
}
