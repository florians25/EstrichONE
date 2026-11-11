import Foundation

/// Abwesenheitsart (Urlaub, Krankheit, Kind krank, Berufsschule …) mit Farbe und Regeln.
public struct AbsenceType: Codable, Equatable, Identifiable, Sendable {
    public let id: UUID
    public let key: String
    public let name: String
    public let code: String
    public let color: String
    public let kind: String
    public let requiresApproval: Bool
    public let countsAsVacation: Bool
    public let requestable: Bool
    public let isActive: Bool
    public let isSickness: Bool

    public init(id: UUID, key: String, name: String, code: String, color: String, kind: String, requiresApproval: Bool, countsAsVacation: Bool,
                requestable: Bool, isActive: Bool = true, isSickness: Bool = false) {
        self.id = id
        self.key = key
        self.name = name
        self.code = code
        self.color = color
        self.kind = kind
        self.requiresApproval = requiresApproval
        self.countsAsVacation = countsAsVacation
        self.requestable = requestable
        self.isActive = isActive
        self.isSickness = isSickness
    }
}

/// Urlaubskonto eines Jahres.
public struct VacationAccount: Codable, Equatable, Sendable {
    public let year: Int
    public let entitlement: Decimal
    public let carryOver: Decimal
    public let taken: Decimal
    public let planned: Decimal
    public let remaining: Decimal
    public let requested: Decimal
    public let annualEntitlement: Decimal
    public let adjustment: Decimal
    public let carryOverExpiresOn: String?
    public let carryOverExpired: Decimal
    public let carryOverExpiring: Decimal
    public let remainingAfterRequests: Decimal

    public init(year: Int, entitlement: Decimal, carryOver: Decimal, taken: Decimal, planned: Decimal, remaining: Decimal, requested: Decimal,
                annualEntitlement: Decimal, adjustment: Decimal = 0, carryOverExpiresOn: String? = nil, carryOverExpired: Decimal = 0,
                carryOverExpiring: Decimal = 0, remainingAfterRequests: Decimal) {
        self.year = year
        self.entitlement = entitlement
        self.carryOver = carryOver
        self.taken = taken
        self.planned = planned
        self.remaining = remaining
        self.requested = requested
        self.annualEntitlement = annualEntitlement
        self.adjustment = adjustment
        self.carryOverExpiresOn = carryOverExpiresOn
        self.carryOverExpired = carryOverExpired
        self.carryOverExpiring = carryOverExpiring
        self.remainingAfterRequests = remainingAfterRequests
    }

    /// Ältere Server ohne Konto-Erweiterung: fehlende Angaben als 0.
    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        year = try c.decode(Int.self, forKey: .year)
        entitlement = try c.decode(Decimal.self, forKey: .entitlement)
        carryOver = try c.decode(Decimal.self, forKey: .carryOver)
        taken = try c.decode(Decimal.self, forKey: .taken)
        planned = try c.decode(Decimal.self, forKey: .planned)
        remaining = try c.decode(Decimal.self, forKey: .remaining)
        requested = try c.decodeIfPresent(Decimal.self, forKey: .requested) ?? 0
        annualEntitlement = try c.decodeIfPresent(Decimal.self, forKey: .annualEntitlement) ?? entitlement
        adjustment = try c.decodeIfPresent(Decimal.self, forKey: .adjustment) ?? 0
        carryOverExpiresOn = try c.decodeIfPresent(String.self, forKey: .carryOverExpiresOn)
        carryOverExpired = try c.decodeIfPresent(Decimal.self, forKey: .carryOverExpired) ?? 0
        carryOverExpiring = try c.decodeIfPresent(Decimal.self, forKey: .carryOverExpiring) ?? 0
        remainingAfterRequests = try c.decodeIfPresent(Decimal.self, forKey: .remainingAfterRequests) ?? remaining - requested
    }
}

public enum AbsenceRequestStatus: String, Codable, CaseIterable, Sendable {
    case pending, approved, rejected, cancelled

    public var title: String {
        switch self {
        case .pending: "Beantragt"
        case .approved: "Genehmigt"
        case .rejected: "Abgelehnt"
        case .cancelled: "Zurückgezogen"
        }
    }
}

/// Antrag (eigener oder zur Genehmigung) mit Arbeitstagen, Hinweisen und erlaubten Aktionen.
public struct AbsenceRequestItem: Codable, Equatable, Identifiable, Sendable {
    public let id: UUID
    public let userId: UUID
    public let userName: String
    public let from: String
    public let to: String
    public let note: String?
    public let status: AbsenceRequestStatus
    public let workingDays: Decimal
    public let decisionComment: String?
    public let warnings: [String]
    public let remainingAfter: Decimal?
    public let canDecide: Bool
    public let canCancel: Bool
    public let typeId: UUID?
    public let typeName: String?
    public let typeColor: String?
    public let halfDayStart: Bool
    public let halfDayEnd: Bool
    public let substituteName: String?
    public let replacesRequestId: UUID?
    /// Vertretung (Benutzer), falls eingetragen.
    public let substituteUserId: UUID?
    /// Genehmigt und noch nicht begonnen: Änderungsantrag möglich.
    public let canChange: Bool

    public init(id: UUID, userId: UUID, userName: String, from: String, to: String, note: String? = nil, status: AbsenceRequestStatus,
                workingDays: Decimal, decisionComment: String? = nil, warnings: [String] = [], remainingAfter: Decimal? = nil, canDecide: Bool = false,
                canCancel: Bool = false, typeId: UUID? = nil, typeName: String? = nil, typeColor: String? = nil, halfDayStart: Bool = false,
                halfDayEnd: Bool = false, substituteName: String? = nil, replacesRequestId: UUID? = nil, substituteUserId: UUID? = nil,
                canChange: Bool = false) {
        self.id = id
        self.userId = userId
        self.userName = userName
        self.from = from
        self.to = to
        self.note = note
        self.status = status
        self.workingDays = workingDays
        self.decisionComment = decisionComment
        self.warnings = warnings
        self.remainingAfter = remainingAfter
        self.canDecide = canDecide
        self.canCancel = canCancel
        self.typeId = typeId
        self.typeName = typeName
        self.typeColor = typeColor
        self.halfDayStart = halfDayStart
        self.halfDayEnd = halfDayEnd
        self.substituteName = substituteName
        self.replacesRequestId = replacesRequestId
        self.substituteUserId = substituteUserId
        self.canChange = canChange
    }

    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = try c.decode(UUID.self, forKey: .id)
        userId = try c.decode(UUID.self, forKey: .userId)
        userName = try c.decode(String.self, forKey: .userName)
        from = try c.decode(String.self, forKey: .from)
        to = try c.decode(String.self, forKey: .to)
        note = try c.decodeIfPresent(String.self, forKey: .note)
        status = try c.decode(AbsenceRequestStatus.self, forKey: .status)
        workingDays = try c.decode(Decimal.self, forKey: .workingDays)
        decisionComment = try c.decodeIfPresent(String.self, forKey: .decisionComment)
        warnings = try c.decodeIfPresent([String].self, forKey: .warnings) ?? []
        remainingAfter = try c.decodeIfPresent(Decimal.self, forKey: .remainingAfter)
        canDecide = try c.decodeIfPresent(Bool.self, forKey: .canDecide) ?? false
        canCancel = try c.decodeIfPresent(Bool.self, forKey: .canCancel) ?? false
        typeId = try c.decodeIfPresent(UUID.self, forKey: .typeId)
        typeName = try c.decodeIfPresent(String.self, forKey: .typeName)
        typeColor = try c.decodeIfPresent(String.self, forKey: .typeColor)
        halfDayStart = try c.decodeIfPresent(Bool.self, forKey: .halfDayStart) ?? false
        halfDayEnd = try c.decodeIfPresent(Bool.self, forKey: .halfDayEnd) ?? false
        substituteName = try c.decodeIfPresent(String.self, forKey: .substituteName)
        replacesRequestId = try c.decodeIfPresent(UUID.self, forKey: .replacesRequestId)
        substituteUserId = try c.decodeIfPresent(UUID.self, forKey: .substituteUserId)
        canChange = try c.decodeIfPresent(Bool.self, forKey: .canChange) ?? false
    }

    /// Zeitraum für die Anzeige, z. B. „07.06. – 09.06.2027 (erster Tag halb)“.
    public var rangeText: String { AbsenceText.range(from: from, to: to, halfDayStart: halfDayStart, halfDayEnd: halfDayEnd) }

    /// Status für die Anzeige („Ersetzt“ nach genehmigtem Änderungsantrag).
    public var statusText: String {
        status == .cancelled && decisionComment == "Durch Änderungsantrag ersetzt." ? "Ersetzt" : status.title
    }

    /// Entwurf für einen Änderungsantrag: gleicher Zeitraum, gleiche Art und Vertretung, bezogen auf diesen Antrag.
    public var changeDraft: AbsenceRequestDraft {
        AbsenceRequestDraft(typeId: typeId, from: from, to: to, halfDayStart: halfDayStart, halfDayEnd: halfDayEnd, note: note,
                            substituteUserId: substituteUserId, replacesRequestId: id)
    }
}

extension MyVacation {
    /// Offener Änderungsantrag zu einem genehmigten Antrag (dann kein zweiter möglich).
    public func pendingChange(of request: AbsenceRequestItem) -> AbsenceRequestItem? {
        requests.first { $0.replacesRequestId == request.id && $0.status == .pending }
    }

    /// „Ändern“ anbieten: genehmigt, noch nicht begonnen und noch keine Änderung offen.
    public func canRequestChange(_ request: AbsenceRequestItem) -> Bool {
        request.canChange && request.status == .approved && pendingChange(of: request) == nil
    }
}

/// Eigene eingetragene Abwesenheit.
public struct MyAbsence: Codable, Equatable, Identifiable, Sendable {
    public let id: UUID
    public let from: String
    public let to: String
    public let typeName: String
    public let color: String
    public let halfDayStart: Bool
    public let halfDayEnd: Bool
    public let workingDays: Decimal
}

public struct Holiday: Codable, Equatable, Sendable {
    public let date: String
    public let name: String

    public init(date: String, name: String) {
        self.date = date
        self.name = name
    }
}

/// Gesetzliche Feiertage (bundesweit plus Bayern) ohne Server, z. B. für den Demomodus. Im Betrieb rechnet der Server
/// mit dem eingestellten Bundesland.
public enum PublicHolidays {
    /// Ostersonntag (Gaußsche Osterformel, gregorianisch).
    public static func easter(_ year: Int) -> DateComponents {
        let a = year % 19, b = year / 100, c = year % 100, d = b / 4, e = b % 4
        let f = (b + 8) / 25, g = (b - f + 1) / 3, h = (19 * a + b - d - g + 15) % 30
        let i = c / 4, k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7
        let m = (a + 11 * h + 22 * l) / 451
        let month = (h + l - 7 * m + 114) / 31
        let day = (h + l - 7 * m + 114) % 31 + 1
        return DateComponents(year: year, month: month, day: day)
    }

    /// Feiertage eines Jahres in Bayern (ohne Augsburger Friedensfest), sortiert, Datum im Format `JJJJ-MM-TT`.
    public static func bavaria(_ year: Int) -> [Holiday] {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = TimeZone(identifier: "UTC") ?? .current
        guard let easter = calendar.date(from: easter(year)) else { return [] }
        func fixed(_ month: Int, _ day: Int, _ name: String) -> Holiday {
            Holiday(date: String(format: "%04d-%02d-%02d", year, month, day), name: name)
        }
        func moving(_ offset: Int, _ name: String) -> Holiday? {
            guard let date = calendar.date(byAdding: .day, value: offset, to: easter) else { return nil }
            let parts = calendar.dateComponents([.year, .month, .day], from: date)
            return Holiday(date: String(format: "%04d-%02d-%02d", parts.year ?? year, parts.month ?? 1, parts.day ?? 1), name: name)
        }
        let list: [Holiday?] = [
            fixed(1, 1, "Neujahr"), fixed(1, 6, "Heilige Drei Könige"), moving(-2, "Karfreitag"), moving(1, "Ostermontag"),
            fixed(5, 1, "Tag der Arbeit"), moving(39, "Christi Himmelfahrt"), moving(50, "Pfingstmontag"), moving(60, "Fronleichnam"),
            fixed(8, 15, "Mariä Himmelfahrt"), fixed(10, 3, "Tag der Deutschen Einheit"), fixed(11, 1, "Allerheiligen"),
            fixed(12, 25, "1. Weihnachtstag"), fixed(12, 26, "2. Weihnachtstag"),
        ]
        return list.compactMap { $0 }.sorted { $0.date < $1.date }
    }

    /// Feiertage zwischen `from` und `to` (einschließlich, `JJJJ-MM-TT`).
    public static func bavaria(from: String, to: String) -> [Holiday] {
        guard let first = Int(from.prefix(4)), let last = Int(to.prefix(4)), first <= last else { return [] }
        return (first...last).flatMap { bavaria($0) }.filter { $0.date >= from && $0.date <= to }
    }
}

/// Eigene Übersicht (GET hr/me).
public struct MyVacation: Codable, Equatable, Sendable {
    public let employeeId: UUID?
    public let vacation: VacationAccount?
    public let requests: [AbsenceRequestItem]
    public let holidays: [Holiday]
    public let year: Int
    public let types: [AbsenceType]?
    public let absences: [MyAbsence]?
    public let canApprove: Bool
    public let pendingApprovals: Int

    public init(employeeId: UUID?, vacation: VacationAccount?, requests: [AbsenceRequestItem], holidays: [Holiday] = [], year: Int,
                types: [AbsenceType]? = nil, absences: [MyAbsence]? = nil, canApprove: Bool = false, pendingApprovals: Int = 0) {
        self.employeeId = employeeId
        self.vacation = vacation
        self.requests = requests
        self.holidays = holidays
        self.year = year
        self.types = types
        self.absences = absences
        self.canApprove = canApprove
        self.pendingApprovals = pendingApprovals
    }

    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        employeeId = try c.decodeIfPresent(UUID.self, forKey: .employeeId)
        vacation = try c.decodeIfPresent(VacationAccount.self, forKey: .vacation)
        requests = try c.decodeIfPresent([AbsenceRequestItem].self, forKey: .requests) ?? []
        holidays = try c.decodeIfPresent([Holiday].self, forKey: .holidays) ?? []
        year = try c.decodeIfPresent(Int.self, forKey: .year) ?? vacation?.year ?? Calendar.current.component(.year, from: .now)
        types = try c.decodeIfPresent([AbsenceType].self, forKey: .types)
        absences = try c.decodeIfPresent([MyAbsence].self, forKey: .absences)
        canApprove = try c.decodeIfPresent(Bool.self, forKey: .canApprove) ?? false
        pendingApprovals = try c.decodeIfPresent(Int.self, forKey: .pendingApprovals) ?? 0
    }

    /// Arten, die der Mitarbeiter selbst beantragen darf.
    public var requestableTypes: [AbsenceType] { (types ?? []).filter { $0.isActive && $0.requestable && !$0.isSickness } }
}

/// Vorschau vor dem Absenden.
public struct AbsencePreview: Codable, Equatable, Sendable {
    public let workingDays: Decimal
    public let warnings: [String]
    public let remainingAfter: Decimal?
    public let requiresApproval: Bool
    /// Feiertage im Zeitraum (zählen nicht als Arbeitstage).
    public let holidays: [Holiday]

    public init(workingDays: Decimal, warnings: [String], remainingAfter: Decimal?, requiresApproval: Bool, holidays: [Holiday] = []) {
        self.workingDays = workingDays
        self.warnings = warnings
        self.remainingAfter = remainingAfter
        self.requiresApproval = requiresApproval
        self.holidays = holidays
    }

    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        workingDays = try c.decode(Decimal.self, forKey: .workingDays)
        warnings = try c.decodeIfPresent([String].self, forKey: .warnings) ?? []
        remainingAfter = try c.decodeIfPresent(Decimal.self, forKey: .remainingAfter)
        requiresApproval = try c.decodeIfPresent(Bool.self, forKey: .requiresApproval) ?? true
        holidays = try c.decodeIfPresent([Holiday].self, forKey: .holidays) ?? []
    }

    public var summary: String {
        let days = "\(AbsenceText.days(workingDays)) Arbeitstage"
        let rest = remainingAfter.map { ", danach \(AbsenceText.days($0)) Tage Resturlaub" } ?? ""
        return days + rest + "." + (requiresApproval ? " Der Antrag geht zur Genehmigung." : " Keine Genehmigung erforderlich.")
    }

    /// „Feiertage im Zeitraum: 25.12. 1. Weihnachtstag, …“ oder `nil`.
    public var holidayText: String? {
        guard !holidays.isEmpty else { return nil }
        return "Feiertage im Zeitraum (nicht mitgezählt): " + holidays.map { "\(AbsenceText.shortDay($0.date)) \($0.name)" }.joined(separator: ", ")
    }
}

/// Antrag stellen (PUT hr/absence-requests/{id}) bzw. Vorschau (POST hr/absence-requests/preview).
public struct AbsenceRequestDraft: Codable, Equatable, Sendable {
    public var typeId: UUID?
    public var from: String
    public var to: String
    public var halfDayStart: Bool
    public var halfDayEnd: Bool
    public var note: String?
    /// Vertretung während der Abwesenheit (optional).
    public var substituteUserId: UUID?
    public var replacesRequestId: UUID?

    public init(typeId: UUID?, from: String, to: String, halfDayStart: Bool = false, halfDayEnd: Bool = false, note: String? = nil,
                substituteUserId: UUID? = nil, replacesRequestId: UUID? = nil) {
        self.typeId = typeId
        self.from = from
        self.to = to
        self.halfDayStart = halfDayStart
        self.halfDayEnd = halfDayEnd
        self.note = note
        self.substituteUserId = substituteUserId
        self.replacesRequestId = replacesRequestId
    }

    /// Fehlertext für das Formular oder `nil`, wenn der Antrag gesendet werden kann.
    public var problem: String? {
        if typeId == nil { return "Bitte eine Art wählen." }
        if to < from { return "Das Ende liegt vor dem Beginn." }
        return nil
    }

    /// Für den Server: eintägig mit beiden Angaben bedeutet einen halben Tag; leere Bemerkung wird nicht gesendet.
    public var normalized: AbsenceRequestDraft {
        var copy = self
        if from == to && halfDayStart { copy.halfDayEnd = false }
        copy.note = note?.trimmingCharacters(in: .whitespacesAndNewlines).nilIfEmpty
        return copy
    }
}

public struct AbsenceDecision: Codable, Equatable, Sendable {
    public let approve: Bool
    public let comment: String?

    public init(approve: Bool, comment: String?) {
        self.approve = approve
        self.comment = comment?.trimmingCharacters(in: .whitespacesAndNewlines).nilIfEmpty
    }
}

/// Texte für Zeiträume und Tage (deutsch, ohne Rechnung auf dem Gerät: Arbeitstage rechnet der Server).
public enum AbsenceText {
    private static func german(_ day: String) -> String {
        let parts = day.split(separator: "-")
        guard parts.count == 3 else { return day }
        return "\(parts[2]).\(parts[1]).\(parts[0])"
    }

    /// „25.12.“ aus „2026-12-25“.
    public static func shortDay(_ day: String) -> String { String(german(day).prefix(6)) }

    public static func range(from: String, to: String, halfDayStart: Bool = false, halfDayEnd: Bool = false) -> String {
        if from == to { return german(from) + (halfDayStart || halfDayEnd ? " (halber Tag)" : "") }
        let start = from.prefix(4) == to.prefix(4) ? String(german(from).prefix(6)) : german(from)
        let half = [halfDayStart ? "erster Tag halb" : nil, halfDayEnd ? "letzter Tag halb" : nil].compactMap { $0 }.joined(separator: ", ")
        return "\(start) – \(german(to))" + (half.isEmpty ? "" : " (\(half))")
    }

    /// Tage mit höchstens einer Nachkommastelle, z. B. „2,5“.
    public static func days(_ value: Decimal) -> String {
        let number = NSDecimalNumber(decimal: value).doubleValue
        let formatter = NumberFormatter()
        formatter.locale = Locale(identifier: "de_DE")
        formatter.minimumFractionDigits = 0
        formatter.maximumFractionDigits = 1
        return formatter.string(from: NSNumber(value: number)) ?? "\(number)"
    }
}

private extension String {
    var nilIfEmpty: String? { isEmpty ? nil : self }
}

extension AuthorizedClient {
    /// Eigene Übersicht: Konto, Anträge, Abwesenheiten, beantragbare Arten (nur online).
    public func myVacation(year: Int? = nil) async throws(APIError) -> MyVacation {
        try await get("hr/me", query: year.map { [URLQueryItem(name: "year", value: String($0))] } ?? [])
    }

    /// Offene Anträge, über die der Benutzer entscheiden darf (Genehmiger, ggf. Kolonnenführer).
    public func pendingAbsenceRequests() async throws(APIError) -> [AbsenceRequestItem] {
        let list: [AbsenceRequestItem] = try await get("hr/absence-requests", query: [URLQueryItem(name: "scope", value: "pending")])
        return list.filter(\.canDecide)
    }

    public func previewAbsence(_ draft: AbsenceRequestDraft) async throws(APIError) -> AbsencePreview {
        try await post("hr/absence-requests/preview", body: draft.normalized)
    }

    /// Antrag stellen; die ID macht Wiederholungen nach Verbindungsabbruch unschädlich.
    public func requestAbsence(id: UUID = UUID(), _ draft: AbsenceRequestDraft) async throws(APIError) -> AbsenceRequestItem {
        let body: Data
        do {
            body = try JSONCoding.encoder.encode(draft.normalized)
        } catch {
            throw .invalidResponse
        }
        return try await send(APIRequest(method: "PUT", path: "hr/absence-requests/\(id.key)", body: body, contentType: "application/json"))
    }

    public func decideAbsence(_ id: UUID, approve: Bool, comment: String?) async throws(APIError) -> AbsenceRequestItem {
        try await post("hr/absence-requests/\(id.key)/decision", body: AbsenceDecision(approve: approve, comment: comment))
    }

    public func cancelAbsence(_ id: UUID) async throws(APIError) -> AbsenceRequestItem {
        try await post("hr/absence-requests/\(id.key)/cancel")
    }
}
