# Installation und Einrichtung

Die Einrichtung dauert etwa eine Minute: App laden, Server verbinden, festlegen, wer das Gerät nutzt, und anmelden.
Ab dann bleiben Sie angemeldet und entsperren nur noch mit Face ID, Gerätecode oder – auf Poolgeräten – mit Ihrer PIN.

## App installieren

1. Öffnen Sie auf dem iPhone oder iPad den **App Store** und suchen Sie nach **EstrichONE**.
2. Tippen Sie auf **Laden**. Die App ist kostenlos und braucht iOS bzw. iPadOS 17 oder neuer.
3. Öffnen Sie die App. Es erscheint der Bildschirm **Willkommen bei EstrichONE**.

Die App fragt Berechtigungen (Kamera, Mikrofon, Standort, Mitteilungen …) erst dann ab, wenn Sie die zugehörige
Funktion zum ersten Mal benutzen – nie ungefragt beim Start. Eine Übersicht steht in Abschnitt 8.7.

## Mit dem Server verbinden

![Willkommen](bilder/iphone-start.jpg "Willkommen: QR-Code scannen, Adresse eingeben oder Demo")
![Serveradresse eingeben](bilder/iphone-start-adresse.jpg "Serveradresse eingeben und Verbindung prüfen")

Der Willkommensbildschirm bietet drei Wege:

- **QR-Code scannen** – der schnellste Weg. Ihre Administration erzeugt im Web unter **IT Center** → **Geräte** →
  **Gerät einrichten** einen Einrichtungscode als QR-Code. Richten Sie die Kamera darauf; die App liest Serveradresse
  und Code in einem Schritt. Der Code gilt nur kurz und nur einmal.
- **Adresse eingeben** – tippen Sie die Serveradresse ein, z. B. `estrichone.ihre-firma.de`. `https://` ergänzt die App
  selbst; die Verbindung ist immer verschlüsselt. Tippen Sie auf **Verbindung prüfen**.
- **Demo ausprobieren – ohne Server** – startet die App mit Beispieldaten (Abschnitt 2.8).

> **Tipp:** Eine Einrichtung per QR-Code ist nicht nur schneller: Das Gerät ist damit nach der Anmeldung **sofort
> freigegeben**. Ohne Code muss die Administration das Gerät erst im IT Center freigeben (Abschnitt 2.5).

Auf der Seite **Server bestätigen** zeigt die App, womit sie sich verbinden wird: **Adresse**, **Serverversion** und die
Anmeldearten des Servers, bei einem gescannten Code zusätzlich **Einrichtungscode erkannt**. Die Meldung „Verbindung
geprüft. Der Server ist erreichbar und verwendet ein gültiges Zertifikat.“ bestätigt die sichere Verbindung. Tippen Sie
auf **Diesen Server verwenden** – oder auf **Andere Adresse eingeben**, wenn es nicht der richtige Server ist.

> **Wichtig:** Hat der Server kein gültiges Zertifikat, lässt sich die App nicht einrichten. Ein „Trotzdem fortfahren“
> gibt es bewusst nicht. Wenden Sie sich in diesem Fall an Ihre Administration.

## Wie wird das Gerät genutzt?

Bevor Sie sich anmelden, legen Sie fest, wer mit dem Gerät arbeitet:

| Auswahl | Wann | Was das bedeutet |
|---|---|---|
| **Persönliches Gerät** | Nur Sie nutzen das iPhone oder iPad. | Sie bleiben angemeldet und entsperren mit Face ID, Touch ID oder dem Gerätecode. |
| **Gemeinsames Gerät (Poolgerät)** | Mehrere Mitarbeiter nutzen das Gerät, z. B. das iPad im Fahrzeug der Kolonne. | Jeder meldet sich mit einer eigenen PIN an. Daten anderer sind nicht sichtbar. Ein Poolgerät muss von der Administration freigegeben werden. |

Über **Anderer Server** kommen Sie zurück zur Serverauswahl.

## Anmelden

![Anmelden](bilder/iphone-anmelden.jpg "Anmelden mit Anmeldename und Passwort")
![Zwei-Faktor-Anmeldung](bilder/iphone-anmelden-code.jpg "Zwei-Faktor-Anmeldung: Code aus der Authenticator-App")

1. Geben Sie **Anmeldename oder E-Mail** und **Passwort** ein und tippen Sie auf **Anmelden**.
2. Ist Ihr Konto mit der **Zwei-Faktor-Anmeldung** geschützt, fragt die App zusätzlich den **6-stelligen Code** aus Ihrer
   Authenticator-App ab. Ist das Handy mit der Authenticator-App nicht zur Hand, tippen Sie auf
   **Wiederherstellungscode verwenden** und geben einen Ihrer Wiederherstellungscodes ein (Format `xxxxx-xxxxx`, jeder
   Code gilt nur einmal).
3. Haben Sie von der Administration ein **Einmalpasswort** bekommen, verlangt die App jetzt ein eigenes Passwort
   (**Passwort ändern**, mindestens 12 Zeichen). Bis dahin gibt der Server keine Daten frei.

**Passwort vergessen?** Ihre Administration kann das Passwort in EstrichONE zurücksetzen; Sie erhalten dann ein
Einmalpasswort. Klappt die Anmeldung gar nicht, schicken Sie mit **Anmeldung klappt nicht? Problem melden** eine
Meldung an die IT – das geht auch ohne Anmeldung (Abschnitt 7.3).

> **Hinweis:** Nach mehreren falschen Passwörtern sperrt der Server die Anmeldung für kurze Zeit. Warten Sie einige
> Minuten oder wenden Sie sich an die Administration.

## Freigabe des Geräts abwarten

![Wartet auf Freigabe](bilder/iphone-warten.jpg "Ohne Einrichtungscode: Das Gerät wartet auf die Freigabe"){rechts}

Haben Sie die App **ohne Einrichtungscode** eingerichtet, meldet sich das Gerät beim Server an und zeigt **Wartet auf
Freigabe**. Ihre Administration wird benachrichtigt und gibt das Gerät im Web unter **IT Center** → **Geräte** frei. Die
App prüft das selbst und öffnet dann die Heute-Seite – Sie müssen nichts tun.

Mit **Abmelden** brechen Sie ab; **App zurücksetzen** löscht alle Angaben und beginnt von vorn.

Wird ein Gerät später im IT Center **gesperrt** oder **entfernt**, zeigt die App einen Hinweis. Mit **Neu einrichten**
und einem neuen Einrichtungscode lässt es sich wieder verbinden. Noch nicht übertragene Daten bleiben erhalten und
werden nach der Anmeldung derselben Person gesendet.

## Persönliches Gerät: angemeldet bleiben mit Face ID

Auf einem persönlichen Gerät melden Sie sich **einmal** mit dem Passwort an. Danach öffnen Sie die App mit **Face ID**
(bzw. Touch ID) oder dem **Gerätecode** Ihres iPhones – auch nach einem Neustart.

![Gesperrt](bilder/iphone-gesperrt.jpg "EstrichONE ist gesperrt: mit Face ID entsperren")
![Sperre und Face ID](bilder/iphone-sicherheit.jpg "Mehr → Sperre und Face ID")

Unter **Mehr** → **Sperre und Face ID** stellen Sie ein:

- **Mit Face ID entsperren** – an oder aus. Aus heißt: Entsperren nur mit dem Gerätecode. Ist auf dem Gerät weder Face ID
  noch Touch ID eingerichtet, wird immer mit dem Gerätecode entsperrt.
- **Sperren** – wann sich die App im Hintergrund sperrt: **Sofort**, **Nach 1 Minute**, **Nach 5 Minuten**,
  **Nach 15 Minuten** (Standard) oder **Nach 1 Stunde**.
- **Sitzung** – bis wann die Anmeldung ohne Nutzung gültig bleibt.

Jede Nutzung verlängert die Anmeldung. Die Frist legt Ihr Betrieb fest (Standard 90 Tage). Das Passwort brauchen Sie
nur noch, wenn Sie sich abgemeldet haben, Ihr Passwort geändert wurde, das Gerät im IT Center gesperrt oder entfernt
wurde oder die App so lange nicht genutzt wurde. Die App sagt Ihnen dann, warum.

![Sitzung beendet](bilder/iphone-sitzung-beendet.jpg "Hinweis: Das Passwort wurde geändert – bitte neu anmelden"){rechts}

> **Hinweis:** Ändern Sie in den iPhone-Einstellungen Ihre Face-ID-Gesichter oder Fingerabdrücke, nimmt iOS Face ID für
> EstrichONE aus Sicherheitsgründen nicht mehr an. Geben Sie dann einmal den Gerätecode ein; die App richtet Face ID
> mit den neuen Merkmalen neu ein und sagt Ihnen das.

Zum sofortigen Sperren tippen Sie auf **Mehr** → **Sperren**.

## Poolgerät: Benutzer wechseln mit PIN

Auf einem **Poolgerät** – etwa dem iPad im Fahrzeug – arbeiten mehrere Personen nacheinander. Jede Person meldet sich
beim ersten Mal mit Anmeldename und Passwort an und legt dann eine **Geräte-PIN** mit **6 Ziffern** fest. Die PIN gilt
nur auf diesem Gerät und wird nicht an den Server übertragen. Zu einfache PINs (z. B. `123456`) lehnt die App ab.

So arbeiten Sie danach:

1. Beim Öffnen fragt die App **Wer arbeitet mit dem Gerät?** und zeigt die Personen, die sich hier schon angemeldet
   haben.
2. Tippen Sie auf Ihren Namen und geben Sie Ihre PIN ein. Ist Ihr Name nicht dabei, wählen Sie **Andere Person …** und
   melden sich mit dem Passwort an.
3. Zum Übergeben tippen Sie auf **Mehr** → **Benutzer wechseln**.

Die App sperrt sich auf Poolgeräten automatisch, sobald sie verlassen wird, und nach **5 Minuten ohne Bedienung**. Jede
Person sieht nur ihre eigenen Daten; jede hat auf dem Gerät einen eigenen, verschlüsselten Speicher.

> **Wichtig:** Nach **5 falschen PIN-Eingaben** ist für diese Person wieder die Anmeldung mit dem Passwort nötig
> (**Mit Passwort**). Unter dem Namen steht dann „Anmeldung mit Passwort nötig“.

## Demo ausprobieren – ohne Server

![Demomodus](bilder/iphone-heute-chat.jpg "Demomodus: Kennzeichnung „Demo“ und Hinweis auf der Heute-Seite"){rechts}

Mit **Demo ausprobieren – ohne Server** auf dem Willkommensbildschirm lernen Sie die App kennen, ohne einen Server zu
brauchen. Die App läuft dann mit Beispieldaten der fiktiven „Estrichbau Muster GmbH“: sechs Bauakten in allen Ständen
mit Kunden, Fotos, Lieferschein, Bautagebuch, Erstbegehung, CM-Messung, Räumen, Kontakten und Aufgaben, Einsätze der
Woche, „Wer fährt wohin“ mit drei Kolonnen, Chats und Meldungen an die IT.

- **Es wird nichts gesendet.** Alle Änderungen bleiben auf dem Gerät. Kamera, Dokumentenscanner und Videoaufnahme
  funktionieren trotzdem.
- Oben links steht **Demo**, auf der Heute-Seite der Hinweis „Demomodus: Beispieldaten, es wird nichts gesendet.“
- Unter **Demo** → **Störungen vorführen** lassen sich Wartung, Server-Ausfall („Nur Ansicht“) und Ausfälle von Backup-
  und Archivserver zeigen, damit Sie wissen, wie die App dann aussieht.
- Unter **Mehr** → **Benachrichtigungen** → **Beispiel senden** kommt nach wenigen Sekunden eine echte Mitteilung.
- Die Demo meldet Sie automatisch als „Max Huber“ an. Wer die Anmeldung üben möchte, meldet sich unter **Mehr** ab;
  die Anmeldeseite nennt dann die Demozugänge: `m.huber`, oder mit Zwei-Faktor-Anmeldung `s.berger`, jeweils
  Passwort `demo`, Code `246810`.

**Beenden** (auf der Heute-Seite), **Demo** → **Demo beenden** oder **Mehr** → **Demo beenden** löscht alle Demodaten und
führt zurück zur Einrichtung.
