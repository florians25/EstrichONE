package de.estrichone.android.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.CloudUpload
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import de.estrichone.android.Session
import de.estrichone.core.UpdateMaintenance
import de.estrichone.core.UpdatePresentation
import de.estrichone.core.UpdateScreen
import kotlinx.coroutines.delay
import java.time.Instant

/** Sekundentakt für Countdowns, solange ein Update läuft. */
@Composable
private fun updateScreen(state: UpdateMaintenance): UpdateScreen {
    var now by remember { mutableStateOf(Instant.now()) }
    LaunchedEffect(state.id, state.phase) { while (true) { now = Instant.now(); delay(1_000) } }
    return UpdatePresentation.screen(state, now)
}

/** Leiste oben: „Update auf Version X beginnt in 0:59 – bitte Arbeit speichern“ (wie im Web). */
@Composable
fun UpdateAnnounceBar(session: Session) {
    val state by session.update.collectAsState()
    val current = state ?: return
    val screen = updateScreen(current)
    if (!screen.banner) return
    Row(
        Modifier.fillMaxWidth().background(tones.warningSoft).statusBarsPadding().padding(horizontal = 16.dp, vertical = 10.dp)
            .semantics { contentDescription = screen.bannerText },
        verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        Icon(Icons.Filled.CloudUpload, null, tint = tones.warning)
        Text(screen.bannerText, style = MaterialTheme.typography.bodyMedium.copy(fontWeight = FontWeight.SemiBold, fontFeatureSettings = "tnum"))
    }
}

/** Wartungsbildschirm über allem: Phase, Zielversion, Schritte, Countdown, Fortschritt (wie Web und iOS). */
@Composable
fun UpdateMaintenanceOverlay(session: Session) {
    val state by session.update.collectAsState()
    val offline by session.updateOffline.collectAsState()
    val current = state ?: return
    val screen = updateScreen(current)
    if (!screen.page) return
    Box(Modifier.fillMaxSize().background(tones.canvas).padding(20.dp), contentAlignment = Alignment.Center) {
        Surface(shape = RoundedCornerShape(20.dp), color = tones.card, tonalElevation = 2.dp, shadowElevation = 6.dp,
            modifier = Modifier.widthIn(max = 480.dp).fillMaxWidth()) {
            Column(Modifier.padding(24.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Icon(Icons.Filled.CloudUpload, null, tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(36.dp))
                Text(screen.title, style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.Bold)
                if (screen.version.isNotEmpty()) Text("Zielversion ${screen.version}", color = tones.textSecondary)
                Row(Modifier.fillMaxWidth().padding(top = 12.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    screen.steps.forEach { (label, step) ->
                        Column(Modifier.weight(1f)) {
                            Box(Modifier.fillMaxWidth().height(4.dp).clip(RoundedCornerShape(2.dp)).background(
                                when (step) { "done" -> tones.success; "now" -> MaterialTheme.colorScheme.primary; else -> tones.fillStrong },
                            ))
                            Spacer(Modifier.height(6.dp))
                            Text(label, style = MaterialTheme.typography.labelMedium,
                                fontWeight = if (step == "now") FontWeight.Bold else FontWeight.Normal,
                                color = if (step == "now") MaterialTheme.colorScheme.onSurface else tones.textSecondary)
                        }
                    }
                }
                Text(screen.countdownLabel, color = tones.textSecondary, modifier = Modifier.padding(top = 8.dp))
                if (screen.countdown.isNotEmpty()) {
                    Text(screen.countdown, fontSize = 40.sp, fontWeight = FontWeight.Bold, style = MaterialTheme.typography.displaySmall.copy(fontFeatureSettings = "tnum"))
                }
                LinearProgressIndicator(progress = { screen.progress }, modifier = Modifier.fillMaxWidth().height(6.dp).clip(RoundedCornerShape(3.dp)),
                    color = if (screen.failed) tones.danger else MaterialTheme.colorScheme.primary, trackColor = tones.fill)
                Text(screen.text, style = MaterialTheme.typography.bodyMedium, modifier = Modifier.padding(top = 4.dp))
                Text(
                    if (offline) "Server wird gerade getauscht – die App wartet automatisch." else "Die App prüft alle 5 Sekunden und geht nach der Freigabe zur Anmeldung.",
                    style = MaterialTheme.typography.bodySmall, color = tones.textSecondary,
                )
            }
        }
    }
}
