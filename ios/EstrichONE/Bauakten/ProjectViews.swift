import EstrichCore
import SwiftUI

/// Eingabe der Werte des Bauvorhabens (Anlage und Bearbeiten): Objekt, Fläche, Estrich, Aufbau, Dämmung, Baustelle vor Ort
/// und – bei der Anlage – der geplante Zeitraum. Alles optional; Einheiten stehen am Feld.
struct ProjectFields: View {
    @Binding var project: BauakteProject
    /// Nur bei der Anlage (der Zeitraum wird danach im Büro geplant).
    var plannedStart: Binding<String?>? = nil
    var plannedEnd: Binding<String?>? = nil

    var body: some View {
        Section {
            Picker("Objektart", selection: $project.objectType) {
                Text("–").tag(BauakteProject.ObjectType?.none)
                ForEach(BauakteProject.ObjectType.allCases) { Text($0.title).tag(Optional($0)) }
            }
            .accessibilityIdentifier("projectObjectType")
            UnitField(title: "Fläche", unit: "m²") {
                TextField("Fläche", value: $project.totalArea, format: .number.precision(.fractionLength(0...2)))
                    .keyboardType(.decimalPad)
                    .accessibilityIdentifier("projectAreaField")
            }
            TextField("Geschosse, z. B. KG, EG, OG", text: text(\.floors))
            Picker("Priorität", selection: $project.priority) {
                Text("–").tag(BauakteProject.Priority?.none)
                ForEach(BauakteProject.Priority.allCases) { Text($0.title).tag(Optional($0)) }
            }
        } header: {
            Text("Bauvorhaben")
        }
        Section("Estrich") {
            Picker("Estrichart", selection: $project.screedType) {
                Text("–").tag(BauakteProject.ScreedType?.none)
                ForEach(BauakteProject.ScreedType.allCases) { Text($0.title).tag(Optional($0)) }
            }
            Picker("Aufbau", selection: $project.construction) {
                Text("–").tag(BauakteProject.Construction?.none)
                ForEach(BauakteProject.Construction.allCases) { Text($0.title).tag(Optional($0)) }
            }
            UnitField(title: "Nenndicke", unit: "mm") {
                TextField("Nenndicke", value: $project.nominalThicknessMm, format: .number).keyboardType(.numberPad)
            }
            TextField("Festigkeitsklasse, z. B. C25-F4", text: text(\.strengthClass))
            Toggle("Fußbodenheizung", isOn: flag(\.underfloorHeating))
            if project.underfloorHeating == true {
                TextField("Heizungsart, z. B. Nasssystem", text: text(\.heatingType))
            }
            TextField("Dämmung, z. B. EPS 035", text: text(\.insulation))
            UnitField(title: "Dämmdicke", unit: "mm") {
                TextField("Dämmdicke", value: $project.insulationThicknessMm, format: .number).keyboardType(.numberPad)
            }
        }
        Section("Vor Ort") {
            Toggle("Wasser vorhanden", isOn: flag(\.waterOnSite))
            Toggle("Strom vorhanden", isOn: flag(\.powerOnSite))
            TextField("Zufahrt, z. B. nur bis 7,5 t", text: text(\.siteAccess), axis: .vertical).lineLimit(1...4)
        }
        if let plannedStart, let plannedEnd {
            PlannedPeriodSection(plannedStart: plannedStart, plannedEnd: plannedEnd)
        }
    }

    private func text(_ keyPath: WritableKeyPath<BauakteProject, String?>) -> Binding<String> {
        Binding { project[keyPath: keyPath] ?? "" } set: { project[keyPath: keyPath] = $0.isEmpty ? nil : $0 }
    }

    private func flag(_ keyPath: WritableKeyPath<BauakteProject, Bool?>) -> Binding<Bool> {
        Binding { project[keyPath: keyPath] ?? false } set: { project[keyPath: keyPath] = $0 }
    }
}

/// Geplanter Zeitraum (nur bei der Anlage).
struct PlannedPeriodSection: View {
    let plannedStart: Binding<String?>
    let plannedEnd: Binding<String?>

    var body: some View {
        Section {
            Toggle("Zeitraum planen", isOn: Binding {
                plannedStart.wrappedValue != nil
            } set: { on in
                if on {
                    let start = Calendar.current.date(byAdding: .day, value: 7, to: .now) ?? .now
                    plannedStart.wrappedValue = CalendarDay.string(start)
                    plannedEnd.wrappedValue = CalendarDay.string(Calendar.current.date(byAdding: .day, value: 2, to: start) ?? start)
                } else {
                    plannedStart.wrappedValue = nil
                    plannedEnd.wrappedValue = nil
                }
            })
            if plannedStart.wrappedValue != nil {
                DatePicker("Beginn", selection: day(plannedStart), displayedComponents: .date)
                DatePicker("Ende", selection: day(plannedEnd), in: (plannedStart.wrappedValue.flatMap { CalendarDay.date($0) } ?? .distantPast)...,
                           displayedComponents: .date)
            }
        } header: {
            Text("Geplanter Zeitraum")
        }
    }

    private func day(_ value: Binding<String?>) -> Binding<Date> {
        Binding { value.wrappedValue.flatMap { CalendarDay.date($0) } ?? .now } set: { value.wrappedValue = CalendarDay.string($0) }
    }
}

/// Zahlenfeld mit Einheit rechts.
private struct UnitField<Field: View>: View {
    let title: String
    let unit: String
    @ViewBuilder let field: () -> Field

    var body: some View {
        LabeledContent(title) {
            HStack(spacing: 4) {
                field().multilineTextAlignment(.trailing)
                Text(unit).foregroundStyle(Theme.textSecondary)
            }
        }
    }
}

/// Abschnitt „Basisdaten“ in der Bauakte: alle Werte des Bauvorhabens und der geplante Zeitraum als Beschriftung/Wert-Paare
/// in zwei Spalten (iPad drei), mit Symbolen und Leerzustand.
struct ProjectSection: View {
    let bauakte: Bauakte
    var schema: FormSchema?
    var lists = ValueLists([])
    let edit: () -> Void
    @Environment(\.horizontalSizeClass) private var sizeClass

    var body: some View {
        let items = schema.map { Self.items(bauakte, schema: $0, lists: lists) } ?? Self.items(bauakte)
        Section {
            if items.isEmpty {
                Button(action: edit) {
                    Label("Fläche, Estrich und Aufbau erfassen", systemImage: "square.and.pencil")
                }
            } else {
                LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 12, alignment: .topLeading), count: sizeClass == .regular ? 3 : 2),
                          alignment: .leading, spacing: 12) {
                    ForEach(items, id: \.title) { item in
                        InfoCell(title: item.title, value: item.value, symbol: item.symbol)
                    }
                }
                .padding(.vertical, 4)
            }
        } header: {
            SectionHeader("Basisdaten") {
                Button(action: edit) { Label("Bearbeiten", systemImage: "pencil") }
                    .font(.subheadline.weight(.semibold))
                    .accessibilityIdentifier("editProjectButton")
            }
        }
        .accessibilityIdentifier("projectSection")
    }

    /// Werte nach dem Schema des Büros (eigene Felder eingeschlossen), Zeitraum zuerst.
    static func items(_ bauakte: Bauakte, schema: FormSchema, lists: ValueLists) -> [(title: String, value: String, symbol: String)] {
        let period: [(title: String, value: String, symbol: String)] = BauakteDetailView.period(bauakte).map { [(title: "Geplant", value: $0, symbol: "calendar")] } ?? []
        return period + schema.fields.compactMap { field in
            FormSchema.display(field, FormSchema.value(field, in: bauakte.projectData), lists: lists)
                .map { (title: field.label, value: $0, symbol: field.builtinSymbol) }
        }
    }

    static func items(_ bauakte: Bauakte) -> [(title: String, value: String, symbol: String)] {
        let project = bauakte.project
        func yesNo(_ value: Bool?) -> String? { value.map { $0 ? "ja" : "nein" } }
        let rows: [(String, String?, String)] = [
            ("Geplant", BauakteDetailView.period(bauakte), "calendar"),
            ("Objektart", project.objectType?.title, "house"),
            ("Fläche", project.totalArea.map { "\($0.formatted(.number.precision(.fractionLength(0...2)))) m²" }, "square.dashed"),
            ("Geschosse", project.floors, "building.2"),
            ("Estrichart", project.screedType?.title, "square.stack.3d.up"),
            ("Aufbau", project.construction?.title, "square.3.layers.3d"),
            ("Nenndicke", project.nominalThicknessMm.map { "\($0) mm" }, "ruler"),
            ("Festigkeit", project.strengthClass, "scalemass"),
            ("Fußbodenheizung", project.underfloorHeating.map { $0 ? ["ja", project.heatingType].compactMap { $0 }.joined(separator: ", ") : "nein" }, "flame"),
            ("Dämmung", [project.insulation, project.insulationThicknessMm.map { "\($0) mm" }].compactMap { $0 }.joined(separator: ", "), "square.split.1x2"),
            ("Wasser", yesNo(project.waterOnSite), "drop"),
            ("Strom", yesNo(project.powerOnSite), "bolt"),
            ("Priorität", project.priority?.title, "flag"),
            ("Zufahrt", project.siteAccess, "car"),
        ]
        return rows.compactMap { title, value, symbol in
            guard let value, !value.isEmpty else { return nil }
            return (title: title, value: value, symbol: symbol)
        }
    }
}

/// Beschriftung über Wert, mit kleinem Symbol – für Karten mit mehreren Spalten.
struct InfoCell: View {
    let title: String
    let value: String
    var symbol: String?

    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            HStack(spacing: 4) {
                if let symbol { Image(systemName: symbol).imageScale(.small) }
                Text(title)
            }
            .font(.caption.weight(.medium))
            .foregroundStyle(Theme.textSecondary)
            Text(value)
                .font(.subheadline)
                .foregroundStyle(Color.primary)
                .multilineTextAlignment(.leading)
                .fixedSize(horizontal: false, vertical: true)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .accessibilityElement(children: .combine)
    }
}

/// Werte des Bauvorhabens bearbeiten (offline, `bauakte.update` mit `project`).
struct EditProjectView: View {
    let bauakte: Bauakte
    var schema: FormSchema?
    var lists = ValueLists([])
    let onDone: () -> Void

    @Environment(AppModel.self) private var model
    @State private var project = BauakteProject()
    @State private var values: [String: JSONValue] = [:]
    @State private var error: String?
    @State private var loaded = false

    var body: some View {
        NavigationStack {
            Form {
                if let error {
                    Section { StatusMessage(kind: .error, text: error) }
                }
                if let schema {
                    SchemaProjectFields(schema: schema, lists: lists, values: $values)
                } else {
                    ProjectFields(project: $project)
                }
            }
            .navigationTitle("Basisdaten")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Abbrechen", action: onDone) }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Sichern") { Task { await save() } }.accessibilityIdentifier("saveProjectButton")
                }
            }
            .onAppear {
                if !loaded {
                    project = bauakte.project
                    values = bauakte.projectData
                    loaded = true
                }
            }
        }
    }

    private func save() async {
        guard let service = model.active?.service else { return }
        do throws(BauakteError) {
            if let schema {
                let missing = schema.missingRequired(in: values)
                guard missing.isEmpty else {
                    error = "Bitte noch angeben: \(missing.joined(separator: ", "))."
                    return
                }
                if values != bauakte.projectData {
                    try await service.updateProject(json: values, of: bauakte.id)
                    model.active?.sync?.localChangeMade()
                }
            } else if project != bauakte.project {
                try await service.updateProject(project, of: bauakte.id)
                model.active?.sync?.localChangeMade()
            }
            onDone()
        } catch {
            self.error = error.message
        }
    }
}


/// Basisdaten nach dem Formular-Schema des Büros: je Gruppe ein Abschnitt, Felder in der Reihenfolge des Schemas,
/// Auswahl aus den Wertelisten (inaktive nur, wenn schon gesetzt), Pflichtfelder markiert.
struct SchemaProjectFields: View {
    let schema: FormSchema
    let lists: ValueLists
    @Binding var values: [String: JSONValue]

    var body: some View {
        ForEach(schema.groups) { group in
            Section {
                ForEach(group.fields) { field in
                    fieldView(field)
                }
            } header: {
                Text(group.label)
            } footer: {
                let help = group.fields.compactMap { field in field.help.map { "\(field.label): \($0)" } }
                if !help.isEmpty { Text(help.joined(separator: "\n")) }
            }
        }
    }

    private func title(_ field: FormSchema.Field) -> String {
        field.required ? "\(field.label) *" : field.label
    }

    @ViewBuilder
    private func fieldView(_ field: FormSchema.Field) -> some View {
        let current = FormSchema.value(field, in: values)
        switch field.type {
        case .text:
            TextField(title(field), text: text(field)).accessibilityIdentifier("schemaField-\(field.key)")
        case .textarea:
            TextField(title(field), text: text(field), axis: .vertical).lineLimit(2...6)
        case .number, .integer:
            LabeledContent(title(field)) {
                HStack(spacing: 4) {
                    TextField("–", text: number(field))
                        .keyboardType(field.type == .integer ? .numberPad : .decimalPad)
                        .multilineTextAlignment(.trailing)
                        .accessibilityIdentifier("schemaField-\(field.key)")
                    if let unit = field.unit { Text(unit).foregroundStyle(Theme.textSecondary) }
                }
            }
        case .select:
            Picker(title(field), selection: Binding<String> {
                current.string ?? ""
            } set: { values = FormSchema.setting($0.isEmpty ? .null : .string($0), for: field, in: values) }) {
                Text("–").tag("")
                ForEach(lists.options(field.list ?? "", keeping: current.string)) { item in
                    Text(item.isActive ? item.label : "\(item.label) (nicht mehr verwendet)").tag(item.key)
                }
            }
        case .boolean:
            Toggle(title(field), isOn: Binding { current.bool ?? false } set: { values = FormSchema.setting(.bool($0), for: field, in: values) })
        case .date:
            let date = current.string.flatMap { CalendarDay.date($0) }
            Toggle(title(field), isOn: Binding { date != nil } set: { on in
                values = FormSchema.setting(on ? .string(CalendarDay.string(.now)) : .null, for: field, in: values)
            })
            if let date {
                DatePicker(field.label, selection: Binding { date } set: {
                    values = FormSchema.setting(.string(CalendarDay.string($0)), for: field, in: values)
                }, displayedComponents: .date)
            }
        }
    }

    private func text(_ field: FormSchema.Field) -> Binding<String> {
        Binding {
            FormSchema.value(field, in: values).string ?? ""
        } set: {
            values = FormSchema.setting($0.isEmpty ? .null : .string($0), for: field, in: values)
        }
    }

    /// Zahl als Text (deutsches Komma erlaubt); leer = keine Angabe.
    private func number(_ field: FormSchema.Field) -> Binding<String> {
        Binding {
            guard let number = FormSchema.value(field, in: values).number else { return "" }
            return field.type == .integer ? String(Int(number)) : number.formatted(.number.grouping(.never).precision(.fractionLength(0...2)))
        } set: { text in
            let cleaned = text.replacingOccurrences(of: ",", with: ".").trimmingCharacters(in: .whitespaces)
            if cleaned.isEmpty {
                values = FormSchema.setting(.null, for: field, in: values)
            } else if let number = Double(cleaned) {
                values = FormSchema.setting(.number(field.type == .integer ? number.rounded() : number), for: field, in: values)
            }
        }
    }
}

extension FormSchema.Field {
    /// Symbol für eingebaute Felder; eigene Felder ohne.
    var builtinSymbol: String {
        switch key {
        case "objectType": "house"
        case "totalArea": "square.dashed"
        case "floors": "building.2"
        case "screedType": "square.stack.3d.up"
        case "construction": "square.3.layers.3d"
        case "nominalThicknessMm": "ruler"
        case "strengthClass": "scalemass"
        case "underfloorHeating", "heatingType": "flame"
        case "insulation", "insulationThicknessMm": "square.split.1x2"
        case "waterOnSite": "drop"
        case "powerOnSite": "bolt"
        case "priority": "flag"
        case "siteAccess": "car"
        default: "list.bullet"
        }
    }
}
