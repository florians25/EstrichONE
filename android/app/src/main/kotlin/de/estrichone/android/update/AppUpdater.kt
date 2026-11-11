package de.estrichone.android.update

import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.content.pm.PackageInstaller
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.provider.Settings
import de.estrichone.android.BuildConfig
import de.estrichone.android.support.Links
import de.estrichone.core.ApiException
import de.estrichone.core.ApkDownloader
import de.estrichone.core.AppReleaseNote
import de.estrichone.core.AppUpdateCheck
import de.estrichone.core.AppUpdateMachine
import de.estrichone.core.ChecksumMismatchException
import de.estrichone.core.ClientIdentity
import de.estrichone.core.EstrichOneApi
import de.estrichone.core.UpdateEvent
import de.estrichone.core.UpdateSchedule
import de.estrichone.core.UpdateState
import de.estrichone.core.UpdateStatus
import de.estrichone.core.WhatsNew
import de.estrichone.core.appReleaseNotes
import de.estrichone.core.check
import de.estrichone.core.checkAppUpdate
import de.estrichone.core.isBusy
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull
import okhttp3.OkHttpClient
import java.io.File
import java.io.IOException
import java.security.MessageDigest
import java.util.concurrent.TimeUnit

/** Offene Änderungen vor der Installation übertragen; Ergebnis = verbleibende Anzahl (0 = alles gesendet). */
interface PendingChanges {
    fun count(): Int
    suspend fun synchronize(): Int
}

/**
 * App-Update für Android (docs/app-updates.md): prüft beim Start und beim Zurückkehren (gedrosselt) und von Hand,
 * überträgt vor der Installation offene Änderungen, lädt das APK vom eigenen Server (fortsetzbar, SHA-256 geprüft) und
 * installiert es mit dem PackageInstaller. Daten bleiben erhalten, solange das Update mit demselben Schlüssel signiert ist –
 * ein anderer Schlüssel (Testversion ↔ Release) wird vorab erkannt und erklärt.
 */
class AppUpdater(
    private val context: Context,
    private val api: () -> EstrichOneApi?,
    private val changes: () -> PendingChanges?,
) {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private val prefs = context.getSharedPreferences("estrichone-update", Context.MODE_PRIVATE)
    private val directory get() = File(context.cacheDir, "updates")

    private val _state = MutableStateFlow<UpdateState>(UpdateState.Idle)
    val state: StateFlow<UpdateState> = _state

    /** „Neu in dieser Version“ nach einem Update. */
    private val _whatsNew = MutableStateFlow<List<AppReleaseNote>>(emptyList())
    val whatsNew: StateFlow<List<AppReleaseNote>> = _whatsNew

    /** Hinweis für diese Version ausgeblendet („Später“) – bis zum nächsten Start. */
    private val _snoozed = MutableStateFlow<String?>(null)
    val snoozed: StateFlow<String?> = _snoozed

    /** Bestätigung des Systems für die Installation (die Activity zeigt sie an). */
    private val _confirmation = MutableStateFlow<Intent?>(null)
    val confirmation: StateFlow<Intent?> = _confirmation

    val installedVersion: String = BuildConfig.VERSION_NAME

    /** SHA-256 der eigenen Signaturzertifikate (für den Vergleich mit dem Update). */
    val signers: List<String> by lazy { ownSigners() }

    /** Screenshots: gezeigter Zustand bleibt stehen. */
    private var frozen = false

    private fun send(event: UpdateEvent) {
        if (frozen) return
        _state.value = AppUpdateMachine.reduce(_state.value, event, signers)
    }

    /** Beim Start bzw. beim Zurückkehren in die App; [trigger] bestimmt die Drosselung. */
    fun check(trigger: UpdateSchedule.Trigger = UpdateSchedule.Trigger.Manual) {
        val now = System.currentTimeMillis()
        val last = prefs.getLong("lastCheck", 0L).takeIf { it > 0 }
        if (!UpdateSchedule.isDue(last, now, trigger) || _state.value.isBusy || _state.value == UpdateState.Checking) return
        val client = api() ?: return
        if (trigger == UpdateSchedule.Trigger.Manual) send(UpdateEvent.CheckStarted)
        scope.launch {
            try {
                val result = withContext(Dispatchers.IO) { client.checkAppUpdate(installedVersion) }
                prefs.edit().putLong("lastCheck", now).apply()
                send(UpdateEvent.Checked(result))
            } catch (e: ApiException) {
                send(UpdateEvent.CheckFailed(e.message ?: "Prüfung nicht möglich"))
            } catch (e: Exception) {
                // Unerwartete Antwort (z. B. älterer Server ohne App-Updates): nie abstürzen, still weiterarbeiten.
                send(UpdateEvent.CheckFailed(e.message ?: "Prüfung nicht möglich"))
            }
        }
    }

    /** Der Server hat eine Anfrage mit 426 abgelehnt (von jedem Thread aus). */
    fun serverRejected(minimum: String?, message: String?) {
        scope.launch {
            send(UpdateEvent.Rejected(minimum, installedVersion, message))
            // Bezugsquelle und Neuerungen nachladen.
            check(UpdateSchedule.Trigger.ServerRejected)
        }
    }

    fun snooze() {
        _snoozed.value = _state.value.check?.latestVersion
    }

    fun dismiss() = send(UpdateEvent.Dismissed)

    /** „Jetzt aktualisieren“: App Store/MDM-Link öffnen bzw. Abgleich → Download → Prüfung → Installation. */
    fun start() {
        val check = _state.value.check ?: return
        when (check.download.kind) {
            "package" -> scope.launch { syncThenDownload(check) }
            "link", "store" -> check.download.url?.let { Links.web(context, it) }
                ?: send(UpdateEvent.Failure("Für dieses Update ist keine Bezugsquelle hinterlegt. Bitte an die IT wenden.", retry = false))
            else -> send(UpdateEvent.Failure("Der Server bietet kein Android-Update an. Bitte an die IT wenden.", retry = false))
        }
    }

    /** Trotz nicht übertragener Änderungen fortfahren (sie bleiben auf dem Gerät und werden nach dem Update gesendet). */
    fun continueWithoutSync() {
        val check = _state.value.check ?: return
        send(UpdateEvent.ContinueWithoutSync)
        scope.launch { download(check) }
    }

    private suspend fun syncThenDownload(check: AppUpdateCheck) {
        val pending = changes()
        val count = pending?.count() ?: 0
        if (pending != null && count > 0) {
            send(UpdateEvent.SyncStarted(count))
            val left = withTimeoutOrNull(90_000) { runCatching { pending.synchronize() }.getOrDefault(count) } ?: pending.count()
            send(UpdateEvent.SyncFinished(left))
            if (left > 0) return
        } else {
            send(UpdateEvent.SyncFinished(0))
        }
        download(check)
    }

    private suspend fun download(check: AppUpdateCheck) {
        val client = api() ?: return send(UpdateEvent.Failure("Keine Verbindung zum Server."))
        val url = client.baseUrl.resolve(check.download.url ?: "")?.toString()
        val sha = check.download.sha256
        if (url == null || sha.isNullOrBlank()) return send(UpdateEvent.Failure("Die Angaben zum Update sind unvollständig. Bitte an die IT wenden.", retry = false))
        val target = File(directory, "EstrichONE-${check.latestVersion ?: "update"}.apk")
        // Ältere, angefangene Downloads anderer Versionen entfernen.
        directory.listFiles()?.filter { it.name != target.name }?.forEach { it.delete() }
        try {
            withContext(Dispatchers.IO) {
                ApkDownloader(http).download(url, target, sha, check.download.size,
                    EstrichOneApi.clientIdentity?.let { mapOf(ClientIdentity.HEADER to it) } ?: emptyMap()) { received, total ->
                    scope.launch { send(UpdateEvent.Progress(received, total)) }
                }
            }
            send(UpdateEvent.DownloadFinished)
            send(UpdateEvent.Verified(target, canInstall()))
            if (_state.value is UpdateState.ReadyToInstall) install()
        } catch (e: ChecksumMismatchException) {
            send(UpdateEvent.Failure(e.message ?: "Prüfsumme stimmt nicht."))
        } catch (e: IOException) {
            send(UpdateEvent.Failure("Download unterbrochen (${e.message ?: "keine Verbindung"}). „Erneut versuchen“ setzt dort fort, wo er aufgehört hat."))
        } catch (e: Exception) {
            send(UpdateEvent.Failure("Download nicht möglich: ${e.message ?: e.javaClass.simpleName}"))
        }
    }

    /** Android 8+: Erlaubnis „Apps aus dieser Quelle installieren“. */
    fun canInstall(): Boolean = context.packageManager.canRequestPackageInstalls()

    fun openInstallPermission() {
        runCatching {
            context.startActivity(Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:${context.packageName}"))
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
        }
    }

    /** Zurück aus den Einstellungen: Erlaubnis erteilt? Dann gleich installieren. */
    fun onResume() {
        if (_state.value is UpdateState.NeedsInstallPermission && canInstall()) {
            send(UpdateEvent.PermissionGranted)
            install()
        }
    }

    /** Installation über den PackageInstaller (Sitzung; das System fragt nach Bestätigung). */
    fun install() {
        val ready = _state.value as? UpdateState.ReadyToInstall ?: return
        if (!canInstall()) {
            send(UpdateEvent.Verified(ready.file, false))
            return
        }
        send(UpdateEvent.InstallStarted)
        scope.launch {
            try {
                withContext(Dispatchers.IO) { commit(ready.file) }
            } catch (e: IOException) {
                send(UpdateEvent.Failure("Installation nicht möglich: ${e.message ?: "Fehler beim Übergeben an Android"}"))
            } catch (e: SecurityException) {
                send(UpdateEvent.Failure("Installation nicht erlaubt: ${e.message ?: ""}".trim()))
            } catch (e: Exception) {
                send(UpdateEvent.Failure("Installation nicht möglich: ${e.message ?: e.javaClass.simpleName}"))
            }
        }
    }

    private fun commit(file: File) {
        val installer = context.packageManager.packageInstaller
        val params = PackageInstaller.SessionParams(PackageInstaller.SessionParams.MODE_FULL_INSTALL).apply {
            setAppPackageName(context.packageName)
            setSize(file.length())
            // Ohne Rückfrage, wenn EstrichONE die App selbst installiert hat (Android 12+); sonst bestätigt der Benutzer.
            if (Build.VERSION.SDK_INT >= 31) setRequireUserAction(PackageInstaller.SessionParams.USER_ACTION_NOT_REQUIRED)
        }
        val id = installer.createSession(params)
        installer.openSession(id).use { session ->
            file.inputStream().use { input ->
                session.openWrite("estrichone.apk", 0, file.length()).use { output ->
                    input.copyTo(output, 64 * 1024)
                    session.fsync(output)
                }
            }
            val intent = Intent(context, InstallResultReceiver::class.java).setPackage(context.packageName)
            val flags = PendingIntent.FLAG_UPDATE_CURRENT or (if (Build.VERSION.SDK_INT >= 31) PendingIntent.FLAG_MUTABLE else 0)
            session.commit(PendingIntent.getBroadcast(context, id, intent, flags).intentSender)
        }
    }

    /** Ergebnis aus [InstallResultReceiver]. */
    fun installStatus(status: Int, message: String?, confirm: Intent?) {
        when (status) {
            PackageInstaller.STATUS_PENDING_USER_ACTION -> _confirmation.value = confirm
            PackageInstaller.STATUS_SUCCESS -> Unit // Android ersetzt die App und startet sie neu.
            PackageInstaller.STATUS_FAILURE_ABORTED -> {
                val ready = _state.value.check
                if (ready != null) (File(directory, "EstrichONE-${ready.latestVersion ?: "update"}.apk")).takeIf { it.exists() }?.let { send(UpdateEvent.Verified(it, true)) }
            }
            PackageInstaller.STATUS_FAILURE_CONFLICT, PackageInstaller.STATUS_FAILURE_INCOMPATIBLE -> send(UpdateEvent.InstallConflict)
            PackageInstaller.STATUS_FAILURE_STORAGE -> send(UpdateEvent.Failure("Zu wenig Speicher auf dem Gerät. Bitte Platz schaffen und erneut versuchen."))
            else -> send(UpdateEvent.Failure("Android hat das Update nicht installiert${message?.let { " ($it)" } ?: ""}."))
        }
    }

    fun confirmationShown() {
        _confirmation.value = null
    }

    // Neu in dieser Version

    /** Nach einem Update einmal die Neuerungen zeigen; ohne Verbindung beim nächsten Start erneut versuchen. */
    fun loadWhatsNew() {
        val last = prefs.getString("lastRunVersion", null)
        if (last == null) {
            prefs.edit().putString("lastRunVersion", installedVersion).apply()
            return
        }
        val since = WhatsNew.since(last, installedVersion) ?: run {
            if (last != installedVersion) prefs.edit().putString("lastRunVersion", installedVersion).apply()
            return
        }
        val client = api() ?: return
        // Heruntergeladene Pakete werden nach der Installation nicht mehr gebraucht.
        directory.deleteRecursively()
        scope.launch {
            runCatching { withContext(Dispatchers.IO) { client.appReleaseNotes(since, installedVersion) } }.onSuccess { notes ->
                if (notes.isEmpty()) prefs.edit().putString("lastRunVersion", installedVersion).apply() else _whatsNew.value = notes
            }
        }
    }

    fun whatsNewShown() {
        _whatsNew.value = emptyList()
        prefs.edit().putString("lastRunVersion", installedVersion).apply()
    }

    /** Nur für Screenshots und Tests. */
    internal fun show(state: UpdateState, notes: List<AppReleaseNote> = emptyList()) {
        frozen = true
        _state.value = state
        _whatsNew.value = notes
    }

    private val http by lazy {
        OkHttpClient.Builder().connectTimeout(20, TimeUnit.SECONDS).readTimeout(60, TimeUnit.SECONDS).retryOnConnectionFailure(true).build()
    }

    @Suppress("DEPRECATION")
    private fun ownSigners(): List<String> = runCatching {
        val info = context.packageManager.getPackageInfo(context.packageName, PackageManager.GET_SIGNING_CERTIFICATES)
        val signing = info.signingInfo ?: return@runCatching emptyList()
        val certificates = if (signing.hasMultipleSigners()) signing.apkContentsSigners else signing.signingCertificateHistory
        certificates.orEmpty().map { signature ->
            MessageDigest.getInstance("SHA-256").digest(signature.toByteArray()).joinToString("") { "%02x".format(it) }
        }
    }.getOrDefault(emptyList())

    /** Banner zeigen? Empfohlenes Update, nicht für diese Version „Später“ gewählt. */
    fun showsBanner(state: UpdateState, snoozed: String?): Boolean {
        val check = state.check ?: return false
        return check.status == UpdateStatus.Recommended && state !is UpdateState.UpToDate && snoozed != check.latestVersion
    }
}
