import EstrichCore
import QuickLook
import SwiftUI

/// Bauakte – das Herzstück. Auf einen Blick: Status, Adresse mit Route, Kunde und Ansprechpartner mit Anruf, nächster
/// Einsatz mit Kolonne, Estrich-Kenndaten, offene Mängel und Aufgaben, die neuesten Fotos und die letzte Aktivität.
/// Darunter alle Bereiche (Fotos und Dokumente, Aufgaben, Bautagebuch, Protokolle, Basisdaten, Kontakte, Notizen).
/// Breite Darstellung (iPad quer): Übersicht links, gewählter Bereich rechts. Alles offline aus der lokalen Ablage.
struct BauakteDetailView: View {
    let bauakteId: UUID
    /// Gleich diesen Bereich öffnen (z. B. aus einer Benachrichtigung „Mangel zugewiesen“).
    var initialArea: BauakteArea?

    @Environment(AppModel.self) private var model
    /// Module der Suite (zuletzt bekannter Stand; ohne Angabe alles an).
    private var modules: AppModules { model.active?.sync?.modules ?? .allOn }
    @Environment(\.shotScene) private var shotScene
    @Environment(\.openURL) private var openURL
    @Environment(\.dynamicTypeSize) private var typeSize
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
    @State private var waivingInspection = false
    @State private var statusError: String?
    @State private var viewing: FileItem?
    @State private var playing: FileItem?
    @State private var pdfURL: URL?
    @State private var loadingPDF = false
    @State private var pdfMessage: String?
    @State private var showsNavigationTitle = false
    /// „Alle Basisdaten“ aufgeklappt.
    @State private var showsAllKeyData = false
    @State private var appliedInitialArea = false
    /// Verknüpfte Teams-Unterhaltung (nur online und im Chat-Modus „Teams“ bzw. „beides“).
    @State private var teamsLink: TeamsLink?
    /// Gruppenchat mit dem ganzen Bauakten-Team in Teams.
    @State private var teamChat: TeamsChatLink?

    var body: some View {
        Group {
            if let content {
                GeometryReader { proxy in
                    if proxy.size.width >= 780 {
                        HStack(spacing: 0) {
                            overview(content, wide: true)
                                .frame(width: min(460, proxy.size.width * 0.46))
                            Divider()
                            BauakteAreaView(area: wideArea, content: content, schema: schema, lists: lists, embedded: true) { await reload() }
                                .id(wideArea)
                        }
                        .onAppear { applyInitialArea(wide: true) }
                    } else {
                        overview(content, wide: false)
                            .onAppear { applyInitialArea(wide: false) }
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
                .toolbar {
                    // Alles außer Foto und Video (unten) steckt im Menü „…“ – auch „In Teams öffnen“.
                    ToolbarItem(placement: .primaryAction) { moreMenu(content.bauakte) }
                }
                .task(id: bauakteId) { await loadTeams() }
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
                .waiveInitialInspectionAlert(isPresented: $waivingInspection, bauakteId: bauakteId) { message in
                    toast = message
                    Task { await reload() }
                }
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
            // Recht „Bauakte bearbeiten“ und Einstellung „Hinweis Erstbegehung fehlt“ einmal frisch laden (danach zwischengespeichert).
            if let store = model.active?.sync?.store {
                _ = await MyPermissions(store: store).load(using: model.active?.client)
                _ = await InspectionHintSetting(store: store).load(using: model.active?.client)
                await reload()
            }
            // Namen für Verfasser und Zuständige; danach neu anzeigen.
            if !(await DirectoryCache.load(model)).isEmpty { await reload() }
        }
        .refreshable {
            model.active?.sync?.forceOfficeSettingsRefresh()
            await model.active?.sync?.run()
            await reload()
        }
    }

    /// Bereich aus der Benachrichtigung einmal öffnen (nur, wenn sein Modul eingeschaltet ist).
    private func applyInitialArea(wide: Bool) {
        guard !appliedInitialArea, let initialArea, initialArea.isAvailable(modules) else { return }
        appliedInitialArea = true
        if wide { wideArea = initialArea } else { area = initialArea }
    }

    // MARK: Übersicht

    /// Standardliste: oben die Basisdaten auf einen Blick (Nummer, Status, Bauvorhaben, Adresse mit Route, Kunde und
    /// Ansprechpartner mit Anruf, nächster Einsatz), dann Kennwerte (aufklappbar), offene Punkte, neueste Fotos und die
    /// Bereiche. „Foto“ und „Video“ stehen immer erreichbar unten.
    private func overview(_ content: BauakteContent, wide: Bool) -> some View {
        ScrollViewReader { proxy in
            List {
                Section {
                    header(content)
                    ForEach(factList(content)) { fact in factRow(fact) }
                }
                highlights(content)
                keyData(content)
                openPoints(content)
                photoStrip(content)
                areas(content, wide: wide)
            }
            .listStyle(.insetGrouped)
            .listSectionSpacing(.compact)
            .scrolledPastHeader($showsNavigationTitle)
            .safeAreaInset(edge: .bottom) { captureBar }
            .task(id: loaded) {
                // Screenshot-Modus: Mitte (Kennwerte, offene Punkte, Fotos) bzw. unten (alle Bereiche).
                guard loaded, let target = shotScene == "bauakte-unten" ? "areas" : shotScene == "bauakte-mitte" ? "keyData" : nil else { return }
                try? await Task.sleep(for: .milliseconds(300))
                proxy.scrollTo(target, anchor: .top)
            }
        }
    }

    /// Die zwei Hauptaktionen der Baustelle – groß, mit Handschuhen bedienbar, Kamera öffnet sofort.
    private var captureBar: some View {
        HStack(spacing: Space.s) {
            Button { action = .photo } label: {
                Label("Foto", systemImage: "camera")
                    .frame(maxWidth: .infinity, minHeight: Size.minTap)
            }
            .buttonStyle(.borderedProminent)
            .accessibilityIdentifier("addPhotoButton")
            Button { action = .video } label: {
                Label("Video", systemImage: "video")
                    .frame(maxWidth: .infinity, minHeight: Size.minTap)
            }
            .buttonStyle(.bordered)
            .accessibilityIdentifier("addVideoButton")
        }
        .controlSize(.large)
        .font(.body.weight(.semibold))
        .padding(.horizontal, Space.l)
        .padding(.vertical, Space.s)
        .background(.bar)
    }

    private func header(_ content: BauakteContent) -> some View {
        let bauakte = content.bauakte
        return VStack(alignment: .leading, spacing: Space.xs) {
            HStack(alignment: .firstTextBaseline, spacing: Space.s) {
                Text(bauakte.number ?? "Neu · noch ohne Nummer")
                    .font(Typo.subhead.monospacedDigit())
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
        }
        .padding(.vertical, Space.xs)
    }

    @ViewBuilder
    private func highlights(_ content: BauakteContent) -> some View {
        let bauakte = content.bauakte
        let warnings = Array(Set(content.todayAssignments.flatMap(\.warnings))).sorted()
        let inspectionMissing = InitialInspectionState.showsMissingHint(bauakte: bauakte, inspections: content.inspections,
                                                                        enabled: content.warnMissingInspection)
        if bauakte.status == .cancelled || statusError != nil || !content.pinnedNotes.isEmpty || !warnings.isEmpty || inspectionMissing {
            Section {
                Group {
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
                    if inspectionMissing {
                        // Hinweiskarte wie im Büro: am Hinweis direkt als nicht erforderlich vermerken.
                        Callout(tone: .warning, message: InitialInspectionState.missingText, symbol: "checklist",
                                actionTitle: content.canEditBauakte ? InitialInspectionState.waiveButtonTitle : nil) { waivingInspection = true }
                            .accessibilityIdentifier("initialInspectionHint")
                    }
                }
                .listRowBackground(Color.clear)
                .listRowInsets(EdgeInsets(top: Space.xs, leading: 0, bottom: Space.xs, trailing: 0))
                .listRowSeparator(.hidden)
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
        // Bauleitung – auf der Baustelle der häufigste Anruf nach dem Kunden.
        if let manager = content.parties.first(where: { $0.role == .siteManager }) {
            list.append(Fact(id: "manager", symbol: "person.badge.key", title: manager.name,
                             subtitle: [manager.role.title, manager.company].compactMap { $0 }.joined(separator: " · "),
                             link: ContactLink.phone(manager.phone), linkTitle: "\(manager.role.title) anrufen", linkSymbol: "phone.fill"))
        }
        if let next = content.nextAssignmentText {
            list.append(Fact(id: "next", symbol: "calendar.badge.clock", title: next.title, subtitle: next.subtitle))
        } else if let period = Self.period(bauakte) {
            list.append(Fact(id: "next", symbol: "calendar", title: "Geplant: \(period)", subtitle: "Ausführungszeitraum"))
        }
        return list
    }

    @ViewBuilder
    private func factRow(_ fact: Fact) -> some View {
        let row = HStack(spacing: Space.m) {
            Image(systemName: fact.symbol)
                .foregroundStyle(fact.tint)
                .frame(width: 28)
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 2) {
                Text(fact.title)
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
            }
        }
        .frame(minHeight: Size.minTap)
        .contentShape(Rectangle())
        if let tap = fact.tap {
            Button(action: tap) { row }
                .buttonStyle(.plain)
        } else {
            row
        }
    }

    // MARK: Basisdaten, offene Punkte, Fotos

    private func projectItems(_ bauakte: Bauakte) -> [InfoItem] {
        let rows = schema.map { ProjectSection.items(bauakte, schema: $0, lists: lists) } ?? ProjectSection.items(bauakte)
        return rows.filter { $0.title != "Geplant" }.map { InfoItem(label: $0.title, value: $0.value, symbol: $0.symbol) }
    }

    /// Die vier wichtigsten Kennwerte direkt, alle weiteren per Tipp auf „Alle Basisdaten“ aufklappbar.
    private func keyData(_ content: BauakteContent) -> some View {
        let items = projectItems(content.bauakte)
        let first = Array(items.prefix(4))
        let rest = Array(items.dropFirst(4))
        return Section {
            if items.isEmpty {
                Button("Basisdaten erfassen", systemImage: "pencil") { action = .project }
                    .frame(minHeight: Size.minTap)
                    .id("keyData")
            } else {
                ForEach(Array(first.enumerated()), id: \.element.id) { index, item in
                    LabeledContent(item.label, value: item.value)
                        .id(index == 0 ? "keyData" : item.id)
                }
                if !rest.isEmpty {
                    DisclosureGroup(isExpanded: $showsAllKeyData) {
                        ForEach(rest) { item in
                            LabeledContent(item.label, value: item.value)
                        }
                    } label: {
                        Text("Alle Basisdaten (\(items.count))")
                            .foregroundStyle(Theme.accent)
                    }
                    .frame(minHeight: Size.minTap)
                    .accessibilityIdentifier("allKeyDataButton")
                }
            }
        } header: {
            HStack {
                Text("Basisdaten")
                Spacer()
                if !items.isEmpty {
                    Button("Bearbeiten") { action = .project }
                        .font(Typo.subhead)
                        .textCase(nil)
                        .accessibilityIdentifier("editProjectButton")
                }
            }
        }
    }

    @ViewBuilder
    private func openPoints(_ content: BauakteContent) -> some View {
        let open = content.openItems.sorted { lhs, rhs in
            if (lhs.kind == .defect) != (rhs.kind == .defect) { return lhs.kind == .defect }
            return (lhs.dueDate ?? "9999") < (rhs.dueDate ?? "9999")
        }
        if !open.isEmpty, modules.anyOn(AppModules.tasks, AppModules.defects) {
            Section("Offene Punkte") {
                ForEach(Array(open.prefix(3).enumerated()), id: \.element.id) { index, item in
                    WorkItemRow(item: item, assigneeName: item.assigneeUserId.flatMap { content.names[$0] }) {
                        Task { await toggle(item) }
                    }
                    .listRowInsets(EdgeInsets(top: 0, leading: Space.xs, bottom: 0, trailing: Space.l))
                    .id(index == 0 ? "openPoints" : item.id.uuidString)
                }
                if open.count > 3 {
                    Button("Alle \(open.count) anzeigen") { show(.work) }
                        .frame(minHeight: Size.minTap)
                }
            }
        }
    }

    @ViewBuilder
    private func photoStrip(_ content: BauakteContent) -> some View {
        if !content.photos.isEmpty {
            Section("Neueste Fotos") {
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack(spacing: Space.s) {
                        ForEach(Array(content.photos.prefix(12).enumerated()), id: \.element.id) { index, file in
                            Button {
                                if file.isVideo { playing = file } else { viewing = file }
                            } label: {
                                PhotoThumbnail(file: file, radius: Radius.s, showsCaption: false)
                                    .frame(width: 88, height: 88)
                            }
                            .buttonStyle(.plain)
                            .accessibilityLabel(file.caption ?? (file.isVideo ? "Video" : "Foto"))
                            .accessibilityIdentifier("photoThumb-\(index)")
                        }
                    }
                    .padding(.horizontal, Space.l)
                    .padding(.vertical, Space.s)
                }
                .listRowInsets(EdgeInsets())
            }
        }
    }

    // MARK: Bereiche

    private func areas(_ content: BauakteContent, wide: Bool) -> some View {
        let projectCount = projectItems(content.bauakte).count
        let list = BauakteArea.allCases.filter { $0.isAvailable(modules) }
        return Section {
            ForEach(Array(list.enumerated()), id: \.element) { index, item in
                let summary = item.summary(content, projectCount: projectCount)
                let selected = wide && wideArea == item
                Button { if wide { wideArea = item } else { area = item } } label: {
                    HStack(spacing: Space.m) {
                        Image(systemName: item.symbol)
                            .foregroundStyle(Theme.accent)
                            .frame(width: 28)
                            .accessibilityHidden(true)
                        VStack(alignment: .leading, spacing: 2) {
                            Text(item.title).foregroundStyle(Theme.textPrimary)
                            Text(summary.text)
                                .font(Typo.subhead)
                                .foregroundStyle(summary.isMissing ? Theme.warning : Theme.textSecondary)
                                .lineLimit(1)
                        }
                        .frame(maxWidth: .infinity, alignment: .leading)
                        Chevron()
                    }
                    .frame(minHeight: Size.minTap)
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .listRowBackground(selected ? Theme.accentSoft : nil)
                .accessibilityIdentifier("area-\(item.rawValue)")
                .id(index == 0 ? "areas" : item.rawValue)
            }
        } header: {
            Text("Bereiche")
        } footer: {
            footer(content)
        }
    }

    private func footer(_ content: BauakteContent) -> some View {
        let state = content.bauakte.syncState == .synced ? "übertragen" : "Übertragung ausstehend"
        if let updated = content.bauakte.updatedAt {
            return Text("Zuletzt geändert \(updated.formatted(.relative(presentation: .named))) · \(state)")
        }
        return Text("Auf dem Gerät gespeichert · \(state)")
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
            HStack(spacing: 4) {
                StatusDot(color: bauakte.status.tint)
                Text(bauakte.status.shortTitle)
                    .font(Typo.subhead)
                    .lineLimit(1)
                Image(systemName: "chevron.up.chevron.down")
                    .font(.caption2.weight(.semibold))
            }
            .frame(minHeight: Size.minTap)
            .contentShape(Rectangle())
        }
        .menuStyle(.button)
        .buttonStyle(.borderless)
        .accessibilityLabel("Status: \(bauakte.status.title). Ändern")
        .accessibilityIdentifier("statusMenu")
    }

    private func moreMenu(_ bauakte: Bauakte) -> some View {
        Menu {
            // Erfassen (Foto und Video stehen unten als Hauptaktionen).
            Section {
                captureItem(.scan, id: "scanDocumentButton")
                if modules.isOn(AppModules.diary) { captureItem(.diary, id: "addDiaryButton") }
                if modules.isOn(AppModules.defects) { captureItem(.defect, id: "addDefectButton") }
                if modules.isOn(AppModules.tasks) { captureItem(.task, id: "add-task") }
                captureItem(.note, id: "add-note")
            }
            Section {
                if modules.isOn(AppModules.inspections) { captureItem(.inspection, id: "add-inspection") }
                if modules.isOn(AppModules.acceptance) { captureItem(.acceptance, id: "add-acceptance") }
                if modules.isOn(AppModules.inspections) { captureItem(.measurement, id: "add-measurement") }
            }
            Section {
                Button("Bauakte als PDF", systemImage: "doc.richtext") { Task { await openPDF(bauakte) } }
                if let url = MapsLink.url(for: bauakte) {
                    Button("Route starten", systemImage: "arrow.triangle.turn.up.right.diamond") { openURL(url) }
                }
                Button("Basisdaten bearbeiten", systemImage: "pencil") { action = .project }
                Button("Verlauf", systemImage: "clock.arrow.circlepath") { show(.notes) }
            }
            if teamsLink != nil || teamChat != nil {
                Section {
                    if let teamsLink {
                        OpenInTeamsButton(targets: teamsLink.targets)
                    }
                    if let teamChat {
                        Button("Chat mit dem Team in Teams", systemImage: "person.3") { Task { await TeamsOpener.open(teamChat.targets) } }
                            .accessibilityIdentifier("teamsTeamChatButton")
                    }
                }
            }
        } label: {
            if loadingPDF {
                ProgressView()
            } else {
                Label("Weitere Aktionen", systemImage: "ellipsis.circle")
            }
        }
        .accessibilityIdentifier("bauakteMoreMenu")
    }

    private func captureItem(_ item: BauakteAction, id: String) -> some View {
        Button(item.title, systemImage: item.symbol) { action = item }
            .accessibilityIdentifier(id)
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

    /// Microsoft Teams: Verknüpfung laden (ohne Verbindung oder Teams-Anbindung bleibt der Knopf weg).
    private func loadTeams() async {
        guard let active = model.active, active.sync?.teams?.available == true else {
            teamsLink = nil
            teamChat = nil
            return
        }
        let teams = try? await active.client.bauakteTeams(bauakteId)
        teamsLink = teams?.link
        teamChat = teams?.teamChat
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
            case "kamera": action = .photo
            case "foto-vorschau":
                if viewing == nil, let photo = content?.photos.first(where: \.isPhoto) { viewing = photo }
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
                ? "Das PDF wird auf dem Server erstellt. Bitte mit Verbindung erneut versuchen."
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
