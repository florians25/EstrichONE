import EstrichCore
import Foundation

/// Urlaub und Abwesenheiten im Demomodus: Konto mit Übertrag, eigene Anträge (auch Änderungsantrag und Vertretung), Vorschau
/// (Montag bis Freitag ohne Feiertage in Bayern) und zwei offene Anträge zur Genehmigung. Aufruf nur unter der Sperre des
/// FakeServers; Antworten als JSON-Objekte.
final class DemoAbsences {
    private struct Request {
        let id: String
        let userId: String
        let userName: String
        var from: String
        var to: String
        var typeId: String
        var halfDayStart: Bool
        var halfDayEnd: Bool
        var note: String?
        var status: String
        var comment: String?
        var substitute: String?
        var warnings: [String]
        var substituteId: String? = nil
        var replaces: String? = nil
    }

    /// Kollegen für die Vertretung (wie `directory/users` im Demomodus).
    static let colleagues: [(id: String, name: String)] = [
        ("0193a1b2-0000-7000-8000-00000000bbbb", "Lukas Brandl"), ("0193a1b2-0000-7000-8000-00000000cccc", "Sabine Wagner"),
        ("0193a1b2-0000-7000-8000-00000000dddd", "Anna Koller"), ("0193a1b2-0000-7000-8000-00000000eeee", "Tobias Lang"),
    ]

    private static let vacation = "01a10000-0000-7000-8000-000000000001"
    private static let school = "01a10000-0000-7000-8000-000000000004"
    private static let overtime = "01a10000-0000-7000-8000-000000000007"
    private static let special = "01a10000-0000-7000-8000-000000000006"
    private static var types: [[String: Any]] { [
        type(vacation, "vacation", "Urlaub", "U", "#43A047", "vacation", approval: true, counts: true),
        type(school, "school", "Berufsschule", "B", "#1E88E5", "school", approval: false, counts: false),
        type("01a10000-0000-7000-8000-000000000005", "training", "Fortbildung", "F", "#00ACC1", "training", approval: true, counts: false),
        type(special, "special_leave", "Sonderurlaub", "SU", "#8E24AA", "specialLeave", approval: true, counts: false),
        type(overtime, "overtime", "Überstundenabbau", "ÜA", "#FB8C00", "other", approval: true, counts: false),
    ] }

    private static func type(_ id: String, _ key: String, _ name: String, _ code: String, _ color: String, _ kind: String, approval: Bool, counts: Bool) -> [String: Any] {
        ["id": id, "key": key, "name": name, "code": code, "color": color, "kind": kind, "requiresApproval": approval, "countsAsVacation": counts,
         "requestable": true, "isPaid": true, "isActive": true, "sortOrder": 10, "isSystem": true, "isSickness": false]
    }

    private let userId: String
    private var requests: [Request] = []
    private var counter = 0

    init(userId: String) {
        self.userId = userId
        let calendar = Calendar.current
        func monday(weeks: Int) -> Date {
            var date = calendar.date(byAdding: .day, value: weeks * 7, to: .now) ?? .now
            while calendar.component(.weekday, from: date) != 2 { date = calendar.date(byAdding: .day, value: 1, to: date) ?? date }
            return date
        }
        func day(_ date: Date, plus days: Int = 0) -> String { CalendarDay.string(calendar.date(byAdding: .day, value: days, to: date) ?? date) }
        let summer = monday(weeks: 9)
        let spring = monday(weeks: 3)
        let later = monday(weeks: 6)
        // Weihnachtsurlaub über die Feiertage (zeigt, dass Feiertage nicht als Urlaubstage zählen).
        let year = calendar.component(.year, from: .now)
        let christmasYear = calendar.component(.month, from: .now) == 12 && calendar.component(.day, from: .now) > 14 ? year + 1 : year
        let christmas = (from: "\(christmasYear)-12-22", to: "\(christmasYear + 1)-01-06")
        requests = [
            Request(id: "0193a1b2-ab00-7000-8000-000000000005", userId: userId, userName: "Max Huber", from: christmas.from, to: christmas.to,
                    typeId: Self.vacation, halfDayStart: false, halfDayEnd: false, note: "Weihnachten", status: "approved", comment: nil,
                    substitute: nil, warnings: []),
            Request(id: "0193a1b2-ab00-7000-8000-000000000001", userId: userId, userName: "Max Huber", from: day(summer), to: day(summer, plus: 11),
                    typeId: Self.vacation, halfDayStart: false, halfDayEnd: false, note: "Familienurlaub", status: "approved", comment: nil,
                    substitute: "Lukas Brandl", warnings: [], substituteId: "0193a1b2-0000-7000-8000-00000000bbbb"),
            Request(id: "0193a1b2-ab00-7000-8000-000000000002", userId: userId, userName: "Max Huber", from: day(spring, plus: 4), to: day(spring, plus: 4),
                    typeId: Self.overtime, halfDayStart: true, halfDayEnd: false, note: nil, status: "pending", comment: nil, substitute: nil, warnings: []),
            Request(id: "0193a1b2-ab00-7000-8000-000000000003", userId: "0193a1b2-0000-7000-8000-00000000bbbb", userName: "Lukas Brandl",
                    from: day(later), to: day(later, plus: 4), typeId: Self.vacation, halfDayStart: false, halfDayEnd: false, note: "Hochzeit",
                    status: "pending", comment: nil, substitute: nil,
                    warnings: ["Kolonne Huber: am \(Self.german(day(later)).prefix(6)) nur 2 von 4 anwesend (Mindestbesetzung 3).", "Im Zeitraum sind 3 Einsätze eingeplant."]),
            Request(id: "0193a1b2-ab00-7000-8000-000000000004", userId: "0193a1b2-0000-7000-8000-00000000cccc", userName: "Sabine Wagner",
                    from: day(later, plus: 14), to: day(later, plus: 15), typeId: Self.special, halfDayStart: false, halfDayEnd: false, note: "Umzug",
                    status: "pending", comment: nil, substitute: nil, warnings: []),
        ]
    }

    /// JSONSerialization kennt kein Optional: fehlende Werte als null.
    private static func value(_ text: String?) -> Any { text.map { $0 as Any } ?? NSNull() }

    private static func german(_ day: String) -> String {
        let p = day.split(separator: "-")
        return p.count == 3 ? "\(p[2]).\(p[1]).\(p[0])" : day
    }

    /// Arbeitstage Montag bis Freitag ohne gesetzliche Feiertage (Bayern, wie der Demo-Betrieb in Landshut).
    private static func workingDays(_ from: String, _ to: String, _ halfStart: Bool, _ halfEnd: Bool) -> Double {
        guard let start = CalendarDay.date(from), let end = CalendarDay.date(to), start <= end else { return 0 }
        let holidays = Set(PublicHolidays.bavaria(from: from, to: to).map(\.date))
        var days = 0.0
        var date = start
        let calendar = Calendar.current
        while date <= end {
            let weekday = calendar.component(.weekday, from: date)
            if weekday != 1 && weekday != 7 && !holidays.contains(CalendarDay.string(date)) {
                let text = CalendarDay.string(date)
                days += (text == from && halfStart) || (text == to && halfEnd && from != to) ? 0.5 : 1
            }
            date = calendar.date(byAdding: .day, value: 1, to: date) ?? end.addingTimeInterval(86_400)
        }
        return days
    }

    private static func holidayList(_ from: String, _ to: String) -> [[String: Any]] {
        PublicHolidays.bavaria(from: from, to: to).map { ["date": $0.date, "name": $0.name] }
    }

    /// Genehmigt, noch nicht begonnen und keine Änderung offen.
    private func canChange(_ r: Request) -> Bool {
        r.userId == userId && r.status == "approved" && r.from > CalendarDay.string(.now)
            && !requests.contains { $0.replaces == r.id && $0.status == "pending" }
    }

    private func view(_ r: Request) -> [String: Any] {
        let type = Self.types.first { $0["id"] as? String == r.typeId }
        let mine = r.userId == userId
        return ["id": r.id, "userId": r.userId, "userName": r.userName, "from": r.from, "to": r.to, "kind": type?["kind"] ?? "vacation",
                "note": Self.value(r.note), "status": r.status, "workingDays": Self.workingDays(r.from, r.to, r.halfDayStart, r.halfDayEnd),
                "requestedByUserId": r.userId, "requestedByName": r.userName, "requestedAt": APIDate.format(.now), "decidedByUserId": NSNull(),
                "decidedByName": NSNull(), "decidedAt": NSNull(), "decisionComment": Self.value(r.comment), "warnings": r.warnings,
                "remainingAfter": mine ? NSNull() as Any : 14.0 as Any, "canDecide": !mine && r.status == "pending",
                "canCancel": mine && (r.status == "pending" || r.status == "approved"), "version": 1, "typeId": r.typeId,
                "typeName": type?["name"] ?? "Urlaub", "typeColor": type?["color"] ?? "#43A047", "halfDayStart": r.halfDayStart,
                "halfDayEnd": r.halfDayEnd, "substituteUserId": Self.value(r.substituteId), "substituteName": Self.value(r.substitute),
                "replacesRequestId": Self.value(r.replaces), "canChange": canChange(r), "countsAsVacation": r.typeId == Self.vacation]
    }

    private var taken: Double { 6 }

    private var planned: Double {
        requests.filter { $0.userId == userId && $0.status == "approved" && $0.typeId == Self.vacation }
            .reduce(0) { $0 + Self.workingDays($1.from, $1.to, $1.halfDayStart, $1.halfDayEnd) }
    }

    private var requested: Double {
        requests.filter { $0.userId == userId && $0.status == "pending" && $0.typeId == Self.vacation }
            .reduce(0) { $0 + Self.workingDays($1.from, $1.to, $1.halfDayStart, $1.halfDayEnd) }
    }

    func mine() -> [String: Any] {
        let year = Calendar.current.component(.year, from: .now)
        let remaining = 30 + 4 - taken - planned
        let account: [String: Any] = [
            "year": year, "entitlement": 30, "carryOver": 4, "taken": taken, "planned": planned, "remaining": remaining, "sickDays": 0,
            "requested": requested, "annualEntitlement": 30, "adjustment": 0, "carryOverExpiresOn": "\(year + 1)-03-31", "carryOverExpired": 0,
            "carryOverExpiring": 0, "remainingAfterRequests": remaining - requested, "byType": [], "note": NSNull(),
        ]
        let own = requests.filter { $0.userId == userId }
        let absences: [[String: Any]] = own.filter { $0.status == "approved" }.map { r in
            let type = Self.types.first { $0["id"] as? String == r.typeId }
            return ["id": "0193a1b2-ab10-7000-8000-" + String(r.id.suffix(12)), "from": r.from, "to": r.to, "typeId": r.typeId,
                    "typeName": type?["name"] ?? "Urlaub", "color": type?["color"] ?? "#43A047", "kind": type?["kind"] ?? "vacation",
                    "halfDayStart": r.halfDayStart, "halfDayEnd": r.halfDayEnd, "workingDays": Self.workingDays(r.from, r.to, r.halfDayStart, r.halfDayEnd),
                    "requestId": r.id]
        }
        return ["employeeId": "0193a1b2-ab20-7000-8000-000000000001", "vacation": account, "requests": own.map(view),
                "holidays": Self.holidayList("\(year)-01-01", "\(year)-12-31"), "year": year,
                "types": Self.types, "closures": [], "absences": absences, "canApprove": true,
                "pendingApprovals": requests.filter { $0.userId != userId && $0.status == "pending" }.count]
    }

    func list(scope: String?) -> [[String: Any]] {
        requests.filter { scope != "pending" || $0.status == "pending" }.filter { scope == "mine" ? $0.userId == userId : true }.map(view)
    }

    func preview(_ body: [String: Any]) -> [String: Any] {
        let from = body["from"] as? String ?? ""
        let to = body["to"] as? String ?? from
        let days = Self.workingDays(from, to, body["halfDayStart"] as? Bool ?? false, body["halfDayEnd"] as? Bool ?? false)
        let typeId = (body["typeId"] as? String)?.lowercased() ?? Self.vacation
        let counts = typeId == Self.vacation
        let type = Self.types.first { $0["id"] as? String == typeId }
        return ["workingDays": days, "warnings": days > 3 ? ["Kolonne Huber: 2 von 4 wären gleichzeitig abwesend."] : [],
                "remainingAfter": counts ? (30 + 4 - taken - planned - requested - days) as Any : NSNull() as Any,
                "requiresApproval": type?["requiresApproval"] as? Bool ?? true, "holidays": Self.holidayList(from, to)]
    }

    func save(id: String, body: [String: Any]) -> [String: Any] {
        counter += 1
        let typeId = (body["typeId"] as? String)?.lowercased() ?? Self.vacation
        let replaces = (body["replacesRequestId"] as? String)?.lowercased()
        // Änderungsanträge gehen immer zur Genehmigung.
        let typeNeedsApproval = Self.types.first { $0["id"] as? String == typeId }?["requiresApproval"] as? Bool ?? true
        let approval = replaces != nil || typeNeedsApproval
        let substituteId = (body["substituteUserId"] as? String)?.lowercased()
        let request = Request(id: id, userId: userId, userName: "Max Huber", from: body["from"] as? String ?? "", to: body["to"] as? String ?? "",
                              typeId: typeId, halfDayStart: body["halfDayStart"] as? Bool ?? false, halfDayEnd: body["halfDayEnd"] as? Bool ?? false,
                              note: body["note"] as? String, status: approval ? "pending" : "approved",
                              comment: approval ? nil : "Keine Genehmigung erforderlich.",
                              substitute: Self.colleagues.first { $0.id == substituteId }?.name, warnings: [], substituteId: substituteId, replaces: replaces)
        requests.removeAll { $0.id == id }
        requests.insert(request, at: 0)
        return view(request)
    }

    func decide(id: String, body: [String: Any]) -> [String: Any]? {
        guard let index = requests.firstIndex(where: { $0.id == id }) else { return nil }
        let approve = body["approve"] as? Bool == true
        requests[index].status = approve ? "approved" : "rejected"
        requests[index].comment = body["comment"] as? String
        // Genehmigter Änderungsantrag ersetzt den bisherigen Antrag.
        if approve, let old = requests[index].replaces, let oldIndex = requests.firstIndex(where: { $0.id == old }) {
            requests[oldIndex].status = "cancelled"
            requests[oldIndex].comment = "Durch Änderungsantrag ersetzt."
        }
        return view(requests[index])
    }

    func cancel(id: String) -> [String: Any]? {
        guard let index = requests.firstIndex(where: { $0.id == id }) else { return nil }
        requests[index].status = "cancelled"
        return view(requests[index])
    }
}
