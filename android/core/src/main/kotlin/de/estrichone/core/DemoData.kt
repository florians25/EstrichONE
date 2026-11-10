package de.estrichone.core

import kotlinx.serialization.json.JsonObject
import java.time.Instant
import java.time.LocalDate
import java.time.ZonedDateTime

/**
 * Beispieldaten des Demomodus (wie DemoContent.swift auf iOS): sechs Bauakten in allen Ständen mit Kunden, Kenndaten nach
 * dem Formular-Schema, Fotos, Lieferschein, Bautagebuch, Erstbegehung, CM-Messung, Aufgaben, Mängeln, Räumen und
 * Beteiligten. Alles im Format des Änderungsfeeds.
 */
object DemoData {
    const val USER_ID = "0193a1b2-0000-7000-8000-00000000aaaa"
    const val USER_NAME = "Max Huber"
    const val EFH = "0193a1b2-de00-7000-8000-0000000b0001"
    const val HALLE = "0193a1b2-de00-7000-8000-0000000b0002"
    const val PRAXIS = "0193a1b2-de00-7000-8000-0000000b0003"
    const val ISARPARK = "0193a1b2-de00-7000-8000-0000000b0004"
    const val WEBER = "0193a1b2-de00-7000-8000-0000000b0005"
    const val KITA = "0193a1b2-de00-7000-8000-0000000b0006"
    const val TICKET = "0193a1b2-de00-7000-8000-00000000c001"
    const val PDF = "0193a1b2-de00-7000-8000-00000000f001"

    private const val SCHNEIDER = "0193a1b2-de00-7000-8000-0000000c0001"
    private const val HUBER_BAU = "0193a1b2-de00-7000-8000-0000000c0002"
    private const val MAIER = "0193a1b2-de00-7000-8000-0000000c0003"
    private const val WOHNBAU = "0193a1b2-de00-7000-8000-0000000c0004"
    private const val T_WEBER = "0193a1b2-de00-7000-8000-0000000c0005"
    private const val GEMEINDE = "0193a1b2-de00-7000-8000-0000000c0006"

    /** Motiv der Beispielfotos (die App zeichnet daraus ruhige Bilder ohne Text). */
    enum class Motif { EdgeStrip, Heating, Screed, Pump, Crack, Access }

    data class Photo(val id: String, val bauakteId: String, val caption: String, val ageSeconds: Long, val motif: Motif)

    val photos = listOf(
        Photo("0193a1b2-de00-7000-8000-00000000a001", EFH, "Randdämmstreifen gesetzt, EG", 86_400 + 3600, Motif.EdgeStrip),
        Photo("0193a1b2-de00-7000-8000-00000000a002", EFH, "Fußbodenheizung vor dem Einbringen", 86_400, Motif.Heating),
        Photo("0193a1b2-de00-7000-8000-00000000a003", EFH, "Estrich eingebracht, EG", 2400, Motif.Screed),
        Photo("0193a1b2-de00-7000-8000-00000000a005", EFH, "Pumpe am Straßenrand", 3000, Motif.Pump),
        Photo("0193a1b2-de00-7000-8000-00000000a006", EFH, "Riss im Flur", 1200, Motif.Crack),
        Photo("0193a1b2-de00-7000-8000-00000000a004", HALLE, "Zufahrt Halle", 86_400 * 4, Motif.Access),
        Photo("0193a1b2-de00-7000-8000-00000000a007", PRAXIS, "Behandlungsraum 1, verlegt", 86_400 * 11, Motif.Screed),
    )

    private fun change(type: String, data: JsonObject) = SyncChange(type, data.str("id")!!, false, data)

    fun changes(now: Instant = Instant.now(), today: LocalDate = LocalDate.now(Dates.zone)): List<SyncChange> {
        fun day(offset: Long) = today.plusDays(offset).toString()
        fun time(offsetSeconds: Long) = ApiDate.format(now.plusSeconds(offsetSeconds))
        val user = USER_ID
        val result = mutableListOf<SyncChange>()

        fun contact(id: String, kind: String, name: String, number: String, street: String, postalCode: String, city: String,
            phones: List<Map<String, String?>>, emails: List<Map<String, String?>>, extra: Map<String, Any?> = emptyMap()) {
            result += change(EntityTypes.CONTACT, json(
                "id" to id, "kind" to kind, "name" to name, "customerNumber" to number, "street" to street, "postalCode" to postalCode, "city" to city,
                "phones" to phones, "emails" to emails, "phone" to phones.firstOrNull()?.get("number"), "email" to emails.firstOrNull()?.get("address"),
                "notes" to null, "version" to 1,
            ).merged(json(*extra.toList().toTypedArray())))
        }

        fun bauakte(id: String, number: String, title: String, status: String, street: String, postalCode: String, city: String, customer: String?,
            project: Map<String, Any?>, start: Long?, end: Long?, extra: Map<String, Any?> = emptyMap()) {
            result += change(EntityTypes.BAUAKTE, json(
                "id" to id, "number" to number, "title" to title, "status" to status, "street" to street, "postalCode" to postalCode, "city" to city,
                "customerId" to customer, "project" to project, "plannedStart" to start?.let(::day), "plannedEnd" to end?.let(::day),
                "responsibleUserId" to user, "updatedAt" to time(-5400), "version" to 3,
            ).merged(json(*extra.toList().toTypedArray())))
        }

        contact(SCHNEIDER, "person", "Familie Schneider", "K-1088", "Ahornweg 3", "84028", "Landshut",
            listOf(mapOf("kind" to "mobile", "number" to "0171 2345678", "label" to "Herr Schneider"), mapOf("kind" to "landline", "number" to "0871 123456", "label" to null)),
            listOf(mapOf("address" to "schneider@example.com", "label" to "privat")),
            mapOf("salutation" to "Familie", "preferredChannel" to "mobile", "notes" to "Hund im Garten, bitte klingeln."))
        contact(HUBER_BAU, "company", "Huber Bau GmbH", "K-1042", "Industriestr. 1", "84028", "Landshut",
            listOf(mapOf("kind" to "office", "number" to "0871 4711", "label" to "Zentrale"), mapOf("kind" to "fax", "number" to "0871 4712", "label" to null)),
            listOf(mapOf("address" to "info@huber.example", "label" to "Zentrale"), mapOf("address" to "bauleitung@huber.example", "label" to "Bauleitung")),
            mapOf("preferredChannel" to "email", "website" to "www.huber.example"))
        contact(MAIER, "company", "Praxis Dr. Maier", "K-1103", "Altstadt 12", "84028", "Landshut",
            listOf(mapOf("kind" to "office", "number" to "0871 96 55 10", "label" to "Praxis")), listOf(mapOf("address" to "praxis@maier.example", "label" to null)))
        contact(WOHNBAU, "company", "Wohnbau Isar GmbH", "K-1017", "Am Isarpark 1", "84034", "Landshut",
            listOf(mapOf("kind" to "office", "number" to "0871 33 20 00", "label" to "Bauleitung")), listOf(mapOf("address" to "bau@wohnbau-isar.example", "label" to "Bauleitung")))
        contact(T_WEBER, "person", "Thomas Weber", "K-1121", "Lindenstr. 8", "84137", "Vilsbiburg",
            listOf(mapOf("kind" to "mobile", "number" to "0160 9876543", "label" to null)), emptyList())
        contact(GEMEINDE, "company", "Gemeinde Ergolding", "K-1002", "Lindenstr. 25", "84030", "Ergolding",
            listOf(mapOf("kind" to "office", "number" to "0871 97 40 0", "label" to "Bauamt")), listOf(mapOf("address" to "bauamt@ergolding.example", "label" to "Bauamt")))

        bauakte(EFH, "2026-041", "EFH Schneider, Neubau", "inProgress", "Ahornweg 3", "84028", "Landshut", SCHNEIDER,
            mapOf("objectType" to "efh", "floors" to "EG, OG", "totalArea" to 142.5, "priority" to "normal", "screedType" to "ct", "construction" to "heated",
                "nominalThicknessMm" to 65, "strengthClass" to "C25-F4", "underfloorHeating" to true, "heatingType" to "water", "insulation" to "EPS 035 DEO",
                "insulationThicknessMm" to 40, "siteAccess" to "Pumpe am Straßenrand, Schlauch über die Terrasse", "waterOnSite" to true, "powerOnSite" to true),
            -2, 3, mapOf("latitude" to 48.5372, "longitude" to 12.1521, "description" to "Heizestrich CT-C25-F4, 65 mm, EG und OG",
                "siteContactName" to "Herr Schneider", "siteContactPhone" to "0171 2345678", "updatedAt" to time(-1800)))
        bauakte(HALLE, "2026-042", "Halle Gewerbering", "planned", "Gewerbering 3", "84030", "Ergolding", HUBER_BAU,
            mapOf("objectType" to "industrial", "totalArea" to 600, "screedType" to "ct", "construction" to "bonded", "nominalThicknessMm" to 50,
                "strengthClass" to "C30-F5", "underfloorHeating" to false, "priority" to "high", "siteAccess" to "Zufahrt nur bis 7,5 t"),
            3, 5, mapOf("description" to "Industrieestrich im Verbund, 600 m²"))
        bauakte(PRAXIS, "2026-038", "Praxis Dr. Maier, Sanierung", "drying", "Altstadt 12", "84028", "Landshut", MAIER,
            mapOf("objectType" to "renovation", "totalArea" to 85, "screedType" to "caf", "construction" to "floating", "nominalThicknessMm" to 45,
                "strengthClass" to "CA-C25-F4", "underfloorHeating" to true, "heatingType" to "water"), -12, -10,
            // Sanierung im Bestand: Untergrund aus dem Vorauftrag bekannt, daher keine Erstbegehung.
            mapOf("initialInspectionWaived" to true, "initialInspectionWaivedReason" to "Untergrund aus dem Vorauftrag bekannt",
                "initialInspectionWaivedByName" to "Max Huber", "initialInspectionWaivedAt" to time(-14L * 86400)))
        bauakte(ISARPARK, "2026-044", "MFH Isarpark, Haus B", "planned", "Am Isarpark 7", "84034", "Landshut", WOHNBAU,
            mapOf("objectType" to "mfh", "floors" to "EG–3. OG", "totalArea" to 920, "screedType" to "ct", "construction" to "floating",
                "nominalThicknessMm" to 55, "strengthClass" to "C25-F4", "underfloorHeating" to true, "heatingType" to "water"), 2, 9)
        bauakte(WEBER, "2026-047", "Anbau Weber", "inquiry", "Lindenstr. 8", "84137", "Vilsbiburg", T_WEBER, mapOf("objectType" to "efh", "totalArea" to 38), null, null)
        bauakte(KITA, "2026-021", "Kindergarten Sonnenschein", "completed", "Schulweg 4", "84030", "Ergolding", GEMEINDE,
            mapOf("objectType" to "public", "totalArea" to 410, "screedType" to "caf", "construction" to "floating", "nominalThicknessMm" to 50,
                "underfloorHeating" to true), -40, -35)

        result += change(EntityTypes.NOTE, json("id" to "0193a1b2-de00-7000-8000-0000000d0001", "bauakteId" to EFH, "text" to "Schlüssel liegt beim Nachbarn (Nr. 5).",
            "authorUserId" to user, "isPinned" to true, "createdAt" to time(-86_400 * 2), "version" to 1))
        result += change(EntityTypes.NOTE, json("id" to "0193a1b2-de00-7000-8000-0000000d0002", "bauakteId" to EFH,
            "text" to "Kunde wünscht Fugen im Türbereich, mit Bauleitung abgestimmt.", "authorUserId" to user, "isPinned" to false,
            "createdAt" to time(-86_400), "version" to 1))
        result += change(EntityTypes.DIARY, json("id" to "0193a1b2-de00-7000-8000-0000000e0001", "bauakteId" to EFH, "day" to day(-1),
            "text" to "Randdämmstreifen gesetzt, Folie verlegt.", "weather" to "cloudy", "temperatureCelsius" to 12, "workersPresent" to 3,
            "authorUserId" to user, "createdAt" to time(-86_400), "version" to 1))
        result += change(EntityTypes.DIARY, json("id" to "0193a1b2-de00-7000-8000-0000000e0002", "bauakteId" to EFH, "day" to day(0),
            "text" to "Estrich im EG eingebracht, 65 mm.\nPumpe lief ohne Störung.", "weather" to "sunny", "temperatureCelsius" to 15, "workersPresent" to 4,
            "startTime" to "07:00:00", "endTime" to "15:00:00", "crew" to "Kolonne Huber", "authorUserId" to user, "createdAt" to time(-3600), "version" to 1))
        result += change(EntityTypes.DIARY, json("id" to "0193a1b2-de00-7000-8000-0000000e0003", "bauakteId" to PRAXIS, "day" to day(-11),
            "text" to "Fließestrich im Behandlungsbereich eingebracht.", "weather" to "rain", "temperatureCelsius" to 9, "workersPresent" to 3,
            "authorUserId" to user, "createdAt" to time(-86_400 * 11), "version" to 1))
        result += change(EntityTypes.INSPECTION, json("id" to "0193a1b2-de00-7000-8000-000000001001", "bauakteId" to EFH, "kind" to "initialInspection",
            "performedByUserId" to user, "performedAt" to time(-86_400 * 3), "notRequired" to false,
            "checks" to mapOf("substrate" to "ok", "edgeInsulation" to "ok", "heatingPressureTest" to "ok", "access" to "notOk", "utilities" to "ok"),
            "checkRemarks" to mapOf("access" to "Zufahrt nur mit kleinem Lkw, Pumpe an der Straße aufstellen."),
            "values" to mapOf("measuredArea" to 142.5, "thickness" to 65, "underfloorHeating" to true, "water" to true, "power" to true),
            "schemaVersion" to 1, "photoFileIds" to listOf(photos[0].id), "remarks" to "Zufahrt nur mit kleinem Lkw",
            "notes" to "Kunde informiert.\nPumpe am Straßenrand aufstellen.", "version" to 1))
        result += change(EntityTypes.INSPECTION, json("id" to "0193a1b2-de00-7000-8000-000000001002", "bauakteId" to PRAXIS, "kind" to "cmMeasurement",
            "performedByUserId" to user, "performedAt" to time(-86_400), "location" to "Behandlungsraum 1", "binder" to "calciumSulfate", "heated" to true,
            "thicknessMm" to 45, "measuredCmPercent" to 0.3, "limitCmPercent" to 0.3, "isReadyForCovering" to true, "notRequired" to false,
            "checks" to emptyMap<String, String>(), "photoFileIds" to emptyList<String>(), "version" to 1))
        result += change(EntityTypes.ACCEPTANCE, json("id" to "0193a1b2-de00-7000-8000-000000001101", "bauakteId" to KITA, "performedAt" to time(-86_400 * 34),
            "result" to "accepted", "state" to "completed", "clientName" to "Herr Bauer (Gemeinde)", "contractorName" to USER_NAME,
            "location" to "Kindergarten Sonnenschein", "scope" to "Fließestrich EG, 410 m²", "hasClientSignature" to true, "hasContractorSignature" to true, "version" to 2))

        fun item(id: String, bauakteId: String, kind: String, title: String, status: String = "open", due: Long? = null) {
            result += change(EntityTypes.WORK_ITEM, json("id" to id, "bauakteId" to bauakteId, "kind" to kind, "title" to title, "status" to status,
                "dueDate" to due?.let(::day), "assigneeUserId" to user, "createdByUserId" to user, "updatedAt" to time(0), "version" to 1))
        }
        item("0193a1b2-de00-7000-8000-000000002001", EFH, "defect", "Riss im Flur prüfen", due = 0)
        item("0193a1b2-de00-7000-8000-000000002003", EFH, "task", "Randdämmstreifen Bad OG nachsetzen", due = 1)
        item("0193a1b2-de00-7000-8000-000000002004", EFH, "task", "Folie im EG verlegen", status = "done")
        item("0193a1b2-de00-7000-8000-000000002002", HALLE, "task", "Zufahrt mit Bauleitung klären", due = 2)
        item("0193a1b2-de00-7000-8000-000000002005", PRAXIS, "task", "CM-Messung Behandlungsraum 2", due = 2)

        fun party(n: Int, role: String, name: String, company: String? = null, phone: String? = null, email: String? = null, note: String? = null,
            mobile: String? = null, street: String? = null, city: String? = null, primary: Boolean = false) {
            result += change(EntityTypes.PARTY, json("id" to "0193a1b2-de00-7000-8000-00000000%04d".format(4000 + n), "bauakteId" to EFH, "role" to role,
                "name" to name, "company" to company, "phone" to phone, "email" to email, "note" to note, "contactId" to null, "version" to 1, "mobile" to mobile,
                "street" to street, "postalCode" to if (city == null) null else "84028", "city" to city, "isPrimary" to primary, "updatedAt" to time(-86_400 * 6)))
        }
        party(1, "architect", "Anna Lechner", company = "Lechner Architekten", phone = "0871 430 22 10", email = "lechner@architekten.example",
            street = "Neustadt 18", city = "Landshut")
        party(2, "siteManager", "Stefan Wimmer", company = "Wimmer Hausbau GmbH", phone = "0871 445 56 0", email = "s.wimmer@wimmer-hausbau.example",
            note = "Bauleitung, Abnahme der Estricharbeiten", mobile = "0171 445 56 67", street = "Gewerbestraße 4", city = "Landshut", primary = true)
        party(3, "other", "Haustechnik Bauer GmbH", phone = "0871 66 12 0", note = "Heizung: Druckprobe vor dem Einbringen")
        party(4, "supplier", "Transportbeton Isar GmbH", phone = "0871 77 88 0", note = "Disposition ab 6 Uhr")

        fun room(n: Int, floor: String, name: String, area: Double, laid: Boolean, heated: Boolean = true, thickness: Int = 65, notes: String? = null) {
            result += change(EntityTypes.ROOM, json("id" to "0193a1b2-de00-7000-8000-00000000%04d".format(3000 + n), "bauakteId" to EFH, "name" to name,
                "floor" to floor, "areaSquareMeters" to area, "thicknessMillimeters" to thickness, "insulationMillimeters" to 40, "underfloorHeating" to heated,
                "status" to if (laid) "laid" else "planned", "notes" to notes, "updatedAt" to time(-3600), "version" to 1))
        }
        room(1, "EG", "Wohnen/Essen", 41.2, true)
        room(2, "EG", "Küche", 13.4, true)
        room(3, "EG", "Flur", 10.6, true, notes = "Haarriss an der Haustür, siehe Mangel")
        room(4, "EG", "Gäste-WC", 3.4, true)
        room(5, "EG", "Hauswirtschaftsraum", 7.9, true, heated = false)
        room(6, "OG", "Schlafen", 17.2, false)
        room(7, "OG", "Kind 1", 13.1, false)
        room(8, "OG", "Kind 2", 12.8, false)
        room(9, "OG", "Bad", 10.4, false, thickness = 70, notes = "Gefälle zur bodengleichen Dusche")
        room(10, "OG", "Flur", 8.6, false)
        room(11, "OG", "Ankleide", 3.9, false)

        result += schemas().map { change(EntityTypes.FORM_SCHEMA, it) }
        result += valueLists().map { change(EntityTypes.VALUE_LIST, it) }
        for (p in photos) {
            result += change(EntityTypes.FILE, json("id" to p.id, "bauakteId" to p.bauakteId, "kind" to "photo", "originalName" to "Foto.jpg",
                "contentType" to "image/jpeg", "sizeBytes" to 412_000, "sha256" to "0".repeat(64), "state" to "stored", "caption" to p.caption,
                "capturedAt" to time(-p.ageSeconds), "createdAt" to time(-p.ageSeconds), "uploadedByUserId" to user, "version" to 1))
        }
        result += change(EntityTypes.FILE, json("id" to PDF, "bauakteId" to EFH, "kind" to "document", "originalName" to "Lieferschein 4711.pdf",
            "contentType" to "application/pdf", "sizeBytes" to 184_320, "sha256" to "0".repeat(64), "state" to "stored", "caption" to "Lieferschein 4711",
            "category" to "delivery", "capturedAt" to time(-7200), "createdAt" to time(-7200), "uploadedByUserId" to user, "version" to 1))
        return result
    }

    private fun field(key: String, label: String, type: String, list: String? = null, unit: String? = null, help: String? = null, badge: Boolean = false,
        photos: Boolean = false, path: String? = null, target: String? = null) = mapOf(
        "key" to key, "label" to label, "type" to type, "path" to (path ?: "project.$key"), "list" to list, "unit" to unit, "required" to false,
        "help" to help, "badge" to badge, "builtin" to true, "photos" to photos, "target" to target,
    )

    private fun schemas(): List<JsonObject> {
        val bauakte = listOf(
            mapOf("key" to "bauvorhaben", "label" to "Bauvorhaben", "fields" to listOf(
                field("objectType", "Objektart", "select", list = "objectType", badge = true), field("floors", "Geschosse", "text", help = "z. B. KG, EG, OG"),
                field("totalArea", "Gesamtfläche", "number", unit = "m²", badge = true), field("priority", "Priorität", "select", list = "priority"))),
            mapOf("key" to "estrich", "label" to "Estrich", "fields" to listOf(
                field("screedType", "Estrichart", "select", list = "screedType", badge = true), field("construction", "Konstruktion", "select", list = "construction"),
                field("nominalThicknessMm", "Nenndicke", "integer", unit = "mm", badge = true), field("strengthClass", "Festigkeitsklasse", "select", list = "strengthClass"),
                field("underfloorHeating", "Fußbodenheizung", "boolean", badge = true), field("heatingType", "Art der Heizung", "select", list = "heatingType"),
                field("insulation", "Dämmung", "text", help = "z. B. EPS 035 DEO"), field("insulationThicknessMm", "Dämmdicke", "integer", unit = "mm"))),
            mapOf("key" to "baustelle", "label" to "Baustelle", "fields" to listOf(
                field("siteAccess", "Zugang / Besonderheiten", "textarea", help = "Pumpenstandort, Zufahrt, Etage"),
                field("waterOnSite", "Wasser vor Ort", "boolean"), field("powerOnSite", "Strom vor Ort", "boolean"))),
        )
        val inspection = listOf(
            mapOf("key" to "pruefpunkte", "label" to "Prüfpunkte", "fields" to listOf(
                field("substrate", "Untergrund tragfähig, eben und sauber", "check", photos = true, path = "custom.substrate"),
                field("edgeInsulation", "Randdämmstreifen vorhanden", "check", photos = true, path = "custom.edgeInsulation"),
                field("heatingPressureTest", "Heizung: Druckprüfung erfolgt", "check", photos = true, path = "custom.heatingPressureTest"),
                field("access", "Zufahrt und Pumpenstandort geklärt", "check", path = "custom.access"),
                field("utilities", "Wasser und Strom vor Ort", "check", path = "custom.utilities"))),
            mapOf("key" to "messwerte", "label" to "Messwerte und Aufbau", "fields" to listOf(
                field("measuredArea", "Gemessene Fläche", "number", unit = "m²", path = "custom.measuredArea", target = "project.totalArea"),
                field("screedType", "Estrichart", "select", list = "screedType", path = "custom.screedType", target = "project.screedType"),
                field("thickness", "Estrichdicke", "integer", unit = "mm", path = "custom.thickness", target = "project.nominalThicknessMm"),
                field("underfloorHeating", "Fußbodenheizung", "boolean", path = "custom.underfloorHeating", target = "project.underfloorHeating"),
                field("water", "Wasser vor Ort", "boolean", path = "custom.water", target = "project.waterOnSite"),
                field("power", "Strom vor Ort", "boolean", path = "custom.power", target = "project.powerOnSite"))),
        )
        return listOf(
            json("id" to "0193a1b2-de00-7000-8000-0000000f0001", "key" to "bauakte", "version" to 1, "groups" to bauakte, "lists" to null),
            json("id" to "0193a1b2-de00-7000-8000-0000000f0002", "key" to "erstbegehung", "version" to 1, "groups" to inspection, "lists" to null),
        )
    }

    private fun valueLists(): List<JsonObject> {
        val lists = listOf(
            "objectType" to listOf(Triple("efh", "Einfamilienhaus", "EFH"), Triple("mfh", "Mehrfamilienhaus", "MFH"), Triple("commercial", "Gewerbe", null),
                Triple("industrial", "Industrie/Halle", "Halle"), Triple("public", "Öffentlicher Bau", null), Triple("renovation", "Sanierung", null),
                Triple("other", "Sonstiges", null)),
            "screedType" to listOf(Triple("ct", "Zementestrich (CT)", "CT"), Triple("caf", "Calciumsulfat-Fließestrich (CAF)", "CAF"),
                Triple("as", "Gussasphalt (AS)", "AS"), Triple("dry", "Trockenestrich", null), Triple("other", "Sonstiges", null)),
            "construction" to listOf(Triple("bonded", "Verbundestrich", null), Triple("separatingLayer", "Auf Trennlage", null),
                Triple("floating", "Schwimmend", null), Triple("heated", "Heizestrich", null)),
            "strengthClass" to listOf("C20-F3", "C25-F4", "C30-F5", "C35-F5", "CA-C25-F4", "CA-C30-F5").map { Triple(it, it, null) },
            "priority" to listOf(Triple("low", "Niedrig", null), Triple("normal", "Normal", null), Triple("high", "Hoch", null), Triple("urgent", "Dringend", null)),
            "heatingType" to listOf(Triple("water", "Warmwasser-Fußbodenheizung", null), Triple("electric", "Elektrische Fußbodenheizung", null),
                Triple("cooling", "Fußbodenkühlung", null)),
        )
        var counter = 0
        return lists.flatMap { (list, items) ->
            items.mapIndexed { index, (key, label, short) ->
                counter++
                json("id" to "0193a1b2-de00-7000-8000-0000001%05x".format(counter), "list" to list, "key" to key, "label" to label, "shortLabel" to short,
                    "sortOrder" to (index + 1) * 10, "isActive" to true, "version" to 1)
            }
        }
    }

    // Planung

    private val me = listOf(mapOf("id" to USER_ID, "name" to USER_NAME), mapOf("id" to "0193a1b2-de00-7000-8000-000000004002", "name" to "Lukas Brandl"))

    fun myPlanning(from: LocalDate): JsonObject {
        fun job(id: String, offset: Long, start: String, activity: String, bauakte: String, number: String, title: String, address: String,
            resources: List<Map<String, String>> = emptyList(), notes: String? = null, warnings: List<String> = emptyList(), end: String? = null) = mapOf(
            "id" to "0193a1b2-de00-7000-8000-00000000$id", "day" to from.plusDays(offset).toString(), "startTime" to start, "endTime" to end,
            "activity" to activity, "bauakteId" to bauakte, "bauakteNumber" to number, "bauakteTitle" to title, "bauakteAddress" to address,
            "crewName" to "Kolonne Huber", "crewColor" to "#E07A1F", "people" to me, "resources" to resources, "notes" to notes, "warnings" to warnings,
        )
        val pump = listOf(mapOf("id" to "0193a1b2-de00-7000-8000-000000005001", "name" to "Estrichpumpe 1", "kind" to "machine"))
        return json(
            "from" to from.toString(), "to" to from.plusDays(6).toString(),
            "assignments" to listOf(
                job("3001", 0, "07:00:00", "Estrich einbringen OG", EFH, "2026-041", "EFH Schneider, Neubau", "Ahornweg 3, 84028 Landshut", pump,
                    "Pumpe vorher prüfen", end = "15:00:00"),
                job("3003", 1, "07:30:00", "CM-Messung und Belegreife", PRAXIS, "2026-038", "Praxis Dr. Maier, Sanierung", "Altstadt 12, 84028 Landshut"),
                job("3005", 1, "10:30:00", "Estrich OG nacharbeiten", EFH, "2026-041", "EFH Schneider, Neubau", "Ahornweg 3, 84028 Landshut"),
                job("3006", 2, "07:00:00", "Erstbegehung", ISARPARK, "2026-044", "MFH Isarpark, Haus B", "Am Isarpark 7, 84034 Landshut"),
                job("3007", 3, "06:30:00", "Estrich einbringen", HALLE, "2026-042", "Halle Gewerbering", "Gewerbering 3, 84030 Ergolding", pump,
                    warnings = listOf("Zufahrt nur bis 7,5 t")),
            ),
            "absences" to emptyList<String>(),
        )
    }

    fun dispatch(day: String): JsonObject = json(
        "day" to day, "canPlan" to false, "mySiteIds" to listOf(EFH),
        "sites" to listOf(
            mapOf("bauakteId" to EFH, "number" to "2026-041", "title" to "EFH Schneider, Neubau", "address" to "Ahornweg 3, 84028 Landshut",
                "latitude" to 48.5372, "longitude" to 12.1521, "siteContactName" to "Herr Schneider", "siteContactPhone" to "0171 2345678",
                "startTime" to "07:00:00", "warnings" to emptyList<String>(),
                "jobs" to listOf(mapOf("assignmentId" to "0193a1b2-de00-7000-8000-000000003001", "activity" to "Estrich einbringen OG", "startTime" to "07:00:00",
                    "endTime" to "15:00:00", "crewName" to "Kolonne Huber", "crewColor" to "#E07A1F", "notes" to "Pumpe vorher prüfen")),
                "people" to me, "vehicles" to listOf(mapOf("id" to "0193a1b2-de00-7000-8000-000000005001", "name" to "Estrichpumpe 1"))),
            mapOf("bauakteId" to HALLE, "number" to "2026-042", "title" to "Halle Gewerbering", "address" to "Gewerbering 3, 84030 Ergolding",
                "startTime" to "08:30:00", "warnings" to listOf("Zufahrt nur bis 7,5 t"),
                "jobs" to listOf(mapOf("assignmentId" to "0193a1b2-de00-7000-8000-000000003002", "activity" to "Erstbegehung", "startTime" to "08:30:00",
                    "endTime" to "09:30:00", "crewName" to "Kolonne Brandl", "crewColor" to "#2F80ED")),
                "people" to listOf(mapOf("id" to "0193a1b2-de00-7000-8000-000000004003", "name" to "Anna Koller")), "vehicles" to emptyList<String>()),
            mapOf("bauakteId" to ISARPARK, "number" to "2026-044", "title" to "MFH Isarpark, Haus B", "address" to "Am Isarpark 7, 84034 Landshut",
                "startTime" to "10:00:00", "warnings" to emptyList<String>(),
                "jobs" to listOf(mapOf("assignmentId" to "0193a1b2-de00-7000-8000-000000003004", "activity" to "Randdämmung und Folie", "startTime" to "10:00:00",
                    "endTime" to "16:00:00", "crewName" to "Kolonne Kaya", "crewColor" to "#3A9D6B")),
                "people" to listOf(mapOf("id" to "0193a1b2-de00-7000-8000-000000004005", "name" to "Emre Kaya"),
                    mapOf("id" to "0193a1b2-de00-7000-8000-000000004006", "name" to "Jonas Wirth")),
                "vehicles" to listOf(mapOf("id" to "0193a1b2-de00-7000-8000-000000005002", "name" to "Sprinter LA-EO 12"))),
        ),
    )

    /** Systemhinweise wie in den iOS-Screenshots: Störung, geplante Wartung in 2 Tagen, Neuigkeit. */
    fun notices(now: Instant = Instant.now()): JsonObject {
        val start = ZonedDateTime.now(Dates.zone).toLocalDate().plusDays(2).atTime(22, 0).atZone(Dates.zone).toInstant()
        return json(
            "announcements" to listOf(
                mapOf("id" to "demo-crit", "title" to "Störung beim E-Mail-Versand",
                    "text" to "Ausgehende E-Mails werden derzeit verzögert zugestellt. Die Störung wird behoben, Nachrichten gehen nicht verloren.",
                    "severity" to "critical", "startsAt" to null, "endsAt" to null, "source" to "console"),
                mapOf("id" to "demo-info", "title" to "Neu: Räume per Foto erfassen",
                    "text" to "Im Bereich Räume lassen sich Maße aus einem Foto übernehmen.", "severity" to "info", "startsAt" to null,
                    "endsAt" to ApiDate.format(now.plusSeconds(5 * 86_400)), "source" to "instance"),
            ),
            "maintenance" to mapOf("active" to false, "message" to "Update auf Version 1.1. EstrichONE ist in dieser Zeit nicht erreichbar. Bitte vorher abgleichen.",
                "startsAt" to ApiDate.format(start), "endsAt" to ApiDate.format(start.plusSeconds(90 * 60)), "adminsMayWork" to true, "source" to "instance"),
            "serverTime" to ApiDate.format(now),
        )
    }
}
