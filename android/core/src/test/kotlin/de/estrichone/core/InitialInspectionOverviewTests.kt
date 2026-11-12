package de.estrichone.core

import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.DisplayName
import org.junit.jupiter.api.Test

class InitialInspectionOverviewTests {
    private val akte = "0193a1b2-0000-7000-8000-000000000001"
    private fun entity(type: String, id: String, vararg fields: Pair<String, JsonElement>) =
        LocalEntity(type, id, JsonObject(fields.toMap()), SyncState.Synced)

    private val bauakte = Bauakte(entity("bauakte", akte, "title" to JsonPrimitive("EFH Termin")))
    private fun job(kind: String?, day: String, activity: String = "Erstbegehung", of: String = akte) = PlannedJob(
        id = "j-$day-$kind", day = day, startTime = "08:00:00", activity = activity, bauakteId = of, bauakteTitle = "EFH Termin",
        crewName = "Kolonne Nord", people = listOf(PersonRef("u1", "Max Huber"), PersonRef("u2", "Kolonne Nord")), kind = kind,
    )

    @Test
    @DisplayName("Erstbegehung: offen, geplant (Einsatz „Erstbegehung“ ab heute), durchgeführt mit Protokollen und Fotos, Termintext")
    fun overview() {
        val today = "2026-11-12"
        val others = listOf(job("screed", "2026-11-20", "Estrich einbringen"), job("initialInspection", "2026-11-01"),
            job("initialInspection", "2026-11-18", of = "0193a1b2-0000-7000-8000-000000000099"))
        val open = InitialInspectionOverview(bauakte, emptyList(), others, today)
        assertEquals(InitialInspectionStatus.Open, open.status)
        assertNull(open.appointment)

        val planned = job("initialInspection", "2026-11-19")
        val overview = InitialInspectionOverview(bauakte, emptyList(), others + planned, today)
        assertEquals(InitialInspectionStatus.Planned, overview.status)
        assertEquals("Geplant", overview.status.title)
        assertEquals(planned, overview.appointment)
        assertEquals("Do., 19.11.2026, 08:00 · Kolonne Nord, Max Huber", InitialInspectionOverview.appointmentText(planned))
        assertTrue(InitialInspectionOverview.isInitialInspection(job(null, "2026-11-19")))
        assertFalse(InitialInspectionOverview.isInitialInspection(job(null, "2026-11-19", "Estrich")))

        val inspection = Inspection(entity("inspection", "0193a1b2-0000-7000-8000-0000000000aa", "bauakteId" to JsonPrimitive(akte),
            "kind" to JsonPrimitive("initialInspection"), "performedAt" to JsonPrimitive("2026-11-10T09:00:00Z"),
            "photoFileIds" to JsonArray(listOf(JsonPrimitive("0193A1B2-0000-7000-8000-0000000000F1")))))
        val done = InitialInspectionOverview(bauakte, listOf(inspection), listOf(planned), today)
        assertEquals(InitialInspectionStatus.Done, done.status)
        assertEquals("Durchgeführt", done.status.title)
        assertEquals(1, done.protocols.size)
        assertEquals(listOf("0193a1b2-0000-7000-8000-0000000000f1"), done.photoIds)
        assertEquals("Nicht erforderlich", InitialInspectionStatus.Waived.title)

        val pdf = FileItem(entity("file", "0193a1b2-0000-7000-8000-0000000000b1", "kind" to JsonPrimitive("document"),
            "originalName" to JsonPrimitive("Erstbegehungsprotokoll 2026-11-10.pdf"), "caption" to JsonPrimitive("Erstbegehung")))
        val other = FileItem(entity("file", "0193a1b2-0000-7000-8000-0000000000b2", "kind" to JsonPrimitive("document"),
            "originalName" to JsonPrimitive("Lieferschein.pdf")))
        assertTrue(InitialInspectionOverview.isProtocolDocument(pdf))
        assertFalse(InitialInspectionOverview.isProtocolDocument(other))
    }
}
