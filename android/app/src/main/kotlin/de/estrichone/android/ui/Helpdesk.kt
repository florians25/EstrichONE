package de.estrichone.android.ui

import android.net.Uri
import android.os.Build
import android.os.Environment
import android.os.StatFs
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.PickVisualMediaRequest
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.MoreVert
import androidx.compose.material.icons.filled.Star
import androidx.compose.material.icons.outlined.AddPhotoAlternate
import androidx.compose.material.icons.outlined.CloudUpload
import androidx.compose.material.icons.outlined.MenuBook
import androidx.compose.material.icons.outlined.ReportProblem
import androidx.compose.material.icons.outlined.StarOutline
import androidx.compose.material.icons.outlined.SupportAgent
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Checkbox
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import de.estrichone.android.BuildConfig
import de.estrichone.android.support.Images
import de.estrichone.core.ApiDate
import de.estrichone.core.Dates
import de.estrichone.core.HelpdeskOptions
import de.estrichone.core.HelpdeskReporter
import de.estrichone.core.KnowledgeArticleSummary
import de.estrichone.core.NewTicket
import de.estrichone.core.SyncState
import de.estrichone.core.TicketCategory
import de.estrichone.core.TicketDiagnosticsReport
import de.estrichone.core.TicketFull
import de.estrichone.core.TicketListItem
import de.estrichone.core.TicketPriority
import de.estrichone.core.agentReply
import de.estrichone.core.agentTickets
import de.estrichone.core.assignTicketToMe
import de.estrichone.core.commentTicket
import de.estrichone.core.helpdeskOptions
import de.estrichone.core.markTicketSeen
import de.estrichone.core.myTickets
import de.estrichone.core.rateTicket
import de.estrichone.core.reopenTicket
import de.estrichone.core.suggestArticles
import de.estrichone.core.ticket
import de.estrichone.core.ticketStatusLabel
import de.estrichone.core.updateTicketStatus
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/** Ampel der Bearbeitung (SLA) als kurzer Text. */
private fun slaText(t: TicketListItem): String? = when (t.slaState) {
    "overdue" -> if (t.dueKind == "response") "Antwort überfällig" else "Lösung überfällig"
    "dueSoon" -> if (t.dueKind == "response") "Antwort bald fällig" else "Lösung bald fällig"
    "paused" -> "wartet auf Melder"
    else -> null
}

/**
 * Meine Meldungen an die IT (Melder) bzw. „Mir zugewiesen“ (Bearbeitung): Status, neue Antworten, Meldungen, die noch auf
 * dem Gerät warten.
 */
@Composable
fun HelpdeskScreen() {
    val session = LocalSession.current
    val nav = LocalNav.current
    val permissions by session.permissions.collectAsState()
    val agent = "helpdesk.agent" in permissions
    var view by rememberSaveable { mutableStateOf("mine") }
    val tickets by rememberLoad(view) { if (view == "mine") session.api.myTickets() else session.api.agentView(view) }
    val pending by session.local { HelpdeskReporter(session.store, session.service).pending() }
    ScreenScaffold(if (agent) "Helpdesk" else "Meine Meldungen", floating = {
        androidx.compose.material3.ExtendedFloatingActionButton(onClick = { nav.push(Route.ReportProblem) }, icon = { Icon(Icons.Outlined.ReportProblem, null) },
            text = { Text("Problem melden") })
    }) { padding ->
        LazyColumn(contentPadding = padding.content(), modifier = Modifier.readable(), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            if (agent) item {
                val options = listOf("mine" to "Meine Meldungen", "assigned" to "Mir zugewiesen", "unassigned" to "Offen")
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    options.forEach { (key, label) -> FilterChip(view == key, { view = key }, label = { Text(label, maxLines = 1) }) }
                }
            }
            val waiting = pending.filter { it.state != SyncState.Synced || it.number == null }
            if (view == "mine" && waiting.isNotEmpty()) item {
                GroupLabel("Wird gesendet")
                SectionCard {
                    waiting.forEachIndexed { index, t ->
                        if (index > 0) RowDivider()
                        RowItem(t.title, icon = Icons.Outlined.CloudUpload, subtitle = if (t.state == SyncState.Problem) "Konnte nicht gesendet werden"
                            else "Wird gesendet, sobald eine Verbindung besteht")
                    }
                }
            }
            item {
                LoadView(tickets) {
                    val list = (tickets as Load.Done).value
                    if (list.isEmpty()) {
                        EmptyState(Icons.Outlined.SupportAgent, if (view == "mine") "Keine Meldungen" else "Nichts offen",
                            if (view == "mine") "Probleme mit App, Gerät oder Zugang hier an die IT melden." else "In dieser Ansicht gibt es gerade kein Ticket.")
                    } else {
                        SectionCard {
                            list.forEachIndexed { index, t ->
                                if (index > 0) RowDivider()
                                val sla = if (agent && view != "mine") slaText(t) else null
                                RowItem(t.title, icon = Icons.Outlined.SupportAgent,
                                    subtitle = listOfNotNull(t.number, ticketStatusLabel(t.status), sla, if (view != "mine") t.reporterDisplayName else null)
                                        .joinToString(" · "),
                                    showChevron = true, onClick = { nav.push(Route.Ticket(t.id)) }, trailing = { if (t.hasUnread) CountBadge(1) })
                            }
                        }
                    }
                }
            }
        }
    }
}

/** API-Ansicht der Bearbeitung zu den Reitern. */
private suspend fun de.estrichone.core.EstrichOneApi.agentView(view: String): List<TicketListItem> =
    agentTickets(if (view == "assigned") "mine" else "unassigned")

/**
 * Ein Ticket: Verlauf als Unterhaltung (Antworten als Sprechblasen, Ereignisse als Zeile), Antwort, Wieder öffnen, Bewertung
 * nach der Lösung. Bearbeiter (Recht helpdesk.agent) können übernehmen, Status setzen und lösen.
 */
@OptIn(ExperimentalLayoutApi::class)
@Composable
fun TicketScreen(id: String) {
    val session = LocalSession.current
    val model = LocalModel.current
    val scope = rememberCoroutineScope()
    val permissions by session.permissions.collectAsState()
    val agent = "helpdesk.agent" in permissions
    var ticket by remember { mutableStateOf<Load<TicketFull>>(Load.Loading) }
    var reply by remember { mutableStateOf("") }
    var internal by remember { mutableStateOf(false) }
    var stars by remember { mutableStateOf(0) }
    var ratingComment by remember { mutableStateOf("") }
    var resolving by remember { mutableStateOf(false) }
    var menu by remember { mutableStateOf(false) }
    LaunchedEffect(id) {
        ticket = attempt { session.api.ticket(id) }
        (ticket as? Load.Done)?.value?.let { stars = it.rating ?: 0; ratingComment = it.ratingComment ?: "" }
        runCatching { withContext(Dispatchers.IO) { session.api.markTicketSeen(id) } }
    }
    val t = (ticket as? Load.Done)?.value
    fun update(call: suspend () -> TicketFull, message: String? = null) {
        scope.launch {
            when (val result = attempt(call)) {
                is Load.Done -> { ticket = result; message?.let(model::message) }
                is Load.Failed -> model.message(result.message)
                Load.Loading -> Unit
            }
        }
    }
    ScreenScaffold(t?.summary?.number ?: "Meldung", actions = {
        if (agent && t != null) {
            IconButton(onClick = { menu = true }) { Icon(Icons.Filled.MoreVert, "Bearbeiten") }
            DropdownMenu(expanded = menu, onDismissRequest = { menu = false }) {
                if (t.summary.assigneeUserId != session.user.id) {
                    DropdownMenuItem(text = { Text("Mir zuweisen") }, onClick = { menu = false; update({ session.api.assignTicketToMe(id, session.user.id) }, "Ticket übernommen") })
                }
                DropdownMenuItem(text = { Text("In Bearbeitung") }, onClick = { menu = false; update({ session.api.updateTicketStatus(id, "inProgress") }) })
                DropdownMenuItem(text = { Text("Rückfrage an Melder") }, onClick = { menu = false; update({ session.api.updateTicketStatus(id, "waitingForReporter") }) })
                DropdownMenuItem(text = { Text("Gelöst …") }, onClick = { menu = false; resolving = true })
            }
        }
    }) { padding ->
        LoadView(ticket) {
            val full = t!!
            LazyColumn(contentPadding = padding.content(), verticalArrangement = Arrangement.spacedBy(10.dp), modifier = Modifier.readable()) {
                item {
                    Text(full.summary.title, style = MaterialTheme.typography.headlineSmall)
                    FlowRow(Modifier.padding(top = 6.dp), horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                        Pill(ticketStatusLabel(full.summary.status), MaterialTheme.colorScheme.primary, tones.infoSoft)
                        TicketCategory.of(full.summary.category)?.let { Pill(full.summary.subcategoryName?.let { s -> "${it.title} › $s" } ?: it.title, tones.textSecondary, tones.fill) }
                        TicketPriority.of(full.summary.priority).takeIf { it == TicketPriority.High || it == TicketPriority.Urgent }
                            ?.let { Pill(it.title, tones.warning, tones.warningSoft) }
                        if (agent) slaText(full.summary)?.let { Pill(it, tones.danger, tones.dangerSoft) }
                    }
                    full.summary.assigneeName?.let { Secondary("Bearbeitet von $it", Modifier.padding(top = 6.dp)) }
                }
                full.mergedIntoNumber?.let { number ->
                    item { Banner("Diese Meldung wurde mit $number zusammengeführt. Antworten erhalten Sie dort.", Icons.Outlined.SupportAgent, tones.info, tones.infoSoft) }
                }
                if (full.summary.status == "resolved" || full.summary.status == "closed") {
                    item {
                        SectionCard(padding = PaddingValues(14.dp)) {
                            Text("Lösung der IT", style = MaterialTheme.typography.titleSmall)
                            Text(full.resolution ?: "–")
                        }
                    }
                }
                val articles = full.articles.orEmpty()
                if (articles.isNotEmpty()) item {
                    SectionCard(padding = PaddingValues(14.dp)) {
                        Text("Anleitungen zu dieser Meldung", style = MaterialTheme.typography.titleSmall)
                        articles.forEach { a -> RowItem(a.title, icon = Icons.Outlined.MenuBook) }
                    }
                }
                items(full.history(includeInternal = agent), key = { it.id }) { e ->
                    if (e.isMessage) {
                        val mine = e.actorUserId == session.user.id
                        Row(Modifier.fillMaxWidth(), horizontalArrangement = if (mine) Arrangement.End else Arrangement.Start) {
                            Column(Modifier.fillMaxWidth(0.86f).clip(RoundedCornerShape(16.dp))
                                .background(if (e.isInternal) tones.warningSoft else if (mine) tones.infoSoft else tones.card).padding(12.dp)) {
                                Text((e.actorName ?: "") + if (e.isInternal) " · interne Notiz" else "", style = MaterialTheme.typography.labelLarge,
                                    color = MaterialTheme.colorScheme.primary)
                                e.text?.let { Text(it) }
                                if (e.attachmentFileIds.isNotEmpty()) Secondary(if (e.attachmentFileIds.size == 1) "1 Anhang" else "${e.attachmentFileIds.size} Anhänge")
                                ApiDate.parse(e.at)?.let { Secondary(Dates.ago(it)) }
                            }
                        }
                    } else {
                        Column(Modifier.fillMaxWidth().padding(horizontal = 12.dp)) {
                            Secondary("${e.summary} · ${ApiDate.parse(e.at)?.let { Dates.ago(it) } ?: ""}")
                            e.text?.takeIf { e.kind == "rating" }?.let { Secondary("„$it“") }
                        }
                    }
                }
                if (full.canRate && !agent) item {
                    SectionCard(padding = PaddingValues(14.dp)) {
                        Text(if (full.rating == null) "Wie zufrieden sind Sie mit der Lösung?" else "Ihre Bewertung", style = MaterialTheme.typography.titleSmall)
                        Row {
                            (1..5).forEach { n ->
                                IconButton(onClick = { stars = n }) {
                                    Icon(if (n <= stars) Icons.Filled.Star else Icons.Outlined.StarOutline, "$n von 5 Sternen",
                                        tint = if (n <= stars) tones.warning else tones.textTertiary)
                                }
                            }
                        }
                        OutlinedTextField(ratingComment, { ratingComment = it }, label = { Text("Kommentar (freiwillig)") }, modifier = Modifier.fillMaxWidth())
                        Button(onClick = { update({ session.api.rateTicket(id, stars, ratingComment) }, "Danke für Ihre Bewertung.") }, enabled = stars > 0,
                            modifier = Modifier.fillMaxWidth().padding(top = 8.dp)) { Text(if (full.rating == null) "Bewerten" else "Bewertung ändern") }
                    }
                }
                item {
                    if (full.summary.status == "closed") {
                        Secondary("Diese Meldung ist abgeschlossen. Bei einem neuen Problem bitte neu melden.")
                    } else if (full.summary.status == "resolved" && !agent) {
                        OutlinedButton(onClick = { update({ session.api.reopenTicket(id) }) }, modifier = Modifier.fillMaxWidth()) { Text("Problem besteht weiter – wieder öffnen") }
                    } else {
                        Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                            DictationField(reply, { reply = it }, if (internal) "Interne Notiz" else if (agent) "Antwort an den Melder" else "Antwort an die IT", minLines = 2)
                            if (agent) Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.clickable { internal = !internal }) {
                                Checkbox(internal, { internal = it })
                                Text("Interne Notiz (für den Melder unsichtbar)")
                            }
                            Button(onClick = {
                                val text = reply
                                reply = ""
                                update({ if (agent) session.api.agentReply(id, text, internal) else session.api.commentTicket(id, text) })
                            }, enabled = reply.isNotBlank(), modifier = Modifier.fillMaxWidth()) { Text(if (internal) "Notiz speichern" else "Antworten") }
                        }
                    }
                }
            }
        }
    }
    if (resolving) {
        var resolution by remember { mutableStateOf("") }
        AlertDialog(onDismissRequest = { resolving = false }, title = { Text("Als gelöst markieren") },
            text = { OutlinedTextField(resolution, { resolution = it }, label = { Text("Lösung (sieht der Melder)") }, minLines = 3, modifier = Modifier.fillMaxWidth()) },
            confirmButton = {
                TextButton(onClick = { resolving = false; update({ session.api.updateTicketStatus(id, "resolved", resolution) }, "Ticket gelöst") },
                    enabled = resolution.isNotBlank()) { Text("Speichern") }
            },
            dismissButton = { TextButton(onClick = { resolving = false }) { Text("Abbrechen") } })
    }
}

/**
 * Problem melden (wie ReportProblemView auf iOS): Art, Unterkategorie, Priorität, Beschreibung mit Diktat, Fotos oder
 * Bildschirmfotos und – nur mit Zustimmung – technische Angaben. Die Meldung wird zuerst auf dem Gerät gespeichert und
 * gesendet, sobald eine Verbindung besteht. Passende Anleitungen aus der Wissensdatenbank erscheinen beim Schreiben.
 */
@OptIn(ExperimentalLayoutApi::class)
@Composable
fun ReportProblemScreen() {
    val session = LocalSession.current
    val nav = LocalNav.current
    val model = LocalModel.current
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    var category by rememberSaveable { mutableStateOf(TicketCategory.AppError) }
    var priority by rememberSaveable { mutableStateOf(TicketPriority.Normal) }
    var subcategory by rememberSaveable { mutableStateOf<String?>(null) }
    var title by rememberSaveable { mutableStateOf("") }
    var description by rememberSaveable { mutableStateOf("") }
    var blocked by rememberSaveable { mutableStateOf(false) }
    var withDiagnostics by rememberSaveable { mutableStateOf(true) }
    var showDiagnostics by remember { mutableStateOf(false) }
    var photos by remember { mutableStateOf<List<ByteArray>>(emptyList()) }
    var sending by remember { mutableStateOf(false) }
    var error by remember { mutableStateOf<String?>(null) }
    var options by remember { mutableStateOf<HelpdeskOptions?>(null) }
    var suggestions by remember { mutableStateOf<List<KnowledgeArticleSummary>>(emptyList()) }
    val status by session.status.collectAsState()
    LaunchedEffect(Unit) { options = (attempt { session.api.helpdeskOptions() } as? Load.Done)?.value }
    LaunchedEffect(title, description, category) {
        val q = "$title $description".trim()
        if (q.length < 6 || status.offline) return@LaunchedEffect
        delay(600)
        suggestions = (attempt { session.api.suggestArticles(q, category.raw) } as? Load.Done)?.value.orEmpty()
    }
    val picker = rememberLauncherForActivityResult(ActivityResultContracts.PickMultipleVisualMedia(HelpdeskReporter.MAX_PHOTOS)) { uris: List<Uri> ->
        scope.launch {
            val added = withContext(Dispatchers.IO) {
                uris.mapNotNull { uri -> context.contentResolver.openInputStream(uri)?.use { it.readBytes() }?.let { Images.preparePhoto(it) } }
            }
            photos = (photos + added).take(HelpdeskReporter.MAX_PHOTOS)
        }
    }
    val report = TicketDiagnosticsReport(
        appVersion = BuildConfig.VERSION_NAME, build = BuildConfig.VERSION_CODE.toString(), osVersion = Build.VERSION.RELEASE ?: "", deviceModel = Build.MODEL,
        deviceKind = if (session.isPool) "pool" else "personal", network = if (status.offline) "offline" else "verbunden", sync = status.summary,
        freeStorageBytes = runCatching { StatFs(Environment.getDataDirectory().path).availableBytes }.getOrNull(),
    )
    val subs = options?.categories?.firstOrNull { it.key == category.raw }?.subcategories.orEmpty()
    ScreenScaffold("Problem melden") { padding ->
        Column(Modifier.padding(padding).verticalScroll(rememberScrollState()).padding(16.dp).readable(), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Text("Was ist betroffen?", style = MaterialTheme.typography.titleMedium)
            FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                TicketCategory.entries.forEach { c -> FilterChip(category == c, { category = c; subcategory = null }, label = { Text(c.title) }) }
            }
            if (subs.isNotEmpty()) FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                subs.forEach { s -> FilterChip(subcategory == s.id, { subcategory = if (subcategory == s.id) null else s.id }, label = { Text(s.name) }) }
            }
            OutlinedTextField(title, { title = it }, label = { Text("Kurz: Was funktioniert nicht?") }, singleLine = true, modifier = Modifier.fillMaxWidth())
            DictationField(description, { description = it }, "Beschreibung (optional)", minLines = 4)
            if (suggestions.isNotEmpty()) SectionCard(padding = PaddingValues(14.dp)) {
                Text("Vielleicht hilft schon eine Anleitung", style = MaterialTheme.typography.titleSmall)
                suggestions.forEach { a -> RowItem(a.title, icon = Icons.Outlined.MenuBook, subtitle = a.excerpt.take(120)) }
            }
            Text("Wie dringend?", style = MaterialTheme.typography.titleSmall)
            FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                TicketPriority.entries.forEach { p -> FilterChip(priority == p, { priority = p }, label = { Text(p.title) }) }
            }
            Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.clickable { blocked = !blocked }) {
                Checkbox(blocked, { blocked = it })
                Text("Ich kann deshalb nicht weiterarbeiten")
            }
            OutlinedButton(onClick = { picker.launch(PickVisualMediaRequest(ActivityResultContracts.PickVisualMedia.ImageOnly)) },
                enabled = photos.size < HelpdeskReporter.MAX_PHOTOS, modifier = Modifier.fillMaxWidth()) {
                Icon(Icons.Outlined.AddPhotoAlternate, null)
                Spacer(Modifier.width(8.dp))
                Text(if (photos.isEmpty()) "Foto oder Bildschirmfoto anhängen" else "${photos.size} von ${HelpdeskReporter.MAX_PHOTOS} Bildern angehängt")
            }
            SectionCard(padding = PaddingValues(14.dp)) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Column(Modifier.weight(1f)) {
                        Text("Technische Angaben mitsenden", style = MaterialTheme.typography.titleSmall)
                        Secondary("Hilft der IT bei der Suche. Nie enthalten: Passwörter, Inhalte von Bauakten, Notizen, Fotos, Kunden, Adressen.")
                    }
                    Switch(withDiagnostics, { withDiagnostics = it })
                }
                if (withDiagnostics) {
                    TextButton(onClick = { showDiagnostics = !showDiagnostics }) { Text(if (showDiagnostics) "Ausblenden" else "Anzeigen, was gesendet wird") }
                    if (showDiagnostics) report.lines().forEach { (label, value) ->
                        Row(Modifier.fillMaxWidth().padding(vertical = 2.dp)) {
                            Text(label, Modifier.width(120.dp), color = tones.textSecondary)
                            Text(value)
                        }
                    }
                }
            }
            if (status.offline) Banner("Keine Verbindung: Die Meldung wird gespeichert und automatisch gesendet.", Icons.Outlined.CloudUpload, tones.warning, tones.warningSoft)
            error?.let { Text(it, color = tones.danger) }
            Button(onClick = {
                sending = true
                scope.launch {
                    val result = runCatching {
                        withContext(Dispatchers.IO) {
                            HelpdeskReporter(session.store, session.service).report(
                                NewTicket(title, description, category, priority.takeIf { it != TicketPriority.Normal || blocked }, blocked, subcategory),
                                if (withDiagnostics) report else null, photos)
                        }
                    }
                    sending = false
                    result.onSuccess {
                        session.synchronizeNow()
                        model.message(if (status.offline) "Meldung gespeichert. Sie wird gesendet, sobald eine Verbindung besteht." else "Meldung an die IT gesendet.")
                        nav.back()
                    }.onFailure { error = it.message ?: "Die Meldung konnte nicht gespeichert werden." }
                }
            }, enabled = title.isNotBlank() && !sending, modifier = Modifier.fillMaxWidth().height(52.dp)) { Text("Meldung senden") }
        }
    }
}
