# Tankbelege

Mit dem Modul **Tankbelege** fotografieren Mitarbeiter ihre Tankquittung direkt nach dem Tanken in der App (iPhone,
iPad, Android); das Büro prüft die Belege, gibt sie frei, erstattet private Auslagen bzw. bucht Karten-Belege und
übergibt sie als CSV an die Buchhaltung. Das Modul lässt sich unter **Systemeinstellungen** → **Module** (Bereich Büro)
ein- und ausschalten (Abschnitt 9.6); es braucht **Maschinen und Fahrzeuge**, denn jeder Beleg gehört zu einem Fahrzeug
aus **Planung** → **Geräte & Fahrzeuge**.

## Belege prüfen und freigeben

**Büro** → **Tankbelege** listet die Belege des gewählten Zeitraums (Standard: laufender und voriger Monat) mit Belegfoto,
Nummer, Datum, Fahrzeug, Mitarbeiter, Menge, Kilometerstand, Verbrauch, Betrag, Zahlart und Status. Oben stehen die
Kennzahlen **Zu prüfen**, **Mit Hinweis**, **Offene Erstattungen** (privat ausgelegt, noch nicht erstattet), **Kosten
im Zeitraum** und **Kraftstoff** in Litern.

![Tankbelege](bilder/tankbelege.png "Tankbelege mit Filter, Belegfoto, Hinweisen und Status")

Filtern Sie nach **Zeitraum**, **Fahrzeug**, **Mitarbeiter**, **Status** (Neu, Geprüft, Erstattet/Gebucht, Abgelehnt
oder „Offen“) und **Zahlart** (Tankkarte, Firmenkarte, privat ausgelegt); **Nur mit Hinweisen** zeigt die auffälligen
Belege.

Ein Klick auf einen Beleg öffnet ihn mit dem Foto in voller Größe (Klick auf das Foto öffnet das Original), allen
Angaben, den Hinweisen und dem Verlauf der Prüfung. Je nach Stand gibt es:

| Aktion | Wirkung |
|---|---|
| **Freigeben** | Beleg ist geprüft (Status **Geprüft**) |
| **Ablehnen** | mit Grund, den der Mitarbeiter in der App sieht; korrigiert er den Beleg, liegt er wieder als **Neu** vor |
| **Erstattet** bzw. **Gebucht** | nach der Freigabe: privat ausgelegte Beträge erstattet, Karten-Belege gebucht – danach unveränderlich |
| **Zurücknehmen** | Freigabe oder Ablehnung zurücknehmen (wieder **Neu**) |
| **Bearbeiten**, **Löschen** | Angaben korrigieren bzw. Fehlerfassung entfernen (nicht mehr nach der Erstattung/Buchung) |

Mehrere Belege entscheiden Sie auf einmal: Häkchen setzen und oben **Freigeben**, **Ablehnen** oder **Erstattet/Gebucht**
wählen. Jede Entscheidung steht im **Protokoll**.

> **Tipp:** Für die monatliche Erstattung filtern Sie Zahlart **Privat ausgelegt** und Status **Geprüft**, markieren
> alle Belege und klicken **Erstattet/Gebucht**.

## Hinweise zur Plausibilität

EstrichONE vergleicht jeden Beleg mit den übrigen Belegen desselben Fahrzeugs und markiert Auffälliges (die Hinweise
blockieren nichts, Sie entscheiden):

| Hinweis | Bedeutung |
|---|---|
| **Kilometerstand rückwärts** | kleiner als bei einem früheren Beleg (oder größer als bei einem späteren) – oft ein Tippfehler |
| **Verbrauch auffällig** | Verbrauch seit dem vorigen Tanken (voll zu voll) liegt deutlich unter der Hälfte oder über dem 1,6-Fachen des üblichen Werts dieses Fahrzeugs; ohne genug Vergleichswerte außerhalb von 3–45 l/100 km |
| **Möglicher Doppelbeleg** | gleiches Fahrzeug, gleicher Tag, gleicher Betrag |
| **Mehr als der Tank fasst** | Liter größer als die **Tankgröße** des Fahrzeugs (in **Geräte & Fahrzeuge** beim Fahrzeug pflegen; AdBlue zählt nicht) |
| **Ohne Foto** | kein Belegfoto vorhanden |

## Belege im Büro erfassen

**Beleg erfassen** trägt einen Papierbeleg im Web nach: Fahrzeug (vorbelegt mit dem heutigen Dispo-Fahrzeug), getankt
von (mit dem Recht „Tankbelege prüfen“ auch für andere), Datum, Kraftstoff (Diesel, Benzin, AdBlue, Sonstiges), Menge,
Betrag brutto, Kilometerstand, **Bezahlt mit** und Tankstelle; **Belegfoto hinzufügen** lädt einen Scan oder ein Foto
hoch. Die **Belegnummer** vergibt EstrichONE aus dem Nummernkreis **Tankbelege** (Standard `TB-{JJJJ}-{NNNN}`); das
Feld zeigt „wird automatisch vergeben“.

## Auswertung je Fahrzeug

Der Reiter **Auswertung je Fahrzeug** zeigt für den Zeitraum je Fahrzeug Belege, Kraftstoff, AdBlue, Kosten brutto,
gefahrene Strecke und den **Ø-Verbrauch** in l/100 km, darunter dieselben Werte **je Monat**. Abgelehnte Belege zählen
nicht mit. Ein Klick auf ein Fahrzeug zeigt dessen Belege.

Auch in **Planung** → **Geräte & Fahrzeuge** öffnet das Belegsymbol bei einem Fahrzeug **Tankbelege und Verbrauch**:
Ø-Verbrauch der letzten zwölf Monate, Kraftstoff, Kosten, letzter Kilometerstand, die Monate und die letzten Belege.

## Export für die Buchhaltung

**Export (CSV)** lädt die Belege des aktuellen Filters (ohne abgelehnte) als CSV-Datei herunter – Semikolon getrennt,
Komma als Dezimalzeichen, UTF-8, öffnet direkt in Excel. Die ersten Spalten folgen dem DATEV-Buchungsstapel: **Umsatz
(brutto)**, **Soll/Haben-Kennzeichen**, **WKZ Umsatz**, **Belegdatum**, **Belegfeld 1** (Belegnummer), **Buchungstext**
(z. B. „Diesel 61,37 l LA-EM 214“) und **KOST1 - Kostenstelle** (Inventarnummer des Fahrzeugs); danach Steuersatz,
Fahrzeug, Kennzeichen, Kraftstoff, Liter, Preis je Liter, Kilometerstand, Zahlart, **Erstattung an** (bei privat
ausgelegten Belegen), Mitarbeiter, Tankstelle und Status. Konten ordnet Ihre Buchhaltung beim Import zu.

## Rechte und Aufbewahrung

| Recht | Bedeutung |
|---|---|
| **Eigene Tankbelege erfassen und sehen** | App und Web; für das Belegfoto zusätzlich „Fotos, Videos und Dokumente hochladen“. Standardrollen Baustelle sowie Büro und Bauleitung |
| **Alle Tankbelege sehen, prüfen, …** | Liste aller Belege, Freigeben, Ablehnen, Erstatten/Buchen, Auswertung, Fotos aller Belege. Standardrolle Büro und Bauleitung |
| **Tankbelege exportieren** | CSV für die Buchhaltung. Standardrolle Büro und Bauleitung |

Belegfotos liegen wie alle Dateien im Speicher von EstrichONE (Sicherung, Auslagerung). Ist ein **Archivserver**
gekoppelt, gehen sie nach der Regel **Tankbelege** sofort nach dem Hochladen ins revisionssichere Archiv – mit
**10 Jahren** Aufbewahrung (Buchungsbelege, § 147 AO); Frist und Auslöser lassen sich unter **System** → **Archiv**
anpassen.
