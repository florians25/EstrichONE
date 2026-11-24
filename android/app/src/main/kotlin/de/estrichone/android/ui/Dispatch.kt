package de.estrichone.android.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowForward
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Autorenew
import androidx.compose.material.icons.filled.CalendarMonth
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.CloudOff
import androidx.compose.material.icons.filled.Directions
import androidx.compose.material.icons.filled.DragHandle
import androidx.compose.material.icons.filled.Edit
import androidx.compose.material.icons.filled.Event
import androidx.compose.material.icons.filled.EventAvailable
import androidx.compose.material.icons.filled.ExpandLess
import androidx.compose.material.icons.filled.ExpandMore
import androidx.compose.material.icons.filled.Groups
import androidx.compose.material.icons.filled.LocalShipping
import androidx.compose.material.icons.filled.MoreVert
import androidx.compose.material.icons.filled.CloudUpload
import androidx.compose.material.icons.filled.People
import androidx.compose.material.icons.filled.EventBusy
import androidx.compose.material.icons.filled.Phone
import androidx.compose.material.icons.filled.Reorder
import androidx.compose.material.icons.filled.Schedule
import androidx.compose.material.icons.filled.Search
import androidx.compose.material.icons.filled.SwapHoriz
import androidx.compose.material.icons.filled.Tune
import androidx.compose.material.icons.filled.Verified
import androidx.compose.material.icons.filled.Warning
import androidx.compose.material.icons.outlined.ChatBubbleOutline
import androidx.compose.material.icons.outlined.Delete
import androidx.compose.material.icons.outlined.DirectionsCar
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.DatePicker
import androidx.compose.material3.DatePickerDialog
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilledTonalButton
import androidx.compose.material3.FilterChip
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.RadioButton
import androidx.compose.material3.SelectableDates
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TimeInput
import androidx.compose.material3.pulltorefresh.PullToRefreshBox
import androidx.compose.material3.rememberDatePickerState
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.material3.rememberTimePickerState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.key
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.semantics.CustomAccessibilityAction
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.customActions
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.zIndex
import de.estrichone.android.support.Links
import de.estrichone.core.ApiDate
import de.estrichone.core.ApiException
import de.estrichone.core.BacklogEntry
import de.estrichone.core.Dates
import de.estrichone.core.DayPlan
import de.estrichone.core.DayPlanCrew
import de.estrichone.core.DayPlanStop
import de.estrichone.core.DispatchDay
import de.estrichone.core.DispatchDesk
import de.estrichone.core.DispatchText
import de.estrichone.core.Ids
import de.estrichone.core.PlanChange
import de.estrichone.core.PlanChoice
import de.estrichone.core.PlanChoices
import de.estrichone.core.PlanMove
import de.estrichone.core.PlanOrder
import de.estrichone.core.PlanOutcome
import de.estrichone.core.PlanTime
import de.estrichone.core.PlanningOutbox
import de.estrichone.core.PlanningResource
import de.estrichone.core.RosterSwap
import de.estrichone.core.planningBacklog
import de.estrichone.core.planningResources
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.time.DayOfWeek
import java.time.Instant
import java.time.LocalDate
import java.time.LocalTime
import java.time.ZoneOffset

/** Offene Blätter der Schnelldisposition (wie PlanSheet auf iOS). */
private sealed interface DispatchSheet {
    data class Assign(val crewId: String) : DispatchSheet
    data class Move(val stopId: String, val crewId: String?, val timeOnly: Boolean = false) : DispatchSheet
    data class Swap(val crewId: String, val vehicle: Boolean) : DispatchSheet
    data class SwapCrews(val crewId: String) : DispatchSheet
    data class Reorder(val crewId: String) : DispatchSheet
    data object Release : DispatchSheet
    data object Pending : DispatchSheet
}

private enum class StopAction { Move, Time, NextFreeDay, Remove }

private enum class CrewAction { Assign, Reorder, SwapPerson, SwapVehicle, SwapCrews }

private sealed interface ReleaseAction {
    data class Release(val until: LocalDate, val note: String) : ReleaseAction
    data object Withdraw : ReleaseAction
}

/**
 * Disposition bzw. „Wer fährt wohin“ (wie DispatchView auf iOS, `GET /planning/day/{day}`): Tagesleiste mit Kalender,
 * Freigabestand, je Kolonne Besetzung, Fahrzeuge und Baustellen in Fahrtreihenfolge mit Route und Anruf.
 *
 * Mit dem Recht zur Planung (`canPlan` vom Server, Recht `planning.manage`) zusätzlich die Schnelldisposition: je Einsatz
 * ein Menü (verschieben, Uhrzeit, nächster freier Tag, aus dem Plan nehmen), je Kolonne „Anpassen“ (Baustelle zuweisen,
 * Reihenfolge, Mitarbeiter, Fahrzeug, Kolonnen tauschen) und die Freigabe. Ohne Verbindung werden Änderungen vorgemerkt.
 * [scene]: Screenshots öffnen damit ein Blatt bzw. das Menü des ersten Einsatzes.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun DispatchScreen(initialDay: String? = null, scene: String? = null) {
    val session = LocalSession.current
    val model = LocalModel.current
    val nav = LocalNav.current
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val desk = remember(session) { DispatchDesk(session.api, session.store.backend) }
    val today = remember { LocalDate.now(Dates.zone) }
    var day by remember { mutableStateOf(initialDay?.let { ApiDate.day(it) } ?: today) }
    var state by remember { mutableStateOf<DispatchDay?>(null) }
    var loading by remember { mutableStateOf(true) }
    var refreshing by remember { mutableStateOf(false) }
    var pending by remember { mutableStateOf(emptyList<PlanningOutbox.Entry>()) }
    var notice by remember { mutableStateOf(emptyList<String>()) }
    var sheet by remember { mutableStateOf<DispatchSheet?>(null) }
    var removing by remember { mutableStateOf<DayPlanStop?>(null) }
    var menuFor by remember { mutableStateOf<String?>(null) }
    var choosingDate by remember { mutableStateOf(false) }
    var reload by remember { mutableIntStateOf(0) }
    var sceneShown by remember { mutableStateOf(false) }
    val plan = state?.plan?.takeIf { it.day == day.toString() }
    val canPlan = plan?.canPlan == true

    suspend fun load() {
        loading = true
        val requested = day
        if (state?.plan?.day != requested.toString()) state = withContext(Dispatchers.IO) { desk.cached(requested.toString()) }
        val flushed = withContext(Dispatchers.IO) { desk.flush() }
        if (flushed.sent > 0) model.message(DispatchText.sent(flushed.sent))
        notice = flushed.warnings
        pending = withContext(Dispatchers.IO) { desk.outbox.entries() }
        val fresh = withContext(Dispatchers.IO) { desk.load(requested.toString()) }
        if (requested == day) state = fresh
        loading = false
    }

    LaunchedEffect(day, reload) { load() }

    fun perform(change: PlanChange) {
        removing = null
        sheet = null
        menuFor = null
        scope.launch {
            when (val outcome = withContext(Dispatchers.IO) { desk.perform(change) }) {
                is PlanOutcome.Applied -> {
                    outcome.plan?.takeIf { it.day == day.toString() }?.let { state = DispatchDay(it, Instant.now(), isCurrent = true) }
                    model.message(outcome.message)
                    load()
                    notice = outcome.warnings
                }
                PlanOutcome.Queued -> {
                    pending = withContext(Dispatchers.IO) { desk.outbox.entries() }
                    model.message(DispatchDesk.QUEUED)
                }
                is PlanOutcome.Failed -> model.message(outcome.message)
            }
        }
    }

    fun release(action: ReleaseAction) {
        sheet = null
        scope.launch {
            val outcome = withContext(Dispatchers.IO) {
                when (action) {
                    is ReleaseAction.Release -> desk.release(day, action.until, action.note)
                    ReleaseAction.Withdraw -> desk.withdraw(day)
                }
            }
            when (outcome) {
                is PlanOutcome.Applied -> { model.message(outcome.message); load() }
                is PlanOutcome.Failed -> model.message(outcome.message)
                PlanOutcome.Queued -> Unit
            }
        }
    }

    // Screenshots: Blatt bzw. Menü des ersten Einsatzes öffnen, sobald der Plan da ist.
    LaunchedEffect(plan) {
        val current = plan ?: return@LaunchedEffect
        if (scene == null || sceneShown || !current.canPlan) return@LaunchedEffect
        sceneShown = true
        val crew = current.sortedCrews.firstOrNull() ?: return@LaunchedEffect
        val stop = crew.stops.firstOrNull()
        delay(300)
        when (scene) {
            "zuweisen" -> sheet = DispatchSheet.Assign(crew.id)
            "menue" -> menuFor = stop?.assignmentId
            "verschieben" -> stop?.let { sheet = DispatchSheet.Move(it.assignmentId, crew.id) }
            "tauschen" -> sheet = DispatchSheet.Swap(crew.id, vehicle = false)
            "kolonnen" -> sheet = DispatchSheet.SwapCrews(crew.id)
            "reihenfolge" -> sheet = DispatchSheet.Reorder(crew.id)
            "freigabe" -> sheet = DispatchSheet.Release
            else -> Unit
        }
    }

    ScreenScaffold(if (canPlan) "Disposition" else "Wer fährt wohin", subtitle = Dates.long(day)) { padding ->
        PullToRefreshBox(isRefreshing = refreshing, onRefresh = {
            scope.launch {
                refreshing = true
                load()
                refreshing = false
            }
        }, modifier = Modifier.fillMaxSize()) {
            LazyColumn(contentPadding = padding.content(), verticalArrangement = Arrangement.spacedBy(Space.m), modifier = Modifier.readable()) {
                item(key = "days") { DayStrip(day, today, onSelect = { day = it }, onCalendar = { choosingDate = true }) }
                val current = state
                if (current != null && !current.isCurrent && current.loadedAt != null && plan != null) {
                    item(key = "offline") {
                        Banner("Keine Verbindung, Stand ${DispatchText.stamp(current.loadedAt, today)}", Icons.Filled.CloudOff, tones.textSecondary, tones.fill)
                    }
                }
                if (pending.isNotEmpty()) {
                    item(key = "pending") {
                        val failed = pending.any { it.error != null }
                        Banner(if (failed) "Vorgemerkte Änderung abgelehnt" else DispatchText.pending(pending.size), Icons.Filled.CloudUpload,
                            if (failed) tones.danger else tones.warning, if (failed) tones.dangerSoft else tones.warningSoft,
                            action = "Anzeigen", onAction = { sheet = DispatchSheet.Pending })
                    }
                }
                plan?.holiday?.let { holiday -> item(key = "holiday") { Banner("Feiertag: $holiday", Icons.Filled.Event, tones.info, tones.infoSoft) } }
                if (plan != null && (plan.stopCount > 0 || plan.release != null)) {
                    item(key = "release") { ReleaseBar(plan, today, onRelease = { sheet = DispatchSheet.Release }) }
                }
                notice.forEachIndexed { index, text ->
                    item(key = "notice-$index") { Banner(text, Icons.Filled.Warning, tones.warning, tones.warningSoft) }
                }
                when {
                    plan != null -> {
                        plan.sortedCrews.forEach { crew ->
                            item(key = "crew-${crew.id}") {
                                CrewCard(crew, plan, menuFor,
                                    onMenu = { id -> menuFor = id },
                                    onOpen = { stop -> nav.push(Route.Bauakte(stop.bauakteId)) },
                                    onRoute = { stop -> Links.route(context, stop.address, stop.latitude, stop.longitude) },
                                    onCall = { phone -> Links.call(context, phone) },
                                    onStop = { stop, action ->
                                        menuFor = null
                                        when (action) {
                                            StopAction.Move -> sheet = DispatchSheet.Move(stop.assignmentId, crew.id)
                                            StopAction.Time -> sheet = DispatchSheet.Move(stop.assignmentId, crew.id, timeOnly = true)
                                            StopAction.NextFreeDay -> perform(PlanChange.NextFreeDay(stop.assignmentId, stop.title))
                                            StopAction.Remove -> removing = stop
                                        }
                                    },
                                    onCrew = { action ->
                                        sheet = when (action) {
                                            CrewAction.Assign -> DispatchSheet.Assign(crew.id)
                                            CrewAction.Reorder -> DispatchSheet.Reorder(crew.id)
                                            CrewAction.SwapPerson -> DispatchSheet.Swap(crew.id, vehicle = false)
                                            CrewAction.SwapVehicle -> DispatchSheet.Swap(crew.id, vehicle = true)
                                            CrewAction.SwapCrews -> DispatchSheet.SwapCrews(crew.id)
                                        }
                                    })
                            }
                        }
                        if (plan.withoutCrew.isNotEmpty()) {
                            item(key = "without-crew") {
                                SectionCard {
                                    Text("Ohne Kolonne", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold,
                                        modifier = Modifier.padding(16.dp))
                                    plan.withoutCrew.forEach { stop ->
                                        HorizontalDivider(color = tones.separator)
                                        StopRow(stop, MaterialTheme.colorScheme.primary, canPlan, hasCrew = false, menuOpen = menuFor == stop.assignmentId,
                                            onMenu = { open -> menuFor = if (open) stop.assignmentId else null },
                                            onOpen = { nav.push(Route.Bauakte(stop.bauakteId)) },
                                            onRoute = { Links.route(context, stop.address, stop.latitude, stop.longitude) },
                                            onCall = { Links.call(context, it) },
                                            onAction = { action ->
                                                menuFor = null
                                                when (action) {
                                                    StopAction.Move -> sheet = DispatchSheet.Move(stop.assignmentId, null)
                                                    StopAction.Time -> sheet = DispatchSheet.Move(stop.assignmentId, null, timeOnly = true)
                                                    StopAction.NextFreeDay -> Unit
                                                    StopAction.Remove -> removing = stop
                                                }
                                            })
                                    }
                                }
                            }
                        }
                        if (plan.free.isNotEmpty() || plan.absent.isNotEmpty()) item(key = "people") { PeopleCard(plan) }
                    }
                    loading -> item(key = "loading") { SkeletonRows(4) }
                    else -> item(key = "empty") {
                        EmptyState(Icons.Outlined.DirectionsCar, "Keine Planung", current?.message ?: "Für diesen Tag liegt keine Planung vor.")
                    }
                }
            }
        }
    }

    if (choosingDate) PlanDatePicker(day, null, onPick = { day = it }, onDismiss = { choosingDate = false })
    removing?.let { stop ->
        ConfirmDialog("Aus dem Plan nehmen?",
            "${stop.activity.ifBlank { "Der Einsatz" }} bei ${stop.title} am ${Dates.long(day)} wird aus dem Plan genommen. Die Bauakte bleibt unverändert.",
            "Entfernen", { perform(PlanChange.Remove(stop.assignmentId, stop.title)) }, { removing = null })
    }
    val open = sheet
    if (plan != null && open != null) {
        val close = { sheet = null }
        when (open) {
            is DispatchSheet.Assign -> plan.crew(open.crewId)?.let { crew ->
                AssignSheet(desk, crew, day) { change -> if (change == null) close() else perform(change) }
            }
            is DispatchSheet.Move -> (plan.crews.flatMap { it.stops } + plan.withoutCrew).firstOrNull { it.assignmentId == open.stopId }?.let { stop ->
                MoveSheet(desk, plan, stop, open.crewId, day, open.timeOnly) { change -> if (change == null) close() else perform(change) }
            }
            is DispatchSheet.Swap -> plan.crew(open.crewId)?.let { crew ->
                SwapRosterSheet(desk, plan, crew, day, open.vehicle) { change -> if (change == null) close() else perform(change) }
            }
            is DispatchSheet.SwapCrews -> plan.crew(open.crewId)?.let { crew ->
                SwapCrewsSheet(plan, crew, day) { change -> if (change == null) close() else perform(change) }
            }
            is DispatchSheet.Reorder -> plan.crew(open.crewId)?.let { crew ->
                ReorderSheet(crew, day) { change -> if (change == null) close() else perform(change) }
            }
            DispatchSheet.Release -> ReleaseSheet(plan, day, today) { action -> if (action == null) close() else release(action) }
            DispatchSheet.Pending -> PendingSheet(pending, today, onDiscard = { id ->
                scope.launch {
                    pending = withContext(Dispatchers.IO) { desk.outbox.discard(id); desk.outbox.entries() }
                }
            }, onDismiss = close)
        }
    }
}

// Bausteine der Tagesansicht

/** Tagesleiste: gestern, heute und die nächsten 13 Tage; rechts der Kalender für jeden anderen Tag. */
@Composable
private fun DayStrip(day: LocalDate, today: LocalDate, onSelect: (LocalDate) -> Unit, onCalendar: () -> Unit) {
    val days = remember(today) { (-1L..13L).map { today.plusDays(it) } }
    val list = rememberLazyListState(initialFirstVisibleItemIndex = (days.indexOf(day) - 1).coerceAtLeast(0))
    LaunchedEffect(day) { days.indexOf(day).takeIf { it >= 0 }?.let { list.animateScrollToItem((it - 1).coerceAtLeast(0)) } }
    Row(verticalAlignment = Alignment.CenterVertically) {
        LazyRow(state = list, modifier = Modifier.weight(1f), horizontalArrangement = Arrangement.spacedBy(Space.s)) {
            items(days, key = { it.toString() }) { date ->
                val selected = date == day
                val weekend = date.dayOfWeek == DayOfWeek.SATURDAY || date.dayOfWeek == DayOfWeek.SUNDAY
                val color = when {
                    selected -> MaterialTheme.colorScheme.onPrimary
                    weekend -> tones.textTertiary
                    else -> MaterialTheme.colorScheme.onSurface
                }
                Column(
                    Modifier.width(56.dp).height(58.dp).clip(RoundedCornerShape(12.dp))
                        .background(if (selected) MaterialTheme.colorScheme.primary else tones.card)
                        .clickable(role = Role.Tab) { onSelect(date) }
                        .semantics { contentDescription = Dates.long(date, today); this.selected = selected },
                    horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.Center,
                ) {
                    val label = when (date) {
                        today -> "Heute"
                        today.plusDays(1) -> "Morgen"
                        else -> Dates.weekdayShort(date)
                    }
                    Text(label, style = MaterialTheme.typography.labelMedium, color = color, maxLines = 1)
                    Text(date.dayOfMonth.toString(), style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold, color = color)
                }
            }
        }
        IconButton(onClick = onCalendar) { Icon(Icons.Filled.CalendarMonth, "Tag wählen") }
    }
}

/** Freigabestand als eine ruhige Zeile: Symbol, Stand, kleine Schaltfläche; entfallene Einsätze zum Aufklappen. */
@Composable
private fun ReleaseBar(plan: DayPlan, today: LocalDate, onRelease: () -> Unit) {
    val (title, detail) = DispatchText.releaseTitle(plan, today)
    val tone = DispatchText.tone(plan)
    val (icon, tint) = when (tone) {
        DispatchText.Tone.Success -> Icons.Filled.Verified to tones.success
        DispatchText.Tone.Warning -> Icons.Filled.Autorenew to tones.warning
        DispatchText.Tone.Neutral -> Icons.Filled.Edit to tones.textSecondary
    }
    var showsRemoved by remember { mutableStateOf(false) }
    val removed = plan.release?.removed.orEmpty()
    SectionCard(padding = PaddingValues(horizontal = 14.dp, vertical = 10.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Icon(icon, null, tint = tint, modifier = Modifier.size(22.dp))
            Spacer(Modifier.width(12.dp))
            Column(Modifier.weight(1f)) {
                Text(title, style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.SemiBold)
                Text(detail, style = MaterialTheme.typography.bodySmall, color = if (tone == DispatchText.Tone.Warning) tones.warning else tones.textSecondary)
            }
            if (plan.canPlan) {
                FilledTonalButton(onClick = onRelease, contentPadding = PaddingValues(horizontal = 14.dp, vertical = 6.dp)) {
                    Text(if (plan.release == null) "Freigeben" else "Freigabe")
                }
            }
        }
        if (removed.isNotEmpty()) {
            Row(Modifier.fillMaxWidth().heightIn(min = 40.dp).clip(RoundedCornerShape(8.dp)).clickable(role = Role.Button) { showsRemoved = !showsRemoved }
                .padding(start = 34.dp), verticalAlignment = Alignment.CenterVertically) {
                Text(if (removed.size == 1) "1 Einsatz entfallen" else "${removed.size} Einsätze entfallen", style = MaterialTheme.typography.bodyMedium,
                    color = tones.textSecondary, modifier = Modifier.weight(1f))
                Icon(if (showsRemoved) Icons.Filled.ExpandLess else Icons.Filled.ExpandMore, if (showsRemoved) "Zuklappen" else "Aufklappen", tint = tones.textSecondary)
            }
            if (showsRemoved) {
                removed.forEach { Secondary("${it.title}, ${it.activity}", Modifier.padding(start = 34.dp, bottom = 4.dp)) }
            }
        }
    }
}

/** Kolonne als Karte: Kopf mit Farbe, Name, „Meine“, „Anpassen“; Besetzung, Fahrzeuge, Konflikte; Baustellen in Fahrtreihenfolge. */
@Composable
private fun CrewCard(
    crew: DayPlanCrew, plan: DayPlan, menuFor: String?, onMenu: (String?) -> Unit, onOpen: (DayPlanStop) -> Unit, onRoute: (DayPlanStop) -> Unit,
    onCall: (String) -> Unit, onStop: (DayPlanStop, StopAction) -> Unit, onCrew: (CrewAction) -> Unit,
) {
    val tint = parseColor(crew.color) ?: MaterialTheme.colorScheme.primary
    SectionCard {
        Column(Modifier.fillMaxWidth().background(if (crew.isMine) tones.infoSoft else Color.Transparent).padding(start = 16.dp, end = 8.dp, top = 10.dp, bottom = 12.dp)) {
            Row(Modifier.heightIn(min = 40.dp), verticalAlignment = Alignment.CenterVertically) {
                // Name und „Meine“ nehmen den ganzen Platz links; „Anpassen“ steht rechts.
                Row(Modifier.weight(1f), verticalAlignment = Alignment.CenterVertically) {
                    Box(Modifier.width(4.dp).height(20.dp).clip(RoundedCornerShape(2.dp)).background(tint))
                    Spacer(Modifier.width(10.dp))
                    Text(crew.name, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold, maxLines = 1,
                        overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f, fill = false))
                    if (crew.isMine) {
                        Spacer(Modifier.width(8.dp))
                        Pill("Meine", MaterialTheme.colorScheme.primary, tones.card, modifier = Modifier.semantics { contentDescription = "Meine Kolonne" })
                    }
                }
                if (plan.canPlan) AdjustMenu(crew, canReorder = crew.stops.size > 1, canSwapCrews = plan.crews.size > 1, onCrew)
            }
            if (crew.memberText.isNotEmpty()) RosterLine(crew.memberText, Icons.Filled.Groups)
            if (crew.vehicles.isNotEmpty()) RosterLine(crew.vehicles.joinToString(", ") { it.name }, Icons.Filled.LocalShipping)
            crew.absent.forEach { RosterLine("${it.name}, ${it.absenceNote ?: "abwesend"}", Icons.Filled.EventBusy, tones.warning) }
            if (crew.rosterChanged) RosterLine("Besetzung geändert", Icons.Filled.SwapHoriz, MaterialTheme.colorScheme.primary)
            crew.rosterNote?.let { RosterLine(it, Icons.Outlined.ChatBubbleOutline) }
            crew.conflicts.forEach { RosterLine(it, Icons.Filled.Warning, tones.warning) }
        }
        HorizontalDivider(color = tones.separator)
        if (crew.stops.isEmpty()) {
            Secondary(if (crew.available.isEmpty()) "Nicht besetzt" else "Noch keine Baustelle", Modifier.padding(16.dp))
        }
        crew.stops.forEachIndexed { index, stop ->
            if (index > 0) RowDivider(56.dp)
            StopRow(stop, tint, plan.canPlan, hasCrew = true, menuOpen = menuFor == stop.assignmentId,
                onMenu = { open -> onMenu(if (open) stop.assignmentId else null) }, onOpen = { onOpen(stop) }, onRoute = { onRoute(stop) },
                onCall = onCall, onAction = { onStop(stop, it) })
        }
        if (plan.canPlan) {
            HorizontalDivider(color = tones.separator)
            Row(Modifier.fillMaxWidth().heightIn(min = 48.dp).clickable(role = Role.Button) { onCrew(CrewAction.Assign) }.padding(horizontal = 16.dp),
                verticalAlignment = Alignment.CenterVertically) {
                Icon(Icons.Filled.Add, null, tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(20.dp))
                Spacer(Modifier.width(10.dp))
                Text("Baustelle zuweisen", style = MaterialTheme.typography.labelLarge, color = MaterialTheme.colorScheme.primary)
            }
        }
    }
}

@Composable
private fun RosterLine(text: String, icon: ImageVector, tint: Color = tones.textSecondary) {
    Row(Modifier.padding(start = 14.dp, top = 4.dp), verticalAlignment = Alignment.Top) {
        Icon(icon, null, tint = tint, modifier = Modifier.padding(top = 2.dp).size(16.dp))
        Spacer(Modifier.width(8.dp))
        Text(text, style = MaterialTheme.typography.bodyMedium, color = tint)
    }
}

/** „Anpassen“: Zuweisen, Reihenfolge und Tauschen in einem Menü, jeweils voll beschriftet. */
@Composable
private fun AdjustMenu(crew: DayPlanCrew, canReorder: Boolean, canSwapCrews: Boolean, onCrew: (CrewAction) -> Unit) {
    var open by remember { mutableStateOf(false) }
    Box {
        TextButton(onClick = { open = true }, contentPadding = PaddingValues(horizontal = 10.dp),
            modifier = Modifier.semantics { contentDescription = "Anpassen, ${crew.name}" }) {
            Icon(Icons.Filled.Tune, null, modifier = Modifier.size(18.dp))
            Spacer(Modifier.width(6.dp))
            Text("Anpassen")
        }
        DropdownMenu(expanded = open, onDismissRequest = { open = false }) {
            fun pick(action: CrewAction) {
                open = false
                onCrew(action)
            }
            DropdownMenuItem(text = { Text("Baustelle zuweisen") }, leadingIcon = { Icon(Icons.Filled.Add, null) }, onClick = { pick(CrewAction.Assign) })
            if (canReorder) {
                DropdownMenuItem(text = { Text("Reihenfolge ändern") }, leadingIcon = { Icon(Icons.Filled.Reorder, null) }, onClick = { pick(CrewAction.Reorder) })
            }
            HorizontalDivider()
            DropdownMenuItem(text = { Text("Mitarbeiter tauschen") }, leadingIcon = { Icon(Icons.Filled.People, null) }, onClick = { pick(CrewAction.SwapPerson) })
            DropdownMenuItem(text = { Text("Fahrzeug tauschen") }, leadingIcon = { Icon(Icons.Filled.LocalShipping, null) }, onClick = { pick(CrewAction.SwapVehicle) })
            if (canSwapCrews) {
                DropdownMenuItem(text = { Text("Kolonnen tauschen") }, leadingIcon = { Icon(Icons.Filled.SwapHoriz, null) }, onClick = { pick(CrewAction.SwapCrews) })
            }
        }
    }
}

/**
 * Baustelle in der Fahrtreihenfolge: Nummer, Uhrzeit, Markierung seit der Freigabe, Bauvorhaben, Tätigkeit, Adresse (Route),
 * Ansprechpartner (Anruf), Notiz, Geräte, Hinweise. Tippen öffnet die Bauakte; rechts das Menü für Planer.
 */
@Composable
private fun StopRow(
    stop: DayPlanStop, tint: Color, canPlan: Boolean, hasCrew: Boolean, menuOpen: Boolean, onMenu: (Boolean) -> Unit, onOpen: () -> Unit,
    onRoute: () -> Unit, onCall: (String) -> Unit, onAction: (StopAction) -> Unit,
) {
    Row(Modifier.fillMaxWidth().clickable(onClickLabel = "Bauakte öffnen", onClick = onOpen).padding(start = 16.dp, end = 4.dp, top = 12.dp, bottom = 12.dp),
        verticalAlignment = Alignment.Top) {
        Box(Modifier.size(28.dp).clip(CircleShape).background(tint.copy(alpha = 0.14f)).semantics { contentDescription = "Station ${stop.order}" },
            contentAlignment = Alignment.Center) {
            Text("${stop.order}", style = MaterialTheme.typography.labelLarge, fontWeight = FontWeight.Bold, color = tint)
        }
        Spacer(Modifier.width(12.dp))
        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(stop.timeText, style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.SemiBold)
                stop.changeLabel?.let {
                    Spacer(Modifier.width(8.dp))
                    Pill(it, MaterialTheme.colorScheme.primary, tones.infoSoft)
                }
            }
            Text(stop.heading, style = MaterialTheme.typography.bodyLarge, fontWeight = FontWeight.SemiBold)
            if (stop.activity.isNotBlank()) Secondary(stop.activity)
            stop.address?.let { address ->
                LinkLine(address, Icons.Filled.Directions, "Route starten", onRoute)
            }
            (stop.siteContactName ?: stop.siteContactPhone)?.let { name ->
                val text = listOfNotNull(name, stop.siteContactPhone.takeIf { stop.siteContactName != null }).joinToString(", ")
                val phone = stop.siteContactPhone
                if (phone != null) LinkLine(text, Icons.Filled.Phone, "Anrufen") { onCall(phone) } else Secondary(text)
            }
            stop.notes?.let { Secondary(it) }
            if (stop.resources.isNotEmpty()) Secondary(stop.resources.joinToString(", ") { it.name })
            stop.warnings.forEach { warning ->
                Row(verticalAlignment = Alignment.Top) {
                    Icon(Icons.Filled.Warning, null, tint = tones.warning, modifier = Modifier.padding(top = 2.dp).size(16.dp))
                    Spacer(Modifier.width(6.dp))
                    Text(warning, style = MaterialTheme.typography.bodyMedium, color = tones.warning)
                }
            }
        }
        if (canPlan) {
            Box {
                IconButton(onClick = { onMenu(true) }) { Icon(Icons.Filled.MoreVert, "Aktionen für ${stop.title}") }
                DropdownMenu(expanded = menuOpen, onDismissRequest = { onMenu(false) }) {
                    DropdownMenuItem(text = { Text("Verschieben") }, leadingIcon = { Icon(Icons.AutoMirrored.Filled.ArrowForward, null) },
                        onClick = { onAction(StopAction.Move) })
                    DropdownMenuItem(text = { Text("Uhrzeit ändern") }, leadingIcon = { Icon(Icons.Filled.Schedule, null) }, onClick = { onAction(StopAction.Time) })
                    if (hasCrew) {
                        DropdownMenuItem(text = { Text("Auf nächsten freien Tag") }, leadingIcon = { Icon(Icons.Filled.EventAvailable, null) },
                            onClick = { onAction(StopAction.NextFreeDay) })
                    }
                    HorizontalDivider()
                    DropdownMenuItem(text = { Text("Aus dem Plan nehmen", color = tones.danger) }, leadingIcon = { Icon(Icons.Outlined.Delete, null, tint = tones.danger) },
                        onClick = { onAction(StopAction.Remove) })
                }
            }
        }
    }
}

@Composable
private fun LinkLine(text: String, icon: ImageVector, label: String, onClick: () -> Unit) {
    Row(Modifier.clip(RoundedCornerShape(6.dp)).clickable(onClickLabel = label, role = Role.Button, onClick = onClick).padding(vertical = 2.dp),
        verticalAlignment = Alignment.Top) {
        Icon(icon, null, tint = MaterialTheme.colorScheme.primary, modifier = Modifier.padding(top = 2.dp).size(16.dp))
        Spacer(Modifier.width(6.dp))
        Text(text, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.primary)
    }
}

/** Wer an dem Tag frei (keiner Kolonne zugeteilt) und wer abwesend ist. */
@Composable
private fun PeopleCard(plan: DayPlan) {
    SectionCard(padding = PaddingValues(16.dp)) {
        Text("Mitarbeiter", style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.SemiBold)
        if (plan.free.isNotEmpty()) {
            Spacer(Modifier.height(6.dp))
            Text("Frei: " + plan.free.joinToString(", ") { it.name }, style = MaterialTheme.typography.bodyMedium)
        }
        if (plan.absent.isNotEmpty()) {
            Spacer(Modifier.height(6.dp))
            Text("Abwesend: " + plan.absent.joinToString(", ") { "${it.name} (${it.absenceNote ?: "abwesend"})" }, style = MaterialTheme.typography.bodyMedium)
        }
    }
}

// Blätter

/** Gerüst der Blätter: oben „Abbrechen“, Titel und Bestätigen (wie die iOS-Blätter), darunter der Inhalt. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun PlanSheet(
    title: String, confirm: String?, canConfirm: Boolean, onDismiss: () -> Unit, onConfirm: () -> Unit, content: @Composable ColumnScope.() -> Unit,
) {
    ModalBottomSheet(onDismissRequest = onDismiss, sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true), containerColor = tones.canvas) {
        Row(Modifier.fillMaxWidth().padding(horizontal = 8.dp), verticalAlignment = Alignment.CenterVertically) {
            TextButton(onClick = onDismiss, modifier = Modifier.width(112.dp)) { Text("Abbrechen", maxLines = 1) }
            Text(title, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold, textAlign = TextAlign.Center, maxLines = 1,
                overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f))
            Box(Modifier.width(112.dp), contentAlignment = Alignment.CenterEnd) {
                if (confirm != null) TextButton(onClick = onConfirm, enabled = canConfirm) { Text(confirm, fontWeight = FontWeight.SemiBold, maxLines = 1) }
            }
        }
        Column(Modifier.verticalScroll(rememberScrollState()).imePadding().padding(horizontal = 16.dp).padding(top = 4.dp, bottom = 32.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp), content = content)
    }
}

@Composable
private fun SheetLabel(text: String) {
    Text(text, style = MaterialTheme.typography.titleSmall, color = tones.textSecondary, modifier = Modifier.padding(start = 4.dp, top = 8.dp))
}

/** Zeile mit Uhrzeit; Tippen öffnet die Eingabe (24 Stunden). */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun TimeRow(label: String, time: LocalTime, onChange: (LocalTime) -> Unit) {
    var open by remember { mutableStateOf(false) }
    Row(Modifier.fillMaxWidth().heightIn(min = 52.dp).clickable(onClickLabel = "$label ändern") { open = true }.padding(horizontal = 16.dp),
        verticalAlignment = Alignment.CenterVertically) {
        Text(label, style = MaterialTheme.typography.bodyLarge, modifier = Modifier.weight(1f))
        Text(PlanTime.clock(time), style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.primary)
    }
    if (open) {
        val state = rememberTimePickerState(initialHour = time.hour, initialMinute = time.minute, is24Hour = true)
        AlertDialog(
            onDismissRequest = { open = false },
            title = { Text(label) },
            text = { TimeInput(state) },
            confirmButton = { TextButton(onClick = { onChange(LocalTime.of(state.hour, state.minute)); open = false }) { Text("Übernehmen") } },
            dismissButton = { TextButton(onClick = { open = false }) { Text("Abbrechen") } },
        )
    }
}

@Composable
private fun SwitchRow(label: String, checked: Boolean, onChange: (Boolean) -> Unit) {
    Row(Modifier.fillMaxWidth().heightIn(min = 52.dp).clickable(role = Role.Switch) { onChange(!checked) }.padding(horizontal = 16.dp),
        verticalAlignment = Alignment.CenterVertically) {
        Text(label, style = MaterialTheme.typography.bodyLarge, modifier = Modifier.weight(1f))
        Switch(checked = checked, onCheckedChange = onChange)
    }
}

/** Auswahlzeile mit Radioknopf: Name vollständig, Zusatz darunter. */
@Composable
private fun ChoiceRow(title: String, detail: String?, selected: Boolean, leading: (@Composable () -> Unit)? = null, onClick: () -> Unit) {
    Row(Modifier.fillMaxWidth().heightIn(min = 52.dp).clickable(role = Role.RadioButton, onClick = onClick).padding(start = 4.dp, end = 16.dp, top = 4.dp, bottom = 4.dp),
        verticalAlignment = Alignment.CenterVertically) {
        RadioButton(selected = selected, onClick = onClick)
        leading?.invoke()
        Column(Modifier.weight(1f)) {
            Text(title, style = MaterialTheme.typography.bodyLarge)
            detail?.let { Secondary(it) }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun PlanDatePicker(initial: LocalDate, min: LocalDate?, onPick: (LocalDate) -> Unit, onDismiss: () -> Unit) {
    val state = rememberDatePickerState(
        initialSelectedDateMillis = initial.atStartOfDay().toInstant(ZoneOffset.UTC).toEpochMilli(),
        selectableDates = object : SelectableDates {
            override fun isSelectableDate(utcTimeMillis: Long): Boolean =
                min == null || !Instant.ofEpochMilli(utcTimeMillis).atZone(ZoneOffset.UTC).toLocalDate().isBefore(min)
        },
    )
    DatePickerDialog(onDismissRequest = onDismiss, confirmButton = {
        TextButton(onClick = {
            state.selectedDateMillis?.let { onPick(Instant.ofEpochMilli(it).atZone(ZoneOffset.UTC).toLocalDate()) }
            onDismiss()
        }) { Text("Übernehmen") }
    }, dismissButton = { TextButton(onClick = onDismiss) { Text("Abbrechen") } }) { DatePicker(state) }
}

/** Konfliktprüfung beim Server ohne Speichern (verzögert, damit nicht jede Eingabe eine Anfrage auslöst). */
@Composable
private fun ConflictPreview(desk: DispatchDesk, change: PlanChange?) {
    var warnings by remember { mutableStateOf<List<String>?>(emptyList()) }
    var checking by remember { mutableStateOf(false) }
    LaunchedEffect(change) {
        if (change == null) {
            warnings = emptyList()
            return@LaunchedEffect
        }
        checking = true
        delay(350)
        warnings = withContext(Dispatchers.IO) { desk.check(change) }
        checking = false
    }
    SectionCard(padding = PaddingValues(16.dp)) {
        val list = warnings
        when {
            checking -> Row(verticalAlignment = Alignment.CenterVertically) {
                CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp)
                Spacer(Modifier.width(10.dp))
                Secondary("Konflikte werden geprüft")
            }
            list == null -> Row(verticalAlignment = Alignment.CenterVertically) {
                Icon(Icons.Filled.CloudOff, null, tint = tones.textSecondary, modifier = Modifier.size(18.dp))
                Spacer(Modifier.width(10.dp))
                Secondary("Die Konfliktprüfung braucht eine Verbindung.")
            }
            list.isEmpty() -> Row(verticalAlignment = Alignment.CenterVertically) {
                Icon(Icons.Filled.CheckCircle, null, tint = tones.success, modifier = Modifier.size(18.dp))
                Spacer(Modifier.width(10.dp))
                Text("Keine Konflikte", style = MaterialTheme.typography.bodyMedium, color = tones.success)
            }
            else -> Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                list.forEach { warning ->
                    Row(verticalAlignment = Alignment.Top) {
                        Icon(Icons.Filled.Warning, null, tint = tones.warning, modifier = Modifier.padding(top = 2.dp).size(18.dp))
                        Spacer(Modifier.width(10.dp))
                        Text(warning, style = MaterialTheme.typography.bodyMedium, color = tones.warning)
                    }
                }
                Secondary("Konflikte verhindern das Einplanen nicht. Bitte prüfen Sie den Plan.")
            }
        }
    }
}

/** Baustelle zuweisen: Bauvorhaben suchen (ungeplante zuerst), Tätigkeit, Uhrzeit, Konfliktprüfung. */
@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun AssignSheet(desk: DispatchDesk, crew: DayPlanCrew, day: LocalDate, onDone: (PlanChange?) -> Unit) {
    val session = LocalSession.current
    var search by remember { mutableStateOf("") }
    var backlog by remember { mutableStateOf<List<BacklogEntry>?>(null) }
    var offline by remember { mutableStateOf(false) }
    var choices by remember { mutableStateOf<List<PlanChoice>?>(null) }
    var selected by remember { mutableStateOf<PlanChoice?>(null) }
    var activity by remember { mutableStateOf(PlanChoices.activities.first()) }
    val suggested = remember(crew) { PlanTime.suggestedStart(crew) }
    var start by remember { mutableStateOf(suggested) }
    var hasEnd by remember { mutableStateOf(false) }
    var end by remember { mutableStateOf(PlanTime.suggestedEnd(suggested)) }
    val newId = remember { Ids.v7() }
    LaunchedEffect(Unit) {
        backlog = try {
            withContext(Dispatchers.IO) { desk.api.planningBacklog() }
        } catch (e: ApiException) {
            offline = true
            emptyList()
        }
    }
    LaunchedEffect(search, backlog, offline) {
        delay(150)
        val loaded = backlog ?: return@LaunchedEffect
        choices = if (!offline) {
            PlanChoices.from(loaded, crew, search)
        } else {
            val text = search.trim().lowercase()
            withContext(Dispatchers.IO) { session.service.bauakten() }
                .filter { text.isEmpty() || "${it.number ?: ""} ${it.title} ${it.city ?: ""}".lowercase().contains(text) }
                .take(60).map { PlanChoice(it.id, listOfNotNull(it.number, it.title).joinToString(", "), it.addressLine, isFree = false) }
        }
    }
    val change = selected?.let {
        PlanChange.Assign(newId, it.id, it.title, crew.id, day.toString(), activity, PlanTime.text(start), if (hasEnd) PlanTime.text(maxOf(end, start)) else null)
    }
    PlanSheet("Baustelle zuweisen", "Einplanen", change != null, onDismiss = { onDone(null) }, onConfirm = { onDone(change) }) {
        Secondary("${crew.name}, ${Dates.long(day)}", Modifier.padding(start = 4.dp))
        val chosen = selected
        if (chosen == null) {
            OutlinedTextField(search, { search = it }, label = { Text("Bauvorhaben suchen") }, singleLine = true, modifier = Modifier.fillMaxWidth(),
                leadingIcon = { Icon(Icons.Filled.Search, null) })
            if (offline) Secondary("Ohne Verbindung: Bauakten auf diesem Gerät.", Modifier.padding(start = 4.dp))
            SectionCard {
                val list = choices
                when {
                    list == null -> Row(Modifier.padding(16.dp), verticalAlignment = Alignment.CenterVertically) {
                        CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp)
                        Spacer(Modifier.width(10.dp))
                        Secondary("Bauvorhaben werden geladen")
                    }
                    list.isEmpty() -> Secondary("Keine passenden Bauvorhaben.", Modifier.padding(16.dp))
                    else -> list.forEachIndexed { index, choice ->
                        if (index > 0) RowDivider(16.dp)
                        RowItem(choice.title, subtitle = choice.subtitle, onClick = { selected = choice },
                            trailing = { if (choice.isFree) Pill("Ungeplant", MaterialTheme.colorScheme.primary, tones.infoSoft) })
                    }
                }
            }
        } else {
            SectionCard {
                RowItem(chosen.title, subtitle = chosen.subtitle, trailing = { TextButton(onClick = { selected = null }) { Text("Ändern") } })
            }
            SheetLabel("Tätigkeit")
            FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                PlanChoices.activities.forEach { option -> FilterChip(selected = activity == option, onClick = { activity = option }, label = { Text(option) }) }
            }
            SheetLabel("Uhrzeit")
            SectionCard {
                TimeRow("Beginn", start) { start = it }
                RowDivider(16.dp)
                SwitchRow("Ende festlegen", hasEnd) { hasEnd = it }
                if (hasEnd) {
                    RowDivider(16.dp)
                    TimeRow("Ende", end) { end = it }
                }
            }
            SheetLabel("Prüfung")
            ConflictPreview(desk, change)
        }
    }
}

/** Verschieben: Tag (Morgen, Übermorgen, +1 Woche, Datum), Kolonne, Uhrzeit; [timeOnly] = nur die Uhrzeit ändern. */
@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun MoveSheet(desk: DispatchDesk, plan: DayPlan, stop: DayPlanStop, crewId: String?, day: LocalDate, timeOnly: Boolean, onDone: (PlanChange?) -> Unit) {
    var targetDay by remember { mutableStateOf(day) }
    var targetCrew by remember { mutableStateOf(crewId) }
    var changeTime by remember { mutableStateOf(timeOnly) }
    var start by remember { mutableStateOf(PlanTime.parse(stop.startTime)) }
    var hasEnd by remember { mutableStateOf(stop.endTime != null) }
    var end by remember { mutableStateOf(PlanTime.parse(stop.endTime, LocalTime.of(16, 0))) }
    var picking by remember { mutableStateOf(false) }
    val change = PlanMove.change(stop, day, crewId, targetDay, targetCrew, changeTime, start, if (hasEnd) maxOf(end, start) else null)
    PlanSheet(if (timeOnly) "Uhrzeit ändern" else "Verschieben", if (timeOnly) "Übernehmen" else "Verschieben", change != null,
        onDismiss = { onDone(null) }, onConfirm = { onDone(change) }) {
        SectionCard(padding = PaddingValues(16.dp)) {
            Text(stop.heading, style = MaterialTheme.typography.bodyLarge, fontWeight = FontWeight.SemiBold)
            Secondary(listOf(stop.activity, stop.timeText).filter { it.isNotBlank() }.joinToString(", "))
        }
        if (!timeOnly) {
            SheetLabel("Tag")
            val quick = PlanMove.quickDays.map { (label, offset) -> label to day.plusDays(offset) }
            FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                quick.forEach { (label, date) -> FilterChip(selected = targetDay == date, onClick = { targetDay = date }, label = { Text(label) }) }
                FilterChip(selected = targetDay != day && quick.none { it.second == targetDay }, onClick = { picking = true }, label = { Text("Datum") },
                    leadingIcon = { Icon(Icons.Filled.CalendarMonth, null, Modifier.size(18.dp)) })
            }
            Secondary(if (targetDay == day) "Gleicher Tag: ${Dates.long(day)}" else "Neuer Tag: ${Dates.long(targetDay)}", Modifier.padding(start = 4.dp))
            SheetLabel("Kolonne")
            SectionCard {
                if (crewId == null) ChoiceRow("Ohne Kolonne", null, targetCrew == null) { targetCrew = null }
                plan.crews.forEachIndexed { index, crew ->
                    if (index > 0 || crewId == null) RowDivider(56.dp)
                    ChoiceRow(crew.name, crew.memberText.ifEmpty { null }, targetCrew == crew.id) { targetCrew = crew.id }
                }
            }
        }
        SheetLabel("Uhrzeit")
        SectionCard {
            if (!timeOnly) SwitchRow("Uhrzeit ändern", changeTime) { changeTime = it }
            if (changeTime) {
                if (!timeOnly) RowDivider(16.dp)
                TimeRow("Beginn", start) { start = it }
                RowDivider(16.dp)
                SwitchRow("Ende festlegen", hasEnd) { hasEnd = it }
                if (hasEnd) {
                    RowDivider(16.dp)
                    TimeRow("Ende", end) { end = it }
                }
            }
        }
        if (change != null) {
            SheetLabel("Prüfung")
            ConflictPreview(desk, change)
        }
    }
    if (picking) PlanDatePicker(targetDay, null, onPick = { targetDay = it }, onDismiss = { picking = false })
}

/** Mitarbeiter bzw. Fahrzeug für den Tag tauschen (freie zuerst, dann aus anderen Kolonnen mit Gegentausch). */
@Composable
private fun SwapRosterSheet(desk: DispatchDesk, plan: DayPlan, crew: DayPlanCrew, day: LocalDate, vehicle: Boolean, onDone: (PlanChange?) -> Unit) {
    var resources by remember { mutableStateOf(emptyList<PlanningResource>()) }
    LaunchedEffect(vehicle) {
        if (vehicle) resources = try { withContext(Dispatchers.IO) { desk.api.planningResources() } } catch (e: ApiException) { emptyList() }
    }
    val swap = remember(resources) { RosterSwap(crew, plan, vehicle, resources) }
    var outgoing by remember { mutableStateOf(RosterSwap(crew, plan, vehicle).defaultOutgoing) }
    var incoming by remember { mutableStateOf<String?>(null) }
    val change = swap.change(day.toString(), outgoing, incoming)
    PlanSheet(if (vehicle) "Fahrzeug tauschen" else "Mitarbeiter tauschen", "Tauschen", change != null,
        onDismiss = { onDone(null) }, onConfirm = { onDone(change) }) {
        Secondary("${crew.name}, ${Dates.long(day)}", Modifier.padding(start = 4.dp))
        SheetLabel(if (vehicle) "Bisheriges Fahrzeug" else "Wer fällt aus?")
        SectionCard {
            ChoiceRow(if (vehicle) "Keins (zusätzlich)" else "Niemand (zusätzlich)", null, outgoing == null) { outgoing = null }
            swap.outOptions.forEach { option ->
                RowDivider(56.dp)
                ChoiceRow(option.name, option.detail, outgoing == option.id) { outgoing = option.id }
            }
        }
        SheetLabel(if (vehicle) "Stattdessen" else "Wer kommt dafür?")
        SectionCard {
            val options = swap.inOptions
            if (options.isEmpty()) {
                Secondary(if (vehicle) "Keine weiteren Fahrzeuge oder Geräte." else "Niemand frei oder in einer anderen Kolonne verfügbar.", Modifier.padding(16.dp))
            }
            options.forEachIndexed { index, option ->
                if (index > 0) RowDivider(56.dp)
                ChoiceRow(option.name, option.detail, incoming == option.id) { incoming = if (incoming == option.id) null else option.id }
            }
        }
        Secondary(if (vehicle) "Gilt nur für diesen Tag. Ist das Fahrzeug bei einer anderen Kolonne, bekommt diese im Gegenzug das bisherige."
            else "Gilt nur für diesen Tag. Ist die Person in einer anderen Kolonne, wechselt die ausfallende Person im Gegenzug dorthin.",
            Modifier.padding(horizontal = 4.dp))
    }
}

/** Zwei Kolonnen tauschen für den Tag ihre Baustellen; Besetzung und Fahrzeuge bleiben. */
@Composable
private fun SwapCrewsSheet(plan: DayPlan, crew: DayPlanCrew, day: LocalDate, onDone: (PlanChange?) -> Unit) {
    val others = plan.crews.filter { it.id != crew.id }
    var other by remember { mutableStateOf(others.firstOrNull { it.stops.isNotEmpty() }?.id ?: others.firstOrNull()?.id) }
    val target = others.firstOrNull { it.id == other }
    PlanSheet("Kolonnen tauschen", "Tauschen", target != null, onDismiss = { onDone(null) }, onConfirm = {
        target?.let { onDone(PlanChange.SwapCrews(day.toString(), crew.id, it.id, "${crew.name} und ${it.name} tauschen")) }
    }) {
        SheetLabel("${crew.name} hat")
        SectionCard(padding = PaddingValues(16.dp)) { StopsText(crew) }
        SheetLabel("Tauschen mit")
        SectionCard {
            others.forEachIndexed { index, candidate ->
                if (index > 0) RowDivider(56.dp)
                Row(Modifier.fillMaxWidth().clickable(role = Role.RadioButton) { other = candidate.id }.padding(start = 4.dp, end = 16.dp, top = 8.dp, bottom = 8.dp),
                    verticalAlignment = Alignment.Top) {
                    RadioButton(selected = other == candidate.id, onClick = { other = candidate.id })
                    Column(Modifier.weight(1f).padding(top = 12.dp)) {
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Box(Modifier.width(4.dp).height(18.dp).clip(RoundedCornerShape(2.dp)).background(parseColor(candidate.color) ?: MaterialTheme.colorScheme.primary))
                            Spacer(Modifier.width(8.dp))
                            Text(candidate.name, style = MaterialTheme.typography.bodyLarge, fontWeight = FontWeight.SemiBold)
                        }
                        StopsText(candidate)
                    }
                }
            }
        }
        Secondary("Die Baustellen beider Kolonnen werden für diesen Tag getauscht. Besetzung und Fahrzeuge bleiben unverändert.",
            Modifier.padding(horizontal = 4.dp))
    }
}

@Composable
private fun StopsText(crew: DayPlanCrew) {
    if (crew.stops.isEmpty()) Secondary("Keine Baustelle")
    crew.stops.forEach { Secondary("${it.startText ?: "ganztägig"}, ${it.title}") }
}

/**
 * Fahrtreihenfolge ändern: Baustellen am Griff ziehen (oder über die Bedienungshilfen nach oben bzw. unten). Die Uhrzeiten
 * bleiben in ihrer Reihenfolge, die Baustellen tauschen die Plätze (wie auf iOS und im Web).
 */
@Composable
private fun ReorderSheet(crew: DayPlanCrew, day: LocalDate, onDone: (PlanChange?) -> Unit) {
    val ids = remember(crew) { mutableStateListOf<String>().also { list -> list.addAll(crew.stops.map { it.assignmentId }) } }
    val byId = remember(crew) { crew.stops.associateBy { it.assignmentId } }
    val times = remember(crew) { crew.stops.map { Dates.clock(it.startTime) } }
    var dragging by remember { mutableStateOf<String?>(null) }
    var offset by remember { mutableFloatStateOf(0f) }
    val rowHeight = 64.dp
    val step = with(LocalDensity.current) { (rowHeight + 1.dp).toPx() }
    val change = PlanOrder.change(crew, day.toString(), ids.toList())
    fun move(id: String, by: Int) {
        val from = ids.indexOf(id)
        val to = from + by
        if (from >= 0 && to in ids.indices) ids.add(to, ids.removeAt(from))
    }
    PlanSheet("Reihenfolge", "Sichern", change != null, onDismiss = { onDone(null) }, onConfirm = { onDone(change) }) {
        Secondary("${crew.name}, ${Dates.long(day)}. Ziehen Sie die Baustellen am Griff in die gewünschte Fahrtreihenfolge. Die Uhrzeiten bleiben in ihrer Reihenfolge.",
            Modifier.padding(horizontal = 4.dp))
        SectionCard {
            ids.forEachIndexed { index, id ->
                val stop = byId[id]
                if (stop != null) key(id) {
                    if (index > 0) RowDivider(56.dp)
                    val active = dragging == id
                    Row(
                        Modifier.fillMaxWidth().height(rowHeight).zIndex(if (active) 1f else 0f)
                            .graphicsLayer { translationY = if (active) offset else 0f }
                            .background(if (active) tones.fill else Color.Transparent)
                            .semantics {
                                customActions = listOf(
                                    CustomAccessibilityAction("Nach oben") { move(id, -1); true },
                                    CustomAccessibilityAction("Nach unten") { move(id, 1); true },
                                )
                            }
                            .padding(start = 16.dp, end = 4.dp),
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        Box(Modifier.size(28.dp).clip(CircleShape).background(tones.infoSoft), contentAlignment = Alignment.Center) {
                            Text("${index + 1}", style = MaterialTheme.typography.labelLarge, fontWeight = FontWeight.Bold, color = MaterialTheme.colorScheme.primary)
                        }
                        Spacer(Modifier.width(12.dp))
                        Column(Modifier.weight(1f)) {
                            Text(stop.heading, style = MaterialTheme.typography.bodyLarge, maxLines = 1, overflow = TextOverflow.Ellipsis)
                            Secondary(listOfNotNull(times.getOrNull(index) ?: "ganztägig", stop.activity.takeIf { it.isNotBlank() }).joinToString(", "), maxLines = 1)
                        }
                        Icon(Icons.Filled.DragHandle, "Ziehen, um ${stop.title} zu verschieben", tint = tones.textSecondary,
                            modifier = Modifier.size(48.dp).padding(12.dp).pointerInput(id) {
                                detectDragGestures(
                                    onDragStart = { dragging = id; offset = 0f },
                                    onDragEnd = { dragging = null; offset = 0f },
                                    onDragCancel = { dragging = null; offset = 0f },
                                ) { pointer, amount ->
                                    pointer.consume()
                                    offset += amount.y
                                    val current = ids.indexOf(id)
                                    if (offset > step * 0.6f && current < ids.lastIndex) {
                                        move(id, 1)
                                        offset -= step
                                    } else if (offset < -step * 0.6f && current > 0) {
                                        move(id, -1)
                                        offset += step
                                    }
                                }
                            })
                    }
                }
            }
        }
    }
}

/** Freigabe: Zeitraum ab dem Tag, Hinweis an die Mitarbeiter, Änderungen seit der letzten Freigabe; zurücknehmen. */
@Composable
private fun ReleaseSheet(plan: DayPlan, day: LocalDate, today: LocalDate, onDone: (ReleaseAction?) -> Unit) {
    var until by remember { mutableStateOf(day) }
    var note by remember { mutableStateOf(plan.release?.note ?: "") }
    var picking by remember { mutableStateOf(false) }
    var confirmWithdraw by remember { mutableStateOf(false) }
    val release = plan.release
    val changed = plan.crews.flatMap { crew -> crew.stops.filter { it.isChanged }.map { it to crew.name } }
    PlanSheet("Freigabe", "Freigeben", true, onDismiss = { onDone(null) }, onConfirm = { onDone(ReleaseAction.Release(until, note)) }) {
        SheetLabel(Dates.long(day, today))
        SectionCard(padding = PaddingValues(16.dp)) {
            if (release != null) {
                Text("Freigegeben ${DispatchText.stamp(release.released, today)}", style = MaterialTheme.typography.bodyLarge)
                if (release.releasedByName.isNotBlank()) Secondary("von ${release.releasedByName}")
                release.note?.let { Secondary("Hinweis: $it") }
            } else {
                Row(verticalAlignment = Alignment.Top) {
                    Icon(Icons.Filled.Schedule, null, tint = tones.textSecondary, modifier = Modifier.padding(top = 2.dp).size(18.dp))
                    Spacer(Modifier.width(10.dp))
                    Secondary("Noch nicht freigegeben. Mitarbeiter sehen den Plan als vorläufig.")
                }
            }
        }
        SheetLabel("Freigabe")
        SectionCard {
            RowItem("Freigeben bis", value = Dates.relativeDay(until, today), showChevron = true, onClick = { picking = true })
            OutlinedTextField(note, { note = it }, label = { Text("Hinweis an die Mitarbeiter (optional)") }, minLines = 2, maxLines = 5,
                modifier = Modifier.fillMaxWidth().padding(start = 16.dp, end = 16.dp, bottom = 16.dp))
        }
        Secondary("Die Freigabe hält den aktuellen Stand fest. Spätere Änderungen werden danach als „Neu“ bzw. „Geändert“ markiert.",
            Modifier.padding(horizontal = 4.dp))
        SheetLabel(if (release == null) "Umfang" else "Änderungen seit der Freigabe")
        SectionCard(padding = PaddingValues(16.dp)) {
            if (release == null) {
                val crews = plan.crews.count { it.stops.isNotEmpty() }
                Text("${if (plan.stopCount == 1) "1 Einsatz" else "${plan.stopCount} Einsätze"} in ${if (crews == 1) "1 Kolonne" else "$crews Kolonnen"}",
                    style = MaterialTheme.typography.bodyMedium)
            } else if (changed.isEmpty() && release.removed.isEmpty()) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Icon(Icons.Filled.CheckCircle, null, tint = tones.success, modifier = Modifier.size(18.dp))
                    Spacer(Modifier.width(10.dp))
                    Text("Keine Änderungen", style = MaterialTheme.typography.bodyMedium, color = tones.success)
                }
            } else {
                Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                    changed.forEach { (stop, crewName) -> ChangeLine(stop.changeLabel ?: "Geändert", "${stop.timeText}, ${stop.title}", "$crewName, ${stop.activity}", false) }
                    release.removed.forEach { ChangeLine("Entfällt", it.title, listOfNotNull(it.crewName, it.activity).joinToString(", "), true) }
                }
            }
        }
        if (release != null) {
            TextButton(onClick = { confirmWithdraw = true }) { Text("Freigabe zurücknehmen", color = tones.danger) }
            Secondary("Der Tag gilt danach wieder als vorläufig.", Modifier.padding(horizontal = 4.dp))
        }
    }
    if (picking) PlanDatePicker(until, day, onPick = { until = maxOf(it, day) }, onDismiss = { picking = false })
    if (confirmWithdraw) {
        ConfirmDialog("Freigabe zurücknehmen?", "Der Tag gilt danach wieder als vorläufig. Die Mitarbeiter sehen den Plan nicht mehr als verbindlich.",
            "Zurücknehmen", { onDone(ReleaseAction.Withdraw) }, { confirmWithdraw = false })
    }
}

@Composable
private fun ChangeLine(kind: String, title: String, detail: String, removed: Boolean) {
    Row(verticalAlignment = Alignment.Top) {
        Pill(kind, if (removed) tones.danger else MaterialTheme.colorScheme.primary, if (removed) tones.dangerSoft else tones.infoSoft)
        Spacer(Modifier.width(10.dp))
        Column(Modifier.weight(1f)) {
            Text(title, style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.SemiBold)
            Secondary(detail)
        }
    }
}

/** Vorgemerkte Änderungen ansehen; abgelehnte verwerfen. */
@Composable
private fun PendingSheet(entries: List<PlanningOutbox.Entry>, today: LocalDate, onDiscard: (String) -> Unit, onDismiss: () -> Unit) {
    PlanSheet("Vorgemerkt", "Fertig", true, onDismiss = onDismiss, onConfirm = onDismiss) {
        if (entries.isEmpty()) {
            EmptyState(Icons.Filled.CheckCircle, "Nichts vorgemerkt")
        } else {
            Secondary("Diese Änderungen sendet die App, sobald eine Verbindung besteht.", Modifier.padding(horizontal = 4.dp))
            SectionCard {
                entries.forEachIndexed { index, entry ->
                    if (index > 0) RowDivider(16.dp)
                    Row(Modifier.fillMaxWidth().padding(start = 16.dp, end = 4.dp, top = 10.dp, bottom = 10.dp), verticalAlignment = Alignment.CenterVertically) {
                        Column(Modifier.weight(1f)) {
                            Text(entry.change.summary, style = MaterialTheme.typography.bodyLarge)
                            Secondary("Erfasst ${DispatchText.stamp(entry.created, today)}")
                            entry.error?.let { Text(it, style = MaterialTheme.typography.bodyMedium, color = tones.danger) }
                        }
                        TextButton(onClick = { onDiscard(entry.id) }) { Text("Verwerfen", color = tones.danger) }
                    }
                }
            }
        }
    }
}

/**
 * „Disposition heute“ auf Heute für Planer (Recht `planning.manage`): eine kompakte Zeile mit Kolonnen und Freigabestand,
 * Tippen öffnet die Tagesansicht. Erst der zuletzt geladene Stand, dann frisch vom Server.
 */
@Composable
fun DispatchTodayRow(refreshKey: Any?, modifier: Modifier = Modifier) {
    val session = LocalSession.current
    val nav = LocalNav.current
    val desk = remember(session) { DispatchDesk(session.api, session.store.backend) }
    val today = remember { LocalDate.now(Dates.zone).toString() }
    var plan by remember { mutableStateOf<DayPlan?>(null) }
    var loaded by remember { mutableStateOf(false) }
    LaunchedEffect(refreshKey) {
        if (plan == null) plan = withContext(Dispatchers.IO) { desk.cached(today)?.plan }
        plan = withContext(Dispatchers.IO) { desk.load(today).plan } ?: plan
        loaded = true
    }
    val current = plan
    SectionCard(modifier.padding(top = Space.s)) {
        RowItem("Disposition heute", icon = Icons.Outlined.DirectionsCar,
            subtitle = current?.let { DispatchText.summary(it) } ?: if (loaded) "Plan für heute nicht verfügbar" else "Wird geladen",
            showChevron = true, onClick = { nav.push(Route.Dispatch()) },
            trailing = {
                when (current?.let { DispatchText.tone(it) }) {
                    DispatchText.Tone.Success -> Icon(Icons.Filled.Verified, "Freigegeben", tint = tones.success, modifier = Modifier.size(20.dp))
                    DispatchText.Tone.Warning -> Icon(Icons.Filled.Autorenew, "Geändert seit Freigabe", tint = tones.warning, modifier = Modifier.size(20.dp))
                    else -> Unit
                }
            })
    }
}
