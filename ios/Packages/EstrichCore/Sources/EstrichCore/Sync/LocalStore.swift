import Foundation

/// Lokale Datenhaltung eines Benutzers: Datensätze, Outbox, Upload-Warteschlange und Konflikte
/// (docs/sync-protokoll.md; docs/reengineering/15). Jede Änderung wird in **einer** Transaktion gespeichert:
/// Datensatz + Outbox-Eintrag (Grundregel 1).
///
/// Ein Datensatz hat zwei Stände: `server` (zuletzt vom Server bestätigt) und `local` (Server plus noch nicht
/// übertragene Änderungen). Angezeigt wird `local`.
public actor LocalStore {
    private let db: EncryptedDatabase
    private let now: @Sendable () -> Date

    public init(database: EncryptedDatabase, now: @escaping @Sendable () -> Date = Date.init) {
        db = database
        self.now = now
    }

    /// Öffnet (bzw. legt an) die verschlüsselte Datenbank eines Benutzers.
    public static func open(url: URL, account: UnlockedAccount, now: @escaping @Sendable () -> Date = Date.init) throws -> LocalStore {
        LocalStore(database: try EncryptedDatabase(url: url, key: account.dataKey, migrations: schema), now: now)
    }

    static let schema: [@Sendable (SQLiteDatabase) throws -> Void] = [
        { db in
            try db.execute("""
                CREATE TABLE entity (
                    type TEXT NOT NULL, id TEXT NOT NULL, server TEXT, local TEXT NOT NULL,
                    deleted INTEGER NOT NULL DEFAULT 0, seen INTEGER NOT NULL DEFAULT 1,
                    parent_id TEXT, search TEXT NOT NULL DEFAULT '', sort_key TEXT NOT NULL DEFAULT '',
                    PRIMARY KEY (type, id))
                """)
            try db.execute("CREATE INDEX entity_parent ON entity (type, parent_id)")
            try db.execute("""
                CREATE TABLE outbox (
                    seq INTEGER PRIMARY KEY AUTOINCREMENT, op_id TEXT NOT NULL UNIQUE, type TEXT NOT NULL,
                    entity_type TEXT NOT NULL, entity_id TEXT NOT NULL, base_version INTEGER, payload TEXT NOT NULL,
                    state TEXT NOT NULL DEFAULT 'pending', attempts INTEGER NOT NULL DEFAULT 0,
                    error_code TEXT, error_message TEXT, created_at TEXT NOT NULL)
                """)
            try db.execute("CREATE INDEX outbox_entity ON outbox (entity_type, entity_id)")
            try db.execute("""
                CREATE TABLE upload (
                    file_id TEXT PRIMARY KEY, size INTEGER NOT NULL, sha256 TEXT NOT NULL, content_type TEXT NOT NULL,
                    uploaded_bytes INTEGER NOT NULL DEFAULT 0, state TEXT NOT NULL DEFAULT 'waiting', error_code TEXT,
                    created_at TEXT NOT NULL, completed_at TEXT)
                """)
            try db.execute("""
                CREATE TABLE conflict (
                    op_id TEXT NOT NULL, entity_type TEXT NOT NULL, entity_id TEXT NOT NULL, field TEXT NOT NULL,
                    server_value TEXT, client_value TEXT, created_at TEXT NOT NULL, PRIMARY KEY (op_id, field))
                """)
            try db.execute("CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT)")
        },
    ]

    // MARK: Lesen

    public func entity(_ type: EntityType, _ id: UUID) throws -> LocalEntity? {
        try db.database.query("SELECT * FROM entity WHERE type = ? AND id = ?", [.text(type.rawValue), .text(id.key)])
            .first.map(makeEntity)
    }

    /// Sichtbare Datensätze eines Typs, optional zu einer Bauakte und gefiltert nach Suchbegriff (lokale Suche).
    public func entities(_ type: EntityType, parent: UUID? = nil, search: String? = nil) throws -> [LocalEntity] {
        var sql = "SELECT * FROM entity WHERE type = ? AND deleted = 0"
        var arguments: [SQLValue] = [.text(type.rawValue)]
        if let parent {
            sql += " AND parent_id = ?"
            arguments.append(.text(parent.key))
        }
        for term in (search ?? "").lowercased().split(separator: " ") {
            sql += " AND search LIKE ? ESCAPE '\\'"
            let escaped = term.replacingOccurrences(of: "\\", with: "\\\\").replacingOccurrences(of: "%", with: "\\%").replacingOccurrences(of: "_", with: "\\_")
            arguments.append(.text("%\(escaped)%"))
        }
        sql += " ORDER BY sort_key DESC"
        return try db.database.query(sql, arguments).map(makeEntity)
    }

    public func operations() throws -> [OutboxOperation] {
        try db.database.query("SELECT * FROM outbox ORDER BY seq").map(makeOperation)
    }

    public func conflicts() throws -> [(entityType: EntityType, entityId: UUID, conflict: FieldConflict)] {
        try db.database.query("SELECT * FROM conflict ORDER BY created_at").compactMap { row in
            guard let type = row["entity_type"]?.string.flatMap(EntityType.init(rawValue:)),
                  let id = row["entity_id"]?.string.flatMap(UUID.init(uuidString:)), let field = row["field"]?.string else { return nil }
            return (type, id, FieldConflict(field: field, serverValue: try row["server_value"]?.string.map(JSONValue.decode),
                                            clientValue: try row["client_value"]?.string.map(JSONValue.decode)))
        }
    }

    public func dismissConflicts(entityId: UUID) throws {
        try db.write { db in _ = try db.execute("DELETE FROM conflict WHERE entity_id = ?", [.text(entityId.key)]) }
    }

    public func summary() throws -> SyncSummary {
        func count(_ sql: String) throws -> Int { Int(try db.database.scalar(sql).int ?? 0) }
        return SyncSummary(
            pendingOperations: try count("SELECT COUNT(*) FROM outbox WHERE state IN ('pending', 'sending')"),
            rejectedOperations: try count("SELECT COUNT(*) FROM outbox WHERE state = 'rejected'"),
            pendingUploads: try count("SELECT COUNT(*) FROM upload WHERE state IN ('waiting', 'uploading')"),
            failedUploads: try count("SELECT COUNT(*) FROM upload WHERE state = 'failed'"),
            conflicts: try count("SELECT COUNT(DISTINCT entity_id) FROM conflict"),
            lastSuccessfulSync: try meta("last_sync").flatMap(APIDate.parse))
    }

    // MARK: Lokal ändern

    /// „Nur Ansicht“ (`ReadOnlyPolicy`): neue Änderungen werden abgewiesen, der Abgleich läuft weiter.
    public private(set) var isReadOnly = false

    public func setReadOnly(_ readOnly: Bool) {
        isReadOnly = readOnly
    }

    private func ensureWritable() throws {
        if isReadOnly { throw ReadOnlyModeError() }
    }

    /// Legt einen Datensatz an. `data` ist die lokale Darstellung (wie vom Server), `payload` die Nutzdaten der
    /// Operation `<typ>.create` bzw. `file.announce`.
    public func create(_ type: EntityType, id: UUID, data: JSONValue, payload: [String: JSONValue],
                       operation: String? = nil) throws {
        try ensureWritable()
        try db.write { db in
            try db.execute("""
                INSERT INTO entity (type, id, server, local, parent_id, search, sort_key) VALUES (?, ?, NULL, ?, ?, ?, ?)
                """, [.text(type.rawValue), .text(id.key), .text(try data.encoded())] + derived(type, data))
            try insertOperation(db, type: operation ?? "\(type.operationPrefix).create", entityType: type, entityId: id,
                                baseVersion: nil, payload: .object(payload))
        }
    }

    /// Ändert Felder eines Datensatzes (feldweise, docs/sync-protokoll.md, Abschnitt 3). Mehrere ungesendete
    /// Änderungen werden zusammengefasst; je Feld bleibt der älteste `base`-Wert erhalten.
    public func update(_ type: EntityType, id: UUID, changes: [String: JSONValue]) throws {
        guard !changes.isEmpty else { return }
        try ensureWritable()
        try db.write { db in
            guard let row = try db.query("SELECT * FROM entity WHERE type = ? AND id = ?", [.text(type.rawValue), .text(id.key)]).first,
                  let localText = row["local"]?.string else { return }
            let server = try row["server"]?.string.map(JSONValue.decode)
            let local = try JSONValue.decode(localText)
            let pending = try db.query("""
                SELECT * FROM outbox WHERE entity_type = ? AND entity_id = ? AND state = 'pending' ORDER BY seq DESC LIMIT 1
                """, [.text(type.rawValue), .text(id.key)]).first.map(makeOperation)

            if let pending, pending.type.hasSuffix(".create") || pending.type == "file.announce" {
                // Noch nicht gesendete Anlage: Änderungen direkt in die Anlage übernehmen.
                try replacePayload(db, pending, with: pending.payload.merging(changes))
            } else if let pending, pending.type.hasSuffix(".update") {
                var merged = pending.payload.object ?? [:]
                var bases = merged["base"]?.object ?? [:]
                for field in changes.keys where bases[field] == nil {
                    bases[field] = (server ?? local)[field] ?? .null
                }
                merged["changes"] = (merged["changes"] ?? [:]).merging(changes)
                merged["base"] = .object(bases)
                try replacePayload(db, pending, with: .object(merged))
            } else {
                let base = Dictionary(uniqueKeysWithValues: changes.keys.map { ($0, (server ?? local)[$0] ?? JSONValue.null) })
                try insertOperation(db, type: "\(type.operationPrefix).update", entityType: type, entityId: id,
                                    baseVersion: (server ?? local)["version"]?.number.map(Int64.init),
                                    payload: ["changes": .object(changes), "base": .object(base)])
            }
            let updated = local.merging(changes)
            try db.execute("UPDATE entity SET local = ?, parent_id = ?, search = ?, sort_key = ? WHERE type = ? AND id = ?",
                           [.text(try updated.encoded())] + derived(type, updated) + [.text(type.rawValue), .text(id.key)])
        }
    }

    /// Status einer Bauakte ändern bzw. stornieren (`bauakte.setStatus`). `baseStatus` ist der Stand des Geräts –
    /// der Server lehnt ab, wenn sich der Status inzwischen anders geändert hat.
    public func setStatus(_ status: String, reason: String?, of bauakteId: UUID) throws {
        try ensureWritable()
        try db.write { db in
            guard let row = try db.query("SELECT * FROM entity WHERE type = 'bauakte' AND id = ?", [.text(bauakteId.key)]).first,
                  let localText = row["local"]?.string else { return }
            let local = try JSONValue.decode(localText)
            var payload: [String: JSONValue] = ["status": .string(status), "baseStatus": local["status"] ?? .null]
            if let reason { payload["reason"] = .string(reason) }
            try insertOperation(db, type: "bauakte.setStatus", entityType: .bauakte, entityId: bauakteId, baseVersion: nil, payload: .object(payload))
            try recompute(db, .bauakte, bauakteId)
        }
    }

    /// Legt an oder ersetzt vollständig (`<typ>.save`, z. B. `bauakteParty.save`): `payload` enthält alle Felder.
    /// Eine noch nicht gesendete Speicherung desselben Datensatzes wird ersetzt; `baseVersion` ist der zuletzt bekannte
    /// Serverstand (der Server lehnt ab, wenn inzwischen jemand anderes geändert hat).
    public func save(_ type: EntityType, id: UUID, payload: [String: JSONValue]) throws {
        try ensureWritable()
        let operation = "\(type.operationPrefix).save"
        try db.write { db in
            let row = try db.query("SELECT server, local FROM entity WHERE type = ? AND id = ?", [.text(type.rawValue), .text(id.key)]).first
            let server = try row?["server"]?.string.map(JSONValue.decode)
            let pending = try db.query("""
                SELECT * FROM outbox WHERE entity_type = ? AND entity_id = ? AND state = 'pending' AND type = ? ORDER BY seq DESC LIMIT 1
                """, [.text(type.rawValue), .text(id.key), .text(operation)]).first.map(makeOperation)
            if let pending {
                try replacePayload(db, pending, with: .object(payload))
            } else {
                try insertOperation(db, type: operation, entityType: type, entityId: id,
                                    baseVersion: server?["version"]?.number.map(Int64.init), payload: .object(payload))
            }
            if row == nil {
                let local = JSONValue.object(payload).merging(["id": JSONValue(id), "version": 0])
                try db.execute("""
                    INSERT INTO entity (type, id, server, local, parent_id, search, sort_key) VALUES (?, ?, NULL, ?, ?, ?, ?)
                    """, [.text(type.rawValue), .text(id.key), .text(try local.encoded())] + derived(type, local))
            } else if server == nil {
                let local = try JSONValue.decode(row?["local"]?.string ?? "{}").merging(payload)
                try db.execute("UPDATE entity SET local = ?, parent_id = ?, search = ?, sort_key = ? WHERE type = ? AND id = ?",
                               [.text(try local.encoded())] + derived(type, local) + [.text(type.rawValue), .text(id.key)])
            } else {
                try recompute(db, type, id)
            }
        }
    }

    /// Löscht einen Datensatz. Nie gesendete Anlagen verschwinden ohne Serverkontakt.
    public func delete(_ type: EntityType, id: UUID) throws {
        try ensureWritable()
        try db.write { db in
            let unsentCreate = try db.query("""
                SELECT seq FROM outbox WHERE entity_type = ? AND entity_id = ? AND state = 'pending'
                  AND (type LIKE '%.create' OR type = 'file.announce'
                       OR (type LIKE '%.save' AND EXISTS (SELECT 1 FROM entity WHERE type = ? AND id = ? AND server IS NULL)))
                """, [.text(type.rawValue), .text(id.key), .text(type.rawValue), .text(id.key)]).first
            if unsentCreate != nil {
                try db.execute("DELETE FROM outbox WHERE entity_type = ? AND entity_id = ?", [.text(type.rawValue), .text(id.key)])
                try db.execute("DELETE FROM entity WHERE type = ? AND id = ?", [.text(type.rawValue), .text(id.key)])
                if type == .file {
                    try db.execute("DELETE FROM upload WHERE file_id = ?", [.text(id.key)])
                }
                return
            }
            try insertOperation(db, type: "\(type.operationPrefix).delete", entityType: type, entityId: id, baseVersion: nil, payload: [:])
            try db.execute("UPDATE entity SET deleted = 1 WHERE type = ? AND id = ?", [.text(type.rawValue), .text(id.key)])
        }
    }

    // MARK: Push

    /// Nach einem App-Neustart: Einträge im Zustand `sending` wieder senden (sicher dank `opId`).
    public func resetInterruptedOperations() throws {
        try db.write { db in
            _ = try db.execute("UPDATE outbox SET state = 'pending' WHERE state = 'sending'")
            _ = try db.execute("UPDATE upload SET state = 'waiting' WHERE state = 'uploading'")
        }
    }

    /// Nächste Operationen in Outbox-Reihenfolge; werden als `sending` markiert.
    public func takeOperations(limit: Int = 100) throws -> [OutboxOperation] {
        try db.write { db in
            let rows = try db.query("SELECT * FROM outbox WHERE state = 'pending' ORDER BY seq LIMIT ?", [.integer(Int64(limit))])
            let operations = try rows.map(makeOperation)
            for operation in operations {
                try db.execute("UPDATE outbox SET state = 'sending', attempts = attempts + 1 WHERE seq = ?", [.integer(operation.seq)])
            }
            return operations
        }
    }

    /// Netz- oder Serverfehler für die ganze Anfrage: alles später unverändert erneut senden.
    public func returnToPending(_ operations: [OutboxOperation]) throws {
        try db.write { db in
            for operation in operations {
                try db.execute("UPDATE outbox SET state = 'pending' WHERE seq = ? AND state = 'sending'", [.integer(operation.seq)])
            }
        }
    }

    /// Übernimmt die Ergebnisse eines Push (Tabelle in docs/sync-protokoll.md, Abschnitt 2).
    /// - Returns: `true`, wenn der Server um spätere Wiederholung gebeten hat (`RETRY_LATER`).
    @discardableResult
    public func apply(_ results: [PushResult], for operations: [OutboxOperation]) throws -> Bool {
        let byId = Dictionary(uniqueKeysWithValues: operations.map { ($0.opId, $0) })
        var retryLater = false
        try db.write { db in
            for result in results {
                guard let operation = byId[result.opId] else { continue }
                let typeValue = SQLValue.text(operation.entityType.rawValue)
                let idValue = SQLValue.text(operation.entityId.key)
                switch result.status {
                case .applied, .duplicate, .conflict:
                    try db.execute("DELETE FROM outbox WHERE seq = ?", [.integer(operation.seq)])
                    for conflict in result.conflicts ?? [] {
                        try db.execute("""
                            INSERT OR REPLACE INTO conflict (op_id, entity_type, entity_id, field, server_value, client_value, created_at)
                            VALUES (?, ?, ?, ?, ?, ?, ?)
                            """, [.text(operation.opId.key), typeValue, idValue, .text(conflict.field),
                                  SQLValue(try conflict.serverValue?.encoded()), SQLValue(try conflict.clientValue?.encoded()),
                                  .text(APIDate.format(now()))])
                    }
                    if operation.type.hasSuffix(".delete") {
                        try removeEntity(db, operation.entityType, operation.entityId)
                    } else if let entity = result.entity {
                        try storeServerState(db, operation.entityType, operation.entityId, entity)
                    }
                case .rejected where result.code == "RETRY_LATER":
                    retryLater = true
                    try db.execute("UPDATE outbox SET state = 'pending' WHERE seq = ?", [.integer(operation.seq)])
                case .rejected where result.code == "OP_ID_CONFLICT":
                    try db.execute("UPDATE outbox SET state = 'pending', op_id = ? WHERE seq = ?",
                                   [.text(UUID.v7(now: now()).key), .integer(operation.seq)])
                case .rejected:
                    // Endgültig abgelehnt: bleibt mit Nutzdaten sichtbar (Synchronisationsansicht), wird nie erneut gesendet.
                    try db.execute("UPDATE outbox SET state = 'rejected', error_code = ?, error_message = ? WHERE seq = ?",
                                   [SQLValue(result.code), SQLValue(result.message), .integer(operation.seq)])
                    if operation.type == "file.announce" {
                        try db.execute("UPDATE upload SET state = 'failed', error_code = ? WHERE file_id = ?", [SQLValue(result.code), idValue])
                    }
                    try recompute(db, operation.entityType, operation.entityId)
                }
            }
        }
        return retryLater
    }

    /// Verwirft eine abgelehnte Operation (der Benutzer hat die Meldung gesehen).
    public func discardRejected(_ operation: OutboxOperation) throws {
        try db.write { db in
            try db.execute("DELETE FROM outbox WHERE seq = ? AND state = 'rejected'", [.integer(operation.seq)])
            if operation.type.hasSuffix(".create") || operation.type == "file.announce" {
                let onServer = try db.query("SELECT server FROM entity WHERE type = ? AND id = ?",
                                            [.text(operation.entityType.rawValue), .text(operation.entityId.key)]).first?["server"]
                if onServer == nil || onServer == .null {
                    try removeEntity(db, operation.entityType, operation.entityId)
                }
            }
            try recompute(db, operation.entityType, operation.entityId)
        }
    }

    // MARK: Pull

    public var cursor: String? {
        get throws { try meta("cursor") }
    }

    /// Server wurde wiederhergestellt oder Cursor ungültig: vollständiger Neuabgleich, Outbox bleibt.
    public func beginFullResync() throws {
        try db.write { db in try Self.beginFullResync(db) }
    }

    private static func beginFullResync(_ db: SQLiteDatabase) throws {
        try db.execute("DELETE FROM meta WHERE key = 'cursor'")
        try db.execute("UPDATE entity SET seen = 0 WHERE server IS NOT NULL")
        try db.execute("INSERT OR REPLACE INTO meta (key, value) VALUES ('full_resync', '1')")
    }

    /// Datensatzarten, die diese App-Version aus dem Änderungsfeed übernimmt (mit jedem Cursor gespeichert).
    static let feedTypes = EntityType.allCases.map(\.rawValue).sorted().joined(separator: ",")

    /// Nach einem App-Update mit neuen Datensatzarten (z. B. Räume): Ältere Versionen haben sie im Feed übersprungen
    /// und der Cursor steht dahinter – darum einmal vollständig neu abgleichen (Outbox bleibt). `true`, wenn begonnen.
    public func beginResyncForNewTypesIfNeeded() throws -> Bool {
        try db.write { db in
            guard try db.scalar("SELECT value FROM meta WHERE key = 'cursor'").string != nil else { return false }
            let known = Set((try db.scalar("SELECT value FROM meta WHERE key = 'feed_types'").string ?? "").split(separator: ",").map(String.init))
            guard !Set(EntityType.allCases.map(\.rawValue)).isSubset(of: known) else { return false }
            try Self.beginFullResync(db)
            return true
        }
    }

    /// Übernimmt eine Seite des Änderungsfeeds.
    public func apply(_ page: PullResponse) throws {
        try db.write { db in
            for change in page.changes {
                guard let type = EntityType(rawValue: change.entityType) else { continue }
                if change.deleted {
                    try removeEntity(db, type, change.id)
                } else if let data = change.data {
                    try storeServerState(db, type, change.id, data)
                }
            }
            if let visible = page.visibleBauakteIds {
                // Rechte entzogen: lokale Bauakten, die nicht mehr sichtbar sind, samt Inhalt entfernen (15, Abschnitt 3).
                let keep = Set(visible.map(\.key))
                let local = try db.query("SELECT id FROM entity WHERE type = 'bauakte' AND server IS NOT NULL")
                for row in local {
                    if let id = row["id"]?.string, !keep.contains(id), let uuid = UUID(uuidString: id) {
                        try removeEntity(db, .bauakte, uuid)
                    }
                }
            }
            try db.execute("INSERT OR REPLACE INTO meta (key, value) VALUES ('cursor', ?)", [.text(page.cursor)])
            try db.execute("INSERT OR REPLACE INTO meta (key, value) VALUES ('feed_types', ?)", [.text(Self.feedTypes)])
            if !page.hasMore {
                try finishFullResyncIfNeeded(db)
                try db.execute("INSERT OR REPLACE INTO meta (key, value) VALUES ('last_sync', ?)", [.text(APIDate.format(now()))])
            }
        }
    }

    /// Bauakten mit Serverstand auf dem Gerät.
    public func knownBauakteIds() throws -> [UUID] {
        try db.database.query("SELECT id FROM entity WHERE type = 'bauakte' AND server IS NOT NULL")
            .compactMap { $0["id"]?.string.flatMap(UUID.init(uuidString:)) }
    }

    /// Übernimmt einen Snapshot (ohne Cursor zu verändern). Nicht mehr sichtbare Bauakten verschwinden samt Inhalt.
    public func apply(snapshot: SnapshotResponse) throws {
        try db.write { db in
            for change in snapshot.changes {
                guard let type = EntityType(rawValue: change.entityType) else { continue }
                if change.deleted {
                    try removeEntity(db, type, change.id)
                } else if let data = change.data {
                    try storeServerState(db, type, change.id, data)
                }
            }
            for id in snapshot.missing {
                let local = try db.query("SELECT server FROM entity WHERE type = 'bauakte' AND id = ?", [.text(id.key)]).first
                if let server = local?["server"], server != .null {
                    try removeEntity(db, .bauakte, id)
                }
            }
        }
    }

    /// Abgelehnte Operation erneut senden (z. B. nachdem die Bauakte zugewiesen wurde). Neue `opId`, weil der Server
    /// die Ablehnung der alten gespeichert hat.
    public func retryRejected(_ operation: OutboxOperation) throws {
        try db.write { db in
            try db.execute("""
                UPDATE outbox SET state = 'pending', op_id = ?, error_code = NULL, error_message = NULL WHERE seq = ? AND state = 'rejected'
                """, [.text(UUID.v7(now: now()).key), .integer(operation.seq)])
            if operation.type == "file.announce" {
                try db.execute("UPDATE upload SET state = 'waiting', error_code = NULL WHERE file_id = ?", [.text(operation.entityId.key)])
            }
            try recompute(db, operation.entityType, operation.entityId)
        }
    }

    /// Fehlgeschlagene Uploads erneut versuchen (dauerhafte Ablehnungen wie zu große Dateien ausgenommen).
    @discardableResult
    public func retryFailedUploads() throws -> Int {
        try db.write { db in
            try db.execute("""
                UPDATE upload SET state = 'waiting', error_code = NULL
                WHERE state = 'failed' AND COALESCE(error_code, '') NOT IN ('FILE_TOO_LARGE', 'TYPE_NOT_ALLOWED', 'LOCAL_FILE_MISSING')
                  AND NOT EXISTS (SELECT 1 FROM outbox o WHERE o.entity_type = 'file' AND o.entity_id = upload.file_id AND o.state = 'rejected')
                """)
        }
    }

    // MARK: Dateien

    /// Datei für den Upload vormerken (nach `create(.file, …, operation: "file.announce")`).
    public func enqueueUpload(fileId: UUID, size: Int64, sha256: String, contentType: String) throws {
        try ensureWritable()
        try db.write { db in
            try db.execute("""
                INSERT OR REPLACE INTO upload (file_id, size, sha256, content_type, state, created_at) VALUES (?, ?, ?, ?, 'waiting', ?)
                """, [.text(fileId.key), .integer(size), .text(sha256), .text(contentType), .text(APIDate.format(now()))])
        }
    }

    /// Uploads, deren Ankündigung der Server bereits angenommen hat.
    public func uploadsReady() throws -> [PendingUpload] {
        try db.database.query("""
            SELECT * FROM upload u WHERE u.state IN ('waiting', 'uploading')
              AND NOT EXISTS (SELECT 1 FROM outbox o WHERE o.entity_type = 'file' AND o.entity_id = u.file_id AND o.type = 'file.announce')
            ORDER BY created_at
            """).map(makeUpload)
    }

    public func uploads() throws -> [PendingUpload] {
        try db.database.query("SELECT * FROM upload ORDER BY created_at").map(makeUpload)
    }

    public func updateUpload(_ fileId: UUID, state: PendingUpload.State, uploadedBytes: Int64, errorCode: String? = nil) throws {
        try db.write { db in
            try db.execute("""
                UPDATE upload SET state = ?, uploaded_bytes = ?, error_code = ?, completed_at = CASE WHEN ? = 'done' THEN ? ELSE completed_at END
                WHERE file_id = ?
                """, [.text(state.rawValue), .integer(uploadedBytes), SQLValue(errorCode), .text(state.rawValue),
                      .text(APIDate.format(now())), .text(fileId.key)])
        }
    }

    /// Hochgeladene Dateien, deren lokale Kopie nach der Aufbewahrungsfrist (7 Tage) entfernt werden darf.
    public func uploadsCompleted(before date: Date) throws -> [UUID] {
        try db.database.query("SELECT file_id, completed_at FROM upload WHERE state = 'done'").compactMap { row in
            guard let completed = row["completed_at"]?.string.flatMap(APIDate.parse), completed < date else { return nil }
            return row["file_id"]?.string.flatMap(UUID.init(uuidString:))
        }
    }

    public func forgetUpload(_ fileId: UUID) throws {
        try db.write { db in _ = try db.execute("DELETE FROM upload WHERE file_id = ?", [.text(fileId.key)]) }
    }

    // MARK: Intern

    public func meta(_ key: String) throws -> String? {
        try db.database.scalar("SELECT value FROM meta WHERE key = ?", [.text(key)]).string
    }

    /// Einstellungen und Listen der App (z. B. „zuletzt geöffnet“), mit der Datenbank verschlüsselt.
    public func setMeta(_ key: String, _ value: String) throws {
        try db.write { db in
            try db.execute("INSERT OR REPLACE INTO meta (key, value) VALUES (?, ?)", [.text(key), .text(value)])
        }
    }

    private func insertOperation(_ db: SQLiteDatabase, type: String, entityType: EntityType, entityId: UUID,
                                 baseVersion: Int64?, payload: JSONValue) throws {
        try db.execute("""
            INSERT INTO outbox (op_id, type, entity_type, entity_id, base_version, payload, created_at) VALUES (?, ?, ?, ?, ?, ?, ?)
            """, [.text(UUID.v7(now: now()).key), .text(type), .text(entityType.rawValue), .text(entityId.key),
                  baseVersion.map(SQLValue.integer) ?? .null, .text(try payload.encoded()), .text(APIDate.format(now()))])
    }

    private func replacePayload(_ db: SQLiteDatabase, _ operation: OutboxOperation, with payload: JSONValue) throws {
        try db.execute("UPDATE outbox SET payload = ? WHERE seq = ?", [.text(try payload.encoded()), .integer(operation.seq)])
    }

    /// Neuer Serverstand; der lokale Stand wird daraus plus ungesendeten Änderungen neu gebildet.
    private func storeServerState(_ db: SQLiteDatabase, _ type: EntityType, _ id: UUID, _ data: JSONValue) throws {
        let encoded = try data.encoded()
        let changed = try db.execute("UPDATE entity SET server = ?, seen = 1 WHERE type = ? AND id = ?",
                                     [.text(encoded), .text(type.rawValue), .text(id.key)])
        if changed == 0 {
            try db.execute("INSERT INTO entity (type, id, server, local, parent_id, search, sort_key) VALUES (?, ?, ?, ?, ?, ?, ?)",
                           [.text(type.rawValue), .text(id.key), .text(encoded), .text(encoded)] + derived(type, data))
        }
        try recompute(db, type, id)
    }

    private func recompute(_ db: SQLiteDatabase, _ type: EntityType, _ id: UUID) throws {
        guard let row = try db.query("SELECT server, local FROM entity WHERE type = ? AND id = ?", [.text(type.rawValue), .text(id.key)]).first
        else { return }
        let operations = try db.query("""
            SELECT * FROM outbox WHERE entity_type = ? AND entity_id = ? AND state IN ('pending', 'sending') ORDER BY seq
            """, [.text(type.rawValue), .text(id.key)]).map(makeOperation)
        var local: JSONValue
        if let serverText = row["server"]?.string {
            local = try JSONValue.decode(serverText)
            for operation in operations {
                local = Self.applyLocally(operation, to: local)
            }
        } else {
            local = try JSONValue.decode(row["local"]?.string ?? "{}")
            for operation in operations where operation.type.hasSuffix(".setStatus") {
                local = Self.applyLocally(operation, to: local)
            }
        }
        let deleted = operations.contains { $0.type.hasSuffix(".delete") }
        try db.execute("UPDATE entity SET local = ?, deleted = ?, parent_id = ?, search = ?, sort_key = ? WHERE type = ? AND id = ?",
                       [.text(try local.encoded()), .integer(deleted ? 1 : 0)] + derived(type, local) + [.text(type.rawValue), .text(id.key)])
    }

    /// Wirkung einer noch nicht bestätigten Operation auf den angezeigten Stand.
    static func applyLocally(_ operation: OutboxOperation, to local: JSONValue) -> JSONValue {
        if operation.type.hasSuffix(".update") {
            return local.merging(operation.payload["changes"]?.object ?? [:])
        }
        if operation.type.hasSuffix(".save") {
            return local.merging(operation.payload.object ?? [:])
        }
        if operation.type.hasSuffix(".setStatus"), let status = operation.payload["status"] {
            return local.merging(["status": status, "cancellationReason": status.string == "cancelled" ? operation.payload["reason"] ?? .null : .null])
        }
        return local
    }

    private func removeEntity(_ db: SQLiteDatabase, _ type: EntityType, _ id: UUID) throws {
        try db.execute("DELETE FROM entity WHERE type = ? AND id = ?", [.text(type.rawValue), .text(id.key)])
        try db.execute("DELETE FROM conflict WHERE entity_type = ? AND entity_id = ?", [.text(type.rawValue), .text(id.key)])
        if type == .bauakte {
            // Der Inhalt folgt seiner Bauakte (Notizen, Dateien, Aufgaben, Tagebuch, Prüfungen, Abnahmen, Räume, Beteiligte).
            try db.execute("""
                DELETE FROM entity WHERE parent_id = ? AND type IN ('note', 'file', 'workItem', 'diaryEntry', 'inspection', 'acceptance', 'room',
                'bauakteParty')
                """, [.text(id.key)])
        }
    }

    /// Nach einem vollständigen Neuabgleich: Was der Server nicht mehr kennt, wurde mit dem Backup verloren.
    /// Fotos, die das Gerät noch hat, werden erneut angekündigt und hochgeladen (15, Abschnitt 3).
    private func finishFullResyncIfNeeded(_ db: SQLiteDatabase) throws {
        guard try db.scalar("SELECT value FROM meta WHERE key = 'full_resync'").string == "1" else { return }
        let missing = try db.query("SELECT type, id, server FROM entity WHERE seen = 0 AND server IS NOT NULL")
        for row in missing {
            guard let typeText = row["type"]?.string, let type = EntityType(rawValue: typeText),
                  let id = row["id"]?.string.flatMap(UUID.init(uuidString:)) else { continue }
            let hasLocalCopy = try db.query("SELECT 1 FROM upload WHERE file_id = ?", [.text(id.key)]).first != nil
            if type == .file, hasLocalCopy, let server = try row["server"]?.string.map(JSONValue.decode) {
                try db.execute("UPDATE entity SET server = NULL, seen = 1 WHERE type = 'file' AND id = ?", [.text(id.key)])
                let fields = ["bauakteId", "kind", "originalName", "contentType", "sizeBytes", "sha256", "capturedAt", "latitude", "longitude", "caption"]
                let payload = Dictionary(uniqueKeysWithValues: fields.compactMap { field in server[field].map { (field, $0) } })
                try insertOperation(db, type: "file.announce", entityType: .file, entityId: id, baseVersion: nil, payload: .object(payload))
                try db.execute("UPDATE upload SET state = 'waiting', uploaded_bytes = 0, completed_at = NULL WHERE file_id = ?", [.text(id.key)])
            } else {
                try removeEntity(db, type, id)
            }
        }
        try db.execute("DELETE FROM meta WHERE key = 'full_resync'")
    }

    /// Abgeleitete Spalten für Abfragen: Bauakte, Suchtext, Sortierung.
    private func derived(_ type: EntityType, _ data: JSONValue) -> [SQLValue] {
        let parent = data["bauakteId"]?.string?.lowercased()
        let searchFields: [String] = switch type {
        case .bauakte: ["number", "title", "street", "postalCode", "city", "description"]
        case .note: ["text"]
        case .file: ["originalName", "caption"]
        case .workItem: ["title", "description"]
        case .contact: ["name", "customerNumber", "street", "city", "email", "phone"]
        case .screedType: ["name", "designation"]
        case .diaryEntry: ["text"]
        case .inspection: ["location", "remarks"]
        case .acceptance: ["clientName", "scope", "location"]
        case .valueListItem: ["list", "key", "label"]
        case .formSchema: ["key"]
        case .ticket: ["title"]
        case .room: ["name", "floor", "notes"]
        case .bauakteParty: ["name", "company", "phone", "email"]
        }
        let search = searchFields.compactMap { data[$0]?.string }.joined(separator: " ").lowercased()
        let sortKey = data["performedAt"]?.string ?? data["day"]?.string.map { $0 + (data["createdAt"]?.string ?? "") }
            ?? data["updatedAt"]?.string ?? data["createdAt"]?.string ?? data["capturedAt"]?.string ?? ""
        return [SQLValue(parent), .text(search), .text(sortKey)]
    }

    private func makeEntity(_ row: SQLRow) throws -> LocalEntity {
        let type = EntityType(rawValue: row["type"]?.string ?? "") ?? .bauakte
        let id = UUID(uuidString: row["id"]?.string ?? "") ?? UUID()
        let operations = try db.database.query("SELECT state FROM outbox WHERE entity_type = ? AND entity_id = ?",
                                               [.text(type.rawValue), .text(id.key)]).compactMap { $0["state"]?.string }
        var state: SyncState = operations.contains("rejected") ? .problem : (operations.isEmpty ? .synced : .pending)
        if type == .file, state == .synced,
           let upload = try db.database.query("SELECT state FROM upload WHERE file_id = ?", [.text(id.key)]).first?["state"]?.string {
            state = upload == "failed" ? .problem : (upload == "done" ? .synced : .pending)
        }
        return LocalEntity(type: type, id: id, data: try JSONValue.decode(row["local"]?.string ?? "{}"), syncState: state)
    }

    private func makeOperation(_ row: SQLRow) throws -> OutboxOperation {
        OutboxOperation(
            seq: row["seq"]?.int ?? 0,
            opId: UUID(uuidString: row["op_id"]?.string ?? "") ?? UUID(),
            type: row["type"]?.string ?? "",
            entityType: EntityType(rawValue: row["entity_type"]?.string ?? "") ?? .bauakte,
            entityId: UUID(uuidString: row["entity_id"]?.string ?? "") ?? UUID(),
            baseVersion: row["base_version"]?.int,
            payload: try JSONValue.decode(row["payload"]?.string ?? "{}"),
            state: OutboxOperation.State(rawValue: row["state"]?.string ?? "") ?? .pending,
            errorCode: row["error_code"]?.string,
            errorMessage: row["error_message"]?.string)
    }

    private func makeUpload(_ row: SQLRow) -> PendingUpload {
        PendingUpload(
            fileId: UUID(uuidString: row["file_id"]?.string ?? "") ?? UUID(),
            size: row["size"]?.int ?? 0,
            sha256: row["sha256"]?.string ?? "",
            contentType: row["content_type"]?.string ?? "application/octet-stream",
            uploadedBytes: row["uploaded_bytes"]?.int ?? 0,
            state: PendingUpload.State(rawValue: row["state"]?.string ?? "") ?? .waiting,
            errorCode: row["error_code"]?.string)
    }
}
