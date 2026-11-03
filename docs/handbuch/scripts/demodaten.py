#!/usr/bin/env python3
"""Demodaten der Beispielfirma „Estrichbau Muster GmbH“ für die Handbuch-Bilder (nur fiktive Personen und Adressen).

Läuft gegen die frisch eingerichtete Instanz aus umgebung.sh (nach `bilder.mjs einrichtung`) und nutzt ausschließlich
die öffentliche API wie die Apps. Bilder und PDFs für Fotos/Dokumente erzeugt ImageMagick (`convert`).
Aufruf: python3 demodaten.py [API-Adresse]   (Standard http://localhost:5191)
"""
import base64
import datetime as dt
import hashlib
import json
import os
import subprocess
import sys
import tempfile
import urllib.error
import urllib.request
import uuid

API = (sys.argv[1] if len(sys.argv) > 1 else "http://localhost:5191").rstrip("/") + "/api/v1/"
ADMIN = ("c.berger", "Estrich-Muster-2026!")
OPENER = urllib.request.build_opener(urllib.request.ProxyHandler({}))
TMP = tempfile.mkdtemp(prefix="hb-demo-")
TOKEN = ""
FAILS = 0


def call(method, path, body=None, raw=None, headers=None, token=None):
    global FAILS
    data = raw if raw is not None else (json.dumps(body).encode() if body is not None else None)
    h = {"content-type": "application/json" if raw is None else "application/octet-stream", "accept": "application/json"}
    h["authorization"] = "Bearer " + (token or TOKEN)
    h.update(headers or {})
    request = urllib.request.Request(API + path, method=method, data=data, headers=h)
    try:
        with OPENER.open(request) as answer:
            text = answer.read()
            return json.loads(text) if text and answer.headers.get("content-type", "").startswith("application/json") else None
    except urllib.error.HTTPError as error:
        FAILS += 1
        print("FEHLER", method, path, error.code, error.read()[:300].decode(errors="replace"))
        return None


def login(user, password):
    r = urllib.request.Request(API + "auth/login", method="POST", headers={"content-type": "application/json"},
                               data=json.dumps({"login": user, "password": password, "client": "other", "clientName": "Demodaten"}).encode())
    with OPENER.open(r) as answer:
        return json.loads(answer.read())


def new_id():
    return str(uuid.uuid4())


TODAY = dt.date.today()
MONDAY = TODAY - dt.timedelta(days=TODAY.weekday())
day = lambda offset: str(MONDAY + dt.timedelta(days=offset))


def magick(*args):
    subprocess.run(["convert", *args], check=True, capture_output=True)


def make_photo(name, kind, seed):
    """Baustellenartiges Bild ohne Personen: Estrichfläche, Fußbodenheizung, Dämmung, Randstreifen, Messung."""
    path = os.path.join(TMP, name)
    size = "1600x1200"
    if kind == "estrich":
        magick("-size", size, "-seed", str(seed), "plasma:gray62-gray48", "-blur", "0x2", "-attenuate", "0.6", "+noise", "Gaussian",
               "-modulate", "100,8,100", "(", "-size", size, "gradient:white-gray30", ")", "-compose", "multiply", "-composite", "-quality", "82", path)
    elif kind == "heizung":
        draw = []
        for i in range(9):
            y = 140 + i * 110
            draw += ["-draw", f"path 'M 80,{y} L 1500,{y} A 50,50 0 0,1 1500,{y + 55} L 120,{y + 55}'"]
        magick("-size", size, "-seed", str(seed), "plasma:#d9dde3-#b8c2cc", "-blur", "0x3", "-modulate", "100,10,100", "-fill", "none", "-stroke", "#c0392b",
               "-strokewidth", "18", *draw, "-stroke", "#7f8c8d", "-strokewidth", "3", "-draw", "line 0,600 1600,600", "+noise", "Gaussian",
               "-attenuate", "0.3", "-quality", "82", path)
    elif kind == "daemmung":
        draw = []
        for x in range(0, 1600, 400):
            for y in range(0, 1200, 300):
                draw += ["-draw", f"rectangle {x + 4},{y + 4} {x + 396},{y + 296}"]
        magick("-size", size, "xc:#f2f2ea", "-fill", "#e9ebe0", "-stroke", "#b9bcae", "-strokewidth", "6", *draw, "-seed", str(seed),
               "-attenuate", "0.5", "+noise", "Gaussian", "-blur", "0x1", "-quality", "82", path)
    elif kind == "rand":
        magick("-size", size, "-seed", str(seed), "plasma:gray70-gray55", "-blur", "0x2", "-modulate", "100,6,100", "-fill", "#e6e3d6", "-stroke", "none",
               "-draw", "rectangle 0,0 1600,260", "-fill", "#f4f1e4", "-draw", "rectangle 0,260 1600,300", "-fill", "#2e86c1",
               "-draw", "rectangle 0,300 1600,312", "+noise", "Gaussian", "-attenuate", "0.4", "-quality", "82", path)
    else:  # messung
        magick("-size", size, "-seed", str(seed), "plasma:gray66-gray50", "-blur", "0x2", "-modulate", "100,6,100", "-fill", "#f1c40f", "-stroke", "#7d6608",
               "-strokewidth", "6", "-draw", "roundrectangle 560,380 1040,820 30,30", "-fill", "#1c2833", "-draw", "rectangle 640,450 960,600",
               "-fill", "#58d68d", "-stroke", "none", "-pointsize", "90", "-annotate", "+690+560", "1,8 %", "-quality", "82", path)
    return path


def make_pdf(name, title, lines):
    path = os.path.join(TMP, name)
    args = ["-size", "1240x1754", "xc:white", "-fill", "#061536", "-pointsize", "44", "-annotate", "+110+190", "Estrichbau Muster GmbH",
            "-fill", "#0062CC", "-draw", "rectangle 110,220 1130,226", "-fill", "#111111", "-pointsize", "38", "-annotate", "+110+330", title,
            "-pointsize", "26"]
    for i, line in enumerate(lines):
        args += ["-annotate", f"+110+{420 + i * 46}", line]
    magick(*args, "-quality", "80", path)
    return path


def upload(path, bauakte, kind, content_type, name=None, caption=None, category=None, folder=None, captured=None, previous=None):
    data = open(path, "rb").read()
    fid = new_id()
    body = {"id": fid, "bauakteId": bauakte, "kind": kind, "originalName": name or os.path.basename(path), "contentType": content_type,
            "sizeBytes": len(data), "sha256": hashlib.sha256(data).hexdigest(), "capturedAt": captured, "caption": caption,
            "category": category, "folder": folder, "previousVersionId": previous}
    if call("POST", "files", body) is None and FAILS:
        return None
    call("PUT", f"files/{fid}/content", raw=data, headers={"content-range": f"bytes 0-{len(data) - 1}/{len(data)}"})
    return fid


def sync(kind, entity, payload):
    global FAILS
    result = call("POST", "sync/push", {"operations": [{"opId": new_id(), "type": kind, "entityId": entity, "baseVersion": None, "payload": payload}]})
    for r in (result or {}).get("results", []):
        if r.get("status") in ("rejected", "conflict"):
            FAILS += 1
            print("FEHLER sync", kind, r.get("code"), r.get("message"))
    return result


def signature(text, seed):
    path = os.path.join(TMP, f"sig{seed}.png")
    magick("-size", "600x200", "xc:none", "-fill", "none", "-stroke", "#0b2e66", "-strokewidth", "4",
           "-draw", f"bezier 40,140 {90 + seed * 7},20 160,190 230,90", "-draw", "bezier 230,90 300,30 330,170 400,110",
           "-draw", f"bezier 400,110 450,60 {500 - seed * 5},180 560,80", path)
    return base64.b64encode(open(path, "rb").read()).decode()


def main():
    global TOKEN
    TOKEN = login(*ADMIN)["accessToken"]
    me = call("GET", "auth/me") or {}
    admin_id = (me.get("user") or me).get("id")
    roles = {r["name"]: r["id"] for r in call("GET", "access/roles")}

    call("PUT", "company", {"name": "Estrichbau Muster GmbH", "street": "Gewerbering 14", "postalCode": "84030", "city": "Ergolding",
                            "phone": "0871 965 40-0", "email": "info@estrichbau-muster.de", "website": "www.estrichbau-muster.de"})
    logo = os.path.join(TMP, "firmenlogo.png")
    magick("-size", "900x240", "xc:none", "-fill", "#0B2E66", "-draw", "roundrectangle 10,20 210,220 36,36", "-fill", "white", "-pointsize", "120",
           "-annotate", "+52+170", "M", "-fill", "#0B2E66", "-pointsize", "84", "-annotate", "+250+130", "Estrichbau", "-fill", "#0062CC",
           "-pointsize", "56", "-annotate", "+254+205", "Muster GmbH", logo)
    call("PUT", "company/logo", raw=open(logo, "rb").read(), headers={"content-type": "image/png"})

    # Benutzer und Kolonnen
    people = [("Martina Keller", "m.keller", "Büro und Bauleitung"), ("Thomas Hofmann", "t.hofmann", "Büro und Bauleitung"),
              ("Markus Huber", "m.huber", "Baustelle"), ("Stefan Wagner", "s.wagner", "Baustelle"), ("Jonas Schmid", "j.schmid", "Baustelle"),
              ("Lukas Brenner", "l.brenner", "Baustelle"), ("Ali Demir", "a.demir", "Baustelle"), ("Peter Zimmermann", "p.zimmermann", "Baustelle"),
              ("Florian Steiner", "f.steiner", "Baustelle"), ("Kevin Albrecht", "k.albrecht", "IT-Administration")]
    users = {}
    for name, login_name, role in people:
        r = call("POST", "users", {"displayName": name, "userName": login_name, "email": f"{login_name}@estrichbau-muster.de",
                                   "phone": None, "roleIds": [roles[role]]})
        if r:
            users[login_name] = r.get("id") or (r.get("user") or {}).get("id")
    u = lambda k: users.get(k)
    crews = {}
    for i, (name, color, leader, members) in enumerate([("Kolonne Huber", "#1F6FEB", "m.huber", ["m.huber", "j.schmid"]),
                                                        ("Kolonne Wagner", "#2DA44E", "s.wagner", ["s.wagner", "l.brenner"]),
                                                        ("Kolonne Demir", "#D97706", "a.demir", ["a.demir", "p.zimmermann", "f.steiner"])]):
        cid = new_id()
        call("PUT", f"planning/crews/{cid}", {"name": name, "leaderUserId": u(leader), "memberUserIds": [u(m) for m in members if u(m)],
                                              "color": color, "isActive": True, "sortOrder": i})
        crews[name] = cid
    resources = []
    for kind, name, ident, model, crew in [("vehicle", "MAN TGE Pritsche", "LA-EM 214", "MAN TGE 3.180", "Kolonne Huber"),
                                           ("vehicle", "Mercedes Sprinter", "LA-EM 318", "Sprinter 316 CDI", "Kolonne Wagner"),
                                           ("pump", "Estrichboy Brinkmann", "EB-02", "Brinkmann Estrich-Boy 260", None),
                                           ("pump", "Fließestrichpumpe", "FP-01", "m-tec duo-mix 2000", None),
                                           ("machine", "Glättmaschine", "GM-03", "Tellerglätter 900", None)]:
        rid = new_id()
        call("PUT", f"planning/resources/{rid}", {"kind": kind, "name": name, "identifier": ident, "notes": None, "isActive": True, "model": model,
                                                  "inspectionDue": day(40) if kind == "vehicle" else None, "serviceDue": day(75),
                                                  "crewId": crews.get(crew) if crew else None, "documentFileIds": []})
        resources.append(rid)

    # Kunden und Kontakte
    customers = {}
    for kind, name, number, street, plz, city, phone, mail in [
            ("company", "Bauträger Sonnenhof GmbH", "K-10021", "Industriestraße 12", "84030", "Ergolding", "0871 966 30-0", "info@sonnenhof-bau.example"),
            ("person", "Familie Oberländer", "K-10022", "Lindenweg 4", "84144", "Geisenhausen", "08743 912 34", "oberlaender@mail.example"),
            ("company", "Wohnbau Isar AG", "K-10023", "Isarpromenade 1", "84028", "Landshut", "0871 430 02-00", "bau@wohnbau-isar.example"),
            ("person", "Dr. Katharina Wimmer", "K-10024", "Am Mühlbach 17a", "84036", "Kumhausen", "0171 555 12 34", "k.wimmer@mail.example"),
            ("company", "Architekturbüro Brandl & Partner", "K-10025", "Altstadt 88", "84028", "Landshut", "0871 223 34", "office@brandl-architekten.example"),
            ("person", "Josef Gruber", "K-10026", "Hauptstraße 3", "84155", "Bodenkirchen", "08741 98 76", None),
            ("company", "Gemeinde Altdorf – Bauamt", "K-10027", "Dekan-Wagner-Straße 13", "84032", "Altdorf", "0871 303 30", "bauamt@altdorf.example")]:
        cid = new_id()
        call("POST", "contacts", {"id": cid, "kind": kind, "name": name, "customerNumber": number, "street": street, "postalCode": plz, "city": city,
                                  "phone": phone, "email": mail,
                                  "phones": [{"kind": "office" if kind == "company" else "landline", "number": phone, "label": None}] if phone else [],
                                  "emails": [{"address": mail, "label": None}] if mail else []})
        customers[name] = cid

    akten = []
    files_specs = [
        ("Neubau Mehrfamilienhaus Sonnenhof, Haus B", "Sonnenhofstraße 5", "84030", "Ergolding", "inProgress", 48.552, 12.207, "Bauträger Sonnenhof GmbH",
         {"objectType": "mfh", "totalArea": 812.5, "screedType": "ct", "construction": "heated", "nominalThicknessMm": 65, "strengthClass": "C25-F4",
          "underfloorHeating": True, "heatingType": "water", "insulation": "eps", "insulationThicknessMm": 40, "floors": "EG–3. OG", "priority": "high"}),
        ("Einfamilienhaus Oberländer", "Lindenweg 4", "84144", "Geisenhausen", "planned", 48.475, 12.258, "Familie Oberländer",
         {"objectType": "efh", "totalArea": 164, "screedType": "ct", "construction": "heated", "nominalThicknessMm": 65, "underfloorHeating": True,
          "heatingType": "water", "insulation": "eps", "floors": "EG, OG"}),
        ("Wohnanlage Isarpark, 3. Bauabschnitt", "Isarpark 10", "84028", "Landshut", "planned", 48.537, 12.152, "Wohnbau Isar AG",
         {"objectType": "mfh", "totalArea": 1430, "screedType": "caf", "construction": "floating", "nominalThicknessMm": 50, "strengthClass": "CA-C25-F4"}),
        ("Praxis Dr. Wimmer – Sanierung", "Am Mühlbach 17a", "84036", "Kumhausen", "inquiry", 48.511, 12.180, "Dr. Katharina Wimmer",
         {"objectType": "renovation", "totalArea": 96, "screedType": "ct", "construction": "separatingLayer"}),
        ("Bürogebäude Altstadt 88", "Altstadt 88", "84028", "Landshut", "drying", 48.536, 12.151, "Architekturbüro Brandl & Partner",
         {"objectType": "commercial", "totalArea": 540, "screedType": "caf", "construction": "floating", "nominalThicknessMm": 45}),
        ("Garage Gruber – Verbundestrich", "Hauptstraße 3", "84155", "Bodenkirchen", "inquiry", 48.390, 12.380, "Josef Gruber",
         {"objectType": "other", "totalArea": 42, "screedType": "ct", "construction": "bonded"}),
        ("Reihenhaus Kastanienweg 7", "Kastanienweg 7", "84034", "Landshut", "planned", 48.548, 12.170, "Familie Oberländer",
         {"objectType": "efh", "totalArea": 118, "screedType": "ct", "construction": "heated", "underfloorHeating": True}),
        ("Kinderhaus Altdorf, Erweiterung", "Dekan-Wagner-Straße 20", "84032", "Altdorf", "completed", 48.561, 12.111, "Gemeinde Altdorf – Bauamt",
         {"objectType": "public", "totalArea": 380, "screedType": "ct", "construction": "heated", "underfloorHeating": True}),
    ]
    for i, (title, street, plz, city, status, lat, lon, customer, project) in enumerate(files_specs):
        aid = new_id()
        call("POST", "bauakten", {"id": aid, "title": title, "street": street, "postalCode": plz, "city": city, "latitude": lat, "longitude": lon,
                                  "customerId": customers[customer], "status": status, "project": project,
                                  "description": "Estricharbeiten laut Angebot inkl. Randdämmstreifen, PE-Folie und Bewegungsfugen.",
                                  "plannedStart": day(-7 + i * 3), "plannedEnd": day(10 + i * 3)})
        akten.append(aid)
    sonnenhof, oberlaender, isarpark, wimmer, altstadt, gruber, kastanienweg, kinderhaus = akten

    # Zuständige und Beteiligte
    for aid, member in [(sonnenhof, "m.keller"), (oberlaender, "t.hofmann"), (isarpark, "m.keller"), (altstadt, "t.hofmann")]:
        call("PUT", f"bauakten/{aid}/members", {"userIds": [u(member), admin_id]})
    for role, name, company, phone, mail in [("siteManager", "Andreas Lechner", "Bauträger Sonnenhof GmbH", "0160 441 22 10", "a.lechner@sonnenhof-bau.example"),
                                             ("architect", "Sabine Brandl", "Architekturbüro Brandl & Partner", "0871 223 34", "s.brandl@brandl-architekten.example"),
                                             ("planner", "Ingenieurbüro Weiß (Heizung)", None, "0871 77 88 90", None)]:
        call("PUT", f"bauakten/{sonnenhof}/parties/{new_id()}", {"role": role, "contactId": None, "name": name, "company": company, "phone": phone,
                                                                  "email": mail, "note": None, "version": None})

    # Räume, Aufgaben, Mängel, Notizen
    rooms = {}
    for aid in [sonnenhof, oberlaender, isarpark, altstadt, kinderhaus]:
        rooms[aid] = []
        for j, (name, floor, area) in enumerate([("Wohnen/Essen", "EG", 38.5), ("Küche", "EG", 14.2), ("Flur", "EG", 11.3), ("Bad", "OG", 9.8),
                                                  ("Schlafen", "OG", 18.0), ("Kind 1", "OG", 13.4)]):
            rid = new_id()
            call("PUT", f"bauakten/{aid}/rooms/{rid}", {"id": rid, "sectionId": None, "name": name, "floor": floor, "areaSquareMeters": area,
                                                         "screedTypeId": None, "thicknessMillimeters": 65, "insulationMillimeters": 40,
                                                         "underfloorHeating": name != "Flur", "status": ["laid", "laid", "ready", "planned", "planned", "planned"][j],
                                                         "notes": "Bewegungsfuge an der Türschwelle" if name == "Küche" else None})
            rooms[aid].append(rid)
    work = [(sonnenhof, "task", "Randdämmstreifen im 2. OG prüfen und ergänzen", -1, "m.huber"),
            (sonnenhof, "defect", "Haarriss im Bereich Türschwelle Küche, Whg. 4", 2, "s.wagner"),
            (sonnenhof, "task", "CM-Messung Haus B mit Bauleitung abstimmen", 3, "m.keller"),
            (oberlaender, "task", "Heizkreisverteiler: Druckprotokoll anfordern", 1, "t.hofmann"),
            (isarpark, "task", "Anlieferung Fließestrich Silo bestätigen", 0, "m.keller"),
            (altstadt, "defect", "Höhenabweichung Flur 3. OG (6 mm) nacharbeiten", 4, "a.demir"),
            (wimmer, "task", "Aufmaß vor Ort vereinbaren", 2, "t.hofmann")]
    work_ids = []
    for aid, kind, title, due, who in work:
        wid = new_id()
        call("POST", "work-items", {"id": wid, "bauakteId": aid, "kind": kind, "title": title, "description": None, "dueDate": str(TODAY + dt.timedelta(days=due)),
                                    "assigneeUserId": u(who) or admin_id})
        work_ids.append(wid)
    # Eine Aufgabe für die Administratorin (Karte „Meine Aufgaben“)
    call("POST", "work-items", {"id": new_id(), "bauakteId": sonnenhof, "kind": "task", "title": "Nachtragsangebot Bewegungsfugen freigeben",
                                "description": None, "dueDate": str(TODAY), "assigneeUserId": admin_id})
    call("POST", "work-items", {"id": new_id(), "bauakteId": oberlaender, "kind": "task", "title": "Termin Belegreifprüfung mit Familie Oberländer",
                                "description": None, "dueDate": str(TODAY + dt.timedelta(days=2)), "assigneeUserId": admin_id})
    for aid, text in [(sonnenhof, "Zufahrt nur über die Baustraße Nord, Schlüssel für Haus B beim Polier (Herr Lechner)."),
                      (oberlaender, "Bauherr wünscht Rückruf vor Arbeitsbeginn; Hund im Garten."),
                      (isarpark, "Silostellplatz mit der Bauleitung abgestimmt: Einfahrt Isarpark 12.")]:
        call("POST", f"bauakten/{aid}/notes", {"id": new_id(), "text": text})

    # Fotos und Dokumente
    photo_ids = []
    for n, (kind, caption) in enumerate([("daemmung", "Dämmung EG verlegt"), ("rand", "Randdämmstreifen und PE-Folie"),
                                         ("heizung", "Fußbodenheizung Wohnen/Essen"), ("heizung", "Heizkreise Küche und Flur"),
                                         ("estrich", "Zementestrich eingebracht, EG"), ("estrich", "Oberfläche abgezogen"),
                                         ("messung", "CM-Messung Bad"), ("estrich", "Estrich OG nach 7 Tagen")]):
        captured = dt.datetime.combine(TODAY - dt.timedelta(days=8 - n), dt.time(7 + n, 15)).isoformat() + "Z"
        fid = upload(make_photo(f"IMG_{4810 + n}.jpg", kind, n + 3), sonnenhof, "photo", "image/jpeg", caption=caption, captured=captured)
        photo_ids.append(fid)
    for aid, kind in [(oberlaender, "daemmung"), (altstadt, "estrich"), (kinderhaus, "estrich")]:
        upload(make_photo(f"IMG_{5000 + len(aid) % 7}.jpg", kind, 11), aid, "photo", "image/jpeg", caption="Baustellenfoto")
    docs = {}
    for name, title, lines, category, folder in [
            ("Angebot A-2026-118.pdf", "Angebot A-2026-118", ["Zementestrich CT-C25-F4, 65 mm, auf Fußbodenheizung", "Randdämmstreifen, PE-Folie", "Fläche ca. 812 m²"], "offer", None),
            ("Auftragsbestätigung.pdf", "Auftragsbestätigung", ["Bezug: Angebot A-2026-118", "Ausführung KW 45–47"], "order", None),
            ("Grundriss EG Haus B.pdf", "Grundriss Erdgeschoss Haus B", ["Maßstab 1:100", "Estrichflächen und Fugenplan"], "plan", None),
            ("Fugenplan Haus B.pdf", "Fugenplan", ["Bewegungsfugen an Türen und Feldgrenzen > 40 m²"], "plan", None),
            ("Lieferschein 778812.pdf", "Lieferschein 778812", ["CEM II 42,5 R – 12 t", "Estrichsand 0/8 – 38 t"], "delivery", None),
            ("Aufheizprotokoll.pdf", "Aufheizprotokoll", ["Beginn Funktionsheizen nach 21 Tagen", "Vorlauftemperatur 25 °C"], "protocol", None)]:
        docs[name] = upload(make_pdf(name.replace(" ", "_"), title, lines), sonnenhof, "document", "application/pdf", name=name, category=category, folder=folder)
    # Neue Fassung des Angebots
    v2 = make_pdf("Angebot_v2.pdf", "Angebot A-2026-118 (Fassung 2)", ["Nachtrag: Bewegungsfugen Haus B", "Fläche 812,5 m²"])
    upload(v2, sonnenhof, "document", "application/pdf", name="Angebot A-2026-118.pdf", category="offer", previous=docs["Angebot A-2026-118.pdf"])
    # Freigaben
    for name, who, msg in [("Fugenplan Haus B.pdf", admin_id, "Bitte den Fugenplan vor Ausführung freigeben."),
                           ("Aufheizprotokoll.pdf", u("m.keller"), "Aufheizprotokoll zur Weitergabe an den Bauträger prüfen.")]:
        if docs.get(name):
            call("POST", f"files/{docs[name]}/approvals", {"id": new_id(), "assigneeUserId": who, "assigneeRoleId": None, "message": msg,
                                                           "dueDate": str(TODAY + dt.timedelta(days=3))})

    # Bautagebuch, Erstbegehung, Abnahme
    for offset, weather, temp, text, crew, n in [
            (-6, "cloudy", 8, "Dämmung und Randdämmstreifen im EG verlegt, PE-Folie ausgelegt. Heizungsbauer hat Druckprobe durchgeführt.", "Kolonne Huber", 3),
            (-5, "sunny", 11, "Zementestrich CT-C25-F4 im EG eingebracht (ca. 210 m²), abgezogen und geglättet. Lieferung Sand 38 t.", "Kolonne Huber", 3),
            (-2, "rain", 6, "OG Wohnungen 3 und 4 fertiggestellt. Fenster geschlossen gehalten, Baustelle gegen Zugluft gesichert.", "Kolonne Wagner", 2)]:
        sync("diary.create", new_id(), {"bauakteId": sonnenhof, "day": str(TODAY + dt.timedelta(days=offset)), "text": text, "weather": weather,
                                        "temperatureCelsius": temp, "workersPresent": n, "startTime": "07:00:00", "endTime": "16:00:00", "crew": crew,
                                        "photoFileIds": [p for p in photo_ids[:3] if p] if offset == -6 else []})
    for n, aid in enumerate([sonnenhof, altstadt, kinderhaus]):
        checks = {"substrate": "ok", "edgeInsulation": "ok", "heatingPressureTest": "ok" if aid != altstadt else "notApplicable",
                  "access": "ok", "utilities": "ok" if n else "notOk"}
        sync("inspection.create", new_id(), {"bauakteId": aid, "kind": "initialInspection", "notRequired": False, "checks": checks,
                                             "remarks": "Rohdecke besenrein, Fenster eingebaut, Putz abgeschlossen. Höhenbezug (Meterriss) vorhanden.",
                                             "notes": None, "values": {}, "checkRemarks": {} if n else {"utilities": "Bauwasser erst ab KW 45 – Wassertank mitbringen"},
                                             "schemaVersion": None, "photoFileIds": [p for p in photo_ids[:2] if p] if aid == sonnenhof else [],
                                             "correctsInspectionId": None, "performedAt": (dt.datetime.now() - dt.timedelta(days=12)).isoformat() + "Z"})
    sync("acceptance.create", new_id(), {"bauakteId": kinderhaus, "performedAt": (dt.datetime.now() - dt.timedelta(days=3)).isoformat() + "Z",
                                         "location": "Kinderhaus Altdorf", "clientName": "Herr Bauer (Bauamt)", "contractorName": "Claudia Berger",
                                         "scope": "Heizestrich EG, 380 m²", "roomIds": rooms[kinderhaus][:3], "result": "accepted", "notes": "Ohne Mängel abgenommen.",
                                         "photoFileIds": [], "defectWorkItemIds": [],
                                         "clientSignature": {"png": signature("Bauer", 1), "signedAt": dt.datetime.now().isoformat() + "Z"},
                                         "contractorSignature": {"png": signature("Berger", 2), "signedAt": dt.datetime.now().isoformat() + "Z"}})

    # Planung: Einsätze der Woche, Abwesenheiten, Kalender
    # Je Kolonne und Tag ohne Überschneidung; Peter Zimmermann ist heute krank gemeldet (zeigt einen echten Konflikt).
    week = {
        "Kolonne Huber": [(sonnenhof, "Estrich einbringen 1. OG", "screed", "07:00", "16:00"), (sonnenhof, "Estrich einbringen 2. OG", "screed", "07:00", "16:00"),
                          (sonnenhof, "Estrich einbringen 3. OG", "screed", "07:00", "16:00"), (sonnenhof, "Bewegungsfugen schneiden", "other", "07:00", "12:00"),
                          (oberlaender, "Dämmung und Randstreifen EG", "screed", "07:00", "15:00")],
        "Kolonne Wagner": [(isarpark, "Fließestrich Haus 3, EG", "screed", "06:30", "15:00"), (isarpark, "Fließestrich Haus 3, 1. OG", "screed", "06:30", "15:00"),
                           (isarpark, "Fließestrich Haus 3, 2. OG", "screed", "06:30", "15:00"), (altstadt, "Nacharbeiten Flur 3. OG", "other", "07:00", "11:30"),
                           None],
        "Kolonne Demir": [(kinderhaus, "Nacharbeiten Sockel", "other", "07:30", "11:00"), (oberlaender, "Erstbegehung mit Bauherr", "initialInspection", "08:00", "09:30"),
                          (altstadt, "CM-Messung 3. OG", "cmMeasurement", "08:00", "10:00"), (wimmer, "Aufmaß Praxisräume", "survey", "08:00", "10:00"),
                          (sonnenhof, "Funktionsheizen einleiten (Aufheizprotokoll)", "check", "07:30", "09:00")],
    }
    extra = {(1, "Kolonne Demir"): (altstadt, "Belegreifprüfung Büro 2. OG", "cmMeasurement", "10:30", "12:00")}
    for crew, days in week.items():
        for d, entry in enumerate(days):
            for item in [entry, extra.get((d, crew))]:
                if not item:
                    continue
                aid, activity, kind, start, end = item
                res = [resources[0]] if crew == "Kolonne Huber" else [resources[1]] if crew == "Kolonne Wagner" else []
                if kind == "screed" and crew == "Kolonne Huber":
                    res = res + [resources[2]]
                if kind == "screed" and crew == "Kolonne Wagner":
                    res = res + [resources[3]]
                call("PUT", f"planning/assignments/{new_id()}", {"bauakteId": aid, "day": day(d), "startTime": start, "endTime": end, "activity": activity,
                                                                 "kind": kind, "crewId": crews[crew], "userIds": [], "resourceIds": res, "notes": None})
    # Termin eines Bauleiters ohne Kolonne (Teamplaner: „Einzelne Mitarbeiter“)
    call("PUT", f"planning/assignments/{new_id()}", {"bauakteId": gruber, "day": day(3), "startTime": "08:00", "endTime": "12:00",
                                                     "activity": "Aufmaß Garage", "kind": "survey", "crewId": None, "userIds": [u("t.hofmann")], "resourceIds": [], "notes": None})
    call("PUT", f"planning/absences/{new_id()}", {"userId": u("j.schmid"), "from": day(7), "to": day(11), "kind": "vacation", "note": "Herbsturlaub"})
    call("PUT", f"planning/absences/{new_id()}", {"userId": u("p.zimmermann"), "from": str(TODAY), "to": str(TODAY + dt.timedelta(days=1)), "kind": "sick", "note": None})
    calendars = {c["name"]: c["id"] for c in call("GET", "calendar/calendars") or []}
    cal = calendars.get("Firmenkalender") or next(iter(calendars.values()), None)
    if cal:
        for offset, title, kind, start, end, loc in [(1, "Baubesprechung Sonnenhof", "meeting", "09:00", "10:30", "Baucontainer Sonnenhof"),
                                                     (2, "Kundentermin Familie Brunner", "customerAppointment", "14:00", "15:00", "Altdorf"),
                                                     (3, "Lieferung Fließestrich Isarpark", "delivery", "06:30", "07:30", "Isarpark 12"),
                                                     (4, "Schulung Arbeitssicherheit", "training", "13:00", "16:00", "Büro Ergolding")]:
            call("PUT", f"calendar/events/{new_id()}", {"calendarId": cal, "title": title, "location": loc, "description": None, "kind": kind, "color": None,
                                                        "allDay": False, "start": f"{day(offset)}T{start}:00+01:00", "end": f"{day(offset)}T{end}:00+01:00",
                                                        "participantUserIds": [admin_id], "crewIds": [], "bauakteId": sonnenhof if offset == 1 else None,
                                                        "scope": "series"})

    # Personal
    for first, last, number, job, login_name, city in [("Markus", "Huber", "P-104", "Estrichleger, Kolonnenführer", "m.huber", "Landshut"),
                                                         ("Stefan", "Wagner", "P-108", "Estrichleger, Kolonnenführer", "s.wagner", "Ergolding"),
                                                         ("Jonas", "Schmid", "P-115", "Estrichleger", "j.schmid", "Essenbach"),
                                                         ("Lukas", "Brenner", "P-121", "Estrichleger", "l.brenner", "Landshut"),
                                                         ("Ali", "Demir", "P-110", "Estrichleger, Kolonnenführer", "a.demir", "Altdorf"),
                                                         ("Peter", "Zimmermann", "P-117", "Helfer", "p.zimmermann", "Kumhausen"),
                                                         ("Martina", "Keller", "P-102", "Büro / Disposition", "m.keller", "Ergolding")]:
        call("PUT", f"staff/employees/{new_id()}", {"firstName": first, "lastName": last, "personnelNumber": number, "jobTitle": job,
                                                    "employmentType": "fullTime", "userId": u(login_name), "mobile": "0170 000 00 00".replace("00 00", number[-2:] + " 10"),
                                                    "city": city, "entryDate": "2019-04-01", "vacationDaysPerYear": 30, "vacationCarryOver": 2, "weeklyHours": 40,
                                                    "workDays": ["monday", "tuesday", "wednesday", "thursday", "friday"]})
    if u("l.brenner"):
        call("PUT", f"hr/absence-requests/{new_id()}", {"userId": u("l.brenner"), "from": str(TODAY + dt.timedelta(days=21)), "to": str(TODAY + dt.timedelta(days=25)),
                                                         "kind": "vacation", "note": "Familienfeier"})

    # Material, Lieferanten, Bestellungen
    suppliers = {}
    for name, number, contact, phone, mail, city in [("Baustoff Huber KG", "KD-44812", "Herr Maier", "0871 400 50", "bestellung@baustoff-huber.example", "Landshut"),
                                                     ("Isar Transportbeton GmbH", "KD-2091", "Frau Lang", "0871 600 70", "dispo@isar-beton.example", "Essenbach"),
                                                     ("Dämmstoffe Süd GmbH", "KD-7733", "Herr Roth", "089 123 456", "auftrag@daemmstoffe-sued.example", "München")]:
        sid = new_id()
        call("PUT", f"suppliers/{sid}", {"id": sid, "name": name, "customerNumber": number, "contactName": contact, "phone": phone, "email": mail,
                                         "city": city, "isActive": True})
        suppliers[name] = sid
    materials = {}
    for name, cat, unit, art, sup, price in [("Zementestrich CT-C25-F4 (Sackware)", "screed", "Sack", "ZE-25", "Baustoff Huber KG", 6.9),
                                             ("CEM II/A-LL 42,5 R", "binder", "t", "CEM-425", "Baustoff Huber KG", 145.0),
                                             ("Estrichsand 0/8", "aggregate", "t", "S-08", "Baustoff Huber KG", 21.5),
                                             ("Calciumsulfat-Fließestrich CAF-C25-F4", "screed", "t", "CAF-25", "Isar Transportbeton GmbH", 98.0),
                                             ("EPS-Dämmplatte DEO 040, 40 mm", "insulation", "m²", "EPS-40", "Dämmstoffe Süd GmbH", 7.4),
                                             ("Randdämmstreifen 8/150 mm mit Folie", "edgeStrip", "m", "RDS-150", "Dämmstoffe Süd GmbH", 0.62),
                                             ("PE-Folie 0,2 mm", "membrane", "m²", "PE-02", "Baustoff Huber KG", 0.35),
                                             ("Estrichzusatz Fließmittel", "additive", "l", "FM-10", "Baustoff Huber KG", 4.2)]:
        mid = new_id()
        call("PUT", f"materials/{mid}", {"id": mid, "name": name, "category": cat, "unit": unit, "articleNumber": art, "supplierId": suppliers[sup],
                                         "price": price, "notes": None, "isActive": True})
        materials[name] = (mid, unit, price)
    orders = []
    for sup, aid, offset, lines in [("Baustoff Huber KG", sonnenhof, 1, [("CEM II/A-LL 42,5 R", 12), ("Estrichsand 0/8", 38), ("PE-Folie 0,2 mm", 400)]),
                                    ("Isar Transportbeton GmbH", isarpark, 3, [("Calciumsulfat-Fließestrich CAF-C25-F4", 64)]),
                                    ("Dämmstoffe Süd GmbH", oberlaender, 6, [("EPS-Dämmplatte DEO 040, 40 mm", 170), ("Randdämmstreifen 8/150 mm mit Folie", 220)])]:
        oid = new_id()
        call("PUT", f"material-orders/{oid}", {"id": oid, "supplierId": suppliers[sup], "bauakteId": aid, "deliveryDate": str(TODAY + dt.timedelta(days=offset)),
                                               "deliveryTime": "07:00", "siteContact": "Markus Huber, 0170 104 10", "notes": "Anlieferung bis 7 Uhr, Abladen mit Kran.",
                                               "lines": [{"id": new_id(), "materialId": materials[m][0], "description": m, "quantity": q, "unit": materials[m][1],
                                                          "unitPrice": materials[m][2]} for m, q in lines]})
        orders.append(oid)
    if orders:
        call("POST", f"material-orders/{orders[0]}/status", {"status": "ordered"})

    # Büro: Anrufe und Anfragen
    for name, company, subject, follow, phone in [("Frau Oberländer", None, "Rückfrage Termin Estrich OG", True, "08743 912 34"),
                                                  ("Sabine Brandl", "Architekturbüro Brandl & Partner", "Angebot Hohlboden nachfassen", True, "0871 223 34"),
                                                  ("Herr Maier", "Baustoff Huber KG", "Lieferung verschiebt sich auf Donnerstag", False, "0871 400 50")]:
        cid = new_id()
        call("PUT", f"calls/{cid}", {"id": cid, "kind": "call", "direction": "inbound", "source": "phone", "priority": "normal", "callerName": name,
                                          "callerPhone": phone, "company": company, "subject": subject, "notes": "Bitte zurückrufen.", "followUp": follow,
                                          "dueDate": str(TODAY) if follow else None})
    for name, phone, subject, area, city, plz in [("Familie Brunner", "0871 776 655", "Heizestrich Neubau Einfamilienhaus", 140, "Altdorf", "84032"),
                                                  ("Hausverwaltung Kastner", "0871 909 12", "Sanierung Estrich Treppenhaus", 60, "Landshut", "84034")]:
        cid = new_id()
        call("PUT", f"calls/{cid}", {"id": cid, "kind": "inquiry", "direction": "inbound", "source": "phone", "priority": "normal", "callerName": name,
                                          "callerPhone": phone, "subject": subject, "notes": "Wunschtermin Frühjahr, Pläne folgen per E-Mail.", "followUp": True,
                                          "dueDate": str(TODAY + dt.timedelta(days=2)), "areaSquareMeters": area, "screedType": "ct", "siteCity": city, "sitePostalCode": plz})

    # Helpdesk (als Mitarbeiter gemeldet)
    for _, title, cat, blocked, text in [("m.huber", "Fotos werden auf dem iPad doppelt angezeigt", "sync", False, "Seit dem Update sehe ich Fotos der Baustelle Sonnenhof doppelt."),
                                                  ("s.wagner", "Anmeldung am Pool-iPad der Kolonne Wagner schlägt fehl", "signIn", True, "PIN wird nicht angenommen."),
                                                  ("m.keller", "Drucker im Büro druckt keine Lieferscheine", "officeIt", False, "Neustart hat nicht geholfen.")]:
        call("POST", "tickets", {"id": new_id(), "title": title, "description": text, "category": cat, "source": "web", "reporterBlocked": blocked,
                                 "bauakteId": sonnenhof if cat == "sync" else None, "diagnostics": None, "attachmentFileIds": []})

    # Chat (Modul einschalten) und Benachrichtigungen
    call("PUT", "chat/admin/settings", {"enabled": True, "retentionDays": 365, "showPreview": False, "workersCanChatEachOther": True, "editWindowMinutes": 15, "attachmentsEnabled": True})
    group = call("POST", "chat/conversations", {"title": "Baustelle Sonnenhof", "memberIds": [x for x in [u("m.keller"), u("m.huber"), u("s.wagner"), u("j.schmid")] if x],
                                                 "crewId": None, "description": "Abstimmung Haus B", "announcementOnly": False, "adminIds": []})
    if group:
        gid = group.get("id") or (group.get("conversation") or {}).get("id")
        for text in ["Guten Morgen! Silo für Haus B steht, Anlieferung Sand kommt um 7:30 Uhr.",
                     "Danke. Bitte nach dem Einbringen ein Foto vom 2. OG in die Bauakte.",
                     "Fugenplan liegt freigegeben in den Dokumenten."]:
            call("POST", f"chat/conversations/{gid}/messages", {"text": text, "clientMessageId": new_id(), "attachmentIds": [], "mentionIds": [],
                                                                 "attachmentFileIds": [], "mentionUserIds": []})
    call("POST", "notifications/send", {"title": "Morgen Treffpunkt 6:30 Uhr am Lager", "body": "Bitte Glättmaschine GM-03 mitnehmen.", "link": None,
                                        "urgency": "normal", "userIds": [admin_id], "roleIds": [], "crewIds": [], "all": False})
    call("POST", "devices/enrollment-codes", {"kind": "pool", "deviceName": "iPad Kolonne Huber"})
    # Feiertage Bayern, E-Mail-Konto (ausgeschaltet), Archivserver koppeln
    call("PUT", "hr/settings", {"state": "BY", "maxCrewAbsencePercent": 50, "remindBirthdays": True, "remindAnniversaries": True})
    call("PUT", "mail/settings", {"enabled": False, "host": "smtp.estrichbau-muster.de", "port": 587, "security": "startTls", "userName": "buero@estrichbau-muster.de",
                                  "password": None, "clearPassword": False, "fromAddress": "buero@estrichbau-muster.de", "fromName": "Estrichbau Muster GmbH",
                                  "replyTo": None})
    code_file = os.path.join(os.environ.get("HB_WORK", "/tmp/estrichone-handbuch"), "archive-code")
    if os.path.exists(code_file) and open(code_file).read().strip():
        call("POST", "archive/pair", {"address": "127.0.0.1:18491", "code": open(code_file).read().strip()})
    # Erste Sicherung über das Backup Center anfordern und abwarten (Backup-Dienst aus umgebung.sh, prüft alle 30 s)
    import time
    call("POST", "backup/run", {})
    for _ in range(40):
        time.sleep(5)
        status = call("GET", "backup/status") or {}
        if (status.get("lastSuccess") or {}).get("status") == "succeeded":
            print("Sicherung fertig:", status["lastSuccess"].get("message"))
            break
    print("Demodaten angelegt;", FAILS, "Fehler")


if __name__ == "__main__":
    main()
