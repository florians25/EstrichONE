package de.estrichone.android.ui

import android.Manifest
import android.content.pm.PackageManager
import android.graphics.Bitmap
import android.net.Uri
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.PickVisualMediaRequest
import androidx.activity.result.contract.ActivityResultContracts
import androidx.camera.core.CameraSelector
import androidx.camera.core.ImageCapture
import androidx.camera.core.ImageCaptureException
import androidx.camera.core.ImageProxy
import androidx.camera.core.Preview
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.camera.view.PreviewView
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBars
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBars
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.DocumentScanner
import androidx.compose.material.icons.filled.PhotoCamera
import androidx.compose.material.icons.filled.PhotoLibrary
import androidx.compose.material.icons.filled.PictureAsPdf
import androidx.compose.material.icons.filled.Thermostat
import androidx.compose.material.icons.filled.UploadFile
import androidx.compose.material.icons.filled.Videocam
import androidx.compose.material.icons.automirrored.outlined.Assignment
import androidx.compose.material.icons.automirrored.outlined.FactCheck
import androidx.compose.material.icons.outlined.Folder
import androidx.compose.material.icons.automirrored.outlined.MenuBook
import androidx.compose.material.icons.outlined.ReportProblem
import androidx.compose.material.icons.automirrored.outlined.StickyNote2
import androidx.compose.material.icons.outlined.VerifiedUser
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilledIconButton
import androidx.compose.material3.IconButtonDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.Text
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateListOf
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
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalLifecycleOwner
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.core.content.ContextCompat
import de.estrichone.android.Session
import de.estrichone.android.support.Images
import de.estrichone.android.support.Links
import de.estrichone.android.support.Videos
import de.estrichone.core.AppModules
import de.estrichone.core.Bauakte
import de.estrichone.core.Dates
import de.estrichone.core.CameraShots
import de.estrichone.core.VideoTarget
import de.estrichone.core.DocumentCategory
import de.estrichone.core.WorkItemKind
import de.estrichone.core.order
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.time.LocalDate

/** Aktion, für die noch die Baustelle gewählt wird (wie SiteTask auf iOS). */
enum class SiteTask(val title: String, val icon: ImageVector) {
    Photo("Fotos aufnehmen", Icons.Filled.PhotoCamera),
    Gallery("Aus Galerie", Icons.Filled.PhotoLibrary),
    Video("Video aufnehmen", Icons.Filled.Videocam),
    Scan("Dokument scannen", Icons.Filled.DocumentScanner),
    Upload("Datei hochladen", Icons.Filled.UploadFile),
    Diary("Bautagebuch", Icons.AutoMirrored.Outlined.MenuBook),
    Inspection("Erstbegehung", Icons.AutoMirrored.Outlined.FactCheck),
    Measurement("CM-Messung", Icons.Filled.Thermostat),
    Acceptance("Abnahme", Icons.Outlined.VerifiedUser),
    Defect("Mangel melden", Icons.Outlined.ReportProblem),
    Task("Aufgabe anlegen", Icons.AutoMirrored.Outlined.Assignment),
    Note("Notiz schreiben", Icons.AutoMirrored.Outlined.StickyNote2),
    Open("Baustelle öffnen", Icons.Outlined.Folder);

    fun with(bauakteId: String?) = CaptureRequest(this, bauakteId)

    /** Gehört zu einem eingeschalteten Modul (Systemeinstellungen › Module)? */
    fun isAvailable(modules: AppModules): Boolean = when (this) {
        Diary -> modules.isOn(AppModules.DIARY)
        Inspection, Measurement -> modules.isOn(AppModules.INSPECTIONS)
        Acceptance -> modules.isOn(AppModules.ACCEPTANCE)
        Defect -> modules.isOn(AppModules.DEFECTS)
        Task -> modules.isOn(AppModules.TASKS)
        else -> true
    }
}

data class CaptureRequest(val task: SiteTask, val bauakteId: String?)

/** Aktionsblatt „Erfassen“ (wie CaptureHub auf iOS). */
@OptIn(ExperimentalMaterial3Api::class, ExperimentalLayoutApi::class)
@Composable
fun CaptureHub(bauakteId: String?, onDismiss: () -> Unit) {
    val nav = LocalNav.current
    val session = LocalSession.current
    val title = bauakteId?.let { session.service.bauakte(it)?.title }
    val modules by session.modules.collectAsState()
    ModalBottomSheet(onDismissRequest = onDismiss, containerColor = tones.canvas) {
        Column(Modifier.padding(horizontal = 16.dp).padding(bottom = 28.dp)) {
            Text("Erfassen", style = MaterialTheme.typography.headlineSmall, modifier = Modifier.padding(start = 4.dp))
            Secondary(title?.let { "für $it" } ?: "Wird auf dem Gerät gespeichert und beim nächsten Abgleich übertragen.", Modifier.padding(start = 4.dp, bottom = 12.dp))
            listOf(
                "Fotos und Dokumente" to listOf(SiteTask.Photo, SiteTask.Gallery, SiteTask.Video, SiteTask.Scan, SiteTask.Upload),
                "Protokolle" to listOf(SiteTask.Diary, SiteTask.Inspection, SiteTask.Measurement, SiteTask.Acceptance),
                "Aufgaben und Notizen" to listOf(SiteTask.Defect, SiteTask.Task, SiteTask.Note),
            ).map { (group, tasks) -> group to tasks.filter { it.isAvailable(modules) } }.filter { it.second.isNotEmpty() }.forEach { (group, tasks) ->
                GroupLabel(group, Modifier.padding(start = 0.dp))
                FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp), maxItemsInEachRow = 4) {
                    tasks.forEach { task ->
                        Column(
                            Modifier.weight(1f).clip(RoundedCornerShape(16.dp)).background(tones.card)
                                .clickable { onDismiss(); nav.captureTask = task.with(bauakteId) }.padding(vertical = 14.dp, horizontal = 4.dp),
                            horizontalAlignment = Alignment.CenterHorizontally,
                        ) {
                            IconTile(task.icon, size = 40.dp)
                            Spacer(Modifier.height(6.dp))
                            Text(task.title, style = MaterialTheme.typography.labelMedium, textAlign = TextAlign.Center, maxLines = 2)
                        }
                    }
                    repeat((4 - tasks.size % 4) % 4) { Spacer(Modifier.weight(1f)) }
                }
            }
        }
    }
}

/** Baustelle wählen: heutige Einsätze zuerst, dann laufende, mit Suche (offline). */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun BauakteQuickPicker(title: String, onDone: (Bauakte?) -> Unit) {
    val session = LocalSession.current
    var search by remember { mutableStateOf("") }
    val todayIds by produceState(emptySet<String>()) {
        val today = LocalDate.now(Dates.zone).toString()
        value = session.weekPlan()?.assignments?.filter { it.day == today }?.map { it.bauakteId }?.toSet().orEmpty()
    }
    val all = remember { session.service.bauakten() }
    ModalBottomSheet(onDismissRequest = { onDone(null) }, sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true), containerColor = tones.canvas) {
        Column(Modifier.padding(horizontal = 16.dp)) {
            Text(title, style = MaterialTheme.typography.titleLarge, modifier = Modifier.padding(bottom = 4.dp))
            Secondary("Baustelle wählen", Modifier.padding(bottom = 12.dp))
            SearchField(search, { search = it }, "Nummer, Bauvorhaben oder Adresse")
        }
        LazyColumn(contentPadding = androidx.compose.foundation.layout.PaddingValues(16.dp)) {
            val today = all.filter { it.id in todayIds }
            if (search.isBlank() && today.isNotEmpty()) {
                item { GroupLabel("Heute im Einsatz", Modifier.padding(start = 4.dp)) }
                item { PickerCard(today, onDone) }
            }
            val others = all.filter { it.matches(search) && (search.isNotBlank() || (it.id !in todayIds && it.status.order <= 3)) }
            item { GroupLabel(if (search.isBlank()) "Laufende Baustellen" else "Treffer", Modifier.padding(start = 4.dp)) }
            item {
                if (others.isEmpty()) Secondary(if (search.isBlank()) "Keine weiteren laufenden Baustellen." else "Keine Bauakte gefunden.", Modifier.padding(8.dp))
                else PickerCard(others, onDone)
            }
        }
    }
}

@Composable
private fun PickerCard(list: List<Bauakte>, onDone: (Bauakte?) -> Unit) {
    SectionCard {
        list.forEachIndexed { index, b ->
            if (index > 0) RowDivider(16.dp)
            BauakteRow(b, null, null) { onDone(b) }
        }
    }
}

/** Ablauf nach der Wahl im Aktionsblatt: ggf. Baustelle wählen, dann Formular bzw. Kamera. */
@Composable
fun CaptureFlow(request: CaptureRequest, onDone: () -> Unit) {
    val nav = LocalNav.current
    var bauakteId by remember(request) { mutableStateOf(request.bauakteId) }
    val id = bauakteId
    if (id == null) {
        BauakteQuickPicker(request.task.title) { picked -> if (picked == null) onDone() else bauakteId = picked.id }
        return
    }
    when (request.task) {
        SiteTask.Photo, SiteTask.Scan, SiteTask.Open -> LaunchedEffect(id) {
            onDone()
            nav.tab = Tab.Bauakten
            nav.set(Tab.Bauakten, listOfNotNull(Route.Bauakte(id), when (request.task) {
                SiteTask.Photo -> Route.Camera(id)
                SiteTask.Scan -> Route.Camera(id, scan = true)
                else -> null
            }))
        }
        SiteTask.Gallery -> GalleryImport(id, onDone)
        SiteTask.Video -> VideoCapture(id, onDone)
        SiteTask.Upload -> DocumentImport(id, onDone)
        SiteTask.Diary -> DiarySheet(id, onDone)
        SiteTask.Measurement -> CmSheet(id, onDone)
        SiteTask.Defect -> TaskSheet(id, WorkItemKind.Defect, onDone)
        SiteTask.Task -> TaskSheet(id, WorkItemKind.Task, onDone)
        SiteTask.Note -> NoteSheet(id, onDone)
        SiteTask.Inspection, SiteTask.Acceptance -> LaunchedEffect(id) {
            onDone()
            nav.set(Tab.Bauakten, listOf(Route.Bauakte(id), if (request.task == SiteTask.Inspection) Route.Inspection(id, null) else Route.Acceptance(id, null)))
            nav.tab = Tab.Bauakten
        }
    }
}

/** Fotos aus der Galerie übernehmen (Photo Picker, ohne Speicherberechtigung). */
@Composable
fun GalleryImport(bauakteId: String, onDone: () -> Unit) {
    val session = LocalSession.current
    val model = LocalModel.current
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val launcher = rememberLauncherForActivityResult(ActivityResultContracts.PickMultipleVisualMedia(20)) { uris ->
        scope.launch {
            var count = 0
            for (uri in uris) if (importMedia(context, session, bauakteId, uri)) count++
            if (count > 0) model.message(if (count == 1) "1 Foto gespeichert." else "$count Fotos gespeichert.")
            onDone()
        }
    }
    LaunchedEffect(Unit) { launcher.launch(PickVisualMediaRequest(ActivityResultContracts.PickVisualMedia.ImageAndVideo)) }
}

private suspend fun importMedia(context: android.content.Context, session: Session, bauakteId: String, uri: Uri): Boolean {
    val type = context.contentResolver.getType(uri) ?: "image/jpeg"
    if (type.startsWith("video/")) {
        // Videos wie Aufnahmen auf 720p/2 Mbit/s verkleinern; gelingt das nicht, das Original übernehmen.
        val small = Videos.compress(context.applicationContext, uri)
        val bytes = withContext(Dispatchers.IO) {
            small?.readBytes() ?: context.contentResolver.openInputStream(uri)?.use { it.readBytes() }
        } ?: return false
        small?.delete()
        return withContext(Dispatchers.IO) { session.service.addFile(bauakteId, "video", bytes, "Video.mp4", if (small != null) "video/mp4" else type) != "" }
    }
    return withContext(Dispatchers.IO) {
        val bytes = context.contentResolver.openInputStream(uri)?.use { it.readBytes() } ?: return@withContext false
        session.service.addFile(bauakteId, "photo", Images.preparePhoto(bytes), "Foto.jpg", "image/jpeg") != ""
    }
}

/** Kamera-App mit Höchstdauer (2 Minuten), wie auf iOS. */
private class CaptureLimitedVideo : ActivityResultContracts.CaptureVideo() {
    override fun createIntent(context: android.content.Context, input: Uri) =
        super.createIntent(context, input).putExtra(android.provider.MediaStore.EXTRA_DURATION_LIMIT, (VideoTarget.MAX_DURATION_MS / 1000).toInt())
}

/**
 * Video mit der Kamera-App des Geräts aufnehmen, danach auf 720p/2 Mbit/s verkleinern (Videos.compress) und speichern.
 * Das Verkleinern läuft im Bereich der Sitzung weiter, auch wenn die Ansicht schon geschlossen ist.
 */
@Composable
fun VideoCapture(bauakteId: String, onDone: () -> Unit) {
    val session = LocalSession.current
    val model = LocalModel.current
    val context = LocalContext.current
    val target = remember { Links.captureUri(context, "video-${System.currentTimeMillis()}.mp4") }
    val launcher = rememberLauncherForActivityResult(CaptureLimitedVideo()) { ok ->
        val source = target.first
        if (ok && source.length() > 0) {
            model.message("Video wird für die Übertragung verkleinert …")
            session.scope.launch {
                val small = Videos.compress(context.applicationContext, Uri.fromFile(source))
                withContext(Dispatchers.IO) {
                    session.service.addFile(bauakteId, "video", (small ?: source).readBytes(), "Video.mp4", "video/mp4")
                    small?.delete()
                    source.delete()
                }
                model.message("Video gespeichert. Es wird übertragen, sobald eine Verbindung besteht.")
            }
        }
        onDone()
    }
    LaunchedEffect(Unit) { launcher.launch(target.second) }
}

/** PDF oder Bild als Dokument hochladen (Speicherzugriffsframework, Art wählen). */
@Composable
fun DocumentImport(bauakteId: String, onDone: () -> Unit) {
    val session = LocalSession.current
    val model = LocalModel.current
    val context = LocalContext.current
    var picked by remember { mutableStateOf<Pair<ByteArray, Pair<String, String>>?>(null) }
    val launcher = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
        if (uri == null) {
            onDone()
            return@rememberLauncherForActivityResult
        }
        val resolver = context.contentResolver
        val name = resolver.query(uri, null, null, null, null)?.use { c ->
            val index = c.getColumnIndex(android.provider.OpenableColumns.DISPLAY_NAME)
            if (c.moveToFirst() && index >= 0) c.getString(index) else null
        } ?: "Dokument.pdf"
        val bytes = resolver.openInputStream(uri)?.use { it.readBytes() }
        if (bytes == null) onDone() else picked = bytes to (name to (resolver.getType(uri) ?: "application/pdf"))
    }
    LaunchedEffect(Unit) { launcher.launch(arrayOf("application/pdf", "image/*")) }
    picked?.let { (bytes, meta) ->
        CategorySheet(meta.first.substringBeforeLast('.'), onDismiss = onDone) { caption, category ->
            session.service.addFile(bauakteId, "document", bytes, meta.first, meta.second, caption, category)
            model.message("Dokument gespeichert. Es wird übertragen, sobald eine Verbindung besteht.")
            onDone()
        }
    }
}

// Kamera

/** Lautstärketaste als Auslöser (mit Handschuhen bedienbar, docs/reengineering/08). */
object VolumeShutter {
    var listener: (() -> Unit)? = null
}

/**
 * Kamera mit Serienaufnahme (CameraX). Jedes Foto wird sofort verschlüsselt auf dem Gerät gespeichert und angekündigt.
 * Im Scanmodus werden die Seiten gesammelt und als PDF abgelegt.
 */
@Composable
fun CameraScreen(bauakteId: String, scan: Boolean) {
    val context = LocalContext.current
    val nav = LocalNav.current
    val session = LocalSession.current
    val model = LocalModel.current
    val lifecycle = LocalLifecycleOwner.current
    val scope = rememberCoroutineScope()
    var granted by remember { mutableStateOf(ContextCompat.checkSelfPermission(context, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED) }
    val permission = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted = it }
    val capture = remember { ImageCapture.Builder().setCaptureMode(ImageCapture.CAPTURE_MODE_MINIMIZE_LATENCY).build() }
    val shots = remember { mutableStateListOf<Bitmap>() }
    val pages = remember { mutableStateListOf<ByteArray>() }
    var busy by remember { mutableStateOf(false) }
    // Ausgelöste, noch nicht gespeicherte Fotos: „Fertig“ schließt erst, wenn sie gespeichert sind.
    var pendingShots by remember { mutableStateOf(CameraShots()) }
    var savedCount by remember { mutableIntStateOf(0) }
    var naming by remember { mutableStateOf(false) }
    var gallery by remember { mutableStateOf(false) }
    val bauakte = remember { session.service.bauakte(bauakteId) }

    LaunchedEffect(Unit) { if (!granted) permission.launch(Manifest.permission.CAMERA) }

    fun shoot() {
        if (busy || !granted || pendingShots.closing) return
        busy = true
        pendingShots = pendingShots.pressed()
        capture.takePicture(ContextCompat.getMainExecutor(context), object : ImageCapture.OnImageCapturedCallback() {
            override fun onCaptureSuccess(image: ImageProxy) {
                val buffer = image.planes[0].buffer
                val raw = ByteArray(buffer.remaining()).also { buffer.get(it) }
                val rotation = image.imageInfo.rotationDegrees
                image.close()
                // Im Bereich der Sitzung, nicht der Ansicht: Wer direkt nach dem Auslösen „Fertig“ oder „Zurück“ tippt,
                // verliert das Foto nicht (vorher brach das Schließen das Verkleinern und Speichern ab).
                session.scope.launch {
                    try {
                        val jpeg = withContext(Dispatchers.Default) { Images.preparePhoto(raw, rotation) }
                        Images.decode(jpeg, 200)?.let { shots.add(0, it) }
                        if (scan) pages.add(jpeg) else withContext(Dispatchers.IO) { session.service.addFile(bauakteId, "photo", jpeg, "Foto.jpg", "image/jpeg") }
                        savedCount++
                    } catch (e: Exception) {
                        model.message("Das Foto konnte nicht gespeichert werden: ${e.message}")
                    } finally {
                        busy = false
                        pendingShots = pendingShots.settled()
                    }
                }
            }

            override fun onError(exception: ImageCaptureException) {
                busy = false
                pendingShots = pendingShots.settled()
                model.message("Die Aufnahme ist fehlgeschlagen: ${exception.message}")
            }
        })
    }

    // „Fertig“: erst schließen, wenn alle ausgelösten Fotos gespeichert sind (höchstens 10 s warten).
    LaunchedEffect(pendingShots) {
        if (pendingShots.canClose) {
            if (scan && pages.isNotEmpty()) {
                // PDF benennen; wird das Blatt verworfen, geht es mit der Kamera weiter.
                pendingShots = CameraShots()
                naming = true
            } else {
                pendingShots = pendingShots.closed()
                nav.back()
            }
        } else if (pendingShots.closing && pendingShots.pending > 0) {
            kotlinx.coroutines.delay(10_000)
            pendingShots = pendingShots.forced()
        }
    }

    DisposableEffect(Unit) {
        VolumeShutter.listener = { shoot() }
        onDispose { VolumeShutter.listener = null }
    }

    Box(Modifier.fillMaxSize().background(Color.Black)) {
        if (granted) {
            AndroidView(factory = { ctx ->
                PreviewView(ctx).also { view ->
                    val future = ProcessCameraProvider.getInstance(ctx)
                    future.addListener({
                        val provider = future.get()
                        val preview = Preview.Builder().build().also { it.setSurfaceProvider(view.surfaceProvider) }
                        runCatching {
                            provider.unbindAll()
                            provider.bindToLifecycle(lifecycle, CameraSelector.DEFAULT_BACK_CAMERA, preview, capture)
                        }
                    }, ContextCompat.getMainExecutor(ctx))
                }
            }, modifier = Modifier.fillMaxSize())
        } else {
            EmptyState(Icons.Filled.PhotoCamera, "Kamera nicht freigegeben", "Bitte den Zugriff auf die Kamera erlauben. Alternativ Fotos aus der Galerie wählen.",
                Modifier.align(Alignment.Center)) {
                Button(onClick = { permission.launch(Manifest.permission.CAMERA) }) { Text("Zugriff erlauben") }
            }
        }
        Row(Modifier.fillMaxWidth().windowInsetsPadding(WindowInsets.statusBars).padding(8.dp), verticalAlignment = Alignment.CenterVertically) {
            IconButton(onClick = { nav.back() }) { Icon(Icons.AutoMirrored.Filled.ArrowBack, "Zurück", tint = Color.White) }
            Column(Modifier.weight(1f)) {
                Text(if (scan) "Dokument scannen" else "Fotos", color = Color.White, style = MaterialTheme.typography.titleMedium)
                Text(bauakte?.title ?: "", color = Color.White.copy(alpha = 0.8f), style = MaterialTheme.typography.bodySmall)
            }
            Text(if (scan) "${pages.size} Seiten" else "$savedCount Fotos", color = Color.White, style = MaterialTheme.typography.labelLarge,
                modifier = Modifier.clip(RoundedCornerShape(50)).background(Color.Black.copy(alpha = 0.5f)).padding(horizontal = 12.dp, vertical = 6.dp))
        }
        Column(Modifier.align(Alignment.BottomCenter).fillMaxWidth().background(Color.Black.copy(alpha = 0.55f))
            .windowInsetsPadding(WindowInsets.navigationBars).padding(vertical = 16.dp)) {
            LazyRow(Modifier.height(64.dp).padding(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                items(shots) { bitmap ->
                    Image(bitmap.asImageBitmap(), null, contentScale = ContentScale.Crop, modifier = Modifier.size(56.dp).clip(RoundedCornerShape(10.dp)))
                }
            }
            Row(Modifier.fillMaxWidth().padding(top = 12.dp, start = 24.dp, end = 24.dp), verticalAlignment = Alignment.CenterVertically) {
                Box(Modifier.weight(1f), contentAlignment = Alignment.CenterStart) {
                    IconButton(onClick = { gallery = true }, modifier = Modifier.size(56.dp).clip(CircleShape).background(Color.White.copy(alpha = 0.18f))) {
                        Icon(Icons.Filled.PhotoLibrary, "Aus Galerie wählen", tint = Color.White)
                    }
                }
                Box(Modifier.size(78.dp).clip(CircleShape).border(4.dp, Color.White, CircleShape).padding(8.dp).clip(CircleShape)
                    .background(if (busy) Color.Gray else Color.White).clickable(enabled = granted) { shoot() }
                    .semantics { contentDescription = "Auslösen" })
                Box(Modifier.weight(1f), contentAlignment = Alignment.CenterEnd) {
                    FilledIconButton(onClick = { pendingShots = pendingShots.closeRequested() }, enabled = !pendingShots.closing, modifier = Modifier.size(56.dp),
                        colors = IconButtonDefaults.filledIconButtonColors(containerColor = MaterialTheme.colorScheme.primary)) {
                        if (pendingShots.closing) CircularProgressIndicator(Modifier.size(22.dp), color = MaterialTheme.colorScheme.onPrimary, strokeWidth = 2.dp)
                        else Icon(if (scan) Icons.Filled.PictureAsPdf else Icons.Filled.Check, if (scan) "Als PDF ablegen" else "Fertig")
                    }
                }
            }
            Text("Auslösen auch mit der Lautstärketaste", color = Color.White.copy(alpha = 0.7f), style = MaterialTheme.typography.bodySmall,
                textAlign = TextAlign.Center, modifier = Modifier.fillMaxWidth().padding(top = 8.dp))
        }
    }
    if (gallery) GalleryImport(bauakteId) { gallery = false }
    if (naming) {
        CategorySheet("Scan ${Dates.date(LocalDate.now(Dates.zone))}", onDismiss = { naming = false }) { caption, category ->
            scope.launch {
                val pdf = withContext(Dispatchers.Default) { Images.pdf(pages.toList()) }
                session.service.addFile(bauakteId, "document", pdf, "${caption.ifBlank { "Scan" }}.pdf", "application/pdf", caption, category)
                model.message("PDF mit ${pages.size} Seiten gespeichert. Es wird übertragen, sobald eine Verbindung besteht.")
                naming = false
                nav.back()
            }
        }
    }
}

/** Beschriftung und Art eines Dokuments wählen. */
@OptIn(ExperimentalMaterial3Api::class, ExperimentalLayoutApi::class)
@Composable
fun CategorySheet(initialCaption: String, onDismiss: () -> Unit, onSave: (String, DocumentCategory) -> Unit) {
    var caption by remember { mutableStateOf(initialCaption) }
    var category by remember { mutableStateOf(DocumentCategory.Other) }
    ModalBottomSheet(onDismissRequest = onDismiss, containerColor = tones.canvas) {
        Column(Modifier.padding(horizontal = 20.dp).padding(bottom = 28.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Text("Dokument ablegen", style = MaterialTheme.typography.headlineSmall)
            androidx.compose.material3.OutlinedTextField(caption, { caption = it }, label = { Text("Bezeichnung") }, singleLine = true, modifier = Modifier.fillMaxWidth())
            Text("Art", style = MaterialTheme.typography.titleSmall)
            FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                DocumentCategory.entries.forEach { c ->
                    androidx.compose.material3.FilterChip(selected = category == c, onClick = { category = c }, label = { Text(c.title) })
                }
            }
            Button(onClick = { onSave(caption.trim(), category) }, modifier = Modifier.fillMaxWidth().height(52.dp)) { Text("Speichern") }
        }
    }
}
