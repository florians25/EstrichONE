# 11 – Persönliche Geräte und Poolgeräte

**Status heute:** nicht vorhanden. Teilweise verwandt ist die PIN-Schnellanmeldung (`quick_logins`): eine PIN
je Gerät **und** Benutzer, mit einer vom Client selbst gelieferten `device_id`. Dazu kommt das Risiko des
benutzerunabhängigen Service-Worker-Caches (01, 🔒-10, 🔒-13).

## 1. Geräteidentität

| Element | Umsetzung |
|---|---|
| Geräteschlüssel | Bei der Einrichtung erzeugt die App ein **P-256-Schlüsselpaar** in der Secure Enclave (iOS) bzw. im Android Keystore (nicht exportierbar). Der öffentliche Schlüssel wird beim Server registriert. |
| `deviceId` | vom **Server** vergeben (UUID), nicht vom Client erfunden |
| Bindung | Refresh-Tokens sind an das Gerät gebunden. Jeder Refresh enthält eine Signatur des Geräteschlüssels über eine Server-Nonce (`POST /auth/refresh` mit `device_proof`). Ein gestohlener Refresh-Token ist ohne den Gerätespeicher wertlos. |
| Kein Fingerprinting | Es werden nur Daten erhoben, die die Plattform offen bereitstellt (13). Keine Hardware-Kennungen (IMEI, Seriennummer sind ohnehin gesperrt). |

## 2. Registrierung und Freigabe

**[Verbindlich]** Ein unbekanntes Gerät darf sich nicht selbst als vertrauenswürdig einstufen. Die Wahl
„Poolgerät“ vergibt keine Rechte.

| Weg | Ergebnis | Begründung |
|---|---|---|
| Einrichtung mit **QR-Code aus dem IT Center** | Gerät ist sofort **freigegeben** mit dem Typ, den der Administrator im Code festgelegt hat | Der Administrator hat das Gerät bewusst eingerichtet; der Code ist einmalig und kurzlebig |
| Manuelle Serveradresse, Typ **persönlich** | Richtlinie **[Vorschlag]**: „Persönliche Geräte automatisch freigeben“ (Standard **an**), **wenn** der Benutzer das Recht `devices.self_enroll` hat. Sonst wartet das Gerät auf Freigabe. Der Administrator erhält in jedem Fall eine Benachrichtigung. | Reibungsarm für den Normalfall, aber abschaltbar |
| Manuelle Serveradresse, Typ **Pool** | immer **wartet auf Freigabe** | Poolgeräte sind sicherheitskritischer |

Statusmodell `devices.status`: `pending` → `active` → (`locked` ⇄ `active`) → `retired`;
zusätzlich `wipe_requested` (13). Nur `active` erhält Zugriffstokens.

## 3. Persönliches Gerät

- Die Anmeldung bleibt bestehen: Refresh-Token 90 Tage gleitend, Access-Token 10 Minuten.
- App-Sperre nach Inaktivität laut Richtlinie (Standard 15 min). Entsperren mit Face ID/Touch ID bzw.
  Android-Biometrie (Klasse 3) oder dem Gerätecode. Biometrie gibt nur den lokal gespeicherten Schlüssel frei.
- Benachrichtigungen gehen an diesen Benutzer.
- Offline-Daten: Bauakten laut Synchronisationsumfang (Standard: eigene und heutige Einsätze).

## 4. Poolgerät

### 4.1 Bedienkonzept

```
┌──────────────────────────────────────┐
│  EstrichONE · Fahrzeug 3             │   ← Gerätename sichtbar
│                                      │
│  Wer arbeitet mit dem Gerät?         │
│                                      │
│  [ Max Huber       ]  zuletzt 07:12  │   ← Kacheln: zuletzt angemeldete Benutzer DIESES Geräts
│  [ Lukas Brandl    ]  gestern        │      (nur Name, keine Daten; Liste abschaltbar per Richtlinie)
│  [ Andere Person … ]                 │
└──────────────────────────────────────┘
        │ Kachel antippen
        ▼
  Geräte-PIN des Benutzers (6 Ziffern, große Tasten)   oder   Passwort / Microsoft
```

- **Aktueller Benutzer immer sichtbar:** Name und Initialen in der Kopfzeile jeder Seite (opak, nicht auf Glas).
- **Benutzer wechseln:** *Mehr › Benutzer wechseln* oder langes Drücken auf den Namen. Das sperrt den
  Container und zeigt die Auswahl.
- **Abmelden:** sperrt den Container und widerruft die Sitzung serverseitig, wenn eine Verbindung besteht.
- **Automatische Sperre:** nach Inaktivität laut Richtlinie (Standard **5 min**), beim Bildschirm-Aus sofort.
  Danach ist eine erneute Eingabe der PIN nötig. Biometrie auf Poolgeräten ist **aus** (die biometrischen
  Merkmale gehören zum Gerät, nicht zum Benutzer).
- **Keine unbeabsichtigte Übernahme:** Beim Entsperren wird nie automatisch der letzte Benutzer fortgesetzt;
  immer erst die Auswahl.

### 4.2 Geräte-PIN

- Wird bei der ersten Online-Anmeldung eines Benutzers **auf diesem Gerät** festgelegt (6 Ziffern, keine
  trivialen Folgen).
- Serverseitig wird nichts von der PIN gespeichert. Sie dient lokal als Schlüssel (siehe 4.3) und erlaubt eine
  Offline-Anmeldung.
- 5 Fehlversuche → Container für diesen Benutzer gesperrt. Danach ist eine Online-Anmeldung mit Passwort
  oder Microsoft nötig. Die Sperre zählt das Gerät; nach Neuinstallation sind die Daten ohnehin weg.

### 4.3 Datentrennung (Cache-Trennung, Verschlüsselung)

**Grundsatz: ein verschlüsselter Container je Benutzer, kein gemeinsamer Datenbestand.**

```
App-Sandbox
 ├─ device/            Gerätekonfiguration, Geräteschlüssel-Referenz, Serveradresse, Richtlinien
 │                     (keine personenbezogenen Fachdaten)
 └─ users/<userId>/    eigene SQLite-DB (SQLCipher) + eigene Dateiablage (AES-GCM je Datei)
      Datenschlüssel DEK (zufällig, 256 Bit)
      DEK doppelt umhüllt: (1) mit einem aus der PIN abgeleiteten Schlüssel (Argon2id, eigenes Salt),
      (2) zusätzlich mit einem nicht exportierbaren Geräteschlüssel (Keystore bzw. Secure Enclave via ECIES)
```

- Ohne PIN des Benutzers ist sein Container auch für die App selbst nicht lesbar. Ein Fehler in der
  App-Logik kann daher keine Daten eines anderen Benutzers anzeigen. Die Trennung ist kryptografisch und
  nicht nur logisch.
- Die Bindung an den Geräteschlüssel verhindert, dass ein kopierter Container auf einem anderen Gerät per
  Brute-Force der 6-stelligen PIN geöffnet wird.
- **Kein gemeinsamer Lese-Cache:** Auch wenn zwei Benutzer dieselbe Bauakte sehen, hat jeder seine eigene
  Kopie. Das kostet Speicher und Datenvolumen, schließt aber aus, dass Daten nach einem Rechteentzug
  sichtbar bleiben.
- **Datenumfang auf Poolgeräten:** Standardmäßig nur Bauakten mit heutigem bzw. morgigem Einsatz und die
  vom Benutzer zuletzt geöffneten. Der Umfang ist per Richtlinie einstellbar.
- Bilder werden nie in die Fotomediathek des Systems gespeichert, und Screenshots im App-Umschalter werden
  durch eine Abdeckung verborgen.

### 4.4 Ungesendete Daten beim Benutzerwechsel

- Vor dem Wechsel versucht die App, sofort zu synchronisieren (Anzeige: „3 Fotos werden noch übertragen“).
- Bleiben Daten übrig, verbleiben sie **verschlüsselt im Container des Benutzers**. Das Gerät überträgt
  sie im Hintergrund weiter, **ohne** dass der Container geöffnet wird:
  - Die Outbox-Einträge und Dateien sind zusätzlich mit einem **Upload-Schlüssel** verschlüsselt, der an
    das Gerät gebunden ist.
  - Bei der Anlage signiert der Server ein Upload-Ticket (Benutzer, Gerät, Zeitraum). Mit diesem Ticket
    darf das Gerät ausschließlich die bereits erfassten Operationen dieses Benutzers einliefern. Es kann
    damit nichts lesen.
  - **[Offen] E-18:** Diese Erweiterung ist aufwendiger. Die einfache Alternative für 1.0: Ungesendete Daten
    gehen erst beim nächsten Login desselben Benutzers raus. Das IT Center zeigt „Gerät X: 12 ungesendete
    Vorgänge von Max Huber“. **Vorschlag: einfache Variante in 1.0, Upload-Ticket in 1.1.**
- Container eines Benutzers, der sich 30 Tage (Richtlinie) nicht angemeldet hat und **keine** ungesendeten
  Daten hat, werden gelöscht.

### 4.5 Poolgeräte und Microsoft-SSO

Siehe 12, Abschnitt 6. Kurz: Microsoft-Anmeldung immer mit Kontoauswahl bzw. erneuter Eingabe, keine
geteilte Browser-Sitzung, keine dauerhaft gespeicherten Microsoft-Tokens.

## 5. Widerruf, Sperre, gesperrte Benutzer

| Ereignis | Verhalten der App beim nächsten Serverkontakt |
|---|---|
| Benutzer gesperrt | Antwort `USER_DISABLED` → Container gesperrt. Ungesendete Daten werden **nicht** mehr angenommen. Sie bleiben verschlüsselt erhalten, bis ein Administrator im IT Center „Daten von Gerät abholen“ auslöst oder das Gerät zurücksetzt (siehe 15, Abschnitt 4). |
| Gerät gesperrt (`locked`) | Alle Container gesperrt, Hinweis „Gerät gesperrt – wenden Sie sich an …“ |
| Gerät widerrufen (`retired`) / Löschbefehl | 13, Abschnitt 4 |
| Rechte entzogen (Bauakte nicht mehr sichtbar) | Pull-Antwort enthält „removed from scope“ → lokale Kopie wird gelöscht; ungesendete Änderungen an dieser Akte erscheinen als „abgelehnt“ (15) |

## 6. Akzeptanzkriterien

- **AK-1:** Benutzer B sieht nach dem Wechsel auf einem Poolgerät keine Bauakte, kein Foto, keine
  Benachrichtigung und keinen Suchverlauf von Benutzer A. Das gilt auch im Flugmodus und auch dann, wenn
  B dieselbe Bauakte sehen darf: B sieht nur seine eigene Kopie.
- **AK-2:** Nach Ablauf der Sperrzeit öffnet die App die Benutzerauswahl, nicht die letzte Sitzung.
- **AK-3:** Die Dateien eines Benutzercontainers sind ohne PIN nicht lesbar (Test: Container-Datei auslesen →
  nur Chiffrat).
- **AK-4:** Ein Gerät mit Typ-Antrag „Pool“ erhält ohne Administratorfreigabe keinen Zugriffstoken.
