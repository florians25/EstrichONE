import Foundation
import Testing
@testable import EstrichCore

@Suite("Microsoft Teams: Chat-Modus und „In Teams öffnen“")
struct TeamsTests {
    @Test("Status: Modus „Microsoft Teams“ macht den Chat zum Archiv")
    func status() throws {
        let status = try JSONCoding.decoder.decode(TeamsStatus.self, from: Data("""
        {"chatMode":"teams","chatReadOnly":true,"available":true,"graphConfigured":true,"postVia":"off","canManage":false,
         "siteTeamName":"Baustellen","siteTeamUrl":"https://teams.microsoft.com/l/team/19%3aabc%40thread.tacv2/conversations?groupId=g",
         "siteTeamAppUrl":"msteams:/l/team/19%3aabc%40thread.tacv2/conversations?groupId=g","teamsWebUrl":"https://teams.microsoft.com"}
        """.utf8))
        #expect(status.chatMode == .teams && status.chatReadOnly && status.available)
        #expect(status.chatTitle == "Chat-Archiv")
        #expect(status.homeTargets.candidates.map(\.scheme) == ["msteams", "https"])
        #expect(status.siteTeamName == "Baustellen")
    }

    @Test("Ohne Teams-Anbindung und unbekannte Werte: wie bisher")
    func defaults() throws {
        let unknown = try JSONCoding.decoder.decode(TeamsStatus.self, from: Data(#"{"chatMode":"neu"}"#.utf8))
        #expect(unknown.chatMode == .estrichOne && !unknown.available && !unknown.chatReadOnly)
        #expect(unknown.chatTitle == "Chat")
        #expect(unknown.homeTargets.candidates == [URL(string: "https://teams.microsoft.com")!])
        let both = TeamsStatus(chatMode: .both)
        #expect(both.available && !both.chatReadOnly)

        let chat = try JSONCoding.decoder.decode(ChatStatus.self, from: Data(#"{"enabled":true,"canUse":true,"readOnly":true,"mode":"teams"}"#.utf8))
        #expect(chat.readOnly)
        let older = try JSONCoding.decoder.decode(ChatStatus.self, from: Data(#"{"enabled":true,"canUse":true}"#.utf8))
        #expect(!older.readOnly)
    }

    @Test("Bauakte: Teams-App zuerst, sonst Browser; fremde Links werden nicht geöffnet")
    func bauakteLink() throws {
        let response = try JSONCoding.decoder.decode(BauakteTeamsResponse.self, from: Data("""
        {"bauakteId":"0193a1b2-0000-7000-8000-0000000b0001","chatMode":"both","canManage":true,"canCreate":false,"canCreateChannel":false,
         "defaultLinkKind":"channel","link":{"kind":"channel","created":true,"displayName":"2026-0042 EFH Müller",
         "webUrl":"https://teams.microsoft.com/l/channel/19%3aabc%40thread.tacv2/x?groupId=g","appUrl":"msteams:/l/channel/19%3aabc%40thread.tacv2/x?groupId=g",
         "syncMembers":true,"lastSyncError":null,"syncedMembers":3,"unmappedMembers":0,"hasWebhook":false,"linkedAt":"2026-11-12T09:00:00Z"}}
        """.utf8))
        let link = try #require(response.link)
        #expect(link.summary == "Kanal „2026-0042 EFH Müller“")
        #expect(link.targets.app?.absoluteString.hasPrefix("msteams:/l/channel/") == true)
        #expect(link.targets.web?.host == "teams.microsoft.com")
        #expect(TeamsLink(kind: "groupChat", displayName: nil, webUrl: "https://teams.microsoft.com/l/chat/19:x/0", appUrl: "x").summary == "Gruppenchat")

        let foreign = TeamsOpenTargets(app: "https://evil.example/l", web: "https://teams.microsoft.com.evil.example/l")
        #expect(foreign.candidates.isEmpty)
        let insecure = TeamsOpenTargets(app: nil, web: "http://teams.microsoft.com/l/chat/1")
        #expect(insecure.candidates.isEmpty)

        let unlinked = try JSONCoding.decoder.decode(BauakteTeamsResponse.self, from: Data(#"{"chatMode":"teams","link":null,"canManage":false}"#.utf8))
        #expect(unlinked.link == nil && unlinked.chatMode == .teams)
    }

    @Test("Chat-Eingabe: keine Anhänge mehr, Hinweis auf Teams mit passendem Ziel")
    func composerPolicy() {
        #expect(ChatComposerPolicy.allowsAttachments == false)
        #expect(ChatComposerPolicy.fileExchangeHint == "Für Datei- und Fotoaustausch bitte Microsoft Teams verwenden.")
        #expect(!ChatComposerPolicy.shareOptions.contains { ["Kamera", "Fotos", "Dokument"].contains($0) })

        let link = TeamsLink(kind: "channel", displayName: "BV Maier", webUrl: "https://teams.microsoft.com/l/channel/x", appUrl: "msteams:/l/channel/x")
        let both = TeamsStatus(chatMode: .both, siteTeamUrl: "https://teams.microsoft.com/l/team/y")
        // Verknüpfte Unterhaltung der Bauakte geht vor.
        #expect(ChatComposerPolicy.teamsTargets(link: link, status: both) == link.targets)
        // Sonst das Team „Baustellen“ bzw. Teams allgemein.
        #expect(ChatComposerPolicy.teamsTargets(link: nil, status: both)?.web == URL(string: "https://teams.microsoft.com/l/team/y"))
        // Ohne Teams-Anbindung: nur der Text.
        #expect(ChatComposerPolicy.teamsTargets(link: nil, status: TeamsStatus(chatMode: .estrichOne)) == nil)
        #expect(ChatComposerPolicy.teamsTargets(link: nil, status: nil) == nil)
        // Fremde Adressen öffnet der Hinweis nicht.
        let foreign = TeamsLink(kind: "channel", displayName: nil, webUrl: "https://evil.example/x", appUrl: "https://evil.example/y")
        #expect(ChatComposerPolicy.teamsTargets(link: foreign, status: nil) == nil)
    }
}
