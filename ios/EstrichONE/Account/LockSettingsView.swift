import EstrichCore
import SwiftUI

/// Mehr › Sperre und Face ID (persönliches Gerät): wie die App entsperrt wird und wann sie sperrt.
struct LockSettingsView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.shotScene) private var shotScene
    @State private var signedInUntil: Date?

    private var biometry: BiometryKind {
        shotScene != nil ? .faceID : BiometryKind.current
    }

    var body: some View {
        List {
            Section {
                Toggle(isOn: Binding(get: { model.lockPreferences.biometricUnlock }, set: { model.setBiometricUnlock($0) })) {
                    Label("Mit \(biometry == .none ? "Face ID" : biometry.name) entsperren", systemImage: biometry == .none ? "faceid" : biometry.symbol)
                }
                .disabled(biometry == .none)
                .accessibilityIdentifier("biometricUnlockToggle")
                Picker(selection: Binding(get: { model.lockPreferences.delay }, set: { model.setLockDelay($0) })) {
                    ForEach(LockDelay.allCases) { delay in
                        Text(delay.title).tag(delay)
                    }
                } label: {
                    Label("Sperren", systemImage: "lock")
                }
                .accessibilityIdentifier("lockDelayPicker")
            } header: {
                Text("Entsperren")
            } footer: {
                Text(footer)
            }

            Section {
                LabeledContent("Anmeldung") {
                    Text("bleibt bestehen").foregroundStyle(Theme.textSecondary)
                }
                if let signedInUntil {
                    LabeledContent("Ohne Nutzung gültig bis", value: signedInUntil.formatted(date: .abbreviated, time: .omitted))
                }
            } header: {
                Text("Sitzung")
            } footer: {
                Text("Jede Nutzung verlängert die Anmeldung (Frist legt Ihr Betrieb fest, Standard 90 Tage). Das Passwort ist nur nötig nach dem Abmelden, nach einer Passwortänderung, wenn das Gerät im IT Center gesperrt oder entfernt wurde oder die App so lange nicht genutzt wurde.")
            }
        }
        .listStyle(.insetGrouped)
        .navigationTitle("Sperre und \(biometry == .none ? "Gerätecode" : biometry.name)")
        .navigationBarTitleDisplayMode(.inline)
        .task {
            signedInUntil = await model.active?.client.session.refreshTokenExpiresAt
        }
    }

    private var footer: String {
        if biometry == .none {
            return "Auf diesem Gerät ist weder Face ID noch Touch ID eingerichtet – entsperrt wird mit dem Gerätecode."
        }
        let name = biometry.name
        let mode = model.lockPreferences.biometricUnlock
            ? "Entsperren mit \(name), bei Bedarf mit dem Gerätecode."
            : "Entsperren nur mit dem Gerätecode."
        return mode + " Werden in den iOS-Einstellungen Gesichter oder Finger geändert, verlangt EstrichONE aus Sicherheitsgründen einmal den Gerätecode; danach gilt wieder \(name)."
    }
}
