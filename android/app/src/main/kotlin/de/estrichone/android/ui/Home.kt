package de.estrichone.android.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.IntrinsicSize
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.statusBars
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.KeyboardArrowRight
import androidx.compose.material.icons.filled.Build
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.HourglassTop
import androidx.compose.material.icons.filled.WbSunny
import androidx.compose.ui.draw.alpha
import androidx.compose.material.icons.filled.CalendarMonth
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Directions
import androidx.compose.material.icons.filled.ErrorOutline
import androidx.compose.material.icons.filled.EventBusy
import androidx.compose.material.icons.filled.Groups
import androidx.compose.material.icons.filled.Handyman
import androidx.compose.material.icons.filled.Info
import androidx.compose.material.icons.filled.LocalShipping
import androidx.compose.material.icons.filled.Phone
import androidx.compose.material.icons.filled.PhotoCamera
import androidx.compose.material.icons.filled.PlayCircle
import androidx.compose.material.icons.filled.Report
import androidx.compose.material.icons.filled.Warning
import androidx.compose.material.icons.outlined.AccountCircle
import androidx.compose.material.icons.outlined.ChatBubbleOutline
import androidx.compose.material.icons.outlined.Folder
import androidx.compose.material.icons.automirrored.outlined.MenuBook
import androidx.compose.material.icons.outlined.ReportProblem
import androidx.compose.material.icons.outlined.Place
import androidx.compose.foundation.layout.heightIn
import androidx.compose.material.icons.filled.CloudDone
import androidx.compose.material.icons.filled.MoreVert
import androidx.compose.material.icons.outlined.Checklist
import androidx.compose.material.icons.outlined.DirectionsCar
import androidx.compose.material3.Button
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.OutlinedIconButton
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import de.estrichone.android.Session
import de.estrichone.core.myVacation
import de.estrichone.core.chatBauakteGroup
import de.estrichone.core.TodayOverview
import androidx.compose.material.icons.filled.ExpandMore
import androidx.compose.material.icons.filled.ExpandLess
import de.estrichone.android.support.Links
import de.estrichone.core.AppModules
import de.estrichone.core.ApiException
import de.estrichone.core.Dates
import de.estrichone.core.EstrichOneApi
import de.estrichone.core.MyPlanning
import de.estrichone.core.NoticeItem
import de.estrichone.core.NoticePresentation
import de.estrichone.core.PlannedJob
import de.estrichone.core.SiteGlance
import de.estrichone.core.TodayGlance
import de.estrichone.core.WorkItemKind
import de.estrichone.core.dueText
import de.estrichone.core.myPlan
import androidx.compose.material3.pulltorefresh.PullToRefreshBox
import androidx.compose.runtime.rememberCoroutineScope
import kotlinx.coroutines.launch
import androidx.compose.foundation.layout.fillMaxSize
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.withContext
import java.time.Instant
import java.time.LocalDate

/** Zuletzt geladener Plan (ohne Netz), damit „Mein Einsatz“ sofort erscheint und nicht erst nach dem Abruf. */
fun Session.cachedWeekPlan(): MyPlanning? =
    runCatching { store.backend.meta("plan")?.let { EstrichOneApi.json.decodeFromString(MyPlanning.serializer(), it) } }.getOrNull()

/** Planung der nächsten 7 Tage; offline aus dem letzten Abruf (lokal gespeichert). */
suspend fun Session.weekPlan(today: LocalDate = LocalDate.now(Dates.zone)): MyPlanning? = withContext(Dispatchers.IO) {
    try {
        api.myPlan(today.toString(), today.plusDays(6).toString()).also { plan ->
            store.backend.transaction { store.backend.setMeta("plan", EstrichOneApi.json.encodeToString(MyPlanning.serializer(), plan)) }
        }
    } catch (e: ApiException) {
        store.backend.meta("plan")?.let { runCatching { EstrichOneApi.json.decodeFromString(MyPlanning.serializer(), it) }.getOrNull() }
    }
}

/**
 * Heute (gleiches Konzept wie iOS): Arbeit zuerst. Oben „Mein Tag“ in drei Kennzahlen, Mitteilungen der Verwaltung als
 * eine Zeile (aufklappbar, Störungen rot), dann der laufende bzw. nächste Einsatz als Hauptkarte mit Schnellaktionen,
 * weitere Einsätze kompakt, offene Aufgaben und Freigaben, Morgen.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun HomeScreen() {
    val session = LocalSession.current
    val nav = LocalNav.current
    val model = LocalModel.current
    val today = remember { LocalDate.now(Dates.zone) }
    val status by session.status.collectAsState()
    val cached = remember { session.cachedWeekPlan() }
    val plan by produceState(cached, status.outcome) { value = session.weekPlan(today) ?: value }
    val scope = rememberCoroutineScope()
    val tasks by session.local { session.service.myTasks().filter { it.isOpen } }
    val modules by session.modules.collectAsState()
    // Uhr für „Jetzt“/„in 2 Std.“ (minütlich).
    var clock by remember { mutableStateOf(Live.clock()) }
    LaunchedEffect(Unit) { while (Live.polling) { delay(60_000); clock = Live.clock() } }
    // Freigaben (Urlaubsanträge) nur für Genehmiger und nur online; ohne Verbindung entfällt die Zeile.
    val vacation by produceState<de.estrichone.core.MyVacation?>(null, status.outcome) {
        if (modules.isOn(AppModules.ABSENCE)) value = runCatching { withContext(Dispatchers.IO) { session.api.myVacation() } }.getOrNull() ?: value
    }
    val approvals = vacation?.takeIf { it.canApprove }?.pendingApprovals ?: 0

    ScreenScaffold("Heute", root = true, subtitle = Dates.long(today), actions = { SyncAction(); NotificationAction(); HomeOverflow() }) { padding ->
        // Nach unten ziehen: abgleichen und Plan, Hinweise und Zähler neu laden.
        PullToRefreshBox(isRefreshing = status.running, onRefresh = { scope.launch { session.synchronize() } }, modifier = Modifier.fillMaxSize()) {
        LazyColumn(contentPadding = padding.content(), modifier = Modifier.readable()) {
            val planning = modules.isOn(AppModules.PLANNING)
            val showsTasks = modules.anyOn(AppModules.TASKS, AppModules.DEFECTS)
            val todays = plan?.assignments?.filter { it.day == today.toString() }.orEmpty()
            val day = TodayOverview.plan(todays, clock)
            item(key = "status") {
                OfflineBanner(session)
                TeamsRolloutCard()
            }
            item(key = "notices") { NoticeSummary(session) }
            if (planning) {
                when {
                    plan == null -> item(key = "loading") { SkeletonRows(3) }
                    day.focus == null -> item(key = "free") {
                        SectionHeader("Einsätze")
                        SectionCard { RowItem("Heute ist nichts für Sie eingeplant.", icon = Icons.Filled.EventBusy, iconTint = tones.textSecondary,
                            subtitle = "Ihre nächsten Einsätze stehen unter Morgen bzw. Meine Woche.") }
                    }
                    else -> {
                        val focus = day.focus!!
                        item(key = "focus-label") { SectionHeader(day.focusTitle) }
                        item(key = "focus-${focus.id}") { TodayJobCard(focus, session, day.focusPhase) }
                        if (day.others.isNotEmpty()) {
                            item(key = "others-label") { SectionHeader("Weitere Einsätze heute (${day.others.size})") }
                            item(key = "others") {
                                SectionCard {
                                    day.others.forEachIndexed { index, job ->
                                        if (index > 0) RowDivider(16.dp)
                                        CompactJobRow(job, session, TodayGlance.phase(job.startTime, job.endTime, clock))
                                    }
                                }
                            }
                        }
                    }
                }
            }
            if (showsTasks || approvals > 0) {
                item(key = "tasks-label") {
                    SectionHeader(if (approvals > 0) "Aufgaben und Freigaben" else "Offene Aufgaben",
                        action = if (tasks.size > 3) "Alle (${tasks.size})" else null, onAction = { nav.push(Route.Tasks) })
                }
                item(key = "tasks") {
                    SectionCard {
                        if (approvals > 0) {
                            RowItem("Anträge genehmigen", icon = Icons.Filled.HourglassTop, iconTint = tones.warning, value = approvals.toString(), showChevron = true,
                                onClick = { nav.open(de.estrichone.core.NotificationRoute.Absences(approvals = true)) })
                            if (tasks.isNotEmpty() || showsTasks) RowDivider(16.dp)
                        }
                        if (showsTasks && tasks.isEmpty()) {
                            RowItem("Keine offenen Aufgaben", icon = Icons.Filled.CheckCircle, iconTint = tones.success)
                        }
                        tasks.sortedWith(compareBy<de.estrichone.core.Task>({ it.dueDate ?: LocalDate.MAX }, { it.kind != WorkItemKind.Defect })).take(3).forEachIndexed { index, task ->
                            if (index > 0) RowDivider(60.dp)
                            val site = task.bauakteId?.let { session.service.bauakte(it) }
                            TaskRow(task, session, subtitle = site?.let { listOfNotNull(it.number, it.title).joinToString(", ") })
                        }
                    }
                }
            }
            if (planning) {
                val tomorrow = today.plusDays(1).toString()
                val upcoming = plan?.assignments?.filter { it.day > today.toString() }.orEmpty().sortedBy { it.day + (it.startTime ?: "99") }
                val next = upcoming.filter { it.day == tomorrow }.ifEmpty { upcoming.take(3) }
                item(key = "next-label") {
                    SectionHeader(if (next.isNotEmpty() && next.first().day == tomorrow) "Morgen" else "Demnächst", action = "Meine Woche",
                        onAction = { nav.push(Route.Week) })
                }
                item(key = "next") {
                    SectionCard {
                        if (next.isEmpty()) RowItem("Keine weiteren Einsätze in den nächsten 7 Tagen", icon = Icons.Filled.CalendarMonth, iconTint = tones.textSecondary)
                        next.forEachIndexed { index, job ->
                            if (index > 0) RowDivider(16.dp)
                            WeekRow(job, today) { nav.open(de.estrichone.core.NotificationRoute.Bauakte(job.bauakteId)) }
                        }
                        RowDivider(16.dp)
                        RowItem("Wer fährt wohin", icon = Icons.Outlined.DirectionsCar, showChevron = true, onClick = { nav.push(Route.Dispatch) })
                    }
                }
            }
            // Zuletzt geöffnet (wie iOS), ohne die Baustellen von heute; im Demomodus mit Beispielen.
            val shownToday = todays.map { it.bauakteId }.toSet()
            val recentIds = model.prefs.recentBauakten(session.user.id).ifEmpty {
                if (session.isDemo) listOf(de.estrichone.core.DemoData.HALLE, de.estrichone.core.DemoData.ISARPARK) else emptyList()
            }
            val recent = recentIds.filter { it !in shownToday }.mapNotNull { session.service.bauakte(it) }.take(3)
            if (recent.isNotEmpty()) {
                item(key = "recent-label") { SectionHeader("Zuletzt geöffnet") }
                item(key = "recent") {
                    SectionCard {
                        recent.forEachIndexed { index, b ->
                            if (index > 0) RowDivider(16.dp)
                            RowItem(b.title, subtitle = listOfNotNull(b.numberText, b.addressLine).joinToString(", "), showChevron = true,
                                onClick = { nav.open(de.estrichone.core.NotificationRoute.Bauakte(b.id)) })
                        }
                    }
                }
            }
        }
        }
    }
}

/** „⋮“ oben rechts auf Heute: Synchronisation und, wenn verknüpft, Microsoft Teams. */
@Composable
private fun HomeOverflow() {
    val nav = LocalNav.current
    val model = LocalModel.current
    val context = LocalContext.current
    val teams by LocalSession.current.teams.collectAsState()
    var open by remember { mutableStateOf(false) }
    Box {
        IconButton(onClick = { open = true }) { Icon(Icons.Filled.MoreVert, "Weitere Aktionen") }
        DropdownMenu(expanded = open, onDismissRequest = { open = false }) {
            DropdownMenuItem(text = { Text("Synchronisation") }, leadingIcon = { Icon(Icons.Filled.CloudDone, null) },
                onClick = { open = false; nav.push(Route.Sync) })
            DropdownMenuItem(text = { Text("Meine Woche") }, leadingIcon = { Icon(Icons.Filled.CalendarMonth, null) },
                onClick = { open = false; nav.push(Route.Week) })
            teams?.takeIf { it.available }?.homeTargets?.let { target ->
                DropdownMenuItem(text = { Text("In Teams öffnen") }, leadingIcon = { Icon(Icons.Filled.Groups, null) }, onClick = {
                    open = false
                    if (!Links.teams(context, target)) model.message("Microsoft Teams lässt sich auf diesem Gerät nicht öffnen.")
                })
            }
        }
    }
}

/** Einsatz-Zeile der Woche: Tag, Zeit, Bauvorhaben, Tätigkeit. */
@Composable
fun WeekRow(job: PlannedJob, today: LocalDate, onClick: () -> Unit) {
    val day = de.estrichone.core.ApiDate.day(job.day)
    Row(Modifier.fillMaxWidth().heightIn(min = 56.dp).clickable(onClick = onClick).padding(horizontal = 16.dp, vertical = 12.dp),
        verticalAlignment = Alignment.CenterVertically) {
        Column(Modifier.width(72.dp)) {
            Text(day?.let { Dates.relativeDay(it, today) }?.substringBefore(',') ?: job.day, style = MaterialTheme.typography.labelLarge,
                color = MaterialTheme.colorScheme.primary)
            Text(Dates.clock(job.startTime) ?: (day?.let { Dates.dayMonth(it) } ?: ""), style = MaterialTheme.typography.bodySmall, color = tones.textSecondary)
        }
        Column(Modifier.weight(1f)) {
            Text(job.bauakteTitle, style = MaterialTheme.typography.bodyLarge, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Secondary(listOfNotNull(job.activity, job.crewName).joinToString(", "), maxLines = 1)
        }
        Icon(Icons.AutoMirrored.Filled.KeyboardArrowRight, null, tint = tones.textTertiary)
    }
}

/** Weiterer Einsatz des Tages als kompakte Zeile: Uhrzeit, Baustelle, Leistung und Adresse, rechts Route. */
@Composable
fun CompactJobRow(job: PlannedJob, session: Session, phase: TodayGlance.Phase) {
    val nav = LocalNav.current
    val context = LocalContext.current
    val bauakte by session.local(job.bauakteId) { session.service.bauakte(job.bauakteId) }
    Row(Modifier.fillMaxWidth().heightIn(min = 64.dp).clickable(onClickLabel = "Bauakte öffnen") { nav.open(de.estrichone.core.NotificationRoute.Bauakte(job.bauakteId)) }
        .padding(start = 16.dp, end = 8.dp, top = 12.dp, bottom = 12.dp), verticalAlignment = Alignment.CenterVertically) {
        Column(Modifier.width(64.dp)) {
            Text(Dates.clock(job.startTime) ?: "ganztags", style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.SemiBold,
                color = if (phase == TodayGlance.Phase.Finished) tones.textSecondary else MaterialTheme.colorScheme.onSurface)
            phase.text?.let { Text(it, style = MaterialTheme.typography.labelSmall, color = if (phase == TodayGlance.Phase.Running) MaterialTheme.colorScheme.primary else tones.textSecondary) }
        }
        Column(Modifier.weight(1f)) {
            Text(job.bauakteTitle, style = MaterialTheme.typography.bodyLarge, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Secondary(listOfNotNull(job.activity, job.bauakteAddress).joinToString(", "), maxLines = 1)
        }
        job.bauakteAddress?.let { address ->
            CircleAction(Icons.Filled.Directions, "Route zu ${job.bauakteTitle}", { Links.route(context, address, bauakte?.latitude, bauakte?.longitude) })
        }
    }
}

/**
 * Hauptkarte des laufenden bzw. nächsten Einsatzes: Uhrzeit mit „Jetzt“/„in 2 Std.“, Bauvorhaben mit Nummer und Status,
 * Adresse, Leistung (Tätigkeit und Kennwerte laut Basisdaten), Kolonne, Fahrzeuge, Zugangshinweise, Kennzahlen und darunter
 * die Schnellaktionen Route, Anrufen, Chat und Bauakte. Foto, Tagebuch und Mangel stehen im Menü „⋮“ der Karte.
 * Mit [expanded] = false als kompakte Zeile.
 */
@OptIn(ExperimentalLayoutApi::class)
@Composable
fun TodayJobCard(job: PlannedJob, session: Session, phase: TodayGlance.Phase = TodayGlance.Phase.Unknown, expanded: Boolean = true, onExpand: () -> Unit = {}) {
    if (!expanded) {
        SectionCard { CompactJobRow(job, session, phase) }
        return
    }
    val nav = LocalNav.current
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val bauakte by session.local(job.bauakteId) { session.service.bauakte(job.bauakteId) }
    val glance by session.local(job.bauakteId) {
        val service = session.service
        SiteGlance.of(service.tasks(job.bauakteId), service.rooms(job.bauakteId), service.inspections(job.bauakteId),
            service.diary(job.bauakteId), LocalDate.now(Dates.zone))
    }
    val modules by session.modules.collectAsState()
    val chat by session.chat.collectAsState()
    val phone = bauakte?.siteContactPhone?.takeIf { it.isNotBlank() }
    val facts = remember(bauakte) { bauakte?.let { session.keyFacts(it) }.orEmpty() }
    val open: () -> Unit = { nav.open(de.estrichone.core.NotificationRoute.Bauakte(job.bauakteId)) }
    val running = phase == TodayGlance.Phase.Running
    val border = if (running) Modifier.border(1.dp, MaterialTheme.colorScheme.primary.copy(alpha = 0.5f), RoundedCornerShape(16.dp)) else Modifier
    SectionCard(border, padding = androidx.compose.foundation.layout.PaddingValues(start = 16.dp, end = 8.dp, top = 8.dp, bottom = 8.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(job.time, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
            PhasePill(phase, Modifier.padding(start = 8.dp))
            Spacer(Modifier.weight(1f))
            var menu by remember { mutableStateOf(false) }
            Box {
                IconButton(onClick = { menu = true }) { Icon(Icons.Filled.MoreVert, "Weitere Aktionen") }
                DropdownMenu(expanded = menu, onDismissRequest = { menu = false }) {
                    DropdownMenuItem(text = { Text("Foto aufnehmen") }, leadingIcon = { Icon(Icons.Filled.PhotoCamera, null) },
                        onClick = { menu = false; nav.captureIn(job.bauakteId, SiteTask.Photo) })
                    if (modules.isOn(AppModules.DIARY)) {
                        DropdownMenuItem(text = { Text("Bautagebuch") }, leadingIcon = { Icon(Icons.AutoMirrored.Outlined.MenuBook, null) },
                            onClick = { menu = false; nav.captureIn(job.bauakteId, SiteTask.Diary) })
                    }
                    if (modules.isOn(AppModules.DEFECTS)) {
                        DropdownMenuItem(text = { Text("Mangel melden") }, leadingIcon = { Icon(Icons.Outlined.ReportProblem, null) },
                            onClick = { menu = false; nav.captureIn(job.bauakteId, SiteTask.Defect) })
                    }
                }
            }
        }
        Column(Modifier.padding(end = 8.dp).clip(RoundedCornerShape(8.dp)).clickable(onClickLabel = "Bauakte öffnen", onClick = open)) {
            Text(job.bauakteTitle, style = MaterialTheme.typography.titleLarge)
            Row(Modifier.padding(top = 4.dp), verticalAlignment = Alignment.CenterVertically) {
                Text(job.bauakteNumber ?: "Neu", style = MaterialTheme.typography.bodyMedium, color = tones.textSecondary)
                bauakte?.let { StatusChip(it.status, Modifier.padding(start = 8.dp)) }
            }
            job.bauakteAddress?.let { Secondary(it, Modifier.padding(top = 4.dp)) }
        }
        HorizontalDivider(Modifier.padding(top = 12.dp, bottom = 4.dp, end = 8.dp), color = tones.separator)
        JobLine(Icons.Filled.Handyman, job.activity, bold = true, detail = facts.joinToString(", ") { it.second }.ifEmpty { null })
        job.crewName?.let { crew ->
            JobLine(Icons.Filled.Groups, crew, dot = parseColor(job.crewColor) ?: tones.warning,
                detail = job.people.joinToString(", ") { it.name }.ifEmpty { null })
        }
        if (job.resources.isNotEmpty()) JobLine(Icons.Filled.LocalShipping, job.resources.joinToString(", ") { it.name })
        val contact = bauakte?.siteContactName
        if (contact != null || phone != null) JobLine(Icons.Outlined.AccountCircle, listOfNotNull(contact?.let { "Vor Ort: $it" } ?: "Ansprechpartner vor Ort", phone).joinToString(", "))
        job.notes?.let { JobLine(Icons.Filled.Info, it, detailLabel = "Zugang und Hinweise") }
        job.warnings.forEach { JobLine(Icons.Filled.Warning, it, color = tones.warning) }
        if (!glance.isEmpty) GlanceChips(glance, Modifier.padding(top = 8.dp, end = 8.dp))
        HorizontalDivider(Modifier.padding(top = 12.dp, bottom = 4.dp, end = 8.dp), color = tones.separator)
        QuickActionRow(Modifier.padding(end = 8.dp)) {
            QuickAction(Icons.Filled.Directions, "Route", Modifier.weight(1f), enabled = job.bauakteAddress != null) {
                job.bauakteAddress?.let { Links.route(context, it, bauakte?.latitude, bauakte?.longitude) }
            }
            QuickAction(Icons.Filled.Phone, "Anrufen", Modifier.weight(1f), enabled = phone != null) { phone?.let { Links.call(context, it) } }
            QuickAction(Icons.Outlined.ChatBubbleOutline, "Chat", Modifier.weight(1f), enabled = chat?.isAvailable == true && chat?.readOnly != true) {
                scope.launch {
                    val group = runCatching { withContext(Dispatchers.IO) { session.api.chatBauakteGroup(job.bauakteId) } }.getOrNull()
                    nav.openChat(group?.conversationId?.takeIf { group.member })
                }
            }
            QuickAction(Icons.Outlined.Folder, "Bauakte", Modifier.weight(1f), primary = true, onClick = open)
        }
    }
}

/** „Jetzt“ (Markenfarbe), „in 2 Std.“ oder „Vorbei“ (grau) neben der Uhrzeit. */
@Composable
private fun PhasePill(phase: TodayGlance.Phase, modifier: Modifier = Modifier) {
    val text = phase.text ?: return
    val running = phase == TodayGlance.Phase.Running
    Pill(text, if (running) MaterialTheme.colorScheme.primary else tones.textSecondary, if (running) tones.infoSoft else tones.fill, modifier = modifier)
}

/** Kennzahlen der Baustelle als kleine Kapseln: offene Mängel (rot), Aufgaben, Belegreife, Wetter laut Tagebuch. */
@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun GlanceChips(glance: SiteGlance, modifier: Modifier = Modifier) {
    FlowRow(modifier, horizontalArrangement = Arrangement.spacedBy(6.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
        glance.defectsText?.let { Pill(it, tones.danger, tones.dangerSoft, Icons.Filled.Warning) }
        glance.tasksText?.let { Pill(it, tones.textSecondary, tones.fill, Icons.Outlined.Checklist) }
        glance.readiness?.let { readiness ->
            when {
                readiness.isReady -> Pill(readiness.text, tones.success, tones.successSoft, Icons.Filled.CheckCircle)
                readiness is SiteGlance.Readiness.NotReady -> Pill(readiness.text, tones.warning, tones.warningSoft, Icons.Filled.HourglassTop)
                else -> Pill(readiness.text, tones.textSecondary, tones.fill)
            }
        }
        glance.weatherText?.let { Pill(it, tones.textSecondary, tones.fill, Icons.Filled.WbSunny) }
    }
}

/** Angabe der Einsatzkarte: Symbol, Text, optional Unterzeile bzw. farbiger Punkt (Kolonne). */
@Composable
private fun JobLine(
    icon: androidx.compose.ui.graphics.vector.ImageVector, text: String, bold: Boolean = false, color: Color? = null,
    detail: String? = null, dot: Color? = null, detailLabel: String? = null,
) {
    Row(Modifier.fillMaxWidth().padding(top = 8.dp, end = 8.dp), verticalAlignment = Alignment.Top) {
        Icon(icon, detailLabel, tint = color ?: tones.textSecondary, modifier = Modifier.size(20.dp))
        Spacer(Modifier.width(16.dp))
        Column(Modifier.weight(1f)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                if (dot != null) {
                    Box(Modifier.size(8.dp).clip(CircleShape).background(dot))
                    Spacer(Modifier.width(6.dp))
                }
                Text(text, style = MaterialTheme.typography.bodyMedium, fontWeight = if (bold) FontWeight.SemiBold else FontWeight.Normal,
                    color = color ?: MaterialTheme.colorScheme.onSurface)
            }
            if (detail != null) Text(detail, style = MaterialTheme.typography.bodySmall, color = tones.textSecondary)
        }
    }
}

fun parseColor(hex: String?): Color? = hex?.removePrefix("#")?.takeIf { it.length == 6 }?.toLongOrNull(16)?.let { Color(0xFF000000 or it) }

// Systemhinweise

/**
 * Mitteilungen der Verwaltung bzw. des Betreibers (Störung, Wartung, Neuigkeiten) als eine kompakte Zeile: „3 Mitteilungen“
 * bzw. der Titel der einen. Störungen rot hervorgehoben. Antippen klappt die Liste auf; ausblendbare Mitteilungen lassen sich
 * dort schließen, Details öffnen ein Blatt.
 */
@Composable
fun NoticeSummary(session: Session) {
    val model = LocalModel.current
    val notices by session.notices.collectAsState()
    var now by remember { mutableStateOf(Instant.now()) }
    LaunchedEffect(Unit) { while (Live.polling) { delay(30_000); now = Instant.now() } }
    var dismissed by remember { mutableStateOf(model.prefs.dismissedNotices(session.user.id)) }
    var expanded by remember { mutableStateOf(false) }
    var details by remember { mutableStateOf<NoticeItem?>(null) }
    val items = NoticePresentation.items(notices, now)
    val summary = TodayOverview.noticeSummary(items, dismissed) ?: return
    val (shown, _) = NoticePresentation.selection(items, dismissed, expanded = true)
    val color = when (summary.tone) {
        NoticeItem.Tone.Error -> tones.danger
        NoticeItem.Tone.Warning -> tones.warning
        NoticeItem.Tone.Information -> tones.info
    }
    Column(Modifier.padding(top = Space.s), verticalArrangement = Arrangement.spacedBy(Space.s)) {
        Row(
            Modifier.fillMaxWidth().clip(RoundedCornerShape(12.dp)).background(if (summary.critical) tones.dangerSoft else tones.card)
                .clickable(onClickLabel = if (expanded) "Mitteilungen einklappen" else "Mitteilungen anzeigen") {
                    expanded = !expanded
                }
                .heightIn(min = 56.dp).padding(start = 16.dp, end = 8.dp, top = 8.dp, bottom = 8.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Icon(if (summary.critical) Icons.Filled.Report else if (summary.tone == NoticeItem.Tone.Warning) Icons.Filled.Warning else Icons.Filled.Info,
                null, tint = color, modifier = Modifier.size(20.dp))
            Spacer(Modifier.width(16.dp))
            Column(Modifier.weight(1f)) {
                Text(summary.title, style = MaterialTheme.typography.titleSmall, color = if (summary.critical) color else MaterialTheme.colorScheme.onSurface,
                    maxLines = 1, overflow = TextOverflow.Ellipsis)
                if (summary.subtitle.isNotEmpty()) {
                    Text(summary.subtitle, style = MaterialTheme.typography.bodySmall, color = tones.textSecondary, maxLines = 1, overflow = TextOverflow.Ellipsis)
                }
            }
            Icon(if (expanded) Icons.Filled.ExpandLess else Icons.Filled.ExpandMore, null, tint = tones.textSecondary, modifier = Modifier.padding(8.dp))
        }
        if (expanded) {
            shown.forEach { item ->
                NoticeCard(item, onDetails = { details = item }, onDismiss = if (item.isDismissible) {
                    {
                        dismissed = dismissed + item.id
                        model.prefs.setDismissedNotices(session.user.id, dismissed)
                    }
                } else null)
            }
        }
    }
    details?.let { NoticeDetails(it) { details = null } }
}

@Composable
private fun noticeColors(item: NoticeItem): Pair<Color, Color> = when (item.tone) {
    NoticeItem.Tone.Error -> tones.danger to tones.dangerSoft
    NoticeItem.Tone.Warning -> tones.warning to tones.warningSoft
    NoticeItem.Tone.Information -> tones.info to tones.infoSoft
}

private fun noticeIcon(item: NoticeItem) = when (item.kind) {
    NoticeItem.Kind.MaintenanceRunning -> Icons.Filled.Build
    NoticeItem.Kind.Critical -> Icons.Filled.Report
    NoticeItem.Kind.MaintenanceSoon, NoticeItem.Kind.Maintenance -> Icons.Filled.CalendarMonth
    NoticeItem.Kind.Warning -> Icons.Filled.Warning
    NoticeItem.Kind.Info -> Icons.Filled.Info
}

@Composable
fun NoticeCard(item: NoticeItem, onDetails: () -> Unit, onDismiss: (() -> Unit)?) {
    val (color, soft) = noticeColors(item)
    // Ruhig: Karte ohne Farbstreifen und Rahmen; nur Symbol und Countdown tragen die Farbe.
    Row(
        Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp))
            .background(if (item.tone == NoticeItem.Tone.Error) soft else tones.card),
    ) {
        Row(Modifier.weight(1f).padding(start = 12.dp, top = 14.dp, bottom = 10.dp, end = 4.dp)) {
            IconTile(noticeIcon(item), color, soft, 38.dp)
            Spacer(Modifier.width(12.dp))
            Column(Modifier.weight(1f)) {
                Text(item.title, style = MaterialTheme.typography.titleMedium)
                val countdown = item.relative.ifEmpty { item.remaining }
                if (countdown.isNotEmpty()) Pill(countdown, color, soft, modifier = Modifier.padding(top = 4.dp))
                if (item.period.isNotEmpty()) {
                    Row(Modifier.padding(top = 4.dp), verticalAlignment = Alignment.CenterVertically) {
                        Icon(Icons.Filled.CalendarMonth, null, tint = tones.textSecondary, modifier = Modifier.size(15.dp))
                        Spacer(Modifier.width(4.dp))
                        Text(item.period, style = MaterialTheme.typography.bodyMedium)
                    }
                }
                Text(item.text, style = MaterialTheme.typography.bodyMedium, color = tones.textSecondary, maxLines = 3, overflow = TextOverflow.Ellipsis,
                    modifier = Modifier.padding(top = 4.dp))
                Row(Modifier.padding(top = 6.dp).clip(RoundedCornerShape(8.dp)).clickable(onClick = onDetails).padding(vertical = 4.dp),
                    verticalAlignment = Alignment.CenterVertically) {
                    Text("Details", color = MaterialTheme.colorScheme.primary, style = MaterialTheme.typography.labelLarge)
                    Icon(Icons.AutoMirrored.Filled.KeyboardArrowRight, null, tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(18.dp))
                }
            }
            if (onDismiss != null) {
                IconButton(onClick = onDismiss) { Icon(Icons.Filled.Close, "Ausblenden", tint = tones.textSecondary) }
            }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun NoticeDetails(item: NoticeItem, onDismiss: () -> Unit) {
    val (color, soft) = noticeColors(item)
    ModalBottomSheet(onDismissRequest = onDismiss, containerColor = tones.canvas) {
        Column(Modifier.padding(horizontal = 20.dp).padding(bottom = 32.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                IconTile(noticeIcon(item), color, soft, 44.dp)
                Spacer(Modifier.width(12.dp))
                Column {
                    Text(item.label, color = color, style = MaterialTheme.typography.labelLarge)
                    Text(item.title, style = MaterialTheme.typography.titleLarge)
                }
            }
            SectionCard {
                if (item.period.isNotEmpty()) RowItem("Zeitraum", icon = Icons.Filled.CalendarMonth, subtitle = item.period)
                val countdown = listOf(item.relative, item.remaining).filter { it.isNotEmpty() }.joinToString(", ")
                if (countdown.isNotEmpty()) { RowDivider(); RowItem("Stand", icon = Icons.Filled.Info, subtitle = countdown) }
                if (item.source.isNotEmpty()) { RowDivider(); RowItem("Absender", icon = Icons.Outlined.AccountCircle, subtitle = item.source) }
            }
            Text(item.text, style = MaterialTheme.typography.bodyLarge)
            if (item.kind == NoticeItem.Kind.Maintenance || item.kind == NoticeItem.Kind.MaintenanceSoon) {
                Secondary("Während der Wartung können Sie offline weiterarbeiten. Erfasstes bleibt auf dem Gerät und wird danach übertragen.")
            }
        }
    }
}

/** Ausfall von Backup- bzw. Archivserver – nur, wenn der Server einen Hinweis liefert (docs/backup.md). */
@Composable
fun HealthStrip(session: Session) {
    val banners by session.health.collectAsState()
    if (banners.isEmpty()) return
    Column(Modifier.fillMaxWidth().background(if (banners.any { it.critical }) tones.danger else tones.warningSoft).windowInsetsPadding(WindowInsets.statusBars)) {
        banners.forEach { banner ->
            Row(Modifier.fillMaxWidth().background(if (banner.critical) tones.danger else tones.warningSoft).padding(horizontal = 16.dp, vertical = 8.dp),
                verticalAlignment = Alignment.CenterVertically) {
                Icon(if (banner.critical) Icons.Filled.ErrorOutline else Icons.Filled.Warning, null,
                    tint = if (banner.critical) Color.White else tones.warning, modifier = Modifier.size(18.dp))
                Spacer(Modifier.width(8.dp))
                Text(banner.text, style = MaterialTheme.typography.bodySmall, color = if (banner.critical) Color.White else MaterialTheme.colorScheme.onSurface)
            }
        }
    }
}

