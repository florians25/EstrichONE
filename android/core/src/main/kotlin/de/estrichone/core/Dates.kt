package de.estrichone.core

import java.time.Instant
import java.time.LocalDate
import java.time.LocalTime
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.time.temporal.ChronoUnit
import java.util.Locale

/** Deutsche Datums- und Zeitangaben wie in der iOS-App. */
object Dates {
    val german: Locale = Locale.GERMANY
    var zone: ZoneId = ZoneId.systemDefault()

    private val weekdayLong = DateTimeFormatter.ofPattern("EEEE, d. MMMM", german)
    private val weekdayLongYear = DateTimeFormatter.ofPattern("EEEE, d. MMMM yyyy", german)
    private val dayMonth = DateTimeFormatter.ofPattern("dd.MM.", german)
    private val date = DateTimeFormatter.ofPattern("dd.MM.yyyy", german)
    private val dateTime = DateTimeFormatter.ofPattern("dd.MM.yyyy, HH:mm", german)
    private val time = DateTimeFormatter.ofPattern("HH:mm", german)
    private val weekdays = listOf("Mo.", "Di.", "Mi.", "Do.", "Fr.", "Sa.", "So.")
    private val weekdayNames = listOf("Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag", "Sonntag")

    /** „Dienstag, 27. Oktober“. */
    fun long(day: LocalDate, today: LocalDate = LocalDate.now(zone)): String =
        if (day.year == today.year) day.format(weekdayLong) else day.format(weekdayLongYear)

    fun dayMonth(day: LocalDate): String = day.format(dayMonth)
    fun date(day: LocalDate): String = day.format(date)
    fun weekdayShort(day: LocalDate): String = weekdays[day.dayOfWeek.value - 1]
    fun weekdayName(day: LocalDate): String = weekdayNames[day.dayOfWeek.value - 1]

    /** „Heute“, „Morgen“, „Gestern“, sonst „Mi., 11.11.“. */
    fun relativeDay(day: LocalDate, today: LocalDate = LocalDate.now(zone)): String = when (ChronoUnit.DAYS.between(today, day)) {
        0L -> "Heute"
        1L -> "Morgen"
        -1L -> "Gestern"
        else -> "${weekdayShort(day)}, ${dayMonth(day)}"
    }

    fun dateTime(instant: Instant): String = instant.atZone(zone).format(dateTime)
    fun time(instant: Instant): String = instant.atZone(zone).format(time)

    /** „07:00:00“ → „07:00“. */
    fun clock(value: String?): String? = value?.takeIf { it.length >= 5 }?.take(5)

    /** „vor 5 Min.“, „heute, 14:20“, „gestern, 09:10“, „11.11.2026“. */
    fun ago(instant: Instant, now: Instant = Instant.now()): String {
        val seconds = ChronoUnit.SECONDS.between(instant, now)
        if (seconds < 60) return "gerade eben"
        if (seconds < 3600) return "vor ${seconds / 60} Min."
        val day = instant.atZone(zone).toLocalDate()
        val today = now.atZone(zone).toLocalDate()
        return when (ChronoUnit.DAYS.between(day, today)) {
            0L -> "heute, ${time(instant)}"
            1L -> "gestern, ${time(instant)}"
            in 2L..6L -> "${weekdayShort(day)}, ${time(instant)}"
            else -> date(day)
        }
    }

    /** Begrüßung nach Tageszeit. */
    fun greeting(now: LocalTime = LocalTime.now(zone)): String = when (now.hour) {
        in 4..10 -> "Guten Morgen"
        in 11..17 -> "Guten Tag"
        else -> "Guten Abend"
    }
}
