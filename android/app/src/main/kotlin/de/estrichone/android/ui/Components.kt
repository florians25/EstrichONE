package de.estrichone.android.ui

import android.content.Intent
import android.speech.RecognizerIntent
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.layout.wrapContentWidth
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.KeyboardArrowRight
import androidx.compose.material.icons.filled.CloudOff
import androidx.compose.material.icons.filled.CloudUpload
import androidx.compose.material.icons.filled.ErrorOutline
import androidx.compose.material.icons.filled.Mic
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.LocalContentColor
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.MutableState
import androidx.compose.runtime.State
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import de.estrichone.android.Session
import de.estrichone.core.ApiException
import de.estrichone.core.BauakteStatus
import de.estrichone.core.ChatLimits
import de.estrichone.core.SyncState
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

// Bausteine der Oberfläche (wie ios/EstrichONE/Design): Karten auf grauem Grund, Zeilen mit Symbolkachel, Statusfarben.

/** Regelmäßiges Nachladen (Chat, Countdown); in Screenshot-Tests aus, damit die Oberfläche zur Ruhe kommt. */
object Live {
    @Volatile
    var polling = true
}

object Space {
    val xs = 4.dp
    val s = 8.dp
    val m = 12.dp
    val l = 16.dp
    val xl = 24.dp
    val section = 28.dp
}

/** Weiße (dunkel: graue) Karte mit großen Ecken. */
@Composable
fun SectionCard(modifier: Modifier = Modifier, padding: PaddingValues = PaddingValues(0.dp), content: @Composable ColumnScope.() -> Unit) {
    Surface(modifier = modifier.fillMaxWidth(), shape = RoundedCornerShape(20.dp), color = tones.card, tonalElevation = 0.dp) {
        Column(Modifier.padding(padding), content = content)
    }
}

/** Überschrift eines Abschnitts mit optionaler Aktion rechts („Alle (5)“). */
@Composable
fun SectionHeader(title: String, modifier: Modifier = Modifier, action: String? = null, onAction: (() -> Unit)? = null) {
    Row(modifier.fillMaxWidth().padding(top = Space.xl, bottom = Space.s, start = 4.dp, end = 4.dp), verticalAlignment = Alignment.CenterVertically) {
        Text(title, style = SectionTitle, modifier = Modifier.weight(1f).semantics { this.contentDescription = title })
        if (action != null && onAction != null) {
            Row(Modifier.clip(RoundedCornerShape(8.dp)).clickable(onClick = onAction).padding(horizontal = 6.dp, vertical = 4.dp),
                verticalAlignment = Alignment.CenterVertically) {
                Text(action, color = MaterialTheme.colorScheme.primary, style = MaterialTheme.typography.labelLarge)
                Icon(Icons.AutoMirrored.Filled.KeyboardArrowRight, null, tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(20.dp))
            }
        }
    }
}

/** Kleine Gruppenüberschrift in Listen („Heute im Einsatz“). */
@Composable
fun GroupLabel(text: String, modifier: Modifier = Modifier) {
    Text(text, style = MaterialTheme.typography.titleSmall, color = tones.textSecondary,
        modifier = modifier.padding(start = 20.dp, top = Space.l, bottom = Space.s))
}

/** Symbol in einer abgerundeten Kachel (Zeilen, Karten). */
@Composable
fun IconTile(icon: ImageVector, tint: Color = MaterialTheme.colorScheme.primary, background: Color = tones.infoSoft, size: Dp = 36.dp) {
    Box(Modifier.size(size).clip(RoundedCornerShape(10.dp)).background(background), contentAlignment = Alignment.Center) {
        Icon(icon, null, tint = tint, modifier = Modifier.size(size * 0.58f))
    }
}

/** Zeile in einer Karte: Symbol, Titel, Untertitel, Wert bzw. Pfeil. */
@Composable
fun RowItem(
    title: String,
    modifier: Modifier = Modifier,
    icon: ImageVector? = null,
    iconTint: Color = MaterialTheme.colorScheme.primary,
    iconBackground: Color = tones.infoSoft,
    subtitle: String? = null,
    value: String? = null,
    showChevron: Boolean = false,
    titleColor: Color = Color.Unspecified,
    onClick: (() -> Unit)? = null,
    trailing: (@Composable RowScope.() -> Unit)? = null,
) {
    Row(
        modifier.fillMaxWidth().heightIn(min = 56.dp)
            .then(if (onClick != null) Modifier.clickable(role = Role.Button, onClick = onClick) else Modifier)
            .padding(horizontal = Space.l, vertical = 10.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        if (icon != null) {
            IconTile(icon, iconTint, iconBackground)
            Spacer(Modifier.width(Space.m))
        }
        Column(Modifier.weight(1f)) {
            Text(title, style = MaterialTheme.typography.bodyLarge, color = titleColor)
            if (!subtitle.isNullOrBlank()) Text(subtitle, style = MaterialTheme.typography.bodyMedium, color = tones.textSecondary)
        }
        if (value != null) Text(value, style = MaterialTheme.typography.bodyMedium, color = tones.textSecondary, modifier = Modifier.padding(start = Space.s))
        trailing?.invoke(this)
        if (showChevron) Icon(Icons.AutoMirrored.Filled.KeyboardArrowRight, null, tint = tones.textTertiary, modifier = Modifier.padding(start = 4.dp))
    }
}

@Composable
fun RowDivider(inset: Dp = 64.dp) {
    HorizontalDivider(Modifier.padding(start = inset), color = tones.separator)
}

/** Runde Schaltfläche mit Symbol (Anrufen, Route). */
@Composable
fun CircleAction(icon: ImageVector, label: String, onClick: () -> Unit, tint: Color = MaterialTheme.colorScheme.primary, background: Color = tones.infoSoft) {
    Box(
        Modifier.size(48.dp).clip(CircleShape).background(background).clickable(role = Role.Button, onClick = onClick)
            .semantics { contentDescription = label },
        contentAlignment = Alignment.Center,
    ) { Icon(icon, null, tint = tint, modifier = Modifier.size(22.dp)) }
}

/** Farbe und Fläche je Bauakten-Status. */
@Composable
fun statusColor(status: BauakteStatus): Color = when (status) {
    BauakteStatus.InProgress -> tones.info
    BauakteStatus.Drying -> tones.warning
    BauakteStatus.Planned -> tones.textSecondary
    BauakteStatus.Inquiry -> Color(0xFF7B4FD0).let { if (tones.dark) Color(0xFFB79BFF) else it }
    BauakteStatus.Completed -> tones.success
    BauakteStatus.Cancelled -> tones.danger
}

/** „● In Ausführung“. */
@Composable
fun StatusLabel(status: BauakteStatus, short: Boolean = true, modifier: Modifier = Modifier) {
    val color = statusColor(status)
    Row(modifier, verticalAlignment = Alignment.CenterVertically) {
        Box(Modifier.size(8.dp).clip(CircleShape).background(color))
        Spacer(Modifier.width(6.dp))
        Text(if (short && status == BauakteStatus.Drying) "Trocknung" else status.label, style = MaterialTheme.typography.labelLarge, color = color)
    }
}

/** Kleiner Hinweis mit Hintergrund („Mangel“, „in 2 Tagen“). */
@Composable
fun Pill(text: String, color: Color, background: Color, icon: ImageVector? = null, modifier: Modifier = Modifier) {
    Row(modifier.clip(RoundedCornerShape(8.dp)).background(background).padding(horizontal = 8.dp, vertical = 3.dp),
        verticalAlignment = Alignment.CenterVertically) {
        if (icon != null) {
            Icon(icon, null, tint = color, modifier = Modifier.size(14.dp))
            Spacer(Modifier.width(4.dp))
        }
        Text(text, color = color, style = MaterialTheme.typography.labelMedium, fontWeight = FontWeight.SemiBold, maxLines = 1)
    }
}

/** Kennzahl in der Kopfzeile der Bauakte („142,5 m²“). */
@Composable
fun Tag(text: String) {
    Text(text, style = MaterialTheme.typography.labelMedium, fontWeight = FontWeight.SemiBold, color = tones.textSecondary,
        modifier = Modifier.clip(RoundedCornerShape(50)).background(tones.fillStrong).padding(horizontal = 10.dp, vertical = 5.dp))
}

/** Zahl als Abzeichen. */
@Composable
fun CountBadge(count: Int, modifier: Modifier = Modifier) {
    if (count <= 0) return
    Box(modifier.heightIn(min = 18.dp).widthIn(min = 18.dp).clip(CircleShape).background(tones.badge).padding(horizontal = 5.dp),
        contentAlignment = Alignment.Center) {
        Text(if (count > 99) "99+" else count.toString(), color = Color.White, style = MaterialTheme.typography.labelSmall, fontWeight = FontWeight.Bold)
    }
}

/** Kennzeichnung eines noch nicht übertragenen bzw. abgelehnten Datensatzes. */
@Composable
fun SyncMark(state: SyncState) {
    when (state) {
        SyncState.Pending -> Icon(Icons.Filled.CloudUpload, "Wird übertragen", tint = tones.textTertiary, modifier = Modifier.size(16.dp))
        SyncState.Problem -> Icon(Icons.Filled.ErrorOutline, "Nicht übertragen", tint = tones.danger, modifier = Modifier.size(16.dp))
        SyncState.Synced -> Unit
    }
}

/** Großer Aktionsknopf (Foto, Tagebuch, Mangel, Akte) wie auf iOS. */
@Composable
fun ActionTile(icon: ImageVector, label: String, primary: Boolean = false, modifier: Modifier = Modifier, onClick: () -> Unit) {
    val bg = if (primary) MaterialTheme.colorScheme.primary else tones.fill
    val fg = if (primary) MaterialTheme.colorScheme.onPrimary else MaterialTheme.colorScheme.onSurface
    Column(
        modifier.heightIn(min = 76.dp).clip(RoundedCornerShape(16.dp)).background(bg).clickable(role = Role.Button, onClick = onClick)
            .padding(vertical = 12.dp, horizontal = 4.dp),
        horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.Center,
    ) {
        Icon(icon, null, tint = if (primary) fg else MaterialTheme.colorScheme.primary, modifier = Modifier.size(26.dp))
        Spacer(Modifier.height(6.dp))
        Text(label, color = fg, style = MaterialTheme.typography.labelMedium, fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis,
            textAlign = TextAlign.Center)
    }
}

/** Leerer Zustand mit Symbol und Erklärung. */
@Composable
fun EmptyState(icon: ImageVector, title: String, text: String? = null, modifier: Modifier = Modifier, action: (@Composable () -> Unit)? = null) {
    Column(modifier.fillMaxWidth().padding(horizontal = 32.dp, vertical = 40.dp), horizontalAlignment = Alignment.CenterHorizontally) {
        Box(Modifier.size(64.dp).clip(CircleShape).background(tones.fill), contentAlignment = Alignment.Center) {
            Icon(icon, null, tint = tones.textTertiary, modifier = Modifier.size(30.dp))
        }
        Spacer(Modifier.height(Space.m))
        Text(title, style = MaterialTheme.typography.titleMedium, textAlign = TextAlign.Center)
        if (text != null) {
            Spacer(Modifier.height(4.dp))
            Text(text, style = MaterialTheme.typography.bodyMedium, color = tones.textSecondary, textAlign = TextAlign.Center)
        }
        if (action != null) {
            Spacer(Modifier.height(Space.l))
            action()
        }
    }
}

/** Runder Kreis mit Initialen. */
@Composable
fun Avatar(initials: String, size: Dp = 44.dp, background: Color = tones.infoSoft, color: Color = MaterialTheme.colorScheme.primary) {
    Box(Modifier.size(size).clip(CircleShape).background(background), contentAlignment = Alignment.Center) {
        Text(initials, color = color, fontWeight = FontWeight.SemiBold, style = if (size > 48.dp) MaterialTheme.typography.titleLarge else MaterialTheme.typography.titleSmall)
    }
}

/** Hinweisleiste (Demo, Offline, Wartung). */
@Composable
fun Banner(text: String, icon: ImageVector, color: Color, background: Color, modifier: Modifier = Modifier, action: String? = null, onAction: (() -> Unit)? = null) {
    Row(modifier.fillMaxWidth().clip(RoundedCornerShape(14.dp)).background(background).padding(horizontal = 14.dp, vertical = 10.dp),
        verticalAlignment = Alignment.CenterVertically) {
        Icon(icon, null, tint = color, modifier = Modifier.size(20.dp))
        Spacer(Modifier.width(10.dp))
        Text(text, style = MaterialTheme.typography.bodyMedium, modifier = Modifier.weight(1f))
        if (action != null && onAction != null) {
            Text(action, color = MaterialTheme.colorScheme.primary, style = MaterialTheme.typography.labelLarge,
                modifier = Modifier.clip(RoundedCornerShape(8.dp)).clickable(onClick = onAction).padding(6.dp))
        }
    }
}

@Composable
fun OfflineBanner(session: Session) {
    val status = session.status.collectAsState().value
    status.maintenanceMessage?.let {
        Banner("Wartung: $it Erfasstes bleibt auf dem Gerät.", Icons.Filled.ErrorOutline, tones.warning, tones.warningSoft, Modifier.padding(bottom = Space.s))
        return
    }
    if (status.offline) {
        Banner("Offline – Änderungen werden gespeichert und später übertragen.", Icons.Filled.CloudOff, tones.textSecondary, tones.fill,
            Modifier.padding(bottom = Space.s))
    }
}

/** Mikrofon: Android-Spracheingabe, Ergebnis wird an den Text angehängt. */
@Composable
fun DictationButton(onText: (String) -> Unit) {
    val launcher = rememberLauncherForActivityResult(ActivityResultContracts.StartActivityForResult()) { result ->
        result.data?.getStringArrayListExtra(RecognizerIntent.EXTRA_RESULTS)?.firstOrNull()?.let(onText)
    }
    val context = LocalContext.current
    IconButton(onClick = {
        val intent = Intent(RecognizerIntent.ACTION_RECOGNIZE_SPEECH)
            .putExtra(RecognizerIntent.EXTRA_LANGUAGE_MODEL, RecognizerIntent.LANGUAGE_MODEL_FREE_FORM)
            .putExtra(RecognizerIntent.EXTRA_LANGUAGE, "de-DE")
            .putExtra(RecognizerIntent.EXTRA_PROMPT, "Sprechen Sie jetzt")
        if (intent.resolveActivity(context.packageManager) != null) launcher.launch(intent)
    }) { Icon(Icons.Filled.Mic, "Diktieren") }
}

fun appendDictation(current: String, spoken: String) = ChatLimits.appendDictation(current, spoken)

// Laden mit Fehleranzeige

sealed interface Load<out T> {
    data object Loading : Load<Nothing>
    data class Done<T>(val value: T) : Load<T>
    data class Failed(val message: String) : Load<Nothing>
}

suspend fun <T> attempt(fetch: suspend () -> T): Load<T> = try {
    Load.Done(withContext(Dispatchers.IO) { fetch() })
} catch (e: ApiException) {
    Load.Failed(e.message ?: "Fehler")
}

/** Lädt bei Änderung von [key] neu; Fehler der API werden als Text angezeigt. */
@Composable
fun <T> rememberLoad(key: Any?, fetch: suspend () -> T): MutableState<Load<T>> {
    val state = remember(key) { mutableStateOf<Load<T>>(Load.Loading) }
    LaunchedEffect(key, state.value is Load.Loading) {
        if (state.value is Load.Loading) state.value = attempt(fetch)
    }
    return state
}

/** Ergebnis einer Abfrage auf den lokalen Stand; neu bei jeder Änderung (Revision). */
@Composable
fun <T> Session.local(vararg keys: Any?, query: () -> T): State<T> {
    val revision = store.revision.collectAsState().value
    return produceState(initialValue = remember(*keys) { query() }, revision, *keys) { value = query() }
}

@Composable
fun LoadView(load: Load<*>, content: @Composable () -> Unit) {
    when (load) {
        Load.Loading -> Box(Modifier.fillMaxWidth().padding(40.dp), contentAlignment = Alignment.Center) { androidx.compose.material3.CircularProgressIndicator() }
        is Load.Failed -> EmptyState(Icons.Filled.CloudOff, "Nicht verfügbar", load.message)
        is Load.Done<*> -> content()
    }
}

@Composable
fun Secondary(text: String, modifier: Modifier = Modifier, maxLines: Int = Int.MAX_VALUE) {
    Text(text, style = MaterialTheme.typography.bodyMedium, color = tones.textSecondary, modifier = modifier, maxLines = maxLines, overflow = TextOverflow.Ellipsis)
}

@Composable
fun Tinted(color: Color, content: @Composable () -> Unit) = CompositionLocalProvider(LocalContentColor provides color, content = content)

/** Volle Breite, auf Tablets lesbar begrenzt und mittig. */
fun Modifier.readable(): Modifier = this.fillMaxWidth().wrapContentWidth(Alignment.CenterHorizontally).widthIn(max = 920.dp)

fun Modifier.screen(): Modifier = this.fillMaxSize()
