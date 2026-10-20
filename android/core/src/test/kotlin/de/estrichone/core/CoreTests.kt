package de.estrichone.core

import org.junit.jupiter.api.Assertions.assertArrayEquals
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.DisplayName
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.assertThrows
import java.math.BigInteger
import java.security.Signature

class CoreTests {
    @Test
    @DisplayName("Serveradresse wird normalisiert")
    fun serverAddressIsNormalized() {
        assertEquals("https://estrich.firma.de/", EstrichOneApi.normalize("estrich.firma.de").toString())
        assertEquals("https://estrich.firma.de:8443/", EstrichOneApi.normalize(" https://estrich.firma.de:8443/api/v1/ ").toString())
        assertEquals("http://192.168.1.10:8080/", EstrichOneApi.normalize("http://192.168.1.10:8080").toString())
        assertEquals("Die Serveradresse ist ungültig.", assertThrows<IllegalArgumentException> { EstrichOneApi.normalize("ftp://x") }.message)
    }

    @Test
    @DisplayName("Gerätesignatur ist r‖s mit je 32 Byte und prüfbar")
    fun deviceSignatureIsRawAndVerifiable() {
        val keys = SoftwareDeviceKeys()
        repeat(20) {
            val message = "EstrichONE refresh v1\ntoken-$it".toByteArray()
            val der = keys.signDer(message)
            val raw = DeviceProof.derToRaw(der)
            assertEquals(64, raw.size)
            // Rohformat zurück nach DER (Java prüft nur DER) und mit dem öffentlichen Schlüssel verifizieren.
            val verifier = Signature.getInstance("SHA256withECDSA")
            verifier.initVerify(java.security.KeyFactory.getInstance("EC").generatePublic(java.security.spec.X509EncodedKeySpec(keys.publicKeyDer)))
            verifier.update(message)
            assertEquals(true, verifier.verify(rawToDer(raw)))
        }
    }

    @Test
    @DisplayName("Kurze Ganzzahlen werden links aufgefüllt")
    fun shortIntegersArePadded() {
        // r = 1, s = 2 (minimal kodiert)
        val der = byteArrayOf(0x30, 0x06, 0x02, 0x01, 0x01, 0x02, 0x01, 0x02)
        val raw = DeviceProof.derToRaw(der)
        assertArrayEquals(ByteArray(31) + byteArrayOf(1) + ByteArray(31) + byteArrayOf(2), raw)
    }

    private fun rawToDer(raw: ByteArray): ByteArray {
        fun integer(part: ByteArray): ByteArray {
            val bytes = BigInteger(1, part).toByteArray()
            return byteArrayOf(0x02, bytes.size.toByte()) + bytes
        }
        val body = integer(raw.copyOfRange(0, 32)) + integer(raw.copyOfRange(32, 64))
        return byteArrayOf(0x30, body.size.toByte()) + body
    }
}
