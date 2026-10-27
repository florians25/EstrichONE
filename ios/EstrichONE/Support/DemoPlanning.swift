import EstrichCore
import Foundation

/// Planung im Demomodus: Kolonnen, Mitarbeiter, Fahrzeuge und Einsätze im Arbeitsspeicher. Beantwortet dieselben
/// Endpunkte wie der Server (`/planning/day`, `/planning/batch`, Reihenfolge, Tausch, Vorrat, Fahrzeuge), damit sich
/// „Wer fährt wohin“ und die Schnelldisposition ohne Server ausprobieren lassen. Aufruf nur unter der Sperre des FakeServers.
final class DemoPlanning {
    private struct Site {
        let number: String
        let title: String
        let address: String
        let latitude: Double?
        let longitude: Double?
        let contactName: String?
        let contactPhone: String?
        let city: String
        let area: Double
        let status: String
    }

    private struct Crew {
        let id: String
        let name: String
        let color: String
        let leader: String
        var members: [String]
        var vehicles: [String]
    }

    private struct Assignment {
        var day: String
        var crewId: String?
        let bauakteId: String
        var start: String?
        var end: String?
        var activity: String
        var notes: String?
        var sequence: Int
        var version: Int
    }

    private static func id(_ number: Int) -> String { String(format: "0193a1b2-de00-7000-8000-00000000%04d", number) }

    private let people: [String: String] = [
        DemoContent.userId: "Max Huber", id(4002): "Lukas Brandl", id(4003): "Anna Koller", id(4004): "Tobias Lang",
        id(4005): "Emre Kaya", id(4006): "Jonas Wirth", id(4007): "Felix Gruber", id(4008): "Stefan Moser",
    ]
    private let vehicles: [String: (name: String, kind: String, identifier: String?)] = [
        id(5001): ("Estrichpumpe 1", "machine", "Mixokret M3"), id(5002): ("Sprinter LA-EO 12", "vehicle", "LA-EO 12"),
        id(5003): ("Pritsche LA-EO 7", "vehicle", "LA-EO 7"), id(5004): ("Estrichpumpe 2", "machine", "Brinkmann 450"),
    ]
    private let sites: [String: Site] = [
        DemoContent.efh: Site(number: "2026-041", title: "EFH Schneider, Neubau", address: "Ahornweg 3, 84028 Landshut", latitude: 48.5372,
                              longitude: 12.1521, contactName: "Herr Schneider", contactPhone: "0171 2345678", city: "Landshut", area: 142.5,
                              status: "inProgress"),
        DemoContent.halle: Site(number: "2026-042", title: "Halle Gewerbering", address: "Gewerbering 3, 84030 Ergolding", latitude: nil,
                                longitude: nil, contactName: "Bauleitung Huber Bau", contactPhone: "0871 4711", city: "Ergolding", area: 860,
                                status: "planned"),
        DemoContent.praxis: Site(number: "2026-038", title: "Praxis Dr. Maier, Sanierung", address: "Altstadt 12, 84028 Landshut", latitude: nil,
                                 longitude: nil, contactName: "Dr. Maier", contactPhone: "0871 22 33 4", city: "Landshut", area: 96, status: "drying"),
        DemoContent.isarpark: Site(number: "2026-044", title: "MFH Isarpark, Haus B", address: "Am Isarpark 7, 84034 Landshut", latitude: nil,
                                   longitude: nil, contactName: nil, contactPhone: nil, city: "Landshut", area: 512, status: "planned"),
        DemoContent.weber: Site(number: "2026-047", title: "Anbau Weber", address: "Lindenstr. 8, 84137 Vilsbiburg", latitude: nil, longitude: nil,
                                contactName: "Thomas Weber", contactPhone: "0160 998877", city: "Vilsbiburg", area: 38, status: "inquiry"),
    ]
    private var crews: [Crew] = [
        Crew(id: id(6001), name: "Kolonne Huber", color: "#E07A1F", leader: DemoContent.userId, members: [DemoContent.userId, id(4002)],
             vehicles: [id(5001), id(5003)]),
        Crew(id: id(6002), name: "Kolonne Brandl", color: "#2F80ED", leader: id(4003), members: [id(4003), id(4004)], vehicles: [id(5002)]),
        Crew(id: id(6003), name: "Kolonne Kaya", color: "#3A9D6B", leader: id(4005), members: [id(4005), id(4006)], vehicles: [id(5004)]),
    ]
    /// Tagesbesetzung abweichend von der Stammbesetzung: „crewId|Tag“.
    private var rosters: [String: (members: [String], vehicles: [String])] = [:]
    private var assignments: [String: Assignment] = [:]
    /// Freigegebener Stand: Einsatz → Version (heute um 6:30 freigegeben).
    private var released: [String: Int] = [:]
    private let today: String
    private let releasedAt: Date

    init(now: Date = Date()) {
        let calendar = Calendar.current
        func day(_ offset: Int) -> String { CalendarDay.string(calendar.date(byAdding: .day, value: offset, to: now) ?? now) }
        today = day(0)
        releasedAt = calendar.date(bySettingHour: 6, minute: 30, second: 0, of: now) ?? now
        func add(_ number: Int, _ offset: Int, _ crew: Int?, _ site: String, _ start: String?, _ end: String?, _ activity: String,
                 notes: String? = nil, sequence: Int) {
            assignments[Self.id(number)] = Assignment(day: day(offset), crewId: crew.map { Self.id($0) }, bauakteId: site, start: start,
                                                      end: end, activity: activity, notes: notes, sequence: sequence, version: 1)
        }
        add(3001, 0, 6001, DemoContent.efh, "07:00:00", "13:00:00", "Estrich einbringen OG", notes: "Pumpe vorher prüfen, Zufahrt über Hof", sequence: 1)
        add(3008, 0, 6001, DemoContent.praxis, "14:00:00", "15:30:00", "CM-Messung und Belegreife", sequence: 2)
        add(3002, 0, 6002, DemoContent.halle, "08:30:00", "09:30:00", "Erstbegehung", notes: "Zufahrt nur bis 7,5 t", sequence: 1)
        add(3004, 0, 6003, DemoContent.isarpark, "07:00:00", "16:00:00", "Randdämmung und Folie", sequence: 1)
        add(3003, 1, 6001, DemoContent.praxis, "07:30:00", "09:00:00", "CM-Messung und Belegreife", sequence: 1)
        add(3005, 1, 6001, DemoContent.efh, "10:30:00", "14:00:00", "Estrich OG nacharbeiten", sequence: 2)
        add(3009, 1, 6003, DemoContent.isarpark, "07:00:00", nil, "Estrich einbringen Haus B", sequence: 1)
        add(3006, 2, 6001, DemoContent.isarpark, "07:00:00", "09:00:00", "Erstbegehung", sequence: 1)
        add(3007, 3, 6002, DemoContent.halle, "06:30:00", nil, "Estrich einbringen", sequence: 1)
        for (key, value) in assignments where value.day == today && key != Self.id(3008) { released[key] = value.version }
        // Freigegeben, danach im Büro entfernt (erscheint als „Entfallen“).
        released[Self.id(3010)] = 1
    }

    // MARK: Lesen

    private func absence(_ person: String, on day: String) -> String? {
        // Tobias Lang ist heute und morgen im Urlaub.
        guard person == Self.id(4004) else { return nil }
        let tomorrow = CalendarDay.string(Calendar.current.date(byAdding: .day, value: 1, to: CalendarDay.date(today) ?? Date()) ?? Date())
        return day == today || day == tomorrow ? "Urlaub" : nil
    }

    private func roster(_ crew: Crew, _ day: String) -> (members: [String], vehicles: [String], changed: Bool) {
        if let custom = rosters["\(crew.id)|\(day)"] { return (custom.members, custom.vehicles, true) }
        return (crew.members, crew.vehicles, false)
    }

    private func warnings(crew: Crew, day: String) -> [String] {
        let roster = roster(crew, day)
        var result = roster.members.compactMap { person in absence(person, on: day).map { "\(people[person] ?? "") ist im \($0)." } }
        let own = assignments.values.filter { $0.crewId == crew.id && $0.day == day }.sorted { ($0.start ?? "") < ($1.start ?? "") }
        for (first, second) in zip(own, own.dropFirst()) {
            if let end = first.end, let start = second.start, start < end {
                result.append("Überschneidung: \(sites[first.bauakteId]?.title ?? "") und \(sites[second.bauakteId]?.title ?? "").")
            }
        }
        return result
    }

    private func stop(_ id: String, _ a: Assignment, order: Int, crew: Crew?) -> [String: Any] {
        let site = sites[a.bauakteId]
        var change: Any = NSNull()
        if a.day == today {
            if released[id] == nil { change = "new" } else if released[id] != a.version { change = "changed" }
        }
        let members: [String] = crew.map { self.roster($0, a.day).members } ?? []
        let cars: [String] = crew.map { self.roster($0, a.day).vehicles } ?? []
        return [
            "assignmentId": id, "order": order, "sequence": a.sequence, "startTime": a.start ?? NSNull(), "endTime": a.end ?? NSNull(),
            "kind": "screed", "activity": a.activity, "bauakteId": a.bauakteId, "number": site?.number ?? NSNull(), "title": site?.title ?? "",
            "address": site?.address ?? NSNull(), "latitude": site?.latitude ?? NSNull(), "longitude": site?.longitude ?? NSNull(),
            "siteContactName": site?.contactName ?? NSNull(), "siteContactPhone": site?.contactPhone ?? NSNull(), "navigationUrl": NSNull(),
            "notes": a.notes ?? NSNull(),
            "people": members.map { ["id": $0, "name": people[$0] ?? ""] as [String: Any] },
            "resources": cars.map { ["id": $0, "name": vehicles[$0]?.name ?? "", "kind": vehicles[$0]?.kind ?? "vehicle"] as [String: Any] },
            "warnings": [String](), "change": change, "version": a.version,
        ]
    }

    func dayPlan(_ day: String) -> [String: Any] {
        let crewJSON: [[String: Any]] = crews.map { crew -> [String: Any] in
            let roster = roster(crew, day)
            let own = assignments.filter { $0.value.crewId == crew.id && $0.value.day == day }
                .sorted { ($0.value.sequence, $0.value.start ?? "") < ($1.value.sequence, $1.value.start ?? "") }
            return [
                "id": crew.id, "name": crew.name, "color": crew.color, "leaderName": people[crew.leader] ?? NSNull(),
                "members": roster.members.map { ["id": $0, "name": people[$0] ?? "", "absenceNote": absence($0, on: day) ?? NSNull()] as [String: Any] },
                "vehicles": roster.vehicles.map { ["id": $0, "name": vehicles[$0]?.name ?? "", "kind": vehicles[$0]?.kind ?? "vehicle"] as [String: Any] },
                "rosterChanged": roster.changed, "rosterNote": NSNull(), "isMine": roster.members.contains(DemoContent.userId),
                "stops": own.enumerated().map { index, entry in stop(entry.key, entry.value, order: index + 1, crew: crew) },
                "warnings": warnings(crew: crew, day: day),
            ]
        }
        let rostered = Set(crews.flatMap { roster($0, day).members })
        let free = people.keys.filter { !rostered.contains($0) && absence($0, on: day) == nil }.sorted { people[$0]! < people[$1]! }
        let absent = people.keys.filter { absence($0, on: day) != nil }
        let changed = assignments.filter { $0.value.day == day && released[$0.key] != $0.value.version }.count
        let removed: [[String: Any]] = day == today
            ? released.keys.filter { assignments[$0] == nil }.map { ["assignmentId": $0, "version": 1, "title": "Anbau Weber", "crewName": "Kolonne Kaya",
                                                                   "activity": "Erstbegehung"] as [String: Any] }
            : []
        var release: Any = NSNull()
        if day == today {
            release = ["day": day, "releasedAt": APIDate.format(releasedAt), "releasedByName": "Sabine Wagner (Büro)", "note": NSNull(),
                       "changedCount": changed + removed.count, "removed": removed] as [String: Any]
        }
        return [
            "day": day, "holiday": NSNull(), "release": release, "canPlan": true,
            "myCrewIds": crews.filter { roster($0, day).members.contains(DemoContent.userId) }.map(\.id),
            "crews": crewJSON, "withoutCrew": [[String: Any]](),
            "absent": absent.map { ["id": $0, "name": people[$0] ?? "", "absenceNote": absence($0, on: day) ?? ""] as [String: Any] },
            "free": free.map { ["id": $0, "name": people[$0] ?? ""] as [String: Any] },
        ]
    }

    func backlog() -> [[String: Any]] {
        sites.map { id, site -> [String: Any] in
            let upcoming = assignments.values.filter { $0.bauakteId == id && $0.day >= today }
            let parts = site.address.split(separator: ",").map { $0.trimmingCharacters(in: .whitespaces) }
            return [
                "bauakteId": id, "number": site.number, "title": site.title, "status": site.status, "street": parts.first ?? NSNull(),
                "postalCode": parts.last.map { String($0.prefix(5)) } ?? NSNull(), "city": site.city, "plannedStart": NSNull(),
                "plannedEnd": NSNull(), "customerName": NSNull(), "totalAreaSquareMeters": site.area, "screedTypes": [String](),
                "upcomingAssignments": upcoming.count, "nextAssignment": upcoming.map(\.day).min() ?? NSNull(),
            ]
        }
    }

    func resources() -> [[String: Any]] {
        vehicles.map { id, vehicle -> [String: Any] in
            ["id": id, "kind": vehicle.kind, "name": vehicle.name, "identifier": vehicle.identifier ?? NSNull(), "notes": NSNull(),
             "isActive": true, "version": 1]
        }
    }

    // MARK: Ändern

    /// `POST /planning/batch`: Upsert, Move, Delete; `dryRun` prüft nur.
    func batch(_ body: [String: Any]) -> (Int, [String: Any]) {
        let operations = body["operations"] as? [[String: Any]] ?? []
        let dryRun = body["dryRun"] as? Bool ?? false
        let backup = assignments
        var touched: [(crew: String?, day: String)] = []
        for operation in operations {
            guard let id = (operation["id"] as? String)?.lowercased() else { continue }
            switch operation["op"] as? String {
            case "upsert":
                let a = operation["assignment"] as? [String: Any] ?? [:]
                let day = a["day"] as? String ?? today
                let crew = (a["crewId"] as? String)?.lowercased()
                let sequence = (assignments.values.filter { $0.crewId == crew && $0.day == day }.map(\.sequence).max() ?? 0) + 1
                assignments[id] = Assignment(day: day, crewId: crew, bauakteId: (a["bauakteId"] as? String ?? "").lowercased(),
                                             start: a["startTime"] as? String, end: a["endTime"] as? String,
                                             activity: a["activity"] as? String ?? "Estrich einbringen", notes: a["notes"] as? String,
                                             sequence: sequence, version: 1)
                touched.append((crew, day))
            case "move":
                guard var a = assignments[id] else { return notFound(dryRun ? backup : nil) }
                if let day = operation["day"] as? String { a.day = day }
                if operation["changeCrew"] as? Bool == true { a.crewId = (operation["crewId"] as? String)?.lowercased() }
                if operation["setTime"] as? Bool == true {
                    a.start = operation["startTime"] as? String
                    a.end = operation["endTime"] as? String
                }
                a.sequence = 99
                a.version += 1
                assignments[id] = a
                touched.append((a.crewId, a.day))
            case "delete":
                guard assignments.removeValue(forKey: id) != nil else { return notFound(dryRun ? backup : nil) }
            default:
                break
            }
        }
        var found: [String] = []
        for (crewId, day) in touched {
            if let crew = crews.first(where: { $0.id == crewId }) { found += warnings(crew: crew, day: day) }
        }
        if dryRun { assignments = backup }
        return (200, ["assignments": [[String: Any]](), "deletedIds": [String](), "warnings": Array(Set(found)).sorted(), "dryRun": dryRun])
    }

    private func notFound(_ restore: [String: Assignment]?) -> (Int, [String: Any]) {
        if let restore { assignments = restore }
        return (404, ["status": 404, "title": "Nicht gefunden", "detail": "Der Einsatz existiert nicht (mehr).", "code": "NOT_FOUND"])
    }

    /// `PUT /planning/crews/{id}/days/{day}/order`.
    func order(crewId: String, day: String, body: [String: Any]) -> [String: Any] {
        let ids = (body["assignmentIds"] as? [String] ?? []).map { $0.lowercased() }
        let times = body["startTimes"] as? [Any]
        for (index, id) in ids.enumerated() {
            guard var a = assignments[id] else { continue }
            a.sequence = index + 1
            if let time = times?[safe: index] as? String, time != a.start {
                let duration = Self.minutes(a.end).map { $0 - (Self.minutes(a.start) ?? $0) }
                a.start = time
                a.end = duration.flatMap { minutes in Self.minutes(time).map { Self.time($0 + minutes) } } ?? a.end
                a.version += 1
            }
            assignments[id] = a
        }
        return dayPlan(day)
    }

    /// `POST /planning/crews/{id}/days/{day}/swap`: Person oder Fahrzeug für den Tag tauschen (mit Gegentausch).
    func swap(crewId: String, day: String, body: [String: Any]) -> [String: Any] {
        guard let crew = crews.first(where: { $0.id == crewId }) else { return dayPlan(day) }
        var current = roster(crew, day)
        func value(_ key: String) -> String? { (body[key] as? String)?.lowercased() }
        if value("fromUserId") != nil || value("toUserId") != nil {
            if let from = value("fromUserId") { current.members.removeAll { $0 == from } }
            if let to = value("toUserId") {
                if let other = crews.first(where: { $0.id != crewId && roster($0, day).members.contains(to) }) {
                    var theirs = roster(other, day)
                    theirs.members.removeAll { $0 == to }
                    if let from = value("fromUserId") { theirs.members.append(from) }
                    rosters["\(other.id)|\(day)"] = (theirs.members, theirs.vehicles)
                }
                current.members.append(to)
            }
        }
        if let from = value("fromResourceId") { current.vehicles.removeAll { $0 == from } }
        if let to = value("toResourceId") {
            for other in crews where other.id != crewId && roster(other, day).vehicles.contains(to) {
                var theirs = roster(other, day)
                theirs.vehicles.removeAll { $0 == to }
                if let from = value("fromResourceId") { theirs.vehicles.append(from) }
                rosters["\(other.id)|\(day)"] = (theirs.members, theirs.vehicles)
            }
            current.vehicles.append(to)
        }
        rosters["\(crewId)|\(day)"] = (current.members, current.vehicles)
        return dayPlan(day)
    }

    private static func minutes(_ time: String?) -> Int? {
        guard let parts = time?.split(separator: ":").compactMap({ Int($0) }), parts.count >= 2 else { return nil }
        return parts[0] * 60 + parts[1]
    }

    private static func time(_ minutes: Int) -> String { String(format: "%02d:%02d:00", min(23, minutes / 60), minutes % 60) }
}

private extension Array {
    subscript(safe index: Int) -> Element? { indices.contains(index) ? self[index] : nil }
}
