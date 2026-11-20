import EstrichCore
import ImageIO
import UIKit

/// Vorschaubilder: eigene Fotos aus der verschlüsselten Ablage, alle anderen als `thumb` bzw. `display` vom Server
/// (WebP). Erzeugt der Server die Vorschau gerade noch (`VARIANT_PENDING`), wird kurz gewartet und erneut gefragt;
/// gibt es keine Vorschau (`VARIANT_NOT_AVAILABLE`, z. B. HEIC ohne Decoder), wird das Original geladen und auf dem
/// Gerät verkleinert. Zwischengespeichert im Arbeitsspeicher und – nur die kleinen Vorschauen – verschlüsselt auf dem
/// Datenträger (`ThumbnailDiskCache`, 40 MB mit LRU): So bleiben Raster und Listen offline gefüllt, auch wenn das
/// Original nach dem Upload vom Gerät entfernt wurde. Nie unverschlüsselt auf dem Datenträger.
actor ThumbnailLoader {
    private let service: BauakteService
    private let client: AuthorizedClient
    private let disk: ThumbnailDiskCache?
    /// Nach so vielen neuen Vorschauen wird der Datenträger-Zwischenspeicher auf sein Limit gekürzt.
    private var storesSinceTrim = 0
    /// Threadsicher: Zugriff auch ohne Umweg über den Actor (sofortige Anzeige beim Scrollen).
    private let cache = ImageCache()
    /// Laufende Ladevorgänge: dasselbe Bild in Raster und Leiste wird nur einmal geladen.
    private var inFlight: [NSString: Task<UIImage?, Never>] = [:]
    /// Originale nur bis zu dieser Größe als Ersatz für fehlende Vorschauen laden.
    private let maxOriginalBytes: Int64 = 25 * 1024 * 1024

    init(service: BauakteService, client: AuthorizedClient, disk: ThumbnailDiskCache? = nil) {
        self.service = service
        self.client = client
        self.disk = disk
        if let disk { Task.detached(priority: .background) { _ = disk.trim() } }
    }

    /// „Cache leeren“: Arbeitsspeicher und Datenträger.
    func clear() {
        cache.removeAll()
        _ = disk?.clear()
    }

    /// Bereits geladenes Vorschaubild ohne Warten (oder `nil`).
    nonisolated func cachedThumbnail(for file: FileItem, size: CGFloat) -> UIImage? {
        cache.object(forKey: "\(file.id.uuidString)-\(Int(size))" as NSString)
    }

    func thumbnail(for file: FileItem, size: CGFloat) async -> UIImage? {
        let key = "\(file.id.uuidString)-\(Int(size))" as NSString
        if let cached = cache.object(forKey: key) { return cached }
        if let running = inFlight[key] { return await running.value }
        let task = Task { await loadThumbnail(for: file, size: size) }
        inFlight[key] = task
        let image = await task.value
        inFlight[key] = nil
        if let image { cache.setObject(image, forKey: key) }
        return image
    }

    private func loadThumbnail(for file: FileItem, size: CGFloat) async -> UIImage? {
        let name = "\(file.id.uuidString.lowercased())-\(Int(size))"
        let persists = size <= 200
        if persists, let disk {
            // Entschlüsseln außerhalb des Actors, damit mehrere Vorschauen parallel entstehen.
            if let image = await Task.detached(priority: .userInitiated, operation: { disk.read(name).flatMap { UIImage(data: $0) } }).value {
                return image
            }
        }
        var image: UIImage?
        if let data = service.localContent(of: file.id) {
            image = await Task.detached(priority: .userInitiated) { Self.downsample(data, to: size) }.value
        } else if file.isStoredOnServer, let data = await remote(file, variant: "thumb") {
            image = await Task.detached(priority: .userInitiated) { Self.downsample(data, to: size) }.value
        }
        if persists, let disk, let image {
            remember(image, as: name, in: disk)
        }
        return image
    }

    /// Kleine Vorschau verschlüsselt ablegen (JPEG, rund 20–40 KB) und gelegentlich auf das Limit kürzen.
    private func remember(_ image: UIImage, as name: String, in disk: ThumbnailDiskCache) {
        storesSinceTrim += 1
        let trim = storesSinceTrim >= 40
        if trim { storesSinceTrim = 0 }
        let data = image.jpegData(compressionQuality: 0.7)
        Task.detached(priority: .utility) {
            if let data { disk.store(data, for: name) }
            if trim { _ = disk.trim() }
        }
    }

    /// Großansicht: eigenes Original oder `display` vom Server (Ersatz: Original).
    func fullImage(for file: FileItem) async -> UIImage? {
        let key = "\(file.id.uuidString)-full" as NSString
        if let cached = cache.object(forKey: key) { return cached }
        var image: UIImage?
        if let data = service.localContent(of: file.id) {
            image = Self.downsample(data, to: 1600)
        } else if file.isStoredOnServer, let data = await remote(file, variant: "display") {
            image = Self.downsample(data, to: 1600)
        }
        if let image { cache.setObject(image, forKey: key) }
        return image
    }

    private func remote(_ file: FileItem, variant: String) async -> Data? {
        let path = "files/\(file.id.uuidString.lowercased())/content"
        for attempt in 0..<4 {
            do throws(APIError) {
                let data: Data = try await client.get(path, query: [URLQueryItem(name: "variant", value: variant)])
                if UIImage(data: data) != nil { return data }
                break
            } catch where error.code == "VARIANT_PENDING" {
                try? await Task.sleep(for: .seconds(Double(attempt + 1) * 2))
            } catch where error.code == "VARIANT_NOT_AVAILABLE" || error.code == "UNKNOWN_VARIANT" {
                break
            } catch {
                return nil
            }
        }
        guard file.sizeBytes <= maxOriginalBytes else { return nil }
        let original: Data? = try? await client.get(path)
        return original.flatMap { UIImage(data: $0) != nil ? $0 : nil }
    }

    /// Verkleinert beim Dekodieren (spart Speicher bei großen Fotos), beachtet die EXIF-Ausrichtung.
    static func downsample(_ data: Data, to size: CGFloat) -> UIImage? {
        let options: [CFString: Any] = [
            kCGImageSourceCreateThumbnailFromImageAlways: true,
            kCGImageSourceCreateThumbnailWithTransform: true,
            kCGImageSourceThumbnailMaxPixelSize: Int(size * 3),
        ]
        if let source = CGImageSourceCreateWithData(data as CFData, nil),
           let cgImage = CGImageSourceCreateThumbnailAtIndex(source, 0, options as CFDictionary) {
            return UIImage(cgImage: cgImage)
        }
        return UIImage(data: data)
    }
}

/// `NSCache` hinter einer Sendable-Hülle (NSCache ist threadsicher).
final class ImageCache: @unchecked Sendable {
    private let storage: NSCache<NSString, UIImage> = {
        let cache = NSCache<NSString, UIImage>()
        cache.countLimit = 300
        return cache
    }()

    func object(forKey key: NSString) -> UIImage? { storage.object(forKey: key) }
    func removeAll() { storage.removeAllObjects() }
    func setObject(_ image: UIImage, forKey key: NSString) { storage.setObject(image, forKey: key) }
}
