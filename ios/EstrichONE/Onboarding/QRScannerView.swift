import AVFoundation
import EstrichCore
import SwiftUI
import VisionKit

/// Kamera-Ansicht, die QR-Codes erkennt (VisionKit). Auf Geräten ohne Unterstützung,
/// etwa im Simulator, bietet die App nur die Adresseingabe an.
struct QRScannerView: UIViewControllerRepresentable {
    /// Wird für jeden neu erkannten Inhalt aufgerufen. `true` beendet das Scannen.
    let onScan: @MainActor (String) -> Bool

    @MainActor
    static var isSupported: Bool {
        DataScannerViewController.isSupported
    }

    /// Fragt beim ersten Mal nach der Kameraberechtigung.
    static func requestCameraAccess() async -> Bool {
        switch AVCaptureDevice.authorizationStatus(for: .video) {
        case .authorized:
            true
        case .notDetermined:
            await AVCaptureDevice.requestAccess(for: .video)
        default:
            false
        }
    }

    func makeUIViewController(context: Context) -> DataScannerViewController {
        let scanner = DataScannerViewController(
            recognizedDataTypes: [.barcode(symbologies: [.qr])],
            qualityLevel: .balanced,
            recognizesMultipleItems: false,
            isHighFrameRateTrackingEnabled: false,
            isHighlightingEnabled: true)
        scanner.delegate = context.coordinator
        return scanner
    }

    func updateUIViewController(_ scanner: DataScannerViewController, context: Context) {
        if !scanner.isScanning && !context.coordinator.finished {
            try? scanner.startScanning()
        }
    }

    static func dismantleUIViewController(_ scanner: DataScannerViewController, coordinator: Coordinator) {
        scanner.stopScanning()
    }

    func makeCoordinator() -> Coordinator {
        Coordinator(onScan: onScan)
    }

    @MainActor
    final class Coordinator: NSObject, DataScannerViewControllerDelegate {
        private let onScan: @MainActor (String) -> Bool
        private var rejected: Set<String> = []
        private(set) var finished = false

        init(onScan: @escaping @MainActor (String) -> Bool) {
            self.onScan = onScan
        }

        func dataScanner(_ dataScanner: DataScannerViewController, didAdd addedItems: [RecognizedItem], allItems: [RecognizedItem]) {
            guard !finished else { return }
            for item in addedItems {
                guard case let .barcode(barcode) = item, let payload = barcode.payloadStringValue,
                      !rejected.contains(payload)
                else { continue }

                if onScan(payload) {
                    finished = true
                    dataScanner.stopScanning()
                    return
                }
                rejected.insert(payload)
            }
        }
    }
}

/// Blatt zum Scannen des Einrichtungscodes aus dem IT Center (10, Schritt 2A).
struct EnrollmentScannerSheet: View {
    let onEnrollment: @MainActor (EnrollmentLink) -> Void

    @Environment(\.dismiss) private var dismiss
    @State private var errorMessage: String?

    var body: some View {
        NavigationStack {
            QRScannerView { payload in
                do throws(EnrollmentLinkError) {
                    let link = try EnrollmentLink.parse(payload, allowInsecureLocal: AppEnvironment.allowsInsecureLocalServers)
                    onEnrollment(link)
                    return true
                } catch {
                    errorMessage = error.message
                    return false
                }
            }
            .ignoresSafeArea()
            .safeAreaInset(edge: .bottom) {
                VStack(spacing: 8) {
                    if let errorMessage {
                        StatusMessage(kind: .error, text: errorMessage)
                    } else {
                        Text("Richten Sie die Kamera auf den Einrichtungscode aus dem IT Center.")
                            .font(.callout.weight(.medium))
                            .multilineTextAlignment(.center)
                            .padding(12)
                            .frame(maxWidth: .infinity)
                            .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 10, style: .continuous))
                    }
                }
                .padding(16)
            }
            .navigationTitle("QR-Code scannen")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Abbrechen") { dismiss() }
                }
            }
            .sensoryFeedback(.warning, trigger: errorMessage) { _, new in new != nil }
        }
    }
}
