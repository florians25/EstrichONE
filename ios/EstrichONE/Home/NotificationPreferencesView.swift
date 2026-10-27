import EstrichCore
import SwiftUI

/// Benachrichtigungen je Ereignis abbestellen oder nur ins Postfach legen (docs/benachrichtigungen.md).
/// Pflicht-Ereignisse (Nachricht aus dem Büro, Wartung, Backup-Störungen) lassen sich nicht abschalten, nur die Pushnachricht.
struct NotificationPreferencesView: View {
    @Environment(AppModel.self) private var model
    @State private var items: [NotificationPreference] = []
    @State private var loaded = false
    @State private var failure: String?
    @State private var toast: String?

    var body: some View {
        List {
            if let failure {
                Section {
                    NoticeLine(tone: .neutral, text: failure, symbol: "exclamationmark.circle")
                        .listRowInsets(EdgeInsets())
                        .listRowBackground(Color.clear)
                }
            }
            ForEach(groups, id: \.self) { group in
                Section(group) {
                    ForEach(items.filter { $0.group == group }) { item in
                        row(item)
                    }
                }
            }
        }
        .listStyle(.insetGrouped)
        .overlay { if !loaded { ProgressView() } }
        .navigationTitle("Benachrichtigungsarten")
        .navigationBarTitleDisplayMode(.inline)
        .task { await load() }
        .toast($toast)
    }

    private var groups: [String] {
        var seen: [String] = []
        for item in items where !seen.contains(item.group) { seen.append(item.group) }
        return seen
    }

    private func row(_ item: NotificationPreference) -> some View {
        Picker(selection: Binding(get: { item.delivery }, set: { value in Task { await change(item, to: value) } })) {
            if !item.mandatory { Text("Aus").tag(NotificationPreference.Delivery.off) }
            Text("Nur Postfach").tag(NotificationPreference.Delivery.inbox)
            Text("Postfach und Push").tag(NotificationPreference.Delivery.push)
        } label: {
            VStack(alignment: .leading, spacing: 2) {
                Text(item.label)
                    .font(Typo.body)
                    .foregroundStyle(Theme.textPrimary)
                if let description = item.description, !description.isEmpty {
                    Text(item.mandatory ? "\(description) Pflicht – nur die Pushnachricht lässt sich abschalten." : description)
                        .font(Typo.meta)
                        .foregroundStyle(Theme.textSecondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
        }
        .pickerStyle(.menu)
        .accessibilityIdentifier("preference-\(item.event)")
    }

    private func load() async {
        guard let client = model.active?.client else { return }
        do throws(APIError) {
            items = try await client.notificationPreferences()
            failure = nil
        } catch {
            failure = error == .offline ? "Ohne Verbindung – die Auswahl lädt, sobald der Server erreichbar ist." : (error.serverMessage ?? error.message)
        }
        loaded = true
    }

    private func change(_ item: NotificationPreference, to delivery: NotificationPreference.Delivery) async {
        guard let client = model.active?.client, let index = items.firstIndex(where: { $0.event == item.event }) else { return }
        let previous = items[index].delivery
        items[index].delivery = delivery
        do throws(APIError) {
            let saved = try await client.saveNotificationPreferences([items[index]])
            if !saved.isEmpty { items = saved }
        } catch {
            items[index].delivery = previous
            toast = error.serverMessage ?? error.message
        }
    }
}
