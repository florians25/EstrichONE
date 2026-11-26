package de.estrichone.core

import kotlinx.serialization.json.JsonElement
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.DisplayName
import org.junit.jupiter.api.Test

class AppModulesTests {
    private val response = """
        {"version":3,"modules":[
          {"key":"bauakten","area":"core","title":"Bauakten","description":"","enabled":true,"core":true,"requires":[],"requiredBy":[]},
          {"key":"staff","area":"hr","title":"Personal","description":"","enabled":false,"core":false,"requires":[],"requiredBy":["absence"]},
          {"key":"absence","area":"hr","title":"Urlaub und Abwesenheit","description":"","enabled":false,"core":false,"requires":["staff"],"requiredBy":[]},
          {"key":"tasks","area":"site","title":"Aufgaben","description":"","enabled":false,"core":false,"requires":[],"requiredBy":[]},
          {"key":"defects","area":"site","title":"Mängel","description":"","enabled":true,"core":false,"requires":[],"requiredBy":[]}
        ]}
    """.trimIndent()

    @Test
    @DisplayName("Module: Zustand vom Server, Kernmodule und unbekannte Schlüssel an")
    fun parses() {
        val modules = AppModules.parse(EstrichOneApi.json.decodeFromString(JsonElement.serializer(), response))
        assertTrue(modules.isOn("bauakten"))
        assertFalse(modules.isOn(AppModules.ABSENCE))
        assertFalse(modules.isOn(AppModules.STAFF))
        assertTrue(modules.isOn(AppModules.PLANNING), "älterer Server ohne Schlüssel: eingeschaltet")
        assertTrue(modules.anyOn(AppModules.TASKS, AppModules.DEFECTS))
        assertFalse(modules.anyOn(AppModules.TASKS, AppModules.ABSENCE))
        assertTrue(AppModules.parse(null).isOn(AppModules.HELPDESK))
    }

    @Test
    @DisplayName("Lexware Office: nur an, wenn der Server es meldet; Vorgabe für neue Kunden übersteht den Zwischenspeicher")
    fun lexware() {
        fun parse(enabled: Boolean, extra: String) = AppModules.parse(EstrichOneApi.json.decodeFromString(JsonElement.serializer(),
            """{"version":1,$extra"modules":[{"key":"lexware","area":"office","title":"Lexware Office","description":"","enabled":$enabled,"core":false,"requires":[],"requiredBy":[]}]}"""))
        assertFalse(AppModules.ALL_ON.lexwareOn, "älterer Server ohne das Modul: aus")
        assertFalse(AppModules.ALL_ON.lexwareSyncDefault)
        assertFalse(AppModules.parse(EstrichOneApi.json.decodeFromString(JsonElement.serializer(), response)).lexwareOn)
        val on = parse(true, """"lexwareSyncDefault":true,""")
        assertTrue(on.lexwareOn)
        assertTrue(on.lexwareSyncDefault)
        assertFalse(parse(true, """"lexwareSyncDefault":false,""").lexwareSyncDefault)
        assertFalse(parse(true, "").lexwareSyncDefault, "Feld fehlt: keine Vorgabe")
        val off = parse(false, """"lexwareSyncDefault":true,""")
        assertFalse(off.lexwareOn)
        assertFalse(off.lexwareSyncDefault, "Modul aus: keine Vorgabe")
        val restored = AppModules.decode(on.encode())
        assertEquals(on, restored)
        assertTrue(restored.lexwareSyncDefault)
    }

    @Test
    @DisplayName("Module: Zwischenspeicher für den Offline-Betrieb")
    fun cache() {
        val modules = AppModules(mapOf(AppModules.PLANNING to false, AppModules.MACHINES to false, AppModules.MATERIALS to true))
        val restored = AppModules.decode(modules.encode())
        assertEquals(modules, restored)
        assertFalse(restored.isOn(AppModules.PLANNING))
        assertTrue(AppModules.decode(null).isOn(AppModules.PLANNING))
        assertTrue(AppModules.decode("kaputt").isOn(AppModules.CALLS))
    }
}
