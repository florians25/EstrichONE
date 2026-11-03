# Sicherheit – Kurzüberblick (Stand Release 0.1.0)

| Bereich | Umsetzung |
|---|---|
| Passwörter | **Argon2id** (m = 19 MiB, t = 2, p = 1 – OWASP-Empfehlung), Salt 16 Byte, PHC-Format; ältere/schwächere Hashes werden bei der Anmeldung neu berechnet. Passwortregeln in `PasswordPolicy`: mindestens 12 Zeichen, Abgleich mit ~49 000 häufigen/geleakten Passwörtern (offline), auch mit angehängten Ziffern/Zeichen. |
| Anmeldung | **Zwei-Faktor-Anmeldung (TOTP)** mit Wiederherstellungscodes, für Administratoren erzwingbar. Sperre nach 5 Fehlversuchen je Konto (Name und E-Mail gemeinsam) und 20 je IP-Adresse, Alarm an die Benutzerverwaltung; zusätzlich Rate-Limit 20 Anfragen/Minute je IP auf `/auth` (einstellbar). Zeitkonstanter Vergleich, Dummy-Hash bei unbekannten Benutzern. |
| Tokens | Keine JWTs: zufällige 256-Bit-Tokens (`eo1_…`/`eor1_…`), in der Datenbank nur als **SHA-256**; Sitzungen serverseitig widerrufbar (Abmelden, Sperren, Gerät löschen). Im Browser nur in `HttpOnly`-, `Secure`-, `SameSite=Strict`-Cookies, dazu XSRF-Token. |
| Gespeicherte Geheimnisse | SMTP-Passwort, Zugänge zu Archivserver und Backup-Zielen, **APNs-Schlüssel (.p8)** und **VAPID-Schlüssel**: ASP.NET Data Protection mit **AES-256-GCM** (neue Schlüssel; ältere AES-256-CBC+HMAC-SHA256-Schlüssel bleiben lesbar und werden nach 90 Tagen turnusgemäß ersetzt). Schlüsselring in `data/files/.keys` (Verzeichnis 0700, Teil der verschlüsselten restic-Sicherung). Geheimnisse werden nie zurückgegeben oder protokolliert. |
| Transport | Caddy: HTTPS (Let's Encrypt bzw. eigenes Zertifikat), HTTP → HTTPS, **HSTS** (1 Jahr, inkl. Subdomains), strikte **CSP** (`script-src 'self'`, `frame-ancestors 'none'`, `object-src 'none'`), `X-Content-Type-Options`, `Referrer-Policy`, `X-Frame-Options: DENY`, `Permissions-Policy`. API-Antworten: `default-src 'none'`, `Cross-Origin-Resource-Policy: same-origin`. |
| Pushnachrichten | Inhalt nur Titel, kurzer Hinweis (≤ 180 Zeichen), Link, IDs – keine Kommentare, Ticket-Texte, Preise, Personaldaten; optional nur „Neue Benachrichtigung“. Web Push Ende-zu-Ende verschlüsselt (RFC 8291), VAPID-Anmeldung (ES256); APNs mit Token-Anmeldung (ES256). Web-Push-Endpunkte nur bei bekannten Push-Diensten per HTTPS (kein SSRF), Links nur relative Pfade. Versand höchstens 20 Nachrichten je 10 Minuten und Benutzer. |
| Rechte | Jeder Endpunkt erfordert Anmeldung (Fallback-Richtlinie) und bei Apps ein freigegebenes Gerät; fachliche Rechte je Endpunkt (`RequirePermission`), automatischer Zugriffstest über alle Routen, keine Rechteausweitung über die Benutzer-/Rollenverwaltung, Modulgrenzen per Architekturtest. Audit-Log für sicherheitsrelevante Aktionen. |
| Dateien | Typprüfung am Inhalt (nicht an der Endung), Bilder höchstens 100 Megapixel, nur JPEG/PNG/WebP/HEIF-Dekoder, Prüfsumme beim Upload, Benutzernamen nie im Dateisystem, Auslieferung mit passender CSP. |

## Geprüft und behoben in Runde 7
- Data Protection auf **AES-256-GCM** umgestellt; Schlüsselverzeichnis nur für den Dienstbenutzer lesbar.
- Neue Geheimnisse (APNs, VAPID) nur verschlüsselt gespeichert.
- Rate-Limits für Nachrichtenversand und Geräte-Registrierung.
- Push-Payload ohne sensible Inhalte, Web-Push-Endpunkte auf Push-Dienste beschränkt.

## Bekannte Grenzen
- Der Schlüsselring liegt unverschlüsselt (aber 0700) im Datenverzeichnis; wer Root auf dem Server hat, kann
  Geheimnisse entschlüsseln. Für höhere Anforderungen ein Zertifikat zum Schutz des Schlüsselrings ergänzen.
- Zertifikats-Pinning in den Apps wird bewusst nicht erzwungen (selbst gehostete Server, eigene Zertifikate).

## App (Backend-API und Web) – Runde 11

Bedrohungsmodell: API und Weboberfläche sind aus dem Internet erreichbar. Angreifer: anonyme Scanner und Bots
(Credential Stuffing, Passwortraten, Ausnutzen anonymer Endpunkte), ein übernommenes Mitarbeiterkonto mit wenigen
Rechten (Baustelle) bzw. mit Benutzerverwaltung (IT-Administration), präparierte Dateien (Bild-/Dekompressionsbomben,
SVG/HTML), gespeichertes XSS über Einstellungen.

Vorgehen: Alle rund 360 Routen (Methode + Pfad) der Routentabelle (auch die nicht in OpenAPI beschriebenen) automatisiert ohne Anmeldung,
mit einem Konto ohne Rollen und als Baustellenkraft mit fremden IDs aufgerufen; Handler mit Objektbezug von Hand
geprüft (Bauakten-Sichtbarkeit `IBauakteAccess`, Chat-Mitgliedschaft, Tickets, Benachrichtigungen, Dateien,
Archiv, Sync, Peer-/Backup-/Hosting-Endpunkte); Abhängigkeiten mit `dotnet list package --vulnerable` und `npm audit`.

| Befund | Schwere | Fix | Test |
|---|---|---|---|
| **Rechteausweitung über die Benutzerverwaltung:** Wer `users.manage` hat (Standardrolle „IT-Administration“), konnte sich selbst die Rolle Administrator geben, das Passwort bzw. die E-Mail eines Administrators ändern oder ihn sperren; wer `roles.manage` hat, konnte Rollen beliebige Rechte geben | **hoch** | `PrivilegeGuard`: Konten anderer nur bearbeiten, zurücksetzen, sperren bzw. Rollen vergeben, wenn man deren Verwaltungsrechte (Benutzer, Rollen, Einstellungen, Backup, Geräte, Protokoll, Archiv, Personalakte) selbst hat – die IT-Administration verwaltet weiter Büro- und Baustellenkonten, aber keine Administratoren; eigenes Konto: keine Rolle mit Rechten, die man nicht schon hat; neue Rechte in einer Rolle nur, wenn man sie selbst hat (gilt für Anlegen, Rollen setzen, Passwort/2FA zurücksetzen, E-Mail ändern, Sperren, Onboarding, Rollen speichern) → 403 `PRIVILEGE_ESCALATION` | `PrivilegeEscalationTests` |
| Keine Zwei-Faktor-Anmeldung; ein erratenes/geleaktes Passwort genügte | **hoch** | TOTP nach RFC 6238 (SHA-1, 6 Ziffern, 30 s, ±1 Schritt, jeder Code nur einmal), Geheimnis mit Data Protection (AES-256-GCM) verschlüsselt, 10 Wiederherstellungscodes (nur SHA-256 gespeichert, je einmal). Einrichten/Ausschalten/Codes erneuern nur mit Passwort (+ Code). Falsche Codes zählen als Fehlversuch (Sperre). Pflicht für Konten mit Verwaltungsrechten einschaltbar (Einstellungen → Anmeldung): bis zur Einrichtung ruhen deren Verwaltungsrechte, Anmelden und Einrichten bleiben möglich; einschalten kann sie nur, wer 2FA selbst nutzt. Administrator kann 2FA eines Kontos zurücksetzen (Handy verloren, beendet alle Sitzungen) | `TwoFactorTests` (RFC-Testvektoren, Wiederverwendung, Wiederherstellungscode, Sperre, Pflicht, Zurücksetzen), Web-E2E `13-sicherheit.spec.ts` |
| Sperre je Konto umgehbar: Anmeldename und E-Mail zählten getrennt (doppelt so viele Versuche) | mittel | Ein Zähler je Konto, egal mit welcher Kennung | `BruteForceTests` |
| Keine Benachrichtigung bei Angriffen | mittel | Beim Erreichen der Sperre (Konto oder IP) Audit `auth.login.attack_detected` und Pflicht-Benachrichtigung „Anmeldeangriff erkannt“ an alle mit `users.manage` (höchstens 1× je Stunde und Konto/Adresse) | `BruteForceTests` |
| Passwortrichtlinie: 10 Zeichen, nur ~90 verbotene Passwörter | mittel | Mindestens **12** Zeichen; Offline-Liste mit ~49 000 häufigen/geleakten Passwörtern (zxcvbn, MIT) plus deutsche Grundwörter; auch „verziert“ („Sommer2026!“, „P@ssw0rd2026“), doppelte Wörter, Wiederholungen und Folgen abgelehnt. Gilt beim Setzen – bestehende Passwörter bleiben gültig | `PasswordPolicyTests`, `PasswordTests` |
| `X-Forwarded-For` von jeder Adresse übernommen: wäre die API (falsch veröffentlichter Port) direkt erreichbar, ließe sich die Sperre je IP mit gefälschten Adressen umgehen | mittel | Nur private Netze/Loopback (Caddy im Docker-Netz, NPM im LAN) und `EstrichOne__TrustedProxies` dürfen die Client-Adresse setzen | `HardeningTests.ForwardedFor_OnlyFromTrustedProxies` |
| Bildbomben: ein kleines PNG mit 40 000 × 40 000 Pixeln wurde beim Erzeugen der Vorschau voll dekodiert (GB an Speicher) | mittel | Höchstens 100 Megapixel und 30 000 Pixel Kante (Kopf wird vor dem Dekodieren geprüft) bei Dateien, Chat-Anhängen, Gruppenbild, Logo → 422 `IMAGE_TOO_LARGE` | `HardeningTests` |
| libvips lud jedes bekannte Format (SVG, PDF, TIFF, Matlab, FITS …) – große Angriffsfläche, Firmenlogo nahm alles an | mittel | Nur JPEG-, PNG-, WebP- und HEIF-Lader freigeschaltet (`ImageSafety`, `Operation.Block`); Logo nur PNG/JPEG/WebP nach Magic Bytes | `HardeningTests` |
| Gespeichertes XSS: Quellenangabe der Karte (Einstellungen) wurde als HTML in Leaflet eingesetzt | mittel (nur mit `settings.manage`) | Nur Text und `<a href="https://…">…</a>`, alles andere maskiert (idempotent) | `HardeningTests.MapAttribution_*` |
| Archiv: Einreihen einer fremden Datei (`POST /archive/files/{id}`) vor der Sichtbarkeitsprüfung; `GET` verriet, ob eine fremde Datei archiviert ist | niedrig | Sichtbarkeit der Datei zuerst prüfen (404) | `EndpointAccessTests.SiteWorker_WithForeignIds_*` |
| Keine Übersicht der eigenen Anmeldungen, kein „überall abmelden“ | mittel | Konto → *Anmeldung und Sicherheit*: aktive Sitzungen (Browser/App, seit, zuletzt aktiv), einzeln oder „überall sonst abmelden“ (wirkt sofort, auch für Apps) | `SessionManagementTests`, E2E |
| Öffentlicher Abnahme-Link (Token im Pfad) stand im Request-Log | niedrig | Pfad im Log maskiert (`/public/acceptances/***`) | `HardeningTests.RequestLog_*` |
| Hosting-Schnittstelle `PUT /system/notices` ohne Rate-Limit | niedrig | Rate-Limit wie Anmeldung | – |
| Abhängigkeiten: MailKit 4.18.1, ASP.NET/EF 10.0.13; npm-Werkzeuge (UI5-Tooling) mit 5 hohen und 26 mittleren Meldungen (pacote, sigstore, minimatch, glob, body-parser …) | mittel (nur Build-Werkzeuge) | MailKit 4.18.2, .NET-Pakete 10.0.14, ui5-tooling-modules 3.42, ui5-tooling-transpile 3.14, `overrides` auf gepatchte Versionen → `npm audit`: 0. Laufzeit-Pakete der Weboberfläche (leaflet, qrcode) ohne Meldung | CI: `backend.yml` bricht bei verwundbaren NuGet-Paketen ab, `web.yml` mit `npm run audit` (Laufzeit ab „moderate“, Werkzeuge ab „high“) |
| CSP/Header | niedrig | CSP zusätzlich mit `media-src`, `worker-src`, `manifest-src`, `frame-src` (weiter ohne `unsafe-eval` und ohne Inline-Skripte), `Permissions-Policy` sperrt Zahlung, USB, Seriell, Bluetooth, HID, MIDI, Bildschirmaufnahme, Topics; `Cross-Origin-Opener-Policy: same-origin`; `/.well-known/security.txt` (RFC 9116) | `web/test/securityHeaders.test.ts` |

Automatischer Zugriffstest (`backend/tests/…/Security/EndpointAccessTests.cs`): liest **alle** Routen aus der
Routentabelle; ohne Anmeldung muss jede 401 liefern, mit einem Konto ohne Rollen jede 403 – außer den beiden
begründeten Listen im Test (öffentlich bzw. mit eigenem Geheimnis: Anmeldung, Einrichtung, öffentlicher Abnahme-Link,
Backup-Spiegel, Hosting-Konsole; ohne Recht: eigenes Konto, eigene Benachrichtigungen/Tickets/Sitzungen, Sync mit
Prüfung je Objekt). Neue anonyme Endpunkte ohne Eintrag lassen den Test scheitern. Dazu greift eine Baustellenkraft
mit den IDs fremder Bauakten, Dateien, Notizen, Aufgaben, Chats, Nachrichten, Anhänge, Tickets und
Benachrichtigungen auf alle passenden Routen zu (nur 403/404 bzw. abgelehnte Eingaben) und nichts ändert sich.

Geprüft und in Ordnung: Fallback-Richtlinie „angemeldet + freigegebenes Gerät“; Bauakten-Sichtbarkeit zentral über
`IBauakteAccess` (Listen, Details, Dateien, Exporte, Berichte, Sync); Chat nur für Mitglieder; Tickets/Benachrichtigungen
nur eigene; Cookies `HttpOnly`, `Secure`, `SameSite=Strict`, CSRF-Doppel-Token für alle schreibenden Cookie-Anfragen;
Abmelden widerruft serverseitig; Fehlerantworten ohne Stacktrace (Problem-JSON mit `traceId`); keine Tokens/Passwörter
in Logs (Query-Strings werden nicht protokolliert); Uploads mit Größenlimits je Endpunkt, Typ per Magic Bytes, Auslieferung
von Nicht-Bildern als Download mit `nosniff` und `default-src 'none'`; ZIP-Importe gibt es nicht (Kontaktimport CSV,
5 MB); PDF-Verdichtung lässt signierte/verschlüsselte PDFs unverändert; SQL ausschließlich über EF Core (parametrisiert);
Web-Push nur an bekannte Push-Dienste per HTTPS; Peer-Endpunkte nur mit Kopplungstoken; Chat-Links nur `http(s)`,
alle Texte maskiert.

### Was Betreiber tun sollten

1. **Zwei-Faktor-Anmeldung einrichten:** Konto (Benutzermenü) → *Anmeldung und Sicherheit* → *Einrichten*, QR-Code mit
   einer Authenticator-App scannen, Wiederherstellungscodes ausdrucken.
2. Danach **Pflicht für Administratoren** einschalten: *System → Firmendaten → Anmeldung → Zwei-Faktor-Anmeldung:
   Pflicht für Administratoren*. Konten mit Verwaltungsrechten sehen dann einen Hinweis und richten 2FA ein.
3. Rolle „IT-Administration“ und eigene Rollen mit `users.manage` prüfen – sie können keine Administratoren mehr
   übernehmen, aber weiterhin alle Konten mit weniger Rechten verwalten.
4. Unbekannte Sitzungen unter *Anmeldung und Sicherheit* beenden; bei Verdacht „überall sonst abmelden“ und Passwort ändern.
5. Steht der Reverse Proxy (NPM) auf einem anderen Rechner mit **öffentlicher** Adresse, diese Adresse in der `.env` als
   `ESTRICHONE_TRUSTED_PROXIES="private_ranges 203.0.113.5"` eintragen (Installer: `-e ESTRICHONE_TRUSTED_PROXIES=203.0.113.5`)
   und `docker compose up -d`. Compose gibt dieselbe Liste an Caddy und an die API (`EstrichOne__TrustedProxies`);
   private Netze sind für die API immer vertrauenswürdig. Prüfung: `docker compose config` (CI, backend.yml).
6. `security.txt` nennt die Meldestelle des Projekts; eigene Kontaktdaten können Betreiber über den Reverse Proxy ergänzen.

### iOS/Android/Windows-App und Zwei-Faktor-Anmeldung (für das jeweilige App-Paket)

- Die Apps funktionieren mit 2FA-Konten weiter: **bestehende Sitzungen** laufen unverändert weiter, das Erneuern
  (`POST /api/v1/auth/refresh`, Gerätesignatur) braucht nie einen Code. Ein Gerät fragt den Code nur bei einer **neuen
  Anmeldung** (Erstanmeldung, nach Abmelden, Passwortwechsel, Gerät gesperrt oder nach Ablauf der Geräte-Leerlaufzeit).
- Anmeldung: `POST /api/v1/auth/login` wie bisher; antwortet der Server **401 mit `code: "TWO_FACTOR_REQUIRED"`**, zeigt
  die App ein Feld „Bestätigungscode“ (iOS: `textContentType = .oneTimeCode`, Tastatur Ziffern, aber auch
  Wiederherstellungscodes `xxxxx-xxxxx` zulassen) und sendet dieselbe Anmeldung mit `twoFactorCode` erneut.
  `TWO_FACTOR_INVALID` (401): Code falsch/abgelaufen, Feld leeren, erneut fragen. `LOGIN_LOCKED` (429) wie bisher.
- Status für eine Einstellungsseite: `GET /api/v1/auth/two-factor` (auch für noch nicht freigegebene Geräte);
  Einrichten über die App ist optional (`/setup`, `/enable` wie im Web), die Einrichtung im Web genügt.
- **Umgesetzt (Nachtrag Runde 11):** iOS-App (persönliche Geräte und Benutzerwechsel am Poolgerät) und Android-App fragen
  nach `TWO_FACTOR_REQUIRED` den Code ab (iOS: `.oneTimeCode`, Ziffernblock, sechste Ziffer prüft sofort; Umschalter
  „Wiederherstellungscode verwenden“ mit Texttastatur), wiederholen dieselbe Anmeldung mit `twoFactorCode` und zeigen
  `TWO_FACTOR_INVALID` am Feld an. Demomodus: Konto `s.berger` / `demo`, Code `246810` bzw. Wiederherstellungscode
  `abcde-23456`. Tests: EstrichCore (`AccountServiceTests`, `LiveServerTests.oneTimePasswordAndTwoFactor` gegen den echten
  Server im Linux-Job), UI-Test `testTwoFactorSignInOnPoolDevice`, Android `LiveServerTests.liveOneTimePasswordAndTwoFactor`.
  Die Windows-App (Desktop) ist nicht Teil dieses Nachtrags.

### Bekannte Grenzen (App)

- ~~`mustChangePassword` nur von den Clients erzwungen~~ – **behoben (Nachtrag Runde 11):** Solange ein Einmalpasswort der
  Administration aktiv ist, lehnt die API jeden angemeldeten Aufruf mit **403 `PASSWORD_CHANGE_REQUIRED`** ab – außer
  Passwort ändern (auch vor der Gerätefreigabe, damit die Apps direkt nach der Anmeldung wechseln können), Abmelden,
  `GET /auth/me` und Zwei-Faktor-Status/-Einrichtung (`/auth/two-factor`, `/setup`, `/enable`); öffentliche Endpunkte
  bleiben unberührt. Der Zustand wird bei jeder Anfrage aus der Datenbank gelesen, nach dem Wechsel gilt dieselbe Sitzung
  sofort voll (`PasswordChangeGateTests`). Web: führt nach Anmeldung, Neuladen oder jeder Antwort mit diesem Code zum
  Pflichtwechsel (`api.test.ts`, E2E `03-verwaltung`); iOS/Android: Bildschirm „Neues Passwort“ vor der Gerätebindung.
  Während einer laufenden Sitzung tritt der Fall praktisch nicht auf (Zurücksetzen beendet alle Sitzungen); die Apps zeigen
  dann die Meldung des Servers.
- Wer `settings.manage` hat, kann weiterhin Kartenserver, SMTP-Server und Archiv-/Backup-Adressen festlegen (Verbindungen
  vom Server zu frei wählbaren Zielen) – bewusst, da Administratorfunktion.
- Bereits vor diesem Stand hochgeladene, sehr große Bilder werden nicht nachträglich geprüft (nur neue Uploads).

## Betrieb (öffentlich erreichbar) – Runde 11

Bedrohungsmodell: Der Hauptserver ist aus dem Internet erreichbar (hinter Nginx Proxy Manager, Betriebsart `proxy`,
HTTP 8181 – oder direkt mit eigenem HTTPS, Betriebsart `direkt`, 8143/443). Angreifer: automatisierte Scanner,
Credential Stuffing, Überlastung (Slowloris, Request-Fluten), bekannte CVEs in Basis-Images, Umgehen des Proxys.

| Befund | Schwere | Fix | Test |
|---|---|---|---|
| Basis-Images nur per Tag (`caddy:2`, `postgres:17-alpine`, `docker:28-cli` …), keine Prüfung auf bekannte Schwachstellen | hoch | Alle Basis-Images mit Version **und Digest** gepinnt; Dependabot aktualisiert Tag und Digest (Docker, Compose, NuGet, npm, Actions); Alpine-Pakete beim Bau aktualisiert (`apk upgrade`) | Workflow *Image-Scan* (`images.yml`): Trivy über alle 7 ausgelieferten Images, **Fehlschlag bei kritischen, behebbaren CVEs**, nächtlich gegen die aktuelle Datenbank |
| Kritische CVEs in Go-Binärdateien: restic 0.18.0, rest-server 0.14.0 (Go 1.24.3), Docker-CLI/Compose 28 (crypto/tls, crypto/x509), `gosu` im Backup-Image | kritisch | restic 0.19.1; rest-server aus den Quellen mit Go 1.27 gebaut; Docker-CLI 29.9.2 (Installer, Hosting); `gosu` aus dem Backup-Image entfernt (Dienst läuft fest als `backup`) | Trivy-Scan je Image; Backup/Restore- und Installer-E2E mit restic 0.19.1 |
| Kein Rate-Limit vor der API: Anmelde-/Kopplungsfluten erreichen ungebremst den API-Prozess | hoch | Caddy mit `caddy-ratelimit` (fest gepinnte Version): Anmeldung/Ersteinrichtung/Koppeln 30/min, Sitzung erneuern 300/min, übrige API 3000/min je Client-Adresse (`ESTRICHONE_RATE_*`); Antwort 429 mit `Retry-After` und Problem-JSON `RATE_LIMITED`. Die API begrenzt Anmeldungen zusätzlich selbst (Abschnitt App) | `infra.yml`, Installer-Job: 31 Aufrufe → 429 mit Retry-After, ohne Server-Header |
| Keine Zeitgrenzen (Slowloris), Kopfzeilen bis 1 MB | mittel | `read_header 15s`, `idle 5m`, `read_body 30m` (große Videos), Kopfzeilen ≤ 64 KB; Anmelde-Anfragen ≤ 1 MB | Caddyfile beim Image-Bau geprüft (`caddy adapt`) |
| `Server`/`Via`-Header verraten Software; Fehlerseiten mit `Server: Caddy` | niedrig | `-Server`, `-Via` auch auf Fehlerantworten | `infra.yml`: kein `Server:`/`Via:` |
| Client-Adresse aus `X-Forwarded-For` von links gelesen | mittel | `trusted_proxies_strict` (von rechts, nur vertrauenswürdige Proxys), nur `X-Forwarded-For`; Proxy-Modus vertraut nur privaten Netzen | E2E hinter Nginx (web.yml) |
| `ESTRICHONE_TRUSTED_PROXIES` erreichte nur Caddy: mit öffentlichem NPM sah die API weiter nur dessen Adresse (Sperre/Rate-Limit je IP trafen alle gemeinsam) | mittel | Compose gibt dieselbe `.env`-Liste als `EstrichOne__TrustedProxies` an die API (Schreibweise wie bei Caddy, `private_ranges` wird erkannt); Installer `-e ESTRICHONE_TRUSTED_PROXIES=…` prüft die Einträge (Nachtrag Runde 11) | backend.yml: `docker compose config`; `HardeningTests.ForwardedFor_OnlyFromTrustedProxies` |
| PostgreSQL und Caddy ohne Härtung (schreibbares Dateisystem, alle Standard-Capabilities) | mittel | Beide `read_only` mit tmpfs, `no-new-privileges`, `cap_drop: ALL` (PostgreSQL nur CHOWN/DAC_OVERRIDE/FOWNER/SETUID/SETGID für den Einstieg, Caddy nur NET_BIND_SERVICE); bestehende Datenverzeichnisse laufen unverändert weiter | `infra.yml`: `docker inspect` aller Dienste nach dem Update einer alten Installation |
| Keine Prozess-/Speichergrenzen, unbegrenzte Container-Protokolle (Platte voll) | mittel | `pids_limit`, großzügige `mem_limit` (`ESTRICHONE_MEMORY_*`), Log-Rotation 5 × 20 MB | wie oben |
| Port 8181 (Proxy-Modus, nur HTTP) aus dem Internet erreichbar → Proxy und HTTPS umgehbar; Docker-Ports umgehen ufw | hoch (Konfiguration) | `ESTRICHONE_BIND` / Installer `-e ESTRICHONE_BIND_ADDRESS=…` bindet an eine Adresse; Checkliste unten (Firewall des Routers, `DOCKER-USER`) | Compose-Prüfung (backend.yml) |
| `.env` (Datenbank- und Sicherungspasswort) bei älteren Installationen evtl. lesbar für alle | mittel | Installer und `update.sh` setzen bei jedem Lauf `0600` (Suite und Hosting-Konsole) | `infra.yml`: `stat` nach dem Update von der letzten Version |
| Hosting-Konsole nur mit Anmeldung geschützt | mittel | Optionale IP-Allowlist `HOSTING_ALLOWED_NETWORKS` (403 auch für Anmeldung/Oberfläche; Loopback als Notzugang, `/health` frei); Anmeldung mit 2FA, Sperre, Rate-Limit wie bisher | `hosting/tests/…/IpAllowlistTests.cs` |
| Sicherheitsupdates nur manuell, Administratoren erfahren nichts davon | hoch | `update.sh --automatik sicherheit` bzw. `patch` (stündliche Prüfung, Einspielen nur im Zeitfenster, Sicherung vorher); Hinweis „Sicherheitsupdate verfügbar“ in der Web-Shell für Rechte `backup.manage` | `infra.yml`: alte Installation → automatisches Sicherheitsupdate über einen Test-Feed; `UpdateCheckTests`, `updateStatus.test.ts` |

Geprüft und unverändert in Ordnung: Peer-Endpunkte (Archivserver, Backup-Ziele) sind nur über die bei der Kopplung
gepinnte CA erreichbar (HMAC-Kopplungsbeweis, kein Vertrauen in öffentliche CAs); ziehende Backup-Ziele nur mit
Spiegel-Token. Installer und `setup.sh` erzeugen Datenbank- (40 Zeichen) und Sicherungspasswort (48 Zeichen) aus
`/dev/urandom`. Den Docker-Socket bekommt **kein** Dienst der Suite – nur der Installer/`update.sh` (einmaliger Lauf
durch root), die Updater-Container der Zusatzserver (`peer.sh`, Fernaktualisierung) und die Hosting-Konsole (steuert
die Mandanten, im Container mit minimalen Capabilities). Zugriff auf den Socket entspricht Root auf dem Host.

### Sicherheitsupdates

- **Hinweis im Web:** Administratoren (Recht „Backups verwalten“) sehen oben in der Shell „Sicherheitsupdate
  verfügbar: EstrichONE x.y.z …“ mit dem Befehl für den Server. Grundlage ist der Release-Feed
  (`GET /api/v1/system/updates`, alle 6 Stunden zwischengespeichert) bzw. ohne Internet der Stand, den `update.sh` in
  `data/backup-status/update.json` schreibt (`EstrichOne__Updates__Enabled=false` schaltet den Abruf ab).
- **Was zählt als Sicherheitsupdate:** ein Release derselben Versionslinie (x.y), neuer als installiert, dessen Titel oder
  Versionshinweise (`docs/releases/<tag>.md`) „**Sicherheitsupdate**“ enthalten (z. B. Überschrift `## Sicherheitsupdate`).
  Vorabversionen nur, solange bereits eine Vorabversion installiert ist. Neue Versionslinien (0.2 …) nie automatisch.
- **Automatisch einspielen** (empfohlen):

  ```bash
  sudo /opt/estrichone/update.sh --automatik sicherheit            # nur Sicherheitsupdates, nachts 02:00-05:00
  sudo /opt/estrichone/update.sh --automatik patch 01:00-04:00     # alle Fehlerbehebungen der Versionslinie
  sudo /opt/estrichone/update.sh --automatik aus
  sudo /opt/estrichone/update.sh --pruefen                         # nur prüfen
  ```

  Eingerichtet wird ein systemd-Timer (`estrichone-update.timer`, Protokoll `journalctl -u estrichone-update`) bzw.
  ohne systemd `/etc/cron.d/estrichone-update`. Jeder Lauf sichert vorher (wie `update.sh`), wartet auf gesunde
  Dienste und prüft die gemeldete Version; das Ergebnis steht im Web-Hinweis („Automatisches Update fehlgeschlagen …“).

### Nginx Proxy Manager (Betriebsart proxy) – empfohlene Einstellungen

| Reiter | Einstellung |
|---|---|
| Details | Scheme `http`, Forward Hostname/IP = Server (bzw. Containername im selben Docker-Netz), Port `8181`; **Block Common Exploits** an; **Websockets Support** an (schadet nicht); Cache Assets aus |
| SSL | Let's-Encrypt-Zertifikat, **Force SSL**, **HTTP/2 Support**, **HSTS Enabled** (HSTS Subdomains nur, wenn alle Subdomains HTTPS haben) |
| Access List | optional: Verwaltung nur aus dem Firmennetz ist in EstrichONE nicht pfadgetrennt – eine Access List gilt für alle Clients (auch Apps unterwegs), daher in der Regel **keine** |
| Advanced | siehe unten |

```nginx
client_max_body_size 600m;          # Videos (Web lädt in 4-MB-Teilen, Apps ganze Dateien)
proxy_request_buffering off;        # Uploads direkt durchreichen
proxy_buffering off;                # Chat-Echtzeit (Server-Sent Events) sofort ausliefern
proxy_read_timeout 1h;              # SSE-Verbindung offen halten
proxy_send_timeout 1h;
```

NPM setzt `X-Forwarded-For`/`-Proto` selbst. Caddy übernimmt diese nur von privaten Adressen
(`ESTRICHONE_TRUSTED_PROXIES`, Standard `private_ranges`); läuft NPM auf einem Rechner mit öffentlicher Adresse,
dort dessen Adresse eintragen, sonst zählen Rate-Limits und Sperren alle Nutzer als „NPM“.

### Checkliste „Öffentlich betreiben“

1. **Nur 443 nach außen.** Am Router/Provider-Firewall nur TCP 443 (und 80 für Let's Encrypt, falls NPM es braucht)
   auf den Proxy weiterleiten. Port 8181 (EstrichONE hinter NPM) **nie** ins Internet freigeben.
2. **Firewall auf dem Server** (Ubuntu/Debian):

   ```bash
   sudo ufw default deny incoming && sudo ufw default allow outgoing
   sudo ufw allow 22/tcp           # besser: nur aus dem Firmennetz, z. B. sudo ufw allow from 192.168.1.0/24 to any port 22
   sudo ufw allow 80/tcp && sudo ufw allow 443/tcp
   sudo ufw enable
   ```

   **Achtung:** Von Docker veröffentlichte Ports (z. B. 8181) umgehen ufw. Deshalb zusätzlich EstrichONE nur an die
   Adresse binden, über die NPM kommt (Installer erneut mit `-e ESTRICHONE_BIND_ADDRESS=192.168.1.10` bzw. in der
   `.env` `ESTRICHONE_BIND=192.168.1.10:` und `sudo /opt/estrichone/update.sh`) – oder in der Kette `DOCKER-USER`
   sperren: `sudo iptables -I DOCKER-USER -p tcp --dport 8181 ! -s 192.168.1.0/24 -j DROP` (dauerhaft mit
   `iptables-persistent`). Läuft NPM auf demselben Rechner im Docker-Netz, braucht 8181 gar keine Freigabe nach außen.
3. **SSH härten:** nur Schlüssel (`PasswordAuthentication no`, `PermitRootLogin no` in `/etc/ssh/sshd_config`,
   dann `sudo systemctl reload ssh`), optional **fail2ban** (`sudo apt install fail2ban`, Jail `sshd` ist Standard).
4. **Betriebssystem-Updates automatisch:** `sudo apt install unattended-upgrades && sudo dpkg-reconfigure -plow unattended-upgrades`;
   Docker aus dem offiziellen Repository aktuell halten.
5. **EstrichONE-Sicherheitsupdates automatisch:** `sudo /opt/estrichone/update.sh --automatik sicherheit`.
6. **NPM-Einstellungen** wie oben (Force SSL, HSTS, Block Common Exploits, Advanced-Block); NPM selbst aktuell halten
   und dessen Verwaltungsoberfläche (Port 81) **nicht** ins Internet freigeben.
7. **Zwei-Faktor-Anmeldung** für alle Administratoren einschalten und für Administratoren erzwingen (Abschnitt App);
   keine geteilten Konten.
8. **Backups:** ein Backup-Ziel auf anderer Hardware (am besten außer Haus) koppeln, Notfallblatt ausdrucken und den
   Wiederherstellungsschlüssel außerhalb des Servers aufbewahren; Wiederherstellung einmal testen (docs/backup.md).
9. **Zusatzserver** (Archiv, Backup-Ziel) nur im LAN/VPN erreichbar machen – Port 8443/8444 nie ins Internet.
10. **Hosting-Konsole:** nur über HTTPS, 2FA, nach Möglichkeit `HOSTING_ALLOWED_NETWORKS` setzen.
11. **Prüfen:** von außen `nmap -Pn <öffentliche-IP>` → nur 80/443 offen; `curl -I https://<domain>/` zeigt HSTS und
    keinen `Server`-Header.
