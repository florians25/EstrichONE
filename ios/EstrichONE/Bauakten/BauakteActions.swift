import EstrichCore
import SwiftUI

/// Was zu einer Bauakte erfasst bzw. bearbeitet wird – von der Übersicht, den Bereichen und dem Kopf aus gleich.
enum BauakteAction: String, Identifiable {
    case photo, video, scan, diary, defect, task, note, inspection, acceptance, measurement, project

    var id: String { rawValue }

    /// Kamera und Scanner erscheinen bildschirmfüllend, alles andere als Blatt.
    var isFullScreen: Bool { self == .photo || self == .scan }

    var title: String {
        switch self {
        case .photo: "Foto aufnehmen"
        case .video: "Video aufnehmen"
        case .scan: "Dokument scannen"
        case .diary: "Bautagebuch-Eintrag"
        case .defect: "Mangel melden"
        case .task: "Aufgabe anlegen"
        case .note: "Notiz schreiben"
        case .inspection: "Erstbegehung"
        case .acceptance: "Abnahme"
        case .measurement: "CM-Messung"
        case .project: "Basisdaten bearbeiten"
        }
    }

    /// Kurze Erläuterung im Blatt „Erfassen“.
    var subtitle: String {
        switch self {
        case .photo: "Serie mit Beschriftung"
        case .video: "Kurze Aufnahme"
        case .scan: "Lieferschein, Plan, Aufmaß"
        case .diary: "Eintrag für heute"
        case .defect: "Mit Frist und Foto"
        case .task: "Für Sie oder andere"
        case .note: "Für alle in der Bauakte"
        case .inspection: "Prüfpunkte vor Ort"
        case .acceptance: "Mit Unterschrift"
        case .measurement: "Belegreife prüfen"
        case .project: "Fläche, Estrich, Zugang"
        }
    }

    /// Bezeichner für Oberflächentests (ältere Bezeichner bleiben gültig).
    var captureIdentifier: String {
        switch self {
        case .photo: "capturePhoto"
        case .video: "captureVideo"
        case .scan: "scanDocumentButton"
        case .diary: "addDiaryButton"
        case .defect: "addDefectButton"
        default: "add-\(rawValue)"
        }
    }

    var symbol: String {
        switch self {
        case .photo: "camera"
        case .video: "video"
        case .scan: "doc.viewfinder"
        case .diary: "book"
        case .defect: "exclamationmark.triangle"
        case .task: "checklist.unchecked"
        case .note: "square.and.pencil"
        case .inspection: "checklist"
        case .acceptance: "signature"
        case .measurement: "drop"
        case .project: "pencil"
        }
    }
}

/// Öffnet die Erfassung zu einer Bauakte (Kamera, Scanner, Formulare) und meldet eine kurze Bestätigung zurück.
struct BauakteActionHost: ViewModifier {
    let bauakte: Bauakte
    var customerName: String?
    var schema: FormSchema?
    var lists = ValueLists([])
    @Binding var action: BauakteAction?
    /// Bestätigung („Foto gespeichert“) bzw. `nil`, wenn abgebrochen.
    let done: (String?) -> Void

    func body(content: Content) -> some View {
        content
            .fullScreenCover(item: Binding(get: { action?.isFullScreen == true ? action : nil }, set: { action = $0 })) { current in
                switch current {
                case .scan:
                    ScanFlowView(bauakte: bauakte) { saved in
                        action = nil
                        done(saved ? "Dokument gespeichert. Es wird übertragen, sobald eine Verbindung besteht." : nil)
                    }
                default:
                    CameraView(bauakte: bauakte) { count in
                        action = nil
                        done(count == 0 ? nil : (count == 1 ? "Foto gespeichert. Es wird übertragen, sobald eine Verbindung besteht."
                                                            : "\(count) Fotos gespeichert. Sie werden übertragen, sobald eine Verbindung besteht."))
                    }
                }
            }
            .sheet(item: Binding(get: { action?.isFullScreen == false ? action : nil }, set: { action = $0 })) { current in
                form(current)
            }
    }

    @ViewBuilder
    private func form(_ current: BauakteAction) -> some View {
        switch current {
        case .diary: NewDiaryEntryView(bauakteId: bauakte.id) { finish("Eintrag gespeichert") }
        case .defect: NewWorkItemView(bauakteId: bauakte.id, initialKind: .defect) { finish(nil) }
        case .task: NewWorkItemView(bauakteId: bauakte.id, initialKind: .task) { finish(nil) }
        case .note: NewNoteView(bauakteId: bauakte.id) { finish(nil) }
        case .inspection: InspectionEntryView(bauakte: bauakte) { finish(nil) }
        case .acceptance: NewAcceptanceView(bauakte: bauakte, customerName: customerName) { finish(nil) }
        case .measurement: NewCmMeasurementView(bauakteId: bauakte.id) { finish(nil) }
        case .video: VideoCaptureView(bauakte: bauakte) { saved in finish(saved ? "Video gespeichert" : nil) }
        case .project: EditProjectView(bauakte: bauakte, schema: schema, lists: lists) { finish(nil) }
        case .photo, .scan: EmptyView()
        }
    }

    private func finish(_ message: String?) {
        action = nil
        done(message)
    }
}

extension View {
    func bauakteActions(_ bauakte: Bauakte, customerName: String? = nil, schema: FormSchema? = nil, lists: ValueLists = ValueLists([]),
                        action: Binding<BauakteAction?>, done: @escaping (String?) -> Void) -> some View {
        modifier(BauakteActionHost(bauakte: bauakte, customerName: customerName, schema: schema, lists: lists, action: action, done: done))
    }
}

/// „Erfassen“ in der Bauakte: alles, was auf der Baustelle neu entsteht, in drei Gruppen mit kurzer Erläuterung.
/// Nur hier wird erfasst; abgeschaltete Module (Systemeinstellungen, Module) erscheinen nicht.
struct BauakteCaptureSheet: View {
    let bauakte: Bauakte
    let pick: (BauakteAction) -> Void
    let cancel: () -> Void

    @Environment(AppModel.self) private var model

    var body: some View {
        let modules = model.active?.sync?.modules ?? .allOn
        NavigationStack {
            List {
                Section {
                    VStack(alignment: .leading, spacing: 2) {
                        Text(bauakte.title)
                            .font(Typo.headline)
                            .foregroundStyle(Theme.textPrimary)
                            .fixedSize(horizontal: false, vertical: true)
                        if let line = [bauakte.number, bauakte.addressLine].compactMap({ $0 }).joined(separator: ", ").nonEmpty {
                            Text(line)
                                .font(Typo.subhead)
                                .foregroundStyle(Theme.textSecondary)
                                .fixedSize(horizontal: false, vertical: true)
                        }
                    }
                    .padding(.vertical, Space.xxs)
                    .accessibilityElement(children: .combine)
                } footer: {
                    Text("Alles wird zuerst auf dem Gerät gespeichert und übertragen, sobald eine Verbindung besteht.")
                }
                Section("Fotos und Dokumente") {
                    row(.photo)
                    row(.video)
                    row(.scan)
                }
                Section("Festhalten") {
                    if modules.isOn(AppModules.diary) { row(.diary) }
                    if modules.isOn(AppModules.defects) { row(.defect) }
                    if modules.isOn(AppModules.tasks) { row(.task) }
                    row(.note)
                }
                if modules.anyOn(AppModules.inspections, AppModules.acceptance) {
                    Section("Prüfen") {
                        if modules.isOn(AppModules.inspections) {
                            row(.inspection)
                            row(.measurement)
                        }
                        if modules.isOn(AppModules.acceptance) { row(.acceptance) }
                    }
                }
            }
            .listStyle(.insetGrouped)
            .listSectionSpacing(.compact)
            .navigationTitle("Erfassen")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Abbrechen", action: cancel) } }
        }
        .presentationDetents([.fraction(0.75), .large])
        .presentationDragIndicator(.visible)
        .accessibilityIdentifier("bauakteCaptureSheet")
    }

    private func row(_ action: BauakteAction) -> some View {
        Button { pick(action) } label: {
            Label {
                VStack(alignment: .leading, spacing: 1) {
                    Text(action.title).foregroundStyle(Theme.textPrimary)
                    Text(action.subtitle).font(Typo.subhead).foregroundStyle(Theme.textSecondary)
                }
            } icon: {
                Image(systemName: action.symbol).foregroundStyle(Theme.accent)
            }
            .frame(maxWidth: .infinity, minHeight: Size.minTap, alignment: .leading)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityIdentifier(action.captureIdentifier)
    }
}

private extension String {
    var nonEmpty: String? { isEmpty ? nil : self }
}
