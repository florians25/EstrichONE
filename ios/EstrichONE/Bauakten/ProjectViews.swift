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
    }

    private func text(_ keyPath: WritableKeyPath<BauakteProject, String?>) -> Binding<String> {
        Binding { project[keyPath: keyPath] ?? "" } set: { project[keyPath: keyPath] = $0.isEmpty ? nil : $0 }
    }

    private func flag(_ keyPath: WritableKeyPath<BauakteProject, Bool?>) -> Binding<Bool> {
        Binding { project[keyPath: keyPath] ?? false } set: { project[keyPath: keyPath] = $0 }
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
    let edit: () -> Void
    @Environment(\.horizontalSizeClass) private var sizeClass

    var body: some View {
        let items = Self.items(bauakte)
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
    let onDone: () -> Void

    @Environment(AppModel.self) private var model
    @State private var project = BauakteProject()
    @State private var error: String?
    @State private var loaded = false

    var body: some View {
        NavigationStack {
            Form {
                if let error {
                    Section { StatusMessage(kind: .error, text: error) }
                }
                ProjectFields(project: $project)
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
                    loaded = true
                }
            }
        }
    }

    private func save() async {
        guard let service = model.active?.service else { return }
        do throws(BauakteError) {
            if project != bauakte.project {
                try await service.updateProject(project, of: bauakte.id)
                model.active?.sync?.localChangeMade()
            }
            onDone()
        } catch {
            self.error = error.message
        }
    }
}
