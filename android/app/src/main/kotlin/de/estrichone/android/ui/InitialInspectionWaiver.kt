package de.estrichone.android.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.FactCheck
import androidx.compose.material.icons.automirrored.filled.Undo
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.unit.dp
import de.estrichone.android.Session
import de.estrichone.core.Bauakte
import de.estrichone.core.InitialInspection

/** Darf die Bauakte bearbeiten (Recht wie im Büro); der Server prüft ohnehin. */
@Composable
fun canWaiveInitialInspection(session: Session): Boolean = "bauakten.edit" in session.permissions.collectAsState().value

/** Bestätigung „Erstbegehung nicht erforderlich“ mit optionaler Begründung; wirkt sofort lokal, Übertragung per Abgleich. */
@Composable
fun WaiveInitialInspectionDialog(session: Session, bauakteId: String, onDismiss: () -> Unit) {
    val model = LocalModel.current
    var reason by remember { mutableStateOf("") }
    var error by remember { mutableStateOf<String?>(null) }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(InitialInspection.WAIVE_BUTTON) },
        text = {
            Column {
                Text(InitialInspection.CONFIRM)
                OutlinedTextField(reason, { reason = it }, label = { Text("Begründung (optional)") },
                    modifier = Modifier.fillMaxWidth().padding(top = 12.dp).testTag("waiveReasonField"))
                error?.let { Text(it, color = tones.danger) }
            }
        },
        confirmButton = {
            TextButton(onClick = {
                try {
                    session.service.waiveInitialInspection(bauakteId, reason)
                    model.message("Vermerkt: Keine Erstbegehung erforderlich")
                    onDismiss()
                } catch (e: IllegalArgumentException) {
                    error = e.message
                }
            }, modifier = Modifier.testTag("waiveConfirmButton")) { Text("Bestätigen") }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Abbrechen") } },
    )
}

/** Hinweiskarte „Erstbegehung noch nicht ausgeführt“ in der Bauakte mit „Erstbegehung nicht erforderlich“. */
@Composable
fun InitialInspectionHint(session: Session, bauakte: Bauakte) {
    val inspections by session.local(bauakte.id) { session.service.inspections(bauakte.id) }
    var waiving by remember { mutableStateOf(false) }
    if (!InitialInspection.showsMissingHint(bauakte, inspections)) return
    val canWaive = canWaiveInitialInspection(session)
    // Text oben, Aktion darunter (wie die Hinweiskarte auf iOS): beide Texte sind lang und sollen nicht mitten im Wort umbrechen.
    Row(Modifier.padding(top = 16.dp).fillMaxWidth().clip(RoundedCornerShape(18.dp)).background(tones.warningSoft).padding(16.dp)
        .testTag("initialInspectionHint"), verticalAlignment = Alignment.Top) {
        Icon(Icons.AutoMirrored.Filled.FactCheck, null, tint = tones.warning)
        Spacer(Modifier.width(12.dp))
        Column {
            Text(InitialInspection.MISSING, style = MaterialTheme.typography.bodyLarge)
            if (canWaive) {
                Text(InitialInspection.WAIVE_BUTTON, color = MaterialTheme.colorScheme.primary, style = MaterialTheme.typography.labelLarge,
                    modifier = Modifier.padding(top = 4.dp).clip(RoundedCornerShape(8.dp)).clickable { waiving = true }.padding(vertical = 6.dp))
            }
        }
    }
    if (waiving) WaiveInitialInspectionDialog(session, bauakte.id) { waiving = false }
}

/** Vermerk „Keine Erstbegehung erforderlich (Name, Datum, Begründung)“ mit „Rückgängig“. */
@Composable
fun InitialInspectionWaiverCard(session: Session, bauakte: Bauakte) {
    val model = LocalModel.current
    SectionCard(Modifier.testTag("initialInspectionWaiver"), padding = androidx.compose.foundation.layout.PaddingValues(16.dp)) {
        Row(verticalAlignment = Alignment.Top) {
            Icon(Icons.Filled.CheckCircle, null, tint = tones.textSecondary)
            Spacer(Modifier.width(12.dp))
            Text(bauakte.initialInspectionWaiverText ?: InitialInspection.WAIVED_TITLE, style = MaterialTheme.typography.bodyLarge)
        }
        if (canWaiveInitialInspection(session)) {
            TextButton(onClick = {
                session.service.resumeInitialInspection(bauakte.id)
                model.message("Vermerk aufgehoben. Die Erstbegehung ist wieder offen.")
            }, modifier = Modifier.testTag("undoWaiverButton")) {
                Icon(Icons.AutoMirrored.Filled.Undo, null)
                Spacer(Modifier.width(6.dp))
                Text(InitialInspection.UNDO)
            }
        }
    }
}
