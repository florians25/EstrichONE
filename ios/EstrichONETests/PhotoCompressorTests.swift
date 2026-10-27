import ImageIO
import Testing
import UIKit
@testable import EstrichONE

@Suite("Fotos speicherschonend")
struct PhotoCompressorTests {
    private func image(width: Int, height: Int) -> UIImage {
        let format = UIGraphicsImageRendererFormat()
        format.scale = 1
        return UIGraphicsImageRenderer(size: CGSize(width: width, height: height), format: format).image { context in
            for index in 0..<40 {
                UIColor(hue: CGFloat(index) / 40, saturation: 0.6, brightness: 0.8, alpha: 1).setFill()
                context.fill(CGRect(x: index * width / 40, y: 0, width: width / 40, height: height))
            }
        }
    }

    private func longEdge(_ data: Data) -> Int {
        let source = CGImageSourceCreateWithData(data as CFData, nil)!
        let properties = CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as! [CFString: Any]
        return max(properties[kCGImagePropertyPixelWidth] as! Int, properties[kCGImagePropertyPixelHeight] as! Int)
    }

    @Test("Große Bilder auf 4096 px, PNG/HEIC zu JPEG; kleine Kamera-JPEGs bleiben unverändert")
    func limits() throws {
        let big = try #require(image(width: 6000, height: 3000).pngData())
        let prepared = PhotoCompressor.prepare(big)
        #expect(longEdge(prepared) == PhotoCompressor.maxPixelLength)
        #expect(prepared.starts(with: [0xFF, 0xD8]))

        let small = try #require(image(width: 1200, height: 900).jpegData(compressionQuality: 0.85))
        #expect(PhotoCompressor.prepare(small) == small)
    }
}
