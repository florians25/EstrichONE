package de.estrichone.android.ui

import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Paint
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.PhotoCamera
import androidx.compose.material.icons.filled.PictureAsPdf
import androidx.compose.material.icons.filled.Share
import androidx.compose.material.icons.filled.Warning
import androidx.compose.material.icons.outlined.Draw
import androidx.compose.material.icons.outlined.Email
import androidx.compose.material.icons.outlined.Info
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FilterChipDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.IntSize
import androidx.compose.ui.unit.dp
import de.estrichone.android.support.Images
import de.estrichone.android.support.Links
import de.estrichone.core.Acceptance
import de.estrichone.core.AcceptanceResult
import de.estrichone.core.ApiException
import de.estrichone.core.CheckResult
import de.estrichone.core.Dates
import de.estrichone.core.Drafts
import de.estrichone.core.ProtocolCheck
import de.estrichone.core.SyncState
import de.estrichone.core.WorkItemKind
import de.estrichone.core.acceptanceCustomerLink
import de.estrichone.core.display
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import kotlinx.serialization.json.JsonPrimitive
import java.time.LocalDate

/** Fotos von heute zu dieser Bauakte anhängen und weitere aufnehmen (Erstbegehung, Abnahme). */
@Composable
private fun ProtocolPhotos(count: Int, attach: Boolean, onAttach: (Boolean) -> Unit, onCamera: () -> Unit) {
    SectionCard(padding = PaddingValues(14.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Column(Modifier.weight(1f)) {
                Text("Fotos von heute anhängen")
                Secondary(when (count) { 0 -> "Heute noch keine Fotos zu dieser Baustelle"; 1 -> "1 Foto dieser Baustelle"; else -> "$count Fotos dieser Baustelle" })
            }
            Switch(attach && count > 0, onAttach, enabled = count > 0)
        }
        OutlinedButton(onClick = onCamera, modifier = Modifier.fillMaxWidth().padding(top = 8.dp).heightIn(min = 48.dp)) {
            Icon(Icons.Filled.PhotoCamera, null, Modifier.size(18.dp))
            Text("Foto aufnehmen", Modifier.padding(start = 8.dp))
        }
    }
}

/**
 * Erstbegehung nach dem Formular-Schema „erstbegehung“ als geführter Ablauf: Prüfpunkte bewerten (bei Mangel mit Bemerkung),
 * Messwerte, Fotos, dann die Zusammenfassung mit dem, was noch fehlt. Eingaben werden laufend als Entwurf gesichert.
 * Vorhandene Protokolle nur ansehen, mit PDF vom Server.
 */
@Composable
fun InspectionScreen(bauakteId: String, id: String?) {
    val session = LocalSession.current
    val nav = LocalNav.current
    val model = LocalModel.current
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val schema = remember { session.service.schema("erstbegehung") }
    val lists = remember { session.service.valueLists() }
    val existing = remember(id) { id?.let { session.service.inspections(bauakteId).firstOrNull { i -> i.id == it } } }
    val readOnly = existing != null
    var generation by remember { mutableIntStateOf(0) }
    val draft = if (readOnly) null else rememberDraft(Drafts.INSPECTION, bauakteId, generation)
    // Zurück aus der Kamera: derselbe Entwurf, ohne Hinweis.
    val resumed = remember { (nav.resumedDraft == Drafts.INSPECTION).also { if (it) nav.resumedDraft = null } }
    fun part(prefix: String) = draft.orEmpty().filterKeys { it.startsWith(prefix) }.mapKeys { it.key.removePrefix(prefix) }
    val checks = remember(generation) {
        mutableStateMapOf<String, CheckResult>().apply {
            existing?.checks?.let { putAll(it) }
            part("c:").forEach { (key, raw) -> CheckResult.entries.firstOrNull { it.raw == raw }?.let { put(key, it) } }
        }
    }
    val remarks = remember(generation) { mutableStateMapOf<String, String>().apply { existing?.checkRemarks?.let { putAll(it) }; putAll(part("r:")) } }
    val values = remember(generation) { mutableStateMapOf<String, String>().apply { putAll(part("v:")) } }
    var notRequired by remember(generation) { mutableStateOf(existing?.notRequired ?: (draft?.get("notRequired") == "true")) }
    var notes by remember(generation) { mutableStateOf(existing?.notes ?: draft?.get("notes") ?: "") }
    var attachPhotos by remember(generation) { mutableStateOf(draft?.get("photos") != "false") }
    var error by remember { mutableStateOf<String?>(null) }
    var loading by remember { mutableStateOf(false) }
    val fields = schema?.groups.orEmpty()
    val checkFields = remember(fields) { fields.flatMap { it.fields }.filter { it.type == "check" }.map { it.path.substringAfterLast('.') to it.label } }
    val today = remember { LocalDate.now(Dates.zone) }
    val todaysPhotos by session.local(bauakteId) { session.service.photos(bauakteId).filter { it.isPhoto && it.time?.atZone(Dates.zone)?.toLocalDate() == today } }
    val missing = if (notRequired) emptyList() else ProtocolCheck.inspection(checkFields, checks, remarks)

    fun draftFields(): Map<String, String> {
        if (checks.isEmpty() && values.isEmpty() && notes.isBlank() && !notRequired) return emptyMap()
        return checks.map { "c:${it.key}" to it.value.raw }.toMap() + remarks.mapKeys { "r:${it.key}" } + values.mapKeys { "v:${it.key}" } +
            mapOf("notes" to notes, "notRequired" to notRequired.toString(), "photos" to attachPhotos.toString())
    }
    if (!readOnly) {
        KeepScreenOn()
        DraftAutosave(Drafts.INSPECTION, bauakteId, draftFields())
    }

    ScreenScaffold(if (readOnly) "Erstbegehung" else "Neue Erstbegehung") { padding ->
        Column(Modifier.padding(padding).verticalScroll(rememberScrollState()).padding(16.dp).readable(), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            existing?.performedAt?.let { Secondary("Durchgeführt ${Dates.dateTime(it)}") }
            if (draft != null && !resumed) DraftNotice { session.drafts.clear(Drafts.INSPECTION, bauakteId); generation++ }
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
                                        // 48 dp hoch: auch mit Handschuhen sicher zu treffen.
                                        FilterChip(selected = checks[key] == result, enabled = !readOnly, onClick = { checks[key] = result },
                                            label = { Text(result.label) }, modifier = Modifier.heightIn(min = 48.dp), colors = FilterChipDefaults.filterChipColors(
                                                selectedContainerColor = color.copy(alpha = 0.16f), selectedLabelColor = color,
                                                disabledSelectedContainerColor = color.copy(alpha = 0.16f)))
                                    }
                                }
                                if (checks[key] == CheckResult.NotOk || remarks[key] != null) {
                                    OutlinedTextField(remarks[key] ?: "", { remarks[key] = it }, label = { Text("Bemerkung") }, readOnly = readOnly,
                                        isError = !readOnly && checks[key] == CheckResult.NotOk && remarks[key].isNullOrBlank(),
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
            if (!readOnly && !notRequired) {
                Text("Fotos", style = SectionTitle, modifier = Modifier.padding(top = 12.dp))
                ProtocolPhotos(todaysPhotos.size, attachPhotos, { attachPhotos = it }) {
                    // Entwurf sofort sichern: Die Kamera ersetzt diese Seite, danach geht es hier nahtlos weiter.
                    session.drafts.save(Drafts.INSPECTION, bauakteId, draftFields())
                    nav.resumedDraft = Drafts.INSPECTION
                    nav.push(Route.Camera(bauakteId))
                }
                Text("Zusammenfassung", style = SectionTitle, modifier = Modifier.padding(top = 12.dp))
                SectionCard {
                    SummaryRow("Prüfpunkte", if (checks.isEmpty()) "Noch keine bewertet" else "${checks.size} von ${checkFields.size} bewertet: ${ProtocolCheck.inspectionSummary(checks)}")
                    RowDivider(16.dp)
                    SummaryRow("Fotos", if (attachPhotos && todaysPhotos.isNotEmpty()) "${todaysPhotos.size} von heute werden angehängt" else "Keine")
                }
                MissingList(missing)
            }
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
                            notes, notRequired, schema?.version, photoFileIds = if (attachPhotos && !notRequired) todaysPhotos.map { it.id } else emptyList())
                        session.drafts.clear(Drafts.INSPECTION, bauakteId)
                        model.message("Erstbegehung gespeichert. Das Protokoll wird auf dem Server erstellt.")
                        nav.back()
                    } catch (e: IllegalArgumentException) {
                        error = e.message
                    }
                }, enabled = missing.isEmpty(), modifier = Modifier.fillMaxWidth().height(56.dp)) { Text("Erstbegehung abschließen", style = MaterialTheme.typography.titleMedium) }
            } else if (existing != null && !existing.notRequired) {
                // Protokoll als PDF vom Server (nur mit Verbindung): ansehen oder teilen.
                fun pdf(share: Boolean) = scope.launch {
                    loading = true
                    val bytes = runCatching { withContext(Dispatchers.IO) { session.api.bytes("bauakten/$bauakteId/initial-inspection.pdf", mapOf("inspectionId" to existing.id)) } }.getOrNull()
                    loading = false
                    val name = "Erstbegehungsprotokoll.pdf"
                    when {
                        bytes == null -> model.message(if (existing.syncState == SyncState.Synced) "Das Protokoll ist nur mit Verbindung zum Server verfügbar."
                            else "Das Protokoll wird nach der Übertragung auf dem Server erstellt.")
                        share -> { Links.share(context, bytes, name, "application/pdf") }
                        !Links.open(context, bytes, name, "application/pdf") -> model.message("Keine App zum Anzeigen von PDF gefunden.")
                        else -> Unit
                    }
                }
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    Button(onClick = { pdf(false) }, enabled = !loading, modifier = Modifier.weight(1f).height(56.dp)) {
                        Icon(Icons.Filled.PictureAsPdf, null, Modifier.size(18.dp))
                        Text("Protokoll ansehen", Modifier.padding(start = 8.dp))
                    }
                    OutlinedButton(onClick = { pdf(true) }, enabled = !loading, modifier = Modifier.weight(1f).height(56.dp)) {
                        Icon(Icons.Filled.Share, null, Modifier.size(18.dp))
                        Text("Teilen", Modifier.padding(start = 8.dp))
                    }
                }
            }
            Spacer(Modifier.height(40.dp))
        }
    }
}

/** Abnahme: neue als geführter Ablauf in drei Schritten, vorhandene als Protokoll mit PDF, Teilen und E-Mail an den Kunden. */
@Composable
fun AcceptanceScreen(bauakteId: String, id: String?) {
    if (id == null) AcceptanceFlow(bauakteId) else AcceptanceProtocol(bauakteId, id)
}

private val acceptanceSteps = listOf("Leistung und Ergebnis", "Unterschriften", "Zusammenfassung")

/**
 * Geführte Abnahme: 1. Leistung, Ergebnis, Vorbehalte und Fotos, 2. Unterschriften (der Kunde unterschreibt in der
 * Kundenansicht ohne interne Angaben), 3. Zusammenfassung mit dem, was noch fehlt, dann Abschluss. Texte werden laufend als
 * Entwurf gesichert; Unterschriften bleiben bis zum Abschluss im Speicher der Sitzung.
 */
@Composable
private fun AcceptanceFlow(bauakteId: String) {
    val session = LocalSession.current
    val nav = LocalNav.current
    val model = LocalModel.current
    val bauakte = remember { session.service.bauakte(bauakteId) }
    var generation by remember { mutableIntStateOf(0) }
    val draft = rememberDraft(Drafts.ACCEPTANCE, bauakteId, generation)
    val resumed = remember { (nav.resumedDraft == Drafts.ACCEPTANCE).also { if (it) nav.resumedDraft = null } }
    var step by remember(generation) { mutableIntStateOf(FieldPreset.acceptanceStep ?: draft?.get("step")?.toIntOrNull()?.coerceIn(0, 2) ?: 0) }
    var result by remember(generation) { mutableStateOf(AcceptanceResult.entries.firstOrNull { it.raw == draft?.get("result") } ?: AcceptanceResult.Accepted) }
    var clientName by remember(generation) { mutableStateOf(draft?.get("clientName") ?: session.service.contact(bauakte?.customerId)?.name ?: "") }
    var contractorName by remember(generation) { mutableStateOf(draft?.get("contractorName") ?: session.user.displayName) }
    var scope by remember(generation) { mutableStateOf(draft?.get("scope") ?: bauakte?.description ?: "") }
    var notes by remember(generation) { mutableStateOf(draft?.get("notes") ?: "") }
    var attachPhotos by remember(generation) { mutableStateOf(draft?.get("photos") != "false") }
    // Unterschriften überstehen den Abstecher in die Kamera (Speicher der Sitzung), nicht aber einen Neustart der App.
    val memoKey = "acceptance-signatures:$bauakteId"
    @Suppress("UNCHECKED_CAST")
    val kept = remember(generation) { session.memo[memoKey] as? Pair<Bitmap?, Bitmap?> }
    var clientSignature by remember(generation) { mutableStateOf(kept?.first) }
    var contractorSignature by remember(generation) { mutableStateOf(kept?.second) }
    fun keepSignatures() {
        if (clientSignature == null && contractorSignature == null) session.memo.remove(memoKey) else session.memo[memoKey] = clientSignature to contractorSignature
    }
    var error by remember { mutableStateOf<String?>(null) }
    val today = remember { LocalDate.now(Dates.zone) }
    val todaysPhotos by session.local(bauakteId) { session.service.photos(bauakteId).filter { it.isPhoto && it.time?.atZone(Dates.zone)?.toLocalDate() == today } }
    val openDefects by session.local(bauakteId) { session.service.tasks(bauakteId).count { it.isOpen && it.kind == WorkItemKind.Defect } }
    val missing = ProtocolCheck.acceptance(result, clientName, contractorName, notes, clientSignature != null, contractorSignature != null)

    fun draftFields(): Map<String, String> {
        // Vorbelegungen allein (Kunde, Leistung) sind kein Entwurf: erst sichern, wenn daran gearbeitet wurde.
        if (step == 0 && result == AcceptanceResult.Accepted && notes.isBlank() && scope == (bauakte?.description ?: "")) return emptyMap()
        return mapOf("step" to step.toString(), "result" to result.raw, "clientName" to clientName, "contractorName" to contractorName, "scope" to scope,
            "notes" to notes, "photos" to attachPhotos.toString())
    }
    KeepScreenOn()
    DraftAutosave(Drafts.ACCEPTANCE, bauakteId, draftFields())

    val lines = listOfNotNull(
        bauakte?.let { "Bauvorhaben" to listOfNotNull(it.title, it.addressLine).joinToString("\n") },
        "Datum" to Dates.long(today),
        "Ergebnis" to result.label,
        scope.trim().takeIf { it.isNotEmpty() }?.let { "Umfang der Leistung" to it },
        notes.trim().takeIf { it.isNotEmpty() }?.let { (if (result == AcceptanceResult.Refused) "Gründe der Verweigerung" else "Vorbehalte") to it },
    )

    ScreenScaffold("Neue Abnahme", subtitle = "Schritt ${step + 1} von 3: ${acceptanceSteps[step]}") { padding ->
        Column(Modifier.padding(padding).verticalScroll(rememberScrollState()).padding(16.dp).readable(), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            LinearProgressIndicator(progress = { (step + 1) / 3f }, modifier = Modifier.fillMaxWidth())
            if (draft != null && !resumed) {
                DraftNotice {
                    session.drafts.clear(Drafts.ACCEPTANCE, bauakteId)
                    session.memo.remove(memoKey)
                    generation++
                }
            }
            when (step) {
                0 -> {
                    Text("Ergebnis", style = MaterialTheme.typography.titleSmall)
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        AcceptanceResult.entries.forEach { r ->
                            FilterChip(selected = result == r, onClick = { result = r }, label = { Text(r.label) }, modifier = Modifier.heightIn(min = 48.dp))
                        }
                    }
                    if (openDefects > 0) {
                        Banner(if (openDefects == 1) "Zu dieser Bauakte ist 1 Mangel offen. Üblich ist dann die Abnahme mit Vorbehalt."
                            else "Zu dieser Bauakte sind $openDefects Mängel offen. Üblich ist dann die Abnahme mit Vorbehalt.", Icons.Outlined.Info, tones.info, tones.infoSoft)
                    }
                    DictationField(scope, { scope = it }, "Umfang der Leistung", minLines = 2)
                    if (result != AcceptanceResult.Accepted || notes.isNotBlank()) {
                        DictationField(notes, { notes = it }, if (result == AcceptanceResult.Refused) "Gründe der Verweigerung" else "Vorbehalte", minLines = 3)
                    }
                    ProtocolPhotos(todaysPhotos.size, attachPhotos, { attachPhotos = it }) {
                        session.drafts.save(Drafts.ACCEPTANCE, bauakteId, draftFields().ifEmpty { mapOf("step" to "0", "result" to result.raw, "scope" to scope) })
                        keepSignatures()
                        nav.resumedDraft = Drafts.ACCEPTANCE
                        nav.push(Route.Camera(bauakteId))
                    }
                }
                1 -> {
                    Text("Kunde", style = MaterialTheme.typography.titleSmall)
                    OutlinedTextField(clientName, { clientName = it }, label = { Text("Name des Kunden bzw. Unterzeichners") }, singleLine = true, modifier = Modifier.fillMaxWidth())
                    val signed = clientSignature
                    if (signed != null) {
                        SignaturePreview(signed) { clientSignature = null; keepSignatures() }
                    } else {
                        Button(onClick = {
                            nav.customerView = CustomerView.Signature("Abnahmeprotokoll", lines, clientName) { name, bitmap ->
                                clientName = name
                                clientSignature = bitmap
                                keepSignatures()
                            }
                        }, modifier = Modifier.fillMaxWidth().height(56.dp)) {
                            Icon(Icons.Outlined.Draw, null, Modifier.size(20.dp))
                            Text("Kunde unterschreibt", Modifier.padding(start = 8.dp), style = MaterialTheme.typography.titleMedium)
                        }
                        Secondary(if (result == AcceptanceResult.Refused) "Bei verweigerter Abnahme ist die Unterschrift des Kunden nicht nötig."
                            else "Öffnet die Kundenansicht: Der Kunde sieht nur das Protokoll und unterschreibt mit dem Finger.")
                    }
                    Text("Auftragnehmer", style = MaterialTheme.typography.titleSmall, modifier = Modifier.padding(top = 8.dp))
                    OutlinedTextField(contractorName, { contractorName = it }, label = { Text("Name Auftragnehmer") }, singleLine = true, modifier = Modifier.fillMaxWidth())
                    val own = contractorSignature
                    if (own != null) SignaturePreview(own) { contractorSignature = null; keepSignatures() }
                    else SignaturePad("Unterschrift Auftragnehmer") { contractorSignature = it; keepSignatures() }
                }
                else -> {
                    SectionCard {
                        lines.forEachIndexed { index, (label, value) ->
                            if (index > 0) RowDivider(16.dp)
                            SummaryRow(label, value)
                        }
                        RowDivider(16.dp)
                        SummaryRow("Kunde", listOfNotNull(clientName.trim().ifEmpty { null }, if (clientSignature != null) "unterschrieben" else "ohne Unterschrift").joinToString(", "))
                        RowDivider(16.dp)
                        SummaryRow("Auftragnehmer", listOfNotNull(contractorName.trim().ifEmpty { null }, if (contractorSignature != null) "unterschrieben" else "ohne Unterschrift").joinToString(", "))
                        RowDivider(16.dp)
                        SummaryRow("Fotos", if (attachPhotos && todaysPhotos.isNotEmpty()) "${todaysPhotos.size} von heute werden angehängt" else "Keine")
                    }
                    MissingList(missing)
                    Secondary("Nach dem Abschluss erstellt der Server das Protokoll als PDF. Sie können es danach ansehen, teilen und dem Kunden per E-Mail senden.")
                }
            }
            error?.let { Text(it, color = tones.danger) }
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                if (step > 0) OutlinedButton(onClick = { step-- }, modifier = Modifier.weight(1f).height(56.dp)) { Text("Zurück") }
                if (step < 2) {
                    Button(onClick = { step++ }, enabled = step != 0 || result == AcceptanceResult.Accepted || notes.isNotBlank(),
                        modifier = Modifier.weight(1f).height(56.dp)) { Text("Weiter", style = MaterialTheme.typography.titleMedium) }
                } else {
                    Button(onClick = {
                        try {
                            val created = session.service.addAcceptance(bauakteId, result, clientName, contractorName, bauakte?.addressLine, scope, notes,
                                clientSignature?.let(Images::pngBase64), contractorSignature?.let(Images::pngBase64),
                                photoFileIds = if (attachPhotos) todaysPhotos.map { it.id } else emptyList())
                            session.drafts.clear(Drafts.ACCEPTANCE, bauakteId)
                            session.memo.remove(memoKey)
                            model.message("Abnahme gespeichert. Das Protokoll wird auf dem Server erstellt.")
                            // Weiter zum Protokoll: dort stehen PDF, Teilen und E-Mail an den Kunden, sobald es übertragen ist.
                            nav.replace(Route.Acceptance(bauakteId, created))
                        } catch (e: IllegalArgumentException) {
                            error = e.message
                        }
                    }, enabled = missing.isEmpty(), modifier = Modifier.weight(1f).height(56.dp)) { Text("Abnahme abschließen", style = MaterialTheme.typography.titleMedium) }
                }
            }
            Spacer(Modifier.height(40.dp))
        }
    }
}

/** Geleistete Unterschrift als Bild mit „Neu unterschreiben“. */
@Composable
private fun SignaturePreview(bitmap: Bitmap, onClear: () -> Unit) {
    Column {
        Box(Modifier.fillMaxWidth().height(120.dp).clip(RoundedCornerShape(14.dp)).background(Color.White).border(1.dp, tones.separator, RoundedCornerShape(14.dp))) {
            Image(bitmap.asImageBitmap(), "Unterschrift", contentScale = ContentScale.Fit, modifier = Modifier.fillMaxSize().padding(6.dp))
        }
        Row(verticalAlignment = Alignment.CenterVertically) {
            Icon(Icons.Filled.CheckCircle, null, tint = tones.success, modifier = Modifier.size(18.dp))
            Text("Unterschrieben", Modifier.weight(1f).padding(start = 6.dp), style = MaterialTheme.typography.bodyMedium)
            TextButton(onClick = onClear, modifier = Modifier.heightIn(min = 48.dp)) { Text("Neu unterschreiben") }
        }
    }
}

/**
 * Erfasstes Abnahmeprotokoll: Angaben, Stand und das PDF vom Server zum Ansehen, Teilen und Senden per E-Mail. Fehlt die
 * Unterschrift des Kunden, lässt sich ihm ein Freigabelink schicken (der Server versendet die E-Mail).
 */
@Composable
private fun AcceptanceProtocol(bauakteId: String, id: String) {
    val session = LocalSession.current
    val model = LocalModel.current
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val status by session.status.collectAsState()
    val acceptance by session.local(bauakteId, id) { session.service.acceptances(bauakteId).firstOrNull { it.id == id } }
    val a = acceptance
    if (a == null) {
        ScreenScaffold("Abnahme") { EmptyState(Icons.Filled.Warning, "Abnahme nicht gefunden", null, Modifier.padding(it)) }
        return
    }
    val bauakte = remember { session.service.bauakte(bauakteId) }
    val customer = remember { session.service.contact(bauakte?.customerId) }
    val pdf by session.local(a.pdfFileId) { a.pdfFileId?.let { session.service.file(it) } }
    var busy by remember { mutableStateOf(false) }
    var linking by remember { mutableStateOf(false) }
    val title = "Abnahmeprotokoll ${bauakte?.numberText ?: ""}".trim()

    fun withPdf(action: (ByteArray, String) -> Boolean, failure: String) = scope.launch {
        val file = pdf ?: return@launch
        busy = true
        val bytes = session.content(file)
        busy = false
        when {
            bytes == null -> model.message("Das Protokoll ist gerade nicht verfügbar. Mit Verbindung erneut versuchen.")
            !action(bytes, file.originalName.ifBlank { "$title.pdf" }) -> model.message(failure)
            else -> Unit
        }
    }

    ScreenScaffold("Abnahme") { padding ->
        Column(Modifier.padding(padding).verticalScroll(rememberScrollState()).padding(16.dp).readable(), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            val done = a.state == "completed"
            Banner("${a.stateLabel}${a.performedAt?.let { ", ${Dates.dateTime(it)}" } ?: ""}", if (done) Icons.Filled.CheckCircle else Icons.Outlined.Info,
                if (done) tones.success else tones.info, if (done) tones.successSoft else tones.infoSoft)
            SectionCard {
                SummaryRow("Ergebnis", a.result?.label ?: "Offen")
                a.scope?.let { RowDivider(16.dp); SummaryRow("Umfang der Leistung", it) }
                a.notes?.let { RowDivider(16.dp); SummaryRow("Vorbehalte bzw. Gründe", it) }
                RowDivider(16.dp)
                SummaryRow("Kunde", listOfNotNull(a.clientName, if (a.hasClientSignature) "unterschrieben" else "ohne Unterschrift").joinToString(", "))
                a.contractorName?.let { RowDivider(16.dp); SummaryRow("Auftragnehmer", it) }
            }
            Text("Protokoll", style = SectionTitle, modifier = Modifier.padding(top = 4.dp))
            if (pdf != null) {
                Button(onClick = { withPdf({ bytes, name -> Links.open(context, bytes, name, "application/pdf") }, "Keine App zum Anzeigen von PDF gefunden.") },
                    enabled = !busy, modifier = Modifier.fillMaxWidth().height(56.dp)) {
                    Icon(Icons.Filled.PictureAsPdf, null, Modifier.size(20.dp))
                    Text("Protokoll ansehen", Modifier.padding(start = 8.dp), style = MaterialTheme.typography.titleMedium)
                }
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    OutlinedButton(onClick = { withPdf({ bytes, name -> Links.share(context, bytes, name, "application/pdf") }, "Teilen ist auf diesem Gerät nicht möglich.") },
                        enabled = !busy, modifier = Modifier.weight(1f).height(56.dp)) {
                        Icon(Icons.Filled.Share, null, Modifier.size(18.dp))
                        Text("Teilen", Modifier.padding(start = 8.dp))
                    }
                    OutlinedButton(onClick = {
                        withPdf({ bytes, name ->
                            Links.mailFile(context, customer?.emails?.firstOrNull()?.address, title,
                                "Guten Tag,\n\nanbei erhalten Sie das Abnahmeprotokoll${bauakte?.let { " zum Bauvorhaben ${it.title}" } ?: ""}.\n\nMit freundlichen Grüßen\n${session.user.displayName}",
                                bytes, name, "application/pdf")
                        }, "Keine E-Mail-App gefunden.")
                    }, enabled = !busy, modifier = Modifier.weight(1f).height(56.dp)) {
                        Icon(Icons.Outlined.Email, null, Modifier.size(18.dp))
                        Text("An Kunden", Modifier.padding(start = 8.dp))
                    }
                }
            } else {
                val waiting = a.syncState != SyncState.Synced
                Secondary(when {
                    waiting && status.offline -> "Die Abnahme liegt auf dem Gerät. Das PDF erstellt der Server, sobald sie übertragen ist."
                    waiting -> "Die Abnahme wird übertragen. Danach erstellt der Server das PDF, es erscheint dann hier."
                    !a.hasClientSignature && !done -> "Die Unterschrift des Kunden steht noch aus. Das PDF entsteht, sobald der Kunde freigegeben hat."
                    else -> "Das PDF wird erstellt und erscheint nach dem nächsten Abgleich hier."
                })
                OutlinedButton(onClick = { scope.launch { session.synchronize() } }, enabled = !status.running, modifier = Modifier.fillMaxWidth().height(56.dp)) {
                    Text(if (status.running) "Wird abgeglichen" else "Jetzt abgleichen")
                }
                if (!waiting && !a.hasClientSignature && !done) {
                    Button(onClick = { linking = true }, modifier = Modifier.fillMaxWidth().height(56.dp)) {
                        Icon(Icons.Outlined.Email, null, Modifier.size(18.dp))
                        Text("Freigabelink an den Kunden senden", Modifier.padding(start = 8.dp))
                    }
                }
            }
            Spacer(Modifier.height(40.dp))
        }
    }
    if (linking) CustomerLinkDialog(a, customer?.emails?.firstOrNull()?.address ?: "") { linking = false }
}

/** Freigabelink der Abnahme: an die E-Mail-Adresse des Kunden senden (Server) oder ohne Adresse als Link teilen. */
@Composable
private fun CustomerLinkDialog(acceptance: Acceptance, initialEmail: String, onDismiss: () -> Unit) {
    val session = LocalSession.current
    val model = LocalModel.current
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    var email by remember { mutableStateOf(initialEmail) }
    var busy by remember { mutableStateOf(false) }
    var error by remember { mutableStateOf<String?>(null) }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Freigabe durch den Kunden") },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Text("Der Kunde erhält einen befristeten Link, sieht dort nur dieses Protokoll und gibt es frei. Ohne Adresse erhalten Sie den Link zum Teilen.")
                OutlinedTextField(email, { email = it }, label = { Text("E-Mail des Kunden") }, singleLine = true, modifier = Modifier.fillMaxWidth(),
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Email))
                error?.let { Text(it, color = tones.danger) }
            }
        },
        confirmButton = {
            TextButton(onClick = {
                scope.launch {
                    busy = true
                    error = null
                    try {
                        val link = withContext(Dispatchers.IO) { session.api.acceptanceCustomerLink(acceptance.id, email) }
                        if (link.emailed) model.message("Der Link wurde an ${email.trim()} gesendet.") else Links.shareText(context, link.url, "Abnahme freigeben")
                        onDismiss()
                    } catch (e: ApiException) {
                        error = if (e.isOffline) "Dafür ist eine Verbindung zum Server nötig." else e.message ?: "Der Link konnte nicht erstellt werden."
                    } finally {
                        busy = false
                    }
                }
            }, enabled = !busy) { Text(if (email.isBlank()) "Link erstellen" else "Senden") }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Abbrechen") } },
    )
}

/** Unterschriftenfeld: Striche mit dem Finger; liefert ein Bild (weiß, schwarze Linien) oder `null` nach „Löschen“. */
@Composable
fun SignaturePad(label: String, height: Dp = 160.dp, onChange: (Bitmap?) -> Unit) {
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
            TextButton(onClick = { strokes.clear(); render() }, modifier = Modifier.heightIn(min = 48.dp)) { Text("Löschen") }
        }
        Box(Modifier.fillMaxWidth().height(height).clip(RoundedCornerShape(14.dp)).background(Color.White).border(1.dp, tones.separator, RoundedCornerShape(14.dp))
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
