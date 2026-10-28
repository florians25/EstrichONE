#!/usr/bin/env python3
"""Entfernt den Fenstergriff von iPadOS 26 (unten rechts) aus einer iPad-13"-Aufnahme (2064 × 2752).

    python3 ios/AppStore/grip.py <bild.png>        (überschreibt das Bild)

Die Maske `ipad-griff-maske.png` (64 × 64, Lage x 2000, y 2688) markiert die Pixel des Griffs; sie werden von außen
nach innen aus den Nachbarpixeln aufgefüllt. Der Griff liegt in einer Ecke ohne Bedienelemente, der Hintergrund dort
ist einfarbig oder ein ruhiger Kartenrand.
"""
import pathlib
import sys

import numpy as np
from PIL import Image

X0, Y0 = 2000, 2688
path = pathlib.Path(sys.argv[1])
image = Image.open(path).convert("RGB")
if image.size != (2064, 2752):
    sys.exit(f"{path}: {image.size} statt 2064 × 2752 – kein iPad-13-Bild, unverändert")
mask = np.asarray(Image.open(pathlib.Path(__file__).with_name("ipad-griff-maske.png")).convert("L")) > 127
pixels = np.asarray(image).astype(float)
h, w = mask.shape
region = pixels[Y0:Y0 + h, X0:X0 + w].copy()
unknown = mask.copy()
while unknown.any():
    known = ~unknown
    total = np.zeros_like(region)
    count = np.zeros(unknown.shape)
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            if dy == dx == 0:
                continue
            shifted_known = np.zeros_like(known)
            shifted_values = np.zeros_like(region)
            ys = slice(max(dy, 0), h + min(dy, 0)); yd = slice(max(-dy, 0), h + min(-dy, 0))
            xs = slice(max(dx, 0), w + min(dx, 0)); xd = slice(max(-dx, 0), w + min(-dx, 0))
            shifted_known[yd, xd] = known[ys, xs]
            shifted_values[yd, xd] = region[ys, xs]
            total += shifted_values * shifted_known[..., None]
            count += shifted_known
    fill = unknown & (count > 0)
    region[fill] = total[fill] / count[fill][:, None]
    unknown &= ~fill
pixels[Y0:Y0 + h, X0:X0 + w] = region
Image.fromarray(pixels.round().clip(0, 255).astype("uint8")).save(path)
