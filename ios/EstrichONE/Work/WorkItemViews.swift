import EstrichCore
import SwiftUI

/// Zeile einer Aufgabe bzw. eines Mangels mit Abhaken. Art und Zustand stehen immer auch als Text da (05, Abschnitt 2.3).
struct WorkItemRow: View {
    let item: WorkItem
    var assigneeName: String?
    var showsBauakte: String?
    /// Öffnet die zugehörige Bauakte (Startseite, „Meine Aufgaben“); dann mit Pfeil rechts.
    var onOpen: (() -> Void)?
    let onToggle: () -> Void

    var body: some View {
        HStack(alignment: .top, spacing: 10) {
            Button(action: onToggle) {
                Image(systemName: item.isDone ? "checkmark.circle.fill" : "circle")
                    .font(.title3)
                    .foregroundStyle(item.isDone ? Theme.success : Theme.textSecondary)
                    .frame(width: 44, height: 44)
            }
            .buttonStyle(.plain)
            .accessibilityLabel(item.isDone ? "Wieder öffnen" : "Als erledigt markieren")
            .accessibilityIdentifier("toggleWorkItem-\(item.title)")

            if let onOpen {
                Button(action: onOpen) {
                    HStack(alignment: .center, spacing: 8) {
                        details
                        Image(systemName: "chevron.right").font(.footnote.weight(.semibold)).foregroundStyle(.tertiary)
                    }
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
            } else {
                details
            }
        }
    }

    /// Titel, darunter Art und Fälligkeit (überfällig rot), danach Bezug und Zuständige grau – streng linksbündig.
    private var details: some View {
        VStack(alignment: .leading, spacing: 3) {
            Text(item.title)
                .font(.body)
                .strikethrough(item.isDone)
                .foregroundStyle(item.isDone ? Theme.textSecondary : Color.primary)
                .lineLimit(2)
            let due = item.dueDate.flatMap { CalendarDay.date($0) }
            if item.kind == .defect || due != nil {
                HStack(spacing: 6) {
                    if item.kind == .defect {
                        Text("Mangel")
                            .font(.caption2.weight(.bold))
                            .foregroundStyle(Theme.hint)
                            .padding(.horizontal, 6).padding(.vertical, 2)
                            .background(Theme.hintBackground, in: Capsule())
                    }
                    if let due {
                        Label(due.formatted(.dateTime.day().month(.abbreviated)), systemImage: "calendar")
                            .labelStyle(TightLabelStyle())
                            .font(.caption.monospacedDigit())
                            .foregroundStyle(item.isDue(on: CalendarDay.string(.now)) && !item.isDone ? Theme.error : Theme.textSecondary)
                    }
                }
            }
            let context = [showsBauakte, assigneeName].compactMap { $0 }.joined(separator: " · ")
            if !context.isEmpty {
                Text(context).font(.caption).foregroundStyle(Theme.textSecondary).lineLimit(1)
            }
            SyncStateBadge(state: item.syncState)
        }
        .multilineTextAlignment(.leading)
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(.vertical, 6)
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
    @State private var hasDueDate = false
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

                Section {
                    Toggle("Fällig am", isOn: $hasDueDate.animation())
                    if hasDueDate {
                        DatePicker("Datum", selection: $dueDate, displayedComponents: .date)
                    }
                    Picker("Zuständig", selection: $assignee) {
                        Text("Ich").tag(model.active?.user.id)
                        Text("Niemand").tag(UUID?.none)
                        ForEach(people.filter { $0.isActive && $0.id != model.active?.user.id }) { person in
                            Text(person.displayName).tag(Optional(person.id))
                        }
                    }
                }

                if let error {
                    Text(error).foregroundStyle(Theme.error).accessibilityIdentifier("workItemError")
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
                                                      dueDate: hasDueDate ? CalendarDay.string(dueDate) : nil,
                                                      assigneeUserId: assignee, bauakteId: bauakteId))
            model.active?.sync?.localChangeMade()
            onDone()
        } catch {
            self.error = error.message
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
