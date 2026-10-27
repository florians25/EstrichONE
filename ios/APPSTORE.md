# EstrichONE im App Store veröffentlichen

Vollständige Checkliste mit vorformulierten Texten. Alles, was im Projekt vorbereitet werden kann, ist erledigt
(Abschnitt „Im Projekt erledigt“); was nur Sie mit Ihrem Apple-Konto tun können, steht in der Checkliste.

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
- [ ] **Altersfreigabe:** Fragebogen überall „Keine“/„Nein“ (keine Gewalt, kein Glücksspiel, kein uneingeschränkter
      Webzugriff, keine Nutzer-zu-Nutzer-Kommunikation mit Fremden – Meldungen gehen nur an die eigene Firma) → **4+**.
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
| Screenshots 6,9" / 6,5" / iPad 13" | `AppStore/screenshots/` |

## 2. Store-Texte (Deutsch)

**Name** (max. 30): `EstrichONE`
– falls vergeben: `EstrichONE – Estrich & Dispo`

**Untertitel** (max. 30): `Bauakte, Baustelle, Dispo`

**Werbetext** (max. 170, jederzeit änderbar):
> Neu: Plan direkt in der App freigeben, Kolonnen tauschen und Benachrichtigungen, wenn sich Ihr Einsatz ändert.

**Beschreibung** (max. 4000):

> EstrichONE ist die Baustellen-App für Estrichbetriebe – für Bauleiter, Kolonnen und Büro. Sie verbindet sich mit
> dem EstrichONE-Server Ihres Betriebs und funktioniert auch ohne Netz: Alles wird verschlüsselt auf dem Gerät
> gespeichert und übertragen, sobald wieder Verbindung besteht.
>
> HEUTE AUF EINEN BLICK
> • Mein Einsatz mit Adresse, Route, Ansprechpartner und Handgriffen vor Ort
> • Schnellzugriff auf Meine Aufgaben, Wer fährt wohin und Meine Woche
> • Benachrichtigungen, wenn der Plan freigegeben oder geändert wird
>
> BAUAKTE IMMER DABEI
> • Fotos und Videos mit Beschriftung, Dokumente scannen als durchsuchbares PDF
> • Bautagebuch, Mängel, Aufgaben und Notizen
> • Erstbegehung mit Prüfpunkten und Abnahme mit Unterschrift als PDF
> • CM-Messung, Räume mit Flächen und Estrichaufbau, Beteiligte und Kontakte
>
> DISPOSITION UNTERWEGS
> • Wer fährt wohin: Kolonnen, Fahrzeuge und Baustellen in Fahrtreihenfolge
> • Schnelldisposition: Baustelle zuweisen, verschieben, auf den nächsten freien Tag legen
> • Mitarbeiter, Fahrzeuge oder ganze Kolonnen tauschen – mit Konfliktprüfung
> • Plan freigeben und Freigabe zurücknehmen, Änderungen seit der Freigabe im Blick
>
> KUNDEN UND HELPDESK
> • Kundenverzeichnis mit Anruf, Nachricht, E-Mail und Route
> • Problem melden direkt aus der App, Antworten der IT im Verlauf
>
> SICHER UND DATENSPARSAM
> • Ihre Daten liegen auf dem Server Ihres Betriebs – nicht bei uns
> • Verschlüsselte Ablage auf dem Gerät, Entsperren mit Face ID oder PIN
> • Persönliche Geräte und Poolgeräte mit Benutzerwechsel
> • Kein Tracking, keine Werbung
>
> Zum Ausprobieren ohne Server: „Demo ausprobieren“ auf dem Startbildschirm.
> Für den Betrieb wird ein EstrichONE-Server benötigt (kostenlos, selbst gehostet).

**Keywords** (max. 100 Zeichen, Komma ohne Leerzeichen):
`Estrich,Bauakte,Baustelle,Disposition,Bautagebuch,Abnahme,Kolonne,Handwerk,CM-Messung,Fußboden,Bau`

**Neuerungen dieser Version** (0.1.0):
> Erste Version: Heute-Seite mit Schnellzugriff, Bauakte offline, Fotos und Scans, Erstbegehung und Abnahme,
> Wer fährt wohin mit Schnelldisposition und Freigabe, Benachrichtigungen, Kunden und Helpdesk.

**URLs:** Support-URL `https://<ihre-domain>/support` (Pflicht, z. B. Kontaktseite der Firma),
Marketing-URL optional, Datenschutz-URL `https://<ihre-domain>/datenschutz` (Pflicht, Vorlage in Abschnitt 6).
**Copyright:** `2026 <Firmenname>`

## 3. Hinweise für App Review (App-Review-Informationen › Notizen)

Anmeldung: „Anmeldung erforderlich“ **nicht** anhaken – der Demomodus braucht kein Konto.

> EstrichONE is a field app for screed (floor) contractors. It connects to a self-hosted company server that each
> business runs itself, so there is no public sign-up.
>
> To review the app without a server, tap “Demo ausprobieren – ohne Server” on the first screen. The app then runs
> with realistic local sample data only; nothing is sent over the network. All features can be used: today screen,
> site files (photos, document scanner, videos, diary, inspection and acceptance with signature), dispatch
> (“Wer fährt wohin”: assign, move, swap people/vehicles/crews, release the plan), customers, helpdesk and the
> notification inbox (Mehr › Benachrichtigungen › “Beispiel senden” shows a local sample notification after a few seconds).
> To leave the demo, tap “Demo” (top left) › “Demo beenden”.
>
> Permissions are only requested on explicit user action: camera (photos, document scanner, enrollment QR code),
> microphone (video recording), location (take over a site address), contacts (save a customer to Contacts),
> notifications (in the inbox), Face ID (unlock on personal devices).
>
> Accounts are created by the company administrator. Users can request deletion of their account in the app
> (Mehr › “Konto löschen beantragen”); the request goes to the administrator, who deletes the account and personal data.
> Signing out removes all data from the device immediately.
>
> Encryption: only standard encryption provided by iOS (HTTPS/TLS, CryptoKit AES-GCM, Keychain). ITSAppUsesNonExemptEncryption = NO.

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
| Nutzerinhalte › Andere Nutzerinhalte | `…OtherUserContent` | Bautagebuch, Mängel, Protokolle, Unterschriften, Notizen |
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

**Vorlage Datenschutzerklärung (Kurzfassung für die Datenschutz-URL):**

> Die App EstrichONE verarbeitet Daten ausschließlich im Auftrag Ihres Arbeitgebers bzw. Betriebs, der den
> EstrichONE-Server selbst betreibt (Verantwortlicher im Sinne der DSGVO ist dieser Betrieb). Übertragen werden nur
> Daten, die Sie in der App erfassen (Bauakten, Fotos, Protokolle, Meldungen), sowie Benutzer- und Gerätekennung zur
> Anmeldung. Standortdaten nur auf Knopfdruck. Es gibt kein Tracking, keine Werbung und keine Weitergabe an Dritte;
> Pushnachrichten werden über den Apple Push Notification Service zugestellt (nur Titel, Kurztext, Link). Auf dem Gerät
> sind alle Daten verschlüsselt. Auskunft, Berichtigung und Löschung: über Ihren Betrieb oder in der App unter
> Mehr › „Konto löschen beantragen“. Kontakt: <Anbieter, Anschrift, E-Mail>.

## 7. Screenshots

Pflicht: iPhone **6,9"** (1320 × 2868) – Apple skaliert für kleinere iPhones; zusätzlich **6,5"** (1284 × 2778) für
ältere Größen empfohlen; iPad **13"** (2064 × 2752), da die App auf dem iPad läuft. Je 3–10 Bilder, erste drei zählen.
Fertige Bilder: `ios/AppStore/screenshots/<gerät>/` – jeweils `roh/` (Originalaufnahme) und gerahmt mit Überschrift.
Erzeugung und Prüfung: `ios/AppStore/README.md`. Reihenfolge:

1. Heute – „Ihr Tag auf einen Blick“
2. Bauakte – „Die Bauakte immer dabei“
3. Fotos & Dokumente – „Fotos und Scans, sauber abgelegt“
4. Wer fährt wohin – „Wer fährt wohin – auf einen Blick“
5. Plan freigeben – „Planen und freigeben unterwegs“
6. Erstbegehung – „Erstbegehung und Abnahme digital“
7. Helpdesk – „Hilfe direkt aus der App“
8. Kunden – „Kunden und Kontakte griffbereit“

App-Vorschauvideos sind optional.
