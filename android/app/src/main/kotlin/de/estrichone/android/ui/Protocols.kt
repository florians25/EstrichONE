package de.estrichone.android.ui

import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Paint
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.Warning
import androidx.compose.material3.Button
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FilterChipDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.unit.IntSize
import androidx.compose.ui.unit.dp
import de.estrichone.android.support.Images
import de.estrichone.core.AcceptanceResult
import de.estrichone.core.CheckResult
import de.estrichone.core.Dates
import de.estrichone.core.display
import kotlinx.serialization.json.JsonPrimitive

/** Erstbegehung nach dem Formular-Schema „erstbegehung“ (Prüfpunkte, Bemerkungen, Messwerte); vorhandene nur ansehen. */
@Composable
fun InspectionScreen(bauakteId: String, id: String?) {
    val session = LocalSession.current
    val nav = LocalNav.current
    val model = LocalModel.current
    val schema = remember { session.service.schema("erstbegehung") }
    val lists = remember { session.service.valueLists() }
    val existing = remember(id) { id?.let { session.service.inspections(bauakteId).firstOrNull { i -> i.id == it } } }
    val checks = remember { mutableStateMapOf<String, CheckResult>().apply { existing?.checks?.let { putAll(it) } } }
    val remarks = remember { mutableStateMapOf<String, String>().apply { existing?.checkRemarks?.let { putAll(it) } } }
    val values = remember { mutableStateMapOf<String, String>() }
    var notRequired by remember { mutableStateOf(existing?.notRequired ?: false) }
    var notes by remember { mutableStateOf(existing?.notes ?: "") }
    var error by remember { mutableStateOf<String?>(null) }
    val readOnly = existing != null
    val fields = schema?.groups.orEmpty()

    ScreenScaffold(if (readOnly) "Erstbegehung" else "Neue Erstbegehung") { padding ->
        Column(Modifier.padding(padding).verticalScroll(rememberScrollState()).padding(16.dp).readable(), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            existing?.performedAt?.let { Secondary("Durchgeführt ${Dates.dateTime(it)}") }
            if (!readOnly) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Column(Modifier.weight(1f)) {
                        Text("Erstbegehung nicht erforderlich")
                        Secondary("z. B. bei Folgeaufträgen ohne Änderungen")
                    }
                    Switch(notRequired, { notRequired = it })
                }
            }
            if (!notRequired) {
                fields.forEach { group ->
                    Text(group.label, style = SectionTitle, modifier = Modifier.padding(top = 12.dp))
                    group.fields.forEach { field ->
                        val key = field.path.substringAfterLast('.')
                        if (field.type == "check") {
                            SectionCard(padding = PaddingValues(14.dp)) {
                                Text(field.label, style = MaterialTheme.typography.bodyLarge)
                                Row(Modifier.padding(top = 8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                    CheckResult.entries.forEach { result ->
                                        val color = when (result) {
                                            CheckResult.Ok -> tones.success
                                            CheckResult.NotOk -> tones.danger
                                            CheckResult.NotApplicable -> tones.textSecondary
                                        }
                                        FilterChip(selected = checks[key] == result, enabled = !readOnly, onClick = { checks[key] = result },
                                            label = { Text(result.label) }, colors = FilterChipDefaults.filterChipColors(
                                                selectedContainerColor = color.copy(alpha = 0.16f), selectedLabelColor = color,
                                                disabledSelectedContainerColor = color.copy(alpha = 0.16f)))
                                    }
                                }
                                if (checks[key] == CheckResult.NotOk || remarks[key] != null) {
                                    OutlinedTextField(remarks[key] ?: "", { remarks[key] = it }, label = { Text("Bemerkung") }, readOnly = readOnly,
                                        modifier = Modifier.fillMaxWidth().padding(top = 8.dp),
                                        trailingIcon = if (readOnly) null else { { DictationButton { remarks[key] = appendDictation(remarks[key] ?: "", it) } } })
                                }
                            }
                        } else if (readOnly) {
                            field.display(existing?.values?.get(key), lists)?.let { RowItem(field.label, value = it) }
                        } else if (field.type == "boolean") {
                            Row(verticalAlignment = Alignment.CenterVertically) {
                                Text(field.label, modifier = Modifier.weight(1f))
                                Switch(values[key] == "true", { values[key] = it.toString() })
                            }
                        } else if (field.type == "select") {
                            SelectField(field.label, values[key], lists.items(field.list ?: "").map { it.key to it.label }) { values[key] = it }
                        } else {
                            NumberField(values[key] ?: "", { values[key] = it }, field.label, field.unit, Modifier.fillMaxWidth(), decimal = field.type == "number")
                        }
                    }
                }
            }
            if (readOnly && existing?.notes.isNullOrBlank()) Unit else DictationField(notes, { notes = it }, "Notizen", minLines = 3)
            error?.let { Text(it, color = tones.danger) }
            if (!readOnly) {
                Button(onClick = {
                    try {
                        val allFields = fields.flatMap { it.fields }
                        val typed = values.mapValues { (key, text) ->
                            when (allFields.firstOrNull { it.path.endsWith(".$key") }?.type) {
                                "boolean" -> text == "true"
                                "number" -> text.toDecimal()
                                "integer" -> text.toIntOrNull()
                                else -> text.ifBlank { null }
                            }
                        }
                        session.service.addInitialInspection(bauakteId, if (notRequired) emptyMap() else checks.toMap(), remarks.toMap(), typed, null,
                            notes, notRequired, schema?.version)
                        model.message("Erstbegehung gespeichert – das Protokoll erstellt der Server.")
                        nav.back()
                    } catch (e: IllegalArgumentException) {
                        error = e.message
                    }
                }, modifier = Modifier.fillMaxWidth().height(52.dp)) { Text("Erstbegehung abschließen") }
            }
            Spacer(Modifier.height(40.dp))
        }
    }
}

/** Abnahme mit Ergebnis, Vorbehalten und Unterschriften von Kunde und Auftragnehmer (wie AcceptanceViews auf iOS). */
@Composable
fun AcceptanceScreen(bauakteId: String, id: String?) {
    val session = LocalSession.current
    val nav = LocalNav.current
    val model = LocalModel.current
    val existing = remember(id) { id?.let { session.service.acceptances(bauakteId).firstOrNull { a -> a.id == it } } }
    val bauakte = remember { session.service.bauakte(bauakteId) }
    var result by remember { mutableStateOf(existing?.result ?: AcceptanceResult.Accepted) }
    var clientName by remember { mutableStateOf(existing?.clientName ?: session.service.contact(bauakte?.customerId)?.name ?: "") }
    var contractorName by remember { mutableStateOf(existing?.contractorName ?: session.user.displayName) }
    var scope by remember { mutableStateOf(existing?.scope ?: bauakte?.description ?: "") }
    var notes by remember { mutableStateOf(existing?.notes ?: "") }
    var clientSignature by remember { mutableStateOf<Bitmap?>(null) }
    var contractorSignature by remember { mutableStateOf<Bitmap?>(null) }
    var error by remember { mutableStateOf<String?>(null) }
    val readOnly = existing != null

    ScreenScaffold(if (readOnly) "Abnahme" else "Neue Abnahme") { padding ->
        Column(Modifier.padding(padding).verticalScroll(rememberScrollState()).padding(16.dp).readable(), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            existing?.let { a ->
                Banner("${a.stateLabel}${a.performedAt?.let { " · ${Dates.dateTime(it)}" } ?: ""}", Icons.Filled.CheckCircle, tones.success, tones.successSoft)
            }
            Text("Ergebnis", style = MaterialTheme.typography.titleSmall)
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                AcceptanceResult.entries.forEach { r ->
                    FilterChip(selected = result == r, enabled = !readOnly, onClick = { result = r }, label = { Text(r.label) })
                }
            }
            OutlinedTextField(scope, { scope = it }, label = { Text("Umfang der Leistung") }, readOnly = readOnly, modifier = Modifier.fillMaxWidth())
            if (result != AcceptanceResult.Accepted || notes.isNotBlank()) {
                if (readOnly) RowItem("Vorbehalte / Gründe", subtitle = notes)
                else DictationField(notes, { notes = it }, if (result == AcceptanceResult.Refused) "Gründe der Verweigerung" else "Vorbehalte", minLines = 3)
            }
            OutlinedTextField(clientName, { clientName = it }, label = { Text("Name Kunde / Bauherr") }, readOnly = readOnly, singleLine = true, modifier = Modifier.fillMaxWidth())
            if (!readOnly) SignaturePad("Unterschrift Kunde") { clientSignature = it }
            OutlinedTextField(contractorName, { contractorName = it }, label = { Text("Name Auftragnehmer") }, readOnly = readOnly, singleLine = true,
                modifier = Modifier.fillMaxWidth())
            if (!readOnly) SignaturePad("Unterschrift Auftragnehmer") { contractorSignature = it }
            if (readOnly && existing?.hasClientSignature == true) Secondary("Vom Kunden unterschrieben. Das PDF liegt nach dem Abgleich unter Dokumente.")
            if (!readOnly && result != AcceptanceResult.Accepted) {
                Banner("Bei Vorbehalt oder Verweigerung bitte die Gründe angeben.", Icons.Filled.Warning, tones.warning, tones.warningSoft)
            }
            error?.let { Text(it, color = tones.danger) }
            if (!readOnly) {
                Button(onClick = {
                    try {
                        session.service.addAcceptance(bauakteId, result, clientName, contractorName, bauakte?.addressLine, scope, notes,
                            clientSignature?.let(Images::pngBase64), contractorSignature?.let(Images::pngBase64))
                        model.message("Abnahme gespeichert – das Protokoll erstellt der Server.")
                        nav.back()
                    } catch (e: IllegalArgumentException) {
                        error = e.message
                    }
                }, modifier = Modifier.fillMaxWidth().height(52.dp)) { Text("Abnahme speichern") }
            }
            Spacer(Modifier.height(40.dp))
        }
    }
}

/** Unterschriftenfeld: Striche mit dem Finger; liefert ein Bild (weiß, schwarze Linien) oder `null` nach „Löschen“. */
@Composable
fun SignaturePad(label: String, onChange: (Bitmap?) -> Unit) {
    val strokes = remember { mutableStateListOf<List<Offset>>() }
    var current by remember { mutableStateOf<List<Offset>>(emptyList()) }
    var size by remember { mutableStateOf(IntSize.Zero) }
    fun render() {
        if (strokes.isEmpty() || size.width == 0) {
            onChange(null)
            return
        }
        val bitmap = Bitmap.createBitmap(size.width, size.height, Bitmap.Config.ARGB_8888)
        val canvas = Canvas(bitmap)
        canvas.drawColor(android.graphics.Color.WHITE)
        val paint = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = android.graphics.Color.BLACK; strokeWidth = 5f; style = Paint.Style.STROKE; strokeCap = Paint.Cap.ROUND }
        strokes.forEach { stroke -> stroke.zipWithNext { a, b -> canvas.drawLine(a.x, a.y, b.x, b.y, paint) } }
        onChange(bitmap)
    }
    Column {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(label, style = MaterialTheme.typography.titleSmall, modifier = Modifier.weight(1f))
            TextButton(onClick = { strokes.clear(); render() }) { Text("Löschen") }
        }
        Box(Modifier.fillMaxWidth().height(160.dp).clip(RoundedCornerShape(14.dp)).background(Color.White).border(1.dp, tones.separator, RoundedCornerShape(14.dp))
            .onSizeChanged { size = it }
            .pointerInput(Unit) {
                detectDragGestures(
                    onDragStart = { current = listOf(it) },
                    onDrag = { change, _ -> current = current + change.position },
                    onDragEnd = { strokes.add(current); current = emptyList(); render() },
                )
            }) {
            Canvas(Modifier.matchParentSize()) {
                (strokes + listOf(current)).forEach { stroke ->
                    if (stroke.size > 1) {
                        val path = Path().apply { moveTo(stroke[0].x, stroke[0].y); stroke.drop(1).forEach { lineTo(it.x, it.y) } }
                        drawPath(path, Color.Black, style = Stroke(width = 5f))
                    }
                }
            }
            if (strokes.isEmpty() && current.isEmpty()) Text("Hier unterschreiben", color = Color.Gray, modifier = Modifier.align(Alignment.Center))
        }
    }
}

@Suppress("unused")
private fun JsonPrimitive.text() = content
