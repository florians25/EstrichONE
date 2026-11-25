import EstrichCore
import SwiftUI

/// Bautagebuch-Eintrag: Tag, was geschah, optional Wetter, Temperatur und Anwesende. Schnell erfasst über
/// Textbausteine für die häufigsten Ereignisse auf der Estrichbaustelle oder per Diktat. Die Eingaben werden laufend als
/// Entwurf gesichert und nach einem Abbruch oder Neustart wieder angeboten.
struct NewDiaryEntryView: View {
    static let phrases = [
        "Estrich eingebracht", "Randdämmstreifen gesetzt", "Folie verlegt", "Aufheizen begonnen",
        "CM-Messung durchgeführt", "Baustelle nicht frei", "Wartezeit wegen Vorgewerk", "Material angeliefert",
    ]

    let bauakteId: UUID
    /// Vorschlag für den Text (z. B. „Angekommen um 07:42 Uhr.“); ein gespeicherter Entwurf hat Vorrang.
    var suggestedText = ""
    let onDone: () -> Void

    @Environment(AppModel.self) private var model
    @Environment(\.shotScene) private var shotScene
    @State private var keeper = DraftKeeper<DiaryDraft>(kind: DraftKind.diary)
    @State private var restoredAt: Date?
    @State private var day = Date.now
    @State private var text = ""
    @State private var weather: DiaryWeather?
    @State private var recordsTemperature = false
    @State private var temperature = 10
    @State private var workers = 0
    @State private var error: String?
    @FocusState private var focused: Bool

    var body: some View {
        NavigationStack {
            Form {
                if let restoredAt {
                    DraftRestoredNotice(savedAt: restoredAt) { discardDraft() }
                }
                DatePicker("Tag", selection: $day, in: ...Date.now, displayedComponents: .date)
                Section("Was ist passiert?") {
                    TextField("z. B. Estrich im EG eingebracht, 65 mm", text: $text, axis: .vertical)
                        .lineLimit(4...12)
                        .focused($focused)
                        .accessibilityIdentifier("diaryTextField")
                    DictationRow(text: $text)
                    // Alle Textbausteine auf einen Blick (umbrechend statt seitlich zu wischen), groß genug für Handschuhe.
                    ChipFlow(spacing: Space.s) {
                        ForEach(Self.phrases, id: \.self) { phrase in
                            Button { append(phrase) } label: {
                                SelectableChipLabel(title: phrase, symbol: "plus", isSelected: false)
                            }
                            .buttonStyle(.plain)
                        }
                    }
                    .padding(.vertical, Space.xs)
                    .accessibilityIdentifier("diaryPhrases")
                }
                Section("Wetter und Anwesende (optional)") {
                    ChipFlow(spacing: Space.s) {
                        ForEach(DiaryWeather.allCases) { option in
                            Button {
                                weather = weather == option ? nil : option
                            } label: {
                                SelectableChipLabel(title: option.title, symbol: option.symbol, isSelected: weather == option)
                            }
                            .buttonStyle(.plain)
                            .accessibilityAddTraits(weather == option ? .isSelected : [])
                        }
                    }
                    .padding(.vertical, Space.xs)
                    .sensoryFeedback(.selection, trigger: weather)
                    Toggle("Temperatur erfassen", isOn: $recordsTemperature.animation())
                    if recordsTemperature {
                        Stepper("\(temperature) °C", value: $temperature, in: -20...45)
                    }
                    Stepper(workers == 0 ? "Anwesende: keine Angabe" : "Anwesende: \(workers)", value: $workers, in: 0...50)
                }
                if let error {
                    StatusMessage(kind: .error, text: error)
                }
            }
            .navigationTitle("Bautagebuch")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Abbrechen", action: onDone) }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Sichern") { Task { await save() } }
                        .disabled(text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                        .accessibilityIdentifier("saveDiaryButton")
                }
            }
            .task {
                await restoreDraft()
                // Mit wiederhergestelltem Entwurf bleibt die Tastatur zu, damit der Hinweis zu sehen ist.
                focused = restoredAt == nil && shotScene != "tagebuch-diktat"
            }
            .autosavesDraft(keeper, value: draft)
            .keepsScreenAwake()
        }
    }

    private var draft: DiaryDraft {
        DiaryDraft(day: CalendarDay.string(day), text: text, weather: weather?.rawValue,
                   temperature: recordsTemperature ? temperature : nil, workers: workers)
    }

    private func restoreDraft() async {
        let stored = await keeper.restore(scope: bauakteId, model: model)
        #if DEBUG
        if shotScene == "tagebuch-entwurf" {
            text = "Estrich im EG eingebracht, 65 mm.\nRanddämmstreifen im Bad nachgesetzt."
            weather = .cloudy
            workers = 3
            restoredAt = Date.now.addingTimeInterval(-25 * 60)
            return
        }
        if shotScene != nil { return }
        #endif
        guard let stored, !stored.value.isEmpty else {
            if text.isEmpty { text = suggestedText }
            return
        }
        text = stored.value.text
        day = CalendarDay.date(stored.value.day) ?? day
        weather = stored.value.weather.flatMap(DiaryWeather.init(rawValue:))
        if let degrees = stored.value.temperature {
            recordsTemperature = true
            temperature = degrees
        }
        workers = stored.value.workers
        restoredAt = stored.savedAt
    }

    private func discardDraft() {
        text = suggestedText
        weather = nil
        recordsTemperature = false
        workers = 0
        day = .now
        restoredAt = nil
        Task { await keeper.discardRestored() }
    }

    /// Textbaustein anfügen: neuer Satz bzw. neue Zeile, falls schon Text da ist.
    private func append(_ phrase: String) {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        text = trimmed.isEmpty ? phrase : "\(trimmed)\n\(phrase)"
    }

    private func save() async {
        guard let service = model.active?.service else { return }
        do throws(SiteRecordError) {
            try await service.addDiaryEntry(NewDiaryEntry(day: CalendarDay.string(day), text: text, weather: weather,
                                                          temperatureCelsius: recordsTemperature ? temperature : nil,
                                                          workersPresent: workers == 0 ? nil : workers), to: bauakteId)
            model.active?.sync?.localChangeMade()
            await keeper.finish()
            onDone()
        } catch {
            self.error = error.message
        }
    }
}

struct DiaryRow: View {
    let entry: DiaryEntry
    var authorName: String?
    /// Datum in der Zeile, wenn kein Abschnittskopf es zeigt.
    var showsDate = false

    var body: some View {
        VStack(alignment: .leading, spacing: Space.s) {
            HStack(spacing: Space.s) {
                if showsDate {
                    Text(Self.dayTitle(entry.day)).font(Typo.subheadStrong).foregroundStyle(Theme.textSecondary)
                }
                if let weather = entry.weather {
                    Chip(text: weather.title, symbol: weather.symbol)
                }
                if let temperature = entry.temperatureCelsius {
                    Chip(text: "\(temperature) °C", symbol: "thermometer.medium")
                }
                Spacer(minLength: 0)
                SyncStateBadge(state: entry.syncState)
            }
            Text(entry.text.trimmingCharacters(in: .whitespacesAndNewlines))
                .font(Typo.body)
                .foregroundStyle(Theme.textPrimary)
                .fixedSize(horizontal: false, vertical: true)
            let people = entry.workersPresent.map { "\($0) \($0 == 1 ? "Person" : "Personen") vor Ort" }
            let meta = [authorName, people].compactMap { $0 }
            if !meta.isEmpty {
                Text(meta.joined(separator: ", ")).font(Typo.meta).foregroundStyle(Theme.textSecondary)
            }
        }
        .padding(.vertical, Space.xs)
        .accessibilityElement(children: .combine)
    }

    /// „Heute · Montag, 26. Oktober“, „Gestern · …“, sonst „Freitag, 23. Oktober“ (Jahr nur, wenn nicht das laufende).
    static func dayTitle(_ day: String, now: Date = .now) -> String {
        guard let date = CalendarDay.date(day) else { return day }
        let calendar = Calendar.current
        let sameYear = calendar.component(.year, from: date) == calendar.component(.year, from: now)
        let text = sameYear ? date.formatted(.dateTime.weekday(.wide).day().month(.wide))
                            : date.formatted(.dateTime.weekday(.wide).day().month(.wide).year())
        if calendar.isDateInToday(date) { return "Heute, \(text)" }
        if calendar.isDateInYesterday(date) { return "Gestern, \(text)" }
        return text
    }
}
