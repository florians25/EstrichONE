package de.estrichone.core

import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import java.time.DayOfWeek
import java.time.Instant
import java.time.LocalDate
import java.time.LocalTime

/**
 * Planung im Demomodus (wie DemoPlanning.swift): Kolonnen, Mitarbeiter, Fahrzeuge und Einsätze im Arbeitsspeicher.
 * Beantwortet dieselben Endpunkte wie der Server (`/planning/day`, `/planning/batch`, Reihenfolge, Tausch, Vorrat,
 * Fahrzeuge, Freigabe), damit sich „Wer fährt wohin“ und die Schnelldisposition ohne Server ausprobieren lassen.
 * Aufruf nur unter der Sperre des DemoServers.
 */
internal class DemoPlanning(private val today: LocalDate = LocalDate.now(Dates.zone), private val nowTime: () -> Instant = Instant::now) {
    private data class Site(
        val number: String, val title: String, val address: String, val latitude: Double?, val longitude: Double?, val contactName: String?,
        val contactPhone: String?, val city: String, val area: Double, val status: String,
    )

    private data class Crew(val id: String, val name: String, val color: String, val leader: String, val members: List<String>, val vehicles: List<String>)

    private data class Assignment(
        var day: String, var crewId: String?, val bauakteId: String, var start: String?, var end: String?, var activity: String, var notes: String?,
        var sequence: Int, var version: Int,
    )

    private data class Roster(val members: List<String>, val vehicles: List<String>, val changed: Boolean)

    private data class ReleaseInfo(val at: Instant, val by: String, val note: String?)

    private val people = linkedMapOf(
        DemoData.USER_ID to DemoData.USER_NAME, id(4002) to "Lukas Brandl", id(4003) to "Anna Koller", id(4004) to "Tobias Lang",
        id(4005) to "Emre Kaya", id(4006) to "Jonas Wirth", id(4007) to "Felix Gruber", id(4008) to "Stefan Moser",
    )
    private val vehicles = linkedMapOf(
        id(5001) to Triple("Estrichpumpe 1", "machine", "Mixokret M3"), id(5002) to Triple("Sprinter LA-EO 12", "vehicle", "LA-EO 12"),
        id(5003) to Triple("Pritsche LA-EO 7", "vehicle", "LA-EO 7"), id(5004) to Triple("Estrichpumpe 2", "machine", "Brinkmann 450"),
    )
    private val sites = linkedMapOf(
        DemoData.EFH to Site("2026-041", "EFH Schneider, Neubau", "Ahornweg 3, 84028 Landshut", 48.5372, 12.1521, "Herr Schneider", "0171 2345678",
            "Landshut", 142.5, "inProgress"),
        DemoData.HALLE to Site("2026-042", "Halle Gewerbering", "Gewerbering 3, 84030 Ergolding", null, null, "Bauleitung Huber Bau", "0871 4711",
            "Ergolding", 860.0, "planned"),
        DemoData.PRAXIS to Site("2026-038", "Praxis Dr. Maier, Sanierung", "Altstadt 12, 84028 Landshut", null, null, "Dr. Maier", "0871 22 33 4",
            "Landshut", 96.0, "drying"),
        DemoData.ISARPARK to Site("2026-044", "MFH Isarpark, Haus B", "Am Isarpark 7, 84034 Landshut", null, null, null, null, "Landshut", 512.0, "planned"),
        DemoData.WEBER to Site("2026-047", "Anbau Weber", "Lindenstr. 8, 84137 Vilsbiburg", null, null, "Thomas Weber", "0160 998877", "Vilsbiburg",
            38.0, "inquiry"),
    )
    private val crews = listOf(
        Crew(id(6001), "Kolonne Huber", "#E07A1F", DemoData.USER_ID, listOf(DemoData.USER_ID, id(4002)), listOf(id(5001), id(5003))),
        Crew(id(6002), "Kolonne Brandl", "#2F80ED", id(4003), listOf(id(4003), id(4004)), listOf(id(5002))),
        Crew(id(6003), "Kolonne Kaya", "#3A9D6B", id(4005), listOf(id(4005), id(4006)), listOf(id(5004))),
    )

    /** Tagesbesetzung abweichend von der Stammbesetzung: „crewId|Tag“. */
    private val rosters = HashMap<String, Pair<List<String>, List<String>>>()
    private val assignments = LinkedHashMap<String, Assignment>()

    /** Freigegebener Stand: Einsatz → Version. */
    private val released = HashMap<String, Int>()
    private val releases = HashMap<String, ReleaseInfo>()

    init {
        fun day(offset: Long) = today.plusDays(offset).toString()
        val sixThirty = today.atTime(LocalTime.of(6, 30)).atZone(Dates.zone).toInstant()
        releases[day(0)] = ReleaseInfo(sixThirty, "Sabine Wagner (Büro)", null)
        fun add(number: Int, offset: Long, crew: Int?, site: String, start: String?, end: String?, activity: String, notes: String? = null, sequence: Int) {
            assignments[id(number)] = Assignment(day(offset), crew?.let { id(it) }, site, start, end, activity, notes, sequence, 1)
        }
        add(3001, 0, 6001, DemoData.EFH, "07:00:00", "13:00:00", "Estrich einbringen OG", notes = "Pumpe vorher prüfen, Zufahrt über Hof", sequence = 1)
        add(3008, 0, 6001, DemoData.PRAXIS, "14:00:00", "15:30:00", "CM-Messung und Belegreife", sequence = 2)
        add(3002, 0, 6002, DemoData.HALLE, "08:30:00", "09:30:00", "Erstbegehung", notes = "Zufahrt nur bis 7,5 t", sequence = 1)
        add(3004, 0, 6003, DemoData.ISARPARK, "07:00:00", "16:00:00", "Randdämmung und Folie", sequence = 1)
        add(3003, 1, 6001, DemoData.PRAXIS, "07:30:00", "09:00:00", "CM-Messung und Belegreife", sequence = 1)
        add(3005, 1, 6001, DemoData.EFH, "10:30:00", "14:00:00", "Estrich OG nacharbeiten", sequence = 2)
        add(3009, 1, 6003, DemoData.ISARPARK, "07:00:00", null, "Estrich einbringen Haus B", sequence = 1)
        add(3006, 2, 6001, DemoData.ISARPARK, "07:00:00", "09:00:00", "Erstbegehung", sequence = 1)
        add(3007, 3, 6002, DemoData.HALLE, "06:30:00", null, "Estrich einbringen", sequence = 1)
        assignments.filter { it.value.day == day(0) && it.key != id(3008) }.forEach { released[it.key] = it.value.version }
        // Freigegeben, danach im Büro entfernt (erscheint als „Entfallen“).
        released[id(3010)] = 1
    }

    /** Beantwortet `planning/…` (außer `planning/mine` und `planning/dispatch/…`); `null` = nicht zuständig. */
    fun handle(method: String, parts: List<String>, body: JsonObject): Pair<Int, Any?>? {
        if (parts.firstOrNull() != "planning") return null
        val p = parts.drop(1)
        return when {
            method == "GET" && p.size == 2 && p[0] == "day" -> 200 to dayPlan(normalizeDay(p[1]))
            method == "GET" && p == listOf("backlog") -> 200 to backlog()
            method == "GET" && p == listOf("resources") -> 200 to resources()
            method == "POST" && p == listOf("batch") -> batch(body)
            method == "PUT" && p.size == 5 && p[0] == "crews" && p[4] == "order" -> 200 to order(p[3], body)
            method == "POST" && p.size == 5 && p[0] == "crews" && p[4] == "swap" -> 200 to swap(p[1], p[3], body)
            method == "POST" && p.size == 3 && p[0] == "days" && p[2] == "swap-crews" -> 200 to swapCrews(p[1], body)
            method == "POST" && p == listOf("releases") -> release(body)
            method == "DELETE" && p.size == 2 && p[0] == "releases" -> { releases.remove(p[1]); 204 to null }
            method == "POST" && p.size == 3 && p[0] == "assignments" && p[2] == "next-free-day" -> nextFreeDay(p[1].lowercase())
            else -> null
        }
    }

    private fun normalizeDay(day: String) = when (day) {
        "heute" -> today.toString()
        "morgen" -> today.plusDays(1).toString()
        else -> day
    }

    // Lesen

    private fun absence(person: String, day: String): String? =
        if (person == id(4004) && (day == today.toString() || day == today.plusDays(1).toString())) "Urlaub" else null

    private fun roster(crew: Crew, day: String): Roster =
        rosters["${crew.id}|$day"]?.let { Roster(it.first, it.second, true) } ?: Roster(crew.members, crew.vehicles, false)

    private fun warnings(crew: Crew, day: String): List<String> {
        val result = roster(crew, day).members.mapNotNull { person -> absence(person, day)?.let { "${people[person]} ist im $it." } }.toMutableList()
        val own = assignments.values.filter { it.crewId == crew.id && it.day == day }.sortedBy { it.start ?: "" }
        own.zipWithNext().forEach { (first, second) ->
            val end = first.end
            val start = second.start
            if (end != null && start != null && start < end) result += "Überschneidung: ${sites[first.bauakteId]?.title} und ${sites[second.bauakteId]?.title}."
        }
        return result
    }

    private fun stop(id: String, a: Assignment, order: Int, crew: Crew?): Map<String, Any?> {
        val site = sites[a.bauakteId]
        val change = if (releases[a.day] == null) null else when (released[id]) {
            null -> "new"
            a.version -> null
            else -> "changed"
        }
        val members = crew?.let { roster(it, a.day).members }.orEmpty()
        val cars = crew?.let { roster(it, a.day).vehicles }.orEmpty()
        return mapOf(
            "assignmentId" to id, "order" to order, "sequence" to a.sequence, "startTime" to a.start, "endTime" to a.end, "kind" to "screed",
            "activity" to a.activity, "bauakteId" to a.bauakteId, "number" to site?.number, "title" to (site?.title ?: ""), "address" to site?.address,
            "latitude" to site?.latitude, "longitude" to site?.longitude, "siteContactName" to site?.contactName, "siteContactPhone" to site?.contactPhone,
            "navigationUrl" to null, "notes" to a.notes,
            "people" to members.map { mapOf("id" to it, "name" to people[it]) },
            "resources" to cars.map { mapOf("id" to it, "name" to vehicles[it]?.first, "kind" to vehicles[it]?.second) },
            "warnings" to emptyList<String>(), "change" to change, "version" to a.version,
        )
    }

    fun dayPlan(day: String): JsonObject {
        val crewJson = crews.map { crew ->
            val roster = roster(crew, day)
            val own = assignments.entries.filter { it.value.crewId == crew.id && it.value.day == day }
                .sortedWith(compareBy({ it.value.sequence }, { it.value.start ?: "" }))
            mapOf(
                "id" to crew.id, "name" to crew.name, "color" to crew.color, "leaderName" to people[crew.leader],
                "members" to roster.members.map { mapOf("id" to it, "name" to people[it], "absenceNote" to absence(it, day)) },
                "vehicles" to roster.vehicles.map { mapOf("id" to it, "name" to vehicles[it]?.first, "kind" to vehicles[it]?.second) },
                "rosterChanged" to roster.changed, "rosterNote" to null, "isMine" to (DemoData.USER_ID in roster.members),
                "stops" to own.mapIndexed { index, entry -> stop(entry.key, entry.value, index + 1, crew) },
                "warnings" to warnings(crew, day),
            )
        }
        val rostered = crews.flatMap { roster(it, day).members }.toSet()
        val free = people.keys.filter { it !in rostered && absence(it, day) == null }.sortedBy { people[it] }
        val absent = people.keys.filter { absence(it, day) != null }
        val changed = assignments.count { it.value.day == day && released[it.key] != it.value.version }
        val removed = if (day == today.toString() && releases[day] != null) {
            released.keys.filter { it !in assignments }.map {
                mapOf("assignmentId" to it, "version" to 1, "title" to "Anbau Weber", "crewName" to "Kolonne Kaya", "activity" to "Erstbegehung")
            }
        } else {
            emptyList()
        }
        val release = releases[day]?.let { info ->
            mapOf("day" to day, "releasedAt" to ApiDate.format(info.at), "releasedByName" to info.by, "note" to info.note,
                "changedCount" to changed + removed.size, "removed" to removed)
        }
        return json(
            "day" to day, "holiday" to null, "release" to release, "canPlan" to true,
            "myCrewIds" to crews.filter { DemoData.USER_ID in roster(it, day).members }.map { it.id },
            "crews" to crewJson, "withoutCrew" to emptyList<Any>(),
            "absent" to absent.map { mapOf("id" to it, "name" to people[it], "absenceNote" to absence(it, day)) },
            "free" to free.map { mapOf("id" to it, "name" to people[it]) },
        )
    }

    fun backlog(): List<Map<String, Any?>> = sites.map { (id, site) ->
        val upcoming = assignments.values.filter { it.bauakteId == id && it.day >= today.toString() }
        val parts = site.address.split(",").map { it.trim() }
        mapOf(
            "bauakteId" to id, "number" to site.number, "title" to site.title, "status" to site.status, "street" to parts.firstOrNull(),
            "postalCode" to parts.lastOrNull()?.take(5), "city" to site.city, "plannedStart" to null, "plannedEnd" to null, "customerName" to null,
            "totalAreaSquareMeters" to site.area, "screedTypes" to emptyList<String>(), "upcomingAssignments" to upcoming.size,
            "nextAssignment" to upcoming.minOfOrNull { it.day },
        )
    }

    fun resources(): List<Map<String, Any?>> = vehicles.map { (id, v) ->
        mapOf("id" to id, "kind" to v.second, "name" to v.first, "identifier" to v.third, "notes" to null, "isActive" to true, "version" to 1)
    }

    // Ändern

    /** `POST /planning/batch`: Upsert, Move, Delete; `dryRun` prüft nur. */
    private fun batch(body: JsonObject): Pair<Int, Any?> {
        val operations = body.arr("operations")?.mapNotNull { it as? JsonObject }.orEmpty()
        val dryRun = body.bool("dryRun") ?: false
        val backup = assignments.mapValues { it.value.copy() }
        val touched = mutableListOf<Pair<String?, String>>()
        for (op in operations) {
            val id = op.str("id")?.lowercase() ?: continue
            when (op.str("op")) {
                "upsert" -> {
                    val a = op.obj("assignment") ?: JsonObject(emptyMap())
                    val day = a.str("day") ?: today.toString()
                    val crew = a.str("crewId")?.lowercase()
                    val sequence = (assignments.values.filter { it.crewId == crew && it.day == day }.maxOfOrNull { it.sequence } ?: 0) + 1
                    assignments[id] = Assignment(day, crew, (a.str("bauakteId") ?: "").lowercase(), a.str("startTime"), a.str("endTime"),
                        a.str("activity") ?: "Estrich einbringen", a.str("notes"), sequence, 1)
                    touched += crew to day
                }
                "move" -> {
                    val a = assignments[id] ?: return notFound(if (dryRun) backup else null)
                    op.str("day")?.let { a.day = it }
                    if (op.bool("changeCrew") == true) a.crewId = op.str("crewId")?.lowercase()
                    if (op.bool("setTime") == true) {
                        a.start = op.str("startTime")
                        a.end = op.str("endTime")
                    }
                    a.sequence = 99
                    a.version += 1
                    touched += a.crewId to a.day
                }
                "delete" -> if (assignments.remove(id) == null) return notFound(if (dryRun) backup else null)
            }
        }
        val found = touched.flatMap { (crewId, day) -> crews.firstOrNull { it.id == crewId }?.let { warnings(it, day) }.orEmpty() }.distinct().sorted()
        if (dryRun) {
            assignments.clear()
            assignments.putAll(backup)
        }
        return 200 to json("assignments" to emptyList<Any>(), "deletedIds" to emptyList<String>(), "warnings" to found, "dryRun" to dryRun)
    }

    private fun notFound(restore: Map<String, Assignment>?): Pair<Int, Any?> {
        if (restore != null) {
            assignments.clear()
            assignments.putAll(restore)
        }
        return 404 to json("status" to 404, "title" to "Nicht gefunden", "detail" to "Der Einsatz existiert nicht (mehr).", "code" to "NOT_FOUND")
    }

    /** `PUT /planning/crews/{id}/days/{day}/order`. */
    private fun order(day: String, body: JsonObject): JsonObject {
        val ids = body.strings("assignmentIds").map { it.lowercase() }
        val times = body.arr("startTimes")
        ids.forEachIndexed { index, id ->
            val a = assignments[id] ?: return@forEachIndexed
            a.sequence = index + 1
            val time = (times?.getOrNull(index) as? JsonPrimitive)?.takeIf { it.isString }?.content
            if (time != null && time != a.start) {
                val duration = minutes(a.end)?.let { endMinutes -> endMinutes - (minutes(a.start) ?: endMinutes) }
                a.start = time
                a.end = duration?.let { d -> minutes(time)?.let { time(it + d) } } ?: a.end
                a.version += 1
            }
        }
        return dayPlan(day)
    }

    /** `POST /planning/crews/{id}/days/{day}/swap`: Person oder Fahrzeug für den Tag tauschen (mit Gegentausch). */
    private fun swap(crewId: String, day: String, body: JsonObject): JsonObject {
        val crew = crews.firstOrNull { it.id == crewId } ?: return dayPlan(day)
        val current = roster(crew, day)
        val members = current.members.toMutableList()
        val cars = current.vehicles.toMutableList()
        fun value(key: String) = body.str(key)?.lowercase()
        val fromUser = value("fromUserId")
        val toUser = value("toUserId")
        if (fromUser != null) members.remove(fromUser)
        if (toUser != null) {
            crews.firstOrNull { it.id != crewId && toUser in roster(it, day).members }?.let { other ->
                val theirs = roster(other, day)
                rosters["${other.id}|$day"] = (theirs.members - toUser + listOfNotNull(fromUser)) to theirs.vehicles
            }
            members += toUser
        }
        val fromResource = value("fromResourceId")
        val toResource = value("toResourceId")
        if (fromResource != null) cars.remove(fromResource)
        if (toResource != null) {
            crews.filter { it.id != crewId && toResource in roster(it, day).vehicles }.forEach { other ->
                val theirs = roster(other, day)
                rosters["${other.id}|$day"] = theirs.members to (theirs.vehicles - toResource + listOfNotNull(fromResource))
            }
            cars += toResource
        }
        rosters["$crewId|$day"] = members to cars
        return dayPlan(day)
    }

    /** `POST /planning/releases`: hält den Stand der Tage `from` bis `to` fest. */
    private fun release(body: JsonObject): Pair<Int, Any?> {
        val from = ApiDate.day(body.str("from"))
        val to = ApiDate.day(body.str("to"))
        if (from == null || to == null || to < from) {
            return 422 to json("status" to 422, "title" to "Ungültige Angaben", "detail" to "Zeitraum prüfen.", "code" to "VALIDATION_FAILED")
        }
        val note = body.str("note")?.takeIf { it.isNotBlank() }
        val now = nowTime()
        val infos = mutableListOf<Map<String, Any?>>()
        var date: LocalDate = from
        while (date <= to && infos.size < 62) {
            val day = date.toString()
            releases[day] = ReleaseInfo(now, DemoData.USER_NAME, note)
            assignments.filter { it.value.day == day }.forEach { released[it.key] = it.value.version }
            if (day == today.toString()) released.remove(id(3010))
            infos += mapOf("day" to day, "releasedAt" to ApiDate.format(now), "releasedByName" to DemoData.USER_NAME, "note" to note,
                "changedCount" to 0, "removed" to emptyList<Any>())
            date = date.plusDays(1)
        }
        return 200 to infos
    }

    /** `POST /planning/assignments/{id}/next-free-day`: nächster Arbeitstag (Montag bis Freitag), an dem die Kolonne nichts hat. */
    private fun nextFreeDay(id: String): Pair<Int, Any?> {
        val a = assignments[id] ?: return notFound(null)
        val crew = a.crewId ?: return 422 to json("status" to 422, "title" to "Ungültige Angaben", "detail" to "Dieser Einsatz hat keine Kolonne.",
            "code" to "VALIDATION_FAILED")
        var date = ApiDate.day(a.day) ?: today
        repeat(90) {
            date = date.plusDays(1)
            val day = date.toString()
            val weekend = date.dayOfWeek == DayOfWeek.SATURDAY || date.dayOfWeek == DayOfWeek.SUNDAY
            if (!weekend && assignments.values.none { it.crewId == crew && it.day == day }) {
                a.day = day
                a.sequence = 1
                a.version += 1
                return 200 to json("id" to id, "day" to day, "bauakteId" to a.bauakteId, "activity" to a.activity, "crewId" to crew, "version" to a.version)
            }
        }
        return 422 to json("status" to 422, "title" to "Ungültige Angaben", "detail" to "In den nächsten 90 Tagen ist kein Tag frei.", "code" to "VALIDATION_FAILED")
    }

    /** `POST /planning/days/{day}/swap-crews`: zwei Kolonnen tauschen am Tag ihre Baustellen. */
    private fun swapCrews(day: String, body: JsonObject): JsonObject {
        val first = body.str("crewA")?.lowercase()
        val second = body.str("crewB")?.lowercase()
        assignments.values.filter { it.day == day && (it.crewId == first || it.crewId == second) }.forEach {
            it.crewId = if (it.crewId == first) second else first
            it.version += 1
        }
        return dayPlan(day)
    }

    private fun minutes(time: String?): Int? {
        val parts = time?.split(':')?.mapNotNull { it.toIntOrNull() } ?: return null
        return if (parts.size >= 2) parts[0] * 60 + parts[1] else null
    }

    private fun time(minutes: Int): String = "%02d:%02d:00".format(minOf(23, minutes / 60), minutes % 60)

    companion object {
        fun id(number: Int): String = "0193a1b2-de00-7000-8000-00000000%04d".format(number)
    }
}
