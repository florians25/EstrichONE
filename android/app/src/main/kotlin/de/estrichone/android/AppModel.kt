package de.estrichone.android

import android.content.Context
import android.os.Build
import de.estrichone.android.data.DataKeys
import de.estrichone.android.data.DevicePrefs
import de.estrichone.android.data.DiskFileVault
import de.estrichone.android.data.KeystoreDeviceKeys
import de.estrichone.android.data.PoolUser
import de.estrichone.android.data.SecureSessionStore
import de.estrichone.android.data.SqlStorage
import de.estrichone.android.support.Images
import de.estrichone.android.update.AppUpdater
import de.estrichone.android.update.PendingChanges
import de.estrichone.core.ApiException
import de.estrichone.core.DemoServer
import de.estrichone.core.DeviceDescription
import de.estrichone.core.DeviceKind
import de.estrichone.core.DeviceStatus
import de.estrichone.core.EstrichOneApi
import de.estrichone.core.LocalStore
import de.estrichone.core.LockDelay
import de.estrichone.core.MemoryFileVault
import de.estrichone.core.MemorySessionStore
import de.estrichone.core.MemoryStorage
import de.estrichone.core.NotificationRoute
import de.estrichone.core.markNotificationRead
import de.estrichone.core.POOL_INACTIVITY_SECONDS
import de.estrichone.core.PinPolicy
import de.estrichone.core.PinVault
import de.estrichone.core.SessionStore
import de.estrichone.core.SoftwareDeviceKeys
import de.estrichone.core.UpdateSchedule
import de.estrichone.core.isUpdateRequired
import de.estrichone.core.UserSummary
import de.estrichone.core.WrongPinException
import de.estrichone.core.requiresTwoFactorCode
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File

/** Ablauf von Ersteinrichtung, Anmeldung und Entsperren (docs/reengineering/10 und 11). */
sealed interface Stage {
    data object Starting : Stage
    data object Welcome : Stage
    data class Server(val address: String = "") : Stage
    /** Anmeldung; [firstDevice]: Gerät ist noch nicht registriert (Art wählbar: persönlich oder Pool). */
    data class SignIn(val serverName: String, val lastLogin: String?, val firstDevice: Boolean, val isPool: Boolean) : Stage
    data class TwoFactor(val serverName: String, val login: String, val password: String, val enrollmentCode: String?, val kind: DeviceKind) : Stage {
        override fun toString() = "TwoFactor($login)"
    }
    data class ChangePassword(val enrollmentCode: String?, val kind: DeviceKind) : Stage
    data class Pending(val deviceName: String) : Stage
    /** Poolgerät: eigene PIN festlegen, bevor die Sitzung beginnt. */
    data class ChoosePin(val user: UserSummary) : Stage
    /** Poolgerät: Wer arbeitet gerade? */
    data class UserPicker(val users: List<PoolUser>) : Stage
    data class PinEntry(val user: PoolUser) : Stage
    /** Persönliches Gerät gesperrt: Fingerabdruck/Gesicht bzw. Gerätecode. */
    data object Locked : Stage
    data object Ready : Stage
}

/** Laufzeit-Abhängigkeiten der Plattform (Keystore, Datenbank); werden erst bei Bedarf angelegt. */
class Platform(private val context: Context) {
    val prefs by lazy { DevicePrefs(context) }
    val sessions by lazy { SecureSessionStore(context) }
    val keys by lazy { KeystoreDeviceKeys() }
    private val dataKeys by lazy { DataKeys(context) }
    private val databases = HashMap<String, SqlStorage>()

    private fun userDir(userId: String) = File(context.filesDir, "users/${userId.lowercase()}")

    fun store(userId: String): LocalStore = LocalStore(databases.getOrPut(userId) { SqlStorage(File(userDir(userId), "estrichone.db"), dataKeys.key("db-$userId")) })

    fun files(userId: String) = DiskFileVault(File(userDir(userId), "files"), dataKeys.key("files-$userId"))

    /** Daten eines Benutzers vom Gerät entfernen. */
    fun wipe(userId: String) {
        databases.remove(userId)?.close()
        userDir(userId).deleteRecursively()
        dataKeys.forget("db-$userId")
        dataKeys.forget("files-$userId")
    }

    fun wipeAll() {
        databases.values.forEach { it.close() }
        databases.clear()
        File(context.filesDir, "users").deleteRecursively()
        dataKeys.forgetAll()
        sessions.clearAll()
        prefs.clear()
        runCatching { keys.reset() }
    }
}

/** Sitzung eines Poolgeräte-Benutzers: Refresh-Token nur im Speicher, bei jeder Erneuerung neu mit der PIN versiegelt. */
private class PoolSessionStore(private val device: SecureSessionStore, user: UserSummary, token: String?, private val onToken: (String?) -> Unit) : SessionStore {
    override var refreshToken: String? = token
        set(value) {
            field = value
            onToken(value)
        }
    override var deviceId: String?
        get() = device.deviceId
        set(value) {
            device.deviceId = value
        }
    override var user: UserSummary? = user
}

/**
 * Anmeldung, Gerätebindung, Poolgeräte mit PIN, App-Sperre und Demomodus. Hält die aktive [Session].
 * Wie AppModel.swift auf iOS: Bildschirmablauf, Sitzungsende, Gerät entfernt, Neueinrichtung.
 */
class AppModel(private val context: Context, private val platform: Platform = Platform(context)) {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)

    private val _stage = MutableStateFlow<Stage>(Stage.Starting)
    val stage: StateFlow<Stage> = _stage

    private val _session = MutableStateFlow<Session?>(null)
    val session: StateFlow<Session?> = _session

    private val _busy = MutableStateFlow(false)
    val busy: StateFlow<Boolean> = _busy

    private val _error = MutableStateFlow<String?>(null)
    val error: StateFlow<String?> = _error

    private val _messages = MutableSharedFlow<String>(extraBufferCapacity = 4)
    val messages: SharedFlow<String> = _messages

    /** Erscheinungsbild (System/Hell/Dunkel) und Systemfarben. */
    private val _look = MutableStateFlow(platform.prefs.appearance to platform.prefs.dynamicColor)
    val look: StateFlow<Pair<String, Boolean>> = _look

    fun setLook(appearance: String, dynamicColor: Boolean) {
        prefs.appearance = appearance
        prefs.dynamicColor = dynamicColor
        _look.value = appearance to dynamicColor
    }

    /** Ziel aus einer angetippten Mitteilung; die Hauptansicht öffnet es, sobald sie bereit (entsperrt) ist. */
    private val _pendingRoute = MutableStateFlow<NotificationRoute?>(null)
    val pendingRoute: StateFlow<NotificationRoute?> = _pendingRoute

    private var pendingNotificationId: String? = null

    fun openFromNotification(route: NotificationRoute, notificationId: String?) {
        pendingNotificationId = notificationId
        _pendingRoute.value = route
    }

    /** Ziel ist geöffnet: Benachrichtigung als gelesen markieren (wie Antippen im Postfach). */
    fun routeOpened() {
        _pendingRoute.value = null
        val id = pendingNotificationId ?: return
        pendingNotificationId = null
        val session = _session.value ?: return
        session.scope.launch(Dispatchers.IO) {
            runCatching { session.api.markNotificationRead(id) }
            session.refreshCounters()
        }
    }

    private var api: EstrichOneApi? = null
    private var poolPin: String? = null

    /** App-Update (docs/app-updates.md): Prüfung gegen den eigenen Server, Download, Installation, Neuerungen. */
    val updater: AppUpdater = AppUpdater(context, api = { _session.value?.api ?: this.api }, changes = {
        _session.value?.takeIf { !it.isDemo }?.let { session ->
            object : PendingChanges {
                override fun count(): Int = session.store.summary().let { it.pendingOperations + it.pendingUploads }
                override suspend fun synchronize(): Int {
                    session.synchronize()
                    return count()
                }
            }
        }
    })

    /** Noch nicht übertragene Änderungen der angemeldeten Person (Hinweis auf dem Update-Bildschirm). */
    fun pendingChangeCount(): Int = _session.value?.takeIf { !it.isDemo }?.store?.summary()?.let { it.pendingOperations + it.pendingUploads } ?: 0

    private fun checkForUpdates() {
        updater.check(UpdateSchedule.Trigger.Start)
        updater.loadWhatsNew()
    }
    private var backgroundSince: Long? = null

    val prefs: DevicePrefs get() = platform.prefs

    val description: DeviceDescription
        get() = DeviceDescription(
            name = "${Build.MANUFACTURER?.replaceFirstChar { it.uppercase() } ?: "Android"} ${Build.MODEL ?: ""}".trim().take(100),
            model = Build.MODEL, osVersion = Build.VERSION.RELEASE, appVersion = BuildConfig.VERSION_NAME,
        )

    fun start() {
        scope.launch {
            if (prefs.demo) {
                startDemo()
                return@launch
            }
            val server = platform.sessions.server
            if (server == null) {
                _stage.value = Stage.Welcome
                return@launch
            }
            api = EstrichOneApi(server, platform.sessions, platform.keys)
            if (prefs.isPool) {
                _stage.value = poolStart()
                checkForUpdates()
                return@launch
            }
            _stage.value = try {
                if (api!!.restore()) {
                    val next = stageForDevice()
                    if (next == Stage.Ready) openSession(api!!, platform.sessions.user!!)
                    if (next == Stage.Ready && prefs.lockEnabled) Stage.Locked else next
                } else {
                    signInStage()
                }
            } catch (e: ApiException) {
                // App zu alt (426): lokale Daten öffnen, der Update-Bildschirm liegt darüber; nach dem Update geht es nahtlos weiter.
                if ((e.isOffline || e.isUpdateRequired) && platform.sessions.refreshToken != null && platform.sessions.user != null) {
                    // Offline weiterarbeiten: lokaler Stand ist auf dem Gerät, der Abgleich folgt später.
                    openSession(api!!, platform.sessions.user!!)
                    if (prefs.lockEnabled) Stage.Locked else Stage.Ready
                } else {
                    _error.value = e.message
                    signInStage()
                }
            } catch (e: IllegalStateException) {
                _error.value = e.message
                signInStage()
            }
            checkForUpdates()
        }
    }

    fun clearError() {
        _error.value = null
    }

    fun message(text: String) {
        _messages.tryEmit(text)
    }

    // Demomodus

    /** Demomodus: Beispieldaten im App-Prozess, nichts wird gesendet (wie „Demo ausprobieren“ auf iOS). */
    fun startDemo() {
        prefs.demo = true
        val server = DemoServer { id -> Images.demoContent(id) }
        val store = MemorySessionStore()
        val demoApi = EstrichOneApi("https://demo.estrichone.invalid", store, SoftwareDeviceKeys(), server.client())
        scope.launch {
            val tokens = demoApi.signIn("m.huber", "demo", "Demo")
            demoApi.bindDevice(DeviceKind.Personal, null, description)
            val session = Session(tokens.user, demoApi, LocalStore(MemoryStorage()), MemoryFileVault(), server, "Telefon von Max", isPool = false)
            _session.value = session
            session.start()
            _stage.value = Stage.Ready
            checkForUpdates()
        }
    }

    fun endDemo() {
        prefs.demo = false
        closeSession()
        _stage.value = if (platform.sessions.server != null) signInStage() else Stage.Welcome
    }

    // Ersteinrichtung

    fun beginSetup() {
        _stage.value = Stage.Server(platform.sessions.server ?: "")
    }

    fun connect(address: String) = run {
        val candidate = EstrichOneApi(address, platform.sessions, platform.keys)
        val info = candidate.serverInfo()
        if (info.setupRequired) throw IllegalStateException("Der Server ist noch nicht eingerichtet. Bitte zuerst die Ersteinrichtung im Browser abschließen.")
        if (platform.sessions.server != candidate.baseUrl.toString()) {
            // Anderer Server: alte Anmeldung und Gerätebindung gelten dort nicht.
            platform.sessions.refreshToken = null
            platform.sessions.deviceId = null
            platform.sessions.user = null
        }
        platform.sessions.server = candidate.baseUrl.toString()
        platform.sessions.serverName = info.serverName
        api = candidate
        _stage.value = signInStage()
    }

    fun changeServer() {
        _stage.value = Stage.Server(platform.sessions.server ?: "")
    }

    fun signIn(login: String, password: String, enrollmentCode: String?, kind: DeviceKind, twoFactorCode: String? = null) = run {
        val client = requireApi()
        val tokens = try {
            client.signIn(login, password, description.name, twoFactorCode)
        } catch (e: ApiException) {
            if (!e.requiresTwoFactorCode) throw e
            _stage.value = Stage.TwoFactor(platform.sessions.serverName ?: "", login, password, enrollmentCode, kind)
            return@run
        }
        if (tokens.mustChangePassword) {
            _stage.value = Stage.ChangePassword(enrollmentCode, kind)
            return@run
        }
        bind(enrollmentCode, kind)
    }

    fun changePassword(current: String, new: String, repeated: String) = run {
        val stage = _stage.value as? Stage.ChangePassword ?: return@run
        if (new != repeated) throw IllegalArgumentException("Die beiden neuen Passwörter stimmen nicht überein.")
        requireApi().changePassword(current, new)
        bind(stage.enrollmentCode, stage.kind)
    }

    fun cancelSignIn() {
        _error.value = null
        _stage.value = if (prefs.isPool && prefs.poolUsers.isNotEmpty()) Stage.UserPicker(prefs.poolUsers) else signInStage()
    }

    private suspend fun bind(enrollmentCode: String?, kind: DeviceKind) {
        val client = requireApi()
        val effectiveKind = if (platform.sessions.deviceId != null) (if (prefs.isPool) DeviceKind.Pool else DeviceKind.Personal) else kind
        try {
            client.bindDevice(effectiveKind, enrollmentCode, description)
        } catch (e: ApiException) {
            if (e.code != "DEVICE_NOT_FOUND" && e.code != "DEVICE_PROOF_INVALID") throw e
            // In der Verwaltung entfernt: neuer Schlüssel, neue Registrierung.
            platform.sessions.deviceId = null
            platform.keys.reset()
            client.bindDevice(effectiveKind, enrollmentCode, description)
        }
        prefs.isPool = effectiveKind == DeviceKind.Pool
        val next = stageForDevice()
        val user = client.user ?: throw IllegalStateException("Anmeldung unvollständig")
        _stage.value = when {
            next != Stage.Ready -> next
            prefs.isPool -> Stage.ChoosePin(user)
            else -> {
                openSession(client, user)
                Stage.Ready
            }
        }
    }

    fun checkApproval() = run {
        val next = stageForDevice()
        val client = requireApi()
        val user = client.user ?: return@run
        _stage.value = when {
            next != Stage.Ready -> next
            prefs.isPool -> Stage.ChoosePin(user)
            else -> {
                openSession(client, user)
                Stage.Ready
            }
        }
    }

    private suspend fun stageForDevice(): Stage {
        val device = requireApi().myDevice()
        return when (device.status) {
            DeviceStatus.Active -> Stage.Ready
            DeviceStatus.Pending -> Stage.Pending(device.name)
            DeviceStatus.Locked -> throw IllegalStateException("Dieses Gerät ist gesperrt. Bitte an die IT-Administration wenden.")
            DeviceStatus.Retired -> throw IllegalStateException("Dieses Gerät wurde aus EstrichONE entfernt.")
        }
    }

    private fun signInStage(): Stage = Stage.SignIn(
        platform.sessions.serverName ?: platform.sessions.server ?: "", platform.sessions.user?.userName,
        firstDevice = platform.sessions.deviceId == null, isPool = prefs.isPool,
    )

    // Poolgeräte

    private fun poolStart(): Stage {
        val users = prefs.poolUsers
        return if (users.isEmpty()) signInStage() else Stage.UserPicker(users.sortedByDescending { it.lastUsed })
    }

    fun choosePin(pin: String, repeated: String) = run {
        val stage = _stage.value as? Stage.ChoosePin ?: return@run
        PinPolicy.violation(pin)?.let { throw IllegalArgumentException(it) }
        if (pin != repeated) throw IllegalArgumentException("Die beiden PINs stimmen nicht überein.")
        val client = requireApi()
        val token = platform.sessions.refreshToken ?: throw IllegalStateException("Bitte erneut anmelden.")
        val sealed = withContext(Dispatchers.Default) { PinVault.seal(token, pin) }
        prefs.poolUsers = prefs.poolUsers.filter { it.user.id != stage.user.id } + PoolUser(stage.user, sealed, 0, System.currentTimeMillis())
        // Auf Poolgeräten liegt das Refresh-Token nur im PIN-Tresor.
        platform.sessions.refreshToken = null
        poolPin = pin
        openPoolSession(stage.user, token, pin, client.baseUrl.toString())
        _stage.value = Stage.Ready
    }

    fun pickUser(user: PoolUser) {
        _error.value = null
        _stage.value = Stage.PinEntry(user)
    }

    fun addPoolUser() {
        _error.value = null
        _stage.value = signInStage()
    }

    fun unlockPool(pin: String) = run {
        val stage = _stage.value as? Stage.PinEntry ?: return@run
        val entry = prefs.poolUsers.firstOrNull { it.user.id == stage.user.user.id } ?: return@run
        val token = try {
            withContext(Dispatchers.Default) { PinVault.open(entry.sealed, pin) }
        } catch (_: WrongPinException) {
            val attempts = entry.failedAttempts + 1
            if (attempts >= PinPolicy.MAX_ATTEMPTS) {
                // Tresor gesperrt: Anmeldung nur noch mit Passwort.
                prefs.poolUsers = prefs.poolUsers.filter { it.user.id != entry.user.id }
                _stage.value = signInStage()
                throw IllegalStateException("Zu viele falsche PINs. Bitte mit Passwort anmelden.")
            }
            prefs.poolUsers = prefs.poolUsers.map { if (it.user.id == entry.user.id) it.copy(failedAttempts = attempts) else it }
            throw IllegalArgumentException("Die PIN ist falsch. Noch ${PinPolicy.MAX_ATTEMPTS - attempts} Versuche.")
        }
        prefs.poolUsers = prefs.poolUsers.map { if (it.user.id == entry.user.id) it.copy(failedAttempts = 0, lastUsed = System.currentTimeMillis()) else it }
        poolPin = pin
        openPoolSession(entry.user, token, pin, platform.sessions.server ?: throw IllegalStateException("Kein Server"))
        _stage.value = Stage.Ready
    }

    private fun openPoolSession(user: UserSummary, token: String, pin: String, server: String) {
        val store = PoolSessionStore(platform.sessions, user, token) { fresh ->
            if (fresh != null) {
                val sealed = PinVault.seal(fresh, pin)
                prefs.poolUsers = prefs.poolUsers.map { if (it.user.id == user.id) it.copy(sealed = sealed) else it }
            }
        }
        val client = EstrichOneApi(server, store, platform.keys)
        api = client
        openSession(client, user)
    }

    /** Poolgerät: Benutzer wechseln (Sitzung bleibt im Tresor). */
    fun switchUser() {
        closeSession()
        poolPin = null
        _stage.value = poolStart()
    }

    // Sitzung

    private fun openSession(client: EstrichOneApi, user: UserSummary) {
        closeSession()
        client.sessionEnded = { problem -> scope.launch { sessionEnded(problem) } }
        val session = Session(user, client, platform.store(user.id), platform.files(user.id), null, description.name, prefs.isPool)
        // Update freigegeben: Sitzung ist serverseitig beendet – zur Anmeldung, Daten bleiben auf dem Gerät.
        session.onUpdateReleased = { text -> scope.launch { sessionEnded(ApiException(401, "UPDATED", text)) } }
        _session.value = session
        session.start()
    }

    private fun closeSession() {
        _session.value?.close()
        _session.value = null
    }

    /** Der Server hat die Sitzung beendet (Gerät gesperrt/entfernt, Passwort zurückgesetzt): neu anmelden, Daten bleiben. */
    private fun sessionEnded(problem: ApiException) {
        if (_session.value?.isDemo == true) return
        _error.value = problem.message ?: "Bitte erneut anmelden."
        closeSession()
        _stage.value = signInStage()
    }

    fun signOut() = run {
        val session = _session.value
        if (session?.isDemo == true) {
            endDemo()
            return@run
        }
        runCatching { session?.api?.signOut() }
        session?.user?.id?.let { id ->
            if (prefs.isPool) prefs.poolUsers = prefs.poolUsers.filter { it.user.id != id }
        }
        closeSession()
        _stage.value = if (prefs.isPool && prefs.poolUsers.isNotEmpty()) Stage.UserPicker(prefs.poolUsers) else signInStage()
    }

    /** Gerät zurücksetzen: alle Daten, Schlüssel und Anmeldungen löschen (wie AppWipe auf iOS). */
    fun resetDevice() = run {
        val session = _session.value
        runCatching { if (session?.isDemo != true) session?.api?.signOut() }
        closeSession()
        withContext(Dispatchers.IO) { platform.wipeAll() }
        api = null
        _stage.value = Stage.Welcome
    }

    // App-Sperre

    fun onBackground() {
        backgroundSince = System.currentTimeMillis()
        _session.value?.takeIf { !it.isDemo && !it.store.summary().isSettled }?.let {
            runCatching { de.estrichone.android.work.SyncWorker.scheduleSoon(context) }
        }
        if (_session.value != null && prefs.isPool && !prefs.demo) switchUser()
    }

    fun onForeground() {
        val since = backgroundSince ?: return
        backgroundSince = null
        // Modulzustand und Büro-Einstellungen gleich beim Zurückkehren auffrischen (auch hinter der App-Sperre).
        _session.value?.refreshSettings()
        updater.check(UpdateSchedule.Trigger.Resume)
        if (_stage.value != Stage.Ready || _session.value?.isDemo == true) return
        val delay = LockDelay.entries.firstOrNull { it.seconds == prefs.lockDelaySeconds } ?: LockDelay.FifteenMinutes
        if (prefs.lockEnabled && delay.shouldLock(since, System.currentTimeMillis())) _stage.value = Stage.Locked
        _session.value?.synchronizeNow()
    }

    /** Poolgerät: nach 5 Minuten ohne Bedienung zurück zur Benutzerauswahl. */
    fun onInactive(seconds: Long) {
        if (prefs.isPool && seconds >= POOL_INACTIVITY_SECONDS && _stage.value == Stage.Ready) switchUser()
    }

    fun unlocked() {
        if (_stage.value == Stage.Locked) _stage.value = Stage.Ready
    }

    fun setLock(enabled: Boolean, delay: LockDelay, biometric: Boolean) {
        prefs.lockEnabled = enabled
        prefs.lockDelaySeconds = delay.seconds
        prefs.biometricUnlock = biometric
    }

    /** Nur für Tests und Screenshots: Bildschirm der Einrichtung direkt zeigen. */
    internal fun showStage(stage: Stage) {
        _stage.value = stage
    }

    private fun requireApi(): EstrichOneApi = api ?: throw IllegalStateException("Keine Verbindung zum Server")

    private fun run(action: suspend () -> Unit) {
        scope.launch {
            _busy.value = true
            _error.value = null
            try {
                action()
            } catch (e: ApiException) {
                _error.value = e.message
            } catch (e: IllegalArgumentException) {
                _error.value = e.message
            } catch (e: IllegalStateException) {
                _error.value = e.message
            } finally {
                _busy.value = false
            }
        }
    }
}
