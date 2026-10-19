import EstrichCore
import SwiftUI

/// Schritt 1: Begrüßung mit den beiden Wegen zum Server.
struct WelcomeView: View {
    @Binding var path: [OnboardingStep]
    @State private var isScanning = false
    @State private var cameraDenied = false

    var body: some View {
        VStack(spacing: 0) {
            Spacer(minLength: 24)

            VStack(alignment: .leading, spacing: Theme.spacing) {
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

/// Bildmarke aus dem App-Symbol: drei Schichten wie ein Estrichaufbau, zusammen ein „E“.
struct BrandMark: View {
    var body: some View {
        GeometryReader { geometry in
            let size = min(geometry.size.width, geometry.size.height)
            let unit = size / 1024
            ZStack(alignment: .topLeading) {
                RoundedRectangle(cornerRadius: 224 * unit, style: .continuous)
                    .fill(Theme.navy)
                layer(x: 300, y: 212, width: 132, height: 600, color: .white, unit: unit)
                layer(x: 300, y: 212, width: 424, height: 132, color: .white, unit: unit)
                layer(x: 300, y: 446, width: 330, height: 132, color: .white, unit: unit)
                layer(x: 300, y: 680, width: 424, height: 132, color: Theme.brandBlue, unit: unit)
            }
            .frame(width: size, height: size)
        }
    }

    private func layer(x: CGFloat, y: CGFloat, width: CGFloat, height: CGFloat, color: Color, unit: CGFloat) -> some View {
        RoundedRectangle(cornerRadius: 18 * unit, style: .continuous)
            .fill(color)
            .frame(width: width * unit, height: height * unit)
            .offset(x: x * unit, y: y * unit)
    }
}
