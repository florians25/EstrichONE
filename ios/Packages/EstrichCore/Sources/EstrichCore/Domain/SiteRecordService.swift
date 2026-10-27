import Foundation

/// Bautagebuch und Prüfungen auf dem Gerät (lokal zuerst, Synchronisation folgt).
extension BauakteService {
    public func diary(of bauakteId: UUID) async throws -> [DiaryEntry] {
        try await store.entities(.diaryEntry, parent: bauakteId).map(DiaryEntry.init)
            .sorted { ($0.day, $0.createdAt ?? .distantPast) > ($1.day, $1.createdAt ?? .distantPast) }
    }

    @discardableResult
    public func addDiaryEntry(_ input: NewDiaryEntry, to bauakteId: UUID) async throws(SiteRecordError) -> UUID {
        let text = input.text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !text.isEmpty else { throw .textRequired }
        let id = UUID.v7(now: currentDate())
        let payload: [String: JSONValue] = [
            "bauakteId": JSONValue(bauakteId), "day": .string(input.day), "text": .string(text),
            "weather": JSONValue(input.weather?.rawValue), "temperatureCelsius": JSONValue(input.temperatureCelsius.map(Double.init)),
            "workersPresent": JSONValue(input.workersPresent.map(Double.init)),
        ]
        let local = JSONValue.object(payload).merging([
            "id": JSONValue(id), "authorUserId": JSONValue(userId), "createdAt": .string(APIDate.format(currentDate())),
            "editedAt": .null, "version": 0,
        ])
        do {
            try await store.create(.diaryEntry, id: id, data: local, payload: payload)
        } catch {
            throw .from(error)
        }
        return id
    }

    public func inspections(of bauakteId: UUID) async throws -> [Inspection] {
        try await store.entities(.inspection, parent: bauakteId).map(Inspection.init)
            .sorted { ($0.performedAt ?? .distantPast) > ($1.performedAt ?? .distantPast) }
    }

    @discardableResult
    public func addCmMeasurement(to bauakteId: UUID, location: String, binder: ScreedBinderKind, heated: Bool, thicknessMm: Int?,
                                 measuredCmPercent: Double, remarks: String? = nil, notes: String? = nil,
                                 corrects: UUID? = nil) async throws(SiteRecordError) -> UUID {
        let location = location.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !location.isEmpty else { throw .locationRequired }
        guard (0...20).contains(measuredCmPercent) else { throw .invalidMeasurement }
        return try await createInspection(bauakteId, kind: .cmMeasurement, fields: [
            "location": .string(location), "binder": .string(binder.rawValue), "heated": .bool(heated),
            "thicknessMm": JSONValue(thicknessMm.map(Double.init)), "measuredCmPercent": .number((measuredCmPercent * 100).rounded() / 100),
        ], remarks: remarks, notes: notes, corrects: corrects)
    }

    @discardableResult
    public func addInitialInspection(to bauakteId: UUID, notRequired: Bool, checks: [InitialCheck: CheckResult], photoFileIds: [UUID],
                                     remarks: String?, notes: String? = nil, corrects: UUID? = nil) async throws(SiteRecordError) -> UUID {
        let reason = remarks?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        if notRequired, reason.isEmpty { throw .reasonRequired }
        if !notRequired, checks.isEmpty { throw .checksRequired }
        let checkValues = notRequired ? [:] : Dictionary(uniqueKeysWithValues: checks.map { ($0.key.rawValue, JSONValue.string($0.value.rawValue)) })
        return try await createInspection(bauakteId, kind: .initialInspection, fields: [
            "notRequired": .bool(notRequired), "checks": .object(checkValues),
            "photoFileIds": .array(photoFileIds.map { JSONValue($0) }),
        ], remarks: remarks, notes: notes, corrects: corrects)
    }

    /// Erstbegehung nach dem Schema des Büros (`inspection.create` mit `values`, `checkRemarks`, `checkPhotos`, `schemaVersion`).
    @discardableResult
    public func addSchemaInspection(to bauakteId: UUID, schema: FormSchema, notRequired: Bool, checks: [String: CheckResult],
                                    values: [String: JSONValue], checkRemarks: [String: String], checkPhotos: [String: [UUID]],
                                    photoFileIds: [UUID], remarks: String?, notes: String? = nil,
                                    corrects: UUID? = nil) async throws(SiteRecordError) -> UUID {
        let reason = remarks?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        if notRequired, reason.isEmpty { throw .reasonRequired }
        if !notRequired {
            if checks.isEmpty, values.isEmpty { throw .checksRequired }
            let missing = schema.fields.filter { field in
                field.required && (field.type == .check ? checks[field.key] == nil : FormSchema.isEmpty(values[field.key] ?? .null))
            }
            if let first = missing.first { throw .fieldRequired(first.label) }
        }
        let remarksJSON = checkRemarks.compactMapValues { text -> JSONValue? in
            let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
            return trimmed.isEmpty ? nil : .string(trimmed)
        }
        let allPhotos = Array(Set(photoFileIds + checkPhotos.values.flatMap { $0 }))
        return try await createInspection(bauakteId, kind: .initialInspection, fields: [
            "notRequired": .bool(notRequired),
            "checks": .object(notRequired ? [:] : checks.mapValues { .string($0.rawValue) }),
            "values": .object(notRequired ? [:] : values.filter { $0.value != .null }),
            "checkRemarks": .object(remarksJSON),
            "checkPhotos": .object(checkPhotos.filter { !$0.value.isEmpty }.mapValues { .array($0.map { JSONValue($0) }) }),
            "photoFileIds": .array(allPhotos.map { JSONValue($0) }),
            "schemaVersion": .number(Double(schema.version)),
        ], remarks: remarks, notes: notes, corrects: corrects)
    }

    private func createInspection(_ bauakteId: UUID, kind: InspectionKind, fields: [String: JSONValue], remarks: String?,
                                  notes: String?, corrects: UUID?) async throws(SiteRecordError) -> UUID {
        let id = UUID.v7(now: currentDate())
        let cleaned = remarks?.trimmingCharacters(in: .whitespacesAndNewlines)
        let payload = JSONValue.object(fields).merging([
            "bauakteId": JSONValue(bauakteId), "kind": .string(kind.rawValue), "performedAt": .string(APIDate.format(currentDate())),
            "remarks": JSONValue(cleaned?.isEmpty == false ? cleaned : nil), "correctsInspectionId": JSONValue(corrects),
            "notes": JSONValue(Self.cleanNotes(notes)),
        ])
        let local = payload.merging(["id": JSONValue(id), "performedByUserId": JSONValue(userId), "version": 0])
        do {
            try await store.create(.inspection, id: id, data: local, payload: payload.object ?? [:])
        } catch {
            throw .from(error)
        }
        return id
    }

    /// Leere Notizen werden nicht gespeichert; Zeilenumbrüche bleiben erhalten.
    static func cleanNotes(_ notes: String?) -> String? {
        guard let notes, !notes.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return nil }
        return String(notes.trimmingCharacters(in: .whitespacesAndNewlines).prefix(100_000))
    }
}
