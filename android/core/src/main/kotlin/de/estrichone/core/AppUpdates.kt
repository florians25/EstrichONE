package de.estrichone.core

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable
import okhttp3.OkHttpClient
import okhttp3.Request
import java.io.File
import java.io.IOException
import java.io.RandomAccessFile
import java.security.MessageDigest

// App-Updates (docs/app-updates.md): Server als Quelle der Wahrheit, Download mit Fortsetzen und Prüfsumme,
// Zustände für Hinweis, Sperre und Installation. Plattformneutral und ohne Android SDK testbar.

/** Versionsvergleich nach Semantic Versioning wie auf dem Server (1.1.0-preview.9 < 1.1.0-preview.10 < 1.1.0 < 1.1.1). */
object AppVersions {
    private val pattern = Regex("""^v?\d{1,6}(\.\d{1,6}){0,2}(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?(\+.*)?$""")

    fun isValid(value: String?): Boolean = value != null && pattern.matches(value.trim())

    fun compare(a: String, b: String): Int {
        val (ca, pa) = split(a)
        val (cb, pb) = split(b)
        for (i in 0 until 3) if (ca[i] != cb[i]) return ca[i].compareTo(cb[i]).coerceIn(-1, 1)
        if (pa.isEmpty() || pb.isEmpty()) return if (pa.size == pb.size) 0 else if (pa.isEmpty()) 1 else -1
        for (i in 0 until minOf(pa.size, pb.size)) {
            val na = pa[i].toIntOrNull()
            val nb = pb[i].toIntOrNull()
            val r = when {
                na != null && nb != null -> na.compareTo(nb)
                na != null -> -1
                nb != null -> 1
                else -> pa[i].compareTo(pb[i])
            }
            if (r != 0) return r.coerceIn(-1, 1)
        }
        return pa.size.compareTo(pb.size).coerceIn(-1, 1)
    }

    private fun split(value: String): Pair<List<Int>, List<String>> {
        val text = value.trim().removePrefix("v").substringBefore('+')
        val core = text.substringBefore('-').split('.').map { it.toIntOrNull() ?: 0 }.let { it + List(3 - it.size.coerceAtMost(3)) { 0 } }
        val pre = if ('-' in text) text.substringAfter('-').split('.') else emptyList()
        return core.take(3) to pre
    }
}

@Serializable
enum class UpdateStatus {
    @SerialName("current") Current,
    @SerialName("recommended") Recommended,
    @SerialName("required") Required,
    @SerialName("newerThanServer") NewerThanServer,
}

/** Bezugsquelle: `package` (APK vom Server), `link` (Browser/MDM), `store`, `none`. */
@Serializable
data class AppDownload(
    val kind: String = "none",
    val url: String? = null,
    val sha256: String? = null,
    val size: Long? = null,
    val versionCode: Int? = null,
    val signerSha256: String? = null,
    val releaseSigned: Boolean = false,
)

@Serializable
data class AppReleaseSection(val title: String, val items: List<String> = emptyList())

@Serializable
data class AppReleaseNote(val version: String, val title: String, val sections: List<AppReleaseSection> = emptyList())

@Serializable
data class AppUpdateCheck(
    val platform: String = "android",
    val installedVersion: String,
    val serverVersion: String,
    val latestVersion: String? = null,
    val minimumVersion: String = "0.0.0",
    val status: UpdateStatus = UpdateStatus.Current,
    val message: String? = null,
    val download: AppDownload = AppDownload(),
    val releaseNotes: List<AppReleaseNote> = emptyList(),
)

/** Kennung der App für den Server (`X-EstrichONE-Client`), z. B. `android/1.0.5`. */
object ClientIdentity {
    const val HEADER = "X-EstrichONE-Client"
    const val UPDATE_REQUIRED = "CLIENT_UPDATE_REQUIRED"

    fun android(versionName: String) = "android/${versionName.trim()}"
}

/** 426 `CLIENT_UPDATE_REQUIRED`: Die App ist zu alt für diesen Server – Update-Bildschirm statt Fehlermeldung. */
val ApiException.isUpdateRequired: Boolean get() = status == 426 && code == ClientIdentity.UPDATE_REQUIRED

suspend fun EstrichOneApi.checkAppUpdate(version: String, platform: String = "android"): AppUpdateCheck =
    send("GET", "app-updates/check", null, authenticated = false, query = mapOf("platform" to platform, "version" to version))

suspend fun EstrichOneApi.appReleaseNotes(from: String?, to: String, platform: String = "android"): List<AppReleaseNote> =
    send("GET", "app-updates/release-notes", null, authenticated = false, query = mapOf("platform" to platform, "from" to from, "to" to to))

/** Wann wieder geprüft wird: beim Start nach 30 Minuten, beim Zurückkehren nach 6 Stunden, von Hand immer. */
object UpdateSchedule {
    const val ON_START_MS = 30 * 60 * 1000L
    const val ON_RESUME_MS = 6 * 60 * 60 * 1000L

    enum class Trigger { Start, Resume, Manual, ServerRejected }

    fun isDue(lastCheck: Long?, now: Long, trigger: Trigger): Boolean = when (trigger) {
        Trigger.Manual, Trigger.ServerRejected -> true
        Trigger.Start -> lastCheck == null || now - lastCheck >= ON_START_MS || now < lastCheck
        Trigger.Resume -> lastCheck == null || now - lastCheck >= ON_RESUME_MS || now < lastCheck
    }
}

/** „Neu in dieser Version“: einmal nach einem Update (nicht nach der Erstinstallation). */
object WhatsNew {
    /** Version, ab der die Neuerungen zu zeigen sind, oder `null`. */
    fun since(lastRunVersion: String?, current: String): String? =
        lastRunVersion?.takeIf { AppVersions.isValid(it) && AppVersions.isValid(current) && AppVersions.compare(current, it) > 0 }
}

/** Signaturzertifikate vergleichen (SHA-256 hex, mit oder ohne Doppelpunkte). */
object Signer {
    fun normalize(value: String): String = value.filter { it.isLetterOrDigit() }.lowercase()

    /** `false` nur, wenn beide bekannt sind und sich unterscheiden – dann lässt Android das Update nicht zu. */
    fun compatible(installed: List<String>, update: String?): Boolean =
        update.isNullOrBlank() || installed.isEmpty() || installed.any { normalize(it) == normalize(update) }
}

/** Zustand des App-Updates (Android). Übergänge in [AppUpdateMachine]. */
sealed interface UpdateState {
    data object Idle : UpdateState
    data object Checking : UpdateState
    data class UpToDate(val check: AppUpdateCheck?) : UpdateState
    /** Neuere Version verfügbar (Hinweis) bzw. erforderlich (Sperre, [AppUpdateCheck.status] = Required). */
    data class Available(val check: AppUpdateCheck) : UpdateState
    /** Vor der Installation: Abgleich der offenen Änderungen läuft bzw. ist nicht vollständig gelungen. */
    data class Syncing(val check: AppUpdateCheck, val pending: Int) : UpdateState
    data class UnsyncedChanges(val check: AppUpdateCheck, val pending: Int) : UpdateState
    data class Downloading(val check: AppUpdateCheck, val received: Long, val total: Long?) : UpdateState {
        val fraction: Float? get() = total?.takeIf { it > 0 }?.let { (received.toDouble() / it).toFloat().coerceIn(0f, 1f) }
    }
    data class Verifying(val check: AppUpdateCheck) : UpdateState
    /** Android muss „Apps aus dieser Quelle installieren“ erlauben. */
    data class NeedsInstallPermission(val check: AppUpdateCheck, val file: File) : UpdateState
    data class ReadyToInstall(val check: AppUpdateCheck, val file: File) : UpdateState
    data class Installing(val check: AppUpdateCheck) : UpdateState
    /** Update mit anderem Schlüssel signiert (z. B. Testversion ↔ Release): Installation unmöglich, Erklärung zeigen. */
    data class SignatureMismatch(val check: AppUpdateCheck) : UpdateState
    data class Failed(val check: AppUpdateCheck?, val message: String, val retry: Boolean = true) : UpdateState
}

/** Ereignisse für [AppUpdateMachine.reduce]. */
sealed interface UpdateEvent {
    data object CheckStarted : UpdateEvent
    data class Checked(val check: AppUpdateCheck) : UpdateEvent
    data class CheckFailed(val message: String) : UpdateEvent
    /** Der Server hat eine Anfrage mit 426 abgelehnt (vor der nächsten Prüfung). */
    data class Rejected(val minimumVersion: String?, val installed: String, val message: String?) : UpdateEvent
    data class SyncStarted(val pending: Int) : UpdateEvent
    data class SyncFinished(val pending: Int) : UpdateEvent
    data object ContinueWithoutSync : UpdateEvent
    data class Progress(val received: Long, val total: Long?) : UpdateEvent
    data object DownloadFinished : UpdateEvent
    data class Verified(val file: File, val canInstall: Boolean) : UpdateEvent
    data class Failure(val message: String, val retry: Boolean = true) : UpdateEvent
    data object PermissionGranted : UpdateEvent
    data object InstallStarted : UpdateEvent
    data object InstallConflict : UpdateEvent
    data object Dismissed : UpdateEvent
}

/** Zustandsautomat ohne Nebenwirkungen (Tests: AppUpdateTests). */
object AppUpdateMachine {
    fun reduce(state: UpdateState, event: UpdateEvent, signers: List<String> = emptyList()): UpdateState {
        val check = state.check
        return when (event) {
            UpdateEvent.CheckStarted -> if (state.isBusy) state else UpdateState.Checking
            is UpdateEvent.Checked -> when (event.check.status) {
                UpdateStatus.Recommended, UpdateStatus.Required -> when {
                    // Anderer Schlüssel: gleich erklären statt Download und Fehlschlag.
                    event.check.download.kind == "package" && !Signer.compatible(signers, event.check.download.signerSha256) ->
                        UpdateState.SignatureMismatch(event.check)
                    state.isBusy && check?.latestVersion == event.check.latestVersion -> state
                    else -> UpdateState.Available(event.check)
                }
                else -> if (state.isBusy) state else UpdateState.UpToDate(event.check)
            }
            is UpdateEvent.CheckFailed -> when {
                state is UpdateState.Checking -> UpdateState.Failed(null, event.message)
                else -> state
            }
            is UpdateEvent.Rejected -> if (check?.status == UpdateStatus.Required) state else UpdateState.Available(
                AppUpdateCheck(installedVersion = event.installed, serverVersion = "", latestVersion = null,
                    minimumVersion = event.minimumVersion ?: "", status = UpdateStatus.Required, message = event.message),
            )
            is UpdateEvent.SyncStarted -> check?.let { UpdateState.Syncing(it, event.pending) } ?: state
            is UpdateEvent.SyncFinished -> check?.let { if (event.pending > 0) UpdateState.UnsyncedChanges(it, event.pending) else UpdateState.Downloading(it, 0, it.download.size) } ?: state
            UpdateEvent.ContinueWithoutSync -> check?.let { UpdateState.Downloading(it, 0, it.download.size) } ?: state
            // Nur während des Downloads (verspätete Meldungen nach dem Abschluss ändern nichts mehr).
            is UpdateEvent.Progress -> (state as? UpdateState.Downloading)?.copy(received = event.received, total = event.total ?: state.total) ?: state
            UpdateEvent.DownloadFinished -> (state as? UpdateState.Downloading)?.let { UpdateState.Verifying(it.check) } ?: state
            is UpdateEvent.Verified -> check?.let { if (event.canInstall) UpdateState.ReadyToInstall(it, event.file) else UpdateState.NeedsInstallPermission(it, event.file) } ?: state
            is UpdateEvent.Failure -> UpdateState.Failed(check, event.message, event.retry)
            UpdateEvent.PermissionGranted -> (state as? UpdateState.NeedsInstallPermission)?.let { UpdateState.ReadyToInstall(it.check, it.file) } ?: state
            UpdateEvent.InstallStarted -> check?.let { UpdateState.Installing(it) } ?: state
            UpdateEvent.InstallConflict -> check?.let { UpdateState.SignatureMismatch(it) } ?: state
            UpdateEvent.Dismissed -> when {
                check?.status == UpdateStatus.Required -> check.let { UpdateState.Available(it) }
                else -> UpdateState.Idle
            }
        }
    }
}

val UpdateState.check: AppUpdateCheck?
    get() = when (this) {
        UpdateState.Idle, UpdateState.Checking -> null
        is UpdateState.UpToDate -> check
        is UpdateState.Available -> check
        is UpdateState.Syncing -> check
        is UpdateState.UnsyncedChanges -> check
        is UpdateState.Downloading -> check
        is UpdateState.Verifying -> check
        is UpdateState.NeedsInstallPermission -> check
        is UpdateState.ReadyToInstall -> check
        is UpdateState.Installing -> check
        is UpdateState.SignatureMismatch -> check
        is UpdateState.Failed -> check
    }

/** Ein Update wird gerade vorbereitet oder installiert (nicht durch eine neue Prüfung unterbrechen). */
val UpdateState.isBusy: Boolean
    get() = this is UpdateState.Syncing || this is UpdateState.Downloading || this is UpdateState.Verifying || this is UpdateState.Installing

/** Die App darf nicht mehr benutzt werden: Mindestversion unterschritten. */
val UpdateState.blocksApp: Boolean get() = check?.status == UpdateStatus.Required

class ChecksumMismatchException : IOException("Die heruntergeladene Datei ist beschädigt (Prüfsumme stimmt nicht). Bitte erneut versuchen.")

/**
 * Lädt das APK in [target] und prüft SHA-256. Ein abgebrochener Download wird mit `Range` fortgesetzt (`If-Range` mit der
 * Prüfsumme als ETag: hat der Server inzwischen ein anderes Paket, beginnt er von vorn). Bei falscher Prüfsumme wird die
 * Datei gelöscht.
 */
class ApkDownloader(private val http: OkHttpClient) {
    fun download(url: String, target: File, sha256: String, expectedSize: Long?, headers: Map<String, String> = emptyMap(), onProgress: (Long, Long?) -> Unit = { _, _ -> }): File {
        target.parentFile?.mkdirs()
        val expected = sha256.trim().lowercase()
        if (target.exists() && expectedSize != null && target.length() == expectedSize && sha256Of(target) == expected) {
            onProgress(expectedSize, expectedSize)
            return target
        }
        if (target.exists() && expectedSize != null && target.length() > expectedSize) target.delete()
        val offset = if (target.exists()) target.length() else 0L
        val request = Request.Builder().url(url).header("Accept", "application/vnd.android.package-archive")
            .apply {
                headers.forEach { (k, v) -> header(k, v) }
                if (offset > 0) {
                    header("Range", "bytes=$offset-")
                    header("If-Range", "\"$expected\"")
                }
            }.build()
        http.newCall(request).execute().use { response ->
            if (response.code != 200 && response.code != 206) {
                if (response.code == 416) target.delete()
                throw IOException("Download fehlgeschlagen (Status ${response.code}).")
            }
            val append = response.code == 206 && offset > 0
            var received = if (append) offset else 0L
            val total = response.body.contentLength().takeIf { it >= 0 }?.let { if (append) it + offset else it } ?: expectedSize
            RandomAccessFile(target, "rw").use { file ->
                if (!append) file.setLength(0)
                file.seek(received)
                response.body.byteStream().use { input ->
                    val buffer = ByteArray(64 * 1024)
                    var lastReport = 0L
                    while (true) {
                        val read = input.read(buffer)
                        if (read < 0) break
                        file.write(buffer, 0, read)
                        received += read
                        if (received - lastReport >= 256 * 1024 || received == total) {
                            onProgress(received, total)
                            lastReport = received
                        }
                    }
                }
            }
            onProgress(received, total)
        }
        if (sha256Of(target) != expected) {
            target.delete()
            throw ChecksumMismatchException()
        }
        return target
    }

    companion object {
        fun sha256Of(file: File): String {
            val digest = MessageDigest.getInstance("SHA-256")
            file.inputStream().use { input ->
                val buffer = ByteArray(64 * 1024)
                while (true) {
                    val read = input.read(buffer)
                    if (read < 0) break
                    digest.update(buffer, 0, read)
                }
            }
            return digest.digest().joinToString("") { "%02x".format(it) }
        }
    }
}
