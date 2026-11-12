import EstrichCore
import SwiftUI
import UIKit

/// Chat des Betriebs (Modul „Chat“) – vertraut wie ein Messenger, ruhig wie die übrige App: Unterhaltungen mit Bild,
/// letzter Nachricht, Entwurf, Stummschaltung und Erwähnungen; Suche über alle Chats; neue Einzelchats und Gruppen.
/// Der Verlauf kommt direkt vom Server und bleibt nur im Arbeitsspeicher.
struct ChatListView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.shotScene) private var shotScene
    @State private var conversations: [ChatConversation] = []
    @State private var drafts: [UUID: String] = [:]
    @State private var loaded = false
    @State private var failure: String?
    @State private var open: ChatConversation?
    @State private var startsChat = false
    @State private var search = ""
    @State private var hits: [ChatSearchHit] = []
    @State private var searching = false
    @State private var showsPrivacy = false
    @State private var startsTeamsChat = false
    @State private var openHit: ChatSearchHit?

    private var chat: ChatCenter? { model.active?.chat }
    private var status: ChatStatus? { model.active?.sync?.chat }
    /// Modus „Microsoft Teams“: Chat-Archiv (nur lesen).
    private var archive: Bool { status?.readOnly == true }
    private var teams: TeamsStatus? { model.active?.sync?.teams }

    var body: some View {
        conversationList
        .listStyle(.insetGrouped)
        .overlay { if !loaded { ProgressView() } }
        .navigationTitle(archive ? "Chat-Archiv" : "Chat")
        .navigationBarTitleDisplayMode(.inline)
        .searchable(text: $search, placement: .navigationBarDrawer(displayMode: .automatic), prompt: "In allen Chats suchen")
        .task(id: search) { await runSearch() }
        .navigationDestination(item: $open) { conversation in
            ChatConversationView(conversationId: conversation.id, title: conversation.title, kind: conversation.chatKind)
        }
        .navigationDestination(item: $openHit) { hit in
            ChatConversationView(conversationId: hit.conversationId, title: hit.conversationTitle, focusMessage: hit.message.id)
        }
        .toolbar {
            if let teams, teams.available {
                ToolbarItem(placement: .primaryAction) {
                    OpenInTeamsButton(targets: teams.homeTargets)
                }
            }
            if !archive {
                ToolbarItem(placement: .primaryAction) {
                    Button("Neuer Chat", systemImage: "square.and.pencil") { startsChat = true }
                        .accessibilityIdentifier("newChatButton")
                }
            } else if teams?.canStartChats == true {
                // Modus „Microsoft Teams“: neuer Chat direkt in der Teams-App.
                ToolbarItem(placement: .primaryAction) {
                    Button("Neuer Chat in Teams", systemImage: "square.and.pencil") { startsTeamsChat = true }
                        .accessibilityIdentifier("newTeamsChatButton")
                }
            }
        }
        .sheet(isPresented: $startsTeamsChat) {
            TeamsPeopleSheet()
                .presentationDetents([.medium, .large])
        }
        .sheet(isPresented: $startsChat) {
            NewChatSheet(canCreateGroups: status?.canAdmin == true) { conversation in
                startsChat = false
                Task {
                    await load()
                    open = conversation
                }
            }
            .presentationDetents([.medium, .large])
        }
        .sheet(isPresented: $showsPrivacy) {
            ChatPrivacyNotice(retentionDays: status?.retentionDays) {
                ChatPrivacyNotice.acknowledge(userId: model.active?.user.id, version: status?.privacyNoticeVersion ?? 1)
                showsPrivacy = false
                if let client = model.active?.client, let version = status?.privacyNoticeVersion {
                    Task { try? await client.acknowledgeChatPrivacy(version: version) }
                }
            }
            .interactiveDismissDisabled()
            .presentationDetents([.large])
        }
        .task(id: model.active?.sync?.revision) { await load() }
        .onChange(of: chat?.eventCount) { _, _ in Task { await load() } }
        .task(id: chat?.isLive) {
            // Sicherheitsnetz: selten mit Echtzeit-Kanal, sonst alle 15 Sekunden.
            while !Task.isCancelled {
                try? await Task.sleep(for: ChatPolling.listInterval(live: chat?.isLive == true))
                await load()
            }
        }
        .onAppear {
            if shotScene == nil, status?.privacyAccepted != true, !ChatPrivacyNotice.isAcknowledged(userId: model.active?.user.id, version: status?.privacyNoticeVersion ?? 1) {
                showsPrivacy = true
            }
        }
        .refreshable { await load() }
    }

    private var conversationList: some View {
        List {
            if archive {
                Section {
                    ChatArchiveNotice(targets: teams?.homeTargets)
                }
            }
            if let failure {
                Section {
                    NoticeLine(tone: .neutral, text: failure, symbol: "wifi.slash")
                        .listRowInsets(EdgeInsets())
                        .listRowBackground(Color.clear)
                }
            }
            if !search.isEmpty {
                searchResults
            } else {
                if loaded, conversations.isEmpty, failure == nil {
                    Section {
                        EmptyStateView(symbol: "bubble.left.and.bubble.right", title: "Noch keine Unterhaltungen",
                                       message: "Sobald das Büro oder Ihre Kolonne schreibt, erscheint die Unterhaltung hier.")
                    }
                }
                Section {
                    ForEach(conversations) { conversation in
                        Button { open = conversation } label: {
                            ConversationRow(conversation: conversation, draft: drafts[conversation.id],
                                            typing: chat?.typingName(in: conversation.id))
                        }
                        .buttonStyle(.plain)
                        .accessibilityIdentifier("chatConversation")
                        .swipeActions(edge: .trailing) {
                            muteButton(conversation)
                        }
                        .swipeActions(edge: .leading) {
                            if conversation.unreadCount > 0 {
                                Button("Gelesen", systemImage: "checkmark.bubble") { Task { await markRead(conversation) } }
                                    .tint(Theme.accentFill)
                            }
                        }
                        .contextMenu { muteMenu(conversation) }
                    }
                } footer: {
                    if !conversations.isEmpty {
                        Text(footer)
                    }
                }
            }
        }
    }

    private var footer: String {
        var text = "Nachrichten und Anhänge werden verschlüsselt auf dem Server Ihres Betriebs gespeichert"
        if let days = status?.retentionDays { text += " und nach \(days) Tagen gelöscht" }
        return text + ". Sprachnachrichten werden nicht unterstützt; zum Diktieren auf das Mikrofon tippen."
    }

    @ViewBuilder
    private var searchResults: some View {
        let term = search.trimmingCharacters(in: .whitespaces).lowercased()
        let titleMatches = conversations.filter { $0.title.lowercased().contains(term) }
        if !titleMatches.isEmpty {
            Section("Chats") {
                ForEach(titleMatches) { conversation in
                    Button { open = conversation } label: { ConversationRow(conversation: conversation, draft: drafts[conversation.id], typing: nil) }
                        .buttonStyle(.plain)
                }
            }
        }
        Section("Nachrichten") {
            if searching, hits.isEmpty {
                HStack { Spacer(); ProgressView(); Spacer() }
            } else if hits.isEmpty {
                Text(term.count < 2 ? "Mindestens zwei Zeichen eingeben." : "Keine Nachrichten gefunden.")
                    .font(Typo.subhead)
                    .foregroundStyle(Theme.textSecondary)
            }
            ForEach(hits) { hit in
                Button { openHit = hit } label: {
                    VStack(alignment: .leading, spacing: 2) {
                        HStack {
                            Text(hit.conversationTitle).font(Typo.bodyStrong).foregroundStyle(Theme.textPrimary).lineLimit(1)
                            Spacer()
                            Text(ChatTime.short(hit.message.createdAt)).font(Typo.meta).foregroundStyle(Theme.textSecondary)
                        }
                        Text(highlighted(hit.message.summary, term: term))
                            .font(Typo.subhead)
                            .foregroundStyle(Theme.textSecondary)
                            .lineLimit(2)
                    }
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
            }
        }
    }

    private func highlighted(_ text: String, term: String) -> AttributedString {
        var attributed = AttributedString(text)
        if !term.isEmpty, let range = attributed.range(of: term, options: .caseInsensitive) {
            attributed[range].foregroundColor = Theme.textPrimary
            attributed[range].font = Typo.subheadStrong
        }
        return attributed
    }

    @ViewBuilder
    private func muteButton(_ conversation: ChatConversation) -> some View {
        if conversation.isMuted() {
            Button("Ton an", systemImage: "bell") { Task { await mute(conversation, nil, on: false) } }
                .tint(Theme.textSecondary)
        } else {
            Button("Stumm", systemImage: "bell.slash") { Task { await mute(conversation, .eightHours, on: true) } }
                .tint(Theme.textSecondary)
        }
    }

    @ViewBuilder
    private func muteMenu(_ conversation: ChatConversation) -> some View {
        if conversation.isMuted() {
            Button("Stummschaltung aufheben", systemImage: "bell") { Task { await mute(conversation, nil, on: false) } }
        } else {
            Menu("Stummschalten", systemImage: "bell.slash") {
                ForEach(ChatMuteOption.allCases) { option in
                    Button(option.title) { Task { await mute(conversation, option, on: true) } }
                }
            }
        }
        if conversation.unreadCount > 0 {
            Button("Als gelesen markieren", systemImage: "checkmark.bubble") { Task { await markRead(conversation) } }
        }
    }

    private func mute(_ conversation: ChatConversation, _ option: ChatMuteOption?, on: Bool) async {
        guard let client = model.active?.client else { return }
        do throws(APIError) {
            try await client.muteChat(conversation.id, muted: on, until: option?.until())
            if let index = conversations.firstIndex(where: { $0.id == conversation.id }) {
                conversations[index].mutedUntil = on ? (option?.until() ?? .distantFuture) : nil
            }
        } catch {
            failure = error.serverMessage ?? error.message
        }
    }

    private func markRead(_ conversation: ChatConversation) async {
        guard let client = model.active?.client else { return }
        try? await client.markChatRead(conversation.id)
        if let index = conversations.firstIndex(where: { $0.id == conversation.id }) { conversations[index].unreadCount = 0 }
        await model.active?.sync?.refreshChat()
    }

    private func runSearch() async {
        let term = search.trimmingCharacters(in: .whitespaces)
        guard term.count >= 2, let client = model.active?.client else {
            hits = []
            return
        }
        searching = true
        try? await Task.sleep(for: .milliseconds(300))
        guard !Task.isCancelled else { return }
        do throws(APIError) {
            hits = try await client.searchChat(term)
        } catch {
            hits = []
        }
        searching = false
    }

    private static let cacheKey = "chat_conversations"

    private static func sorted(_ list: [ChatConversation]) -> [ChatConversation] {
        list.sorted { lhs, rhs in
            if lhs.pinned != rhs.pinned { return lhs.pinned }
            return (lhs.lastMessageAt ?? .distantPast) > (rhs.lastMessageAt ?? .distantPast)
        }
    }

    private func load() async {
        guard let client = model.active?.client else { return }
        let store = model.active?.sync?.store
        // Zuletzt geladene Liste sofort zeigen (verschlüsselt auf dem Gerät), dann frisch laden.
        if !loaded, conversations.isEmpty, let store, let text = try? await store.meta(Self.cacheKey),
           let cached = ChatConversation.decodeList(Data(text.utf8)), !cached.isEmpty {
            conversations = Self.sorted(cached)
            loaded = true
        }
        do throws(APIError) {
            let (list, data) = try await client.chatConversationsWithData()
            conversations = Self.sorted(list)
            if let store { try? await store.setMeta(Self.cacheKey, String(decoding: data, as: UTF8.self)) }
            failure = nil
            applyShotScene()
        } catch {
            failure = error == .offline
                ? (conversations.isEmpty ? "Keine Verbindung. Der Chat wird geladen, sobald der Server erreichbar ist. Neue Nachrichten werden dann gesendet."
                                         : "Keine Verbindung. Angezeigt wird der zuletzt geladene Stand; neue Nachrichten werden gesendet, sobald der Server erreichbar ist.")
                                        : (error.serverMessage ?? error.message)
        }
        if let chat { drafts = await chat.outbox.allDrafts() }
        loaded = true
    }

    private func applyShotScene() {
        guard let shotScene, open == nil else { return }
        switch shotScene {
        case "chat-verlauf", "chat-anhang", "chat-diktat":
            open = conversations.first { $0.chatKind == .site } ?? conversations.first
        case "chat-gruppe":
            open = conversations.first { $0.chatKind == .crew } ?? conversations.first
        default: break
        }
    }
}

/// Zeile einer Unterhaltung: Bild, Titel, letzte Nachricht bzw. Entwurf, Zeit, ungelesen, stumm, Erwähnung.
private struct ConversationRow: View {
    let conversation: ChatConversation
    let draft: String?
    let typing: String?

    var body: some View {
        HStack(spacing: Space.m) {
            ChatAvatar(title: conversation.title, kind: conversation.chatKind, conversationId: conversation.id, imageVersion: conversation.imageUpdatedAt, size: 46)
            VStack(alignment: .leading, spacing: 2) {
                HStack(alignment: .firstTextBaseline, spacing: Space.xs) {
                    Text(conversation.title)
                        .font(conversation.unreadCount > 0 ? Typo.bodyStrong : Typo.body)
                        .foregroundStyle(Theme.textPrimary)
                        .lineLimit(1)
                    if conversation.chatKind == .announcement {
                        Image(systemName: "megaphone.fill").font(.caption2).foregroundStyle(Theme.textTertiary).accessibilityHidden(true)
                    }
                    Spacer(minLength: Space.s)
                    if let at = conversation.lastMessageAt {
                        Text(ChatTime.short(at))
                            .font(Typo.meta)
                            .foregroundStyle(conversation.unreadCount > 0 && !conversation.isMuted() ? Theme.accent : Theme.textSecondary)
                    }
                }
                HStack(alignment: .firstTextBaseline, spacing: Space.xs) {
                    preview
                        .font(Typo.subhead)
                        .lineLimit(2)
                    Spacer(minLength: Space.s)
                    if conversation.isMuted() {
                        Image(systemName: "bell.slash.fill").font(.caption).foregroundStyle(Theme.textTertiary).accessibilityLabel("stumm")
                    }
                    if conversation.mentioned, conversation.unreadCount > 0 {
                        Text("@")
                            .font(.caption.weight(.bold))
                            .foregroundStyle(.white)
                            .frame(width: 20, height: 20)
                            .background(Circle().fill(Theme.accentFill))
                            .accessibilityLabel("Sie wurden erwähnt")
                    }
                    if conversation.unreadCount > 0 {
                        Text("\(conversation.unreadCount)")
                            .font(.caption2.weight(.bold).monospacedDigit())
                            .foregroundStyle(.white)
                            .padding(.horizontal, 6)
                            .frame(minWidth: 20, minHeight: 20)
                            .background(Capsule().fill(conversation.isMuted() ? Theme.textTertiary : Theme.accentFill))
                    }
                }
            }
        }
        .padding(.vertical, 2)
        .contentShape(Rectangle())
        .accessibilityElement(children: .combine)
        .accessibilityValue(conversation.unreadCount > 0 ? "\(conversation.unreadCount) ungelesen" : "")
    }

    @ViewBuilder
    private var preview: some View {
        if let typing {
            Text("\(typing) schreibt …").foregroundStyle(Theme.accent)
        } else if let draft, !draft.isEmpty {
            (Text("Entwurf: ").foregroundStyle(Theme.danger) + Text(draft).foregroundStyle(Theme.textSecondary))
        } else {
            Text(previewText).foregroundStyle(Theme.textSecondary)
        }
    }

    private var previewText: String {
        guard let text = conversation.lastMessageText, !text.isEmpty else { return "Noch keine Nachrichten" }
        if conversation.isGroup, let sender = conversation.lastMessageSender { return "\(sender): \(text)" }
        return text
    }
}

/// Bild einer Unterhaltung: Gruppenbild, sonst Symbol der Art bzw. Initialen.
struct ChatAvatar: View {
    let title: String
    let kind: ChatKind
    var conversationId: UUID?
    /// Stand des Gruppenbilds; `nil` = kein Bild.
    var imageVersion: Date?
    var size: CGFloat = 40
    @Environment(AppModel.self) private var model
    @State private var image: UIImage?

    var body: some View {
        ZStack {
            Circle().fill(Theme.accentSoft)
            if let image {
                Image(uiImage: image).resizable().scaledToFill()
            } else if kind == .direct {
                Text(LocalUser.initials(of: title.filter { $0.isLetter || $0 == " " }))
                    .font(.system(size: size * 0.36, weight: .semibold, design: .rounded))
            } else {
                Image(systemName: kind.symbol).font(.system(size: size * 0.36, weight: .semibold))
            }
        }
        .foregroundStyle(Theme.accent)
        .frame(width: size, height: size)
        .clipShape(Circle())
        .accessibilityHidden(true)
        .task(id: imageVersion) {
            guard let conversationId, let imageVersion, kind != .direct, let chat = model.active?.chat else {
                image = nil
                return
            }
            image = await chat.groupImage(conversationId, version: imageVersion)
        }
    }
}

enum ChatTime {
    /// Heute: Uhrzeit, gestern: „Gestern“, sonst Datum.
    static func short(_ date: Date) -> String {
        let calendar = Calendar.current
        if calendar.isDateInToday(date) { return date.shortTime }
        if calendar.isDateInYesterday(date) { return "Gestern" }
        return date.formatted(.dateTime.day().month(.twoDigits))
    }

    static func day(_ date: Date) -> String {
        let calendar = Calendar.current
        if calendar.isDateInToday(date) { return "Heute" }
        if calendar.isDateInYesterday(date) { return "Gestern" }
        return date.formatted(.dateTime.weekday(.wide).day().month(.wide))
    }
}

/// Sprechblasen-Symbol mit Zähler für die Werkzeugleiste (nur wenn das Modul aktiv ist).
struct ChatToolbarButton: View {
    let count: Int
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            ZStack(alignment: .topTrailing) {
                Image(systemName: "bubble.left.and.bubble.right")
                    .font(.system(size: 15, weight: .medium))
                    .foregroundStyle(Theme.accent)
                    .frame(width: 24, height: 20)
                    .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: count > 0 ? .bottomLeading : .center)
                if let text = UnreadBadge.text(count) { CountBadge(text: text) }
            }
            .frame(width: count > 0 ? 34 : 26, height: 22)
            .contentShape(Rectangle())
        }
        .accessibilityLabel(count > 0 ? "Chat, \(count) ungelesen" : "Chat")
        .accessibilityIdentifier("chatButton")
    }
}

/// Datenschutzhinweis beim ersten Öffnen (und nach einer Änderung des Hinweises).
struct ChatPrivacyNotice: View {
    let retentionDays: Int?
    let accept: () -> Void

    static func key(_ userId: UUID?) -> String { "chat.privacyNotice.\(userId?.uuidString.lowercased() ?? "-")" }

    static func isAcknowledged(userId: UUID?, version: Int) -> Bool {
        UserDefaults.standard.integer(forKey: key(userId)) >= version
    }

    static func acknowledge(userId: UUID?, version: Int) {
        UserDefaults.standard.set(version, forKey: key(userId))
    }

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: Space.l) {
                    Image(systemName: "lock.shield")
                        .font(.system(size: 44, weight: .regular))
                        .foregroundStyle(Theme.accent)
                        .frame(maxWidth: .infinity)
                        .padding(.top, Space.l)
                    Text("Chat im Betrieb").font(.title2.weight(.semibold)).frame(maxWidth: .infinity)
                    point("server.rack", "Auf dem Server Ihres Betriebs", "Nachrichten, Fotos und Dokumente liegen verschlüsselt auf dem eigenen Server und nicht bei einem Messenger-Anbieter.")
                    point("calendar.badge.clock", "Löschfrist",
                          retentionDays.map { "Inhalte werden nach \($0) Tagen automatisch endgültig gelöscht." } ?? "Inhalte werden nach der im Betrieb eingestellten Frist automatisch gelöscht.")
                    point("bell.badge", "Pushnachrichten", "Mitteilungen zeigen Absender und Chat. Der Text erscheint nur, wenn der Betrieb und Sie die Vorschau erlauben.")
                    point("mic.slash", "Keine Sprachnachrichten", "Das Mikrofon dient nur dem Diktat: Die Erkennung läuft auf dem Gerät, die Aufnahme wird nie gespeichert oder gesendet.")
                    point("person.badge.shield.checkmark", "Ihre Rechte", "Auskunft und Löschung Ihrer Chatdaten über die Verwaltung. Der Chat dient der Arbeit, nicht der Leistungs- oder Verhaltenskontrolle (Betriebsvereinbarung).")
                }
                .padding(.horizontal, Space.xl)
                .padding(.bottom, Space.xl)
                .frame(maxWidth: 560)
                .frame(maxWidth: .infinity)
            }
            .safeAreaInset(edge: .bottom) {
                Button("Verstanden", action: accept)
                    .buttonStyle(EstrichButtonStyle(rank: .primary, fullWidth: true))
                    .padding(.horizontal, Space.xl)
                    .padding(.vertical, Space.m)
                    .frame(maxWidth: 560)
                    .accessibilityIdentifier("chatPrivacyAccept")
            }
        }
    }

    private func point(_ symbol: String, _ title: String, _ text: String) -> some View {
        HStack(alignment: .top, spacing: Space.m) {
            Image(systemName: symbol).font(.title3).foregroundStyle(Theme.accent).frame(width: 30)
            VStack(alignment: .leading, spacing: 2) {
                Text(title).font(Typo.bodyStrong)
                Text(text).font(Typo.subhead).foregroundStyle(Theme.textSecondary).fixedSize(horizontal: false, vertical: true)
            }
        }
    }
}

/// Neuer Chat: Person wählen (nur Betriebsangehörige mit Chat-Recht) oder – mit `chat.admin` – eine Gruppe anlegen.
private struct NewChatSheet: View {
    let canCreateGroups: Bool
    let opened: (ChatConversation) -> Void
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var contacts: [ChatMember] = []
    @State private var search = ""
    @State private var loaded = false
    @State private var failure: String?
    @State private var busy: UUID?
    @State private var createsGroup = false

    var body: some View {
        NavigationStack {
            List {
                if let failure {
                    NoticeLine(tone: .danger, text: failure)
                        .listRowInsets(EdgeInsets())
                        .listRowBackground(Color.clear)
                }
                if canCreateGroups, search.isEmpty {
                    Section {
                        Button { createsGroup = true } label: {
                            Label("Neue Gruppe", systemImage: "person.3")
                                .font(Typo.bodyStrong)
                                .foregroundStyle(Theme.accent)
                        }
                        .accessibilityIdentifier("newGroupButton")
                    } footer: {
                        Text("Gruppe, Kolonnen-, Baustellen- oder Ankündigungsgruppe. Mitglieder wählen Sie aus der Liste aus. Telefonnummern sind nicht erforderlich.")
                    }
                }
                Section {
                    ForEach(filtered) { person in
                        Button {
                            Task { await start(person) }
                        } label: {
                            HStack(spacing: Space.m) {
                                Avatar(name: person.displayName, size: 34)
                                VStack(alignment: .leading, spacing: 0) {
                                    Text(person.displayName).font(Typo.body).foregroundStyle(Theme.textPrimary)
                                    if let role = person.role { Text(role).font(Typo.meta).foregroundStyle(Theme.textSecondary) }
                                }
                                Spacer()
                                if busy == person.id { ProgressView() }
                            }
                            .contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                        .disabled(busy != nil)
                    }
                    if loaded, filtered.isEmpty, failure == nil {
                        Text(search.isEmpty ? "Keine Personen verfügbar." : "Keine Treffer.")
                            .font(Typo.subhead)
                            .foregroundStyle(Theme.textSecondary)
                    }
                }
            }
            .overlay { if !loaded { ProgressView() } }
            .searchable(text: $search, prompt: "Person suchen")
            .navigationTitle("Neuer Chat")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Abbrechen") { dismiss() } }
            }
            .navigationDestination(isPresented: $createsGroup) {
                ChatGroupEditor(mode: .create) { conversation in
                    if let conversation { opened(conversation) }
                }
            }
            .task { await load() }
        }
    }

    private var filtered: [ChatMember] {
        let term = search.trimmingCharacters(in: .whitespaces)
        let list = contacts.filter { $0.id != model.active?.user.id }
        return term.isEmpty ? list : list.filter { $0.displayName.localizedCaseInsensitiveContains(term) }
    }

    private func load() async {
        guard let client = model.active?.client else { return }
        do throws(APIError) {
            contacts = try await client.chatContacts().sorted { $0.displayName.localizedCompare($1.displayName) == .orderedAscending }
        } catch {
            failure = error.serverMessage ?? error.message
        }
        loaded = true
    }

    private func start(_ person: ChatMember) async {
        guard let client = model.active?.client else { return }
        busy = person.id
        do throws(APIError) {
            opened(try await client.startDirectChat(with: person.id))
        } catch {
            failure = error.serverMessage ?? error.message
        }
        busy = nil
    }
}
