# Personal und Urlaub

## Personalstamm

**Personal** → **Mitarbeiter** ist der Personalstamm: Kontakt, Beschäftigung, Qualifikationen, Urlaub, ausgegebene
Geräte und vertrauliche Personaldokumente.

![Personal](bilder/personal.png "Der Personalstamm mit Tätigkeit, Kolonne, Resturlaub und Status")

- **Mitarbeiter anlegen** erfasst einen neuen Mitarbeiter. Auf Wunsch legt EstrichONE gleich das **Benutzerkonto** mit
  an und erstellt ein **Onboarding-PDF** mit Zugangsdaten, App-Einrichtung (QR-Code, 15 Minuten gültig) und ersten
  Schritten, abgelegt in der Mitarbeiterakte und optional per E-Mail verschickt.
- **Aus Benutzerkonten übernehmen** legt für alle aktiven Benutzer ohne Personalstamm einen Mitarbeiter an.
- Die Reiter **Aktiv**, **Ausgetreten** und **Alle** sowie die Suche (Name, Personalnummer, Ort, Tätigkeit) grenzen die
  Liste ein. Der **Status** zeigt, ob jemand verfügbar, im Urlaub oder krank ist.

### Mitarbeiterakte

Ein Klick öffnet die Mitarbeiterakte mit den Abschnitten:

| Abschnitt | Inhalt |
|---|---|
| **Übersicht / Kontakt** | Handy, Telefon, E-Mail, Anschrift, Geburtsdatum, Notfallkontakt mit Beziehung |
| **Beschäftigung** | Personalnummer (automatisch aus dem Nummernkreis, nicht änderbar), Art (Vollzeit, Teilzeit …), Eintritt und Austritt, Arbeitszeit, Arbeitstage, Stundensatz, Notizen |
| **Kolonnen** | Zugehörigkeit zu Kolonnen (**Kolonnen ändern**) |
| **Qualifikationen** | Qualifikationen und Führerscheinklassen mit Nummer und **Gültig bis** |
| **Urlaub und Abwesenheiten** | Anspruch, Übertrag, genommen, geplant, Rest, Krankheitstage; **Abwesenheit eintragen** |
| **Geräte und Fahrzeuge** | Ausgegebene Geräte mit Ausgabe und Rückgabe (**Ausgeben**) |
| **Dokumente** | Personaldokumente (vertraulich), z. B. Arbeitsvertrag oder Bescheinigungen |

> **Wichtig:** Kolonnen, Planung und Abwesenheiten brauchen ein **verknüpftes Benutzerkonto**. Verknüpfen Sie es in der
> Mitarbeiterakte unter **Bearbeiten** → **Benutzerkonto**.

## Urlaub und Abwesenheiten

**Personal** → **Mein Urlaub** ist die Seite für alle Mitarbeiter. Oben stehen **Mein Resturlaub
(Tage)**, **Offene Anträge** und **Heute abwesend**.

![Urlaub und Abwesenheiten](bilder/urlaub.png "Die Seite Urlaub und Abwesenheiten")

### Urlaub beantragen

1. Klicken Sie auf **Urlaub beantragen**.
2. Wählen Sie Zeitraum und Art (Urlaub, Schulung, Berufsschule, Sonderurlaub, Sonstiges) und schreiben Sie bei Bedarf
   eine Notiz.
3. Senden Sie den Antrag. Er erscheint unter **Meine Anträge** mit Status; solange er offen ist, können Sie ihn
   **zurückziehen**.

Die Genehmigenden werden benachrichtigt. Sie sehen den Antrag im Reiter **Urlaubsanträge** unter **Warten auf Genehmigung**,
mit den Tagen, dem Resturlaub danach und Hinweisen (z. B. wenn schon viele aus derselben Kolonne fehlen), und
entscheiden mit **Genehmigen** oder **Ablehnen**. **Für Mitarbeiter beantragen** erfasst einen Antrag stellvertretend.

![Urlaubsanträge](bilder/urlaubsantraege.png "Offener Urlaubsantrag mit Tagen und Resturlaub danach")

### Urlaubsplan

Der Reiter **Urlaubsplan** zeigt einen Monat mit allen Mitarbeitern, gruppiert nach Kolonnen. Die Kürzel bedeuten:
**U** Urlaub, **K** Krank, **S** Schulung, **B** Berufsschule, **SU** Sonderurlaub, **A** Abwesend. Beantragte, noch
nicht genehmigte Tage sind schraffiert; Wochenenden und Feiertage sind grau hinterlegt. Die Feiertage richten sich nach
dem eingestellten Bundesland. **PDF Monat** und **PDF Jahr** drucken den Plan.

![Urlaubsplan](bilder/urlaubsplan.png "Der Urlaubsplan eines Monats nach Kolonnen")

### Krankmeldungen und Fristen

**Krankmeldungen** erfasst die Personalverwaltung mit **Krankmeldung erfassen**, auf Wunsch mit AU-Bescheinigung.
Krankmeldungen sind vertraulich: Sie sieht nur die Personalverwaltung; in der Planung erscheint lediglich „abwesend“.
Wurde die elektronische AU (eAU) bei der Krankenkasse abgerufen, tragen Sie **eAU abgerufen am** ein. Die Spalte
**AU-Bescheinigung** zeigt dann „eAU abgerufen“. Andernfalls zeigt sie „vorhanden“, „fehlt“, „ab …“ (noch nicht fällig)
oder „nicht erforderlich“.

### Warnungen bei Krankmeldungen und Abwesenheiten

Über der Liste der Krankmeldungen stehen die **Warnungen** mit Status (Dringend, Warnung, Hinweis), Stichtag und
Aktionen. Die Kennzahl **Warnungen** oben auf der Seite zeigt ihre Anzahl. Folgende Warnungen gibt es:

- **Einsätze betroffen:** Eine Abwesenheit fällt auf geplante Einsätze (Vorschau standardmäßig 14 Tage). Die Warnung
  ist dringend, wenn die Kolonne unter ihre Mindestbesetzung fällt oder niemand mehr eingeplant ist. **Zur Planung**
  öffnet den betroffenen Tag.
- **AU-Nachweis fehlt:** Ab dem eingestellten Kalendertag (Standard: 4. Tag, also bei mehr als drei Tagen) fehlt eine
  AU-Bescheinigung oder Folgebescheinigung. **eAU abgerufen** markiert die eAU mit dem heutigen Datum.
- **Entgeltfortzahlung:** Eine Woche bevor die sechs Wochen eines Krankheitsfalls (Erstmeldung mit
  Folgebescheinigungen) enden. Frühere Krankmeldungen der letzten sechs Monate werden als möglicher Fortsetzungsfall
  genannt. Dies ist nur ein Hinweis. Ob dieselbe Ursache vorliegt, prüft die Personalverwaltung.
- **BEM anbieten:** mehr als 42 Kalendertage Arbeitsunfähigkeit in den letzten zwölf Monaten (§ 167 Abs. 2 SGB IX).
- **Überschneidung mit Urlaub:** Krankmeldung im genehmigten Urlaub (die Tage werden nicht als Urlaub gezählt) oder
  während eines offenen Urlaubsantrags.
- **Häufung kurzer Krankmeldungen** und **Krankmeldung direkt vor oder nach Urlaub oder Wochenende:** neutrale Hinweise,
  standardmäßig ausgeschaltet. Schalten Sie sie nur ein, wenn es dafür eine Rechtsgrundlage oder Betriebsvereinbarung gibt.

**Zur Kenntnis genommen** blendet eine Warnung für alle aus, bis sich der Sachverhalt ändert (zum Beispiel wenn ein
weiterer Einsatz betroffen ist). Mit **Warnungen einstellen** (auch im Reiter **Arten und Betriebsferien**) legen Sie
Regeln, Schwellwerte und Empfänger fest. Der Hauptschalter schaltet alle Warnungen ab.

**Datenschutz:** Diagnosen werden nie gespeichert. Gesundheitsbezogene Warnungen sieht nur, wer Krankmeldungen pflegen
darf (Recht „Personalstamm pflegen …“). Die Disposition (Recht „Einsätze planen“) sieht nur „abwesend“ und die
betroffenen Einsätze. In der **Kolonnenplanung** erscheint dazu eine Hinweisleiste mit **Betroffene Einsätze anzeigen**,
auf der Startseite eine Kennzahl. Benachrichtigungen (Postfach und Push) enthalten nie „krank“ oder Fristen. Die
Disposition erhält „Abwesenheit betrifft Einsätze“ mit Name, Zeitraum und betroffenen Einsätzen. Die
Personalverwaltung erhält „Neue Abwesenheitsmeldung“ oder „Personalhinweis“ mit der Bitte, in EstrichONE nachzusehen.
In den Apps erscheinen die Warnungen für dieselben Berechtigten unter **Urlaub und Abwesenheit**.

Der Reiter **Fristen & Qualifikationen** erinnert an ablaufende Qualifikationen und
Führerscheine, Probezeitende, Geburtstage und Jubiläen. Über das Zahnrad stellen Sie **Bundesland**, die
**Kolonnen-Grenze** (wie viel Prozent einer Kolonne gleichzeitig fehlen dürfen) und die **Erinnerungen** ein.
