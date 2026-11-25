package de.estrichone.core

import java.time.Instant
import java.time.LocalDate
import java.time.LocalTime
import java.util.Locale

/**
 * Aufbau der Seite „Heute“ (gleiches Konzept wie iOS): Arbeit zuerst. Der laufende bzw. nächste Einsatz als Hauptkarte,
 * die übrigen kompakt darunter, Mitteilungen der Verwaltung zu einer Zeile zusammengefasst.
 */
object TodayOverview {
    /** Einsätze eines Tages, aufgeteilt in Hauptkarte und weitere. */
    data class Plan(val focus: PlannedJob?, val focusPhase: TodayGlance.Phase, val others: List<PlannedJob>) {
        /** Überschrift über der Hauptkarte. */
        val focusTitle: String
            get() = when (focusPhase) {
                TodayGlance.Phase.Running -> "Jetzt im Einsatz"
                TodayGlance.Phase.Finished -> "Letzter Einsatz"
                else -> "Nächster Einsatz"
            }
    }

    /** Laufender, sonst nächster, sonst letzter Einsatz als Hauptkarte; die anderen nach Uhrzeit. */
    fun plan(jobs: List<PlannedJob>, now: LocalTime): Plan {
        val sorted = jobs.sortedBy { it.startTime ?: "99" }
        val phases = sorted.map { TodayGlance.phase(it.startTime, it.endTime, now) }
        val index = TodayGlance.focusIndex(phases) ?: return Plan(null, TodayGlance.Phase.Unknown, emptyList())
        return Plan(sorted[index], phases[index], sorted.filterIndexed { i, _ -> i != index })
    }

    /** Zusammenfassung „Mein Tag“: Kennzahl und Beschriftung je Kachel. */
    data class Tile(val value: String, val label: String)

    fun jobsTile(jobs: List<PlannedJob>): Tile {
        val first = jobs.mapNotNull { Dates.clock(it.startTime) }.minOrNull()
        return Tile(jobs.size.toString(), when {
            jobs.isEmpty() -> "Einsätze"
            first != null -> "${if (jobs.size == 1) "Einsatz" else "Einsätze"} ab $first"
            else -> if (jobs.size == 1) "Einsatz" else "Einsätze"
        })
    }

    fun tasksTile(open: Int, defects: Int): Tile =
        Tile(open.toString(), when {
            defects == 1 -> "Aufgaben, 1 Mangel"
            defects > 1 -> "Aufgaben, $defects Mängel"
            open == 1 -> "offene Aufgabe"
            else -> "offene Aufgaben"
        })

    /** Mitteilungen als eine Zeile: „Störung beim E-Mail-Versand“ bzw. „3 Mitteilungen“. */
    data class NoticeSummary(val count: Int, val title: String, val subtitle: String, val tone: NoticeItem.Tone, val critical: Boolean)

    fun noticeSummary(items: List<NoticeItem>, dismissed: Set<String>): NoticeSummary? {
        val open = items.filter { !it.isDismissible || it.id !in dismissed }
        if (open.isEmpty()) return null
        val tone = when {
            open.any { it.tone == NoticeItem.Tone.Error } -> NoticeItem.Tone.Error
            open.any { it.tone == NoticeItem.Tone.Warning } -> NoticeItem.Tone.Warning
            else -> NoticeItem.Tone.Information
        }
        val critical = open.any { it.kind == NoticeItem.Kind.Critical || it.kind == NoticeItem.Kind.MaintenanceRunning }
        if (open.size == 1) {
            val item = open.first()
            val subtitle = listOf(item.label, item.relative.ifEmpty { item.remaining }).filter { it.isNotEmpty() }.joinToString(", ")
            return NoticeSummary(1, item.title, subtitle, tone, critical)
        }
        return NoticeSummary(open.size, "${open.size} Mitteilungen", open.joinToString(", ") { it.title }, tone, critical)
    }
}

/** Liste der Baustellen: Filter, Reihenfolge (heute und aktiv zuerst) und nächster Termin. */
object SiteList {
    enum class Filter(val title: String, val statuses: Set<BauakteStatus>?) {
        Today("Heute", null),
        Running("Laufend", setOf(BauakteStatus.InProgress, BauakteStatus.Drying, BauakteStatus.Planned, BauakteStatus.Inquiry)),
        Active("In Ausführung", setOf(BauakteStatus.InProgress, BauakteStatus.Drying)),
        Planned("Geplant", setOf(BauakteStatus.Planned)),
        Inquiry("Anfrage", setOf(BauakteStatus.Inquiry)),
        Done("Abgeschlossen", setOf(BauakteStatus.Completed, BauakteStatus.Cancelled)),
        All("Alle", null),
    }

    fun matches(filter: Filter, status: BauakteStatus, isToday: Boolean): Boolean = when (filter) {
        Filter.Today -> isToday
        Filter.All -> true
        else -> status in filter.statuses.orEmpty()
    }

    /**
     * Reihenfolge: heute im Einsatz (nach Uhrzeit), dann nach Status (In Ausführung, Trocknung, Geplant, Anfrage,
     * Abgeschlossen), innerhalb gleichen Status der nächste Termin zuerst, ohne Termin nach Nummer absteigend.
     */
    fun <T> ordered(
        items: List<T>, id: (T) -> String, status: (T) -> BauakteStatus, number: (T) -> String?,
        next: Map<String, PlannedJob>, today: String,
    ): List<T> = items.sortedWith(
        compareBy<T> { if (next[id(it)]?.day == today) 0 else 1 }
            .thenBy { if (next[id(it)]?.day == today) next[id(it)]?.startTime ?: "99" else "" }
            .thenBy { status(it).order }
            .thenBy { next[id(it)]?.let { job -> job.day + (job.startTime ?: "99") } ?: "9999" }
            .thenByDescending { number(it) ?: "" },
    )

    /** Nächster Einsatz je Bauakte (ab heute). */
    fun nextJobs(jobs: List<PlannedJob>, today: String): Map<String, PlannedJob> =
        jobs.filter { it.day >= today }.groupBy { it.bauakteId.lowercase() }
            .mapValues { (_, list) -> list.minBy { it.day + (it.startTime ?: "99") } }

    /** „Heute, 07:00“, „Morgen“, „Mi., 25.11., 07:00“. */
    fun appointmentText(job: PlannedJob, today: LocalDate): String = appointmentText(job.day, job.startTime, today)

    /** Wie oben, aus Tag (JJJJ-MM-TT) und Beginn (für Einsätze aus dem Tagesplan der Disposition). */
    fun appointmentText(dayText: String, startTime: String?, today: LocalDate): String {
        val day = ApiDate.day(dayText) ?: return dayText
        val days = java.time.temporal.ChronoUnit.DAYS.between(today, day)
        val date = when (days) {
            0L -> "Heute"
            1L -> "Morgen"
            else -> "${Dates.weekdayShort(day)}, ${Dates.dayMonth(day)}"
        }
        return listOfNotNull(date, Dates.clock(startTime)).joinToString(", ")
    }
}
