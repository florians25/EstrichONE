import EstrichCore
import Foundation
import Observation
import UIKit

/// Chat der angemeldeten Person: Echtzeit-Kanal (Server-Sent Events, sonst Abfragen), Warteschlange für Nachrichten
/// und Anhänge (offline), Entwürfe, „… schreibt“ und ein kleiner Bildspeicher im Arbeitsspeicher.
@MainActor
@Observable
final class ChatCenter {
    let client: AuthorizedClient
    let outbox: ChatOutbox
    let userId: UUID
    let userName: String
    private let isDemo: Bool

    /// Echtzeit-Kanal verbunden – Ansichten fragen dann nur noch selten nach.
    private(set) var isLive = false
    /// Zähler für Ereignisse; Ansichten laden bei Änderung nach (`lastEvent` sagt, was).
    private(set) var eventCount = 0
    private(set) var lastEvent: ChatEvent?
    /// Wer gerade schreibt, je Unterhaltung (Name, gültig bis).
    private(set) var typing: [UUID: (name: String, until: Date)] = [:]
    /// Wartende Nachrichten (Warteschlange), je Unterhaltung für den Verlauf.
    private(set) var queued: [ChatOutboxItem] = []
    /// Gerade übertragene Nachrichten der Warteschlange.
    private(set) var sending: Set<UUID> = []
    /// Vom Server bestätigte Nachrichten aus der Warteschlange (der Verlauf übernimmt sie sofort).
    private(set) var confirmed: [ChatMessage] = []
    /// Geöffnete Unterhaltung (keine Mitteilung im Vordergrund für sie).
    var visibleConversation: UUID? {
        didSet { VisibleChat.shared.id = visibleConversation }
    }

    private var stream: Task<Void, Never>?
    private var pump: Task<Void, Never>?
    private var images: [String: UIImage] = [:]
    private var imageOrder: [String] = []
    private var lastTypingSent: [UUID: Date] = [:]
    private var statusRefresh: Task<Void, Never>?
    /// Status (ungelesen, Badge) neu laden – setzt das App-Modell.
    var refreshStatus: (() async -> Void)?

    init(client: AuthorizedClient, store: LocalStore, files: FileVault, userId: UUID, userName: String, isDemo: Bool) {
        self.client = client
        outbox = ChatOutbox(store: store, files: files)
        self.userId = userId
        self.userName = userName
        self.isDemo = isDemo
    }

    // MARK: Lebenszyklus

    func start() {
        guard pump == nil else { return }
        Task { await reloadQueue() }
        if !isDemo, stream == nil {
            stream = Task { [weak self] in await self?.runStream() }
        }
        // Warteschlange: alle 15 Sekunden, solange etwas wartet.
        pump = Task { [weak self] in
            while !Task.isCancelled {
                try? await Task.sleep(for: .seconds(15))
                guard let self else { return }
                if !self.queued.isEmpty { await self.flush() }
                self.expireTyping()
            }
        }
    }

    /// Hintergrund oder Sperre: Verbindung schließen (Warteschlange bleibt verschlüsselt liegen).
    func stop() {
        stream?.cancel()
        stream = nil
        pump?.cancel()
        pump = nil
        isLive = false
    }

    // MARK: Echtzeit

    private enum StreamFailure: Error {
        case status(Int)
    }

    private func runStream() async {
        let configuration = URLSessionConfiguration.ephemeral
        // Der Server schickt mindestens alle 25 Sekunden ein Lebenszeichen; ohne es gilt die Verbindung als tot.
        configuration.timeoutIntervalForRequest = 75
        configuration.timeoutIntervalForResource = 24 * 3600
        let session = URLSession(configuration: configuration)
        defer { session.invalidateAndCancel() }
        var parser = ServerSentEventParser()
        var attempt = 0
        while !Task.isCancelled {
            let pause: Duration
            do {
                let request = try await client.streamRequest("chat/events", lastEventId: parser.lastEventId)
                let (bytes, response) = try await session.bytes(for: request)
                let status = (response as? HTTPURLResponse)?.statusCode ?? 0
                guard status == 200 else { throw StreamFailure.status(status) }
                isLive = true
                attempt = 0
                // Nach dem (Wieder-)Verbinden einmal alles auffrischen: Verpasstes kommt so nach.
                deliver(ChatEvent(kind: .status))
                if !queued.isEmpty { Task { await flush() } }
                var line: [UInt8] = []
                for try await byte in bytes {
                    if byte == UInt8(ascii: "\n") {
                        if let event = parser.feed(line: String(decoding: line, as: UTF8.self)) {
                            deliver(ChatEvent(name: event.name, data: event.data))
                        }
                        line.removeAll(keepingCapacity: true)
                    } else {
                        line.append(byte)
                    }
                }
                attempt += 1
                pause = ChatPolling.reconnectDelay(attempt: attempt, serverRetryMilliseconds: parser.retryMilliseconds)
            } catch StreamFailure.status(let status) {
                // Ohne Echtzeit-Kanal (älterer Server, Modul aus, Proxy puffert): weiter mit Abfragen, selten neu versuchen.
                attempt += 1
                pause = [404, 405, 409, 501].contains(status) ? .seconds(300) : ChatPolling.reconnectDelay(attempt: attempt)
            } catch {
                attempt += 1
                pause = ChatPolling.reconnectDelay(attempt: attempt, serverRetryMilliseconds: parser.retryMilliseconds)
            }
            isLive = false
            try? await Task.sleep(for: pause)
        }
    }

    private func deliver(_ event: ChatEvent) {
        switch event.kind {
        case .ping: return
        case .typing:
            guard let conversation = event.conversationId, event.userId != userId else { return }
            typing[conversation] = (event.userName ?? "Jemand", Date().addingTimeInterval(6))
            return
        default:
            if let conversation = event.conversationId, event.kind == .message { typing[conversation] = nil }
            lastEvent = event
            eventCount += 1
            // Badge: neue bzw. gelesene Nachrichten ändern die Zahl ungelesener Chats (entprellt).
            if event.kind == .message || event.kind == .read || event.kind == .conversation || event.kind == .status {
                statusRefresh?.cancel()
                statusRefresh = Task { [weak self] in
                    try? await Task.sleep(for: .seconds(1))
                    guard !Task.isCancelled else { return }
                    await self?.refreshStatus?()
                }
            }
        }
    }

    private func expireTyping() {
        let now = Date()
        typing = typing.filter { $0.value.until > now }
    }

    func typingName(in conversationId: UUID) -> String? {
        guard let entry = typing[conversationId], entry.until > Date() else { return nil }
        return entry.name
    }

    /// „… schreibt“ melden, höchstens alle 4 Sekunden (nur mit Echtzeit-Kanal sinnvoll).
    func reportTyping(in conversationId: UUID) {
        guard isLive else { return }
        let now = Date()
        if let last = lastTypingSent[conversationId], now.timeIntervalSince(last) < 4 { return }
        lastTypingSent[conversationId] = now
        let client = client
        Task { try? await client.sendChatTyping(conversationId) }
    }

    // MARK: Warteschlange

    func reloadQueue() async {
        queued = await outbox.items()
    }

    func queued(in conversationId: UUID) -> [ChatOutboxItem] {
        queued.filter { $0.conversationId == conversationId }
    }

    /// Nachricht ablegen und gleich senden; ohne Verbindung bleibt sie (verschlüsselt) in der Warteschlange.
    func enqueue(_ item: ChatOutboxItem, contents: [UUID: Data]) async throws {
        try await outbox.enqueue(item, contents: contents)
        for (id, data) in contents where item.attachments.first(where: { $0.id == id })?.kind == "photo" {
            if let image = ThumbnailLoader.downsample(data, to: 260) { remember(image, key: "\(id.uuidString.lowercased())-thumb") }
        }
        await reloadQueue()
        await flush()
    }

    func retry(_ id: UUID) async {
        try? await outbox.retry(id)
        await reloadQueue()
        await flush()
    }

    func discard(_ id: UUID) async {
        await outbox.remove(id)
        await reloadQueue()
    }

    /// Sendet die Warteschlange; bestätigte Nachrichten gehen sofort in den Verlauf.
    @discardableResult
    func flush() async -> ChatFlushResult {
        let waiting = queued.filter { $0.failure == nil }.map(\.id)
        guard !waiting.isEmpty else { return ChatFlushResult() }
        sending.formUnion(waiting)
        let result = await outbox.flush(client: client)
        sending.subtract(waiting)
        if !result.sent.isEmpty {
            confirmed.append(contentsOf: result.sent)
            if confirmed.count > 50 { confirmed.removeFirst(confirmed.count - 50) }
            lastEvent = ChatEvent(kind: .message, conversationId: result.sent.last?.conversationId)
            eventCount += 1
        }
        await reloadQueue()
        return result
    }

    /// Antwort direkt aus der Mitteilung: wie eine getippte Nachricht (Warteschlange, wenn offline).
    func reply(_ text: String, to conversationId: UUID) async -> PushCenter.ChatReplyOutcome {
        guard let clean = ChatLimits.clean(text) else { return .failed }
        let item = ChatOutboxItem(conversationId: conversationId, text: clean)
        do {
            try await enqueue(item, contents: [:])
        } catch {
            return .failed
        }
        if let waiting = queued.first(where: { $0.id == item.id }) {
            return waiting.failure == nil ? .queued : .failed
        }
        try? await client.markChatRead(conversationId)
        return .sent
    }

    // MARK: Entwürfe

    func draft(for conversationId: UUID) async -> String {
        await outbox.draft(for: conversationId) ?? ""
    }

    func saveDraft(_ text: String, for conversationId: UUID) {
        let outbox = outbox
        Task { await outbox.setDraft(text, for: conversationId) }
    }

    // MARK: Bilder

    /// Vorschaubild bzw. Foto eines Anhangs (Arbeitsspeicher; wartende Anhänge aus dem verschlüsselten Tresor).
    func image(for attachment: ChatAttachment, thumbnail: Bool) async -> UIImage? {
        let key = "\(attachment.id.uuidString.lowercased())-\(thumbnail ? "thumb" : "full")"
        if let cached = images[key] { return cached }
        if thumbnail, let full = images["\(attachment.id.uuidString.lowercased())-full"] { return full }
        var data = await outbox.content(of: attachment.id)
        if data == nil {
            do throws(APIError) {
                data = try await client.chatAttachmentContent(attachment.id, thumbnail: thumbnail)
            } catch where thumbnail && error.status == 404 {
                data = try? await client.chatAttachmentContent(attachment.id, thumbnail: false)
            } catch {
                return nil
            }
        }
        guard let data, let image = ThumbnailLoader.downsample(data, to: thumbnail ? 260 : 1400) else { return nil }
        remember(image, key: key)
        return image
    }

    /// Gruppenbild (Arbeitsspeicher, je Stand).
    func groupImage(_ conversationId: UUID, version: Date) async -> UIImage? {
        let key = "group-\(conversationId.uuidString)-\(version.timeIntervalSince1970)"
        if let cached = images[key] { return cached }
        guard let data = try? await client.chatGroupImage(conversationId), let image = UIImage(data: data) else { return nil }
        remember(image, key: key)
        return image
    }

    /// Inhalt eines Anhangs (Dokument öffnen, in die Bauakte übernehmen, teilen).
    func content(of attachment: ChatAttachment) async throws(APIError) -> Data {
        if let local = await outbox.content(of: attachment.id) { return local }
        return try await client.chatAttachmentContent(attachment.id)
    }

    private func remember(_ image: UIImage, key: String) {
        images[key] = image
        imageOrder.removeAll { $0 == key }
        imageOrder.append(key)
        if imageOrder.count > 120 {
            let old = imageOrder.removeFirst()
            images[old] = nil
        }
    }
}

/// Geöffnete Unterhaltung für den Mitteilungs-Delegate (läuft außerhalb des Main Actors).
final class VisibleChat: @unchecked Sendable {
    static let shared = VisibleChat()
    private let lock = NSLock()
    private var current: UUID?

    var id: UUID? {
        get { lock.withLock { current } }
        set { lock.withLock { current = newValue } }
    }
}
