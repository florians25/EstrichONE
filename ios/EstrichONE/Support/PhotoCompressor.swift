import Foundation
import ImageIO
import UniformTypeIdentifiers

/// Fotos speicherschonend, aber scharf ablegen (passend zur Ablage auf dem Server): lange Kante höchstens 4096 px,
/// JPEG mit Qualität 0,8, Ausrichtung angewendet, Aufnahmedaten (EXIF, GPS, TIFF, IPTC) bleiben erhalten.
/// Kamerafotos der App (JPEG, 12 MP) liegen schon darunter und werden unverändert übernommen – kein zweites Komprimieren.
/// HEIC aus der Mediathek wird zu JPEG (für Web, Berichte und PDF überall lesbar).
enum PhotoCompressor {
    static let maxPixelLength = 4096
    static let quality = 0.8
    /// JPEGs bis zu dieser Größe und Kantenlänge bleiben unverändert.
    static let keepJPEGBelowBytes = 3_000_000

    static func prepare(_ data: Data) -> Data {
        guard let source = CGImageSourceCreateWithData(data as CFData, nil),
              let type = CGImageSourceGetType(source) as String?,
              let properties = CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [CFString: Any] else { return data }
        let longEdge = max(properties[kCGImagePropertyPixelWidth] as? Int ?? 0, properties[kCGImagePropertyPixelHeight] as? Int ?? 0)
        let isJPEG = type == UTType.jpeg.identifier
        if isJPEG, longEdge > 0, longEdge <= maxPixelLength, data.count <= keepJPEGBelowBytes { return data }

        let options: [CFString: Any] = [
            kCGImageSourceCreateThumbnailFromImageAlways: true,
            kCGImageSourceCreateThumbnailWithTransform: true,
            kCGImageSourceThumbnailMaxPixelSize: longEdge > 0 ? min(longEdge, maxPixelLength) : maxPixelLength,
        ]
        guard let image = CGImageSourceCreateThumbnailAtIndex(source, 0, options as CFDictionary) else { return data }
        let output = NSMutableData()
        guard let destination = CGImageDestinationCreateWithData(output as CFMutableData, UTType.jpeg.identifier as CFString, 1, nil) else {
            return data
        }
        var metadata: [CFString: Any] = [kCGImageDestinationLossyCompressionQuality: quality]
        for key in [kCGImagePropertyExifDictionary, kCGImagePropertyGPSDictionary, kCGImagePropertyIPTCDictionary] {
            if let value = properties[key] { metadata[key] = value }
        }
        // Die Drehung steckt jetzt in den Pixeln.
        var tiff = properties[kCGImagePropertyTIFFDictionary] as? [CFString: Any] ?? [:]
        tiff[kCGImagePropertyTIFFOrientation] = 1
        metadata[kCGImagePropertyTIFFDictionary] = tiff
        metadata[kCGImagePropertyOrientation] = 1
        CGImageDestinationAddImage(destination, image, metadata as CFDictionary)
        guard CGImageDestinationFinalize(destination) else { return data }
        let result = output as Data
        return isJPEG && longEdge <= maxPixelLength && result.count >= data.count ? data : result
    }
}
