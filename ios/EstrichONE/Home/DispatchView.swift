import EstrichCore
import SwiftUI

/// „Wer fährt wohin“: alle Einsätze eines Tages aus der Planung, nach Kolonne gruppiert. Planen geschieht im Büro;
/// hier wird angezeigt. Ohne Verbindung bleibt der zuletzt geladene Stand des Tages sichtbar.
struct DispatchView: View {
    @Environment(AppModel.self) private var model
    @State private var day = Date.now
    @State private var data: DispatchData.Day?
    @State private var loading = false

    var body: some View {
        List {
            Section {
                HStack(spacing: 8) {
                    Button { shift(-1) } label: { Label("Vorheriger Tag", systemImage: "chevron.left") }
                        .buttonStyle(IconButtonStyle())
                    DatePicker("Tag", selection: $day, displayedComponents: .date)
                        .labelsHidden()
                        .frame(maxWidth: .infinity)
                    Button { shift(1) } label: { Label("Nächster Tag", systemImage: "chevron.right") }
                        .buttonStyle(IconButtonStyle())
                }
                if !Calendar.current.isDateInToday(day) {
                    Button("Heute") { day = .now }
                }
            }
            if let data, !data.isCurrent {
                Label("Ohne Verbindung – Stand \(data.loadedAt.formatted(date: .abbreviated, time: .shortened))", systemImage: "wifi.slash")
                    .font(.footnote).foregroundStyle(Theme.textSecondary)
            }
            if let message = data?.message {
                Text(message).foregroundStyle(Theme.textSecondary)
            } else if let data, data.assignments.isEmpty {
                ContentUnavailableView("Nichts eingeplant", systemImage: "calendar", description: Text("Für diesen Tag gibt es keine Einsätze."))
            }
            ForEach(DispatchData.groups(data?.assignments ?? []), id: \.title) { group in
                Section(group.title) {
                    ForEach(group.assignments) { assignment in
                        NavigationLink(value: HomeRoute.bauakte(assignment.bauakteId)) {
                            DispatchRow(assignment: assignment, isMine: assignment.people.contains { $0.id == model.active?.user.id })
                        }
                    }
                }
            }
        }
        .overlay { if loading && data == nil { ProgressView() } }
        .navigationTitle("Wer fährt wohin")
        .navigationBarTitleDisplayMode(.inline)
        .task(id: CalendarDay.string(day)) { await load() }
        .refreshable { await load() }
    }

    private func shift(_ days: Int) {
        day = Calendar.current.date(byAdding: .day, value: days, to: day) ?? day
    }

    private func load() async {
        loading = true
        defer { loading = false }
        data = await DispatchData.load(day: CalendarDay.string(day), model: model)
    }
}

/// Laden und Zwischenspeichern der Tagesplanung (auch für die Startseite).
enum DispatchData {
    struct Day: Equatable {
        let assignments: [Assignment]
        let loadedAt: Date
        let isCurrent: Bool
        /// Hinweis statt Liste (z. B. fehlendes Recht).
        let message: String?
    }

    struct Group {
        let title: String
        let assignments: [Assignment]
    }

    @MainActor
    static func load(day: String, model: AppModel) async -> Day? {
        guard let active = model.active else { return nil }
        let key = "dispatch_\(day)"
        do throws(APIError) {
            let range = try await active.client.planning(from: day, to: day)
            let assignments = range.assignments.sorted { ($0.startTime ?? "99") < ($1.startTime ?? "99") }
            if let encoded = try? JSONCoding.encoder.encode(assignments) {
                try? await active.sync?.store.setMeta(key, String(decoding: encoded, as: UTF8.self))
            }
            return Day(assignments: assignments, loadedAt: .now, isCurrent: true, message: nil)
        } catch {
            if error.status == 403 {
                return Day(assignments: [], loadedAt: .now, isCurrent: true, message: "Für die Planung fehlt Ihnen die Berechtigung.")
            }
            if let text = try? await active.sync?.store.meta(key), let cached = try? JSONCoding.decoder.decode([Assignment].self, from: Data(text.utf8)) {
                return Day(assignments: cached, loadedAt: .now, isCurrent: false, message: nil)
            }
            return Day(assignments: [], loadedAt: .now, isCurrent: false,
                       message: "Ohne Verbindung ist die Planung für diesen Tag nicht verfügbar.")
        }
    }

    /// Nach Kolonne gruppiert, Einsätze ohne Kolonne zuletzt.
    static func groups(_ assignments: [Assignment]) -> [Group] {
        let grouped = Dictionary(grouping: assignments) { $0.crewName ?? "" }
        return grouped.keys.sorted { lhs, rhs in
            if lhs.isEmpty != rhs.isEmpty { return !lhs.isEmpty }
            return lhs.localizedCompare(rhs) == .orderedAscending
        }.map { Group(title: $0.isEmpty ? "Ohne Kolonne" : $0, assignments: grouped[$0] ?? []) }
    }
}

struct DispatchRow: View {
    let assignment: Assignment
    var isMine = false

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            Text(assignment.startText ?? "–")
                .font(.subheadline.monospacedDigit().weight(.bold))
                .foregroundStyle(Theme.navyText)
                .frame(width: 46, alignment: .leading)
            VStack(alignment: .leading, spacing: 3) {
                HStack(spacing: 6) {
                    Text(assignment.bauakteTitle).font(.subheadline.weight(.semibold)).lineLimit(2)
                    if isMine {
                        Text("Ich").font(.caption2.weight(.bold)).foregroundStyle(Theme.onAccent)
                            .padding(.horizontal, 6).padding(.vertical, 1)
                            .background(Theme.accent, in: Capsule())
                    }
                }
                Text(assignment.activity).font(.footnote)
                if let address = assignment.bauakteAddress {
                    Label(address, systemImage: "mappin.and.ellipse").labelStyle(TightLabelStyle())
                        .font(.caption).foregroundStyle(Theme.textSecondary).lineLimit(1)
                }
                let people = assignment.people.map(\.name) + assignment.resources.map(\.name)
                if !people.isEmpty {
                    Label(people.joined(separator: ", "), systemImage: "person.2").labelStyle(TightLabelStyle())
                        .font(.caption).foregroundStyle(Theme.textSecondary).lineLimit(2)
                }
            }
        }
        .padding(.vertical, 2)
        .accessibilityElement(children: .combine)
    }
}
