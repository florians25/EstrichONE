package de.estrichone.android.ui

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Groups
import androidx.compose.material.icons.outlined.Archive
import androidx.compose.material3.Button
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import de.estrichone.android.support.Links
import de.estrichone.core.TeamsTargets

// Microsoft Teams (docs/microsoft-teams.md): „In Teams öffnen“ öffnet die Teams-App, sonst Teams im Browser.

/** Symbol in der Kopfzeile, solange Teams im Einsatz ist (Modus „Teams“ oder „beides“). */
@Composable
fun TeamsAction(targets: TeamsTargets? = null) {
    val session = LocalSession.current
    val teams by session.teams.collectAsState()
    val context = LocalContext.current
    val model = LocalModel.current
    val target = targets ?: teams?.takeIf { it.available }?.homeTargets ?: return
    IconButton(onClick = { if (!Links.teams(context, target)) model.message("Microsoft Teams lässt sich auf diesem Gerät nicht öffnen.") }) {
        Icon(Icons.Filled.Groups, "In Teams öffnen")
    }
}

/** Hinweis im Chat-Archiv: lesen ja, schreiben nur noch in Teams. */
@Composable
fun ChatArchiveCard() {
    val session = LocalSession.current
    val teams by session.teams.collectAsState()
    val context = LocalContext.current
    SectionCard(Modifier.padding(bottom = 12.dp)) {
        Column(Modifier.padding(16.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Icon(Icons.Outlined.Archive, null)
                Spacer(Modifier.width(8.dp))
                Text("Chat-Archiv", style = MaterialTheme.typography.titleMedium)
            }
            Secondary("Unterhaltungen finden jetzt in Microsoft Teams statt. Bisherige Verläufe bleiben hier lesbar und lassen sich in der Weboberfläche exportieren.",
                Modifier.padding(top = 6.dp))
            val target = teams?.takeIf { it.available }?.homeTargets
            if (target != null) {
                Button(onClick = { Links.teams(context, target) }, modifier = Modifier.padding(top = 10.dp)) { Text("In Teams öffnen") }
            }
        }
    }
}
