# App-Store-Screenshots

Fertige Bilder für App Store Connect in exakt Apples Pixelmaßen, je Gerät acht Motive (Reihenfolge = Upload-Reihenfolge):

| Ordner | Größe | App Store Connect |
|---|---|---|
| `screenshots/iphone-6.9/` | 1320 × 2868 | iPhone 6,9" (Pflicht; Apple skaliert für kleinere iPhones) |
| `screenshots/iphone-6.5/` | 1284 × 2778 | iPhone 6,5" (aus 6,9" skaliert) |
| `screenshots/ipad-13/` | 2064 × 2752 | iPad 13" (Pflicht, da die App auch auf dem iPad läuft) |

Je Ordner gerahmte Bilder (Markenhintergrund, Überschrift, Geräterahmen) und unter `roh/` die unveränderte Aufnahme –
beides ist zulässig; empfohlen sind die gerahmten. PNG, ohne Alphakanal, hell, Deutsch, Statusleiste 9:41.

## Neu erzeugen

1. Workflow „iOS“ von Hand starten mit **appstore = true** (oder `[appstore]` in der Commit-Nachricht). Der Job
   „App-Store-Screenshots“ baut die App, startet den Demomodus ohne Demo-Kennzeichnung (`-EstrichAppStore YES`) auf
   iPhone 17/16 Pro Max (6,9") und iPad Pro 13" und nimmt die Motive auf (`scripts/screenshots.sh`, `APPSTORE_SCENES`).
2. Bilder holen: Artefakt `appstore-iphone69` / `appstore-ipad13` (PNG) – oder aus dem Protokoll (Zeilen `SHOT|…`,
   AVIF in voller Auflösung, Qualität 90) dekodieren und als `iphone69-<motiv>-hell.png` / `ipad13-<motiv>-hell.png`
   ablegen.
3. `ios/AppStore/frame.sh <rohordner>` erzeugt alle drei Größen (roh und gerahmt) und prüft Maße und Alphakanal.
   Texte der Überschriften stehen im Skript (`TITLES`, `SUBS`).
