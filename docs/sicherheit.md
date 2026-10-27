# Sicherheit – Kurzüberblick (Stand Release 0.1.0)

| Bereich | Umsetzung |
|---|---|
| Passwörter | **Argon2id** (m = 19 MiB, t = 2, p = 1 – OWASP-Empfehlung), Salt 16 Byte, PHC-Format; ältere/schwächere Hashes werden bei der Anmeldung neu berechnet. Passwortregeln in `PasswordPolicy`. |
| Anmeldung | Sperre nach 5 Fehlversuchen je Konto und 20 je IP-Adresse; zusätzlich Rate-Limit 20 Anfragen/Minute je IP auf `/auth` (einstellbar). Zeitkonstanter Vergleich, Dummy-Hash bei unbekannten Benutzern. |
| Tokens | Keine JWTs: zufällige 256-Bit-Tokens (`eo1_…`/`eor1_…`), in der Datenbank nur als **SHA-256**; Sitzungen serverseitig widerrufbar (Abmelden, Sperren, Gerät löschen). Im Browser nur in `HttpOnly`-, `Secure`-, `SameSite=Strict`-Cookies, dazu XSRF-Token. |
| Gespeicherte Geheimnisse | SMTP-Passwort, Zugänge zu Archivserver und Backup-Zielen, **APNs-Schlüssel (.p8)** und **VAPID-Schlüssel**: ASP.NET Data Protection mit **AES-256-GCM** (neue Schlüssel; ältere AES-256-CBC+HMAC-SHA256-Schlüssel bleiben lesbar und werden nach 90 Tagen turnusgemäß ersetzt). Schlüsselring in `data/files/.keys` (Verzeichnis 0700, Teil der verschlüsselten restic-Sicherung). Geheimnisse werden nie zurückgegeben oder protokolliert. |
| Transport | Caddy: HTTPS (Let's Encrypt bzw. eigenes Zertifikat), HTTP → HTTPS, **HSTS** (1 Jahr, inkl. Subdomains), strikte **CSP** (`script-src 'self'`, `frame-ancestors 'none'`, `object-src 'none'`), `X-Content-Type-Options`, `Referrer-Policy`, `X-Frame-Options: DENY`, `Permissions-Policy`. API-Antworten: `default-src 'none'`, `Cross-Origin-Resource-Policy: same-origin`. |
| Pushnachrichten | Inhalt nur Titel, kurzer Hinweis (≤ 180 Zeichen), Link, IDs – keine Kommentare, Ticket-Texte, Preise, Personaldaten; optional nur „Neue Benachrichtigung“. Web Push Ende-zu-Ende verschlüsselt (RFC 8291), VAPID-Anmeldung (ES256); APNs mit Token-Anmeldung (ES256). Web-Push-Endpunkte nur bei bekannten Push-Diensten per HTTPS (kein SSRF), Links nur relative Pfade. Versand höchstens 20 Nachrichten je 10 Minuten und Benutzer. |
| Rechte | Jeder Endpunkt erfordert Anmeldung (Fallback-Richtlinie) und bei Apps ein freigegebenes Gerät; fachliche Rechte je Endpunkt (`RequirePermission`), Modulgrenzen per Architekturtest. Audit-Log für sicherheitsrelevante Aktionen. |
| Dateien | Typprüfung am Inhalt (nicht an der Endung), Prüfsumme beim Upload, Benutzernamen nie im Dateisystem, Auslieferung mit passender CSP. |

## Geprüft und behoben in Runde 7
- Data Protection auf **AES-256-GCM** umgestellt; Schlüsselverzeichnis nur für den Dienstbenutzer lesbar.
- Neue Geheimnisse (APNs, VAPID) nur verschlüsselt gespeichert.
- Rate-Limits für Nachrichtenversand und Geräte-Registrierung.
- Push-Payload ohne sensible Inhalte, Web-Push-Endpunkte auf Push-Dienste beschränkt.

## Bekannte Grenzen
- Der Schlüsselring liegt unverschlüsselt (aber 0700) im Datenverzeichnis; wer Root auf dem Server hat, kann
  Geheimnisse entschlüsseln. Für höhere Anforderungen ein Zertifikat zum Schutz des Schlüsselrings ergänzen.
- Zertifikats-Pinning in den Apps wird bewusst nicht erzwungen (selbst gehostete Server, eigene Zertifikate).
