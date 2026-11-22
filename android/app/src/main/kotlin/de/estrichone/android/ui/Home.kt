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
    val firstName = session.user.displayName.substringBefore(' ')
    val modules by session.modules.collectAsState()
    // Uhr für „Jetzt“/„in 2 Std.“ (minütlich); vom Nutzer aufgeklappter Einsatz.
    var clock by remember { mutableStateOf(Live.clock()) }
    LaunchedEffect(Unit) { while (Live.polling) { delay(60_000); clock = Live.clock() } }
    var expanded by remember { mutableStateOf<String?>(null) }

    ScreenScaffold("Heute", root = true, actions = { HomeOverflow(); NotificationAction() }) { padding ->
        // Nach unten ziehen: abgleichen und Plan, Hinweise und Zähler neu laden.
        PullToRefreshBox(isRefreshing = status.running, onRefresh = { scope.launch { session.synchronize() } }, modifier = Modifier.fillMaxSize()) {
        LazyColumn(contentPadding = padding.content(), modifier = Modifier.readable()) {
            item(key = "head") {
                // Datum als ruhige Zeile, rechts dezent der Abgleich; Hinweise nur bei Bedarf. Der Demomodus steht oben links („Demo“).
                Row(Modifier.fillMaxWidth().padding(start = 4.dp, bottom = Space.s), verticalAlignment = Alignment.CenterVertically) {
                    Secondary(Dates.long(today), modifier = Modifier.weight(1f))
                    Row(Modifier.clip(RoundedCornerShape(8.dp)).clickable { nav.push(Route.Sync) }.heightIn(min = 32.dp).padding(horizontal = 4.dp),
                        verticalAlignment = Alignment.CenterVertically) {
                        val problem = status.summary.hasProblems
                        Icon(if (problem) Icons.Filled.ErrorOutline else Icons.Filled.CloudDone, null,
                            tint = if (problem) tones.danger else tones.textSecondary, modifier = Modifier.size(16.dp))
                        Spacer(Modifier.width(4.dp))
                        Text("Abgleich: ${status.shortText}", style = MaterialTheme.typography.bodySmall,
                            color = if (problem) tones.danger else tones.textSecondary, maxLines = 1)
                    }
                }
                OfflineBanner(session)
                NoticeCards(session)
                TeamsRolloutCard()
            }
            // Systemeinstellungen › Module: ohne Disposition kein Einsatz, ohne Aufgaben und Mängel keine Aufgabenzeile.
            val planning = modules.isOn(AppModules.PLANNING)
            val todays = plan?.assignments?.filter { it.day == today.toString() }.orEmpty().sortedBy { it.startTime ?: "99" }
            if (planning) item { SectionHeader(if (todays.size > 1) "Meine Einsätze (${todays.size})" else "Mein Einsatz") }
            // Laufender bzw. nächster Einsatz aufgeklappt, die übrigen kompakt (Tipp klappt auf).
            val phases = todays.map { TodayGlance.phase(it.startTime, it.endTime, clock) }
            val focus = expanded?.takeIf { id -> todays.any { it.id == id } } ?: TodayGlance.focusIndex(phases)?.let { todays[it].id }
            when {
                !planning -> Unit
                plan == null -> item { SectionCard { RowItem("Einsätze werden geladen …") } }
                todays.isEmpty() -> item { SectionCard { RowItem("Heute ist nichts für Sie eingeplant.") } }
                else -> items(todays, key = { it.id }) { job ->
                    val index = todays.indexOf(job)
                    TodayJobCard(job, session, phases[index], expanded = todays.size == 1 || job.id == focus, onExpand = { expanded = job.id })
                    Spacer(Modifier.height(Space.m))
                }
            }
            val showsTasks = modules.anyOn(AppModules.TASKS, AppModules.DEFECTS)
            if (showsTasks || planning) {
                // Überblick wie auf iOS: drei normale Listenzeilen mit Zahl rechts.
                item { SectionHeader("Überblick") }
                item {
                    SectionCard {
                        val defects = tasks.count { it.kind == WorkItemKind.Defect }
                        if (showsTasks) {
                            RowItem("Meine Aufgaben", icon = Icons.Outlined.Checklist, value = "${tasks.size}", showChevron = true,
                                subtitle = when (defects) { 0 -> null; 1 -> "davon 1 Mangel"; else -> "davon $defects Mängel" },
                                onClick = { nav.push(Route.Tasks) })
                        }
                        if (planning) {
                            if (showsTasks) RowDivider()
                            RowItem("Wer fährt wohin", icon = Icons.Outlined.DirectionsCar, showChevron = true, onClick = { nav.push(Route.Dispatch) })
                            RowDivider()
                            RowItem("Meine Woche", icon = Icons.Filled.CalendarMonth, showChevron = true,
                                value = plan?.assignments?.count { it.day >= today.toString() }?.toString(), onClick = { nav.push(Route.Week) })
                        }
                    }
                }
            }
            val upcoming = plan?.assignments?.filter { it.day > today.toString() }.orEmpty()
            if (upcoming.isNotEmpty() && planning) {
                item { SectionHeader("Diese Woche") }
                item {
                    SectionCard {
                        upcoming.take(3).forEachIndexed { index, job ->
                            if (index > 0) RowDivider(16.dp)
                            WeekRow(job, today) { nav.push(Route.Bauakte(job.bauakteId)) }
                        }
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
                item { SectionHeader("Zuletzt geöffnet") }
                item {
                    SectionCard {
                        recent.forEachIndexed { index, b ->
                            if (index > 0) RowDivider(16.dp)
                            RowItem(b.title, subtitle = listOfNotNull(b.numberText, b.addressLine).joinToString(", "), showChevron = true,
                                onClick = { nav.push(Route.Bauakte(b.id)) })
                        }
                    }
                }
            }
        }
        }
    }
}

/** „⋮“ oben rechts auf Heute: Abgleich (mit Stand), Chat, Microsoft Teams – eine sichtbare Aktion daneben (Benachrichtigungen). */
@Composable
private fun HomeOverflow() {
    val session = LocalSession.current
    val nav = LocalNav.current
    val model = LocalModel.current
    val context = LocalContext.current
    val status by session.status.collectAsState()
    val teams by session.teams.collectAsState()
    var open by remember { mutableStateOf(false) }
    Box {
        IconButton(onClick = { open = true }) {
            Icon(if (status.summary.hasProblems) Icons.Filled.ErrorOutline else Icons.Filled.MoreVert, "Weitere Aktionen",
                tint = if (status.summary.hasProblems) tones.danger else MaterialTheme.colorScheme.onSurface)
        }
        DropdownMenu(expanded = open, onDismissRequest = { open = false }) {
            DropdownMenuItem(text = { Text("Abgleich: ${status.shortText}") }, leadingIcon = { Icon(Icons.Filled.CloudDone, null) },
                onClick = { open = false; nav.push(Route.Sync) })
            // Chat steht jetzt unten in der Navigation.
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
    Row(Modifier.fillMaxWidth().clickable(onClick = onClick).padding(horizontal = 16.dp, vertical = 12.dp), verticalAlignment = Alignment.CenterVertically) {
        Column(Modifier.width(74.dp)) {
            Text(day?.let { Dates.relativeDay(it, today) }?.substringBefore(',') ?: job.day, style = MaterialTheme.typography.labelLarge,
                color = MaterialTheme.colorScheme.primary)
            Secondary(day?.let { Dates.dayMonth(it) } ?: "")
        }
        Column(Modifier.weight(1f)) {
            Text(job.bauakteTitle, style = MaterialTheme.typography.bodyLarge, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Secondary("${Dates.clock(job.startTime) ?: "ganztags"}, ${job.activity}", maxLines = 1)
        }
        Icon(Icons.AutoMirrored.Filled.KeyboardArrowRight, null, tint = tones.textTertiary)
    }
}

/**
 * Karte eines Einsatzes wie TodayJob.swift: Zeit mit „Jetzt“/„in 2 Std.“, Baustelle mit Status, Kennzahlen (offene Mängel und
 * Aufgaben, Belegreife, Wetter laut Tagebuch), Adresse, Ansprechpartner, Leistung, Kolonne, Fahrzeuge, Hinweise und vier große
 * Handgriffe (Route, Anrufen, Foto, Tagebuch). Zugeklappt: eine kompakte Zeile mit Route und Anruf; Tipp klappt auf.
 */
@OptIn(ExperimentalLayoutApi::class)
@Composable
fun TodayJobCard(job: PlannedJob, session: Session, phase: TodayGlance.Phase = TodayGlance.Phase.Unknown, expanded: Boolean = true, onExpand: () -> Unit = {}) {
    val nav = LocalNav.current
    val context = LocalContext.current
    val bauakte by session.local(job.bauakteId) { session.service.bauakte(job.bauakteId) }
    val glance by session.local(job.bauakteId) {
        val service = session.service
        SiteGlance.of(service.tasks(job.bauakteId), service.rooms(job.bauakteId), service.inspections(job.bauakteId),
            service.diary(job.bauakteId), LocalDate.now(Dates.zone))
    }
    val modules by session.modules.collectAsState()
    val phone = bauakte?.siteContactPhone?.takeIf { it.isNotBlank() }
    val route: (() -> Unit)? = job.bauakteAddress?.let { address -> { Links.route(context, address, bauakte?.latitude, bauakte?.longitude); Unit } }
    val call: (() -> Unit)? = phone?.let { number -> { Links.call(context, number); Unit } }
    val open: () -> Unit = { nav.open(de.estrichone.core.NotificationRoute.Bauakte(job.bauakteId)) }
    val running = phase == TodayGlance.Phase.Running
    val border = if (running) Modifier.border(1.5.dp, MaterialTheme.colorScheme.primary.copy(alpha = 0.55f), RoundedCornerShape(16.dp)) else Modifier
    if (!expanded) {
        SectionCard(border.clickable(onClickLabel = "Alle Angaben zeigen", onClick = onExpand), padding = androidx.compose.foundation.layout.PaddingValues(16.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text(job.time, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold)
                        PhasePill(phase, Modifier.padding(start = 8.dp))
                    }
                    Text(job.bauakteTitle, style = MaterialTheme.typography.titleMedium)
                    Secondary(listOfNotNull(job.activity, job.crewName).joinToString(", "))
                    job.bauakteAddress?.let { Secondary(it) }
                    if (!glance.isEmpty) GlanceChips(glance, Modifier.padding(top = 4.dp))
                }
                Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    route?.let { CircleAction(Icons.Filled.Directions, "Route", it) }
                    call?.let { CircleAction(Icons.Filled.Phone, "Anrufen", it) }
                }
            }
        }
        return
    }
    SectionCard(border, padding = androidx.compose.foundation.layout.PaddingValues(16.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(job.time, style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.Bold)
            PhasePill(phase, Modifier.padding(start = 10.dp))
            Spacer(Modifier.weight(1f))
            var menu by remember { mutableStateOf(false) }
            Box {
                IconButton(onClick = { menu = true }) { Icon(Icons.Filled.MoreVert, "Weitere Aktionen") }
                DropdownMenu(expanded = menu, onDismissRequest = { menu = false }) {
                    if (modules.isOn(AppModules.DEFECTS)) {
                        DropdownMenuItem(text = { Text("Mangel melden") }, leadingIcon = { Icon(Icons.Outlined.ReportProblem, null) },
                            onClick = { menu = false; nav.captureIn(job.bauakteId, SiteTask.Defect) })
                    }
                    DropdownMenuItem(text = { Text("Bauakte öffnen") }, leadingIcon = { Icon(Icons.Outlined.Folder, null) },
                        onClick = { menu = false; open() })
                }
            }
        }
        Row(Modifier.fillMaxWidth().clickable(onClick = open).padding(vertical = 4.dp), verticalAlignment = Alignment.CenterVertically) {
            Column(Modifier.weight(1f)) {
                Text(job.bauakteTitle, style = MaterialTheme.typography.titleLarge)
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Secondary(job.bauakteNumber ?: "Neu")
                    bauakte?.let { StatusLabel(it.status, modifier = Modifier.padding(start = 10.dp)) }
                }
            }
            Icon(Icons.AutoMirrored.Filled.KeyboardArrowRight, null, tint = tones.textTertiary)
        }
        if (!glance.isEmpty) GlanceChips(glance, Modifier.padding(top = 6.dp).clickable(onClick = open))
        HorizontalDivider(Modifier.padding(vertical = 10.dp), color = tones.separator)
        job.bauakteAddress?.let { JobLine(Icons.Outlined.Place, it) }
        val contact = bauakte?.siteContactName
        if (contact != null || phone != null) {
            JobLine(Icons.Outlined.AccountCircle, listOfNotNull(contact?.let { "Vor Ort: $it" } ?: "Ansprechpartner vor Ort", phone).joinToString(", "))
        }
        JobLine(Icons.Filled.Handyman, job.activity, bold = true)
        job.crewName?.let { crew ->
            Row(Modifier.padding(vertical = 6.dp), verticalAlignment = Alignment.Top) {
                Icon(Icons.Filled.Groups, null, tint = tones.textSecondary, modifier = Modifier.size(22.dp))
                Spacer(Modifier.width(14.dp))
                Column {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Box(Modifier.size(9.dp).clip(CircleShape).background(parseColor(job.crewColor) ?: tones.warning))
                        Spacer(Modifier.width(6.dp))
                        Text(crew, style = MaterialTheme.typography.bodyLarge, fontWeight = FontWeight.SemiBold)
                    }
                    if (job.people.isNotEmpty()) Secondary(job.people.joinToString(", ") { it.name })
                }
            }
        }
        if (job.resources.isNotEmpty()) JobLine(Icons.Filled.LocalShipping, job.resources.joinToString(", ") { it.name }, secondary = true)
        job.notes?.let { JobLine(Icons.Outlined.ChatBubbleOutline, it, secondary = true) }
        job.warnings.forEach { JobLine(Icons.Filled.Warning, it, color = tones.warning) }
        Spacer(Modifier.height(Space.m))
        // Vier gleich große Handgriffe (56 dp hoch, Trefferfläche über 48 dp), gut mit Handschuhen und einer Hand.
        // Foto und Tagebuch erfassen in der Bauakte: Zurück führt in die Bauakte, nicht nach „Heute“.
        Row(Modifier.height(IntrinsicSize.Min), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            ActionTile(Icons.Filled.Directions, "Route", enabled = route != null, modifier = Modifier.weight(1f)) { route?.invoke() }
            ActionTile(Icons.Filled.Phone, "Anrufen", enabled = call != null, modifier = Modifier.weight(1f)) { call?.invoke() }
            ActionTile(Icons.Filled.PhotoCamera, "Foto", primary = true, modifier = Modifier.weight(1f)) { nav.captureIn(job.bauakteId, SiteTask.Photo) }
            ActionTile(Icons.AutoMirrored.Outlined.MenuBook, "Tagebuch", enabled = modules.isOn(AppModules.DIARY), modifier = Modifier.weight(1f)) {
                nav.captureIn(job.bauakteId, SiteTask.Diary)
            }
        }
    }
}

/** Aktionskachel: Symbol über kurzer Beschriftung, 56 dp hoch. */
@Composable
private fun ActionTile(
    icon: androidx.compose.ui.graphics.vector.ImageVector, label: String, modifier: Modifier = Modifier, primary: Boolean = false,
    enabled: Boolean = true, onClick: () -> Unit,
) {
    val background = if (primary) MaterialTheme.colorScheme.primary else tones.fill
    val content = if (primary) MaterialTheme.colorScheme.onPrimary else MaterialTheme.colorScheme.primary
    Column(
        modifier.fillMaxHeight().heightIn(min = 56.dp).clip(RoundedCornerShape(12.dp)).background(background)
            .clickable(enabled = enabled, role = androidx.compose.ui.semantics.Role.Button, onClick = onClick)
            .alpha(if (enabled) 1f else 0.4f).padding(vertical = 6.dp, horizontal = 4.dp),
        horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.Center,
    ) {
        Icon(icon, null, tint = content, modifier = Modifier.size(22.dp))
        Spacer(Modifier.height(2.dp))
        Text(label, style = MaterialTheme.typography.labelMedium, fontWeight = FontWeight.SemiBold,
            color = if (primary) content else MaterialTheme.colorScheme.onSurface, maxLines = 1, overflow = TextOverflow.Ellipsis)
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

@Composable
private fun JobLine(
    icon: androidx.compose.ui.graphics.vector.ImageVector, text: String, bold: Boolean = false, secondary: Boolean = false,
    color: Color? = null, trailing: (@Composable () -> Unit)? = null,
) {
    Row(Modifier.fillMaxWidth().padding(vertical = 6.dp), verticalAlignment = Alignment.CenterVertically) {
        Icon(icon, null, tint = color ?: tones.textSecondary, modifier = Modifier.size(22.dp))
        Spacer(Modifier.width(14.dp))
        Text(text, style = MaterialTheme.typography.bodyLarge, fontWeight = if (bold) FontWeight.SemiBold else FontWeight.Normal,
            color = color ?: if (secondary) tones.textSecondary else MaterialTheme.colorScheme.onSurface, modifier = Modifier.weight(1f))
        trailing?.invoke()
    }
}

fun parseColor(hex: String?): Color? = hex?.removePrefix("#")?.takeIf { it.length == 6 }?.toLongOrNull(16)?.let { Color(0xFF000000 or it) }

// Systemhinweise

/** Hinweis-Karten des Betreibers wie auf iOS (SystemNoticeViews): Störung, Wartung mit Countdown, Neuigkeiten; ausblendbar. */
@Composable
fun NoticeCards(session: Session) {
    val model = LocalModel.current
    val notices by session.notices.collectAsState()
    var now by remember { mutableStateOf(Instant.now()) }
    LaunchedEffect(Unit) { while (Live.polling) { delay(30_000); now = Instant.now() } }
    var dismissed by remember { mutableStateOf(model.prefs.dismissedNotices(session.user.id)) }
    var expanded by remember { mutableStateOf(false) }
    var details by remember { mutableStateOf<NoticeItem?>(null) }
    val items = NoticePresentation.items(notices, now)
    // Bis zu drei Hinweise stehen offen da; bei mehr bleibt der wichtigste sichtbar, der Rest hinter „weitere Hinweise“.
    val (shown, hidden) = NoticePresentation.selection(items, dismissed, expanded || items.size <= 3)
    Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
        shown.forEach { item ->
            NoticeCard(item, onDetails = { details = item }, onDismiss = if (item.isDismissible) {
                {
                    dismissed = dismissed + item.id
                    model.prefs.setDismissedNotices(session.user.id, dismissed)
                }
            } else null)
        }
        if (hidden > 0) {
            TextButton(onClick = { expanded = true }) { Text(NoticePresentation.moreText(hidden)) }
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

