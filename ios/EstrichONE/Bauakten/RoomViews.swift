import EstrichCore
import SwiftUI

/// Raum anlegen oder ändern: Name, Geschoss, Fläche, Estrichdicke, Dämmung, Fußbodenheizung, Stand, Notiz.
/// Offline gespeichert und per Synchronisation übertragen (`room.create`/`room.update`).
struct RoomEditView: View {
    let bauakteId: UUID
    /// `nil` = neuer Raum.
    let room: Room?
    /// Vorgaben für einen neuen Raum (aus den Basisdaten der Bauakte).
    var defaults = RoomInput()
    /// Geschosse zur Auswahl mit einem Tipp.
    var floors = ["KG", "EG", "OG", "DG"]
    let onDone: (String?) -> Void

    @Environment(AppModel.self) private var model
    @State private var input = RoomInput()
    @State private var loaded = false
    @State private var error: String?
    @State private var saving = false
    @State private var confirmDelete = false
    @FocusState private var nameFocused: Bool

    var body: some View {
        NavigationStack {
            Form {
                if let error {
                    Section { StatusMessage(kind: .error, text: error) }
                }
                Section {
                    TextField("z. B. Bad, Küche, Flur", text: $input.name)
                        .font(Typo.bodyStrong)
                        .focused($nameFocused)
                        .accessibilityIdentifier("roomNameField")
                    VStack(alignment: .leading, spacing: Space.s) {
                        TextField("Geschoss", text: $input.floor)
                        ChipFlow(spacing: Space.s) {
                            ForEach(floors, id: \.self) { floor in
                                Button { input.floor = floor } label: {
                                    SelectableChipLabel(title: floor, isSelected: input.floor == floor)
                                }
                                .buttonStyle(.plain)
                                .accessibilityAddTraits(input.floor == floor ? .isSelected : [])
                            }
                        }
                    }
                } header: {
                    Text("Raum")
                }
                Section {
                    numberRow("Fläche", unit: "m²", value: Binding { input.areaSquareMeters } set: { input.areaSquareMeters = $0 }, decimals: true)
                    numberRow("Estrichdicke", unit: "mm", value: Binding { input.thicknessMillimeters.map(Double.init) } set: { input.thicknessMillimeters = $0.map { Int($0) } })
                    numberRow("Dämmung", unit: "mm", value: Binding { input.insulationMillimeters.map(Double.init) } set: { input.insulationMillimeters = $0.map { Int($0) } })
                    Toggle("Fußbodenheizung", isOn: $input.underfloorHeating)
                } header: {
                    Text("Fläche und Aufbau")
                }
                Section {
                    HStack(spacing: Space.s) {
                        ForEach(RoomStatus.allCases) { status in
                            ChoiceButton(title: status.title, symbol: status.symbol, isSelected: input.status == status,
                                         tone: status == .ready ? .success : status == .laid ? .accent : .neutral) { input.status = status }
                        }
                    }
                    .listRowInsets(EdgeInsets(top: Space.s, leading: Space.s, bottom: Space.s, trailing: Space.s))
                } header: {
                    Text("Stand")
                }
                Section("Notiz") {
                    TextField("z. B. Fliesen folgen, Bewegungsfuge an der Tür", text: $input.notes, axis: .vertical)
                        .lineLimit(2...5)
                }
                if room != nil {
                    Section {
                        Button("Raum löschen", role: .destructive) { confirmDelete = true }
                    }
                }
            }
            .navigationTitle(room == nil ? "Neuer Raum" : "Raum")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Abbrechen") { onDone(nil) } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Sichern") { Task { await save() } }
                        .disabled(saving || input.name.trimmingCharacters(in: .whitespaces).isEmpty)
                        .accessibilityIdentifier("saveRoomButton")
                }
            }
            .confirmationDialog("Raum löschen?", isPresented: $confirmDelete, titleVisibility: .visible) {
                Button("Löschen", role: .destructive) { Task { await delete() } }
            } message: {
                Text("Der Raum wird aus der Bauakte entfernt – auch im Büro.")
            }
            .sensoryFeedback(.error, trigger: error) { _, new in new != nil }
            .onAppear {
                guard !loaded else { return }
                loaded = true
                if let room {
                    input = RoomInput(room)
                } else {
                    input = defaults
                    nameFocused = true
                }
            }
        }
        .presentationDetents([.large])
    }

    private func numberRow(_ title: String, unit: String, value: Binding<Double?>, decimals: Bool = false) -> some View {
        let digits = decimals ? 2 : 0
        return LabeledContent(title) {
            HStack(spacing: Space.xs) {
                TextField("–", value: value, format: .number.precision(.fractionLength(0...digits)))
                    .keyboardType(decimals ? .decimalPad : .numberPad)
                    .multilineTextAlignment(.trailing)
                    .font(Typo.bodyStrong.monospacedDigit())
                Text(unit).foregroundStyle(Theme.textSecondary)
            }
        }
    }

    private func save() async {
        guard let service = model.active?.service else { return }
        saving = true
        defer { saving = false }
        do throws(RoomError) {
            if let room {
                try await service.updateRoom(room, to: input)
            } else {
                try await service.addRoom(input, to: bauakteId)
            }
            model.active?.sync?.localChangeMade()
            onDone(room == nil ? "Raum angelegt" : "Raum gesichert")
        } catch {
            self.error = error.message
        }
    }

    private func delete() async {
        guard let room, let service = model.active?.service else { return }
        do throws(RoomError) {
            try await service.deleteRoom(room.id)
            model.active?.sync?.localChangeMade()
            onDone("Raum gelöscht")
        } catch {
            self.error = error.message
        }
    }
}

/// Zeile eines Raums wie eine Tabellenzeile: Name links, Fläche rechts; darunter Aufbau und Stand; Notiz.
struct RoomRow: View {
    let room: Room

    var body: some View {
        VStack(alignment: .leading, spacing: 3) {
            HStack(alignment: .firstTextBaseline, spacing: Space.s) {
                Text(room.name)
                    .font(Typo.body)
                    .foregroundStyle(Theme.textPrimary)
                    .frame(maxWidth: .infinity, alignment: .leading)
                if let area = room.areaSquareMeters {
                    Text("\(area.formatted(.number.precision(.fractionLength(0...2)))) m²")
                        .font(Typo.bodyStrong.monospacedDigit())
                        .foregroundStyle(Theme.textPrimary)
                }
                SyncStateBadge(state: room.syncState)
            }
            HStack(alignment: .firstTextBaseline, spacing: Space.s) {
                let parts: [String?] = [room.thicknessMillimeters.map { "\($0) mm" }, room.underfloorHeating ? "Fußbodenheizung" : nil]
                let details = parts.compactMap { $0 }
                Text(details.isEmpty ? "Aufbau noch offen" : details.joined(separator: " · "))
                    .font(Typo.subhead.monospacedDigit())
                    .foregroundStyle(Theme.textSecondary)
                    .frame(maxWidth: .infinity, alignment: .leading)
                HStack(spacing: 5) {
                    StatusDot(color: room.status.tint, size: 7)
                    Text(room.status.title)
                }
                .font(Typo.metaStrong)
                .foregroundStyle(Theme.textSecondary)
                .fixedSize()
            }
            if let notes = room.notes?.compactText {
                Text(notes).font(Typo.meta).foregroundStyle(Theme.textSecondary).lineLimit(2)
            }
        }
        .padding(.vertical, Space.xs)
        .contentShape(Rectangle())
        .accessibilityElement(children: .combine)
    }
}

extension RoomStatus {
    var tint: Color {
        switch self {
        case .planned: Theme.textTertiary
        case .laid: Theme.accent
        case .ready: Theme.success
        }
    }

    var symbol: String {
        switch self {
        case .planned: "calendar"
        case .laid: "square.stack.3d.up.fill"
        case .ready: "checkmark.seal.fill"
        }
    }

    /// Nächster Stand beim Wischen: geplant → eingebracht → belegreif.
    var next: RoomStatus? {
        switch self {
        case .planned: .laid
        case .laid: .ready
        case .ready: nil
        }
    }
}

extension RoomInput {
    /// Vorgaben aus den Basisdaten: Nenndicke, Dämmdicke und Fußbodenheizung der Bauakte.
    init(defaults project: BauakteProject) {
        self.init(thicknessMillimeters: project.nominalThicknessMm, insulationMillimeters: project.insulationThicknessMm,
                  underfloorHeating: project.underfloorHeating ?? false)
    }
}

/// Summen der Räume als Kennzahlen.
struct RoomTotalsView: View {
    let rooms: [Room]

    var body: some View {
        let totals = RoomTotals(rooms)
        HStack(spacing: Space.s) {
            MetricTile(value: totals.area.formatted(.number.precision(.fractionLength(0...1))), label: "m² gesamt", symbol: "square.dashed")
            MetricTile(value: totals.heatedArea.formatted(.number.precision(.fractionLength(0...1))), label: "m² mit Heizung", symbol: "flame")
            MetricTile(value: "\(totals.laid + totals.ready)/\(totals.count)", label: "Räume eingebracht", symbol: "square.stack.3d.up")
        }
    }
}
