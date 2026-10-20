"""Erzeugt die vier Logo-Konzepte (branding/concepts/*.svg) und die HTML-Seite für die Vergleichstafel.

  python3 branding/tools/concepts.py
"""
from __future__ import annotations

from pathlib import Path

from brand import circle, num, rrect, squircle, svg, text_path

OUT = Path(__file__).resolve().parent.parent / "concepts"
NAVY, NAVY2, ACCENT, SKY = "#0B2D5B", "#174A8B", "#3CCB7F", "#8EC5FF"
GRAD = (f'<linearGradient id="bg" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="{NAVY2}"/>'
        f'<stop offset="1" stop-color="{NAVY}"/></linearGradient>')


# Jede Funktion liefert (Kachel-SVG-Körper, Zeichen ohne Kachel für helle Flächen, Schrift für die Wortmarke)

def concept_a():
    """A – Schichten-E: das E aus drei Estrichlagen, mittlere Lage (Dämmung) hell, Libelle als Akzent."""
    e = "M30 24H70A4 4 0 0 1 74 28V32A4 4 0 0 1 70 36H38V64H70A4 4 0 0 1 74 68V72A4 4 0 0 1 70 76H30A4 4 0 0 1 26 72V28A4 4 0 0 1 30 24Z"
    glyph = (f'  <path d="{e}" fill="#fff"/>\n'
             f'  <path d="{rrect(44, 44, 30, 12, 6)}" fill="{SKY}"/>\n'
             f'  <path d="{circle(59, 50, 3.6)}" fill="{ACCENT}"/>\n')
    tile = f'  <path d="{squircle(0, 0, 100)}" fill="url(#bg)"/>\n' + glyph
    light = glyph.replace('"#fff"', f'"{NAVY}"').replace(SKY, NAVY2)
    return tile, light, ("inter-tight-latin-600-normal.woff", "inter-tight-latin-800-normal.woff")


def concept_b():
    """B – Pegel: das O von ONE als Nivellierziel, Fließestrich steht exakt waagrecht, Laserlinie grün."""
    ring = f'{circle(50, 50, 28)}{circle(50, 50, 17)}'
    fill = 'M31 52H69V52A19 19 0 0 1 31 52Z'
    glyph = (f'  <path d="{ring}" fill="#fff" fill-rule="evenodd"/>\n'
             f'  <path d="M33 53H67A17 17 0 0 1 33 53Z" fill="{SKY}"/>\n'
             f'  <path d="{circle(50, 41.5, 4)}" fill="{ACCENT}"/>\n')
    tile = f'  <path d="{squircle(0, 0, 100)}" fill="url(#bg)"/>\n' + glyph
    light = glyph.replace('"#fff"', f'"{NAVY}"').replace(SKY, NAVY2)
    return tile, light, ("manrope-latin-600-normal.woff", "manrope-latin-800-normal.woff")


def concept_c():
    """C – Fugenplatte: die Kachel selbst ist eine Estrichplatte; zwei Scheinfugen schneiden das E heraus."""
    slab = squircle(0, 0, 100)
    # Platte 100 hoch: Arm 24 – Fuge 14 – Arm 24 – Fuge 14 – Arm 24; mittlerer Arm kürzer
    cuts = (f'{rrect(36, 24, 80, 14, (7, 0, 0, 7))}'
            f'{rrect(36, 62, 80, 14, (7, 0, 0, 7))}'
            f'{rrect(88, 30, 40, 40, 0)}')
    mask = (f'<mask id="m"><rect width="100" height="100" fill="#fff"/><path d="{cuts}" fill="#000"/></mask>')
    tile = (f'  <path d="{slab}" fill="url(#bg)" mask="url(#m)"/>\n'
            f'  <path d="{circle(76, 50, 5)}" fill="{ACCENT}"/>\n')
    light = tile
    return tile, light, ("red-hat-display-latin-500-normal.woff", "red-hat-display-latin-700-normal.woff"), mask


def concept_d():
    """D – Aufbau isometrisch: drei Lagen eines Estrichaufbaus übereinander, oberste Lage eben und grün gekantet."""
    def layer(y, color):
        return f'M50 {num(y)}L82 {num(y + 14)}L50 {num(y + 28)}L18 {num(y + 14)}Z', color
    parts = [layer(52, NAVY), layer(38, "#2F6FB8"), layer(24, "#fff")]
    glyph = "".join(f'  <path d="{d}" fill="{c}" stroke="{NAVY}" stroke-width="3" stroke-linejoin="round"/>\n' for d, c in parts)
    glyph = glyph.replace(f'fill="{NAVY}" stroke', f'fill="{SKY}" stroke')
    glyph += f'  <path d="M18 38L50 52L82 38" fill="none" stroke="{ACCENT}" stroke-width="0"/>\n'
    tile = f'  <path d="{squircle(0, 0, 100)}" fill="url(#bg)"/>\n' + glyph
    light = glyph
    return tile, light, ("plus-jakarta-sans-latin-600-normal.woff", "plus-jakarta-sans-latin-800-normal.woff")


def lockup(tile_body: str, defs: str, fonts, dark: bool) -> str:
    regular, bold = fonts
    ink = "#fff" if dark else NAVY
    one = ACCENT if dark else NAVY2
    runs, end = text_path([("Estrich", regular, ink), ("ONE", bold, one)], cap_height=36, x=128, baseline=68, tracking=-10)
    body = tile_body + "".join(f'  <path d="{d}" fill="{c}"/>\n' for c, d in runs)
    return svg(end + 4, 100, body, defs=defs)


def main():
    OUT.mkdir(exist_ok=True)
    pages = []
    for key, fn in (("a", concept_a), ("b", concept_b), ("c", concept_c), ("d", concept_d)):
        res = fn()
        tile, light, fonts = res[:3]
        extra = res[3] if len(res) > 3 else ""
        defs = GRAD + extra
        (OUT / f"concept-{key}-icon.svg").write_text(svg(100, 100, tile, defs=defs, comment=fn.__doc__.strip()))
        (OUT / f"concept-{key}-lockup.svg").write_text(lockup(tile, defs, fonts, False))
        (OUT / f"concept-{key}-lockup-dark.svg").write_text(lockup(tile, defs, fonts, True))
        pages.append((key, fn.__doc__.strip()))
    print("Konzepte geschrieben:", ", ".join(k for k, _ in pages))


if __name__ == "__main__":
    main()
