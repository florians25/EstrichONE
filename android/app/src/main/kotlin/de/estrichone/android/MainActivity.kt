package de.estrichone.android

import android.Manifest
import android.os.Build
import android.os.Bundle
import android.view.KeyEvent
import android.view.WindowManager
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.contract.ActivityResultContracts
import androidx.biometric.BiometricManager
import androidx.biometric.BiometricPrompt
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.core.content.ContextCompat
import androidx.fragment.app.FragmentActivity
import de.estrichone.android.ui.Appearance
import de.estrichone.android.ui.EstrichTheme
import de.estrichone.android.ui.Root
import de.estrichone.android.ui.VolumeShutter

/** Einzige Activity: Compose-Oberfläche, App-Sperre per BiometricPrompt, Lautstärketaste als Kamera-Auslöser. */
class MainActivity : FragmentActivity() {
    private val model: AppModel get() = (application as EstrichApp).model
    private var lastInteraction = System.currentTimeMillis()

    private val notificationPermission = registerForActivityResult(ActivityResultContracts.RequestPermission()) {}

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent {
            val look by model.look.collectAsState()
            val stage by model.stage.collectAsState()
            EstrichTheme(Appearance.entries.firstOrNull { it.name == look.first } ?: Appearance.System, look.second) {
                Root(model, unlock = ::unlock)
            }
            LaunchedEffect(stage) {
                if (stage == Stage.Locked) unlock()
                if (stage == Stage.Ready && Build.VERSION.SDK_INT >= 33 &&
                    ContextCompat.checkSelfPermission(this@MainActivity, Manifest.permission.POST_NOTIFICATIONS) != android.content.pm.PackageManager.PERMISSION_GRANTED) {
                    notificationPermission.launch(Manifest.permission.POST_NOTIFICATIONS)
                }
            }
        }
    }

    override fun onStart() {
        super.onStart()
        model.onForeground()
    }

    override fun onStop() {
        super.onStop()
        model.onBackground()
    }

    override fun onResume() {
        super.onResume()
        // Inhalte nicht in der App-Übersicht zeigen, solange die Sperre aktiv ist.
        if (model.prefs.lockEnabled) window.addFlags(WindowManager.LayoutParams.FLAG_SECURE) else window.clearFlags(WindowManager.LayoutParams.FLAG_SECURE)
        model.onInactive((System.currentTimeMillis() - lastInteraction) / 1000)
    }

    override fun onUserInteraction() {
        super.onUserInteraction()
        val now = System.currentTimeMillis()
        model.onInactive((now - lastInteraction) / 1000)
        lastInteraction = now
    }

    override fun onKeyDown(keyCode: Int, event: KeyEvent?): Boolean {
        val shutter = VolumeShutter.listener
        if (shutter != null && (keyCode == KeyEvent.KEYCODE_VOLUME_DOWN || keyCode == KeyEvent.KEYCODE_VOLUME_UP)) {
            shutter()
            return true
        }
        return super.onKeyDown(keyCode, event)
    }

    /** Entsperren mit Fingerabdruck/Gesicht (Klasse 3) bzw. Gerätecode. */
    private fun unlock() {
        val authenticators = if (model.prefs.biometricUnlock) {
            BiometricManager.Authenticators.BIOMETRIC_STRONG or BiometricManager.Authenticators.DEVICE_CREDENTIAL
        } else {
            BiometricManager.Authenticators.DEVICE_CREDENTIAL
        }
        if (BiometricManager.from(this).canAuthenticate(authenticators) != BiometricManager.BIOMETRIC_SUCCESS) {
            // Ohne eingerichtete Gerätesperre lässt sich nicht prüfen: nicht aussperren.
            model.unlocked()
            return
        }
        val prompt = BiometricPrompt(this, ContextCompat.getMainExecutor(this), object : BiometricPrompt.AuthenticationCallback() {
            override fun onAuthenticationSucceeded(result: BiometricPrompt.AuthenticationResult) = model.unlocked()
        })
        prompt.authenticate(
            BiometricPrompt.PromptInfo.Builder().setTitle("EstrichONE entsperren").setSubtitle("Ihre Baustellendaten sind geschützt")
                .setAllowedAuthenticators(authenticators).build(),
        )
    }
}
