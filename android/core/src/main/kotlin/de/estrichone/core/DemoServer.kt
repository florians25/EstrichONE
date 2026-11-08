package de.estrichone.core

import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import okhttp3.Interceptor
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Protocol
import okhttp3.Response
import okhttp3.ResponseBody.Companion.toResponseBody
import okio.Buffer
import java.time.Instant
import java.time.LocalDate

/**
 * Demomodus (wie LocalServer.swift auf iOS): Ein Server im App-Prozess beantwortet die API mit Beispieldaten. Nichts wird
 * gesendet. Die App nutzt denselben Client, dieselbe Synchronisation und dieselben Ansichten wie im Betrieb.
 * [content] liefert die Bilddaten der Beispielfotos (zeichnet die App).
 */
class DemoServer(private val content: (String) -> ByteArray? = { null }) : Interceptor {
    private val lock = Any()
    private val entities = LinkedHashMap<String, SyncChange>()
    private var version = 0
    private val uploaded = HashMap<String, ByteArray>()
    private val chat = DemoChat()
    private val notifications = DemoInbox()
    private val tickets = DemoTickets()

    /** Störungen vorführen (Menü „Demo“). */
    @Volatile var backupDown = false
    @Volatile var archiveDown = false
    @Volatile var maintenance = false

    init {
        DemoData.changes().forEach { entities["${it.entityType}/${it.id}"] = it }
    }

    /** OkHttp-Client, dessen Anfragen dieser Server beantwortet. */
    fun client(): OkHttpClient = OkHttpClient.Builder().addInterceptor(this).build()

    override fun intercept(chain: Interceptor.Chain): Response {
        val request = chain.request()
        val path = request.url.encodedPath.removePrefix("/api/v1/")
        val query = (0 until request.url.querySize).associate { request.url.queryParameterName(it) to request.url.queryParameterValue(it) }
        val raw = request.body?.let { body -> Buffer().also { body.writeTo(it) }.readByteArray() }
        val body = raw?.takeIf { request.body?.contentType()?.subtype == "json" && it.isNotEmpty() }
            ?.let { runCatching { EstrichOneApi.json.parseToJsonElement(String(it, Charsets.UTF_8)) as? JsonObject }.getOrNull() }
            ?: JsonObject(emptyMap())
        val (status, payload) = synchronized(lock) { route(request.method, path, query, body, raw) }
        val bytes: ByteArray
        val type: String
        when (payload) {
            is ByteArray -> { bytes = payload; type = if (path.endsWith(".pdf") || path.contains(DemoData.PDF)) "application/pdf" else "image/jpeg" }
            null -> { bytes = ByteArray(0); type = "application/json" }
            else -> { bytes = EstrichOneApi.json.encodeToString(JsonElement.serializer(), toJson(payload)).toByteArray(); type = "application/json" }
        }
        return Response.Builder().request(request).protocol(Protocol.HTTP_1_1).code(status).message("OK")
            .body(bytes.toResponseBody(type.toMediaType())).build()
    }

    private fun problem(status: Int, code: String, detail: String) = status to json("status" to status, "title" to "Fehler", "detail" to detail, "code" to code)

    private fun route(method: String, path: String, query: Map<String, String?>, body: JsonObject, raw: ByteArray?): Pair<Int, Any?> {
        val now = Instant.now()
        if (maintenance && !path.startsWith("system/notices") && !path.startsWith("server-info") && !path.startsWith("auth")) {
            return problem(503, "MAINTENANCE", "Update auf Version 1.1. EstrichONE ist in Kürze wieder verfügbar.")
        }
        val parts = path.split('/')
        return when {
            path == "server-info" -> 200 to json("product" to "EstrichONE", "serverName" to "Demo-Betrieb", "serverVersion" to "1.0.0",
                "apiVersions" to listOf("v1"), "minClientVersion" to "1.0.0", "authMethods" to listOf("password"), "setupRequired" to false)
            path == "auth/login" || path == "auth/refresh" -> 200 to json(
                "accessToken" to "demo-access", "accessTokenExpiresAt" to ApiDate.format(now.plusSeconds(900)), "refreshToken" to "demo-refresh",
                "refreshTokenExpiresAt" to ApiDate.format(now.plusSeconds(86_400 * 30)),
                "user" to mapOf("id" to DemoData.USER_ID, "displayName" to DemoData.USER_NAME, "userName" to "m.huber", "email" to "m.huber@example.com"),
                "mustChangePassword" to false)
            path == "auth/logout" || path == "auth/change-password" -> 204 to null
            path.startsWith("devices/") -> 200 to json("id" to "0193a1b2-de00-7000-8000-0000000dddd1", "name" to "Telefon von Max", "kind" to "personal",
                "status" to "active", "model" to "Pixel", "osVersion" to "16", "appVersion" to "1.0.0")
            path == "auth/sessions" -> 200 to listOf(
                mapOf("id" to "s1", "client" to "android", "clientName" to "Telefon von Max", "deviceBound" to true, "createdAt" to ApiDate.format(now.minusSeconds(86_400 * 3)),
                    "lastSeenAt" to ApiDate.format(now), "current" to true),
                mapOf("id" to "s2", "client" to "web", "clientName" to "Chrome unter Windows", "deviceBound" to false,
                    "createdAt" to ApiDate.format(now.minusSeconds(86_400)), "lastSeenAt" to ApiDate.format(now.minusSeconds(7200)), "current" to false))
            path == "auth/two-factor" -> 200 to json("enabled" to false, "recoveryCodesRemaining" to 0, "requiredByPolicy" to false)
            path == "access/me/permissions" -> 200 to listOf("bauakten.read", "bauakten.create", "bauakten.edit", "notes.write", "files.upload", "work.edit",
                "contacts.read", "contacts.edit", "chat.use")
            path == "sync/push" -> 200 to push(body)
            path == "sync/pull" -> 200 to json("changes" to entities.values.map { EstrichOneApi.json.encodeToJsonElement(SyncChange.serializer(), it) },
                "cursor" to "demo-$version", "hasMore" to false,
                "visibleBauakteIds" to entities.values.filter { it.entityType == EntityTypes.BAUAKTE && !it.deleted }.map { it.id })
            path == "sync/snapshot" -> {
                val ids = body.strings("bauakteIds").map { it.lowercase() }.toSet()
                200 to json("changes" to entities.values.filter { c -> c.id in ids || (c.data as? JsonObject)?.str("bauakteId") in ids }
                    .map { EstrichOneApi.json.encodeToJsonElement(SyncChange.serializer(), it) }, "missing" to emptyList<String>())
            }
            parts.size == 3 && parts[0] == "files" && parts[2] == "upload-status" -> 200 to json("id" to parts[1], "receivedBytes" to (uploaded[parts[1]]?.size ?: 0),
                "sizeBytes" to 0, "state" to if (uploaded.containsKey(parts[1])) "stored" else "pending")
            parts.size == 3 && parts[0] == "files" && parts[2] == "content" && method == "PUT" -> {
                uploaded[parts[1]] = raw ?: ByteArray(0)
                entities["file/${parts[1]}"]?.let { c ->
                    entities["file/${parts[1]}"] = c.copy(data = (c.data as JsonObject).merged(mapOf("state" to JsonPrimitive("stored"))))
                }
                200 to json("id" to parts[1], "state" to "stored")
            }
            parts.size == 3 && parts[0] == "files" && parts[2] == "content" -> (uploaded[parts[1]] ?: content(parts[1]))?.let { 200 to it }
                ?: problem(404, "NOT_FOUND", "Datei nicht gefunden")
            path.startsWith("bauakten/") && path.endsWith("/members/me") -> 200 to emptyList<String>()
            path.startsWith("bauakten/") && path.endsWith(".pdf") -> content(DemoData.PDF)?.let { 200 to it } ?: problem(404, "NOT_FOUND", "Nicht gefunden")
            path == "planning/mine" -> 200 to DemoData.myPlanning(LocalDate.parse(query["from"] ?: LocalDate.now(Dates.zone).toString()))
            path.startsWith("planning/dispatch/") -> 200 to DemoData.dispatch(parts.last())
            path == "system/notices" -> 200 to if (maintenance) json("announcements" to emptyList<String>(), "maintenance" to mapOf("active" to true,
                "message" to "Update auf Version 1.1. EstrichONE ist in Kürze wieder verfügbar.", "startsAt" to ApiDate.format(now.minusSeconds(120)),
                "endsAt" to ApiDate.format(now.plusSeconds(20 * 60)), "adminsMayWork" to true), "serverTime" to ApiDate.format(now)) else DemoData.notices(now)
            path == "system/health-banner" -> 200 to health(now)
            path == "notifications" -> 200 to notifications.inbox(now)
            path == "notifications/read-all" -> 200 to json("count" to notifications.readAll())
            path == "notifications/preferences" -> 200 to if (method == "PUT") notifications.save(body) else notifications.preferences()
            parts.size == 3 && parts[0] == "notifications" && parts[2] == "read" -> { notifications.markRead(parts[1]); 204 to null }
            parts.size == 3 && parts[0] == "notifications" && parts[2] == "unread" -> { notifications.markRead(parts[1], read = false); 204 to null }
            parts.size == 2 && parts[0] == "notifications" && method == "DELETE" -> { notifications.delete(parts[1]); 204 to null }
            path.startsWith("notifications/devices") -> 204 to null
            path.startsWith("chat/") -> chat.handle(method, path.removePrefix("chat/"), query, body, raw, content)
            path == "tickets/mine" -> 200 to tickets.list()
            path == "tickets" && method == "POST" -> 200 to tickets.create(body)
            path == "tickets/device" -> 200 to json("number" to "T-2026-0031")
            parts.size >= 2 && parts[0] == "tickets" -> tickets.handle(method, parts, body)
            path == "directory/users" -> 200 to listOf(mapOf("id" to DemoData.USER_ID, "displayName" to DemoData.USER_NAME, "isActive" to true))
            else -> problem(404, "NOT_FOUND", "Im Demomodus nicht verfügbar.")
        }
    }

    private fun health(now: Instant): JsonObject {
        val banners = mutableListOf<Map<String, Any?>>()
        if (backupDown) banners += mapOf("key" to "backup", "severity" to "critical",
            "text" to "ACHTUNG: Backup-Server ausgefallen oder nicht erreichbar (Letztes Backup ${Dates.dateTime(now.minusSeconds(31 * 3600))} Uhr). Bitte geben Sie umgehend Ihrem Admin Bescheid.")
        if (archiveDown) banners += mapOf("key" to "archive", "severity" to "warning",
            "text" to "Hinweis: Archiv-Server ausgefallen oder nicht erreichbar (Letzte Archivierung ${Dates.dateTime(now.minusSeconds(9 * 86_400))} Uhr). Bitte informieren Sie Ihren Admin.")
        return json("backup" to mapOf("configured" to true, "reachable" to !backupDown), "archive" to mapOf("configured" to true, "reachable" to !archiveDown),
            "banners" to banners, "checkedAt" to ApiDate.format(now))
    }

    /** Operationen übernehmen wie der Server: Datensatz anlegen bzw. Felder ändern, Bauakten bekommen eine Nummer. */
    private fun push(body: JsonObject): JsonObject {
        val results = (body.arr("operations") ?: JsonArray(emptyList())).mapNotNull { it as? JsonObject }.map { op ->
            val type = op.str("type") ?: ""
            val entityType = EntityTypes.fromOperation(type) ?: return@map json("opId" to op.str("opId"), "status" to "rejected", "code" to "UNKNOWN_OPERATION")
            val id = (op.str("entityId") ?: "").lowercase()
            val key = "$entityType/$id"
            val payload = op.obj("payload") ?: JsonObject(emptyMap())
            version++
            val existing = entities[key]?.data as? JsonObject
            val action = type.substringAfter('.')
            val entity: JsonObject? = when {
                action == "delete" -> { entities[key] = SyncChange(entityType, id, true, null); null }
                action == "create" || action == "announce" -> {
                    var data = payload.merged(mapOf("id" to JsonPrimitive(id), "version" to JsonPrimitive(1)))
                    if (entityType == EntityTypes.BAUAKTE) {
                        val count = entities.values.count { it.entityType == EntityTypes.BAUAKTE }
                        data = data.merged(mapOf("number" to JsonPrimitive("2026-%03d".format(48 + count - 6)), "status" to JsonPrimitive("planned")))
                    }
                    if (entityType == EntityTypes.NOTE) data = data.merged(mapOf("isPinned" to (payload["pinned"] ?: JsonPrimitive(false))))
                    if (entityType == EntityTypes.FILE) data = data.merged(mapOf("state" to JsonPrimitive("pending")))
                    data
                }
                action == "setStatus" -> (existing ?: JsonObject(emptyMap())).merged(mapOf("status" to (payload["status"] ?: JsonNull)))
                action == "update" && entityType == EntityTypes.NOTE -> (existing ?: JsonObject(emptyMap())).merged(
                    payload.mapKeys { if (it.key == "pinned") "isPinned" else it.key })
                else -> (existing ?: JsonObject(emptyMap())).merged(payload.obj("changes") ?: JsonObject(emptyMap()))
            }
            if (entity != null) {
                val withVersion = entity.merged(mapOf("version" to JsonPrimitive((existing?.long("version") ?: 0) + 1)))
                entities[key] = SyncChange(entityType, id, false, withVersion)
                json("opId" to op.str("opId"), "status" to "applied", "version" to withVersion.long("version"), "entity" to withVersion)
            } else {
                json("opId" to op.str("opId"), "status" to "applied")
            }
        }
        return json("results" to results)
    }
}

/** Postfach im Demomodus (wie DemoNotifications.swift). */
private class DemoInbox {
    private data class Item(val id: String, val category: String, val urgency: String, val title: String, val body: String, val link: String?,
        val minutesAgo: Long, var read: Boolean, val objectType: String? = null, val sender: String? = null)

    /** Wie der Server sie schickt (Kategorien und Links aus backend/src, gleich wie DemoNotifications.swift). */
    private val items = mutableListOf(
        Item("0193a1b2-de00-7000-8000-00000000a001", "planning", "high", "Plan für morgen freigegeben",
            "Kolonne Huber: 07:30 Praxis Dr. Maier, 10:30 EFH Schneider.", "/planung/wer-faehrt-wohin", 12, false),
        Item("0193a1b2-de00-7000-8000-00000000a010", "chat", "normal", "Neue Nachricht von Sabine Wagner", "Büro · 2 neue Nachrichten",
            "/chat/${DemoChat.OFFICE}", 14, false),
        Item("0193a1b2-de00-7000-8000-00000000a011", "work", "high", "Mangel zugewiesen", "Riss im Flur prüfen · 2026-038 EFH Schneider",
            "/bauakten/${DemoData.EFH}", 25, false, objectType = "workItem"),
        Item("0193a1b2-de00-7000-8000-00000000a002", "ticket", "normal", "Antwort zum Ticket", "T-2026-0007 · Bitte die App im WLAN öffnen und den Abgleich abwarten.",
            "/it-center/helpdesk/${DemoData.TICKET}", 47, false),
        Item("0193a1b2-de00-7000-8000-00000000a012", "bauakte", "normal", "Neue Notiz von Sabine Wagner", "2026-038 EFH Schneider · Fotos der Dämmung vor dem Einbau",
            "/bauakten/${DemoData.EFH}", 70, false),
        Item("0193a1b2-de00-7000-8000-00000000a013", "approval", "normal", "Dokument zur Genehmigung", "Aufmaß Haus B · 2026-044 MFH Isarpark",
            "/bauakten/${DemoData.ISARPARK}", 110, true),
        Item("0193a1b2-de00-7000-8000-00000000a003", "message", "normal", "Pumpe 2 ab Montag später",
            "Ab Montag Abholung der Pumpe 2 erst ab 6:45 Uhr am Lager. Bitte die Touren entsprechend planen und Material am Vortag laden.",
            null, 180, false, sender = "Sabine Wagner"),
        Item("0193a1b2-de00-7000-8000-00000000a014", "bauakte", "normal", "Abnahme unterschrieben", "Kindergarten Sonnenschein · ohne Mängel",
            "/bauakten/${DemoData.KITA}", 300, true),
        Item("0193a1b2-de00-7000-8000-00000000a015", "absence", "low", "Urlaubsantrag genehmigt", "Mo 16.11. bis Fr 20.11. · 5 Tage", "/personal/urlaub", 60 * 26, true),
        Item("0193a1b2-de00-7000-8000-00000000a004", "bauakte", "normal", "Bauakte zugewiesen", "2026-044 · MFH Isarpark, Haus B",
            "/bauakten/${DemoData.ISARPARK}", 60 * 28, true),
        Item("0193a1b2-de00-7000-8000-00000000a016", "system", "normal", "Wartung angekündigt", "Samstag 6:00 bis 7:30 Uhr · EstrichONE nicht erreichbar",
            null, 60 * 50, true),
        Item("0193a1b2-de00-7000-8000-00000000a005", "deadline", "low", "Frist endet morgen", "CM-Messung Behandlungsraum 2 · Praxis Dr. Maier",
            "/bauakten/${DemoData.PRAXIS}", 60 * 75, true),
        Item("0193a1b2-de00-7000-8000-00000000a017", "planning", "normal", "Planänderung für Donnerstag", "Halle Weber: Beginn 8:00 statt 7:00 Uhr.",
            "/planung/wer-faehrt-wohin", 60 * 24 * 8, true),
        Item("0193a1b2-de00-7000-8000-00000000a018", "ticket", "low", "Ticket erledigt", "T-2026-0004 · Tablet-Halterung in Fahrzeug 3 getauscht",
            "/it-center/helpdesk/${DemoData.TICKET}", 60 * 24 * 12, true),
    )

    private val preferences = mutableListOf(
        Triple("bauakte.assigned", "Bauakten", "Bauakte zugewiesen"), Triple("bauakte.comment", "Bauakten", "Neue Notiz in meiner Bauakte"),
        Triple("acceptance.signed", "Bauakten", "Abnahme unterschrieben"), Triple("work.assigned", "Aufgaben", "Aufgabe oder Mangel zugewiesen"),
        Triple("deadline.due", "Aufgaben", "Fristen"), Triple("planning.released", "Planung", "Plan freigegeben"),
        Triple("planning.changed", "Planung", "Planänderung für mich"), Triple("chat.message", "Chat", "Neue Chatnachricht"),
        Triple("office.message", "System", "Nachricht aus dem Büro"),
    ).map { (event, group, label) -> mutableMapOf<String, Any?>("event" to event, "group" to group, "label" to label, "description" to "",
        "adminOnly" to false, "mandatory" to (event == "office.message"), "delivery" to "push") }

    fun inbox(now: Instant) = json(
        "items" to items.map {
            mapOf("id" to it.id, "category" to it.category, "urgency" to it.urgency, "title" to it.title, "body" to it.body, "link" to it.link,
                "objectType" to it.objectType, "objectId" to null, "senderName" to it.sender, "createdAt" to ApiDate.format(now.minusSeconds(it.minutesAgo * 60)),
                "readAt" to if (it.read) ApiDate.format(now.minusSeconds(it.minutesAgo * 60 - 300)) else null)
        },
        "unreadCount" to items.count { !it.read },
    )

    fun markRead(id: String, read: Boolean = true) { items.firstOrNull { it.id == id }?.read = read }
    fun delete(id: String) { items.removeAll { it.id == id } }
    fun readAll(): Int { items.forEach { it.read = true }; return 0 }
    fun preferences() = json("items" to preferences)
    fun save(body: JsonObject): JsonObject {
        body.arr("items")?.mapNotNull { it as? JsonObject }?.forEach { change ->
            preferences.firstOrNull { it["event"] == change.str("event") }?.let { if (it["mandatory"] != true || change.str("delivery") != "off") it["delivery"] = change.str("delivery") }
        }
        return preferences()
    }
}

/** Meldungen an die IT im Demomodus (wie DemoHelpdesk.swift). */
private class DemoTickets {
    private data class Ticket(val id: String, val number: String, val title: String, val description: String?, val category: String, var status: String,
        val minutesAgo: Long, val comments: MutableList<Triple<String, String, Long>>)

    private var counter = 0
    private val tickets = mutableListOf(
        Ticket(DemoData.TICKET, "T-2026-0007", "Fotos werden nicht übertragen", "Seit gestern bleiben Fotos auf „Wird übertragen“ stehen.", "sync",
            "waitingForReporter", 60 * 26, mutableListOf(
                Triple("IT-Support", "Danke für die Meldung. Welche App-Version ist installiert? Sie steht unter Mehr > Info.", 60L * 24),
                Triple(DemoData.USER_NAME, "Version 1.0.0, Pixel 8. Betroffen sind nur Fotos von heute früh.", 60L * 23),
                Triple("IT-Support", "Bitte einmal die App öffnen und im WLAN warten, bis die Synchronisation fertig ist. Klappt es dann?", 35L),
            )),
        Ticket("0193a1b2-de00-7000-8000-00000000c004", "T-2026-0004", "Zugang für neuen Mitarbeiter", "Jonas Wirth braucht einen Zugang für die App.", "access",
            "resolved", 60 * 24 * 6, mutableListOf(Triple("IT-Support", "Zugang ist eingerichtet, Einladung ging per E-Mail raus.", 60L * 24 * 5))),
    )

    private fun summary(t: Ticket, now: Instant = Instant.now()) = mapOf(
        "id" to t.id, "number" to t.number, "title" to t.title, "category" to t.category, "status" to t.status, "reporterBlocked" to false,
        "assigneeName" to "IT-Support", "createdAt" to ApiDate.format(now.minusSeconds(t.minutesAgo * 60)),
        "lastActivityAt" to ApiDate.format(now.minusSeconds((t.comments.lastOrNull()?.third ?: t.minutesAgo) * 60)),
        "hasUnread" to (t.status == "waitingForReporter"),
    )

    private fun detail(t: Ticket): JsonObject {
        val now = Instant.now()
        return json("summary" to summary(t, now), "description" to t.description, "resolution" to null, "attachmentFileIds" to emptyList<String>(),
            "comments" to t.comments.mapIndexed { i, (author, text, minutes) ->
                mapOf("id" to "${t.id}-$i", "authorName" to author, "authorUserId" to if (author == DemoData.USER_NAME) DemoData.USER_ID else null,
                    "text" to text, "isInternal" to false, "createdAt" to ApiDate.format(now.minusSeconds(minutes * 60)))
            })
    }

    fun list() = tickets.map { summary(it) }

    fun create(body: JsonObject): JsonObject {
        counter++
        val t = Ticket(body.str("id") ?: Ids.v7(), "T-2026-%04d".format(20 + counter), body.str("title") ?: "Meldung", body.str("description"),
            body.str("category") ?: "other", "new", 0, mutableListOf())
        tickets.add(0, t)
        return detail(t)
    }

    fun handle(method: String, parts: List<String>, body: JsonObject): Pair<Int, Any?> {
        val t = tickets.firstOrNull { it.id == parts[1] } ?: return 404 to json("code" to "NOT_FOUND", "detail" to "Nicht gefunden")
        return when (parts.getOrNull(2)) {
            "comments" -> { t.comments.add(Triple(DemoData.USER_NAME, body.str("text") ?: "", 0L)); t.status = "inProgress"; 200 to detail(t) }
            "reopen" -> { t.status = "inProgress"; 200 to detail(t) }
            "seen" -> 204 to null
            else -> 200 to detail(t)
        }
    }
}

/** Chat im Demomodus: Baustellen-, Kolonnen- und Ankündigungsgruppe sowie Einzelchats (wie DemoChat.swift). */
class DemoChat {
    companion object {
        const val SITE = "0193a1b2-de00-7000-8000-00000000d005"
        const val CREW = "0193a1b2-de00-7000-8000-00000000d002"
        const val ALL = "0193a1b2-de00-7000-8000-00000000d006"
        const val OFFICE = "0193a1b2-de00-7000-8000-00000000d001"
        const val COLLEAGUE = "0193a1b2-de00-7000-8000-00000000d003"
        private const val SABINE = "0193a1b2-de00-7000-8000-00000000a0a2"
        private const val LUKAS = "0193a1b2-0000-7000-8000-00000000bbbb"
        private const val ANNA = "0193a1b2-de00-7000-8000-00000000a0a4"
        private const val JONAS = "0193a1b2-de00-7000-8000-00000000a0a5"
    }

    private val names = mapOf(DemoData.USER_ID to DemoData.USER_NAME, SABINE to "Sabine Wagner", LUKAS to "Lukas Brandl", ANNA to "Anna Lechner", JONAS to "Jonas Wirth")

    private data class Conv(val id: String, val title: String, val kind: String, val members: List<String>, var unread: Int,
        val bauakteId: String? = null, val crewId: String? = null, val announcementOnly: Boolean = false, val description: String? = null, var muted: Boolean = false)

    private data class Msg(val id: String, val conversationId: String, val sender: String, var text: String?, val minutesAgo: Double,
        var attachments: List<Map<String, Any?>> = emptyList(), var reactions: Map<String, String> = emptyMap(), var replyTo: Map<String, Any?>? = null,
        var deleted: Boolean = false, var edited: Boolean = false, val created: Instant = Instant.now().minusSeconds((minutesAgo * 60).toLong()),
        var location: Map<String, Any?>? = null, val clientMessageId: String? = null)

    private val conversations = mutableListOf(
        Conv(SITE, "Praxis Dr. Maier, Sanierung", "group", listOf(DemoData.USER_ID, SABINE, LUKAS, ANNA), 1, bauakteId = DemoData.PRAXIS,
            description = "Baustellengruppe der Bauakte 2026-038"),
        Conv(OFFICE, "Sabine Wagner (Büro)", "direct", listOf(DemoData.USER_ID, SABINE), 1),
        Conv(CREW, "Kolonne Huber", "group", listOf(DemoData.USER_ID, LUKAS, SABINE), 1, crewId = "0193a1b2-de00-7000-8000-0000000e0001",
            description = "Alles rund um Einsätze, Material und Fahrzeuge der Kolonne."),
        Conv(ALL, "Alle Mitarbeiter", "group", listOf(DemoData.USER_ID, SABINE, LUKAS, ANNA, JONAS), 0, announcementOnly = true,
            description = "Ankündigungen der Geschäftsleitung. Nur Admins schreiben."),
        Conv(COLLEAGUE, "Lukas Brandl", "direct", listOf(DemoData.USER_ID, LUKAS), 0),
    )

    private fun photo(id: String) = mapOf("id" to id, "kind" to "photo", "fileName" to "Foto.jpg", "contentType" to "image/jpeg", "sizeBytes" to 412_000,
        "width" to 1600, "height" to 1200, "hasThumbnail" to true)

    private val messages = mutableListOf(
        Msg("0193a1b2-de00-7000-8000-0000000e0101", SITE, ANNA, "Guten Morgen zusammen! Schlüssel liegt ab 6:45 beim Empfang, Zufahrt über den Hof.", 60 * 20.0),
        Msg("0193a1b2-de00-7000-8000-0000000e0102", SITE, SABINE, "Lieferschein vom Fließestrich für heute.", 60 * 19.5,
            attachments = listOf(mapOf("id" to DemoData.PDF, "kind" to "document", "fileName" to "Lieferschein 4711.pdf", "contentType" to "application/pdf",
                "sizeBytes" to 184_320, "hasThumbnail" to false))),
        Msg("0193a1b2-de00-7000-8000-0000000e0103", SITE, DemoData.USER_ID, "Behandlungsraum 1 ist verlegt.", 95.0,
            attachments = listOf(photo("0193a1b2-de00-7000-8000-00000000a007"), photo("0193a1b2-de00-7000-8000-00000000a003")),
            reactions = mapOf(ANNA to "👍", SABINE to "👍")),
        Msg("0193a1b2-de00-7000-8000-0000000e0104", SITE, ANNA, "Sieht sauber aus 👍 Bitte Raum 2 genauso, Randstreifen erst nach dem Abziehen kürzen.", 80.0,
            replyTo = mapOf("id" to "0193a1b2-de00-7000-8000-0000000e0103", "senderName" to DemoData.USER_NAME, "text" to "Behandlungsraum 1 ist verlegt.",
                "deleted" to false, "attachment" to "photo"), edited = true),
        Msg("0193a1b2-de00-7000-8000-0000000e0105", SITE, LUKAS, "Ich stehe mit dem Silo hier:", 40.0,
            location = mapOf("latitude" to 48.5361, "longitude" to 12.1498, "label" to "Silo am Hof")),
        Msg("0193a1b2-de00-7000-8000-0000000e0106", SITE, ANNA, "@Max Huber kannst du heute noch die CM-Messung in Raum 2 eintragen?", 12.0),
        Msg("0193a1b2-de00-7000-8000-0000000e0201", CREW, LUKAS, "Pumpe 2 ist getankt und steht am Lager.", 240.0),
        Msg("0193a1b2-de00-7000-8000-0000000e0202", CREW, DemoData.USER_ID, "Super, ich hole sie um 6:45 ab.", 236.0, reactions = mapOf(LUKAS to "🙏")),
        Msg("0193a1b2-de00-7000-8000-0000000e0203", CREW, SABINE, "Morgen bitte zwei Sack Randdämmstreifen extra mitnehmen.", 32.0),
        Msg("0193a1b2-de00-7000-8000-0000000e0301", ALL, SABINE, "Betriebsversammlung am Freitag um 14 Uhr in der Halle. Danach gibt es Leberkäs 🙂", 60 * 26.0,
            reactions = mapOf(LUKAS to "👍", JONAS to "😂", DemoData.USER_ID to "👍")),
        Msg("0193a1b2-de00-7000-8000-0000000e0001", OFFICE, DemoData.USER_ID, "Guten Morgen, wir sind um 7:20 an der Praxis Dr. Maier.", 95.0),
        Msg("0193a1b2-de00-7000-8000-0000000e0002", OFFICE, SABINE, "Danke! Der Hausmeister ist ab 7 Uhr da.", 88.0),
        Msg("0193a1b2-de00-7000-8000-0000000e0003", OFFICE, SABINE, "Kurze Info: Die Rechnung für den Kran ist da, liegt im Büro.", 14.0),
        Msg("0193a1b2-de00-7000-8000-0000000e0007", COLLEAGUE, LUKAS, "Hast du den Laser noch im Wagen?", 60 * 26.0),
        Msg("0193a1b2-de00-7000-8000-0000000e0008", COLLEAGUE, DemoData.USER_ID, "Ja, bringe ich morgen mit.", 60 * 25.5),
    )
    private val uploads = HashMap<String, Pair<ByteArray, Map<String, Any?>>>()

    private fun message(m: Msg): Map<String, Any?> {
        val conv = conversations.first { it.id == m.conversationId }
        val recipients = conv.members.size - 1
        return mapOf(
            "id" to m.id, "conversationId" to m.conversationId, "senderUserId" to m.sender, "senderName" to (names[m.sender] ?: ""),
            "text" to if (m.deleted) null else m.text, "createdAt" to ApiDate.format(m.created), "mine" to (m.sender == DemoData.USER_ID), "deleted" to m.deleted,
            "status" to "sent", "readBy" to recipients, "recipients" to recipients, "clientMessageId" to m.clientMessageId, "replyTo" to m.replyTo,
            "attachments" to m.attachments,
            "reactions" to m.reactions.entries.groupBy { it.value }.map { (emoji, list) ->
                mapOf("emoji" to emoji, "count" to list.size, "mine" to list.any { it.key == DemoData.USER_ID }, "names" to list.map { names[it.key] })
            },
            "editedAt" to if (m.edited) ApiDate.format(m.created.plusSeconds(120)) else null, "forwarded" to false, "pinnedAt" to null, "location" to m.location,
            "canEdit" to (m.sender == DemoData.USER_ID),
        )
    }

    private fun summary(c: Conv): Map<String, Any?> {
        val last = messages.filter { it.conversationId == c.id }.maxByOrNull { it.created }
        return mapOf(
            "id" to c.id, "kind" to c.kind, "title" to c.title, "crewId" to c.crewId, "members" to c.members.map { mapOf("id" to it, "name" to names[it]) },
            "lastMessage" to last?.let(::message), "lastActivityAt" to last?.let { ApiDate.format(it.created) }, "unreadCount" to c.unread,
            "muted" to c.muted, "partnerUserId" to c.members.firstOrNull { it != DemoData.USER_ID }.takeIf { c.kind == "direct" },
            "description" to c.description, "bauakteId" to c.bauakteId, "announcementOnly" to c.announcementOnly, "isAdmin" to false,
            "canPost" to !c.announcementOnly, "unreadMentions" to if (c.id == SITE) 1 else 0,
        )
    }

    fun handle(method: String, path: String, query: Map<String, String?>, body: JsonObject, raw: ByteArray?, content: (String) -> ByteArray?): Pair<Int, Any?> {
        val parts = path.split('/')
        return when {
            path == "status" -> 200 to json("enabled" to true, "canUse" to true, "canAdmin" to false, "unreadCount" to conversations.sumOf { it.unread },
                "unreadConversations" to conversations.count { it.unread > 0 }, "retentionDays" to 365, "showPreview" to true, "privacyAccepted" to true,
                "editWindowMinutes" to 15, "attachmentsEnabled" to true, "readReceipts" to true)
            path == "contacts" -> 200 to names.filterKeys { it != DemoData.USER_ID }.map { mapOf("id" to it.key, "name" to it.value) }
            path == "conversations" -> 200 to conversations.sortedByDescending { c -> messages.filter { it.conversationId == c.id }.maxOfOrNull { it.created } }.map(::summary)
            path == "conversations/direct" -> {
                val partner = body.str("userId") ?: ""
                val existing = conversations.firstOrNull { it.kind == "direct" && partner in it.members }
                val conv = existing ?: Conv(Ids.v7(), names[partner] ?: "Unterhaltung", "direct", listOf(DemoData.USER_ID, partner), 0).also { conversations.add(it) }
                200 to summary(conv)
            }
            path == "search" -> {
                val q = (query["q"] ?: "").lowercase()
                200 to json("items" to messages.filter { !it.deleted && (it.text ?: "").lowercase().contains(q) }.map { m ->
                    mapOf("conversationId" to m.conversationId, "conversationTitle" to conversations.first { it.id == m.conversationId }.title,
                        "messageId" to m.id, "senderName" to names[m.sender], "snippet" to m.text, "createdAt" to ApiDate.format(m.created))
                })
            }
            parts.size == 3 && parts[0] == "attachments" && parts[2] == "content" -> (uploads[parts[1]]?.first ?: content(parts[1]))?.let { 200 to it }
                ?: (404 to json("code" to "NOT_FOUND"))
            parts.size >= 2 && parts[0] == "conversations" -> {
                val conv = conversations.firstOrNull { it.id == parts[1] } ?: return 404 to json("code" to "NOT_FOUND", "detail" to "Unterhaltung nicht gefunden")
                when (parts.getOrNull(2)) {
                    null -> 200 to json("conversation" to summary(conv), "canManage" to false, "pinned" to emptyList<String>(),
                        "bauakteTitle" to if (conv.bauakteId != null) conv.title else null,
                        "members" to conv.members.map { mapOf("userId" to it, "name" to names[it], "isAdmin" to (it == SABINE), "fromCrew" to (conv.crewId != null)) })
                    "messages" -> if (method == "POST") {
                        val m = Msg(Ids.v7(), conv.id, DemoData.USER_ID, body.str("text"), 0.0, clientMessageId = body.str("clientMessageId"),
                            attachments = body.strings("attachmentFileIds").mapNotNull { uploads[it]?.second },
                            replyTo = body.str("replyToId")?.let { rid -> messages.firstOrNull { it.id == rid }?.let { r ->
                                mapOf("id" to r.id, "senderName" to names[r.sender], "text" to r.text, "deleted" to r.deleted) } })
                        messages.add(m)
                        200 to message(m)
                    } else {
                        200 to json("items" to messages.filter { it.conversationId == conv.id }.sortedBy { it.created }.map(::message), "hasMore" to false,
                            "members" to conv.members.map { mapOf("userId" to it, "name" to names[it]) })
                    }
                    "read" -> { conv.unread = 0; 204 to null }
                    "mute" -> { conv.muted = body.bool("muted") ?: false; 204 to null }
                    "attachments" -> {
                        val id = Ids.v7()
                        val meta = mapOf("id" to id, "kind" to "photo", "fileName" to (query["fileName"] ?: "Foto.jpg"), "contentType" to "image/jpeg",
                            "sizeBytes" to (raw?.size ?: 0), "hasThumbnail" to true)
                        uploads[id] = (raw ?: ByteArray(0)) to meta
                        200 to meta
                    }
                    else -> 204 to null
                }
            }
            parts.size >= 2 && parts[0] == "messages" -> {
                val m = messages.firstOrNull { it.id == parts[1] } ?: return 404 to json("code" to "NOT_FOUND")
                when {
                    parts.getOrNull(2) == "reaction" -> {
                        val emoji = body.str("emoji")
                        m.reactions = if (emoji == null || m.reactions[DemoData.USER_ID] == emoji) m.reactions - DemoData.USER_ID else m.reactions + (DemoData.USER_ID to emoji)
                        200 to message(m)
                    }
                    method == "DELETE" -> { m.deleted = true; 204 to null }
                    method == "PUT" -> { m.text = body.str("text"); m.edited = true; 200 to message(m) }
                    else -> 200 to message(m)
                }
            }
            else -> 204 to null
        }
    }
}
