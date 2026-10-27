import EstrichCore
import SwiftUI

/// Abschluss von Schritt 2: Der Benutzer sieht, mit welchem Server er sich verbindet, und bestätigt ihn.
/// Ein unbekannter Server wird nie stillschweigend übernommen.
struct ServerConfirmView: View {
    let server: VerifiedServer
    let enrollment: EnrollmentLink?

    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var errorMessage: String?

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: Space.l) {
                StatusMessage(kind: .success, text: "Verbindung geprüft. Der Server ist erreichbar und verwendet ein gültiges Zertifikat.")

                VStack(alignment: .leading, spacing: 4) {
                    Text("Server")
                        .font(.subheadline.weight(.medium))
                        .foregroundStyle(Theme.textSecondary)
                    Text(server.info.serverName)
                        .font(.title2.weight(.semibold))
                        .fixedSize(horizontal: false, vertical: true)
                        .accessibilityIdentifier("confirmServerName")
                }

                ServerDetails(server: server)

                if enrollment?.enrollmentCode != nil {
                    Label("Einrichtungscode erkannt", systemImage: "qrcode")
                        .font(.callout)
                        .foregroundStyle(Theme.textSecondary)
                }

                if let errorMessage {
                    StatusMessage(kind: .error, text: errorMessage)
                }

                VStack(spacing: 12) {
                    Button {
                        do {
                            try model.confirm(server, enrollment: enrollment)
                        } catch {
                            errorMessage = "Der Server konnte nicht gespeichert werden. Bitte erneut versuchen."
                        }
                    } label: {
                        ActionLabel("Diesen Server verwenden", systemImage: "arrow.right")
                    }
                    .primaryActionStyle()
                    .accessibilityIdentifier("useServerButton")

                    Button {
                        dismiss()
                    } label: {
                        ActionLabel("Andere Adresse eingeben")
                    }
                    .secondaryActionStyle()
                }
                .padding(.top, 8)
            }
            .padding(24)
            .frame(maxWidth: 560)
            .frame(maxWidth: .infinity)
        }
        .navigationTitle("Server bestätigen")
        .navigationBarTitleDisplayMode(.inline)
    }
}

/// Adresse, Version und Anmeldemethoden eines Servers als deckende Liste (kein Glas auf Inhalten).
struct ServerDetails: View {
    let server: VerifiedServer

    var body: some View {
        VStack(spacing: 0) {
            row("Adresse", server.address.baseURL.absoluteString)
            Divider()
            row("Serverversion", server.info.serverVersion)
            Divider()
            row("Anmeldung", Self.describe(server.info.authMethods))
        }
        .background(Color(uiColor: .secondarySystemBackground), in: RoundedRectangle(cornerRadius: 10, style: .continuous))
    }

    private func row(_ title: LocalizedStringKey, _ value: String) -> some View {
        LabeledContent {
            Text(value)
                .multilineTextAlignment(.trailing)
                .textSelection(.enabled)
        } label: {
            Text(title)
                .foregroundStyle(Theme.textSecondary)
        }
        .font(.callout)
        .padding(.horizontal, 14)
        .padding(.vertical, 12)
    }

    static func describe(_ methods: [String]) -> String {
        let names = methods.map { method in
            switch method {
            case "local": "Benutzername und Passwort"
            case "entra": "Microsoft-Konto"
            default: method
            }
        }
        return names.isEmpty ? "–" : names.joined(separator: ", ")
    }
}
