package de.estrichone.android

import de.estrichone.android.ui.duplicates
import de.estrichone.android.ui.keyDataFirst
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
    fun navigationHasChatTabAndCapturesInsideBauakte() {
        val tabs = de.estrichone.android.ui.Tab.entries.map { it.label }
        assertEquals(listOf("Heute", "Baustellen", "Chat", "Kunden", "Mehr"), tabs)
        val nav = de.estrichone.android.ui.Nav(de.estrichone.android.ui.Tab.Today)
        nav.open(de.estrichone.core.NotificationRoute.Chat("c1"))
        assertEquals(de.estrichone.android.ui.Tab.Chat, nav.tab)
        assertEquals(listOf(de.estrichone.android.ui.Route.Chat("c1")), nav.stack())
        // Foto aus „Heute“: Kamera über der Bauakte (Zurück führt in die Bauakte).
        nav.captureIn("b1", de.estrichone.android.ui.SiteTask.Photo)
        assertEquals(de.estrichone.android.ui.Tab.Bauakten, nav.tab)
        assertEquals(listOf(de.estrichone.android.ui.Route.Bauakte("b1"), de.estrichone.android.ui.Route.Camera("b1")), nav.stack())
        nav.captureIn("b1", de.estrichone.android.ui.SiteTask.Diary)
        assertEquals(listOf(de.estrichone.android.ui.Route.Bauakte("b1")), nav.stack())
        assertEquals(de.estrichone.android.ui.SiteTask.Diary.with("b1"), nav.captureTask)
    }

    @Test
    fun crewColorsAreParsed() {
        assertEquals(0xFFE07A1F.toInt(), parseColor("#E07A1F")!!.value.shr(32).toInt())
        assertNull(parseColor("rot"))
    }

    @Test
    fun keyDataStartsWithEstrichartDickeFlaeche() {
        val labels = listOf("Objektart", "Geschosse", "Gesamtfläche", "Priorität", "Estrichart", "Dämmdicke", "Nenndicke")
        val sorted = keyDataFirst(labels.map { it to "x" }).map { it.first }
        assertEquals(listOf("Estrichart", "Nenndicke", "Gesamtfläche", "Objektart", "Geschosse", "Priorität", "Dämmdicke"), sorted)
        // Einsatzkarte auf Heute: nur Estrichart, Dicke und Fläche.
        assertEquals(listOf("Estrichart", "Nenndicke", "Gesamtfläche"), sorted.filter { de.estrichone.android.ui.keyDataRank(it) <= 2 })
    }
}
