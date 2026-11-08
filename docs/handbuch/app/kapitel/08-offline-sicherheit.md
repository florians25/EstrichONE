# Offline arbeiten, Störungen und Sicherheit

Auf Baustellen gibt es oft kein Netz – im Keller, im Rohbau, auf dem Land. EstrichONE ist dafür gebaut: Die App
speichert alles zuerst verschlüsselt auf dem Gerät und überträgt es, sobald der Server wieder erreichbar ist. Dieses
Kapitel erklärt, was dabei im Hintergrund passiert und welche Hinweise die App zeigt.

## Synchronisation

![Synchronisation](bilder/iphone-sync.jpg "Mehr → Synchronisation: alles übertragen"){rechts}

Die App gleicht sich selbstständig mit dem Server ab: beim Öffnen, regelmäßig im Vordergrund und sofort, wenn Sie etwas
erfassen. Den Stand sehen Sie

- an der **Wolke** oben auf der Heute-Seite und in den Listen,
- unter **Mehr** → **Synchronisation**: „Alles übertragen“ mit dem Zeitpunkt des letzten vollständigen Abgleichs, darunter
  **Ausstehend** mit allem, was noch auf die Übertragung wartet (Fotos, Dokumente, Einträge).

**Jetzt synchronisieren** stößt den Abgleich sofort an. Große Dateien wie Fotos und Videos werden einzeln übertragen;
schlägt ein Upload fehl, versucht die App es später selbst noch einmal, oder Sie tippen auf **Fehlgeschlagene Uploads
erneut versuchen**.

**Bitte prüfen:** Lehnt der Server eine Änderung ab – etwa weil das Büro dieselbe Angabe gleichzeitig anders geändert
hat oder ein Recht fehlt –, steht sie unter **Vom Server abgelehnt** mit dem Grund. Solche Vorgänge werden nicht erneut
gesendet; prüfen Sie den Stand in der Bauakte und wischen Sie den Eintrag nach links, um ihn zu entfernen. Auf der
Heute-Seite weist „Einige Änderungen wurden nicht übernommen“ darauf hin.

## Offline arbeiten

Ohne Netz zeigt die App **Offline**. Sie können wie gewohnt weiterarbeiten: Bauakten öffnen, Fotos, Videos und
Dokumente aufnehmen, Bautagebuch, Mängel, Aufgaben, Erstbegehung, CM-Messung und Abnahme erfassen, neue Bauakten und
Kunden anlegen, Chatnachrichten schreiben und Meldungen an die IT verfassen. Alles wird auf dem Gerät gespeichert und
übertragen, sobald wieder Verbindung besteht.

Nur mit Verbindung möglich sind Dinge, die der Server erzeugt oder prüft: **PDFs** (Bauakte, Protokolle), der
**Änderungsverlauf**, die **Konfliktprüfung** und **Freigabe** in der Disposition, Freigabelinks für die Abnahme und
Tage der Planung, die noch nicht auf dem Gerät liegen.

> **Wichtig:** Solange etwas nur auf dem Gerät liegt, sehen Kollegen und Büro es noch nicht – und es ist nur dort
> gesichert. Öffnen Sie die App, sobald Sie wieder Netz haben, und löschen Sie sie nicht, solange **Ausstehend** nicht
> leer ist.

## „Nur Ansicht“ – Schutz vor Datenverlust

![Störungen](bilder/iphone-stoerung.jpg "Rote, gelbe und graue Leiste oben: Backup, Archiv, Nur Ansicht"){rechts}

Ist der Server **seit über 5 Minuten nicht erreichbar**, schaltet die App vorsorglich auf **Nur Ansicht**. Eine graue
Leiste oben sagt: „Nur Ansicht – seit … keine Verbindung zum Server. Erfassen und Ändern sind gesperrt.“ Sie können
weiterhin alles ansehen, aber nichts Neues erfassen. So verhindert die App, dass über längere Zeit wichtige Daten nur
auf einem Gerät liegen, das verloren gehen kann.

Müssen Sie trotzdem erfassen – etwa auf einer Baustelle ganz ohne Netz –, tippen Sie in der Leiste auf **Trotzdem
arbeiten** und bestätigen **Auf eigene Gefahr offline arbeiten**. Die Leiste zeigt dann „Offline auf eigene Gefahr“; mit
**Nur Ansicht** kehren Sie in den geschützten Modus zurück. Sobald der Server wieder erreichbar ist, endet der Modus von
selbst und alles wird übertragen.

## Wartung des Servers

![Wartung](bilder/iphone-wartung.jpg "Wartungsarbeiten: die App wartet und gibt sich danach selbst frei"){rechts}

Wird der Server gewartet (z. B. für ein Update), zeigt die App den Bildschirm **Wartungsarbeiten** mit dem Grund und
**Voraussichtlich bis …**. Darunter sehen Sie, ob auf dem Gerät noch etwas aussteht, und wann zuletzt geprüft wurde.

Sie müssen nichts tun: Die App prüft regelmäßig und **gibt sich nach der Wartung automatisch wieder frei**. Ihre Daten
bleiben verschlüsselt auf dem Gerät und werden danach übertragen. **Erneut prüfen** fragt sofort nach.

Eine **geplante Wartung** kündigt die App vorher als Hinweis an; ihn können Sie mit **Hinweis ausblenden** schließen.

## Hinweise zu Backup- und Archivserver

Oben auf allen Hauptseiten erscheint eine farbige Leiste, wenn die Datensicherung des Betriebs gestört ist:

| Leiste | Bedeutung | Was Sie tun |
|---|---|---|
| **Rot** – „Backup-Ziel nicht erreichbar. Letzte erfolgreiche Sicherung: …“ | Die tägliche Datensicherung läuft nicht. | Geben Sie **umgehend** Ihrer Administration Bescheid. |
| **Gelb** – „Archivserver nicht erreichbar. Letzte Archivierung: …“ | Der Archivserver (Langzeitablage) ist nicht erreichbar. Erscheint nur, wenn ein Archivserver eingerichtet ist. | Informieren Sie Ihre Administration. |

Den Text der Leisten liefert der Server; die App zeigt ihn unverändert. Ohne Störung ist keine Leiste zu sehen. Ihre
Arbeit in der App ist davon nicht betroffen.

## Datenschutz und Sicherheit

- **Ihre Daten bleiben im Betrieb.** Die App spricht ausschließlich mit dem Server Ihres Betriebs, immer verschlüsselt
  (HTTPS mit gültigem Zertifikat). Es gibt keine Cloud des Herstellers, kein Tracking und keine Werbung.
- **Verschlüsselt auf dem Gerät.** Daten, Fotos und Dokumente liegen auf dem Gerät verschlüsselt, je Person getrennt.
  Der Schlüssel ist im iOS-Schlüsselbund geschützt und wird mit Face ID, Gerätecode oder – auf Poolgeräten – mit der
  PIN freigegeben.
- **An das Gerät gebunden.** Bei der Einrichtung erzeugt das Gerät einen eigenen Schlüssel, der es nicht verlassen kann.
  Die Anmeldung gilt nur zusammen mit diesem Gerät; im IT Center sieht die Administration jedes Gerät und kann es
  sperren oder entfernen.
- **Abmelden löscht.** Beim Abmelden und beim Zurücksetzen löscht die App alle Daten dieser Person bzw. alle Daten vom
  Gerät (Kapitel 9).
- **Meldungen an die IT** enthalten nie Passwörter, PINs oder Inhalte aus Bauakten.

## Berechtigungen der App

Die App fragt iOS-Berechtigungen nur, wenn Sie die Funktion benutzen. Eine erteilte oder verweigerte Berechtigung
ändern Sie in der Einstellungen-App unter **EstrichONE**.

| Berechtigung | Wofür |
|---|---|
| **Kamera** | Fotos, Videos, Dokumentenscanner, QR-Code bei der Einrichtung |
| **Fotos (Hinzufügen)** | nur auf Ihren Wunsch ein Foto aus EstrichONE in der Mediathek sichern |
| **Lokales Netzwerk** | Verbindung zum Server, wenn er im Firmennetz steht |
| **Mikrofon** | Ton bei Videoaufnahmen; Diktat im Chat (der Ton wird nie gespeichert) |
| **Spracherkennung** | Diktat im Chat |
| **Standort** | nur auf Knopfdruck: Baustellenadresse übernehmen, Aufnahmeort eines Fotos, Standort im Chat teilen |
| **Kontakte** | Kunden in die Kontakte-App übernehmen |
| **Mitteilungen** | Pushnachrichten |
| **Face ID** | Entsperren auf persönlichen Geräten |
