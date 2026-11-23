package de.estrichone.android.ui

import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.automirrored.filled.KeyboardArrowRight
import androidx.compose.material.icons.filled.RemoveCircleOutline
import androidx.compose.material.icons.filled.Timelapse
import androidx.compose.material.icons.outlined.Groups
import androidx.compose.material3.ExtendedFloatingActionButton
import androidx.compose.runtime.saveable.rememberSaveable
import de.estrichone.core.SiteList
import de.estrichone.core.SiteWorkflow
import androidx.compose.material.icons.filled.Edit
import androidx.compose.material.icons.filled.ExpandLess
import androidx.compose.foundation.layout.IntrinsicSize
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.withLink
import de.estrichone.core.chatBauakteGroup
import androidx.compose.material.icons.filled.ExpandMore
import androidx.compose.material.icons.filled.Videocam
import androidx.compose.material3.Button
import androidx.compose.material3.FilledTonalButton
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Surface
import androidx.compose.ui.text.style.TextAlign

import androidx.compose.foundation.combinedClickable
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
import androidx.compose.foundation.layout.heightIn
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
import androidx.compose.material.icons.automirrored.filled.FactCheck
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
import androidx.compose.runtime.collectAsState
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
import de.estrichone.core.AppModules
import de.estrichone.core.Bauakte
import de.estrichone.core.BauakteStatus
import de.estrichone.core.Dates
import de.estrichone.core.FileItem
import de.estrichone.core.PlannedJob
import de.estrichone.core.Task
import de.estrichone.core.WorkItemKind
import de.estrichone.core.WorkItemStatus
import de.estrichone.core.display
import de.estrichone.core.BauakteNotAssignedException
import de.estrichone.core.isMemberOf
import androidx.compose.material.icons.outlined.Visibility
import de.estrichone.core.dueText
import kotlinx.coroutines.launch
import java.time.LocalDate
import de.estrichone.core.bauakteTeams
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/** Bereiche einer Bauakte (wie BauakteDetailView.Area auf iOS). */
enum class AreaKind(val title: String, val icon: ImageVector) {
    Notes("Notizen", Icons.AutoMirrored.Outlined.StickyNote2),
    Photos("Fotos und Videos", Icons.Outlined.PhotoLibrary),
    Documents("Dokumente", Icons.Outlined.Description),
    Tasks("Aufgaben und Mängel", Icons.AutoMirrored.Outlined.Assignment),
    Diary("Bautagebuch", Icons.AutoMirrored.Outlined.MenuBook),
    InitialInspection("Erstbegehung", Icons.AutoMirrored.Filled.FactCheck),
    Protocols("Abnahme und CM-Messungen", Icons.Outlined.VerifiedUser),
    Rooms("Räume und Flächen", Icons.Outlined.GridView),
    Contacts("Kontakte und Beteiligte", Icons.Outlined.Contacts),
    Project("Estrich und Bauvorhaben", Icons.Outlined.Tune);

    /** Bereich gehört zu einem eingeschalteten Modul (Systemeinstellungen › Module)? */
    fun isAvailable(modules: AppModules): Boolean = when (this) {
        Tasks -> modules.anyOn(AppModules.TASKS, AppModules.DEFECTS)
        Diary -> modules.isOn(AppModules.DIARY)
        InitialInspection -> modules.isOn(AppModules.INSPECTIONS)
        Protocols -> modules.anyOn(AppModules.INSPECTIONS, AppModules.ACCEPTANCE)
        else -> true
    }
}

/**
 * Bauakte nach dem Arbeitsablauf (gleiches Konzept wie iOS): 1. Kopf mit Aktionsleiste, 2. heute bzw. nächster Termin,
 * 3. Hinweise und Behinderungen, angeheftete Notizen, 4. Leistung und Kennwerte, 5. Ablauf als Fortschritt mit dem
 * nächsten Schritt zum Erfassen, 6. offene Aufgaben und Mängel, 7. Fotos und Dokumentation, 8. Kontakte, 9. Beschreibung
 * und Verlauf. Erfassen als „Erfassen“ unten rechts (klappt beim Scrollen ein und verdeckt keinen Inhalt).
 */
@Composable
fun BauakteScreen(id: String, showBack: Boolean = true) {
    val session = LocalSession.current
    val modules by session.modules.collectAsState()
    val nav = LocalNav.current
    val context = LocalContext.current
    val model = LocalModel.current
    val bauakte by session.local(id) { session.service.bauakte(id) }
    // Microsoft Teams: verknüpfte Unterhaltung (nur online und im Modus „Teams“ bzw. „beides“).
    val teams by session.teams.collectAsState()
    var teamsLink by remember(id) { mutableStateOf<de.estrichone.core.TeamsLink?>(null) }
    var teamChat by remember(id) { mutableStateOf<de.estrichone.core.TeamsChatLink?>(null) }
    LaunchedEffect(id, teams?.available) {
        val loaded = if (teams?.available == true) {
            runCatching { withContext(Dispatchers.IO) { session.api.bauakteTeams(id) } }.getOrNull()
        } else {
            null
        }
        teamsLink = loaded?.link
        teamChat = loaded?.teamChat
    }
    var menu by remember { mutableStateOf(false) }
    var cancelling by remember { mutableStateOf(false) }
    // Für „Zuletzt geöffnet“ auf Heute merken.
    LaunchedEffect(id) { model.prefs.markBauakteOpened(session.user.id, id) }
    val scope = rememberCoroutineScope()

    val b = bauakte
    if (b == null) {
        // Nicht auf dem Gerät, meist nicht zugeteilt (offline liegen nur zugeteilte): Kopf online, „Mich als Beteiligten eintragen“.
        OnlineBauakteScreen(id, showBack)
        return
    }
    val revision by session.store.revision.collectAsState()
    val viewOnly = remember(revision, id) { session.store.isViewOnly(id) }
    var isMember by remember(id) { mutableStateOf(false) }
    LaunchedEffect(id) { isMember = runCatching { withContext(Dispatchers.IO) { session.api.isMemberOf(id, session.user.id) } }.getOrDefault(false) }
    val join: () -> Unit = {
        scope.launch {
            runCatching { session.joinBauakte(id) }
                .onSuccess { isMember = true; model.message("Als Beteiligter eingetragen. Bearbeiten ist freigeschaltet.") }
                .onFailure { model.message(it.message ?: "Eintragen nicht möglich. Bitte mit Verbindung erneut versuchen.") }
        }
    }

    val listState = androidx.compose.foundation.lazy.rememberLazyListState()
    // Titel oben erst nach dem Kopf; „Erfassen“ klappt beim Scrollen nach unten auf das Symbol ein.
    val scrolled by remember { androidx.compose.runtime.derivedStateOf { listState.firstVisibleItemIndex > 0 } }
    val fabExpanded by remember { androidx.compose.runtime.derivedStateOf { listState.firstVisibleItemIndex == 0 } }
    ScreenScaffold(if (scrolled) b.title else "", showBack = showBack, floating = {
        if (!viewOnly) {
            ExtendedFloatingActionButton(onClick = { nav.captureOpenFor = id }, expanded = fabExpanded,
                icon = { Icon(Icons.Filled.Add, null) }, text = { Text("Erfassen") }, modifier = Modifier.testTag("bauakteCapture"))
        }
    }, actions = {
        IconButton(onClick = { menu = true }) { Icon(Icons.Filled.MoreVert, "Weitere Aktionen") }
        DropdownMenu(menu, { menu = false }) {
            if (viewOnly) {
                DropdownMenuItem(text = { Text(BauakteNotAssignedException.JOIN) }, onClick = { menu = false; join() })
            }
            if (!viewOnly) {
                DropdownMenuItem(text = { Text("Basisdaten bearbeiten") }, leadingIcon = { Icon(Icons.Filled.Edit, null) },
                    onClick = { menu = false; nav.push(Route.Area(id, AreaKind.Project)) })
            }
            DropdownMenuItem(text = { Text("Bauakte als PDF") }, leadingIcon = { Icon(Icons.Outlined.Description, null) }, onClick = {
                menu = false
                scope.launch {
                    val pdf = runCatching { session.api.bytes("bauakten/$id/report.pdf") }.getOrNull()
                    if (pdf != null) Links.open(context, pdf, "${b.numberText} ${b.title}.pdf", "application/pdf")
                    else model.message("Das PDF ist nur mit Verbindung zum Server verfügbar.")
                }
            })
            teamChat?.let { chat ->
                DropdownMenuItem(text = { Text("Chat mit dem Team in Teams") }, leadingIcon = { Icon(Icons.Outlined.Groups, null) }, onClick = {
                    menu = false
                    if (!Links.teams(context, chat.targets)) model.message("Microsoft Teams lässt sich auf diesem Gerät nicht öffnen.")
                })
            }
            if (isMember) {
                DropdownMenuItem(text = { Text(BauakteNotAssignedException.LEAVE) }, onClick = {
                    menu = false
                    scope.launch {
                        runCatching { session.leaveBauakte(id) }
                            .onSuccess { isMember = false; model.message("Als Beteiligter ausgetragen.") }
                            .onFailure { model.message(it.message ?: "Austragen nicht möglich. Bitte mit Verbindung erneut versuchen.") }
                    }
                })
            }
            if (!viewOnly) {
                HorizontalDivider()
                DropdownMenuItem(text = { Text("Bauakte stornieren", color = tones.danger) }, enabled = b.status != BauakteStatus.Cancelled,
                    onClick = { menu = false; cancelling = true })
            }
        }
    }) { padding ->
        androidx.compose.runtime.CompositionLocalProvider(LocalViewOnly provides viewOnly) {
        LazyColumn(state = listState, contentPadding = padding.content(), modifier = Modifier.readable()) {
            if (viewOnly) {
                item(key = "viewOnly") {
                    Banner(BauakteNotAssignedException.MESSAGE, Icons.Outlined.Visibility, MaterialTheme.colorScheme.primary, tones.infoSoft,
                        Modifier.padding(bottom = Space.s), action = BauakteNotAssignedException.JOIN, onAction = join)
                }
            }
            // 1. Kopf und Aktionsleiste
            item(key = "head") {
                Header(b, session, viewOnly)
                ActionBar(b, session, teamsLink)
            }
            // 2. Heute bzw. nächster Termin
            if (modules.isOn(AppModules.PLANNING)) item(key = "next") { NextAppointment(b, session) }
            // 3. Hinweise und Behinderungen, Zugang, angeheftete Notizen, fehlende Erstbegehung
            item(key = "hints") {
                FreeTexts(session, b, viewOnly, important = true)
                PinnedNote(session, id)
                InitialInspectionHint(session, b)
            }
            // 4. Leistung und Kennwerte
            item(key = "keyData") { KeyData(session, b) }
            // 5. Ablauf
            item(key = "workflow") { Workflow(session, b, viewOnly) }
            // 6. Offene Aufgaben und Mängel
            if (modules.anyOn(AppModules.TASKS, AppModules.DEFECTS)) item(key = "tasks") { OpenPoints(session, id) }
            // 7. Fotos und Dokumentation
            item(key = "docs") { Documentation(session, b) }
            // 8. Kontakte
            item(key = "contacts") { ContactSection(b, session) }
            // 9. Beschreibung und Verlauf
            item(key = "history") { History(session, b, viewOnly) }
        }
        }
    }
    if (cancelling) CancelDialog(onDismiss = { cancelling = false }) { reason -> session.service.setStatus(id, BauakteStatus.Cancelled, reason) }
}

/** Kopf: Nummer, Bauvorhaben, Status (antippen ändert ihn) und Adresse. */
@Composable
private fun Header(b: Bauakte, session: Session, viewOnly: Boolean = false) {
    var statusMenu by remember { mutableStateOf(false) }
    Column(Modifier.padding(horizontal = 4.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(b.numberText, style = MaterialTheme.typography.labelLarge, color = tones.textSecondary)
            Spacer(Modifier.width(8.dp))
            SyncMark(b.syncState)
        }
        Text(b.title, style = MaterialTheme.typography.headlineSmall, modifier = Modifier.padding(top = 2.dp))
        Row(Modifier.padding(top = 8.dp), verticalAlignment = Alignment.CenterVertically) {
            Box {
                StatusChip(b.status, short = false, modifier = Modifier.clip(RoundedCornerShape(8.dp))
                    .clickable(enabled = !viewOnly, onClickLabel = "Status ändern") { statusMenu = true },
                    trailing = if (viewOnly) null else ({ Icon(Icons.Filled.ArrowDropDown, null, tint = statusColor(b.status), modifier = Modifier.size(18.dp)) }))
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
        b.addressLine?.let { Secondary(it, Modifier.padding(top = 8.dp)) }
        if (b.status == BauakteStatus.Cancelled && b.cancellationReason != null) Secondary("Storniert: ${b.cancellationReason}", Modifier.padding(top = 4.dp))
    }
}

/** Ein Kontakt der Bauakte mit Rolle und Telefonnummer (für „Anrufen“ und den Abschnitt Kontakte). */
private data class SiteContact(val name: String, val role: String, val phone: String?, val icon: ImageVector, val open: (() -> Unit)?)

@Composable
private fun siteContacts(b: Bauakte, session: Session): List<SiteContact> {
    val nav = LocalNav.current
    val revision by session.store.revision.collectAsState()
    val customer = remember(b, revision) { session.service.contact(b.customerId) }
    val parties = remember(b, revision) { session.service.parties(b.id) }
    val out = mutableListOf<SiteContact>()
    b.siteContactName?.let { out += SiteContact(it, "Ansprechpartner vor Ort", b.siteContactPhone?.takeIf { p -> p.isNotBlank() }, Icons.Outlined.PersonPin, null) }
    customer?.let { c ->
        out += SiteContact(c.name, listOfNotNull("Kunde", c.customerNumber).joinToString(", "), c.phone, Icons.Outlined.AccountCircle, { nav.push(Route.Customer(c.id)) })
    }
    parties.sortedByDescending { it.isPrimary }.forEach { p ->
        out += SiteContact(p.name, listOfNotNull(p.role.label, p.company).joinToString(", "), p.bestPhone, Icons.Outlined.Engineering,
            { nav.push(Route.Area(b.id, AreaKind.Contacts)) })
    }
    return out
}

/** Aktionsleiste unter dem Kopf: Route, Anrufen (mit Auswahl der Kontakte), Chat der Baustelle, Teams (wenn verknüpft). */
@Composable
private fun ActionBar(b: Bauakte, session: Session, teamsLink: de.estrichone.core.TeamsLink?) {
    val context = LocalContext.current
    val nav = LocalNav.current
    val model = LocalModel.current
    val scope = rememberCoroutineScope()
    val chat by session.chat.collectAsState()
    val callable = siteContacts(b, session).filter { it.phone != null }
    var calling by remember { mutableStateOf(false) }
    SectionCard(Modifier.padding(top = Space.l), padding = androidx.compose.foundation.layout.PaddingValues(horizontal = 8.dp, vertical = 4.dp)) {
        QuickActionRow {
            QuickAction(Icons.Filled.Directions, "Route", Modifier.weight(1f), enabled = b.addressLine != null) {
                b.addressLine?.let { Links.route(context, it, b.latitude, b.longitude) }
            }
            Box(Modifier.weight(1f)) {
                QuickAction(Icons.Filled.Phone, "Anrufen", Modifier.fillMaxWidth(), enabled = callable.isNotEmpty()) {
                    if (callable.size == 1) Links.call(context, callable.first().phone!!) else calling = true
                }
                DropdownMenu(calling, { calling = false }) {
                    callable.forEach { contact ->
                        DropdownMenuItem(text = {
                            Column {
                                Text(contact.name)
                                Text(listOfNotNull(contact.role, contact.phone).joinToString(", "), style = MaterialTheme.typography.bodySmall, color = tones.textSecondary)
                            }
                        }, leadingIcon = { Icon(Icons.Filled.Phone, null) }, onClick = { calling = false; Links.call(context, contact.phone!!) })
                    }
                }
            }
            QuickAction(Icons.Outlined.ChatBubbleOutline, "Chat", Modifier.weight(1f), enabled = chat?.isAvailable == true && chat?.readOnly != true) {
                // Gruppe der Bauakte direkt öffnen (wenn Mitglied), sonst die Chatliste.
                scope.launch {
                    val group = runCatching { withContext(Dispatchers.IO) { session.api.chatBauakteGroup(b.id) } }.getOrNull()
                    nav.openChat(group?.conversationId?.takeIf { group.member })
                }
            }
            if (teamsLink != null) {
                QuickAction(Icons.Outlined.Groups, "Teams", Modifier.weight(1f)) {
                    if (!Links.teams(context, teamsLink.targets)) model.message("Microsoft Teams lässt sich auf diesem Gerät nicht öffnen.")
                }
            }
        }
    }
}

/** Heute bzw. nächster Termin auf dieser Baustelle: Einsatz, Kolonne mit Besetzung, Fahrzeuge. */
@Composable
private fun NextAppointment(b: Bauakte, session: Session) {
    val today = remember { LocalDate.now(Dates.zone) }
    val next by produceState<PlannedJob?>(null, b.id) {
        value = session.weekPlan()?.assignments?.let { SiteList.nextJobs(it, today.toString())[b.id.lowercase()] }
    }
    val job = next
    SectionHeader(if (job?.day == today.toString()) "Heute auf der Baustelle" else "Nächster Termin")
    SectionCard {
        if (job == null) {
            RowItem("Kein Einsatz in den nächsten 7 Tagen geplant", icon = Icons.Filled.CalendarMonth, iconTint = tones.textSecondary)
            return@SectionCard
        }
        Row(Modifier.fillMaxWidth().padding(16.dp), verticalAlignment = Alignment.Top) {
            Icon(Icons.Filled.CalendarMonth, null, tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(24.dp))
            Spacer(Modifier.width(16.dp))
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                Text(listOfNotNull(SiteList.appointmentText(job, today), Dates.clock(job.endTime)?.let { "bis $it" }).joinToString(" "),
                    style = MaterialTheme.typography.titleSmall, color = MaterialTheme.colorScheme.primary)
                Text(job.activity, style = MaterialTheme.typography.bodyLarge, fontWeight = FontWeight.SemiBold)
                val crew = listOfNotNull(job.crewName, job.people.joinToString(", ") { it.name }.ifEmpty { null }).joinToString(": ")
                if (crew.isNotEmpty()) Secondary(crew)
                if (job.resources.isNotEmpty()) Secondary(job.resources.joinToString(", ") { it.name })
                job.notes?.let { Secondary(it) }
            }
        }
    }
}

/** Textblöcke: [important] = Hinweise und Behinderungen sowie Zugang; sonst Beschreibung und weitere Freitexte. */
@Composable
private fun FreeTexts(session: Session, b: Bauakte, viewOnly: Boolean, important: Boolean) {
    val nav = LocalNav.current
    val schema = remember(b.id) { session.service.schema("bauakte") }
    val blocks = remember(b) { de.estrichone.core.FreeText.blocks(b, schema) }.filter { isHint(it) == important }
    if (blocks.isEmpty()) return
    if (!important) SectionHeader("Beschreibung")
    Column(Modifier.padding(top = if (important) Space.l else 0.dp), verticalArrangement = Arrangement.spacedBy(Space.s)) {
        blocks.forEach { block ->
            FreeTextCard(block, onEdit = if (viewOnly) null else ({ nav.push(Route.Area(b.id, AreaKind.Project)) }))
        }
    }
}

private fun isHint(block: de.estrichone.core.FreeText.Block) = block.important || block.label.contains("zugang", ignoreCase = true)

@Composable
private fun FreeTextCard(block: de.estrichone.core.FreeText.Block, onEdit: (() -> Unit)?) {
    var expanded by remember(block.key, block.text) { mutableStateOf(false) }
    val accent = if (block.important) tones.warning else MaterialTheme.colorScheme.primary
    val linkStyle = androidx.compose.ui.text.TextLinkStyles(androidx.compose.ui.text.SpanStyle(color = MaterialTheme.colorScheme.primary,
        textDecoration = androidx.compose.ui.text.style.TextDecoration.Underline))
    val text = remember(block.text, linkStyle) {
        androidx.compose.ui.text.buildAnnotatedString {
            de.estrichone.core.FreeText.segments(block.text).forEach { part ->
                val target = part.target
                if (target == null) append(part.text)
                else withLink(androidx.compose.ui.text.LinkAnnotation.Url(target, linkStyle)) { append(part.text) }
            }
        }
    }
    SectionCard(Modifier.testTag("freeText-${block.key}")) {
        Row(Modifier.fillMaxWidth().height(IntrinsicSize.Min)) {
            if (block.important) Box(Modifier.width(4.dp).fillMaxHeight().background(accent))
            Column(Modifier.weight(1f).padding(start = 16.dp, end = 4.dp, top = 4.dp, bottom = 12.dp)) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    if (block.important) {
                        Icon(Icons.Filled.Warning, null, tint = accent, modifier = Modifier.size(18.dp))
                        Spacer(Modifier.width(6.dp))
                    }
                    Text(block.label, style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.SemiBold, modifier = Modifier.weight(1f))
                    if (onEdit != null) {
                        IconButton(onClick = onEdit) { Icon(Icons.Filled.Edit, "${block.label} bearbeiten", tint = tones.textSecondary, modifier = Modifier.size(20.dp)) }
                    } else {
                        Spacer(Modifier.height(48.dp))
                    }
                }
                Text(text, style = MaterialTheme.typography.bodyLarge, maxLines = if (expanded || !block.isLong) Int.MAX_VALUE else de.estrichone.core.FreeText.COLLAPSED_LINES,
                    overflow = TextOverflow.Ellipsis)
                if (block.isLong) {
                    TextButton(onClick = { expanded = !expanded }, contentPadding = androidx.compose.foundation.layout.PaddingValues(horizontal = 0.dp),
                        modifier = Modifier.heightIn(min = 48.dp)) {
                        Text(if (expanded) "Weniger anzeigen" else "Mehr anzeigen")
                    }
                }
            }
        }
    }
}


@Composable
private fun PinnedNote(session: Session, id: String) {
    val pinned by session.local(id) { session.service.notes(id).filter { it.isPinned } }
    if (pinned.isEmpty()) return
    val nav = LocalNav.current
    Column(Modifier.padding(top = Space.s), verticalArrangement = Arrangement.spacedBy(Space.s)) {
        pinned.forEach { note ->
            Row(Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(tones.infoSoft).clickable { nav.push(Route.Area(id, AreaKind.Notes)) }
                .padding(16.dp)) {
                Icon(Icons.Filled.PushPin, null, tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(20.dp))
                Spacer(Modifier.width(16.dp))
                Column {
                    Text("Angeheftete Notiz", color = MaterialTheme.colorScheme.primary, style = MaterialTheme.typography.labelLarge)
                    Text(note.text, style = MaterialTheme.typography.bodyLarge)
                }
            }
        }
    }
}

/** Leistung und Kennwerte: Estrichart, Dicke, Fläche, Termin zuerst, dazu die Räume; alle weiteren aufklappbar. */
@Composable
private fun KeyData(session: Session, b: Bauakte) {
    val nav = LocalNav.current
    val schema = remember(b.id) { session.service.schema("bauakte") }
    val lists = remember(b.id) { session.service.valueLists() }
    val rooms by session.local(b.id) { session.service.rooms(b.id) }
    var expanded by remember(b.id) { mutableStateOf(false) }
    // Mehrzeilige Freitexte stehen als eigene Textblöcke (FreeTexts).
    val fields = keyDataFirst(schema?.groups?.flatMap { it.fields }.orEmpty().filter { it.type != "textarea" }.mapNotNull { field ->
        field.display(b.project[field.path.removePrefix("project.")], lists)?.let { field.label to it }
    })
    val roomsText = if (rooms.isEmpty()) null else {
        val area = rooms.mapNotNull { it.area }.sum()
        listOfNotNull(if (rooms.size == 1) "1 Raum" else "${rooms.size} Räume",
            area.takeIf { it > 0 }?.let { String.format(java.util.Locale.GERMANY, "%.1f m²", it) }).joinToString(", ")
    }
    SectionHeader("Leistung und Kennwerte", action = if (LocalViewOnly.current) null else "Bearbeiten", onAction = { nav.push(Route.Area(b.id, AreaKind.Project)) })
    SectionCard {
        if (fields.isEmpty() && roomsText == null) {
            RowItem("Basisdaten erfassen", titleColor = MaterialTheme.colorScheme.primary, onClick = { nav.push(Route.Area(b.id, AreaKind.Project)) })
        }
        val shown = if (expanded) fields else fields.take(4)
        shown.forEachIndexed { index, (label, value) ->
            if (index > 0) RowDivider(16.dp)
            KeyValueRow(label, value)
        }
        roomsText?.let {
            if (shown.isNotEmpty()) RowDivider(16.dp)
            Row(Modifier.fillMaxWidth().heightIn(min = 48.dp).clickable { nav.push(Route.Area(b.id, AreaKind.Rooms)) }.padding(horizontal = 16.dp, vertical = 12.dp),
                verticalAlignment = Alignment.CenterVertically) {
                Text("Räume", style = MaterialTheme.typography.bodyMedium, color = tones.textSecondary, modifier = Modifier.weight(1f))
                Text(it, style = MaterialTheme.typography.bodyMedium, textAlign = TextAlign.End, modifier = Modifier.weight(1.4f).padding(start = 8.dp))
                Icon(Icons.AutoMirrored.Filled.KeyboardArrowRight, null, tint = tones.textTertiary, modifier = Modifier.padding(start = 4.dp))
            }
        }
        if (fields.size > 4) {
            RowDivider(16.dp)
            RowItem(if (expanded) "Weniger anzeigen" else "Alle Kennwerte (${fields.size})", titleColor = MaterialTheme.colorScheme.primary,
                onClick = { expanded = !expanded }, trailing = {
                    Icon(if (expanded) Icons.Filled.ExpandLess else Icons.Filled.ExpandMore, null, tint = MaterialTheme.colorScheme.primary)
                })
        }
    }
}

/** Bezeichnung links (sekundär), Wert rechts (betont): ruhige Tabellenzeile. */
@Composable
private fun KeyValueRow(label: String, value: String) {
    Row(Modifier.fillMaxWidth().heightIn(min = 48.dp).padding(horizontal = 16.dp, vertical = 12.dp), verticalAlignment = Alignment.CenterVertically) {
        Text(label, style = MaterialTheme.typography.bodyMedium, color = tones.textSecondary, modifier = Modifier.weight(1f))
        Text(value, style = MaterialTheme.typography.bodyMedium, textAlign = TextAlign.End, modifier = Modifier.weight(1.4f).padding(start = 8.dp))
    }
}

/**
 * Ablauf als Fortschritt aus vorhandenen Daten: Erstbegehung, Estrich einbringen, Belegreife, Abnahme. Erledigte Schritte
 * mit Haken, der nächste offene hervorgehoben mit „Erfassen“. Ein Tipp auf einen Schritt öffnet den passenden Bereich.
 */
@Composable
private fun Workflow(session: Session, b: Bauakte, viewOnly: Boolean) {
    val nav = LocalNav.current
    val modules by session.modules.collectAsState()
    val progress by session.local(b.id, modules) {
        val s = session.service
        SiteWorkflow.of(b, s.inspections(b.id), s.rooms(b.id), s.acceptances(b.id),
            inspectionsOn = modules.isOn(AppModules.INSPECTIONS), acceptanceOn = modules.isOn(AppModules.ACCEPTANCE))
    }
    val next = progress.next
    SectionHeader("Ablauf")
    SectionCard {
        // Fortschrittsbalken in Schritten.
        Row(Modifier.fillMaxWidth().padding(start = 16.dp, end = 16.dp, top = 16.dp), verticalAlignment = Alignment.CenterVertically) {
            Text(progress.summary, style = MaterialTheme.typography.labelLarge, color = tones.textSecondary, modifier = Modifier.weight(1f))
        }
        Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 8.dp), horizontalArrangement = Arrangement.spacedBy(4.dp)) {
            progress.steps.forEach { step ->
                Box(Modifier.weight(1f).height(4.dp).clip(RoundedCornerShape(2.dp)).background(when {
                    step.isDone -> tones.success
                    step.state == SiteWorkflow.State.InProgress -> MaterialTheme.colorScheme.primary
                    else -> tones.fillStrong
                }))
            }
        }
        progress.steps.forEachIndexed { index, step ->
            if (index > 0) RowDivider(56.dp)
            val isNext = step == next
            val area = when (step.kind) {
                SiteWorkflow.Kind.InitialInspection -> AreaKind.InitialInspection
                SiteWorkflow.Kind.Laying -> AreaKind.Rooms
                SiteWorkflow.Kind.Readiness, SiteWorkflow.Kind.Acceptance -> AreaKind.Protocols
            }
            val task = when (step.kind) {
                SiteWorkflow.Kind.InitialInspection -> SiteTask.Inspection
                SiteWorkflow.Kind.Laying -> SiteTask.Diary.takeIf { modules.isOn(AppModules.DIARY) }
                SiteWorkflow.Kind.Readiness -> SiteTask.Measurement
                SiteWorkflow.Kind.Acceptance -> SiteTask.Acceptance
            }
            Row(Modifier.fillMaxWidth().heightIn(min = 56.dp).clickable { nav.push(Route.Area(b.id, area)) }
                .padding(start = 16.dp, end = 12.dp, top = 8.dp, bottom = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                Icon(when (step.state) {
                    SiteWorkflow.State.Done -> Icons.Filled.CheckCircle
                    SiteWorkflow.State.Skipped -> Icons.Filled.RemoveCircleOutline
                    SiteWorkflow.State.InProgress -> Icons.Filled.Timelapse
                    SiteWorkflow.State.Open -> Icons.Filled.RadioButtonUnchecked
                }, when (step.state) {
                    SiteWorkflow.State.Done -> "Erledigt"
                    SiteWorkflow.State.Skipped -> "Entfällt"
                    SiteWorkflow.State.InProgress -> "In Arbeit"
                    SiteWorkflow.State.Open -> "Offen"
                }, tint = when (step.state) {
                    SiteWorkflow.State.Done -> tones.success
                    SiteWorkflow.State.InProgress -> MaterialTheme.colorScheme.primary
                    else -> tones.textTertiary
                }, modifier = Modifier.size(24.dp))
                Spacer(Modifier.width(16.dp))
                Column(Modifier.weight(1f)) {
                    Text(step.kind.title, style = MaterialTheme.typography.bodyLarge, fontWeight = if (isNext) FontWeight.SemiBold else FontWeight.Normal,
                        color = if (step.isDone) tones.textSecondary else MaterialTheme.colorScheme.onSurface)
                    Text(step.detail, style = MaterialTheme.typography.bodySmall, color = tones.textSecondary)
                }
                if (isNext && !viewOnly && task != null) {
                    FilledTonalButton(onClick = { nav.captureTask = task.with(b.id) }, modifier = Modifier.heightIn(min = 40.dp),
                        contentPadding = androidx.compose.foundation.layout.PaddingValues(horizontal = 12.dp)) {
                        Text("Erfassen")
                    }
                } else {
                    Icon(Icons.AutoMirrored.Filled.KeyboardArrowRight, null, tint = tones.textTertiary)
                }
            }
        }
    }
}

@Composable
private fun OpenPoints(session: Session, id: String) {
    val nav = LocalNav.current
    val tasks by session.local(id) { session.service.tasks(id) }
    val open = tasks.filter { it.isOpen }.sortedWith(compareBy({ it.kind != WorkItemKind.Defect }, { it.dueDate ?: LocalDate.MAX }))
    SectionHeader("Offene Aufgaben und Mängel", action = if (open.isEmpty()) null else "Alle (${open.size})", onAction = { nav.push(Route.Area(id, AreaKind.Tasks)) })
    SectionCard {
        if (open.isEmpty()) RowItem("Keine offenen Punkte", icon = Icons.Filled.CheckCircle, iconTint = tones.success)
        open.take(3).forEachIndexed { index, task ->
            if (index > 0) RowDivider(60.dp)
            TaskRow(task, session)
        }
    }
}

/** Aufgabe bzw. Mangel mit Abhaken (wie WorkItemViews auf iOS). */
@Composable
fun TaskRow(task: Task, session: Session, subtitle: String? = null) {
    val today = LocalDate.now(Dates.zone)
    val haptics = androidx.compose.ui.platform.LocalHapticFeedback.current
    Row(Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 10.dp), verticalAlignment = Alignment.Top) {
        IconButton(enabled = !LocalViewOnly.current || task.assigneeUserId.equals(session.user.id, ignoreCase = true), onClick = {
            haptics.performHapticFeedback(if (task.isOpen) androidx.compose.ui.hapticfeedback.HapticFeedbackType.Confirm
                else androidx.compose.ui.hapticfeedback.HapticFeedbackType.ToggleOff)
            session.service.setTaskStatus(task, if (task.isOpen) WorkItemStatus.Done else WorkItemStatus.Open)
        }, modifier = Modifier.size(48.dp)) {
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
                task.number?.let { Text(it, style = MaterialTheme.typography.labelLarge, color = tones.textSecondary) }
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

/** Vorschaubild mit Ladezustand und Übertragungskennzeichen. */
@Composable
@OptIn(androidx.compose.foundation.ExperimentalFoundationApi::class)
fun Thumbnail(file: FileItem, session: Session, size: Dp, modifier: Modifier = Modifier, onLongClick: (() -> Unit)? = null, onClick: () -> Unit) {
    val bitmap by produceState<android.graphics.Bitmap?>(null, file.id, file.isStoredOnServer) { value = session.thumbnail(file) }
    Box(modifier.size(size).clip(RoundedCornerShape(16.dp)).background(tones.fill)
        .then(if (onLongClick != null) Modifier.combinedClickable(onClick = onClick, onLongClick = onLongClick) else Modifier.clickable(onClick = onClick))) {
        bitmap?.let { Image(it.asImageBitmap(), file.displayName, contentScale = ContentScale.Crop, modifier = Modifier.fillMaxSize()) }
        if (file.isVideo) Icon(Icons.Filled.PlayCircle, "Video", tint = androidx.compose.ui.graphics.Color.White, modifier = Modifier.align(Alignment.Center).size(36.dp))
        Box(Modifier.align(Alignment.TopEnd).padding(6.dp)) { SyncMark(file.syncState) }
    }
}

/** Was die Kolonne zuerst braucht: Estrichart, Dicke, Fläche, Termin – danach alles in der Reihenfolge des Formulars. */
internal fun keyDataFirst(fields: List<Pair<String, String>>): List<Pair<String, String>> =
    fields.sortedBy { keyDataRank(it.first) } // stabil: gleiche Stufe behält die Formular-Reihenfolge

/** 0 Estrichart, 1 Dicke, 2 Fläche, 3 Termin, 4 alles andere. */
internal fun keyDataRank(label: String): Int {
    val text = label.lowercase()
    return when {
        "estrichart" in text -> 0
        text == "dicke" || "nenndicke" in text || "estrichdicke" in text -> 1
        text == "fläche" || "gesamtfläche" in text -> 2
        "termin" in text -> 3
        else -> 4
    }
}


/** Fotos und Dokumentation: neueste Fotos als Streifen, darunter die Bereiche mit Zähler. */
@Composable
private fun Documentation(session: Session, b: Bauakte) {
    val nav = LocalNav.current
    val photos by session.local(b.id) { session.service.photos(b.id) }
    val counts by session.local(b.id) {
        val s = session.service
        mapOf(
            AreaKind.Notes to s.notes(b.id).size, AreaKind.Photos to s.photos(b.id).size, AreaKind.Documents to s.documents(b.id).size,
            AreaKind.Tasks to s.tasks(b.id).count { it.isOpen }, AreaKind.Diary to s.diary(b.id).size,
            AreaKind.InitialInspection to s.inspections(b.id).count { it.isInitial },
            AreaKind.Protocols to s.inspections(b.id).count { it.isCmMeasurement } + s.acceptances(b.id).size, AreaKind.Rooms to s.rooms(b.id).size,
            AreaKind.Contacts to s.parties(b.id).size + (if (b.customerId != null) 1 else 0),
        )
    }
    val modules by session.modules.collectAsState()
    SectionHeader("Fotos und Dokumentation", action = if (photos.isEmpty()) null else "Alle Fotos (${photos.size})",
        onAction = { nav.push(Route.Area(b.id, AreaKind.Photos)) })
    SectionCard {
        if (photos.isNotEmpty()) {
            LazyRow(contentPadding = androidx.compose.foundation.layout.PaddingValues(16.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                items(photos.take(12), key = { it.id }) { file -> Thumbnail(file, session, 88.dp) { nav.push(Route.Photo(file.id)) } }
            }
            RowDivider(16.dp)
        }
        val areas = listOf(AreaKind.Photos, AreaKind.Documents, AreaKind.Diary, AreaKind.Notes, AreaKind.Protocols, AreaKind.InitialInspection, AreaKind.Rooms)
            .filter { it.isAvailable(modules) }
        areas.forEachIndexed { index, area ->
            if (index > 0) RowDivider()
            RowItem(area.title, icon = area.icon, iconTint = tones.textSecondary, value = counts[area]?.takeIf { it > 0 }?.toString(), showChevron = true,
                onClick = { nav.push(Route.Area(b.id, area)) })
        }
    }
}

/** Kontakte: eingeklappt eine Zeile mit den Namen, aufgeklappt je Kontakt Rolle und Anruf. */
@Composable
private fun ContactSection(b: Bauakte, session: Session) {
    val context = LocalContext.current
    val nav = LocalNav.current
    val contacts = siteContacts(b, session)
    var expanded by rememberSaveable(b.id) { mutableStateOf(false) }
    SectionHeader("Kontakte (${contacts.size})", action = "Alle Beteiligten", onAction = { nav.push(Route.Area(b.id, AreaKind.Contacts)) })
    SectionCard {
        if (contacts.isEmpty()) {
            RowItem("Keine Kontakte hinterlegt", icon = Icons.Outlined.Contacts, iconTint = tones.textSecondary)
            return@SectionCard
        }
        if (!expanded) {
            RowItem(contacts.joinToString(", ") { it.name }, icon = Icons.Outlined.Contacts, iconTint = tones.textSecondary,
                subtitle = contacts.map { it.role.substringBefore(',') }.distinct().joinToString(", "),
                onClick = { expanded = true }, trailing = { Icon(Icons.Filled.ExpandMore, "Kontakte aufklappen", tint = tones.textSecondary) })
            return@SectionCard
        }
        contacts.forEachIndexed { index, contact ->
            if (index > 0) RowDivider()
            RowItem(contact.name, icon = contact.icon, iconTint = tones.textSecondary, subtitle = listOfNotNull(contact.role, contact.phone).joinToString(", "),
                onClick = contact.open, trailing = {
                    contact.phone?.let { phone -> CircleAction(Icons.Filled.Phone, "${contact.name} anrufen", { Links.call(context, phone) }) }
                })
        }
        RowDivider(16.dp)
        RowItem("Einklappen", titleColor = MaterialTheme.colorScheme.primary, onClick = { expanded = false },
            trailing = { Icon(Icons.Filled.ExpandLess, null, tint = MaterialTheme.colorScheme.primary) })
    }
}

/** Beschreibung (weitere Freitexte) und Verlauf (neueste Einträge im Bautagebuch). */
@Composable
private fun History(session: Session, b: Bauakte, viewOnly: Boolean) {
    val nav = LocalNav.current
    FreeTexts(session, b, viewOnly, important = false)
    val diary by session.local(b.id) { session.service.diary(b.id).sortedByDescending { it.day ?: LocalDate.MIN } }
    if (diary.isEmpty()) return
    SectionHeader("Verlauf", action = "Bautagebuch", onAction = { nav.push(Route.Area(b.id, AreaKind.Diary)) })
    SectionCard {
        diary.take(3).forEachIndexed { index, entry ->
            if (index > 0) RowDivider(16.dp)
            Column(Modifier.fillMaxWidth().clickable { nav.push(Route.Area(b.id, AreaKind.Diary)) }.padding(horizontal = 16.dp, vertical = 12.dp)) {
                Text(listOfNotNull(entry.day?.let { "${Dates.weekdayShort(it)}, ${Dates.date(it)}" }, entry.crew, entry.weather?.label).joinToString(", "),
                    style = MaterialTheme.typography.labelLarge, color = tones.textSecondary)
                Text(entry.text.ifBlank { "Ohne Text" }, style = MaterialTheme.typography.bodyMedium, maxLines = 2, overflow = TextOverflow.Ellipsis)
            }
        }
    }
}

/** Kennwerte für die Einsatzkarte auf Heute: Estrichart, Dicke, Fläche laut Basisdaten. */
fun Session.keyFacts(b: Bauakte): List<Pair<String, String>> {
    val schema = service.schema("bauakte") ?: return emptyList()
    val lists = service.valueLists()
    return keyDataFirst(schema.groups.flatMap { it.fields }.filter { it.type != "textarea" }.mapNotNull { field ->
        field.display(b.project[field.path.removePrefix("project.")], lists)?.let { field.label to it }
    }).filter { keyDataRank(it.first) <= 2 }
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

/**
 * Bauakte, die nicht auf dem Gerät liegt: Alle sehen alle Bauakten, offline liegen aber nur zugeteilte. Kopf online mit
 * „Nur ansehen“ und „Mich als Beteiligten eintragen“; danach wird sie vollständig geladen und ist bearbeitbar.
 */
@Composable
private fun OnlineBauakteScreen(id: String, showBack: Boolean) {
    val session = LocalSession.current
    val model = LocalModel.current
    val scope = rememberCoroutineScope()
    var working by remember { mutableStateOf(false) }
    var tried by remember { mutableStateOf(false) }
    var remote by remember { mutableStateOf<de.estrichone.core.BauakteDetail?>(null) }
    LaunchedEffect(id) {
        // Erst versuchen, sie aufs Gerät zu laden (eingeplant oder gerade zugeteilt), sonst den Kopf online holen.
        session.synchronize()
        if (session.service.bauakte(id) == null) remote = runCatching { withContext(Dispatchers.IO) { session.api.bauakte(id) } }.getOrNull()
        tried = true
    }
    ScreenScaffold(remote?.title ?: "Bauakte", showBack = showBack) { padding ->
        val r = remote
        if (r == null) {
            EmptyState(Icons.Outlined.Folder, if (tried) "Bauakte nicht verfügbar" else "Bauakte wird geladen …",
                if (tried) "Ohne Verbindung lässt sie sich nicht laden. Bitte später erneut versuchen." else null, Modifier.padding(padding)) {
                if (tried) TextButton(onClick = { scope.launch { session.synchronize() } }) { Text("Jetzt abgleichen") }
            }
            return@ScreenScaffold
        }
        Column(Modifier.padding(padding).padding(16.dp).readable(), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Text(r.number ?: "ohne Nummer", style = MaterialTheme.typography.bodyMedium, color = tones.textSecondary)
            Text(r.title, style = MaterialTheme.typography.headlineSmall)
            listOfNotNull(r.street, listOfNotNull(r.postalCode, r.city).joinToString(" ").ifBlank { null }).joinToString(", ").ifBlank { null }?.let {
                Secondary(it)
            }
            Banner(BauakteNotAssignedException.MESSAGE, Icons.Outlined.Visibility, MaterialTheme.colorScheme.primary, tones.infoSoft)
            if (r.status != BauakteStatus.Cancelled) {
                Button(onClick = {
                    working = true
                    scope.launch {
                        runCatching { session.joinBauakte(id) }
                            .onSuccess { model.message("Als Beteiligter eingetragen. Bearbeiten ist freigeschaltet.") }
                            .onFailure { model.message(it.message ?: "Eintragen nicht möglich. Bitte mit Verbindung erneut versuchen.") }
                        working = false
                    }
                }, enabled = !working, modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp)) { Text(BauakteNotAssignedException.JOIN) }
            }
            Secondary("Auf dem Gerät liegen nur Bauakten, denen Sie zugeteilt sind. Nach dem Eintragen wird die Bauakte vollständig geladen, auch für unterwegs ohne Verbindung.")
        }
    }
}
