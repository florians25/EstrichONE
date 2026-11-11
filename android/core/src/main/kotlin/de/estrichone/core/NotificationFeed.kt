package de.estrichone.core

import java.text.Normalizer
import java.time.DayOfWeek
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.time.temporal.ChronoUnit
import java.time.temporal.TemporalAdjusters

// Postfach (wie NotificationFeed.swift auf iOS): Arten, Zeiträume, Filter, Abschnitte, Zeitangaben.

/** Art einer Benachrichtigung: fasst die Kategorien des Servers zu Filtern mit eigener Farbe zusammen. */
enum class NotificationKind(val title: String) {
    Planning("Planung"), Bauakte("Bauakten"), Work("Aufgaben"), Chat("Chat"), Ticket("Meldungen"), Office("Büro"), Personal("Personal"), System("System");

    companion object {
        /** Art zur Kategorie des Servers; Unbekanntes zählt zum Büro (von Hand gesendete Nachrichten). */
        fun of(category: String?): NotificationKind {
            val key = category.orEmpty().lowercase()
            return when (key.split('.', '_', '-').first()) {
                "planning", "plan", "planchange", "planday", "disposition" -> Planning
                "bauakte", "assignment", "approval", "document", "acceptance", "abnahme", "comment", "reply" -> Bauakte
                "work", "deadline", "defect", "mangel", "task" -> Work
                "chat", "chatmessage" -> Chat
                "ticket", "helpdesk", "feedback" -> Ticket
                "absence", "leave", "vacation" -> Personal
                "system", "maintenance", "backup", "archive", "peerupdate" -> System
                else -> Office
            }
        }
    }
}

/** Zeitraum: Heute, Gestern, Diese Woche (ab Montag), Älter. */
enum class NotificationPeriod(val title: String) {
    Today("Heute"), Yesterday("Gestern"), ThisWeek("Diese Woche"), Older("Älter");

    companion object {
        fun of(instant: Instant, now: Instant, zone: ZoneId = Dates.zone): NotificationPeriod {
            val day = instant.atZone(zone).toLocalDate()
            val today = now.atZone(zone).toLocalDate()
            return when {
                !day.isBefore(today) -> Today
                day == today.minusDays(1) -> Yesterday
                !day.isBefore(today.with(TemporalAdjusters.previousOrSame(DayOfWeek.MONDAY))) -> ThisWeek
                else -> Older
            }
        }
    }
}

enum class NotificationGrouping(val title: String) { Time("Nach Zeit"), Kind("Nach Art") }

/** Auswahl oben im Postfach: Alle oder nur ungelesene, optional eine Art, dazu der Suchtext. */
data class NotificationFilter(val unreadOnly: Boolean = false, val kind: NotificationKind? = null, val query: String = "") {
    val isActive: Boolean get() = unreadOnly || kind != null || query.isNotBlank()

    /** Jedes Suchwort muss in Titel, Text, Absender oder Art vorkommen (ohne Groß-/Kleinschreibung und Akzente). */
    fun matches(item: InboxNotification): Boolean {
        if (unreadOnly && item.isRead) return false
        if (kind != null && item.kind != kind) return false
        val words = fold(query).split(Regex("\\s+")).filter { it.isNotEmpty() }
        if (words.isEmpty()) return true
        val haystack = fold(listOfNotNull(item.title, item.body, item.senderName, item.kind.title).joinToString(" "))
        return words.all { it in haystack }
    }

    companion object {
        fun fold(text: String): String =
            Normalizer.normalize(text.lowercase(Dates.german), Normalizer.Form.NFD).replace(Regex("\\p{M}+"), "").replace("ß", "ss")
    }
}

data class NotificationSection(val id: String, val title: String, val kind: NotificationKind?, val items: List<InboxNotification>) {
    val unreadCount: Int get() = items.count { !it.isRead }
}

/** Aufbereitung des Postfachs (testbarer Kern der Ansicht). */
object NotificationFeed {
    private val newestFirst = compareByDescending<InboxNotification> { it.created ?: Instant.EPOCH }.thenByDescending { it.id }

    fun filter(items: List<InboxNotification>, filter: NotificationFilter): List<InboxNotification> = items.filter(filter::matches).sortedWith(newestFirst)

    fun sections(items: List<InboxNotification>, grouping: NotificationGrouping = NotificationGrouping.Time, now: Instant = Instant.now(),
        zone: ZoneId = Dates.zone): List<NotificationSection> {
        val sorted = items.sortedWith(newestFirst)
        return when (grouping) {
            NotificationGrouping.Time -> {
                val groups = sorted.groupBy { NotificationPeriod.of(it.created ?: Instant.EPOCH, now, zone) }
                NotificationPeriod.entries.mapNotNull { period -> groups[period]?.let { NotificationSection("period-${period.name}", period.title, null, it) } }
            }
            NotificationGrouping.Kind -> sorted.groupBy { it.kind }
                .map { (kind, list) -> NotificationSection("kind-${kind.name}", kind.title, kind, list) }
                .sortedWith(compareByDescending<NotificationSection> { it.unreadCount > 0 }.thenByDescending { it.items.first().created ?: Instant.EPOCH })
        }
    }

    fun kinds(items: List<InboxNotification>): List<NotificationKind> {
        val present = items.map { it.kind }.toSet()
        return NotificationKind.entries.filter { it in present }
    }

    fun unreadCounts(items: List<InboxNotification>): Map<NotificationKind, Int> = items.filter { !it.isRead }.groupingBy { it.kind }.eachCount()

    fun emptyMessage(filter: NotificationFilter): Pair<String, String> = when {
        filter.query.isNotBlank() -> "Keine Treffer" to "Keine Benachrichtigung enthält „${filter.query.trim()}“."
        filter.unreadOnly -> "Alles gelesen" to (filter.kind?.let { "Keine ungelesenen Benachrichtigungen unter „${it.title}“." } ?: "Keine ungelesenen Benachrichtigungen.")
        filter.kind != null -> "Nichts unter „${filter.kind.title}“" to "Hier ist gerade nichts."
        else -> "Keine Benachrichtigungen" to "Hier erscheinen Freigaben der Planung, Antworten im Helpdesk und Nachrichten aus dem Büro."
    }

    /** Zeit in der Zeile wie in Mail: „jetzt“, „vor 12 Min.“, „09:41“, „Gestern“, „Montag“, „26.10.“, „30.9.25“. */
    fun timeText(instant: Instant, now: Instant = Instant.now(), zone: ZoneId = Dates.zone): String {
        val seconds = ChronoUnit.SECONDS.between(instant, now)
        if (seconds < 60) return "jetzt"
        if (seconds < 3600) return "vor ${seconds / 60} Min."
        val local = instant.atZone(zone)
        return when (NotificationPeriod.of(instant, now, zone)) {
            NotificationPeriod.Today -> local.format(DateTimeFormatter.ofPattern("HH:mm", Dates.german))
            NotificationPeriod.Yesterday -> "Gestern"
            NotificationPeriod.ThisWeek -> Dates.weekdayName(local.toLocalDate())
            NotificationPeriod.Older -> local.format(DateTimeFormatter.ofPattern(if (local.year == now.atZone(zone).year) "d.M." else "d.M.yy", Dates.german))
        }
    }

    /** „Mittwoch, 4. November 2026, 10:00 Uhr“ (Einzelansicht). */
    fun fullTimeText(instant: Instant, zone: ZoneId = Dates.zone): String =
        instant.atZone(zone).format(DateTimeFormatter.ofPattern("EEEE, d. MMMM yyyy, HH:mm", Dates.german)) + " Uhr"

    /** Titel des Knopfs zum Ziel. */
    fun openTitle(route: NotificationRoute): String = when (route) {
        is NotificationRoute.Bauakte -> "Bauakte öffnen"
        is NotificationRoute.Ticket -> "Meldung öffnen"
        is NotificationRoute.Planning -> "Planung öffnen"
        NotificationRoute.Tasks -> "Aufgaben öffnen"
        is NotificationRoute.Chat -> "Chat öffnen"
        is NotificationRoute.Absences -> if (route.approvals) "Anträge öffnen" else "Urlaub öffnen"
        NotificationRoute.Inbox -> "Postfach öffnen"
    }
}
