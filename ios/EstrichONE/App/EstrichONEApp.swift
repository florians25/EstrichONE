import EstrichCore
import SwiftUI

@main
struct EstrichONEApp: App {
    @State private var model = AppModel(
        store: AppEnvironment.makeServerStore(),
        checker: ServerConnectionChecker(transport: AppEnvironment.makeTransport(), appVersion: AppEnvironment.appVersion))

    var body: some Scene {
        WindowGroup {
            RootView()
                .environment(model)
                .onOpenURL { model.handle(url: $0) }
        }
    }
}

struct RootView: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        @Bindable var model = model

        Group {
            if let server = model.server {
                ConnectedView(server: server)
            } else {
                OnboardingView()
            }
        }
        .alert("Hinweis", isPresented: $model.hasNotice) {
            Button("OK", role: .cancel) {}
        } message: {
            Text(model.notice ?? "")
        }
    }
}
