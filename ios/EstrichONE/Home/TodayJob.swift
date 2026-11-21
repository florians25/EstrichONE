import EstrichCore
import SwiftUI

/// Ein Einsatz von heute, zusammengeführt aus meiner Planung, „Wer fährt wohin“ und der lokalen Akte.
struct TodayJob: Identifiable {
    let id: UUID
    let bauakteId: UUID
    let startText: String?
    let endText: String?
    let activity: String
    let number: String?
    let title: String
    let address: String?
    let crewName: String?
    let crewColor: Color?
    /// Personen des Einsatzes.
    let people: [String]
    /// Fahrzeuge und Geräte.
    let resources: [String]
    let warnings: [String]
    let notes: String?
    let contactName: String?
    let phone: URL?
    /// Telefonnummer zum Lesen („0171 2345678“).
    let phoneText: String?
    let route: URL?
    let status: BauakteStatus?
    /// Koordinaten der Baustelle (für die Entfernung), falls bekannt.
    var latitude: Double?
    var longitude: Double?

    var timeText: String {
        switch (startText, endText) {
        case let (start?, end?): "\(start) bis \(end)"
        case let (start?, nil): "ab \(start)"
        default: "ganztägig"
        }
    }

    static func make(assignments: [Assignment], board: DispatchBoard?, bauakten: [UUID: Bauakte]) -> [TodayJob] {
        let sites = Dictionary((board?.sites ?? []).map { ($0.bauakteId, $0) }, uniquingKeysWith: { first, _ in first })
        let named = (board?.crews ?? []).map { ($0.name, $0.color) } + (board?.sites ?? []).flatMap(\.crews).map { ($0.name, $0.color) }
        let crewColors = Dictionary(named.compactMap { name, color in Color(hexString: color).map { (name, $0) } },
                                    uniquingKeysWith: { first, _ in first })
        func color(_ job: DispatchBoard.Job?, crewName: String?) -> Color? {
            Color(hexString: job?.crewColor) ?? crewName.flatMap { crewColors[$0] }
        }
        func links(_ site: DispatchBoard.Site?, _ bauakte: Bauakte?, address: String?) -> (String?, URL?, String?, URL?) {
            let name = site?.siteContactName ?? bauakte?.siteContactName
            let number = (site?.siteContactPhone ?? bauakte?.siteContactPhone)?.trimmingCharacters(in: .whitespaces)
            let phone = ContactLink.phone(number)
            let route = site?.navigationUrl.flatMap(URL.init(string:))
                ?? bauakte.flatMap(MapsLink.url(for:))
                ?? MapsLink.url(address: address)
            return (name, phone, phone == nil ? nil : number, route)
        }

        if !assignments.isEmpty {
            return assignments.sorted { ($0.startTime ?? "99") < ($1.startTime ?? "99") }.map { assignment in
                let site = sites[assignment.bauakteId]
                let job = site?.jobs.first { $0.assignmentId == assignment.id }
                let bauakte = bauakten[assignment.bauakteId]
                let address = assignment.bauakteAddress ?? site?.address
                let (name, phone, phoneText, route) = links(site, bauakte, address: address)
                return TodayJob(
                    id: assignment.id, bauakteId: assignment.bauakteId,
                    startText: assignment.startText ?? job?.startText, endText: job?.endText,
                    activity: assignment.activity, number: assignment.bauakteNumber, title: assignment.bauakteTitle,
                    address: address, crewName: assignment.crewName ?? job?.crewName,
                    crewColor: color(job, crewName: assignment.crewName ?? job?.crewName),
                    people: assignment.people.map(\.name), resources: assignment.resources.map(\.name),
                    warnings: assignment.warnings + (site?.warnings ?? []).filter { !assignment.warnings.contains($0) },
                    notes: assignment.notes ?? job?.notes, contactName: name, phone: phone, phoneText: phoneText, route: route,
                    status: bauakte?.status, latitude: bauakte?.latitude, longitude: bauakte?.longitude)
            }
        }
        // Ohne eigene Einsätze: Baustellen, auf denen ich (z. B. über meine Kolonne) heute eingeplant bin.
        let mine = (board?.sortedSites ?? []).filter { board?.mySiteIds.contains($0.bauakteId) == true }
        return mine.flatMap { site -> [TodayJob] in
            let bauakte = bauakten[site.bauakteId]
            let (name, phone, phoneText, route) = links(site, bauakte, address: site.address)
            return site.jobs.map { job in
                TodayJob(
                    id: job.assignmentId, bauakteId: site.bauakteId, startText: job.startText ?? site.startText, endText: job.endText,
                    activity: job.activity, number: site.number, title: site.title, address: site.address,
                    crewName: job.crewName, crewColor: color(job, crewName: job.crewName),
                    people: site.people.map(\.name), resources: site.vehicles.map(\.name), warnings: site.warnings, notes: job.notes,
                    contactName: name, phone: phone, phoneText: phoneText, route: route, status: bauakte?.status,
                    latitude: bauakte?.latitude, longitude: bauakte?.longitude)
            }
        }
    }
}

/// Karte eines Einsatzes – alles für die Baustelle auf einen Blick: Uhrzeit mit „Jetzt“/„in 2 Std.“, Baustelle mit Status,
/// Kennzahlen (offene Mängel und Aufgaben, Belegreife, Wetter laut Tagebuch), Adresse mit Entfernung, Ansprechpartner,
/// Leistung, Kolonne, Fahrzeuge und Geräte, Hinweise. Unten vier große Handgriffe (Route, Anrufen, Foto, Tagebuch) –
/// gut mit Handschuhen und einer Hand. Weitere Einsätze des Tages stehen zugeklappt darunter (Tipp klappt auf).
struct TodayJobCard: View {
    enum Action {
        case open, photo, diary, defect, route, call
    }

    let job: TodayJob
    var glance: SiteGlance?
    /// „1,8 km“ (Luftlinie), falls Standort und Koordinaten bekannt.
    var distance: String?
    var phase: TodayGlance.Phase = .unknown
    /// Aufgeklappt (alle Angaben und Handgriffe) oder kompakt (eine Zeile mit Route und Anruf).
    var expanded = true
    /// Bauakte liegt auf dem Gerät (sonst nur Öffnen, das lädt sie nach).
    var canCapture = true
    var onExpand: () -> Void = {}
    let perform: (Action) -> Void
    @Environment(\.dynamicTypeSize) private var typeSize

    var body: some View {
        Group {
            if expanded { full } else { compact }
        }
        .padding(Space.l)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card()
        .overlay {
            // Der laufende Einsatz ist dezent umrandet.
            if phase == .running {
                RoundedRectangle(cornerRadius: Radius.xl, style: .continuous)
                    .strokeBorder(Theme.accent.opacity(0.55), lineWidth: 1.5)
            }
        }
        .contentShape(.contextMenuPreview, RoundedRectangle(cornerRadius: Radius.xl, style: .continuous))
        .contextMenu { menuItems }
    }

    // MARK: Aufgeklappt

    private var full: some View {
        VStack(alignment: .leading, spacing: Space.m) {
            // Kopf: Uhrzeit mit Kennzeichnung und rechts „…“; darunter Baustelle (Tipp öffnet die Bauakte) über die volle Breite.
            HStack(alignment: .top, spacing: Space.s) {
                timeRow
                Spacer(minLength: 0)
                Menu { menuItems } label: {
                    Image(systemName: "ellipsis.circle")
                        .font(.title3)
                        .foregroundStyle(Theme.accent)
                        .frame(width: Size.minTap, height: Size.minTap)
                        .contentShape(Rectangle())
                }
                .menuStyle(.button)
                .buttonStyle(.plain)
                .padding(.top, -Space.s)
                .accessibilityLabel("Weitere Aktionen")
                .accessibilityIdentifier("todayJobMore")
            }
            Button { perform(.open) } label: { heading }
                .buttonStyle(.plain)
                .padding(.top, -Space.s)
                .accessibilityIdentifier("todayJob-\(job.title)")
                .accessibilityHint("Öffnet die Bauakte")

            if let glance, !glance.isEmpty {
                Button { perform(.open) } label: { GlanceChips(glance: glance) }
                    .buttonStyle(.plain)
                    .accessibilityIdentifier("todayJobGlance")
            }

            InsetDivider()

            VStack(alignment: .leading, spacing: Space.m) {
                if let address = job.address {
                    line(symbol: "mappin.and.ellipse", text: address, detail: distance.map { "\($0) entfernt (Luftlinie)" })
                }
                if let contact = job.contactName {
                    line(symbol: "person.crop.circle", text: "Vor Ort: \(contact)", detail: job.phoneText)
                } else if let phone = job.phoneText {
                    line(symbol: "phone", text: "Ansprechpartner vor Ort", detail: phone)
                }
                line(symbol: "hammer", text: job.activity, strong: true)
                if job.crewName != nil || !job.people.isEmpty {
                    crewLine
                }
                if !job.resources.isEmpty {
                    line(symbol: "truck.box", text: job.resources.joined(separator: ", "))
                }
                if let notes = job.notes?.compactText {
                    line(symbol: "text.bubble", text: notes)
                }
                ForEach(job.warnings, id: \.self) { warning in
                    Callout(tone: .warning, message: warning)
                }
            }

            actionBar
        }
    }

    /// Uhrzeit und „Jetzt“/„in 2 Std.“; bei sehr großer Schrift untereinander (nichts wird abgeschnitten).
    private var timeRow: some View {
        let layout = typeSize.isAccessibilitySize ? AnyLayout(VStackLayout(alignment: .leading, spacing: Space.xs))
                                                  : AnyLayout(HStackLayout(alignment: .center, spacing: Space.s))
        return layout {
            Text(job.timeText)
                .font(Typo.time)
                .foregroundStyle(Theme.textPrimary)
            PhaseBadge(phase: phase)
        }
        .accessibilityElement(children: .combine)
    }

    private var heading: some View {
        VStack(alignment: .leading, spacing: Space.xs) {
            HStack(alignment: .firstTextBaseline, spacing: Space.s) {
                Text(job.title)
                    .font(.title3.weight(.semibold))
                    .foregroundStyle(Theme.textPrimary)
                    .multilineTextAlignment(.leading)
                    .fixedSize(horizontal: false, vertical: true)
                Spacer(minLength: Space.s)
                Chevron()
            }
            // Nummer und Status: nebeneinander, bei sehr großer Schrift untereinander.
            let layout = typeSize.isAccessibilitySize ? AnyLayout(VStackLayout(alignment: .leading, spacing: Space.xxs))
                                                      : AnyLayout(HStackLayout(alignment: .firstTextBaseline, spacing: Space.s))
            layout {
                if let number = job.number {
                    Text(number)
                        .font(Typo.subhead.monospacedDigit())
                        .foregroundStyle(Theme.textSecondary)
                        .fixedSize()
                }
                if let status = job.status { StatusLabel(status: status) }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .contentShape(Rectangle())
    }

    /// Vier gleich große Handgriffe (64 pt hoch); bei sehr großer Schrift zwei je Zeile.
    private var actionBar: some View {
        let columns = Array(repeating: GridItem(.flexible(), spacing: Space.s), count: typeSize.isAccessibilitySize ? 2 : 4)
        return LazyVGrid(columns: columns, spacing: Space.s) {
            Button { perform(.route) } label: { Label("Route", systemImage: "arrow.triangle.turn.up.right.diamond.fill") }
                .disabled(job.route == nil)
                .accessibilityIdentifier("todayJobRoute")
            Button { perform(.call) } label: { Label("Anrufen", systemImage: "phone.fill") }
                .disabled(job.phone == nil)
                .accessibilityLabel(job.contactName.map { "\($0) anrufen" } ?? "Anrufen")
                .accessibilityIdentifier("todayJobCall")
            Button { perform(.photo) } label: { Label("Foto", systemImage: "camera.fill") }
                .buttonStyle(ActionTileStyle(rank: .primary))
                .disabled(!canCapture)
                .accessibilityIdentifier("todayJobPhoto")
            Button { perform(.diary) } label: { Label("Tagebuch", systemImage: "book.fill") }
                .disabled(!canCapture)
                .accessibilityLabel("Bautagebuch-Eintrag")
                .accessibilityIdentifier("todayJobDiary")
        }
        .buttonStyle(ActionTileStyle())
    }

    // MARK: Zugeklappt

    private var compact: some View {
        HStack(alignment: .center, spacing: Space.m) {
            Button(action: onExpand) {
                VStack(alignment: .leading, spacing: Space.xs) {
                    let layout = typeSize.isAccessibilitySize ? AnyLayout(VStackLayout(alignment: .leading, spacing: Space.xs))
                                                              : AnyLayout(HStackLayout(alignment: .center, spacing: Space.s))
                    layout {
                        Text(job.timeText)
                            .font(Typo.headline.monospacedDigit())
                            .foregroundStyle(Theme.textPrimary)
                        PhaseBadge(phase: phase)
                    }
                    Text(job.title)
                        .font(Typo.headline)
                        .foregroundStyle(Theme.textPrimary)
                        .multilineTextAlignment(.leading)
                        .fixedSize(horizontal: false, vertical: true)
                    Text([job.activity, job.crewName].compactMap { $0 }.joined(separator: ", "))
                        .font(Typo.subhead)
                        .foregroundStyle(Theme.textSecondary)
                        .fixedSize(horizontal: false, vertical: true)
                    if let address = job.address {
                        Text([address, distance].compactMap { $0 }.joined(separator: ", "))
                            .font(Typo.subhead)
                            .foregroundStyle(Theme.textSecondary)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                    if let glance, !glance.isEmpty {
                        GlanceChips(glance: glance).padding(.top, Space.xxs)
                    }
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .accessibilityHint("Zeigt alle Angaben und Handgriffe")
            .accessibilityIdentifier("todayJob-\(job.title)")

            VStack(spacing: Space.s) {
                if job.route != nil {
                    Button { perform(.route) } label: { Label("Route", systemImage: "arrow.triangle.turn.up.right.diamond.fill") }
                        .buttonStyle(IconButtonStyle())
                }
                if job.phone != nil {
                    Button { perform(.call) } label: { Label("Anrufen", systemImage: "phone.fill") }
                        .buttonStyle(IconButtonStyle())
                }
            }
        }
    }

    // MARK: Bausteine

    @ViewBuilder
    private var menuItems: some View {
        if job.route != nil {
            Button("Route starten", systemImage: "arrow.triangle.turn.up.right.diamond") { perform(.route) }
        }
        if job.phone != nil {
            Button(job.contactName.map { "\($0) anrufen" } ?? "Anrufen", systemImage: "phone") { perform(.call) }
        }
        if canCapture {
            Button("Foto aufnehmen", systemImage: "camera") { perform(.photo) }
            Button("Bautagebuch-Eintrag", systemImage: "book") { perform(.diary) }
            Button("Mangel melden", systemImage: "exclamationmark.triangle") { perform(.defect) }
        }
        Button("Bauakte öffnen", systemImage: "folder") { perform(.open) }
            .accessibilityIdentifier("todayJobOpen")
    }

    private var crewLine: some View {
        HStack(alignment: .firstTextBaseline, spacing: Space.m) {
            Image(systemName: "person.3")
                .font(.subheadline.weight(SymbolWeight.content))
                .foregroundStyle(Theme.textSecondary)
                .frame(width: 22)
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 2) {
                if let crew = job.crewName {
                    HStack(spacing: 6) {
                        StatusDot(color: job.crewColor ?? Theme.accent, size: 9)
                        Text(crew).font(Typo.subheadStrong).foregroundStyle(Theme.textPrimary)
                    }
                }
                if !job.people.isEmpty {
                    Text(job.people.joined(separator: ", "))
                        .font(Typo.subhead)
                        .foregroundStyle(Theme.textSecondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
        }
        .accessibilityElement(children: .combine)
    }

    private func line(symbol: String, text: String, detail: String? = nil, strong: Bool = false) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: Space.m) {
            Image(systemName: symbol)
                .font(.subheadline.weight(SymbolWeight.content))
                .foregroundStyle(Theme.textSecondary)
                .frame(width: 22)
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 2) {
                Text(text)
                    .font(strong ? Typo.subheadStrong : Typo.subhead)
                    .foregroundStyle(Theme.textPrimary)
                    .fixedSize(horizontal: false, vertical: true)
                if let detail {
                    Text(detail)
                        .font(Typo.subhead.monospacedDigit())
                        .foregroundStyle(Theme.textSecondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
        }
        .accessibilityElement(children: .combine)
    }

    /// Platzhalter beim ersten Laden.
    static var placeholder: some View {
        VStack(alignment: .leading, spacing: Space.m) {
            SkeletonBar(width: 130, height: 20)
            SkeletonBar(width: 220, height: 18)
            SkeletonBar(width: 90, height: 12)
            InsetDivider()
            SkeletonBar(height: 14)
            SkeletonBar(width: 200, height: 14)
            SkeletonBar(width: 160, height: 14)
            RoundedRectangle(cornerRadius: Radius.l, style: .continuous).fill(Theme.fill).frame(height: 64)
        }
        .padding(Space.l)
        .card()
        .pulsing()
    }
}

/// „Jetzt“ (Markenblau), „in 2 Std.“ (grau) oder „Vorbei“ – neben der Uhrzeit.
private struct PhaseBadge: View {
    let phase: TodayGlance.Phase

    var body: some View {
        if let text = phase.text {
            let running = phase == .running
            HStack(spacing: 5) {
                if running { StatusDot(color: Theme.accent, size: 7) }
                Text(text)
            }
            .font(Typo.labelStrong)
            .foregroundStyle(running ? Theme.accent : Theme.textSecondary)
            .padding(.horizontal, 8)
            .frame(minHeight: 22)
            .background(running ? Theme.accentSoft : Theme.fill, in: Capsule())
            .lineLimit(1)
            .fixedSize()
            .accessibilityLabel(running ? "Läuft jetzt" : text)
        }
    }
}

/// Kennzahlen der Baustelle als kleine Kapseln: offene Mängel (rot), Aufgaben, Belegreife, Wetter laut Tagebuch.
private struct GlanceChips: View {
    let glance: SiteGlance

    var body: some View {
        ChipFlow(spacing: 6) {
            if let defects = glance.defectsText {
                chip(defects, symbol: "exclamationmark.triangle.fill", foreground: Theme.danger, background: Theme.dangerSoft)
            }
            if let tasks = glance.tasksText {
                chip(tasks, symbol: "checklist", foreground: Theme.textSecondary, background: Theme.fill)
            }
            if let readiness = glance.readiness {
                switch readiness {
                case .ready:
                    chip(readiness.text, symbol: "checkmark.seal.fill", foreground: Theme.success, background: Theme.successSoft)
                case .notReady:
                    chip(readiness.text, symbol: "hourglass", foreground: Theme.warning, background: Theme.warningSoft)
                case .rooms, .laid:
                    chip(readiness.text, symbol: readiness.isReady ? "checkmark.seal" : "square.split.2x2",
                         foreground: readiness.isReady ? Theme.success : Theme.textSecondary,
                         background: readiness.isReady ? Theme.successSoft : Theme.fill)
                }
            }
            if let weather = glance.weatherText {
                chip(weather, symbol: glance.weather?.symbol ?? "thermometer.medium", foreground: Theme.textSecondary, background: Theme.fill)
            }
        }
        .accessibilityElement(children: .combine)
    }

    private func chip(_ text: String, symbol: String, foreground: Color, background: Color) -> some View {
        HStack(spacing: 5) {
            Image(systemName: symbol).imageScale(.small).accessibilityHidden(true)
            Text(text).lineLimit(2).fixedSize(horizontal: false, vertical: true)
        }
        .font(Typo.labelStrong)
        .foregroundStyle(foreground)
        .padding(.horizontal, 9)
        .padding(.vertical, 3)
        .frame(minHeight: 26)
        .background(background, in: Capsule())
    }
}
