package de.estrichone.android.ui

import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.PickVisualMediaRequest
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.Reply
import androidx.compose.material.icons.automirrored.filled.Send
import androidx.compose.material.icons.filled.AddPhotoAlternate
import androidx.compose.material.icons.filled.Apartment
import androidx.compose.material.icons.filled.Campaign
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Description
import androidx.compose.material.icons.filled.DoneAll
import androidx.compose.material.icons.filled.Edit
import androidx.compose.material.icons.filled.Groups
import androidx.compose.material.icons.filled.LocalShipping
import androidx.compose.material.icons.filled.Place
import androidx.compose.material.icons.filled.Schedule
import androidx.compose.material.icons.outlined.Forum
import androidx.compose.material.icons.outlined.PersonAdd
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ExtendedFloatingActionButton
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextField
import androidx.compose.material3.TextFieldDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
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
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalClipboardManager
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.font.FontStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import de.estrichone.android.Session
import de.estrichone.android.support.Images
import de.estrichone.android.support.Links
import de.estrichone.core.ApiException
import de.estrichone.core.ChatAttachment
import de.estrichone.core.ChatConversation
import de.estrichone.core.ChatLimits
import de.estrichone.core.ChatMessage
import de.estrichone.core.ChatType
import de.estrichone.core.Dates
import de.estrichone.core.Ids
import de.estrichone.core.chatAttachment
import de.estrichone.core.chatContacts
import de.estrichone.core.chatConversations
import de.estrichone.core.chatMessages
import de.estrichone.core.deleteChatMessage
import de.estrichone.core.editChatMessage
import de.estrichone.core.initials
import de.estrichone.core.markChatRead
import de.estrichone.core.muteChat
import de.estrichone.core.chatConversation
import de.estrichone.core.reactToChatMessage
import de.estrichone.core.sendChatMessage
import de.estrichone.core.startDirectChat
import de.estrichone.core.uploadChatAttachment
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.time.Instant

private fun chatIcon(type: ChatType) = when (type) {
    ChatType.Direct -> null
    ChatType.Group -> Icons.Filled.Groups
    ChatType.Crew -> Icons.Filled.LocalShipping
    ChatType.Site -> Icons.Filled.Apartment
    ChatType.Announcement -> Icons.Filled.Campaign
}

/** Unterhaltungen (WhatsApp-Ersatz): ungelesene oben fett, Abfrage alle 15 Sekunden (ohne Push). */
@Composable
fun ChatListScreen() {
    val session = LocalSession.current
    val nav = LocalNav.current
    var search by remember { mutableStateOf("") }
    var conversations by remember { mutableStateOf<Load<List<ChatConversation>>>(Load.Loading) }
    var starting by remember { mutableStateOf(false) }
    LaunchedEffect(Unit) {
        while (true) {
            val next = attempt { session.api.chatConversations() }
            if (next is Load.Done || conversations !is Load.Done) conversations = next
            session.refreshCounters()
            delay(15_000)
        }
    }
    ScreenScaffold("Chat", floating = {
        ExtendedFloatingActionButton(onClick = { starting = true }, icon = { Icon(Icons.Outlined.PersonAdd, null) }, text = { Text("Neuer Chat") })
    }) { padding ->
        LoadView(conversations) {
            val list = (conversations as Load.Done).value.filter { search.isBlank() || it.title.contains(search, true) || it.lastMessage?.summary?.contains(search, true) == true }
            LazyColumn(contentPadding = padding.content(), modifier = Modifier.readable()) {
                item { SearchField(search, { search = it }, "Unterhaltungen durchsuchen", Modifier.padding(bottom = 12.dp)) }
                if (list.isEmpty()) item { EmptyState(Icons.Outlined.Forum, "Keine Unterhaltungen", "Mit „Neuer Chat“ eine Kollegin oder einen Kollegen anschreiben.") }
                else item {
                    SectionCard {
                        list.forEachIndexed { index, c ->
                            if (index > 0) RowDivider(76.dp)
                            ConversationRow(c) { nav.push(Route.Chat(c.id)) }
                        }
                    }
                }
            }
        }
    }
    if (starting) StartChatSheet(onDismiss = { starting = false }) { id -> starting = false; nav.push(Route.Chat(id)) }
}

@Composable
private fun ConversationRow(c: ChatConversation, onClick: () -> Unit) {
    Row(Modifier.fillMaxWidth().clickable(onClick = onClick).padding(horizontal = 16.dp, vertical = 12.dp), verticalAlignment = Alignment.CenterVertically) {
        val icon = chatIcon(c.type)
        if (icon == null) Avatar(initials(c.title), 48.dp) else Box(Modifier.size(48.dp).clip(CircleShape).background(tones.infoSoft), contentAlignment = Alignment.Center) {
            Icon(icon, null, tint = MaterialTheme.colorScheme.primary)
        }
        Spacer(Modifier.width(12.dp))
        Column(Modifier.weight(1f)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(c.title, style = MaterialTheme.typography.titleMedium, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f))
                c.lastActivity?.let { Text(Dates.ago(it), style = MaterialTheme.typography.labelMedium, color = if (c.unreadCount > 0) MaterialTheme.colorScheme.primary else tones.textTertiary) }
            }
            Row(verticalAlignment = Alignment.CenterVertically) {
                val last = c.lastMessage
                val preview = when {
                    last == null -> c.type.title
                    c.isDirect || last.mine -> (if (last.mine) "Sie: " else "") + last.summary
                    else -> "${last.senderName?.substringBefore(' ')}: ${last.summary}"
                }
                Text(preview, style = MaterialTheme.typography.bodyMedium, color = tones.textSecondary, maxLines = 2, overflow = TextOverflow.Ellipsis,
                    fontWeight = if (c.unreadCount > 0) FontWeight.SemiBold else FontWeight.Normal, modifier = Modifier.weight(1f))
                if (c.unreadMentions > 0) Text("@", color = MaterialTheme.colorScheme.primary, fontWeight = FontWeight.Bold, modifier = Modifier.padding(horizontal = 4.dp))
                CountBadge(c.unreadCount)
            }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun StartChatSheet(onDismiss: () -> Unit, onStarted: (String) -> Unit) {
    val session = LocalSession.current
    val scope = rememberCoroutineScope()
    var search by remember { mutableStateOf("") }
    val people by rememberLoad(Unit) { session.api.chatContacts() }
    ModalBottomSheet(onDismissRequest = onDismiss, containerColor = tones.canvas) {
        Column(Modifier.padding(horizontal = 16.dp).padding(bottom = 24.dp)) {
            Text("Neuer Chat", style = MaterialTheme.typography.headlineSmall, modifier = Modifier.padding(bottom = 12.dp))
            SearchField(search, { search = it }, "Name suchen")
            Spacer(Modifier.height(12.dp))
            LoadView(people) {
                SectionCard {
                    (people as Load.Done).value.filter { it.name.contains(search, true) }.forEachIndexed { index, person ->
                        if (index > 0) RowDivider(72.dp)
                        Row(Modifier.fillMaxWidth().clickable {
                            scope.launch { runCatching { withContext(Dispatchers.IO) { session.api.startDirectChat(person.id) } }.onSuccess { onStarted(it.id) } }
                        }.padding(horizontal = 16.dp, vertical = 10.dp), verticalAlignment = Alignment.CenterVertically) {
                            Avatar(initials(person.name))
                            Spacer(Modifier.width(12.dp))
                            Text(person.name, style = MaterialTheme.typography.bodyLarge)
                        }
                    }
                }
            }
        }
    }
}

/** Verlauf einer Unterhaltung mit Eingabe, Diktat und Fotoanhang. */
@OptIn(ExperimentalFoundationApi::class)
@Composable
fun ConversationScreen(id: String) {
    val session = LocalSession.current
    val model = LocalModel.current
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val messages = remember { mutableStateListOf<ChatMessage>() }
    var conversation by remember { mutableStateOf<ChatConversation?>(null) }
    var text by remember { mutableStateOf("") }
    var replyTo by remember { mutableStateOf<ChatMessage?>(null) }
    var editing by remember { mutableStateOf<ChatMessage?>(null) }
    var loaded by remember { mutableStateOf(false) }
    val listState = rememberLazyListState()
    val windowMinutes = session.chat.value?.editWindowMinutes ?: 15

    suspend fun reload() {
        val page = runCatching { withContext(Dispatchers.IO) { session.api.chatMessages(id) } }.getOrNull() ?: return
        val pending = messages.filter { it.localState != null && page.items.none { m -> m.clientMessageId == it.clientMessageId } }
        messages.clear()
        messages.addAll(page.items + pending)
        loaded = true
        runCatching { withContext(Dispatchers.IO) { session.api.markChatRead(id) } }
    }

    LaunchedEffect(id) {
        conversation = runCatching { withContext(Dispatchers.IO) { session.api.chatConversations().firstOrNull { it.id == id } } }.getOrNull()
        while (true) {
            reload()
            session.refreshCounters()
            delay(5_000)
        }
    }
    LaunchedEffect(messages.size) { if (messages.isNotEmpty()) listState.animateScrollToItem(messages.size) }

    fun send(attachments: List<ChatAttachment> = emptyList()) {
        val body = text.trim()
        val edit = editing
        if (edit != null) {
            scope.launch {
                runCatching { withContext(Dispatchers.IO) { session.api.editChatMessage(edit.id, body) } }.onFailure { model.message((it as? ApiException)?.message ?: "Nicht gespeichert.") }
                editing = null
                text = ""
                reload()
            }
            return
        }
        if (!ChatLimits.canSend(body, attachments.size)) return
        val clientId = Ids.v7()
        val local = ChatMessage(Ids.v7(), id, session.user.id, session.user.displayName, body, de.estrichone.core.ApiDate.format(Instant.now()), mine = true,
            clientMessageId = clientId, attachments = attachments, localState = "sending",
            replyTo = replyTo?.let { de.estrichone.core.ChatReply(it.id, it.senderName, it.text) })
        val reply = replyTo?.id
        messages.add(local)
        text = ""
        replyTo = null
        scope.launch {
            val result = runCatching { withContext(Dispatchers.IO) { session.api.sendChatMessage(id, body, clientId, reply, attachments.map { it.id }) } }
            val index = messages.indexOfFirst { it.clientMessageId == clientId }
            if (index >= 0) messages[index] = result.getOrNull() ?: local.copy(localState = "failed")
            if (result.isFailure) model.message("Nachricht nicht gesendet – bitte erneut versuchen.")
        }
    }

    val picker = rememberLauncherForActivityResult(ActivityResultContracts.PickVisualMedia()) { uri ->
        uri ?: return@rememberLauncherForActivityResult
        scope.launch {
            val attachment = runCatching {
                withContext(Dispatchers.IO) {
                    val bytes = context.contentResolver.openInputStream(uri)?.use { it.readBytes() } ?: error("leer")
                    session.api.uploadChatAttachment(id, Images.preparePhoto(bytes), "Foto.jpg", "image/jpeg")
                }
            }.getOrNull()
            if (attachment == null) model.message("Das Foto konnte nicht hochgeladen werden.") else send(listOf(attachment))
        }
    }

    val c = conversation
    var info by remember { mutableStateOf(false) }
    ScreenScaffold(c?.title ?: "Chat", actions = {
        IconButton(onClick = { info = true }) { Icon(Icons.Filled.Groups, "Info zur Unterhaltung") }
    }, bottomBar = {
        if (c?.canPost == false) {
            Text("Nur Administratoren schreiben in dieser Gruppe.", color = tones.textSecondary, style = MaterialTheme.typography.bodyMedium,
                modifier = Modifier.fillMaxWidth().background(tones.card).navigationBarsPadding().padding(16.dp))
        } else {
            Composer(text, { text = it }, replyTo ?: editing, editing != null, onCancel = { replyTo = null; editing = null; text = "" },
                onAttach = { picker.launch(PickVisualMediaRequest(ActivityResultContracts.PickVisualMedia.ImageOnly)) }, onSend = { send() })
        }
    }) { padding ->
        if (info) ConversationInfo(id) { info = false }
        if (!loaded) LoadView(Load.Loading) {} else
        LazyColumn(Modifier.fillMaxSize(), state = listState, contentPadding = PaddingValues(start = 12.dp, end = 12.dp, top = padding.calculateTopPadding() + 8.dp,
            bottom = padding.calculateBottomPadding() + 12.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            var lastDay: java.time.LocalDate? = null
            messages.forEachIndexed { index, m ->
                val day = m.created.atZone(Dates.zone).toLocalDate()
                if (day != lastDay) {
                    lastDay = day
                    item(key = "day-$day") {
                        Box(Modifier.fillMaxWidth().padding(vertical = 8.dp), contentAlignment = Alignment.Center) {
                            Text(Dates.relativeDay(day), style = MaterialTheme.typography.labelMedium, color = tones.textSecondary,
                                modifier = Modifier.clip(RoundedCornerShape(50)).background(tones.fill).padding(horizontal = 12.dp, vertical = 4.dp))
                        }
                    }
                }
                val showName = !m.mine && c?.isDirect != true && messages.getOrNull(index - 1)?.senderUserId != m.senderUserId
                item(key = m.clientMessageId ?: m.id) {
                    Bubble(m, showName, session, windowMinutes, onReply = { replyTo = m }, onEdit = { editing = m; text = m.text ?: "" },
                        onDelete = { scope.launch { runCatching { withContext(Dispatchers.IO) { session.api.deleteChatMessage(m.id) } }; reload() } },
                        onReact = { emoji -> scope.launch { runCatching { withContext(Dispatchers.IO) { session.api.reactToChatMessage(m.id, emoji) } }; reload() } })
                }
            }
        }
    }
}

/** Info zur Unterhaltung: Art, Beschreibung, Mitglieder, Stummschalten (wie ChatInfoView auf iOS). */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun ConversationInfo(id: String, onDismiss: () -> Unit) {
    val session = LocalSession.current
    val scope = rememberCoroutineScope()
    var detail by remember { mutableStateOf<Load<de.estrichone.core.ChatConversationDetail>>(Load.Loading) }
    LaunchedEffect(id) { detail = attempt { session.api.chatConversation(id) } }
    ModalBottomSheet(onDismissRequest = onDismiss, containerColor = tones.canvas) {
        Column(Modifier.padding(horizontal = 16.dp).padding(bottom = 28.dp)) {
            LoadView(detail) {
                val d = (detail as Load.Done).value
                Text(d.conversation.title, style = MaterialTheme.typography.headlineSmall)
                Secondary(listOfNotNull(d.conversation.type.title, d.bauakteTitle?.let { "Bauakte $it" }).joinToString(" · "))
                d.conversation.description?.let { Text(it, modifier = Modifier.padding(top = 8.dp)) }
                Spacer(Modifier.height(12.dp))
                SectionCard {
                    RowItem("Stummschalten", subtitle = "Keine Mitteilungen für diese Unterhaltung", trailing = {
                        androidx.compose.material3.Switch(d.conversation.muted, { muted ->
                            scope.launch {
                                runCatching { withContext(Dispatchers.IO) { session.api.muteChat(id, muted) } }
                                detail = attempt { session.api.chatConversation(id) }
                            }
                        })
                    })
                }
                GroupLabel("${d.members.size} Mitglieder", Modifier.padding(start = 4.dp))
                SectionCard {
                    d.members.forEachIndexed { index, m ->
                        if (index > 0) RowDivider(72.dp)
                        Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 10.dp), verticalAlignment = Alignment.CenterVertically) {
                            Avatar(initials(m.name))
                            Spacer(Modifier.width(12.dp))
                            Text(m.name, style = MaterialTheme.typography.bodyLarge, modifier = Modifier.weight(1f))
                            if (m.isAdmin) Pill("Admin", MaterialTheme.colorScheme.primary, tones.infoSoft)
                            if (m.fromCrew) Pill("Kolonne", tones.textSecondary, tones.fill)
                        }
                    }
                }
            }
        }
    }
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
private fun Bubble(
    m: ChatMessage, showName: Boolean, session: Session, windowMinutes: Int, onReply: () -> Unit, onEdit: () -> Unit, onDelete: () -> Unit,
    onReact: (String?) -> Unit,
) {
    val clipboard = LocalClipboardManager.current
    val context = LocalContext.current
    var menu by remember { mutableStateOf(false) }
    val mine = m.mine
    val bubbleColor = if (mine) MaterialTheme.colorScheme.primary else tones.card
    val textColor = if (mine) MaterialTheme.colorScheme.onPrimary else MaterialTheme.colorScheme.onSurface
    Row(Modifier.fillMaxWidth(), horizontalArrangement = if (mine) Arrangement.End else Arrangement.Start) {
        Column(horizontalAlignment = if (mine) Alignment.End else Alignment.Start, modifier = Modifier.widthIn(max = 320.dp)) {
            Box {
                Column(
                    Modifier.clip(RoundedCornerShape(topStart = 18.dp, topEnd = 18.dp, bottomStart = if (mine) 18.dp else 6.dp, bottomEnd = if (mine) 6.dp else 18.dp))
                        .background(bubbleColor).combinedClickable(onClick = {}, onLongClick = { menu = true }).padding(horizontal = 12.dp, vertical = 8.dp),
                ) {
                    if (showName) Text(m.senderName ?: "", style = MaterialTheme.typography.labelLarge, color = nameColor(m.senderName))
                    m.replyTo?.let { r ->
                        Column(Modifier.padding(vertical = 4.dp).clip(RoundedCornerShape(8.dp)).background(if (mine) Color.White.copy(alpha = 0.18f) else tones.fill)
                            .padding(horizontal = 8.dp, vertical = 4.dp)) {
                            Text(r.senderName ?: "", style = MaterialTheme.typography.labelMedium, color = if (mine) textColor else MaterialTheme.colorScheme.primary)
                            Text(if (r.deleted) "Nachricht gelöscht" else r.text ?: (if (r.attachment == "photo") "📷 Foto" else ""), style = MaterialTheme.typography.bodySmall,
                                color = textColor.copy(alpha = 0.85f), maxLines = 2, overflow = TextOverflow.Ellipsis)
                        }
                    }
                    m.attachments.forEach { a -> AttachmentView(a, session, mine) }
                    m.location?.let { loc ->
                        Row(Modifier.padding(vertical = 4.dp).clip(RoundedCornerShape(10.dp)).background(if (mine) Color.White.copy(alpha = 0.18f) else tones.fill)
                            .clickable { Links.route(context, loc.label, loc.latitude, loc.longitude) }.padding(10.dp), verticalAlignment = Alignment.CenterVertically) {
                            Icon(Icons.Filled.Place, null, tint = if (mine) textColor else tones.danger)
                            Spacer(Modifier.width(6.dp))
                            Text(loc.label ?: "Standort", color = textColor, style = MaterialTheme.typography.bodyMedium)
                        }
                    }
                    if (m.deleted) Text("Nachricht gelöscht", color = textColor.copy(alpha = 0.7f), fontStyle = FontStyle.Italic)
                    else m.text?.takeIf { it.isNotBlank() }?.let { Text(it, color = textColor, style = MaterialTheme.typography.bodyLarge) }
                    Row(Modifier.align(Alignment.End), verticalAlignment = Alignment.CenterVertically) {
                        if (m.editedAt != null) Text("bearbeitet · ", style = MaterialTheme.typography.labelSmall, color = textColor.copy(alpha = 0.7f))
                        Text(Dates.time(m.created), style = MaterialTheme.typography.labelSmall, color = textColor.copy(alpha = 0.7f))
                        if (mine) {
                            Spacer(Modifier.width(3.dp))
                            when {
                                m.localState == "sending" -> Icon(Icons.Filled.Schedule, "Wird gesendet", tint = textColor.copy(alpha = 0.7f), modifier = Modifier.size(14.dp))
                                m.localState == "failed" -> Icon(Icons.Filled.Close, "Nicht gesendet", tint = tones.badge, modifier = Modifier.size(14.dp))
                                else -> Icon(Icons.Filled.DoneAll, if (m.isRead) "Gelesen" else "Zugestellt",
                                    tint = if (m.isRead) Color(0xFF8FD3FF) else textColor.copy(alpha = 0.7f), modifier = Modifier.size(14.dp))
                            }
                        }
                    }
                }
                DropdownMenu(menu, { menu = false }) {
                    Row(Modifier.padding(horizontal = 8.dp)) {
                        ChatLimits.quickReactions.forEach { emoji ->
                            Text(emoji, style = MaterialTheme.typography.titleLarge, modifier = Modifier.clip(CircleShape).clickable { menu = false; onReact(emoji) }.padding(6.dp))
                        }
                    }
                    HorizontalDivider()
                    DropdownMenuItem(text = { Text("Antworten") }, leadingIcon = { Icon(Icons.AutoMirrored.Filled.Reply, null) }, onClick = { menu = false; onReply() })
                    m.text?.let { t -> DropdownMenuItem(text = { Text("Kopieren") }, onClick = { menu = false; clipboard.setText(AnnotatedString(t)) }) }
                    if (m.isEditable(windowMinutes)) DropdownMenuItem(text = { Text("Bearbeiten") }, leadingIcon = { Icon(Icons.Filled.Edit, null) }, onClick = { menu = false; onEdit() })
                    if (mine && !m.deleted) DropdownMenuItem(text = { Text("Löschen", color = tones.danger) }, onClick = { menu = false; onDelete() })
                }
            }
            if (m.reactions.isNotEmpty()) {
                Row(Modifier.padding(top = 2.dp), horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                    m.reactions.forEach { r ->
                        Text("${r.emoji} ${r.count}", style = MaterialTheme.typography.labelMedium,
                            modifier = Modifier.clip(RoundedCornerShape(50)).background(if (r.mine) tones.infoSoft else tones.card).clickable { onReact(if (r.mine) null else r.emoji) }
                                .padding(horizontal = 8.dp, vertical = 2.dp))
                    }
                }
            }
        }
    }
}

@Composable
private fun AttachmentView(a: ChatAttachment, session: Session, mine: Boolean) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    if (a.isPhoto) {
        val bitmap by produceState<android.graphics.Bitmap?>(null, a.id) {
            value = withContext(Dispatchers.IO) { runCatching { session.api.chatAttachment(a.id, thumbnail = true) ?: session.api.chatAttachment(a.id) }.getOrNull()?.let { Images.decode(it, 600) } }
        }
        Box(Modifier.padding(vertical = 4.dp).size(220.dp, 165.dp).clip(RoundedCornerShape(12.dp)).background(tones.fill).clickable {
            scope.launch { session.api.chatAttachment(a.id)?.let { Links.open(context, it, a.fileName.ifBlank { "Foto.jpg" }, a.contentType.ifBlank { "image/jpeg" }) } }
        }) { bitmap?.let { Image(it.asImageBitmap(), a.fileName, contentScale = ContentScale.Crop, modifier = Modifier.fillMaxSize()) } }
    } else {
        Row(Modifier.padding(vertical = 4.dp).clip(RoundedCornerShape(10.dp)).background(if (mine) Color.White.copy(alpha = 0.18f) else tones.fill).clickable {
            scope.launch { session.api.chatAttachment(a.id)?.let { Links.open(context, it, a.fileName, a.contentType) } }
        }.padding(10.dp), verticalAlignment = Alignment.CenterVertically) {
            Icon(Icons.Filled.Description, null, tint = if (mine) Color.White else tones.danger)
            Spacer(Modifier.width(8.dp))
            Column {
                Text(a.fileName, style = MaterialTheme.typography.bodyMedium, color = if (mine) Color.White else MaterialTheme.colorScheme.onSurface)
                Text("${(a.sizeBytes / 1024).coerceAtLeast(1)} KB", style = MaterialTheme.typography.labelSmall, color = if (mine) Color.White.copy(alpha = 0.8f) else tones.textSecondary)
            }
        }
    }
}

@Composable
private fun nameColor(name: String?): Color {
    val palette = listOf(Color(0xFF0062CC), Color(0xFF1C7A45), Color(0xFFB4530A), Color(0xFF7B4FD0), Color(0xFFC0262D), Color(0xFF00838F))
    val dark = listOf(Color(0xFF4DA3FF), Color(0xFF5FCF8C), Color(0xFFF3B54A), Color(0xFFB79BFF), Color(0xFFFF7B7B), Color(0xFF4DD0E1))
    val index = ((name ?: "").hashCode() and 0x7FFFFFFF) % palette.size
    return if (tones.dark) dark[index] else palette[index]
}

@Composable
private fun Composer(
    text: String, onText: (String) -> Unit, context: ChatMessage?, editing: Boolean, onCancel: () -> Unit, onAttach: () -> Unit, onSend: () -> Unit,
) {
    Surface(color = tones.card, tonalElevation = 2.dp) {
        Column(Modifier.navigationBarsPadding().imePadding()) {
            context?.let { m ->
                Row(Modifier.fillMaxWidth().padding(start = 16.dp, end = 4.dp, top = 6.dp), verticalAlignment = Alignment.CenterVertically) {
                    Icon(if (editing) Icons.Filled.Edit else Icons.AutoMirrored.Filled.Reply, null, tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(18.dp))
                    Spacer(Modifier.width(8.dp))
                    Column(Modifier.weight(1f)) {
                        Text(if (editing) "Nachricht bearbeiten" else "Antwort an ${m.senderName ?: ""}", style = MaterialTheme.typography.labelLarge, color = MaterialTheme.colorScheme.primary)
                        Secondary(m.summary, maxLines = 1)
                    }
                    IconButton(onClick = onCancel) { Icon(Icons.Filled.Close, "Abbrechen") }
                }
            }
            Row(Modifier.fillMaxWidth().padding(horizontal = 6.dp, vertical = 6.dp), verticalAlignment = Alignment.Bottom) {
                if (!editing) IconButton(onClick = onAttach) { Icon(Icons.Filled.AddPhotoAlternate, "Foto anhängen") }
                TextField(text, onText, placeholder = { Text("Nachricht") }, maxLines = 5, shape = RoundedCornerShape(22.dp), modifier = Modifier.weight(1f),
                    colors = TextFieldDefaults.colors(focusedContainerColor = tones.fill, unfocusedContainerColor = tones.fill,
                        focusedIndicatorColor = Color.Transparent, unfocusedIndicatorColor = Color.Transparent),
                    trailingIcon = { DictationButton { onText(appendDictation(text, it)) } })
                IconButton(onClick = onSend, enabled = text.isNotBlank()) {
                    Icon(Icons.AutoMirrored.Filled.Send, "Senden", tint = if (text.isNotBlank()) MaterialTheme.colorScheme.primary else tones.textTertiary)
                }
            }
        }
    }
}
