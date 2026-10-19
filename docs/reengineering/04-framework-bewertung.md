# 04 – Framework-Bewertung

Kennzeichnung: **[Verbindlich]** = Vorgabe aus dem Master-Prompt · **[Vorschlag]** = meine Empfehlung,
Freigabe erforderlich · **[Offen]** = Entscheidung steht aus.

Bewertungskriterien laut Vorgabe: Wartbarkeit, Sicherheit, Leistung, Komponentensystem, Barrierefreiheit,
Integrationsaufwand. Dazu kommt ein siebtes Kriterium, das bei einem einzelnen Hobbyentwickler entscheidet:
**Anzahl der Sprachen und Ökosysteme, die dauerhaft beherrscht werden müssen.**

> Versionsangaben: Ich gebe den Stand meines Wissens an (Mitte 2026). **Vor Projektstart werden die aktuellen
> stabilen Versionen geprüft und in `docs/adr/` festgehalten.** Insbesondere bei Windows App SDK und Material 3
> Expressive ändern sich Stabilitätsstände schnell.

## 1. Backend

Die Vorgaben schließen PHP auf dem Webspace praktisch aus: PostgreSQL, Docker Compose, Backup-Worker,
Hintergrunddienste für Synchronisation und Benachrichtigungen. Zur Wahl stehen:

| Kriterium | PHP 8.4 + Laravel | Node.js + NestJS (wie SATZIO) | **ASP.NET Core (.NET 10 LTS)** |
|---|---|---|---|
| Übernahme aus Estrich Web | gering: Router und Logik müssten ohnehin neu strukturiert werden | keine | keine |
| Gemeinsame Sprache mit Pflicht-Clients | – | Web (TypeScript) | **Windows-Desktop (C#, verbindlich)** |
| Microsoft-Entra-Integration mit offiziellen Bibliotheken | Community-Pakete | MSAL Node (Client-seitig), Tokenprüfung über `jose` | **Microsoft.Identity.Web / JwtBearer (offiziell)** |
| Hintergrunddienste (Sync, Backup-Steuerung, Erinnerungen) | Queue-Worker als zweiter Prozess | möglich | `BackgroundService` im selben Prozess |
| Typsicherheit, Refactoring | mittel | gut | sehr gut |
| ORM, Migrationen | Eloquent, gut | Prisma, gut | EF Core + Npgsql, sehr gut |
| Leistung, Ressourcen auf kleinem Server | gut | gut | sehr gut, ~100–150 MB RAM |
| Support-Zeitraum | jährliche Hauptversionen | LTS von Node, häufige Major-Wechsel im Ökosystem | **LTS 3 Jahre** (.NET 10 bis Nov. 2028), danach planbarer Wechsel auf die nächste LTS |
| Vorkenntnisse des Entwicklers | Estrich Web (PHP ohne Framework) | SATZIO | über WinUI 3 ohnehin erforderlich |

**[Vorschlag] Entscheidung E-01: ASP.NET Core (.NET 10 LTS) als modularer Monolith.**

Begründung:
1. **WinUI 3 erzwingt C#.** Mit einem C#-Backend bleibt es bei drei Sprachen (C#, TypeScript, Swift/Kotlin)
   statt vier. Die API-Verträge (DTOs) lassen sich als gemeinsame Bibliothek `EstrichOne.Contracts` direkt in
   Server und Desktop verwenden.
2. Für Entra ID gibt es offizielle, gepflegte Microsoft-Bibliotheken. Das verlangt die Vorgabe ausdrücklich.
3. Ein Prozess genügt für API, Synchronisation und Hintergrundaufgaben. Das ist weniger Betriebsaufwand als
   API plus separater Worker.

**Ehrlicher Gegenpunkt:** Mit NestJS/Prisma/PostgreSQL/Docker hast du in SATZIO bereits eine funktionierende,
selbst gehostete Architektur betrieben, inklusive einer Sync-Engine mit `opId`-Idempotenz, die das hier benötigte
Muster bereits umsetzt. Das wäre der kürzere Weg zum ersten lauffähigen Backend. Ich empfehle trotzdem .NET, weil
die Sprachgleichheit mit dem Desktop über die Lebensdauer schwerer wiegt als der schnellere Start. Wenn der
Windows-Desktop später zurückgestellt würde (siehe 20, Risiko R-1), kippt diese Abwägung zugunsten von NestJS.
**Bitte diese Entscheidung ausdrücklich bestätigen.**

### Bibliotheken Backend [Vorschlag]

| Zweck | Wahl | Lizenz | Alternative / Hinweis |
|---|---|---|---|
| Datenbank | PostgreSQL 17 | PostgreSQL | – |
| ORM, Migrationen | EF Core + Npgsql | MIT | Migrationen als C#-Code im Repository, beim Start **nicht** automatisch auf Produktion, sondern über `estrichone migrate` |
| Passwort-Hashing | Argon2id (Konscious.Security.Cryptography oder Isopoh) | MIT | Altbestand bcrypt → Rehash beim ersten Login |
| OpenAPI | Microsoft.AspNetCore.OpenApi | MIT | Spezifikation wird in `contracts/openapi.yaml` eingecheckt (Vertragstest) |
| Bildverarbeitung | NetVips (libvips) | MIT / LGPL | SkiaSharp (MIT). **Nicht** ImageSharp (eingeschränkte Lizenz) |
| PDF | **[Offen] E-09** PDFsharp/MigraDoc (MIT) oder QuestPDF (Community-Lizenz nur bis 1 Mio. USD Jahresumsatz des Anwenderbetriebs) | – | Für eine frei weitergegebene Software empfehle ich MIT |
| Logging | Serilog → stdout (JSON) | Apache-2.0 | Docker sammelt die Logs |
| E-Mail | MailKit | MIT | nur wenn SMTP konfiguriert |
| Tests | xUnit, Testcontainers (PostgreSQL) | Apache/MIT | echte Datenbank in Tests, keine In-Memory-Attrappe |

## 2. Web

| Kriterium | **Angular (bestehend)** | React + TypeScript | Vue | SvelteKit |
|---|---|---|---|---|
| Übernahme vorhandener Fachkomponenten | **hoch** (Wochenplaner, Kalender, Bauakte als Vorlage) | keine | keine | keine |
| Struktur für große Verwaltungsanwendungen | sehr gut (DI, Router, Formulare integriert) | über Bibliotheksauswahl | gut | gut |
| Barrierefreiheit | **Angular CDK a11y** (FocusTrap, LiveAnnouncer, ListKeyManager) | Radix/React Aria | wechselnd | wechselnd |
| Stabilität und Pflegeaufwand | planbarer 6-Monats-Takt, `ng update` | hohe Bibliotheksfluktuation | gut | jünger |
| Kenntnisse im Projekt | vorhanden (Estrich Web) | SATZIO | – | – |

**[Vorschlag] Entscheidung E-02: Angular beibehalten, aber als neues, sauberes Projekt aufbauen.**
- Ein **Frameworkwechsel bringt keinen Nutzen**, der den Verlust der vorhandenen Fachkomponenten aufwiegt
  (Regel 3 und 22).
- **Neuaufbau statt Umbau:** Die API ändert sich vollständig (typisierte Endpunkte statt `/module/{key}`).
  Das globale Stylesheet ist nicht sanierbar (2.549 `!important`), und es gibt keine Tests, die einen Umbau
  absichern würden. Fachkomponenten werden **Bildschirm für Bildschirm** portiert, mit dem Altbestand als
  Spezifikation.
- **Angular Material wird nicht mehr als Gestaltung verwendet.** Eigenes Designsystem
  (05) auf Basis des **Angular CDK** (Overlay, Dialog, Table, Virtual Scroll, a11y). Für sehr große Tabellen
  **[Offen] E-10:** eigene CDK-Tabelle (Standard) oder AG Grid Community (MIT) für den Kolonnenplaner.
- FullCalendar (MIT) bleibt für die Kalenderansichten.
- Zustand über Angular Signals und Services; **kein NgRx** (wird heute nur für einen Mini-Reducer genutzt).

## 3. Windows Desktop [Verbindlich: WinUI 3]

| Baustein | Vorschlag | Bewertung |
|---|---|---|
| UI | WinUI 3 + Windows App SDK (aktuelle stabile Version) | verbindlich |
| Architektur | MVVM mit CommunityToolkit.Mvvm | MIT, Microsoft-gepflegt, wenig Boilerplate |
| Ribbon | siehe 06: **eigenes Ribbon-Steuerelement aus nativen WinUI-Komponenten**. Das Ribbon aus den Windows Community Toolkit Labs wird zuerst geprüft und nur übernommen, wenn es stabil veröffentlicht ist. | Labs-Komponenten sind experimentell, siehe 06 |
| Tabellen | **[Offen] E-11:** WinUI.TableView (MIT, aktiv gepflegt) oder `ItemsRepeater`-basierte Eigenentwicklung | Eine erstklassige DataGrid-Komponente von Microsoft gibt es für WinUI 3 nicht; das alte Toolkit-DataGrid ist nicht mehr gepflegt |
| API-Zugriff | gemeinsame Bibliothek `EstrichOne.Contracts` + typisierter `HttpClient` | Typgleichheit mit dem Server |
| Anmeldung | lokal; Entra über MSAL.NET mit WAM-Broker | offiziell |
| Verteilung | MSIX + `.appinstaller` (Updates ohne Store) | siehe 06 und 20, Abschnitt 6 |

## 4. iOS [Verbindlich: Swift, SwiftUI]

| Baustein | Vorschlag | Begründung |
|---|---|---|
| Mindestversion | **[Offen] E-12:** iOS 17 (Vorschlag) | Liquid Glass ab iOS 26, darunter nativer Fallback (siehe 07) |
| Lokale Datenbank | **GRDB.swift** (SQLite, MIT) | ausgereift, explizite Transaktionen, Migrationen, getrennte Datenbankdateien je Benutzer problemlos möglich (wichtig für Poolgeräte). SwiftData ist für Mehr-Container-Szenarien und kontrollierte Migrationen weniger geeignet. |
| API-Client | swift-openapi-generator (Apple) | aus `contracts/openapi.yaml` erzeugt |
| Uploads | `URLSession` mit Hintergrundkonfiguration | läuft weiter, wenn die App beendet wird |
| Scanner | VisionKit `VNDocumentCameraViewController` | systemeigen, kostenlos |
| Entra | MSAL für iOS (offiziell) | optional, nur wenn serverseitig aktiviert |
| Schlüssel | Keychain, Secure Enclave (Geräteschlüssel) | – |

## 5. Android [Verbindlich: Kotlin, Jetpack Compose, Material 3]

| Baustein | Vorschlag | Begründung |
|---|---|---|
| Mindestversion | **[Offen] E-13:** API 29 (Android 10) | deckt Firmengeräte der letzten Jahre ab; Predictive Back ab API 33/34 |
| UI | Compose + Material 3; **Material 3 Expressive nur in Teilen, die als stabil veröffentlicht sind** | Vorgabe; Expressive war lange nur im Alpha-Kanal |
| Lokale Datenbank | Room (SQLite) mit einer Datenbank je Benutzer | offiziell, Migrationen |
| Verschlüsselung | Android Keystore + SQLCipher für die Datenbank (siehe 11) | Jetpack Security Crypto ist abgekündigt |
| Hintergrund | WorkManager | Uploads und Synchronisation mit Netzbedingung |
| Kamera | CameraX | Vorgabe |
| Dokumentenscanner | **[Offen] E-14:** ML Kit Document Scanner (benötigt Google Play Services) **mit Fallback** auf Kamera + Zuschneiden | auf Geräten ohne Play Services (z. B. manche Firmengeräte) nicht verfügbar |
| Entra | MSAL für Android | optional |
| API-Client | OpenAPI Generator (Kotlin, Ktor- oder Retrofit-Vorlage) | aus derselben Spezifikation |

## 6. Gesamtbild

```
Sprachen dauerhaft:  C# (Backend + Desktop) · TypeScript (Web) · Swift (iOS) · Kotlin (Android) · SQL
Eine Vertragsquelle: contracts/openapi.yaml → generierte Clients für TS, Swift, Kotlin; C# nutzt die DTO-Bibliothek
```

Vier Clientplattformen bleiben für eine einzelne Person die größte Last. Wie die Reihenfolge darauf reagiert,
steht in 20.
