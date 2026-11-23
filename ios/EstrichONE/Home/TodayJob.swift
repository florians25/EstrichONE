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

/// Hauptkarte „Nächster Einsatz“ auf „Heute“: oben Uhrzeit mit „Jetzt“ bzw. „in 2 Std.“, dann Bauvorhaben mit Nummer
/// und Status (Tipp öffnet die Bauakte), darunter in ruhigen Zeilen Adresse mit Entfernung, Leistung und Fläche, Kolonne
/// und Fahrzeuge, Ansprechpartner, Zugang und Hinweise. Kennzahlen (Mängel, Aufgaben, Belegreife) als kleine Kapseln,
/// die letzte Nachricht im Chat der Baustelle, unten vier gleich große Schnellaktionen: Route, Anrufen, Chat, Bauakte.
/// Foto, Bautagebuch und Mangel stehen im Menü „…“, im Kontextmenü und beim Wischen.
struct TodayJobCard: View {
    enum Action {
        case open, photo, diary, defect, route, call, chat
    }

    let job: TodayJob
    var glance: SiteGlance?
    var extras: TodaySiteExtras?
    /// „1,8 km“ (Luftlinie), falls Standort und Koordinaten bekannt.
    var distance: String?
    var phase: TodayGlance.Phase = .unknown
    /// Bauakte liegt auf dem Gerät (sonst nur Öffnen, das lädt sie nach).
    var canCapture = true
    /// Chat ist eingeschaltet (sonst ist „Chat“ ausgegraut).
    var chatAvailable = true
    let perform: (Action) -> Void
    @Environment(\.dynamicTypeSize) private var typeSize

    var body: some View {
        full
            .padding(Space.l)
            .frame(maxWidth: .infinity, alignment: .leading)
            .card()
            .overlay {
                // Der laufende Einsatz ist dezent umrandet.
                if phase == .running {
                    RoundedRectangle(cornerRadius: Radius.xl, style: .continuous)
                        .strokeBorder(Theme.accent.opacity(0.45), lineWidth: 1.5)
                }
            }
            .contentShape(.contextMenuPreview, RoundedRectangle(cornerRadius: Radius.xl, style: .continuous))
            .contextMenu { menuItems }
    }

    private var full: some View {
        VStack(alignment: .leading, spacing: Space.m) {
            // Kopf: Uhrzeit mit Kennzeichnung und rechts „…“; darunter Baustelle (Tipp öffnet die Bauakte).
            HStack(alignment: .center, spacing: Space.s) {
                timeRow
                Spacer(minLength: 0)
                Menu { menuItems } label: {
                    Image(systemName: "ellipsis")
                        .font(.body.weight(SymbolWeight.control))
                        .foregroundStyle(Theme.accent)
                        .frame(width: Size.minTap, height: Size.minTap)
                        .contentShape(Rectangle())
                }
                .menuStyle(.button)
                .buttonStyle(.plain)
                .padding(.vertical, -Space.s)
                .padding(.trailing, -Space.s)
                .accessibilityLabel("Weitere Aktionen")
                .accessibilityIdentifier("todayJobMore")
            }
            Button { perform(.open) } label: { heading }
                .buttonStyle(.plain)
                .accessibilityIdentifier("todayJob-\(job.title)")
                .accessibilityHint("Öffnet die Bauakte")

            VStack(alignment: .leading, spacing: Space.s) {
                if let address = job.address {
                    line(symbol: "mappin.and.ellipse", text: address, detail: distance.map { "\($0) entfernt (Luftlinie)" })
                }
                line(symbol: "hammer", text: job.activity, detail: extras?.facts, strong: true)
                if job.crewName != nil || !job.people.isEmpty || !job.resources.isEmpty {
                    crewLine
                }
                if let contact = job.contactName {
                    line(symbol: "person.crop.circle", text: "Vor Ort: \(contact)", detail: job.phoneText)
                } else if let phone = job.phoneText {
                    line(symbol: "phone", text: "Ansprechpartner vor Ort", detail: phone)
                }
                if let access = extras?.access {
                    line(symbol: "key", text: access, caption: "Zugang", limit: 3)
                }
                if let notes = job.notes?.compactText {
                    line(symbol: "text.bubble", text: notes, caption: "Hinweis der Disposition", limit: 3)
                }
                if let note = extras?.pinned.first {
                    line(symbol: "pin", text: note, caption: "Angeheftete Notiz", limit: 3)
                }
            }

            if let glance, !glance.isEmpty {
                GlanceChips(glance: glance)
                    .accessibilityIdentifier("todayJobGlance")
            }
            if let chat = extras?.chat {
                chatLine(chat)
            }
            ForEach(job.warnings, id: \.self) { warning in
                Callout(tone: .warning, message: warning)
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
        HStack(alignment: .center, spacing: Space.s) {
            VStack(alignment: .leading, spacing: Space.xs) {
                Text(job.title)
                    .font(.title3.weight(.semibold))
                    .foregroundStyle(Theme.textPrimary)
                    .multilineTextAlignment(.leading)
                    .fixedSize(horizontal: false, vertical: true)
                // Nummer und Status: nebeneinander, bei sehr großer Schrift untereinander.
                let layout = typeSize.isAccessibilitySize ? AnyLayout(VStackLayout(alignment: .leading, spacing: Space.xxs))
                                                          : AnyLayout(HStackLayout(alignment: .center, spacing: Space.s))
                layout {
                    if let number = job.number {
                        Text(number)
                            .font(Typo.subhead.monospacedDigit())
                            .foregroundStyle(Theme.textSecondary)
                            .fixedSize()
                    }
                    if let status = job.status { StatusBadge(status: status) }
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            Chevron()
        }
        .contentShape(Rectangle())
    }

    /// Vier gleich große Schnellaktionen; bei sehr großer Schrift zwei je Zeile.
    private var actionBar: some View {
        let columns = Array(repeating: GridItem(.flexible(), spacing: Space.s), count: typeSize.isAccessibilitySize ? 2 : 4)
        return LazyVGrid(columns: columns, spacing: Space.s) {
            Button { perform(.route) } label: { Label("Route", systemImage: "arrow.triangle.turn.up.right.diamond.fill") }
                .buttonStyle(ActionTileStyle(rank: .primary))
                .disabled(job.route == nil)
                .accessibilityIdentifier("todayJobRoute")
            Button { perform(.call) } label: { Label("Anrufen", systemImage: "phone.fill") }
                .disabled(job.phone == nil)
                .accessibilityLabel(job.contactName.map { "\($0) anrufen" } ?? "Anrufen")
                .accessibilityIdentifier("todayJobCall")
            Button { perform(.chat) } label: { Label("Chat", systemImage: "bubble.left.and.bubble.right.fill") }
                .disabled(!chatAvailable)
                .accessibilityLabel("Chat der Baustelle")
                .accessibilityIdentifier("todayJobChatButton")
            Button { perform(.open) } label: { Label("Bauakte", systemImage: "folder.fill") }
                .accessibilityLabel("Bauakte öffnen")
                .accessibilityIdentifier("todayJobOpenBauakte")
        }
        .buttonStyle(ActionTileStyle())
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
        if chatAvailable {
            Button("Chat der Baustelle", systemImage: "bubble.left.and.bubble.right") { perform(.chat) }
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
                if !job.resources.isEmpty {
                    Label(job.resources.joined(separator: ", "), systemImage: "truck.box")
                        .labelStyle(TightLabelStyle())
                        .font(Typo.subhead)
                        .foregroundStyle(Theme.textSecondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
        }
        .accessibilityElement(children: .combine)
    }

    /// Letzte Nachricht im Chat der Baustelle; Tipp öffnet die Unterhaltung im Reiter „Chat“.
    private func chatLine(_ chat: ChatConversation) -> some View {
        Button { perform(.chat) } label: {
            HStack(alignment: .firstTextBaseline, spacing: Space.m) {
                Image(systemName: chat.unreadCount > 0 ? "bubble.left.fill" : "bubble.left")
                    .font(.subheadline.weight(SymbolWeight.content))
                    .foregroundStyle(chat.unreadCount > 0 ? Theme.accent : Theme.textSecondary)
                    .frame(width: 22)
                    .accessibilityHidden(true)
                VStack(alignment: .leading, spacing: 2) {
                    HStack(spacing: Space.xs) {
                        Text(chat.unreadCount > 0 ? "Chat, \(chat.unreadCount) neu" : "Chat der Baustelle")
                            .font(Typo.labelStrong)
                            .foregroundStyle(chat.unreadCount > 0 ? Theme.accent : Theme.textSecondary)
                        if let at = chat.lastMessageAt {
                            Text(at.shortStamp).font(Typo.label).foregroundStyle(Theme.textSecondary)
                        }
                    }
                    Text([chat.lastMessageSender.map { "\($0):" }, chat.lastMessageText].compactMap { $0 }.joined(separator: " "))
                        .font(Typo.subhead)
                        .foregroundStyle(Theme.textPrimary)
                        .lineLimit(2)
                        .multilineTextAlignment(.leading)
                        .fixedSize(horizontal: false, vertical: true)
                }
                Spacer(minLength: 0)
                Chevron()
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityElement(children: .combine)
        .accessibilityHint("Öffnet den Chat der Baustelle")
        .accessibilityIdentifier("todayJobLastMessage")
    }

    private func line(symbol: String, text: String, detail: String? = nil, caption: String? = nil, limit: Int? = nil,
                      strong: Bool = false) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: Space.m) {
            Image(systemName: symbol)
                .font(.subheadline.weight(SymbolWeight.content))
                .foregroundStyle(Theme.textSecondary)
                .frame(width: 22)
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 2) {
                if let caption {
                    Text(caption)
                        .font(Typo.labelStrong)
                        .foregroundStyle(Theme.textSecondary)
                }
                Text(text)
                    .font(strong ? Typo.subheadStrong : Typo.subhead)
                    .foregroundStyle(Theme.textPrimary)
                    .lineLimit(limit)
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
            HStack(spacing: Space.s) {
                ForEach(0..<4, id: \.self) { _ in
                    RoundedRectangle(cornerRadius: Radius.l, style: .continuous).fill(Theme.fill).frame(height: 54)
                }
            }
        }
        .padding(Space.l)
        .card()
        .pulsing()
    }
}

/// Weiterer Einsatz des Tages als ruhige Listenzeile: Uhrzeit links, Bauvorhaben, Tätigkeit mit Kolonne, Adresse.
/// Tipp öffnet die Bauakte; Route, Anrufen und Erfassen per Wischen bzw. Kontextmenü.
struct TodayJobRow: View {
    let job: TodayJob
    var phase: TodayGlance.Phase = .unknown
    var distance: String?
    @Environment(\.dynamicTypeSize) private var typeSize

    var body: some View {
        let layout = typeSize.isAccessibilitySize ? AnyLayout(VStackLayout(alignment: .leading, spacing: Space.xs))
                                                  : AnyLayout(HStackLayout(alignment: .firstTextBaseline, spacing: Space.m))
        HStack(spacing: Space.s) {
            layout {
                VStack(alignment: .leading, spacing: 2) {
                    Text(job.startText ?? "ganztägig")
                        .font(Typo.headline.monospacedDigit())
                        .foregroundStyle(phase == .finished ? Theme.textSecondary : Theme.textPrimary)
                    if phase == .running {
                        Text("Jetzt").font(Typo.labelStrong).foregroundStyle(Theme.accent)
                    }
                }
                .frame(minWidth: 56, alignment: .leading)
                VStack(alignment: .leading, spacing: 2) {
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
                            .lineLimit(1)
                    }
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            Chevron()
        }
        .padding(.vertical, Space.xxs)
        .contentShape(Rectangle())
        .accessibilityElement(children: .combine)
        .accessibilityHint("Öffnet die Bauakte")
        .accessibilityIdentifier("todayJob-\(job.title)")
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
