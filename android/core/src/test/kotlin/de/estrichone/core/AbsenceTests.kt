package de.estrichone.core

import kotlinx.coroutines.runBlocking
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.DisplayName
import org.junit.jupiter.api.Test
import java.io.File

class AbsenceTests {
    private val me = """
        {"employeeId":"01a00000-0000-7000-8000-0000000000e1","year":2027,"canApprove":true,"pendingApprovals":2,
         "vacation":{"year":2027,"entitlement":22.5,"carryOver":5,"taken":3,"planned":4.5,"remaining":20,"sickDays":0,"requested":2,
           "annualEntitlement":30,"adjustment":0,"carryOverExpiresOn":"2027-03-31","carryOverExpired":0,"carryOverExpiring":2,
           "remainingAfterRequests":18,"byType":[],"note":null},
         "requests":[{"id":"r1","userId":"u1","userName":"Max Huber","from":"2027-06-07","to":"2027-06-09","kind":"vacation","note":null,
           "status":"approved","workingDays":2.5,"requestedByUserId":"u1","requestedAt":"2026-11-10T08:00:00Z","warnings":[],"remainingAfter":17.5,
           "canDecide":false,"canCancel":true,"version":1,"typeId":"t1","typeName":"Urlaub","typeColor":"#43A047","halfDayStart":true,
           "halfDayEnd":false,"substituteName":"Lukas Brandl","replacesRequestId":null,"canChange":true}],
         "holidays":[{"date":"2027-01-01","name":"Neujahr"}],
         "types":[{"id":"t1","key":"vacation","name":"Urlaub","code":"U","color":"#43A047","kind":"vacation","requiresApproval":true,
           "countsAsVacation":true,"requestable":true,"isPaid":true,"isActive":true,"sortOrder":10,"isSystem":true,"isSickness":false},
          {"id":"t2","key":"sick","name":"Krankheit","code":"K","color":"#E53935","kind":"sick","requiresApproval":false,"countsAsVacation":false,
           "requestable":false,"isPaid":true,"isActive":true,"sortOrder":20,"isSystem":true,"isSickness":true}],
         "closures":[],"absences":[{"id":"a1","from":"2027-06-07","to":"2027-06-09","typeId":"t1","typeName":"Urlaub","color":"#43A047",
           "kind":"vacation","halfDayStart":true,"halfDayEnd":false,"workingDays":2.5,"requestId":"r1"}]}
    """.trimIndent()

    @Test
    @DisplayName("Eigene Übersicht: Konto mit Verfall, Anträge, beantragbare Arten")
    fun decodesMine() {
        val mine = EstrichOneApi.json.decodeFromString(MyVacation.serializer(), me)
        val account = mine.vacation!!
        assertEquals(22.5, account.entitlement)
        assertEquals(18.0, account.afterRequests)
        assertEquals("2 Tage Resturlaub verfallen nach dem 31.03.2027, wenn sie bis dahin nicht genommen oder geplant sind.", account.expiryHint)
        assertEquals(listOf("vacation"), mine.requestableTypes.map { it.key })
        val request = mine.requests.single()
        assertEquals("07.06. – 09.06.2027 (erster Tag halb)", request.rangeText)
        assertEquals("Genehmigt", request.statusText)
        assertEquals("2,5", AbsenceText.days(request.workingDays))
        assertTrue(mine.canApprove)
    }

    @Test
    @DisplayName("Ältere Server ohne Erweiterungen: Standardwerte")
    fun decodesOlderServer() {
        val mine = EstrichOneApi.json.decodeFromString(MyVacation.serializer(), """
            {"employeeId":null,"vacation":{"year":2026,"entitlement":30,"carryOver":0,"taken":1,"planned":2,"remaining":27,"sickDays":0},
             "requests":[{"id":"r1","userId":"u1","userName":"Max","from":"2026-12-01","to":"2026-12-01","status":"pending","workingDays":1}],"holidays":[]}
        """.trimIndent())
        assertEquals(27.0, mine.vacation!!.afterRequests)
        assertNull(mine.vacation!!.expiryHint)
        assertFalse(mine.requests.single().halfDayStart)
        assertEquals("01.12.2026", mine.requests.single().rangeText)
        assertTrue(mine.requestableTypes.isEmpty())
    }

    @Test
    @DisplayName("Antrag: Prüfung, Normalisierung, Texte")
    fun draft() {
        assertEquals("Bitte eine Art wählen.", AbsenceDraft(null, "2027-06-07", "2027-06-09").problem)
        assertEquals("Das Ende liegt vor dem Beginn.", AbsenceDraft("t1", "2027-06-09", "2027-06-07").problem)
        val normalized = AbsenceDraft("t1", "2027-06-09", "2027-06-09", halfDayStart = true, halfDayEnd = true, note = "  ").normalized()
        assertNull(normalized.problem)
        assertFalse(normalized.halfDayEnd)
        assertNull(normalized.note)
        val body = EstrichOneApi.json.encodeToString(AbsenceDraft.serializer(), normalized)
        assertFalse(body.contains("note"))
        assertTrue(body.contains("\"halfDayStart\":true"))
        assertEquals("28.12.2027 – 02.01.2028 (letzter Tag halb)", AbsenceText.range("2027-12-28", "2028-01-02", halfDayEnd = true))
        assertEquals("07.06.2027 (halber Tag)", AbsenceText.range("2027-06-07", "2027-06-07", halfDayStart = true))
        assertEquals("2,5 Arbeitstage, danach 17 Tage Resturlaub. Der Antrag geht zur Genehmigung.",
            AbsencePreview(2.5, emptyList(), 17.0, true).summary)
        assertEquals("1 Arbeitstag. Die Änderung geht zur Genehmigung.", AbsencePreview(1.0, emptyList(), null, true).summary(change = true))
        assertEquals("Ersetzt", AbsenceRequestItem("x", "u", "A", "2027-01-04", "2027-01-05", status = "cancelled",
            decisionComment = "Durch Änderungsantrag ersetzt.").statusText)
    }

    @Test
    @DisplayName("Demomodus: beantragen, Vorschau, genehmigen, stornieren")
    fun demoFlow() = runBlocking {
        val api = EstrichOneApi("https://demo.invalid", MemorySessionStore(), SoftwareDeviceKeys(), DemoServer().client())
        api.signIn("m.huber", "demo", "Test")
        val mine = api.myVacation()
        assertTrue(mine.canApprove)
        val vacation = mine.requestableTypes.first { it.key == "vacation" }
        val preview = api.previewAbsence(AbsenceDraft(vacation.id, "2030-06-03", "2030-06-07", halfDayStart = true))
        assertEquals(4.5, preview.workingDays)
        val created = api.requestAbsence(AbsenceDraft(vacation.id, "2030-06-03", "2030-06-07", halfDayStart = true, note = "Sommer"))
        assertEquals("pending", created.status)
        val pending = api.pendingAbsenceRequests()
        assertEquals(2, pending.size)
        assertEquals("approved", api.decideAbsence(pending.first().id, true, null).status)
        assertEquals("cancelled", api.cancelAbsence(created.id).status)
    }

    @Test
    @DisplayName("Feiertage: Ostern, Bundesländer, Zeitraum")
    fun holidays() {
        assertEquals(java.time.LocalDate.of(2027, 3, 28), GermanHolidays.easterSunday(2027))
        assertEquals(java.time.LocalDate.of(2026, 4, 5), GermanHolidays.easterSunday(2026))
        val bavaria = GermanHolidays.of(2026, "BY").map { it.name }
        assertEquals(13, bavaria.size)
        assertTrue("Mariä Himmelfahrt" in bavaria && "Fronleichnam" in bavaria)
        assertFalse("Reformationstag" in GermanHolidays.of(2026, "BY").map { it.name })
        assertTrue("Buß- und Bettag" in GermanHolidays.of(2026, "SN").map { it.name })
        assertEquals(listOf("2026-12-25", "2026-12-26", "2027-01-01"),
            GermanHolidays.between(java.time.LocalDate.of(2026, 12, 20), java.time.LocalDate.of(2027, 1, 2), "BY").map { it.date })
    }

    @Test
    @DisplayName("Demomodus: Feiertage zählen nicht als Urlaubstage")
    fun demoHolidays() = runBlocking {
        val api = EstrichOneApi("https://demo.invalid", MemorySessionStore(), SoftwareDeviceKeys(), DemoServer().client())
        api.signIn("m.huber", "demo", "Test")
        val vacation = api.myVacation().requestableTypes.first { it.key == "vacation" }
        // Mo 21.12. bis Fr 1.1.: 10 Werktage, davon 25.12. und 1.1. Feiertag (26.12. ist Samstag).
        val preview = api.previewAbsence(AbsenceDraft(vacation.id, "2026-12-21", "2027-01-01"))
        assertEquals(8.0, preview.workingDays)
        assertEquals(listOf("1. Weihnachtstag", "2. Weihnachtstag", "Neujahr"), preview.holidays.map { it.name })
        assertEquals("Feiertage im Zeitraum (zählen nicht): 25.12.2026 1. Weihnachtstag, 26.12.2026 2. Weihnachtstag, 01.01.2027 Neujahr", preview.holidayText)
        // Fronleichnam 2027 (Do 27.5.) fällt in eine Woche Pfingsturlaub.
        assertEquals(4.0, api.previewAbsence(AbsenceDraft(vacation.id, "2027-05-24", "2027-05-28")).workingDays)
    }

    @Test
    @DisplayName("Demomodus: Vertretung und Änderungsantrag zu genehmigtem Urlaub")
    fun demoChangeRequest() = runBlocking {
        val api = EstrichOneApi("https://demo.invalid", MemorySessionStore(), SoftwareDeviceKeys(), DemoServer().client())
        api.signIn("m.huber", "demo", "Test")
        val approved = api.myVacation().requests.first { it.isApproved }
        assertTrue(approved.canChange)
        assertEquals("Lukas Brandl", approved.substituteName)
        val colleagues = api.directory().filter { it.id != approved.userId }
        val sabine = colleagues.first { it.displayName == "Sabine Wagner" }

        val change = AbsenceDraft.changing(approved).copy(to = java.time.LocalDate.parse(approved.to).minusDays(4).toString(), substituteUserId = sabine.id)
        assertEquals(approved.id, change.replacesRequestId)
        val body = EstrichOneApi.json.encodeToString(AbsenceDraft.serializer(), change.normalized())
        assertTrue(body.contains("\"replacesRequestId\":\"${approved.id}\"") && body.contains("\"substituteUserId\":\"${sabine.id}\""))
        val preview = api.previewAbsence(change)
        assertTrue(preview.remainingAfter!! > api.myVacation().vacation!!.afterRequests, "Der bisherige Zeitraum wird gutgeschrieben")

        val created = api.requestAbsence(change)
        assertEquals("pending", created.status)
        assertEquals(approved.id, created.replacesRequestId)
        assertEquals("Sabine Wagner", created.substituteName)
        // Solange die Änderung offen ist, gilt der alte Zeitraum und kein zweiter Änderungsantrag.
        val before = api.myVacation().requests.first { it.id == approved.id }
        assertTrue(before.isApproved)
        assertFalse(before.canChange)

        // Der Demobenutzer genehmigt selbst (Genehmiger): alter Antrag gilt als ersetzt.
        assertEquals("approved", api.decideAbsence(created.id, true, null).status)
        val after = api.myVacation().requests.first { it.id == approved.id }
        assertEquals("Ersetzt", after.statusText)
    }

    @Test
    @DisplayName("Feldnamen stimmen mit dem API-Vertrag überein")
    fun contract() {
        var dir: File? = File("").absoluteFile
        while (dir != null && !File(dir, "contracts/openapi/v1.json").exists()) dir = dir.parentFile
        val text = File(dir ?: return, "contracts/openapi/v1.json").readText()
        val schemas = EstrichOneApi.json.parseToJsonElement(text).let { it as kotlinx.serialization.json.JsonObject }
            .obj("components")!!.obj("schemas")!!
        fun props(name: String) = schemas.obj(name)!!.obj("properties")!!.keys
        val checks = mapOf(
            "MyVacation" to MyVacation.serializer().descriptor, "VacationSummary" to VacationAccount.serializer().descriptor,
            "AbsenceRequestView" to AbsenceRequestItem.serializer().descriptor, "AbsenceTypeView" to AbsenceType.serializer().descriptor,
            "MyAbsence" to MyAbsence.serializer().descriptor, "AbsencePreview" to AbsencePreview.serializer().descriptor,
            "SaveAbsenceRequestRequest" to AbsenceDraft.serializer().descriptor, "DecideAbsenceRequest" to AbsenceDecision.serializer().descriptor,
        )
        for ((schema, descriptor) in checks) {
            val known = props(schema)
            for (i in 0 until descriptor.elementsCount) {
                assertTrue(descriptor.getElementName(i) in known, "$schema.${descriptor.getElementName(i)} fehlt im Vertrag")
            }
        }
    }
}
