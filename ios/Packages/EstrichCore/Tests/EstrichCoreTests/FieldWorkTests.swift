import Foundation
#if canImport(CryptoKit)
import CryptoKit
#else
import Crypto
#endif
import Testing
@testable import EstrichCore

@Suite("Einsatz vor Ort: Entwürfe, Übertragungsstatus, Fotoserie, Offline-Paket, Ankunft, Abnahme", .serialized)
struct FieldWorkTests {
    private func makeStore() throws -> (LocalStore, FileVault) {
        let directory = FileManager.default.temporaryDirectory.appending(path: "field-\(UUID().uuidString)")
        let key = SymmetricKey(size: .bits256)
        let store = LocalStore(database: try EncryptedDatabase(url: directory.appending(path: "data.sealed"), key: key, migrations: LocalStore.schema))
        return (store, FileVault(directory: directory.appending(path: "files"), key: key))
    }

    private func file(kind: String, size: Int64 = 1000, stored: Bool = true, created: String = "2026-11-20T08:00:00Z", id: UUID = UUID()) -> FileItem {
        FileItem(LocalEntity(type: .file, id: id, data: .object([
            "kind": .string(kind), "sizeBytes": .number(Double(size)), "state": .string(stored ? "stored" : "awaitingUpload"),
            "createdAt": .string(created), "capturedAt": .string(created),
            "contentType": .string(kind == "document" ? "application/pdf" : "image/jpeg"),
        ]), syncState: .synced))
    }

    private func assignment(day: String, start: String?, bauakte: UUID) throws -> Assignment {
        let startJSON = start.map { "\"\($0)\"" } ?? "null"
        let json = """
        {"id":"\(UUID().uuidString)","day":"\(day)","startTime":\(startJSON),"activity":"Estrich","bauakteId":"\(bauakte.uuidString)",
         "bauakteNumber":null,"bauakteTitle":"Halle","bauakteAddress":null,"crewName":null,"people":[],"resources":[],"notes":null,"warnings":[]}
        """
        return try JSONCoding.decoder.decode(Assignment.self, from: Data(json.utf8))
    }

    private var calendar: Calendar {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = TimeZone(identifier: "Europe/Berlin")!
        return calendar
    }

    private func date(_ text: String) -> Date { APIDate.parse(text)! }

    // MARK: Entwürfe

    @Test("Entwurf bleibt über einen Neustart erhalten, je Art und Bauakte getrennt, und verschwindet nach dem Verwerfen")
    func draftsSurviveRestart() async throws {
        let (store, _) = try makeStore()
        let drafts = DraftStore(store: store)
        let first = UUID(), second = UUID()
        await drafts.save(DiaryDraft(day: "2026-11-24", text: "Estrich im EG eingebracht", weather: "rain", temperature: 6, workers: 3),
                          kind: DraftKind.diary, scope: first)
        await drafts.save(NoteDraft(text: "Schlüssel beim Nachbarn"), kind: DraftKind.note, scope: first)

        // Neue Instanz wie nach einem Neustart der App.
        let restored = await DraftStore(store: store).load(DiaryDraft.self, kind: DraftKind.diary, scope: first)
        #expect(restored?.value == DiaryDraft(day: "2026-11-24", text: "Estrich im EG eingebracht", weather: "rain", temperature: 6, workers: 3))
        #expect(await drafts.load(NoteDraft.self, kind: DraftKind.note, scope: first)?.value.text == "Schlüssel beim Nachbarn")
        #expect(await drafts.load(DiaryDraft.self, kind: DraftKind.diary, scope: second) == nil)

        await drafts.discard(kind: DraftKind.diary, scope: first)
        #expect(await drafts.load(DiaryDraft.self, kind: DraftKind.diary, scope: first) == nil)
        #expect(await drafts.load(NoteDraft.self, kind: DraftKind.note, scope: first) != nil)
    }

    @Test("Alte Entwürfe werden nicht mehr angeboten; Unterschriften der Abnahme bleiben im Entwurf")
    func draftsExpireAndKeepSignatures() async throws {
        let (store, _) = try makeStore()
        let bauakte = UUID()
        let signature = Data([0x89, 0x50, 0x4E, 0x47, 1, 2, 3])
        let draft = AcceptanceDraft(clientName: "Familie Schneider", contractorName: "Max Huber", location: "Landshut", scope: "CT-C25-F4",
                                    result: "accepted", notes: "", defects: [.init(title: "Fuge Bad", dueDate: "2026-12-08")],
                                    photoIds: [UUID()], clientSignature: signature, contractorSignature: nil)
        let written = DraftStore(store: store, now: { APIDate.parse("2026-11-01T08:00:00Z")! })
        await written.save(draft, kind: DraftKind.acceptance, scope: bauakte)

        let nextDay = DraftStore(store: store, now: { APIDate.parse("2026-11-02T08:00:00Z")! })
        #expect(await nextDay.load(AcceptanceDraft.self, kind: DraftKind.acceptance, scope: bauakte)?.value == draft)
        let weeksLater = DraftStore(store: store, now: { APIDate.parse("2026-11-20T08:00:00Z")! })
        #expect(await weeksLater.load(AcceptanceDraft.self, kind: DraftKind.acceptance, scope: bauakte) == nil)

        #expect(!draft.isEmpty)
        #expect(AcceptanceDraft(clientName: "Familie Schneider", contractorName: "Max Huber", location: "Landshut", scope: " ", result: nil,
                                notes: "", defects: [], photoIds: [], clientSignature: nil, contractorSignature: nil).isEmpty)
        #expect(DiaryDraft(day: "2026-11-24", text: "  \n").isEmpty)
        #expect(WorkItemDraft(kind: "defect", title: "", details: "Riss").isEmpty == false)
    }

    // MARK: Übertragungsstatus

    @Test("Übertragungsstatus in Worten: Einzahl, Mehrzahl, mehrere Arten")
    func transferTexts() {
        #expect(TransferQueue().waitingText == nil)
        #expect(TransferQueue(photos: 3).waitingText == "3 Fotos warten auf Übertragung")
        #expect(TransferQueue(photos: 1).waitingText == "1 Foto wartet auf Übertragung")
        #expect(TransferQueue(photos: 3, entries: 2).waitingText == "3 Fotos und 2 Einträge warten auf Übertragung")
        #expect(TransferQueue(photos: 3, videos: 1, documents: 2, entries: 1).summary == "3 Fotos, 1 Video, 2 Dokumente und 1 Eintrag")
        #expect(TransferQueue(documents: 1).sendingText == "1 Dokument wird übertragen")
        #expect(TransferQueue(photos: 2).sendingText == "2 Fotos werden übertragen")
        #expect(TransferQueue(failedFiles: 1).problemText == "1 Datei konnte nicht übertragen werden")
        #expect(TransferQueue(failedFiles: 2, rejectedEntries: 1).problemText == "2 Dateien konnten nicht übertragen werden, 1 Eintrag wurde nicht übernommen")
        #expect(TransferQueue(photos: 1).problemText == nil)
    }

    @Test("Übertragungsstatus zählt echte Warteschlange: Fotos, Dokument, Tagebuch; Ankündigungen zählen nicht doppelt")
    func transferCountsFromStore() async throws {
        let (store, files) = try makeStore()
        let service = BauakteService(store: store, files: files, userId: UUID.v7())
        let bauakte = try await service.create(NewBauakte(title: "Halle"))
        for _ in 0..<3 { try await service.addPhoto(Data([0xFF, 0xD8, 1, 2]), to: bauakte, capturedAt: .now, latitude: 48.54, longitude: 12.13) }
        try await service.addDocument(Data("%PDF".utf8), title: "Lieferschein", category: .other, to: bauakte)
        try await service.addDiaryEntry(NewDiaryEntry(day: "2026-11-24", text: "Estrich eingebracht"), to: bauakte)

        let queue = TransferQueue(uploads: try await store.uploads(), operations: try await store.operations())
        #expect(queue.photos == 3 && queue.documents == 1 && queue.videos == 0)
        // Bauakte anlegen und Tagebucheintrag; die vier Ankündigungen der Dateien stecken in den Dateien.
        #expect(queue.entries == 2)
        #expect(queue.waitingText == "3 Fotos, 1 Dokument und 2 Einträge warten auf Übertragung")
        #expect(!queue.hasProblems)

        // Standort und Aufnahmezeit stehen in der Ankündigung des Fotos.
        let announce = try await store.operations().first { $0.type == "file.announce" }
        #expect(announce?.payload["latitude"]?.number == 48.54 && announce?.payload["longitude"]?.number == 12.13)
        #expect(announce?.payload["capturedAt"]?.string != nil)
    }

    @Test("Fehlgeschlagene Uploads: verständliche Erklärung und ob erneut senden hilft")
    func uploadFailures() {
        #expect(UploadFailure.canRetry(code: nil) && UploadFailure.canRetry(code: "CHECKSUM_MISMATCH"))
        #expect(!UploadFailure.canRetry(code: "LOCAL_FILE_MISSING") && !UploadFailure.canRetry(code: "FILE_TOO_LARGE"))
        #expect(UploadFailure.message(code: "LOCAL_FILE_MISSING").contains("erneut aufnehmen"))
        #expect(UploadFailure.message(code: nil).contains("erneut senden"))
        #expect(TransferQueue.kind(ofContentType: "image/JPEG") == .photo && TransferQueue.kind(ofContentType: "video/mp4") == .video)
        #expect(TransferQueue.kind(ofContentType: "application/pdf") == .document)
    }

    // MARK: Fotoserie

    @Test("Fotoserie: Art, Raum und Beschriftung ergeben die Beschriftung der Fotos")
    func photoSeriesCaption() {
        #expect(PhotoSeries().caption == nil && PhotoSeries().isEmpty)
        #expect(PhotoSeries(phase: .installation, room: " Bad OG ", note: "Randdämmstreifen").caption == "Einbau, Bad OG, Randdämmstreifen")
        #expect(PhotoSeries(phase: .defect).caption == "Mangel")
        #expect(PhotoSeries(room: "Flur").caption == "Flur")
        #expect(PhotoSeries.phase(ofCaption: "Einbau, Bad OG") == .installation)
        #expect(PhotoSeries.phase(ofCaption: "Nachher") == .after)
        #expect(PhotoSeries.phase(ofCaption: "Bad OG, Einbau") == nil && PhotoSeries.phase(ofCaption: nil) == nil)
        #expect(PhotoPhase.allCases.map(\.title) == ["Vorher", "Einbau", "Nachher", "Mangel"])
        #expect(PhotoSeries.roomSuggestions([("Bad", "OG"), ("Bad", "OG"), ("Flur EG", "EG"), (" ", nil), ("Küche", nil)]) == ["Bad OG", "Flur EG", "Küche"])
    }

    // MARK: Offline-Paket

    @Test("Offline-Paket: heutige und kommende Einsätze, heute zuerst, vergangene nicht; von Hand Gewählte zuletzt")
    func offlineBauakten() throws {
        let today = UUID(), tomorrow = UUID(), past = UUID(), pinned = UUID(), crew = UUID()
        let assignments = [
            try assignment(day: "2026-11-25", start: "07:00:00", bauakte: tomorrow),
            try assignment(day: "2026-11-24", start: "13:00:00", bauakte: today),
            try assignment(day: "2026-11-23", start: "07:00:00", bauakte: past),
            try assignment(day: "2026-11-26", start: nil, bauakte: today),
        ]
        #expect(OfflinePlan.bauakteIds(assignments: assignments, today: "2026-11-24", todayExtra: [crew], pinned: [pinned, tomorrow])
                == [crew, today, tomorrow, pinned])
        #expect(OfflinePlan.bauakteIds(assignments: [], today: "2026-11-24").isEmpty)
    }

    @Test("Offline-Paket: Dokumente bis zur Größengrenze und die neuesten Fotos, nur was schon beim Server liegt, mit Obergrenze")
    func offlineFiles() {
        let plan = file(kind: "document", size: 2_000_000)
        let huge = file(kind: "document", size: OfflinePlan.maxFileBytes + 1)
        let unsent = file(kind: "document", stored: false)
        let photos = (0..<45).map { file(kind: "photo", size: 1_500_000, created: String(format: "2026-11-20T08:%02d:00Z", $0)) }
        let wanted = OfflinePlan.wanted(files: [huge, plan, unsent] + photos)
        #expect(wanted.filter(\.isDocument).map(\.id) == [plan.id])
        #expect(wanted.filter(\.isPhoto).count == OfflinePlan.photosPerBauakte)
        // Die neuesten Fotos zuerst; die ältesten fünf bleiben draußen.
        #expect(wanted.filter(\.isPhoto).first?.id == photos.last?.id)
        #expect(!wanted.contains { $0.id == photos[0].id })

        #expect(OfflinePlan.cost(of: plan) == 2_000_000 && OfflinePlan.cost(of: photos[0]) == OfflinePlan.photoEstimateBytes)
        let downloads = OfflinePlan.downloads(wanted: wanted, present: [plan.id], usedBytes: 0)
        #expect(downloads.count == OfflinePlan.photosPerBauakte && !downloads.contains { $0.id == plan.id })
        // Obergrenze: Es passt nur noch ein Foto.
        #expect(OfflinePlan.downloads(wanted: wanted, present: [], usedBytes: 0, limit: OfflinePlan.photoEstimateBytes).count == 1)
    }

    @Test("Anzeige „Auf dem Gerät verfügbar“: vollständig, wird geladen, nur Daten")
    func offlineAvailability() {
        let plan = file(kind: "document"), slip = file(kind: "document"), huge = file(kind: "document", size: OfflinePlan.maxFileBytes + 1)
        let photo = file(kind: "photo")
        let files = [plan, slip, huge, photo]

        let complete = OfflineAvailability(files: files, present: [plan.id, slip.id, photo.id], isPlanned: true)
        #expect(complete.state == .complete && complete.title == "Auf dem Gerät verfügbar")
        #expect(complete.detail == "Daten und Kontakte, 2 Dokumente und 1 Foto. 1 großes Dokument wird bei Bedarf geladen.")

        let loading = OfflineAvailability(files: files, present: [plan.id], isPlanned: true)
        #expect(loading.state == .loading && loading.title == "Wird für unterwegs geladen")
        #expect(loading.detail == "1 von 3 Dateien geladen. Daten und Kontakte sind schon da.")

        let dataOnly = OfflineAvailability(files: files, present: [], isPlanned: false)
        #expect(dataOnly.state == .dataOnly && dataOnly.title == "Daten auf dem Gerät")

        let empty = OfflineAvailability(files: [], present: [], isPlanned: false)
        #expect(empty.state == .complete && empty.detail == "Daten und Kontakte")
    }

    @Test("Vorab geladene Dateien: verschlüsselt abgelegt, ohne eigene Aufnahmen zu überschreiben, aufgeräumt nach Ablauf")
    func offlineCopies() async throws {
        let (store, files) = try makeStore()
        let service = BauakteService(store: store, files: files, userId: UUID.v7())
        let bauakte = try await service.create(NewBauakte(title: "Halle"))
        let own = try await service.addPhoto(Data([0xFF, 0xD8, 9, 9]), to: bauakte, capturedAt: .now)
        let remote = UUID()

        #expect(!service.hasLocalCopy(of: remote))
        try service.storeOfflineCopy(Data("Plan".utf8), of: remote)
        try service.storeOfflineCopy(Data("anders".utf8), of: own)
        #expect(service.hasLocalCopy(of: remote) && service.localContent(of: remote) == Data("Plan".utf8))
        #expect(service.localContent(of: own) == Data([0xFF, 0xD8, 9, 9]))

        var index = OfflineIndex()
        let start = date("2026-11-20T08:00:00Z")
        index.add(remote, bauakteId: bauakte, bytes: 4, at: start)
        index.markPlanned([bauakte], at: start)
        #expect(index.contains(remote) && index.totalBytes == 4)
        await index.save(to: store)
        #expect(await OfflineIndex.load(from: store) == index)

        // Noch geplant bzw. erst kurz nicht mehr geplant: bleibt.
        let whilePlanned = index.removeExpired(planned: [bauakte], now: start.addingTimeInterval(30 * 86_400))
        index.markPlanned([bauakte], at: start)
        let shortlyAfter = index.removeExpired(planned: [], now: start.addingTimeInterval(86_400))
        #expect(whilePlanned.isEmpty && shortlyAfter.isEmpty)
        // Seit über drei Tagen nicht mehr geplant: wird entfernt.
        let expired = index.removeExpired(planned: [], now: start.addingTimeInterval(4 * 86_400))
        #expect(expired == [remote])
        #expect(!index.contains(remote) && index.lastPlanned.isEmpty)
        for id in expired { service.removeOfflineCopy(of: id) }
        #expect(!service.hasLocalCopy(of: remote) && service.hasLocalCopy(of: own))
    }

    // MARK: Ankunft und Abschluss

    @Test("Ankunft und Abschluss: Texte für das Bautagebuch mit Uhrzeit und Dauer")
    func siteVisitTexts() {
        let arrived = date("2026-11-24T06:42:00Z"), finished = date("2026-11-24T14:10:00Z")
        #expect(SiteVisitLog.arrivalText(at: arrived, calendar: calendar) == "Angekommen um 07:42 Uhr.")
        #expect(SiteVisitLog.finishText(at: finished, arrivedAt: arrived, calendar: calendar) == "Fertig um 15:10 Uhr. Dauer vor Ort: 7 Std. 28 Min.")
        #expect(SiteVisitLog.finishText(at: finished, arrivedAt: nil, calendar: calendar) == "Fertig um 15:10 Uhr.")
        // Ankunft an einem anderen Tag: keine Dauer.
        #expect(SiteVisitLog.finishText(at: finished, arrivedAt: date("2026-11-23T06:42:00Z"), calendar: calendar) == "Fertig um 15:10 Uhr.")
        #expect(SiteVisitLog.duration(from: arrived, to: arrived.addingTimeInterval(45 * 60)) == "45 Min.")
        #expect(SiteVisitLog.duration(from: arrived, to: arrived.addingTimeInterval(120 * 60)) == "2 Std.")
        #expect(SiteVisitLog.duration(from: arrived, to: arrived.addingTimeInterval(10)) == "1 Min.")
    }

    @Test("Ankunft und Abschluss werden je Bauakte und Tag gemerkt")
    func siteVisitStore() async throws {
        let (store, _) = try makeStore()
        let visits = SiteVisitStore(store: store)
        let bauakte = UUID()
        let arrived = date("2026-11-24T06:42:00Z"), finished = date("2026-11-24T14:10:00Z")
        #expect(await visits.visit(of: bauakte, day: "2026-11-24").isEmpty)

        await visits.markArrived(bauakte, at: arrived, calendar: calendar)
        let done = await visits.markFinished(bauakte, at: finished, calendar: calendar)
        #expect(done == SiteVisit(day: "2026-11-24", arrivedAt: arrived, finishedAt: finished))
        #expect(await SiteVisitStore(store: store).visit(of: bauakte, day: "2026-11-24") == done)
        #expect(await visits.visit(of: bauakte, day: "2026-11-25").isEmpty)
        #expect(await visits.visit(of: UUID(), day: "2026-11-24").isEmpty)
    }

    // MARK: Schnellzugriffe

    @Test("Schnellzugriff führt zum laufenden Einsatz, sonst zum nächsten, sonst zum letzten heute")
    func quickTarget() throws {
        let morning = UUID(), noon = UUID(), other = UUID()
        let assignments = [
            try assignment(day: "2026-11-24", start: "12:00:00", bauakte: noon),
            try assignment(day: "2026-11-24", start: "07:00:00", bauakte: morning),
            try assignment(day: "2026-11-25", start: "07:00:00", bauakte: other),
        ]
        func target(_ time: String) -> UUID? {
            QuickTarget.bauakteId(assignments: assignments, today: "2026-11-24", now: date("2026-11-24T\(time):00Z"), calendar: calendar)
        }
        // 06:30 Uhr Ortszeit: noch keiner läuft, der nächste ist der Morgeneinsatz.
        #expect(target("05:30") == morning)
        // 09:00 Uhr: der Morgeneinsatz läuft.
        #expect(target("08:00") == morning)
        // 14:00 Uhr: der Mittagseinsatz läuft, der Morgeneinsatz ist vorbei.
        #expect(target("13:00") == noon)
        #expect(QuickTarget.bauakteId(assignments: assignments, today: "2026-11-27", now: date("2026-11-27T08:00:00Z"), calendar: calendar) == nil)
        // Ohne Uhrzeit: der erste Einsatz des Tages.
        let untimed = [try assignment(day: "2026-11-24", start: nil, bauakte: other)]
        #expect(QuickTarget.bauakteId(assignments: untimed, today: "2026-11-24", now: date("2026-11-24T08:00:00Z"), calendar: calendar) == other)
    }

    // MARK: Abnahme

    @Test("Abnahme vor dem Abschluss: Zusammenfassung, Pflichtangaben und Hinweise")
    func acceptanceReview() {
        let png = Data([0x89, 0x50, 0x4E, 0x47])
        let empty = AcceptanceReview(NewAcceptance(), bauakteTitle: "EFH Schneider", bauakteNumber: "BA-2026-0042")
        #expect(!empty.canFinish && empty.missing == ["Ergebnis der Abnahme wählen"])
        #expect(empty.lines.map(\.title) == ["Bauvorhaben"] && empty.lines.first?.value == "BA-2026-0042, EFH Schneider")
        #expect(empty.hints.count == 4)

        let reserved = AcceptanceReview(NewAcceptance(result: .acceptedWithReservations, defects: [AcceptanceDefect(title: "  ")]),
                                        bauakteTitle: "EFH Schneider", bauakteNumber: nil)
        #expect(reserved.missing == ["Bei Vorbehalt oder Verweigerung mindestens einen Mangel oder eine Notiz erfassen"])

        let complete = AcceptanceReview(
            NewAcceptance(location: "Landshut", clientName: "Familie Schneider", contractorName: "Max Huber", scope: "CT-C25-F4, EG und OG",
                          result: .acceptedWithReservations, notes: " Restarbeiten am Freitag ", photoFileIds: [UUID(), UUID()],
                          defects: [AcceptanceDefect(title: "Fuge Bad nacharbeiten", dueDate: "2026-12-08"), AcceptanceDefect(title: "")],
                          clientSignature: png, contractorSignature: png),
            bauakteTitle: "EFH Schneider", bauakteNumber: "BA-2026-0042")
        #expect(complete.canFinish && complete.hints.isEmpty)
        #expect(complete.lines.map(\.title) == ["Bauvorhaben", "Ort", "Abgenommene Leistung", "Ergebnis", "Auftraggeber", "Auftragnehmer"])
        #expect(complete.defects.map(\.title) == ["Fuge Bad nacharbeiten"] && complete.defects.first?.due == "bis 08.12.2026")
        #expect(complete.notes == "Restarbeiten am Freitag" && complete.photoCount == 2)

        let unsigned = AcceptanceReview(NewAcceptance(clientName: "Familie Schneider", scope: "Estrich EG", result: .accepted),
                                        bauakteTitle: "EFH Schneider", bauakteNumber: nil)
        #expect(unsigned.canFinish && unsigned.hints.count == 2)
    }
}
