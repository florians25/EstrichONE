package de.estrichone.android.ui

import androidx.compose.material.icons.filled.PlayCircle
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.outlined.Circle
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.ArrowDropDown
import androidx.compose.material.icons.filled.DocumentScanner
import androidx.compose.material.icons.filled.Videocam
import androidx.compose.material.icons.outlined.ReportProblem
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.Email
import androidx.compose.material.icons.automirrored.filled.FactCheck
import androidx.compose.material.icons.filled.Phone
import androidx.compose.material.icons.filled.PhotoCamera
import androidx.compose.material.icons.filled.PhotoLibrary
import androidx.compose.material.icons.filled.PictureAsPdf
import androidx.compose.material.icons.filled.PushPin
import androidx.compose.material.icons.filled.Share
import androidx.compose.material.icons.filled.Thermostat
import androidx.compose.material.icons.filled.UploadFile
import androidx.compose.material.icons.filled.VerifiedUser
import androidx.compose.material.icons.outlined.Description
import androidx.compose.material.icons.outlined.GridView
import androidx.compose.material.icons.automirrored.outlined.MenuBook
import androidx.compose.material.icons.outlined.PhotoLibrary
import androidx.compose.material.icons.outlined.PushPin
import androidx.compose.material.icons.automirrored.outlined.StickyNote2
import androidx.compose.material.icons.automirrored.outlined.Assignment
import androidx.compose.material.icons.outlined.Contacts
import androidx.compose.material3.Button
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExtendedFloatingActionButton
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import de.estrichone.android.support.Links
import de.estrichone.core.CheckResult
import de.estrichone.core.Dates
import de.estrichone.core.DiaryWeather
import de.estrichone.core.DocumentCategory
import de.estrichone.core.FormField
import de.estrichone.core.SiteNote
import de.estrichone.core.WorkItemKind
import de.estrichone.core.display
import de.estrichone.core.formatNumber
import kotlinx.coroutines.launch
import kotlinx.serialization.json.JsonPrimitive
import java.time.Instant
import androidx.compose.material3.TextButton
import androidx.compose.ui.platform.testTag
import de.estrichone.core.InitialInspection
import androidx.compose.material.icons.filled.DoNotDisturbOn
import androidx.compose.material.icons.filled.CalendarMonth
import de.estrichone.core.InitialInspectionOverview
import de.estrichone.core.InitialInspectionStatus
import de.estrichone.core.PlannedJob
import java.time.LocalDate

@Composable
fun AreaScreen(bauakteId: String, area: AreaKind) {
    when (area) {
        AreaKind.Notes -> NotesArea(bauakteId)
        AreaKind.Photos -> PhotosArea(bauakteId)
        AreaKind.Documents -> DocumentsArea(bauakteId)
        AreaKind.Tasks -> TasksArea(bauakteId)
        AreaKind.Diary -> DiaryArea(bauakteId)
        AreaKind.InitialInspection -> InitialInspectionArea(bauakteId)
        AreaKind.Protocols -> ProtocolsArea(bauakteId)
        AreaKind.Rooms -> RoomsArea(bauakteId)
        AreaKind.Contacts -> ContactsArea(bauakteId)
        AreaKind.Project -> ProjectArea(bauakteId)
    }
}

@Composable
private fun AddButton(text: String, onClick: () -> Unit) {
    ExtendedFloatingActionButton(onClick = onClick, icon = { Icon(Icons.Filled.Add, null) }, text = { Text(text) })
}

@Composable
private fun NotesArea(id: String) {
    val session = LocalSession.current
    var adding by remember { mutableStateOf(false) }
    var deleting by remember { mutableStateOf<SiteNote?>(null) }
    val notes by session.local(id) { session.service.notes(id) }
    ScreenScaffold(AreaKind.Notes.title, floating = { AddButton("Notiz") { adding = true } }) { padding ->
        LazyColumn(contentPadding = padding.content(), verticalArrangement = Arrangement.spacedBy(10.dp), modifier = Modifier.readable()) {
            if (notes.isEmpty()) item { EmptyState(Icons.AutoMirrored.Outlined.StickyNote2, "Noch keine Notizen", "Hinweise für alle, die auf der Baustelle arbeiten.") }
            items(notes, key = { it.id }) { note ->
                SectionCard(padding = PaddingValues(16.dp)) {
                    Row(verticalAlignment = Alignment.Top) {
                        Column(Modifier.weight(1f)) {
                            if (note.isPinned) Pill("Angeheftet", MaterialTheme.colorScheme.primary, tones.infoSoft, Icons.Filled.PushPin, Modifier.padding(bottom = 6.dp))
                            Text(note.text, style = MaterialTheme.typography.bodyLarge)
                            Row(Modifier.padding(top = 6.dp), verticalAlignment = Alignment.CenterVertically) {
                                Secondary(note.createdAt?.let { Dates.ago(it) } ?: "")
                                Spacer(Modifier.width(6.dp))
                                SyncMark(note.syncState)
                            }
                        }
                        if (note.authorUserId == session.user.id) {
                            IconButton(onClick = { session.service.setNotePinned(note, !note.isPinned) }) {
                                Icon(if (note.isPinned) Icons.Filled.PushPin else Icons.Outlined.PushPin, if (note.isPinned) "Lösen" else "Anheften")
                            }
                            IconButton(onClick = { deleting = note }) { Icon(Icons.Filled.Delete, "Löschen", tint = tones.textSecondary) }
                        }
                    }
                }
            }
        }
    }
    if (adding) NoteSheet(id) { adding = false }
    deleting?.let { note ->
        ConfirmDialog("Notiz löschen?", "Die Notiz wird für alle entfernt.", "Löschen", { session.service.deleteNote(note) }, { deleting = null })
    }
}

@Composable
private fun PhotosArea(id: String) {
    val session = LocalSession.current
    val nav = LocalNav.current
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    var gallery by remember { mutableStateOf(false) }
    var video by remember { mutableStateOf(false) }
    // Auswahl zum Teilen mehrerer Fotos und Videos (lange drücken startet sie).
    var selected by remember { mutableStateOf<Set<String>>(emptySet()) }
    var sharing by remember { mutableStateOf<String?>(null) }
    val photos by session.local(id) { session.service.photos(id) }
    val selecting = selected.isNotEmpty()
    fun shareSelected() = scope.launch {
        val files = photos.filter { it.id in selected }
        val loaded = files.mapIndexedNotNull { index, file ->
            sharing = if (files.size > 1) "Wird vorbereitet … ${index + 1} von ${files.size}" else "Wird vorbereitet …"
            session.content(file)?.let { Triple(it, file.shareName, file.shareMime) }
        }
        sharing = null
        if (loaded.size < files.size) nav.snackbar.showSnackbar("${files.size - loaded.size} Datei(en) noch nicht übertragen und daher nicht verfügbar.")
        if (loaded.isNotEmpty() && Links.shareAll(context, loaded)) selected = emptySet()
    }
    ScreenScaffold(if (selecting) "${selected.size} ausgewählt" else AreaKind.Photos.title, actions = {
        if (selecting) {
            IconButton(onClick = { shareSelected() }, enabled = sharing == null) { Icon(Icons.Filled.Share, "Auswahl teilen") }
            IconButton(onClick = { selected = emptySet() }) { Icon(Icons.Filled.Close, "Auswahl beenden") }
        } else {
            IconButton(onClick = { gallery = true }) { Icon(Icons.Filled.PhotoLibrary, "Aus Galerie") }
            IconButton(onClick = { video = true }) { Icon(Icons.Filled.Videocam, "Video aufnehmen") }
        }
    }, floating = {
        if (!selecting) ExtendedFloatingActionButton(onClick = { nav.push(Route.Camera(id)) }, icon = { Icon(Icons.Filled.PhotoCamera, null) }, text = { Text("Foto") })
    }) { padding ->
        if (photos.isEmpty()) {
            EmptyState(Icons.Outlined.PhotoLibrary, "Noch keine Fotos", "Fotos werden sofort auf dem Gerät gespeichert und im Hintergrund übertragen.",
                Modifier.padding(padding))
        } else {
            val groups = photos.groupBy { it.time?.atZone(Dates.zone)?.toLocalDate() }
            LazyVerticalGrid(GridCells.Adaptive(112.dp), contentPadding = padding.content(), horizontalArrangement = Arrangement.spacedBy(6.dp),
                verticalArrangement = Arrangement.spacedBy(6.dp)) {
                item(span = { androidx.compose.foundation.lazy.grid.GridItemSpan(maxLineSpan) }) {
                    Secondary(sharing ?: if (selecting) "Weitere durch Tippen auswählen. Teilen über das Symbol oben rechts, auch Videos."
                        else "Lange drücken, um mehrere Fotos oder Videos auszuwählen und zu teilen.", Modifier.padding(start = 4.dp))
                }
                groups.forEach { (day, list) ->
                    item(span = { androidx.compose.foundation.lazy.grid.GridItemSpan(maxLineSpan) }) {
                        GroupLabel(day?.let { "${Dates.relativeDay(it)} (${list.size})" } ?: "Ohne Datum", Modifier.padding(start = 4.dp))
                    }
                    items(list, key = { it.id }) { file ->
                        val isSelected = file.id in selected
                        Box(Modifier.fillMaxWidth().aspectRatio(1f)) {
                            Thumbnail(file, session, 200.dp, Modifier.fillMaxSize(),
                                onLongClick = { selected = selected + file.id }) {
                                if (selecting) selected = if (isSelected) selected - file.id else selected + file.id else nav.push(Route.Photo(file.id))
                            }
                            if (selecting) {
                                Icon(if (isSelected) Icons.Filled.CheckCircle else Icons.Outlined.Circle, if (isSelected) "Ausgewählt" else "Nicht ausgewählt",
                                    tint = if (isSelected) MaterialTheme.colorScheme.primary else Color.White,
                                    modifier = Modifier.align(Alignment.TopStart).padding(6.dp).size(26.dp).clip(CircleShape)
                                        .background(if (isSelected) Color.White else Color.Black.copy(alpha = 0.3f)))
                            }
                        }
                    }
                }
            }
        }
    }
    if (gallery) GalleryImport(id) { gallery = false }
    if (video) VideoCapture(id) { video = false }
}

/** Großansicht eines Fotos mit Beschriftung, Teilen und Löschen. */
@Composable
fun PhotoScreen(fileId: String) {
    val session = LocalSession.current
    val nav = LocalNav.current
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val file by session.local(fileId) { session.service.file(fileId) }
    var caption by remember(file?.caption) { mutableStateOf(file?.caption ?: "") }
    var deleting by remember { mutableStateOf(false) }
    var loading by remember { mutableStateOf<String?>(null) }
    val f = file ?: run { ScreenScaffold("Foto") { EmptyState(Icons.Outlined.PhotoLibrary, "Foto nicht gefunden", null, Modifier.padding(it)) }; return }
    val bitmap by produceState<android.graphics.Bitmap?>(null, fileId) { value = session.thumbnail(f, 1600) }
    ScreenScaffold(f.displayName, actions = {
        IconButton(onClick = {
            scope.launch {
                loading = if (f.isVideo) "Video wird vorbereitet …" else "Wird vorbereitet …"
                val bytes = session.content(f)
                loading = null
                if (bytes == null) nav.snackbar.showSnackbar("Noch nicht verfügbar, die Übertragung steht aus.")
                else Links.share(context, bytes, f.shareName, f.shareMime)
            }
        }, enabled = loading == null) {
            Icon(Icons.Filled.Share, if (f.isVideo) "Video teilen" else "Teilen")
        }
        if (f.entity.data["uploadedByUserId"] == null || (f.entity.data["uploadedByUserId"] as? JsonPrimitive)?.content == session.user.id) {
            IconButton(onClick = { deleting = true }) { Icon(Icons.Filled.Delete, "Löschen") }
        }
    }) { padding ->
        Column(Modifier.padding(padding).verticalScroll(rememberScrollState()).padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Box(Modifier.fillMaxWidth().aspectRatio(4f / 3f).clip(RoundedCornerShape(18.dp)).background(Color.Black), contentAlignment = Alignment.Center) {
                bitmap?.let { Image(it.asImageBitmap(), f.displayName, contentScale = ContentScale.Fit, modifier = Modifier.fillMaxSize()) }
                    ?: if (f.isVideo) Icon(Icons.Filled.Videocam, null, tint = Color.White.copy(alpha = 0.5f), modifier = Modifier.size(56.dp))
                    else androidx.compose.material3.CircularProgressIndicator()
                if (f.isVideo) {
                    // Video mit der Abspiel-App des Geräts öffnen (echte Datei mit Videoendung).
                    IconButton(onClick = {
                        scope.launch {
                            loading = "Video wird geladen …"
                            val bytes = session.content(f)
                            loading = null
                            if (bytes == null || !Links.open(context, bytes, f.shareName, f.shareMime)) {
                                nav.snackbar.showSnackbar(if (bytes == null) "Noch nicht verfügbar, die Übertragung steht aus." else "Keine App zum Abspielen gefunden.")
                            }
                        }
                    }, modifier = Modifier.size(72.dp)) {
                        Icon(Icons.Filled.PlayCircle, "Video abspielen", tint = Color.White, modifier = Modifier.size(64.dp))
                    }
                }
                loading?.let {
                    Row(Modifier.align(Alignment.BottomCenter).padding(12.dp).clip(RoundedCornerShape(50)).background(Color.Black.copy(alpha = 0.6f))
                        .padding(horizontal = 12.dp, vertical = 6.dp), verticalAlignment = Alignment.CenterVertically) {
                        androidx.compose.material3.CircularProgressIndicator(Modifier.size(16.dp), color = Color.White, strokeWidth = 2.dp)
                        Spacer(Modifier.width(8.dp))
                        Text(it, color = Color.White, style = MaterialTheme.typography.labelLarge)
                    }
                }
            }
            Row(verticalAlignment = Alignment.CenterVertically) {
                Secondary(listOfNotNull(f.time?.let { Dates.dateTime(it) }, if (f.isStoredOnServer) "übertragen" else "wird übertragen").joinToString(" · "),
                    Modifier.weight(1f))
                SyncMark(f.syncState)
            }
            OutlinedTextField(caption, { caption = it }, label = { Text("Beschriftung") }, modifier = Modifier.fillMaxWidth(),
                trailingIcon = { DictationButton { caption = appendDictation(caption, it) } })
            Button(onClick = { session.service.updateFile(f, caption, f.category); nav.back() }, enabled = caption != (f.caption ?: ""),
                modifier = Modifier.fillMaxWidth()) { Text("Beschriftung speichern") }
        }
    }
    if (deleting) ConfirmDialog("Foto löschen?", "Das Foto wird aus der Bauakte entfernt.", "Löschen", { session.service.deleteFile(f); nav.back() }, { deleting = false })
}

@Composable
private fun DocumentsArea(id: String) {
    val session = LocalSession.current
    val nav = LocalNav.current
    val context = LocalContext.current
    val model = LocalModel.current
    val scope = rememberCoroutineScope()
    var importing by remember { mutableStateOf(false) }
    val documents by session.local(id) { session.service.documents(id) }
    ScreenScaffold(AreaKind.Documents.title, actions = {
        IconButton(onClick = { nav.push(Route.Camera(id, scan = true)) }) { Icon(Icons.Filled.DocumentScanner, "Scannen") }
    }, floating = {
        ExtendedFloatingActionButton(onClick = { importing = true }, icon = { Icon(Icons.Filled.UploadFile, null) }, text = { Text("Datei") })
    }) { padding ->
        LazyColumn(contentPadding = padding.content(), modifier = Modifier.readable()) {
            if (documents.isEmpty()) item { EmptyState(Icons.Outlined.Description, "Keine Dokumente", "Lieferscheine, Pläne und Protokolle scannen oder als Datei ablegen.") }
            documents.groupBy { it.category ?: DocumentCategory.Other }.toSortedMap(compareBy { it.ordinal }).forEach { (category, list) ->
                item { GroupLabel("${category.title} (${list.size})") }
                item {
                    SectionCard {
                        list.forEachIndexed { index, file ->
                            if (index > 0) RowDivider()
                            RowItem(file.displayName, icon = Icons.Filled.PictureAsPdf, iconTint = tones.danger, iconBackground = tones.dangerSoft,
                                subtitle = listOfNotNull(file.time?.let { Dates.ago(it) }, "${(file.sizeBytes / 1024).coerceAtLeast(1)} KB").joinToString(" · "),
                                onClick = {
                                    scope.launch {
                                        val bytes = session.content(file)
                                        if (bytes == null || !Links.open(context, bytes, file.originalName.ifBlank { "Dokument.pdf" }, file.contentType)) {
                                            model.message("Das Dokument kann gerade nicht geöffnet werden.")
                                        }
                                    }
                                }, trailing = { SyncMark(file.syncState) })
                        }
                    }
                }
            }
        }
    }
    if (importing) DocumentImport(id) { importing = false }
}

@Composable
private fun TasksArea(id: String) {
    val session = LocalSession.current
    var adding by remember { mutableStateOf<WorkItemKind?>(null) }
    var showDone by remember { mutableStateOf(false) }
    val tasks by session.local(id) { session.service.tasks(id) }
    val visible = tasks.filter { showDone || it.isOpen }
    ScreenScaffold(AreaKind.Tasks.title, actions = {
        IconButton(onClick = { adding = WorkItemKind.Defect }) { Icon(Icons.Outlined.ReportProblem, "Mangel melden") }
    }, floating = { AddButton("Aufgabe") { adding = WorkItemKind.Task } }) { padding ->
        LazyColumn(contentPadding = padding.content(), modifier = Modifier.readable()) {
            item {
                Row(Modifier.padding(bottom = 8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    FilterChip(!showDone, { showDone = false }, label = { Text("Offen (${tasks.count { it.isOpen }})") })
                    FilterChip(showDone, { showDone = true }, label = { Text("Alle (${tasks.size})") })
                }
            }
            if (visible.isEmpty()) item { EmptyState(Icons.AutoMirrored.Outlined.Assignment, "Keine offenen Aufgaben", "Offene Aufgaben und Mängel erscheinen hier.") }
            else item {
                SectionCard { visible.forEachIndexed { index, task -> if (index > 0) RowDivider(56.dp); TaskRow(task, session, task.description) } }
            }
        }
    }
    adding?.let { kind -> TaskSheet(id, kind) { adding = null } }
}

@Composable
private fun DiaryArea(id: String) {
    val session = LocalSession.current
    var adding by remember { mutableStateOf(false) }
    val entries by session.local(id) { session.service.diary(id) }
    ScreenScaffold(AreaKind.Diary.title, floating = { AddButton("Eintrag") { adding = true } }) { padding ->
        LazyColumn(contentPadding = padding.content(), verticalArrangement = Arrangement.spacedBy(10.dp), modifier = Modifier.readable()) {
            if (entries.isEmpty()) item { EmptyState(Icons.AutoMirrored.Outlined.MenuBook, "Noch kein Bautagebuch", "Ein Eintrag je Tag: Wetter, Personen, was gemacht wurde.") }
            items(entries, key = { it.id }) { entry ->
                SectionCard(padding = PaddingValues(16.dp)) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text(entry.day?.let { Dates.long(it) } ?: "", style = MaterialTheme.typography.titleMedium, modifier = Modifier.weight(1f))
                        SyncMark(entry.syncState)
                    }
                    val facts = listOfNotNull(entry.weather?.label, entry.temperature?.let { "$it °C" }, entry.workers?.let { "$it Personen" },
                        listOfNotNull(Dates.clock(entry.startTime), Dates.clock(entry.endTime)).joinToString("–").ifEmpty { null }, entry.crew)
                    if (facts.isNotEmpty()) Secondary(facts.joinToString(" · "), Modifier.padding(top = 2.dp, bottom = 6.dp))
                    Text(entry.text, style = MaterialTheme.typography.bodyLarge)
                }
            }
        }
    }
    if (adding) DiarySheet(id) { adding = false }
}

/**
 * Bereich „Erstbegehung“ wie der Reiter „Erstbegehung“ der Dokumentation im Büro: Status (offen, geplant, durchgeführt,
 * nicht erforderlich), Termin aus der Planung, Durchführen bzw. Vermerk, alle Protokolle, Fotos und abgelegte Protokoll-PDFs.
 */
@Composable
private fun InitialInspectionArea(id: String) {
    val session = LocalSession.current
    val nav = LocalNav.current
    val context = LocalContext.current
    val model = LocalModel.current
    val scope = rememberCoroutineScope()
    val inspections by session.local(id) { session.service.inspections(id) }
    val bauakte by session.local(id) { session.service.bauakte(id) }
    val photos by session.local(id) { session.service.photos(id) }
    val documents by session.local(id) { session.service.documents(id).filter { InitialInspectionOverview.isProtocolDocument(it) } }
    val jobs by produceState<List<PlannedJob>>(emptyList(), id) { value = session.weekPlan()?.assignments.orEmpty() }
    var waiving by remember { mutableStateOf(false) }
    val canWaive = canWaiveInitialInspection(session)
    val b = bauakte
    val overview = b?.let { InitialInspectionOverview(it, inspections, jobs, LocalDate.now(Dates.zone).toString()) }
    val pending = overview?.status == InitialInspectionStatus.Open || overview?.status == InitialInspectionStatus.Planned
    val palette = tones
    val (tint, soft) = when (overview?.status) {
        InitialInspectionStatus.Planned -> palette.info to palette.infoSoft
        InitialInspectionStatus.Done -> palette.success to palette.successSoft
        InitialInspectionStatus.Waived -> palette.textSecondary to palette.fillStrong
        else -> palette.warning to palette.warningSoft
    }
    ScreenScaffold(AreaKind.InitialInspection.title, floating = {
        if (pending) AddButton("Durchführen") { nav.push(Route.Inspection(id, null)) }
    }) { padding ->
        LazyColumn(contentPadding = padding.content(), modifier = Modifier.readable()) {
            if (b == null || overview == null) return@LazyColumn
            item { GroupLabel("Status") }
            item {
                SectionCard {
                    RowItem(overview.status.title, modifier = Modifier.testTag("initialInspectionStatus"), icon = Icons.AutoMirrored.Filled.FactCheck,
                        iconTint = tint, iconBackground = soft,
                        subtitle = overview.protocols.firstOrNull()?.performedAt?.let { "Durchgeführt am ${Dates.dateTime(it)}" })
                    RowDivider()
                    RowItem("Termin", modifier = Modifier.testTag("initialInspectionAppointment"), icon = Icons.Filled.CalendarMonth,
                        subtitle = overview.appointment?.let { InitialInspectionOverview.appointmentText(it) } ?: "nicht geplant – Termine plant das Büro")
                }
            }
            when (overview.status) {
                InitialInspectionStatus.Waived -> item { InitialInspectionWaiverCard(session, b) }
                InitialInspectionStatus.Open, InitialInspectionStatus.Planned -> if (canWaive) {
                    item {
                        TextButton(onClick = { waiving = true }, modifier = Modifier.padding(top = 4.dp).testTag("waiveInitialInspectionButton")) {
                            Icon(Icons.Filled.DoNotDisturbOn, null)
                            Spacer(Modifier.width(8.dp))
                            Text(InitialInspection.WAIVE_BUTTON)
                        }
                    }
                }
                InitialInspectionStatus.Done -> Unit
            }
            if (overview.protocols.isNotEmpty()) {
                item { GroupLabel("Protokolle (${overview.protocols.size})") }
                item {
                    SectionCard {
                        overview.protocols.forEachIndexed { index, i ->
                            if (index > 0) RowDivider()
                            RowItem(i.title, icon = Icons.AutoMirrored.Filled.FactCheck,
                                iconTint = if (i.defects > 0) tones.warning else tones.success,
                                iconBackground = if (i.defects > 0) tones.warningSoft else tones.successSoft,
                                subtitle = (listOfNotNull(i.performedAt?.let { Dates.dateTime(it) }) +
                                    listOfNotNull(if (i.notRequired) "nicht erforderlich" else null, if (i.defects > 0) "${i.defects} Mangel" else "ohne Mängel")).joinToString(" · "),
                                showChevron = true, onClick = { nav.push(Route.Inspection(id, i.id)) }, trailing = { SyncMark(i.syncState) })
                        }
                    }
                }
            }
            val shown = photos.filter { it.id in overview.photoIds }
            if (shown.isNotEmpty() || documents.isNotEmpty()) {
                item { GroupLabel("Fotos und Dokumente (${shown.size + documents.size})") }
                item {
                    SectionCard {
                        shown.forEachIndexed { index, photo ->
                            if (index > 0) RowDivider()
                            RowItem(photo.caption ?: "Foto", icon = Icons.Filled.PhotoCamera,
                                subtitle = photo.time?.let { Dates.ago(it) }, showChevron = true, onClick = { nav.push(Route.Photo(photo.id)) })
                        }
                        documents.forEachIndexed { index, file ->
                            if (index > 0 || shown.isNotEmpty()) RowDivider()
                            RowItem(file.displayName, icon = Icons.Filled.PictureAsPdf, iconTint = tones.danger, iconBackground = tones.dangerSoft,
                                subtitle = file.time?.let { Dates.ago(it) },
                                onClick = {
                                    scope.launch {
                                        val bytes = session.content(file)
                                        if (bytes == null || !Links.open(context, bytes, file.originalName.ifBlank { "Erstbegehungsprotokoll.pdf" }, file.contentType)) {
                                            model.message("Das Dokument kann gerade nicht geöffnet werden.")
                                        }
                                    }
                                }, trailing = { SyncMark(file.syncState) })
                        }
                    }
                }
            }
        }
    }
    if (waiving) WaiveInitialInspectionDialog(session, id) { waiving = false }
}

/** Abnahmen und CM-Messungen (die Erstbegehung hat einen eigenen Bereich). */
@Composable
private fun ProtocolsArea(id: String) {
    val session = LocalSession.current
    val nav = LocalNav.current
    var menu by remember { mutableStateOf(false) }
    var cm by remember { mutableStateOf(false) }
    val measurements by session.local(id) { session.service.inspections(id).filter { it.isCmMeasurement } }
    val acceptances by session.local(id) { session.service.acceptances(id) }
    ScreenScaffold(AreaKind.Protocols.title, floating = {
        Box {
            AddButton("Protokoll") { menu = true }
            DropdownMenu(menu, { menu = false }) {
                DropdownMenuItem(text = { Text("CM-Messung") }, leadingIcon = { Icon(Icons.Filled.Thermostat, null) }, onClick = { menu = false; cm = true })
                DropdownMenuItem(text = { Text("Abnahme") }, leadingIcon = { Icon(Icons.Filled.VerifiedUser, null) }, onClick = { menu = false; nav.push(Route.Acceptance(id, null)) })
            }
        }
    }) { padding ->
        LazyColumn(contentPadding = padding.content(), modifier = Modifier.readable()) {
            if (measurements.isEmpty() && acceptances.isEmpty()) {
                item { EmptyState(Icons.Filled.VerifiedUser, "Noch keine Protokolle", "CM-Messungen und die Abnahme werden hier festgehalten.") }
            }
            if (acceptances.isNotEmpty()) {
                item { GroupLabel("Abnahmen") }
                item {
                    SectionCard {
                        acceptances.forEachIndexed { index, a ->
                            if (index > 0) RowDivider()
                            RowItem("Abnahme · ${a.result?.label ?: "offen"}", icon = Icons.Filled.VerifiedUser,
                                subtitle = listOfNotNull(a.performedAt?.let { Dates.dateTime(it) }, a.stateLabel).joinToString(" · "), showChevron = true,
                                onClick = { nav.push(Route.Acceptance(id, a.id)) }, trailing = { SyncMark(a.syncState) })
                        }
                    }
                }
            }
            if (measurements.isNotEmpty()) {
                item { GroupLabel("CM-Messungen") }
                item {
                    SectionCard {
                        measurements.forEachIndexed { index, i ->
                            if (index > 0) RowDivider()
                            val subtitle = listOfNotNull(i.location, i.measured?.let { "${formatNumber(it)} CM-%" },
                                when (i.isReady) { true -> "belegreif"; false -> "nicht belegreif"; null -> null })
                            RowItem(i.title, icon = Icons.Filled.Thermostat,
                                iconTint = if (i.isReady == false) tones.warning else tones.success,
                                iconBackground = if (i.isReady == false) tones.warningSoft else tones.successSoft,
                                subtitle = (listOfNotNull(i.performedAt?.let { Dates.dateTime(it) }) + subtitle).joinToString(" · "),
                                trailing = { SyncMark(i.syncState) })
                        }
                    }
                }
            }
        }
    }
    if (cm) CmSheet(id) { cm = false }
}

@Composable
private fun RoomsArea(id: String) {
    val session = LocalSession.current
    var editing by remember { mutableStateOf<de.estrichone.core.Room?>(null) }
    var adding by remember { mutableStateOf(false) }
    val rooms by session.local(id) { session.service.rooms(id) }
    ScreenScaffold(AreaKind.Rooms.title, floating = { AddButton("Raum") { adding = true } }) { padding ->
        LazyColumn(contentPadding = padding.content(), modifier = Modifier.readable()) {
            if (rooms.isEmpty()) item { EmptyState(Icons.Outlined.GridView, "Noch keine Räume", "Räume mit Fläche und Aufbau erfassen. Die Summe erscheint hier.") }
            else item {
                val total = rooms.sumOf { it.area ?: 0.0 }
                val laid = rooms.filter { it.status != de.estrichone.core.RoomStatus.Planned }.sumOf { it.area ?: 0.0 }
                SectionCard(padding = PaddingValues(16.dp)) {
                    Row {
                        Column(Modifier.weight(1f)) { Secondary("Gesamtfläche"); Text("${formatNumber(total)} m²", style = MaterialTheme.typography.headlineSmall) }
                        Column(Modifier.weight(1f)) { Secondary("Eingebracht"); Text("${formatNumber(laid)} m²", style = MaterialTheme.typography.headlineSmall, color = tones.success) }
                    }
                    androidx.compose.material3.LinearProgressIndicator(progress = { if (total > 0) (laid / total).toFloat() else 0f },
                        modifier = Modifier.fillMaxWidth().padding(top = 12.dp), color = tones.success, trackColor = tones.fill)
                }
            }
            rooms.groupBy { it.floor ?: "Ohne Geschoss" }.forEach { (floor, list) ->
                item { GroupLabel("$floor · ${formatNumber(list.sumOf { it.area ?: 0.0 })} m²") }
                item {
                    SectionCard {
                        list.forEachIndexed { index, room ->
                            if (index > 0) RowDivider(16.dp)
                            RowItem(room.name, subtitle = listOfNotNull(room.thickness?.let { "$it mm" }, if (room.underfloorHeating) "FBH" else null, room.notes).joinToString(" · "),
                                value = room.area?.let { "${formatNumber(it)} m²" }, onClick = { editing = room }, trailing = {
                                    Spacer(Modifier.width(8.dp))
                                    Pill(room.status.label, if (room.status == de.estrichone.core.RoomStatus.Planned) tones.textSecondary else tones.success,
                                        if (room.status == de.estrichone.core.RoomStatus.Planned) tones.fill else tones.successSoft)
                                })
                        }
                    }
                }
            }
        }
    }
    if (adding) RoomSheet(id, null) { adding = false }
    editing?.let { RoomSheet(id, it) { editing = null } }
}

@Composable
private fun ContactsArea(id: String) {
    val session = LocalSession.current
    val nav = LocalNav.current
    val context = LocalContext.current
    val bauakte = remember { session.service.bauakte(id) }
    val customer = remember { session.service.contact(bauakte?.customerId) }
    val parties by session.local(id) { session.service.parties(id) }
    ScreenScaffold(AreaKind.Contacts.title) { padding ->
        LazyColumn(contentPadding = padding.content(), verticalArrangement = Arrangement.spacedBy(10.dp), modifier = Modifier.readable()) {
            customer?.let { c ->
                item {
                    PersonCard("Bauherr/Kunde", c.name, listOfNotNull(c.customerNumber, c.addressLine).joinToString(" · "), c.phone, c.email,
                        onOpen = { nav.push(Route.Customer(c.id)) }, context = context)
                }
            }
            bauakte?.siteContactName?.let { name -> item { PersonCard("Ansprechpartner vor Ort", name, null, bauakte.siteContactPhone, null, null, context) } }
            items(parties, key = { it.id }) { p ->
                PersonCard(p.role.label + if (p.isPrimary) " · Hauptansprechpartner" else "", p.name,
                    listOfNotNull(p.company, p.addressLine, p.note).joinToString("\n").ifEmpty { null }, p.bestPhone, p.email, null, context)
            }
            if (customer == null && parties.isEmpty()) item { EmptyState(Icons.Outlined.Contacts, "Keine Kontakte", "Beteiligte pflegt das Büro.") }
        }
    }
}

@Composable
private fun PersonCard(role: String, name: String, detail: String?, phone: String?, email: String?, onOpen: (() -> Unit)?, context: android.content.Context) {
    SectionCard(Modifier.then(if (onOpen != null) Modifier.clickable(onClick = onOpen) else Modifier), padding = PaddingValues(16.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Avatar(de.estrichone.core.initials(name))
            Spacer(Modifier.width(12.dp))
            Column(Modifier.weight(1f)) {
                Text(role, style = MaterialTheme.typography.labelLarge, color = MaterialTheme.colorScheme.primary)
                Text(name, style = MaterialTheme.typography.titleMedium)
                detail?.let { Secondary(it) }
            }
            if (email != null) IconButton(onClick = { Links.mail(context, email) }) { Icon(Icons.Filled.Email, "E-Mail an $name") }
            if (phone != null) CircleAction(Icons.Filled.Phone, "$name anrufen", { Links.call(context, phone) })
        }
    }
}

/** Basisdaten nach dem Formular-Schema bearbeiten (wie ProjectViews auf iOS). Felder unter `project.*`. */
@Composable
private fun ProjectArea(id: String) {
    val session = LocalSession.current
    val nav = LocalNav.current
    val model = LocalModel.current
    val bauakte = remember { session.service.bauakte(id) } ?: return
    val schema = remember { session.service.schema("bauakte") }
    val lists = remember { session.service.valueLists() }
    val values = remember { mutableStateMapOf<String, String>() }
    val bools = remember { mutableStateMapOf<String, Boolean>() }
    var title by remember { mutableStateOf(bauakte.title) }
    var street by remember { mutableStateOf(bauakte.street ?: "") }
    var postalCode by remember { mutableStateOf(bauakte.postalCode ?: "") }
    var city by remember { mutableStateOf(bauakte.city ?: "") }
    var description by remember { mutableStateOf(bauakte.description ?: "") }
    fun key(field: FormField) = field.path.removePrefix("project.")
    fun current(field: FormField) = (bauakte.project[key(field)] as? JsonPrimitive)

    ScreenScaffold("Basisdaten", actions = {
        androidx.compose.material3.TextButton(onClick = {
            val project = bauakte.project.toMutableMap()
            schema?.groups?.flatMap { it.fields }?.forEach { field ->
                val k = key(field)
                when (field.type) {
                    "boolean" -> bools[k]?.let { project[k] = JsonPrimitive(it) }
                    "number" -> values[k]?.let { project[k] = it.toDecimal()?.let(::JsonPrimitive) ?: kotlinx.serialization.json.JsonNull }
                    "integer" -> values[k]?.let { project[k] = it.toIntOrNull()?.let(::JsonPrimitive) ?: kotlinx.serialization.json.JsonNull }
                    else -> values[k]?.let { project[k] = it.trim().ifEmpty { null }?.let(::JsonPrimitive) ?: kotlinx.serialization.json.JsonNull }
                }
            }
            val changes = mutableMapOf<String, Any?>()
            if (title.trim() != bauakte.title && title.isNotBlank()) changes["title"] = title.trim()
            if (street.trim() != (bauakte.street ?: "")) changes["street"] = street
            if (postalCode.trim() != (bauakte.postalCode ?: "")) changes["postalCode"] = postalCode
            if (city.trim() != (bauakte.city ?: "")) changes["city"] = city
            if (description.trim() != (bauakte.description ?: "")) changes["description"] = description
            if (kotlinx.serialization.json.JsonObject(project) != bauakte.project) changes["project"] = kotlinx.serialization.json.JsonObject(project)
            if (changes.isNotEmpty()) {
                session.service.updateBauakte(id, changes)
                model.message("Gespeichert.")
            }
            nav.back()
        }) { Text("Sichern") }
    }) { padding ->
        Column(Modifier.padding(padding).verticalScroll(rememberScrollState()).padding(16.dp).readable(), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            OutlinedTextField(title, { title = it }, label = { Text("Bauvorhaben") }, singleLine = true, modifier = Modifier.fillMaxWidth())
            OutlinedTextField(street, { street = it }, label = { Text("Straße") }, singleLine = true, modifier = Modifier.fillMaxWidth())
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                OutlinedTextField(postalCode, { postalCode = it }, label = { Text("PLZ") }, singleLine = true, modifier = Modifier.weight(0.35f))
                OutlinedTextField(city, { city = it }, label = { Text("Ort") }, singleLine = true, modifier = Modifier.weight(0.65f))
            }
            DictationField(description, { description = it }, "Beschreibung", minLines = 2)
            schema?.groups?.forEach { group ->
                Text(group.label, style = SectionTitle, modifier = Modifier.padding(top = 12.dp))
                group.fields.forEach { field ->
                    val k = key(field)
                    when (field.type) {
                        "boolean" -> Row(verticalAlignment = Alignment.CenterVertically) {
                            Text(field.label, modifier = Modifier.weight(1f))
                            Switch(bools[k] ?: (current(field)?.content == "true"), { bools[k] = it })
                        }
                        "select" -> SelectField(field.label, values[k] ?: current(field)?.content, lists.items(field.list ?: "").map { it.key to it.label }) { values[k] = it }
                        else -> OutlinedTextField(values[k] ?: (field.display(current(field), lists)?.removeSuffix(" ${field.unit}") ?: ""), { values[k] = it },
                            label = { Text(field.label) }, supportingText = field.help?.let { { Text(it) } }, suffix = field.unit?.let { { Text(it) } },
                            minLines = if (field.type == "textarea") 3 else 1, singleLine = field.type != "textarea", modifier = Modifier.fillMaxWidth())
                    }
                }
            }
            Spacer(Modifier.height(80.dp))
        }
    }
}

/** Auswahl aus einer Werteliste. */
@Composable
fun SelectField(label: String, selected: String?, options: List<Pair<String, String>>, onSelect: (String) -> Unit) {
    var open by remember { mutableStateOf(false) }
    Box {
        OutlinedTextField(options.firstOrNull { it.first == selected }?.second ?: selected ?: "", {}, label = { Text(label) }, readOnly = true,
            modifier = Modifier.fillMaxWidth(), trailingIcon = { Icon(Icons.Filled.ArrowDropDown, null) })
        Box(Modifier.matchParentSize().clickable { open = true })
        DropdownMenu(open, { open = false }) {
            options.forEach { (key, text) -> DropdownMenuItem(text = { Text(text) }, onClick = { onSelect(key); open = false }) }
        }
    }
}
