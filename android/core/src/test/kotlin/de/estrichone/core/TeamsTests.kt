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

    @Test
    @DisplayName("Chat-Eingabe: keine Anhänge mehr, Hinweis auf Teams mit passendem Ziel")
    fun composerPolicy() {
        assertFalse(ChatComposerPolicy.ALLOWS_ATTACHMENTS)
        assertEquals("Für Datei- und Fotoaustausch bitte Microsoft Teams verwenden.", ChatComposerPolicy.FILE_EXCHANGE_HINT)
        val link = TeamsLink("channel", "BV Maier", "https://teams.microsoft.com/l/channel/x", "msteams:/l/channel/x")
        val both = TeamsStatus(chatMode = "both", available = true, siteTeamUrl = "https://teams.microsoft.com/l/team/y")
        assertEquals(link.targets, ChatComposerPolicy.teamsTargets(link, both))
        assertEquals("https://teams.microsoft.com/l/team/y", ChatComposerPolicy.teamsTargets(null, both)?.web)
        assertNull(ChatComposerPolicy.teamsTargets(null, TeamsStatus()))
        assertNull(ChatComposerPolicy.teamsTargets(null, null))
        assertNull(ChatComposerPolicy.teamsTargets(TeamsLink("channel", null, "https://evil.example/x", "https://evil.example/y"), null))
    }

    @Test
    @DisplayName("Chat in Teams: Deep Link wie Server und Web, Teams-App zuerst")
    fun chatDeepLinks() {
        assertEquals("https://teams.microsoft.com/l/chat/0/0?users=dieter@example.org", TeamsChatLinks.webUrl(listOf("dieter@example.org"), "Thema"))
        assertEquals(
            "https://teams.microsoft.com/l/chat/0/0?users=a@x.de,b%2Bc@x.de&topicName=Bauakte%204711%20EFH%20%26%20Co&message=Hallo",
            TeamsChatLinks.webUrl(listOf("a@x.de", "b+c@x.de"), "Bauakte 4711 EFH & Co", "Hallo"),
        )
        assertEquals("%C3%A4", TeamsChatLinks.encode("ä"))
        assertEquals("msteams:/l/chat/0/0?users=a@x.de", TeamsChatLinks.appUrl("https://teams.microsoft.com/l/chat/0/0?users=a@x.de"))
        val people = listOf(TeamsPerson("1", "Anna", "anna@x.de"), TeamsPerson("2", "Bernd", null))
        assertEquals(
            listOf("msteams:/l/chat/0/0?users=anna@x.de", "https://teams.microsoft.com/l/chat/0/0?users=anna@x.de"),
            TeamsChatLinks.targets(people)!!.candidates,
        )
        assertNull(TeamsChatLinks.targets(listOf(people[1])))
    }

    @Test
    @DisplayName("Teams: Personen, Teamchat der Bauakte und Hinweis aus der API")
    fun peopleAndNotice() {
        val people = EstrichOneApi.json.decodeFromString(kotlinx.serialization.builtins.ListSerializer(TeamsPerson.serializer()), """
            [{"userId":"1","name":"Anna","upn":"anna@x.de","fromEmail":false,"reason":null},{"userId":"2","name":"Bernd","upn":null,"reason":"Kein Konto"}]
        """.trimIndent())
        assertEquals(listOf("anna@x.de", null), people.map { it.upn })
        val akte = EstrichOneApi.json.decodeFromString(BauakteTeams.serializer(), """
            {"chatMode":"teams","teamChat":{"webUrl":"https://teams.microsoft.com/l/chat/0/0?users=a@x.de","appUrl":"msteams:/l/chat/0/0?users=a@x.de",
             "viaGraph":false,"included":["Anna"],"missing":[]}}
        """.trimIndent())
        assertEquals("msteams", akte.teamChat!!.targets.candidates.first().substringBefore(':'))
        val status = EstrichOneApi.json.decodeFromString(TeamsStatus.serializer(), """{"chatMode":"teams","rolloutNotice":"Ab sofort Teams","canStartChats":true}""")
        assertTrue(status.canStartChats)
        assertEquals("Ab sofort Teams", status.rolloutNotice)
    }
}
