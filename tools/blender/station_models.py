"""Ein prozedurales Modell je Stationstyp (Key = stationTypes[].key in default-world.json).

Jede Bau-Funktion arbeitet in einem freien Massstab (Koerper etwa Radius 10 in XY);
Builder.finish() zentriert und normiert danach auf Radius 1 in der Spielebene.
Blender-Koordinaten: XY = Spielebene, +Z = "oben" (nach glTF-Export +Y).
"""

import math
from mathutils import Vector

import bmesh
from stationkit import Builder, material as M, polar, qdir
from ship_models import hp_turret, hp_shafts, greebles, _rand, interp, side_hatch, top_hatch, merge_material, vents, sensor

MODELS = {}


def model(key, idea):
    def deco(fn):
        MODELS[key] = (fn, idea)
        return fn
    return deco


# Heller Space-Opera-Stil (wie die Schiffe): Rumpftoene werden beim Abschluss umgesetzt.
RESTYLE = {"Hull": "ShipHull", "Steel": "ShipPanel", "Armor": "ShipArmor"}
DARK_KEEP = {"pirate_hideout"}          # bleibt dunkel und zusammengeflickt (Kontrast gewollt)
HARDPOINTS = {}                          # key -> Liste wie in stations.json
# Mehr als zehn Materialien werden der Reihe nach zusammengelegt.
STATION_MERGE = (("Glass", "Light"), ("ShipArmor", "ShipPanel"), ("Window", "Light"), ("Copper", "ShipPanel"),
                 ("Hazard", "Accent"), ("Ice", "ShipPanel"), ("Rust", "ShipPanel"), ("Container", "ShipPanel"))
DRESS = {"trade_station": (2, 5), "satellite_sensor": (1, 2), "platform_light": (2, 3),
         "platform_medium": (4, 5), "platform_heavy": (5, 6),
         "colony": (3, 4), "company_seat": (3, 4), "drive_works": (3, 4), "weapons_works": (3, 4),
         "systems_works": (3, 4), "warehouse": (3, 4)}   # Stationen mit eigener, dichter Dachgestaltung brauchen weniger Automatik
PIRATE_MERGE = (("Window", "LightRed"), ("Container", "Rust"), ("Armor", "Steel"), ("Glow", "Ember"),
                ("Hazard", "Rust"))


def restyle(b):
    for i, m in enumerate(b.mats):
        if m.name in RESTYLE:
            b.mats[i] = M(RESTYLE[m.name])


def dress_station(b, seed=1.0, tops=5, sides=6):
    """Automatische Ausstattung: dunkle Technikgraeben mit Greebles auf den groessten Oberseiten,
    grosse Accent-Felder auf den groessten senkrechten Flaechen."""
    bm = b.bm
    names = [m.name for m in b.mats]
    light = {i for i, n in enumerate(names) if n in ("ShipHull", "ShipPanel", "ShipArmor")}
    faces = [f for f in bm.faces if f.material_index in light]
    up = sorted((f for f in faces if f.normal.z > 0.85), key=lambda f: -f.calc_area())
    side = sorted((f for f in faces if abs(f.normal.z) < 0.25), key=lambda f: -f.calc_area())
    jobs = []
    used = []
    for f in up:
        c = f.calc_center_median()
        if any((c - u).length < 2.0 for u in used) or f.calc_area() < 1.0:
            continue
        used.append(c)
        xs = [v.co.x for v in f.verts]
        ys = [v.co.y for v in f.verts]
        jobs.append(("top", c.copy(), (max(xs) - min(xs)), (max(ys) - min(ys))))
        if len(used) >= tops:
            break
    used = []
    for f in side:
        c = f.calc_center_median()
        if any((c - u).length < 2.5 for u in used) or f.calc_area() < 1.2:
            continue
        used.append(c)
        n = f.normal.copy()
        t = Vector((-n.y, n.x, 0))
        ts = [v.co.dot(t) for v in f.verts]
        zs = [v.co.z for v in f.verts]
        jobs.append(("side", c.copy(), n, (max(ts) - min(ts)), (max(zs) - min(zs))))
        if len(used) >= sides:
            break
    k = 0
    for job in jobs:
        k += 1
        if job[0] == "top":
            _, c, sx, sy = job
            w, l = (sx, sy) if sy >= sx else (sy, sx)
            rot = (0, 0, 0) if sy >= sx else (0, 0, 90)
            b.box(max(0.3, w * 0.3), l * 0.75, 0.05, M("HullDark"), loc=c + Vector((0, 0, 0.01)), rot=rot)
            greebles(b, c.x, c.y, c.z + 0.04, w * 0.28 if sy >= sx else l * 0.7, l * 0.7 if sy >= sx else w * 0.28,
                     n=6, seed=seed + k)
        else:
            _, c, n, w, h = job
            yaw = math.degrees(math.atan2(n.y, n.x))
            b.box(0.06, w * 0.62, h * 0.42, M("Accent"), loc=c + n * 0.03, rot=(0, 0, yaw))
            b.box(0.07, w * 0.62, 0.06, M("HullDark"), loc=c + n * 0.04 + Vector((0, 0, h * 0.27)), rot=(0, 0, yaw))
            b.box(0.07, w * 0.5, 0.1, M("Window"), loc=c + n * 0.04 - Vector((0, 0, h * 0.3)), rot=(0, 0, yaw))


def extremes(b, dirs):
    """Aeusserste Rumpfpunkte (Vertex mit groesster Projektion) je Richtung."""
    vs = list(b.bm.verts)
    return [max(vs, key=lambda v: v.co.dot(Vector(d))).co.copy() for d in dirs]


def station_details(b, key):
    """Gemeinsame Ausstattung jeder Station: Navigationsbaken an den vier aeussersten Punkten,
    Manoevrierduesen an den Diagonalen, Antennen-/Sensorgruppe oben, Bake unten."""
    cards = [(1, 0, 0), (0, 1, 0), (-1, 0, 0), (0, -1, 0)]
    for k, (p, d) in enumerate(zip(extremes(b, cards), cards)):
        d = Vector(d)
        b.strut(p - d * 0.2, p + d * 0.9, 0.08, M("HullDark"), seg=4)
        b.sphere(0.24, M("LightRed" if k % 2 else "Light"), seg=6, rings=4, loc=p + d * 1.0)
    diag = [(1, 1, 0), (-1, 1, 0), (-1, -1, 0), (1, -1, 0)]
    for p, d in zip(extremes(b, diag), diag):
        b.rcs(p + Vector(d).normalized() * 0.25, s=0.8)
    top, bot = extremes(b, [(0, 0, 1), (0, 0, -1)])
    for i, (dx, h) in enumerate(((0.0, 2.2), (0.5, 1.4), (-0.45, 1.1))):
        b.strut(top + Vector((dx, dx * 0.6, -0.2)), top + Vector((dx, dx * 0.6, h)), 0.06, M("HullDark"), seg=4)
        b.sphere(0.13, M("LightRed" if i == 0 else "Light"), seg=5, rings=3, loc=top + Vector((dx, dx * 0.6, h)))
    b.strut(bot + Vector((0, 0, 0.2)), bot + Vector((0, 0, -1.2)), 0.08, M("HullDark"), seg=4)
    b.sphere(0.2, M("Light"), seg=6, rings=4, loc=bot + Vector((0, 0, -1.25)))


def build(key):
    fn, _ = MODELS[key]
    b = Builder(key)
    b.hardpoints = []
    fn(b)
    if key not in DARK_KEEP:
        if key not in RADIUS:          # Kleinstationen bringen ihre Details selbst mit
            station_details(b, key)
        restyle(b)
        tops, sides = DRESS.get(key, (8, 10))
        dress_station(b, seed=sum(map(ord, key)) * 0.01, tops=tops, sides=sides)
    for src, dst in (PIRATE_MERGE if key in DARK_KEEP else STATION_MERGE):
        if len(b.mats) <= 10:
            break
        merge_material(b, src, dst)
    parts = getattr(b, "parts", [])
    if key in DARK_KEEP:               # Tuerme im Piratenstil: kein eigenes Schiffs-Paneelmaterial
        for rb in parts:
            rb.mats = [M("Steel") if m.name == "ShipPanel" else m for m in rb.mats]
    if not parts:
        HARDPOINTS[key] = []
        return b.finish()
    # Normierung wie finish(): Bounding-Box-Mitte = Ursprung, groesster Radius in der Ebene = 1
    co = [v.co.copy() for v in b.bm.verts] + [v.co.copy() for rb in parts for v in rb.bm.verts]
    lo = Vector((min(c.x for c in co), min(c.y for c in co), min(c.z for c in co)))
    hi = Vector((max(c.x for c in co), max(c.y for c in co), max(c.z for c in co)))
    c0 = (lo + hi) / 2
    k = 1.0 / max(math.hypot(c.x - c0.x, c.y - c0.y) for c in co)
    for v in b.bm.verts:
        v.co = (v.co - c0) * k
    ob = b.finish(normalize="none")
    for rb in parts:
        q = rb.q
        qi = q.inverted()
        cen = (rb.center - c0) * k
        for v in rb.bm.verts:
            v.co = qi @ ((v.co - c0) * k - cen)
        po = rb.finish(normalize="none")
        po.name = rb.name
        po.rotation_mode = "QUATERNION"
        po.rotation_quaternion = q
        po.location = cen
        po.parent = ob
    hps = []
    for hp in sorted(b.hardpoints, key=lambda h: (h["slot"] < 0, h["slot"])):
        pn = (hp["pos"] - c0) * k
        d = hp["dir"].normalized()
        e = {"gun": hp["slot"], "kind": hp["kind"],
             "pos": [round(pn.x, 4), round(pn.z, 4), round(-pn.y, 4)],
             "dir": [round(d.x, 4), round(d.z, 4), round(-d.y, 4)]}
        if hp.get("node"):
            e["node"] = hp["node"]
        hps.append(e)
    HARDPOINTS[key] = hps
    return ob



def V(x, y, z):
    return Vector((x, y, z))


def intake(b, p, d, r=1.1):
    """Geschlossener Erz-Einlass: Trichter mit Warnkragen und dunklem Schlund, Oeffnung in Richtung d."""
    p, d = Vector(p), Vector(d).normalized()
    q = qdir(d)
    b.lathe([(r * 0.55, 0), (r * 0.8, r * 0.9), (r * 1.25, r * 1.8), (r * 1.1, r * 1.8), (r * 0.7, r * 0.9),
             (r * 0.4, r * 0.1)], M("Steel"), 10, loc=p, rot=q, closed=True)
    b.lathe([(r * 1.1, r * 1.5), (0, r * 1.5)], M("HullDark"), 10, loc=p, rot=q)
    b.lathe([(r * 1.27, r * 1.55), (r * 1.27, r * 1.85)], M("Hazard"), 10, loc=p, rot=q)
    b.cyl(r * 0.6, r * 1.2, M("Hull"), seg=10, loc=p - d * r * 0.4, rot=q)


# =============================================================== Grundstoffe
@model("solar_plant", "Nabe mit vier Gittertraegern und 24 grossen, schraeg gestellten Solarpaneelen")
def solar_plant(b):
    b.lathe([(0, -2.2), (1.2, -1.6), (1.7, -0.8), (1.7, 0.8), (1.2, 1.6), (0.5, 2.0), (0, 2.1)],
            M("Hull"), seg=12)
    b.ring(1.82, 0.2, 0.5, M("Accent"), seg=12)
    b.window_band(1.75, 1.15, 0.22, n=6, seg=12)
    b.cyl(0.18, 2.6, M("HullDark"), seg=6, loc=(0, 0, 3.2))
    b.nav_lights([(0, 0, 4.55)], s=0.2)
    b.cyl(0.5, 0.9, M("HullDark"), seg=8, loc=(0, 0, -2.6))
    for x, y, a in polar(4, 1.6, 45):
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        n = V(-d.y, d.x, 0)
        b.truss(d * 1.4, d * 9.6, 0.32, 0.11, M("HullDark"), n=7)
        for dist in (3.4, 5.9, 8.4):
            for side in (-1, 1):
                c = d * dist + n * side * 2.05
                tilt = 18 * side
                b.box(2.3, 3.3, 0.06, M("Hull"), loc=c - V(0, 0, 0.06), rot=(tilt, 0, a))
                b.box(2.1, 3.1, 0.08, M("Panel"), loc=c, rot=(tilt, 0, a))
                b.box(2.12, 0.07, 0.1, M("Hull"), loc=c, rot=(tilt, 0, a))
                b.box(0.07, 3.12, 0.1, M("Hull"), loc=c, rot=(tilt, 0, a))
        b.nav_lights([d * 9.7 + V(0, 0, 0.35)], mat="LightRed", s=0.18)


@model("steelworks", "Kompakter Hochofen-Kern: gepanzerte, achteckige Schmelztrommel mit zwei glutroten Schlitzbaendern und Panzerguertel, Giesskegel oben und unten, vier kurze, breite Radiatorfluegel, zwei geschlossene Erz-Einlasstrichter, haengende Barrenkapseln, Punktverteidigung")
def steelworks(b):
    b.lathe([(0, -4.6), (2.2, -4.2), (3.6, -3.0), (4.2, -1.6), (4.2, 1.6), (3.6, 3.0), (2.2, 4.2), (0, 4.6)],
            M("Hull"), seg=8, start=22.5)
    for z in (0.75, -0.75):
        b.lathe([(4.24, z - 0.22), (4.24, z + 0.22)], M("Glow"), seg=8, start=22.5)
    b.armor_belt(4.45, 0.0, 0.9, 8, mat="Armor", tilt=0, start=22.5 + 22.5)
    for z in (1, -1):
        b.armor_belt(3.95, z * 2.3, 0.9, 8, mat="HullDark", tilt=-z * 25, start=22.5 + 22.5)
        b.lathe([(1.4, 0), (1.8, 0.9), (0.9, 1.8), (0.0, 1.9)], M("HullDark"), seg=8, loc=(0, 0, z * 4.4),
                rot=None if z > 0 else (180, 0, 0))
        b.torus(1.0, 0.18, M("Glow"), seg=8, minor=4, loc=(0, 0, z * 5.9))
    for x, y, a in polar(4, 4.5, 45):
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        b.radiator(d * 4.3, d, 4.6, 3.4, tilt=14, mat="Steel", stripe="Glow", n=3)
    for s in (1, -1):
        d = V(s, 0, 0)
        b.box(1.6, 1.6, 1.6, M("HullDark"), loc=d * 4.9, bevel=0.15)
        intake(b, d * 5.4, d, r=1.1)
    for x, y, a in polar(3, 2.4, 0):
        b.capsule(V(x, y, -6.3), (math.cos(math.radians(a + 90)), math.sin(math.radians(a + 90)), 0),
                  2.0, 0.5, M("Copper"), seg=8)
    pass
    pass
    b.mast((2.0, 0, 4.3), 2.6)
    b.insignia(V(0, -4.85, 2.3), (0, -1, 0.35), s=0.8)


@model("water_plant", "Drei Eistanks schwebend an Auslegern um eine durchgehende Pumpensaeule, Eisfaenger-Trichter oben, Andockstutzen unten, Rohrleitungen")
def water_plant(b):
    b.lathe([(0, -7.6), (0.7, -7.2), (1.0, -6.0), (1.0, 4.6)], M("Hull"), seg=12)
    b.ring(1.2, 0.3, 0.6, M("Accent"), seg=12, loc=(0, 0, 2.6))
    b.ring(1.2, 0.3, 0.6, M("Accent"), seg=12, loc=(0, 0, -2.6))
    b.window_band(1.05, -0.8, 0.3, n=6, seg=12)
    b.lathe([(0.9, 4.5), (2.4, 5.7), (3.3, 6.2), (3.2, 6.4), (2.2, 5.9), (0.9, 4.9)], M("HullDark"), seg=16, closed=True)
    b.lathe([(0, 6.05), (2.9, 6.05)], M("Ice"), seg=16)
    for x, y, a in polar(3, 5.0, 90):
        b.sphere(2.4, M("Ice"), seg=16, rings=10, loc=(x, y, 0))
        b.torus(2.42, 0.18, M("HullDark"), seg=16, minor=4, loc=(x, y, 0))
        for z in (1, -1):
            b.cyl(1.0, 0.5, M("Hull"), seg=10, loc=(x, y, z * 2.45))
        d = V(x, y, 0).normalized()
        b.strut(d * 0.9 + V(0, 0, 1.0), d * 3.4 + V(0, 0, 1.2), 0.3, M("Hull"), seg=8)
        b.strut(d * 0.9 + V(0, 0, -1.0), d * 3.4 + V(0, 0, -1.2), 0.25, M("Copper"), seg=8)
        b.nav_lights([V(x, y, 2.85)], s=0.2)
    for x, y, a in polar(3, 1.0, 30):
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        b.radiator(d * 1.0 + V(0, 0, -4.0), d, 5.0, 1.6, tilt=0, stripe="Light", n=2)
    b.dock(V(0, 0, -7.4), (0, 0, -1), r=0.6, L=1.2)
    for x, y, a in polar(3, 5.0, 90):
        b.lathe([(2.36, 0.6), (2.2, 1.2)], M("Accent"), 16, loc=(x, y, 0))
        b.lathe([(2.2, -1.2), (2.36, -0.6)], M("Accent"), 16, loc=(x, y, 0))


@model("electrolysis", "Zwei dicke, geriffelte Elektrolysezellen mit blau leuchtenden Spalten dicht an einer gepanzerten Nabe, Kupfer-Stromschienen, Wasserstoff- und Sauerstoffkugeln mit Panzerkragen, Andockschleusen oben und unten")
def electrolysis(b):
    b.sphere(2.4, M("Steel"), seg=10, rings=6)
    b.armor_belt(2.5, 0, 0.8, 8, mat="Armor", tilt=0)
    for y in (2.9, -2.9):
        prof = [(0, -6.2), (1.2, -5.8)]
        for i in range(6):
            z = -5.4 + i * 1.8
            prof += [(1.75, z), (1.75, z + 1.35), (1.45, z + 1.45), (1.45, z + 1.8)]
        prof += [(1.2, 5.8), (0, 6.2)]
        b.lathe(prof, M("Hull"), seg=10, loc=(0, y, 0), rot=(0, 90, 0))
        for i in range(6):
            x = -5.4 + i * 1.8 + 1.62
            b.lathe([(1.5, -0.1), (1.5, 0.1)], M("Light"), seg=10, loc=(x, y, 0), rot=(0, 90, 0))
        for z in (1.8, -1.8):
            b.box(11.6, 0.35, 0.35, M("Copper"), loc=(0, y, z))
    for y, mat in ((6.3, "Steel"), (-6.3, "Accent")):
        b.sphere(2.0, M(mat), seg=10, rings=6, loc=(1.0 if y > 0 else -1.0, y, 0))
        b.armor_belt(2.05, 0, 0.7, 8, mat="HullDark", tilt=0, loc=(1.0 if y > 0 else -1.0, y, 0))
    for z in (1, -1):
        b.dock(V(0, 0, z * 2.2), (0, 0, z), r=0.7, L=1.6, mat="Armor")
    pass
    b.nav_lights([(6.4, 2.9, 0), (6.4, -2.9, 0), (-6.4, 2.9, 0), (-6.4, -2.9, 0)], mat="LightRed", s=0.22)


@model("copper_smelter", "Rotierender Schmelzring: kupferbeschlagener Torus mit gluehender Schmelzrinne um eine Nabe, Kupferspeichen, haengende Barrenkapseln, Erz-Einlasstrichter, Kuehlrippen oben und unten")
def copper_smelter(b):
    b.spindle(1.6, 6.0, M("Hull"), seg=12)
    b.ring(1.85, 0.3, 0.5, M("Accent"), seg=12, loc=(0, 0, 1.4))
    b.ring(1.85, 0.3, 0.5, M("Accent"), seg=12, loc=(0, 0, -1.4))
    R = 7.0
    b.torus(R, 1.25, M("HullDark"), seg=24, minor=8, start_minor=22.5)
    b.ring(R + 0.7, 1.4, 0.2, M("Glow"), seg=24, loc=(0, 0, 1.08))
    b.lathe([(R - 1.27, 0.35), (R - 1.27, -0.35)], M("Glow"), seg=24)
    for x, y, a in polar(8, R, 0):
        b.ring(1.55, 0.35, 0.4, M("Copper"), seg=10, loc=(x, y, 0), rot=(90, 0, a + 90))
    for x, y, a in polar(4, 1.5, 45):
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        b.strut(d * 1.5, d * (R - 1.1), 0.35, M("Copper"), seg=8)
    for x, y, a in polar(3, R, 22.5):
        b.beam(V(x, y, -1.2), V(x, y, -2.6), 0.18, M("HullDark"))
        b.capsule(V(x, y, -3.4), (math.cos(math.radians(a + 90)), math.sin(math.radians(a + 90)), 0),
                  2.2, 0.55, M("Copper"), seg=8)
    for z in (1, -1):
        for k in range(4):
            b.box(0.15, 3.4, 2.6, M("Copper"), loc=(0, 0, z * 4.6), rot=(0, 0, 45 * k))
    d = V(math.cos(math.radians(-22.5)), math.sin(math.radians(-22.5)), 0)
    b.beam(d * (R + 1.0), d * (R + 2.4), 0.5, M("HullDark"))
    intake(b, d * (R + 2.3), d, r=1.2)
    b.dock(V(0, 0, 4.0), (0, 0, 1), r=0.5, L=1.4)
    b.nav_lights([(x, y, 1.4) for x, y, a in polar(4, R, 45)], mat="LightRed", s=0.22)


@model("wafer_fab", "Flache Reinraum-Anlage: zwei weite Waferscheiben mit blau leuchtenden Raendern und Chipfeldern um eine kurze Achse, gepanzerter Sechseck-Unterbau mit Schleuse, Glaskuppel oben, Sensormast")
def wafer_fab(b):
    b.cyl(1.4, 7.0, M("Steel"), seg=8)
    for z, r in ((1.1, 6.6), (-0.9, 5.0)):
        b.lathe([(1.3, z - 0.3), (r - 0.5, z - 0.3), (r, z), (r - 0.5, z + 0.25), (1.3, z + 0.25)],
                M("Hull"), seg=16, closed=True)
        for k in range(3):
            b.ring(r + 0.06, 0.6, 0.6, M("Accent"), seg=6, loc=(0, 0, z), arc=40, start=120 * k + z * 20)
        b.torus(r - 0.05, 0.16, M("Plasma"), seg=16, minor=4, loc=(0, 0, z))
        for x, y, a in polar(8, r * 0.62, 11.25):
            b.box(0.9, 0.9, 0.22, M("HullDark"), loc=(x, y, z + 0.32), rot=(0, 0, a))
            b.box(0.9, 0.9, 0.22, M("HullDark"), loc=(x, y, z - 0.37), rot=(0, 0, a))
        for x, y, a in polar(4, 1.3, 45):
            b.beam(V(x, y, z), V(x * 3.4, y * 3.4, z), 0.2, M("Hull"))
    hexa = [(math.cos(math.radians(60 * i + 30)) * 2.6, math.sin(math.radians(60 * i + 30)) * 2.6) for i in range(6)]
    b.prism(hexa, 2.0, M("Armor"), loc=(0, 0, -3.2), top_scale=1.0)
    b.prism([(x * 0.7, y * 0.7) for x, y in hexa], 1.0, M("HullDark"), loc=(0, 0, -4.7), top_scale=1 / 0.7)
    b.dock(V(0, 0, -5.2), (0, 0, -1), r=0.6, L=1.0, mat="Armor")
    b.sphere(1.8, M("Glass"), seg=10, rings=6, loc=(0, 0, 3.4), half=True)
    b.ring(1.95, 0.3, 0.4, M("Steel"), seg=10, loc=(0, 0, 3.4))
    b.mast((1.0, 0.0, 4.6), 2.0, light="Plasma")
    b.nav_lights([(x, y, 1.6) for x, y, a in polar(4, 6.4, 0)], mat="LightRed", s=0.18)


@model("titanium_works", "Liegende, dicke Brechertrommel mit gelben Warnstreifen und Panzerrippen als Rumpf, sechseckige Verarbeitungsbloecke oben und unten, geschlossener Erztrichter am Bug, gepanzerte Andockschleuse am Heck, kurze Radiatoren")
def titanium_works(b):
    b.lathe([(0, -8.0), (1.8, -7.6), (3.2, -6.0), (3.4, -5.2), (3.4, 5.2), (3.2, 6.0), (1.8, 7.6), (0, 8.0)],
            M("Hull"), seg=12, rot=(0, 90, 0))
    for x in (-3.4, -1.15, 1.15, 3.4):
        b.torus(3.5, 0.22, M("HullDark"), seg=12, minor=4, loc=(x, 0, 0), rot=(0, 90, 0))
    for x in (-5.4, 5.4):
        b.lathe([(3.45, -0.45), (3.45, 0.45)], M("Hazard"), seg=12, loc=(x, 0, 0), rot=(0, 90, 0))
    hexa = [(math.cos(math.radians(60 * i)) * 2.0, math.sin(math.radians(60 * i)) * 2.0) for i in range(6)]
    for z in (1, -1):
        b.prism(hexa, 2.4, M("Steel"), loc=(-1.0, 0, z * 4.0), top_scale=0.85 if z > 0 else 1.15)
        b.prism([(x * 1.06, y * 1.06) for x, y in hexa], 0.35, M("Armor"), loc=(-1.0, 0, z * 3.0))
        b.prism([(x * 0.55, y * 0.55) for x, y in hexa], 0.5, M("Light"), loc=(-1.0, 0, z * 5.3))
        b.prism(hexa, 1.6, M("Steel"), loc=(2.6, 0, z * 3.7), scale=(0.7, 0.7, 1))
    for y in (1, -1):
        b.radiator(V(-2.0, y * 3.2, 0), (0, y, 0), 4.6, 2.6, tilt=-15 * y, mat="Steel", stripe="Light")
        b.radiator(V(2.4, y * 3.2, 0), (0, y, 0), 4.0, 2.2, tilt=-15 * y, mat="Steel", stripe="Light")
    intake(b, V(-8.3, 0, 0), (-1, 0, 0), r=1.5)
    b.dock(V(8.0, 0, 0), (1, 0, 0), r=0.9, L=1.6, mat="Armor")
    pass
    b.nav_lights([(-1.0, 0, 5.6), (-1.0, 0, -5.6), (-11.8, 0, 1.6)], mat="LightRed", s=0.22)
    b.insignia(V(0.6, -3.45, 0.6), (0, -1, 0.1), s=0.8)


@model("platinum_refinery", "Zentrifugen-Turm: goldene Doppelringe mit Speichen, sechs haengende Zentrifugenkapseln, Tresorkugel")
def platinum_refinery(b):
    b.cyl(0.7, 13.0, M("Hull"), seg=10)
    b.lathe([(0.7, 4.6), (2.0, 5.0), (2.2, 6.0), (1.6, 6.9), (0, 7.2)], M("Gold"), seg=14)
    b.window_band(2.22, 5.6, 0.3, n=6, seg=12)
    for z, R in ((1.6, 6.0), (-1.4, 5.0)):
        b.torus(R, 0.55, M("Gold"), seg=24, minor=6, loc=(0, 0, z))
        b.torus(R, 0.6, M("Accent"), seg=24, minor=4, loc=(0, 0, z), minor_scale=(1.05, 0.25))
        for x, y, a in polar(6, R, 15 if z > 0 else 45):
            b.beam(V(0, 0, z), V(x, y, z), 0.22, M("Hull"))
    for x, y, a in polar(6, 6.0, 15):
        b.strut(V(x, y, 1.6), V(x, y, -0.2), 0.12, M("HullDark"), seg=5)
        b.lathe([(0, -1.6), (0.55, -1.3), (0.7, -0.6), (0.7, 0.6), (0.55, 1.3), (0, 1.6)],
                M("Hull"), seg=10, loc=(x, y, -1.8))
        b.lathe([(0.72, -0.15), (0.72, 0.15)], M("Light"), seg=10, loc=(x, y, -1.8))
    b.lathe([(0, -8.0), (1.0, -7.5), (1.4, -6.5), (0.7, -5.5)], M("HullDark"), seg=12)
    b.ring(1.6, 0.3, 0.5, M("Accent"), seg=12, loc=(0, 0, -3.5))
    b.nav_lights([(0, 0, 7.35), (0, 0, -8.1)], s=0.25)
    pass
@model("powerplant", "Fusionsreaktor: grosser Tokamak-Torus mit Spulenringen und Plasmaband, Mittelsaeule, vier Kuehlfluegel")
def powerplant(b):
    b.torus(4.6, 1.6, M("Hull"), seg=24, minor=10)
    b.lathe([(6.25, -0.32), (6.25, 0.32)], M("Plasma"), seg=24)
    b.lathe([(2.95, 0.32), (2.95, -0.32)], M("Plasma"), seg=24)
    for x, y, a in polar(8, 4.6, 22.5):
        b.ring(2.1, 0.45, 0.55, M("HullDark"), seg=10, loc=(x, y, 0), rot=(90, 0, a))
    b.cyl(1.4, 7.0, M("Hull"), seg=12)
    b.ring(1.6, 0.3, 0.6, M("Accent"), seg=12, loc=(0, 0, 2.4))
    b.ring(1.6, 0.3, 0.6, M("Accent"), seg=12, loc=(0, 0, -2.4))
    b.lathe([(1.4, 3.5), (0.8, 4.4), (0.3, 4.8), (0, 4.9)], M("HullDark"), seg=12)
    b.lathe([(1.4, -3.5), (0.8, -4.4), (0, -4.6)][::-1], M("HullDark"), seg=12)
    for x, y, a in polar(4, 1.0, 45):
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        b.beam(d * 1.2, d * 3.2, 0.35, M("Hull"))
    for x, y, a in polar(4, 7.6, 0):
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        b.beam(d * 6.0, d * 7.0, 0.5, M("HullDark"))
        b.box(3.0, 0.18, 4.4, M("HullDark"), loc=d * 8.4, rot=(0, 0, a))
        for k in range(3):
            b.box(2.8, 0.24, 0.15, M("Glow"), loc=d * 8.4 + V(0, 0, -1.5 + k * 1.5), rot=(0, 0, a))
        b.nav_lights([d * 9.95 + V(0, 0, 2.3)], mat="LightRed", s=0.18)
    pass
    pass
    for k in range(8):
        b.ring(4.6 + 1.0, 2.0, 0.1, M("Accent"), seg=6, loc=(0, 0, 1.58), arc=22, start=45 * k + 11.5)


@model("farm", "Ringdeck mit sechs gruen leuchtenden Gewaechshauskuppeln, Speichen zur Nabe, Wassertanks darunter")
def farm(b):
    b.cyl(2.0, 2.4, M("Hull"), seg=12)
    b.sphere(1.6, M("Glass"), seg=12, rings=6, loc=(0, 0, 1.2), half=True)
    b.lathe([(0, -4.6), (0.9, -4.2), (1.5, -3.0), (1.6, -1.2)], M("Hull"), seg=12)
    b.dock(V(0, 0, -4.4), (0, 0, -1), r=0.5, L=1.0)
    for x, y, a in polar(3, 1.4, 0):
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        b.radiator(d * 1.4 + V(0, 0, -2.6), d, 4.0, 1.4, stripe="Light", n=2)
    b.ring(8.8, 3.0, 0.6, M("Hull"), seg=24)
    b.ring(8.95, 0.3, 0.75, M("Accent"), seg=24)
    b.ring(6.0, 0.25, 0.7, M("HullDark"), seg=24)
    for x, y, a in polar(6, 2.0, 0):
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        b.beam(d * 1.9, d * 6.0, 0.4, M("HullDark"))
    for x, y, a in polar(6, 7.3, 30):
        b.sphere(1.65, M("Plant"), seg=12, rings=6, loc=(x, y, 0.3), half=True)
        for k in range(2):
            b.ring(1.72, 0.12, 0.14, M("Hull"), seg=6, loc=(x, y, 0.3), rot=(90, 0, a + 45 + 90 * k), arc=180)
        b.sphere(0.8, M("Ice"), seg=8, rings=4, loc=(x * 0.95, y * 0.95, -1.0))
    for x, y, a in polar(6, 7.3, 0):
        b.lathe([(0.6, -1.1), (0.6, 1.1)], M("Glass"), seg=8, loc=(x, y, 0.55), rot=(90, 0, a + 90),
                cap_bottom=True, cap_top=True)
    b.nav_lights([V(x, y, 0.4) for x, y, a in polar(6, 8.9, 0)], s=0.18)


@model("electronics_fab", "Nabe mit vier grossen dunklen Platinenfluegeln (Chips, leuchtende Leiterbahnen) im Kreuz und vier kleineren, versetzt darueber und darunter, Antennenmasten oben und unten")
def electronics_fab(b):
    b.spindle(1.7, 5.0, M("HullDark"), seg=8)
    b.window_band(1.72, 0.8, 0.25, mat="Light", n=4, seg=8)
    b.window_band(1.72, -0.8, 0.25, mat="Light", n=4, seg=8)
    b.ring(1.9, 0.3, 0.45, M("Accent"), seg=8)
    def board(c, a, L, W, z=0.0):
        rot = (0, 0, a)
        ca, sa = math.cos(math.radians(a)), math.sin(math.radians(a))
        u, v = V(ca, sa, 0), V(-sa, ca, 0)
        c = c + V(0, 0, z)
        b.box(L, W, 0.3, M("HullDark"), loc=c, rot=rot)
        b.box(L + 0.2, 0.2, 0.36, M("Accent"), loc=c + v * (W / 2), rot=rot)
        b.box(L + 0.2, 0.2, 0.36, M("Accent"), loc=c - v * (W / 2), rot=rot)
        for k in (-1, 0, 1):
            b.box(L * 0.9, 0.07, 0.34, M("Light"), loc=c + v * (k * W * 0.3), rot=rot)
        for i in range(3):
            for j in (-1, 1):
                p = c + u * ((i - 1) * L * 0.3) + v * (j * W * 0.17)
                b.box(L * 0.16, W * 0.2, 0.55, M("Hull"), loc=p, rot=rot)
        return u
    for x, y, a in polar(4, 0, 0):
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        b.beam(d * 1.6, d * 3.2, 0.4, M("Hull"))
        board(d * 6.3, a, 6.0, 4.4)
    for i, (x, y, a) in enumerate(polar(4, 0, 45)):
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        z = 1.6 if i % 2 else -1.6
        b.beam(d * 1.4 + V(0, 0, z * 0.6), d * 2.6 + V(0, 0, z), 0.3, M("Hull"))
        board(d * 4.6, a, 3.6, 2.8, z=z)
    for z in (1, -1):
        b.cyl(0.08, 3.4, M("HullDark"), seg=5, loc=(0.6, 0.6, z * 4.8))
        b.cyl(0.08, 2.4, M("HullDark"), seg=5, loc=(-0.6, -0.3, z * 4.3))
        b.nav_lights([(0.6, 0.6, z * 6.5), (-0.6, -0.3, z * 5.5)], s=0.18)


@model("plating_works", "Schwerer gepanzerter Walzrumpf (Kastenprofil) mit drei Walzgeruesten, Walzen ueber und unter dem Rumpf, geklammerte Plattenpakete seitlich, Pressring mit Glutkante, Leitstand, gepanzerte Schleuse")
def plating_works(b):
    b.box(16.0, 3.6, 3.0, M("Hull"), bevel=0.4)
    b.box(16.2, 0.25, 0.5, M("Accent"), loc=(0, -1.85, 1.0))
    b.box(16.2, 0.25, 0.5, M("Hazard"), loc=(0, 1.85, -1.0))
    for x in (-4.5, 0.0, 4.5):
        b.box(1.4, 4.6, 6.0, M("Steel"), loc=(x, 0, 0), bevel=0.2)
        for z in (1, -1):
            b.cyl(0.95, 4.2, M("HullDark"), seg=10, loc=(x + 1.3, 0, z * 2.3), rot=(90, 0, 0))
            b.cyl(0.4, 4.9, M("Hull"), seg=6, loc=(x + 1.3, 0, z * 2.3), rot=(90, 0, 0))
    for x in (-2.25, 2.25):
        for s in (1, -1):
            for k in range(6):
                mat = "Armor" if k in (0, 5) else "Hull"
                b.box(2.6, 2.4, 0.3, M(mat), loc=(x, s * 3.4, -0.9 + k * 0.36))
            for dx in (-1.4, 1.4):
                b.beam(V(x + dx, s * 3.4, -1.4), V(x + dx, s * 3.4, 1.4), 0.25, M("Hazard"))
    b.ring(3.2, 0.9, 1.4, M("Armor"), seg=8, loc=(8.6, 0, 0), rot=(0, 90, 0), start=22.5)
    b.ring(2.35, 0.25, 1.45, M("Glow"), seg=8, loc=(8.6, 0, 0), rot=(0, 90, 0), start=22.5)
    b.box(2.6, 2.6, 2.2, M("Steel"), loc=(-6.2, 0, 2.4), bevel=0.2)
    b.box(2.7, 2.0, 0.35, M("Window"), loc=(-6.2, 0, 2.6))
    b.box(2.4, 2.4, 1.6, M("HullDark"), loc=(-6.2, 0, -2.2), bevel=0.2)
    b.dock(V(-8.0, 0, 0), (-1, 0, 0), r=0.9, L=1.4, mat="Armor")
    pass
    pass
    b.mast((-6.2, 0.6, 3.5), 2.4)
    b.nav_lights([(9.6, 0, 1.8), (-8.0, 0, 1.6)], mat="LightRed", s=0.22)


@model("fuel_cell_fab", "Stern aus sechs liegenden Brennstoffzellen-Kapseln mit gelben Kappen um eine Nabe, sechs kleinere Zellen versetzt darueber und darunter, Wasserstoffkugel unten, Kuehlkreuz oben")
def fuel_cell_fab(b):
    b.spindle(1.7, 4.0, M("Hull"), seg=12)
    b.ring(1.9, 0.3, 0.5, M("Accent"), seg=12)
    hexa = [(math.cos(math.radians(60 * i + 30)) * 2.6, math.sin(math.radians(60 * i + 30)) * 2.6) for i in range(6)]
    b.prism(hexa, 0.8, M("HullDark"))
    for x, y, a in polar(6, 5.4, 0):
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        b.capsule(d * 5.6, d, 6.0, 1.05, M("Hull"), seg=12)
        b.sphere(1.0, M("Hazard"), seg=12, rings=6, loc=d * 8.0, rot=qdir(d), half=True)
        for t in (4.4, 6.4):
            b.lathe([(1.08, -0.15), (1.08, 0.15)], M("Light"), seg=12, loc=d * t, rot=qdir(d))
    for i, (x, y, a) in enumerate(polar(6, 4.2, 30)):
        z = 1.5 if i % 2 else -1.5
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        b.capsule(d * 4.0 + V(0, 0, z), d, 3.6, 0.65, M("Hull"), seg=10)
        b.lathe([(0.67, -0.12), (0.67, 0.12)], M("Hazard"), seg=10, loc=d * 5.4 + V(0, 0, z), rot=qdir(d))
        b.beam(V(0, 0, z), d * 2.4 + V(0, 0, z), 0.25, M("HullDark"))
    b.sphere(1.9, M("Accent"), seg=12, rings=8, loc=(0, 0, -4.4))
    b.torus(1.92, 0.12, M("HullDark"), seg=12, minor=4, loc=(0, 0, -4.4))
    for k in range(2):
        b.box(0.15, 4.4, 2.4, M("HullDark"), loc=(0, 0, 4.0), rot=(0, 0, 45 + 90 * k))
        b.box(0.17, 4.2, 0.18, M("Glow"), loc=(0, 0, 4.4), rot=(0, 0, 45 + 90 * k))
    b.nav_lights([(0, 0, 5.4)], s=0.22)


@model("life_support_fab", "Schlanker Turm mit vier gestaffelten Luftwaescher-Ringen (gruen leuchtend), Luefterrad unten, Atemkugeln")
def life_support_fab(b):
    b.cyl(1.0, 13.0, M("Hull"), seg=12)
    for i, (z, R) in enumerate(((4.0, 3.2), (1.6, 4.4), (-0.8, 5.4), (-3.2, 4.4))):
        b.torus(R, 0.75, M("Hull"), seg=16, minor=6, loc=(0, 0, z), start_minor=30)
        b.lathe([(R + 0.7, z - 0.22), (R + 0.7, z + 0.22)], M("Plant"), seg=16)
        b.lathe([(R - 0.7, z + 0.22), (R - 0.7, z - 0.22)], M("Plant"), seg=16)
        for x, y, a in polar(4, R, 45 * (i % 2)):
            b.beam(V(0, 0, z), V(x, y, z), 0.25, M("HullDark"))
    b.ring(1.2, 0.25, 0.5, M("Accent"), seg=12, loc=(0, 0, 5.4))
    b.ring(1.2, 0.25, 0.5, M("Accent"), seg=12, loc=(0, 0, -4.6))
    # Luefter unten
    b.ring(3.0, 0.3, 0.8, M("HullDark"), seg=18, loc=(0, 0, -6.0))
    for x, y, a in polar(7, 1.6, 0):
        b.box(2.6, 0.7, 0.1, M("Hull"), loc=(x, y, -6.0), rot=(30, 0, a))
    b.cyl(0.6, 1.0, M("HullDark"), seg=10, loc=(0, 0, -6.0))
    # Kopf und Atemkugeln
    b.lathe([(1.0, 6.5), (1.6, 7.0), (1.4, 7.6), (0, 7.9)], M("Glass"), seg=12)
    for x, y, a in polar(2, 7.6, 90):
        b.sphere(1.3, M("Glass"), seg=12, rings=6, loc=(x * 0.0 + 0, y, 2.8))
        b.beam(V(0, 0, 2.8), V(0, y * 0.85, 2.8), 0.25, M("Hull"))
    b.nav_lights([(0, 0, 8.0), (0, 0, -6.7)], s=0.22)


@model("goods_factory", "Kompakter Fertigungsring: acht gepanzerte Werkmodule mit Fensterbaendern auf einem Ring um eine Nabe, vier verkleidete Frachtpods mit Toren an kurzen Auslegern")
def goods_factory(b):
    b.spindle(2.0, 6.0, M("Steel"), seg=8)
    b.armor_belt(2.15, 0, 1.0, 8, tilt=0)
    R = 6.0
    b.ring(R + 0.9, 1.8, 1.2, M("HullDark"), seg=16)
    for x, y, a in polar(8, R, 0):
        b.box(2.8, 3.0, 2.6, M("Hull"), loc=(x, y, 0), rot=(0, 0, a), bevel=0.2)
        b.box(0.12, 2.4, 0.45, M("Window"), loc=(x * (R + 1.52) / R, y * (R + 1.52) / R, 0.4), rot=(0, 0, a))
        b.box(2.0, 2.2, 0.12, M("Armor"), loc=(x, y, 1.33), rot=(0, 0, a))
    for x, y, a in polar(4, 1.9, 22.5):
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        b.beam(d * 1.9, d * (R - 1.2), 0.55, M("HullDark"))
    for i, (x, y, a) in enumerate(polar(4, R, 22.5)):
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        n = V(-d.y, d.x, 0)
        b.beam(d * (R + 1.0), d * (R + 1.8), 0.7, M("HullDark"))
        c = d * (R + 3.1)
        b.capsule(c, n, 5.0, 1.25, M("Steel"), seg=10)
        for u in (-1.2, 0.0, 1.2):
            b.ring(1.32, 0.2, 0.3, M("HullDark"), seg=10, loc=c + n * u, rot=qdir(n))
        for u in (-0.6, 0.6):
            b.box(0.12, 1.0, 1.4, M("Hull"), loc=c + n * u + d * 1.28, rot=(0, 0, a))
            b.box(0.16, 1.0, 0.1, M("HullDark"), loc=c + n * u + d * 1.3, rot=(0, 0, a))
        b.box(0.1, 3.4, 0.5, M("Accent"), loc=c + d * 1.27 + V(0, 0, 0.85), rot=(0, 0, a))
        b.nav_lights([c + n * 2.6, c - n * 2.6], s=0.2)
    for z in (1, -1):
        b.dock(V(0, 0, z * 3.8), (0, 0, z), r=0.7, L=1.4, mat="Armor")
    pass
    pass
@model("luxury_workshop", "Facettierter, violett leuchtender Kristall in goldenen Boegen ueber einem schlanken Sockel, Atelierkapseln")
def luxury_workshop(b):
    b.lathe([(0, 1.0), (1.8, 3.2), (2.3, 4.6), (1.6, 6.4), (0, 8.4)], M("Crystal"), seg=6)
    for k in range(3):
        b.ring(3.2, 0.25, 0.35, M("Gold"), seg=20, loc=(0, 0, 4.6), rot=(90, 0, 60 * k), arc=200,
               start=-10)
    b.torus(3.1, 0.2, M("Gold"), seg=20, minor=6, loc=(0, 0, 4.6))
    b.lathe([(0.6, -4.5), (0.9, -1.0), (2.6, 0.2), (2.8, 0.7), (1.2, 1.2), (0, 1.3)], M("Accent"), seg=16)
    b.ring(2.9, 0.3, 0.3, M("Gold"), seg=16, loc=(0, 0, 0.55))
    b.lathe([(0, -6.5), (0.9, -5.5), (0.6, -4.4)], M("Gold"), seg=10)
    for x, y, a in polar(4, 5.6, 45):
        b.lathe([(0, -1.4), (0.9, -1.2), (1.1, -0.5), (1.1, 0.5), (0.9, 1.2), (0, 1.4)],
                M("Hull"), seg=12, loc=(x, y, -1.0), rot=(0, 90, a))
        b.lathe([(1.12, -0.12), (1.12, 0.12)], M("Window"), seg=12, loc=(x, y, -1.0), rot=(0, 90, a))
        b.strut(V(x, y, -1.0) * 1.0, V(x * 0.15, y * 0.15, -1.6), 0.2, M("Gold"))
    b.nav_lights([(0, 0, 8.6)], mat="Crystal", s=0.25)


# =============================================================== Werften und Fabriken
@model("shipyard", "Werft mit geschlossener Dockbucht: zwei gepanzerte Seitenwaende mit Leuchtleisten und Materialtoren, Dach und Kiel, darin ein halbfertiger Schiffsrumpf, Heckblock mit Leitstand und Schuessel, Andockarme, zwei Geschuetztuerme")
def shipyard(b):
    L, W, H = 8.5, 3.8, 2.8
    for s in (1, -1):
        Wl = [(L + 0.6, 0.5, H * 0.9), (L, 0.9, H), (-L, 0.9, H), (-L - 0.4, 0.7, H * 0.9)]
        b.loft(Wl, M("Hull"), x=s * W)
        for y in (5.0, 1.6, -1.8, -5.2):
            side_hatch(b, s * (W + 0.9), y, 0.0, 2.4, 2.4, mat="Steel")
        b.box(0.08, 2 * L - 1.0, 0.18, M("Light"), loc=(s * (W - 0.92), 0, 1.2))
        b.box(0.08, 2 * L - 1.0, 0.18, M("Light"), loc=(s * (W - 0.92), 0, -1.2))
        b.box(0.1, 2 * L - 2.0, 0.9, M("Accent"), loc=(s * (W + 0.93), 0, 2.2))
    for z in (1, -1):
        Rf = [(L, 0.0, 0.0, z * (H + 0.1)), (L - 0.6, W + 0.9, 0.35, z * (H + 0.1)), (-L, W + 0.9, 0.35, z * (H + 0.1))]
        b.loft(Rf, M("Steel"))
        for y in (6.0, 2.0, -2.0, -6.0):
            b.box(2 * W + 1.6, 0.35, 0.3, M("HullDark"), loc=(0, y, z * (H + 0.45)))
    S = [(-L, 4.4, 3.2), (-L - 2.6, 4.2, 3.0), (-L - 3.4, 3.0, 2.2)]
    b.loft(S, M("Armor"))
    b.box(4.0, 0.12, 0.4, M("Window"), loc=(0, -L - 1.0, H + 0.1))
    # halbfertiger Rumpf in der Bucht (laengs Y)
    prof = [(0, -6.0), (1.2, -5.2), (1.8, -3.0), (2.0, 0.0), (1.8, 2.5), (0, 1.0)]
    b.lathe(prof, M("Hull"), seg=10, rot=(-90, 0, 0), scale=(0.8, 1, 1))
    for yr in (2.9, 4.0, 5.0):
        r = 1.5 if yr < 4.5 else 0.9
        b.ring(r, 0.18, 0.18, M("HullDark"), seg=10, loc=(0, yr, 0), rot=(90, 0, 0))
    b.lathe([(1.82, -0.1), (1.82, 0.4)], M("Accent"), seg=10, rot=(-90, 0, 0), loc=(0, -3.0, 0), scale=(0.8, 1, 1))
    for s in (1, -1):
        b.beam(V(s * 1.5, -1.0, 0), V(s * (W - 0.9), -1.0, 0), 0.3, M("HullDark"))
        b.beam(V(s * 1.0, 3.0, 1.0), V(s * (W - 0.9), 3.0, 1.6), 0.2, M("Hazard"))
    # Leitstand auf dem Heckblock
    b.box(2.6, 2.4, 2.2, M("Hull"), loc=(0, -L - 1.6, H + 1.4), bevel=0.2)
    b.box(2.7, 0.1, 0.4, M("Window"), loc=(0, -L - 0.38, H + 1.8))
    b.dish((1.2, -L - 2.4, H + 2.6), 1.0, M("Hull"), rot=(-30, 0, 0))
    b.mast((-0.8, -L - 2.4, H + 2.5), 2.2)
    b.insignia(V(0, -L - 3.45, 0.0), (0, -1, 0), s=0.9)
    for s in (1, -1):
        d = V(s, 0, 0)
        b.cyl(0.45, 2.2, M("HullDark"), seg=8, loc=d * (W + 2.0) + V(0, -L - 1.6, 0), rot=(0, 90, 0))
        b.ring(1.0, 0.3, 0.6, M("Hull"), seg=8, loc=d * (W + 3.1) + V(0, -L - 1.6, 0), rot=(0, 90, 0))
        b.ring(0.75, 0.1, 0.62, M("Light"), seg=8, loc=d * (W + 3.1) + V(0, -L - 1.6, 0), rot=(0, 90, 0))
    hp_turret(b, 0, (W, L - 2.0, H + 0.45), s=0.95)
    hp_turret(b, 1, (-W, -L + 3.0, -H - 0.45), s=0.95, up=False)
    b.nav_lights([V(s * W, y, z) for s in (1, -1) for y in (L + 0.6, -L) for z in (H, -H)], mat="Light", s=0.22)


@model("fuel_refinery", "Geriffelte Destillationsachse quer durch die Station mit Abfackelflamme oben, sechs Kugeltanks ringsum an Auslegern, Liegetanks ueber und unter dem Ring, Radiatoren")
def fuel_refinery(b):
    prof = [(0, -8.5), (0.9, -8.0), (1.3, -7.0)]
    for k in range(6):
        z = -6.5 + k * 2.3
        prof += [(1.3, z), (1.6, z + 0.15), (1.6, z + 0.45), (1.3, z + 0.6)]
    prof += [(1.3, 7.2), (0.7, 7.8), (0.3, 8.6), (0, 8.7)]
    b.lathe(prof, M("Hull"), seg=12)
    b.lathe([(0, 8.4), (0.65, 8.9), (0.45, 9.9), (0, 11.2)], M("Glow"), seg=8)
    b.ring(1.9, 0.4, 0.8, M("Accent"), seg=12)
    R = 6.6
    b.ring(R + 0.25, 0.5, 0.5, M("HullDark"), seg=12, start=30)
    for i, (x, y, a) in enumerate(polar(6, R, 0)):
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        b.beam(d * 1.5, d * (R - 1.9), 0.35, M("HullDark"))
        b.sphere(2.0, M("Hull" if i % 2 else "Accent"), seg=12, rings=6, loc=(x, y, 0))
        b.lathe([(1.99, -0.28), (1.99, 0.28)], M("Hazard" if i % 2 == 0 else "HullDark"), seg=12, loc=(x, y, 0))
        b.nav_lights([V(x, y, 2.15)], mat="LightRed", s=0.18)
    for z, a in ((3.4, 30), (-3.4, 90)):
        for k in range(3):
            ang = math.radians(a + 120 * k)
            d = V(math.cos(ang), math.sin(ang), 0)
            n = V(-d.y, d.x, 0)
            b.capsule(d * 3.6 + V(0, 0, z), n, 3.6, 0.65, M("Hazard" if k == 0 else "Hull"), seg=8)
            b.beam(V(0, 0, z), d * 3.0 + V(0, 0, z), 0.25, M("HullDark"))
    for a in (60, 240):
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        b.radiator(d * (R + 2.0), d, 5.0, 2.0, tilt=20)
    b.dock(V(0, 0, -8.6), (0, 0, -1), r=0.5, L=1.2)


# =============================================================== Handel und Lager
@model("trade_station", "Moderner Handels-Hub (Terminal): flacher, dreistoeckiger Scheibenbau mit umlaufender verglaster Promenade, fuenf radiale Andockfinger mit Andockklammern und Positionslichtern, geschlossener Hangar mit Leuchttor (Schiffshaendler), Kranz aus Frachtmodulen unter der Scheibe, Kontroll- und Sendeturm, breite Accent-Baender")
def trade_station(b):
    prof = [(0, -2.2), (4.6, -2.2), (6.2, -1.6), (6.6, -0.9), (6.6, -0.3), (6.0, 0.0), (6.0, 0.6), (6.4, 0.9),
            (6.4, 1.5), (5.2, 1.9), (3.4, 2.1), (0, 2.1)]
    b.lathe(prof, M("Hull"), seg=16)
    b.lathe([(6.03, 0.06), (6.03, 0.54)], M("Glass"), seg=16)
    b.window_band(6.06, 0.3, 0.16, n=8, seg=16, gap=0.3)
    b.lathe([(6.63, -0.85), (6.63, -0.35)], M("Accent"), seg=16)
    b.lathe([(6.43, 1.05), (6.43, 1.35)], M("Accent"), seg=16)
    b.window_band(5.3, 1.75, 0.14, n=12, seg=16, gap=0.4)
    b.lathe([(3.0, -2.2), (2.2, -3.4), (1.0, -3.9), (0, -4.0)], M("Steel"), seg=12)
    # Andockfinger mit Klammern (fuenf), der sechste Platz ist der Hangar
    for k in range(5):
        a = 90 + 60 * k
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        n = V(-d.y, d.x, 0)
        b.box(3.4, 1.3, 0.9, M("Hull"), loc=d * 7.9 + V(0, 0, -0.6), rot=(0, 0, a), bevel=0.15)
        b.box(3.5, 0.3, 0.3, M("Accent"), loc=d * 7.9 + V(0, 0, -0.1), rot=(0, 0, a))
        for u in (1, -1):
            b.box(1.2, 0.3, 0.7, M("HullDark"), loc=d * 10.0 + n * u * 0.75 + V(0, 0, -0.6), rot=(0, 0, a))
            b.sphere(0.16, M("LightRed" if u > 0 else "Light"), seg=6, rings=4,
                     loc=d * 10.55 + n * u * 0.75 + V(0, 0, -0.15))
        b.ring(0.62, 0.2, 0.5, M("Light"), seg=8, loc=d * 9.7 + V(0, 0, -0.6), rot=qdir(d))
    # Hangar des Schiffshaendlers
    a = -30
    d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
    n = V(-d.y, d.x, 0)
    c = d * 8.6 + V(0, 0, -0.5)
    b.box(5.2, 4.4, 2.6, M("Hull"), loc=c, rot=(0, 0, a), bevel=0.3)
    b.hangar_slit(c + d * 2.62, d, w=3.4, h=1.6)
    b.box(5.3, 0.3, 0.6, M("Accent"), loc=c + n * 2.22, rot=(0, 0, a))
    b.box(5.3, 0.3, 0.6, M("Accent"), loc=c - n * 2.22, rot=(0, 0, a))
    b.prism([(-2.0, -0.9), (1.4, -0.5), (2.4, 0), (1.4, 0.5), (-2.0, 0.9)], 0.7, M("Steel"),
            loc=c + V(0, 0, 1.65), rot=(0, 0, a), top_scale=0.6)
    # Frachtmodul-Kranz unter der Scheibe
    for k, (x, y, aa) in enumerate(polar(6, 4.4, 30)):
        b.capsule(V(x, y, -3.0), (0, 0, 1), 2.4, 0.7, M("Steel" if k % 2 else "Hull"), seg=6)
        b.ring(0.68, 0.12, 0.3, M("Accent" if k % 2 == 0 else "HullDark"), seg=8, loc=(x, y, -3.0))
    # Oberdeck mit Atrium-Glasband
    b.lathe([(3.4, 2.05), (3.4, 2.7), (3.0, 3.1), (0, 3.1)], M("Steel"), seg=16)
    b.lathe([(3.42, 2.2), (3.42, 2.55)], M("Glass"), seg=16)
    # Kontroll- und Sendeturm (Wahrzeichen): gestufter Schaft, grosse verglaste Kanzel
    b.lathe([(1.9, 3.0), (1.9, 3.8), (1.5, 4.2), (1.3, 4.2), (1.3, 6.4), (1.1, 6.8)], M("Hull"), seg=12)
    b.window_band(1.32, 5.3, 0.25, n=6, seg=12, gap=0.4)
    b.lathe([(1.1, 0), (2.9, 0.6), (3.1, 1.4), (2.5, 2.1), (1.2, 2.4), (0, 2.5)], M("Hull"), seg=12, loc=(0, 0, 6.8))
    b.lathe([(3.04, 0.75), (3.0, 1.3)], M("Window"), seg=12, loc=(0, 0, 6.8))
    b.lathe([(3.12, 1.38), (2.8, 1.75)], M("Accent"), seg=12, loc=(0, 0, 6.8))
    b.dish((1.3, 0.6, 9.3), 1.5, M("Hull"), rot=(-35, 20, 0))
    b.mast((-0.9, -0.5, 9.1), 3.4)
    # Dachflaeche: Landefelder, Technikfelder, Luken, Aufbauten, Accent-Markierungen
    for k, aa in enumerate((150, 270, 30)):
        d = V(math.cos(math.radians(aa)), math.sin(math.radians(aa)), 0)
        pc = d * 4.35 + V(0, 0, 2.02)
        b.cyl(0.85, 0.08, M("HullDark"), seg=8, loc=pc)
        b.ring(0.82, 0.12, 0.1, M("Accent"), seg=8, loc=pc + V(0, 0, 0.02))
        b.box(0.5, 0.1, 0.03, M("Accent"), loc=pc + V(0, 0, 0.06), rot=(0, 0, aa))
        b.box(0.1, 0.5, 0.03, M("Accent"), loc=pc + V(0, 0, 0.06), rot=(0, 0, aa))
        n = V(-d.y, d.x, 0)
        b.nav_lights([pc + n * 0.95 + V(0, 0, 0.1), pc - n * 0.95 + V(0, 0, 0.1)], s=0.1)
    for aa in (90, 210, 330):
        d = V(math.cos(math.radians(aa)), math.sin(math.radians(aa)), 0)
        n = V(-d.y, d.x, 0)
        c = d * 4.3 + V(0, 0, 2.0)
        b.box(1.2, 1.4, 0.5, M("Steel"), loc=c + V(0, 0, 0.22), rot=(0, 0, aa))
        vents(b, c + V(0, 0, 0.47), n=3, length=1.0, step=0.3)
        top_hatch(b, (d * 4.9 + n * 0.9).x, (d * 4.9 + n * 0.9).y, 1.93, 0.5, 0.5, rib=False)
    for k in range(6):
        aa = 60 * k + 60
        d = V(math.cos(math.radians(aa)), math.sin(math.radians(aa)), 0)
        b.box(1.4, 0.3, 0.04, M("Accent"), loc=d * 5.6 + V(0, 0, 1.78), rot=(0, 0, aa))
    b.insignia(V(0, -6.65, -0.6), (0, -1, 0), s=0.9)


@model("trade_post", "Kleiner Orbital-Umschlagpunkt: kompakter achteckiger Frachtknoten mit zwei Faehrenkrallen, darunter ein langer Orbitalaufzug mit Kletterkapseln und absteigender Frachtfaehre, Hangarschlitz")
def trade_post(b):
    octa = [(math.cos(math.radians(22.5 + 45 * i)) * 3.4, math.sin(math.radians(22.5 + 45 * i)) * 3.4) for i in range(8)]
    b.prism(octa, 2.2, M("Hull"), top_scale=0.85)
    b.prism(octa, 1.4, M("Armor"), loc=(0, 0, -1.8), top_scale=1.0, scale=(0.9, 0.9, 1))
    b.window_band(3.2, 0.2, 0.25, n=8, seg=8, gap=0.4)
    b.hangar_slit(V(-3.15, -1.3, 0.1), (-1, -0.4, 0), w=1.8, h=0.7)
    for s in (1, -1):
        d = V(s, 0, 0)
        b.beam(d * 3.2, d * 6.0, 0.6, M("HullDark"))
        b.cyl(1.7, 0.3, M("HullDark"), seg=8, loc=d * 7.4)
        b.ring(1.7, 0.25, 0.36, M("Hazard"), seg=8, loc=d * 7.4)
        b.nav_lights([d * 7.4 + V(0, k * 1.9, 0.3) for k in (-1, 1)], s=0.14)
    b.prism([(-1.3, -0.65), (0.9, -0.35), (1.5, 0), (0.9, 0.35), (-1.3, 0.65)], 0.6, M("Accent"),
            loc=(7.4, 0, 0.6), rot=(0, 0, 100), top_scale=0.6)
    b.cyl(0.3, 2.4, M("HullDark"), seg=6, loc=(0, 0, 2.2))
    b.dish((0, 0, 3.3), 1.2, M("Hull"), rot=(-25, 0, 0))
    # Orbitalaufzug
    b.lathe([(0, -4.4), (1.2, -3.8), (1.8, -2.5)], M("Steel"), seg=8)
    b.cyl(0.32, 10.0, M("Accent"), seg=6, loc=(0, 0, -9.0))
    for z in (-6.0, -10.0, -13.6):
        b.cyl(0.45, 0.3, M("Light"), seg=6, loc=(0, 0, z))
    for z, s in ((-7.6, 1.0), (-11.8, 0.8)):
        b.lathe([(0, -1.0), (0.9, -0.8), (1.1, 0), (0.9, 0.8), (0, 1.0)], M("Hull"), seg=8, loc=(0, 0, z),
                scale=(s, s, s))
        b.lathe([(1.12 * s, -0.12), (1.12 * s, 0.12)], M("Window"), seg=8, loc=(0, 0, z))
    b.prism([(-1.1, -0.55), (0.8, -0.3), (1.3, 0), (0.8, 0.3), (-1.1, 0.55)], 0.5, M("Accent"),
            loc=(-3.0, 1.6, -7.0), rot=(0, 55, 150), top_scale=0.6)
    b.nav_lights([(-3.0, 1.6, -7.8)], mat="Glow", s=0.18)
    b.insignia(V(0, -3.25, 0.2), (0, -1, 0), s=0.6)


@model("build_site", "Schwebende Baustelle: zylindrischer Geruestkaefig aus gelben Ringen und Streben um ein halb beplanktes Kugelmodul (offene Spanten), Auslegerkran mit Platte, schwebende Bauteile, Baudrohnen, Warnlichter")
def build_site(b):
    b.sphere(3.0, M("Hull"), seg=12, rings=8, half=True, rot=(180, 0, 0))
    b.ring(3.1, 0.35, 0.35, M("Accent"), seg=12)
    for k in range(3):
        b.ring(3.0, 0.2, 0.2, M("HullDark"), seg=6, rot=(90, 0, 60 * k), arc=180)
    for z in (1.0, 2.0):
        r = math.sqrt(9 - z * z)
        b.ring(r, 0.18, 0.18, M("HullDark"), seg=12, loc=(0, 0, z), arc=240, start=30 * z)
    b.box(2.2, 1.6, 0.15, M("Hull"), loc=(1.0, -1.6, 2.2), rot=(30, 10, 20))
    R = 6.0
    for z in (-3.6, 0.0, 3.6):
        b.ring(R, 0.3, 0.3, M("Hazard" if z else "HullDark"), seg=8, loc=(0, 0, z), start=22.5)
    for x, y, a in polar(8, R - 0.15, 22.5):
        b.beam(V(x, y, -3.6), V(x, y, 3.6), 0.22, M("HullDark"))
    for i, (x, y, a) in enumerate(polar(8, R - 0.15, 22.5)):
        x2, y2, _ = polar(8, R - 0.15, 22.5 + 45)[0]
        a2 = math.radians(22.5 + 45 * (i + 1))
        p2 = V((R - 0.15) * math.cos(a2), (R - 0.15) * math.sin(a2), 0)
        z0, z1 = (-3.6, 0.0) if i % 2 else (0.0, 3.6)
        b.beam(V(x, y, z0), V(p2.x, p2.y, z1), 0.13, M("HullDark"))
    for x, y, a in polar(4, 3.0, 0):
        b.beam(V(x, y, 0), V(x * (R - 0.2) / 3.0, y * (R - 0.2) / 3.0, 0), 0.22, M("HullDark"))
    c = V(R * math.cos(math.radians(67.5)), R * math.sin(math.radians(67.5)), 3.6)
    b.box(1.2, 1.2, 1.0, M("Hazard"), loc=c + V(0, 0, 0.6))
    tip = c + V(6.0, 3.0, 2.0)
    b.truss(c + V(0, 0, 1.0), tip, 0.35, 0.1, M("Hazard"), n=6)
    b.beam(tip, tip - V(0, 0, 3.0), 0.06, M("Hull"))
    b.box(2.0, 1.4, 0.15, M("Hull"), loc=tip - V(0, 0, 3.1), rot=(0, 0, 25))
    for (x, y, z, rx, rz) in ((8.0, -2.0, 1.0, 25, 10), (-7.6, -3.4, -2.2, -15, 40), (-6.0, 5.0, 2.8, 35, -20),
                              (2.0, -8.0, -3.0, 50, 70)):
        b.box(2.2, 1.4, 0.15, M("Hull"), loc=(x, y, z), rot=(rx, 0, rz))
    b.box(1.8, 1.0, 1.0, M("Container"), loc=(-1.0, 3.6, -2.4), rot=(0, 0, 20))
    for p in ((4.0, -5.4, 2.4), (-4.6, 4.4, -1.4)):
        b.box(0.7, 0.7, 0.35, M("HullDark"), loc=p)
        b.nav_lights([V(*p) + V(0, 0, 0.3)], mat="Glow", s=0.16)
    b.nav_lights([(x, y, z) for x, y, a in polar(4, R, 22.5) for z in (3.9, -3.9)], mat="Glow", s=0.24)


# =============================================================== Regierung, Sicherheit, Sonstiges
@model("headquarters", "Wehrhafte Fraktionszentrale: gestufte Doppelspitze mit Panzerguertel und Hangarschlitzen in der Taille, vier kleine Hoheitszeichen, Rotationsring mit Landearmen, zwei Zwillingstuerme, zwei Raketenwerfer, Punktverteidigung oben und unten, Sensormasten")
def headquarters(b):
    up = [(6.8, 0.0), (6.6, 0.8), (4.6, 1.2), (3.4, 1.6), (3.2, 3.6), (2.4, 4.0), (2.2, 6.4),
          (1.5, 6.8), (1.3, 8.8), (0.6, 9.4), (0.3, 11.5), (0, 12.2)]
    down = [(0, -9.0), (0.4, -8.2), (1.0, -7.6), (1.2, -6.0), (1.8, -5.6), (2.2, -3.8), (3.2, -3.4),
            (3.4, -1.6), (4.6, -1.2), (6.6, -0.8)]
    b.lathe(down + up, M("Hull"), seg=12)
    b.armor_belt(7.05, 0.0, 1.5, 12, tilt=0)
    for x, y, a in polar(4, 7.25, 0):
        b.hangar_slit(V(x, y, 0), (x, y, 0), w=2.2, h=0.7)
    for z in (2.2, -2.4):
        b.window_band(3.3 if z > 0 else 3.5, z, 0.3, n=6, seg=12, gap=0.3)
    b.window_band(2.27, 5.0, 0.3, n=6, seg=12, gap=0.3)
    b.window_band(1.35, 7.8, 0.3, n=4, seg=12, gap=0.3)
    b.armor_belt(3.55, 2.6, 0.7, 8, mat="Steel", tilt=-20)
    b.armor_belt(3.75, -2.6, 0.7, 8, mat="Steel", tilt=20)
    for x, y, a in polar(4, 1.5, 0):
        b.beam(V(x, y, 8.6), V(x * 0.3, y * 0.3, 10.6), 0.18, M("Accent"))
    b.nav_lights([(0, 0, 12.3)], mat="LightRed", s=0.3)
    for x, y, a in polar(4, 0, 45):
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        b.insignia(d * 3.5 + V(0, 0, 2.7), d + V(0, 0, 0.4), s=0.75)
    b.torus(9.6, 0.45, M("HullDark"), seg=24, minor=6)
    for x, y, a in polar(4, 0, 0):
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        b.beam(d * 6.6, d * 9.3, 0.45, M("HullDark"))
        b.cyl(1.1, 0.3, M("HullDark"), seg=8, loc=d * 10.8)
        b.ring(1.1, 0.2, 0.32, M("Hazard"), seg=8, loc=d * 10.8)
        b.nav_lights([d * 10.8 + V(0, 0, 0.3)], s=0.18)
    for i, (x, y, a) in enumerate(polar(2, 5.0, 90)):
        hp_turret(b, i, (x, y, 1.15), s=1.0)
    hp_shafts(b, 2, (5.2, 0.0, 1.1), rows=2, cols=2, cell=0.55)
    b.cyl(1.5, 0.3, M("Armor"), seg=8, loc=(-5.2, 0, -1.05))
    hp_turret(b, 3, (-5.2, 0.0, -1.2), s=1.0, up=False)
    b.cyl(1.9, 0.3, M("Armor"), seg=8, loc=(-5.2, 0, 1.05))
    hp_turret(b, 4, (-5.2, 0.0, 1.2), s=1.35, twin=True, length=2.2)
    hp_shafts(b, 5, (5.2, 0.0, -1.1), rows=2, cols=2, cell=0.55, down=True)
    b.mast((1.0, 0.6, 9.0), 2.2)


@model("sector_hq", "Achteckiger Verwaltungs- und Wachring aus Panzerplatten um eine Kommandokugel, Hoheitszeichen an zwei Ringseiten, Hangarschlitze, vier Geschuetztuerme (oben und unten), Funkschuessel, Punktverteidigung, gepanzerte Schleuse")
def sector_hq(b):
    b.sphere(2.6, M("Steel"), seg=10, rings=6)
    b.window_band(2.62, 0.0, 0.4, n=8, seg=10, gap=0.3)
    b.armor_belt(2.85, 1.1, 0.6, 8, mat="Armor", tilt=-25)
    b.armor_belt(2.85, -1.1, 0.6, 8, mat="Armor", tilt=25)
    R = 7.4
    b.lathe([(R - 1.6, -0.8), (R, -0.8), (R, 0.8), (R - 1.6, 0.8)], M("Hull"), seg=8, closed=True, start=22.5)
    b.armor_belt(R + 0.15, 0.0, 1.5, 8, mat="Armor", tilt=0, start=22.5 + 22.5)
    b.lathe([(R - 1.62, 0.2), (R - 1.62, -0.2)], M("Window"), seg=8, start=22.5)
    for x, y, a in polar(4, 2.5, 0):
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        b.beam(d * 2.4, d * (R - 1.5), 0.55, M("HullDark"))
    for a in (90, 270):
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        b.insignia(d * (R + 0.55), d, s=0.75)
    for a in (45, 225, 135, 315):
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        b.hangar_slit(d * (R + 0.5), d, w=1.6, h=0.6)
    for i, (x, y, a) in enumerate(polar(2, R - 0.8, 0)):
        hp_turret(b, i, (x, y, 0.8), s=1.0)
    hp_turret(b, 2, (0, R - 0.8, -0.8), s=1.0, up=False)
    hp_turret(b, 3, (0, -(R - 0.8), -0.8), s=1.0, up=False, length=2.2)
    b.cyl(0.35, 1.2, M("HullDark"), seg=6, loc=(0, 0, 3.0))
    b.dish((0, 0, 3.6), 1.8, M("Hull"), rot=(-30, 0, 30))
    b.dock(V(0, 0, -2.5), (0, 0, -1), r=0.6, L=1.8, mat="Armor")
    b.mast((1.2, -1.2, 2.2), 2.4)
    b.nav_lights([(x, y, 0.95) for x, y, a in polar(8, R, 22.5)], mat="LightRed", s=0.16)


@model("office", "Kompakter Bueroknoten: gepanzerter Kugelknoten mit dickem Buero-Druckmodul (kalte Fensterbaender, Panzerkragen), Versorgungsmodul unten, Andockschleuse, zwei kurze Solarfluegel, Sensormast")
def office(b):
    b.sphere(2.2, M("Steel"), seg=10, rings=6)
    b.armor_belt(2.3, 0, 0.8, 8, tilt=0)
    b.capsule(V(4.4, 0, 0), (1, 0, 0), 6.4, 2.1, M("Hull"), seg=10)
    for x in (3.0, 4.4, 5.8):
        b.lathe([(2.13, -0.25), (2.13, 0.25)], M("Window"), seg=10, loc=(x, 0, 0), rot=(0, 90, 0), arc=144, start=18)
        b.lathe([(2.13, -0.25), (2.13, 0.25)], M("Window"), seg=10, loc=(x, 0, 0), rot=(0, 90, 0), arc=144, start=198)
    for x in (2.2, 6.7):
        b.torus(2.15, 0.2, M("Armor"), seg=10, minor=4, loc=(x, 0, 0), rot=(0, 90, 0))
    b.capsule(V(0, 0, -3.0), (0, 0, 1), 3.2, 1.3, M("HullDark"), seg=8)
    b.dock(V(-2.1, 0, 0), (-1, 0, 0), r=0.8, L=1.5, mat="Armor")
    for s in (1, -1):
        b.radiator(V(0, s * 2.0, 0), (0, s, 0), 5.2, 3.2, tilt=20 * s, mat="Panel", stripe="Hull", n=2)
    b.mast((5.2, 0, 2.0), 2.6)
    b.insignia(V(4.4, -2.12, 0.0), (0, -1, 0), s=0.6)
    b.nav_lights([(7.75, 0, 0)], mat="LightRed", s=0.2)


@model("police_station", "Wehrhafter sechszackiger Stern in dunkler Panzerung, Kommandoturm, blaue und rote Signallichter, Hangartore, drei Geschuetze")
def police_station(b):
    star = []
    for i in range(12):
        r = 8.0 if i % 2 == 0 else 3.8
        a = math.radians(30 * i)
        star.append((r * math.cos(a), r * math.sin(a)))
    b.prism(star, 2.2, M("HullDark"), top_scale=0.75)
    b.prism([(x * 0.7, y * 0.7) for x, y in star], 1.4, M("HullDark"), loc=(0, 0, -1.8), top_scale=1 / 0.7)
    b.prism([(x * 0.78, y * 0.78) for x, y in star], 0.2, M("Steel"), loc=(0, 0, 1.15))
    b.prism([(x * 0.8, y * 0.8) for x, y in star], 0.12, M("Accent"), loc=(0, 0, 1.0), top_scale=1.0)
    for i in range(6):
        a = 60 * i
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        b.nav_lights([d * 7.0 + V(0, 0, 0.1)], mat="Light" if i % 2 else "LightRed", s=0.3)
        if i % 2 == 0:
            n = V(-d.y, d.x, 0)
            b.box(0.3, 1.8, 0.9, M("Light"), loc=d * 3.6 + n * 1.6 + V(0, 0, -0.3), rot=(0, 0, a + 30))
    b.lathe([(0, 1.0), (2.8, 1.0), (2.6, 2.6), (1.8, 3.4), (1.8, 4.6), (1.0, 5.2), (0, 5.4)],
            M("Hull"), seg=12)
    b.window_band(2.75, 1.8, 0.3, n=6, seg=12)
    b.window_band(1.85, 4.0, 0.3, n=6, seg=12)
    b.ring(2.0, 0.2, 0.3, M("Accent"), seg=12, loc=(0, 0, 3.4))
    b.box(0.6, 0.3, 0.3, M("Light"), loc=(0.4, 0, 5.4))
    b.box(0.6, 0.3, 0.3, M("LightRed"), loc=(-0.4, 0, 5.4))
    for i, (x, y, a) in enumerate(polar(3, 5.6, 30)):
        hp_turret(b, i, (x, y, 0.75), s=0.95, length=2.0 if i == 2 else 1.6)
    b.lathe([(0, -4.4), (1.0, -4.2), (1.8, -3.6), (1.8, -2.4), (2.6, -1.8), (2.8, -1.0)], M("Hull"), seg=12)
    b.window_band(1.85, -3.0, 0.3, n=6, seg=12)
    b.dock(V(0, 0, -4.3), (0, 0, -1), r=0.6, L=1.2)
    b.nav_lights([(0, 0, -5.6)], mat="Light", s=0.25)
    for x, y, a in polar(3, 3.4, 30):
        b.hangar_slit(V(x, y, 0.0), (x, y, 0), w=1.6, h=0.6)
    b.insignia(V(0, -2.85, 1.8), (0, -1, 0.2), s=0.55)


@model("pirate_hideout", "Bedrohliche, zusammengeflickte Piratenfestung in einem ausgehoehlten, zerklueften Asteroiden: rot gluehendes Hangarmaul, eingeschweisste erbeutete Rumpfteile, Flickpanzer in ungleichen Farben, Rammsporne, grobe Geschuetze oben und unten, Trossen zu kleineren Brocken, Warn- und Feuerlichter, schiefe Antennen")
def pirate_hideout(b):
    b.rock(5.6, M("Rock"), seed=2.3, seg=11, rings=8, scale=(1.15, 1.0, 0.95), amp=0.42)
    b.rock(2.4, M("Rock"), seed=7.7, seg=8, rings=6, loc=(2.8, 3.6, 3.2), amp=0.45)
    b.rock(2.0, M("Rock"), seed=5.2, seg=8, rings=6, loc=(-2.6, 2.6, -4.0), amp=0.45)
    # Hangarmaul zur Kamera (-x/-y)
    d = V(-0.6, -0.8, 0.08).normalized()
    q = qdir(d)
    m0 = d * 5.6
    b.cyl(2.6, 3.2, M("HullDark"), seg=7, loc=m0 - d * 1.3, rot=q)
    b.ring(3.6, 1.1, 1.4, M("Rust"), seg=7, loc=m0, rot=q)
    b.lathe([(2.55, 0), (0, 0)], M("Ember"), seg=7, loc=m0 + d * 0.32, rot=q)
    b.box(4.0, 0.25, 0.3, M("Hazard"), loc=m0 + d * 0.75 + V(0, 0, 2.9), rot=(0, 0, math.degrees(math.atan2(d.y, d.x)) + 90))
    n = V(-d.y, d.x, 0).normalized()
    up = d.cross(n).normalized()
    for k in range(7):
        a = math.radians(360 * k / 7 + 10)
        rad = (n * math.cos(a) + up * math.sin(a))
        p = m0 + d * 0.5 + rad * 3.7
        b.lathe([(0.4, 0), (0, 1.8)], M("HullDark"), seg=4, loc=p, rot=qdir(d * 0.7 + rad))
        if k % 2 == 0:
            b.sphere(0.2, M("LightRed"), seg=5, rings=3, loc=m0 + d * 0.8 + rad * 3.1)
    # eingeschweisste Beuterumpfe
    b.capsule(V(1.6, -1.0, 5.6), (0.7, 0.2, 0.7), 6.0, 1.3, M("Steel"), seg=8)
    b.box(2.2, 0.15, 0.5, M("Accent"), loc=V(2.6, -1.3, 6.4), rot=(0, -45, 15))
    b.prism([(-2.2, -1.1), (1.6, -0.6), (2.8, 0), (1.6, 0.6), (-2.2, 1.1)], 1.0, M("Hull"),
            loc=(-4.6, 0.8, -4.2), rot=(25, -30, 60), top_scale=0.6)
    b.capsule(V(5.8, -1.2, -2.6), (0.2, 1.0, -0.6), 4.6, 1.0, M("Armor"), seg=8)
    # Flickpanzer in ungleichen Farben
    plates = [((-3.8, -3.6, 3.0), (30, 10, 40), "Rust"), ((4.6, -3.0, 2.2), (-20, 30, 20), "Armor"),
              ((5.6, 1.4, 1.0), (10, 60, 80), "Steel"), ((-5.8, 0.6, 1.4), (70, 10, -20), "Hull"),
              ((0.6, -5.4, -2.4), (-40, 0, 10), "Rust"), ((-1.8, 4.6, 2.0), (30, -20, 75), "Steel"),
              ((2.6, 3.4, -4.4), (40, 30, 0), "Armor"), ((-0.4, -3.2, -5.2), (-15, 10, 5), "Hull")]
    for p, r, m in plates:
        b.box(2.8, 2.0, 0.28, M(m), loc=p, rot=r)
    # Rammsporne
    for dv in ((1, 0.2, 0.1), (0.6, 0.9, 0.3), (-0.3, 1, -0.2), (0.2, -0.4, 1), (-0.7, 0.3, 0.8),
               (0.4, 0.2, -1), (-0.9, -0.1, -0.5), (0.9, -0.5, -0.5)):
        dv = V(*dv).normalized()
        b.lathe([(0.55, 0), (0.25, 2.4), (0, 3.6)], M("HullDark" if dv.z > 0 else "Rust"), seg=4,
                loc=dv * 5.2, rot=qdir(dv))
    # grobe Geschuetze in Kasematten im Fels: Massetreiber an der Flanke und unter dem Bauch, Laser oben
    c = V(7.2, 1.2, 0.4)
    b.box(3.4, 2.8, 2.6, M("HullDark"), loc=c, rot=(0, 0, 8))
    b.box(3.6, 0.4, 2.9, M("Rust"), loc=c + V(0, 1.3, 0), rot=(0, 0, 8))
    b.box(0.3, 3.0, 0.5, M("Hazard"), loc=c + V(1.7, 0, 1.15))
    b.box(0.4, 1.0, 0.8, M("Steel"), loc=c + V(1.65, -0.9, -0.8), rot=(15, 0, 0))
    hp_turret(b, 0, c + V(1.7, 0, 0), s=1.0, side=1, length=2.4)
    c = V(1.8, 0.6, -6.6)
    b.cyl(1.6, 2.8, M("Rust"), seg=6, loc=c + V(0, 0, 1.3))
    b.box(3.4, 0.3, 0.9, M("Armor"), loc=c + V(0, 1.3, 0.5), rot=(0, -10, 0))
    b.box(0.5, 0.5, 0.5, M("Glow"), loc=c + V(-1.5, -0.5, 0.6))
    hp_turret(b, 1, c - V(0, 0, 0.1), s=1.0, up=False, length=2.4)
    c = V(0.6, 2.4, 6.2)
    b.cyl(1.5, 2.6, M("Rust"), seg=6, loc=c - V(0, 0, 1.2))
    b.box(3.4, 0.3, 0.9, M("Armor"), loc=c + V(0, -1.3, -0.4), rot=(0, 10, 0))
    b.box(0.6, 0.6, 0.6, M("Glow"), loc=c + V(1.4, 0.4, -0.6))
    hp_turret(b, 2, c + V(0, 0, 0.1), s=0.95, length=1.8)
    # Trossen zu Brocken
    for i, (c, r) in enumerate(((V(-8.6, 2.6, 1.0), 1.4), (V(7.6, 4.4, -1.6), 1.1), (V(3.4, -7.8, 2.6), 0.9))):
        b.rock(r, M("Rock"), seed=11 + i, seg=7, rings=5, loc=c, amp=0.45)
        p0 = c.normalized() * 5.6
        L = (c - p0).length - r
        dv = (c - p0).normalized()
        for k in range(int(L / 0.9)):
            a = p0 + dv * (k * 0.9 + 0.45)
            b.box(0.7, 0.18, 0.32, M("HullDark"), loc=a, rot=(90 * (k % 2), 0,
                  math.degrees(math.atan2(dv.y, dv.x))))
    # Container und Lagerfeuer-Lichter
    for p, r, m in (((-5.0, -2.6, -2.0), (10, 20, 30), "Container"), ((3.6, -4.6, -0.4), (0, 15, -30), "Hazard"),
                    ((-2.4, -4.2, 4.0), (25, 0, 10), "Container")):
        b.box(2.0, 1.0, 1.0, M(m), loc=p, rot=r)
    for p in ((-2.6, -5.0, 2.0), (2.8, -4.8, -2.8), (-4.8, -3.2, 0.2), (1.2, -5.4, 1.2)):
        b.sphere(0.28, M("Glow"), seg=5, rings=3, loc=p)
    # schiefe Antennen
    for p, dv, h in (((-1.0, 0.8, 5.4), (0.3, -0.2, 1), 4.0), ((2.6, 2.2, 4.4), (-0.4, 0.3, 1), 3.0),
                     ((-0.6, 1.4, -5.4), (0.2, 0.4, -1), 3.4)):
        dv = V(*dv).normalized()
        b.strut(V(*p), V(*p) + dv * h, 0.08, M("HullDark"), seg=4)
        b.sphere(0.2, M("LightRed"), seg=5, rings=3, loc=V(*p) + dv * h)
    b.dish((-3.0, 2.2, 4.4), 1.2, M("Rust"), rot=(-35, -20, 0))
    # mehr Stacheln, Rammsporne und Flickwerk
    rnd = _rand(9.0)
    for k in range(14):
        u, v = next(rnd), next(rnd)
        th, ph = 2 * math.pi * u, math.acos(2 * v - 1)
        dv = V(math.sin(ph) * math.cos(th), math.sin(ph) * math.sin(th), math.cos(ph) * 0.9)
        if dv.dot(V(-0.6, -0.8, 0.1)) > 0.55:
            continue
        b.lathe([(0.45, 0), (0.2, 1.6), (0, 2.8)], M("HullDark" if k % 3 else "Rust"), 4,
                loc=dv * 5.0, rot=qdir(dv))
    for k, (p, r) in enumerate((((4.8, 2.4, 2.4), (20, -40, 60)), ((-4.6, 2.0, -1.6), (-30, 50, 10)),
                                ((1.6, 4.6, -2.6), (60, 10, -20)), ((-1.0, -2.8, 4.6), (10, 20, 40)))):
        b.box(2.4, 1.6, 0.3, M(("Rust", "Steel", "Armor", "Hull")[k]), loc=p, rot=r)
        b.box(0.5, 0.5, 0.35, M("HullDark"), loc=V(*p) + V(0.6, 0.3, 0.1), rot=r)
    d = V(-0.6, -0.8, 0.08).normalized()
    b.lathe([(3.0, 0), (0, 0)], M("Glow"), 7, loc=d * 5.75, rot=qdir(d))


@model("research_station", "Forschungsstation: grosses Radioteleskop auf Gabel, kugelfoermiges Labor mit Fensterband, Sensorring, Solarfluegel")
def research_station(b):
    b.sphere(2.8, M("Hull"), seg=16, rings=10)
    b.window_band(2.82, 0.0, 0.4, n=8, seg=16, gap=0.3)
    b.ring(3.0, 0.25, 0.5, M("Accent"), seg=16, loc=(0, 0, -1.2))
    b.ring(6.6, 0.3, 0.4, M("HullDark"), seg=28, loc=(0, 0, -0.6))
    for x, y, a in polar(4, 6.5, 45):
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        b.beam(d * 2.7 + V(0, 0, -0.6), d * 6.4 + V(0, 0, -0.6), 0.25, M("HullDark"))
    for x, y, a in polar(8, 6.5, 0):
        b.box(0.7, 0.7, 0.9, M("Hull"), loc=(x, y, -0.6), rot=(0, 0, a))
        b.sphere(0.3, M("Light"), seg=6, rings=4, loc=(x, y, -0.05))
    # Teleskop
    b.cyl(0.6, 2.2, M("HullDark"), seg=10, loc=(0, 0, 3.6))
    b.box(3.4, 0.5, 0.5, M("Hull"), loc=(0, 0, 4.8))
    for s in (-1, 1):
        b.beam(V(s * 1.6, 0, 4.8), V(s * 1.6, 0, 6.8), 0.4, M("Hull"))
    b.dish((0, -0.6, 7.0), 4.6, M("Hull"), rot=(40, 0, 0), depth=0.3, seg=18)
    # Solarfluegel
    for s in (-1, 1):
        b.beam(V(0, s * 2.6, -1.6), V(0, s * 4.0, -1.6), 0.25, M("HullDark"))
        b.box(2.6, 4.4, 0.08, M("Panel"), loc=(0, s * 6.4, -1.6), rot=(0, 25, 0))
        b.box(2.7, 4.5, 0.06, M("Hull"), loc=(0, s * 6.4, -1.68), rot=(0, 25, 0))
    b.lathe([(0, -5.0), (1.0, -4.2), (1.6, -2.4)], M("HullDark"), seg=12)
    b.nav_lights([(0, 0, -5.1)], s=0.25)



# =============================================================== Kleinstationen (Waffenstellungen, Satellit)
# Nicht andockbar, frei im Raum. Geschuetze in der Reihenfolge der Weltdatei (defense.guns), Punkt-
# verteidigungstuerme zusaetzlich mit gun -1 (Node Turret_pd<n>).
SMALL_TYPES = [("platform_light", "Leichte Waffenstellung"), ("platform_medium", "Mittlere Waffenstellung"),
               ("platform_heavy", "Schwere Waffenstellung"), ("satellite_sensor", "Überwachungssatellit")]
RADIUS = {"platform_light": 3.5, "platform_medium": 6.0, "platform_heavy": 8.0, "satellite_sensor": 2.6}
DEFAULT_RADIUS = 15.0
# Groessenklassen der Weltdatei (stationTypes[].size); XS = Kleinstationen mit eigenem Radius (RADIUS)
SIZE_RADIUS = {"S": 10.0, "M": 15.0, "L": 22.0, "XL": 30.0}


def radius_of(key, size=None):
    """Spielradius eines Stationstyps: Kleinstationen je Typ, sonst nach Groessenklasse."""
    if key in RADIUS:
        return RADIUS[key]
    return SIZE_RADIUS.get(size or "M", DEFAULT_RADIUS)
# Soll-Bewaffnung, solange die Weltdatei die Kleinstationen noch nicht kennt (dann gilt defense.guns)
SMALL_GUNS = {"platform_light": ["weapon_laser_s", "weapon_laser_s"],
              "platform_medium": ["weapon_laser_m", "weapon_laser_s", "weapon_massdriver_s"],
              "platform_heavy": ["weapon_laser_m", "weapon_laser_m", "weapon_massdriver_s",
                                 "missile_launcher_s", "missile_launcher_s"],
              "satellite_sensor": []}
SMALL_PD = {"platform_light": 0, "platform_medium": 1, "platform_heavy": 2, "satellite_sensor": 0}


@model("satellite_sensor", "Ueberwachungssatellit (Relais-Schuessel): gepanzerter Sechskant-Bus mit grosser gekippter Relaisschuessel, Sensorkopf und Antennenbuendel, zwei Solarfluegel, Accent-Felder, Baken; unbewaffnet")
def satellite_sensor(b):
    hexa = [(math.cos(math.radians(60 * i + 30)) * 1.0, math.sin(math.radians(60 * i + 30)) * 1.0) for i in range(6)]
    b.prism(hexa, 1.8, M("Hull"))
    b.prism([(x * 1.05, y * 1.05) for x, y in hexa], 0.3, M("Accent"), loc=(0, 0, 0.55))
    b.prism([(x * 1.06, y * 1.06) for x, y in hexa], 0.12, M("HullDark"), loc=(0, 0, -0.5))
    b.box(0.06, 0.8, 0.5, M("Window"), loc=(0.87, 0, -0.1), rot=(0, 0, 30))
    b.cyl(0.25, 0.7, M("HullDark"), seg=6, loc=(0, 0, 1.25))
    b.dish((0, 0.3, 1.6), 1.7, M("Hull"), rot=(-35, 0, 0), depth=0.3, seg=14)
    b.box(1.0, 0.12, 0.08, M("Accent"), loc=(0, -0.5, 1.75), rot=(-35, 0, 0))
    b.sphere(0.45, M("Steel"), seg=8, rings=4, loc=(0, 0, -0.9), half=True, rot=(180, 0, 0))
    b.cyl(0.18, 0.5, M("HullDark"), seg=6, loc=(0, 0, -1.4))
    b.sphere(0.2, M("Light"), seg=6, rings=4, loc=(0, 0, -1.7))
    for s in (1, -1):
        b.radiator(V(s * 0.95, 0, 0), (s, 0, 0), 3.6, 1.3, tilt=0, mat="Panel", stripe="Hull", n=2)
        b.sphere(0.12, M("LightRed" if s > 0 else "Light"), seg=6, rings=3, loc=(s * 4.5, 0, 0.1))
    for dx in (-0.4, 0.4):
        b.strut(V(dx, -0.6, 0.9), V(dx * 1.6, -0.9, 2.3), 0.04, M("HullDark"), seg=4)
        b.sphere(0.08, M("LightRed"), seg=5, rings=3, loc=(dx * 1.6, -0.9, 2.3))
    b.rcs((0.0, 0.95, 0.2), s=0.5)
    b.rcs((0.0, -0.95, 0.2), s=0.5)


@model("platform_light", "Leichte Waffenstellung (Mastturm): robuster Panzerkern mit Accent-Band, kraeftiger Gittermast durch die Ebene mit Laserturm oben und unten auf verstaerkten Plattformen, zwei Kuehlfluegel, Baken")
def platform_light(b):
    b.lathe([(0, -1.6), (1.1, -1.4), (1.6, -0.6), (1.6, 0.6), (1.1, 1.4), (0, 1.6)], M("Hull"), seg=8, start=22.5)
    b.armor_belt(1.7, 0.0, 0.7, 8, mat="Armor", tilt=0, start=45)
    b.lathe([(1.62, 0.65), (1.4, 1.05)], M("Accent"), 8, start=22.5)
    b.lathe([(1.4, -1.05), (1.62, -0.65)], M("Accent"), 8, start=22.5)
    b.window_band(1.63, 0.0, 0.14, n=4, seg=8, gap=0.4)
    for z in (1, -1):
        b.truss(V(0, 0, z * 1.4), V(0, 0, z * 3.6), 0.5, 0.14, M("Steel"), n=4)
        b.cyl(0.95, 0.35, M("Steel"), seg=8, loc=(0, 0, z * 3.75))
        b.ring(1.0, 0.18, 0.4, M("HullDark"), seg=8, loc=(0, 0, z * 3.75))
    hp_turret(b, 0, (0, 0, 3.93), s=0.85, length=1.7)
    hp_turret(b, 1, (0, 0, -3.93), s=0.85, up=False, length=1.7)
    for s in (1, -1):
        b.radiator(V(s * 1.55, 0, 0), (s, 0, 0), 4.6, 1.6, tilt=0, mat="Steel", stripe="Glow", n=2)
        b.sphere(0.14, M("LightRed" if s > 0 else "Light"), seg=6, rings=3, loc=(s * 6.1, 0, 0.12))
    b.insignia(V(0, -1.72, 0.2), (0, -1, 0), s=0.45)
    b.rcs((0, 1.65, 0), s=0.6)


@model("platform_medium", "Mittlere Waffenstellung (Bastionstrommel): geschlossene Achtkant-Panzertrommel mit Technikgraben und Accent-Feldern, drei Randbastionen mit Laserturm M (gross), Laserturm S und Massetreiberturm, Punktverteidigungskuppel in der Mitte, Wartungsluken, Baken")
def platform_medium(b):
    octa = [(math.cos(math.radians(22.5 + 45 * i)) * 3.4, math.sin(math.radians(22.5 + 45 * i)) * 3.4) for i in range(8)]
    b.prism(octa, 1.6, M("Hull"), loc=(0, 0, 0.3), top_scale=0.88)
    b.prism(octa, 1.0, M("Steel"), loc=(0, 0, -1.0), top_scale=1.0, scale=(0.9, 0.9, 1))
    b.prism([(x * 0.8, y * 0.8) for x, y in octa], 0.6, M("HullDark"), loc=(0, 0, -1.7), top_scale=0.8)
    b.armor_belt(3.45, 0.2, 0.8, 8, mat="Armor", tilt=0, start=45)
    b.lathe([(2.0, 1.1), (2.0, 1.5), (1.4, 1.75), (0, 1.75)], M("Steel"), seg=10)
    b.ring(2.05, 0.18, 0.22, M("Accent"), seg=10, loc=(0, 0, 1.25))
    hp_turret(b, -1, (0, 0, 1.75), s=0.6, kind="pd", node="Turret_pd0", length=1.3)
    for i, (gun, s, twin, L) in enumerate(((0, 1.3, True, 2.3), (1, 0.9, False, 1.7), (2, 1.0, False, 2.2))):
        a = 90 + 120 * i
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        c = d * 3.9
        b.box(2.2, 2.0, 1.8, M("Hull"), loc=c + V(0, 0, 0.3), rot=(0, 0, a), bevel=0.2)
        b.box(0.08, 1.6, 0.5, M("Accent"), loc=c + d * 1.12 + V(0, 0, 0.4), rot=(0, 0, a))
        b.box(0.08, 1.2, 0.12, M("Light"), loc=c + d * 1.12 + V(0, 0, -0.1), rot=(0, 0, a))
        hp_turret(b, gun, c + V(0, 0, 1.2), s=s, twin=twin, length=L)
    for i in range(3):
        a = 30 + 120 * i
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        n = V(-d.y, d.x, 0)
        b.box(1.4, 0.25, 0.05, M("HullDark"), loc=d * 2.6 + V(0, 0, 1.11), rot=(0, 0, a))
        greebles(b, (d * 2.6).x, (d * 2.6).y, 1.12, 0.9, 0.9, n=4, seed=3.0 + i)
        b.hangar_slit(d * 3.35 + V(0, 0, -0.9), d, w=1.4, h=0.4)


@model("platform_heavy", "Schwere Waffenstellung (Zitadelle): zweistufige Panzerscheibe mit Ringgraben, zwei schwere Zwillings-Laserturm M am Unterdeck, Massetreiberturm auf dem Oberdeck, zwei bündige Raketen-Schachtfelder, zwei Punktverteidigungstuerme, Panzerschuerzen, Hangarschlitze, grosse Accent-Felder")
def platform_heavy(b):
    octa = [(math.cos(math.radians(22.5 + 45 * i)) * 5.4, math.sin(math.radians(22.5 + 45 * i)) * 5.4) for i in range(8)]
    b.prism(octa, 1.8, M("Hull"), loc=(0, 0, 0.2), top_scale=0.9)
    b.prism(octa, 1.4, M("Steel"), loc=(0, 0, -1.4), top_scale=1 / 0.85, scale=(0.85, 0.85, 1))
    b.prism([(x * 0.55, y * 0.55) for x, y in octa], 0.8, M("HullDark"), loc=(0, 0, -2.4), top_scale=0.8)
    b.armor_belt(5.5, 0.1, 1.0, 8, mat="Armor", tilt=0, start=45)
    up = [(x * 0.55, y * 0.55) for x, y in octa]
    b.prism(up, 1.4, M("Armor"), loc=(0, 0, 1.8), top_scale=0.85)
    b.ring(3.0, 0.3, 0.2, M("HullDark"), seg=8, loc=(0, 0, 1.12), start=22.5)
    b.lathe([(2.6, 2.4), (2.55, 2.55)], M("Accent"), 8, start=22.5)
    for k in range(4):
        a = 45 + 90 * k
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        b.box(0.08, 3.0, 0.8, M("Accent"), loc=d * 5.6 + V(0, 0, 0.3), rot=(0, 0, a))
        b.hangar_slit(d * 4.75 + V(0, 0, -1.3), d, w=1.8, h=0.5)
    hp_turret(b, 0, (-3.4, 1.8, 1.12), s=1.4, twin=True, length=2.4)
    hp_turret(b, 1, (3.4, -1.8, 1.12), s=1.4, twin=True, length=2.4)
    hp_turret(b, 2, (0, 0.6, 2.5), s=1.1, length=2.4)
    hp_shafts(b, 3, (-1.1, -1.3, 2.5), rows=2, cols=2, cell=0.42)
    hp_shafts(b, 4, (1.1, -1.3, 2.5), rows=2, cols=2, cell=0.42)
    hp_turret(b, -1, (3.5, 2.6, 1.12), s=0.65, kind="pd", node="Turret_pd0", length=1.3)
    hp_turret(b, -1, (-3.5, -2.6, 1.12), s=0.65, kind="pd", node="Turret_pd1", length=1.3)
    for i in range(4):
        a = 90 * i
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        greebles(b, (d * 4.3).x, (d * 4.3).y, 1.12, 1.2, 1.2, n=5, seed=11.0 + i)
    b.mast((0.8, 1.4, 2.5), 1.8)
    b.insignia(V(0, -2.7, 1.8), (0, -1, 0.3), s=0.6)


# ---------------------------------------------------------------- Detailbausteine (zweite Detailrunde L-Stationen)
def _rotz(v, deg):
    r = math.radians(deg)
    return V(v.x * math.cos(r) - v.y * math.sin(r), v.x * math.sin(r) + v.y * math.cos(r), v.z)


def plate_field(b, c, sx, sy, nx, ny, rot=0.0, mat="Steel", gap=0.14, h=0.1, down=False):
    """Gestuftes Plattenfeld auf einer waagerechten Flaeche: dunkle Grundplatte mit Fugen, erhabene Platten
    in zwei Hoehen (schachbrettartig)."""
    c = Vector(c)
    sg = -1 if down else 1
    b.box(sx, sy, 0.04, M("HullDark"), loc=c + V(0, 0, sg * 0.01), rot=(0, 0, rot))
    px, py = sx / nx, sy / ny
    for i in range(nx):
        for j in range(ny):
            o = _rotz(V((i - (nx - 1) / 2) * px, (j - (ny - 1) / 2) * py, 0), rot)
            hh = h * (1.6 if (i + j) % 2 else 1.0)
            b.box(px - gap, py - gap, hh, M(mat), loc=c + o + V(0, 0, sg * hh / 2), rot=(0, 0, rot))


def radial_seams(b, r0, r1, z, n, start=0.0, w=0.09, h=0.05):
    """Radiale Plattenfugen auf einer Ringflaeche."""
    for k in range(n):
        a = start + 360.0 * k / n
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        b.box(r1 - r0, w, h, M("HullDark"), loc=d * ((r0 + r1) / 2) + V(0, 0, z), rot=(0, 0, a))


def accent_arcs(b, R, width, z, n=4, arc=40.0, start=0.0, h=0.08):
    """Grosse, von oben lesbare Accent-Bogenfelder auf einer Ringflaeche."""
    for k in range(n):
        b.ring(R, width, h, M("Accent"), seg=10, loc=(0, 0, z), arc=arc, start=start + 360.0 * k / n - arc / 2)


def pipe_bundle(b, p1, p2, n=3, r=0.12, sp=0.32, up=(0, 0, 1), mat="Steel"):
    p1, p2 = Vector(p1), Vector(p2)
    d = (p2 - p1).normalized()
    s = d.cross(Vector(up)).normalized()
    for k in range(n):
        o = s * ((k - (n - 1) / 2) * sp)
        b.strut(p1 + o, p2 + o, r, M(mat if k % 2 == 0 else "HullDark"), seg=5)


def portal(b, c, span, h, yaw, w=0.35, mat="Hazard", trolley=True):
    """Portalkran/Roboterportal: zwei Stuetzen und ein Querbalken quer zur Richtung yaw, mit Laufkatze."""
    c = Vector(c)
    n = _rotz(V(0, 1, 0), yaw)
    for s in (1, -1):
        b.box(w, w, h, M(mat), loc=c + n * (s * span / 2) + V(0, 0, h / 2), rot=(0, 0, yaw))
    b.box(w, span + w, w, M(mat), loc=c + V(0, 0, h), rot=(0, 0, yaw))
    if trolley:
        b.box(w * 2.2, w * 1.8, w * 1.4, M("HullDark"), loc=c + n * (span * 0.15) + V(0, 0, h - w * 0.4),
              rot=(0, 0, yaw))
        b.strut(c + n * (span * 0.15) + V(0, 0, h - w), c + n * (span * 0.15) + V(0, 0, h * 0.45), 0.05,
                M("HullDark"), seg=4)


# =============================================================== L-Stationen (zweite Detailrunde)
@model("colony", "Wohnrad: geschlossener Habitatring mit drei Fensterreihen, Glaskuppeln und gruenen Parkdecks im Wechsel auf dem Ring, radiale Plattenfugen, grosse Accent-Bogenfelder oben und unten, vier Speichen mit Aufzugsschaechten und Kabinen, gestufte Nabe mit Fensterband, Kuppel und Andockring, vier Radiatoren zwischen den Speichen")
def colony(b):
    R = 8.6
    b.lathe([(R - 1.1, -0.9), (R + 0.9, -0.9), (R + 1.3, -0.45), (R + 1.3, 0.45), (R + 0.9, 0.9), (R - 1.1, 0.9)],
            M("Hull"), seg=32, closed=True)
    for z in (-0.22, 0.2):
        b.window_band(R + 1.32, z, 0.13, n=16, seg=32, gap=0.22)
    b.window_band(R - 1.12, 0.0, 0.32, n=16, seg=32, gap=0.3)
    b.window_band(R + 1.1, 0.68, 0.08, n=8, seg=32, gap=0.5, rot=None)
    radial_seams(b, R - 1.05, R + 0.85, 0.92, 16, start=11.25)
    radial_seams(b, R - 1.05, R + 0.85, -0.92, 16, start=11.25)
    accent_arcs(b, R, 1.5, 0.93, n=4, arc=24, start=45)
    accent_arcs(b, R, 1.5, -0.93, n=4, arc=24, start=45)
    for k in range(8):
        a = 22.5 + 45 * k
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        if k % 2 == 0:
            b.sphere(0.75, M("Glass"), seg=10, rings=4, loc=d * R + V(0, 0, 0.9), half=True)
            b.ring(0.8, 0.12, 0.12, M("HullDark"), seg=10, loc=d * R + V(0, 0, 0.92))
        else:
            b.box(1.25, 2.3, 0.06, M("HullDark"), loc=d * R + V(0, 0, 0.92), rot=(0, 0, a))
            b.box(1.05, 2.1, 0.08, M("Plant"), loc=d * R + V(0, 0, 0.95), rot=(0, 0, a))
    for k in range(4):
        a = 90 * k
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        n = V(-d.y, d.x, 0)
        b.beam(d * 2.4, d * (R - 1.0), 0.9, M("Steel"))
        b.box(R - 3.6, 0.08, 0.5, M("Window"), loc=d * (R / 2 + 0.7) + n * 0.46, rot=(0, 0, a))
        b.box(R - 3.6, 0.08, 0.5, M("Window"), loc=d * (R / 2 + 0.7) - n * 0.46, rot=(0, 0, a))
        for sz in (1, -1):
            b.strut(d * 2.4 + V(0, 0, sz * 0.75), d * (R - 1.0) + V(0, 0, sz * 0.75), 0.28, M("Hull"), seg=6)
            b.box(0.7, 0.62, 0.62, M("Accent"), loc=d * (3.6 if sz > 0 else 6.2) + V(0, 0, sz * 0.75),
                  rot=(0, 0, a))
    for k in range(4):
        a = 45 + 90 * k
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        b.radiator(d * 2.5, d, 3.6, 1.3, tilt=0, mat="Steel", stripe="Glow", n=2)
    b.lathe([(0, -4.6), (0.9, -4.2), (1.4, -3.2), (2.0, -2.6), (2.4, -1.4), (2.6, -1.0), (2.6, 1.0), (2.3, 1.3),
             (1.9, 1.5)], M("Hull"), seg=16)
    b.window_band(2.62, 0.25, 0.22, n=8, seg=16, gap=0.25)
    b.window_band(2.62, -0.35, 0.22, n=8, seg=16, gap=0.25)
    b.armor_belt(2.5, -1.9, 0.7, 8, mat="Armor", tilt=-30)
    b.sphere(1.75, M("Glass"), seg=16, rings=8, loc=(0, 0, 1.5), half=True)
    b.ring(1.95, 0.35, 0.4, M("Accent"), seg=16, loc=(0, 0, 1.5))
    for x, y, a in polar(4, 1.8, 45):
        b.beam(V(x, y, 1.5), V(x * 0.25, y * 0.25, 3.15), 0.12, M("HullDark"))
    b.ring(1.1, 0.25, 0.3, M("Light"), seg=12, loc=(0, 0, -4.5))
    b.cyl(0.55, 1.0, M("HullDark"), seg=10, loc=(0, 0, -5.0))
    for x, y, a in polar(4, 2.62, 45):
        b.hangar_slit(V(x, y, -0.05), (x, y, 0), w=0.9, h=0.4)


@model("company_seat", "Repraesentativer Firmensitz: gegliederte Spindel mit Stufen und Fensterbaendern, drei verglaste Ringdecks mit je zwei Fensterreihen, Glasatrium mit Rippen zwischen den Decks, Dachgarten mit Glasdach und Sprossen, Accent-Bogenfelder und Fugen auf dem Hauptdeck, geneigter Logoring, drei Landeplattformen an Auslegern, Hangarschlitze")
def company_seat(b):
    b.lathe([(0, -9.6), (0.55, -8.8), (0.9, -8.0), (1.2, -7.6), (1.2, -5.2), (1.45, -4.9), (1.45, 4.9),
             (1.15, 5.4), (1.15, 7.0), (0.8, 7.5), (0.55, 9.2), (0.15, 11.0), (0, 11.2)], M("Hull"), seg=12)
    for z in (-6.4, 6.2):
        b.window_band(1.22 if z < 0 else 1.17, z, 0.5, n=6, seg=12, gap=0.3)
    for z, R, h in ((0.0, 7.0, 1.8), (4.2, 4.3, 1.3), (-4.0, 3.7, 1.2)):
        b.lathe([(1.4, z - h / 2), (R - 0.45, z - h / 2), (R, z - h * 0.25), (R, z + h * 0.25),
                 (R - 0.45, z + h / 2), (1.4, z + h / 2)], M("Hull"), seg=16, closed=True)
        for o in (-0.13, 0.13):
            b.window_band(R + 0.03, z + o * h, h * 0.1, mat="Window", n=8, seg=16, gap=0.2)
        b.ring(R + 0.1, 0.2, 0.08, M("Accent"), seg=16, loc=(0, 0, z + h * 0.27))
        radial_seams(b, 1.8, R - 0.5, z + h / 2 + 0.02, 8 if R < 5 else 12, start=15)
    top = 0.9
    b.lathe([(1.5, top + 0.02), (4.4, top + 0.02)], M("Glass"), seg=16)
    radial_seams(b, 1.5, 4.4, top + 0.05, 12, start=0, w=0.12)
    b.ring(4.55, 0.25, 0.12, M("HullDark"), seg=16, loc=(0, 0, top + 0.04))
    accent_arcs(b, 5.7, 1.5, top + 0.06, n=4, arc=50, start=45)
    for x, y, a in polar(6, 3.0, 30):
        b.box(0.7, 0.7, 0.12, M("Plant"), loc=(x, y, top + 0.06), rot=(0, 0, a))
    for z0, z1 in ((0.9, 3.55), (-0.9, -3.4)):
        b.cyl(1.6, abs(z1 - z0), M("Glass"), seg=12, loc=(0, 0, (z0 + z1) / 2))
        for x, y, a in polar(6, 1.62, 0):
            b.box(0.14, 0.14, abs(z1 - z0), M("HullDark"), loc=(x, y, (z0 + z1) / 2), rot=(0, 0, a))
    b.torus(5.6, 0.28, M("Accent"), seg=20, minor=5, loc=(0, 0, 2.2), rot=(18, 0, 0))
    for a in (-50, 70, 190):
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        b.beam(d * 6.8, d * 9.6, 0.7, M("Steel"))
        b.box(2.8, 0.08, 0.3, M("Accent"), loc=d * 8.2 + V(0, 0, 0.38), rot=(0, 0, a))
        b.cyl(1.9, 0.3, M("Hull"), seg=12, loc=d * 11.2)
        b.ring(1.85, 0.22, 0.32, M("Hazard"), seg=12, loc=d * 11.2)
        b.box(1.2, 0.18, 0.04, M("HullDark"), loc=d * 11.2 + V(0, 0, 0.17), rot=(0, 0, a))
        b.box(0.18, 1.2, 0.04, M("HullDark"), loc=d * 11.2 + V(0, 0, 0.17), rot=(0, 0, a))
        b.nav_lights([d * 11.2 + _rotz(V(1.5, 0, 0.2), a + q) for q in (90, 270)], s=0.14)
    for a in (10, 130, 250):
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        b.hangar_slit(d * 7.05, d, w=1.8, h=0.5)
    b.dock(V(0, 0, -9.0), (0, 0, -1), r=0.45, L=1.0)


@model("drive_works", "Triebwerks-Pruefwerk: Reaktorkern unter einem gepanzerten Kragen mit Plasmafuge und Accent-Band, drei geschlossene Pruefstaende (Bodenplatte, Seitenwaende mit Fensterband und Accent-Feld, Dachtraeger) mit je einem eingespannten Triebwerk, gluehender Duese und Schubfahne, Versorgungsleitungen zum Kern, Radiatoren zwischen den Staenden, Technikgraben auf dem Kern")
def drive_works(b):
    b.sphere(2.4, M("Hull"), seg=12, rings=8)
    b.lathe([(2.1, -1.0), (3.5, -0.6), (3.6, 0.0), (3.5, 0.6), (2.1, 1.0)], M("Armor"), seg=12, closed=True)
    b.torus(3.62, 0.14, M("Plasma"), seg=12, minor=4)
    b.ring(3.1, 0.55, 0.06, M("Accent"), seg=12, loc=(0, 0, 0.82))
    b.ring(3.1, 0.55, 0.06, M("Accent"), seg=12, loc=(0, 0, -0.82))
    b.lathe([(0, 2.1), (1.6, 2.1), (1.8, 1.7)], M("Steel"), seg=12)
    plate_field(b, (0, 0, 2.12), 2.0, 1.0, 2, 1, mat="Steel")
    greebles(b, 0, 0, 2.2, 1.6, 0.8, n=4, seed=31.0)
    b.cyl(0.7, 1.6, M("HullDark"), seg=10, loc=(0, 0, -3.0))
    b.ring(0.75, 0.15, 0.3, M("Light"), seg=10, loc=(0, 0, -3.7))
    for k in range(3):
        a = 90 + 120 * k
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        n = V(-d.y, d.x, 0)
        c = d * 7.0
        b.box(7.4, 4.0, 0.6, M("Hull"), loc=c + V(0, 0, -1.9), rot=(0, 0, a))
        plate_field(b, c + V(0, 0, -2.2), 6.6, 3.4, 3, 2, rot=a, mat="Steel", down=True)
        for s in (1, -1):
            w = c + n * (s * 1.95)
            b.box(6.6, 0.45, 3.4, M("Hull"), loc=w, rot=(0, 0, a))
            b.box(5.6, 0.06, 0.9, M("Accent"), loc=w + n * (s * 0.24) + V(0, 0, 0.5), rot=(0, 0, a))
            b.box(5.0, 0.07, 0.18, M("Window"), loc=w + n * (s * 0.25) - V(0, 0, 0.45), rot=(0, 0, a))
            vents(b, w + V(0, 0, 1.7) - d * 1.8, n=3, length=0.4)
        for t in (-2.2, 0.0, 2.2):
            b.box(0.4, 4.3, 0.4, M("Steel"), loc=c + d * t + V(0, 0, 1.75), rot=(0, 0, a))
        b.box(6.8, 0.5, 0.12, M("Accent"), loc=c + V(0, 0, 1.97), rot=(0, 0, a))
        rot = (0, 90, a)
        b.cyl(1.05, 3.6, M("Steel"), seg=10, loc=d * 5.6, rot=rot)
        for t in (4.6, 6.4):
            b.ring(1.12, 0.22, 0.32, M("HullDark"), seg=10, loc=d * t, rot=rot)
        b.lathe([(0.8, 0), (1.0, 0.4), (1.5, 1.8), (1.7, 2.6), (1.55, 2.6), (1.3, 1.8), (0.8, 0.6)],
                M("HullDark"), seg=12, loc=d * 7.4, rot=rot, closed=True)
        b.lathe([(0, 2.3), (1.45, 2.3)], M("Engine"), seg=12, loc=d * 7.4, rot=rot)
        b.lathe([(1.4, 2.55), (0.9, 3.6), (0, 5.0)], M("Plasma"), seg=10, loc=d * 7.4, rot=rot)
        pipe_bundle(b, d * 3.3 + V(0, 0, 1.0), d * 4.2 + V(0, 0, 1.1), n=3, r=0.12)
        pipe_bundle(b, d * 3.3 - V(0, 0, 0.9), d * 4.2 - V(0, 0, 1.0), n=2, r=0.14)
    for k in range(3):
        a = 30 + 120 * k
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        b.radiator(d * 3.5, d, 5.2, 1.6, tilt=0, mat="Steel", stripe="Glow", n=3)


@model("weapons_works", "Waffen- und Schildwerk: lange helle Fertigungshalle mit gestuftem Plattendach, Oberlichtband und Accent-Streifen, Leitstand obenauf, Hangarschlitze an den Flanken, drei gepanzerte Munitionsbunker mit Warnstreifen und Explosionstoren an Foerderbruecken, langer Erprobungsausleger mit Zielring, zwei Schildprojektoren, Radiatoren, Andockschleuse")
def weapons_works(b):
    L, W, H = 12.0, 5.0, 3.2
    cx = -1.0
    b.box(L, W, H, M("Hull"), loc=(cx, 0, 0), bevel=0.3)
    b.box(L - 1.0, W - 1.0, 0.5, M("Steel"), loc=(cx, 0, H / 2 + 0.2), bevel=0.1)
    plate_field(b, (cx - 2.4, 0, H / 2 + 0.45), 5.6, 3.6, 4, 2, mat="Hull")
    b.box(5.0, 0.6, 0.08, M("Window"), loc=(cx + 3.0, 0.0, H / 2 + 0.48))
    for s in (1, -1):
        b.box(4.8, 0.3, 0.08, M("Accent"), loc=(cx + 3.0, s * 1.2, H / 2 + 0.48))
        b.box(L - 0.8, 0.06, 0.7, M("Accent"), loc=(cx, s * (W / 2 + 0.01), 0.75))
        b.box(L - 1.4, 0.07, 0.16, M("Window"), loc=(cx, s * (W / 2 + 0.02), -0.15))
        for x in (-5.0, -1.0, 3.0):
            b.box(0.06, 0.07, H * 0.85, M("HullDark"), loc=(cx + x + 1.0, s * (W / 2 + 0.02), 0))
    for x in (-4.5, -0.5):
        b.hangar_slit(V(cx + x, -W / 2 - 0.05, -0.9), (0, -1, 0), w=2.2, h=0.7)
    b.box(3.6, 2.6, 1.3, M("Armor"), loc=(cx - 3.6, 0, H / 2 + 1.05), bevel=0.15)
    b.box(3.62, 2.0, 0.22, M("Window"), loc=(cx - 3.6, 0, H / 2 + 1.25))
    b.mast((cx - 3.0, 0.6, H / 2 + 1.7), 1.6)
    plate_field(b, (cx, 0, -H / 2 - 0.02), 10.0, 3.6, 4, 2, mat="Steel", down=True)
    for k, x in enumerate((-5.2, -1.4, 2.4)):
        c = V(cx + x + 1.0, 5.4, -0.4)
        b.box(2.6, 2.2, 1.8, M("Armor"), loc=c, bevel=0.2)
        b.box(2.0, 1.6, 0.4, M("Armor"), loc=c + V(0, 0, 1.05), bevel=0.1)
        b.box(2.62, 0.4, 0.08, M("Hazard"), loc=c + V(0, 0.5, 1.26))
        b.box(2.62, 0.4, 0.08, M("Hazard"), loc=c + V(0, -0.5, 1.26))
        b.box(1.6, 0.08, 1.2, M("HullDark"), loc=c + V(0, 1.12, -0.1))
        b.box(1.7, 0.1, 0.1, M("LightRed"), loc=c + V(0, 1.14, 0.6))
        b.beam(V(c.x, W / 2, c.z), V(c.x, c.y - 1.1, c.z), 0.6, M("Steel"))
    b.truss(V(cx + L / 2, 0, 0), V(14.0, 0, 0), 0.6, 0.16, M("Hull"), n=6)
    b.box(1.4, 1.6, 1.6, M("Steel"), loc=(cx + L / 2 + 0.6, 0, 0), bevel=0.1)
    b.ring(2.0, 0.4, 0.4, M("Hazard"), seg=14, loc=(14.4, 0, 0), rot=(0, 90, 0))
    b.ring(1.0, 0.25, 0.42, M("LightRed"), seg=10, loc=(14.4, 0, 0), rot=(0, 90, 0))
    for z in (1, -1):
        p = V(cx + 3.0, -1.4, z * (H / 2 + (0.45 if z > 0 else 0.05)))
        b.cyl(0.9, 0.5, M("Hull"), seg=10, loc=p)
        b.sphere(1.2, M("Light"), seg=10, rings=6, loc=p + V(0, 0, z * 0.25), half=True,
                 scale=(1, 1, 0.5), rot=None if z > 0 else (180, 0, 0))
        b.ring(1.3, 0.25, 0.25, M("HullDark"), seg=10, loc=p + V(0, 0, z * 0.25))
    for x in (-4.0, 1.5):
        b.radiator(V(cx + x, -W / 2, 1.2), (0, -1, 0), 3.4, 1.6, tilt=0, mat="Steel", stripe="Glow", n=2)
    b.dock(V(cx - L / 2, 0, 0), (-1, 0, 0))
    b.nav_lights([(14.4, 0, 2.3)], mat="LightRed", s=0.22)
    b.insignia(V(cx - 1.0, -W / 2 - 0.05, 0.75), (0, -1, 0), s=0.7)


@model("systems_works", "Systemwerk: zusammenhaengende Montagehalle mit gestuftem Dach und offenem Montagebett, drei Roboterportale mit Greifarmen ueber dem Bett, Modulregal als Gitterregal mit farbigen Modulen an der Flanke, Buero- und Sensoranbau mit Schuessel und Antennen, Andockbuchten mit Leuchtrahmen an beiden Enden, Accent-Streifen")
def systems_works(b):
    L, W, H = 11.0, 5.0, 3.0
    b.box(L, W, H, M("Hull"), bevel=0.3)
    for s in (1, -1):
        b.box(L - 0.6, 1.1, 0.4, M("Steel"), loc=(0, s * (W / 2 - 0.7), H / 2 + 0.2), bevel=0.08)
        b.box(L - 1.0, 0.35, 0.08, M("Accent"), loc=(0, s * (W / 2 - 0.7), H / 2 + 0.42))
        b.box(L - 1.0, 0.06, 0.6, M("Accent"), loc=(0, s * (W / 2 + 0.01), 0.7))
        b.box(L - 1.6, 0.07, 0.14, M("Window"), loc=(0, s * (W / 2 + 0.02), -0.2))
    b.box(L - 1.4, 2.0, 0.2, M("HullDark"), loc=(0, 0, H / 2 + 0.02))
    for x, m in ((-3.4, "Accent"), (0.0, "Steel"), (3.4, "Container")):
        b.box(1.3, 1.1, 0.9, M(m), loc=(x, 0, H / 2 + 0.55), bevel=0.08)
        b.box(1.32, 0.3, 0.12, M("Light"), loc=(x, 0, H / 2 + 0.75))
    for x in (-3.4, 0.0, 3.4):
        portal(b, V(x + 0.9, 0, H / 2 + 0.4), W - 0.6, 2.2, 0, w=0.32)
        j = V(x + 0.9, 0.0, H / 2 + 2.4)
        b.sphere(0.25, M("HullDark"), seg=6, rings=4, loc=j)
        b.beam(j, V(x + 0.3, 0.0, H / 2 + 1.5), 0.18, M("Steel"))
        b.beam(V(x + 0.3, 0.0, H / 2 + 1.5), V(x, 0.0, H / 2 + 1.1), 0.12, M("HullDark"))
    plate_field(b, (0, 0, -H / 2 - 0.02), 9.6, 3.6, 4, 2, mat="Steel", down=True)
    # Modulregal an der +y-Flanke
    ry = W / 2 + 1.1
    for x in (-4.5, -1.5, 1.5, 4.5):
        b.box(0.22, 1.6, 3.4, M("Steel"), loc=(x, ry, 0))
    for z in (-1.6, 0.0, 1.6):
        b.box(9.2, 1.7, 0.18, M("Steel"), loc=(0, ry, z))
    b.box(9.2, 0.1, 0.12, M("Accent"), loc=(0, ry + 0.86, 1.6))
    mods = [(-3.0, -0.8, "Accent"), (0.0, -0.8, "Container"), (3.0, -0.8, "Hull"),
            (-3.0, 0.8, "HullDark"), (0.0, 0.8, "Accent"), (3.0, 0.8, "Container")]
    for x, z, m in mods:
        b.box(1.4, 1.1, 1.2, M(m), loc=(x - 0.6, ry, z), bevel=0.06)
        b.box(1.0, 1.12, 0.2, M("Light"), loc=(x + 0.6, ry, z + 0.3))
    # Buero-/Sensoranbau an der -y-Flanke
    b.box(4.4, 2.0, 2.0, M("Armor"), loc=(-2.0, -W / 2 - 0.9, 0.0), bevel=0.15)
    b.box(4.42, 0.1, 0.3, M("Window"), loc=(-2.0, -W / 2 - 1.92, 0.3))
    b.dish((-2.6, -W / 2 - 1.0, 1.7), 1.3, M("Hull"), rot=(-25, 0, 0))
    b.mast((-0.6, -W / 2 - 1.2, 1.0), 2.4)
    b.mast((-1.0, -W / 2 - 0.7, 1.0), 1.6, light="Light")
    b.radiator(V(2.5, -W / 2, 0.0), (0, -1, 0), 3.6, 1.5, tilt=0, mat="Steel", stripe="Glow", n=2)
    for s in (1, -1):
        b.box(0.3, 2.6, 1.4, M("HullDark"), loc=(s * (L / 2 + 0.05), 0, -0.3))
        for z in (0.45, -1.05):
            b.box(0.34, 2.8, 0.1, M("Light"), loc=(s * (L / 2 + 0.06), 0, z))
        b.dock(V(s * (L / 2), 0, 0.9), (s, 0, 0), r=0.5, L=1.2)
    b.insignia(V(3.4, -W / 2 - 0.05, 0.7), (0, -1, 0), s=0.6)


@model("warehouse", "Lagerhaus mit drei Trakten um einen Kontrollturm: Kuehltrakt (isolierte Halle mit Rippen und Frostband), Fluessigtrakt (zwei Liegetanks mit Warnringen und Rohrbuendeln), Stueckguttrakt (Containerdeck mit gestapelten Containern), anliegende Portalkrane ueber Kuehl- und Stueckguttrakt, Kontrollturm mit verglaster Kanzel, Schuessel und Antennen, Accent-Felder auf jedem Trakt")
def warehouse(b):
    b.lathe([(0, -6.0), (0.8, -5.6), (1.1, -4.0), (1.1, 3.0), (1.8, 3.4), (2.0, 4.2), (1.8, 5.0), (0.9, 5.4),
             (0.4, 6.6), (0, 6.8)], M("Hull"), seg=12)
    b.window_band(2.02, 4.2, 0.4, n=8, seg=12, gap=0.2)
    b.ring(1.85, 0.4, 0.08, M("Accent"), seg=12, loc=(0, 0, 5.02))
    b.dish((0.9, 0.0, 5.3), 0.8, M("Hull"), rot=(-30, 0, 90))
    b.ring(1.0, 0.3, 0.4, M("Light"), seg=10, loc=(0, 0, -5.4))
    tracts = (90, 210, 330)
    for k, a in enumerate(tracts):
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        n = V(-d.y, d.x, 0)
        b.beam(d * 1.0, d * 3.0, 1.4, M("Steel"))
        c = d * 6.4
        if k == 0:                      # Kuehltrakt
            b.box(7.0, 3.6, 3.4, M("Hull"), loc=c, rot=(0, 0, a), bevel=0.25)
            for t in (-2.6, -1.3, 0.0, 1.3, 2.6):
                b.box(0.18, 3.7, 3.5, M("Ice"), loc=c + d * t, rot=(0, 0, a))
            for s in (1, -1):
                b.box(6.6, 0.08, 0.22, M("Window"), loc=c + n * (s * 1.82) + V(0, 0, 0.9), rot=(0, 0, a))
            b.box(6.0, 1.2, 0.08, M("Accent"), loc=c + V(0, 0, 1.75), rot=(0, 0, a))
            portal(b, c + d * 1.9 - V(0, 0, 1.7), 4.4, 4.0, a, w=0.32)
        elif k == 1:                    # Fluessigtrakt
            b.box(7.2, 4.0, 0.6, M("Steel"), loc=c - V(0, 0, 0.6), rot=(0, 0, a))
            for s in (1, -1):
                p = c + n * (s * 1.0) + V(0, 0, 0.6)
                b.capsule(p, d, 6.0, 0.95, M("Hull"), seg=10)
                for t in (-1.6, 0.0, 1.6):
                    b.ring(0.98, 0.2, 0.3, M("Hazard"), seg=10, loc=p + d * t, rot=(0, 90, a))
                b.box(4.8, 0.3, 0.08, M("Accent"), loc=p + V(0, 0, 0.96), rot=(0, 0, a))
            pipe_bundle(b, d * 2.4 + V(0, 0, 1.5), d * 4.0 + V(0, 0, 1.6), n=3, r=0.13)
            pipe_bundle(b, d * 2.4 - V(0, 0, 0.6), d * 4.0 - V(0, 0, 0.6), n=2, r=0.15)
        else:                           # Stueckguttrakt
            b.box(7.4, 4.2, 0.6, M("Steel"), loc=c - V(0, 0, 0.9), rot=(0, 0, a))
            b.box(7.0, 0.5, 0.08, M("Accent"), loc=c + n * 1.85 - V(0, 0, 0.58), rot=(0, 0, a))
            cols = ("Container", "Hull", "Container", "Accent")
            i = 0
            for t in (-2.4, -0.8, 0.8, 2.4):
                for s in (1, -1):
                    hgt = 2 if (i % 3) else 1
                    for lv in range(hgt):
                        b.box(1.4, 1.5, 1.0, M(cols[(i + lv) % 4]), loc=c + d * t + n * (s * 0.85) +
                              V(0, 0, -0.1 + lv * 1.02), rot=(0, 0, a))
                    i += 1
            portal(b, c - d * 1.6 - V(0, 0, 1.2), 4.8, 3.6, a, w=0.32)
            portal(b, c + d * 1.6 - V(0, 0, 1.2), 4.8, 3.6, a, w=0.32, trolley=False)
        b.nav_lights([c + d * 3.8 + V(0, 0, 0.8)], mat="LightRed" if k % 2 else "Light", s=0.2)
    for a in (30, 150, 270):
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        b.radiator(d * 1.1, d, 3.2, 1.2, tilt=0, mat="Steel", stripe="Glow", n=2)
