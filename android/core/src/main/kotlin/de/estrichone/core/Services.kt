package de.estrichone.core

import kotlinx.serialization.Serializable
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import java.net.URI
import java.time.Instant

// Benachrichtigungen (Postfach, docs/benachrichtigungen.md)

@Serializable
data class InboxNotification(
    val id: String,
    val category: String? = null,
    val urgency: String? = null,
    val title: String = "Benachrichtigung",
    val body: String? = null,
    val link: String? = null,
    val objectType: String? = null,
    val objectId: String? = null,
    val senderName: String? = null,
    val createdAt: String? = null,
    val readAt: String? = null,
) {
    val isRead: Boolean get() = readAt != null
    val isUrgent: Boolean get() = urgency == "high"
    val created: Instant? get() = ApiDate.parse(createdAt)
    val route: NotificationRoute? get() = NotificationRoute.from(link, objectType, objectId)
    /** Art fürs Postfach (Filter, Symbol, Farbe). */
    val kind: NotificationKind get() = NotificationKind.of(category)
    /** Öffnet ein Objekt in der App (sonst zeigt das Postfach die Einzelansicht). */
    val hasTarget: Boolean get() = route.let { it != null && it != NotificationRoute.Inbox }
    /** Pfad der Weboberfläche für „Im Browser öffnen“ (z. B. /system/backup-ziele). */
    val webPath: String? get() = link?.trim()?.takeIf { it.startsWith("/") && !it.startsWith("/api/") }
}

@Serializable
data class NotificationInbox(val items: List<InboxNotification> = emptyList(), val unreadCount: Int = 0)

@Serializable
data class NotificationPreference(
    val event: String, val group: String = "", val label: String = "", val description: String = "", val adminOnly: Boolean = false,
    val mandatory: Boolean = false, val delivery: String = "inbox",
)

@Serializable
data class NotificationPreferences(val items: List<NotificationPreference> = emptyList())

@Serializable
private data class PreferenceChange(val event: String, val delivery: String)

@Serializable
private data class PreferenceChanges(val items: List<PreferenceChange>)

@Serializable
private data class PushDeviceRequest(val platform: String, val token: String, val deviceName: String?, val appVersion: String?)

/** Ziel einer Benachrichtigung in der App (Bauakte, Meldung, Planung, Aufgaben, Chat, Urlaub, Postfach). */
sealed interface NotificationRoute {
    /** [section]: Bereich in der Bauakte, der gleich geöffnet wird (Aufgaben und Mängel, Dokumente zur Freigabe). */
    data class Bauakte(val id: String, val section: BauakteSection? = null) : NotificationRoute
    data class Ticket(val id: String) : NotificationRoute
    data class Planning(val day: String?) : NotificationRoute
    data object Tasks : NotificationRoute
    data class Chat(val conversationId: String?) : NotificationRoute
    /** Urlaub und Abwesenheit; [approvals]: Anträge zur Genehmigung (Hinweis an Genehmiger). */
    data class Absences(val approvals: Boolean = false) : NotificationRoute
    data object Inbox : NotificationRoute

    enum class BauakteSection { Tasks, Documents }

    companion object {
        private val uuid = Regex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")

        fun from(link: String?, objectType: String? = null, objectId: String? = null): NotificationRoute? {
            // Meldungen im IT-Center (Fehler, Wünsche) haben in der App keine eigene Seite – nicht als Helpdesk-Ticket öffnen.
            if (objectType.equals("feedback", ignoreCase = true)) return Inbox
            val type = objectType?.lowercase()
            link?.let { parse(it) }?.let { route ->
                // Aufgabe, Mangel oder Freigabe in einer Bauakte: gleich den passenden Bereich zeigen.
                if (route is Bauakte && route.section == null) {
                    when (type) {
                        "workitem", "task", "defect" -> return route.copy(section = BauakteSection.Tasks)
                        "approval" -> return route.copy(section = BauakteSection.Documents)
                    }
                }
                return route
            }
            val id = objectId?.takeIf { uuid.matches(it) }?.lowercase()
            return when (type) {
                "bauakte" -> id?.let { Bauakte(it) }
                "ticket" -> id?.let { Ticket(it) }
                "assignment", "plan", "planday", "planning" -> Planning(null)
                "workitem", "task" -> Tasks
                "chatconversation", "chat", "chatmessage" -> Chat(id)
                "absencerequest", "absence" -> Absences()
                "approval" -> Inbox
                else -> null
            } ?: if (link != null) Inbox else null
        }

        private fun parse(link: String): NotificationRoute? {
            val uri = runCatching { URI(link.trim()) }.getOrNull() ?: return null
            val parts = (uri.path ?: "").split('/').filter { it.isNotEmpty() }.toMutableList()
            if (uri.scheme == "estrichone" && uri.host != null) parts.add(0, uri.host)
            while (parts.isNotEmpty() && parts.first().lowercase() in setOf("app", "it-center", "api", "v1")) parts.removeAt(0)
            val head = parts.firstOrNull()?.lowercase() ?: return null
            val id = parts.getOrNull(1)?.takeIf { uuid.matches(it) }?.lowercase()
            val query = uri.query?.split('&')?.map { it.split('=', limit = 2) }.orEmpty()
            val day = query.firstOrNull { it[0] == "day" || it[0] == "tag" }?.getOrNull(1)
            return when (head) {
                "bauakte", "bauakten" -> id?.let {
                    Bauakte(it, when (parts.getOrNull(2)?.lowercase()) {
                        "maengel", "aufgaben", "tasks", "work-items" -> BauakteSection.Tasks
                        "dokumente", "documents", "freigaben" -> BauakteSection.Documents
                        else -> null
                    })
                }
                "helpdesk", "tickets", "ticket", "meldungen" -> id?.let { Ticket(it) }
                "planung", "planning", "plan", "disposition" -> Planning(day ?: parts.drop(1).lastOrNull()?.takeIf { it.length == 10 })
                "aufgaben", "tasks", "work-items", "maengel" -> Tasks
                "chat", "nachrichten", "messenger" -> Chat(id)
                "urlaub" -> Absences(query.any { it[0] == "reiter" && it.getOrNull(1) == "antraege" })
                "personal" -> if (parts.getOrNull(1)?.lowercase() in setOf("urlaub", "abwesenheit", null)) {
                    Absences(query.any { it[0] == "reiter" && it.getOrNull(1) == "antraege" })
                } else Inbox
                "benachrichtigungen", "notifications", "postfach", "system" -> Inbox
                else -> null
            }
        }
    }
}

suspend fun EstrichOneApi.notifications(): NotificationInbox = send("GET", "notifications", null)
/** Seite des Postfachs: höchstens [limit] Einträge, optional nur ältere als [before] („Ältere laden“). */
suspend fun EstrichOneApi.notifications(limit: Int, before: Instant? = null): NotificationInbox =
    send("GET", "notifications", null, query = mapOf("limit" to limit.toString(), "before" to before?.let { ApiDate.format(it) }))
suspend fun EstrichOneApi.markNotificationUnread(id: String) = send<Unit>("POST", "notifications/$id/unread", JsonObject(emptyMap()))
suspend fun EstrichOneApi.markNotificationRead(id: String) = send<Unit>("POST", "notifications/$id/read", JsonObject(emptyMap()))
suspend fun EstrichOneApi.markAllNotificationsRead() = send<JsonElement>("POST", "notifications/read-all", JsonObject(emptyMap()))
suspend fun EstrichOneApi.deleteNotification(id: String) = send<Unit>("DELETE", "notifications/$id", null)
suspend fun EstrichOneApi.notificationPreferences(): NotificationPreferences = send("GET", "notifications/preferences", null)
suspend fun EstrichOneApi.saveNotificationPreference(event: String, delivery: String): NotificationPreferences =
    send("PUT", "notifications/preferences", PreferenceChanges(listOf(PreferenceChange(event, delivery))))

// Systemzustand

suspend fun EstrichOneApi.systemNotices(): SystemNotices = send("GET", "system/notices", null, authenticated = false)

suspend fun EstrichOneApi.healthBanners(): List<HealthBanner> = try {
    SystemHealth.banners(send<JsonElement>("GET", "system/health-banner", null))
} catch (e: ApiException) {
    if (e.status == 404) emptyList() else throw e
}

suspend fun EstrichOneApi.myPermissions(): List<String> = send("GET", "access/me/permissions", null)

/** Einstellung „Hinweis Erstbegehung fehlt“ (Systemeinstellungen, für alle Angemeldeten lesbar); ohne Angabe eingeschaltet. */
suspend fun EstrichOneApi.warnMissingInspection(): Boolean =
    (send<JsonElement>("GET", "settings", null) as? JsonObject)?.obj("settings")?.obj("bauakte")?.bool("warnMissingInspection") ?: true

/** Bauakte sich selbst zuweisen („Mir zuweisen“), danach per Snapshot laden. */
suspend fun EstrichOneApi.assignToMe(bauakteId: String) = send<JsonElement>("POST", "bauakten/$bauakteId/members/me", JsonObject(emptyMap()))

// Planung

@Serializable
data class PersonRef(val id: String, val name: String)

@Serializable
data class ResourceRef(val id: String, val name: String, val kind: String? = null)

@Serializable
data class PlannedJob(
    val id: String,
    val day: String,
    val startTime: String? = null,
    val endTime: String? = null,
    val activity: String,
    val bauakteId: String,
    val bauakteNumber: String? = null,
    val bauakteTitle: String,
    val bauakteAddress: String? = null,
    val crewName: String? = null,
    val crewColor: String? = null,
    val people: List<PersonRef> = emptyList(),
    val resources: List<ResourceRef> = emptyList(),
    val notes: String? = null,
    val warnings: List<String> = emptyList(),
    val kind: String? = null,
) {
    val time: String
        get() = listOfNotNull(Dates.clock(startTime), Dates.clock(endTime)).joinToString(" – ").ifEmpty { "ganztags" }
}

@Serializable
data class AbsenceEntry(val id: String, val userId: String, val userName: String, val from: String, val to: String, val kind: String, val note: String? = null)

@Serializable
data class MyPlanning(val from: String, val to: String, val assignments: List<PlannedJob> = emptyList(), val absences: List<AbsenceEntry> = emptyList())

suspend fun EstrichOneApi.myPlan(from: String, to: String): MyPlanning = send("GET", "planning/mine", null, query = mapOf("from" to from, "to" to to))

@Serializable
data class DispatchJob(val assignmentId: String, val activity: String, val startTime: String? = null, val endTime: String? = null,
    val crewName: String? = null, val crewColor: String? = null, val notes: String? = null)

@Serializable
data class DispatchSite(
    val bauakteId: String, val number: String? = null, val title: String, val address: String? = null, val latitude: Double? = null,
    val longitude: Double? = null, val siteContactName: String? = null, val siteContactPhone: String? = null, val startTime: String? = null,
    val jobs: List<DispatchJob> = emptyList(), val people: List<PersonRef> = emptyList(), val vehicles: List<ResourceRef> = emptyList(),
    val warnings: List<String> = emptyList(),
)

@Serializable
data class DispatchBoard(val day: String, val sites: List<DispatchSite> = emptyList(), val mySiteIds: List<String> = emptyList(), val canPlan: Boolean = false)

/** „Wer fährt wohin“ für einen Tag. */
suspend fun EstrichOneApi.dispatch(day: String): DispatchBoard = send("GET", "planning/dispatch/$day", null)

// Helpdesk (Meldungen an die IT) – Modelle und Aufrufe; Melden offline siehe Helpdesk.kt

enum class TicketCategory(val raw: String, val title: String) {
    SignIn("signIn", "Anmeldung"), Sync("sync", "Synchronisation"), AppError("appError", "Fehler in der App"),
    DeviceDefect("deviceDefect", "Gerät defekt"), OfficeIt("officeIt", "Drucker/Büro-IT"), Access("access", "Zugang/Rechte"), Other("other", "Sonstiges");

    companion object {
        fun of(raw: String?): TicketCategory? = entries.firstOrNull { it.raw == raw }
    }
}

enum class TicketPriority(val raw: String, val title: String) {
    Low("low", "Niedrig"), Normal("normal", "Normal"), High("high", "Hoch"), Urgent("urgent", "Dringend");

    companion object {
        fun of(raw: String?): TicketPriority = entries.firstOrNull { it.raw == raw } ?: Normal
    }
}

fun ticketStatusLabel(status: String): String = when (status) {
    "new" -> "Neu"
    "inProgress" -> "In Bearbeitung"
    "waitingForReporter" -> "Rückfrage an Sie"
    "resolved" -> "Gelöst"
    "closed" -> "Geschlossen"
    else -> status
}

@Serializable
data class TicketListItem(
    val id: String, val number: String, val title: String, val category: String = "other", val status: String = "new",
    val reporterBlocked: Boolean = false, val assigneeName: String? = null, val createdAt: String? = null, val lastActivityAt: String? = null,
    val hasUnread: Boolean = false, val priority: String = "normal", val reporterDisplayName: String? = null, val assigneeUserId: String? = null,
    val subcategoryName: String? = null, val groupName: String? = null, val slaState: String? = null, val dueAt: String? = null,
    val dueKind: String? = null, val rating: Int? = null, val groupId: String? = null, val subcategoryId: String? = null,
    /** Eingang: selfService, phone, email, inPerson (Melder angerufen, gemailt, vor Ort – dann hat die IT erfasst). */
    val channel: String? = null, val createdByName: String? = null,
) {
    val isOpen: Boolean get() = status != "resolved" && status != "closed"

    /** „Telefon, aufgenommen von Anna Becker“ – nur, wenn die IT die Meldung erfasst hat. */
    val intakeText: String? get() = ticketChannelLabel(channel)?.let { c -> createdByName?.let { "$c, aufgenommen von $it" } ?: c }
}

/** Eingangskanal lesbar; null bei „selbst gemeldet“ bzw. unbekannt. */
fun ticketChannelLabel(channel: String?): String? = when (channel) {
    "phone" -> "Telefon"
    "email" -> "E-Mail"
    "inPerson" -> "Vor Ort"
    else -> null
}

/** Bearbeiter (Zuweisen, Erwähnen) mit Anzahl offener Tickets. */
@Serializable
data class HelpdeskAgent(val id: String, val displayName: String, val openTickets: Int = 0)

@Serializable
data class HelpdeskGroup(val id: String, val name: String, val isActive: Boolean = true)

/** Textbaustein der Bearbeitung (gemeinsam oder eigener). */
@Serializable
data class CannedResponse(val id: String, val name: String, val text: String, val category: String? = null, val isPersonal: Boolean = false)

/** Person, für die die IT eine Meldung aufnimmt. */
@Serializable
data class HelpdeskReporterPerson(val id: String, val displayName: String, val userName: String = "", val email: String? = null, val phone: String? = null)

/** Metadaten einer Datei (Anhang im Verlauf). */
@Serializable
data class StoredFileInfo(val id: String, val originalName: String = "Anhang", val contentType: String = "application/octet-stream", val sizeBytes: Long = 0) {
    val isImage: Boolean get() = contentType.startsWith("image/")
}

/**
 * Textbaustein mit Platzhaltern füllen (wie im Web): {Melder}, {Vorname}, {Ticket}, {Titel}, {Bearbeiter}.
 * „(laut Eingabe)“ hinter Namen von Meldungen ohne Anmeldung fällt weg.
 */
fun fillTicketPlaceholders(text: String, reporter: String?, ticket: String, title: String, agent: String?): String {
    val name = (reporter ?: "").removeSuffix(" (laut Eingabe)").trim()
    val first = name.split(Regex("\\s+")).firstOrNull().orEmpty()
    return text.replace("{Melder}", name.ifEmpty { "zusammen" }).replace("{Vorname}", first.ifEmpty { "zusammen" })
        .replace("{Ticket}", ticket).replace("{Titel}", title).replace("{Bearbeiter}", agent ?: "Ihre IT")
}

@Serializable
data class TicketComment(val id: String, val authorUserId: String? = null, val authorName: String? = null, val text: String = "",
    val isInternal: Boolean = false, val createdAt: String? = null)

/** Eintrag im Verlauf (Server liefert fertige deutsche Sätze). */
@Serializable
data class TicketTimelineEntry(
    val id: String, val at: String, val kind: String, val actorUserId: String? = null, val actorName: String? = null, val summary: String = "",
    val text: String? = null, val isInternal: Boolean = false, val isReporter: Boolean = false, val attachmentFileIds: List<String> = emptyList(),
) {
    /** Antwort (Sprechblase) statt Ereigniszeile. */
    val isMessage: Boolean get() = kind == "created" || kind == "comment" || kind == "note"
}

@Serializable
data class TicketArticleRef(val id: String, val title: String, val isPublished: Boolean = true)

@Serializable
data class TicketFull(
    val summary: TicketListItem, val description: String? = null, val resolution: String? = null, val comments: List<TicketComment> = emptyList(),
    val attachmentFileIds: List<String> = emptyList(), val bauakteId: String? = null, val timeline: List<TicketTimelineEntry>? = null,
    val articles: List<TicketArticleRef>? = null, val mergedIntoNumber: String? = null, val rating: Int? = null, val ratingComment: String? = null,
    val canRate: Boolean = false,
) {
    /** Verlauf für die Anzeige, älteste zuerst: vom Server oder (ältere Server) aus Beschreibung und Antworten. */
    fun history(includeInternal: Boolean = false): List<TicketTimelineEntry> = timeline?.filter { includeInternal || !it.isInternal }?.sortedBy { it.at }
        ?: comments.filter { includeInternal || !it.isInternal }.map {
            TicketTimelineEntry(it.id, it.createdAt ?: "", if (it.isInternal) "note" else "comment", it.authorUserId, it.authorName,
                "${it.authorName ?: "Jemand"} hat geantwortet", it.text, it.isInternal)
        }
}

@Serializable
data class HelpdeskSubcategory(val id: String, val category: String, val name: String, val isActive: Boolean = true)

@Serializable
data class HelpdeskCategoryOption(val key: String, val name: String, val subcategories: List<HelpdeskSubcategory> = emptyList())

@Serializable
data class HelpdeskOptions(val categories: List<HelpdeskCategoryOption> = emptyList(), val ratingEnabled: Boolean = true, val suggestArticles: Boolean = true)

@Serializable
data class KnowledgeArticleSummary(val id: String, val title: String, val excerpt: String = "", val category: String? = null)

@Serializable
data class KnowledgeArticle(val id: String, val title: String, val body: String = "", val category: String? = null)

@Serializable
private data class NewTicketRequest(
    val id: String, val title: String, val description: String?, val category: String, val source: String, val reporterBlocked: Boolean,
    val bauakteId: String?, val diagnostics: JsonObject?, val attachmentFileIds: List<String>, val priority: String? = null, val subcategoryId: String? = null,
)

@Serializable
private data class TicketCommentRequest(val text: String, val isInternal: Boolean = false, val attachmentFileIds: List<String> = emptyList())

@Serializable
private data class TicketRatingRequest(val rating: Int, val comment: String? = null)

/** Änderung der Bearbeitung (PATCH /tickets/{id}); null = unverändert. */
@Serializable
data class TicketUpdate(
    val status: String? = null, val resolution: String? = null, val priority: String? = null, val category: String? = null,
    val assigneeUserId: String? = null, val unassign: Boolean = false, val groupId: String? = null, val clearGroup: Boolean = false,
    val subcategoryId: String? = null, val clearSubcategory: Boolean = false,
)

@Serializable
private data class TicketOnBehalfRequest(
    val id: String, val title: String, val description: String?, val category: String, val channel: String, val source: String,
    val reporterUserId: String?, val reporterName: String?, val reporterBlocked: Boolean, val priority: String?, val subcategoryId: String?,
    val assigneeUserId: String?,
)

@Serializable
private data class TicketStatusRequest(val status: String? = null, val resolution: String? = null, val assigneeUserId: String? = null, val unassign: Boolean = false)

suspend fun EstrichOneApi.myTickets(): List<TicketListItem> = send("GET", "tickets/mine", null)
suspend fun EstrichOneApi.ticket(id: String): TicketFull = send("GET", "tickets/$id", null)
suspend fun EstrichOneApi.commentTicket(id: String, text: String): TicketFull = send("POST", "tickets/$id/comments", TicketCommentRequest(text.trim()))
suspend fun EstrichOneApi.reopenTicket(id: String): TicketFull = send("POST", "tickets/$id/reopen", JsonObject(emptyMap()))
suspend fun EstrichOneApi.markTicketSeen(id: String) = send<Unit>("POST", "tickets/$id/seen", JsonObject(emptyMap()))
suspend fun EstrichOneApi.rateTicket(id: String, rating: Int, comment: String?): TicketFull =
    send("POST", "tickets/$id/rating", TicketRatingRequest(rating.coerceIn(1, 5), comment?.trim()?.ifEmpty { null }))

/** Bearbeitung (Recht helpdesk.agent): Ansichten `mine`, `unassigned`, `overdue`, `waiting`, `open`. */
suspend fun EstrichOneApi.agentTickets(filter: String = "mine"): List<TicketListItem> = send("GET", "tickets", null, query = mapOf("filter" to filter, "sort" to "due"))
suspend fun EstrichOneApi.updateTicketStatus(id: String, status: String, resolution: String? = null): TicketFull =
    send("PATCH", "tickets/$id", TicketStatusRequest(status = status, resolution = resolution?.trim()?.ifEmpty { null }))
suspend fun EstrichOneApi.assignTicketToMe(id: String, userId: String): TicketFull = send("PATCH", "tickets/$id", TicketStatusRequest(assigneeUserId = userId))
suspend fun EstrichOneApi.agentReply(id: String, text: String, internal: Boolean): TicketFull =
    send("POST", "tickets/$id/comments", TicketCommentRequest(text.trim(), isInternal = internal))
suspend fun EstrichOneApi.helpdeskOptions(): HelpdeskOptions = send("GET", "helpdesk/options", null)
suspend fun EstrichOneApi.suggestArticles(query: String, category: String?): List<KnowledgeArticleSummary> =
    send("GET", "helpdesk/articles/suggest", null, query = mapOf("q" to query, "category" to category))
suspend fun EstrichOneApi.knowledgeArticle(id: String): KnowledgeArticle = send("GET", "helpdesk/articles/$id", null)
suspend fun EstrichOneApi.updateTicket(id: String, change: TicketUpdate): TicketFull = send("PATCH", "tickets/$id", change)
suspend fun EstrichOneApi.helpdeskAgents(): List<HelpdeskAgent> = send("GET", "helpdesk/agents", null)
suspend fun EstrichOneApi.helpdeskGroups(): List<HelpdeskGroup> = send<List<HelpdeskGroup>>("GET", "helpdesk/groups", null).filter { it.isActive }
suspend fun EstrichOneApi.cannedResponses(): List<CannedResponse> = send("GET", "helpdesk/responses", null)
suspend fun EstrichOneApi.helpdeskReporters(query: String): List<HelpdeskReporterPerson> =
    send("GET", "helpdesk/reporters", null, query = mapOf("q" to query.trim().ifEmpty { null }, "limit" to "30"))
suspend fun EstrichOneApi.fileInfo(id: String): StoredFileInfo = send("GET", "files/$id", null)

/** Meldung aufnehmen (IT nach Anruf, E-Mail, vor Ort): Ticket gehört dem Melder; ohne Zugang genügt der Name. */
suspend fun EstrichOneApi.createTicketOnBehalf(
    title: String, description: String?, category: TicketCategory, channel: String, reporterUserId: String?, reporterName: String?,
    priority: TicketPriority = TicketPriority.Normal, blocked: Boolean = false, subcategoryId: String? = null, assigneeUserId: String? = null,
): TicketFull = send(
    "POST", "tickets/on-behalf",
    TicketOnBehalfRequest(Ids.v7(), title.trim(), description?.trim()?.ifEmpty { null }, category.raw, channel, "android", reporterUserId,
        if (reporterUserId == null) reporterName?.trim()?.ifEmpty { null } else null, blocked, priority.raw, subcategoryId, assigneeUserId),
)

/** Direkt senden (online, ältere Wege); die App meldet sonst über [HelpdeskReporter] offline-fähig. */
suspend fun EstrichOneApi.createTicket(
    title: String, description: String?, category: TicketCategory, blocked: Boolean, bauakteId: String?, diagnostics: JsonObject?,
    attachmentFileIds: List<String> = emptyList(), priority: TicketPriority? = null,
): TicketFull = send(
    "POST", "tickets",
    NewTicketRequest(Ids.v7(), title.trim(), description?.trim()?.ifEmpty { null }, category.raw, "android", blocked, bauakteId, diagnostics,
        attachmentFileIds, priority?.raw),
)

// Konto und Sicherheit

@Serializable
data class SessionInfo(val id: String, val client: String = "", val clientName: String? = null, val deviceBound: Boolean = false,
    val createdAt: String? = null, val lastSeenAt: String? = null, val current: Boolean = false)

@Serializable
data class TwoFactorStatus(val enabled: Boolean = false, val recoveryCodesRemaining: Int = 0, val requiredByPolicy: Boolean = false)

suspend fun EstrichOneApi.sessions(): List<SessionInfo> = send("GET", "auth/sessions", null)
suspend fun EstrichOneApi.revokeSession(id: String) = send<Unit>("DELETE", "auth/sessions/$id", null)
suspend fun EstrichOneApi.twoFactorStatus(): TwoFactorStatus = send("GET", "auth/two-factor", null)
