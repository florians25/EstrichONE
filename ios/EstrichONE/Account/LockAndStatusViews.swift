import EstrichCore
import SwiftUI

/// Persönliches Gerät gesperrt: Face ID / Touch ID / Gerätecode (11, Abschnitt 3). Kein Passwort – die Sitzung bleibt
/// bestehen, solange die App innerhalb der Frist des Servers genutzt wird.
struct AppLockView: View {
    /// Screenshot-Modus: nur zeigen, nicht automatisch entsperren.
    var preview = false
    @Environment(AppModel.self) private var model
    @Environment(\.shotScene) private var shotScene

    private var kind: BiometryKind {
        guard model.lockPreferences.biometricUnlock else { return .none }
        // Simulator ohne Face ID: für die Aufnahmen wie auf einem iPhone mit Face ID.
        return shotScene != nil ? .faceID : BiometryKind.current
    }

    var body: some View {
        VStack(spacing: 24) {
            Spacer()
            BrandMark().frame(width: 72, height: 72).accessibilityHidden(true)
            VStack(spacing: 6) {
                Text("EstrichONE ist gesperrt").font(Typo.title)
                if let user = model.deviceState.users.first {
                    Text(user.displayName)
                        .font(Typo.subhead)
                        .foregroundStyle(Theme.textSecondary)
                }
            }
            Spacer()
            Image(systemName: kind.symbol)
                .font(.system(size: 56, weight: .light))
                .foregroundStyle(Theme.accent)
                .accessibilityHidden(true)
            Text(kind == .none
                 ? "Mit dem Gerätecode entsperren."
                 : "Mit \(kind.name) entsperren – oder mit dem Gerätecode, falls \(kind.name) nicht klappt.")
                .font(.callout)
                .foregroundStyle(Theme.textSecondary)
                .multilineTextAlignment(.center)
                .fixedSize(horizontal: false, vertical: true)
            if let message = model.lockMessage {
                StatusMessage(kind: .error, text: message)
            }
            Spacer()
            Button {
                Task { await model.unlockPersonal() }
            } label: {
                ActionLabel(kind == .none ? "Entsperren" : "Mit \(kind.name) entsperren", systemImage: kind.symbol)
            }
            .primaryActionStyle()
            .disabled(model.isUnlocking)
            .accessibilityIdentifier("unlockButton")
            Text("Kein Passwort nötig: Sie bleiben angemeldet, solange Sie EstrichONE regelmäßig nutzen.")
                .font(.footnote)
                .foregroundStyle(Theme.textSecondary)
                .multilineTextAlignment(.center)
            ResetAppButton()
        }
        .padding(24)
        .frame(maxWidth: 560)
        .frame(maxWidth: .infinity)
        .task {
            if !preview { await model.unlockPersonal() }
        }
    }
}

/// Gerät registriert, aber noch nicht freigegeben (10, Schritt 5).
struct WaitingForApprovalView: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        VStack(spacing: 20) {
            Spacer()
            Image(systemName: "hourglass")
                .font(.system(size: 48))
                .foregroundStyle(Theme.accent)
                .accessibilityHidden(true)
            Text("Wartet auf Freigabe")
                .font(Typo.title)
                .accessibilityIdentifier("waitingTitle")
            Text("Ihre Administration wurde benachrichtigt. Sie können die App nutzen, sobald das Gerät freigegeben ist. Die App prüft das automatisch.")
                .font(.body)
                .foregroundStyle(Theme.textSecondary)
                .multilineTextAlignment(.center)
                .fixedSize(horizontal: false, vertical: true)
            if let registration = model.deviceRegistration {
                Text("Gerät: \(registration.name)").font(.callout).foregroundStyle(Theme.textSecondary)
            }
            Spacer()
            Button(role: .destructive) {
                Task { await model.signOut() }
            } label: {
                ActionLabel("Abmelden")
            }
            .secondaryActionStyle()
            ResetAppButton()
        }
        .padding(24)
        .frame(maxWidth: 560)
        .frame(maxWidth: .infinity)
        .task {
            while !Task.isCancelled {
                try? await Task.sleep(for: .seconds(10))
                await model.refreshDeviceStatus()
            }
        }
    }
}

/// Der Server hat Gerät oder Person gesperrt bzw. das Gerät entfernt (11, Abschnitt 5).
struct BlockedView: View {
    let end: SessionEnd
    @Environment(AppModel.self) private var model

    var body: some View {
        VStack(spacing: 20) {
            Spacer()
            Image(systemName: end == .deviceRevoked ? "iphone.slash" : "lock.fill")
                .font(.system(size: 48))
                .foregroundStyle(Theme.danger)
                .accessibilityHidden(true)
            StatusMessage(kind: .error, text: end.message)
                .accessibilityIdentifier("blockedMessage")
            if end == .deviceRevoked {
                Text("Mit einem neuen Einrichtungscode (QR-Code oder Eingabe) aus dem IT Center lässt sich das Gerät neu einrichten. Noch nicht übertragene Daten bleiben erhalten und werden nach der Anmeldung derselben Person gesendet.")
                    .font(.subheadline)
                    .foregroundStyle(Theme.textSecondary)
                    .multilineTextAlignment(.center)
            }
            Spacer()
            Button {
                model.leaveBlockedScreen()
            } label: {
                ActionLabel(end == .deviceRevoked ? "Neu einrichten" : "Zurück")
            }
            .primaryActionStyle()
            ResetAppButton()
        }
        .padding(24)
        .frame(maxWidth: 560)
        .frame(maxWidth: .infinity)
    }
}
