package de.estrichone.core

import kotlinx.serialization.json.JsonObject
import java.time.DayOfWeek
import java.time.LocalDate
import java.time.temporal.TemporalAdjusters

/**
 * Urlaub und Abwesenheiten im Demomodus (wie DemoAbsences.swift): Konto mit Übertrag, eigene Anträge, Vorschau
 * (Montag bis Freitag ohne die Feiertage in Bayern), Vertretung, Änderungsantrag und zwei offene Anträge zur Genehmigung.
 * Aufruf nur unter der Sperre des DemoServers.
 */
internal class DemoAbsences(private val today: LocalDate = LocalDate.now(Dates.zone)) {
    private data class Request(
        val id: String, val userId: String, val userName: String, var from: String, var to: String, var typeId: String,
        var halfDayStart: Boolean = false, var halfDayEnd: Boolean = false, var note: String? = null, var status: String = "pending",
        var comment: String? = null, var substituteId: String? = null, val warnings: List<String> = emptyList(), val replaces: String? = null,
    )

    private val types = listOf(
        type(VACATION, "vacation", "Urlaub", "U", "#43A047", "vacation", approval = true, counts = true),
        type(SCHOOL, "school", "Berufsschule", "B", "#1E88E5", "school", approval = false, counts = false),
        type("01a10000-0000-7000-8000-000000000005", "training", "Fortbildung", "F", "#00ACC1", "training", approval = true, counts = false),
        type(SPECIAL, "special_leave", "Sonderurlaub", "SU", "#8E24AA", "specialLeave", approval = true, counts = false),
        type(OVERTIME, "overtime", "Überstundenabbau", "ÜA", "#FB8C00", "other", approval = true, counts = false),
    )

    private fun type(id: String, key: String, name: String, code: String, color: String, kind: String, approval: Boolean, counts: Boolean) = mapOf(
        "id" to id, "key" to key, "name" to name, "code" to code, "color" to color, "kind" to kind, "requiresApproval" to approval,
        "countsAsVacation" to counts, "requestable" to true, "isPaid" to true, "isActive" to true, "sortOrder" to 10, "isSystem" to true, "isSickness" to false,
    )

    private fun monday(weeks: Long): LocalDate = today.plusWeeks(weeks).with(TemporalAdjusters.nextOrSame(DayOfWeek.MONDAY))

    private val requests = mutableListOf(
        Request("0193a1b2-ab00-7000-8000-000000000001", DemoData.USER_ID, DemoData.USER_NAME, monday(9).toString(), monday(9).plusDays(11).toString(),
            VACATION, note = "Familienurlaub", status = "approved", substituteId = LUKAS),
        Request("0193a1b2-ab00-7000-8000-000000000002", DemoData.USER_ID, DemoData.USER_NAME, monday(3).plusDays(4).toString(), monday(3).plusDays(4).toString(),
            OVERTIME, halfDayStart = true),
        Request("0193a1b2-ab00-7000-8000-000000000003", LUKAS, "Lukas Brandl", monday(6).toString(),
            monday(6).plusDays(4).toString(), VACATION, note = "Hochzeit",
            warnings = listOf("Kolonne Huber: am ${AbsenceText.date(monday(6).toString()).take(6)} nur 2 von 4 anwesend (Mindestbesetzung 3).",
                "Im Zeitraum sind 3 Einsätze eingeplant.")),
        Request("0193a1b2-ab00-7000-8000-000000000004", SABINE, "Sabine Wagner", monday(8).plusDays(1).toString(),
            monday(8).plusDays(2).toString(), SPECIAL, note = "Umzug"),
    )

    private fun holidays(from: String, to: String): List<AbsenceHoliday> {
        val start = runCatching { LocalDate.parse(from) }.getOrNull() ?: return emptyList()
        val end = runCatching { LocalDate.parse(to) }.getOrNull() ?: return emptyList()
        return if (end < start || end.year - start.year > 1) emptyList() else GermanHolidays.between(start, end, STATE)
    }

    /** Arbeitstage Montag bis Freitag ohne Feiertage; halbe Tage am Anfang bzw. Ende (wie VacationCalculator im Server). */
    fun workingDays(from: String, to: String, halfStart: Boolean, halfEnd: Boolean): Double {
        val start = runCatching { LocalDate.parse(from) }.getOrNull() ?: return 0.0
        val end = runCatching { LocalDate.parse(to) }.getOrNull() ?: return 0.0
        val free = holidays(from, to).map { it.date }.toSet()
        var days = 0.0
        var d = start
        while (!d.isAfter(end)) {
            if (d.dayOfWeek != DayOfWeek.SATURDAY && d.dayOfWeek != DayOfWeek.SUNDAY && d.toString() !in free) {
                days += if ((d == start && halfStart) || (d == end && halfEnd && start != end)) 0.5 else 1.0
            }
            d = d.plusDays(1)
        }
        return days
    }

    private fun typeOf(id: String) = types.firstOrNull { it["id"] == id }

    private fun view(r: Request): Map<String, Any?> {
        val mine = r.userId == DemoData.USER_ID
        val type = typeOf(r.typeId)
        return mapOf(
            "id" to r.id, "userId" to r.userId, "userName" to r.userName, "from" to r.from, "to" to r.to, "kind" to type?.get("kind"), "note" to r.note,
            "status" to r.status, "workingDays" to workingDays(r.from, r.to, r.halfDayStart, r.halfDayEnd), "decisionComment" to r.comment,
            "warnings" to r.warnings, "remainingAfter" to if (mine) null else 14.0, "canDecide" to (!mine && r.status == "pending"),
            "canCancel" to (mine && (r.status == "pending" || r.status == "approved")), "typeId" to r.typeId, "typeName" to type?.get("name"),
            "typeColor" to type?.get("color"), "halfDayStart" to r.halfDayStart, "halfDayEnd" to r.halfDayEnd, "substituteUserId" to r.substituteId,
            "substituteName" to r.substituteId?.let { PEOPLE[it] }, "replacesRequestId" to r.replaces,
            "canChange" to (mine && r.status == "approved" && LocalDate.parse(r.from).isAfter(today) && requests.none { it.replaces == r.id && it.status == "pending" }),
        )
    }

    private fun sum(status: String) = requests.filter { it.userId == DemoData.USER_ID && it.status == status && it.typeId == VACATION }
        .sumOf { workingDays(it.from, it.to, it.halfDayStart, it.halfDayEnd) }

    private fun mine(): Map<String, Any?> {
        val planned = sum("approved")
        val requested = sum("pending")
        val remaining = 30 + 4 - 6.0 - planned
        val own = requests.filter { it.userId == DemoData.USER_ID }
        return mapOf(
            "employeeId" to "0193a1b2-ab20-7000-8000-000000000001", "year" to today.year, "canApprove" to true,
            "pendingApprovals" to requests.count { it.userId != DemoData.USER_ID && it.status == "pending" },
            "vacation" to mapOf("year" to today.year, "entitlement" to 30, "carryOver" to 4, "taken" to 6, "planned" to planned, "remaining" to remaining,
                "sickDays" to 0, "requested" to requested, "annualEntitlement" to 30, "adjustment" to 0, "carryOverExpiresOn" to "${today.year + 1}-03-31",
                "carryOverExpired" to 0, "carryOverExpiring" to 0, "remainingAfterRequests" to remaining - requested),
            "requests" to own.map(::view), "holidays" to GermanHolidays.of(today.year, STATE).map { mapOf("date" to it.date, "name" to it.name) }, "types" to types,
            "absences" to own.filter { it.status == "approved" }.map {
                mapOf("id" to "a-${it.id}", "from" to it.from, "to" to it.to, "typeId" to it.typeId, "typeName" to typeOf(it.typeId)?.get("name"),
                    "color" to typeOf(it.typeId)?.get("color"), "halfDayStart" to it.halfDayStart, "halfDayEnd" to it.halfDayEnd,
                    "workingDays" to workingDays(it.from, it.to, it.halfDayStart, it.halfDayEnd))
            },
        )
    }

    fun handle(method: String, path: String, query: Map<String, String?>, body: JsonObject): Pair<Int, Any?> {
        val parts = path.split('/')
        return when {
            path == "hr/me" -> 200 to mine()
            path == "hr/absence-types" -> 200 to types
            path == "hr/absence-requests" -> 200 to requests.filter { query["scope"] != "pending" || it.status == "pending" }.map(::view)
            path == "hr/absence-requests/preview" -> {
                val from = body.str("from") ?: ""
                val to = body.str("to") ?: ""
                val days = workingDays(from, to, body.bool("halfDayStart") == true, body.bool("halfDayEnd") == true)
                val typeId = body.str("typeId") ?: VACATION
                val replaced = body.str("replacesRequestId")?.let { id -> requests.firstOrNull { it.id == id && it.typeId == VACATION } }
                val freed = replaced?.let { workingDays(it.from, it.to, it.halfDayStart, it.halfDayEnd) } ?: 0.0
                val warnings = buildList {
                    if (days > 3) add("Kolonne Huber: 2 von 4 wären gleichzeitig abwesend.")
                    if (body.str("substituteUserId") == LUKAS && overlaps(from, to, requests.first { it.userId == LUKAS })) add("Die Vertretung ist im Zeitraum selbst abwesend.")
                }
                200 to mapOf("workingDays" to days, "warnings" to warnings,
                    "remainingAfter" to if (typeId == VACATION) 30 + 4 - 6.0 - sum("approved") - sum("pending") - days + freed else null,
                    "requiresApproval" to (typeOf(typeId)?.get("requiresApproval") as? Boolean ?: true),
                    "holidays" to holidays(from, to).map { mapOf("date" to it.date, "name" to it.name) })
            }
            parts.size == 3 && parts[1] == "absence-requests" && method == "PUT" -> {
                val typeId = body.str("typeId") ?: VACATION
                val approval = typeOf(typeId)?.get("requiresApproval") as? Boolean ?: true
                val replaces = body.str("replacesRequestId")
                if (replaces != null && requests.none { it.id == replaces && it.status == "approved" && it.userId == DemoData.USER_ID }) {
                    return 422 to json("status" to 422, "title" to "Ein Änderungsantrag bezieht sich auf einen genehmigten Antrag.", "code" to "VALIDATION_FAILED")
                }
                val r = Request(parts[2], DemoData.USER_ID, DemoData.USER_NAME, body.str("from") ?: today.toString(), body.str("to") ?: today.toString(), typeId,
                    body.bool("halfDayStart") == true, body.bool("halfDayEnd") == true, body.str("note"), if (approval) "pending" else "approved",
                    if (approval) null else "Keine Genehmigung erforderlich.", body.str("substituteUserId")?.takeIf { it in PEOPLE && it != DemoData.USER_ID },
                    replaces = replaces)
                requests.removeAll { it.id == r.id }
                requests.add(0, r)
                if (r.status == "approved") replace(r)
                200 to view(r)
            }
            parts.size == 4 && parts[1] == "absence-requests" -> {
                val r = requests.firstOrNull { it.id == parts[2] } ?: return 404 to json("status" to 404, "title" to "Antrag nicht gefunden", "code" to "NOT_FOUND")
                when (parts[3]) {
                    "decision" -> {
                        r.status = if (body.bool("approve") == true) "approved" else "rejected"
                        r.comment = body.str("comment")
                        if (r.status == "approved") replace(r)
                    }
                    "cancel" -> r.status = "cancelled"
                }
                200 to view(r)
            }
            else -> 404 to json("status" to 404, "title" to "Nicht gefunden", "code" to "NOT_FOUND")
        }
    }

    /** Genehmigter Änderungsantrag ersetzt den bisherigen Antrag. */
    private fun replace(r: Request) {
        val old = requests.firstOrNull { it.id == r.replaces } ?: return
        old.status = "cancelled"
        old.comment = "Durch Änderungsantrag ersetzt."
    }

    private fun overlaps(from: String, to: String, other: Request) = from <= other.to && to >= other.from

    /** Kollegen für die Auswahl der Vertretung (directory/users). */
    fun directory(): List<Map<String, Any?>> = PEOPLE.map { (id, name) -> mapOf("id" to id, "displayName" to name, "isActive" to true) }

    companion object {
        /** Bundesland des Demobetriebs (Landshut). */
        const val STATE = "BY"
        const val LUKAS = "0193a1b2-0000-7000-8000-00000000bbbb"
        const val SABINE = "0193a1b2-de00-7000-8000-00000000a0a2"
        val PEOPLE = linkedMapOf(
            DemoData.USER_ID to DemoData.USER_NAME, LUKAS to "Lukas Brandl", SABINE to "Sabine Wagner",
            "0193a1b2-de00-7000-8000-00000000a0a4" to "Anna Lechner", "0193a1b2-de00-7000-8000-00000000a0a5" to "Jonas Wirth",
        )
        const val VACATION = "01a10000-0000-7000-8000-000000000001"
        const val SCHOOL = "01a10000-0000-7000-8000-000000000004"
        const val SPECIAL = "01a10000-0000-7000-8000-000000000006"
        const val OVERTIME = "01a10000-0000-7000-8000-000000000007"
    }
}
