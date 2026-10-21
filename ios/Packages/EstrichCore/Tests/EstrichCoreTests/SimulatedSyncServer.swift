import Foundation
@testable import EstrichCore

/// Vereinfachter Server für Zustandsautomaten-Tests der Synchronisation (15, Abschnitt 5) mit Fehlerinjektion.
/// Das echte Protokoll prüft `LiveServerTests` gegen den richtigen Server.
final class SimulatedSyncServer: SyncTransport, @unchecked Sendable {
    enum Failure {
        /// Anfrage kommt nicht an.
        case beforeReceipt
        /// Server verarbeitet, die Antwort geht verloren.
        case afterAcceptance
    }

    private let lock = NSLock()
    private var entities: [String: (type: String, data: JSONValue, deleted: Bool)] = [:]
    private var changeLog: [String] = []
    private var processed: [UUID: PushResult] = [:]
    private var numberCounter = 0
    private var epoch = 1
    private var uploads: [UUID: Data] = [:]
    private(set) var receivedOpIds: [UUID] = []
    var nextPushFailure: Failure?
    var rejectNext: (code: String, message: String)?
    var pullPageSize = 2
    var visibleBauakten: Set<UUID>?
    var failUploadAfterBytes: Int?
    /// Server in Wartung: jede Anfrage endet mit 503 `MAINTENANCE`.
    var inMaintenance = false

    // MARK: Steuerung durch den Test

    func serverSideChange(_ type: String, id: UUID, data: JSONValue) {
        lock.withLock { record(type, id: id, data: data) }
    }

    /// Datensatz, der dem Gerät über den Änderungsfeed nicht (mehr) geliefert wird – z. B. eine Bauakte, die erst
    /// durch eine spätere Einplanung sichtbar wird.
    func serverSideExisting(_ type: String, id: UUID, data: JSONValue) {
        lock.withLock { entities[id.key] = (type, data, false) }
    }

    func serverSideDelete(_ type: String, id: UUID) {
        lock.withLock {
            entities[id.key]?.deleted = true
            changeLog.append(id.key)
        }
    }

    /// Wiederherstellung aus einem Backup: neuer Epoch, verlorene Datensätze.
    func restoreFromBackup(losing ids: [UUID]) {
        lock.withLock {
            epoch += 1
            for id in ids {
                entities.removeValue(forKey: id.key)
                uploads.removeValue(forKey: id)
            }
            changeLog = Array(Set(changeLog.filter { entities[$0] != nil }))
        }
    }

    func entity(_ id: UUID) -> JSONValue? { lock.withLock { entities[id.key].flatMap { $0.deleted ? nil : $0.data } } }
    func uploaded(_ id: UUID) -> Data? { lock.withLock { uploads[id] } }

    // MARK: SyncTransport

    func push(_ operations: [PushOperation]) async throws(APIError) -> [PushResult] {
        if lock.withLock({ inMaintenance }) { throw .server(status: 503, code: "MAINTENANCE", message: "Wartung bis 22 Uhr") }
        let failure = lock.withLock { () -> Failure? in
            defer { nextPushFailure = nil }
            return nextPushFailure
        }
        if failure == .beforeReceipt { throw .offline }
        let results = lock.withLock { operations.map(apply) }
        if failure == .afterAcceptance { throw .offline }
        return results
    }

    func pull(cursor: String?, includeScope: Bool) async throws(APIError) -> PullResponse {
        try locked { () throws(APIError) -> PullResponse in
            var position = 0
            if let cursor {
                let parts = cursor.split(separator: ":")
                guard parts.count == 2, Int(parts[0]) == epoch, let value = Int(parts[1]) else {
                    throw .server(status: 409, code: "RESYNC_REQUIRED", message: nil)
                }
                position = value
            }
            // Cursor = Position im Änderungsprotokoll; innerhalb einer Seite je Datensatz nur der letzte Stand.
            let end = min(position + pullPageSize, changeLog.count)
            let slice = deduplicated(Array(changeLog[position..<end]))
            let changes = slice.compactMap { key -> SyncChange? in
                guard let entry = entities[key], let id = UUID(uuidString: key) else { return nil }
                return SyncChange(entityType: entry.type, id: id, deleted: entry.deleted, data: entry.deleted ? nil : entry.data)
            }
            let next = end
            let visible = includeScope
                ? (visibleBauakten.map(Array.init) ?? entities.filter { $0.value.type == "bauakte" && !$0.value.deleted }.compactMap { UUID(uuidString: $0.key) })
                : nil
            return PullResponse(changes: changes, cursor: "\(epoch):\(next)", hasMore: next < changeLog.count, visibleBauakteIds: visible)
        }
    }

    func snapshot(_ bauakteIds: [UUID]) async throws(APIError) -> SnapshotResponse {
        lock.withLock {
            let wanted = Set(bauakteIds.filter { visibleBauakten?.contains($0) ?? true }.map(\.key))
            var changes: [SyncChange] = []
            var found = Set<String>()
            for (key, entry) in entities where !entry.deleted {
                guard let id = UUID(uuidString: key) else { continue }
                let parent = entry.data["bauakteId"]?.string?.lowercased()
                if entry.type == "bauakte", wanted.contains(key) {
                    found.insert(key)
                    changes.insert(SyncChange(entityType: "bauakte", id: id, deleted: false, data: entry.data), at: 0)
                } else if let parent, wanted.contains(parent) {
                    changes.append(SyncChange(entityType: entry.type, id: id, deleted: false, data: entry.data))
                }
            }
            return SnapshotResponse(changes: changes, missing: bauakteIds.filter { !found.contains($0.key) })
        }
    }

    func uploadStatus(_ fileId: UUID) async throws(APIError) -> UploadStatus {
        try locked { () throws(APIError) -> UploadStatus in
            guard let file = entities[fileId.key]?.data else { throw .server(status: 404, code: "FILE_NOT_FOUND", message: nil) }
            let received = Int64(uploads[fileId]?.count ?? 0)
            let size = Int64(file["sizeBytes"]?.number ?? 0)
            return UploadStatus(id: fileId, receivedBytes: received, sizeBytes: size, state: file["state"]?.string == "stored" ? "stored" : "awaitingUpload")
        }
    }

    func upload(_ fileId: UUID, chunk: Data, offset: Int64, total: Int64) async throws(APIError) -> (received: Int64, stored: Bool) {
        try locked { () throws(APIError) -> (received: Int64, stored: Bool) in
            var data = uploads[fileId] ?? Data()
            guard Int64(data.count) == offset else { throw .server(status: 409, code: "OFFSET_MISMATCH", message: nil) }
            var part = chunk
            if let limit = failUploadAfterBytes {
                failUploadAfterBytes = nil
                part = chunk.prefix(limit)
                data.append(part)
                uploads[fileId] = data
                throw .offline
            }
            data.append(part)
            uploads[fileId] = data
            guard Int64(data.count) == total else { return (Int64(data.count), false) }
            guard FileVault.sha256(data) == entities[fileId.key]?.data["sha256"]?.string else {
                uploads[fileId] = nil
                throw .server(status: 422, code: "CHECKSUM_MISMATCH", message: nil)
            }
            if var file = entities[fileId.key] {
                file.data = file.data.merging(["state": "stored"])
                entities[fileId.key] = file
                changeLog.append(fileId.key)
            }
            return (total, true)
        }
    }

    private func locked<T>(_ body: () throws(APIError) -> T) throws(APIError) -> T {
        lock.lock()
        defer { lock.unlock() }
        return try body()
    }

    // MARK: Verarbeitung

    private func apply(_ operation: PushOperation) -> PushResult {
        receivedOpIds.append(operation.opId)
        if let earlier = processed[operation.opId] {
            return PushResult(opId: earlier.opId, status: .duplicate, code: earlier.code, message: earlier.message,
                              version: earlier.version, conflicts: earlier.conflicts, entity: earlier.entity)
        }
        if let rejection = rejectNext {
            rejectNext = nil
            let result = PushResult(opId: operation.opId, status: .rejected, code: rejection.code, message: rejection.message,
                                    version: nil, conflicts: nil, entity: nil)
            if rejection.code != "RETRY_LATER" { processed[operation.opId] = result }
            return result
        }
        let result = process(operation)
        processed[operation.opId] = result
        return result
    }

    private func process(_ operation: PushOperation) -> PushResult {
        let id = operation.entityId
        let payload = operation.payload.object ?? [:]
        let parts = operation.type.split(separator: ".")
        let type = parts[0] == "workitem" ? "workItem" : String(parts[0])
        switch parts[1] {
        case "create", "announce":
            var data = JSONValue.object(payload).merging(["id": JSONValue(id), "version": 1])
            if type == "bauakte" {
                numberCounter += 1
                data = data.merging(["number": .string(String(format: "2026-%03d", numberCounter)), "status": "planned"])
            }
            if type == "file" { data = data.merging(["state": "awaitingUpload"]) }
            record(type, id: id, data: data)
            return PushResult(opId: operation.opId, status: .applied, code: nil, message: nil, version: 1, conflicts: nil, entity: data)
        case "update":
            guard var current = entities[id.key]?.data else {
                return PushResult(opId: operation.opId, status: .rejected, code: "NOT_FOUND", message: "Nicht gefunden", version: nil, conflicts: nil, entity: nil)
            }
            let changes = payload["changes"]?.object ?? [:]
            let bases = payload["base"]?.object ?? [:]
            var conflicts: [FieldConflict] = []
            for (field, value) in changes {
                let serverValue = current[field] ?? .null
                if serverValue == (bases[field] ?? .null) || serverValue == value {
                    current = current.merging([field: value])
                } else {
                    conflicts.append(FieldConflict(field: field, serverValue: serverValue, clientValue: value))
                }
            }
            let version = (current["version"]?.number ?? 0) + 1
            current = current.merging(["version": .number(version)])
            record(type, id: id, data: current)
            return PushResult(opId: operation.opId, status: conflicts.isEmpty ? .applied : .conflict, code: nil, message: nil,
                              version: Int64(version), conflicts: conflicts.isEmpty ? nil : conflicts, entity: current)
        default:
            entities[id.key]?.deleted = true
            changeLog.append(id.key)
            return PushResult(opId: operation.opId, status: .applied, code: nil, message: nil, version: nil, conflicts: nil, entity: nil)
        }
    }

    private func record(_ type: String, id: UUID, data: JSONValue) {
        entities[id.key] = (type, data, false)
        changeLog.append(id.key)
    }

    private func deduplicated(_ log: [String]) -> [String] {
        var seen = Set<String>()
        return log.reversed().filter { seen.insert($0).inserted }.reversed()
    }
}
