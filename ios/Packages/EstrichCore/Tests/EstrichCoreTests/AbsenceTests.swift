import Foundation
import Testing
@testable import EstrichCore

@Suite("Urlaub und Abwesenheiten")
struct AbsenceTests {
    static let me = """
        {"employeeId":"01a00000-0000-7000-8000-0000000000e1","year":2027,"canApprove":true,"pendingApprovals":2,
         "vacation":{"year":2027,"entitlement":22.5,"carryOver":5,"taken":3,"planned":4.5,"remaining":20,"sickDays":0,"requested":2,
           "annualEntitlement":30,"adjustment":0,"carryOverExpiresOn":"2027-03-31","carryOverExpired":0,"carryOverExpiring":2,
           "remainingAfterRequests":18,"byType":[],"note":null},
         "requests":[{"id":"01a00000-0000-7000-8000-0000000000f1","userId":"01a00000-0000-7000-8000-0000000000a1","userName":"Max Huber",
           "from":"2027-06-07","to":"2027-06-09","kind":"vacation","note":null,"status":"approved","workingDays":2.5,
           "requestedByUserId":"01a00000-0000-7000-8000-0000000000a1","requestedByName":"Max Huber","requestedAt":"2026-11-10T08:00:00Z",
           "decidedByUserId":null,"decidedByName":null,"decidedAt":null,"decisionComment":null,"warnings":[],"remainingAfter":17.5,
           "canDecide":false,"canCancel":true,"version":1,"typeId":"01a10000-0000-7000-8000-000000000001","typeName":"Urlaub",
           "typeColor":"#43A047","halfDayStart":true,"halfDayEnd":false,"substituteUserId":null,"substituteName":"Lukas Brandl",
           "replacesRequestId":null,"canChange":true,"countsAsVacation":true}],
         "holidays":[{"date":"2027-01-01","name":"Neujahr"}],
         "types":[{"id":"01a10000-0000-7000-8000-000000000001","key":"vacation","name":"Urlaub","code":"U","color":"#43A047","kind":"vacation",
           "requiresApproval":true,"countsAsVacation":true,"requestable":true,"isPaid":true,"isActive":true,"sortOrder":10,"isSystem":true,"isSickness":false},
          {"id":"01a10000-0000-7000-8000-000000000002","key":"sick","name":"Krankheit","code":"K","color":"#E53935","kind":"sick",
           "requiresApproval":false,"countsAsVacation":false,"requestable":false,"isPaid":true,"isActive":true,"sortOrder":20,"isSystem":true,"isSickness":true}],
         "closures":[],"absences":[{"id":"01a00000-0000-7000-8000-0000000000b1","from":"2027-06-07","to":"2027-06-09",
           "typeId":"01a10000-0000-7000-8000-000000000001","typeName":"Urlaub","color":"#43A047","kind":"vacation","halfDayStart":true,
           "halfDayEnd":false,"workingDays":2.5,"requestId":"01a00000-0000-7000-8000-0000000000f1"}]}
        """

    @Test("Eigene Übersicht: Konto mit Verfall, Anträge, beantragbare Arten")
    func decodesMine() throws {
        let mine = try JSONCoding.decoder.decode(MyVacation.self, from: Data(Self.me.utf8))
        let account = try #require(mine.vacation)
        #expect(account.entitlement == Decimal(string: "22.5"))
        #expect(account.carryOverExpiring == 2)
        #expect(account.remainingAfterRequests == 18)
        #expect(mine.canApprove && mine.pendingApprovals == 2)
        #expect(mine.requestableTypes.map(\.key) == ["vacation"])
        let request = try #require(mine.requests.first)
        #expect(request.status == .approved)
        #expect(request.rangeText == "07.06. – 09.06.2027 (erster Tag halb)")
        #expect(AbsenceText.days(request.workingDays) == "2,5")
        #expect(mine.absences?.first?.workingDays == Decimal(string: "2.5"))
        #expect(request.canChange)
        #expect(mine.canRequestChange(request))
        let change = request.changeDraft
        #expect(change.replacesRequestId == request.id && change.from == "2027-06-07" && change.halfDayStart)
    }

    @Test("Änderungsantrag: nur einer offen, Vertretung wird gesendet")
    func changeRequest() throws {
        let approvedId = UUID()
        let approved = AbsenceRequestItem(id: approvedId, userId: UUID(), userName: "A", from: "2027-08-02", to: "2027-08-13", status: .approved,
                                          workingDays: 10, substituteUserId: UUID(), canChange: true)
        let pending = AbsenceRequestItem(id: UUID(), userId: approved.userId, userName: "A", from: "2027-08-09", to: "2027-08-20", status: .pending,
                                         workingDays: 10, replacesRequestId: approvedId)
        let mine = MyVacation(employeeId: nil, vacation: nil, requests: [pending, approved], year: 2027)
        #expect(mine.pendingChange(of: approved) == pending)
        #expect(!mine.canRequestChange(approved))
        let body = try #require(String(data: try JSONCoding.encoder.encode(approved.changeDraft.normalized), encoding: .utf8))
        #expect(body.contains("substituteUserId") && body.contains("replacesRequestId"))
    }

    @Test("Feiertage Bayern (Demomodus)")
    func holidays() {
        let easter = PublicHolidays.easter(2027)
        #expect(easter.month == 3 && easter.day == 28)
        let days = PublicHolidays.bavaria(2027).map(\.date)
        #expect(days.contains("2027-03-26"), "Karfreitag")
        #expect(days.contains("2027-05-27"), "Fronleichnam")
        #expect(days.contains("2027-01-06") && days.contains("2027-08-15"))
        #expect(days.count == 13)
        let christmas = PublicHolidays.bavaria(from: "2026-12-21", to: "2027-01-08").map(\.name)
        #expect(christmas == ["1. Weihnachtstag", "2. Weihnachtstag", "Neujahr", "Heilige Drei Könige"])
        let preview = AbsencePreview(workingDays: 1, warnings: [], remainingAfter: nil, requiresApproval: true,
                                     holidays: [Holiday(date: "2026-12-25", name: "1. Weihnachtstag")])
        #expect(preview.holidayText == "Feiertage im Zeitraum (nicht mitgezählt): 25.12. 1. Weihnachtstag")
    }

    @Test("Ältere Server ohne Erweiterungen: fehlende Felder mit Standardwerten")
    func decodesOlderServer() throws {
        let json = """
            {"employeeId":null,"vacation":{"year":2026,"entitlement":30,"carryOver":0,"taken":1,"planned":2,"remaining":27,"sickDays":0},
             "requests":[{"id":"01a00000-0000-7000-8000-0000000000f1","userId":"01a00000-0000-7000-8000-0000000000a1","userName":"Max",
               "from":"2026-12-01","to":"2026-12-01","status":"pending","workingDays":1}],"holidays":[]}
            """
        let mine = try JSONCoding.decoder.decode(MyVacation.self, from: Data(json.utf8))
        #expect(mine.year == 2026)
        #expect(mine.vacation?.remainingAfterRequests == 27)
        #expect(mine.requests.first?.halfDayStart == false)
        #expect(mine.requests.first?.rangeText == "01.12.2026")
        #expect(mine.requestableTypes.isEmpty)
    }

    @Test("Zeiträume, Status und Tage")
    func texts() {
        #expect(AbsenceText.range(from: "2027-06-07", to: "2027-06-07", halfDayStart: true) == "07.06.2027 (halber Tag)")
        #expect(AbsenceText.range(from: "2027-12-28", to: "2028-01-02", halfDayEnd: true) == "28.12.2027 – 02.01.2028 (letzter Tag halb)")
        #expect(AbsenceText.days(3) == "3")
        let replaced = AbsenceRequestItem(id: UUID(), userId: UUID(), userName: "A", from: "2027-01-04", to: "2027-01-05", status: .cancelled,
                                          workingDays: 2, decisionComment: "Durch Änderungsantrag ersetzt.")
        #expect(replaced.statusText == "Ersetzt")
        #expect(AbsenceRequestStatus.pending.title == "Beantragt")
        let preview = AbsencePreview(workingDays: Decimal(string: "2.5")!, warnings: [], remainingAfter: 17, requiresApproval: true)
        #expect(preview.summary == "2,5 Arbeitstage, danach 17 Tage Resturlaub. Der Antrag geht zur Genehmigung.")
    }

    @Test("Antrag: Prüfung und Normalisierung vor dem Senden")
    func draft() throws {
        var draft = AbsenceRequestDraft(typeId: nil, from: "2027-06-09", to: "2027-06-07")
        #expect(draft.problem == "Bitte eine Art wählen.")
        draft.typeId = UUID()
        #expect(draft.problem == "Das Ende liegt vor dem Beginn.")
        draft.to = "2027-06-09"
        draft.halfDayStart = true
        draft.halfDayEnd = true
        draft.note = "  "
        #expect(draft.problem == nil)
        let normalized = draft.normalized
        #expect(normalized.halfDayEnd == false)
        #expect(normalized.note == nil)
        let body = try #require(String(data: try JSONCoding.encoder.encode(normalized), encoding: .utf8))
        #expect(!body.contains("note"))
        #expect(body.contains("\"halfDayStart\":true"))
        #expect(AbsenceDecision(approve: false, comment: " Bitte verschieben ").comment == "Bitte verschieben")
    }

    @Test("Feldnamen stimmen mit dem API-Vertrag überein")
    func contract() throws {
        let url = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appending(path: "contracts/openapi/v1.json")
        let document = try #require(try JSONSerialization.jsonObject(with: Data(contentsOf: url)) as? [String: Any])
        let schemas = try #require((document["components"] as? [String: Any])?["schemas"] as? [String: [String: Any]])
        func properties(_ name: String) throws -> Set<String> {
            Set(((try #require(schemas[name], "Schema \(name) fehlt"))["properties"] as? [String: Any] ?? [:]).keys)
        }
        let checks: [(String, [String])] = [
            ("MyVacation", ["employeeId", "vacation", "requests", "holidays", "year", "types", "absences", "canApprove", "pendingApprovals"]),
            ("VacationSummary", ["year", "entitlement", "carryOver", "taken", "planned", "remaining", "requested", "annualEntitlement", "adjustment",
                                 "carryOverExpiresOn", "carryOverExpired", "carryOverExpiring", "remainingAfterRequests"]),
            ("AbsenceRequestView", ["id", "userId", "userName", "from", "to", "note", "status", "workingDays", "decisionComment", "warnings",
                                    "remainingAfter", "canDecide", "canCancel", "typeId", "typeName", "typeColor", "halfDayStart", "halfDayEnd",
                                    "substituteName", "replacesRequestId", "substituteUserId", "canChange"]),
            ("AbsenceTypeView", ["id", "key", "name", "code", "color", "kind", "requiresApproval", "countsAsVacation", "requestable", "isActive", "isSickness"]),
            ("MyAbsence", ["id", "from", "to", "typeName", "color", "halfDayStart", "halfDayEnd", "workingDays"]),
            ("AbsencePreview", ["workingDays", "warnings", "remainingAfter", "requiresApproval", "holidays"]),
            ("SaveAbsenceRequestRequest", ["typeId", "from", "to", "halfDayStart", "halfDayEnd", "note", "substituteUserId", "replacesRequestId"]),
            ("DecideAbsenceRequest", ["approve", "comment"]),
        ]
        for (schema, fields) in checks {
            let known = try properties(schema)
            for field in fields { #expect(known.contains(field), "\(schema).\(field) fehlt im Vertrag") }
        }
        let statuses = try #require(schemas["AbsenceRequestStatus"]?["enum"] as? [String])
        #expect(Set(statuses) == Set(AbsenceRequestStatus.allCases.map(\.rawValue)))
    }
}
