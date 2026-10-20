package de.estrichone.android.ui

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Folder
import androidx.compose.material.icons.filled.Search
import androidx.compose.material.icons.filled.SupportAgent
import androidx.compose.material.icons.filled.Today
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.Checkbox
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ExtendedFloatingActionButton
import androidx.compose.material3.Icon
import androidx.compose.material3.ListItem
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import de.estrichone.android.AppModel
import de.estrichone.android.BuildConfig
import de.estrichone.core.ApiException
import de.estrichone.core.CreateBauakteRequest
import de.estrichone.core.EstrichOneApi
import kotlinx.coroutines.launch
import java.time.LocalDate

private enum class Tab(val label: String) { Today("Heute"), Bauakten("Bauakten"), Support("Support") }

/** Hauptansicht mit Navigation Bar (docs/reengineering/08, Abschnitt 1). */
@Composable
fun MainScreen(model: AppModel) {
    var tab by rememberSaveable { mutableStateOf(Tab.Today) }
    var open by rememberSaveable { mutableStateOf<String?>(null) }
    val snackbar = remember { SnackbarHostState() }

    open?.let { id ->
        BackHandler { open = null }
        BauakteScreen(model.api, id, onBack = { open = null })
        return
    }

    Scaffold(
        snackbarHost = { SnackbarHost(snackbar) },
        bottomBar = {
            NavigationBar {
                NavigationBarItem(tab == Tab.Today, { tab = Tab.Today }, { Icon(Icons.Filled.Today, null) }, label = { Text(Tab.Today.label) })
                NavigationBarItem(tab == Tab.Bauakten, { tab = Tab.Bauakten }, { Icon(Icons.Filled.Folder, null) }, label = { Text(Tab.Bauakten.label) })
                NavigationBarItem(tab == Tab.Support, { tab = Tab.Support }, { Icon(Icons.Filled.SupportAgent, null) }, label = { Text(Tab.Support.label) })
            }
        },
    ) { padding ->
        when (tab) {
            Tab.Today -> TodayScreen(model, padding) { open = it }
            Tab.Bauakten -> BauaktenScreen(model.api, padding, snackbar) { open = it }
            Tab.Support -> SupportScreen(model, padding, snackbar)
        }
    }
}

@Composable
private fun TodayScreen(model: AppModel, padding: PaddingValues, onOpen: (String) -> Unit) {
    val today = LocalDate.now()
    val plan by rememberLoad(today) { model.api.myPlanning(today.toString(), today.plusDays(6).toString()) }
    LazyColumn(Modifier.fillMaxSize().padding(padding), contentPadding = PaddingValues(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        item {
            Text("Hallo ${model.userName}", style = MaterialTheme.typography.headlineSmall)
            Text("Mein Einsatz – die nächsten 7 Tage", color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
        when (val p = plan) {
            Load.Loading -> item { Message("Lädt …") }
            is Load.Failed -> item { Message(p.message, error = true) }
            is Load.Done -> {
                if (p.value.assignments.isEmpty()) item { Message("In den nächsten 7 Tagen ist für Sie nichts eingeplant.") }
                p.value.assignments.groupBy { it.day }.toSortedMap().forEach { (date, assignments) ->
                    item { Text(day(date), style = MaterialTheme.typography.titleMedium, modifier = Modifier.padding(top = 8.dp)) }
                    items(assignments, key = { it.id }) { a ->
                        Card(Modifier.fillMaxWidth().clickable { onOpen(a.bauakteId) }) {
                            Column(Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                                val time = listOfNotNull(a.startTime?.take(5), a.endTime?.take(5)).joinToString("–").ifEmpty { "ganztags" }
                                Text("$time · ${a.crewName ?: "ohne Kolonne"}", style = MaterialTheme.typography.labelLarge)
                                Text("${a.bauakteNumber ?: "neu"} · ${a.bauakteTitle}", fontWeight = FontWeight.SemiBold)
                                Text(a.activity)
                                a.bauakteAddress?.let { Text(it, color = MaterialTheme.colorScheme.onSurfaceVariant) }
                                a.warnings.forEach { Text(it, color = Warning, style = MaterialTheme.typography.bodySmall) }
                            }
                        }
                    }
                }
            }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun BauaktenScreen(api: EstrichOneApi, padding: PaddingValues, snackbar: SnackbarHostState, onOpen: (String) -> Unit) {
    var search by rememberSaveable { mutableStateOf("") }
    var query by rememberSaveable { mutableStateOf("") }
    var creating by remember { mutableStateOf(false) }
    val list by rememberLoad(query) { api.bauakten(search = query.ifBlank { null }) }

    Scaffold(
        modifier = Modifier.padding(padding),
        floatingActionButton = {
            ExtendedFloatingActionButton(onClick = { creating = true }, icon = { Icon(Icons.Filled.Add, null) }, text = { Text("Neue Bauakte") })
        },
    ) { inner ->
        Column(Modifier.fillMaxSize().padding(inner)) {
            OutlinedTextField(
                value = search, onValueChange = { search = it }, singleLine = true, leadingIcon = { Icon(Icons.Filled.Search, null) },
                placeholder = { Text("Nummer, Bauvorhaben, Ort") }, modifier = Modifier.fillMaxWidth().padding(16.dp),
                keyboardActions = androidx.compose.foundation.text.KeyboardActions(onSearch = { query = search }),
                keyboardOptions = KeyboardOptions(imeAction = androidx.compose.ui.text.input.ImeAction.Search),
            )
            when (val l = list) {
                Load.Loading -> Message("Lädt …")
                is Load.Failed -> Message(l.message, error = true)
                is Load.Done -> if (l.value.items.isEmpty()) Message("Keine Bauakten gefunden.") else LazyColumn {
                    items(l.value.items, key = { it.id }) { b ->
                        ListItem(
                            headlineContent = { Text(b.title, maxLines = 1, overflow = TextOverflow.Ellipsis) },
                            overlineContent = { Text("${b.number ?: "neu"} · ${b.status.label}") },
                            supportingContent = { Text(address(b.street, b.postalCode, b.city)) },
                            modifier = Modifier.clickable { onOpen(b.id) },
                        )
                    }
                }
            }
        }
    }

    if (creating) {
        val scope = rememberCoroutineScope()
        var title by remember { mutableStateOf("") }
        var street by remember { mutableStateOf("") }
        var postalCode by remember { mutableStateOf("") }
        var city by remember { mutableStateOf("") }
        var saving by remember { mutableStateOf(false) }
        ModalBottomSheet(onDismissRequest = { creating = false }) {
            Column(Modifier.padding(horizontal = 20.dp).padding(bottom = 28.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                Text("Neue Bauakte", style = MaterialTheme.typography.titleLarge)
                OutlinedTextField(title, { title = it }, label = { Text("Bauvorhaben") }, singleLine = true, modifier = Modifier.fillMaxWidth(),
                    keyboardOptions = KeyboardOptions(capitalization = KeyboardCapitalization.Sentences))
                OutlinedTextField(street, { street = it }, label = { Text("Straße") }, singleLine = true, modifier = Modifier.fillMaxWidth())
                Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                    OutlinedTextField(postalCode, { postalCode = it }, label = { Text("PLZ") }, singleLine = true, modifier = Modifier.weight(0.35f))
                    OutlinedTextField(city, { city = it }, label = { Text("Ort") }, singleLine = true, modifier = Modifier.weight(0.65f))
                }
                Button(
                    enabled = title.isNotBlank() && !saving, modifier = Modifier.fillMaxWidth(),
                    onClick = {
                        saving = true
                        scope.launch {
                            try {
                                val created = api.createBauakte(CreateBauakteRequest(EstrichOneApi.newId(), title.trim(), street.ifBlank { null },
                                    postalCode.ifBlank { null }, city.ifBlank { null }))
                                creating = false
                                onOpen(created.id)
                            } catch (e: ApiException) {
                                snackbar.showSnackbar(e.message ?: "Fehler")
                            } finally {
                                saving = false
                            }
                        }
                    },
                ) { Text("Anlegen") }
            }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun SupportScreen(model: AppModel, padding: PaddingValues, snackbar: SnackbarHostState) {
    val scope = rememberCoroutineScope()
    var title by rememberSaveable { mutableStateOf("") }
    var description by rememberSaveable { mutableStateOf("") }
    var blocked by rememberSaveable { mutableStateOf(false) }
    Column(Modifier.fillMaxSize().padding(padding).padding(16.dp).verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        TopAppBar(title = { Text("Support") })
        Text("Problem melden", style = MaterialTheme.typography.titleMedium)
        Text("Mitgeschickt werden nur App- und Android-Version – keine Inhalte aus Bauakten.", color = MaterialTheme.colorScheme.onSurfaceVariant)
        OutlinedTextField(title, { title = it }, label = { Text("Was funktioniert nicht?") }, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(description, { description = it }, label = { Text("Beschreibung (optional)") }, minLines = 3, modifier = Modifier.fillMaxWidth())
        Row { Checkbox(blocked, { blocked = it }); Text("Ich kann deshalb nicht weiterarbeiten", Modifier.padding(top = 12.dp)) }
        Button(enabled = title.isNotBlank(), modifier = Modifier.fillMaxWidth(), onClick = {
            scope.launch {
                try {
                    val ticket = model.api.reportProblem(title, description, blocked, BuildConfig.VERSION_NAME, android.os.Build.VERSION.RELEASE)
                    title = ""; description = ""; blocked = false
                    snackbar.showSnackbar("Meldung ${ticket.summary.number} wurde an die IT übermittelt.")
                } catch (e: ApiException) {
                    snackbar.showSnackbar(e.message ?: "Fehler")
                }
            }
        }) { Text("Meldung senden") }
        Spacer(Modifier.height(16.dp))
        Text("Angemeldet als ${model.userName}", color = MaterialTheme.colorScheme.onSurfaceVariant)
        Text("EstrichONE für Android ${BuildConfig.VERSION_NAME}", color = MaterialTheme.colorScheme.onSurfaceVariant)
        OutlinedButton(onClick = model::signOut, modifier = Modifier.fillMaxWidth()) { Text("Abmelden") }
    }
}
