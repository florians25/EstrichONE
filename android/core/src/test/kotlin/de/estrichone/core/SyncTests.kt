package de.estrichone.core

import kotlinx.coroutines.runBlocking
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertNotNull
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.DisplayName
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.assertThrows

class SyncTests {
    private fun demo(): Triple<EstrichOneApi, LocalStore, BauakteService> {
        val server = DemoServer { id -> if (id == DemoData.PDF) "%PDF-1.4".toByteArray() else byteArrayOf(1, 2, 3) }
        val api = EstrichOneApi("https://demo.estrichone.invalid", MemorySessionStore(), SoftwareDeviceKeys(), server.client())
        val store = LocalStore(MemoryStorage())
        return Triple(api, store, BauakteService(store, MemoryFileVault()) { DemoData.USER_ID })
    }

    @Test
    @DisplayName("Demoserver: Anmelden, Erstabgleich liefert Bauakten, Kunden, Schema und Inhalte")
    fun firstSync() = runBlocking {
        val (api, store, service) = demo()
        api.signIn("m.huber", "demo", "Test")
        api.bindDevice(DeviceKind.Personal, null, DeviceDescription("Test", null, null, null))
        val engine = SyncEngine(store, ApiSyncTransport(api), MemoryFileVault())

        assertEquals(SyncOutcome.Completed, engine.synchronize(includeScope = true))
        val list = service.bauakten()
        assertEquals(6, list.size)
        assertEquals(BauakteStatus.InProgress, list.first().status)
        val efh = service.bauakte(DemoData.EFH)!!
        assertEquals("Familie Schneider", service.contact(efh.customerId)!!.name)
        assertEquals(listOf("EFH", "142,5 m²", "CT", "65 mm", "Fußbodenheizung"), service.badges(efh))
        assertTrue(service.notes(efh.id).first().isPinned)
        assertEquals(5, service.photos(efh.id).size)
        assertEquals(11, service.rooms(efh.id).size)
        assertEquals("EG", service.rooms(efh.id).first().floor)
        assertEquals(PartyRole.SiteManager, service.parties(efh.id).first().role)
        assertEquals(WorkItemKind.Defect, service.tasks(efh.id).first().kind)
        assertTrue(store.summary().isSettled)
    }

    @Test
    @DisplayName("Offline angelegt: Bauakte mit neuem Kunden, Notiz und Foto – beim Abgleich übertragen, Nummer vom Server")
    fun offlineCreateThenSync() = runBlocking {
        val (api, store, service) = demo()
        api.signIn("m.huber", "demo", "Test")
        val files = MemoryFileVault()
        val local = BauakteService(store, files) { DemoData.USER_ID }
        val id = local.createBauakte("Garage Huber", "Hauptstr. 1", "84028", "Landshut", BauakteTemplate.NewBuild,
            newCustomer = NewCustomer("Huber, Josef", phone = "0171 1"))
        local.addNote(id, "Schlüssel beim Nachbarn", pinned = true)
        val fileId = local.addFile(id, "photo", byteArrayOf(9, 9, 9), "Foto.jpg", "image/jpeg", caption = "Einfahrt")

        assertNull(local.bauakte(id)!!.number)
        assertEquals("Neu", local.bauakte(id)!!.numberText)
        val ops = store.backend.outbox().map { it.type }
        assertEquals(listOf("contact.create", "bauakte.create", "note.create", "file.announce"), ops)
        assertEquals(1, store.summary().pendingUploads)

        val engine = SyncEngine(store, ApiSyncTransport(api), files)
        assertEquals(SyncOutcome.Completed, engine.synchronize())
        assertNotNull(local.bauakte(id)!!.number)
        assertEquals(SyncState.Synced, local.bauakte(id)!!.syncState)
        assertTrue(local.notes(id).single().isPinned)
        assertTrue(local.file(fileId)!!.isStoredOnServer)
        assertTrue(store.summary().isSettled)
        assertNotNull(service)
    }

    @Test
    @DisplayName("Feldweise Änderungen: ungesendete Änderungen werden zusammengefasst, ältester base-Wert bleibt")
    fun mergeUpdates() {
        val store = LocalStore(MemoryStorage())
        store.applyPage(listOf(SyncChange(EntityTypes.BAUAKTE, "b1", false, json("id" to "b1", "title" to "Alt", "city" to "Landshut", "version" to 4))), "c1", null)
        val service = BauakteService(store, MemoryFileVault()) { "u" }
        service.updateBauakte("b1", mapOf("city" to "Ergolding"))
        service.updateBauakte("b1", mapOf("city" to "Altdorf", "title" to "Neu"))

        val op = store.backend.outbox().single()
        assertEquals("bauakte.update", op.type)
        assertEquals(4L, op.baseVersion)
        assertEquals(JsonPrimitive("Altdorf"), op.payload.obj("changes")!!["city"])
        assertEquals(JsonPrimitive("Landshut"), op.payload.obj("base")!!["city"])
        assertEquals(JsonPrimitive("Alt"), op.payload.obj("base")!!["title"])
        assertEquals("Altdorf", service.bauakte("b1")!!.city)
        assertEquals(SyncState.Pending, service.bauakte("b1")!!.syncState)

        // Eine Feedseite überschreibt ungesendete lokale Änderungen nicht.
        store.applyPage(listOf(SyncChange(EntityTypes.BAUAKTE, "b1", false, json("id" to "b1", "title" to "Server", "version" to 5))), "c2", null)
        assertEquals("Neu", service.bauakte("b1")!!.title)
    }

    @Test
    @DisplayName("Abgelehnte Operation wird markiert, RETRY_LATER bleibt in der Warteschlange")
    fun rejectedAndRetry() {
        val store = LocalStore(MemoryStorage())
        val service = BauakteService(store, MemoryFileVault()) { "u" }
        val a = service.addTask(null, WorkItemKind.Task, "Eins")
        service.addTask(null, WorkItemKind.Task, "Zwei")
        val sent = store.takeOperations(10)
        val retry = store.applyResults(listOf(
            PushResult(sent[0].opId, "rejected", code = "VALIDATION_FAILED", message = "Titel zu lang"),
            PushResult(sent[1].opId, "rejected", code = "RETRY_LATER"),
        ), sent)
        assertTrue(retry)
        assertEquals(1, store.rejected().size)
        assertEquals(SyncState.Problem, store.entity(EntityTypes.WORK_ITEM, a)!!.syncState)
        assertEquals(1, store.summary().pendingOperations)
        assertTrue(store.summary().hasProblems)
    }

    @Test
    @DisplayName("Sichtbarkeit: nicht mehr sichtbare Bauakten verschwinden samt Inhalt")
    fun scopeRemovesBauakten() {
        val store = LocalStore(MemoryStorage())
        store.applyPage(listOf(
            SyncChange(EntityTypes.BAUAKTE, "b1", false, json("id" to "b1", "title" to "A")),
            SyncChange(EntityTypes.BAUAKTE, "b2", false, json("id" to "b2", "title" to "B")),
            SyncChange(EntityTypes.NOTE, "n1", false, json("id" to "n1", "bauakteId" to "b2", "text" to "x")),
        ), "c", listOf("b1", "b2"))
        store.applyPage(emptyList(), "d", listOf("b1"))
        assertEquals(setOf("b1"), store.knownBauakteIds())
        assertNull(store.entity(EntityTypes.NOTE, "n1"))
    }

    @Test
    @DisplayName("Status: Storno nur mit Begründung, setStatus mit baseStatus")
    fun statusChange() {
        val store = LocalStore(MemoryStorage())
        store.applyPage(listOf(SyncChange(EntityTypes.BAUAKTE, "b1", false, json("id" to "b1", "title" to "A", "status" to "planned"))), "c", null)
        val service = BauakteService(store, MemoryFileVault()) { "u" }
        assertThrows<IllegalArgumentException> { service.setStatus("b1", BauakteStatus.Cancelled) }
        service.setStatus("b1", BauakteStatus.InProgress)
        val op = store.backend.outbox().single()
        assertEquals("bauakte.setStatus", op.type)
        assertEquals("planned", op.payload.str("baseStatus"))
        assertEquals(BauakteStatus.InProgress, service.bauakte("b1")!!.status)
    }

    @Test
    @DisplayName("Protokolle: CM-Messung bewertet Belegreife, Erstbegehung verlangt Bemerkung bei Mangel")
    fun siteRecords() {
        val store = LocalStore(MemoryStorage())
        val service = BauakteService(store, MemoryFileVault()) { "u" }
        val cm = service.addCmMeasurement("b1", "Bad", ScreedBinder.Cement, heated = true, thicknessMm = 65, measured = 1.6)
        val inspection = Inspection(store.entity(EntityTypes.INSPECTION, cm)!!)
        assertEquals(true, inspection.isReady)
        assertEquals(1.8, inspection.limit)
        assertThrows<IllegalArgumentException> {
            service.addInitialInspection("b1", mapOf("access" to CheckResult.NotOk), emptyMap(), emptyMap(), null, null)
        }
        val diary = service.addDiary("b1", java.time.LocalDate.of(2026, 11, 6), "Estrich eingebracht", DiaryWeather.Sunny, 12, 3)
        assertEquals(DiaryWeather.Sunny, service.diary("b1").single { it.id == diary }.weather)
    }

    @Test
    @DisplayName("Benutzer-ID v7: Version und Variante, zeitlich sortierbar")
    fun uuidV7() {
        val a = Ids.v7(1_700_000_000_000)
        val b = Ids.v7(1_700_000_000_001)
        assertEquals('7', a[14])
        assertTrue(a[19] in "89ab")
        assertTrue(a < b)
        assertFalse(a.any { it.isUpperCase() })
        assertTrue(JsonObject(emptyMap()).compact().isEmpty())
    }
}
