import EstrichCore
import SwiftUI
import UIKit
import UserNotifications

/// Pushnachrichten (APNs) und Postfach: Berechtigung, Geräte-Token, Weiterleitung beim Tippen (Deep Link), Abzeichen.
///
/// Ablauf: Die Berechtigung wird erst auf Wunsch angefragt (Postfach oder Mehr › Benachrichtigungen), nie beim ersten
/// Start. Mit Zustimmung registriert sich die App bei APNs; das Token geht an `POST /notifications/devices`
/// (docs/benachrichtigungen.md). Ohne eingerichteten APNs-Schlüssel auf dem Server bleibt alles im Postfach.
/// Push-Inhalte enthalten nur Titel, kurzen Text und Link – keine sensiblen Daten.
@MainActor
@Observable
final class PushCenter {
    static let shared = PushCenter()

    enum Permission: Equatable {
        case unknown, notDetermined, denied, allowed
    }

    private(set) var permission: Permission = .unknown
    /// APNs-Gerätetoken (hex), sobald registriert.
    private(set) var token: String?
    /// Fehler der APNs-Registrierung (z. B. Capability fehlt beim Signieren).
    private(set) var registrationError: String?
    /// Ziel einer angetippten Benachrichtigung, das die Hauptansicht öffnet.
    var pendingRoute: NotificationRoute?
    /// Angetippte Benachrichtigung, die beim Server als gelesen markiert wird.
    var pendingReadId: UUID?
    /// Ungelesene Einträge im Postfach (Glocke, Abzeichen am App-Symbol).
    private(set) var unreadCount = 0

    private init() {}

    /// Debug-Builds (Xcode) nutzen die APNs-Sandbox, TestFlight und App Store die Produktion.
    static var isSandbox: Bool {
        #if DEBUG
        true
        #else
        false
        #endif
    }

    func refreshPermission() {
        UNUserNotificationCenter.current().getNotificationSettings { settings in
            let status = settings.authorizationStatus
            Task { @MainActor in
                PushCenter.shared.apply(status)
            }
        }
    }

    private func apply(_ status: UNAuthorizationStatus) {
        switch status {
        case .notDetermined: permission = .notDetermined
        case .denied: permission = .denied
        default:
            permission = .allowed
            UIApplication.shared.registerForRemoteNotifications()
        }
    }

    /// Fragt die Berechtigung an (Hinweise, Töne, Abzeichen) und registriert bei Zustimmung bei APNs.
    func requestPermission() {
        UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound, .badge]) { _, _ in
            Task { @MainActor in
                PushCenter.shared.refreshPermission()
            }
        }
    }

    func didRegister(_ deviceToken: Data) {
        token = PushDeviceRegistration.hex(deviceToken)
        registrationError = nil
    }

    func didFailToRegister(_ error: any Error) {
        registrationError = error.localizedDescription
    }

    /// Meldet das Token beim Server an (nach Anmeldung und bei jedem neuen Token). Demomodus: nichts senden.
    func upload(model: AppModel) async {
        guard let token, let client = model.active?.client, !model.isDemo else { return }
        let registration = PushDeviceRegistration(token: token, sandbox: Self.isSandbox, bundleId: Bundle.main.bundleIdentifier,
                                                  deviceName: model.deviceRegistration?.name, appVersion: AppEnvironment.shortVersion)
        do throws(APIError) {
            try await client.registerPushDevice(registration)
            DiagnosticsLog.shared.record("PUSH_REGISTERED", [:])
        } catch {
            DiagnosticsLog.shared.record("PUSH_REGISTER_FAILED", ["status": error.status ?? 0])
        }
    }

    /// Beim Abmelden: Gerät beim Server abmelden, damit keine Nachrichten für die Person mehr kommen.
    func unregister(client: AuthorizedClient) async {
        guard let token else { return }
        try? await client.unregisterPushDevice(token: token)
    }

    /// Zurücksetzen der App: Abmelden beim Server, höchstens `timeout` lang (ohne Netz soll das Löschen nicht hängen).
    func unregister(client: AuthorizedClient, timeout: Duration) async {
        let work = Task { await unregister(client: client) }
        let deadline = Task {
            try? await Task.sleep(for: timeout)
            work.cancel()
        }
        await work.value
        deadline.cancel()
    }

    /// Nach dem Zurücksetzen: kein Zähler, keine wartenden Ziele. Das APNs-Token gehört zum Gerät und bleibt.
    func resetAfterWipe() {
        unreadCount = 0
        pendingRoute = nil
        pendingReadId = nil
    }

    /// Ungelesene Anzahl übernehmen (Glocke) und das Abzeichen am App-Symbol setzen.
    func setUnread(_ count: Int) {
        unreadCount = UnreadBadge.appBadge(count)
        applyBadge()
    }

    /// Postfach übernehmen: Chat-Hinweise zählen am App-Symbol nicht einzeln, sondern als ungelesene Unterhaltungen.
    func setUnread(inbox: NotificationInbox) {
        inboxChatUnread = inbox.items.filter { !$0.isRead && Self.isChat($0) }.count
        setUnread(inbox.unreadCount)
    }

    /// Ungelesene Chats (Unterhaltungen mit neuen Nachrichten) für das Abzeichen am App-Symbol.
    func setChatUnread(conversations: Int) {
        guard chatUnreadConversations != conversations else { return }
        chatUnreadConversations = conversations
        applyBadge()
    }

    private var inboxChatUnread = 0
    private(set) var chatUnreadConversations = 0

    /// Zahl am App-Symbol: übrige ungelesene Hinweise plus ungelesene Chats.
    var appBadge: Int { UnreadBadge.appBadge(max(0, unreadCount - inboxChatUnread) + chatUnreadConversations) }

    private func applyBadge() {
        if permission == .allowed {
            UNUserNotificationCenter.current().setBadgeCount(appBadge) { _ in }
        }
    }

    static func isChat(_ item: InboxNotification) -> Bool {
        if case .chat = item.route { return true }
        return item.category?.lowercased().hasPrefix("chat") == true
    }

    // MARK: Chat: Antworten aus der Mitteilung

    /// Kategorie der Chat-Mitteilungen (APNs `aps.category`): „Antworten“ mit Texteingabe und „Gelesen“.
    nonisolated static let chatCategory = "chat"
    nonisolated static let replyAction = "chat.reply"
    nonisolated static let readAction = "chat.read"

    static func registerCategories() {
        let reply = UNTextInputNotificationAction(identifier: replyAction, title: "Antworten", options: [.authenticationRequired],
                                                  icon: UNNotificationActionIcon(systemImageName: "arrowshape.turn.up.left"),
                                                  textInputButtonTitle: "Senden", textInputPlaceholder: "Nachricht")
        let read = UNNotificationAction(identifier: readAction, title: "Als gelesen markieren", options: [],
                                        icon: UNNotificationActionIcon(systemImageName: "checkmark.bubble"))
        let chat = UNNotificationCategory(identifier: chatCategory, actions: [reply, read], intentIdentifiers: [],
                                          hiddenPreviewsBodyPlaceholder: "Neue Nachricht", categorySummaryFormat: "%u weitere Nachrichten",
                                          options: [.hiddenPreviewsShowTitle])
        UNUserNotificationCenter.current().setNotificationCategories([chat])
    }

    /// Sendet die Antwort (`AppSession` setzt die Ausführung); `false`, wenn die App gesperrt ist.
    var replyHandler: ((UUID, String) async -> ChatReplyOutcome)?
    var readHandler: ((UUID) async -> Void)?

    enum ChatReplyOutcome {
        case sent, queued, locked, failed
    }

    func handleReply(_ text: String, conversationId: UUID) async {
        let outcome = await replyHandler?(conversationId, text) ?? .locked
        let content = UNMutableNotificationContent()
        content.threadIdentifier = "chat-\(conversationId.uuidString.lowercased())"
        content.userInfo = ["link": "/chat/\(conversationId.uuidString.lowercased())", "objectType": "chat", "objectId": conversationId.uuidString.lowercased()]
        switch outcome {
        case .sent:
            return
        case .queued:
            content.title = "Antwort wartet auf Verbindung"
            content.body = "EstrichONE sendet sie, sobald der Server erreichbar ist."
        case .locked:
            pendingReplies[conversationId] = text
            content.title = "Antwort nicht gesendet"
            content.body = "EstrichONE ist gesperrt. Tippen Sie hier – Ihr Text steht dann im Eingabefeld."
        case .failed:
            pendingReplies[conversationId] = text
            content.title = "Antwort nicht gesendet"
            content.body = "Bitte öffnen Sie die Unterhaltung und senden Sie erneut."
        }
        try? await UNUserNotificationCenter.current().add(UNNotificationRequest(identifier: UUID().uuidString, content: content, trigger: nil))
    }

    /// Nicht gesendete Antworten aus Mitteilungen; die Unterhaltung übernimmt sie als Entwurf.
    var pendingReplies: [UUID: String] = [:]

    func takePendingReply(for conversationId: UUID) -> String? {
        pendingReplies.removeValue(forKey: conversationId)
    }

    nonisolated static func conversationId(from info: [AnyHashable: Any]) -> UUID? {
        let objectId = (info["objectId"] as? String).flatMap { UUID(uuidString: $0) }
            ?? (info["conversationId"] as? String).flatMap { UUID(uuidString: $0) }
        let route = NotificationRoute(link: info["link"] as? String, objectType: (info["objectType"] as? String) ?? "chat", objectId: objectId)
        if case let .chat(id?) = route { return id }
        return (info["conversationId"] as? String).flatMap { UUID(uuidString: $0) }
    }

    /// Angetippte Benachrichtigung öffnen.
    func open(link: String?, objectType: String?, objectId: String?, notificationId: String?) {
        pendingReadId = notificationId.flatMap { UUID(uuidString: $0) }
        pendingRoute = NotificationRoute(link: link, objectType: objectType, objectId: objectId.flatMap { UUID(uuidString: $0) }) ?? .inbox
    }

    /// Demomodus: eine Beispiel-Benachrichtigung in wenigen Sekunden (lokal, ohne Server).
    func scheduleDemoNotification() {
        let content = UNMutableNotificationContent()
        content.title = "Plan für morgen freigegeben"
        content.body = "Kolonne Huber: 07:30 Praxis Dr. Maier, 10:30 EFH Schneider."
        content.sound = .default
        content.threadIdentifier = "plan"
        content.userInfo = ["link": "/planung/wer-faehrt-wohin?tag=morgen", "category": "planning"]
        let trigger = UNTimeIntervalNotificationTrigger(timeInterval: 4, repeats: false)
        UNUserNotificationCenter.current().add(UNNotificationRequest(identifier: UUID().uuidString, content: content, trigger: trigger)) { _ in }
    }
}

/// Empfang im Vordergrund (Banner zeigen) und Tippen auf eine Benachrichtigung (Objekt öffnen).
final class NotificationDelegate: NSObject, UNUserNotificationCenterDelegate, @unchecked Sendable {
    static let shared = NotificationDelegate()

    func userNotificationCenter(_ center: UNUserNotificationCenter, willPresent notification: UNNotification,
                                withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void) {
        // Die Unterhaltung ist gerade offen: keine Mitteilung, die Nachricht steht schon im Verlauf.
        let info = notification.request.content.userInfo
        if let open = VisibleChat.shared.id, notification.request.content.categoryIdentifier == PushCenter.chatCategory
            || (info["objectType"] as? String)?.hasPrefix("chat") == true,
           PushCenter.conversationId(from: info) == open {
            completionHandler([])
            return
        }
        completionHandler([.banner, .list, .sound])
    }

    func userNotificationCenter(_ center: UNUserNotificationCenter, didReceive response: UNNotificationResponse,
                                withCompletionHandler completionHandler: @escaping () -> Void) {
        let info = response.notification.request.content.userInfo
        if response.actionIdentifier == PushCenter.replyAction || response.actionIdentifier == PushCenter.readAction {
            let text = (response as? UNTextInputNotificationResponse)?.userText ?? ""
            let isReply = response.actionIdentifier == PushCenter.replyAction
            guard let conversationId = PushCenter.conversationId(from: info) else {
                completionHandler()
                return
            }
            let done = CompletionBox(completionHandler)
            Task { @MainActor in
                if isReply {
                    await PushCenter.shared.handleReply(text, conversationId: conversationId)
                } else {
                    await PushCenter.shared.readHandler?(conversationId)
                }
                done.call()
            }
            return
        }
        let link = info["link"] as? String
        let objectType = info["objectType"] as? String
        let objectId = info["objectId"] as? String
        let notificationId = info["notificationId"] as? String
        Task { @MainActor in
            PushCenter.shared.open(link: link, objectType: objectType, objectId: objectId, notificationId: notificationId)
        }
        completionHandler()
    }
}

/// Abschluss einer Mitteilungs-Aktion, der nach dem Senden aufgerufen wird.
private final class CompletionBox: @unchecked Sendable {
    private let action: () -> Void
    init(_ action: @escaping () -> Void) { self.action = action }
    func call() { action() }
}

/// Nimmt das APNs-Token entgegen und setzt den Delegate für Benachrichtigungen.
final class AppDelegate: NSObject, UIApplicationDelegate {
    func application(_ application: UIApplication, didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]? = nil) -> Bool {
        UNUserNotificationCenter.current().delegate = NotificationDelegate.shared
        PushCenter.registerCategories()
        return true
    }

    func application(_ application: UIApplication, didRegisterForRemoteNotificationsWithDeviceToken deviceToken: Data) {
        PushCenter.shared.didRegister(deviceToken)
    }

    func application(_ application: UIApplication, didFailToRegisterForRemoteNotificationsWithError error: any Error) {
        PushCenter.shared.didFailToRegister(error)
    }
}
