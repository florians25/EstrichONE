import EstrichCore
import SwiftUI

/// Alles, was zu einer Bauakte auf dem Gerät liegt – in einem Ladevorgang aus der lokalen Ablage (offline sofort).
struct BauakteContent {
    var bauakte: Bauakte
    var customer: Contact?
    /// Fotos und Videos, neueste zuerst.
    var photos: [FileItem] = []
    /// Dokumente, neueste zuerst.
    var documents: [FileItem] = []
    var notes: [Note] = []
    var workItems: [WorkItem] = []
    var diary: [DiaryEntry] = []
    var inspections: [Inspection] = []
    var acceptances: [Acceptance] = []
    /// Räume mit Fläche und Estrichaufbau.
    var rooms: [Room] = []
    /// Beteiligte (Bauherr, Bauleiter, Architekt, Lieferant …), gepflegt im Büro.
    var parties: [BauakteParty] = []
    /// Einsätze aus der Planung ab heute (zuletzt geladener Stand).
    var assignments: [Assignment] = []
    /// Namen für Verfasser und Zuständige.
    var names: [UUID: String] = [:]

    var openItems: [WorkItem] { workItems.filter { !$0.isDone } }
    var openDefects: Int { openItems.filter { $0.kind == .defect }.count }
    var pinnedNotes: [Note] { notes.filter(\.isPinned) }
    /// Neueste Erstbegehung (Korrekturen sind neuere Fassungen).
    var latestInitialInspection: Inspection? { inspections.first { $0.kind == .initialInspection } }
    var measurements: [Inspection] { inspections.filter { $0.kind == .cmMeasurement } }
    var todayAssignments: [Assignment] { assignments.filter { $0.day == CalendarDay.string(.now) } }

    @MainActor
    static func load(_ id: UUID, model: AppModel) async -> BauakteContent? {
        guard let service = model.active?.service, let bauakte = try? await service.bauakte(id) else { return nil }
        var content = BauakteContent(bauakte: bauakte)
        let files = (try? await service.files(of: id)) ?? []
        let newest: (FileItem, FileItem) -> Bool = { ($0.capturedAt ?? $0.createdAt ?? .distantPast) > ($1.capturedAt ?? $1.createdAt ?? .distantPast) }
        content.photos = files.filter { $0.isPhoto || $0.isVideo }.sorted(by: newest)
        content.documents = files.filter(\.isDocument).sorted(by: newest)
        content.notes = (try? await service.notes(of: id)) ?? []
        content.workItems = (try? await service.workItems(of: id)) ?? []
        content.diary = (try? await service.diary(of: id)) ?? []
        content.inspections = (try? await service.inspections(of: id)) ?? []
        content.acceptances = (try? await service.acceptances(of: id)) ?? []
        content.rooms = (try? await service.rooms(of: id)) ?? []
        content.parties = (try? await service.parties(of: id)) ?? []
        content.customer = if let customerId = bauakte.customerId { try? await service.contact(customerId) } else { nil }
        if let store = model.active?.sync?.store {
            let today = CalendarDay.string(.now)
            content.assignments = (await MyPlanning(store: store).cached())?.range.assignments
                .filter { $0.bauakteId == id && $0.day >= today }
                .sorted { ($0.day, $0.startTime ?? "") < ($1.day, $1.startTime ?? "") } ?? []
        }
        let directory = (try? await service.cachedDirectory()) ?? []
        content.names = Dictionary(directory.map { ($0.id, $0.displayName) }, uniquingKeysWith: { first, _ in first })
        return content
    }

    // MARK: Zusammenfassungen

    /// Nächster Einsatz: „Heute ab 07:00 · Estrich einbringen OG“ und „Kolonne Huber · Max Huber, …“.
    var nextAssignmentText: (title: String, subtitle: String?)? {
        guard let next = assignments.first, let date = CalendarDay.date(next.day) else { return nil }
        let calendar = Calendar.current
        let day = calendar.isDateInToday(date) ? "Heute" : calendar.isDateInTomorrow(date) ? "Morgen"
            : date.formatted(.dateTime.weekday(.wide).day().month(.abbreviated))
        let title = [day + (next.startText.map { " ab \($0)" } ?? ""), next.activity].joined(separator: " · ")
        let team = next.people.map(\.name) + next.resources.map(\.name)
        let subtitle = [next.crewName, team.isEmpty ? nil : team.joined(separator: ", ")].compactMap { $0 }.joined(separator: " · ")
        return (title, subtitle.isEmpty ? nil : subtitle)
    }

    /// Letzte Aktivität: Fotos je Tag zusammengefasst, Dokumente, Tagebuch, Notizen, Prüfungen, Abnahmen.
    var activity: [ActivityEntry] {
        var list: [ActivityEntry] = []
        let calendar = Calendar.current
        let photosByDay = Dictionary(grouping: photos.filter { ($0.capturedAt ?? $0.createdAt) != nil }) {
            calendar.startOfDay(for: $0.capturedAt ?? $0.createdAt ?? .distantPast)
        }
        for (day, files) in photosByDay {
            let videos = files.filter(\.isVideo).count
            let pictures = files.count - videos
            let text = [pictures > 0 ? (pictures == 1 ? "1 Foto" : "\(pictures) Fotos") : nil,
                        videos > 0 ? (videos == 1 ? "1 Video" : "\(videos) Videos") : nil].compactMap { $0 }.joined(separator: " und ")
            let latest = files.compactMap { $0.capturedAt ?? $0.createdAt }.max() ?? day
            list.append(ActivityEntry(id: "photos-\(day.timeIntervalSince1970)", symbol: "camera", text: "\(text) aufgenommen", date: latest))
        }
        for document in documents {
            if let date = document.createdAt ?? document.capturedAt {
                list.append(ActivityEntry(id: "doc-\(document.id)", symbol: "doc.text", text: "Dokument „\(document.displayName)“", date: date))
            }
        }
        for entry in diary {
            if let date = entry.createdAt ?? CalendarDay.date(entry.day) {
                let first = entry.text.split(separator: "\n").first.map(String.init) ?? entry.text
                list.append(ActivityEntry(id: "diary-\(entry.id)", symbol: "book", text: "Bautagebuch: \(first)", date: date))
            }
        }
        for note in notes {
            if let date = note.createdAt {
                list.append(ActivityEntry(id: "note-\(note.id)", symbol: "note.text", text: "Notiz: \(note.text.compactText ?? "")", date: date))
            }
        }
        for inspection in inspections {
            if let date = inspection.performedAt {
                list.append(ActivityEntry(id: "insp-\(inspection.id)", symbol: inspection.kind == .cmMeasurement ? "drop" : "checklist",
                                          text: inspection.kind.title, date: date))
            }
        }
        for acceptance in acceptances {
            if let date = acceptance.performedAt {
                list.append(ActivityEntry(id: "acc-\(acceptance.id)", symbol: "signature", text: "Abnahme: \(acceptance.result.title)", date: date))
            }
        }
        return list.sorted { $0.date > $1.date }
    }
}

/// Eintrag der Zeitleiste „Letzte Aktivität“.
struct ActivityEntry: Identifiable {
    let id: String
    let symbol: String
    let text: String
    let date: Date
}

/// Bereiche einer Bauakte – alle Daten, verständlich gegliedert.
enum BauakteArea: String, CaseIterable, Identifiable, Hashable {
    case media, work, diary, rooms, protocols, project, contacts, notes

    var id: String { rawValue }

    var title: String {
        switch self {
        case .media: "Fotos & Dokumente"
        case .work: "Aufgaben & Mängel"
        case .diary: "Bautagebuch"
        case .rooms: "Räume & Flächen"
        case .protocols: "Protokolle"
        case .project: "Basisdaten"
        case .contacts: "Kontakte"
        case .notes: "Notizen & Verlauf"
        }
    }

    var symbol: String {
        switch self {
        case .media: "photo.on.rectangle"
        case .work: "checklist"
        case .diary: "book"
        case .rooms: "square.split.2x2"
        case .protocols: "doc.text"
        case .project: "list.bullet.rectangle"
        case .contacts: "person.2"
        case .notes: "note.text"
        }
    }

    /// Screenshot-Modus: „bereich-fotos“ usw.
    static func shot(_ scene: String?) -> BauakteArea? {
        switch scene {
        case "bereich-fotos": .media
        case "bereich-aufgaben": .work
        case "bereich-tagebuch": .diary
        case "bereich-raeume": .rooms
        case "bereich-protokolle": .protocols
        case "bereich-basisdaten": .project
        case "bereich-kontakte": .contacts
        case "bereich-notizen": .notes
        default: nil
        }
    }

    /// Kurzinfo je Bereich – man sieht ohne Öffnen, was drin ist.
    func summary(_ content: BauakteContent, projectCount: Int) -> (text: String, isMissing: Bool) {
        switch self {
        case .media:
            let photos = content.photos.count
            let documents = content.documents.count
            return ("\(photos) \(photos == 1 ? "Foto" : "Fotos") · \(documents) \(documents == 1 ? "Dokument" : "Dokumente")", false)
        case .work:
            let open = content.openItems.count
            if content.workItems.isEmpty { return ("keine", false) }
            if open == 0 { return ("alles erledigt", false) }
            return (content.openDefects > 0 ? "\(open) offen, davon \(content.openDefects) \(content.openDefects == 1 ? "Mangel" : "Mängel")" : "\(open) offen",
                    false)
        case .diary:
            guard let last = content.diary.first.flatMap({ CalendarDay.date($0.day) }) else { return ("noch kein Eintrag", false) }
            let calendar = Calendar.current
            let when = calendar.isDateInToday(last) ? "heute" : calendar.isDateInYesterday(last) ? "gestern"
                : last.formatted(.dateTime.day().month(.abbreviated))
            return ("\(content.diary.count) \(content.diary.count == 1 ? "Eintrag" : "Einträge") · zuletzt \(when)", false)
        case .rooms:
            guard !content.rooms.isEmpty else { return ("noch keine Räume", false) }
            let totals = RoomTotals(content.rooms)
            let area = totals.area.formatted(.number.precision(.fractionLength(0...1)))
            let done = totals.laid + totals.ready
            let progress = done == 0 ? nil : done == totals.count ? "alle eingebracht" : "\(done) eingebracht"
            return (["\(totals.count) \(totals.count == 1 ? "Raum" : "Räume")", "\(area) m²", progress].compactMap { $0 }.joined(separator: " · "), false)
        case .protocols:
            let inspection = content.latestInitialInspection == nil ? "Erstbegehung offen" : "Erstbegehung erfasst"
            let acceptance = content.acceptances.isEmpty ? "keine Abnahme" : "\(content.acceptances.count) Abnahme"
            return ("\(inspection) · \(acceptance)", false)
        case .project:
            return projectCount == 0 ? ("Angaben fehlen", true) : ("\(projectCount) Angaben", false)
        case .contacts:
            let parties = content.parties.isEmpty ? nil : "\(content.parties.count) \(content.parties.count == 1 ? "Beteiligter" : "Beteiligte")"
            guard let customer = content.customer else { return ([parties, "Kunde fehlt"].compactMap { $0 }.joined(separator: " · "), true) }
            return ([customer.name, parties].compactMap { $0 }.joined(separator: " · "), false)
        case .notes:
            return (content.notes.isEmpty ? "keine Notizen" : "\(content.notes.count) \(content.notes.count == 1 ? "Notiz" : "Notizen")", false)
        }
    }
}
