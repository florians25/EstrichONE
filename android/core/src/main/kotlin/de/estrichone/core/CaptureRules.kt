package de.estrichone.core

import kotlin.math.abs
import kotlin.math.max
import kotlin.math.min
import kotlin.math.roundToInt

/**
 * Ausgelöste, aber noch nicht gespeicherte Fotos der Kamera und der Wunsch zu schließen (wie PendingShots auf iOS).
 * „Fertig“ schließt erst, wenn nichts mehr unterwegs ist – sonst gingen die letzten Fotos einer Serie verloren –,
 * hängt eine Aufnahme, nach Ablauf der Wartezeit ([forced]). Geschlossen wird genau einmal.
 */
data class CameraShots(
    val pending: Int = 0,
    val closing: Boolean = false,
    private val forcedClose: Boolean = false,
    private val done: Boolean = false,
) {
    fun pressed() = copy(pending = pending + 1)
    fun settled() = copy(pending = max(0, pending - 1))
    fun closeRequested() = copy(closing = true)
    fun forced() = if (closing) copy(forcedClose = true) else this
    val canClose: Boolean get() = closing && !done && (pending == 0 || forcedClose)
    fun closed() = copy(done = true)
}

/**
 * Videos für die Übertragung (wie VideoCompressor auf iOS): höchstens 720p, etwa 2 Mbit/s, HEVC mit Rückfall auf
 * H.264, höchstens 2 Minuten – rechnerisch rund 15 MB je Minute.
 */
object VideoTarget {
    const val MAX_LONG_EDGE = 1280
    const val MAX_SHORT_EDGE = 720
    const val VIDEO_BITRATE = 2_000_000
    const val AUDIO_BITRATE = 64_000
    const val MAX_DURATION_MS = 120_000L

    /** Rechnerische Größe je Minute: (Video + Ton) × 60 s / 8. */
    const val BYTES_PER_MINUTE = (VIDEO_BITRATE + AUDIO_BITRATE) * 60 / 8

    /** Zielgröße (Breite, Höhe) im angezeigten Bild: Seitenverhältnis bleibt, nie vergrößert, gerade Pixelzahlen. */
    fun size(width: Int, height: Int): Pair<Int, Int> {
        val w = abs(width).toDouble()
        val h = abs(height).toDouble()
        if (w <= 0 || h <= 0) return MAX_LONG_EDGE to MAX_SHORT_EDGE
        val scale = min(1.0, min(MAX_LONG_EDGE / max(w, h), MAX_SHORT_EDGE / min(w, h)))
        fun even(value: Double) = max(2, (value * scale / 2).roundToInt() * 2)
        return even(w) to even(h)
    }
}
