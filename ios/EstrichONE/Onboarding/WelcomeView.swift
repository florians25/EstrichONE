import EstrichCore
import SwiftUI

/// Schritt 1: Begrüßung mit den beiden Wegen zum Server.
struct WelcomeView: View {
    @Binding var path: [OnboardingStep]
    @Environment(AppModel.self) private var model
    @State private var isScanning = false
    @State private var cameraDenied = false

    var body: some View {
        VStack(spacing: 0) {
            Spacer(minLength: 24)

            VStack(alignment: .leading, spacing: Space.l) {
                BrandMark()
                    .frame(width: 72, height: 72)
                    .accessibilityHidden(true)

                Text("Willkommen bei EstrichONE")
                    .font(.largeTitle.weight(.bold))
                    .accessibilityIdentifier("welcomeTitle")
                    .foregroundStyle(.primary)
                    .accessibilityAddTraits(.isHeader)

                Text("Diese App arbeitet mit dem EstrichONE-Server Ihres Betriebs. Die Adresse oder einen Einrichtungscode erhalten Sie von Ihrem Administrator.")
                    .font(.body)
                    .foregroundStyle(Theme.textSecondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            .frame(maxWidth: .infinity, alignment: .leading)

            Spacer(minLength: 24)

            // Primäraktionen im unteren Drittel, gut mit dem Daumen erreichbar.
            VStack(spacing: 12) {
                if cameraDenied {
                    StatusMessage(kind: .error, text: "Die App darf die Kamera nicht verwenden. Erlauben Sie den Zugriff in den Einstellungen oder geben Sie die Adresse ein.")
                }

                if QRScannerView.isSupported {
                    Button {
                        Task { await startScanning() }
                    } label: {
                        ActionLabel("QR-Code scannen", systemImage: "qrcode.viewfinder")
                    }
                    .primaryActionStyle()

                    Button {
                        path.append(.address(nil))
                    } label: {
                        ActionLabel("Adresse eingeben", systemImage: "keyboard")
                    }
                    .secondaryActionStyle()
                    .accessibilityIdentifier("enterAddressButton")
                } else {
                    Button {
                        path.append(.address(nil))
                    } label: {
                        ActionLabel("Adresse eingeben", systemImage: "keyboard")
                    }
                    .primaryActionStyle()
                    .accessibilityIdentifier("enterAddressButton")
                }

                // Ohne eigenen Server ausprobieren (auch für die App-Prüfung): Beispieldaten, nichts wird gesendet.
                Button {
                    model.requestDemo(true)
                } label: {
                    Label("Demo ausprobieren – ohne Server", systemImage: "play.rectangle")
                }
                .estrichButton(.tertiary, size: .regular, fullWidth: true)
                .accessibilityIdentifier("tryDemoButton")
            }
        }
        .padding(.horizontal, 24)
        .padding(.bottom, 16)
        .frame(maxWidth: 560)
        .frame(maxWidth: .infinity)
        .sheet(isPresented: $isScanning) {
            EnrollmentScannerSheet { link in
                isScanning = false
                path.append(.address(link))
            }
        }
        .toolbar(.hidden, for: .navigationBar)
    }

    private func startScanning() async {
        if await QRScannerView.requestCameraAccess() {
            cameraDenied = false
            isScanning = true
        } else {
            cameraDenied = true
        }
    }
}

/// Bildmarke des Logos (branding/logo-mark*.svg) als Vektor aus dem Asset-Katalog; passt sich dem Dunkelmodus an.
/// `onDark` für Navy-Flächen (z. B. Kopf der Startseite), unabhängig vom Erscheinungsbild.
struct BrandMark: View {
    var onDark = false

    var body: some View {
        Image(onDark ? "BrandMarkOnDark" : "BrandMark")
            .resizable()
            .scaledToFit()
            .accessibilityLabel("EstrichONE")
    }
}
