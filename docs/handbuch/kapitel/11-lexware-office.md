# Lexware Office

Mit dem Modul **Lexware Office** arbeiten EstrichONE und Lexware Office (früher lexoffice) zusammen. Die Aufgaben sind
klar getrennt:

- **Verkauf:** Angebote, Auftragsbestätigungen, Rechnungen (auch Abschlags- und Schlussrechnungen) und Gutschriften
  erfassen Sie mit dem Modul „Verkauf“ in EstrichONE, passend zur Bauakte, und senden sie an Lexware Office. Lexware
  Office bleibt Buchhaltung, Nummernkreis und Belegarchiv; dort versenden und buchen Sie die Belege (siehe „Verkauf“).
- **Artikel und Leistungen** gleicht EstrichONE mit den Artikeln in Lexware Office ab (Modul „Lexware Office: Artikel“).
- **Status der Bauakte** folgt auf Wunsch dem Angebots- und Rechnungsstatus (Modul „Lexware Office: Status der Bauakte
  automatisch“).
- **EstrichONE zeigt die Belege an**: am Kunden alle Rechnungen und Angebote, in der Bauakte die zugeordneten, jeweils
  mit Nummer, Datum, Betrag und Status. Abgeschlossene Belege öffnen Sie direkt als PDF; ein Klick führt zum Beleg in
  Lexware Office.
- **Kunden werden abgeglichen**: Sie verknüpfen Kunden mit ihren Kontakten in Lexware Office oder legen sie dort an,
  immer mit Prüfung auf doppelte Einträge. Spätere Änderungen an Name, Anschrift, E-Mail-Adresse und Telefon gleicht
  EstrichONE zwischen beiden Systemen ab.

Voraussetzung ist ein Tarif von Lexware Office mit Public API (im Tarif XL enthalten).

## Einrichten

1. Erzeugen Sie in Lexware Office unter **Einstellungen** → **Erweiterungen** → **Public API** einen API-Schlüssel
   (direkt: app.lexware.de/addons/public-api).
2. Öffnen Sie in EstrichONE **Verwaltung** → **Integrationen** → **Lexware Office**, fügen Sie den Schlüssel ein und
   wählen Sie **Verbindung testen**. Bei Erfolg zeigt EstrichONE den Firmennamen Ihrer Organisation in Lexware Office.
3. Schalten Sie **Lexware Office verwenden** ein. Derselbe Schalter steht unter **Systemeinstellungen** → **Module**.
4. Geben Sie den zuständigen Rollen unter **Rollen und Rechte** die Rechte der Gruppe **Rechnungen**.

Der Schlüssel wird verschlüsselt gespeichert und nie wieder angezeigt. Die **Basis-URL** ändern Sie nur, wenn Lexware
eine andere Adresse vorgibt. Unter **Kunden** legen Sie fest, ob neue Kunden abgeglichen werden und in welche Richtung
spätere Änderungen laufen (siehe „Spätere Änderungen abgleichen“).

![Lexware Office einrichten](bilder/lexware-einstellungen.png "Verwaltung, Integrationen, Lexware Office")

| Recht | Erlaubt |
|---|---|
| Rechnungen und Angebote sehen | Belege am Kunden und in der Bauakte ansehen |
| Belege zuordnen und Kunden verknüpfen | Belege Bauakten zuordnen, Kunden verknüpfen, anlegen und übernehmen, Abweichungen entscheiden |
| Integrationen einrichten | Schlüssel, Vorgaben und den Kundenabgleich in der Verwaltung |

## Welche Kunden abgeglichen werden

Nicht jeder Kunde muss nach Lexware Office. Jeder Kunde hat den Schalter **Mit Lexware Office abgleichen**:

- **Ein**: Der Kunde darf mit Lexware Office verknüpft oder dort angelegt werden.
- **Aus**: Der Kunde bleibt nur in EstrichONE. Er wird weder verknüpft noch angelegt und erscheint im Kundenabgleich
  unter **Bewusst nur in EstrichONE**, nicht als fehlend.

Der Schalter steht im Dialog **Neuer Kunde**, in **Kunde bearbeiten** und in der Kundenansicht im Abschnitt **Lexware
Office**, außerdem in den Apps (iPhone, iPad, Android) im Kunden-Formular. Er ist nur sichtbar, solange das Modul
eingeschaltet ist.

Die Vorgabe für neue Kunden legen Sie unter **Verwaltung** → **Integrationen** → **Lexware Office** mit **Neue Kunden
standardmäßig mit Lexware Office abgleichen** fest. Kunden, die es vor dem Einschalten des Moduls schon gab, stehen auf
**Aus**, damit nichts ungefragt übertragen wird. Im Kundenabgleich schalten Sie den Abgleich für ausgewählte Kunden
gesammelt ein. Vorher sehen Sie, wie viele dieser Kunden dadurch in Lexware Office neu angelegt würden.

## Neue Kunden anlegen

### Warnung, wenn es den Kunden in Lexware Office schon gibt

Während Sie im Dialog **Neuer Kunde** Name, Anschrift oder E-Mail-Adresse eingeben, sucht EstrichONE in Lexware Office
nach passenden Kontakten. Gibt es Treffer, erscheint die Warnung **Achtung: Dieser Kunde ist bereits in Lexware Office
vorhanden.** mit Name, Kundennummer, Anschrift und dem Grund der Übereinstimmung. Die Warnung kommt auch, wenn der
Schalter **Mit Lexware Office abgleichen** aus ist.

- **Aus Lexware Office übernehmen** legt den Kunden mit den Daten aus Lexware Office an, verknüpft ihn und schaltet den
  Abgleich ein. Dafür benötigen Sie das Recht, Kunden zu verknüpfen.
- **Trotzdem neu anlegen** legt den Kunden bewusst ohne Verknüpfung an. Er erscheint danach im Kundenabgleich unter
  **Mögliche Dubletten**.
- Ist der Kontakt schon mit einem anderen Kunden in EstrichONE verknüpft, führt ein Link zu diesem Kunden.

![Warnung beim Anlegen eines Kunden](bilder/lexware-neuer-kunde.png "Neuer Kunde: Der Kunde ist bereits in Lexware Office vorhanden")

### Prüfung beim Klick auf Anlegen

Maßgeblich ist die Prüfung beim Klick auf **Kunde anlegen**: EstrichONE fragt erst Lexware Office mit den endgültigen
Angaben und legt den Kunden erst danach an. Solange steht im Dialog **Prüfung in Lexware Office läuft** und die
Schaltflächen sind gesperrt.

- **Kein Treffer:** Der Kunde wird angelegt.
- **Treffer:** Der Kunde wird nicht angelegt. Der Dialog zeigt die Treffer; Sie übernehmen den Kunden aus Lexware Office
  oder wählen **Trotzdem neu anlegen** und klicken erneut.
- **Prüfung nicht möglich** (Lexware Office nicht erreichbar): Der Kunde wird nicht stillschweigend angelegt. EstrichONE
  fragt nach; mit **Ohne Prüfung anlegen** legen Sie ihn bewusst an.

Das gilt an jeder Stelle, an der Sie im Web einen Kunden neu anlegen, auch im Assistenten für neue Bauakten und bei den
Beteiligten einer Bauakte. Außerhalb des Dialogs **Neuer Kunde** erscheinen die Treffer in einer Rückfrage.

![Prüfung in Lexware Office nicht möglich](bilder/lexware-pruefung-nicht-moeglich.png "Rückfrage, wenn die Prüfung in Lexware Office nicht möglich ist")

### Automatisch in Lexware Office anlegen

Legen Sie einen Kunden mit eingeschaltetem **Mit Lexware Office abgleichen** an und gibt es in Lexware Office keinen
ähnlichen Kontakt, legt EstrichONE ihn dort kurz darauf im Hintergrund an und verknüpft ihn. Das gilt für Web und
Apps, auch für Kunden, die in der App ohne Verbindung angelegt wurden, und für Kunden, bei denen Sie den Schalter später
einschalten.

- Gibt es einen ähnlichen Kontakt, legt EstrichONE nichts an. Der Kunde steht im Kundenabgleich unter **Mögliche
  Dubletten**, dort verknüpfen Sie ihn oder legen ihn bewusst neu an.
- Mit ausgeschaltetem Schalter wird nichts angelegt.
- Kunden, die es beim Update schon gab, werden nicht nachträglich angelegt.
- Der Abschnitt **Lexware Office** am Kunden nennt den Stand: **Anlage ausstehend**, angelegt mit Datum oder **Anlage
  fehlgeschlagen** mit Grund und **Erneut versuchen**. Vor jedem Versuch prüft EstrichONE erneut auf ähnliche Kontakte,
  damit nichts doppelt entsteht.

Die automatische Anlage schalten Sie unter **Verwaltung** → **Integrationen** → **Lexware Office** mit **Neue Kunden ohne
Treffer automatisch in Lexware Office anlegen** aus. Dann legen Sie Kunden wie bisher einzeln an.

Schalten Sie den Abgleich bei einem bereits verknüpften Kunden aus, bleibt die Verknüpfung bestehen, bis Sie sie lösen.
Der Kontakt und seine Belege liegen ja bereits in Lexware Office und werden weiter angezeigt; die Kundenansicht weist
darauf hin. Änderungen werden für diesen Kunden dann nicht mehr abgeglichen.

## Kunden verknüpfen

Jeder Kunde, dessen Belege Sie in EstrichONE sehen möchten, wird einmal mit seinem Kontakt in Lexware Office verknüpft. Ein Kunde gehört zu
genau einem Lexware-Kontakt und umgekehrt.

Öffnen Sie den Kunden und dort den Abschnitt **Lexware Office**. **Kontakt suchen und verknüpfen** prüft zuerst, ob es
den Kunden in Lexware Office schon gibt. Die Prüfung vergleicht Name (ohne Rücksicht auf Schreibweise, Rechtsform und
Anrede), E-Mail-Adresse, Telefonnummer, Anschrift, Kundennummer und USt-ID und nennt zu jedem Treffer den Grund, zum
Beispiel „gleiche E-Mail-Adresse“ oder „gleicher Name und PLZ“.

- **Mit Auswahl verknüpfen** verbindet den Kunden mit dem gewählten Treffer.
- Über das Suchfeld finden Sie einen Kontakt gezielt nach Name, E-Mail-Adresse oder Kundennummer.
- **In Lexware anlegen** legt den Kunden als neuen Kontakt an. Gibt es mögliche doppelte Einträge, fragt EstrichONE
  ausdrücklich nach, bevor es anlegt.

**In Lexware öffnen** zeigt den Kontakt in Lexware Office. **Verknüpfung lösen** trennt die Verbindung; in Lexware
Office bleibt alles erhalten.

Darunter stehen die **Rechnungen und Angebote** des Kunden aus Lexware Office (auch Gutschriften und
Auftragsbestätigungen) mit Art, Nummer, Datum, Fälligkeit bzw. Gültigkeit, Betrag und Status. Ist ein Beleg einer
Bauakte zugeordnet, führt der Link dorthin. Unter der Tabelle steht eine Zeile mit den Summen. **Aktualisieren** lädt
die Liste neu. **PDF anzeigen** und das Symbol zum Herunterladen stehen in jeder Zeile (siehe „Beleg als PDF“).

Bei verknüpften Kunden nennt die Zeile **Änderungsabgleich** den Stand und den Zeitpunkt des letzten Abgleichs. **Jetzt
abgleichen** vergleicht Kunde und Kontakt sofort.

![Kunde mit Lexware Office verknüpft](bilder/lexware-kunde.png "Abschnitt Lexware Office beim Kunden mit Rechnungen und Angeboten")

## Spätere Änderungen abgleichen

Ist ein Kunde verknüpft und **Mit Lexware Office abgleichen** eingeschaltet, hält EstrichONE diese Angaben in beiden
Systemen gleich: Name (bei Personen Anrede, Vor- und Nachname), Rechnungsanschrift mit Straße, PLZ und Ort,
E-Mail-Adresse, Telefon und bei Firmen die USt-ID. Alle anderen Angaben des Kontakts in Lexware Office bleiben
unberührt. **Kundennummern werden nie überschrieben**, jedes System behält seine eigene.

- **Änderung in EstrichONE** (Web oder App): Der Kontakt in Lexware Office wird kurz darauf im Hintergrund
  aktualisiert. Das Speichern in EstrichONE wartet nicht auf Lexware Office. Ist Lexware Office gerade nicht
  erreichbar, holt EstrichONE die Änderung nach.
- **Änderung in Lexware Office**: EstrichONE vergleicht alle Verknüpfungen etwa alle 15 Minuten mit Lexware Office und
  übernimmt die Änderung in den Kunden. Sofort geht es mit **Jetzt abgleichen** am Kunden oder **Aktualisieren** im
  Kundenabgleich.
- **Beide Seiten haben dasselbe Feld unterschiedlich geändert**: EstrichONE überschreibt nichts. Das Feld erscheint
  als **Abweichung** und Sie entscheiden.

Auch beim ersten Abgleich einer Verknüpfung überschreibt EstrichONE nichts. Unterscheiden sich Kunde und Kontakt schon
beim Verknüpfen, zum Beispiel in der Schreibweise des Namens, stehen diese Felder als Abweichungen zur Entscheidung.

### Abweichungen entscheiden

Offene Abweichungen sehen Sie an zwei Stellen: am Kunden im Abschnitt **Lexware Office** und gesammelt unter
**Verwaltung** → **Integrationen** → **Lexware Office** → **Kundenabgleich** in der Liste **Abweichungen**. Zu jedem Feld
stehen beide Werte nebeneinander.

- **Je Feld entscheiden** öffnet die Felder einzeln. Wählen Sie je Feld **EstrichONE** oder **Lexware** oder lassen Sie
  es offen, dann **Auswahl übernehmen**.
- **EstrichONE übernehmen** schreibt alle abweichenden Werte aus EstrichONE in den Kontakt in Lexware Office.
- **Lexware übernehmen** schreibt alle abweichenden Werte aus Lexware Office in den Kunden. Dafür benötigen Sie
  zusätzlich das Recht, Kunden zu bearbeiten.

![Abweichungen im Kundenabgleich](bilder/lexware-abweichungen.png "Kundenabgleich, Liste Abweichungen mit beiden Werten je Feld")

Jede übernommene Änderung steht im Protokoll, mit Richtung und Feldern.

### Richtung des Abgleichs

Unter **Verwaltung** → **Integrationen** → **Lexware Office** wählen Sie bei **Änderungen an verknüpften Kunden
abgleichen** die Richtung:

| Einstellung | Wirkung |
|---|---|
| Beide Richtungen (Vorgabe) | Änderungen einer Seite werden auf der anderen übernommen |
| Nur von EstrichONE nach Lexware Office | Änderungen aus Lexware Office erscheinen als Abweichung |
| Nur von Lexware Office nach EstrichONE | Änderungen aus EstrichONE erscheinen als Abweichung |
| Aus | kein Abgleich späterer Änderungen |

Schalten Sie den Abgleich nach einer Pause wieder ein, zeigt EstrichONE die Unterschiede aus der Zwischenzeit als
Abweichungen und übernimmt sie nicht von selbst. Dasselbe gilt, wenn Sie **Mit Lexware Office abgleichen** bei einem
Kunden aus- und später wieder einschalten.

### Was nicht geschieht

- EstrichONE archiviert oder löscht in Lexware Office keinen Kontakt. Löschen Sie einen Kunden in EstrichONE, endet nur
  die Verknüpfung. Archivierte Kunden werden nicht abgeglichen; ihr Kontakt in Lexware Office bleibt unverändert.
- Wurde der Kontakt in Lexware Office gelöscht oder archiviert, zeigen Kunde und Kundenabgleich das an. Geschrieben
  wird dann nichts.
- Hat der Kunde mehrere E-Mail-Adressen oder Telefonnummern, wird jeweils die erste abgeglichen. Weitere Einträge
  bleiben in beiden Systemen, wie sie sind.

### Kunden aus Lexware Office übernehmen

In der Kundenliste öffnet **Aus Lexware übernehmen** die Kunden aus Lexware Office, die noch nicht verknüpft sind, mit
Suche. **Übernehmen** prüft zuerst, ob es den Kunden in EstrichONE schon gibt. Bei Treffern verknüpfen Sie den
Lexware-Kontakt mit dem vorhandenen Kunden oder legen ihn bewusst neu an. Die Kundennummer in EstrichONE wird
automatisch vergeben.

### Kundenabgleich in der Verwaltung

**Verwaltung** → **Integrationen** → **Lexware Office** → **Kundenabgleich** zeigt den Stand beider Systeme:

| Liste | Inhalt | Aktion je Zeile |
|---|---|---|
| Abweichungen | verknüpfte Kunden mit unterschiedlichen Angaben, beide Werte je Feld | Je Feld entscheiden, EstrichONE übernehmen, Lexware übernehmen |
| Mögliche Dubletten | Kunde und Lexware-Kontakt könnten zusammengehören, mit Grund | Verknüpfen |
| Nur in EstrichONE | Kunden mit eingeschaltetem Abgleich ohne Kontakt in Lexware Office | Prüfen und verknüpfen |
| Bewusst nur in EstrichONE | Kunden mit ausgeschaltetem Abgleich | Auswählen und **Abgleich einschalten** |
| Nur in Lexware | Kontakte ohne Kunden in EstrichONE | Prüfen und übernehmen |
| Verknüpft | zusammengehörige Einträge | Verknüpfung lösen |

Unter den möglichen Dubletten stehen als Hinweis auch doppelte Einträge innerhalb eines Systems. EstrichONE führt
nichts automatisch zusammen und löscht nichts. **Eindeutige Treffer verknüpfen** zeigt eine Vorschau und verknüpft dann
nur Paare, bei denen es auf beiden Seiten genau ein Gegenstück mit gleicher E-Mail-Adresse oder gleicher Kundennummer
gibt. **Aktualisieren** lädt die Kontaktliste neu aus Lexware Office und gleicht die Änderungen aller verknüpften
Kunden ab; sonst hält EstrichONE die Liste einige Minuten vor.

## Lexware Office in der Bauakte

Ist der Kunde einer Bauakte mit Lexware Office verknüpft, zeigt die **Übersicht** der Bauakte den Block **Lexware
Office**: Name und Kundennummer des Kontakts, Rechnungsanschrift und E-Mail-Adresse aus Lexware Office, den Stand des
Abgleichs und eine Zeile mit den Summen der zugeordneten Belege, zum Beispiel „4 Rechnungen: offen 15.767,50 €, davon
überfällig 5.176,50 €“. Weicht eine Angabe von EstrichONE ab, steht das dezent darunter. Links führen zum Abschnitt
**Rechnungen und Angebote**, zum Kontakt in Lexware Office und zum Kunden.

![Lexware Office in der Übersicht der Bauakte](bilder/lexware-bauakte-block.png "Übersicht der Bauakte mit dem Block Lexware Office")

Ohne Verknüpfung erscheint der Block nicht. Wer Kunden verknüpfen darf, sieht stattdessen einen Hinweis mit Link zum
Kunden.

## Rechnungen und Angebote in der Bauakte

Die Bauakte zeigt mit eingeschaltetem Modul den Abschnitt **Rechnungen und Angebote** mit den Belegen, die Sie dieser
Bauakte zugeordnet haben. Die Zuordnung liegt nur in EstrichONE; in Lexware Office ändert sich dadurch nichts.

**Beleg zuordnen** öffnet die Belege des Kunden der Bauakte:

- Nennt ein Beleg die Bauaktennummer in Titel, Einleitung oder Schlusstext, steht er mit dem Hinweis **Nennt diese
  Bauakte** oben. Es lohnt sich deshalb, die Bauaktennummer in Lexware Office in den Beleg zu schreiben. Zugeordnet
  wird trotzdem erst mit Ihrem Klick auf **Zuordnen**.
- Über das Suchfeld finden Sie einen Beleg mit seiner Belegnummer, auch wenn er zu einem anderen Kontakt gehört.
- Ein Beleg gehört zu genau einer Bauakte. Das Symbol am Zeilenende löst die Zuordnung wieder.

**Automatisch zuordnen:** Ist unter **Verwaltung › Integrationen › Lexware Office** der Schalter **Belege automatisch
zuordnen** an (Vorgabe), ordnet EstrichONE sichere Belege selbst zu: Belege, die Sie in EstrichONE erstellt haben, Belege
mit der Bauaktennummer im Text, wenn der Kunde dieser Bauakte verknüpft ist, und Belege, die an einem bereits zugeordneten
Beleg hängen (zum Beispiel die Gutschrift zur Rechnung). Mit dem zweiten Schalter **Einzige offene Bauakte des Kunden**
(Vorgabe aus) ordnet EstrichONE auch zu, wenn der Kunde genau eine offene Bauakte hat und das Belegdatum passt. Kommen
mehrere Bauakten in Frage, ordnet EstrichONE nie selbst zu, sondern zeigt den Beleg weiter als Vorschlag. Automatisch
zugeordnete Belege tragen in der Tabelle den Grund. Heben Sie eine solche Zuordnung auf, merkt sich EstrichONE das und
ordnet den Beleg nicht erneut zu. Die zuletzt automatisch zugeordneten Belege sehen Sie in den Einstellungen.

![Rechnungen und Angebote der Bauakte](bilder/lexware-rechnungen.png "Abschnitt Rechnungen und Angebote in der Bauakte")

Die Tabelle zeigt je Beleg Art, Nummer, Datum, Fälligkeit bzw. Gültigkeit, Netto- und Bruttobetrag und den Status:

| Status | Bedeutung |
|---|---|
| Entwurf | in Lexware Office noch nicht abgeschlossen |
| Offen | abgeschlossen, Rechnung noch nicht bezahlt bzw. Angebot noch nicht entschieden |
| Überfällig | Rechnung offen und die Fälligkeit ist überschritten |
| Bezahlt | Zahlung in Lexware Office verbucht |
| Angenommen, Abgelehnt | Entscheidung zum Angebot in Lexware Office |
| Storniert | in Lexware Office storniert |
| Erstellt | Auftragsbestätigung bzw. Lieferschein ist in Lexware Office festgeschrieben (ohne Zahlungsbedeutung) |

„Offen“, „Überfällig“ und „Bezahlt“ gibt es nur bei Rechnungen und Gutschriften, „Offen“ beim Angebot heißt: noch nicht
angenommen. Auftragsbestätigungen und Lieferscheine zählen nie zu offenen Beträgen. Lieferscheine aus Lexware Office
erscheinen als eigene Belegart **Lieferschein** (nur Anzeige).

Unter der Tabelle fasst eine Zeile die Summen zusammen, zum Beispiel offene und bezahlte Rechnungen.

EstrichONE gleicht den Status beim Öffnen des Abschnitts mit Lexware Office ab, frühestens wieder nach einigen Minuten.
**Aktualisieren** gleicht sofort ab. Ist Lexware Office nicht erreichbar, sehen Sie den zuletzt gespeicherten Stand mit
einem Hinweis. **In Lexware öffnen** führt zum Beleg; dort bearbeiten, versenden und buchen Sie ihn.

## Beleg als PDF

Am Kunden und in der Bauakte zeigt **PDF anzeigen** den Beleg direkt in EstrichONE, so wie Lexware Office ihn erzeugt
hat. In der Vorschau stehen **Herunterladen**, **Neuer Tab** und **In Lexware öffnen**. Das Symbol neben **PDF anzeigen**
lädt die Datei ohne Vorschau herunter.

![Beleg als PDF](bilder/lexware-beleg-pdf.png "Rechnung aus Lexware Office als PDF in der Vorschau")

- Für Entwürfe stellt Lexware Office kein PDF bereit. Die Schaltflächen sind dann nicht wählbar.
- EstrichONE holt das PDF bei jedem Öffnen aus Lexware Office und speichert es nicht. Sie sehen also immer den
  aktuellen Stand.
- Kann das PDF nicht abgerufen werden, nennt eine Meldung den Grund. **In Lexware öffnen** führt weiterhin zum Beleg.

![Beleg zuordnen](bilder/lexware-beleg-zuordnen.png "Dialog Beleg zuordnen mit Vorschlag über die Bauaktennummer")

## Artikel und Leistungen

Unter **Material** → **Artikel und Leistungen** pflegen Sie die Verkaufsartikel und Leistungen, die in Angeboten stehen:
Bezeichnung, Art (Leistung oder Material), Artikelnummer, Einheit, Preis netto und Steuersatz. Ein Eintrag lässt sich
mit einem Artikel des Materialstamms verknüpfen; der Materialstamm selbst führt weiter Einkaufspreise und Lieferanten.

Die Liste hat oben eine Filterleiste: Suche, Art, Einheit, Abgleich mit Lexware Office und Aktiv (Vorgabe: nur aktive
Einträge). **Sortieren** sortiert nach jeder Spalte, auch nach dem Preis brutto. Ein Klick auf eine Zeile öffnet die
Objektseite mit Preis netto, Umsatzsteuer und brutto und dem Abgleichstand; dort finden Sie **Bearbeiten**,
**Duplizieren** und **Inaktiv setzen**. Gelöscht wird ein Eintrag nicht: Inaktive Einträge bleiben erhalten und werden in
Belegen nicht mehr vorgeschlagen.

- **Anlegen:** Einheit m² und 19 Prozent Umsatzsteuer sind vorbelegt. Den Preis geben Sie netto oder brutto ein; der
  andere Wert wird sofort umgerechnet, gespeichert wird netto.
- **Preise ändern:** Wählen Sie in der Liste mehrere Einträge aus und ändern Sie die Preise um einen Prozentsatz, auf
  Cent, 10 Cent oder ganze Euro gerundet. Eine Vorschau zeigt alten und neuen Preis, bevor Sie übernehmen.

Mit dem Modul **Lexware Office: Artikel** (Systemeinstellungen, Module; setzt Lexware Office voraus, nach der
Installation aus) gleicht EstrichONE diese Einträge mit den Artikeln in Lexware Office ab. Die Richtung wählen Sie unter
**Verwaltung** → **Integrationen** → **Lexware Office** bei **Artikel abgleichen**:

| Richtung | Wirkung |
|---|---|
| Aus Lexware Office nach EstrichONE (Vorgabe) | Neue Artikel aus Lexware Office werden übernommen, Änderungen dort nachgezogen. In Lexware Office ändert EstrichONE nichts. |
| Beide Richtungen | Zusätzlich legt EstrichONE neue Einträge in Lexware Office an und überträgt Änderungen dorthin. |
| Aus | Kein Abgleich. Artikel und Leistungen bleiben in EstrichONE nutzbar. |

Der Abgleich läuft regelmäßig im Hintergrund; mit **Jetzt abgleichen** sofort. Gelöscht wird nie, weder in EstrichONE
noch in Lexware Office. Ein Artikel, den es in Lexware Office nicht mehr gibt, bleibt in EstrichONE und ist gekennzeichnet.

- **Mögliche Dubletten:** Hat ein Artikel aus Lexware Office dieselbe Artikelnummer oder dieselbe Bezeichnung wie ein
  Eintrag in EstrichONE, übernimmt EstrichONE ihn nicht automatisch. Er steht unter der Liste zur Entscheidung:
  **Verknüpfen** oder **Trotzdem übernehmen**.
- **Abweichungen:** Haben beide Seiten ein Feld unterschiedlich geändert, oder geht eine Änderung wegen der Richtung
  nicht nach Lexware Office, zeigt die Spalte „Abgleich mit Lexware Office“ **Abweichungen**. Über die Schaltfläche
  entscheiden Sie je Feld.

![Artikel und Leistungen](bilder/lexware-artikel.png "Material, Artikel und Leistungen mit Abgleichstand")

## Verkauf

Mit dem Modul **Verkauf** (setzt Lexware Office voraus, nach dem Update aus) erfassen Sie Belege in EstrichONE und senden
sie an Lexware Office. Die Belegarten schalten Sie unter **Verwaltung › Module** einzeln zu: **Verkauf: Angebote**,
**Verkauf: Auftragsbestätigungen**, **Verkauf: Rechnungen** (auch Abschlags- und Schlussrechnungen), **Verkauf:
Gutschriften**, **Verkauf: Zahlungen und Mahnstufe** und **Verkauf: Festschreiben aus EstrichONE**. Das frühere Modul
„Angebote (Beta)“ ist darin aufgegangen; vorhandene Angebote bleiben erhalten.

Rechte gibt es je Belegart: **ansehen**, **erfassen** und **festschreiben** (Gruppe Verkauf unter Benutzer und Rechte).
Festschreiben ist zusätzlich an das Modul gebunden und standardmäßig aus.

### Übersicht

Unter **Verkauf** in der Seitennavigation stehen alle Belege wie in Lexware Office: Reiter je Belegart, darüber Umschalter
für den Status (Entwurf, Offen, Überfällig, Bezahlt, Angenommen, Abgelehnt), eine Suche und die Filter Kunde und Bauakte.
Die Tabelle zeigt Nummer, Kunde, Bauakte, Datum, Fälligkeit, netto, brutto, offenen Betrag und Status; darunter steht die
Summe der Auswahl als Textzeile. Ein Klick öffnet den Beleg im Belegeditor, Belege, die nur in Lexware Office erfasst
wurden, öffnen sich dort. **Neuer Beleg** fragt nach Kunde und optional Bauakte.

![Übersicht Verkauf](bilder/verkauf-uebersicht.png "Übersicht mit Reitern, Status und Summenzeile")

### Belegeditor

Der Belegeditor ist eine Objektseite mit festem Kopf (Belegart, Status, Kunde, Bauakte, Beträge) und den Abschnitten:

- **Kopfdaten:** Kunde mit Anschrift und Kundennummer, Bauakte, Nummer („wird automatisch vergeben“, die Nummer vergibt
  Lexware Office), Belegdatum (leer heißt Tag des Sendens), beim Angebot **Gültig bis**, sonst **Leistungsdatum bzw.
  Beginn** und **Ende des Leistungszeitraums**, Titel, Zahlungsbedingung und Steuerart. Pflichtfelder sind markiert.
- **Positionen:** Bearbeitung direkt in der Zeile (Bezeichnung mit Beschreibung, Menge, Einheit, Einzelpreis netto,
  Umsatzsteuer, Rabatt, Betrag). Unter der Tabelle steht die Eingabezeile **Artikel suchen oder neue Position eingeben**:
  Tippen zeigt passende Artikel und Leistungen, Enter übernimmt die Eingabe als neue Position. Die Leiste darüber bietet
  **Vorschläge aus der Bauakte** (Fläche aus „Räume und Flächen“ bzw. Kennwerten, Regiestunden aus den Einsätzen),
  **Artikel suchen**, **Position hinzufügen**, **Textposition**, **Zwischensumme** sowie Nach oben, Nach unten und Löschen
  für die ausgewählten Positionen.
- **Texte:** Einleitungstext und Schlusstext.
- **Summen:** netto je Steuersatz, Umsatzsteuer und brutto, live gerechnet.
- **Belegkette und Zahlungen:** Angebot, Auftragsbestätigung, Abschläge, Schlussrechnung und Gutschrift mit Stand; bei
  Rechnungen der offene Betrag aus Lexware Office.

In der Fußleiste: **Speichern** (auch mit Strg und S), **Vorschau** (PDF aus Lexware Office, sobald der Beleg dort
festgeschrieben ist), **An Lexware Office senden** (als Entwurf), **Festschreiben** (mit Modul und Recht, nach
ausdrücklicher Bestätigung; Lexware Office vergibt dann die Nummer) und **Weitere**: Duplizieren, in Auftragsbestätigung
oder Rechnung umwandeln, Gutschrift zur Rechnung, Verwerfen.

Nach dem Senden gilt der Beleg in Lexware Office. Lexware Office erlaubt keine Änderung über die Schnittstelle; ändern
Sie den Beleg dort (**In Lexware öffnen**). Mehrfaches Senden legt keinen zweiten Beleg an.

![Belegeditor Angebot](bilder/verkauf-angebot.png "Angebot im Belegeditor")

### Rechnungsplan in der Bauakte

In der Bauakte zeigt der Abschnitt **Rechnungen und Angebote** oben den **Rechnungsplan**: Auftragssumme (aus der
Auftragsbestätigung bzw. dem angenommenen Angebot), Abschläge, bezahlt, offen und noch nicht berechnet als Textzeile und
als schlichter Balken. Daneben:

- **Angebot erstellen** öffnet ein neues Angebot zur Bauakte.
- **Abschlag erstellen** schlägt den Prozentsatz nach Leistungsstand vor (eingebrachte Fläche laut „Räume und Flächen“
  abzüglich bisheriger Abschläge). Sie können einen anderen Prozentsatz oder einen festen Betrag wählen.
- **Schlussrechnung erstellen** übernimmt alle Positionen des Auftrags und zieht bisherige Abschläge als eigene Zeilen mit
  Nummer und Datum ab.

Abschlags- und Schlussrechnungen sind in Lexware Office normale Rechnungen mit klarer Bezeichnung; Lexware Office legt
über die Schnittstelle keine eigenen Abschlagsrechnungen an. Der Kunde der Bauakte muss mit Lexware Office verknüpft
sein; sonst nennt EstrichONE den Weg zum Verknüpfen.

![Rechnungsplan](bilder/verkauf-rechnungsplan.png "Rechnungsplan der Bauakte")

![Schlussrechnung mit Abzügen](bilder/verkauf-schlussrechnung.png "Schlussrechnung mit Abzug des Abschlags")

### Wenn Lexware Office einen Beleg ablehnt

Lehnt Lexware Office Angaben ab, zeigt EstrichONE eine verständliche Meldung auf Deutsch, wo möglich mit dem betroffenen
Feld bzw. der Position (zum Beispiel „Position 3: Die Einheit fehlt.“). Den Originaltext von Lexware Office sehen Sie bei
Bedarf unter **Technische Einzelheiten**. Danach können Sie den Entwurf bearbeiten und erneut senden; ein bereits
angelegtes Angebot wird nicht doppelt angelegt.

Für bekannte Fälle bietet EstrichONE die Behebung direkt an. Lässt Lexware Office die Steuerart Bauleistung nach § 13b
UStG für Ihr Unternehmen nicht zu, lautet die Meldung: „Lexware Office lässt die Steuerart Bauleistung nach § 13b UStG
für Ihr Unternehmen nicht zu. Prüfen Sie die Steuereinstellungen in Lexware Office oder senden Sie das Angebot mit der
Steuerart Netto.“ Die Schaltfläche **Steuerart Netto übernehmen und erneut senden** ändert die Steuerart, speichert und
sendet in einem Schritt.

EstrichONE merkt sich die Ablehnung je Belegart. Die Steuerart erscheint danach in der Auswahl ausgegraut, mit einem
Hinweis darunter. Haben Sie die Steuereinstellungen in Lexware Office geändert, wählen Sie unter **Verwaltung ›
Integrationen › Lexware Office** im Abschnitt **Steuerarten** die Schaltfläche **Erlaubte Steuerarten erneut prüfen**.
EstrichONE liest dabei das Profil Ihres Unternehmens und bietet alle Steuerarten wieder an.

![Fehler beheben](bilder/lexware-fehler-13b.png "Ablehnung der Steuerart § 13b mit direkter Behebung")

## Status der Bauakte aus Lexware Office

Mit dem Modul **Lexware Office: Status der Bauakte automatisch** (setzt Lexware Office voraus, nach der Installation aus)
folgt die Bauakte dem Stand der zugeordneten Belege. Die Regeln schalten Sie unter **Verwaltung** → **Integrationen** →
**Lexware Office** bei **Status der Bauakte** einzeln ein und aus:

- **Angenommenes Angebot:** Ist ein zugeordnetes Angebot in Lexware Office angenommen und steht die Bauakte auf
  „Anfrage“, setzt EstrichONE sie auf „Geplant“.
- **Alle Rechnungen bezahlt:** Sind alle zugeordneten Rechnungen bezahlt (mindestens eine, keine offen oder überfällig,
  keine offene Gutschrift) und ist die Bauakte weder abgeschlossen noch storniert, erhält sie das Kennzeichen
  **Zur Abschlussprüfung** mit dem Hinweis „Rechnungen vollständig bezahlt, bitte prüfen und abschließen“.

Die Regeln laufen beim Statusabgleich der Belege (beim Öffnen der Bauakte, mit **Aktualisieren** und regelmäßig im
Hintergrund). Sie gehen nur vorwärts: Kein Status wird zurückgesetzt, und eine Bauakte, die jemand schon weitergeschaltet
hat, bleibt unverändert. Die zuständige Person erhält eine Benachrichtigung; der Verlauf vermerkt „automatisch durch
Lexware Office“.

**Abschließen bleibt eine bewusste Handlung.** Im Kopf der Bauakte stehen dafür **Abschließen** (der gewohnte Ablauf) und
**Prüfung zurückstellen**. Zurückgestellt wird das Kennzeichen erst wieder gesetzt, wenn sich der Zahlstand ändert, etwa
durch eine weitere bezahlte Rechnung. In der Bauaktenliste zeigt der Reiter **Zur Abschlussprüfung** alle betroffenen
Bauakten. Die Apps zeigen das Kennzeichen in der Bauakte an.

![Zur Abschlussprüfung](bilder/lexware-abschlusspruefung.png "Kennzeichen und Aktionen im Kopf der Bauakte")

## Fragen

| Frage | Antwort |
|---|---|
| Der Verbindungstest meldet einen abgelehnten Schlüssel. | Erzeugen Sie in Lexware Office einen neuen Schlüssel und hinterlegen Sie ihn. |
| Der Verbindungstest meldet fehlenden Zugriff. | Die Public API ist im gebuchten Tarif nicht enthalten. |
| Die Abschnitte fehlen in Kunde und Bauakte. | Das Modul ist ausgeschaltet oder Ihrer Rolle fehlen die Rechte der Gruppe Rechnungen. |
| Kann ich in EstrichONE eine Rechnung oder ein Angebot schreiben? | Ja, mit dem Modul „Verkauf“. EstrichONE sendet die Belege an Lexware Office; Nummer, Buchhaltung und Archiv bleiben dort. |
| Ein Beleg zeigt „In Lexware Office nicht mehr vorhanden“. | Er wurde dort gelöscht. Lösen Sie die Zuordnung in der Bauakte. |
| Ein Kunde soll nie nach Lexware Office. | Schalten Sie beim Kunden **Mit Lexware Office abgleichen** aus. |
| Was geht an Lexware Office? | Die Daten der Kunden mit eingeschaltetem Abgleich, bei Richtung „Beide Richtungen“ Artikel und Leistungen und im Verkauf die gesendeten Belege. Geändert oder gelöscht wird dort kein Beleg. |
| Ein Artikel aus Lexware Office wurde nicht übernommen. | Es gibt in EstrichONE einen Eintrag mit gleicher Artikelnummer oder Bezeichnung. Entscheiden Sie unter Artikel und Leistungen: verknüpfen oder trotzdem übernehmen. |
| Der Beleg lässt sich nicht senden. | Der Kunde ist nicht mit Lexware Office verknüpft oder Angaben fehlen; die Maske nennt sie. |
| Der gesendete Beleg soll geändert werden. | Ändern Sie ihn in Lexware Office. Der Beleg in EstrichONE ist nach dem Senden schreibgeschützt. |
| Die Bauakte wurde nicht abgeschlossen, obwohl alles bezahlt ist. | Das ist so gewollt: EstrichONE stellt sie nur zur Abschlussprüfung. Abschließen Sie selbst im Kopf der Bauakte. |
| Ein neuer Kunde wurde nicht in Lexware Office angelegt. | Es gibt dort einen ähnlichen Kontakt, der Schalter am Kunden ist aus oder die automatische Anlage ist ausgeschaltet. Der Abschnitt Lexware Office am Kunden nennt den Stand. |
| Ein Kunde zeigt „Abweichungen offen“. | Kunde und Kontakt haben unterschiedliche Angaben. Entscheiden Sie am Kunden oder im Kundenabgleich, welcher Wert gilt. |
| Eine Änderung aus Lexware Office ist noch nicht da. | Der Abgleich läuft etwa alle 15 Minuten. **Jetzt abgleichen** am Kunden holt sie sofort. |
| Der Kunde zeigt „Kontakt in Lexware Office nicht mehr vorhanden“. | Der Kontakt wurde dort gelöscht. Lösen Sie die Verknüpfung und verknüpfen Sie den Kunden bei Bedarf neu. |
| **PDF anzeigen** ist nicht wählbar. | Der Beleg ist ein Entwurf. Lexware Office stellt das PDF erst nach dem Abschließen bereit. |
