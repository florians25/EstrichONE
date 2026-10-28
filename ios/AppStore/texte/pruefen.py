#!/usr/bin/env python3
"""Prüft die App-Store-Texte gegen die Zeichengrenzen von App Store Connect.

    python3 ios/AppStore/texte/pruefen.py        (Exit-Code 1 bei Fehlern; Platzhalter nur als Warnung)

Gezählt werden Zeichen (Unicode) ohne abschließenden Zeilenumbruch; Stichwörter zusätzlich in Bytes (UTF-8), weil
App Store Connect dort Umlaute teils doppelt zählt.
"""
import pathlib
import re
import sys

LIMITS = {"name.txt": 30, "subtitle.txt": 30, "promotional_text.txt": 170, "description.txt": 4000,
          "keywords.txt": 100, "release_notes.txt": 4000, "review_notes.txt": 4000, "review_notes_en.txt": 4000,
          "support_url.txt": 255, "marketing_url.txt": 255, "privacy_url.txt": 255, "copyright.txt": 255}
PFLICHT = ["name.txt", "subtitle.txt", "description.txt", "keywords.txt", "support_url.txt", "privacy_url.txt",
           "copyright.txt", "review_notes.txt"]
root = pathlib.Path(__file__).resolve().parent
fehler = warnungen = 0
for lang in sorted(p for p in root.iterdir() if p.is_dir()):
    print(f"[{lang.name}]")
    for name in PFLICHT:
        if not (lang / name).exists():
            print(f"  FEHLT    {name}"); fehler += 1
    for name, limit in LIMITS.items():
        path = lang / name
        if not path.exists():
            continue
        text = path.read_text(encoding="utf-8").rstrip("\n")
        n = len(text)
        probleme = []
        if n > limit: probleme.append(f"zu lang ({n} > {limit})")
        if not text.strip(): probleme.append("leer")
        if name in ("name.txt", "subtitle.txt", "keywords.txt", "promotional_text.txt") and "\n" in text:
            probleme.append("mehrzeilig")
        if name == "keywords.txt":
            b = len(text.encode("utf-8"))
            if b > limit:
                print(f"  HINWEIS  keywords.txt: {b} Bytes (UTF-8) – falls App Store Connect „zu lang“ meldet, ein Stichwort kürzen")
            if ", " in text or " ," in text: probleme.append("Leerzeichen an Kommas")
            woerter = [w.lower() for w in text.split(",")]
            if len(set(woerter)) != len(woerter): probleme.append("doppelte Stichwörter")
            if any(not w for w in woerter): probleme.append("leeres Stichwort")
        if name.endswith("_url.txt") and not re.fullmatch(r"https://\S+", text): probleme.append("keine https-URL")
        if re.search(r"[<>]", text) and name != "description.txt": probleme.append("spitze Klammern")
        platzhalter = "PLATZHALTER" in text
        status = "FEHLER" if probleme else ("WARNUNG" if platzhalter else "ok")
        extra = "; ".join(probleme) or ("Platzhalter ersetzen" if platzhalter else "")
        print(f"  {status:8} {name:22} {n:5}/{limit:<5} {extra}")
        fehler += bool(probleme); warnungen += platzhalter and not probleme
print(f"{fehler} Fehler, {warnungen} Platzhalter-Warnungen")
sys.exit(1 if fehler else 0)
