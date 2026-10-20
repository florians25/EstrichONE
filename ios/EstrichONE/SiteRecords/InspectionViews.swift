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

/// Erstbegehung als kurze Liste: je Punkt „In Ordnung / Mangel / Entfällt“, Fotos (z. B. Zählerstand), Bemerkung.
/// „Keine Erstbegehung notwendig“ verlangt eine Begründung.
struct InitialInspectionView: View {
    let bauakte: Bauakte
    let onDone: () -> Void

    @Environment(AppModel.self) private var model
    @State private var notRequired = false
    @State private var checks: [InitialCheck: CheckResult] = [:]
    @State private var remarks = ""
    @State private var photoIds: [UUID] = []
    @State private var capturing = false
    @State private var captureStarted = Date.now
    @State private var error: String?

    var body: some View {
        NavigationStack {
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
                Section(notRequired ? "Begründung" : "Bemerkung (optional)") {
                    TextField(notRequired ? "Warum ist keine Erstbegehung notwendig?" : "z. B. Randdämmung im Bad fehlt", text: $remarks, axis: .vertical)
                        .lineLimit(2...6)
                        .accessibilityIdentifier("inspectionRemarksField")
                }
                if let error {
                    Text(error).foregroundStyle(Theme.error).accessibilityIdentifier("inspectionError")
                }
            }
            .navigationTitle("Erstbegehung")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Abbrechen", action: onDone) }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Sichern") { Task { await save() } }
                        .accessibilityIdentifier("saveInspectionButton")
                }
            }
            .fullScreenCover(isPresented: $capturing) {
                CameraView(bauakte: bauakte, allowsChangingBauakte: false) { _ in
                    capturing = false
                    Task { await collectPhotos() }
                }
            }
        }
    }

    /// Fotos, die seit dem Öffnen der Kamera zu dieser Bauakte gespeichert wurden, gehören zur Erstbegehung.
    private func collectPhotos() async {
        guard let service = model.active?.service else { return }
        let new = ((try? await service.files(of: bauakte.id)) ?? [])
            .filter { $0.isPhoto && ($0.capturedAt ?? .distantPast) >= captureStarted.addingTimeInterval(-1) }
            .map(\.id)
        photoIds = Array(Set(photoIds + new))
    }

    private func save() async {
        guard let service = model.active?.service else { return }
        do throws(SiteRecordError) {
            try await service.addInitialInspection(to: bauakte.id, notRequired: notRequired, checks: checks, photoFileIds: photoIds,
                                                   remarks: remarks)
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
