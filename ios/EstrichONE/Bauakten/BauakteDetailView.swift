import EstrichCore
import QuickLook
import SwiftUI

/// Bauakte, das Herzstück, nach dem Arbeitsablauf aufgebaut: Kopf (Nummer, Titel, Status, Adresse) mit Aktionsleiste
/// (Route, Anrufen, Chat, Foto, Teams), heute bzw. nächster Termin, Hinweise, Leistung und Kennwerte, Ablauf als
/// Fortschritt mit Erfassen des nächsten Schritts, offene Aufgaben und Mängel, Fotos und Dokumentation, Kontakte,
/// Beschreibung und Verlauf. Erfasst wird über „+“ oben rechts (alle Erfassungen an einer Stelle), nichts verdeckt Inhalt.
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
    /// Unterhaltung der Baustelle im Chat (aus der zuletzt geladenen Chat-Liste).
    @State private var chatConversation: UUID?
    @State private var appliedInitialArea = false
    /// Verknüpfte Teams-Unterhaltung (nur online und im Chat-Modus „Teams“ bzw. „beides“).
    @State private var teamsLink: TeamsLink?
    /// Gruppenchat mit dem ganzen Bauakten-Team in Teams.
    @State private var teamChat: TeamsChatLink?
    /// Selbst als Beteiligter eingetragen (online geladen) – dann „Austragen“ im Menü.
    @State private var isMember = false
    @State private var joining = false
    /// Blatt „Erfassen“.
    @State private var capturing = false
    /// Zuständige Person und weitere Beteiligte (online geladen).
    @State private var responsibleName: String?
    @State private var memberNames: [String] = []
    /// Stand auf dem Gerät („Auf dem Gerät verfügbar“): Daten, Dokumente und Fotos für die Arbeit ohne Netz.
    @State private var availability: OfflineAvailability?

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
                    // „+“ öffnet „Erfassen“ (alles an einer Stelle), „…“ enthält PDF, Bearbeiten, Verlauf und Teams.
                    if !content.viewOnly {
                        ToolbarItem(placement: .primaryAction) {
                            Button { capturing = true } label: { Label("Erfassen", systemImage: "plus") }
                                .accessibilityHint("Foto, Video, Dokument, Bautagebuch, Mangel, Aufgabe, Notiz und Prüfungen")
                                .accessibilityIdentifier("captureMenuButton")
                        }
                    }
                    ToolbarItem(placement: .primaryAction) { moreMenu(content.bauakte) }
                }
                .sheet(isPresented: $capturing) {
                    BauakteCaptureSheet(bauakte: content.bauakte, pick: { startCapture($0) }) { capturing = false }
                }
                .task(id: bauakteId) {
                    await loadChat()
                    await loadTeams()
                    await loadMembership()
                }
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
        .task(id: model.active?.sync?.offline.revision) {
            availability = await model.active?.sync?.offline.availability(of: bauakteId)
        }
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

    /// Standardliste nach dem Arbeitsablauf: 1. Kopf mit Aktionsleiste, 2. heute bzw. nächster Termin, 3. Hinweise und
    /// angeheftete Notizen, 4. Leistung und Kennwerte, 5. Ablauf als Fortschritt, 6. offene Aufgaben und Mängel,
    /// 7. Fotos und Dokumentation, 8. Kontakte, 9. Beschreibung und Verlauf. Erfasst wird über „+“ oben rechts.
    private func overview(_ content: BauakteContent, wide: Bool) -> some View {
        ScrollViewReader { proxy in
            List {
                Section {
                    header(content)
                    actionBar(content)
                        .listRowInsets(EdgeInsets(top: Space.xs, leading: Space.l, bottom: Space.m, trailing: Space.l))
                }
                highlights(content)
                appointment(content)
                siteNotes(content)
                keyData(content, wide: wide)
                workflow(content, wide: wide)
                openPoints(content, wide: wide)
                documentation(content, wide: wide)
                contacts(content, wide: wide)
                history(content, wide: wide)
            }
            .listStyle(.insetGrouped)
            .listSectionSpacing(.compact)
            .scrolledPastHeader($showsNavigationTitle)
            .task(id: loaded) {
                // Screenshot-Modus: Mitte (Kennwerte, Ablauf) bzw. unten (Dokumentation, Kontakte, Verlauf).
                guard loaded, let target = shotScene == "bauakte-unten" ? "documentation" : shotScene == "bauakte-mitte" ? "keyData" : nil else { return }
                try? await Task.sleep(for: .milliseconds(300))
                proxy.scrollTo(target, anchor: .top)
            }
        }
    }

    /// Kopf: Nummer mit Status (Menü zum Ändern), Bauvorhaben, Adresse in einer Zeile, Tagesbezug und Priorität.
    private func header(_ content: BauakteContent) -> some View {
        let bauakte = content.bauakte
        return VStack(alignment: .leading, spacing: Space.xs) {
            HStack(alignment: .center, spacing: Space.s) {
                Text(bauakte.number ?? "Neu, noch ohne Nummer")
                    .font(Typo.subhead.monospacedDigit())
                    .foregroundStyle(Theme.textSecondary)
                    .accessibilityIdentifier("bauakteNumber")
                SyncStateBadge(state: bauakte.syncState)
                Spacer(minLength: Space.s)
                statusMenu(bauakte)
                    .disabled(content.viewOnly)
            }
            Text(bauakte.title)
                .font(Typo.title)
                .foregroundStyle(Theme.textPrimary)
                .fixedSize(horizontal: false, vertical: true)
                .accessibilityAddTraits(.isHeader)
                .accessibilityIdentifier("bauakteTitle")
            Label {
                Text(bauakte.addressLine ?? "Adresse fehlt")
                    .lineLimit(2)
            } icon: {
                Image(systemName: bauakte.addressLine == nil ? "mappin.slash" : "mappin.and.ellipse")
            }
            .labelStyle(TightLabelStyle())
            .font(Typo.subhead)
            .foregroundStyle(bauakte.addressLine == nil ? Theme.warning : Theme.textSecondary)
            .accessibilityIdentifier("bauakteAddress")
            if let availability {
                OfflineAvailabilityLine(availability: availability)
            }
            let today = !content.todayAssignments.isEmpty
            let priority = bauakte.project.priority.flatMap { $0 == .high || $0 == .urgent ? $0 : nil }
            if today || priority != nil {
                ChipFlow(spacing: 6) {
                    if today { headerChip("Heute im Einsatz", symbol: "sun.max.fill", tint: Theme.accent, background: Theme.accentSoft) }
                    if let priority {
                        headerChip("Priorität \(priority.title.lowercased())", symbol: "flag.fill", tint: Theme.warning, background: Theme.warningSoft)
                    }
                }
                .padding(.top, Space.xxs)
                .accessibilityIdentifier("bauakteHeaderChips")
            }
        }
        .padding(.vertical, Space.xs)
    }

    private func headerChip(_ text: String, symbol: String, tint: Color, background: Color) -> some View {
        HStack(spacing: 5) {
            Image(systemName: symbol).imageScale(.small).accessibilityHidden(true)
            Text(text).lineLimit(2).fixedSize(horizontal: false, vertical: true)
        }
        .font(Typo.labelStrong)
        .foregroundStyle(tint)
        .padding(.horizontal, 9)
        .padding(.vertical, 3)
        .frame(minHeight: 26)
        .background(background, in: Capsule())
    }

    // MARK: Aktionsleiste

    /// Wen man aus der Bauakte anrufen kann: Kunde, Ansprechpartner vor Ort, alle Kontakte mit Nummer.
    private struct CallTarget: Identifiable {
        let id: String
        let title: String
        let subtitle: String
        let url: URL
    }

    private func callTargets(_ content: BauakteContent) -> [CallTarget] {
        var list: [CallTarget] = []
        if let customer = content.customer, let url = ContactLink.phone(customer.phone) {
            list.append(CallTarget(id: "customer", title: customer.name, subtitle: "Kunde", url: url))
        }
        let bauakte = content.bauakte
        if let url = ContactLink.phone(bauakte.siteContactPhone) {
            list.append(CallTarget(id: "site", title: bauakte.siteContactName ?? bauakte.siteContactPhone ?? "", subtitle: "Ansprechpartner vor Ort", url: url))
        }
        for party in content.parties {
            if let url = ContactLink.phone(party.phone ?? party.mobile), !list.contains(where: { $0.url == url }) {
                list.append(CallTarget(id: "party-\(party.id)", title: party.name,
                                       subtitle: [party.role.title, party.company].compactMap { $0 }.joined(separator: ", "), url: url))
            }
        }
        return list
    }

    /// Waagrechte Leiste mit beschrifteten Symbolknöpfen: Route, Anrufen (mit Auswahl), Chat, Foto, Teams (falls verknüpft).
    private func actionBar(_ content: BauakteContent) -> some View {
        let calls = callTargets(content)
        let route = MapsLink.url(for: content.bauakte)
        let chatOn = model.active?.sync?.chat?.isAvailable == true
        let teams = teamsLink != nil || teamChat != nil
        let columns = typeSize.isAccessibilitySize ? 2 : (teams ? 5 : 4)
        return LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: Space.s), count: columns), spacing: Space.s) {
            Button { if let route { openURL(route) } } label: { Label("Route", systemImage: "arrow.triangle.turn.up.right.diamond.fill") }
                .buttonStyle(ActionTileStyle(rank: .primary))
                .disabled(route == nil)
                .accessibilityIdentifier("bauakteRouteButton")
            if calls.count == 1, let only = calls.first {
                Button { openURL(only.url) } label: { Label("Anrufen", systemImage: "phone.fill") }
                    .buttonStyle(ActionTileStyle())
                    .accessibilityLabel("\(only.title) anrufen")
                    .accessibilityIdentifier("bauakteCallButton")
            } else {
                Menu {
                    ForEach(calls) { target in
                        Button { openURL(target.url) } label: {
                            Label {
                                Text(target.title)
                                Text(target.subtitle)
                            } icon: {
                                Image(systemName: "phone")
                            }
                        }
                    }
                } label: {
                    Label("Anrufen", systemImage: "phone.fill")
                }
                .menuStyle(.button)
                .buttonStyle(ActionTileStyle())
                .disabled(calls.isEmpty)
                .accessibilityIdentifier("bauakteCallButton")
            }
            Button { openChat() } label: { Label("Chat", systemImage: "bubble.left.and.bubble.right.fill") }
                .buttonStyle(ActionTileStyle())
                .disabled(!chatOn)
                .accessibilityLabel("Chat der Baustelle")
                .accessibilityIdentifier("bauakteChatButton")
            Button { action = .photo } label: { Label("Foto", systemImage: "camera.fill") }
                .buttonStyle(ActionTileStyle())
                .disabled(content.viewOnly)
                .accessibilityIdentifier("addPhotoButton")
            if teams {
                Button {
                    if let teamsLink { Task { await TeamsOpener.open(teamsLink.targets) } } else if let teamChat { Task { await TeamsOpener.open(teamChat.targets) } }
                } label: { Label("Teams", systemImage: "person.2.wave.2.fill") }
                    .buttonStyle(ActionTileStyle())
                    .accessibilityLabel("In Microsoft Teams öffnen")
                    .accessibilityIdentifier("bauakteTeamsButton")
            }
        }
    }

    /// Chat der Baustelle im Reiter „Chat“ öffnen (ohne bekannte Unterhaltung die Liste).
    private func openChat() {
        ChatNavigation.shared.show(chatConversation)
    }

    // MARK: Heute bzw. nächster Termin

    @ViewBuilder
    private func appointment(_ content: BauakteContent) -> some View {
        let today = CalendarDay.string(.now)
        if let next = content.assignments.first {
            let team = next.people.map(\.name)
            let resources = next.resources.map(\.name)
            Section {
                VStack(alignment: .leading, spacing: Space.s) {
                    HStack(alignment: .firstTextBaseline, spacing: Space.s) {
                        Text(TodayGlance.appointmentText(day: next.day, start: next.startText.map { "ab \($0)" }, detail: nil, today: today))
                            .font(Typo.headline.monospacedDigit())
                            .foregroundStyle(next.day == today ? Theme.accent : Theme.textPrimary)
                        Spacer(minLength: 0)
                    }
                    appointmentLine("hammer", next.activity, detail: content.bauakte.project.keyFactsText, strong: true)
                    if next.crewName != nil || !team.isEmpty {
                        appointmentLine("person.3", next.crewName ?? team.joined(separator: ", "), detail: next.crewName == nil || team.isEmpty ? nil : team.joined(separator: ", "))
                    }
                    if !resources.isEmpty {
                        appointmentLine("truck.box", resources.joined(separator: ", "))
                    }
                }
                .padding(.vertical, Space.xs)
                .accessibilityElement(children: .combine)
                .accessibilityIdentifier("bauakteAppointment")
                if content.assignments.count > 1 {
                    let later = content.assignments.count - 1
                    Text(later == 1 ? "1 weiterer Termin geplant" : "\(later) weitere Termine geplant")
                        .font(Typo.subhead)
                        .foregroundStyle(Theme.textSecondary)
                }
            } header: {
                Text(next.day == today ? "Heute auf der Baustelle" : "Nächster Termin")
            }
        } else if let period = Self.period(content.bauakte) {
            Section("Nächster Termin") {
                appointmentLine("calendar", "Geplant: \(period)", detail: "Ausführungszeitraum")
                    .padding(.vertical, Space.xxs)
            }
        }
    }

    private func appointmentLine(_ symbol: String, _ text: String, detail: String? = nil, strong: Bool = false) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: Space.m) {
            Image(systemName: symbol)
                .font(.subheadline.weight(SymbolWeight.content))
                .foregroundStyle(Theme.textSecondary)
                .frame(width: 22)
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 2) {
                Text(text)
                    .font(strong ? Typo.subheadStrong : Typo.subhead)
                    .foregroundStyle(Theme.textPrimary)
                    .fixedSize(horizontal: false, vertical: true)
                if let detail {
                    Text(detail)
                        .font(Typo.subhead)
                        .foregroundStyle(Theme.textSecondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
        }
    }

    // MARK: Hinweise zur Baustelle (Freitexte)

    private struct SiteNote: Identifiable {
        let id: String
        let title: String
        let text: String
        let symbol: String
        var tint: Color = Theme.accent
    }

    /// Was oft per Freitext erfasst wird: angeheftete Notizen, Zugang und Besonderheiten, Hinweise und Behinderungen (und
    /// weitere mehrzeilige Felder des Büros), Hinweise der Disposition für heute. Die Beschreibung steht weiter unten.
    private func siteNoteItems(_ content: BauakteContent) -> [SiteNote] {
        var list: [SiteNote] = content.pinnedNotes.compactMap { note in
            FreeText.clean(note.text).map { SiteNote(id: "pin-\(note.id)", title: "Angeheftete Notiz", text: $0, symbol: "pin.fill") }
        }
        list += ProjectSection.freeTexts(content.bauakte, schema: schema).map {
            SiteNote(id: "field-\($0.id)", title: $0.title, text: $0.text, symbol: $0.symbol)
        }
        var seen = Set<String>()
        for assignment in content.todayAssignments {
            if let notes = FreeText.clean(assignment.notes), seen.insert(notes).inserted {
                list.append(SiteNote(id: "dispo-\(assignment.id)", title: "Hinweis der Disposition für heute", text: notes, symbol: "text.bubble"))
            }
        }
        return list
    }

    @ViewBuilder
    private func siteNotes(_ content: BauakteContent) -> some View {
        let notes = siteNoteItems(content)
        if !notes.isEmpty {
            Section {
                ForEach(Array(notes.enumerated()), id: \.element.id) { index, note in
                    FreeTextBlock(title: note.title, text: note.text, symbol: note.symbol, tint: note.tint)
                        .id(index == 0 ? "siteNotes" : note.id)
                }
            } header: {
                HStack {
                    Text("Hinweise zur Baustelle")
                    Spacer()
                    if !content.viewOnly {
                        Button("Bearbeiten") { action = .project }
                            .font(Typo.subhead)
                            .textCase(nil)
                            .accessibilityIdentifier("editSiteNotesButton")
                    }
                }
            }
            .accessibilityIdentifier("siteNotesSection")
        }
    }

    @ViewBuilder
    private func highlights(_ content: BauakteContent) -> some View {
        let bauakte = content.bauakte
        let warnings = Array(Set(content.todayAssignments.flatMap(\.warnings))).sorted()
        let inspectionMissing = InitialInspectionState.showsMissingHint(bauakte: bauakte, inspections: content.inspections,
                                                                        enabled: content.warnMissingInspection)
        if content.viewOnly || bauakte.status == .cancelled || statusError != nil || !warnings.isEmpty || inspectionMissing {
            Section {
                Group {
                    if content.viewOnly {
                        // Nicht zugeteilt: ruhiger Hinweis, Eintragen schaltet das Bearbeiten frei.
                        Callout(tone: .info, title: "Nur ansehen", message: BauakteViewOnly.banner, symbol: "eye",
                                actionTitle: joining ? nil : BauakteViewOnly.join) { Task { await join() } }
                            .accessibilityIdentifier("viewOnlyBanner")
                    }
                    if bauakte.status == .cancelled {
                        Callout(tone: .danger, title: "Storniert", message: bauakte.cancellationReason ?? "ohne Angabe eines Grundes", symbol: "xmark.circle.fill")
                    }
                    if let statusError {
                        Callout(tone: .danger, message: statusError)
                    }
                    ForEach(warnings, id: \.self) { warning in
                        Callout(tone: .warning, message: warning)
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

    // MARK: Leistung und Kennwerte

    private func projectItems(_ bauakte: Bauakte) -> [InfoItem] {
        let rows = schema.map { ProjectSection.items(bauakte, schema: $0, lists: lists) } ?? ProjectSection.items(bauakte)
        let items = rows.filter { $0.title != "Geplant" }.map { InfoItem(label: $0.title, value: $0.value, symbol: $0.symbol) }
        return Self.keyDataFirst(items)
    }

    /// Was die Kolonne zuerst braucht: Estrichart, Dicke, Fläche, Termin, danach alles in der Reihenfolge des Formulars.
    static func keyDataFirst(_ items: [InfoItem]) -> [InfoItem] {
        func rank(_ label: String) -> Int {
            let text = label.lowercased()
            if text.contains("estrichart") { return 0 }
            if text == "dicke" || text.contains("nenndicke") || text.contains("estrichdicke") { return 1 }
            if text == "fläche" || text.contains("gesamtfläche") { return 2 }
            if text.contains("termin") { return 3 }
            return 4
        }
        return items.enumerated().sorted { lhs, rhs in
            let (left, right) = (rank(lhs.element.label), rank(rhs.element.label))
            return left != right ? left < right : lhs.offset < rhs.offset
        }.map(\.element)
    }

    /// Die vier wichtigsten Kennwerte direkt, Räume mit Fortschritt, alle Basisdaten als eigener Bereich.
    private func keyData(_ content: BauakteContent, wide: Bool) -> some View {
        let items = projectItems(content.bauakte)
        let first = Array(items.prefix(4))
        return Section {
            if items.isEmpty {
                Button("Basisdaten erfassen", systemImage: "pencil") { action = .project }
                    .frame(minHeight: Size.minTap)
                    .disabled(content.viewOnly)
                    .id("keyData")
            } else {
                ForEach(Array(first.enumerated()), id: \.element.id) { index, item in
                    LabeledContent(item.label, value: item.value)
                        .id(index == 0 ? "keyData" : item.id)
                }
            }
            areaRow(.rooms, content: content, wide: wide)
            if items.count > first.count {
                areaRow(.project, content: content, wide: wide, title: "Alle Basisdaten (\(items.count))")
                    .accessibilityIdentifier("allKeyDataButton")
            }
        } header: {
            HStack {
                Text("Leistung und Kennwerte")
                Spacer()
                if !items.isEmpty, !content.viewOnly {
                    Button("Bearbeiten") { action = .project }
                        .font(Typo.subhead)
                        .textCase(nil)
                        .accessibilityIdentifier("editProjectButton")
                }
            }
        }
    }

    // MARK: Ablauf

    private func workflowModel(_ content: BauakteContent) -> BauakteWorkflow {
        let totals = RoomTotals(content.rooms)
        let glance = SiteGlance(items: [], rooms: content.rooms, inspections: content.inspections, diary: [], today: CalendarDay.string(.now))
        var measured: Bool?
        var readinessText: String?
        switch glance.readiness {
        case .ready?: measured = true; readinessText = glance.readiness?.text
        case .notReady?: measured = false; readinessText = glance.readiness?.text
        case .rooms?: readinessText = glance.readiness?.text
        default: break
        }
        let inspection = content.initialInspection
        return BauakteWorkflow(.init(
            status: content.bauakte.status, initialInspection: inspection.status,
            inspectionAppointment: inspection.appointment.map(InitialInspectionOverview.appointmentText),
            roomCount: totals.count, roomsLaid: totals.laid, roomsReady: totals.ready,
            lastMeasurementReady: measured, readinessText: readinessText,
            acceptanceCount: content.acceptances.count, acceptanceResult: content.acceptances.first?.result.title,
            showsInspections: modules.isOn(AppModules.inspections), showsAcceptance: modules.isOn(AppModules.acceptance)))
    }

    /// Ablauf als Fortschritt: je Schritt erledigt, nicht erforderlich oder offen; der nächste mit „Erfassen“.
    private func workflow(_ content: BauakteContent, wide: Bool) -> some View {
        let flow = workflowModel(content)
        return Section {
            ForEach(Array(flow.steps.enumerated()), id: \.element.id) { index, step in
                workflowRow(step, number: index + 1, content: content, wide: wide)
                    .id(index == 0 ? "workflow" : step.id)
            }
        } header: {
            HStack {
                Text("Ablauf")
                Spacer()
                Text(flow.progressText)
                    .font(Typo.meta.monospacedDigit())
                    .textCase(nil)
            }
        }
        .accessibilityIdentifier("workflowSection")
    }

    private func workflowArea(_ kind: BauakteWorkflow.StepKind) -> BauakteArea {
        switch kind {
        case .initialInspection: .initialInspection
        case .laying: .rooms
        case .readiness, .acceptance: .protocols
        }
    }

    /// Erfassen für den nächsten Schritt (Einbringen läuft über die Räume).
    private func workflowAction(_ kind: BauakteWorkflow.StepKind) -> BauakteAction? {
        switch kind {
        case .initialInspection: .inspection
        case .laying: nil
        case .readiness: .measurement
        case .acceptance: .acceptance
        }
    }

    private func workflowRow(_ step: BauakteWorkflow.Step, number: Int, content: BauakteContent, wide: Bool) -> some View {
        let target = workflowArea(step.kind)
        return HStack(spacing: Space.m) {
            Button { select(target, wide: wide) } label: {
                HStack(spacing: Space.m) {
                    stepMarker(step.state, number: number)
                    VStack(alignment: .leading, spacing: 2) {
                        Text(step.title)
                            .font(step.state == .next ? Typo.bodyStrong : Typo.body)
                            .foregroundStyle(step.state == .open ? Theme.textSecondary : Theme.textPrimary)
                        Text(step.detail)
                            .font(Typo.subhead)
                            .foregroundStyle(Theme.textSecondary)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                }
                .frame(minHeight: Size.minTap)
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .accessibilityElement(children: .combine)
            .accessibilityValue(step.state == .done ? "erledigt" : step.state == .skipped ? "nicht erforderlich" : step.state == .next ? "als Nächstes" : "offen")
            .accessibilityIdentifier("area-\(target.rawValue)")
            if step.state == .next, !content.viewOnly {
                if let next = workflowAction(step.kind) {
                    Button("Erfassen") { action = next }
                        .estrichButton(.secondary)
                        .accessibilityLabel("\(step.title) erfassen")
                        .accessibilityIdentifier("workflowNextAction")
                } else {
                    Button("Räume") { select(.rooms, wide: wide) }
                        .estrichButton(.secondary)
                        .accessibilityIdentifier("workflowNextAction")
                }
            } else {
                Chevron()
            }
        }
        .listRowBackground(step.state == .next ? Theme.accentSoft : nil)
    }

    /// Kreis je Schritt: Haken (erledigt, grün), Strich (nicht erforderlich), Nummer (nächster in Akzentfarbe, offen grau).
    private func stepMarker(_ state: BauakteWorkflow.State, number: Int) -> some View {
        ZStack {
            switch state {
            case .done:
                Image(systemName: "checkmark.circle.fill").font(.title2).foregroundStyle(Theme.success)
            case .skipped:
                Image(systemName: "minus.circle.fill").font(.title2).foregroundStyle(Theme.textTertiary)
            case .next:
                Circle().fill(Theme.accentFill).frame(width: 26, height: 26)
                Text("\(number)").font(Typo.labelStrong.monospacedDigit()).foregroundStyle(Theme.onAccent)
            case .open:
                Circle().strokeBorder(Theme.textTertiary, lineWidth: 1.5).frame(width: 26, height: 26)
                Text("\(number)").font(Typo.labelStrong.monospacedDigit()).foregroundStyle(Theme.textSecondary)
            }
        }
        .frame(width: 28, height: 28)
        .accessibilityHidden(true)
    }

    // MARK: Offene Aufgaben und Mängel

    @ViewBuilder
    private func openPoints(_ content: BauakteContent, wide: Bool) -> some View {
        let open = content.openItems.sorted { lhs, rhs in
            if (lhs.kind == .defect) != (rhs.kind == .defect) { return lhs.kind == .defect }
            return (lhs.dueDate ?? "9999") < (rhs.dueDate ?? "9999")
        }
        if modules.anyOn(AppModules.tasks, AppModules.defects) {
            Section {
                if open.isEmpty {
                    Button { select(.work, wide: wide) } label: {
                        HStack(spacing: Space.m) {
                            Image(systemName: "checkmark.circle")
                                .foregroundStyle(Theme.success)
                                .frame(width: 28)
                                .accessibilityHidden(true)
                            Text(content.workItems.isEmpty ? "Keine Aufgaben und Mängel" : "Alles erledigt")
                                .foregroundStyle(Theme.textSecondary)
                                .frame(maxWidth: .infinity, alignment: .leading)
                            Chevron()
                        }
                        .frame(minHeight: Size.minTap)
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .id("openPoints")
                } else {
                    ForEach(Array(open.prefix(3).enumerated()), id: \.element.id) { index, item in
                        WorkItemRow(item: item, assigneeName: item.assigneeUserId.flatMap { content.names[$0] }) {
                            Task { await toggle(item) }
                        }
                        .listRowInsets(EdgeInsets(top: 0, leading: Space.xs, bottom: 0, trailing: Space.l))
                        .id(index == 0 ? "openPoints" : item.id.uuidString)
                    }
                    if open.count > 3 {
                        Button("Alle \(open.count) anzeigen") { select(.work, wide: wide) }
                            .frame(minHeight: Size.minTap)
                    }
                }
            } header: {
                HStack {
                    Text(open.isEmpty ? "Offene Aufgaben und Mängel" : "Offene Aufgaben und Mängel (\(open.count))")
                    Spacer()
                    if content.openDefects > 0 {
                        Text(content.openDefects == 1 ? "1 Mangel" : "\(content.openDefects) Mängel")
                            .font(Typo.metaStrong)
                            .foregroundStyle(Theme.danger)
                            .textCase(nil)
                    }
                }
            }
        }
    }

    // MARK: Fotos und Dokumentation

    private func documentation(_ content: BauakteContent, wide: Bool) -> some View {
        Section {
            if !content.photos.isEmpty {
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack(spacing: Space.s) {
                        ForEach(Array(content.photos.prefix(12).enumerated()), id: \.element.id) { index, file in
                            Button {
                                if file.isVideo { playing = file } else { viewing = file }
                            } label: {
                                PhotoThumbnail(file: file, radius: Radius.s, showsCaption: false)
                                    .frame(width: 80, height: 80)
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
                .id("photos")
            }
            areaRow(.media, content: content, wide: wide)
                .id("documentation")
            if BauakteArea.diary.isAvailable(modules) { areaRow(.diary, content: content, wide: wide) }
            if BauakteArea.protocols.isAvailable(modules) { areaRow(.protocols, content: content, wide: wide) }
        } header: {
            Text("Fotos und Dokumentation")
        }
    }

    // MARK: Kontakte

    private struct Fact: Identifiable {
        let id: String
        let symbol: String
        let title: String
        var subtitle: String?
        var tint: Color = Theme.textSecondary
        var phone: URL?
        var tap: (() -> Void)?
    }

    private func factList(_ content: BauakteContent) -> [Fact] {
        let bauakte = content.bauakte
        var list: [Fact] = []
        if let customer = content.customer {
            list.append(Fact(id: "customer", symbol: customer.isCompany ? "building.2" : "person", title: customer.name,
                             subtitle: ["Kunde", customer.customerNumber].compactMap { $0 }.joined(separator: ", "),
                             phone: ContactLink.phone(customer.phone)))
        } else {
            list.append(Fact(id: "customer", symbol: "person.crop.circle.badge.questionmark", title: "Kein Kunde zugeordnet",
                             subtitle: "Antippen zum Zuordnen", tint: Theme.warning, tap: { area = .contacts }))
        }
        if bauakte.siteContactName != nil || bauakte.siteContactPhone != nil {
            list.append(Fact(id: "site", symbol: "person.badge.shield.checkmark", title: bauakte.siteContactName ?? bauakte.siteContactPhone ?? "",
                             subtitle: "Ansprechpartner vor Ort", phone: ContactLink.phone(bauakte.siteContactPhone)))
        }
        // Bauleitung: auf der Baustelle der häufigste Anruf nach dem Kunden.
        if let manager = content.parties.first(where: { $0.role == .siteManager }) {
            list.append(Fact(id: "manager", symbol: "person.badge.key", title: manager.name,
                             subtitle: [manager.role.title, manager.company].compactMap { $0 }.joined(separator: ", "),
                             phone: ContactLink.phone(manager.phone ?? manager.mobile)))
        }
        // Zuständige Person und Beteiligte (online geladen; ohne Verbindung entfällt die Zeile).
        if let responsibleName {
            let others = memberNames.filter { $0 != responsibleName }
            list.append(Fact(id: "responsible", symbol: "person.crop.circle.badge.checkmark", title: responsibleName,
                             subtitle: others.isEmpty ? "Zuständig im Betrieb" : "Zuständig, beteiligt: \(others.joined(separator: ", "))"))
        } else if !memberNames.isEmpty {
            list.append(Fact(id: "members", symbol: "person.2", title: memberNames.joined(separator: ", "), subtitle: "Beteiligt im Betrieb"))
        }
        return list
    }

    /// Kompakte Zeile: Symbol, Name, Rolle; rechts ein schlichtes Telefonsymbol (kein großer Knopf).
    @ViewBuilder
    private func factRow(_ fact: Fact) -> some View {
        let row = HStack(spacing: Space.m) {
            Image(systemName: fact.symbol)
                .foregroundStyle(fact.tint)
                .frame(width: 28)
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 1) {
                Text(fact.title)
                    .foregroundStyle(Theme.textPrimary)
                    .fixedSize(horizontal: false, vertical: true)
                if let subtitle = fact.subtitle {
                    Text(subtitle)
                        .font(Typo.meta)
                        .foregroundStyle(Theme.textSecondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            if let phone = fact.phone {
                Button { openURL(phone) } label: {
                    Image(systemName: "phone.fill")
                        .font(.body.weight(SymbolWeight.control))
                        .foregroundStyle(Theme.accent)
                        .frame(width: Size.minTap, height: Size.minTap)
                        .contentShape(Rectangle())
                }
                .buttonStyle(.borderless)
                .accessibilityLabel("\(fact.title) anrufen")
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

    private func contacts(_ content: BauakteContent, wide: Bool) -> some View {
        Section {
            ForEach(factList(content)) { fact in factRow(fact) }
            areaRow(.contacts, content: content, wide: wide, title: content.parties.isEmpty ? "Kontakte verwalten" : "Alle Kontakte")
        } header: {
            Text("Kontakte")
        }
    }

    // MARK: Beschreibung und Verlauf

    private func history(_ content: BauakteContent, wide: Bool) -> some View {
        Section {
            if let description = FreeText.clean(content.bauakte.description) {
                FreeTextBlock(title: "Beschreibung", text: description, symbol: "doc.text", tint: Theme.textSecondary)
            }
            areaRow(.notes, content: content, wide: wide)
                .id("areas")
        } header: {
            Text("Beschreibung und Verlauf")
        } footer: {
            footer(content)
        }
    }

    /// Zeile zu einem Bereich mit Kurzinfo (man sieht ohne Öffnen, was drin ist); iPad: rechts in der zweiten Spalte.
    private func areaRow(_ item: BauakteArea, content: BauakteContent, wide: Bool, title: String? = nil) -> some View {
        let summary = item.summary(content, projectCount: item == .project ? projectItems(content.bauakte).count : 0)
        let selected = wide && wideArea == item
        return Button { select(item, wide: wide) } label: {
            HStack(spacing: Space.m) {
                Image(systemName: item.symbol)
                    .foregroundStyle(Theme.accent)
                    .frame(width: 28)
                    .accessibilityHidden(true)
                VStack(alignment: .leading, spacing: 2) {
                    Text(title ?? item.title).foregroundStyle(Theme.textPrimary)
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
    }

    private func footer(_ content: BauakteContent) -> some View {
        let state = content.bauakte.syncState == .synced ? "übertragen" : "Übertragung ausstehend"
        if let updated = content.bauakte.updatedAt {
            return Text("Zuletzt geändert \(updated.formatted(.relative(presentation: .named))), \(state)")
        }
        return Text("Auf dem Gerät gespeichert, \(state)")
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
                Text(bauakte.status.shortTitle)
                    .font(Typo.subheadStrong)
                    .lineLimit(1)
                Image(systemName: "chevron.up.chevron.down")
                    .font(.caption2.weight(.semibold))
            }
            .foregroundStyle(bauakte.status.tint)
            .padding(.horizontal, Space.m)
            .padding(.vertical, 5)
            .background(bauakte.status.softTint, in: Capsule())
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
            if content?.viewOnly == true {
                Button(BauakteViewOnly.join, systemImage: "person.badge.plus") { Task { await join() } }
                    .accessibilityIdentifier("joinBauakteButton")
            } else {
            Section {
                Button("Erfassen", systemImage: "plus.circle") { capturing = true }
                    .accessibilityIdentifier("captureMenuItem")
            }
            Section {
                Button("Bauakte als PDF", systemImage: "doc.richtext") { Task { await openPDF(bauakte) } }
                if let url = MapsLink.url(for: bauakte) {
                    Button("Route starten", systemImage: "arrow.triangle.turn.up.right.diamond") { openURL(url) }
                }
                Button("Basisdaten bearbeiten", systemImage: "pencil") { action = .project }
                Button("Verlauf", systemImage: "clock.arrow.circlepath") { show(.notes) }
            }
            }
            if let offline = model.active?.sync?.offline, !offline.planned.contains(bauakteId) {
                // Einsätze der nächsten Tage werden von selbst geladen; alle anderen Bauakten auf Wunsch.
                Section {
                    let pinned = offline.pinned.contains(bauakteId)
                    Button(pinned ? "Nicht mehr bereithalten" : "Auf dem Gerät bereithalten",
                           systemImage: pinned ? "arrow.down.circle.fill" : "arrow.down.circle") {
                        Task { await offline.setPinned(bauakteId, !pinned) }
                    }
                    .accessibilityIdentifier("pinOfflineButton")
                }
            }
            if isMember {
                Section {
                    Button(BauakteViewOnly.leave, systemImage: "person.badge.minus", role: .destructive) { Task { await leave() } }
                        .accessibilityIdentifier("leaveBauakteButton")
                }
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

    /// Aus dem Blatt „Erfassen“: erst schließen, dann Kamera bzw. Formular öffnen.
    private func startCapture(_ item: BauakteAction) {
        capturing = false
        Task {
            try? await Task.sleep(for: .milliseconds(450))
            action = item
        }
    }

    /// „2.–6. Nov.“ bzw. „ab 2. Nov.“
    static func period(_ bauakte: Bauakte) -> String? {
        guard let start = bauakte.plannedStart.flatMap({ CalendarDay.date($0) }) else { return nil }
        let format = Date.FormatStyle.dateTime.day().month(.abbreviated)
        if let end = bauakte.plannedEnd.flatMap({ CalendarDay.date($0) }), end != start {
            return "\(start.formatted(format)) bis \(end.formatted(format))"
        }
        return "ab \(start.formatted(format))"
    }

    // MARK: Laden und Ändern

    private func show(_ target: BauakteArea) {
        area = target
        wideArea = target
    }

    private func select(_ target: BauakteArea, wide: Bool) {
        if wide { wideArea = target } else { area = target }
    }

    /// Unterhaltung der Baustelle aus der zuletzt geladenen Chat-Liste (offline); ohne Treffer öffnet „Chat“ die Liste.
    private func loadChat() async {
        guard let store = model.active?.sync?.store, model.active?.sync?.chat?.isAvailable == true,
              let text = try? await store.meta(ChatListView.cacheKey), let list = ChatConversation.decodeList(Data(text.utf8)) else { return }
        chatConversation = list.filter { $0.bauakteId == bauakteId }
            .max { ($0.lastMessageAt ?? .distantPast) < ($1.lastMessageAt ?? .distantPast) }?.id
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
            case "abnahme", "abnahme-pruefen", "abnahme-unterschrift": action = .acceptance
            case "tagebuch-neu", "tagebuch-diktat", "tagebuch-entwurf": action = .diary
            case "kamera-serie": action = .photo
            case "mangel-neu": action = .defect
            case "basisdaten-neu": action = .project
            case "kamera": action = .photo
            case "bauakte-erfassen": capturing = true
            case "foto-vorschau", "foto-kunde":
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

    /// Ob man selbst als Beteiligter eingetragen ist (online; ohne Verbindung bleibt „Austragen“ verborgen).
    private func loadMembership() async {
        guard let active = model.active else { return }
        let rows: [JSONValue]? = try? await active.client.get("bauakten/\(bauakteId.uuidString.lowercased())/members")
        isMember = (rows ?? []).contains { $0["userId"]?.string?.lowercased() == active.user.id.uuidString.lowercased() && $0["role"]?.string == "member" }
        let current = (rows ?? []).filter { $0["isActive"]?.bool != false }
        responsibleName = current.first { $0["role"]?.string?.lowercased() == "responsible" }?["displayName"]?.string
        memberNames = current.compactMap { $0["displayName"]?.string }
    }

    /// „Mich als Beteiligten eintragen“: danach darf man die Bauakte bearbeiten (wird protokolliert, die Leitung erfährt es).
    private func join() async {
        guard let active = model.active, let sync = active.sync else { return }
        joining = true
        defer { joining = false }
        do throws(APIError) {
            try await active.client.assignToMe(bauakteId)
            try? await sync.store.setViewOnly(bauakte: bauakteId, false)
            _ = await sync.load(bauakte: bauakteId)
            isMember = true
            toast = "Als Beteiligter eingetragen. Bearbeiten ist freigeschaltet."
            await reload()
        } catch {
            toast = error == .offline ? "Ohne Verbindung nicht möglich. Bitte später erneut versuchen." : error.message
        }
    }

    /// „Als Beteiligter austragen“: Die Bauakte bleibt sichtbar; ohne andere Zuteilung verschwindet sie vom Gerät.
    private func leave() async {
        guard let active = model.active, let sync = active.sync else { return }
        do throws(APIError) {
            try await active.client.leaveBauakte(bauakteId)
            isMember = false
            await sync.run(includeScope: true)
            toast = "Als Beteiligter ausgetragen."
            await reload()
        } catch {
            toast = error == .offline ? "Ohne Verbindung nicht möglich. Bitte später erneut versuchen." : error.message
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

/// Bauakte liegt nicht auf dem Gerät – meist, weil man ihr nicht zugeteilt ist (offline liegen nur zugeteilte).
/// Alle sehen alle Bauakten: Kopf online anzeigen („Nur ansehen“) und „Mich als Beteiligten eintragen“ anbieten;
/// danach wird sie vollständig geladen und ist bearbeitbar.
struct MissingBauakteView: View {
    let bauakteId: UUID
    let reload: () async -> Void

    @Environment(AppModel.self) private var model
    @State private var working = false
    @State private var message: String?
    @State private var triedLoading = false
    @State private var remote: RemoteBauakte?

    var body: some View {
        Group {
            if let remote {
                preview(remote)
            } else {
                ContentUnavailableView {
                    Label(triedLoading ? "Bauakte nicht verfügbar" : "Bauakte wird geladen …", systemImage: "folder.badge.questionmark")
                } description: {
                    Text(message ?? (triedLoading
                        ? "Die Bauakte konnte nicht geladen werden."
                        : "Die Bauakte liegt noch nicht auf diesem Gerät."))
                } actions: {
                    if triedLoading {
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
            }
        }
        .background(Theme.canvas)
        .task { await load() }
    }

    /// Kopf der Bauakte (online) mit Hinweis „Nur ansehen“ und Eintragen-Knopf.
    private func preview(_ remote: RemoteBauakte) -> some View {
        let bauakte = remote.bauakte
        return List {
            Section {
                VStack(alignment: .leading, spacing: Space.xs) {
                    HStack(spacing: Space.s) {
                        Text(bauakte.number ?? "ohne Nummer")
                            .font(Typo.subhead.monospacedDigit())
                            .foregroundStyle(Theme.textSecondary)
                        Spacer(minLength: Space.s)
                        StatusDot(color: bauakte.status.tint)
                        Text(bauakte.status.shortTitle).font(Typo.subhead)
                    }
                    Text(bauakte.title)
                        .font(Typo.title)
                        .foregroundStyle(Theme.textPrimary)
                        .fixedSize(horizontal: false, vertical: true)
                        .accessibilityAddTraits(.isHeader)
                }
                .padding(.vertical, Space.xs)
                if let address = bauakte.addressLine {
                    Label(address, systemImage: "mappin.and.ellipse")
                }
                if let contact = bauakte.siteContactName ?? bauakte.siteContactPhone {
                    Label(contact, systemImage: "person.badge.shield.checkmark")
                }
            }
            Section {
                Callout(tone: .info, title: "Nur ansehen", message: message ?? BauakteViewOnly.banner, symbol: "eye")
                    .listRowBackground(Color.clear)
                    .listRowInsets(EdgeInsets(top: Space.xs, leading: 0, bottom: Space.xs, trailing: 0))
                    .accessibilityIdentifier("viewOnlyBanner")
                if bauakte.status != .cancelled {
                    Button {
                        Task { await assignToMe() }
                    } label: {
                        ActionLabel(LocalizedStringKey(BauakteViewOnly.join), systemImage: "person.badge.plus", isBusy: working)
                            .frame(maxWidth: .infinity)
                    }
                    .estrichButton(.primary, size: .regular)
                    .disabled(working)
                    .listRowBackground(Color.clear)
                    .accessibilityIdentifier("assignToMeButton")
                }
            } footer: {
                Text("Auf dem Gerät liegen nur Bauakten, denen Sie zugeteilt sind. Nach dem Eintragen wird die Bauakte vollständig geladen, auch für unterwegs ohne Verbindung.")
            }
        }
        .listStyle(.insetGrouped)
        .navigationTitle(bauakte.number ?? "Bauakte")
    }

    private func load() async {
        guard let active = model.active, let sync = active.sync else { return }
        working = true
        defer { working = false }
        // Zuerst versuchen, sie aufs Gerät zu laden (eingeplant oder gerade zugeteilt).
        let outcome = await sync.load(bauakte: bauakteId)
        await reload()
        if outcome == .offline {
            message = "Ohne Verbindung kann die Bauakte nicht geladen werden. Bitte später erneut versuchen."
        } else if let fetched = try? await active.client.onlineBauakte(bauakteId) {
            remote = fetched
        }
        triedLoading = true
    }

    private func assignToMe() async {
        guard let active = model.active, let sync = active.sync else { return }
        working = true
        defer { working = false }
        do throws(APIError) {
            try await active.client.assignToMe(bauakteId)
            try? await sync.store.setViewOnly(bauakte: bauakteId, false)
            _ = await sync.load(bauakte: bauakteId)
            await reload()
        } catch {
            message = error == .offline
                ? "Ohne Verbindung nicht möglich. Bitte später erneut versuchen."
                : error.message
        }
    }
}
