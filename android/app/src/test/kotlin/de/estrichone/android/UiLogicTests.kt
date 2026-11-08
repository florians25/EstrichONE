package de.estrichone.android

import de.estrichone.android.ui.duplicates
import de.estrichone.android.ui.parseColor
import de.estrichone.core.Contact
import de.estrichone.core.EntityTypes
import de.estrichone.core.LocalEntity
import de.estrichone.core.SyncState
import de.estrichone.core.json
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** Logik der Oberfläche ohne Android (JVM). */
class UiLogicTests {
    private fun contact(id: String, name: String, city: String) =
        Contact(LocalEntity(EntityTypes.CONTACT, id, json("id" to id, "name" to name, "city" to city), SyncState.Synced))

    @Test
    fun duplicatesFindSimilarCustomersPreferringSameCity() {
        val list = listOf(contact("1", "Familie Schneider", "Landshut"), contact("2", "Schneider Bau GmbH", "Ergolding"), contact("3", "Huber", "Landshut"))
        val found = duplicates(list, "Schneider, Peter", "Ergolding")
        assertEquals(listOf("2", "1"), found.map { it.id })
        assertTrue(duplicates(list, "Familie", "").isEmpty())
    }

    @Test
    fun crewColorsAreParsed() {
        assertEquals(0xFFE07A1F.toInt(), parseColor("#E07A1F")!!.value.shr(32).toInt())
        assertNull(parseColor("rot"))
    }
}
