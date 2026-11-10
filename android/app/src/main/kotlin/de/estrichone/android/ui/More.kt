package de.estrichone.android.ui

import android.os.Build
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
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
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.Logout
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Apartment
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.DeleteForever
import androidx.compose.material.icons.filled.DirectionsCar
import androidx.compose.material.icons.filled.ErrorOutline
import androidx.compose.material.icons.filled.Fingerprint
import androidx.compose.material.icons.filled.Inbox
import androidx.compose.material.icons.filled.Info
import androidx.compose.material.icons.filled.Lock
import androidx.compose.material.icons.filled.Notifications
import androidx.compose.material.icons.filled.Palette
import androidx.compose.material.icons.filled.Phone
import androidx.compose.material.icons.filled.PlayCircle
import androidx.compose.material.icons.filled.Sync
import androidx.compose.material.icons.filled.SwitchAccount
import androidx.compose.material.icons.filled.Warning
import androidx.compose.material.icons.automirrored.outlined.Assignment
import androidx.compose.material.icons.outlined.CalendarMonth
import androidx.compose.material.icons.outlined.EventAvailable
import androidx.compose.material.icons.outlined.ChatBubbleOutline
import androidx.compose.material.icons.outlined.Devices
import androidx.compose.material.icons.outlined.Forum
import androidx.compose.material.icons.outlined.Inbox
import androidx.compose.material.icons.outlined.MarkEmailRead
import androidx.compose.material.icons.outlined.ReportProblem
import androidx.compose.material.icons.outlined.SupportAgent
import androidx.compose.material3.Button
import androidx.compose.material3.Checkbox
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.RadioButton
import androidx.compose.material3.Switch
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
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import de.estrichone.android.BuildConfig
import de.estrichone.android.support.Links
import de.estrichone.core.ApiDate
import de.estrichone.core.Dates
import de.estrichone.core.DispatchBoard
import de.estrichone.core.InboxNotification
import de.estrichone.core.LockDelay
import de.estrichone.core.NotificationInbox
import de.estrichone.core.SyncOutcome
import de.estrichone.core.TicketCategory
import de.estrichone.core.TicketFull
import de.estrichone.core.WorkItemKind
import de.estrichone.core.commentTicket
import de.estrichone.core.createTicket
import de.estrichone.core.deleteNotification
import de.estrichone.core.dispatch
import de.estrichone.core.markAllNotificationsRead
import de.estrichone.core.markNotificationRead
import de.estrichone.core.markTicketSeen
import de.estrichone.core.myTickets
import de.estrichone.core.notificationPreferences
import de.estrichone.core.notifications
import de.estrichone.core.reopenTicket
import de.estrichone.core.saveNotificationPreference
import de.estrichone.core.sessions
import de.estrichone.core.ticket
import de.estrichone.core.ticketStatusLabel
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.time.LocalDate

@Composable
fun MoreScreen() {
    val session = LocalSession.current
    val model = LocalModel.current
    val nav = LocalNav.current
    val status by session.status.collectAsState()
    val chat by session.chat.collectAsState()
    val unread by session.unreadNotifications.collectAsState()
    val unreadTickets by session.unreadTickets.collectAsState()
    var confirmReset by remember { mutableStateOf(false) }
    var confirmSignOut by remember { mutableStateOf(false) }
    ScreenScaffold("Mehr", root = true) { padding ->
        LazyColumn(contentPadding = padding.content(), modifier = Modifier.readable()) {
            item {
                SectionCard {
                    Row(Modifier.padding(16.dp), verticalAlignment = Alignment.CenterVertically) {
                        Avatar(de.estrichone.core.initials(session.user.displayName), 60.dp)
                        Spacer(Modifier.width(14.dp))
                        Column {
                            Text(session.user.displayName, style = MaterialTheme.typography.titleLarge)
                            Secondary("${session.user.userName} · ${if (session.isDemo) "Telefon von Max" else session.deviceName}${if (session.isPool) " · Poolgerät" else ""}")
                        }
                    }
                    RowDivider(16.dp)
                    RowItem("Synchronisation", icon = Icons.Filled.Sync, value = status.shortText, showChevron = true, onClick = { nav.push(Route.Sync) })
                }
            }
            item { GroupLabel("Kommunikation") }
            item {
                SectionCard {
                    if (chat?.isAvailable == true) {
                        RowItem("Chat", icon = Icons.Outlined.Forum, showChevron = true, onClick = { nav.push(Route.ChatList) }, trailing = { CountBadge(chat?.unreadCount ?: 0) })
                        RowDivider()
                    }
                    RowItem("Benachrichtigungen", icon = Icons.Outlined.Inbox, showChevron = true, onClick = { nav.push(Route.Inbox) }, trailing = { CountBadge(unread) })
                }
            }
            item { GroupLabel("Planung und Aufgaben") }
            item {
                SectionCard {
                    RowItem("Meine Aufgaben", icon = Icons.AutoMirrored.Outlined.Assignment, showChevron = true, onClick = { nav.push(Route.Tasks) })
                    RowDivider()
                    RowItem("Wer fährt wohin", icon = Icons.Filled.DirectionsCar, showChevron = true, onClick = { nav.push(Route.Dispatch) })
                    RowDivider()
                    RowItem("Meine Woche", icon = Icons.Outlined.CalendarMonth, showChevron = true, onClick = { nav.push(Route.Week) })
                    RowDivider()
                    RowItem("Urlaub und Abwesenheit", icon = Icons.Outlined.EventAvailable, showChevron = true, onClick = { nav.push(Route.Absences) })
                }
            }
            item { GroupLabel("Hilfe") }
            item {
                SectionCard {
                    RowItem("Problem melden", icon = Icons.Outlined.ReportProblem, onClick = { nav.push(Route.ReportProblem) })
                    RowDivider()
                    RowItem("Meine Meldungen", icon = Icons.Outlined.SupportAgent, showChevron = true, onClick = { nav.push(Route.Helpdesk) },
                        trailing = { CountBadge(unreadTickets) })
                }
            }
            item { GroupLabel("Einstellungen") }
            item {
                SectionCard {
                    RowItem("Sicherheit und Sperre", icon = Icons.Filled.Lock, showChevron = true, onClick = { nav.push(Route.Security) })
                    RowDivider()
                    RowItem("Mitteilungen", icon = Icons.Filled.Notifications, showChevron = true, onClick = { nav.push(Route.NotificationSettings) })
                    RowDivider()
                    RowItem("Darstellung", icon = Icons.Filled.Palette, showChevron = true, onClick = { nav.push(Route.Appearance) })
                    RowDivider()
                    RowItem("Info", icon = Icons.Filled.Info, value = BuildConfig.VERSION_NAME, showChevron = true, onClick = { nav.push(Route.About) })
                }
            }
            item { Spacer(Modifier.height(16.dp)) }
            item {
                SectionCard {
                    if (session.isDemo) {
                        RowItem("Demo beenden", icon = Icons.Filled.PlayCircle, iconTint = tones.danger, iconBackground = tones.dangerSoft, titleColor = tones.danger,
                            onClick = model::endDemo)
                    } else {
                        if (session.isPool) {
                            RowItem("Benutzer wechseln", icon = Icons.Filled.SwitchAccount, onClick = model::switchUser)
                            RowDivider()
                        }
                        RowItem("Abmelden", icon = Icons.AutoMirrored.Filled.Logout, iconTint = tones.danger, iconBackground = tones.dangerSoft, titleColor = tones.danger,
                            onClick = { confirmSignOut = true })
                        RowDivider()
                        RowItem("Gerät zurücksetzen", icon = Icons.Filled.DeleteForever, iconTint = tones.danger, iconBackground = tones.dangerSoft, titleColor = tones.danger,
                            subtitle = "Alle Daten und Anmeldungen von diesem Gerät löschen", onClick = { confirmReset = true })
                    }
                }
            }
        }
    }
    if (confirmSignOut) {
        val pending = !status.summary.isSettled
        ConfirmDialog("Abmelden?", if (pending) "Es sind noch ${status.summary.pendingOperations + status.summary.pendingUploads} Änderungen nicht übertragen. Sie bleiben auf dem Gerät und werden nach der nächsten Anmeldung gesendet."
            else "Die Daten bleiben verschlüsselt auf dem Gerät.", "Abmelden", model::signOut, { confirmSignOut = false })
    }
    if (confirmReset) {
        ConfirmDialog("Gerät zurücksetzen?", "Alle EstrichONE-Daten, Fotos und Anmeldungen werden von diesem Gerät gelöscht. Nicht übertragene Änderungen gehen verloren.",
            "Zurücksetzen", model::resetDevice, { confirmReset = false })
    }
}

// Helpdesk

@Composable
fun HelpdeskScreen() {
    val session = LocalSession.current
    val nav = LocalNav.current
    val tickets by rememberLoad(Unit) { session.api.myTickets() }
    ScreenScaffold("Meine Meldungen", floating = {
        androidx.compose.material3.ExtendedFloatingActionButton(onClick = { nav.push(Route.ReportProblem) }, icon = { Icon(Icons.Outlined.ReportProblem, null) },
            text = { Text("Problem melden") })
    }) { padding ->
        LoadView(tickets) {
            val list = (tickets as Load.Done).value
            LazyColumn(contentPadding = padding.content(), modifier = Modifier.readable()) {
                if (list.isEmpty()) item { EmptyState(Icons.Outlined.SupportAgent, "Keine Meldungen", "Probleme mit App, Gerät oder Zugang hier an die IT melden.") }
                else item {
                    SectionCard {
                        list.forEachIndexed { index, t ->
                            if (index > 0) RowDivider()
                            RowItem(t.title, icon = Icons.Outlined.SupportAgent, subtitle = "${t.number} · ${ticketStatusLabel(t.status)}",
                                showChevron = true, onClick = { nav.push(Route.Ticket(t.id)) }, trailing = { if (t.hasUnread) CountBadge(1) })
                        }
                    }
                }
            }
        }
    }
}

@Composable
fun TicketScreen(id: String) {
    val session = LocalSession.current
    val scope = rememberCoroutineScope()
    var ticket by remember { mutableStateOf<Load<TicketFull>>(Load.Loading) }
    var reply by remember { mutableStateOf("") }
    LaunchedEffect(id) {
        ticket = attempt { session.api.ticket(id) }
        runCatching { withContext(Dispatchers.IO) { session.api.markTicketSeen(id) } }
    }
    val t = (ticket as? Load.Done)?.value
    ScreenScaffold(t?.summary?.number ?: "Meldung") { padding ->
        LoadView(ticket) {
            val full = t!!
            LazyColumn(contentPadding = padding.content(), verticalArrangement = Arrangement.spacedBy(10.dp), modifier = Modifier.readable()) {
                item {
                    Text(full.summary.title, style = MaterialTheme.typography.headlineSmall)
                    Row(Modifier.padding(top = 6.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        Pill(ticketStatusLabel(full.summary.status), MaterialTheme.colorScheme.primary, tones.infoSoft)
                        TicketCategory.entries.firstOrNull { it.raw == full.summary.category }?.let { Pill(it.title, tones.textSecondary, tones.fill) }
                    }
                }
                full.description?.let { item { SectionCard(padding = PaddingValues(16.dp)) { Text(it) } } }
                items(full.comments) { c ->
                    val mine = c.authorUserId == session.user.id
                    Row(Modifier.fillMaxWidth(), horizontalArrangement = if (mine) Arrangement.End else Arrangement.Start) {
                        Column(Modifier.fillMaxWidth(0.86f).clip(RoundedCornerShape(16.dp)).background(if (mine) tones.infoSoft else tones.card).padding(12.dp)) {
                            Text(c.authorName ?: "", style = MaterialTheme.typography.labelLarge, color = MaterialTheme.colorScheme.primary)
                            Text(c.text)
                            ApiDate.parse(c.createdAt)?.let { Secondary(Dates.ago(it)) }
                        }
                    }
                }
                item {
                    if (full.summary.status in setOf("resolved", "closed")) {
                        OutlinedButton(onClick = { scope.launch { ticket = attempt { session.api.reopenTicket(id) } } }, modifier = Modifier.fillMaxWidth()) { Text("Wieder öffnen") }
                    } else {
                        Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                            DictationField(reply, { reply = it }, "Antwort an die IT", minLines = 2)
                            Button(onClick = { scope.launch { ticket = attempt { session.api.commentTicket(id, reply) }; reply = "" } }, enabled = reply.isNotBlank(),
                                modifier = Modifier.fillMaxWidth()) { Text("Antworten") }
                        }
                    }
                }
            }
        }
    }
}

/** Problem melden (wie ReportProblemView auf iOS): Art, Beschreibung, Diktat; nur technische Angaben, keine Inhalte. */
@OptIn(androidx.compose.foundation.layout.ExperimentalLayoutApi::class)
@Composable
fun ReportProblemScreen() {
    val session = LocalSession.current
    val nav = LocalNav.current
    val model = LocalModel.current
    val scope = rememberCoroutineScope()
    var category by remember { mutableStateOf(TicketCategory.AppError) }
    var title by remember { mutableStateOf("") }
    var description by remember { mutableStateOf("") }
    var blocked by remember { mutableStateOf(false) }
    var sending by remember { mutableStateOf(false) }
    var error by remember { mutableStateOf<String?>(null) }
    val status by session.status.collectAsState()
    ScreenScaffold("Problem melden") { padding ->
        Column(Modifier.padding(padding).verticalScroll(rememberScrollState()).padding(16.dp).readable(), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Text("Was ist passiert?", style = MaterialTheme.typography.titleMedium)
            androidx.compose.foundation.layout.FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                TicketCategory.entries.forEach { c -> FilterChip(category == c, { category = c }, label = { Text(c.title) }) }
            }
            OutlinedTextField(title, { title = it }, label = { Text("Kurz: Was funktioniert nicht?") }, singleLine = true, modifier = Modifier.fillMaxWidth())
            DictationField(description, { description = it }, "Beschreibung (optional)", minLines = 4)
            Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.clickable { blocked = !blocked }) {
                Checkbox(blocked, { blocked = it })
                Text("Ich kann deshalb nicht weiterarbeiten")
            }
            SectionCard(padding = PaddingValues(14.dp)) {
                Text("Mitgeschickt werden", style = MaterialTheme.typography.titleSmall)
                Secondary("App ${BuildConfig.VERSION_NAME} · Android ${Build.VERSION.RELEASE} · ${Build.MODEL} · Abgleich: ${status.shortText}. Keine Inhalte aus Bauakten.")
            }
            error?.let { Text(it, color = tones.danger) }
            Button(onClick = {
                sending = true
                scope.launch {
                    val result = attempt {
                        session.api.createTicket(title, description, category, blocked, null, mapOf(
                            "appVersion" to BuildConfig.VERSION_NAME, "platform" to "android", "osVersion" to Build.VERSION.RELEASE, "deviceModel" to (Build.MODEL ?: ""),
                            "deviceKind" to if (session.isPool) "pool" else "personal", "pendingOperations" to status.summary.pendingOperations.toString(),
                            "pendingUploads" to status.summary.pendingUploads.toString(),
                        ))
                    }
                    sending = false
                    when (result) {
                        is Load.Done -> { model.message("Meldung ${result.value.summary.number} wurde an die IT übermittelt."); nav.back() }
                        is Load.Failed -> error = result.message
                        Load.Loading -> Unit
                    }
                }
            }, enabled = title.isNotBlank() && !sending, modifier = Modifier.fillMaxWidth().height(52.dp)) { Text("Meldung senden") }
        }
    }
}

// Synchronisation

@Composable
fun SyncScreen() {
    val session = LocalSession.current
    val scope = rememberCoroutineScope()
    val status by session.status.collectAsState()
    val rejected by session.local { session.store.rejected() }
    ScreenScaffold("Synchronisation") { padding ->
        LazyColumn(contentPadding = padding.content(), modifier = Modifier.readable()) {
            item {
                SectionCard(padding = PaddingValues(16.dp)) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        IconTile(if (status.summary.hasProblems) Icons.Filled.ErrorOutline else Icons.Filled.CheckCircle,
                            if (status.summary.hasProblems) tones.danger else tones.success, if (status.summary.hasProblems) tones.dangerSoft else tones.successSoft, 44.dp)
                        Spacer(Modifier.width(12.dp))
                        Column(Modifier.weight(1f)) {
                            Text(when {
                                status.running -> "Wird abgeglichen …"
                                status.offline -> "Offline"
                                status.maintenanceMessage != null -> "Wartung des Servers"
                                status.summary.isSettled -> "Alles übertragen"
                                else -> "Änderungen ausstehend"
                            }, style = MaterialTheme.typography.titleMedium)
                            Secondary(status.summary.lastSuccessfulSync?.let { "Letzter Abgleich ${Dates.ago(java.time.Instant.ofEpochMilli(it))}" } ?: "Noch kein Abgleich")
                        }
                    }
                    Button(onClick = { scope.launch { session.synchronize() } }, enabled = !status.running, modifier = Modifier.fillMaxWidth().padding(top = 12.dp)) {
                        Text("Jetzt abgleichen")
                    }
                }
            }
            item { GroupLabel("Warteschlange") }
            item {
                SectionCard {
                    RowItem("Änderungen", value = status.summary.pendingOperations.toString(), icon = Icons.Filled.Sync)
                    RowDivider()
                    RowItem("Fotos und Dateien", value = status.summary.pendingUploads.toString(), icon = Icons.Outlined.Devices)
                    if (status.summary.failedUploads > 0) {
                        RowDivider()
                        RowItem("Fehlgeschlagene Uploads", value = status.summary.failedUploads.toString(), icon = Icons.Filled.ErrorOutline, iconTint = tones.danger,
                            iconBackground = tones.dangerSoft)
                    }
                }
            }
            if (rejected.isNotEmpty()) {
                item { GroupLabel("Vom Server abgelehnt") }
                item {
                    SectionCard {
                        rejected.forEachIndexed { index, op ->
                            if (index > 0) RowDivider()
                            RowItem(operationLabel(op.type), icon = Icons.Filled.ErrorOutline, iconTint = tones.danger, iconBackground = tones.dangerSoft,
                                subtitle = op.errorMessage ?: op.errorCode, trailing = { TextButton(onClick = { session.store.discard(op) }) { Text("Verwerfen") } })
                        }
                    }
                }
            }
            item {
                Secondary("Erfasste Daten werden verschlüsselt auf dem Gerät gespeichert und automatisch übertragen: nach jeder Änderung, alle 2 Minuten und im Hintergrund.",
                    Modifier.padding(16.dp))
            }
            (status.outcome as? SyncOutcome.Failed)?.let { failed -> item { Banner(failed.error.message ?: "Fehler", Icons.Filled.ErrorOutline, tones.danger, tones.dangerSoft) } }
        }
    }
}

private fun operationLabel(type: String) = when (type.substringBefore('.')) {
    "bauakte" -> "Bauakte"
    "note" -> "Notiz"
    "file" -> "Foto/Dokument"
    "workitem" -> "Aufgabe/Mangel"
    "diary" -> "Bautagebuch"
    "inspection" -> "Prüfung"
    "acceptance" -> "Abnahme"
    "room" -> "Raum"
    "contact" -> "Kunde"
    else -> type
} + when (type.substringAfter('.')) {
    "create", "announce" -> " anlegen"
    "update", "setStatus" -> " ändern"
    "delete" -> " löschen"
    else -> ""
}

// Sicherheit

@Composable
fun SecurityScreen() {
    val session = LocalSession.current
    val model = LocalModel.current
    val prefs = model.prefs
    var enabled by remember { mutableStateOf(prefs.lockEnabled) }
    var biometric by remember { mutableStateOf(prefs.biometricUnlock) }
    var delay by remember { mutableStateOf(LockDelay.entries.firstOrNull { it.seconds == prefs.lockDelaySeconds } ?: LockDelay.FifteenMinutes) }
    val sessions by rememberLoad(Unit) { session.api.sessions() }
    var changing by remember { mutableStateOf(false) }
    fun save() = model.setLock(enabled, delay, biometric)
    ScreenScaffold("Sicherheit und Sperre") { padding ->
        LazyColumn(contentPadding = padding.content(), modifier = Modifier.readable()) {
            if (session.isPool) {
                item {
                    Banner("Poolgerät: Die App sperrt beim Verlassen sofort und nach 5 Minuten ohne Bedienung. Entsperrt wird mit der persönlichen PIN.",
                        Icons.Filled.Lock, MaterialTheme.colorScheme.primary, tones.infoSoft)
                }
            } else {
                item {
                    SectionCard {
                        RowItem("App-Sperre", icon = Icons.Filled.Lock, subtitle = "Beim Öffnen mit Fingerabdruck, Gesicht oder Gerätecode entsperren",
                            trailing = { Switch(enabled, { enabled = it; save() }) })
                        if (enabled) {
                            RowDivider()
                            RowItem("Biometrie verwenden", icon = Icons.Filled.Fingerprint, subtitle = "Aus: nur Gerätecode",
                                trailing = { Switch(biometric, { biometric = it; save() }) })
                        }
                    }
                }
                if (enabled) {
                    item { GroupLabel("Sperren nach") }
                    item {
                        SectionCard {
                            LockDelay.entries.forEachIndexed { index, d ->
                                if (index > 0) RowDivider(16.dp)
                                Row(Modifier.fillMaxWidth().clickable { delay = d; save() }.padding(horizontal = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                                    RadioButton(delay == d, { delay = d; save() })
                                    Text(d.title)
                                }
                            }
                        }
                    }
                }
            }
            if (!session.isDemo) {
                item { GroupLabel("Konto") }
                item { SectionCard { RowItem("Passwort ändern", icon = Icons.Filled.Lock, showChevron = true, onClick = { changing = true }) } }
            }
            item { GroupLabel("Angemeldete Geräte und Browser") }
            item {
                LoadView(sessions) {
                    SectionCard {
                        (sessions as Load.Done).value.forEachIndexed { index, s ->
                            if (index > 0) RowDivider()
                            RowItem(s.clientName ?: s.client, icon = Icons.Outlined.Devices,
                                subtitle = listOfNotNull(if (s.current) "Dieses Gerät" else null, if (s.deviceBound) "an Gerät gebunden" else null,
                                    ApiDate.parse(s.lastSeenAt)?.let { "zuletzt ${Dates.ago(it)}" }).joinToString(" · "))
                        }
                    }
                }
            }
            item {
                Secondary("Die Anmeldung ist an dieses Gerät gebunden (Schlüssel im Android Keystore). Daten und Fotos liegen verschlüsselt auf dem Gerät. " +
                    "Die Zwei-Faktor-Anmeldung richten Sie in der Weboberfläche unter „Mein Konto“ ein.", Modifier.padding(16.dp))
            }
        }
    }
    if (changing) ChangePasswordSheet { changing = false }
}

/** Eigenes Passwort ändern (wie ChangePasswordView auf iOS). */
@Composable
private fun ChangePasswordSheet(onDismiss: () -> Unit) {
    val session = LocalSession.current
    val model = LocalModel.current
    val scope = rememberCoroutineScope()
    var current by remember { mutableStateOf("") }
    var new by remember { mutableStateOf("") }
    var repeated by remember { mutableStateOf("") }
    FormSheet("Passwort ändern", null, onDismiss, save = "Ändern", canSave = current.isNotBlank() && new.length >= 8 && new == repeated, onSave = {
        scope.launch {
            val result = attempt { session.api.changePassword(current, new) }
            model.message(if (result is Load.Done) "Passwort geändert." else (result as? Load.Failed)?.message ?: "Nicht geändert.")
        }
    }) {
        val hidden = androidx.compose.ui.text.input.PasswordVisualTransformation()
        OutlinedTextField(current, { current = it }, label = { Text("Aktuelles Passwort") }, singleLine = true, visualTransformation = hidden, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(new, { new = it }, label = { Text("Neues Passwort") }, singleLine = true, visualTransformation = hidden, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(repeated, { repeated = it }, label = { Text("Neues Passwort wiederholen") }, singleLine = true, visualTransformation = hidden,
            modifier = Modifier.fillMaxWidth(), isError = repeated.isNotEmpty() && repeated != new)
        Secondary("Mindestens 12 Zeichen. Andere Geräte bleiben angemeldet.")
    }
}

@Composable
fun NotificationSettingsScreen() {
    val session = LocalSession.current
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    var prefs by remember { mutableStateOf<Load<de.estrichone.core.NotificationPreferences>>(Load.Loading) }
    LaunchedEffect(Unit) { prefs = attempt { session.api.notificationPreferences() } }
    ScreenScaffold("Mitteilungen") { padding ->
        LoadView(prefs) {
            val items = (prefs as Load.Done).value.items
            LazyColumn(contentPadding = padding.content(), modifier = Modifier.readable()) {
                item {
                    Banner("Ohne Google-Push fragt die App das Postfach regelmäßig ab und zeigt neue Benachrichtigungen als Mitteilung.",
                        Icons.Filled.Info, MaterialTheme.colorScheme.primary, tones.infoSoft, action = "System", onAction = {
                            context.startActivity(android.content.Intent(android.provider.Settings.ACTION_APP_NOTIFICATION_SETTINGS)
                                .putExtra(android.provider.Settings.EXTRA_APP_PACKAGE, context.packageName))
                        })
                }
                items.groupBy { it.group }.forEach { (group, list) ->
                    item { GroupLabel(group) }
                    item {
                        SectionCard {
                            list.forEachIndexed { index, p ->
                                if (index > 0) RowDivider(16.dp)
                                RowItem(p.label, subtitle = p.description.ifBlank { null }, trailing = {
                                    Switch(p.delivery != "off", enabled = !p.mandatory, onCheckedChange = { on ->
                                        scope.launch { prefs = attempt { session.api.saveNotificationPreference(p.event, if (on) "push" else "off") } }
                                    })
                                })
                            }
                        }
                    }
                }
            }
        }
    }
}

@Composable
fun AppearanceScreen() {
    val model = LocalModel.current
    val look by model.look.collectAsState()
    ScreenScaffold("Darstellung") { padding ->
        LazyColumn(contentPadding = padding.content(), modifier = Modifier.readable()) {
            item { GroupLabel("Erscheinungsbild") }
            item {
                SectionCard {
                    Appearance.entries.forEachIndexed { index, a ->
                        if (index > 0) RowDivider(16.dp)
                        Row(Modifier.fillMaxWidth().clickable { model.setLook(a.name, look.second) }.padding(horizontal = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                            RadioButton(look.first == a.name, { model.setLook(a.name, look.second) })
                            Text(a.title)
                        }
                    }
                }
            }
            if (Build.VERSION.SDK_INT >= 31) {
                item { GroupLabel("Farben") }
                item {
                    SectionCard {
                        RowItem("Systemfarben (Material You)", subtitle = "Flächen nach dem Hintergrundbild; Markenfarbe und Statusfarben bleiben.",
                            trailing = { Switch(look.second, { model.setLook(look.first, it) }) })
                    }
                }
            }
            item { Secondary("Die Schriftgröße folgt der Einstellung des Geräts.", Modifier.padding(16.dp)) }
        }
    }
}

@Composable
fun AboutScreen() {
    val session = LocalSession.current
    val context = LocalContext.current
    ScreenScaffold("Info") { padding ->
        LazyColumn(contentPadding = padding.content(), modifier = Modifier.readable()) {
            item {
                SectionCard {
                    RowItem("App-Version", value = "${BuildConfig.VERSION_NAME} (${BuildConfig.VERSION_CODE})")
                    RowDivider(16.dp)
                    RowItem("Android", value = "${Build.VERSION.RELEASE} · ${Build.MODEL}")
                    RowDivider(16.dp)
                    RowItem("Server", value = if (session.isDemo) "Demo" else session.api.baseUrl.host)
                    RowDivider(16.dp)
                    RowItem("Gerät", value = if (session.isPool) "Poolgerät" else "Persönliches Gerät")
                }
            }
            item {
                SectionCard(Modifier.padding(top = 16.dp)) {
                    RowItem("Impressum und Datenschutz", showChevron = true, onClick = { Links.web(context, if (session.isDemo) "https://estrichone.de" else "${session.api.baseUrl}impressum") })
                }
            }
        }
    }
}

// Planung und Aufgaben

@Composable
fun MyTasksScreen() {
    val session = LocalSession.current
    var adding by remember { mutableStateOf(false) }
    var showDone by remember { mutableStateOf(false) }
    val tasks by session.local { session.service.myTasks() }
    val visible = tasks.filter { showDone || it.isOpen }
    ScreenScaffold("Meine Aufgaben", floating = {
        androidx.compose.material3.ExtendedFloatingActionButton(onClick = { adding = true }, icon = { Icon(Icons.Filled.Add, null) }, text = { Text("Aufgabe") })
    }) { padding ->
        LazyColumn(contentPadding = padding.content(), modifier = Modifier.readable()) {
            item {
                Row(Modifier.padding(bottom = 8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    FilterChip(!showDone, { showDone = false }, label = { Text("Offen (${tasks.count { it.isOpen }})") })
                    FilterChip(showDone, { showDone = true }, label = { Text("Alle (${tasks.size})") })
                }
            }
            if (visible.isEmpty()) item { EmptyState(Icons.AutoMirrored.Outlined.Assignment, "Keine offenen Aufgaben", "Offene Aufgaben und Mängel erscheinen hier.") }
            visible.groupBy { it.bauakteId }.forEach { (bauakteId, list) ->
                val b = bauakteId?.let { session.service.bauakte(it) }
                item { GroupLabel(b?.let { "${it.numberText} · ${it.title}" } ?: "Ohne Bauakte") }
                item { SectionCard { list.forEachIndexed { i, t -> if (i > 0) RowDivider(56.dp); TaskRow(t, session) } } }
            }
        }
    }
    if (adding) TaskSheet(null, WorkItemKind.Task) { adding = false }
}

@Composable
fun WeekScreen() {
    val session = LocalSession.current
    val nav = LocalNav.current
    val today = remember { LocalDate.now(Dates.zone) }
    val plan by produceState<de.estrichone.core.MyPlanning?>(null) { value = session.weekPlan(today) }
    ScreenScaffold("Meine Woche") { padding ->
        LazyColumn(contentPadding = padding.content(), modifier = Modifier.readable()) {
            if (plan == null) item { LoadView(Load.Loading) {} }
            (0L..6L).map { today.plusDays(it) }.forEach { day ->
                val jobs = plan?.assignments?.filter { it.day == day.toString() }.orEmpty()
                val absent = plan?.absences?.filter { it.userId == session.user.id && it.from <= day.toString() && it.to >= day.toString() }.orEmpty()
                item { GroupLabel("${Dates.weekdayName(day)}, ${Dates.dayMonth(day)}${if (day == today) " · Heute" else ""}") }
                item {
                    SectionCard {
                        when {
                            absent.isNotEmpty() -> RowItem("Abwesend", icon = Icons.Outlined.CalendarMonth, subtitle = absent.first().note ?: absent.first().kind)
                            jobs.isEmpty() -> RowItem("Nichts eingeplant", icon = Icons.Outlined.CalendarMonth, iconTint = tones.textTertiary, iconBackground = tones.fill)
                            else -> jobs.forEachIndexed { i, job ->
                                if (i > 0) RowDivider()
                                RowItem(job.bauakteTitle, icon = Icons.Filled.Apartment, subtitle = "${job.time} · ${job.activity}${job.crewName?.let { " · $it" } ?: ""}",
                                    showChevron = true, onClick = { nav.push(Route.Bauakte(job.bauakteId)) })
                            }
                        }
                    }
                }
            }
        }
    }
}

/** „Wer fährt wohin“: Baustellen des Tages mit Kolonnen, Personen und Fahrzeugen (lesend). */
@Composable
fun DispatchScreen() {
    val session = LocalSession.current
    val nav = LocalNav.current
    val context = LocalContext.current
    var day by remember { mutableStateOf(LocalDate.now(Dates.zone)) }
    val board by rememberLoad(day) { session.api.dispatch(day.toString()) }
    ScreenScaffold("Wer fährt wohin") { padding ->
        LazyColumn(contentPadding = padding.content(), verticalArrangement = Arrangement.spacedBy(10.dp), modifier = Modifier.readable()) {
            item {
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    listOf(0L, 1L, 2L).forEach { offset ->
                        val d = LocalDate.now(Dates.zone).plusDays(offset)
                        FilterChip(day == d, { day = d }, label = { Text(Dates.relativeDay(d)) })
                    }
                }
            }
            item {
                LoadView(board) {
                    val b: DispatchBoard = (board as Load.Done).value
                    Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                        if (b.sites.isEmpty()) EmptyState(Icons.Filled.DirectionsCar, "Kein Plan freigegeben", "Für diesen Tag ist noch nichts veröffentlicht.")
                        b.sites.forEach { site ->
                            val mine = site.bauakteId in b.mySiteIds
                            SectionCard(Modifier.clickable { nav.push(Route.Bauakte(site.bauakteId)) }, padding = PaddingValues(16.dp)) {
                                Row(verticalAlignment = Alignment.CenterVertically) {
                                    Text(Dates.clock(site.startTime) ?: "–", style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold)
                                    Spacer(Modifier.width(10.dp))
                                    Column(Modifier.weight(1f)) {
                                        Text(site.title, style = MaterialTheme.typography.titleMedium, maxLines = 1, overflow = TextOverflow.Ellipsis)
                                        Secondary(listOfNotNull(site.number, site.address).joinToString(" · "), maxLines = 1)
                                    }
                                    if (mine) Pill("Ich", MaterialTheme.colorScheme.primary, tones.infoSoft)
                                }
                                site.jobs.forEach { job ->
                                    Row(Modifier.padding(top = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                                        Box(Modifier.size(10.dp).clip(CircleShape).background(parseColor(job.crewColor) ?: tones.textTertiary))
                                        Spacer(Modifier.width(8.dp))
                                        Text("${job.crewName ?: ""} · ${job.activity}", style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.SemiBold)
                                    }
                                }
                                if (site.people.isNotEmpty()) Secondary(site.people.joinToString(", ") { it.name }, Modifier.padding(top = 4.dp))
                                if (site.vehicles.isNotEmpty()) Secondary("🚚 " + site.vehicles.joinToString(", ") { it.name })
                                site.warnings.forEach { Text("⚠ $it", color = tones.warning, style = MaterialTheme.typography.bodyMedium) }
                                Row(Modifier.padding(top = 8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                    site.address?.let { a -> OutlinedButton(onClick = { Links.route(context, a, site.latitude, site.longitude) }) { Text("Route") } }
                                    site.siteContactPhone?.let { p -> OutlinedButton(onClick = { Links.call(context, p) }) { Icon(Icons.Filled.Phone, null); Text(" ${site.siteContactName ?: "Anrufen"}") } }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
