package de.estrichone.core

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.JsonPrimitive
import java.time.Instant
import java.time.LocalDate
import java.util.Locale

// Tankbelege (Modul „Tankbelege“): in der App erfassen, offline über die Outbox `fuelReceipt.create`, Foto als Datei ohne Bauakte.

@Serializable
enum class FuelType(val title: String) {
    @SerialName("diesel") Diesel("Diesel"),
    @SerialName("petrol") Petrol("Benzin"),
    @SerialName("adBlue") AdBlue("AdBlue"),
    @SerialName("other") Other("Sonstiges");

    val raw: String get() = when (this) { Diesel -> "diesel"; Petrol -> "petrol"; AdBlue -> "adBlue"; Other -> "other" }
}

@Serializable
enum class FuelPaymentMethod(val title: String) {
    @SerialName("fuelCard") FuelCard("Tankkarte"),
    @SerialName("companyCard") CompanyCard("Firmenkarte"),
    @SerialName("private") Private("Privat ausgelegt");

    val raw: String get() = when (this) { FuelCard -> "fuelCard"; CompanyCard -> "companyCard"; Private -> "private" }
}

@Serializable
enum class FuelReceiptStatus {
    @SerialName("new") New,
    @SerialName("checked") Checked,
    @SerialName("settled") Settled,
    @SerialName("rejected") Rejected,
}

@Serializable
data class FuelWarning(val code: String = "", val text: String = "")

/** Eigener Tankbeleg (`GET fuel-receipts/mine`). */
@Serializable
data class FuelReceipt(
    val id: String,
    val number: String = "",
    val vehicleId: String = "",
    val vehicleName: String = "",
    val vehicleIdentifier: String? = null,
    val date: String = "",
    val odometerKm: Int? = null,
    val liters: Double = 0.0,
    val amountGross: Double = 0.0,
    val fuelType: FuelType = FuelType.Other,
    val paymentMethod: FuelPaymentMethod = FuelPaymentMethod.FuelCard,
    val station: String? = null,
    val status: FuelReceiptStatus = FuelReceiptStatus.New,
    val statusText: String = "",
    val rejectionReason: String? = null,
    val imageFileId: String? = null,
    val consumptionPer100Km: Double? = null,
    val warnings: List<FuelWarning> = emptyList(),
) {
    val vehicleText: String get() = vehicleIdentifier?.let { "$vehicleName ($it)" } ?: vehicleName
}

/** Fahrzeug zur Auswahl mit letztem Kilometerstand. */
@Serializable
data class FuelVehicleOption(
    val id: String,
    val kind: String = "vehicle",
    val name: String = "",
    val identifier: String? = null,
    val tankCapacityLiters: Double? = null,
    val lastOdometerKm: Int? = null,
    val lastFuelType: FuelType? = null,
) {
    val title: String get() = identifier?.let { "$name ($it)" } ?: name
}

/** `GET fuel-receipts/options`: Fahrzeuge und das heutige Dispo-Fahrzeug. */
@Serializable
data class FuelOptions(val vehicles: List<FuelVehicleOption> = emptyList(), val suggestedVehicleId: String? = null, val today: String = "")

/** Eingaben im Formular; Zahlen als Text wie getippt (Komma oder Punkt). */
data class FuelReceiptDraft(
    val vehicleId: String? = null,
    val date: LocalDate = LocalDate.now(Dates.zone),
    val odometer: String = "",
    val liters: String = "",
    val amount: String = "",
    val fuelType: FuelType = FuelType.Diesel,
    val paymentMethod: FuelPaymentMethod = FuelPaymentMethod.FuelCard,
    val station: String = "",
    val notes: String = "",
) {
    val litersValue: Double? get() = number(liters)
    val amountValue: Double? get() = number(amount)
    val odometerValue: Int? get() = odometer.filter { it.isDigit() }.takeIf { it.isNotEmpty() && it.length <= 7 }?.toIntOrNull()

    val pricePerLiter: Double?
        get() {
            val l = litersValue ?: return null
            val a = amountValue ?: return null
            return if (l > 0) Math.round(a / l * 1000) / 1000.0 else null
        }

    /** Erster Fehler als Text oder `null`. */
    fun validate(today: LocalDate = LocalDate.now(Dates.zone)): String? = when {
        vehicleId == null -> "Bitte das Fahrzeug wählen."
        litersValue.let { it == null || it <= 0 || it > 5000 } -> "Bitte die getankte Menge in Litern angeben."
        amountValue.let { it == null || it <= 0 || it > 50_000 } -> "Bitte den Betrag brutto angeben."
        date.isAfter(today) -> "Das Datum des Belegs liegt in der Zukunft."
        odometer.isNotBlank() && odometerValue == null -> "Bitte einen gültigen Kilometerstand angeben."
        else -> null
    }

    /** Hinweise vor dem Speichern (blockieren nicht). */
    fun hints(vehicle: FuelVehicleOption?): List<String> = buildList {
        val km = odometerValue
        val last = vehicle?.lastOdometerKm
        if (km != null && last != null && km < last) add("Der Kilometerstand ist kleiner als zuletzt (${grouped(last)} km).")
        val l = litersValue
        val tank = vehicle?.tankCapacityLiters
        if (fuelType != FuelType.AdBlue && l != null && tank != null && l > tank) add("Mehr Liter, als in den Tank passen (${decimal(tank)} l).")
        val price = pricePerLiter
        if (fuelType != FuelType.AdBlue && price != null && (price < 0.8 || price > 3.5)) {
            add("Der Preis je Liter (${decimal(price)} €) wirkt ungewöhnlich. Bitte Menge und Betrag prüfen.")
        }
    }

    /** Nutzdaten für `fuelReceipt.create`. */
    fun payload(imageFileId: String?, source: String = "android") = json(
        "vehicleId" to vehicleId, "date" to date.toString(), "odometerKm" to odometerValue,
        "liters" to litersValue?.let { Math.round(it * 100) / 100.0 }, "amountGross" to amountValue?.let { Math.round(it * 100) / 100.0 },
        "fuelType" to fuelType.raw, "paymentMethod" to paymentMethod.raw, "station" to station.trim().ifEmpty { null },
        "notes" to notes.trim().ifEmpty { null }, "imageFileId" to imageFileId, "source" to source,
    )

    companion object {
        /** Vorbelegung: heutiges Dispo-Fahrzeug, sonst das einzige; Kraftstoff wie zuletzt bei diesem Fahrzeug. */
        fun prefilled(options: FuelOptions?, today: LocalDate = LocalDate.now(Dates.zone)): FuelReceiptDraft {
            val vehicleId = options?.suggestedVehicleId ?: options?.vehicles?.singleOrNull()?.id
            val last = options?.vehicles?.firstOrNull { it.id == vehicleId }?.lastFuelType
            return FuelReceiptDraft(vehicleId = vehicleId, date = today, fuelType = last ?: FuelType.Diesel)
        }

        /** „61,37“, „61.37“, „1.234,56“ → Zahl. */
        fun number(text: String): Double? {
            var value = text.trim().replace("€", "").replace(" ", "")
            if (value.isEmpty()) return null
            if (value.contains(',') && value.contains('.')) value = value.replace(".", "")
            return value.replace(',', '.').toDoubleOrNull()
        }

        fun decimal(value: Double): String = String.format(Locale.GERMANY, "%.3f", value).trimEnd('0').trimEnd(',')

        fun grouped(value: Int): String = String.format(Locale.GERMANY, "%,d", value)
    }
}

/** Beleg auf dem Gerät (Anzeige „wird übertragen“). */
data class LocalFuelReceipt(val id: String, val number: String?, val date: String, val vehicleName: String, val liters: Double, val amountGross: Double,
    val state: SyncState)

/**
 * Tankbelege erfassen: Belegfoto als eigene Datei ohne Bauakte in die Upload-Warteschlange, der Beleg als Outbox-Operation
 * `fuelReceipt.create` (genau einmal über die opId). Die Belegnummer vergibt der Server.
 */
class FuelReceiptRecorder(private val store: LocalStore, private val service: BauakteService, private val now: () -> Instant = Instant::now) {
    fun record(draft: FuelReceiptDraft, photo: ByteArray?, vehicle: FuelVehicleOption?, source: String = "android"): String {
        draft.validate()?.let { throw IllegalArgumentException(it) }
        val imageId = photo?.let { service.addFile(null, "photo", it, "Tankbeleg.jpg", "image/jpeg", caption = "Tankbeleg", capturedAt = now()) }
        val id = Ids.v7()
        val payload = draft.payload(imageId, source)
        val local = payload.merged(mapOf(
            "id" to JsonPrimitive(id), "status" to JsonPrimitive("new"), "statusText" to JsonPrimitive("Neu"),
            "vehicleName" to JsonPrimitive(vehicle?.name ?: "Fahrzeug"), "createdAt" to JsonPrimitive(ApiDate.format(now())),
        ))
        store.create(EntityTypes.FUEL_RECEIPT, id, local, payload, "fuelReceipt.create")
        return id
    }

    /** Belege auf dem Gerät, die noch nicht beim Server sind, neueste zuerst. */
    fun pending(): List<LocalFuelReceipt> = store.entities(EntityTypes.FUEL_RECEIPT)
        .map { LocalFuelReceipt(it.id, it.data.str("number"), it.data.str("date") ?: "", it.data.str("vehicleName") ?: "Fahrzeug",
            it.data.num("liters") ?: 0.0, it.data.num("amountGross") ?: 0.0, it.syncState) }
        .filter { it.number == null || it.state != SyncState.Synced }
        .sortedByDescending { it.date }

    /** Fahrzeuge offline: zuletzt geladene Auswahl. */
    fun cachedOptions(): FuelOptions? = store.backend.meta(OPTIONS_KEY)?.let { runCatching { EstrichOneApi.json.decodeFromString(FuelOptions.serializer(), it) }.getOrNull() }

    fun rememberOptions(options: FuelOptions) = store.backend.setMeta(OPTIONS_KEY, EstrichOneApi.json.encodeToString(FuelOptions.serializer(), options))

    companion object {
        const val OPTIONS_KEY = "fuel_options"
    }
}

/** Liest typische deutsche Tankquittungen (Texterkennung): Betrag, Liter, Datum, Kraftstoff. Nur Vorschläge. */
object FuelReceiptTextParser {
    data class Suggestion(val amount: Double? = null, val liters: Double? = null, val date: LocalDate? = null, val fuelType: FuelType? = null)

    private val litersPattern = Regex("""(\d{1,4}[.,]\d{1,3})\s*(?:l|ltr|liter)\b""")
    private val moneyPattern = Regex("""(\d{1,5}[.,]\d{2})(?!\d)""")
    private val datePattern = Regex("""(\d{1,2})\.(\d{1,2})\.(\d{2,4})""")
    private val keywords = listOf("summe", "gesamt", "betrag", "total", "zu zahlen", "zahlbetrag", "bar", "ec-karte", "kartenzahlung")

    fun suggest(lines: List<String>, today: LocalDate = LocalDate.now(Dates.zone)): Suggestion {
        val lowered = lines.map { it.lowercase(Locale.GERMANY) }
        val all = lowered.joinToString("\n")
        val fuelType = when {
            "adblue" in all -> FuelType.AdBlue
            "diesel" in all -> FuelType.Diesel
            listOf("super", "benzin", "e10", "e5", "v-power").any { it in all } -> FuelType.Petrol
            else -> null
        }
        val liters = lowered.firstNotNullOfOrNull { line -> litersPattern.find(line)?.groupValues?.get(1)?.let(FuelReceiptDraft::number) }
            ?.takeIf { it > 0 && it < 2000 }
        var amount: Double? = null
        for ((index, line) in lowered.withIndex()) {
            if (keywords.none { it in line }) continue
            amount = moneyPattern.findAll(line).lastOrNull()?.groupValues?.get(1)?.let(FuelReceiptDraft::number)
                ?: lowered.getOrNull(index + 1)?.let { next -> moneyPattern.findAll(next).lastOrNull()?.groupValues?.get(1)?.let(FuelReceiptDraft::number) }
            if (amount != null) break
        }
        if (amount == null) {
            amount = lowered.filter { "eur" in it || "€" in it }
                .mapNotNull { moneyPattern.findAll(it).lastOrNull()?.groupValues?.get(1)?.let(FuelReceiptDraft::number) }.filter { it < 5000 }.maxOrNull()
        }
        val date = lines.asSequence().flatMap { datePattern.findAll(it) }.mapNotNull { match ->
            val (d, m, y) = match.destructured
            val year = y.toInt().let { if (it < 100) 2000 + it else it }
            runCatching { LocalDate.of(year, m.toInt(), d.toInt()) }.getOrNull()
        }.firstOrNull { !it.isAfter(today) && it.isAfter(today.minusYears(1)) }
        return Suggestion(amount, liters?.takeIf { it != amount }, date, fuelType)
    }
}

suspend fun EstrichOneApi.fuelOptions(): FuelOptions = send("GET", "fuel-receipts/options", null)

suspend fun EstrichOneApi.myFuelReceipts(): List<FuelReceipt> = send("GET", "fuel-receipts/mine", null)
