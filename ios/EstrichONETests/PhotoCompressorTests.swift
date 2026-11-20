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

    @Test("Große Bilder auf 3000 px, PNG/HEIC zu JPEG; kleine JPEGs bleiben unverändert")
    func limits() throws {
        let big = try #require(image(width: 6000, height: 3000).pngData())
        let prepared = PhotoCompressor.prepare(big)
        #expect(longEdge(prepared) == 3000)
        #expect(longEdge(prepared) == PhotoCompressor.maxPixelLength)
        #expect(prepared.starts(with: [0xFF, 0xD8]))

        let small = try #require(image(width: 1200, height: 900).jpegData(compressionQuality: 0.85))
        #expect(PhotoCompressor.prepare(small) == small)
    }

    @Test("Kamerafoto 12 MP: auf 3000 px verkleinert, Aufnahmezeit und GPS bleiben erhalten")
    func keepsCaptureMetadata() throws {
        let cgImage = try #require(image(width: 4032, height: 3024).cgImage)
        let output = NSMutableData()
        let destination = try #require(CGImageDestinationCreateWithData(output as CFMutableData, "public.jpeg" as CFString, 1, nil))
        let metadata: [CFString: Any] = [
            kCGImageDestinationLossyCompressionQuality: 0.85,
            kCGImagePropertyExifDictionary: [kCGImagePropertyExifDateTimeOriginal: "2026:11:20 07:15:00"] as [CFString: Any],
            kCGImagePropertyGPSDictionary: [kCGImagePropertyGPSLatitude: 48.5, kCGImagePropertyGPSLatitudeRef: "N",
                                            kCGImagePropertyGPSLongitude: 12.1, kCGImagePropertyGPSLongitudeRef: "E"] as [CFString: Any],
        ]
        CGImageDestinationAddImage(destination, cgImage, metadata as CFDictionary)
        #expect(CGImageDestinationFinalize(destination))

        let prepared = PhotoCompressor.prepare(output as Data)
        #expect(longEdge(prepared) == 3000)
        let source = try #require(CGImageSourceCreateWithData(prepared as CFData, nil))
        let properties = try #require(CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [CFString: Any])
        let exif = properties[kCGImagePropertyExifDictionary] as? [CFString: Any]
        #expect(exif?[kCGImagePropertyExifDateTimeOriginal] as? String == "2026:11:20 07:15:00")
        #expect(exif?[kCGImagePropertyExifPixelXDimension] as? Int == 3000)
        let gps = properties[kCGImagePropertyGPSDictionary] as? [CFString: Any]
        #expect((gps?[kCGImagePropertyGPSLatitude] as? Double).map { abs($0 - 48.5) < 0.001 } == true)
    }
}
