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
import de.estrichone.core.WorkItemKind
import de.estrichone.core.dueText
import de.estrichone.core.myPlan
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.withContext
import java.time.Instant
import java.time.LocalDate

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

@Composable
fun HomeScreen() {
    val session = LocalSession.current
    val nav = LocalNav.current
    val model = LocalModel.current
    val today = remember { LocalDate.now(Dates.zone) }
    val status by session.status.collectAsState()
    val plan by produceState<MyPlanning?>(null, status.outcome) { value = session.weekPlan(today) ?: value }
    val tasks by session.local { session.service.myTasks().filter { it.isOpen } }
    val firstName = session.user.displayName.substringBefore(' ')
    val modules by session.modules.collectAsState()

    ScreenScaffold("Heute", root = true, actions = { SyncAction(); InboxActions() }) { padding ->
        LazyColumn(contentPadding = padding.content(), modifier = Modifier.readable()) {
            item {
                Column(Modifier.padding(horizontal = 4.dp, vertical = 4.dp)) {
                    Text("${Dates.greeting()}, $firstName", style = MaterialTheme.typography.titleLarge.copy(fontSize = 22.sp))
                    Secondary(Dates.long(today))
                }
                Spacer(Modifier.height(Space.m))
                if (session.isDemo) {
                    Banner("Demomodus mit Beispieldaten. Es werden keine Daten gesendet.", Icons.Filled.PlayCircle, MaterialTheme.colorScheme.primary,
                        tones.infoSoft, Modifier.padding(bottom = Space.m), action = "Beenden", onAction = model::endDemo)
                }
                OfflineBanner(session)
                NoticeCards(session)
            }
            // Systemeinstellungen › Module: ohne Disposition kein Einsatz, ohne Aufgaben und Mängel keine offenen Punkte.
            val planning = modules.isOn(AppModules.PLANNING)
            if (planning) item { SectionHeader("Mein Einsatz") }
            val todays = plan?.assignments?.filter { it.day == today.toString() }.orEmpty()
            when {
                !planning -> Unit
                plan == null -> item { SectionCard { EmptyState(Icons.Filled.CalendarMonth, "Einsätze werden geladen", "Ohne Verbindung erscheint der zuletzt geladene Plan.") } }
                todays.isEmpty() -> item {
                    SectionCard { EmptyState(Icons.Filled.EventBusy, "Heute nichts eingeplant", "Ihre nächsten Einsätze stehen unten bei „Diese Woche“.") }
                }
                else -> items(todays, key = { it.id }) { job ->
                    TodayJobCard(job, session)
                    Spacer(Modifier.height(Space.m))
                }
            }
            if (tasks.isNotEmpty() && modules.anyOn(AppModules.TASKS, AppModules.DEFECTS)) {
                item { SectionHeader("Offene Punkte", action = "Alle (${tasks.size})", onAction = { nav.push(Route.Tasks) }) }
                item {
                    SectionCard {
                        tasks.take(3).forEachIndexed { index, task ->
                            if (index > 0) RowDivider()
                            val bauakte = task.bauakteId?.let { session.service.bauakte(it) }
                            RowItem(task.title, icon = if (task.kind == WorkItemKind.Defect) Icons.Filled.Warning else Icons.Filled.Handyman,
                                iconTint = if (task.kind == WorkItemKind.Defect) tones.warning else MaterialTheme.colorScheme.primary,
                                iconBackground = if (task.kind == WorkItemKind.Defect) tones.warningSoft else tones.infoSoft,
                                subtitle = listOfNotNull(task.number, task.dueDate?.let { dueText(it, today) }, bauakte?.title).joinToString(" · "),
                                showChevron = bauakte != null, onClick = bauakte?.let { b -> { nav.push(Route.Bauakte(b.id)) } })
                        }
                    }
                }
            }
            val upcoming = plan?.assignments?.filter { it.day > today.toString() }.orEmpty()
            if (upcoming.isNotEmpty() && planning) {
                item { SectionHeader("Diese Woche", action = "Woche", onAction = { nav.push(Route.Week) }) }
                item {
                    SectionCard {
                        upcoming.take(5).forEachIndexed { index, job ->
                            if (index > 0) RowDivider(16.dp)
                            WeekRow(job, today) { nav.push(Route.Bauakte(job.bauakteId)) }
                        }
                    }
                }
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
            Secondary("${Dates.clock(job.startTime) ?: "ganztags"} · ${job.activity}", maxLines = 1)
        }
        Icon(Icons.AutoMirrored.Filled.KeyboardArrowRight, null, tint = tones.textTertiary)
    }
}

/** Karte „Mein Einsatz“ wie TodayJob.swift: Zeit, Status, Baustelle, Adresse mit Route, Kontakt, Tätigkeit, Kolonne, Aktionen. */
@OptIn(ExperimentalLayoutApi::class)
@Composable
fun TodayJobCard(job: PlannedJob, session: Session) {
    val nav = LocalNav.current
    val context = LocalContext.current
    val bauakte by session.local(job.bauakteId) { session.service.bauakte(job.bauakteId) }
    SectionCard(padding = androidx.compose.foundation.layout.PaddingValues(16.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(job.time, style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.Bold, modifier = Modifier.weight(1f))
            bauakte?.let { StatusLabel(it.status) }
        }
        Row(Modifier.fillMaxWidth().clickable { nav.open(de.estrichone.core.NotificationRoute.Bauakte(job.bauakteId)) }.padding(vertical = 4.dp),
            verticalAlignment = Alignment.CenterVertically) {
            Column(Modifier.weight(1f)) {
                Text(job.bauakteTitle, style = MaterialTheme.typography.titleLarge)
                Secondary(job.bauakteNumber ?: "Neu")
            }
            Icon(Icons.AutoMirrored.Filled.KeyboardArrowRight, null, tint = tones.textTertiary)
        }
        HorizontalDivider(Modifier.padding(vertical = 10.dp), color = tones.separator)
        job.bauakteAddress?.let { address ->
            JobLine(Icons.Outlined.Place, address) { CircleAction(Icons.Filled.Directions, "Route", { Links.route(context, address, bauakte?.latitude, bauakte?.longitude) }) }
        }
        val contact = bauakte?.siteContactName
        if (contact != null) {
            JobLine(Icons.Outlined.AccountCircle, "Vor Ort: $contact") {
                bauakte?.siteContactPhone?.let { phone -> CircleAction(Icons.Filled.Phone, "Anrufen", { Links.call(context, phone) }) }
            }
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
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            ActionTile(Icons.Filled.PhotoCamera, "Foto", primary = true, modifier = Modifier.weight(1f)) { nav.push(Route.Camera(job.bauakteId)) }
            val modules by session.modules.collectAsState()
            if (modules.isOn(AppModules.DIARY)) {
                ActionTile(Icons.AutoMirrored.Outlined.MenuBook, "Tagebuch", modifier = Modifier.weight(1f)) { nav.captureTask = SiteTask.Diary.with(job.bauakteId) }
            }
            if (modules.isOn(AppModules.DEFECTS)) {
                ActionTile(Icons.Outlined.ReportProblem, "Mangel", modifier = Modifier.weight(1f)) { nav.captureTask = SiteTask.Defect.with(job.bauakteId) }
            }
            ActionTile(Icons.Outlined.Folder, "Akte", modifier = Modifier.weight(1f)) { nav.open(de.estrichone.core.NotificationRoute.Bauakte(job.bauakteId)) }
        }
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
    Row(
        Modifier.fillMaxWidth().height(IntrinsicSize.Min).clip(RoundedCornerShape(18.dp))
            .background(if (item.tone == NoticeItem.Tone.Error) soft.copy(alpha = if (tones.dark) 0.6f else 0.35f) else tones.card)
            .border(1.dp, color.copy(alpha = 0.25f), RoundedCornerShape(18.dp)),
    ) {
        Box(Modifier.width(5.dp).fillMaxHeight().background(color))
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
                val countdown = listOf(item.relative, item.remaining).filter { it.isNotEmpty() }.joinToString(" · ")
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

