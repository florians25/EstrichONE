import EstrichCore
import SwiftUI

/// Abnahmeprotokoll erfassen (VOB/B § 12): Teilnehmer, Ort, abgenommene Leistung, Ergebnis, Mängel mit Frist, Fotos,
/// Notizen und die Unterschriften beider Seiten. Funktioniert offline; die Übertragung übernimmt die Synchronisation.
/// Fehlt die Unterschrift des Auftraggebers, kann er später über einen Freigabelink zustimmen.
struct NewAcceptanceView: View {
    let bauakte: Bauakte
    var customerName: String?
    let onDone: () -> Void

    @Environment(AppModel.self) private var model
    @State private var clientName = ""
    @State private var contractorName = ""
    @State private var location = ""
    @State private var scope = ""
    @State private var result: AcceptanceResult?
    @State private var defects: [AcceptanceDefect] = []
    @State private var notes = ""
    @State private var photoIds: [UUID] = []
    @State private var capturing = false
    @State private var captureStarted = Date.now
    @State private var clientSignature: Data?
    @State private var contractorSignature: Data?
    @State private var signing: Side?
    @State private var error: String?
    @State private var saving = false
    @State private var prefilled = false

    private enum Side: String, Identifiable {
        case client, contractor
        var id: String { rawValue }
        var title: String { self == .client ? "Unterschrift Auftraggeber" : "Unterschrift Auftragnehmer" }
    }

    var body: some View {
        NavigationStack {
            Form {
                if let error {
                    Section {
                        Label(error, systemImage: "exclamationmark.triangle.fill").foregroundStyle(Theme.danger)
                            .accessibilityIdentifier("acceptanceError")
                    }
                }
                Section {
                    VStack(alignment: .leading, spacing: Space.xs) {
                        Text(bauakte.title).font(Typo.title).foregroundStyle(Theme.textPrimary)
                        Text([bauakte.number, bauakte.addressLine].compactMap { $0 }.joined(separator: " · "))
                            .font(Typo.subhead).foregroundStyle(Theme.textSecondary)
                    }
                    .listRowBackground(Color.clear)
                    .listRowInsets(EdgeInsets(top: Space.s, leading: Space.xs, bottom: Space.s, trailing: Space.xs))
                }
                Section {
                    HStack(spacing: Space.s) {
                        ChoiceButton(title: "Abgenommen", symbol: "checkmark.seal.fill", isSelected: result == .accepted, tone: .success) {
                            result = .accepted
                        }
                        ChoiceButton(title: "Mit Vorbehalt", symbol: "exclamationmark.triangle.fill", isSelected: result == .acceptedWithReservations,
                                     tone: .warning) {
                            result = .acceptedWithReservations
                        }
                        ChoiceButton(title: "Verweigert", symbol: "xmark.octagon.fill", isSelected: result == .refused, tone: .danger) {
                            result = .refused
                        }
                    }
                    .listRowInsets(EdgeInsets(top: Space.s, leading: Space.s, bottom: Space.s, trailing: Space.s))
                    .accessibilityIdentifier("acceptanceResultPicker")
                } header: {
                    Text("Ergebnis")
                } footer: {
                    if result == .acceptedWithReservations || result == .refused {
                        Text("Bitte die Mängel bzw. Vorbehalte unten erfassen – jeder wird als Aufgabe mit Frist angelegt.")
                    }
                }
                // Beschriftung bleibt sichtbar, auch wenn die Felder vorausgefüllt sind.
                Section("Teilnehmer und Ort") {
                    LabeledField("Auftraggeber") {
                        TextField("Name", text: $clientName)
                            .textContentType(.name)
                            .accessibilityIdentifier("acceptanceClientField")
                    }
                    LabeledField("Auftragnehmer") {
                        TextField("Name, Firma", text: $contractorName)
                            .accessibilityIdentifier("acceptanceContractorField")
                    }
                    LabeledField("Ort") {
                        TextField("Adresse der Baustelle", text: $location)
                    }
                }
                Section("Abgenommene Leistung") {
                    TextField("z. B. Zementestrich CT-C25-F4, EG und OG", text: $scope, axis: .vertical)
                        .lineLimit(2...6)
                        .accessibilityIdentifier("acceptanceScopeField")
                }
                defectSection
                Section {
                    Button {
                        captureStarted = .now
                        capturing = true
                    } label: {
                        Label(photoIds.isEmpty ? "Fotos aufnehmen" : "\(photoIds.count) \(photoIds.count == 1 ? "Foto" : "Fotos") – weitere aufnehmen", systemImage: "camera")
                    }
                } header: {
                    Text("Fotos")
                }
                Section("Notizen") {
                    TextField("Absprachen, Restarbeiten, Besonderheiten", text: $notes, axis: .vertical)
                        .lineLimit(3...12)
                        .accessibilityIdentifier("acceptanceNotesField")
                }
                Section {
                    signatureRow(.client, data: clientSignature)
                    signatureRow(.contractor, data: contractorSignature)
                } header: {
                    Text("Unterschriften")
                } footer: {
                    Text("Unterschreibt der Auftraggeber nicht vor Ort, kann er das Protokoll später über einen Freigabelink bestätigen.")
                }
            }
            .navigationTitle("Abnahme")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Abbrechen", action: onDone) }
            }
            .safeAreaInset(edge: .bottom) {
                Button { Task { await save() } } label: {
                    ActionLabel("Abnahme sichern", systemImage: "checkmark", isBusy: saving)
                }
                .primaryActionStyle()
                .disabled(saving)
                .accessibilityIdentifier("saveAcceptanceButton")
                .padding(.horizontal, Space.l)
                .padding(.top, Space.m)
                .padding(.bottom, Space.s)
                .background(.bar)
            }
            .fullScreenCover(isPresented: $capturing) {
                CameraView(bauakte: bauakte, allowsChangingBauakte: false, caption: "Abnahme") { _ in
                    capturing = false
                    Task { await collectPhotos() }
                }
            }
            .sheet(item: $signing) { side in
                SignatureView(title: side.title) { png in
                    signing = nil
                    if let png {
                        if side == .client { clientSignature = png } else { contractorSignature = png }
                    }
                }
            }
            .onAppear(perform: prefill)
        }
    }

    private var defectSection: some View {
        Section {
            ForEach($defects) { $defect in
                VStack(alignment: .leading, spacing: 6) {
                    TextField("Mangel, z. B. Fuge Bad nacharbeiten", text: $defect.title)
                        .accessibilityIdentifier("acceptanceDefectField")
                    DatePicker("Frist", selection: dueDateBinding($defect), displayedComponents: .date)
                        .font(.subheadline)
                }
            }
            .onDelete { defects.remove(atOffsets: $0) }
            Button {
                let due = Calendar.current.date(byAdding: .day, value: 14, to: .now) ?? .now
                defects.append(AcceptanceDefect(title: "", dueDate: CalendarDay.string(due)))
            } label: {
                Label("Mangel hinzufügen", systemImage: "plus")
            }
            .accessibilityIdentifier("addAcceptanceDefectButton")
        } header: {
            Text("Mängel (\(defects.filter { !$0.title.trimmingCharacters(in: .whitespaces).isEmpty }.count))")
        } footer: {
            Text("Jeder Mangel wird als Aufgabe mit Frist in der Bauakte angelegt.")
        }
    }

    private func dueDateBinding(_ defect: Binding<AcceptanceDefect>) -> Binding<Date> {
        Binding {
            defect.wrappedValue.dueDate.flatMap { CalendarDay.date($0) } ?? .now
        } set: { date in
            defect.wrappedValue.dueDate = CalendarDay.string(date)
        }
    }

    private func signatureRow(_ side: Side, data: Data?) -> some View {
        Button { signing = side } label: {
            HStack(spacing: Space.m) {
                IconBadge(symbol: data == nil ? "signature" : "checkmark.seal.fill", tint: data == nil ? Theme.accent : Theme.success,
                          background: (data == nil ? Theme.accent : Theme.success).opacity(0.12))
                VStack(alignment: .leading, spacing: 2) {
                    Text(side == .client ? "Auftraggeber" : "Auftragnehmer").font(Typo.body).foregroundStyle(Theme.textPrimary)
                    Text(data == nil ? "Noch nicht unterschrieben" : "Unterschrieben").font(Typo.subhead).foregroundStyle(Theme.textSecondary)
                }
                Spacer()
                if let data, let image = UIImage(data: data) {
                    Image(uiImage: image).resizable().scaledToFit().frame(height: 38)
                        .padding(4)
                        .background(Color.white, in: RoundedRectangle(cornerRadius: Radius.xs))
                } else {
                    Text("Unterschreiben").font(Typo.subheadStrong).foregroundStyle(Theme.accent)
                }
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityIdentifier(side == .client ? "signClientButton" : "signContractorButton")
    }

    private func prefill() {
        guard !prefilled else { return }
        prefilled = true
        clientName = customerName ?? ""
        contractorName = model.active?.user.displayName ?? ""
        location = bauakte.addressLine ?? ""
    }

    private func collectPhotos() async {
        guard let service = model.active?.service else { return }
        let files = (try? await service.files(of: bauakte.id)) ?? []
        let new = files.filter { $0.isPhoto && ($0.capturedAt ?? .distantPast) >= captureStarted.addingTimeInterval(-1) }.map(\.id)
        photoIds = Array(Set(photoIds + new))
    }

    private func save() async {
        guard let service = model.active?.service else { return }
        saving = true
        defer { saving = false }
        let input = NewAcceptance(location: location, clientName: clientName, contractorName: contractorName, scope: scope, result: result,
                                  notes: notes, photoFileIds: photoIds, defects: defects,
                                  clientSignature: clientSignature, contractorSignature: contractorSignature)
        do throws(AcceptanceError) {
            try await service.addAcceptance(input, to: bauakte.id)
            model.active?.sync?.localChangeMade()
            onDone()
        } catch {
            self.error = error.message
        }
    }
}

/// Zeile in der Bauakte: Ergebnis mit Symbol, Stand des Protokolls, Mängel, Datum und Übertragungszustand.
struct AcceptanceRow: View {
    let acceptance: Acceptance

    var body: some View {
        HStack(alignment: .top, spacing: Space.m) {
            IconBadge(symbol: "signature", tint: acceptance.result.tint, background: acceptance.result.soft)
            VStack(alignment: .leading, spacing: 3) {
                HStack(alignment: .firstTextBaseline, spacing: Space.s) {
                    Text("Abnahme").font(Typo.bodyStrong).foregroundStyle(Theme.textPrimary)
                    AcceptanceResultChip(result: acceptance.result)
                    Spacer(minLength: Space.s)
                    SyncStateBadge(state: acceptance.syncState)
                }
                Text(acceptance.syncState == .synced ? acceptance.state.title : "Wird übertragen")
                    .font(Typo.subhead)
                    .foregroundStyle(Theme.textSecondary)
                if !acceptance.defectWorkItemIds.isEmpty {
                    let count = acceptance.defectWorkItemIds.count
                    Label("\(count) \(count == 1 ? "Mangel" : "Mängel") mit Frist", systemImage: "exclamationmark.triangle.fill")
                        .labelStyle(TightLabelStyle())
                        .font(Typo.subheadStrong)
                        .foregroundStyle(Theme.warning)
                }
                if let date = acceptance.performedAt {
                    Text(date.formatted(.dateTime.day().month(.abbreviated).year().hour().minute()))
                        .font(Typo.meta)
                        .foregroundStyle(Theme.textSecondary)
                }
            }
        }
        .padding(.vertical, Space.xs)
        .accessibilityElement(children: .combine)
    }
}

extension AcceptanceResult {
    var tint: Color {
        switch self {
        case .accepted: Theme.success
        case .acceptedWithReservations: Theme.warning
        case .refused: Theme.danger
        }
    }

    var soft: Color {
        switch self {
        case .accepted: Theme.successSoft
        case .acceptedWithReservations: Theme.warningSoft
        case .refused: Theme.dangerSoft
        }
    }
}

struct AcceptanceResultChip: View {
    let result: AcceptanceResult

    var body: some View {
        Chip(text: result.title, tone: result == .accepted ? .success : result == .refused ? .danger : .warning)
    }
}

/// Protokoll ansehen: Angaben, Mängel, Unterschriften, PDF (nach Abschluss) und Freigabelink für den Kunden.
struct AcceptanceDetailView: View {
    let acceptanceId: UUID
    let bauakteId: UUID

    @Environment(AppModel.self) private var model
    @State private var acceptance: Acceptance?
    @State private var defects: [WorkItem] = []
    @State private var pdf: FileItem?
    @State private var previewing: FileItem?
    @State private var email = ""
    @State private var link: AcceptanceLink?
    @State private var linkError: String?
    @State private var requesting = false

    var body: some View {
        List {
            if let acceptance {
                Section {
                    LabeledContent("Ergebnis") { AcceptanceResultChip(result: acceptance.result) }
                    LabeledContent("Stand", value: acceptance.syncState == .synced ? acceptance.state.title : "Noch nicht übertragen")
                        .accessibilityIdentifier("acceptanceState")
                    if let date = acceptance.performedAt {
                        LabeledContent("Durchgeführt", value: date.formatted(date: .abbreviated, time: .shortened))
                    }
                    if let location = acceptance.location { LabeledContent("Ort", value: location) }
                }
                Section("Teilnehmer") {
                    LabeledContent("Auftraggeber", value: acceptance.clientName ?? "–")
                    LabeledContent("Auftragnehmer", value: acceptance.contractorName ?? "–")
                }
                if let scope = acceptance.scope {
                    Section("Abgenommene Leistung") { Text(scope.compactText ?? "") }
                }
                if !defects.isEmpty {
                    Section("Mängel") {
                        ForEach(defects) { item in
                            WorkItemRow(item: item, showsBauakte: nil) {}
                        }
                    }
                }
                if let notes = acceptance.notes {
                    Section("Notizen") { Text(notes.compactText ?? "") }
                }
                Section("Unterschriften") {
                    signature("Auftraggeber", acceptance.hasClientSignature)
                    signature("Auftragnehmer", acceptance.hasContractorSignature)
                    if let name = acceptance.customerDecisionName {
                        LabeledContent("Online freigegeben von", value: name)
                    }
                }
                if let pdf {
                    Section {
                        Button { previewing = pdf } label: { Label("Protokoll (PDF) ansehen", systemImage: "doc.richtext") }
                            .accessibilityIdentifier("acceptancePdfButton")
                    }
                }
                if acceptance.state != .completed, !acceptance.hasClientSignature {
                    customerLinkSection(acceptance)
                }
            } else {
                ProgressView()
            }
        }
        .navigationTitle("Abnahmeprotokoll")
        .navigationBarTitleDisplayMode(.inline)
        .filePreview($previewing)
        .task(id: model.active?.sync?.revision) { await reload() }
    }

    private func signature(_ title: String, _ present: Bool) -> some View {
        LabeledContent(title) {
            Label(present ? "Unterschrieben" : "Fehlt", systemImage: present ? "checkmark.seal.fill" : "minus.circle")
                .foregroundStyle(present ? Theme.success : Theme.textSecondary)
        }
    }

    @ViewBuilder
    private func customerLinkSection(_ acceptance: Acceptance) -> some View {
        Section {
            if acceptance.syncState != .synced {
                Text("Der Freigabelink kann erstellt werden, sobald das Protokoll übertragen ist.").foregroundStyle(Theme.textSecondary)
            } else {
                TextField("E-Mail des Kunden (optional)", text: $email)
                    .keyboardType(.emailAddress).textContentType(.emailAddress).textInputAutocapitalization(.never)
                    .accessibilityIdentifier("customerLinkEmailField")
                Button {
                    Task { await requestLink(acceptance.id) }
                } label: {
                    Label(email.isEmpty ? "Freigabelink erstellen" : "Freigabelink per E-Mail senden", systemImage: "link")
                }
                .disabled(requesting)
                .accessibilityIdentifier("customerLinkButton")
                if let link {
                    VStack(alignment: .leading, spacing: 6) {
                        Text(link.emailed ? "Link an \(email) gesendet." : "Link erstellt – bitte an den Kunden weitergeben.")
                            .font(.subheadline.weight(.semibold))
                        Text(link.url).font(.caption.monospaced()).textSelection(.enabled)
                        Text("Gültig bis \(link.expiresAt.formatted(date: .abbreviated, time: .shortened))")
                            .font(.caption).foregroundStyle(Theme.textSecondary)
                        if let url = URL(string: link.url) {
                            ShareLink(item: url) { Label("Teilen", systemImage: "square.and.arrow.up") }
                        }
                    }
                    .accessibilityIdentifier("customerLinkResult")
                }
                if let linkError {
                    Label(linkError, systemImage: "exclamationmark.triangle").foregroundStyle(Theme.danger)
                }
            }
        } header: {
            Text("Freigabe durch den Kunden")
        } footer: {
            Text("Der Kunde sieht das Protokoll ohne Anmeldung und kann es freigeben oder mit Begründung ablehnen. Der Link ist befristet.")
        }
    }

    private func reload() async {
        guard let service = model.active?.service else { return }
        acceptance = try? await service.acceptance(acceptanceId)
        let items = (try? await service.workItems(of: bauakteId)) ?? []
        defects = items.filter { acceptance?.defectWorkItemIds.contains($0.id) == true }
        if let pdfId = acceptance?.pdfFileId {
            pdf = ((try? await service.files(of: bauakteId)) ?? []).first { $0.id == pdfId }
        }
    }

    private func requestLink(_ id: UUID) async {
        guard let client = model.active?.client else { return }
        requesting = true
        defer { requesting = false }
        do throws(APIError) {
            link = try await client.customerLink(for: id, email: email)
            linkError = nil
            await model.active?.sync?.run()
        } catch {
            linkError = error.message
        }
    }
}
