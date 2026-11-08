import EstrichCore
import MapKit
import SwiftUI
import UIKit

/// Was eine Sprechblase anbietet (langes Drücken, Wischen, Tippen auf Anhänge).
struct ChatBubbleActions {
    var reply: (ChatMessage) -> Void = { _ in }
    var react: (ChatMessage, String) -> Void = { _, _ in }
    var edit: ((ChatMessage) -> Void)?
    var delete: ((ChatMessage) -> Void)?
    var pin: ((ChatMessage) -> Void)?
    var forward: ((ChatMessage) -> Void)?
    var saveToBauakte: ((ChatMessage, ChatAttachment) -> Void)?
    var readReceipts: ((ChatMessage) -> Void)?
    var openAttachment: (ChatMessage, ChatAttachment) -> Void = { _, _ in }
    var openQuote: (UUID) -> Void = { _ in }
    var retry: (ChatMessage) -> Void = { _ in }
    var discard: (ChatMessage) -> Void = { _ in }
}

/// Sprechblase wie im Messenger: Zitat, Fotos, Dokumente, Standort, Text mit Links und Erwähnungen, „bearbeitet“,
/// Zeit, Zustand und Reaktionen. Nach rechts wischen zitiert, langes Drücken zeigt Reaktionen und Aktionen.
struct MessageBubble: View {
    let message: ChatMessage
    let isMine: Bool
    var isGroup = false
    let showsSender: Bool
    var highlighted = false
    var members: [ChatMember] = []
    var canEdit = false
    var canDelete = false
    var canPin = false
    var readOnly = false
    let actions: ChatBubbleActions

    @Environment(\.openURL) private var openURL
    @State private var dragOffset: CGFloat = 0

    var body: some View {
        if message.isSystem {
            Text(message.text)
                .font(Typo.label)
                .foregroundStyle(Theme.textSecondary)
                .multilineTextAlignment(.center)
                .padding(.horizontal, Space.m)
                .padding(.vertical, 5)
                .background(Theme.fill, in: Capsule())
                .frame(maxWidth: .infinity)
                .padding(.vertical, Space.xs)
        } else {
            bubbleRow
        }
    }

    private var bubbleRow: some View {
        HStack(alignment: .center) {
            if isMine { Spacer(minLength: 48) }
            VStack(alignment: isMine ? .trailing : .leading, spacing: 2) {
                if showsSender, let name = message.senderName {
                    Text(name)
                        .font(Typo.labelStrong)
                        .foregroundStyle(Theme.accent)
                        .padding(.horizontal, Space.m)
                }
                bubble
                    .contextMenu { if !message.deleted, message.state != .queued, message.state != .failed, message.state != .sending { menu } }
                if !message.reactions.isEmpty { reactionRow }
                if message.state == .failed {
                    HStack(spacing: Space.m) {
                        Button("Erneut senden", systemImage: "arrow.clockwise") { actions.retry(message) }
                        Button("Verwerfen", systemImage: "trash", role: .destructive) { actions.discard(message) }
                    }
                    .font(Typo.label)
                    .labelStyle(.titleAndIcon)
                }
            }
            .frame(maxWidth: 540, alignment: isMine ? .trailing : .leading)
            if !isMine { Spacer(minLength: 48) }
        }
        .overlay(alignment: .leading) {
            Image(systemName: "arrowshape.turn.up.left.fill")
                .font(.system(size: 15, weight: .semibold))
                .foregroundStyle(Theme.accent)
                .padding(8)
                .background(Circle().fill(Theme.accentSoft))
                .opacity(min(1, Double(dragOffset / 60)))
                .offset(x: -36 + min(dragOffset, 60) / 2)
                .accessibilityHidden(true)
        }
        .offset(x: dragOffset)
        .simultaneousGesture(swipeToReply)
        .accessibilityElement(children: .combine)
        .accessibilityLabel(accessibilityText)
        .accessibilityValue(stateText)
        .accessibilityAction(named: "Antworten") { actions.reply(message) }
    }

    /// Nach rechts wischen: zitierend antworten (wie im Messenger), nur deutlich waagerecht.
    private var swipeToReply: some Gesture {
        DragGesture(minimumDistance: 24, coordinateSpace: .local)
            .onChanged { value in
                guard !message.deleted, !readOnly, message.state != .queued, value.translation.width > 0,
                      abs(value.translation.width) > abs(value.translation.height) * 1.6 else { return }
                dragOffset = min(value.translation.width, 90)
            }
            .onEnded { _ in
                if dragOffset > 58 {
                    UIImpactFeedbackGenerator(style: .light).impactOccurred()
                    actions.reply(message)
                }
                withAnimation(.snappy) { dragOffset = 0 }
            }
    }

    private var foreground: Color { isMine ? Theme.onAccent : Theme.textPrimary }
    private var secondary: Color { isMine ? Theme.onAccent.opacity(0.78) : Theme.textSecondary }

    private var bubble: some View {
        VStack(alignment: .leading, spacing: 4) {
            if message.forwarded, !message.deleted {
                Label("Weitergeleitet", systemImage: "arrowshape.turn.up.right")
                    .font(Typo.label.italic())
                    .foregroundStyle(secondary)
            }
            if let quote = message.replyTo, !message.deleted {
                Button { actions.openQuote(quote.id) } label: { QuoteView(quote: quote, onAccent: isMine) }
                    .buttonStyle(.plain)
            }
            if message.deleted {
                Label("Nachricht gelöscht", systemImage: "nosign")
                    .font(Typo.body.italic())
                    .foregroundStyle(secondary)
            } else {
                let photos = message.attachments.filter(\.isPhoto)
                if !photos.isEmpty {
                    ChatPhotoGrid(photos: photos) { actions.openAttachment(message, $0) }
                }
                ForEach(message.attachments.filter { !$0.isPhoto }) { document in
                    Button { actions.openAttachment(message, document) } label: { DocumentChip(attachment: document, onAccent: isMine) }
                        .buttonStyle(.plain)
                }
                if let location = message.location {
                    LocationCard(location: location, onAccent: isMine) { if let url = location.mapsURL { openURL(url) } }
                }
                if !message.text.isEmpty {
                    Text(ChatText.attributed(message.text, members: members, mentions: message.mentions, onAccent: isMine))
                        .font(Typo.body)
                        .foregroundStyle(foreground)
                        .tint(isMine ? Theme.onAccent : Theme.accent)
                        .fixedSize(horizontal: false, vertical: true)
                        .textSelection(.enabled)
                }
            }
            HStack(spacing: 3) {
                Spacer(minLength: 0)
                if message.pinned { Image(systemName: "pin.fill").accessibilityLabel("angeheftet") }
                if message.editedAt != nil, !message.deleted { Text("bearbeitet ·") }
                // Gruppen: „gelesen 3/5“, solange noch nicht alle gelesen haben.
                if isMine, isGroup, message.recipients > 1, message.state != .read, message.readBy > 0 {
                    Text("gelesen \(message.readBy)/\(message.recipients) ·")
                }
                Text(message.createdAt.shortTime)
                if isMine { stateMark }
            }
            .font(.caption2.monospacedDigit())
            .foregroundStyle(secondary)
        }
        .padding(.horizontal, message.attachments.contains(where: \.isPhoto) && message.text.isEmpty ? 4 : Space.m)
        .padding(.top, message.attachments.contains(where: \.isPhoto) && message.replyTo == nil && !message.forwarded ? 4 : 7)
        .padding(.bottom, 6)
        .background(isMine ? Theme.accentFill : Theme.surface, in: UnevenRoundedRectangle(
            topLeadingRadius: 18, bottomLeadingRadius: isMine ? 18 : 6, bottomTrailingRadius: isMine ? 6 : 18, topTrailingRadius: 18,
            style: .continuous))
        .overlay {
            if highlighted {
                UnevenRoundedRectangle(topLeadingRadius: 18, bottomLeadingRadius: isMine ? 18 : 6, bottomTrailingRadius: isMine ? 6 : 18,
                                       topTrailingRadius: 18, style: .continuous)
                    .strokeBorder(Theme.warning, lineWidth: 2)
            }
        }
        .opacity(message.state == .sending || message.state == .queued ? 0.82 : 1)
    }

    private var reactionRow: some View {
        HStack(spacing: 4) {
            ForEach(message.reactions) { reaction in
                Button { if !readOnly { actions.react(message, reaction.emoji) } } label: {
                    HStack(spacing: 2) {
                        Text(reaction.emoji)
                        if reaction.count > 1 { Text("\(reaction.count)").font(.caption2.weight(.semibold).monospacedDigit()) }
                    }
                    .padding(.horizontal, 7)
                    .padding(.vertical, 3)
                    .background(reaction.mine ? Theme.accentSoft : Theme.surface, in: Capsule())
                    .overlay(Capsule().strokeBorder(reaction.mine ? Theme.accent.opacity(0.5) : Theme.separator, lineWidth: 0.5))
                }
                .buttonStyle(.plain)
                .accessibilityLabel("\(reaction.emoji) \(reaction.count)\(reaction.names.isEmpty ? "" : ": " + reaction.names.joined(separator: ", "))")
            }
        }
        .padding(.horizontal, Space.xs)
        .offset(y: -6)
        .padding(.bottom, -6)
    }

    @ViewBuilder
    private var menu: some View {
        if !readOnly {
            ControlGroup {
                ForEach(ChatReaction.quick.prefix(4), id: \.self) { emoji in
                    Button(emoji) { actions.react(message, emoji) }
                }
            }
            Menu("Weitere Reaktion", systemImage: "face.smiling") {
                ForEach(ChatReaction.quick.dropFirst(4), id: \.self) { emoji in
                    Button(emoji) { actions.react(message, emoji) }
                }
            }
            Button("Antworten", systemImage: "arrowshape.turn.up.left") { actions.reply(message) }
        }
        if !message.text.isEmpty {
            Button("Kopieren", systemImage: "doc.on.doc") { UIPasteboard.general.string = message.text }
        }
        if !readOnly {
            if canEdit, let edit = actions.edit {
                Button("Bearbeiten", systemImage: "pencil") { edit(message) }
            }
            if canPin, let pin = actions.pin {
                Button(message.pinned ? "Lösen" : "Anheften", systemImage: message.pinned ? "pin.slash" : "pin") { pin(message) }
            }
            if let forward = actions.forward {
                Button("Weiterleiten", systemImage: "arrowshape.turn.up.right") { forward(message) }
            }
            if let save = actions.saveToBauakte {
                ForEach(message.attachments) { attachment in
                    Button(attachment.isPhoto ? "Foto in Bauakte übernehmen" : "„\(attachment.name)“ in Bauakte übernehmen",
                           systemImage: "folder.badge.plus") { save(message, attachment) }
                }
            }
        }
        if isMine, isGroup, let receipts = actions.readReceipts {
            Button("Gelesen von …", systemImage: "checkmark.circle") { receipts(message) }
        }
        if !readOnly, canDelete, let delete = actions.delete {
            Divider()
            Button("Für alle löschen", systemImage: "trash", role: .destructive) { delete(message) }
        }
    }

    @ViewBuilder
    private var stateMark: some View {
        switch message.state {
        case .queued: Image(systemName: "clock.badge.exclamationmark")
        case .sending: Image(systemName: "clock")
        case .sent: Image(systemName: "checkmark")
        case .delivered: DoubleCheck()
        case .read: DoubleCheck().foregroundStyle(Theme.ice)
        case .failed: Image(systemName: "exclamationmark.circle")
        }
    }

    private var accessibilityText: String {
        var parts = [isMine ? "Ich" : (message.senderName ?? "")]
        if let quote = message.replyTo { parts.append("Antwort auf \(quote.senderName ?? ""): \(quote.text)") }
        parts.append(message.summary)
        if message.editedAt != nil { parts.append("bearbeitet") }
        return parts.joined(separator: ", ")
    }

    private var stateText: String {
        guard isMine else { return "" }
        switch message.state {
        case .queued: return "wartet auf Verbindung"
        case .sending: return "wird gesendet"
        case .sent: return "gesendet"
        case .delivered: return "zugestellt"
        case .read: return "gelesen"
        case .failed: return "nicht gesendet"
        }
    }
}

/// Zitat in der Sprechblase bzw. über dem Eingabefeld.
struct QuoteView: View {
    let quote: ChatReplyPreview
    var onAccent = false

    var body: some View {
        HStack(spacing: Space.s) {
            RoundedRectangle(cornerRadius: 2).fill(onAccent ? Theme.onAccent : Theme.accent).frame(width: 3)
            VStack(alignment: .leading, spacing: 1) {
                Text(quote.senderName ?? "Nachricht")
                    .font(Typo.labelStrong)
                    .foregroundStyle(onAccent ? Theme.onAccent : Theme.accent)
                Text(quote.deleted ? "Nachricht gelöscht" : (quote.text.isEmpty ? attachmentText : quote.text))
                    .font(Typo.meta)
                    .foregroundStyle(onAccent ? Theme.onAccent.opacity(0.85) : Theme.textSecondary)
                    .lineLimit(2)
            }
            Spacer(minLength: 0)
        }
        .padding(.vertical, 5)
        .padding(.horizontal, Space.s)
        .background((onAccent ? Color.white.opacity(0.16) : Theme.fill), in: RoundedRectangle(cornerRadius: 10, style: .continuous))
    }

    private var attachmentText: String {
        switch quote.attachmentKind {
        case "photo": "Foto"
        case "document": "📄 Dokument"
        default: "…"
        }
    }
}

/// Fotos einer Nachricht: eins groß, sonst ein Raster (höchstens vier, darüber „+n“).
private struct ChatPhotoGrid: View {
    let photos: [ChatAttachment]
    let open: (ChatAttachment) -> Void

    var body: some View {
        let shown = Array(photos.prefix(4))
        if shown.count == 1, let photo = shown.first {
            tile(photo, width: 240, height: height(for: photo))
        } else {
            LazyVGrid(columns: [GridItem(.fixed(118), spacing: 4), GridItem(.fixed(118), spacing: 4)], spacing: 4) {
                ForEach(Array(shown.enumerated()), id: \.element.id) { index, photo in
                    tile(photo, width: 118, height: 118)
                        .overlay {
                            if index == 3, photos.count > 4 {
                                ZStack {
                                    Color.black.opacity(0.45)
                                    Text("+\(photos.count - 4)").font(.title2.weight(.semibold)).foregroundStyle(.white)
                                }
                                .clipShape(RoundedRectangle(cornerRadius: 14, style: .continuous))
                                .allowsHitTesting(false)
                            }
                        }
                }
            }
        }
    }

    private func height(for photo: ChatAttachment) -> CGFloat {
        guard let width = photo.width, let height = photo.height, width > 0 else { return 200 }
        return min(320, max(140, 240 * CGFloat(height) / CGFloat(width)))
    }

    private func tile(_ photo: ChatAttachment, width: CGFloat, height: CGFloat) -> some View {
        Button { open(photo) } label: {
            ChatThumbnail(attachment: photo)
                .frame(width: width, height: height)
                .clipShape(RoundedRectangle(cornerRadius: 14, style: .continuous))
        }
        .buttonStyle(.plain)
        .accessibilityLabel("Foto öffnen")
    }
}

/// Vorschaubild eines Anhangs (lädt aus dem Arbeitsspeicher, dem Tresor der Warteschlange oder vom Server).
struct ChatThumbnail: View {
    let attachment: ChatAttachment
    @Environment(AppModel.self) private var model
    @State private var image: UIImage?
    @State private var failed = false

    var body: some View {
        ZStack {
            Theme.fill
            if let image {
                Image(uiImage: image).resizable().scaledToFill()
            } else if failed {
                Image(systemName: "photo").font(.title2).foregroundStyle(Theme.textTertiary)
            } else {
                ProgressView()
            }
        }
        .clipped()
        .task(id: attachment.id) {
            image = await model.active?.chat?.image(for: attachment, thumbnail: true)
            failed = image == nil
        }
    }
}

/// Dokument in der Sprechblase: Symbol, Name, Größe.
struct DocumentChip: View {
    let attachment: ChatAttachment
    var onAccent = false

    var body: some View {
        HStack(spacing: Space.s) {
            Image(systemName: attachment.contentType == "application/pdf" ? "doc.richtext.fill" : "doc.fill")
                .font(.title2)
                .foregroundStyle(onAccent ? Theme.onAccent : Theme.accent)
            VStack(alignment: .leading, spacing: 1) {
                Text(attachment.name)
                    .font(Typo.subheadStrong)
                    .foregroundStyle(onAccent ? Theme.onAccent : Theme.textPrimary)
                    .lineLimit(2)
                Text(ChatFormat.size(attachment.sizeBytes) + (attachment.contentType == "application/pdf" ? " · PDF" : ""))
                    .font(Typo.label)
                    .foregroundStyle(onAccent ? Theme.onAccent.opacity(0.8) : Theme.textSecondary)
            }
            Spacer(minLength: 0)
        }
        .padding(Space.s)
        .frame(minWidth: 200)
        .background(onAccent ? Color.white.opacity(0.16) : Theme.fill, in: RoundedRectangle(cornerRadius: 12, style: .continuous))
        .accessibilityLabel("Dokument \(attachment.name)")
    }
}

/// Standort bzw. Baustellenadresse mit kleiner Karte; Tippen startet die Route in Karten.
struct LocationCard: View {
    let location: ChatLocation
    var onAccent = false
    let open: () -> Void

    var body: some View {
        Button(action: open) {
            VStack(alignment: .leading, spacing: 0) {
                Map(initialPosition: .region(MKCoordinateRegion(center: coordinate, latitudinalMeters: 600, longitudinalMeters: 600)),
                    interactionModes: []) {
                    Marker(location.label ?? "Standort", systemImage: "mappin", coordinate: coordinate)
                        .tint(Theme.accentFill)
                }
                .frame(width: 240, height: 120)
                .allowsHitTesting(false)
                HStack(spacing: Space.s) {
                    Image(systemName: "arrow.triangle.turn.up.right.diamond.fill")
                        .foregroundStyle(onAccent ? Theme.onAccent : Theme.accent)
                    VStack(alignment: .leading, spacing: 0) {
                        Text(location.label ?? "Standort")
                            .font(Typo.subheadStrong)
                            .foregroundStyle(onAccent ? Theme.onAccent : Theme.textPrimary)
                        if let address = location.address {
                            Text(address).font(Typo.label).foregroundStyle(onAccent ? Theme.onAccent.opacity(0.8) : Theme.textSecondary).lineLimit(2)
                        }
                    }
                    Spacer(minLength: 0)
                }
                .padding(Space.s)
                .frame(width: 240, alignment: .leading)
            }
            .background(onAccent ? Color.white.opacity(0.16) : Theme.fill)
            .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
        }
        .buttonStyle(.plain)
        .accessibilityLabel("\(location.label ?? "Standort") \(location.address ?? ""), Route starten")
    }

    private var coordinate: CLLocationCoordinate2D {
        CLLocationCoordinate2D(latitude: location.latitude, longitude: location.longitude)
    }
}

/// Zwei versetzte Häkchen (zugestellt bzw. gelesen).
struct DoubleCheck: View {
    var body: some View {
        ZStack(alignment: .leading) {
            Image(systemName: "checkmark")
            Image(systemName: "checkmark").offset(x: 4)
        }
        .padding(.trailing, 4)
    }
}

/// Text einer Nachricht: Links klickbar, `@Erwähnungen` hervorgehoben.
enum ChatText {
    static func attributed(_ text: String, members: [ChatMember], mentions: [UUID], onAccent: Bool) -> AttributedString {
        var result = AttributedString(text)
        if let detector = try? NSDataDetector(types: NSTextCheckingResult.CheckingType.link.rawValue | NSTextCheckingResult.CheckingType.phoneNumber.rawValue) {
            let nsText = text as NSString
            for match in detector.matches(in: text, range: NSRange(location: 0, length: nsText.length)) {
                guard let range = Range(match.range, in: text), let attributedRange = Range(range, in: result) else { continue }
                if let url = match.url {
                    result[attributedRange].link = url
                } else if let phone = match.phoneNumber, let url = URL(string: "tel:" + phone.filter { $0.isNumber || $0 == "+" }) {
                    result[attributedRange].link = url
                }
            }
        }
        let names = members.filter { mentions.contains($0.id) }.map(\.displayName)
            + members.map { $0.displayName.split(separator: " ").first.map(String.init) ?? "" }.filter { !$0.isEmpty }
        for name in Set(names) {
            var searchStart = result.startIndex
            while let range = result[searchStart...].range(of: "@" + name, options: .caseInsensitive) {
                result[range].font = Typo.bodyStrong
                if !onAccent { result[range].foregroundColor = Theme.accent }
                searchStart = range.upperBound
            }
        }
        return result
    }
}

enum ChatFormat {
    static func size(_ bytes: Int64) -> String {
        ByteCountFormatter.string(fromByteCount: bytes, countStyle: .file)
    }
}
