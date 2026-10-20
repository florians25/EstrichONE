import EstrichCore
import UIKit

/// Vorschaubilder: eigene Fotos aus der verschlüsselten Ablage, alle anderen als `thumb` vom Server.
/// Zwischengespeichert nur im Arbeitsspeicher, nie unverschlüsselt auf dem Datenträger.
actor ThumbnailLoader {
    private let service: BauakteService
    private let client: AuthorizedClient
    private let cache = NSCache<NSString, UIImage>()

    init(service: BauakteService, client: AuthorizedClient) {
        self.service = service
        self.client = client
        cache.countLimit = 300
    }

    func thumbnail(for file: FileItem, size: CGFloat) async -> UIImage? {
        let key = "\(file.id.uuidString)-\(Int(size))" as NSString
        if let cached = cache.object(forKey: key) { return cached }
        var image: UIImage?
        if let data = service.localContent(of: file.id) {
            image = Self.downsample(data, to: size)
        } else if file.isStoredOnServer {
            let data: Data? = try? await client.get("files/\(file.id.uuidString.lowercased())/content",
                                                    query: [URLQueryItem(name: "variant", value: "thumb")])
            image = data.flatMap { Self.downsample($0, to: size) }
        }
        if let image { cache.setObject(image, forKey: key) }
        return image
    }

    /// Großansicht: eigenes Original oder `display` vom Server.
    func fullImage(for file: FileItem) async -> UIImage? {
        if let data = service.localContent(of: file.id) { return UIImage(data: data) }
        guard file.isStoredOnServer else { return nil }
        let data: Data? = try? await client.get("files/\(file.id.uuidString.lowercased())/content",
                                                query: [URLQueryItem(name: "variant", value: "display")])
        return data.flatMap(UIImage.init(data:))
    }

    private static func downsample(_ data: Data, to size: CGFloat) -> UIImage? {
        guard let image = UIImage(data: data) else { return nil }
        let scale = size * 2 / max(image.size.width, image.size.height, 1)
        guard scale < 1 else { return image }
        let target = CGSize(width: image.size.width * scale, height: image.size.height * scale)
        return image.preparingThumbnail(of: target) ?? image
    }
}
