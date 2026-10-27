import EstrichCore
import SwiftUI

/// Schritt 2: Serveradresse eingeben (oder aus dem Einrichtungscode übernehmen) und prüfen.
struct ServerEntryView: View {
    let enrollment: EnrollmentLink?
    @Binding var path: [OnboardingStep]

    @Environment(AppModel.self) private var model
    @State private var input = ""
    @State private var errorMessage: String?
    @State private var isChecking = false
    @State private var isScanning = false
    @State private var appliedEnrollment = false
    @FocusState private var fieldFocused: Bool

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: Space.l) {
                Text("Mit welchem Server soll sich die App verbinden?")
                    .font(.title2.weight(.semibold))
                    .fixedSize(horizontal: false, vertical: true)
                    .accessibilityAddTraits(.isHeader)

                VStack(alignment: .leading, spacing: 6) {
                    Text("Serveradresse")
                        .font(.subheadline.weight(.medium))
                        .foregroundStyle(Theme.textSecondary)

                    TextField("estrichone.meinefirma.de", text: $input)
                        .keyboardType(.URL)
                        .textContentType(.URL)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()
                        .submitLabel(.go)
                        .focused($fieldFocused)
                        .onSubmit { Task { await check() } }
                        .padding(.horizontal, 14)
                        .frame(minHeight: 52)
                        .background(Color(uiColor: .secondarySystemBackground), in: RoundedRectangle(cornerRadius: 10, style: .continuous))
                        .overlay {
                            RoundedRectangle(cornerRadius: 10, style: .continuous)
                                .strokeBorder(errorMessage == nil ? Theme.separator : Theme.danger, lineWidth: 1)
                        }
                        .disabled(isChecking)
                        .accessibilityLabel("Serveradresse")
                        .accessibilityIdentifier("serverAddressField")

                    Text("Die Verbindung ist immer verschlüsselt. „https://“ wird automatisch ergänzt.")
                        .font(.footnote)
                        .foregroundStyle(Theme.textSecondary)
                        .fixedSize(horizontal: false, vertical: true)
                }

                if let errorMessage {
                    StatusMessage(kind: .error, text: errorMessage)
                        .accessibilityIdentifier("serverError")
                }

                Button {
                    Task { await check() }
                } label: {
                    ActionLabel(isChecking ? "Verbindung wird geprüft …" : "Verbindung prüfen",
                                systemImage: "checkmark.shield", isBusy: isChecking)
                }
                .primaryActionStyle()
                .disabled(isChecking || input.trimmingCharacters(in: .whitespaces).isEmpty)
                .accessibilityIdentifier("checkConnectionButton")
            }
            .padding(24)
            .frame(maxWidth: 560)
            .frame(maxWidth: .infinity)
        }
        .scrollDismissesKeyboard(.interactively)
        .navigationTitle("Server")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            if QRScannerView.isSupported {
                ToolbarItem(placement: .topBarTrailing) {
                    Button {
                        Task {
                            if await QRScannerView.requestCameraAccess() {
                                isScanning = true
                            } else {
                                errorMessage = "Die App darf die Kamera nicht verwenden. Erlauben Sie den Zugriff in den Einstellungen."
                            }
                        }
                    } label: {
                        Label("QR-Code scannen", systemImage: "qrcode.viewfinder")
                    }
                }
            }
        }
        .sheet(isPresented: $isScanning) {
            EnrollmentScannerSheet { link in
                isScanning = false
                path = [.address(link)]
            }
        }
        .sensoryFeedback(.error, trigger: errorMessage) { _, new in new != nil }
        .task {
            // Nur beim ersten Erscheinen: Beim Zurückkehren aus der Bestätigung nicht erneut prüfen.
            if let enrollment, !appliedEnrollment {
                appliedEnrollment = true
                input = enrollment.server.baseURL.absoluteString
                await check()
            } else if errorMessage == nil {
                fieldFocused = true
            }
        }
    }

    private func check() async {
        guard !isChecking else { return }
        errorMessage = nil

        let address: ServerAddress
        do throws(ServerAddressError) {
            address = try ServerAddress.parse(input, allowInsecureLocal: AppEnvironment.allowsInsecureLocalServers)
        } catch {
            errorMessage = error.message
            return
        }

        isChecking = true
        defer { isChecking = false }

        switch await model.checker.check(address) {
        case let .success(server):
            fieldFocused = false
            // Der Einrichtungscode gilt nur für den Server, von dem er stammt.
            path.append(.confirm(server, enrollment?.server == address ? enrollment : nil))
        case let .failure(error):
            errorMessage = error.message
        }
    }
}
