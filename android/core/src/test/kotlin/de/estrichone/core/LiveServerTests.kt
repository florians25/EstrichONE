package de.estrichone.core

import kotlinx.coroutines.runBlocking
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.RequestBody.Companion.toRequestBody
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNotNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Assumptions.assumeTrue
import org.junit.jupiter.api.DisplayName
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.assertThrows

/**
 * Gegen einen echten EstrichONE-Server (scripts/test-core-against-server.sh). Ohne ESTRICHONE_LIVE_SERVER übersprungen;
 * das Skript prüft, dass sie wirklich gelaufen sind.
 */
class LiveServerTests {
    private val server = System.getenv("ESTRICHONE_LIVE_SERVER")
    private val login = System.getenv("ESTRICHONE_LIVE_LOGIN") ?: "admin"
    private val password = System.getenv("ESTRICHONE_LIVE_PASSWORD") ?: ""
    private val phone = DeviceDescription("Testtelefon", "Pixel Test", "16", "0.1.0")

    @Test
    @DisplayName("Echter Server: Anmelden, Gerät binden, Bauakte dokumentieren, Erneuern mit Gerätesignatur")
    fun liveSignInBindDocumentRefresh() = runBlocking {
        assumeTrue(server != null, "ESTRICHONE_LIVE_SERVER nicht gesetzt")
        val store = MemorySessionStore()
        val keys = SoftwareDeviceKeys()
        val api = EstrichOneApi(server!!, store, keys)

        val info = api.serverInfo()
        api.signIn(login, password, "Testtelefon")
        val device = api.bindDevice(DeviceKind.Personal, null, phone)
        val created = api.createBauakte(CreateBauakteRequest(EstrichOneApi.newId(), "Android-Test ${System.nanoTime()}", city = "Landshut"))
        val note = api.addNote(created.id, "Fotos folgen")
        val task = api.addWorkItem(created.id, WorkItemKind.Defect, "Randdämmstreifen fehlt")
        val done = api.setWorkItemStatus(task.id, WorkItemStatus.Done)
        val found = api.bauakten(search = created.title)

        assertEquals("EstrichONE", info.product)
        assertEquals(DeviceStatus.Active, device.status)
        assertEquals(DeviceStatus.Active, api.myDevice().status)
        assertEquals("Fotos folgen", note.text)
        assertEquals(WorkItemStatus.Done, done.status)
        assertEquals(created.id, found.items.single().id)
        assertTrue(api.hints(created.id).isNotEmpty())

        // Neuer Start: nur Refresh-Token und Geräteschlüssel – die Signatur muss der Server akzeptieren.
        val restarted = EstrichOneApi(server, store, keys)
        assertTrue(restarted.restore())
        assertEquals(created.title, restarted.bauakte(created.id).title)
        assertEquals(1, restarted.notes(created.id).size)

        // Mit einem fremden Schlüssel schlägt das Erneuern fehl und die Anmeldung wird verworfen.
        val stolen = EstrichOneApi(server, MemorySessionStore().also { it.refreshToken = store.refreshToken; it.deviceId = store.deviceId }, SoftwareDeviceKeys())
        assertEquals(false, stolen.restore())
    }

    @Test
    @DisplayName("Echter Server: Einmalpasswort wechseln, Zwei-Faktor-Anmeldung mit Code und Wiederherstellungscode")
    fun liveOneTimePasswordAndTwoFactor() = runBlocking {
        assumeTrue(server != null, "ESTRICHONE_LIVE_SERVER nicht gesetzt")
        // Administrator legt eine Person an (Einmalpasswort), über die Web-Schnittstelle der API.
        val admin = EstrichOneApi(server!!, MemorySessionStore(), SoftwareDeviceKeys())
        val adminTokens = admin.signIn(login, password, "Testtelefon", null)
        val userName = "zf.android.${System.nanoTime() % 100000}"
        val created = Live.post(server, "users", adminTokens.accessToken,
            """{"displayName":"Zwei Faktor Android","userName":"$userName","roleIds":["01a00000-0000-7000-8000-000000000002"]}""")
        val oneTime = Live.string(created, "oneTimePassword")

        val api = EstrichOneApi(server, MemorySessionStore(), SoftwareDeviceKeys())
        assertTrue(api.signIn(userName, oneTime, "Testtelefon").mustChangePassword)
        val blocked = assertThrows<ApiException> { runBlocking { api.bindDevice(DeviceKind.Personal, null, phone) } }
        assertEquals("PASSWORD_CHANGE_REQUIRED", blocked.code)
        val newPassword = "Zwei-Faktor-Android-2026!"
        api.changePassword(oneTime, newPassword)

        val first = api.signIn(userName, newPassword, "Testtelefon")
        val setup = Live.post(server, "auth/two-factor/setup", first.accessToken, """{"currentPassword":"$newPassword"}""")
        val secret = Live.base32(Live.string(setup, "secret"))
        val step = System.currentTimeMillis() / 1000 / 30
        val enabled = Live.post(server, "auth/two-factor/enable", first.accessToken, """{"code":"${Live.totp(secret, step)}"}""")
        val recovery = Live.firstRecoveryCode(enabled)

        val required = assertThrows<ApiException> { runBlocking { api.signIn(userName, newPassword, "Testtelefon") } }
        assertTrue(required.requiresTwoFactorCode)
        val invalid = assertThrows<ApiException> { runBlocking { api.signIn(userName, newPassword, "Testtelefon", Live.totp(secret, step + 5)) } }
        assertTrue(invalid.isInvalidTwoFactorCode)
        api.signIn(userName, newPassword, "Testtelefon", Live.totp(secret, step + 1))
        api.bindDevice(DeviceKind.Personal, null, phone)
        // Erneuern braucht keinen Code.
        assertTrue(api.restore())
        api.signIn(userName, newPassword, "Testtelefon", recovery.uppercase())
        val reused = assertThrows<ApiException> { runBlocking { api.signIn(userName, newPassword, "Testtelefon", recovery) } }
        assertTrue(reused.isInvalidTwoFactorCode)
    }

    @Test
    @DisplayName("Echter Server: falsches Passwort und Problem melden")
    fun liveWrongPasswordAndReportProblem() = runBlocking {
        assumeTrue(server != null, "ESTRICHONE_LIVE_SERVER nicht gesetzt")
        val api = EstrichOneApi(server!!, MemorySessionStore(), SoftwareDeviceKeys())

        val error = assertThrows<ApiException> { runBlocking { api.signIn(login, "falsch-falsch-falsch", "Testtelefon") } }
        api.signIn(login, password, "Testtelefon")
        api.bindDevice(DeviceKind.Personal, null, phone)
        val ticket = api.reportProblem("Kamera startet nicht", null, blocked = false, appVersion = "0.1.0", osVersion = "16")

        assertEquals(401, error.status)
        assertEquals("Anmeldename oder Passwort ist nicht korrekt.", error.message)
        assertNotNull(ticket.summary.number)
    }
}

/** Hilfen nur für den Live-Test: rohe Aufrufe der Verwaltung und TOTP wie eine Authenticator-App (RFC 6238). */
private object Live {
    private val http = okhttp3.OkHttpClient()

    fun post(server: String, path: String, token: String, json: String): kotlinx.serialization.json.JsonObject {
        val url = EstrichOneApi.normalize(server).newBuilder().addPathSegments("api/v1/$path").build()
        val request = okhttp3.Request.Builder().url(url).header("Authorization", "Bearer $token")
            .post(json.toRequestBody("application/json".toMediaType()))
            .build()
        http.newCall(request).execute().use { response ->
            val body = response.body?.string().orEmpty()
            check(response.isSuccessful) { "$path: ${response.code} $body" }
            return kotlinx.serialization.json.Json.parseToJsonElement(body) as kotlinx.serialization.json.JsonObject
        }
    }

    fun string(json: kotlinx.serialization.json.JsonObject, key: String): String =
        (json[key] as kotlinx.serialization.json.JsonPrimitive).content

    fun firstRecoveryCode(json: kotlinx.serialization.json.JsonObject): String =
        ((json["recoveryCodes"] as kotlinx.serialization.json.JsonArray)[0] as kotlinx.serialization.json.JsonPrimitive).content

    fun base32(text: String): ByteArray {
        val alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"
        var buffer = 0L
        var bits = 0
        val out = java.io.ByteArrayOutputStream()
        for (c in text.uppercase()) {
            if (c == '=' || c == ' ') continue
            buffer = (buffer shl 5) or alphabet.indexOf(c).toLong()
            bits += 5
            if (bits >= 8) {
                bits -= 8
                out.write(((buffer shr bits) and 0xFF).toInt())
            }
        }
        return out.toByteArray()
    }

    fun totp(secret: ByteArray, step: Long): String {
        val mac = javax.crypto.Mac.getInstance("HmacSHA1")
        mac.init(javax.crypto.spec.SecretKeySpec(secret, "HmacSHA1"))
        val hash = mac.doFinal(java.nio.ByteBuffer.allocate(8).putLong(step).array())
        val offset = hash[hash.size - 1].toInt() and 0x0F
        val binary = ((hash[offset].toInt() and 0x7F) shl 24) or ((hash[offset + 1].toInt() and 0xFF) shl 16) or
            ((hash[offset + 2].toInt() and 0xFF) shl 8) or (hash[offset + 3].toInt() and 0xFF)
        return "%06d".format(binary % 1_000_000)
    }
}
