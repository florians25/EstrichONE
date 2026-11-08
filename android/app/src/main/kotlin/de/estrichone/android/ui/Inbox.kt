package de.estrichone.android.ui

import androidx.compose.animation.animateContentSize
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.background
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.horizontalScroll
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
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.selection.SelectionContainer
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.Assignment
import androidx.compose.material.icons.automirrored.outlined.OpenInNew
import androidx.compose.material.icons.filled.Circle
import androidx.compose.material.icons.filled.PriorityHigh
import androidx.compose.material.icons.outlined.BeachAccess
import androidx.compose.material.icons.outlined.CalendarMonth
import androidx.compose.material.icons.outlined.Campaign
import androidx.compose.material.icons.outlined.ChatBubbleOutline
import androidx.compose.material.icons.outlined.Delete
import androidx.compose.material.icons.outlined.Dns
import androidx.compose.material.icons.outlined.DoneAll
import androidx.compose.material.icons.outlined.Folder
import androidx.compose.material.icons.outlined.Inbox
import androidx.compose.material.icons.outlined.MarkEmailRead
import androidx.compose.material.icons.outlined.MarkEmailUnread
import androidx.compose.material.icons.outlined.MoreVert
import androidx.compose.material.icons.outlined.NotificationsNone
import androidx.compose.material.icons.outlined.Public
import androidx.compose.material.icons.outlined.Schedule
import androidx.compose.material.icons.outlined.SearchOff
import androidx.compose.material.icons.outlined.Settings
import androidx.compose.material.icons.outlined.SupportAgent
import androidx.compose.material.icons.outlined.Tune
import androidx.compose.material.icons.outlined.Verified
import androidx.compose.material.icons.outlined.ViewAgenda
import androidx.compose.material3.Button
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FilterChipDefaults
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.SwipeToDismissBox
import androidx.compose.material3.SwipeToDismissBoxValue
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.pulltorefresh.PullToRefreshBox
import androidx.compose.material3.rememberSwipeToDismissBoxState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import de.estrichone.android.support.Links
import de.estrichone.core.ApiDate
import de.estrichone.core.ApiException
import de.estrichone.core.InboxNotification
import de.estrichone.core.NotificationFeed
import de.estrichone.core.NotificationFilter
import de.estrichone.core.NotificationGrouping
import de.estrichone.core.NotificationKind
import de.estrichone.core.deleteNotification
import de.estrichone.core.markAllNotificationsRead
import de.estrichone.core.markNotificationRead
import de.estrichone.core.markNotificationUnread
import de.estrichone.core.notifications
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.time.Instant

// Postfach (wie NotificationViews.swift): Filter, Suche, Abschnitte nach Zeit oder Art, Wischen, Einzelansicht.

/** Anfangszustand des Postfachs (Screenshots: „ungelesen“, „nach Art“). */
object InboxPreset {
    var filter = NotificationFilter()
    var grouping = NotificationGrouping.Time
}

private const val PAGE_SIZE = 100

/** Symbol der Art bzw. einzelner Kategorien (Freigabe, Frist). */
fun notificationIcon(item: InboxNotification): ImageVector = when (item.category?.lowercase()) {
    "approval" -> Icons.Outlined.Verified
    "deadline" -> Icons.Outlined.Schedule
    else -> item.kind.icon
}

val NotificationKind.icon: ImageVector
    get() = when (this) {
        NotificationKind.Planning -> Icons.Outlined.CalendarMonth
        NotificationKind.Bauakte -> Icons.Outlined.Folder
        NotificationKind.Work -> Icons.AutoMirrored.Outlined.Assignment
        NotificationKind.Chat -> Icons.Outlined.ChatBubbleOutline
        NotificationKind.Ticket -> Icons.Outlined.SupportAgent
        NotificationKind.Office -> Icons.Outlined.Campaign
        NotificationKind.Personal -> Icons.Outlined.BeachAccess
        NotificationKind.System -> Icons.Outlined.Dns
    }

/** Farbe der Art, hell und dunkel (wie die Systemfarben auf iOS). */
val NotificationKind.tint: Color
    @Composable get() {
        val dark = tones.dark
        return when (this) {
            NotificationKind.Planning -> MaterialTheme.colorScheme.primary
            NotificationKind.Bauakte -> if (dark) Color(0xFF5AC8D8) else Color(0xFF0E7C86)
            NotificationKind.Work -> if (dark) Color(0xFFFFA94D) else Color(0xFFB45309)
            NotificationKind.Chat -> if (dark) Color(0xFF5FCF8C) else Color(0xFF1C7A45)
            NotificationKind.Ticket -> if (dark) Color(0xFFC59BFF) else Color(0xFF7A3FC4)
            NotificationKind.Office -> if (dark) Color(0xFF8E9BFF) else Color(0xFF4451C9)
            NotificationKind.Personal -> if (dark) Color(0xFFFF8FB1) else Color(0xFFB8336A)
            NotificationKind.System -> tones.textSecondary
        }
    }

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun InboxScreen() {
    val session = LocalSession.current
    val nav = LocalNav.current
    val scope = rememberCoroutineScope()
    val items = remember { mutableStateListOf<InboxNotification>() }
    var loaded by remember { mutableStateOf(false) }
    var failure by remember { mutableStateOf<String?>(null) }
    var refreshing by remember { mutableStateOf(false) }
    var hasMore by remember { mutableStateOf(false) }
    var loadingMore by remember { mutableStateOf(false) }
    var filter by remember { mutableStateOf(InboxPreset.filter) }
    var grouping by remember { mutableStateOf(InboxPreset.grouping) }
    var menu by remember { mutableStateOf(false) }

    suspend fun reload() {
        try {
            val inbox = withContext(Dispatchers.IO) { session.api.notifications(PAGE_SIZE) }
            items.clear()
            items.addAll(inbox.items)
            hasMore = inbox.items.size >= PAGE_SIZE
            session.setUnreadNotifications(inbox.unreadCount)
            failure = null
        } catch (e: ApiException) {
            failure = e.message ?: "Fehler"
        }
        loaded = true
    }

    fun loadMore() = scope.launch {
        val oldest = items.mapNotNull { it.created }.minOrNull() ?: return@launch
        loadingMore = true
        try {
            val page = withContext(Dispatchers.IO) { session.api.notifications(PAGE_SIZE, oldest) }
            val known = items.map { it.id }.toSet()
            items.addAll(page.items.filter { it.id !in known })
            hasMore = page.items.size >= PAGE_SIZE
        } catch (e: ApiException) {
            nav.snackbar.showSnackbar(e.message ?: "Fehler")
        }
        loadingMore = false
    }

    fun unread() = items.count { !it.isRead }

    fun setRead(item: InboxNotification, read: Boolean) {
        val index = items.indexOfFirst { it.id == item.id }
        if (index < 0 || items[index].isRead == read) return
        items[index] = items[index].copy(readAt = if (read) ApiDate.format(Instant.now()) else null)
        session.setUnreadNotifications(unread())
        scope.launch {
            runCatching { withContext(Dispatchers.IO) { if (read) session.api.markNotificationRead(item.id) else session.api.markNotificationUnread(item.id) } }
        }
    }

    fun delete(item: InboxNotification) {
        val index = items.indexOfFirst { it.id == item.id }
        if (index < 0) return
        items.removeAt(index)
        session.setUnreadNotifications(unread())
        scope.launch {
            val result = runCatching { withContext(Dispatchers.IO) { session.api.deleteNotification(item.id) } }
            if (result.isFailure && (result.exceptionOrNull() as? ApiException)?.status != 404) {
                items.add(minOf(index, items.size), item)
                session.setUnreadNotifications(unread())
                nav.snackbar.showSnackbar("Nicht gelöscht, keine Verbindung")
            } else {
                nav.snackbar.showSnackbar("Benachrichtigung gelöscht")
            }
        }
    }

    fun markAllRead() {
        val now = ApiDate.format(Instant.now())
        items.indices.forEach { if (!items[it].isRead) items[it] = items[it].copy(readAt = now) }
        session.setUnreadNotifications(0)
        scope.launch { runCatching { withContext(Dispatchers.IO) { session.api.markAllNotificationsRead() } } }
    }

    fun open(item: InboxNotification) {
        setRead(item, true)
        val route = item.route
        if (item.hasTarget && route != null) nav.open(route) else nav.push(Route.NotificationDetail(item.copy(readAt = item.readAt ?: ApiDate.format(Instant.now()))))
    }

    LaunchedEffect(Unit) { reload() }

    val visible = NotificationFeed.filter(items, filter)
    val sections = NotificationFeed.sections(visible, grouping)
    val counts = NotificationFeed.unreadCounts(items)
    val unreadTotal = unread()

    ScreenScaffold("Benachrichtigungen", actions = {
        if (unreadTotal > 0) IconButton(onClick = ::markAllRead) { Icon(Icons.Outlined.DoneAll, "Alle als gelesen markieren") }
        IconButton(onClick = { nav.push(Route.NotificationSettings) }) { Icon(Icons.Outlined.Settings, "Mitteilungseinstellungen") }
        Box {
            IconButton(onClick = { menu = true }) { Icon(Icons.Outlined.MoreVert, "Optionen") }
            DropdownMenu(menu, { menu = false }) {
                NotificationGrouping.entries.forEach { option ->
                    DropdownMenuItem(text = { Text(option.title) }, onClick = { grouping = option; menu = false },
                        leadingIcon = { Icon(if (option == NotificationGrouping.Time) Icons.Outlined.Schedule else Icons.Outlined.ViewAgenda, null) },
                        trailingIcon = { if (grouping == option) Icon(Icons.Filled.Circle, null, Modifier.size(8.dp)) })
                }
                HorizontalDivider()
                DropdownMenuItem(text = { Text("Alle als gelesen markieren") }, enabled = unreadTotal > 0, onClick = { markAllRead(); menu = false },
                    leadingIcon = { Icon(Icons.Outlined.DoneAll, null) })
                if (filter.isActive) {
                    DropdownMenuItem(text = { Text("Filter zurücksetzen") }, onClick = { filter = NotificationFilter(); menu = false },
                        leadingIcon = { Icon(Icons.Outlined.Tune, null) })
                }
            }
        }
    }) { padding ->
        PullToRefreshBox(isRefreshing = refreshing, onRefresh = { scope.launch { refreshing = true; reload(); refreshing = false } },
            modifier = Modifier.fillMaxSize()) {
            LazyColumn(contentPadding = padding.content(), modifier = Modifier.readable()) {
                if (!loaded) {
                    item { Box(Modifier.fillMaxWidth().padding(40.dp), contentAlignment = Alignment.Center) { androidx.compose.material3.CircularProgressIndicator() } }
                }
                if (loaded && items.isNotEmpty()) {
                    item(key = "search") {
                        SearchField(filter.query, { filter = filter.copy(query = it) }, "Benachrichtigungen durchsuchen")
                    }
                    item(key = "chips") {
                        Row(Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()).padding(vertical = 8.dp),
                            horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                            InboxChip("Alle", Icons.Outlined.Inbox, 0, MaterialTheme.colorScheme.primary, !filter.unreadOnly && filter.kind == null) {
                                filter = filter.copy(unreadOnly = false, kind = null)
                            }
                            InboxChip("Ungelesen", Icons.Filled.Circle, unreadTotal, MaterialTheme.colorScheme.primary, filter.unreadOnly) {
                                filter = filter.copy(unreadOnly = !filter.unreadOnly)
                            }
                            NotificationFeed.kinds(items).forEach { kind ->
                                InboxChip(kind.title, kind.icon, counts[kind] ?: 0, kind.tint, filter.kind == kind) {
                                    filter = filter.copy(kind = if (filter.kind == kind) null else kind)
                                }
                            }
                        }
                    }
                }
                if (loaded && visible.isEmpty()) {
                    item(key = "empty") {
                        val (title, text) = when {
                            failure != null && items.isEmpty() -> "Nicht verfügbar" to failure!!
                            else -> NotificationFeed.emptyMessage(filter)
                        }
                        EmptyState(if (filter.isActive) Icons.Outlined.SearchOff else Icons.Outlined.NotificationsNone, title, text,
                            action = if (filter.isActive) ({ OutlinedButton(onClick = { filter = NotificationFilter() }) { Text("Filter zurücksetzen") } }) else null)
                    }
                }
                sections.forEach { section ->
                    item(key = "h-${section.id}") {
                        Row(Modifier.fillMaxWidth().padding(start = 20.dp, end = 8.dp, top = 16.dp, bottom = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                            section.kind?.let { Icon(it.icon, null, tint = tones.textSecondary, modifier = Modifier.size(16.dp)); Spacer(Modifier.width(6.dp)) }
                            Text(section.title, style = MaterialTheme.typography.titleSmall, color = tones.textSecondary, modifier = Modifier.weight(1f))
                            if (section.unreadCount > 0) {
                                Text("${section.unreadCount} neu", style = MaterialTheme.typography.labelMedium, fontWeight = FontWeight.SemiBold,
                                    color = MaterialTheme.colorScheme.primary)
                            }
                        }
                    }
                    itemsIndexed(section.items, key = { _, item -> "${section.id}-${item.id}" }) { index, item ->
                        val shape = RoundedCornerShape(
                            topStart = if (index == 0) 18.dp else 0.dp, topEnd = if (index == 0) 18.dp else 0.dp,
                            bottomStart = if (index == section.items.lastIndex) 18.dp else 0.dp, bottomEnd = if (index == section.items.lastIndex) 18.dp else 0.dp,
                        )
                        Column(Modifier.clip(shape).animateItem()) {
                            SwipeRow(item, onToggleRead = { setRead(item, !item.isRead) }, onDelete = { delete(item) }) {
                                NotificationRow(item, onClick = { open(item) },
                                    onDetails = { nav.push(Route.NotificationDetail(item)) },
                                    onToggleRead = { setRead(item, !item.isRead) },
                                    onOnlyKind = if (filter.kind != item.kind) ({ filter = filter.copy(kind = item.kind) }) else null,
                                    onDelete = { delete(item) })
                            }
                            if (index < section.items.lastIndex) RowDivider(inset = 76.dp)
                        }
                    }
                }
                if (hasMore && visible.isNotEmpty()) {
                    item(key = "more") {
                        TextButton(onClick = { loadMore() }, enabled = !loadingMore, modifier = Modifier.fillMaxWidth().padding(top = 8.dp)) {
                            Text(if (loadingMore) "Lädt …" else "Ältere laden")
                        }
                    }
                }
                if (loaded) {
                    item(key = "settings") {
                        SectionCard(Modifier.padding(top = 24.dp)) {
                            RowItem("Mitteilungseinstellungen", icon = Icons.Outlined.Tune, iconTint = tones.textSecondary, iconBackground = tones.fill,
                                subtitle = "Was ins Postfach kommt und was zusätzlich als Pushnachricht", showChevron = true,
                                onClick = { nav.push(Route.NotificationSettings) })
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun InboxChip(title: String, icon: ImageVector, count: Int, tint: Color, selected: Boolean, onClick: () -> Unit) {
    FilterChip(
        selected = selected, onClick = onClick,
        label = { Text(if (count > 0) "$title ${if (count > 99) "99+" else count}" else title, maxLines = 1) },
        leadingIcon = { Icon(icon, null, tint = if (selected) MaterialTheme.colorScheme.onPrimary else tint, modifier = Modifier.size(if (icon == Icons.Filled.Circle) 8.dp else 18.dp)) },
        colors = FilterChipDefaults.filterChipColors(containerColor = tones.card, selectedContainerColor = MaterialTheme.colorScheme.primary,
            selectedLabelColor = MaterialTheme.colorScheme.onPrimary),
        modifier = Modifier.semantics { if (count > 0) contentDescription = "$title, $count ungelesen" },
    )
}

/** Wischen nach rechts: gelesen/ungelesen; nach links: löschen. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun SwipeRow(item: InboxNotification, onToggleRead: () -> Unit, onDelete: () -> Unit, content: @Composable () -> Unit) {
    val state = rememberSwipeToDismissBoxState(confirmValueChange = { value ->
        when (value) {
            SwipeToDismissBoxValue.StartToEnd -> { onToggleRead(); false }
            SwipeToDismissBoxValue.EndToStart -> { onDelete(); true }
            SwipeToDismissBoxValue.Settled -> false
        }
    })
    SwipeToDismissBox(state, backgroundContent = {
        val toRead = state.dismissDirection == SwipeToDismissBoxValue.StartToEnd
        val color = if (toRead) MaterialTheme.colorScheme.primary else tones.danger
        Box(Modifier.fillMaxSize().background(color).padding(horizontal = 24.dp), contentAlignment = if (toRead) Alignment.CenterStart else Alignment.CenterEnd) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Icon(if (!toRead) Icons.Outlined.Delete else if (item.isRead) Icons.Outlined.MarkEmailUnread else Icons.Outlined.MarkEmailRead, null, tint = Color.White)
                Spacer(Modifier.width(8.dp))
                Text(if (!toRead) "Löschen" else if (item.isRead) "Ungelesen" else "Gelesen", color = Color.White, fontWeight = FontWeight.SemiBold)
            }
        }
    }) { content() }
}

/** Zeile wie in Mail: Ungelesen-Punkt, Symbol in der Farbe der Art, Titel, Zeit, zwei Zeilen Text, Art bzw. Absender. */
@OptIn(ExperimentalFoundationApi::class)
@Composable
private fun NotificationRow(
    item: InboxNotification, onClick: () -> Unit, onDetails: () -> Unit, onToggleRead: () -> Unit, onOnlyKind: (() -> Unit)?, onDelete: () -> Unit,
) {
    var menu by remember { mutableStateOf(false) }
    val kindTint = item.kind.tint
    val iconTint = if (item.isUrgent) tones.warning else kindTint
    Box {
        Row(
            Modifier.fillMaxWidth().background(tones.card).combinedClickable(onClick = onClick, onLongClick = { menu = true })
                .padding(start = 8.dp, end = 16.dp, top = 12.dp, bottom = 12.dp)
                .semantics(mergeDescendants = true) { stateDescription = listOfNotNull(if (item.isRead) null else "ungelesen", if (item.isUrgent) "dringend" else null).joinToString(", ") },
            verticalAlignment = Alignment.Top,
        ) {
            Box(Modifier.padding(top = 14.dp).size(8.dp).clip(CircleShape).background(if (item.isRead) Color.Transparent else MaterialTheme.colorScheme.primary))
            Spacer(Modifier.width(6.dp))
            IconTile(notificationIcon(item), iconTint, iconTint.copy(alpha = 0.14f))
            Spacer(Modifier.width(12.dp))
            Column(Modifier.weight(1f).animateContentSize()) {
                Row(verticalAlignment = Alignment.Top) {
                    Text(item.title, style = MaterialTheme.typography.bodyLarge, fontWeight = if (item.isRead) FontWeight.Normal else FontWeight.SemiBold,
                        maxLines = 2, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f))
                    Spacer(Modifier.width(8.dp))
                    item.created?.let {
                        Text(NotificationFeed.timeText(it), style = MaterialTheme.typography.labelMedium, maxLines = 1,
                            color = if (item.isRead) tones.textTertiary else MaterialTheme.colorScheme.primary, modifier = Modifier.padding(top = 3.dp))
                    }
                }
                item.body?.takeIf { it.isNotBlank() }?.let { Secondary(it, maxLines = 2) }
                Row(Modifier.padding(top = 4.dp), verticalAlignment = Alignment.CenterVertically) {
                    Text(item.senderName?.let { "${item.kind.title} · $it" } ?: item.kind.title, style = MaterialTheme.typography.labelMedium,
                        color = kindTint, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f, fill = false))
                    if (item.isUrgent) {
                        Spacer(Modifier.width(8.dp))
                        Pill("Dringend", tones.warning, tones.warningSoft, Icons.Filled.PriorityHigh)
                    }
                }
            }
        }
        DropdownMenu(menu, { menu = false }) {
            if (item.hasTarget) DropdownMenuItem(text = { Text(item.route?.let(NotificationFeed::openTitle) ?: "Öffnen") }, onClick = { menu = false; onClick() },
                leadingIcon = { Icon(Icons.AutoMirrored.Outlined.OpenInNew, null) })
            DropdownMenuItem(text = { Text("Details") }, onClick = { menu = false; onDetails() }, leadingIcon = { Icon(Icons.Outlined.Inbox, null) })
            DropdownMenuItem(text = { Text(if (item.isRead) "Als ungelesen markieren" else "Als gelesen markieren") }, onClick = { menu = false; onToggleRead() },
                leadingIcon = { Icon(if (item.isRead) Icons.Outlined.MarkEmailUnread else Icons.Outlined.MarkEmailRead, null) })
            onOnlyKind?.let { only ->
                DropdownMenuItem(text = { Text("Nur „${item.kind.title}“ zeigen") }, onClick = { menu = false; only() }, leadingIcon = { Icon(item.kind.icon, null) })
            }
            HorizontalDivider()
            DropdownMenuItem(text = { Text("Löschen", color = tones.danger) }, onClick = { menu = false; onDelete() },
                leadingIcon = { Icon(Icons.Outlined.Delete, null, tint = tones.danger) })
        }
    }
}

/** Einzelansicht: ganzer Text, Art, Absender, Zeitpunkt; Ziel öffnen, im Browser öffnen, gelesen/ungelesen, löschen. */
@Composable
fun NotificationDetailScreen(initial: InboxNotification) {
    val session = LocalSession.current
    val nav = LocalNav.current
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    var item by remember(initial.id) { mutableStateOf(initial) }
    var confirmDelete by remember { mutableStateOf(false) }
    val tint = item.kind.tint
    val webUrl = item.webPath?.takeIf { !session.isDemo }?.let { session.api.baseUrl.resolve(it)?.toString() }

    ScreenScaffold(item.kind.title) { padding ->
        Column(Modifier.padding(padding).verticalScroll(rememberScrollState()).readable().padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            SectionCard(padding = androidx.compose.foundation.layout.PaddingValues(16.dp)) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    IconTile(notificationIcon(item), tint, tint.copy(alpha = 0.14f), size = 48.dp)
                    Spacer(Modifier.width(12.dp))
                    Column {
                        Text(item.kind.title, style = MaterialTheme.typography.titleSmall, color = tint)
                        item.created?.let { Secondary(NotificationFeed.fullTimeText(it)) }
                    }
                }
                Spacer(Modifier.height(12.dp))
                Text(item.title, style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.Bold)
                item.body?.takeIf { it.isNotBlank() }?.let {
                    Spacer(Modifier.height(8.dp))
                    SelectionContainer { Text(it, style = MaterialTheme.typography.bodyLarge) }
                }
                Spacer(Modifier.height(12.dp))
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    item.senderName?.let { Pill(it, tones.textSecondary, tones.fillStrong) }
                    if (item.isUrgent) Pill("Dringend", tones.warning, tones.warningSoft, Icons.Filled.PriorityHigh)
                    Pill(if (item.isRead) "Gelesen" else "Ungelesen", if (item.isRead) tones.textSecondary else MaterialTheme.colorScheme.primary,
                        if (item.isRead) tones.fillStrong else tones.infoSoft)
                }
            }
            val route = item.route
            if (item.hasTarget && route != null) {
                Button(onClick = { nav.open(route) }, modifier = Modifier.fillMaxWidth()) {
                    Icon(Icons.AutoMirrored.Outlined.OpenInNew, null); Spacer(Modifier.width(8.dp)); Text(NotificationFeed.openTitle(route))
                }
            }
            if (webUrl != null) {
                OutlinedButton(onClick = { Links.web(context, webUrl) }, modifier = Modifier.fillMaxWidth()) {
                    Icon(Icons.Outlined.Public, null); Spacer(Modifier.width(8.dp)); Text("Im Browser öffnen")
                }
            }
            OutlinedButton(onClick = {
                val read = !item.isRead
                item = item.copy(readAt = if (read) ApiDate.format(Instant.now()) else null)
                scope.launch {
                    runCatching { withContext(Dispatchers.IO) { if (read) session.api.markNotificationRead(item.id) else session.api.markNotificationUnread(item.id) } }
                    runCatching { withContext(Dispatchers.IO) { session.api.notifications(1) } }.onSuccess { session.setUnreadNotifications(it.unreadCount) }
                }
            }, modifier = Modifier.fillMaxWidth()) {
                Icon(if (item.isRead) Icons.Outlined.MarkEmailUnread else Icons.Outlined.MarkEmailRead, null); Spacer(Modifier.width(8.dp))
                Text(if (item.isRead) "Als ungelesen markieren" else "Als gelesen markieren")
            }
            TextButton(onClick = { confirmDelete = true }, modifier = Modifier.fillMaxWidth()) {
                Icon(Icons.Outlined.Delete, null, tint = tones.danger); Spacer(Modifier.width(8.dp)); Text("Löschen", color = tones.danger)
            }
            if (!item.hasTarget && webUrl == null) Secondary("Zu dieser Benachrichtigung gibt es in der App keine eigene Seite.")
        }
    }
    if (confirmDelete) {
        ConfirmDialog("Benachrichtigung löschen?", "Sie wird aus Ihrem Postfach entfernt.", "Löschen", {
            scope.launch {
                runCatching { withContext(Dispatchers.IO) { session.api.deleteNotification(item.id) } }
                nav.back()
            }
        }, { confirmDelete = false })
    }
}
