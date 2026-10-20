import CoreGraphics
import CoreText
import Foundation
import ImageIO
import UniformTypeIdentifiers
import Vision

/// Seite eines Scans: verkleinertes, komprimiertes JPEG.
struct ScanPage: Identifiable, Sendable, Equatable {
    let id: UUID
    let jpeg: Data
    let pixelWidth: Int
    let pixelHeight: Int
}

/// Erkannte Textzeile in normierten Koordinaten (0…1, Ursprung unten links wie in Vision und PDF).
struct RecognizedLine: Sendable, Equatable {
    let text: String
    let box: CGRect
}

/// Baut aus Scanseiten ein PDF: je Seite ein Bild in A4-Breite, optional mit unsichtbarer Textebene (durchsuchbar,
/// Text markierbar). Nur Core Graphics, ImageIO und Vision – läuft abseits des Hauptthreads.
enum ScanPDFBuilder {
    /// Längste Seite in Pixeln: ~250 dpi bei A4, gut lesbar, typischerweise 200–400 KB je Seite.
    static let maxPixelLength = 2_200
    static let jpegQuality = 0.62
    /// A4-Breite in Punkt.
    static let pageWidth: CGFloat = 595.28

    /// Verkleinert und komprimiert ein Seitenbild (beliebiges Format, Ausrichtung wird übernommen).
    static func preparePage(_ imageData: Data) -> ScanPage? {
        guard let source = CGImageSourceCreateWithData(imageData as CFData, nil) else { return nil }
        let options: [CFString: Any] = [
            kCGImageSourceCreateThumbnailFromImageAlways: true,
            kCGImageSourceCreateThumbnailWithTransform: true,
            kCGImageSourceThumbnailMaxPixelSize: maxPixelLength,
        ]
        guard let image = CGImageSourceCreateThumbnailAtIndex(source, 0, options as CFDictionary) else { return nil }
        let output = NSMutableData()
        guard let destination = CGImageDestinationCreateWithData(output as CFMutableData, UTType.jpeg.identifier as CFString, 1, nil) else { return nil }
        CGImageDestinationAddImage(destination, image, [kCGImageDestinationLossyCompressionQuality: jpegQuality] as CFDictionary)
        guard CGImageDestinationFinalize(destination) else { return nil }
        return ScanPage(id: UUID(), jpeg: output as Data, pixelWidth: image.width, pixelHeight: image.height)
    }

    /// Texterkennung (Deutsch, dann Englisch) für die Textebene. Leer, wenn nichts erkannt wurde.
    static func recognizeText(in page: ScanPage) -> [RecognizedLine] {
        let request = VNRecognizeTextRequest()
        request.recognitionLevel = .accurate
        request.usesLanguageCorrection = true
        request.recognitionLanguages = ["de-DE", "en-US"]
        let handler = VNImageRequestHandler(data: page.jpeg, options: [:])
        do {
            try handler.perform([request])
        } catch {
            return []
        }
        return (request.results ?? []).compactMap { observation in
            guard let candidate = observation.topCandidates(1).first, !candidate.string.isEmpty else { return nil }
            return RecognizedLine(text: candidate.string, box: observation.boundingBox)
        }
    }

    /// Erzeugt das PDF. `text[i]` gehört zu `pages[i]` (fehlende Einträge: keine Textebene).
    static func makePDF(pages: [ScanPage], text: [[RecognizedLine]] = [], title: String, author: String? = nil) -> Data? {
        guard !pages.isEmpty else { return nil }
        let output = NSMutableData()
        guard let consumer = CGDataConsumer(data: output as CFMutableData) else { return nil }
        var info: [CFString: Any] = [kCGPDFContextTitle: title, kCGPDFContextCreator: "EstrichONE"]
        if let author { info[kCGPDFContextAuthor] = author }
        var defaultBox = CGRect(x: 0, y: 0, width: pageWidth, height: pageWidth * 1.414)
        guard let context = CGContext(consumer: consumer, mediaBox: &defaultBox, info as CFDictionary) else { return nil }

        for (index, page) in pages.enumerated() {
            guard let provider = CGDataProvider(data: page.jpeg as CFData),
                  let image = CGImage(jpegDataProviderSource: provider, decode: nil, shouldInterpolate: true, intent: .defaultIntent)
            else { continue }
            let aspect = CGFloat(image.height) / CGFloat(max(image.width, 1))
            var box = CGRect(x: 0, y: 0, width: pageWidth, height: (pageWidth * aspect).rounded())
            let boxData = Data(bytes: &box, count: MemoryLayout<CGRect>.size)
            context.beginPDFPage([kCGPDFContextMediaBox: boxData] as CFDictionary)
            // Bild unverändert als JPEG eingebettet (keine erneute Kompression).
            context.draw(image, in: box)
            if index < text.count {
                drawInvisibleText(text[index], in: box, context: context)
            }
            context.endPDFPage()
        }
        context.closePDF()
        return output.length > 0 ? output as Data : nil
    }

    /// Unsichtbare Textebene: jede Zeile auf die Breite ihres Rahmens gestreckt, damit Suche und Markieren passen.
    private static func drawInvisibleText(_ lines: [RecognizedLine], in page: CGRect, context: CGContext) {
        context.saveGState()
        context.setTextDrawingMode(.invisible)
        for line in lines {
            let rect = CGRect(x: line.box.minX * page.width, y: line.box.minY * page.height,
                              width: line.box.width * page.width, height: line.box.height * page.height)
            guard rect.width > 1, rect.height > 1 else { continue }
            let font = CTFontCreateWithName("Helvetica" as CFString, max(rect.height * 0.85, 2), nil)
            let attributed = NSAttributedString(string: line.text, attributes: [NSAttributedString.Key(kCTFontAttributeName as String): font])
            let ctLine = CTLineCreateWithAttributedString(attributed)
            var descent: CGFloat = 0
            let width = CGFloat(CTLineGetTypographicBounds(ctLine, nil, &descent, nil))
            guard width > 0 else { continue }
            context.saveGState()
            context.translateBy(x: rect.minX, y: rect.minY + descent)
            context.scaleBy(x: rect.width / width, y: 1)
            context.textPosition = .zero
            CTLineDraw(ctLine, context)
            context.restoreGState()
        }
        context.restoreGState()
    }
}
