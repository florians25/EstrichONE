import Foundation
import Testing
@testable import EstrichCore

/// Prüft die handgeschriebenen API-Typen gegen contracts/openapi/v1.json (ADR 0012).
@Suite("API-Vertrag")
struct ContractTests {
    private let schemas: [String: [String: Any]]

    init() throws {
        let url = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appending(path: "contracts/openapi/v1.json")
        let document = try #require(try JSONSerialization.jsonObject(with: Data(contentsOf: url)) as? [String: Any])
        let components = try #require(document["components"] as? [String: Any])
        schemas = try #require(components["schemas"] as? [String: [String: Any]])
    }

    /// Eigenschaften eines Beispielwerts: Name → optional?
    private func fields(of value: Any) -> [String: Bool] {
        Dictionary(uniqueKeysWithValues: Mirror(reflecting: value).children.compactMap { child in
            child.label.map { ($0, Mirror(reflecting: child.value).displayStyle == .optional) }
        })
    }

    private func schema(_ name: String) throws -> (properties: [String: [String: Any]], required: Set<String>) {
        let schema = try #require(schemas[name], "Schema \(name) fehlt im Vertrag")
        return (schema["properties"] as? [String: [String: Any]] ?? [:], Set(schema["required"] as? [String] ?? []))
    }

    private func isNullable(_ property: [String: Any]) -> Bool {
        if let types = property["type"] as? [String] { return types.contains("null") }
        return property["nullable"] as? Bool ?? false
    }

    /// Antworttypen: Jedes Swift-Feld existiert; nicht-optionale Felder sind im Vertrag Pflicht und nie `null`.
    private func checkResponse(_ name: String, sample: Any) throws {
        let (properties, required) = try schema(name)
        for (field, optional) in fields(of: sample) {
            let property = try #require(properties[field], "\(name).\(field) fehlt im Vertrag")
            if !optional {
                #expect(required.contains(field) && !isNullable(property), "\(name).\(field) kann laut Vertrag fehlen oder null sein")
            }
        }
    }

    /// Anfragetypen: Jedes Swift-Feld existiert; jedes Pflichtfeld, das nicht `null` sein darf, wird immer gesendet.
    private func checkRequest(_ name: String, sample: Any) throws {
        let (properties, required) = try schema(name)
        let swiftFields = fields(of: sample)
        for field in swiftFields.keys {
            #expect(properties[field] != nil, "\(name).\(field) fehlt im Vertrag")
        }
        for field in required where !isNullable(properties[field] ?? [:]) {
            #expect(swiftFields[field] == false, "Pflichtfeld \(name).\(field) wird nicht immer gesendet")
        }
    }

    @Test("Antworttypen")
    func responses() throws {
        let tokens = Samples.tokens()
        try checkResponse("TokenResponse", sample: tokens)
        try checkResponse("UserSummary", sample: tokens.user)
        try checkResponse("RefreshResponse", sample: try JSONCoding.decoder.decode(RefreshResponse.self, from: Data(Samples.refreshJSON(access: "a", refresh: "b").utf8)))
        try checkResponse("DeviceResponse", sample: try JSONCoding.decoder.decode(DeviceResponse.self, from: Data(Samples.deviceJSON(kind: "pool", status: "active").utf8)))
        try checkResponse("EnrollmentCodeResponse", sample: EnrollmentCodeResponse(code: "A", kind: .pool, deviceName: nil, expiresAt: Date(), link: nil))
        try checkResponse("ServerInfo", sample: ServerInfo(product: "EstrichONE", serverName: "x", serverVersion: "1", apiVersions: [],
                                                           minClientVersion: "1", authMethods: [], instanceEpoch: UUID(), setupRequired: false))
        try checkResponse("DirectoryUser", sample: DirectoryUser(id: UUID(), displayName: "a", isActive: true))
        try checkResponse("BauakteHistoryEntry", sample: try JSONCoding.decoder.decode(BauakteHistoryEntry.self, from: Data("""
            {"at":"2026-10-20T07:00:00.0000000+00:00","action":"bauakte.created","actorUserId":null,"actorName":null,"text":"x"}
            """.utf8)))
    }

    @Test("Anfragetypen")
    func requests() throws {
        try checkRequest("LoginRequest", sample: LoginRequest(login: "a", password: "b", client: "ios", clientName: nil))
        try checkRequest("RefreshRequest", sample: RefreshRequest(refreshToken: "a", deviceSignature: nil))
        try checkRequest("ChangePasswordRequest", sample: ChangePasswordRequest(currentPassword: "a", newPassword: "b"))
        try checkRequest("RegisterDeviceRequest", sample: RegisterDeviceRequest(name: "a", platform: "ios", kind: .pool, publicKey: "k",
                                                                                model: nil, osVersion: nil, appVersion: nil, enrollmentCode: nil))
        try checkRequest("CreateEnrollmentCodeRequest", sample: CreateEnrollmentCodeRequest(kind: .pool, deviceName: nil))
        try checkRequest("AttachDeviceRequest", sample: AttachDeviceRequest(deviceId: UUID(), signature: "s"))
    }

    @Test("Aufzählungswerte stimmen mit dem Vertrag überein")
    func enums() throws {
        let kinds = try #require(schemas["DeviceKind"]?["enum"] as? [String])
        #expect(Set(kinds) == Set(DeviceKind.allCases.map(\.rawValue)))
        let statuses = try #require(schemas["DeviceStatus"]?["enum"] as? [String])
        #expect(Set(statuses) == ["pending", "active", "locked", "retired"])
        let bauakteStatus = try #require(schemas["BauakteStatus"]?["enum"] as? [String])
        #expect(Set(bauakteStatus) == Set(BauakteStatus.allCases.map(\.rawValue)))
        // Aufgaben und Mängel laufen nur über den Sync (kein REST-Schema); ihre Werte prüft der Live-Test.
    }
}
