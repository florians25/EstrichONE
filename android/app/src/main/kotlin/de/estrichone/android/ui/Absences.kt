package de.estrichone.android.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.selection.toggleable
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.outlined.Celebration
import androidx.compose.material.icons.outlined.EditCalendar
import androidx.compose.material.icons.outlined.PersonOutline
import androidx.compose.material3.pulltorefresh.PullToRefreshBox
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import de.estrichone.core.AbsenceHoliday
import de.estrichone.core.EstrichOneApi
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.CalendarMonth
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.WarningAmber
import androidx.compose.material.icons.outlined.EventAvailable
import androidx.compose.material.icons.outlined.Verified
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.DatePicker
import androidx.compose.material3.DatePickerDialog
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ExposedDropdownMenuBox
import androidx.compose.material3.ExposedDropdownMenuDefaults
import androidx.compose.material3.ExtendedFloatingActionButton
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.MenuAnchorType
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.rememberDatePickerState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import de.estrichone.core.AbsenceDraft
import de.estrichone.core.AbsencePreview
import de.estrichone.core.AbsenceRequestItem
import de.estrichone.core.AbsenceText
import de.estrichone.core.ApiException
import de.estrichone.core.Dates
import de.estrichone.core.VacationAccount
import de.estrichone.core.cancelAbsence
import de.estrichone.core.decideAbsence
import de.estrichone.core.myVacation
import de.estrichone.core.pendingAbsenceRequests
import de.estrichone.core.previewAbsence
import de.estrichone.core.requestAbsence
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneOffset

/**
 * Urlaub und Abwesenheit (wie AbsenceOverviewView auf iOS): Urlaubskonto mit Verfall des Resturlaubs, eigene Anträge mit
 * Stornieren, Urlaub beantragen mit Vorschau, für Genehmiger die offenen Anträge. Nur online; der Server rechnet.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun AbsencesScreen() {
    val session = LocalSession.current
    val nav = LocalNav.current
    val scope = rememberCoroutineScope()
    val haptics = LocalHapticFeedback.current
    val mine = rememberRefreshable("hr/me") { session.api.myVacation() }
    var cancelling by remember { mutableStateOf<AbsenceRequestItem?>(null) }
    var error by remember { mutableStateOf<String?>(null) }
    val data = mine.value
    ScreenScaffold("Urlaub und Abwesenheit", floating = {
        if (data != null) {
            ExtendedFloatingActionButton(onClick = { nav.push(Route.AbsenceRequest()) }, icon = { Icon(Icons.Filled.Add, null) }, text = { Text("Urlaub beantragen") })
        }
    }) { padding ->
        LoadView(mine.load) {
            val m = data ?: return@LoadView
            PullToRefreshBox(isRefreshing = mine.refreshing && data != null, onRefresh = { mine.refresh() }, modifier = Modifier.fillMaxSize()) {
            LazyColumn(contentPadding = padding.content(), verticalArrangement = Arrangement.spacedBy(10.dp), modifier = Modifier.readable()) {
                m.vacation?.let { account -> item(key = "account-label") { GroupLabel("Urlaubskonto ${account.year}") }; item(key = "account") { AccountCard(account) } }
                    ?: item(key = "no-account") { Banner("Für Ihr Konto ist noch kein Personalstamm angelegt. Das Büro trägt den Urlaubsanspruch ein.", Icons.Filled.CalendarMonth,
                        tones.textSecondary, tones.fill) }
                if (m.canApprove) {
                    item(key = "approvals") {
                        SectionCard {
                            RowItem("Anträge genehmigen", icon = Icons.Outlined.Verified, showChevron = true, onClick = { nav.push(Route.AbsenceApprovals) },
                                trailing = { CountBadge(m.pendingApprovals) })
                        }
                    }
                }
                item(key = "requests-label") { GroupLabel("Meine Anträge") }
                item(key = "requests") {
                    SectionCard {
                        if (m.requests.isEmpty()) RowItem("Noch keine Anträge", icon = Icons.Outlined.EventAvailable, iconTint = tones.textTertiary, iconBackground = tones.fill)
                        m.requests.forEachIndexed { index, r ->
                            if (index > 0) RowDivider(16.dp)
                            RequestRow(r, showsName = false, onCancel = if (r.canCancel) ({ cancelling = r }) else null,
                                onChange = if (r.canChange) ({ nav.push(Route.AbsenceRequest(changing = r)) }) else null)
                        }
                    }
                }
                m.absences?.takeIf { it.isNotEmpty() }?.let { absences ->
                    item(key = "absences-label") { GroupLabel("Eingetragene Abwesenheiten") }
                    item(key = "absences") {
                        SectionCard {
                            absences.forEachIndexed { index, a ->
                                if (index > 0) RowDivider(16.dp)
                                Row(Modifier.fillMaxWidth().heightIn(min = 56.dp).padding(horizontal = 16.dp, vertical = 12.dp), verticalAlignment = Alignment.CenterVertically) {
                                    Dot(a.color)
                                    Column(Modifier.weight(1f).padding(start = 10.dp)) {
                                        Text(a.typeName)
                                        Secondary(AbsenceText.range(a.from, a.to, a.halfDayStart, a.halfDayEnd))
                                    }
                                    Secondary("${AbsenceText.days(a.workingDays)} AT", Modifier.semantics { contentDescription = "${AbsenceText.days(a.workingDays)} Arbeitstage" })
                                }
                            }
                        }
                    }
                }
                upcomingHolidays(m.holidays).takeIf { it.isNotEmpty() }?.let { holidays ->
                    item(key = "holidays-label") { GroupLabel("Nächste Feiertage") }
                    item(key = "holidays") {
                        SectionCard {
                            holidays.forEachIndexed { index, h ->
                                if (index > 0) RowDivider(16.dp)
                                RowItem(h.name, icon = Icons.Outlined.Celebration, iconTint = tones.textSecondary, iconBackground = tones.fill,
                                    value = "${Dates.weekdayName(LocalDate.parse(h.date)).take(2)}, ${AbsenceText.date(h.date)}")
                            }
                        }
                    }
                }
            }
            }
        }
    }
    cancelling?.let { r ->
        ConfirmDialog(if (r.isApproved) "Abwesenheit stornieren?" else "Antrag zurückziehen?", "${r.typeName ?: "Abwesenheit"} ${r.rangeText}",
            if (r.isApproved) "Stornieren" else "Zurückziehen", {
                scope.launch {
                    try {
                        withContext(Dispatchers.IO) { session.api.cancelAbsence(r.id) }
                        haptics.performHapticFeedback(HapticFeedbackType.Confirm)
                        mine.refresh()
                    } catch (e: ApiException) {
                        haptics.performHapticFeedback(HapticFeedbackType.Reject)
                        error = e.message
                    }
                }
                cancelling = null
            }, { cancelling = null })
    }
    error?.let { ErrorDialog(it) { error = null } }
}

/** Die nächsten drei Feiertage ab heute (aus der Übersicht des Servers, Bundesland des Betriebs). */
private fun upcomingHolidays(holidays: List<AbsenceHoliday>): List<AbsenceHoliday> {
    val today = LocalDate.now(Dates.zone).toString()
    return holidays.filter { it.date >= today }.sortedBy { it.date }.take(3)
}

@Composable
private fun ErrorDialog(text: String, onDismiss: () -> Unit) {
    AlertDialog(onDismissRequest = onDismiss, title = { Text("Nicht möglich") }, text = { Text(text) },
        confirmButton = { TextButton(onClick = onDismiss) { Text("OK") } })
}

@Composable
private fun Dot(color: String?) {
    Box(Modifier.size(10.dp).clip(CircleShape).background(parseColor(color) ?: MaterialTheme.colorScheme.primary))
}

/** Konto: Rest groß, darunter Anspruch, Übertrag, genommen, geplant, beantragt; Hinweis auf verfallenden Resturlaub. */
@Composable
private fun AccountCard(account: VacationAccount) {
    SectionCard(padding = PaddingValues(16.dp)) {
        Text("Verbleibend", style = MaterialTheme.typography.labelLarge, color = tones.textSecondary)
        Text("${AbsenceText.days(account.remaining)} Tage", style = MaterialTheme.typography.headlineMedium, fontWeight = FontWeight.Bold,
            color = if (account.remaining < 0) tones.danger else MaterialTheme.colorScheme.onSurface)
        if (account.requested > 0) Secondary("nach Anträgen ${AbsenceText.days(account.afterRequests)}")
        Spacer(Modifier.height(10.dp))
        listOfNotNull(
            "Anspruch" to account.entitlement, "Übertrag Vorjahr" to account.carryOver,
            if (account.adjustment != 0.0) "Korrektur" to account.adjustment else null,
            if (account.carryOverExpired > 0) "Verfallen" to -account.carryOverExpired else null,
            "Genommen" to account.taken, "Geplant" to account.planned, "Beantragt" to account.requested,
        ).forEach { (label, value) ->
            Row(Modifier.fillMaxWidth().padding(vertical = 2.dp)) {
                Text(label, color = tones.textSecondary, modifier = Modifier.weight(1f))
                Text(AbsenceText.days(value), fontWeight = FontWeight.SemiBold)
            }
        }
        account.expiryHint?.let {
            Spacer(Modifier.height(8.dp))
            Row(verticalAlignment = Alignment.Top) {
                Icon(Icons.Filled.WarningAmber, null, tint = tones.warning, modifier = Modifier.size(18.dp))
                Text(it, style = MaterialTheme.typography.bodyMedium, color = tones.warning, modifier = Modifier.padding(start = 6.dp))
            }
        }
    }
}

/** Zeile eines Antrags: Art (Farbe), Zeitraum, Tage, Status; für Genehmiger mit Name, Rest danach und Hinweisen. */
@Composable
private fun RequestRow(r: AbsenceRequestItem, showsName: Boolean, onCancel: (() -> Unit)? = null, onChange: (() -> Unit)? = null) {
    Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 12.dp), verticalAlignment = Alignment.Top) {
        Box(Modifier.padding(top = 6.dp)) { Dot(r.typeColor) }
        Column(Modifier.weight(1f).padding(start = 10.dp), verticalArrangement = Arrangement.spacedBy(2.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(if (showsName) r.userName else r.typeName ?: "Abwesenheit", fontWeight = FontWeight.SemiBold, modifier = Modifier.weight(1f))
                val (color, background) = when (r.status) {
                    "pending" -> tones.warning to tones.warningSoft
                    "approved" -> tones.success to tones.successSoft
                    "rejected" -> tones.danger to tones.dangerSoft
                    else -> tones.textSecondary to tones.fill
                }
                Pill(r.statusText, color, background)
            }
            Secondary(listOfNotNull(if (showsName) r.typeName else null, r.rangeText, AbsenceText.workingDays(r.workingDays)).joinToString(" · "))
            if (r.replacesRequestId != null) Text("Änderungsantrag", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.primary)
            listOfNotNull(r.substituteName?.let { "Vertretung: $it" }, r.decisionComment, r.note).takeIf { it.isNotEmpty() }?.let {
                Text(it.joinToString(" · "), style = MaterialTheme.typography.bodySmall, color = tones.textSecondary)
            }
            if (showsName) r.remainingAfter?.let {
                Text("Rest danach ${AbsenceText.days(it)} Tage", style = MaterialTheme.typography.bodySmall, color = if (it < 0) tones.danger else tones.textSecondary)
            }
            r.warnings.forEach { w ->
                Row(verticalAlignment = Alignment.Top) {
                    Icon(Icons.Filled.WarningAmber, null, tint = tones.warning, modifier = Modifier.size(16.dp))
                    Text(w, style = MaterialTheme.typography.bodySmall, color = tones.warning, modifier = Modifier.padding(start = 4.dp))
                }
            }
            if (onCancel != null || onChange != null) {
                Row(horizontalArrangement = Arrangement.spacedBy(16.dp)) {
                    if (onChange != null) {
                        TextButton(onClick = onChange, contentPadding = PaddingValues(horizontal = 0.dp, vertical = 8.dp), modifier = Modifier.heightIn(min = 48.dp)) {
                            Icon(Icons.Outlined.EditCalendar, null, modifier = Modifier.size(18.dp))
                            Spacer(Modifier.width(6.dp))
                            Text("Ändern")
                        }
                    }
                    if (onCancel != null) {
                        TextButton(onClick = onCancel, contentPadding = PaddingValues(horizontal = 0.dp, vertical = 8.dp), modifier = Modifier.heightIn(min = 48.dp)) {
                            Text(if (r.isApproved) "Stornieren" else "Zurückziehen", color = tones.danger)
                        }
                    }
                }
            }
        }
    }
}

/**
 * Antrag: Art, Zeitraum, halbe Tage, Vertretung, Bemerkung; Vorschau vom Server (Arbeitstage ohne Wochenende und Feiertage, Rest,
 * Hinweise). Mit [changing] Änderungsantrag zu einem genehmigten Antrag: Der bisherige Zeitraum gilt, bis die Änderung genehmigt ist.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun AbsenceRequestScreen(changing: AbsenceRequestItem? = null) {
    val session = LocalSession.current
    val nav = LocalNav.current
    val model = LocalModel.current
    val scope = rememberCoroutineScope()
    val haptics = LocalHapticFeedback.current
    val mine = rememberRefreshable("hr/me") { session.api.myVacation() }
    val people = rememberRefreshable("directory/users") { session.api.directory() }
    val types = mine.value?.requestableTypes.orEmpty()
    val colleagues = people.value.orEmpty().filter { it.isActive && it.id != session.user.id }.sortedBy { it.displayName }
    val today = remember { LocalDate.now(Dates.zone) }
    val base = remember(changing) { changing?.let { AbsenceDraft.changing(it) } }
    // Feste ID: Wiederholen nach Verbindungsabbruch legt keinen zweiten Antrag an.
    val requestId = remember { EstrichOneApi.newId() }
    var typeId by remember { mutableStateOf(base?.typeId) }
    var from by remember { mutableStateOf(base?.let { LocalDate.parse(it.from) } ?: today.plusDays(14)) }
    var to by remember { mutableStateOf(base?.let { LocalDate.parse(it.to) } ?: today.plusDays(18)) }
    var halfDayStart by remember { mutableStateOf(base?.halfDayStart ?: false) }
    var halfDayEnd by remember { mutableStateOf(base?.halfDayEnd ?: false) }
    var substituteId by remember { mutableStateOf(base?.substituteUserId) }
    var note by remember { mutableStateOf(base?.note ?: "") }
    var preview by remember { mutableStateOf<AbsencePreview?>(null) }
    var previewError by remember { mutableStateOf<String?>(null) }
    var picking by remember { mutableStateOf<String?>(null) }
    var typeMenu by remember { mutableStateOf(false) }
    var substituteMenu by remember { mutableStateOf(false) }
    var error by remember { mutableStateOf<String?>(null) }
    var sending by remember { mutableStateOf(false) }
    LaunchedEffect(types) { if (typeId == null) typeId = (types.firstOrNull { it.key == "vacation" } ?: types.firstOrNull())?.id }
    val draft = AbsenceDraft(typeId, from.toString(), to.toString(), halfDayStart, halfDayEnd && from != to, note, substituteId, changing?.id)
    val unchanged = base != null && draft.normalized() == base.copy(halfDayEnd = base.halfDayEnd && base.from != base.to).normalized()
    LaunchedEffect(draft) {
        delay(300)
        if (draft.problem != null) return@LaunchedEffect
        try {
            preview = withContext(Dispatchers.IO) { session.api.previewAbsence(draft) }
            previewError = null
        } catch (e: ApiException) {
            preview = null
            previewError = e.message
        }
    }
    ScreenScaffold(if (changing != null) "Änderung beantragen" else "Urlaub beantragen") { padding ->
        LoadView(mine.load) {
            Column(Modifier.readable().verticalScroll(rememberScrollState()).padding(padding.content(extraBottom = 24)), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                if (changing != null) Banner(AbsenceText.changeIntro(changing), Icons.Outlined.EditCalendar, MaterialTheme.colorScheme.primary, tones.infoSoft)
                ExposedDropdownMenuBox(expanded = typeMenu, onExpandedChange = { typeMenu = it }) {
                    OutlinedTextField(types.firstOrNull { it.id == typeId }?.name ?: changing?.typeName ?: "", {}, readOnly = true, label = { Text("Art") },
                        trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(typeMenu) },
                        modifier = Modifier.fillMaxWidth().menuAnchor(MenuAnchorType.PrimaryNotEditable))
                    ExposedDropdownMenu(expanded = typeMenu, onDismissRequest = { typeMenu = false }) {
                        types.forEach { t ->
                            DropdownMenuItem(text = { Text(t.name) }, leadingIcon = { Dot(t.color) }, onClick = { typeId = t.id; typeMenu = false },
                                modifier = Modifier.heightIn(min = 48.dp))
                        }
                    }
                }
                DateRow("Von", from) { picking = "from" }
                DateRow("Bis einschließlich", to) { picking = "to" }
                SwitchRow("Erster Tag nur halb", "Abwesend ab Mittag", halfDayStart) { halfDayStart = it }
                if (from != to) SwitchRow("Letzter Tag nur halb", "Abwesend bis Mittag", halfDayEnd) { halfDayEnd = it }
                ExposedDropdownMenuBox(expanded = substituteMenu, onExpandedChange = { substituteMenu = it }) {
                    val name = colleagues.firstOrNull { it.id == substituteId }?.displayName ?: changing?.takeIf { it.substituteUserId == substituteId }?.substituteName
                    OutlinedTextField(name ?: "Keine", {}, readOnly = true, label = { Text("Vertretung (optional)") },
                        leadingIcon = { Icon(Icons.Outlined.PersonOutline, null) },
                        trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(substituteMenu) },
                        supportingText = if (people.load is Load.Failed) ({ Text("Kollegenliste nicht verfügbar.") }) else null,
                        modifier = Modifier.fillMaxWidth().menuAnchor(MenuAnchorType.PrimaryNotEditable))
                    ExposedDropdownMenu(expanded = substituteMenu, onDismissRequest = { substituteMenu = false }) {
                        DropdownMenuItem(text = { Text("Keine") }, onClick = { substituteId = null; substituteMenu = false }, modifier = Modifier.heightIn(min = 48.dp))
                        colleagues.forEach { p ->
                            DropdownMenuItem(text = { Text(p.displayName) }, leadingIcon = { Avatar(de.estrichone.core.initials(p.displayName), size = 28.dp) },
                                onClick = { substituteId = p.id; substituteMenu = false }, modifier = Modifier.heightIn(min = 48.dp))
                        }
                    }
                }
                OutlinedTextField(note, { note = it.take(500) }, label = { Text("Bemerkung (optional)") }, placeholder = { Text("z. B. Familienurlaub, Umzug") },
                    modifier = Modifier.fillMaxWidth(), minLines = 2)
                SectionCard(padding = PaddingValues(16.dp)) {
                    Text("Vorschau", style = MaterialTheme.typography.labelLarge, color = tones.textSecondary)
                    when {
                        draft.problem != null -> Text(draft.problem!!, color = tones.danger)
                        preview != null -> {
                            Text(preview!!.summary(change = changing != null))
                            preview!!.holidayText?.let { Secondary(it, Modifier.padding(top = 4.dp)) }
                            preview!!.warnings.forEach { w ->
                                Row(Modifier.padding(top = 4.dp), verticalAlignment = Alignment.Top) {
                                    Icon(Icons.Filled.WarningAmber, null, tint = tones.warning, modifier = Modifier.size(16.dp))
                                    Text(w, style = MaterialTheme.typography.bodySmall, color = tones.warning, modifier = Modifier.padding(start = 4.dp))
                                }
                            }
                        }
                        previewError != null -> Secondary(previewError!!)
                        else -> Secondary("Wird berechnet …")
                    }
                }
                Button(onClick = {
                    sending = true
                    scope.launch {
                        try {
                            withContext(Dispatchers.IO) { session.api.requestAbsence(draft, requestId) }
                            haptics.performHapticFeedback(HapticFeedbackType.Confirm)
                            session.memo.remove("hr/me")
                            model.message(if (changing != null) "Änderung beantragt. Bis zur Genehmigung gilt der bisherige Zeitraum." else "Antrag gestellt.")
                            nav.back()
                        } catch (e: ApiException) {
                            haptics.performHapticFeedback(HapticFeedbackType.Reject)
                            error = e.message
                        } finally {
                            sending = false
                        }
                    }
                }, enabled = draft.problem == null && !sending && !unchanged, modifier = Modifier.fillMaxWidth().height(56.dp)) {
                    Text(when {
                        sending -> "Wird gesendet …"
                        changing != null -> "Änderung beantragen"
                        else -> "Beantragen"
                    })
                }
                if (unchanged) Secondary("Bitte Zeitraum, Art oder Vertretung ändern.", Modifier.fillMaxWidth())
            }
        }
    }
    picking?.let { which ->
        val state = rememberDatePickerState(initialSelectedDateMillis = (if (which == "from") from else to).atStartOfDay().toInstant(ZoneOffset.UTC).toEpochMilli())
        DatePickerDialog(onDismissRequest = { picking = null }, confirmButton = {
            TextButton(onClick = {
                state.selectedDateMillis?.let {
                    val day = Instant.ofEpochMilli(it).atZone(ZoneOffset.UTC).toLocalDate()
                    if (which == "from") {
                        from = day
                        if (to < day) to = day
                    } else {
                        to = day
                    }
                }
                picking = null
            }) { Text("Übernehmen") }
        }, dismissButton = { TextButton(onClick = { picking = null }) { Text("Abbrechen") } }) { DatePicker(state) }
    }
    error?.let { ErrorDialog(it) { error = null } }
}

@Composable
private fun DateRow(label: String, day: LocalDate, onClick: () -> Unit) {
    SectionCard {
        RowItem(label, icon = Icons.Filled.CalendarMonth, value = "${Dates.weekdayName(day).take(2)}, ${AbsenceText.date(day.toString())}", onClick = onClick)
    }
}

/** Ganze Zeile schaltet (große Tippfläche), Hilfetext darunter. */
@Composable
private fun SwitchRow(label: String, help: String, checked: Boolean, onChange: (Boolean) -> Unit) {
    Row(Modifier.fillMaxWidth().heightIn(min = 56.dp).clip(RoundedCornerShape(12.dp))
        .toggleable(value = checked, role = Role.Switch, onValueChange = onChange).padding(horizontal = 4.dp),
        verticalAlignment = Alignment.CenterVertically) {
        Column(Modifier.weight(1f)) {
            Text(label)
            Text(help, style = MaterialTheme.typography.bodySmall, color = tones.textSecondary)
        }
        Switch(checked, null)
    }
}

/** Offene Anträge für Genehmiger: genehmigen mit einem Tipp, ablehnen mit Begründung. */
@Composable
fun AbsenceApprovalsScreen() {
    val session = LocalSession.current
    val scope = rememberCoroutineScope()
    val haptics = LocalHapticFeedback.current
    val items = rememberRefreshable("hr/pending") { session.api.pendingAbsenceRequests() }
    var rejecting by remember { mutableStateOf<AbsenceRequestItem?>(null) }
    var reason by remember { mutableStateOf("") }
    var error by remember { mutableStateOf<String?>(null) }
    fun decide(r: AbsenceRequestItem, approve: Boolean, comment: String?) {
        scope.launch {
            try {
                withContext(Dispatchers.IO) { session.api.decideAbsence(r.id, approve, comment) }
                haptics.performHapticFeedback(HapticFeedbackType.Confirm)
                session.memo.remove("hr/me")
                items.refresh()
            } catch (e: ApiException) {
                haptics.performHapticFeedback(HapticFeedbackType.Reject)
                error = e.message
            }
        }
    }
    ScreenScaffold("Anträge genehmigen") { padding ->
        LoadView(items.load) {
            val list = items.value ?: return@LoadView
            LazyColumn(contentPadding = padding.content(), verticalArrangement = Arrangement.spacedBy(10.dp), modifier = Modifier.readable()) {
                if (list.isEmpty()) item { EmptyState(Icons.Outlined.Verified, "Keine offenen Anträge", "Neue Anträge Ihres Teams erscheinen hier.") }
                list.forEach { r ->
                    item(key = r.id) {
                        SectionCard {
                            RequestRow(r, showsName = true)
                            if (r.replacesRequestId != null) {
                                Secondary("Bei Genehmigung entfällt der bisher genehmigte Zeitraum.", Modifier.padding(start = 36.dp, end = 16.dp, bottom = 8.dp))
                            }
                            Row(Modifier.fillMaxWidth().padding(start = 16.dp, end = 16.dp, bottom = 12.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                Button(onClick = { decide(r, true, null) }, modifier = Modifier.weight(1f).heightIn(min = 48.dp),
                                    colors = ButtonDefaults.buttonColors(containerColor = tones.success)) {
                                    Icon(Icons.Filled.Check, null)
                                    Spacer(Modifier.width(6.dp))
                                    Text("Genehmigen")
                                }
                                OutlinedButton(onClick = { reason = ""; rejecting = r }, modifier = Modifier.weight(1f).heightIn(min = 48.dp)) {
                                    Icon(Icons.Filled.Close, null, tint = tones.danger)
                                    Spacer(Modifier.width(6.dp))
                                    Text("Ablehnen", color = tones.danger)
                                }
                            }
                        }
                    }
                }
            }
        }
    }
    rejecting?.let { r ->
        AlertDialog(onDismissRequest = { rejecting = null }, title = { Text("Antrag ablehnen") }, text = {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Text("${r.userName}: ${r.typeName ?: "Abwesenheit"} ${r.rangeText}")
                OutlinedTextField(reason, { reason = it }, label = { Text("Begründung") }, modifier = Modifier.fillMaxWidth())
            }
        }, confirmButton = {
            TextButton(onClick = { decide(r, false, reason); rejecting = null }, enabled = reason.isNotBlank()) { Text("Ablehnen", color = tones.danger) }
        }, dismissButton = { TextButton(onClick = { rejecting = null }) { Text("Abbrechen") } })
    }
    error?.let { ErrorDialog(it) { error = null } }
}
