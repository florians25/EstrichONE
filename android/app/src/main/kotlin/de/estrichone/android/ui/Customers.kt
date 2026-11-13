package de.estrichone.android.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.KeyboardArrowRight
import androidx.compose.material.icons.filled.Directions
import androidx.compose.material.icons.filled.Edit
import androidx.compose.material.icons.filled.Email
import androidx.compose.material.icons.filled.Language
import androidx.compose.material.icons.filled.Phone
import androidx.compose.material.icons.filled.Smartphone
import androidx.compose.material.icons.outlined.Apartment
import androidx.compose.material.icons.outlined.People
import androidx.compose.material.icons.outlined.Place
import androidx.compose.material.icons.automirrored.outlined.StickyNote2
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.Inventory2
import androidx.compose.material.icons.filled.MoreVert
import androidx.compose.material.icons.filled.Unarchive
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.foundation.layout.Box
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import de.estrichone.android.support.Links
import de.estrichone.core.Contact
import de.estrichone.core.ContactPhoneKind
import de.estrichone.core.CustomerReferences
import de.estrichone.core.CustomerRemoval
import de.estrichone.core.archiveCustomer
import de.estrichone.core.customerReferences
import de.estrichone.core.deleteCustomer
import de.estrichone.core.reactivateCustomer
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

@Composable
fun CustomersScreen(selected: String?) {
    val session = LocalSession.current
    val nav = LocalNav.current
    var search by rememberSaveable { mutableStateOf("") }
    var showArchived by rememberSaveable { mutableStateOf(false) }
    val contacts by session.local { session.service.contacts() }
    val bauakten by session.local { session.service.bauakten().groupBy { it.customerId } }
    // Archivierte Kunden nur auf Wunsch (wie „Archivierte“ im Büro).
    val archivedCount = contacts.count { it.isArchived }
    val list = contacts.filter { (showArchived || !it.isArchived) && it.matches(search) }
    ScreenScaffold("Kunden", root = true) { padding ->
        LazyColumn(contentPadding = padding.content()) {
            item { SearchField(search, { search = it }, "Name, Nummer, Ort oder Telefon", Modifier.padding(bottom = 8.dp)) }
            if (archivedCount > 0) {
                item {
                    FilterChip(showArchived, { showArchived = !showArchived }, label = { Text("Archivierte anzeigen ($archivedCount)") },
                        modifier = Modifier.padding(bottom = 8.dp))
                }
            }
            if (list.isEmpty()) {
                item { EmptyState(Icons.Outlined.People, if (contacts.isEmpty()) "Keine Kunden auf dem Gerät" else "Kein Kunde gefunden",
                    if (contacts.isEmpty()) "Kunden erscheinen mit dem Recht „Kontakte lesen“." else null) }
            }
            list.groupBy { it.name.firstOrNull()?.uppercaseChar()?.takeIf { c -> c.isLetter() } ?: '#' }.toSortedMap().forEach { (letter, group) ->
                item { GroupLabel(letter.toString()) }
                item {
                    SectionCard {
                        group.forEachIndexed { index, c ->
                            if (index > 0) RowDivider(72.dp)
                            val count = bauakten[c.id]?.size ?: 0
                            Row(Modifier.fillMaxWidth().background(if (selected == c.id) tones.infoSoft else Color.Transparent)
                                .clickable { nav.set(nav.tab, listOf(Route.Customer(c.id))) }.padding(horizontal = 16.dp, vertical = 12.dp),
                                verticalAlignment = Alignment.CenterVertically) {
                                Avatar(c.initials)
                                Spacer(Modifier.width(12.dp))
                                Column(Modifier.weight(1f)) {
                                    Row(verticalAlignment = Alignment.CenterVertically) {
                                        Text(c.name, style = MaterialTheme.typography.titleMedium, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f, false))
                                        Spacer(Modifier.width(6.dp))
                                        SyncMark(c.syncState)
                                    }
                                    Secondary(listOf(if (c.isArchived) "Archiviert" else "", c.subtitle.ifEmpty { "Neu" }, if (count > 0) "$count Bauakte${if (count > 1) "n" else ""}" else "").filter { it.isNotEmpty() }.joinToString(" · "))
                                }
                                Icon(Icons.AutoMirrored.Filled.KeyboardArrowRight, null, tint = tones.textTertiary)
                            }
                        }
                    }
                }
            }
        }
    }
}

/** Kundenkarte (wie CustomerDirectoryView auf iOS): Nummern, E-Mail, Anschrift, Notiz, Bauakten. */
@Composable
fun CustomerScreen(id: String, showBack: Boolean = true) {
    val session = LocalSession.current
    val nav = LocalNav.current
    val context = LocalContext.current
    val contact by session.local(id) { session.service.contact(id) }
    val bauakten by session.local(id) { session.service.bauaktenOfCustomer(id) }
    var editing by remember { mutableStateOf(false) }
    var menu by remember { mutableStateOf(false) }
    var removing by remember { mutableStateOf(false) }
    val permissions by session.permissions.collectAsState()
    val canEdit = "contacts.edit" in permissions
    val canDelete = "contacts.delete" in permissions
    val scope = rememberCoroutineScope()
    val c = contact ?: run { ScreenScaffold("Kunde", showBack = showBack) { EmptyState(Icons.Outlined.People, "Kunde nicht gefunden", null, Modifier.padding(it)) }; return }
    ScreenScaffold(c.name, showBack = showBack, actions = {
        if (canEdit && c.anonymizedAt == null) IconButton(onClick = { editing = true }) { Icon(Icons.Filled.Edit, "Bearbeiten") }
        if ((canEdit || canDelete) && c.anonymizedAt == null) {
            Box {
                IconButton(onClick = { menu = true }) { Icon(Icons.Filled.MoreVert, "Weitere Aktionen") }
                DropdownMenu(menu, { menu = false }) {
                    if (canEdit && c.isArchived) {
                        DropdownMenuItem(text = { Text("Reaktivieren") }, leadingIcon = { Icon(Icons.Filled.Unarchive, null) }, onClick = {
                            menu = false
                            scope.launch {
                                runCatching { withContext(Dispatchers.IO) { session.api.reactivateCustomer(c.id) } }
                                session.synchronize()
                            }
                        })
                    }
                    DropdownMenuItem(text = { Text(if (c.isArchived) "Löschen" else "Löschen oder archivieren") }, leadingIcon = { Icon(Icons.Filled.Delete, null) },
                        onClick = { menu = false; removing = true })
                }
            }
        }
    }) { padding ->
        LazyColumn(contentPadding = padding.content(), modifier = Modifier.readable()) {
            item {
                Column(Modifier.fillMaxWidth().padding(vertical = 8.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                    Avatar(c.initials, size = 76.dp)
                    Text(c.name, style = MaterialTheme.typography.headlineSmall, modifier = Modifier.padding(top = 10.dp))
                    Secondary(listOfNotNull(c.customerNumber?.let { "Kd.-Nr. $it" } ?: "Noch ohne Kundennummer (vergibt das Büro)", if (c.isCompany) "Firma" else "Privat",
                        if (c.anonymizedAt != null) "Anonymisiert" else if (c.isArchived) "Archiviert" else null).joinToString(" · "))
                    Row(Modifier.padding(top = 14.dp), horizontalArrangement = Arrangement.spacedBy(16.dp)) {
                        c.phone?.let { CircleAction(Icons.Filled.Phone, "Anrufen", { Links.call(context, it) }) }
                        c.email?.let { CircleAction(Icons.Filled.Email, "E-Mail", { Links.mail(context, it) }) }
                        c.addressLine?.let { CircleAction(Icons.Filled.Directions, "Route", { Links.route(context, it) }) }
                    }
                }
            }
            if (c.phones.isNotEmpty() || c.emails.isNotEmpty() || c.website != null) {
                item { SectionHeader("Kontakt") }
                item {
                    SectionCard {
                        c.phones.forEachIndexed { index, p ->
                            if (index > 0) RowDivider()
                            RowItem(p.number, icon = if (p.kind == ContactPhoneKind.Mobile) Icons.Filled.Smartphone else Icons.Filled.Phone,
                                subtitle = listOfNotNull(p.kind.label, p.label).joinToString(" · "), onClick = { Links.call(context, p.number) })
                        }
                        c.emails.forEach { e ->
                            RowDivider()
                            RowItem(e.address, icon = Icons.Filled.Email, subtitle = e.label ?: "E-Mail", onClick = { Links.mail(context, e.address) })
                        }
                        c.website?.let { w -> RowDivider(); RowItem(w, icon = Icons.Filled.Language, subtitle = "Webseite", onClick = { Links.web(context, w) }) }
                    }
                }
            }
            c.addressLine?.let { address ->
                item { SectionHeader("Anschrift") }
                item { SectionCard { RowItem(address, icon = Icons.Outlined.Place, onClick = { Links.route(context, address) }) } }
            }
            c.notes?.takeIf { it.isNotBlank() }?.let { notes ->
                item { SectionHeader("Notiz") }
                item { SectionCard { RowItem(notes, icon = Icons.AutoMirrored.Outlined.StickyNote2) } }
            }
            item { SectionHeader("Bauakten (${bauakten.size})") }
            item {
                SectionCard {
                    if (bauakten.isEmpty()) RowItem("Keine Bauakten auf dem Gerät", icon = Icons.Outlined.Apartment)
                    bauakten.forEachIndexed { index, b ->
                        if (index > 0) RowDivider(16.dp)
                        BauakteRow(b, null, null) { nav.push(Route.Bauakte(b.id)) }
                    }
                }
            }
        }
    }
    if (editing) CustomerSheet(c) { editing = false }
    if (removing) CustomerRemovalDialog(c, canDelete, canEdit, onDismiss = { removing = false }) { deleted ->
        removing = false
        scope.launch {
            // Der Abgleich holt den Tombstone (gelöscht) bzw. den archivierten Stand.
            session.synchronize()
            if (deleted) nav.back()
        }
    }
}

/**
 * Löschen bzw. Archivieren (nur online): lädt die Bezüge des Kunden. Ohne Bezüge wird nach Eintippen der Kundennummer
 * endgültig gelöscht; mit Bauakten, Anrufen o. ä. nennt der Dialog die Bezüge und bietet Archivieren an.
 */
@Composable
private fun CustomerRemovalDialog(contact: Contact, canDelete: Boolean, canArchive: Boolean, onDismiss: () -> Unit, onDone: (deleted: Boolean) -> Unit) {
    val session = LocalSession.current
    val scope = rememberCoroutineScope()
    var refs by remember { mutableStateOf<CustomerReferences?>(null) }
    var error by remember { mutableStateOf<String?>(null) }
    var confirm by remember { mutableStateOf("") }
    var busy by remember { mutableStateOf(false) }
    androidx.compose.runtime.LaunchedEffect(contact.id) {
        runCatching { withContext(Dispatchers.IO) { session.api.customerReferences(contact.id) } }
            .onSuccess { refs = it }
            .onFailure { error = "Löschen und Archivieren gehen nur mit Verbindung zum Server. ${it.message ?: ""}".trim() }
    }
    fun act(deleted: Boolean, call: suspend () -> Unit) {
        busy = true
        scope.launch {
            runCatching { withContext(Dispatchers.IO) { call() } }
                .onSuccess { onDone(deleted) }
                .onFailure { error = it.message; busy = false }
        }
    }
    val r = refs
    val deletable = r?.deletable == true && canDelete
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(if (r == null) "Kunde löschen" else if (r.deletable) "Kunde löschen?" else "Kunde kann nicht gelöscht werden") },
        text = {
            Column {
                when {
                    r == null && error == null -> Text("Bezüge werden geprüft …")
                    r != null && r.deletable -> {
                        Text("${contact.name} hat keine Bauakten, Anrufe oder andere verknüpfte Daten. Stammdaten, Telefonnummern, E-Mail-Adressen, " +
                            "Notiz und Ansprechpartner werden endgültig entfernt." + (r.customerNumber?.let { " Die Kundennummer $it wird nicht erneut vergeben." } ?: ""))
                        if (canDelete) {
                            OutlinedTextField(confirm, { confirm = it }, singleLine = true,
                                label = { Text(if (r.customerNumber != null) "Kundennummer ${r.confirmText} eintippen" else "Namen „${r.confirmText}“ eintippen") },
                                modifier = Modifier.fillMaxWidth().padding(top = 12.dp))
                        } else {
                            Secondary("Zum Löschen fehlt die Berechtigung „Kunden löschen“.", Modifier.padding(top = 8.dp))
                        }
                    }
                    r != null -> Text("Noch verknüpft: ${r.summary}. Stattdessen archivieren: Der Kunde bleibt mit allen Unterlagen erhalten, " +
                        "erscheint aber nicht mehr in Auswahllisten und standardmäßig nicht in der Kundenliste.")
                }
                error?.let { Text(it, color = tones.danger, modifier = Modifier.padding(top = 8.dp)) }
            }
        },
        confirmButton = {
            if (r != null && deletable) {
                TextButton(enabled = !busy && CustomerRemoval.confirms(confirm, r.confirmText), onClick = {
                    act(true) { session.api.deleteCustomer(contact.id, confirm.trim()) }
                }) { Text("Endgültig löschen", color = tones.danger) }
            } else if (r != null && canArchive && !r.archived) {
                TextButton(enabled = !busy, onClick = { act(false) { session.api.archiveCustomer(contact.id) } }) {
                    Icon(Icons.Filled.Inventory2, null, Modifier.padding(end = 6.dp)); Text("Archivieren")
                }
            }
        },
        dismissButton = {
            Row {
                if (r != null && deletable && canArchive && !r.archived) {
                    TextButton(enabled = !busy, onClick = { act(false) { session.api.archiveCustomer(contact.id) } }) { Text("Archivieren") }
                }
                TextButton(onClick = onDismiss) { Text("Abbrechen") }
            }
        },
    )
}

@Composable
private fun CustomerSheet(contact: Contact, onDismiss: () -> Unit) {
    val session = LocalSession.current
    var name by remember { mutableStateOf(contact.name) }
    var street by remember { mutableStateOf(contact.street ?: "") }
    var postalCode by remember { mutableStateOf(contact.postalCode ?: "") }
    var city by remember { mutableStateOf(contact.city ?: "") }
    var notes by remember { mutableStateOf(contact.notes ?: "") }
    var website by remember { mutableStateOf(contact.website ?: "") }
    FormSheet("Kunde bearbeiten", null, onDismiss, canSave = name.isNotBlank(), onSave = {
        session.service.updateContact(contact, mapOf("name" to name, "street" to street, "postalCode" to postalCode, "city" to city, "notes" to notes, "website" to website))
    }) {
        OutlinedTextField(name, { name = it }, label = { Text("Name") }, singleLine = true, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(street, { street = it }, label = { Text("Straße") }, singleLine = true, modifier = Modifier.fillMaxWidth())
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            OutlinedTextField(postalCode, { postalCode = it }, label = { Text("PLZ") }, singleLine = true, modifier = Modifier.weight(0.35f))
            OutlinedTextField(city, { city = it }, label = { Text("Ort") }, singleLine = true, modifier = Modifier.weight(0.65f))
        }
        OutlinedTextField(website, { website = it }, label = { Text("Webseite") }, singleLine = true, modifier = Modifier.fillMaxWidth())
        DictationField(notes, { notes = it }, "Notiz", minLines = 2)
        Secondary("Kundennummer, Steuerdaten und Ansprechpartner pflegt das Büro.", Modifier.padding(PaddingValues(top = 4.dp)))
    }
}
