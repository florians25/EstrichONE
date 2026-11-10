import EstrichCore
import SwiftUI

/// „Urlaub und Abwesenheit“: eigenes Urlaubskonto (mit Verfall des Resturlaubs), eigene Anträge mit Stornieren, Urlaub beantragen
/// mit Vorschau (Arbeitstage, Rest, Hinweise) und für Genehmiger die offenen Anträge. Nur online; Berechnung auf dem Server.
struct AbsenceOverviewView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.shotScene) private var shotScene
    @State private var mine: MyVacation?
    @State private var pending: [AbsenceRequestItem] = []
    @State private var message: String?
    @State private var requesting = false
    @State private var approvals = false
    @State private var cancelling: AbsenceRequestItem?
    @State private var error: String?

    var body: some View {
        List {
            if let message {
                Section { Label(message, systemImage: "wifi.slash").foregroundStyle(Theme.textSecondary) }
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
                                Image(systemName: "chevron.right").font(.footnote.weight(.semibold)).foregroundStyle(Theme.textTertiary)
                            }
                        }
                        .accessibilityIdentifier("absenceApprovalsRow")
                    }
                }
                Section("Meine Anträge") {
                    if mine.requests.isEmpty {
                        Text("Noch keine Anträge.").foregroundStyle(Theme.textSecondary)
                    }
                    ForEach(mine.requests) { request in
                        AbsenceRequestRow(request: request, showsName: false)
                            .swipeActions {
                                if request.canCancel {
                                    Button(request.status == .approved ? "Stornieren" : "Zurückziehen", role: .destructive) { cancelling = request }
                                }
                            }
                            .contextMenu {
                                if request.canCancel {
                                    Button(request.status == .approved ? "Stornieren" : "Zurückziehen", systemImage: "xmark.circle", role: .destructive) { cancelling = request }
                                }
                            }
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
                            }
                            .accessibilityElement(children: .combine)
                        }
                    }
                }
            } else if message == nil {
                Section { ProgressView().frame(maxWidth: .infinity) }
            }
        }
        .navigationTitle("Abwesenheiten")
        .toolbar {
            ToolbarItem(placement: .primaryAction) {
                Button { requesting = true } label: { Label("Urlaub beantragen", systemImage: "plus") }
                    .disabled(mine == nil)
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

    private func load() async {
        guard let client = model.active?.client else { return }
        do throws(APIError) {
            let loaded = try await client.myVacation()
            mine = loaded
            pending = loaded.canApprove ? ((try? await client.pendingAbsenceRequests()) ?? []) : []
            message = nil
            if shotScene == "abwesenheit-antrag", !requesting { requesting = true }
            if shotScene == "abwesenheit-genehmigung", !approvals { approvals = true }
        } catch {
            message = error.status == 403 ? "Für Urlaubsanträge fehlt Ihnen die Berechtigung." : "Urlaub und Abwesenheiten sind nur mit Verbindung zum Server verfügbar."
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
                    Text("Änderungsantrag").font(Typo.meta).foregroundStyle(Theme.accent)
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

/// Antrag: Art, Zeitraum, halbe Tage, Bemerkung; Vorschau vom Server (Arbeitstage ohne Wochenende und Feiertage, Rest, Hinweise).
struct AbsenceRequestSheet: View {
    let types: [AbsenceType]
    let onDone: () -> Void

    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var typeId: UUID?
    @State private var from = Calendar.current.date(byAdding: .day, value: 14, to: .now) ?? .now
    @State private var to = Calendar.current.date(byAdding: .day, value: 18, to: .now) ?? .now
    @State private var halfDayStart = false
    @State private var halfDayEnd = false
    @State private var note = ""
    @State private var preview: AbsencePreview?
    @State private var previewError: String?
    @State private var sending = false
    @State private var error: String?

    private var draft: AbsenceRequestDraft {
        AbsenceRequestDraft(typeId: typeId, from: CalendarDay.string(from), to: CalendarDay.string(to), halfDayStart: halfDayStart,
                            halfDayEnd: halfDayEnd, note: note)
    }

    var body: some View {
        Form {
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
                TextField("Bemerkung (optional)", text: $note, axis: .vertical)
            }
            Section("Vorschau") {
                if let problem = draft.problem {
                    Text(problem).foregroundStyle(Theme.danger)
                } else if let preview {
                    Text(preview.summary).font(Typo.subhead)
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
        .navigationTitle("Antrag")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            ToolbarItem(placement: .cancellationAction) { Button("Abbrechen") { dismiss() } }
            ToolbarItem(placement: .confirmationAction) {
                Button("Beantragen") { Task { await send() } }
                    .disabled(draft.problem != nil || sending)
                    .accessibilityIdentifier("sendAbsenceRequest")
            }
        }
        .alert("Nicht möglich", isPresented: Binding(get: { error != nil }, set: { if !$0 { error = nil } })) {
            Button("OK", role: .cancel) {}
        } message: { Text(error ?? "") }
        .onAppear { if typeId == nil { typeId = types.first(where: { $0.key == "vacation" })?.id ?? types.first?.id } }
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

    private func send() async {
        guard let client = model.active?.client else { return }
        sending = true
        defer { sending = false }
        do throws(APIError) {
            _ = try await client.requestAbsence(draft)
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
                    .controlSize(.regular)
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
