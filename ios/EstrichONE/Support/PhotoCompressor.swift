import EstrichCore
import Foundation
import ImageIO
import UniformTypeIdentifiers

/// Fotos speicherschonend, aber scharf ablegen (`MediaLimits`, docs/speicher.md): lange Kante höchstens 3000 px,
/// JPEG mit Qualität 0,78, Ausrichtung angewendet, Aufnahmedaten (EXIF mit Aufnahmezeit, GPS, TIFF, IPTC) bleiben
/// erhalten. Kamerafotos (12 MP, rund 3–4 MB) werden so zu rund 1–1,5 MB – noch vor dem Speichern auf dem Gerät und dem
/// Upload. Bereits kleine JPEGs bleiben unverändert (kein zweites Komprimieren). HEIC aus der Mediathek wird zu JPEG
/// (Web, Berichte und PDF überall lesbar; der Server erzeugt aus HEIC nicht zuverlässig Vorschaustufen).
enum PhotoCompressor {
    static var maxPixelLength: Int { MediaLimits.photoMaxEdge }
    static var quality: Double { MediaLimits.photoQuality }

    static func prepare(_ data: Data) -> Data {
        guard let source = CGImageSourceCreateWithData(data as CFData, nil),
              let type = CGImageSourceGetType(source) as String?,
              let properties = CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [CFString: Any] else { return data }
        let longEdge = max(properties[kCGImagePropertyPixelWidth] as? Int ?? 0, properties[kCGImagePropertyPixelHeight] as? Int ?? 0)
        let isJPEG = type == UTType.jpeg.identifier
        guard MediaLimits.photoNeedsReencoding(isJPEG: isJPEG, longEdge: longEdge, bytes: data.count) else { return data }

        let options: [CFString: Any] = [
            kCGImageSourceCreateThumbnailFromImageAlways: true,
            kCGImageSourceCreateThumbnailWithTransform: true,
            kCGImageSourceThumbnailMaxPixelSize: MediaLimits.photoTargetEdge(longEdge: longEdge),
        ]
        guard let image = CGImageSourceCreateThumbnailAtIndex(source, 0, options as CFDictionary) else { return data }
        let output = NSMutableData()
        guard let destination = CGImageDestinationCreateWithData(output as CFMutableData, UTType.jpeg.identifier as CFString, 1, nil) else {
            return data
        }
        var metadata: [CFString: Any] = [kCGImageDestinationLossyCompressionQuality: quality]
        for key in [kCGImagePropertyGPSDictionary, kCGImagePropertyIPTCDictionary] {
            if let value = properties[key] { metadata[key] = value }
        }
        // Aufnahmezeit, Kamera usw. bleiben; die Pixelmaße beschreiben das verkleinerte Bild.
        if var exif = properties[kCGImagePropertyExifDictionary] as? [CFString: Any] {
            exif[kCGImagePropertyExifPixelXDimension] = image.width
            exif[kCGImagePropertyExifPixelYDimension] = image.height
            metadata[kCGImagePropertyExifDictionary] = exif
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
