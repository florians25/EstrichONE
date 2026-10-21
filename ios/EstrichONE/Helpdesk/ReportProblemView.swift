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
                    Text(error).foregroundStyle(Theme.error).accessibilityIdentifier("ticketError")
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
        let ticket = NewTicket(title: title, description: details, category: category, reporterBlocked: blocked)
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

/// Eigene Meldungen: lokal wartende und die Liste vom Server mit Status und Antworten.
struct MyTicketsView: View {
    @Environment(AppModel.self) private var model
    @State private var tickets: [TicketSummary] = []
    @State private var waiting: [(id: UUID, title: String)] = []
    @State private var offline = false

    var body: some View {
        List {
            if !waiting.isEmpty {
                Section("Wartet auf Übertragung") {
                    ForEach(waiting, id: \.id) { item in
                        Label(item.title, systemImage: "arrow.up.circle").foregroundStyle(Theme.textSecondary)
                    }
                }
            }
            Section {
                if offline {
                    Label("Die Liste ist nur mit Verbindung zum Server verfügbar.", systemImage: "wifi.slash").foregroundStyle(Theme.textSecondary)
                } else if tickets.isEmpty {
                    Text("Keine Meldungen.").foregroundStyle(Theme.textSecondary)
                }
                ForEach(tickets) { ticket in
                    NavigationLink {
                        TicketDetailView(ticketId: ticket.id)
                    } label: {
                        VStack(alignment: .leading, spacing: 4) {
                            Text(ticket.title)
                            HStack {
                                Text(ticket.number).monospacedDigit()
                                TicketStatusText(status: ticket.status)
                            }
                            .font(.caption)
                        }
                    }
                }
            }
        }
        .navigationTitle("Meine Meldungen")
        .task(id: model.active?.sync?.revision) { await load() }
        .refreshable { await load() }
    }

    private func load() async {
        let local = (try? await model.helpdesk?.localReports()) ?? []
        waiting = local.filter { $0.state != .synced }.map { ($0.id, $0.title) }
        guard let client = model.active?.client else { return }
        do throws(APIError) {
            tickets = try await client.myTickets()
            offline = false
        } catch {
            offline = true
        }
    }
}

struct TicketStatusText: View {
    let status: TicketStatus

    var body: some View {
        Label(status.title, systemImage: symbol)
            .foregroundStyle(status == .waitingForReporter ? Theme.hint : status == .resolved || status == .closed ? Theme.success : Theme.textSecondary)
    }

    private var symbol: String {
        switch status {
        case .new: "tray"
        case .inProgress: "wrench.and.screwdriver"
        case .waitingForReporter: "questionmark.bubble"
        case .resolved: "checkmark.circle"
        case .closed: "lock"
        }
    }
}

/// Ein Ticket aus Sicht des Melders: öffentliche Antworten, eigene Antwort, „Wieder öffnen“ nach „Gelöst“.
struct TicketDetailView: View {
    let ticketId: UUID

    @Environment(AppModel.self) private var model
    @State private var detail: TicketDetail?
    @State private var reply = ""
    @State private var error: String?

    var body: some View {
        List {
            if let detail {
                Section {
                    Text(detail.summary.title).font(.headline)
                    LabeledContent("Nummer", value: detail.summary.number)
                    LabeledContent("Status") { TicketStatusText(status: detail.summary.status) }
                    if let description = detail.description { Text(description).foregroundStyle(Theme.textSecondary) }
                    if let resolution = detail.resolution { LabeledContent("Lösung", value: resolution) }
                }
                Section("Verlauf") {
                    if detail.timeline.isEmpty { Text("Noch keine Antwort.").foregroundStyle(Theme.textSecondary) }
                    ForEach(Array(detail.timeline.enumerated()), id: \.offset) { _, item in
                        TicketTimelineRow(item: item, isOwn: { comment in comment.authorUserId == model.active?.user.id })
                    }
                }
                if detail.summary.status != .closed {
                    Section {
                        TextField("Antwort an den Support", text: $reply, axis: .vertical)
                            .lineLimit(2...6)
                            .accessibilityIdentifier("ticketReplyField")
                        Button { Task { await send() } } label: { Label("Antwort senden", systemImage: "paperplane.fill") }
                            .estrichButton(.primary)
                            .disabled(reply.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                            .accessibilityIdentifier("sendReplyButton")
                        if detail.summary.status == .resolved {
                            Button("Problem besteht weiter – wieder öffnen") { Task { await reopen() } }
                        }
                    }
                }
                if let error { Text(error).foregroundStyle(Theme.error) }
            } else {
                ProgressView()
            }
        }
        .navigationTitle(detail?.summary.number ?? "Meldung")
        .navigationBarTitleDisplayMode(.inline)
        .task { await load() }
        .refreshable { await load() }
    }

    private func load() async {
        do throws(APIError) {
            detail = try await model.active?.client.ticket(ticketId)
        } catch {
            self.error = error.message
        }
    }

    private func send() async {
        do throws(APIError) {
            detail = try await model.active?.client.comment(on: ticketId, text: reply)
            reply = ""
        } catch {
            self.error = error.message
        }
    }

    private func reopen() async {
        do throws(APIError) {
            detail = try await model.active?.client.reopen(ticketId)
        } catch {
            self.error = error.message
        }
    }
}

/// Eintrag im Ticketverlauf: Antworten als Sprechblase (eigene rechts), Ereignisse als ruhige Zeile mit Symbol.
struct TicketTimelineRow: View {
    let item: TicketDetail.TimelineItem
    let isOwn: (TicketComment) -> Bool

    var body: some View {
        switch item {
        case let .comment(comment):
            let own = isOwn(comment)
            VStack(alignment: own ? .trailing : .leading, spacing: 3) {
                Text(comment.text)
                    .padding(.horizontal, 12).padding(.vertical, 8)
                    .background(own ? Theme.navyBackground : Theme.neutralBackground, in: RoundedRectangle(cornerRadius: 14, style: .continuous))
                    .foregroundStyle(own ? Theme.navyText : .primary)
                Text([own ? "Sie" : comment.authorName, comment.createdAt.formatted(date: .abbreviated, time: .shortened)]
                    .compactMap { $0 }.joined(separator: " · "))
                    .font(.caption2).foregroundStyle(Theme.textSecondary)
            }
            .frame(maxWidth: .infinity, alignment: own ? .trailing : .leading)
            .listRowSeparator(.hidden)
            .accessibilityElement(children: .combine)
        case let .event(event):
            Label {
                VStack(alignment: .leading, spacing: 1) {
                    Text(event.text).font(.footnote)
                    Text(event.at.formatted(date: .abbreviated, time: .shortened)).font(.caption2).foregroundStyle(Theme.textSecondary)
                }
            } icon: {
                Image(systemName: event.kind == "created" ? "plus.circle" : "arrow.triangle.swap").foregroundStyle(Theme.textSecondary)
            }
            .listRowSeparator(.hidden)
        }
    }
}
