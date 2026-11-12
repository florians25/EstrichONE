package de.estrichone.core

import kotlinx.serialization.Serializable
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import java.time.Duration
import java.time.Instant
import java.time.ZoneId
import java.time.temporal.ChronoUnit
import kotlin.math.roundToLong

/** Hinweise des Betreibers (`GET /system/notices`, ohne Anmeldung): Ankündigungen und Wartung. */
@Serializable
data class SystemNotices(
    val announcements: List<Announcement> = emptyList(), val maintenance: Maintenance? = null, val serverTime: String? = null,
    /** Laufendes Update (Ankündigung, Update, Abschluss) – siehe [UpdatePresentation]. */
    val update: UpdateMaintenance? = null,
) {
    /** Gültiges Update oder null (unbekannte Phase wird ignoriert). */
    val activeUpdate: UpdateMaintenance? get() = update?.takeIf { it.phase in UpdatePresentation.phases }

    @Serializable
    data class Announcement(
        val id: String, val title: String, val text: String = "", val severity: String = "info", val startsAt: String? = null,
        val endsAt: String? = null, val source: String? = null,
    ) {
        fun isCurrent(now: Instant): Boolean {
            ApiDate.parse(startsAt)?.let { if (now < it) return false }
            ApiDate.parse(endsAt)?.let { if (now >= it) return false }
            return true
        }
    }

    @Serializable
    data class Maintenance(
        val active: Boolean = false, val message: String? = null, val startsAt: String, val endsAt: String? = null,
        val adminsMayWork: Boolean = true, val source: String? = null,
    ) {
        val start: Instant? get() = ApiDate.parse(startsAt)
        val end: Instant? get() = ApiDate.parse(endsAt)
    }

    fun currentAnnouncements(now: Instant = Instant.now()) = announcements.filter { it.isCurrent(now) }
}

/** Aufbereiteter Hinweis für eine Karte (Art, Farbe, Zeitraum, Countdown). */
data class NoticeItem(
    val id: String, val kind: Kind, val title: String, val text: String, val period: String, val relative: String, val remaining: String,
    val source: String, val start: Instant?, val end: Instant?,
) {
    enum class Kind(val rank: Int) { MaintenanceRunning(0), Critical(1), MaintenanceSoon(2), Warning(3), Maintenance(4), Info(5) }

    /** Farbe: Information (blau), Warnung (gelb), Fehler (rot). */
    enum class Tone { Information, Warning, Error }

    val tone: Tone
        get() = when (kind) {
            Kind.Critical -> Tone.Error
            Kind.MaintenanceRunning, Kind.MaintenanceSoon, Kind.Warning -> Tone.Warning
            Kind.Maintenance, Kind.Info -> Tone.Information
        }

    /** Störungen und laufende Wartung lassen sich nicht ausblenden. */
    val isDismissible: Boolean get() = kind != Kind.Critical && kind != Kind.MaintenanceRunning

    val label: String
        get() = when (kind) {
            Kind.MaintenanceRunning -> "Wartung läuft"
            Kind.Critical -> "Störung"
            Kind.MaintenanceSoon -> "Wartung in Kürze"
            Kind.Warning -> "Wichtiger Hinweis"
            Kind.Maintenance -> "Geplante Wartung"
            Kind.Info -> "Information"
        }

    /** Zeitlich begrenzt: Countdown und Zeitraum anzeigen. */
    val isTimed: Boolean get() = start != null || end != null
}

/** Aufbereitung der Systemhinweise wie im Web (`noticeView.ts`) und auf iOS (NoticePresentation). */
object NoticePresentation {
    private val weekdays = listOf("Mo.", "Di.", "Mi.", "Do.", "Fr.", "Sa.", "So.")

    private fun two(value: Int) = value.toString().padStart(2, '0')

    fun shortDate(instant: Instant, now: Instant, zone: ZoneId = Dates.zone): String {
        val d = instant.atZone(zone)
        val year = if (d.year != now.atZone(zone).year) d.year.toString() else ""
        return "${weekdays[d.dayOfWeek.value - 1]}, ${two(d.dayOfMonth)}.${two(d.monthValue)}.$year"
    }

    private fun clock(instant: Instant, zone: ZoneId): String = instant.atZone(zone).let { "${two(it.hour)}:${two(it.minute)}" }

    fun period(start: Instant?, end: Instant?, now: Instant, zone: ZoneId = Dates.zone): String = when {
        start != null && end != null ->
            if (start.atZone(zone).toLocalDate() == end.atZone(zone).toLocalDate()) {
                "${shortDate(start, now, zone)}, ${clock(start, zone)}–${clock(end, zone)} Uhr"
            } else {
                "${shortDate(start, now, zone)}, ${clock(start, zone)} Uhr – ${shortDate(end, now, zone)}, ${clock(end, zone)} Uhr"
            }
        start != null -> "ab ${shortDate(start, now, zone)}, ${clock(start, zone)} Uhr"
        end != null -> "bis ${shortDate(end, now, zone)}, ${clock(end, zone)} Uhr"
        else -> ""
    }

    /** „10 Min.“, „1 Std. 30 Min.“, „5 Std.“, „1 Tag“, „2 Tagen“ (Dativ) bzw. „2 Tage“. */
    fun duration(seconds: Long, dative: Boolean = true): String {
        val minutes = maxOf(1L, (seconds / 60.0).roundToLong())
        if (minutes < 60) return "$minutes Min."
        if (seconds < 86_400) {
            val hours = minutes / 60
            val rest = minutes % 60
            return if (hours < 3 && rest >= 5) "$hours Std. $rest Min." else "${(minutes / 60.0).roundToLong()} Std."
        }
        val days = (seconds / 86_400.0).roundToLong()
        return if (days == 1L) "1 Tag" else "$days ${if (dative) "Tagen" else "Tage"}"
    }

    fun relative(start: Instant?, end: Instant?, now: Instant, zone: ZoneId = Dates.zone): String {
        if (end != null && now >= end) return "beendet"
        if (start == null) return ""
        val diff = Duration.between(now, start).seconds
        if (diff > 0) {
            if (diff < 60) return "beginnt gleich"
            if (diff < 3600) return "in ${duration(diff)}"
            val days = ChronoUnit.DAYS.between(now.atZone(zone).toLocalDate(), start.atZone(zone).toLocalDate())
            return when (days) {
                0L -> "heute, in ${duration(diff)}"
                1L -> "morgen"
                else -> "in $days Tagen"
            }
        }
        val since = Duration.between(start, now).seconds
        return if (since < 60) "läuft gerade an" else "läuft seit ${duration(since)}"
    }

    fun remaining(end: Instant?, now: Instant): String {
        if (end == null) return ""
        val left = Duration.between(now, end).seconds
        return if (left > 0) "noch ${duration(left, dative = false)}" else ""
    }

    /** Kurzer, stabiler Inhaltsschlüssel (FNV-1a über UTF-16, wie im Web). */
    fun contentHash(text: String): String {
        var hash = 0x811C9DC5L
        for (unit in text) {
            hash = hash xor unit.code.toLong()
            hash = (hash * 0x01000193L) and 0xFFFFFFFFL
        }
        return hash.toString(36)
    }

    private fun sourceText(source: String?) = when (source) {
        "console" -> "Betreiber"
        "instance" -> "Verwaltung"
        else -> ""
    }

    fun item(a: SystemNotices.Announcement, now: Instant, zone: ZoneId = Dates.zone): NoticeItem {
        val kind = when (a.severity) {
            "critical" -> NoticeItem.Kind.Critical
            "warning" -> NoticeItem.Kind.Warning
            else -> NoticeItem.Kind.Info
        }
        val severity = if (a.severity in setOf("critical", "warning")) a.severity else "info"
        val start = ApiDate.parse(a.startsAt)
        val end = ApiDate.parse(a.endsAt)
        val upcoming = start?.let { it > now } ?: false
        return NoticeItem(
            id = "a:${a.id}:${contentHash("$severity|${a.title}|${a.text}|${a.startsAt ?: ""}|${a.endsAt ?: ""}")}",
            kind = kind, title = a.title, text = a.text, period = period(start, end, now, zone),
            relative = if (upcoming) relative(start, end, now, zone) else "", remaining = if (upcoming) "" else remaining(end, now),
            source = sourceText(a.source), start = start, end = end,
        )
    }

    fun item(m: SystemNotices.Maintenance, now: Instant, zone: ZoneId = Dates.zone): NoticeItem {
        val start = m.start
        val end = m.end
        val kind = when {
            m.active || (start?.let { it <= now } ?: false) -> NoticeItem.Kind.MaintenanceRunning
            start != null && Duration.between(now, start).seconds <= 3600 -> NoticeItem.Kind.MaintenanceSoon
            else -> NoticeItem.Kind.Maintenance
        }
        val running = kind == NoticeItem.Kind.MaintenanceRunning
        val kindName = kind.name.replaceFirstChar { it.lowercase() }
        return NoticeItem(
            id = "m:${m.startsAt}:$kindName:${contentHash("${m.message ?: ""}|${m.endsAt ?: ""}")}",
            kind = kind, title = if (running) "Wartungsarbeiten laufen" else "Geplante Wartung",
            text = m.message ?: "EstrichONE ist in dieser Zeit nicht erreichbar. Bitte vorher abgleichen. Erfasste Daten bleiben auf dem Gerät gespeichert.",
            period = period(start, end, now, zone), relative = relative(start, end, now, zone), remaining = if (running) remaining(end, now) else "",
            source = sourceText(m.source), start = start, end = end,
        )
    }

    /** Alle aktuellen Hinweise in Anzeige-Reihenfolge. */
    fun items(notices: SystemNotices?, now: Instant, zone: ZoneId = Dates.zone): List<NoticeItem> {
        if (notices == null) return emptyList()
        val list = notices.currentAnnouncements(now).map { item(it, now, zone) }.toMutableList()
        notices.maintenance?.let { m -> if (m.active || (m.end?.let { it > now } ?: true)) list.add(item(m, now, zone)) }
        return list.withIndex().sortedWith(compareBy({ it.value.kind.rank }, { it.index })).map { it.value }
    }

    /** Ausgeblendete fallen weg; eingeklappt bleiben der wichtigste und alle nicht ausblendbaren sichtbar. */
    fun selection(items: List<NoticeItem>, dismissed: Set<String>, expanded: Boolean): Pair<List<NoticeItem>, Int> {
        val open = items.filter { !it.isDismissible || it.id !in dismissed }
        if (expanded || open.size <= 1) return open to 0
        val shown = open.filterIndexed { index, item -> index == 0 || !item.isDismissible }
        return shown to open.size - shown.size
    }

    fun moreText(count: Int) = if (count == 1) "1 weiterer Hinweis" else "$count weitere Hinweise"
}

/** Hinweis oben in der App (rot: Backup, gelb: Archiv) – nur, wenn der Server ihn liefert. */
data class HealthBanner(val critical: Boolean, val text: String, val key: String)

/** `GET /system/health-banner`: Der Server entscheidet, welche Hinweise gelten (docs/backup.md). */
object SystemHealth {
    fun banners(response: JsonElement?): List<HealthBanner> {
        val obj = response as? JsonObject ?: return emptyList()
        val list = obj.arr("banners") ?: return emptyList()
        return list.mapNotNull { it as? JsonObject }.mapIndexedNotNull { index, b ->
            val text = b.str("text")?.takeIf { it.isNotBlank() } ?: return@mapIndexedNotNull null
            val critical = b.str("severity")?.lowercase() in setOf("critical", "error", "danger", "red")
            HealthBanner(critical, text, b.str("key") ?: if (critical) "backup" else "archive-$index")
        }
    }
}
