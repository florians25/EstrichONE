# Lexware Office

Mit dem Modul **Lexware Office** arbeiten EstrichONE und Lexware Office (früher lexoffice) zusammen. Die Aufgaben sind
klar getrennt:

- **Rechnungen und Angebote erstellen, bearbeiten und versenden Sie ausschließlich in Lexware Office.** EstrichONE legt
  dort keine Belege an und ändert keine.
- **EstrichONE zeigt die Belege an**: am Kunden alle Rechnungen und Angebote, in der Bauakte die zugeordneten, jeweils
  mit Nummer, Datum, Betrag und Status, und führt mit einem Klick zum Beleg in Lexware Office.
- **Kunden werden abgeglichen**: Sie verknüpfen Kunden mit ihren Kontakten in Lexware Office oder legen sie dort an,
  immer mit Prüfung auf doppelte Einträge.

Voraussetzung ist ein Tarif von Lexware Office mit Public API (im Tarif XL enthalten).

## Einrichten

1. Erzeugen Sie in Lexware Office unter **Einstellungen** → **Erweiterungen** → **Public API** einen API-Schlüssel
   (direkt: app.lexware.de/addons/public-api).
2. Öffnen Sie in EstrichONE **Verwaltung** → **Integrationen** → **Lexware Office**, fügen Sie den Schlüssel ein und
   wählen Sie **Verbindung testen**. Bei Erfolg zeigt EstrichONE den Firmennamen Ihrer Organisation in Lexware Office.
3. Schalten Sie **Lexware Office verwenden** ein. Derselbe Schalter steht unter **Systemeinstellungen** → **Module**.
4. Geben Sie den zuständigen Rollen unter **Rollen und Rechte** die Rechte der Gruppe **Rechnungen**.

Der Schlüssel wird verschlüsselt gespeichert und nie wieder angezeigt. Die **Basis-URL** ändern Sie nur, wenn Lexware
eine andere Adresse vorgibt.

![Lexware Office einrichten](bilder/lexware-einstellungen.png "Verwaltung, Integrationen, Lexware Office")

| Recht | Erlaubt |
|---|---|
| Rechnungen und Angebote sehen | Belege am Kunden und in der Bauakte ansehen |
| Belege zuordnen und Kunden verknüpfen | Belege Bauakten zuordnen, Kunden verknüpfen, anlegen und übernehmen |
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
gesammelt ein. Auch mit eingeschaltetem Schalter überträgt EstrichONE nichts von selbst: Übertragen wird erst, wenn Sie
einen Kunden verknüpfen oder in Lexware Office anlegen.

Schalten Sie den Abgleich bei einem bereits verknüpften Kunden aus, bleibt die Verknüpfung bestehen, bis Sie sie lösen.
Der Kontakt und seine Belege liegen ja bereits in Lexware Office und werden weiter angezeigt; die Kundenansicht weist
darauf hin.

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
die Liste neu.

![Kunde mit Lexware Office verknüpft](bilder/lexware-kunde.png "Abschnitt Lexware Office beim Kunden mit Rechnungen und Angeboten")

### Kunden aus Lexware Office übernehmen

In der Kundenliste öffnet **Aus Lexware übernehmen** die Kunden aus Lexware Office, die noch nicht verknüpft sind, mit
Suche. **Übernehmen** prüft zuerst, ob es den Kunden in EstrichONE schon gibt. Bei Treffern verknüpfen Sie den
Lexware-Kontakt mit dem vorhandenen Kunden oder legen ihn bewusst neu an. Die Kundennummer in EstrichONE wird
automatisch vergeben.

### Kundenabgleich in der Verwaltung

**Verwaltung** → **Integrationen** → **Lexware Office** → **Kundenabgleich** zeigt den Stand beider Systeme:

| Liste | Inhalt | Aktion je Zeile |
|---|---|---|
| Mögliche Dubletten | Kunde und Lexware-Kontakt könnten zusammengehören, mit Grund | Verknüpfen |
| Nur in EstrichONE | Kunden mit eingeschaltetem Abgleich ohne Kontakt in Lexware Office | Prüfen und verknüpfen |
| Bewusst nur in EstrichONE | Kunden mit ausgeschaltetem Abgleich | Auswählen und **Abgleich einschalten** |
| Nur in Lexware | Kontakte ohne Kunden in EstrichONE | Prüfen und übernehmen |
| Verknüpft | zusammengehörige Einträge | Verknüpfung lösen |

Unter den möglichen Dubletten stehen als Hinweis auch doppelte Einträge innerhalb eines Systems. EstrichONE führt
nichts automatisch zusammen und löscht nichts. **Eindeutige Treffer verknüpfen** zeigt eine Vorschau und verknüpft dann
nur Paare, bei denen es auf beiden Seiten genau ein Gegenstück mit gleicher E-Mail-Adresse oder gleicher Kundennummer
gibt. **Aktualisieren** lädt die Kontaktliste neu aus Lexware Office; sonst hält EstrichONE sie einige Minuten vor.

## Rechnungen und Angebote in der Bauakte

Die Bauakte zeigt mit eingeschaltetem Modul den Abschnitt **Rechnungen und Angebote** mit den Belegen, die Sie dieser
Bauakte zugeordnet haben. Die Zuordnung liegt nur in EstrichONE; in Lexware Office ändert sich dadurch nichts.

**Beleg zuordnen** öffnet die Belege des Kunden der Bauakte:

- Nennt ein Beleg die Bauaktennummer in Titel, Einleitung oder Schlusstext, steht er mit dem Hinweis **Nennt diese
  Bauakte** oben. Es lohnt sich deshalb, die Bauaktennummer in Lexware Office in den Beleg zu schreiben. Zugeordnet
  wird trotzdem erst mit Ihrem Klick auf **Zuordnen**.
- Über das Suchfeld finden Sie einen Beleg mit seiner Belegnummer, auch wenn er zu einem anderen Kontakt gehört.
- Ein Beleg gehört zu genau einer Bauakte. Das Symbol am Zeilenende löst die Zuordnung wieder.

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

Unter der Tabelle fasst eine Zeile die Summen zusammen, zum Beispiel offene und bezahlte Rechnungen.

EstrichONE gleicht den Status beim Öffnen des Abschnitts mit Lexware Office ab, frühestens wieder nach einigen Minuten.
**Aktualisieren** gleicht sofort ab. Ist Lexware Office nicht erreichbar, sehen Sie den zuletzt gespeicherten Stand mit
einem Hinweis. **In Lexware öffnen** führt zum Beleg; dort bearbeiten, versenden und buchen Sie ihn.

![Beleg zuordnen](bilder/lexware-beleg-zuordnen.png "Dialog Beleg zuordnen mit Vorschlag über die Bauaktennummer")

## Fragen

| Frage | Antwort |
|---|---|
| Der Verbindungstest meldet einen abgelehnten Schlüssel. | Erzeugen Sie in Lexware Office einen neuen Schlüssel und hinterlegen Sie ihn. |
| Der Verbindungstest meldet fehlenden Zugriff. | Die Public API ist im gebuchten Tarif nicht enthalten. |
| Die Abschnitte fehlen in Kunde und Bauakte. | Das Modul ist ausgeschaltet oder Ihrer Rolle fehlen die Rechte der Gruppe Rechnungen. |
| Kann ich in EstrichONE eine Rechnung oder ein Angebot schreiben? | Nein. Belege entstehen ausschließlich in Lexware Office; EstrichONE zeigt sie an. |
| Ein Beleg zeigt „In Lexware Office nicht mehr vorhanden“. | Er wurde dort gelöscht. Lösen Sie die Zuordnung in der Bauakte. |
| Ein Kunde soll nie nach Lexware Office. | Schalten Sie beim Kunden **Mit Lexware Office abgleichen** aus. |
| Was geht an Lexware Office? | Nur die Daten der Kunden, die Sie dort anlegen. Belege liest EstrichONE nur. |
