import Foundation

/// Netzwerkzugriff der Synchronisation (für Tests austauschbar).
public protocol SyncTransport: Sendable {
    func push(_ operations: [PushOperation]) async throws(APIError) -> [PushResult]
    func pull(cursor: String?, includeScope: Bool) async throws(APIError) -> PullResponse
    func uploadStatus(_ fileId: UUID) async throws(APIError) -> UploadStatus
    /// Lädt `chunk` ab Position `offset` hoch. Liefert die beim Server angekommenen Bytes und ob die Datei vollständig ist.
    func upload(_ fileId: UUID, chunk: Data, offset: Int64, total: Int64) async throws(APIError) -> (received: Int64, stored: Bool)
}

/// Synchronisation über die API mit der Sitzung des angemeldeten Benutzers.
public struct APISyncTransport: SyncTransport {
    private let client: AuthorizedClient

    public init(client: AuthorizedClient) {
        self.client = client
    }

    public func push(_ operations: [PushOperation]) async throws(APIError) -> [PushResult] {
        let response: PushResponse = try await client.post("sync/push", body: PushRequest(operations: operations))
        return response.results
    }

    public func pull(cursor: String?, includeScope: Bool) async throws(APIError) -> PullResponse {
        var query = [URLQueryItem(name: "limit", value: "200")]
        if let cursor { query.append(URLQueryItem(name: "cursor", value: cursor)) }
        if includeScope { query.append(URLQueryItem(name: "includeScope", value: "true")) }
        return try await client.get("sync/pull", query: query)
    }

    public func uploadStatus(_ fileId: UUID) async throws(APIError) -> UploadStatus {
        try await client.get("files/\(fileId.key)/upload-status")
    }

    public func upload(_ fileId: UUID, chunk: Data, offset: Int64, total: Int64) async throws(APIError) -> (received: Int64, stored: Bool) {
        let end = offset + Int64(chunk.count) - 1
        let response: JSONValue = try await client.send(APIRequest(
            method: "PUT", path: "files/\(fileId.key)/content", body: chunk, contentType: "application/octet-stream",
            headers: ["Content-Range": "bytes \(offset)-\(end)/\(total)"]))
        // 200: FileResponse (vollständig gespeichert), 202: UploadStatus (Teil angekommen).
        if let received = response["receivedBytes"]?.number {
            return (Int64(received), response["state"]?.string == "stored")
        }
        return (total, true)
    }
}

/// Ergebnis eines Synchronisationslaufs.
public enum SyncOutcome: Equatable, Sendable {
    case completed
    /// Kein Netz oder Server nicht erreichbar: Alles bleibt lokal gespeichert und wird später gesendet.
    case offline
    /// Der Server hat um spätere Wiederholung gebeten oder einen vorübergehenden Fehler gemeldet.
    case retryLater
    case failed(APIError)
}

/// Ablauf eines Sync-Laufs (docs/sync-protokoll.md, Grundregel 6): Uploads fortsetzen → Push bis die Outbox leer ist →
/// neu angekündigte Dateien hochladen → Pull bis `hasMore = false`. Es läuft höchstens ein Lauf gleichzeitig.
public actor SyncEngine {
    public static let chunkSize = 4 * 1024 * 1024
    /// Hochgeladene Dateien bleiben 7 Tage lokal (Absicherung für eine Wiederherstellung des Servers).
    public static let localRetention: TimeInterval = 7 * 24 * 3600

    private let store: LocalStore
    private let transport: SyncTransport
    private let files: FileVault
    private let now: @Sendable () -> Date
    private var running: Task<SyncOutcome, Never>?
    private var preparedAfterLaunch = false

    public init(store: LocalStore, transport: SyncTransport, files: FileVault, now: @escaping @Sendable () -> Date = Date.init) {
        self.store = store
        self.transport = transport
        self.files = files
        self.now = now
    }

    /// Startet einen Lauf oder schließt sich dem laufenden an.
    public func synchronize(includeScope: Bool = false) async -> SyncOutcome {
        if let running {
            return await running.value
        }
        let task = Task { await run(includeScope: includeScope) }
        running = task
        let outcome = await task.value
        running = nil
        return outcome
    }

    private func run(includeScope: Bool) async -> SyncOutcome {
        do {
            if !preparedAfterLaunch {
                try await store.resetInterruptedOperations()
                preparedAfterLaunch = true
            }
            try await uploadReadyFiles()
            if try await pushAll() == .retryLater {
                return .retryLater
            }
            try await uploadReadyFiles()
            try await pullAll(includeScope: includeScope)
            try await removeExpiredLocalCopies()
            return .completed
        } catch let error as APIError {
            if error == .offline { return .offline }
            if let status = error.status, status >= 500 || status == 429 { return .retryLater }
            return .failed(error)
        } catch {
            return .failed(.invalidResponse)
        }
    }

    private func pushAll() async throws -> SyncOutcome {
        while true {
            let operations = try await store.takeOperations(limit: 100)
            guard !operations.isEmpty else { return .completed }
            let request = operations.map {
                PushOperation(opId: $0.opId, type: $0.type, entityId: $0.entityId, baseVersion: $0.baseVersion, payload: $0.payload)
            }
            let results: [PushResult]
            do {
                results = try await transport.push(request)
            } catch {
                try await store.returnToPending(operations)
                throw error
            }
            if try await store.apply(results, for: operations) {
                return .retryLater
            }
        }
    }

    private func pullAll(includeScope: Bool) async throws {
        var resynced = false
        var first = true
        while true {
            let page: PullResponse
            do {
                page = try await transport.pull(cursor: try await store.cursor, includeScope: includeScope && first)
            } catch let error as APIError where error.code == "RESYNC_REQUIRED" && !resynced {
                // Server aus einem Backup wiederhergestellt: Cursor verwerfen, vollständig neu abgleichen.
                resynced = true
                first = true
                try await store.beginFullResync()
                continue
            }
            first = false
            try await store.apply(page)
            guard page.hasMore else { break }
        }
        // Nach einem Neuabgleich angekündigte Dateien sofort übertragen.
        if resynced, try await pushAll() == .completed {
            try await uploadReadyFiles()
        }
    }

    private func uploadReadyFiles() async throws {
        for upload in try await store.uploadsReady() {
            try await transfer(upload)
        }
    }

    private func transfer(_ upload: PendingUpload) async throws {
        let data: Data
        do {
            data = try files.read(upload.fileId)
        } catch {
            try await store.updateUpload(upload.fileId, state: .failed, uploadedBytes: 0, errorCode: "LOCAL_FILE_MISSING")
            return
        }

        var offset: Int64
        do {
            let status = try await transport.uploadStatus(upload.fileId)
            if status.state == "stored" {
                try await store.updateUpload(upload.fileId, state: .done, uploadedBytes: upload.size)
                return
            }
            offset = status.receivedBytes
        } catch let error as APIError where error.status == 404 {
            // Ankündigung beim Server unbekannt (z. B. abgelehnt): später erneut versuchen.
            return
        }

        var restartedAfterChecksum = false
        try await store.updateUpload(upload.fileId, state: .uploading, uploadedBytes: offset)
        while true {
            let end = min(Int(offset) + Self.chunkSize, data.count)
            do {
                let (received, stored) = try await transport.upload(upload.fileId, chunk: data.subdata(in: Int(offset)..<end),
                                                                     offset: offset, total: Int64(data.count))
                if stored {
                    try await store.updateUpload(upload.fileId, state: .done, uploadedBytes: Int64(data.count))
                    return
                }
                offset = received
                try await store.updateUpload(upload.fileId, state: .uploading, uploadedBytes: offset)
            } catch let error as APIError where error.code == "OFFSET_MISMATCH" {
                offset = try await transport.uploadStatus(upload.fileId).receivedBytes
            } catch let error as APIError where error.code == "CHECKSUM_MISMATCH" && !restartedAfterChecksum {
                restartedAfterChecksum = true
                offset = 0
            } catch let error as APIError where (400..<500).contains(error.status ?? 0) {
                try await store.updateUpload(upload.fileId, state: .failed, uploadedBytes: offset, errorCode: error.code)
                return
            }
        }
    }

    private func removeExpiredLocalCopies() async throws {
        for fileId in try await store.uploadsCompleted(before: now().addingTimeInterval(-Self.localRetention)) {
            files.remove(fileId)
            try await store.forgetUpload(fileId)
        }
    }
}
