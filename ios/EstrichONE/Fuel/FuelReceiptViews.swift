import EstrichCore
import PhotosUI
import SwiftUI
import UIKit

/// Mehr › Tankbelege: eigene Belege mit Stand der Prüfung, oben die noch nicht übertragenen. „+“ erfasst einen neuen Beleg
/// (auch offline; Fahrzeuge aus dem letzten Abruf).
struct FuelReceiptsView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.shotScene) private var shotScene
    @State private var receipts: [FuelReceipt]?
    @State private var pending: [LocalFuelReceipt] = []
    @State private var message: String?
    @State private var creating = false
    @State private var opened: FuelReceipt?
    @State private var toast: String?

    var body: some View {
        List {
            let unsent = pending.filter { $0.state != .synced || $0.number == nil }
            if !unsent.isEmpty {
                Section {
                    ForEach(unsent) { receipt in
                        HStack(spacing: Space.m) {
                            VStack(alignment: .leading, spacing: 2) {
                                Text("\(FuelText.money(receipt.amountGross)) · \(FuelText.liters(receipt.liters))").font(Typo.body)
                                Text("\(FuelText.day(receipt.date)) · \(receipt.vehicleName)").font(Typo.subhead).foregroundStyle(Theme.textSecondary)
                            }
                            Spacer()
                            Label(receipt.state == .problem ? "Bitte prüfen" : "wird übertragen",
                                  systemImage: receipt.state == .problem ? "exclamationmark.triangle" : "arrow.up.circle")
                                .font(Typo.meta)
                                .foregroundStyle(receipt.state == .problem ? Theme.danger : Theme.textSecondary)
                        }
                        .accessibilityElement(children: .combine)
                    }
                } header: {
                    Text("Auf diesem Gerät")
                } footer: {
                    Text("Belege und Fotos werden übertragen, sobald eine Verbindung besteht.")
                }
            }

            if let message {
                Section { Text(message).font(Typo.subhead).foregroundStyle(Theme.textSecondary) }
            }

            if let receipts {
                if receipts.isEmpty && unsent.isEmpty {
                    Section {
                        EmptyStateView(symbol: "fuelpump", title: "Noch keine Tankbelege",
                                       message: "Beleg nach dem Tanken fotografieren – Fahrzeug, Liter und Betrag sind schnell erfasst.",
                                       actionTitle: "Beleg erfassen", actionSymbol: "camera") { creating = true }
                    }
                } else if !receipts.isEmpty {
                    Section("Meine Tankbelege") {
                        ForEach(receipts) { receipt in
                            Button { opened = receipt } label: { FuelReceiptRow(receipt: receipt) }
                                .foregroundStyle(Theme.textPrimary)
                        }
                    }
                }
            } else if message == nil {
                Section { HStack { Spacer(); ProgressView(); Spacer() } }
            }
        }
        .listStyle(.insetGrouped)
        .navigationTitle("Tankbelege")
        .toolbar {
            ToolbarItem(placement: .primaryAction) {
                Button { creating = true } label: { Label("Beleg erfassen", systemImage: "plus") }
                    .accessibilityIdentifier("newFuelReceipt")
            }
        }
        .refreshable { await load() }
        .task { await load() }
        .sheet(isPresented: $creating) {
            NewFuelReceiptView { saved in
                creating = false
                if saved {
                    toast = "Tankbeleg gespeichert – er wird übertragen"
                    Task { await load() }
                }
            }
        }
        .sheet(item: $opened) { receipt in
            NavigationStack { FuelReceiptDetailView(receipt: receipt) }
        }
        .toast($toast)
    }

    private func load() async {
        if let active = model.active, let sync = active.sync {
            pending = (try? await FuelReceiptService(store: sync.store, files: sync.files, userId: active.user.id).localReceipts()) ?? []
        }
        guard let client = model.active?.client else { return }
        do throws(APIError) {
            receipts = try await client.myFuelReceipts()
            message = nil
        } catch {
            message = receipts == nil ? "Ihre übertragenen Belege sind nur mit Verbindung sichtbar. Neue Belege lassen sich trotzdem erfassen." : nil
            if receipts == nil { receipts = [] }
        }
        if shotScene == "tankbeleg-neu", !creating { creating = true }
    }
}

private struct FuelReceiptRow: View {
    let receipt: FuelReceipt

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: Space.m) {
            VStack(alignment: .leading, spacing: 2) {
                Text("\(FuelText.money(receipt.amountGross)) · \(FuelText.liters(receipt.liters)) \(receipt.fuelType.title)").font(Typo.body)
                Text("\(FuelText.day(receipt.date)) · \(receipt.vehicleText)").font(Typo.subhead).foregroundStyle(Theme.textSecondary)
                if let reason = receipt.rejectionReason, receipt.status == .rejected {
                    Text(reason).font(Typo.meta).foregroundStyle(Theme.danger)
                }
            }
            Spacer(minLength: Space.s)
            VStack(alignment: .trailing, spacing: 2) {
                Text(receipt.statusText).font(Typo.subheadStrong).foregroundStyle(FuelText.tint(receipt.status))
                Text(receipt.number).font(Typo.meta.monospacedDigit()).foregroundStyle(Theme.textTertiary)
            }
        }
        .padding(.vertical, 2)
        .accessibilityElement(children: .combine)
    }
}

/// Ein übertragener Beleg: Angaben, Stand der Prüfung, Hinweise.
struct FuelReceiptDetailView: View {
    let receipt: FuelReceipt
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        List {
            Section {
                LabeledContent("Status") { Text(receipt.statusText).foregroundStyle(FuelText.tint(receipt.status)) }
                if let reason = receipt.rejectionReason { LabeledContent("Grund", value: reason) }
            }
            Section("Beleg") {
                LabeledContent("Datum", value: FuelText.day(receipt.date))
                LabeledContent("Fahrzeug", value: receipt.vehicleText)
                LabeledContent("Kraftstoff", value: receipt.fuelType.title)
                LabeledContent("Menge", value: FuelText.liters(receipt.liters))
                LabeledContent("Betrag brutto", value: FuelText.money(receipt.amountGross))
                LabeledContent("Bezahlt mit", value: receipt.paymentMethod.title)
                if let km = receipt.odometerKm { LabeledContent("Kilometerstand", value: "\(km.formatted()) km") }
                if let consumption = receipt.consumptionPer100Km {
                    LabeledContent("Verbrauch", value: "\(consumption.formatted(.number.precision(.fractionLength(1)))) l/100 km")
                }
                if let station = receipt.station { LabeledContent("Tankstelle", value: station) }
            }
            let hints = receipt.warnings.filter { $0.code != "missing_image" }
            if !hints.isEmpty {
                Section("Hinweise für das Büro") {
                    ForEach(hints, id: \.text) { Label($0.text, systemImage: "exclamationmark.triangle").font(Typo.subhead).foregroundStyle(Theme.warning) }
                }
            }
        }
        .navigationTitle(receipt.number)
        .navigationBarTitleDisplayMode(.inline)
        .toolbar { ToolbarItem(placement: .confirmationAction) { Button("Fertig") { dismiss() } } }
    }
}

/// Neuer Tankbeleg: Beleg fotografieren (Dokumentkamera oder Foto), Vorschläge aus der Texterkennung, Fahrzeug (vorbelegt mit
/// dem heutigen Dispo-Fahrzeug), Datum, Kilometerstand, Liter, Betrag, Kraftstoff, Zahlart, Tankstelle. Speichern geht offline.
struct NewFuelReceiptView: View {
    let onDone: (Bool) -> Void

    @Environment(AppModel.self) private var model
    @Environment(\.shotScene) private var shotScene
    @State private var options: FuelOptions?
    @State private var draft = FuelReceiptDraft()
    @State private var photo: Data?
    @State private var preview: UIImage?
    @State private var scanning = false
    @State private var pickerItem: PhotosPickerItem?
    @State private var recognizing = false
    @State private var suggested = false
    @State private var saving = false
    @State private var error: String?
    @State private var loaded = false

    private var vehicle: FuelVehicleOption? { options?.vehicles.first { $0.id == draft.vehicleId } }

    var body: some View {
        NavigationStack {
            Form {
                photoSection

                Section {
                    if let vehicles = options?.vehicles, !vehicles.isEmpty {
                        Picker("Fahrzeug", selection: $draft.vehicleId) {
                            Text("Bitte wählen").tag(UUID?.none)
                            ForEach(vehicles) { Text($0.title).tag(Optional($0.id)) }
                        }
                        .accessibilityIdentifier("fuelVehiclePicker")
                    } else if loaded {
                        Text("Keine Fahrzeuge verfügbar. Bitte einmal mit Verbindung öffnen.").font(Typo.subhead).foregroundStyle(Theme.textSecondary)
                    }
                    DatePicker("Datum", selection: $draft.date, in: ...Date(), displayedComponents: .date)
                    LabeledContent("Kilometerstand") {
                        TextField(vehicle?.lastOdometerKm.map { "zuletzt \($0.formatted())" } ?? "km", text: $draft.odometer)
                            .keyboardType(.numberPad)
                            .multilineTextAlignment(.trailing)
                            .accessibilityIdentifier("fuelOdometerField")
                    }
                } header: {
                    Text("Fahrzeug")
                } footer: {
                    if options?.suggestedVehicleId != nil, draft.vehicleId == options?.suggestedVehicleId {
                        Text("Vorbelegt mit Ihrem Fahrzeug aus der heutigen Disposition.")
                    }
                }

                Section("Getankt") {
                    Picker("Kraftstoff", selection: $draft.fuelType) {
                        ForEach(FuelType.allCases) { Text($0.title).tag($0) }
                    }
                    .pickerStyle(.segmented)
                    LabeledContent("Menge (Liter)") {
                        TextField("0,00", text: $draft.liters)
                            .keyboardType(.decimalPad)
                            .multilineTextAlignment(.trailing)
                            .accessibilityIdentifier("fuelLitersField")
                    }
                    LabeledContent("Betrag brutto (€)") {
                        TextField("0,00", text: $draft.amount)
                            .keyboardType(.decimalPad)
                            .multilineTextAlignment(.trailing)
                            .accessibilityIdentifier("fuelAmountField")
                    }
                    if let price = draft.pricePerLiter {
                        LabeledContent("Preis je Liter", value: "\(price.formatted(.number.precision(.fractionLength(3)))) €")
                            .foregroundStyle(Theme.textSecondary)
                    }
                }

                Section {
                    Picker("Bezahlt mit", selection: $draft.paymentMethod) {
                        ForEach(FuelPaymentMethod.allCases) { Text($0.title).tag($0) }
                    }
                    TextField("Tankstelle (optional)", text: $draft.station)
                        .textContentType(.organizationName)
                } footer: {
                    if draft.paymentMethod == .privatelyPaid {
                        Text("Privat ausgelegt: Das Büro erstattet den Betrag nach der Prüfung.")
                    }
                }

                let hints = draft.hints(for: vehicle)
                if !hints.isEmpty {
                    Section {
                        ForEach(hints, id: \.self) { Label($0, systemImage: "exclamationmark.triangle").font(Typo.subhead).foregroundStyle(Theme.warning) }
                    }
                }
                if let error {
                    Text(error).foregroundStyle(Theme.danger).accessibilityIdentifier("fuelError")
                }
            }
            .navigationTitle("Tankbeleg")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Abbrechen") { onDone(false) } }
                ToolbarItem(placement: .confirmationAction) {
                    if saving {
                        ProgressView()
                    } else {
                        Button("Speichern") { Task { await save() } }
                            .disabled(draft.validate() != nil)
                            .accessibilityIdentifier("saveFuelReceipt")
                    }
                }
            }
            .fullScreenCover(isPresented: $scanning) {
                DocumentCameraView { pages in
                    scanning = false
                    if let first = pages.first { Task { await received(first) } }
                }
                .ignoresSafeArea()
            }
            .onChange(of: pickerItem) { _, item in
                guard let item else { return }
                Task {
                    if let data = try? await item.loadTransferable(type: Data.self) { await received(data) }
                    pickerItem = nil
                }
            }
            .task { await prepare() }
        }
    }

    private var photoSection: some View {
        // Label-Inhalte laufen in Sendable-Closures: nur fertige Texte übergeben.
        let scanText = photo == nil ? "Beleg fotografieren" : "Neu fotografieren"
        let pickText = photo == nil ? "Foto aus der Mediathek" : "Anderes Foto wählen"
        return Section {
            if let preview {
                Image(uiImage: preview)
                    .resizable()
                    .scaledToFit()
                    .frame(maxHeight: 220)
                    .frame(maxWidth: .infinity)
                    .clipShape(RoundedRectangle(cornerRadius: Radius.s))
                    .accessibilityLabel("Belegfoto")
            }
            if DocumentCameraView.isAvailable {
                Button { scanning = true } label: {
                    Label(scanText, systemImage: "doc.viewfinder")
                }
                .accessibilityIdentifier("scanFuelReceipt")
            }
            PhotosPicker(selection: $pickerItem, matching: .images) {
                Label(pickText, systemImage: "photo.on.rectangle")
            }
            if recognizing {
                HStack(spacing: Space.s) {
                    ProgressView()
                    Text("Beleg wird gelesen …").font(Typo.subhead).foregroundStyle(Theme.textSecondary)
                }
            }
        } header: {
            Text("Beleg")
        } footer: {
            if suggested {
                Text("Betrag, Liter und Datum aus dem Beleg vorgeschlagen – bitte prüfen.")
            } else {
                Text("Das Foto wird mit dem Beleg übertragen und im Büro geprüft.")
            }
        }
    }

    private func prepare() async {
        guard !loaded else { return }
        var loadedOptions: FuelOptions?
        if let store = model.active?.sync?.store {
            loadedOptions = await FuelOptionsStore(store: store).load(using: model.active?.client)
        }
        options = loadedOptions
        draft = FuelReceiptDraft.prefilled(from: loadedOptions)
        loaded = true
        if shotScene == "tankbeleg-neu" {
            // Screenshot-Modus: ausgefüllter Beleg mit Foto.
            draft.odometer = "48210"
            draft.liters = "61,37"
            draft.amount = "104,27"
            draft.station = "Aral Landshut Nord"
            suggested = true
            let image = DemoReceipt.image()
            preview = image
            photo = image.jpegData(compressionQuality: 0.8)
        }
    }

    /// Foto übernehmen: wie alle Fotos verkleinern und komprimieren (3000 px, JPEG 0,78), dann Text erkennen und leere Felder vorschlagen.
    private func received(_ data: Data) async {
        let prepared = await Task.detached(priority: .userInitiated) { PhotoCompressor.prepare(data) }.value
        photo = prepared
        preview = UIImage(data: prepared)
        recognizing = true
        let lines = await Task.detached(priority: .userInitiated) { () -> [String] in
            guard let page = ScanPDFBuilder.preparePage(prepared) else { return [] }
            return ScanPDFBuilder.recognizeText(in: page).sorted { $0.box.midY > $1.box.midY }.map(\.text)
        }.value
        recognizing = false
        let suggestion = FuelReceiptTextParser.suggest(from: lines)
        var applied = false
        if draft.amount.isEmpty, let amount = suggestion.amount {
            draft.amount = FuelText.decimal(amount, digits: 2)
            applied = true
        }
        if draft.liters.isEmpty, let liters = suggestion.liters {
            draft.liters = FuelText.decimal(liters, digits: 2)
            applied = true
        }
        if let day = suggestion.date, let date = CalendarDay.date(day) {
            draft.date = date
            applied = true
        }
        if let type = suggestion.fuelType, draft.fuelType != type {
            draft.fuelType = type
            applied = true
        }
        suggested = applied
    }

    private func save() async {
        guard let active = model.active, let sync = active.sync else {
            error = FuelReceiptError.storage.message
            return
        }
        saving = true
        defer { saving = false }
        do throws(FuelReceiptError) {
            try await FuelReceiptService(store: sync.store, files: sync.files, userId: active.user.id).record(draft, photo: photo, vehicle: vehicle)
            active.sync?.localChangeMade()
            onDone(true)
        } catch {
            self.error = error.message
        }
    }
}

enum FuelText {
    static func money(_ value: Double) -> String { value.formatted(.currency(code: "EUR")) }

    static func liters(_ value: Double) -> String { "\(value.formatted(.number.precision(.fractionLength(0...2)))) l" }

    static func decimal(_ value: Double, digits: Int) -> String { value.formatted(.number.precision(.fractionLength(digits)).grouping(.never)) }

    static func day(_ text: String) -> String {
        CalendarDay.date(text)?.formatted(date: .abbreviated, time: .omitted) ?? text
    }

    static func tint(_ status: FuelReceiptStatus) -> Color {
        switch status {
        case .new: Theme.accent
        case .checked: Theme.success
        case .rejected: Theme.danger
        case .settled: Theme.textSecondary
        }
    }
}

/// Beispielbeleg (Demomodus, Bildschirmfotos): eine schlichte Tankquittung.
enum DemoReceipt {
    static func image() -> UIImage {
        let size = CGSize(width: 600, height: 900)
        return UIGraphicsImageRenderer(size: size).image { context in
            UIColor(white: 0.97, alpha: 1).setFill()
            context.fill(CGRect(origin: .zero, size: size))
            let lines = ["ARAL Tankstelle", "Landshut Nord", "", "Datum 18.11.2026  07:12", "Säule 4", "", "Diesel", "61,37 l x 1,699 EUR/l",
                         "", "Summe EUR        104,27", "", "Kartenzahlung", "Vielen Dank für Ihren Besuch"]
            let font = UIFont.monospacedSystemFont(ofSize: 30, weight: .regular)
            for (index, line) in lines.enumerated() {
                (line as NSString).draw(at: CGPoint(x: 44, y: 60 + CGFloat(index) * 56),
                                        withAttributes: [.font: index == 0 || line.hasPrefix("Summe") ? UIFont.monospacedSystemFont(ofSize: 32, weight: .bold) : font,
                                                         .foregroundColor: UIColor(white: 0.15, alpha: 1)])
            }
        }
    }
}
