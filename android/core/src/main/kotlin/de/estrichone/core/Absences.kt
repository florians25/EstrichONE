package de.estrichone.core

import kotlinx.serialization.Serializable
import java.text.DecimalFormat
import java.text.DecimalFormatSymbols
import java.util.Locale

// Urlaub und Abwesenheiten (Personal-API hr/…). Arbeitstage, Konto und Hinweise rechnet der Server.

/** Abwesenheitsart mit Farbe und Regeln. */
@Serializable
data class AbsenceType(
    val id: String,
    val key: String = "",
    val name: String = "",
    val code: String = "",
    val color: String = "#9E9E9E",
    val kind: String = "other",
    val requiresApproval: Boolean = true,
    val countsAsVacation: Boolean = false,
    val requestable: Boolean = true,
    val isActive: Boolean = true,
    val isSickness: Boolean = false,
)

/** Urlaubskonto eines Jahres. */
@Serializable
data class VacationAccount(
    val year: Int,
    val entitlement: Double = 0.0,
    val carryOver: Double = 0.0,
    val taken: Double = 0.0,
    val planned: Double = 0.0,
    val remaining: Double = 0.0,
    val requested: Double = 0.0,
    val annualEntitlement: Double = 0.0,
    val adjustment: Double = 0.0,
    val carryOverExpiresOn: String? = null,
    val carryOverExpired: Double = 0.0,
    val carryOverExpiring: Double = 0.0,
    val remainingAfterRequests: Double? = null,
) {
    val afterRequests: Double get() = remainingAfterRequests ?: (remaining - requested)

    /** Hinweis auf verfallenden Resturlaub oder null. */
    val expiryHint: String?
        get() = if (carryOverExpiring > 0 && carryOverExpiresOn != null)
            "${AbsenceText.days(carryOverExpiring)} Tage Resturlaub verfallen nach dem ${AbsenceText.date(carryOverExpiresOn)}, wenn sie bis dahin nicht genommen oder geplant sind."
        else null
}

/** Antrag (eigener oder zur Genehmigung). */
@Serializable
data class AbsenceRequestItem(
    val id: String,
    val userId: String,
    val userName: String = "",
    val from: String,
    val to: String,
    val note: String? = null,
    val status: String = "pending",
    val workingDays: Double = 0.0,
    val decisionComment: String? = null,
    val warnings: List<String> = emptyList(),
    val remainingAfter: Double? = null,
    val canDecide: Boolean = false,
    val canCancel: Boolean = false,
    val typeId: String? = null,
    val typeName: String? = null,
    val typeColor: String? = null,
    val halfDayStart: Boolean = false,
    val halfDayEnd: Boolean = false,
    val substituteName: String? = null,
    val replacesRequestId: String? = null,
) {
    val rangeText: String get() = AbsenceText.range(from, to, halfDayStart, halfDayEnd)
    val statusText: String get() = if (status == "cancelled" && decisionComment == "Durch Änderungsantrag ersetzt.") "Ersetzt" else AbsenceText.status(status)
    val isApproved: Boolean get() = status == "approved"
}

/** Eigene eingetragene Abwesenheit. */
@Serializable
data class MyAbsence(
    val id: String,
    val from: String,
    val to: String,
    val typeName: String = "Abwesenheit",
    val color: String = "#9E9E9E",
    val halfDayStart: Boolean = false,
    val halfDayEnd: Boolean = false,
    val workingDays: Double = 0.0,
)

@Serializable
data class AbsenceHoliday(val date: String, val name: String)

/** Eigene Übersicht (GET hr/me). */
@Serializable
data class MyVacation(
    val employeeId: String? = null,
    val vacation: VacationAccount? = null,
    val requests: List<AbsenceRequestItem> = emptyList(),
    val holidays: List<AbsenceHoliday> = emptyList(),
    val year: Int = 0,
    val types: List<AbsenceType>? = null,
    val absences: List<MyAbsence>? = null,
    val canApprove: Boolean = false,
    val pendingApprovals: Int = 0,
) {
    /** Arten, die der Mitarbeiter selbst beantragen darf. */
    val requestableTypes: List<AbsenceType> get() = types.orEmpty().filter { it.isActive && it.requestable && !it.isSickness }
}

/** Vorschau vor dem Absenden. */
@Serializable
data class AbsencePreview(
    val workingDays: Double = 0.0,
    val warnings: List<String> = emptyList(),
    val remainingAfter: Double? = null,
    val requiresApproval: Boolean = true,
) {
    val summary: String
        get() = "${AbsenceText.days(workingDays)} Arbeitstage" + (remainingAfter?.let { ", danach ${AbsenceText.days(it)} Tage Resturlaub" } ?: "") + "." +
            if (requiresApproval) " Der Antrag geht zur Genehmigung." else " Keine Genehmigung erforderlich."
}

/** Antrag bzw. Vorschau (Tage als yyyy-MM-dd). */
@Serializable
data class AbsenceDraft(
    val typeId: String?,
    val from: String,
    val to: String,
    val halfDayStart: Boolean = false,
    val halfDayEnd: Boolean = false,
    val note: String? = null,
    val replacesRequestId: String? = null,
) {
    /** Fehlertext für das Formular oder null. */
    val problem: String?
        get() = when {
            typeId == null -> "Bitte eine Art wählen."
            to < from -> "Das Ende liegt vor dem Beginn."
            else -> null
        }

    /** Eintägig mit beiden Angaben = ein halber Tag; leere Bemerkung wird nicht gesendet. */
    fun normalized(): AbsenceDraft = copy(halfDayEnd = halfDayEnd && !(from == to && halfDayStart), note = note?.trim()?.ifEmpty { null })
}

@Serializable
data class AbsenceDecision(val approve: Boolean, val comment: String?)

object AbsenceText {
    private val number = DecimalFormat("0.#", DecimalFormatSymbols(Locale.GERMANY))

    fun days(value: Double): String = number.format(value)

    /** 2027-06-07 → 07.06.2027 */
    fun date(day: String): String = day.split('-').takeIf { it.size == 3 }?.let { "${it[2]}.${it[1]}.${it[0]}" } ?: day

    fun range(from: String, to: String, halfDayStart: Boolean = false, halfDayEnd: Boolean = false): String {
        if (from == to) return date(from) + if (halfDayStart || halfDayEnd) " (halber Tag)" else ""
        val start = if (from.take(4) == to.take(4)) date(from).take(6) else date(from)
        val half = listOfNotNull(if (halfDayStart) "erster Tag halb" else null, if (halfDayEnd) "letzter Tag halb" else null).joinToString(", ")
        return "$start – ${date(to)}" + if (half.isEmpty()) "" else " ($half)"
    }

    fun status(status: String): String = when (status) {
        "pending" -> "Beantragt"
        "approved" -> "Genehmigt"
        "rejected" -> "Abgelehnt"
        "cancelled" -> "Zurückgezogen"
        else -> status
    }
}

suspend fun EstrichOneApi.myVacation(year: Int? = null): MyVacation = send("GET", "hr/me", null, query = mapOf("year" to year?.toString()))

/** Offene Anträge, über die der Benutzer entscheiden darf. */
suspend fun EstrichOneApi.pendingAbsenceRequests(): List<AbsenceRequestItem> =
    send<List<AbsenceRequestItem>>("GET", "hr/absence-requests", null, query = mapOf("scope" to "pending")).filter { it.canDecide }

suspend fun EstrichOneApi.previewAbsence(draft: AbsenceDraft): AbsencePreview = send("POST", "hr/absence-requests/preview", draft.normalized())

/** Antrag stellen; die ID macht Wiederholungen nach Verbindungsabbruch unschädlich. */
suspend fun EstrichOneApi.requestAbsence(draft: AbsenceDraft, id: String = EstrichOneApi.newId()): AbsenceRequestItem =
    send("PUT", "hr/absence-requests/$id", draft.normalized())

suspend fun EstrichOneApi.decideAbsence(id: String, approve: Boolean, comment: String?): AbsenceRequestItem =
    send("POST", "hr/absence-requests/$id/decision", AbsenceDecision(approve, comment?.trim()?.ifEmpty { null }))

suspend fun EstrichOneApi.cancelAbsence(id: String): AbsenceRequestItem = send("POST", "hr/absence-requests/$id/cancel", kotlinx.serialization.json.JsonObject(emptyMap()))
