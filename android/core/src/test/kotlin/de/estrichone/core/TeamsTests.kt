package de.estrichone.core

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.DisplayName
import org.junit.jupiter.api.Test

class TeamsTests {
    @Test
    @DisplayName("Teams: Modus „Microsoft Teams“ macht den Chat zum Archiv")
    fun status() {
        val status = EstrichOneApi.json.decodeFromString(TeamsStatus.serializer(), """
            {"chatMode":"teams","chatReadOnly":true,"available":true,"graphConfigured":true,"postVia":"off","canManage":false,
             "siteTeamName":"Baustellen","siteTeamUrl":"https://teams.microsoft.com/l/team/19%3aabc%40thread.tacv2/conversations?groupId=g",
             "siteTeamAppUrl":"msteams:/l/team/19%3aabc%40thread.tacv2/conversations?groupId=g","teamsWebUrl":"https://teams.microsoft.com"}
        """.trimIndent())
        assertTrue(status.chatReadOnly && status.available)
        assertEquals("Chat-Archiv", status.chatTitle)
        assertEquals(listOf("msteams", "https"), status.homeTargets.candidates.map { it.substringBefore(':') })

        val chat = EstrichOneApi.json.decodeFromString(ChatStatus.serializer(), """{"enabled":true,"canUse":true,"readOnly":true,"mode":"teams"}""")
        assertTrue(chat.readOnly)
        assertFalse(EstrichOneApi.json.decodeFromString(ChatStatus.serializer(), """{"enabled":true,"canUse":true}""").readOnly)
    }

    @Test
    @DisplayName("Teams: ohne Anbindung wie bisher, Teams allgemein als Ziel")
    fun defaults() {
        val status = TeamsStatus()
        assertFalse(status.available)
        assertEquals("Chat", status.chatTitle)
        assertEquals(listOf("https://teams.microsoft.com"), status.homeTargets.candidates)
    }

    @Test
    @DisplayName("Teams: Bauakte öffnet zuerst die App, sonst den Browser; fremde Links nie")
    fun bauakteLink() {
        val teams = EstrichOneApi.json.decodeFromString(BauakteTeams.serializer(), """
            {"bauakteId":"b","chatMode":"both","canManage":true,"canCreate":false,"canCreateChannel":false,"defaultLinkKind":"channel",
             "link":{"kind":"channel","created":true,"displayName":"2026-0042 EFH Müller","webUrl":"https://teams.microsoft.com/l/channel/19%3aabc/x?groupId=g",
             "appUrl":"msteams:/l/channel/19%3aabc/x?groupId=g","syncMembers":true,"lastSyncError":null,"syncedMembers":3}}
        """.trimIndent())
        val link = teams.link!!
        assertEquals("Kanal „2026-0042 EFH Müller“", link.summary)
        assertTrue(link.targets.app!!.startsWith("msteams:/l/channel/"))
        assertEquals("teams.microsoft.com", java.net.URI(link.targets.web!!).host)
        assertEquals("Gruppenchat", TeamsLink(kind = "groupChat").summary)
        assertTrue(TeamsTargets.of("https://evil.example", "https://teams.microsoft.com.evil.example/l").candidates.isEmpty())
        assertTrue(TeamsTargets.of(null, "http://teams.microsoft.com/l/chat/1").candidates.isEmpty())
        assertNull(EstrichOneApi.json.decodeFromString(BauakteTeams.serializer(), """{"chatMode":"teams","link":null}""").link)
    }
}
