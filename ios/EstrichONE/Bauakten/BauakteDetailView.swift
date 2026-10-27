import EstrichCore
import QuickLook
import SwiftUI

/// Bauakte – das Herzstück. Auf einen Blick: Status, Adresse mit Route, Kunde und Ansprechpartner mit Anruf, nächster
/// Einsatz mit Kolonne, Estrich-Kenndaten, offene Mängel und Aufgaben, die neuesten Fotos und die letzte Aktivität.
/// Darunter alle Bereiche (Fotos und Dokumente, Aufgaben, Bautagebuch, Protokolle, Basisdaten, Kontakte, Notizen).
/// Breite Darstellung (iPad quer): Übersicht links, gewählter Bereich rechts. Alles offline aus der lokalen Ablage.
struct BauakteDetailView: View {
    let bauakteId: UUID

    @Environment(AppModel.self) private var model
    @Environment(\.shotScene) private var shotScene
    @Environment(\.openURL) private var openURL
    @State private var content: BauakteContent?
    @State private var loaded = false
    @State private var schema: FormSchema?
    @State private var lists = ValueLists([])
    @State private var action: BauakteAction?
    /// Schmale Darstellung: geöffneter Bereich.
    @State private var area: BauakteArea?
    /// Breite Darstellung: Bereich in der rechten Spalte.
    @State private var wideArea: BauakteArea = .media
    @State private var toast: String?
    @State private var cancelling = false
    @State private var cancelReason = ""
    @State private var statusError: String?
    @State private var viewing: FileItem?
    @State private var playing: FileItem?
    @State private var pdfURL: URL?
    @State private var loadingPDF = false
    @State private var pdfMessage: String?
    @State private var showsNavigationTitle = false

    var body: some View {
        Group {
            if let content {
                GeometryReader { proxy in
                    if proxy.size.width >= 900 {
                        HStack(spacing: 0) {
                            overview(content, wide: true)
                                .frame(width: min(460, proxy.size.width * 0.45))
                            Divider()
                            BauakteAreaView(area: wideArea, content: content, schema: schema, lists: lists, embedded: true) { await reload() }
                                .id(wideArea)
                        }
                    } else {
                        overview(content, wide: false)
                    }
                }
                .bauakteActions(content.bauakte, customerName: content.customer?.name, schema: schema, lists: lists, action: $action) { message in
                    if let message { toast = message }
                    Task { await reload() }
                }
                .navigationDestination(item: $area) { area in
                    BauakteAreaView(area: area, content: self.content ?? content, schema: schema, lists: lists) { await reload() }
                }
                .fullScreenCover(item: $viewing) { file in
                    PhotoViewer(photos: content.photos.filter(\.isPhoto), selection: file.id) { Task { await reload() } }
                }
                .fullScreenCover(item: $playing) { file in VideoPreview(file: file) }
                .toolbar { ToolbarItem(placement: .primaryAction) { moreMenu(content.bauakte) } }
                .alert("Bauakte stornieren?", isPresented: $cancelling) {
                    TextField("Grund (Pflicht)", text: $cancelReason)
                        .accessibilityIdentifier("cancelReasonField")
                    Button("Stornieren", role: .destructive) { Task { await change(to: .cancelled, reason: cancelReason) } }
                        .disabled(cancelReason.trimmingCharacters(in: .whitespaces).isEmpty)
                    Button("Abbrechen", role: .cancel) {}
                } message: {
                    Text("Die Bauakte bleibt mit Verlauf erhalten. Löschen ist nur im Büro möglich.")
                }
                .alert("PDF nicht verfügbar", isPresented: Binding(get: { pdfMessage != nil }, set: { if !$0 { pdfMessage = nil } })) {
                    Button("OK", role: .cancel) {}
                } message: {
                    Text(pdfMessage ?? "")
                }
                .quickLookPreview($pdfURL)
                .toast($toast)
            } else if !loaded {
                SkeletonList()
            } else {
                MissingBauakteView(bauakteId: bauakteId) { await reload() }
            }
        }
        // Titel erst in der Leiste, wenn der Kopf weggescrollt ist – sonst stünde er doppelt da.
        .navigationTitle(showsNavigationTitle ? (content?.bauakte.title ?? "Bauakte") : "")
        .navigationBarTitleDisplayMode(.inline)
        .task(id: model.active?.sync?.revision) { await reload() }
        .task {
            try? await model.active?.service?.markOpened(bauakteId)
            // Namen für Verfasser und Zuständige; danach neu anzeigen.
            if !(await DirectoryCache.load(model)).isEmpty { await reload() }
        }
        .refreshable {
            await model.active?.sync?.run()
            await reload()
        }
    }

    // MARK: Übersicht

    private func overview(_ content: BauakteContent, wide: Bool) -> some View {
        ScrollViewReader { proxy in
            ScrollView {
                VStack(alignment: .leading, spacing: Space.xl) {
                    header(content)
                    highlights(content)
                    facts(content)
                    actionBar
                    openPoints(content)
                    photoStrip(content)
                    keyData(content)
                    areas(content, wide: wide)
                        .id("areas")
                    activitySection(content)
                    footer(content)
                }
                .padding(.horizontal, Space.l)
                .padding(.top, Space.s)
                .padding(.bottom, Space.xxl)
            }
            .scrolledPastHeader($showsNavigationTitle)
            .background(Theme.canvas)
            .task(id: loaded) {
                guard loaded, shotScene == "bauakte-unten" else { return }
                try? await Task.sleep(for: .milliseconds(300))
                proxy.scrollTo("areas", anchor: .top)
            }
        }
    }

    private func header(_ content: BauakteContent) -> some View {
        let bauakte = content.bauakte
        let badges = schema?.badges(for: bauakte.projectData, lists: lists) ?? bauakte.project.badges
        return VStack(alignment: .leading, spacing: Space.s) {
            HStack(alignment: .center, spacing: Space.s) {
                Text(bauakte.number ?? "Neu · noch ohne Nummer")
                    .font(Typo.subheadStrong.monospacedDigit())
                    .foregroundStyle(Theme.textSecondary)
                    .accessibilityIdentifier("bauakteNumber")
                SyncStateBadge(state: bauakte.syncState)
                Spacer(minLength: Space.s)
                statusMenu(bauakte)
            }
            Text(bauakte.title)
                .font(Typo.title)
                .foregroundStyle(Theme.textPrimary)
                .fixedSize(horizontal: false, vertical: true)
                .accessibilityAddTraits(.isHeader)
                .accessibilityIdentifier("bauakteTitle")
            if !badges.isEmpty {
                ChipFlow {
                    ForEach(badges, id: \.self) { badge in
                        Chip(text: badge)
                    }
                }
                .padding(.top, 2)
                .accessibilityIdentifier("projectBadges")
            }
        }
        .padding(.horizontal, Space.xs)
    }

    @ViewBuilder
    private func highlights(_ content: BauakteContent) -> some View {
        let bauakte = content.bauakte
        let warnings = Array(Set(content.todayAssignments.flatMap(\.warnings))).sorted()
        if bauakte.status == .cancelled || statusError != nil || !content.pinnedNotes.isEmpty || !warnings.isEmpty {
            VStack(spacing: Space.s) {
                if bauakte.status == .cancelled {
                    Callout(tone: .danger, title: "Storniert", message: bauakte.cancellationReason ?? "ohne Angabe eines Grundes", symbol: "xmark.circle.fill")
                }
                if let statusError {
                    Callout(tone: .danger, message: statusError)
                }
                ForEach(warnings, id: \.self) { warning in
                    Callout(tone: .warning, message: warning)
                }
                ForEach(content.pinnedNotes) { note in
                    Callout(tone: .info, title: "Angeheftet", message: note.text.compactText ?? "", symbol: "pin.fill")
                }
            }
        }
    }

    // MARK: Auf einen Blick

    private struct Fact: Identifiable {
        let id: String
        let symbol: String
        let title: String
        var subtitle: String?
        var tint: Color = Theme.accent
        var link: URL?
        var linkTitle = ""
        var linkSymbol = ""
        var tap: (() -> Void)?
    }

    private func factList(_ content: BauakteContent) -> [Fact] {
        let bauakte = content.bauakte
        var list: [Fact] = []
        if let address = bauakte.addressLine {
            list.append(Fact(id: "address", symbol: "mappin.and.ellipse", title: address, subtitle: "Baustelle",
                             link: MapsLink.url(for: bauakte), linkTitle: "Route starten", linkSymbol: "arrow.triangle.turn.up.right.diamond.fill"))
        } else {
            list.append(Fact(id: "address", symbol: "mappin.slash", title: "Adresse fehlt",
                             subtitle: bauakte.hasLocation ? "Standort ist gespeichert" : "Bitte ergänzen, sobald bekannt", tint: Theme.warning,
                             link: MapsLink.url(for: bauakte), linkTitle: "Route starten", linkSymbol: "arrow.triangle.turn.up.right.diamond.fill"))
        }
        if let customer = content.customer {
            list.append(Fact(id: "customer", symbol: customer.isCompany ? "building.2" : "person", title: customer.name,
                             subtitle: ["Kunde", customer.customerNumber].compactMap { $0 }.joined(separator: " · "),
                             link: ContactLink.phone(customer.phone), linkTitle: "Kunde anrufen", linkSymbol: "phone.fill"))
        } else {
            list.append(Fact(id: "customer", symbol: "person.crop.circle.badge.questionmark", title: "Kein Kunde zugeordnet",
                             subtitle: "Antippen zum Zuordnen", tint: Theme.warning, tap: { area = .contacts }))
        }
        if bauakte.siteContactName != nil || bauakte.siteContactPhone != nil {
            list.append(Fact(id: "site", symbol: "person.badge.shield.checkmark", title: bauakte.siteContactName ?? bauakte.siteContactPhone ?? "",
                             subtitle: "Ansprechpartner vor Ort", link: ContactLink.phone(bauakte.siteContactPhone),
                             linkTitle: "Ansprechpartner anrufen", linkSymbol: "phone.fill"))
        }
        if let next = content.nextAssignmentText {
            list.append(Fact(id: "next", symbol: "calendar.badge.clock", title: next.title, subtitle: next.subtitle))
        } else if let period = Self.period(bauakte) {
            list.append(Fact(id: "next", symbol: "calendar", title: "Geplant: \(period)", subtitle: "Ausführungszeitraum"))
        }
        return list
    }

    private func facts(_ content: BauakteContent) -> some View {
        let list = factList(content)
        return VStack(spacing: 0) {
            ForEach(Array(list.enumerated()), id: \.element.id) { index, fact in
                if index > 0 { InsetDivider(leading: Space.l + Size.iconBadge + Space.m) }
                factRow(fact)
            }
        }
        .card()
    }

    @ViewBuilder
    private func factRow(_ fact: Fact) -> some View {
        let row = HStack(spacing: Space.m) {
            IconBadge(symbol: fact.symbol, tint: fact.tint, background: fact.tint.opacity(0.12))
            VStack(alignment: .leading, spacing: 2) {
                Text(fact.title)
                    .font(Typo.body)
                    .foregroundStyle(Theme.textPrimary)
                    .fixedSize(horizontal: false, vertical: true)
                if let subtitle = fact.subtitle {
                    Text(subtitle)
                        .font(Typo.subhead)
                        .foregroundStyle(Theme.textSecondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            if let link = fact.link {
                Link(destination: link) { Label(fact.linkTitle, systemImage: fact.linkSymbol) }
                    .buttonStyle(IconButtonStyle())
                    .accessibilityLabel(fact.linkTitle)
                    .accessibilityIdentifier("fact-\(fact.id)-link")
            } else if fact.tap != nil {
                Chevron()
            }
        }
        .padding(.horizontal, Space.l)
        .padding(.vertical, Space.m)
        .contentShape(Rectangle())
        if let tap = fact.tap {
            Button(action: tap) { row }.buttonStyle(RowButtonStyle())
        } else {
            row
        }
    }

    private var actionBar: some View {
        HStack(spacing: Space.s) {
            Button { action = .photo } label: { Label("Foto", systemImage: "camera.fill") }
                .buttonStyle(ActionTileStyle(rank: .primary))
                .accessibilityIdentifier("addPhotoButton")
            Button { action = .scan } label: { Label("Scannen", systemImage: "doc.viewfinder") }
                .buttonStyle(ActionTileStyle())
                .accessibilityIdentifier("scanDocumentButton")
            Button { action = .diary } label: { Label("Tagebuch", systemImage: "book") }
                .buttonStyle(ActionTileStyle())
                .accessibilityIdentifier("addDiaryButton")
            Button { action = .defect } label: { Label("Mangel", systemImage: "exclamationmark.triangle") }
                .buttonStyle(ActionTileStyle())
                .accessibilityIdentifier("addDefectButton")
            MoreCaptureMenu(perform: { action = $0 }) {
                Label("Mehr", systemImage: "ellipsis")
            }
            .buttonStyle(ActionTileStyle())
            .accessibilityIdentifier("addMenu")
        }
    }

    // MARK: Offene Punkte, Fotos, Kenndaten

    @ViewBuilder
    private func openPoints(_ content: BauakteContent) -> some View {
        let open = content.openItems.sorted { lhs, rhs in
            if (lhs.kind == .defect) != (rhs.kind == .defect) { return lhs.kind == .defect }
            return (lhs.dueDate ?? "9999") < (rhs.dueDate ?? "9999")
        }
        if !open.isEmpty {
            VStack(alignment: .leading, spacing: Space.m) {
                SectionHeading("Offene Punkte") {
                    MoreLink("Alle (\(open.count))") { show(.work) }
                }
                VStack(spacing: 0) {
                    ForEach(Array(open.prefix(3).enumerated()), id: \.element.id) { index, item in
                        if index > 0 { InsetDivider(leading: 60) }
                        WorkItemRow(item: item, assigneeName: item.assigneeUserId.flatMap { content.names[$0] }) {
                            Task { await toggle(item) }
                        }
                        .padding(.horizontal, Space.s)
                    }
                }
                .card()
            }
        }
    }

    private func photoStrip(_ content: BauakteContent) -> some View {
        VStack(alignment: .leading, spacing: Space.m) {
            SectionHeading("Fotos") {
                if !content.photos.isEmpty {
                    MoreLink("Alle (\(content.photos.count))") { show(.media) }
                }
            }
            ScrollView(.horizontal, showsIndicators: false) {
                HStack(spacing: Space.s) {
                    Button { action = .photo } label: {
                        VStack(spacing: Space.xs) {
                            Image(systemName: "camera.fill").font(.title3.weight(SymbolWeight.control))
                            Text(content.photos.isEmpty ? "Erstes Foto" : "Foto").font(Typo.metaStrong)
                        }
                        .foregroundStyle(Theme.accent)
                        .frame(width: 96, height: 96)
                        .background(Theme.accentSoft, in: RoundedRectangle(cornerRadius: Radius.m, style: .continuous))
                    }
                    .buttonStyle(TileButtonStyle())
                    ForEach(Array(content.photos.prefix(12).enumerated()), id: \.element.id) { index, file in
                        Button {
                            if file.isVideo { playing = file } else { viewing = file }
                        } label: {
                            PhotoThumbnail(file: file, radius: Radius.m, showsCaption: false)
                                .frame(width: 96, height: 96)
                        }
                        .buttonStyle(.plain)
                        .accessibilityLabel(file.caption ?? (file.isVideo ? "Video" : "Foto"))
                        .accessibilityIdentifier("photoThumb-\(index)")
                    }
                }
            }
            .scrollClipDisabled()
        }
    }

    private func projectItems(_ bauakte: Bauakte) -> [InfoItem] {
        let rows = schema.map { ProjectSection.items(bauakte, schema: $0, lists: lists) } ?? ProjectSection.items(bauakte)
        return rows.filter { $0.title != "Geplant" }.map { InfoItem(label: $0.title, value: $0.value, symbol: $0.symbol) }
    }

    private func keyData(_ content: BauakteContent) -> some View {
        let items = projectItems(content.bauakte)
        return VStack(alignment: .leading, spacing: Space.m) {
            SectionHeading("Estrich und Bauvorhaben") {
                Button("Bearbeiten") { action = .project }
                    .accessibilityIdentifier("editProjectButton")
            }
            Card {
                if items.isEmpty {
                    EmptyStateView(symbol: "list.bullet.rectangle", title: "Noch keine Angaben",
                                   message: "Fläche, Estrichart, Dicke und Heizung – für Kolonne und Büro auf einen Blick.",
                                   actionTitle: "Angaben erfassen", actionSymbol: "pencil") { action = .project }
                } else {
                    InfoGrid(items: Array(items.prefix(8)))
                    if items.count > 8 {
                        MoreLink("Alle \(items.count) Angaben") { show(.project) }
                    }
                }
            }
        }
    }

    // MARK: Bereiche, Aktivität

    private func areas(_ content: BauakteContent, wide: Bool) -> some View {
        let projectCount = projectItems(content.bauakte).count
        return VStack(alignment: .leading, spacing: Space.m) {
            SectionHeading("Alles zur Baustelle")
            VStack(spacing: 0) {
                ForEach(Array(BauakteArea.allCases.enumerated()), id: \.element) { index, item in
                    if index > 0 { InsetDivider(leading: Space.l + Size.iconBadge + Space.m) }
                    let summary = item.summary(content, projectCount: projectCount)
                    let selected = wide && wideArea == item
                    Button { if wide { wideArea = item } else { area = item } } label: {
                        HStack(spacing: Space.m) {
                            IconBadge(symbol: item.symbol)
                            VStack(alignment: .leading, spacing: 2) {
                                Text(item.title).font(Typo.body).foregroundStyle(Theme.textPrimary)
                                Text(summary.text)
                                    .font(Typo.subhead)
                                    .foregroundStyle(summary.isMissing ? Theme.warning : Theme.textSecondary)
                                    .lineLimit(1)
                            }
                            .frame(maxWidth: .infinity, alignment: .leading)
                            Chevron()
                        }
                        .padding(.horizontal, Space.l)
                        .padding(.vertical, Space.m)
                        .background(selected ? Theme.accentSoft : .clear)
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(RowButtonStyle())
                    .accessibilityIdentifier("area-\(item.rawValue)")
                }
            }
            .card()
        }
    }

    @ViewBuilder
    private func activitySection(_ content: BauakteContent) -> some View {
        let entries = Array(content.activity.prefix(5))
        if !entries.isEmpty {
            VStack(alignment: .leading, spacing: Space.m) {
                SectionHeading("Letzte Aktivität")
                VStack(alignment: .leading, spacing: Space.m) {
                    ForEach(entries) { entry in
                        HStack(alignment: .firstTextBaseline, spacing: Space.m) {
                            Image(systemName: entry.symbol)
                                .font(.subheadline.weight(SymbolWeight.content))
                                .foregroundStyle(Theme.textSecondary)
                                .frame(width: 22)
                                .accessibilityHidden(true)
                            Text(entry.text)
                                .font(Typo.subhead)
                                .foregroundStyle(Theme.textPrimary)
                                .lineLimit(2)
                                .frame(maxWidth: .infinity, alignment: .leading)
                            Text(entry.date.formatted(.relative(presentation: .named)))
                                .font(Typo.meta)
                                .foregroundStyle(Theme.textSecondary)
                        }
                        .accessibilityElement(children: .combine)
                    }
                }
                .padding(Space.l)
                .card()
            }
        }
    }

    private func footer(_ content: BauakteContent) -> some View {
        VStack(spacing: Space.xs) {
            if let updated = content.bauakte.updatedAt {
                Text("Zuletzt geändert \(updated.formatted(.relative(presentation: .named)))")
            }
            Text(content.bauakte.syncState == .synced ? "Auf dem Gerät gespeichert und übertragen" : "Auf dem Gerät gespeichert – wird übertragen")
        }
        .font(Typo.meta)
        .foregroundStyle(Theme.textTertiary)
        .frame(maxWidth: .infinity)
        .multilineTextAlignment(.center)
    }

    // MARK: Status und Menü

    private func statusMenu(_ bauakte: Bauakte) -> some View {
        Menu {
            Section("Status ändern") {
                ForEach(BauakteStatus.allCases.filter { $0 != bauakte.status && $0 != .cancelled
                                                         && !(bauakte.status == .cancelled && $0 == .completed) }, id: \.self) { status in
                    Button(status.title, systemImage: status.symbol) { Task { await change(to: status) } }
                }
            }
            if bauakte.status != .cancelled {
                Button("Stornieren …", systemImage: "xmark.circle", role: .destructive) {
                    cancelReason = ""
                    cancelling = true
                }
                .accessibilityIdentifier("cancelBauakteButton")
            }
        } label: {
            HStack(spacing: 6) {
                StatusDot(color: bauakte.status.tint)
                Text(bauakte.status.shortTitle)
                    .font(Typo.subheadStrong)
                    .foregroundStyle(Theme.textPrimary)
                Image(systemName: "chevron.up.chevron.down")
                    .font(.caption2.weight(.bold))
                    .foregroundStyle(Theme.textSecondary)
            }
            .padding(.horizontal, Space.m)
            .frame(minHeight: 36)
            .background(Theme.surface, in: Capsule())
            .contentShape(Capsule())
        }
        .accessibilityLabel("Status: \(bauakte.status.title). Ändern")
        .accessibilityIdentifier("statusMenu")
    }

    private func moreMenu(_ bauakte: Bauakte) -> some View {
        Menu {
            Button("Bauakte als PDF", systemImage: "doc.richtext") { Task { await openPDF(bauakte) } }
            if let url = MapsLink.url(for: bauakte) {
                Button("Route starten", systemImage: "arrow.triangle.turn.up.right.diamond") { openURL(url) }
            }
            Button("Basisdaten bearbeiten", systemImage: "pencil") { action = .project }
            Button("Verlauf", systemImage: "clock.arrow.circlepath") { show(.notes) }
        } label: {
            if loadingPDF {
                ProgressView()
            } else {
                Label("Weitere Aktionen", systemImage: "ellipsis.circle")
            }
        }
        .accessibilityIdentifier("bauakteMoreMenu")
    }

    /// „2.–6. Nov.“ bzw. „ab 2. Nov.“
    static func period(_ bauakte: Bauakte) -> String? {
        guard let start = bauakte.plannedStart.flatMap({ CalendarDay.date($0) }) else { return nil }
        let format = Date.FormatStyle.dateTime.day().month(.abbreviated)
        if let end = bauakte.plannedEnd.flatMap({ CalendarDay.date($0) }), end != start {
            return "\(start.formatted(format)) – \(end.formatted(format))"
        }
        return "ab \(start.formatted(format))"
    }

    // MARK: Laden und Ändern

    private func show(_ target: BauakteArea) {
        area = target
        wideArea = target
    }

    private func reload() async {
        let fresh = await BauakteContent.load(bauakteId, model: model)
        content = fresh
        loaded = true
        guard let service = model.active?.service else { return }
        if schema == nil { schema = await service.formSchema(using: model.active?.client) }
        lists = await service.valueLists()
        applyShotScene()
    }

    /// Screenshot-Modus: Bereich öffnen, nach unten scrollen oder ein Formular zeigen.
    private func applyShotScene() {
        guard let scene = shotScene, content != nil else { return }
        if let target = BauakteArea.shot(scene), area == nil {
            show(target)
        } else if action == nil {
            switch scene {
            case "erstbegehung": action = .inspection
            case "abnahme": action = .acceptance
            case "tagebuch-neu": action = .diary
            case "mangel-neu": action = .defect
            case "basisdaten-neu": action = .project
            default: break
            }
        }
    }

    private func toggle(_ item: WorkItem) async {
        try? await model.active?.service?.setStatus(item.isDone ? .open : .done, of: item.id)
        model.active?.sync?.localChangeMade()
        await reload()
    }

    /// Status ändern bzw. stornieren – sofort lokal, Übertragung und Konfliktprüfung über die Synchronisation.
    private func change(to status: BauakteStatus, reason: String? = nil) async {
        do throws(BauakteError) {
            try await model.active?.service?.changeStatus(status, reason: reason, of: bauakteId)
            statusError = nil
            model.active?.sync?.localChangeMade()
            await reload()
        } catch {
            statusError = error.message
        }
    }

    /// Bauakte-PDF vom Server (alle Teile), angezeigt mit QuickLook (Teilen über QuickLook).
    private func openPDF(_ bauakte: Bauakte) async {
        guard let client = model.active?.client else { return }
        loadingPDF = true
        defer { loadingPDF = false }
        do throws(APIError) {
            let data: Data = try await client.get("bauakten/\(bauakte.id.uuidString.lowercased())/report.pdf")
            let folder = FileManager.default.temporaryDirectory.appending(path: "Vorschau", directoryHint: .isDirectory)
            try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            let url = folder.appending(path: "Bauakte \(bauakte.number ?? bauakte.title).pdf")
            do {
                try data.write(to: url, options: [.atomic, .completeFileProtection])
                pdfURL = url
            } catch {
                pdfMessage = "Das PDF konnte nicht gespeichert werden."
            }
        } catch {
            pdfMessage = error == .offline
                ? "Das PDF erstellt der Server – bitte mit Verbindung erneut versuchen."
                : (error.status == 404 ? "Die Bauakte ist noch nicht übertragen. Bitte nach der Synchronisation erneut versuchen." : error.message)
        }
    }
}

/// Bauakte fehlt auf dem Gerät (z. B. eingeplant, aber noch nicht übertragen, oder nicht zugewiesen): laden oder
/// „Mir zuweisen“ statt einer leeren Seite.
struct MissingBauakteView: View {
    let bauakteId: UUID
    let reload: () async -> Void

    @Environment(AppModel.self) private var model
    @State private var working = false
    @State private var message: String?
    @State private var triedLoading = false

    var body: some View {
        ContentUnavailableView {
            Label(triedLoading ? "Bauakte nicht verfügbar" : "Bauakte wird geladen …", systemImage: "folder.badge.questionmark")
        } description: {
            Text(message ?? (triedLoading
                ? "Sie ist Ihnen nicht zugewiesen oder noch nicht übertragen. Sie können sie sich selbst zuweisen."
                : "Die Bauakte liegt noch nicht auf diesem Gerät."))
        } actions: {
            if triedLoading {
                Button {
                    Task { await assignToMe() }
                } label: {
                    ActionLabel("Mir zuweisen", systemImage: "person.badge.plus", isBusy: working)
                }
                .estrichButton(.primary, size: .regular)
                .disabled(working)
                .accessibilityIdentifier("assignToMeButton")
                Button {
                    Task { await load() }
                } label: {
                    Label("Erneut laden", systemImage: "arrow.clockwise")
                }
                .estrichButton(.tertiary)
                .disabled(working)
            } else {
                ProgressView()
            }
        }
        .background(Theme.canvas)
        .task { await load() }
    }

    private func load() async {
        guard let sync = model.active?.sync else { return }
        working = true
        defer { working = false }
        let outcome = await sync.load(bauakte: bauakteId)
        await reload()
        triedLoading = true
        message = outcome == .offline ? "Ohne Verbindung kann die Bauakte nicht geladen werden. Bitte später erneut versuchen." : nil
    }

    private func assignToMe() async {
        guard let active = model.active, let sync = active.sync else { return }
        working = true
        defer { working = false }
        do throws(APIError) {
            try await active.client.assignToMe(bauakteId)
            _ = await sync.load(bauakte: bauakteId)
            await reload()
        } catch {
            message = error == .offline
                ? "Ohne Verbindung nicht möglich. Bitte später erneut versuchen."
                : (error.status == 404 ? "Diese Bauakte können Sie sich nicht selbst zuweisen. Bitte im Büro melden." : error.message)
        }
    }
}
