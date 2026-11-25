package de.estrichone.core

import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.DisplayName
import org.junit.jupiter.api.Test
import java.time.Instant
import java.time.LocalDate
import java.time.LocalTime

class SiteOverviewTests {
    private var counter = 0
    private fun entity(type: String, vararg fields: Pair<String, Any>): LocalEntity {
        counter += 1
        val values: Map<String, JsonElement> = fields.associate { (key, value) ->
            key to when (value) {
                is Number -> JsonPrimitive(value)
                is Boolean -> JsonPrimitive(value)
                else -> JsonPrimitive(value.toString())
            }
        }
        return LocalEntity(type, "0193a1b2-0000-7000-8000-%012d".format(counter), JsonObject(values), SyncState.Synced)
    }

    private fun job(id: String, day: String, start: String?, end: String? = null, bauakte: String = id) =
        PlannedJob(id = id, day = day, startTime = start, endTime = end, activity = "Estrich", bauakteId = bauakte, bauakteTitle = "B $id")

    @Test
    @DisplayName("Heute: laufender Einsatz als Hauptkarte, die übrigen nach Uhrzeit")
    fun plan() {
        val jobs = listOf(job("c", "2026-11-23", "13:00", "16:00"), job("a", "2026-11-23", "07:00", "12:00"), job("b", "2026-11-23", "09:00", "11:00"))
        val plan = TodayOverview.plan(jobs, LocalTime.of(9, 41))
        assertEquals("b", plan.focus?.id)
        assertEquals("Jetzt im Einsatz", plan.focusTitle)
        assertEquals(listOf("a", "c"), plan.others.map { it.id })
        val early = TodayOverview.plan(jobs, LocalTime.of(6, 0))
        assertEquals("a", early.focus?.id)
        assertEquals("Nächster Einsatz", early.focusTitle)
        assertNull(TodayOverview.plan(emptyList(), LocalTime.NOON).focus)
    }

    @Test
    @DisplayName("Mein Tag: Einsätze mit Beginn, Aufgaben mit Mängeln")
    fun tiles() {
        val tile = TodayOverview.jobsTile(listOf(job("a", "d", "09:00:00"), job("b", "d", "07:00:00")))
        assertEquals("2", tile.value)
        assertEquals("Einsätze ab 07:00", tile.label)
        assertEquals("Einsätze", TodayOverview.jobsTile(emptyList()).label)
        assertEquals("Aufgaben, 2 Mängel", TodayOverview.tasksTile(5, 2).label)
        assertEquals("offene Aufgabe", TodayOverview.tasksTile(1, 0).label)
    }

    private fun notice(id: String, kind: NoticeItem.Kind, title: String) =
        NoticeItem(id, kind, title, "Text", "", "in 2 Tagen", "", "", null, null)

    @Test
    @DisplayName("Mitteilungen: eine Zeile, Störung rot, ausgeblendete zählen nicht")
    fun notices() {
        val items = listOf(notice("1", NoticeItem.Kind.Critical, "Störung beim E-Mail-Versand"), notice("2", NoticeItem.Kind.Maintenance, "Geplante Wartung"),
            notice("3", NoticeItem.Kind.Info, "Neu: Räume per Foto"))
        val all = TodayOverview.noticeSummary(items, emptySet())!!
        assertEquals("3 Mitteilungen", all.title)
        assertEquals(NoticeItem.Tone.Error, all.tone)
        assertTrue(all.critical)
        assertTrue(all.subtitle.startsWith("Störung beim E-Mail-Versand"))
        val one = TodayOverview.noticeSummary(items.drop(1), setOf("3"))!!
        assertEquals("Geplante Wartung", one.title)
        assertEquals("Geplante Wartung, in 2 Tagen", one.subtitle)
        assertFalse(one.critical)
        // Störungen lassen sich nicht ausblenden.
        assertEquals(1, TodayOverview.noticeSummary(items.take(1), setOf("1"))!!.count)
        assertNull(TodayOverview.noticeSummary(items.drop(2), setOf("3")))
    }

    @Test
    @DisplayName("Baustellen: heute zuerst, dann nach Status und nächstem Termin")
    fun order() {
        data class Site(val id: String, val status: BauakteStatus, val number: String)
        val sites = listOf(Site("p", BauakteStatus.Planned, "2026-010"), Site("i", BauakteStatus.InProgress, "2026-011"),
            Site("t", BauakteStatus.Planned, "2026-012"), Site("d", BauakteStatus.Drying, "2026-013"), Site("q", BauakteStatus.Planned, "2026-014"))
        val today = "2026-11-23"
        val next = SiteList.nextJobs(listOf(job("j1", today, "07:00", bauakte = "t"), job("j2", "2026-11-25", "08:00", bauakte = "p"),
            job("j3", "2026-11-20", "08:00", bauakte = "q")), today)
        assertEquals(setOf("t", "p"), next.keys)
        val sorted = SiteList.ordered(sites, { it.id }, { it.status }, { it.number }, next, today)
        assertEquals(listOf("t", "i", "d", "p", "q"), sorted.map { it.id })
        assertTrue(SiteList.matches(SiteList.Filter.Today, BauakteStatus.Completed, isToday = true))
        assertFalse(SiteList.matches(SiteList.Filter.Running, BauakteStatus.Completed, isToday = true))
        assertEquals("Heute, 07:00", SiteList.appointmentText(job("x", today, "07:00:00"), LocalDate.parse(today)))
        assertEquals("Morgen", SiteList.appointmentText(job("x", "2026-11-24", null), LocalDate.parse(today)))
        assertEquals("Mi., 25.11., 08:00", SiteList.appointmentText(job("x", "2026-11-25", "08:00"), LocalDate.parse(today)))
        assertEquals("Morgen, 06:30", SiteList.appointmentText("2026-11-24", "06:30:00", LocalDate.parse(today)))
    }
}
