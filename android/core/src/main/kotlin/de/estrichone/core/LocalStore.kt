package de.estrichone.core

import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject

/** Entitätstypen des Änderungsfeeds (docs/sync-protokoll.md, Abschnitt 5). */
object EntityTypes {
    const val BAUAKTE = "bauakte"
    const val NOTE = "note"
    const val FILE = "file"
    const val WORK_ITEM = "workItem"
    const val CONTACT = "contact"
    const val SCREED_TYPE = "screedType"
    const val DIARY = "diaryEntry"
    const val INSPECTION = "inspection"
    const val ACCEPTANCE = "acceptance"
    const val VALUE_LIST = "valueListItem"
    const val FORM_SCHEMA = "formSchema"
    const val TICKET = "ticket"
    const val ROOM = "room"
    const val PARTY = "bauakteParty"

    val all = listOf(BAUAKTE, NOTE, FILE, WORK_ITEM, CONTACT, SCREED_TYPE, DIARY, INSPECTION, ACCEPTANCE, VALUE_LIST, FORM_SCHEMA, TICKET, ROOM, PARTY)

    /** Diese Typen gehören zu einer Bauakte und werden mit ihr entfernt. */
    val childTypes = listOf(NOTE, FILE, WORK_ITEM, DIARY, INSPECTION, ACCEPTANCE, ROOM, PARTY)

    /** Präfix der Operationstypen (`workitem.update`, `diary.create`). */
    fun operationPrefix(type: String): String = when (type) {
        WORK_ITEM -> "workitem"
        DIARY -> "diary"
        else -> type
    }

    fun fromOperation(operation: String): String? {
        val prefix = operation.substringBefore('.')
        return all.firstOrNull { operationPrefix(it) == prefix }
    }
}

/** Übertragungszustand eines Datensatzes für die Anzeige. */
enum class SyncState { Pending, Synced, Problem }

data class LocalEntity(val type: String, val id: String, val data: JsonObject, val syncState: SyncState)

/** Operation der Outbox (Abschnitt 2 des Protokolls). */
@Serializable
data class OutboxOperation(
    val seq: Long,
    val opId: String,
    val type: String,
    val entityType: String,
    val entityId: String,
    val baseVersion: Long? = null,
    val payload: JsonObject,
    val state: OutboxState = OutboxState.Pending,
    val errorCode: String? = null,
    val errorMessage: String? = null,
)

@Serializable
enum class OutboxState { Pending, Sending, Rejected }

/** Datei in der Upload-Warteschlange (Abschnitt 6). */
@Serializable
data class PendingUpload(
    val fileId: String,
    val size: Long,
    val sha256: String,
    val contentType: String,
    val uploadedBytes: Long = 0,
    val state: UploadState = UploadState.Waiting,
    val errorCode: String? = null,
    val completedAt: Long? = null,
)

@Serializable
enum class UploadState { Waiting, Uploading, Done, Failed }

/** Überblick für Kopfzeile und Synchronisationsansicht. */
data class SyncSummary(
    val pendingOperations: Int = 0,
    val rejectedOperations: Int = 0,
    val pendingUploads: Int = 0,
    val failedUploads: Int = 0,
    val lastSuccessfulSync: Long? = null,
) {
    val isSettled: Boolean get() = pendingOperations == 0 && pendingUploads == 0
    val hasProblems: Boolean get() = rejectedOperations > 0 || failedUploads > 0
}

/**
 * Dauerhafte Ablage der Synchronisation: Datensätze (JSON wie im Feed), Outbox, Upload-Warteschlange und Metadaten.
 * Android: verschlüsselte SQLite-Datenbank (SQLCipher); Tests und Demomodus: [MemoryStorage].
 * Alle Methoden laufen innerhalb von [transaction] atomar.
 */
interface StorageBackend {
    fun <T> transaction(block: () -> T): T

    fun getEntity(type: String, id: String): LocalEntity?
    fun entities(type: String): List<LocalEntity>
    fun putEntity(entity: LocalEntity, bauakteId: String?)
    fun deleteEntity(type: String, id: String)
    fun entitiesOfBauakte(bauakteId: String): List<LocalEntity>

    fun outbox(): List<OutboxOperation>
    fun insertOperation(operation: OutboxOperation): OutboxOperation
    fun updateOperation(operation: OutboxOperation)
    fun deleteOperation(seq: Long)

    fun uploads(): List<PendingUpload>
    fun putUpload(upload: PendingUpload)
    fun deleteUpload(fileId: String)

    fun meta(key: String): String?
    fun setMeta(key: String, value: String?)

    /** Alles löschen (Abmelden, Gerät zurücksetzen). */
    fun clear()
}

/** Speicher im Arbeitsspeicher (Unit-Tests, Demomodus, Screenshots). */
class MemoryStorage : StorageBackend {
    private val lock = Any()
    private val entities = LinkedHashMap<String, Pair<LocalEntity, String?>>()
    private val operations = ArrayList<OutboxOperation>()
    private val uploadList = LinkedHashMap<String, PendingUpload>()
    private val metadata = HashMap<String, String>()
    private var nextSeq = 1L

    override fun <T> transaction(block: () -> T): T = synchronized(lock) { block() }
    override fun getEntity(type: String, id: String) = synchronized(lock) { entities["$type/$id"]?.first }
    override fun entities(type: String) = synchronized(lock) { entities.values.map { it.first }.filter { it.type == type } }
    override fun putEntity(entity: LocalEntity, bauakteId: String?) {
        synchronized(lock) { entities["${entity.type}/${entity.id}"] = entity to bauakteId }
    }
    override fun deleteEntity(type: String, id: String) = synchronized(lock) { entities.remove("$type/$id"); Unit }
    override fun entitiesOfBauakte(bauakteId: String) = synchronized(lock) { entities.values.filter { it.second == bauakteId }.map { it.first } }
    override fun outbox() = synchronized(lock) { operations.sortedBy { it.seq } }
    override fun insertOperation(operation: OutboxOperation) = synchronized(lock) {
        operation.copy(seq = nextSeq++).also { operations.add(it) }
    }
    override fun updateOperation(operation: OutboxOperation) {
        synchronized(lock) {
            val index = operations.indexOfFirst { it.seq == operation.seq }
            if (index >= 0) operations[index] = operation
        }
    }
    override fun deleteOperation(seq: Long) = synchronized(lock) { operations.removeAll { it.seq == seq }; Unit }
    override fun uploads() = synchronized(lock) { uploadList.values.toList() }
    override fun putUpload(upload: PendingUpload) {
        synchronized(lock) { uploadList[upload.fileId] = upload }
    }
    override fun deleteUpload(fileId: String) = synchronized(lock) { uploadList.remove(fileId); Unit }
    override fun meta(key: String) = synchronized(lock) { metadata[key] }
    override fun setMeta(key: String, value: String?) {
        synchronized(lock) { if (value == null) metadata.remove(key) else metadata[key] = value }
    }
    override fun clear() = synchronized(lock) {
        entities.clear(); operations.clear(); uploadList.clear(); metadata.clear()
    }
}

/**
 * Lokaler Stand mit Outbox (lokal zuerst, docs/sync-protokoll.md, Abschnitt 1): Jede Änderung wird zusammen mit ihrer
 * Operation in einer Transaktion gespeichert. [revision] erhöht sich nach jeder Änderung; die Oberfläche lädt dann neu.
 */
class LocalStore(val backend: StorageBackend) {
    private val _revision = MutableStateFlow(0L)
    val revision: StateFlow<Long> = _revision

    private fun changed() {
        _revision.value = _revision.value + 1
    }

    fun entity(type: String, id: String): LocalEntity? = backend.getEntity(type, id.lowercase())

    fun entities(type: String): List<LocalEntity> = backend.entities(type)

    fun children(type: String, bauakteId: String): List<LocalEntity> =
        backend.entitiesOfBauakte(bauakteId.lowercase()).filter { it.type == type }

    // Lokale Änderungen

    /** Neuer Datensatz plus `<typ>.create` mit den Nutzdaten. */
    fun create(type: String, id: String, data: JsonObject, payload: JsonObject = data, operation: String = "${EntityTypes.operationPrefix(type)}.create") {
        val key = id.lowercase()
        backend.transaction {
            backend.putEntity(LocalEntity(type, key, data.merged(mapOf("id" to toJson(key))), SyncState.Pending), bauakteOf(type, key, data))
            backend.insertOperation(OutboxOperation(0, Ids.v7(), operation, type, key, null, payload))
        }
        changed()
    }

    /**
     * Feldweise Änderung (Abschnitt 3): `changes` plus `base` (Wert vor der Änderung). Ungesendete Änderungen desselben
     * Datensatzes werden zusammengefasst, dabei bleibt der älteste `base`-Wert je Feld erhalten.
     */
    fun update(type: String, id: String, changes: Map<String, JsonElement>, operation: String = "${EntityTypes.operationPrefix(type)}.update") {
        val key = id.lowercase()
        backend.transaction {
            val current = backend.getEntity(type, key) ?: throw IllegalStateException("Datensatz nicht gefunden")
            val base = changes.keys.associateWith { current.data[it] ?: JsonNull }
            backend.putEntity(current.copy(data = current.data.merged(changes), syncState = SyncState.Pending), bauakteOf(type, key, current.data))
            val pending = backend.outbox().lastOrNull { it.entityId == key && it.type == operation && it.state == OutboxState.Pending }
            if (pending != null) {
                val oldChanges = pending.payload.obj("changes") ?: JsonObject(emptyMap())
                val oldBase = pending.payload.obj("base") ?: JsonObject(emptyMap())
                val merged = json("changes" to oldChanges.merged(changes), "base" to JsonObject(base + oldBase.toMap()))
                backend.updateOperation(pending.copy(payload = merged))
            } else {
                backend.insertOperation(
                    OutboxOperation(0, Ids.v7(), operation, type, key, current.data.long("version"),
                        json("changes" to JsonObject(changes), "base" to JsonObject(base))),
                )
            }
        }
        changed()
    }

    /** Operation mit eigenen Nutzdaten (z. B. `bauakte.setStatus`) und lokaler Vorschau der Daten. */
    fun operation(type: String, id: String, operation: String, payload: JsonObject, localChanges: Map<String, JsonElement> = emptyMap()) {
        val key = id.lowercase()
        backend.transaction {
            backend.getEntity(type, key)?.let { current ->
                backend.putEntity(current.copy(data = current.data.merged(localChanges), syncState = SyncState.Pending), bauakteOf(type, key, current.data))
            }
            backend.insertOperation(OutboxOperation(0, Ids.v7(), operation, type, key, backend.getEntity(type, key)?.data?.long("version"), payload))
        }
        changed()
    }

    /** Löschen (`<typ>.delete`): lokal sofort entfernt. */
    fun delete(type: String, id: String) {
        val key = id.lowercase()
        backend.transaction {
            backend.deleteEntity(type, key)
            backend.insertOperation(OutboxOperation(0, Ids.v7(), "${EntityTypes.operationPrefix(type)}.delete", type, key, null, JsonObject(emptyMap())))
        }
        changed()
    }

    /** Datei ankündigen und in die Upload-Warteschlange stellen (Abschnitt 6). */
    fun announceFile(data: JsonObject, upload: PendingUpload) {
        val key = upload.fileId.lowercase()
        backend.transaction {
            backend.putEntity(LocalEntity(EntityTypes.FILE, key, data.merged(mapOf("id" to toJson(key))), SyncState.Pending), data.str("bauakteId")?.lowercase())
            backend.insertOperation(OutboxOperation(0, Ids.v7(), "file.announce", EntityTypes.FILE, key, null, data))
            backend.putUpload(upload.copy(fileId = key))
        }
        changed()
    }

    // Für die Synchronisation

    fun resetInterrupted() = backend.transaction {
        backend.outbox().filter { it.state == OutboxState.Sending }.forEach { backend.updateOperation(it.copy(state = OutboxState.Pending)) }
        backend.uploads().filter { it.state == UploadState.Uploading }.forEach { backend.putUpload(it.copy(state = UploadState.Waiting)) }
    }

    fun takeOperations(limit: Int): List<OutboxOperation> = backend.transaction {
        backend.outbox().filter { it.state == OutboxState.Pending }.take(limit).map { op ->
            op.copy(state = OutboxState.Sending).also { backend.updateOperation(it) }
        }
    }

    fun returnToPending(operations: List<OutboxOperation>) = backend.transaction {
        operations.forEach { backend.updateOperation(it.copy(state = OutboxState.Pending)) }
    }

    /** Ergebnisse übernehmen. Liefert `true`, wenn der Server um spätere Wiederholung gebeten hat. */
    fun applyResults(results: List<PushResult>, sent: List<OutboxOperation>): Boolean {
        var retry = false
        backend.transaction {
            val byOp = results.associateBy { it.opId.lowercase() }
            for (op in sent) {
                val result = byOp[op.opId.lowercase()]
                if (result == null) {
                    backend.updateOperation(op.copy(state = OutboxState.Pending))
                    continue
                }
                when (result.status) {
                    "applied", "duplicate", "conflict" -> {
                        backend.deleteOperation(op.seq)
                        val stillPending = backend.outbox().any { it.entityId == op.entityId && it.seq != op.seq && it.state != OutboxState.Rejected }
                        val entity = result.entity as? JsonObject
                        if (entity != null && !op.type.endsWith(".delete")) {
                            val existing = backend.getEntity(op.entityType, op.entityId)
                            val data = if (stillPending && existing != null) existing.data.merged(mapOf("version" to (entity["version"] ?: JsonNull))) else entity
                            backend.putEntity(
                                LocalEntity(op.entityType, op.entityId, data, if (stillPending) SyncState.Pending else SyncState.Synced),
                                bauakteOf(op.entityType, op.entityId, data),
                            )
                        } else if (!stillPending) {
                            backend.getEntity(op.entityType, op.entityId)?.let {
                                backend.putEntity(it.copy(syncState = SyncState.Synced), bauakteOf(it.type, it.id, it.data))
                            }
                        }
                        if (result.status == "conflict" && result.conflicts.isNotEmpty()) {
                            val count = (backend.meta("conflicts")?.toIntOrNull() ?: 0) + result.conflicts.size
                            backend.setMeta("conflicts", count.toString())
                        }
                    }
                    "rejected" -> when (result.code) {
                        "RETRY_LATER" -> {
                            backend.updateOperation(op.copy(state = OutboxState.Pending))
                            retry = true
                        }
                        "OP_ID_CONFLICT" -> backend.updateOperation(op.copy(opId = Ids.v7(), state = OutboxState.Pending))
                        else -> {
                            backend.updateOperation(op.copy(state = OutboxState.Rejected, errorCode = result.code, errorMessage = result.message))
                            backend.getEntity(op.entityType, op.entityId)?.let {
                                backend.putEntity(it.copy(syncState = SyncState.Problem), bauakteOf(it.type, it.id, it.data))
                            }
                            if (op.type == "file.announce") {
                                backend.uploads().firstOrNull { it.fileId == op.entityId }?.let { backend.putUpload(it.copy(state = UploadState.Failed, errorCode = result.code)) }
                            }
                        }
                    }
                    else -> backend.updateOperation(op.copy(state = OutboxState.Pending))
                }
            }
        }
        changed()
        return retry
    }

    var cursor: String?
        get() = backend.meta("cursor")
        set(value) = backend.transaction { backend.setMeta("cursor", value) }

    /** Seite des Änderungsfeeds übernehmen. Datensätze mit ungesendeten lokalen Änderungen bleiben unverändert. */
    fun applyPage(changes: List<SyncChange>, cursor: String?, visible: List<String>?) {
        backend.transaction {
            applyChanges(changes)
            if (visible != null) {
                val keep = visible.map { it.lowercase() }.toSet()
                val pendingIds = backend.outbox().filter { it.state != OutboxState.Rejected }.map { it.entityId }.toSet()
                backend.entities(EntityTypes.BAUAKTE).filter { it.id !in keep && it.id !in pendingIds }.forEach { removeBauakte(it.id) }
            }
            if (cursor != null) backend.setMeta("cursor", cursor)
            backend.setMeta("lastSync", System.currentTimeMillis().toString())
        }
        changed()
    }

    fun applySnapshot(changes: List<SyncChange>, missing: List<String>) {
        backend.transaction {
            applyChanges(changes)
            missing.forEach { removeBauakte(it.lowercase()) }
        }
        changed()
    }

    private fun applyChanges(changes: List<SyncChange>) {
        val pendingIds = backend.outbox().filter { it.state != OutboxState.Rejected }.map { it.entityId }.toSet()
        for (change in changes) {
            if (change.entityType !in EntityTypes.all) continue
            val id = change.id.lowercase()
            if (id in pendingIds) continue
            val data = change.data as? JsonObject
            if (change.deleted || data == null) {
                if (change.entityType == EntityTypes.BAUAKTE) removeBauakte(id) else backend.deleteEntity(change.entityType, id)
            } else {
                backend.putEntity(LocalEntity(change.entityType, id, data, SyncState.Synced), bauakteOf(change.entityType, id, data))
            }
        }
    }

    private fun removeBauakte(id: String) {
        backend.entitiesOfBauakte(id).forEach { backend.deleteEntity(it.type, it.id) }
        backend.deleteEntity(EntityTypes.BAUAKTE, id)
    }

    fun knownBauakteIds(): Set<String> = backend.entities(EntityTypes.BAUAKTE).map { it.id }.toSet()

    /** Neuabgleich ohne Cursor (Server aus Backup wiederhergestellt); Outbox bleibt erhalten. */
    fun beginFullResync() = backend.transaction { backend.setMeta("cursor", null) }

    /** Nach einem Update, das neue Datensatzarten versteht, einmal vollständig neu abgleichen. */
    fun resyncForNewTypesIfNeeded(): Boolean = backend.transaction {
        val known = EntityTypes.all.joinToString(",")
        if (backend.meta("knownTypes") == known) return@transaction false
        val hadCursor = backend.meta("cursor") != null
        backend.setMeta("knownTypes", known)
        backend.setMeta("cursor", null)
        hadCursor
    }

    fun uploadsReady(): List<PendingUpload> = backend.transaction {
        val announced = backend.outbox().filter { it.type == "file.announce" }.map { it.entityId }.toSet()
        backend.uploads().filter { (it.state == UploadState.Waiting || it.state == UploadState.Uploading) && it.fileId !in announced }
    }

    fun updateUpload(fileId: String, state: UploadState, uploadedBytes: Long, errorCode: String? = null) {
        backend.transaction {
            backend.uploads().firstOrNull { it.fileId == fileId }?.let {
                backend.putUpload(it.copy(state = state, uploadedBytes = uploadedBytes, errorCode = errorCode,
                    completedAt = if (state == UploadState.Done) System.currentTimeMillis() else null))
            }
            if (state == UploadState.Done) {
                backend.getEntity(EntityTypes.FILE, fileId)?.let {
                    backend.putEntity(it.copy(data = it.data.merged(mapOf("state" to toJson("stored"))), syncState = SyncState.Synced), it.data.str("bauakteId")?.lowercase())
                }
            }
        }
        changed()
    }

    /**
     * Fehlgeschlagene Uploads erneut einreihen (z. B. nach einer Ablehnung durch den Server, die inzwischen behoben ist).
     * Fehlt die Datei auf dem Gerät, hilft kein neuer Versuch. Liefert die Zahl der neu eingereihten Dateien.
     */
    fun retryFailedUploads(): Int {
        val count = backend.transaction {
            val failed = backend.uploads().filter { it.state == UploadState.Failed && it.errorCode != "LOCAL_FILE_MISSING" }
            failed.forEach { backend.putUpload(it.copy(state = UploadState.Waiting, uploadedBytes = 0, errorCode = null)) }
            failed.size
        }
        if (count > 0) changed()
        return count
    }

    fun uploadsCompletedBefore(time: Long): List<String> =
        backend.uploads().filter { it.state == UploadState.Done && (it.completedAt ?: 0) < time }.map { it.fileId }

    fun forgetUpload(fileId: String) = backend.transaction { backend.deleteUpload(fileId) }

    fun upload(fileId: String): PendingUpload? = backend.uploads().firstOrNull { it.fileId == fileId.lowercase() }

    fun summary(): SyncSummary {
        val ops = backend.outbox()
        val uploads = backend.uploads()
        return SyncSummary(
            pendingOperations = ops.count { it.state != OutboxState.Rejected },
            rejectedOperations = ops.count { it.state == OutboxState.Rejected },
            pendingUploads = uploads.count { it.state == UploadState.Waiting || it.state == UploadState.Uploading },
            failedUploads = uploads.count { it.state == UploadState.Failed },
            lastSuccessfulSync = backend.meta("lastSync")?.toLongOrNull(),
        )
    }

    fun rejected(): List<OutboxOperation> = backend.outbox().filter { it.state == OutboxState.Rejected }

    /** Abgelehnte Operation verwerfen (Benutzer hat die Meldung gesehen). */
    fun discard(operation: OutboxOperation) {
        backend.transaction { backend.deleteOperation(operation.seq) }
        changed()
    }

    fun clear() {
        backend.transaction { backend.clear() }
        changed()
    }

    /** Neu laden auslösen (z. B. nach Änderungen außerhalb des Stores). */
    fun touch() = changed()

    private fun bauakteOf(type: String, id: String, data: JsonObject): String? =
        if (type == EntityTypes.BAUAKTE) id else data.str("bauakteId")?.lowercase()
}
