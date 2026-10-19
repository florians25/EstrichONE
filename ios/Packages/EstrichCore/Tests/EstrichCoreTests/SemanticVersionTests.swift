import Testing
@testable import EstrichCore

@Suite("Versionsnummern")
struct SemanticVersionTests {
    @Test("liest verkürzte und erweiterte Schreibweisen")
    func parses() {
        #expect(SemanticVersion("1") == SemanticVersion(major: 1, minor: 0, patch: 0))
        #expect(SemanticVersion("1.2") == SemanticVersion(major: 1, minor: 2, patch: 0))
        #expect(SemanticVersion("1.2.3-beta+45") == SemanticVersion(major: 1, minor: 2, patch: 3))
    }

    @Test("lehnt ungültige Angaben ab", arguments: ["", "a.b", "1.2.3.4", "-1.0", "1..2", "1.2.", "v1.0", "1.٣"])
    func rejects(input: String) {
        #expect(SemanticVersion(input) == nil)
    }

    @Test("vergleicht numerisch, nicht alphabetisch")
    func comparesNumerically() throws {
        let older = try #require(SemanticVersion("1.9.0"))
        let newer = try #require(SemanticVersion("1.10.0"))
        #expect(older < newer)
    }
}
