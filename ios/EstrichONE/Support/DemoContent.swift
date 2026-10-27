import EstrichCore
import Foundation
import UIKit

/// Beispieldaten des Demomodus (App-Prüfung durch Apple, Ausprobieren ohne eigenen Server): sechs Bauakten in allen
/// Ständen mit Kunden, Kenndaten nach dem Formular-Schema, Fotos, Lieferschein, Bautagebuch, Erstbegehung, CM-Messung,
/// Aufgaben und Mängeln, Einsätzen der Woche, „Wer fährt wohin“ und einer Meldung an die IT. Alles entsteht lokal.
struct DemoContent: Sendable {
    struct Photo: Sendable {
        let id: String
        let bauakteId: String
        let caption: String
        /// Alter der Aufnahme in Sekunden.
        let age: TimeInterval
        let jpeg: Data
    }

    let photos: [Photo]
    let pdf: Data
    let pdfId = "0193a1b2-de00-7000-8000-00000000f001"

    static let userId = "0193a1b2-0000-7000-8000-00000000aaaa"
    static let efh = "0193a1b2-de00-7000-8000-0000000b0001"
    static let halle = "0193a1b2-de00-7000-8000-0000000b0002"
    static let praxis = "0193a1b2-de00-7000-8000-0000000b0003"
    static let isarpark = "0193a1b2-de00-7000-8000-0000000b0004"
    static let weber = "0193a1b2-de00-7000-8000-0000000b0005"
    static let kita = "0193a1b2-de00-7000-8000-0000000b0006"
    static let ticketId = "0193a1b2-de00-7000-8000-00000000c001"

    private static let schneider = "0193a1b2-de00-7000-8000-0000000c0001"
    private static let huberBau = "0193a1b2-de00-7000-8000-0000000c0002"
    private static let maier = "0193a1b2-de00-7000-8000-0000000c0003"
    private static let wohnbau = "0193a1b2-de00-7000-8000-0000000c0004"
    private static let tWeber = "0193a1b2-de00-7000-8000-0000000c0005"
    private static let gemeinde = "0193a1b2-de00-7000-8000-0000000c0006"

    @MainActor
    static func make() -> DemoContent {
        let photos = [
            Photo(id: "0193a1b2-de00-7000-8000-00000000a001", bauakteId: efh, caption: "Randdämmstreifen gesetzt, EG",
                  age: 86_400 + 3600, jpeg: render(.edgeStrip)),
            Photo(id: "0193a1b2-de00-7000-8000-00000000a002", bauakteId: efh, caption: "Fußbodenheizung vor dem Einbringen",
                  age: 86_400, jpeg: render(.heating)),
            Photo(id: "0193a1b2-de00-7000-8000-00000000a003", bauakteId: efh, caption: "Estrich eingebracht, EG",
                  age: 2400, jpeg: render(.screed)),
            Photo(id: "0193a1b2-de00-7000-8000-00000000a005", bauakteId: efh, caption: "Pumpe am Straßenrand",
                  age: 3000, jpeg: render(.pump)),
            Photo(id: "0193a1b2-de00-7000-8000-00000000a006", bauakteId: efh, caption: "Riss im Flur",
                  age: 1200, jpeg: render(.crack)),
            Photo(id: "0193a1b2-de00-7000-8000-00000000a004", bauakteId: halle, caption: "Zufahrt Halle",
                  age: 86_400 * 4, jpeg: render(.access)),
            Photo(id: "0193a1b2-de00-7000-8000-00000000a007", bauakteId: praxis, caption: "Behandlungsraum 1, verlegt",
                  age: 86_400 * 11, jpeg: render(.screed)),
        ]
        let page = renderDocument().jpegData(compressionQuality: 0.8).flatMap(ScanPDFBuilder.preparePage)
        let pdf = page.flatMap { ScanPDFBuilder.makePDF(pages: [$0], title: "Lieferschein 4711", author: "Demo") } ?? Data()
        return DemoContent(photos: photos, pdf: pdf)
    }

    // MARK: Bilder

    private enum Motif {
        case edgeStrip, heating, screed, pump, crack, access

        var seed: UInt64 {
            switch self {
            case .edgeStrip: 11
            case .heating: 23
            case .screed: 37
            case .pump: 41
            case .crack: 53
            case .access: 67
            }
        }
    }

    /// Einfache, ruhige Motive als Ersatz für Baustellenfotos (keine Texte im Bild).
    @MainActor
    private static func render(_ motif: Motif) -> Data {
        let size = CGSize(width: 1200, height: 900)
        let image = UIGraphicsImageRenderer(size: size).image { context in
            let cg = context.cgContext
            func fill(_ color: UIColor, _ rect: CGRect) {
                color.setFill()
                cg.fill(rect)
            }
            var generator = SeededGenerator(seed: motif.seed)
            func grain(_ rect: CGRect, light: CGFloat, count: Int) {
                for _ in 0..<count {
                    let x = rect.minX + CGFloat.random(in: 0..<rect.width, using: &generator)
                    let y = rect.minY + CGFloat.random(in: 0..<rect.height, using: &generator)
                    UIColor(white: light, alpha: CGFloat.random(in: 0.04...0.12, using: &generator)).setFill()
                    cg.fill(CGRect(x: x, y: y, width: 6, height: 6))
                }
            }
            switch motif {
            case .edgeStrip:
                fill(UIColor(red: 0.86, green: 0.84, blue: 0.80, alpha: 1), CGRect(x: 0, y: 0, width: 1200, height: 520))
                fill(UIColor(red: 0.55, green: 0.56, blue: 0.58, alpha: 1), CGRect(x: 0, y: 520, width: 1200, height: 380))
                fill(UIColor(red: 0.93, green: 0.93, blue: 0.90, alpha: 1), CGRect(x: 0, y: 470, width: 1200, height: 60))
                grain(CGRect(x: 0, y: 530, width: 1200, height: 370), light: 0.2, count: 1500)
            case .heating:
                fill(UIColor(red: 0.80, green: 0.80, blue: 0.78, alpha: 1), CGRect(origin: .zero, size: size))
                grain(CGRect(origin: .zero, size: size), light: 1, count: 900)
                UIColor(red: 0.78, green: 0.20, blue: 0.16, alpha: 1).setStroke()
                cg.setLineWidth(26)
                cg.setLineCap(.round)
                for row in 0..<6 {
                    let y = CGFloat(110 + row * 130)
                    cg.move(to: CGPoint(x: 90, y: y))
                    cg.addLine(to: CGPoint(x: 1110, y: y))
                }
                cg.strokePath()
            case .screed:
                fill(UIColor(red: 0.62, green: 0.62, blue: 0.60, alpha: 1), CGRect(origin: .zero, size: size))
                grain(CGRect(origin: .zero, size: size), light: 0.95, count: 2200)
                grain(CGRect(origin: .zero, size: size), light: 0.15, count: 1200)
                fill(UIColor(red: 0.90, green: 0.88, blue: 0.84, alpha: 1), CGRect(x: 0, y: 0, width: 1200, height: 140))
            case .pump:
                fill(UIColor(red: 0.62, green: 0.70, blue: 0.78, alpha: 1), CGRect(x: 0, y: 0, width: 1200, height: 430))
                fill(UIColor(red: 0.30, green: 0.31, blue: 0.33, alpha: 1), CGRect(x: 0, y: 430, width: 1200, height: 470))
                grain(CGRect(x: 0, y: 430, width: 1200, height: 470), light: 0.8, count: 1500)
                fill(UIColor(red: 0.93, green: 0.55, blue: 0.10, alpha: 1), CGRect(x: 300, y: 330, width: 560, height: 260))
                fill(UIColor(red: 0.12, green: 0.12, blue: 0.13, alpha: 1), CGRect(x: 340, y: 590, width: 120, height: 120))
                fill(UIColor(red: 0.12, green: 0.12, blue: 0.13, alpha: 1), CGRect(x: 700, y: 590, width: 120, height: 120))
            case .crack:
                fill(UIColor(red: 0.66, green: 0.65, blue: 0.62, alpha: 1), CGRect(origin: .zero, size: size))
                grain(CGRect(origin: .zero, size: size), light: 0.95, count: 1800)
                UIColor(white: 0.18, alpha: 1).setStroke()
                cg.setLineWidth(5)
                cg.move(to: CGPoint(x: 150, y: 820))
                for point in [CGPoint(x: 380, y: 610), CGPoint(x: 450, y: 640), CGPoint(x: 700, y: 400), CGPoint(x: 760, y: 430), CGPoint(x: 1050, y: 120)] {
                    cg.addLine(to: point)
                }
                cg.strokePath()
            case .access:
                fill(UIColor(red: 0.55, green: 0.68, blue: 0.80, alpha: 1), CGRect(x: 0, y: 0, width: 1200, height: 380))
                fill(UIColor(red: 0.36, green: 0.50, blue: 0.33, alpha: 1), CGRect(x: 0, y: 380, width: 1200, height: 160))
                fill(UIColor(red: 0.27, green: 0.28, blue: 0.30, alpha: 1), CGRect(x: 0, y: 540, width: 1200, height: 360))
                grain(CGRect(x: 0, y: 540, width: 1200, height: 360), light: 0.85, count: 1400)
                fill(UIColor(red: 0.85, green: 0.86, blue: 0.88, alpha: 1), CGRect(x: 640, y: 180, width: 460, height: 240))
            }
        }
        return image.jpegData(compressionQuality: 0.7) ?? Data()
    }

    @MainActor
    private static func renderDocument() -> UIImage {
        let size = CGSize(width: 1240, height: 1754)
        return UIGraphicsImageRenderer(size: size).image { context in
            UIColor.white.setFill()
            context.fill(CGRect(origin: .zero, size: size))
            let bold: [NSAttributedString.Key: Any] = [.font: UIFont.boldSystemFont(ofSize: 54), .foregroundColor: UIColor.black]
            let body: [NSAttributedString.Key: Any] = [.font: UIFont.systemFont(ofSize: 34), .foregroundColor: UIColor.darkGray]
            ("Lieferschein 4711" as NSString).draw(at: CGPoint(x: 100, y: 120), withAttributes: bold)
            let lines = ["Baustelle: EFH Schneider, Ahornweg 3, 84028 Landshut", "Material: Zementestrich CT-C25-F4",
                         "Menge: 18 m³", "Anlieferung: 07:15 Uhr", "Fahrer: K. Bauer"]
            for (index, line) in lines.enumerated() {
                (line as NSString).draw(at: CGPoint(x: 100, y: 260 + CGFloat(index) * 70), withAttributes: body)
            }
            UIColor.lightGray.setFill()
            context.fill(CGRect(x: 100, y: 700, width: 1040, height: 3))
        }
    }

    // MARK: Datensätze im Format des Änderungsfeeds

    func changes(today: Date = .now) -> [[String: Any]] {
        let calendar = Calendar.current
        func day(_ offset: Int) -> String { CalendarDay.string(calendar.date(byAdding: .day, value: offset, to: today) ?? today) }
        func time(_ offset: TimeInterval) -> String { APIDate.format(today.addingTimeInterval(offset)) }
        let now = APIDate.format(today)
        let user = Self.userId
        func change(_ type: String, _ data: [String: Any]) -> [String: Any] {
            ["entityType": type, "id": data["id"] ?? "", "deleted": false, "data": data]
        }
        func contact(_ id: String, _ kind: String, _ name: String, number: String, street: String, postalCode: String, city: String,
                     phones: [[String: Any]], emails: [[String: Any]], extra: [String: Any] = [:]) -> [String: Any] {
            var data: [String: Any] = ["id": id, "kind": kind, "name": name, "customerNumber": number, "street": street,
                                       "postalCode": postalCode, "city": city, "phones": phones, "emails": emails,
                                       "phone": phones.first?["number"] ?? NSNull(), "email": emails.first?["address"] ?? NSNull(),
                                       "notes": NSNull(), "version": 1]
            data.merge(extra) { _, new in new }
            return change("contact", data)
        }
        func bauakte(_ id: String, number: String, title: String, status: String, street: String, postalCode: String, city: String,
                     customer: String?, project: [String: Any], start: Int?, end: Int?, extra: [String: Any] = [:]) -> [String: Any] {
            var data: [String: Any] = ["id": id, "number": number, "title": title, "status": status, "street": street,
                                       "postalCode": postalCode, "city": city, "customerId": customer ?? NSNull(), "project": project,
                                       "plannedStart": start.map(day) ?? NSNull(), "plannedEnd": end.map(day) ?? NSNull(),
                                       "responsibleUserId": user, "updatedAt": time(-5400), "version": 3]
            data.merge(extra) { _, new in new }
            return change("bauakte", data)
        }
        func room(_ number: Int, _ floor: String, _ name: String, area: Double, laid: Bool, heated: Bool = true, thickness: Int = 65,
                  notes: String? = nil) -> [String: Any] {
            change("room", ["id": String(format: "0193a1b2-de00-7000-8000-00000000%04d", 3000 + number), "bauakteId": Self.efh, "name": name,
                            "floor": floor, "areaSquareMeters": area, "thicknessMillimeters": thickness, "insulationMillimeters": 40,
                            "underfloorHeating": heated, "status": laid ? "laid" : "planned", "notes": notes ?? NSNull(),
                            "updatedAt": time(-3600), "version": 1])
        }
        func item(_ id: String, _ bauakteId: String, kind: String, title: String, status: String = "open", due: Int? = nil) -> [String: Any] {
            change("workItem", ["id": id, "bauakteId": bauakteId, "kind": kind, "title": title, "status": status,
                                "dueDate": due.map(day) ?? NSNull(), "assigneeUserId": user, "createdByUserId": user,
                                "updatedAt": now, "version": 1])
        }

        var result: [[String: Any]] = [
            contact(Self.schneider, "person", "Familie Schneider", number: "K-1088", street: "Ahornweg 3", postalCode: "84028", city: "Landshut",
                    phones: [["kind": "mobile", "number": "0171 2345678", "label": "Herr Schneider"],
                             ["kind": "landline", "number": "0871 123456", "label": NSNull()]],
                    emails: [["address": "schneider@example.com", "label": "privat"]],
                    extra: ["salutation": "Familie", "preferredChannel": "mobile", "notes": "Hund im Garten – bitte klingeln."]),
            contact(Self.huberBau, "company", "Huber Bau GmbH", number: "K-1042", street: "Industriestr. 1", postalCode: "84028", city: "Landshut",
                    phones: [["kind": "office", "number": "0871 4711", "label": "Zentrale"], ["kind": "fax", "number": "0871 4712", "label": NSNull()]],
                    emails: [["address": "info@huber.example", "label": "Zentrale"], ["address": "bauleitung@huber.example", "label": "Bauleitung"]],
                    extra: ["preferredChannel": "email", "website": "www.huber.example"]),
            contact(Self.maier, "company", "Praxis Dr. Maier", number: "K-1103", street: "Altstadt 12", postalCode: "84028", city: "Landshut",
                    phones: [["kind": "office", "number": "0871 96 55 10", "label": "Praxis"]],
                    emails: [["address": "praxis@maier.example", "label": NSNull()]]),
            contact(Self.wohnbau, "company", "Wohnbau Isar GmbH", number: "K-1017", street: "Am Isarpark 1", postalCode: "84034", city: "Landshut",
                    phones: [["kind": "office", "number": "0871 33 20 00", "label": "Bauleitung"]],
                    emails: [["address": "bau@wohnbau-isar.example", "label": "Bauleitung"]]),
            contact(Self.tWeber, "person", "Thomas Weber", number: "K-1121", street: "Lindenstr. 8", postalCode: "84137", city: "Vilsbiburg",
                    phones: [["kind": "mobile", "number": "0160 9876543", "label": NSNull()]], emails: []),
            contact(Self.gemeinde, "company", "Gemeinde Ergolding", number: "K-1002", street: "Lindenstr. 25", postalCode: "84030", city: "Ergolding",
                    phones: [["kind": "office", "number": "0871 97 40 0", "label": "Bauamt"]],
                    emails: [["address": "bauamt@ergolding.example", "label": "Bauamt"]]),

            bauakte(Self.efh, number: "2026-041", title: "EFH Schneider, Neubau", status: "inProgress", street: "Ahornweg 3",
                    postalCode: "84028", city: "Landshut", customer: Self.schneider,
                    project: ["objectType": "efh", "floors": "EG, OG", "totalArea": 142.5, "priority": "normal", "screedType": "ct",
                              "construction": "heated", "nominalThicknessMm": 65, "strengthClass": "C25-F4", "underfloorHeating": true,
                              "heatingType": "water", "insulation": "EPS 035 DEO", "insulationThicknessMm": 40,
                              "siteAccess": "Pumpe am Straßenrand, Schlauch über die Terrasse", "waterOnSite": true, "powerOnSite": true],
                    start: -2, end: 3,
                    extra: ["latitude": 48.5372, "longitude": 12.1521, "description": "Heizestrich CT-C25-F4, 65 mm, EG und OG",
                            "siteContactName": "Herr Schneider", "siteContactPhone": "0171 2345678", "updatedAt": time(-1800)]),
            bauakte(Self.halle, number: "2026-042", title: "Halle Gewerbering", status: "planned", street: "Gewerbering 3",
                    postalCode: "84030", city: "Ergolding", customer: Self.huberBau,
                    project: ["objectType": "industrial", "totalArea": 600, "screedType": "ct", "construction": "bonded",
                              "nominalThicknessMm": 50, "strengthClass": "C30-F5", "underfloorHeating": false, "priority": "high",
                              "siteAccess": "Zufahrt nur bis 7,5 t"],
                    start: 3, end: 5, extra: ["description": "Industrieestrich im Verbund, 600 m²"]),
            bauakte(Self.praxis, number: "2026-038", title: "Praxis Dr. Maier, Sanierung", status: "drying", street: "Altstadt 12",
                    postalCode: "84028", city: "Landshut", customer: Self.maier,
                    project: ["objectType": "renovation", "totalArea": 85, "screedType": "caf", "construction": "floating",
                              "nominalThicknessMm": 45, "strengthClass": "CA-C25-F4", "underfloorHeating": true, "heatingType": "water"],
                    start: -12, end: -10),
            bauakte(Self.isarpark, number: "2026-044", title: "MFH Isarpark, Haus B", status: "planned", street: "Am Isarpark 7",
                    postalCode: "84034", city: "Landshut", customer: Self.wohnbau,
                    project: ["objectType": "mfh", "floors": "EG–3. OG", "totalArea": 920, "screedType": "ct", "construction": "floating",
                              "nominalThicknessMm": 55, "strengthClass": "C25-F4", "underfloorHeating": true, "heatingType": "water"],
                    start: 2, end: 9),
            bauakte(Self.weber, number: "2026-047", title: "Anbau Weber", status: "inquiry", street: "Lindenstr. 8",
                    postalCode: "84137", city: "Vilsbiburg", customer: Self.tWeber,
                    project: ["objectType": "efh", "totalArea": 38], start: nil, end: nil),
            bauakte(Self.kita, number: "2026-021", title: "Kindergarten Sonnenschein", status: "completed", street: "Schulweg 4",
                    postalCode: "84030", city: "Ergolding", customer: Self.gemeinde,
                    project: ["objectType": "public", "totalArea": 410, "screedType": "caf", "construction": "floating",
                              "nominalThicknessMm": 50, "underfloorHeating": true],
                    start: -40, end: -35),

            change("note", ["id": "0193a1b2-de00-7000-8000-0000000d0001", "bauakteId": Self.efh, "text": "Schlüssel liegt beim Nachbarn (Nr. 5).",
                            "authorUserId": user, "isPinned": true, "createdAt": time(-86_400 * 2), "version": 1]),
            change("note", ["id": "0193a1b2-de00-7000-8000-0000000d0002", "bauakteId": Self.efh,
                            "text": "Kunde wünscht Fugen im Türbereich – mit Bauleitung abgestimmt.",
                            "authorUserId": user, "isPinned": false, "createdAt": time(-86_400), "version": 1]),
            change("diaryEntry", ["id": "0193a1b2-de00-7000-8000-0000000e0001", "bauakteId": Self.efh, "day": day(-1),
                                  "text": "Randdämmstreifen gesetzt, Folie verlegt.", "weather": "cloudy", "temperatureCelsius": 12,
                                  "workersPresent": 3, "authorUserId": user, "createdAt": time(-86_400), "version": 1]),
            change("diaryEntry", ["id": "0193a1b2-de00-7000-8000-0000000e0002", "bauakteId": Self.efh, "day": day(0),
                                  "text": "Estrich im EG eingebracht, 65 mm.\nPumpe lief ohne Störung.", "weather": "sunny",
                                  "temperatureCelsius": 15, "workersPresent": 4, "authorUserId": user, "createdAt": time(-3600), "version": 1]),
            change("diaryEntry", ["id": "0193a1b2-de00-7000-8000-0000000e0003", "bauakteId": Self.praxis, "day": day(-11),
                                  "text": "Fließestrich im Behandlungsbereich eingebracht.", "weather": "rainy", "temperatureCelsius": 9,
                                  "workersPresent": 3, "authorUserId": user, "createdAt": time(-86_400 * 11), "version": 1]),
            change("inspection", ["id": "0193a1b2-de00-7000-8000-000000001001", "bauakteId": Self.efh, "kind": "initialInspection",
                                  "performedByUserId": user, "performedAt": time(-86_400 * 3), "notRequired": false,
                                  "checks": ["substrate": "ok", "edgeInsulation": "ok", "heatingPressureTest": "ok", "access": "notOk", "utilities": "ok"],
                                  "checkRemarks": ["access": "Zufahrt nur mit kleinem Lkw – Pumpe an die Straße."],
                                  "values": ["measuredArea": 142.5, "thickness": 65, "underfloorHeating": true, "water": true, "power": true],
                                  "schemaVersion": 1,
                                  "photoFileIds": [photos[0].id], "remarks": "Zufahrt nur mit kleinem Lkw",
                                  "notes": "Kunde informiert.\nPumpe am Straßenrand aufstellen.", "version": 1]),
            change("inspection", ["id": "0193a1b2-de00-7000-8000-000000001002", "bauakteId": Self.praxis, "kind": "cmMeasurement",
                                  "performedByUserId": user, "performedAt": time(-86_400), "location": "Behandlungsraum 1", "binder": "calciumSulfate",
                                  "heated": true, "thicknessMm": 45, "measuredCmPercent": 0.3, "limitCmPercent": 0.3,
                                  "isReadyForCovering": true, "notRequired": false, "checks": [String: String](), "photoFileIds": [String](), "version": 1]),
            item("0193a1b2-de00-7000-8000-000000002001", Self.efh, kind: "defect", title: "Riss im Flur prüfen", due: 0),
            item("0193a1b2-de00-7000-8000-000000002003", Self.efh, kind: "task", title: "Randdämmstreifen Bad OG nachsetzen", due: 1),
            item("0193a1b2-de00-7000-8000-000000002004", Self.efh, kind: "task", title: "Folie im EG verlegen", status: "done"),
            item("0193a1b2-de00-7000-8000-000000002002", Self.halle, kind: "task", title: "Zufahrt mit Bauleitung klären", due: 2),
            item("0193a1b2-de00-7000-8000-000000002005", Self.praxis, kind: "task", title: "CM-Messung Behandlungsraum 2", due: 2),
        ]
        // Beteiligte am EFH Schneider (gepflegt im Büro).
        func party(_ number: Int, _ role: String, _ name: String, company: String? = nil, phone: String? = nil, email: String? = nil,
                   note: String? = nil) -> [String: Any] {
            change("bauakteParty", ["id": String(format: "0193a1b2-de00-7000-8000-00000000%04d", 4000 + number), "bauakteId": Self.efh,
                                    "role": role, "name": name, "company": company ?? NSNull(), "phone": phone ?? NSNull(),
                                    "email": email ?? NSNull(), "note": note ?? NSNull(), "contactId": NSNull(), "version": 1,
                                    "updatedAt": time(-86_400 * 6)])
        }
        result += [
            party(1, "architect", "Anna Lechner", company: "Lechner Architekten", phone: "0871 430 22 10", email: "lechner@architekten.example"),
            party(2, "siteManager", "Stefan Wimmer", company: "Wimmer Hausbau GmbH", phone: "0171 445 56 67",
                  note: "Bauleitung – Abnahme der Estricharbeiten"),
            party(3, "other", "Haustechnik Bauer GmbH", phone: "0871 66 12 0", note: "Heizung: Druckprobe vor dem Einbringen"),
            party(4, "supplier", "Transportbeton Isar GmbH", phone: "0871 77 88 0", note: "Disposition ab 6 Uhr"),
        ]
        // EFH Schneider: EG heute eingebracht, OG folgt – zusammen die 142,5 m² aus den Basisdaten.
        result += [
            room(1, "EG", "Wohnen/Essen", area: 41.2, laid: true),
            room(2, "EG", "Küche", area: 13.4, laid: true),
            room(3, "EG", "Flur", area: 10.6, laid: true, notes: "Haarriss an der Haustür – siehe Mangel"),
            room(4, "EG", "Gäste-WC", area: 3.4, laid: true),
            room(5, "EG", "Hauswirtschaftsraum", area: 7.9, laid: true, heated: false),
            room(6, "OG", "Schlafen", area: 17.2, laid: false),
            room(7, "OG", "Kind 1", area: 13.1, laid: false),
            room(8, "OG", "Kind 2", area: 12.8, laid: false),
            room(9, "OG", "Bad", area: 10.4, laid: false, thickness: 70, notes: "Gefälle zur bodengleichen Dusche"),
            room(10, "OG", "Flur", area: 8.6, laid: false),
            room(11, "OG", "Ankleide", area: 3.9, laid: false),
        ]
        result += Self.schemas.map { change("formSchema", $0) }
        result += Self.valueLists.map { change("valueListItem", $0) }
        for photo in photos {
            result.append(change("file", ["id": photo.id, "bauakteId": photo.bauakteId, "kind": "photo", "originalName": "Foto.jpg",
                                          "contentType": "image/jpeg", "sizeBytes": photo.jpeg.count, "sha256": String(repeating: "0", count: 64),
                                          "state": "stored", "caption": photo.caption, "capturedAt": time(-photo.age),
                                          "createdAt": time(-photo.age), "uploadedByUserId": user, "version": 1]))
        }
        result.append(change("file", ["id": pdfId, "bauakteId": Self.efh, "kind": "document", "originalName": "Lieferschein 4711.pdf",
                                      "contentType": "application/pdf", "sizeBytes": pdf.count, "sha256": String(repeating: "0", count: 64),
                                      "state": "stored", "caption": "Lieferschein 4711", "category": "delivery", "capturedAt": time(-7200),
                                      "createdAt": time(-7200), "uploadedByUserId": user, "version": 1]))
        return result
    }

    /// Formular-Schemata wie die Grundbelegung des Servers (Basisdaten und Erstbegehung).
    private static var schemas: [[String: Any]] {
        func field(_ key: String, _ label: String, _ type: String, list: String? = nil, unit: String? = nil, help: String? = nil,
                   badge: Bool = false, photos: Bool = false, path: String? = nil, target: String? = nil) -> [String: Any] {
            ["key": key, "label": label, "type": type, "path": path ?? "project.\(key)", "list": list ?? NSNull(), "unit": unit ?? NSNull(),
             "required": false, "help": help ?? NSNull(), "badge": badge, "builtin": true, "photos": photos, "target": target ?? NSNull()]
        }
        let bauakte: [[String: Any]] = [
            ["key": "bauvorhaben", "label": "Bauvorhaben", "fields": [
                field("objectType", "Objektart", "select", list: "objectType", badge: true),
                field("floors", "Geschosse", "text", help: "z. B. KG, EG, OG"),
                field("totalArea", "Gesamtfläche", "number", unit: "m²", badge: true),
                field("priority", "Priorität", "select", list: "priority"),
            ]],
            ["key": "estrich", "label": "Estrich", "fields": [
                field("screedType", "Estrichart", "select", list: "screedType", badge: true),
                field("construction", "Konstruktion", "select", list: "construction"),
                field("nominalThicknessMm", "Nenndicke", "integer", unit: "mm", badge: true),
                field("strengthClass", "Festigkeitsklasse", "select", list: "strengthClass"),
                field("underfloorHeating", "Fußbodenheizung", "boolean", badge: true),
                field("heatingType", "Art der Heizung", "select", list: "heatingType"),
                field("insulation", "Dämmung", "text", help: "z. B. EPS 035 DEO"),
                field("insulationThicknessMm", "Dämmdicke", "integer", unit: "mm"),
            ]],
            ["key": "baustelle", "label": "Baustelle", "fields": [
                field("siteAccess", "Zugang / Besonderheiten", "textarea", help: "Pumpenstandort, Zufahrt, Etage"),
                field("waterOnSite", "Wasser vor Ort", "boolean"),
                field("powerOnSite", "Strom vor Ort", "boolean"),
            ]],
        ]
        let inspection: [[String: Any]] = [
            ["key": "pruefpunkte", "label": "Prüfpunkte", "fields": [
                field("substrate", "Untergrund tragfähig, eben und sauber", "check", photos: true, path: "custom.substrate"),
                field("edgeInsulation", "Randdämmstreifen vorhanden", "check", photos: true, path: "custom.edgeInsulation"),
                field("heatingPressureTest", "Heizung: Druckprüfung erfolgt", "check", photos: true, path: "custom.heatingPressureTest"),
                field("access", "Zufahrt und Pumpenstandort geklärt", "check", path: "custom.access"),
                field("utilities", "Wasser und Strom vor Ort", "check", path: "custom.utilities"),
            ]],
            ["key": "messwerte", "label": "Messwerte und Aufbau", "fields": [
                field("measuredArea", "Gemessene Fläche", "number", unit: "m²", path: "custom.measuredArea", target: "project.totalArea"),
                field("screedType", "Estrichart", "select", list: "screedType", path: "custom.screedType", target: "project.screedType"),
                field("thickness", "Estrichdicke", "integer", unit: "mm", path: "custom.thickness", target: "project.nominalThicknessMm"),
                field("underfloorHeating", "Fußbodenheizung", "boolean", path: "custom.underfloorHeating", target: "project.underfloorHeating"),
                field("water", "Wasser vor Ort", "boolean", path: "custom.water", target: "project.waterOnSite"),
                field("power", "Strom vor Ort", "boolean", path: "custom.power", target: "project.powerOnSite"),
            ]],
        ]
        return [
            ["id": "0193a1b2-de00-7000-8000-0000000f0001", "key": "bauakte", "version": 1, "groups": bauakte, "lists": NSNull()],
            ["id": "0193a1b2-de00-7000-8000-0000000f0002", "key": "erstbegehung", "version": 1, "groups": inspection, "lists": NSNull()],
        ]
    }

    /// Wertelisten wie die Grundbelegung des Servers.
    private static var valueLists: [[String: Any]] {
        let lists: [(String, [(String, String, String?)])] = [
            ("objectType", [("efh", "Einfamilienhaus", "EFH"), ("mfh", "Mehrfamilienhaus", "MFH"), ("commercial", "Gewerbe", nil),
                            ("industrial", "Industrie/Halle", "Halle"), ("public", "Öffentlicher Bau", nil), ("renovation", "Sanierung", nil),
                            ("other", "Sonstiges", nil)]),
            ("screedType", [("ct", "Zementestrich (CT)", "CT"), ("caf", "Calciumsulfat-Fließestrich (CAF)", "CAF"), ("as", "Gussasphalt (AS)", "AS"),
                            ("dry", "Trockenestrich", nil), ("other", "Sonstiges", nil)]),
            ("construction", [("bonded", "Verbundestrich", nil), ("separatingLayer", "Auf Trennlage", nil), ("floating", "Schwimmend", nil),
                              ("heated", "Heizestrich", nil)]),
            ("strengthClass", [("C20-F3", "C20-F3", nil), ("C25-F4", "C25-F4", nil), ("C30-F5", "C30-F5", nil), ("C35-F5", "C35-F5", nil),
                               ("CA-C25-F4", "CA-C25-F4", nil), ("CA-C30-F5", "CA-C30-F5", nil)]),
            ("priority", [("low", "Niedrig", nil), ("normal", "Normal", nil), ("high", "Hoch", nil), ("urgent", "Dringend", nil)]),
            ("heatingType", [("water", "Warmwasser-Fußbodenheizung", nil), ("electric", "Elektrische Fußbodenheizung", nil),
                             ("cooling", "Fußbodenkühlung", nil)]),
        ]
        var result: [[String: Any]] = []
        var counter = 0
        for (list, items) in lists {
            for (index, item) in items.enumerated() {
                counter += 1
                result.append(["id": String(format: "0193a1b2-de00-7000-8000-0000001%05x", counter), "list": list, "key": item.0,
                               "label": item.1, "shortLabel": item.2 ?? NSNull(), "sortOrder": (index + 1) * 10, "isActive": true, "version": 1])
            }
        }
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
           "crews":[{"id":"0193a1b2-de00-7000-8000-000000006001","name":"Kolonne Huber","color":"#E07A1F",
                     "assignmentId":"0193a1b2-de00-7000-8000-000000003001","activity":"Estrich einbringen OG","startTime":"07:00:00","endTime":"15:00:00"}],
           "jobs":[{"assignmentId":"0193a1b2-de00-7000-8000-000000003001","kind":"screed","activity":"Estrich einbringen OG",
             "startTime":"07:00:00","endTime":"15:00:00","crewId":null,"crewName":"Kolonne Huber","crewColor":"#E07A1F","notes":"Pumpe vorher prüfen"}],
           "people":[{"id":"\(Self.userId)","name":"Max Huber"},{"id":"0193a1b2-de00-7000-8000-000000004002","name":"Lukas Brandl"}],
           "vehicles":[{"id":"0193a1b2-de00-7000-8000-000000005001","name":"Estrichpumpe 1"}]},
          {"bauakteId":"\(Self.halle)","number":"2026-042","title":"Halle Gewerbering","address":"Gewerbering 3, 84030 Ergolding",
           "latitude":null,"longitude":null,"siteContactName":null,"siteContactPhone":null,"startTime":"08:30:00","navigationUrl":null,
           "warnings":["Zufahrt nur bis 7,5 t"],
           "crews":[{"id":"0193a1b2-de00-7000-8000-000000006002","name":"Kolonne Brandl","color":"#2F80ED",
                     "assignmentId":"0193a1b2-de00-7000-8000-000000003002","activity":"Erstbegehung","startTime":"08:30:00","endTime":"09:30:00"}],
           "jobs":[{"assignmentId":"0193a1b2-de00-7000-8000-000000003002","kind":"initialInspection","activity":"Erstbegehung",
             "startTime":"08:30:00","endTime":"09:30:00","crewId":null,"crewName":"Kolonne Brandl","crewColor":"#2F80ED","notes":null}],
           "people":[{"id":"0193a1b2-de00-7000-8000-000000004003","name":"Anna Koller"}],"vehicles":[]},
          {"bauakteId":"\(Self.isarpark)","number":"2026-044","title":"MFH Isarpark, Haus B","address":"Am Isarpark 7, 84034 Landshut",
           "latitude":null,"longitude":null,"siteContactName":null,"siteContactPhone":null,"startTime":"10:00:00","navigationUrl":null,
           "warnings":[],
           "crews":[{"id":"0193a1b2-de00-7000-8000-000000006003","name":"Kolonne Kaya","color":"#3A9D6B",
                     "assignmentId":"0193a1b2-de00-7000-8000-000000003004","activity":"Randdämmung und Folie","startTime":"10:00:00","endTime":"16:00:00"}],
           "jobs":[{"assignmentId":"0193a1b2-de00-7000-8000-000000003004","kind":"preparation","activity":"Randdämmung und Folie",
             "startTime":"10:00:00","endTime":"16:00:00","crewId":null,"crewName":"Kolonne Kaya","crewColor":"#3A9D6B","notes":null}],
           "people":[{"id":"0193a1b2-de00-7000-8000-000000004005","name":"Emre Kaya"},{"id":"0193a1b2-de00-7000-8000-000000004006","name":"Jonas Wirth"}],
           "vehicles":[{"id":"0193a1b2-de00-7000-8000-000000005002","name":"Sprinter LA-EO 12"}]}],
         "people":[],"vehicles":[],"appointments":[],
         "crews":[{"id":"0193a1b2-de00-7000-8000-000000006001","name":"Kolonne Huber","color":"#E07A1F","leaderName":"Max Huber","memberCount":2,
                   "vehicles":[{"id":"0193a1b2-de00-7000-8000-000000005001","name":"Estrichpumpe 1","kind":"machine"}],"absentMembers":[],
                   "siteIds":["\(Self.efh)"]},
                  {"id":"0193a1b2-de00-7000-8000-000000006002","name":"Kolonne Brandl","color":"#2F80ED","leaderName":"Anna Koller","memberCount":1,"vehicles":[],
                   "absentMembers":[{"id":"0193a1b2-de00-7000-8000-000000004004","name":"Tobias Lang","note":"Urlaub"}],
                   "siteIds":["\(Self.halle)"]},
                  {"id":"0193a1b2-de00-7000-8000-000000006003","name":"Kolonne Kaya","color":"#3A9D6B","leaderName":"Emre Kaya","memberCount":2,
                   "vehicles":[{"id":"0193a1b2-de00-7000-8000-000000005002","name":"Sprinter LA-EO 12","kind":"vehicle"}],"absentMembers":[],
                   "siteIds":["\(Self.isarpark)"]}]}
        """
    }

    func myPlanning(day: String) -> String {
        func offset(_ days: Int) -> String {
            CalendarDay.date(day).flatMap { Calendar.current.date(byAdding: .day, value: days, to: $0) }.map { CalendarDay.string($0) } ?? day
        }
        let me = #"[{"id":"\#(Self.userId)","name":"Max Huber"},{"id":"0193a1b2-de00-7000-8000-000000004002","name":"Lukas Brandl"}]"#
        return """
        {"from":"\(day)","to":"\(offset(6))","assignments":[
          {"id":"0193a1b2-de00-7000-8000-000000003001","day":"\(day)","startTime":"07:00:00",
          "activity":"Estrich einbringen OG","bauakteId":"\(Self.efh)","bauakteNumber":"2026-041","bauakteTitle":"EFH Schneider, Neubau",
          "bauakteAddress":"Ahornweg 3, 84028 Landshut","crewName":"Kolonne Huber","people":\(me),
          "resources":[{"id":"0193a1b2-de00-7000-8000-000000005001","name":"Estrichpumpe 1","kind":"machine"}],"notes":"Pumpe vorher prüfen","warnings":[]},
          {"id":"0193a1b2-de00-7000-8000-000000003003","day":"\(offset(1))","startTime":"07:30:00",
          "activity":"CM-Messung und Belegreife","bauakteId":"\(Self.praxis)","bauakteNumber":"2026-038","bauakteTitle":"Praxis Dr. Maier, Sanierung",
          "bauakteAddress":"Altstadt 12, 84028 Landshut","crewName":"Kolonne Huber","people":\(me),"resources":[],"notes":null,"warnings":[]},
          {"id":"0193a1b2-de00-7000-8000-000000003005","day":"\(offset(1))","startTime":"10:30:00",
          "activity":"Estrich OG nacharbeiten","bauakteId":"\(Self.efh)","bauakteNumber":"2026-041","bauakteTitle":"EFH Schneider, Neubau",
          "bauakteAddress":"Ahornweg 3, 84028 Landshut","crewName":"Kolonne Huber","people":\(me),"resources":[],"notes":null,"warnings":[]},
          {"id":"0193a1b2-de00-7000-8000-000000003006","day":"\(offset(2))","startTime":"07:00:00",
          "activity":"Erstbegehung","bauakteId":"\(Self.isarpark)","bauakteNumber":"2026-044","bauakteTitle":"MFH Isarpark, Haus B",
          "bauakteAddress":"Am Isarpark 7, 84034 Landshut","crewName":"Kolonne Huber","people":\(me),"resources":[],"notes":null,"warnings":[]},
          {"id":"0193a1b2-de00-7000-8000-000000003007","day":"\(offset(3))","startTime":"06:30:00",
          "activity":"Estrich einbringen","bauakteId":"\(Self.halle)","bauakteNumber":"2026-042","bauakteTitle":"Halle Gewerbering",
          "bauakteAddress":"Gewerbering 3, 84030 Ergolding","crewName":"Kolonne Huber","people":\(me),
          "resources":[{"id":"0193a1b2-de00-7000-8000-000000005001","name":"Estrichpumpe 1","kind":"machine"}],"notes":null,
          "warnings":["Zufahrt nur bis 7,5 t"]}],
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

/// Reproduzierbarer Zufall für die Beispielbilder (immer dieselben Motive).
private struct SeededGenerator: RandomNumberGenerator {
    private var state: UInt64

    init(seed: UInt64) {
        state = seed &+ 0x9E37_79B9_7F4A_7C15
    }

    mutating func next() -> UInt64 {
        state &+= 0x9E37_79B9_7F4A_7C15
        var value = state
        value = (value ^ (value >> 30)) &* 0xBF58_476D_1CE4_E5B9
        value = (value ^ (value >> 27)) &* 0x94D0_49BB_1331_11EB
        return value ^ (value >> 31)
    }
}
