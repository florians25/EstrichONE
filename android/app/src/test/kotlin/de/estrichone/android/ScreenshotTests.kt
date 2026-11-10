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
import de.estrichone.core.DemoChat
import de.estrichone.core.ApiDate
import de.estrichone.core.DemoData
import de.estrichone.core.InboxNotification
import de.estrichone.core.NotificationFilter
import de.estrichone.core.NotificationGrouping
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

    private fun main(name: String, tab: Tab, routes: List<Route> = emptyList()) {
        val model = demo()
        val session = model.session.value!!
        shot(name) {
            val nav = remember { Nav(tab).also { it.set(tab, routes) } }
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
        listOf(Route.Bauakte(DemoData.PRAXIS), Route.Area(DemoData.PRAXIS, AreaKind.Protocols)))

    @Test
    fun raeume() = main("raeume", Tab.Bauakten, listOf(Route.Bauakte(DemoData.EFH), Route.Area(DemoData.EFH, AreaKind.Rooms)))

    @Test
    fun chat() = main("chat", Tab.Today, listOf(Route.ChatList, Route.Chat(DemoChat.SITE)))

    @Test
    fun chatliste() = main("chatliste", Tab.Today, listOf(Route.ChatList))

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
    fun einstellungen() = main("einstellungen", Tab.More)

    @Test
    fun kunden() = main("kunden", Tab.Customers)
}

@Config(application = TestApp::class, sdk = [35], qualifiers = "w411dp-h891dp-port-notnight-xxhdpi")
class PhoneLightScreenshots : ScreenshotBase("telefon", Appearance.Light)

@Config(application = TestApp::class, sdk = [35], qualifiers = "w411dp-h891dp-port-night-xxhdpi")
class PhoneDarkScreenshots : ScreenshotBase("telefon", Appearance.Dark)

@Config(application = TestApp::class, sdk = [35], qualifiers = "w1280dp-h800dp-land-notnight-xhdpi")
class TabletLightScreenshots : ScreenshotBase("tablet", Appearance.Light)

@Config(application = TestApp::class, sdk = [35], qualifiers = "w1280dp-h800dp-land-night-xhdpi")
class TabletDarkScreenshots : ScreenshotBase("tablet", Appearance.Dark)
