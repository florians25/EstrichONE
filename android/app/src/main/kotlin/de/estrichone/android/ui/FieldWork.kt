package de.estrichone.android.ui

import android.graphics.Bitmap
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.navigationBars
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBars
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.pager.HorizontalPager
import androidx.compose.foundation.pager.rememberPagerState
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.MenuBook
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.CloudUpload
import androidx.compose.material.icons.filled.Directions
import androidx.compose.material.icons.filled.ErrorOutline
import androidx.compose.material.icons.filled.PhotoCamera
import androidx.compose.material.icons.filled.Restore
import androidx.compose.material.icons.filled.Warning
import androidx.compose.material.icons.outlined.Folder
import androidx.compose.material.icons.outlined.OfflinePin
import androidx.compose.material.icons.outlined.Place
import androidx.compose.material.icons.outlined.ReportProblem
import androidx.compose.material3.Button
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import de.estrichone.android.Session
import de.estrichone.android.support.Links
import de.estrichone.core.AppModules
import de.estrichone.core.Dates
import de.estrichone.core.PlannedJob
import de.estrichone.core.SiteVisit
import de.estrichone.core.TransferStatus
import kotlinx.coroutines.delay

// Arbeiten auf der Baustelle und beim Kunden: Bildschirm bleibt an, Entwürfe, Übertragungsstand, „Auf dem Gerät verfügbar“,
// Ankunft und Abschluss, Menü der Einsatzzeilen und die Kundenansicht (Fotos, Unterschrift) ohne interne Angaben.

/** Bildschirm bleibt an, solange diese Ansicht zu sehen ist (Erfassung, Kamera, Kundenansicht). */
@Composable
fun KeepScreenOn() {
    val view = LocalView.current
    DisposableEffect(view) {
        view.keepScreenOn = true
        onDispose { view.keepScreenOn = false }
    }
}

/** Entwurf eines Formulars beim Öffnen; nach „Verwerfen“ ([generation] > 0) leer. */
@Composable
fun rememberDraft(form: String, bauakteId: String?, generation: Int): Map<String, String>? {
    val session = LocalSession.current
    return remember(form, bauakteId, generation) { if (generation == 0) session.drafts.load(form, bauakteId) else null }
}

/** Eingaben kurz nach jeder Änderung sichern: Abbruch, Absturz oder ein Anruf dazwischen kosten keinen Text. */
@Composable
fun DraftAutosave(form: String, bauakteId: String?, fields: Map<String, String>) {
    val session = LocalSession.current
    LaunchedEffect(fields) {
        delay(350)
        session.drafts.save(form, bauakteId, fields)
    }
}

@Composable
fun DraftNotice(onDiscard: () -> Unit) {
    Row(Modifier.fillMaxWidth().clip(RoundedCornerShape(12.dp)).background(tones.infoSoft).padding(start = 12.dp), verticalAlignment = Alignment.CenterVertically) {
        Icon(Icons.Filled.Restore, null, tint = tones.info, modifier = Modifier.size(20.dp))
        Text("Entwurf wiederhergestellt", Modifier.weight(1f).padding(start = 10.dp), style = MaterialTheme.typography.bodyMedium)
        TextButton(onClick = onDiscard, modifier = Modifier.heightIn(min = 48.dp)) { Text("Verwerfen") }
    }
}

/** Was vor dem Abschluss noch fehlt (Pflichtangaben). */
@Composable
fun MissingList(missing: List<String>, modifier: Modifier = Modifier) {
    if (missing.isEmpty()) return
    Column(modifier.fillMaxWidth().clip(RoundedCornerShape(12.dp)).background(tones.warningSoft).padding(12.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Icon(Icons.Filled.Warning, null, tint = tones.warning, modifier = Modifier.size(18.dp))
            Text("Vor dem Abschluss fehlt noch", Modifier.padding(start = 8.dp), style = MaterialTheme.typography.titleSmall)
        }
        missing.forEach { Text(it, style = MaterialTheme.typography.bodyMedium) }
    }
}

/**
 * Übertragungsstand als eine ruhige Zeile: „3 Fotos warten auf Übertragung“ bzw. rot, wenn etwas gescheitert ist.
 * Antippen öffnet die Synchronisation. Ist alles übertragen, erscheint nichts. Mit [bauakteId] nur die Dateien dieser Bauakte.
 */
@Composable
fun TransferLine(bauakteId: String? = null, modifier: Modifier = Modifier) {
    val session = LocalSession.current
    val nav = LocalNav.current
    val status by session.status.collectAsState()
    val transfer by session.local(bauakteId) { TransferStatus.of(session.store).let { all -> if (bauakteId != null) all.of(bauakteId) else all } }
    val failed = transfer.failedText
    val waiting = transfer.waitingText
    if (failed == null && waiting == null) return
    Row(
        modifier.fillMaxWidth().clip(RoundedCornerShape(12.dp)).background(if (failed != null) tones.dangerSoft else tones.card)
            .clickable(role = Role.Button, onClickLabel = "Synchronisation öffnen") { nav.push(Route.Sync) }
            .heightIn(min = 56.dp).padding(horizontal = 16.dp, vertical = 8.dp).testTag("transferLine"),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Icon(if (failed != null) Icons.Filled.ErrorOutline else Icons.Filled.CloudUpload, null,
            tint = if (failed != null) tones.danger else tones.textSecondary, modifier = Modifier.size(20.dp))
        Spacer(Modifier.width(16.dp))
        Column(Modifier.weight(1f)) {
            Text("${failed ?: waiting}.", style = MaterialTheme.typography.titleSmall)
            Text(
                when {
                    failed != null && waiting != null -> "$waiting. Antippen und erneut senden."
                    failed != null -> "Antippen und erneut senden."
                    status.running -> "Wird gerade übertragen."
                    status.offline -> "Bleibt auf dem Gerät und wird gesendet, sobald eine Verbindung besteht."
                    else -> "Wird automatisch im Hintergrund gesendet."
                },
                style = MaterialTheme.typography.bodySmall, color = tones.textSecondary,
            )
        }
    }
}

/** „Auf dem Gerät verfügbar“ im Kopf der Bauakte: Daten liegen vollständig vor, Dokumente soweit geladen. */
@Composable
fun OfflineAvailability(bauakteId: String, modifier: Modifier = Modifier) {
    val session = LocalSession.current
    val loaded by session.offlineFiles.collectAsState()
    val state by session.local(bauakteId, loaded) { session.offlineState(bauakteId, loaded) }
    Row(modifier, verticalAlignment = Alignment.CenterVertically) {
        Icon(Icons.Outlined.OfflinePin, null, tint = if (state.isComplete) tones.success else tones.textSecondary, modifier = Modifier.size(16.dp))
        Text(state.text, Modifier.padding(start = 6.dp), style = MaterialTheme.typography.bodySmall, color = tones.textSecondary)
    }
}

/**
 * Ankunft und Abschluss am Einsatz: „Angekommen“ merkt die Uhrzeit, „Fertig“ ebenso und schlägt den Eintrag im Bautagebuch
 * mit Beginn und Ende vor. Die Uhrzeiten bleiben auf dem Gerät (der Server hat dafür kein eigenes Feld).
 */
@Composable
fun VisitActions(job: PlannedJob, visit: SiteVisit, onChange: (SiteVisit) -> Unit, modifier: Modifier = Modifier) {
    val session = LocalSession.current
    val nav = LocalNav.current
    val model = LocalModel.current
    val modules by session.modules.collectAsState()
    val diaryOn = modules.isOn(AppModules.DIARY)
    fun diary(current: SiteVisit) {
        nav.captureIn(job.bauakteId, SiteTask.Diary, mapOf(
            "start" to (current.arrived ?: ""), "end" to (current.finished ?: ""), "text" to current.diaryText(job.activity)))
    }
    Row(modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        when {
            visit.arrived == null -> {
                OutlinedButton(onClick = {
                    val next = visit.copy(arrived = SiteVisit.clock(Live.clock()))
                    onChange(next)
                    model.message("Ankunft ${next.arrived} Uhr gemerkt.")
                }, modifier = Modifier.weight(1f).heightIn(min = 48.dp)) {
                    Icon(Icons.Outlined.Place, null, Modifier.size(18.dp))
                    Text("Angekommen", Modifier.padding(start = 8.dp))
                }
            }
            visit.finished == null -> {
                Text(visit.text ?: "", Modifier.weight(1f), style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.Medium)
                Button(onClick = {
                    val next = visit.copy(finished = SiteVisit.clock(Live.clock()))
                    onChange(next)
                    if (diaryOn) diary(next) else model.message("Abschluss ${next.finished} Uhr gemerkt.")
                }, modifier = Modifier.heightIn(min = 48.dp)) {
                    Icon(Icons.Filled.Check, null, Modifier.size(18.dp))
                    Text("Fertig", Modifier.padding(start = 8.dp))
                }
            }
            else -> {
                Text(visit.text ?: "", Modifier.weight(1f), style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.Medium)
                if (diaryOn) TextButton(onClick = { diary(visit) }, modifier = Modifier.heightIn(min = 48.dp)) { Text("Ins Tagebuch") }
            }
        }
    }
}

/** Menü einer Einsatz- oder Baustellenzeile (langes Drücken): Route, Foto, Tagebuch, Mangel, Bauakte. */
@Composable
fun SiteQuickMenu(expanded: Boolean, onDismiss: () -> Unit, bauakteId: String, address: String?, latitude: Double? = null, longitude: Double? = null) {
    val nav = LocalNav.current
    val session = LocalSession.current
    val context = LocalContext.current
    val modules by session.modules.collectAsState()
    DropdownMenu(expanded = expanded, onDismissRequest = onDismiss) {
        if (address != null) {
            DropdownMenuItem(text = { Text("Route") }, leadingIcon = { Icon(Icons.Filled.Directions, null) },
                onClick = { onDismiss(); Links.route(context, address, latitude, longitude) })
        }
        if (session.service.bauakte(bauakteId) != null && session.service.canEdit(bauakteId)) {
            DropdownMenuItem(text = { Text("Foto aufnehmen") }, leadingIcon = { Icon(Icons.Filled.PhotoCamera, null) },
                onClick = { onDismiss(); nav.captureIn(bauakteId, SiteTask.Photo) })
            if (modules.isOn(AppModules.DIARY)) {
                DropdownMenuItem(text = { Text("Bautagebuch") }, leadingIcon = { Icon(Icons.AutoMirrored.Outlined.MenuBook, null) },
                    onClick = { onDismiss(); nav.captureIn(bauakteId, SiteTask.Diary) })
            }
            if (modules.isOn(AppModules.DEFECTS)) {
                DropdownMenuItem(text = { Text("Mangel melden") }, leadingIcon = { Icon(Icons.Outlined.ReportProblem, null) },
                    onClick = { onDismiss(); nav.captureIn(bauakteId, SiteTask.Defect) })
            }
        }
        DropdownMenuItem(text = { Text("Bauakte öffnen") }, leadingIcon = { Icon(Icons.Outlined.Folder, null) },
            onClick = { onDismiss(); nav.open(de.estrichone.core.NotificationRoute.Bauakte(bauakteId)) })
    }
}

// Kundenansicht

/** Was dem Kunden auf dem Gerät gezeigt wird: Vollbild ohne Navigation, Menüs und interne Angaben. */
sealed interface CustomerView {
    /** Fotos der Baustelle zum Durchblättern, beginnend bei [startId]. */
    data class Photos(val bauakteId: String, val startId: String? = null) : CustomerView

    /** Zusammenfassung eines Protokolls mit Unterschrift des Kunden; [onSigned] liefert Name und Unterschrift. */
    data class Signature(
        val title: String, val lines: List<Pair<String, String>>, val signer: String, val onSigned: (String, Bitmap) -> Unit,
    ) : CustomerView
}

@Composable
fun CustomerViewOverlay(view: CustomerView, onClose: () -> Unit) {
    BackHandler(onBack = onClose)
    KeepScreenOn()
    when (view) {
        is CustomerView.Photos -> CustomerPhotos(view, onClose)
        is CustomerView.Signature -> CustomerSignature(view, onClose)
    }
}

/** Fotos im Vollbild auf schwarzem Grund: wischen zum Blättern, nur Datum und Zähler, keine Beschriftungen und Aktionen. */
@Composable
private fun CustomerPhotos(view: CustomerView.Photos, onClose: () -> Unit) {
    val session = LocalSession.current
    val photos = remember(view.bauakteId) { session.service.photos(view.bauakteId).filter { it.isPhoto } }
    val start = remember(photos) { photos.indexOfFirst { it.id == view.startId }.coerceAtLeast(0) }
    val pager = rememberPagerState(initialPage = start) { photos.size }
    // Eingaben darunter abfangen: Die Ansicht liegt über der ganzen App.
    Box(Modifier.fillMaxSize().background(Color.Black).pointerInput(Unit) {}.testTag("customerPhotos")) {
        if (photos.isEmpty()) {
            Text("Zu dieser Baustelle gibt es noch keine Fotos.", color = Color.White, modifier = Modifier.align(Alignment.Center).padding(32.dp))
        } else {
            HorizontalPager(state = pager, modifier = Modifier.fillMaxSize()) { page ->
                val file = photos[page]
                val bitmap by produceState<Bitmap?>(null, file.id) { value = session.thumbnail(file, 1600) }
                Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                    bitmap?.let { Image(it.asImageBitmap(), "Foto ${page + 1}", contentScale = ContentScale.Fit, modifier = Modifier.fillMaxSize()) }
                }
            }
        }
        Row(Modifier.fillMaxWidth().windowInsetsPadding(WindowInsets.statusBars).padding(8.dp), verticalAlignment = Alignment.CenterVertically) {
            IconButton(onClick = onClose, modifier = Modifier.size(48.dp).clip(RoundedCornerShape(50)).background(Color.Black.copy(alpha = 0.5f))) {
                Icon(Icons.Filled.Close, "Kundenansicht schließen", tint = Color.White)
            }
            Spacer(Modifier.weight(1f))
            if (photos.isNotEmpty()) {
                val current = photos[pager.currentPage.coerceIn(0, photos.size - 1)]
                Text(listOfNotNull(current.time?.let { Dates.date(it.atZone(Dates.zone).toLocalDate()) }, "${pager.currentPage + 1} von ${photos.size}").joinToString(", "),
                    color = Color.White, style = MaterialTheme.typography.labelLarge,
                    modifier = Modifier.clip(RoundedCornerShape(50)).background(Color.Black.copy(alpha = 0.5f)).padding(horizontal = 12.dp, vertical = 8.dp))
            }
        }
    }
}

/** Protokoll für den Kunden: was abgenommen wird, darunter Name und Unterschrift mit dem Finger. Helle, ruhige Seite. */
@Composable
private fun CustomerSignature(view: CustomerView.Signature, onClose: () -> Unit) {
    var name by remember { mutableStateOf(view.signer) }
    var signature by remember { mutableStateOf<Bitmap?>(null) }
    Box(Modifier.fillMaxSize().background(tones.canvas).pointerInput(Unit) {}.testTag("customerSignature")) {
        Column(
            Modifier.fillMaxSize().windowInsetsPadding(WindowInsets.statusBars).windowInsetsPadding(WindowInsets.navigationBars).imePadding()
                .verticalScroll(rememberScrollState()).padding(horizontal = 20.dp, vertical = 12.dp).readable(),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(view.title, style = MaterialTheme.typography.headlineSmall, modifier = Modifier.weight(1f))
                IconButton(onClick = onClose, modifier = Modifier.size(48.dp)) { Icon(Icons.Filled.Close, "Schließen") }
            }
            SectionCard {
                view.lines.forEachIndexed { index, (label, value) ->
                    if (index > 0) RowDivider(16.dp)
                    Column(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 10.dp)) {
                        Text(label, style = MaterialTheme.typography.labelMedium, color = tones.textSecondary)
                        Text(value, style = MaterialTheme.typography.bodyLarge)
                    }
                }
            }
            OutlinedTextField(name, { name = it }, label = { Text("Name des Unterzeichners") }, singleLine = true, modifier = Modifier.fillMaxWidth())
            SignaturePad("Unterschrift", height = 220.dp) { signature = it }
            Text("Mit der Unterschrift bestätigen Sie die oben genannten Angaben.", style = MaterialTheme.typography.bodySmall, color = tones.textSecondary)
            Button(onClick = { signature?.let { view.onSigned(name.trim(), it); onClose() } }, enabled = signature != null && name.isNotBlank(),
                modifier = Modifier.fillMaxWidth().height(56.dp)) { Text("Unterschrift übernehmen", style = MaterialTheme.typography.titleMedium) }
            Spacer(Modifier.height(24.dp))
        }
    }
}

/** Zeile mit zwei Zeilen Text für Zusammenfassungen (Beschriftung klein, Wert darunter). */
@Composable
fun SummaryRow(label: String, value: String, maxLines: Int = 4) {
    Column(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 10.dp)) {
        Text(label, style = MaterialTheme.typography.labelMedium, color = tones.textSecondary)
        Text(value, style = MaterialTheme.typography.bodyLarge, maxLines = maxLines, overflow = TextOverflow.Ellipsis)
    }
}

/** Nur für Screenshots: Zustände, die sonst eine Bedienung bräuchten (Schritt der Abnahme, Kundenansicht). */
object FieldPreset {
    @Volatile
    var acceptanceStep: Int? = null
}
