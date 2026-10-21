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
                        Label(error, systemImage: "exclamationmark.triangle.fill").foregroundStyle(Theme.error)
                            .accessibilityIdentifier("acceptanceError")
                    }
                }
                Section("Ergebnis") {
                    Picker("Ergebnis", selection: $result) {
                        ForEach(AcceptanceResult.allCases) { Text($0.title).tag(Optional($0)) }
                    }
                    .pickerStyle(.segmented)
                    .accessibilityIdentifier("acceptanceResultPicker")
                    if result == .acceptedWithReservations || result == .refused {
                        Text("Bitte die Mängel bzw. Vorbehalte unten erfassen.").font(.footnote).foregroundStyle(Theme.textSecondary)
                    }
                }
                Section("Teilnehmer") {
                    TextField("Auftraggeber (Name)", text: $clientName)
                        .textContentType(.name)
                        .accessibilityIdentifier("acceptanceClientField")
                    TextField("Auftragnehmer (Name, Firma)", text: $contractorName)
                        .accessibilityIdentifier("acceptanceContractorField")
                    TextField("Ort", text: $location)
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
                        Label(photoIds.isEmpty ? "Fotos aufnehmen" : "\(photoIds.count) Foto(s) – weitere aufnehmen", systemImage: "camera")
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
                ToolbarItem(placement: .confirmationAction) {
                    Button("Sichern") { Task { await save() } }
                        .disabled(saving)
                        .accessibilityIdentifier("saveAcceptanceButton")
                }
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
            HStack {
                Label(side == .client ? "Auftraggeber" : "Auftragnehmer", systemImage: "signature")
                Spacer()
                if let data, let image = UIImage(data: data) {
                    Image(uiImage: image).resizable().scaledToFit().frame(height: 34)
                        .background(Color.white, in: RoundedRectangle(cornerRadius: 4))
                    Image(systemName: "checkmark.circle.fill").foregroundStyle(Theme.success)
                } else {
                    Text("Unterschreiben").foregroundStyle(Theme.actionPrimary)
                }
            }
        }
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

/// Zeile in der Bauakte: Datum, Ergebnis, Stand und Übertragungszustand.
struct AcceptanceRow: View {
    let acceptance: Acceptance

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack {
                Text("Abnahme").font(.subheadline.weight(.semibold))
                AcceptanceResultChip(result: acceptance.result)
                Spacer()
                SyncStateBadge(state: acceptance.syncState)
                if let date = acceptance.performedAt {
                    Text(date.formatted(date: .abbreviated, time: .shortened)).font(.caption).foregroundStyle(Theme.textSecondary)
                }
            }
            Text(acceptance.syncState == .synced ? acceptance.state.title : "Wird übertragen")
                .font(.caption).foregroundStyle(Theme.textSecondary)
            if !acceptance.defectWorkItemIds.isEmpty {
                Label("\(acceptance.defectWorkItemIds.count) Mangel/Mängel mit Frist", systemImage: "exclamationmark.triangle")
                    .font(.caption).foregroundStyle(Theme.hint)
            }
        }
        .accessibilityElement(children: .combine)
    }
}

struct AcceptanceResultChip: View {
    let result: AcceptanceResult

    var body: some View {
        let color: Color = switch result {
        case .accepted: Theme.success
        case .acceptedWithReservations: Theme.hint
        case .refused: Theme.error
        }
        Text(result.title)
            .font(.caption2.weight(.semibold))
            .foregroundStyle(color)
            .padding(.horizontal, 7).padding(.vertical, 2)
            .background(color.opacity(0.14), in: Capsule())
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
                    Section("Abgenommene Leistung") { Text(scope) }
                }
                if !defects.isEmpty {
                    Section("Mängel") {
                        ForEach(defects) { item in
                            WorkItemRow(item: item, showsBauakte: nil) {}
                        }
                    }
                }
                if let notes = acceptance.notes {
                    Section("Notizen") { Text(notes) }
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
                    Label(linkError, systemImage: "exclamationmark.triangle").foregroundStyle(Theme.error)
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
