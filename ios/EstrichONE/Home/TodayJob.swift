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
    let route: URL?
    let status: BauakteStatus?

    var timeText: String {
        switch (startText, endText) {
        case let (start?, end?): "\(start) – \(end)"
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
        func links(_ site: DispatchBoard.Site?, _ bauakte: Bauakte?, address: String?) -> (String?, URL?, URL?) {
            let name = site?.siteContactName ?? bauakte?.siteContactName
            let phone = ContactLink.phone(site?.siteContactPhone ?? bauakte?.siteContactPhone)
            let route = site?.navigationUrl.flatMap(URL.init(string:))
                ?? bauakte.flatMap(MapsLink.url(for:))
                ?? MapsLink.url(address: address)
            return (name, phone, route)
        }

        if !assignments.isEmpty {
            return assignments.map { assignment in
                let site = sites[assignment.bauakteId]
                let job = site?.jobs.first { $0.assignmentId == assignment.id }
                let bauakte = bauakten[assignment.bauakteId]
                let address = assignment.bauakteAddress ?? site?.address
                let (name, phone, route) = links(site, bauakte, address: address)
                return TodayJob(
                    id: assignment.id, bauakteId: assignment.bauakteId,
                    startText: assignment.startText ?? job?.startText, endText: job?.endText,
                    activity: assignment.activity, number: assignment.bauakteNumber, title: assignment.bauakteTitle,
                    address: address, crewName: assignment.crewName ?? job?.crewName,
                    crewColor: color(job, crewName: assignment.crewName ?? job?.crewName),
                    people: assignment.people.map(\.name), resources: assignment.resources.map(\.name),
                    warnings: assignment.warnings + (site?.warnings ?? []).filter { !assignment.warnings.contains($0) },
                    notes: assignment.notes ?? job?.notes, contactName: name, phone: phone, route: route, status: bauakte?.status)
            }
        }
        // Ohne eigene Einsätze: Baustellen, auf denen ich (z. B. über meine Kolonne) heute eingeplant bin.
        let mine = (board?.sortedSites ?? []).filter { board?.mySiteIds.contains($0.bauakteId) == true }
        return mine.flatMap { site -> [TodayJob] in
            let bauakte = bauakten[site.bauakteId]
            let (name, phone, route) = links(site, bauakte, address: site.address)
            return site.jobs.map { job in
                TodayJob(
                    id: job.assignmentId, bauakteId: site.bauakteId, startText: job.startText ?? site.startText, endText: job.endText,
                    activity: job.activity, number: site.number, title: site.title, address: site.address,
                    crewName: job.crewName, crewColor: color(job, crewName: job.crewName),
                    people: site.people.map(\.name), resources: site.vehicles.map(\.name), warnings: site.warnings, notes: job.notes,
                    contactName: name, phone: phone, route: route, status: bauakte?.status)
            }
        }
    }
}

/// Karte eines Einsatzes – das Wichtigste des Tages: Uhrzeit, Baustelle, Adresse mit Route, Ansprechpartner mit Anruf,
/// Tätigkeit, Kolonne, Notiz und Hinweise; darunter die Handgriffe vor Ort (Foto, Tagebuch, Mangel, Akte).
struct TodayJobCard: View {
    enum Action {
        case open, photo, diary, defect
    }

    let job: TodayJob
    /// Bauakte liegt auf dem Gerät (sonst nur Öffnen, das lädt sie nach).
    var canCapture = true
    let perform: (Action) -> Void
    @Environment(\.dynamicTypeSize) private var typeSize

    var body: some View {
        VStack(alignment: .leading, spacing: Space.m) {
            Button { perform(.open) } label: { heading }
                .buttonStyle(.plain)
                .accessibilityIdentifier("todayJob-\(job.title)")

            InsetDivider()

            VStack(alignment: .leading, spacing: Space.m) {
                if let address = job.address {
                    lineWithAction(symbol: "mappin.and.ellipse", text: address, action: job.route.map { (url: $0, title: "Route starten", icon: "arrow.triangle.turn.up.right.diamond.fill") })
                }
                if let contact = job.contactName {
                    lineWithAction(symbol: "person.crop.circle", text: "Vor Ort: \(contact)", action: job.phone.map { (url: $0, title: "Anrufen", icon: "phone.fill") })
                } else if let phone = job.phone {
                    lineWithAction(symbol: "phone", text: "Ansprechpartner vor Ort", action: (url: phone, title: "Anrufen", icon: "phone.fill"))
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

            HStack(spacing: Space.s) {
                Button { perform(.photo) } label: { Label("Foto", systemImage: "camera.fill") }
                    .buttonStyle(ActionTileStyle(rank: .primary))
                    .disabled(!canCapture)
                    .accessibilityIdentifier("todayJobPhoto")
                Button { perform(.diary) } label: { Label("Tagebuch", systemImage: "book") }
                    .buttonStyle(ActionTileStyle())
                    .disabled(!canCapture)
                Button { perform(.defect) } label: { Label("Mangel", systemImage: "exclamationmark.triangle") }
                    .buttonStyle(ActionTileStyle())
                    .disabled(!canCapture)
                Button { perform(.open) } label: { Label("Akte", systemImage: "folder") }
                    .buttonStyle(ActionTileStyle())
                    .accessibilityIdentifier("todayJobOpen")
            }
        }
        .padding(Space.l)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card()
    }

    private var heading: some View {
        VStack(alignment: .leading, spacing: Space.xs) {
            // Sehr große Schrift: Stand unter die Uhrzeit statt daneben (nichts wird abgeschnitten).
            let layout = typeSize.isAccessibilitySize ? AnyLayout(VStackLayout(alignment: .leading, spacing: Space.xs))
                                                      : AnyLayout(HStackLayout(alignment: .firstTextBaseline, spacing: Space.s))
            layout {
                Text(job.timeText)
                    .font(Typo.time)
                    .foregroundStyle(Theme.textPrimary)
                if !typeSize.isAccessibilitySize { Spacer(minLength: Space.s) }
                if let status = job.status { StatusLabel(status: status) }
            }
            HStack(alignment: .firstTextBaseline, spacing: Space.s) {
                Text(job.title)
                    .font(.title3.weight(.semibold))
                    .foregroundStyle(Theme.textPrimary)
                    .multilineTextAlignment(.leading)
                    .fixedSize(horizontal: false, vertical: true)
                Spacer(minLength: Space.s)
                Chevron()
            }
            if let number = job.number {
                Text(number)
                    .font(Typo.subhead.monospacedDigit())
                    .foregroundStyle(Theme.textSecondary)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .contentShape(Rectangle())
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

    private func line(symbol: String, text: String, strong: Bool = false) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: Space.m) {
            Image(systemName: symbol)
                .font(.subheadline.weight(SymbolWeight.content))
                .foregroundStyle(Theme.textSecondary)
                .frame(width: 22)
                .accessibilityHidden(true)
            Text(text)
                .font(strong ? Typo.subheadStrong : Typo.subhead)
                .foregroundStyle(strong ? Theme.textPrimary : Theme.textSecondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    private func lineWithAction(symbol: String, text: String, action: (url: URL, title: String, icon: String)?) -> some View {
        HStack(alignment: .center, spacing: Space.m) {
            Image(systemName: symbol)
                .font(.subheadline.weight(SymbolWeight.content))
                .foregroundStyle(Theme.textSecondary)
                .frame(width: 22)
                .accessibilityHidden(true)
            Text(text)
                .font(Typo.subhead)
                .foregroundStyle(Theme.textPrimary)
                .fixedSize(horizontal: false, vertical: true)
                .frame(maxWidth: .infinity, alignment: .leading)
            if let action {
                Link(destination: action.url) { Label(action.title, systemImage: action.icon) }
                    .buttonStyle(IconButtonStyle())
                    .accessibilityLabel(action.title)
            }
        }
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
                    RoundedRectangle(cornerRadius: Radius.l, style: .continuous).fill(Theme.fill).frame(height: 64)
                }
            }
        }
        .padding(Space.l)
        .card()
        .pulsing()
    }
}
