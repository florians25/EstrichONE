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
import androidx.compose.material.icons.filled.Apartment
import androidx.compose.material.icons.filled.Forum
import androidx.compose.material.icons.filled.MoreHoriz
import androidx.compose.material.icons.filled.People
import androidx.compose.material.icons.filled.WbSunny
import androidx.compose.material.icons.outlined.Apartment
import androidx.compose.material.icons.outlined.Forum
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

/**
 * Reiter: Heute, Baustellen, Chat, Kunden, Mehr. Erfassen gibt es nur noch in der Bauakte (Knopf „Erfassen“), damit immer
 * klar ist, zu welcher Baustelle Fotos, Tagebuch und Mängel gehören. Chat nur mit Chat-Recht und eingeschaltetem Modul.
 */
enum class Tab(val label: String, val selected: ImageVector, val icon: ImageVector) {
    Today("Heute", Icons.Filled.WbSunny, Icons.Outlined.WbSunny),
    Bauakten("Baustellen", Icons.Filled.Apartment, Icons.Outlined.Apartment),
    Chat("Chat", Icons.Filled.Forum, Icons.Outlined.Forum),
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
    data object Absences : Route
    /** Antrag stellen; mit [changing] Änderungsantrag zu diesem genehmigten Antrag. */
    data class AbsenceRequest(val changing: de.estrichone.core.AbsenceRequestItem? = null) : Route
    data object AbsenceApprovals : Route
    /** Tankbelege: eigene Belege und neuen Beleg erfassen. */
    data object FuelReceipts : Route
    data object FuelReceiptNew : Route
    /** Disposition bzw. „Wer fährt wohin“; [day] JJJJ-MM-TT (leer = heute), [scene] nur für Screenshots. */
    data class Dispatch(val day: String? = null, val scene: String? = null) : Route
    data object Helpdesk : Route
    data object ReportProblem : Route
    /** IT: Meldung im Namen eines Melders aufnehmen (Anruf, E-Mail, vor Ort). */
    data object RecordTicket : Route
    /** Anleitung aus der Wissensdatenbank. */
    data class Article(val id: String) : Route
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
    /** Erfassen-Blatt der Bauakte mit dieser ID (Knopf „Erfassen“ in der Bauakte). */
    var captureOpenFor by mutableStateOf<String?>(null)
    var captureTask by mutableStateOf<CaptureRequest?>(null)
    /** Blatt „Einplanen“ bzw. „Verschieben“ der Schnelldisposition (Bauakte, Baustellenliste). */
    var planRequest by mutableStateOf<QuickPlanRequest?>(null)
    /** Zählt übernommene Änderungen der Planung, damit „Nächster Termin“ neu lädt; [planDay] = zuletzt geplanter Tag. */
    var planChanged by mutableStateOf(0)
    var planDay: String? = null
    /** Kundenansicht (Fotos, Unterschrift) im Vollbild über der ganzen App. */
    var customerView by mutableStateOf<CustomerView?>(null)
    /** Entwurf, der nach einem Abstecher (Kamera) ohne Hinweis weitergeführt wird. */
    var resumedDraft: String? = null
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
        if (target == tab) root() else tab = target
    }

    /** Reiter „Chat“ öffnen, optional gleich eine Unterhaltung. */
    fun openChat(conversationId: String? = null) {
        tab = Tab.Chat
        stacks[Tab.Chat] = listOfNotNull(conversationId?.let { Route.Chat(it) })
    }

    /**
     * Erfassen aus „Heute“: Bauakte öffnen und dort erfassen (Foto: Kamera der Bauakte, sonst das Formular),
     * damit Zurück in der Bauakte landet.
     */
    fun captureIn(bauakteId: String, task: SiteTask, prefill: Map<String, String> = emptyMap()) {
        tab = Tab.Bauakten
        stacks[Tab.Bauakten] = listOfNotNull(Route.Bauakte(bauakteId), if (task == SiteTask.Photo) Route.Camera(bauakteId) else null)
        if (task != SiteTask.Photo) captureTask = CaptureRequest(task, bauakteId, prefill)
    }

    /** Ziel einer Benachrichtigung bzw. eines Links öffnen. */
    fun open(route: NotificationRoute) {
        when (route) {
            is NotificationRoute.Bauakte -> {
                tab = Tab.Bauakten
                val area = when (route.section) {
                    NotificationRoute.BauakteSection.Tasks -> AreaKind.Tasks
                    NotificationRoute.BauakteSection.Documents -> AreaKind.Documents
                    null -> null
                }
                stacks[tab] = listOfNotNull(Route.Bauakte(route.id), area?.let { Route.Area(route.id, it) })
            }
            is NotificationRoute.Absences -> { tab = Tab.More; stacks[tab] = listOfNotNull(Route.Absences, if (route.approvals) Route.AbsenceApprovals else null) }
            is NotificationRoute.Ticket -> { tab = Tab.More; stacks[tab] = listOf(Route.Helpdesk, Route.Ticket(route.id)) }
            is NotificationRoute.Planning -> { tab = Tab.More; stacks[tab] = listOf(Route.Dispatch(route.day)) }
            NotificationRoute.Tasks -> { tab = Tab.More; stacks[tab] = listOf(Route.Tasks) }
            is NotificationRoute.Chat -> openChat(route.conversationId)
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
    val chatAvailable = chat?.isAvailable == true
    val chatBadge = chat?.takeIf { it.isAvailable }?.unreadCount ?: 0
    val moreBadge = unreadTickets

    LaunchedEffect(Unit) { model.messages.collect { nav.snackbar.showSnackbar(it) } }
    // Angetippte Mitteilung: Ziel öffnen (Bauakte mit Bereich, Chat, Urlaub, Postfach …).
    val pending by model.pendingRoute.collectAsState()
    LaunchedEffect(pending) {
        pending?.let {
            nav.captureOpenFor = null
            nav.open(it)
            model.routeOpened()
        }
    }
    // Schnellzugriff des App-Symbols (Foto, Tagebuch, Mangel): in die Bauakte des laufenden bzw. nächsten Einsatzes von heute;
    // ohne Einsatz wird die Baustelle gewählt. „Synchronisation“ kommt aus der Mitteilung „Übertragung prüfen“.
    val action by model.pendingAction.collectAsState()
    LaunchedEffect(action) {
        val current = action ?: return@LaunchedEffect
        model.actionOpened()
        nav.captureOpenFor = null
        nav.customerView = null
        if (current == de.estrichone.android.MainActivity.ACTION_SYNC) {
            nav.set(Tab.Today, listOf(Route.Sync))
            return@LaunchedEffect
        }
        val task = when (current) {
            de.estrichone.core.QuickShortcut.DIARY -> SiteTask.Diary
            de.estrichone.core.QuickShortcut.DEFECT -> SiteTask.Defect
            else -> SiteTask.Photo
        }
        if (!task.isAvailable(session.modules.value)) return@LaunchedEffect
        val target = de.estrichone.core.QuickShortcut.target(session.cachedPlan(), java.time.LocalDate.now(de.estrichone.core.Dates.zone), Live.clock())
            ?.takeIf { session.service.bauakte(it) != null && session.service.canEdit(it) }
        if (target != null) nav.captureIn(target, task) else nav.captureTask = task.with(null)
    }
    BackHandler(enabled = nav.stack().isNotEmpty()) { nav.back() }

    CompositionLocalProvider(LocalNav provides nav, LocalSession provides session, LocalModel provides model) {
      Box(Modifier.fillMaxSize()) {
        NavigationSuiteScaffold(
            navigationSuiteItems = {
                Tab.entries.filter { it != Tab.Chat || chatAvailable || nav.tab == Tab.Chat }.forEach { tab ->
                    val badge = when (tab) {
                        Tab.Chat -> chatBadge
                        Tab.More -> moreBadge
                        else -> 0
                    }
                    item(
                        selected = nav.tab == tab,
                        onClick = { nav.select(tab) },
                        icon = {
                            val image = if (nav.tab == tab) tab.selected else tab.icon
                            if (badge > 0) {
                                BadgedBox(badge = { CountBadge(badge) }) { Icon(image, if (tab == Tab.Chat) "$badge ungelesen" else null) }
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
                    val updateBanner = model.hasUpdateBanner
                    // Update-Ankündigung (Countdown) ganz oben; sie übernimmt dann die Statusleiste.
                    val serverUpdate by session.update.collectAsState()
                    val announcing = serverUpdate?.phase == "announce"
                    UpdateAnnounceBar(session)
                    Box(if (announcing) Modifier.consumeWindowInsets(WindowInsets.statusBars) else Modifier) { HealthStrip(session) }
                    // Neuere App-Version: Hinweis mit „Aktualisieren“ (unter einem Ausfall-Hinweis ohne eigene Statusleiste).
                    if (banners.isEmpty() && !announcing) UpdateBanner(model) else Box(Modifier.consumeWindowInsets(WindowInsets.statusBars)) { UpdateBanner(model) }
                    // Unter einem Hinweis gehört die Statusleiste schon dem Hinweis.
                    Box(Modifier.weight(1f).then(if (banners.isNotEmpty() || updateBanner || announcing) Modifier.consumeWindowInsets(WindowInsets.statusBars) else Modifier)) { TabContent(nav) }
                }
                SnackbarHost(nav.snackbar, Modifier.align(androidx.compose.ui.Alignment.BottomCenter).padding(16.dp))
            }
        }
        // Kundenansicht: Vollbild über Inhalt und Navigation, damit beim Zeigen des Geräts nichts Internes zu sehen ist.
        nav.customerView?.let { view -> CustomerViewOverlay(view) { nav.customerView = null } }
        // Update-Ablauf: Wartungsbildschirm über allem (auch über die Navigation).
        UpdateMaintenanceOverlay(session)
      }
        nav.captureOpenFor?.let { id -> CaptureHub(bauakteId = id, onDismiss = { nav.captureOpenFor = null }) }
        nav.captureTask?.let { request -> CaptureFlow(request, onDone = { nav.captureTask = null }) }
        QuickPlanHost(nav.planRequest, onClose = { nav.planRequest = null }) { day ->
            nav.planDay = day
            nav.planChanged += 1
        }
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
        Tab.Today -> HomeScreen()
        Tab.Chat -> ChatListScreen(root = true)
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
        Route.Absences -> AbsencesScreen()
        is Route.AbsenceRequest -> AbsenceRequestScreen(route.changing)
        Route.AbsenceApprovals -> AbsenceApprovalsScreen()
        Route.FuelReceipts -> FuelReceiptsScreen()
        Route.FuelReceiptNew -> FuelReceiptNewScreen()
        is Route.Dispatch -> DispatchScreen(route.day, route.scene)
        Route.Helpdesk -> HelpdeskScreen()
        Route.ReportProblem -> ReportProblemScreen()
        Route.RecordTicket -> RecordTicketScreen()
        is Route.Article -> ArticleScreen(route.id)
        Route.Sync -> SyncScreen()
        Route.Security -> SecurityScreen()
        Route.NotificationSettings -> NotificationSettingsScreen()
        Route.Appearance -> AppearanceScreen()
        Route.About -> AboutScreen()
    }
}
