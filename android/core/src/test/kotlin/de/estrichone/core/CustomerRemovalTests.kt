package de.estrichone.core

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.DisplayName
import org.junit.jupiter.api.Test

class CustomerRemovalTests {
    private fun service(store: LocalStore) = BauakteService(store, MemoryFileVault()) { DemoData.USER_ID }

    @Test
    @DisplayName("Archivierte Kunden: in Auswahllisten ausgeblendet, an der Bauakte weiter mit Namen")
    fun archivedCustomersHiddenFromPickers() {
        val store = LocalStore(MemoryStorage())
        store.applyPage(listOf(
            SyncChange(EntityTypes.CONTACT, "k1", false, json("id" to "k1", "name" to "Aktiv GmbH", "kind" to "company")),
            SyncChange(EntityTypes.CONTACT, "k2", false, json("id" to "k2", "name" to "Archiv KG", "kind" to "company", "archivedAt" to "2026-11-01T08:00:00Z")),
            SyncChange(EntityTypes.BAUAKTE, "b1", false, json("id" to "b1", "title" to "Halle", "customerId" to "k2")),
        ), "c1", null)
        val service = service(store)
        assertEquals(listOf("Aktiv GmbH", "Archiv KG"), service.contacts().map { it.name })
        assertEquals(listOf("Aktiv GmbH"), service.activeContacts().map { it.name })
        val archived = service.contact("k2")!!
        assertTrue(archived.isArchived)
        assertNull(archived.anonymizedAt)
        assertFalse(service.contact("k1")!!.isArchived)
        assertEquals("Archiv KG", service.contact(service.bauakte("b1")!!.customerId)!!.name)
    }

    @Test
    @DisplayName("Gelöschter Kunde: Tombstone aus dem Abgleich entfernt ihn vom Gerät; Reaktivierung zeigt ihn wieder")
    fun tombstoneRemovesCustomer() {
        val store = LocalStore(MemoryStorage())
        store.applyPage(listOf(
            SyncChange(EntityTypes.CONTACT, "k1", false, json("id" to "k1", "name" to "Weg GmbH", "archivedAt" to "2026-11-01T08:00:00Z")),
            SyncChange(EntityTypes.CONTACT, "k2", false, json("id" to "k2", "name" to "Bleibt", "archivedAt" to "2026-11-01T08:00:00Z")),
        ), "c1", null)
        store.applyPage(listOf(
            SyncChange(EntityTypes.CONTACT, "k1", true, null),
            SyncChange(EntityTypes.CONTACT, "k2", false, json("id" to "k2", "name" to "Bleibt")),
        ), "c2", null)
        val service = service(store)
        assertNull(service.contact("k1"))
        assertEquals(listOf("Bleibt"), service.activeContacts().map { it.name })
    }

    @Test
    @DisplayName("Bezüge beschreiben und Bestätigung prüfen")
    fun describeAndConfirm() {
        assertEquals("", CustomerRemoval.describe(emptyList()))
        assertEquals("1 Bauakte", CustomerRemoval.describe(listOf(CustomerReference("bauakten", "Bauakte", 1))))
        assertEquals("2 Bauakten, 1 Projektbeteiligung und 3 Anrufe und Anfragen", CustomerRemoval.describe(listOf(
            CustomerReference("bauakten", "Bauakten", 2), CustomerReference("parties", "Projektbeteiligung", 1),
            CustomerReference("calls", "Anrufe und Anfragen", 3))))
        assertTrue(CustomerRemoval.confirms(" 600123 ", "600123"))
        assertTrue(CustomerRemoval.confirms("familie huber", "Familie Huber"))
        assertFalse(CustomerRemoval.confirms("", ""))
        assertFalse(CustomerRemoval.confirms("60012", "600123"))
        val refs = EstrichOneApi.json.decodeFromString(CustomerReferences.serializer(),
            """{"id":"k1","name":"Huber","customerNumber":"600001","deletable":false,"confirmText":"600001","archived":false,"anonymized":false,
               "references":[{"kind":"bauakten","label":"Bauakte","count":1,"link":"/bauakten?kunde=k1","items":[{"id":"b1","text":"B-1 · Halle","link":null}]}]}""")
        assertEquals("1 Bauakte", refs.summary)
        assertEquals("B-1 · Halle", refs.references.single().items.single().text)
    }
}
