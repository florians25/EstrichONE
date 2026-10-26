"""Zweite Konzeptrunde (mutiger): sechs Richtungen als SVG in branding/concepts/v2/.

  python3 branding/tools/concepts2.py
"""
from __future__ import annotations

import math
from pathlib import Path

from brand import circle, num, rrect, squircle, svg, text_path

OUT = Path(__file__).resolve().parent.parent / "concepts" / "v2"
NIGHT, NIGHT_2, BLUE, BLUE_L, ICE = "#061536", "#0B2E66", "#0062CC", "#4DA3FF", "#CFE5FF"

TILE_DEFS = (f'<linearGradient id="t" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="{NIGHT_2}"/>'
             f'<stop offset="1" stop-color="{NIGHT}"/></linearGradient>'
             f'<radialGradient id="g" cx=".2" cy=".05" r=".95"><stop offset="0" stop-color="{BLUE}" stop-opacity=".75"/>'
             f'<stop offset="1" stop-color="{BLUE}" stop-opacity="0"/></radialGradient>'
             f'<linearGradient id="w" gradientUnits="userSpaceOnUse" x1="0" y1="20" x2="0" y2="80"><stop offset="0" stop-color="#FFFFFF"/>'
             f'<stop offset="1" stop-color="{ICE}"/></linearGradient>')
TILE = (f'  <path d="{squircle(0, 0, 100)}" fill="url(#t)"/>\n'
        f'  <path d="{squircle(0, 0, 100)}" fill="url(#g)"/>\n')


def poly(points) -> str:
    return "M" + "L".join(f"{num(x)} {num(y)}" for x, y in points)


def wave(y0, amp, x0=22, x1=78, settle=0.75, periods=1.0):
    """Linie, die links wellt und nach rechts exakt eben ausläuft."""
    pts = []
    for i in range(61):
        t = i / 60
        decay = 0.5 * (1 + math.cos(math.pi * min(1.0, t / settle)))
        pts.append((x0 + (x1 - x0) * t, y0 + amp * decay * math.sin(2 * math.pi * periods * t)))
    return poly(pts)


def c_flow(on_dark=True):
    """A · Nivellierwelle: drei Lagen Fließestrich – oben noch in Bewegung, unten exakt eben. Zusammen ein „E“-Rhythmus."""
    ink = "url(#w)" if on_dark else NIGHT
    lines = [(wave(30, 6), ink), (wave(50, 3), BLUE_L if on_dark else BLUE), (wave(70, 0), ink)]
    return "".join(f'  <path d="{d}" fill="none" stroke="{c}" stroke-width="11" stroke-linecap="round" stroke-linejoin="round"/>\n'
                   for d, c in lines)


ONE = [(47, 22), (61, 22), (61, 78), (47, 78), (47, 39), (36, 46), (31, 37)]


def c_layers(on_dark=True):
    """B · Schicht-Eins: die „1“ aus drei versetzten Lagen – Rohdecke, Dämmung, Estrich – mit echter Tiefe."""
    base = poly(ONE) + "Z"
    cols = ([NIGHT_2, BLUE, "url(#w)"] if on_dark else ["#BFD9F7", BLUE_L, NIGHT])
    out = ""
    for i, c in enumerate(cols):
        d = 2 - i
        out += (f'  <path d="{base}" transform="translate({num(d * 5)} {num(d * 5)})" fill="{c}" '
                f'stroke="{c}" stroke-width="3" stroke-linejoin="round"/>\n')
    return out


def c_mono(on_dark=True):
    """C · E1-Monogramm: der Rücken des E trägt den Anstrich der „1“ – EstrichONE in einem Zeichen."""
    ink = "url(#w)" if on_dark else NIGHT
    e = poly([(32, 24), (74, 24), (74, 36), (45, 36), (45, 64), (74, 64), (74, 76), (32, 76), (32, 40), (22, 46), (18, 36)]) + "Z"
    mid = rrect(51, 44, 19, 12, 6)
    return (f'  <path d="{e}" fill="{ink}" stroke="{ink}" stroke-width="3" stroke-linejoin="round"/>\n'
            f'  <path d="{mid}" fill="{BLUE_L if on_dark else BLUE}"/>\n')


def c_grid(on_dark=True):
    """D · Fugenraster: das E aus acht Estrichfeldern mit Scheinfugen; das fehlende Feld ist die Libellenblase."""
    ink = "url(#w)" if on_dark else NIGHT
    out = ""
    for r in range(3):
        for c in range(3):
            x, y = 22 + c * 20, 22 + r * 20
            if (r, c) == (1, 2):
                out += f'  <path d="{circle(x + 8, y + 8, 5)}" fill="{BLUE_L if on_dark else BLUE}"/>\n'
            elif (r, c) == (1, 1):
                out += f'  <path d="{rrect(x, y, 16, 16, 4)}" fill="{BLUE_L if on_dark else BLUE}"/>\n'
            else:
                out += f'  <path d="{rrect(x, y, 16, 16, 4)}" fill="{ink}"/>\n'
    return out


def c_vial(on_dark=True):
    """E · Libelle: die Wasserwaage auf ihr Wesen reduziert – eine Kapsel, eine Blase, zwei Marken. Eben."""
    ink = "#FFFFFF" if on_dark else NIGHT
    return (f'  <path d="{rrect(15, 33, 70, 34, 17)}" fill="none" stroke="{ink}" stroke-width="8"/>\n'
            f'  <path d="M38 41V59M62 41V59" stroke="{ink}" stroke-width="4" stroke-linecap="round" opacity=".55"/>\n'
            f'  <path d="{circle(50, 50, 8)}" fill="{BLUE_L if on_dark else BLUE}"/>\n')


def c_glass(on_dark=True):
    """F · Glas-E: ein plastisches E aus gegossenem Material – Front glatt abgezogen, Kante in Markenblau."""
    e = poly([(28, 24), (72, 24), (72, 36), (41, 36), (41, 44), (64, 44), (64, 56), (41, 56), (41, 64), (72, 64),
              (72, 76), (28, 76)]) + "Z"
    out = ""
    for i in range(6, 0, -1):
        out += (f'  <path d="{e}" transform="translate({num(i)} {num(i)})" fill="{BLUE if i > 2 else BLUE_L}" '
                f'stroke="{BLUE if i > 2 else BLUE_L}" stroke-width="2" stroke-linejoin="round"/>\n')
    front = "url(#w)" if on_dark else "#FFFFFF"
    out += f'  <path d="{e}" fill="{front}" stroke="{front}" stroke-width="2" stroke-linejoin="round"/>\n'
    return out


CONCEPTS = [
    ("a", c_flow, ("geist-sans-latin-500-normal.woff", "geist-sans-latin-700-normal.woff")),
    ("b", c_layers, ("sora-latin-600-normal.woff", "sora-latin-700-normal.woff")),
    ("c", c_mono, ("inter-tight-latin-600-normal.woff", "inter-tight-latin-800-normal.woff")),
    ("d", c_grid, ("space-grotesk-latin-500-normal.woff", "space-grotesk-latin-700-normal.woff")),
    ("e", c_vial, ("manrope-latin-600-normal.woff", "manrope-latin-800-normal.woff")),
    ("f", c_glass, ("plus-jakarta-sans-latin-600-normal.woff", "plus-jakarta-sans-latin-800-normal.woff")),
]


def lockup(body, fonts, dark):
    reg, bold = fonts
    ink, one = ("#FFFFFF", BLUE_L) if dark else (NIGHT, BLUE)
    runs, end = text_path([("Estrich", reg, ink), ("ONE", bold, one)], cap_height=40, x=126, baseline=70, tracking=-14)
    return svg(round(end + 2), 100, TILE + body + "".join(f'  <path d="{d}" fill="{c}"/>\n' for c, d in runs), defs=TILE_DEFS)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    for key, fn, fonts in CONCEPTS:
        body = fn(True)
        doc = fn.__doc__.strip()
        (OUT / f"{key}-icon.svg").write_text(svg(100, 100, TILE + body, defs=TILE_DEFS, comment=doc))
        (OUT / f"{key}-mark.svg").write_text(svg(100, 100, fn(False), defs=TILE_DEFS))
        (OUT / f"{key}-lockup.svg").write_text(lockup(body, fonts, False))
        (OUT / f"{key}-lockup-dark.svg").write_text(lockup(body, fonts, True))
    print("Konzepte v2:", " ".join(k for k, *_ in CONCEPTS))


if __name__ == "__main__":
    main()
