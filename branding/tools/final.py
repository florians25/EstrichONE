"""Erzeugt das EstrichONE-Logosystem (Runde 2: „E1-Monogramm“) als SVG und Android-Vektoren.

  python3 branding/tools/final.py

Das Zeichen: ein E, dessen Rücken oben den Anstrich einer „1“ trägt – Estrich und ONE in einem Zeichen.
Der Mittelstrich ist als Kapsel abgesetzt: die Libelle, das Ausgleichen, das Ebene.
Raster 100 × 100; Flaggenwinkel 37° (Richtung 4:3), Flaggenstärke 12, Rücken 13, Arme 12.
Für 16/32 px gibt es eine eigene, auf das 16-px-Raster (6,25 Einheiten = 1 px) gelegte Kleinform.
"""
from __future__ import annotations

import math
from pathlib import Path

from brand import num, rrect, squircle, svg, text_path

OUT = Path(__file__).resolve().parent.parent

# Farben (siehe README) – Runde 2: eine Akzentfarbe Markenblau, kein Grün mehr
NIGHT = "#061536"     # Nachtblau: Kachel unten, Zeichen und Schrift auf hell
NIGHT_2 = "#0B2E66"   # Nachtblau hell: Kachel oben
BLUE = "#0062CC"      # Markenblau (= AccentColor der Apps)
BLUE_L = "#4DA3FF"    # Markenblau hell: auf dunklen Flächen (= AccentColor dunkel)
ICE = "#CFE5FF"       # Eis: Verlaufsende des Zeichens in der Kachel
WHITE = "#FFFFFF"

FONT_REG = "inter-tight-latin-600-normal.woff"
FONT_BOLD = "inter-tight-latin-800-normal.woff"
STORY = ("EstrichONE – E1-Monogramm: das E trägt oben den Anstrich der „1“; "
         "der abgesetzte Mittelstrich ist die Libelle – eben.")


def rounded_poly(points, radii) -> str:
    """Geschlossenes Polygon mit Eckrundungen (quadratische Bézier je Ecke)."""
    n = len(points)
    out = []
    for i in range(n):
        p0, p1, p2 = points[i - 1], points[i], points[(i + 1) % n]
        r = radii[i]
        v1 = (p0[0] - p1[0], p0[1] - p1[1]); l1 = math.hypot(*v1)
        v2 = (p2[0] - p1[0], p2[1] - p1[1]); l2 = math.hypot(*v2)
        a = (p1[0] + v1[0] / l1 * min(r, l1 / 2), p1[1] + v1[1] / l1 * min(r, l1 / 2))
        b = (p1[0] + v2[0] / l2 * min(r, l2 / 2), p1[1] + v2[1] / l2 * min(r, l2 / 2))
        out.append(("M" if i == 0 else "L") + f"{num(a[0])} {num(a[1])}Q{num(p1[0])} {num(p1[1])} {num(b[0])} {num(b[1])}")
    return "".join(out) + "Z"


def e1(sx0, top, bottom, spine, arm, right, flag_len, flag_t, ro, ri, dx=0.0):
    """Kontur des E1 (Rücken mit Flagge, oberer und unterer Arm)."""
    d = (0.8, -0.6)                      # Flaggenrichtung (nach rechts oben)
    nrm = (0.6, 0.8)                     # Normale nach unten
    p1 = (sx0, top)
    p11 = (sx0 - d[0] * flag_len, top - d[1] * flag_len)
    p10 = (p11[0] + nrm[0] * flag_t, p11[1] + nrm[1] * flag_t)
    p9 = (sx0, p10[1] + (sx0 - p10[0]) * d[1] / d[0])
    sx1 = sx0 + spine
    pts = [p1, (right, top), (right, top + arm), (sx1, top + arm), (sx1, bottom - arm), (right, bottom - arm),
           (right, bottom), (sx0, bottom), p9, p10, p11]
    rad = [ro * 0.8, ro, ro, ri, ri, ro, ro, ro, ri, ro * 0.6, ro * 0.6]
    pts = [(x + dx, y) for x, y in pts]
    return rounded_poly(pts, rad)


DX = 3  # optischer Ausgleich: die Flagge ist leicht, das Zeichen wandert 3 nach rechts
E = e1(34, 24, 76, 13, 12, 76, 20, 12, 3.5, 1.5, DX)
VIAL = rrect(53 + DX, 44, 23, 12, 6)
# Kleinform (16-px-Raster, Werte in px × 6,25)
U = 6.25
E_SMALL = e1(5 * U, 3 * U, 13 * U, 3 * U, 3 * U, 13.5 * U, 3.75 * U, 2.5 * U, 0.6 * U, 0.15 * U)
VIAL_SMALL = rrect(9.5 * U, 7 * U, 4 * U, 2 * U, 1 * U)

TILE_DEFS = (f'<linearGradient id="eo-t" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="{NIGHT_2}"/>'
             f'<stop offset="1" stop-color="{NIGHT}"/></linearGradient>'
             f'<radialGradient id="eo-g" cx=".18" cy=".04" r=".95"><stop offset="0" stop-color="{BLUE}" stop-opacity=".8"/>'
             f'<stop offset="1" stop-color="{BLUE}" stop-opacity="0"/></radialGradient>'
             f'<linearGradient id="eo-w" gradientUnits="userSpaceOnUse" x1="0" y1="20" x2="0" y2="80">'
             f'<stop offset="0" stop-color="{WHITE}"/><stop offset="1" stop-color="{ICE}"/></linearGradient>'
             f'<linearGradient id="eo-h" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="{WHITE}" stop-opacity=".22"/>'
             f'<stop offset=".5" stop-color="{WHITE}" stop-opacity="0"/></linearGradient>'
             f'<filter id="eo-s" x="-20%" y="-20%" width="140%" height="150%"><feDropShadow dx="0" dy="2.2" stdDeviation="2.4" '
             f'flood-color="#020814" flood-opacity=".45"/></filter>')


def glyph(e_fill, vial_fill, small=False, shadow=False) -> str:
    e, v = (E_SMALL, VIAL_SMALL) if small else (E, VIAL)
    f = ' filter="url(#eo-s)"' if shadow else ""
    return f'  <g{f}>\n    <path d="{e}" fill="{e_fill}"/>\n    <path d="{v}" fill="{vial_fill}"/>\n  </g>\n'


def tile(shape: str, small=False) -> str:
    """App-Symbol: Nachtblau mit Markenblau-Licht oben links, feine Glanzkante, Zeichen mit weichem Schatten."""
    return (f'  <path d="{shape}" fill="url(#eo-t)"/>\n  <path d="{shape}" fill="url(#eo-g)"/>\n'
            + ("" if small else f'  <path d="{shape}" fill="none" stroke="url(#eo-h)" stroke-width="1.2"/>\n')
            + glyph("url(#eo-w)", BLUE_L, small, shadow=not small))


ROUND = squircle(0, 0, 100)
SQUARE = "M0 0H100V100H0Z"
GLYPH_SCALE = 1.15  # freistehendes Zeichen im Lockup


def lockup(icon_body: str, defs: str, word_ink: str, one_ink: str, comment: str, free: bool = False) -> str:
    """Horizontales Lockup: Kachel 100 hoch, Abstand 26, Versalhöhe 40.
    free=True: Bildmarke ohne Kachel, auf 1,15 skaliert und an den linken Rand gerückt."""
    x = 126
    if free:
        sc = GLYPH_SCALE
        left = 34 - 20 * 0.8 + DX  # linke Flaggenspitze
        icon_body = (f'  <g transform="translate({num(-left * sc)} {num(50 - 50 * sc)}) scale({num(sc)})">\n'
                     + icon_body + '  </g>\n')
        x = (76 + DX - left) * sc + 22
    runs, end = text_path([("Estrich", FONT_REG, word_ink), ("ONE", FONT_BOLD, one_ink)],
                          cap_height=40, x=x, baseline=70, tracking=-14)
    body = icon_body + "".join(f'  <path d="{d}" fill="{c}"/>\n' for c, d in runs)
    return svg(round(end + 1), 100, body, defs=defs, comment=comment)


ANDROID = OUT.parent / "android/app/src/main/res/drawable"


def android_vector(comment: str, paths: list[tuple[str, str]]) -> str:
    """Adaptives Android-Symbol 108 dp: sichtbar 72 dp, sicher 66 dp (Kreis). Das 100er-Raster liegt auf den
    sichtbaren 72 dp (Faktor 0,72, Versatz 18); der weiteste Punkt (Flaggenspitze) liegt 27 dp vom Mittelpunkt."""
    body = "".join(f'        <path android:fillColor="{c}" android:pathData="{d}" />\n' for d, c in paths)
    return ('<?xml version="1.0" encoding="utf-8"?>\n'
            f'<!-- {comment} Erzeugt von branding/tools/final.py, nicht von Hand ändern. -->\n'
            '<vector xmlns:android="http://schemas.android.com/apk/res/android"\n'
            '    android:width="108dp" android:height="108dp" android:viewportWidth="108" android:viewportHeight="108">\n'
            '    <group android:scaleX="0.72" android:scaleY="0.72" android:translateX="18" android:translateY="18">\n'
            f'{body}    </group>\n</vector>\n')


def write_android():
    ANDROID.mkdir(parents=True, exist_ok=True)
    (ANDROID / "ic_launcher_foreground.xml").write_text(android_vector(
        "Vordergrund des App-Symbols: E1-Monogramm (branding/logo.svg).", [(E, WHITE), (VIAL, BLUE_L)]))
    (ANDROID / "ic_launcher_monochrome.xml").write_text(android_vector(
        "Einfarbiges Zeichen für Themensymbole (Android 13+).", [(E, WHITE), (VIAL, WHITE)]))
    (ANDROID / "ic_launcher_background.xml").write_text(
        '<?xml version="1.0" encoding="utf-8"?>\n'
        '<!-- Hintergrund des App-Symbols: Nachtblau-Verlauf wie branding/logo.svg. Erzeugt von branding/tools/final.py. -->\n'
        '<shape xmlns:android="http://schemas.android.com/apk/res/android" android:shape="rectangle">\n'
        f'    <gradient android:angle="270" android:startColor="{NIGHT_2}" android:endColor="{NIGHT}" android:type="linear" />\n'
        '</shape>\n')


def main():
    flat = ""
    files = {
        "logo.svg": svg(100, 100, tile(ROUND), defs=TILE_DEFS, comment=STORY + " App-Symbol, abgerundet (Web/Desktop)."),
        "logo-square.svg": svg(100, 100, tile(SQUARE), defs=TILE_DEFS, comment="App-Symbol vollflächig (iOS/Android maskieren selbst)."),
        "logo-small.svg": svg(100, 100, tile(ROUND, small=True), defs=TILE_DEFS,
                              comment="Kleinform für 16–32 px (Favicon, Fenstersymbol), auf das 16-px-Raster gelegt."),
        "logo-mark.svg": svg(100, 100, glyph(NIGHT, BLUE), comment="Bildmarke, transparent, für helle Flächen."),
        "logo-mark-dark.svg": svg(100, 100, glyph(WHITE, BLUE_L), comment="Bildmarke, transparent, für dunkle Flächen."),
        "logo-mark-white.svg": svg(100, 100, glyph(WHITE, WHITE), comment="Bildmarke einfarbig weiß (High Contrast Black)."),
        "logo-mark-black.svg": svg(100, 100, glyph("#000000", "#000000"), comment="Bildmarke einfarbig schwarz (High Contrast White)."),
        "logo-horizontal.svg": lockup(tile(ROUND), TILE_DEFS, NIGHT, BLUE, "Horizontales Logo für helle Flächen."),
        "logo-horizontal-white.svg": lockup(glyph(WHITE, BLUE_L), flat, WHITE, BLUE_L,
                                            "Horizontales Logo für dunkle Flächen (Bildmarke ohne Kachel).", free=True),
        "logo-horizontal-mono-black.svg": lockup(glyph("#000000", "#000000"), flat, "#000000", "#000000",
                                                 "Horizontales Logo einfarbig schwarz.", free=True),
        "logo-horizontal-mono-white.svg": lockup(glyph(WHITE, WHITE), flat, WHITE, WHITE,
                                                 "Horizontales Logo einfarbig weiß.", free=True),
    }
    for name, content in files.items():
        (OUT / name).write_text(content)
    write_android()
    print("Logos geschrieben:", ", ".join(files), "+ Android-Vektoren")


if __name__ == "__main__":
    main()
