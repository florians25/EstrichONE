# 00 – Zusammenfassung der Analyse

**Stand:** 19.10.2026 · **Phase:** 1 bis 5 abgeschlossen (Bestandsaufnahme, Funktionskatalog, Architektur,
UX-Konzept, Roadmap) · **Implementierung:** **nicht begonnen, Freigabe ausstehend.**

Untersucht wurde der Quellcode von Estrich Web (Stand 23.08.2026), abgelegt in
[`legacy/estrichweb/`](../../legacy/estrichweb/). Die Anwendung wurde **nicht ausgeführt**: Es gab weder
Datenbank noch Produktivdaten noch PHP-Laufzeit. Alle Aussagen beruhen auf dem Code.

## 1. Bewertung des vorhandenen Projekts

Estrich Web ist **fachlich wertvoll und technisch nicht weiter ausbaufähig.**

- **Stark:** realistisches Fachmodell eines Estrichbetriebs (Bauakte mit Räumen, Estrichaufbau, Erstbegehung,
  CM-Messung, Kolonnenplanung, Material, Anrufe). Viele gute UX-Detailentscheidungen aus 66 Iterationsrunden.
  Verlustfrei migrierbare IDs.
- **Schwach:** auf Shared-Webspace zugeschnitten. Eine 3.000-Zeilen-Router-Datei, ein 12.800-Zeilen-Stylesheet
  mit 2.549 `!important`, 101 Schemaänderungen zur Laufzeit, keine Fremdschlüssel, keine Tests.
- **Kritisch:**
  - Die Offline-Synchronisation ist **nur scheinbar vorhanden**: Vorgänge werden angenommen, nie verarbeitet
    und auf dem Gerät gelöscht.
  - Die Microsoft-Anmeldung ordnet Benutzer per E-Mail aus einem ungeprüften Token zu, mit Mandant `common`.
  - 2FA wird nicht geprüft.
  - Jeder Benutzer darf alle Bauakten schreiben und alle Benutzerkontakte lesen.
  - Das Audit-Log ist abgeschaltet.
- **Sofort zu handeln (Altbestand, unabhängig von EstrichONE):** Das weitergegebene ZIP enthielt die
  **Produktionszugangsdaten** (Datenbank-Passwort, JWT-Secret). Beides sollte rotiert werden. Weitere
  Sofortmaßnahmen stehen in [17, Abschnitt 1](17-sicherheit-und-datenschutz.md). Im Repository sind diese Daten
  nicht enthalten.

Details: [01-bestandsaufnahme.md](01-bestandsaufnahme.md)

## 2. Wichtigste Architektur- und Qualitätsprobleme

1. Schein-Offline-Sync mit faktischem Datenverlust (❌-1)
2. Unsichere Entra-SSO-Zuordnung (🔒-2), ungeprüfte 2FA (🔒-3)
3. Fest codierte Rechte-Ausnahmen statt Berechtigungen; zu weite Lese- und Schreibrechte (🔒-4)
4. Token in URLs und `localStorage`; Cron-Schlüssel = JWT-Secret (🔒-5, 🔒-6, 🔒-9)
5. Benutzerunabhängiger API-Cache im Service Worker: Risiko für gemeinsam genutzte Geräte (🔒-10)
6. Kein reproduzierbarer Schemastand (Laufzeit-DDL, überlappende Migrationsskripte)
7. Doppelte Entitäten (Bauakte/Baustelle) und doppelte Konfiguration (PHP/TS)
8. Monolithische Dateien, globales CSS ohne Kapselung
9. Keine Tests, keine CI, kein Backup außer dem des Hosters

## 3. Frameworkvorschlag

| Teil | Vorschlag | Status |
|---|---|---|
| Backend | **ASP.NET Core (.NET 10 LTS), modularer Monolith**, EF Core, PostgreSQL 17 | **entschieden (E-01)** |
| Web | **Angular beibehalten**, als neues Projekt; eigenes Designsystem auf Angular CDK statt Material-Optik | [Vorschlag] E-02 |
| Windows | WinUI 3 + Windows App SDK, MVVM Toolkit, eigenes natives Ribbon | [Verbindlich] + [Vorschlag] |
| iOS | SwiftUI, Liquid Glass ab iOS 26, Fallback ab iOS 17, GRDB | [Verbindlich] + [Vorschlag] |
| Android | Kotlin, Compose, Material 3, Room + SQLCipher, WorkManager, CameraX | [Verbindlich] + [Vorschlag] |
| Betrieb | Docker Compose: Caddy, API, PostgreSQL, Backup (restic) | [Vorschlag] |

## 4. Zielarchitektur und Module

Ein Server, eine Datenbank, eine versionierte API (`/api/v1`, OpenAPI als einzige Vertragsquelle) und vier Clients.
16 fachliche Module in einem Prozess (Identity, Access, Devices, Organization, Contacts, Bauakten, Documentation,
Work, Planning, Office, Helpdesk, Sync, Notifications, Audit, Backup, Platform).
Details: [03-zielarchitektur.md](03-zielarchitektur.md)

## 5. Datenbank- und API-Konzept

- Client-generierte UUIDv7 (Offline-Anlage).
- Versionsnummer je Datensatz, Tombstones, Fremdschlüssel.
- Keine Mandantenspalte.
- Bauakte und Baustelle zusammengeführt.
- Fehler nach RFC 9457, `Idempotency-Key`/`opId`, `If-Match`, Cursor-Paginierung.
- Keine Tokens in URLs.

[03, Abschnitte 3–6](03-zielarchitektur.md)

## 6. Plattformübergreifendes UX-Konzept

- Gemeinsame Identität aus Navy-Akzent, Bauakten-Kopf, Statusfarben und Begriffen. Steuerelemente und
  Navigation bleiben plattformeigen.
- **Palettenbefund:** `#007BFF` hat mit Weiß nur 3,98 : 1. Für Schrift und Schaltflächen wird `#0062CC`
  (5,8 : 1) ergänzt.
- Status wird immer über Farbe, Symbol und Text vermittelt.

[05](05-designsystem.md) · [06 Windows](06-windows-winui3.md) · [07 iOS](07-ios-swiftui-liquid-glass.md) ·
[08 Android](08-android-jetpack-compose.md)

## 7. Mobile Bauaktenanlage

Ein einziger Bildschirm, **ein Pflichtfeld** (Bauvorhaben), Standort mit einem Tipp, Kunde optional aus „Zuletzt“.
„Anlegen und fotografieren“ öffnet direkt die Kamera. Ziel 12–18 s, mit Kundensuche ≤ 30 s.
Vollständig offline; Duplikate werden erkannt, ohne zu blockieren. [09](09-mobile-bauaktenanlage.md)

## 8. Persönliche Geräte und Poolgeräte

- Gerätebindung über einen Hardware-Schlüssel.
- Freigabe nur durch den Administrator bzw. per Admin-QR-Code. Persönliche Geräte werden optional automatisch freigegeben.
- **Poolgeräte:** ein verschlüsselter Container je Benutzer, geöffnet mit der Geräte-PIN des Benutzers.
  Kein gemeinsamer Cache, Biometrie aus, Sperre nach 5 min, immer Benutzerauswahl statt Fortsetzung.

[10](10-server-onboarding.md) · [11](11-pool-und-personalgeraete.md)

## 9. Microsoft Entra SSO

- Optional und vollständig abschaltbar.
- Serverseitige Tokenprüfung mit offiziellen Bibliotheken.
- Zuordnung über `tid + oid` statt E-Mail. Feste Tenant-ID statt `common`.
- Microsoft liefert nur die Identität. Danach gelten EstrichONE-Sitzungen mit eigenen Rechten.
- Poolgeräte: Kontoauswahl erzwingen, kein MSAL-Cache, ehrlicher Hinweis zur weiter bestehenden
  Microsoft-Sitzung.

[12](12-microsoft-entra-sso.md)

## 10. IT Center / Mini-MDM

- **Selbst umsetzbar:** Inventar der EstrichONE-Installationen, Freigabe, Sperre, Sitzungswiderruf,
  Löschen der App-Daten beim nächsten Kontakt (ohne Garantie für Offline-Geräte),
  App-Richtlinien, Mindestversion, Prüfung der Gerätesperre.
- **Nicht möglich ohne echtes MDM:** Betriebssystem-Löschung, Durchsetzung der Gerätesperre, Verwaltung anderer Apps.
- **Später:** Managed App Configuration als herstellerneutraler Weg.

[13](13-it-center-mini-mdm.md)

## 11. Helpdesk

- Ticket mit nur einem Pflichtfeld, aus allen Apps erstellbar, auch offline.
- Mit Gerät, Benutzer und Bauakte verknüpft. Interne Notizen.
- Diagnosedaten werden vor dem Senden angezeigt; Inhalte, Tokens und Passwörter sind technisch ausgeschlossen.
- Meldung ohne Anmeldung über den Geräteschlüssel. Benachrichtigung In-App bzw. über SMTP.

[14](14-helpdesk.md)

## 12. Migrationsstrategie

**Entschieden: keine Datenübernahme. EstrichONE startet als Neuprodukt.** Der Altbestand bleibt fachliche
Referenz; Estrich Web läuft unabhängig weiter, bis der Betrieb umstellt. Optional gibt es einen
CSV-Import für Kunden und Mitarbeiter.
[18](18-migrationsstrategie.md)

## 13. Roadmap

Fundament → Dateien, Sync, **Backup (vor Go-live)** → **iOS-App 1.0** mit Web-Administration (Basis) →
Web 1.0 → Android → SSO/IT-Center-Ausbau → Windows-Desktop (mit Prüfpunkt).
Gesamtaufwand ≈ 300–440 Personentage; jeder Meilenstein ist für sich nutzbar.
[20](20-entwicklungsroadmap.md)

## 14. Die ersten zehn Arbeitspakete

AP-01 Repository und CI · AP-02 Backend-Skelett und Docker · AP-03 Kern-Datenmodell · AP-04 Lokale
Anmeldung · AP-05 Rechte, Objektzugriff, Geräteidentität · AP-06 Ersteinrichtung und Stammdaten-Import · AP-07 Dateiservice ·
AP-08 Sync-Protokoll · AP-09 Backup und Restore · AP-10 iOS-Grundgerüst und Server-Onboarding.
Details mit Akzeptanzkriterien: [20, Abschnitt 4](20-entwicklungsroadmap.md#4-die-ersten-zehn-arbeitspakete)

## Offene Entscheidungen

| ID | Entscheidung | Mein Vorschlag |
|---|---|---|
| E-01 | Backend-Technologie | ✅ **entschieden:** ASP.NET Core .NET 10 LTS |
| E-02 | Web | Angular, Neuaufbau |
| E-04 | Mandantenfähigkeit | entfällt (1 Installation = 1 Betrieb) – durch Neuprodukt-Entscheidung bestätigt |
| E-05 | Bauakte + Baustelle | ✅ **entschieden:** zusammenführen, Bauabschnitte als Bereiche |
| E-07 | Push-Benachrichtigungen | optional; Standard In-App + E-Mail |
| E-08 | Original-Fotos behalten | ja |
| E-09 | PDF-Bibliothek | MIT-lizenziert (PDFsharp/MigraDoc) |
| E-10 / E-11 | Tabellenkomponente Web / Windows | CDK-Eigenbau / WinUI.TableView (Spike) |
| E-12 / E-13 | Mindestversion iOS / Android | iOS 17 / Android 10 |
| E-14 | Dokumentenscanner Android | ML Kit + Fallback |
| E-15 | Anrede in den Apps | „Sie“ |
| E-16 | Windows 10 unterstützen? | nach Gerätebasis |
| E-18 | Ungesendete Daten auf Poolgeräten nach Abmeldung | 1.0 beim nächsten Login, 1.1 Upload-Ticket |
| E-19 | Welche Mobilplattform zuerst? | ✅ **entschieden:** iOS/iPhone |
| E-20 | Datenübernahme aus Estrich Web | ✅ **entschieden:** keine, Neuprodukt |

## Verbindlich · Vorschlag · Offen

- **Verbindlich** (aus der Vorgabe): WinUI 3, SwiftUI, Kotlin/Compose, Selbsthosting mit Docker Compose,
  PostgreSQL bevorzugt, SSO optional, kein Betriebssystem-MDM-Versprechen, keine Rechnungs- und
  Angebotsfunktionen in 1.0.
- **Vorschlag:** alles, was in den Dokumenten mit [Vorschlag] markiert ist. Gilt erst nach deiner Freigabe.
- **Offen:** Tabelle oben.
