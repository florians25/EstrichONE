package de.estrichone.android

import android.app.Application
import android.os.Build
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import de.estrichone.android.data.KeystoreDeviceKeys
import de.estrichone.android.data.SecureSessionStore
import de.estrichone.core.ApiException
import de.estrichone.core.DeviceDescription
import de.estrichone.core.DeviceKind
import de.estrichone.core.DeviceStatus
import de.estrichone.core.EstrichOneApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.launch

/** Ablauf der Ersteinrichtung (docs/reengineering/10): Server → Anmeldung → Gerät gebunden → ggf. Freigabe. */
sealed interface Stage {
    data object Starting : Stage
    data class Server(val address: String = "") : Stage
    data class SignIn(val serverName: String, val lastLogin: String?) : Stage
    data class Pending(val deviceName: String) : Stage
    data object Ready : Stage
}

/** Anmeldung und Sitzung der App; die Seiten nutzen [api]. */
class AppModel(application: Application) : AndroidViewModel(application) {
    private val sessions = SecureSessionStore(application)
    private val keys = KeystoreDeviceKeys()
    private var client: EstrichOneApi? = null

    private val _stage = MutableStateFlow<Stage>(Stage.Starting)
    val stage: StateFlow<Stage> = _stage

    private val _busy = MutableStateFlow(false)
    val busy: StateFlow<Boolean> = _busy

    private val _error = MutableStateFlow<String?>(null)
    val error: StateFlow<String?> = _error

    val api: EstrichOneApi get() = client ?: error("Keine Verbindung zum Server")

    val userName: String get() = sessions.user?.displayName ?: ""

    private val phone = DeviceDescription(
        name = "${Build.MANUFACTURER.replaceFirstChar { it.uppercase() }} ${Build.MODEL}".take(100),
        model = Build.MODEL,
        osVersion = Build.VERSION.RELEASE,
        appVersion = BuildConfig.VERSION_NAME,
    )

    init {
        viewModelScope.launch {
            val server = sessions.server
            if (server == null) {
                _stage.value = Stage.Server()
                return@launch
            }
            client = EstrichOneApi(server, sessions, keys)
            _stage.value = try {
                if (api.restore()) stageForDevice() else signInStage()
            } catch (e: Exception) {
                // Server nicht erreichbar oder Gerät gesperrt: Anmeldung anzeigen, Grund sichtbar machen.
                if (e !is ApiException && e !is IllegalStateException) throw e
                _error.value = e.message
                Stage.SignIn(server, sessions.user?.userName)
            }
        }
    }

    fun clearError() {
        _error.value = null
    }

    fun connect(address: String) = run {
        val candidate = EstrichOneApi(address, sessions, keys)
        val info = candidate.serverInfo()
        if (info.setupRequired) throw IllegalStateException("Der Server ist noch nicht eingerichtet. Bitte zuerst die Ersteinrichtung im Browser abschließen.")
        if (sessions.server != candidate.baseUrl.toString()) {
            // Anderer Server: alte Anmeldung und Gerätebindung gelten dort nicht.
            sessions.refreshToken = null
            sessions.deviceId = null
            sessions.user = null
        }
        sessions.server = candidate.baseUrl.toString()
        client = candidate
        _stage.value = Stage.SignIn(info.serverName, sessions.user?.userName)
    }

    fun changeServer() {
        _stage.value = Stage.Server(sessions.server ?: "")
    }

    fun signIn(login: String, password: String, enrollmentCode: String?) = run {
        api.signIn(login, password, phone.name)
        try {
            api.bindDevice(DeviceKind.Personal, enrollmentCode, phone)
        } catch (e: ApiException) {
            if (e.code != "DEVICE_NOT_FOUND" && e.code != "DEVICE_PROOF_INVALID") throw e
            // In der Verwaltung entfernt: neuer Schlüssel, neue Registrierung.
            sessions.deviceId = null
            keys.reset()
            api.bindDevice(DeviceKind.Personal, enrollmentCode, phone)
        }
        _stage.value = stageForDevice()
    }

    fun checkApproval() = run { _stage.value = stageForDevice() }

    fun signOut() = run {
        api.signOut()
        _stage.value = signInStage()
    }

    private suspend fun stageForDevice(): Stage {
        val device = api.myDevice()
        return when (device.status) {
            DeviceStatus.Active -> Stage.Ready
            DeviceStatus.Pending -> Stage.Pending(device.name)
            DeviceStatus.Locked -> throw IllegalStateException("Dieses Gerät ist gesperrt. Bitte an die IT-Administration wenden.")
            DeviceStatus.Retired -> throw IllegalStateException("Dieses Gerät wurde aus EstrichONE entfernt.")
        }
    }

    private fun signInStage(): Stage = Stage.SignIn(sessions.server ?: "", sessions.user?.userName)

    private fun run(action: suspend () -> Unit) {
        viewModelScope.launch {
            _busy.value = true
            _error.value = null
            try {
                action()
            } catch (e: ApiException) {
                _error.value = e.message
            } catch (e: IllegalArgumentException) {
                _error.value = e.message
            } catch (e: IllegalStateException) {
                _error.value = e.message
            } finally {
                _busy.value = false
            }
        }
    }
}
