package de.estrichone.android

import android.graphics.Bitmap
import android.util.LruCache
import de.estrichone.android.support.Images
import de.estrichone.core.ApiException
import de.estrichone.core.ApiSyncTransport
import de.estrichone.core.BauakteService
import de.estrichone.core.ChatStatus
import de.estrichone.core.DemoServer
import de.estrichone.core.EstrichOneApi
import de.estrichone.core.FileItem
import de.estrichone.core.FileVault
import de.estrichone.core.HealthBanner
import de.estrichone.core.LocalStore
import de.estrichone.core.SyncEngine
import de.estrichone.core.SyncOutcome
import de.estrichone.core.SyncSummary
import de.estrichone.core.SystemNotices
import de.estrichone.core.UserSummary
import de.estrichone.core.chatStatus
import de.estrichone.core.healthBanners
import de.estrichone.core.myPermissions
import de.estrichone.core.myTickets
import de.estrichone.core.notifications
import de.estrichone.core.systemNotices
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.FlowPreview
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.debounce
import kotlinx.coroutines.flow.drop
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext

/** Zustand der Synchronisation für Kopfzeile und Synchronisationsseite. */
data class SyncStatus(
    val running: Boolean = false,
    val outcome: SyncOutcome? = null,
    val summary: SyncSummary = SyncSummary(),
) {
    val offline: Boolean get() = outcome == SyncOutcome.Offline
    val maintenanceMessage: String? get() = (outcome as? SyncOutcome.Maintenance)?.let { it.message ?: "Der Server wird gewartet." }

    /** Kurztext („aktuell“, „3 ausstehend“, „offline“). */
    val shortText: String
        get() = when {
            running -> "wird abgeglichen"
            summary.hasProblems -> "Problem"
            outcome == SyncOutcome.Offline -> if (summary.isSettled) "offline" else "offline · ${summary.pendingOperations + summary.pendingUploads} ausstehend"
            !summary.isSettled -> "${summary.pendingOperations + summary.pendingUploads} ausstehend"
            else -> "aktuell"
        }
}

/**
 * Angemeldeter Benutzer: API-Client, lokaler Stand, Dateien und Abgleich. Gleicht alle 120 Sekunden und kurz nach jeder
 * lokalen Änderung ab und holt dabei Systemhinweise, Ausfall-Hinweise, Postfach und Chatstatus (wie SyncController auf iOS).
 */
class Session(
    val user: UserSummary,
    val api: EstrichOneApi,
    val store: LocalStore,
    val files: FileVault,
    val demo: DemoServer?,
    val deviceName: String,
    val isPool: Boolean,
) {
    val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    val service = BauakteService(store, files) { user.id }
    private val engine = SyncEngine(store, ApiSyncTransport(api), files)
    private val runLock = Mutex()
    private var loop: Job? = null
    private var firstRun = true

    private val _status = MutableStateFlow(SyncStatus(summary = store.summary()))
    val status: StateFlow<SyncStatus> = _status

    private val _notices = MutableStateFlow<SystemNotices?>(null)
    val notices: StateFlow<SystemNotices?> = _notices

    private val _health = MutableStateFlow<List<HealthBanner>>(emptyList())
    val health: StateFlow<List<HealthBanner>> = _health

    private val _chat = MutableStateFlow<ChatStatus?>(null)
    val chat: StateFlow<ChatStatus?> = _chat

    private val _unread = MutableStateFlow(0)
    val unreadNotifications: StateFlow<Int> = _unread

    private val _unreadTickets = MutableStateFlow(0)
    val unreadTickets: StateFlow<Int> = _unreadTickets

    private val _permissions = MutableStateFlow<Set<String>>(emptySet())
    val permissions: StateFlow<Set<String>> = _permissions

    val isDemo: Boolean get() = demo != null

    /** Vorschaubilder (klein) und Großansichten im Speicher. */
    private val thumbnails = LruCache<String, Bitmap>(80)

    @OptIn(FlowPreview::class)
    fun start() {
        if (loop != null) return
        loop = scope.launch {
            launch { store.revision.drop(1).debounce(1500).collect { if (!store.summary().isSettled) synchronize() } }
            while (true) {
                synchronize()
                delay(120_000)
            }
        }
    }

    /** Ein Abgleich mit allem Drumherum; parallele Aufrufe warten auf den laufenden. */
    suspend fun synchronize(): SyncOutcome = runLock.withLock {
        _status.value = _status.value.copy(running = true)
        val outcome = withContext(Dispatchers.IO) { engine.synchronize(includeScope = firstRun) }
        if (outcome == SyncOutcome.Completed) firstRun = false
        _status.value = SyncStatus(false, outcome, store.summary())
        withContext(Dispatchers.IO) { refreshExtras() }
        outcome
    }

    fun synchronizeNow() {
        scope.launch { synchronize() }
    }

    private suspend fun refreshExtras() {
        runCatching { api.systemNotices() }.onSuccess { _notices.value = it }
        if (_status.value.outcome == SyncOutcome.Offline) return
        runCatching { api.healthBanners() }.onSuccess { _health.value = it }
        runCatching { api.chatStatus() }.onSuccess { _chat.value = it }.onFailure { if (it is ApiException && it.status == 404) _chat.value = null }
        runCatching { api.notifications() }.onSuccess { _unread.value = it.unreadCount }
        runCatching { api.myTickets() }.onSuccess { list -> _unreadTickets.value = list.count { it.hasUnread } }
        if (_permissions.value.isEmpty()) runCatching { api.myPermissions() }.onSuccess { _permissions.value = it.toSet() }
    }

    fun refreshCounters() {
        scope.launch(Dispatchers.IO) {
            runCatching { api.notifications() }.onSuccess { _unread.value = it.unreadCount }
            runCatching { api.chatStatus() }.onSuccess { _chat.value = it }
        }
    }

    fun setUnreadNotifications(count: Int) {
        _unread.value = count
    }

    /** Vorschau: eigenes Original auf dem Gerät oder `thumb` vom Server. */
    suspend fun thumbnail(file: FileItem, size: Int = 360): Bitmap? {
        val key = "${file.id}-$size"
        thumbnails.get(key)?.let { return it }
        val bitmap = withContext(Dispatchers.IO) {
            val local = service.localContent(file.id)
            val data = local ?: if (file.isStoredOnServer) {
                runCatching { api.bytes("files/${file.id}/content", mapOf("variant" to if (size > 600) "display" else "thumb")) }.getOrNull()
                    ?: runCatching { api.bytes("files/${file.id}/content") }.getOrNull()
            } else {
                null
            }
            data?.let { Images.decode(it, size) }
        }
        bitmap?.let { thumbnails.put(key, it) }
        return bitmap
    }

    /** Inhalt einer Datei (Dokument öffnen, teilen). */
    suspend fun content(file: FileItem): ByteArray? = withContext(Dispatchers.IO) {
        service.localContent(file.id) ?: runCatching { api.bytes("files/${file.id}/content") }.getOrNull()
    }

    fun close() {
        loop?.cancel()
        scope.cancel()
    }
}
