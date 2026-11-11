package de.estrichone.core

import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import java.time.Instant

/**
 * Technische Angaben zu einer Meldung (docs/reengineering/14, Abschnitt 3). Nur die Felder, die der Server annimmt
 * (`TicketDiagnostics`): App, System, Gerät, Synchronisation, Netz, Speicher, Fehlercodes. Keine Inhalte aus Bauakten,
 * keine Namen, keine Tokens – die Klasse hat dafür gar keine Eingänge.
 */
data class TicketDiagnosticsReport(
    val appVersion: String,
    val build: String? = null,
    val osVersion: String,
    val deviceModel: String?,
    val deviceKind: String?,
    val deviceId: String? = null,
    val network: String? = null,
    val sync: SyncSummary = SyncSummary(),
    val freeStorageBytes: Long? = null,
    /** Fehlercodes wie `SYNC_UPLOAD_FAILED` mit Zeitpunkt; andere Einträge werden verworfen. */
    val errorCodes: List<Pair<String, Instant?>> = emptyList(),
) {
    /** Was die App sendet (gleiche Schlüssel wie iOS und Server). */
    fun payload(): JsonObject {
        val values = linkedMapOf<String, Any?>(
            "appVersion" to appVersion.take(200), "build" to build?.take(200), "platform" to "Android", "osVersion" to osVersion.take(200),
            "deviceModel" to deviceModel?.take(200), "deviceKind" to deviceKind, "deviceId" to deviceId, "network" to network,
            "syncPendingOperations" to sync.pendingOperations, "syncPendingUploads" to sync.pendingUploads, "syncFailedUploads" to sync.failedUploads,
            "syncConflicts" to sync.rejectedOperations,
            "syncLastSuccess" to sync.lastSuccessfulSync?.let { ApiDate.format(Instant.ofEpochMilli(it)) },
            "freeStorageBytes" to freeStorageBytes,
        )
        val codes = errorCodes.filter { (code, _) -> code.matches(ERROR_CODE) }.takeLast(50)
            .map { (code, at) -> if (at == null) mapOf("code" to code) else mapOf("code" to code, "at" to ApiDate.format(at)) }
        if (codes.isNotEmpty()) values["errorCodes"] = codes
        return json(*values.filterValues { it != null }.toList().toTypedArray())
    }

    /** Anzeige „Anzeigen, was gesendet wird“ vor dem Senden. */
    fun lines(): List<Pair<String, String>> = buildList {
        add("App" to listOfNotNull(appVersion, build?.let { "($it)" }).joinToString(" "))
        add("System" to "Android $osVersion")
        deviceModel?.let { add("Gerät" to it + (deviceKind?.let { k -> if (k == "pool") " · Poolgerät" else " · persönlich" } ?: "")) }
        network?.let { add("Netz" to it) }
        add("Abgleich" to "${sync.pendingOperations} Vorgänge, ${sync.pendingUploads} Dateien ausstehend" +
            (if (sync.failedUploads > 0) ", ${sync.failedUploads} fehlgeschlagen" else ""))
        freeStorageBytes?.let { add("Speicher frei" to "%.1f GB".format(java.util.Locale.GERMANY, it / 1_000_000_000.0)) }
        if (errorCodes.isNotEmpty()) add("Fehlercodes" to errorCodes.takeLast(5).joinToString(", ") { it.first })
    }

    companion object {
        private val ERROR_CODE = Regex("^[A-Z][A-Z0-9_]{1,63}$")
    }
}

/** Eingaben von „Problem melden“. Pflicht ist nur der Titel. */
data class NewTicket(
    val title: String,
    val description: String? = null,
    val category: TicketCategory = TicketCategory.Other,
    val priority: TicketPriority? = null,
    val reporterBlocked: Boolean = false,
    val subcategoryId: String? = null,
)

/** Meldung, die noch auf dem Gerät wartet (Outbox) oder gerade übertragen wurde. */
data class LocalTicket(val id: String, val title: String, val number: String?, val state: SyncState, val createdAt: String?)

/**
 * „Problem melden“ offline: Fotos und Bildschirmfotos gehen als eigene Dateien ohne Bauakte in die Upload-Warteschlange,
 * das Ticket als Outbox-Operation `ticket.create` (genau einmal über die opId, docs/reengineering/14, AK-1).
 */
class HelpdeskReporter(private val store: LocalStore, private val service: BauakteService, private val now: () -> Instant = Instant::now) {
    fun report(ticket: NewTicket, diagnostics: TicketDiagnosticsReport?, photos: List<ByteArray> = emptyList(), source: String = "android"): String {
        val title = ticket.title.trim()
        require(title.isNotEmpty()) { "Bitte kurz beschreiben, was passiert ist." }
        require(title.length <= 200) { "Bitte kürzer fassen (höchstens 200 Zeichen)." }
        val attachments = photos.take(MAX_PHOTOS).mapIndexed { i, bytes ->
            service.addFile(null, "photo", bytes, "Bildschirmfoto-${i + 1}.jpg", "image/jpeg", capturedAt = now())
        }
        val id = Ids.v7()
        val priority = ticket.priority ?: if (ticket.reporterBlocked) TicketPriority.Urgent else null
        val payload = json(
            "title" to title, "description" to ticket.description?.trim()?.ifEmpty { null }, "category" to ticket.category.raw,
            "reporterBlocked" to ticket.reporterBlocked, "source" to source, "priority" to priority?.raw, "subcategoryId" to ticket.subcategoryId,
            "diagnostics" to diagnostics?.payload(), "attachmentFileIds" to attachments,
        )
        val local = payload.merged(mapOf("id" to JsonPrimitive(id), "status" to JsonPrimitive("new"), "createdAt" to JsonPrimitive(ApiDate.format(now()))))
        store.create(EntityTypes.TICKET, id, local, payload, "ticket.create")
        return id
    }

    /** Eigene Meldungen, die noch nicht beim Server sind (Anzeige „wird gesendet“). */
    fun pending(): List<LocalTicket> = store.entities(EntityTypes.TICKET)
        .map { LocalTicket(it.id, it.data.str("title") ?: "", it.data.str("number"), it.syncState, it.data.str("createdAt")) }
        .filter { it.number == null || it.state != SyncState.Synced }
        .sortedByDescending { it.createdAt }

    companion object {
        const val MAX_PHOTOS = 5
    }
}
