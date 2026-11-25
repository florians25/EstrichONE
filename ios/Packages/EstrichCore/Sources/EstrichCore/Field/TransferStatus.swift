import Foundation

/// Was noch nicht beim Server ist, in Worten: „3 Fotos warten auf Übertragung“. Zählt Dateien nach ihrer Art und alle
/// übrigen Vorgänge als Einträge. Grundlage der ruhigen Statuszeile und der Seite „Synchronisation“.
public struct TransferQueue: Equatable, Sendable {
    public var photos = 0
    public var videos = 0
    public var documents = 0
    /// Übrige Vorgänge (Bautagebuch, Mängel, Messungen, Änderungen), ohne die Ankündigung von Dateien.
    public var entries = 0
    public var failedFiles = 0
    public var rejectedEntries = 0

    public init(photos: Int = 0, videos: Int = 0, documents: Int = 0, entries: Int = 0, failedFiles: Int = 0, rejectedEntries: Int = 0) {
        self.photos = photos
        self.videos = videos
        self.documents = documents
        self.entries = entries
        self.failedFiles = failedFiles
        self.rejectedEntries = rejectedEntries
    }

    public init(uploads: [PendingUpload], operations: [OutboxOperation]) {
        for upload in uploads {
            switch upload.state {
            case .done: continue
            case .failed: failedFiles += 1
            case .waiting, .uploading:
                switch Self.kind(ofContentType: upload.contentType) {
                case .photo: photos += 1
                case .video: videos += 1
                case .document: documents += 1
                }
            }
        }
        for operation in operations where operation.type != "file.announce" {
            if operation.state == .rejected { rejectedEntries += 1 } else { entries += 1 }
        }
    }

    public enum FileKind: Equatable, Sendable {
        case photo, video, document
    }

    public static func kind(ofContentType contentType: String) -> FileKind {
        let type = contentType.lowercased()
        if type.hasPrefix("image/") { return .photo }
        if type.hasPrefix("video/") { return .video }
        return .document
    }

    /// Anzahl der wartenden Dateien und Einträge.
    public var waitingCount: Int { photos + videos + documents + entries }
    public var isEmpty: Bool { waitingCount == 0 }
    public var hasProblems: Bool { failedFiles > 0 || rejectedEntries > 0 }

    /// „3 Fotos“, „1 Video“, „2 Dokumente“, „4 Einträge“ in dieser Reihenfolge.
    var parts: [(count: Int, text: String)] {
        [
            (photos, photos == 1 ? "1 Foto" : "\(photos) Fotos"),
            (videos, videos == 1 ? "1 Video" : "\(videos) Videos"),
            (documents, documents == 1 ? "1 Dokument" : "\(documents) Dokumente"),
            (entries, entries == 1 ? "1 Eintrag" : "\(entries) Einträge"),
        ].filter { $0.0 > 0 }.map { (count: $0.0, text: $0.1) }
    }

    /// „3 Fotos und 2 Einträge“; `nil`, wenn nichts wartet.
    public var summary: String? {
        let texts = parts.map(\.text)
        switch texts.count {
        case 0: return nil
        case 1: return texts[0]
        default: return texts.dropLast().joined(separator: ", ") + " und " + texts[texts.count - 1]
        }
    }

    /// „3 Fotos warten auf Übertragung“ bzw. „1 Foto wartet auf Übertragung“; `nil`, wenn nichts wartet.
    public var waitingText: String? {
        guard let summary else { return nil }
        return "\(summary) \(waitingCount == 1 ? "wartet" : "warten") auf Übertragung"
    }

    /// Während des Sendens: „3 Fotos werden übertragen“.
    public var sendingText: String? {
        guard let summary else { return nil }
        return "\(summary) \(waitingCount == 1 ? "wird" : "werden") übertragen"
    }

    /// „1 Datei konnte nicht übertragen werden“ bzw. „2 Einträge wurden nicht übernommen“; `nil` ohne Problem.
    public var problemText: String? {
        var texts: [String] = []
        if failedFiles > 0 {
            texts.append(failedFiles == 1 ? "1 Datei konnte nicht übertragen werden" : "\(failedFiles) Dateien konnten nicht übertragen werden")
        }
        if rejectedEntries > 0 {
            texts.append(rejectedEntries == 1 ? "1 Eintrag wurde nicht übernommen" : "\(rejectedEntries) Einträge wurden nicht übernommen")
        }
        return texts.isEmpty ? nil : texts.joined(separator: ", ")
    }
}

/// Verständliche Erklärung, warum eine Datei nicht übertragen wurde, und ob „Erneut senden“ hilft.
public enum UploadFailure {
    public static func message(code: String?) -> String {
        switch code {
        case "FILE_TOO_LARGE": "Die Datei ist größer, als der Server annimmt. Bitte kürzer aufnehmen oder im Büro nachfragen."
        case "TYPE_NOT_ALLOWED": "Dieser Dateityp wird vom Server nicht angenommen."
        case "LOCAL_FILE_MISSING": "Die Datei liegt nicht mehr auf diesem Gerät. Bitte erneut aufnehmen."
        case "CHECKSUM_MISMATCH": "Die Datei kam beschädigt an. Bitte erneut senden."
        case "FORBIDDEN", "BAUAKTE_NOT_ASSIGNED": "Für diese Bauakte fehlt die Berechtigung. Bitte im Büro nachfragen."
        default: "Die Übertragung wurde abgebrochen. Bitte erneut senden."
        }
    }

    /// Ob ein neuer Versuch sinnvoll ist (sonst hilft nur neu aufnehmen oder das Büro).
    public static func canRetry(code: String?) -> Bool {
        !["FILE_TOO_LARGE", "TYPE_NOT_ALLOWED", "LOCAL_FILE_MISSING"].contains(code ?? "")
    }
}
