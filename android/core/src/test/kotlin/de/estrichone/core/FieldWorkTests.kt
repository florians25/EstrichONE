package de.estrichone.core

import kotlinx.serialization.json.JsonObject
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.DisplayName
import org.junit.jupiter.api.Test
import java.time.LocalDate
import java.time.LocalTime

class FieldWorkTests {
    private val akte = "0193a1b2-0000-7000-8000-00000000000a"
    private var counter = 0

    private fun service(store: LocalStore = LocalStore(MemoryStorage()), files: FileVault = MemoryFileVault()) = BauakteService(store, files) { DemoData.USER_ID }

    private fun file(vararg fields: Pair<String, Any?>): FileItem {
        counter++
        return FileItem(LocalEntity(EntityTypes.FILE, "0193a1b2-0000-7000-8000-%012d".format(counter), JsonObject(fields.associate { it.first to toJson(it.second) }), SyncState.Synced))
    }

    private fun job(id: String, day: String, start: String?, end: String? = null, bauakte: String = akte) =
        PlannedJob(id = id, day = day, startTime = start, endTime = end, activity = "Estrich", bauakteId = bauakte, bauakteTitle = "B $id")

    @Test
    @DisplayName("Übertragungsstand in Worten: „3 Fotos warten auf Übertragung“, gemischt mit „und“, Einzahl mit „wartet“")
    fun waitingText() {
        val store = LocalStore(MemoryStorage())
        val service = service(store)
        assertNull(TransferStatus.of(store).waitingText)
        assertTrue(TransferStatus.of(store).isSettled)

        service.addFile(null, "photo", byteArrayOf(1), "Foto.jpg", "image/jpeg")
        assertEquals("1 Foto wartet auf Übertragung", TransferStatus.of(store).waitingText)
        service.addFile(null, "photo", byteArrayOf(2), "Foto.jpg", "image/jpeg")
        service.addFile(null, "photo", byteArrayOf(3), "Foto.jpg", "image/jpeg")
        assertEquals("3 Fotos warten auf Übertragung", TransferStatus.of(store).waitingText)

        service.addFile(null, "document", byteArrayOf(4), "Plan.pdf", "application/pdf")
        service.addTask(null, WorkItemKind.Defect, "Riss an der Tür")
        // Die Ankündigung einer Datei zählt nicht als eigener Eintrag: gezählt wird die Datei.
        assertEquals("3 Fotos, 1 Dokument und 1 Eintrag warten auf Übertragung", TransferStatus.of(store).waitingText)
        assertFalse(TransferStatus.of(store).isSettled)
    }

    @Test
    @DisplayName("Fehlgeschlagene Uploads: verständlicher Grund, „Erneut senden“ reiht ein, fehlende Datei lässt sich nicht wiederholen")
    fun failedUploads() {
        val store = LocalStore(MemoryStorage())
        val service = service(store)
        val first = service.addFile(akte, "photo", byteArrayOf(1), "Foto.jpg", "image/jpeg", caption = "Vorher, Bad EG")
        val second = service.addFile(akte, "photo", byteArrayOf(2), "Foto.jpg", "image/jpeg")
        store.updateUpload(first, UploadState.Failed, 0, "FILE_TOO_LARGE")
        store.updateUpload(second, UploadState.Failed, 0, "LOCAL_FILE_MISSING")

        val status = TransferStatus.of(store)
        assertEquals("2 Fotos konnten nicht übertragen werden", status.failedText)
        assertTrue(status.hasProblems)
        val item = status.failed.first { it.fileId == first }
        assertEquals("Vorher, Bad EG", item.name)
        assertEquals(akte, item.bauakteId)
        assertTrue(item.canRetry)
        assertFalse(status.failed.first { it.fileId == second }.canRetry)
        assertEquals("Die Datei ist zu groß für den Server.", TransferStatus.failureText("FILE_TOO_LARGE"))
        assertTrue(TransferStatus.failureText("SOMETHING").contains("SOMETHING"))

        assertTrue(store.retryUpload(first))
        assertFalse(store.retryUpload(second))
        assertFalse(store.retryUpload(first))
        val after = TransferStatus.of(store)
        assertEquals("1 Foto konnte nicht übertragen werden", after.failedText)
        assertEquals(1, after.waiting.size)
        assertEquals(1, after.of(akte).waiting.size)
        assertTrue(after.of("0193a1b2-0000-7000-8000-00000000000b").isSettled)
    }

    @Test
    @DisplayName("Vorab laden: Bauakten der Einsätze von heute bis in 6 Tagen, in Reihenfolge, ohne Dubletten")
    fun offlineTargets() {
        val today = LocalDate.of(2026, 11, 24)
        val other = "0193A1B2-0000-7000-8000-00000000000B"
        val plan = MyPlanning("2026-11-24", "2026-11-30", listOf(
            job("3", "2026-11-26", "07:00", bauakte = other), job("1", "2026-11-24", "13:00"), job("2", "2026-11-24", "07:00", bauakte = other),
            job("4", "2026-11-23", "07:00", bauakte = "gestern"), job("5", "2026-12-02", "07:00", bauakte = "spaeter"),
        ))
        assertEquals(listOf(other.lowercase(), akte), OfflinePack.targets(plan, today))
        assertEquals(emptyList<String>(), OfflinePack.targets(null, today))
    }

    @Test
    @DisplayName("Vorab laden: Dokumente vollständig bis 25 MB, von Fotos die Vorschau; Geladenes nicht erneut, Überholtes entfernen")
    fun offlinePlan() {
        val plan = file("kind" to "document", "state" to "stored", "sizeBytes" to 2_000_000, "originalName" to "Plan.pdf")
        val huge = file("kind" to "document", "state" to "stored", "sizeBytes" to 80_000_000, "originalName" to "Film.pdf")
        val pending = file("kind" to "document", "state" to "announced", "sizeBytes" to 1000, "originalName" to "Scan.pdf")
        val photo = file("kind" to "photo", "state" to "stored", "sizeBytes" to 900_000)
        val wanted = OfflinePack.wanted(listOf(plan, huge, pending, photo))
        assertEquals(listOf(plan.id, OfflinePack.previewKey(photo.id)), wanted.map { it.key })
        assertTrue(wanted.last().preview)

        val first = OfflinePack.plan(wanted, emptyMap())
        assertEquals(2, first.load.size)
        assertTrue(first.evict.isEmpty())
        val second = OfflinePack.plan(wanted, mapOf(plan.id to 2_000_000L, "alt" to 5L))
        assertEquals(listOf(OfflinePack.previewKey(photo.id)), second.load.map { it.key })
        assertEquals(listOf("alt"), second.evict)

        // Obergrenze: Was nicht mehr passt, bleibt aus, kleinere Dateien danach kommen noch mit.
        val big = (1..20).map { OfflinePack.Wanted("d$it", "d$it", false, 24L * 1024 * 1024) } + OfflinePack.Wanted("klein", "klein", false, 1000)
        val limited = OfflinePack.plan(big, emptyMap())
        assertEquals(16, limited.load.count { it.key.startsWith("d") })
        assertEquals("klein", limited.load.last().key)
    }

    @Test
    @DisplayName("Anzeige je Bauakte: „Auf dem Gerät verfügbar“ mit Dokumentenstand; eigene, noch nicht übertragene Dateien zählen als vorhanden")
    fun offlineState() {
        val a = file("kind" to "document", "state" to "stored", "sizeBytes" to 10)
        val b = file("kind" to "document", "state" to "stored", "sizeBytes" to 10)
        val own = file("kind" to "document", "state" to "announced", "sizeBytes" to 10)
        assertEquals("Auf dem Gerät verfügbar", OfflinePack.state(emptyList(), emptyMap()).text)
        val partial = OfflinePack.state(listOf(a, b, own), mapOf(a.id to 10L))
        assertFalse(partial.isComplete)
        assertEquals("Auf dem Gerät verfügbar, 2 von 3 Dokumenten geladen", partial.text)
        assertEquals("Auf dem Gerät verfügbar, mit 3 Dokumenten", OfflinePack.state(listOf(a, b, own), mapOf(a.id to 10L, b.id to 10L)).text)
        assertEquals("Auf dem Gerät verfügbar, mit 1 Dokument", OfflinePack.state(listOf(a), emptyMap()) { true }.text)

        // Symbol oben in der Bauakte: Zustand und Erklärung beim Antippen.
        assertEquals(OfflinePack.State.Kind.Complete, OfflinePack.state(emptyList(), emptyMap()).kind)
        assertEquals("Auf dem Gerät verfügbar", OfflinePack.state(emptyList(), emptyMap()).title)
        assertEquals(OfflinePack.State.Kind.Loading, partial.kind)
        assertEquals("Dokumente werden geladen", partial.title)
        assertTrue(partial.detail.contains("2 von 3 Dokumenten"), partial.detail)
        val dataOnly = OfflinePack.state(listOf(a, b), emptyMap())
        assertEquals(OfflinePack.State.Kind.DataOnly, dataOnly.kind)
        assertEquals("Nur Daten auf dem Gerät", dataOnly.title)
        assertTrue(dataOnly.detail.contains("Die 2 Dokumente sind noch nicht geladen"), dataOnly.detail)

        val store = LocalStore(MemoryStorage())
        OfflinePack.remember(store, mapOf("x" to 7L))
        assertEquals(mapOf("x" to 7L), OfflinePack.loaded(store))
    }

    @Test
    @DisplayName("Serienfotos: Art und Bereich ergeben die Beschriftung, Räume als Vorschläge, Merken über Text")
    fun photoSeries() {
        assertNull(PhotoSeries().caption)
        assertEquals("Vorher, Bad EG", PhotoSeries(PhotoKind.Before, " Bad EG ").caption)
        assertEquals("Mangel", PhotoSeries(PhotoKind.Defect, " ").caption)
        assertEquals("Flur", PhotoSeries(null, "Flur").caption)
        val series = PhotoSeries(PhotoKind.Laying, "Wohnen EG")
        assertEquals(series, PhotoSeries.decode(series.encode()))
        assertEquals(PhotoSeries(), PhotoSeries.decode(null))
        assertEquals(PhotoSeries(), PhotoSeries.decode(PhotoSeries().encode()))

        fun room(name: String, floor: String?) = Room(LocalEntity(EntityTypes.ROOM, "r-$name-$floor", JsonObject(mapOf("name" to toJson(name), "floor" to toJson(floor))), SyncState.Synced))
        assertEquals(listOf("Bad EG", "Flur"), PhotoSeries.areas(listOf(room("Bad", "EG"), room("Flur", null), room("Bad", "EG"))))
    }

    @Test
    @DisplayName("Entwürfe: bei jeder Eingabe gesichert, je Formular und Bauakte getrennt, leer hinterlässt nichts, nach dem Speichern weg")
    fun drafts() {
        val backend = MemoryStorage()
        val drafts = Drafts(backend)
        assertNull(drafts.load(Drafts.DIARY, akte))
        drafts.save(Drafts.DIARY, akte, mapOf("text" to "Estrich im EG eingebracht", "workers" to "4", "start" to ""))
        // Neustart der App: neue Instanz über derselben Datenbank.
        assertEquals(mapOf("text" to "Estrich im EG eingebracht", "workers" to "4"), Drafts(backend).load(Drafts.DIARY, akte.uppercase()))
        assertNull(drafts.load(Drafts.DIARY, null))
        assertNull(drafts.load(Drafts.NOTE, akte))
        drafts.save(Drafts.DIARY, akte, mapOf("text" to " "))
        assertNull(drafts.load(Drafts.DIARY, akte))
        drafts.save(Drafts.TASK, null, mapOf("title" to "Riss"))
        drafts.clear(Drafts.TASK, null)
        assertNull(drafts.load(Drafts.TASK, null))
    }

    @Test
    @DisplayName("Ankunft und Abschluss: Uhrzeiten je Einsatz und Tag auf dem Gerät, Vorschlag für das Bautagebuch")
    fun siteVisit() {
        val backend = MemoryStorage()
        assertNull(SiteVisit.load(backend, "e1", "2026-11-24").text)
        val arrived = SiteVisit(arrived = SiteVisit.clock(LocalTime.of(7, 5)))
        SiteVisit.save(backend, "E1", "2026-11-24", arrived)
        val loaded = SiteVisit.load(backend, "e1", "2026-11-24")
        assertEquals("Angekommen 07:05 Uhr", loaded.text)
        assertTrue(loaded.isRunning)
        assertEquals(SiteVisit(), SiteVisit.load(backend, "e1", "2026-11-25"))
        val done = loaded.copy(finished = "15:10")
        assertFalse(done.isRunning)
        assertEquals("07:05 bis 15:10 Uhr", done.text)
        assertEquals("Ankunft 07:05 Uhr. Abschluss 15:10 Uhr. Estrich einbringen.", done.diaryText("Estrich einbringen"))
        assertEquals("", SiteVisit().diaryText(null))
        SiteVisit.save(backend, "e1", "2026-11-24", SiteVisit())
        assertEquals(SiteVisit(), SiteVisit.load(backend, "e1", "2026-11-24"))
    }

    @Test
    @DisplayName("Schnellzugriff: Bauakte des laufenden, sonst des nächsten Einsatzes von heute; ohne Einsatz Baustelle wählen")
    fun shortcutTarget() {
        val today = LocalDate.of(2026, 11, 24)
        val second = "0193a1b2-0000-7000-8000-00000000000b"
        val plan = MyPlanning("2026-11-24", "2026-11-30", listOf(
            job("1", "2026-11-24", "07:00", "11:00"), job("2", "2026-11-24", "13:00", "16:00", bauakte = second), job("3", "2026-11-25", "07:00", bauakte = "morgen"),
        ))
        assertEquals(akte, QuickShortcut.target(plan, today, LocalTime.of(9, 41)))
        assertEquals(second, QuickShortcut.target(plan, today, LocalTime.of(11, 30)))
        assertEquals(second, QuickShortcut.target(plan, today, LocalTime.of(18, 0)))
        assertNull(QuickShortcut.target(plan, today.plusDays(3), LocalTime.of(9, 0)))
        assertNull(QuickShortcut.target(null, today, LocalTime.of(9, 0)))
    }

    @Test
    @DisplayName("Abnahme vor dem Abschluss: Name, Vorbehalte und Unterschriften als Pflicht; bei Verweigerung keine Kundenunterschrift nötig")
    fun acceptanceCheck() {
        assertEquals(emptyList<String>(), ProtocolCheck.acceptance(AcceptanceResult.Accepted, "Schneider", "Huber", "", true, true))
        assertEquals(listOf("Name des Kunden bzw. Unterzeichners fehlt.", "Unterschrift des Kunden fehlt.", "Unterschrift des Auftragnehmers fehlt."),
            ProtocolCheck.acceptance(AcceptanceResult.Accepted, " ", "Huber", "", false, false))
        assertEquals(listOf("Vorbehalte fehlen."), ProtocolCheck.acceptance(AcceptanceResult.WithReservations, "Schneider", "Huber", "", true, true))
        assertEquals(listOf("Gründe der Verweigerung fehlen."), ProtocolCheck.acceptance(AcceptanceResult.Refused, "Schneider", "Huber", "", false, true))
    }

    @Test
    @DisplayName("Erstbegehung und CM-Messung vor dem Abschluss: offene Prüfpunkte, fehlende Bemerkungen, Zusammenfassung")
    fun inspectionCheck() {
        val keys = listOf("ebenheit" to "Ebenheit", "feuchte" to "Feuchte", "randstreifen" to "Randstreifen")
        assertEquals(listOf("3 Prüfpunkte sind noch nicht bewertet."), ProtocolCheck.inspection(keys, emptyMap(), emptyMap()))
        val checks = mapOf("ebenheit" to CheckResult.Ok, "feuchte" to CheckResult.NotOk)
        assertEquals(listOf("Prüfpunkt „Randstreifen“ ist noch nicht bewertet.", "Bemerkung zu „Feuchte“ fehlt."), ProtocolCheck.inspection(keys, checks, emptyMap()))
        val all = checks + ("randstreifen" to CheckResult.NotApplicable)
        assertEquals(emptyList<String>(), ProtocolCheck.inspection(keys, all, mapOf("feuchte" to "Estrich noch feucht")))
        assertEquals("1 in Ordnung, 1 Mangel, 1 entfallen", ProtocolCheck.inspectionSummary(all))
        assertEquals("2 in Ordnung", ProtocolCheck.inspectionSummary(mapOf("a" to CheckResult.Ok, "b" to CheckResult.Ok)))

        assertEquals(listOf("Messort (Raum) fehlt.", "Messwert fehlt."), ProtocolCheck.cm("", null))
        assertEquals(listOf("Der Messwert muss zwischen 0 und 20 CM-% liegen."), ProtocolCheck.cm("Bad", 42.0))
        assertEquals(emptyList<String>(), ProtocolCheck.cm("Bad", 1.8))
    }
}
