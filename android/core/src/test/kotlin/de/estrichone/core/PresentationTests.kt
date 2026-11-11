package de.estrichone.core

import kotlinx.coroutines.runBlocking
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertNotEquals
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.DisplayName
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.assertThrows
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneId
import java.time.ZonedDateTime

class PresentationTests {
    private val zone = ZoneId.of("Europe/Berlin")

    private fun at(day: Int, hour: Int, minute: Int = 0, month: Int = 11, year: Int = 2026): Instant =
        ZonedDateTime.of(year, month, day, hour, minute, 0, 0, zone).toInstant()

    /** Freitag, 06.11.2026, 12:00 Uhr. */
    private val now = at(6, 12)

    private fun decode(text: String) = EstrichOneApi.json.decodeFromString(SystemNotices.serializer(), text)

    @Test
    @DisplayName("Systemhinweise: Zeitraum, gleicher Tag, über Mitternacht, nur Beginn bzw. Ende, anderes Jahr")
    fun period() {
        assertEquals("So., 08.11., 22:00–23:30 Uhr", NoticePresentation.period(at(8, 22), at(8, 23, 30), now, zone))
        assertEquals("Sa., 07.11., 22:00 Uhr – So., 08.11., 02:00 Uhr", NoticePresentation.period(at(7, 22), at(8, 2), now, zone))
        assertEquals("ab Sa., 07.11., 06:05 Uhr", NoticePresentation.period(at(7, 6, 5), null, now, zone))
        assertEquals("bis Fr., 13.11., 18:00 Uhr", NoticePresentation.period(null, at(13, 18), now, zone))
        assertEquals("", NoticePresentation.period(null, null, now, zone))
        assertEquals("Sa., 02.01.2027, 08:00–09:00 Uhr", NoticePresentation.period(at(2, 8, month = 1, year = 2027), at(2, 9, month = 1, year = 2027), now, zone))
    }

    @Test
    @DisplayName("Systemhinweise: relative Angabe und Restdauer")
    fun relative() {
        fun plus(minutes: Long) = now.plusSeconds(minutes * 60)
        assertEquals("in 45 Min.", NoticePresentation.relative(plus(45), plus(90), now, zone))
        assertEquals("heute, in 5 Std.", NoticePresentation.relative(at(6, 17), null, now, zone))
        assertEquals("morgen", NoticePresentation.relative(at(7, 9), null, now, zone))
        assertEquals("in 2 Tagen", NoticePresentation.relative(at(8, 22), at(8, 23), now, zone))
        assertEquals("läuft seit 10 Min.", NoticePresentation.relative(plus(-10), plus(20), now, zone))
        assertEquals("läuft seit 2 Std. 30 Min.", NoticePresentation.relative(plus(-150), null, now, zone))
        assertEquals("beendet", NoticePresentation.relative(plus(-60), plus(-1), now, zone))
        assertEquals("noch 20 Min.", NoticePresentation.remaining(plus(20), now))
        assertEquals("noch 3 Tage", NoticePresentation.remaining(plus(60 * 24 * 3), now))
        assertEquals("", NoticePresentation.remaining(plus(-5), now))
        assertEquals("1 Tag", NoticePresentation.duration(86_400))
    }

    @Test
    @DisplayName("Systemhinweise: Art, Farbe, Ausblendbarkeit und Reihenfolge")
    fun kinds() {
        val notices = decode("""
        {"announcements":[
           {"id":"i","title":"Neu","text":"PDF","severity":"info","startsAt":null,"endsAt":null,"source":"instance"},
           {"id":"c","title":"Störung","text":"Mail","severity":"critical","startsAt":null,"endsAt":null,"source":"console"},
           {"id":"w","title":"Achtung","text":"x","severity":"warning","startsAt":null,"endsAt":null}],
         "maintenance":{"active":false,"message":"Update","startsAt":"${ApiDate.format(at(8, 22))}","endsAt":"${ApiDate.format(at(8, 23, 30))}","adminsMayWork":true,"source":"console"},
         "serverTime":null}
        """)
        val items = NoticePresentation.items(notices, now, zone)
        assertEquals(listOf(NoticeItem.Kind.Critical, NoticeItem.Kind.Warning, NoticeItem.Kind.Maintenance, NoticeItem.Kind.Info), items.map { it.kind })
        assertEquals(NoticeItem.Tone.Error, items[0].tone)
        assertFalse(items[0].isDismissible)
        assertEquals("Betreiber", items[0].source)
        val maintenance = items[2]
        assertEquals("Geplante Wartung", maintenance.title)
        assertEquals("in 2 Tagen", maintenance.relative)
        assertEquals("So., 08.11., 22:00–23:30 Uhr", maintenance.period)

        val (shown, hidden) = NoticePresentation.selection(items, emptySet(), expanded = false)
        assertEquals(listOf(NoticeItem.Kind.Critical), shown.map { it.kind })
        assertEquals(3, hidden)
        val (after, _) = NoticePresentation.selection(items, setOf(items[0].id, items[1].id), expanded = true)
        assertEquals(listOf(NoticeItem.Kind.Critical, NoticeItem.Kind.Maintenance, NoticeItem.Kind.Info), after.map { it.kind })
        assertEquals("1 weiterer Hinweis", NoticePresentation.moreText(1))
    }

    @Test
    @DisplayName("Systemhinweise: Wartung in Kürze und laufende Wartung")
    fun maintenance() {
        val soon = decode("""{"announcements":[],"maintenance":{"active":false,"message":null,"startsAt":"${ApiDate.format(now.plusSeconds(1800))}","endsAt":null,"adminsMayWork":true},"serverTime":null}""")
        val item = NoticePresentation.items(soon, now, zone).first()
        assertEquals(NoticeItem.Kind.MaintenanceSoon, item.kind)
        assertEquals("in 30 Min.", item.relative)
        val early = NoticePresentation.items(soon, now.minusSeconds(3 * 3600), zone).first()
        assertEquals(NoticeItem.Kind.Maintenance, early.kind)
        assertNotEquals(item.id, early.id)

        val running = decode("""{"announcements":[],"maintenance":{"active":true,"message":"Umzug","startsAt":"${ApiDate.format(now.minusSeconds(600))}","endsAt":"${ApiDate.format(now.plusSeconds(1200))}","adminsMayWork":true},"serverTime":null}""")
        val live = NoticePresentation.items(running, now, zone).first()
        assertEquals(NoticeItem.Kind.MaintenanceRunning, live.kind)
        assertFalse(live.isDismissible)
        assertEquals("läuft seit 10 Min.", live.relative)
        assertEquals("noch 20 Min.", live.remaining)
    }

    @Test
    @DisplayName("Inhaltsschlüssel wie im Web (FNV-1a)")
    fun contentHash() {
        assertEquals("ztntfp", NoticePresentation.contentHash("")) // 0x811C9DC5
        assertNotEquals(NoticePresentation.contentHash("a"), NoticePresentation.contentHash("b"))
    }

    @Test
    @DisplayName("Ausfall-Hinweise nur, wenn der Server sie liefert")
    fun healthBanners() {
        assertTrue(SystemHealth.banners(EstrichOneApi.json.parseToJsonElement("""{"backup":{"configured":true},"banners":[]}""")).isEmpty())
        val banners = SystemHealth.banners(EstrichOneApi.json.parseToJsonElement(
            """{"banners":[{"key":"backup","severity":"critical","text":"ACHTUNG"},{"key":"archive","severity":"warning","text":"Hinweis"},{"key":"x","text":""}]}"""))
        assertEquals(listOf(true, false), banners.map { it.critical })
        assertTrue(SystemHealth.banners(null).isEmpty())
    }

    @Test
    @DisplayName("Benachrichtigung: Ziel aus Link oder Objekttyp")
    fun routes() {
        val id = "0193a1b2-de00-7000-8000-0000000b0001"
        assertEquals(NotificationRoute.Bauakte(id), NotificationRoute.from("/bauakten/$id"))
        assertEquals(NotificationRoute.Bauakte(id), NotificationRoute.from("estrichone://bauakte/$id"))
        assertEquals(NotificationRoute.Ticket(id), NotificationRoute.from("/it-center/helpdesk/$id"))
        assertEquals(NotificationRoute.Planning("2026-11-09"), NotificationRoute.from("/planung?day=2026-11-09"))
        assertEquals(NotificationRoute.Chat(id), NotificationRoute.from(null, "chatConversation", id))
        assertEquals(NotificationRoute.Tasks, NotificationRoute.from(null, "workItem", null))
        assertEquals(NotificationRoute.Absences(), NotificationRoute.from("/urlaub"))
        assertNull(NotificationRoute.from(null))
    }

    @Test
    @DisplayName("PIN: 6 Ziffern, keine trivialen Folgen; Tresor öffnet nur mit richtiger PIN")
    fun pin() {
        assertEquals("Die PIN besteht aus genau 6 Ziffern.", PinPolicy.violation("12345"))
        assertEquals("Die PIN besteht aus genau 6 Ziffern.", PinPolicy.violation("12a456"))
        listOf("123456", "111111", "654321", "121212", "123123", "890123").forEach { assertTrue(PinPolicy.violation(it) != null, it) }
        assertNull(PinPolicy.violation("482915"))
        val sealed = PinVault.seal("refresh-token", "482915", iterations = 1000)
        assertEquals("refresh-token", PinVault.open(sealed, "482915"))
        assertThrows<WrongPinException> { PinVault.open(sealed, "482916") }
    }

    @Test
    @DisplayName("Sperre: Zeitspanne bis zur Sperre")
    fun lockDelay() {
        assertTrue(LockDelay.Immediately.shouldLock(0, 0))
        assertFalse(LockDelay.FiveMinutes.shouldLock(0, 299_000))
        assertTrue(LockDelay.FiveMinutes.shouldLock(0, 300_000))
    }

    @Test
    @DisplayName("Anzeige: Fälligkeit, Begrüßung, Diktat, Initialen, Zahlen")
    fun texts() {
        val today = LocalDate.of(2026, 11, 6)
        assertEquals("heute fällig", dueText(today, today))
        assertEquals("morgen fällig", dueText(today.plusDays(1), today))
        assertEquals("seit 3 Tagen fällig", dueText(today.minusDays(3), today))
        assertEquals("fällig Mi.", dueText(today.plusDays(5), today))
        assertEquals("Guten Morgen", Dates.greeting(java.time.LocalTime.of(7, 0)))
        assertEquals("Guten Abend", Dates.greeting(java.time.LocalTime.of(21, 0)))
        assertEquals("Bitte Pumpe prüfen", ChatLimits.appendDictation("", "bitte Pumpe prüfen"))
        assertEquals("Hallo. Bin gleich da", ChatLimits.appendDictation("Hallo.", "bin gleich da"))
        assertEquals("Ich komme später", ChatLimits.appendDictation("Ich komme", "später"))
        assertFalse(ChatLimits.canSend("   "))
        assertTrue(ChatLimits.canSend("", attachments = 1))
        assertEquals("MH", initials("Max Huber"))
        assertEquals("142,5", formatNumber(142.5))
        assertEquals("600", formatNumber(600.0))
        assertEquals(1, floorRank("EG"))
        assertTrue(floorRank("KG") < floorRank("EG") && floorRank("EG") < floorRank("OG") && floorRank("1. OG") < floorRank("2. OG"))
    }

    @Test
    @DisplayName("Demoserver: Chat, Postfach, Meldungen und Planung")
    fun demoServices() = runBlocking {
        val api = EstrichOneApi("https://demo.invalid", MemorySessionStore(), SoftwareDeviceKeys(), DemoServer().client())
        api.signIn("demo", "demo", "Test")
        assertTrue(api.chatStatus().isAvailable)
        val conversations = api.chatConversations()
        assertEquals(5, conversations.size)
        val site = conversations.first { it.id == DemoChat.SITE }
        assertEquals(ChatType.Site, site.type)
        val page = api.chatMessages(site.id)
        assertTrue(page.items.any { it.attachments.any(ChatAttachment::isPhoto) })
        val sent = api.sendChatMessage(site.id, "Bin unterwegs", Ids.v7())
        assertTrue(sent.mine)
        assertEquals(sent.id, api.chatMessages(site.id).items.last().id)
        val inbox = api.notifications()
        assertEquals(inbox.items.count { !it.isRead }, inbox.unreadCount)
        assertTrue(api.myTickets().isNotEmpty())
        val ticket = api.createTicket("Kamera startet nicht", null, TicketCategory.AppError, false, null, json("platform" to "Android"))
        assertEquals("new", ticket.summary.status)
        val plan = api.myPlan(LocalDate.now().toString(), LocalDate.now().plusDays(6).toString())
        assertEquals("Estrich einbringen OG", plan.assignments.first().activity)
        assertEquals("07:00 – 15:00", plan.assignments.first().time)
        assertEquals(3, NoticePresentation.items(api.systemNotices(), Instant.now()).size)
        assertTrue(api.healthBanners().isEmpty())
    }
}
