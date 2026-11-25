import EstrichCore
import SwiftUI

/// Zusammenfassung der Abnahme vor dem Abschluss, zugleich die Ansicht für den Kunden: bildschirmfüllend und ruhig, nur
/// das Protokoll (Bauvorhaben, Leistung, Ergebnis, Mängel mit Frist, Notizen) und die Unterschriften. Reiter, Statuszeilen,
/// Chat und andere Bauakten sind nicht zu sehen, sodass das Gerät dem Kunden gezeigt oder in die Hand gegeben werden kann.
/// Der Auftraggeber unterschreibt hier mit dem Finger und nennt seinen Namen. Fehlt eine Pflichtangabe, steht das unten
/// und „Abnahme abschließen“ bleibt gesperrt.
struct AcceptanceReviewView: View {
    let review: AcceptanceReview
    @Binding var clientName: String
    @Binding var clientSignature: Data?
    @Binding var contractorSignature: Data?
    let isSaving: Bool
    let error: String?
    let onBack: () -> Void
    let onFinish: () -> Void

    @Environment(\.shotScene) private var shotScene
    @State private var signing: Side?

    private enum Side: String, Identifiable {
        case client, contractor
        var id: String { rawValue }
    }

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: Space.l) {
                    VStack(alignment: .leading, spacing: Space.xs) {
                        Text("Abnahmeprotokoll")
                            .font(Typo.title)
                            .foregroundStyle(Theme.textPrimary)
                            .accessibilityAddTraits(.isHeader)
                        Text(Date.now.formatted(.dateTime.weekday(.wide).day().month(.wide).year()))
                            .font(Typo.subhead)
                            .foregroundStyle(Theme.textSecondary)
                    }
                    Card(spacing: Space.m) {
                        ForEach(review.lines) { line in
                            VStack(alignment: .leading, spacing: 2) {
                                Text(line.title).font(Typo.metaStrong).foregroundStyle(Theme.textSecondary)
                                Text(line.value)
                                    .font(line.title == "Ergebnis" ? Typo.bodyStrong : Typo.body)
                                    .foregroundStyle(Theme.textPrimary)
                                    .fixedSize(horizontal: false, vertical: true)
                            }
                            .accessibilityElement(children: .combine)
                        }
                    }
                    if !review.defects.isEmpty {
                        Card(spacing: Space.m) {
                            Text("Mängel und Vorbehalte").font(Typo.headline).foregroundStyle(Theme.textPrimary)
                            ForEach(Array(review.defects.enumerated()), id: \.element.id) { index, defect in
                                HStack(alignment: .firstTextBaseline, spacing: Space.s) {
                                    Text("\(index + 1).").font(Typo.body.monospacedDigit()).foregroundStyle(Theme.textSecondary)
                                    VStack(alignment: .leading, spacing: 2) {
                                        Text(defect.title).font(Typo.body).foregroundStyle(Theme.textPrimary)
                                            .fixedSize(horizontal: false, vertical: true)
                                        if let due = defect.due {
                                            Text("Zu beheben \(due)").font(Typo.subhead).foregroundStyle(Theme.textSecondary)
                                        }
                                    }
                                }
                                .accessibilityElement(children: .combine)
                            }
                        }
                    }
                    if review.notes != nil || review.photoCount > 0 {
                        Card(spacing: Space.s) {
                            if let notes = review.notes {
                                Text("Notizen").font(Typo.headline).foregroundStyle(Theme.textPrimary)
                                Text(notes).font(Typo.body).foregroundStyle(Theme.textPrimary).fixedSize(horizontal: false, vertical: true)
                            }
                            if review.photoCount > 0 {
                                Label(review.photoCount == 1 ? "1 Foto gehört zum Protokoll" : "\(review.photoCount) Fotos gehören zum Protokoll",
                                      systemImage: "photo.on.rectangle")
                                    .labelStyle(TightLabelStyle())
                                    .font(Typo.subhead)
                                    .foregroundStyle(Theme.textSecondary)
                            }
                        }
                    }
                    Card(spacing: Space.m) {
                        Text("Unterschriften").font(Typo.headline).foregroundStyle(Theme.textPrimary)
                        signatureField("Auftraggeber", name: clientName, data: clientSignature, side: .client)
                        InsetDivider()
                        signatureField("Auftragnehmer", name: nil, data: contractorSignature, side: .contractor)
                    }
                    if !review.missing.isEmpty {
                        Callout(tone: .danger, title: "Vor dem Abschluss noch nötig", message: review.missing.joined(separator: "\n"))
                            .accessibilityIdentifier("acceptanceMissing")
                    } else if !review.hints.isEmpty {
                        Callout(tone: .neutral, title: "Hinweise", message: review.hints.joined(separator: "\n"), symbol: "info.circle")
                            .accessibilityIdentifier("acceptanceHints")
                    }
                    if let error {
                        StatusMessage(kind: .error, text: error).accessibilityIdentifier("acceptanceError")
                    }
                }
                .padding(Space.l)
                .frame(maxWidth: 640)
                .frame(maxWidth: .infinity)
            }
            .background(Theme.canvas)
            .navigationTitle("Zusammenfassung")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Bearbeiten", action: onBack).accessibilityIdentifier("editAcceptanceButton")
                }
            }
            .safeAreaInset(edge: .bottom) {
                Button(action: onFinish) {
                    ActionLabel("Abnahme abschließen", systemImage: "checkmark", isBusy: isSaving)
                }
                .primaryActionStyle()
                .disabled(isSaving || !review.canFinish)
                .accessibilityIdentifier("finishAcceptanceButton")
                .padding(.horizontal, Space.l)
                .padding(.top, Space.m)
                .padding(.bottom, Space.s)
                .background(.bar)
            }
            .sheet(item: $signing) { side in
                SignatureView(title: side == .client ? "Unterschrift Auftraggeber" : "Unterschrift Auftragnehmer",
                              signerName: side == .client ? $clientName : nil) { png in
                    signing = nil
                    guard let png else { return }
                    if side == .client { clientSignature = png } else { contractorSignature = png }
                }
            }
            .task {
                // Screenshot-Modus: Unterschrift des Kunden mit Namen zeigen.
                if shotScene == "abnahme-unterschrift" { signing = .client }
            }
        }
        .keepsScreenAwake()
    }

    /// Unterschriftsfeld: unterschrieben mit Bild, sonst eine große Schaltfläche zum Unterschreiben.
    private func signatureField(_ title: String, name: String?, data: Data?, side: Side) -> some View {
        VStack(alignment: .leading, spacing: Space.s) {
            HStack(alignment: .firstTextBaseline) {
                Text(title).font(Typo.bodyStrong).foregroundStyle(Theme.textPrimary)
                Spacer(minLength: Space.s)
                if data != nil {
                    Button("Neu unterschreiben") { signing = side }
                        .font(Typo.subheadStrong)
                        .foregroundStyle(Theme.accent)
                        .frame(minHeight: Size.minTap)
                        .contentShape(Rectangle())
                }
            }
            if let data, let image = UIImage(data: data) {
                Image(uiImage: image)
                    .resizable()
                    .scaledToFit()
                    .frame(maxWidth: .infinity, maxHeight: 110)
                    .padding(Space.s)
                    .background(Color.white, in: RoundedRectangle(cornerRadius: Radius.s, style: .continuous))
                    .overlay(RoundedRectangle(cornerRadius: Radius.s, style: .continuous).strokeBorder(Theme.separator))
                    .accessibilityLabel("\(title) hat unterschrieben")
                if let name = name?.trimmingCharacters(in: .whitespaces), !name.isEmpty {
                    Text(name).font(Typo.subhead).foregroundStyle(Theme.textSecondary)
                }
            } else {
                Button { signing = side } label: {
                    Label("Hier unterschreiben", systemImage: "signature")
                }
                .estrichButton(.secondary, size: .regular, fullWidth: true)
                .accessibilityIdentifier(side == .client ? "reviewSignClientButton" : "reviewSignContractorButton")
            }
        }
    }
}
