# Erste Schritte

## Anmelden

1. Öffnen Sie im Browser die Adresse Ihres EstrichONE-Servers – immer mit `https://`.
2. Geben Sie Ihren **Anmeldenamen** (oder Ihre E-Mail-Adresse) und das **Passwort** ein.
3. Auf dem eigenen Arbeitsplatzrechner können Sie **Angemeldet bleiben (dieses Gerät)** einschalten. Auf fremden oder
   gemeinsam genutzten Rechnern lassen Sie das Häkchen weg.
4. Klicken Sie auf **Anmelden**.

![Anmeldeseite](bilder/anmeldung.png "Die Anmeldeseite. Darunter steht, dass die Daten auf Ihrem eigenen Server bleiben.")

Bei der ersten Anmeldung mit einem **Einmalpasswort** aus dem Zugangsbrief verlangt EstrichONE sofort ein eigenes
Passwort. Es muss mindestens 12 Zeichen lang sein; verbreitete Passwörter wie „Sommer2026!“ werden abgelehnt. Am
sichersten sind mehrere zufällige Wörter, z. B. „Kelle Silo Wasserwaage Morgen“.

> **Tipp:** Ist die Zwei-Faktor-Anmeldung eingeschaltet, fragt EstrichONE nach dem Passwort zusätzlich den
> **Bestätigungscode** ab: die sechs Ziffern aus Ihrer Authenticator-App. Haben Sie das Handy nicht zur Hand, geben Sie
> stattdessen einen Ihrer Wiederherstellungscodes ein.

Läuft eine Sitzung wegen längerer Pause ab, erscheint ein kleiner Anmeldedialog über der Seite. Ihre Eingaben bleiben
dabei erhalten – nach der erneuten Anmeldung arbeiten Sie an derselben Stelle weiter.

### Ersteinrichtung (nur einmal, durch die Administration)

Nach der Installation zeigt EstrichONE beim ersten Aufruf „EstrichONE ist noch nicht eingerichtet“ mit dem Link
**Jetzt einrichten**. Für die Einrichtung brauchen Sie den **Einrichtungscode**, den der Server beim ersten Start in sein
Protokoll schreibt (`docker compose logs api`). Tragen Sie Firmennamen und den ersten Administrator ein und klicken Sie
auf **Einrichten und anmelden**.

![Ersteinrichtung](bilder/einrichtung.png "Ersteinrichtung mit Einrichtungscode, Firmenname und erstem Administrator"){halb}

## Zwei-Faktor-Anmeldung einrichten

Die Zwei-Faktor-Anmeldung schützt Ihr Konto zusätzlich zum Passwort. Für Konten mit Verwaltungsrechten kann die
Administration sie vorschreiben (Abschnitt 9.6). Dann ruhen die Verwaltungsrechte, bis die Einrichtung erledigt ist,
und oben erscheint der Hinweis „Bitte richten Sie die Zwei-Faktor-Anmeldung ein“.

1. Installieren Sie auf dem Smartphone eine Authenticator-App, z. B. Microsoft Authenticator, Google Authenticator,
   1Password, Bitwarden oder Apple Passwörter.
2. Öffnen Sie in EstrichONE das **Benutzermenü** (Ihre Initialen oben rechts) → **Anmeldung und Sicherheit**.
3. Klicken Sie unter **Zwei-Faktor-Anmeldung** auf **Einrichten**.
4. Scannen Sie den QR-Code mit der App. Alternativ tippen Sie den angezeigten **Schlüssel** von Hand ab.
5. Geben Sie den sechsstelligen Code aus der App ein und klicken Sie auf **Einschalten**.
6. Speichern oder drucken Sie die **Wiederherstellungscodes**. Jeder Code gilt genau einmal, falls das Handy verloren
   geht. Sie werden nicht noch einmal angezeigt.

![Zwei-Faktor-Anmeldung einrichten](bilder/zwei-faktor-einrichten.png "Einrichten der Zwei-Faktor-Anmeldung: QR-Code scannen und den Code aus der App bestätigen")

Auf derselben Seite sehen Sie unter **Angemeldete Geräte und Browser** alle aktiven Anmeldungen mit Programm,
Anmeldezeit und letzter Aktivität. Unbekannte Anmeldungen beenden Sie mit **Abmelden** oder auf einen Schlag mit
**Überall sonst abmelden** – und ändern danach Ihr Passwort.

> **Wichtig:** Handy verloren? Melden Sie sich mit einem Wiederherstellungscode an und richten Sie die App neu ein.
> Haben Sie keinen Code mehr, setzt die Administration die Zwei-Faktor-Anmeldung in der Benutzerverwaltung zurück
> (**Zwei-Faktor-Anmeldung zurücksetzen (Handy verloren)**, Abschnitt 9.1).

## Die Oberfläche im Überblick

![Startseite mit Bedienelementen](bilder/start.png "Die Startseite „Übersicht“ mit Kopfzeile, Seitennavigation und Cards")

| Nr. | Element | Funktion |
|---|---|---|
| 1 | **Suche** | Sucht gleichzeitig in Bauakten (auch Altakten), Kunden und Dokumenten. |
| 2 | **Neue Bauakte** | Startet den Assistenten für eine neue Bauakte oder Anfrage (Abschnitt 3.2), auch mit <kbd>Alt</kbd> + <kbd>N</kbd>. |
| 3 | **Glocke** | Benachrichtigungen mit Anzahl der ungelesenen (Abschnitt 8.3). |
| 4 | **Chat** | Öffnet den Chat; eine Zahl zeigt ungelesene Nachrichten (Abschnitt 8.2). |
| 5 | **Telefon** | Schnellerfassung eines Anrufs während des Gesprächs (Abschnitt 4.2). Die Zahl zeigt fällige Wiedervorlagen. |
| 6 | **Benutzermenü** | Einstellungen, Benachrichtigungen, Passwort, Anmeldung und Sicherheit, Abmelden. |
| 7 | **Seitennavigation** | Alle Bereiche, gruppiert nach Auftragsbearbeitung, Planung, Material, Büro, Personal, IT Center und System. |
| 8 | **Cards** | Das Wichtigste für Sie auf einen Blick, jeweils mit Sprung in die passende Liste. |

Unten zeigt die **Statuszeile**, mit welchem Server Sie verbunden sind, dessen Version sowie Ihren Namen und das Datum.
Bricht die Verbindung ab, steht dort „Keine Verbindung zum Server“; EstrichONE versucht es selbstständig erneut.

### Suche

Tippen Sie zwei oder mehr Zeichen in das Suchfeld. Die Vorschläge zeigen Bauakten mit Nummer und Ort, Kunden und
Dokumente mit zugehöriger Bauakte. Ein Klick öffnet den Treffer; <kbd>Enter</kbd> zeigt alle Treffer in der
Bauaktenliste.

![Suche mit Vorschlägen](bilder/suche.png "Vorschläge der Suche: Bauakte, Kunde und Dokumente zu „sonn“"){halb}

### Navigation

Die Seitennavigation lässt sich mit dem Menüsymbol links oben ein- und ausklappen. Gruppen öffnen und schließen Sie mit
dem Pfeil neben dem Gruppennamen. Jede Seite hat eine eigene Adresse: Sie können sie als Lesezeichen speichern, in einem
neuen Tab öffnen (<kbd>Strg</kbd> + Klick) oder per E-Mail an Kollegen schicken.

### Benutzermenü und Einstellungen

![Benutzermenü](bilder/benutzermenue.png "Das Benutzermenü hinter den Initialen oben rechts"){klein}

Unter **Einstellungen** wählen Sie die **Dichte der Oberfläche** (**Kompakt** für Maus und Tastatur, **Komfortabel
(Touch)** für Tablets) und das **Farbschema**. Zur Wahl stehen die Themes *Horizon* und *Quartz* jeweils hell, dunkel
und mit hohem Kontrast (Schwarz oder Weiß). **Systemeinstellung folgen** schaltet automatisch zwischen Morgenhorizont
(hell) und Abendhorizont (dunkel) um, je nach Einstellung Ihres Betriebssystems. Die Auswahl wirkt sofort und gilt für diesen
Browser. Solange Sie nichts wählen, gilt die Vorgabe der Administration (Abschnitt 9.6, Darstellung und Karte).

![Einstellungen](bilder/einstellungen-dialog.png "Dichte und Farbschema der Oberfläche")

Unter **Benachrichtigungen einrichten** legen Sie fest, welche Ereignisse ins Postfach kommen und welche zusätzlich als
Pushnachricht im Browser oder in der App erscheinen (Abschnitt 8.3).

## Die Startseite

Die **Übersicht** begrüßt Sie mit Ihrem Namen und dem Datum. Darunter stehen die Cards. Welche Cards erscheinen, hängt
von Ihren Rechten ab:

| Card | Inhalt |
|---|---|
| **Meine Aufgaben** | Offene Aufgaben und Mängel, die Ihnen zugewiesen sind, mit Fälligkeit |
| **Einsätze heute** | Die heutigen Einsätze aller Kolonnen mit Uhrzeit; Hinweise auf Konflikte |
| **Warten auf Genehmigung** | Dokumente, die Sie freigeben sollen |
| **Offene Tickets** | Helpdesk-Meldungen, auch „kann nicht arbeiten“ (für das Helpdesk-Team) |
| **Benachrichtigungen** | Ungelesene Meldungen (statt Tickets, wenn Sie nicht im Helpdesk arbeiten) |
| **Fristen** | Fällige Aufgaben und Fristen der nächsten 7 Tage |
| **Neue Bauakten** | Bauakten im Status Anfrage |

Der **Schnellzugriff** führt direkt zu den häufigsten Seiten. Rechts zeigt die Card **Sicherung** den Stand der letzten
Datensicherung und Warnungen der Überwachung (für Administratoren), darunter stehen **Kennzahlen** wie Baustellen in
Ausführung und offene Urlaubsanträge. **Aktualisieren** lädt alle Cards neu.

> **Hinweis:** Fällt der Backup- oder Archivserver aus, erscheint für alle Benutzer ein Hinweis oben auf der Seite, damit
> die Administration schnell Bescheid bekommt.

## Abmelden

Benutzermenü → **Abmelden**. Haben Sie **Angemeldet bleiben** gewählt, bleibt die Sitzung sonst auf diesem Gerät
bestehen. Wie lange Sitzungen im Browser ohne Aktivität gültig bleiben, stellt die Administration ein (Abschnitt 9.6).
