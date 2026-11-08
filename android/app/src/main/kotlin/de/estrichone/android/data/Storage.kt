package de.estrichone.android.data

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import de.estrichone.core.EstrichOneApi
import de.estrichone.core.FileVault
import de.estrichone.core.LocalEntity
import de.estrichone.core.OutboxOperation
import de.estrichone.core.PendingUpload
import de.estrichone.core.SealedSecret
import de.estrichone.core.StorageBackend
import de.estrichone.core.SyncState
import de.estrichone.core.UserSummary
import kotlinx.serialization.Serializable
import kotlinx.serialization.builtins.ListSerializer
import kotlinx.serialization.json.JsonObject
import net.zetetic.database.sqlcipher.SQLiteDatabase
import java.io.File
import java.security.KeyStore
import java.security.SecureRandom
import java.util.concurrent.locks.ReentrantLock
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec
import kotlin.concurrent.withLock

/**
 * Datenschlüssel des Geräts bzw. Benutzers: 32 Zufallsbytes, verschlüsselt mit einem nicht exportierbaren AES-Schlüssel im
 * Android Keystore. Daraus: Passwort der SQLCipher-Datenbank und Schlüssel der Dateiablage (schnelles AES-GCM in Software).
 */
class DataKeys(context: Context) {
    private val prefs = context.getSharedPreferences("estrichone-keys", Context.MODE_PRIVATE)
    private val alias = "estrichone-data-wrap"

    fun key(name: String): ByteArray {
        prefs.getString(name, null)?.let { stored -> runCatching { unwrap(stored) }.getOrNull()?.let { return it } }
        val fresh = ByteArray(32).also(SecureRandom()::nextBytes)
        prefs.edit().putString(name, wrap(fresh)).apply()
        return fresh
    }

    fun forget(name: String) = prefs.edit().remove(name).apply()

    fun forgetAll() = prefs.edit().clear().apply()

    private fun wrapKey(): SecretKey {
        val store = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (store.getKey(alias, null) as? SecretKey)?.let { return it }
        return KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore").run {
            init(KeyGenParameterSpec.Builder(alias, KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).build())
            generateKey()
        }
    }

    private fun wrap(bytes: ByteArray): String {
        val cipher = Cipher.getInstance("AES/GCM/NoPadding").apply { init(Cipher.ENCRYPT_MODE, wrapKey()) }
        return Base64.encodeToString(cipher.iv + cipher.doFinal(bytes), Base64.NO_WRAP)
    }

    private fun unwrap(text: String): ByteArray {
        val sealed = Base64.decode(text, Base64.NO_WRAP)
        val cipher = Cipher.getInstance("AES/GCM/NoPadding").apply { init(Cipher.DECRYPT_MODE, wrapKey(), GCMParameterSpec(128, sealed, 0, 12)) }
        return cipher.doFinal(sealed, 12, sealed.size - 12)
    }
}

/** Lokale Datenbank (SQLCipher, docs/reengineering/08 Abschnitt 2): Datensätze als JSON wie im Feed, Outbox, Uploads, Metadaten. */
class SqlStorage(file: File, key: ByteArray) : StorageBackend {
    private val db: SQLiteDatabase
    private val lock = ReentrantLock()

    init {
        System.loadLibrary("sqlcipher")
        file.parentFile?.mkdirs()
        val password = key.joinToString("") { "%02x".format(it) }
        db = SQLiteDatabase.openOrCreateDatabase(file, password, null, null)
        db.execSQL("CREATE TABLE IF NOT EXISTS entities (type TEXT NOT NULL, id TEXT NOT NULL, bauakte TEXT, state TEXT NOT NULL, json TEXT NOT NULL, PRIMARY KEY (type, id))")
        db.execSQL("CREATE INDEX IF NOT EXISTS entities_bauakte ON entities (bauakte)")
        db.execSQL("CREATE TABLE IF NOT EXISTS outbox (seq INTEGER PRIMARY KEY AUTOINCREMENT, json TEXT NOT NULL)")
        db.execSQL("CREATE TABLE IF NOT EXISTS uploads (fileId TEXT PRIMARY KEY, json TEXT NOT NULL)")
        db.execSQL("CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT)")
    }

    private val json = EstrichOneApi.json

    override fun <T> transaction(block: () -> T): T = lock.withLock {
        db.beginTransaction()
        try {
            val result = block()
            db.setTransactionSuccessful()
            result
        } finally {
            db.endTransaction()
        }
    }

    private fun entityRows(sql: String, vararg args: String): List<LocalEntity> = lock.withLock {
        db.rawQuery(sql, arrayOf(*args)).use { c ->
            buildList {
                while (c.moveToNext()) {
                    val data = runCatching { json.parseToJsonElement(c.getString(3)) as JsonObject }.getOrNull() ?: continue
                    add(LocalEntity(c.getString(0), c.getString(1), data, SyncState.valueOf(c.getString(2))))
                }
            }
        }
    }

    override fun getEntity(type: String, id: String) = entityRows("SELECT type, id, state, json FROM entities WHERE type = ? AND id = ?", type, id).firstOrNull()
    override fun entities(type: String) = entityRows("SELECT type, id, state, json FROM entities WHERE type = ?", type)
    override fun entitiesOfBauakte(bauakteId: String) = entityRows("SELECT type, id, state, json FROM entities WHERE bauakte = ?", bauakteId)

    override fun putEntity(entity: LocalEntity, bauakteId: String?) = lock.withLock {
        db.execSQL("INSERT OR REPLACE INTO entities (type, id, bauakte, state, json) VALUES (?, ?, ?, ?, ?)",
            arrayOf<Any?>(entity.type, entity.id, bauakteId, entity.syncState.name, json.encodeToString(JsonObject.serializer(), entity.data)))
    }

    override fun deleteEntity(type: String, id: String) = lock.withLock {
        db.execSQL("DELETE FROM entities WHERE type = ? AND id = ?", arrayOf<Any?>(type, id))
    }

    override fun outbox(): List<OutboxOperation> = lock.withLock {
        db.rawQuery("SELECT seq, json FROM outbox ORDER BY seq", arrayOf<String>()).use { c ->
            buildList { while (c.moveToNext()) add(json.decodeFromString(OutboxOperation.serializer(), c.getString(1)).copy(seq = c.getLong(0))) }
        }
    }

    override fun insertOperation(operation: OutboxOperation): OutboxOperation = lock.withLock {
        db.execSQL("INSERT INTO outbox (json) VALUES (?)", arrayOf<Any?>(json.encodeToString(OutboxOperation.serializer(), operation)))
        val seq = db.rawQuery("SELECT last_insert_rowid()", arrayOf<String>()).use { c -> c.moveToFirst(); c.getLong(0) }
        operation.copy(seq = seq)
    }

    override fun updateOperation(operation: OutboxOperation) = lock.withLock {
        db.execSQL("UPDATE outbox SET json = ? WHERE seq = ?", arrayOf<Any?>(json.encodeToString(OutboxOperation.serializer(), operation), operation.seq))
    }

    override fun deleteOperation(seq: Long) = lock.withLock { db.execSQL("DELETE FROM outbox WHERE seq = ?", arrayOf<Any?>(seq)) }

    override fun uploads(): List<PendingUpload> = lock.withLock {
        db.rawQuery("SELECT json FROM uploads", arrayOf<String>()).use { c ->
            buildList { while (c.moveToNext()) add(json.decodeFromString(PendingUpload.serializer(), c.getString(0))) }
        }
    }

    override fun putUpload(upload: PendingUpload) = lock.withLock {
        db.execSQL("INSERT OR REPLACE INTO uploads (fileId, json) VALUES (?, ?)", arrayOf<Any?>(upload.fileId, json.encodeToString(PendingUpload.serializer(), upload)))
    }

    override fun deleteUpload(fileId: String) = lock.withLock { db.execSQL("DELETE FROM uploads WHERE fileId = ?", arrayOf<Any?>(fileId)) }

    override fun meta(key: String): String? = lock.withLock {
        db.rawQuery("SELECT value FROM meta WHERE key = ?", arrayOf(key)).use { c -> if (c.moveToFirst() && !c.isNull(0)) c.getString(0) else null }
    }

    override fun setMeta(key: String, value: String?) = lock.withLock {
        if (value == null) db.execSQL("DELETE FROM meta WHERE key = ?", arrayOf<Any?>(key))
        else db.execSQL("INSERT OR REPLACE INTO meta (key, value) VALUES (?, ?)", arrayOf<Any?>(key, value))
    }

    override fun clear() = lock.withLock {
        listOf("entities", "outbox", "uploads", "meta").forEach { db.execSQL("DELETE FROM $it") }
    }

    fun close() = lock.withLock { db.close() }
}

/** Fotos und Dokumente im privaten Speicher des Benutzers, AES-GCM-verschlüsselt mit dem Datenschlüssel. */
class DiskFileVault(private val directory: File, key: ByteArray) : FileVault {
    private val secret = SecretKeySpec(key, "AES")
    private val random = SecureRandom()

    init {
        directory.mkdirs()
    }

    private fun file(id: String) = File(directory, "${id.lowercase()}.sealed")

    override fun write(fileId: String, bytes: ByteArray) {
        val iv = ByteArray(12).also(random::nextBytes)
        val cipher = Cipher.getInstance("AES/GCM/NoPadding").apply { init(Cipher.ENCRYPT_MODE, secret, GCMParameterSpec(128, iv)) }
        val target = file(fileId)
        val temp = File(directory, "${target.name}.tmp")
        temp.writeBytes(iv + cipher.doFinal(bytes))
        temp.renameTo(target)
    }

    override fun read(fileId: String): ByteArray? {
        val sealed = file(fileId).takeIf { it.exists() }?.readBytes() ?: return null
        return runCatching {
            Cipher.getInstance("AES/GCM/NoPadding").apply { init(Cipher.DECRYPT_MODE, secret, GCMParameterSpec(128, sealed, 0, 12)) }
                .doFinal(sealed, 12, sealed.size - 12)
        }.getOrNull()
    }

    override fun remove(fileId: String) {
        file(fileId).delete()
    }
}

/** Benutzer eines Poolgeräts: Anmeldung mit der PIN versiegelt (ADR 0012), Fehlversuche gezählt. */
@Serializable
data class PoolUser(val user: UserSummary, val sealed: SealedSecret, val failedAttempts: Int = 0, val lastUsed: Long = 0)

/** Geräteweite Einstellungen: Server, Geräteart, Benutzer des Poolgeräts, Sperre, Demomodus. */
class DevicePrefs(context: Context) {
    private val prefs = context.getSharedPreferences("estrichone-device", Context.MODE_PRIVATE)
    private val json = EstrichOneApi.json

    var isPool: Boolean
        get() = prefs.getBoolean("pool", false)
        set(value) = prefs.edit().putBoolean("pool", value).apply()

    var demo: Boolean
        get() = prefs.getBoolean("demo", false)
        set(value) = prefs.edit().putBoolean("demo", value).apply()

    var poolUsers: List<PoolUser>
        get() = prefs.getString("poolUsers", null)?.let { runCatching { json.decodeFromString(ListSerializer(PoolUser.serializer()), it) }.getOrNull() } ?: emptyList()
        set(value) = prefs.edit().putString("poolUsers", json.encodeToString(ListSerializer(PoolUser.serializer()), value)).apply()

    var biometricUnlock: Boolean
        get() = prefs.getBoolean("lockBiometric", true)
        set(value) = prefs.edit().putBoolean("lockBiometric", value).apply()

    var lockDelaySeconds: Long
        get() = prefs.getLong("lockDelay", 900)
        set(value) = prefs.edit().putLong("lockDelay", value).apply()

    var lockEnabled: Boolean
        get() = prefs.getBoolean("lockEnabled", false)
        set(value) = prefs.edit().putBoolean("lockEnabled", value).apply()

    var appearance: String
        get() = prefs.getString("appearance", "System") ?: "System"
        set(value) = prefs.edit().putString("appearance", value).apply()

    var dynamicColor: Boolean
        get() = prefs.getBoolean("dynamicColor", false)
        set(value) = prefs.edit().putBoolean("dynamicColor", value).apply()

    /** Zuletzt gemeldete Benachrichtigung (Mitteilungen ohne Push). */
    var lastNotified: String?
        get() = prefs.getString("lastNotified", null)
        set(value) = prefs.edit().putString("lastNotified", value).apply()

    fun dismissedNotices(userId: String): Set<String> = prefs.getStringSet("dismissed-$userId", emptySet()) ?: emptySet()

    fun setDismissedNotices(userId: String, ids: Set<String>) = prefs.edit().putStringSet("dismissed-$userId", ids).apply()

    fun clear() = prefs.edit().clear().apply()
}
