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
                        done(saved ? "Dokument gespeichert – wird übertragen, sobald Netz da ist." : nil)
                    }
                default:
                    CameraView(bauakte: bauakte) { count in
                        action = nil
                        done(count == 0 ? nil : (count == 1 ? "Foto gespeichert – wird übertragen, sobald Netz da ist."
                                                            : "\(count) Fotos gespeichert – werden übertragen, sobald Netz da ist."))
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

/// Menü „Mehr erfassen“ mit allen Aktionen, die nicht als eigene Kachel erscheinen.
struct MoreCaptureMenu<MenuLabel: View>: View {
    let perform: (BauakteAction) -> Void
    @ViewBuilder let label: () -> MenuLabel

    var body: some View {
        Menu {
            Section {
                item(.inspection)
                item(.acceptance)
                item(.measurement)
            }
            Section {
                item(.video)
                item(.task)
                item(.note)
            }
        } label: {
            label()
        }
    }

    private func item(_ action: BauakteAction) -> some View {
        Button(action.title, systemImage: action.symbol) { perform(action) }
            .accessibilityIdentifier("add-\(action.rawValue)")
    }
}
