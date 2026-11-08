package de.estrichone.core

import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject

@Serializable
data class PushOperation(val opId: String, val type: String, val entityId: String, val baseVersion: Long? = null, val payload: JsonObject)

@Serializable
data class PushRequest(val operations: List<PushOperation>)

@Serializable
data class FieldConflict(val field: String, val serverValue: JsonElement? = null, val clientValue: JsonElement? = null)

@Serializable
data class PushResult(
    val opId: String,
    val status: String,
    val code: String? = null,
    val message: String? = null,
    val version: Long? = null,
    val conflicts: List<FieldConflict> = emptyList(),
    val entity: JsonElement? = null,
)

@Serializable
data class PushResponse(val results: List<PushResult>)

@Serializable
data class SyncChange(val entityType: String, val id: String, val deleted: Boolean = false, val data: JsonElement? = null)

@Serializable
data class PullResponse(val changes: List<SyncChange>, val cursor: String, val hasMore: Boolean = false, val visibleBauakteIds: List<String>? = null)

@Serializable
data class SnapshotResponse(val changes: List<SyncChange>, val missing: List<String> = emptyList())

@Serializable
private data class SnapshotRequest(val bauakteIds: List<String>)

@Serializable
data class UploadStatus(val id: String, val receivedBytes: Long, val sizeBytes: Long = 0, val state: String = "")

/** Netzwerkzugriff der Synchronisation (für Tests austauschbar). */
interface SyncTransport {
    suspend fun push(operations: List<PushOperation>): List<PushResult>
    suspend fun pull(cursor: String?, includeScope: Boolean): PullResponse
    suspend fun snapshot(bauakteIds: List<String>): SnapshotResponse
    suspend fun uploadStatus(fileId: String): UploadStatus

    /** Lädt [chunk] ab [offset] hoch; liefert angekommene Bytes und ob die Datei vollständig gespeichert ist. */
    suspend fun upload(fileId: String, chunk: ByteArray, offset: Long, total: Long): Pair<Long, Boolean>
}

class ApiSyncTransport(private val api: EstrichOneApi) : SyncTransport {
    override suspend fun push(operations: List<PushOperation>): List<PushResult> =
        api.send<PushResponse>("POST", "sync/push", PushRequest(operations)).results

    override suspend fun pull(cursor: String?, includeScope: Boolean): PullResponse =
        api.send("GET", "sync/pull", null, query = mapOf("limit" to "200", "cursor" to cursor, "includeScope" to if (includeScope) "true" else null))

    override suspend fun snapshot(bauakteIds: List<String>): SnapshotResponse = api.send("POST", "sync/snapshot", SnapshotRequest(bauakteIds))

    override suspend fun uploadStatus(fileId: String): UploadStatus = api.send("GET", "files/$fileId/upload-status", null)

    override suspend fun upload(fileId: String, chunk: ByteArray, offset: Long, total: Long): Pair<Long, Boolean> {
        val end = offset + chunk.size - 1
        val text = api.upload("PUT", "files/$fileId/content", chunk, "application/octet-stream", mapOf("Content-Range" to "bytes $offset-$end/$total"))
        val response = runCatching { EstrichOneApi.json.parseToJsonElement(text) as? JsonObject }.getOrNull()
        val received = response?.long("receivedBytes")
        // 200: FileResponse (vollständig gespeichert), 202: UploadStatus (Teil angekommen).
        return if (received != null) received to (response.str("state") == "stored") else total to true
    }
}

/** Dateien auf dem Gerät (Android: privater Speicher des Benutzers). */
interface FileVault {
    fun write(fileId: String, bytes: ByteArray)
    fun read(fileId: String): ByteArray?
    fun remove(fileId: String)
}

class MemoryFileVault : FileVault {
    private val files = HashMap<String, ByteArray>()
    override fun write(fileId: String, bytes: ByteArray) = synchronized(files) { files[fileId.lowercase()] = bytes }
    override fun read(fileId: String) = synchronized(files) { files[fileId.lowercase()] }
    override fun remove(fileId: String) = synchronized(files) { files.remove(fileId.lowercase()); Unit }
}

/** Ergebnis eines Synchronisationslaufs. */
sealed interface SyncOutcome {
    data object Completed : SyncOutcome
    /** Kein Netz: alles bleibt lokal gespeichert und wird später gesendet. */
    data object Offline : SyncOutcome
    data object RetryLater : SyncOutcome
    data class Maintenance(val message: String?) : SyncOutcome
    data class Failed(val error: ApiException) : SyncOutcome
}

/**
 * Ablauf eines Sync-Laufs (docs/sync-protokoll.md, Grundregel 6): Uploads fortsetzen → Push bis die Outbox leer ist →
 * neu angekündigte Dateien hochladen → Pull bis `hasMore = false` → fehlende Bauakten per Snapshot. Höchstens ein Lauf gleichzeitig.
 */
class SyncEngine(
    private val store: LocalStore,
    private val transport: SyncTransport,
    private val files: FileVault,
    private val now: () -> Long = System::currentTimeMillis,
) {
    private val gate = Mutex()
    private var running: CompletableDeferred<SyncOutcome>? = null
    private var prepared = false

    suspend fun synchronize(includeScope: Boolean = false): SyncOutcome {
        val joined = gate.withLock {
            running?.let { return@withLock it to false }
            CompletableDeferred<SyncOutcome>().also { running = it } to true
        }
        val (deferred, owner) = joined
        if (!owner) return deferred.await()
        val outcome = run(includeScope)
        gate.withLock { running = null }
        deferred.complete(outcome)
        return outcome
    }

    private suspend fun run(includeScope: Boolean): SyncOutcome = try {
        if (!prepared) {
            store.resetInterrupted()
            prepared = true
        }
        uploadReadyFiles()
        if (pushAll() == SyncOutcome.RetryLater) {
            SyncOutcome.RetryLater
        } else {
            uploadReadyFiles()
            pullAll(includeScope)
            removeExpiredLocalCopies()
            SyncOutcome.Completed
        }
    } catch (e: ApiException) {
        when {
            e.isOffline -> SyncOutcome.Offline
            e.isMaintenance -> SyncOutcome.Maintenance(e.message)
            e.status >= 500 || e.status == 429 -> SyncOutcome.RetryLater
            else -> SyncOutcome.Failed(e)
        }
    }

    private suspend fun pushAll(): SyncOutcome {
        while (true) {
            val operations = store.takeOperations(100)
            if (operations.isEmpty()) return SyncOutcome.Completed
            val results = try {
                transport.push(operations.map { PushOperation(it.opId, it.type, it.entityId, it.baseVersion, it.payload) })
            } catch (e: Exception) {
                store.returnToPending(operations)
                throw e
            }
            if (store.applyResults(results, operations)) return SyncOutcome.RetryLater
        }
    }

    /** Sichtbare, aber lokal fehlende Bauakten samt Inhalt nachladen (je 50). */
    suspend fun fillGaps(visible: List<String>) {
        val known = store.knownBauakteIds()
        val missing = visible.map { it.lowercase() }.filter { it !in known }
        missing.chunked(50).forEach { batch ->
            val snapshot = transport.snapshot(batch)
            store.applySnapshot(snapshot.changes, snapshot.missing)
        }
    }

    /** Eine Bauakte sofort vollständig laden (z. B. nach „Mir zuweisen“). */
    suspend fun load(bauakteId: String): SyncOutcome = try {
        val snapshot = transport.snapshot(listOf(bauakteId.lowercase()))
        store.applySnapshot(snapshot.changes, snapshot.missing)
        SyncOutcome.Completed
    } catch (e: ApiException) {
        if (e.isOffline) SyncOutcome.Offline else SyncOutcome.Failed(e)
    }

    private suspend fun pullAll(includeScope: Boolean) {
        var resynced = store.resyncForNewTypesIfNeeded()
        var first = true
        var visible: List<String>? = null
        while (true) {
            val page = try {
                transport.pull(store.cursor, includeScope && first)
            } catch (e: ApiException) {
                if (e.code == "RESYNC_REQUIRED" && !resynced) {
                    // Server aus einem Backup wiederhergestellt: Cursor verwerfen, vollständig neu abgleichen.
                    resynced = true
                    first = true
                    store.beginFullResync()
                    continue
                }
                throw e
            }
            first = false
            store.applyPage(page.changes, page.cursor, page.visibleBauakteIds)
            page.visibleBauakteIds?.let { visible = it }
            if (!page.hasMore) break
        }
        visible?.let { fillGaps(it) }
        if (resynced && pushAll() == SyncOutcome.Completed) uploadReadyFiles()
    }

    private suspend fun uploadReadyFiles() {
        for (upload in store.uploadsReady()) transfer(upload)
    }

    private suspend fun transfer(upload: PendingUpload) {
        val data = files.read(upload.fileId)
        if (data == null) {
            store.updateUpload(upload.fileId, UploadState.Failed, 0, "LOCAL_FILE_MISSING")
            return
        }
        var offset = try {
            val status = transport.uploadStatus(upload.fileId)
            if (status.state == "stored") {
                store.updateUpload(upload.fileId, UploadState.Done, upload.size)
                return
            }
            status.receivedBytes
        } catch (e: ApiException) {
            // Ankündigung beim Server (noch) unbekannt: später erneut versuchen.
            if (e.status == 404) return else throw e
        }
        var restarted = false
        store.updateUpload(upload.fileId, UploadState.Uploading, offset)
        while (true) {
            val end = minOf(offset + CHUNK_SIZE, data.size.toLong()).toInt()
            try {
                val (received, stored) = transport.upload(upload.fileId, data.copyOfRange(offset.toInt(), end), offset, data.size.toLong())
                if (stored) {
                    store.updateUpload(upload.fileId, UploadState.Done, data.size.toLong())
                    return
                }
                offset = received
                store.updateUpload(upload.fileId, UploadState.Uploading, offset)
            } catch (e: ApiException) {
                when {
                    e.code == "OFFSET_MISMATCH" -> offset = transport.uploadStatus(upload.fileId).receivedBytes
                    e.code == "CHECKSUM_MISMATCH" && !restarted -> {
                        restarted = true
                        offset = 0
                    }
                    e.status in 400..499 -> {
                        store.updateUpload(upload.fileId, UploadState.Failed, offset, e.code)
                        return
                    }
                    else -> throw e
                }
            }
        }
    }

    private fun removeExpiredLocalCopies() {
        for (fileId in store.uploadsCompletedBefore(now() - LOCAL_RETENTION_MS)) {
            files.remove(fileId)
            store.forgetUpload(fileId)
        }
    }

    companion object {
        const val CHUNK_SIZE = 4L * 1024 * 1024
        /** Hochgeladene Dateien bleiben 7 Tage lokal (Absicherung für eine Wiederherstellung des Servers). */
        const val LOCAL_RETENTION_MS = 7L * 24 * 3600 * 1000
    }
}
