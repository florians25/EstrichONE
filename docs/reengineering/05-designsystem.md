# 05 – EstrichONE Designsystem

## 1. Haltung

EstrichONE ist ein **Arbeitswerkzeug**. Die Gestaltung soll Vertrauen und Ruhe ausstrahlen und Informationen
schnell erfassbar machen. Sie soll nicht beeindrucken.

Vorbild ist die Sorgfalt guter Fachsoftware: klare Raster, zurückhaltende Farbe, Farbe nur mit Bedeutung,
konsistente Zustände.

**Gemeinsam über alle Plattformen:** Farben, Begriffe, Symbolbedeutungen, Statuslogik und Tonalität.
**Plattformeigen:** Navigation, Steuerelemente, Typografie, Bewegungen, Materialien. Eine iOS-App sieht aus wie
iOS, eine Windows-App wie Windows.

## 2. Farben

### 2.1 Markenpalette (vorgegeben)

| Token | Wert | Rolle |
|---|---|---|
| `brand.navy` | `#0B2D5B` | Markenfarbe: Kopfbereiche, aktive Navigation, Logo, Primärflächen mit weißer Schrift |
| `brand.blue` | `#007BFF` | Akzent: Fokusringe, Auswahlmarkierung, Diagramm-Hauptserie, große Symbole |
| `neutral.graphite` | `#1F2937` | Primärtext, dunkle Oberfläche im Dunkelmodus |
| `neutral.line` | `#D1D5DB` | Linien, Rahmen, Trennlinien |
| `neutral.white` | `#FFFFFF` | Hintergrund, Text auf Navy |

### 2.2 Kontrollmessung (WCAG 2.2, berechnet)

| Kombination | Kontrast | Bewertung |
|---|---|---|
| Graphit `#1F2937` auf Weiß | 14,68 : 1 | AAA |
| Navy `#0B2D5B` auf Weiß (bzw. Weiß auf Navy) | 13,63 : 1 | AAA |
| **Blau `#007BFF` auf Weiß / Weiß auf Blau** | **3,98 : 1** | **nicht ausreichend für normalen Text (AA verlangt 4,5 : 1)**; zulässig für große Schrift ≥ 24 px bzw. ≥ 18,66 px fett sowie für Bedienelement-Konturen (≥ 3 : 1) |
| Linie `#D1D5DB` auf Weiß | 1,47 : 1 | nur dekorativ; Eingabefeld-Rahmen brauchen ≥ 3 : 1 |

**Konsequenz:** Für Schaltflächen und Links mit normal großer Schrift wird ein abgedunkeltes Blau ergänzt.

| Ergänztes Token | Wert | Kontrast | Einsatz |
|---|---|---|---|
| `action.primary` | `#0062CC` | 5,80 : 1 mit Weiß | Primärschaltflächen, Textlinks |
| `action.primary.hover` | `#0B2D5B` | – | Hover/Pressed (Navy) |
| `text.secondary` | `#4B5563` | 7,56 : 1 | Sekundärtext, Beschriftungen |
| `text.tertiary` | `#6B7280` | 4,83 : 1 | Hilfstexte (nicht auf `surface.alt`) |
| `border.control` | `#6B7280` | ≥ 3 : 1 | Eingabefeld-Rahmen |
| `surface.alt` | `#F3F4F6` | – | Zebra-Zeilen, Seitenhintergrund |
| `focus.ring` | `#007BFF` 2 px + 2 px Abstand | – | sichtbarer Tastaturfokus |

### 2.3 Funktionsfarben (Status)

Status wird **immer** über drei Kanäle vermittelt: Farbe, Symbol bzw. Punkt und Text. Diese Regel stammt
bereits aus Estrich Web, Runde 66.

| Status | Farbe (Text auf Weiß ≥ 4,5 : 1) | Hintergrund hell | Beispiele |
|---|---|---|---|
| Erfolg / erledigt / Einsatz heute | `#15803D` | `#DCFCE7` | „Synchronisiert“, „Abgenommen“ |
| Hinweis / ausstehend | `#B45309` | `#FEF3C7` | „Erstbegehung steht aus“, „Wartet auf Upload“ |
| Fehler / gesperrt | `#B91C1C` | `#FEE2E2` | „Upload fehlgeschlagen“, „Gerät gesperrt“ |
| Information | `#0062CC` | `#E0EFFF` | „2 Materialbestellungen offen“ |
| Neutral / Entwurf | `#4B5563` | `#F3F4F6` | „Entwurf“, „Storniert“ |

Nachgerechnet: Jede Textfarbe erreicht auf Weiß **und** auf ihrem hellen Hintergrund mindestens 4,5 : 1
(niedrigster Wert: Hinweis `#B45309` auf `#FEF3C7` mit 4,51 : 1).

**Bauakten-Status** (abgeleitet aus dem Altbestand):

| Status | Farbe |
|---|---|
| Anfrage | Neutral |
| Geplant | Information |
| In Ausführung | Navy |
| Belegreif / Trocknung | Hinweis |
| Abgeschlossen | Erfolg |
| Storniert | Neutral, durchgestrichen |

### 2.4 Dunkelmodus

| Token | Hell | Dunkel |
|---|---|---|
| `surface` | `#FFFFFF` | `#111827` |
| `surface.raised` | `#FFFFFF` | `#1F2937` (Graphit) |
| `text.primary` | `#1F2937` | `#F9FAFB` |
| `action.primary` | `#0062CC` | `#4DA3FF` (6,76 : 1 auf `#111827`) |
| `brand.navy` (Flächen) | `#0B2D5B` | `#0B2D5B` mit Rahmen `#1E4A87` |
| `neutral.line` | `#D1D5DB` | `#374151` |

### 2.5 Baustellenmodus (mobil)

Für direkte Sonne gibt es eine Darstellung mit maximalem Kontrast (`Einstellungen › Darstellung › Hoher
Kontrast`). Sie verwendet reines Schwarz auf Weiß, Linien in `#1F2937`, keine transparenten Flächen und
fettere Schrift. Auf iOS wird zusätzlich „Transparenz reduzieren“ des Systems beachtet (07).

## 3. Typografie

| Plattform | Schrift | Begründung |
|---|---|---|
| Windows | Segoe UI Variable (System) | nativ, optimale Darstellung, Typ-Rampe von Fluent |
| iOS | SF Pro (System), Dynamic Type | Pflicht für Barrierefreiheit |
| Android | Roboto Flex/System, Material-Typ-Rampe, Schriftgröße des Systems | nativ |
| Web | **Inter** (selbst gehostet, SIL OFL), Fallback System-Schriften | keine Abhängigkeit von Google Fonts (Datenschutz) |

**Web-Typ-Rampe** (Basis 15 px für Büro-Dichte, 16 px auf Touch-Geräten):

| Stufe | Größe / Zeilenhöhe | Gewicht | Verwendung |
|---|---|---|---|
| Titel 1 | 24 / 32 | 600 | Seitentitel |
| Titel 2 | 19 / 26 | 600 | Abschnitte, Bauakten-Kopf |
| Titel 3 | 16 / 24 | 600 | Kartentitel, Tabellengruppen |
| Text | 15 / 22 | 400 | Standard |
| Text klein | 13 / 18 | 400 | Tabellen in dichter Ansicht, Metadaten |
| Beschriftung | 12 / 16 | 500, Versalien +0,04 em | Feldgruppen, Spaltenköpfe |
| Ziffern | `font-variant-numeric: tabular-nums` | – | Flächen, Datumsangaben, Bauaktennummern |

## 4. Raster und Abstände

- **4-px-Grundraster.** Abstandsskala: 4 · 8 · 12 · 16 · 24 · 32 · 48.
- **Web-Layout:** linke Navigation (232 px, einklappbar auf 56 px), Inhaltsbereich mit 12-Spalten-Raster,
  maximale Lesebreite für Formulare 720 px.
- **Dichte:** Web und Desktop haben zwei Stufen, „Standard“ (Zeilenhöhe 40 px) und „Kompakt“ (32 px).
  Mobil gibt es nur eine Stufe mit Touch-Zielen von **mindestens 48 × 48 dp/pt**. Primäraktionen auf der
  Baustelle haben 56 px Höhe.
- **Rundungen:** Web 6 px (Bedienelemente), 8 px (Flächen). Nativ gelten die Plattformwerte.
  Keine „Pillen“-Karten.
- **Schatten:** nur für Ebenen, die tatsächlich schweben (Menüs, Dialoge). Karten im Inhaltsbereich haben
  einen Rahmen und keinen Schatten.

## 5. Komponenten-Grundsätze (Web)

| Komponente | Regeln |
|---|---|
| Tabelle | Kopfzeile fixiert, Spalten sortier-, filter- und ausblendbar, Spaltenauswahl wird gespeichert, Zeilenaktionen per Kontextmenü und Tastatur, Mehrfachauswahl mit Sammelaktionen, virtuelles Scrollen ab 200 Zeilen |
| Formular | Beschriftung über dem Feld; Pflichtfelder markiert mit „Pflichtfeld“ (nicht nur Stern); Fehler unter dem Feld in Klartext („Bitte eine PLZ mit 5 Ziffern eingeben“); Speichern mit Strg+S/Strg+Enter |
| Dialog | nur für kurze, abgeschlossene Aufgaben; alles Längere wird eine Seite oder ein Seitenpanel |
| Leerer Zustand | Was fehlt, warum, und **eine** Aktion („Noch keine Fotos. Fotos werden in der App auf der Baustelle aufgenommen oder hier hochgeladen.“ + „Hochladen“) |
| Laden | Skelettzeilen in der Struktur des Inhalts, kein Vollbild-Spinner; ab 10 s Hinweis mit Abbrechen |
| Hinweise | schmale Zeilen mit linkem Farbrand (Muster aus Estrich Web, Runde 66), keine Vollflächen-Kästen |
| Bestätigung | nur bei Datenverlust; die Schaltfläche benennt die Folge („Bauakte endgültig löschen“, nicht „OK“) |
| Symbole | Material Symbols (Apache-2.0) im Web, Segoe Fluent Icons unter Windows, SF Symbols unter iOS, Material Symbols unter Android; **Symbole nie ohne Text** in der Hauptnavigation |

## 6. Sprache

- Deutsche Fachbegriffe des Betriebs: **Bauakte**, Baustelle, Kolonne, Einsatz, Erstbegehung, CM-Messung,
  Belegreife, Estrichaufbau.
- Anglizismen werden vermieden: „Synchronisieren“ statt „Sync“, „Gerät“ statt „Device“.
- Meldungen sagen, **was passiert ist und was zu tun ist**: „Keine Verbindung zum Server. Die Bauakte ist auf
  diesem Gerät gespeichert und wird automatisch übertragen.“
- Anrede: **Sie** in Web und Desktop, **Sie** in den Apps. **[Offen] E-15:** Duzen in den Apps, falls im Betrieb üblich.

## 7. Bewegung

- Übergänge 150–200 ms, nur für räumliche Orientierung (Panel öffnet, Element wird verschoben).
- Keine dekorativen Animationen. Die Systemeinstellungen „Bewegung reduzieren“ werden auf allen Plattformen
  respektiert.

## 8. Produktidentität ohne Gleichförmigkeit

Die Wiedererkennung entsteht aus:
- Navy-Kopfbereich bzw. Navy-Akzent,
- dem Bauakten-Kopf (Nummer · Bezeichnung · Adresse · Status) in identischer Informationsreihenfolge,
- gleichen Statusfarben und Begriffen,
- dem gleichen Logo.

Alles andere folgt der jeweiligen Plattform.

## 9. Umsetzung

- `design/tokens.json` als einzige Quelle (W3C-Design-Tokens-Format). Daraus erzeugt **Style Dictionary**:
  CSS-Variablen (Web), `ResourceDictionary` (WinUI), Swift-`Color`-Erweiterungen (iOS) und
  Compose-`ColorScheme` (Android).
- Das Web-Komponentenset liegt im Angular-Projekt unter `web/src/app/ui/` und ist mit Storybook dokumentiert.
  Jede Komponente hat Tastatur- und axe-Tests (19).
