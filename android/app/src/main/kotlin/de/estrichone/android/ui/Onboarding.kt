package de.estrichone.android.ui

import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
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
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.Backspace
import androidx.compose.material.icons.filled.Fingerprint
import androidx.compose.material.icons.filled.HourglassTop
import androidx.compose.material.icons.filled.Lock
import androidx.compose.material.icons.filled.PersonAdd
import androidx.compose.material.icons.filled.Visibility
import androidx.compose.material.icons.filled.VisibilityOff
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.SegmentedButton
import androidx.compose.material3.SegmentedButtonDefaults
import androidx.compose.material3.SingleChoiceSegmentedButtonRow
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import de.estrichone.android.AppModel
import de.estrichone.android.R
import de.estrichone.android.Stage
import de.estrichone.android.data.PoolUser
import de.estrichone.core.DeviceKind
import de.estrichone.core.PinPolicy
import de.estrichone.core.TwoFactorLogin
import de.estrichone.core.initials

/** Wurzel der Oberfläche: Einrichtung, Anmeldung, Sperre oder Hauptansicht. */
@Composable
fun Root(model: AppModel, unlock: () -> Unit = {}) {
    val stage by model.stage.collectAsState()
    val session by model.session.collectAsState()
    when (val s = stage) {
        Stage.Starting -> Box(Modifier.fillMaxSize().background(tones.canvas), contentAlignment = Alignment.Center) { CircularProgressIndicator() }
        Stage.Welcome -> WelcomeScreen(model)
        is Stage.Server -> OnboardingFrame(model, "Server verbinden", "Adresse Ihres EstrichONE-Servers, z. B. estrich.firma.de – steht im Zugangsbrief.") { ServerStep(model, s.address) }
        is Stage.SignIn -> OnboardingFrame(model, "Anmelden", s.serverName.ifBlank { "EstrichONE" }) { SignInStep(model, s) }
        is Stage.TwoFactor -> OnboardingFrame(model, "Bestätigungscode", "Code aus der Authenticator-App bzw. Wiederherstellungscode.") { TwoFactorStep(model, s) }
        is Stage.ChangePassword -> OnboardingFrame(model, "Neues Passwort", "Sie haben ein Einmalpasswort erhalten. Bitte legen Sie jetzt ein eigenes fest.") { ChangePasswordStep(model) }
        is Stage.Pending -> OnboardingFrame(model, "Wartet auf Freigabe", "„${s.deviceName}“ ist registriert und muss in der Verwaltung freigegeben werden.") { PendingStep(model) }
        is Stage.ChoosePin -> OnboardingFrame(model, "PIN festlegen", "Poolgerät: Mit dieser 6-stelligen PIN entsperren Sie künftig Ihre Daten auf diesem Gerät.") { ChoosePinStep(model) }
        is Stage.UserPicker -> UserPickerScreen(model, s.users)
        is Stage.PinEntry -> PinEntryScreen(model, s.user)
        Stage.Locked -> LockScreen(model, unlock)
        Stage.Ready -> session?.let { MainScreen(model, it) }
            ?: Box(Modifier.fillMaxSize().background(tones.canvas), contentAlignment = Alignment.Center) { CircularProgressIndicator() }
    }
}

/** Markenlogo (E1-Monogramm) auf Navy. */
@Composable
fun BrandMark(size: androidx.compose.ui.unit.Dp = 88.dp) {
    Box(Modifier.size(size).clip(RoundedCornerShape(size * 0.24f)).background(Brush.linearGradient(listOf(NavyLight, Navy))), contentAlignment = Alignment.Center) {
        Image(painterResource(R.drawable.ic_launcher_foreground), "EstrichONE", modifier = Modifier.size(size * 1.5f))
    }
}

@Composable
private fun WelcomeScreen(model: AppModel) {
    Box(Modifier.fillMaxSize().background(Brush.verticalGradient(listOf(Navy, NavyLight)))) {
        Column(Modifier.fillMaxSize().windowInsetsPadding(WindowInsets.safeDrawing).padding(28.dp).widthIn(max = 520.dp).align(Alignment.Center),
            horizontalAlignment = Alignment.CenterHorizontally) {
            Spacer(Modifier.weight(1f))
            BrandMark(112.dp)
            Spacer(Modifier.height(24.dp))
            Text("EstrichONE", color = Color.White, style = MaterialTheme.typography.displaySmall)
            Text("Baustelle, Büro und Kolonne – in einer App.", color = Ice, style = MaterialTheme.typography.titleMedium, textAlign = TextAlign.Center,
                modifier = Modifier.padding(top = 8.dp))
            Spacer(Modifier.height(28.dp))
            listOf("Einsätze und Baustellen auch ohne Netz", "Fotos, Bautagebuch und Protokolle direkt vor Ort", "Chat mit Büro und Kolonne").forEach {
                Row(Modifier.fillMaxWidth().padding(vertical = 5.dp), verticalAlignment = Alignment.CenterVertically) {
                    Box(Modifier.size(8.dp).clip(CircleShape).background(Color(0xFF4DA3FF)))
                    Spacer(Modifier.width(12.dp))
                    Text(it, color = Color.White, style = MaterialTheme.typography.bodyLarge)
                }
            }
            Spacer(Modifier.weight(1f))
            Button(onClick = model::beginSetup, modifier = Modifier.fillMaxWidth().height(54.dp),
                colors = ButtonDefaults.buttonColors(containerColor = Color.White, contentColor = Navy)) { Text("Einrichten", fontSize = 17.sp) }
            TextButton(onClick = model::startDemo, modifier = Modifier.fillMaxWidth().padding(top = 8.dp)) {
                Text("Demo ausprobieren", color = Color.White, fontSize = 16.sp)
            }
            Text("Die Demo zeigt Beispieldaten. Es wird nichts gesendet.", color = Ice.copy(alpha = 0.8f), style = MaterialTheme.typography.bodySmall)
        }
    }
}

@Composable
private fun OnboardingFrame(model: AppModel, title: String, subtitle: String, content: @Composable ColumnScope.() -> Unit) {
    val error by model.error.collectAsState()
    val busy by model.busy.collectAsState()
    Box(Modifier.fillMaxSize().background(tones.canvas)) {
        Column(Modifier.fillMaxSize().windowInsetsPadding(WindowInsets.safeDrawing).imePadding().verticalScroll(rememberScrollState())
            .padding(24.dp).widthIn(max = 520.dp).align(Alignment.TopCenter), verticalArrangement = Arrangement.spacedBy(14.dp)) {
            Spacer(Modifier.height(24.dp))
            BrandMark(64.dp)
            Text(title, style = MaterialTheme.typography.headlineMedium)
            Secondary(subtitle)
            Spacer(Modifier.height(4.dp))
            content()
            if (busy) CircularProgressIndicator(Modifier.align(Alignment.CenterHorizontally))
            error?.let { Banner(it, Icons.Filled.Lock, tones.danger, tones.dangerSoft) }
        }
    }
}

@Composable
private fun ServerStep(model: AppModel, initial: String) {
    var address by rememberSaveable { mutableStateOf(initial) }
    OutlinedTextField(address, { address = it }, label = { Text("Serveradresse") }, singleLine = true, modifier = Modifier.fillMaxWidth(),
        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri, imeAction = ImeAction.Go), keyboardActions = KeyboardActions(onGo = { model.connect(address) }))
    Button(onClick = { model.connect(address) }, enabled = address.isNotBlank(), modifier = Modifier.fillMaxWidth().height(52.dp)) { Text("Weiter") }
    TextButton(onClick = model::startDemo, modifier = Modifier.fillMaxWidth()) { Text("Stattdessen Demo ausprobieren") }
}

@Composable
private fun PasswordField(value: String, onChange: (String) -> Unit, label: String, onDone: (() -> Unit)? = null) {
    var visible by remember { mutableStateOf(false) }
    OutlinedTextField(value, onChange, label = { Text(label) }, singleLine = true, modifier = Modifier.fillMaxWidth(),
        visualTransformation = if (visible) VisualTransformation.None else PasswordVisualTransformation(),
        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password, imeAction = if (onDone != null) ImeAction.Done else ImeAction.Next),
        keyboardActions = KeyboardActions(onDone = { onDone?.invoke() }),
        trailingIcon = { IconButton(onClick = { visible = !visible }) { Icon(if (visible) Icons.Filled.VisibilityOff else Icons.Filled.Visibility, if (visible) "Verbergen" else "Anzeigen") } })
}

@Composable
private fun SignInStep(model: AppModel, stage: Stage.SignIn) {
    var login by rememberSaveable { mutableStateOf(stage.lastLogin ?: "") }
    var password by rememberSaveable { mutableStateOf("") }
    var code by rememberSaveable { mutableStateOf("") }
    var pool by rememberSaveable { mutableStateOf(stage.isPool) }
    val submit = { model.signIn(login, password, code.ifBlank { null }, if (pool) DeviceKind.Pool else DeviceKind.Personal) }
    if (stage.firstDevice) {
        Text("Wie wird dieses Gerät genutzt?", style = MaterialTheme.typography.titleSmall)
        SingleChoiceSegmentedButtonRow(Modifier.fillMaxWidth()) {
            SegmentedButton(!pool, { pool = false }, SegmentedButtonDefaults.itemShape(0, 2)) { Text("Persönlich") }
            SegmentedButton(pool, { pool = true }, SegmentedButtonDefaults.itemShape(1, 2)) { Text("Poolgerät") }
        }
        Secondary(if (pool) "Mehrere Personen teilen das Gerät (z. B. Tablet der Kolonne). Jede Person entsperrt mit einer eigenen PIN."
            else "Nur Sie nutzen dieses Telefon. Entsperren mit Fingerabdruck oder Gesicht.")
    }
    OutlinedTextField(login, { login = it }, label = { Text("Benutzername oder E-Mail") }, singleLine = true, modifier = Modifier.fillMaxWidth(),
        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Email, imeAction = ImeAction.Next))
    PasswordField(password, { password = it }, "Passwort", onDone = { submit() })
    if (stage.firstDevice) {
        OutlinedTextField(code, { code = it.uppercase() }, label = { Text("Einrichtungscode (falls vorhanden)") }, singleLine = true, modifier = Modifier.fillMaxWidth(),
            supportingText = { Text("Mit Code ist das Gerät sofort freigegeben, sonst nach Freigabe durch die Verwaltung.") })
    }
    Button(onClick = submit, enabled = login.isNotBlank() && password.isNotBlank(), modifier = Modifier.fillMaxWidth().height(52.dp)) { Text("Anmelden") }
    Row {
        TextButton(onClick = model::changeServer) { Text("Anderer Server") }
        if (stage.isPool) TextButton(onClick = model::cancelSignIn) { Text("Zur Benutzerauswahl") }
    }
}

@Composable
private fun TwoFactorStep(model: AppModel, stage: Stage.TwoFactor) {
    var code by remember { mutableStateOf("") }
    var recovery by remember { mutableStateOf(false) }
    OutlinedTextField(code, { code = it }, label = { Text(if (recovery) "Wiederherstellungscode" else "6-stelliger Code") }, singleLine = true,
        modifier = Modifier.fillMaxWidth(), keyboardOptions = KeyboardOptions(keyboardType = if (recovery) KeyboardType.Ascii else KeyboardType.NumberPassword))
    Button(onClick = { model.signIn(stage.login, stage.password, stage.enrollmentCode, stage.kind, code) },
        enabled = TwoFactorLogin.isComplete(code, recovery), modifier = Modifier.fillMaxWidth().height(52.dp)) { Text("Bestätigen") }
    TextButton(onClick = { recovery = !recovery; code = "" }) { Text(if (recovery) "Code aus der App eingeben" else "Wiederherstellungscode verwenden") }
    TextButton(onClick = model::cancelSignIn) { Text("Abbrechen") }
}

@Composable
private fun ChangePasswordStep(model: AppModel) {
    var current by remember { mutableStateOf("") }
    var new by remember { mutableStateOf("") }
    var repeated by remember { mutableStateOf("") }
    PasswordField(current, { current = it }, "Einmalpasswort")
    PasswordField(new, { new = it }, "Neues Passwort")
    PasswordField(repeated, { repeated = it }, "Neues Passwort wiederholen") { model.changePassword(current, new, repeated) }
    Secondary("Mindestens 12 Zeichen. Nicht dasselbe wie das Einmalpasswort.")
    Button(onClick = { model.changePassword(current, new, repeated) }, enabled = current.isNotBlank() && new.length >= 8, modifier = Modifier.fillMaxWidth().height(52.dp)) {
        Text("Passwort festlegen")
    }
}

@Composable
private fun PendingStep(model: AppModel) {
    SectionCard {
        RowItem("Freigabe angefragt", icon = Icons.Filled.HourglassTop, iconTint = tones.warning, iconBackground = tones.warningSoft,
            subtitle = "Die Verwaltung sieht das Gerät unter „Geräte“. Danach geht es hier weiter.")
    }
    Button(onClick = model::checkApproval, modifier = Modifier.fillMaxWidth().height(52.dp)) { Text("Erneut prüfen") }
    TextButton(onClick = model::cancelSignIn) { Text("Mit anderem Konto anmelden") }
}

@Composable
private fun ChoosePinStep(model: AppModel) {
    var pin by remember { mutableStateOf("") }
    var first by remember { mutableStateOf<String?>(null) }
    Text(if (first == null) "Neue PIN eingeben" else "PIN wiederholen", style = MaterialTheme.typography.titleMedium, modifier = Modifier.fillMaxWidth(), textAlign = TextAlign.Center)
    PinPad(pin, onChange = { value ->
        pin = value
        if (value.length == PinPolicy.LENGTH) {
            val f = first
            if (f == null) {
                PinPolicy.violation(value)?.let { model.message(it); pin = "" } ?: run { first = value; pin = "" }
            } else {
                model.choosePin(f, value)
                first = null
                pin = ""
            }
        }
    })
}

/** Ziffernblock mit 6 Punkten (wie PINPad auf iOS), große Tasten für Handschuhe. */
@Composable
fun PinPad(pin: String, onChange: (String) -> Unit, dark: Boolean = false) {
    val fg = if (dark) Color.White else MaterialTheme.colorScheme.onSurface
    Column(Modifier.fillMaxWidth(), horizontalAlignment = Alignment.CenterHorizontally) {
        Row(Modifier.padding(vertical = 20.dp), horizontalArrangement = Arrangement.spacedBy(16.dp)) {
            repeat(PinPolicy.LENGTH) { i ->
                Box(Modifier.size(16.dp).clip(CircleShape).background(if (i < pin.length) fg else fg.copy(alpha = 0.2f)))
            }
        }
        listOf(listOf("1", "2", "3"), listOf("4", "5", "6"), listOf("7", "8", "9"), listOf("", "0", "⌫")).forEach { row ->
            Row(horizontalArrangement = Arrangement.spacedBy(20.dp), modifier = Modifier.padding(vertical = 8.dp)) {
                row.forEach { key ->
                    Box(Modifier.size(76.dp).clip(CircleShape).background(if (key.isEmpty()) Color.Transparent else fg.copy(alpha = 0.08f))
                        .clickable(enabled = key.isNotEmpty()) {
                            when {
                                key == "⌫" -> onChange(pin.dropLast(1))
                                pin.length < PinPolicy.LENGTH -> onChange(pin + key)
                            }
                        }, contentAlignment = Alignment.Center) {
                        if (key == "⌫") Icon(Icons.AutoMirrored.Filled.Backspace, "Löschen", tint = fg)
                        else Text(key, color = fg, fontSize = 28.sp, fontWeight = FontWeight.Medium)
                    }
                }
            }
        }
    }
}

@Composable
private fun UserPickerScreen(model: AppModel, users: List<PoolUser>) {
    OnboardingFrame(model, "Wer arbeitet?", "Poolgerät – bitte Ihren Namen wählen und mit Ihrer PIN entsperren.") {
        SectionCard {
            users.forEachIndexed { index, u ->
                if (index > 0) RowDivider(72.dp)
                Row(Modifier.fillMaxWidth().clickable { model.pickUser(u) }.padding(16.dp), verticalAlignment = Alignment.CenterVertically) {
                    Avatar(initials(u.user.displayName), 48.dp)
                    Spacer(Modifier.width(12.dp))
                    Column(Modifier.weight(1f)) {
                        Text(u.user.displayName, style = MaterialTheme.typography.titleMedium)
                        Secondary(u.user.userName)
                    }
                }
            }
        }
        OutlinedButton(onClick = model::addPoolUser, modifier = Modifier.fillMaxWidth().height(52.dp)) {
            Icon(Icons.Filled.PersonAdd, null)
            Spacer(Modifier.width(8.dp))
            Text("Andere Person anmelden")
        }
    }
}

@Composable
private fun PinEntryScreen(model: AppModel, user: PoolUser) {
    var pin by remember { mutableStateOf("") }
    val error by model.error.collectAsState()
    Box(Modifier.fillMaxSize().background(Brush.verticalGradient(listOf(Navy, NavyLight)))) {
        Column(Modifier.fillMaxSize().windowInsetsPadding(WindowInsets.safeDrawing).padding(24.dp), horizontalAlignment = Alignment.CenterHorizontally) {
            Spacer(Modifier.height(32.dp))
            Avatar(initials(user.user.displayName), 72.dp, Color.White.copy(alpha = 0.15f), Color.White)
            Text(user.user.displayName, color = Color.White, style = MaterialTheme.typography.titleLarge, modifier = Modifier.padding(top = 12.dp))
            Text("PIN eingeben", color = Ice, style = MaterialTheme.typography.bodyMedium)
            error?.let { Text(it, color = Color(0xFFFFB4B4), modifier = Modifier.padding(top = 8.dp), textAlign = TextAlign.Center) }
            PinPad(pin, { value ->
                pin = value
                if (value.length == PinPolicy.LENGTH) {
                    model.unlockPool(value)
                    pin = ""
                }
            }, dark = true)
            TextButton(onClick = model::switchUser) { Text("Andere Person", color = Color.White) }
        }
    }
}

@Composable
private fun LockScreen(model: AppModel, unlock: () -> Unit) {
    Box(Modifier.fillMaxSize().background(Brush.verticalGradient(listOf(Navy, NavyLight))), contentAlignment = Alignment.Center) {
        Column(horizontalAlignment = Alignment.CenterHorizontally, modifier = Modifier.padding(32.dp)) {
            BrandMark(96.dp)
            Text("EstrichONE ist gesperrt", color = Color.White, style = MaterialTheme.typography.titleLarge, modifier = Modifier.padding(top = 20.dp))
            Text("Mit Fingerabdruck, Gesicht oder Gerätecode entsperren.", color = Ice, textAlign = TextAlign.Center, modifier = Modifier.padding(top = 6.dp))
            Button(onClick = unlock, modifier = Modifier.padding(top = 28.dp).height(52.dp),
                colors = ButtonDefaults.buttonColors(containerColor = Color.White, contentColor = Navy)) {
                Icon(Icons.Filled.Fingerprint, null)
                Spacer(Modifier.width(8.dp))
                Text("Entsperren")
            }
        }
    }
}
