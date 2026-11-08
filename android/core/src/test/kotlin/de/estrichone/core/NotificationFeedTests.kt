package de.estrichone.core

import kotlinx.coroutines.runBlocking
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.DisplayName
import org.junit.jupiter.api.Test
import java.time.Instant
import java.time.ZoneId
import java.time.ZonedDateTime

class NotificationFeedTests {
    private val zone = ZoneId.of("Europe/Berlin")

    /** Mittwoch, 4.11.2026, 10:00 Uhr in Berlin (wie NotificationFeedTests.swift). */
    private val now: Instant = ZonedDateTime.of(2026, 11, 4, 10, 0, 0, 0, zone).toInstant()

    private fun ago(hours: Double): Instant = now.minusSeconds((hours * 3600).toLong())

    private fun item(n: Int, category: String?, title: String, body: String? = null, link: String? = null, objectType: String? = null,
        sender: String? = null, hoursAgo: Double, read: Boolean = false) = InboxNotification(
        id = "0193a1b2-0000-7000-8000-%012d".format(n), category = category, urgency = "normal", title = title, body = body, link = link,
        objectType = objectType, senderName = sender, createdAt = ApiDate.format(ago(hoursAgo)), readAt = if (read) ApiDate.format(now) else null)

    private val sample = listOf(
        item(1, "planning", "Plan für morgen freigegeben", hoursAgo = 0.2),
        item(2, "chat", "Neue Nachricht von Sabine Wagner", hoursAgo = 1.0),
        item(3, "work", "Mangel zugewiesen", body = "Randfuge Flur · Praxis Dr. Maier", hoursAgo = 9.0, read = true),
        item(4, "ticket", "Antwort zu T-2026-0007", hoursAgo = 14.0),
        item(5, "bauakte", "Abnahme unterschrieben", hoursAgo = 40.0, read = true),
        item(6, "message", "Nachricht aus dem Büro", body = "Pumpe 2 erst ab 6:45 Uhr", sender = "Sabine Wagner", hoursAgo = 60.0),
        item(7, "system", "Wartung angekündigt", hoursAgo = 24.0 * 9, read = true),
    )

    @Test
    @DisplayName("Zeiträume: Heute, Gestern, Diese Woche (ab Montag), Älter")
    fun periods() {
        assertEquals(NotificationPeriod.Today, NotificationPeriod.of(ago(1.0), now, zone))
        assertEquals(NotificationPeriod.Today, NotificationPeriod.of(now.plusSeconds(60), now, zone))
        assertEquals(NotificationPeriod.Yesterday, NotificationPeriod.of(ago(14.0), now, zone))
        assertEquals(NotificationPeriod.ThisWeek, NotificationPeriod.of(ago(40.0), now, zone))
        assertEquals(NotificationPeriod.Older, NotificationPeriod.of(ago(60.0), now, zone))
        val sections = NotificationFeed.sections(sample, now = now, zone = zone)
        assertEquals(listOf("Heute", "Gestern", "Diese Woche", "Älter"), sections.map { it.title })
        assertEquals(listOf("Plan für morgen freigegeben", "Neue Nachricht von Sabine Wagner", "Mangel zugewiesen"), sections[0].items.map { it.title })
        assertEquals(2, sections[0].unreadCount)
        assertEquals(2, sections[3].items.size)
    }

    @Test
    @DisplayName("Gruppiert nach Art: ungelesene Arten zuerst")
    fun byKind() {
        val sections = NotificationFeed.sections(sample, NotificationGrouping.Kind, now, zone)
        assertEquals("Planung", sections.first().title)
        assertEquals("System", sections.last().title)
        assertEquals(sections.size, sections.map { it.id }.toSet().size)
    }

    @Test
    @DisplayName("Filter: Ungelesen, Art und Suche ohne Akzente")
    fun filters() {
        assertEquals(4, NotificationFeed.filter(sample, NotificationFilter(unreadOnly = true)).size)
        assertEquals(listOf("Mangel zugewiesen"), NotificationFeed.filter(sample, NotificationFilter(kind = NotificationKind.Work)).map { it.title })
        assertEquals(1, NotificationFeed.filter(sample, NotificationFilter(query = "praxis maier")).size)
        assertEquals(2, NotificationFeed.filter(sample, NotificationFilter(query = "SABINE")).size)
        assertEquals(1, NotificationFeed.filter(sample, NotificationFilter(query = "buro")).size)
        assertTrue(NotificationFeed.filter(sample, NotificationFilter(unreadOnly = true, kind = NotificationKind.System)).isEmpty())
        assertFalse(NotificationFilter().isActive)
        assertTrue(NotificationFilter(query = " x ").isActive)
        assertEquals(listOf(NotificationKind.Planning, NotificationKind.Bauakte, NotificationKind.Work, NotificationKind.Chat, NotificationKind.Ticket,
            NotificationKind.Office, NotificationKind.System), NotificationFeed.kinds(sample))
        assertEquals(1, NotificationFeed.unreadCounts(sample)[NotificationKind.Chat])
        assertNull(NotificationFeed.unreadCounts(sample)[NotificationKind.Work])
        assertEquals("Alles gelesen", NotificationFeed.emptyMessage(NotificationFilter(unreadOnly = true)).first)
        assertTrue(NotificationFeed.emptyMessage(NotificationFilter(query = "xy")).second.contains("„xy“"))
    }

    @Test
    @DisplayName("Zeit in der Zeile wie in Mail")
    fun times() {
        assertEquals("jetzt", NotificationFeed.timeText(now.minusSeconds(20), now, zone))
        assertEquals("vor 12 Min.", NotificationFeed.timeText(now.minusSeconds(12 * 60), now, zone))
        assertEquals("07:00", NotificationFeed.timeText(ago(3.0), now, zone))
        assertEquals("Gestern", NotificationFeed.timeText(ago(14.0), now, zone))
        assertEquals("Montag", NotificationFeed.timeText(ago(40.0), now, zone))
        assertEquals("26.10.", NotificationFeed.timeText(ago(24.0 * 9), now, zone))
        assertEquals("30.9.25", NotificationFeed.timeText(ago(24.0 * 400), now, zone))
        assertEquals("Mittwoch, 4. November 2026, 10:00 Uhr", NotificationFeed.fullTimeText(now, zone))
    }

    @Test
    @DisplayName("Kategorien des Servers und ältere Schreibweisen")
    fun kinds() {
        assertEquals(NotificationKind.Bauakte, NotificationKind.of("approval"))
        assertEquals(NotificationKind.Work, NotificationKind.of("deadline"))
        assertEquals(NotificationKind.Personal, NotificationKind.of("absence"))
        assertEquals(NotificationKind.Planning, NotificationKind.of("planning.released"))
        assertEquals(NotificationKind.Office, NotificationKind.of("broadcast"))
        assertEquals(NotificationKind.Office, NotificationKind.of(null))
    }

    @Test
    @DisplayName("Ziele aller Links des Servers")
    fun targets() {
        val id = "0193a1b2-0000-7000-8000-0000000b0001"
        assertEquals(NotificationRoute.Bauakte(id), NotificationRoute.from("/bauakten/$id/maengel"))
        assertEquals(NotificationRoute.Bauakte(id), NotificationRoute.from("/api/v1/bauakten/$id/notes"))
        assertEquals(NotificationRoute.Chat(id), NotificationRoute.from("/chat/$id"))
        assertEquals(NotificationRoute.Tasks, NotificationRoute.from("/start", "workItem", id))
        assertEquals(NotificationRoute.Inbox, NotificationRoute.from("/it-center/meldungen/$id", "feedback", id))
        assertEquals(NotificationRoute.Inbox, NotificationRoute.from("/personal/urlaub"))
        assertEquals(NotificationRoute.Inbox, NotificationRoute.from("/system/backup-ziele"))

        val system = item(9, "system", "Sicherung fehlgeschlagen", link = "/system/backup-ziele", hoursAgo = 1.0)
        assertFalse(system.hasTarget)
        assertEquals("/system/backup-ziele", system.webPath)
        val note = item(10, "bauakte", "Notiz", link = "/api/v1/bauakten/$id/notes", hoursAgo = 1.0)
        assertTrue(note.hasTarget)
        assertNull(note.webPath)
        assertFalse(item(11, "message", "Hallo", hoursAgo = 1.0).hasTarget)
        assertEquals("Bauakte öffnen", NotificationFeed.openTitle(NotificationRoute.Bauakte(id)))
    }

    @Test
    @DisplayName("Demoserver: Seite, gelesen/ungelesen, löschen, alle Ziele auflösbar")
    fun demoInbox() = runBlocking {
        val api = EstrichOneApi("https://demo.invalid", MemorySessionStore(), SoftwareDeviceKeys(), DemoServer().client())
        api.signIn("demo", "demo", "Test")
        val inbox = api.notifications(100)
        assertTrue(inbox.items.size >= 10)
        assertEquals(inbox.items.count { !it.isRead }, inbox.unreadCount)
        assertTrue(NotificationFeed.sections(inbox.items).size >= 2) // je nach Uhrzeit und Wochentag
        assertTrue(inbox.items.any { it.senderName != null && !it.hasTarget })
        val first = inbox.items.first { !it.isRead }
        api.markNotificationRead(first.id)
        assertEquals(inbox.unreadCount - 1, api.notifications().unreadCount)
        api.markNotificationUnread(first.id)
        assertEquals(inbox.unreadCount, api.notifications().unreadCount)
        api.deleteNotification(first.id)
        assertTrue(api.notifications().items.none { it.id == first.id })
    }
}
