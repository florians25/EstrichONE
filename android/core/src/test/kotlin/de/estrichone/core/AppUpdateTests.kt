package de.estrichone.core

import kotlinx.coroutines.runBlocking
import okhttp3.Interceptor
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Protocol
import okhttp3.Response
import okhttp3.ResponseBody.Companion.asResponseBody
import okhttp3.ResponseBody.Companion.toResponseBody
import okio.buffer
import org.junit.jupiter.api.Assertions.assertArrayEquals
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertInstanceOf
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.DisplayName
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.assertThrows
import org.junit.jupiter.api.io.TempDir
import java.io.File
import java.io.IOException
import java.security.MessageDigest
import kotlin.random.Random

class AppUpdateTests {
    private fun sha(data: ByteArray) = MessageDigest.getInstance("SHA-256").digest(data).joinToString("") { "%02x".format(it) }

    private fun check(status: UpdateStatus = UpdateStatus.Recommended, signer: String? = null) = AppUpdateCheck(
        installedVersion = "1.0.10", serverVersion = "1.0.12", latestVersion = "1.0.12", minimumVersion = "1.0.0", status = status,
        download = AppDownload("package", "/api/v1/app-updates/android/package", "ab", 100, 10012, signer, true),
    )

    @Test
    @DisplayName("Versionen werden wie auf dem Server verglichen")
    fun versions() {
        assertEquals(-1, AppVersions.compare("1.0.9", "1.0.10"))
        assertEquals(-1, AppVersions.compare("1.1.0-preview.2", "1.1.0"))
        assertEquals(1, AppVersions.compare("1.1.0-preview.10", "1.1.0-preview.9"))
        assertEquals(0, AppVersions.compare("v1.0.5+7", "1.0.5"))
        assertEquals(1, AppVersions.compare("2", "1.9.9"))
        assertTrue(AppVersions.isValid("1.0.12"))
        assertFalse(AppVersions.isValid("neu"))
    }

    @Test
    @DisplayName("Prüfung gedrosselt: Start nach 30 Minuten, Rückkehr nach 6 Stunden, von Hand immer")
    fun schedule() {
        val now = 10_000_000_000L
        assertTrue(UpdateSchedule.isDue(null, now, UpdateSchedule.Trigger.Resume))
        assertFalse(UpdateSchedule.isDue(now - 60_000, now, UpdateSchedule.Trigger.Start))
        assertTrue(UpdateSchedule.isDue(now - UpdateSchedule.ON_START_MS, now, UpdateSchedule.Trigger.Start))
        assertFalse(UpdateSchedule.isDue(now - UpdateSchedule.ON_START_MS, now, UpdateSchedule.Trigger.Resume))
        assertTrue(UpdateSchedule.isDue(now - 1000, now, UpdateSchedule.Trigger.Manual))
        // Uhr zurückgestellt: trotzdem prüfen.
        assertTrue(UpdateSchedule.isDue(now + 60_000, now, UpdateSchedule.Trigger.Resume))
    }

    @Test
    @DisplayName("„Neu in dieser Version“ nur nach einem Update")
    fun whatsNew() {
        assertNull(WhatsNew.since(null, "1.0.12"))
        assertNull(WhatsNew.since("1.0.12", "1.0.12"))
        assertNull(WhatsNew.since("1.0.13", "1.0.12"))
        assertEquals("1.0.10", WhatsNew.since("1.0.10", "1.0.12"))
    }

    @Test
    @DisplayName("Signatur: Doppelpunkte und Groß-/Kleinschreibung egal, unbekannt gilt als passend")
    fun signer() {
        assertTrue(Signer.compatible(listOf("AB:CD:EF"), "abcdef"))
        assertFalse(Signer.compatible(listOf("abcdef"), "123456"))
        assertTrue(Signer.compatible(emptyList(), "123456"))
        assertTrue(Signer.compatible(listOf("abcdef"), null))
    }

    @Test
    @DisplayName("Zustandsautomat: Hinweis, Abgleich vor dem Download, Fortschritt, Prüfung, Berechtigung, Installation")
    fun machine() {
        var state: UpdateState = UpdateState.Idle
        fun on(event: UpdateEvent, signers: List<String> = listOf("ab")) { state = AppUpdateMachine.reduce(state, event, signers) }

        on(UpdateEvent.CheckStarted)
        assertEquals(UpdateState.Checking, state)
        on(UpdateEvent.Checked(check(signer = "AB")))
        assertInstanceOf(UpdateState.Available::class.java, state)
        on(UpdateEvent.SyncStarted(3))
        assertEquals(3, (state as UpdateState.Syncing).pending)
        // Neue Prüfung während der Vorbereitung unterbricht nicht.
        on(UpdateEvent.CheckStarted)
        assertInstanceOf(UpdateState.Syncing::class.java, state)
        on(UpdateEvent.SyncFinished(1))
        assertEquals(1, (state as UpdateState.UnsyncedChanges).pending)
        on(UpdateEvent.ContinueWithoutSync)
        on(UpdateEvent.Progress(50, 100))
        assertEquals(0.5f, (state as UpdateState.Downloading).fraction)
        on(UpdateEvent.DownloadFinished)
        assertInstanceOf(UpdateState.Verifying::class.java, state)
        // Verspätete Fortschrittsmeldung nach dem Download ändert nichts.
        on(UpdateEvent.Progress(60, 100))
        assertInstanceOf(UpdateState.Verifying::class.java, state)
        val file = File("update.apk")
        on(UpdateEvent.Verified(file, canInstall = false))
        assertInstanceOf(UpdateState.NeedsInstallPermission::class.java, state)
        on(UpdateEvent.PermissionGranted)
        assertEquals(UpdateState.ReadyToInstall(check(signer = "AB"), file), state)
        on(UpdateEvent.InstallStarted)
        assertInstanceOf(UpdateState.Installing::class.java, state)
        on(UpdateEvent.InstallConflict)
        assertInstanceOf(UpdateState.SignatureMismatch::class.java, state)
    }

    @Test
    @DisplayName("Andere Signatur wird vor dem Download erkannt; Sperre lässt sich nicht wegklicken")
    fun mismatchAndRequired() {
        assertInstanceOf(UpdateState.SignatureMismatch::class.java,
            AppUpdateMachine.reduce(UpdateState.Checking, UpdateEvent.Checked(check(signer = "ff")), listOf("ab")))
        val required = AppUpdateMachine.reduce(UpdateState.Idle, UpdateEvent.Checked(check(UpdateStatus.Required)))
        assertTrue(required.blocksApp)
        assertTrue(AppUpdateMachine.reduce(required, UpdateEvent.Dismissed).blocksApp)
        assertEquals(UpdateState.Idle, AppUpdateMachine.reduce(UpdateState.Available(check()), UpdateEvent.Dismissed))
        // 426 vom Server ohne vorherige Prüfung: sofort sperren.
        val rejected = AppUpdateMachine.reduce(UpdateState.UpToDate(null), UpdateEvent.Rejected("1.0.12", "1.0.10", null))
        assertTrue(rejected.blocksApp)
        assertEquals("1.0.12", rejected.check?.minimumVersion)
        assertInstanceOf(UpdateState.UpToDate::class.java,
            AppUpdateMachine.reduce(UpdateState.Checking, UpdateEvent.Checked(check(UpdateStatus.Current))))
        assertInstanceOf(UpdateState.Failed::class.java, AppUpdateMachine.reduce(UpdateState.Checking, UpdateEvent.CheckFailed("offline")))
        assertInstanceOf(UpdateState.Available::class.java, AppUpdateMachine.reduce(UpdateState.Available(check()), UpdateEvent.CheckFailed("offline")))
    }

    /** Server, der das Paket mit Range/If-Range ausliefert; [failAfter] bricht die erste Antwort nach so vielen Bytes ab. */
    private class PackageServer(val data: ByteArray, var failAfter: Int? = null) : Interceptor {
        val ranges = mutableListOf<String?>()
        override fun intercept(chain: Interceptor.Chain): Response {
            val request = chain.request()
            val range = request.header("Range")
            ranges += range
            val etag = "\"${MessageDigest.getInstance("SHA-256").digest(data).joinToString("") { "%02x".format(it) }}\""
            val start = range?.takeIf { request.header("If-Range") == etag }?.removePrefix("bytes=")?.removeSuffix("-")?.toInt() ?: 0
            val limit = failAfter?.also { failAfter = null }
            val slice = data.copyOfRange(start, if (limit != null) minOf(data.size, start + limit) else data.size)
            val builder = Response.Builder().request(request).protocol(Protocol.HTTP_1_1).code(if (start > 0) 206 else 200).message("OK")
                .header("ETag", etag)
            if (limit != null) {
                // Verbindung bricht ab: Inhalt kürzer als angekündigt.
                val source = object : okio.Source {
                    val buffer = okio.Buffer().write(slice)
                    override fun read(sink: okio.Buffer, byteCount: Long): Long {
                        if (buffer.size == 0L) throw IOException("Verbindung abgebrochen")
                        return buffer.read(sink, byteCount)
                    }
                    override fun timeout() = okio.Timeout.NONE
                    override fun close() {}
                }
                return builder.body(source.buffer().asResponseBody("application/vnd.android.package-archive".toMediaType(), (data.size - start).toLong())).build()
            }
            return builder.body(slice.toResponseBody("application/vnd.android.package-archive".toMediaType())).build()
        }
    }

    @Test
    @DisplayName("Download wird nach Abbruch fortgesetzt und per SHA-256 geprüft")
    fun resumableDownload(@TempDir dir: File) {
        val data = Random(7).nextBytes(1_000_000)
        val server = PackageServer(data, failAfter = 300_000)
        val downloader = ApkDownloader(OkHttpClient.Builder().addInterceptor(server).build())
        val target = File(dir, "update.apk")
        assertThrows<IOException> { downloader.download("https://server/app.apk", target, sha(data), data.size.toLong()) }
        assertEquals(300_000L, target.length())
        val progress = mutableListOf<Long>()
        downloader.download("https://server/app.apk", target, sha(data), data.size.toLong()) { received, _ -> progress += received }
        assertEquals("bytes=300000-", server.ranges.last())
        assertArrayEquals(data, target.readBytes())
        assertEquals(data.size.toLong(), progress.last())
        // Bereits vollständig und geprüft: kein erneuter Download.
        val requests = server.ranges.size
        downloader.download("https://server/app.apk", target, sha(data), data.size.toLong())
        assertEquals(requests, server.ranges.size)
    }

    @Test
    @DisplayName("Falsche Prüfsumme: Datei wird verworfen")
    fun checksumMismatch(@TempDir dir: File) {
        val data = Random(3).nextBytes(10_000)
        val downloader = ApkDownloader(OkHttpClient.Builder().addInterceptor(PackageServer(data)).build())
        val target = File(dir, "update.apk")
        assertThrows<ChecksumMismatchException> { downloader.download("https://server/app.apk", target, sha(byteArrayOf(1)), data.size.toLong()) }
        assertFalse(target.exists())
    }

    @Test
    @DisplayName("Demoserver beantwortet die Update-Prüfung; 426 meldet die Mindestversion")
    fun demoAndRejection() = runBlocking {
        val api = EstrichOneApi("https://demo.estrichone.invalid", MemorySessionStore(), SoftwareDeviceKeys(), DemoServer().client())
        val result = api.checkAppUpdate("1.0.0")
        assertEquals(UpdateStatus.Current, result.status)

        val rejecting = Interceptor { chain ->
            Response.Builder().request(chain.request()).protocol(Protocol.HTTP_1_1).code(426).message("Upgrade Required")
                .body("""{"title":"App-Update erforderlich","detail":"Bitte aktualisieren","code":"CLIENT_UPDATE_REQUIRED","minimumVersion":"1.0.12"}"""
                    .toResponseBody("application/problem+json".toMediaType())).build()
        }
        var reported: String? = null
        EstrichOneApi.updateRequired = { _, minimum -> reported = minimum }
        try {
            val old = EstrichOneApi("https://server", MemorySessionStore(), SoftwareDeviceKeys(), OkHttpClient.Builder().addInterceptor(rejecting).build())
            val error = assertThrows<ApiException> { runBlocking { old.serverInfo() } }
            assertTrue(error.isUpdateRequired)
            assertEquals("1.0.12", reported)
        } finally {
            EstrichOneApi.updateRequired = null
        }
    }
}

class StorageSchemaTests {
    @Test
    @DisplayName("Datenbankschema: fortlaufend, nur ergänzend, ältere Datenbanken bekommen nur Fehlendes")
    fun schemaIsAdditive() {
        assertTrue(StorageSchema.version >= 1)
        StorageSchema.migrations.flatten().forEach { assertFalse(StorageSchema.isDestructive(it), it) }
        // Datenbanken vor der Versionierung (user_version 0) haben die Tabellen schon: Version 1 muss idempotent sein.
        StorageSchema.migrations.first().forEach { assertTrue(" IF NOT EXISTS " in it, it) }
        assertEquals(StorageSchema.migrations.flatten(), StorageSchema.pending(0))
        assertTrue(StorageSchema.pending(StorageSchema.version).isEmpty())
        // Zurückgestufte App: neuere Datenbank bleibt unangetastet.
        assertTrue(StorageSchema.pending(StorageSchema.version + 3).isEmpty())
        assertTrue(StorageSchema.isDestructive("DROP TABLE outbox"))
        assertTrue(StorageSchema.isDestructive("alter table entities drop column json"))
    }
}
