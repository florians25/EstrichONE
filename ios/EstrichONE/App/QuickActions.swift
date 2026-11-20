import AppIntents
import EstrichCore
import SwiftUI
import UIKit

/// Schnellzugriffe von außen: App-Symbol lange drücken (Schnellaktionen), Kurzbefehle, Siri und die Aktionstaste.
/// „Foto“ und „Bautagebuch“ gelten der aktuellen Baustelle – dem heutigen Einsatz, sonst der zuletzt geöffneten
/// Bauakte; die Kamera zeigt sie oben an und lässt sie dort wechseln. Gesperrt oder abgemeldet wartet die Aktion,
/// bis die App wieder offen ist.
enum QuickAction: String, CaseIterable, Sendable {
    case photo = "de.estrichone.quick.photo"
    case diary = "de.estrichone.quick.diary"
    case search = "de.estrichone.quick.search"

    var title: String {
        switch self {
        case .photo: "Foto zur Baustelle"
        case .diary: "Bautagebuch"
        case .search: "Baustelle suchen"
        }
    }

    var subtitle: String? {
        switch self {
        case .photo: "Heutiger Einsatz oder zuletzt geöffnet"
        case .diary: "Eintrag für heute"
        case .search: nil
        }
    }

    var symbol: String {
        switch self {
        case .photo: "camera"
        case .diary: "book"
        case .search: "magnifyingglass"
        }
    }

    @MainActor
    var shortcutItem: UIApplicationShortcutItem {
        UIApplicationShortcutItem(type: rawValue, localizedTitle: title, localizedSubtitle: subtitle,
                                  icon: UIApplicationShortcutIcon(systemImageName: symbol), userInfo: nil)
    }
}

/// Wartende Schnellaktion; `MainTabView` führt sie aus, sobald die App entsperrt ist.
@MainActor
@Observable
final class QuickActionCenter {
    static let shared = QuickActionCenter()

    var pending: QuickAction?

    func receive(_ item: UIApplicationShortcutItem) {
        pending = QuickAction(rawValue: item.type)
    }

    /// Schnellaktionen am App-Symbol (ab dem ersten Start).
    static func install(_ application: UIApplication) {
        application.shortcutItems = QuickAction.allCases.map { $0.shortcutItem }
    }
}

/// Nimmt Schnellaktionen entgegen, während die App schon läuft (beim Kaltstart: `AppDelegate`).
final class QuickActionSceneDelegate: NSObject, UIWindowSceneDelegate {
    func windowScene(_ windowScene: UIWindowScene, performActionFor shortcutItem: UIApplicationShortcutItem) async -> Bool {
        QuickActionCenter.shared.receive(shortcutItem)
        return QuickAction(rawValue: shortcutItem.type) != nil
    }
}

// MARK: Kurzbefehle, Siri, Aktionstaste

struct SitePhotoIntent: AppIntent {
    static let title: LocalizedStringResource = "Foto zur Baustelle"
    static let openAppWhenRun = true

    @MainActor
    func perform() async throws -> some IntentResult {
        QuickActionCenter.shared.pending = .photo
        return .result()
    }
}

struct SiteDiaryIntent: AppIntent {
    static let title: LocalizedStringResource = "Bautagebuch-Eintrag"
    static let openAppWhenRun = true

    @MainActor
    func perform() async throws -> some IntentResult {
        QuickActionCenter.shared.pending = .diary
        return .result()
    }
}

struct EstrichShortcuts: AppShortcutsProvider {
    static var appShortcuts: [AppShortcut] {
        AppShortcut(intent: SitePhotoIntent(), phrases: ["Foto mit \(.applicationName)", "Baustellenfoto mit \(.applicationName)"],
                    shortTitle: "Foto zur Baustelle", systemImageName: "camera")
        AppShortcut(intent: SiteDiaryIntent(), phrases: ["Bautagebuch in \(.applicationName)", "Tagebuch mit \(.applicationName)"],
                    shortTitle: "Bautagebuch", systemImageName: "book")
    }
}
