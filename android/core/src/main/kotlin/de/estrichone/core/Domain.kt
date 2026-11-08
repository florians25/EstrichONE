package de.estrichone.core

import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import java.time.Instant
import java.time.LocalDate

// Ansichten auf die lokalen Datensätze (Format des Änderungsfeeds), wie EstrichCore/Domain auf iOS.

/** Art der Bauakte bei der Schnellanlage (`templateKey`). */
enum class BauakteTemplate(val key: String, val title: String) {
    Standard("standard", "Standard"), NewBuild("neubau", "Neubau"), Renovation("sanierung", "Sanierung"), Commercial("gewerbe", "Gewerbe")
}

fun bauakteStatus(raw: String?): BauakteStatus = BauakteStatus.entries.firstOrNull { it.raw == raw } ?: BauakteStatus.Planned

val BauakteStatus.raw: String
    get() = when (this) {
        BauakteStatus.Inquiry -> "inquiry"
        BauakteStatus.Planned -> "planned"
        BauakteStatus.InProgress -> "inProgress"
        BauakteStatus.Drying -> "drying"
        BauakteStatus.Completed -> "completed"
        BauakteStatus.Cancelled -> "cancelled"
    }

/** Kurzform für Listen („Trocknung“ statt „Belegreif / Trocknung“). */
val BauakteStatus.shortLabel: String get() = if (this == BauakteStatus.Drying) "Trocknung" else label

/** Laufende Baustellen zuerst. */
val BauakteStatus.order: Int
    get() = when (this) {
        BauakteStatus.InProgress -> 0
        BauakteStatus.Drying -> 1
        BauakteStatus.Planned -> 2
        BauakteStatus.Inquiry -> 3
        BauakteStatus.Completed -> 4
        BauakteStatus.Cancelled -> 5
    }

private fun addressLine(street: String?, postalCode: String?, city: String?): String? {
    val place = listOfNotNull(postalCode?.takeIf { it.isNotBlank() }, city?.takeIf { it.isNotBlank() }).joinToString(" ")
    return listOfNotNull(street?.takeIf { it.isNotBlank() }, place.takeIf { it.isNotBlank() }).joinToString(", ").ifEmpty { null }
}

data class Bauakte(val entity: LocalEntity) {
    private val d = entity.data
    val id: String get() = entity.id
    val number: String? = d.str("number")
    val title: String = d.str("title") ?: ""
    val status: BauakteStatus = bauakteStatus(d.str("status"))
    val street: String? = d.str("street")
    val postalCode: String? = d.str("postalCode")
    val city: String? = d.str("city")
    val latitude: Double? = d.num("latitude")
    val longitude: Double? = d.num("longitude")
    val description: String? = d.str("description")
    val customerId: String? = d.str("customerId")?.lowercase()
    val templateKey: String? = d.str("templateKey")
    val siteContactName: String? = d.str("siteContactName")
    val siteContactPhone: String? = d.str("siteContactPhone")
    val cancellationReason: String? = d.str("cancellationReason")
    val project: JsonObject = d.obj("project") ?: JsonObject(emptyMap())
    val plannedStart: LocalDate? = ApiDate.day(d.str("plannedStart"))
    val plannedEnd: LocalDate? = ApiDate.day(d.str("plannedEnd"))
    val updatedAt: Instant? = ApiDate.parse(d.str("updatedAt"))
    val syncState: SyncState get() = entity.syncState
    val addressLine: String? get() = addressLine(street, postalCode, city)
    val hasLocation: Boolean get() = latitude != null && longitude != null

    /** „2026-041“ bzw. „Neu“, solange der Server noch keine Nummer vergeben hat. */
    val numberText: String get() = number ?: "Neu"

    fun matches(search: String): Boolean {
        val q = search.trim().lowercase()
        if (q.isEmpty()) return true
        return listOfNotNull(number, title, street, postalCode, city, description).any { it.lowercase().contains(q) }
    }
}

enum class ContactPhoneKind(val raw: String, val label: String) {
    Mobile("mobile", "Mobil"), Landline("landline", "Telefon"), Office("office", "Büro"), Fax("fax", "Fax"), Other("other", "Sonstige")
}

data class ContactPhone(val kind: ContactPhoneKind, val number: String, val label: String?)
data class ContactEmail(val address: String, val label: String?)

data class Contact(val entity: LocalEntity) {
    private val d = entity.data
    val id: String get() = entity.id
    val name: String = d.str("name") ?: ""
    val isCompany: Boolean = d.str("kind") == "company"
    val customerNumber: String? = d.str("customerNumber")
    val street: String? = d.str("street")
    val postalCode: String? = d.str("postalCode")
    val city: String? = d.str("city")
    val phones: List<ContactPhone> = (d.arr("phones")?.mapNotNull { it as? JsonObject }?.mapNotNull { p ->
        val number = p.str("number") ?: return@mapNotNull null
        ContactPhone(ContactPhoneKind.entries.firstOrNull { it.raw == p.str("kind") } ?: ContactPhoneKind.Other, number, p.str("label"))
    } ?: emptyList()).ifEmpty { d.str("phone")?.let { listOf(ContactPhone(ContactPhoneKind.Landline, it, null)) } ?: emptyList() }
    val emails: List<ContactEmail> = (d.arr("emails")?.mapNotNull { it as? JsonObject }?.mapNotNull { e ->
        e.str("address")?.let { ContactEmail(it, e.str("label")) }
    } ?: emptyList()).ifEmpty { d.str("email")?.let { listOf(ContactEmail(it, null)) } ?: emptyList() }
    val phone: String? get() = phones.firstOrNull()?.number
    val email: String? get() = emails.firstOrNull()?.address
    val notes: String? = d.str("notes")
    val salutation: String? = d.str("salutation")
    val website: String? = d.str("website")
    val preferredChannel: String? = d.str("preferredChannel")
    val syncState: SyncState get() = entity.syncState
    val addressLine: String? get() = addressLine(street, postalCode, city)
    val subtitle: String get() = listOfNotNull(customerNumber?.takeIf { it.isNotBlank() }, city?.takeIf { it.isNotBlank() }).joinToString(" · ")
    val initials: String get() = initials(name)

    fun matches(search: String): Boolean {
        val q = search.trim().lowercase()
        if (q.isEmpty()) return true
        return (listOfNotNull(name, customerNumber, street, postalCode, city) + phones.map { it.number } + emails.map { it.address })
            .any { it.lowercase().contains(q) }
    }
}

/** „Max Huber“ → „MH“. */
fun initials(name: String): String =
    name.split(' ', '-').filter { it.isNotBlank() && it.first().isLetter() }.take(2).joinToString("") { it.first().uppercase() }.ifEmpty { "?" }

/** Dokumentarten wie im Büro (Server: `DocumentCategories`). */
enum class DocumentCategory(val raw: String, val title: String) {
    Delivery("delivery", "Lieferschein"), Acceptance("acceptance", "Abnahme"), Plan("plan", "Plan"), Order("order", "Auftrag"),
    Offer("offer", "Angebot"), Invoice("invoice", "Rechnung"), Customer("customer", "Kundenunterlagen"), Office("office", "Büro"),
    Other("other", "Sonstiges"),
}

data class FileItem(val entity: LocalEntity) {
    private val d = entity.data
    val id: String get() = entity.id
    val bauakteId: String? = d.str("bauakteId")?.lowercase()
    val kind: String = d.str("kind") ?: "photo"
    val originalName: String = d.str("originalName") ?: ""
    val contentType: String = d.str("contentType") ?: "application/octet-stream"
    val sizeBytes: Long = d.long("sizeBytes") ?: 0
    val caption: String? = d.str("caption")
    val category: DocumentCategory? = DocumentCategory.entries.firstOrNull { it.raw == d.str("category") }
    val capturedAt: Instant? = ApiDate.parse(d.str("capturedAt"))
    val createdAt: Instant? = ApiDate.parse(d.str("createdAt"))
    val isStoredOnServer: Boolean = d.str("state") == "stored"
    val syncState: SyncState get() = entity.syncState
    val isPhoto: Boolean get() = kind == "photo"
    val isVideo: Boolean get() = kind == "video"
    val isDocument: Boolean get() = kind == "document"
    val isPdf: Boolean get() = contentType == "application/pdf" || originalName.lowercase().endsWith(".pdf")
    val displayName: String get() = caption?.takeIf { it.isNotBlank() } ?: originalName.substringBeforeLast('.').ifEmpty { "Datei" }
    val time: Instant? get() = capturedAt ?: createdAt
    /** Dateiname zum Teilen und Öffnen: Videos immer mit Videoendung, damit WhatsApp & Co. sie als Video annehmen. */
    val shareName: String get() = ShareFile.name(displayName, originalName, contentType, isVideo, isPhoto)
    /** Inhaltstyp zum Teilen (Videos ohne erkennbaren Typ: video/mp4). */
    val shareMime: String get() = ShareFile.mime(contentType, isVideo, isPhoto)
}

/** Name und Typ geteilter Dateien (Teilen-Menü von Android, FileProvider). */
object ShareFile {
    private val videoExtensions = setOf("mp4", "mov", "m4v")

    fun mime(contentType: String, isVideo: Boolean, isPhoto: Boolean): String {
        val type = contentType.substringBefore(';').trim().lowercase()
        return when {
            isVideo && !type.startsWith("video/") -> "video/mp4"
            isPhoto && !type.startsWith("image/") -> "image/jpeg"
            type.isEmpty() -> "application/octet-stream"
            else -> type
        }
    }

    fun name(displayName: String, originalName: String, contentType: String, isVideo: Boolean, isPhoto: Boolean): String {
        val base = displayName.map { if (it in "/\\:*?\"<>|") '_' else it }.joinToString("").trim().take(80).ifEmpty { "Datei" }
        val original = originalName.substringAfterLast('.', "").lowercase()
        val ext = when {
            isVideo -> original.takeIf { it in videoExtensions } ?: if (mime(contentType, true, false) == "video/quicktime") "mov" else "mp4"
            isPhoto -> original.ifEmpty { if (contentType.contains("png")) "png" else if (contentType.contains("heic")) "heic" else "jpg" }
            else -> original.ifEmpty { if (contentType.contains("pdf")) "pdf" else "bin" }
        }
        return "$base.$ext"
    }
}

data class SiteNote(val entity: LocalEntity) {
    private val d = entity.data
    val id: String get() = entity.id
    val bauakteId: String? = d.str("bauakteId")?.lowercase()
    val text: String = d.str("text") ?: ""
    val isPinned: Boolean = d.bool("isPinned") ?: d.bool("pinned") ?: false
    val authorUserId: String? = d.str("authorUserId")?.lowercase()
    val createdAt: Instant? = ApiDate.parse(d.str("createdAt"))
    val syncState: SyncState get() = entity.syncState
}

data class Task(val entity: LocalEntity) {
    private val d = entity.data
    val id: String get() = entity.id
    val bauakteId: String? = d.str("bauakteId")?.lowercase()
    val kind: WorkItemKind = if (d.str("kind") == "defect") WorkItemKind.Defect else WorkItemKind.Task
    val status: WorkItemStatus = when (d.str("status")) {
        "done" -> WorkItemStatus.Done
        "inProgress" -> WorkItemStatus.InProgress
        else -> WorkItemStatus.Open
    }
    val title: String = d.str("title") ?: ""
    val description: String? = d.str("description")
    val dueDate: LocalDate? = ApiDate.day(d.str("dueDate"))
    val assigneeUserId: String? = d.str("assigneeUserId")?.lowercase()
    val syncState: SyncState get() = entity.syncState
    val isOpen: Boolean get() = status != WorkItemStatus.Done
}

val WorkItemKind.label: String get() = if (this == WorkItemKind.Defect) "Mangel" else "Aufgabe"
val WorkItemKind.raw: String get() = if (this == WorkItemKind.Defect) "defect" else "task"
val WorkItemStatus.label: String
    get() = when (this) {
        WorkItemStatus.Open -> "Offen"
        WorkItemStatus.InProgress -> "In Arbeit"
        WorkItemStatus.Done -> "Erledigt"
    }
val WorkItemStatus.raw: String
    get() = when (this) {
        WorkItemStatus.Open -> "open"
        WorkItemStatus.InProgress -> "inProgress"
        WorkItemStatus.Done -> "done"
    }

/** „heute fällig“, „morgen fällig“, „seit 2 Tagen fällig“, „fällig 12.11.“. */
fun dueText(due: LocalDate, today: LocalDate): String {
    val days = java.time.temporal.ChronoUnit.DAYS.between(today, due)
    return when {
        days == 0L -> "heute fällig"
        days == 1L -> "morgen fällig"
        days < 0 -> if (days == -1L) "seit gestern fällig" else "seit ${-days} Tagen fällig"
        days < 7 -> "fällig ${Dates.weekdayShort(due)}"
        else -> "fällig ${Dates.dayMonth(due)}"
    }
}

enum class DiaryWeather(val raw: String, val label: String) {
    Sunny("sunny", "Sonnig"), Cloudy("cloudy", "Bewölkt"), Rain("rain", "Regen"), Snow("snow", "Schnee"), Frost("frost", "Frost"), Wind("wind", "Wind")
}

data class DiaryEntry(val entity: LocalEntity) {
    private val d = entity.data
    val id: String get() = entity.id
    val bauakteId: String? = d.str("bauakteId")?.lowercase()
    val day: LocalDate? = ApiDate.day(d.str("day"))
    val text: String = d.str("text") ?: ""
    val weather: DiaryWeather? = DiaryWeather.entries.firstOrNull { it.raw == d.str("weather") || (it == DiaryWeather.Rain && d.str("weather") == "rainy") }
    val temperature: Int? = d.long("temperatureCelsius")?.toInt()
    val workers: Int? = d.long("workersPresent")?.toInt()
    val startTime: String? = d.str("startTime")
    val endTime: String? = d.str("endTime")
    val crew: String? = d.str("crew")
    val photoFileIds: List<String> = d.strings("photoFileIds").map { it.lowercase() }
    val syncState: SyncState get() = entity.syncState
}

enum class CheckResult(val raw: String, val label: String) { Ok("ok", "In Ordnung"), NotOk("notOk", "Mangel"), NotApplicable("notApplicable", "Entfällt") }

enum class ScreedBinder(val raw: String, val label: String) { Cement("cement", "Zementestrich (CT)"), CalciumSulfate("calciumSulfate", "Calciumsulfatestrich (CA)") }

/** Grenzwerte der Belegreife (CM-%), wie der Server sie bestimmt. */
fun cmLimit(binder: ScreedBinder, heated: Boolean): Double = when (binder) {
    ScreedBinder.Cement -> if (heated) 1.8 else 2.0
    ScreedBinder.CalciumSulfate -> if (heated) 0.3 else 0.5
}

data class Inspection(val entity: LocalEntity) {
    private val d = entity.data
    val id: String get() = entity.id
    val bauakteId: String? = d.str("bauakteId")?.lowercase()
    val isCmMeasurement: Boolean = d.str("kind") == "cmMeasurement"
    val performedAt: Instant? = ApiDate.parse(d.str("performedAt"))
    val notRequired: Boolean = d.bool("notRequired") ?: false
    val checks: Map<String, CheckResult> = d.obj("checks")?.mapNotNull { (k, v) ->
        CheckResult.entries.firstOrNull { it.raw == (v as? JsonPrimitive)?.content }?.let { k to it }
    }?.toMap() ?: emptyMap()
    val checkRemarks: Map<String, String> = d.obj("checkRemarks")?.mapNotNull { (k, v) -> (v as? JsonPrimitive)?.content?.let { k to it } }?.toMap() ?: emptyMap()
    val values: JsonObject = d.obj("values") ?: JsonObject(emptyMap())
    val remarks: String? = d.str("remarks")
    val notes: String? = d.str("notes")
    val location: String? = d.str("location")
    val binder: ScreedBinder? = ScreedBinder.entries.firstOrNull { it.raw == d.str("binder") }
    val heated: Boolean = d.bool("heated") ?: false
    val thicknessMm: Int? = d.long("thicknessMm")?.toInt()
    val measured: Double? = d.num("measuredCmPercent")
    val limit: Double? = d.num("limitCmPercent")
    val isReady: Boolean? = d.bool("isReadyForCovering")
    val photoFileIds: List<String> = d.strings("photoFileIds").map { it.lowercase() }
    val syncState: SyncState get() = entity.syncState
    val title: String get() = if (isCmMeasurement) "CM-Messung" else "Erstbegehung"
    val defects: Int get() = checks.values.count { it == CheckResult.NotOk }
}

enum class AcceptanceResult(val raw: String, val label: String) {
    Accepted("accepted", "Abgenommen"), WithReservations("acceptedWithReservations", "Mit Vorbehalt"), Refused("refused", "Verweigert")
}

data class Acceptance(val entity: LocalEntity) {
    private val d = entity.data
    val id: String get() = entity.id
    val bauakteId: String? = d.str("bauakteId")?.lowercase()
    val performedAt: Instant? = ApiDate.parse(d.str("performedAt"))
    val result: AcceptanceResult? = AcceptanceResult.entries.firstOrNull { it.raw == d.str("result") }
    val state: String? = d.str("state")
    val clientName: String? = d.str("clientName")
    val contractorName: String? = d.str("contractorName")
    val location: String? = d.str("location")
    val scope: String? = d.str("scope")
    val notes: String? = d.str("notes")
    val hasClientSignature: Boolean = d.bool("hasClientSignature") ?: (d.obj("clientSignature") != null)
    val pdfFileId: String? = d.str("pdfFileId")?.lowercase()
    val syncState: SyncState get() = entity.syncState
    val stateLabel: String
        get() = when (state) {
            "awaitingCustomer" -> "Freigabe durch Kunden ausstehend"
            "completed" -> "Abgeschlossen"
            else -> "Erfasst"
        }
}

enum class RoomStatus(val raw: String, val label: String) { Planned("planned", "Geplant"), Laid("laid", "Eingebracht"), Ready("ready", "Belegreif") }

data class Room(val entity: LocalEntity) {
    private val d = entity.data
    val id: String get() = entity.id
    val bauakteId: String? = d.str("bauakteId")?.lowercase()
    val name: String = d.str("name") ?: ""
    val floor: String? = d.str("floor")
    val area: Double? = d.num("areaSquareMeters")
    val thickness: Int? = d.long("thicknessMillimeters")?.toInt()
    val insulation: Int? = d.long("insulationMillimeters")?.toInt()
    val underfloorHeating: Boolean = d.bool("underfloorHeating") ?: false
    val status: RoomStatus = RoomStatus.entries.firstOrNull { it.raw == d.str("status") } ?: RoomStatus.Planned
    val notes: String? = d.str("notes")
    val syncState: SyncState get() = entity.syncState
}

/** Reihenfolge der Geschosse: Keller, EG, 1. OG … Dach. */
fun floorRank(floor: String?): Int {
    val f = floor?.uppercase()?.trim() ?: return 99
    return when {
        f.startsWith("KG") || f.startsWith("UG") || f.contains("KELLER") -> 0
        f.startsWith("EG") -> 1
        f == "OG" -> 2
        f.contains("OG") -> 1 + (f.filter { it.isDigit() }.toIntOrNull() ?: 1)
        f.startsWith("DG") || f.contains("DACH") || f.contains("SPITZBODEN") -> 50
        else -> 60
    }
}

enum class PartyRole(val raw: String, val label: String) {
    Client("client", "Bauherr/Kunde"), Architect("architect", "Architekt"), Planner("planner", "Planer"), SiteManager("siteManager", "Bauleiter"),
    GeneralContractor("generalContractor", "Generalunternehmer"), Subcontractor("subcontractor", "Nachunternehmer"), Supplier("supplier", "Lieferant"),
    SiteContact("siteContact", "Ansprechpartner vor Ort"), Other("other", "Beteiligter"),
}

data class Party(val entity: LocalEntity) {
    private val d = entity.data
    val id: String get() = entity.id
    val bauakteId: String? = d.str("bauakteId")?.lowercase()
    val role: PartyRole = PartyRole.entries.firstOrNull { it.raw == d.str("role") } ?: PartyRole.Other
    val name: String = d.str("name") ?: ""
    val company: String? = d.str("company")
    val phone: String? = d.str("phone")
    val mobile: String? = d.str("mobile")
    val email: String? = d.str("email")
    val note: String? = d.str("note")
    val isPrimary: Boolean = d.bool("isPrimary") ?: false
    val addressLine: String? = addressLine(d.str("street"), d.str("postalCode"), d.str("city"))
    val bestPhone: String? get() = mobile ?: phone
}

/** Formular-Schema (Basisdaten der Bauakte, Erstbegehung) aus dem Feed. */
data class FormField(
    val key: String, val label: String, val type: String, val list: String?, val unit: String?, val help: String?, val badge: Boolean,
    val path: String, val target: String?, val photos: Boolean,
)

data class FormGroup(val key: String, val label: String, val fields: List<FormField>)

data class FormSchema(val key: String, val version: Long, val groups: List<FormGroup>) {
    companion object {
        fun from(entity: LocalEntity): FormSchema {
            val d = entity.data
            val groups = d.arr("groups")?.mapNotNull { it as? JsonObject }?.map { g ->
                FormGroup(
                    g.str("key") ?: "", g.str("label") ?: "",
                    g.arr("fields")?.mapNotNull { it as? JsonObject }?.map { f ->
                        val key = f.str("key") ?: ""
                        FormField(
                            key, f.str("label") ?: key, f.str("type") ?: "text", f.str("list"), f.str("unit"), f.str("help"), f.bool("badge") ?: false,
                            f.str("path") ?: "project.custom.$key", f.str("target"), f.bool("photos") ?: false,
                        )
                    } ?: emptyList(),
                )
            } ?: emptyList()
            return FormSchema(d.str("key") ?: "", d.long("version") ?: 1, groups)
        }
    }
}

data class ValueListItem(val list: String, val key: String, val label: String, val shortLabel: String?, val sortOrder: Int, val isActive: Boolean)

/** Wertelisten (Objektart, Estrichart …) für Auswahlfelder und Kurzbezeichnungen. */
class ValueLists(items: List<ValueListItem>) {
    private val byList = items.filter { it.isActive }.groupBy { it.list }.mapValues { (_, v) -> v.sortedBy { it.sortOrder } }

    fun items(list: String): List<ValueListItem> = byList[list] ?: emptyList()

    fun label(list: String?, key: String?): String? = key?.let { k -> list?.let { byList[it]?.firstOrNull { item -> item.key == k }?.label } ?: k }

    fun short(list: String?, key: String?): String? = key?.let { k -> list?.let { byList[it]?.firstOrNull { item -> item.key == k } }?.let { it.shortLabel ?: it.label } ?: k }
}

/** Wert eines Felds aus dem Schema darstellen („142,5 m²“, „Ja“, Listenwert). */
fun FormField.display(value: JsonElement?, lists: ValueLists): String? {
    val text = when (type) {
        "select" -> (value as? JsonPrimitive)?.content?.let { lists.label(list, it) }
        "boolean", "check" -> (value as? JsonPrimitive)?.let { p ->
            when (p.content) {
                "true" -> "Ja"
                "false" -> "Nein"
                else -> CheckResult.entries.firstOrNull { it.raw == p.content }?.label ?: p.content
            }
        }
        else -> value.display()
    } ?: return null
    if (text.isBlank()) return null
    return if (unit != null && type in setOf("number", "integer")) "$text $unit" else text
}

/** Kurzangabe für die Kopfzeile der Bauakte („EFH“, „142,5 m²“, „Fußbodenheizung“). */
fun FormField.badge(value: JsonElement?, lists: ValueLists): String? = when (type) {
    "select" -> (value as? JsonPrimitive)?.content?.let { lists.short(list, it) }
    "boolean" -> if ((value as? JsonPrimitive)?.content == "true") label else null
    else -> display(value, lists)
}
