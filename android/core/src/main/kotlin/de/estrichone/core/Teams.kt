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
data class BauakteTeams(val chatMode: String = "estrichOne", val link: TeamsLink? = null, val canManage: Boolean = false)

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

suspend fun EstrichOneApi.bauakteTeams(bauakteId: String): BauakteTeams = send("GET", "bauakten/$bauakteId/teams", null)
