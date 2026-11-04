# Bauakten

Die **Bauakte** ist das Herzstück von EstrichONE. Sie bündelt alles zu einem Bauvorhaben: Kunde und Beteiligte,
Dokumente, Fotos und Videos, Bautagebuch, Erstbegehung, Abnahme, Räume, Aufgaben, Material, Einsätze und den gesamten
Verlauf. Büro, Bauleitung und Kolonnen arbeiten an derselben Akte – im Web wie in den Apps.

## Der Lebenslauf einer Bauakte

Jede Bauakte hat einen **Status**. Er steuert, wo die Akte erscheint, und wird teils automatisch gesetzt:

| Status | Bedeutung |
|---|---|
| **Anfrage** | Ein Interessent hat angefragt; noch kein Auftrag. Mit **Als Auftrag übernehmen** wird daraus eine Bauakte. |
| **Geplant** | Auftrag erteilt, Einsätze werden geplant. |
| **In Ausführung** | Die Kolonne arbeitet auf der Baustelle. Auf Wunsch automatisch am ersten Einsatztag oder bei der ersten Aktivität vor Ort (Foto, Bautagebuch, Erstbegehung). |
| **Belegreif / Trocknung** | Der Estrich liegt und trocknet bis zur Belegreife. |
| **Abgeschlossen** | Arbeiten beendet und abgenommen. Auf Wunsch automatisch nach der Abnahme. |
| **Storniert** | Der Auftrag ist entfallen. Mit **Storno aufheben** lässt er sich wiederherstellen. |

Abgeschlossene und stornierte Bauakten heißen **Altakten**. Sie bleiben vollständig erhalten und sind über die Suche und
**Auftragsbearbeitung** → **Altakten** jederzeit erreichbar. Wie der Status automatisch wechselt, legt die Administration
unter **System** → **Systemeinstellungen** → **Bauakten** fest.

## Neue Bauakte anlegen {#bauakte-anlegen}

Eine Bauakte entsteht immer mit **Kunde** – ohne Kunde keine Bauakte. Der Assistent führt in drei Schritten durch die
Anlage und verhindert doppelte Kunden.

1. Klicken Sie oben auf **Neue Bauakte** (oder in der Bauaktenliste auf **Neue Bauakte** bzw. **Neue Anfrage**).
2. **Kunde:** Geben Sie den Namen ein. Schon beim Tippen schlägt EstrichONE vorhandene Kunden vor; ein Klick übernimmt
   den Kunden mit allen Angaben. Für einen neuen Kunden wählen Sie **Person** oder **Firma** und ergänzen Anschrift,
   Telefon und E-Mail.
3. Klicken Sie auf **Weiter**. Der Server prüft jetzt auf **Dubletten** – nach Name, Telefon, E-Mail und Anschrift.

![Assistent Schritt Kunde](bilder/bauakte-neu-kunde.png "Neue Bauakte, Schritt 1: Kunde mit Anschrift und Telefon")

Findet EstrichONE ähnliche Kunden, erscheint der Hinweis „Es gibt bereits ähnliche Kunden“ mit der Liste
**Mögliche Dubletten**. Wählen Sie den vorhandenen Kunden und **Diesen Kunden verwenden** – oder setzen Sie bewusst das
Häkchen **Trotzdem als neuen Kunden anlegen**.

![Dublettenprüfung](bilder/bauakte-neu-dublette.png "Dublettenprüfung: Telefonnummer und Anschrift passen zu „Familie Oberländer“")

4. **Bauvorhaben:** Geben Sie eine aussagekräftige Bezeichnung ein, z. B. „EFH Müller, Neubau“. Unter **Anlegen als**
   wählen Sie **Bauakte (Auftrag)** oder **Anfrage**. Eine **Vorlage** legt Ordner, Räume und Kennwerte gleich mit an.
   **Adresse des Kunden übernehmen** füllt die Baustellenanschrift, wenn sie mit der Kundenanschrift übereinstimmt.
5. Ergänzen Sie, falls angeboten, die **Kennwerte** (Objektart, Estrichart, Fläche, Dicke, Fußbodenheizung …).
6. **Prüfen:** Kontrollieren Sie die Zusammenfassung und klicken Sie auf **Anlegen**.

![Assistent Schritt Bauvorhaben](bilder/bauakte-neu-bauvorhaben.png "Schritt 2: Bauvorhaben, Art der Anlage, Vorlage und Baustelle")

> **Tipp:** Sind Sie gerade beim Kunden vor Ort? Setzen Sie im Schritt Bauvorhaben das Häkchen **Direkt im Anschluss
> die Erstbegehung durchführen** – dann öffnet sich nach dem Anlegen sofort das Erstbegehungsprotokoll.

Die **Aktennummer** (z. B. 2026-001) vergibt EstrichONE automatisch nach dem eingestellten Nummernkreis.

## Bauaktenliste

**Auftragsbearbeitung** → **Bauakten** zeigt alle laufenden Bauakten. Die Reiter filtern nach Status (**Laufend**,
**Anfrage**, **Geplant**, **In Ausführung**, **Belegreif / Trocknung**, **Altakten**). Darüber suchen Sie nach Nummer,
Bauvorhaben und Adresse und filtern nach **Zuständig** und **Kunde**. **Filter zurücksetzen** hebt alle Filter auf.

![Bauaktenliste](bilder/bauakten-liste.png "Die Bauaktenliste mit Statusreitern, Suche und Filtern")

Weitere Möglichkeiten in der Kopfzeile der Liste:

- **Karte** – zeigt die Bauakten mit bekanntem Standort auf der Baustellenkarte.
- **Exportieren** – lädt die geladenen Bauakten als CSV-Datei für Excel herunter.
- **Neue Anfrage** / **Neue Bauakte** – startet den Assistenten.

Ein Klick auf eine Zeile öffnet die Bauakte rechts daneben. Über die Symbole oben rechts in der Akte schalten Sie auf
Vollbild oder schließen die Akte wieder.

## Die Bauakte im Überblick

![Bauakte, Übersicht](bilder/bauakte-uebersicht.png "Kopf der Bauakte mit Status, Kennwerten, Hinweisen und Kennzahlen")

Der **Kopf** der Bauakte zeigt Nummer, Bauvorhaben und Status. Darunter stehen farbige Marken mit den wichtigsten
Kennwerten (z. B. Objektart, Priorität, Estrichart, Dicke, Fußbodenheizung) und Hinweise, die Aufmerksamkeit brauchen:
überfällige Aufgaben, fehlende Erstbegehung, offene Mängel, ausstehende Freigaben, der heutige Einsatz und die nächste
Lieferung. Die Kennzahlenleiste nennt Kunde, Baustelle, Zuständige, nächsten Einsatz, Anlagedatum, offene Punkte,
Fläche und die Zahl der Fotos und Dokumente.

Die Schaltflächen im Kopf:

- **Status** – ändert den Status von Hand.
- **Als Auftrag übernehmen** (bei Anfragen) bzw. **Abschließen** (bei laufenden Bauakten). Ist ein Archivserver
  eingerichtet, bietet der Abschlussdialog zusätzlich **Abschließen & Archivieren** an: Die Bauakte wird abgeschlossen
  und alle ihre Dokumente werden sofort revisionssicher archiviert (Fortschritt im Dialog, Nachweis unter
  **Aktionen** → **Archivnachweis**). Ist der Archivserver gerade nicht erreichbar, ist die Schaltfläche ausgegraut –
  dann nur abschließen und später archivieren.
- **Mir zuweisen** – trägt Sie als Zuständige(n) ein.
- **Baustellen-Chat anlegen** – Gruppenchat mit allen Beteiligten der Baustelle (Abschnitt 8.2).
- **Bearbeiten** – Bezeichnung, Baustelle, Termine, Beschreibung.
- **Aktionen** – alle Ausdrucke und weitere Funktionen:

![Menü Aktionen](bilder/bauakte-aktionen.png "Das Menü Aktionen der Bauakte"){halb}

| Aktion | Wirkung |
|---|---|
| **Letzte Aktivitäten …** | Wer hat zuletzt was in dieser Akte getan |
| **Abnahmeprotokoll erstellen …** | Startet die digitale Abnahme (Abschnitt 3.10) |
| **Erstbegehungsprotokoll (PDF)** | Protokoll der letzten Erstbegehung |
| **Schreiben an den Kunden …** | Brief nach DIN 5008 mit Firmenkopf, als PDF in der Akte abgelegt |
| **Archivnachweis …** | Prüfbericht, welche Dokumente revisionssicher archiviert sind |
| **Bauakte als PDF (mit Fotos)** | Die komplette Akte als PDF |
| **Baustellenbericht drucken** / **Abnahmeprotokoll drucken** / **Bauaktenblatt drucken** | Ausdrucke für Mappe und Kolonne |
| **Route planen** | Öffnet die Navigation zur Baustelle |
| **E-Mail an Baustellenkontakt** | Öffnet Ihr E-Mail-Programm |
| **Bauakte stornieren** / **In den Papierkorb** | Storno bzw. Löschen (nur mit Recht „Bauakten löschen“) |

Alle Ausdrucke erstellt der Server als PDF mit Ihrem Firmenlogo; sie werden zusätzlich automatisch in der Bauakte
abgelegt.

Die Bauakte ist in **Abschnitte** gegliedert, die Sie über die Reiter unter dem Kopf erreichen. Mit dem kleinen Pfeil in
der Mitte klappen Sie den Kopf ein, damit mehr Platz für den Inhalt bleibt.

**Übersicht** fasst Basisdaten, Baustellendaten, Termine, den Stand der Dokumentation, offene Aufgaben, die nächsten
Einsätze, neueste Dokumente, Freigaben, angeheftete Notizen, den Standort auf der Karte und die neuesten Bilder
zusammen. **Kennwerte bearbeiten** ändert Objektart, Estrichart, Fläche, Dicke, Dämmung, Heizung, Zufahrt usw. Fehlt der
Standort, klicken Sie in die Karte und speichern ihn.

## Kontakte und Beteiligte

Im Abschnitt **Kontakte** stehen der **Kunde (Auftraggeber)**, der **Ansprechpartner vor Ort**, die **Bauleitung** und
weitere Beteiligte wie Architekt, Planer, Generalunternehmer, Lieferanten und Subunternehmer – mit Telefon und E-Mail
griffbereit für Büro und Baustelle.

![Kontakte der Bauakte](bilder/bauakte-kontakte.png "Kunde, Baustelle und Bauleitung im Abschnitt Kontakte")

- **Kontakt hinzufügen** → **Bestehenden Kontakt suchen …** übernimmt einen Kontakt aus dem Kundenstamm,
  **Neuen Kontakt erfassen …** legt ihn neu an.
- Über das Menü einer Karte ändern Sie die **Rolle**, machen den Kontakt zum **Hauptkontakt**, öffnen ihn **im
  Kundenstamm** oder entfernen ihn aus der Bauakte.
- Unter **Team im Betrieb** weisen Sie Mitarbeiter zu (**Mitarbeiter zuweisen**). Zugewiesene sehen die Bauakte, auch
  wenn sie nur das Recht „Eigene Bauakten sehen“ haben.

## Dokumente

Der Abschnitt **Dokumente** ist die Ablage der Bauakte. Links steht der Ordnerbaum **Kunde → Bauakte → Dokumentart**
(Angebote, Aufträge, Pläne, Protokolle, Abnahmen, Rechnungen, Lieferscheine, Kundenschreiben, Büro, Sonstiges), rechts die
Dateien des gewählten Ordners.

![Dokumente der Bauakte](bilder/bauakte-dokumente.png "Ordnerbaum und Dokumentliste mit Fassungen und Freigabestand")

### Hochladen

Klicken Sie auf **Hochladen** oder ziehen Sie eine oder mehrere Dateien aus dem Explorer in die Liste. EstrichONE
überträgt große Dateien in Teilen von 4 MB; bricht die Verbindung ab, wird der Upload beim nächsten Versuch fortgesetzt.
Im Dialog wählen Sie die **Dokumentart**, auf Wunsch einen **Unterordner** (z. B. „Pläne/EG“) und ergänzen eine
**Beschreibung**.

### Fassungen

Lädt jemand eine überarbeitete Datei hoch, entsteht keine Kopie, sondern eine neue **Fassung** (v1, v2 …): Menü der
Zeile → **Neue Fassung hochladen …**. Die Liste zeigt immer die aktuelle Fassung; **Alle Fassungen** blendet frühere ein.
Frühere Fassungen bleiben unverändert erhalten.

### Weitere Aktionen

- **Vorschau** – zeigt PDFs und Bilder direkt im Browser, mit den Pfeiltasten blättern Sie zur nächsten Datei.
- **Auswahl herunterladen** – mehrere markierte Dateien als ZIP.
- **Verschieben** – in einen anderen Ordner oder eine andere Dokumentart.
- **Umbenennen …**, **Löschen** (nur mit Recht „Fotos, Videos und Dokumente löschen“).
- **Archivnachweis …** – zeigt, ob und wann das Dokument revisionssicher archiviert wurde.
- **Neuer Unterordner** – eigene Ordner innerhalb einer Dokumentart.

### Freigaben (Genehmigungen) {#freigaben}

Pläne, Angebote oder Protokolle lassen sich von Kollegen **freigeben**, bevor sie weitergegeben oder ausgeführt werden:

1. Menü der Zeile → **Freigabe anfordern**.
2. Wählen Sie eine Person oder eine Rolle, eine Frist und schreiben Sie einen kurzen Hinweis.
3. Die angefragte Person erhält eine Benachrichtigung und sieht das Dokument in der Card **Warten auf Genehmigung**.
4. Sie öffnet das Dokument, prüft es und klickt auf **Freigeben** oder **Ablehnen** – auf Wunsch mit Kommentar.

Bis zur Entscheidung zeigt das Dokument die Marke **Wartet auf Freigabe**. Eine Anfrage lässt sich mit **Zurückziehen**
zurücknehmen. Alle Freigaben über alle Bauakten finden Sie unter **Auftragsbearbeitung** → **Dokumente** im Reiter
**Freigaben**.

![Freigaben](bilder/freigaben.png "Zentrale Übersicht der Freigaben: Warten auf meine Freigabe und Von mir angefragt")

### Zentrale Dokumentverwaltung

**Auftragsbearbeitung** → **Dokumente** zeigt die Ablage **aller** Bauakten in einem Baum nach Kunde und Bauakte. Hier
suchen Sie nach Dateiname, Beschreibung oder Bauakte und filtern nach Freigabestand (**Wartet auf Freigabe**,
**Freigegeben**, **Abgelehnt**, **Ohne Freigabe**).

![Zentrale Dokumente](bilder/dokumente.png "Die zentrale Dokumentverwaltung über alle Bauakten")

## Fotos und Videos

Fotos und Videos der Baustelle kommen meist aus der App – mit Aufnahmezeit und Standort. Im Web laden Sie sie mit
**Hochladen** oder per Ziehen hoch.

![Fotos und Videos](bilder/bauakte-fotos.png "Fotos der Bauakte im Zeitstrahl, nach Tagen gruppiert")

- **Zeitstrahl** gruppiert die Bilder nach Aufnahmetag, **Raster** zeigt sie als Kacheln.
- Filtern Sie nach **Beschriftung**, **Raum** und **Bezug** (z. B. zu einer Aufgabe oder einem Mangel).
- Ein Klick öffnet die **Großansicht**; mit <kbd>←</kbd> und <kbd>→</kbd> blättern Sie.
- **Auswählen** markiert mehrere Bilder für **Herunterladen (ZIP)**, **Zuordnen …** (zu Raum, Aufgabe oder Mangel) und
  **Löschen**.

> **Hinweis:** EstrichONE speichert Fotos platzsparend (JPEG neu kodiert, nach Bedarf verkleinert), Aufnahmezeit und
> GPS bleiben erhalten. Die Einstellungen dazu stehen unter **Systemeinstellungen** → **Speicher** (Abschnitt 9.6).

## Bautagebuch

Im Abschnitt **Dokumentation** → **Bautagebuch** steht die **Tageschronik** der Baustelle: je Tag Wetter, Temperatur,
Arbeitszeit, Kolonne, Zahl der Anwesenden, was gemacht wurde, und die Fotos des Tages. Die Kolonnen erfassen ihre
Tagesberichte meist in der App; im Web ergänzen oder korrigieren Sie sie.

![Bautagebuch](bilder/bauakte-dokumentation.png "Die Tageschronik im Bautagebuch")

1. Klicken Sie auf **Tagesbericht erfassen**.
2. Füllen Sie **Tag** und **Was wurde gemacht?** aus (Pflicht). Ergänzen Sie Wetter, Temperatur, Arbeitsbeginn und
   -ende, Kolonne und Anwesende.
3. Unter **Fotos des Tages** verknüpfen Sie Fotos der Bauakte.
4. **Speichern** (oder <kbd>Strg</kbd> + <kbd>Enter</kbd>).

![Tagesbericht erfassen](bilder/bautagebuch-dialog.png "Dialog Tagesbericht erfassen")

Mit dem Stift bearbeiten, mit dem Papierkorb löschen Sie einen Bericht. Die Suche über der Chronik findet Texte,
Kolonnen und Daten.

## Erstbegehung

Bei der **Erstbegehung** prüft die Bauleitung vor Beginn, ob die Baustelle bereit ist. Das Protokoll ist ein Nachweis:
Es wird nicht überschrieben, sondern bei Bedarf **korrigiert** (eine neue Prüfung mit Verweis auf die alte).

1. Öffnen Sie **Dokumentation** → **Prüfungen & Protokolle** und klicken Sie auf **Erstbegehung erfassen**.
2. **Begehung:** Zeitpunkt und freie Notizen (Gespräch mit dem Kunden, Zufahrt, Besonderheiten). Ist keine Begehung
   nötig, schalten Sie **Erstbegehung nicht notwendig** ein und nennen den Grund.
3. **Prüfpunkte:** Je Punkt **In Ordnung**, **Mangel** oder **Entfällt**, auf Wunsch mit Bemerkung – z. B. „Untergrund
   tragfähig, eben und sauber“, „Randdämmstreifen vorhanden“, „Fußbodenheizung abgedrückt“, „Zufahrt und
   Pumpenstandort“, „Wasser und Strom vorhanden“. Dazu kommen die Messwerte und Felder aus Ihrem Formular.
4. **Fotos:** Fotos der Bauakte auswählen.
5. **Mängel:** Für jeden Prüfpunkt mit Mangel kann direkt ein Mangel in **Aufgaben & Mängel** entstehen.
6. **Speichern**. Das Protokoll steht anschließend als PDF bereit.

![Erstbegehung erfassen](bilder/erstbegehung-dialog.png "Der Assistent Erstbegehung erfassen")

![Erstbegehungsprotokoll](bilder/bauakte-pruefungen.png "Ergebnis der Erstbegehung mit Prüfpunkten und Bemerkungen")

Welche Prüfpunkte und Felder erscheinen, legt die Administration unter **IT Center** → **Stammdaten** → **Formulare**
→ **Erstbegehung** fest. Im selben Abschnitt erfassen Sie **CM-Messungen** (Messstelle, Estrich, Dicke, CM-Wert,
Ergebnis) mit **CM-Messung erfassen**.

## Abnahme mit Unterschrift {#abnahme}

Das digitale **Abnahmeprotokoll** wird vor Ort auf dem Tablet oder im Büro ausgefüllt und von beiden Seiten
unterschrieben.

1. **Dokumentation** → **Abnahmen** → **Abnahmeprotokoll erstellen** (oder **Aktionen** → **Abnahmeprotokoll erstellen …**).
2. **Termin:** Datum und Uhrzeit, Ort, Name des Auftraggebers und des Auftragnehmers.
3. **Leistung:** Umfang der Leistung und die abgenommenen Räume.
4. **Ergebnis:** **Abgenommen**, **Abgenommen mit Vorbehalt** oder **Abnahme verweigert**; festgestellte Mängel mit Frist werden
   direkt als Mängel der Bauakte angelegt.
5. **Fotos** und **Notizen** ergänzen.
6. **Unterschriften:** Auftraggeber und Auftragnehmer unterschreiben mit Finger, Stift oder Maus im jeweiligen Feld.
   **Löschen** setzt ein Feld zurück.
7. **Abnahme speichern**.

![Abnahmeprotokoll](bilder/abnahme-dialog.png "Abnahmeprotokoll, Schritt Termin")

![Unterschriften](bilder/abnahme-unterschrift.png "Schritt Unterschriften: Auftraggeber und Auftragnehmer unterschreiben")

Ist der Auftraggeber nicht vor Ort, setzen Sie das Häkchen **Auftraggeber nicht vor Ort: Freigabe per Link an den
Kunden**. Der Kunde erhält einen Link (auf Wunsch per E-Mail) und bestätigt die Abnahme auf einer eigenen Seite – ohne
Konto in EstrichONE. Wie lange der Link gilt, stellt die Administration ein.

Das unterschriebene Protokoll legt EstrichONE als PDF unter der Dokumentart **Abnahmen** ab. Ist die Automatik
eingeschaltet, wechselt die Bauakte danach auf **Abgeschlossen**.

![Abnahmen](bilder/bauakte-abnahmen.png "Abgeschlossene Abnahme mit PDF in einer Altakte")

## Aufgaben und Mängel

Im Abschnitt **Aufgaben & Mängel** halten Sie fest, was zu tun ist. Eine **Aufgabe** ist ein To-do (z. B. „CM-Messung
abstimmen“), ein **Mangel** eine Beanstandung (z. B. „Haarriss an der Türschwelle“).

![Aufgaben und Mängel](bilder/bauakte-aufgaben.png "Aufgaben und Mängel mit Fälligkeit, Zuständigen und Status")

- Neu: Art wählen (**Aufgabe** oder **Mangel**), in **Was ist zu tun?** schreiben, **Fällig am** und Zuständigen wählen,
  **Hinzufügen**.
- Status: **Offen**, **In Arbeit**, **Erledigt**. Überfällige Einträge sind rot markiert.
- Filter: **Offen**, **Mängel**, **Überfällig**, **Alle**.
- Fotos lassen sich einem Eintrag zuordnen (Abschnitt Fotos → **Zuordnen …**), z. B. das Foto eines Mangels.

Ihre eigenen offenen Aufgaben aus allen Bauakten sehen Sie in der Card **Meine Aufgaben** und in der Tagesübersicht.

## Räume und Flächen

**Räume & Flächen** listet die Räume mit Geschoss, Fläche, Estrich, Dicke, Dämmung, Heizung und Status (**Geplant**,
**Eingebracht**, **Belegreif**). Oben stehen die Summen: Fläche gesamt, davon beheizt, Zahl der Räume und das
**Estrichvolumen** (Fläche × Dicke, netto).

![Räume und Flächen](bilder/bauakte-raeume.png "Räume mit Flächen, Aufbau und Status")

**Raum hinzufügen** legt einen Raum an; mit **Bereiche** gliedern Sie größere Objekte (z. B. Haus A, Haus B). Die Räume
sind Grundlage für den **Materialbedarf** und die Abnahme.

## Material und Einsätze

Der Abschnitt **Material** berechnet aus Räumen und Flächen den **Materialbedarf** (Volumen = Fläche × Estrichdicke,
aufgerundet auf 0,5 m³; Dämmung auf volle m²) mit einstellbarer **Reserve** in Prozent. **Auswahl bestellen** übernimmt
Positionen direkt in eine Bestellung; darunter stehen die **Bestellungen für diese Baustelle** (Kapitel 7).

Unter **Einsätze** sehen Sie alle geplanten Einsätze der Baustelle mit Tag, Zeit, Tätigkeit, Kolonne und Geräten.
**Einsatz planen** legt einen neuen Einsatz an, **Zur Plantafel** springt in die Planung (Kapitel 5).

## Notizen und Verlauf

**Notizen & Verlauf** vereint freie Notizen und die lückenlose Historie der Akte: Wer hat wann was geändert – vom Anlegen
über Statuswechsel bis zu jedem hochgeladenen Dokument.

![Notizen und Verlauf](bilder/bauakte-verlauf.png "Notizen und Verlauf mit Systemänderungen")

- **Neue Notiz**: Text schreiben, optional **Textbaustein einfügen**, einfärben (Gelb, Rot, Grün, Blau) und
  **Anheften** – angeheftete Notizen erscheinen auch in der Übersicht. <kbd>Strg</kbd> + <kbd>Enter</kbd> speichert.
- **Mit Systemänderungen** zeigt den vollständigen Verlauf, **Nur Notizen** blendet die Systemeinträge aus.

## Tagesübersicht und Baustellenkarte

**Auftragsbearbeitung** → **Tagesübersicht** zeigt, was heute ansteht: Einsätze aller Kolonnen mit Baustelle,
Tätigkeit und Team, fällige Wiedervorlagen, Freigaben für Sie, Ihre Aufgaben und neue Tickets. Die Kennzahlen oben
(Einsätze heute, offene Aufgaben, Freigaben, Wiedervorlagen, offene Anfragen, neue Tickets) springen direkt in die Liste.

![Tagesübersicht](bilder/tagesuebersicht.png "Die Tagesübersicht für den heutigen Tag")

Die **Baustellenkarte** zeigt alle Bauakten mit Standort auf einer Karte (OpenStreetMap). Filtern Sie nach Status,
suchen Sie nach Nummer, Bauvorhaben oder Ort und blenden Sie bei Bedarf **auch abgeschlossene** Baustellen ein. **Als
Liste** wechselt zurück zur Bauaktenliste.

## Altakten

Abgeschlossene und stornierte Bauakten finden Sie unter **Auftragsbearbeitung** → **Altakten** bzw. im Reiter
**Altakten** der Bauaktenliste. Sie lassen sich weiter öffnen, durchsuchen und drucken. Werden Bauakten nach einer
Frist komplett auf den Archivserver ausgelagert, erscheinen sie im **Aktenarchiv** (Abschnitt 9.10).

![Altakten](bilder/altakten.png "Altakten: abgeschlossene und stornierte Bauakten")
