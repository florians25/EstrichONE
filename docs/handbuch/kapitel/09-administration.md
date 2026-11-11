# Administration

Dieses Kapitel richtet sich an Inhaber und Administratoren. Die Verwaltungsseiten sind in der Seitennavigation nach
Themen gruppiert und brauchen besondere Rechte (z. B. „Benutzer verwalten“, „Einstellungen verwalten“, „Backups
verwalten“). Jede Änderung wird im **Protokoll** festgehalten (Abschnitt 9.14).

| Gruppe | Seiten |
|---|---|
| **Organisation** | Verwaltung (Übersicht), Systemeinstellungen, Stammdaten, Module |
| **Benutzer & Geräte** | Benutzer, Rollen und Rechte, Geräte, Helpdesk |
| **Datensicherung & Archiv** | Sicherung (Backup Center), Backup-Ziele, Archiv, Aktenarchiv |
| **System & Betrieb** | Überwachung, Speicher, Hinweise & Wartung, Protokoll |
| **Integrationen** | E-Mail, Pushnachrichten |

**Organisation** → **Verwaltung** zeigt alle Bereiche, die Sie sehen dürfen, als Karten – je Bereich mit kurzer
Beschreibung und, wo sinnvoll, dem Zustand (z. B. Sicherung „aktuell“, Archiv „verbunden“, Helpdesk „2 neu“). Das
Suchfeld findet Bereiche auch über Stichwörter: „Logo“ führt zu den Systemeinstellungen, „Push“ zu den
Pushnachrichten, „Passwort“ zu den Benutzern. Mit <kbd>Enter</kbd> öffnet sich der Bereich, wenn genau einer passt.
Auch die Suche oben in der Kopfzeile schlägt passende Verwaltungsbereiche vor.

![Verwaltung](bilder/verwaltung.png "Verwaltungsübersicht mit Suche, Gruppen und Zustand je Bereich")

Alle Verwaltungsseiten folgen demselben Aufbau: Titel mit Kurzbeschreibung, die Hauptaktion oben rechts, darunter die
**Statuszeile** – grün „Alles in Ordnung“ mit einer kurzen Zusammenfassung, gelb oder rot „Handlungsbedarf“ mit den
offenen Punkten (z. B. „Noch kein Backup-Ziel gekoppelt“, „Letzte Testmail fehlgeschlagen“). Aktionen mit großer
Tragweite (Archivserver entkoppeln, Wartungsmodus starten) stehen getrennt am Seitenende unter **Gefährliche
Aktionen** bzw. in einem rot umrandeten Abschnitt. Lange Seiten wie das Archiv bieten oben Sprungmarken.

> **Wichtig:** Für Konten mit Verwaltungsrechten sollte die Zwei-Faktor-Anmeldung eingerichtet sein – erst recht, wenn
> EstrichONE aus dem Internet erreichbar ist. Richten Sie sie für sich selbst ein (Abschnitt 2.2), bevor Sie sie unter
> **Anmeldung** zur Pflicht machen.

## Benutzer

**Benutzer & Geräte** → **Benutzer** verwaltet die Konten, ihre Rollen und Zugänge.

![Benutzer](bilder/benutzer.png "Benutzerliste mit Rollen, Status, letzter Anmeldung und Aktionen")

### Benutzer anlegen

1. **Benutzer anlegen**.
2. **Name**, **Anmeldename** (für Mitarbeiter ohne E-Mail-Adresse z. B. „m.huber“), optional **E-Mail** und **Telefon**.
3. Eine oder mehrere **Rollen** wählen.
4. **Anlegen**. EstrichONE erzeugt ein **Einmalpasswort** und zeigt es an: **Kopieren** übernimmt es in die
   Zwischenablage, **Zugangsbrief (PDF)** erstellt einen Brief nach DIN 5008 mit Anmeldedaten und Anleitung. Ist ein
   E-Mail-Konto eingerichtet, geht der Zugangsbrief zusätzlich per E-Mail an den Benutzer.

![Benutzer anlegen](bilder/benutzer-anlegen.png "Dialog Benutzer anlegen")

Beim ersten Anmelden muss der Benutzer ein eigenes Passwort festlegen.

### Aktionen je Benutzer

| Aktion | Wirkung |
|---|---|
| **Bearbeiten** | Name, Anmeldename, E-Mail, Telefon, Rollen |
| **Passwort zurücksetzen** | Neues Einmalpasswort mit Zugangsbrief, z. B. wenn jemand sein Passwort vergessen hat |
| **Zwei-Faktor-Anmeldung zurücksetzen (Handy verloren)** | Schaltet die Zwei-Faktor-Anmeldung des Benutzers ab, damit er sie neu einrichten kann |
| **Sperren** / **Entsperren** | Sperrt das Konto sofort (z. B. beim Ausscheiden); die Daten bleiben erhalten |
| **Chatdaten (Auskunft, Löschung)** | Datenschutz-Auskunft und Löschung der Chatdaten einer Person |

Das Schild **2FA** zeigt, wer die Zwei-Faktor-Anmeldung eingeschaltet hat. Benutzer werden nicht gelöscht, sondern
gesperrt – so bleiben Verlauf und Protokoll nachvollziehbar.

## Rollen und Rechte

**Benutzer & Geräte** → **Rollen und Rechte** zeigt die **Rechtematrix**: Zeilen sind die Rechte (gruppiert nach Bauakten,
Kontakte, Dokumentation, Planung, Personal, Material, Verwaltung …), Spalten die Rollen.

![Rollen und Rechte](bilder/rollen.png "Die Rechtematrix mit den vier Standardrollen")

- Setzen oder entfernen Sie Häkchen und übernehmen Sie die Änderungen mit **Änderungen speichern** – oder **Verwerfen**.
- **Rolle anlegen** erstellt eine eigene Rolle, z. B. „Disposition“ oder „Buchhaltung“. Der Stift über einer Spalte
  ändert Name und Beschreibung.
- **Recht suchen** findet ein Recht nach Name oder Kürzel (z. B. `documents.approve`).
- Die Rolle **Administrator** hat immer alle Rechte.

Wichtige Rechte im Überblick:

| Recht | Bedeutung |
|---|---|
| Eigene Bauakten sehen / Alle Bauakten sehen | Nur Bauakten, bei denen man Mitglied oder zuständig ist – oder alle |
| Bauakten anlegen, bearbeiten, löschen, Kollegen zuordnen | Arbeiten an Bauakten |
| Fotos, Videos und Dokumente hochladen bzw. löschen | Ablage |
| Über alle Dokumentfreigaben entscheiden | Auch fremde Freigaben erledigen |
| Planung sehen bzw. planen | Plantafeln, Cockpit, Freigabe des Plans |
| Personal sehen / verwalten, Urlaub beantragen / genehmigen | Personalbereich |
| Benutzer verwalten, Rollen und Rechte verwalten | Benutzerverwaltung |
| Geräte sehen / verwalten / sperren | Benutzer & Geräte → Geräte |
| Einstellungen verwalten, Backups verwalten, Protokoll lesen | System |
| Chat nutzen / Chat verwalten | Chat |

## Stammdaten

**Organisation** → **Stammdaten** pflegt die Grundlagen, auf denen Formulare und Listen aufbauen:

- **Wertelisten** – Auswahllisten wie Objektart, Estrichart, Festigkeitsklasse, Aufbau, Dämmung, Heizungsart, Priorität
  und Dokumentart, jeweils mit Bezeichnung, Kurzform, Schlüssel, Reihenfolge und Aktiv-Schalter.
- **Formulare** – die Felder der **Basisdaten** (Kennwerte der Bauakte) und der **Erstbegehung** (Prüfpunkte,
  Messwerte). Mit **Feld hinzufügen** und **Gruppe anlegen** passen Sie die Formulare an Ihren Betrieb an.
- **Textbausteine** – wiederkehrende Texte für Notizen, Helpdesk-Antworten und Schreiben.
- **Ordnervorlagen** – Unterordner, die in jeder neuen Bauakte angelegt werden.
- **Bauakten-Vorlagen** – Vorlagen mit Ordnern, Räumen und Kennwerten für typische Objekte (z. B. „Einfamilienhaus mit
  Fußbodenheizung“), auswählbar im Assistenten Neue Bauakte.

![Stammdaten](bilder/stammdaten.png "Stammdaten: Auswahllisten, Formulare, Vorlagen und Texte")

## Geräte und App-Einrichtung (QR-Code) {#geraete}

**Benutzer & Geräte** → **Geräte** listet alle iPhones, iPads, Android-Geräte und Windows-Arbeitsplätze, die mit EstrichONE
verbunden sind – mit Nutzung, Status, App-Version und Betriebssystem sowie dem Zeitpunkt der letzten Verbindung.

So richten Sie ein neues Gerät ein:

1. Klicken Sie auf **Gerät einrichten**.
2. Wählen Sie die **Nutzung**:
   - **Persönliches Gerät** – gehört einer Person; sie meldet sich mit ihrem eigenen Konto an.
   - **Poolgerät (mehrere Personen)** – z. B. das iPad einer Kolonne; mehrere Mitarbeiter melden sich nacheinander an
     und entsperren mit ihrer Pool-PIN.
3. Optional einen **Gerätenamen** (z. B. „iPad Kolonne Huber“) und **Code erzeugen**.
4. In der App **QR-Code scannen** wählen und den angezeigten Code scannen – oder den **Code zum Abtippen** eingeben.
   Der Code gilt nur kurz und nur einmal; die Uhrzeit steht darunter.

![Gerät einrichten](bilder/geraet-einrichten.png "Gerät einrichten: Nutzung wählen")

![QR-Code zur Einrichtung](bilder/geraet-qr.png "QR-Code und Code zum Abtippen für die App"){halb}

Ein mit diesem Code eingerichtetes Gerät ist sofort freigegeben. Geräte, die sich selbst angemeldet haben, geben Sie mit
**Freigeben** frei. **Sperren** sperrt ein verlorenes oder gestohlenes Gerät sofort, **Entsperren** hebt das auf, **Entfernen** meldet es endgültig ab, **Neu einrichten** erzeugt einen
neuen Code für dasselbe Gerät.

> **Hinweis:** Für den QR-Code muss die öffentliche Serveradresse konfiguriert sein (`ESTRICHONE_DOMAIN`). Fehlt sie,
> zeigt der Dialog einen Hinweis. Den QR-Code mit Zugangsdaten enthält auch das Onboarding-PDF neuer Mitarbeiter
> (Kapitel 6).

## App-Updates {#app-updates}

**Benutzer & Geräte** → **App-Updates** (auch unter Systemeinstellungen → Weitere Bereiche) zeigt, welche App-Versionen
bereitstehen, welche Regeln gelten und welche Geräte eine ältere App haben.

**Verfügbare Versionen.** Die Android-App liefert der Server selbst aus – sie ist im Server-Update enthalten und hat
immer dieselbe Version wie der Server. Die Seite zeigt Version, Signatur (Release oder Testversion) und die geprüfte
Prüfsumme. **Android-App laden** zeigt einen QR-Code mit dem Download-Link für die Erstinstallation. iPhone und iPad
aktualisieren über App Store, TestFlight oder die Geräteverwaltung Ihres Betriebs (MDM).

**Regeln:**

- **Hinweis „Update verfügbar“** – die Apps zeigen oben einen Hinweis, sobald eine neuere Version bereitsteht.
- **Mindestversion erzwingen** – ältere Apps zeigen nur noch den Bildschirm „Update erforderlich“. Leer gelassen gilt
  die Version des Servers. Die Seite nennt vorab, wie viele Geräte das sofort betrifft. Nicht übertragene Änderungen
  bleiben auf den Geräten und werden nach dem Update gesendet.
- **Hinweis an die Benutzer** – eigener Text, z. B. „Bitte bis Freitag aktualisieren“.
- **iPhone und iPad: Link zum Update** – App-Store-, TestFlight- oder MDM-Link. **Dort verfügbare Version**: die Version,
  die über diesen Link tatsächlich bereitsteht (leer = Serverversion). Die Mindestversion liegt nie darüber.
- **Android: Eigener Download** – nur für eine Geräteverwaltung; ohne Eintrag lädt die App das Update vom Server.

**Geräte.** Die Liste zeigt je Gerät Person, App-Version, Stand (**Aktuell**, **Update verfügbar**, **Gesperrt – Update
nötig**, **Neuer als der Server**) und letzte Verbindung. **Zum Update auffordern** schickt der Person eine
Benachrichtigung (einmal je Version). Steht dort **Neuer als der Server**, aktualisieren Sie den Server (Abschnitt
„Updates und Sicherheitsupdates“).

> **Hinweis:** Android installiert ein Update nur, wenn es mit demselben Schlüssel signiert ist wie die installierte App.
> Zeigt die Seite „Testversion (Debug-Signatur)“, fehlt beim Herausgeber der Signaturschlüssel; Updates sind dann nur
> nach Deinstallation möglich.

## Firmendaten und Logo

**Organisation** → **Systemeinstellungen** → **Firma** enthält die Firmendaten für den Briefkopf: Firmenname, Straße, PLZ, Ort,
Telefon, E-Mail und Website.

![Firmendaten](bilder/systemeinstellungen.png "Systemeinstellungen, Reiter Firma mit Firmenlogo")

Unter **Firmenlogo** laden Sie Ihr Logo hoch (**Logo hochladen …**; PNG, JPEG oder WebP, am besten quer mit
transparentem Hintergrund). Es erscheint im Web in der Kopfzeile und auf allen PDFs; das EstrichONE-Logo bleibt
zusätzlich klein in der Fußzeile. **Entfernen** nimmt es wieder heraus. Änderungen übernehmen Sie mit **Speichern** oben
rechts.

## Systemeinstellungen {#systemeinstellungen}

Die **Systemeinstellungen** gelten für alle Benutzer. Links stehen die Bereiche in vier Gruppen (Firma & Auftritt,
Arbeitsweise, Zugang & Meldungen, Technik), rechts die Einstellungen des gewählten Bereichs mit Überschrift und
Kurzbeschreibung. **Speichern** oben rechts übernimmt die Änderungen aller Bereiche. Die Statuszeile meldet fehlende
Angaben für den Briefkopf (Anschrift, Telefon oder E-Mail) und weist auf ein fehlendes Logo oder Apple Push hin.
Speicher, Module und Pushnachrichten haben zusätzlich einen eigenen Eintrag in der Seitennavigation.

| Bereich | Einstellungen |
|---|---|
| **Firma** | Firmendaten (Briefkopf) und Firmenlogo |
| **Bauakten** | Hinweis „Erstbegehung fehlt“, vorgeschlagene Vorlage, Status-Automatik („Geplant“ → „In Ausführung“ am ersten Einsatztag oder bei erster Aktivität vor Ort, nach Abnahme abschließen), Gültigkeit des Freigabelinks für Kunden |
| **Nummernkreise** | Nummern aller Datensätze in einer Tabelle (Datensatz, Muster, nächste Nummer, Bereich, Vorschau, verbleibend mit Warnung): Bauakten `{JJJJ}-{NNN}`, Kunden `60{NNNN}`, Lieferanten `70{NNNN}`, Personal `10{NNN}`, Maschinen und Fahrzeuge `20{NNN}`, Kolonnen `K{NN}`, Material `50{NNNN}`, Bestellungen `B-{JJJJ}-{NNNN}`, Anfragen `A-{JJJJ}-{NNNN}`, Aufgaben `AU-{JJJJ}-{NNNN}`, Mängel `M-{JJJJ}-{NNNN}`, Helpdesk-Tickets `T-{JJJJ}-{NNNN}`, Fehler und Wünsche `F-{JJJJ}-{NNNN}`. Muster mit Jahr zählen jedes Jahr neu. Die Nummer vergibt der Server beim Anlegen (Web, App, Import); vergebene Nummern werden übersprungen, vorhandene nie geändert. Von Hand vergeben dürfen Nummern nur Administratoren (Kunden- und Personalnummern auch die Sachbearbeitung). Änderungen stehen im Protokoll. |
| **Dokumente und Freigaben** | Vorgeschlagene Frist und Rolle für Freigaben, Kommentar beim Freigeben verlangen, Verweis auf Ordner- und Bauakten-Vorlagen und Aufbewahrungsfristen |
| **Anmeldung** | Sitzungen im Browser (Abmelden nach Leerlauf, Dauer von „Angemeldet bleiben“), Zwei-Faktor-Pflicht für Administratoren, wie lange Apps auf Smartphones und Tablets ohne Nutzung angemeldet bleiben |
| **Benachrichtigungen** | Welche E-Mails verschickt werden, Web Push, Inhalt in Pushnachrichten, Apple Push (APNs) für die iOS-App |
| **Darstellung und Karte** | Farbschema und Dichte als Vorgabe für neue Benutzer, Kachelserver und Quellenangabe der Karte |
| **PDF-Layout** | Firmenlogo im Kopf, EstrichONE-Logo in der Fußzeile, Akzentfarbe, zusätzliche Fußzeile (Bankverbindung, Registergericht, USt-IdNr.), Fotos je Zeile |
| **Speicher** | Speicherschonende Ablage von Fotos und PDFs, Belegung, Nachverdichten |
| **Module** | Alle Fachmodule (Planung, Material, Personal, Urlaub, Anrufe, Protokolle, Abnahmen, Aufgaben, Mängel, Helpdesk, Chat …) ein- und ausschalten |
| **Weitere Bereiche** | Sprungziele zu Sicherung, Backup-Zielen, Archiv, E-Mail und Protokoll |

### Anmeldung

![Anmeldung](bilder/einstellungen-anmeldung.png "Reiter Anmeldung: Sitzungen, Zwei-Faktor-Pflicht, Apps")

Bei Aktivität verlängert sich eine Sitzung von selbst. Läuft sie ab, erscheint ein Anmeldedialog – Eingaben bleiben
erhalten. **Pflicht für Administratoren** bewirkt, dass Konten mit Verwaltungsrechten (Benutzer, Rollen, Einstellungen,
Backup, Geräte, Protokoll, Archiv) diese Rechte erst nutzen, wenn sie eine Authenticator-App eingerichtet haben.
Registrierte Apps bleiben angemeldet, solange sie innerhalb der eingestellten Frist genutzt werden; entsperrt wird mit
Face ID/Touch ID, Gerätecode oder Pool-PIN.

### Benachrichtigungen und Push (APNs) {#push}

Jede Benachrichtigung landet im Postfach (Glocke). Zusätzlich erscheint sie als Pushnachricht im Browser (**Web Push**)
und in der iOS-App über den Apple Push Notification Service (**APNs**). Mit **Inhalt in Pushnachrichten zeigen** = aus
steht auf dem Sperrbildschirm nur „Neue Benachrichtigung“.

![Benachrichtigungen und Push](bilder/einstellungen-push.png "Reiter Benachrichtigungen: E-Mails, Web Push und APNs")

Damit die iOS-App Pushnachrichten erhält, tragen Sie unter **Apple Push (APNs) für die iOS-App** ein:

1. **APNs verwenden** einschalten.
2. **Key-ID** und **Team-ID** (je 10 Zeichen) aus dem Apple-Developer-Konto.
3. **Bundle-ID der App** (Standard `de.estrichone.EstrichONE`).
4. **Umgebung**: **Produktion (App Store, TestFlight)** oder **Sandbox (Xcode)**.
5. Den **Schlüssel (.p8)** hochladen (**.p8-Datei wählen …**). Er wird verschlüsselt gespeichert.
6. **Speichern** und mit **Testnachricht an mich** prüfen.

Unter **Registrierte Geräte** sehen Sie, wie viele Browser, iPhones/iPads und Android-Geräte Pushnachrichten
empfangen. Eine ausführliche Anleitung liegt im Repository unter `docs/benachrichtigungen.md`.

### Speicher

![Speicher](bilder/einstellungen-speicher.png "Reiter Speicher: Bildqualität, längste Kante, PDF-Verdichtung und Belegung")

**Fotos und Dokumente speicherschonend ablegen** gilt für alle Uploads aus Web und Apps: Fotos werden als JPEG neu
kodiert, nach EXIF gedreht und bei Bedarf verkleinert (**Längste Kante**, empfohlen 4096 px für Druck A4); Aufnahmezeit
und GPS bleiben erhalten. **PDFs verlustfrei verdichten** packt unkomprimierte Inhalte; signierte oder verschlüsselte
PDFs und Office-Dateien bleiben bitgenau. **Fotos unverändert speichern** schaltet die Verdichtung für Fotos ab.

Die **Belegung** zeigt gespeicherte Größe, Ersparnis, Dubletten und den Platz auf dem Datenträger je Dateiart. **Bestehende
Dateien nachverdichten** wendet die Einstellungen nachträglich auf ältere Dateien an.

### Module

![Module](bilder/einstellungen-module.png "Reiter Module: Karten je Bereich mit Schalter")

Jedes Fachmodul hat eine Karte mit Schalter, gruppiert nach Bereichen (Büro, Planung, Material, Personal, Baustelle und
Dokumentation, Kommunikation und Service). Die Schalter wirken **sofort** für alle Benutzer in Web und Apps:

- **Ausschalten** fragt nach. Das Modul verschwindet aus Navigation, Startseite, Suche, Bauakte, Tagesübersicht und den
  Apps; seine Benachrichtigungen (Postfach, Push, E-Mail) entfallen. Wer eine Seite eines abgeschalteten Moduls direkt
  aufruft (Lesezeichen, alter Link), sieht „Modul deaktiviert“. Die Schnittstelle antwortet mit `MODULE_DISABLED`.
  **Die Daten bleiben erhalten** und sind nach dem Einschalten wieder da. Administratoren können vorhandene Daten weiter lesen.
- **Abhängigkeiten** stehen auf der Karte („Benötigt: …“, „Wird benötigt von: …“): *Urlaub und Abwesenheit* braucht
  *Personal*, *Bestellungen* braucht *Material und Lieferanten*, *Maschinen und Fahrzeuge* braucht *Disposition und
  Kolonnenplanung*. Abhängige Module gehen beim Ausschalten mit aus; beim Einschalten kommen fehlende Voraussetzungen
  nach Rückfrage mit.
- **Immer aktiv** sind Bauakten, Kunden und Kontakte, Benutzer und Rechte, Einstellungen und Sicherung.
- Jede Änderung steht im **Protokoll** („settings.modules_changed“).
- **Chat**: Schalter auf der Karte unter „Kommunikation und Service“; darunter **Löschfrist (Tage)**, **Vorschau in
  Pushnachrichten** (aus empfohlen: nur „Neue Nachricht von …“), **Mitarbeiter untereinander** (aus: Mitarbeiter ohne
  Recht „Chat verwalten“ können Einzelchats nur mit dem Büro beginnen). **Bestand** zeigt Umfang und letzte Löschung;
  Auskunft und Löschung je Person erledigen Sie in der Benutzerverwaltung.

## E-Mail

**Integrationen** → **E-Mail** richtet das E-Mail-Konto ein, über das EstrichONE Benachrichtigungen (Freigaben, Helpdesk),
Zugangsbriefe und Fehlermeldungen verschickt.

![E-Mail](bilder/e-mail.png "E-Mail-Konto mit SMTP-Server, Verschlüsselung und Testmail")

1. **E-Mail-Versand** einschalten.
2. **SMTP-Server** (z. B. smtp.ionos.de, smtp.office365.com), **Port** und **Verschlüsselung** (STARTTLS auf Port 587 ist
   üblich, SSL/TLS auf Port 465, „Keine“ nur für ein internes Relay).
3. **Benutzername** (meist die E-Mail-Adresse) und **Passwort** – es wird verschlüsselt gespeichert und nie wieder
   angezeigt.
4. **Absenderadresse**, **Absendername** und optional **Antworten an**.
5. **Speichern** und mit **Testmail senden** prüfen.

Im Reiter **Vorlagen** passen Sie Betreff und Text jeder E-Mail an (**Standardtext wiederherstellen** setzt zurück). Das
**Versandprotokoll** zeigt jede verschickte E-Mail mit Status; fehlgeschlagene lassen sich **erneut senden**.

## Backup Center {#backup}

**Datensicherung & Archiv** → **Sicherung** ist das **Backup Center**. EstrichONE sichert täglich vollständig: Datenbank (alle Bauakten,
Kontakte, Planung, Zeiten, Benutzer, Archiv-Nachweise), alle Dateien (Fotos, Dokumente, Videos, Pläne – Originale und
Vorschaubilder) und die Einstellungen (Konfiguration des Servers, Zeitplan, Backup-Ziele, Schlüsselbund). Die Sicherung
ist mit AES-256 verschlüsselt, jeder Block hat eine Prüfsumme; wöchentlich wird geprüft und probeweise wiederhergestellt.

![Backup Center](bilder/sicherung.png "Backup Center, Übersicht: letzte Sicherung, Prüfungen, Zeitplan und Inhalt")

Die **Übersicht** zeigt letzte Sicherung, letzte erfolgreiche Sicherung, Integritätsprüfung, Wiederherstellungstest und
Zeitplan. **Jetzt sichern** startet sofort eine zusätzliche Vollsicherung.

### Sicherungen und Wiederherstellen

Der Reiter **Sicherungen** listet alle Stände mit Zeitpunkt, Art, Umfang und Kennung – je **Quelle** (Hauptziel auf
diesem Server oder ein Backup-Ziel).

![Sicherungen](bilder/sicherung-staende.png "Alle Sicherungsstände mit Umfang und Kennung")

- Das Dokumentsymbol holt eine **einzelne Datei** (Foto, Dokument) aus dieser Sicherung zurück – ohne den Rest zu
  verändern.
- **Wiederherstellen** setzt das **ganze System** auf diesen Stand zurück: Datenbank, Fotos, Dokumente, Videos und
  Einstellungen. Zur Sicherheit tippen Sie `WIEDERHERSTELLEN` ein.

![System wiederherstellen](bilder/sicherung-wiederherstellen.png "Sicherheitsabfrage vor der vollständigen Wiederherstellung")

> **Achtung:** Änderungen nach dem gewählten Stand gehen verloren; die bisherige Datenbank bleibt als Reserve auf dem
> Server. Während der Wiederherstellung ist EstrichONE für alle gesperrt (Wartungsmodus). Die Apps behalten ungesendete
> Eingaben und gleichen danach neu ab. Alle müssen sich neu anmelden.

### Zeitplan und Meldungen

![Zeitplan und Meldungen](bilder/sicherung-zeitplan.png "Zeitplan, Aufbewahrung und Meldungen der Sicherung")

- **Tägliche Vollsicherung um** (Standard 22:30 Uhr) und **Zusätzlich inkrementell** (aus, stündlich, alle 2/4/6/12
  Stunden).
- **Wöchentliche Probe-Wiederherstellung** in eine temporäre Datenbank.
- **Aufbewahrung** des lokalen Hauptziels: tägliche, wöchentliche, monatliche und jährliche Stände. Gekoppelte
  Backup-Ziele sind „nur anhängen“ und behalten alles.
- **Pushnachrichten an Administratoren**: Fehler, Ausfall des Backup- oder Archivservers und „wieder in Ordnung“ werden
  gemeldet; auf Wunsch **auch erfolgreiche Sicherungen**.

### Notfallblatt {#notfallblatt}

Der Reiter **Notfall** erklärt die Neuinstallation nach einem Totalausfall und bietet das **Notfallblatt (PDF)** an.

![Notfall](bilder/sicherung-notfall.png "Reiter Notfall mit Notfallblatt und Befehl für die Neuinstallation")

> **Wichtig:** Drucken Sie das Notfallblatt **jetzt** aus und bewahren Sie es getrennt vom Server auf (Tresor). Es
> enthält den **Wiederherstellungsschlüssel** – ohne ihn sind die Sicherungen wertlos.

Server komplett ausgefallen? 1. Neuen Linux-Server mit Docker bereitstellen und die Domain auf ihn zeigen lassen.
2. Den Befehl aus dem Reiter Notfall ausführen (**Befehl kopieren**). 3. Quelle (Ordner oder Zugang vom Notfallblatt)
und Wiederherstellungsschlüssel eingeben – der Installer holt die letzte Sicherung, stellt alles her und startet
EstrichONE.

## Backup-Ziele {#backup-ziele}

**Datensicherung & Archiv** → **Backup-Ziele** richtet eine zusätzliche Sicherung auf einem **anderen Rechner** ein (NAS, alter PC,
Raspberry Pi). Die Ziele arbeiten „nur anhängen“: Löschen ist dort unmöglich – Schutz vor Brand, Diebstahl und
Verschlüsselungstrojanern.

![Backup-Ziele](bilder/backup-ziele.png "Backup-Ziele: koppeln, Ziele, die ziehen, und Aktualisierung der Zusatzserver")

1. Auf dem Zielrechner (Linux mit Docker) einmal ausführen:
   `sudo docker run --rm -v /var/run/docker.sock:/var/run/docker.sock ghcr.io/florians25/estrichone-installer:latest backup-ziel`.
   Er zeigt Adresse und Kopplungscode.
2. In EstrichONE **Backup-Ziel koppeln** wählen und Adresse und Code eingeben.
3. **Notfallzugang** herunterladen und zusammen mit dem Backup-Passwort getrennt vom Server aufbewahren – nur damit lässt
   sich nach einem Totalverlust vom Backup-Ziel wiederherstellen.

**Backup-Ziele, die ziehen** sind für Ziele ohne feste Adresse (zuhause hinter dem Router), wenn dieser Server öffentlich
erreichbar ist (z. B. im Rechenzentrum): Das Ziel verbindet sich selbst und holt die verschlüsselten Sicherungen – keine
Portfreigabe nötig (**Ziel, das zieht, hinzufügen**).

### Kombiserver: Backup-Ziel und Archiv auf einem Rechner {#kombiserver}

Wer nur einen weiteren Rechner hat, richtet dort **Backup-Ziel und Archivserver zusammen** ein – mit einem Befehl und
einem Kopplungscode. Beide Dienste laufen getrennt (eigene Container, eigene Daten), teilen sich aber den Rechner.

1. Auf dem Kombiserver (Linux mit Docker) einmal ausführen:
   `sudo docker run --rm -v /var/run/docker.sock:/var/run/docker.sock -v /opt/estrichone-kombi:/opt/estrichone-kombi ghcr.io/florians25/estrichone-installer:latest backup-archiv`.
   Er zeigt Adresse, Ports (Backup-Ziel 8444, Archiv 8443), **einen** Kopplungscode und beide Fingerabdrücke.
2. In EstrichONE unter **Backup-Ziele** **Kombiserver koppeln** wählen, Adresse und Code eingeben. EstrichONE koppelt
   beide Dienste; bereits gekoppelte (z. B. ein vorhandenes Backup-Ziel, das um das Archiv erweitert wurde) werden
   übersprungen.
3. Fingerabdrücke vergleichen und den **Notfallzugang** des Backup-Ziels aufbewahren.

Steht der Kombiserver zuhause hinter dem Router und dieser Server im Rechenzentrum, wählen Sie **Kombiserver, der zieht**:
EstrichONE zeigt den Befehl mit Code; das Backup-Ziel holt die Sicherungen selbst, das Archiv koppeln Sie im zweiten
Schritt über eine weitergeleitete Adresse (Port 8443) oder eine Tunnel-Adresse – mit demselben Code.

Wichtig: Backup und Archiv auf demselben Rechner ist bequem, aber **kein zweiter Standort für das Archiv**. Stellen Sie
den Kombiserver räumlich getrennt vom EstrichONE-Server auf (anderes Gebäude, zuhause, Rechenzentrum). Status-Banner
(rot Backup, gelb Archiv) und Aktualisierung gelten für beide Dienste getrennt.

### Zusatzserver aktualisieren {#zusatzserver}

Backup-Ziele und Archivserver sind **Zusatzserver** mit eigener Version. Unter **Aktualisierung der Zusatzserver** (auf
den Seiten Backup-Ziele und Archiv) sehen Sie je Zusatzserver die installierte und die verfügbare Version.

- **Aktualisieren** bzw. **Alle aktualisieren** bringt die Zusatzserver per Fernaktualisierung auf die Version dieses
  Servers. Die Daten bleiben erhalten; bei Problemen springt der Rechner automatisch auf die bisherige Version zurück.
- **Automatisch mitaktualisieren**: nach Updates dieses Servers werden die Zusatzserver automatisch nachgezogen.
- Zusatzserver ohne Updater aktualisieren Sie einmal am Rechner mit dem aktuellen Installer (**Befehl für den
  Rechner**), danach geht es per Knopfdruck.

## Archiv (revisionssicher) {#archiv}

**Datensicherung & Archiv** → **Archiv** bewahrt Dokumente revisionssicher nach GoBD auf: unveränderbar, mit Prüfsummen, Hash-Kette und
Löschsperre bis zum Fristende – auf einem eigenen **Archivserver**.

![Archiv](bilder/archiv.png "Archiv: gekoppelter Archivserver, Bestand, Einstellungen und Zusatzserver")

1. Auf dem Archivrechner einmal ausführen:
   `sudo docker run --rm -v /var/run/docker.sock:/var/run/docker.sock ghcr.io/florians25/estrichone-installer:latest archiv`.
2. **Archivserver koppeln** wählen und Adresse und Kopplungscode eingeben.

Danach zeigt die Seite Verbindung, Fingerabdruck, Bestand (Dokumente, Speicher, Journal), letzte Prüfung und
Warteschlange. **Archiv prüfen** kontrolliert die Unversehrtheit, **Export für Betriebsprüfer** erstellt einen Export für
die Außenprüfung.

Unter **Aufbewahrung je Dokumentart** legen Sie fest, ob und wann archiviert wird (**Sofort nach dem Hochladen**,
**Nach der Freigabe**, **Wenn die Bauakte abgeschlossen ist** oder **Nur manuell**) und wie lange die Frist läuft. Die
Vorschläge folgen § 147 AO und § 257 HGB (6 bzw. 10 Jahre ab Jahresende) – maßgeblich ist Ihre steuerliche Beratung.
**Regeln speichern** übernimmt sie. Unter **Archivierte Dokumente (Nachweis)** sehen Sie jedes Dokument mit Stand
(wartend, Fehler, archiviert).

In den **Einstellungen** schalten Sie **Automatisch archivieren** ein, legen fest, nach wie vielen Monaten
abgeschlossene Bauakten komplett ausgelagert werden (**Akten auslagern nach**, 0 = nur manuell), und wer bei Fehlern
benachrichtigt wird.

### Aktenarchiv

**Datensicherung & Archiv** → **Aktenarchiv** zeigt komplett ausgelagerte Bauakten. Sie liegen dauerhaft auf dem Archivserver; beim
**Abrufen** stehen sie 5 Tage im System zur Ansicht bereit. Änderungen sind erst nach **Entsperren** mit Passwort
möglich, danach archivieren Sie sie mit **Jetzt neu archivieren** wieder. **Bauakte archivieren …** lagert eine Akte von
Hand aus.

## Überwachung

**System & Betrieb** → **Überwachung** zeigt den Zustand aller Systeme: Produktivsystem (Platte, Datenbank, Sicherung,
Archiv-Warteschlange), Archivserver und Backup-Ziele. Warnschwellen: Platte ab 85 % (kritisch ab 95 % oder wenn sie in
weniger als 14 Tagen voll wäre), Sicherung älter als 36 Stunden.

![Überwachung](bilder/ueberwachung.png "Überwachung von Produktivsystem, Archivserver und Backup-Ziel")

Ist alles in Ordnung, steht oben „Alle Systeme in Ordnung“. Warnungen erscheinen zusätzlich in der Card **Sicherung**
der Startseite und als Pushnachricht an die Administratoren.

## Hinweise und Wartung {#wartung}

**System & Betrieb** → **Hinweise & Wartung** verwaltet Ankündigungen und den Wartungsmodus dieser Instanz.

![Hinweise und Wartung](bilder/hinweise-wartung.png "Wartungsmodus und Ankündigungen")

- **Neue Ankündigung**: Titel, Text, Art, Zeitraum und Zielgruppe. Ankündigungen erscheinen als Banner im Web, als
  Hinweis in den Apps und optional per E-Mail.
- **Wartung planen …**: Nachricht an die Benutzer, Beginn und Ende. Die geplante Wartung wird vorab angekündigt (Banner)
  und sperrt zum Beginn die Anmeldung. Mit **Administratoren dürfen während der Wartung weiterarbeiten** bleiben Sie
  selbst handlungsfähig.
- **Jetzt starten …** beginnt die Wartung sofort.

![Wartung planen](bilder/wartung-planen.png "Dialog Wartung planen")

Während der Wartung sehen Benutzer die Seite „Wartungsarbeiten“. Sie prüft alle 30 Sekunden, ob es weitergeht.
Hinweise des Betreibers (bei gehosteten Instanzen aus der Hosting-Konsole) erscheinen hier zusätzlich zum Lesen.

## Updates und Sicherheitsupdates {#updates}

EstrichONE prüft regelmäßig, ob eine neue Version verfügbar ist. Administratoren (Recht „Backups verwalten“) sehen
dann oben einen Hinweis:

- **Update verfügbar** (blau) – eine neue Version derselben Versionslinie. Lässt sich je Version ausblenden.
- **Sicherheitsupdate verfügbar** (rot) – bitte zeitnah einspielen. Der Hinweis lässt sich nur für die laufende Sitzung
  ausblenden.
- **Automatisches Update fehlgeschlagen** (gelb) – das Ergebnis der Automatik, mit Verweis auf das Protokoll.

Der Hinweis nennt den Befehl für den Server und verlinkt die **Versionshinweise**. Aktualisiert wird auf dem Server:

`sudo /opt/estrichone/update.sh 0.1.2` (Versionsnummer wie im Hinweis)

`update.sh` sichert vorher, wartet auf gesunde Dienste und prüft die gemeldete Version. Sicherheitsupdates lassen sich
automatisch einspielen:

| Befehl | Wirkung |
|---|---|
| `sudo /opt/estrichone/update.sh --automatik sicherheit` | nur Sicherheitsupdates, nachts 02:00–05:00 Uhr |
| `sudo /opt/estrichone/update.sh --automatik patch 01:00-04:00` | alle Fehlerbehebungen der Versionslinie im gewählten Zeitfenster |
| `sudo /opt/estrichone/update.sh --automatik aus` | Automatik ausschalten |
| `sudo /opt/estrichone/update.sh --pruefen` | nur prüfen |

Neue Versionslinien (z. B. von 0.1 auf 0.2) werden nie automatisch eingespielt. Nach einem Update des Hauptservers
ziehen Sie die Zusatzserver nach (Abschnitt 9.9).

Mit dem Server-Update steht auch die neue Android-App bereit: Die Apps zeigen den Hinweis „Update verfügbar“ von selbst
(siehe „App-Updates“). Für iPhone und iPad die neue Version über App Store, TestFlight oder MDM verteilen und unter
App-Updates eintragen.

## Protokoll

**System & Betrieb** → **Protokoll** zeigt nachvollziehbar, wer wann was getan hat: Zeitpunkt, Ereignis mit Details, Bereich,
Person, Objekt und IP-Adresse. Einträge lassen sich nicht ändern oder löschen. Filtern Sie nach Bereich, Person und
Zeitraum; **Bauakte öffnen** springt zum betroffenen Objekt, **Weitere laden** holt ältere Einträge. Ereignisse, Details
und Werte stehen auf Deutsch, Personen mit Namen statt interner Kennung.

![Protokoll](bilder/protokoll.png "Das Protokoll mit Ereignissen, Personen und IP-Adressen")
