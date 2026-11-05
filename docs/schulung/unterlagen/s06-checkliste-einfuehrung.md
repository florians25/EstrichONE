% datei: Checkliste-Einfuehrung-im-Betrieb
% art: Checkliste · Einführung
% fuer: Inhaber, Projektleitung und Administration
% felder: Betrieb, Projektleitung, Go-live am

# Checkliste: Einführung von EstrichONE im Betrieb

Haken Sie jeden Punkt ab und tragen Sie ein, wer ihn erledigt. Verweise: Benutzerhandbuch Web (BH Web), Benutzerhandbuch
App (BH App), Kurzanleitungen (K).

## Vor dem Go-live: Technik

- [ ] Server installiert, erreichbar über **HTTPS** mit gültigem Zertifikat (die App lässt sich ohne nicht einrichten)
- [ ] Öffentliche Serveradresse (`ESTRICHONE_DOMAIN`) gesetzt – nötig für QR-Codes, Links in E-Mails und Pushnachrichten
- [ ] Ersteinrichtung mit Einrichtungscode (`docker compose logs api`), erster Administrator angelegt
- [ ] Zwei-Faktor-Anmeldung für alle Administratoren eingerichtet, danach **Pflicht für Administratoren** (BH Web 9.6)
- [ ] **Firma**: Firmendaten und Logo; **PDF-Layout** geprüft (Probeausdruck „Bauaktenblatt“)
- [ ] **E-Mail**-Konto eingerichtet, **Testmail senden** erfolgreich
- [ ] Pushnachrichten: Web Push geprüft; für die iOS-App **Apple Push (APNs)** eingetragen, **Testnachricht an mich**
- [ ] **Backup Center**: erste Sicherung erfolgreich, Zeitplan geprüft (Standard 22:30 Uhr)
- [ ] **Notfallblatt** ausgedruckt und getrennt vom Server verwahrt (K14)
- [ ] **Backup-Ziel** bzw. **Kombiserver** an einem anderen Ort gekoppelt, **Notfallzugang** verwahrt (K15)
- [ ] Archivserver gekoppelt und **Aufbewahrung je Dokumentart** mit der Steuerberatung abgestimmt (falls genutzt)
- [ ] Update-Automatik entschieden (z. B. `update.sh --automatik sicherheit`) (K16)
- [ ] **Überwachung** zeigt „Alle Systeme in Ordnung“

## Vor dem Go-live: Organisation und Daten

- [ ] Rollen festgelegt und in der **Rechtematrix** geprüft (wer plant, wer genehmigt Urlaub, wer gibt Dokumente frei)
- [ ] Module entschieden: **Material**; **Chat** mit dem Betriebsrat abgestimmt (§ 87 BetrVG), Löschfrist festgelegt
- [ ] **Stammdaten**: Wertelisten, Formulare (Basisdaten, Erstbegehung), Textbausteine, Ordner- und Bauakten-Vorlagen
- [ ] **Systemeinstellungen** → **Bauakten**: Nummernkreis, Status-Automatik, Gültigkeit des Freigabelinks
- [ ] Kundenbestand übernommen (**Kunden** → **CSV importieren**), Stichprobe auf Dubletten
- [ ] Kolonnen mit Farbe, Kolonnenführer und Mitgliedern; Fahrzeuge und Geräte mit TÜV/Wartung
- [ ] Personalstamm angelegt und mit **Benutzerkonten** verknüpft; Urlaubsansprüche, Bundesland für Feiertage
- [ ] Materialstamm und Lieferanten (falls Modul Material)
- [ ] Laufende Baustellen als Bauakten angelegt (mindestens Kunde, Bauvorhaben, Adresse, Status)

## Vor dem Go-live: Menschen und Geräte

- [ ] Benutzer angelegt, **Zugangsbriefe** bzw. **Onboarding-PDFs** erstellt
- [ ] iPhones/iPads mit iOS 17 oder neuer, App installiert; Poolgeräte festgelegt und benannt
- [ ] Schulungen durchgeführt: Kolonne, Bauleitung, Büro/Disposition, Administration (Schulungsleitfaden)
- [ ] Wissenstests ausgewertet, Teilnahmebestätigungen ausgestellt
- [ ] Kurzanleitungen gedruckt: Fahrzeuge/Lager, Büro, Serverraum (Notfall)
- [ ] Ansprechpartner für Fragen benannt und auf den Kurzanleitungen eingetragen
- [ ] Helpdesk-Team festgelegt (**Benutzer & Geräte** → **Helpdesk**)

## Am Go-live-Tag

- [ ] Morgens: alle Geräte per QR-Code eingerichtet bzw. freigegeben (**Geräte**: keine Geräte „wartet auf Freigabe“)
- [ ] Plan für die ersten Tage im Cockpit erstellt und **freigegeben**; Kolonnen sehen ihn unter **Wer fährt wohin**
- [ ] Jede Kolonne: ein Testfoto und ein Bautagebuch-Eintrag – im Büro in der Bauakte sichtbar
- [ ] Chat-Gruppen der Kolonnen/Baustellen geprüft (falls Chat aktiv)
- [ ] Ansprechpartner erreichbar; Helpdesk-Tickets werden am selben Tag beantwortet
- [ ] Abends: Sicherung des Tages erfolgreich (**Sicherung** bzw. Card **Sicherung** auf der Startseite)

## Nach dem Go-live (erste 4 Wochen)

- [ ] Täglich: Backup-Status, rote/gelbe Leisten, offene Helpdesk-Tickets
- [ ] Wöchentlich: Integritätsprüfung und Probe-Wiederherstellung im Backup Center erfolgreich
- [ ] Woche 1: Geräte-Liste prüfen (alle Geräte aktiv, App-Versionen), „Bitte prüfen“-Fälle in den Apps nachfassen
- [ ] Woche 2: Nachschulung mit den häufigsten Fragen; Rechte bei Bedarf anpassen
- [ ] Woche 2: einzelne Datei testweise aus der Sicherung zurückholen
- [ ] Woche 4: Rückblick mit Büro, Bauleitung und Kolonnen; Vorlagen und Textbausteine verbessern
- [ ] Altsystem nur noch lesend; Zeitpunkt für die Abschaltung festgelegt
- [ ] Update-Hinweise beobachten; nach Updates Zusatzserver aktualisieren

## Notizen und offene Punkte

<div class="linien n4"></div>
