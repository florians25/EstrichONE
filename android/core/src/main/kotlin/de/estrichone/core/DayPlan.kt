package de.estrichone.core

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable
import kotlinx.serialization.builtins.ListSerializer
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import java.time.Instant
import java.time.LocalDate
import java.time.LocalTime
import java.time.temporal.ChronoUnit

// „Wer fährt wohin“ und Schnelldisposition (wie EstrichCore Planning/DayPlan.swift): dieselben Endpunkte wie das
// Disponenten-Cockpit im Web (docs/planung-api.md). Lesen mit planning.read, Ändern mit planning.manage.

/** Mitarbeiter einer Kolonne am Tag; [absenceNote] = Abwesenheit oder Termin („Urlaub“), leer = verfügbar. */
@Serializable
data class DayPlanMember(val id: String, val name: String = "", val absenceNote: String? = null)

/** Person, Fahrzeug oder Gerät; [kind] nur bei Fahrzeugen und Geräten (`vehicle`, `machine`). */
@Serializable
data class DayPlanRef(val id: String, val name: String = "", val kind: String? = null)

/** Baustelle in der Fahrtreihenfolge einer Kolonne ([order] 1 = erste Baustelle). */
@Serializable
data class DayPlanStop(
    val assignmentId: String,
    val order: Int = 0,
    val startTime: String? = null,
    val endTime: String? = null,
    val kind: String? = null,
    val activity: String = "",
    val bauakteId: String,
    val number: String? = null,
    val title: String = "",
    val address: String? = null,
    val latitude: Double? = null,
    val longitude: Double? = null,
    val siteContactName: String? = null,
    val siteContactPhone: String? = null,
    val navigationUrl: String? = null,
    val notes: String? = null,
    val people: List<DayPlanRef> = emptyList(),
    val resources: List<DayPlanRef> = emptyList(),
    val warnings: List<String> = emptyList(),
    /** `new` oder `changed` seit der Freigabe. */
    val change: String? = null,
    val version: Long? = null,
) {
    val startText: String? get() = Dates.clock(startTime)
    val endText: String? get() = Dates.clock(endTime)

    /** „07:00 bis 12:00“, „ab 07:00“ oder „ganztägig“. */
    val timeText: String
        get() = when {
            startText != null && endText != null -> "$startText bis $endText"
            startText != null -> "ab $startText"
            else -> "ganztägig"
        }

    val isChanged: Boolean get() = change != null

    /** „Neu“ bzw. „Geändert“ seit der Freigabe. */
    val changeLabel: String? get() = change?.let { if (it == "new") "Neu" else "Geändert" }

    /** „2026-041, EFH Schneider, Neubau“. */
    val heading: String get() = listOfNotNull(number, title.takeIf { it.isNotBlank() }).joinToString(", ")
}

@Serializable
data class DayPlanCrew(
    val id: String,
    val name: String = "",
    val color: String? = null,
    val leaderName: String? = null,
    val members: List<DayPlanMember> = emptyList(),
    val vehicles: List<DayPlanRef> = emptyList(),
    /** Tagesbesetzung weicht von der Stammbesetzung ab. */
    val rosterChanged: Boolean = false,
    val rosterNote: String? = null,
    val isMine: Boolean = false,
    val stops: List<DayPlanStop> = emptyList(),
    val warnings: List<String> = emptyList(),
) {
    val available: List<DayPlanMember> get() = members.filter { it.absenceNote == null }
    val absent: List<DayPlanMember> get() = members.filter { it.absenceNote != null }

    /** Konflikte der Kolonne und ihrer Baustellen (Doppelbelegung, Abwesenheit, Gerät belegt), ohne Wiederholungen. */
    val conflicts: List<String> get() = (warnings + stops.flatMap { it.warnings }).distinct()

    /** Besetzung ohne Abwesende, Leitung gekennzeichnet: „Max Huber (Leitung), Lukas Brandl“. */
    val memberText: String
        get() = available.joinToString(", ") { it.name + if (it.name == leaderName) " (Leitung)" else "" }
}

@Serializable
data class DayPlanRemoved(val assignmentId: String, val title: String = "", val crewName: String? = null, val activity: String = "")

@Serializable
data class DayPlanRelease(
    val releasedAt: String? = null,
    val releasedByName: String = "",
    val note: String? = null,
    /** Neue, geänderte und entfernte Einsätze seit der Freigabe. */
    val changedCount: Int = 0,
    val removed: List<DayPlanRemoved> = emptyList(),
) {
    val released: Instant? get() = ApiDate.parse(releasedAt)
}

/** Tagesplan (`GET /planning/day/{day}`): Kolonnen mit Besetzung, Fahrzeugen und Baustellen, Freigabestand. */
@Serializable
data class DayPlan(
    val day: String,
    val holiday: String? = null,
    val release: DayPlanRelease? = null,
    val canPlan: Boolean = false,
    val myCrewIds: List<String> = emptyList(),
    val crews: List<DayPlanCrew> = emptyList(),
    val withoutCrew: List<DayPlanStop> = emptyList(),
    val absent: List<DayPlanMember> = emptyList(),
    val free: List<DayPlanRef> = emptyList(),
) {
    /** Meine Kolonne zuerst, dann wie geliefert (Reihenfolge der Kolonnen im Büro). */
    val sortedCrews: List<DayPlanCrew> get() = crews.filter { it.isMine } + crews.filterNot { it.isMine }

    val stopCount: Int get() = withoutCrew.size + crews.sumOf { it.stops.size }

    fun crew(id: String?): DayPlanCrew? = crews.firstOrNull { it.id == id }

    /** Kolonne, in der ein Einsatz steht. */
    fun crewOf(assignmentId: String): DayPlanCrew? = crews.firstOrNull { crew -> crew.stops.any { it.assignmentId == assignmentId } }

    /** Baustellen je Kolonne nach Fahrtreihenfolge sortiert (wie geliefert, aber verlässlich). */
    fun normalized(): DayPlan = copy(crews = crews.map { crew -> crew.copy(stops = crew.stops.sortedBy { it.order }) })
}

// Uhrzeiten

/** Uhrzeiten der Planung: `HH:mm:ss` des Servers, Anzeige `HH:mm`. */
object PlanTime {
    fun parse(text: String?, fallback: LocalTime = LocalTime.of(7, 0)): LocalTime =
        text?.let { runCatching { LocalTime.parse(it.take(5)) }.getOrNull() } ?: fallback

    fun text(time: LocalTime): String = "%02d:%02d:00".format(time.hour, time.minute)

    fun clock(time: LocalTime): String = "%02d:%02d".format(time.hour, time.minute)

    /** Beginn nach der letzten Station der Kolonne (sonst 07:00). */
    fun suggestedStart(crew: DayPlanCrew?): LocalTime =
        crew?.stops?.mapNotNull { it.endTime ?: it.startTime }?.maxOrNull()?.let { parse(it) } ?: LocalTime.of(7, 0)

    /** Vorschlag für das Ende: vier Stunden nach Beginn, höchstens 23:00. */
    fun suggestedEnd(start: LocalTime): LocalTime = if (start.hour >= 19) LocalTime.of(23, 0) else start.plusHours(4)
}

// Änderungen der Schnelldisposition

/**
 * Eine Änderung der Schnelldisposition. Online sofort an den Server, ohne Verbindung vorgemerkt ([PlanningOutbox]) und
 * beim nächsten Laden in der gemerkten Reihenfolge gesendet.
 */
@Serializable
sealed class PlanChange {
    /** Baustelle einer Kolonne für den Tag zuweisen (neuer Einsatz mit Client-ID). */
    @Serializable
    @SerialName("assign")
    data class Assign(
        val id: String, val bauakteId: String, val title: String, val crewId: String, val day: String, val activity: String,
        val startTime: String? = null, val endTime: String? = null,
    ) : PlanChange()

    /** Einsatz auf anderen Tag, andere Kolonne und/oder andere Uhrzeit setzen ([day]/[crewId] leer = unverändert). */
    @Serializable
    @SerialName("move")
    data class Move(
        val id: String, val title: String, val day: String? = null, val crewId: String? = null, val startTime: String? = null,
        val endTime: String? = null, val setTime: Boolean = false,
    ) : PlanChange()

    /** Einsatz entfernen. */
    @Serializable
    @SerialName("remove")
    data class Remove(val id: String, val title: String) : PlanChange()

    /** Fahrtreihenfolge (und Uhrzeiten) der Baustellen einer Kolonne am Tag. */
    @Serializable
    @SerialName("order")
    data class Order(val crewId: String, val day: String, val assignmentIds: List<String>, val startTimes: List<String?> = emptyList()) : PlanChange()

    /** Mitarbeiter bzw. Fahrzeug der Kolonne für den Tag tauschen. */
    @Serializable
    @SerialName("swap")
    data class Swap(
        val crewId: String, val day: String, val fromUserId: String? = null, val toUserId: String? = null, val fromResourceId: String? = null,
        val toResourceId: String? = null, val text: String = "",
    ) : PlanChange()

    /** Einsatz auf den nächsten freien Arbeitstag seiner Kolonne legen. */
    @Serializable
    @SerialName("nextFreeDay")
    data class NextFreeDay(val id: String, val title: String) : PlanChange()

    /** Zwei Kolonnen tauschen am Tag ihre Baustellen. */
    @Serializable
    @SerialName("swapCrews")
    data class SwapCrews(val day: String, val crewA: String, val crewB: String, val text: String = "") : PlanChange()

    /** Kurzer Satz für die Liste der vorgemerkten Änderungen. */
    val summary: String
        get() = when (this) {
            is Assign -> "$title einplanen (${dayText(day)}${startTime?.let { ", ${it.take(5)}" } ?: ""})"
            is Move -> "$title verschieben${day?.let { " auf ${dayText(it)}" } ?: ""}${startTime?.let { ", ${it.take(5)}" } ?: ""}"
            is Remove -> "$title aus dem Plan nehmen"
            is Order -> "Reihenfolge von ${assignmentIds.size} Baustellen (${dayText(day)})"
            is Swap -> text
            is NextFreeDay -> "$title auf den nächsten freien Tag legen"
            is SwapCrews -> text
        }

    /** Tag dieser Änderung; bei Verschieben der Zieltag. */
    val targetDay: String?
        get() = when (this) {
            is Assign -> day
            is Move -> day
            is Remove -> null
            is Order -> day
            is Swap -> day
            is NextFreeDay -> null
            is SwapCrews -> day
        }

    /** Bestätigung nach dem Senden. */
    val doneText: String
        get() = when (this) {
            is Assign -> "Eingeplant"
            is Move -> "Verschoben"
            is Remove -> "Aus dem Plan genommen"
            is Order -> "Reihenfolge gesichert"
            is Swap -> "Besetzung geändert"
            is NextFreeDay -> "Auf den nächsten freien Tag gelegt"
            is SwapCrews -> "Kolonnen getauscht"
        }

    /** Operationen für `POST /planning/batch` (Upsert, Move, Delete); andere Änderungen haben eigene Endpunkte. */
    val batchOperations: List<JsonObject>?
        get() = when (this) {
            is Assign -> listOf(json(
                "op" to "upsert", "id" to id,
                "assignment" to json(
                    "bauakteId" to bauakteId, "day" to day, "startTime" to startTime, "endTime" to endTime, "activity" to activity,
                    "crewId" to crewId, "userIds" to null, "resourceIds" to null, "notes" to null,
                ),
            ))
            is Move -> {
                val op = linkedMapOf<String, JsonElement>("op" to JsonPrimitive("move"), "id" to JsonPrimitive(id), "day" to toJson(day))
                if (crewId != null) {
                    op["changeCrew"] = JsonPrimitive(true)
                    op["crewId"] = JsonPrimitive(crewId)
                }
                if (setTime) {
                    op["setTime"] = JsonPrimitive(true)
                    op["startTime"] = toJson(startTime)
                    op["endTime"] = toJson(endTime)
                }
                listOf(JsonObject(op))
            }
            is Remove -> listOf(json("op" to "delete", "id" to id))
            else -> null
        }

    companion object {
        /** „Mi. 26.11.“ */
        fun dayText(day: String): String = ApiDate.day(day)?.let { "${Dates.weekdayShort(it)} ${Dates.dayMonth(it)}" } ?: day
    }
}

/** Neue Reihenfolge einer Kolonne: Uhrzeiten bleiben in ihrer Reihenfolge, die Baustellen tauschen die Plätze. */
object PlanOrder {
    fun move(ids: List<String>, from: Int, to: Int): List<String> {
        if (from !in ids.indices || to !in ids.indices || from == to) return ids
        return ids.toMutableList().also { it.add(to, it.removeAt(from)) }
    }

    /** `null`, wenn sich nichts geändert hat. */
    fun change(crew: DayPlanCrew, day: String, newIds: List<String>): PlanChange.Order? {
        val old = crew.stops.map { it.assignmentId }
        if (newIds == old || newIds.toSet() != old.toSet()) return null
        return PlanChange.Order(crew.id, day, newIds, crew.stops.map { it.startTime })
    }
}

/** Verschieben: Tag, Kolonne, Uhrzeit. `null`, solange nichts geändert ist. */
object PlanMove {
    fun change(
        stop: DayPlanStop, fromDay: LocalDate, fromCrewId: String?, targetDay: LocalDate, targetCrewId: String?, changeTime: Boolean,
        start: LocalTime, end: LocalTime?,
    ): PlanChange.Move? {
        val dayChanged = targetDay != fromDay
        val crewChanged = targetCrewId != fromCrewId && targetCrewId != null
        if (!dayChanged && !crewChanged && !changeTime) return null
        return PlanChange.Move(
            stop.assignmentId, stop.title, day = if (dayChanged) targetDay.toString() else null, crewId = if (crewChanged) targetCrewId else null,
            startTime = if (changeTime) PlanTime.text(start) else null, endTime = if (changeTime) end?.let { PlanTime.text(it) } else null,
            setTime = changeTime,
        )
    }

    /** Schnellwahl im Blatt „Verschieben“: Morgen, Übermorgen, +1 Woche (vom angezeigten Tag aus). */
    val quickDays: List<Pair<String, Long>> = listOf("Morgen" to 1L, "Übermorgen" to 2L, "+1 Woche" to 7L)
}

/** Mitarbeiter bzw. Fahrzeug tauschen: wer fällt aus, wer kommt dafür (freie zuerst, dann aus anderen Kolonnen). */
class RosterSwap(private val crew: DayPlanCrew, private val plan: DayPlan, val vehicle: Boolean, private val resources: List<PlanningResource> = emptyList()) {
    data class Option(val id: String, val name: String, val detail: String? = null)

    val outOptions: List<Option>
        get() = if (vehicle) crew.vehicles.map { Option(it.id, it.name) } else crew.members.map { Option(it.id, it.name, it.absenceNote) }

    val inOptions: List<Option>
        get() {
            if (vehicle) {
                val own = crew.vehicles.map { it.id }.toSet()
                val used = plan.crews.flatMap { c -> c.vehicles.map { it.id to c.name } }.toMap()
                return resources.filter { it.isActive && it.id !in own }.map { Option(it.id, it.name, used[it.id]?.let { name -> "bei $name" } ?: it.identifier) }
            }
            val own = crew.members.map { it.id }.toSet()
            val free = plan.free.filter { it.id !in own }.map { Option(it.id, it.name, "frei") }
            val others = plan.crews.filter { it.id != crew.id }.flatMap { other ->
                other.available.filter { it.id !in own }.map { Option(it.id, it.name, other.name) }
            }
            return (free + others).distinctBy { it.id }
        }

    /** Vorauswahl „fällt aus“: Fahrzeug bzw. zuerst eine abwesende Person. */
    val defaultOutgoing: String? get() = if (vehicle) crew.vehicles.firstOrNull()?.id else (crew.absent.firstOrNull() ?: crew.members.firstOrNull())?.id

    /** `null`, wenn weder jemand ausfällt noch dazukommt. */
    fun change(day: String, outgoing: String?, incoming: String?): PlanChange.Swap? {
        if (outgoing == null && incoming == null) return null
        val names = (outOptions + inOptions).associate { it.id to it.name }
        val outName = outgoing?.let { names[it] }
        val inName = incoming?.let { names[it] }
        val text = when {
            outName != null && inName != null -> "${crew.name}: $inName statt $outName"
            inName != null -> "${crew.name}: $inName zusätzlich"
            outName != null -> "${crew.name}: ohne $outName"
            else -> crew.name
        }
        return if (vehicle) PlanChange.Swap(crew.id, day, fromResourceId = outgoing, toResourceId = incoming, text = text)
        else PlanChange.Swap(crew.id, day, fromUserId = outgoing, toUserId = incoming, text = text)
    }
}

/** Auswahl beim Zuweisen: Bauvorhaben aus dem Vorrat, ungeplante zuerst, ohne die schon bei der Kolonne geplanten. */
data class PlanChoice(val id: String, val title: String, val subtitle: String?, val isFree: Boolean)

object PlanChoices {
    /** Tätigkeiten zur Auswahl (wie iOS und Web). */
    val activities = listOf("Estrich einbringen", "Randdämmung und Folie", "Erstbegehung", "CM-Messung und Belegreife", "Nacharbeiten",
        "Schleifen und Grundieren")

    fun from(backlog: List<BacklogEntry>, crew: DayPlanCrew, search: String, limit: Int = 60): List<PlanChoice> {
        val planned = crew.stops.map { it.bauakteId }.toSet()
        val text = search.trim().lowercase()
        return backlog.asSequence()
            .filter { it.bauakteId !in planned }
            .filter { text.isEmpty() || "${it.number ?: ""} ${it.title} ${it.city ?: ""} ${it.customerName ?: ""}".lowercase().contains(text) }
            .sortedWith(compareBy<BacklogEntry>({ if (it.isFree) 0 else 1 }, { it.plannedStart ?: "9999" }))
            .take(limit)
            .map { entry ->
                val area = entry.totalAreaSquareMeters?.takeIf { it > 0 }?.let { "${Math.round(it)} m²" }
                PlanChoice(entry.bauakteId, listOfNotNull(entry.number, entry.title).joinToString(", "),
                    listOfNotNull(entry.address, area).joinToString(", ").ifEmpty { null }, entry.isFree)
            }
            .toList()
    }
}

/** Nächster Einsatz einer Bauakte im Tagesplan: Einsatz, Kolonne und der Plan des Tages (für „Verschieben“). */
data class SiteAppointment(val plan: DayPlan, val stop: DayPlanStop, val crew: DayPlanCrew?) {
    val day: String get() = plan.day

    /** „Kolonne Huber: Max Huber (Leitung), Lukas Brandl“. */
    val crewLine: String? get() = crew?.let { listOf(it.name, it.memberText).filter { part -> part.isNotBlank() }.joinToString(": ") }?.ifEmpty { null }
}

/** Schnelles Einplanen einer Bauakte vom Handy: Tag, Kolonne mit Auslastung, Tätigkeit, optional Uhrzeit. */
object QuickPlan {
    /** Schnellwahl des Tages (von heute aus). */
    val quickDays: List<Pair<String, Long>> = listOf("Heute" to 0L, "Morgen" to 1L, "Übermorgen" to 2L)

    /** Auslastung der Kolonne am Tag: „Frei“, „1 Einsatz“, „2 Einsätze“. */
    fun load(crew: DayPlanCrew): String = when (crew.stops.size) {
        0 -> "Frei"
        1 -> "1 Einsatz"
        else -> "${crew.stops.size} Einsätze"
    }

    /** Zusatz in der Liste der Kolonnen: Auslastung und Besetzung. */
    fun crewDetail(crew: DayPlanCrew): String = listOf(load(crew), crew.memberText).filter { it.isNotBlank() }.joinToString(", ")

    /** Vorgabe der Tätigkeit nach dem Stand der Bauakte. */
    fun defaultActivity(status: BauakteStatus?): String = when (status) {
        BauakteStatus.Inquiry -> "Erstbegehung"
        BauakteStatus.Drying -> "CM-Messung und Belegreife"
        BauakteStatus.Completed -> "Nacharbeiten"
        else -> PlanChoices.activities.first()
    }

    /** Tätigkeiten zur Auswahl, die Vorgabe zuerst. */
    fun activities(preferred: String): List<String> = listOf(preferred) + PlanChoices.activities.filter { it != preferred }

    /** Vorbelegte Kolonne: die gewählte, sonst die eigene, sonst die mit den wenigsten Einsätzen am Tag. */
    fun defaultCrew(plan: DayPlan, preferred: String?): String? =
        plan.crew(preferred)?.id ?: plan.crews.firstOrNull { it.isMine }?.id ?: plan.crews.minByOrNull { it.stops.size }?.id

    /** Erster Einsatz der Bauakte am Tag (nach Uhrzeit), mit seiner Kolonne. */
    fun find(plan: DayPlan, bauakteId: String): SiteAppointment? =
        (plan.crews.flatMap { crew -> crew.stops.map { SiteAppointment(plan, it, crew) } } + plan.withoutCrew.map { SiteAppointment(plan, it, null) })
            .filter { it.stop.bauakteId.equals(bauakteId, ignoreCase = true) }
            .minByOrNull { it.stop.startTime ?: "99" }

    fun assign(id: String, site: PlanChoice, crewId: String, day: LocalDate, activity: String, start: LocalTime?, end: LocalTime?): PlanChange.Assign =
        PlanChange.Assign(id, site.id, site.title, crewId, day.toString(), activity, start?.let { PlanTime.text(it) },
            if (start != null && end != null) PlanTime.text(maxOf(end, start)) else null)

    /** Bestätigung nach dem Einplanen bzw. Verschieben, mit Hinweis auf Konflikte. */
    fun confirmation(change: PlanChange, message: String, warnings: List<String>): String {
        val text = when (change) {
            is PlanChange.Assign -> "Eingeplant für ${PlanChange.dayText(change.day)}"
            is PlanChange.Move -> change.day?.let { "Verschoben auf ${PlanChange.dayText(it)}" } ?: message
            else -> message
        }
        return if (warnings.isEmpty()) text else "${text.trimEnd('.')}. Bitte prüfen Sie die Konflikte in der Disposition."
    }
}

/** Bauakte im Vorrat („Ungeplante Bauvorhaben“, `GET /planning/backlog`). */
@Serializable
data class BacklogEntry(
    val bauakteId: String,
    val number: String? = null,
    val title: String = "",
    val status: String? = null,
    val street: String? = null,
    val postalCode: String? = null,
    val city: String? = null,
    val plannedStart: String? = null,
    val customerName: String? = null,
    val totalAreaSquareMeters: Double? = null,
    val upcomingAssignments: Int = 0,
    val nextAssignment: String? = null,
) {
    val address: String?
        get() {
            val place = listOfNotNull(postalCode, city).filter { it.isNotBlank() }.joinToString(" ")
            return listOfNotNull(street, place).filter { it.isNotBlank() }.joinToString(", ").ifEmpty { null }
        }

    /** Noch nicht eingeplant. */
    val isFree: Boolean get() = upcomingAssignments == 0
}

/** Fahrzeug oder Gerät (`GET /planning/resources`). */
@Serializable
data class PlanningResource(val id: String, val kind: String = "vehicle", val name: String = "", val identifier: String? = null, val isActive: Boolean = true)

@Serializable
data class PlanBatchResult(val warnings: List<String> = emptyList(), val deletedIds: List<String> = emptyList(), val dryRun: Boolean = false)

@Serializable
data class PlanReleaseInfo(val day: String, val releasedAt: String? = null, val releasedByName: String? = null, val note: String? = null, val changedCount: Int = 0)

@Serializable
private data class MovedAssignment(val day: String)

/** Ergebnis einer gesendeten Änderung: Hinweise des Servers (Konflikte blockieren nicht), ggf. neuer Plan bzw. neuer Tag. */
data class PlanChangeResult(val warnings: List<String> = emptyList(), val plan: DayPlan? = null, val movedTo: String? = null)

// API

suspend fun EstrichOneApi.dayPlan(day: String): DayPlan = send<DayPlan>("GET", "planning/day/$day", null).normalized()

suspend fun EstrichOneApi.planningBacklog(): List<BacklogEntry> = send("GET", "planning/backlog", null)

suspend fun EstrichOneApi.planningResources(): List<PlanningResource> = send("GET", "planning/resources", null)

/** Konfliktvorschau ohne Speichern (`dryRun`); Änderungen ohne Batch-Operation haben keine Vorschau. */
suspend fun EstrichOneApi.checkPlanChange(change: PlanChange): List<String> {
    val operations = change.batchOperations ?: return emptyList()
    return send<PlanBatchResult>("POST", "planning/batch", json("operations" to JsonArray(operations), "dryRun" to true)).warnings
}

/** Sendet eine Änderung an die Endpunkte des Cockpits (gleiche wie im Web und auf iOS). */
suspend fun EstrichOneApi.applyPlanChange(change: PlanChange): PlanChangeResult = when (change) {
    is PlanChange.Order -> {
        val body = linkedMapOf<String, Any?>("assignmentIds" to change.assignmentIds)
        if (change.startTimes.any { it != null }) body["startTimes"] = change.startTimes
        PlanChangeResult(plan = send<DayPlan>("PUT", "planning/crews/${change.crewId}/days/${change.day}/order", toJson(body)).normalized())
    }
    is PlanChange.Swap -> PlanChangeResult(plan = send<DayPlan>("POST", "planning/crews/${change.crewId}/days/${change.day}/swap", json(
        "fromUserId" to change.fromUserId, "toUserId" to change.toUserId, "fromResourceId" to change.fromResourceId,
        "toResourceId" to change.toResourceId, "exchange" to true,
    )).normalized())
    is PlanChange.NextFreeDay -> PlanChangeResult(movedTo = send<MovedAssignment>("POST", "planning/assignments/${change.id}/next-free-day", JsonObject(emptyMap())).day)
    is PlanChange.SwapCrews -> PlanChangeResult(plan = send<DayPlan>("POST", "planning/days/${change.day}/swap-crews",
        json("crewA" to change.crewA, "crewB" to change.crewB)).normalized())
    is PlanChange.Assign, is PlanChange.Move, is PlanChange.Remove ->
        PlanChangeResult(warnings = send<PlanBatchResult>("POST", "planning/batch", json("operations" to JsonArray(change.batchOperations.orEmpty()))).warnings)
}

/** Gibt die Planung für die Tage [from] bis [to] (je JJJJ-MM-TT) frei; erneutes Freigeben setzt den Stand neu. */
suspend fun EstrichOneApi.releasePlan(from: String, to: String, note: String?): List<PlanReleaseInfo> =
    send("POST", "planning/releases", json("from" to from, "to" to to, "note" to note?.trim()?.takeIf { it.isNotEmpty() }))

/** Nimmt die Freigabe eines Tages zurück (der Plan gilt wieder als Entwurf). */
suspend fun EstrichOneApi.withdrawPlanRelease(day: String) {
    send<Unit>("DELETE", "planning/releases/$day", null)
}

// Offline vormerken

/**
 * Vorgemerkte Änderungen der Schnelldisposition (ohne Verbindung erfasst), im verschlüsselten lokalen Speicher. Gesendet
 * wird in der Reihenfolge der Erfassung; was der Server ablehnt, bleibt mit Begründung zum Verwerfen stehen.
 */
class PlanningOutbox(private val backend: StorageBackend, private val now: () -> Instant = Instant::now) {
    @Serializable
    data class Entry(val id: String, val change: PlanChange, val createdAt: String, val error: String? = null) {
        val created: Instant? get() = ApiDate.parse(createdAt)
    }

    data class FlushResult(val sent: Int = 0, val warnings: List<String> = emptyList(), val failed: Int = 0)

    private val serializer = ListSerializer(Entry.serializer())

    fun entries(): List<Entry> =
        backend.meta(KEY)?.let { runCatching { EstrichOneApi.json.decodeFromString(serializer, it) }.getOrNull() } ?: emptyList()

    fun add(change: PlanChange) = save(entries() + Entry(Ids.v7(now().toEpochMilli()), change, ApiDate.format(now())))

    fun discard(id: String) = save(entries().filterNot { it.id == id })

    /** Sendet alle offenen Einträge. Ohne Verbindung bleibt alles liegen; Ablehnungen werden markiert. */
    suspend fun flush(api: EstrichOneApi): FlushResult {
        val list = entries().toMutableList()
        var sent = 0
        var failed = 0
        val warnings = mutableListOf<String>()
        var index = 0
        while (index < list.size) {
            if (list[index].error != null) { index++; continue }
            try {
                warnings += api.applyPlanChange(list[index].change).warnings
                sent++
                list.removeAt(index)
            } catch (e: ApiException) {
                if (e.isOffline) break
                list[index] = list[index].copy(error = e.message ?: "Vom Server abgelehnt")
                failed++
                index++
            }
        }
        save(list)
        return FlushResult(sent, warnings, failed)
    }

    private fun save(list: List<Entry>) {
        backend.transaction { backend.setMeta(KEY, if (list.isEmpty()) null else EstrichOneApi.json.encodeToString(serializer, list)) }
    }

    companion object {
        const val KEY = "planning_outbox"
    }
}

// Laden, Ändern, Freigeben

/** Tagesplan mit Stand: [isCurrent] = frisch vom Server, sonst der zuletzt gespeicherte; [message] statt Plan (Recht, offline). */
data class DispatchDay(val plan: DayPlan?, val loadedAt: Instant?, val isCurrent: Boolean, val message: String? = null)

/** Ausgang einer Änderung bzw. Freigabe. */
sealed interface PlanOutcome {
    /** Übernommen; [plan] = neuer Tagesplan, wenn der Endpunkt ihn liefert. */
    data class Applied(val message: String, val warnings: List<String> = emptyList(), val plan: DayPlan? = null) : PlanOutcome

    /** Ohne Verbindung vorgemerkt. */
    data object Queued : PlanOutcome

    data class Failed(val message: String) : PlanOutcome
}

/**
 * Schnelldisposition (wie DispatchView auf iOS): Tagesplan laden (offline der zuletzt geladene Stand), Änderungen senden
 * bzw. ohne Verbindung vormerken, Konflikte vorab prüfen, Plan freigeben (nur online).
 */
class DispatchDesk(val api: EstrichOneApi, private val backend: StorageBackend, private val now: () -> Instant = Instant::now) {
    val outbox = PlanningOutbox(backend, now)

    @Serializable
    private data class StoredPlan(val plan: DayPlan, val loadedAt: String)

    private fun key(day: String) = "day_plan_$day"

    /** Zuletzt gespeicherter Stand des Tages, ohne Netz. */
    fun cached(day: String): DispatchDay? = backend.meta(key(day))
        ?.let { runCatching { EstrichOneApi.json.decodeFromString(StoredPlan.serializer(), it) }.getOrNull() }
        ?.let { DispatchDay(it.plan.normalized(), ApiDate.parse(it.loadedAt), isCurrent = false) }

    suspend fun load(day: String): DispatchDay = try {
        val plan = api.dayPlan(day)
        val at = now()
        backend.transaction { backend.setMeta(key(day), EstrichOneApi.json.encodeToString(StoredPlan.serializer(), StoredPlan(plan, ApiDate.format(at)))) }
        DispatchDay(plan, at, isCurrent = true)
    } catch (e: ApiException) {
        when {
            e.status == 403 -> DispatchDay(null, null, isCurrent = true, message = NO_PERMISSION)
            else -> cached(day) ?: DispatchDay(null, null, isCurrent = false,
                message = if (e.isOffline) "Ohne Verbindung ist die Planung für diesen Tag nicht verfügbar." else e.message)
        }
    }

    /** Vorgemerkte Änderungen senden (vor dem Laden). */
    suspend fun flush(): PlanningOutbox.FlushResult = if (outbox.entries().none { it.error == null }) PlanningOutbox.FlushResult() else outbox.flush(api)

    /** Online sofort an den Server; ohne Verbindung vormerken. */
    suspend fun perform(change: PlanChange): PlanOutcome = try {
        val result = api.applyPlanChange(change)
        val message = result.movedTo?.let { day -> ApiDate.day(day)?.let { "Verschoben auf ${Dates.long(it)}" } ?: "Verschoben auf $day" } ?: change.doneText
        PlanOutcome.Applied(message, result.warnings, result.plan)
    } catch (e: ApiException) {
        if (e.isOffline) {
            outbox.add(change)
            PlanOutcome.Queued
        } else {
            PlanOutcome.Failed(e.message ?: "Die Änderung wurde nicht übernommen.")
        }
    }

    /**
     * Nächster Einsatz einer Bauakte ab heute (für Planer, auch bei fremden Kolonnen): der Tag aus dem Vorrat bzw. der
     * gerade eingeplante Tag [hintDay], der Einsatz aus dem Tagesplan. `null` ohne Verbindung oder ohne Einsatz.
     */
    suspend fun nextAppointment(bauakteId: String, hintDay: String? = null, today: LocalDate = LocalDate.now(Dates.zone)): SiteAppointment? = try {
        val fromBacklog = api.planningBacklog().firstOrNull { it.bauakteId.equals(bauakteId, ignoreCase = true) }?.nextAssignment?.take(10)
        listOfNotNull(fromBacklog, hintDay).filter { it >= today.toString() }.distinct().sorted()
            .firstNotNullOfOrNull { day -> QuickPlan.find(api.dayPlan(day), bauakteId) }
    } catch (e: ApiException) {
        null
    }

    /** Konfliktvorschau; `null` ohne Verbindung. */
    suspend fun check(change: PlanChange): List<String>? = try {
        api.checkPlanChange(change)
    } catch (e: ApiException) {
        if (e.isOffline) null else listOf(e.message ?: "Prüfung nicht möglich.")
    }

    /** Freigeben ab [from] bis [to]; nur online (hält den aktuellen Stand fest). */
    suspend fun release(from: LocalDate, to: LocalDate, note: String?): PlanOutcome = try {
        val infos = api.releasePlan(from.toString(), maxOf(from, to).toString(), note)
        PlanOutcome.Applied(if (infos.size > 1) "${infos.size} Tage freigegeben" else "Plan freigegeben")
    } catch (e: ApiException) {
        PlanOutcome.Failed(if (e.isOffline) ONLINE_ONLY else e.message ?: "Freigabe nicht möglich.")
    }

    suspend fun withdraw(day: LocalDate): PlanOutcome = try {
        api.withdrawPlanRelease(day.toString())
        PlanOutcome.Applied("Freigabe zurückgenommen")
    } catch (e: ApiException) {
        PlanOutcome.Failed(if (e.isOffline) ONLINE_ONLY else e.message ?: "Zurücknehmen nicht möglich.")
    }

    companion object {
        const val NO_PERMISSION = "Für die Planung fehlt Ihnen die Berechtigung."
        const val ONLINE_ONLY = "Freigeben geht nur mit Verbindung."
        const val QUEUED = "Keine Verbindung, Änderung vorgemerkt"

        /** Recht zum Ändern der Planung (Disposition, Bauleitung). */
        const val PERMISSION_MANAGE = "planning.manage"
    }
}

// Texte

/** Kurzfassung für „Heute“ und den Freigabestand der Tagesansicht. */
object DispatchText {
    enum class Tone { Success, Warning, Neutral }

    /** „3 Kolonnen, freigegeben“, „2 Kolonnen, noch nicht freigegeben“, „1 Kolonne, 2 Änderungen seit Freigabe“. */
    fun summary(plan: DayPlan): String {
        val crews = plan.crews.count { it.stops.isNotEmpty() }
        if (plan.stopCount == 0) return if (plan.release != null) "Keine Einsätze, freigegeben" else "Noch keine Einsätze geplant"
        val head = when {
            crews == 0 -> if (plan.withoutCrew.size == 1) "1 Einsatz ohne Kolonne" else "${plan.withoutCrew.size} Einsätze ohne Kolonne"
            crews == 1 -> "1 Kolonne"
            else -> "$crews Kolonnen"
        }
        val release = plan.release
        return when {
            release == null -> "$head, noch nicht freigegeben"
            release.changedCount > 0 -> "$head, ${changes(release.changedCount)} seit Freigabe"
            else -> "$head, freigegeben"
        }
    }

    fun tone(plan: DayPlan): Tone = when {
        plan.release == null -> Tone.Neutral
        plan.release.changedCount > 0 -> Tone.Warning
        else -> Tone.Success
    }

    fun changes(count: Int): String = if (count == 1) "1 Änderung" else "$count Änderungen"

    /** „heute, 06:30“, „gestern, 17:10“, „24.11., 06:30“. */
    fun stamp(instant: Instant?, today: LocalDate = LocalDate.now(Dates.zone)): String {
        if (instant == null) return ""
        val day = instant.atZone(Dates.zone).toLocalDate()
        val time = Dates.time(instant)
        return when (ChronoUnit.DAYS.between(day, today)) {
            0L -> "heute, $time"
            1L -> "gestern, $time"
            else -> "${Dates.dayMonth(day)}, $time"
        }
    }

    /** Freigabestand als Titel und Zusatz: „Freigegeben heute, 06:30“ und „von Sabine Wagner“ bzw. Änderungen. */
    fun releaseTitle(plan: DayPlan, today: LocalDate = LocalDate.now(Dates.zone)): Pair<String, String> {
        val release = plan.release ?: return "Entwurf" to "Noch nicht freigegeben"
        val detail = if (release.changedCount > 0) "${changes(release.changedCount)} seit Freigabe"
        else release.releasedByName.takeIf { it.isNotBlank() }?.let { "von $it" } ?: "unverändert"
        return "Freigegeben ${stamp(release.released, today)}".trim() to detail
    }

    /** „5 vorgemerkte Änderungen gesendet“. */
    fun sent(count: Int): String = if (count == 1) "1 vorgemerkte Änderung gesendet" else "$count vorgemerkte Änderungen gesendet"

    /** „2 Änderungen vorgemerkt, Versand bei Verbindung“. */
    fun pending(count: Int): String = "${changes(count)} vorgemerkt, Versand bei Verbindung"
}
