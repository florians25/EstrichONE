package de.estrichone.core

import kotlinx.serialization.Serializable
import java.time.Instant
import java.time.LocalDate
import java.util.Locale

/**
 * Lexware Office in den Apps (nur lesend): Daten des verknüpften Lexware-Kontakts und zugeordnete Belege in der Bauakte,
 * Beleg als PDF und die Prüfung beim Anlegen eines Kunden. Rechnungen und Angebote entstehen ausschließlich in Lexware
 * Office; die Apps ordnen nichts zu und bearbeiten nichts.
 */
@Serializable
data class LexwareDeviationField(val field: String, val label: String = "", val estrichOne: String? = null, val lexware: String? = null)

@Serializable
data class LexwareSyncState(val state: String = "", val syncedAt: String? = null, val deviations: List<LexwareDeviationField> = emptyList(),
    val error: String? = null)

@Serializable
data class LexwareBauakteContact(
    val lexwareContactId: String, val name: String? = null, val number: String? = null, val url: String = "", val street: String? = null,
    val postalCode: String? = null, val city: String? = null, val email: String? = null, val differingFields: List<String> = emptyList(),
    val sync: LexwareSyncState = LexwareSyncState(),
)

@Serializable
data class LexwareVoucher(
    val id: String, val lexwareId: String = "", val type: String = "invoice", val number: String? = null, val title: String? = null,
    val voucherDate: String? = null, val dueDate: String? = null, val netAmount: Double? = null, val grossAmount: Double? = null,
    val currency: String = "EUR", val status: String = "draft", val url: String = "",
)

/** Rechnungsplan der Bauakte (Modul „Verkauf“, nur lesend): `GET sales/bauakten/{id}/plan`. */
@Serializable
data class SalesPlan(
    val orderNet: Double? = null, val invoicedNet: Double = 0.0, val text: String = "", val progressPercent: Double? = null,
) {
    /** Anteil der Auftragssumme, der berechnet ist (0 bis 1), für einen schlichten Balken; ohne Auftragssumme nichts. */
    val billedFraction: Float? get() = orderNet?.takeIf { it > 0 }?.let { (invoicedNet / it).coerceIn(0.0, 1.0).toFloat() }
}

@Serializable
data class BauakteLexware(
    val bauakteId: String = "", val configured: Boolean = false, val vouchers: List<LexwareVoucher> = emptyList(), val syncError: String? = null,
    val contact: LexwareBauakteContact? = null, val summary: String? = null,
)

@Serializable
data class LexwareContactSummary(
    val id: String, val name: String, val isCompany: Boolean = true, val customerNumber: String? = null, val street: String? = null,
    val postalCode: String? = null, val city: String? = null, val email: String? = null, val phone: String? = null, val url: String = "",
    val linkedContactId: String? = null, val linkedContactName: String? = null,
)

@Serializable
data class LexwareContactCandidate(val contact: LexwareContactSummary, val score: Int = 0, val reasons: List<String> = emptyList(), val strong: Boolean = false)

@Serializable
data class LexwareContactCheck(val checked: Boolean = false, val message: String? = null, val candidates: List<LexwareContactCandidate> = emptyList(),
    val canImport: Boolean = false)

@Serializable
data class LexwareCheckRequest(val name: String?, val kind: String, val email: String? = null, val phone: String? = null, val street: String? = null,
    val postalCode: String? = null, val city: String? = null)

@Serializable
data class LexwareImportRequest(val createAnyway: Boolean = false)

@Serializable
data class LexwareImportResult(val imported: Boolean = false, val contactId: String? = null, val customerNumber: String? = null)

/** Farbton eines Status in der Oberfläche. */
enum class LexwareTone { Neutral, Info, Success, Warning, Danger }

/** Texte und Regeln der Lexware-Anzeige, gleich auf iOS und Android. */
object Lexware {
    const val EXISTS_WARNING = "Achtung: Dieser Kunde ist bereits in Lexware Office vorhanden."
    const val DRAFT_HINT = "Für Entwürfe stellt Lexware Office kein PDF bereit."
    const val OFFLINE_HINT = "Das PDF ist nur mit Verbindung zum Server verfügbar."

    /** Block und Belege nur mit eingeschaltetem Modul und dem Recht, Rechnungen und Angebote zu sehen. */
    fun visible(modules: AppModules, permissions: Set<String>): Boolean = modules.lexwareOn && "invoices.read" in permissions

    /** Die Prüfung beim Anlegen läuft mit eingeschaltetem Modul, unabhängig vom Schalter am Kunden. */
    fun checkActive(modules: AppModules, permissions: Set<String>): Boolean = modules.lexwareOn && "contacts.edit" in permissions

    fun typeText(type: String): String = when (type) {
        "invoice" -> "Rechnung"
        "quotation" -> "Angebot"
        "creditnote" -> "Gutschrift"
        "orderconfirmation" -> "Auftragsbestätigung"
        "deliverynote" -> "Lieferschein"
        else -> "Beleg"
    }

    /** Belege mit Zahlungsbedeutung: nur Rechnung und Gutschrift. */
    fun isPayable(type: String): Boolean = type == "invoice" || type == "creditnote"

    /** Status je Belegart: Auftragsbestätigung und Lieferschein sind festgeschrieben „Erstellt“, nie „Offen“. */
    fun status(status: String, type: String): Pair<String, LexwareTone> = when {
        (type == "orderconfirmation" || type == "deliverynote") && status == "open" -> "Erstellt" to LexwareTone.Neutral
        !isPayable(type) && type != "quotation" && status in setOf("overdue", "paid", "paidoff") -> "Erstellt" to LexwareTone.Neutral
        else -> status(status)
    }

    fun status(status: String): Pair<String, LexwareTone> = when (status) {
        "draft" -> "Entwurf" to LexwareTone.Neutral
        "open" -> "Offen" to LexwareTone.Info
        "overdue" -> "Überfällig" to LexwareTone.Danger
        "paid", "paidoff" -> "Bezahlt" to LexwareTone.Success
        "accepted" -> "Angenommen" to LexwareTone.Success
        "rejected" -> "Abgelehnt" to LexwareTone.Danger
        "voided" -> "Storniert" to LexwareTone.Warning
        else -> "Unbekannt" to LexwareTone.Neutral
    }

    fun pdfAvailable(voucher: LexwareVoucher): Boolean = voucher.status != "draft"

    fun number(voucher: LexwareVoucher): String = voucher.number ?: if (voucher.status == "draft") "wird automatisch vergeben" else "ohne Nummer"

    /** Titel einer Belegzeile, z. B. „Rechnung RE1042“. */
    fun title(voucher: LexwareVoucher): String = listOfNotNull(typeText(voucher.type), voucher.number).joinToString(" ")

    fun fileName(voucher: LexwareVoucher): String =
        "${title(voucher).replace(Regex("[^\\wäöüÄÖÜß-]+"), "-").trim('-').ifEmpty { "Beleg" }}.pdf"

    fun euro(value: Double?): String = if (value == null) "" else String.format(Locale.GERMANY, "%,.2f €", value)

    fun date(iso: String?): String = runCatching { Dates.date(LocalDate.parse(iso!!.take(10))) }.getOrDefault("")

    /** Zweite Zeile eines Belegs: Datum und Betrag. */
    fun detail(voucher: LexwareVoucher): String = listOf(date(voucher.voucherDate), euro(voucher.grossAmount)).filter { it.isNotEmpty() }.joinToString(", ")

    fun address(contact: LexwareBauakteContact): String =
        listOf(contact.street, listOfNotNull(contact.postalCode, contact.city).joinToString(" ")).filter { !it.isNullOrBlank() }.joinToString(", ")

    fun differs(contact: LexwareBauakteContact, vararg fields: String): Boolean = fields.any { it in contact.differingFields }

    /** Stand des Abgleichs als Text und Farbton. */
    fun syncState(sync: LexwareSyncState): Pair<String, LexwareTone> = when (sync.state) {
        "synced" -> "Abgeglichen" to LexwareTone.Success
        "deviations" -> (if (sync.deviations.size == 1) "1 Abweichung offen" else "${sync.deviations.size} Abweichungen offen") to LexwareTone.Warning
        "pending" -> "Abgleich steht aus" to LexwareTone.Info
        "error" -> "Abgleich fehlgeschlagen" to LexwareTone.Danger
        "missing" -> "Kontakt in Lexware Office nicht mehr vorhanden" to LexwareTone.Danger
        "archived" -> "Kontakt in Lexware Office archiviert" to LexwareTone.Warning
        "paused" -> "Abgleich ruht" to LexwareTone.Neutral
        "disabled" -> "Abgleich ausgeschaltet" to LexwareTone.Neutral
        "off" -> "Änderungsabgleich ausgeschaltet" to LexwareTone.Neutral
        else -> "" to LexwareTone.Neutral
    }

    fun syncedAt(sync: LexwareSyncState): String? =
        sync.syncedAt?.let { at -> runCatching { "Zuletzt abgeglichen: ${Dates.dateTime(Instant.parse(at))}" }.getOrNull() }

    /** Anfrage der Prüfung; ohne Name ab drei Zeichen wird nicht geprüft. */
    fun checkRequest(name: String, company: Boolean, street: String = "", postalCode: String = "", city: String = "", phone: String = "",
        email: String = ""): LexwareCheckRequest? {
        fun text(value: String) = value.trim().ifEmpty { null }
        val trimmed = text(name)
        if ((trimmed?.length ?: 0) < 3 && text(email) == null) return null
        return LexwareCheckRequest(trimmed, if (company) "company" else "person", text(email), text(phone), text(street), text(postalCode), text(city))
    }

    /** Zeile zu einem Treffer: Kundennummer, Anschrift und Grund der Übereinstimmung. */
    fun candidateLine(candidate: LexwareContactCandidate): String {
        val c = candidate.contact
        val place = listOf(c.street, listOfNotNull(c.postalCode, c.city).joinToString(" ")).filter { !it.isNullOrBlank() }.joinToString(", ")
        return listOf(c.customerNumber?.let { "Kundennummer $it" } ?: "ohne Kundennummer", place, candidate.reasons.joinToString(", "))
            .filter { it.isNotEmpty() }.joinToString(", ")
    }

    /** Übernehmen geht nur mit dem Recht dazu und wenn der Kontakt noch zu keinem Kunden gehört. */
    fun canImport(check: LexwareContactCheck, candidate: LexwareContactCandidate): Boolean = check.canImport && candidate.contact.linkedContactId == null
}

/** Antwort auf das Hochladen eines Eingangsbelegs (Modul „Eingangsbelege“), gekürzt auf das, was die App zeigt. */
@Serializable
data class PurchaseUploadDocument(val id: String, val fileName: String = "", val formatName: String = "", val problemCount: Int = 0)

@Serializable
data class PurchaseUploadResult(val document: PurchaseUploadDocument, val duplicate: Boolean? = null)

object PurchaseUpload {
    /** Höchstgröße je Beleg (wie Lexware Office). */
    const val MAX_BYTES = 5 * 1024 * 1024

    /** Prüfung vor dem Hochladen; liefert die Meldung oder null. */
    fun problem(byteCount: Int): String? = when {
        byteCount == 0 -> "Die Datei ist leer."
        byteCount > MAX_BYTES -> "Der Beleg ist größer als 5 MB. Bitte als kleineres Foto oder PDF hochladen."
        else -> null
    }

    /** Meldung nach dem Hochladen. */
    fun text(result: PurchaseUploadResult): String {
        if (result.duplicate == true) return "Diesen Beleg gibt es schon. Er wurde nicht ein zweites Mal hochgeladen."
        val base = "Beleg hochgeladen (${result.document.formatName}). Das Büro gibt ihn an Lexware Office weiter."
        return if (result.document.problemCount > 0) "$base Bei der E-Rechnung fehlen Angaben; das Büro prüft sie." else base
    }
}

/**
 * Eingangsbeleg hochladen (Foto, PDF oder XRechnung) und der Bauakte zuordnen; der Server liest E-Rechnungen und erkennt
 * doppelte Dateien. Weitergegeben an Lexware Office wird im Büro.
 */
suspend fun EstrichOneApi.uploadPurchase(bytes: ByteArray, fileName: String, bauakteId: String?): PurchaseUploadResult {
    val query = buildMap {
        put("fileName", fileName)
        put("source", "app")
        if (bauakteId != null) put("bauakteId", bauakteId)
    }
    val text = upload("POST", "purchases/documents", bytes, "application/octet-stream", query = query)
    return EstrichOneApi.json.decodeFromString(PurchaseUploadResult.serializer(), text)
}

suspend fun EstrichOneApi.bauakteLexware(bauakteId: String): BauakteLexware = send("GET", "bauakten/$bauakteId/lexware", null)

/** Rechnungsplan der Bauakte (Modul „Verkauf“); ohne Modul oder Recht ein Fehler. */
suspend fun EstrichOneApi.salesPlan(bauakteId: String): SalesPlan = send("GET", "sales/bauakten/$bauakteId/plan", null)

/** Beleg-PDF, vom Server aus Lexware Office durchgereicht; für Entwürfe meldet der Server, dass es kein PDF gibt. */
suspend fun EstrichOneApi.lexwareVoucherPdf(bauakteId: String, linkId: String): ByteArray? = bytes("bauakten/$bauakteId/lexware/vouchers/$linkId/document")

suspend fun EstrichOneApi.checkLexwareContact(request: LexwareCheckRequest): LexwareContactCheck = send("POST", "lexware/contacts/check", request)

/** Kontakt aus Lexware Office als Kunde übernehmen: Der Server legt ihn mit den Lexware-Daten an und verknüpft ihn. */
suspend fun EstrichOneApi.importLexwareContact(lexwareContactId: String): LexwareImportResult =
    send("POST", "lexware/contacts/$lexwareContactId/import", LexwareImportRequest())
