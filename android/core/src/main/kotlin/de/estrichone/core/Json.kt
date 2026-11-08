package de.estrichone.core

import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.doubleOrNull
import kotlinx.serialization.json.longOrNull
import java.security.SecureRandom
import java.time.Instant
import java.time.LocalDate
import java.time.OffsetDateTime
import java.time.format.DateTimeFormatter
import java.util.UUID

// Zugriff auf JSON-Datensätze des Änderungsfeeds (wie JSONValue in EstrichCore): fehlende oder falsche Typen → null.

fun JsonObject.str(key: String): String? = (this[key] as? JsonPrimitive)?.takeIf { it.isString }?.content

fun JsonObject.num(key: String): Double? = (this[key] as? JsonPrimitive)?.takeIf { !it.isString }?.doubleOrNull

fun JsonObject.long(key: String): Long? = (this[key] as? JsonPrimitive)?.takeIf { !it.isString }?.let { it.longOrNull ?: it.doubleOrNull?.toLong() }

fun JsonObject.bool(key: String): Boolean? = (this[key] as? JsonPrimitive)?.takeIf { !it.isString }?.booleanOrNull

fun JsonObject.obj(key: String): JsonObject? = this[key] as? JsonObject

fun JsonObject.arr(key: String): JsonArray? = this[key] as? JsonArray

fun JsonObject.strings(key: String): List<String> = arr(key)?.mapNotNull { (it as? JsonPrimitive)?.takeIf { p -> p.isString }?.content } ?: emptyList()

/** Text eines JSON-Werts für die Anzeige (Zahlen ohne „.0“). */
fun JsonElement?.display(): String? = when (this) {
    null, JsonNull -> null
    is JsonPrimitive -> if (isString) content else doubleOrNull?.let { formatNumber(it) } ?: booleanOrNull?.let { if (it) "Ja" else "Nein" } ?: content
    else -> toString()
}

fun formatNumber(value: Double, decimals: Int = 2): String {
    val rounded = java.math.BigDecimal(value).setScale(decimals, java.math.RoundingMode.HALF_UP).stripTrailingZeros()
    return rounded.toPlainString().replace('.', ',')
}

/** Einfacher Aufbau von JSON-Objekten: `json("a" to 1, "b" to null)`. */
fun json(vararg pairs: Pair<String, Any?>): JsonObject = JsonObject(pairs.associate { (k, v) -> k to toJson(v) })

@Suppress("UNCHECKED_CAST")
fun toJson(value: Any?): JsonElement = when (value) {
    null -> JsonNull
    is JsonElement -> value
    is String -> JsonPrimitive(value)
    is Number -> JsonPrimitive(value)
    is Boolean -> JsonPrimitive(value)
    is Map<*, *> -> JsonObject((value as Map<String, Any?>).mapValues { toJson(it.value) })
    is Iterable<*> -> JsonArray(value.map { toJson(it) })
    is Array<*> -> JsonArray(value.map { toJson(it) })
    else -> JsonPrimitive(value.toString())
}

fun JsonObject.merged(other: Map<String, JsonElement>): JsonObject = JsonObject(this.toMap() + other)

/** Zeitpunkte der API: ISO 8601 mit Zeitzone. */
object ApiDate {
    fun parse(value: String?): Instant? = value?.let { runCatching { OffsetDateTime.parse(it).toInstant() }.getOrNull() ?: runCatching { Instant.parse(it) }.getOrNull() }

    fun format(instant: Instant): String = DateTimeFormatter.ISO_INSTANT.format(instant)

    fun day(value: String?): LocalDate? = value?.let { runCatching { LocalDate.parse(it.take(10)) }.getOrNull() }
}

/** UUIDv7 (RFC 9562): vom Client vergebene IDs (ADR 0006), in Kleinbuchstaben wie der Server. */
object Ids {
    private val random = SecureRandom()

    fun v7(now: Long = System.currentTimeMillis()): String {
        val bytes = ByteArray(16)
        random.nextBytes(bytes)
        for (i in 0 until 6) bytes[i] = ((now ushr (8 * (5 - i))) and 0xFF).toByte()
        bytes[6] = ((bytes[6].toInt() and 0x0F) or 0x70).toByte()
        bytes[8] = ((bytes[8].toInt() and 0x3F) or 0x80).toByte()
        var msb = 0L
        var lsb = 0L
        for (i in 0 until 8) msb = (msb shl 8) or (bytes[i].toLong() and 0xFF)
        for (i in 8 until 16) lsb = (lsb shl 8) or (bytes[i].toLong() and 0xFF)
        return UUID(msb, lsb).toString()
    }
}
