import EstrichCore
import SwiftUI
import UIKit

// MARK: Bildschirm bleibt an

/// Hält den Bildschirm an, solange erfasst wird (Kamera, Formulare, Unterschrift, Kundenansicht): Auf der Baustelle
/// soll das Gerät nicht mitten in der Eingabe abdunkeln oder sperren. Mehrere Ansichten können das gleichzeitig
/// verlangen; erst wenn die letzte verschwindet, gilt wieder die Einstellung des Systems.
@MainActor
final class ScreenAwake {
    static let shared = ScreenAwake()
    private var holders = 0

    func begin() {
        holders += 1
        UIApplication.shared.isIdleTimerDisabled = true
    }

    func end() {
        holders = max(0, holders - 1)
        if holders == 0 { UIApplication.shared.isIdleTimerDisabled = false }
    }
}

private struct KeepsScreenAwake: ViewModifier {
    func body(content: Content) -> some View {
        content
            .onAppear { ScreenAwake.shared.begin() }
            .onDisappear { ScreenAwake.shared.end() }
    }
}

extension View {
    /// Bildschirm bleibt an, solange diese Ansicht zu sehen ist.
    func keepsScreenAwake() -> some View {
        modifier(KeepsScreenAwake())
    }
}

// MARK: Diktat

/// Diktieren in ein Textfeld der Erfassung (Bautagebuch, Mangel, Notiz, Abnahme, Messung): große Schaltfläche unter dem
/// Feld, der erkannte Text wird an den vorhandenen angehängt und bleibt bearbeitbar. Die Erkennung läuft, wo das Gerät
/// es kann, auf dem Gerät; Tonaufnahmen werden nie gespeichert oder gesendet (wie im Chat).
struct DictationRow: View {
    @Binding var text: String
    var title = "Diktieren"
    /// Bezeichner für Oberflächentests.
    var identifier = "dictateButton"

    @Environment(\.shotScene) private var shotScene
    @State private var dictation = Dictation()
    @State private var base = ""

    var body: some View {
        if dictation.isSupported {
            VStack(alignment: .leading, spacing: Space.xxs) {
                Button { toggle() } label: {
                    Label(dictation.isActive ? "Diktat beenden" : title, systemImage: dictation.isActive ? "stop.circle.fill" : "mic")
                        .font(Typo.subheadStrong)
                        .foregroundStyle(dictation.isActive ? Theme.danger : Theme.accent)
                        .frame(maxWidth: .infinity, minHeight: Size.minTap, alignment: .leading)
                        .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .accessibilityIdentifier(identifier)
                if dictation.isActive {
                    Text(dictation.onDevice ? "Bitte sprechen. Die Erkennung läuft auf dem Gerät, es wird nichts aufgezeichnet."
                                            : "Bitte sprechen. Es wird nichts aufgezeichnet.")
                        .font(Typo.meta)
                        .foregroundStyle(Theme.textSecondary)
                        .fixedSize(horizontal: false, vertical: true)
                        .accessibilityIdentifier("dictationRunning")
                } else if let problem = dictation.problem {
                    Text(problem)
                        .font(Typo.meta)
                        .foregroundStyle(Theme.textSecondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            .onAppear {
                dictation.onTranscript = { transcript in text = base + transcript }
                #if DEBUG
                // Screenshot-Modus: laufendes Diktat vorführen (im Simulator gibt es kein Mikrofon).
                if shotScene == "tagebuch-diktat", !dictation.isActive {
                    base = ""
                    dictation.simulateListening("Estrich im Erdgeschoss eingebracht, 65 Millimeter. Randdämmstreifen im Bad nachgesetzt.")
                }
                #endif
            }
            .onDisappear { if dictation.isActive { dictation.stop() } }
        }
    }

    private func toggle() {
        if dictation.isActive {
            dictation.stop()
            return
        }
        let trimmed = text.trimmingCharacters(in: .whitespaces)
        base = trimmed.isEmpty ? "" : (text.hasSuffix(" ") || text.hasSuffix("\n") ? text : text + " ")
        // Tastatur schließen: Das Feld soll beim Sprechen sichtbar bleiben.
        UIApplication.shared.sendAction(#selector(UIResponder.resignFirstResponder), to: nil, from: nil, for: nil)
        dictation.toggle()
    }
}

// MARK: Entwürfe

/// Sichert die Eingaben eines Formulars laufend als Entwurf (kurz nach der letzten Änderung, beim Verlassen und wenn die
/// App in den Hintergrund geht) und bietet ihn beim nächsten Öffnen wieder an. Nach dem Sichern oder Verwerfen wird der
/// Entwurf entfernt. So geht bei einem Abbruch, einem Anruf oder einem Absturz nichts verloren.
@MainActor
final class DraftKeeper<Value: FieldDraft> {
    private let kind: String
    private var scope: UUID?
    private var store: DraftStore?
    private var isReady = false
    private var finished = false
    private var pending: Task<Void, Never>?
    private var latest: Value?

    init(kind: String) {
        self.kind = kind
    }

    /// Beim Öffnen des Formulars: gespeicherten Entwurf laden. Erst danach werden Änderungen gesichert.
    func restore(scope: UUID?, model: AppModel) async -> (value: Value, savedAt: Date)? {
        self.scope = scope
        defer { isReady = true }
        guard let localStore = model.active?.sync?.store else { return nil }
        let drafts = DraftStore(store: localStore)
        store = drafts
        return await drafts.load(Value.self, kind: kind, scope: scope)
    }

    /// Eingaben haben sich geändert: kurz danach sichern (die Datenbank wird bei jedem Schreiben neu verschlüsselt).
    func changed(_ value: Value) {
        guard isReady, !finished else { return }
        latest = value
        pending?.cancel()
        pending = Task { [weak self] in
            try? await Task.sleep(for: .milliseconds(1200))
            guard !Task.isCancelled else { return }
            await self?.write()
        }
    }

    /// Sofort sichern (Formular verschwindet, App geht in den Hintergrund).
    func flush() {
        guard isReady, !finished, latest != nil else { return }
        pending?.cancel()
        pending = Task { [weak self] in await self?.write() }
    }

    private func write() async {
        guard !finished, let latest, let store else { return }
        if latest.isEmpty {
            await store.discard(kind: kind, scope: scope)
        } else {
            await store.save(latest, kind: kind, scope: scope)
        }
    }

    /// Nach dem Sichern des Formulars oder wenn der Entwurf verworfen wird.
    func finish() async {
        finished = true
        pending?.cancel()
        await store?.discard(kind: kind, scope: scope)
    }

    /// Nach „Verwerfen“ des wiederhergestellten Entwurfs: weiter sichern, was neu eingegeben wird.
    func discardRestored() async {
        pending?.cancel()
        latest = nil
        await store?.discard(kind: kind, scope: scope)
    }
}

private struct DraftAutosave<Value: FieldDraft>: ViewModifier {
    let keeper: DraftKeeper<Value>
    let value: Value
    @Environment(\.scenePhase) private var scenePhase

    func body(content: Content) -> some View {
        content
            .onChange(of: value) { _, new in keeper.changed(new) }
            .onChange(of: scenePhase) { _, phase in
                if phase != .active { keeper.flush() }
            }
            .onDisappear { keeper.flush() }
    }
}

extension View {
    /// Eingaben laufend als Entwurf sichern (siehe `DraftKeeper`).
    func autosavesDraft<Value: FieldDraft>(_ keeper: DraftKeeper<Value>, value: Value) -> some View {
        modifier(DraftAutosave(keeper: keeper, value: value))
    }
}

/// Hinweis über einem Formular: Der Entwurf vom letzten Mal wurde wiederhergestellt, mit „Verwerfen“.
struct DraftRestoredNotice: View {
    let savedAt: Date
    let discard: () -> Void

    var body: some View {
        NoticeLine(tone: .info, text: "Entwurf von \(Self.stamp(savedAt)) wiederhergestellt.", symbol: "arrow.uturn.backward.circle.fill",
                   actionTitle: "Verwerfen", action: discard)
            .listRowInsets(EdgeInsets(top: Space.xs, leading: Space.xs, bottom: Space.xs, trailing: Space.xs))
            .listRowBackground(Color.clear)
            .accessibilityIdentifier("draftRestored")
    }

    /// „heute, 07:42 Uhr“, „gestern, 16:05 Uhr“, sonst „21.11., 16:05 Uhr“.
    static func stamp(_ date: Date, now: Date = .now) -> String {
        let calendar = Calendar.current
        let time = date.formatted(.dateTime.hour(.twoDigits(amPM: .omitted)).minute(.twoDigits)) + " Uhr"
        if calendar.isDate(date, inSameDayAs: now) { return "heute, \(time)" }
        if calendar.isDateInYesterday(date) { return "gestern, \(time)" }
        return date.formatted(.dateTime.day(.twoDigits).month(.twoDigits)) + ", \(time)"
    }
}
