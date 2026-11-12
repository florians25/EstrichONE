package de.estrichone.core

import kotlinx.serialization.Serializable
import kotlinx.serialization.json.JsonObject
import java.time.Duration
import java.time.Instant

// Chat (WhatsApp-Ersatz, docs/chat.md): Einzel-, Gruppen-, Kolonnen- und Baustellenchats.

@Serializable
data class ChatStatus(
    val enabled: Boolean = false, val canUse: Boolean = false, val canAdmin: Boolean = false, val unreadCount: Int = 0,
    val unreadConversations: Int = 0, val retentionDays: Int? = null, val showPreview: Boolean = true, val privacyAccepted: Boolean = true,
    val editWindowMinutes: Int = 15, val attachmentsEnabled: Boolean = true, val readReceipts: Boolean = true, val privacyNoticeVersion: Int = 0,
    /** Modus „Microsoft Teams“: Chat-Archiv – lesen und exportieren, nicht schreiben. */
    val readOnly: Boolean = false,
) {
    val isAvailable: Boolean get() = enabled && canUse
}

@Serializable
data class ChatPerson(val id: String, val name: String)

@Serializable
data class ChatMemberState(val userId: String, val name: String, val readUpTo: String? = null, val fromCrew: Boolean = false, val isAdmin: Boolean = false)

@Serializable
data class ChatAttachment(
    val id: String, val kind: String? = null, val fileName: String = "", val contentType: String = "", val sizeBytes: Long = 0,
    val width: Int? = null, val height: Int? = null, val hasThumbnail: Boolean = false,
) {
    val isPhoto: Boolean get() = kind == "photo" || contentType.startsWith("image/")
}

@Serializable
data class ChatReaction(val emoji: String, val count: Int, val mine: Boolean = false, val names: List<String> = emptyList())

@Serializable
data class ChatReply(val id: String, val senderName: String? = null, val text: String? = null, val deleted: Boolean = false, val attachment: String? = null)

@Serializable
data class ChatLocation(val latitude: Double, val longitude: Double, val label: String? = null)

@Serializable
data class ChatMessage(
    val id: String,
    val conversationId: String? = null,
    val senderUserId: String? = null,
    val senderName: String? = null,
    val text: String? = null,
    val createdAt: String,
    val mine: Boolean = false,
    val deleted: Boolean = false,
    val status: String? = null,
    val readBy: Int = 0,
    val recipients: Int = 0,
    val clientMessageId: String? = null,
    val replyTo: ChatReply? = null,
    val attachments: List<ChatAttachment> = emptyList(),
    val reactions: List<ChatReaction> = emptyList(),
    val editedAt: String? = null,
    val forwarded: Boolean = false,
    val pinnedAt: String? = null,
    val location: ChatLocation? = null,
    val canEdit: Boolean = false,
    /** Nur lokal: wartet auf das Senden bzw. fehlgeschlagen. */
    val localState: String? = null,
) {
    val created: Instant get() = ApiDate.parse(createdAt) ?: Instant.EPOCH
    val isSystem: Boolean get() = senderUserId == null && !mine
    val isRead: Boolean get() = recipients > 0 && readBy >= recipients

    /** Vorschau in der Liste: Text bzw. „Foto“. */
    val summary: String
        get() = when {
            deleted -> "Nachricht gelöscht"
            !text.isNullOrBlank() -> text
            attachments.any { it.isPhoto } -> "Foto"
            attachments.isNotEmpty() -> attachments.first().fileName
            location != null -> "Standort"
            else -> ""
        }

    fun isEditable(windowMinutes: Int, now: Instant = Instant.now()): Boolean =
        mine && !deleted && Duration.between(created, now).toMinutes() < windowMinutes
}

@Serializable
data class ChatConversation(
    val id: String,
    val kind: String = "group",
    val title: String,
    val crewId: String? = null,
    val members: List<ChatPerson> = emptyList(),
    val lastMessage: ChatMessage? = null,
    val lastActivityAt: String? = null,
    val unreadCount: Int = 0,
    val muted: Boolean = false,
    val partnerUserId: String? = null,
    val description: String? = null,
    val bauakteId: String? = null,
    val announcementOnly: Boolean = false,
    val isAdmin: Boolean = false,
    val canPost: Boolean = true,
    val unreadMentions: Int = 0,
) {
    val isDirect: Boolean get() = kind == "direct"
    val lastActivity: Instant? get() = ApiDate.parse(lastActivityAt) ?: lastMessage?.created

    /** Art für Symbol und Untertitel. */
    val type: ChatType
        get() = when {
            isDirect -> ChatType.Direct
            announcementOnly -> ChatType.Announcement
            bauakteId != null -> ChatType.Site
            crewId != null -> ChatType.Crew
            else -> ChatType.Group
        }
}

enum class ChatType(val title: String) {
    Direct("Einzelchat"), Group("Gruppe"), Crew("Kolonnengruppe"), Site("Baustellengruppe"), Announcement("Ankündigungen")
}

@Serializable
data class ChatConversationDetail(
    val conversation: ChatConversation, val members: List<ChatMemberState> = emptyList(), val canManage: Boolean = false,
    val pinned: List<ChatMessage> = emptyList(), val bauakteTitle: String? = null,
)

@Serializable
data class ChatPage(val items: List<ChatMessage> = emptyList(), val hasMore: Boolean = false, val members: List<ChatMemberState> = emptyList())

@Serializable
private data class SendMessageRequest(
    val text: String?, val clientMessageId: String, val replyToId: String? = null, val attachmentFileIds: List<String> = emptyList(),
    val mentionUserIds: List<String> = emptyList(),
)

@Serializable
private data class DirectChatRequest(val userId: String)

@Serializable
private data class EditMessageRequest(val text: String)

@Serializable
private data class ReactionRequest(val emoji: String?)

@Serializable
private data class MuteRequest(val until: String?, val muted: Boolean)

@Serializable
data class ChatSearchResult(val items: List<ChatSearchHit> = emptyList())

@Serializable
data class ChatSearchHit(val conversationId: String, val conversationTitle: String = "", val messageId: String? = null,
    val senderName: String? = null, val snippet: String? = null, val createdAt: String? = null)

/** Grenzen und Hilfen für das Schreiben. */
object ChatLimits {
    const val MAX_TEXT = 4000
    val quickReactions = listOf("👍", "❤️", "😂", "😮", "🙏", "✅")

    /** Nachricht sendbar: nicht leer und nicht zu lang. */
    fun canSend(text: String, attachments: Int = 0): Boolean = (text.isNotBlank() || attachments > 0) && text.trim().length <= MAX_TEXT

    /** Diktat an vorhandenen Text anhängen (mit Leerzeichen, Großschreibung am Satzanfang). */
    fun appendDictation(current: String, spoken: String): String {
        val words = spoken.trim()
        if (words.isEmpty()) return current
        if (current.isBlank()) return words.replaceFirstChar { it.uppercase() }
        val sentenceEnd = current.trimEnd().lastOrNull() in setOf('.', '!', '?')
        val addition = if (sentenceEnd) words.replaceFirstChar { it.uppercase() } else words
        return current.trimEnd() + " " + addition
    }
}

suspend fun EstrichOneApi.chatStatus(): ChatStatus = send("GET", "chat/status", null)
suspend fun EstrichOneApi.chatConversations(): List<ChatConversation> = send("GET", "chat/conversations", null)
suspend fun EstrichOneApi.chatConversation(id: String): ChatConversationDetail = send("GET", "chat/conversations/$id", null)
suspend fun EstrichOneApi.chatMessages(conversationId: String, before: String? = null, limit: Int = 50): ChatPage =
    send("GET", "chat/conversations/$conversationId/messages", null, query = mapOf("before" to before, "limit" to limit.toString()))

suspend fun EstrichOneApi.sendChatMessage(
    conversationId: String, text: String?, clientMessageId: String, replyToId: String? = null, attachmentIds: List<String> = emptyList(),
): ChatMessage = send(
    "POST", "chat/conversations/$conversationId/messages",
    SendMessageRequest(text?.trim()?.ifEmpty { null }, clientMessageId, replyToId, attachmentIds),
)

suspend fun EstrichOneApi.markChatRead(conversationId: String) = send<Unit>("POST", "chat/conversations/$conversationId/read", JsonObject(emptyMap()))
suspend fun EstrichOneApi.chatContacts(): List<ChatPerson> = send("GET", "chat/contacts", null)
suspend fun EstrichOneApi.startDirectChat(userId: String): ChatConversation = send("POST", "chat/conversations/direct", DirectChatRequest(userId))
suspend fun EstrichOneApi.editChatMessage(id: String, text: String): ChatMessage = send("PUT", "chat/messages/$id", EditMessageRequest(text.trim()))
suspend fun EstrichOneApi.deleteChatMessage(id: String) = send<Unit>("DELETE", "chat/messages/$id", null)
suspend fun EstrichOneApi.reactToChatMessage(id: String, emoji: String?): ChatMessage = send("PUT", "chat/messages/$id/reaction", ReactionRequest(emoji))
suspend fun EstrichOneApi.muteChat(conversationId: String, muted: Boolean) =
    send<Unit>("POST", "chat/conversations/$conversationId/mute", MuteRequest(null, muted))
suspend fun EstrichOneApi.searchChat(query: String): ChatSearchResult = send("GET", "chat/search", null, query = mapOf("q" to query))

/** Foto als Anhang hochladen; die ID geht mit der Nachricht mit. */
suspend fun EstrichOneApi.uploadChatAttachment(conversationId: String, bytes: ByteArray, fileName: String, contentType: String): ChatAttachment {
    val text = upload("POST", "chat/conversations/$conversationId/attachments", bytes, contentType, query = mapOf("fileName" to fileName))
    return EstrichOneApi.json.decodeFromString(ChatAttachment.serializer(), text)
}

suspend fun EstrichOneApi.chatAttachment(id: String, thumbnail: Boolean = false): ByteArray? =
    bytes("chat/attachments/$id/content", if (thumbnail) mapOf("variant" to "thumb") else emptyMap())
