package de.estrichone.android.ui

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Cloud
import androidx.compose.material.icons.filled.CloudDone
import androidx.compose.material.icons.filled.CloudOff
import androidx.compose.material.icons.filled.CloudSync
import androidx.compose.material.icons.filled.ErrorOutline
import androidx.compose.material.icons.outlined.Forum
import androidx.compose.material.icons.outlined.Notifications
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.BadgedBox
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.LargeTopAppBar
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.material3.rememberTopAppBarState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.input.nestedscroll.nestedScroll
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.material.icons.filled.Build
import androidx.compose.material3.HorizontalDivider
import androidx.compose.ui.unit.dp

/**
 * Seitengerüst: Startseiten mit großem Titel, der beim Scrollen einklappt (Material 3), Unterseiten mit Zurück-Pfeil.
 * Im Demomodus steht links eine Kennzeichnung mit „Demo beenden“.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ScreenScaffold(
    title: String,
    root: Boolean = false,
    showBack: Boolean = !root,
    actions: @Composable RowScope.() -> Unit = {},
    floating: @Composable () -> Unit = {},
    bottomBar: @Composable () -> Unit = {},
    content: @Composable (PaddingValues) -> Unit,
) {
    val nav = LocalNav.current
    val behavior = if (root) TopAppBarDefaults.exitUntilCollapsedScrollBehavior(rememberTopAppBarState())
    else TopAppBarDefaults.pinnedScrollBehavior(rememberTopAppBarState())
    val colors = TopAppBarDefaults.topAppBarColors(containerColor = tones.canvas, scrolledContainerColor = MaterialTheme.colorScheme.surfaceContainer)
    Scaffold(
        modifier = Modifier.nestedScroll(behavior.nestedScrollConnection),
        containerColor = tones.canvas,
        topBar = {
            val navigation: @Composable () -> Unit = {
                if (showBack) {
                    IconButton(onClick = { nav.back() }) { Icon(Icons.AutoMirrored.Filled.ArrowBack, "Zurück") }
                } else if (root) {
                    DemoMark()
                }
            }
            if (root) {
                LargeTopAppBar(title = { Text(title, maxLines = 1, overflow = TextOverflow.Ellipsis, fontWeight = FontWeight.Bold) },
                    navigationIcon = navigation, actions = actions, colors = colors, scrollBehavior = behavior)
            } else {
                TopAppBar(title = { Text(title, maxLines = 1, overflow = TextOverflow.Ellipsis) }, navigationIcon = navigation, actions = actions,
                    colors = colors, scrollBehavior = behavior)
            }
        },
        floatingActionButton = floating,
        bottomBar = bottomBar,
        content = content,
    )
}

/** „Demo“ oben links mit Menü (Demo beenden, Störungen vorführen). */
@Composable
fun DemoMark() {
    val session = LocalSession.current
    val model = LocalModel.current
    val demo = session.demo ?: return
    var open by remember { mutableStateOf(false) }
    var tick by remember { mutableStateOf(0) }
    Box {
        Text("Demo", color = MaterialTheme.colorScheme.primary, style = MaterialTheme.typography.labelLarge,
            modifier = Modifier.padding(start = 12.dp).clip(RoundedCornerShape(50)).background(tones.infoSoft).clickable { open = true }
                .padding(horizontal = 14.dp, vertical = 7.dp))
        DropdownMenu(expanded = open, onDismissRequest = { open = false }) {
            Text("Demomodus – Beispieldaten, es wird nichts gesendet.", style = MaterialTheme.typography.bodySmall, color = tones.textSecondary,
                modifier = Modifier.padding(horizontal = 16.dp, vertical = 8.dp))
            HorizontalDivider()
            fun refresh() { tick++; session.synchronizeNow() }
            DropdownMenuItem(text = { Text((if (demo.backupDown) "✓ " else "") + "Backup-Server ausgefallen") }, onClick = { demo.backupDown = !demo.backupDown; refresh() })
            DropdownMenuItem(text = { Text((if (demo.archiveDown) "✓ " else "") + "Archiv-Server ausgefallen") }, onClick = { demo.archiveDown = !demo.archiveDown; refresh() })
            DropdownMenuItem(text = { Text(if (demo.maintenance) "Wartung beenden" else "Wartung starten") }, leadingIcon = { Icon(Icons.Filled.Build, null) },
                onClick = { demo.maintenance = !demo.maintenance; refresh() })
            HorizontalDivider()
            DropdownMenuItem(text = { Text("Demo beenden", color = tones.danger) }, onClick = { open = false; model.endDemo() })
        }
    }
}

/** Abgleich-Symbol mit Zustand; öffnet die Synchronisationsseite. */
@Composable
fun SyncAction() {
    val session = LocalSession.current
    val nav = LocalNav.current
    val status by session.status.collectAsState()
    val icon = when {
        status.running -> Icons.Filled.CloudSync
        status.summary.hasProblems -> Icons.Filled.ErrorOutline
        status.offline -> Icons.Filled.CloudOff
        !status.summary.isSettled -> Icons.Filled.Cloud
        else -> Icons.Filled.CloudDone
    }
    val tint = if (status.summary.hasProblems) tones.danger else MaterialTheme.colorScheme.onSurface
    IconButton(onClick = { nav.push(Route.Sync) }) { Icon(icon, "Synchronisation: ${status.shortText}", tint = tint) }
}

/** Chat- und Postfach-Symbol mit Zahl (wie oben rechts auf iOS). */
@Composable
fun InboxActions() {
    val session = LocalSession.current
    val nav = LocalNav.current
    val chat by session.chat.collectAsState()
    val unread by session.unreadNotifications.collectAsState()
    if (chat?.isAvailable == true) {
        IconButton(onClick = { nav.push(Route.ChatList) }) {
            BadgedBox(badge = { CountBadge(chat?.unreadCount ?: 0) }) { Icon(Icons.Outlined.Forum, "Chat") }
        }
    }
    IconButton(onClick = { nav.push(Route.Inbox) }) {
        BadgedBox(badge = { CountBadge(unread) }) { Icon(Icons.Outlined.Notifications, "Benachrichtigungen") }
    }
}

/** Bestätigung vor Aktionen mit Datenverlust. */
@Composable
fun ConfirmDialog(title: String, text: String, confirm: String, onConfirm: () -> Unit, onDismiss: () -> Unit, destructive: Boolean = true) {
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(title) },
        text = { Text(text) },
        confirmButton = { TextButton(onClick = { onDismiss(); onConfirm() }) { Text(confirm, color = if (destructive) tones.danger else MaterialTheme.colorScheme.primary) } },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Abbrechen") } },
    )
}

/** Inhalt der Seite mit Standardabständen. */
fun PaddingValues.content(extraBottom: Int = 96): PaddingValues = PaddingValues(
    start = 16.dp, end = 16.dp, top = calculateTopPadding() + 4.dp, bottom = calculateBottomPadding() + extraBottom.dp,
)

@Composable
fun Fill(modifier: Modifier = Modifier, content: @Composable () -> Unit) = Box(modifier.fillMaxSize()) { content() }
