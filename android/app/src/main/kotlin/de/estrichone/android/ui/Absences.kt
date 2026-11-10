package de.estrichone.android.ui

import androidx.compose.foundation.background
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
@Composable
fun AbsencesScreen() {
    val session = LocalSession.current
    val nav = LocalNav.current
    val scope = rememberCoroutineScope()
    var reload by remember { mutableStateOf(0) }
    val mine by rememberLoad(reload) { session.api.myVacation() }
    var cancelling by remember { mutableStateOf<AbsenceRequestItem?>(null) }
    var error by remember { mutableStateOf<String?>(null) }
    val data = (mine as? Load.Done)?.value
    ScreenScaffold("Urlaub und Abwesenheit", floating = {
        if (data != null) {
            ExtendedFloatingActionButton(onClick = { nav.push(Route.AbsenceRequest) }, icon = { Icon(Icons.Filled.Add, null) }, text = { Text("Urlaub beantragen") })
        }
    }) { padding ->
        LoadView(mine) {
            val m = data!!
            LazyColumn(contentPadding = padding.content(), verticalArrangement = Arrangement.spacedBy(10.dp), modifier = Modifier.readable()) {
                m.vacation?.let { account -> item { GroupLabel("Urlaubskonto ${account.year}") }; item { AccountCard(account) } }
                    ?: item { Banner("Für Ihr Konto ist noch kein Personalstamm angelegt. Das Büro trägt den Urlaubsanspruch ein.", Icons.Filled.CalendarMonth,
                        tones.textSecondary, tones.fill) }
                if (m.canApprove) {
                    item {
                        SectionCard {
                            RowItem("Anträge genehmigen", icon = Icons.Outlined.Verified, showChevron = true, onClick = { nav.push(Route.AbsenceApprovals) },
                                trailing = { CountBadge(m.pendingApprovals) })
                        }
                    }
                }
                item { GroupLabel("Meine Anträge") }
                item {
                    SectionCard {
                        if (m.requests.isEmpty()) RowItem("Noch keine Anträge", icon = Icons.Outlined.EventAvailable, iconTint = tones.textTertiary, iconBackground = tones.fill)
                        m.requests.forEachIndexed { index, r ->
                            if (index > 0) RowDivider(16.dp)
                            RequestRow(r, showsName = false, onCancel = if (r.canCancel) ({ cancelling = r }) else null)
                        }
                    }
                }
                m.absences?.takeIf { it.isNotEmpty() }?.let { absences ->
                    item { GroupLabel("Eingetragene Abwesenheiten") }
                    item {
                        SectionCard {
                            absences.forEachIndexed { index, a ->
                                if (index > 0) RowDivider(16.dp)
                                Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 12.dp), verticalAlignment = Alignment.CenterVertically) {
                                    Dot(a.color)
                                    Column(Modifier.weight(1f).padding(start = 10.dp)) {
                                        Text(a.typeName)
                                        Secondary(AbsenceText.range(a.from, a.to, a.halfDayStart, a.halfDayEnd))
                                    }
                                    Secondary("${AbsenceText.days(a.workingDays)} AT")
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
                        reload++
                    } catch (e: ApiException) {
                        error = e.message
                    }
                }
                cancelling = null
            }, { cancelling = null })
    }
    error?.let { ErrorDialog(it) { error = null } }
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
private fun RequestRow(r: AbsenceRequestItem, showsName: Boolean, onCancel: (() -> Unit)? = null) {
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
            Secondary(listOfNotNull(if (showsName) r.typeName else null, r.rangeText, "${AbsenceText.days(r.workingDays)} Arbeitstage").joinToString(" · "))
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
            if (onCancel != null) {
                TextButton(onClick = onCancel, contentPadding = PaddingValues(0.dp)) { Text(if (r.isApproved) "Stornieren" else "Zurückziehen", color = tones.danger) }
            }
        }
    }
}

/** Antrag: Art, Zeitraum, halbe Tage, Bemerkung; Vorschau vom Server (Arbeitstage ohne Wochenende und Feiertage, Rest, Hinweise). */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun AbsenceRequestScreen() {
    val session = LocalSession.current
    val nav = LocalNav.current
    val model = LocalModel.current
    val scope = rememberCoroutineScope()
    val mine by rememberLoad(Unit) { session.api.myVacation() }
    val types = (mine as? Load.Done)?.value?.requestableTypes.orEmpty()
    val today = remember { LocalDate.now(Dates.zone) }
    var typeId by remember { mutableStateOf<String?>(null) }
    var from by remember { mutableStateOf(today.plusDays(14)) }
    var to by remember { mutableStateOf(today.plusDays(18)) }
    var halfDayStart by remember { mutableStateOf(false) }
    var halfDayEnd by remember { mutableStateOf(false) }
    var note by remember { mutableStateOf("") }
    var preview by remember { mutableStateOf<AbsencePreview?>(null) }
    var previewError by remember { mutableStateOf<String?>(null) }
    var picking by remember { mutableStateOf<String?>(null) }
    var typeMenu by remember { mutableStateOf(false) }
    var error by remember { mutableStateOf<String?>(null) }
    var sending by remember { mutableStateOf(false) }
    LaunchedEffect(types) { if (typeId == null) typeId = (types.firstOrNull { it.key == "vacation" } ?: types.firstOrNull())?.id }
    val draft = AbsenceDraft(typeId, from.toString(), to.toString(), halfDayStart, halfDayEnd && from != to, note)
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
    ScreenScaffold("Urlaub beantragen") { padding ->
        LoadView(mine) {
            Column(Modifier.readable().verticalScroll(rememberScrollState()).padding(padding.content(extraBottom = 24)), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                ExposedDropdownMenuBox(expanded = typeMenu, onExpandedChange = { typeMenu = it }) {
                    OutlinedTextField(types.firstOrNull { it.id == typeId }?.name ?: "", {}, readOnly = true, label = { Text("Art") },
                        trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(typeMenu) },
                        modifier = Modifier.fillMaxWidth().menuAnchor(MenuAnchorType.PrimaryNotEditable))
                    ExposedDropdownMenu(expanded = typeMenu, onDismissRequest = { typeMenu = false }) {
                        types.forEach { t ->
                            DropdownMenuItem(text = { Text(t.name) }, leadingIcon = { Dot(t.color) }, onClick = { typeId = t.id; typeMenu = false })
                        }
                    }
                }
                DateRow("Von", from) { picking = "from" }
                DateRow("Bis einschließlich", to) { picking = "to" }
                SwitchRow("Erster Tag nur halb", halfDayStart) { halfDayStart = it }
                if (from != to) SwitchRow("Letzter Tag nur halb", halfDayEnd) { halfDayEnd = it }
                OutlinedTextField(note, { note = it }, label = { Text("Bemerkung (optional)") }, modifier = Modifier.fillMaxWidth(), minLines = 2)
                SectionCard(padding = PaddingValues(16.dp)) {
                    Text("Vorschau", style = MaterialTheme.typography.labelLarge, color = tones.textSecondary)
                    when {
                        draft.problem != null -> Text(draft.problem!!, color = tones.danger)
                        preview != null -> {
                            Text(preview!!.summary)
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
                            withContext(Dispatchers.IO) { session.api.requestAbsence(draft) }
                            model.message("Antrag gestellt.")
                            nav.back()
                        } catch (e: ApiException) {
                            error = e.message
                        } finally {
                            sending = false
                        }
                    }
                }, enabled = draft.problem == null && !sending, modifier = Modifier.fillMaxWidth().height(52.dp)) { Text("Beantragen") }
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

@Composable
private fun SwitchRow(label: String, checked: Boolean, onChange: (Boolean) -> Unit) {
    Row(Modifier.fillMaxWidth().padding(horizontal = 4.dp), verticalAlignment = Alignment.CenterVertically) {
        Text(label, modifier = Modifier.weight(1f))
        Switch(checked, onChange)
    }
}

/** Offene Anträge für Genehmiger: genehmigen mit einem Tipp, ablehnen mit Begründung. */
@Composable
fun AbsenceApprovalsScreen() {
    val session = LocalSession.current
    val scope = rememberCoroutineScope()
    var reload by remember { mutableStateOf(0) }
    val items by rememberLoad(reload) { session.api.pendingAbsenceRequests() }
    var rejecting by remember { mutableStateOf<AbsenceRequestItem?>(null) }
    var reason by remember { mutableStateOf("") }
    var error by remember { mutableStateOf<String?>(null) }
    fun decide(r: AbsenceRequestItem, approve: Boolean, comment: String?) {
        scope.launch {
            try {
                withContext(Dispatchers.IO) { session.api.decideAbsence(r.id, approve, comment) }
                reload++
            } catch (e: ApiException) {
                error = e.message
            }
        }
    }
    ScreenScaffold("Anträge genehmigen") { padding ->
        LoadView(items) {
            val list = (items as Load.Done).value
            LazyColumn(contentPadding = padding.content(), verticalArrangement = Arrangement.spacedBy(10.dp), modifier = Modifier.readable()) {
                if (list.isEmpty()) item { EmptyState(Icons.Outlined.Verified, "Keine offenen Anträge") }
                list.forEach { r ->
                    item(key = r.id) {
                        SectionCard {
                            RequestRow(r, showsName = true)
                            Row(Modifier.fillMaxWidth().padding(start = 16.dp, end = 16.dp, bottom = 12.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                Button(onClick = { decide(r, true, null) }, modifier = Modifier.weight(1f),
                                    colors = ButtonDefaults.buttonColors(containerColor = tones.success)) {
                                    Icon(Icons.Filled.Check, null)
                                    Spacer(Modifier.width(6.dp))
                                    Text("Genehmigen")
                                }
                                OutlinedButton(onClick = { reason = ""; rejecting = r }, modifier = Modifier.weight(1f)) {
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
