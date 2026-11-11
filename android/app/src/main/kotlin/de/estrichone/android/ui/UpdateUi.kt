package de.estrichone.android.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBars
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.CloudUpload
import androidx.compose.material.icons.filled.ErrorOutline
import androidx.compose.material.icons.filled.NewReleases
import androidx.compose.material.icons.filled.SystemUpdate
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import de.estrichone.android.AppModel
import de.estrichone.android.update.AppUpdater
import de.estrichone.core.AppReleaseNote
import de.estrichone.core.UpdateSchedule
import de.estrichone.core.UpdateState
import de.estrichone.core.UpdateStatus
import de.estrichone.core.blocksApp
import de.estrichone.core.check
import de.estrichone.core.isBusy

// App-Updates (docs/app-updates.md): Hinweis oben in der App, Sperrbildschirm unter der Mindestversion, Ablauf
// (Abgleich, Download, Installation) als Dialog und „Neu in dieser Version“ nach dem Update.

private fun megabytes(bytes: Long) = "%.1f MB".format(java.util.Locale.GERMANY, bytes / 1_000_000.0)

/** Über allen Bildschirmen: Sperre, Ablauf des Updates und Neuerungen. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun UpdateOverlay(model: AppModel) {
    val updater = model.updater
    val state by updater.state.collectAsState()
    val notes by updater.whatsNew.collectAsState()
    if (state.blocksApp) {
        UpdateRequiredScreen(updater, state, pendingChanges = model.pendingChangeCount())
        return
    }
    if (state.showsFlow) {
        AlertDialog(
            onDismissRequest = { if (state.isDismissable) updater.dismiss() },
            icon = { Icon(Icons.Filled.SystemUpdate, null) },
            title = { Text(state.flowTitle) },
            text = { UpdateFlowContent(updater, state) },
            confirmButton = { UpdateFlowPrimary(updater, state) },
            dismissButton = { if (state.isDismissable) TextButton(onClick = updater::dismiss) { Text("Schließen") } },
        )
    }
    if (notes.isNotEmpty()) {
        ModalBottomSheet(onDismissRequest = updater::whatsNewShown, sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true),
            containerColor = tones.canvas) {
            WhatsNewContent(notes, onDone = updater::whatsNewShown)
        }
    }
}

private val UpdateState.showsFlow: Boolean
    get() = this is UpdateState.Syncing || this is UpdateState.UnsyncedChanges || this is UpdateState.Downloading || this is UpdateState.Verifying ||
        this is UpdateState.NeedsInstallPermission || this is UpdateState.ReadyToInstall || this is UpdateState.Installing ||
        this is UpdateState.SignatureMismatch || (this is UpdateState.Failed && check != null)

private val UpdateState.isDismissable: Boolean
    get() = this !is UpdateState.Syncing && this !is UpdateState.Verifying && this !is UpdateState.Installing

private val UpdateState.flowTitle: String
    get() = when (this) {
        is UpdateState.Syncing -> "Änderungen übertragen"
        is UpdateState.UnsyncedChanges -> "Nicht alles übertragen"
        is UpdateState.Downloading, is UpdateState.Verifying -> "Update wird geladen"
        is UpdateState.NeedsInstallPermission -> "Installation erlauben"
        is UpdateState.ReadyToInstall, is UpdateState.Installing -> "Update installieren"
        is UpdateState.SignatureMismatch -> "Update nicht möglich"
        is UpdateState.Failed -> "Update unterbrochen"
        else -> "App-Update"
    }

/** Text und Fortschritt je Schritt (auch auf dem Sperrbildschirm). */
@Composable
fun UpdateFlowContent(updater: AppUpdater, state: UpdateState) {
    val version = state.check?.latestVersion ?: ""
    Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
        when (state) {
            is UpdateState.Syncing -> {
                Text("Vor dem Update werden ${state.pending} offene Änderungen an den Server übertragen.")
                LinearProgressIndicator(Modifier.fillMaxWidth())
            }
            is UpdateState.UnsyncedChanges -> Text("${state.pending} Änderungen konnten gerade nicht übertragen werden (keine Verbindung oder Server lehnt ab). " +
                "Sie bleiben beim Update auf dem Gerät gespeichert und werden danach automatisch gesendet.")
            is UpdateState.Downloading -> {
                Text("Version $version wird vom Server geladen. Bricht die Verbindung ab, geht es an derselben Stelle weiter.")
                val fraction = state.fraction
                if (fraction != null) LinearProgressIndicator(progress = { fraction }, modifier = Modifier.fillMaxWidth()) else LinearProgressIndicator(Modifier.fillMaxWidth())
                Secondary(state.total?.let { "${megabytes(state.received)} von ${megabytes(it)}" } ?: megabytes(state.received))
            }
            is UpdateState.Verifying -> {
                Text("Prüfsumme wird kontrolliert …")
                LinearProgressIndicator(Modifier.fillMaxWidth())
            }
            is UpdateState.NeedsInstallPermission -> Text("Android fragt einmalig, ob EstrichONE Updates installieren darf. Bitte unter „Einstellungen“ " +
                "„Aus dieser Quelle zulassen“ einschalten und zurückkehren – die Installation startet dann von selbst.")
            is UpdateState.ReadyToInstall -> Text("Version $version ist geladen und geprüft. Ihre Daten und Anmeldung bleiben erhalten.")
            is UpdateState.Installing -> {
                Text("Android installiert Version $version. Die App startet danach neu.")
                LinearProgressIndicator(Modifier.fillMaxWidth())
            }
            is UpdateState.SignatureMismatch -> {
                Text("Das Update ist mit einem anderen Schlüssel signiert als die installierte App (z. B. Testversion statt Release). " +
                    "Android lässt ein solches Update nicht zu.")
                Text("So geht es weiter: Unter Mehr › Synchronisation prüfen, dass alles übertragen ist, die App deinstallieren und " +
                    "Version $version neu installieren. Danach neu anmelden. Bitte die IT informieren.", style = MaterialTheme.typography.bodyMedium)
            }
            is UpdateState.Failed -> Text(state.message)
            else -> Unit
        }
    }
}

@Composable
private fun UpdateFlowPrimary(updater: AppUpdater, state: UpdateState) {
    when (state) {
        is UpdateState.UnsyncedChanges -> Button(onClick = updater::continueWithoutSync) { Text("Trotzdem aktualisieren") }
        is UpdateState.NeedsInstallPermission -> Button(onClick = updater::openInstallPermission) { Text("Einstellungen") }
        is UpdateState.ReadyToInstall -> Button(onClick = updater::install) { Text("Installieren") }
        is UpdateState.Failed -> if (state.retry) Button(onClick = updater::start) { Text("Erneut versuchen") }
        else -> Unit
    }
}

/** Hinweis oben in der App: neuere Version verfügbar. */
@Composable
fun UpdateBanner(model: AppModel) {
    val updater = model.updater
    val state by updater.state.collectAsState()
    val snoozed by updater.snoozed.collectAsState()
    if (!updater.showsBanner(state, snoozed)) return
    val check = state.check ?: return
    Row(Modifier.fillMaxWidth().background(tones.infoSoft).windowInsetsPadding(WindowInsets.statusBars).padding(start = 16.dp, end = 4.dp, top = 4.dp, bottom = 4.dp),
        verticalAlignment = Alignment.CenterVertically) {
        Icon(Icons.Filled.SystemUpdate, null, tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(20.dp))
        Spacer(Modifier.width(10.dp))
        Column(Modifier.weight(1f)) {
            Text("Version ${check.latestVersion} verfügbar", style = MaterialTheme.typography.labelLarge)
            check.message?.let { Text(it, style = MaterialTheme.typography.bodySmall, color = tones.textSecondary, maxLines = 2) }
        }
        TextButton(onClick = updater::snooze) { Text("Später") }
        TextButton(onClick = updater::start) { Text("Aktualisieren") }
    }
}

val AppModel.hasUpdateBanner: Boolean
    @Composable get() {
        val state by updater.state.collectAsState()
        val snoozed by updater.snoozed.collectAsState()
        return updater.showsBanner(state, snoozed)
    }

/** Sperrbildschirm: App unter der Mindestversion. Daten bleiben, Arbeiten erst nach dem Update. */
@Composable
fun UpdateRequiredScreen(updater: AppUpdater, state: UpdateState, pendingChanges: Int) {
    val check = state.check
    Box(Modifier.fillMaxSize().background(Brush.verticalGradient(listOf(Navy, NavyLight)))) {
        Column(Modifier.align(Alignment.TopCenter).widthIn(max = 560.dp).fillMaxSize().windowInsetsPadding(WindowInsets.safeDrawing)
            .verticalScroll(rememberScrollState()).padding(28.dp), horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(14.dp)) {
            Spacer(Modifier.height(32.dp))
            BrandMark(88.dp, onDark = true)
            Text("Update erforderlich", color = Color.White, style = MaterialTheme.typography.headlineMedium, textAlign = TextAlign.Center)
            Text(
                buildString {
                    append("Diese App-Version (${updater.installedVersion}) wird vom Server nicht mehr unterstützt.")
                    val target = check?.latestVersion ?: check?.minimumVersion?.takeIf { it.isNotBlank() }
                    if (target != null) append(" Bitte auf Version $target aktualisieren.")
                },
                color = Ice, style = MaterialTheme.typography.bodyLarge, textAlign = TextAlign.Center,
            )
            check?.message?.let { message ->
                Text(message, color = Color.White, style = MaterialTheme.typography.bodyMedium, textAlign = TextAlign.Center,
                    modifier = Modifier.clip(RoundedCornerShape(12.dp)).background(Color.White.copy(alpha = 0.1f)).padding(12.dp))
            }
            WhiteCard {
                if (pendingChanges > 0) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Icon(Icons.Filled.CloudUpload, null, tint = tones.warning, modifier = Modifier.size(20.dp))
                        Spacer(Modifier.width(8.dp))
                        Text("$pendingChanges Änderungen warten auf Übertragung. Sie bleiben gespeichert und werden nach dem Update gesendet.",
                            style = MaterialTheme.typography.bodyMedium)
                    }
                }
                when (state) {
                    is UpdateState.Available, is UpdateState.UpToDate -> {
                        val kind = check?.download?.kind
                        Button(onClick = updater::start, enabled = kind == "package" || kind == "link" || kind == "store",
                            modifier = Modifier.fillMaxWidth().height(52.dp)) { Text(if (kind == "package") "Jetzt aktualisieren" else "Update öffnen") }
                        if (kind == null || kind == "none") Secondary("Der Server bietet noch keinen Download an. Bitte an die IT wenden.")
                        check?.download?.size?.takeIf { kind == "package" }?.let { Secondary("Download ${megabytes(it)} vom eigenen Server.") }
                    }
                    else -> {
                        UpdateFlowContent(updater, state)
                        UpdateFlowPrimary(updater, state)
                    }
                }
                // Während Abgleich, Download und Installation nicht unterbrechen.
                if (!state.isBusy) OutlinedButton(onClick = { updater.check(UpdateSchedule.Trigger.Manual) }, modifier = Modifier.fillMaxWidth()) { Text("Erneut prüfen") }
            }
            check?.releaseNotes?.takeIf { it.isNotEmpty() }?.let { notes ->
                WhiteCard { ReleaseNotesList(notes, maxItems = 6) }
            }
        }
    }
}

@Composable
private fun WhiteCard(content: @Composable ColumnScope.() -> Unit) {
    Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(20.dp)).background(tones.card).padding(18.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp), content = content)
}

@Composable
fun ReleaseNotesList(notes: List<AppReleaseNote>, maxItems: Int = Int.MAX_VALUE) {
    var shown = 0
    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
        notes.forEach { note ->
            if (shown >= maxItems) return@forEach
            Text("Version ${note.version}", style = MaterialTheme.typography.titleMedium)
            note.sections.forEach { section ->
                if (shown >= maxItems) return@forEach
                Text(section.title, style = MaterialTheme.typography.labelLarge, color = tones.textSecondary, modifier = Modifier.padding(top = 4.dp))
                section.items.forEach { item ->
                    if (shown < maxItems) {
                        Row { Text("•", modifier = Modifier.width(16.dp)); Text(item, style = MaterialTheme.typography.bodyMedium) }
                        shown++
                    }
                }
            }
        }
    }
}

/** „Neu in dieser Version“ nach einem Update. */
@Composable
fun WhatsNewContent(notes: List<AppReleaseNote>, onDone: () -> Unit) {
    Column(Modifier.fillMaxWidth().verticalScroll(rememberScrollState()).padding(horizontal = 24.dp, vertical = 8.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            IconTile(Icons.Filled.NewReleases)
            Spacer(Modifier.width(12.dp))
            Column {
                Text("Neu in dieser Version", style = SectionTitle)
                Secondary("EstrichONE ${notes.first().version}")
            }
        }
        SectionCard(padding = androidx.compose.foundation.layout.PaddingValues(16.dp)) { ReleaseNotesList(notes) }
        Button(onClick = onDone, modifier = Modifier.fillMaxWidth().heightIn(min = 52.dp)) { Text("Weiter") }
        Spacer(Modifier.height(16.dp))
    }
}

/** Mehr › Info: Stand und „Nach Updates suchen“. */
@Composable
fun UpdateSection(model: AppModel) {
    val updater = model.updater
    val state by updater.state.collectAsState()
    val check = state.check
    val text = when {
        state is UpdateState.Checking -> "Wird geprüft …"
        check?.status == UpdateStatus.Recommended || check?.status == UpdateStatus.Required -> "Version ${check?.latestVersion} verfügbar"
        check?.status == UpdateStatus.NewerThanServer -> "Neuer als der Server (${check?.serverVersion})"
        check != null -> "Aktuell, Version ${updater.installedVersion}"
        state is UpdateState.Failed -> "Prüfung nicht möglich"
        else -> "Noch nicht geprüft"
    }
    SectionCard(Modifier.padding(top = 16.dp)) {
        RowItem("App-Update", subtitle = text, icon = Icons.Filled.SystemUpdate, trailing = {
            if (state is UpdateState.Checking) CircularProgressIndicator(Modifier.size(22.dp), strokeWidth = 2.dp)
        })
        RowDivider(16.dp)
        if (check?.status == UpdateStatus.Recommended || check?.status == UpdateStatus.Required) {
            RowItem("Jetzt aktualisieren", titleColor = MaterialTheme.colorScheme.primary, onClick = updater::start, showChevron = true)
        } else {
            RowItem("Nach Updates suchen", titleColor = MaterialTheme.colorScheme.primary, onClick = { updater.check(UpdateSchedule.Trigger.Manual) })
        }
    }
    if (check?.status == UpdateStatus.NewerThanServer) {
        Banner("Diese App ist neuer als der Server. Sie funktioniert weiter; die IT sollte den Server aktualisieren " +
            "(sudo /opt/estrichone/update.sh).", Icons.Filled.ErrorOutline, tones.info, tones.infoSoft, Modifier.padding(top = 12.dp))
    }
    check?.takeIf { it.status == UpdateStatus.Recommended || it.status == UpdateStatus.Required }?.releaseNotes?.takeIf { it.isNotEmpty() }?.let { notes ->
        SectionCard(Modifier.padding(top = 12.dp), padding = androidx.compose.foundation.layout.PaddingValues(16.dp)) {
            Text("Neu in Version ${notes.first().version}", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
            Spacer(Modifier.height(6.dp))
            ReleaseNotesList(notes, maxItems = 8)
        }
    }
}
