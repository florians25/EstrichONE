import EstrichCore
import QuickLook
import SwiftUI

extension LexwareTone {
    var color: Color {
        switch self {
        case .success: Theme.success
        case .warning: Theme.warning
        case .danger: Theme.danger
        case .info: Theme.accent
        case .neutral: Color.secondary
        }
    }
}

/// Lexware Office in der Bauakte (nur lesend, nur online): Kontakt in Lexware Office, Abgleichstand, Summenzeile und die
/// zugeordneten Belege. Antippen eines Belegs öffnet das PDF, wenn Lexware Office eines bereitstellt. Keine Zuordnung,
/// keine Bearbeitung. Wird nur gezeigt, wenn das Modul eingeschaltet und der Kunde der Bauakte verknüpft ist.
struct LexwareBauakteSections: View {
    let bauakteId: UUID
    let data: BauakteLexware

    @Environment(AppModel.self) private var model
    @State private var pdfURL: URL?
    @State private var message: String?
    @State private var loading: String?

    var body: some View {
        if let contact = data.contact {
            Section("Lexware Office") {
                row("Kontakt in Lexware", contact.name ?? "Lexware-Kontakt", differs: Lexware.differs(contact, "name"))
                    .accessibilityIdentifier("lexwareContactRow")
                    .quickLookPreview($pdfURL)
                    .alert("Beleg", isPresented: Binding(get: { message != nil }, set: { if !$0 { message = nil } })) {
                        Button("OK", role: .cancel) {}
                    } message: {
                        Text(message ?? "")
                    }
                LabeledContent("Kundennummer in Lexware", value: contact.number ?? "ohne Kundennummer")
                if !Lexware.address(contact).isEmpty {
                    row("Rechnungsanschrift", Lexware.address(contact), differs: Lexware.differs(contact, "street", "postalCode", "city"))
                }
                if let email = contact.email, !email.isEmpty {
                    row("E-Mail", email, differs: Lexware.differs(contact, "email"))
                }
                if !Lexware.syncText(contact.sync).isEmpty {
                    LabeledContent("Abgleich") {
                        VStack(alignment: .trailing, spacing: 2) {
                            Text(Lexware.syncText(contact.sync))
                                .foregroundStyle(Lexware.syncTone(contact.sync).color)
                            if let at = Lexware.syncedAt(contact.sync) {
                                Text(at).font(.footnote).foregroundStyle(Color.secondary)
                            }
                        }
                    }
                }
            }
            if !data.vouchers.isEmpty {
                Section("Rechnungen und Angebote") {
                    ForEach(data.vouchers) { voucher in
                        Button {
                            Task { await open(voucher) }
                        } label: {
                            voucherRow(voucher)
                        }
                        .disabled(loading != nil)
                        .accessibilityIdentifier("lexwareVoucherRow")
                    }
                }
            }
        }
    }

    private func row(_ label: String, _ value: String, differs: Bool) -> some View {
        LabeledContent(label) {
            VStack(alignment: .trailing, spacing: 2) {
                Text(value)
                if differs {
                    Text(Lexware.differsText).font(.footnote).foregroundStyle(Color.secondary)
                }
            }
        }
    }

    private func voucherRow(_ voucher: LexwareVoucher) -> some View {
        HStack(spacing: 12) {
            Image(systemName: "doc.text")
                .foregroundStyle(Lexware.pdfAvailable(voucher) ? Theme.accent : Color.secondary)
            VStack(alignment: .leading, spacing: 2) {
                Text(Lexware.title(voucher)).foregroundStyle(Color.primary)
                Text(Lexware.detail(voucher)).font(.footnote).foregroundStyle(Color.secondary)
            }
            Spacer()
            if loading == voucher.id {
                ProgressView()
            } else {
                Text(Lexware.statusText(voucher.status, type: voucher.type))
                    .font(.subheadline.weight(.semibold))
                    .foregroundStyle(Lexware.statusTone(voucher.status, type: voucher.type).color)
            }
        }
    }

    /// Beleg-PDF vom Server (aus Lexware Office durchgereicht), angezeigt mit QuickLook. Für Entwürfe gibt es kein PDF.
    private func open(_ voucher: LexwareVoucher) async {
        guard Lexware.pdfAvailable(voucher) else {
            message = Lexware.draftHint
            return
        }
        guard let client = model.active?.client else {
            message = Lexware.offlineHint
            return
        }
        loading = voucher.id
        defer { loading = nil }
        do throws(APIError) {
            let data: Data = try await client.lexwareVoucherPDF(bauakteId: bauakteId, linkId: voucher.id)
            let folder = FileManager.default.temporaryDirectory.appending(path: "Vorschau", directoryHint: .isDirectory)
            try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            let url = folder.appending(path: Lexware.fileName(voucher))
            do {
                try data.write(to: url, options: [.atomic, .completeFileProtection])
                pdfURL = url
            } catch {
                message = "Das PDF konnte nicht gespeichert werden."
            }
        } catch {
            message = error == .offline ? Lexware.offlineHint : error.message
        }
    }
}

/// Stand der Prüfung gegen Lexware Office beim Anlegen eines Kunden.
struct LexwareCheckState: Equatable {
    var check: LexwareContactCheck?
    /// „Trotzdem neu anlegen“: Der Nutzer legt den Kunden bewusst ohne Verknüpfung an.
    var anyway = false
    /// Die verbindliche Prüfung beim Sichern läuft.
    var running = false
    /// Rückfrage „Ohne Prüfung anlegen“, wenn die Prüfung nicht möglich war.
    var askUnchecked = false

    var hits: [LexwareContactCandidate] {
        guard let check, check.checked else { return [] }
        return check.candidates
    }

    /// Es gibt Treffer, über die noch nicht entschieden wurde.
    var blocking: Bool { !hits.isEmpty && !anyway }
}

/// Prüfung beim Anlegen eines Kunden: nur online und mit eingeschaltetem Modul, unabhängig vom Schalter „Mit Lexware
/// Office abgleichen“. Ohne Verbindung wird nicht geprüft und nichts blockiert.
enum LexwareCustomerCheck {
    static func key(_ customer: NewCustomer?) -> String {
        guard let customer else { return "" }
        return "\(customer.name)|\(customer.isCompany)|\(customer.street ?? "")|\(customer.postalCode ?? "")|\(customer.city ?? "")"
    }

    /// Ergebnis der verbindlichen Prüfung beim Sichern.
    enum Verdict {
        /// Kein Treffer bzw. keine Prüfung zuständig: anlegen.
        case proceed
        /// Treffer in Lexware Office: nicht anlegen, bis der Nutzer übernimmt oder „Trotzdem neu anlegen“ wählt.
        case hits
        /// Prüfung nicht möglich (keine Verbindung, Lexware Office nicht erreichbar): Rückfrage.
        case unavailable
    }

    static let uncheckedTitle = "Prüfung in Lexware Office nicht möglich"
    static let uncheckedQuestion = "Die Prüfung in Lexware Office ist derzeit nicht möglich. Möchten Sie den Kunden ohne Prüfung anlegen? Gibt es ihn dort schon, entsteht ein doppelter Eintrag."
    static let existsMessage = "Dieser Kunde ist bereits in Lexware Office vorhanden. Bitte übernehmen Sie ihn oder wählen Sie „Trotzdem neu anlegen“."

    /// Verbindliche Prüfung beim Sichern: erst in Lexware Office prüfen (über den Server, mit den endgültigen Angaben),
    /// dann anlegen. Nur mit eingeschaltetem Modul; antwortet der Server ablehnend (Recht fehlt, älterer Server), ist
    /// keine Prüfung zuständig.
    @MainActor
    static func confirm(_ model: AppModel, _ customer: NewCustomer?, state: Binding<LexwareCheckState>) async -> Verdict {
        guard let customer, !state.wrappedValue.anyway, (model.active?.sync?.modules ?? .allOn).lexwareOn, let client = model.active?.client,
              let request = Lexware.checkRequest(name: customer.name, company: customer.isCompany, street: customer.street ?? "",
                                                 postalCode: customer.postalCode ?? "", city: customer.city ?? "", phone: customer.phone ?? "")
        else { return .proceed }
        state.wrappedValue.running = true
        defer { state.wrappedValue.running = false }
        do throws(APIError) {
            let result = try await client.checkLexwareContact(request)
            guard result.checked else { return .unavailable }
            state.wrappedValue.check = result
            return result.candidates.isEmpty ? .proceed : .hits
        } catch {
            if case .server = error { return .proceed }
            return .unavailable
        }
    }

    @MainActor
    static func run(_ model: AppModel, _ customer: NewCustomer?) async -> LexwareContactCheck? {
        guard let customer, (model.active?.sync?.modules ?? .allOn).lexwareOn, let client = model.active?.client,
              let request = Lexware.checkRequest(name: customer.name, company: customer.isCompany, street: customer.street ?? "",
                                                 postalCode: customer.postalCode ?? "", city: customer.city ?? "", phone: customer.phone ?? "")
        else { return nil }
        try? await Task.sleep(for: .milliseconds(500))
        guard !Task.isCancelled else { return nil }
        return try? await client.checkLexwareContact(request)
    }
}

/// Warnung „Dieser Kunde ist bereits in Lexware Office vorhanden“ mit den Treffern und den beiden Wegen: aus Lexware
/// Office übernehmen (der Server legt den Kunden mit den Lexware-Daten an und verknüpft ihn) oder trotzdem neu anlegen.
struct LexwareCheckSection: View {
    @Binding var state: LexwareCheckState
    let onImported: (Contact) -> Void

    @Environment(AppModel.self) private var model
    @State private var importing = false
    @State private var message: String?

    var body: some View {
        if state.running {
            Section {
                HStack(spacing: 12) {
                    ProgressView()
                    Text("Prüfung in Lexware Office läuft")
                }
                .accessibilityIdentifier("lexwareCheckRunning")
            }
        }
        if !state.hits.isEmpty {
            Section {
                ForEach(state.hits) { candidate in
                    VStack(alignment: .leading, spacing: 4) {
                        Text(candidate.contact.name).font(.body.weight(.semibold))
                        Text(Lexware.candidateLine(candidate)).font(.footnote).foregroundStyle(Color.secondary)
                        if candidate.contact.linkedContactId != nil {
                            Text("Bereits verknüpft mit dem Kunden \(candidate.contact.linkedContactName ?? "").")
                                .font(.footnote).foregroundStyle(Color.secondary)
                        } else if let check = state.check, Lexware.canImport(check, candidate) {
                            Button("Aus Lexware Office übernehmen") {
                                Task { await importContact(candidate) }
                            }
                            .buttonStyle(.borderless)
                            .disabled(importing)
                            .accessibilityIdentifier("lexwareImportButton")
                        }
                    }
                }
                Toggle("Trotzdem neu anlegen", isOn: $state.anyway)
                    .accessibilityIdentifier("lexwareAnywayToggle")
            } header: {
                Label(Lexware.existsWarning, systemImage: "exclamationmark.triangle.fill")
                    .foregroundStyle(Theme.warning)
                    .textCase(nil)
            } footer: {
                Text(message ?? "Übernehmen legt den Kunden mit den Daten aus Lexware Office an und verknüpft ihn. Trotzdem neu anlegen legt ihn ohne Verknüpfung an.")
            }
        } else if let check = state.check, !check.checked, let note = check.message {
            Section {
                Text("\(note) Beim Sichern wird erneut geprüft.").font(.footnote).foregroundStyle(Color.secondary)
            }
        }
    }

    private func importContact(_ candidate: LexwareContactCandidate) async {
        guard let active = model.active else { return }
        importing = true
        defer { importing = false }
        do throws(APIError) {
            let result = try await active.client.importLexwareContact(candidate.contact.id)
            guard result.imported, let text = result.contactId, let id = UUID(uuidString: text) else {
                message = "In EstrichONE gibt es bereits einen ähnlichen Kunden. Bitte verwenden Sie den vorhandenen Kunden."
                return
            }
            // Der Kunde steht jetzt auf dem Server: abgleichen und den übernommenen Kunden verwenden.
            await active.sync?.run()
            let contact = try? await active.service?.contact(id)
            if let contact {
                onImported(contact)
            } else {
                message = "Der Kunde wurde übernommen und erscheint nach der nächsten Synchronisation."
            }
        } catch {
            message = error == .offline ? "Ohne Verbindung nicht möglich. Bitte später erneut versuchen." : error.message
        }
    }
}
