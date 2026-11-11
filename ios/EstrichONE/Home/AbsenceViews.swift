import EstrichCore
import SwiftUI

/// „Urlaub und Abwesenheit“: eigenes Urlaubskonto (mit Verfall des Resturlaubs), eigene Anträge mit Stornieren und
/// Änderungsantrag, Urlaub beantragen mit Vorschau (Arbeitstage, Rest, Feiertage, Hinweise) und Vertretung, für Genehmiger
/// die offenen Anträge. Berechnung auf dem Server; der zuletzt geladene Stand bleibt verschlüsselt auf dem Gerät und wird
/// ohne Verbindung angezeigt.
struct AbsenceOverviewView: View {
    /// Aus einer Benachrichtigung: diesen Antrag hervorheben.
    var focusRequestId: UUID?
    /// Aus einer Benachrichtigung „Antrag eingegangen“: gleich die offenen Anträge zeigen.
    var opensApprovals = false

    @Environment(AppModel.self) private var model
    @Environment(\.shotScene) private var shotScene
    @State private var mine: MyVacation?
    @State private var pending: [AbsenceRequestItem] = []
    @State private var message: String?
    @State private var showsCached = false
    @State private var requesting = false
    @State private var changing: AbsenceRequestItem?
    @State private var approvals = false
    @State private var cancelling: AbsenceRequestItem?
    @State private var error: String?
    @State private var highlighted: UUID?
    @State private var handledLaunchTarget = false

    private static let cacheKey = "hr_me"

    var body: some View {
        ScrollViewReader { proxy in
            list
                .onChange(of: highlighted) { _, id in
                    guard let id else { return }
                    withAnimation { proxy.scrollTo(id, anchor: .center) }
                }
        }
        .navigationTitle("Abwesenheiten")
        .toolbar {
            ToolbarItem(placement: .primaryAction) {
                Button { requesting = true } label: { Label("Urlaub beantragen", systemImage: "plus") }
                    .disabled(mine == nil || showsCached)
                    .accessibilityIdentifier("requestAbsenceButton")
            }
        }
        .sheet(isPresented: $requesting) {
            if let mine {
                NavigationStack {
                    AbsenceRequestSheet(types: mine.requestableTypes) {
                        requesting = false
                        Task { await load() }
                    }
                }
            }
        }
        .sheet(item: $changing) { request in
            if let mine {
                NavigationStack {
                    AbsenceRequestSheet(types: mine.requestableTypes, replaces: request) {
                        changing = nil
                        Task { await load() }
                    }
                }
            }
        }
        .navigationDestination(isPresented: $approvals) {
            AbsenceApprovalsView(onChange: { Task { await load() } })
        }
        .confirmationDialog(cancelling?.status == .approved ? "Abwesenheit stornieren?" : "Antrag zurückziehen?", isPresented: Binding(
            get: { cancelling != nil }, set: { if !$0 { cancelling = nil } }), titleVisibility: .visible) {
            Button(cancelling?.status == .approved ? "Stornieren" : "Zurückziehen", role: .destructive) {
                if let request = cancelling { Task { await cancel(request) } }
            }
        } message: {
            Text(cancelling.map { "\($0.typeName ?? "Abwesenheit") \($0.rangeText)" } ?? "")
        }
        .alert("Nicht möglich", isPresented: Binding(get: { error != nil }, set: { if !$0 { error = nil } })) {
            Button("OK", role: .cancel) {}
        } message: { Text(error ?? "") }
        .task { await load() }
        .refreshable { await load() }
    }

    private var list: some View {
        List {
            if let message {
                Section {
                    Label(message, systemImage: showsCached ? "clock.arrow.circlepath" : "wifi.slash")
                        .font(Typo.subhead)
                        .foregroundStyle(Theme.textSecondary)
                }
            }
            if let mine {
                if let account = mine.vacation {
                    Section("Urlaubskonto \(String(account.year))") {
                        AbsenceAccountCard(account: account)
                    }
                } else {
                    Section {
                        Label("Für Ihr Konto ist noch kein Personalstamm angelegt. Das Büro trägt den Urlaubsanspruch ein.", systemImage: "info.circle")
                            .foregroundStyle(Theme.textSecondary)
                    }
                }
                if mine.canApprove {
                    Section {
                        Button { approvals = true } label: {
                            HStack {
                                Label("Anträge genehmigen", systemImage: "checkmark.seal")
                                Spacer()
                                if !pending.isEmpty { Chip(text: "\(pending.count) offen", tone: .accent) }
                                Chevron()
                            }
                            .frame(minHeight: 44)
                        }
                        .disabled(showsCached)
                        .accessibilityIdentifier("absenceApprovalsRow")
                    }
                }
                Section {
                    if mine.requests.isEmpty {
                        Text("Noch keine Anträge. Mit „+“ beantragen Sie Urlaub oder eine andere Abwesenheit.")
                            .font(Typo.subhead)
                            .foregroundStyle(Theme.textSecondary)
                    }
                    ForEach(mine.requests) { request in
                        requestRow(request, in: mine)
                    }
                } header: {
                    Text("Meine Anträge")
                } footer: {
                    if mine.requests.contains(where: { mine.canRequestChange($0) }) {
                        Text("Genehmigte Abwesenheiten, die noch nicht begonnen haben, lassen sich nach links wischen und ändern. Bis zur Genehmigung der Änderung gilt der bisherige Zeitraum.")
                    }
                }
                if let absences = mine.absences, !absences.isEmpty {
                    Section("Eingetragene Abwesenheiten") {
                        ForEach(absences) { absence in
                            HStack(spacing: Space.s) {
                                StatusDot(color: Color(hexString: absence.color) ?? Theme.accent, size: 9)
                                VStack(alignment: .leading, spacing: 2) {
                                    Text(absence.typeName).font(Typo.body)
                                    Text(AbsenceText.range(from: absence.from, to: absence.to, halfDayStart: absence.halfDayStart, halfDayEnd: absence.halfDayEnd))
                                        .font(Typo.meta).foregroundStyle(Theme.textSecondary)
                                }
                                Spacer()
                                Text("\(AbsenceText.days(absence.workingDays)) AT").font(Typo.meta.monospacedDigit()).foregroundStyle(Theme.textSecondary)
                                    .accessibilityLabel("\(AbsenceText.days(absence.workingDays)) Arbeitstage")
                            }
                            .accessibilityElement(children: .combine)
                        }
                    }
                }
            } else if message == nil {
                Section {
                    ForEach(0..<3, id: \.self) { _ in
                        VStack(alignment: .leading, spacing: Space.s) {
                            SkeletonBar(width: 180, height: 14)
                            SkeletonBar(width: 120, height: 11)
                        }
                        .padding(.vertical, Space.xs)
                    }
                }
                .pulsing()
            }
        }
    }

    private func requestRow(_ request: AbsenceRequestItem, in mine: MyVacation) -> some View {
        let canChange = mine.canRequestChange(request) && !showsCached
        let changePending = request.status == .approved && mine.pendingChange(of: request) != nil
        return AbsenceRequestRow(request: request, showsName: false, changePending: changePending)
            .id(request.id)
            .listRowBackground(highlighted == request.id ? Theme.accentSoft : Theme.surface)
            .swipeActions(edge: .trailing) {
                if request.canCancel && !showsCached {
                    Button(request.status == .approved ? "Stornieren" : "Zurückziehen", systemImage: "xmark.circle", role: .destructive) { cancelling = request }
                }
                if canChange {
                    Button("Ändern", systemImage: "calendar.badge.clock") { changing = request }
                        .tint(Theme.accentFill)
                }
            }
            .contextMenu {
                if canChange {
                    Button("Änderung beantragen", systemImage: "calendar.badge.clock") { changing = request }
                }
                if request.canCancel && !showsCached {
                    Button(request.status == .approved ? "Stornieren" : "Zurückziehen", systemImage: "xmark.circle", role: .destructive) { cancelling = request }
                }
            }
            .accessibilityActions {
                if canChange { Button("Änderung beantragen") { changing = request } }
                if request.canCancel && !showsCached {
                    Button(request.status == .approved ? "Stornieren" : "Zurückziehen") { cancelling = request }
                }
            }
    }

    private func load() async {
        guard let client = model.active?.client else { return }
        let store = model.active?.sync?.store
        if mine == nil, let store, let text = try? await store.meta(Self.cacheKey),
           let cached = try? JSONCoding.decoder.decode(MyVacation.self, from: Data(text.utf8)) {
            // Sofort den letzten Stand zeigen, während frisch geladen wird.
            mine = cached
            showsCached = true
        }
        do throws(APIError) {
            let loaded = try await client.myVacation()
            mine = loaded
            showsCached = false
            pending = loaded.canApprove ? ((try? await client.pendingAbsenceRequests()) ?? []) : []
            message = nil
            if let store, let data = try? JSONCoding.encoder.encode(loaded) {
                try? await store.setMeta(Self.cacheKey, String(decoding: data, as: UTF8.self))
            }
            if shotScene == "abwesenheit-antrag", !requesting { requesting = true }
            if shotScene == "abwesenheit-genehmigung", !approvals { approvals = true }
            if shotScene == "abwesenheit-aenderung", changing == nil, let request = loaded.requests.first(where: { loaded.canRequestChange($0) }) {
                changing = request
            }
            applyLaunchTarget(loaded)
        } catch {
            if error.status == 403 {
                message = "Für Urlaubsanträge fehlt Ihnen die Berechtigung."
            } else if mine != nil {
                message = "Keine Verbindung zum Server. Angezeigt wird der zuletzt geladene Stand; Anträge stellen und ändern geht erst wieder mit Verbindung."
            } else {
                message = "Urlaub und Abwesenheiten sind nur mit Verbindung zum Server verfügbar."
            }
        }
    }

    /// Ziel aus der Benachrichtigung einmal anwenden: offene Anträge öffnen bzw. den Antrag hervorheben.
    private func applyLaunchTarget(_ loaded: MyVacation) {
        guard !handledLaunchTarget else { return }
        handledLaunchTarget = true
        let isOwn = focusRequestId.map { id in loaded.requests.contains { $0.id == id } } ?? false
        if loaded.canApprove, opensApprovals || (focusRequestId != nil && !isOwn && pending.contains { $0.id == focusRequestId }) {
            approvals = true
        } else if isOwn, let id = focusRequestId {
            highlighted = id
            Task {
                try? await Task.sleep(for: .seconds(2.5))
                withAnimation { highlighted = nil }
            }
        }
    }

    private func cancel(_ request: AbsenceRequestItem) async {
        guard let client = model.active?.client else { return }
        do throws(APIError) {
            _ = try await client.cancelAbsence(request.id)
            await load()
        } catch {
            self.error = error.message
        }
    }
}

/// Konto: Rest groß, darunter Anspruch, Übertrag (mit Verfall), genommen, geplant, beantragt.
struct AbsenceAccountCard: View {
    let account: VacationAccount

    var body: some View {
        VStack(alignment: .leading, spacing: Space.m) {
            HStack(alignment: .firstTextBaseline) {
                VStack(alignment: .leading, spacing: 2) {
                    Text("Verbleibend").font(Typo.label).foregroundStyle(Theme.textSecondary)
                    Text("\(AbsenceText.days(account.remaining)) Tage")
                        .font(Typo.metric)
                        .foregroundStyle(account.remaining < 0 ? Theme.danger : Theme.textPrimary)
                }
                Spacer()
                if account.requested > 0 {
                    Text("nach Anträgen \(AbsenceText.days(account.remainingAfterRequests))").font(Typo.meta).foregroundStyle(Theme.textSecondary)
                }
            }
            Grid(alignment: .leading, horizontalSpacing: Space.l, verticalSpacing: Space.xs) {
                fact("Anspruch", account.entitlement, note: account.entitlement != account.annualEntitlement ? "anteilig" : nil)
                fact("Übertrag Vorjahr", account.carryOver)
                if account.adjustment != 0 { fact("Korrektur", account.adjustment) }
                if account.carryOverExpired > 0 { fact("Verfallen", -account.carryOverExpired) }
                fact("Genommen", account.taken)
                fact("Geplant", account.planned)
                fact("Beantragt", account.requested)
            }
            if account.carryOverExpiring > 0, let expires = account.carryOverExpiresOn {
                Label("\(AbsenceText.days(account.carryOverExpiring)) Tage Resturlaub verfallen nach dem \(AbsenceText.range(from: expires, to: expires)), wenn sie bis dahin nicht genommen oder geplant sind.",
                      systemImage: "exclamationmark.triangle")
                    .font(Typo.meta)
                    .foregroundStyle(Theme.warning)
            }
        }
        .padding(.vertical, Space.xs)
        .accessibilityIdentifier("vacationAccount")
    }

    private func fact(_ title: String, _ value: Decimal, note: String? = nil) -> some View {
        GridRow {
            Text(title).font(Typo.subhead).foregroundStyle(Theme.textSecondary)
            Text(AbsenceText.days(value)).font(Typo.subheadStrong.monospacedDigit()).gridColumnAlignment(.trailing)
            Text(note ?? "").font(Typo.meta).foregroundStyle(Theme.textTertiary)
        }
    }
}

/// Zeile eines Antrags: Art (Farbe), Zeitraum, Tage, Status; für Genehmiger mit Name, Rest danach und Hinweisen.
struct AbsenceRequestRow: View {
    let request: AbsenceRequestItem
    var showsName = true
    /// Zu diesem genehmigten Antrag ist eine Änderung beantragt.
    var changePending = false

    var body: some View {
        HStack(alignment: .top, spacing: Space.s) {
            StatusDot(color: Color(hexString: request.typeColor) ?? Theme.accent, size: 9).padding(.top, 6)
            VStack(alignment: .leading, spacing: 3) {
                HStack(alignment: .firstTextBaseline) {
                    Text(showsName ? request.userName : request.typeName ?? "Abwesenheit").font(Typo.bodyStrong).lineLimit(1)
                    Spacer(minLength: Space.s)
                    Chip(text: request.statusText, tone: tone)
                }
                Text([showsName ? request.typeName : nil, request.rangeText, "\(AbsenceText.days(request.workingDays)) Arbeitstage"]
                    .compactMap { $0 }.joined(separator: " · "))
                    .font(Typo.subhead).foregroundStyle(Theme.textSecondary)
                if request.replacesRequestId != nil {
                    Label("Änderungsantrag", systemImage: "calendar.badge.clock").labelStyle(TightLabelStyle())
                        .font(Typo.metaStrong).foregroundStyle(Theme.accent)
                } else if changePending {
                    Label("Änderung beantragt, bis zur Entscheidung gilt dieser Zeitraum", systemImage: "calendar.badge.clock")
                        .labelStyle(TightLabelStyle())
                        .font(Typo.meta).foregroundStyle(Theme.accent)
                }
                let details = [request.substituteName.map { "Vertretung: \($0)" }, request.decisionComment, request.note].compactMap { $0 }
                if !details.isEmpty {
                    Text(details.joined(separator: " · ")).font(Typo.meta).foregroundStyle(Theme.textSecondary).lineLimit(3)
                }
                if showsName, let rest = request.remainingAfter {
                    Text("Rest danach \(AbsenceText.days(rest)) Tage").font(Typo.meta).foregroundStyle(rest < 0 ? Theme.danger : Theme.textSecondary)
                }
                ForEach(request.warnings, id: \.self) { warning in
                    Label(warning, systemImage: "exclamationmark.triangle").font(Typo.meta).foregroundStyle(Theme.warning)
                }
            }
        }
        .padding(.vertical, Space.xxs)
        .accessibilityElement(children: .combine)
    }

    private var tone: Chip.Tone {
        switch request.status {
        case .pending: .warning
        case .approved: .success
        case .rejected: .danger
        case .cancelled: .neutral
        }
    }
}

/// Antrag oder Änderungsantrag: Art, Zeitraum, halbe Tage, Vertretung, Bemerkung; Vorschau vom Server (Arbeitstage ohne
/// Wochenende und Feiertage, Rest, Hinweise).
struct AbsenceRequestSheet: View {
    let types: [AbsenceType]
    /// Änderungsantrag zu diesem genehmigten Antrag.
    var replaces: AbsenceRequestItem?
    let onDone: () -> Void

    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var typeId: UUID?
    @State private var from = Calendar.current.date(byAdding: .day, value: 14, to: .now) ?? .now
    @State private var to = Calendar.current.date(byAdding: .day, value: 18, to: .now) ?? .now
    @State private var halfDayStart = false
    @State private var halfDayEnd = false
    @State private var note = ""
    @State private var substitute: UUID?
    @State private var people: [DirectoryUser] = []
    @State private var preview: AbsencePreview?
    @State private var previewError: String?
    @State private var sending = false
    @State private var error: String?
    @State private var prepared = false
    /// Eine ID je Formular: Wiederholungen nach Verbindungsabbruch legen keinen zweiten Antrag an.
    @State private var requestId = UUID()

    private var draft: AbsenceRequestDraft {
        AbsenceRequestDraft(typeId: typeId, from: CalendarDay.string(from), to: CalendarDay.string(to), halfDayStart: halfDayStart,
                            halfDayEnd: halfDayEnd, note: note, substituteUserId: substitute, replacesRequestId: replaces?.id)
    }

    /// Unverändert gegenüber dem genehmigten Antrag (Änderung ohne Änderung).
    private var unchanged: Bool {
        guard let replaces else { return false }
        let base = replaces.changeDraft.normalized
        let current = draft.normalized
        return base.from == current.from && base.to == current.to && base.typeId == current.typeId && base.halfDayStart == current.halfDayStart
            && base.halfDayEnd == current.halfDayEnd && base.substituteUserId == current.substituteUserId && base.note == current.note
    }

    private var candidates: [DirectoryUser] {
        let me = model.active?.user.id
        let ownerId = replaces?.userId ?? me
        return people.filter { ($0.isActive || $0.id == substitute) && $0.id != ownerId }
            .sorted { $0.displayName.localizedCompare($1.displayName) == .orderedAscending }
    }

    var body: some View {
        Form {
            if let replaces {
                Section {
                    Label {
                        Text("Genehmigt: \(replaces.typeName ?? "Abwesenheit") \(replaces.rangeText). Bis zur Genehmigung der Änderung gilt der bisherige Zeitraum.")
                    } icon: {
                        Image(systemName: "calendar.badge.clock").foregroundStyle(Theme.accent)
                    }
                    .font(Typo.subhead)
                }
            }
            Section {
                Picker("Art", selection: $typeId) {
                    ForEach(types) { type in
                        Text(type.name).tag(Optional(type.id))
                    }
                }
                DatePicker("Von", selection: $from, displayedComponents: .date)
                DatePicker("Bis einschließlich", selection: $to, in: from..., displayedComponents: .date)
                Toggle("Erster Tag nur halb", isOn: $halfDayStart)
                if CalendarDay.string(from) != CalendarDay.string(to) {
                    Toggle("Letzter Tag nur halb", isOn: $halfDayEnd)
                }
            }
            Section {
                Picker(selection: $substitute) {
                    Text("Keine").tag(UUID?.none)
                    ForEach(candidates) { person in
                        Text(person.displayName).tag(Optional(person.id))
                    }
                } label: {
                    Label("Vertretung", systemImage: "person.2")
                }
                .accessibilityIdentifier("absenceSubstitutePicker")
                TextField("Bemerkung (optional)", text: $note, axis: .vertical)
            } footer: {
                Text(people.isEmpty ? "Die Liste der Kollegen wird geladen, sobald eine Verbindung besteht."
                     : "Die Vertretung wird nach der Genehmigung benachrichtigt.")
            }
            Section("Vorschau") {
                if let problem = draft.problem {
                    Text(problem).foregroundStyle(Theme.danger)
                } else if let preview {
                    if unchanged {
                        Text("Noch unverändert: Zeitraum, Art oder Vertretung anpassen.").font(Typo.meta).foregroundStyle(Theme.textSecondary)
                    }
                    Text(preview.summary).font(Typo.subhead)
                    if let holidays = preview.holidayText {
                        Label(holidays, systemImage: "calendar").font(Typo.meta).foregroundStyle(Theme.textSecondary)
                    }
                    ForEach(preview.warnings, id: \.self) { warning in
                        Label(warning, systemImage: "exclamationmark.triangle").font(Typo.meta).foregroundStyle(Theme.warning)
                    }
                } else if let previewError {
                    Text(previewError).foregroundStyle(Theme.textSecondary)
                } else {
                    ProgressView()
                }
            }
        }
        .navigationTitle(replaces == nil ? "Antrag" : "Änderung beantragen")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            ToolbarItem(placement: .cancellationAction) { Button("Abbrechen") { dismiss() } }
            ToolbarItem(placement: .confirmationAction) {
                Button(replaces == nil ? "Beantragen" : "Senden") { Task { await send() } }
                    .disabled(draft.problem != nil || unchanged || sending)
                    .accessibilityIdentifier("sendAbsenceRequest")
            }
        }
        .alert("Nicht möglich", isPresented: Binding(get: { error != nil }, set: { if !$0 { error = nil } })) {
            Button("OK", role: .cancel) {}
        } message: { Text(error ?? "") }
        .sensoryFeedback(.error, trigger: error != nil) { _, failed in failed }
        .onAppear(perform: prepare)
        .task { people = await DirectoryCache.load(model) }
        .task(id: draft) {
            // Kurz warten, damit nicht jede Änderung einzeln abgefragt wird.
            try? await Task.sleep(for: .milliseconds(300))
            guard !Task.isCancelled, draft.problem == nil, let client = model.active?.client else { return }
            do throws(APIError) {
                preview = try await client.previewAbsence(draft)
                previewError = nil
            } catch {
                preview = nil
                previewError = error.message
            }
        }
    }

    /// Einmalig vorbelegen: Urlaub bzw. bei Änderungen der bisherige Antrag.
    private func prepare() {
        guard !prepared else { return }
        prepared = true
        if let replaces {
            typeId = replaces.typeId.flatMap { id in types.contains { $0.id == id } ? id : nil } ?? types.first?.id
            from = CalendarDay.date(replaces.from) ?? from
            to = CalendarDay.date(replaces.to) ?? to
            halfDayStart = replaces.halfDayStart
            halfDayEnd = replaces.halfDayEnd
            note = replaces.note ?? ""
            substitute = replaces.substituteUserId
        } else if typeId == nil {
            typeId = types.first(where: { $0.key == "vacation" })?.id ?? types.first?.id
        }
    }

    private func send() async {
        guard let client = model.active?.client else { return }
        sending = true
        defer { sending = false }
        do throws(APIError) {
            _ = try await client.requestAbsence(id: requestId, draft)
            onDone()
        } catch {
            self.error = error.message
        }
    }
}

/// Offene Anträge für Genehmiger: genehmigen mit einem Tipp, ablehnen mit Begründung.
struct AbsenceApprovalsView: View {
    var onChange: () -> Void = {}

    @Environment(AppModel.self) private var model
    @State private var items: [AbsenceRequestItem] = []
    @State private var loaded = false
    @State private var offline = false
    @State private var rejecting: AbsenceRequestItem?
    @State private var reason = ""
    @State private var error: String?

    var body: some View {
        List {
            if offline {
                Label("Die Anträge sind nur mit Verbindung zum Server verfügbar.", systemImage: "wifi.slash").foregroundStyle(Theme.textSecondary)
            } else if loaded, items.isEmpty {
                EmptyStateView(symbol: "checkmark.circle", title: "Keine offenen Anträge")
            }
            ForEach(items) { item in
                VStack(alignment: .leading, spacing: Space.s) {
                    AbsenceRequestRow(request: item)
                    HStack(spacing: Space.s) {
                        Button { Task { await decide(item, approve: true, comment: nil) } } label: {
                            Label("Genehmigen", systemImage: "checkmark").frame(maxWidth: .infinity)
                        }
                        .buttonStyle(.borderedProminent)
                        .tint(Theme.success)
                        Button { reason = ""; rejecting = item } label: {
                            Label("Ablehnen", systemImage: "xmark").frame(maxWidth: .infinity)
                        }
                        .buttonStyle(.bordered)
                        .tint(Theme.danger)
                    }
                    .controlSize(.large)
                }
                .padding(.vertical, Space.xs)
            }
        }
        .navigationTitle("Anträge genehmigen")
        .alert("Antrag ablehnen", isPresented: Binding(get: { rejecting != nil }, set: { if !$0 { rejecting = nil } })) {
            TextField("Begründung", text: $reason)
            Button("Abbrechen", role: .cancel) {}
            Button("Ablehnen", role: .destructive) {
                if let item = rejecting { Task { await decide(item, approve: false, comment: reason) } }
            }
            .disabled(reason.trimmingCharacters(in: .whitespaces).isEmpty)
        } message: {
            Text(rejecting.map { "\($0.userName): \($0.typeName ?? "Abwesenheit") \($0.rangeText)" } ?? "")
        }
        .alert("Nicht möglich", isPresented: Binding(get: { error != nil }, set: { if !$0 { error = nil } })) {
            Button("OK", role: .cancel) {}
        } message: { Text(error ?? "") }
        .task { await load() }
        .refreshable { await load() }
    }

    private func load() async {
        guard let client = model.active?.client else { return }
        do throws(APIError) {
            items = try await client.pendingAbsenceRequests()
            offline = false
        } catch {
            offline = true
        }
        loaded = true
    }

    private func decide(_ item: AbsenceRequestItem, approve: Bool, comment: String?) async {
        guard let client = model.active?.client else { return }
        do throws(APIError) {
            _ = try await client.decideAbsence(item.id, approve: approve, comment: comment)
            await load()
            onChange()
        } catch {
            self.error = error.message
        }
    }
}
