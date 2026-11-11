package de.estrichone.core

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.DisplayName
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.assertThrows
import java.time.Instant

@DisplayName("Helpdesk: Problem melden offline, Diagnose, Verlauf")
class HelpdeskTests {
    private val report = TicketDiagnosticsReport(
        appVersion = "1.0.13", build = "213", osVersion = "14", deviceModel = "Galaxy Tab Active5", deviceKind = "personal", network = "WLAN",
        sync = SyncSummary(pendingOperations = 4, pendingUploads = 23, failedUploads = 2, rejectedOperations = 1, lastSuccessfulSync = 0L),
        freeStorageBytes = 1_200_000_000L,
        errorCodes = listOf("SYNC_UPLOAD_FAILED" to Instant.parse("2026-11-11T06:12:03Z"), "Kunde Müller" to null),
    )

    /** Felder, die der Server annimmt (backend Helpdesk/Diagnostics.cs). */
    private val allowed = setOf("appVersion", "build", "platform", "osVersion", "deviceModel", "deviceKind", "deviceId", "network", "syncOldestPending",
        "syncLastSuccess", "syncPendingOperations", "syncPendingUploads", "syncFailedUploads", "syncPendingBytes", "syncConflicts", "freeStorageBytes",
        "errorCodes", "log")

    @Test
    fun `Diagnose enthält nur erlaubte Felder und gültige Fehlercodes`() {
        val payload = report.payload()
        assertTrue(allowed.containsAll(payload.keys), "Unbekannte Felder: ${payload.keys - allowed}")
        assertEquals("Android", payload.str("platform"))
        assertEquals("23", payload["syncPendingUploads"].toString())
        val codes = payload["errorCodes"] as JsonArray
        assertEquals(1, codes.size, "Freitext wie Kundennamen landet nie in den Fehlercodes.")
        assertEquals("SYNC_UPLOAD_FAILED", (codes[0] as JsonObject).str("code"))
        assertFalse(Json.encodeToString(JsonObject.serializer(), payload).contains("Müller"))
        assertTrue(report.lines().any { it.first == "Abgleich" && it.second.contains("23 Dateien") })
    }

    @Test
    fun `Meldung mit Fotos geht offline in die Outbox – Dateien zuerst`() {
        val store = LocalStore(MemoryStorage())
        val reporter = HelpdeskReporter(store, BauakteService(store, MemoryFileVault()) { "u" })
        val id = reporter.report(NewTicket("  Fotos hängen in der Warteschlange ", "seit heute früh", TicketCategory.Sync, reporterBlocked = true),
            report, listOf(byteArrayOf(1, 2, 3), byteArrayOf(4, 5)))

        val ops = store.backend.outbox()
        assertEquals(listOf("file.announce", "file.announce", "ticket.create"), ops.map { it.type })
        val payload = ops.last().payload
        assertEquals("Fotos hängen in der Warteschlange", payload.str("title"))
        assertEquals("urgent", payload.str("priority"), "„Ich kann nicht arbeiten“ ohne eigene Wahl gilt als dringend.")
        assertEquals("android", payload.str("source"))
        assertEquals(ops.take(2).map { it.entityId }, payload.strings("attachmentFileIds"))
        assertEquals(id, ops.last().entityId)
        assertEquals(listOf(id), reporter.pending().map { it.id })
        assertNull(reporter.pending().single().number)
    }

    @Test
    fun `Titel ist Pflicht, ohne Einwilligung keine Diagnose`() {
        val store = LocalStore(MemoryStorage())
        val reporter = HelpdeskReporter(store, BauakteService(store, MemoryFileVault()) { "u" })
        assertThrows<IllegalArgumentException> { reporter.report(NewTicket("   "), null) }
        reporter.report(NewTicket("Drucker druckt nicht", category = TicketCategory.OfficeIt, priority = TicketPriority.High), null)
        val payload = store.backend.outbox().single().payload
        assertEquals(kotlinx.serialization.json.JsonNull, payload["diagnostics"])
        assertEquals("officeIt", payload.str("category"))
        assertEquals("high", payload.str("priority"))
    }

    @Test
    fun `Kategorien und Prioritäten entsprechen dem Server`() {
        assertEquals(listOf("signIn", "sync", "appError", "deviceDefect", "officeIt", "access", "other"), TicketCategory.entries.map { it.raw })
        assertEquals(TicketPriority.Normal, TicketPriority.of("unbekannt"))
        assertEquals(TicketCategory.OfficeIt, TicketCategory.of("officeIt"))
    }

    @Test
    fun `Verlauf ohne interne Notizen, älteste zuerst, auch für ältere Server`() {
        val json = Json { ignoreUnknownKeys = true }
        val full = json.decodeFromString(TicketFull.serializer(), """
            {"summary":{"id":"1","number":"T-2026-0001","title":"Drucker","status":"resolved","priority":"high","slaState":"met"},
             "canRate":true,"rating":null,
             "timeline":[{"id":"b","at":"2026-11-11T09:00:00Z","kind":"comment","actorName":"Anna","summary":"Anna hat geantwortet","text":"Toner getauscht"},
                         {"id":"c","at":"2026-11-11T08:30:00Z","kind":"note","summary":"intern","text":"geheim","isInternal":true},
                         {"id":"a","at":"2026-11-11T08:00:00Z","kind":"created","summary":"gemeldet","text":"druckt nicht","isReporter":true}]}
        """.trimIndent())
        assertEquals(listOf("a", "b"), full.history().map { it.id })
        assertTrue(full.canRate)
        assertEquals(TicketPriority.High, TicketPriority.of(full.summary.priority))
        assertFalse(full.summary.isOpen)

        val old = json.decodeFromString(TicketFull.serializer(), """
            {"summary":{"id":"1","number":"T-1","title":"x"},"comments":[{"id":"k","text":"Hallo","createdAt":"2026-11-11T08:00:00Z"},
             {"id":"n","text":"intern","isInternal":true,"createdAt":"2026-11-11T08:01:00Z"}]}
        """.trimIndent())
        assertEquals(listOf("Hallo"), old.history().map { it.text })
    }

    @Test
    fun `Demomodus liefert Verlauf, Bewertung und Unterkategorien`() = kotlinx.coroutines.runBlocking {
        val api = EstrichOneApi("https://demo.invalid", MemorySessionStore(), SoftwareDeviceKeys(), DemoServer().client())
        api.signIn("demo", "demo", "Test")
        val open = api.ticket(DemoData.TICKET)
        assertTrue(open.history().first().kind == "created")
        assertFalse(open.canRate)
        val resolved = api.ticket("0193a1b2-de00-7000-8000-00000000c004")
        assertTrue(resolved.canRate)
        assertEquals(4, api.rateTicket(resolved.summary.id, 4, "gut").rating)
        assertTrue(api.helpdeskOptions().categories.any { it.subcategories.isNotEmpty() })
        assertEquals(1, api.suggestArticles("Fotos fehlen", "sync").size)
    }
}
