import AVFoundation
import Foundation
import Observation
import Speech

/// Diktat im Eingabefeld des Chats (statt Sprachnachrichten): Spracherkennung von Apple auf Deutsch, auf dem Gerät,
/// sobald das Gerät das kann (`requiresOnDeviceRecognition`). Der erkannte Text landet bearbeitbar im Feld.
/// Die Tonaufnahme bleibt ausschließlich im Arbeitsspeicher, wird nie gespeichert und nie gesendet.
@MainActor
@Observable
final class Dictation {
    enum Phase: Equatable {
        case idle
        case starting
        case listening
    }

    private(set) var phase: Phase = .idle
    /// Bisher erkannter Text dieses Diktats (wird laufend ersetzt).
    private(set) var transcript = ""
    /// Hinweis, wenn Diktat nicht möglich ist (Berechtigung, Sprache, kein Mikrofon).
    var problem: String?
    /// Erkennung läuft nur auf dem Gerät (keine Übertragung an Apple).
    private(set) var onDevice = false

    private var engine: AVAudioEngine?
    private var request: SFSpeechAudioBufferRecognitionRequest?
    private var task: SFSpeechRecognitionTask?
    private var stopTimer: Task<Void, Never>?
    private let recognizer = SFSpeechRecognizer(locale: Locale(identifier: "de-DE"))

    /// Neuer Text (Teilergebnis oder Endergebnis) – die Eingabezeile setzt ihn hinter den vorhandenen Text.
    var onTranscript: ((String) -> Void)?

    var isActive: Bool { phase != .idle }

    /// Gerät und Sprache unterstützen Diktat grundsätzlich.
    var isSupported: Bool { recognizer != nil }

    #if DEBUG
    /// Screenshot-Modus: Diktat im Gange vorführen (im Simulator gibt es kein Mikrofon).
    func simulateListening(_ text: String) {
        phase = .listening
        onDevice = true
        transcript = text
        onTranscript?(text)
    }
    #endif

    func toggle() {
        if isActive { stop() } else { Task { await start() } }
    }

    func start() async {
        guard phase == .idle else { return }
        problem = nil
        guard let recognizer else {
            problem = "Diktat ist auf diesem Gerät für Deutsch nicht verfügbar."
            return
        }
        phase = .starting
        guard await Self.requestSpeechPermission() else {
            phase = .idle
            problem = "Diktat braucht die Erlaubnis zur Spracherkennung (Einstellungen › EstrichONE)."
            return
        }
        guard await AVAudioApplication.requestRecordPermission() else {
            phase = .idle
            problem = "Diktat braucht das Mikrofon (Einstellungen › EstrichONE). Es wird nichts aufgezeichnet oder gesendet."
            return
        }
        guard recognizer.isAvailable else {
            phase = .idle
            problem = "Die Spracherkennung ist gerade nicht verfügbar. Bitte später erneut versuchen."
            return
        }
        do {
            try begin(with: recognizer)
            phase = .listening
        } catch {
            finish()
            problem = "Das Mikrofon lässt sich gerade nicht verwenden."
        }
    }

    /// Beendet das Diktat; der Text bleibt im Feld.
    func stop() {
        request?.endAudio()
        finish()
    }

    private func begin(with recognizer: SFSpeechRecognizer) throws {
        let session = AVAudioSession.sharedInstance()
        try session.setCategory(.record, mode: .measurement, options: [.duckOthers])
        try session.setActive(true, options: .notifyOthersOnDeactivation)

        let request = SFSpeechAudioBufferRecognitionRequest()
        request.shouldReportPartialResults = true
        request.addsPunctuation = true
        request.taskHint = .dictation
        onDevice = recognizer.supportsOnDeviceRecognition
        if onDevice { request.requiresOnDeviceRecognition = true }
        self.request = request
        transcript = ""

        let engine = AVAudioEngine()
        let input = engine.inputNode
        let format = input.outputFormat(forBus: 0)
        guard format.sampleRate > 0, format.channelCount > 0 else { throw DictationError.noMicrophone }
        Self.installTap(on: input, format: format, feeding: RequestBox(request))
        engine.prepare()
        try engine.start()
        self.engine = engine

        task = Self.recognize(with: recognizer, request: request, owner: self)
        // Sicherheitsgrenze: nach 2 Minuten automatisch beenden.
        stopTimer = Task { [weak self] in
            try? await Task.sleep(for: .seconds(120))
            guard !Task.isCancelled else { return }
            self?.stop()
        }
    }

    fileprivate func received(_ text: String, isFinal: Bool, failed: Bool) {
        guard phase != .idle else { return }
        if !text.isEmpty {
            transcript = text
            onTranscript?(text)
        }
        if isFinal || failed { finish() }
    }

    private func finish() {
        stopTimer?.cancel()
        stopTimer = nil
        if let engine {
            engine.stop()
            engine.inputNode.removeTap(onBus: 0)
        }
        engine = nil
        task?.cancel()
        task = nil
        request = nil
        phase = .idle
        try? AVAudioSession.sharedInstance().setActive(false, options: .notifyOthersOnDeactivation)
    }

    private enum DictationError: Error {
        case noMicrophone
    }

    /// Hält die Anfrage für den Audio-Thread (die Puffer gehen nur an die Erkennung, nirgends sonst hin).
    private final class RequestBox: @unchecked Sendable {
        let request: SFSpeechAudioBufferRecognitionRequest
        init(_ request: SFSpeechAudioBufferRecognitionRequest) { self.request = request }
    }

    /// Außerhalb des Main Actors: Der Abgriff läuft auf dem Audio-Thread.
    private nonisolated static func installTap(on input: AVAudioInputNode, format: AVAudioFormat, feeding box: RequestBox) {
        input.installTap(onBus: 0, bufferSize: 1024, format: format) { buffer, _ in
            box.request.append(buffer)
        }
    }

    private nonisolated static func recognize(with recognizer: SFSpeechRecognizer, request: SFSpeechAudioBufferRecognitionRequest,
                                              owner: Dictation) -> SFSpeechRecognitionTask {
        recognizer.recognitionTask(with: request) { result, error in
            let text = result?.bestTranscription.formattedString ?? ""
            let isFinal = result?.isFinal ?? false
            let failed = error != nil
            Task { @MainActor in
                owner.received(text, isFinal: isFinal, failed: failed)
            }
        }
    }

    private nonisolated static func requestSpeechPermission() async -> Bool {
        let status = SFSpeechRecognizer.authorizationStatus()
        if status == .authorized { return true }
        guard status == .notDetermined else { return false }
        return await withCheckedContinuation { continuation in
            SFSpeechRecognizer.requestAuthorization { status in
                continuation.resume(returning: status == .authorized)
            }
        }
    }
}
