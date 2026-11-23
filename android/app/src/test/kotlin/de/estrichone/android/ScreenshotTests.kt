package de.estrichone.android

import android.app.Application
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onRoot
import androidx.test.core.app.ApplicationProvider
import com.github.takahirom.roborazzi.captureRoboImage
import de.estrichone.android.ui.Appearance
import de.estrichone.android.ui.AreaKind
import de.estrichone.android.ui.EstrichTheme
import de.estrichone.android.ui.InboxPreset
import de.estrichone.android.ui.MainScreen
import de.estrichone.android.ui.Nav
import de.estrichone.android.ui.Root
import de.estrichone.android.ui.Route
import de.estrichone.android.ui.Tab
import de.estrichone.android.ui.WhatsNewContent
import de.estrichone.android.ui.weekPlan
import de.estrichone.core.AppDownload
import de.estrichone.core.AppReleaseNote
import de.estrichone.core.AppReleaseSection
import de.estrichone.core.AppUpdateCheck
import de.estrichone.core.UpdateState
import de.estrichone.core.UpdateStatus
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.ui.unit.dp
import de.estrichone.core.DemoChat
import de.estrichone.core.ApiDate
import de.estrichone.core.DemoData
import de.estrichone.core.InboxNotification
import de.estrichone.core.NotificationFilter
import de.estrichone.core.NotificationGrouping
import de.estrichone.core.myVacation
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import org.robolectric.annotation.GraphicsMode
import org.robolectric.shadows.ShadowLooper

/** Anwendung ohne Hintergrundarbeit für Robolectric. */
class TestApp : Application()

/**
 * Screenshots im Demomodus (wie ios/scripts/screenshots.sh): JVM ohne Emulator (Robolectric, native Grafik), Telefon und
 * Tablet, hell und dunkel. Ablage: app/build/outputs/roborazzi/<gerät>-<bildschirm>-<hell|dunkel>.png
 */
@RunWith(RobolectricTestRunner::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
abstract class ScreenshotBase(private val device: String, private val appearance: Appearance) {
    @get:Rule
    val compose = createComposeRule()

    private val suffix get() = if (appearance == Appearance.Dark) "dunkel" else "hell"

    init {
        de.estrichone.android.ui.Live.polling = false
        // Heute: „Jetzt“ beim ersten Einsatz wie auf iOS (9:41).
        de.estrichone.android.ui.Live.fixedTime = java.time.LocalTime.of(9, 41)
    }

    private fun demo(): AppModel {
        val model = AppModel(ApplicationProvider.getApplicationContext())
        model.startDemo()
        // Robolectric pausiert den Main-Looper: beim Warten selbst abarbeiten (Abgleich mit dem Demoserver, Systemhinweise).
        val deadline = System.currentTimeMillis() + 60_000
        while (!(model.session.value?.service?.bauakten()?.isNotEmpty() == true && model.session.value?.notices?.value != null)) {
            ShadowLooper.idleMainLooper()
            Thread.sleep(50)
            check(System.currentTimeMillis() < deadline) { "Demomodus nicht bereit: ${model.session.value?.status?.value}" }
        }
        return model
    }

    private fun shot(name: String, content: @Composable () -> Unit) {
        compose.setContent { EstrichTheme(appearance) { content() } }
        compose.waitForIdle()
        // Vorschaubilder und Abrufe laufen im Hintergrund.
        repeat(8) {
            Thread.sleep(200)
            ShadowLooper.idleMainLooper()
            compose.mainClock.advanceTimeBy(500)
            compose.waitForIdle()
        }
        compose.onRoot().captureRoboImage("build/outputs/roborazzi/$device-$name-$suffix.png")
    }

    private fun main(name: String, tab: Tab, routes: List<Route> = emptyList()) = scene(name, tab, {}) { routes }

    private fun main(name: String, tab: Tab, routes: (Session) -> List<Route>) = scene(name, tab, {}, routes)

    /** Hauptansicht mit vorbereitetem Modell (z. B. Zustand des App-Updates). */
    private fun scene(name: String, tab: Tab, prepare: (AppModel) -> Unit, routes: (Session) -> List<Route>) {
        val model = demo()
        prepare(model)
        val session = model.session.value!!
        val stack = routes(session)
        shot(name) {
            val nav = remember { Nav(tab).also { it.set(tab, stack) } }
            MainScreen(model, session, nav)
        }
    }

    @Test
    fun anmeldung() {
        val model = AppModel(ApplicationProvider.getApplicationContext())
        model.showStage(Stage.SignIn("EstrichONE Musterbetrieb", null, firstDevice = true, isPool = false))
        shot("anmeldung") { Root(model) }
    }

    @Test
    fun willkommen() {
        val model = AppModel(ApplicationProvider.getApplicationContext())
        model.showStage(Stage.Welcome)
        shot("willkommen") { Root(model) }
    }

    @Test
    fun heute() = main("heute", Tab.Today)

    /** Heute: nur die Einsätze (laufender aufgeklappt, nächster kompakt), ohne die Hinweise darüber. */
    @Test
    fun heuteEinsatz() {
        val model = demo()
        val session = model.session.value!!
        val today = java.time.LocalDate.now(de.estrichone.core.Dates.zone).toString()
        val jobs = kotlinx.coroutines.runBlocking { session.weekPlan() }?.assignments.orEmpty().filter { it.day == today }
            .sortedBy { it.startTime ?: "99" }
        shot("heute-einsatz") {
            val nav = remember { Nav(Tab.Today) }
            androidx.compose.runtime.CompositionLocalProvider(
                de.estrichone.android.ui.LocalNav provides nav, de.estrichone.android.ui.LocalSession provides session,
                de.estrichone.android.ui.LocalModel provides model,
            ) {
                androidx.compose.foundation.layout.Column(
                    androidx.compose.ui.Modifier.fillMaxSize().background(de.estrichone.android.ui.tones.canvas)
                        .padding(16.dp),
                    verticalArrangement = androidx.compose.foundation.layout.Arrangement.spacedBy(12.dp),
                ) {
                    jobs.forEachIndexed { index, job ->
                        val phase = de.estrichone.core.TodayGlance.phase(job.startTime, job.endTime, de.estrichone.android.ui.Live.clock())
                        de.estrichone.android.ui.TodayJobCard(job, session, phase, expanded = index == 0)
                    }
                }
            }
        }
    }

    /** Update-Ablauf: Ankündigung mit Countdown, Wartungsbildschirm im Update und im Abschluss (wie Web und iOS). */
    private fun updateScene(name: String, phase: String, seconds: Long, offline: Boolean = false) =
        scene(name, Tab.Today, { model ->
            val now = java.time.Instant.now()
            model.session.value!!.previewUpdate(de.estrichone.core.UpdateMaintenance(
                "demo-update", phase, "1.0.15", "1.0.16", now.minusSeconds(90).toString(), now.minusSeconds(60 - seconds).toString(),
                now.plusSeconds(seconds).toString(),
            ), offline)
        }) { emptyList() }

    @Test
    fun updateAnkuendigung() = updateScene("update-1-ankuendigung", "announce", 47)

    @Test
    fun updateLaeuft() = updateScene("update-2-update", "update", 38, offline = true)

    @Test
    fun updateAbschluss() = updateScene("update-3-abschluss", "finish", 52)

    @Test
    fun baustellen() = main("baustellen", Tab.Bauakten)

    @Test
    fun bauakte() = main("bauakte", Tab.Bauakten, listOf(Route.Bauakte(DemoData.EFH)))

    @Test
    fun fotos() = main("fotos", Tab.Bauakten, listOf(Route.Bauakte(DemoData.EFH), Route.Area(DemoData.EFH, AreaKind.Photos)))

    @Test
    fun kamera() = main("kamera", Tab.Bauakten, listOf(Route.Bauakte(DemoData.EFH), Route.Camera(DemoData.EFH)))

    @Test
    fun foto() = main("foto", Tab.Bauakten, listOf(Route.Bauakte(DemoData.EFH), Route.Photo(DemoData.photos.first().id)))

    @Test
    fun erstbegehungOffen() = main("erstbegehung-offen", Tab.Bauakten, listOf(Route.Bauakte(DemoData.ISARPARK)))

    @Test
    fun erstbegehungEntfaellt() = main("erstbegehung-entfaellt", Tab.Bauakten,
        listOf(Route.Bauakte(DemoData.PRAXIS), Route.Area(DemoData.PRAXIS, AreaKind.InitialInspection)))

    @Test
    fun raeume() = main("raeume", Tab.Bauakten, listOf(Route.Bauakte(DemoData.EFH), Route.Area(DemoData.EFH, AreaKind.Rooms)))

    @Test
    fun chat() = main("chat", Tab.Chat, listOf(Route.Chat(DemoChat.SITE)))

    @Test
    fun chatliste() = main("chatliste", Tab.Chat)

    /** Bauakte: Knopf „Erfassen“ öffnet das Erfassen-Blatt dieser Baustelle (unten gibt es kein „Erfassen“ mehr). */
    @Test
    fun bauakteErfassen() {
        val model = demo()
        val session = model.session.value!!
        shot("bauakte-erfassen") {
            val nav = remember { Nav(Tab.Bauakten).also { it.set(Tab.Bauakten, listOf(Route.Bauakte(DemoData.EFH))); it.captureOpenFor = DemoData.EFH } }
            MainScreen(model, session, nav)
        }
    }

    @Test
    fun benachrichtigungen() = inbox("benachrichtigungen")

    @Test
    fun benachrichtigungenUngelesen() = inbox("benachrichtigungen-ungelesen", NotificationFilter(unreadOnly = true))

    @Test
    fun benachrichtigungenNachArt() = inbox("benachrichtigungen-art", grouping = NotificationGrouping.Kind)

    @Test
    fun benachrichtigungDetail() = main("benachrichtigung-detail", Tab.Today, listOf(Route.Inbox, Route.NotificationDetail(InboxNotification(
        id = "0193a1b2-de00-7000-8000-00000000a003", category = "message", urgency = "normal", title = "Pumpe 2 ab Montag später",
        body = "Ab Montag Abholung der Pumpe 2 erst ab 6:45 Uhr am Lager. Bitte die Touren entsprechend planen und Material am Vortag laden.",
        senderName = "Sabine Wagner", createdAt = ApiDate.format(java.time.Instant.now().minusSeconds(3 * 3600)), readAt = null))))

    private fun inbox(name: String, filter: NotificationFilter = NotificationFilter(), grouping: NotificationGrouping = NotificationGrouping.Time) {
        InboxPreset.filter = filter
        InboxPreset.grouping = grouping
        try {
            main(name, Tab.Today, listOf(Route.Inbox))
        } finally {
            InboxPreset.filter = NotificationFilter()
            InboxPreset.grouping = NotificationGrouping.Time
        }
    }

    @Test
    fun helpdesk() = main("helpdesk", Tab.More, listOf(Route.Helpdesk))

    @Test
    fun helpdeskTicket() = main("helpdesk-ticket", Tab.More, listOf(Route.Helpdesk, Route.Ticket(DemoData.TICKET)))

    @Test
    fun helpdeskBewertung() = main("helpdesk-bewertung", Tab.More, listOf(Route.Helpdesk, Route.Ticket("0193a1b2-de00-7000-8000-00000000c004")))

    @Test
    fun problemMelden() = main("problem-melden", Tab.More, listOf(Route.Helpdesk, Route.ReportProblem))

    /** Demonutzer als IT-Bearbeiter: Rechte neu laden lassen und warten, bis sie da sind. */
    private fun agent(name: String, routes: List<Route>) = scene(name, Tab.More, { model ->
        val session = model.session.value!!
        session.demo!!.itAgent = true
        session.synchronizeNow()
        val deadline = System.currentTimeMillis() + 30_000
        while ("helpdesk.agent" !in session.permissions.value) {
            ShadowLooper.idleMainLooper()
            Thread.sleep(50)
            check(System.currentTimeMillis() < deadline) { "Rechte der Bearbeitung nicht geladen" }
        }
    }) { routes }

    @Test
    fun helpdeskItListe() = agent("helpdesk-it-liste", listOf(Route.Helpdesk))

    @Test
    fun helpdeskWartetAufSupport() = agent("helpdesk-warten-auf-support", listOf(Route.Helpdesk, Route.Ticket("0193a1b2-de00-7000-8000-00000000c005")))

    @Test
    fun helpdeskBearbeitung() = agent("helpdesk-bearbeitung", listOf(Route.Helpdesk, Route.Ticket(DemoData.TICKET)))

    @Test
    fun helpdeskMeldungAufnehmen() = agent("helpdesk-meldung-aufnehmen", listOf(Route.Helpdesk, Route.RecordTicket))

    @Test
    fun helpdeskAnleitung() = main("helpdesk-anleitung", Tab.More, listOf(Route.Helpdesk, Route.Ticket(DemoData.TICKET),
        Route.Article("0193a1b2-de00-7000-8000-00000000e001")))

    @Test
    fun einstellungen() = main("einstellungen", Tab.More)

    @Test
    fun abwesenheit() = main("abwesenheit", Tab.More, listOf(Route.Absences))

    @Test
    fun abwesenheitAntrag() = main("abwesenheit-antrag", Tab.More, listOf(Route.Absences, Route.AbsenceRequest()))

    @Test
    fun abwesenheitAenderung() = main("abwesenheit-aenderung", Tab.More) { session ->
        val approved = kotlinx.coroutines.runBlocking { session.api.myVacation() }.requests.first { it.canChange }
        listOf(Route.Absences, Route.AbsenceRequest(changing = approved))
    }

    @Test
    fun abwesenheitGenehmigung() = main("abwesenheit-genehmigung", Tab.More, listOf(Route.Absences, Route.AbsenceApprovals))

    @Test
    fun aufgaben() = main("aufgaben", Tab.More, listOf(Route.Tasks))

    @Test
    fun tankbelege() = main("tankbelege", Tab.More, listOf(Route.FuelReceipts))

    @Test
    fun tankbelegNeu() {
        de.estrichone.android.ui.FuelPreset.filled = true
        try {
            main("tankbeleg-neu", Tab.More, listOf(Route.FuelReceipts, Route.FuelReceiptNew))
        } finally {
            de.estrichone.android.ui.FuelPreset.filled = false
        }
    }

    @Test
    fun kunden() = main("kunden", Tab.Customers)

    // App-Updates (docs/app-updates.md)

    private val notes = listOf(
        AppReleaseNote("1.0.13", "EstrichONE 1.0.13", listOf(
            AppReleaseSection("Apps", listOf("Updates direkt in der App: laden, prüfen, installieren. Daten bleiben erhalten", "**Was gibt’s Neues:** nach jedem Update einmal die Neuerungen")),
            AppReleaseSection("Bauakten", listOf("Abnahme mit Unterschrift auch ohne Netz", "Fotos schneller hochladen")))),
        AppReleaseNote("1.0.12", "EstrichONE 1.0.12", listOf(AppReleaseSection("Module", listOf("Module lassen sich in der Verwaltung ein- und ausschalten")))),
    )

    private fun update(status: UpdateStatus, message: String? = null) = AppUpdateCheck(
        installedVersion = BuildConfig.VERSION_NAME, serverVersion = "1.0.13", latestVersion = "1.0.13", minimumVersion = if (status == UpdateStatus.Required) "1.0.13" else "1.0.0",
        status = status, message = message, download = AppDownload("package", "/api/v1/app-updates/android/package", "ab", 31_400_000, 10013, null, true),
        releaseNotes = notes,
    )

    @Test
    fun updateHinweis() = scene("update-hinweis", Tab.Today, { it.updater.show(UpdateState.Available(update(UpdateStatus.Recommended, "Bitte bis Freitag aktualisieren"))) }) { emptyList() }

    @Test
    fun updateInfo() = scene("update-info", Tab.More, { it.updater.show(UpdateState.Available(update(UpdateStatus.Recommended))) }) { listOf(Route.About) }

    @Test
    fun updateErforderlich() {
        val model = demo()
        model.updater.show(UpdateState.Available(update(UpdateStatus.Required, "Neue Abnahme-Funktion. Bitte heute noch aktualisieren.")))
        shot("update-erforderlich") { Root(model) }
    }

    @Test
    fun updateDownload() {
        val model = demo()
        model.updater.show(UpdateState.Downloading(update(UpdateStatus.Required), 12_600_000, 31_400_000))
        shot("update-download") { Root(model) }
    }

    @Test
    fun updateSignatur() {
        val model = demo()
        model.updater.show(UpdateState.SignatureMismatch(update(UpdateStatus.Required)))
        shot("update-signatur") { Root(model) }
    }

    @Test
    fun neuInDieserVersion() = shot("neu-in-dieser-version") {
        androidx.compose.foundation.layout.Box(androidx.compose.ui.Modifier.fillMaxSize().background(de.estrichone.android.ui.tones.canvas)) {
            WhatsNewContent(notes) {}
        }
    }
}

@Config(application = TestApp::class, sdk = [35], qualifiers = "w411dp-h891dp-port-notnight-xxhdpi")
class PhoneLightScreenshots : ScreenshotBase("telefon", Appearance.Light)

@Config(application = TestApp::class, sdk = [35], qualifiers = "w411dp-h891dp-port-night-xxhdpi")
class PhoneDarkScreenshots : ScreenshotBase("telefon", Appearance.Dark)

@Config(application = TestApp::class, sdk = [35], qualifiers = "w1280dp-h800dp-land-notnight-xhdpi")
class TabletLightScreenshots : ScreenshotBase("tablet", Appearance.Light)

@Config(application = TestApp::class, sdk = [35], qualifiers = "w1280dp-h800dp-land-night-xhdpi")
class TabletDarkScreenshots : ScreenshotBase("tablet", Appearance.Dark)

/**
 * Lange Telefonansicht (2400 dp hoch): Heute und Bauakte vollständig, damit Ablauf, Aufgaben, Dokumentation, Kontakte und
 * Verlauf auf einem Bild prüfbar sind. Ablage: telefon-lang-<bildschirm>-hell.png
 */
@RunWith(RobolectricTestRunner::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
@Config(application = TestApp::class, sdk = [35], qualifiers = "w411dp-h2400dp-port-notnight-xhdpi")
class PhoneTallScreenshots {
    @get:Rule
    val compose = createComposeRule()

    init {
        de.estrichone.android.ui.Live.polling = false
        de.estrichone.android.ui.Live.fixedTime = java.time.LocalTime.of(9, 41)
    }

    private fun scene(name: String, tab: Tab, routes: List<Route>) {
        val model = AppModel(ApplicationProvider.getApplicationContext())
        model.startDemo()
        val deadline = System.currentTimeMillis() + 60_000
        while (!(model.session.value?.service?.bauakten()?.isNotEmpty() == true && model.session.value?.notices?.value != null)) {
            ShadowLooper.idleMainLooper()
            Thread.sleep(50)
            check(System.currentTimeMillis() < deadline) { "Demomodus nicht bereit" }
        }
        val session = model.session.value!!
        compose.setContent {
            EstrichTheme(Appearance.Light) {
                val nav = remember { Nav(tab).also { it.set(tab, routes) } }
                MainScreen(model, session, nav)
            }
        }
        compose.waitForIdle()
        repeat(8) {
            Thread.sleep(200)
            ShadowLooper.idleMainLooper()
            compose.mainClock.advanceTimeBy(500)
            compose.waitForIdle()
        }
        compose.onRoot().captureRoboImage("build/outputs/roborazzi/telefon-lang-$name-hell.png")
    }

    @Test
    fun heute() = scene("heute", Tab.Today, emptyList())

    @Test
    fun bauakte() = scene("bauakte", Tab.Bauakten, listOf(Route.Bauakte(DemoData.EFH)))
}
