package de.estrichone.core

import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject

/**
 * Module der Suite (Systemeinstellungen › Module im Web). Abgeschaltete Module blendet die App aus: Einträge unter „Mehr“,
 * Kacheln auf „Heute“, Erfassen und Bereiche der Bauakte. Fehlende Schlüssel gelten als eingeschaltet (ältere Server).
 */
data class AppModules(val states: Map<String, Boolean> = emptyMap()) {
    fun isOn(key: String): Boolean = states[key] ?: true

    /** Mindestens eines der Module ist an (z. B. Aufgaben oder Mängel). */
    fun anyOn(vararg keys: String): Boolean = keys.any { isOn(it) }

    /** Für den Zwischenspeicher: „calls=false,planning=true“. */
    fun encode(): String = states.entries.joinToString(",") { "${it.key}=${it.value}" }

    companion object {
        const val CALLS = "calls"
        const val PLANNING = "planning"
        const val MACHINES = "machines"
        const val MAP = "map"
        const val MATERIALS = "materials"
        const val ORDERS = "orders"
        const val STAFF = "staff"
        const val ABSENCE = "absence"
        const val INSPECTIONS = "inspections"
        const val DIARY = "diary"
        const val ACCEPTANCE = "acceptance"
        const val TASKS = "tasks"
        const val DEFECTS = "defects"
        const val APPROVALS = "approvals"
        const val DOSSIERS = "dossiers"
        const val HELPDESK = "helpdesk"
        const val FEEDBACK = "feedback"

        val ALL_ON = AppModules()

        /** Zustand aus `GET settings/modules`; Kernmodule sind immer an. */
        fun parse(element: JsonElement?): AppModules {
            val list = ((element as? JsonObject)?.get("modules") as? JsonArray) ?: return ALL_ON
            return AppModules(list.mapNotNull { it as? JsonObject }.mapNotNull { module ->
                val key = module.str("key") ?: return@mapNotNull null
                key to (module.bool("core") == true || module.bool("enabled") != false)
            }.toMap())
        }

        /** Gegenstück zu [encode]; unlesbares wird ignoriert. */
        fun decode(text: String?): AppModules = AppModules(text.orEmpty().split(',').mapNotNull { pair ->
            val parts = pair.split('=')
            if (parts.size == 2 && parts[0].isNotBlank()) parts[0] to (parts[1] != "false") else null
        }.toMap())
    }
}

/** Modulzustand (für alle Angemeldeten lesbar). */
suspend fun EstrichOneApi.modules(): AppModules = AppModules.parse(send<JsonElement>("GET", "settings/modules", null))
