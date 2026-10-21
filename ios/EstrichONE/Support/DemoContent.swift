import EstrichCore
import Foundation
import UIKit

/// Beispieldaten des Demomodus (App-Prüfung durch Apple, Ausprobieren ohne eigenen Server): drei Bauakten mit Kunden,
/// Fotos, Lieferschein, Bautagebuch, Erstbegehung, Aufgaben, Einsätzen und einer Meldung. Alles entsteht lokal.
struct DemoContent: Sendable {
    struct Photo: Sendable {
        let id: String
        let bauakteId: String
        let caption: String
        let jpeg: Data
    }

    let photos: [Photo]
    let pdf: Data
    let pdfId = "0193a1b2-de00-7000-8000-00000000f001"

    static let userId = "0193a1b2-0000-7000-8000-00000000aaaa"
    static let efh = "0193a1b2-de00-7000-8000-0000000b0001"
    static let halle = "0193a1b2-de00-7000-8000-0000000b0002"
    static let praxis = "0193a1b2-de00-7000-8000-0000000b0003"
    static let ticketId = "0193a1b2-de00-7000-8000-00000000c001"

    @MainActor
    static func make() -> DemoContent {
        let photos = [
            Photo(id: "0193a1b2-de00-7000-8000-00000000a001", bauakteId: efh, caption: "Randdämmstreifen gesetzt",
                  jpeg: render("Randdämmung", color: UIColor(red: 0.55, green: 0.50, blue: 0.44, alpha: 1))),
            Photo(id: "0193a1b2-de00-7000-8000-00000000a002", bauakteId: efh, caption: "Fußbodenheizung vor dem Einbringen",
                  jpeg: render("Fußbodenheizung", color: UIColor(red: 0.70, green: 0.25, blue: 0.20, alpha: 1))),
            Photo(id: "0193a1b2-de00-7000-8000-00000000a003", bauakteId: efh, caption: "Estrich eingebracht, EG",
                  jpeg: render("Estrich EG", color: UIColor(red: 0.62, green: 0.62, blue: 0.60, alpha: 1))),
            Photo(id: "0193a1b2-de00-7000-8000-00000000a004", bauakteId: halle, caption: "Zufahrt Halle",
                  jpeg: render("Zufahrt", color: UIColor(red: 0.30, green: 0.45, blue: 0.30, alpha: 1))),
        ]
        let page = ScanPDFBuilder.preparePage(render("LIEFERSCHEIN 4711\nCT-C25-F4 · 18 m³", color: .white, text: .black))
        let pdf = page.flatMap { ScanPDFBuilder.makePDF(pages: [$0], title: "Lieferschein 4711", author: "Demo") } ?? Data()
        return DemoContent(photos: photos, pdf: pdf)
    }

    @MainActor
    private static func render(_ title: String, color: UIColor, text: UIColor = .white) -> Data {
        let size = CGSize(width: 1200, height: 900)
        let image = UIGraphicsImageRenderer(size: size).image { context in
            color.setFill()
            context.fill(CGRect(origin: .zero, size: size))
            UIColor.white.withAlphaComponent(0.08).setFill()
            for row in 0..<9 {
                context.fill(CGRect(x: 0, y: CGFloat(row) * 100, width: size.width, height: 40))
            }
            let style = NSMutableParagraphStyle()
            style.alignment = .center
            (title as NSString).draw(in: CGRect(x: 60, y: 360, width: size.width - 120, height: 300), withAttributes: [
                .font: UIFont.boldSystemFont(ofSize: 72), .foregroundColor: text, .paragraphStyle: style,
            ])
        }
        return image.jpegData(compressionQuality: 0.7) ?? Data()
    }

    // MARK: Datensätze im Format des Änderungsfeeds

    func changes(today: Date = .now) -> [[String: Any]] {
        let day = CalendarDay.string(today)
        let yesterday = CalendarDay.string(Calendar.current.date(byAdding: .day, value: -1, to: today) ?? today)
        let now = APIDate.format(today)
        let user = Self.userId
        func change(_ type: String, _ data: [String: Any]) -> [String: Any] {
            ["entityType": type, "id": data["id"] ?? "", "deleted": false, "data": data]
        }
        var result: [[String: Any]] = [
            change("contact", ["id": "0193a1b2-de00-7000-8000-0000000c0001", "kind": "person", "name": "Familie Schneider",
                               "customerNumber": "K-1088", "street": "Ahornweg 3", "postalCode": "84028", "city": "Landshut",
                               "phone": "0871 123456", "email": "schneider@example.com", "notes": NSNull(), "version": 1]),
            change("contact", ["id": "0193a1b2-de00-7000-8000-0000000c0002", "kind": "company", "name": "Huber Bau GmbH",
                               "customerNumber": "K-1042", "street": "Industriestr. 1", "postalCode": "84028", "city": "Landshut",
                               "phone": "0871 4711", "email": "info@huber.example", "notes": NSNull(), "version": 1]),
            change("bauakte", ["id": Self.efh, "number": "2026-041", "title": "EFH Schneider, Neubau", "status": "inProgress",
                               "street": "Ahornweg 3", "postalCode": "84028", "city": "Landshut", "latitude": 48.5372, "longitude": 12.1521,
                               "description": "Heizestrich CT-C25-F4, 65 mm, ca. 140 m²", "customerId": "0193a1b2-de00-7000-8000-0000000c0001",
                               "siteContactName": "Herr Schneider", "siteContactPhone": "0171 2345678",
                               "responsibleUserId": user, "updatedAt": now, "version": 3]),
            change("bauakte", ["id": Self.halle, "number": "2026-042", "title": "Halle Gewerbering", "status": "planned",
                               "street": "Gewerbering 3", "postalCode": "84030", "city": "Ergolding",
                               "description": "Industrieestrich, 600 m²", "customerId": "0193a1b2-de00-7000-8000-0000000c0002",
                               "responsibleUserId": user, "updatedAt": now, "version": 1]),
            change("bauakte", ["id": Self.praxis, "number": "2026-038", "title": "Praxis Dr. Maier, Sanierung", "status": "drying",
                               "street": "Altstadt 12", "postalCode": "84028", "city": "Landshut",
                               "responsibleUserId": user, "updatedAt": now, "version": 5]),
            change("note", ["id": "0193a1b2-de00-7000-8000-0000000d0001", "bauakteId": Self.efh, "text": "Schlüssel liegt beim Nachbarn (Nr. 5).",
                            "authorUserId": user, "isPinned": true, "createdAt": now, "version": 1]),
            change("diaryEntry", ["id": "0193a1b2-de00-7000-8000-0000000e0001", "bauakteId": Self.efh, "day": yesterday,
                                  "text": "Randdämmstreifen gesetzt, Folie verlegt.", "weather": "cloudy", "temperatureCelsius": 12,
                                  "workersPresent": 3, "authorUserId": user, "createdAt": now, "version": 1]),
            change("diaryEntry", ["id": "0193a1b2-de00-7000-8000-0000000e0002", "bauakteId": Self.efh, "day": day,
                                  "text": "Estrich im EG eingebracht, 65 mm.", "weather": "sunny", "temperatureCelsius": 15,
                                  "workersPresent": 4, "authorUserId": user, "createdAt": now, "version": 1]),
            change("inspection", ["id": "0193a1b2-de00-7000-8000-000000001001", "bauakteId": Self.efh, "kind": "initialInspection",
                                  "performedByUserId": user, "performedAt": now, "notRequired": false,
                                  "checks": ["substrate": "ok", "edgeInsulation": "ok", "heatingPressureTest": "ok", "access": "notOk", "utilities": "ok"],
                                  "photoFileIds": [photos[0].id], "remarks": "Zufahrt nur mit kleinem Lkw",
                                  "notes": "Kunde informiert.\nPumpe am Straßenrand aufstellen.", "version": 1]),
            change("inspection", ["id": "0193a1b2-de00-7000-8000-000000001002", "bauakteId": Self.praxis, "kind": "cmMeasurement",
                                  "performedByUserId": user, "performedAt": now, "location": "Behandlungsraum 1", "binder": "cement",
                                  "heated": false, "thicknessMm": 50, "measuredCmPercent": 1.8, "limitCmPercent": 2.0,
                                  "isReadyForCovering": true, "notRequired": false, "checks": [String: String](), "photoFileIds": [String](), "version": 1]),
            change("workItem", ["id": "0193a1b2-de00-7000-8000-000000002001", "bauakteId": Self.efh, "kind": "defect",
                                "title": "Riss im Flur prüfen", "status": "open", "dueDate": day, "assigneeUserId": user,
                                "createdByUserId": user, "updatedAt": now, "version": 1]),
            change("workItem", ["id": "0193a1b2-de00-7000-8000-000000002002", "bauakteId": Self.halle, "kind": "task",
                                "title": "Zufahrt mit Bauleitung klären", "status": "open", "assigneeUserId": user,
                                "createdByUserId": user, "updatedAt": now, "version": 1]),
        ]
        for photo in photos {
            result.append(change("file", ["id": photo.id, "bauakteId": photo.bauakteId, "kind": "photo", "originalName": "Foto.jpg",
                                          "contentType": "image/jpeg", "sizeBytes": photo.jpeg.count, "sha256": String(repeating: "0", count: 64),
                                          "state": "stored", "caption": photo.caption, "capturedAt": now, "createdAt": now,
                                          "uploadedByUserId": user, "version": 1]))
        }
        result.append(change("file", ["id": pdfId, "bauakteId": Self.efh, "kind": "document", "originalName": "Lieferschein 4711.pdf",
                                      "contentType": "application/pdf", "sizeBytes": pdf.count, "sha256": String(repeating: "0", count: 64),
                                      "state": "stored", "caption": "Lieferschein 4711", "category": "delivery", "capturedAt": now,
                                      "createdAt": now, "uploadedByUserId": user, "version": 1]))
        return result
    }

    /// Inhalt einer Beispieldatei (Fotos, Lieferschein, Bauakte-PDF).
    func content(of fileId: String) -> Data? {
        if fileId == pdfId { return pdf }
        return photos.first { $0.id == fileId }?.jpeg
    }

    func dispatchBoard(day: String) -> String {
        """
        {"day":"\(day)","canPlan":false,"mySiteIds":["\(Self.efh)"],
         "sites":[{"bauakteId":"\(Self.efh)","number":"2026-041","title":"EFH Schneider, Neubau","address":"Ahornweg 3, 84028 Landshut",
           "latitude":48.5372,"longitude":12.1521,"siteContactName":"Herr Schneider","siteContactPhone":"0171 2345678","startTime":"07:00:00",
           "navigationUrl":"https://maps.apple.com/?daddr=48.5372,12.1521","warnings":[],
           "jobs":[{"assignmentId":"0193a1b2-de00-7000-8000-000000003001","kind":"screed","activity":"Estrich einbringen OG",
             "startTime":"07:00:00","endTime":"15:00:00","crewId":null,"crewName":"Kolonne Huber","crewColor":"#E07A1F","notes":null}],
           "people":[{"id":"\(Self.userId)","name":"Max Huber"},{"id":"0193a1b2-de00-7000-8000-000000004002","name":"Lukas Brandl"}],
           "vehicles":[{"id":"0193a1b2-de00-7000-8000-000000005001","name":"Estrichpumpe 1"}]},
          {"bauakteId":"\(Self.halle)","number":"2026-042","title":"Halle Gewerbering","address":"Gewerbering 3, 84030 Ergolding",
           "latitude":null,"longitude":null,"siteContactName":null,"siteContactPhone":null,"startTime":"08:30:00","navigationUrl":null,
           "warnings":["Zufahrt nur bis 7,5 t"],
           "jobs":[{"assignmentId":"0193a1b2-de00-7000-8000-000000003002","kind":"initialInspection","activity":"Erstbegehung",
             "startTime":"08:30:00","endTime":"09:30:00","crewId":null,"crewName":"Kolonne Brandl","crewColor":"#2F80ED","notes":null}],
           "people":[{"id":"0193a1b2-de00-7000-8000-000000004003","name":"Anna Koller"}],"vehicles":[]}],
         "people":[],"vehicles":[],"appointments":[],
         "crews":[{"id":"0193a1b2-de00-7000-8000-000000006001","name":"Kolonne Huber","color":"#E07A1F","memberNames":["Max Huber","Lukas Brandl"],
                   "siteIds":["\(Self.efh)"]},
                  {"id":"0193a1b2-de00-7000-8000-000000006002","name":"Kolonne Brandl","color":"#2F80ED","memberNames":["Anna Koller"],
                   "siteIds":["\(Self.halle)"]}]}
        """
    }

    func myPlanning(day: String) -> String {
        let tomorrow = CalendarDay.date(day).flatMap { Calendar.current.date(byAdding: .day, value: 1, to: $0) }.map { CalendarDay.string($0) } ?? day
        return """
        {"from":"\(day)","to":"\(day)","assignments":[{"id":"0193a1b2-de00-7000-8000-000000003001","day":"\(day)","startTime":"07:00:00",
          "activity":"Estrich einbringen OG","bauakteId":"\(Self.efh)","bauakteNumber":"2026-041","bauakteTitle":"EFH Schneider, Neubau",
          "bauakteAddress":"Ahornweg 3, 84028 Landshut","crewName":"Kolonne Huber","people":[{"id":"\(Self.userId)","name":"Max Huber"}],
          "resources":[{"id":"0193a1b2-de00-7000-8000-000000005001","name":"Estrichpumpe 1","kind":"machine"}],"notes":"Pumpe vorher prüfen","warnings":[]},
          {"id":"0193a1b2-de00-7000-8000-000000003003","day":"\(tomorrow)","startTime":"07:30:00",
          "activity":"CM-Messung und Belegreife","bauakteId":"\(Self.praxis)","bauakteNumber":"2026-038","bauakteTitle":"Praxis Dr. Maier, Sanierung",
          "bauakteAddress":"Altstadt 12, 84028 Landshut","crewName":"Kolonne Huber","people":[{"id":"\(Self.userId)","name":"Max Huber"}],
          "resources":[],"notes":null,"warnings":[]}],
         "absences":[]}
        """
    }

    func tickets(now: String) -> (list: String, detail: String) {
        let summary = """
        {"id":"\(Self.ticketId)","number":"T-2026-0007","title":"Fotos werden nicht übertragen","category":"sync","priority":"normal",
         "status":"waitingForReporter","reporterBlocked":false,"assigneeName":"IT-Support","createdAt":"\(now)","lastActivityAt":"\(now)"}
        """
        let detail = """
        {"summary":\(summary),"description":"Seit gestern bleiben Fotos auf „Wird übertragen“ stehen.","resolution":null,
         "comments":[],"events":[],
         "timeline":[{"id":"0193a1b2-de00-7000-8000-000000006001","at":"\(now)","kind":"created","actorUserId":"\(Self.userId)",
           "actorName":"Max Huber","summary":"Max Huber hat die Meldung erstellt.","text":null,"fromValue":null,"toValue":"New",
           "isInternal":false,"isReporter":true,"attachmentFileIds":[]},
          {"id":"0193a1b2-de00-7000-8000-000000006002","at":"\(now)","kind":"comment","actorUserId":null,"actorName":"IT-Support",
           "summary":"IT-Support hat geantwortet.","text":"Bitte einmal die App öffnen und im WLAN warten, bis die Synchronisation fertig ist.",
           "fromValue":null,"toValue":null,"isInternal":false,"isReporter":false,"attachmentFileIds":[]}]}
        """
        return ("[\(summary)]", detail)
    }
}
