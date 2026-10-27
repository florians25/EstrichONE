import EstrichCore
import SwiftUI

/// Poolgerät: „Wer arbeitet mit dem Gerät?“ (11, Abschnitt 4.1). Nur Namen, keine Daten.
struct UserPickerView: View {
    @Environment(AppModel.self) private var model
    @State private var confirmReset = false

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: Space.l) {
                    Text("Wer arbeitet mit dem Gerät?")
                        .font(.title2.weight(.semibold))
                        .accessibilityAddTraits(.isHeader)
                        .accessibilityIdentifier("userPickerTitle")

                    ForEach(model.deviceState.usersByRecency) { user in
                        Button {
                            model.selectUser(user)
                        } label: {
                            HStack(spacing: 14) {
                                Initials(text: user.initials)
                                VStack(alignment: .leading, spacing: 2) {
                                    Text(user.displayName).font(.headline).foregroundStyle(.primary)
                                    Group {
                                        if user.requiresPasswordLogin {
                                            Text("Anmeldung mit Passwort nötig")
                                        } else {
                                            Text("zuletzt \(user.lastUsedAt, format: .relative(presentation: .named))")
                                        }
                                    }
                                    .font(.footnote)
                                    .foregroundStyle(Theme.textSecondary)
                                }
                                Spacer()
                                Image(systemName: "chevron.right").foregroundStyle(Theme.textSecondary).accessibilityHidden(true)
                            }
                            .padding(14)
                            .frame(minHeight: 64)
                            .background(Color(uiColor: .secondarySystemBackground), in: RoundedRectangle(cornerRadius: 12, style: .continuous))
                        }
                        .buttonStyle(.plain)
                        .accessibilityIdentifier("userTile-\(user.userName)")
                    }

                    Button {
                        model.addPerson()
                    } label: {
                        ActionLabel("Andere Person …", systemImage: "person.badge.plus")
                    }
                    .secondaryActionStyle()
                    .accessibilityIdentifier("addPersonButton")
                }
                .padding(24)
                .frame(maxWidth: 560)
                .frame(maxWidth: .infinity)
            }
            .navigationTitle("EstrichONE · \(model.deviceRegistration?.name ?? "")")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .topBarTrailing) {
                    Menu {
                        Button("App zurücksetzen", role: .destructive) { confirmReset = true }
                    } label: {
                        Label("Einstellungen", systemImage: "gearshape")
                    }
                }
            }
            .confirmationDialog("App zurücksetzen?", isPresented: $confirmReset, titleVisibility: .visible) {
                Button("Alle Daten auf diesem Gerät löschen", role: .destructive) { model.resetApp() }
            } message: {
                Text("Die App vergisst Server, Gerät und alle Personen. Das Gerät muss danach neu eingerichtet werden.")
            }
        }
    }
}

struct Initials: View {
    let text: String

    var body: some View {
        Text(text.isEmpty ? "?" : text)
            .font(.headline)
            .foregroundStyle(.white)
            .frame(width: 44, height: 44)
            .background(Theme.navy, in: Circle())
            .accessibilityHidden(true)
    }
}
