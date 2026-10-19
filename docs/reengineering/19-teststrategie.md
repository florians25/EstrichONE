# 19 – Teststrategie

**Ausgangslage:** Estrich Web hat keine automatisierten Tests (01, Abschnitt 13). Fehler wurden in der
Produktion gefunden. **Ziel:** Jede Funktion ist beim Abschluss ihres Arbeitspakets automatisiert getestet.
Kein Arbeitspaket gilt ohne grüne CI als fertig.

**Grundsatz:** Ich melde nur Tests als bestanden, die tatsächlich ausgeführt wurden, mit Ausgabe. Was nur
manuell oder auf echten Geräten prüfbar ist, wird als solches gekennzeichnet.

## 1. Testebenen

| Ebene | Werkzeug | Umfang | Läuft |
|---|---|---|---|
| Unit (Backend) | xUnit | Domänenregeln: Nummernkreis, Statusübergänge, feldweises Zusammenführen, Rechteauswertung | jeder Push |
| Integration/API | xUnit + `WebApplicationFactory` + **Testcontainers PostgreSQL** | jeder Endpunkt: Erfolg, Validierung, **Berechtigung je Rolle (darf / darf nicht / fremdes Objekt)** | jeder Push |
| Datenbankmigrationen | EF-Migrationen auf leere DB **und** auf DB mit Vorgängerschema + Testdaten | jede Migration vorwärts | jeder Push |
| Vertragstests | generierte OpenAPI-Spezifikation gegen `contracts/openapi.yaml` (Diff = Fehler, sofern nicht bewusst geändert) | | jeder Push |
| Authentifizierung | Login, Sperre, Refresh-Rotation, Wiederverwendungserkennung, Gerätebindung, Entra-Token-Prüfung mit selbst signierten Test-Tokens (falsche Signatur, falscher `tid`, abgelaufen) | | jeder Push |
| Sync-Protokoll | jeder Sonderfall aus 15, Abschnitt 3 als Integrationstest | | jeder Push |
| Web Unit/Komponenten | Vitest bzw. Jest + Angular Testing Library | | jeder Push |
| Web E2E | Playwright (Chromium; WebKit/Firefox nächtlich) gegen Docker-Compose-Stack | Hauptabläufe | jeder PR |
| Barrierefreiheit Web | axe-core in Playwright, Tastaturpfade | jede Seite | jeder PR |
| iOS | XCTest (Core-Paket inkl. SyncEngine mit simuliertem Netz), XCUITest (Hauptabläufe) | | macOS-Runner (GitHub Actions, für öffentliche Repos kostenlos) |
| Android | JUnit + Robolectric (Logik), Compose UI Tests, Room-Migrationstests | | jeder PR |
| Windows | xUnit für ViewModels und Ribbon-Logik; UI-Automation (WinAppDriver/Appium) nur für Rauchtests | | Windows-Runner |
| Backup/Restore | CI-Job: Stack starten, Testdaten, Backup → neuer Stack → Restore → Vergleich | | nächtlich |
| Stammdaten-Import | CSV-Import mit fehlerhaften, doppelten und leeren Zeilen | | jeder PR |
| Sicherheit | OWASP ZAP Baseline gegen Staging; Abhängigkeits- und Container-Scan | | nächtlich |

## 2. Mobile Testfälle (verbindlich laut Vorgabe)

| ID | Fall | Automatisiert | Echtes Gerät |
|---|---|---|---|
| MT-1 | Offline-Bauaktenanlage | ✅ UI-Test im Flugmodus-Simulator | ✅ |
| MT-2 | Fotoaufnahme ohne Verbindung (20 Fotos) | teilweise (Kamera im Simulator eingeschränkt) | ✅ |
| MT-3 | Große Foto-/Video-Uploads (500 MB Video) | ✅ gegen Testserver | ✅ |
| MT-4 | Verbindungsabbruch beim Speichern (nach Senden / nach Annahme) | ✅ Proxy mit Fehlerinjektion (Toxiproxy) | – |
| MT-5 | Benutzerwechsel auf Poolgeräten (AK aus 11) | ✅ | ✅ |
| MT-6 | Serverwechsel mit ausstehenden Daten | ✅ | – |
| MT-7 | Entzug von Geräteberechtigung während Sync | ✅ | – |
| MT-8 | Microsoft-SSO-Anmeldung | ❌ nur manuell (echter Test-Tenant nötig) | ✅ |
| MT-9 | Abmeldung und Sitzungsablauf | ✅ | – |
| MT-10 | App-Neustart während Synchronisation (Prozess beenden) | ✅ (Android: `am kill`; iOS: XCUITest `terminate`) | ✅ |

## 3. Geräte- und UX-Tests (manuell, mit Protokoll)

| Thema | Prüfung |
|---|---|
| Bildschirmgrößen | kleinstes unterstütztes iPhone (SE, 4,7"), großes iPhone, iPad; Android kompakt (360 dp), groß, Tablet |
| Helle Umgebung | Außentest bei direkter Sonne mit Standard- und Baustellenmodus (Lesbarkeit Status, Schaltflächen) |
| Schriftgrößen | iOS Dynamic Type bis AX3, Android 200 %: keine abgeschnittenen Primäraktionen |
| Screenreader | VoiceOver, TalkBack, Windows-Sprachausgabe, NVDA (Web): Hauptabläufe vollständig bedienbar |
| Eingeschränkte Motorik / Handschuhe | Touch-Ziele ≥ 48 pt; Test mit Arbeitshandschuhen (kapazitiv) |
| Langsames Netz | Netzwerkdrosselung „3G/Edge“, 30 % Paketverlust |
| Einhandbedienung | Primäraktionen im unteren Drittel |

## 4. UX-Abnahme Schnellanlage

Messung von AK-1 aus 09 mit **5 Personen aus dem Betrieb** (Mitarbeiter mit wenig Technikerfahrung
einbeziehen): Zeit von „Startseite“ bis „erstes Foto gespeichert“; Fehlbedienungen; Nachbefragung.
Wiederholung nach Anpassungen.

## 5. Akzeptanzkriterien je Arbeitsablauf

Die Akzeptanzkriterien stehen bei den Konzepten (09 bis 16, Abschnitt „Akzeptanzkriterien“). Jedes
Arbeitspaket in 20 verweist auf seine Kriterien. Ein Arbeitspaket ist abgeschlossen, wenn:
1. Code und automatisierte Tests in CI grün sind,
2. die Akzeptanzkriterien nachweislich erfüllt sind (Testname oder Protokoll),
3. die Dokumentation aktualisiert ist,
4. vorhandene Funktionen nicht beeinträchtigt sind (Regressionstests),
5. Migrationen auf einer Kopie realistischer Daten ohne Verlust gelaufen sind.
