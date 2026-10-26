# EstrichONE-Archivserver – 10 Jahre revisionssicher aufbewahren

Der Archivserver bewahrt Rechnungen, Aufträge, Abnahmen, Pläne und andere Dokumente aus EstrichONE **unveränderbar**
auf, bis die Aufbewahrungsfrist abgelaufen ist (GoBD-orientiert). Er läuft am besten auf **einem zweiten Rechner**
(alter PC, Mini-PC, NAS mit Docker, Raspberry Pi 4/5 mit 64-Bit-Linux), damit ein Defekt des EstrichONE-Servers das
Archiv nicht mitnimmt.

## In 3 Schritten

**1. Auf dem Archivrechner** (Linux mit Docker) einen Befehl ausführen:

```bash
sudo docker run --rm -v /var/run/docker.sock:/var/run/docker.sock ghcr.io/florians25/estrichone-installer:latest archiv
```

Am Ende steht da zum Beispiel:

```
   Adresse:         192.168.1.50:8443
   Kopplungscode:   K7QD-9MXA-WE4P        (24 Stunden gültig, einmal verwendbar)
 Fingerabdruck:     0C6419F5 72413A22 …
```

**2. In EstrichONE:** System → **Archiv** → „Archivserver koppeln“ → Adresse und Code eingeben → „Koppeln“.

**3. Fertig.** Danach zeigt EstrichONE den Fingerabdruck an; er muss mit dem auf dem Archivrechner übereinstimmen.
Ab jetzt archiviert EstrichONE automatisch nach den Regeln (siehe unten).

Port **8443** muss im Firmennetz vom EstrichONE-Server aus erreichbar sein. Eine Domain oder ein gekauftes Zertifikat
ist **nicht** nötig: Der Archivserver erzeugt sein eigenes Zertifikat, EstrichONE merkt sich beim Koppeln genau dieses
(„Pinning“) und spricht danach nur noch mit diesem Rechner.

Ohne Installer geht es auch direkt mit Docker (Code dann mit `sudo docker logs estrichone-archive`):

```bash
sudo docker run -d --name estrichone-archive --restart unless-stopped -p 8443:8443 \
  -v estrichone-archive:/data ghcr.io/florians25/estrichone-archive:latest
```

## Kein zweiter Rechner?

Dann kann der Archivserver im selben Docker-Stack mitlaufen (schlechter, weil derselbe Rechner): EstrichONE-Installer
mit `-e ESTRICHONE_ARCHIVE=lokal` ausführen bzw. in `/opt/estrichone/.env` `COMPOSE_PROFILES=archive` eintragen.
Koppeln mit Adresse **`archive`** und dem Code aus `sudo docker compose -p estrichone logs archive`.

## Was archiviert wird (Regeln)

Unter System → Archiv steht je Dokumentart: archivieren ja/nein, Frist in Jahren, Zeitpunkt.

| Dokumentart | Vorschlag | Zeitpunkt |
|---|---|---|
| Rechnungen | 10 Jahre | sofort nach dem Hochladen |
| Aufträge | 6 Jahre | sofort nach dem Hochladen |
| Angebote | 6 Jahre | nach der Freigabe |
| Abnahmen | 10 Jahre | sofort nach dem Hochladen |
| Lieferscheine, Protokolle, Pläne, Kundenschreiben | 6–10 Jahre | wenn die Bauakte abgeschlossen ist |
| Fotos, Videos, Sonstiges | aus | nur manuell |

Die Frist läuft ab dem Ende des Kalenderjahres (wie § 147 AO), z. B. Rechnung vom März 2026 mit 10 Jahren →
gesperrt bis 31.12.2036. **Die Vorschläge ersetzen keine steuerliche Beratung** – Fristen bitte mit der
Steuerberatung abstimmen. Mit `-e ESTRICHONE_MIN_RETENTION_YEARS=10` erzwingt der Archivserver eine Mindestfrist.

Manuell: im Dialog „Archivnachweis“ (Bauakte bzw. Dokument) → „Jetzt archivieren“ oder per API
`POST /api/v1/archive/bauakten/{id}` bzw. `/archive/files/{id}`. Fehlgeschlagene Übertragungen
wiederholt EstrichONE automatisch (1, 5, 15, 60 Minuten, danach alle 6 Stunden). Optional geht eine E-Mail an eine
Adresse aus „Bei Fehlern benachrichtigen“.

## Komplette Bauakten auslagern (Aktenarchiv)

Abgeschlossene Bauakten können **komplett** auf den Archivserver wandern und werden danach aus dem Produktivsystem
entfernt – das hält den Server klein. In Listen und Suche bleibt die Akte als Platzhalter („archiviert“) sichtbar.

- **Automatisch:** System → Archiv → „Akten auslagern nach … Monaten“ (abgeschlossene Bauakten). **Manuell:**
  System → **Aktenarchiv** → „Bauakte archivieren …“.
- Übertragen werden alle Dokumente, Fotos und Videos (je mit Prüfsumme) und der Datenstand der Akte als eigene,
  unveränderbare Fassung. Erst wenn alles bestätigt ist, werden die Inhalte lokal gelöscht.
- **Abrufen:** „Akte abrufen“ holt alles zurück (Fortschritt sichtbar). Danach ist die Akte **5 Tage** im System
  (nur Ansicht) und wird dann automatisch wieder entfernt. Auf dem Archivserver bleibt sie **immer**.
- **Ändern:** nur nach „Entsperren“ mit dem eigenen Passwort (Recht „Archivierte Bauakten entsperren“, wird
  protokolliert). Nach 8 Stunden bzw. „Jetzt neu archivieren“ geht die Akte als **neue Fassung** wieder ins Archiv.
- Hinweis: Die strukturierten Daten (Bauakte, Räume, Notizen …) bleiben schreibgeschützt in der Datenbank – sie sind
  klein; ausgelagert werden die Dateiinhalte. Der vollständige Datenstand liegt zusätzlich je Fassung im Archiv.

## Wie die Unveränderbarkeit funktioniert

- Jedes Dokument wird mit seiner **SHA-256-Prüfsumme** übertragen; der Archivserver nimmt es nur an, wenn die
  Prüfsumme stimmt. Danach ist die Datei schreibgeschützt.
- Es gibt **keine Funktion zum Überschreiben**. Wer unter derselben Kennung etwas anderes ablegen will, bekommt
  „unveränderbar“ zurück.
- **Löschsperre:** Löschen geht erst nach Ablauf der Frist und wird protokolliert (die Metadaten bleiben).
- **Journal mit Hash-Kette:** Jede Aktion (Ablage, Fristverlängerung, Löschen, Prüfung) steht in `ledger.jsonl`;
  jeder Eintrag enthält den Hash des vorigen. Eine nachträgliche Änderung irgendwo bricht die Kette.
- **Prüfprotokoll:** Täglich (und auf Knopfdruck „Archiv prüfen“) rechnet der Archivserver die Kette und alle
  Prüfsummen nach. Ergebnis in EstrichONE und auf dem Archivrechner unter `reports/`.
- **Nachweis in EstrichONE:** je Dokument Zeitpunkt, Frist, Journal-Nummer und Journal-Hash (System → Archiv,
  Dialog „Archivnachweis“ für Bauakte/Dokument, API `GET /api/v1/archive/files/{id}` und `/archive/bauakten/{id}`).

Ehrlicher Hinweis: Wer Root-Rechte auf dem Archivrechner hat, kann Dateien auf der Festplatte trotzdem verändern –
das fällt aber bei der nächsten Prüfung sofort auf (Kette/Prüfsumme). Gegen das physische Löschen hilft nur eine
Sicherung des Archivs (z. B. Archiv-Volume zusätzlich auf USB-Platte oder in ein zweites Backup).

## Betriebsprüfung (Export)

System → Archiv → **„Export für Betriebsprüfer“** lädt eine ZIP-Datei mit: Dokumenten im Original, Metadaten,
Verzeichnis (CSV), Prüfsummen (`sha256sums.txt`), vollständigem Journal, aktuellem Prüfprotokoll und einem kleinen
Prüfskript. Der Prüfer kann ohne EstrichONE prüfen:

```bash
sha256sum -c sha256sums.txt
python3 journal_pruefen.py journal.jsonl
```

Direkt auf dem Archivrechner: `sudo docker exec estrichone-archive estrichone-archive export /data/export.zip`
(liegt dann im Volume, z. B. `sudo docker cp estrichone-archive:/data/export.zip .`).

## Befehle auf dem Archivrechner

```bash
sudo docker logs estrichone-archive                          # Protokoll, Kopplungscode, Fingerabdruck
sudo docker exec estrichone-archive estrichone-archive pair    # neuen Kopplungscode erzeugen
sudo docker exec estrichone-archive estrichone-archive status  # gekoppelte Server, Bestand, letzte Prüfung
sudo docker exec estrichone-archive estrichone-archive verify  # Prüfung jetzt
```

**Aktualisieren:** den Installationsbefehl von oben einfach erneut ausführen. Daten, Zertifikat und Kopplungen bleiben.

**Sichern:** Alle Daten liegen im Docker-Volume `estrichone-archive` (bzw. mit `-e ESTRICHONE_DATA_DIR=/srv/archiv` in
einem Ordner). Dieses Volume gehört zusätzlich auf eine zweite Platte – das Archiv ist sonst nur einmal vorhanden.

## Technik (für Interessierte)

Eigenes kleines Image auf Basis .NET 10 („chiseled“: ohne Shell und Paketmanager, Benutzer ohne Root-Rechte,
schreibgeschütztes Dateisystem außer `/data`). Bewusst keine Objektspeicher-Software wie MinIO: deren WORM-Funktion
(Object Lock) braucht S3-Zugangsschlüssel, Bucket-Richtlinien und eine eigene Zertifikatsverwaltung – genau die
„komplizierte Konfiguration“, die hier vermieden werden soll. Das Archiv kann außerdem Journal, Prüfprotokoll und
Export für den Betriebsprüfer direkt liefern.

Kopplung (Protokoll v1): Der Archivserver hat eine eigene Zertifizierungsstelle (CA). EstrichONE ruft das Zertifikat
ab, und beide Seiten beweisen per HMAC-SHA256 über den Fingerabdruck dieser CA, dass sie den Einmal-Code kennen. Ein
Angreifer dazwischen hätte einen anderen Fingerabdruck, die Beweise passen dann nicht. Danach authentisiert sich
EstrichONE mit einem Zugangstoken (in EstrichONE verschlüsselt per ASP.NET Data Protection, auf dem Archivserver nur
als SHA-256 gespeichert). Der Archivserver ist über die gepinnte CA authentisiert. Nach 5 falschen Codes ist der
Code verbraucht.

Schnittstelle (HTTPS, Port 8443): `GET /pair/hello`, `POST /pair/complete`, mit Token: `GET /api/v1/status`,
`POST /api/v1/objects/{id}` (Kopfzeilen `X-Archive-Sha256`, `X-Archive-Metadata`), `GET /api/v1/objects/{id}[/receipt]`,
`POST /api/v1/objects/{id}/retention` (nur verlängern), `DELETE /api/v1/objects/{id}` (erst nach Fristende),
`POST /api/v1/verify`, `GET /api/v1/export`.
