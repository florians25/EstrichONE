package de.estrichone.core

import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.DisplayName
import org.junit.jupiter.api.Test

class SearchTests {
    private fun entity(type: String, vararg fields: Pair<String, Any>) = LocalEntity(type, "0193a1b2-0000-7000-8000-000000000001",
        JsonObject(fields.associate { (k, v) -> k to (v as? JsonArray ?: JsonPrimitive(v.toString())) }), SyncState.Synced)

    private val customer = Contact(entity("contact", "name" to "Familie Schneider", "customerNumber" to "K-1088", "city" to "Landshut",
        "phones" to JsonArray(listOf(JsonObject(mapOf("kind" to JsonPrimitive("mobile"), "number" to JsonPrimitive("0171 / 234 56 78")))))))
    private val bauakte = Bauakte(entity("bauakte", "number" to "2026-041", "title" to "EFH Schneider, Neubau", "street" to "Ahornweg 3",
        "postalCode" to "84028", "city" to "Landshut"))

    @Test
    @DisplayName("Bauakten: Nummer, Adresse, Kunde und Kundennummer; mehrere Wörter, ohne Akzente")
    fun bauakten() {
        assertTrue(bauakte.matches("2026-041"))
        assertTrue(bauakte.matches("ahornweg landshut"))
        assertFalse(bauakte.matches("K-1088"))
        assertTrue(bauakte.matches("K-1088", customer))
        assertTrue(bauakte.matches("familie neubau", customer))
        assertTrue(bauakte.matches("  "))
        assertFalse(bauakte.matches("ahornweg münchen", customer))
        assertTrue(Bauakte(entity("bauakte", "title" to "Halle Müller")).matches("muller"))
    }

    @Test
    @DisplayName("Kunden: Name, Kundennummer, Ort, Telefon ohne Leerzeichen")
    fun kunden() {
        assertTrue(customer.matches("k-1088"))
        assertTrue(customer.matches("schneider landshut"))
        assertTrue(customer.matches("0171234"))
        assertTrue(customer.matches("0171 234"))
        assertFalse(customer.matches("0899"))
        assertFalse(customer.matches("Huber"))
    }
}
