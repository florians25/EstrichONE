package de.estrichone.core

import java.time.DayOfWeek
import java.time.LocalDate

/**
 * Gesetzliche Feiertage je Bundesland (wie GermanHolidays im Server). Nur für den Demomodus; im Betrieb rechnet der Server
 * mit dem Bundesland aus den Personaleinstellungen.
 */
object GermanHolidays {
    fun easterSunday(year: Int): LocalDate {
        val a = year % 19
        val b = year / 100
        val c = year % 100
        val d = b / 4
        val e = b % 4
        val f = (b + 8) / 25
        val g = (b - f + 1) / 3
        val h = (19 * a + b - d - g + 15) % 30
        val i = c / 4
        val k = c % 4
        val l = (32 + 2 * e + 2 * i - h - k) % 7
        val m = (a + 11 * h + 22 * l) / 451
        val month = (h + l - 7 * m + 114) / 31
        val day = ((h + l - 7 * m + 114) % 31) + 1
        return LocalDate.of(year, month, day)
    }

    fun of(year: Int, state: String): List<AbsenceHoliday> {
        val easter = easterSunday(year)
        val list = mutableListOf(
            LocalDate.of(year, 1, 1) to "Neujahr", easter.minusDays(2) to "Karfreitag", easter.plusDays(1) to "Ostermontag",
            LocalDate.of(year, 5, 1) to "Tag der Arbeit", easter.plusDays(39) to "Christi Himmelfahrt", easter.plusDays(50) to "Pfingstmontag",
            LocalDate.of(year, 10, 3) to "Tag der Deutschen Einheit", LocalDate.of(year, 12, 25) to "1. Weihnachtstag",
            LocalDate.of(year, 12, 26) to "2. Weihnachtstag",
        )
        fun add(condition: Boolean, date: LocalDate, name: String) {
            if (condition) list += date to name
        }
        add(state in setOf("BW", "BY", "ST"), LocalDate.of(year, 1, 6), "Heilige Drei Könige")
        add(state == "BE" || (state == "MV" && year >= 2023), LocalDate.of(year, 3, 8), "Internationaler Frauentag")
        add(state == "BB", easter, "Ostersonntag")
        add(state == "BB", easter.plusDays(49), "Pfingstsonntag")
        add(state in setOf("BW", "BY", "HE", "NW", "RP", "SL"), easter.plusDays(60), "Fronleichnam")
        add(state in setOf("BY", "SL"), LocalDate.of(year, 8, 15), "Mariä Himmelfahrt")
        add(state == "TH" && year >= 2019, LocalDate.of(year, 9, 20), "Weltkindertag")
        add(state in setOf("BB", "HB", "HH", "MV", "NI", "SN", "ST", "SH", "TH") || year == 2017, LocalDate.of(year, 10, 31), "Reformationstag")
        add(state in setOf("BW", "BY", "NW", "RP", "SL"), LocalDate.of(year, 11, 1), "Allerheiligen")
        if (state == "SN") {
            var day = LocalDate.of(year, 11, 22)
            while (day.dayOfWeek != DayOfWeek.WEDNESDAY) day = day.minusDays(1)
            list += day to "Buß- und Bettag"
        }
        return list.distinctBy { it.first }.sortedBy { it.first }.map { AbsenceHoliday(it.first.toString(), it.second) }
    }

    /** Feiertage von [from] bis [to] einschließlich. */
    fun between(from: LocalDate, to: LocalDate, state: String): List<AbsenceHoliday> =
        (from.year..to.year).flatMap { of(it, state) }.filter { LocalDate.parse(it.date) in from..to }
}
