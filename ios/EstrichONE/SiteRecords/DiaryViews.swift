import EstrichCore
import SwiftUI

/// Bautagebuch-Eintrag: Tag, was geschah, optional Wetter, Temperatur und Anwesende.
struct NewDiaryEntryView: View {
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
                }
                Section("Wetter (optional)") {
                    ScrollView(.horizontal, showsIndicators: false) {
                        HStack {
                            ForEach(DiaryWeather.allCases) { option in
                                Button {
                                    weather = weather == option ? nil : option
                                } label: {
                                    Label(option.title, systemImage: option.symbol)
                                        .padding(.horizontal, 10).padding(.vertical, 6)
                                        .background(weather == option ? Theme.navyBackground : Theme.neutralBackground, in: Capsule())
                                        .foregroundStyle(weather == option ? Theme.navyText : Theme.neutral)
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
                    Text(error).foregroundStyle(Theme.error)
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

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack(spacing: 8) {
                Text(CalendarDay.date(entry.day)?.formatted(date: .complete, time: .omitted) ?? entry.day)
                    .font(.subheadline.weight(.semibold))
                Spacer()
                if let weather = entry.weather {
                    Label(weather.title, systemImage: weather.symbol).labelStyle(.iconOnly)
                        .accessibilityLabel(weather.title)
                }
                if let temperature = entry.temperatureCelsius {
                    Text("\(temperature) °C").monospacedDigit()
                }
            }
            .foregroundStyle(Theme.textSecondary)
            Text(entry.text)
            HStack(spacing: 8) {
                if let authorName { Text(authorName) }
                if let workers = entry.workersPresent { Text("\(workers) anwesend") }
            }
            .font(.caption)
            .foregroundStyle(Theme.textSecondary)
            SyncStateBadge(state: entry.syncState)
        }
        .padding(.vertical, 2)
    }
}
