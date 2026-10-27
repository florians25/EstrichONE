import EstrichCore
import SwiftUI

/// Chat zwischen Büro und Mitarbeitern (Modul „Chat“): Unterhaltungen wie in einem Messenger, nur Text.
/// Nachrichten kommen direkt vom Server und bleiben nur im Arbeitsspeicher – auf dem Gerät wird nichts abgelegt.
struct ChatListView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.shotScene) private var shotScene
    @State private var conversations: [ChatConversation] = []
    @State private var loaded = false
    @State private var failure: String?
    @State private var open: ChatConversation?

    var body: some View {
        List {
            if let failure {
                Section {
                    NoticeLine(tone: .neutral, text: failure, symbol: "wifi.slash")
                        .listRowInsets(EdgeInsets())
                        .listRowBackground(Color.clear)
                }
            }
            if loaded, conversations.isEmpty, failure == nil {
                Section {
                    EmptyStateView(symbol: "bubble.left.and.bubble.right", title: "Noch keine Unterhaltungen",
                                   message: "Sobald das Büro oder Ihre Kolonne schreibt, erscheint die Unterhaltung hier.")
                }
            }
            Section {
                ForEach(conversations) { conversation in
                    Button { open = conversation } label: { ConversationRow(conversation: conversation) }
                        .buttonStyle(.plain)
                        .accessibilityIdentifier("chatConversation")
                }
            } footer: {
                if !conversations.isEmpty {
                    Text("Nur Text. Nachrichten werden verschlüsselt auf dem Server Ihres Betriebs gespeichert und nach der eingestellten Frist gelöscht.")
                }
            }
        }
        .listStyle(.insetGrouped)
        .overlay { if !loaded { ProgressView() } }
        .navigationTitle("Chat")
        .navigationBarTitleDisplayMode(.inline)
        .navigationDestination(item: $open) { conversation in
            ChatConversationView(conversationId: conversation.id, title: conversation.title, isGroup: conversation.isGroup)
        }
        .task(id: model.active?.sync?.revision) { await load() }
        .task {
            // Liste auffrischen, solange sie sichtbar ist.
            while !Task.isCancelled {
                try? await Task.sleep(for: .seconds(15))
                await load()
            }
        }
        .refreshable { await load() }
    }

    private func load() async {
        guard let client = model.active?.client else { return }
        do throws(APIError) {
            conversations = try await client.chatConversations().sorted { ($0.lastMessageAt ?? .distantPast) > ($1.lastMessageAt ?? .distantPast) }
            failure = nil
            if shotScene == "chat-verlauf", open == nil { open = conversations.first }
        } catch {
            failure = error == .offline ? "Ohne Verbindung – der Chat lädt, sobald der Server erreichbar ist." : (error.serverMessage ?? error.message)
        }
        loaded = true
    }
}

/// Zeile einer Unterhaltung: Avatar, Titel, letzte Nachricht, Zeit und ungelesene Nachrichten.
private struct ConversationRow: View {
    let conversation: ChatConversation

    var body: some View {
        HStack(spacing: Space.m) {
            ZStack {
                Circle().fill(Theme.accentSoft)
                if conversation.isGroup {
                    Image(systemName: "person.3.fill").font(.system(size: 14, weight: .semibold))
                } else {
                    Text(LocalUser.initials(of: conversation.title)).font(.system(size: 15, weight: .semibold, design: .rounded))
                }
            }
            .foregroundStyle(Theme.accent)
            .frame(width: 40, height: 40)
            .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 2) {
                HStack(alignment: .firstTextBaseline) {
                    Text(conversation.title)
                        .font(conversation.unreadCount > 0 ? Typo.bodyStrong : Typo.body)
                        .foregroundStyle(Theme.textPrimary)
                        .lineLimit(1)
                    Spacer(minLength: Space.s)
                    if let at = conversation.lastMessageAt {
                        Text(ChatTime.short(at))
                            .font(Typo.meta)
                            .foregroundStyle(conversation.unreadCount > 0 ? Theme.accent : Theme.textSecondary)
                    }
                }
                HStack(alignment: .firstTextBaseline) {
                    Text(preview)
                        .font(Typo.subhead)
                        .foregroundStyle(Theme.textSecondary)
                        .lineLimit(2)
                    Spacer(minLength: Space.s)
                    if conversation.unreadCount > 0 {
                        Text("\(conversation.unreadCount)")
                            .font(.caption2.weight(.bold).monospacedDigit())
                            .foregroundStyle(.white)
                            .padding(.horizontal, 6)
                            .frame(minWidth: 20, minHeight: 20)
                            .background(Capsule().fill(Theme.accentFill))
                    }
                }
            }
        }
        .padding(.vertical, 2)
        .contentShape(Rectangle())
        .accessibilityElement(children: .combine)
        .accessibilityValue(conversation.unreadCount > 0 ? "\(conversation.unreadCount) ungelesen" : "")
    }

    private var preview: String {
        guard let text = conversation.lastMessageText else { return "Noch keine Nachrichten" }
        if conversation.isGroup, let sender = conversation.lastMessageSender { return "\(sender): \(text)" }
        return text
    }
}

/// Verlauf einer Unterhaltung mit Eingabezeile. Eigene Nachrichten rechts in Markenblau, andere links; je Nachricht
/// Uhrzeit und Zustand (gesendet ✓, zugestellt ✓✓, gelesen ✓✓ blau). Neue Nachrichten werden alle 4 Sekunden abgeholt.
struct ChatConversationView: View {
    let conversationId: UUID
    var title: String?
    var isGroup = false

    @Environment(AppModel.self) private var model
    @Environment(\.shotScene) private var shotScene
    @State private var messages: [ChatMessage] = []
    @State private var pending: [ChatMessage] = []
    @State private var draft = ""
    @State private var loaded = false
    @State private var failure: String?
    @State private var resolvedTitle: String?
    @FocusState private var focused: Bool

    private var userId: UUID? { model.active?.user.id }
    private var all: [ChatMessage] { messages + pending }

    var body: some View {
        ScrollViewReader { proxy in
            ScrollView {
                LazyVStack(spacing: 2) {
                    if loaded, all.isEmpty {
                        Text("Schreiben Sie die erste Nachricht.")
                            .font(Typo.subhead)
                            .foregroundStyle(Theme.textSecondary)
                            .padding(.top, Space.xxl)
                    }
                    ForEach(Array(all.enumerated()), id: \.element.id) { index, message in
                        if showsDay(at: index) {
                            Text(ChatTime.day(message.createdAt))
                                .font(Typo.labelStrong)
                                .foregroundStyle(Theme.textSecondary)
                                .padding(.horizontal, Space.s + 2)
                                .padding(.vertical, 4)
                                .background(Theme.fill, in: Capsule())
                                .padding(.vertical, Space.s)
                        }
                        MessageBubble(message: message, isMine: message.senderId == userId,
                                      showsSender: isGroup && message.senderId != userId && startsRun(at: index),
                                      retry: message.state == .failed ? { Task { await resend(message) } } : nil)
                            .padding(.top, startsRun(at: index) ? Space.s : 0)
                            .id(message.id)
                    }
                    Color.clear.frame(height: 1).id("end")
                }
                .padding(.horizontal, Space.m)
                .padding(.vertical, Space.s)
                .frame(maxWidth: 760)
                .frame(maxWidth: .infinity)
            }
            .scrollDismissesKeyboard(.interactively)
            .background(Theme.canvas)
            .overlay { if !loaded { ProgressView() } }
            .safeAreaInset(edge: .top, spacing: 0) {
                if let failure {
                    NoticeLine(tone: .neutral, text: failure, symbol: "wifi.slash")
                        .padding(.horizontal, Space.m)
                        .padding(.vertical, Space.xs)
                }
            }
            .safeAreaInset(edge: .bottom, spacing: 0) { composer }
            .onChange(of: all.count) { _, _ in
                withAnimation(.snappy) { proxy.scrollTo("end", anchor: .bottom) }
            }
            .task {
                await load()
                proxy.scrollTo("end", anchor: .bottom)
                while !Task.isCancelled {
                    try? await Task.sleep(for: .seconds(4))
                    await load()
                }
            }
        }
        .navigationTitle(resolvedTitle ?? title ?? "Chat")
        .navigationBarTitleDisplayMode(.inline)
        .task { await resolveTitle() }
        .onAppear { if shotScene == "chat-verlauf" { draft = "Alles klar, bis morgen!" } }
    }

    private var composer: some View {
        HStack(alignment: .bottom, spacing: Space.s) {
            TextField("Nachricht", text: $draft, axis: .vertical)
                .lineLimit(1...5)
                .font(Typo.body)
                .focused($focused)
                .padding(.horizontal, Space.m)
                .padding(.vertical, 8)
                .background(Theme.surface, in: RoundedRectangle(cornerRadius: 18, style: .continuous))
                .overlay(RoundedRectangle(cornerRadius: 18, style: .continuous).strokeBorder(Theme.separator, lineWidth: 0.5))
                .accessibilityIdentifier("chatInput")
            Button {
                Task { await send() }
            } label: {
                Image(systemName: "arrow.up")
                    .font(.system(size: 15, weight: .bold))
                    .foregroundStyle(.white)
                    .frame(width: 34, height: 34)
                    .background(Circle().fill(ChatLimits.clean(draft) == nil ? Theme.fillStrong : Theme.accentFill))
            }
            .disabled(ChatLimits.clean(draft) == nil)
            .padding(.bottom, 1)
            .accessibilityLabel("Senden")
            .accessibilityIdentifier("chatSend")
        }
        .padding(.horizontal, Space.m)
        .padding(.vertical, Space.s)
        .background(.bar)
    }

    private func showsDay(at index: Int) -> Bool {
        guard index > 0 else { return true }
        return !Calendar.current.isDate(all[index].createdAt, inSameDayAs: all[index - 1].createdAt)
    }

    /// Erste Nachricht einer Folge desselben Absenders (Abstand, Name in Gruppen).
    private func startsRun(at index: Int) -> Bool {
        guard index > 0 else { return true }
        let previous = all[index - 1], current = all[index]
        return previous.senderId != current.senderId || current.createdAt.timeIntervalSince(previous.createdAt) > 600 || showsDay(at: index)
    }

    private func load() async {
        guard let client = model.active?.client else { return }
        do throws(APIError) {
            let fresh = try await client.chatMessages(conversationId)
            let hadUnread = fresh.contains { $0.senderId != userId } && fresh.last?.id != messages.last?.id
            messages = fresh
            pending.removeAll { item in fresh.contains { $0.id == item.id } }
            failure = nil
            if hadUnread || !loaded {
                try? await client.markChatRead(conversationId)
                await model.active?.sync?.refreshChat()
            }
        } catch {
            failure = error == .offline ? "Ohne Verbindung – Nachrichten werden geladen, sobald der Server erreichbar ist." : (error.serverMessage ?? error.message)
        }
        loaded = true
    }

    private func resolveTitle() async {
        guard title == nil, let client = model.active?.client else { return }
        resolvedTitle = (try? await client.chatConversations())?.first { $0.id == conversationId }?.title
    }

    private func send() async {
        guard let text = ChatLimits.clean(draft), let userId else { return }
        draft = ""
        let local = ChatMessage(id: UUID(), conversationId: conversationId, senderId: userId, senderName: model.active?.user.displayName,
                                text: text, createdAt: .now, state: .sending)
        pending.append(local)
        await deliver(local)
    }

    private func resend(_ message: ChatMessage) async {
        guard let index = pending.firstIndex(where: { $0.id == message.id }) else { return }
        pending[index].state = .sending
        await deliver(pending[index])
    }

    /// Senden mit der lokalen ID als `clientMessageId`: Wiederholen erzeugt keine doppelte Nachricht.
    private func deliver(_ message: ChatMessage) async {
        guard let client = model.active?.client else { return }
        do throws(APIError) {
            let stored = try await client.sendChatMessage(message.text, to: conversationId, clientMessageId: message.id)
            pending.removeAll { $0.id == message.id }
            if !messages.contains(where: { $0.id == stored.id }) { messages.append(stored) }
        } catch {
            if let index = pending.firstIndex(where: { $0.id == message.id }) { pending[index].state = .failed }
            failure = error == .offline ? "Nicht gesendet – keine Verbindung. Tippen Sie auf die Nachricht, um es erneut zu versuchen."
                                        : (error.serverMessage ?? error.message)
        }
    }
}

/// Sprechblase mit Zeit und Zustand. Abgerundet wie im Messenger, die Ecke zum Absender etwas spitzer.
private struct MessageBubble: View {
    let message: ChatMessage
    let isMine: Bool
    let showsSender: Bool
    var retry: (() -> Void)?

    var body: some View {
        HStack {
            if isMine { Spacer(minLength: 48) }
            VStack(alignment: isMine ? .trailing : .leading, spacing: 2) {
                if showsSender, let name = message.senderName {
                    Text(name)
                        .font(Typo.labelStrong)
                        .foregroundStyle(Theme.accent)
                        .padding(.horizontal, Space.m)
                }
                VStack(alignment: .trailing, spacing: 2) {
                    Text(message.text)
                        .font(Typo.body)
                        .foregroundStyle(isMine ? Theme.onAccent : Theme.textPrimary)
                        .fixedSize(horizontal: false, vertical: true)
                        .textSelection(.enabled)
                    HStack(spacing: 3) {
                        Text(message.createdAt.formatted(date: .omitted, time: .shortened))
                        if isMine { stateMark }
                    }
                    .font(.caption2.monospacedDigit())
                    .foregroundStyle(isMine ? Theme.onAccent.opacity(0.75) : Theme.textSecondary)
                }
                .padding(.horizontal, Space.m)
                .padding(.vertical, 7)
                .background(isMine ? Theme.accentFill : Theme.surface, in: UnevenRoundedRectangle(
                    topLeadingRadius: 18, bottomLeadingRadius: isMine ? 18 : 6, bottomTrailingRadius: isMine ? 6 : 18, topTrailingRadius: 18,
                    style: .continuous))
                .opacity(message.state == .sending ? 0.7 : 1)
                .onTapGesture { retry?() }
                if message.state == .failed {
                    Label("Nicht gesendet – tippen zum Wiederholen", systemImage: "exclamationmark.circle.fill")
                        .font(Typo.label)
                        .foregroundStyle(Theme.danger)
                }
            }
            if !isMine { Spacer(minLength: 48) }
        }
        .accessibilityElement(children: .combine)
        .accessibilityLabel("\(isMine ? "Ich" : (message.senderName ?? "")): \(message.text)")
        .accessibilityValue(stateText)
    }

    @ViewBuilder
    private var stateMark: some View {
        switch message.state {
        case .sending: Image(systemName: "clock")
        case .sent: Image(systemName: "checkmark")
        case .delivered: DoubleCheck()
        case .read: DoubleCheck().foregroundStyle(Theme.ice)
        case .failed: Image(systemName: "exclamationmark.circle")
        }
    }

    private var stateText: String {
        guard isMine else { return "" }
        switch message.state {
        case .sending: return "wird gesendet"
        case .sent: return "gesendet"
        case .delivered: return "zugestellt"
        case .read: return "gelesen"
        case .failed: return "nicht gesendet"
        }
    }
}

/// Zwei versetzte Häkchen (zugestellt bzw. gelesen).
private struct DoubleCheck: View {
    var body: some View {
        ZStack(alignment: .leading) {
            Image(systemName: "checkmark")
            Image(systemName: "checkmark").offset(x: 4)
        }
        .padding(.trailing, 4)
    }
}

enum ChatTime {
    /// Heute: Uhrzeit, gestern: „Gestern“, sonst Datum.
    static func short(_ date: Date) -> String {
        let calendar = Calendar.current
        if calendar.isDateInToday(date) { return date.formatted(date: .omitted, time: .shortened) }
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
