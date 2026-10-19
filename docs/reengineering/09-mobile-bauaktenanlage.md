# 09 – Mobile Bauakten-Schnellanlage

**Priorität: höchste.** **Status heute:** Es gibt einen 5-Schritt-Assistenten im Browser
(`workflow-wizard`, Schritte „Kunde auswählen → Bauakte erfassen → Adresse & Ausführung → Räume & Aufbau →
Speichern“). Pflicht sind dort Kunde, Projektname und Baustellenname. Offline wird der Vorgang nur „vorgemerkt“
und geht serverseitig verloren (01, ❌-1).

**Ziel [Verbindlich]:** Eine einfache Bauakte ist bei vorhandenen Stammdaten in **15–30 Sekunden** angelegt,
auch offline. Danach sind sofort Fotos, Dokumente, Notizen und Aufgaben möglich.

## 1. Pflichtfelder auf das Minimum

| Feld | Pflicht? | Begründung |
|---|---|---|
| **Bauvorhaben** (Bezeichnung) | **ja** | Wiedererkennung in Listen; einziges technisch nötiges Feld |
| Baustellenadresse | nein (dringend empfohlen, Hinweis bleibt in der Akte bis ergänzt) | vor Ort oft nicht genau bekannt (Neubaugebiet) |
| Kunde | nein | wird im Büro nachgetragen (heute Pflicht → entfällt) |
| Beschreibung | nein | |
| Vorlage | vorausgewählt („Standard“) | setzt Status und Prüfvorgaben |
| Bauaktennummer | **nie vom Benutzer** | vergibt der Server bei der Synchronisation (`JJJJ-NNN`); offline wird „Neu · noch ohne Nummer“ angezeigt |

**Server-Regel:** Eine Bauakte ohne Kunde ist gültig. Hinweiszeilen („Kunde fehlt“, „Adresse fehlt“) erscheinen
im Büro und in der Akte, bis die Angaben ergänzt sind. Das bestehende Hinweiskonzept aus Runde 66 wird dafür
verwendet.

## 2. Ablauf (ein Bildschirm)

```
[Heute]  ── Tippen auf „+ Neue Bauakte“ (große Hauptaktion unten rechts)
   │
   ▼
┌─ Neue Bauakte ────────────────────────────── [Abbrechen] ┐
│ Bauvorhaben *                                            │
│ [ EFH Müller, Neubau                              ]      │  ← Fokus + Tastatur sofort offen
│   Vorschläge: „Musterstr. 12, Landshut“ (Standort)       │
│                                                          │
│ Baustelle                                                │
│ [ 📍 Aktuellen Standort übernehmen ]                     │  ← 1 Tipp: Adresse aus GPS
│ [ Straße, Nr., PLZ, Ort                       ]          │
│                                                          │
│ Kunde (optional)                                         │
│ [ 🔍 Kunde suchen … ]   Zuletzt: Bau GmbH · Fa. Huber    │  ← Chips der letzten 3 Kunden
│                                                          │
│ Art: (•) Standard  ( ) Neubau  ( ) Sanierung  ( ) Gewerbe│
│ ▸ Beschreibung hinzufügen                                │  ← eingeklappt
│                                                          │
│ ⚠ Ähnliche Bauakte: „EFH Müller“, Musterstr. 12 – Öffnen │  ← nur bei Treffer, blockiert nicht
├──────────────────────────────────────────────────────────┤
│ [ Anlegen ]          [ 📷 Anlegen und fotografieren ]    │  ← Primär: rechts, Daumenbereich
└──────────────────────────────────────────────────────────┘
   │ „Anlegen und fotografieren“
   ▼
Kamera (Serienaufnahme) ── „Fertig (5)“ ──► Bauakte „EFH Müller, Neubau“
                                            Status: ● Auf diesem Gerät gespeichert · wird übertragen
                                            [Foto] [Notiz] [Aufgabe] [Dokument] [Kollegen]
```

**Zeitbudget (Ziel, im Test zu messen):**

| Schritt | Ziel |
|---|---|
| Hauptaktion tippen | 1 s |
| Bauvorhaben eintippen | 6–10 s |
| Standort übernehmen | 2–4 s (GPS-Fix + Adressauflösung; offline nur Koordinaten) |
| Kunde aus „Zuletzt“ antippen (optional) | 2 s |
| „Anlegen und fotografieren“ | 1 s, Kamera ist sofort bereit |
| **Summe** | **12–18 s, mit Kundensuche ≤ 30 s** |

## 3. Intelligente Hilfen

| Hilfe | Umsetzung | Offline | Pflichtdienst? |
|---|---|---|---|
| Bezeichnungsvorschlag aus Adresse | „Straße Nr., Ort“ als Vorschlag, sobald Adresse vorhanden | ja (bei manueller Adresse) | nein |
| Standort → Adresse | iOS `CLGeocoder`, Android `Geocoder` (Systemdienste, kostenlos, ohne Konto); optional serverseitiger Anbieter (z. B. eigene Nominatim-Instanz) | **nein** → speichert Koordinaten, Adresse wird nach Sync serverseitig vorgeschlagen | nein, abschaltbar |
| Kundensuche | lokale Volltextsuche über synchronisierte Kunden; „Zuletzt verwendet“ je Benutzer | ja | nein |
| Vorhandene Baustellen | Suche in lokalen Bauakten: Treffer „Diese Adresse gehört zu 2025-114 · EFH Müller – Öffnen statt neu anlegen?“ | ja | nein |
| **Duplikaterkennung** | (1) lokal: gleiche Adresse (normalisiert) oder Bezeichnung mit hoher Ähnlichkeit **und** Standort < 100 m; (2) serverseitig beim Sync gegen **alle** Bauakten, auch solche, die das Gerät nicht kennt. Ein Treffer ist **nie blockierend**: Das Büro erhält einen Hinweis „Mögliche Doppelanlage“ mit Zusammenführen-Funktion **[1.1]** | teilweise | nein |
| Vorlagen | 3–5 Vorlagen (Standard, Neubau, Sanierung, Gewerbe) im Web pflegbar; setzen Status, Standard-Aufgaben (z. B. „Erstbegehung durchführen“), Prüfvorgaben | ja | nein |
| Zuletzt verwendet | „Heute“ zeigt die 5 zuletzt geöffneten und alle heute eingeplanten Bauakten | ja | nein |
| Sinnvolle Vorgaben | Ersteller = Zuständiger, Kolonne des heutigen Einsatzes als Mitglied, Datum = heute | ja | nein |

## 4. Sofort nach der Anlage

Die Bauakte ist **lokal vollständig** und sofort bearbeitbar:

| Aktion | Offline | Verhalten |
|---|---|---|
| Fotos/Video aufnehmen | ja | Datei liegt verschlüsselt in der Benutzer-Ablage, Eintrag in der Upload-Warteschlange |
| Dokument scannen/hochladen | ja | wie Fotos |
| Notiz | ja | Anfüge-Operation, konfliktfrei |
| Aufgabe/Mangel | ja | |
| Baustelleninformationen ergänzen | ja | Feld-Patches mit Basisversion |
| Kollegen zuordnen | ja (Liste aus dem lokalen Benutzerverzeichnis, nur Name) | Recht `bauakten.assign` wird lokal vorgeprüft und serverseitig endgültig geprüft; bei Ablehnung Hinweis in der Synchronisationsübersicht |

## 5. Technischer Ablauf (offline-sicher)

1. Der Client erzeugt `bauakteId` (UUIDv7) und `opId`.
2. **Eine** lokale Transaktion schreibt die Bauakte (Zustand `pending`) und die Operation `bauakte.create`
   in die Outbox.
3. Fotos werden **zuerst als Datei dauerhaft gespeichert** und erhalten eine `fileId`. Erst danach wird
   „Foto gespeichert“ angezeigt. Die Operationen `file.attach` referenzieren die `bauakteId`.
4. Die SyncEngine sendet `bauakte.create` vor abhängigen Operationen (Reihenfolge nach Abhängigkeit).
   Der Server antwortet mit der vergebenen Nummer. Ein wiederholter Versand mit derselben `opId` liefert
   dieselbe Antwort (`duplicate`) und erzeugt keine zweite Akte.
5. Dateien laden parallel zu Metadaten hoch (`PUT /files/{fileId}`, SHA-256). Die Zuordnung wird wirksam, sobald
   beides angekommen ist.
6. **Kein Datenverlust bei Abbruch:** Jeder Schritt ist wiederholbar. App-Neustart, Akkuende und Funkloch führen
   nur zu einer späteren Übertragung (Tests in 19).

## 6. Fünf häufigste mobile Baustellen-Workflows

Abgeleitet aus dem Altbestand (Bauakte-Reiter, Tagesdokumentation, Erstbegehung, Arbeitsplan) und den
Änderungsnotizen. **Mit dem Betrieb zu bestätigen.**

| # | Workflow | Optimierung |
|---|---|---|
| W-1 | **Einsatz heute ansehen und hinfahren** | „Heute“ zeigt den eigenen Einsatz oben (Bauakte, Uhrzeit, Kolonne, Adresse). Ein Tipp öffnet die Navigation in der Karten-App des Systems. Funktioniert offline aus dem letzten Sync. |
| W-2 | **Fotos zu einer bestehenden Bauakte aufnehmen** | Reiter „Aufnehmen“: Bauakte ist vorausgewählt (heutiger Einsatz bzw. zuletzt geöffnet) → Kamera → Serienaufnahme → „Fertig“. **3 Tipps.** |
| W-3 | **Neue Bauakte vor Ort anlegen** | siehe oben, ≤ 30 s |
| W-4 | **Bautagebuch / Tagesdokumentation** | Vorbelegt mit Datum, Kolonne aus dem Einsatz, Wetter (falls online). Felder: Personal (Chips aus der Kolonne), ausgeführte Leistung (Räume antippen), Besonderheiten (Diktat über die Systemtastatur), Fotos. |
| W-5 | **Erstbegehung / CM-Messung erfassen** | Geführte Maske mit Pflichtfotos (Zähler, Anmischung). Messwerte über große Zifferntasten, Ampel bei Grenzwerten, „Keine Erstbegehung notwendig“ mit Begründung. |

Weitere häufige Kurzaktionen: Notiz oder Mangel erfassen (Bauakten-Toolbar, 2 Tipps) und Problem melden (14).

## 7. Akzeptanzkriterien

- **AK-1:** Ein Testnutzer ohne Vorerfahrung legt nach einmaliger Vorführung eine Bauakte mit Adresse aus dem
  Standort und 3 Fotos in **≤ 30 s** an (Median über 5 Testpersonen, Messung in 19).
- **AK-2:** Im Flugmodus angelegte Bauakte und 20 Fotos erscheinen nach Wiederverbindung vollständig und
  genau einmal auf dem Server, auch wenn die App während des Uploads beendet wurde.
- **AK-3:** Doppelt gesendete Anlage (simulierter Timeout nach Serverbestätigung) erzeugt keine zweite Bauakte.
- **AK-4:** Alle Bedienelemente des Anlagebildschirms sind ≥ 48 pt groß und mit VoiceOver/TalkBack bedienbar.
- **AK-5:** Ohne Standortfreigabe und ohne Netz ist die Anlage ohne Fehlermeldung möglich.
