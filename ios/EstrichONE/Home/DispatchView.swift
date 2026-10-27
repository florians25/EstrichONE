import EstrichCore
import SwiftUI

extension DispatchBoard {
    /// Baustellen einer Kolonne an diesem Tag (meine zuerst, dann nach Beginn).
    func crewSites(_ crew: Crew) -> [Site] {
        sortedSites.filter { site in crew.siteIds.contains(site.id) || site.crews.contains { $0.id == crew.id } }
            .sorted { lhs, rhs in
                (lhs.crews.first { $0.id == crew.id }?.startTime ?? "99") < (rhs.crews.first { $0.id == crew.id }?.startTime ?? "99")
            }
    }

    /// Baustellen, denen noch keine Kolonne zugeordnet ist.
    var sitesWithoutCrew: [Site] {
        let assigned = Set(crews.flatMap(\.siteIds))
        return sortedSites.filter { $0.crews.isEmpty && !assigned.contains($0.id) }
    }

    /// Kolonnen mit Baustellen zuerst; ohne Kolonnen (ältere Server) alle Baustellen in einer Gruppe.
    var crewGroups: [(crew: Crew?, sites: [Site])] {
        let groups: [(crew: Crew?, sites: [Site])] = crews.map { (crew: Optional($0), sites: crewSites($0)) }.filter { !$0.sites.isEmpty }
        let rest = sitesWithoutCrew
        return rest.isEmpty ? groups : groups + [(crew: nil, sites: rest)]
    }
}

/// Laden und Zwischenspeichern der Tafel (auch für die Startseite). Offline bleibt der zuletzt geladene Stand des Tages.
enum DispatchData {
    struct Day: Equatable {
        let board: DispatchBoard?
        let loadedAt: Date
        let isCurrent: Bool
        /// Hinweis statt Liste (z. B. fehlendes Recht).
        let message: String?
    }

    private static func key(_ day: String) -> String { "dispatch_board_\(day)" }

    /// Zuletzt gespeicherter Stand des Tages – ohne Netz, sofort.
    @MainActor
    static func cached(day: String, model: AppModel) async -> Day? {
        guard let store = model.active?.sync?.store,
              let text = try? await store.meta(key(day)),
              let cached = try? JSONCoding.decoder.decode(StoredBoard.self, from: Data(text.utf8)) else { return nil }
        return Day(board: cached.board, loadedAt: cached.loadedAt, isCurrent: false, message: nil)
    }

    @MainActor
    static func load(day: String, model: AppModel) async -> Day? {
        guard let active = model.active else { return nil }
        do throws(APIError) {
            let board = try await active.client.dispatch(day: day)
            if let encoded = try? JSONCoding.encoder.encode(StoredBoard(board: board, loadedAt: .now)) {
                try? await active.sync?.store.setMeta(key(day), String(decoding: encoded, as: UTF8.self))
            }
            return Day(board: board, loadedAt: .now, isCurrent: true, message: nil)
        } catch {
            if error.status == 403 {
                return Day(board: nil, loadedAt: .now, isCurrent: true, message: "Für die Planung fehlt Ihnen die Berechtigung.")
            }
            if let cached = await cached(day: day, model: model) { return cached }
            return Day(board: nil, loadedAt: .now, isCurrent: false, message: "Ohne Verbindung ist die Planung für diesen Tag nicht verfügbar.")
        }
    }

    /// Gespeicherte Tafel mit Ladezeitpunkt (ältere Stände ohne Zeitpunkt werden einfach neu geladen).
    private struct StoredBoard: Codable {
        let board: DispatchBoard
        let loadedAt: Date
    }
}

/// Baustelle in „Wer fährt wohin“: links Beginn und Ende (gleich breite Ziffern) und ggf. „Ich“, daneben Nummer und Titel,
/// Tätigkeit, Adresse und Hinweise – streng linksbündig; rechts ein Routen-Knopf.
struct DispatchSiteRow: View {
    let site: DispatchBoard.Site
    var isMine = false
    var showsCrews = false
    /// Einsatz der Kolonne, unter der die Zeile steht: deren Zeitspanne statt des frühesten Beginns.
    var slot: DispatchBoard.SiteCrew?

    var body: some View {
        HStack(alignment: .top, spacing: Space.m) {
            VStack(alignment: .leading, spacing: 4) {
                Text(slot?.startText ?? site.startText ?? "–")
                    .font(Typo.bodyStrong.monospacedDigit())
                    .foregroundStyle(Theme.textPrimary)
                if let end = slot?.endText {
                    Text("bis \(end)")
                        .font(Typo.meta.monospacedDigit())
                        .foregroundStyle(Theme.textSecondary)
                        .lineLimit(1)
                        .minimumScaleFactor(0.8)
                }
                if isMine {
                    Chip(text: "Ich", tone: .accent)
                        .accessibilityLabel("Mein Einsatz")
                }
            }
            .frame(width: 64, alignment: .leading)

            NavigationLink(value: HomeRoute.bauakte(site.bauakteId)) {
                VStack(alignment: .leading, spacing: 3) {
                    Text([site.number, site.title].compactMap { $0 }.joined(separator: " · "))
                        .font(Typo.subheadStrong)
                        .foregroundStyle(Theme.textPrimary)
                        .lineLimit(2)
                    ForEach(site.jobs) { job in
                        Text(job.activity)
                            .font(Typo.subhead)
                            .foregroundStyle(Theme.textSecondary)
                            .lineLimit(2)
                    }
                    if showsCrews, !site.crews.isEmpty {
                        HStack(spacing: 6) {
                            ForEach(site.crews) { crew in
                                Chip(text: crew.name, dot: Color(hexString: crew.color) ?? Theme.accent)
                            }
                        }
                    }
                    if let address = site.address {
                        Text(address).font(Typo.meta).foregroundStyle(Theme.textSecondary).lineLimit(1)
                    }
                    ForEach(site.warnings, id: \.self) { warning in
                        Chip(text: warning, symbol: "exclamationmark.triangle.fill", tone: .warning)
                    }
                }
                .multilineTextAlignment(.leading)
                .frame(maxWidth: .infinity, alignment: .leading)
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)

            if let url = site.navigationUrl.flatMap(URL.init(string:)) ?? MapsLink.url(address: site.address) {
                Link(destination: url) { Label("Route starten", systemImage: "arrow.triangle.turn.up.right.diamond.fill") }
                    .buttonStyle(IconButtonStyle())
                    .accessibilityLabel("Route starten")
            }
        }
        .padding(.vertical, Space.xs)
    }
}
