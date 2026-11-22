package de.estrichone.core

import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.DisplayName
import org.junit.jupiter.api.Test

class FreeTextTests {
    private fun bauakte(vararg fields: Pair<String, Any?>) = Bauakte(LocalEntity(EntityTypes.BAUAKTE, "0193a1b2-0000-7000-8000-000000000001",
        JsonObject(fields.associate { (k, v) -> k to (v as? JsonObject ?: JsonPrimitive(v as String?)) } + ("title" to JsonPrimitive("EFH"))), SyncState.Synced))

    @Test
    @DisplayName("Freitext: Links, E-Mail und Telefon erkannt, Zeilenumbrüche bleiben")
    fun segments() {
        val parts = FreeText.segments("Plan: https://example.com/plan.pdf.\nMail bau@firma.de, Tel. 0871 123456 oder +49 (0)171 2233445")
        assertEquals(FreeText.Segment("https://example.com/plan.pdf", FreeText.Kind.Url, "https://example.com/plan.pdf"), parts[1])
        assertEquals(".\nMail ", parts[2].text)
        assertEquals("mailto:bau@firma.de", parts.first { it.kind == FreeText.Kind.Mail }.target)
        assertEquals(listOf("tel:0871123456", "tel:+491712233445"), parts.filter { it.kind == FreeText.Kind.Phone }.map { it.target })
        assertEquals("https://www.estrich.de", FreeText.segments("www.estrich.de").single().target)
        assertTrue(FreeText.segments("Ab 08.12.2026, 45 mm, PLZ 84028").all { it.kind == FreeText.Kind.Text })
        assertEquals(1, FreeText.segments("javascript:alert(1)").size)
        assertEquals("Zeile 1\nZeile 2", FreeText.segments("Zeile 1\r\nZeile 2").single().text)
    }

    @Test
    @DisplayName("Freitext: lange Texte werden gekürzt angezeigt")
    fun long() {
        assertFalse(FreeText.isLong("kurz"))
        assertTrue(FreeText.isLong("x".repeat(400)))
        assertTrue(FreeText.isLong("1\n2\n3\n4\n5\n6"))
    }

    @Test
    @DisplayName("Bauakte: Hinweise, Beschreibung und mehrzeilige Schema-Felder als Textblöcke, leere entfallen")
    fun blocks() {
        val schema = FormSchema("bauakte", 1, listOf(FormGroup("site", "Baustelle", listOf(
            FormField("siteAccess", "Zugang / Besonderheiten", "textarea", null, null, null, false, "project.siteAccess", null, false),
            FormField("remarks", "Bemerkungen", "textarea", null, null, null, false, "project.custom.remarks", null, false),
            FormField("floors", "Geschosse", "text", null, null, null, false, "project.floors", null, false),
        ))))
        val b = bauakte("siteNotes" to "Hund im Hof", "description" to " ", "project" to JsonObject(mapOf(
            "siteAccess" to JsonPrimitive("Pumpe vorne"), "floors" to JsonPrimitive("EG"),
            "custom" to JsonObject(mapOf("remarks" to JsonPrimitive("Schlüssel beim Nachbarn"))))))
        val blocks = FreeText.blocks(b, schema)
        assertEquals(listOf("siteNotes", "siteAccess", "remarks"), blocks.map { it.key })
        assertTrue(blocks.first().important)
        assertEquals("Schlüssel beim Nachbarn", blocks.last().text)
        assertEquals(listOf("siteNotes"), FreeText.blocks(b, null).map { it.key })
    }
}
