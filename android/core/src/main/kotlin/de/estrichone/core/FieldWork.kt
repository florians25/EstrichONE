package de.estrichone.core

import kotlinx.serialization.Serializable
import kotlinx.serialization.builtins.MapSerializer
import kotlinx.serialization.builtins.serializer
import java.time.LocalDate
import java.time.LocalTime

// Arbeiten auf der Baustelle und beim Kunden: Übertragungsstand in ruhigen Sätzen, Bauakten der kommenden Einsätze auf dem
// Gerät, Serienfotos mit Art und Bereich, Entwürfe, Ankunft und Abschluss, Schnellzugriffe, Prüfung vor dem Abschluss.
// Alles ohne Android-Abhängigkeit, damit es im Kern getestet werden kann.

/** Eine Datei der Upload-Warteschlange mit dem, was die Oberfläche zeigt. */
data class TransferItem(
    val fileId: String, val name: String, val kind: String, val bauakteId: String?, val state: UploadState, val errorCode: String?,
    val size: Long, val uploadedBytes: Long,
) {
    val isWaiting: Boolean get() = state == UploadState.Waiting || state == UploadState.Uploading
    val isFailed: Boolean get() = state == UploadState.Failed
    /** Lässt sich der Versuch wiederholen? Fehlt die Datei auf dem Gerät, hilft kein neuer Versuch. */
    val canRetry: Boolean get() = isFailed && errorCode != "LOCAL_FILE_MISSING"
    val kindLabel: String get() = when (kind) { "video" -> "Video"; "document" -> "Dokument"; else -> "Foto" }
}

/** Übertragungsstand: was wartet, was ist gescheitert, in Worten statt Zahlenkolonnen. */
data class TransferStatus(val items: List<TransferItem>, val pendingChanges: Int, val rejectedChanges: Int = 0) {
    val waiting: List<TransferItem> get() = items.filter { it.isWaiting }
    val failed: List<TransferItem> get() = items.filter { it.isFailed }
    val isSettled: Boolean get() = waiting.isEmpty() && pendingChanges == 0
    val hasProblems: Boolean get() = failed.isNotEmpty() || rejectedChanges > 0

    /** „3 Fotos warten auf Übertragung“, „2 Fotos und 1 Eintrag warten auf Übertragung“; `null`, wenn nichts wartet. */
    val waitingText: String?
        get() {
            val parts = listOfNotNull(
                count(waiting.count { it.kind == "photo" }, "Foto", "Fotos"),
                count(waiting.count { it.kind == "video" }, "Video", "Videos"),
                count(waiting.count { it.kind != "photo" && it.kind != "video" }, "Dokument", "Dokumente"),
                count(pendingChanges, "Eintrag", "Einträge"),
            )
            if (parts.isEmpty()) return null
            val total = waiting.size + pendingChanges
            return "${join(parts)} ${if (total == 1) "wartet" else "warten"} auf Übertragung"
        }

    /** „1 Foto konnte nicht übertragen werden“; `null` ohne Fehler. */
    val failedText: String?
        get() {
            val n = failed.size + rejectedChanges
            if (n == 0) return null
            val what = when {
                rejectedChanges == 0 && failed.all { it.kind == "photo" } -> if (n == 1) "1 Foto" else "$n Fotos"
                rejectedChanges == 0 -> if (n == 1) "1 Datei" else "$n Dateien"
                failed.isEmpty() -> if (n == 1) "1 Eintrag" else "$n Einträge"
                else -> "$n Elemente"
            }
            return "$what ${if (n == 1) "konnte" else "konnten"} nicht übertragen werden"
        }

    /** Nur die Dateien und Einträge einer Bauakte. */
    fun of(bauakteId: String): TransferStatus = TransferStatus(items.filter { it.bauakteId == bauakteId.lowercase() }, 0)

    companion object {
        /** Aus dem lokalen Stand: Upload-Warteschlange mit den Angaben der Datei, dazu die offenen Einträge der Outbox. */
        fun of(store: LocalStore): TransferStatus {
            val outbox = store.backend.outbox()
            val items = store.backend.uploads().filter { it.state != UploadState.Done }.map { upload ->
                val data = store.entity(EntityTypes.FILE, upload.fileId)?.data
                TransferItem(
                    upload.fileId, data?.str("caption")?.takeIf { it.isNotBlank() } ?: data?.str("originalName") ?: "Datei", data?.str("kind") ?: "photo",
                    data?.str("bauakteId")?.lowercase(), upload.state, upload.errorCode, upload.size, upload.uploadedBytes,
                )
            }
            return TransferStatus(
                items,
                pendingChanges = outbox.count { it.state != OutboxState.Rejected && it.type != "file.announce" },
                rejectedChanges = outbox.count { it.state == OutboxState.Rejected },
            )
        }

        /** Verständlicher Grund statt Fehlercode. */
        fun failureText(code: String?): String = when (code) {
            "LOCAL_FILE_MISSING" -> "Die Datei liegt nicht mehr auf dem Gerät. Bitte erneut aufnehmen."
            "FILE_TOO_LARGE", "PAYLOAD_TOO_LARGE" -> "Die Datei ist zu groß für den Server."
            "CHECKSUM_MISMATCH" -> "Die Datei kam beschädigt an. Bitte erneut senden."
            "BAUAKTE_NOT_ASSIGNED" -> "Sie sind an dieser Bauakte nicht mehr beteiligt."
            "NOT_FOUND", "FILE_NOT_FOUND" -> "Der Server kennt diese Datei nicht mehr. Bitte erneut senden."
            "FORBIDDEN" -> "Für diese Bauakte fehlt Ihnen das Recht zum Hochladen."
            "UNSUPPORTED_MEDIA_TYPE", "FILE_TYPE_NOT_ALLOWED" -> "Dieser Dateityp wird vom Server nicht angenommen."
            null, "" -> "Die Übertragung wurde vom Server abgelehnt. Bitte erneut senden."
            else -> "Die Übertragung wurde vom Server abgelehnt ($code). Bitte erneut senden."
        }

        private fun count(n: Int, one: String, many: String): String? = when (n) { 0 -> null; 1 -> "1 $one"; else -> "$n $many" }

        private fun join(parts: List<String>): String =
            if (parts.size <= 1) parts.joinToString("") else parts.dropLast(1).joinToString(", ") + " und " + parts.last()
    }
}

/** Einen gescheiterten Upload erneut einreihen. Liefert `false`, wenn er sich nicht wiederholen lässt. */
fun LocalStore.retryUpload(fileId: String): Boolean {
    val done = backend.transaction {
        val upload = backend.uploads().firstOrNull { it.fileId == fileId.lowercase() && it.state == UploadState.Failed && it.errorCode != "LOCAL_FILE_MISSING" }
            ?: return@transaction false
        backend.putUpload(upload.copy(state = UploadState.Waiting, uploadedBytes = 0, errorCode = null))
        true
    }
    if (done) touch()
    return done
}

/**
 * Bauakten der heutigen und kommenden Einsätze vorab aufs Gerät: Die Daten (Kontakte, Hinweise, Aufgaben, Protokolle) bringt
 * der Abgleich ohnehin mit. Hier wird entschieden, welche Dokumente und Vorschaubilder zusätzlich geladen werden, und
 * festgehalten, was schon da ist.
 */
object OfflinePack {
    /** Einzelne Dokumente bis 25 MB, zusammen höchstens 400 MB; Vorschaubilder der 24 neuesten Fotos je Bauakte. */
    const val MAX_DOCUMENT_BYTES = 25L * 1024 * 1024
    const val MAX_TOTAL_BYTES = 400L * 1024 * 1024
    const val PHOTO_PREVIEWS = 24
    private const val META = "offline_pack"

    /** Schlüssel der Vorschau eines Fotos in der Dateiablage (das Original behält die Datei-ID). */
    fun previewKey(fileId: String) = "${fileId.lowercase()}-thumb"

    /** Bauakten mit Einsatz von [today] bis einschließlich [days] Tage voraus, in der Reihenfolge der Einsätze. */
    fun targets(plan: MyPlanning?, today: LocalDate, days: Long = 6): List<String> {
        val from = today.toString()
        val to = today.plusDays(days).toString()
        return plan?.assignments.orEmpty().filter { it.day in from..to }.sortedBy { it.day + (it.startTime ?: "99") }
            .map { it.bauakteId.lowercase() }.distinct()
    }

    /** Eine Datei, die geladen werden soll: [key] in der Dateiablage, [preview] = nur das Vorschaubild. */
    data class Wanted(val fileId: String, val key: String, val preview: Boolean, val size: Long)

    /** Was für eine Bauakte geladen wird: Dokumente (Pläne, Aufträge) vollständig, von Fotos die Vorschau. */
    fun wanted(files: List<FileItem>): List<Wanted> {
        val stored = files.filter { it.isStoredOnServer }
        val documents = stored.filter { it.isDocument && it.sizeBytes <= MAX_DOCUMENT_BYTES }.map { Wanted(it.id, it.id.lowercase(), false, it.sizeBytes) }
        val previews = stored.filter { it.isPhoto }.take(PHOTO_PREVIEWS).map { Wanted(it.id, previewKey(it.id), true, 40_000) }
        return documents + previews
    }

    /** Stand je Bauakte für die Anzeige. */
    data class State(val documents: Int, val documentsLoaded: Int, val loadedAt: Long?) {
        val isComplete: Boolean get() = documentsLoaded >= documents
        /** „Auf dem Gerät verfügbar“ bzw. „Auf dem Gerät verfügbar, 2 von 5 Dokumenten geladen“. */
        val text: String
            get() = when {
                documents == 0 -> "Auf dem Gerät verfügbar"
                isComplete -> "Auf dem Gerät verfügbar, mit ${if (documents == 1) "1 Dokument" else "$documents Dokumenten"}"
                else -> "Auf dem Gerät verfügbar, $documentsLoaded von $documents Dokumenten geladen"
            }
    }

    /** Schlüssel der geladenen Dateien mit Größe (dauerhaft in den Metadaten, damit nichts doppelt geladen wird). */
    fun loaded(store: LocalStore): Map<String, Long> = store.backend.meta(META)?.let {
        runCatching { EstrichOneApi.json.decodeFromString(MapSerializer(String.serializer(), Long.serializer()), it) }.getOrNull()
    } ?: emptyMap()

    fun remember(store: LocalStore, loaded: Map<String, Long>) {
        store.backend.transaction { store.backend.setMeta(META, EstrichOneApi.json.encodeToString(MapSerializer(String.serializer(), Long.serializer()), loaded)) }
    }

    fun state(files: List<FileItem>, loaded: Map<String, Long>, hasLocalOriginal: (String) -> Boolean = { false }): State {
        val documents = files.filter { it.isDocument }
        val there = documents.count { it.id.lowercase() in loaded || !it.isStoredOnServer || hasLocalOriginal(it.id) }
        return State(documents.size, there, null)
    }

    /**
     * Was zu tun ist: [load] fehlt noch (in Reihenfolge der Einsätze, bis die Obergrenze erreicht ist), [evict] gehört zu
     * keiner Bauakte der kommenden Einsätze mehr und wird vom Gerät entfernt.
     */
    data class Plan(val load: List<Wanted>, val evict: List<String>)

    fun plan(wanted: List<Wanted>, loaded: Map<String, Long>): Plan {
        val keys = wanted.map { it.key }.toSet()
        var total = loaded.filterKeys { it in keys }.values.sum()
        val load = ArrayList<Wanted>()
        for (item in wanted.distinctBy { it.key }) {
            if (item.key in loaded) continue
            if (total + item.size > MAX_TOTAL_BYTES) continue
            total += item.size
            load += item
        }
        return Plan(load, loaded.keys.filter { it !in keys })
    }
}

/** Art eines Baustellenfotos; steht mit dem Bereich in der Beschriftung, damit das Büro die Serie einordnen kann. */
enum class PhotoKind(val label: String) { Before("Vorher"), Laying("Einbau"), After("Nachher"), Defect("Mangel"), Detail("Detail") }

/** Für eine Fotoserie gemerkte Angaben: Art und Raum bzw. Bereich. */
data class PhotoSeries(val kind: PhotoKind? = null, val area: String? = null) {
    /** Beschriftung der Fotos: „Vorher, Bad EG“, „Mangel“, „Bad EG“; ohne Angaben `null`. */
    val caption: String?
        get() = listOfNotNull(kind?.label, area?.trim()?.takeIf { it.isNotEmpty() }).joinToString(", ").ifEmpty { null }

    fun encode(): String = "${kind?.name.orEmpty()}|${area.orEmpty()}"

    companion object {
        fun decode(text: String?): PhotoSeries {
            val parts = text?.split('|', limit = 2) ?: return PhotoSeries()
            return PhotoSeries(PhotoKind.entries.firstOrNull { it.name == parts[0] }, parts.getOrNull(1)?.takeIf { it.isNotBlank() })
        }

        /** Vorschläge für den Bereich: Räume der Bauakte („Bad EG“), ohne Dubletten. */
        fun areas(rooms: List<Room>): List<String> =
            rooms.map { listOfNotNull(it.name.trim().takeIf { n -> n.isNotEmpty() }, it.floor?.trim()?.takeIf { f -> f.isNotEmpty() }).joinToString(" ") }
                .filter { it.isNotEmpty() }.distinct()
    }
}

/**
 * Entwürfe der Erfassung (Tagebuch, Mangel, Notiz, Messung, Erstbegehung, Abnahme): bei jeder Eingabe gesichert, nach
 * Abbruch, Absturz oder Neustart wieder da. Liegen in der verschlüsselten Datenbank des Benutzers, nicht in der Outbox.
 */
class Drafts(private val backend: StorageBackend) {
    private val serializer = MapSerializer(String.serializer(), String.serializer())

    private fun key(form: String, bauakteId: String?) = "draft:$form:${bauakteId?.lowercase() ?: "-"}"

    fun load(form: String, bauakteId: String?): Map<String, String>? = backend.meta(key(form, bauakteId))?.let {
        runCatching { EstrichOneApi.json.decodeFromString(serializer, it) }.getOrNull()
    }?.takeIf { it.isNotEmpty() }

    /** Sichern; leere Formulare hinterlassen keinen Entwurf. */
    fun save(form: String, bauakteId: String?, fields: Map<String, String>) {
        val filled = fields.filterValues { it.isNotBlank() }
        backend.transaction { backend.setMeta(key(form, bauakteId), if (filled.isEmpty()) null else EstrichOneApi.json.encodeToString(serializer, filled)) }
    }

    fun clear(form: String, bauakteId: String?) = backend.transaction { backend.setMeta(key(form, bauakteId), null) }

    companion object {
        const val DIARY = "diary"
        const val TASK = "task"
        const val NOTE = "note"
        const val CM = "cm"
        const val INSPECTION = "inspection"
        const val ACCEPTANCE = "acceptance"
    }
}

/**
 * Ankunft und Abschluss am Einsatz. Der Server kennt dafür (noch) kein eigenes Feld: Die Uhrzeiten bleiben auf dem Gerät
 * und werden als Beginn und Ende des Bautagebuchs vorgeschlagen.
 */
data class SiteVisit(val arrived: String? = null, val finished: String? = null) {
    val isRunning: Boolean get() = arrived != null && finished == null

    /** „Angekommen 07:32 Uhr“, „07:32 bis 15:10 Uhr“; ohne Ankunft `null`. */
    val text: String?
        get() = when {
            arrived != null && finished != null -> "$arrived bis $finished Uhr"
            arrived != null -> "Angekommen $arrived Uhr"
            finished != null -> "Fertig $finished Uhr"
            else -> null
        }

    /** Vorschlag für den Text des Tagebucheintrags. */
    fun diaryText(activity: String?): String = listOfNotNull(
        arrived?.let { "Ankunft $it Uhr" }, finished?.let { "Abschluss $it Uhr" }, activity?.trim()?.takeIf { it.isNotEmpty() },
    ).joinToString(". ").let { if (it.isEmpty()) it else "$it." }

    fun encode(): String = "${arrived.orEmpty()}|${finished.orEmpty()}"

    companion object {
        fun clock(time: LocalTime): String = "%02d:%02d".format(time.hour, time.minute)

        private fun key(assignmentId: String, day: String) = "visit:$day:${assignmentId.lowercase()}"

        fun load(backend: StorageBackend, assignmentId: String, day: String): SiteVisit {
            val parts = backend.meta(key(assignmentId, day))?.split('|') ?: return SiteVisit()
            return SiteVisit(parts.getOrNull(0)?.takeIf { it.isNotEmpty() }, parts.getOrNull(1)?.takeIf { it.isNotEmpty() })
        }

        fun save(backend: StorageBackend, assignmentId: String, day: String, visit: SiteVisit) {
            backend.transaction { backend.setMeta(key(assignmentId, day), if (visit.arrived == null && visit.finished == null) null else visit.encode()) }
        }
    }
}

/** Schnellzugriffe des App-Symbols: Foto, Tagebuch, Mangel in der Bauakte des laufenden Einsatzes. */
object QuickShortcut {
    const val PHOTO = "photo"
    const val DIARY = "diary"
    const val DEFECT = "defect"

    /** Bauakte für den Schnellzugriff: laufender, sonst nächster, sonst letzter Einsatz von heute; ohne Einsatz `null` (Baustelle wählen). */
    fun target(plan: MyPlanning?, today: LocalDate, now: LocalTime): String? {
        val jobs = plan?.assignments.orEmpty().filter { it.day == today.toString() }
        return TodayOverview.plan(jobs, now).focus?.bauakteId
    }
}

/** Was vor dem Abschluss eines Protokolls noch fehlt (Pflichtangaben), als Sätze für die Zusammenfassung. */
object ProtocolCheck {
    fun acceptance(result: AcceptanceResult, clientName: String, contractorName: String, notes: String, clientSigned: Boolean, contractorSigned: Boolean): List<String> =
        listOfNotNull(
            "Name des Kunden bzw. Unterzeichners fehlt.".takeIf { clientName.isBlank() },
            (if (result == AcceptanceResult.Refused) "Gründe der Verweigerung fehlen." else "Vorbehalte fehlen.")
                .takeIf { result != AcceptanceResult.Accepted && notes.isBlank() },
            "Unterschrift des Kunden fehlt.".takeIf { !clientSigned && result != AcceptanceResult.Refused },
            "Name des Auftragnehmers fehlt.".takeIf { contractorName.isBlank() },
            "Unterschrift des Auftragnehmers fehlt.".takeIf { !contractorSigned },
        )

    /** Erstbegehung: jeder Prüfpunkt bewertet, bei „nicht in Ordnung“ eine Bemerkung. */
    fun inspection(checkKeys: List<Pair<String, String>>, checks: Map<String, CheckResult>, remarks: Map<String, String>): List<String> {
        val open = checkKeys.filter { it.first !in checks }
        val withoutRemark = checkKeys.filter { checks[it.first] == CheckResult.NotOk && remarks[it.first].isNullOrBlank() }
        return listOfNotNull(
            when (open.size) {
                0 -> null
                1 -> "Prüfpunkt „${open[0].second}“ ist noch nicht bewertet."
                else -> "${open.size} Prüfpunkte sind noch nicht bewertet."
            },
        ) + withoutRemark.map { "Bemerkung zu „${it.second}“ fehlt." }
    }

    /** „8 in Ordnung, 1 Mangel, 2 entfallen“. */
    fun inspectionSummary(checks: Map<String, CheckResult>): String {
        val ok = checks.values.count { it == CheckResult.Ok }
        val notOk = checks.values.count { it == CheckResult.NotOk }
        val skipped = checks.values.count { it == CheckResult.NotApplicable }
        return listOfNotNull(
            "$ok in Ordnung", when (notOk) { 0 -> null; 1 -> "1 Mangel"; else -> "$notOk Mängel" }, if (skipped > 0) "$skipped entfallen" else null,
        ).joinToString(", ")
    }

    fun cm(location: String, measured: Double?): List<String> = listOfNotNull(
        "Messort (Raum) fehlt.".takeIf { location.isBlank() },
        "Messwert fehlt.".takeIf { measured == null },
        "Der Messwert muss zwischen 0 und 20 CM-% liegen.".takeIf { measured != null && measured !in 0.0..20.0 },
    )
}

/** Freigabelink der Abnahme für den Kunden (befristet, ohne Anmeldung); mit E-Mail-Adresse verschickt ihn der Server. */
@Serializable
data class AcceptanceLink(val url: String, val expiresAt: String? = null, val emailed: Boolean = false)

@Serializable
private data class AcceptanceLinkRequest(val email: String? = null)

suspend fun EstrichOneApi.acceptanceCustomerLink(acceptanceId: String, email: String?): AcceptanceLink =
    send("POST", "acceptances/${acceptanceId.lowercase()}/customer-link", AcceptanceLinkRequest(email?.trim()?.takeIf { it.isNotEmpty() }))
