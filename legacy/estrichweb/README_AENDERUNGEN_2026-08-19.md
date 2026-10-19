# EstrichWeb – Änderungen 19.08.2026 (Runde 40)

---

## 1. Kalender: nur noch die gedrängte Ansicht

Die Wahlmöglichkeit ist vollständig entfernt – Umschalter, Menü, gespeicherte
Einstellung und alle drei Regelsätze. Eine halbe Stunde entspricht fest
**26 px**, damit passen 05:00 bis 22:00 ohne Scrollen auf den Bildschirm.

Der Textaufbau ist darauf abgestimmt: Titel einzeilig, kleinere Schrift,
knapper Innenabstand. Die zuständige Person entfällt im Raster – dort ist kein
Platz dafür; sie steht weiterhin im Detailfenster und in der Terminübersicht.

## 2. Kalender-Kategorien bearbeiten und löschen

In der Seitenleiste erscheint bei jedem eigenen Kalender ein Menü (drei
Punkte, beim Überfahren sichtbar; auf Tablets dauerhaft):

- **Bearbeiten** – Name, Code, Farbe und Symbol im bekannten Dialog.
- **Löschen** – mit Rückfrage. Sind Termine betroffen, steht die Zahl in der
  Rückfrage: „3 Termine verlieren damit ihre Zuordnung – sie bleiben erhalten
  und erscheinen ohne Farbe." Keine stillen Datenverluste.

Die Sammelkategorie **„Termine ohne Kolonne"** hat kein Menü – sie stammt aus
der Planung und ist kein eigener Datensatz. Gleiches gilt für die fest
hinterlegten Grundkategorien; dort erscheint ein Hinweis statt eines Fehlers.

## 3. Bauakte: Baustellen-Cockpit

Neu über den Reitern – der Stand einer Akte auf einen Blick, gedacht für
Bauleitung, Verwaltung und Geschäftsführung.

### Fortschritt in fünf Schritten

**Angelegt → Erstbegehung → Eingeplant → Ausführung → Abgeschlossen**

Balken mit Prozentwert, darunter die Schritte als Marken: erledigte grün mit
Haken, der nächste offene blau hervorgehoben. Jede Marke führt in den
zuständigen Reiter. Über dem Balken steht der nächste Schritt im Klartext.

**Alles wird aus vorhandenen Daten abgeleitet** – Erstbegehung aus den
Begehungsdaten, „eingeplant" aus den Terminen, „Ausführung" aus Fotos und
Tagesberichten, „abgeschlossen" aus dem Status. Es gibt keine zusätzliche
Pflege und keine neuen Felder.

### Fünf Kacheln

| Kachel | Inhalt |
|---|---|
| Nächster Termin | „heute", „morgen", „in 4 Tagen" oder Datum |
| Material offen | Bestellungen, die noch nicht geliefert sind – orange, wenn > 0 |
| Dokumente | Anzahl in der Akte |
| Fotos | Anzahl in der Akte |
| Letzte Aktivität | „heute", „gestern", „vor 5 T." |

Jede Kachel führt in den passenden Reiter.

### Offene Punkte

Darunter, sofern zutreffend, bis zu vier konkrete Hinweise mit Sprungknopf:

- Erstbegehung steht noch aus
- Noch kein Termin mit einer Kolonne vereinbart
- Für die Baustelle ist keine Adresse hinterlegt
- Keine Fläche erfasst – Material lässt sich so nicht berechnen
- Es liegen noch keine Dokumente in der Akte

Bewusst kurz gehalten: nur Dinge, die blockieren oder erfahrungsgemäß
vergessen werden. Ist alles erledigt, verschwindet der Bereich ganz.

**Die bestehende Bauakte bleibt vollständig unverändert** – alle Reiter,
Felder und Abläufe wie bisher. Das Cockpit ist eine Ergänzung darüber, keine
Umstellung. Beim Drucken wird es ausgeblendet.

---

## Deployment

1. **`api/` hochladen.**
2. Restliche Dateien in den Document-Root.
3. `api/config/local.php` beibehalten (nicht überschreiben).
4. **Strg+F5**, damit der Service Worker die neue Fassung übernimmt.

Datenbank-Migrationen sind nicht erforderlich.

---

## Gedanke zum Cockpit

Der Zuschnitt orientiert sich daran, was die drei Rollen beim Öffnen einer
Akte zuerst wissen wollen: die **Bauleitung** den nächsten Termin und was
fehlt, die **Verwaltung** ob Dokumente und Material vollständig sind, die
**Geschäftsführung** den Fortschritt. Deshalb Balken, Kacheln und offene
Punkte – und bewusst keine weiteren Kennzahlen. Sagen Sie gern, welche Angabe
Ihnen im Alltag fehlt; das ist dann eine Kachel mehr.
