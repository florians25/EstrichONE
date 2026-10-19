import Foundation
import Testing
@testable import EstrichCore

@Suite("Serveradresse")
struct ServerAddressTests {
    @Test("ergänzt https:// und entfernt abschließende Schrägstriche")
    func addsHttpsAndTrims() throws {
        let address = try ServerAddress.parse("  Estrichone.Firma.de/ \n")
        #expect(address.baseURL.absoluteString == "https://estrichone.firma.de")
    }

    @Test("behält Port und Pfadpräfix")
    func keepsPortAndPath() throws {
        let address = try ServerAddress.parse("https://firma.de:8443/estrichone/")
        #expect(address.baseURL.absoluteString == "https://firma.de:8443/estrichone")
        #expect(address.url(for: "api/v1/server-info").absoluteString == "https://firma.de:8443/estrichone/api/v1/server-info")
    }

    @Test("lehnt leere Eingabe ab")
    func rejectsEmpty() {
        #expect(throws: ServerAddressError.empty) { try ServerAddress.parse("   ") }
    }

    @Test("lehnt unverschlüsselte Adressen ab", arguments: ["http://estrichone.firma.de", "http://localhost:8080"])
    func rejectsHttp(input: String) {
        #expect(throws: ServerAddressError.insecure) { try ServerAddress.parse(input) }
    }

    @Test("erlaubt http nur für lokale Testserver in den Entwicklereinstellungen",
          arguments: ["http://localhost:8080", "http://127.0.0.1", "http://10.0.2.2:5000", "http://macbook.local"])
    func allowsLocalHttpInDevelopment(input: String) throws {
        let address = try ServerAddress.parse(input, allowInsecureLocal: true)
        #expect(address.baseURL.scheme == "http")
    }

    @Test("lehnt http für entfernte Server auch in den Entwicklereinstellungen ab")
    func rejectsRemoteHttpInDevelopment() {
        #expect(throws: ServerAddressError.insecure) { try ServerAddress.parse("http://estrichone.firma.de", allowInsecureLocal: true) }
    }

    @Test("lehnt Zugangsdaten, Parameter, Fragmente und fremde Schemata ab",
          arguments: ["https://admin:geheim@firma.de", "https://firma.de?x=1", "https://firma.de#start", "ftp://firma.de", "https://"])
    func rejectsInvalid(input: String) {
        #expect(throws: ServerAddressError.invalid) { try ServerAddress.parse(input) }
    }
}
