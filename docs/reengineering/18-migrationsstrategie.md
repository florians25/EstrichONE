# 18 – Migrationsstrategie Estrich Web → EstrichONE

## 1. Grundentscheidung

**[Vorschlag] Neuaufbau mit einmaliger Datenübernahme und kurzer Parallelphase.** Kein schrittweises
„Strangler“-Verfahren.

Begründung: Ein schrittweiser Umbau würde verlangen, dass alte PHP-API und neue API **dieselbe Datenbank**
nutzen. Das alte System läuft auf MySQL bei STRATO, das neue auf PostgreSQL im eigenen Docker-Betrieb, mit
anderem Datenmodell (Bauakte/Baustelle zusammengeführt, Rechte, Versionen, Tombstones). Eine Brücke zwischen
beiden wäre aufwendiger als die Migration und würde Fehler aus dem Altbestand mitnehmen.

**Was trotzdem schrittweise geschieht:** Die **Fachfunktionen** werden einzeln portiert und jeweils gegen
den Altbestand abgenommen. Der Wechsel des Betriebs passiert einmal, wenn der vereinbarte Funktionsumfang
(Version 1.0 Web + Backup) erreicht ist.

## 2. Vorgehen

| Schritt | Inhalt | Ergebnis |
|---|---|---|
| **M-1 Bestandsanalyse der Produktivdaten** | Lesender Export der Produktivdatenbank (mysqldump über phpMyAdmin bzw. STRATO-Export). Daraus ein Analysebericht: Zeilen je Tabelle, leere Tabellen, `tenant_id`-Werte, Bauakten mit mehr als einer Baustelle, verwaiste Datensätze (keine Fremdschlüssel!), Dateipfade ohne Datei und Dateien ohne Datensatz, tatsächlich vorhandene Spalten (Laufzeitmigrationen!) | bestätigt oder korrigiert 02 (Funktionsumfang) und E-04/E-05 |
| **M-2 Abbildungsregeln** | Tabelle für Tabelle: Quelle → Ziel, Feldumwandlungen, Standardwerte, Behandlung von Waisen | `docs/migration/mapping.md` |
| **M-3 Migrationswerkzeug** | `estrichone migrate-legacy --mysql-dump <datei> --files <ordner> [--dry-run]`: liest den Dump in eine temporäre MySQL-Instanz (Container) und schreibt per Upsert in PostgreSQL. **Wiederholbar** (Upsert über die erhaltene ID): Probeläufe und finaler Lauf verwenden dasselbe Werkzeug. | Code + Tests mit anonymisiertem Ausschnitt |
| **M-4 Dateien** | STRATO-Ablage per SFTP herunterladen → in die neue Struktur kopieren; SHA-256 berechnen, Varianten neu erzeugen; Bericht über fehlende/überzählige Dateien | |
| **M-5 Probemigrationen** | mindestens zwei vollständige Probeläufe auf einer Staging-Instanz; Abnahme durch Stichproben (20 Bauakten Seite an Seite vergleichen) | Abnahmeprotokoll |
| **M-6 Umstellung** | Freitagnachmittag: Estrich Web schreibgeschützt schalten (Wartungshinweis in der API) → finaler Export → Migration → Prüfbericht → DNS/Adresse umstellen → Benutzer erhalten Einrichtungs-QR für die Apps | Ausfall ≤ 4 h |
| **M-7 Parallelphase** | Estrich Web bleibt 4 Wochen **schreibgeschützt** erreichbar (Nachschlagen) | |
| **M-8 Abschaltung** | finaler Export als Archiv (Dump + Dateien) ins EstrichONE-Backup, Kündigung Webspace nach Entscheidung des Betriebs | |

## 3. Abbildung wichtiger Daten

| Alt | Neu | Regel |
|---|---|---|
| IDs `CHAR(32)` (32 Hex) | `uuid` | direkte Umwandlung (Hex → UUID-Darstellung); alle Bezüge bleiben gültig |
| `tenant_id` | entfällt | Annahme: genau ein Mandant (M-1 prüft); bei mehreren → eine Installation je Mandant oder Abbruch mit Rückfrage |
| `projects` + `construction_sites` | `bauakten` (+ `bauakte_bereiche`) | 1:1 → zusammenführen (Adresse aus Baustelle, falls bei Bauakte leer); 1:n → erste Baustelle = Hauptadresse, weitere als Bereiche; `site_id`-Bezüge (Fotos, Dokumente, Räume) werden auf Bauakte + Bereich umgehängt |
| `project_no` | `nummer` | unverändert; Nummernkreis-Stand aus `project_number_sequences` übernehmen |
| `users.password_hash` (bcrypt `$2y$`) | `user_credentials` | übernehmen, Kennzeichen „bcrypt“ → Rehash zu Argon2id beim nächsten Login |
| Rollen/Rechte | neue Standardrollen | Zuordnung alte Rolle → neue Standardrolle per Tabelle (M-2); Sonderregeln (Kolonnenmitglied, Führung) werden zu Rechten. **Ergebnis vor Umstellung vom Betrieb prüfen lassen** |
| `photos` (main/thumb, Original meist nicht vorhanden) | `files` + `file_variants` | das beste vorhandene Bild wird Original; Varianten neu erzeugen |
| `project_notes` | `notes` | inkl. Anheftung, Farbe |
| `defects` | `tasks` (typ = mangel) | |
| `calendar_events`, `planning_assignments` | `calendar_events`, `assignments` | Kategorien und Farben übernehmen |
| `lexware_contact_id`, Lexware-Links | `external_refs (system='lexware')` | Bezug bleibt erhalten, keine Funktion in 1.0 |
| `audit_logs`, `login_logs` | Archiv (JSON-Export im Backup) | nicht ins neue Audit (andere Semantik) |
| `offline_sync_jobs` | **prüfen und ausgeben** | enthält ggf. nie verarbeitete Vorgänge (01, ❌-1). M-1 listet sie; sinnvolle Inhalte (z. B. vorgemerkte Bauakten) werden dem Betrieb zur manuellen Nachpflege übergeben |
| `quick_logins`, `refresh_tokens`, `device_sessions`, `sso_*` | entfallen | alle Benutzer melden sich neu an und richten Apps neu ein |
| leere Platzhaltertabellen (`ai_jobs`, …) | entfallen | nur wenn M-1 sie als leer bestätigt |

## 4. Risiken

| Risiko | Gegenmaßnahme |
|---|---|
| Unbekannter Schemastand der Produktivdatenbank (101 Laufzeitänderungen) | M-1 liest das reale Schema; das Werkzeug arbeitet spaltenweise tolerant und meldet Unbekanntes |
| Verwaiste Datensätze (keine Fremdschlüssel) | Prüfbericht; Waisen werden in eine Ablage „Nicht zuordenbar“ übernommen, nicht verworfen |
| Dateien fehlen auf dem Webspace | Bericht; betroffene Einträge bleiben mit Kennzeichen „Datei fehlt“ |
| Rechte-Abbildung weicht von der gewohnten Praxis ab | Rechte-Vorschau je Benutzer vor der Umstellung |
| Benutzer müssen Apps neu einrichten | QR-Einrichtung, Aushang/Anleitung, Termin mit Kolonnen |

**Keine destruktiven Schritte am Altbestand** ohne ausdrückliche Freigabe. Die Migration liest ausschließlich.
