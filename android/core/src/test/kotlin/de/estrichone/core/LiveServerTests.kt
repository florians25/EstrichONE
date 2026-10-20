package de.estrichone.core

import kotlinx.coroutines.runBlocking
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNotNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Assumptions.assumeTrue
import org.junit.jupiter.api.DisplayName
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.assertThrows

/**
 * Gegen einen echten EstrichONE-Server (scripts/test-core-against-server.sh). Ohne ESTRICHONE_LIVE_SERVER übersprungen;
 * das Skript prüft, dass sie wirklich gelaufen sind.
 */
class LiveServerTests {
    private val server = System.getenv("ESTRICHONE_LIVE_SERVER")
    private val login = System.getenv("ESTRICHONE_LIVE_LOGIN") ?: "admin"
    private val password = System.getenv("ESTRICHONE_LIVE_PASSWORD") ?: ""
    private val phone = DeviceDescription("Testtelefon", "Pixel Test", "16", "0.1.0")

    @Test
    @DisplayName("Echter Server: Anmelden, Gerät binden, Bauakte dokumentieren, Erneuern mit Gerätesignatur")
    fun liveSignInBindDocumentRefresh() = runBlocking {
        assumeTrue(server != null, "ESTRICHONE_LIVE_SERVER nicht gesetzt")
        val store = MemorySessionStore()
        val keys = SoftwareDeviceKeys()
        val api = EstrichOneApi(server!!, store, keys)

        val info = api.serverInfo()
        api.signIn(login, password, "Testtelefon")
        val device = api.bindDevice(DeviceKind.Personal, null, phone)
        val created = api.createBauakte(CreateBauakteRequest(EstrichOneApi.newId(), "Android-Test ${System.nanoTime()}", city = "Landshut"))
        val note = api.addNote(created.id, "Fotos folgen")
        val task = api.addWorkItem(created.id, WorkItemKind.Defect, "Randdämmstreifen fehlt")
        val done = api.setWorkItemStatus(task.id, WorkItemStatus.Done)
        val found = api.bauakten(search = created.title)

        assertEquals("EstrichONE", info.product)
        assertEquals(DeviceStatus.Active, device.status)
        assertEquals(DeviceStatus.Active, api.myDevice().status)
        assertEquals("Fotos folgen", note.text)
        assertEquals(WorkItemStatus.Done, done.status)
        assertEquals(created.id, found.items.single().id)
        assertTrue(api.hints(created.id).isNotEmpty())

        // Neuer Start: nur Refresh-Token und Geräteschlüssel – die Signatur muss der Server akzeptieren.
        val restarted = EstrichOneApi(server, store, keys)
        assertTrue(restarted.restore())
        assertEquals(created.title, restarted.bauakte(created.id).title)
        assertEquals(1, restarted.notes(created.id).size)

        // Mit einem fremden Schlüssel schlägt das Erneuern fehl und die Anmeldung wird verworfen.
        val stolen = EstrichOneApi(server, MemorySessionStore().also { it.refreshToken = store.refreshToken; it.deviceId = store.deviceId }, SoftwareDeviceKeys())
        assertEquals(false, stolen.restore())
    }

    @Test
    @DisplayName("Echter Server: falsches Passwort und Problem melden")
    fun liveWrongPasswordAndReportProblem() = runBlocking {
        assumeTrue(server != null, "ESTRICHONE_LIVE_SERVER nicht gesetzt")
        val api = EstrichOneApi(server!!, MemorySessionStore(), SoftwareDeviceKeys())

        val error = assertThrows<ApiException> { runBlocking { api.signIn(login, "falsch-falsch-falsch", "Testtelefon") } }
        api.signIn(login, password, "Testtelefon")
        api.bindDevice(DeviceKind.Personal, null, phone)
        val ticket = api.reportProblem("Kamera startet nicht", null, blocked = false, appVersion = "0.1.0", osVersion = "16")

        assertEquals(401, error.status)
        assertEquals("Anmeldename oder Passwort ist nicht korrekt.", error.message)
        assertNotNull(ticket.summary.number)
    }
}
