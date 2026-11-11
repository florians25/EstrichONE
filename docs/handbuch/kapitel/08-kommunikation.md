# Kommunikation: Helpdesk, Chat und Benachrichtigungen

## Helpdesk {#helpdesk}

Klemmt etwas – am iPad der Kolonne, an der App oder im Büro –, melden Mitarbeiter es als **Ticket**. Die Apps senden auf
Wunsch technische Angaben (Gerät, Version, Abgleich, Fehlercodes) mit; Fotos und Bildschirmfotos lassen sich anhängen.
Alles rund um den Helpdesk steht in der Seitennavigation in der eigenen Gruppe **IT-Service/Helpdesk**: **Helpdesk**
(Tickets), **Problem melden**, **Helpdesk-Einstellungen** (nur für die Bearbeitung) und **Wissensdatenbank**. Die Gruppe
erscheint nur, wenn das Modul Helpdesk eingeschaltet ist.

![Helpdesk](bilder/helpdesk.png "Arbeitsplatz der IT mit Kennzahlen, Ansichten und Ticketliste")

### Arbeitsplatz der IT

- Oben die **Kennzahlen**: offen, nicht zugewiesen, überfällig, Rückfrage, mir zugewiesen, diese Woche gelöst,
  durchschnittliche Zeit bis zur ersten Antwort, durchschnittliche Lösungszeit und Zufriedenheit. Ein Klick auf eine
  Kennzahl öffnet die passende Ansicht. Erste Antwort und Lösungszeit zählen wie die Ziele in **Geschäftszeit**: Nächte,
  Wochenenden, Feiertage und die Zeit einer Rückfrage an den Melder zählen nicht mit.
  Darunter **Offen nach Kategorie** und die **Auslastung** je Bearbeiter.
- Gespeicherte **Ansichten**: **Meine**, **Nicht zugewiesen**, **Überfällig**, **Warten auf Support**, **Warten auf
  Anwenderinfo**, **Alle offenen**, **Meine Gruppen**, **Erledigt**, **Alle**.
- **Status**: Neu → In Bearbeitung → **Warten auf Anwenderinfo** (Rückfrage beim Melder, die Uhr der Ziele steht) →
  **Warten auf Support** (der Melder hat geantwortet, die IT ist wieder am Zug, die Uhr läuft) → Gelöst → Geschlossen.
  „Warten auf Support“ setzt EstrichONE selbst, sobald der Melder auf eine Rückfrage antwortet; die zuständige Person (sonst
  die Gruppe bzw. alle Bearbeiter) wird benachrichtigt. Eine öffentliche Antwort der IT setzt wieder „In Bearbeitung“.
- **Gelöste Tickets** schließen sich nach **5 Tagen** ohne Widerspruch automatisch (Frist unter Helpdesk-Einstellungen →
  Optionen). Bis dahin kann der Melder das Ticket wieder öffnen; der Verlauf zeigt „Automatisch geschlossen“.
- Die **Liste** lässt sich über die Spaltenköpfe sortieren (Nummer, Titel, Melder, Kategorie, Priorität, Status,
  Zuständig, Fällig, Aktivität). Die Suche findet Ticketnummern (auch nur „0042“), Titel, Beschreibung, Melder und Antworten.
  Die Liste lädt 100 Tickets auf einmal; **Weitere laden** unter der Liste holt die nächsten (Anzeige „100 von 742 Tickets“).
  Sortierung und Suche gelten immer für alle Tickets der Ansicht, nicht nur für die geladenen.
- **Sammelbearbeitung**: Tickets ankreuzen und mit **Zuweisen**, **Status**, **Priorität** oder **Gruppe** gemeinsam ändern.
  Beim Lösen fragt EstrichONE einmal nach der Lösung für alle.
- Die Spalte **Fällig** zeigt das nächste Ziel (erste Antwort bzw. Lösung): rot **überfällig**, orange **bald fällig**,
  blau **angehalten**, solange der Melder eine Rückfrage beantworten soll.

### Meldung aufnehmen (Anruf, E-Mail, vor Ort)

Ruft jemand an, schreibt eine E-Mail oder kommt vorbei, legt die IT das Ticket im Namen dieser Person an:
**Meldung aufnehmen** (Hörer-Symbol) → **Eingang** wählen (**Telefon**, **E-Mail**, **Vor Ort**) → **Melder** suchen
(Name, Benutzername, E-Mail oder Telefon). Hat die Person keinen Zugang zu EstrichONE, tragen Sie ihren Namen unter
**Oder Name** ein. Dazu Kurzbeschreibung, Kategorie, Priorität und Beschreibung, auf Wunsch einen Anhang (z. B. das
Bildschirmfoto aus der E-Mail). **Zuweisen** steht auf **Mir** – dann ist das Ticket gleich **In Bearbeitung**;
**Automatisch nach Regeln** überlässt die Zuordnung den Regeln.

Das Ticket gehört dem Melder: Er sieht es unter seinen Tickets, erhält einen Hinweis „Ticket … für Sie angelegt“ und alle
Antworten. Im Ticketkopf steht unter **Gemeldet** der Eingang mit der Person, die aufgenommen hat (z. B. „Telefon,
aufgenommen von Anna Becker“). Die Apps bieten dasselbe unter **Meldung aufnehmen**.

![Meldung aufnehmen](bilder/helpdesk-meldung-aufnehmen.png "Meldung nach einem Anruf aufnehmen")

### Ticket bearbeiten

Ein Klick öffnet das Ticket neben der Liste. Im Kopf stehen Status, Priorität, Kategorie mit Unterkategorie, Melder,
Zuständig mit Gruppe, die Ziele und die Schnellaktionen (Status, Priorität, Kategorie, Unterkategorie, Gruppe, Zuständig,
**Mir zuweisen** über das Personen-Symbol). Die Schnellaktionen stehen als Raster untereinander und bleiben auch in
schmalen Fenstern (z. B. 1024 Pixel) vollständig sichtbar. Unter **…** (Weitere Aktionen): **Als Duplikat zusammenführen**,
**Ticket verknüpfen**, **Artikel verknüpfen**, **Arbeitszeit erfassen** und **Lösung als Artikel speichern**.

- **Verlauf**: Meldung, Antworten, interne Notizen und alle Änderungen; Anhänge mit Vorschau. Im Antwortfeld gibt es
  **Datei anhängen**, Bildschirmfotos per <kbd>Strg</kbd> + <kbd>V</kbd>, **Textbaustein** und **Kollegen benachrichtigen**.
  Tippen Sie **@** und den Anfang eines Namens, schlägt EstrichONE passende Kolleginnen und Kollegen vor: mit den
  Pfeiltasten wählen, mit <kbd>Eingabe</kbd> oder <kbd>Tab</kbd> übernehmen, <kbd>Esc</kbd> schließt die Auswahl. Erwähnte
  werden benachrichtigt. **Interne Notiz** ist für den Melder unsichtbar. Senden mit <kbd>Strg</kbd> + <kbd>Enter</kbd>.
  **Mit Rückfrage senden** schickt die Antwort und setzt den Status auf „Warten auf Anwenderinfo“.
- **Melder & Geräte**: Kontakt des Melders, seine Geräte mit App-Version und Update-Stand, letzter Kontakt, die
  Diagnosedaten, weitere Tickets der Person und der Systemstatus (Server, Sicherung, Archiv).
- **Verknüpfungen**: verknüpfte Tickets und Artikel sowie passende Vorschläge aus der Wissensdatenbank.
- **Zeit**: erfasste Arbeitszeit (z. B. „30“, „1:15“ oder „1,5 h“).
- **Prüfpfad**: jede Änderung mit Zeitpunkt und Person.

**Zusammenführen** schließt ein doppelt gemeldetes Ticket mit Hinweis auf das Ziel. Dessen Melder verfolgt das Ziel-Ticket
und erhält dort die Antworten; Beschreibung und Anhänge landen als interne Notiz im Ziel.

### Einstellungen und Wissensdatenbank

Unter **Helpdesk-Einstellungen** legen Personen mit dem Recht „Helpdesk einrichten“ fest:

- **Ziele und Zeiten**: erste Antwort und Lösung je Priorität in Stunden, Geschäftszeiten, Arbeitstage, Feiertage,
  Bewertung, Zeiterfassung, Vorschläge und nach wie vielen Tagen gelöste Tickets geschlossen werden.
- **Feiertage**: Wählen Sie das **Bundesland**. EstrichONE berechnet die gesetzlichen Feiertage jedes Jahr selbst, auch
  die beweglichen um Ostern (Karfreitag, Ostermontag, Christi Himmelfahrt, Pfingstmontag, Fronleichnam); die Vorschau zeigt
  das laufende und das nächste Jahr. Unter **Zusätzliche Tage (Betriebsruhe)** tragen Sie eigene Tage ein, z. B. Heiligabend
  oder Betriebsferien – ein Datum je Zeile. An all diesen Tagen steht die Uhr der Ziele. Feiertage, die nur in einzelnen
  Gemeinden gelten (z. B. Mariä Himmelfahrt in Teilen Bayerns, Fronleichnam in Teilen Sachsens und Thüringens), prüfen Sie
  bitte selbst und ergänzen sie bei Bedarf als zusätzliche Tage.
- **Gruppen** (z. B. IT, Büro-IT) mit Mitgliedern, **Unterkategorien** (z. B. Drucker, Telefon, E-Mail) und
  **Automatische Zuweisung**: Regeln nach Kategorie, Unterkategorie oder Stichwort setzen Gruppe, Person (fest oder das
  Mitglied mit den wenigsten offenen Tickets) und Priorität.
- **Textbausteine** für Antworten, gemeinsam oder nur für sich; Platzhalter {Melder}, {Vorname}, {Ticket}, {Titel},
  {Bearbeiter}.

Die **Wissensdatenbank** sammelt Anleitungen. Veröffentlichte Artikel sehen alle; beim Melden schlägt EstrichONE passende
Artikel vor. Entwürfe sieht nur die IT.

### Für alle Mitarbeiter

Mit **Neues Ticket** melden Sie ein Problem (Kategorie, auf Wunsch Unterkategorie, Priorität, Beschreibung, Anhänge).
Jeder sieht seine eigenen Tickets mit Verlauf; neue Antworten erscheinen als Benachrichtigung. Nach der Lösung können Sie
mit **1 bis 5 Sternen** bewerten oder das Ticket **wieder öffnen**.

## Chat {#chat}

Der **Chat** dient der schnellen Abstimmung zwischen Büro und Baustelle: Einzelchats, Gruppen (auch je Kolonne) und
**Baustellen-Chats** zu einer Bauakte.

![Chat](bilder/chat.png "Gruppenchat „Baustelle Sonnenhof“")

- **Neuer Einzelchat** und **Neue Gruppe** beginnen eine Unterhaltung. Aus einer Bauakte legen Sie mit
  **Baustellen-Chat anlegen** eine Gruppe mit allen Beteiligten an; **Bauakte öffnen** springt zurück.
- Nachrichten können Fotos, Dokumente, Kamerabilder oder den Standort enthalten. Ein Foto aus dem Chat lässt sich direkt
  in die Bauakte übernehmen.
- **Diktieren** nutzt die Spracherkennung des Browsers; es wird kein Audio gespeichert – prüfen Sie den Text vor dem
  Senden.
- Nachrichten lassen sich beantworten, mit einer Reaktion versehen, anheften, weiterleiten und innerhalb einer Frist
  bearbeiten. **In dieser Unterhaltung suchen** und **Medien und Dokumente** helfen beim Wiederfinden.
- **Meine Chat-Einstellungen**: Lesebestätigungen ein- oder ausschalten und die eigenen Chatdaten exportieren. Einzelne
  Unterhaltungen lassen sich stumm schalten.

Beim ersten Öffnen erklärt ein Hinweis den Datenschutz im Chat: Bitte keine privaten Gesundheitsdaten o. Ä. teilen.
Nachrichten und Anhänge werden verschlüsselt gespeichert und nach der eingestellten Frist automatisch gelöscht.

> **Wichtig:** Der Chat ist ab Werk **ausgeschaltet**. Ein Chat unter Beschäftigten ist mit dem Betriebsrat abzustimmen
> (§ 87 BetrVG) und ins Verzeichnis der Verarbeitungstätigkeiten aufzunehmen. Eingeschaltet wird er unter
> **Systemeinstellungen** → **Module** (Abschnitt 9.6).

## Benachrichtigungen {#benachrichtigungen}

Die **Glocke** oben rechts zeigt die Zahl der ungelesenen Benachrichtigungen. Sie führt zur Seite
**Benachrichtigungen**: Ereignisse zu Ihren Bauakten, Freigaben, Plänen, Tickets und Fristen sowie Nachrichten aus dem
Büro.

![Benachrichtigungen](bilder/benachrichtigungen.png "Das Postfach mit Benachrichtigungen")

- **Alle** oder **Ungelesen**, gefiltert nach **Art** (Nachrichten, Planung, Bauakten, Freigaben, Helpdesk, Aufgaben und
  Mängel, Fristen, Urlaub, Chat, System).
- Ein Klick öffnet das Ziel, z. B. die Bauakte oder das Dokument zur Freigabe. Über das Menü einer Meldung markieren Sie
  sie als gelesen oder ungelesen; das Kreuz entfernt sie. **Alle gelesen** markiert alles auf einmal.
- **Einrichten** (auch Benutzermenü → **Benachrichtigungen einrichten**): Je Ereignis wählen Sie **Aus**, **Postfach**
  oder **Push**. Pflichtmeldungen (Büro, Wartung, Störungen) lassen sich nicht abbestellen. Unter **Push und Geräte**
  schalten Sie **Pushnachrichten in diesem Browser** ein, sehen **Meine Geräte** und senden eine **Testnachricht**.

> **Hinweis:** Pushnachrichten im Browser brauchen eine HTTPS-Verbindung und die Erlaubnis des Browsers. Erscheint keine
> Abfrage, prüfen Sie die Website-Einstellungen Ihres Browsers.

## Nachrichten senden

Mit dem Recht „Nachrichten senden“ erreicht das Büro Mitarbeiter direkt: **Büro und Stammdaten** → **Nachrichten
senden** (oder **Nachricht senden** auf der Seite Benachrichtigungen).

![Nachrichten senden](bilder/nachrichten.png "Gesendete Nachrichten mit Empfängern, Lesestand und Zustellung")

1. **Neue Nachricht**.
2. Empfänger auswählen: einzelne **Personen**, **Rollen**, **Kolonnen** oder alle.
3. **Titel** (z. B. „Morgen Treffpunkt 6:30 Uhr am Lager“), **Text** und **Dringlichkeit** (Niedrig, Normal, Dringend).
4. Optional eine **Verknüpfung** (Tagesübersicht, Urlaub, Helpdesk, Übersicht), die beim Öffnen angesprungen wird.
5. **Senden**.

Die Nachricht landet im Postfach und erscheint als Pushnachricht in App und Browser. Die Liste **Gesendet** zeigt, wie
viele Empfänger sie gelesen haben und wie sie zugestellt wurde (Push, nur Postfach oder Fehler).

> **Achtung:** Pushnachrichten erscheinen auf dem Sperrbildschirm. Schreiben Sie keine vertraulichen Inhalte
> (Gesundheit, Preise, Passwörter) hinein.
