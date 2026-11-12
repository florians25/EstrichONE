package de.estrichone.core

import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import java.security.MessageDigest
import java.time.Instant
import java.time.LocalDate

/** Neuer Kunde bei der Schnellanlage (`contact.create` vor `bauakte.create`). */
data class NewCustomer(
    val name: String, val isCompany: Boolean = false, val street: String? = null, val postalCode: String? = null, val city: String? = null,
    val phone: String? = null, val email: String? = null,
)

/**
 * Lesen und Ändern der Baustellendaten auf dem Gerät (offline, wie BauakteService in EstrichCore). Jede Änderung landet
 * mit ihrer Operation in der Outbox; die Synchronisation überträgt sie.
 */
class BauakteService(val store: LocalStore, private val files: FileVault, private val userId: () -> String?) {

    // Lesen

    fun bauakten(): List<Bauakte> = store.entities(EntityTypes.BAUAKTE).map(::Bauakte)
        .sortedWith(compareBy<Bauakte>({ it.status.order }, { it.plannedStart ?: LocalDate.MAX }, { it.title.lowercase() }))

    fun bauakte(id: String): Bauakte? = store.entity(EntityTypes.BAUAKTE, id)?.let(::Bauakte)

    /** Alle Kunden auf dem Gerät (auch archivierte, z. B. für den Kundennamen an einer Bauakte). */
    fun contacts(): List<Contact> = store.entities(EntityTypes.CONTACT).map(::Contact).sortedBy { it.name.lowercase() }

    /** Aktive Kunden für Auswahllisten (Bauaktenanlage, Kundenliste ohne „Archivierte anzeigen“). */
    fun activeContacts(): List<Contact> = contacts().filterNot { it.isArchived }

    fun contact(id: String?): Contact? = id?.let { store.entity(EntityTypes.CONTACT, it) }?.let(::Contact)

    fun bauaktenOfCustomer(contactId: String): List<Bauakte> = bauakten().filter { it.customerId == contactId.lowercase() }

    fun notes(bauakteId: String): List<SiteNote> = store.children(EntityTypes.NOTE, bauakteId).map(::SiteNote)
        .sortedWith(compareByDescending<SiteNote> { it.isPinned }.thenByDescending { it.createdAt ?: Instant.MAX })

    fun files(bauakteId: String): List<FileItem> = store.children(EntityTypes.FILE, bauakteId).map(::FileItem)
        .sortedByDescending { it.time ?: Instant.MAX }

    fun photos(bauakteId: String): List<FileItem> = files(bauakteId).filter { it.isPhoto || it.isVideo }

    fun documents(bauakteId: String): List<FileItem> = files(bauakteId).filter { it.isDocument }

    fun file(id: String): FileItem? = store.entity(EntityTypes.FILE, id)?.let(::FileItem)

    fun tasks(bauakteId: String): List<Task> = store.children(EntityTypes.WORK_ITEM, bauakteId).map(::Task).sortedForDisplay()

    /** Offene Aufgaben und Mängel, die mir zugewiesen sind, über alle Bauakten. */
    fun myTasks(): List<Task> {
        val me = userId()?.lowercase()
        return store.entities(EntityTypes.WORK_ITEM).map(::Task).filter { it.assigneeUserId == null || it.assigneeUserId == me }.sortedForDisplay()
    }

    private fun List<Task>.sortedForDisplay() = sortedWith(
        compareBy<Task>({ !it.isOpen }, { it.dueDate ?: LocalDate.MAX }, { if (it.kind == WorkItemKind.Defect) 0 else 1 }, { it.title.lowercase() }),
    )

    fun diary(bauakteId: String): List<DiaryEntry> = store.children(EntityTypes.DIARY, bauakteId).map(::DiaryEntry)
        .sortedByDescending { it.day ?: LocalDate.MIN }

    fun inspections(bauakteId: String): List<Inspection> = store.children(EntityTypes.INSPECTION, bauakteId).map(::Inspection)
        .sortedByDescending { it.performedAt ?: Instant.MIN }

    fun acceptances(bauakteId: String): List<Acceptance> = store.children(EntityTypes.ACCEPTANCE, bauakteId).map(::Acceptance)
        .sortedByDescending { it.performedAt ?: Instant.MIN }

    fun rooms(bauakteId: String): List<Room> = store.children(EntityTypes.ROOM, bauakteId).map(::Room)
        .sortedWith(compareBy<Room>({ floorRank(it.floor) }, { it.floor ?: "" }, { it.name.lowercase() }))

    fun parties(bauakteId: String): List<Party> = store.children(EntityTypes.PARTY, bauakteId).map(::Party)
        .sortedWith(compareByDescending<Party> { it.isPrimary }.thenBy { it.role.ordinal })

    fun schema(key: String): FormSchema? = store.entities(EntityTypes.FORM_SCHEMA).firstOrNull { it.data.str("key") == key }?.let(FormSchema::from)

    fun valueLists(): ValueLists = ValueLists(store.entities(EntityTypes.VALUE_LIST).map {
        val d = it.data
        ValueListItem(d.str("list") ?: "", d.str("key") ?: "", d.str("label") ?: "", d.str("shortLabel"), d.long("sortOrder")?.toInt() ?: 0, d.bool("isActive") ?: true)
    })

    /** Kurzangaben für den Kopf der Bauakte aus den Feldern mit `badge`. */
    fun badges(bauakte: Bauakte): List<String> {
        val schema = schema("bauakte") ?: return emptyList()
        val lists = valueLists()
        return schema.groups.flatMap { it.fields }.filter { it.badge }.mapNotNull { field ->
            field.badge(bauakte.project[field.path.removePrefix("project.")], lists)
        }
    }

    fun localContent(fileId: String): ByteArray? = files.read(fileId)

    // Ändern

    fun createBauakte(
        title: String, street: String?, postalCode: String?, city: String?, template: BauakteTemplate = BauakteTemplate.Standard,
        customerId: String? = null, newCustomer: NewCustomer? = null, description: String? = null,
    ): String {
        require(title.isNotBlank()) { "Bitte das Bauvorhaben angeben." }
        val customer = newCustomer?.let { createContact(it) } ?: customerId?.lowercase()
        val id = Ids.v7()
        val payload = json(
            "title" to title.trim(), "street" to street.clean(), "postalCode" to postalCode.clean(), "city" to city.clean(),
            "description" to description.clean(), "customerId" to customer, "templateKey" to template.key,
        )
        store.create(
            EntityTypes.BAUAKTE, id,
            payload.merged(mapOf("status" to toJson("planned"), "responsibleUserId" to toJson(userId()), "updatedAt" to toJson(ApiDate.format(Instant.now())))),
            payload,
        )
        return id
    }

    fun updateBauakte(id: String, changes: Map<String, Any?>) {
        store.update(EntityTypes.BAUAKTE, id, changes.mapValues { toJson(if (it.value is String) (it.value as String).clean() else it.value) })
    }

    /** „Erstbegehung nicht erforderlich“ vermerken (Begründung optional, höchstens 500 Zeichen); offline möglich. */
    fun waiveInitialInspection(id: String, reason: String? = null) {
        val text = reason.clean()
        require((text?.length ?: 0) <= 500) { "Die Begründung ist zu lang (höchstens 500 Zeichen)." }
        store.update(EntityTypes.BAUAKTE, id, mapOf("initialInspectionWaived" to toJson(true), "initialInspectionWaivedReason" to toJson(text)))
    }

    /** Vermerk aufheben: Die Erstbegehung gilt wieder als offen. */
    fun resumeInitialInspection(id: String) {
        store.update(EntityTypes.BAUAKTE, id, mapOf("initialInspectionWaived" to toJson(false), "initialInspectionWaivedReason" to JsonNull))
    }

    /** Status ändern (`bauakte.setStatus`); Storno braucht eine Begründung. */
    fun setStatus(id: String, status: BauakteStatus, reason: String? = null) {
        val current = bauakte(id) ?: return
        if (status == BauakteStatus.Cancelled) require(!reason.isNullOrBlank()) { "Bitte den Grund für das Storno angeben." }
        store.operation(
            EntityTypes.BAUAKTE, id, "bauakte.setStatus",
            json("status" to status.raw, "baseStatus" to current.status.raw, "reason" to reason.clean()),
            mapOf("status" to toJson(status.raw)),
        )
    }

    fun addNote(bauakteId: String, text: String, pinned: Boolean = false): String {
        require(text.isNotBlank()) { "Bitte einen Text eingeben." }
        val id = Ids.v7()
        val payload = json("bauakteId" to bauakteId.lowercase(), "text" to text.trim(), "pinned" to pinned)
        store.create(EntityTypes.NOTE, id, json(
            "bauakteId" to bauakteId.lowercase(), "text" to text.trim(), "isPinned" to pinned, "authorUserId" to userId(),
            "createdAt" to ApiDate.format(Instant.now()),
        ), payload)
        return id
    }

    fun setNotePinned(note: SiteNote, pinned: Boolean) {
        store.operation(EntityTypes.NOTE, note.id, "note.update", json("pinned" to pinned), mapOf("isPinned" to toJson(pinned)))
    }

    fun deleteNote(note: SiteNote) = store.delete(EntityTypes.NOTE, note.id)

    fun addTask(bauakteId: String?, kind: WorkItemKind, title: String, description: String? = null, due: LocalDate? = null): String {
        require(title.isNotBlank()) { "Bitte einen Titel eingeben." }
        val id = Ids.v7()
        val payload = json(
            "kind" to kind.raw, "title" to title.trim(), "description" to description.clean(), "dueDate" to due?.toString(),
            "assigneeUserId" to userId(), "bauakteId" to bauakteId?.lowercase(),
        )
        store.create(EntityTypes.WORK_ITEM, id, payload.merged(mapOf("status" to toJson("open"), "createdByUserId" to toJson(userId()))), payload)
        return id
    }

    fun setTaskStatus(task: Task, status: WorkItemStatus) = store.update(EntityTypes.WORK_ITEM, task.id, mapOf("status" to toJson(status.raw)))

    fun addDiary(
        bauakteId: String, day: LocalDate, text: String, weather: DiaryWeather?, temperature: Int?, workers: Int?,
        startTime: String? = null, endTime: String? = null, crew: String? = null, photoFileIds: List<String> = emptyList(),
    ): String {
        require(text.isNotBlank()) { "Bitte beschreiben, was heute gemacht wurde." }
        val id = Ids.v7()
        val payload = json(
            "bauakteId" to bauakteId.lowercase(), "day" to day.toString(), "text" to text.trim(), "weather" to weather?.raw,
            "temperatureCelsius" to temperature, "workersPresent" to workers, "startTime" to startTime.clean(), "endTime" to endTime.clean(),
            "crew" to crew.clean(), "photoFileIds" to photoFileIds,
        )
        store.create(EntityTypes.DIARY, id, payload.merged(mapOf("authorUserId" to toJson(userId()), "createdAt" to toJson(ApiDate.format(Instant.now())))), payload)
        return id
    }

    /** Erstbegehung nach Schema: Prüfpunkte, Bemerkungen und Messwerte. */
    fun addInitialInspection(
        bauakteId: String, checks: Map<String, CheckResult>, checkRemarks: Map<String, String>, values: Map<String, Any?>, remarks: String?,
        notes: String?, notRequired: Boolean = false, schemaVersion: Long? = null, photoFileIds: List<String> = emptyList(),
    ): String {
        if (!notRequired) require(checks.isNotEmpty()) { "Bitte die Prüfpunkte bewerten." }
        val missingRemark = checks.filter { it.value == CheckResult.NotOk }.keys.firstOrNull { checkRemarks[it].isNullOrBlank() }
        require(missingRemark == null) { "Bitte bei jedem Mangel eine Bemerkung angeben." }
        val id = Ids.v7()
        val payload = json(
            "bauakteId" to bauakteId.lowercase(), "kind" to "initialInspection", "performedAt" to ApiDate.format(Instant.now()), "notRequired" to notRequired,
            "checks" to checks.mapValues { it.value.raw }, "checkRemarks" to checkRemarks.filterValues { it.isNotBlank() },
            "values" to values.filterValues { it != null }, "remarks" to remarks.clean(), "notes" to notes.clean(), "schemaVersion" to schemaVersion,
            "photoFileIds" to photoFileIds,
        )
        store.create(EntityTypes.INSPECTION, id, payload.merged(mapOf("performedByUserId" to toJson(userId()))), payload)
        return id
    }

    fun addCmMeasurement(bauakteId: String, location: String, binder: ScreedBinder, heated: Boolean, thicknessMm: Int?, measured: Double): String {
        require(location.isNotBlank()) { "Bitte den Messort (Raum) angeben." }
        require(measured in 0.0..20.0) { "Bitte einen gültigen Messwert angeben." }
        val id = Ids.v7()
        val limit = cmLimit(binder, heated)
        val payload = json(
            "bauakteId" to bauakteId.lowercase(), "kind" to "cmMeasurement", "performedAt" to ApiDate.format(Instant.now()), "location" to location.trim(),
            "binder" to binder.raw, "heated" to heated, "thicknessMm" to thicknessMm, "measuredCmPercent" to measured, "notRequired" to false,
        )
        store.create(EntityTypes.INSPECTION, id, payload.merged(mapOf(
            "limitCmPercent" to toJson(limit), "isReadyForCovering" to toJson(measured <= limit), "performedByUserId" to toJson(userId()),
        )), payload)
        return id
    }

    /** Abnahmeprotokoll mit Unterschriften (PNG, Base64). */
    fun addAcceptance(
        bauakteId: String, result: AcceptanceResult, clientName: String?, contractorName: String?, location: String?, scope: String?, notes: String?,
        clientSignaturePng: String?, contractorSignaturePng: String?, photoFileIds: List<String> = emptyList(),
    ): String {
        if (result != AcceptanceResult.Accepted) require(!notes.isNullOrBlank()) { "Bitte Vorbehalte bzw. Gründe angeben." }
        val now = ApiDate.format(Instant.now())
        val id = Ids.v7()
        val payload = json(
            "bauakteId" to bauakteId.lowercase(), "performedAt" to now, "location" to location.clean(), "clientName" to clientName.clean(),
            "contractorName" to contractorName.clean(), "scope" to scope.clean(), "result" to result.raw, "notes" to notes.clean(),
            "photoFileIds" to photoFileIds, "roomIds" to emptyList<String>(), "defectWorkItemIds" to emptyList<String>(),
            "clientSignature" to clientSignaturePng?.let { mapOf("png" to it, "signedAt" to now) },
            "contractorSignature" to contractorSignaturePng?.let { mapOf("png" to it, "signedAt" to now) },
        )
        store.create(EntityTypes.ACCEPTANCE, id, payload.merged(mapOf(
            "state" to toJson("recorded"), "hasClientSignature" to toJson(clientSignaturePng != null), "performedByUserId" to toJson(userId()),
        )), payload)
        return id
    }

    fun saveRoom(
        bauakteId: String, room: Room?, name: String, floor: String?, area: Double?, thickness: Int?, insulation: Int?, heated: Boolean,
        status: RoomStatus, notes: String?,
    ): String {
        require(name.isNotBlank()) { "Bitte den Namen des Raums angeben." }
        area?.let { require(it in 0.0..100_000.0) { "Die Fläche ist ungültig." } }
        thickness?.let { require(it in 10..300) { "Die Estrichdicke muss zwischen 10 und 300 mm liegen." } }
        insulation?.let { require(it in 0..500) { "Die Dämmdicke muss zwischen 0 und 500 mm liegen." } }
        val fields = mapOf(
            "name" to name.trim(), "floor" to floor.clean(), "areaSquareMeters" to area, "thicknessMillimeters" to thickness,
            "insulationMillimeters" to insulation, "underfloorHeating" to heated, "status" to status.raw, "notes" to notes.clean(),
        )
        if (room == null) {
            val id = Ids.v7()
            val payload = json("bauakteId" to bauakteId.lowercase(), *fields.toList().toTypedArray())
            store.create(EntityTypes.ROOM, id, payload, payload)
            return id
        }
        val changes = fields.mapValues { toJson(it.value) }.filter { (k, v) -> room.entity.data[k] ?: JsonNull != v }
        if (changes.isNotEmpty()) store.update(EntityTypes.ROOM, room.id, changes)
        return room.id
    }

    fun deleteRoom(room: Room) = store.delete(EntityTypes.ROOM, room.id)

    fun createContact(customer: NewCustomer): String {
        require(customer.name.isNotBlank()) { "Bitte den Namen des Kunden angeben." }
        val id = Ids.v7()
        val payload = json(
            "name" to customer.name.trim(), "kind" to if (customer.isCompany) "company" else "person", "street" to customer.street.clean(),
            "postalCode" to customer.postalCode.clean(), "city" to customer.city.clean(), "phone" to customer.phone.clean(), "email" to customer.email.clean(),
        )
        store.create(EntityTypes.CONTACT, id, payload, payload)
        return id
    }

    fun updateContact(contact: Contact, changes: Map<String, Any?>) {
        val values: Map<String, JsonElement> = changes.mapValues { toJson(if (it.value is String) (it.value as String).clean() else it.value) }
            .filter { (k, v) -> contact.entity.data[k] ?: JsonNull != v }
        if (values.isNotEmpty()) store.update(EntityTypes.CONTACT, contact.id, values)
    }

    /**
     * Foto, Video oder Dokument aufnehmen: sofort dauerhaft auf dem Gerät speichern, SHA-256 berechnen, ankündigen
     * (`file.announce`) und in die Upload-Warteschlange stellen.
     */
    fun addFile(
        bauakteId: String?, kind: String, bytes: ByteArray, originalName: String, contentType: String, caption: String? = null,
        category: DocumentCategory? = null, capturedAt: Instant = Instant.now(), latitude: Double? = null, longitude: Double? = null,
    ): String {
        val id = Ids.v7()
        files.write(id, bytes)
        val sha = sha256(bytes)
        val data = json(
            "bauakteId" to bauakteId?.lowercase(), "kind" to kind, "originalName" to originalName, "contentType" to contentType, "sizeBytes" to bytes.size,
            "sha256" to sha, "capturedAt" to ApiDate.format(capturedAt), "latitude" to latitude, "longitude" to longitude, "caption" to caption.clean(),
            "category" to category?.raw,
        )
        store.announceFile(data.merged(mapOf("createdAt" to toJson(ApiDate.format(Instant.now())))), PendingUpload(id, bytes.size.toLong(), sha, contentType))
        return id
    }

    fun updateFile(file: FileItem, caption: String?, category: DocumentCategory?) {
        val changes = mutableMapOf<String, JsonElement>()
        if (caption.clean() != file.caption) changes["caption"] = toJson(caption.clean())
        if (file.isDocument && category != file.category) changes["category"] = toJson(category?.raw)
        if (changes.isNotEmpty()) store.update(EntityTypes.FILE, file.id, changes)
    }

    fun deleteFile(file: FileItem) {
        store.delete(EntityTypes.FILE, file.id)
        files.remove(file.id)
    }

    companion object {
        fun sha256(bytes: ByteArray): String = MessageDigest.getInstance("SHA-256").digest(bytes).joinToString("") { "%02x".format(it) }
    }
}

private fun String?.clean(): String? = this?.trim()?.takeIf { it.isNotEmpty() }

/** Nutzdaten ohne leere Werte (für Tests und Anzeige). */
fun JsonObject.compact(): JsonObject = JsonObject(filterValues { it != JsonNull })
