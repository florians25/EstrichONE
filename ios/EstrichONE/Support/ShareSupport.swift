import MessageUI
import SwiftUI
import UIKit

/// Teilen-Dialog des Systems für eine Datei (AirDrop, Nachrichten, Mail, Drucken, „In Dateien sichern“).
struct ActivitySheet: UIViewControllerRepresentable {
    let items: [Any]

    func makeUIViewController(context: Context) -> UIActivityViewController {
        UIActivityViewController(activityItems: items, applicationActivities: nil)
    }

    func updateUIViewController(_ controller: UIActivityViewController, context: Context) {}
}

/// E-Mail mit Anhang über das Mail-Programm des Geräts. Der Benutzer sieht die Nachricht und sendet sie selbst.
struct MailSheet: UIViewControllerRepresentable {
    struct Draft: Identifiable {
        let id = UUID()
        var recipients: [String]
        var subject: String
        var body: String
        var attachment: Data
        var attachmentName: String
        var mimeType: String
    }

    /// Ob auf dem Gerät ein Mail-Konto eingerichtet ist.
    static var isAvailable: Bool { MFMailComposeViewController.canSendMail() }

    let draft: Draft
    let onDone: @MainActor () -> Void

    func makeUIViewController(context: Context) -> MFMailComposeViewController {
        let controller = MFMailComposeViewController()
        controller.mailComposeDelegate = context.coordinator
        controller.setToRecipients(draft.recipients)
        controller.setSubject(draft.subject)
        controller.setMessageBody(draft.body, isHTML: false)
        controller.addAttachmentData(draft.attachment, mimeType: draft.mimeType, fileName: draft.attachmentName)
        return controller
    }

    func updateUIViewController(_ controller: MFMailComposeViewController, context: Context) {}

    func makeCoordinator() -> Coordinator { Coordinator(onDone: onDone) }

    final class Coordinator: NSObject, MFMailComposeViewControllerDelegate {
        private let onDone: @MainActor () -> Void

        init(onDone: @escaping @MainActor () -> Void) {
            self.onDone = onDone
        }

        nonisolated func mailComposeController(_ controller: MFMailComposeViewController, didFinishWith result: MFMailComposeResult,
                                               error: (any Error)?) {
            let done = onDone
            Task { @MainActor in done() }
        }
    }
}
