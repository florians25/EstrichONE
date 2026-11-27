package de.estrichone.android.ui

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Warning
import androidx.compose.material.icons.outlined.Description
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import de.estrichone.android.Session
import de.estrichone.android.support.Links
import de.estrichone.core.ApiException
import de.estrichone.core.Bauakte
import de.estrichone.core.BauakteLexware
import de.estrichone.core.Lexware
import de.estrichone.core.LexwareContactCandidate
import de.estrichone.core.LexwareContactCheck
import de.estrichone.core.LexwareTone
import de.estrichone.core.LexwareVoucher
import de.estrichone.core.bauakteLexware
import de.estrichone.core.checkLexwareContact
import de.estrichone.core.importLexwareContact
import de.estrichone.core.lexwareVoucherPdf
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

@Composable
private fun LexwareTone.color(): Color = when (this) {
    LexwareTone.Success -> tones.success
    LexwareTone.Warning -> tones.warning
    LexwareTone.Danger -> tones.danger
    LexwareTone.Info -> MaterialTheme.colorScheme.primary
    LexwareTone.Neutral -> tones.textSecondary
}

@Composable
private fun LexwareRow(label: String, value: String, note: String? = null, valueColor: Color = Color.Unspecified) {
    Row(Modifier.fillMaxWidth().heightIn(min = 48.dp).padding(horizontal = 16.dp, vertical = 10.dp), verticalAlignment = Alignment.CenterVertically) {
        Text(label, style = MaterialTheme.typography.bodyMedium, color = tones.textSecondary, modifier = Modifier.weight(1f))
        Column(Modifier.weight(1.4f).padding(start = 8.dp), horizontalAlignment = Alignment.End) {
            Text(value, style = MaterialTheme.typography.bodyMedium, textAlign = TextAlign.End, color = valueColor)
            note?.let { Text(it, style = MaterialTheme.typography.bodySmall, color = tones.textSecondary, textAlign = TextAlign.End) }
        }
    }
}

/**
 * Lexware Office in der Bauakte (nur lesend, nur online): Kontakt in Lexware Office, Abgleichstand, Summenzeile und die
 * zugeordneten Belege. Antippen eines Belegs öffnet das PDF, wenn Lexware Office eines bereitstellt. Erscheint nur mit
 * eingeschaltetem Modul, dem Recht, Rechnungen und Angebote zu sehen, und verknüpftem Kunden. Keine Zuordnung, keine Bearbeitung.
 */
@Composable
fun LexwareSection(session: Session, b: Bauakte) {
    val modules by session.modules.collectAsState()
    val permissions by session.permissions.collectAsState()
    if (!Lexware.visible(modules, permissions)) return
    var data by remember(b.id) { mutableStateOf<BauakteLexware?>(null) }
    LaunchedEffect(b.id) { data = runCatching { session.api.bauakteLexware(b.id) }.getOrNull() }
    val contact = data?.contact ?: return
    val vouchers = data?.vouchers.orEmpty()
    val model = LocalModel.current
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val differs = "weicht von EstrichONE ab"

    SectionHeader("Lexware Office")
    SectionCard(Modifier.testTag("lexwareBlock")) {
        LexwareRow("Kontakt in Lexware", contact.name ?: "Lexware-Kontakt", differs.takeIf { Lexware.differs(contact, "name") })
        RowDivider(16.dp)
        LexwareRow("Kundennummer in Lexware", contact.number ?: "ohne Kundennummer")
        Lexware.address(contact).takeIf { it.isNotEmpty() }?.let {
            RowDivider(16.dp)
            LexwareRow("Rechnungsanschrift", it, differs.takeIf { Lexware.differs(contact, "street", "postalCode", "city") })
        }
        contact.email?.let {
            RowDivider(16.dp)
            LexwareRow("E-Mail", it, differs.takeIf { Lexware.differs(contact, "email") })
        }
        val (stateText, tone) = Lexware.syncState(contact.sync)
        if (stateText.isNotEmpty()) {
            RowDivider(16.dp)
            LexwareRow("Abgleich", stateText, Lexware.syncedAt(contact.sync), tone.color())
        }
        data?.summary?.let {
            RowDivider(16.dp)
            LexwareRow("Belege", it)
        }
    }
    if (vouchers.isNotEmpty()) {
        SectionHeader("Rechnungen und Angebote")
        SectionCard(Modifier.testTag("lexwareVouchers")) {
            vouchers.forEachIndexed { index, voucher ->
                if (index > 0) RowDivider(16.dp)
                VoucherRow(voucher) {
                    if (!Lexware.pdfAvailable(voucher)) {
                        model.message(Lexware.DRAFT_HINT)
                    } else scope.launch {
                        val result = runCatching { session.api.lexwareVoucherPdf(b.id, voucher.id) }
                        val pdf = result.getOrNull()
                        when {
                            pdf != null -> if (!Links.open(context, pdf, Lexware.fileName(voucher), "application/pdf")) model.message("Keine App zum Öffnen von PDF gefunden.")
                            result.exceptionOrNull() is ApiException -> model.message(result.exceptionOrNull()?.message ?: Lexware.OFFLINE_HINT)
                            else -> model.message(Lexware.OFFLINE_HINT)
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun VoucherRow(voucher: LexwareVoucher, onClick: () -> Unit) {
    val (statusText, tone) = Lexware.status(voucher.status)
    Row(Modifier.fillMaxWidth().heightIn(min = 56.dp).clickable(onClick = onClick).padding(horizontal = 16.dp, vertical = 10.dp),
        verticalAlignment = Alignment.CenterVertically) {
        Icon(Icons.Outlined.Description, null, tint = if (Lexware.pdfAvailable(voucher)) MaterialTheme.colorScheme.primary else tones.textTertiary,
            modifier = Modifier.padding(end = 12.dp))
        Column(Modifier.weight(1f)) {
            Text("${Lexware.typeText(voucher.type)} ${Lexware.number(voucher)}", style = MaterialTheme.typography.bodyLarge)
            Text(Lexware.detail(voucher), style = MaterialTheme.typography.bodyMedium, color = tones.textSecondary)
        }
        Text(statusText, style = MaterialTheme.typography.labelLarge, color = tone.color(), modifier = Modifier.padding(start = 8.dp))
    }
}

/** Stand der Prüfung gegen Lexware Office im Formular „Neuer Kunde“. */
class LexwareCheckState {
    var check by mutableStateOf<LexwareContactCheck?>(null)
    /** „Trotzdem neu anlegen“: Der Nutzer legt den Kunden bewusst ohne Verknüpfung an. */
    var anyway by mutableStateOf(false)
    /** Die verbindliche Prüfung beim Klick auf Anlegen läuft. */
    var running by mutableStateOf(false)
    /** Rückfrage „Ohne Prüfung anlegen“, wenn die Prüfung nicht möglich war. */
    var askUnchecked by mutableStateOf(false)

    val hits: List<LexwareContactCandidate> get() = check?.takeIf { it.checked }?.candidates.orEmpty()
}

/**
 * Verbindliche Prüfung beim Klick auf Anlegen: erst in Lexware Office prüfen (über den Server, mit den endgültigen
 * Angaben), dann anlegen. Bei Treffern wird nicht angelegt, bis der Nutzer übernimmt oder „Trotzdem neu anlegen“ wählt.
 * Ist die Prüfung nicht möglich (keine Verbindung, Lexware Office nicht erreichbar), fragt die App nach.
 *
 * @return true, wenn angelegt werden darf.
 */
suspend fun LexwareCheckState.confirm(session: Session, name: String, company: Boolean, street: String, postalCode: String, city: String,
    phone: String, uncheckedConfirmed: Boolean = false): Boolean {
    if (!Lexware.checkActive(session.modules.value, session.permissions.value) || anyway) return true
    val request = Lexware.checkRequest(name, company, street, postalCode, city, phone) ?: return true
    running = true
    val result = runCatching { session.api.checkLexwareContact(request) }.getOrNull()
    running = false
    if (result == null || !result.checked) {
        if (uncheckedConfirmed) return true
        askUnchecked = true
        return false
    }
    check = result
    return result.candidates.isEmpty()
}

/**
 * Anzeige der Prüfung beim Anlegen eines Kunden (mit eingeschaltetem Modul, unabhängig vom Schalter „Mit Lexware Office
 * abgleichen“): Schon während der Eingabe wird entprellt gesucht; maßgeblich ist die Prüfung beim Klick auf Anlegen
 * ([confirm]). Gibt es den Kunden in Lexware Office schon, erscheint die Warnung mit den Treffern. „Aus Lexware Office
 * übernehmen“ legt den Kunden auf dem Server mit den Lexware-Daten an und verknüpft ihn; sonst entscheidet der Nutzer mit
 * „Trotzdem neu anlegen“ bewusst.
 *
 * @param onImported Kunde übernommen: ID und Name des neuen Kunden.
 * @param onUnchecked Der Nutzer hat „Ohne Prüfung anlegen“ bestätigt.
 */
@Composable
fun LexwareCustomerCheck(
    session: Session, state: LexwareCheckState, name: String, company: Boolean, street: String, postalCode: String, city: String, phone: String,
    onImported: (String, String) -> Unit, onUnchecked: () -> Unit = {},
) {
    val modules by session.modules.collectAsState()
    val permissions by session.permissions.collectAsState()
    if (!Lexware.checkActive(modules, permissions)) return
    val model = LocalModel.current
    val scope = rememberCoroutineScope()
    val request = Lexware.checkRequest(name, company, street, postalCode, city, phone)
    LaunchedEffect(request) {
        state.anyway = false
        if (request == null) {
            state.check = null
        } else {
            delay(500) // entprellt: erst nach einer kurzen Pause beim Tippen
            state.check = runCatching { session.api.checkLexwareContact(request) }.getOrNull()
        }
    }
    if (state.running) Secondary("Prüfung in Lexware Office läuft", Modifier.testTag("lexwareRunning"))
    if (state.askUnchecked) {
        androidx.compose.material3.AlertDialog(
            onDismissRequest = { state.askUnchecked = false },
            title = { Text("Prüfung in Lexware Office nicht möglich") },
            text = { Text("Die Prüfung in Lexware Office ist derzeit nicht möglich. Möchten Sie den Kunden ohne Prüfung anlegen? Gibt es ihn dort schon, entsteht ein doppelter Eintrag.") },
            confirmButton = { androidx.compose.material3.TextButton(onClick = { state.askUnchecked = false; onUnchecked() }) { Text("Ohne Prüfung anlegen") } },
            dismissButton = { androidx.compose.material3.TextButton(onClick = { state.askUnchecked = false }) { Text("Abbrechen") } },
        )
    }
    val check = state.check
    val hits = state.hits
    if (hits.isEmpty() || check == null) return

    Banner(Lexware.EXISTS_WARNING, Icons.Filled.Warning, tones.warning, tones.warningSoft, Modifier.testTag("lexwareWarning"))
    SectionCard {
        hits.forEachIndexed { index, candidate: LexwareContactCandidate ->
            if (index > 0) RowDivider(16.dp)
            val contact = candidate.contact
            when {
                contact.linkedContactId != null -> RowItem(contact.name,
                    subtitle = "${Lexware.candidateLine(candidate)}. Bereits verknüpft mit dem Kunden ${contact.linkedContactName.orEmpty()}.".trim())
                Lexware.canImport(check, candidate) -> RowItem(contact.name, subtitle = Lexware.candidateLine(candidate), value = "Aus Lexware Office übernehmen",
                    onClick = {
                        scope.launch {
                            val result = runCatching { session.api.importLexwareContact(contact.id) }
                            val id = result.getOrNull()?.takeIf { it.imported }?.contactId
                            if (id != null) {
                                session.synchronizeNow()
                                onImported(id, contact.name)
                            } else {
                                model.message(result.exceptionOrNull()?.message
                                    ?: "In EstrichONE gibt es bereits einen ähnlichen Kunden. Bitte wählen Sie den vorhandenen Kunden.")
                            }
                        }
                    })
                else -> RowItem(contact.name, subtitle = Lexware.candidateLine(candidate))
            }
        }
    }
    Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth()) {
        Column(Modifier.weight(1f)) {
            Text("Trotzdem neu anlegen")
            Secondary("Der Kunde wird ohne Verknüpfung mit Lexware Office angelegt.")
        }
        androidx.compose.material3.Switch(state.anyway, { state.anyway = it }, modifier = Modifier.testTag("lexwareAnyway"))
    }
}
