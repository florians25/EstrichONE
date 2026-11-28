package de.estrichone.core

import kotlinx.coroutines.runBlocking
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertNotNull
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertThrows
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.DisplayName
import org.junit.jupiter.api.Test

class LexwareTests {
    private val response = """
        {"bauakteId":"b1","configured":true,"canAssign":false,"syncError":null,"neuesFeld":1,
         "customer":{"id":"k1","name":"Huber","lexwareContactId":"l1","syncEnabled":true},
         "contact":{"lexwareContactId":"l1","name":"HUBER BAU GMBH","number":"10001","url":"https://app.lexware.de/permalink/contacts/view/l1",
           "street":"Postfach 7","postalCode":"83001","city":"Rosenheim","email":"rechnung@huber.example","differingFields":["name","email"],
           "sync":{"state":"deviations","syncedAt":"2026-11-26T10:00:00Z","error":null,
             "deviations":[{"field":"name","label":"Name","estrichOne":"Huber Bau GmbH","lexware":"HUBER BAU GMBH"},
                           {"field":"email","label":"E-Mail-Adresse","estrichOne":null,"lexware":"rechnung@huber.example"}]}},
         "summary":"1 Rechnung: offen 1.190,00 €",
         "vouchers":[{"id":"v1","lexwareId":"x1","type":"invoice","number":"RE1042","title":null,"voucherDate":"2026-10-01","dueDate":"2026-10-15",
           "netAmount":1000.0,"grossAmount":1190.0,"currency":"EUR","status":"overdue","contactName":"Huber","url":"https://app.lexware.de/x","syncedAt":null,"syncError":null},
           {"id":"v2","lexwareId":"x2","type":"quotation","number":null,"status":"draft","url":""}]}
    """.trimIndent()

    private fun modules(lexware: Boolean) = AppModules(mapOf(AppModules.LEXWARE to lexware))

    @Test
    @DisplayName("Lexware: Antwort der Bauakte mit Kontakt, Abgleichstand und Belegen wird gelesen")
    fun parses() {
        val data = EstrichOneApi.json.decodeFromString(BauakteLexware.serializer(), response)
        val contact = data.contact!!
        assertEquals("HUBER BAU GMBH", contact.name)
        assertEquals("Postfach 7, 83001 Rosenheim", Lexware.address(contact))
        assertTrue(Lexware.differs(contact, "email"))
        assertFalse(Lexware.differs(contact, "street", "postalCode", "city"))
        assertEquals("2 Abweichungen offen" to LexwareTone.Warning, Lexware.syncState(contact.sync))
        assertTrue(Lexware.syncedAt(contact.sync)!!.startsWith("Zuletzt abgeglichen: 26.11.2026"))
        assertEquals("1 Rechnung: offen 1.190,00 €", data.summary)
        assertEquals(2, data.vouchers.size)
    }

    @Test
    @DisplayName("Lexware: Belegzeile mit Art, Nummer, Datum, Betrag und Status; für Entwürfe kein PDF")
    fun vouchers() {
        val data = EstrichOneApi.json.decodeFromString(BauakteLexware.serializer(), response)
        val (invoice, draft) = data.vouchers
        assertEquals("Rechnung RE1042", Lexware.title(invoice))
        assertEquals("01.10.2026, 1.190,00 €", Lexware.detail(invoice))
        assertEquals("Überfällig" to LexwareTone.Danger, Lexware.status(invoice.status))
        assertEquals("Rechnung-RE1042.pdf", Lexware.fileName(invoice))
        assertTrue(Lexware.pdfAvailable(invoice))
        assertEquals("Angebot", Lexware.title(draft))
        assertEquals("wird automatisch vergeben", Lexware.number(draft))
        assertFalse(Lexware.pdfAvailable(draft))
        assertEquals("Für Entwürfe stellt Lexware Office kein PDF bereit.", Lexware.DRAFT_HINT)
        assertEquals("Bezahlt" to LexwareTone.Success, Lexware.status("paidoff"))
        assertEquals("Angenommen" to LexwareTone.Success, Lexware.status("accepted"))
        assertEquals("Gutschrift", Lexware.typeText("creditnote"))
        // Auftragsbestätigung und Lieferschein: festgeschrieben heißt „Erstellt“, nie „Offen“ mit Zahlungsbedeutung.
        assertEquals("Erstellt" to LexwareTone.Neutral, Lexware.status("open", "orderconfirmation"))
        assertEquals("Erstellt" to LexwareTone.Neutral, Lexware.status("open", "deliverynote"))
        assertEquals("Lieferschein", Lexware.typeText("deliverynote"))
        assertEquals("Storniert" to LexwareTone.Warning, Lexware.status("voided", "deliverynote"))
        assertEquals("Offen" to LexwareTone.Info, Lexware.status("open", "invoice"))
        assertEquals("Offen" to LexwareTone.Info, Lexware.status("open", "quotation"))
        assertEquals("Unbekannt" to LexwareTone.Neutral, Lexware.status("irgendwas", "invoice"))
        // Rechnungsplan (Verkauf): Anteil berechnet für den schlichten Balken.
        assertEquals(0.65f, SalesPlan(orderNet = 10000.0, invoicedNet = 6500.0).billedFraction)
        assertEquals(null, SalesPlan(orderNet = null).billedFraction)
        assertEquals(1f, SalesPlan(orderNet = 1000.0, invoicedNet = 1500.0).billedFraction)
    }

    @Test
    @DisplayName("Lexware: Block nur mit Modul und Leserecht, Prüfung beim Anlegen nur mit Modul")
    fun visibility() {
        assertTrue(Lexware.visible(modules(true), setOf("invoices.read")))
        assertFalse(Lexware.visible(modules(false), setOf("invoices.read")))
        assertFalse(Lexware.visible(modules(true), setOf("bauakten.read")))
        assertFalse(Lexware.visible(AppModules.ALL_ON, setOf("invoices.read")))
        assertTrue(Lexware.checkActive(modules(true), setOf("contacts.edit")))
        assertFalse(Lexware.checkActive(modules(false), setOf("contacts.edit")))
    }

    @Test
    @DisplayName("Lexware: Prüfung beim Anlegen eines Kunden, Warnung und Wege")
    fun check() {
        assertNull(Lexware.checkRequest("Hu", company = true))
        val request = Lexware.checkRequest(" Huber Bau ", company = true, postalCode = "83022")!!
        assertEquals(LexwareCheckRequest("Huber Bau", "company", postalCode = "83022"), request)
        assertEquals("person", Lexware.checkRequest("Anna Kraus", company = false)!!.kind)
        assertEquals("Achtung: Dieser Kunde ist bereits in Lexware Office vorhanden.", Lexware.EXISTS_WARNING)

        val free = LexwareContactCandidate(LexwareContactSummary("l1", "HUBER BAU GMBH", customerNumber = "10001", street = "Am Anger 7", postalCode = "83022",
            city = "Rosenheim"), 80, listOf("gleicher Name und PLZ"))
        assertEquals("Kundennummer 10001, Am Anger 7, 83022 Rosenheim, gleicher Name und PLZ", Lexware.candidateLine(free))
        val taken = free.copy(contact = free.contact.copy(linkedContactId = "k9", linkedContactName = "Huber"))
        assertTrue(Lexware.canImport(LexwareContactCheck(true, null, listOf(free), canImport = true), free))
        assertFalse(Lexware.canImport(LexwareContactCheck(true, null, listOf(taken), canImport = true), taken))
        assertFalse(Lexware.canImport(LexwareContactCheck(true, null, listOf(free), canImport = false), free))
    }

    @Test
    @DisplayName("Lexware im Demomodus: Block, Belege, PDF, Entwurf ohne PDF und Warnung beim Anlegen")
    fun demo() = runBlocking {
        val api = EstrichOneApi("https://demo.invalid", MemorySessionStore(), SoftwareDeviceKeys(),
            DemoServer { id -> if (id == DemoData.PDF) "%PDF-1.4 Demo".toByteArray() else null }.client())
        api.signIn("m.huber", "demo", "Test")
        assertTrue(api.modules().lexwareOn)
        assertTrue("invoices.read" in api.myPermissions())
        val linked = api.bauakteLexware(DemoData.EFH)
        assertNotNull(linked.contact)
        assertEquals(4, linked.vouchers.size)
        assertNull(api.bauakteLexware(DemoData.HALLE).contact)
        val paid = linked.vouchers.first { it.status == "paid" }
        assertEquals("%PDF", String(api.lexwareVoucherPdf(DemoData.EFH, paid.id)!!.copyOfRange(0, 4)))
        val draft = linked.vouchers.first { it.status == "draft" }
        val error = assertThrows(ApiException::class.java) { runBlocking { api.lexwareVoucherPdf(DemoData.EFH, draft.id) } }
        assertEquals(Lexware.DRAFT_HINT, error.message)
        assertEquals(1, api.checkLexwareContact(Lexware.checkRequest("Huber Wohnbau", company = true)!!).candidates.size)
        assertTrue(api.checkLexwareContact(Lexware.checkRequest("Malerbetrieb Kern", company = true)!!).candidates.isEmpty())
    }
}
