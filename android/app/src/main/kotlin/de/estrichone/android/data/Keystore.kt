package de.estrichone.android.data

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import de.estrichone.core.DeviceKeys
import de.estrichone.core.SessionStore
import de.estrichone.core.UserSummary
import de.estrichone.core.EstrichOneApi
import java.security.KeyPairGenerator
import java.security.KeyStore
import java.security.PrivateKey
import java.security.Signature
import java.security.spec.ECGenParameterSpec
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

private const val PROVIDER = "AndroidKeyStore"

/** Geräteschlüssel im Android Keystore (P-256, nicht exportierbar); StrongBox wird genutzt, wenn vorhanden. */
class KeystoreDeviceKeys(private val alias: String = "estrichone-device") : DeviceKeys {
    private val store: KeyStore = KeyStore.getInstance(PROVIDER).apply { load(null) }

    init {
        if (!store.containsAlias(alias)) generate()
    }

    override val publicKeyDer: ByteArray get() = store.getCertificate(alias).publicKey.encoded

    override fun signDer(message: ByteArray): ByteArray = Signature.getInstance("SHA256withECDSA").run {
        initSign(store.getKey(alias, null) as PrivateKey)
        update(message)
        sign()
    }

    /** Neues Schlüsselpaar, z. B. nachdem das Gerät in der Verwaltung entfernt wurde. */
    fun reset() {
        store.deleteEntry(alias)
        generate()
    }

    private fun generate() {
        fun spec(strongBox: Boolean) = KeyGenParameterSpec.Builder(alias, KeyProperties.PURPOSE_SIGN)
            .setAlgorithmParameterSpec(ECGenParameterSpec("secp256r1"))
            .setDigests(KeyProperties.DIGEST_SHA256)
            .setIsStrongBoxBacked(strongBox)
            .build()
        val generator = KeyPairGenerator.getInstance(KeyProperties.KEY_ALGORITHM_EC, PROVIDER)
        try {
            generator.initialize(spec(strongBox = true))
            generator.generateKeyPair()
        } catch (_: Exception) {
            generator.initialize(spec(strongBox = false))
            generator.generateKeyPair()
        }
    }
}

/**
 * Anmeldung des Geräts: Refresh-Token AES-GCM-verschlüsselt mit einem Keystore-Schlüssel in den SharedPreferences
 * (EncryptedSharedPreferences ist abgekündigt, docs/reengineering/08, Abschnitt 2).
 */
class SecureSessionStore(context: Context) : SessionStore {
    private val prefs = context.getSharedPreferences("estrichone-session", Context.MODE_PRIVATE)
    private val alias = "estrichone-session-key"

    var server: String?
        get() = prefs.getString("server", null)
        set(value) = prefs.edit().putString("server", value).apply()

    override var refreshToken: String?
        get() = prefs.getString("refresh", null)?.let { runCatching { decrypt(it) }.getOrNull() }
        set(value) = prefs.edit().putString("refresh", value?.let(::encrypt)).apply()

    override var deviceId: String?
        get() = prefs.getString("device", null)
        set(value) = prefs.edit().putString("device", value).apply()

    override var user: UserSummary?
        get() = prefs.getString("user", null)?.let { runCatching { EstrichOneApi.json.decodeFromString(UserSummary.serializer(), it) }.getOrNull() }
        set(value) = prefs.edit().putString("user", value?.let { EstrichOneApi.json.encodeToString(UserSummary.serializer(), it) }).apply()

    private fun key(): SecretKey {
        val store = KeyStore.getInstance(PROVIDER).apply { load(null) }
        (store.getKey(alias, null) as? SecretKey)?.let { return it }
        return KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, PROVIDER).run {
            init(
                KeyGenParameterSpec.Builder(alias, KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
                    .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                    .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                    .build(),
            )
            generateKey()
        }
    }

    private fun encrypt(text: String): String {
        val cipher = Cipher.getInstance("AES/GCM/NoPadding").apply { init(Cipher.ENCRYPT_MODE, key()) }
        val sealed = cipher.iv + cipher.doFinal(text.toByteArray(Charsets.UTF_8))
        return Base64.encodeToString(sealed, Base64.NO_WRAP)
    }

    private fun decrypt(value: String): String {
        val sealed = Base64.decode(value, Base64.NO_WRAP)
        val cipher = Cipher.getInstance("AES/GCM/NoPadding").apply {
            init(Cipher.DECRYPT_MODE, key(), GCMParameterSpec(128, sealed, 0, 12))
        }
        return String(cipher.doFinal(sealed, 12, sealed.size - 12), Charsets.UTF_8)
    }
}
