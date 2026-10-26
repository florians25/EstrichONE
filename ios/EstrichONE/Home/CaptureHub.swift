import EstrichCore
import SwiftUI

/// Zentrales „Erfassen“: alles, was auf der Baustelle neu entsteht, an einer Stelle – Foto, Scan, Bautagebuch,
/// Erstbegehung, Abnahme, Mangel, neue Bauakte. Die Baustelle wird bei Bedarf gewählt (heutige zuerst).
struct CaptureHub: View {
    let onDone: () -> Void

    @Environment(AppModel.self) private var model
    @State private var cameraMode: CaptureMode?
    @State private var picking: SiteTask?
    @State private var todayIds: [UUID] = []
    @State private var diaryFor: Bauakte?
    @State private var inspectFor: Bauakte?
    @State private var acceptanceFor: Bauakte?
    @State private var defectFor: Bauakte?
    @State private var creating = false
    @State private var created: UUID?

    var body: some View {
        NavigationStack {
            List {
                Section {
                    row("Foto", "camera", id: "hubPhoto") { cameraMode = .photo }
                    row("Dokument scannen", "doc.viewfinder", id: "hubScan") { cameraMode = .document }
                }
                Section {
                    row("Bautagebuch", "book", id: "hubDiary") { picking = .diary }
                    row("Erstbegehung", "checklist", id: "hubInspection") { picking = .inspection }
                    row("Abnahme", "signature", id: "hubAcceptance") { picking = .acceptance }
                    row("Mangel melden", "exclamationmark.triangle", id: "hubDefect") { picking = .defect }
                }
                Section {
                    row("Neue Bauakte", "folder.badge.plus", id: "newBauakteTile") { creating = true }
                }
            }
            .navigationTitle("Erfassen")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Schließen", action: onDone) } }
            .task {
                if let store = model.active?.sync?.store {
                    todayIds = (await MyPlanning(store: store).cached())?.assignments(on: CalendarDay.string(.now)).map(\.bauakteId) ?? []
                }
            }
            .fullScreenCover(item: Binding(get: { cameraMode.map(CameraTarget.init) }, set: { cameraMode = $0?.mode })) { target in
                CaptureTabView(mode: Binding(get: { target.mode }, set: { cameraMode = $0 })) { cameraMode = nil }
            }
            .sheet(item: $picking) { task in
                BauakteQuickPicker(task: task, todayIds: todayIds) { bauakte in
                    picking = nil
                    guard let bauakte else { return }
                    Task {
                        try? await Task.sleep(for: .milliseconds(450))
                        switch task {
                        case .diary: diaryFor = bauakte
                        case .inspection: inspectFor = bauakte
                        case .acceptance: acceptanceFor = bauakte
                        case .defect: defectFor = bauakte
                        case .open: created = bauakte.id
                        }
                    }
                }
            }
            .sheet(item: $diaryFor) { bauakte in NewDiaryEntryView(bauakteId: bauakte.id) { diaryFor = nil } }
            .sheet(item: $inspectFor) { bauakte in InspectionEntryView(bauakte: bauakte) { inspectFor = nil } }
            .sheet(item: $acceptanceFor) { bauakte in NewAcceptanceView(bauakte: bauakte) { acceptanceFor = nil } }
            .sheet(item: $defectFor) { bauakte in NewWorkItemView(bauakteId: bauakte.id, initialKind: .defect) { defectFor = nil } }
            .sheet(isPresented: $creating) {
                NewBauakteView { id, _ in
                    creating = false
                    Task {
                        try? await Task.sleep(for: .milliseconds(450))
                        created = id
                    }
                }
            }
            .sheet(item: Binding(get: { created.map(OpenTarget.init) }, set: { created = $0?.id })) { target in
                NavigationStack {
                    BauakteDetailView(bauakteId: target.id)
                        .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Fertig") { created = nil } } }
                }
            }
        }
    }

    private struct CameraTarget: Identifiable {
        let mode: CaptureMode
        var id: String { "\(mode)" }
    }

    private struct OpenTarget: Identifiable {
        let id: UUID
    }

    private func row(_ title: String, _ symbol: String, id: String, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            HStack(spacing: 14) {
                Image(systemName: symbol)
                    .font(.title3)
                    .foregroundStyle(Theme.actionPrimary)
                    .frame(width: 32)
                    .accessibilityHidden(true)
                Text(title).font(.body.weight(.medium)).foregroundStyle(Color.primary)
                Spacer()
                Image(systemName: "chevron.right").font(.footnote.weight(.semibold)).foregroundStyle(.tertiary)
            }
            .frame(minHeight: 52)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityIdentifier(id)
    }
}
