# App-Store-Paket

| Ordner | Inhalt |
|---|---|
| `upload/iphone-6.9/` | 1320 × 2868, gerahmt mit Überschrift – Pflichtfach „iPhone 6,9"“ |
| `upload/iphone-6.5/` | 1284 × 2778, gerahmt – optionales Fach „iPhone 6,5"“ |
| `upload/ipad-13/` | 2064 × 2752, gerahmt – Pflichtfach „iPad 13"“ |
| `roh/<gerät>/` | dieselben Größen ohne Rahmen (ebenfalls zulässig) |
| `banner-1920x886/` | Querformat 1920 × 886 für Website/Werbung (nicht für App Store Connect) |
| `texte/de-DE/`, `texte/en-US/` | je Feld eine Datei; Prüfung der Zeichengrenzen: `python3 texte/pruefen.py` |
| `web/` | Startseite, Support mit Impressum, Datenschutzerklärung zum Hosten (Platzhalter gelb markiert) |

Dateinamen 01–09 = Reihenfolge im App Store. Alle Bilder JPEG (Qualität 94, 4:4:4), sRGB mit eingebettetem Profil
(`sRGB.icc`), ohne Alphakanal; Statusleiste 9:41, voller Empfang und Akku, Deutsch, ohne Demo-Kennzeichnung,
iPad im Vollbild ohne Fenstergriff. Schritt für Schritt hochladen: `ios/APPSTORE.md`, Abschnitt 0.

## Neu erzeugen

1. Workflow „iOS“ von Hand starten mit **appstore = true** (oder `[appstore]` in der Commit-Nachricht). Der Job
   „App-Store-Screenshots“ baut die App, startet den Demomodus ohne Demo-Kennzeichnung (`-EstrichAppStore YES`) auf
   iPhone 17/16 Pro Max (6,9") und iPad Pro 13" und nimmt die Motive auf (`APPSTORE_SCENES` in `.github/workflows/ios.yml`).
2. Bilder holen: Artefakt `appstore-iphone69` / `appstore-ipad13` (PNG) – oder aus dem Protokoll (Zeilen `SHOT|…`,
   AVIF in voller Auflösung) dekodieren und als `iphone69-<motiv>-hell.png` / `ipad13-<motiv>-hell.png` ablegen.
3. `ios/AppStore/frame.sh <rohordner>` erzeugt `upload/`, `roh/` und `banner-1920x886/` neu und prüft jede Datei
   (Pixelmaß, sRGB-Profil, kein Alpha, 1–10 je Gerät). Den Fenstergriff, den iPadOS 26 im Simulator unten
   rechts immer zeigt, entfernt `grip.py` (Maske `ipad-griff-maske.png`, Auffüllen aus den Nachbarpixeln; Bildgröße bleibt). Überschriften stehen im Skript (`TITLES`, `SUBS`).
   Nur prüfen: `ios/AppStore/frame.sh --pruefen`.
