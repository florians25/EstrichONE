import EstrichCore
import SwiftUI

/// CM-Messung: Ort, Estrich, Messwert. Die Belegreife steht sofort da – Farbe, Symbol und Text gemeinsam.
struct NewCmMeasurementView: View {
    let bauakteId: UUID
    let onDone: () -> Void

    @Environment(AppModel.self) private var model
    @State private var location = ""
    @State private var binder: ScreedBinderKind = .cement
    @State private var heated = false
    @State private var thickness = 65
    @State private var measuredText = ""
    @State private var remarks = ""
    @State private var error: String?

    private var measured: Double? {
        Double(measuredText.replacingOccurrences(of: ",", with: ".").trimmingCharacters(in: .whitespaces))
    }

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    TextField("Messort, z. B. Bad OG", text: $location)
                        .accessibilityIdentifier("cmLocationField")
                    Picker("Estrich", selection: $binder) {
                        ForEach(ScreedBinderKind.allCases) { Text($0.title).tag($0) }
                    }
                    Toggle("Beheizt (Fußbodenheizung)", isOn: $heated)
                    Stepper("Dicke: \(thickness) mm", value: $thickness, in: 20...150, step: 5)
                }
                Section {
                    HStack {
                        TextField("Messwert", text: $measuredText)
                            .keyboardType(.decimalPad)
                            .font(.title2.monospacedDigit())
                            .accessibilityIdentifier("cmValueField")
                        Text("CM-%").foregroundStyle(Theme.textSecondary)
                    }
                    let limit = binder.limit(heated: heated)
                    LabeledContent("Grenzwert", value: "\(limit.formatted(.number.precision(.fractionLength(1)))) CM-%")
                    if let measured {
                        ReadinessLabel(isReady: measured <= limit)
                    }
                } footer: {
                    Text("Grenzwerte nach den gängigen Merkblättern. Vorgaben des Bodenbelagsherstellers können strenger sein.")
                }
                Section("Bemerkung (optional)") {
                    TextField("z. B. Messung nach 28 Tagen", text: $remarks, axis: .vertical)
                }
                if let error {
                    Text(error).foregroundStyle(Theme.error)
                }
            }
            .navigationTitle("CM-Messung")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Abbrechen", action: onDone) }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Sichern") { Task { await save() } }
                        .disabled(measured == nil || location.trimmingCharacters(in: .whitespaces).isEmpty)
                        .accessibilityIdentifier("saveCmButton")
                }
            }
        }
    }

    private func save() async {
        guard let service = model.active?.service, let measured else { return }
        do throws(SiteRecordError) {
            try await service.addCmMeasurement(to: bauakteId, location: location, binder: binder, heated: heated, thicknessMm: thickness,
                                               measuredCmPercent: measured, remarks: remarks)
            model.active?.sync?.localChangeMade()
            onDone()
        } catch {
            self.error = error.message
        }
    }
}

struct ReadinessLabel: View {
    let isReady: Bool

    var body: some View {
        Label(isReady ? "Belegreif" : "Noch nicht belegreif", systemImage: isReady ? "checkmark.seal.fill" : "hourglass")
            .font(.subheadline.weight(.semibold))
            .foregroundStyle(isReady ? Theme.success : Theme.hint)
            // Ein Element mit dem Text vorlesen, nicht den Symbolnamen („Überprüft“).
            .accessibilityElement(children: .ignore)
            .accessibilityLabel(isReady ? "Belegreif" : "Noch nicht belegreif")
            .accessibilityIdentifier("readinessLabel")
    }
}

/// Erstbegehung: standardmäßig als Assistent (ein Prüfpunkt je Schritt, große Antworten, weiter nach dem Antippen),
/// umschaltbar auf die kurze Liste. Je Punkt „In Ordnung / Mangel / Entfällt“, Fotos (z. B. Zählerstand), Bemerkung.
/// „Keine Erstbegehung notwendig“ verlangt eine Begründung.
struct InitialInspectionView: View {
    let bauakte: Bauakte
    /// Korrektur einer vorhandenen Erstbegehung: Werte werden übernommen, gespeichert wird eine neue Fassung.
    var correcting: Inspection?
    let onDone: () -> Void

    @Environment(AppModel.self) private var model
    @State private var notRequired = false
    @State private var checks: [InitialCheck: CheckResult] = [:]
    @State private var remarks = ""
    /// Freie Notizen (mehrzeilig, beliebig lang) – Besonderheiten, Absprachen, Maße.
    @State private var notes = ""
    @State private var photoIds: [UUID] = []
    @State private var capturing = false
    /// Beschriftung der nächsten Fotos (z. B. der Prüfpunkt, zu dem sie gehören).
    @State private var captureCaption = ""
    @State private var signing = false
    @State private var captureStarted = Date.now
    @State private var error: String?
    @SwiftUI.AppStorage("inspectionMode") private var mode: NewBauakteView.Mode = .wizard
    /// Schritt im Assistenten: 0 = Beginn, 1…n = Prüfpunkte, n+1 = Fotos und Bemerkung.
    @State private var step = 0

    var body: some View {
        NavigationStack {
            Group {
                if mode == .wizard {
                    wizard
                } else {
                    form
                }
            }
            .safeAreaInset(edge: .top) {
                Picker("Darstellung", selection: $mode) {
                    ForEach(NewBauakteView.Mode.allCases) { Text($0.title).tag($0) }
                }
                .pickerStyle(.segmented)
                .padding(.horizontal)
                .padding(.vertical, 6)
                .background(.bar)
                .accessibilityIdentifier("inspectionModePicker")
            }
            .navigationTitle(correcting == nil ? "Erstbegehung" : "Erstbegehung korrigieren")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Abbrechen", action: onDone) }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Sichern") { Task { await save() } }
                        .accessibilityIdentifier("saveInspectionButton")
                }
            }
            .fullScreenCover(isPresented: $capturing) {
                CameraView(bauakte: bauakte, allowsChangingBauakte: false, caption: captureCaption) { _ in
                    capturing = false
                    Task { await collectPhotos() }
                }
            }
            .sensoryFeedback(.selection, trigger: step)
            .sheet(isPresented: $signing) {
                SignatureView { png in
                    signing = false
                    if let png { Task { await addSignature(png) } }
                }
            }
            .onAppear(perform: prefill)
        }
    }

    /// Bei einer Korrektur die bisherigen Angaben übernehmen.
    private func prefill() {
        guard let correcting, checks.isEmpty, remarks.isEmpty else { return }
        notRequired = correcting.notRequired
        checks = correcting.checks
        remarks = correcting.remarks ?? ""
        notes = correcting.notes ?? ""
        photoIds = correcting.photoFileIds
    }

    private func addSignature(_ png: Data) async {
        guard let service = model.active?.service else { return }
        if let id = try? await service.addPhoto(png, to: bauakte.id, capturedAt: .now, contentType: "image/png",
                                                caption: InspectionProtocol.signatureCaption) {
            photoIds.append(id)
            signatureIds.insert(id)
            photoCaptions[id] = InspectionProtocol.signatureCaption
            model.active?.sync?.localChangeMade()
        }
    }

    private var lastStep: Int { InitialCheck.allCases.count + 1 }

    /// Ein Prüfpunkt je Bildschirm; Antwort antippen führt weiter.
    private var wizard: some View {
        VStack(spacing: 0) {
            WizardProgress(steps: ["Beginn", "Prüfpunkte", "Abschluss"],
                           current: step == 0 ? 0 : (step == lastStep || notRequired ? 2 : 1))
                .padding(.horizontal)
                .padding(.vertical, 10)
            Form {
                if step == 0 {
                    Section {
                        Button {
                            notRequired = false
                            withAnimation { step = 1 }
                        } label: {
                            Label("Erstbegehung durchführen", systemImage: "checklist")
                        }
                        .estrichButton(.primary, size: .regular, fullWidth: true)
                        .listRowBackground(Color.clear)
                        .accessibilityIdentifier("startInspectionButton")
                        Button {
                            notRequired = true
                            withAnimation { step = lastStep }
                        } label: {
                            Label("Keine Erstbegehung notwendig", systemImage: "xmark.circle")
                        }
                        .estrichButton(.secondary, size: .regular, fullWidth: true)
                        .listRowBackground(Color.clear)
                        .accessibilityIdentifier("notRequiredButton")
                    } footer: {
                        Text("Geprüft werden \(InitialCheck.allCases.count) Punkte, danach Fotos und Bemerkung.")
                    }
                } else if step < lastStep, !notRequired {
                    let check = InitialCheck.allCases[step - 1]
                    Section {
                        ForEach(CheckResult.allCases) { result in
                            Button {
                                checks[check] = result
                                withAnimation { step += 1 }
                            } label: {
                                HStack {
                                    Label(result.title, systemImage: symbol(for: result))
                                    Spacer()
                                    if checks[check] == result { Image(systemName: "checkmark") }
                                }
                            }
                            .estrichButton(result == .notOk ? .destructive : .secondary, size: .regular, fullWidth: true)
                            .listRowBackground(Color.clear)
                            .accessibilityIdentifier("answer-\(check.rawValue)-\(result.rawValue)")
                        }
                    } header: {
                        Text("Punkt \(step) von \(InitialCheck.allCases.count)")
                    } footer: {
                        Text(check.title).font(.title3.weight(.semibold)).foregroundStyle(.primary).padding(.top, 4)
                    }
                    Section {
                        Button {
                            captureCaption = check.title
                            captureStarted = .now
                            capturing = true
                        } label: {
                            let count = photoCount(for: check)
                            Label(count == 0 ? "Foto zu diesem Punkt" : "\(count) Fotos zu diesem Punkt – weitere", systemImage: "camera")
                        }
                        .accessibilityIdentifier("checkPhotoButton")
                    }
                } else {
                    finishSections
                }
                if let error {
                    Section { StatusMessage(kind: .error, text: error).accessibilityIdentifier("inspectionError") }
                }
            }
            if step > 0 {
                HStack(spacing: 10) {
                    Button {
                        withAnimation { step = notRequired ? 0 : max(0, step - 1) }
                    } label: {
                        Label("Zurück", systemImage: "chevron.left")
                    }
                    .estrichButton(.secondary, size: .regular)
                    if step < lastStep {
                        Button { withAnimation { step += 1 } } label: { Label("Überspringen", systemImage: "chevron.right") }
                            .estrichButton(.tertiary, size: .regular, fullWidth: true)
                    } else {
                        Button { Task { await save() } } label: { Label("Erstbegehung sichern", systemImage: "checkmark") }
                            .estrichButton(.primary, size: .regular, fullWidth: true)
                            .accessibilityIdentifier("finishInspectionButton")
                    }
                }
                .padding(.horizontal, 16)
                .padding(.vertical, 8)
                .background(.bar)
            }
        }
    }

    /// Abschluss im Assistenten: Übersicht, Fotos, Bemerkung bzw. Begründung.
    @ViewBuilder
    private var finishSections: some View {
        // Notizen zuerst: vor Ort das Wichtigste, nicht unter Übersicht und Fotos versteckt.
        if !notRequired { notesSection }
        if !notRequired {
            Section("Ergebnis") {
                ForEach(InitialCheck.allCases) { check in
                    LabeledContent(check.title, value: checks[check]?.title ?? "–")
                        .font(.subheadline)
                }
            }
            Section {
                Button {
                    captureCaption = ""
                    captureStarted = .now
                    capturing = true
                } label: {
                    Label(photoIds.isEmpty ? "Fotos aufnehmen (z. B. Zählerstand)" : "\(photoIds.count) Fotos – weitere aufnehmen",
                          systemImage: "camera")
                }
                Button {
                    signing = true
                } label: {
                    Label(hasSignature ? "Unterschrift des Kunden erneut erfassen" : "Unterschrift des Kunden", systemImage: "signature")
                }
                .accessibilityIdentifier("signatureButton")
            } header: {
                Text("Fotos und Unterschrift")
            }
        }
        if notRequired { notesSection }
        Section {
            TextField(notRequired ? "Warum ist keine Erstbegehung notwendig?" : "Kurz zusammengefasst, z. B. Randdämmung im Bad fehlt",
                      text: $remarks, axis: .vertical)
                .lineLimit(2...5)
                .accessibilityIdentifier("inspectionRemarksField")
        } header: {
            Text(notRequired ? "Begründung *" : "Bemerkung (kurz, optional)")
        }
    }

    /// Freie Notizen – auf dem iPad großzügig, auf dem iPhone mitwachsend.
    private var notesSection: some View {
        Section {
            TextField("Freie Notizen: Besonderheiten, Absprachen mit dem Kunden, Maße, Zugang …", text: $notes, axis: .vertical)
                .lineLimit(5...30)
                .accessibilityIdentifier("inspectionNotesField")
        } header: {
            Text("Notizen")
        } footer: {
            Text("Mehrzeilig und ohne Längenbegrenzung; erscheinen im Protokoll und im PDF.")
        }
    }

    private var hasSignature: Bool {
        !signatureIds.isEmpty
    }

    @State private var signatureIds: Set<UUID> = []
    @State private var photoCaptions: [UUID: String] = [:]

    private func photoCount(for check: InitialCheck) -> Int {
        photoIds.filter { photoCaptions[$0] == check.title }.count
    }

    private func symbol(for result: CheckResult) -> String {
        switch result {
        case .ok: "checkmark.circle.fill"
        case .notOk: "exclamationmark.triangle.fill"
        case .notApplicable: "minus.circle"
        }
    }

    private var form: some View {
            Form {
                Toggle("Keine Erstbegehung notwendig", isOn: $notRequired.animation())
                    .accessibilityIdentifier("notRequiredToggle")
                if !notRequired {
                    Section("Prüfpunkte") {
                        ForEach(InitialCheck.allCases) { check in
                            VStack(alignment: .leading, spacing: 6) {
                                Text(check.title)
                                Picker(check.title, selection: Binding(get: { checks[check] }, set: { checks[check] = $0 })) {
                                    ForEach(CheckResult.allCases) { Text($0.title).tag(Optional($0)) }
                                }
                                .pickerStyle(.segmented)
                                .labelsHidden()
                                .accessibilityIdentifier("check-\(check.rawValue)")
                            }
                            .padding(.vertical, 2)
                        }
                    }
                    Section {
                        Button {
                            captureStarted = .now
                            capturing = true
                        } label: {
                            Label(photoIds.isEmpty ? "Fotos aufnehmen (z. B. Zählerstand)" : "\(photoIds.count) Fotos – weitere aufnehmen",
                                  systemImage: "camera")
                        }
                    }
                }
                notesSection
                Section(notRequired ? "Begründung" : "Bemerkung (kurz, optional)") {
                    TextField(notRequired ? "Warum ist keine Erstbegehung notwendig?" : "z. B. Randdämmung im Bad fehlt", text: $remarks, axis: .vertical)
                        .lineLimit(2...6)
                        .accessibilityIdentifier("inspectionRemarksField")
                }
                if let error {
                    Text(error).foregroundStyle(Theme.error).accessibilityIdentifier("inspectionError")
                }
            }
    }

    /// Fotos, die seit dem Öffnen der Kamera zu dieser Bauakte gespeichert wurden, gehören zur Erstbegehung.
    private func collectPhotos() async {
        guard let service = model.active?.service else { return }
        let files = (try? await service.files(of: bauakte.id)) ?? []
        let new = files.filter { $0.isPhoto && ($0.capturedAt ?? .distantPast) >= captureStarted.addingTimeInterval(-1) }.map(\.id)
        photoIds = Array(Set(photoIds + new))
        for file in files where photoIds.contains(file.id) {
            photoCaptions[file.id] = file.caption
            if file.caption == InspectionProtocol.signatureCaption { signatureIds.insert(file.id) }
        }
    }

    private func save() async {
        guard let service = model.active?.service else { return }
        do throws(SiteRecordError) {
            try await service.addInitialInspection(to: bauakte.id, notRequired: notRequired, checks: checks, photoFileIds: photoIds,
                                                   remarks: remarks, notes: notes, corrects: correcting?.id)
            model.active?.sync?.localChangeMade()
            onDone()
        } catch {
            self.error = error.message
        }
    }
}

struct InspectionRow: View {
    let inspection: Inspection
    var performerName: String?

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack {
                Text(inspection.kind.title).font(.subheadline.weight(.semibold))
                if inspection.correctsInspectionId != nil {
                    Text("Korrektur").font(.caption).foregroundStyle(Theme.textSecondary)
                }
                Spacer()
                if let date = inspection.performedAt {
                    Text(date.formatted(date: .abbreviated, time: .shortened)).font(.caption).foregroundStyle(Theme.textSecondary)
                }
            }
            switch inspection.kind {
            case .cmMeasurement:
                if let measured = inspection.measuredCmPercent, let limit = inspection.limitCmPercent {
                    Text("\(inspection.location ?? "–"): \(measured.formatted(.number.precision(.fractionLength(1...2)))) CM-% (Grenzwert \(limit.formatted(.number.precision(.fractionLength(1)))))")
                        .monospacedDigit()
                }
                if let ready = inspection.isReadyForCovering {
                    ReadinessLabel(isReady: ready)
                }
            case .initialInspection:
                if inspection.notRequired {
                    Text("Nicht notwendig").foregroundStyle(Theme.textSecondary)
                } else if inspection.hasDefects {
                    Label("\(inspection.checks.values.filter { $0 == .notOk }.count) Mangel/Mängel festgestellt", systemImage: "exclamationmark.triangle")
                        .foregroundStyle(Theme.hint)
                } else {
                    Label("Alle geprüften Punkte in Ordnung", systemImage: "checkmark.circle").foregroundStyle(Theme.success)
                }
                if !inspection.photoFileIds.isEmpty {
                    Text("\(inspection.photoFileIds.count) Fotos").font(.caption).foregroundStyle(Theme.textSecondary)
                }
            }
            if let remarks = inspection.remarks {
                Text(remarks).font(.callout).foregroundStyle(Theme.textSecondary)
            }
            if let performerName {
                Text(performerName).font(.caption).foregroundStyle(Theme.textSecondary)
            }
            SyncStateBadge(state: inspection.syncState)
        }
        .padding(.vertical, 2)
    }
}
