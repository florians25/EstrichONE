package de.estrichone.core

import kotlinx.serialization.Serializable
import java.security.SecureRandom
import java.util.Base64
import javax.crypto.AEADBadTagException
import javax.crypto.Cipher
import javax.crypto.SecretKeyFactory
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.PBEKeySpec
import javax.crypto.spec.SecretKeySpec

/** Regeln für die Geräte-PIN auf Poolgeräten (docs/reengineering/11, Abschnitt 4.2). */
object PinPolicy {
    const val LENGTH = 6
    const val MAX_ATTEMPTS = 5

    /** Fehlermeldung oder `null`, wenn die PIN zulässig ist. */
    fun violation(pin: String): String? {
        if (pin.length != LENGTH || !pin.all { it in '0'..'9' }) return "Die PIN besteht aus genau 6 Ziffern."
        if (isTrivial(pin.map { it - '0' })) return "Diese PIN ist zu leicht zu erraten. Bitte keine Folgen wie 123456 oder 111111 verwenden."
        return null
    }

    /** Gleiche Ziffern, auf- oder absteigende Folgen, Wiederholungen kurzer Muster (121212, 123123). */
    fun isTrivial(digits: List<Int>): Boolean {
        val steps = digits.zipWithNext { a, b -> (b - a + 10) % 10 }
        if (steps.toSet().size == 1) return true
        for (period in listOf(2, 3)) {
            if (digits.size % period == 0 && digits.indices.all { digits[it] == digits[it % period] }) return true
        }
        return false
    }
}

/** Wann ein persönliches Gerät die App sperrt (Mehr › Sicherheit). Entsperren mit Fingerabdruck/Gesicht bzw. Gerätecode. */
enum class LockDelay(val seconds: Long, val title: String) {
    Immediately(0, "Sofort"), OneMinute(60, "Nach 1 Minute"), FiveMinutes(300, "Nach 5 Minuten"),
    FifteenMinutes(900, "Nach 15 Minuten"), OneHour(3600, "Nach 1 Stunde");

    /** Beim Zurückkehren sperren, wenn die App mindestens so lange im Hintergrund war. */
    fun shouldLock(backgroundSinceMillis: Long, nowMillis: Long): Boolean = (nowMillis - backgroundSinceMillis) / 1000 >= seconds
}

/** Poolgeräte sperren nach 5 Minuten Inaktivität bzw. sofort beim Verlassen der App. */
const val POOL_INACTIVITY_SECONDS = 300L

class WrongPinException : Exception("Die PIN ist falsch.")

/** Mit der PIN versiegeltes Geheimnis (Refresh-Token eines Benutzers auf einem Poolgerät, ADR 0012). */
@Serializable
data class SealedSecret(val salt: String, val iv: String, val data: String, val iterations: Int)

object PinVault {
    const val ITERATIONS = 210_000

    private val random = SecureRandom()

    fun deriveKey(pin: String, salt: ByteArray, iterations: Int): ByteArray {
        val spec = PBEKeySpec(pin.toCharArray(), salt, iterations, 256)
        return SecretKeyFactory.getInstance("PBKDF2WithHmacSHA256").generateSecret(spec).encoded
    }

    fun seal(secret: String, pin: String, iterations: Int = ITERATIONS): SealedSecret {
        val salt = ByteArray(16).also(random::nextBytes)
        val iv = ByteArray(12).also(random::nextBytes)
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.ENCRYPT_MODE, SecretKeySpec(deriveKey(pin, salt, iterations), "AES"), GCMParameterSpec(128, iv))
        val data = cipher.doFinal(secret.toByteArray(Charsets.UTF_8))
        val b64 = Base64.getEncoder()
        return SealedSecret(b64.encodeToString(salt), b64.encodeToString(iv), b64.encodeToString(data), iterations)
    }

    fun open(sealed: SealedSecret, pin: String): String {
        val b64 = Base64.getDecoder()
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.DECRYPT_MODE, SecretKeySpec(deriveKey(pin, b64.decode(sealed.salt), sealed.iterations), "AES"), GCMParameterSpec(128, b64.decode(sealed.iv)))
        return try {
            String(cipher.doFinal(b64.decode(sealed.data)), Charsets.UTF_8)
        } catch (_: AEADBadTagException) {
            throw WrongPinException()
        }
    }
}
