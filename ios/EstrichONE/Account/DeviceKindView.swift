import EstrichCore
import SwiftUI

/// Schritt 3: Wie wird dieses Gerät genutzt? Die Auswahl ist ein Antrag, keine Freigabe (11, Abschnitt 2).
struct DeviceKindView: View {
    @Environment(AppModel.self) private var model
    @State private var selection: DeviceKind?

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: Theme.spacing) {
                    Text("Wie wird dieses Gerät genutzt?")
                        .font(.title2.weight(.semibold))
                        .accessibilityAddTraits(.isHeader)

                    option(.personal, title: "Persönliches Gerät", symbol: "person",
                           text: "Nur ich nutze dieses Gerät. Ich bleibe angemeldet und kann mit Face ID entsperren.")
                    option(.pool, title: "Gemeinsames Gerät (Poolgerät)", symbol: "person.3",
                           text: "Mehrere Mitarbeiter nutzen das Gerät, z. B. im Fahrzeug oder auf der Baustelle. Jeder meldet sich mit einer eigenen PIN an. Daten anderer sind nicht sichtbar.")

                    Text("Ein Poolgerät muss von der Administration freigegeben werden.")
                        .font(.footnote)
                        .foregroundStyle(Theme.textSecondary)

                    Button {
                        if let selection { model.chooseKind(selection) }
                    } label: {
                        ActionLabel("Weiter", systemImage: "arrow.right")
                    }
                    .primaryActionStyle()
                    .disabled(selection == nil)
                    .accessibilityIdentifier("deviceKindContinue")
                }
                .padding(24)
                .frame(maxWidth: 560)
                .frame(maxWidth: .infinity)
            }
            .navigationTitle("Gerät einrichten")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Anderer Server") { model.resetApp() }
                }
            }
        }
    }

    private func option(_ kind: DeviceKind, title: LocalizedStringKey, symbol: String, text: LocalizedStringKey) -> some View {
        let selected = selection == kind
        return Button {
            selection = kind
        } label: {
            HStack(alignment: .top, spacing: 14) {
                Image(systemName: selected ? "largecircle.fill.circle" : "circle")
                    .font(.title3)
                    .foregroundStyle(selected ? Theme.actionPrimary : Theme.textSecondary)
                    .accessibilityHidden(true)
                VStack(alignment: .leading, spacing: 4) {
                    Label(title, systemImage: symbol)
                        .font(.headline)
                        .foregroundStyle(.primary)
                    Text(text)
                        .font(.callout)
                        .foregroundStyle(Theme.textSecondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
                Spacer(minLength: 0)
            }
            .padding(16)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(Color(uiColor: .secondarySystemBackground), in: RoundedRectangle(cornerRadius: 12, style: .continuous))
            .overlay {
                RoundedRectangle(cornerRadius: 12, style: .continuous)
                    .strokeBorder(selected ? Theme.actionPrimary : Theme.line, lineWidth: selected ? 2 : 1)
            }
        }
        .buttonStyle(.plain)
        .accessibilityAddTraits(selected ? .isSelected : [])
        .accessibilityIdentifier(kind == .pool ? "kindPool" : "kindPersonal")
    }
}
