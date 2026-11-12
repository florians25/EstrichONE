package de.estrichone.core

import kotlinx.serialization.Serializable

/**
 * Microsoft Teams (docs/microsoft-teams.md): Chat-Modus und „In Teams öffnen“. Ohne Teams-Anbindung (älterer Server, 404)
 * ist der Status `null` und alles bleibt wie bisher.
 */
@Serializable
data class TeamsStatus(
    /** `estrichOne` (Standard), `teams` (eingebauter Chat nur noch Archiv) oder `both`. */
    val chatMode: String = "estrichOne",
    val chatReadOnly: Boolean = false,
    val available: Boolean = false,
    val siteTeamName: String? = null,
    val siteTeamUrl: String? = null,
    val siteTeamAppUrl: String? = null,
    val teamsWebUrl: String = "https://teams.microsoft.com",
    /** Hinweis „Ab sofort chatten wir in Teams“ (nur wenn eingeschaltet). */
    val rolloutNotice: String? = null,
    /** „Chat in Teams“ mit Kollegen anbieten (Teams im Einsatz, Recht chat.use). */
    val canStartChats: Boolean = false,
) {
    /** Ziel für „Microsoft Teams“: Team „Baustellen“, sonst Teams allgemein. */
    val homeTargets: TeamsTargets get() = TeamsTargets.of(siteTeamAppUrl, siteTeamUrl ?: teamsWebUrl)

    /** Bezeichnung des eingebauten Chats. */
    val chatTitle: String get() = if (chatReadOnly) "Chat-Archiv" else "Chat"
}

/** Verknüpfte Teams-Unterhaltung einer Bauakte (Auszug aus `TeamsLinkResponse`). */
@Serializable
data class TeamsLink(
    val kind: String = "channel",
    val displayName: String? = null,
    val webUrl: String = "",
    val appUrl: String = "",
    val lastSyncError: String? = null,
) {
    val targets: TeamsTargets get() = TeamsTargets.of(appUrl, webUrl)

    val summary: String get() {
        val type = if (kind == "groupChat") "Gruppenchat" else "Kanal"
        return if (displayName.isNullOrBlank()) type else "$type „$displayName“"
    }
}

@Serializable
data class BauakteTeams(
    val chatMode: String = "estrichOne",
    val link: TeamsLink? = null,
    val canManage: Boolean = false,
    /** Gruppenchat mit dem ganzen Bauakten-Team (ohne die eigene Person); `null` ohne Microsoft-Konten. */
    val teamChat: TeamsChatLink? = null,
)

/** Link zu einem Einzel- bzw. Gruppenchat in Teams (`TeamsChatLinkResponse`). */
@Serializable
data class TeamsChatLink(val webUrl: String = "", val appUrl: String = "", val included: List<String> = emptyList()) {
    val targets: TeamsTargets get() = TeamsTargets.of(appUrl, webUrl)
}

/** Kollege für „Chat in Teams“ (`GET /api/v1/teams/people`); ohne [upn] keine Aktion anbieten. */
@Serializable
data class TeamsPerson(val userId: String = "", val name: String = "", val upn: String? = null)

/**
 * Deep Links für Chats in Teams (wie Server und Web): `https://teams.microsoft.com/l/chat/0/0?users=a@x,b@x&topicName=…&message=…`,
 * in der Teams-App `msteams:/l/chat/0/0?…`. Ein Empfänger = Einzelchat, mehrere = Gruppenchat (nur dann mit Thema).
 */
object TeamsChatLinks {
    const val TEAMS_PACKAGE = "com.microsoft.teams"
    const val PLAY_STORE_URL = "https://play.google.com/store/apps/details?id=com.microsoft.teams"

    private const val UNRESERVED = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~"

    /** Wie encodeURIComponent bzw. Uri.EscapeDataString (UTF-8, nur „-._~“ und ASCII-Buchstaben/Ziffern unkodiert). */
    fun encode(text: String): String = buildString {
        for (byte in text.toByteArray(Charsets.UTF_8)) {
            val char = (byte.toInt() and 0xFF).toChar()
            if (char in UNRESERVED) append(char) else append('%').append("%02X".format(byte.toInt() and 0xFF))
        }
    }

    fun webUrl(upns: List<String>, topic: String? = null, message: String? = null): String {
        val users = upns.joinToString(",") { encode(it.trim()).replace("%40", "@") }
        val url = StringBuilder("https://teams.microsoft.com/l/chat/0/0?users=$users")
        topic?.trim()?.takeIf { it.isNotEmpty() && upns.size > 1 }?.let { url.append("&topicName=").append(encode(it.take(250))) }
        message?.trim()?.takeIf { it.isNotEmpty() }?.let { url.append("&message=").append(encode(it.take(1000))) }
        return url.toString()
    }

    /** `https://teams.microsoft.com/l/…` → `msteams:/l/…` */
    fun appUrl(web: String): String? {
        val uri = runCatching { java.net.URI(web) }.getOrNull() ?: return null
        if (!uri.scheme.equals("https", ignoreCase = true)) return null
        return "msteams:" + uri.rawPath + (uri.rawQuery?.let { "?$it" } ?: "")
    }

    /** Ziele (Teams-App zuerst, dann https) für einen Chat mit diesen Personen; `null`, wenn niemand ein Microsoft-Konto hat. */
    fun targets(people: List<TeamsPerson>, topic: String? = null): TeamsTargets? {
        val upns = people.mapNotNull { it.upn?.takeIf(String::isNotBlank) }
        if (upns.isEmpty()) return null
        val web = webUrl(upns, topic)
        return TeamsTargets.of(appUrl(web), web)
    }
}

/** Reihenfolge beim Öffnen: Teams-App (`msteams:`), sonst Browser – nur echte Teams-Adressen. */
data class TeamsTargets(val app: String?, val web: String?) {
    val candidates: List<String> get() = listOfNotNull(app, web)

    companion object {
        private val teamsHosts = setOf("teams.microsoft.com", "teams.cloud.microsoft", "teams.live.com")

        fun of(app: String?, web: String?): TeamsTargets = TeamsTargets(
            app?.takeIf { it.startsWith("msteams:", ignoreCase = true) },
            web?.takeIf { isTeamsWeb(it) },
        )

        fun isTeamsWeb(url: String): Boolean {
            val uri = runCatching { java.net.URI(url) }.getOrNull() ?: return false
            return uri.scheme.equals("https", ignoreCase = true) && uri.host?.lowercase() in teamsHosts
        }
    }
}

/** Chat-Modus und Team „Baustellen“; `null`, wenn der Server keine Teams-Anbindung kennt. */
suspend fun EstrichOneApi.teamsStatus(): TeamsStatus? = try {
    send("GET", "teams/status", null)
} catch (error: ApiException) {
    if (error.status == 404 || error.status == 403) null else throw error
}

/** Microsoft-Konten der Kollegen für „Chat in Teams“ (leer ohne Teams-Anbindung bzw. Recht). */
suspend fun EstrichOneApi.teamsPeople(): List<TeamsPerson> = try {
    send("GET", "teams/people", null)
} catch (error: ApiException) {
    if (error.status == 404 || error.status == 403) emptyList() else throw error
}

suspend fun EstrichOneApi.bauakteTeams(bauakteId: String): BauakteTeams = send("GET", "bauakten/$bauakteId/teams", null)
