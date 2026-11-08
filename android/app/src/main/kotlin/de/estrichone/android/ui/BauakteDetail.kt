package de.estrichone.android.ui

import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowDropDown
import androidx.compose.material.icons.filled.CalendarMonth
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.Directions
import androidx.compose.material.icons.filled.DocumentScanner
import androidx.compose.material.icons.filled.MoreVert
import androidx.compose.material.icons.filled.Phone
import androidx.compose.material.icons.filled.PhotoCamera
import androidx.compose.material.icons.filled.PlayCircle
import androidx.compose.material.icons.filled.PushPin
import androidx.compose.material.icons.filled.RadioButtonUnchecked
import androidx.compose.material.icons.filled.Warning
import androidx.compose.material.icons.outlined.AccountCircle
import androidx.compose.material.icons.automirrored.outlined.Assignment
import androidx.compose.material.icons.outlined.ChatBubbleOutline
import androidx.compose.material.icons.outlined.Contacts
import androidx.compose.material.icons.outlined.Description
import androidx.compose.material.icons.outlined.Engineering
import androidx.compose.material.icons.outlined.Folder
import androidx.compose.material.icons.outlined.Forum
import androidx.compose.material.icons.outlined.GridView
import androidx.compose.material.icons.automirrored.outlined.MenuBook
import androidx.compose.material.icons.outlined.MoreHoriz
import androidx.compose.material.icons.outlined.PersonPin
import androidx.compose.material.icons.outlined.PhotoLibrary
import androidx.compose.material.icons.outlined.Place
import androidx.compose.material.icons.outlined.ReportProblem
import androidx.compose.material.icons.automirrored.outlined.StickyNote2
import androidx.compose.material.icons.outlined.Tune
import androidx.compose.material.icons.outlined.VerifiedUser
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import de.estrichone.android.Session
import de.estrichone.android.support.Links
import de.estrichone.core.Bauakte
import de.estrichone.core.BauakteStatus
import de.estrichone.core.Dates
import de.estrichone.core.FileItem
import de.estrichone.core.PlannedJob
import de.estrichone.core.Task
import de.estrichone.core.WorkItemKind
import de.estrichone.core.WorkItemStatus
import de.estrichone.core.display
import de.estrichone.core.dueText
import kotlinx.coroutines.launch
import java.time.LocalDate

/** Bereiche einer Bauakte (wie BauakteDetailView.Area auf iOS). */
enum class AreaKind(val title: String, val icon: ImageVector) {
    Notes("Notizen", Icons.AutoMirrored.Outlined.StickyNote2),
    Photos("Fotos und Videos", Icons.Outlined.PhotoLibrary),
    Documents("Dokumente", Icons.Outlined.Description),
    Tasks("Aufgaben und Mängel", Icons.AutoMirrored.Outlined.Assignment),
    Diary("Bautagebuch", Icons.AutoMirrored.Outlined.MenuBook),
    Protocols("Protokolle", Icons.Outlined.VerifiedUser),
    Rooms("Räume und Flächen", Icons.Outlined.GridView),
    Contacts("Kontakte und Beteiligte", Icons.Outlined.Contacts),
    Project("Estrich und Bauvorhaben", Icons.Outlined.Tune),
}

@Composable
fun BauakteScreen(id: String, showBack: Boolean = true) {
    val session = LocalSession.current
    val nav = LocalNav.current
    val context = LocalContext.current
    val model = LocalModel.current
    val bauakte by session.local(id) { session.service.bauakte(id) }
    var menu by remember { mutableStateOf(false) }
    var cancelling by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    val b = bauakte
    if (b == null) {
        ScreenScaffold("Bauakte", showBack = showBack) { padding ->
            EmptyState(Icons.Outlined.Folder, "Bauakte nicht auf dem Gerät", "Sie ist nicht (mehr) sichtbar oder wird gerade geladen.",
                Modifier.padding(padding)) {
                TextButton(onClick = { scope.launch { session.synchronize() } }) { Text("Jetzt abgleichen") }
            }
        }
        return
    }

    ScreenScaffold(b.title, showBack = showBack, actions = {
        IconButton(onClick = { menu = true }) { Icon(Icons.Filled.MoreVert, "Weitere Aktionen") }
        DropdownMenu(menu, { menu = false }) {
            DropdownMenuItem(text = { Text("Basisdaten bearbeiten") }, onClick = { menu = false; nav.push(Route.Area(id, AreaKind.Project)) })
            DropdownMenuItem(text = { Text("Bauakte als PDF") }, onClick = {
                menu = false
                scope.launch {
                    val pdf = runCatching { session.api.bytes("bauakten/$id/report.pdf") }.getOrNull()
                    if (pdf != null) Links.open(context, pdf, "${b.numberText} ${b.title}.pdf", "application/pdf")
                    else model.message("Das PDF ist nur mit Verbindung zum Server verfügbar.")
                }
            })
            if (session.chat.value?.isAvailable == true) {
                DropdownMenuItem(text = { Text("Baustellenchat") }, onClick = { menu = false; nav.push(Route.ChatList) })
            }
            DropdownMenuItem(text = { Text("Bauakte stornieren", color = tones.danger) }, enabled = b.status != BauakteStatus.Cancelled,
                onClick = { menu = false; cancelling = true })
        }
    }) { padding ->
        LazyColumn(contentPadding = padding.content(), modifier = Modifier.readable()) {
            item { Header(b, session) }
            item { PinnedNote(session, id) }
            item { ContactCard(b, session) }
            item { ActionBar(id) }
            item { OpenPoints(session, id) }
            item { PhotoStrip(session, id) }
            item { ProjectGrid(session, b) }
            item { Areas(session, b) }
            b.description?.takeIf { it.isNotBlank() }?.let { text ->
                item { SectionHeader("Beschreibung") }
                item { SectionCard(padding = androidx.compose.foundation.layout.PaddingValues(16.dp)) { Text(text) } }
            }
        }
    }
    if (cancelling) CancelDialog(onDismiss = { cancelling = false }) { reason -> session.service.setStatus(id, BauakteStatus.Cancelled, reason) }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun Header(b: Bauakte, session: Session) {
    var statusMenu by remember { mutableStateOf(false) }
    val badges = remember(b) { session.service.badges(b) }
    Column(Modifier.padding(horizontal = 4.dp, vertical = 4.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(b.numberText, style = MaterialTheme.typography.titleMedium, color = tones.textSecondary, modifier = Modifier.weight(1f))
            SyncMark(b.syncState)
            Spacer(Modifier.width(8.dp))
            Box {
                Row(Modifier.clip(RoundedCornerShape(50)).background(tones.card).clickable { statusMenu = true }.padding(start = 12.dp, end = 6.dp, top = 6.dp, bottom = 6.dp),
                    verticalAlignment = Alignment.CenterVertically) {
                    StatusLabel(b.status, short = false)
                    Icon(Icons.Filled.ArrowDropDown, "Status ändern", tint = tones.textSecondary)
                }
                DropdownMenu(statusMenu, { statusMenu = false }) {
                    BauakteStatus.entries.filter { it != BauakteStatus.Cancelled }.forEach { status ->
                        DropdownMenuItem(text = { StatusLabel(status, short = false) }, onClick = {
                            statusMenu = false
                            if (status != b.status) session.service.setStatus(b.id, status)
                        })
                    }
                }
            }
        }
        Spacer(Modifier.height(6.dp))
        Text(b.title, style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.Bold)
        if (b.status == BauakteStatus.Cancelled && b.cancellationReason != null) Secondary("Storniert: ${b.cancellationReason}")
        if (badges.isNotEmpty()) {
            FlowRow(Modifier.padding(top = 10.dp), horizontalArrangement = Arrangement.spacedBy(6.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                badges.forEach { Tag(it) }
            }
        }
    }
}

@Composable
private fun PinnedNote(session: Session, id: String) {
    val pinned by session.local(id) { session.service.notes(id).filter { it.isPinned } }
    if (pinned.isEmpty()) return
    val nav = LocalNav.current
    Column(Modifier.padding(top = 16.dp)) {
        pinned.forEach { note ->
            Row(Modifier.fillMaxWidth().clip(RoundedCornerShape(18.dp)).background(tones.infoSoft).clickable { nav.push(Route.Area(id, AreaKind.Notes)) }
                .padding(16.dp)) {
                Icon(Icons.Filled.PushPin, null, tint = MaterialTheme.colorScheme.primary)
                Spacer(Modifier.width(12.dp))
                Column {
                    Text("Angeheftet", color = MaterialTheme.colorScheme.primary, style = MaterialTheme.typography.labelLarge)
                    Text(note.text, style = MaterialTheme.typography.bodyLarge)
                }
            }
        }
    }
}

@Composable
private fun ContactCard(b: Bauakte, session: Session) {
    val context = LocalContext.current
    val nav = LocalNav.current
    val customer = remember(b) { session.service.contact(b.customerId) }
    val primary = remember(b) { session.service.parties(b.id).firstOrNull { it.isPrimary } }
    val next by produceState<PlannedJob?>(null, b.id) {
        val today = LocalDate.now(Dates.zone).toString()
        value = session.weekPlan()?.assignments?.filter { it.bauakteId == b.id && it.day >= today }?.minByOrNull { it.day + (it.startTime ?: "") }
    }
    val rows = mutableListOf<@Composable () -> Unit>()
    b.addressLine?.let {
        rows += {
            RowItem(it, icon = Icons.Outlined.Place, subtitle = "Baustelle", trailing = {
                CircleAction(Icons.Filled.Directions, "Route", { Links.route(context, it, b.latitude, b.longitude) })
            })
        }
    }
    customer?.let { c ->
        rows += {
            RowItem(c.name, icon = Icons.Outlined.AccountCircle, subtitle = listOfNotNull("Kunde", c.customerNumber).joinToString(" · "),
                onClick = { nav.push(Route.Customer(c.id)) }, trailing = { c.phone?.let { p -> CircleAction(Icons.Filled.Phone, "Kunden anrufen", { Links.call(context, p) }) } })
        }
    }
    b.siteContactName?.let { name ->
        rows += {
            RowItem(name, icon = Icons.Outlined.PersonPin, subtitle = "Ansprechpartner vor Ort",
                trailing = { b.siteContactPhone?.let { p -> CircleAction(Icons.Filled.Phone, "Ansprechpartner anrufen", { Links.call(context, p) }) } })
        }
    }
    primary?.let { p ->
        rows += {
            RowItem(p.name, icon = Icons.Outlined.Engineering, subtitle = listOfNotNull(p.role.label, p.company).joinToString(" · "),
                onClick = { nav.push(Route.Area(b.id, AreaKind.Contacts)) },
                trailing = { p.bestPhone?.let { phone -> CircleAction(Icons.Filled.Phone, "${p.role.label} anrufen", { Links.call(context, phone) }) } })
        }
    }
    next?.let { job ->
        val day = de.estrichone.core.ApiDate.day(job.day)
        rows += {
            RowItem("${day?.let { Dates.relativeDay(it) } ?: job.day}${Dates.clock(job.startTime)?.let { " ab $it" } ?: ""} · ${job.activity}",
                icon = Icons.Filled.CalendarMonth, subtitle = listOfNotNull(job.crewName, job.people.joinToString(", ") { it.name }.ifEmpty { null },
                    job.resources.joinToString(", ") { it.name }.ifEmpty { null }).joinToString(" · "))
        }
    }
    SectionCard(Modifier.padding(top = 16.dp)) {
        if (rows.isEmpty()) RowItem("Keine Adresse und keine Kontakte hinterlegt", icon = Icons.Outlined.Place)
        rows.forEachIndexed { index, row ->
            if (index > 0) RowDivider()
            row()
        }
    }
}

@Composable
private fun ActionBar(id: String) {
    val nav = LocalNav.current
    Row(Modifier.fillMaxWidth().padding(top = 16.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        ActionTile(Icons.Filled.PhotoCamera, "Foto", primary = true, modifier = Modifier.weight(1f)) { nav.push(Route.Camera(id)) }
        ActionTile(Icons.Filled.DocumentScanner, "Scannen", modifier = Modifier.weight(1f)) { nav.push(Route.Camera(id, scan = true)) }
        ActionTile(Icons.AutoMirrored.Outlined.MenuBook, "Tagebuch", modifier = Modifier.weight(1f)) { nav.captureTask = SiteTask.Diary.with(id) }
        ActionTile(Icons.Outlined.ReportProblem, "Mangel", modifier = Modifier.weight(1f)) { nav.captureTask = SiteTask.Defect.with(id) }
        ActionTile(Icons.Outlined.MoreHoriz, "Mehr", modifier = Modifier.weight(1f)) { nav.captureOpenFor = id }
    }
}

@Composable
private fun OpenPoints(session: Session, id: String) {
    val nav = LocalNav.current
    val tasks by session.local(id) { session.service.tasks(id) }
    val open = tasks.filter { it.isOpen }
    SectionHeader("Offene Punkte", action = "Alle (${tasks.size})", onAction = { nav.push(Route.Area(id, AreaKind.Tasks)) })
    SectionCard {
        if (open.isEmpty()) {
            RowItem("Keine offenen Aufgaben oder Mängel", icon = Icons.Filled.CheckCircle, iconTint = tones.success, iconBackground = tones.successSoft)
        }
        open.take(3).forEachIndexed { index, task ->
            if (index > 0) RowDivider(56.dp)
            TaskRow(task, session)
        }
    }
}

/** Aufgabe bzw. Mangel mit Abhaken (wie WorkItemViews auf iOS). */
@Composable
fun TaskRow(task: Task, session: Session, subtitle: String? = null) {
    val today = LocalDate.now(Dates.zone)
    Row(Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 10.dp), verticalAlignment = Alignment.Top) {
        IconButton(onClick = {
            session.service.setTaskStatus(task, if (task.isOpen) WorkItemStatus.Done else WorkItemStatus.Open)
        }) {
            Icon(if (task.isOpen) Icons.Filled.RadioButtonUnchecked else Icons.Filled.CheckCircle,
                if (task.isOpen) "Als erledigt markieren" else "Wieder öffnen",
                tint = when {
                    !task.isOpen -> tones.success
                    task.kind == WorkItemKind.Defect -> tones.warning
                    else -> tones.textTertiary
                }, modifier = Modifier.size(28.dp))
        }
        Column(Modifier.weight(1f).padding(top = 10.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(task.title, style = MaterialTheme.typography.bodyLarge, modifier = Modifier.weight(1f, fill = false),
                    color = if (task.isOpen) MaterialTheme.colorScheme.onSurface else tones.textTertiary)
                Spacer(Modifier.width(6.dp))
                SyncMark(task.syncState)
            }
            Row(Modifier.padding(top = 4.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                if (task.kind == WorkItemKind.Defect) Pill("Mangel", tones.warning, tones.warningSoft, Icons.Filled.Warning)
                task.dueDate?.let { due ->
                    val overdue = task.isOpen && !due.isAfter(today)
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Icon(Icons.Filled.CalendarMonth, null, tint = if (overdue) tones.danger else tones.textSecondary, modifier = Modifier.size(15.dp))
                        Spacer(Modifier.width(4.dp))
                        Text(dueText(due, today), style = MaterialTheme.typography.labelLarge, color = if (overdue) tones.danger else tones.textSecondary)
                    }
                }
            }
            subtitle?.let { Secondary(it, Modifier.padding(top = 2.dp)) }
        }
    }
}

@Composable
private fun PhotoStrip(session: Session, id: String) {
    val nav = LocalNav.current
    val photos by session.local(id) { session.service.photos(id) }
    SectionHeader("Fotos", action = "Alle (${photos.size})", onAction = { nav.push(Route.Area(id, AreaKind.Photos)) })
    LazyRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        item {
            Column(Modifier.size(116.dp).clip(RoundedCornerShape(16.dp)).background(tones.infoSoft).clickable { nav.push(Route.Camera(id)) },
                horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.Center) {
                Icon(Icons.Filled.PhotoCamera, null, tint = MaterialTheme.colorScheme.primary)
                Text("Foto", color = MaterialTheme.colorScheme.primary, style = MaterialTheme.typography.labelLarge)
            }
        }
        items(photos.take(12), key = { it.id }) { file -> Thumbnail(file, session, 116.dp) { nav.push(Route.Photo(file.id)) } }
    }
}

/** Vorschaubild mit Ladezustand und Übertragungskennzeichen. */
@Composable
fun Thumbnail(file: FileItem, session: Session, size: Dp, modifier: Modifier = Modifier, onClick: () -> Unit) {
    val bitmap by produceState<android.graphics.Bitmap?>(null, file.id, file.isStoredOnServer) { value = session.thumbnail(file) }
    Box(modifier.size(size).clip(RoundedCornerShape(16.dp)).background(tones.fill).clickable(onClick = onClick)) {
        bitmap?.let { Image(it.asImageBitmap(), file.displayName, contentScale = ContentScale.Crop, modifier = Modifier.fillMaxSize()) }
        if (file.isVideo) Icon(Icons.Filled.PlayCircle, "Video", tint = androidx.compose.ui.graphics.Color.White, modifier = Modifier.align(Alignment.Center).size(36.dp))
        Box(Modifier.align(Alignment.TopEnd).padding(6.dp)) { SyncMark(file.syncState) }
    }
}

@Composable
private fun ProjectGrid(session: Session, b: Bauakte) {
    val nav = LocalNav.current
    val schema = remember(b.id) { session.service.schema("bauakte") }
    val lists = remember(b.id) { session.service.valueLists() }
    val fields = schema?.groups?.flatMap { it.fields }.orEmpty().mapNotNull { field ->
        field.display(b.project[field.path.removePrefix("project.")], lists)?.let { field.label to it }
    }.filter { it.second.length < 40 }
    SectionHeader("Estrich und Bauvorhaben", action = "Bearbeiten", onAction = { nav.push(Route.Area(b.id, AreaKind.Project)) })
    SectionCard(padding = androidx.compose.foundation.layout.PaddingValues(16.dp)) {
        if (fields.isEmpty()) Secondary("Noch keine Angaben. Über „Bearbeiten“ ergänzen.")
        fields.chunked(2).forEach { row ->
            Row(Modifier.fillMaxWidth().padding(vertical = 6.dp)) {
                row.forEach { (label, value) ->
                    Column(Modifier.weight(1f).padding(end = 8.dp)) {
                        Text(label, style = MaterialTheme.typography.bodySmall, color = tones.textSecondary)
                        Text(value, style = MaterialTheme.typography.bodyLarge, fontWeight = FontWeight.SemiBold, maxLines = 2, overflow = TextOverflow.Ellipsis)
                    }
                }
                if (row.size == 1) Spacer(Modifier.weight(1f))
            }
        }
    }
}

@Composable
private fun Areas(session: Session, b: Bauakte) {
    val nav = LocalNav.current
    val counts by session.local(b.id) {
        val s = session.service
        mapOf(
            AreaKind.Notes to s.notes(b.id).size, AreaKind.Photos to s.photos(b.id).size, AreaKind.Documents to s.documents(b.id).size,
            AreaKind.Tasks to s.tasks(b.id).count { it.isOpen }, AreaKind.Diary to s.diary(b.id).size,
            AreaKind.Protocols to s.inspections(b.id).size + s.acceptances(b.id).size, AreaKind.Rooms to s.rooms(b.id).size,
            AreaKind.Contacts to s.parties(b.id).size + (if (b.customerId != null) 1 else 0),
        )
    }
    SectionHeader("Bereiche")
    SectionCard {
        AreaKind.entries.filter { it != AreaKind.Project }.forEachIndexed { index, area ->
            if (index > 0) RowDivider()
            RowItem(area.title, icon = area.icon, value = counts[area]?.takeIf { it > 0 }?.toString(), showChevron = true,
                onClick = { nav.push(Route.Area(b.id, area)) })
        }
    }
}

@Composable
private fun CancelDialog(onDismiss: () -> Unit, onConfirm: (String) -> Unit) {
    var reason by remember { mutableStateOf("") }
    var error by remember { mutableStateOf<String?>(null) }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Bauakte stornieren?") },
        text = {
            Column {
                Text("Die Bauakte bleibt erhalten und ist als storniert gekennzeichnet. Bitte den Grund angeben.")
                OutlinedTextField(reason, { reason = it }, label = { Text("Grund") }, modifier = Modifier.fillMaxWidth().padding(top = 12.dp))
                error?.let { Text(it, color = tones.danger) }
            }
        },
        confirmButton = {
            TextButton(onClick = {
                try {
                    onConfirm(reason)
                    onDismiss()
                } catch (e: IllegalArgumentException) {
                    error = e.message
                }
            }) { Text("Stornieren", color = tones.danger) }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Abbrechen") } },
    )
}
