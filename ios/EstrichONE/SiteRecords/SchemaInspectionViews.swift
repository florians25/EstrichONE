import EstrichCore
import SwiftUI

/// Erstbegehung öffnen: mit dem Schema des Büros (frei konfigurierbar), sonst die bisherigen festen Prüfpunkte.
/// Das Schema kommt aus der lokalen Ablage (offline der zuletzt geladene Stand) – kein Warten auf das Netz.
struct InspectionEntryView: View {
    let bauakte: Bauakte
    var correcting: Inspection?
    let onDone: () -> Void

    @Environment(AppModel.self) private var model
    @State private var schema: FormSchema?
    @State private var loaded = false

    var body: some View {
        Group {
            if !loaded {
                ProgressView().frame(maxWidth: .infinity, maxHeight: .infinity)
            } else if let schema, correcting == nil || correcting?.schemaVersion != nil {
                SchemaInspectionView(bauakte: bauakte, schema: schema, correcting: correcting, onDone: onDone)
            } else {
                InitialInspectionView(bauakte: bauakte, correcting: correcting, onDone: onDone)
            }
        }
        .task {
            schema = await model.active?.service?.formSchema(key: "erstbegehung", using: nil)
            loaded = true
            // Im Hintergrund aktualisieren; ein neues Schema gilt ab der nächsten Erstbegehung.
            if let service = model.active?.service, schema == nil {
                schema = await service.formSchema(key: "erstbegehung", using: model.active?.client)
            }
        }
    }
}

/// Erstbegehung nach Schema: Gruppen und Felder in der Reihenfolge des Büros. Prüfpunkte mit „In Ordnung / Mangel /
/// Entfällt“, Bemerkung und – wo vorgesehen – Fotos je Punkt; Messwerte mit Hinweis, wenn sie in die Basisdaten übernommen
/// werden. Dazu Notizen, weitere Fotos und die Unterschrift des Kunden. Offline gespeichert, gesendet per Synchronisation.
struct SchemaInspectionView: View {
    let bauakte: Bauakte
    let schema: FormSchema
    var correcting: Inspection?
    let onDone: () -> Void

    @Environment(AppModel.self) private var model
    @State private var notRequired = false
    @State private var reason = ""
    @State private var checks: [String: CheckResult] = [:]
    @State private var remarks: [String: String] = [:]
    @State private var values: [String: JSONValue] = [:]
    @State private var checkPhotos: [String: [UUID]] = [:]
    @State private var photos: [UUID] = []
    @State private var notes = ""
    @State private var lists = ValueLists([])
    @State private var capturingFor: String?
    @State private var captureStarted = Date.now
    @State private var signing = false
    @State private var error: String?
    @State private var saving = false
    @State private var prefilled = false

    private static let general = "__general"

    @Environment(\.shotScene) private var shotScene

    private var checkFields: [FormSchema.Field] { schema.fields.filter { $0.type == .check } }

    var body: some View {
        NavigationStack {
            Form {
                if let error {
                    Section { StatusMessage(kind: .error, text: error).accessibilityIdentifier("inspectionError") }
                }
                Section {
                    progressHeader
                        .listRowBackground(Color.clear)
                        .listRowInsets(EdgeInsets(top: Space.s, leading: Space.xs, bottom: Space.s, trailing: Space.xs))
                }
                Section {
                    Toggle("Erstbegehung nicht notwendig", isOn: $notRequired.animation())
                    if notRequired {
                        TextField("Begründung (Pflicht)", text: $reason, axis: .vertical).lineLimit(2...4)
                    }
                }
                if !notRequired {
                    ForEach(schema.groups) { group in
                        let checkFields = group.fields.filter { $0.type == .check }
                        let valueFields = group.fields.filter { $0.type != .check }
                        if !checkFields.isEmpty {
                            Section(group.label) {
                                ForEach(checkFields) { field in checkRow(field) }
                            }
                        }
                        if !valueFields.isEmpty {
                            SchemaProjectFields(schema: Self.valueSchema(group, fields: valueFields, separate: !checkFields.isEmpty),
                                                lists: lists, values: $values)
                        }
                    }
                }
                Section("Notizen") {
                    TextField("Besonderheiten, Absprachen, Maße", text: $notes, axis: .vertical)
                        .lineLimit(3...10)
                        .accessibilityIdentifier("inspectionNotesField")
                }
                Section {
                    Button { capture(Self.general) } label: {
                        Label(photos.isEmpty ? "Weitere Fotos aufnehmen" : "Weitere Fotos (\(photos.count))", systemImage: "camera")
                    }
                    Button { signing = true } label: { Label("Unterschrift des Kunden", systemImage: "signature") }
                        .accessibilityIdentifier("signatureButton")
                } header: {
                    Text("Fotos und Unterschrift")
                }
            }
            .navigationTitle(correcting == nil ? "Erstbegehung" : "Erstbegehung korrigieren")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Abbrechen", action: onDone) }
            }
            .safeAreaInset(edge: .bottom) {
                Button { Task { await save() } } label: {
                    ActionLabel("Erstbegehung sichern", systemImage: "checkmark", isBusy: saving)
                }
                .primaryActionStyle()
                .disabled(saving)
                .accessibilityIdentifier("saveInspectionButton")
                .padding(.horizontal, Space.l)
                .padding(.top, Space.m)
                .padding(.bottom, Space.s)
                .background(.bar)
            }
            .sensoryFeedback(.error, trigger: error) { _, new in new != nil }
            .fullScreenCover(item: Binding(get: { capturingFor.map(CaptureTarget.init) }, set: { capturingFor = $0?.id })) { target in
                CameraView(bauakte: bauakte, allowsChangingBauakte: false, caption: caption(for: target.id)) { _ in
                    capturingFor = nil
                    Task { await collect(for: target.id) }
                }
            }
            .sheet(isPresented: $signing) {
                SignatureView { png in
                    signing = false
                    if let png { Task { await addSignature(png) } }
                }
            }
            .task {
                lists = await model.active?.service?.valueLists() ?? ValueLists([])
                prefill()
                if shotScene == "erstbegehung", checks.isEmpty {
                    // Screenshot-Modus: ein Teil ist schon geprüft, ein Mangel mit Bemerkung.
                    for (index, field) in checkFields.prefix(3).enumerated() {
                        checks[field.key] = index == 2 ? .notOk : .ok
                    }
                    if checkFields.count > 2 { remarks[checkFields[2].key] = "Druckprotokoll fehlt noch – Heizungsbauer angerufen." }
                }
            }
        }
    }

    /// Fortschritt: geprüfte Punkte, festgestellte Mängel.
    private var progressHeader: some View {
        let done = checkFields.filter { checks[$0.key] != nil }.count
        let defects = checkFields.filter { checks[$0.key] == .notOk }.count
        return VStack(alignment: .leading, spacing: Space.s) {
            HStack(alignment: .firstTextBaseline) {
                Text(bauakte.title)
                    .font(Typo.headline)
                    .foregroundStyle(Theme.textPrimary)
                    .lineLimit(1)
                Spacer()
                if defects > 0 {
                    Chip(text: defects == 1 ? "1 Mangel" : "\(defects) Mängel", symbol: "exclamationmark.triangle.fill", tone: .warning)
                }
            }
            ProgressBar(value: checkFields.isEmpty ? 0 : Double(done) / Double(checkFields.count), tint: defects > 0 ? Theme.warning : Theme.accent)
            Text(notRequired ? "Keine Erstbegehung notwendig – bitte begründen." : "\(done) von \(checkFields.count) Prüfpunkten bewertet")
                .font(Typo.meta)
                .foregroundStyle(Theme.textSecondary)
        }
    }

    private struct CaptureTarget: Identifiable {
        let id: String
    }

    private func checkRow(_ field: FormSchema.Field) -> some View {
        let selection = checks[field.key]
        let photoCount = checkPhotos[field.key]?.count ?? 0
        return VStack(alignment: .leading, spacing: Space.m) {
            VStack(alignment: .leading, spacing: 3) {
                Text(field.required ? "\(field.label) *" : field.label)
                    .font(Typo.bodyStrong)
                    .foregroundStyle(Theme.textPrimary)
                    .fixedSize(horizontal: false, vertical: true)
                if let help = field.help {
                    Text(help).font(Typo.meta).foregroundStyle(Theme.textSecondary)
                }
            }
            HStack(spacing: Space.s) {
                ChoiceButton(title: "In Ordnung", symbol: "checkmark.circle.fill", isSelected: selection == .ok, tone: .success) {
                    checks[field.key] = selection == .ok ? nil : .ok
                }
                ChoiceButton(title: "Mangel", symbol: "exclamationmark.triangle.fill", isSelected: selection == .notOk, tone: .warning) {
                    checks[field.key] = selection == .notOk ? nil : .notOk
                }
                ChoiceButton(title: "Entfällt", symbol: "minus.circle.fill", isSelected: selection == .notApplicable, tone: .neutral) {
                    checks[field.key] = selection == .notApplicable ? nil : .notApplicable
                }
            }
            .accessibilityElement(children: .contain)
            .accessibilityIdentifier("check-\(field.key)")
            if selection == .notOk || !(remarks[field.key] ?? "").isEmpty {
                TextField("Was ist mangelhaft?", text: Binding(get: { remarks[field.key] ?? "" }, set: { remarks[field.key] = $0 }), axis: .vertical)
                    .lineLimit(1...4)
                    .padding(Space.m)
                    .background(Theme.fill, in: RoundedRectangle(cornerRadius: Radius.s, style: .continuous))
            }
            if field.photos {
                Button { capture(field.key) } label: {
                    Label(photoCount == 0 ? "Foto zu diesem Punkt" : "\(photoCount) \(photoCount == 1 ? "Foto" : "Fotos") – weitere", systemImage: "camera")
                }
                .estrichButton(.secondary)
            }
        }
        .padding(.vertical, Space.s)
    }

    /// Wertefelder einer Gruppe als eigenes Mini-Schema (flache Werte, Hinweis auf Übernahme in die Basisdaten).
    private static func valueSchema(_ group: FormSchema.Group, fields: [FormSchema.Field], separate: Bool) -> FormSchema {
        let mapped = fields.map { field in
            FormSchema.Field(key: field.key, label: field.label, type: field.type, list: field.list, path: "project.\(field.key)",
                             required: field.required, unit: field.unit,
                             help: [field.help, field.target.map { _ in "wird in die Basisdaten übernommen" }].compactMap { $0 }
                                .joined(separator: " – ").nilIfEmpty)
        }
        return FormSchema(key: "erstbegehung", version: 0, groups: [
            FormSchema.Group(key: group.key, label: separate ? "\(group.label) – Werte" : group.label, fields: mapped),
        ])
    }

    private func caption(for key: String) -> String {
        key == Self.general ? "Erstbegehung" : (schema.fields.first { $0.key == key }?.label ?? "Erstbegehung")
    }

    private func capture(_ key: String) {
        captureStarted = .now
        capturingFor = key
    }

    private func collect(for key: String) async {
        guard let service = model.active?.service else { return }
        let files = (try? await service.files(of: bauakte.id)) ?? []
        let new = files.filter { $0.isPhoto && ($0.capturedAt ?? .distantPast) >= captureStarted.addingTimeInterval(-1) }.map(\.id)
        guard !new.isEmpty else { return }
        if key == Self.general {
            photos = Array(Set(photos + new))
        } else {
            checkPhotos[key] = Array(Set((checkPhotos[key] ?? []) + new))
        }
    }

    private func addSignature(_ png: Data) async {
        guard let service = model.active?.service else { return }
        if let id = try? await service.addPhoto(png, to: bauakte.id, capturedAt: .now, contentType: "image/png",
                                                caption: InspectionProtocol.signatureCaption) {
            photos.append(id)
            model.active?.sync?.localChangeMade()
        }
    }

    /// Korrektur: bisherige Angaben übernehmen.
    private func prefill() {
        guard !prefilled else { return }
        prefilled = true
        guard let correcting else { return }
        notRequired = correcting.notRequired
        reason = correcting.remarks ?? ""
        checks = correcting.checkResults
        remarks = correcting.checkRemarks
        values = correcting.values
        checkPhotos = correcting.checkPhotos
        let perCheck = Set(correcting.checkPhotos.values.flatMap { $0 })
        photos = correcting.photoFileIds.filter { !perCheck.contains($0) }
        notes = correcting.notes ?? ""
    }

    private func save() async {
        guard let service = model.active?.service else { return }
        saving = true
        defer { saving = false }
        do throws(SiteRecordError) {
            try await service.addSchemaInspection(to: bauakte.id, schema: schema, notRequired: notRequired, checks: checks,
                                                  values: values, checkRemarks: remarks, checkPhotos: checkPhotos,
                                                  photoFileIds: photos, remarks: notRequired ? reason : nil, notes: notes,
                                                  corrects: correcting?.id)
            model.active?.sync?.localChangeMade()
            onDone()
        } catch {
            self.error = error.message
        }
    }
}

/// Ergebnis einer Erstbegehung nach Schema in der Protokollansicht: Prüfpunkte mit Ergebnis, Bemerkung und Fotozahl,
/// danach die erfassten Werte – mit den Beschriftungen des Schemas (ohne Schema die gespeicherten Schlüssel).
struct SchemaInspectionResultSections: View {
    let inspection: Inspection
    let schema: FormSchema?
    let lists: ValueLists

    var body: some View {
        let checkFields = schema?.fields.filter { $0.type == .check } ?? []
        let knownChecks = Set(checkFields.map(\.key))
        let extraChecks = inspection.checkResults.keys.filter { !knownChecks.contains($0) }.sorted()
        Section("Prüfpunkte") {
            ForEach(checkFields.filter { inspection.checkResults[$0.key] != nil }) { field in
                row(field.label, key: field.key)
            }
            ForEach(extraChecks, id: \.self) { key in row(key, key: key) }
        }
        let valueFields = schema?.fields.filter { $0.type != .check } ?? []
        if !inspection.values.isEmpty {
            Section("Messwerte und Angaben") {
                ForEach(valueFields.filter { inspection.values[$0.key] != nil }) { field in
                    if let text = FormSchema.display(field, inspection.values[field.key] ?? .null, lists: lists) {
                        LabeledContent(field.label, value: text)
                    }
                }
                let known = Set(valueFields.map(\.key))
                ForEach(inspection.values.keys.filter { !known.contains($0) }.sorted(), id: \.self) { key in
                    LabeledContent(key, value: Self.plain(inspection.values[key] ?? .null))
                }
            }
        }
    }

    private func row(_ label: String, key: String) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack {
                Text(label).font(.body)
                Spacer()
                if let result = inspection.checkResults[key] {
                    Text(result.title)
                        .font(.subheadline.weight(.semibold))
                        .foregroundStyle(result == .notOk ? Theme.danger : result == .ok ? Theme.success : Theme.textSecondary)
                }
            }
            if let remark = inspection.checkRemarks[key]?.compactText {
                Text(remark).font(.subheadline).foregroundStyle(Theme.textSecondary)
            }
            if let count = inspection.checkPhotos[key]?.count, count > 0 {
                Label("\(count) \(count == 1 ? "Foto" : "Fotos")", systemImage: "photo").font(.caption).foregroundStyle(Theme.textSecondary)
            }
        }
    }

    static func plain(_ value: JSONValue) -> String {
        switch value {
        case let .string(text): text
        case let .number(number): number.formatted()
        case let .bool(flag): flag ? "ja" : "nein"
        default: "–"
        }
    }
}

extension String {
    var nilIfEmpty: String? { isEmpty ? nil : self }
}
