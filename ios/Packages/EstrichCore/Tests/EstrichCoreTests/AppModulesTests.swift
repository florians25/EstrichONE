import Foundation
#if canImport(CryptoKit)
import CryptoKit
#else
import Crypto
#endif
import Testing
@testable import EstrichCore

@Suite("Module der Suite")
struct AppModulesTests {
    @Test("liest den Zustand vom Server; Kernmodule und unbekannte Schlüssel sind an")
    func decodes() throws {
        let json = """
        {"version":3,"modules":[
          {"key":"bauakten","area":"core","title":"Bauakten","description":"","enabled":true,"core":true,"requires":[],"requiredBy":[]},
          {"key":"staff","area":"hr","title":"Personal","description":"","enabled":false,"core":false,"requires":[],"requiredBy":["absence"]},
          {"key":"absence","area":"hr","title":"Urlaub und Abwesenheit","description":"","enabled":false,"core":false,"requires":["staff"],"requiredBy":[]},
          {"key":"tasks","area":"site","title":"Aufgaben","description":"","enabled":false,"core":false,"requires":[],"requiredBy":[]},
          {"key":"defects","area":"site","title":"Mängel","description":"","enabled":true,"core":false,"requires":[],"requiredBy":[]}
        ]}
        """
        let modules = try AppModules.decode(Data(json.utf8))
        #expect(modules.isOn("bauakten"))
        #expect(!modules.isOn(AppModules.absence))
        #expect(!modules.isOn(AppModules.staff))
        #expect(modules.isOn(AppModules.planning), "unbekannt bzw. älterer Server: eingeschaltet")
        #expect(modules.anyOn(AppModules.tasks, AppModules.defects), "Mängel noch an")
        #expect(!modules.anyOn(AppModules.tasks, AppModules.absence))
        #expect(AppModules.allOn.isOn(AppModules.helpdesk))
    }

    @Test("ohne Verbindung gilt der zuletzt gespeicherte Stand, ohne Stand alles an")
    func cachesOffline() async throws {
        let directory = FileManager.default.temporaryDirectory.appending(path: "modules-\(UUID().uuidString)")
        let store = LocalStore(database: try EncryptedDatabase(url: directory.appending(path: "data.sealed"), key: SymmetricKey(size: .bits256),
                                                                migrations: LocalStore.schema))
        let settings = ModuleSettingsStore(store: store)
        #expect(await settings.cached() == .allOn)
        #expect(await settings.load(using: nil) == .allOn)

        let off = AppModules(states: [AppModules.planning: false, AppModules.machines: false])
        let text = try #require(String(data: try JSONEncoder().encode(off), encoding: .utf8))
        try await store.setMeta(ModuleSettingsStore.metaKey, text)
        let cached = await settings.load(using: nil)
        #expect(!cached.isOn(AppModules.planning))
        #expect(cached.isOn(AppModules.materials))
    }
}
