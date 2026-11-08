package de.estrichone.android.ui

import androidx.activity.compose.BackHandler
import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.layout.consumeWindowInsets
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.statusBars
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.AddCircle
import androidx.compose.material.icons.filled.Apartment
import androidx.compose.material.icons.filled.MoreHoriz
import androidx.compose.material.icons.filled.People
import androidx.compose.material.icons.filled.WbSunny
import androidx.compose.material.icons.outlined.AddCircleOutline
import androidx.compose.material.icons.outlined.Apartment
import androidx.compose.material.icons.outlined.MoreHoriz
import androidx.compose.material.icons.outlined.People
import androidx.compose.material.icons.outlined.WbSunny
import androidx.compose.material3.BadgedBox
import androidx.compose.material3.Icon
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Text
import androidx.compose.material3.VerticalDivider
import androidx.compose.material3.adaptive.navigationsuite.NavigationSuiteScaffold
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.unit.dp
import de.estrichone.android.AppModel
import de.estrichone.android.Session
import de.estrichone.core.NotificationRoute

/** Reiter wie auf iOS: Heute · Baustellen · Erfassen · Kunden · Mehr. */
enum class Tab(val label: String, val selected: ImageVector, val icon: ImageVector) {
    Today("Heute", Icons.Filled.WbSunny, Icons.Outlined.WbSunny),
    Bauakten("Baustellen", Icons.Filled.Apartment, Icons.Outlined.Apartment),
    Capture("Erfassen", Icons.Filled.AddCircle, Icons.Outlined.AddCircleOutline),
    Customers("Kunden", Icons.Filled.People, Icons.Outlined.People),
    More("Mehr", Icons.Filled.MoreHoriz, Icons.Outlined.MoreHoriz),
}

/** Ziele innerhalb der Reiter. */
sealed interface Route {
    data class Bauakte(val id: String) : Route
    data class Area(val bauakteId: String, val area: AreaKind) : Route
    data class Photo(val fileId: String) : Route
    data class Customer(val id: String) : Route
    data class Chat(val id: String) : Route
    data class Ticket(val id: String) : Route
    data class Inspection(val bauakteId: String, val id: String?) : Route
    data class Acceptance(val bauakteId: String, val id: String?) : Route
    data class Camera(val bauakteId: String, val scan: Boolean = false) : Route
    data object ChatList : Route
    data object Inbox : Route
    data class NotificationDetail(val item: de.estrichone.core.InboxNotification) : Route
    data object Tasks : Route
    data object Week : Route
    data object Dispatch : Route
    data object Helpdesk : Route
    data object ReportProblem : Route
    data object Sync : Route
    data object Security : Route
    data object NotificationSettings : Route
    data object Appearance : Route
    data object About : Route
}

/** Navigation der Hauptansicht: je Reiter ein eigener Stapel. */
class Nav(initialTab: Tab) {
    var tab by mutableStateOf(initialTab)
    private val stacks = mutableStateMapOf<Tab, List<Route>>()
    var captureOpen by mutableStateOf(false)
    /** „Mehr“ in der Bauakte: Erfassen für diese Baustelle. */
    var captureOpenFor by mutableStateOf<String?>(null)
    var captureTask by mutableStateOf<CaptureRequest?>(null)
    var snackbar = SnackbarHostState()

    fun stack(tab: Tab = this.tab): List<Route> = stacks[tab] ?: emptyList()

    fun push(route: Route) {
        stacks[tab] = stack() + route
    }

    /** Neues Ziel statt des obersten (z. B. nach dem Anlegen). */
    fun replace(route: Route) {
        stacks[tab] = stack().dropLast(1) + route
    }

    fun back(): Boolean {
        val current = stack()
        if (current.isEmpty()) return false
        stacks[tab] = current.dropLast(1)
        return true
    }

    fun root() {
        stacks[tab] = emptyList()
    }

    fun select(target: Tab) {
        if (target == Tab.Capture) {
            captureOpen = true
            return
        }
        if (target == tab) root() else tab = target
    }

    /** Ziel einer Benachrichtigung bzw. eines Links öffnen. */
    fun open(route: NotificationRoute) {
        when (route) {
            is NotificationRoute.Bauakte -> { tab = Tab.Bauakten; stacks[tab] = listOf(Route.Bauakte(route.id)) }
            is NotificationRoute.Ticket -> { tab = Tab.More; stacks[tab] = listOf(Route.Helpdesk, Route.Ticket(route.id)) }
            is NotificationRoute.Planning -> { tab = Tab.More; stacks[tab] = listOf(Route.Dispatch) }
            NotificationRoute.Tasks -> { tab = Tab.More; stacks[tab] = listOf(Route.Tasks) }
            is NotificationRoute.Chat -> { tab = Tab.Today; stacks[tab] = listOfNotNull(Route.ChatList, route.conversationId?.let { Route.Chat(it) }) }
            NotificationRoute.Inbox -> { tab = Tab.Today; stacks[tab] = listOf(Route.Inbox) }
        }
    }

    /** Startzustand setzen (Screenshots, Tests). */
    fun set(tab: Tab, routes: List<Route>) {
        this.tab = tab
        stacks[tab] = routes
    }
}

val LocalNav = staticCompositionLocalOf<Nav> { error("Keine Navigation") }
val LocalSession = staticCompositionLocalOf<Session> { error("Keine Sitzung") }
val LocalModel = staticCompositionLocalOf<AppModel> { error("Kein Modell") }

/** Breite ab der Liste und Detail nebeneinander stehen (Tablet, quer). */
val TwoPaneWidth = 840.dp

@Composable
fun MainScreen(model: AppModel, session: Session, nav: Nav = remember { Nav(Tab.Today) }) {
    val chat by session.chat.collectAsState()
    val unreadTickets by session.unreadTickets.collectAsState()
    val moreBadge = unreadTickets + (chat?.takeIf { it.isAvailable }?.unreadCount ?: 0)

    LaunchedEffect(Unit) { model.messages.collect { nav.snackbar.showSnackbar(it) } }
    BackHandler(enabled = nav.stack().isNotEmpty()) { nav.back() }

    CompositionLocalProvider(LocalNav provides nav, LocalSession provides session, LocalModel provides model) {
        NavigationSuiteScaffold(
            navigationSuiteItems = {
                Tab.entries.forEach { tab ->
                    item(
                        selected = nav.tab == tab && tab != Tab.Capture,
                        onClick = { nav.select(tab) },
                        icon = {
                            val image = if (nav.tab == tab) tab.selected else tab.icon
                            if (tab == Tab.More && moreBadge > 0) {
                                BadgedBox(badge = { CountBadge(moreBadge) }) { Icon(image, null) }
                            } else {
                                Icon(image, null)
                            }
                        },
                        label = { Text(tab.label) },
                    )
                }
            },
        ) {
            Box(Modifier.fillMaxSize().background(tones.canvas)) {
                Column(Modifier.fillMaxSize()) {
                    val banners by session.health.collectAsState()
                    HealthStrip(session)
                    // Unter einem Ausfall-Hinweis gehört die Statusleiste schon dem Hinweis.
                    Box(Modifier.weight(1f).then(if (banners.isNotEmpty()) Modifier.consumeWindowInsets(WindowInsets.statusBars) else Modifier)) { TabContent(nav) }
                }
                SnackbarHost(nav.snackbar, Modifier.align(androidx.compose.ui.Alignment.BottomCenter).padding(16.dp))
            }
        }
        if (nav.captureOpen || nav.captureOpenFor != null) {
            CaptureHub(bauakteId = nav.captureOpenFor, onDismiss = { nav.captureOpen = false; nav.captureOpenFor = null })
        }
        nav.captureTask?.let { request -> CaptureFlow(request, onDone = { nav.captureTask = null }) }
    }
}

@Composable
private fun TabContent(nav: Nav) {
    val stack = nav.stack()
    BoxWithConstraints(Modifier.fillMaxSize()) {
        val twoPane = maxWidth >= TwoPaneWidth && nav.tab in setOf(Tab.Bauakten, Tab.Customers)
        if (twoPane) {
            // Tablet: Liste links, Auswahl rechts (Liste + Detail wie auf dem iPad).
            Row(Modifier.fillMaxSize()) {
                Box(Modifier.width(400.dp).fillMaxHeight()) { RootScreen(nav.tab, selected = (stack.firstOrNull() as? Route.Bauakte)?.id ?: (stack.firstOrNull() as? Route.Customer)?.id) }
                VerticalDivider(color = tones.separator)
                Box(Modifier.weight(1f).fillMaxHeight()) {
                    val top = stack.lastOrNull()
                    if (top == null) {
                        EmptyState(nav.tab.selected, if (nav.tab == Tab.Bauakten) "Baustelle wählen" else "Kunden wählen",
                            "Links auswählen, um die Details hier zu sehen.", Modifier.windowInsetsPadding(WindowInsets.statusBars).padding(top = 120.dp))
                    } else {
                        RouteScreen(top, twoPane = true)
                    }
                }
            }
        } else {
            AnimatedContent(stack.lastOrNull() to nav.tab, transitionSpec = { fadeIn() togetherWith fadeOut() }, label = "route") { (top, tab) ->
                if (top == null) RootScreen(tab, null) else RouteScreen(top, twoPane = false)
            }
        }
    }
}

@Composable
private fun RootScreen(tab: Tab, selected: String?) {
    when (tab) {
        Tab.Today, Tab.Capture -> HomeScreen()
        Tab.Bauakten -> BauaktenScreen(selected)
        Tab.Customers -> CustomersScreen(selected)
        Tab.More -> MoreScreen()
    }
}

@Composable
private fun RouteScreen(route: Route, twoPane: Boolean) {
    when (route) {
        is Route.Bauakte -> BauakteScreen(route.id, showBack = !twoPane || LocalNav.current.stack().size > 1)
        is Route.Area -> AreaScreen(route.bauakteId, route.area)
        is Route.Photo -> PhotoScreen(route.fileId)
        is Route.Customer -> CustomerScreen(route.id, showBack = !twoPane)
        is Route.Chat -> ConversationScreen(route.id)
        is Route.Ticket -> TicketScreen(route.id)
        is Route.Inspection -> InspectionScreen(route.bauakteId, route.id)
        is Route.Acceptance -> AcceptanceScreen(route.bauakteId, route.id)
        is Route.Camera -> CameraScreen(route.bauakteId, route.scan)
        Route.ChatList -> ChatListScreen()
        Route.Inbox -> InboxScreen()
        is Route.NotificationDetail -> NotificationDetailScreen(route.item)
        Route.Tasks -> MyTasksScreen()
        Route.Week -> WeekScreen()
        Route.Dispatch -> DispatchScreen()
        Route.Helpdesk -> HelpdeskScreen()
        Route.ReportProblem -> ReportProblemScreen()
        Route.Sync -> SyncScreen()
        Route.Security -> SecurityScreen()
        Route.NotificationSettings -> NotificationSettingsScreen()
        Route.Appearance -> AppearanceScreen()
        Route.About -> AboutScreen()
    }
}
