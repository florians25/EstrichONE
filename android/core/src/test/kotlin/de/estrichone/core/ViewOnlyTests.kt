package de.estrichone.core

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.DisplayName
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.assertThrows

/** „Nur ansehen“: Änderungen an nicht zugeteilten Bauakten kommen gar nicht erst in die Warteschlange. */
class ViewOnlyTests {
    private val assigned = "0190a000-0000-7000-8000-000000000001"
    private val foreign = "0190a000-0000-7000-8000-000000000002"

    private fun bauakte(id: String, title: String) =
        SyncChange(EntityTypes.BAUAKTE, id, data = json("id" to id, "title" to title, "status" to "planned", "version" to 1))

    private fun service(assignedIds: List<String>?): Pair<LocalStore, BauakteService> {
        val store = LocalStore(MemoryStorage())
        store.applyPage(listOf(bauakte(assigned, "Eigene"), bauakte(foreign, "Fremde")), "1:1", listOf(assigned, foreign), assignedIds)
        return store to BauakteService(store, MemoryFileVault()) { DemoData.USER_ID }
    }

    @Test
    @DisplayName("Sichtbar, aber nicht zugeteilt: Notiz, Status und Foto abgewiesen; nach dem Eintragen erlaubt")
    fun blocksUntilJoined() {
        val (store, service) = service(listOf(assigned))
        assertTrue(store.isViewOnly(foreign))
        assertFalse(store.isViewOnly(assigned))
        assertFalse(service.canEdit(foreign))

        val error = assertThrows<BauakteNotAssignedException> { service.addNote(foreign, "Darf ich?") }
        assertEquals(BauakteNotAssignedException.MESSAGE, error.message)
        assertThrows<BauakteNotAssignedException> { service.setStatus(foreign, BauakteStatus.InProgress) }
        assertEquals("", service.addFile(foreign, "photo", byteArrayOf(1, 2, 3), "Foto.jpg", "image/jpeg"))
        assertTrue(store.backend.outbox().isEmpty())

        service.addNote(assigned, "Material da")
        store.setViewOnly(foreign, false)
        service.addNote(foreign, "Jetzt dabei")
        assertEquals(2, store.backend.outbox().size)
        assertEquals(BauakteNotAssignedException.messageFor("BAUAKTE_NOT_ASSIGNED")?.contains("Beteiligter"), true)
    }

    @Test
    @DisplayName("Ältere Server ohne Angabe der bearbeitbaren Bauakten: alles bleibt bearbeitbar")
    fun olderServer() {
        val (store, service) = service(null)
        assertFalse(store.isViewOnly(foreign))
        service.addNote(foreign, "Geht")
        assertEquals(1, store.backend.outbox().size)
    }
}
