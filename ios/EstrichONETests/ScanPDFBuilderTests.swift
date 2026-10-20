import Foundation
import PDFKit
import Testing
import UIKit
@testable import EstrichONE

/// PDF aus Scanseiten (der Simulator hat keine Dokumentkamera; die Seiten entstehen hier als Testbilder).
@MainActor
@Suite("Dokumentenscan als PDF")
struct ScanPDFBuilderTests {
    /// Seite mit schwarzem Text auf Weiß, Hochformat wie ein Scan.
    private func pageImage(_ text: String, size: CGSize = CGSize(width: 1240, height: 1754)) -> Data {
        let renderer = UIGraphicsImageRenderer(size: size)
        let image = renderer.image { context in
            UIColor.white.setFill()
            context.fill(CGRect(origin: .zero, size: size))
            (text as NSString).draw(at: CGPoint(x: 100, y: 150), withAttributes: [.font: UIFont.boldSystemFont(ofSize: 96), .foregroundColor: UIColor.black])
            for line in 0..<20 {
                UIColor.darkGray.setFill()
                context.fill(CGRect(x: 100, y: 400 + line * 60, width: 900, height: 18))
            }
        }
        return image.pngData() ?? Data()
    }

    @Test("Seiten werden verkleinert und als JPEG komprimiert")
    func preparePage() throws {
        let large = pageImage("Lieferschein", size: CGSize(width: 3000, height: 4200))
        let page = try #require(ScanPDFBuilder.preparePage(large))
        #expect(max(page.pixelWidth, page.pixelHeight) == ScanPDFBuilder.maxPixelLength)
        #expect(page.jpeg.starts(with: [0xFF, 0xD8]), "JPEG")
        #expect(page.jpeg.count < large.count)
        #expect(ScanPDFBuilder.preparePage(Data("kein Bild".utf8)) == nil)
    }

    @Test("Mehrseitiges PDF in A4-Breite mit Titel und durchsuchbarer Textebene")
    func pdfWithTextLayer() throws {
        let pages = try [pageImage("Lieferschein 4711"), pageImage("Seite zwei")].map { try #require(ScanPDFBuilder.preparePage($0)) }
        let text: [[RecognizedLine]] = [
            [RecognizedLine(text: "Lieferschein 4711", box: CGRect(x: 0.08, y: 0.86, width: 0.6, height: 0.05)),
             RecognizedLine(text: "CT-C25-F4 Heizestrich 65 mm", box: CGRect(x: 0.08, y: 0.7, width: 0.7, height: 0.02))],
        ]

        let data = try #require(ScanPDFBuilder.makePDF(pages: pages, text: text, title: "Lieferschein 4711", author: "Max Huber"))

        #expect(data.starts(with: Array("%PDF".utf8)))
        let document = try #require(PDFDocument(data: data))
        #expect(document.pageCount == 2)
        let bounds = try #require(document.page(at: 0)).bounds(for: .mediaBox)
        #expect(abs(bounds.width - ScanPDFBuilder.pageWidth) < 1)
        #expect(abs(bounds.height / bounds.width - 1754.0 / 1240.0) < 0.01)
        #expect(document.page(at: 0)?.string?.contains("Lieferschein 4711") == true)
        #expect(document.page(at: 0)?.string?.contains("Heizestrich") == true)
        #expect(document.page(at: 1)?.string?.isEmpty ?? true, "Ohne erkannte Zeilen keine Textebene")
        #expect(!document.findString("CT-C25-F4", withOptions: .caseInsensitive).isEmpty)
        #expect(document.documentAttributes?[PDFDocumentAttribute.titleAttribute] as? String == "Lieferschein 4711")
        // Zwei Seiten mit eingebettetem JPEG bleiben klein.
        #expect(data.count < 2 * 600_000)
    }

    @Test("Ohne Seiten entsteht kein PDF")
    func empty() {
        #expect(ScanPDFBuilder.makePDF(pages: [], title: "Leer") == nil)
    }
}
