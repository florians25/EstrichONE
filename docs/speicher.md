# Speicherschonende Ablage von Fotos und Dokumenten

Gilt für jeden Upload – Web, iPhone/iPad, Android, Windows –, weil der Server verdichtet. Einstellungen unter
**Systemeinstellungen → Speicher** (`settings.media`, für alle lesbar, damit Apps passend vorverkleinern können).

## Fotos
- JPEG-Fotos werden mit **mozjpeg-Einstellungen** neu kodiert (libvips: Trellis-Quantisierung, optimierte
  Huffman-Tabellen, progressive Scans, Quantisierungstabelle 3), Qualität **82** (einstellbar 60–95).
- **EXIF-Ausrichtung** wird angewendet (Bild steht danach richtig, Ausrichtung = 1). **Aufnahmezeit, GPS, XMP und
  Farbprofil bleiben erhalten.**
- Längste Kante höchstens **4096 px** (einstellbar 2048–8192, Lanczos). Das reicht für A4-Druck mit 300 dpi.
- Gespeichert wird nur, wenn das Ergebnis mindestens 8 % kleiner ist (oder gedreht/verkleinert werden musste);
  sonst bleibt das Original bitgenau. „Fotos unverändert speichern“ schaltet die Verdichtung ab.
- PNG, WebP, HEIC und Videos bleiben unverändert (Videos: keine Transkodierung, da im API-Image kein ffmpeg).
- Vorschaustufen (WebP, ohne Metadaten): `thumb` 320 px, `display` 1280 px, `large` 2560 px
  (`GET /api/v1/files/{id}/content?variant=…`).

Messung mit echten Kamerafotos (Server, Standardeinstellungen):

| Foto | vorher | nachher | Ersparnis | PSNR |
|---|---:|---:|---:|---:|
| iPhone 6, 3264×2448 | 2,59 MB | 0,93 MB | 64 % | 38,7 dB |
| iPhone 5, 3264×2448 (um 180° gedreht gespeichert) | 2,37 MB | 0,72 MB | 69 % | 40,3 dB |
| Nikon, 3888×2592 (bereits stark komprimiert) | 0,74 MB | 0,63 MB | 14 % | 46,3 dB |
| 6000×4500, Qualität 95 | 5,44 MB | 1,20 MB (4096×3072) | 78 % | – |

Zum Vergleich: ImageMagick mit Qualität 82 ohne mozjpeg ergibt beim iPhone-6-Foto 1,19 MB. Ab etwa 38 dB PSNR ist
ein Unterschied in der 100-%-Ansicht praktisch nicht sichtbar.

## Dokumente
- **PDFs** werden verlustfrei neu geschrieben: unkomprimierte Streams mit Flate gepackt, nur erreichbare Objekte
  gespeichert. Ergebnis zählt nur, wenn es mindestens 8 % kleiner ist und sich mit gleicher Seitenzahl öffnen lässt.
  **Signierte** (`/ByteRange`, `/Sig`), verschlüsselte und XFA-PDFs bleiben bitgenau.
- Office-Dateien und alles andere bleiben unverändert.

## Dubletten
Gleiche Prüfsumme und Größe → die neue Datei benutzt die vorhandene Ablage mit (auch über Bauakten hinweg). Die
Archivierung löscht ein Original nur, wenn keine andere Datei es mitbenutzt.

## Prüfsummen
`sha256`/`sizeBytes` einer Datei beschreiben nach dem Upload den **gespeicherten** Inhalt. Die beim Ankündigen
übermittelte Prüfsumme bleibt gültig: Eine wiederholte Ankündigung mit dem Original gilt weiterhin als dieselbe Datei.

## Nachverdichten und Statistik
`GET /api/v1/files/storage` (Belegung je Art, Ersparnis, Dubletten, Datenträger, Kandidaten, Status) und
`POST /api/v1/files/storage/optimize` (Hintergrundlauf) – beides mit `settings.manage`. Archivierte oder zur
Archivierung vorgemerkte Dateien werden nie verändert (ihre Prüfsumme liegt im Archiv).
