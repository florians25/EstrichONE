package de.estrichone.core

import kotlinx.coroutines.runBlocking
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import okhttp3.OkHttpClient
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertNotNull
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.DisplayName
import org.junit.jupiter.api.Test
import java.io.IOException
import java.time.Instant
import java.time.LocalDate
import java.time.LocalTime

@DisplayName("Disposition: Tagesplan, Schnelldisposition, Vormerken, Freigabe")
class DayPlanTests {
    private val planJson = """
        {"day":"2026-11-24","holiday":null,"canPlan":true,"myCrewIds":["c1"],
         "release":{"day":"2026-11-24","releasedAt":"2026-11-24T05:30:00Z","releasedByName":"Sabine Wagner","note":null,"changedCount":2,
           "removed":[{"assignmentId":"r1","version":1,"title":"Anbau Weber","crewName":"Kolonne Kaya","activity":"Erstbegehung"}]},
         "crews":[
          {"id":"c2","name":"Kolonne Brandl","color":"#2F80ED","leaderName":"Anna Koller","members":[{"id":"u3","name":"Anna Koller"},
            {"id":"u4","name":"Tobias Lang","absenceNote":"Urlaub"}],"vehicles":[{"id":"v2","name":"Sprinter","kind":"vehicle"}],
           "rosterChanged":false,"isMine":false,"warnings":["Tobias Lang ist im Urlaub."],
           "stops":[{"assignmentId":"a3","order":2,"startTime":"12:00:00","activity":"Nacharbeiten","bauakteId":"b3","title":"Praxis","warnings":["Tobias Lang ist im Urlaub."]},
                    {"assignmentId":"a2","order":1,"startTime":"08:30:00","endTime":"09:30:00","activity":"Erstbegehung","bauakteId":"b2","number":"2026-042","title":"Halle","change":"new"}]},
          {"id":"c1","name":"Kolonne Huber","color":"#E07A1F","leaderName":"Max Huber","members":[{"id":"u1","name":"Max Huber"},{"id":"u2","name":"Lukas Brandl"}],
           "vehicles":[],"isMine":true,"stops":[{"assignmentId":"a1","order":1,"activity":"Estrich einbringen","bauakteId":"b1","title":"EFH","change":"changed"}]}],
         "withoutCrew":[],"absent":[{"id":"u4","name":"Tobias Lang","absenceNote":"Urlaub"}],"free":[{"id":"u7","name":"Felix Gruber"}],"neuesFeld":1}
    """.trimIndent()

    private fun plan() = EstrichOneApi.json.decodeFromString(DayPlan.serializer(), planJson).normalized()

    @Test
    fun `Tagesplan lesen, meine Kolonne zuerst, Fahrtreihenfolge, Zeiten, Konflikte ohne Wiederholung`() {
        val plan = plan()
        assertEquals(listOf("Kolonne Huber", "Kolonne Brandl"), plan.sortedCrews.map { it.name })
        val brandl = plan.crew("c2")!!
        assertEquals(listOf("a2", "a3"), brandl.stops.map { it.assignmentId })
        assertEquals("08:30 bis 09:30", brandl.stops[0].timeText)
        assertEquals("ab 12:00", brandl.stops[1].timeText)
        assertEquals("ganztägig", plan.crew("c1")!!.stops[0].timeText)
        assertEquals("Neu", brandl.stops[0].changeLabel)
        assertEquals("Geändert", plan.crew("c1")!!.stops[0].changeLabel)
        assertEquals("2026-042, Halle", brandl.stops[0].heading)
        assertEquals(listOf("Tobias Lang ist im Urlaub."), brandl.conflicts)
        assertEquals("Anna Koller (Leitung)", brandl.memberText)
        assertEquals(3, plan.stopCount)
        assertEquals("c2", plan.crewOf("a3")?.id)
        assertEquals(2, plan.release?.changedCount)
    }

    @Test
    fun `Kurzfassung für Heute und Freigabestand`() {
        val plan = plan()
        val releasedAt = Instant.parse("2026-11-24T05:30:00Z")
        val today = releasedAt.atZone(Dates.zone).toLocalDate()
        val clock = Dates.time(releasedAt)
        assertEquals("2 Kolonnen, 2 Änderungen seit Freigabe", DispatchText.summary(plan))
        assertEquals(DispatchText.Tone.Warning, DispatchText.tone(plan))
        assertEquals("Freigegeben heute, $clock" to "2 Änderungen seit Freigabe", DispatchText.releaseTitle(plan, today))
        val released = plan.copy(release = plan.release!!.copy(changedCount = 0))
        assertEquals("2 Kolonnen, freigegeben", DispatchText.summary(released))
        assertEquals(DispatchText.Tone.Success, DispatchText.tone(released))
        assertEquals("Freigegeben heute, $clock" to "von Sabine Wagner", DispatchText.releaseTitle(released, today))
        val draft = plan.copy(release = null, crews = plan.crews.take(1))
        assertEquals("1 Kolonne, noch nicht freigegeben", DispatchText.summary(draft))
        assertEquals("Entwurf" to "Noch nicht freigegeben", DispatchText.releaseTitle(draft, today))
        assertEquals("Noch keine Einsätze geplant", DispatchText.summary(DayPlan("2026-11-24")))
        assertEquals("1 vorgemerkte Änderung gesendet", DispatchText.sent(1))
        assertEquals("3 Änderungen vorgemerkt, Versand bei Verbindung", DispatchText.pending(3))
        assertEquals("gestern, $clock", DispatchText.stamp(releasedAt, today.plusDays(1)))
        assertEquals("${Dates.dayMonth(today)}, $clock", DispatchText.stamp(releasedAt, today.plusDays(5)))
    }

    @Test
    fun `Änderungen als Batch-Operationen wie im Web`() {
        val assign = PlanChange.Assign("n1", "b1", "EFH", "c1", "2026-11-25", "Erstbegehung", "07:00:00", null)
        val op = assign.batchOperations!!.single()
        assertEquals("upsert", (op["op"] as JsonPrimitive).content)
        val assignment = op["assignment"] as JsonObject
        assertEquals("c1", assignment.str("crewId"))
        assertTrue(assignment.containsKey("endTime"))
        assertEquals("EFH einplanen (Mi. 25.11., 07:00)", assign.summary)

        val onlyDay = PlanChange.Move("a1", "EFH", day = "2026-11-26").batchOperations!!.single()
        assertFalse(onlyDay.containsKey("changeCrew"))
        assertFalse(onlyDay.containsKey("setTime"))
        val time = PlanChange.Move("a1", "EFH", crewId = "c2", startTime = "08:00:00", setTime = true).batchOperations!!.single()
        assertEquals(true, time.bool("changeCrew"))
        assertEquals(true, time.bool("setTime"))
        assertEquals("08:00:00", time.str("startTime"))
        assertEquals("delete", PlanChange.Remove("a1", "EFH").batchOperations!!.single().str("op"))
        assertNull(PlanChange.SwapCrews("2026-11-24", "c1", "c2", "Huber und Brandl tauschen").batchOperations)
        assertEquals("Kolonnen getauscht", PlanChange.SwapCrews("d", "a", "b").doneText)
    }

    @Test
    fun `Reihenfolge, Verschieben, Tausch und Auswahl`() {
        val plan = plan()
        val brandl = plan.crew("c2")!!
        val ids = PlanOrder.move(brandl.stops.map { it.assignmentId }, 1, 0)
        assertEquals(listOf("a3", "a2"), ids)
        val order = PlanOrder.change(brandl, plan.day, ids)!!
        assertEquals(listOf("08:30:00", "12:00:00"), order.startTimes)
        assertNull(PlanOrder.change(brandl, plan.day, brandl.stops.map { it.assignmentId }))

        val day = LocalDate.of(2026, 11, 24)
        val stop = brandl.stops[0]
        assertNull(PlanMove.change(stop, day, "c2", day, "c2", false, LocalTime.of(8, 30), null))
        val moved = PlanMove.change(stop, day, "c2", day.plusDays(1), "c1", true, LocalTime.of(9, 0), LocalTime.of(11, 0))!!
        assertEquals("2026-11-25", moved.day)
        assertEquals("c1", moved.crewId)
        assertEquals("09:00:00", moved.startTime)
        assertEquals("11:00:00", moved.endTime)

        val swap = RosterSwap(brandl, plan, vehicle = false)
        assertEquals("u4", swap.defaultOutgoing)
        assertEquals(listOf("Felix Gruber", "Max Huber", "Lukas Brandl"), swap.inOptions.map { it.name })
        assertEquals("Kolonne Brandl: Felix Gruber statt Tobias Lang", swap.change(plan.day, "u4", "u7")!!.text)
        assertNull(swap.change(plan.day, null, null))
        val cars = RosterSwap(plan.crew("c1")!!, plan, vehicle = true, resources = listOf(PlanningResource("v2", name = "Sprinter"), PlanningResource("v9", name = "Alt", isActive = false)))
        assertEquals(listOf("Sprinter" to "bei Kolonne Brandl"), cars.inOptions.map { it.name to it.detail })
        assertEquals("v2", cars.change(plan.day, null, "v2")!!.toResourceId)

        val backlog = listOf(BacklogEntry("b1", "2026-041", "EFH", city = "Landshut", upcomingAssignments = 2),
            BacklogEntry("b5", "2026-047", "Anbau Weber", street = "Lindenstr. 8", postalCode = "84137", city = "Vilsbiburg", totalAreaSquareMeters = 38.0),
            BacklogEntry("b6", "2026-050", "Kita", plannedStart = "2026-12-01", upcomingAssignments = 1))
        val choices = PlanChoices.from(backlog, plan.crew("c1")!!, "")
        assertEquals(listOf("b5", "b6"), choices.map { it.id })
        assertEquals("Lindenstr. 8, 84137 Vilsbiburg, 38 m²", choices[0].subtitle)
        assertTrue(choices[0].isFree)
        assertEquals(listOf("b6"), PlanChoices.from(backlog, plan.crew("c1")!!, "kita").map { it.id })
        assertEquals(LocalTime.of(7, 0), PlanTime.suggestedStart(plan.crew("c1")))
        assertEquals(LocalTime.of(12, 0), PlanTime.suggestedStart(brandl))
    }

    @Test
    fun `Demoserver laden, zuweisen, verschieben, tauschen, freigeben`() = runBlocking {
        val api = EstrichOneApi("https://demo.invalid", MemorySessionStore(), SoftwareDeviceKeys(), DemoServer().client())
        api.signIn("m.huber", "demo", "Test")
        val desk = DispatchDesk(api, MemoryStorage())
        val today = LocalDate.now(Dates.zone)
        val loaded = desk.load(today.toString())
        val plan = loaded.plan!!
        assertTrue(loaded.isCurrent)
        assertTrue(plan.canPlan)
        assertEquals("Kolonne Huber", plan.sortedCrews.first().name)
        assertEquals(listOf("Estrich einbringen OG", "CM-Messung und Belegreife"), plan.sortedCrews.first().stops.map { it.activity })
        assertEquals(1, plan.release!!.removed.size)

        val huber = plan.sortedCrews.first()
        val assign = PlanChange.Assign(Ids.v7(), DemoData.WEBER, "Anbau Weber", huber.id, today.toString(), "Erstbegehung", "16:00:00", null)
        assertEquals(emptyList<String>(), desk.check(assign))
        assertEquals("Eingeplant", (desk.perform(assign) as PlanOutcome.Applied).message)
        val after = desk.load(today.toString()).plan!!
        val weber = after.crew(huber.id)!!.stops.last()
        assertEquals("Anbau Weber", weber.title)
        assertEquals("Neu", weber.changeLabel)

        val reorder = PlanOrder.change(after.crew(huber.id)!!, today.toString(), PlanOrder.move(after.crew(huber.id)!!.stops.map { it.assignmentId }, 2, 0))!!
        val ordered = (desk.perform(reorder) as PlanOutcome.Applied).plan!!
        assertEquals("Anbau Weber", ordered.crew(huber.id)!!.stops.first().title)
        assertEquals("07:00", ordered.crew(huber.id)!!.stops.first().startText)

        val next = desk.perform(PlanChange.NextFreeDay(weber.assignmentId, weber.title)) as PlanOutcome.Applied
        assertTrue(next.message.startsWith("Verschoben auf "), next.message)

        val brandl = after.crews.first { it.name == "Kolonne Brandl" }
        val swapped = (desk.perform(PlanChange.SwapCrews(today.toString(), huber.id, brandl.id, "tauschen")) as PlanOutcome.Applied).plan!!
        assertTrue(swapped.crew(brandl.id)!!.stops.any { it.title == "EFH Schneider, Neubau" })

        val swap = RosterSwap(brandl, after, vehicle = false)
        val person = (desk.perform(swap.change(today.toString(), "0193a1b2-de00-7000-8000-000000004004", "0193a1b2-de00-7000-8000-000000004007")!!) as PlanOutcome.Applied).plan!!
        assertTrue(person.crew(brandl.id)!!.rosterChanged)
        assertTrue(person.crew(brandl.id)!!.members.any { it.name == "Felix Gruber" })

        assertEquals("2 Tage freigegeben", (desk.release(today, today.plusDays(1), "Bitte pünktlich") as PlanOutcome.Applied).message)
        assertEquals(0, desk.load(today.toString()).plan!!.release!!.changedCount)
        assertEquals("Freigabe zurückgenommen", (desk.withdraw(today) as PlanOutcome.Applied).message)
        assertNull(desk.load(today.toString()).plan!!.release)
    }

    @Test
    fun `Einplanen vom Handy, Auslastung, Vorgaben und Bestätigung`() {
        val plan = plan()
        val huber = plan.crew("c1")!!
        val brandl = plan.crew("c2")!!
        assertEquals("1 Einsatz", QuickPlan.load(huber))
        assertEquals("2 Einsätze", QuickPlan.load(brandl))
        assertEquals("Frei", QuickPlan.load(huber.copy(stops = emptyList())))
        assertEquals("1 Einsatz, Max Huber (Leitung), Lukas Brandl", QuickPlan.crewDetail(huber))
        assertEquals(listOf("Heute", "Morgen", "Übermorgen"), QuickPlan.quickDays.map { it.first })

        assertEquals("c2", QuickPlan.defaultCrew(plan, "c2"))
        assertEquals("c1", QuickPlan.defaultCrew(plan, null))
        assertEquals("c2", QuickPlan.defaultCrew(plan.copy(crews = plan.crews.map { it.copy(isMine = false, stops = if (it.id == "c2") emptyList() else it.stops) }), "fehlt"))
        assertNull(QuickPlan.defaultCrew(plan.copy(crews = emptyList()), null))

        assertEquals("Estrich einbringen", QuickPlan.defaultActivity(BauakteStatus.Planned))
        assertEquals("Erstbegehung", QuickPlan.defaultActivity(BauakteStatus.Inquiry))
        assertEquals("CM-Messung und Belegreife", QuickPlan.defaultActivity(BauakteStatus.Drying))
        assertEquals("Estrich einbringen", QuickPlan.defaultActivity(null))
        val offered = QuickPlan.activities("Erstbegehung")
        assertEquals("Erstbegehung", offered.first())
        assertEquals(PlanChoices.activities.toSet(), offered.toSet())
        assertEquals(PlanChoices.activities.size, offered.size)

        val site = PlanChoice("b9", "2026-050, Neubau Lang", null, isFree = true)
        val day = LocalDate.parse("2026-11-25")
        val timed = QuickPlan.assign("n1", site, "c1", day, "Estrich einbringen", LocalTime.of(7, 30), LocalTime.of(7, 0))
        assertEquals(PlanChange.Assign("n1", "b9", "2026-050, Neubau Lang", "c1", "2026-11-25", "Estrich einbringen", "07:30:00", "07:30:00"), timed)
        val allDay = QuickPlan.assign("n1", site, "c1", day, "Estrich einbringen", null, LocalTime.of(9, 0))
        assertNull(allDay.startTime)
        assertNull(allDay.endTime)
        assertEquals("Eingeplant für Mi. 25.11.", QuickPlan.confirmation(timed, "Eingeplant", emptyList()))
        assertEquals("Eingeplant für Mi. 25.11. Bitte prüfen Sie die Konflikte in der Disposition.",
            QuickPlan.confirmation(timed, "Eingeplant", listOf("Überschneidung")))
        assertEquals("Verschoben auf Do. 26.11.", QuickPlan.confirmation(PlanChange.Move("a1", "EFH", day = "2026-11-26"), "Verschoben", emptyList()))
        assertEquals("Verschoben", QuickPlan.confirmation(PlanChange.Move("a1", "EFH", startTime = "08:00:00", setTime = true), "Verschoben", emptyList()))

        val found = QuickPlan.find(plan, "B2")!!
        assertEquals("a2", found.stop.assignmentId)
        assertEquals("2026-11-24", found.day)
        assertEquals("Kolonne Brandl: Anna Koller (Leitung)", found.crewLine)
        assertNull(QuickPlan.find(plan, "b9"))
    }

    @Test
    fun `Nächster Einsatz einer Bauakte für Planer, nach dem Einplanen aktualisiert`() = runBlocking {
        val api = EstrichOneApi("https://demo.invalid", MemorySessionStore(), SoftwareDeviceKeys(), DemoServer().client())
        api.signIn("m.huber", "demo", "Test")
        val desk = DispatchDesk(api, MemoryStorage())
        val today = LocalDate.now(Dates.zone)
        val efh = desk.nextAppointment(DemoData.EFH)!!
        assertEquals(today.toString(), efh.day)
        assertEquals("Kolonne Huber", efh.crew?.name)
        assertNull(desk.nextAppointment(DemoData.WEBER))

        val crew = desk.load(today.plusDays(2).toString()).plan!!.crews.first { it.name == "Kolonne Brandl" }
        val site = PlanChoice(DemoData.WEBER, "2026-047, Anbau Weber", null, isFree = true)
        val change = QuickPlan.assign(Ids.v7(), site, crew.id, today.plusDays(2), QuickPlan.defaultActivity(BauakteStatus.Inquiry), LocalTime.of(8, 0), null)
        assertTrue(desk.perform(change) is PlanOutcome.Applied)
        val planned = desk.nextAppointment(DemoData.WEBER, change.targetDay)!!
        assertEquals(today.plusDays(2).toString(), planned.day)
        assertEquals("Erstbegehung", planned.stop.activity)
        assertEquals("Kolonne Brandl", planned.crew?.name)
        assertEquals("08:00", planned.stop.startText)

        val move = PlanMove.change(planned.stop, today.plusDays(2), crew.id, today.plusDays(3), crew.id, false, LocalTime.of(8, 0), null)!!
        assertTrue(desk.perform(move) is PlanOutcome.Applied)
        assertEquals(today.plusDays(3).toString(), desk.nextAppointment(DemoData.WEBER, move.targetDay)!!.day)
    }

    @Test
    fun `Ohne Verbindung letzter Stand, vormerken, später senden, Ablehnung bleibt sichtbar`() = runBlocking {
        val server = DemoServer()
        var offline = false
        val http = OkHttpClient.Builder().addInterceptor { chain -> if (offline) throw IOException("offline") else server.intercept(chain) }.build()
        val api = EstrichOneApi("https://demo.invalid", MemorySessionStore(), SoftwareDeviceKeys(), http)
        api.signIn("m.huber", "demo", "Test")
        val storage = MemoryStorage()
        val desk = DispatchDesk(api, storage)
        val today = LocalDate.now(Dates.zone).toString()
        val plan = desk.load(today).plan!!

        offline = true
        val cached = desk.load(today)
        assertFalse(cached.isCurrent)
        assertEquals(plan, cached.plan)
        assertNotNull(cached.loadedAt)
        val missing = desk.load(LocalDate.now(Dates.zone).plusDays(30).toString())
        assertNull(missing.plan)
        assertEquals("Ohne Verbindung ist die Planung für diesen Tag nicht verfügbar.", missing.message)
        assertNull(desk.check(PlanChange.Remove("x", "y")))

        val stop = plan.sortedCrews.first().stops.first()
        assertEquals(PlanOutcome.Queued, desk.perform(PlanChange.Move(stop.assignmentId, stop.title, startTime = "08:00:00", setTime = true)))
        assertEquals(PlanOutcome.Queued, desk.perform(PlanChange.Remove("0193a1b2-de00-7000-8000-00000000ffff", "Unbekannt")))
        assertEquals(DispatchDesk.ONLINE_ONLY, (desk.release(LocalDate.now(Dates.zone), LocalDate.now(Dates.zone), null) as PlanOutcome.Failed).message)
        assertEquals(2, desk.outbox.entries().size)
        assertEquals("${stop.title} verschieben, 08:00", desk.outbox.entries().first().change.summary)
        assertEquals(0, desk.flush().sent)
        assertEquals(2, PlanningOutbox(storage).entries().size)

        offline = false
        val result = desk.flush()
        assertEquals(1, result.sent)
        assertEquals(1, result.failed)
        val left = desk.outbox.entries().single()
        assertEquals("Der Einsatz existiert nicht (mehr).", left.error)
        assertEquals("08:00", desk.load(today).plan!!.crewOf(stop.assignmentId)!!.stops.first { it.assignmentId == stop.assignmentId }.startText)
        assertEquals(PlanningOutbox.FlushResult(), desk.flush())
        desk.outbox.discard(left.id)
        assertTrue(desk.outbox.entries().isEmpty())
    }
}
