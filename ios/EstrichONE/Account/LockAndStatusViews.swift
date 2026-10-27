import EstrichCore
import SwiftUI

/// Persönliches Gerät gesperrt: Face ID / Touch ID / Gerätecode (11, Abschnitt 3).
struct AppLockView: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        VStack(spacing: 24) {
            Spacer()
            BrandMark().frame(width: 72, height: 72).accessibilityHidden(true)
            Text("EstrichONE ist gesperrt").font(.title2.weight(.semibold))
            Spacer()
            Button {
                Task { await model.unlockPersonal() }
            } label: {
                ActionLabel("Entsperren", systemImage: "faceid")
            }
            .primaryActionStyle()
            .accessibilityIdentifier("unlockButton")
        }
        .padding(24)
        .frame(maxWidth: 560)
        .frame(maxWidth: .infinity)
        .task { await model.unlockPersonal() }
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
                .font(.title2.weight(.semibold))
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
        }
        .padding(24)
        .frame(maxWidth: 560)
        .frame(maxWidth: .infinity)
    }
}
