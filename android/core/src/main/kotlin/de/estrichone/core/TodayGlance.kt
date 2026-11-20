package de.estrichone.core

import java.time.Instant
import java.time.LocalDate
import java.time.LocalTime
import java.util.Locale
import kotlin.math.atan2
import kotlin.math.cos
import kotlin.math.roundToInt
import kotlin.math.sin
import kotlin.math.sqrt

/**
 * „Heute“ auf einen Blick (wie TodayGlance.swift): Zeitphase eines Einsatzes, Kennzahlen der Baustelle (offene Mängel und
 * Aufgaben, Belegreife, Wetter laut Bautagebuch) und Entfernung. Alles aus Daten, die schon auf dem Gerät liegen.
 */
object TodayGlance {
    /** Wo ein Einsatz im Tagesablauf steht. */
    sealed interface Phase {
        data class Upcoming(val minutes: Int) : Phase
        data object Running : Phase
        data object Finished : Phase
        data object Unknown : Phase

        /** „Jetzt“, „in 25 Min.“, „in 2 Std.“, „Vorbei“; ohne Uhrzeit `null`. */
        val text: String?
            get() = when (this) {
                Running -> "Jetzt"
                Finished -> "Vorbei"
                Unknown -> null
                is Upcoming -> when {
                    minutes < 60 -> "in ${maxOf(minutes, 1)} Min."
                    minutes < 120 -> "in 1 Std."
                    else -> "in ${minutes / 60} Std."
                }
            }
    }

    /** Phase aus „HH:mm[:ss]“ (Beginn, Ende). Ohne Ende gilt der Einsatz ab Beginn als laufend. */
    fun phase(start: String?, end: String?, now: LocalTime): Phase {
        fun minutes(text: String?): Int? {
            val parts = text?.split(":")?.take(2)?.mapNotNull { it.toIntOrNull() } ?: return null
            return if (parts.size == 2) parts[0] * 60 + parts[1] else null
        }
        val begin = minutes(start) ?: return Phase.Unknown
        val current = now.hour * 60 + now.minute
        if (current < begin) return Phase.Upcoming(begin - current)
        val finish = minutes(end)
        if (finish != null && finish > begin && current >= finish) return Phase.Finished
        return Phase.Running
    }

    /** Der Einsatz, der aufgeklappt gezeigt wird: der laufende, sonst der nächste, sonst der letzte. */
    fun focusIndex(phases: List<Phase>): Int? {
        if (phases.isEmpty()) return null
        phases.indexOfLast { it == Phase.Running }.takeIf { it >= 0 }?.let { return it }
        phases.indexOfFirst { it is Phase.Upcoming }.takeIf { it >= 0 }?.let { return it }
        return phases.size - 1
    }

    /** „850 m“, „1,8 km“, „23 km“ (Luftlinie). */
    fun distanceText(meters: Double): String = when {
        meters < 1000 -> "${maxOf((meters / 50).roundToInt() * 50, 50)} m"
        meters < 10_000 -> String.format(Locale.GERMANY, "%.1f km", meters / 1000)
        else -> "${(meters / 1000).roundToInt()} km"
    }

    /** Luftlinie in Metern (Haversine). */
    fun distance(lat1: Double, lon1: Double, lat2: Double, lon2: Double): Double {
        val dLat = Math.toRadians(lat2 - lat1)
        val dLon = Math.toRadians(lon2 - lon1)
        val a = sin(dLat / 2) * sin(dLat / 2) + cos(Math.toRadians(lat1)) * cos(Math.toRadians(lat2)) * sin(dLon / 2) * sin(dLon / 2)
        return 6_371_000.0 * 2 * atan2(sqrt(a), sqrt(1 - a))
    }
}

/** Kennzahlen einer Baustelle für die Karte auf „Heute“ (wie SiteGlance in TodayGlance.swift). */
data class SiteGlance(
    val openDefects: Int = 0,
    val openTasks: Int = 0,
    val readiness: Readiness? = null,
    val weather: DiaryWeather? = null,
    val temperature: Int? = null,
) {
    sealed interface Readiness {
        data class Ready(val cm: Double?) : Readiness
        data class NotReady(val cm: Double?) : Readiness
        data class Rooms(val ready: Int, val total: Int) : Readiness
        data class Laid(val laid: Int, val total: Int) : Readiness

        val text: String
            get() = when (this) {
                is Ready -> cm?.let { "Belegreif (${percent(it)} CM-%)" } ?: "Belegreif"
                is NotReady -> cm?.let { "Noch nicht belegreif (${percent(it)} CM-%)" } ?: "Noch nicht belegreif"
                is Rooms -> "$ready von $total Räumen belegreif"
                is Laid -> "$laid von $total Räumen eingebracht"
            }

        val isReady: Boolean
            get() = this is Ready || (this is Rooms && ready == total)
    }

    val defectsText: String? get() = when (openDefects) { 0 -> null; 1 -> "1 Mangel"; else -> "$openDefects Mängel" }
    val tasksText: String? get() = when (openTasks) { 0 -> null; 1 -> "1 Aufgabe"; else -> "$openTasks Aufgaben" }
    val weatherText: String?
        get() = when {
            weather != null && temperature != null -> "${weather.label}, $temperature °C"
            weather != null -> weather.label
            temperature != null -> "$temperature °C"
            else -> null
        }
    val isEmpty: Boolean get() = openDefects == 0 && openTasks == 0 && readiness == null && weatherText == null

    companion object {
        /** Aus den lokalen Daten einer Bauakte. */
        fun of(tasks: List<Task>, rooms: List<Room>, inspections: List<Inspection>, diary: List<DiaryEntry>, today: LocalDate): SiteGlance {
            val open = tasks.filter { it.isOpen }
            val measurement = inspections.filter { it.isCmMeasurement && !it.notRequired && it.ready != null }
                .maxByOrNull { it.performedAt ?: Instant.MIN }
            val ready = rooms.count { it.status == RoomStatus.Ready }
            val laid = rooms.count { it.status == RoomStatus.Laid }
            val readiness = when {
                measurement != null -> if (measurement.ready == true) Readiness.Ready(measurement.measured) else Readiness.NotReady(measurement.measured)
                rooms.isNotEmpty() && ready > 0 -> Readiness.Rooms(ready, rooms.size)
                rooms.isNotEmpty() && laid > 0 -> Readiness.Laid(laid, rooms.size)
                else -> null
            }
            val entry = diary.firstOrNull { it.day == today }
            return SiteGlance(open.count { it.kind == WorkItemKind.Defect }, open.count { it.kind == WorkItemKind.Task }, readiness,
                entry?.weather, entry?.temperature)
        }
    }
}

private fun percent(value: Double) = String.format(Locale.GERMANY, "%.1f", value)

/** Ergebnis der CM-Messung: vom Server, sonst lokal aus Messwert und Grenzwert. */
private val Inspection.ready: Boolean?
    get() = isReady ?: measured?.let { value -> limit?.let { value <= it } }
