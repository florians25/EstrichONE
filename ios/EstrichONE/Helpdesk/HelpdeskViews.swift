import EstrichCore
import PhotosUI
import SwiftUI

/// Helpdesk in der App: eigene Meldungen (für IT-Admins zusätzlich die Bearbeitung) mit Status-Segmenten, Suche,
/// Markierung neuer Antworten und „Problem melden“. Lokal wartende Meldungen stehen oben.
struct MyTicketsView: View {
    enum Scope: String, CaseIterable, Identifiable {
        case mine = "Meine Meldungen"
        case agent = "Bearbeitung"
        var id: String { rawValue }
    }

    enum Segment: String, CaseIterable, Identifiable {
        case open = "Offen"
        case waiting = "Rückfrage"
        case done = "Erledigt"
        case all = "Alle"
        var id: String { rawValue }

        func matches(_ ticket: TicketSummary) -> Bool {
            switch self {
            case .open: ticket.isOpen
            case .waiting: ticket.status == .waitingForReporter
            case .done: !ticket.isOpen
            case .all: true
            }
        }
    }

    @Environment(AppModel.self) private var model
    @Environment(\.shotScene) private var shotScene
    @State private var scope: Scope = .mine
    @State private var segment: Segment = .open
    @State private var search = ""
    @State private var tickets: [TicketSummary] = []
    @State private var unread: Set<UUID> = []
    @State private var waiting: [(id: UUID, title: String)] = []
    @State private var offline = false
    @State private var loaded = false
    @State private var isAgent = false
    @State private var reporting = false
    /// Screenshot-Modus „meldung-detail“: erste Meldung geöffnet.
    @State private var opened: UUID?

    private var visible: [TicketSummary] {
        let text = search.trimmingCharacters(in: .whitespaces).lowercased()
        return tickets.filter { segment.matches($0) }
            .filter { text.isEmpty || "\($0.number) \($0.title) \($0.reporterDisplayName ?? "") \($0.category.title)".lowercased().contains(text) }
            .sorted { lhs, rhs in
                let left = (unread.contains(lhs.id) ? 0 : 1, lhs.status == .waitingForReporter ? 0 : 1)
                let right = (unread.contains(rhs.id) ? 0 : 1, rhs.status == .waitingForReporter ? 0 : 1)
                return left != right ? left < right : lhs.lastActivityAt > rhs.lastActivityAt
            }
    }

    var body: some View {
        List {
            Section {
                if isAgent {
                    Picker("Ansicht", selection: $scope) {
                        ForEach(Scope.allCases) { Text($0.rawValue).tag($0) }
                    }
                    .pickerStyle(.segmented)
                }
                Picker("Status", selection: $segment) {
                    ForEach(Segment.allCases) { segment in
                        let count = tickets.filter { segment.matches($0) }.count
                        Text(segment == .all || count == 0 ? segment.rawValue : "\(segment.rawValue) \(count)").tag(segment)
                    }
                }
                .pickerStyle(.segmented)
            }
            .listRowBackground(Color.clear)
            .listRowInsets(EdgeInsets(top: Space.xs, leading: 0, bottom: Space.xs, trailing: 0))
            if !waiting.isEmpty {
                Section("Wartet auf Übertragung") {
                    ForEach(waiting, id: \.id) { item in
                        Label(item.title, systemImage: "arrow.up.circle").foregroundStyle(Theme.textSecondary)
                    }
                }
            }
            Section {
                if offline {
                    Label("Die Liste ist nur mit Verbindung zum Server verfügbar.", systemImage: "wifi.slash").foregroundStyle(Theme.textSecondary)
                } else if loaded, visible.isEmpty {
                    EmptyStateView(symbol: segment == .open ? "checkmark.circle" : "tray", title: emptyTitle,
                                   message: scope == .mine ? "Bei Problemen mit App, Gerät oder Zugang hilft die IT." : nil,
                                   actionTitle: scope == .mine ? "Problem melden" : nil, actionSymbol: "exclamationmark.bubble") { reporting = true }
                }
                ForEach(visible) { ticket in
                    NavigationLink {
                        TicketDetailView(ticketId: ticket.id, isAgent: isAgent)
                    } label: {
                        TicketRow(ticket: ticket, isUnread: unread.contains(ticket.id), showsReporter: scope == .agent)
                    }
                }
            }
        }
        .searchable(text: $search, prompt: "Nummer, Titel, Melder")
        .navigationTitle("Helpdesk")
        .toolbar {
            ToolbarItem(placement: .primaryAction) {
                Button { reporting = true } label: { Label("Problem melden", systemImage: "square.and.pencil") }
                    .accessibilityIdentifier("newTicketButton")
            }
        }
        .sheet(isPresented: $reporting) {
            ReportProblemView { reporting = false; Task { await load() } }
        }
        .navigationDestination(item: $opened) { id in TicketDetailView(ticketId: id, isAgent: isAgent) }
        .task(id: "\(model.active?.sync?.revision ?? 0)|\(scope.rawValue)") { await load() }
        .refreshable { await load() }
        .onDisappear { Task { await TicketReadState.mark(tickets, model: model) } }
    }

    private var emptyTitle: String {
        switch segment {
        case .open: "Keine offenen Meldungen"
        case .waiting: "Keine Rückfragen"
        case .done: "Nichts erledigt"
        case .all: "Keine Meldungen"
        }
    }

    private func load() async {
        let local = (try? await model.helpdesk?.localReports()) ?? []
        waiting = local.filter { $0.state != .synced }.map { ($0.id, $0.title) }
        guard let client = model.active?.client else { return }
        if let store = model.active?.sync?.store { isAgent = await MyPermissions(store: store).load(using: client).contains("helpdesk.agent") }
        do throws(APIError) {
            tickets = scope == .agent ? try await client.agentTickets(filter: "all") : try await client.myTickets()
            offline = false
            if scope == .mine, let store = model.active?.sync?.store { unread = await TicketReadState(store: store).unread(tickets) }
            if shotScene == "meldung-detail", opened == nil { opened = tickets.first?.id }
            if shotScene == "meldung-bearbeitung", scope == .mine, isAgent { scope = .agent }
        } catch {
            offline = true
        }
        loaded = true
    }
}

extension TicketReadState {
    @MainActor
    static func mark(_ tickets: [TicketSummary], model: AppModel) async {
        guard let store = model.active?.sync?.store else { return }
        await TicketReadState(store: store).markRead(tickets)
    }

    /// Zahl der Meldungen mit neuer Antwort (für das Abzeichen im Reiter „Mehr“).
    @MainActor
    static func unreadCount(model: AppModel) async -> Int {
        guard let client = model.active?.client, let store = model.active?.sync?.store,
              let tickets = try? await client.myTickets() else { return 0 }
        return await TicketReadState(store: store).unread(tickets).count
    }
}

/// Zeile einer Meldung: Titel, Nummer, Status, Priorität (nur hoch/dringend), letzte Aktivität, Punkt bei neuer Antwort.
struct TicketRow: View {
    let ticket: TicketSummary
    var isUnread = false
    var showsReporter = false

    var body: some View {
        HStack(alignment: .top, spacing: Space.s) {
            StatusDot(color: isUnread ? Theme.accent : .clear, size: 9)
                .padding(.top, 6)
                .accessibilityHidden(!isUnread)
                .accessibilityLabel("Neue Antwort")
            VStack(alignment: .leading, spacing: Space.xs) {
                HStack(alignment: .firstTextBaseline) {
                    Text(ticket.title)
                        .font(isUnread ? Typo.bodyStrong : Typo.body)
                        .foregroundStyle(Theme.textPrimary)
                        .lineLimit(2)
                    Spacer(minLength: Space.s)
                    Text(ticket.lastActivityAt.formatted(.relative(presentation: .named)))
                        .font(Typo.meta).foregroundStyle(Theme.textSecondary).lineLimit(1)
                }
                HStack(spacing: Space.s) {
                    Text(ticket.number).font(Typo.meta.monospacedDigit()).foregroundStyle(Theme.textSecondary)
                    TicketStatusChip(status: ticket.status)
                    if ticket.priority == .high || ticket.priority == .urgent {
                        Chip(text: ticket.priority.title, symbol: "exclamationmark", tone: ticket.priority == .urgent ? .danger : .warning)
                    }
                }
                if showsReporter {
                    Text([ticket.reporterDisplayName, ticket.category.title, ticket.assigneeName.map { "→ \($0)" } ?? "nicht zugewiesen"]
                        .compactMap { $0 }.joined(separator: " · "))
                        .font(Typo.meta).foregroundStyle(Theme.textSecondary).lineLimit(1)
                }
            }
        }
        .padding(.vertical, Space.xxs)
        .accessibilityElement(children: .combine)
    }
}

/// Stand einer Meldung als Kapsel: wartet auf mich (gelb), gelöst (grün), sonst ruhig.
struct TicketStatusChip: View {
    let status: TicketStatus

    var body: some View {
        Chip(text: status.title, symbol: symbol, tone: tone)
            .accessibilityLabel("Status: \(status.title)")
    }

    private var tone: Chip.Tone {
        switch status {
        case .new, .inProgress: .accent
        case .waitingForReporter: .warning
        case .resolved, .closed: .success
        }
    }

    private var symbol: String {
        switch status {
        case .new: "tray"
        case .inProgress: "wrench.and.screwdriver"
        case .waitingForReporter: "questionmark.bubble"
        case .resolved: "checkmark.circle"
        case .closed: "lock"
        }
    }
}

/// Ein Ticket wie ein Chat: Kopf mit Status, Priorität, Kategorie, Melder, Bearbeiter und Alter; Verlauf mit
/// Sprechblasen (eigene rechts) und Ereignissen; unten das Antwortfeld mit Bildanhang. IT-Admins setzen Status,
/// Priorität und Zuweisung über das Menü oben rechts.
struct TicketDetailView: View {
    let ticketId: UUID
    var isAgent = false

    @Environment(AppModel.self) private var model
    @State private var detail: TicketDetail?
    @State private var reply = ""
    @State private var error: String?
    @State private var sending = false
    @State private var pickerItems: [PhotosPickerItem] = []
    @State private var attachments: [Data] = []
    @State private var resolving = false
    @State private var resolution = ""

    var body: some View {
        ScrollViewReader { proxy in
            ScrollView {
                LazyVStack(alignment: .leading, spacing: Space.m) {
                    if let detail {
                        header(detail)
                        ForEach(detail.history) { entry in
                            TicketTimelineRow(entry: entry, isOwn: isOwn(entry))
                                .id(entry.id)
                        }
                        if detail.summary.status == .resolved, !isAgent {
                            Button("Problem besteht weiter – wieder öffnen") { Task { await reopen() } }
                                .estrichButton(.secondary, fullWidth: true)
                        }
                    } else if let error {
                        EmptyStateView(symbol: "wifi.slash", title: "Nicht verfügbar", message: error)
                    } else {
                        ProgressView().frame(maxWidth: .infinity).padding(.top, Space.xxl)
                    }
                }
                .padding(Space.l)
                .readableWidth(760)
            }
            .background(Theme.canvas)
            .onChange(of: detail?.history.count) { _, _ in
                if let last = detail?.history.last { withAnimation { proxy.scrollTo(last.id, anchor: .bottom) } }
            }
        }
        .safeAreaInset(edge: .bottom) {
            if let detail, detail.summary.status != .closed { composer }
        }
        .navigationTitle(detail?.summary.number ?? "Meldung")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            if isAgent, let detail {
                ToolbarItem(placement: .primaryAction) { agentMenu(detail) }
            }
        }
        .alert("Lösung eintragen", isPresented: $resolving) {
            TextField("Was wurde getan?", text: $resolution)
            Button("Als gelöst markieren") { Task { await update(TicketUpdate(status: .resolved, resolution: resolution)) } }
            Button("Abbrechen", role: .cancel) {}
        }
        .task { await load() }
        .refreshable { await load() }
        .onChange(of: pickerItems) { _, items in
            Task {
                var images: [Data] = []
                for item in items {
                    if let data = try? await item.loadTransferable(type: Data.self), let image = UIImage(data: data) {
                        if let jpeg = ThumbnailLoader.downsample(data, to: 700)?.jpegData(compressionQuality: 0.8) ?? image.jpegData(compressionQuality: 0.7) {
                            images.append(jpeg)
                        }
                    }
                }
                attachments = images
            }
        }
    }

    private func isOwn(_ entry: TicketTimelineEntry) -> Bool {
        if let user = model.active?.user.id, let actor = entry.actorUserId { return actor == user }
        return isAgent ? !entry.isReporter : entry.isReporter
    }

    private func header(_ detail: TicketDetail) -> some View {
        let summary = detail.summary
        return Card(spacing: Space.s) {
            HStack(spacing: Space.s) {
                TicketStatusChip(status: summary.status)
                Chip(text: summary.priority.title, symbol: summary.priority == .urgent || summary.priority == .high ? "exclamationmark" : nil,
                     tone: summary.priority == .urgent ? .danger : (summary.priority == .high ? .warning : .neutral))
                Spacer(minLength: 0)
                Text(summary.number).font(Typo.metaStrong.monospacedDigit()).foregroundStyle(Theme.textSecondary)
            }
            Text(summary.title).font(Typo.section).foregroundStyle(Theme.textPrimary).fixedSize(horizontal: false, vertical: true)
            if let description = detail.description?.trimmingCharacters(in: .whitespacesAndNewlines), !description.isEmpty {
                Text(description).font(Typo.body).foregroundStyle(Theme.textPrimary).fixedSize(horizontal: false, vertical: true)
            }
            InsetDivider()
            InfoGrid(items: [
                InfoItem(label: "Kategorie", value: summary.category.title),
                InfoItem(label: "Gemeldet", value: summary.createdAt.formatted(.relative(presentation: .named))),
                InfoItem(label: "Melder", value: summary.reporterDisplayName ?? "Sie"),
                InfoItem(label: "Bearbeitung", value: summary.assigneeName ?? "noch offen"),
            ])
            if let resolution = detail.resolution?.trimmingCharacters(in: .whitespacesAndNewlines), !resolution.isEmpty {
                Callout(tone: .success, title: "Lösung", message: resolution, symbol: "checkmark.seal.fill")
            }
        }
    }

    private var composer: some View {
        VStack(spacing: Space.s) {
            if let error { Text(error).font(Typo.meta).foregroundStyle(Theme.danger).frame(maxWidth: .infinity, alignment: .leading) }
            if !attachments.isEmpty {
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack(spacing: Space.s) {
                        ForEach(attachments.indices, id: \.self) { index in
                            if let image = UIImage(data: attachments[index]) {
                                Image(uiImage: image).resizable().scaledToFill().frame(width: 56, height: 56)
                                    .clipShape(RoundedRectangle(cornerRadius: Radius.s, style: .continuous))
                                    .overlay(alignment: .topTrailing) {
                                        Button { attachments.remove(at: index) } label: {
                                            Image(systemName: "xmark.circle.fill").symbolRenderingMode(.palette)
                                                .foregroundStyle(.white, .black.opacity(0.6))
                                        }
                                        .accessibilityLabel("Anhang entfernen")
                                    }
                            }
                        }
                    }
                }
            }
            HStack(alignment: .bottom, spacing: Space.s) {
                PhotosPicker(selection: $pickerItems, maxSelectionCount: 3, matching: .images) {
                    Image(systemName: "paperclip").font(.body.weight(.semibold)).frame(width: 40, height: 40)
                }
                .accessibilityLabel("Bild anhängen")
                TextField(isAgent ? "Antwort an den Melder" : "Antwort an die IT", text: $reply, axis: .vertical)
                    .lineLimit(1...5)
                    .padding(.horizontal, Space.m).padding(.vertical, Space.s + 2)
                    .background(Theme.surface, in: RoundedRectangle(cornerRadius: 20, style: .continuous))
                    .accessibilityIdentifier("ticketReplyField")
                Button { Task { await send() } } label: {
                    if sending { ProgressView().frame(width: 40, height: 40) } else {
                        Image(systemName: "arrow.up.circle.fill").font(.system(size: 32)).frame(width: 40, height: 40)
                    }
                }
                .disabled(sending || (reply.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty && attachments.isEmpty))
                .accessibilityLabel("Antwort senden")
                .accessibilityIdentifier("sendReplyButton")
            }
        }
        .padding(.horizontal, Space.l)
        .padding(.vertical, Space.s)
        .background(.bar)
    }

    private func agentMenu(_ detail: TicketDetail) -> some View {
        Menu {
            Section("Status") {
                ForEach([TicketStatus.inProgress, .waitingForReporter, .closed], id: \.self) { status in
                    Button(status.title) { Task { await update(TicketUpdate(status: status)) } }
                        .disabled(detail.summary.status == status)
                }
                Button("Gelöst …") { resolution = ""; resolving = true }
            }
            Menu("Priorität") {
                ForEach(TicketPriority.allCases) { priority in
                    Button { Task { await update(TicketUpdate(priority: priority)) } } label: {
                        if detail.summary.priority == priority { Label(priority.title, systemImage: "checkmark") } else { Text(priority.title) }
                    }
                }
            }
            if let me = model.active?.user.id {
                Button("Mir zuweisen", systemImage: "person.crop.circle.badge.checkmark") { Task { await update(TicketUpdate(assigneeUserId: me)) } }
            }
            Button("Zuweisung aufheben", systemImage: "person.crop.circle.badge.xmark") { Task { await update(TicketUpdate(unassign: true)) } }
        } label: {
            Label("Bearbeiten", systemImage: "slider.horizontal.3")
        }
        .accessibilityIdentifier("ticketAgentMenu")
    }

    private func load() async {
        do throws(APIError) {
            detail = try await model.active?.client.ticket(ticketId)
            error = nil
            if let summary = detail?.summary { await TicketReadState.mark([summary], model: model) }
        } catch {
            self.error = error.message
        }
    }

    private func send() async {
        guard let client = model.active?.client else { return }
        sending = true
        defer { sending = false }
        do throws(APIError) {
            var ids: [UUID] = []
            for image in attachments { ids.append(try await client.uploadLooseImage(image)) }
            let text = reply.trimmingCharacters(in: .whitespacesAndNewlines)
            detail = try await client.comment(on: ticketId, text: text.isEmpty ? "Bild" : text, attachmentFileIds: ids)
            reply = ""
            attachments = []
            pickerItems = []
            error = nil
            if let summary = detail?.summary { await TicketReadState.mark([summary], model: model) }
        } catch {
            self.error = error == .offline ? "Keine Verbindung – Antwort bitte später senden." : (error.serverMessage ?? error.message)
        }
    }

    private func update(_ change: TicketUpdate) async {
        do throws(APIError) {
            detail = try await model.active?.client.updateTicket(ticketId, change)
            error = nil
        } catch {
            self.error = error.serverMessage ?? error.message
        }
    }

    private func reopen() async {
        do throws(APIError) {
            detail = try await model.active?.client.reopen(ticketId)
        } catch {
            self.error = error.message
        }
    }
}

/// Eintrag im Ticketverlauf: Antworten als Sprechblase (eigene rechts, in Markenblau), mit Bildern; Ereignisse als
/// ruhige, zentrierte Zeile.
struct TicketTimelineRow: View {
    let entry: TicketTimelineEntry
    let isOwn: Bool

    var body: some View {
        if let text = entry.text, entry.isComment {
            VStack(alignment: isOwn ? .trailing : .leading, spacing: 4) {
                if !isOwn {
                    Text(entry.actorName ?? "IT").font(Typo.labelStrong).foregroundStyle(Theme.textSecondary)
                }
                VStack(alignment: .leading, spacing: Space.s) {
                    Text(text).font(Typo.body)
                    if !entry.attachmentFileIds.isEmpty {
                        HStack(spacing: Space.s) {
                            ForEach(entry.attachmentFileIds, id: \.self) { TicketAttachment(fileId: $0) }
                        }
                    }
                }
                .padding(.horizontal, Space.m).padding(.vertical, Space.s + 2)
                .foregroundStyle(isOwn ? Theme.onAccent : Theme.textPrimary)
                .background(isOwn ? Theme.accentFill : Theme.surface, in: RoundedRectangle(cornerRadius: 18, style: .continuous))
                Text(entry.at.formatted(date: .abbreviated, time: .shortened)).font(.caption2).foregroundStyle(Theme.textSecondary)
            }
            .frame(maxWidth: .infinity, alignment: isOwn ? .trailing : .leading)
            .padding(isOwn ? .leading : .trailing, 48)
            .accessibilityElement(children: .combine)
        } else {
            HStack(spacing: Space.xs) {
                Image(systemName: entry.kind == "created" ? "plus.circle" : "arrow.triangle.swap")
                Text("\(entry.summary) · \(entry.at.formatted(date: .omitted, time: .shortened))")
            }
            .font(.caption)
            .foregroundStyle(Theme.textSecondary)
            .frame(maxWidth: .infinity)
            .multilineTextAlignment(.center)
        }
    }
}

/// Bildanhang einer Antwort (vom Server geladen).
struct TicketAttachment: View {
    let fileId: UUID
    @Environment(AppModel.self) private var model
    @State private var image: UIImage?

    var body: some View {
        Group {
            if let image {
                Image(uiImage: image).resizable().scaledToFill()
            } else {
                Image(systemName: "photo").foregroundStyle(Theme.textTertiary)
            }
        }
        .frame(width: 96, height: 96)
        .background(Theme.fill)
        .clipShape(RoundedRectangle(cornerRadius: Radius.s, style: .continuous))
        .task {
            guard let client = model.active?.client else { return }
            if let data: Data = try? await client.get("files/\(fileId.uuidString.lowercased())/content") {
                image = ThumbnailLoader.downsample(data, to: 96)
            }
        }
        .accessibilityLabel("Bildanhang")
    }
}
