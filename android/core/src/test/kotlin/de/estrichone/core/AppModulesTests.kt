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
