import CoreLocation
import EstrichCore
import PhotosUI
import SwiftUI
import UIKit
import UniformTypeIdentifiers

/// Verlauf einer Unterhaltung mit Eingabezeile. Eigene Nachrichten rechts in Markenblau, andere links; je Nachricht
/// Uhrzeit und Zustand (wartet, gesendet ✓, zugestellt ✓✓, gelesen ✓✓ hell). Neues kommt über den Echtzeit-Kanal,
/// sonst alle 5 Sekunden. Ältere Nachrichten lädt das Hochscrollen nach.
struct ChatConversationView: View {
    let conversationId: UUID
    var title: String?
    var kind: ChatKind?
    /// Aus der Suche: zu dieser Nachricht springen.
    var focusMessage: UUID?

    @Environment(AppModel.self) private var model
    @Environment(\.shotScene) private var shotScene
    @Environment(\.openURL) private var openURL
    @Environment(\.colorScheme) private var colorScheme
    @State private var messages: [ChatMessage] = []
    @State private var hasMore = false
    @State private var loadingOlder = false
    @State private var detail: ChatConversationDetail?
    @State private var loaded = false
    @State private var failure: String?
    @State private var draft = ""
    @State private var draftReady = false
    @State private var replyTo: ChatMessage?
    @State private var editing: ChatMessage?
    @State private var picked: [PickedAttachment] = []
    @State private var location: ChatLocation?
    @State private var photoItems: [PhotosPickerItem] = []
    @State private var showsPhotoPicker = false
    @State private var showsCamera = false
    @State private var showsDocuments = false
    @State private var choosesBauakteAddress = false
    @State private var locating = false
    @State private var forwarding: ChatMessage?
    @State private var saving: SaveTarget?
    @State private var receipts: ChatMessage?
    @State private var viewing: ViewedAttachment?
    @State private var deleting: ChatMessage?
    @State private var showsInfo = false
    @State private var highlight: UUID?
    @State private var scrollTarget: UUID?
    @State private var toast: String?
    @State private var confirmsOverride = false
    @State private var dictation = Dictation()
    @State private var dictationBase = ""
    @FocusState private var focused: Bool

    private var chat: ChatCenter? { model.active?.chat }
    private var userId: UUID? { model.active?.user.id }
    private var sync: SyncController? { model.active?.sync }
    private var isReadOnly: Bool { sync?.isReadOnly == true }
    private var effectiveKind: ChatKind { detail?.chatKind ?? kind ?? .direct }
    private var isGroup: Bool { effectiveKind != .direct }
    private var canWrite: Bool { detail?.canWrite ?? true }
    private var editWindow: Int { sync?.chat?.editWindowMinutes ?? 15 }
    private var isAdmin: Bool { detail?.canManage == true || sync?.chat?.canAdmin == true }

    /// Verlauf plus wartende Nachrichten aus der Warteschlange.
    private var all: [ChatMessage] {
        let known = Set(messages.compactMap(\.clientMessageId)).union(messages.map(\.id))
        let waiting = (chat?.queued(in: conversationId) ?? [])
            .filter { !known.contains($0.id) }
            .map { $0.preview(senderId: userId, senderName: model.active?.user.displayName, sending: chat?.sending.contains($0.id) == true) }
        return messages + waiting
    }

    private var pinnedMessage: ChatMessage? { messages.last { $0.pinned && !$0.deleted } ?? detail?.pinned.last { !$0.deleted } }

    var body: some View {
        presenting(configured(scrollArea))
    }

    private var scrollArea: some View {
        ScrollViewReader { proxy in
            ScrollView { messageStack(proxy: proxy) }
            .scrollDismissesKeyboard(.interactively)
            .background(Theme.canvas)
            .overlay { if !loaded { ProgressView() } }
            .safeAreaInset(edge: .top, spacing: 0) { topBanners(proxy: proxy) }
            .safeAreaInset(edge: .bottom, spacing: 0) { composer }
            .onChange(of: all.count) { old, new in
                guard new > old, !loadingOlder else { return }
                withAnimation(.snappy) { proxy.scrollTo("end", anchor: .bottom) }
            }
            .onChange(of: scrollTarget) { _, target in
                guard let target else { return }
                withAnimation(.snappy) { proxy.scrollTo(target, anchor: .center) }
                flash(target)
                scrollTarget = nil
            }
            .task {
                await load(full: true)
                if let focusMessage {
                    await reveal(focusMessage)
                } else {
                    proxy.scrollTo("end", anchor: .bottom)
                }
            }
            .task(id: chat?.isLive) {
                // Sicherheitsnetz: mit Echtzeit-Kanal alle 30 Sekunden, sonst alle 5 Sekunden; jede 6. Runde vollständig
                // (Zustell-/Lesestand, Bearbeitungen, Reaktionen).
                var round = 0
                while !Task.isCancelled {
                    try? await Task.sleep(for: ChatPolling.conversationInterval(live: chat?.isLive == true))
                    round += 1
                    await load(full: round % 6 == 0)
                }
            }
        }
    }

    private func messageStack(proxy: ScrollViewProxy) -> some View {
        LazyVStack(spacing: 2) {
            if hasMore {
                ProgressView()
                    .padding(.vertical, Space.m)
                    .onAppear { Task { await loadOlder(proxy: proxy) } }
            }
            if loaded, all.isEmpty {
                Text(canWrite ? "Schreiben Sie die erste Nachricht." : "Noch keine Ankündigungen.")
                    .font(Typo.subhead)
                    .foregroundStyle(Theme.textSecondary)
                    .padding(.top, Space.xxl)
            }
            let list = all
            ForEach(Array(list.enumerated()), id: \.element.id) { index, message in
                if showsDay(at: index, in: list) {
                    Text(ChatTime.day(message.createdAt))
                        .font(Typo.labelStrong)
                        .foregroundStyle(Theme.textSecondary)
                        .padding(.horizontal, Space.s + 2)
                        .padding(.vertical, 4)
                        .background(Theme.fill, in: Capsule())
                        .padding(.vertical, Space.s)
                }
                bubble(message, startsRun: startsRun(at: index, in: list))
                    .padding(.top, startsRun(at: index, in: list) ? Space.s : 0)
                    .id(message.id)
            }
            if let name = chat?.typingName(in: conversationId) {
                TypingIndicator(name: name)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(.top, Space.xs)
            }
            Color.clear.frame(height: 1).id("end")
        }
        .padding(.horizontal, Space.m)
        .padding(.vertical, Space.s)
        .frame(maxWidth: 760)
        .frame(maxWidth: .infinity)
    }

    private func configured<Content: View>(_ content: Content) -> some View {
        content
        .navigationTitle("")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            ToolbarItem(placement: .principal) {
                header
                    .environment(\.colorScheme, colorScheme)
                    .contentShape(Rectangle())
                    .onTapGesture { showsInfo = true }
                    .accessibilityElement(children: .combine)
                    .accessibilityAddTraits(.isButton)
                    .accessibilityIdentifier("chatInfoButton")
                    .accessibilityHint("Info zur Unterhaltung")
            }
            ToolbarItem(placement: .primaryAction) {
                Button("Info", systemImage: "info.circle") { showsInfo = true }
            }
        }
        .navigationDestination(isPresented: $showsInfo) {
            ChatInfoView(conversationId: conversationId, fallbackTitle: title ?? "Chat", kind: effectiveKind) { target in
                showsInfo = false
                Task { await reveal(target) }
            } changed: {
                Task { await loadDetail() }
            }
        }
        .task { await loadDetail() }
        .task { await restoreDraft() }
        .onAppear {
            chat?.visibleConversation = conversationId
            dictation.onTranscript = { text in applyDictation(text) }
            applyShotScene()
        }
        .onDisappear {
            if chat?.visibleConversation == conversationId { chat?.visibleConversation = nil }
            dictation.stop()
            chat?.saveDraft(editing == nil ? draft : "", for: conversationId)
        }
        .onChange(of: draft) { _, text in
            guard draftReady, editing == nil else { return }
            chat?.saveDraft(text, for: conversationId)
            if !text.isEmpty, !dictation.isActive { chat?.reportTyping(in: conversationId) }
        }
        .onChange(of: chat?.eventCount) { _, _ in
            guard let event = chat?.lastEvent else { return }
            if event.conversationId == nil || event.conversationId == conversationId {
                if event.kind == .messageChanged, let id = event.messageId {
                    // Bearbeitet, gelöscht, Reaktion, angeheftet: nur diese Nachricht neu holen.
                    Task { await refreshMessage(id) }
                } else {
                    Task { await load(full: event.kind != .message) }
                }
            }
            if event.kind == .conversation, event.conversationId == conversationId { Task { await loadDetail() } }
        }
        .photosPicker(isPresented: $showsPhotoPicker, selection: $photoItems, maxSelectionCount: ChatLimits.maxAttachments, matching: .images)
        .onChange(of: photoItems) { _, items in
            guard !items.isEmpty else { return }
            Task { await importPhotos(items) }
        }
    }

    private func presenting<Content: View>(_ content: Content) -> some View {
        content
        .fullScreenCover(isPresented: $showsCamera) {
            ChatCameraPicker { image in
                showsCamera = false
                if let image { Task { await addCameraPhoto(image) } }
            }
            .ignoresSafeArea()
        }
        .fileImporter(isPresented: $showsDocuments, allowedContentTypes: [.pdf, .image, .plainText, .commaSeparatedText, .spreadsheet, .presentation, .item],
                      allowsMultipleSelection: true) { result in
            if case let .success(urls) = result { importDocuments(urls) }
        }
        .sheet(isPresented: $choosesBauakteAddress) {
            BauaktePickerSheet(title: "Baustellenadresse teilen", preferred: detail?.bauakteId) { bauakte in
                choosesBauakteAddress = false
                Task { await shareAddress(of: bauakte) }
            }
        }
        .sheet(item: $forwarding) { message in
            ChatForwardSheet(message: message, excluding: conversationId) { count in
                forwarding = nil
                toast = count == 1 ? "Weitergeleitet" : "An \(count) Chats weitergeleitet"
            }
        }
        .sheet(item: $saving) { target in
            BauaktePickerSheet(title: target.attachment.isPhoto ? "Foto in Bauakte übernehmen" : "Dokument in Bauakte übernehmen",
                               preferred: detail?.bauakteId) { bauakte in
                saving = nil
                Task { await save(target, to: bauakte) }
            }
        }
        .sheet(item: $receipts) { message in
            ChatReadReceiptsSheet(message: message, members: detail?.members ?? [], readReceipts: detail?.readReceipts ?? true, userId: userId)
                .presentationDetents([.medium, .large])
        }
        .fullScreenCover(item: $viewing) { viewed in
            ChatAttachmentViewer(attachment: viewed.attachment, message: viewed.message,
                                 canSaveToBauakte: model.active?.service != nil && !isReadOnly) {
                viewing = nil
                saving = SaveTarget(message: viewed.message, attachment: viewed.attachment)
            }
        }
        .confirmationDialog("Für alle löschen?", isPresented: Binding(get: { deleting != nil }, set: { if !$0 { deleting = nil } }),
                            titleVisibility: .visible, presenting: deleting) { message in
            Button("Für alle löschen", role: .destructive) { Task { await delete(message) } }
            Button("Abbrechen", role: .cancel) {}
        } message: { _ in
            Text("Die Nachricht wird bei allen Mitgliedern durch „Nachricht gelöscht“ ersetzt. Anhänge werden vom Server entfernt.")
        }
        .readOnlyOverrideDialog(isPresented: $confirmsOverride, sync: sync)
        .toast($toast)
    }

    // MARK: Kopf und Hinweise

    private var header: some View {
        HStack(spacing: Space.s) {
            ChatAvatar(title: detail?.title ?? title ?? "Chat", kind: effectiveKind, conversationId: conversationId, imageVersion: detail?.imageUpdatedAt, size: 30)
            VStack(alignment: .leading, spacing: 0) {
                // Feste Farben: Inhalte in der Werkzeugleiste passen sich sonst dem Hintergrund unter dem Glas an.
                Text(detail?.title ?? title ?? "Chat")
                    .font(Typo.subheadStrong)
                    .foregroundStyle(colorScheme == .dark ? Color.white : Color.black)
                    .lineLimit(1)
                Text(subtitle)
                    .font(.caption2)
                    .foregroundStyle(chat?.typingName(in: conversationId) != nil ? Theme.accent
                                     : (colorScheme == .dark ? Color(hex: 0xAEB5C0) : Color(hex: 0x4A5361)))
                    .lineLimit(1)
            }
        }
        .frame(maxWidth: 260)
    }

    private var subtitle: String {
        if let name = chat?.typingName(in: conversationId) { return isGroup ? "\(name) schreibt …" : "schreibt …" }
        guard let detail else { return isGroup ? effectiveKind.title : "Tippen für Info" }
        if detail.mutedUntil.map({ $0 > Date() }) == true { return "Stummgeschaltet" }
        if isGroup { return detail.members.map { $0.displayName.split(separator: " ").first.map(String.init) ?? $0.displayName }.joined(separator: ", ") }
        return "Tippen für Info"
    }

    @ViewBuilder
    private func topBanners(proxy: ScrollViewProxy) -> some View {
        VStack(spacing: 0) {
            if let failure {
                NoticeLine(tone: .neutral, text: failure, symbol: "wifi.slash")
                    .padding(.horizontal, Space.m)
                    .padding(.vertical, Space.xs)
            }
            if let pinned = pinnedMessage {
                Button { scrollTarget = pinned.id } label: {
                    HStack(spacing: Space.s) {
                        Image(systemName: "pin.fill").foregroundStyle(Theme.accent)
                        VStack(alignment: .leading, spacing: 0) {
                            Text("Angeheftet").font(Typo.labelStrong).foregroundStyle(Theme.accent)
                            Text(pinned.summary).font(Typo.meta).foregroundStyle(Theme.textPrimary).lineLimit(1)
                        }
                        Spacer(minLength: 0)
                    }
                    .padding(.horizontal, Space.l)
                    .padding(.vertical, Space.s)
                    .background(.bar)
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .accessibilityIdentifier("chatPinnedBanner")
                Divider()
            }
        }
    }

    private func bubble(_ message: ChatMessage, startsRun: Bool) -> some View {
        let mine = message.isMine(userId: userId)
        var actions = ChatBubbleActions()
        actions.reply = { message in startReply(message) }
        actions.react = { message, emoji in Task { await react(message, emoji) } }
        actions.edit = { message in startEditing(message) }
        actions.delete = { message in deleting = message }
        actions.pin = { message in Task { await pin(message) } }
        actions.forward = { message in forwarding = message }
        if model.active?.service != nil {
            actions.saveToBauakte = { message, attachment in saving = SaveTarget(message: message, attachment: attachment) }
        }
        actions.readReceipts = { message in receipts = message }
        actions.openAttachment = { message, attachment in viewing = ViewedAttachment(message: message, attachment: attachment) }
        actions.openQuote = { id in Task { await reveal(id) } }
        actions.retry = { message in Task { await chat?.retry(message.id) } }
        actions.discard = { message in Task { await chat?.discard(message.id) } }
        return MessageBubble(message: message, isMine: mine, isGroup: isGroup,
                             showsSender: isGroup && !mine && startsRun, highlighted: highlight == message.id,
                             members: detail?.members ?? [],
                             canEdit: message.canEdit(userId: userId, windowMinutes: editWindow),
                             canDelete: mine || isAdmin, canPin: canWrite || isAdmin, readOnly: isReadOnly || !canWriteNow,
                             actions: actions)
    }

    private var canWriteNow: Bool { canWrite }

    private func showsDay(at index: Int, in list: [ChatMessage]) -> Bool {
        guard index > 0 else { return true }
        return !Calendar.current.isDate(list[index].createdAt, inSameDayAs: list[index - 1].createdAt)
    }

    /// Erste Nachricht einer Folge desselben Absenders (Abstand, Name in Gruppen).
    private func startsRun(at index: Int, in list: [ChatMessage]) -> Bool {
        guard index > 0 else { return true }
        let previous = list[index - 1], current = list[index]
        return previous.senderId != current.senderId || previous.isSystem || current.createdAt.timeIntervalSince(previous.createdAt) > 600
            || showsDay(at: index, in: list)
    }

    // MARK: Eingabe

    @ViewBuilder
    private var composer: some View {
        VStack(spacing: 0) {
            Divider()
            if isReadOnly {
                NoticeLine(tone: .warning, text: "Nur Ansicht: Der Server ist seit über 5 Minuten nicht erreichbar. Schreiben ist gesperrt, bis die Verbindung zurück ist.",
                           symbol: "lock", actionTitle: "Trotzdem", action: { confirmsOverride = true })
                    .padding(Space.m)
            } else if !canWrite {
                Label("Nur Admins können in dieser Gruppe schreiben.", systemImage: "megaphone")
                    .font(Typo.subhead)
                    .foregroundStyle(Theme.textSecondary)
                    .frame(maxWidth: .infinity)
                    .padding(Space.l)
            } else {
                composerContent
            }
        }
        .background(.bar)
    }

    @ViewBuilder
    private var composerContent: some View {
        VStack(spacing: Space.s) {
            if let editing {
                ComposerBanner(symbol: "pencil", title: "Nachricht bearbeiten", text: editing.text) { cancelEditing() }
            } else if let replyTo {
                ComposerBanner(symbol: "arrowshape.turn.up.left.fill", title: replyTo.isMine(userId: userId) ? "Antwort an mich" : "Antwort an \(replyTo.senderName ?? "")",
                               text: replyTo.summary) { self.replyTo = nil }
            }
            if !picked.isEmpty || location != nil { attachmentStrip }
            if let query = ChatMentions.pendingQuery(in: draft), isGroup {
                let suggestions = ChatMentions.suggestions(for: query, members: detail?.members ?? [], excluding: userId)
                if !suggestions.isEmpty {
                    ScrollView(.horizontal, showsIndicators: false) {
                        HStack(spacing: Space.s) {
                            ForEach(suggestions) { member in
                                Button { draft = ChatMentions.complete(draft, with: member) } label: {
                                    HStack(spacing: Space.xs) {
                                        Avatar(name: member.displayName, size: 22)
                                        Text(member.displayName).font(Typo.subheadStrong)
                                    }
                                    .padding(.horizontal, Space.s)
                                    .padding(.vertical, 5)
                                    .background(Theme.surface, in: Capsule())
                                }
                                .buttonStyle(.plain)
                            }
                        }
                    }
                }
            }
            if dictation.isActive {
                HStack(spacing: Space.s) {
                    Image(systemName: "waveform").symbolEffect(.variableColor.iterative, isActive: dictation.phase == .listening)
                        .foregroundStyle(Theme.danger)
                    Text(dictation.onDevice ? "Diktat läuft – Erkennung auf dem Gerät, nichts wird aufgezeichnet" : "Diktat läuft – nichts wird aufgezeichnet")
                        .font(Typo.label)
                        .foregroundStyle(Theme.textSecondary)
                    Spacer()
                    Button("Fertig") { dictation.stop() }.font(Typo.subheadStrong)
                }
                .accessibilityIdentifier("dictationActive")
            } else if let problem = dictation.problem {
                NoticeLine(tone: .neutral, text: problem, symbol: "mic.slash")
            }
            HStack(alignment: .bottom, spacing: Space.s) {
                if editing == nil { attachMenu }
                HStack(alignment: .bottom, spacing: 4) {
                    TextField(editing == nil ? "Nachricht" : "Text", text: $draft, axis: .vertical)
                        .lineLimit(1...6)
                        .font(Typo.body)
                        .focused($focused)
                        .padding(.leading, Space.m)
                        .padding(.vertical, 8)
                        .accessibilityIdentifier("chatInput")
                    if dictation.isSupported {
                        Button { toggleDictation() } label: {
                            Image(systemName: dictation.isActive ? "mic.fill" : "mic")
                                .font(.system(size: 17, weight: .medium))
                                .foregroundStyle(dictation.isActive ? Theme.danger : Theme.textSecondary)
                                .frame(width: 34, height: 34)
                                .contentShape(Rectangle())
                        }
                        .accessibilityLabel(dictation.isActive ? "Diktat beenden" : "Diktieren")
                        .accessibilityIdentifier("dictationButton")
                    }
                }
                .padding(.trailing, 2)
                .background(Theme.surface, in: RoundedRectangle(cornerRadius: 18, style: .continuous))
                .overlay(RoundedRectangle(cornerRadius: 18, style: .continuous)
                    .strokeBorder(dictation.isActive ? Theme.danger.opacity(0.6) : Theme.separator, lineWidth: dictation.isActive ? 1 : 0.5))
                Button {
                    Task { await send() }
                } label: {
                    Image(systemName: editing == nil ? "arrow.up" : "checkmark")
                        .font(.system(size: 15, weight: .bold))
                        .foregroundStyle(.white)
                        .frame(width: 34, height: 34)
                        .background(Circle().fill(canSend ? Theme.accentFill : Theme.fillStrong))
                }
                .disabled(!canSend)
                .padding(.bottom, 1)
                .accessibilityLabel(editing == nil ? "Senden" : "Änderung sichern")
                .accessibilityIdentifier("chatSend")
            }
        }
        .padding(.horizontal, Space.m)
        .padding(.vertical, Space.s)
    }

    private var canSend: Bool {
        if editing != nil { return ChatLimits.clean(draft) != nil && ChatLimits.clean(draft) != editing?.text }
        return ChatLimits.clean(draft) != nil || !picked.isEmpty || location != nil
    }

    private var attachMenu: some View {
        Menu {
            Button("Kamera", systemImage: "camera") { showsCamera = true }
                .disabled(!UIImagePickerController.isSourceTypeAvailable(.camera))
            Button("Fotos", systemImage: "photo.on.rectangle") { showsPhotoPicker = true }
            Button("Dokument", systemImage: "doc") { showsDocuments = true }
            Divider()
            Button("Mein Standort", systemImage: "location") { Task { await shareCurrentLocation() } }
            Button("Baustellenadresse", systemImage: "building.2") {
                if let id = detail?.bauakteId {
                    Task { await shareAddress(ofId: id) }
                } else {
                    choosesBauakteAddress = true
                }
            }
            .disabled(model.active?.service == nil)
        } label: {
            Group {
                if locating { ProgressView() } else { Image(systemName: "plus").font(.system(size: 18, weight: .semibold)) }
            }
            .foregroundStyle(Theme.accent)
            .frame(width: 34, height: 34)
            .contentShape(Rectangle())
        }
        .accessibilityLabel("Anhang")
        .accessibilityIdentifier("chatAttachButton")
    }

    private var attachmentStrip: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: Space.s) {
                ForEach(picked) { item in
                    ZStack(alignment: .topTrailing) {
                        Group {
                            if let preview = item.preview {
                                Image(uiImage: preview).resizable().scaledToFill()
                            } else {
                                VStack(spacing: 2) {
                                    Image(systemName: "doc.fill").font(.title2).foregroundStyle(Theme.accent)
                                    Text(item.name).font(.caption2).lineLimit(2).multilineTextAlignment(.center)
                                    Text(ChatFormat.size(Int64(item.data.count))).font(.caption2).foregroundStyle(Theme.textSecondary)
                                }
                                .padding(4)
                            }
                        }
                        .frame(width: 72, height: 72)
                        .background(Theme.fill)
                        .clipShape(RoundedRectangle(cornerRadius: 10, style: .continuous))
                        Button { picked.removeAll { $0.id == item.id } } label: {
                            Image(systemName: "xmark.circle.fill").font(.system(size: 20)).foregroundStyle(.white, .black.opacity(0.6))
                        }
                        .offset(x: 6, y: -6)
                        .accessibilityLabel("Anhang entfernen")
                    }
                }
                if let location {
                    ZStack(alignment: .topTrailing) {
                        VStack(spacing: 2) {
                            Image(systemName: "mappin.circle.fill").font(.title2).foregroundStyle(Theme.accent)
                            Text(location.label ?? "Standort").font(.caption2).lineLimit(2).multilineTextAlignment(.center)
                        }
                        .padding(4)
                        .frame(width: 96, height: 72)
                        .background(Theme.fill, in: RoundedRectangle(cornerRadius: 10, style: .continuous))
                        Button { self.location = nil } label: {
                            Image(systemName: "xmark.circle.fill").font(.system(size: 20)).foregroundStyle(.white, .black.opacity(0.6))
                        }
                        .offset(x: 6, y: -6)
                        .accessibilityLabel("Standort entfernen")
                    }
                }
            }
            .padding(.top, 6)
            .padding(.trailing, 6)
        }
    }

    private func toggleDictation() {
        if dictation.isActive {
            dictation.stop()
        } else {
            let trimmed = draft.trimmingCharacters(in: .whitespaces)
            dictationBase = trimmed.isEmpty ? "" : (draft.hasSuffix(" ") || draft.hasSuffix("\n") ? draft : draft + " ")
            focused = false
            dictation.toggle()
        }
    }

    private func applyDictation(_ text: String) {
        draft = String((dictationBase + text).prefix(ChatLimits.maxLength))
    }

    private func startReply(_ message: ChatMessage) {
        editing = nil
        replyTo = message
        focused = true
    }

    private func startEditing(_ message: ChatMessage) {
        replyTo = nil
        if editing == nil { chat?.saveDraft(draft, for: conversationId) }
        editing = message
        draft = message.text
        focused = true
    }

    private func cancelEditing() {
        editing = nil
        Task { await restoreDraft() }
    }

    private func restoreDraft() async {
        guard let chat else {
            draftReady = true
            return
        }
        if let pendingReply = PushCenter.shared.takePendingReply(for: conversationId) {
            draft = pendingReply
        } else if shotScene == nil || shotScene == "chat-diktat" {
            draft = await chat.draft(for: conversationId)
        }
        draftReady = true
    }

    // MARK: Anhänge

    private func importPhotos(_ items: [PhotosPickerItem]) async {
        photoItems = []
        for item in items.prefix(ChatLimits.maxAttachments - picked.count) {
            guard let raw = try? await item.loadTransferable(type: Data.self) else { continue }
            let jpeg = await Task.detached(priority: .userInitiated) { PhotoCompressor.prepare(raw) }.value
            picked.append(PickedAttachment.photo(jpeg))
        }
    }

    private func addCameraPhoto(_ image: UIImage) async {
        guard let raw = image.jpegData(compressionQuality: 0.9) else { return }
        let jpeg = await Task.detached(priority: .userInitiated) { PhotoCompressor.prepare(raw) }.value
        picked.append(PickedAttachment.photo(jpeg))
    }

    private func importDocuments(_ urls: [URL]) {
        for url in urls.prefix(ChatLimits.maxAttachments - picked.count) {
            let scoped = url.startAccessingSecurityScopedResource()
            defer { if scoped { url.stopAccessingSecurityScopedResource() } }
            guard let data = try? Data(contentsOf: url) else { continue }
            guard data.count <= ChatLimits.maxDocumentBytes else {
                toast = "„\(url.lastPathComponent)“ ist größer als 25 MB."
                continue
            }
            let type = UTType(filenameExtension: url.pathExtension)
            if type?.conforms(to: .image) == true, type != .pdf {
                // Bilder als Dokument gewählt: wie Fotos verkleinern.
                picked.append(PickedAttachment.photo(PhotoCompressor.prepare(data)))
            } else {
                picked.append(PickedAttachment(name: url.lastPathComponent, kind: "document",
                                               contentType: type?.preferredMIMEType ?? "application/octet-stream", data: data, preview: nil))
            }
        }
    }

    private func shareCurrentLocation() async {
        locating = true
        defer { locating = false }
        let provider = LocationProvider()
        do throws(LocationProvider.Failure) {
            let current = try await provider.currentLocation()
            let address = await provider.address(for: current)
            let line = [address?.street, [address?.postalCode, address?.city].compactMap { $0 }.joined(separator: " ")]
                .compactMap { $0 }.filter { !$0.isEmpty }.joined(separator: ", ")
            location = ChatLocation(latitude: current.coordinate.latitude, longitude: current.coordinate.longitude, label: "Mein Standort",
                                    address: line.isEmpty ? nil : line)
        } catch {
            toast = error == .denied ? "Standort nicht erlaubt (Einstellungen › EstrichONE › Standort)." : "Standort gerade nicht verfügbar."
        }
    }

    private func shareAddress(ofId id: UUID) async {
        guard let bauakte = try? await model.active?.service?.bauakte(id) else {
            choosesBauakteAddress = true
            return
        }
        await shareAddress(of: bauakte)
    }

    private func shareAddress(of bauakte: Bauakte) async {
        locating = true
        defer { locating = false }
        var latitude = bauakte.latitude, longitude = bauakte.longitude
        if latitude == nil || longitude == nil, let line = bauakte.addressLine,
           let placemark = try? await CLGeocoder().geocodeAddressString(line, in: nil, preferredLocale: Locale(identifier: "de_DE")).first,
           let coordinate = placemark.location?.coordinate {
            latitude = coordinate.latitude
            longitude = coordinate.longitude
        }
        guard let latitude, let longitude else {
            toast = "Für diese Baustelle ist keine Adresse mit Kartenposition hinterlegt."
            return
        }
        location = ChatLocation(latitude: latitude, longitude: longitude, label: bauakte.title, address: bauakte.addressLine)
    }

    // MARK: Laden

    private func loadDetail() async {
        guard let client = model.active?.client else { return }
        if let fresh = try? await client.chatConversation(conversationId) { detail = fresh }
    }

    private func load(full: Bool) async {
        guard let client = model.active?.client else { return }
        do throws(APIError) {
            let after = full ? nil : messages.last { $0.state != .queued }?.createdAt
            let page = try await client.chatPage(conversationId, limit: full ? max(50, min(messages.count, 200)) : 50, after: after)
            let hadUnread = page.items.contains { !$0.isMine(userId: userId) && !messages.map(\.id).contains($0.id) }
            if full {
                // Bereits nachgeladene ältere Nachrichten behalten.
                let oldest = page.items.first?.createdAt ?? .distantFuture
                let older = messages.filter { $0.createdAt < oldest }
                messages = older + page.items
                if older.isEmpty { hasMore = page.hasMore }
            } else {
                merge(page.items)
            }
            merge(chat?.confirmed.filter { $0.conversationId == conversationId || $0.conversationId == nil } ?? [])
            failure = nil
            if hadUnread || !loaded {
                try? await client.markChatRead(conversationId)
                await sync?.refreshChat()
            }
        } catch {
            if !loaded || error != .offline {
                failure = error == .offline ? "Ohne Verbindung – neue Nachrichten werden gesendet, sobald der Server erreichbar ist."
                                            : (error.serverMessage ?? error.message)
            }
        }
        loaded = true
    }

    private func merge(_ fresh: [ChatMessage]) {
        for message in fresh {
            if let index = messages.firstIndex(where: { $0.id == message.id }) {
                messages[index] = message
            } else {
                messages.append(message)
            }
        }
        messages.sort { $0.createdAt < $1.createdAt }
    }

    private func refreshMessage(_ id: UUID) async {
        guard let client = model.active?.client else { return }
        if let fresh = try? await client.chatMessage(id) {
            merge([fresh])
            if fresh.pinned != (detail?.pinned.contains { $0.id == id } ?? false) { await loadDetail() }
        }
    }

    private func loadOlder(proxy: ScrollViewProxy) async {
        guard hasMore, !loadingOlder, let client = model.active?.client, let first = messages.first else { return }
        loadingOlder = true
        defer { loadingOlder = false }
        do throws(APIError) {
            let page = try await client.chatPage(conversationId, limit: 50, before: first.createdAt)
            let known = Set(messages.map(\.id))
            messages = page.items.filter { !known.contains($0.id) } + messages
            hasMore = page.hasMore && !page.items.isEmpty
            proxy.scrollTo(first.id, anchor: .top)
        } catch {
            hasMore = false
            failure = error.serverMessage ?? error.message
        }
    }

    /// Zu einer Nachricht springen (Zitat, Suche, angeheftet) – ältere Seiten werden dafür nachgeladen.
    private func reveal(_ id: UUID) async {
        var rounds = 0
        while !messages.contains(where: { $0.id == id }), hasMore, rounds < 10, let client = model.active?.client, let first = messages.first {
            rounds += 1
            guard let page = try? await client.chatPage(conversationId, limit: 100, before: first.createdAt) else { break }
            let known = Set(messages.map(\.id))
            messages = page.items.filter { !known.contains($0.id) } + messages
            hasMore = page.hasMore && !page.items.isEmpty
        }
        if messages.contains(where: { $0.id == id }) {
            scrollTarget = id
        } else {
            toast = "Die Nachricht ist nicht mehr vorhanden."
        }
    }

    private func flash(_ id: UUID) {
        highlight = id
        Task {
            try? await Task.sleep(for: .seconds(1.6))
            if highlight == id { withAnimation { highlight = nil } }
        }
    }

    // MARK: Senden und Ändern

    private func send() async {
        guard let chat, canSend else { return }
        if let editing {
            await saveEdit(editing)
            return
        }
        let text = ChatLimits.clean(draft) ?? ""
        dictation.stop()
        let attachments = picked.map { ChatOutboxItem.Attachment(id: $0.id, kind: $0.kind, name: $0.name, contentType: $0.contentType,
                                                                 sizeBytes: Int64($0.data.count)) }
        var contents: [UUID: Data] = [:]
        for item in picked { contents[item.id] = item.data }
        let item = ChatOutboxItem(conversationId: conversationId, text: text, attachments: attachments, replyToId: replyTo?.id,
                                  replyToSender: replyTo?.senderName, replyToText: replyTo?.summary,
                                  mentionUserIds: ChatMentions.mentioned(in: text, members: detail?.members ?? []), location: location)
        draft = ""
        picked = []
        location = nil
        replyTo = nil
        chat.saveDraft("", for: conversationId)
        do {
            try await chat.enqueue(item, contents: contents)
            if let confirmed = chat.confirmed.last(where: { $0.clientMessageId == item.id }) { merge([confirmed]) }
            if chat.queued.contains(where: { $0.id == item.id && $0.failure == nil }) {
                failure = "Ohne Verbindung – die Nachricht wartet und wird automatisch gesendet."
            }
        } catch {
            toast = "Die Nachricht konnte auf dem Gerät nicht abgelegt werden."
        }
    }

    private func saveEdit(_ message: ChatMessage) async {
        guard let client = model.active?.client, let text = ChatLimits.clean(draft) else { return }
        do throws(APIError) {
            let updated = try await client.editChatMessage(message.id, text: text)
            merge([updated])
            editing = nil
            draft = ""
            await restoreDraft()
        } catch {
            toast = error.code == "EDIT_WINDOW_EXPIRED" || error.status == 409
                ? "Bearbeiten ist nur \(editWindow) Minuten nach dem Senden möglich." : (error.serverMessage ?? error.message)
        }
    }

    private func react(_ message: ChatMessage, _ emoji: String) async {
        guard let client = model.active?.client else { return }
        let isMine = message.reactions.first { $0.emoji == emoji }?.mine == true
        do throws(APIError) {
            merge([try await client.reactToChatMessage(message.id, emoji: isMine ? nil : emoji)])
        } catch {
            toast = error.serverMessage ?? error.message
        }
    }

    private func pin(_ message: ChatMessage) async {
        guard let client = model.active?.client else { return }
        do throws(APIError) {
            merge([try await client.pinChatMessage(message.id, pinned: !message.pinned)])
            toast = message.pinned ? "Gelöst" : "Angeheftet"
        } catch {
            toast = error.serverMessage ?? error.message
        }
    }

    private func delete(_ message: ChatMessage) async {
        guard let client = model.active?.client else { return }
        do throws(APIError) {
            try await client.deleteChatMessage(message.id)
            await load(full: true)
        } catch {
            toast = error.serverMessage ?? error.message
        }
    }

    /// „Foto in Bauakte übernehmen“: Inhalt laden und wie ein eigenes Foto bzw. Dokument der Bauakte ablegen (verschlüsselt
    /// auf dem Gerät, Upload über die normale Synchronisation – auch offline).
    private func save(_ target: SaveTarget, to bauakte: Bauakte) async {
        guard let chat, let service = model.active?.service else { return }
        // Online: der Server übernimmt Foto bzw. Dokument direkt (mit Vorschauen, Kategorie „Chat“).
        if let client = model.active?.client {
            do throws(APIError) {
                try await client.copyChatAttachmentToBauakte(target.attachment.id, bauakteId: bauakte.id,
                                                             caption: target.message.text.isEmpty ? nil : target.message.text)
                sync?.localChangeMade()
                toast = "In „\(bauakte.title)“ übernommen"
                return
            } catch where error != .offline {
                toast = "Übernehmen nicht möglich: \(error.serverMessage ?? error.message)"
                return
            } catch {
                // Ohne Verbindung: auf dem Gerät ablegen, der Abgleich lädt später hoch.
            }
        }
        do {
            let data = try await chat.content(of: target.attachment)
            let sender = target.message.senderName.map { " von \($0)" } ?? ""
            if target.attachment.isPhoto {
                try await service.addPhoto(data, to: bauakte.id, capturedAt: target.message.createdAt,
                                           contentType: target.attachment.contentType == "image/png" ? "image/png" : "image/jpeg",
                                           caption: target.message.text.isEmpty ? "Aus dem Chat\(sender)" : target.message.text)
            } else {
                let name = (target.attachment.name as NSString).deletingPathExtension
                try await service.addDocument(data, title: name.isEmpty ? "Dokument aus dem Chat" : name, category: .other, to: bauakte.id,
                                              contentType: target.attachment.contentType)
            }
            sync?.localChangeMade()
            toast = "In „\(bauakte.title)“ übernommen"
        } catch {
            toast = "Übernehmen nicht möglich: \((error as? APIError)?.message ?? error.localizedDescription)"
        }
    }

    // MARK: Screenshot-Modus

    private func applyShotScene() {
        #if DEBUG
        switch shotScene {
        case "chat-verlauf":
            draft = "Passt, ich bringe die Rolle mit."
        case "chat-anhang":
            Task {
                try? await Task.sleep(for: .milliseconds(900))
                let demo = model.active?.chat
                if let photo = await demo?.image(for: ChatAttachment(id: UUID(uuidString: DemoChat.samplePhoto)!, kind: "photo", name: "Foto.jpg",
                                                                   contentType: "image/jpeg", sizeBytes: 0), thumbnail: false),
                   let jpeg = photo.jpegData(compressionQuality: 0.8) {
                    picked = [PickedAttachment.photo(jpeg)]
                }
                picked.append(PickedAttachment(name: "Aufmaß Halle 2.pdf", kind: "document", contentType: "application/pdf",
                                               data: Data(count: 412_000), preview: nil))
                location = ChatLocation(latitude: 48.3705, longitude: 10.8978, label: "Praxis Dr. Maier, Sanierung", address: "Altstadt 12, Augsburg")
                draft = "Aufmaß und Zufahrt für morgen"
            }
        case "chat-gruppe":
            Task {
                try? await Task.sleep(for: .milliseconds(600))
                showsInfo = true
            }
        case "chat-diktat":
            Task {
                try? await Task.sleep(for: .milliseconds(700))
                dictationBase = ""
                dictation.simulateListening("Estrich im Behandlungsraum zwei ist begehbar, CM-Messung mache ich morgen früh")
            }
        default: break
        }
        #endif
    }
}

/// Gewählter, noch nicht gesendeter Anhang (Foto bereits verkleinert).
struct PickedAttachment: Identifiable {
    let id = UUID.v7()
    let name: String
    let kind: String
    let contentType: String
    let data: Data
    let preview: UIImage?

    static func photo(_ jpeg: Data) -> PickedAttachment {
        let stamp = Date().formatted(.dateTime.year().month(.twoDigits).day(.twoDigits).hour(.twoDigits(amPM: .omitted)).minute(.twoDigits))
            .filter { $0.isNumber }
        return PickedAttachment(name: "Foto_\(stamp).jpg", kind: "photo", contentType: "image/jpeg", data: jpeg,
                                preview: ThumbnailLoader.downsample(jpeg, to: 72))
    }
}

struct SaveTarget: Identifiable {
    let message: ChatMessage
    let attachment: ChatAttachment
    var id: UUID { attachment.id }
}

struct ViewedAttachment: Identifiable {
    let message: ChatMessage
    let attachment: ChatAttachment
    var id: UUID { attachment.id }
}

/// Zitat bzw. Bearbeitung über dem Eingabefeld.
private struct ComposerBanner: View {
    let symbol: String
    let title: String
    let text: String
    let cancel: () -> Void

    var body: some View {
        HStack(spacing: Space.s) {
            RoundedRectangle(cornerRadius: 2).fill(Theme.accent).frame(width: 3)
            Image(systemName: symbol).foregroundStyle(Theme.accent)
            VStack(alignment: .leading, spacing: 0) {
                Text(title).font(Typo.labelStrong).foregroundStyle(Theme.accent)
                Text(text).font(Typo.meta).foregroundStyle(Theme.textSecondary).lineLimit(1)
            }
            Spacer()
            Button(action: cancel) { Image(systemName: "xmark.circle.fill").font(.title3).foregroundStyle(Theme.textTertiary) }
                .accessibilityLabel("Abbrechen")
        }
        .padding(.vertical, 2)
    }
}

/// „… schreibt“ mit drei ruhigen Punkten.
private struct TypingIndicator: View {
    let name: String

    var body: some View {
        HStack(spacing: Space.s) {
            HStack(spacing: 3) {
                ForEach(0..<3, id: \.self) { _ in Circle().fill(Theme.textTertiary).frame(width: 6, height: 6) }
            }
            .padding(.horizontal, Space.m)
            .padding(.vertical, 10)
            .background(Theme.surface, in: Capsule())
            .symbolEffect(.pulse)
            Text("\(name) schreibt …").font(Typo.label).foregroundStyle(Theme.textSecondary)
        }
        .accessibilityElement(children: .combine)
    }
}

/// Kamera für ein Foto direkt in den Chat.
struct ChatCameraPicker: UIViewControllerRepresentable {
    let onFinish: @MainActor (UIImage?) -> Void

    func makeUIViewController(context: Context) -> UIImagePickerController {
        let picker = UIImagePickerController()
        picker.sourceType = .camera
        picker.cameraCaptureMode = .photo
        picker.delegate = context.coordinator
        return picker
    }

    func updateUIViewController(_ picker: UIImagePickerController, context: Context) {}

    func makeCoordinator() -> Coordinator { Coordinator(onFinish: onFinish) }

    final class Coordinator: NSObject, UIImagePickerControllerDelegate, UINavigationControllerDelegate, @unchecked Sendable {
        private let onFinish: @MainActor (UIImage?) -> Void

        init(onFinish: @escaping @MainActor (UIImage?) -> Void) {
            self.onFinish = onFinish
        }

        nonisolated func imagePickerController(_ picker: UIImagePickerController,
                                               didFinishPickingMediaWithInfo info: [UIImagePickerController.InfoKey: Any]) {
            let image = info[.originalImage] as? UIImage
            MainActor.assumeIsolated { self.onFinish(image) }
        }

        nonisolated func imagePickerControllerDidCancel(_ picker: UIImagePickerController) {
            MainActor.assumeIsolated { self.onFinish(nil) }
        }
    }
}
