"""Gibt Screenshots verkleinert (AVIF, ersatzweise WebP) als base64 ins Job-Protokoll aus.

Artefakte lassen sich aus der Entwicklungsumgebung nicht immer laden; das Protokoll schon. Je Bild eine oder mehrere
Zeilen `SHOT|<name>|<format>|<teil>/<teile>|<base64>` (höchstens 16 000 Zeichen je Zeile).

    python3 shots-to-log.py <ordner> [breite-iphone] [breite-ipad] [qualität]
"""
import base64
import io
import pathlib
import sys

from PIL import Image

folder = pathlib.Path(sys.argv[1])
phone_width = int(sys.argv[2]) if len(sys.argv) > 2 else 540
pad_width = int(sys.argv[3]) if len(sys.argv) > 3 else 760
quality = int(sys.argv[4]) if len(sys.argv) > 4 else 45
chunk = 16000

total = 0
for path in sorted(folder.glob("*.png")):
    image = Image.open(path).convert("RGB")
    width = pad_width if path.name.startswith("ipad") else phone_width
    if image.width > width:
        image = image.resize((width, round(image.height * width / image.width)), Image.LANCZOS)
    buffer = io.BytesIO()
    try:
        image.save(buffer, "AVIF", quality=quality)
        kind = "avif"
    except Exception:  # noqa: BLE001 – ohne AVIF-Unterstützung WebP
        buffer = io.BytesIO()
        image.save(buffer, "WEBP", quality=quality + 10)
        kind = "webp"
    text = base64.b64encode(buffer.getvalue()).decode("ascii")
    parts = [text[i:i + chunk] for i in range(0, len(text), chunk)]
    for index, part in enumerate(parts, start=1):
        print(f"SHOT|{path.stem}|{kind}|{index}/{len(parts)}|{part}")
    total += len(buffer.getvalue())
print(f"SHOTS-ENDE|{total} Bytes")
