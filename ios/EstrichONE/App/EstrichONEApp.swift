import EstrichCore
import SwiftUI

@main
struct EstrichONEApp: App {
    @State private var session = AppSession()
    @Environment(\.scenePhase) private var scenePhase

    var body: some Scene {
        WindowGroup {
            RootView()
                .environment(session.model)
                .environment(\.shotScene, session.shotScene)
                .id(session.generation)
                .onOpenURL { session.model.handle(url: $0) }
        }
        .onChange(of: scenePhase) { _, phase in
            switch phase {
            case .background: session.model.didEnterBackground(at: .now)
            case .active: session.model.didBecomeActive(at: .now)
            default: break
            }
        }
    }
}

/// Hält das App-Modell und tauscht es beim Wechsel in den bzw. aus dem Demomodus aus.
@MainActor
@Observable
final class AppSession {
    private(set) var model: AppModel
    private(set) var generation = 0
    #if DEBUG
    /// Screenshot-Modus (Launch-Argument `-EstrichShots`), nur in Debug-Builds.
    let director: ScreenshotDirector?
    #endif

    init() {
        #if DEBUG
        director = ScreenshotDirector.fromLaunchArguments()
        if let director {
            ScreenshotDirector.resetDemoData()
            UserDefaults.standard.set(true, forKey: AppEnvironment.demoFlagKey)
            model = AppModel(environment: .demo())
            connect()
            Task { await model.startDemo() }
            Task { [weak self] in
                await director.run { self?.model }
            }
            return
        }
        #endif
        model = AppModel(environment: .live())
        connect()
    }

    /// Bildschirm des Screenshot-Modus (sonst `nil`).
    var shotScene: String? {
        #if DEBUG
        director?.current
        #else
        nil
        #endif
    }

    private func connect() {
        model.onDemoRequest = { [weak self] start in self?.switchDemo(start) }
    }

    private func switchDemo(_ start: Bool) {
        UserDefaults.standard.set(start, forKey: AppEnvironment.demoFlagKey)
        if !start { model.resetApp() }
        model = AppModel(environment: start ? .demo() : .live())
        connect()
        generation += 1
        if start { Task { await model.startDemo() } }
    }
}

struct RootView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.scenePhase) private var scenePhase
    @Environment(\.shotScene) private var shotScene

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
        // Screenshot-Modus (nur Debug): Einrichtung und Anmeldung zeigen, obwohl die Demo angemeldet ist.
        switch shotScene {
        case "start": OnboardingView()
        case "start-adresse": OnboardingView(initialPath: [.address(nil)])
        case "anmelden": SignInView(prefilledUserName: "m.huber")
        default: screen
        }
    }

    @ViewBuilder
    private var screen: some View {
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
            // Screenshot-Modus: je Bildschirm frisch aufbauen (Blätter und Navigation zurücksetzen).
            MainTabView()
                .id(shotScene ?? "")
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
