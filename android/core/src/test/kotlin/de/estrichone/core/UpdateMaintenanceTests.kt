package de.estrichone.core

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.DisplayName
import org.junit.jupiter.api.Test
import java.time.Instant

class UpdateMaintenanceTests {
    private val now = Instant.parse("2026-11-12T10:00:00Z")
    private fun at(seconds: Long) = now.plusSeconds(seconds).toString()
    private fun state(phase: String, from: Long, to: Long?, message: String? = null) =
        UpdateMaintenance("u1", phase, "1.0.15", "1.0.16", at(-120), at(from), to?.let { at(it) }, message)

    @Test
    @DisplayName("Update: Feld update in /system/notices wird gelesen, unbekannte Phasen ignoriert")
    fun decode() {
        val json = """{"announcements":[],"maintenance":null,"serverTime":"2026-11-12T10:00:00Z",
            "update":{"id":"u1","phase":"announce","fromVersion":"1.0.15","targetVersion":"1.0.16","startedAt":"2026-11-12T10:00:00Z",
            "phaseStartedAt":"2026-11-12T10:00:00Z","phaseEndsAt":"2026-11-12T10:01:00Z","expiresAt":"2026-11-12T16:00:00Z","source":"update.sh"}}"""
        val notices = EstrichOneApi.json.decodeFromString(SystemNotices.serializer(), json)
        assertEquals("announce", notices.activeUpdate?.phase)
        assertEquals("1.0.16", notices.activeUpdate?.targetVersion)
        val unknown = EstrichOneApi.json.decodeFromString(SystemNotices.serializer(), """{"update":{"id":"x","phase":"bunt"}}""")
        assertNull(unknown.activeUpdate)
        assertNull(EstrichOneApi.json.decodeFromString(SystemNotices.serializer(), "{}").activeUpdate)
    }

    @Test
    @DisplayName("Update: Ankündigung mit Countdown, danach Wartungsbildschirm")
    fun announce() {
        assertEquals("0:59", UpdatePresentation.clock(59.0))
        assertEquals("1:00", UpdatePresentation.clock(59.2))
        assertEquals("0:00", UpdatePresentation.clock(-4.0))
        val view = UpdatePresentation.screen(state("announce", -1, 59), now)
        assertTrue(view.banner)
        assertFalse(view.page)
        assertEquals("Update auf Version 1.0.16 beginnt in 0:59 – bitte Arbeit speichern. Offene Änderungen werden jetzt übertragen.", view.bannerText)
        assertEquals(listOf("now", "todo", "todo"), view.steps.map { it.second })
        val late = UpdatePresentation.screen(state("announce", -61, -1), now)
        assertTrue(late.page)
        assertEquals("Update wird eingespielt", late.title)
    }

    @Test
    @DisplayName("Update: Phasen, Fortschritt, Rücknahme und Fehlschlag")
    fun phases() {
        val update = UpdatePresentation.screen(state("update", -30, 30), now)
        assertEquals("Mindestens noch", update.countdownLabel)
        assertEquals("0:30", update.countdown)
        assertEquals(0.5f, update.progress, 0.01f)
        assertEquals(listOf("done", "now", "todo"), update.steps.map { it.second })
        val finish = UpdatePresentation.screen(state("finish", -15, 45), now)
        assertEquals("Update wird abgeschlossen", finish.title)
        assertEquals("Freigabe in", finish.countdownLabel)
        assertEquals("0:45", finish.countdown)
        assertEquals("Rücknahme", UpdatePresentation.screen(state("rollback", 0, null), now).steps[2].first)
        val failed = UpdatePresentation.screen(state("failed", 0, null, "Datenbank startet nicht."), now)
        assertTrue(failed.failed)
        assertEquals("Datenbank startet nicht. Der Administrator ist informiert.", failed.text)
        assertEquals("Gesperrt, bis das Problem behoben ist", failed.countdownLabel)
        assertEquals("EstrichONE wurde auf Version 1.0.16 aktualisiert. Bitte erneut anmelden.", UpdatePresentation.doneText("1.0.16", false))
    }
}
