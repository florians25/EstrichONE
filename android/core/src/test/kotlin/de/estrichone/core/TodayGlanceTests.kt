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
import java.time.LocalDate
import java.time.LocalTime

class TodayGlanceTests {
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

    @Test
    @DisplayName("Phase: bevorstehend, läuft, vorbei, ohne Uhrzeit")
    fun phase() {
        assertEquals(TodayGlance.Phase.Upcoming(25), TodayGlance.phase("07:00", "13:00", LocalTime.of(6, 35)))
        assertEquals(TodayGlance.Phase.Running, TodayGlance.phase("07:00:00", "13:00:00", LocalTime.of(9, 41)))
        assertEquals(TodayGlance.Phase.Finished, TodayGlance.phase("07:00", "13:00", LocalTime.of(13, 0)))
        assertEquals(TodayGlance.Phase.Running, TodayGlance.phase("07:00", null, LocalTime.of(18, 0)))
        assertEquals(TodayGlance.Phase.Unknown, TodayGlance.phase(null, null, LocalTime.of(9, 0)))
        assertEquals("in 25 Min.", TodayGlance.Phase.Upcoming(25).text)
        assertEquals("in 4 Std.", TodayGlance.Phase.Upcoming(260).text)
        assertEquals("Jetzt", TodayGlance.Phase.Running.text)
        assertNull(TodayGlance.Phase.Unknown.text)
    }

    @Test
    @DisplayName("Aufgeklappt: der laufende, sonst der nächste, sonst der letzte Einsatz")
    fun focus() {
        val p = TodayGlance.Phase.Running
        assertEquals(1, TodayGlance.focusIndex(listOf(TodayGlance.Phase.Finished, p, TodayGlance.Phase.Upcoming(200))))
        assertEquals(1, TodayGlance.focusIndex(listOf(TodayGlance.Phase.Finished, TodayGlance.Phase.Upcoming(20), TodayGlance.Phase.Upcoming(200))))
        assertEquals(1, TodayGlance.focusIndex(listOf(TodayGlance.Phase.Finished, TodayGlance.Phase.Finished)))
        assertNull(TodayGlance.focusIndex(emptyList()))
    }

    @Test
    @DisplayName("Entfernung: Meter, Kilometer mit Komma, gerundet")
    fun distance() {
        assertEquals("850 m", TodayGlance.distanceText(830.0))
        assertEquals("1,8 km", TodayGlance.distanceText(1840.0))
        assertEquals("23 km", TodayGlance.distanceText(23_400.0))
        val meters = TodayGlance.distance(48.5468, 12.1350, 48.5372, 12.1521)
        assertTrue(meters in 1500.0..1800.0)
    }

    @Test
    @DisplayName("Kennzahlen: offene Mängel und Aufgaben, Belegreife, Wetter von heute")
    fun site() {
        val tasks = listOf(
            Task(entity("workItem", "kind" to "defect", "status" to "open", "title" to "Riss")),
            Task(entity("workItem", "kind" to "task", "status" to "inProgress", "title" to "Randdämmung")),
            Task(entity("workItem", "kind" to "task", "status" to "done", "title" to "Folie")),
        )
        val rooms = listOf(Room(entity("room", "name" to "Bad", "status" to "laid")), Room(entity("room", "name" to "Flur", "status" to "planned")))
        val diary = listOf(
            DiaryEntry(entity("diaryEntry", "day" to "2026-11-20", "text" to "heute", "weather" to "sunny", "temperatureCelsius" to 15)),
            DiaryEntry(entity("diaryEntry", "day" to "2026-11-19", "text" to "gestern", "weather" to "rain", "temperatureCelsius" to 8)),
        )
        val today = LocalDate.of(2026, 11, 20)
        val glance = SiteGlance.of(tasks, rooms, emptyList(), diary, today)
        assertEquals("1 Mangel", glance.defectsText)
        assertEquals("1 Aufgabe", glance.tasksText)
        assertEquals("1 von 2 Räumen eingebracht", glance.readiness?.text)
        assertEquals("Sonnig, 15 °C", glance.weatherText)
        assertFalse(glance.isEmpty)
        assertTrue(SiteGlance.of(emptyList(), emptyList(), emptyList(), diary.drop(1), today).isEmpty)
    }

    @Test
    @DisplayName("Belegreife: die letzte CM-Messung zählt vor den Räumen")
    fun readiness() {
        val older = Inspection(entity("inspection", "kind" to "cmMeasurement", "performedAt" to "2026-11-10T08:00:00Z",
            "measuredCmPercent" to 0.8, "limitCmPercent" to 0.3, "isReadyForCovering" to false))
        val newer = Inspection(entity("inspection", "kind" to "cmMeasurement", "performedAt" to "2026-11-19T08:00:00Z",
            "measuredCmPercent" to 0.3, "limitCmPercent" to 0.3))
        val rooms = listOf(Room(entity("room", "name" to "Bad", "status" to "ready")))
        val today = LocalDate.of(2026, 11, 20)
        val glance = SiteGlance.of(emptyList(), rooms, listOf(older, newer), emptyList(), today)
        assertEquals("Belegreif (0,3 CM-%)", glance.readiness?.text)
        assertTrue(glance.readiness?.isReady == true)
        val roomsOnly = SiteGlance.of(emptyList(), rooms + Room(entity("room", "name" to "Flur", "status" to "laid")), emptyList(), emptyList(), today)
        assertEquals("1 von 2 Räumen belegreif", roomsOnly.readiness?.text)
    }
}
