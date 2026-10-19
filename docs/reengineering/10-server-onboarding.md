# 10 – Server-Onboarding (Ersteinrichtung der Apps)

**Status heute:** nicht vorhanden. Estrich Web läuft im Browser unter einer festen Adresse.
**Gilt für:** iOS und Android vollständig. Windows nutzt Schritt 1, 2 und 4.

## 1. Ablauf

```
1 Willkommen ─► 2 Server ─► 3 Gerätetyp ─► 4 Anmeldung ─► 5 Einrichtung ─► Heute
                   │            │
                   │            └─ bei QR-Einrichtungscode bereits festgelegt (übersprungen)
                   └─ QR scannen | Adresse eingeben
```

### Schritt 1 – Willkommen

„Willkommen bei EstrichONE“
„Diese App arbeitet mit dem EstrichONE-Server Ihres Betriebs. Die Adresse oder einen Einrichtungscode
erhalten Sie von Ihrem Administrator.“
Schaltflächen: **[QR-Code scannen]** (primär) · [Adresse eingeben]

### Schritt 2 – Serververbindung

**A. QR-Code (empfohlen).** Der Administrator erzeugt im Web unter *IT Center › Geräte › Gerät einrichten*
einen Code. Dessen Inhalt ist kein Geheimnis an sich, aber kurzlebig:

```
estrichone://enroll?v=1
  &server=https://estrichone.meinefirma.de
  &code=K7QX-M2PA            (Einmal-Einrichtungscode, 15 min gültig, serverseitig nur als Hash)
  &type=pool                 (optional: vom Admin festgelegter Gerätetyp)
  &spki=sha256/AbC…          (optional: Hash des Server-Zertifikatsschlüssels, nur bei interner CA)
```

**B. Manuelle Eingabe.** Feld mit Tastatur-Typ URL. `https://` wird ergänzt, wenn es fehlt. Die Eingabe von
`http://` wird abgelehnt (Ausnahme: Entwicklereinstellungen, siehe 4).

**C. Lokale Erkennung (Discovery).** **[Vorschlag: nicht in 1.0]** mDNS/Bonjour würde im Firmen-WLAN den
Server anzeigen. Ein so gefundener Server darf aber **nicht automatisch** übernommen werden: Der Benutzer
müsste ihn bestätigen, und die TLS-Prüfung bleibt Pflicht. Für den Mehrwert gegenüber dem QR-Code ist das zu
aufwendig, deshalb vorgemerkt.

**Verbindungsprüfung** – `GET /api/v1/server-info` (öffentlich, ohne Anmeldung):

| Prüfung | Fehlertext (Beispiel) |
|---|---|
| DNS/Erreichbarkeit (Timeout 10 s) | „Server nicht erreichbar. Prüfen Sie die Adresse und ob Sie mit dem Internet bzw. Firmen-WLAN verbunden sind.“ |
| HTTPS erzwungen | „Der Server verwendet keine verschlüsselte Verbindung. Aus Sicherheitsgründen ist das nicht erlaubt.“ |
| TLS-Zertifikat gültig (Systemprüfung; bei `spki` im QR zusätzlich Schlüssel-Abgleich) | „Das Sicherheitszertifikat des Servers ist ungültig.“ – **kein „Trotzdem fortfahren“** |
| Antwort ist ein EstrichONE-Server (`product: "EstrichONE"`, Schema-Version) | „Unter dieser Adresse läuft kein EstrichONE-Server.“ |
| API-Kompatibilität: `apiVersions` enthält die App-Version; `minClientVersion` ≤ App-Version | „Diese App-Version ist zu alt für Ihren Server. Bitte aktualisieren.“ bzw. „Der Server ist zu alt für diese App. Bitte Administrator informieren.“ |
| Anmeldemethoden (`auth: ["local","entra"]`) | steuert Schritt 4 |

Anzeige nach Erfolg: Servername (z. B. „Estrichbau Müller GmbH“), Adresse und Serverversion, Schaltfläche
„Weiter“. **Der Benutzer bestätigt den Server sichtbar.** Ein unbekannter Server wird nie stillschweigend
übernommen.

### Schritt 3 – Gerätetyp

```
Wie wird dieses Gerät genutzt?

 ( ) Persönliches Gerät
     Nur ich nutze dieses Gerät. Ich bleibe angemeldet und kann mit Face ID entsperren.

 ( ) Gemeinsames Gerät (Poolgerät)
     Mehrere Mitarbeiter nutzen das Gerät, z. B. im Fahrzeug oder auf der Baustelle.
     Jeder meldet sich an und ab. Daten anderer sind nicht sichtbar.
```

Die Auswahl ist ein **Antrag**. Was daraus folgt, regelt 11, Abschnitt 2.

### Schritt 4 – Anmeldung

- Angezeigt werden nur die Methoden aus `server-info`: **Benutzername/E-Mail + Passwort** und/oder
  **„Mit Microsoft anmelden“** (12).
- Nach erfolgreicher Anmeldung sendet die App die **Geräteregistrierung** (11, Abschnitt 1) mit Gerätetyp,
  Gerätename („iPhone von Max“ bzw. vom Admin vorgegeben), Plattform, OS-Version und App-Version.

### Schritt 5 – Einrichtung abschließen

Fortschrittsliste, jeder Punkt mit Status:
1. Berechtigungen laden ✓
2. Gerät beim Server registrieren ✓ – oder **„Wartet auf Freigabe durch den Administrator“** (siehe unten)
3. Geräterichtlinien übernehmen (Sperrzeit, Offline erlaubt, …) ✓
4. Bauakten für dieses Gerät festlegen: „Meine Bauakten und heutige Einsätze“ (Standard) oder
   „Alle, die ich sehen darf“ (nur wenn das Recht und die Richtlinie es erlauben)
5. Benachrichtigungen: Systemabfrage mit vorheriger Erklärung
6. Kamera/Standort: **nicht** hier, sondern beim ersten Gebrauch (Begründung ist dann verständlicher)
7. Offline-Speicher anlegen, erste Daten laden (mit Fortschritt; kann im Hintergrund fortgesetzt werden)

**Wartet auf Freigabe:** Die App zeigt einen klaren Zustand („Ihr Administrator wurde benachrichtigt. Sie
können die App nutzen, sobald das Gerät freigegeben ist.“), prüft im Hintergrund und erlaubt das Melden
eines Problems.

## 2. Spätere Änderungen

| Aktion | Wer | Absicherung |
|---|---|---|
| Server wechseln | Benutzer mit Recht `devices.reconfigure` **oder** nach Entsperrung mit Administrator-Code | (1) Sync-Prüfung: Gibt es ungesendete Operationen oder Dateien? → Warnung mit Anzahl und „Jetzt übertragen“. (2) Wechsel nur, wenn alles übertragen ist **oder** der Benutzer ausdrücklich „Nicht übertragene Daten exportieren und löschen“ wählt. Der Export ist eine verschlüsselte ZIP-Datei, die über die Teilen-Funktion abgelegt wird. (3) Danach vollständiges Zurücksetzen: lokale Datenbanken, Dateien, Schlüssel, Tokens. |
| Gerätetyp ändern | nur Administrator (serverseitig) | App übernimmt beim nächsten Kontakt; bei Wechsel persönlich → Pool werden lokale Daten nach erfolgreichem Sync neu aufgebaut |
| App zurücksetzen | jeder (unter *Mehr › Einstellungen*) | wie Serverwechsel, ohne neuen Server |

## 3. Desktop (Windows)

Schritte 1, 2 und 4 identisch, außerdem: Einrichtung über eine `.estrichone`-Datei bzw. einen Link
`estrichone://enroll?…`, den der Administrator per E-Mail verschickt. Der Gerätetyp ist immer „persönlich“.
Gemeinsam genutzte Windows-PCs werden über getrennte Windows-Benutzerkonten abgebildet (Windows bietet das
bereits sauber).

## 4. Entwicklereinstellungen

Nur in Debug-Builds bzw. nach siebenmaligem Tippen auf die Versionsnummer **und** mit einem
Entwicklercode, der in der Serverkonfiguration hinterlegt ist:
- `http://` für `localhost`, `10.0.2.2`, `*.local` zulassen,
- eigene CA vertrauen (nur Android: `network_security_config` für Debug-Builds),
- ausführliche Protokollierung.

Release-Builds enthalten die `http`-Ausnahme technisch nicht (iOS: keine ATS-Ausnahme im Release-Plist;
Android: `cleartextTrafficPermitted=false` im Release).

## 5. Akzeptanzkriterien

- **AK-1:** Einrichtung per QR-Code (persönliches Gerät, lokale Anmeldung) dauert ≤ 60 s bis zur Startseite,
  ohne Datendownload gerechnet.
- **AK-2:** Ein Server mit abgelaufenem, selbstsigniertem oder fremdem Zertifikat lässt sich in Release-Builds
  **nicht** einrichten.
- **AK-3:** Ein Serverwechsel mit 5 ungesendeten Fotos ist ohne ausdrückliche Bestätigung und Export nicht möglich.
- **AK-4:** Ein abgelaufener oder bereits benutzter Einrichtungscode wird mit klarer Meldung abgelehnt.
