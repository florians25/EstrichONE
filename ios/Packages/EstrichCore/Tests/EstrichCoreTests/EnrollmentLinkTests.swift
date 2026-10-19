import Foundation
import Testing
@testable import EstrichCore

@Suite("Einrichtungscode (QR)")
struct EnrollmentLinkTests {
    @Test("liest Server, Code und Gerätetyp")
    func parsesFullLink() throws {
        let link = try EnrollmentLink.parse("estrichone://enroll?v=1&server=https://estrichone.firma.de&code=k7qx-m2pa&type=pool")
        #expect(link.server.baseURL.absoluteString == "https://estrichone.firma.de")
        #expect(link.enrollmentCode == "K7QX-M2PA")
        #expect(link.deviceKind == .pool)
    }

    @Test("Code und Gerätetyp sind optional, unbekannte Parameter werden ignoriert")
    func optionalParameters() throws {
        let link = try EnrollmentLink.parse("estrichone://enroll?v=1&server=estrichone.firma.de&type=tablet&future=1")
        #expect(link.server.baseURL.absoluteString == "https://estrichone.firma.de")
        #expect(link.enrollmentCode == nil)
        #expect(link.deviceKind == nil)
    }

    @Test("erkennt fremde QR-Codes", arguments: ["https://estrichone.firma.de", "estrichone://login?v=1", "Hallo", "estrichone://enroll?v=1"])
    func rejectsForeignCodes(input: String) {
        #expect(throws: EnrollmentLinkError.notAnEstrichOneCode) { try EnrollmentLink.parse(input) }
    }

    @Test("verlangt Formatversion 1")
    func rejectsNewerVersion() {
        #expect(throws: EnrollmentLinkError.unsupportedVersion) {
            try EnrollmentLink.parse("estrichone://enroll?v=2&server=https://estrichone.firma.de")
        }
    }

    @Test("übernimmt die Prüfung der Serveradresse")
    func rejectsInsecureServer() {
        #expect(throws: EnrollmentLinkError.invalidServer(.insecure)) {
            try EnrollmentLink.parse("estrichone://enroll?v=1&server=http://estrichone.firma.de")
        }
    }
}
