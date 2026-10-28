# EstrichONE im App Store veröffentlichen

Vollständige Checkliste mit vorformulierten Texten. Alles, was im Projekt vorbereitet werden kann, ist erledigt
(Abschnitt „Im Projekt erledigt“); was nur Sie mit Ihrem Apple-Konto tun können, steht in der Checkliste.

## 0. Hochladen in App Store Connect – Schritt für Schritt

Alles liegt fertig im Ordner `ios/AppStore/` (auch als ZIP vom Koordinator):

| Was | Wo |
|---|---|
| Screenshots zum Hochladen (Reihenfolge = Dateiname 01–09) | `AppStore/upload/iphone-6.9/`, `upload/iphone-6.5/`, `upload/ipad-13/` |
| Dieselben ohne Rahmen (Alternative) | `AppStore/roh/<gerät>/` |
| Texte, je Feld eine Datei (Deutsch, Englisch) | `AppStore/texte/de-DE/`, `AppStore/texte/en-US/` |
| Altersfreigabe, App-Datenschutz, Exportkonformität, Kategorie | `texte/de-DE/age_rating.md`, `privacy_answers.md`, `export_compliance.md`, `category.txt` |
| Datenschutzerklärung, Support- und Startseite zum Hosten | `AppStore/web/` (`datenschutz.html`, `support.html`, `index.html`, `style.css`) |
| Querformat-Banner 1920 × 886 (Website, Werbung – **nicht** für App Store Connect) | `AppStore/banner-1920x886/` |

1. **Webseiten online stellen** (Pflicht: Support- und Datenschutz-URL). In `AppStore/web/` alle gelb markierten
   `PLATZHALTER` (Firma, Anschrift, E-Mail, Telefon, Impressum) ersetzen und die vier Dateien hochladen – z. B. auf den
   eigenen Webserver unter `https://ihre-domain.de/estrichone/` oder per GitHub Pages (öffentliches Repository, Ordner
   als Pages-Quelle). Dann in `texte/*/support_url.txt`, `privacy_url.txt`, `marketing_url.txt` und `copyright.txt`
   die Platzhalter ersetzen und prüfen: `python3 ios/AppStore/texte/pruefen.py` (0 Fehler, keine Platzhalter-Warnung).
2. **App anlegen** (Abschnitt 1 C): *Apps* › „+“ › Neue App – Name aus `name.txt`, Primärsprache Deutsch, Bundle-ID
   `de.estrichone.EstrichONE`, SKU `estrichone-ios`.
3. **App-Informationen** (linke Spalte): Untertitel `subtitle.txt`, Kategorie `category.txt`, Altersfreigabe nach
   `age_rating.md`, Datenschutzrichtlinie-URL `privacy_url.txt`. Optional Sprache „Englisch (USA)“ hinzufügen und die
   Felder aus `texte/en-US/` eintragen.
4. **Preise und Verfügbarkeit:** kostenlos, Länder wählen.
5. **App-Datenschutz:** nach `privacy_answers.md` ausfüllen und veröffentlichen.
6. **Version 0.1.0** (iOS-App › 0.1.0 Vorbereitung):
   - *Vorschauen und Screenshots*: Reiter „iPhone 6,9"-Display“ → alle Dateien aus `upload/iphone-6.9/` auf einmal
     hineinziehen (Reihenfolge stimmt durch die Dateinamen; sonst per Ziehen ordnen). Reiter „iPad 13"-Display“ →
     `upload/ipad-13/`. Das 6,5"-Fach ist optional (Apple skaliert 6,9" herunter); wer es füllen will: `upload/iphone-6.5/`.
   - *Werbetext* `promotional_text.txt`, *Beschreibung* `description.txt`, *Schlüsselwörter* `keywords.txt`,
     *Support-URL* `support_url.txt`, *Marketing-URL* `marketing_url.txt` (optional), *Copyright* `copyright.txt`.
   - *Neuerungen*: bei der ersten Version nicht abgefragt; `release_notes.txt` für das erste Update bzw. TestFlight „Was testen?“.
   - *Build*: hochgeladenen Build wählen (Abschnitt 1 B). Exportkonformität beantwortet die App selbst (`export_compliance.md`).
   - *App-Review-Informationen*: „Anmeldung erforderlich“ **nicht** anhaken; Kontaktdaten eintragen;
     *Anmerkungen* `review_notes.txt` (Deutsch) – oder `review_notes_en.txt` (Englisch, für das Prüfteam meist schneller).
   - *Versionsfreigabe*: manuell oder automatisch.
7. **Sichern**, oben rechts „Zur Prüfung hinzufügen“ › „An App Review senden“.

Bilder neu erzeugen (nach UI-Änderungen): Commit-Nachricht mit `[appstore]` oder Workflow „iOS“ von Hand mit
*appstore = true*; danach `ios/AppStore/frame.sh <rohordner>` (Ablauf: `ios/AppStore/README.md`). Nur prüfen:
`ios/AppStore/frame.sh --pruefen`.

## 1. Checkliste

### A. Konto und Identität (einmalig)

- [ ] **Apple Developer Program** (99 € im Jahr) für die Firma abschließen – als *Organisation* (D-U-N-S-Nummer nötig),
      damit im App Store der Firmenname als Anbieter steht. Rechtliches: Vereinbarungen in App Store Connect › *Verträge*
      akzeptieren (für eine kostenlose App reicht die „Free Apps Agreement“; Steuer-/Bankdaten nicht nötig).
- [ ] **DSA-Händlerstatus** (EU, Digital Services Act): in App Store Connect › *Geschäft* angeben, ob Sie als Händler
      auftreten (Firma: ja → Adresse, Telefon, E-Mail werden im EU-Store angezeigt).
- [ ] **Bundle-ID** registrieren: *Certificates, Identifiers & Profiles* › *Identifiers* › `de.estrichone.EstrichONE`
      (oder eigene, dann `PRODUCT_BUNDLE_IDENTIFIER` und `bundleIdPrefix` in `project.yml` anpassen).
      Capability **Push Notifications** anhaken. Weitere Capabilities braucht die App nicht.
- [ ] **APNs-Schlüssel** (*Keys* › „Apple Push Notifications service (APNs)“) erzeugen, .p8 sicher ablegen, Key-ID und
      Team-ID notieren und im EstrichONE-Server eintragen (`docs/benachrichtigungen.md`, `ios/README.md`).

### B. Projekt und Signierung

- [ ] `brew install xcodegen && cd ios && xcodegen generate`, `EstrichONE.xcodeproj` öffnen.
- [ ] Ziel *EstrichONE* › *Signing & Capabilities*: *Automatically manage signing*, Team wählen. Push ist über
      `EstrichONE/Config/EstrichONE.entitlements` eingetragen (Xcode setzt beim Archivieren `production`).
      Dauerhaft: `DEVELOPMENT_TEAM` in `project.yml` setzen, damit es `xcodegen generate` übersteht.
- [ ] **Version und Build**: `MARKETING_VERSION` (z. B. `0.1.0`, sichtbar im Store) und `CURRENT_PROJECT_VERSION`
      (Buildnummer, bei jedem Upload um 1 erhöhen) in `project.yml`. Gleiche Version + neue Buildnummer = neuer
      TestFlight-Build; neue Store-Version = neue `MARKETING_VERSION`.
- [ ] *Product* › *Archive* (Schema EstrichONE, Gerät „Any iOS Device“), dann im Organizer *Distribute App* › *App Store
      Connect* › *Upload*. Alternativ `xcodebuild archive` + `xcrun altool`/Transporter.
- [ ] Nach dem Upload: E-Mail „ITMS-…“-Hinweise prüfen (fehlende Nutzungstexte, Privacy Manifest) – sollten keine kommen.

### C. App Store Connect – App anlegen

- [ ] *Apps* › „+“ › Neue App: Plattform iOS, Name (Abschnitt 2), Primärsprache **Deutsch**, Bundle-ID, SKU
      (`estrichone-ios`), Zugriff „Vollständig“.
- [ ] **Kategorie:** Primär *Wirtschaft* (Business), Sekundär *Produktivität*.
- [ ] **Altersfreigabe:** nach `AppStore/texte/de-DE/age_rating.md` (Chat und Nutzerinhalte nur innerhalb des
      Betriebs, sonst überall „Keine“/„Nein“) → erwartet **4+**.
- [ ] **Preis:** kostenlos; Verfügbarkeit: Deutschland, Österreich, Schweiz (oder alle Länder).
- [ ] **Geräte:** iPhone und iPad (Universal). Mac (Apple Silicon) „nicht verfügbar machen“ empfohlen, da nicht getestet.
- [ ] **Texte, URLs, Screenshots:** Abschnitt 2 und 7.
- [ ] **App-Datenschutz** („Nährwertangaben“): Abschnitt 4; **Datenschutzrichtlinie-URL** Pflicht.
- [ ] **Exportkonformität:** wird durch `ITSAppUsesNonExemptEncryption = NO` automatisch beantwortet (Abschnitt 5).
- [ ] **App-Review-Informationen:** Kontakt (Name, Telefon, E-Mail), Anmeldung „nicht erforderlich“ (Demomodus),
      Notizen aus Abschnitt 3.

### D. TestFlight

- [ ] Build erscheint nach 10–30 Minuten unter *TestFlight*. **Interne Tester** (bis 100, Mitglieder des Teams): sofort.
- [ ] **Externe Tester** (Bauleiter, Kolonnen): Gruppe anlegen, „Was testen?“ ausfüllen, erste Beta-Prüfung abwarten
      (meist < 24 h), öffentlichen Link oder E-Mail-Einladungen verschicken.
- [ ] Pushnachrichten mit TestFlight testen: Server auf **Produktion** (`ApnsProduction`), da TestFlight-Builds das
      Produktions-APNs nutzen.

### E. Einreichen

- [ ] Unter der App-Version den Build wählen, „Zur Prüfung hinzufügen“, Versionsfreigabe „manuell“ oder „automatisch“.
- [ ] Nach Freigabe: Version veröffentlichen; Aktualisierungen später mit neuer `MARKETING_VERSION`.

### Im Projekt erledigt

| Punkt | Wo |
|---|---|
| Exportkonformität `ITSAppUsesNonExemptEncryption = NO` | `EstrichONE/Config/Info.plist`, `Info-Debug.plist` |
| Privacy Manifest (Required-Reason-APIs, erhobene Daten, kein Tracking) | `EstrichONE/Resources/PrivacyInfo.xcprivacy` |
| Nutzungstexte Kamera, Mikrofon, Standort, Kontakte, Face ID, lokales Netzwerk, Fotos sichern | `project.yml` (`INFOPLIST_KEY_*`) |
| ATS ohne Ausnahmen im Release (nur HTTPS); `NSAllowsLocalNetworking` nur im Debug-Build | `Info.plist` / `Info-Debug.plist` |
| Push-Entitlement, APNs-Registrierung, Postfach, Deep Links | `Config/EstrichONE.entitlements`, `App/PushNotifications.swift` |
| Konto-Löschung in der App (Mehr › „Konto löschen beantragen“) | `Home/MoreView.swift` |
| Demomodus ohne Server für App Review | Startbildschirm „Demo ausprobieren – ohne Server“ |
| App-Umschalter zeigt keine Inhalte (deckende Fläche mit Bildmarke) | `App/EstrichONEApp.swift` (`PrivacyCover`) |
| App-Icon 1024 px ohne Alpha, Startbildschirm | `Resources/Assets.xcassets` |
| Screenshots 6,9" / 6,5" / iPad 13", Texte, Webseiten | `AppStore/upload/`, `AppStore/texte/`, `AppStore/web/` |
| Zeichengrenzen der Texte, Pixelmaße/sRGB/Alpha der Bilder geprüft | `AppStore/texte/pruefen.py`, `AppStore/frame.sh --pruefen` |

## 2. Store-Texte (Deutsch)

Maßgeblich sind die Dateien in `AppStore/texte/de-DE/` (englisch: `texte/en-US/`); hier zum Lesen.

**Name** (max. 30): `EstrichONE`

**Untertitel** (max. 30): `Bauakte, Dispo und Baustelle`

**Werbetext** (max. 170, jederzeit änderbar):
> Die Baustellen-App für Estrichbetriebe: Bauakte, Fotos und Dokumente offline, Wer fährt wohin, Schnelldisposition und Chat mit dem Büro – sicher auf Ihrem Server.

**Beschreibung** (max. 4000):

> EstrichONE ist die Baustellen-App für Estrichbetriebe – für Bauleitung, Kolonnen und Büro. Die App verbindet sich mit dem EstrichONE-Server Ihres Betriebs und funktioniert auch ohne Netz: Alles wird verschlüsselt auf dem Gerät gespeichert und übertragen, sobald wieder Verbindung besteht.
>
> HEUTE AUF EINEN BLICK
> • Mein Einsatz mit Adresse, Route und Ansprechpartner
> • Schnellzugriff auf Meine Aufgaben, Wer fährt wohin und Meine Woche
> • Benachrichtigungen, wenn der Plan freigegeben oder geändert wird
>
> BAUAKTE IMMER DABEI
> • Fotos und Videos mit Beschriftung, Dokumente scannen als PDF
> • Bautagebuch, Mängel, Aufgaben und Notizen
> • Erstbegehung mit Prüfpunkten, Abnahme mit Unterschrift als PDF
> • CM-Messung, Räume mit Flächen und Estrichaufbau
> • Beteiligte und Kontakte verwalten – anrufen, schreiben, Route starten
>
> DISPOSITION UNTERWEGS
> • Wer fährt wohin: Kolonnen, Fahrzeuge und Baustellen in Fahrtreihenfolge
> • Schnelldisposition: Baustelle zuweisen, verschieben, auf den nächsten freien Tag legen
> • Mitarbeiter, Fahrzeuge oder ganze Kolonnen tauschen – mit Konfliktprüfung
> • Plan freigeben und Änderungen seit der Freigabe im Blick behalten
>
> KOMMUNIKATION
> • Chat zwischen Büro und Baustelle – Einzel- und Kolonnenchats
> • Helpdesk: Problem melden mit Foto, Antworten im Verlauf
> • Pushnachrichten zu Zuweisungen, Planänderungen und Freigaben
>
> ZUVERLÄSSIG UND SICHER
> • Ihre Daten liegen auf dem Server Ihres Betriebs – nicht bei uns
> • Verschlüsselte Ablage auf dem Gerät, Entsperren mit Face ID oder PIN
> • Schutz vor Datenverlust: „Nur Ansicht“, wenn der Server länger nicht erreichbar ist
> • Hinweise bei Wartung und bei Ausfall der Datensicherung
> • Persönliche Geräte und Poolgeräte mit Benutzerwechsel
> • Kein Tracking, keine Werbung
>
> Zum Ausprobieren ohne Server: „Demo ausprobieren“ auf dem Startbildschirm.
> Für den Betrieb wird ein EstrichONE-Server benötigt (kostenlos, selbst gehostet).

**Schlüsselwörter** (max. 100 Zeichen, Komma ohne Leerzeichen):
`Estrich,Bauakte,Baustelle,Disposition,Bautagebuch,Abnahme,Kolonne,Handwerk,Fußboden,Bauleiter,Mängel`

**Neuerungen dieser Version** (0.1.0):
> Erste Version von EstrichONE:
> • Heute: Ihr Einsatz mit Route, Aufgaben und Wer fährt wohin
> • Bauakte offline: Fotos, Scans, Bautagebuch, Mängel, Räume
> • Erstbegehung und Abnahme mit Unterschrift als PDF
> • Wer fährt wohin mit Schnelldisposition und Freigabe
> • Chat mit Büro und Kolonnen, Benachrichtigungen
> • Kunden mit Anruf, E-Mail und Route
> • Helpdesk: Problem melden direkt aus der App

**URLs:** Support-URL (Pflicht) `support_url.txt`, Marketing-URL (optional) `marketing_url.txt`, Datenschutz-URL
(Pflicht) `privacy_url.txt` – Platzhalter, bis die Seiten aus `AppStore/web/` online sind.
**Copyright:** `copyright.txt` (`2026 <Firmenname>`).

## 3. Hinweise für App Review (App-Review-Informationen › Anmerkungen)

Anmeldung erforderlich: **NEIN** (nicht anhaken – der Demomodus braucht kein Konto). Text: `texte/de-DE/review_notes.txt`
(Deutsch) oder `review_notes_en.txt` (Englisch).

> EstrichONE ist eine Baustellen-App für Estrichbetriebe. Sie verbindet sich mit einem Server, den jeder Betrieb selbst betreibt; deshalb gibt es keine öffentliche Registrierung.
>
> So prüfen Sie die App ohne Server: Auf dem ersten Bildschirm „Demo ausprobieren – ohne Server“ tippen. Die App läuft dann ausschließlich mit lokalen Beispieldaten; es werden keine Daten über das Netzwerk gesendet. Alle Funktionen sind nutzbar: Heute-Seite, Bauakte (Fotos, Dokumentenscanner, Videos, Bautagebuch, Erstbegehung und Abnahme mit Unterschrift), Disposition („Wer fährt wohin“: zuweisen, verschieben, Mitarbeiter/Fahrzeuge/Kolonnen tauschen, Plan freigeben), Kunden, Chat, Helpdesk und Benachrichtigungen (Mehr › Benachrichtigungen › „Beispiel senden“ zeigt nach wenigen Sekunden eine lokale Beispielmitteilung). Unter „Demo“ › „Störungen vorführen“ lassen sich Wartungsmodus, Server-Ausfall und Backup-Warnungen zeigen. Beenden: „Demo“ (oben links) › „Demo beenden“.
>
> Berechtigungen werden nur bei ausdrücklicher Aktion abgefragt: Kamera (Fotos, Dokumentenscanner, QR-Code zur Einrichtung), Mikrofon (Videoaufnahme), Standort (Baustellenadresse übernehmen), Kontakte (Kunden in Kontakte sichern), Mitteilungen, Face ID (Entsperren auf persönlichen Geräten).
>
> Benutzerkonten legt die Administration des Betriebs an. Die Löschung des eigenen Kontos kann in der App beantragt werden (Mehr › „Konto löschen beantragen“); die Administration löscht daraufhin Konto und personenbezogene Daten. Abmelden entfernt sofort alle Daten vom Gerät.
>
> Verschlüsselung: ausschließlich Standardverfahren von iOS (HTTPS/TLS, CryptoKit AES-GCM, Schlüsselbund). ITSAppUsesNonExemptEncryption = NO.

## 4. App-Datenschutz („Nährwertangaben“) – passend zum Privacy Manifest

Grundsatz: Daten gehen nur an den **Server des jeweiligen Betriebs** und dienen ausschließlich der App-Funktion. Kein
Tracking, keine Werbung, keine Analyse- oder Absturz-SDKs Dritter. Da der Anbieter der App die Server meist nicht
betreibt, wäre auch „Keine Daten erfasst“ vertretbar; empfohlen ist die vorsichtige Angabe unten (deckt Kunden ab, die
den Server von Ihnen betreiben lassen) – identisch mit `PrivacyInfo.xcprivacy`.

Frage „Erfassen Sie oder Drittpartner Daten?“ → **Ja**. Für jeden Typ: *Zweck* **App-Funktionalität**,
*mit Identität verknüpft* **Ja**, *Tracking* **Nein**.

| App Store Connect | Privacy Manifest | Wofür |
|---|---|---|
| Kontaktinformationen › Name | `NSPrivacyCollectedDataTypeName` | Anzeigename des Benutzers (vom Betrieb angelegt) |
| Kennungen › Nutzer-ID | `…UserID` | Benutzerkonto beim Server |
| Kennungen › Geräte-ID | `…DeviceID` | Geräteregistrierung (Gerätesperre, Poolgeräte); keine Werbe-ID |
| Nutzerinhalte › Fotos oder Videos | `…PhotosorVideos` | Baustellenfotos und -videos in der Bauakte |
| Nutzerinhalte › Kundensupport | `…CustomerSupport` | Meldungen an die IT („Problem melden“) |
| Nutzerinhalte › Andere Nutzerinhalte | `…OtherUserContent` | Bautagebuch, Mängel, Protokolle, Unterschriften, Notizen, Chat |
| Standort › Genauer Standort | `…PreciseLocation` | nur auf Knopfdruck: Adresse einer Baustelle, Aufnahmeort eines Fotos |
| Diagnose › Andere Diagnosedaten | `…OtherDiagnosticData` | nur im freiwilligen Fehlerbericht (App-Version, Speicher, Abgleichstand) |

Nicht erfasst: E-Mail/Telefon des Benutzers, Finanz-, Gesundheits-, Browser-, Such-, Kaufdaten, Kontakte des Adressbuchs
(nur Schreiben auf Wunsch), Werbedaten. Required-Reason-APIs im Manifest: UserDefaults (`CA92.1`), Datei-Zeitstempel
(`C617.1`), freier Speicher (`7D9E.1`, nur Fehlerbericht).

## 5. Verschlüsselung und Exportkonformität

`ITSAppUsesNonExemptEncryption = NO` (Info.plist): Die App nutzt **ausschließlich die im Betriebssystem enthaltene
Standard-Kryptografie** zum Schutz von Daten des Benutzers bzw. zur Authentifizierung. Nach den US-Exportregeln (EAR
Kategorie 5 Teil 2, Note 3 / §740.17(b)(1) bzw. Ausnahme für Authentifizierung und Datenschutz) und Apples Vorgaben ist
dafür keine Exportdokumentation (kein CCATS, kein jährlicher Selbstbericht) nötig: keine eigenen oder proprietären
Verschlüsselungsverfahren, keine Krypto-Bibliotheken Dritter in der App, Verschlüsselung dient nicht als Hauptfunktion.
In App Store Connect erscheint die Frage dadurch nicht mehr. (Für Frankreich ist keine gesonderte Erklärung nötig, da nur
Standard-Krypto zum Datenschutz.) Ändert sich das – z. B. eigene Ende-zu-Ende-Verschlüsselung zwischen Nutzern –, neu bewerten.

**Krypto-Inventar** (alles Apple CryptoKit/Security/URLSession; unter Linux nur für Tests swift-crypto):

| Zweck | Verfahren | Ort |
|---|---|---|
| Transport | TLS 1.2/1.3 über `URLSession`, ATS ohne Ausnahmen im Release | System |
| Geräteschlüssel (Nachweis gegenüber dem Server) | ECDSA P-256 / SHA-256, privater Schlüssel in der **Secure Enclave** (sonst Schlüsselbund) | `EstrichCore/Security/DeviceKeys.swift` |
| Datenschlüssel umhüllen | ECIES: P-256-Schlüsselvereinbarung → HKDF-SHA256 → **AES-256-GCM** | `DeviceKeys.swift` |
| PIN-Schutz des Benutzertresors | PBKDF2-HMAC-SHA256, 210 000 Iterationen, zufälliges Salz → AES-256-GCM | `Security/UserVault.swift`, `PBKDF2.swift` |
| Lokale Datenbank | SQLite im Arbeitsspeicher, Abbild mit **AES-256-GCM** versiegelt, atomar geschrieben | `Storage/EncryptedDatabase.swift` |
| Fotos und Dokumente | je Datei **AES-256-GCM**, Dateischutz `completeUntilFirstUserAuthentication` (Upload im Hintergrund) | `Storage/FileVault.swift` |
| Exporte zum Teilen (PDF) | Dateischutz `complete`, temporär | `BauakteDetailView`, `DocumentViews`, `InspectionProtocolView` |
| Geheimnisse (Tokens, Schlüssel) | Schlüsselbund, `kSecAttrAccessibleWhenUnlockedThisDeviceOnly` (kein Backup, kein Gerätewechsel) | `Security/SecretStore.swift` |
| Prüfsummen | SHA-256 (Dateien, Anhänge) | `Helpdesk/Tickets.swift` u. a. |

## 6. Sicherheit – Stand und Entscheidungen

- **ATS:** Release nur HTTPS ohne Ausnahmen. Server im Firmennetz brauchen ein gültiges Zertifikat (z. B. Let's Encrypt
  per DNS-Challenge oder eigene, auf den Geräten per MDM installierte CA). `NSAllowsLocalNetworking` gibt es nur im
  Debug-Build für Testserver. `NSLocalNetworkUsageDescription` erklärt die Abfrage bei Servern im LAN.
- **Zertifikats-Pinning:** bewusst **nicht** erzwungen – jeder Betrieb hostet selbst mit eigenen Zertifikaten,
  Zertifikatswechsel würden die App aussperren. Die Systemprüfung (ATS) gilt; kein „Trotzdem fortfahren“.
- **Sperre:** persönliche Geräte entsperren mit Face ID/Touch ID/Gerätecode (`NSFaceIDUsageDescription`), Poolgeräte
  mit PIN und Benutzerwechsel nach Inaktivität. Der Server kann Geräte sperren oder entfernen.
- **App-Umschalter:** deckende Fläche mit Bildmarke, sobald die App nicht aktiv ist – keine Inhalte im Vorschaubild.
- **Logs:** keine personenbezogenen Daten in Protokollen; das Diagnoseprotokoll speichert nur Ereigniscodes und Zahlen.
  Push-Inhalte enthalten nur Titel, kurzen Text und Link.
- **Jailbreak-/Debugger-Erkennung:** nicht eingebaut (leicht zu umgehen, bringt Fehlalarme); Schutz liegt in Secure
  Enclave, Schlüsselbund `ThisDeviceOnly`, verschlüsselter Ablage und serverseitiger Gerätesperre.
- **Kontolöschung (Richtlinie 5.1.1(v)):** Konten legt der Betrieb an, die App bietet keine Registrierung. Trotzdem gibt
  es den Weg in der App: Mehr › **„Konto löschen beantragen“** erstellt eine Meldung an die Administration (offline
  vorgemerkt); die Administration löscht das Konto im Web. „Abmelden“ löscht alle Daten sofort vom Gerät.

**Vorlage Datenschutzerklärung** (vollständig zum Hosten: `AppStore/web/datenschutz.html`; Kurzfassung):

> Die App EstrichONE verarbeitet Daten ausschließlich im Auftrag Ihres Arbeitgebers bzw. Betriebs, der den
> EstrichONE-Server selbst betreibt (Verantwortlicher im Sinne der DSGVO ist dieser Betrieb). Übertragen werden nur
> Daten, die Sie in der App erfassen (Bauakten, Fotos, Protokolle, Meldungen), sowie Benutzer- und Gerätekennung zur
> Anmeldung. Standortdaten nur auf Knopfdruck. Es gibt kein Tracking, keine Werbung und keine Weitergabe an Dritte;
> Pushnachrichten werden über den Apple Push Notification Service zugestellt (nur Titel, Kurztext, Link). Auf dem Gerät
> sind alle Daten verschlüsselt. Auskunft, Berichtigung und Löschung: über Ihren Betrieb oder in der App unter
> Mehr › „Konto löschen beantragen“. Kontakt: <Anbieter, Anschrift, E-Mail>.

## 7. Screenshots

Pflicht: iPhone **6,9"** (1320 × 2868) – Apple skaliert für kleinere iPhones; **6,5"** (1284 × 2778) optional; iPad
**13"** (2064 × 2752), da die App auf dem iPad läuft. Je Gerät 1–10 Bilder, die ersten drei erscheinen in der Suche.
JPEG, sRGB mit eingebettetem Profil, ohne Alphakanal, Statusleiste 9:41 mit vollem Empfang und Akku, ohne
Demo-Kennzeichnung, iPad im Vollbild ohne Fenstergriff. Fertig: `AppStore/upload/<gerät>/`, ohne Rahmen
`AppStore/roh/<gerät>/`. Erzeugung und Prüfung: `AppStore/README.md`. Reihenfolge:

1. Heute – „Ihr Tag auf einen Blick“
2. Bauakte – „Die Bauakte immer dabei“
3. Fotos & Dokumente – „Fotos und Scans, sauber abgelegt“
4. Wer fährt wohin – „Wer fährt wohin“
5. Schnelldisposition – „Disponieren in Sekunden“
6. Erstbegehung – „Erstbegehung und Abnahme digital“
7. Chat – „Kurzer Draht ins Büro“
8. Kunden – „Kunden griffbereit“
9. Helpdesk – „Hilfe direkt aus der App“

Zusätzlich `AppStore/banner-1920x886/` (Querformat 1920 × 886 für Website und Werbung). Diese Größe nimmt App Store
Connect nicht an. App-Vorschauvideos sind optional.
