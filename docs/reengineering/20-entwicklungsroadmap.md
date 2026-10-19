# 20 – Entwicklungsroadmap

## 1. Kritische Prüfung der gewünschten Reihenfolge

| Gewünschte Reihenfolge | Bewertung | Änderung |
|---|---|---|
| 1–3 Analyse, Umfang, Zielarchitektur | ✅ erledigt (dieses Dokument-Set) | – |
| 4 Datenmodell und API stabilisieren | ✅ | ohne Rücksicht auf Altdaten (Neuprodukt, keine Migration – siehe 18) |
| 5 Authentifizierung und Berechtigungen | ✅ | **Geräteidentität gehört bereits hierher.** Sie ist Teil der Token-Ausgabe (Gerätebindung), nachträglich wäre sie ein Umbau |
| 6 Webanwendung modernisieren | ✅ | parallel zu 7/8 möglich, sobald die API steht |
| 7 Geräteverwaltung und Onboarding | ✅ | Serverteil früh (mit 5), Oberfläche im IT Center später |
| 8 Mobile Schnellanlage + Offline-Sync | ✅ | **Sync-Protokoll serverseitig zuerst** und mit Tests fertig, bevor die erste App beginnt |
| 9 iOS / 10 Android | ✅ | **iOS zuerst und vollständig** (entschieden, E-19), Android danach. Die iOS-App wird vorgezogen: Sie folgt direkt auf das Server-Fundament, die Web-Oberfläche wächst parallel nur so weit, wie die App sie braucht (Benutzer, Gerätefreigabe, Bauakten ansehen). |
| 11 WinUI-3-Desktop | ⚠️ | nach beiden Apps, mit **Prüfpunkt**: Reicht die Web-App dem Büro? (Risiko R-1) |
| 12 IT Center und Helpdesk | ⚠️ | Helpdesk (einfach) **vor** dem App-Rollout, denn genau dann entstehen die meisten Supportfälle |
| 13 Backup Center | ❌ **zu spät** | **vor der Inbetriebnahme** des neuen Servers mit Echtdaten. Ohne geprüftes Backup kein Go-live. |
| 14 End-to-End-Tests | ⚠️ | laufend ab dem ersten Arbeitspaket, nicht als Phase am Ende |
| 15 Installationspakete, Self-Hosting-Doku | ⚠️ | Docker-Compose-Installation ab dem ersten Arbeitspaket mitpflegen; Pakete für die Apps je Plattform-Meilenstein |

## 2. Meilensteine

| Meilenstein | Inhalt | Ergebnis für den Betrieb | Aufwand (Personentage, grob) |
|---|---|---|---|
| **M0 Fundament** | AP-01 … AP-05 | lauffähiger Server mit Anmeldung, Rechten, Kern-Datenmodell; Docker-Installation | 30–45 |
| **M1 Dateien, Sync, Backup** | AP-06 … AP-09 | Ersteinrichtung, Dateiservice, Sync-Protokoll, Backup + Restore geprüft | 25–35 |
| **M2 iOS-App 1.0** + Web-Administration (Basis) | AP-10 ff.: Onboarding, Poolgeräte, Schnellanlage, Fotos, Notizen, Aufgaben, Bautagebuch, Erstbegehung; im Web: Login, Benutzer, Gerätefreigabe, Bauakten ansehen | **native iPhone-App für die Baustelle** | 70–100 |
| **M3 Web 1.0** | Planung, Büro, Ressourcen, Helpdesk, IT Center | vollständige Büroumgebung, Inbetriebnahme als Neuprodukt | 50–80 |
| **M4 Android-App** | Funktionsgleichheit mit iOS | | 50–70 |
| **M5 Entra-SSO, IT-Center-Richtlinien, Helpdesk 1.1** | | | 20–30 |
| **M6 Windows-Desktop** (nach Prüfpunkt) | Ribbon, Tabellen, Mehrfenster, Druck | | 60–90 |
| **Summe** | | | **≈ 300–445 Personentage** |

**Ehrliche Einordnung:** Das ist ein großes Vorhaben. Bei rund einem Tag pro Woche Hobbyzeit wären es mehrere
Jahre. KI-Unterstützung beschleunigt das Schreiben von Code erheblich. Prüfen, Testen auf echten Geräten,
Abstimmung mit dem Betrieb und dauerhafte Wartung bleiben aber deine Zeit.

Deshalb ist die Roadmap so geschnitten, dass **jeder Meilenstein für sich nutzbar** ist. Nach M2 gibt es eine
native iPhone-App mit Server. Nach M3 eine vollständige Büroumgebung. M6 kann entfallen oder warten, ohne dass
etwas fehlt.

## 3. Risiken

| ID | Risiko | Wirkung | Gegenmaßnahme |
|---|---|---|---|
| R-1 | Vier Clientplattformen für eine Person | Wartungslast, Stillstand | Reihenfolge oben; Desktop mit Prüfpunkt; gemeinsame Verträge; Logik auf dem Server |
| R-2 | Tabellenkomponente WinUI 3 | Desktop-Qualität | Spike vor M6 |
| R-3 | Akzeptanz ohne historische Bauakten (keine Migration) | Doppelte Pflege in der Übergangszeit | Stichtag für neue Bauakten, Altsystem schreibgeschützt nachschlagbar (18) |
| R-4 | Distribution iOS (Developer-Konto) und Android (Verifizierungspflicht) | Kosten, Verzögerung | früh klären, siehe 07/08 und Abschnitt 6 |
| R-5 | Sync-Komplexität | Datenverlust | Protokoll zuerst, Testsuite mit Fehlerinjektion, nur-anlegen für Medien |
| R-6 | Material 3 Expressive / Liquid Glass ändern sich | Nacharbeit | nur stabile APIs, Systemkomponenten statt Eigenbau |
| R-7 | Abhängigkeit vom Betriebsalltag (Nutzertests) | falsche Prioritäten | UX-Tests mit Betrieb je Meilenstein |

## 4. Die ersten zehn Arbeitspakete

Jedes Arbeitspaket endet mit Code, Tests, Dokumentation und erfüllten Akzeptanzkriterien (19, Abschnitt 5).

| AP | Titel | Inhalt | Abhängig von | Akzeptanzkriterien | Aufwand |
|---|---|---|---|---|---|
| **AP-01** | Repository-Grundgerüst und CI | Ordnerstruktur (README), `.editorconfig`, ADR-Vorlage + ADRs für E-01…E-09, GitHub Actions (Build/Test Backend, Lint), Renovate | Freigabe E-01 | CI läuft grün auf leerem Projekt; ADRs eingecheckt | 2 PT |
| **AP-02** | Backend-Skelett und Docker-Stack | ASP.NET-Core-Projekt mit Modulstruktur, Architekturtest, EF Core + PostgreSQL, `migrate`-Befehl, Serilog, Health-Checks, Fehlerformat RFC 9457, `server-info`; `deploy/compose.yml` mit Caddy, API, Postgres; `setup.sh` erzeugt Secrets | AP-01 | `docker compose up` liefert `/api/v1/server-info` über HTTPS; Testcontainers-Test grün; kein Container läuft als root | 5–7 PT |
| **AP-03** | Kern-Datenmodell v1 | Organisation, Benutzer, Rollen/Rechte, Kontakte, Bauakten (+ Bereiche, Räume, Estrichaufbau), Notizen, Dateien (Metadaten), Aufgaben; Versionen, Tombstones, Fremdschlüssel; Nummernkreis | AP-02 | Migration auf leere DB grün; Nummernkreis unter Parallelzugriff eindeutig (Test mit 50 parallelen Anlagen) | 6–8 PT |
| **AP-04** | Lokale Authentifizierung und Sitzungen | Argon2id, Login-Drosselung, Access/Refresh mit Rotation und Wiederverwendungserkennung, Web-Cookie + CSRF, Ersteinrichtung mit Einmalcode, Passwort-Reset durch Admin, Audit-Ereignisse | AP-03 | Tests: Sperre, Rotation, Wiederverwendung → Familie widerrufen, kein Token in Logs; Ersteinrichtung nur einmal möglich | 6–8 PT |
| **AP-05** | Rechtesystem und Objektzugriff | Rechtekatalog, Standardrollen, Rollenverwaltung (API), `BauakteAccess`-Filter, Policies; Geräteregistrierung (Server: Schlüssel, Status, Bindung des Refresh-Tokens) | AP-04 | Rechte-Matrixtest: jede Rolle × jeder Endpunkt; IDOR-Tests (fremde Bauakte → 404) | 7–9 PT |
| **AP-06** | Ersteinrichtung und Stammdaten-Import | Einrichtungsablauf (Firma, Notfall-Administrator per Einmalcode, Standardrollen, Estricharten-Grundliste, Bauakten-Vorlagen); CSV-Import für Kunden und Mitarbeiter mit Vorschau und Dublettenprüfung | AP-05 | Leere Installation ist nach der Einrichtung sofort nutzbar; Import von 500 Kunden mit 10 fehlerhaften Zeilen zeigt genau diese 10 an und übernimmt den Rest | 4–6 PT |
| **AP-07** | Dateiservice | idempotenter Upload mit SHA-256, Teil-Upload für Videos, Inhaltsprüfung, Varianten (NetVips) im Hintergrund, Download mit Range | AP-05 | Doppelter Upload → eine Datei; abgebrochener Upload fortsetzbar; Bild mit falscher Endung erkannt; Download nur mit Recht auf die Bauakte | 5–7 PT |
| **AP-08** | Sync-Protokoll v1 (Server) | `docs/sync-protokoll.md`, `/sync/push` mit `opId`-Idempotenz, feldweises Zusammenführen, `/sync/pull` mit Cursor und Sichtbarkeitsfilter, Tombstones, Ablehnungsgründe, `instanceEpoch` | AP-05, AP-07 | jeder Fall aus 15, Abschnitt 3 als grüner Integrationstest | 8–10 PT |
| **AP-09** | Backup-Worker, Restore-Werkzeug, Backup Center (API) | Image mit restic + pg_dump, Zeitplan, Ziele lokal/SFTP, Aufbewahrung, Integritätsprüfung, automatischer Wiederherstellungstest, `estrichone-restore`, Statusendpunkte, Benachrichtigung | AP-02 | AK-1 bis AK-3 aus 16 als CI-Job (nächtlich) grün | 6–8 PT |
| **AP-10** | iOS-Grundgerüst und Server-Onboarding | Xcode-Projekt, Swift-Pakete `EstrichCore`/`EstrichAPI`/`EstrichDesign`, Tokens, generierter API-Client, Onboarding Schritt 1–2 (QR + Adresse, Verbindungsprüfung gegen `server-info`), CI-Build auf macOS-Runner | AP-02 | Build und Tests in CI grün; AK-2 aus 10 (TLS-Fehler werden abgelehnt) automatisiert, Gegenprobe auf Gerät manuell. AK-4 wandert zu AP-11, weil der Server Einrichtungscodes erst dort ausstellt. `EstrichAPI` folgt mit AP-11, `EstrichDesign` als eigenes Paket erst bei einem zweiten Nutzer (ios/README.md) | 6–8 PT |

**Direkt danach (M2):** AP-11 Geräteregistrierung, Anmeldung und Poolgeräte in iOS · AP-12 Schnellanlage + Fotos offline (iOS) · AP-13 Web-Grundgerüst mit Administration (Login, Benutzer, Gerätefreigabe per QR, Bauakten ansehen).

**Hinweis iOS:** Für Build, Simulator und Gerätetests ist ein **Mac mit Xcode** nötig. In der Cloud-Umgebung
dieser Entwicklung läuft Linux: Swift-Code kann ich schreiben, aber nicht mit Xcode bauen. Gebaut und getestet wird
über GitHub Actions mit macOS-Runnern (für öffentliche Repositorys kostenlos, für private mit Minutenkontingent)
und abschließend auf deinem Mac/iPhone.

## 5. Freigaben, die ich vor Beginn brauche

Vollständige Liste in [00-zusammenfassung.md](00-zusammenfassung.md#offene-entscheidungen).

## 6. Verteilung und externe Voraussetzungen

| Bestandteil | Weg | Externe Voraussetzung | Kosten |
|---|---|---|---|
| Server | Docker Compose: `setup.sh` (Secrets, Pfade, Domain) → `docker compose up -d`; `update.sh` (Backup → neue Images → Migration → Healthcheck → bei Fehler zurück auf die vorige Image-Version, die Datenbank bleibt dank vorherigem Backup wiederherstellbar) | Linux-Server mit Docker; Domain empfohlen (für Let's-Encrypt-Zertifikat) | kostenlos (Domain ≈ wenige €/Jahr) |
| Container-Images | GitHub Container Registry (öffentlich) | GitHub-Konto des Herausgebers | kostenlos für öffentliche Images |
| Windows | MSIX + `.appinstaller` vom eigenen Server | Signaturzertifikat: eigenes (kostenlos, muss auf den PCs vertraut werden) oder Signaturdienst (kostenpflichtig) | 0 € bzw. laufend |
| iOS | App Store / TestFlight | **Apple Developer Program** des Herausgebers; Demo-Server für die App-Prüfung | 99 USD/Jahr |
| Android | Google Play und/oder direkte APK vom eigenen Server | Play-Entwicklerkonto (einmalig 25 USD, Testpflicht 12 Tester × 14 Tage für neue private Konten); angekündigte Entwickler-Verifizierung auch außerhalb von Play beachten | 25 USD einmalig |
| Push (optional) | APNs (iOS), FCM (Android) | Apple Developer Program; Firebase-Projekt des **Herausgebers** – Push-Schlüssel können nicht pro Betrieb vergeben werden, ohne dass jeder Betrieb eigene App-Builds erstellt → Push über einen kleinen Relay-Dienst des Herausgebers **oder** ohne Push (Standard). **[Offen] E-07** | 0 € (FCM), im Apple-Programm enthalten |
| Microsoft Entra (optional) | App-Registrierung im Tenant des Betriebs | Microsoft 365 Business des Betriebs | im M365-Abo enthalten |
| SMTP (optional) | beliebiger Anbieter des Betriebs | – | – |

**Transparenz:** Server, Web und Desktop sind ohne kostenpflichtige Konten betreibbar. Für die **mobilen Apps**
braucht der Herausgeber Entwicklerkonten bei Apple und Google. Die Betriebe selbst zahlen nichts.
