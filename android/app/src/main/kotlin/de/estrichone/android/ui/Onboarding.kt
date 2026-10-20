package de.estrichone.android.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import de.estrichone.android.AppModel
import de.estrichone.android.Stage

/** Einstieg: je nach Stand Einrichtung, Anmeldung, Freigabe oder die App. */
@Composable
fun Root(model: AppModel) {
    val stage by model.stage.collectAsState()
    when (val s = stage) {
        Stage.Starting -> Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) { CircularProgressIndicator() }
        is Stage.Server -> OnboardingFrame(model) { ServerStep(model, s.address) }
        is Stage.SignIn -> OnboardingFrame(model) { SignInStep(model, s) }
        is Stage.Pending -> OnboardingFrame(model) { PendingStep(model, s.deviceName) }
        Stage.Ready -> MainScreen(model)
    }
}

@Composable
private fun OnboardingFrame(model: AppModel, content: @Composable () -> Unit) {
    val busy by model.busy.collectAsState()
    val error by model.error.collectAsState()
    Box(Modifier.fillMaxSize().safeDrawingPadding().imePadding(), contentAlignment = Alignment.Center) {
        Card(Modifier.widthIn(max = 460.dp).fillMaxWidth().padding(20.dp)) {
            Column(
                Modifier.padding(24.dp).verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(14.dp),
            ) {
                Text("EstrichONE", style = MaterialTheme.typography.headlineMedium, fontWeight = FontWeight.SemiBold,
                    color = MaterialTheme.colorScheme.primary)
                content()
                if (busy) LinearProgressIndicator(Modifier.fillMaxWidth())
                error?.let { Text(it, color = MaterialTheme.colorScheme.error, modifier = Modifier.semantics { contentDescription = "Fehler: $it" }) }
            }
        }
    }
}

@Composable
private fun ServerStep(model: AppModel, initial: String) {
    var address by rememberSaveable { mutableStateOf(initial) }
    Text("Mit welchem EstrichONE-Server möchten Sie arbeiten? Die Adresse erhalten Sie von Ihrer IT.")
    OutlinedTextField(
        value = address, onValueChange = { address = it }, label = { Text("Serveradresse") }, placeholder = { Text("estrich.firma.de") },
        singleLine = true, modifier = Modifier.fillMaxWidth(),
        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri, imeAction = ImeAction.Go),
        keyboardActions = KeyboardActions(onGo = { model.connect(address) }),
    )
    Button(onClick = { model.connect(address) }, enabled = address.isNotBlank(), modifier = Modifier.fillMaxWidth()) { Text("Verbinden") }
}

@Composable
private fun SignInStep(model: AppModel, stage: Stage.SignIn) {
    var login by rememberSaveable { mutableStateOf(stage.lastLogin ?: "") }
    var password by rememberSaveable { mutableStateOf("") }
    var code by rememberSaveable { mutableStateOf("") }
    Text(stage.serverName, fontWeight = FontWeight.SemiBold)
    TextButton(onClick = model::changeServer) { Text("Anderen Server wählen") }
    OutlinedTextField(login, { login = it }, label = { Text("Anmeldename oder E-Mail") }, singleLine = true, modifier = Modifier.fillMaxWidth(),
        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Email, imeAction = ImeAction.Next))
    OutlinedTextField(password, { password = it }, label = { Text("Passwort") }, singleLine = true, modifier = Modifier.fillMaxWidth(),
        visualTransformation = PasswordVisualTransformation(),
        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password, imeAction = ImeAction.Next))
    OutlinedTextField(code, { code = it }, label = { Text("Einrichtungscode (optional)") }, singleLine = true, modifier = Modifier.fillMaxWidth(),
        supportingText = { Text("Mit Code ist das Gerät sofort freigegeben, sonst prüft die IT.") })
    Button(
        onClick = { model.signIn(login, password, code.ifBlank { null }); password = "" },
        enabled = login.isNotBlank() && password.isNotEmpty(), modifier = Modifier.fillMaxWidth(),
    ) { Text("Anmelden") }
}

@Composable
private fun PendingStep(model: AppModel, deviceName: String) {
    Text("Gerät wartet auf Freigabe", style = MaterialTheme.typography.titleMedium)
    Text("„$deviceName“ ist registriert. Sobald die IT-Administration es im IT Center freigibt, können Sie loslegen.")
    Button(onClick = model::checkApproval, modifier = Modifier.fillMaxWidth()) { Text("Erneut prüfen") }
    OutlinedButton(onClick = model::signOut, modifier = Modifier.fillMaxWidth()) { Text("Abmelden") }
}
