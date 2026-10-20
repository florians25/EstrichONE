"""Erzeugt das endgültige EstrichONE-Logosystem (Konzept A „Libellen-E“, verfeinert) als SVG.

  python3 branding/tools/final.py

Raster: 100 × 100 Einheiten. Das E steht auf 26…74 × 24…76, Strichstärken optisch korrigiert
(senkrecht 13, waagrecht 12), der Mittelstrich ist die Libelle (Kapsel) mit Blase.
Für 16/32 px gibt es eine eigene, auf das 16-px-Pixelraster (6,25 Einheiten) gelegte Kleinform ohne Blase.
"""
from __future__ import annotations

from pathlib import Path

from brand import circle, num, rrect, squircle, svg, text_path

OUT = Path(__file__).resolve().parent.parent

# Farben (siehe README)
NAVY = "#0B2D5B"      # Estrich-Navy, Primärfarbe
NAVY_2 = "#174A8B"    # Verlauf hell, Schrift „ONE“ auf hell
BLUE = "#2F6DB5"      # Libelle auf hellen Flächen
SKY = "#8EC5FF"       # Libelle auf dunklen Flächen
GREEN = "#3CCB7F"     # Blase / Akzent
WHITE = "#FFFFFF"

FONT_REG = "inter-tight-latin-600-normal.woff"
FONT_BOLD = "inter-tight-latin-800-normal.woff"

TILE_GRAD = (f'<linearGradient id="eo-bg" x1="0" y1="0" x2="1" y2="1">'
             f'<stop offset="0" stop-color="{NAVY_2}"/><stop offset="1" stop-color="{NAVY}"/></linearGradient>')
STORY = ("EstrichONE – das „E“ aus dem Estrichaufbau: oben die abgezogene Oberfläche, unten die tragende Decke, "
         "der Mittelstrich ist die Libelle der Wasserwaage mit Blase: eben und belegreif.")


def e_path(x0=26, x1=74, y0=24, y1=76, spine=13, arm=12, ro=4.0, ri=1.5, dx=0.0, dy=0.0) -> str:
    """Das E ohne Mittelstrich (Rücken + oberer und unterer Arm) als eine Kontur, Innenecken leicht gerundet."""
    x0, x1, y0, y1 = x0 + dx, x1 + dx, y0 + dy, y1 + dy
    xs = x0 + spine
    a, b = y0 + arm, y1 - arm
    n = num
    return (f"M{n(x0 + ro)} {n(y0)}H{n(x1 - ro)}A{n(ro)} {n(ro)} 0 0 1 {n(x1)} {n(y0 + ro)}V{n(a - ro)}"
            f"A{n(ro)} {n(ro)} 0 0 1 {n(x1 - ro)} {n(a)}H{n(xs + ri)}A{n(ri)} {n(ri)} 0 0 0 {n(xs)} {n(a + ri)}"
            f"V{n(b - ri)}A{n(ri)} {n(ri)} 0 0 0 {n(xs + ri)} {n(b)}H{n(x1 - ro)}A{n(ro)} {n(ro)} 0 0 1 {n(x1)} {n(b + ro)}"
            f"V{n(y1 - ro)}A{n(ro)} {n(ro)} 0 0 1 {n(x1 - ro)} {n(y1)}H{n(x0 + ro)}A{n(ro)} {n(ro)} 0 0 1 {n(x0)} {n(y1 - ro)}"
            f"V{n(y0 + ro)}A{n(ro)} {n(ro)} 0 0 1 {n(x0 + ro)} {n(y0)}Z")


# Hauptform (ab 48 px)
E = e_path()
VIAL = rrect(45, 44, 29, 12, 6)
BUBBLE = circle(59.5, 50, 3.6)
# Kleinform (16/32 px): Raster 6,25 = 1 px bei 16 px
E_SMALL = e_path(18.75, 81.25, 18.75, 81.25, spine=18.75, arm=18.75, ro=3.125, ri=0)
VIAL_SMALL = rrect(43.75, 43.75, 37.5, 12.5, 6.25)


def glyph(e_fill, vial_fill, bubble_fill, small=False) -> str:
    if small:
        return (f'  <path d="{E_SMALL}" fill="{e_fill}"/>\n'
                f'  <path d="{VIAL_SMALL}" fill="{vial_fill}"/>\n')
    if bubble_fill is None:  # einfarbig: Blase ausgespart
        return (f'  <path d="{E}" fill="{e_fill}"/>\n'
                f'  <path d="{VIAL}{BUBBLE}" fill="{vial_fill}" fill-rule="evenodd"/>\n')
    return (f'  <path d="{E}" fill="{e_fill}"/>\n'
            f'  <path d="{VIAL}" fill="{vial_fill}"/>\n'
            f'  <path d="{BUBBLE}" fill="{bubble_fill}"/>\n')


def tile(shape: str, small=False) -> str:
    return f'  <path d="{shape}" fill="url(#eo-bg)"/>\n' + glyph(WHITE, SKY, GREEN, small)


ROUND = squircle(0, 0, 100)
SQUARE = "M0 0H100V100H0Z"


GLYPH_SCALE = 1.15  # freistehendes E im Lockup: 60 hoch = 1,5 × Versalhöhe


def lockup(icon_body: str, defs: str, word_ink: str, one_ink: str, comment: str, free: bool = False) -> str:
    """Horizontales Lockup: Kachel 100 hoch, Abstand 26, Versalhöhe 40 (optisch zur Symbolmitte).
    free=True: Bildmarke ohne Kachel, auf 1,15 skaliert und an den linken Rand gerückt."""
    x = 126
    if free:
        sc = GLYPH_SCALE
        icon_body = (f'  <g transform="translate({num(-26 * sc)} {num(50 - 50 * sc)}) scale({num(sc)})">\n'
                     + icon_body + '  </g>\n')
        x = 48 * sc + 24
    runs, end = text_path([("Estrich", FONT_REG, word_ink), ("ONE", FONT_BOLD, one_ink)],
                          cap_height=40, x=x, baseline=70, tracking=-12)
    body = icon_body + "".join(f'  <path d="{d}" fill="{c}"/>\n' for c, d in runs)
    return svg(round(end + 1), 100, body, defs=defs, comment=comment)


ANDROID = OUT.parent / "android/app/src/main/res/drawable"


def android_vector(comment: str, paths: list[tuple[str, str, bool]]) -> str:
    """Adaptives Android-Symbol 108 dp: sichtbar sind 72 dp, sicher 66 dp (Kreis). Das 100er-Raster wird auf
    die sichtbaren 72 dp gelegt (Faktor 0,72, Versatz 18); die Ecken des E liegen bei 25,5 dp vom Mittelpunkt."""
    body = "".join(
        f'        <path android:fillColor="{c}"{' android:fillType="evenOdd"' if eo else ''} android:pathData="{d}" />\n'
        for d, c, eo in paths)
    return ('<?xml version="1.0" encoding="utf-8"?>\n'
            f'<!-- {comment} Erzeugt von branding/tools/final.py – nicht von Hand ändern. -->\n'
            '<vector xmlns:android="http://schemas.android.com/apk/res/android"\n'
            '    android:width="108dp" android:height="108dp" android:viewportWidth="108" android:viewportHeight="108">\n'
            '    <group android:scaleX="0.72" android:scaleY="0.72" android:translateX="18" android:translateY="18">\n'
            f'{body}    </group>\n</vector>\n')


def write_android():
    ANDROID.mkdir(parents=True, exist_ok=True)
    (ANDROID / "ic_launcher_foreground.xml").write_text(android_vector(
        "Vordergrund des App-Symbols: das Libellen-E (branding/logo.svg).",
        [(E, WHITE, False), (VIAL, SKY, False), (BUBBLE, GREEN, False)]))
    (ANDROID / "ic_launcher_monochrome.xml").write_text(android_vector(
        "Einfarbiges Zeichen für Themensymbole (Android 13+), Blase ausgespart.",
        [(E, WHITE, False), (VIAL + BUBBLE, WHITE, True)]))
    (ANDROID / "ic_launcher_background.xml").write_text(
        '<?xml version="1.0" encoding="utf-8"?>\n'
        '<!-- Hintergrund des App-Symbols: Navy-Verlauf wie branding/logo.svg. Erzeugt von branding/tools/final.py. -->\n'
        '<shape xmlns:android="http://schemas.android.com/apk/res/android" android:shape="rectangle">\n'
        f'    <gradient android:angle="315" android:startColor="{NAVY_2}" android:endColor="{NAVY}" android:type="linear" />\n'
        '</shape>\n')


def main():
    files = {
        # App-Symbole
        "logo.svg": svg(100, 100, tile(ROUND), defs=TILE_GRAD, comment=STORY + " App-Symbol, abgerundet (Web/Desktop)."),
        "logo-square.svg": svg(100, 100, tile(SQUARE), defs=TILE_GRAD, comment="App-Symbol vollflächig (iOS/Android maskieren selbst)."),
        "logo-small.svg": svg(100, 100, tile(ROUND, small=True), defs=TILE_GRAD,
                              comment="Kleinform für 16–32 px (Favicon, Fenstersymbol): auf das 16-px-Raster gelegt, ohne Blase."),
        # Bildmarke ohne Fläche
        "logo-mark.svg": svg(100, 100, glyph(NAVY, BLUE, GREEN), comment="Bildmarke, transparent, für helle Flächen."),
        "logo-mark-dark.svg": svg(100, 100, glyph(WHITE, SKY, GREEN), comment="Bildmarke, transparent, für dunkle Flächen."),
        "logo-mark-white.svg": svg(100, 100, glyph(WHITE, WHITE, None), comment="Bildmarke einfarbig weiß (High Contrast Black, Prägung)."),
        "logo-mark-black.svg": svg(100, 100, glyph("#000000", "#000000", None), comment="Bildmarke einfarbig schwarz (High Contrast White, Fax/Stempel)."),
        # Lockups
        "logo-horizontal.svg": lockup(tile(ROUND), TILE_GRAD, NAVY, NAVY_2, "Horizontales Logo für helle Flächen."),
        "logo-horizontal-white.svg": lockup(glyph(WHITE, SKY, GREEN), "", WHITE, SKY,
                                            "Horizontales Logo für dunkle Flächen (Bildmarke ohne Kachel).", free=True),
        "logo-horizontal-mono-black.svg": lockup(glyph("#000000", "#000000", None), "", "#000000", "#000000",
                                                 "Horizontales Logo einfarbig schwarz.", free=True),
        "logo-horizontal-mono-white.svg": lockup(glyph(WHITE, WHITE, None), "", WHITE, WHITE,
                                                 "Horizontales Logo einfarbig weiß.", free=True),
    }
    for name, content in files.items():
        (OUT / name).write_text(content)
    write_android()
    print("Logos geschrieben:", ", ".join(files), "+ Android-Vektoren")


if __name__ == "__main__":
    main()
