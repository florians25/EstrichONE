package de.estrichone.core

import java.math.BigInteger
import java.security.KeyPair
import java.security.KeyPairGenerator
import java.security.Signature
import java.security.spec.ECGenParameterSpec
import java.util.Base64

/**
 * Geräteschlüssel (docs/reengineering/11): ECDSA P-256. Der Server erwartet den öffentlichen Schlüssel als
 * SubjectPublicKeyInfo (DER, Base64) und Signaturen im Rohformat r‖s (64 Byte). Auf dem Telefon liegt der private
 * Schlüssel im Android Keystore und verlässt das Gerät nie.
 */
interface DeviceKeys {
    val publicKeyDer: ByteArray

    /** Signatur im DER-Format, wie sie `java.security.Signature` liefert. */
    fun signDer(message: ByteArray): ByteArray
}

object DeviceProof {
    fun refresh(refreshToken: String, keys: DeviceKeys): String = sign("EstrichONE refresh v1\n$refreshToken", keys)

    fun attach(accessToken: String, keys: DeviceKeys): String = sign("EstrichONE attach v1\n$accessToken", keys)

    private fun sign(text: String, keys: DeviceKeys): String =
        Base64.getEncoder().encodeToString(derToRaw(keys.signDer(text.toByteArray(Charsets.UTF_8))))

    /** ASN.1 SEQUENCE { INTEGER r, INTEGER s } → r‖s mit je 32 Byte. */
    fun derToRaw(der: ByteArray): ByteArray {
        require(der.size >= 8 && der[0] == 0x30.toByte()) { "Keine DER-Signatur." }
        var index = if (der[1].toInt() and 0x80 != 0) 2 + (der[1].toInt() and 0x7f) else 2
        fun integer(): ByteArray {
            require(der[index] == 0x02.toByte()) { "Ungültige DER-Signatur." }
            val length = der[index + 1].toInt() and 0xff
            val value = der.copyOfRange(index + 2, index + 2 + length)
            index += 2 + length
            return BigInteger(1, value).toByteArray().let { bytes ->
                val trimmed = if (bytes.size > 32) bytes.copyOfRange(bytes.size - 32, bytes.size) else bytes
                ByteArray(32 - trimmed.size) + trimmed
            }
        }
        return integer() + integer()
    }
}

/** Software-Schlüssel für Tests und Emulatoren ohne Keystore. */
class SoftwareDeviceKeys(private val pair: KeyPair = generate()) : DeviceKeys {
    override val publicKeyDer: ByteArray get() = pair.public.encoded

    override fun signDer(message: ByteArray): ByteArray =
        Signature.getInstance("SHA256withECDSA").run {
            initSign(pair.private)
            update(message)
            sign()
        }

    companion object {
        fun generate(): KeyPair = KeyPairGenerator.getInstance("EC").run {
            initialize(ECGenParameterSpec("secp256r1"))
            generateKeyPair()
        }
    }
}
