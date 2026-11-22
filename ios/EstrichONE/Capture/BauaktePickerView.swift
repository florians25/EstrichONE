import EstrichCore
import SwiftUI

/// Bauakte wählen (z. B. Zuordnung der Fotos in der Kamera ändern).
struct BauaktePickerView: View {
    let selected: UUID?
    let onSelect: (Bauakte) -> Void

    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var search = ""
    @State private var bauakten: [Bauakte] = []

    var body: some View {
        NavigationStack {
            List(bauakten) { bauakte in
                Button {
                    onSelect(bauakte)
                } label: {
                    HStack {
                        BauakteRow(bauakte: bauakte)
                        if bauakte.id == selected {
                            Image(systemName: "checkmark").foregroundStyle(Theme.accent).fontWeight(.semibold)
                        }
                    }
                }
                .buttonStyle(.plain)
            }
            .searchable(text: $search, placement: .navigationBarDrawer(displayMode: .always), prompt: "Nummer, Bauvorhaben, Kunde, Ort")
            .navigationTitle("Fotos zuordnen")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Abbrechen") { dismiss() } } }
            .task(id: search) { bauakten = (try? await model.active?.service?.bauakten(search: search)) ?? [] }
        }
    }
}
