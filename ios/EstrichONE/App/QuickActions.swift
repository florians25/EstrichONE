import AppIntents
import EstrichCore
import SwiftUI
import UIKit

/// Schnellzugriffe von außen: App-Symbol lange drücken (Schnellaktionen), Kurzbefehle, Siri und die Aktionstaste.
/// „Foto“, „Bautagebuch“ und „Mangel“ gelten der aktuellen Bauakte: dem laufenden Einsatz von heute (sonst dem nächsten
/// heute), sonst der zuletzt geöffneten Bauakte, sonst wird die Baustelle gewählt. Die Kamera zeigt die Bauakte oben an und lässt sie dort wechseln. Gesperrt oder
/// abgemeldet wartet die Aktion, bis die App wieder offen ist.
enum QuickAction: String, CaseIterable, Identifiable, Sendable {
    case photo = "de.estrichone.quick.photo"
    case diary = "de.estrichone.quick.diary"
    case defect = "de.estrichone.quick.defect"
    case search = "de.estrichone.quick.search"

    var id: String { rawValue }

    var title: String {
        switch self {
        case .photo: "Foto zur heutigen Baustelle"
        case .diary: "Bautagebuch"
        case .defect: "Mangel melden"
        case .search: "Baustelle suchen"
        }
    }

    var subtitle: String? {
        switch self {
        case .photo: "Laufender Einsatz oder zuletzt geöffnet"
        case .diary: "Eintrag für heute"
        case .defect: "Mit Frist, zum laufenden Einsatz"
        case .search: nil
        }
    }

    var symbol: String {
        switch self {
        case .photo: "camera"
        case .diary: "book"
        case .defect: "exclamationmark.triangle"
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

struct SiteDefectIntent: AppIntent {
    static let title: LocalizedStringResource = "Mangel melden"
    static let openAppWhenRun = true

    @MainActor
    func perform() async throws -> some IntentResult {
        QuickActionCenter.shared.pending = .defect
        return .result()
    }
}

struct EstrichShortcuts: AppShortcutsProvider {
    static var appShortcuts: [AppShortcut] {
        AppShortcut(intent: SitePhotoIntent(), phrases: ["Foto mit \(.applicationName)", "Baustellenfoto mit \(.applicationName)"],
                    shortTitle: "Foto zur Baustelle", systemImageName: "camera")
        AppShortcut(intent: SiteDiaryIntent(), phrases: ["Bautagebuch in \(.applicationName)", "Tagebuch mit \(.applicationName)"],
                    shortTitle: "Bautagebuch", systemImageName: "book")
        AppShortcut(intent: SiteDefectIntent(), phrases: ["Mangel melden mit \(.applicationName)", "Mangel in \(.applicationName)"],
                    shortTitle: "Mangel melden", systemImageName: "exclamationmark.triangle")
    }
}
