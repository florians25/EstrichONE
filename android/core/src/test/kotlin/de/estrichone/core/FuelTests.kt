package de.estrichone.core

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.DisplayName
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.assertThrows
import java.time.LocalDate

@DisplayName("Tankbelege: erfassen offline, Prüfung, Hinweise, Texterkennung")
class FuelTests {
    private val sprinter = FuelVehicleOption("0193a1b2-0000-7000-8000-0000000f0001", name = "Sprinter 3", identifier = "LA-EB 300", tankCapacityLiters = 80.0,
        lastOdometerKm = 48_000, lastFuelType = FuelType.Diesel)

    @Test
    fun `Vorbelegung mit dem heutigen Dispo-Fahrzeug und dem letzten Kraftstoff`() {
        val pump = FuelVehicleOption("p", kind = "pump", name = "Mixokret 2")
        val draft = FuelReceiptDraft.prefilled(FuelOptions(listOf(pump, sprinter), sprinter.id, "2026-11-20"))
        assertEquals(sprinter.id, draft.vehicleId)
        assertEquals(FuelType.Diesel, draft.fuelType)
        assertNull(FuelReceiptDraft.prefilled(FuelOptions(listOf(pump, sprinter), null)).vehicleId)
        assertEquals(sprinter.id, FuelReceiptDraft.prefilled(FuelOptions(listOf(sprinter), null)).vehicleId)
    }

    @Test
    fun `Prüfung und Hinweise`() {
        val today = LocalDate.of(2026, 11, 20)
        var draft = FuelReceiptDraft(date = today, liters = "61,37", amount = "104,27")
        assertEquals("Bitte das Fahrzeug wählen.", draft.validate(today))
        draft = draft.copy(vehicleId = sprinter.id)
        assertNull(draft.validate(today))
        assertEquals(1.699, draft.pricePerLiter)
        assertEquals("Bitte den Betrag brutto angeben.", draft.copy(amount = "0").validate(today))
        assertEquals(1234.56, FuelReceiptDraft.number("1.234,56"))
        assertEquals("Das Datum des Belegs liegt in der Zukunft.", draft.copy(date = today.plusDays(2)).validate(today))
        val hints = FuelReceiptDraft(vehicleId = sprinter.id, odometer = "47.900", liters = "95", amount = "20").hints(sprinter)
        assertEquals(3, hints.size)
        assertTrue(hints[0].contains("48.000 km"))
        assertTrue(hints[1].contains("80 l"))
        assertEquals(1, FuelReceiptDraft(vehicleId = sprinter.id, odometer = "47900", liters = "95", amount = "20", fuelType = FuelType.AdBlue).hints(sprinter).size)
    }

    @Test
    fun `Beleg mit Foto geht offline in die Outbox – Foto zuerst`() {
        val store = LocalStore(MemoryStorage())
        val recorder = FuelReceiptRecorder(store, BauakteService(store, MemoryFileVault()) { "u" })
        assertThrows<IllegalArgumentException> { recorder.record(FuelReceiptDraft(liters = "10", amount = "17"), null, null) }
        val id = recorder.record(FuelReceiptDraft(vehicleId = sprinter.id, odometer = "48 210", liters = "61,37", amount = "104,27",
            paymentMethod = FuelPaymentMethod.Private, station = " Aral "), byteArrayOf(1, 2, 3), sprinter)

        val ops = store.backend.outbox()
        assertEquals(listOf("file.announce", "fuelReceipt.create"), ops.map { it.type })
        val payload = ops[1].payload
        assertEquals(sprinter.id, payload.str("vehicleId"))
        assertEquals(48_210L, payload.long("odometerKm"))
        assertEquals(61.37, payload.num("liters"))
        assertEquals("private", payload.str("paymentMethod"))
        assertEquals("Aral", payload.str("station"))
        assertEquals("android", payload.str("source"))
        assertEquals(ops[0].entityId, payload.str("imageFileId"))
        assertEquals(listOf(id), recorder.pending().map { it.id })
        assertEquals("Sprinter 3", recorder.pending()[0].vehicleName)

        recorder.rememberOptions(FuelOptions(listOf(sprinter), sprinter.id, "2026-11-20"))
        assertEquals(sprinter.id, recorder.cachedOptions()?.suggestedVehicleId)
    }

    @Test
    fun `Texterkennung schlägt Betrag, Liter, Datum und Kraftstoff vor`() {
        val today = LocalDate.of(2026, 11, 20)
        val s = FuelReceiptTextParser.suggest(listOf("ARAL Tankstelle", "Datum 18.11.2026 07:12", "Diesel", "61,37 l x 1,699 EUR/l", "Summe EUR 104,27"), today)
        assertEquals(104.27, s.amount)
        assertEquals(61.37, s.liters)
        assertEquals(LocalDate.of(2026, 11, 18), s.date)
        assertEquals(FuelType.Diesel, s.fuelType)
        val other = FuelReceiptTextParser.suggest(listOf("Super E10", "Menge 42,10 Liter", "Gesamt", "73,50", "01.01.2030"), today)
        assertEquals(FuelType.Petrol, other.fuelType)
        assertEquals(42.10, other.liters)
        assertEquals(73.50, other.amount)
        assertNull(other.date)
    }

    @Test
    fun `Antwort des Servers wird gelesen`() {
        val text = """[{"id":"x","number":"TB-2026-0001","vehicleId":"v","vehicleName":"Sprinter 3","vehicleIdentifier":"LA-EB 300","date":"2026-11-18",
            "odometerKm":48210,"liters":61.37,"amountGross":104.27,"fuelType":"diesel","paymentMethod":"private","status":"rejected","statusText":"Abgelehnt",
            "rejectionReason":"unleserlich","warnings":[{"code":"missing_image","text":"Kein Belegfoto vorhanden."}],"unknownField":1}]"""
        val receipts = EstrichOneApi.json.decodeFromString(kotlinx.serialization.builtins.ListSerializer(FuelReceipt.serializer()), text)
        assertEquals(FuelReceiptStatus.Rejected, receipts[0].status)
        assertEquals(FuelPaymentMethod.Private, receipts[0].paymentMethod)
        assertEquals("Sprinter 3 (LA-EB 300)", receipts[0].vehicleText)
    }
}
