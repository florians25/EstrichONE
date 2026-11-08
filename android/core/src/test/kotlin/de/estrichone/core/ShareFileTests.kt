package de.estrichone.core

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.DisplayName
import org.junit.jupiter.api.Test

class ShareFileTests {
    @Test
    @DisplayName("Teilen: Videos immer mit Videoendung und Videotyp (WhatsApp nimmt sonst kein Video an)")
    fun videos() {
        assertEquals("Estrich EG.mp4", ShareFile.name("Estrich EG", "VID_1.mp4", "video/mp4", isVideo = true, isPhoto = false))
        assertEquals("Estrich EG.mov", ShareFile.name("Estrich EG", "aufnahme.mov", "video/quicktime", isVideo = true, isPhoto = false))
        assertEquals("Clip.mp4", ShareFile.name("Clip", "upload.bin", "application/octet-stream", isVideo = true, isPhoto = false))
        assertEquals("Clip.mov", ShareFile.name("Clip", "", "video/quicktime", isVideo = true, isPhoto = false))
        assertEquals("video/mp4", ShareFile.mime("application/octet-stream", isVideo = true, isPhoto = false))
        assertEquals("video/quicktime", ShareFile.mime("video/quicktime", isVideo = true, isPhoto = false))
    }

    @Test
    @DisplayName("Teilen: Fotos und Dokumente, unzulässige Zeichen im Namen")
    fun others() {
        assertEquals("Bad OG_Rand.jpg", ShareFile.name("Bad OG/Rand", "IMG_1.jpg", "image/jpeg", isVideo = false, isPhoto = true))
        assertEquals("Foto.jpg", ShareFile.name("Foto", "", "image/jpeg", isVideo = false, isPhoto = true))
        assertEquals("image/jpeg", ShareFile.mime("", isVideo = false, isPhoto = true))
        assertEquals("Aufmaß.pdf", ShareFile.name("Aufmaß", "", "application/pdf", isVideo = false, isPhoto = false))
        assertEquals("Datei.bin", ShareFile.name("  ", "", "application/zip", isVideo = false, isPhoto = false))
    }
}
