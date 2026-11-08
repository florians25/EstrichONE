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

/** Ziel einer Benachrichtigung in der App (Bauakte, Meldung, Planung, Aufgaben, Chat, Postfach). */
sealed interface NotificationRoute {
    data class Bauakte(val id: String) : NotificationRoute
    data class Ticket(val id: String) : NotificationRoute
    data class Planning(val day: String?) : NotificationRoute
    data object Tasks : NotificationRoute
    data class Chat(val conversationId: String?) : NotificationRoute
    data object Inbox : NotificationRoute

    companion object {
        private val uuid = Regex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")

        fun from(link: String?, objectType: String? = null, objectId: String? = null): NotificationRoute? {
            // Meldungen im IT-Center (Fehler, Wünsche) haben in der App keine eigene Seite – nicht als Helpdesk-Ticket öffnen.
            if (objectType.equals("feedback", ignoreCase = true)) return Inbox
            link?.let { parse(it) }?.let { return it }
            val id = objectId?.takeIf { uuid.matches(it) }?.lowercase()
            return when (objectType?.lowercase()) {
                "bauakte" -> id?.let { Bauakte(it) }
                "ticket" -> id?.let { Ticket(it) }
                "assignment", "plan", "planday", "planning" -> Planning(null)
                "workitem", "task" -> Tasks
                "chatconversation", "chat", "chatmessage" -> Chat(id)
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
            val day = uri.query?.split('&')?.map { it.split('=', limit = 2) }?.firstOrNull { it[0] == "day" || it[0] == "tag" }?.getOrNull(1)
            return when (head) {
                "bauakte", "bauakten" -> id?.let { Bauakte(it) }
                "helpdesk", "tickets", "ticket", "meldungen" -> id?.let { Ticket(it) }
                "planung", "planning", "plan", "disposition" -> Planning(day ?: parts.drop(1).lastOrNull()?.takeIf { it.length == 10 })
                "aufgaben", "tasks", "work-items", "maengel" -> Tasks
                "chat", "nachrichten", "messenger" -> Chat(id)
                "benachrichtigungen", "notifications", "postfach", "urlaub", "personal", "system" -> Inbox
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

// Helpdesk (Meldungen an die IT)

enum class TicketCategory(val raw: String, val title: String) {
    SignIn("signIn", "Anmeldung"), Sync("sync", "Synchronisation"), AppError("appError", "Fehler in der App"),
    DeviceDefect("deviceDefect", "Gerät defekt"), Access("access", "Zugriff/Rechte"), Other("other", "Sonstiges"),
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
    val hasUnread: Boolean = false,
)

@Serializable
data class TicketComment(val id: String, val authorUserId: String? = null, val authorName: String? = null, val text: String = "",
    val isInternal: Boolean = false, val createdAt: String? = null)

@Serializable
data class TicketFull(
    val summary: TicketListItem, val description: String? = null, val resolution: String? = null, val comments: List<TicketComment> = emptyList(),
    val attachmentFileIds: List<String> = emptyList(), val bauakteId: String? = null,
)

@Serializable
private data class NewTicketRequest(
    val id: String, val title: String, val description: String?, val category: String, val source: String, val reporterBlocked: Boolean,
    val bauakteId: String?, val diagnostics: Map<String, String>, val attachmentFileIds: List<String>,
)

@Serializable
private data class TicketCommentRequest(val text: String, val isInternal: Boolean = false, val attachmentFileIds: List<String> = emptyList())

suspend fun EstrichOneApi.myTickets(): List<TicketListItem> = send("GET", "tickets/mine", null)
suspend fun EstrichOneApi.ticket(id: String): TicketFull = send("GET", "tickets/$id", null)
suspend fun EstrichOneApi.commentTicket(id: String, text: String): TicketFull = send("POST", "tickets/$id/comments", TicketCommentRequest(text.trim()))
suspend fun EstrichOneApi.reopenTicket(id: String): TicketFull = send("POST", "tickets/$id/reopen", JsonObject(emptyMap()))
suspend fun EstrichOneApi.markTicketSeen(id: String) = send<Unit>("POST", "tickets/$id/seen", JsonObject(emptyMap()))

suspend fun EstrichOneApi.createTicket(
    title: String, description: String?, category: TicketCategory, blocked: Boolean, bauakteId: String?, diagnostics: Map<String, String>,
    attachmentFileIds: List<String> = emptyList(),
): TicketFull = send(
    "POST", "tickets",
    NewTicketRequest(Ids.v7(), title.trim(), description?.trim()?.ifEmpty { null }, category.raw, "android", blocked, bauakteId, diagnostics, attachmentFileIds),
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
