import EstrichCore
import SwiftUI

/// Zeile einer Aufgabe bzw. eines Mangels mit Abhaken (große Tippfläche). Art und Zustand stehen immer auch als Text da.
struct WorkItemRow: View {
    let item: WorkItem
    var assigneeName: String?
    var showsBauakte: String?
    /// Öffnet die zugehörige Bauakte (Startseite, „Meine Aufgaben“); dann mit Pfeil rechts.
    var onOpen: (() -> Void)?
    let onToggle: () -> Void

    var body: some View {
        HStack(alignment: .top, spacing: Space.xs) {
            Button(action: onToggle) {
                Image(systemName: item.isDone ? "checkmark.circle.fill" : "circle")
                    .font(.title2.weight(.regular))
                    .foregroundStyle(item.isDone ? Theme.success : (item.kind == .defect ? Theme.warning : Theme.textTertiary))
                    .frame(width: 48, height: 48)
                    .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .sensoryFeedback(.success, trigger: item.isDone) { _, done in done }
            .accessibilityLabel(item.isDone ? "Wieder öffnen" : "Als erledigt markieren")
            .accessibilityIdentifier("toggleWorkItem-\(item.title)")

            if let onOpen {
                Button(action: onOpen) {
                    HStack(alignment: .center, spacing: Space.s) {
                        details
                        Chevron()
                    }
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
            } else {
                details
            }
        }
    }

    /// „heute fällig“, „morgen fällig“, „überfällig seit 24. Okt.“, sonst „bis 30. Okt.“ – heute und überfällig rot.
    static func dueText(_ due: Date, done: Bool, now: Date = .now) -> (text: String, urgent: Bool) {
        let calendar = Calendar.current
        let date = due.formatted(.dateTime.day().month(.abbreviated))
        if done { return ("bis \(date)", false) }
        if calendar.isDate(due, inSameDayAs: now) { return ("heute fällig", true) }
        if due < calendar.startOfDay(for: now) { return ("überfällig seit \(date)", true) }
        if calendar.isDateInTomorrow(due) { return ("morgen fällig", false) }
        return ("bis \(date)", false)
    }

    /// Titel, darunter Art und Fälligkeit (überfällig rot), danach Bezug und Zuständige – streng linksbündig.
    private var details: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(item.title)
                .font(Typo.body)
                .strikethrough(item.isDone)
                .foregroundStyle(item.isDone ? Theme.textSecondary : Theme.textPrimary)
                .lineLimit(3)
            let due = item.dueDate.flatMap { CalendarDay.date($0) }
            if item.kind == .defect || due != nil {
                HStack(spacing: Space.s) {
                    if item.kind == .defect {
                        Chip(text: "Mangel", symbol: "exclamationmark.triangle.fill", tone: .warning)
                    }
                    if let due {
                        let label = Self.dueText(due, done: item.isDone)
                        Label(label.text, systemImage: "calendar")
                            .labelStyle(TightLabelStyle())
                            .font(Typo.metaStrong.monospacedDigit())
                            .foregroundStyle(label.urgent ? Theme.danger : Theme.textSecondary)
                    }
                }
            }
            let context = [showsBauakte, assigneeName].compactMap { $0 }.joined(separator: " · ")
            if !context.isEmpty {
                Text(context).font(Typo.meta).foregroundStyle(Theme.textSecondary).lineLimit(1)
            }
            SyncStateBadge(state: item.syncState)
        }
        .multilineTextAlignment(.leading)
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(.vertical, Space.m)
        .accessibilityElement(children: .combine)
    }
}

/// Neue Aufgabe oder neuer Mangel. Pflicht ist nur der Titel.
struct NewWorkItemView: View {
    let bauakteId: UUID?
    var initialKind: WorkItemKind = .task
    let onDone: () -> Void

    @Environment(AppModel.self) private var model
    @State private var kind: WorkItemKind = .task
    @State private var title = ""
    @State private var details = ""
    @State private var due: DueChoice = .none
    @State private var dueDate = Date.now
    @State private var assignee: UUID?
    @State private var people: [DirectoryUser] = []
    @State private var error: String?
    @FocusState private var titleFocused: Bool

    var body: some View {
        NavigationStack {
            Form {
                Picker("Art", selection: $kind) {
                    ForEach(WorkItemKind.allCases, id: \.self) { Text($0.title).tag($0) }
                }
                .pickerStyle(.segmented)
                .accessibilityIdentifier("workItemKindPicker")

                Section {
                    TextField(kind == .defect ? "Was ist mangelhaft?" : "Was ist zu tun?", text: $title, axis: .vertical)
                        .focused($titleFocused)
                        .accessibilityIdentifier("workItemTitleField")
                    TextField("Details (optional)", text: $details, axis: .vertical)
                        .lineLimit(2...6)
                }

                // Frist mit einem Tipp – die häufigsten Fristen als Kapseln, ein eigenes Datum nur bei Bedarf.
                Section("Frist") {
                    ChipFlow(spacing: Space.s) {
                        ForEach(DueChoice.allCases) { choice in
                            Button { withAnimation { due = choice } } label: {
                                SelectableChipLabel(title: choice.title, isSelected: due == choice)
                            }
                            .buttonStyle(.plain)
                            .accessibilityAddTraits(due == choice ? .isSelected : [])
                        }
                    }
                    .padding(.vertical, Space.xs)
                    .sensoryFeedback(.selection, trigger: due)
                    .accessibilityIdentifier("dueChoices")
                    if due == .custom {
                        DatePicker("Datum", selection: $dueDate, in: Calendar.current.startOfDay(for: .now)..., displayedComponents: .date)
                    }
                }

                Section {
                    Picker("Zuständig", selection: $assignee) {
                        Text("Ich").tag(model.active?.user.id)
                        Text("Niemand").tag(UUID?.none)
                        ForEach(people.filter { $0.isActive && $0.id != model.active?.user.id }) { person in
                            Text(person.displayName).tag(Optional(person.id))
                        }
                    }
                }

                if let error {
                    Text(error).foregroundStyle(Theme.danger).accessibilityIdentifier("workItemError")
                }
            }
            .navigationTitle(kind == .defect ? "Neuer Mangel" : "Neue Aufgabe")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Abbrechen", action: onDone) }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Sichern") { Task { await save() } }
                        .disabled(title.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                        .accessibilityIdentifier("saveWorkItemButton")
                }
            }
            .task {
                kind = initialKind
                assignee = model.active?.user.id
                titleFocused = true
                people = await DirectoryCache.load(model)
            }
        }
    }

    private func save() async {
        guard let service = model.active?.service else { return }
        do throws(BauakteError) {
            try await service.addWorkItem(NewWorkItem(kind: kind, title: title, description: details,
                                                      dueDate: due.date(custom: dueDate).map { CalendarDay.string($0) },
                                                      assigneeUserId: assignee, bauakteId: bauakteId))
            model.active?.sync?.localChangeMade()
            onDone()
        } catch {
            self.error = error.message
        }
    }
}

/// Frist einer neuen Aufgabe bzw. eines neuen Mangels.
enum DueChoice: String, CaseIterable, Identifiable {
    case none, today, tomorrow, threeDays, week, custom

    var id: String { rawValue }

    var title: String {
        switch self {
        case .none: "Ohne Frist"
        case .today: "Heute"
        case .tomorrow: "Morgen"
        case .threeDays: "In 3 Tagen"
        case .week: "In 1 Woche"
        case .custom: "Datum …"
        }
    }

    func date(custom: Date, now: Date = .now) -> Date? {
        let calendar = Calendar.current
        switch self {
        case .none: return nil
        case .today: return now
        case .tomorrow: return calendar.date(byAdding: .day, value: 1, to: now)
        case .threeDays: return calendar.date(byAdding: .day, value: 3, to: now)
        case .week: return calendar.date(byAdding: .day, value: 7, to: now)
        case .custom: return custom
        }
    }
}

/// Namen für „Zuständig“: online frisch vom Server, sonst der zuletzt gespeicherte Stand.
enum DirectoryCache {
    @MainActor
    static func load(_ model: AppModel) async -> [DirectoryUser] {
        guard let active = model.active, let service = active.service else { return [] }
        if let fresh = try? await active.client.directoryUsers() {
            try? await service.saveDirectory(fresh)
            return fresh
        }
        return (try? await service.cachedDirectory()) ?? []
    }
}
