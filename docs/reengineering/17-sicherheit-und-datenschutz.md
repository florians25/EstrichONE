# 17 – Sicherheit und Datenschutz

Grundlage: OWASP ASVS 4.0 Level 2 (Web/API) und OWASP MASVS (mobil) als Prüfliste, DSGVO Art. 25
(Datenschutz durch Technikgestaltung) und Art. 32 (Sicherheit der Verarbeitung).

## 1. Sofortmaßnahmen am Altbestand (unabhängig von EstrichONE)

Estrich Web ist produktiv. Diese Punkte betreffen den **laufenden Betrieb** und sollten nicht bis zur
Ablösung warten. Sie sind **nicht** Teil der Implementierungsfreigabe für EstrichONE. Ich führe sie nur
nach deiner ausdrücklichen Anweisung durch.

| # | Maßnahme | Bezug |
|---|---|---|
| S-1 | **Datenbank-Passwort und JWT-Secret rotieren.** Beide lagen im weitergegebenen ZIP (`api/config/local.php`). Alle Benutzer müssen sich danach neu anmelden. | 01, 🔒-1 |
| S-2 | Eigenen `cron_key` setzen (nicht das JWT-Secret); Cron-URL im Hosting aktualisieren | 🔒-6 |
| S-3 | Microsoft-Endpunkte serverseitig abschalten (`microsoft_sso.enabled=false` reicht; prüfen, dass `sso_settings.is_enabled=0`) | 🔒-2 |
| S-4 | Falls bei Benutzern `two_factor_enabled=1` gesetzt ist: Diese Benutzer können sich mit beliebigem Code anmelden. Wert zurücksetzen oder den Code tatsächlich prüfen. | 🔒-3 |
| S-5 | Fehlerdetails in API-Antworten wieder entfernen (nur im Server-Log) | 🔒-8 |
| S-6 | ZIP-Dateien mit `local.php` nicht mehr weitergeben; vorhandene Kopien löschen | – |

## 2. Maßnahmen in EstrichONE

| Bereich | Maßnahme |
|---|---|
| Passwörter | Argon2id (m=19 MiB, t=2, p=1 als Untergrenze; nach Serverleistung kalibriert). Mindestlänge 10, Prüfung gegen eine Liste häufiger Passwörter (offline, kein externer Dienst); keine Zwangswechsel ohne Anlass (NIST SP 800-63B). |
| Brute-Force | je Konto und je IP gedrosselt (ASP.NET Core Rate Limiting) + progressive Verzögerung; Geräte-PIN lokal (11) |
| Sitzungen | Access-Token 10 min (JWT, ES256, Schlüssel rotierbar mit `kid`), Refresh-Token rotierend mit Wiederverwendungserkennung (Wiederverwendung → ganze Sitzungsfamilie widerrufen), Gerätebindung (11). Web: HttpOnly-, Secure-, SameSite=Strict-Cookie + CSRF-Token für zustandsändernde Cookie-Requests. **Kein Token in `localStorage`, kein Token in URLs.** |
| Widerruf | Jede Anfrage prüft Sitzungs- und Gerätestatus (zwischengespeichert ≤ 30 s) → Sperre wirkt praktisch sofort |
| Erster Administrator | Beim ersten Start schreibt die API einen **Einmal-Einrichtungscode** ins Log bzw. auf die Konsole (`docker compose logs api`). Wer Zugriff auf den Server hat, richtet ein. Keine Standardpasswörter und kein offener `/install`-Pfad. |
| Passwort zurücksetzen | per E-Mail-Link (nur mit SMTP; Token 30 min, einmalig, Hash in DB) oder durch Administrator (Einmalpasswort + Zwangswechsel) |
| Zugriffskontrolle | serverseitig, deny-by-default, Objektprüfung über `BauakteAccess` (03, Abschnitt 6); automatisierte Tests gegen IDOR für jeden Endpunkt (19) |
| Injection | EF Core (parametrisiert); kein String-SQL außer in geprüften Ausnahmen mit Parametern; Ausgabe-Encoding durch Angular; strikte CSP |
| Dateien | Typprüfung über Inhalt (Magic Bytes), Größenlimits, Bilder werden neu kodiert (entfernt eingebettete Inhalte), zufällige Dateinamen, Ablage außerhalb des Webroots, Auslieferung nur über die API mit `Content-Disposition` und `X-Content-Type-Options: nosniff` |
| Transport | TLS 1.2+ (Caddy), HSTS; Apps: ATS (iOS) bzw. `cleartextTrafficPermitted=false` (Android) |
| CORS | nur konfigurierte Ursprünge; Apps brauchen kein CORS |
| Header | CSP, `frame-ancestors 'none'`, Referrer-Policy, Permissions-Policy |
| Logs | strukturiert, **ohne** Passwörter, Tokens, Inhalte; Benutzer nur als ID; IPs nach 14 Tagen gekürzt |
| Audit | `audit_events` (nur anfügen; DB-Rolle der API hat kein `UPDATE`/`DELETE` darauf): Anmeldung, Fehlversuche, Rechteänderung, Gerät freigeben/sperren/löschen, Bauakte löschen/stornieren, Export, Backup/Restore, SSO-Konfiguration |
| Secrets | `.env` mit restriktiven Rechten, von `setup.sh` erzeugt; Data Protection für gespeicherte Drittzugänge (SMTP, Entra) |
| Abhängigkeiten | Dependabot/Renovate, `dotnet list package --vulnerable`, `npm audit`, Container-Scan (Trivy) in CI |
| Updates | signierte Container-Images (cosign) **[1.1]**; Update-Skript erzwingt vorher ein Backup; Datenbank-Migrationen sind vorwärtsgerichtet getestet |
| Mobile | Daten verschlüsselt je Benutzer (11), Keychain/Keystore, Screenshot-Schutz für Poolgeräte, keine Logs mit Inhalten, Root/Jailbreak-Erkennung **nicht** (unzuverlässig, erzeugt falsche Sicherheit) |

## 3. Gemeinschaftsgeräte – Risiken

| Risiko | Maßnahme (Details 11/12) |
|---|---|
| Daten des Vorgängers sichtbar | ein verschlüsselter Container je Benutzer, kein gemeinsamer Cache |
| Sitzung des Vorgängers wird übernommen | Sperre nach 5 min, Benutzerauswahl statt Fortsetzung |
| Microsoft-Sitzung des Vorgängers | `prompt=select_account`/`login`, ephemere Browsersitzung, kein MSAL-Cache |
| Biometrie öffnet fremde Daten | Biometrie auf Poolgeräten aus |
| PIN wird beobachtet | PIN schützt nur den eigenen Container; Online-Anmeldung bleibt nötig nach Höchstdauer |
| Gerät geht verloren | Sperre/Löschbefehl im IT Center, Offline-Höchstdauer 3 Tage |

## 4. Personenbezogene Daten – Verzeichnis (Entwurf)

Grundlage für das Verarbeitungsverzeichnis des **Betreibers** (jeder Betrieb ist selbst Verantwortlicher; der
Herausgeber von EstrichONE verarbeitet keine Daten).

| Bereich | Daten | Zweck | Speicherdauer (Vorschlag) | Zugriff |
|---|---|---|---|---|
| Benutzer | Name, E-Mail, Telefon, Rolle, Passwort-Hash, letzte Anmeldung | Zugang, Zuständigkeiten | bis Löschung des Kontos + 30 Tage | Admin; Name für Kollegen sichtbar, **Kontaktdaten nur mit Recht** (heute für alle lesbar, 🔒-4) |
| IT Center | Gerätename, Modell, OS-/App-Version, Gerätetyp, zugeordneter Benutzer, letzte Verbindung, Sync-Zähler, Gerätesperre ja/nein | Gerätesicherheit, Support | bis Entfernung des Geräts + 90 Tage | `devices.read` |
| Sicherheitsereignisse | Zeit, Benutzer-ID, IP (gekürzt nach 14 Tagen), Ergebnis | Missbrauchserkennung | 180 Tage | Admin |
| Audit | wer, was, wann, Objekt-ID | Nachvollziehbarkeit | 2 Jahre (konfigurierbar) | Admin |
| Helpdesk | Ticketinhalt, Anhänge, Diagnosedaten | Support | 1 Jahr nach Schließen | `helpdesk.agent`, Melder |
| Bauakten | Kundenname, Adressen, Ansprechpartner, Fotos (ggf. mit Personen), GPS der Aufnahme | Auftragsdokumentation | gesetzliche/vertragliche Fristen (z. B. Gewährleistung 5 Jahre, steuerlich bis 10 Jahre) – vom Betrieb festzulegen | nach Bauaktenrecht |
| Abwesenheiten | Zeitraum, Art (Urlaub/Krank) | Planung | laufendes + 1 Jahr | Planung; **Art nur für berechtigte Rollen** |
| Standort | nur auf Knopfdruck, nur Baustellenadresse/Fotokoordinaten | Dokumentation | wie Bauakte | – |

Es gibt **keine** Leistungs- oder Verhaltenskontrolle: kein Bewegungsprofil und keine Auswertung von
Anmeldezeiten pro Mitarbeiter. In Betrieben mit Betriebsrat ist die Einführung von EstrichONE trotzdem
mitbestimmungspflichtig (§ 87 Abs. 1 Nr. 6 BetrVG). Die Tabelle dient als Grundlage dafür.

## 5. Offene Punkte

- Eine juristische Prüfung (AV-Vertrag nur bei Fremdhosting, TOMs, Löschkonzept) ist Sache des Betreibers.
  EstrichONE liefert Vorlagen, keine Rechtsberatung.
- Penetrationstest vor Version 1.0 **[Empfehlung]**, mindestens mit OWASP ZAP automatisiert in CI.
