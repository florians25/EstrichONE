package de.estrichone.core

import kotlinx.serialization.Serializable
import java.time.Duration
import java.time.Instant
import kotlin.math.ceil

/**
 * Update-Ablauf (deploy/README.md, „Update-Ablauf“) – Feld `update` in `GET /system/notices`:
 * `announce` (Countdown, Arbeit speichern, Abgleich), `update` (Sitzungen beendet), `finish` (Freigabe folgt),
 * `rollback`, `failed`. Texte wie im Web (util/updateView.ts) und auf iOS (UpdatePresentation).
 */
@Serializable
data class UpdateMaintenance(
    val id: String,
    val phase: String,
    val fromVersion: String? = null,
    val targetVersion: String? = null,
    val startedAt: String? = null,
    val phaseStartedAt: String? = null,
    val phaseEndsAt: String? = null,
    val message: String? = null,
) {
    val phaseStart: Instant? get() = ApiDate.parse(phaseStartedAt)
    val phaseEnd: Instant? get() = ApiDate.parse(phaseEndsAt)
}

/** Aufbereitet für Leiste und Wartungsbildschirm. */
data class UpdateScreen(
    val banner: Boolean,
    val bannerText: String,
    val page: Boolean,
    val title: String,
    val text: String,
    val countdown: String,
    val countdownLabel: String,
    /** 0.0–1.0 innerhalb der Phase. */
    val progress: Float,
    /** Schritte mit Zustand: done, now, todo. */
    val steps: List<Pair<String, String>>,
    val failed: Boolean,
    val version: String,
)

object UpdatePresentation {
    val phases = setOf("announce", "update", "finish", "rollback", "failed")

    fun clock(seconds: Double): String {
        val s = maxOf(0L, ceil(seconds).toLong())
        return "${s / 60}:${(s % 60).toString().padStart(2, '0')}"
    }

    fun secondsLeft(state: UpdateMaintenance, now: Instant): Double? =
        state.phaseEnd?.let { Duration.between(now, it).toMillis() / 1000.0 }

    fun screen(state: UpdateMaintenance, now: Instant): UpdateScreen {
        val target = state.targetVersion?.takeIf { it.isNotBlank() }?.let { "Version $it" } ?: "die neue Version"
        val left = secondsLeft(state, now)
        val running = left != null && left > 0
        val start = state.phaseStart
        val end = state.phaseEnd
        val progress = if (start != null && end != null && end > start) {
            (Duration.between(start, now).toMillis().toFloat() / Duration.between(start, end).toMillis()).coerceIn(0f, 1f)
        } else if (state.phase == "update" || state.phase == "rollback") 1f else 0f
        val announcing = state.phase == "announce" && (left == null || left > 0)
        val index = when (state.phase) { "announce" -> 0; "update" -> 1; else -> 2 }
        val third = when (state.phase) { "rollback" -> "Rücknahme"; "failed" -> "Fehlgeschlagen"; else -> "Abschluss" }
        fun steps(i: Int) = listOf("Ankündigung", "Update", third).mapIndexed { n, label ->
            label to when { n < i -> "done"; n == i -> "now"; else -> "todo" }
        }
        val version = state.targetVersion ?: ""
        val failed = state.phase == "failed"
        if (announcing) {
            val text = if (running) "Update auf $target beginnt in ${clock(left!!)} – bitte Arbeit speichern. Offene Änderungen werden jetzt übertragen."
            else "Update auf $target beginnt gleich – bitte Arbeit speichern."
            return UpdateScreen(true, text, false, "Update angekündigt", text, if (running) clock(left!!) else "", "Beginnt in",
                progress, steps(0), false, version)
        }
        return when (state.phase) {
            "announce", "update" -> UpdateScreen(false, "", true, "Update wird eingespielt",
                "EstrichONE wird auf $target aktualisiert. Erfasste Daten bleiben auf dem Gerät und werden danach übertragen.",
                if (running) clock(left!!) else "", if (running) "Mindestens noch" else "Läuft …", progress, steps(1), false, version)
            "finish" -> UpdateScreen(false, "", true, "Update wird abgeschlossen",
                "$target läuft. Letzte Prüfungen – danach geht es mit einer neuen Anmeldung weiter.",
                if (running) clock(left!!) else "", if (running) "Freigabe in" else "Gleich geschafft …", progress, steps(index), false, version)
            "rollback" -> UpdateScreen(false, "", true, "Update wird zurückgenommen",
                "Das Update auf $target hat nicht geklappt. Der bisherige Stand wird wiederhergestellt.", "", "Läuft …", progress, steps(2), false, version)
            else -> UpdateScreen(false, "", true, "Update fehlgeschlagen",
                "${state.message?.takeIf { it.isNotBlank() } ?: "Das Update auf $target konnte nicht eingespielt werden."} Der Administrator ist informiert.",
                if (running) clock(left!!) else "", if (running) "Freigabe in" else "Gesperrt, bis das Problem behoben ist", progress, steps(2), failed, version)
        }
    }

    /** Hinweis nach der Freigabe (Anmeldeseite). */
    fun doneText(version: String?, failed: Boolean): String = when {
        failed -> "Das Update konnte nicht eingespielt werden – EstrichONE läuft mit dem bisherigen Stand weiter. Bitte erneut anmelden."
        !version.isNullOrBlank() -> "EstrichONE wurde auf Version $version aktualisiert. Bitte erneut anmelden."
        else -> "EstrichONE wurde aktualisiert. Bitte erneut anmelden."
    }
}
