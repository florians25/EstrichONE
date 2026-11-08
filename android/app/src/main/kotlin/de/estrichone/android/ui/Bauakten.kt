package de.estrichone.android.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.KeyboardArrowRight
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.CalendarMonth
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.PersonAdd
import androidx.compose.material.icons.filled.Search
import androidx.compose.material.icons.outlined.Apartment
import androidx.compose.material3.Button
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ExtendedFloatingActionButton
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FilterChipDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.SegmentedButton
import androidx.compose.material3.SegmentedButtonDefaults
import androidx.compose.material3.SingleChoiceSegmentedButtonRow
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextField
import androidx.compose.material3.TextFieldDefaults
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import de.estrichone.android.Session
import de.estrichone.core.Bauakte
import de.estrichone.core.BauakteStatus
import de.estrichone.core.BauakteTemplate
import de.estrichone.core.Dates
import de.estrichone.core.NewCustomer
import de.estrichone.core.PlannedJob
import de.estrichone.core.order
import java.time.LocalDate

private enum class Filter(val title: String, val statuses: Set<BauakteStatus>) {
    Running("Laufend", setOf(BauakteStatus.InProgress, BauakteStatus.Drying, BauakteStatus.Planned, BauakteStatus.Inquiry)),
    Active("In Ausführung", setOf(BauakteStatus.InProgress, BauakteStatus.Drying)),
    Planned("Geplant", setOf(BauakteStatus.Planned)),
    Inquiry("Anfrage", setOf(BauakteStatus.Inquiry)),
    Done("Abgeschlossen", setOf(BauakteStatus.Completed, BauakteStatus.Cancelled)),
}

@Composable
fun BauaktenScreen(selected: String?) {
    val session = LocalSession.current
    val nav = LocalNav.current
    var search by rememberSaveable { mutableStateOf("") }
    var filter by rememberSaveable { mutableStateOf(Filter.Running) }
    var creating by remember { mutableStateOf(false) }
    val all by session.local { session.service.bauakten() }
    val plan by produceState<List<PlannedJob>>(emptyList()) { value = session.weekPlan()?.assignments.orEmpty() }
    val today = LocalDate.now(Dates.zone).toString()
    val todayJobs = plan.filter { it.day == today }.associateBy { it.bauakteId }
    val customers by session.local { session.service.contacts().associateBy { it.id } }
    val list = all.filter { it.matches(search) && (search.isNotBlank() || it.status in filter.statuses) }

    ScreenScaffold("Baustellen", root = true, actions = { SyncAction() }, floating = {
        ExtendedFloatingActionButton(onClick = { creating = true }, icon = { Icon(Icons.Filled.Add, null) }, text = { Text("Neue Bauakte") })
    }) { padding ->
        LazyColumn(contentPadding = padding.content()) {
            item {
                SearchField(search, { search = it }, "Nummer, Bauvorhaben, Adresse")
                Row(Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()).padding(vertical = 8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    Filter.entries.forEach { f ->
                        val count = all.count { it.status in f.statuses }
                        FilterChip(selected = filter == f, onClick = { filter = f }, label = { Text("${f.title} ($count)") },
                            colors = FilterChipDefaults.filterChipColors(containerColor = tones.card))
                    }
                }
            }
            if (list.isEmpty()) {
                item { EmptyState(Icons.Outlined.Apartment, if (search.isBlank()) "Keine Baustellen" else "Keine Bauakte gefunden",
                    if (search.isBlank()) "Neue Bauakten legen Sie unten rechts an." else "Suche nach Nummer, Bauvorhaben, Straße oder Ort.") }
            }
            val todayList = if (search.isBlank()) list.filter { it.id in todayJobs } else emptyList()
            if (todayList.isNotEmpty()) {
                item { GroupLabel("Heute im Einsatz") }
                item { BauakteCards(todayList, todayJobs, customers, selected, session) }
            }
            val rest = list.filter { it !in todayList }
            rest.groupBy { it.status }.toSortedMap(compareBy { it.order }).forEach { (status, group) ->
                item { GroupLabel("${if (status == BauakteStatus.Drying) "Trocknung" else status.label} (${group.size})") }
                item { BauakteCards(group, todayJobs, customers, selected, session) }
            }
        }
    }
    if (creating) NewBauakteSheet(onDismiss = { creating = false }) { id -> creating = false; nav.set(nav.tab, listOf(Route.Bauakte(id))) }
}

@Composable
private fun BauakteCards(list: List<Bauakte>, today: Map<String, PlannedJob>, customers: Map<String, de.estrichone.core.Contact>, selected: String?, session: Session) {
    val nav = LocalNav.current
    SectionCard {
        list.forEachIndexed { index, b ->
            if (index > 0) RowDivider(16.dp)
            BauakteRow(b, customers[b.customerId]?.name, today[b.id], selected == b.id) { nav.set(nav.tab, listOf(Route.Bauakte(b.id))) }
        }
    }
}

@Composable
fun BauakteRow(b: Bauakte, customer: String?, job: PlannedJob?, selected: Boolean = false, onClick: () -> Unit) {
    Row(
        Modifier.fillMaxWidth().background(if (selected) tones.infoSoft else Color.Transparent).clickable(onClick = onClick)
            .padding(horizontal = 16.dp, vertical = 14.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(b.title, style = MaterialTheme.typography.titleMedium, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f, fill = false))
                Spacer(Modifier.width(6.dp))
                SyncMark(b.syncState)
            }
            Secondary(listOfNotNull(b.numberText, b.addressLine).joinToString(" · "), maxLines = 1)
            Row(verticalAlignment = Alignment.CenterVertically) {
                StatusLabel(b.status)
                if (customer != null) {
                    Spacer(Modifier.width(8.dp))
                    Text(customer, style = MaterialTheme.typography.bodyMedium, color = tones.textSecondary, maxLines = 1, overflow = TextOverflow.Ellipsis)
                }
            }
            if (job != null) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Icon(Icons.Filled.CalendarMonth, null, tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(16.dp))
                    Spacer(Modifier.width(4.dp))
                    Text(listOfNotNull("Heute", Dates.clock(job.startTime), job.crewName).joinToString(" · "), color = MaterialTheme.colorScheme.primary,
                        style = MaterialTheme.typography.labelLarge)
                }
            }
        }
        Icon(Icons.AutoMirrored.Filled.KeyboardArrowRight, null, tint = tones.textTertiary)
    }
}

/** Suchfeld wie die Suchleiste auf iOS (gefüllt, abgerundet). */
@Composable
fun SearchField(value: String, onChange: (String) -> Unit, placeholder: String, modifier: Modifier = Modifier) {
    TextField(
        value = value, onValueChange = onChange, singleLine = true, placeholder = { Text(placeholder, maxLines = 1, overflow = TextOverflow.Ellipsis) },
        leadingIcon = { Icon(Icons.Filled.Search, null) },
        trailingIcon = { if (value.isNotEmpty()) IconButton(onClick = { onChange("") }) { Icon(Icons.Filled.Close, "Suche leeren") } },
        shape = RoundedCornerShape(16.dp), modifier = modifier.fillMaxWidth(),
        colors = TextFieldDefaults.colors(
            focusedContainerColor = tones.fill, unfocusedContainerColor = tones.fill,
            focusedIndicatorColor = Color.Transparent, unfocusedIndicatorColor = Color.Transparent,
        ),
    )
}

/** Schnellanlage (wie NewBauakteView auf iOS): Art, Bauvorhaben, Adresse, Kunde aus der Liste oder neu. Offline möglich. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun NewBauakteSheet(onDismiss: () -> Unit, onCreated: (String) -> Unit) {
    val session = LocalSession.current
    val model = LocalModel.current
    var template by remember { mutableStateOf(BauakteTemplate.Standard) }
    var title by remember { mutableStateOf("") }
    var street by remember { mutableStateOf("") }
    var postalCode by remember { mutableStateOf("") }
    var city by remember { mutableStateOf("") }
    var customerSearch by remember { mutableStateOf("") }
    var customerId by remember { mutableStateOf<String?>(null) }
    var newCustomer by remember { mutableStateOf(false) }
    var customerName by remember { mutableStateOf("") }
    var company by remember { mutableStateOf(false) }
    var phone by remember { mutableStateOf("") }
    var error by remember { mutableStateOf<String?>(null) }
    val contacts = remember { session.service.contacts() }

    ModalBottomSheet(onDismissRequest = onDismiss, sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true), containerColor = tones.canvas) {
        Column(Modifier.verticalScroll(rememberScrollState()).imePadding().padding(horizontal = 20.dp).padding(bottom = 32.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Text("Neue Bauakte", style = MaterialTheme.typography.headlineSmall)
            Secondary("Die Nummer vergibt der Server beim nächsten Abgleich. Ohne Netz wird die Bauakte sofort auf dem Gerät gespeichert.")
            SingleChoiceSegmentedButtonRow(Modifier.fillMaxWidth()) {
                BauakteTemplate.entries.forEachIndexed { index, t ->
                    SegmentedButton(selected = template == t, onClick = { template = t }, shape = SegmentedButtonDefaults.itemShape(index, BauakteTemplate.entries.size)) {
                        Text(t.title, maxLines = 1)
                    }
                }
            }
            OutlinedTextField(title, { title = it }, label = { Text("Bauvorhaben") }, placeholder = { Text("z. B. EFH Müller, Neubau") }, singleLine = true,
                modifier = Modifier.fillMaxWidth(), keyboardOptions = KeyboardOptions(capitalization = KeyboardCapitalization.Sentences))
            OutlinedTextField(street, { street = it }, label = { Text("Straße und Hausnummer") }, singleLine = true, modifier = Modifier.fillMaxWidth())
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                OutlinedTextField(postalCode, { postalCode = it.filter(Char::isDigit).take(5) }, label = { Text("PLZ") }, singleLine = true,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number), modifier = Modifier.weight(0.35f))
                OutlinedTextField(city, { city = it }, label = { Text("Ort") }, singleLine = true, modifier = Modifier.weight(0.65f))
            }
            Text("Kunde", style = MaterialTheme.typography.titleMedium, modifier = Modifier.padding(top = 4.dp))
            if (!newCustomer) {
                val chosen = contacts.firstOrNull { it.id == customerId }
                if (chosen != null) {
                    SectionCard {
                        RowItem(chosen.name, icon = Icons.Filled.PersonAdd, subtitle = chosen.subtitle, trailing = {
                            IconButton(onClick = { customerId = null }) { Icon(Icons.Filled.Close, "Kunde entfernen") }
                        })
                    }
                } else {
                    SearchField(customerSearch, { customerSearch = it }, "Kunde suchen")
                    if (customerSearch.isNotBlank()) {
                        SectionCard {
                            contacts.filter { it.matches(customerSearch) }.take(5).forEachIndexed { i, c ->
                                if (i > 0) RowDivider(16.dp)
                                RowItem(c.name, subtitle = c.subtitle, onClick = { customerId = c.id; customerSearch = "" })
                            }
                        }
                    }
                    Text("+ Neuen Kunden anlegen", color = MaterialTheme.colorScheme.primary, style = MaterialTheme.typography.labelLarge,
                        modifier = Modifier.clip(RoundedCornerShape(8.dp)).clickable { newCustomer = true; customerName = customerSearch }.padding(6.dp))
                }
            } else {
                OutlinedTextField(customerName, { customerName = it }, label = { Text("Name des Kunden") }, singleLine = true, modifier = Modifier.fillMaxWidth())
                // Dublettenhinweis aus dem Bestand auf dem Gerät (auch offline).
                val similar = remember(customerName) { duplicates(contacts, customerName, city) }
                if (similar.isNotEmpty()) {
                    Banner("Ähnliche Kunden gibt es schon – bitte prüfen:", Icons.Filled.PersonAdd, tones.warning, tones.warningSoft)
                    SectionCard {
                        similar.forEachIndexed { i, c ->
                            if (i > 0) RowDivider(16.dp)
                            RowItem(c.name, subtitle = c.subtitle, value = "Übernehmen", onClick = { customerId = c.id; newCustomer = false })
                        }
                    }
                }
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text("Firma", modifier = Modifier.weight(1f))
                    Switch(company, { company = it })
                }
                OutlinedTextField(phone, { phone = it }, label = { Text("Telefon (optional)") }, singleLine = true, modifier = Modifier.fillMaxWidth(),
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Phone))
                Text("Vorhandenen Kunden wählen", color = MaterialTheme.colorScheme.primary, style = MaterialTheme.typography.labelLarge,
                    modifier = Modifier.clip(RoundedCornerShape(8.dp)).clickable { newCustomer = false }.padding(6.dp))
            }
            error?.let { Text(it, color = tones.danger) }
            Button(onClick = {
                try {
                    val id = session.service.createBauakte(title, street, postalCode, city, template, customerId,
                        if (newCustomer && customerName.isNotBlank()) NewCustomer(customerName, company, street, postalCode, city, phone) else null)
                    model.message("Bauakte angelegt – wird beim nächsten Abgleich übertragen.")
                    onCreated(id)
                } catch (e: IllegalArgumentException) {
                    error = e.message
                }
            }, enabled = title.isNotBlank(), modifier = Modifier.fillMaxWidth().height(52.dp)) { Text("Anlegen") }
        }
    }
}

/** Ähnliche Kunden: gleicher Nachname bzw. Name enthalten, bevorzugt im selben Ort (wie DuplicateFinder auf iOS). */
fun duplicates(contacts: List<de.estrichone.core.Contact>, name: String, city: String): List<de.estrichone.core.Contact> {
    val words = name.lowercase().split(' ', ',', '-').filter { it.length >= 3 && it !in setOf("familie", "herr", "frau", "gmbh", "und") }
    if (words.isEmpty()) return emptyList()
    return contacts.filter { c -> val n = c.name.lowercase(); words.any { n.contains(it) } }
        .sortedByDescending { it.city.equals(city.trim(), ignoreCase = true) }.take(3)
}
