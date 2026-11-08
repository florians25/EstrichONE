package de.estrichone.core

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.DisplayName
import org.junit.jupiter.api.Test

class CaptureRulesTests {
    @Test
    @DisplayName("„Fertig“ wartet, bis alle ausgelösten Fotos gespeichert sind, und schließt genau einmal")
    fun closeWaitsForPendingShots() {
        var shots = CameraShots().pressed().pressed().closeRequested()
        assertTrue(shots.closing)
        assertFalse(shots.canClose)
        shots = shots.settled()
        assertFalse(shots.canClose)
        shots = shots.settled()
        assertTrue(shots.canClose)
        assertFalse(shots.closed().canClose)
    }

    @Test
    @DisplayName("Ohne Fotos unterwegs sofort schließen; hängt eine Aufnahme, nach Ablauf der Wartezeit")
    fun closeImmediatelyOrAfterTimeout() {
        assertTrue(CameraShots().closeRequested().canClose)
        val stuck = CameraShots().pressed()
        assertFalse(stuck.forced().canClose)
        assertFalse(stuck.closeRequested().canClose)
        assertTrue(stuck.closeRequested().forced().canClose)
        assertEquals(0, CameraShots().settled().pending)
    }

    @Test
    @DisplayName("Videos: höchstens 720p im Hoch- und Querformat, nie vergrößert, rund 15 MB je Minute")
    fun videoTarget() {
        assertEquals(1280 to 720, VideoTarget.size(1920, 1080))
        assertEquals(1280 to 720, VideoTarget.size(3840, 2160))
        assertEquals(720 to 1280, VideoTarget.size(1080, 1920))
        assertEquals(960 to 720, VideoTarget.size(1440, 1080))
        assertEquals(640 to 480, VideoTarget.size(640, 480))
        assertEquals(15_480_000, VideoTarget.BYTES_PER_MINUTE)
    }
}
