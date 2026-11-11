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
    @State private var recording = false
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
            .listRowSeparator(.hidden)
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
            if isAgent {
                ToolbarItem(placement: .secondaryAction) {
                    Button { recording = true } label: { Label("Meldung aufnehmen", systemImage: "phone.badge.plus") }
                        .accessibilityIdentifier("recordTicketButton")
                }
            }
            ToolbarItem(placement: .primaryAction) {
                Button { reporting = true } label: { Label("Problem melden", systemImage: "square.and.pencil") }
                    .accessibilityIdentifier("newTicketButton")
            }
        }
        .sheet(isPresented: $reporting) {
            ReportProblemView { reporting = false; Task { await load() } }
        }
        .sheet(isPresented: $recording) {
            RecordTicketView { created in
                recording = false
                Task { await load() }
                if let created { opened = created }
            }
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
            if shotScene == "meldung-bewertung", opened == nil { opened = tickets.first(where: { $0.status == .resolved })?.id }
            if shotScene == "meldung-bearbeitung", scope == .mine, isAgent { scope = .agent }
            if shotScene == "meldung-it" || shotScene == "meldung-anleitung", opened == nil { opened = UUID(uuidString: DemoContent.ticketId) }
            if shotScene == "meldung-aufnehmen", isAgent, !recording { recording = true }
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
                    Text([ticket.reporterDisplayName, ticket.category.title, ticket.assigneeName.map { "zugewiesen an \($0)" } ?? "nicht zugewiesen"]
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
    @State private var stars = 0
    @State private var ratingComment = ""
    @State private var internalNote = false
    @State private var agents: [HelpdeskAgent] = []
    @State private var groups: [HelpdeskGroup] = []
    @State private var blocks: [CannedResponse] = []
    @State private var options: HelpdeskOptions?
    @State private var article: UUID?
    @Environment(\.shotScene) private var shotScene

    var body: some View {
        ScrollViewReader { proxy in
            ScrollView {
                LazyVStack(alignment: .leading, spacing: Space.m) {
                    if let detail {
                        header(detail)
                        ForEach(detail.history(includeInternal: isAgent)) { entry in
                            TicketTimelineRow(entry: entry, isOwn: isOwn(entry))
                                .id(entry.id)
                        }
                        if detail.isRatable { ratingCard(detail) }
                        if detail.summary.status == .resolved, !isAgent {
                            Button("Ticket wieder öffnen") { Task { await reopen() } }
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
        .navigationDestination(item: $article) { KnowledgeArticleView(articleId: $0) }
        .task { await load() }
        .task(id: isAgent) { if isAgent { await loadAgentData() } }
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
        // Ohne Benutzer-ID: Bin ich der Melder, sind die Einträge des Melders meine – sonst die der Bearbeitung.
        let reporter = detail?.summary.reporterDisplayName
        let iAmReporter = reporter == nil || reporter == model.active?.user.displayName
        return iAmReporter ? entry.isReporter : !entry.isReporter
    }

    /// Kenndaten im Kopf; die Bearbeitung sieht zusätzlich Gruppe und Eingang (Telefon, E-Mail, vor Ort).
    private func info(_ summary: TicketSummary) -> [InfoItem] {
        let category = summary.subcategoryName.map { "\(summary.category.title) › \($0)" } ?? summary.category.title
        var handler = summary.assigneeName ?? "noch offen"
        if isAgent, let group = summary.groupName { handler += " · \(group)" }
        var items = [
            InfoItem(label: "Kategorie", value: category),
            InfoItem(label: "Gemeldet", value: summary.createdAt.formatted(.relative(presentation: .named))),
            InfoItem(label: "Melder", value: summary.reporterDisplayName ?? "Sie"),
            InfoItem(label: "Bearbeitung", value: handler),
        ]
        if isAgent, let intake = summary.intakeText { items.append(InfoItem(label: "Eingang", value: intake)) }
        return items
    }

    private func header(_ detail: TicketDetail) -> some View {
        let summary = detail.summary
        return Card(spacing: Space.s) {
            HStack(spacing: Space.s) {
                TicketStatusChip(status: summary.status)
                Chip(text: summary.priority.title, symbol: summary.priority == .urgent || summary.priority == .high ? "exclamationmark" : nil,
                     tone: summary.priority == .urgent ? .danger : (summary.priority == .high ? .warning : .neutral))
                if isAgent, let hint = summary.slaHint {
                    Chip(text: hint, symbol: "clock", tone: summary.slaState == "overdue" ? .danger : .warning)
                }
                Spacer(minLength: 0)
                Text(summary.number).font(Typo.metaStrong.monospacedDigit()).foregroundStyle(Theme.textSecondary)
            }
            Text(summary.title).font(Typo.section).foregroundStyle(Theme.textPrimary).fixedSize(horizontal: false, vertical: true)
            if let description = detail.description?.trimmingCharacters(in: .whitespacesAndNewlines), !description.isEmpty {
                Text(description).font(Typo.body).foregroundStyle(Theme.textPrimary).fixedSize(horizontal: false, vertical: true)
            }
            InsetDivider()
            InfoGrid(items: info(summary))
            if let resolution = detail.resolution?.trimmingCharacters(in: .whitespacesAndNewlines), !resolution.isEmpty {
                Callout(tone: .success, title: "Lösung", message: resolution, symbol: "checkmark.seal.fill")
            }
            if let merged = detail.mergedIntoNumber {
                Callout(tone: .info, title: "Zusammengeführt", message: "Diese Meldung wurde mit \(merged) zusammengeführt. Antworten erhalten Sie dort.")
            }
            if !detail.linkedArticles.isEmpty {
                InsetDivider()
                Text("Anleitung der IT").font(Typo.labelStrong).foregroundStyle(Theme.textSecondary)
                ForEach(detail.linkedArticles) { linked in
                    Button { article = linked.id } label: {
                        HStack(spacing: Space.s) {
                            Image(systemName: "book.fill").foregroundStyle(Theme.accent)
                            Text(linked.title).font(Typo.body).foregroundStyle(Theme.textPrimary).multilineTextAlignment(.leading)
                            Spacer(minLength: 0)
                            Image(systemName: "chevron.right").font(.caption.weight(.semibold)).foregroundStyle(Theme.textTertiary)
                        }
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .accessibilityIdentifier("ticketArticle")
                }
            }
        }
    }

    /// Zufriedenheit nach der Lösung: 1–5 Sterne und ein freiwilliger Kommentar.
    private func ratingCard(_ detail: TicketDetail) -> some View {
        Card(spacing: Space.s) {
            Text(detail.rating == nil ? "Wie zufrieden sind Sie mit der Lösung?" : "Ihre Bewertung")
                .font(Typo.bodyStrong).foregroundStyle(Theme.textPrimary)
            HStack(spacing: Space.s) {
                ForEach(1...5, id: \.self) { value in
                    Button { stars = value } label: {
                        Image(systemName: value <= stars ? "star.fill" : "star").font(.title2)
                            .foregroundStyle(value <= stars ? Theme.warning : Theme.textSecondary)
                    }
                    .buttonStyle(.plain)
                    .accessibilityLabel("\(value) von 5 Sternen")
                }
            }
            .accessibilityIdentifier("ticketRating")
            TextField("Kommentar (freiwillig)", text: $ratingComment, axis: .vertical).lineLimit(1...3)
            Button(detail.rating == nil ? "Bewerten" : "Bewertung ändern") { Task { await rate() } }
                .estrichButton(.primary, fullWidth: true)
                .disabled(stars == 0)
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
            if isAgent {
                HStack(spacing: Space.s) {
                    Toggle(isOn: $internalNote) { Label("Interne Notiz", systemImage: "lock") }
                        .toggleStyle(.button)
                        .accessibilityIdentifier("ticketInternalToggle")
                    if !blocks.isEmpty {
                        Menu {
                            ForEach(blocks) { block in
                                Button(block.isPersonal == true ? "\(block.name) (eigener)" : block.name) { insert(block) }
                            }
                        } label: { Label("Textbaustein", systemImage: "text.badge.plus") }
                        .accessibilityIdentifier("ticketBlocksMenu")
                    }
                    Spacer(minLength: 0)
                }
                .font(Typo.meta)
            }
            HStack(alignment: .bottom, spacing: Space.s) {
                PhotosPicker(selection: $pickerItems, maxSelectionCount: 3, matching: .images) {
                    Image(systemName: "paperclip").font(.body.weight(.semibold)).frame(width: 40, height: 40)
                }
                .accessibilityLabel("Bild anhängen")
                TextField(internalNote ? "Interne Notiz (nur für die IT)" : (isAgent ? "Öffentliche Antwort an den Melder" : "Antwort an die IT"),
                          text: $reply, axis: .vertical)
                    .lineLimit(1...5)
                    .padding(.horizontal, Space.m).padding(.vertical, Space.s + 2)
                    .background(internalNote ? Theme.warningSoft : Theme.surface, in: RoundedRectangle(cornerRadius: 20, style: .continuous))
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
            Menu("Zuständig") {
                if let me = model.active?.user.id {
                    Button("Mir zuweisen", systemImage: "person.crop.circle.badge.checkmark") { Task { await update(TicketUpdate(assigneeUserId: me)) } }
                }
                ForEach(agents.filter { $0.id != model.active?.user.id }) { agent in
                    Button { Task { await update(TicketUpdate(assigneeUserId: agent.id)) } } label: {
                        if detail.summary.assigneeUserId == agent.id { Label(agent.displayName, systemImage: "checkmark") } else { Text(agent.displayName) }
                    }
                }
                Button("Zuweisung aufheben", systemImage: "person.crop.circle.badge.xmark") { Task { await update(TicketUpdate(unassign: true)) } }
            }
            if !groups.isEmpty {
                Menu("Gruppe") {
                    ForEach(groups) { group in
                        Button { Task { await update(TicketUpdate(groupId: group.id)) } } label: {
                            if detail.summary.groupId == group.id { Label(group.name, systemImage: "checkmark") } else { Text(group.name) }
                        }
                    }
                    Button("Keine Gruppe") { Task { await update(TicketUpdate(clearGroup: true)) } }
                }
            }
            Menu("Kategorie") {
                ForEach(TicketCategory.allCases) { category in
                    Button { Task { await update(TicketUpdate(category: category)) } } label: {
                        if detail.summary.category == category { Label(category.title, systemImage: "checkmark") } else { Text(category.title) }
                    }
                }
            }
            if let subcategories = options?.subcategories(of: detail.summary.category), !subcategories.isEmpty {
                Menu("Unterkategorie") {
                    ForEach(subcategories) { sub in
                        Button { Task { await update(TicketUpdate(subcategoryId: sub.id)) } } label: {
                            if detail.summary.subcategoryId == sub.id { Label(sub.name, systemImage: "checkmark") } else { Text(sub.name) }
                        }
                    }
                    Button("Keine") { Task { await update(TicketUpdate(clearSubcategory: true)) } }
                }
            }
        } label: {
            Label("Bearbeiten", systemImage: "slider.horizontal.3")
        }
        .accessibilityIdentifier("ticketAgentMenu")
    }

    /// Bearbeiter, Gruppen, Textbausteine und Unterkategorien für die Bearbeitung.
    private func loadAgentData() async {
        guard let client = model.active?.client else { return }
        agents = (try? await client.helpdeskAgents()) ?? []
        groups = (try? await client.helpdeskGroups()) ?? []
        blocks = (try? await client.cannedResponses()) ?? []
        options = try? await client.helpdeskOptions()
    }

    /// Textbaustein mit Platzhaltern ins Antwortfeld übernehmen.
    private func insert(_ block: CannedResponse) {
        guard let summary = detail?.summary else { return }
        let me = agents.first { $0.id == model.active?.user.id }?.displayName ?? model.active?.user.displayName
        let text = CannedResponse.fill(block.text, reporter: summary.reporterDisplayName, ticket: summary.number, title: summary.title, agent: me)
        let current = reply.trimmingCharacters(in: .whitespacesAndNewlines)
        reply = current.isEmpty ? text : current + "\n\n" + text
    }

    private func rate() async {
        do throws(APIError) {
            detail = try await model.active?.client.rate(ticketId, rating: stars, comment: ratingComment)
            error = nil
        } catch {
            self.error = error.serverMessage ?? error.message
        }
    }

    private func load() async {
        do throws(APIError) {
            detail = try await model.active?.client.ticket(ticketId)
            if let detail { stars = detail.rating ?? 0; ratingComment = detail.ratingComment ?? "" }
            if shotScene == "meldung-anleitung", article == nil, let first = detail?.linkedArticles.first { article = first.id }
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
            detail = try await client.comment(on: ticketId, text: text.isEmpty ? "Bild" : text, attachmentFileIds: ids, isInternal: isAgent && internalNote)
            reply = ""
            internalNote = false
            attachments = []
            pickerItems = []
            error = nil
            if let summary = detail?.summary { await TicketReadState.mark([summary], model: model) }
        } catch {
            self.error = error == .offline ? "Keine Verbindung. Bitte die Antwort später senden." : (error.serverMessage ?? error.message)
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
                if !isOwn || entry.isInternal {
                    Text(entry.isInternal ? "\(entry.actorName ?? "IT") · interne Notiz" : (entry.actorName ?? "IT"))
                        .font(Typo.labelStrong).foregroundStyle(Theme.textSecondary)
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
                .foregroundStyle(isOwn && !entry.isInternal ? Theme.onAccent : Theme.textPrimary)
                .background(entry.isInternal ? Theme.warningSoft : (isOwn ? Theme.accentFill : Theme.surface),
                            in: RoundedRectangle(cornerRadius: 18, style: .continuous))
                Text(entry.at.shortStamp).font(.caption2).foregroundStyle(Theme.textSecondary)
            }
            .frame(maxWidth: .infinity, alignment: isOwn ? .trailing : .leading)
            .padding(isOwn ? .leading : .trailing, 48)
            .accessibilityElement(children: .combine)
        } else {
            // Symbol im Text, damit es beim Umbruch mit der ersten Zeile mitläuft; Zeitpunkt mit Tag (Verlauf über Tage).
            Text("\(Image(systemName: entry.kind == "created" ? "plus.circle" : "arrow.triangle.swap")) \(entry.summary) · \(entry.at.shortStamp)")
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

/// Anleitung aus der Wissensdatenbank, die die IT mit einer Meldung verknüpft hat.
struct KnowledgeArticleView: View {
    let articleId: UUID
    @Environment(AppModel.self) private var model
    @State private var article: KnowledgeArticle?
    @State private var error: String?

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: Space.m) {
                if let article {
                    Text(article.title).font(Typo.section).foregroundStyle(Theme.textPrimary).fixedSize(horizontal: false, vertical: true)
                    if let category = article.category { Chip(text: category.title, symbol: "tag", tone: .neutral) }
                    ForEach(Array(article.paragraphs.enumerated()), id: \.offset) { _, paragraph in
                        Text(paragraph).font(Typo.body).foregroundStyle(Theme.textPrimary).fixedSize(horizontal: false, vertical: true)
                    }
                } else if let error {
                    EmptyStateView(symbol: "book.closed", title: "Anleitung nicht verfügbar", message: error)
                } else {
                    ProgressView().frame(maxWidth: .infinity).padding(.top, Space.xxl)
                }
            }
            .padding(Space.l)
            .readableWidth(760)
        }
        .background(Theme.canvas)
        .navigationTitle("Anleitung")
        .navigationBarTitleDisplayMode(.inline)
        .task {
            do throws(APIError) {
                article = try await model.active?.client.knowledgeArticle(articleId)
            } catch {
                self.error = error == .offline ? "Keine Verbindung zum Server." : (error.serverMessage ?? error.message)
            }
        }
    }
}

/// „Meldung aufnehmen“ für die IT: Nach Anruf, E-Mail oder Besuch ein Ticket im Namen des Melders anlegen. Der Melder sieht
/// es unter „Meine Meldungen“ und erhält die Antworten; ohne Zugang genügt sein Name.
struct RecordTicketView: View {
    let onDone: (UUID?) -> Void

    @Environment(AppModel.self) private var model
    @State private var channel: TicketChannel = .phone
    @State private var query = ""
    @State private var results: [HelpdeskReporterPerson] = []
    @State private var reporter: HelpdeskReporterPerson?
    @State private var title = ""
    @State private var details = ""
    @State private var category: TicketCategory = .other
    @State private var priority: TicketPriority = .normal
    @State private var blocked = false
    @State private var assignToMe = true
    @State private var sending = false
    @State private var error: String?

    private var reporterName: String { query.trimmingCharacters(in: .whitespacesAndNewlines) }
    private var canSend: Bool {
        !title.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty && (reporter != nil || !reporterName.isEmpty) && !sending
    }

    var body: some View {
        NavigationStack {
            Form {
                Section("Eingang") {
                    Picker("Eingang", selection: $channel) {
                        ForEach(TicketChannel.allCases) { Label($0.title, systemImage: $0.symbol).tag($0) }
                    }
                    .pickerStyle(.segmented)
                    .accessibilityIdentifier("recordChannelPicker")
                }
                Section {
                    if let reporter {
                        HStack {
                            VStack(alignment: .leading, spacing: 2) {
                                Text(reporter.displayName).font(Typo.bodyStrong)
                                Text([reporter.userName, reporter.email].compactMap { $0 }.joined(separator: " · ")).font(Typo.meta).foregroundStyle(Theme.textSecondary)
                            }
                            Spacer()
                            Button("Ändern") { self.reporter = nil }
                        }
                    } else {
                        TextField("Name, Benutzername, E-Mail oder Telefon", text: $query)
                            .textInputAutocapitalization(.words)
                            .accessibilityIdentifier("recordReporterField")
                        ForEach(results.prefix(6)) { person in
                            Button { reporter = person } label: {
                                VStack(alignment: .leading, spacing: 2) {
                                    Text(person.displayName).foregroundStyle(Theme.textPrimary)
                                    Text([person.userName, person.email ?? person.phone].compactMap { $0 }.joined(separator: " · "))
                                        .font(Typo.meta).foregroundStyle(Theme.textSecondary)
                                }
                            }
                        }
                    }
                } header: {
                    Text("Melder")
                } footer: {
                    if reporter == nil { Text("Ohne Zugang zu EstrichONE: Namen eintragen, dann wird er als Melder vermerkt.") }
                }
                Section("Problem") {
                    TextField("Kurzbeschreibung", text: $title, axis: .vertical)
                        .accessibilityIdentifier("recordTitleField")
                    TextField("Was schildert der Melder?", text: $details, axis: .vertical).lineLimit(3...8)
                    Picker("Kategorie", selection: $category) {
                        ForEach(TicketCategory.allCases) { Text($0.title).tag($0) }
                    }
                    Picker("Priorität", selection: $priority) {
                        ForEach(TicketPriority.allCases) { Text($0.title).tag($0) }
                    }
                    Toggle("Melder kann deswegen nicht arbeiten", isOn: $blocked)
                }
                Section {
                    Toggle("Selbst übernehmen", isOn: $assignToMe)
                } footer: {
                    Text("Sonst ordnen die Regeln das Ticket einer Gruppe oder Person zu.")
                }
                if let error {
                    Section { Text(error).foregroundStyle(Theme.danger) }
                }
            }
            .navigationTitle("Meldung aufnehmen")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Abbrechen") { onDone(nil) } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Anlegen") { Task { await send() } }
                        .disabled(!canSend)
                        .accessibilityIdentifier("recordSendButton")
                }
            }
            .task(id: query) {
                guard reporter == nil else { return }
                try? await Task.sleep(for: .milliseconds(300))
                results = (try? await model.active?.client.helpdeskReporters(query: query)) ?? []
            }
        }
    }

    private func send() async {
        guard let client = model.active?.client else { return }
        sending = true
        defer { sending = false }
        let intake = TicketIntake(title: title, description: details, category: category, channel: channel, reporterUserId: reporter?.id,
                                  reporterName: reporter == nil ? reporterName : nil, priority: priority, reporterBlocked: blocked,
                                  assigneeUserId: assignToMe ? model.active?.user.id : nil)
        do throws(APIError) {
            let created = try await client.recordTicket(intake)
            onDone(created.summary.id)
        } catch {
            self.error = error == .offline ? "Keine Verbindung. Meldungen für andere lassen sich nur online aufnehmen." : (error.serverMessage ?? error.message)
        }
    }
}
