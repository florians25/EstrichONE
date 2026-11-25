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
import de.estrichone.core.Dates
import de.estrichone.core.Drafts
import de.estrichone.core.MyPlanning
import de.estrichone.core.OfflinePack
import de.estrichone.core.myPlan
import de.estrichone.core.SyncEngine
import de.estrichone.core.SyncOutcome
import de.estrichone.core.SyncSummary
import de.estrichone.core.SystemNotices
import de.estrichone.core.UpdateMaintenance
import de.estrichone.core.UpdatePresentation
import de.estrichone.core.UserSummary
import de.estrichone.core.AppModules
import de.estrichone.core.chatStatus
import de.estrichone.core.TeamsStatus
import de.estrichone.core.teamsStatus
import de.estrichone.core.modules
import de.estrichone.core.healthBanners
import de.estrichone.core.myPermissions
import de.estrichone.core.warnMissingInspection
import de.estrichone.core.myTickets
import de.estrichone.core.notifications
import de.estrichone.core.systemNotices
import de.estrichone.core.assignToMe
import de.estrichone.core.leaveBauakte
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
            outcome == SyncOutcome.Offline -> if (summary.isSettled) "offline" else "offline, ${summary.pendingOperations + summary.pendingUploads} ausstehend"
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

    /** Laufendes Update (Ankündigung, Update, Abschluss); [updateOffline] = Server gerade nicht erreichbar (Container-Tausch). */
    private val _update = MutableStateFlow<UpdateMaintenance?>(null)
    val update: StateFlow<UpdateMaintenance?> = _update
    private val _updateOffline = MutableStateFlow(false)
    val updateOffline: StateFlow<Boolean> = _updateOffline
    private var updateSynced: String? = null
    private var updateBlocked = false

    /** Nach der Freigabe eines Updates (Sitzung serverseitig beendet): neu anmelden mit Hinweis. */
    @Volatile
    var onUpdateReleased: ((String) -> Unit)? = null

    private val _health = MutableStateFlow<List<HealthBanner>>(emptyList())
    val health: StateFlow<List<HealthBanner>> = _health

    private val _chat = MutableStateFlow<ChatStatus?>(null)
    val chat: StateFlow<ChatStatus?> = _chat
    private val _teams = MutableStateFlow<TeamsStatus?>(null)
    /** Microsoft Teams: Chat-Modus und Team „Baustellen“ (`null` = Server ohne Teams-Anbindung). */
    val teams: StateFlow<TeamsStatus?> = _teams

    private val _unread = MutableStateFlow(0)
    val unreadNotifications: StateFlow<Int> = _unread

    private val _unreadTickets = MutableStateFlow(0)
    val unreadTickets: StateFlow<Int> = _unreadTickets

    /** Rechte (nur für die Oberfläche; der Server prüft selbst), zwischengespeichert für den Offline-Betrieb. */
    private val _permissions = MutableStateFlow(store.backend.meta("my_permissions")?.split(',')?.filter { it.isNotEmpty() }?.toSet() ?: emptySet())
    val permissions: StateFlow<Set<String>> = _permissions

    /** Einstellung „Hinweis Erstbegehung fehlt“ des Büros, zwischengespeichert; ohne Angabe eingeschaltet. */
    private val _warnMissingInspection = MutableStateFlow(store.backend.meta("warn_missing_inspection") != "false")
    val warnMissingInspection: StateFlow<Boolean> = _warnMissingInspection

    /** Module der Suite (Systemeinstellungen › Module), zwischengespeichert; ohne Angabe alles an. */
    private val _modules = MutableStateFlow(AppModules.decode(store.backend.meta("app_modules")))
    val modules: StateFlow<AppModules> = _modules

    val isDemo: Boolean get() = demo != null

    /** Entwürfe der Erfassung: bei jeder Eingabe gesichert, nach Abbruch oder Neustart wieder da. */
    val drafts = Drafts(store.backend)

    /** Vorab geladene Dokumente und Vorschaubilder der Bauakten kommender Einsätze (Schlüssel mit Größe). */
    private val _offlineFiles = MutableStateFlow(OfflinePack.loaded(store))
    val offlineFiles: StateFlow<Map<String, Long>> = _offlineFiles
    private val prefetchLock = Mutex()

    /** Zuletzt geladene Online-Daten je Seite (Urlaub, Aufgaben …): beim Wiederöffnen sofort sichtbar, ohne Ladekreis. */
    val memo = java.util.concurrent.ConcurrentHashMap<String, Any>()

    /** Vorschaubilder (klein) und Großansichten im Speicher. */
    private val thumbnails = object : LruCache<String, Bitmap>((Runtime.getRuntime().maxMemory() / 8 / 1024).toInt().coerceAtLeast(16 * 1024)) {
        // Größe in KiB: ein Achtel des Heaps, damit lange Fotolisten flüssig scrollen, ohne den Speicher zu sprengen.
        override fun sizeOf(key: String, value: Bitmap): Int = (value.allocationByteCount / 1024).coerceAtLeast(1)
    }

    @OptIn(FlowPreview::class)
    fun start() {
        if (loop != null) return
        loop = scope.launch {
            launch { store.revision.drop(1).debounce(1500).collect { if (!store.summary().isSettled) synchronize() } }
            // Systemhinweise und Update-Ablauf: alle 30 s, während eines Updates alle 5 s (Countdown, Freigabe).
            launch { while (true) { delay(if (_update.value != null) 5_000 else 30_000); refreshNotices() } }
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
        // Bauakten der heutigen und kommenden Einsätze: Dokumente und Vorschaubilder nachladen, ohne den Abgleich aufzuhalten.
        if (outcome == SyncOutcome.Completed) scope.launch { prefetchUpcoming() }
        outcome
    }

    /** Zuletzt geladener Plan (ohne Netz), damit „Mein Einsatz“ sofort erscheint und nicht erst nach dem Abruf. */
    fun cachedPlan(): MyPlanning? =
        runCatching { store.backend.meta("plan")?.let { EstrichOneApi.json.decodeFromString(MyPlanning.serializer(), it) } }.getOrNull()

    /** Planung der nächsten 7 Tage; offline aus dem letzten Abruf (lokal gespeichert). */
    suspend fun loadPlan(today: java.time.LocalDate = java.time.LocalDate.now(Dates.zone)): MyPlanning? = withContext(Dispatchers.IO) {
        try {
            api.myPlan(today.toString(), today.plusDays(6).toString()).also { plan ->
                store.backend.transaction { store.backend.setMeta("plan", EstrichOneApi.json.encodeToString(MyPlanning.serializer(), plan)) }
            }
        } catch (e: ApiException) {
            cachedPlan()
        }
    }

    /**
     * Bauakten der Einsätze von heute bis in 6 Tagen offline bereitstellen: Die Daten bringt der Abgleich mit, hier kommen
     * Dokumente (Pläne, Aufträge) und Vorschaubilder der Fotos dazu. Was zu keinem kommenden Einsatz mehr gehört, wird entfernt.
     * Eigene Aufnahmen sind davon nie betroffen: Entfernt wird nur, was hier geladen wurde.
     */
    suspend fun prefetchUpcoming() {
        if (!prefetchLock.tryLock()) return
        try {
            withContext(Dispatchers.IO) {
                val plan = loadPlan()
                val wanted = OfflinePack.targets(plan, java.time.LocalDate.now(Dates.zone)).flatMap { id ->
                    // Eigene Dateien liegen als Original auf dem Gerät, solange sie in der Upload-Liste stehen.
                    OfflinePack.wanted(service.files(id)).filter { it.preview || store.upload(it.fileId) == null }
                }
                var loaded = OfflinePack.loaded(store)
                val todo = OfflinePack.plan(wanted, loaded)
                if (todo.evict.isNotEmpty()) {
                    todo.evict.forEach { files.remove(it) }
                    loaded = loaded - todo.evict.toSet()
                    OfflinePack.remember(store, loaded)
                    _offlineFiles.value = loaded
                }
                for (item in todo.load) {
                    val bytes = try {
                        api.bytes("files/${item.fileId}/content", if (item.preview) mapOf("variant" to "thumb") else emptyMap())
                    } catch (e: ApiException) {
                        // Ohne Netz später weiter; einzelne fehlende Dateien halten die übrigen nicht auf.
                        if (e.isOffline || e.isMaintenance) break else continue
                    }
                    if (bytes == null) continue
                    files.write(item.key, bytes)
                    loaded = loaded + (item.key to bytes.size.toLong())
                    OfflinePack.remember(store, loaded)
                    _offlineFiles.value = loaded
                }
            }
        } catch (e: Exception) {
            // Vorabladen ist Zugabe: Fehler (voller Speicher, Abbruch) dürfen die Arbeit nie stören.
            if (e is kotlinx.coroutines.CancellationException) throw e
        } finally {
            prefetchLock.unlock()
        }
    }

    /** Stand „Auf dem Gerät verfügbar“ einer Bauakte (Dokumente geladen bzw. als eigene Aufnahme vorhanden). */
    fun offlineState(bauakteId: String, loaded: Map<String, Long> = _offlineFiles.value): OfflinePack.State =
        OfflinePack.state(service.files(bauakteId), loaded) { store.upload(it) != null }

    fun synchronizeNow() {
        scope.launch { synchronize() }
    }

    /** „Mich als Beteiligten eintragen“: beim Server eintragen, danach die Bauakte vollständig aufs Gerät laden. */
    suspend fun joinBauakte(id: String) {
        withContext(Dispatchers.IO) {
            api.assignToMe(id)
            store.setViewOnly(id, false)
            engine.load(id)
        }
    }

    /** „Als Beteiligter austragen“: danach mit Umfang abgleichen (ohne andere Zuteilung verschwindet sie vom Gerät). */
    suspend fun leaveBauakte(id: String) {
        withContext(Dispatchers.IO) { api.leaveBauakte(id) }
        firstRun = true
        synchronize()
    }

    /** Hinweise neu laden und den Update-Ablauf nachführen (ohne Anmeldung lesbar, auch während des Updates). */
    suspend fun refreshNotices() {
        val result = withContext(Dispatchers.IO) { runCatching { api.systemNotices() } }
        result.onSuccess { notices ->
            _notices.value = notices
            if (!previewing) _updateOffline.value = false
            applyUpdate(notices.activeUpdate)
        }.onFailure { if (_update.value != null) _updateOffline.value = true }
    }

    /** Vorschau und Bildschirmfotos: Update-Stand direkt setzen. */
    fun previewUpdate(state: UpdateMaintenance?, offline: Boolean = false) {
        previewing = state != null
        _updateOffline.value = offline
        _update.value = state
    }

    /** Vorgegebener Stand (Vorschau): nicht vom Server überschreiben. */
    @Volatile
    private var previewing = false

    private fun applyUpdate(state: UpdateMaintenance?) {
        if (previewing) return
        val previous = _update.value
        _update.value = state
        if (state != null) {
            if (UpdatePresentation.screen(state, java.time.Instant.now()).page) updateBlocked = true
            // Ankündigung: offene Änderungen sofort übertragen, solange der Server noch annimmt.
            if (state.phase == "announce" && updateSynced != state.id) {
                updateSynced = state.id
                scope.launch { synchronize() }
            }
        } else if (previous != null && updateBlocked) {
            updateBlocked = false
            onUpdateReleased?.invoke(UpdatePresentation.doneText(previous.targetVersion, previous.phase == "failed"))
        }
    }

    private suspend fun refreshExtras() {
        runCatching { api.systemNotices() }.onSuccess { _notices.value = it; if (!previewing) _updateOffline.value = false; withContext(Dispatchers.Main) { applyUpdate(it.activeUpdate) } }
        if (_status.value.outcome == SyncOutcome.Offline) return
        runCatching { api.healthBanners() }.onSuccess { _health.value = it }
        runCatching { api.chatStatus() }.onSuccess { _chat.value = it }.onFailure { if (it is ApiException && it.status == 404) _chat.value = null }
        runCatching { api.teamsStatus() }.onSuccess { _teams.value = it }
        runCatching { api.notifications() }.onSuccess { _unread.value = it.unreadCount }
        runCatching { api.myTickets() }.onSuccess { list -> _unreadTickets.value = list.count { it.hasUnread } }
        loadSettings()
    }

    /** Rechte, Module und Büro-Einstellungen (z. B. „Hinweis Erstbegehung fehlt“) laden und offline vorhalten. */
    private suspend fun loadSettings() {
        runCatching { api.myPermissions() }.onSuccess {
            _permissions.value = it.toSet()
            store.backend.setMeta("my_permissions", it.joinToString(","))
        }
        runCatching { api.modules() }.onSuccess {
            _modules.value = it
            store.backend.setMeta("app_modules", it.encode())
        }
        runCatching { api.warnMissingInspection() }.onSuccess {
            _warnMissingInspection.value = it
            store.backend.setMeta("warn_missing_inspection", it.toString())
        }
    }

    /**
     * Beim Zurückkehren in die App: Einstellungen des Büros sofort neu laden, ohne auf den (ggf. längeren) Abgleich zu warten.
     * Höchstens alle 30 Sekunden, damit schnelles Hin- und Herwechseln keine Abrufe häuft.
     */
    fun refreshSettings() {
        val now = System.currentTimeMillis()
        if (now - lastSettingsRefresh < 30_000) return
        lastSettingsRefresh = now
        scope.launch(Dispatchers.IO) { loadSettings() }
    }

    @Volatile
    private var lastSettingsRefresh = 0L

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
            // Vorab geladene Vorschau: für kleine Ansichten sofort, für große als Ersatz ohne Netz.
            val preview = if (local == null && file.isPhoto && OfflinePack.previewKey(file.id) in _offlineFiles.value) files.read(OfflinePack.previewKey(file.id)) else null
            val data = local ?: preview?.takeIf { size <= 600 } ?: if (file.isStoredOnServer) {
                runCatching { api.bytes("files/${file.id}/content", mapOf("variant" to if (size > 600) "display" else "thumb")) }.getOrNull()
                    ?: runCatching { api.bytes("files/${file.id}/content") }.getOrNull() ?: preview
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
