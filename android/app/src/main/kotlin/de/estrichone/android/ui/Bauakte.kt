package de.estrichone.android.ui

import android.content.Intent
import android.net.Uri
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Directions
import androidx.compose.material.icons.filled.Phone
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.Checkbox
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.ListItem
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.PrimaryScrollableTabRow
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Tab
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import de.estrichone.core.ApiException
import de.estrichone.core.BauakteDetail
import de.estrichone.core.BauakteHint
import de.estrichone.core.EstrichOneApi
import de.estrichone.core.HintSeverity
import de.estrichone.core.Note
import de.estrichone.core.WorkItem
import de.estrichone.core.WorkItemKind
import de.estrichone.core.WorkItemStatus
import kotlinx.coroutines.async
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.launch

private data class BauakteData(val bauakte: BauakteDetail, val hints: List<BauakteHint>, val notes: List<Note>, val work: List<WorkItem>,
    val people: Map<String, String>)

/** Bauakte: Kopf, Hinweise, Reiter Übersicht / Notizen / Aufgaben & Mängel (08: TopAppBar, PrimaryScrollableTabRow). */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun BauakteScreen(api: EstrichOneApi, id: String, onBack: () -> Unit) {
    var reload by remember { mutableIntStateOf(0) }
    var data by rememberLoad(id to reload) {
        coroutineScope {
            val bauakte = async { api.bauakte(id) }
            val hints = async { api.hints(id) }
            val notes = async { api.notes(id) }
            val work = async { runCatching { api.workItems(id) }.getOrDefault(emptyList()) }
            val people = async { api.directory().associate { it.id to it.displayName } }
            BauakteData(bauakte.await(), hints.await(), notes.await(), work.await(), people.await())
        }
    }
    var tab by rememberSaveable { mutableIntStateOf(0) }
    val snackbar = remember { SnackbarHostState() }
    val scope = rememberCoroutineScope()
    val context = LocalContext.current

    fun act(block: suspend () -> Unit) {
        scope.launch {
            try {
                block()
                reload++
            } catch (e: ApiException) {
                snackbar.showSnackbar(e.message ?: "Fehler")
            }
        }
    }

    val loaded = (data as? Load.Done)?.value
    Scaffold(
        snackbarHost = { SnackbarHost(snackbar) },
        topBar = {
            TopAppBar(
                title = {
                    Column {
                        Text(loaded?.bauakte?.let { "${it.number ?: "neu"} · ${it.status.label}" } ?: "Bauakte", style = MaterialTheme.typography.labelMedium)
                        Text(loaded?.bauakte?.title ?: "", maxLines = 1, overflow = TextOverflow.Ellipsis)
                    }
                },
                navigationIcon = { IconButton(onClick = onBack) { Icon(Icons.AutoMirrored.Filled.ArrowBack, "Zurück") } },
                actions = {
                    loaded?.bauakte?.siteContactPhone?.let { phone ->
                        IconButton(onClick = { context.startActivity(Intent(Intent.ACTION_DIAL, Uri.parse("tel:$phone"))) }) {
                            Icon(Icons.Filled.Phone, "Ansprechpartner anrufen")
                        }
                    }
                    loaded?.bauakte?.let { b ->
                        IconButton(onClick = {
                            val target = if (b.latitude != null && b.longitude != null) "${b.latitude},${b.longitude}" else address(b.street, b.postalCode, b.city)
                            context.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse("geo:0,0?q=${Uri.encode(target)}")))
                        }) { Icon(Icons.Filled.Directions, "Route zur Baustelle") }
                    }
                },
            )
        },
    ) { padding ->
        when (val d = data) {
            Load.Loading -> Message("Lädt …")
            is Load.Failed -> Message(d.message, error = true)
            is Load.Done -> Column(Modifier.fillMaxSize().padding(padding)) {
                d.value.hints.forEach { HintCard(it) }
                PrimaryScrollableTabRow(selectedTabIndex = tab, edgePadding = 8.dp) {
                    listOf("Übersicht", "Notizen", "Aufgaben & Mängel").forEachIndexed { index, label ->
                        Tab(selected = tab == index, onClick = { tab = index }, text = { Text(label) })
                    }
                }
                when (tab) {
                    0 -> Overview(d.value)
                    1 -> Notes(d.value) { text -> act { api.addNote(id, text) } }
                    else -> Work(
                        d.value,
                        onAdd = { title, defect -> act { api.addWorkItem(id, if (defect) WorkItemKind.Defect else WorkItemKind.Task, title) } },
                        onToggle = { item -> act { api.setWorkItemStatus(item.id, if (item.status == WorkItemStatus.Done) WorkItemStatus.Open else WorkItemStatus.Done) } },
                    )
                }
            }
        }
    }
}

@Composable
private fun HintCard(hint: BauakteHint) {
    val color = when (hint.severity) {
        HintSeverity.Success -> Success
        HintSeverity.Warning -> Warning
        HintSeverity.Danger -> MaterialTheme.colorScheme.error
        HintSeverity.Info -> MaterialTheme.colorScheme.primary
    }
    Card(
        Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 3.dp),
        colors = CardDefaults.cardColors(containerColor = color.copy(alpha = 0.10f)),
    ) { Text(hint.text, color = color, modifier = Modifier.padding(horizontal = 12.dp, vertical = 8.dp)) }
}

@Composable
private fun Overview(d: BauakteData) {
    val b = d.bauakte
    LazyColumn(contentPadding = PaddingValues(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        item { Fact("Baustelle", address(b.street, b.postalCode, b.city)) }
        item { Fact("Zuständig", b.responsibleUserId?.let { d.people[it] } ?: "–") }
        item { Fact("Ansprechpartner vor Ort", listOfNotNull(b.siteContactName, b.siteContactPhone).joinToString(", ").ifEmpty { "–" }) }
        b.siteNotes?.let { item { Fact("Hinweise / Behinderungen", it) } }
        b.description?.let { item { Fact("Beschreibung", it) } }
    }
}

@Composable
private fun Fact(label: String, value: String) {
    Column {
        Text(label.uppercase(), style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        Text(value, fontWeight = FontWeight.SemiBold)
    }
}

@Composable
private fun Notes(d: BauakteData, onAdd: (String) -> Unit) {
    var text by rememberSaveable { mutableStateOf("") }
    LazyColumn(contentPadding = PaddingValues(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        item {
            OutlinedTextField(text, { text = it }, label = { Text("Neue Notiz") }, minLines = 2, modifier = Modifier.fillMaxWidth())
            Button(onClick = { onAdd(text.trim()); text = "" }, enabled = text.isNotBlank(), modifier = Modifier.padding(top = 6.dp)) { Text("Notiz speichern") }
        }
        if (d.notes.isEmpty()) item { Message("Noch keine Notizen.") }
        items(d.notes.sortedByDescending { it.createdAt }, key = { it.id }) { n ->
            Card(Modifier.fillMaxWidth()) {
                Column(Modifier.padding(12.dp)) {
                    Text("${d.people[n.authorUserId] ?: "–"} · ${dateTime(n.createdAt)}", style = MaterialTheme.typography.labelMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant)
                    Text(n.text)
                }
            }
        }
    }
}

@Composable
private fun Work(d: BauakteData, onAdd: (String, Boolean) -> Unit, onToggle: (WorkItem) -> Unit) {
    var title by rememberSaveable { mutableStateOf("") }
    var defect by rememberSaveable { mutableStateOf(true) }
    LazyColumn(contentPadding = PaddingValues(vertical = 12.dp)) {
        item {
            Column(Modifier.padding(horizontal = 16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    FilterChip(defect, { defect = true }, label = { Text("Mangel") })
                    FilterChip(!defect, { defect = false }, label = { Text("Aufgabe") })
                }
                OutlinedTextField(title, { title = it }, label = { Text("Titel") }, singleLine = true, modifier = Modifier.fillMaxWidth())
                Button(onClick = { onAdd(title.trim(), defect); title = "" }, enabled = title.isNotBlank()) { Text("Hinzufügen") }
            }
        }
        if (d.work.isEmpty()) item { Message("Keine Aufgaben oder Mängel.") }
        items(d.work.sortedBy { it.status == WorkItemStatus.Done }, key = { it.id }) { w ->
            ListItem(
                leadingContent = { Checkbox(w.status == WorkItemStatus.Done, { onToggle(w) }) },
                headlineContent = { Text(w.title) },
                supportingContent = { Text(if (w.kind == WorkItemKind.Defect) "Mangel" else "Aufgabe") },
                trailingContent = { w.dueDate?.let { Text(day(it)) } },
            )
        }
    }
}
