% datei: Schulungsleitfaden-Trainer
% art: Schulungsleitfaden · für Trainer
% fuer: Trainerinnen und Trainer, Projektverantwortliche der Einführung
% stil: dokument
% inhalt: ja

# Schulungsleitfaden für die Einführung von EstrichONE

## So nutzen Sie diesen Leitfaden

Der Leitfaden beschreibt vier Schulungen, die zusammen alle Rollen eines Estrichbetriebs abdecken. Jede Schulung hat
einen Ablaufplan mit Zeiten, Lernziele, Übungen Schritt für Schritt mit Lösung und die typischen Fragen der
Teilnehmenden.

| Zielgruppe | Dauer | Gerät | Arbeitsblatt | Wissenstest |
|---|---|---|---|---|
| **Kolonne / Monteure** | ca. 60 min | iPhone oder iPad (App) | Arbeitsblatt Kolonne | Wissenstest Kolonne |
| **Bauleitung** | ca. 90 min | iPhone/iPad und Browser | Arbeitsblatt Bauleitung | Wissenstest Bauleitung |
| **Büro / Disposition** | ca. 2 h | Browser (PC), optional iPhone | Arbeitsblatt Büro | Wissenstest Büro |
| **Administration** | ca. 2 h | Browser, Server-Zugang (SSH) | Arbeitsblatt Administration | Wissenstest Administration |

Zu jeder Schulung gehören die passenden **Kurzanleitungen** (eigene PDFs, auch als Sammel-PDF) zum Mitgeben und
Aushängen, die **Checkliste Einführung im Betrieb** für die Projektleitung und die **Teilnahmebestätigung** als Vorlage.
Nachschlagewerke sind das **Benutzerhandbuch Web** und das **Benutzerhandbuch App**.

> **Wichtig:** Schulen Sie, was Ihr Betrieb wirklich eingeschaltet hat. Seiten und Schaltflächen ohne passendes Recht
> blendet EstrichONE aus; die Module **Chat** und **Material** sind abschaltbar (der Chat ist ab Werk aus). Prüfen Sie
> vorher Rollen und Module, damit die Teilnehmenden genau das sehen, was der Leitfaden beschreibt.

### Grundsätze für die Schulung

- **Selbst tun statt zusehen:** Jede Teilnehmerin, jeder Teilnehmer arbeitet am eigenen Gerät. Der Trainer zeigt einen
  Schritt einmal am Beamer, dann machen alle mit.
- **Echte Arbeitssituationen:** Die Übungen folgen einem Arbeitstag – Einsatz ansehen, Foto, Tagebuch, Mangel, Abnahme.
- **Kleine Gruppen:** Kolonnen höchstens 8 Personen, Büro und Administration höchstens 6.
- **Kurzanleitung als roter Faden:** Die Teilnehmenden arbeiten mit der ausgedruckten Kurzanleitung – so finden sie
  sich später allein zurecht.

## Vorbereitung

### Zwei Wochen vorher

- [ ] Termine je Zielgruppe festlegen; Kolonnen möglichst an einem Regentag oder vor Arbeitsbeginn schulen.
- [ ] Rollen und Rechte klären (**Benutzer & Geräte** → **Rollen und Rechte**): Wer ist **Baustelle**, wer **Büro und
  Bauleitung**, wer **IT-Administration**, wer **Administrator**? Wer darf planen (Recht „Planung … planen“)?
- [ ] Module festlegen: **Chat** (vorher mit dem Betriebsrat abstimmen, § 87 BetrVG) und **Material**
  (**Systemeinstellungen** → **Module**).
- [ ] Übungsumgebung wählen (siehe unten) und einrichten.
- [ ] Benutzerkonten für alle Teilnehmenden anlegen, Zugangsbriefe drucken (Abschnitt Administration).

### Übungsumgebung: Demomodus der App

Für alle App-Übungen eignet sich der eingebaute **Demomodus** am besten – er braucht keinen Server und verändert keine
echten Daten:

1. App öffnen → **Demo ausprobieren – ohne Server**.
2. Die App startet mit der fiktiven **Estrichbau Muster GmbH**: sechs Bauakten in allen Ständen – u. a.
   **2026-041 EFH Schneider, Neubau** (in Ausführung, heutiger Einsatz „Estrich einbringen OG“), **2026-042 Halle
   Gewerbering** (geplant), **2026-038 Praxis Dr. Maier, Sanierung** (Trocknung), **2026-047 Anbau Weber** (Anfrage)
   und **2026-021 Kindergarten Sonnenschein** (abgeschlossen) – dazu Einsätze der Woche, „Wer fährt wohin“ mit drei
   Kolonnen, Chats und eine Meldung an die IT.
3. Die Demo meldet automatisch **Max Huber** an. Zum Üben der Anmeldung unter **Mehr** abmelden; Demozugänge:
   `m.huber` (ohne) oder `s.berger` (mit Zwei-Faktor-Anmeldung), Passwort `demo`, Code `246810`.
4. **Demo** → **Störungen vorführen** zeigt Wartung, Server-Ausfall („Nur Ansicht“) und Ausfälle von Backup- und
   Archivserver.
5. **Mehr** → **Benachrichtigungen** → **Beispiel senden** schickt nach wenigen Sekunden eine echte Mitteilung.
6. Nach der Schulung: **Demo** → **Demo beenden** löscht alle Demodaten.

Im Demomodus wird **nichts gesendet**; Kamera, Scanner und Videoaufnahme funktionieren trotzdem. Disponieren ist in
der Demo möglich (die Demo-Person hat das Planungsrecht).

> **Hinweis:** Den Demomodus gibt es in der App für iPhone und iPad. Die Android-App hat (noch) keinen Demomodus und
> deutlich weniger Funktionen (Benutzerhandbuch App, Anhang Android).

### Übungsumgebung: Web

Für die Web-Übungen gibt es zwei Wege:

**A) Eigene Übungsinstanz mit Beispieldaten (empfohlen für Büro und Administration).** Die Skripte des Handbuchs
starten auf einem Trainer-Rechner mit Docker, .NET SDK, Node.js ≥ 22.12 und Python 3 eine frische Instanz mit den
fiktiven Daten der „Estrichbau Muster GmbH“ (Kunden, Bauakten, Kolonnen, Fahrzeuge, Material, Personal):

| Schritt | Befehl (im Repository) |
|---|---|
| Instanz starten (Datenbank, API, Backup-Dienst, Web unter `http://localhost:4291`) | `docs/handbuch/scripts/umgebung.sh` |
| Ersteinrichtung (Administrator `c.berger`) | `node docs/handbuch/scripts/bilder.mjs einrichtung` |
| Beispieldaten über die API anlegen | `python3 docs/handbuch/scripts/demodaten.py` |
| Nach der Schulung beenden | `docs/handbuch/scripts/umgebung.sh stop` |

Voraussetzungen wie im README unter `docs/handbuch` (u. a. `npm ci` in `web/`). Anmeldung als Administrator: `c.berger`, Passwort `Estrich-Muster-2026!`. Die Beispielbenutzer (z. B. `m.keller` und
`t.hofmann` – Büro und Bauleitung, `m.huber` – Baustelle, `k.albrecht` – IT-Administration) haben kein bekanntes
Passwort: Vergeben Sie es vor der Schulung mit **Benutzer** → **Passwort zurücksetzen** (Einmalpasswort). Die Instanz
läuft nur auf diesem Rechner (`http://localhost`); Teilnehmende arbeiten dort per Bildschirmfreigabe oder am
Trainer-Rechner. Für die App nutzen Sie den Demomodus – die App verbindet sich nur mit Servern mit gültigem Zertifikat.

**B) Im Echtsystem mit Übungsakten.** Legen Sie je Teilnehmer eine Übungsbauakte an, z. B. „SCHULUNG Meier – bitte
nicht verplanen“, mit einem Übungskunden „Schulung Musterkunde“. Nach der Schulung werden die Akten **storniert**
(Grund: „Schulung“) oder – mit dem Recht „Bauakten löschen“ – **In den Papierkorb** gelegt. Planen Sie Übungseinsätze
nur auf eine Übungskolonne und geben Sie diese Tage **nicht** frei, damit keine echte Kolonne benachrichtigt wird.

> **Achtung:** Im Echtsystem sind Benachrichtigungen, E-Mails (z. B. Freigabelink an den Kunden, Bestellschein an den
> Lieferanten) und Pushnachrichten **echt**. Verwenden Sie für Übungen nur eigene E-Mail-Adressen und keine echten
> Lieferanten.

### Am Schulungstag

- [ ] Beamer bzw. Bildschirm mit Spiegelung des iPhones/iPads (AirPlay) oder des Browsers.
- [ ] WLAN für alle Geräte; für die Offline-Übung den Flugmodus erklären.
- [ ] Geräte geladen, App aus dem App Store installiert (iOS/iPadOS 17 oder neuer).
- [ ] Ausgedruckt: Arbeitsblätter, passende Kurzanleitungen, Wissenstests, Teilnahmebestätigungen.
- [ ] Für die Echtsystem-Einrichtung: QR-Codes (**Benutzer & Geräte** → **Geräte** → **Gerät einrichten**) erst in der
  Schulung erzeugen – sie gelten nur kurz und nur einmal.

## Schulung Kolonne / Monteure (60 min)

### Lernziele

Nach der Schulung können die Teilnehmenden

- die App einrichten bzw. entsperren und auf einem Poolgerät mit PIN den Benutzer wechseln,
- ihren Einsatz des Tages finden, die Route starten und den Ansprechpartner anrufen,
- Fotos mit Beschriftung, Lieferscheine als PDF, Bautagebuch-Einträge und Mängel erfassen,
- erklären, was ohne Netz passiert, was „Nur Ansicht“ bedeutet und wann sie die Administration informieren,
- den Chat ihrer Kolonne bzw. Baustelle nutzen (falls eingeschaltet).

### Ablaufplan

| Zeit | Thema | Methode | Material |
|---|---|---|---|
| 0:00 | Begrüßung, warum EstrichONE: „Alles zur Baustelle in einer Akte, auch ohne Netz“ | Vortrag | – |
| 0:05 | Einrichten und Anmelden (QR-Code oder Demo), Poolgerät mit PIN | Vormachen, Mitmachen | Kurzanleitung 01 |
| 0:15 | Die Heute-Seite: Mein Einsatz, Route, Anrufen, Meine Woche, Wer fährt wohin | Mitmachen | Kurzanleitung 05 |
| 0:22 | Fotos mit Beschriftung, Lieferschein scannen | Übung K1 | Kurzanleitung 02 |
| 0:32 | Bautagebuch mit Textbausteinen, Mangel mit Frist, Aufgabe erledigen | Übung K2 | Kurzanleitung 03 |
| 0:42 | Offline arbeiten: Flugmodus, Synchronisation, „Nur Ansicht“, Leisten | Übung K3, Vorführung | Kurzanleitung 07 |
| 0:50 | Chat der Kolonne (falls eingeschaltet), Problem melden | Mitmachen | Kurzanleitung 06 |
| 0:55 | Wissenstest, Fragen, Ausgabe der Kurzanleitungen | Test | Wissenstest Kolonne |

### Übungen mit Lösung

**Übung K1 – Fotoserie und Lieferschein (Bauakte EFH Schneider)**

1. Heute-Seite → unter **Mein Einsatz** auf **Foto** tippen.
2. Beschriftung „Randdämmung OG“ eingeben, zwei Fotos aufnehmen, **Übernehmen**.
3. Bauakte öffnen (**Akte**) → **Scanner** → ein Blatt Papier als Lieferschein scannen → **Dokumentart** „Lieferschein“
   → **Als PDF ablegen**.

> **Lösung:** Beide Fotos und das PDF stehen in der Bauakte unter **Fotos & Dokumente**. Die Fotos tragen
> die Beschriftung „Randdämmung OG“. Ohne Netz zeigt die App „Foto gespeichert – wird übertragen, sobald Netz da ist.“

**Übung K2 – Tagebuch, Mangel, Aufgabe erledigen**

1. Heute-Seite → **Tagebuch** → Textbaustein „Estrich eingebracht“ → **Sichern**.
2. **Mangel** → „Randdämmstreifen im Bad fehlt“ → Frist **Morgen** → Zuständig: sich selbst → **Sichern**.
3. Kachel **Meine Aufgaben** → den neuen Mangel mit dem **Kreis** erledigen → wieder öffnen.

> **Lösung:** Der Eintrag steht im **Bautagebuch** unter dem heutigen Tag. Der Mangel erscheint in der
> Bauakte unter **Aufgaben & Mängel** und unter **Meine Aufgaben** mit „morgen fällig“. Nach dem Tipp auf den Kreis
> steht er unter **Erledigt**, **Wieder öffnen** macht das rückgängig.

**Übung K3 – Ohne Netz**

1. Flugmodus einschalten. Ein Foto aufnehmen und einen Tagebuch-Eintrag sichern.
2. **Mehr** → **Synchronisation** öffnen: Was steht unter **Ausstehend**?
3. Flugmodus aus, App im Vordergrund lassen, erneut nachsehen.

> **Lösung:** Offline stehen Foto und Eintrag unter **Ausstehend**. Mit Netz werden sie automatisch
> übertragen; oben steht „Alles übertragen“. (Im Demomodus wird nichts gesendet – die Übung im Demomodus nur zeigen,
> „Nur Ansicht“ über **Demo** → **Störungen vorführen**.)

### Typische Fragen

| Frage | Antwort |
|---|---|
| „Muss ich jedes Mal mein Passwort eingeben?“ | Nein. Persönliches Gerät: Face ID oder Gerätecode. Poolgerät: Name antippen, PIN. Das Passwort nur nach Abmelden, Passwortänderung, Sperre des Geräts oder langer Nichtnutzung (Standard 90 Tage). |
| „Was ist, wenn ich im Keller kein Netz habe?“ | Weiterarbeiten. Alles liegt verschlüsselt auf dem Gerät und wird später übertragen. Nur PDFs und Verlauf brauchen Netz. |
| „Die Leiste ist grau und ich kann nichts erfassen.“ | „Nur Ansicht“: Server seit über 5 Minuten nicht erreichbar. Netz prüfen; im Notfall **Trotzdem arbeiten**. |
| „Darf ich die App löschen, wenn sie spinnt?“ | Nicht, solange unter **Synchronisation** etwas **Ausstehend** ist. Besser **Mehr** → **Problem melden**. |
| „Sieht das Büro meine Fotos sofort?“ | Ja, sobald sie übertragen sind – in derselben Bauakte im Web. |
| „Wo ist der Chat?“ | Nur sichtbar, wenn der Betrieb das Modul eingeschaltet hat. |

## Schulung Bauleitung (90 min)

### Lernziele

Zusätzlich zu den Zielen der Kolonne können die Teilnehmenden

- eine neue Bauakte unterwegs (App) und im Büro (Web) mit Dublettenprüfung anlegen,
- eine Erstbegehung mit Prüfpunkten, Fotos und Unterschrift sowie eine CM-Messung erfassen,
- eine Abnahme mit Vorbehalt, Mängeln und Unterschriften erfassen und einen Freigabelink für den Kunden erstellen,
- Räume und Flächen sowie Basisdaten pflegen,
- in „Wer fährt wohin“ disponieren (mit Planungsrecht) und Dokumente zur Freigabe anfordern bzw. freigeben.

### Ablaufplan

| Zeit | Thema | Methode | Material |
|---|---|---|---|
| 0:00 | Begrüßung, Lebenslauf einer Bauakte (Anfrage → Geplant → In Ausführung → Belegreif / Trocknung → Abgeschlossen) | Vortrag | Handbuch Web 3.1 |
| 0:10 | App-Grundlagen im Schnelldurchlauf (Heute, Foto, Tagebuch, Mangel) | Mitmachen | Kurzanleitungen 02, 03 |
| 0:20 | Neue Bauakte in der App (Assistent, 4 Schritte) | Übung B1 | – |
| 0:30 | Erstbegehung und CM-Messung | Übung B2 | Kurzanleitung 17 |
| 0:45 | Abnahme mit Unterschrift, Freigabelink | Übung B3 | Kurzanleitung 04 |
| 1:00 | Web: Bauakte im Überblick, Dokumente, Freigaben anfordern/erteilen | Vorführung, Übung B4 | Kurzanleitung 08 |
| 1:10 | Disposition unterwegs: Baustelle zuweisen, tauschen, Freigabe | Übung B5 | Kurzanleitung 05 |
| 1:20 | Offline-Regeln, Chat der Baustelle, Wissenstest | Test | Wissenstest Bauleitung |

### Übungen mit Lösung

**Übung B1 – Neue Bauakte in der App**

1. Tab **Baustellen** → **„+“**.
2. **Für wen bauen wir?** → **Neuen Kunden anlegen** (Privatperson „Familie Probst“).
3. **Wo ist die Baustelle?** → Bauvorhaben „EFH Probst, Neubau“, **Adresse des Kunden übernehmen**.
4. **Was wird gebaut?** → Estrichart CT, Nenndicke 65 mm, Fußbodenheizung.
5. **Alles richtig?** → **Gleich danach**: **Erstbegehung** → **Anlegen**.

> **Lösung:** Die Bauakte heißt bis zur Übertragung „Neu · noch ohne Nummer“; die Nummer vergibt der Server.
> Gibt es an der Adresse schon eine Bauakte, bietet die App **Öffnen statt neu anlegen** an.

**Übung B2 – Erstbegehung und CM-Messung**

1. In der neuen (oder in der Demo-Bauakte **EFH Schneider**) **Mehr** → **Erstbegehung**.
2. Alle Prüfpunkte **In Ordnung**, einen Punkt **Mangel** mit **Foto zu diesem Punkt** und kurzer Beschreibung.
3. Kunde unterschreibt unter **Fotos und Unterschrift** → **Erstbegehung sichern**.
4. **Mehr** → **CM-Messung**: Zementestrich (CT), beheizt, Messort „Bad OG“, Wert **2,1** CM-%.

> **Lösung:** Die Erstbegehung steht unter **Protokolle**. Die CM-Messung zeigt **Noch nicht belegreif**:
> Der Grenzwert für beheizten Zementestrich ist **1,8 CM-%** (unbeheizt 2,0; CA 0,5 bzw. 0,3).

**Übung B3 – Abnahme mit Vorbehalt**

1. Bauakte → **Mehr** → **Abnahme** → Ergebnis **Mit Vorbehalt**.
2. Abgenommene Leistung „Zementestrich CT-C25-F4, 65 mm, EG“ eintragen.
3. **Mangel hinzufügen**: „Haarriss an der Türschwelle Bad“.
4. Nur **Auftragnehmer** unterschreibt → **Abnahme sichern**.
5. Mit Verbindung (nicht im Demomodus): Abnahme unter **Protokolle** öffnen → **Freigabelink erstellen**.

> **Lösung:** Der Mangel steht als **Aufgabe mit Frist** unter **Aufgaben & Mängel**. Der Kunde kann über den
> befristeten Link ohne Anmeldung freigeben oder mit Begründung ablehnen; danach zeigt die Bauakte „Online freigegeben
> von …“.

**Übung B4 – Dokument zur Freigabe (Web)**

1. Bauakte → **Dokumente** → eine PDF hochladen (Dokumentart **Pläne**).
2. Menü der Zeile → **Freigabe anfordern** → Kollegin aus der Gruppe, Frist, Hinweis.
3. Die Kollegin öffnet die Card **Warten auf Genehmigung** und klickt **Freigeben** (oder **Ablehnen** mit Kommentar).

> **Lösung:** Bis zur Entscheidung trägt das Dokument die Marke **Wartet auf Freigabe**. Alle Freigaben
> stehen unter **Auftragsbearbeitung** → **Dokumente**, Reiter **Freigaben**.

**Übung B5 – Disposition unterwegs (App, Planungsrecht nötig)**

1. **Wer fährt wohin** → morgen wählen → bei einer Kolonne **Anpassen** → **Baustelle zuweisen**.
2. Bauvorhaben suchen, Tätigkeit „Erstbegehung“, Uhrzeit 8:00 → Prüfung ansehen → **Einplanen**.
3. **Anpassen** → **Mitarbeiter tauschen**: eine Person tauschen.
4. **Freigabe** → **Freigeben bis** morgen → Hinweis „Treffpunkt 6:30 Uhr am Lager“ → **Freigeben**
   (im Echtsystem nur für die Übungskolonne!).

> **Lösung:** Der Einsatz steht in der Kolonne; nach der Freigabe sind spätere Änderungen mit **Neu** bzw.
> **Geändert** markiert. Tauschaktionen gelten nur für den gewählten Tag. Konfliktprüfung und Freigabe brauchen Verbindung.

### Typische Fragen

| Frage | Antwort |
|---|---|
| „Kann ich eine Erstbegehung nachträglich ändern?“ | Sie wird nicht überschrieben, sondern **korrigiert**; die Bauakte zeigt dann die korrigierte Fassung. |
| „Der Kunde ist bei der Abnahme nicht da.“ | Ohne seine Unterschrift sichern, dann **Freigabelink** (auf Wunsch per E-Mail). Im Web: Häkchen **Auftraggeber nicht vor Ort: Freigabe per Link an den Kunden**. |
| „Woher kommen die Prüfpunkte der Erstbegehung?“ | Die Administration legt sie fest: **Stammdaten** → **Formulare** → **Erstbegehung**. |
| „Warum wechselt die Bauakte von selbst auf ‚In Ausführung‘?“ | Status-Automatik unter **Systemeinstellungen** → **Bauakten** (erster Einsatztag oder erste Aktivität vor Ort). |
| „Ich sehe eine Bauakte nicht.“ | Mit „Eigene Bauakten sehen“ nur zugewiesene Akten; im Web **Mir zuweisen**, in der App ggf. **Mir zuweisen** beim Öffnen per Link. |

## Schulung Büro / Disposition (2 h)

### Lernziele

Die Teilnehmenden können

- sich anmelden, die Oberfläche bedienen (Suche, Navigation, Cards, Benutzermenü) und die Zwei-Faktor-Anmeldung einrichten,
- Kunden pflegen, Anrufe mit Wiedervorlage erfassen und Anfragen in Bauakten überführen,
- Bauakten anlegen und pflegen (Kontakte, Dokumente mit Fassungen, Freigaben, Notizen, Ausdrucke),
- im Disponenten-Cockpit einplanen, Konflikte lösen und den Plan freigeben,
- Urlaubsanträge stellen und genehmigen, Material bestellen und Bestellungen verfolgen.

### Ablaufplan

| Zeit | Thema | Methode | Material |
|---|---|---|---|
| 0:00 | Begrüßung, Anmeldung, eigenes Passwort, Oberfläche, Suche, Einstellungen | Mitmachen | Handbuch Web Kap. 2 |
| 0:15 | Kunden, Telefon-Schnellerfassung, Anfragen | Übung U1 | Kurzanleitung 10 |
| 0:30 | Neue Bauakte mit Dublettenprüfung, Kontakte, Team | Übung U2 | Kurzanleitung 08 |
| 0:45 | Dokumente: Hochladen, Fassungen, Freigaben; Ausdrucke unter **Aktionen** | Übung U3 | Handbuch Web 3.6 |
| 1:00 | Pause | – | – |
| 1:10 | Disponenten-Cockpit: Vorrat, Ziehen, Konflikte, Freigabe; Tastatur | Übung U4 | Kurzanleitung 09, Spickzettel |
| 1:35 | Urlaub beantragen/genehmigen, Urlaubsplan | Übung U5 | Kurzanleitung 11 |
| 1:45 | Material bestellen (falls Modul aktiv), Tagesübersicht | Übung U6 | Kurzanleitung 12 |
| 1:55 | Wissenstest, Fragen | Test | Wissenstest Büro |

### Übungen mit Lösung

**Übung U1 – Anruf wird zur Anfrage**

1. Kopfzeile → **Telefon**-Symbol. **Wer ruft an?** „Familie Probst“, Rückrufnummer, Richtung **Eingehend**.
2. Notiz „EFH, ca. 140 m², Fußbodenheizung, Wunschtermin Mai“ → **Als Anfrage speichern**.
3. **Auftragsbearbeitung** → **Anfragen** → Anfrage öffnen → Status **In Bearbeitung** → in eine Bauakte übernehmen.

> **Lösung:** Aus der Anfrage entsteht eine Bauakte (Status **Anfrage** oder direkt als Auftrag); die Anfrage
> gilt damit als erledigt. Der Anruf steht unter **Büro und Stammdaten** → **Anrufe**.

**Übung U2 – Neue Bauakte mit Dublettenprüfung**

1. <kbd>Alt</kbd> + <kbd>N</kbd>. Kunde: einen Namen eines **vorhandenen** Kunden leicht anders schreiben, aber dieselbe
   Telefonnummer eingeben → **Weiter**.
2. Hinweis „Es gibt bereits ähnliche Kunden“ → vorhandenen Kunden wählen → **Diesen Kunden verwenden**.
3. Bauvorhaben eintragen, **Anlegen als** **Bauakte (Auftrag)**, Vorlage wählen (falls vorhanden) → **Anlegen**.
4. **Kontakte** → **Kontakt hinzufügen** → **Neuen Kontakt erfassen …** (Architekt); **Team im Betrieb** →
   **Mitarbeiter zuweisen**.

> **Lösung:** Es entsteht **kein** doppelter Kunde. Die Aktennummer wird automatisch vergeben (z. B.
> 2026-001). Zugewiesene Mitarbeiter sehen die Akte auch mit dem Recht „Eigene Bauakten sehen“.

**Übung U3 – Dokument mit Fassung und Freigabe**

1. Bauakte → **Dokumente** → PDF per Ziehen hochladen, Dokumentart **Angebote**.
2. Menü der Zeile → **Neue Fassung hochladen …** → geänderte Datei wählen.
3. Menü → **Freigabe anfordern** an eine Kollegin; die Kollegin gibt frei.
4. **Aktionen** → **Bauaktenblatt drucken**.

> **Lösung:** Die Liste zeigt v2 als aktuelle Fassung, **Alle Fassungen** blendet v1 ein. Der Ausdruck
> entsteht als PDF mit Firmenlogo und wird zusätzlich in der Bauakte abgelegt.

**Übung U4 – Cockpit**

1. **Planung** → **Disponenten-Cockpit** → Ansicht **Woche**.
2. Ein Bauvorhaben aus dem **Vorrat** auf eine Kolonne und Mittwoch ziehen.
3. Den Einsatz mit <kbd>Strg</kbd> + Ziehen auf Donnerstag kopieren, dann <kbd>Strg</kbd> + <kbd>Z</kbd>.
4. **Konflikte** öffnen und einen Konflikt durch Verschieben lösen (z. B. abwesende Person).
5. **Plan freigeben** → Zeitraum **morgen** → Hinweis → **Freigeben** (im Echtsystem nur die Übungskolonne).

> **Lösung:** Der kopierte Einsatz verschwindet nach <kbd>Strg</kbd> + <kbd>Z</kbd> wieder. Nicht
> freigegebene Tage tragen **Entwurf**; nach der Freigabe erhalten die Mitarbeiter eine Benachrichtigung und sehen den
> Tag in der App unter **Wer fährt wohin**.

**Übung U5 – Urlaub**

1. **Personal (HR)** → **Mein Urlaub / Urlaubsantrag** → **Urlaub beantragen** (zwei Tage nächsten Monat).
2. Partnerin/Partner mit Genehmigungsrecht: Reiter **Anträge** → **Genehmigen**.
3. Reiter **Urlaubsplan** → Monat ansehen.

> **Lösung:** Beantragte Tage sind schraffiert, genehmigte als **U** eingetragen. Die Planung zeigt die
> Person an diesen Tagen als abwesend.

**Übung U6 – Material bestellen**

1. Bauakte → **Material** → Reserve 5 % → Positionen markieren → **Auswahl bestellen**.
2. Lieferant, Liefertermin, Zeitfenster → **Speichern** (Entwurf).
3. **Drucken / PDF** → **Nur als bestellt markieren**.

> **Lösung:** Status **Bestellt**; der Bestellschein liegt in der Bauakte, die Lieferung erscheint im Kopf
> der Bauakte und in den Kennzahlen **Lieferungen heute** am Liefertag.

### Typische Fragen

| Frage | Antwort |
|---|---|
| „Kann ich eine Seite als Lesezeichen speichern?“ | Ja, jede Seite hat eine eigene Adresse – auch die Tagesübersicht mit Datum. |
| „Der Plan zeigt ein rotes Dreieck.“ | Konflikt (z. B. Person abwesend). Konflikte sind Hinweise; Sie entscheiden bewusst. |
| „Ich habe etwas falsch verschoben.“ | <kbd>Strg</kbd> + <kbd>Z</kbd> im Cockpit. |
| „Wie bekomme ich den Plan an die Wand im Lager?“ | **Drucken** im Cockpit, **Wochen-PDF** in der Kolonnenplanung oder **Drucken** in der Tagesübersicht. |
| „Wo sind abgeschlossene Bauakten?“ | **Auftragsbearbeitung** → **Altakten** oder über die Suche. |
| „Warum sehe ich ‚Material‘ nicht?“ | Modul ausgeschaltet oder Recht fehlt. |

## Schulung Administration (2 h)

### Lernziele

Die Teilnehmenden können

- Benutzer mit Rollen anlegen, Passwörter und Zwei-Faktor-Anmeldung zurücksetzen, Konten sperren,
- Rollen und Rechte anpassen, Stammdaten (Wertelisten, Formulare, Vorlagen) pflegen,
- Geräte per QR-Code einrichten, freigeben, sperren und entfernen,
- Systemeinstellungen (Firma, Logo, Anmeldung, Benachrichtigungen, Module, Speicher) und das E-Mail-Konto einrichten,
- die Datensicherung prüfen, das Notfallblatt sichern, einzelne Dateien und das System wiederherstellen,
- einen Kombiserver (Backup-Ziel und Archiv) koppeln, Updates einspielen und Wartungen ankündigen.

### Ablaufplan

| Zeit | Thema | Methode | Material |
|---|---|---|---|
| 0:00 | Verwaltung im Überblick, Statuszeilen, Zwei-Faktor-Anmeldung für Admins | Vorführung, Übung A1 | Handbuch Web Kap. 9 |
| 0:15 | Benutzer, Rollen und Rechte, Zugangsbrief | Übung A2 | Kurzanleitung 13 |
| 0:30 | Geräte: QR-Code, Poolgerät, Sperren | Übung A3 | Kurzanleitung 13, 01 |
| 0:45 | Systemeinstellungen, Stammdaten, E-Mail, Module | Übung A4 | Handbuch Web 9.3–9.7 |
| 1:05 | Pause | – | – |
| 1:15 | Backup Center, Notfallblatt, Wiederherstellen (einzelne Datei) | Übung A5 | Kurzanleitung 14 |
| 1:30 | Backup-Ziele, Kombiserver, Archiv, Überwachung | Vorführung | Kurzanleitung 15 |
| 1:45 | Updates, Wartung planen, Protokoll | Übung A6 | Kurzanleitung 16 |
| 1:55 | Wissenstest, Fragen | Test | Wissenstest Administration |

### Übungen mit Lösung

**Übung A1 – Zwei-Faktor-Anmeldung einrichten**

1. Benutzermenü → **Anmeldung und Sicherheit** → **Zwei-Faktor-Anmeldung** → **Einrichten**.
2. QR-Code mit der Authenticator-App scannen, Code eingeben → **Einschalten**.
3. **Wiederherstellungscodes** sichern.

> **Lösung:** Das Schild **2FA** erscheint in der Benutzerliste. Unter **Systemeinstellungen** →
> **Anmeldung** lässt sich die **Pflicht für Administratoren** einschalten – erst danach, wenn alle Admins eingerichtet sind.

**Übung A2 – Benutzer und Rolle**

1. **Benutzer & Geräte** → **Benutzer** → **Benutzer anlegen**: „Test Monteur“, Anmeldename „t.monteur“, Rolle **Baustelle**.
2. **Zugangsbrief (PDF)** öffnen.
3. **Rollen und Rechte** → **Rolle anlegen** „Disposition“ → passende Rechte setzen → **Änderungen speichern**.
4. Den Testbenutzer **sperren**.

> **Lösung:** Der Zugangsbrief enthält Anmeldedaten und Anleitung (DIN 5008). Gesperrte Benutzer bleiben
> mit Verlauf erhalten – gelöscht wird nicht. Die Rolle **Administrator** hat immer alle Rechte.

**Übung A3 – Gerät per QR-Code**

1. **Geräte** → **Gerät einrichten** → **Poolgerät (mehrere Personen)** → Name „iPad Kolonne Test“ → **Code erzeugen**.
2. In der App (im Echtsystem): **QR-Code scannen** → anmelden → PIN festlegen.
3. Im Web das Gerät **Sperren**, dann **Entsperren**.

> **Lösung:** Mit Code eingerichtet ist das Gerät sofort freigegeben. Der Code gilt nur kurz und einmal. Für
> den QR-Code muss die öffentliche Serveradresse (`ESTRICHONE_DOMAIN`) konfiguriert sein.

**Übung A4 – Systemeinstellungen**

1. **Organisation** → **Systemeinstellungen** → **Firma**: Firmendaten prüfen, **Logo hochladen …** → **Speichern**.
2. **Integrationen** → **E-Mail**: SMTP-Daten eintragen → **Testmail senden**.
3. **Module**: Chat bzw. Material nach Betriebsentscheidung.
4. **Stammdaten** → **Formulare** → **Erstbegehung**: einen Prüfpunkt ergänzen (**Feld hinzufügen**).

> **Lösung:** Das Logo erscheint in der Kopfzeile und auf allen PDFs. Die Testmail steht im
> **Versandprotokoll**. Der neue Prüfpunkt erscheint bei der nächsten Erstbegehung in Web und App.

**Übung A5 – Sicherung**

1. **Datensicherung & Archiv** → **Sicherung** → **Jetzt sichern**.
2. Reiter **Notfall** → **Notfallblatt (PDF)** öffnen (im Echtsystem ausdrucken und wegschließen).
3. Reiter **Sicherungen** → Stand wählen → **Dokumentsymbol** → eine Datei zurückholen.

> **Lösung:** Die einzelne Datei kommt zurück, ohne den Rest zu verändern. **Wiederherstellen** des ganzen
> Systems (mit Eingabe `WIEDERHERSTELLEN`) nur in der Übungsinstanz vorführen – Änderungen nach dem Stand gehen verloren.

**Übung A6 – Wartung und Update**

1. **System & Betrieb** → **Hinweise & Wartung** → **Wartung planen …** (Nachricht, Beginn, Ende; Administratoren dürfen
   weiterarbeiten).
2. Auf dem Server (Übungsinstanz nur zeigen): `sudo /opt/estrichone/update.sh --pruefen`.
3. **Protokoll** → nach Bereich filtern und die eigenen Änderungen dieser Schulung finden.

> **Lösung:** Die geplante Wartung erscheint vorab als Banner und sperrt zum Beginn die Anmeldung. Das
> Protokoll zeigt alle Änderungen mit Person und IP-Adresse; Einträge lassen sich nicht ändern oder löschen.

### Typische Fragen

| Frage | Antwort |
|---|---|
| „Wo ist der Einrichtungscode für die Ersteinrichtung?“ | Im Protokoll des Servers beim ersten Start: `docker compose logs api`. |
| „Ein iPad wurde gestohlen.“ | **Geräte** → **Sperren**; Passwörter der Nutzer ändern. |
| „Die Startseite meldet keine Sicherung.“ | **Sicherung** prüfen, **Jetzt sichern**; sonst `docker compose logs backup`. |
| „Kommt 0.2 automatisch?“ | Nein. Neue Versionslinien werden nie automatisch eingespielt. |
| „Reicht ein Kombiserver im selben Raum?“ | Nein – er muss räumlich getrennt stehen; er ist kein zweiter Standort für das Archiv. |
| „Wie lange archivieren?“ | Vorschläge nach § 147 AO und § 257 HGB (6 bzw. 10 Jahre); maßgeblich ist die Steuerberatung. |

## Nach der Schulung

- Wissenstest gemeinsam auflösen (Lösungsblatt), offene Punkte notieren.
- **Teilnahmebestätigung** ausfüllen und unterschreiben.
- Kurzanleitungen austeilen bzw. aushängen: Kolonnen im Fahrzeug oder Lager (01, 02, 03, 05, 07), Büro (08–12),
  Administration (13–16).
- Übungsdaten aufräumen: Demo beenden; im Echtsystem Übungsakten stornieren, Übungstage nicht freigeben, Testbenutzer
  sperren.
- Nach zwei Wochen: kurze Nachschulung, Fragen aus dem Helpdesk (**Benutzer & Geräte** → **Helpdesk**) auswerten.
