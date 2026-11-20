package de.estrichone.android.ui

import android.graphics.Bitmap
import android.net.Uri
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.PickVisualMediaRequest
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.Image
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.CalendarMonth
import androidx.compose.material.icons.filled.WarningAmber
import androidx.compose.material.icons.outlined.CloudUpload
import androidx.compose.material.icons.outlined.LocalGasStation
import androidx.compose.material.icons.outlined.PhotoCamera
import androidx.compose.material.icons.outlined.PhotoLibrary
import androidx.compose.material3.Button
import androidx.compose.material3.DatePicker
import androidx.compose.material3.DatePickerDialog
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ExposedDropdownMenuBox
import androidx.compose.material3.ExposedDropdownMenuDefaults
import androidx.compose.material3.ExtendedFloatingActionButton
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.MenuAnchorType
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.pulltorefresh.PullToRefreshBox
import androidx.compose.material3.rememberDatePickerState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import de.estrichone.android.support.Images
import de.estrichone.android.support.Links
import de.estrichone.core.AbsenceText
import de.estrichone.core.Dates
import de.estrichone.core.FuelOptions
import de.estrichone.core.FuelPaymentMethod
import de.estrichone.core.FuelReceipt
import de.estrichone.core.FuelReceiptDraft
import de.estrichone.core.FuelReceiptRecorder
import de.estrichone.core.FuelReceiptStatus
import de.estrichone.core.FuelType
import de.estrichone.core.SyncState
import de.estrichone.core.fuelOptions
import de.estrichone.core.myFuelReceipts
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.text.NumberFormat
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneOffset
import java.util.Locale

private val euro: NumberFormat = NumberFormat.getCurrencyInstance(Locale.GERMANY)

private fun liters(value: Double) = "${FuelReceiptDraft.decimal(Math.round(value * 100) / 100.0)} l"

/**
 * Mehr › Tankbelege (wie FuelReceiptsView auf iOS): eigene Belege mit Stand der Prüfung, oben die noch nicht übertragenen.
 * „Beleg erfassen“ geht auch offline.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun FuelReceiptsScreen() {
    val session = LocalSession.current
    val nav = LocalNav.current
    val mine = rememberRefreshable("fuel-receipts/mine") { session.api.myFuelReceipts() }
    val pending by session.local { FuelReceiptRecorder(session.store, session.service).pending() }
    var opened by remember { mutableStateOf<FuelReceipt?>(null) }
    ScreenScaffold("Tankbelege", floating = {
        ExtendedFloatingActionButton(onClick = { nav.push(Route.FuelReceiptNew) }, icon = { Icon(Icons.Filled.Add, null) }, text = { Text("Beleg erfassen") })
    }) { padding ->
        PullToRefreshBox(isRefreshing = mine.refreshing && mine.value != null, onRefresh = { mine.refresh() }, modifier = Modifier.fillMaxSize()) {
            LazyColumn(contentPadding = padding.content(), verticalArrangement = Arrangement.spacedBy(10.dp), modifier = Modifier.readable()) {
                if (pending.isNotEmpty()) {
                    item(key = "pending-label") { GroupLabel("Auf diesem Gerät") }
                    item(key = "pending") {
                        SectionCard {
                            pending.forEachIndexed { index, r ->
                                if (index > 0) RowDivider(16.dp)
                                RowItem("${euro.format(r.amountGross)} · ${liters(r.liters)}", subtitle = "${AbsenceText.date(r.date)} · ${r.vehicleName}",
                                    value = if (r.state == SyncState.Problem) "Bitte prüfen" else "wird übertragen")
                            }
                        }
                    }
                }
                when (val load = mine.load) {
                    Load.Loading -> item(key = "loading") { SkeletonRows(3) }
                    is Load.Failed -> item(key = "offline") {
                        Banner("Ihre übertragenen Belege sind nur mit Verbindung sichtbar. Neue Belege lassen sich trotzdem erfassen.",
                            Icons.Outlined.CloudUpload, tones.textSecondary, tones.fill)
                    }
                    is Load.Done -> {
                        val receipts = load.value
                        if (receipts.isEmpty() && pending.isEmpty()) {
                            item(key = "empty") {
                                EmptyState(Icons.Outlined.LocalGasStation, "Noch keine Tankbelege",
                                    "Beleg nach dem Tanken fotografieren – Fahrzeug, Liter und Betrag sind schnell erfasst.")
                            }
                        } else if (receipts.isNotEmpty()) {
                            item(key = "mine-label") { GroupLabel("Meine Tankbelege") }
                            item(key = "mine") {
                                SectionCard {
                                    receipts.forEachIndexed { index, r ->
                                        if (index > 0) RowDivider(16.dp)
                                        FuelRow(r) { opened = r }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
    opened?.let { r -> FuelReceiptDialog(r) { opened = null } }
}

@Composable
private fun statusColor(status: FuelReceiptStatus) = when (status) {
    FuelReceiptStatus.New -> MaterialTheme.colorScheme.primary
    FuelReceiptStatus.Checked -> tones.success
    FuelReceiptStatus.Rejected -> tones.danger
    FuelReceiptStatus.Settled -> tones.textSecondary
}

@Composable
private fun FuelRow(r: FuelReceipt, onClick: () -> Unit) {
    RowItem("${euro.format(r.amountGross)} · ${liters(r.liters)} ${r.fuelType.title}",
        subtitle = listOfNotNull("${AbsenceText.date(r.date)} · ${r.vehicleText}", r.rejectionReason?.takeIf { r.status == FuelReceiptStatus.Rejected })
            .joinToString("\n"),
        onClick = onClick,
        trailing = {
            Column(horizontalAlignment = Alignment.End) {
                Text(r.statusText, style = MaterialTheme.typography.labelLarge, color = statusColor(r.status))
                Text(r.number, style = MaterialTheme.typography.bodySmall, color = tones.textTertiary)
            }
        })
}

@Composable
private fun FuelReceiptDialog(r: FuelReceipt, onDismiss: () -> Unit) {
    androidx.compose.material3.AlertDialog(onDismissRequest = onDismiss, confirmButton = { TextButton(onClick = onDismiss) { Text("Fertig") } },
        title = { Text(r.number) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                Text("Status: ${r.statusText}", color = statusColor(r.status))
                r.rejectionReason?.let { Text("Grund: $it", color = tones.danger) }
                Secondary("${AbsenceText.date(r.date)} · ${r.vehicleText}")
                Secondary("${r.fuelType.title} · ${liters(r.liters)} · ${euro.format(r.amountGross)} · ${r.paymentMethod.title}")
                r.odometerKm?.let { Secondary("Kilometerstand ${FuelReceiptDraft.grouped(it)} km") }
                r.consumptionPer100Km?.let { Secondary("Verbrauch ${FuelReceiptDraft.decimal(it)} l/100 km") }
                r.warnings.filter { it.code != "missing_image" }.forEach { w ->
                    Row(verticalAlignment = Alignment.Top) {
                        Icon(Icons.Filled.WarningAmber, null, tint = tones.warning, modifier = Modifier.size(16.dp))
                        Text(w.text, style = MaterialTheme.typography.bodySmall, color = tones.warning, modifier = Modifier.padding(start = 4.dp))
                    }
                }
            }
        })
}

/** Beispielbeleg für Demomodus und Bildschirmfotos: eine schlichte Tankquittung. */
private fun demoReceipt(): Bitmap {
    val bitmap = Bitmap.createBitmap(600, 900, Bitmap.Config.ARGB_8888)
    val canvas = android.graphics.Canvas(bitmap)
    canvas.drawColor(0xFFF7F7F7.toInt())
    val paint = android.graphics.Paint().apply {
        color = 0xFF262626.toInt(); textSize = 30f; typeface = android.graphics.Typeface.MONOSPACE; isAntiAlias = true
    }
    listOf("ARAL Tankstelle", "Landshut Nord", "", "Datum 18.11.2026  07:12", "Säule 4", "", "Diesel", "61,37 l x 1,699 EUR/l", "",
        "Summe EUR        104,27", "", "Kartenzahlung").forEachIndexed { index, line -> canvas.drawText(line, 44f, 90f + index * 56f, paint) }
    return bitmap
}

/** Voreinstellung für Bildschirmfotos (Roborazzi): ausgefüllter Beleg. */
object FuelPreset {
    var filled: Boolean = false
}

/**
 * Neuer Tankbeleg: Beleg fotografieren (Kamera-App) oder aus der Galerie wählen, verkleinert wie alle Fotos; Fahrzeug
 * (vorbelegt mit dem heutigen Dispo-Fahrzeug), Datum, Kilometerstand, Liter, Betrag, Kraftstoff, Zahlart, Tankstelle.
 * Texterkennung bräuchte ML Kit (Google-Dienste) und entfällt auf Android.
 */
@OptIn(ExperimentalMaterial3Api::class, ExperimentalLayoutApi::class)
@Composable
fun FuelReceiptNewScreen() {
    val session = LocalSession.current
    val nav = LocalNav.current
    val model = LocalModel.current
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val recorder = remember { FuelReceiptRecorder(session.store, session.service) }
    var options by remember { mutableStateOf<FuelOptions?>(recorder.cachedOptions()) }
    var draft by remember { mutableStateOf(FuelReceiptDraft.prefilled(options)) }
    var touched by remember { mutableStateOf(false) }
    var photo by remember { mutableStateOf<ByteArray?>(null) }
    var preview by remember { mutableStateOf<Bitmap?>(null) }
    var vehicleMenu by remember { mutableStateOf(false) }
    var paymentMenu by remember { mutableStateOf(false) }
    var pickingDate by remember { mutableStateOf(false) }
    var saving by remember { mutableStateOf(false) }
    var error by remember { mutableStateOf<String?>(null) }
    val status by session.status.collectAsState()

    LaunchedEffect(Unit) {
        val fresh = runCatching { withContext(Dispatchers.IO) { session.api.fuelOptions() } }.getOrNull()
        if (fresh != null) {
            recorder.rememberOptions(fresh)
            options = fresh
            if (!touched) draft = FuelReceiptDraft.prefilled(fresh)
        }
        if (FuelPreset.filled) {
            draft = draft.copy(odometer = "48210", liters = "61,37", amount = "104,27", station = "Aral Landshut Nord")
            preview = demoReceipt()
        }
    }

    fun take(bytes: ByteArray?) {
        if (bytes == null) return
        scope.launch {
            val prepared = withContext(Dispatchers.Default) { Images.preparePhoto(bytes) }
            photo = prepared
            preview = withContext(Dispatchers.Default) { Images.decode(prepared, 900) }
        }
    }

    // Zieldatei der Kamera-App erst beim Antippen anlegen (FileProvider).
    var capture by remember { mutableStateOf<Pair<java.io.File, Uri>?>(null) }
    val camera = rememberLauncherForActivityResult(ActivityResultContracts.TakePicture()) { ok ->
        val file = capture?.first
        if (ok && file != null) take(runCatching { file.readBytes() }.getOrNull())
        file?.delete()
    }
    val gallery = rememberLauncherForActivityResult(ActivityResultContracts.PickVisualMedia()) { uri: Uri? ->
        uri?.let { take(runCatching { context.contentResolver.openInputStream(it)?.use { s -> s.readBytes() } }.getOrNull()) }
    }
    val vehicles = options?.vehicles.orEmpty()
    val vehicle = vehicles.firstOrNull { it.id == draft.vehicleId }
    fun update(change: FuelReceiptDraft) {
        touched = true
        draft = change
    }

    ScreenScaffold("Tankbeleg erfassen") { padding ->
        Column(Modifier.readable().verticalScroll(rememberScrollState()).padding(padding.content(extraBottom = 24)), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            preview?.let { bitmap ->
                Image(bitmap.asImageBitmap(), "Belegfoto", contentScale = ContentScale.Fit,
                    modifier = Modifier.fillMaxWidth().heightIn(max = 260.dp).clip(RoundedCornerShape(12.dp)))
            }
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Button(onClick = {
                    runCatching { Links.captureUri(context, "tankbeleg-${System.currentTimeMillis()}.jpg") }.getOrNull()?.let {
                        capture = it
                        camera.launch(it.second)
                    }
                }, modifier = Modifier.weight(1f).height(52.dp)) {
                    Icon(Icons.Outlined.PhotoCamera, null)
                    Spacer(Modifier.width(8.dp))
                    Text(if (photo == null) "Beleg fotografieren" else "Neu fotografieren")
                }
                OutlinedButton(onClick = { gallery.launch(PickVisualMediaRequest(ActivityResultContracts.PickVisualMedia.ImageOnly)) },
                    modifier = Modifier.height(52.dp)) {
                    Icon(Icons.Outlined.PhotoLibrary, "Aus der Galerie wählen")
                }
            }

            ExposedDropdownMenuBox(expanded = vehicleMenu, onExpandedChange = { vehicleMenu = it }) {
                OutlinedTextField(vehicle?.title ?: if (vehicles.isEmpty()) "Keine Fahrzeuge (bitte einmal mit Verbindung öffnen)" else "Bitte wählen", {},
                    readOnly = true, label = { Text("Fahrzeug") }, trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(vehicleMenu) },
                    supportingText = if (options?.suggestedVehicleId != null && draft.vehicleId == options?.suggestedVehicleId) ({ Text("Aus der heutigen Disposition") }) else null,
                    modifier = Modifier.fillMaxWidth().menuAnchor(MenuAnchorType.PrimaryNotEditable))
                ExposedDropdownMenu(expanded = vehicleMenu, onDismissRequest = { vehicleMenu = false }) {
                    vehicles.forEach { v ->
                        DropdownMenuItem(text = { Text(v.title) }, onClick = {
                            update(draft.copy(vehicleId = v.id, fuelType = v.lastFuelType ?: draft.fuelType))
                            vehicleMenu = false
                        }, modifier = Modifier.heightIn(min = 48.dp))
                    }
                }
            }
            SectionCard {
                RowItem("Datum", icon = Icons.Filled.CalendarMonth, value = "${Dates.weekdayName(draft.date).take(2)}, ${AbsenceText.date(draft.date.toString())}",
                    onClick = { pickingDate = true })
            }
            OutlinedTextField(draft.odometer, { update(draft.copy(odometer = it.take(9))) }, label = { Text("Kilometerstand") },
                placeholder = { Text(vehicle?.lastOdometerKm?.let { "zuletzt ${FuelReceiptDraft.grouped(it)}" } ?: "km") },
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number), singleLine = true, modifier = Modifier.fillMaxWidth())
            Text("Kraftstoff", style = MaterialTheme.typography.titleSmall)
            FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                FuelType.entries.forEach { t -> FilterChip(draft.fuelType == t, { update(draft.copy(fuelType = t)) }, label = { Text(t.title) }) }
            }
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                OutlinedTextField(draft.liters, { update(draft.copy(liters = it.take(10))) }, label = { Text("Menge (Liter)") }, singleLine = true,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Decimal), modifier = Modifier.weight(1f))
                OutlinedTextField(draft.amount, { update(draft.copy(amount = it.take(10))) }, label = { Text("Betrag brutto (€)") }, singleLine = true,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Decimal), modifier = Modifier.weight(1f))
            }
            draft.pricePerLiter?.let { Secondary("Preis je Liter: ${FuelReceiptDraft.decimal(it)} €") }
            ExposedDropdownMenuBox(expanded = paymentMenu, onExpandedChange = { paymentMenu = it }) {
                OutlinedTextField(draft.paymentMethod.title, {}, readOnly = true, label = { Text("Bezahlt mit") },
                    trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(paymentMenu) },
                    supportingText = if (draft.paymentMethod == FuelPaymentMethod.Private) ({ Text("Das Büro erstattet den Betrag nach der Prüfung.") }) else null,
                    modifier = Modifier.fillMaxWidth().menuAnchor(MenuAnchorType.PrimaryNotEditable))
                ExposedDropdownMenu(expanded = paymentMenu, onDismissRequest = { paymentMenu = false }) {
                    FuelPaymentMethod.entries.forEach { p ->
                        DropdownMenuItem(text = { Text(p.title) }, onClick = { update(draft.copy(paymentMethod = p)); paymentMenu = false },
                            modifier = Modifier.heightIn(min = 48.dp))
                    }
                }
            }
            OutlinedTextField(draft.station, { update(draft.copy(station = it.take(200))) }, label = { Text("Tankstelle (optional)") }, singleLine = true,
                modifier = Modifier.fillMaxWidth())
            draft.hints(vehicle).forEach { hint ->
                Row(verticalAlignment = Alignment.Top) {
                    Icon(Icons.Filled.WarningAmber, null, tint = tones.warning, modifier = Modifier.size(18.dp))
                    Text(hint, style = MaterialTheme.typography.bodyMedium, color = tones.warning, modifier = Modifier.padding(start = 6.dp))
                }
            }
            if (status.offline) Banner("Keine Verbindung: Der Beleg wird gespeichert und automatisch übertragen.", Icons.Outlined.CloudUpload, tones.warning, tones.warningSoft)
            error?.let { Text(it, color = tones.danger) }
            Button(onClick = {
                saving = true
                scope.launch {
                    val result = runCatching { withContext(Dispatchers.IO) { recorder.record(draft, photo, vehicle) } }
                    saving = false
                    result.onSuccess {
                        session.synchronizeNow()
                        session.memo.remove("fuel-receipts/mine")
                        model.message("Tankbeleg gespeichert. Er wird übertragen, sobald eine Verbindung besteht.")
                        nav.back()
                    }.onFailure { error = it.message ?: "Der Beleg konnte nicht gespeichert werden." }
                }
            }, enabled = draft.validate() == null && !saving, modifier = Modifier.fillMaxWidth().height(56.dp)) {
                Text(if (saving) "Wird gespeichert …" else "Speichern")
            }
            if (photo == null) Secondary("Ohne Foto speichern geht auch – das Büro sieht dann einen Hinweis.", Modifier.fillMaxWidth())
        }
    }
    if (pickingDate) {
        val state = rememberDatePickerState(initialSelectedDateMillis = draft.date.atStartOfDay().toInstant(ZoneOffset.UTC).toEpochMilli())
        DatePickerDialog(onDismissRequest = { pickingDate = false }, confirmButton = {
            TextButton(onClick = {
                state.selectedDateMillis?.let { update(draft.copy(date = Instant.ofEpochMilli(it).atZone(ZoneOffset.UTC).toLocalDate().coerceAtMost(LocalDate.now(Dates.zone)))) }
                pickingDate = false
            }) { Text("Übernehmen") }
        }, dismissButton = { TextButton(onClick = { pickingDate = false }) { Text("Abbrechen") } }) { DatePicker(state) }
    }
}
