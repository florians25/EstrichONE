import EstrichCore
import SwiftUI

@main
struct EstrichONEApp: App {
    @State private var model = AppModel(environment: .live())
    @Environment(\.scenePhase) private var scenePhase

    var body: some Scene {
        WindowGroup {
            RootView()
                .environment(model)
                .onOpenURL { model.handle(url: $0) }
        }
        .onChange(of: scenePhase) { _, phase in
            switch phase {
            case .background: model.didEnterBackground(at: .now)
            case .active: model.didBecomeActive(at: .now)
            default: break
            }
        }
    }
}

struct RootView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.scenePhase) private var scenePhase

    var body: some View {
        @Bindable var model = model

        content
            .animation(.default, value: model.screen)
            // Im App-Umschalter keine Inhalte zeigen (11, Abschnitt 4.3).
            .overlay {
                if scenePhase != .active {
                    PrivacyCover()
                }
            }
            .background {
                if model.isPoolDevice, model.active != nil {
                    InactivityMonitor(timeout: model.poolInactivityTimeout) { model.inactivityTimeoutElapsed() }
                }
            }
            .alert("Hinweis", isPresented: $model.hasNotice) {
                Button("OK", role: .cancel) {}
            } message: {
                Text(model.notice ?? "")
            }
    }

    @ViewBuilder
    private var content: some View {
        switch model.screen {
        case .onboarding:
            OnboardingView()
        case .deviceKind:
            DeviceKindView()
        case let .signIn(userName):
            SignInView(prefilledUserName: userName)
        case .changePassword:
            ChangePasswordView()
        case .choosePIN:
            ChoosePINView()
        case .userPicker:
            UserPickerView()
        case let .pinEntry(userId):
            PINEntryView(userId: userId)
        case .appLocked:
            AppLockView()
        case .waitingForApproval:
            WaitingForApprovalView()
        case .home:
            MainTabView()
        case let .blocked(end):
            BlockedView(end: end)
        }
    }
}

/// Deckende Fläche mit Bildmarke, solange die App nicht im Vordergrund ist.
struct PrivacyCover: View {
    var body: some View {
        ZStack {
            Color(uiColor: .systemBackground).ignoresSafeArea()
            BrandMark().frame(width: 72, height: 72)
        }
        .accessibilityHidden(true)
    }
}
