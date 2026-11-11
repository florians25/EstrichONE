package de.estrichone.core

/**
 * Schema der lokalen Datenbank (SQLCipher) mit fortlaufender Version (`PRAGMA user_version`). Neue Versionen werden nur
 * angehängt und dürfen nie Daten löschen: Ein App-Update findet ungesendete Änderungen (Outbox, Uploads) unverändert vor.
 * Eine neuere Datenbank (App zurückgestuft) wird nicht angefasst – dann gilt der vorhandene Stand.
 */
object StorageSchema {
    /** Je Version die Anweisungen; Index 0 = Version 1. Nur anhängen, nie ändern. */
    val migrations: List<List<String>> = listOf(
        // Version 1: Stand bis 1.0.12 (vor der Versionierung mit CREATE … IF NOT EXISTS angelegt – daher idempotent).
        listOf(
            "CREATE TABLE IF NOT EXISTS entities (type TEXT NOT NULL, id TEXT NOT NULL, bauakte TEXT, state TEXT NOT NULL, json TEXT NOT NULL, PRIMARY KEY (type, id))",
            "CREATE INDEX IF NOT EXISTS entities_bauakte ON entities (bauakte)",
            "CREATE TABLE IF NOT EXISTS outbox (seq INTEGER PRIMARY KEY AUTOINCREMENT, json TEXT NOT NULL)",
            "CREATE TABLE IF NOT EXISTS uploads (fileId TEXT PRIMARY KEY, json TEXT NOT NULL)",
            "CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT)",
        ),
    )

    val version: Int get() = migrations.size

    /** Auszuführende Anweisungen für eine Datenbank mit [current]; leer, wenn aktuell oder neuer. */
    fun pending(current: Int): List<String> = if (current >= version) emptyList() else migrations.drop(current.coerceAtLeast(0)).flatten()

    /** Anweisungen, die Daten vernichten würden – in Migrationen verboten (geprüft in den Tests). */
    fun isDestructive(statement: String): Boolean {
        val text = statement.trim().uppercase()
        return text.startsWith("DROP TABLE") || text.startsWith("DELETE") || text.startsWith("TRUNCATE") ||
            (text.startsWith("ALTER TABLE") && " DROP " in text)
    }
}
