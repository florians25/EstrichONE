import EstrichCore
import QuickLook
import SwiftUI
import UIKit

/// Bauakte wählen (Baustellenadresse teilen, Foto/Dokument übernehmen). Die verknüpfte Bauakte der Gruppe steht oben.
struct BauaktePickerSheet: View {
    let title: String
    var preferred: UUID?
    let picked: (Bauakte) -> Void

    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var bauakten: [Bauakte] = []
    @State private var search = ""
    @State private var loaded = false

    var body: some View {
        NavigationStack {
            List {
                if let preferredBauakte, search.isEmpty {
                    Section("Verknüpfte Baustelle") { row(preferredBauakte) }
                }
                Section(search.isEmpty && preferredBauakte != nil ? "Weitere Baustellen" : "") {
                    ForEach(filtered.filter { $0.id != preferred || !search.isEmpty }) { row($0) }
                    if loaded, filtered.isEmpty {
                        Text("Keine Baustellen gefunden.").font(Typo.subhead).foregroundStyle(Theme.textSecondary)
                    }
                }
            }
            .overlay { if !loaded { ProgressView() } }
            .searchable(text: $search, prompt: "Baustelle suchen")
            .navigationTitle(title)
            .navigationBarTitleDisplayMode(.inline)
            .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Abbrechen") { dismiss() } } }
            .task {
                bauakten = (try? await model.active?.service?.bauakten()) ?? []
                loaded = true
            }
        }
        .presentationDetents([.medium, .large])
    }

    private var preferredBauakte: Bauakte? { preferred.flatMap { id in bauakten.first { $0.id == id } } }

    private var filtered: [Bauakte] {
        let term = search.trimmingCharacters(in: .whitespaces)
        guard !term.isEmpty else { return bauakten }
        return bauakten.filter {
            $0.title.localizedCaseInsensitiveContains(term) || ($0.addressLine ?? "").localizedCaseInsensitiveContains(term)
                || ($0.number ?? "").localizedCaseInsensitiveContains(term)
        }
    }

    private func row(_ bauakte: Bauakte) -> some View {
        Button { picked(bauakte) } label: {
            VStack(alignment: .leading, spacing: 2) {
                Text(bauakte.title).font(Typo.body).foregroundStyle(Theme.textPrimary)
                Text([bauakte.number, bauakte.addressLine].compactMap { $0 }.joined(separator: " · "))
                    .font(Typo.meta)
                    .foregroundStyle(Theme.textSecondary)
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }
}

/// Weiterleiten in einen oder mehrere Chats.
struct ChatForwardSheet: View {
    let message: ChatMessage
    let excluding: UUID
    let done: (Int) -> Void

    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var conversations: [ChatConversation] = []
    @State private var selected: Set<UUID> = []
    @State private var busy = false
    @State private var failure: String?

    var body: some View {
        NavigationStack {
            List {
                Section {
                    Text(message.summary).font(Typo.subhead).foregroundStyle(Theme.textSecondary).lineLimit(3)
                } header: { Text("Nachricht") }
                if let failure {
                    NoticeLine(tone: .danger, text: failure).listRowInsets(EdgeInsets()).listRowBackground(Color.clear)
                }
                Section("An") {
                    ForEach(conversations.filter { $0.id != excluding && $0.canWrite }) { conversation in
                        Button {
                            if selected.contains(conversation.id) { selected.remove(conversation.id) } else { selected.insert(conversation.id) }
                        } label: {
                            HStack(spacing: Space.m) {
                                ChatAvatar(title: conversation.title, kind: conversation.chatKind, avatarFileId: conversation.avatarFileId, size: 34)
                                Text(conversation.title).font(Typo.body).foregroundStyle(Theme.textPrimary)
                                Spacer()
                                Image(systemName: selected.contains(conversation.id) ? "checkmark.circle.fill" : "circle")
                                    .foregroundStyle(selected.contains(conversation.id) ? Theme.accent : Theme.textTertiary)
                                    .font(.title3)
                            }
                            .contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                    }
                }
            }
            .navigationTitle("Weiterleiten")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Abbrechen") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    if busy { ProgressView() } else {
                        Button("Senden") { Task { await forward() } }.disabled(selected.isEmpty)
                    }
                }
            }
            .task { conversations = (try? await model.active?.client.chatConversations()) ?? [] }
        }
        .presentationDetents([.medium, .large])
    }

    private func forward() async {
        guard let client = model.active?.client else { return }
        busy = true
        defer { busy = false }
        do throws(APIError) {
            try await client.forwardChatMessage(message.id, to: Array(selected))
            done(selected.count)
        } catch {
            failure = error.serverMessage ?? error.message
        }
    }
}

/// Lesebestätigungen je Mitglied („Gelesen von …“) – nur wenn der Betrieb sie nicht abgeschaltet hat.
struct ChatReadReceiptsSheet: View {
    let message: ChatMessage
    let members: [ChatMember]
    let readReceipts: Bool
    let userId: UUID?

    var body: some View {
        NavigationStack {
            List {
                if !readReceipts {
                    Text("Lesebestätigungen je Person sind im Betrieb abgeschaltet. Angezeigt wird nur, ob alle gelesen haben.")
                        .font(Typo.subhead).foregroundStyle(Theme.textSecondary)
                } else {
                    let others = members.filter { $0.id != userId }
                    let read = others.filter { ($0.readUpTo ?? .distantPast) >= message.createdAt }
                    let unread = others.filter { ($0.readUpTo ?? .distantPast) < message.createdAt }
                    Section("Gelesen (\(read.count))") {
                        ForEach(read) { member in
                            HStack {
                                Avatar(name: member.displayName, size: 30)
                                Text(member.displayName)
                                Spacer()
                                if let at = member.readUpTo { Text(ChatTime.short(at)).font(Typo.meta).foregroundStyle(Theme.textSecondary) }
                            }
                        }
                    }
                    Section("Noch nicht gelesen (\(unread.count))") {
                        ForEach(unread) { member in
                            HStack {
                                Avatar(name: member.displayName, size: 30)
                                Text(member.displayName).foregroundStyle(Theme.textSecondary)
                            }
                        }
                    }
                }
            }
            .navigationTitle("Gelesen von")
            .navigationBarTitleDisplayMode(.inline)
        }
    }
}

/// Foto groß (zoombar) bzw. Dokument in der Vorschau; „In Bauakte übernehmen“ und Teilen.
struct ChatAttachmentViewer: View {
    let attachment: ChatAttachment
    let message: ChatMessage
    let canSaveToBauakte: Bool
    let saveToBauakte: () -> Void

    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var image: UIImage?
    @State private var documentURL: URL?
    @State private var failure: String?
    @State private var scale: CGFloat = 1

    var body: some View {
        NavigationStack {
            ZStack {
                (attachment.isPhoto ? Color.black : Theme.canvas).ignoresSafeArea()
                if let image {
                    Image(uiImage: image)
                        .resizable()
                        .scaledToFit()
                        .scaleEffect(scale)
                        .gesture(MagnifyGesture().onChanged { scale = max(1, min(5, $0.magnification)) }.onEnded { _ in
                            if scale < 1.05 { withAnimation { scale = 1 } }
                        })
                        .onTapGesture(count: 2) { withAnimation { scale = scale > 1 ? 1 : 2.5 } }
                        .accessibilityLabel("Foto von \(message.senderName ?? "")")
                } else if let documentURL {
                    QuickLookPreview(url: documentURL)
                } else if let failure {
                    EmptyStateView(symbol: "exclamationmark.triangle", title: "Nicht verfügbar", message: failure)
                } else {
                    ProgressView().tint(attachment.isPhoto ? .white : nil)
                }
            }
            .navigationTitle(attachment.isPhoto ? (message.senderName ?? "Foto") : attachment.name)
            .navigationBarTitleDisplayMode(.inline)
            .toolbarBackground(.visible, for: .navigationBar)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Schließen") { dismiss() } }
                ToolbarItemGroup(placement: .bottomBar) {
                    if canSaveToBauakte {
                        Button(attachment.isPhoto ? "In Bauakte übernehmen" : "In Bauakte", systemImage: "folder.badge.plus") { saveToBauakte() }
                            .accessibilityIdentifier("chatSaveToBauakte")
                    }
                    Spacer()
                    if let shareURL = documentURL {
                        ShareLink(item: shareURL)
                    } else if let image {
                        ShareLink(item: Image(uiImage: image), preview: SharePreview("Foto", image: Image(uiImage: image)))
                    }
                }
            }
        }
        .task { await load() }
        .onDisappear {
            // Dokumente liegen nur für die Vorschau kurz im temporären Ordner der App.
            if let documentURL { try? FileManager.default.removeItem(at: documentURL) }
        }
    }

    private func load() async {
        guard let chat = model.active?.chat else { return }
        if attachment.isPhoto {
            image = await chat.image(for: attachment, thumbnail: false)
            if image == nil { failure = "Das Foto konnte nicht geladen werden." }
            return
        }
        do throws(APIError) {
            let data = try await chat.content(of: attachment)
            let folder = FileManager.default.temporaryDirectory.appending(path: "chat-\(attachment.id.uuidString.lowercased())", directoryHint: .isDirectory)
            try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            let url = folder.appending(path: attachment.name.replacingOccurrences(of: "/", with: "-"))
            try? data.write(to: url, options: [.atomic, .completeFileProtection])
            documentURL = url
        } catch {
            failure = error.serverMessage ?? error.message
        }
    }
}

/// QuickLook-Vorschau eines Dokuments (PDF, Bilder, Office).
struct QuickLookPreview: UIViewControllerRepresentable {
    let url: URL

    func makeUIViewController(context: Context) -> QLPreviewController {
        let controller = QLPreviewController()
        controller.dataSource = context.coordinator
        return controller
    }

    func updateUIViewController(_ controller: QLPreviewController, context: Context) {
        context.coordinator.url = url
        controller.reloadData()
    }

    func makeCoordinator() -> Coordinator { Coordinator(url: url) }

    final class Coordinator: NSObject, QLPreviewControllerDataSource, @unchecked Sendable {
        var url: URL
        init(url: URL) { self.url = url }

        nonisolated func numberOfPreviewItems(in controller: QLPreviewController) -> Int { 1 }

        nonisolated func previewController(_ controller: QLPreviewController, previewItemAt index: Int) -> any QLPreviewItem {
            let current = MainActor.assumeIsolated { url }
            return current as NSURL
        }
    }
}

/// Medien und Dokumente einer Unterhaltung.
struct ChatMediaView: View {
    let conversationId: UUID
    var initialKind = "photo"

    @Environment(AppModel.self) private var model
    @State private var kind = "photo"
    @State private var items: [ChatMediaItem] = []
    @State private var loaded = false
    @State private var viewing: ViewedAttachment?

    var body: some View {
        ScrollView {
            Picker("Art", selection: $kind) {
                Text("Fotos").tag("photo")
                Text("Dokumente").tag("document")
            }
            .pickerStyle(.segmented)
            .padding(.horizontal, Space.l)
            .padding(.top, Space.s)
            let shown = items.filter { kind == "photo" ? $0.attachment.isPhoto : !$0.attachment.isPhoto }
            if loaded, shown.isEmpty {
                EmptyStateView(symbol: kind == "photo" ? "photo.on.rectangle" : "doc", title: kind == "photo" ? "Keine Fotos" : "Keine Dokumente",
                               message: "Was in dieser Unterhaltung geteilt wird, erscheint hier.")
                    .padding(.top, Space.xxl)
            }
            if kind == "photo" {
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 104), spacing: 2)], spacing: 2) {
                    ForEach(shown) { item in
                        Button { viewing = ViewedAttachment(message: message(for: item), attachment: item.attachment) } label: {
                            ChatThumbnail(attachment: item.attachment)
                                .aspectRatio(1, contentMode: .fill)
                                .frame(minWidth: 0, maxWidth: .infinity)
                                .clipped()
                        }
                        .buttonStyle(.plain)
                    }
                }
                .padding(.top, Space.s)
            } else {
                LazyVStack(spacing: Space.s) {
                    ForEach(shown) { item in
                        Button { viewing = ViewedAttachment(message: message(for: item), attachment: item.attachment) } label: {
                            VStack(alignment: .leading, spacing: 2) {
                                DocumentChip(attachment: item.attachment)
                                Text("\(item.senderName ?? "") · \(ChatTime.short(item.createdAt))")
                                    .font(Typo.label)
                                    .foregroundStyle(Theme.textSecondary)
                                    .padding(.leading, Space.s)
                            }
                        }
                        .buttonStyle(.plain)
                    }
                }
                .padding(Space.l)
            }
        }
        .background(Theme.canvas)
        .overlay { if !loaded { ProgressView() } }
        .navigationTitle("Medien und Dokumente")
        .navigationBarTitleDisplayMode(.inline)
        .task {
            kind = initialKind
            await load()
        }
        .fullScreenCover(item: $viewing) { viewed in
            ChatAttachmentViewer(attachment: viewed.attachment, message: viewed.message, canSaveToBauakte: false) {}
        }
    }

    private func message(for item: ChatMediaItem) -> ChatMessage {
        ChatMessage(id: item.messageId, conversationId: conversationId, senderId: nil, senderName: item.senderName, text: "",
                    createdAt: item.createdAt, state: .sent, attachments: [item.attachment])
    }

    private func load() async {
        guard let client = model.active?.client else { return }
        do throws(APIError) {
            items = try await client.chatMedia(conversationId)
        } catch {
            // Ältere Server ohne Übersicht: aus dem Verlauf zusammenstellen.
            if let page = try? await client.chatPage(conversationId, limit: 200) { items = ChatMediaItem.collect(from: page.items) }
        }
        loaded = true
    }
}
