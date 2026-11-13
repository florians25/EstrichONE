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
import de.estrichone.core.TeamsChatLinks
import de.estrichone.core.TeamsPerson
import de.estrichone.core.TeamsTargets
import de.estrichone.core.teamsPeople
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.filled.Close
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.TextButton
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

// Microsoft Teams (docs/microsoft-teams.md): „In Teams öffnen“ öffnet immer die Teams-App (sonst Google Play „Teams installieren“).

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

/**
 * „Chat in Teams“: Kollegen mit Microsoft-Konto; Tippen öffnet den Einzelchat direkt in der Teams-App. Wer kein Microsoft-Konto
 * hat, erscheint nicht. Ohne Teams-App: Hinweis „Microsoft Teams installieren“ (Google Play).
 */
@Composable
fun TeamsPeopleDialog(onDismiss: () -> Unit) {
    val session = LocalSession.current
    val context = LocalContext.current
    val model = LocalModel.current
    var people by remember { mutableStateOf<List<TeamsPerson>?>(null) }
    var search by remember { mutableStateOf("") }
    LaunchedEffect(Unit) {
        people = runCatching { withContext(Dispatchers.IO) { session.api.teamsPeople() } }.getOrDefault(emptyList())
            .filter { it.upn != null && it.userId != session.user.id }
    }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Chat in Teams") },
        text = {
            Column {
                if (!Links.teamsInstalled(context)) {
                    TextButton(onClick = { Links.installTeams(context) }) { Text("Microsoft Teams installieren") }
                }
                OutlinedTextField(search, { search = it }, label = { Text("Name") }, singleLine = true, modifier = Modifier.fillMaxWidth())
                val list = people
                when {
                    list == null -> CircularProgressIndicator(Modifier.padding(16.dp))
                    list.isEmpty() -> Secondary("Kollegen erscheinen hier, sobald ihnen ein Microsoft-Konto zugeordnet ist.", Modifier.padding(top = 12.dp))
                    else -> LazyColumn(Modifier.heightIn(max = 360.dp).padding(top = 8.dp)) {
                        items(list.filter { search.isBlank() || it.name.contains(search, ignoreCase = true) }, key = { it.userId }) { person ->
                            TextButton(onClick = {
                                val targets = TeamsChatLinks.targets(listOf(person))
                                if (targets == null || !Links.teams(context, targets)) model.message("Microsoft Teams lässt sich auf diesem Gerät nicht öffnen.")
                                onDismiss()
                            }, modifier = Modifier.fillMaxWidth()) {
                                Text(person.name, Modifier.fillMaxWidth())
                            }
                        }
                    }
                }
            }
        },
        confirmButton = {},
        dismissButton = { TextButton(onClick = onDismiss) { Text("Abbrechen") } },
    )
}

/** Einmaliger Hinweis „Ab sofort chatten wir in Teams“ (wegklickbar, je Text gemerkt). */
@Composable
fun TeamsRolloutCard() {
    val session = LocalSession.current
    val teams by session.teams.collectAsState()
    val context = LocalContext.current
    val text = teams?.rolloutNotice?.takeIf { it.isNotBlank() } ?: return
    val prefs = remember { context.getSharedPreferences("teams", android.content.Context.MODE_PRIVATE) }
    var dismissed by remember(text) { mutableStateOf(prefs.getString("noticeDismissed", null) == text) }
    if (dismissed) return
    SectionCard(Modifier.padding(bottom = 12.dp)) {
        Column(Modifier.padding(16.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Icon(Icons.Filled.Groups, null)
                Spacer(Modifier.width(8.dp))
                Text("Microsoft Teams", style = MaterialTheme.typography.titleMedium, modifier = Modifier.weight(1f))
                IconButton(onClick = { prefs.edit().putString("noticeDismissed", text).apply(); dismissed = true }) {
                    Icon(Icons.Filled.Close, "Hinweis schließen")
                }
            }
            Secondary(text, Modifier.padding(top = 6.dp))
            if (!Links.teamsInstalled(context)) {
                TextButton(onClick = { Links.installTeams(context) }) { Text("Microsoft Teams installieren") }
            }
        }
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
