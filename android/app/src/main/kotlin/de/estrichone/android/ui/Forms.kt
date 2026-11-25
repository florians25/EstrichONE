package de.estrichone.android.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.CalendarMonth
import androidx.compose.material.icons.filled.Cloud
import androidx.compose.material.icons.filled.AcUnit
import androidx.compose.material.icons.filled.Air
import androidx.compose.material.icons.filled.Grain
import androidx.compose.material.icons.filled.Umbrella
import androidx.compose.material.icons.filled.WbSunny
import androidx.compose.material3.Button
import androidx.compose.material3.DatePicker
import androidx.compose.material3.DatePickerDialog
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.SegmentedButton
import androidx.compose.material3.SegmentedButtonDefaults
import androidx.compose.material3.SingleChoiceSegmentedButtonRow
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.rememberDatePickerState
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.foundation.horizontalScroll
import de.estrichone.core.Drafts
import de.estrichone.core.PhotoSeries
import de.estrichone.core.ProtocolCheck
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import de.estrichone.core.Dates
import de.estrichone.core.DiaryWeather
import de.estrichone.core.Room
import de.estrichone.core.RoomStatus
import de.estrichone.core.ScreedBinder
import de.estrichone.core.WorkItemKind
import de.estrichone.core.cmLimit
import de.estrichone.core.formatNumber
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneOffset

/** Gerüst eines Erfassungsblatts: Titel, Untertitel (Baustelle), Fehler, Speichern. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun FormSheet(
    title: String, bauakteId: String?, onDismiss: () -> Unit, save: String = "Speichern", canSave: Boolean = true, onSave: () -> Unit,
    content: @Composable ColumnScope.() -> Unit,
) {
    val session = LocalSession.current
    var error by remember { mutableStateOf<String?>(null) }
    // Während der Erfassung bleibt der Bildschirm an (nasse Hände, Handschuhe: nicht ständig entsperren).
    KeepScreenOn()
    val subtitle = remember(bauakteId) { bauakteId?.let { session.service.bauakte(it)?.let { b -> "${b.numberText}, ${b.title}" } } }
    ModalBottomSheet(onDismissRequest = onDismiss, sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true), containerColor = tones.canvas) {
        Column(Modifier.verticalScroll(rememberScrollState()).imePadding().padding(horizontal = 20.dp).padding(bottom = 32.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Text(title, style = MaterialTheme.typography.headlineSmall)
            subtitle?.let { Secondary(it) }
            content()
            error?.let { Text(it, color = tones.danger) }
            Button(onClick = {
                try {
                    onSave()
                    onDismiss()
                } catch (e: IllegalArgumentException) {
                    error = e.message
                } catch (e: IllegalStateException) {
                    error = e.message
                }
            }, enabled = canSave, modifier = Modifier.fillMaxWidth().height(56.dp)) { Text(save, style = MaterialTheme.typography.titleMedium) }
        }
    }
}

/** Mehrzeiliges Textfeld mit Diktat. */
@Composable
fun DictationField(value: String, onChange: (String) -> Unit, label: String, minLines: Int = 4) {
    OutlinedTextField(value, onChange, label = { Text(label) }, minLines = minLines, modifier = Modifier.fillMaxWidth(),
        keyboardOptions = KeyboardOptions(capitalization = KeyboardCapitalization.Sentences),
        trailingIcon = { DictationButton { spoken -> onChange(appendDictation(value, spoken)) } })
}

@Composable
fun NumberField(value: String, onChange: (String) -> Unit, label: String, suffix: String? = null, modifier: Modifier = Modifier, decimal: Boolean = false) {
    OutlinedTextField(value, { text -> onChange(text.filter { it.isDigit() || (decimal && (it == ',' || it == '.')) || it == '-' }) },
        label = { Text(label) }, singleLine = true, suffix = suffix?.let { { Text(it) } }, modifier = modifier,
        keyboardOptions = KeyboardOptions(keyboardType = if (decimal) KeyboardType.Decimal else KeyboardType.Number))
}

fun String.toDecimal(): Double? = replace(',', '.').toDoubleOrNull()

private fun weatherIcon(w: DiaryWeather) = when (w) {
    DiaryWeather.Sunny -> Icons.Filled.WbSunny
    DiaryWeather.Cloudy -> Icons.Filled.Cloud
    DiaryWeather.Rain -> Icons.Filled.Umbrella
    DiaryWeather.Snow -> Icons.Filled.AcUnit
    DiaryWeather.Frost -> Icons.Filled.Grain
    DiaryWeather.Wind -> Icons.Filled.Air
}

/** Bautagebuch für heute: Wetter, Temperatur, Personen, Arbeitszeit, Kolonne, Text (wie DiaryViews auf iOS). */
@OptIn(ExperimentalLayoutApi::class)
@Composable
fun DiarySheet(bauakteId: String, prefill: Map<String, String> = emptyMap(), onDismiss: () -> Unit) {
    val session = LocalSession.current
    val model = LocalModel.current
    // Entwurf: bei jeder Eingabe gesichert, nach Abbruch oder Neustart wieder da. Vorgaben (Ankunft, Abschluss) füllen Lücken.
    var generation by remember { mutableIntStateOf(0) }
    val draft = rememberDraft(Drafts.DIARY, bauakteId, generation)
    fun initial(key: String) = draft?.get(key) ?: prefill[key].takeIf { generation == 0 } ?: ""
    var weather by remember(generation) { mutableStateOf(DiaryWeather.entries.firstOrNull { it.raw == draft?.get("weather") }) }
    var temperature by remember(generation) { mutableStateOf(initial("temperature")) }
    var workers by remember(generation) { mutableStateOf(initial("workers")) }
    var start by remember(generation) { mutableStateOf(initial("start")) }
    var end by remember(generation) { mutableStateOf(initial("end")) }
    var text by remember(generation) { mutableStateOf(initial("text")) }
    // Vorgaben allein (Uhrzeiten aus Ankunft und Abschluss) sind kein Entwurf.
    DraftAutosave(Drafts.DIARY, bauakteId, if (text.isBlank() && temperature.isBlank() && workers.isBlank() && weather == null) emptyMap() else mapOf(
        "weather" to (weather?.raw ?: ""), "temperature" to temperature, "workers" to workers, "start" to start, "end" to end, "text" to text))
    val day = remember { LocalDate.now(Dates.zone) }
    // Fotos von heute zu dieser Baustelle (wie auf iOS am Tagebucheintrag).
    val todaysPhotos = remember { session.service.photos(bauakteId).filter { it.time?.atZone(Dates.zone)?.toLocalDate() == day && it.isPhoto } }
    var attachPhotos by remember { mutableStateOf(true) }
    FormSheet("Bautagebuch: ${Dates.relativeDay(day)}", bauakteId, onDismiss, canSave = text.isNotBlank(), onSave = {
        session.service.addDiary(bauakteId, day, text, weather, temperature.toIntOrNull(), workers.toIntOrNull(),
            start.takeIf { it.length == 5 }?.let { "$it:00" }, end.takeIf { it.length == 5 }?.let { "$it:00" },
            photoFileIds = if (attachPhotos) todaysPhotos.map { it.id } else emptyList())
        session.drafts.clear(Drafts.DIARY, bauakteId)
        model.message("Bautagebuch gespeichert.")
    }) {
        if (draft != null) DraftNotice { session.drafts.clear(Drafts.DIARY, bauakteId); generation++ }
        Text("Wetter", style = MaterialTheme.typography.titleSmall)
        FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            DiaryWeather.entries.forEach { w ->
                FilterChip(selected = weather == w, onClick = { weather = if (weather == w) null else w }, label = { Text(w.label) },
                    leadingIcon = { Icon(weatherIcon(w), null, Modifier.padding(0.dp)) })
            }
        }
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            NumberField(temperature, { temperature = it.take(3) }, "Temperatur", "°C", Modifier.weight(1f))
            NumberField(workers, { workers = it.take(2) }, "Personen", null, Modifier.weight(1f))
        }
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            TimeField(start, { start = it }, "Beginn", Modifier.weight(1f))
            TimeField(end, { end = it }, "Ende", Modifier.weight(1f))
        }
        DictationField(text, { text = it }, "Was wurde gemacht?", minLines = 5)
        if (todaysPhotos.isNotEmpty()) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Column(Modifier.weight(1f)) {
                    Text("Fotos von heute anhängen")
                    Secondary("${todaysPhotos.size} Foto${if (todaysPhotos.size == 1) "" else "s"} dieser Baustelle")
                }
                Switch(attachPhotos, { attachPhotos = it })
            }
        }
    }
}

/** Uhrzeit „07:30“. */
@Composable
fun TimeField(value: String, onChange: (String) -> Unit, label: String, modifier: Modifier = Modifier) {
    OutlinedTextField(value, { input ->
        val digits = input.filter(Char::isDigit).take(4)
        onChange(if (digits.length > 2) "${digits.take(2)}:${digits.drop(2)}" else digits)
    }, label = { Text(label) }, placeholder = { Text("hh:mm") }, singleLine = true, modifier = modifier,
        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number))
}

/** CM-Messung: Messort, Bindemittel, beheizt, Dicke, Messwert; Grenzwert und Belegreife sofort sichtbar. */
@Composable
fun CmSheet(bauakteId: String, onDismiss: () -> Unit) {
    val session = LocalSession.current
    val model = LocalModel.current
    var generation by remember { mutableIntStateOf(0) }
    val draft = rememberDraft(Drafts.CM, bauakteId, generation)
    var location by remember(generation) { mutableStateOf(draft?.get("location") ?: "") }
    var binder by remember(generation) { mutableStateOf(ScreedBinder.entries.firstOrNull { it.raw == draft?.get("binder") } ?: ScreedBinder.Cement) }
    var heated by remember(generation) { mutableStateOf(draft?.get("heated") != "false") }
    var thickness by remember(generation) { mutableStateOf(draft?.get("thickness") ?: "") }
    var measured by remember(generation) { mutableStateOf(draft?.get("measured") ?: "") }
    val limit = cmLimit(binder, heated)
    val value = measured.toDecimal()
    // Voreinstellungen allein sind kein Entwurf: erst sichern, wenn etwas eingegeben wurde.
    DraftAutosave(Drafts.CM, bauakteId, if (location.isBlank() && thickness.isBlank() && measured.isBlank()) emptyMap() else mapOf(
        "location" to location, "binder" to binder.raw, "heated" to heated.toString(), "thickness" to thickness, "measured" to measured))
    val missing = ProtocolCheck.cm(location, value)
    val rooms = remember { PhotoSeries.areas(session.service.rooms(bauakteId)) }
    FormSheet("CM-Messung", bauakteId, onDismiss, save = "Messung abschließen", canSave = missing.isEmpty(), onSave = {
        session.service.addCmMeasurement(bauakteId, location, binder, heated, thickness.toIntOrNull(), value ?: -1.0)
        session.drafts.clear(Drafts.CM, bauakteId)
        model.message(if ((value ?: 99.0) <= limit) "Gespeichert: belegreif." else "Gespeichert: noch nicht belegreif.")
    }) {
        if (draft != null) DraftNotice { session.drafts.clear(Drafts.CM, bauakteId); generation++ }
        OutlinedTextField(location, { location = it }, label = { Text("Messort (Raum)") }, singleLine = true, modifier = Modifier.fillMaxWidth(),
            trailingIcon = { DictationButton { location = appendDictation(location, it) } })
        if (rooms.isNotEmpty() && location.isBlank()) {
            Row(Modifier.horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                rooms.take(8).forEach { room -> FilterChip(selected = false, onClick = { location = room }, label = { Text(room) }) }
            }
        }
        SingleChoiceSegmentedButtonRow(Modifier.fillMaxWidth()) {
            ScreedBinder.entries.forEachIndexed { index, b ->
                SegmentedButton(selected = binder == b, onClick = { binder = b }, shape = SegmentedButtonDefaults.itemShape(index, 2)) {
                    Text(if (b == ScreedBinder.Cement) "Zement (CT)" else "Calciumsulfat (CA)")
                }
            }
        }
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text("Beheizt (Fußbodenheizung)", modifier = Modifier.weight(1f))
            Switch(heated, { heated = it })
        }
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            NumberField(thickness, { thickness = it.take(3) }, "Dicke", "mm", Modifier.weight(1f))
            NumberField(measured, { measured = it.take(5) }, "Messwert", "CM-%", Modifier.weight(1f), decimal = true)
        }
        val ready = value != null && value <= limit
        Banner(
            if (value == null) "Grenzwert für Belegreife: ${formatNumber(limit)} CM-%" else if (ready) "Belegreif (Grenzwert ${formatNumber(limit)} CM-%)"
            else "Noch nicht belegreif (Grenzwert ${formatNumber(limit)} CM-%)",
            Icons.Filled.CalendarMonth, if (ready) tones.success else if (value == null) tones.info else tones.warning,
            if (ready) tones.successSoft else if (value == null) tones.infoSoft else tones.warningSoft,
        )
        MissingList(missing)
    }
}

/** Aufgabe oder Mangel mit Fälligkeit (wie WorkItemViews auf iOS). */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun TaskSheet(bauakteId: String?, initialKind: WorkItemKind, onDismiss: () -> Unit) {
    val session = LocalSession.current
    val model = LocalModel.current
    // Mangel und Aufgabe haben getrennte Entwürfe: Wer einen Mangel meldet, bekommt keinen halben Aufgabentext vorgesetzt.
    val form = "${Drafts.TASK}-${initialKind.name.lowercase()}"
    var generation by remember { mutableIntStateOf(0) }
    val draft = rememberDraft(form, bauakteId, generation)
    var kind by remember(generation) { mutableStateOf(initialKind) }
    var title by remember(generation) { mutableStateOf(draft?.get("title") ?: "") }
    var description by remember(generation) { mutableStateOf(draft?.get("description") ?: "") }
    var due by remember(generation) { mutableStateOf(draft?.get("due")?.let { runCatching { LocalDate.parse(it) }.getOrNull() }) }
    var picking by remember { mutableStateOf(false) }
    DraftAutosave(form, bauakteId, if (title.isBlank() && description.isBlank()) emptyMap() else mapOf(
        "title" to title, "description" to description, "due" to (due?.toString() ?: "")))
    FormSheet(if (kind == WorkItemKind.Defect) "Mangel melden" else "Neue Aufgabe", bauakteId, onDismiss, canSave = title.isNotBlank(), onSave = {
        session.service.addTask(bauakteId, kind, title, description, due)
        session.drafts.clear(form, bauakteId)
        model.message(if (kind == WorkItemKind.Defect) "Mangel gespeichert." else "Aufgabe gespeichert.")
    }) {
        if (draft != null) DraftNotice { session.drafts.clear(form, bauakteId); generation++ }
        SingleChoiceSegmentedButtonRow(Modifier.fillMaxWidth()) {
            listOf(WorkItemKind.Task, WorkItemKind.Defect).forEachIndexed { index, k ->
                SegmentedButton(selected = kind == k, onClick = { kind = k }, shape = SegmentedButtonDefaults.itemShape(index, 2)) {
                    Text(if (k == WorkItemKind.Defect) "Mangel" else "Aufgabe")
                }
            }
        }
        OutlinedTextField(title, { title = it }, label = { Text(if (kind == WorkItemKind.Defect) "Was ist mangelhaft?" else "Was ist zu tun?") },
            singleLine = true, modifier = Modifier.fillMaxWidth(), keyboardOptions = KeyboardOptions(capitalization = KeyboardCapitalization.Sentences),
            trailingIcon = { DictationButton { title = appendDictation(title, it) } })
        DictationField(description, { description = it }, "Beschreibung (optional)", minLines = 3)
        Row(verticalAlignment = Alignment.CenterVertically) {
            Icon(Icons.Filled.CalendarMonth, null, tint = tones.textSecondary)
            Text(due?.let { "Fällig ${Dates.relativeDay(it)}" } ?: "Ohne Fälligkeit", modifier = Modifier.weight(1f).padding(start = 10.dp))
            TextButton(onClick = { due = LocalDate.now(Dates.zone) }) { Text("Heute") }
            TextButton(onClick = { due = LocalDate.now(Dates.zone).plusDays(1) }) { Text("Morgen") }
            TextButton(onClick = { picking = true }) { Text("Datum") }
        }
    }
    if (picking) {
        val state = rememberDatePickerState(initialSelectedDateMillis = (due ?: LocalDate.now()).atStartOfDay().toInstant(ZoneOffset.UTC).toEpochMilli())
        DatePickerDialog(onDismissRequest = { picking = false }, confirmButton = {
            TextButton(onClick = {
                state.selectedDateMillis?.let { due = Instant.ofEpochMilli(it).atZone(ZoneOffset.UTC).toLocalDate() }
                picking = false
            }) { Text("Übernehmen") }
        }, dismissButton = { TextButton(onClick = { picking = false }) { Text("Abbrechen") } }) { DatePicker(state) }
    }
}

@Composable
fun NoteSheet(bauakteId: String, onDismiss: () -> Unit) {
    val session = LocalSession.current
    var generation by remember { mutableIntStateOf(0) }
    val draft = rememberDraft(Drafts.NOTE, bauakteId, generation)
    var text by remember(generation) { mutableStateOf(draft?.get("text") ?: "") }
    var pinned by remember(generation) { mutableStateOf(draft?.get("pinned") == "true") }
    DraftAutosave(Drafts.NOTE, bauakteId, if (text.isBlank()) emptyMap() else mapOf("text" to text, "pinned" to pinned.toString()))
    FormSheet("Notiz", bauakteId, onDismiss, canSave = text.isNotBlank(), onSave = {
        session.service.addNote(bauakteId, text, pinned)
        session.drafts.clear(Drafts.NOTE, bauakteId)
    }) {
        if (draft != null) DraftNotice { session.drafts.clear(Drafts.NOTE, bauakteId); generation++ }
        DictationField(text, { text = it }, "Notiz", minLines = 4)
        Row(verticalAlignment = Alignment.CenterVertically) {
            Column(Modifier.weight(1f)) {
                Text("Oben anheften")
                Secondary("Erscheint als Hinweis ganz oben in der Bauakte.")
            }
            Switch(pinned, { pinned = it })
        }
    }
}

/** Raum anlegen bzw. ändern (Fläche, Estrichdicke, Dämmung, Heizung, Stand). */
@Composable
fun RoomSheet(bauakteId: String, room: Room?, onDismiss: () -> Unit) {
    val session = LocalSession.current
    var name by remember { mutableStateOf(room?.name ?: "") }
    var floor by remember { mutableStateOf(room?.floor ?: "EG") }
    var area by remember { mutableStateOf(room?.area?.let { formatNumber(it) } ?: "") }
    var thickness by remember { mutableStateOf(room?.thickness?.toString() ?: "") }
    var insulation by remember { mutableStateOf(room?.insulation?.toString() ?: "") }
    var heated by remember { mutableStateOf(room?.underfloorHeating ?: true) }
    var status by remember { mutableStateOf(room?.status ?: RoomStatus.Planned) }
    var notes by remember { mutableStateOf(room?.notes ?: "") }
    FormSheet(if (room == null) "Neuer Raum" else "Raum bearbeiten", bauakteId, onDismiss, canSave = name.isNotBlank(), onSave = {
        session.service.saveRoom(bauakteId, room, name, floor, area.toDecimal(), thickness.toIntOrNull(), insulation.toIntOrNull(), heated, status, notes)
    }) {
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            OutlinedTextField(name, { name = it }, label = { Text("Raum") }, singleLine = true, modifier = Modifier.weight(0.65f))
            OutlinedTextField(floor, { floor = it }, label = { Text("Geschoss") }, singleLine = true, modifier = Modifier.weight(0.35f))
        }
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            NumberField(area, { area = it }, "Fläche", "m²", Modifier.weight(1f), decimal = true)
            NumberField(thickness, { thickness = it.take(3) }, "Estrich", "mm", Modifier.weight(1f))
            NumberField(insulation, { insulation = it.take(3) }, "Dämmung", "mm", Modifier.weight(1f))
        }
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text("Fußbodenheizung", modifier = Modifier.weight(1f))
            Switch(heated, { heated = it })
        }
        SingleChoiceSegmentedButtonRow(Modifier.fillMaxWidth()) {
            RoomStatus.entries.forEachIndexed { index, s ->
                SegmentedButton(selected = status == s, onClick = { status = s }, shape = SegmentedButtonDefaults.itemShape(index, RoomStatus.entries.size)) { Text(s.label) }
            }
        }
        DictationField(notes, { notes = it }, "Bemerkung (optional)", minLines = 2)
    }
}
