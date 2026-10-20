import CoreLocation
import Foundation

/// Standort nur auf Knopfdruck (07, Abschnitt 3; Datenminimierung): einmal bestimmen, dann wieder aus.
@MainActor
final class LocationProvider: NSObject, CLLocationManagerDelegate {
    struct Address: Sendable {
        let street: String?
        let postalCode: String?
        let city: String?
    }

    enum Failure: Error {
        case denied
        case unavailable
    }

    private let manager = CLLocationManager()
    private var authorization: CheckedContinuation<CLAuthorizationStatus, Never>?

    override init() {
        super.init()
        manager.delegate = self
        manager.desiredAccuracy = kCLLocationAccuracyNearestTenMeters
    }

    /// Fragt beim ersten Mal nach der Erlaubnis und liefert einen aktuellen Standort (höchstens 15 Sekunden).
    func currentLocation() async throws(Failure) -> CLLocation {
        var status = manager.authorizationStatus
        if status == .notDetermined {
            status = await withCheckedContinuation { continuation in
                authorization = continuation
                manager.requestWhenInUseAuthorization()
            }
        }
        guard status == .authorizedWhenInUse || status == .authorizedAlways else { throw .denied }

        let result = await withTaskGroup(of: CLLocationCoordinate2D?.self) { group in
            group.addTask {
                do {
                    for try await update in CLLocationUpdate.liveUpdates(.default) {
                        if let location = update.location, location.horizontalAccuracy >= 0, location.horizontalAccuracy < 100 {
                            return location.coordinate
                        }
                    }
                } catch {}
                return nil
            }
            group.addTask {
                try? await Task.sleep(for: .seconds(15))
                return nil
            }
            let first = await group.next() ?? nil
            group.cancelAll()
            return first
        }
        guard let result else { throw .unavailable }
        return CLLocation(latitude: result.latitude, longitude: result.longitude)
    }

    /// Adresse zum Standort über den Systemdienst. Ohne Netz `nil` – dann bleiben nur die Koordinaten.
    func address(for location: CLLocation) async -> Address? {
        guard let placemark = try? await CLGeocoder().reverseGeocodeLocation(location, preferredLocale: Locale(identifier: "de_DE")).first else {
            return nil
        }
        let street = [placemark.thoroughfare, placemark.subThoroughfare].compactMap { $0 }.joined(separator: " ")
        return Address(street: street.isEmpty ? nil : street, postalCode: placemark.postalCode, city: placemark.locality)
    }

    nonisolated func locationManagerDidChangeAuthorization(_ manager: CLLocationManager) {
        let status = manager.authorizationStatus
        Task { @MainActor in
            guard status != .notDetermined, let continuation = self.authorization else { return }
            self.authorization = nil
            continuation.resume(returning: status)
        }
    }
}
