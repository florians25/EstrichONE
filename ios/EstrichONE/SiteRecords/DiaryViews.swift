import EstrichCore
import SwiftUI

/// Bautagebuch-Eintrag: Tag, was geschah, optional Wetter, Temperatur und Anwesende. Schnell erfasst über
/// Textbausteine für die häufigsten Ereignisse auf der Estrichbaustelle.
struct NewDiaryEntryView: View {
    static let phrases = [
        "Estrich eingebracht", "Randdämmstreifen gesetzt", "Folie verlegt", "Aufheizen begonnen",
        "CM-Messung durchgeführt", "Baustelle nicht frei", "Wartezeit wegen Vorgewerk", "Material angeliefert",
    ]

    let bauakteId: UUID
    let onDone: () -> Void

    @Environment(AppModel.self) private var model
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
                DatePicker("Tag", selection: $day, in: ...Date.now, displayedComponents: .date)
                Section("Was ist passiert?") {
                    TextField("z. B. Estrich im EG eingebracht, 65 mm", text: $text, axis: .vertical)
                        .lineLimit(4...12)
                        .focused($focused)
                        .accessibilityIdentifier("diaryTextField")
                    ScrollView(.horizontal, showsIndicators: false) {
                        HStack(spacing: 6) {
                            ForEach(Self.phrases, id: \.self) { phrase in
                                Button {
                                    append(phrase)
                                } label: {
                                    Label(phrase, systemImage: "plus")
                                }
                                .estrichButton(.secondary)
                            }
                        }
                        .padding(.vertical, 2)
                    }
                    .accessibilityIdentifier("diaryPhrases")
                }
                Section("Wetter (optional)") {
                    ScrollView(.horizontal, showsIndicators: false) {
                        HStack {
                            ForEach(DiaryWeather.allCases) { option in
                                Button {
                                    weather = weather == option ? nil : option
                                } label: {
                                    Label(option.title, systemImage: option.symbol)
                                        .labelStyle(TightLabelStyle())
                                        .font(.subheadline.weight(.medium))
                                        .padding(.horizontal, 12)
                                        .frame(minHeight: 36)
                                        .background(weather == option ? Theme.navyBackground : Theme.neutralBackground, in: Capsule())
                                        .foregroundStyle(weather == option ? Theme.navyText : Theme.neutral)
                                        .padding(.vertical, 4)
                                        .contentShape(Rectangle())
                                }
                                .buttonStyle(.plain)
                                .accessibilityAddTraits(weather == option ? .isSelected : [])
                            }
                        }
                    }
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
            .task { focused = true }
        }
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
                Text(meta.joined(separator: " · ")).font(Typo.meta).foregroundStyle(Theme.textSecondary)
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
        if calendar.isDateInToday(date) { return "Heute · \(text)" }
        if calendar.isDateInYesterday(date) { return "Gestern · \(text)" }
        return text
    }
}
