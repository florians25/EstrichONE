# Planung

Die Planung beantwortet die Frage „Wer fährt wann wohin – mit welchem Gerät?“. Geplant wird je **Einsatz**: ein
Arbeitstag einer Kolonne (oder einzelner Mitarbeiter) auf einer Baustelle, mit Tätigkeit, Uhrzeit und Geräten.
EstrichONE bietet dafür mehrere Ansichten auf dieselben Daten:

| Ansicht | Wofür |
|---|---|
| **Disponenten-Cockpit** | Die Plantafel für die Disposition: Vorrat einplanen, ziehen, Konflikte lösen, Plan freigeben |
| **Kolonnenplanung** | Plantafel nach Kolonnen oder Mitarbeitern über Tag, Woche oder Monat, mit Wochen-PDF |
| **Teamplaner** | Zeitachse in Stunden je Kolonne bzw. Mitarbeiter, mit Terminen und Abwesenheiten |
| **Terminkalender** | Termine (Besprechungen, Kundentermine, Lieferungen) in gemeinsamen und eigenen Kalendern |
| **Arbeitsplan** | Liste „wer ist wann wo“ für 7 oder 14 Tage, mit Navigation und Druck |

## Disponenten-Cockpit {#cockpit}

**Planung** → **Disponenten-Cockpit** ist der Arbeitsplatz der Disposition.

![Disponenten-Cockpit](bilder/cockpit.png "Das Disponenten-Cockpit in der Wochenansicht")

1. **Vorrat** – Bauvorhaben, die noch eingeplant werden müssen (**Ungeplant**) bzw. alle offenen. Sortieren Sie nach
   Dringlichkeit und suchen Sie nach Name oder Nummer. Ziehen Sie eine Karte auf eine Kolonne und einen Tag, um sie
   einzuplanen – oder klicken Sie auf **Einplanen**.
2. **Konflikte** – Doppelbelegungen, abwesende Mitarbeiter, Geräte an zwei Orten oder überlastete Kolonnen. Die
   Schaltfläche zeigt die Anzahl; ein Klick öffnet die Liste.
3. **Plan freigeben** – macht die Planung für die Mitarbeiter verbindlich.
4. **Ansicht** – **Tag**, **Woche**, **2 Wochen** oder **Monat**; **Mo–Fr** blendet das Wochenende aus. Mit **Heute**
   und den Pfeilen wechseln Sie den Zeitraum.

Oben stehen die Kennzahlen **Ungeplant**, **Einsätze** und **Auslastung** (geplante gegen verfügbare Stunden). Jede
Kolonne zeigt ihre Leitung, die Mitglieder, das zugeordnete Fahrzeug und ihre Auslastung im Zeitraum. In den Tageszellen
stehen Arbeitsstunden und Hinweise, z. B. ein rotes Dreieck bei Konflikten oder „Peter: abwesend“.

### Planen mit der Maus

| Aktion | Wirkung |
|---|---|
| Klick | Schnellansicht mit Details und Aktionen |
| Doppelklick | Einsatz bearbeiten; in einer leeren Zelle: neuen Einsatz planen |
| Ziehen | Einsatz verschieben (Tag, Kolonne; in der Zeitachse auch die Uhrzeit) |
| <kbd>Strg</kbd> + Ziehen | Einsatz kopieren |
| Kante ziehen | Woche: auf weitere Tage ausdehnen · Zeitachse: Dauer ändern |
| <kbd>Strg</kbd> + Klick | Mehrfachauswahl (gemeinsam ziehen, verschieben, löschen) |
| Rechtsklick | Schnellaktionen für Einsatz bzw. Zelle (Tagesbesetzung, Kolonne tauschen …) |
| Vorrat ziehen | Bauvorhaben auf Kolonne und Tag einplanen |

Jede Änderung meldet EstrichONE kurz unten am Bildschirm – mit Hinweisen, falls ein Konflikt entsteht. Mit
<kbd>Strg</kbd> + <kbd>Z</kbd> nehmen Sie sie zurück, mit <kbd>Strg</kbd> + <kbd>Y</kbd> stellen Sie sie wieder her (auch
über die Pfeilsymbole oben).

### Planen mit der Tastatur

Alle Funktionen gehen auch ohne Maus. Die vollständige Übersicht öffnet <kbd>?</kbd> im Cockpit:

![Bedienung des Cockpits](bilder/cockpit-tastatur.png "Die eingebaute Hilfe zur Bedienung (Taste ?)")

Die wichtigsten Tasten: <kbd>Enter</kbd> bearbeitet den markierten Einsatz, <kbd>Leertaste</kbd> nimmt ihn in die Auswahl
auf, <kbd>Alt</kbd> + <kbd>←</kbd>/<kbd>→</kbd> verschiebt die Auswahl um einen Tag, <kbd>N</kbd> legt sie auf den nächsten
freien Tag, <kbd>Entf</kbd> entfernt sie, <kbd>T</kbd> springt zu heute, <kbd>W</kbd> blendet das Wochenende ein oder aus und
<kbd>Bild ↑</kbd>/<kbd>Bild ↓</kbd> blättert zum vorherigen bzw. nächsten Zeitraum. Eine Liste aller Kürzel steht in
Anhang A.

### Konflikte erkennen und lösen

![Konflikte im Zeitraum](bilder/cockpit-konflikte.png "Liste der Konflikte im angezeigten Zeitraum")

Die Liste **Konflikte im Zeitraum** nennt jeden Konflikt mit Kolonne und Tag – etwa einen Mitarbeiter, der krank
gemeldet oder im Urlaub ist, aber eingeplant wurde. Ein Klick auf das Warndreieck in einer Tageszelle zeigt die
Hinweise genau dieses Tages. Lösen Sie den Konflikt, indem Sie den Einsatz verschieben, die Tagesbesetzung ändern
(Rechtsklick auf die Zelle) oder die Kolonnen tauschen. Konflikte sind Hinweise – EstrichONE verhindert die Planung
nicht, damit Sie bewusst entscheiden können.

### Plan freigeben

Solange ein Tag nicht freigegeben ist, steht im Tageskopf **Entwurf**. Mitarbeiter sehen Entwürfe nicht als verbindlich.

1. Klicken Sie auf **Plan freigeben** (nach späteren Änderungen: **Änderungen freigeben (n)**).
2. Wählen Sie den **Zeitraum**: den sichtbaren Zeitraum ab heute, heute, morgen oder die nächste Woche.
3. Optional ein **Hinweis** an die Kolonnen, z. B. „Treffpunkt 6:30 Uhr am Lager“.
4. Unter **Änderungen seit der letzten Freigabe** sehen Sie, was sich je Tag geändert hat.
5. **Freigeben**.

![Plan freigeben](bilder/cockpit-freigabe.png "Dialog Plan freigeben mit Zeitraum und Hinweis an die Kolonnen")

Die Mitarbeiter sehen freigegebene Tage in der App unter **„Wer fährt wohin“** und erhalten eine Benachrichtigung.
Spätere Änderungen werden markiert, bis Sie erneut freigeben.

### Weitere Funktionen

- **Karte** zeigt die Baustellen des Zeitraums auf der Karte.
- **Drucken** erstellt den Plan als PDF (z. B. für den Aushang im Lager).
- **Filter** und **Im Plan suchen** grenzen Kolonnen, Baustellen und Tätigkeiten ein.
- **Kolonnen tauschen**, **Tagesbesetzung** und **Reihenfolge** ändern Sie über das Kontextmenü einer Zelle.

## Kolonnenplanung

**Planung** → **Kolonnenplanung** zeigt die Einsätze als Plantafel – wahlweise nach **Kolonnen** oder **Mitarbeitern**,
als **Tag**, **Woche** oder **Monat**. Oben stehen Abwesenheiten, darunter je Kolonne die Einsätze mit Uhrzeit, Art,
Baustelle, Tätigkeit, Gerät und Hinweisen (z. B. „Jonas Schmid ist im Urlaub“).

![Kolonnenplanung](bilder/kolonnenplanung.png "Die Kolonnenplanung in der Wochenansicht")

- **Einplanung** legt einen Einsatz an: Baustelle, Tag, Uhrzeit, Tätigkeit, Art (Estrich/Ausführung, Erstbegehung,
  Zweitbegehung, CM-Messung, Abnahme, Aufmaß, Kontrolle, Kundentermin, Lieferung, Sonstiges), Kolonne oder Mitarbeiter,
  Geräte und Notiz.
- Einsätze lassen sich wie im Cockpit per Ziehen verschieben.
- Die Filter oben grenzen nach Bauakte, Kolonne, Status und Terminart ein.
- **Wochen-PDF** druckt die Woche, **Baustellen-Vorrat** zeigt noch nicht eingeplante Bauvorhaben, **Cockpit** wechselt
  in das Disponenten-Cockpit.

## Teamplaner

Der **Teamplaner** zeigt Kolonnen bzw. Mitarbeiter auf einer **Zeitachse in Stunden** – ideal, um mehrere kurze Termine
an einem Tag zu planen (z. B. Erstbegehung um 8 Uhr, CM-Messung um 10:30 Uhr). Termine aus dem Kalender und
Abwesenheiten werden mit angezeigt; die Legende unten erklärt die Farben der Terminarten.

![Teamplaner](bilder/teamplaner.png "Der Teamplaner mit Stunden-Zeitachse je Kolonne")

Ansichten: **Tag (Stunden)**, **Woche**, **2 Wochen**, **Monat**. **Einplanung** legt einen Einsatz an, **Tages-PDF**
druckt den Tag. Ein Hinweis oben fasst Konflikte zusammen (**Anzeigen** listet sie auf). Einsätze ohne Kolonne stehen in
der Zeile **Einzelne Mitarbeiter**.

## Terminkalender

Der **Terminkalender** verwaltet Termine, die keine Baustelleneinsätze sind: Baubesprechungen, Kundentermine,
Lieferungen, Schulungen, Urlaub oder Privates.

![Terminkalender](bilder/kalender.png "Der Terminkalender in der Wochenansicht mit Kalenderliste")

- **Neuer Termin**: Titel, Ort, Beginn und Ende (oder ganztägig), Terminart, Teilnehmer, Kolonnen, Bezug zu einer
  Bauakte und optional eine **Wiederholung** (Serie). Beim Ändern einer Serie wählen Sie, ob nur dieser Termin, alle
  folgenden oder die ganze Serie betroffen ist.
- Links schalten Sie die Kalender ein und aus: **Mein Kalender**, gemeinsame Kalender wie **Firmenkalender** und
  **Kundentermine** sowie **Einsätze der Planung**. Mit **+** legen Sie weitere Kalender an.
- **Farben nach** **Kalender** oder **Terminart**.
- Ansichten **Tag**, **Woche**, **Mo–Fr**, **Monat** und **Agenda**. Termine lassen sich per Ziehen verschieben.

## Arbeitsplan

Der **Arbeitsplan** ist die Liste „wer ist wann wo“ für die nächsten **7 Tage** oder **14 Tage** – je Tag die
Abwesenheiten und alle Einsätze mit Zeit, Baustelle, Tätigkeit, Kolonne, Team und Geräten. **Navigation starten**
öffnet die Route zur Baustelle, **Nur meine Einsätze** zeigt nur Ihre eigenen. **PDF** und **Drucken** geben die Liste
aus.

![Arbeitsplan](bilder/arbeitsplan.png "Der Arbeitsplan der nächsten 7 Tage")

## Kolonnen

**Büro und Stammdaten** → **Kolonnen** listet die Kolonnen mit Kolonnenführer, Mitgliedern, **Auslastung (Woche)** und
Status. **Kolonne anlegen** bzw. **Bearbeiten**: Name, Farbe (erscheint in allen Plantafeln), Kolonnenführer, Mitglieder
und Reihenfolge. **Deaktivieren** blendet eine Kolonne aus der Planung aus, ohne ihre Einsätze zu löschen.

**Teams-Chat** (nur wenn Microsoft Teams eingerichtet ist): öffnet den Teams-Chat der Kolonne bzw. legt ihn an – mit
allen Mitgliedern der Kolonne, die ein Microsoft-Konto haben, und Ihnen. Ändert sich die Kolonne, passt EstrichONE die
Mitglieder im Chat automatisch an. Löschen (Recht `teams.delete`) über **…** → **Teams-Chat löschen …**. Ohne
App-Registrierung öffnet **Teams-Chat** einen Gruppenchat direkt in Teams (nicht gespeichert).

![Kolonnen](bilder/kolonnen.png "Kolonnen mit Kolonnenführer, Mitgliedern und Auslastung")

## Geräte und Fahrzeuge

**Büro und Stammdaten** → **Geräte & Fahrzeuge** verwaltet **Fahrzeuge** sowie **Pumpen & Maschinen** (z. B.
Estrichboy, Fließestrichpumpe, Glättmaschine) mit Kennzeichen, Typ, Führerscheinklasse, **TÜV**- und **Wartungstermin**,
zugeordneter Kolonne, Bild und Dokumenten (Fahrzeugschein, Prüfberichte). Geräte werden in den Einsätzen mit geplant;
EstrichONE erkennt, wenn ein Gerät an zwei Orten gleichzeitig eingeplant ist.

![Geräte und Fahrzeuge](bilder/geraete-fahrzeuge.png "Fahrzeuge mit Kennzeichen, TÜV, Wartung und Kolonne")

> **Tipp:** Abwesenheiten (Urlaub, Krankheit, Schule) pflegen Sie im Personalbereich (Kapitel 6). Die Planung
> berücksichtigt sie automatisch und zeigt sie als Hinweis.
