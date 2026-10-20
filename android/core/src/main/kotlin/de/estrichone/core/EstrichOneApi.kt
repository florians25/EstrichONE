package de.estrichone.core

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import kotlinx.serialization.KSerializer
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import kotlinx.serialization.serializer
import okhttp3.HttpUrl
import okhttp3.HttpUrl.Companion.toHttpUrl
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import java.io.IOException
import java.util.UUID
import java.util.concurrent.TimeUnit

/** Fehler der API mit verständlicher Meldung und stabilem [code]. */
class ApiException(val status: Int, val code: String?, message: String) : Exception(message)

/** Dauerhafte Ablage der Anmeldung (Android: verschlüsselt, Schlüssel im Keystore). */
interface SessionStore {
    var refreshToken: String?
    var deviceId: String?
    var user: UserSummary?
}

class MemorySessionStore : SessionStore {
    override var refreshToken: String? = null
    override var deviceId: String? = null
    override var user: UserSummary? = null
}

@Serializable
private data class LoginRequest(val login: String, val password: String, val client: String, val clientName: String?)

@Serializable
private data class RefreshRequest(val refreshToken: String, val deviceSignature: String?)

@Serializable
private data class RegisterDeviceRequest(
    val name: String, val platform: String, val kind: DeviceKind, val publicKey: String, val model: String?, val osVersion: String?,
    val appVersion: String?, val enrollmentCode: String?,
)

@Serializable
private data class AttachDeviceRequest(val deviceId: String, val signature: String)

@Serializable
private data class NoteRequest(val id: String, val text: String)

@Serializable
private data class WorkItemRequest(val id: String, val bauakteId: String, val kind: WorkItemKind, val title: String)

@Serializable
private data class WorkStatusRequest(val status: WorkItemStatus)

@Serializable
private data class TicketRequest(
    val id: String, val title: String, val description: String?, val category: String, val source: String, val reporterBlocked: Boolean,
    val diagnostics: Map<String, String>,
)

/** Angaben zum Telefon für die Geräteregistrierung. */
data class DeviceDescription(val name: String, val model: String?, val osVersion: String?, val appVersion: String?)

/**
 * Client der EstrichONE-API v1 für Android: Anmeldung als `android`, Bindung der Sitzung an das Gerät
 * (Registrierung bzw. Nachweis per Gerätesignatur), Erneuern mit Signatur. Ein abgelaufenes Zugriffstoken wird
 * einmal erneuert und die Anfrage wiederholt – auch bei parallelen Anfragen nur ein Refresh.
 */
class EstrichOneApi(
    serverUrl: String,
    private val store: SessionStore,
    private val keys: DeviceKeys,
    private val http: OkHttpClient = OkHttpClient.Builder().connectTimeout(15, TimeUnit.SECONDS).readTimeout(60, TimeUnit.SECONDS).build(),
) {
    val baseUrl: HttpUrl = normalize(serverUrl)
    private val api: HttpUrl = baseUrl.newBuilder().addPathSegments("api/v1/").build()
    private val refreshLock = Mutex()

    @Volatile
    private var accessToken: String? = null

    val user: UserSummary? get() = store.user

    val isSignedIn: Boolean get() = store.refreshToken != null

    // Server und Anmeldung

    suspend fun serverInfo(): ServerInfo = send("GET", "server-info", null, authenticated = false)

    /** Anmeldung mit Passwort; danach muss die Sitzung mit [bindDevice] an dieses Telefon gebunden werden. */
    suspend fun signIn(login: String, password: String, clientName: String): TokenResponse {
        val tokens: TokenResponse = send("POST", "auth/login", LoginRequest(login.trim(), password, "android", clientName), authenticated = false)
        accessToken = tokens.accessToken
        store.refreshToken = tokens.refreshToken
        store.user = tokens.user
        return tokens
    }

    /** Erstregistrierung (mit optionalem Einrichtungscode) oder Nachweis eines bereits registrierten Geräts. */
    suspend fun bindDevice(kind: DeviceKind, enrollmentCode: String?, description: DeviceDescription): DeviceResponse {
        val token = accessToken ?: throw ApiException(401, "UNAUTHORIZED", "Bitte zuerst anmelden.")
        val device: DeviceResponse = store.deviceId?.let { id ->
            send("POST", "devices/attach", AttachDeviceRequest(id, DeviceProof.attach(token, keys)))
        } ?: send(
            "POST", "devices/register",
            RegisterDeviceRequest(
                description.name, "android", kind, java.util.Base64.getEncoder().encodeToString(keys.publicKeyDer), description.model,
                description.osVersion, description.appVersion, enrollmentCode?.trim()?.takeIf { it.isNotEmpty() },
            ),
        )
        store.deviceId = device.id
        return device
    }

    /** Status dieses Geräts, z. B. für „Wartet auf Freigabe“. */
    suspend fun myDevice(): DeviceResponse = send("GET", "devices/me", null)

    /** Beim Start: mit dem gespeicherten Refresh-Token und der Gerätesignatur eine neue Sitzung holen. */
    suspend fun restore(): Boolean = store.refreshToken != null && refresh(null)

    suspend fun signOut() {
        try {
            if (accessToken != null) send<Unit>("POST", "auth/logout", null)
        } catch (_: Exception) {
            // Abmelden gelingt lokal auch ohne Server.
        } finally {
            accessToken = null
            store.refreshToken = null
            store.user = null
        }
    }

    // Fachliche Aufrufe

    suspend fun bauakten(search: String? = null, limit: Int = 50, cursor: String? = null): BauaktePage =
        send("GET", "bauakten", null, query = mapOf("search" to search, "limit" to limit.toString(), "cursor" to cursor))

    suspend fun bauakte(id: String): BauakteDetail = send("GET", "bauakten/$id", null)

    suspend fun createBauakte(request: CreateBauakteRequest): BauakteDetail = send("POST", "bauakten", request)

    suspend fun hints(id: String): List<BauakteHint> = send("GET", "bauakten/$id/hints", null)

    suspend fun notes(id: String): List<Note> = send("GET", "bauakten/$id/notes", null)

    suspend fun addNote(bauakteId: String, text: String): Note = send("POST", "bauakten/$bauakteId/notes", NoteRequest(newId(), text.trim()))

    suspend fun workItems(bauakteId: String): List<WorkItem> = send("GET", "bauakten/$bauakteId/work-items", null)

    suspend fun addWorkItem(bauakteId: String, kind: WorkItemKind, title: String): WorkItem =
        send("POST", "work-items", WorkItemRequest(newId(), bauakteId, kind, title.trim()))

    suspend fun setWorkItemStatus(id: String, status: WorkItemStatus): WorkItem = send("PATCH", "work-items/$id", WorkStatusRequest(status))

    suspend fun myPlanning(from: String, to: String): PlanningRange = send("GET", "planning/mine", null, query = mapOf("from" to from, "to" to to))

    suspend fun directory(): List<DirectoryUser> = send("GET", "directory/users", null)

    suspend fun reportProblem(title: String, description: String?, blocked: Boolean, appVersion: String, osVersion: String): TicketDetail =
        send(
            "POST", "tickets",
            TicketRequest(
                newId(), title.trim(), description?.trim()?.takeIf { it.isNotEmpty() }, "other", "android", blocked,
                mapOf("appVersion" to appVersion, "platform" to "android", "osVersion" to osVersion, "deviceKind" to "phone"),
            ),
        )

    // Transport

    private suspend inline fun <reified T> send(
        method: String, path: String, body: Any?, authenticated: Boolean = true, query: Map<String, String?> = emptyMap(),
    ): T = send(method, path, body, serializer<T>(), authenticated, query)

    @Suppress("UNCHECKED_CAST")
    private suspend fun <T> send(
        method: String, path: String, body: Any?, result: KSerializer<T>, authenticated: Boolean, query: Map<String, String?>,
    ): T {
        if (authenticated && accessToken == null && !refresh(null)) {
            throw ApiException(401, "UNAUTHORIZED", "Bitte erneut anmelden.")
        }

        val used = accessToken
        var response = execute(method, path, body, if (authenticated) used else null, query)
        if (authenticated && response.status == 401 && refresh(used)) {
            response = execute(method, path, body, accessToken, query)
        }

        if (response.status !in 200..299) {
            throw problem(response.status, response.text)
        }

        if (response.status == 204 || response.text.isBlank()) {
            return Unit as T
        }

        return json.decodeFromString(result, response.text)
    }

    /** Erneuert einmal; parallele Aufrufer warten auf denselben Refresh. */
    private suspend fun refresh(failedToken: String?): Boolean = refreshLock.withLock {
        if (failedToken != null && accessToken != failedToken) return@withLock true
        val refreshToken = store.refreshToken ?: return@withLock false
        val signature = if (store.deviceId != null) DeviceProof.refresh(refreshToken, keys) else null
        val response = execute("POST", "auth/refresh", RefreshRequest(refreshToken, signature), null, emptyMap())
        if (response.status in 200..299) {
            val tokens = json.decodeFromString(RefreshResponse.serializer(), response.text)
            accessToken = tokens.accessToken
            store.refreshToken = tokens.refreshToken
            true
        } else {
            if (response.status in 400..499) {
                accessToken = null
                store.refreshToken = null
            }
            if (response.status == 401 || response.status == 403) false else throw problem(response.status, response.text)
        }
    }

    private class RawResponse(val status: Int, val text: String)

    private suspend fun execute(method: String, path: String, body: Any?, token: String?, query: Map<String, String?>): RawResponse =
        withContext(Dispatchers.IO) {
            val url = api.resolve(path)!!.newBuilder().apply { query.forEach { (k, v) -> if (v != null) addQueryParameter(k, v) } }.build()
            val payload = body?.let { json.encodeToString(serializerFor(it), it).toRequestBody(jsonType) }
                ?: if (method == "GET") null else "".toRequestBody(jsonType)
            val request = Request.Builder().url(url).method(method, payload).header("Accept", "application/json")
                .apply { if (token != null) header("Authorization", "Bearer $token") }.build()
            try {
                http.newCall(request).execute().use { RawResponse(it.code, it.body.string()) }
            } catch (exception: IOException) {
                throw ApiException(0, "NETWORK", "Der Server ist nicht erreichbar. Bitte Netzwerk und Serveradresse prüfen.")
            }
        }

    @Suppress("UNCHECKED_CAST")
    private fun serializerFor(value: Any): KSerializer<Any> = serializer(value::class.java) as KSerializer<Any>

    private fun problem(status: Int, text: String): ApiException {
        val parsed = runCatching { json.decodeFromString(Problem.serializer(), text) }.getOrNull()
        return ApiException(status, parsed?.code, parsed?.detail ?: parsed?.title ?: "Der Server meldet einen Fehler ($status).")
    }

    companion object {
        val json = Json { ignoreUnknownKeys = true; explicitNulls = false; encodeDefaults = true }
        private val jsonType = "application/json".toMediaType()

        fun newId(): String = UUID.randomUUID().toString()

        /** „estrich.firma.de“ → https://estrich.firma.de/; Pfade werden entfernt. */
        fun normalize(address: String): HttpUrl {
            val text = address.trim().trimEnd('/')
            require(text.isNotEmpty()) { "Bitte die Adresse des EstrichONE-Servers angeben." }
            val withScheme = if ("://" in text) text else "https://$text"
            val url = runCatching { withScheme.toHttpUrl() }.getOrNull()
            require(url != null) { "Die Serveradresse ist ungültig." }
            return url.newBuilder().encodedPath("/").query(null).build()
        }
    }
}
