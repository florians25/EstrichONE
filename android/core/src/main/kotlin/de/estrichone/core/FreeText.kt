package de.estrichone.core

import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.contentOrNull

/**
 * Freitexte der Bauakte (Hinweise und Zugang, Beschreibung sowie mehrzeilige Felder aus dem
 * Formular-Designer) als lesbare Textblöcke: Zeilenumbrüche bleiben, Links, E-Mail-Adressen und Telefonnummern werden
 * erkannt, lange Texte zeigen zuerst den Anfang (wie `web/webapp/util/freeText.ts`).
 */
object FreeText {
    const val LONG_CHARS = 320
    const val LONG_LINES = 5

    /** „Hinweise und Zugang“ (siteNotes der Bauakte): Bezeichnung, Eingabehilfe und Höchstlänge wie im Web. */
    const val SITE_NOTES_LABEL = "Hinweise und Zugang"
    const val SITE_NOTES_HELP = "Zufahrt, Schlüssel, Strom, Wasser, Pumpenstandort, Behinderungen"
    const val SITE_NOTES_MAX = 3000

    /** Früheres Feld „Zugang / Besonderheiten“: aufgegangen in „Hinweise und Zugang“; ältere Server liefern es noch. */
    const val RETIRED_ACCESS_PATH = "project.siteAccess"

    /** Gekürzt zeigt die App so viele Zeilen. */
    const val COLLAPSED_LINES = 6

    data class Block(val key: String, val label: String, val text: String, val important: Boolean = false, val fromSchema: Boolean = false) {
        val isLong: Boolean get() = isLong(text)
    }

    enum class Kind { Text, Url, Mail, Phone }

    /** Abschnitt eines Freitexts; [target] ist die Adresse zum Öffnen (https:, mailto:, tel:). */
    data class Segment(val text: String, val kind: Kind = Kind.Text, val target: String? = null)

    private val token = Regex("""(https?://[^\s<>"']+|www\.[^\s<>"']+)|([A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,})|((?:\+|\b0)[0-9][0-9 /()-]{4,}[0-9])""")

    fun isLong(text: String?): Boolean = text != null && (text.length > LONG_CHARS || text.lines().size > LONG_LINES)

    /** Telefonnummer für `tel:` (Ziffern, führendes +; „(0)“ entfällt bei internationaler Schreibweise). */
    fun telHref(raw: String): String {
        val plus = raw.trim().startsWith("+")
        val digits = (if (plus) raw.replace("(0)", "") else raw).filter { it.isDigit() }
        return "tel:" + (if (plus) "+" else "") + digits
    }

    /** Text in Abschnitte zerlegen: Klartext, Link, E-Mail, Telefon. Zeilenumbrüche bleiben im Klartext erhalten. */
    fun segments(text: String?): List<Segment> {
        if (text.isNullOrEmpty()) return emptyList()
        val normalized = text.replace("\r\n", "\n").replace('\r', '\n')
        val out = mutableListOf<Segment>()
        var last = 0
        fun plain(value: String) {
            if (value.isEmpty()) return
            val previous = out.lastOrNull()
            if (previous?.kind == Kind.Text) out[out.size - 1] = previous.copy(text = previous.text + value) else out += Segment(value)
        }
        for (match in token.findAll(normalized)) {
            plain(normalized.substring(last, match.range.first))
            val (url, mail, phone) = match.destructured
            when {
                url.isNotEmpty() -> {
                    val trimmed = url.trimEnd('.', ',', ';', ':', '!', '?', ')')
                    out += Segment(trimmed, Kind.Url, if (trimmed.startsWith("www.")) "https://$trimmed" else trimmed)
                    plain(url.substring(trimmed.length))
                }
                mail.isNotEmpty() -> out += Segment(mail, Kind.Mail, "mailto:$mail")
                phone.count { it.isDigit() } >= 6 -> out += Segment(phone, Kind.Phone, telHref(phone))
                else -> plain(match.value)
            }
            last = match.range.last + 1
        }
        plain(normalized.substring(last))
        return out
    }

    /**
     * Hinweise und früheres Feld „Zugang / Besonderheiten“ als ein Text: mit Leerzeile getrennt; steht der Zugang schon
     * wortgleich in den Hinweisen, bleibt es bei den Hinweisen. `null`, wenn beides leer ist.
     */
    fun merged(notes: String?, access: String?): String? {
        val a = access?.trim()?.takeIf { it.isNotEmpty() }
        val n = notes?.trim()?.takeIf { it.isNotEmpty() }
        return when {
            a == null -> n
            n == null -> a
            n.contains(a) -> n
            else -> "$n\n\n$a"
        }
    }

    /**
     * Textblöcke der Bauakte: Hinweise und Zugang (ein hervorgehobener Block, auch wenn ein älterer Server den Zugang noch
     * getrennt liefert), Beschreibung, dann eigene mehrzeilige Felder aus dem Formular-Schema. Nur Blöcke mit Inhalt, damit
     * die Bauakte ruhig bleibt.
     */
    fun blocks(b: Bauakte, schema: FormSchema?): List<Block> {
        val out = mutableListOf<Block>()
        b.siteNotesAndAccess?.let { out += Block("siteNotes", SITE_NOTES_LABEL, it, important = true) }
        b.description?.trim()?.takeIf { it.isNotEmpty() }?.let { out += Block("description", "Beschreibung", it) }
        schema?.groups?.flatMap { it.fields }?.filter { it.type == "textarea" && it.path != RETIRED_ACCESS_PATH }?.forEach { field ->
            val value = (projectValue(b, field.path) as? JsonPrimitive)?.contentOrNull?.trim()
            if (!value.isNullOrEmpty()) out += Block(field.key, field.label, value, fromSchema = true)
        }
        return out
    }

    /** Wert unter `project.x` bzw. `project.custom.x`. */
    fun projectValue(b: Bauakte, path: String): JsonElement? {
        var current: JsonElement? = b.project
        for (part in path.removePrefix("project.").split('.')) current = (current as? JsonObject)?.get(part)
        return current
    }
}
