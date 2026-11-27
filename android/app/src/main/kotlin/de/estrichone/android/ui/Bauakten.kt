package de.estrichone.android.ui

import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.layout.Box
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
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.heightIn
import androidx.compose.material3.pulltorefresh.PullToRefreshBox
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.rememberCoroutineScope
import kotlinx.coroutines.launch
import de.estrichone.core.onlineBauakten
import androidx.compose.material.icons.outlined.Visibility
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
import de.estrichone.core.SiteList
import java.time.LocalDate

/**
 * Baustellen: Suche, Filter (Heute, Laufend, Status, Alle) mit Anzahl, kompakte Zeilen mit Nummer, Bauvorhaben, Ort,
 * Status-Kennzeichen und nächstem Termin. Heute eingeplante zuerst, dann nach Status und nächstem Termin.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun BauaktenScreen(selected: String?) {
    val session = LocalSession.current
    val nav = LocalNav.current
    var search by rememberSaveable { mutableStateOf("") }
    var filter by rememberSaveable { mutableStateOf(SiteList.Filter.Running) }
    var creating by remember { mutableStateOf(false) }
    val all by session.local { session.service.bauakten() }
    val today = LocalDate.now(Dates.zone)
    val plan by produceState<List<PlannedJob>>(session.cachedWeekPlan()?.assignments.orEmpty(), nav.planChanged) { value = session.weekPlan()?.assignments.orEmpty() }
    val next = remember(plan) { SiteList.nextJobs(plan, today.toString()) }
    val todayIds = next.filterValues { it.day == today.toString() }.keys
    val customers by session.local { session.service.contacts().associateBy { it.id } }
    val list = remember(all, customers, search, filter, next) {
        SiteList.ordered(
            all.filter { it.matches(search, customers[it.customerId]) && (search.isNotBlank() || SiteList.matches(filter, it.status, it.id.lowercase() in todayIds)) },
            { it.id.lowercase() }, { it.status }, { it.number }, next, today.toString(),
        )
    }
    val counts = remember(all, next) { SiteList.Filter.entries.associateWith { f -> all.count { SiteList.matches(f, it.status, it.id.lowercase() in todayIds) } } }
    val status by session.status.collectAsState()
    val scope = rememberCoroutineScope()
    val listState = androidx.compose.foundation.lazy.rememberLazyListState()
    val fabExpanded by remember { androidx.compose.runtime.derivedStateOf { listState.firstVisibleItemIndex == 0 } }
    // Alle sehen alle Bauakten: Bei der Suche auch nicht zugeteilte vom Server (nicht auf dem Gerät, nur ansehen).
    val others by produceState<List<de.estrichone.core.BauakteListItem>>(emptyList(), search, all) {
        if (search.isBlank()) {
            value = emptyList()
            return@produceState
        }
        kotlinx.coroutines.delay(400)
        val local = all.map { it.id.lowercase() }.toSet()
        value = runCatching { kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) { session.api.onlineBauakten(search) } }
            .getOrDefault(emptyList()).filter { it.id.lowercase() !in local }
    }

    ScreenScaffold("Baustellen", root = true, floating = {
        ExtendedFloatingActionButton(onClick = { creating = true }, expanded = fabExpanded, icon = { Icon(Icons.Filled.Add, null) }, text = { Text("Neue Bauakte") })
    }) { padding ->
        // Nach unten ziehen: sofort abgleichen (neue Bauakten, Änderungen aus dem Büro).
        PullToRefreshBox(isRefreshing = status.running, onRefresh = { scope.launch { session.synchronize() } }, modifier = Modifier.fillMaxSize()) {
        LazyColumn(state = listState, contentPadding = padding.content()) {
            item(key = "search", contentType = "search") {
                OfflineBanner(session)
                SearchField(search, { search = it }, "Nummer, Bauvorhaben, Ort, Kunde")
                Row(Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()).padding(top = 8.dp, bottom = 4.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    SiteList.Filter.entries.filter { it != SiteList.Filter.Today || (counts[it] ?: 0) > 0 || filter == it }.forEach { f ->
                        FilterChip(selected = filter == f && search.isBlank(), onClick = { filter = f }, label = { Text("${f.title} ${counts[f] ?: 0}") },
                            colors = FilterChipDefaults.filterChipColors(containerColor = tones.card), modifier = Modifier.heightIn(min = 40.dp))
                    }
                }
            }
            if (list.isEmpty()) {
                item(key = "empty") {
                    EmptyState(Icons.Outlined.Apartment, if (search.isBlank()) "Keine Baustellen in „${filter.title}“" else "Keine Bauakte gefunden",
                        if (search.isBlank()) "Wählen Sie oben einen anderen Filter oder legen Sie eine neue Bauakte an."
                        else "Suche nach Nummer, Bauvorhaben, Adresse, Kunde oder Kundennummer.")
                }
            }
            val todayList = list.filter { it.id.lowercase() in todayIds }
            // Jede Zeile ein eigenes Listenelement mit fester ID: auch bei Hunderten Bauakten wird nur das Sichtbare gezeichnet.
            fun group(id: String, title: String, rows: List<Bauakte>) {
                item(key = "label-$id", contentType = "label") { GroupLabel(title) }
                itemsIndexed(rows, key = { _, b -> "$id-${b.id}" }, contentType = { _, _ -> "bauakte" }) { index, b ->
                    CardSlot(index, rows.size) {
                        BauakteRow(b, customers[b.customerId], next[b.id.lowercase()], selected == b.id, today) { nav.set(nav.tab, listOf(Route.Bauakte(b.id))) }
                    }
                }
            }
            if (todayList.isNotEmpty() && filter != SiteList.Filter.Today) group("today", "Heute im Einsatz (${todayList.size})", todayList)
            val rest = list.filter { it !in todayList || filter == SiteList.Filter.Today }
            if (rest.isNotEmpty()) group("rest", if (search.isBlank()) "${filter.title} (${rest.size})" else "Treffer (${rest.size})", rest)
            if (others.isNotEmpty()) {
                item(key = "label-online", contentType = "label") { GroupLabel("Weitere Bauakten (online, nur ansehen)") }
                itemsIndexed(others, key = { _, b -> "online-${b.id}" }, contentType = { _, _ -> "online" }) { index, b ->
                    CardSlot(index, others.size) {
                        Row(Modifier.fillMaxWidth().clickable { nav.set(nav.tab, listOf(Route.Bauakte(b.id.lowercase()))) }
                            .padding(horizontal = 16.dp, vertical = 12.dp), verticalAlignment = Alignment.CenterVertically) {
                            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                                Text(b.number ?: "ohne Nummer", style = MaterialTheme.typography.labelMedium, color = tones.textSecondary)
                                Text(b.title, style = MaterialTheme.typography.bodyLarge, maxLines = 1, overflow = TextOverflow.Ellipsis)
                                Secondary(listOfNotNull(b.street, b.city).joinToString(", "), maxLines = 1)
                            }
                            StatusChip(b.status)
                            Icon(Icons.Outlined.Visibility, "Nur ansehen", tint = tones.textSecondary, modifier = Modifier.padding(start = 8.dp))
                        }
                    }
                }
            }
        }
        }
    }
    if (creating) NewBauakteSheet(onDismiss = { creating = false }) { id -> creating = false; nav.set(nav.tab, listOf(Route.Bauakte(id))) }
}

/** Kompakte Zeile: Nummer und Status oben, Bauvorhaben, Ort und Kunde, nächster Termin (heute in der Markenfarbe). */
@OptIn(androidx.compose.foundation.ExperimentalFoundationApi::class)
@Composable
fun BauakteRow(b: Bauakte, customer: de.estrichone.core.Contact?, job: PlannedJob?, selected: Boolean = false, today: LocalDate = LocalDate.now(Dates.zone), onClick: () -> Unit) {
    var menu by remember { mutableStateOf(false) }
    Row(
        Modifier.fillMaxWidth().background(if (selected) tones.infoSoft else Color.Transparent)
            .combinedClickable(onClick = onClick, onLongClickLabel = "Aktionen", onLongClick = { menu = true })
            .heightIn(min = 72.dp).padding(start = 16.dp, end = 12.dp, top = 12.dp, bottom = 12.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        // Langes Drücken: für Planer Einplanen, dazu Route, Foto, Tagebuch, Mangel direkt aus der Liste.
        Box { SiteQuickMenu(menu, { menu = false }, b.id, b.addressLine, b.latitude, b.longitude) }
        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(b.numberText, style = MaterialTheme.typography.labelMedium, color = tones.textSecondary)
                Spacer(Modifier.width(8.dp))
                SyncMark(b.syncState)
                Spacer(Modifier.weight(1f))
                StatusChip(b.status)
            }
            Text(b.title, style = MaterialTheme.typography.bodyLarge, fontWeight = FontWeight.Medium, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Secondary(listOfNotNull(listOfNotNull(b.postalCode, b.city).joinToString(" ").ifBlank { null } ?: b.addressLine, customer?.name).joinToString(", "), maxLines = 1)
            if (job != null) {
                val isToday = job.day == today.toString()
                Row(Modifier.padding(top = 2.dp), verticalAlignment = Alignment.CenterVertically) {
                    Icon(Icons.Filled.CalendarMonth, null, tint = if (isToday) MaterialTheme.colorScheme.primary else tones.textSecondary, modifier = Modifier.size(14.dp))
                    Spacer(Modifier.width(4.dp))
                    Text(listOfNotNull(SiteList.appointmentText(job, today), job.activity).joinToString(", "), maxLines = 1, overflow = TextOverflow.Ellipsis,
                        color = if (isToday) MaterialTheme.colorScheme.primary else tones.textSecondary, style = MaterialTheme.typography.labelLarge)
                }
            }
        }
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
    val contacts = remember { session.service.activeContacts() }
    // „Mit Lexware Office abgleichen“ nur mit eingeschaltetem Modul, vorbelegt mit der Vorgabe aus den Einstellungen.
    val modules by session.modules.collectAsState()
    var lexwareSync by remember(modules) { mutableStateOf(modules.lexwareSyncDefault) }
    // Prüfung gegen Lexware Office beim Anlegen eines Kunden: offene Treffer und ein von dort übernommener Kunde.
    val lexwareState = remember { LexwareCheckState() }
    var imported by remember { mutableStateOf<Pair<String, String>?>(null) }
    val scope = androidx.compose.runtime.rememberCoroutineScope()
    fun create() {
        try {
            val id = session.service.createBauakte(title, street, postalCode, city, template, customerId,
                if (newCustomer && customerName.isNotBlank()) NewCustomer(customerName, company, street, postalCode, city, phone,
                    lexwareSync = if (modules.lexwareOn) lexwareSync else null) else null)
            model.message("Bauakte angelegt. Sie wird beim nächsten Abgleich übertragen.")
            onCreated(id)
        } catch (e: IllegalArgumentException) {
            error = e.message
        }
    }
    // Neuer Kunde: erst in Lexware Office prüfen, dann anlegen. Bei Treffern oder ohne Antwort nur nach ausdrücklicher Entscheidung.
    fun submit(uncheckedConfirmed: Boolean = false) {
        if (!(newCustomer && customerName.isNotBlank())) {
            create()
            return
        }
        scope.launch {
            if (lexwareState.confirm(session, customerName, company, street, postalCode, city, phone, uncheckedConfirmed)) create()
            else if (lexwareState.hits.isNotEmpty()) {
                error = "Dieser Kunde ist bereits in Lexware Office vorhanden. Bitte übernehmen Sie ihn oder wählen Sie „Trotzdem neu anlegen“."
            }
        }
    }

    ModalBottomSheet(onDismissRequest = onDismiss, sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true), containerColor = tones.canvas) {
        Column(Modifier.verticalScroll(rememberScrollState()).imePadding().padding(horizontal = 20.dp).padding(bottom = 32.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Text("Neue Bauakte", style = MaterialTheme.typography.headlineSmall)
            Secondary("Die Nummer vergibt der Server beim nächsten Abgleich. Ohne Verbindung wird die Bauakte auf dem Gerät gespeichert.")
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
                val fromLexware = imported?.takeIf { it.first == customerId }
                if (chosen == null && fromLexware != null) {
                    // Eben aus Lexware Office übernommen: Der Kunde steht auf dem Server und kommt mit dem Abgleich auf das Gerät.
                    SectionCard {
                        RowItem(fromLexware.second, icon = Icons.Filled.PersonAdd, subtitle = "Aus Lexware Office übernommen", trailing = {
                            IconButton(onClick = { customerId = null; imported = null }) { Icon(Icons.Filled.Close, "Kunde entfernen") }
                        })
                    }
                } else if (chosen != null) {
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
                // Kundennummer nur aus dem Nummernkreis (Server): schreibgeschützt, ohne weitere Hinweise.
                OutlinedTextField("wird automatisch vergeben", {}, label = { Text("Kundennummer") }, readOnly = true, enabled = false, singleLine = true,
                    modifier = Modifier.fillMaxWidth())
                // Dublettenhinweis aus dem Bestand auf dem Gerät (auch offline).
                val similar = remember(customerName) { duplicates(contacts, customerName, city) }
                if (similar.isNotEmpty()) {
                    Banner("Ähnliche Kunden vorhanden. Bitte prüfen.", Icons.Filled.PersonAdd, tones.warning, tones.warningSoft)
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
                LexwareCustomerCheck(session, lexwareState, customerName, company, street, postalCode, city, phone,
                    onImported = { id, name -> imported = id to name; customerId = id; newCustomer = false; error = null },
                    onUnchecked = { submit(uncheckedConfirmed = true) })
                if (modules.lexwareOn) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Column(Modifier.weight(1f)) {
                            Text("Mit Lexware Office abgleichen")
                            Secondary("Aus: Der Kunde bleibt nur in EstrichONE.")
                        }
                        Switch(lexwareSync, { lexwareSync = it })
                    }
                }
                OutlinedTextField(phone, { phone = it }, label = { Text("Telefon (optional)") }, singleLine = true, modifier = Modifier.fillMaxWidth(),
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Phone))
                Text("Vorhandenen Kunden wählen", color = MaterialTheme.colorScheme.primary, style = MaterialTheme.typography.labelLarge,
                    modifier = Modifier.clip(RoundedCornerShape(8.dp)).clickable { newCustomer = false }.padding(6.dp))
            }
            error?.let { Text(it, color = tones.danger) }
            Button(onClick = { submit() }, enabled = title.isNotBlank() && !lexwareState.running, modifier = Modifier.fillMaxWidth().height(48.dp)) { Text("Anlegen") }
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
