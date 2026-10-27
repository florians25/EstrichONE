import EstrichCore
import PhotosUI
import SwiftUI

/// „Problem melden“ (14, Abschnitt 2): Pflicht ist nur der Titel. Gerät und Benutzer setzt die App; die
/// Diagnosedaten sieht der Benutzer vor dem Senden und kann sie abwählen.
struct ReportProblemView: View {
    var initialCategory: TicketCategory = .other
    /// Ohne Anmeldung (vom Anmeldebildschirm): Name statt Konto, keine Anhänge, sofort senden.
    var withoutSignIn = false
    let onDone: () -> Void

    @Environment(AppModel.self) private var model
    @State private var title = ""
    @State private var details = ""
    @State private var category: TicketCategory = .other
    @State private var blocked = false
    @State private var priority: TicketPriority = .normal
    @State private var includeDiagnostics = true
    @State private var reporterName = ""
    @State private var diagnostics: DiagnosticsReport?
    @State private var pickerItems: [PhotosPickerItem] = []
    @State private var attachments: [Data] = []
    @State private var sending = false
    @State private var error: String?
    @State private var sentNumber: String?
    @State private var queued = false

    var body: some View {
        NavigationStack {
            Form {
                if withoutSignIn {
                    Section {
                        TextField("Ihr Name", text: $reporterName)
                            .textContentType(.name)
                            .accessibilityIdentifier("reporterNameField")
                    } footer: {
                        Text("Die Meldung kommt von diesem eingerichteten Gerät. Die IT sieht Ihren Namen so, wie Sie ihn eingeben.")
                    }
                }
                Section {
                    TextField("Was ist passiert?", text: $title, axis: .vertical)
                        .accessibilityIdentifier("ticketTitleField")
                    TextField("Details (optional)", text: $details, axis: .vertical)
                        .lineLimit(3...8)
                    Picker("Kategorie", selection: $category) {
                        ForEach(TicketCategory.allCases) { Text($0.title).tag($0) }
                    }
                    if !withoutSignIn {
                        Picker("Priorität", selection: $priority) {
                            ForEach(TicketPriority.allCases) { Text($0.title).tag($0) }
                        }
                    }
                    Toggle("Dringend – ich kann nicht arbeiten", isOn: $blocked)
                }
                if !withoutSignIn {
                    // Der Label-Inhalt läuft in einer Sendable-Closure: nur den fertigen Text übergeben.
                    let attachmentText = attachments.isEmpty ? "Bildschirmfoto oder Foto anhängen" : "\(attachments.count) Bilder angehängt"
                    Section {
                        PhotosPicker(selection: $pickerItems, maxSelectionCount: 5, matching: .images) {
                            Label(attachmentText, systemImage: "paperclip")
                        }
                    }
                }
                Section {
                    Toggle("Technische Informationen mitsenden", isOn: $includeDiagnostics)
                        .accessibilityIdentifier("diagnosticsToggle")
                    if includeDiagnostics, let diagnostics {
                        DisclosureGroup("Anzeigen, was gesendet wird") {
                            ForEach(diagnostics.displayLines, id: \.label) { line in
                                LabeledContent(line.label, value: line.value).font(.footnote)
                            }
                        }
                        .accessibilityIdentifier("diagnosticsDisclosure")
                    }
                } footer: {
                    Text("Nie enthalten: Passwörter, PINs, Inhalte von Bauakten, Notizen, Fotos, Kunden und Adressen.")
                }
                if let error {
                    Text(error).foregroundStyle(Theme.danger).accessibilityIdentifier("ticketError")
                }
            }
            .navigationTitle("Problem melden")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Abbrechen", action: onDone) }
                ToolbarItem(placement: .confirmationAction) {
                    if sending {
                        ProgressView()
                    } else {
                        Button("Senden") { Task { await send() } }
                            .disabled(title.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                            .accessibilityIdentifier("sendTicketButton")
                    }
                }
            }
            .alert("Meldung gesendet", isPresented: Binding(get: { sentNumber != nil }, set: { if !$0 { onDone() } })) {
                Button("Fertig", action: onDone)
            } message: {
                Text("Die IT hat Ihre Meldung \(sentNumber ?? "") erhalten.")
            }
            .alert("Meldung gespeichert", isPresented: $queued) {
                Button("Fertig", action: onDone)
            } message: {
                Text("Die Meldung wird gesendet, sobald eine Verbindung besteht. Den Stand sehen Sie unter „Meine Meldungen“.")
            }
            .task {
                category = initialCategory
                diagnostics = await model.diagnostics()
            }
            .onChange(of: pickerItems) { _, items in
                Task {
                    var images: [Data] = []
                    for item in items {
                        if let data = try? await item.loadTransferable(type: Data.self),
                           let jpeg = UIImage(data: data)?.jpegData(compressionQuality: 0.8) {
                            images.append(jpeg)
                        }
                    }
                    attachments = images
                }
            }
        }
    }

    private func send() async {
        sending = true
        defer { sending = false }
        let ticket = NewTicket(title: title, description: details, category: category, reporterBlocked: blocked,
                               priority: blocked ? .urgent : priority)
        do throws(TicketError) {
            if withoutSignIn {
                sentNumber = try await model.reportWithoutSignIn(ticket, reporterName: reporterName, includeDiagnostics: includeDiagnostics)
            } else {
                try await model.report(ticket, includeDiagnostics: includeDiagnostics, attachments: attachments)
                queued = true
            }
        } catch {
            self.error = error.message
        }
    }
}
