import EstrichCore
import SwiftUI

/// Schritt im Einrichtungsablauf (docs/reengineering/10-server-onboarding.md).
enum OnboardingStep: Hashable {
    /// Adresse eingeben oder – bei einem Einrichtungscode – direkt prüfen.
    case address(EnrollmentLink?)
    /// Geprüften Server anzeigen und bestätigen lassen.
    case confirm(VerifiedServer, EnrollmentLink?)
}

struct OnboardingView: View {
    @Environment(AppModel.self) private var model
    @State private var path: [OnboardingStep] = []

    var body: some View {
        NavigationStack(path: $path) {
            WelcomeView(path: $path)
                .navigationDestination(for: OnboardingStep.self) { step in
                    switch step {
                    case let .address(link):
                        ServerEntryView(enrollment: link, path: $path)
                    case let .confirm(server, link):
                        ServerConfirmView(server: server, enrollment: link)
                    }
                }
        }
        .onChange(of: model.pendingEnrollment, initial: true) { _, link in
            guard let link else { return }
            model.pendingEnrollment = nil
            path = [.address(link)]
        }
    }
}
