import Foundation
#if canImport(CryptoKit)
import CryptoKit
#else
import Crypto
#endif

/// Angaben zum Gerät für die Registrierung.
public struct DeviceDescription: Sendable {
    public let name: String
    public let model: String?
    public let osVersion: String?
    public let appVersion: String?

    public init(name: String, model: String?, osVersion: String?, appVersion: String?) {
        self.name = name
        self.model = model
        self.osVersion = osVersion
        self.appVersion = appVersion
    }
}

/// Entsperrter Benutzer: Sitzung plus Datenschlüssel seines Tresors.
public struct UnlockedAccount: Sendable {
    public let user: UserSummary
    public let session: StoredSession
    public let dataKey: SymmetricKey
}

public enum AccountError: Error, Equatable, Sendable {
    case api(APIError)
    case vault(VaultError)
    case pin(PINPolicy.Violation)
    /// 5 falsche PINs: Der Tresor bleibt gesperrt, bis sich die Person online mit Passwort anmeldet.
    case pinLocked
    case notRegistered
    case storage

    public var message: String {
        switch self {
        case let .api(error): error.message
        case .vault(.wrongPIN): "Die PIN ist falsch."
        case .vault: "Die gespeicherten Daten auf diesem Gerät sind nicht lesbar. Bitte erneut mit Passwort anmelden."
        case let .pin(violation): violation.message
        case .pinLocked: "Zu viele falsche PINs. Bitte mit Passwort anmelden."
        case .notRegistered: "Dieses Gerät ist noch nicht eingerichtet."
        case .storage: "Die Daten konnten auf diesem Gerät nicht gespeichert werden."
        }
    }
}

/// Anmeldung, Geräteregistrierung und Datentresore (docs/reengineering/10, Schritt 3–5; 11).
public final class AccountService: @unchecked Sendable {
    public let api: APIClient
    public let storage: AppStorage
    public let keys: DeviceKeys
    private let secrets: SecretStore
    private let now: @Sendable () -> Date
    private let pinIterations: Int
    private let lock = NSLock()

    public init(api: APIClient, storage: AppStorage, keys: DeviceKeys, secrets: SecretStore,
                pinIterations: Int = VaultEnvelope.pinIterations, now: @escaping @Sendable () -> Date = Date.init) {
        self.api = api
        self.storage = storage
        self.keys = keys
        self.secrets = secrets
        self.pinIterations = pinIterations
        self.now = now
    }

    public var state: DeviceState { lock.withLock { storage.loadState() } }

    // MARK: Anmeldung

    public func signIn(login: String, password: String, clientName: String) async throws(AccountError) -> TokenResponse {
        do {
            return try await api.post("auth/login", body: LoginRequest(
                login: login.trimmingCharacters(in: .whitespacesAndNewlines), password: password, client: "ios", clientName: clientName))
        } catch {
            throw .api(error)
        }
    }

    public func changePassword(tokens: TokenResponse, current: String, new: String) async throws(AccountError) {
        do {
            let _: NoContent = try await api.post("auth/change-password",
                                                  body: ChangePasswordRequest(currentPassword: current, newPassword: new),
                                                  accessToken: tokens.accessToken)
        } catch {
            throw .api(error)
        }
    }

    /// Bindet die frische Sitzung an dieses Gerät: Erstregistrierung oder – wenn das Gerät schon registriert ist –
    /// Nachweis per Gerätesignatur (Poolgerät, erneute Anmeldung).
    public func bindDevice(tokens: TokenResponse, kind: DeviceKind, enrollmentCode: String?,
                           description: DeviceDescription) async throws(AccountError) -> DeviceRegistration {
        var state = self.state
        let device: DeviceResponse
        do {
            if let registration = state.registration {
                let signature = try DeviceProof.attach(tokens.accessToken, keys: keys)
                device = try await api.post("devices/attach", body: AttachDeviceRequest(deviceId: registration.deviceId, signature: signature),
                                            accessToken: tokens.accessToken)
            } else {
                device = try await api.post("devices/register", body: RegisterDeviceRequest(
                    name: description.name, platform: "ios", kind: kind, publicKey: keys.signingPublicKey.base64EncodedString(),
                    model: description.model, osVersion: description.osVersion, appVersion: description.appVersion,
                    enrollmentCode: enrollmentCode), accessToken: tokens.accessToken)
            }
        } catch let error as APIError {
            throw .api(error)
        } catch {
            throw .storage
        }

        let registration = DeviceRegistration(deviceId: device.id, kind: device.kind, name: device.name, status: device.status)
        state.registration = registration
        try save(state)
        return registration
    }

    /// Legt den Tresor des Benutzers an (bzw. neu an) und speichert seine Sitzung darin.
    /// Poolgeräte verlangen eine PIN, persönliche Geräte nicht.
    public func createVault(for tokens: TokenResponse, pin: String?) throws(AccountError) -> UnlockedAccount {
        guard let registration = state.registration else { throw .notRegistered }
        if registration.kind == .pool {
            guard let pin else { throw .vault(.pinRequired) }
            do throws(PINPolicy.Violation) {
                try PINPolicy.validate(pin)
            } catch {
                throw .pin(error)
            }
        }

        let session = StoredSession(tokens)
        do {
            let directory = storage.userDirectory(tokens.user.id)
            // Erneute Anmeldung derselben Person (z. B. nach Ablauf der Sitzung): Lässt sich der vorhandene Tresor
            // öffnen, bleiben Datenschlüssel und lokale Daten erhalten – auch noch nicht übertragene Änderungen.
            if let envelopeData = try? Data(contentsOf: directory.appending(path: "vault.json")),
               let envelope = try? JSONCoding.decoder.decode(VaultEnvelope.self, from: envelopeData),
               let existingKey = try? UserVault.unlock(envelope, pin: registration.kind == .pool ? pin : nil, keys: keys) {
                try storage.write(UserVault.encrypt(JSONCoding.encoder.encode(session), with: existingKey),
                                  to: directory.appending(path: "session.sealed"))
                var state = self.state
                state.upsert(tokens.user, at: now())
                try save(state)
                return UnlockedAccount(user: tokens.user, session: session, dataKey: existingKey)
            }
            try storage.eraseUser(tokens.user.id)
            let (envelope, dataKey) = try UserVault.create(pin: registration.kind == .pool ? pin : nil, keys: keys, iterations: pinIterations)
            try storage.write(JSONCoding.encoder.encode(envelope), to: directory.appending(path: "vault.json"))
            try storage.write(UserVault.encrypt(JSONCoding.encoder.encode(session), with: dataKey), to: directory.appending(path: "session.sealed"))

            var state = self.state
            state.upsert(tokens.user, at: now())
            try save(state)
            return UnlockedAccount(user: tokens.user, session: session, dataKey: dataKey)
        } catch let error as AccountError {
            throw error
        } catch {
            throw .storage
        }
    }

    /// Öffnet den Tresor eines Benutzers. Zählt falsche PINs; nach 5 Fehlversuchen nur noch Passwort-Anmeldung.
    public func unlock(userId: UUID, pin: String?) throws(AccountError) -> UnlockedAccount {
        var state = self.state
        guard let index = state.users.firstIndex(where: { $0.id == userId }) else { throw .vault(.unreadable) }
        if state.users[index].requiresPasswordLogin { throw .pinLocked }

        let directory = storage.userDirectory(userId)
        let envelope: VaultEnvelope
        let sealedSession: Data
        do {
            envelope = try JSONCoding.decoder.decode(VaultEnvelope.self, from: Data(contentsOf: directory.appending(path: "vault.json")))
            sealedSession = try Data(contentsOf: directory.appending(path: "session.sealed"))
        } catch {
            throw .vault(.unreadable)
        }

        let dataKey: SymmetricKey
        do throws(VaultError) {
            dataKey = try UserVault.unlock(envelope, pin: pin, keys: keys)
        } catch .wrongPIN {
            state.users[index].failedPINAttempts += 1
            let locked = state.users[index].failedPINAttempts >= PINPolicy.maxAttempts
            state.users[index].requiresPasswordLogin = locked
            try save(state)
            throw locked ? .pinLocked : .vault(.wrongPIN)
        } catch {
            throw .vault(error)
        }

        let session: StoredSession
        do {
            session = try JSONCoding.decoder.decode(StoredSession.self, from: UserVault.decrypt(sealedSession, with: dataKey))
        } catch {
            throw .vault(.unreadable)
        }
        state.users[index].failedPINAttempts = 0
        state.users[index].lastUsedAt = now()
        try save(state)
        return UnlockedAccount(user: session.user, session: session, dataKey: dataKey)
    }

    /// Speichert erneuerte Tokens im Tresor.
    public func store(_ session: StoredSession, dataKey: SymmetricKey) throws(AccountError) {
        do {
            try storage.write(UserVault.encrypt(JSONCoding.encoder.encode(session), with: dataKey),
                              to: storage.userDirectory(session.user.id).appending(path: "session.sealed"))
        } catch {
            throw .storage
        }
    }

    public func updateDeviceStatus(_ status: DeviceStatus) throws(AccountError) {
        var state = self.state
        state.registration?.status = status
        try save(state)
    }

    /// Abmelden: Sitzung serverseitig beenden (wenn erreichbar) und Tresor des Benutzers löschen.
    public func signOut(_ account: UnlockedAccount, client: AuthorizedClient?) async {
        if let client {
            let _: NoContent? = try? await client.post("auth/logout")
        }
        var state = self.state
        state.users.removeAll { $0.id == account.user.id }
        try? storage.eraseUser(account.user.id)
        try? save(state)
    }

    /// Das Gerät wurde im Büro entfernt: nur die Registrierung vergessen. Geräteschlüssel und verschlüsselte Tresore
    /// bleiben, damit nach der Neuregistrierung (gleicher Server, gleiche Person) noch nicht übertragene Daten gesendet
    /// werden. Die Sitzungen sind serverseitig ungültig; geöffnet wird erst nach erneuter Anmeldung mit Passwort.
    public func forgetRegistration() {
        var state = self.state
        state.registration = nil
        try? save(state)
    }

    /// Gerät entfernt oder App zurücksetzen: alle Daten und den Geräteschlüssel löschen.
    public func eraseDevice() {
        try? storage.eraseAll()
        try? secrets.remove(DeviceKeyFactory.storageKey)
    }

    private func save(_ state: DeviceState) throws(AccountError) {
        do {
            try lock.withLock { try storage.save(state) }
        } catch {
            throw .storage
        }
    }
}

/// Lädt oder erzeugt die Geräteschlüssel. Auf echten Geräten in der Secure Enclave.
public enum DeviceKeyFactory {
    public static let storageKey = "device.keys.v1"

    public static func loadOrCreate(secrets: SecretStore) throws -> DeviceKeys {
        #if canImport(CryptoKit) && os(iOS)
        if SecureEnclaveDeviceKeys.isAvailable {
            if let stored = try secrets.data(for: storageKey), let keys = try? SecureEnclaveDeviceKeys(serialized: stored) {
                return keys
            }
            let keys = try SecureEnclaveDeviceKeys()
            try secrets.set(keys.serialized, for: storageKey)
            return keys
        }
        #endif
        if let stored = try secrets.data(for: storageKey), let keys = try? SoftwareDeviceKeys(serialized: stored) {
            return keys
        }
        let keys = SoftwareDeviceKeys()
        try secrets.set(keys.serialized, for: storageKey)
        return keys
    }
}
