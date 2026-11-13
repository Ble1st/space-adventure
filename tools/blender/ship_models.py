"""Ein prozedurales Modell je Schiffsmodell (Key = shipModels[].key in default-world.json,
ohne unit_player) plus die kommenden Kampfschiffe corvette und missile_boat.

Gestaltungsregeln
- Vollstaendig geschlossene Raumschiffe: ein durchgehender Rumpf (Builder.loft aus Querschnitten),
  oben und unten gestaltet, kein Deck, keine aufgesetzten Bruecken, nichts offen.
- Rumpffamilien (shape): Sechskant-Frachter, Zigarre, Linse, Raute, Ei, Katamaran/Gondeln,
  Pfeil mit Triebwerksgondeln, Hammerkopf, schwerer Kasten, Stufenkeil, Asymmetrisch, Tankspindel.
- Frachtraum sichtbar zur Kapazitaet: je CARGO_PER_SEGMENT (20) Einheiten ein Frachtsegment der festen
  Laenge SEG (in allen Groessenklassen gleich, die Klassen haben feste Baulaengen S 10, M 15, L 26),
  jedes Segment mit Ladetor/Klappe und Spant. Tanker: ein Druckkoerper je Segment.
- Bis zu zehn Materialien je Schiff, Positionslichter backbord rot, steuerbord blau.

Achsen beim Bauen (Blender): Bug = +Y, oben = +Z, Steuerbord = +X. Nach glTF-Export (Y-up)
zeigt der Bug in Godot nach -Z (Godot-Vorwaerts), oben nach +Y.
Builder.finish(normalize="length") zentriert die Bounding-Box und normiert die Laenge auf 2.
"""

import math
from mathutils import Vector

import bmesh
from mathutils import Euler, Matrix, Quaternion

from stationkit import Builder, material as _material, polar, qdir

# Schiffe nutzen den hellen Rumpfsatz statt der dunklen Stationsfarben.
SHIPMAP = {"Hull": "ShipHull", "Steel": "ShipPanel", "Armor": "ShipArmor"}


def M(name):
    return _material(SHIPMAP.get(name, name))

SHIPS = {}
SIZE_SCALE = {"S": 1.0, "M": 1.5, "L": 2.4, "XL": 3.6}
CARGO_PER_SEGMENT = 20
SEG = 0.8          # Laenge eines Frachtsegments in Baueinheiten
CARGO = {}         # key -> (Kapazitaet, sichtbare Segmente), fuer Bericht und Pruefung


ROLE = {}        # key -> "Handel" | "Bergbau/Bau" | "Kampf"


def ship(key, size, idea, name=None, cargo=0, role=None):
    def deco(fn):
        SHIPS[key] = (fn, size, idea, name)
        ROLE[key] = role or ("Kampf" if cargo == 0 else "Handel")
        CARGO[key] = (cargo, round(cargo / CARGO_PER_SEGMENT))
        return fn
    return deco


MAX_MATERIALS = 10
MERGE = (("Hull", "HullDark"), ("Hazard", "Accent"), ("ShipArmor", "ShipPanel"), ("Glass", "Window"),
         ("Rust", "ShipPanel"), ("Copper", "ShipPanel"), ("Glow", "Engine"), ("Steel", "ShipPanel"),
         ("Window", "Light"))


def merge_material(b, src, dst):
    names = [m.name for m in b.mats]
    if src not in names:
        return
    if dst not in names:
        b.mats.append(M(dst))
        names.append(dst)
    si, di = names.index(src), names.index(dst)
    for f in b.bm.faces:
        if f.material_index == si:
            f.material_index = di
    for f in b.bm.faces:
        if f.material_index > si:
            f.material_index -= 1
    del b.mats[si]


# Verbreiterung je Schiff (Durchlauf 5): Rumpf-Lofts werden in X gestreckt, alle anderen Bauteile
# behalten ihre Form und wandern mit ihrem Schwerpunkt nach aussen.
WIDEN_DEFAULT = 1.15
WIDEN = {
    "unit_trader_100": 1.322,
    "unit_trader_60": 1.552,
    "unit_miner_100": 1.150,
    "unit_miner_60": 1.495,
    "pip": 1.150,
    "nomad": 1.380,
    "prospector": 1.150,
    "swift": 1.155,
    "pickaxe": 1.150,
    "corsair": 1.150,
    "falcon": 1.150,
    "mule": 1.380,
    "hauler": 1.438,
    "tinker": 1.150,
    "cistern": 1.552,
    "behemoth": 1.725,
    "rockbreaker": 1.380,
    "excavator": 1.265,
    "foreman": 1.667,
    "quarry": 1.667,
    "rockbreaker_ii": 1.150,
    "warden": 1.150,
    "corvette": 1.380,
    "missile_boat": 1.050,
    "bastion": 1.265,
    "bomber": 1.150,
    "gunboat": 1.322,
    "destroyer": 1.208,
    "battlecruiser": 1.265,
    "strip_miner": 1.495,
}

# Abbauart und bewegliche Werkzeuge (Durchlauf 5)
MINING = {"unit_miner_100": "contact", "unit_miner_60": "contact", "prospector": "contact", "pickaxe": "contact",
          "rockbreaker": "contact", "excavator": "contact", "strip_miner": "contact", "quarry": "contact",
          "rockbreaker_ii": "contact", "pip": "laser", "tinker": "laser"}
ROTOR_INFO = {}     # key -> {"rotors": [...], "jaws": [...], "contactRadius": r}


def rotor(b, name, center, axis, kind="rotor", spin=""):
    """Eigenes Teilobjekt (eigener glTF-Node). Geometrie wird in Schiffskoordinaten gebaut;
    beim Abschluss liegt der Node-Ursprung auf center und seine lokale Z-Achse auf axis."""
    rb = Builder(name)
    rb.center, rb.axis, rb.kind, rb.spin = Vector(center), Vector(axis).normalized(), kind, spin
    if not hasattr(b, "parts"):
        b.parts = []
    b.parts.append(rb)
    return rb


def _islands(bm):
    seen = set()
    for v in bm.verts:
        if v in seen:
            continue
        stack, isl = [v], []
        seen.add(v)
        while stack:
            u = stack.pop()
            isl.append(u)
            for e in u.link_edges:
                w = e.other_vert(u)
                if w not in seen:
                    seen.add(w)
                    stack.append(w)
        yield isl


def widen(b, f, parts=()):
    if abs(f - 1.0) < 1e-6:
        return
    lv = getattr(b, "loft_verts", set())
    for isl in _islands(b.bm):
        if isl[0] in lv:
            for v in isl:
                v.co.x *= f
        else:
            cx = sum(v.co.x for v in isl) / len(isl)
            for v in isl:
                v.co.x += cx * (f - 1)
    for rb in parts:
        dx = rb.center.x * (f - 1)
        rb.center.x += dx
        for v in rb.bm.verts:
            v.co.x += dx


def build(key):
    fn = SHIPS[key][0]
    b = Builder(key)
    b.hardpoints = []
    fn(b)
    parts = getattr(b, "parts", [])
    f = WIDEN.get(key, WIDEN_DEFAULT)
    widen(b, f, parts)
    for hp in b.hardpoints:
        hp["pos"] = hp["pos"] + Vector((hp["ax"] * (f - 1), 0, 0))
    for src, dst in MERGE:
        if len(b.mats) <= MAX_MATERIALS:
            break
        merge_material(b, src, dst)
    # Teil-Nodes nutzen nur Materialien, die der Rumpf auch hat (gemeinsame Grenze von 10)
    hull_names = {m.name for m in b.mats}
    for rb in parts:
        for src, dst in MERGE:
            if src not in hull_names and src in {m.name for m in rb.mats}:
                merge_material(rb, src, dst)
    b.name = "Hull"
    # gemeinsame Normierung: Laenge (inkl. Teile) = 2, Bounding-Box-Mitte = Ursprung
    co = [v.co.copy() for v in b.bm.verts] + [v.co.copy() for rb in parts for v in rb.bm.verts]
    lo = Vector((min(c.x for c in co), min(c.y for c in co), min(c.z for c in co)))
    hi = Vector((max(c.x for c in co), max(c.y for c in co), max(c.z for c in co)))
    c0 = (lo + hi) / 2
    k = 2.0 / (hi.y - lo.y)
    for v in b.bm.verts:
        v.co = (v.co - c0) * k
    ob = b.finish(normalize="none")
    ob.name = "Hull"
    info = {"rotors": [], "jaws": [], "contactRadius": 0.0, "hardpoints": []}
    for rb in parts:
        if rb.kind in ("turret", "pd"):
            # Turm: Blender-lokales +Z (Drehachse) wird nach dem glTF-Export zum lokalen +Y des Nodes,
            # das Rohr (Blender +Y) zeigt in Ruhe nach lokal -Z.
            q = rb.q
        else:
            # Werkzeug: Blender-lokales +Y wird zum lokalen -Z des Nodes (Drehung um lokale Z-Achse).
            q = Vector((0, 1, 0)).rotation_difference(rb.axis)
        qi = q.inverted()
        cen = (rb.center - c0) * k
        rmax = 0.0
        for v in rb.bm.verts:
            w = (v.co - c0) * k
            rmax = max(rmax, math.hypot(w.x - cen.x, w.z - cen.z))
            v.co = qi @ (w - cen)
        po = rb.finish(normalize="none")
        po.name = rb.name
        po.rotation_mode = "QUATERNION"
        po.rotation_quaternion = q
        po.location = cen
        po.parent = ob
        if rb.kind in ("turret", "pd"):
            continue
        entry = {"node": rb.name, "axis": "z", "spin": rb.spin}  # lokale Godot-Z-Achse
        if rb.kind == "jaw":
            info["jaws"].append(entry)
        else:
            info["rotors"].append(entry)
            info["contactRadius"] = round(max(info["contactRadius"], rmax), 3)
    for hp in sorted(b.hardpoints, key=lambda h: h["slot"]):
        pn = (hp["pos"] - c0) * k
        d = hp["dir"].normalized()
        e = {"slot": hp["slot"], "kind": hp["kind"],
             "pos": [round(pn.x, 4), round(pn.z, 4), round(-pn.y, 4)],      # Godot: Bug -Z, oben +Y
             "dir": [round(d.x, 4), round(d.z, 4), round(-d.y, 4)]}
        if hp.get("node"):
            e["node"] = hp["node"]
        if hp.get("empty"):
            e["empty"] = True
        info["hardpoints"].append(e)
    ROTOR_INFO[key] = info
    return ob


# ---------------------------------------------------------------- Geschuetzstellungen (Hardpoints)
def _hp(b, slot, kind, pos, d, ax, node=None, empty=False):
    b.hardpoints.append({"slot": slot, "kind": kind, "pos": Vector(pos), "dir": Vector(d), "ax": ax,
                         "node": node, "empty": empty})


def hp_turret(b, slot, base, s=0.5, twin=False, up=True, side=0, length=1.6, kind="turret", empty=False):
    """Drehturm als eigener Node Turret_<slot>: Ursprung auf der Drehachse (Sockelmitte), Drehachse nach
    Export = lokales +Y, Rohr in Ruhe nach lokal -Z (Bugrichtung). up=False: Bauchturm, side=+-1: Flanke."""
    if side:
        q = Euler((0, math.radians(90 * side), 0)).to_quaternion()
    elif up:
        q = Quaternion()
    else:
        q = Euler((0, math.pi, 0)).to_quaternion()
    name = "Turret_%d" % slot
    rb = Builder(name)
    rb.kind, rb.q, rb.center, rb.axis, rb.spin = kind, q, Vector(base), q @ Vector((0, 0, 1)), ""
    if kind == "pd":
        rb.cyl(0.45 * s, 0.3 * s, M("HullDark"), seg=6, loc=(0, 0, 0.15 * s))
        rb.sphere(0.42 * s, M("Steel"), seg=6, rings=4, loc=(0, 0, 0.3 * s), half=True)
        p0 = V(0, 0.2 * s, 0.55 * s)
        tip = p0 + V(0, length * s, 0)
        rb.strut(p0, tip, 0.06 * s, M("HullDark"), seg=4)
        rb.box(0.14 * s, 0.14 * s, 0.1 * s, M("LightRed"), loc=(0, -0.25 * s, 0.6 * s))
    else:
        rb.cyl(0.78 * s, 0.3 * s, M("HullDark"), seg=8, loc=(0, 0, 0.15 * s))
        hx = [(-0.62, -0.7), (0.62, -0.7), (0.78, 0.15), (0.4, 0.62), (-0.4, 0.62), (-0.78, 0.15)]
        rb.prism([(x * s, y * s) for x, y in hx], 0.5 * s, M("Steel"), loc=(0, 0, 0.55 * s), top_scale=0.82)
        rb.box(0.9 * s, 0.14 * s, 0.05 * s, M("Accent"), loc=(0, -0.38 * s, 0.81 * s))
        zb = 0.55 * s
        if empty:
            tip = V(0, 0.0, 0.81 * s)
            rb.box(0.5 * s, 0.5 * s, 0.06 * s, M("HullDark"), loc=(0, 0, 0.8 * s))
        else:
            for o in ((-0.22, 0.22) if twin else (0.0,)):
                a = V(o * s, 0.45 * s, zb)
                bb = a + V(0, length * s, 0)
                rb.strut(a, bb, 0.09 * s, M("HullDark"), seg=5)
                rb.cyl(0.13 * s, 0.2 * s, M("HullDark"), seg=5, loc=bb - V(0, 0.1 * s, 0), rot=(-90, 0, 0))
            tip = V(0, 0.45 * s + length * s, zb)
    M4 = Matrix.Translation(Vector(base)) @ q.to_matrix().to_4x4()
    bmesh.ops.transform(rb.bm, matrix=M4, verts=rb.bm.verts)
    if not hasattr(b, "parts"):
        b.parts = []
    b.parts.append(rb)
    _hp(b, slot, kind, M4 @ tip, q @ Vector((0, 1, 0)), Vector(base).x, node=name, empty=empty)
    return rb


def hp_gun(b, slot, p, length, r=0.11):
    """Starre Bugwaffe (Rohr nach vorn), Muendung = Rohrende."""
    p = Vector(p)
    gun(b, p, length, r)
    _hp(b, slot, "gun", p + V(0, length, 0), (0, 1, 0), p.x)


def hp_shafts(b, slot, c, rows=2, cols=2, cell=0.34, d=(0, 0, 1)):
    """Raketenschaechte in der Huelle: Schachtfeld mit Rahmen und dunklen Oeffnungen, Austritt nach d
    (Standard: nach oben). Position = Mitte der Oeffnungen."""
    c = Vector(c)
    w, h = cols * cell + 0.16, rows * cell + 0.16
    b.box(w + 0.1, h + 0.1, 0.06, M("Accent"), loc=c)
    b.box(w, h, 0.09, M("Steel"), loc=c + V(0, 0, 0.02))
    for i in range(cols):
        for jj in range(rows):
            o = V((i - (cols - 1) / 2) * cell, (jj - (rows - 1) / 2) * cell, 0.05)
            b.box(cell * 0.72, cell * 0.72, 0.06, M("HullDark"), loc=c + o)
            b.box(cell * 0.3, cell * 0.3, 0.07, M("LightRed"), loc=c + o)
    _hp(b, slot, "missile", c + V(0, 0, 0.085), d, c.x)


def V(x, y, z):
    return Vector((x, y, z))


def segs(key):
    return CARGO[key][1]


# ---------------------------------------------------------------- Rumpf-Hilfen
def interp(secs, y):
    for a, c in zip(secs, secs[1:]):
        if a[0] >= y >= c[0]:
            t = (a[0] - y) / (a[0] - c[0]) if a[0] != c[0] else 0.0
            za = a[3] if len(a) > 3 else 0.0
            zc = c[3] if len(c) > 3 else 0.0
            return a[1] + (c[1] - a[1]) * t, a[2] + (c[2] - a[2]) * t, za + (zc - za) * t
    last = secs[-1] if y < secs[-1][0] else secs[0]
    return last[1], last[2], (last[3] if len(last) > 3 else 0.0)


def band(b, secs, y, mat="HullDark", t=0.22, g=1.05, shape="oct", x=0.0, c=0.35, cap=True):
    """Spant/Panzerkragen an Stelle y."""
    w, h, z = interp(secs, y)
    b.loft([(y + t / 2, w * g, h * g, z), (y - t / 2, w * g, h * g, z)], M(mat), shape=shape, x=x, c=c, cap=cap)


def plate(b, secs, y0, y1, mat="Armor", g=1.04, shape="oct", x=0.0, c=0.35):
    """Panzerplatten-Stufe: aufgedoppelte Rumpfhaut zwischen y0 und y1."""
    w0, h0, z0 = interp(secs, y0)
    w1, h1, z1 = interp(secs, y1)
    b.loft([(y0, w0 * g, h0 * g, z0), (y1, w1 * g, h1 * g, z1)], M(mat), shape=shape, x=x, c=c)


def visor(b, secs, y, width=None, length=0.9, x=0.0, c=0.35, mat="Window"):
    """Eingelassenes Cockpit-Fensterband auf der Oberseite, der Rumpfneigung folgend."""
    w, h, z = interp(secs, y)
    w2, h2, z2 = interp(secs, y - 0.4)
    a = math.degrees(math.atan(((z2 + h2) - (z + h)) / -0.4))
    width = width or w * (1 - c) * 1.6
    b.box(width, length, 0.08, M(mat), loc=(x, y, z + h + 0.01), rot=(a, 0, 0))


def side_hatch(b, x, y, z, ly, lz, mat="Steel", rib=True):
    """Ladetor an der Bordwand: dunkler Rahmen, erhabene Torflaeche, optional eine Querrippe."""
    s = 1 if x > 0 else -1
    b.box(0.06, ly + 0.14, lz + 0.14, M("HullDark"), loc=(x + s * 0.02, y, z))
    b.box(0.1, ly, lz, M(mat), loc=(x + s * 0.04, y, z))
    if rib:
        b.box(0.14, ly * 0.94, 0.06, M("HullDark"), loc=(x + s * 0.06, y, z))


def top_hatch(b, x, y, z, lx, ly, mat="Steel", down=False, rib=True):
    """Ladeklappe oben/unten: dunkler Rahmen, erhabene Flaeche, optional eine Laengsrippe."""
    s = -1 if down else 1
    b.box(lx + 0.14, ly + 0.14, 0.06, M("HullDark"), loc=(x, y, z + s * 0.02))
    b.box(lx, ly, 0.1, M(mat), loc=(x, y, z + s * 0.04))
    if rib:
        b.box(0.06, ly * 0.94, 0.14, M("HullDark"), loc=(x, y, z + s * 0.06))


def cargo_bay(b, secs, y0, n, x=0.0, sides=(1, -1), top=False, bottom=False, c=0.35, mat="Steel", rib=True,
              frames=True, shape="oct"):
    """n Frachtsegmente ab y0 nach achtern: Spant vor/nach jedem Segment (Querschnitt wie der Rumpf),
    Tor je Seite und Segment."""
    for i in range(n + 1 if frames else 0):
        band(b, secs, y0 - SEG * i, t=0.12, g=1.05, x=x, c=c, cap=False, shape=shape)
    for i in range(n):
        yc = y0 - SEG * (i + 0.5)
        w, h, z = interp(secs, yc)
        for s in sides:
            side_hatch(b, x + s * w, yc, z, SEG * 0.72, h * (1 - c) * 1.5, mat, rib=rib)
        if top:
            top_hatch(b, x, yc, z + h, w * (1 - c) * 1.4, SEG * 0.72, mat, rib=rib)
        if bottom:
            top_hatch(b, x, yc, z - h, w * (1 - c) * 1.4, SEG * 0.72, mat, down=True, rib=rib)


def lens(b, p, r=0.3, d=(0, 1, 0)):
    p, d = Vector(p), Vector(d).normalized()
    b.cyl(r * 1.5, r * 0.8, M("HullDark"), seg=8, loc=p, rot=qdir(d))
    b.sphere(r, M("Light"), seg=6, rings=4, loc=p + d * r * 0.4)


def navl(b, x, y, z, s=0.14):
    b.sphere(s, M("LightRed"), seg=5, rings=3, loc=(-x, y, z))
    b.sphere(s, M("Light"), seg=5, rings=3, loc=(x, y, z))


def stripe(b, secs, y0, y1, x=0.0, width=0.35, mat="Accent"):
    for i in range(3):
        ya = y0 + (y1 - y0) * i / 3
        yb = y0 + (y1 - y0) * (i + 1) / 3
        w, h, z = interp(secs, (ya + yb) / 2)
        wa, ha, za = interp(secs, ya)
        wb, hb, zb = interp(secs, yb)
        a = math.degrees(math.atan(((zb + hb) - (za + ha)) / (yb - ya))) if yb != ya else 0
        b.box(width, abs(yb - ya) + 0.05, 0.06, M(mat), loc=(x, (ya + yb) / 2, z + h + 0.02), rot=(a, 0, 0))


def gun(b, p, length, r=0.11):
    p = Vector(p)
    b.box(r * 3.4, length * 0.3, r * 3.2, M("Steel"), loc=p + V(0, length * 0.05, 0))
    b.strut(p, p + V(0, length, 0), r, M("HullDark"), seg=5)
    b.cyl(r * 1.7, length * 0.15, M("HullDark"), seg=5, loc=p + V(0, length * 0.93, 0), rot=(-90, 0, 0))


def fin(b, pts, t, loc, mat="Accent", side=0.0):
    """Senkrechte Finne: pts = [(y_vor, z_hoch), ...] relativ zu loc."""
    b.prism(pts, t, M(mat), loc=loc, rot=(90, side, 90))


def turret(b, p, s=0.5, twin=True, up=True, facing=90):
    b.turret(p, facing=facing, s=s, twin=twin, up=up, hull="HullDark", barrel="Hull", light="LightRed")


def vents(b, p, n=4, length=1.0, side=0, top=True, step=0.22):
    """Kuehlrippen/Lufteinlass: n Rippen auf einer Flaeche (top) oder Bordwand (side = +-1)."""
    p = Vector(p)
    for k in range(n):
        o = (k - (n - 1) / 2) * step
        if side:
            b.box(0.1, 0.08, length, M("HullDark"), loc=p + V(side * 0.03, o, 0))
        else:
            b.box(length, 0.08, 0.1, M("HullDark"), loc=p + V(0, o, 0.03 if top else -0.03))


def sensor(b, p, r=0.32, up=True):
    """Sensorbuckel mit kurzer Antenne."""
    p = Vector(p)
    sg = 1 if up else -1
    b.sphere(r, M("Steel"), seg=6, rings=4, loc=p, half=True, rot=None if up else (180, 0, 0), scale=(1, 1.3, 0.8))
    b.strut(p + V(0, 0, sg * r * 0.6), p + V(0, -0.2, sg * r * 2.4), 0.03, M("HullDark"), seg=4)


def engine(b, p, r, ring="Accent", housing=True):
    """Triebwerk mit Verkleidungsring."""
    p = Vector(p)
    b.nozzle(p, r, housing=housing)
    b.torus(r * 0.86, r * 0.12, M(ring), seg=10, minor=4, loc=p + V(0, r * 0.7, 0), rot=(90, 0, 0))


def thrusters(b, pts, s=0.6):
    for p in pts:
        b.rcs(p, s=s)


def _rand(seed):
    """Deterministischer Zufallsstrom (kein Zufall aus der Umgebung)."""
    r = int(seed * 7919) + 12345
    while True:
        r = (r * 1103515245 + 12345) % 2147483648
        yield r / 2147483648.0


def greebles(b, cx, cy, z, sx, sy, n=8, seed=1.0, up=True, mats=("HullDark", "ShipPanel")):
    """Technikfeld: n kleine Kaesten im Rechteck (cx, cy, sx, sy) auf Hoehe z."""
    rnd = _rand(seed)
    sg = 1 if up else -1
    for i in range(n):
        u, v, a, c, h = next(rnd), next(rnd), next(rnd), next(rnd), next(rnd)
        lx = 0.12 + a * min(0.45, sx * 0.4)
        ly = 0.12 + c * min(0.6, sy * 0.3)
        hh = 0.05 + h * 0.16
        x = cx + (u - 0.5) * (sx - lx)
        y = cy + (v - 0.5) * (sy - ly)
        b.box(lx, ly, hh, M(mats[i % len(mats)]), loc=(x, y, z + sg * hh / 2))


def dress(b, secs, seed=1.0, y0=None, y1=None, x=0.0, shape="oct", c=0.35, trench=True, sides=True,
          accent=True, bottom=True, n=8):
    """Used-universe-Ausstattung eines Rumpfs: dunkler Technikgraben auf dem Ruecken mit Greebles,
    Plattenfugen an den Flanken, kraeftige Accent-Felder vorn an beiden Seiten, Technikfeld unten."""
    y0 = secs[0][0] - (secs[0][0] - secs[-1][0]) * 0.28 if y0 is None else y0
    y1 = secs[-1][0] + (secs[0][0] - secs[-1][0]) * 0.12 if y1 is None else y1
    L = y0 - y1
    ym = (y0 + y1) / 2
    w, h, z = interp(secs, ym)
    flat = {"oct": 1 - c, "hex": 0.55, "wedge": 0.5}.get(shape, 0.35)
    if trench:
        tw = max(0.25, w * flat * 0.55)
        b.box(tw, L * 0.8, 0.05, M("HullDark"), loc=(x, ym, z + h + 0.005))
        greebles(b, x, ym, z + h + 0.03, tw * 0.9, L * 0.75, n=n, seed=seed)
    if shape == "diamond":
        # Rautenrumpf hat keine senkrechte Flanke: Accent als Doppelstreifen auf den Oberflaechen
        if accent:
            ya = y0 - L * 0.12
            wa, ha, za = interp(secs, ya)
            for s in (1, -1):
                ang = math.degrees(math.atan2(ha, wa))
                b.box(wa * 0.7, L * 0.22, 0.05, M("Accent"), loc=(x + s * wa * 0.5, ya, za + ha * 0.5 + 0.02),
                      rot=(0, s * ang, 0))
        sides = accent = False
    if sides:
        for s in (1, -1):
            for k in range(3):
                yk = y0 - L * (k + 1) / 4
                wk, hk, zk = interp(secs, yk)
                b.box(0.04, 0.06, hk * 0.9, M("HullDark"), loc=(x + s * wk * 1.005, yk, zk))
    if accent:
        ya = y0 - L * 0.12
        wa, ha, za = interp(secs, ya)
        for s in (1, -1):
            b.box(0.05, L * 0.22, ha * 0.55, M("Accent"), loc=(x + s * wa * 1.01, ya, za))
            b.box(0.05, L * 0.06, ha * 0.55, M("Accent"), loc=(x + s * wa * 1.01, ya - L * 0.16, za))
    if bottom:
        greebles(b, x, ym, z - h - 0.01, w * flat * 0.8, L * 0.5, n=max(4, n // 2), seed=seed + 3, up=False)


# ---------------------------------------------------------------- Abbauwerkzeuge (eigene Nodes)
def drill(b, name, base, length, r, flutes=2, seg=8, turns=1.5, steps=7):
    """Bohrkegel/Bohrlanze: dreht um die Laengsachse (+Y). Spiralfluegel und ein halber Warnring
    machen die Drehung sichtbar."""
    rb = rotor(b, name, base, (0, 1, 0), spin="laengs (Bugrichtung)")
    base = Vector(base)
    prof = [(r * 0.85, -0.25 * length), (r, 0.0), (r, length * 0.15), (r * 0.72, length * 0.45),
            (r * 0.38, length * 0.75), (0, length)]
    rb.lathe(prof, M("HullDark"), seg=seg, loc=base, rot=(-90, 0, 0))
    rb.lathe([(r * 1.06, -0.2 * length), (r * 1.06, 0.06 * length)], M("Hazard"), seg=seg, loc=base,
             rot=(-90, 0, 0), arc=180)
    rb.lathe([(r * 1.06, -0.2 * length), (r * 1.06, 0.06 * length)], M("Steel"), seg=seg, loc=base,
             rot=(-90, 0, 0), arc=180, start=180)

    def rad(t):
        pts = [(0.15, 1.0), (0.45, 0.72), (0.75, 0.38), (1.0, 0.0)]
        prev = (0.0, 1.0)
        for tt, rr in pts:
            if t <= tt:
                u = (t - prev[0]) / (tt - prev[0])
                return r * (prev[1] + (rr - prev[1]) * u)
            prev = (tt, rr)
        return 0.0
    n = steps
    for f in range(flutes):
        for i in range(n):
            t = 0.08 + 0.8 * i / (n - 1)
            a = math.radians(f * 360 / flutes + t * 360 * turns)
            rr = rad(t) + r * 0.08
            p = base + V(rr * math.cos(a), length * t, rr * math.sin(a))
            rb.box(r * 0.5, length * 0.13, r * 0.14, M("Hazard" if f == 0 else "Steel"), loc=p,
                   rot=(0, 90 - math.degrees(a), 0))
    return rb


def wheel(b, name, center, R, teeth=10):
    """Schneidrad: dreht um die Laengsachse (+Y), Zaehne nach vorn, Warnmarkierung an zwei Speichen."""
    rb = rotor(b, name, center, (0, 1, 0), spin="laengs (Bugrichtung)")
    c = Vector(center)
    rb.ring(R, 0.55, 0.7, M("Steel"), seg=12, loc=c, rot=(90, 0, 0))
    rb.cyl(R * 0.3, 0.9, M("HullDark"), seg=8, loc=c, rot=(-90, 0, 0))
    for k in range(4):
        a = math.radians(90 * k + 20)
        d = V(math.cos(a), 0, math.sin(a))
        rb.beam(c + d * R * 0.28, c + d * (R - 0.5), 0.35, M("Hazard" if k % 2 == 0 else "HullDark"))
    for k in range(teeth):
        a = math.radians(360 * k / teeth)
        d = V(math.cos(a), 0, math.sin(a))
        p = c + d * (R - 0.15) + V(0, 0.25, 0)
        rb.lathe([(0.26, 0), (0.1, 0.55), (0, 0.85)], M("HullDark"), seg=4, loc=p,
                 rot=qdir(V(0, 1, 0) + d * 0.35))
        if k % 2 == 0:
            rb.box(0.5, 0.45, 0.35, M("Steel"), loc=c + d * (R + 0.15), rot=(0, 90 - math.degrees(a), 0))
    return rb


def drum(b, name, center, half, r, rows=6):
    """Schneidwalze quer zur Flugrichtung: dreht um die Querachse (X), Zaehne in Schraubenlinien,
    Warnstreifen laengs auf der Walze."""
    rb = rotor(b, name, center, (1, 0, 0), spin="quer (Steuerbord-Achse)")
    c = Vector(center)
    rb.cyl(r, half * 2, M("Steel"), seg=12, loc=c, rot=(0, 90, 0))
    for sx in (-1, 1):
        rb.cyl(r * 1.08, 0.3, M("Hazard"), seg=12, loc=c + V(sx * (half - 0.15), 0, 0), rot=(0, 90, 0))
    for i in range(rows * 3):
        t = i / (rows * 3 - 1)
        x = -half * 0.85 + 1.7 * half * t
        for f in range(2):
            a = math.radians(f * 180 + t * 540)
            d = V(0, math.cos(a), math.sin(a))
            rb.lathe([(0.2, 0), (0, 0.65)], M("HullDark"), seg=4, loc=c + V(x, 0, 0) + d * r * 0.95, rot=qdir(d))
    rb.box(half * 1.6, 0.18, 0.12, M("Hazard"), loc=c + V(0, 0, r * 1.02))
    return rb


def mill(b, name, center, r, length, teeth=6):
    """Fraeskopf zwischen Spaltbacken: dreht um die Laengsachse (+Y), radiale Schneidzaehne."""
    rb = rotor(b, name, center, (0, 1, 0), spin="laengs (Bugrichtung)")
    c = Vector(center)
    rb.cyl(r, length, M("HullDark"), seg=8, loc=c, rot=(-90, 0, 0), r2=r * 0.7)
    for row in range(2):
        for k in range(teeth):
            a = math.radians(360 * k / teeth + row * 30)
            d = V(math.cos(a), 0, math.sin(a))
            p = c + V(0, (row - 0.3) * length * 0.45, 0) + d * r * 0.9
            rb.box(r * 0.5, length * 0.25, r * 0.2, M("Steel" if k % 3 else "Hazard"), loc=p,
                   rot=(0, 90 - math.degrees(a), 0))
    rb.lathe([(r * 0.7, 0), (0, r * 0.9)], M("Steel"), seg=8, loc=c + V(0, length / 2, 0), rot=(-90, 0, 0))
    return rb


def jaw(b, name, hinge, secs, x):
    """Bewegliche Spaltbacke: Gelenk bei hinge, Drehachse senkrecht (Godot +Y)."""
    rb = rotor(b, name, hinge, (0, 0, 1), kind="jaw", spin="Gelenk senkrecht")
    rb.loft(secs, M("Steel"), x=x)
    rb.cyl(0.35, 1.2, M("HullDark"), seg=8, loc=hinge)
    return rb


# =============================================================== S: Einheitsschiffe
@ship("unit_trader_100", "S", "Standardfrachter gross (Sechskant-Frachter): runde Cockpitkapsel vorn, dicke Sechskant-Frachtsektion mit 5 Segmenten (Tore beidseits, Dachklappen), schmaler Antriebsteil mit zwei beringten Duesen, Laserturm, Sensor, Duesen", cargo=100)
def unit_trader_100(b):
    n = segs("unit_trader_100")
    C = [(5.0, 0.0, 0.0), (4.6, 0.55, 0.45), (3.8, 0.9, 0.7), (2.8, 0.95, 0.75), (2.4, 0.7, 0.6)]
    b.loft(C, M("Hull"), shape="round", n=10)
    visor(b, C, 3.9, width=1.0, length=0.8)
    y0 = 2.5
    H = [(y0 + 0.4, 1.4, 1.0), (y0, 2.0, 1.4), (y0 - n * SEG, 2.0, 1.4), (y0 - n * SEG - 0.4, 1.4, 1.0)]
    b.loft(H, M("Hull"), shape="hex")
    cargo_bay(b, H, y0, n, top=True, c=0.45, rib=False, shape="hex")
    E = [(y0 - n * SEG - 0.2, 1.1, 0.8), (-4.6, 1.2, 0.85), (-5.0, 1.0, 0.7)]
    b.loft(E, M("Steel"))
    dress(b, E, seed=1.0, accent=False, sides=False, bottom=False, n=3)
    dress(b, C, seed=1.5, trench=False, sides=False, bottom=False, shape="round")
    vents(b, (0, -3.4, 0.85), n=4, length=1.2)
    for x in (-0.6, 0.6):
        engine(b, (x, -5.3, 0.0), 0.5)
    hp_turret(b, 4, (0, 2.0, 1.4), s=0.34)
    sensor(b, (0, 3.3, 0.72), r=0.22)
    thrusters(b, [(1.6, y0 - 0.2, 1.0), (-1.6, y0 - 0.2, 1.0)], s=0.4)
    b.box(0.35, n * SEG, 0.05, M("Accent"), loc=(0, y0 - n * SEG / 2, 1.42))
    navl(b, 2.05, y0 - 0.3, 0.0)


@ship("unit_trader_60", "S", "Standardfrachter klein (schlanke Zigarre): Bugkapsel mit Fensterring, kurze, nur leicht verdickte Frachtsektion mit 3 Segmenten, ein beringtes Triebwerk, zwei Heckflossen, Laserturm", cargo=60)
def unit_trader_60(b):
    n = segs("unit_trader_60")
    y0 = 1.6
    S = [(5.0, 0.0, 0.0), (4.4, 0.5, 0.5), (3.2, 0.85, 0.85), (y0 + 0.4, 0.9, 0.9), (y0, 1.1, 1.1),
         (y0 - n * SEG, 1.1, 1.1), (y0 - n * SEG - 0.4, 0.85, 0.85), (-4.4, 0.8, 0.8), (-5.0, 0.6, 0.6)]
    b.loft(S, M("Hull"), shape="round", n=10)
    dress(b, S, seed=2.0, shape="round")
    b.loft([(3.9, 0.66, 0.66), (3.5, 0.78, 0.78)], M("Window"), shape="round", n=10)
    cargo_bay(b, S, y0, n, c=0.0, shape="round")
    plate(b, S, -1.6, -3.6, mat="Steel", shape="round")
    for s in (1, -1):
        fin(b, [(0.2, 0), (-1.2, 0), (-1.6, 1.0), (-0.8, 1.0)], 0.1, (s * 0.55, -3.8, 0.55), side=-30 * s)
    engine(b, (0, -5.3, 0.0), 0.62)
    hp_turret(b, 4, (0, 2.9, 0.85), s=0.3)
    sensor(b, (0, -2.4, 0.8), r=0.2)
    navl(b, 1.15, y0 - 1.2, 0.0)


@ship("unit_miner_100", "S", "Standardschuerfer gross (Linsenrumpf): flache, breite Linse mit Doppelbohrer am Bug (zwei drehende Bohrkegel auf Panzerarmen), darunter ein grosser Erzraum mit 5 Segmenten und Bodenklappen, zwei beringte Duesen, Kuehlrippen", cargo=100)
def unit_miner_100(b):
    n = segs("unit_miner_100")
    S = [(4.4, 0.9, 0.35), (3.6, 1.8, 0.55), (1.4, 2.4, 0.7), (-3.6, 2.4, 0.7), (-4.8, 1.6, 0.5)]
    b.loft(S, M("Hull"), shape="round", n=10)
    dress(b, S, seed=3.0, shape="round", trench=False, sides=False, bottom=False)
    for s in (1, -1):
        Pr = [(4.9, 0.55, 0.45, -0.05), (3.4, 0.7, 0.55), (2.6, 0.55, 0.45)]
        b.loft(Pr, M("Armor"), x=s * 1.1)
        drill(b, "Rotor_Drill_" + ("R" if s > 0 else "L"), (s * 1.1, 5.1, -0.05), 2.6, 0.62, seg=6, steps=4)
    y0 = 2.2
    K = [(y0 + 0.5, 0.0, 0.0, 0.5), (y0, 1.3, 0.55, 0.7), (y0 - n * SEG, 1.3, 0.55, 0.7),
         (y0 - n * SEG - 0.6, 0.8, 0.35, 0.6)]
    b.loft(K, M("Armor"))
    cargo_bay(b, K, y0, n, top=True, rib=False, frames=False)
    band(b, K, y0, g=1.06)
    band(b, K, y0 - n * SEG, g=1.06)
    visor(b, S, 3.3, width=1.4, length=0.7)
    for x in (-0.8, 0.8):
        engine(b, (x, -5.0, 0.0), 0.5)
    sensor(b, (0, -3.6, 0.65), r=0.25)
    navl(b, 2.45, -1.0, 0.0)


@ship("unit_miner_60", "S", "Standardschuerfer klein (Rautenrumpf): Rautenrumpf mit kleinem drehendem Bohrkopf am Bug, kleiner Erzraum unten mit 3 Segmenten, ein beringtes Triebwerk, Rueckenfinne", cargo=60)
def unit_miner_60(b):
    n = segs("unit_miner_60")
    S = [(4.0, 0.45, 0.35), (3.2, 0.9, 0.6), (0.6, 1.4, 0.85), (-4.2, 1.3, 0.8), (-5.0, 0.9, 0.55)]
    b.loft(S, M("Hull"), shape="diamond")
    dress(b, S, seed=4.0, shape="diamond")
    band(b, S, 3.7, mat="Armor", g=1.15, t=0.5, shape="diamond")
    drill(b, "Rotor_Drill", (0, 4.2, 0.0), 2.6, 0.8, flutes=2)
    y0 = 0.8
    K = [(y0 + 0.5, 0.0, 0.0, 0.55), (y0, 0.85, 0.45, 0.75), (y0 - n * SEG, 0.85, 0.45, 0.75),
         (y0 - n * SEG - 0.5, 0.5, 0.3, 0.65)]
    b.loft(K, M("Steel"))
    cargo_bay(b, K, y0, n, top=True)
    visor(b, S, 2.8, width=0.6, length=1.0)
    fin(b, [(0.4, 0), (-1.4, 0), (-1.9, 1.0), (-1.0, 1.0)], 0.1, (0, -3.0, 0.7))
    engine(b, (0, -5.3, 0.0), 0.6)
    navl(b, 1.4, -2.6, 0.0)


# =============================================================== S: Skiffs, Kurier, Schuerfer, Kampf
@ship("pip", "S", "Kleiner Allzweck-Skiff (Ei): eiförmige Kapsel mit Fensterring, backbord drei Kanister-Wölbungen (60), steuerbord eine Frachtblase mit 2 Segmenten (30), Linse unter dem Bug, Heckfinne", cargo=90)
def pip(b):
    S = [(4.2, 0.0, 0.0), (3.6, 1.0, 0.85), (1.6, 1.6, 1.3), (-1.4, 1.55, 1.25), (-3.4, 1.0, 0.8), (-3.9, 0.7, 0.55)]
    b.loft(S, M("Hull"), shape="round", n=10)
    dress(b, S, seed=5.0, shape="round")
    b.loft([(2.95, 1.35, 1.12), (2.45, 1.48, 1.22)], M("Window"), shape="round", n=10)
    for k in range(3):
        b.sphere(0.62, M("Steel"), seg=8, rings=5, loc=(-1.35, 1.0 - k * SEG * 1.1, -0.1))
    y0 = 0.9
    P = [(y0 + 0.5, 0.0, 0.0), (y0 + 0.2, 0.6, 0.6), (y0 - 2 * SEG, 0.65, 0.65), (y0 - 2 * SEG - 0.5, 0.0, 0.0)]
    b.loft(P, M("Armor"), shape="round", n=8, x=1.4)
    cargo_bay(b, P, y0, 2, x=1.4, sides=(1,), c=0.0, shape="round")
    lens(b, (0, 3.6, -0.8), r=0.22)
    plate(b, S, -1.8, -3.0, mat="Steel", shape="round")
    fin(b, [(0.6, 0), (-1.2, 0), (-1.6, 0.9), (-0.8, 0.9)], 0.12, (0, -2.4, 1.0))
    engine(b, (0, -4.2, 0.0), 0.55)
    thrusters(b, [(1.5, -2.6, 0.3), (-1.5, -2.6, 0.3)], s=0.35)
    navl(b, 2.15, 0.2, 0.3)
    hp_turret(b, 6, (0, -0.6, 1.2), s=0.3)

@ship("nomad", "S", "Fracht-Skiff (Katamaran): schmaler Mittelrumpf mit zwei angeformten Frachtgondeln gleicher Laenge mit gleichen Triebwerken (steuerbord dick mit 4 Segmenten = 70, backbord schlanker mit 2 Segmenten = 30), Fensterband, Gondelduesen", cargo=100)
def nomad(b):
    S = [(5.0, 0.0, 0.0), (3.8, 0.55, 0.35), (2.0, 0.8, 0.55), (-4.2, 0.8, 0.55), (-4.8, 0.6, 0.4)]
    b.loft(S, M("Hull"))
    dress(b, S, seed=6.0)
    # beide Gondeln gleich lang (Antrieb symmetrisch); die Kapazitaet (70 bzw. 30) zeigt sich nur an
    # Dicke und Torzahl innerhalb gleicher Laenge
    L4 = 4 * SEG
    for s, n, w in ((1, 4, 0.75), (-1, 2, 0.55)):
        y0 = 1.8
        P = [(y0 + 0.6, 0.0, 0.0, -0.05), (y0, w, w * 0.8, -0.05), (y0 - L4, w, w * 0.8, -0.05),
             (y0 - L4 - 0.6, 0.45, 0.36, -0.05)]
        b.loft(P, M("Steel"), x=s * 1.45)
        ys = y0 - (L4 - n * SEG) / 2
        cargo_bay(b, P, ys, n, x=s * 1.45, sides=(s,))
        b.beam(V(s * 0.75, y0 - 0.6, 0.0), V(s * 0.8, y0 - 0.6, 0.0), 0.4, M("HullDark"))
        engine(b, (s * 1.45, y0 - L4 - 0.95, -0.05), 0.42, housing=False)
    visor(b, S, 3.0, width=0.7, length=1.0)
    stripe(b, S, 1.8, -3.8, width=0.28)
    fin(b, [(0.2, 0), (-1.0, 0), (-1.3, 0.7), (-0.6, 0.7)], 0.1, (0, -3.4, 0.5))
    engine(b, (0, -5.0, 0.0), 0.4)
    navl(b, 2.3, -0.6, 0.0)
    hp_turret(b, 6, (0, -1.4, 0.55), s=0.3)

@ship("prospector", "S", "Schneller Schuerf-Skiff (Lanzenpfeil): Pfeilrumpf, der in eine Bohrlanze mit drehendem Kernbohrer ausläuft, flacher Frachtruecken mit 3 Segmenten und Klappen, gepfeilte Flossen, Einlaesse, ein beringtes Triebwerk", cargo=60)
def prospector(b):
    n = segs("prospector")
    S = [(5.4, 0.16, 0.16, -0.25), (3.4, 0.28, 0.28, -0.2), (2.4, 0.7, 0.45), (0.6, 1.15, 0.6), (-3.8, 1.25, 0.6),
         (-4.6, 0.95, 0.45)]
    b.loft(S, M("Hull"))
    dress(b, S, seed=7.0)
    drill(b, "Rotor_CoreDrill", (0, 5.3, -0.25), 2.2, 0.38, flutes=2, turns=2.0)
    band(b, S, 3.1, g=1.2, mat="Steel")
    y0 = 0.4
    H = [(y0 + 0.4, 0.0, 0.0, 0.55), (y0, 0.7, 0.35, 0.6), (y0 - n * SEG, 0.75, 0.38, 0.6), (y0 - n * SEG - 0.5, 0.4, 0.2, 0.55)]
    b.loft(H, M("Steel"))
    cargo_bay(b, H, y0, n, sides=(), top=True)
    visor(b, S, 1.6, width=0.8, length=1.0)
    for s in (1, -1):
        b.prism([(0, 0), (2.0, -1.8), (2.0, -2.5), (0, -1.6)], 0.1, M("Accent"), loc=(s * 1.1, -1.8, -0.1),
                scale=(s, 1, 1))
        vents(b, (s * 1.22, 0.4, 0.0), n=3, length=0.4, side=s)
    engine(b, (0, -4.9, 0.0), 0.55)
    navl(b, 3.05, -4.2, -0.1)
    hp_turret(b, 6, (0, 2.0, 0.5), s=0.3, empty=True)

@ship("swift", "S", "Kurier (Pfeil mit Gondeln): Rautenpfeil zwischen zwei uebergrossen, beringten Triebwerksgondeln mit Einlaessen, kleine Bauchfracht mit 2 Segmenten, Bugrohr", cargo=30)
def swift(b):
    S = [(5.0, 0.0, 0.0), (3.0, 0.45, 0.3), (0.0, 0.85, 0.5), (-4.0, 0.95, 0.55), (-4.6, 0.7, 0.4)]
    b.loft(S, M("Steel"), shape="diamond")
    dress(b, S, seed=8.0, shape="diamond")
    for s in (1, -1):
        G = [(0.6, 0.0, 0.0), (0.0, 0.7, 0.7), (-0.6, 0.9, 0.9), (-4.0, 0.9, 0.9), (-4.4, 0.78, 0.78)]
        b.loft(G, M("Hull"), shape="round", n=10, x=s * 1.5)
        b.ring(0.97, 0.15, 0.4, M("Accent"), seg=10, loc=(s * 1.5, -1.4, 0.0), rot=(90, 0, 0))
        b.ring(0.97, 0.15, 0.3, M("HullDark"), seg=10, loc=(s * 1.5, -2.8, 0.0), rot=(90, 0, 0))
        b.lathe([(0.55, 0), (0.0, 0.01)], M("HullDark"), seg=8, loc=(s * 1.5, 0.15, 0.0), rot=(-90, 0, 0))
        b.nozzle((s * 1.5, -4.8, 0.0), 0.8, housing=False)
    y0 = 0.4
    P = [(y0 + 0.4, 0.0, 0.0, -0.55), (y0, 0.45, 0.3, -0.6), (y0 - 2 * SEG, 0.45, 0.3, -0.6), (y0 - 2 * SEG - 0.4, 0.0, 0.0, -0.55)]
    b.loft(P, M("Armor"))
    cargo_bay(b, P, y0, 2, sides=(), bottom=True)
    visor(b, S, 2.4, width=0.5, length=1.4)
    hp_turret(b, 4, (0, 0.6, 0.5), s=0.26)
    fin(b, [(0.0, 0), (-1.4, 0), (-1.8, 0.9), (-1.0, 0.9)], 0.08, (0, -2.6, 0.5))
    engine(b, (0, -4.9, 0.0), 0.35, housing=False)
    navl(b, 2.45, -2.0, 0.0)


@ship("pickaxe", "S", "Leichter Schuerfer (Hammerkopf): Sechskantrumpf mit angeformtem Spitzhacken-Querkopf (drehender Fraesdorn in der Mitte, feste Spitzen aussen), Bauch-Erzraum mit 3 Segmenten, Kuehlrippen, ein beringtes Triebwerk", cargo=60)
def pickaxe(b):
    n = segs("pickaxe")
    S = [(2.8, 0.5, 0.45), (2.2, 1.1, 0.75), (-4.0, 1.2, 0.8), (-4.8, 0.9, 0.6)]
    b.loft(S, M("Hull"), shape="hex")
    dress(b, S, seed=9.0, shape="hex")
    T = [(3.6, 0.0, 0.0), (3.2, 2.6, 0.45), (2.4, 2.9, 0.5), (2.0, 1.0, 0.4)]
    b.loft(T, M("Armor"), shape="diamond")
    drill(b, "Rotor_Spike", (0, 3.6, 0.0), 2.8, 0.62, flutes=3, turns=1.0)
    for s in (1, -1):
        b.lathe([(0.3, 0), (0, 1.2)], M("HullDark"), seg=5, loc=(s * 2.9, 2.6, 0), rot=qdir((s, 0.5, 0)))
        b.box(0.6, 0.6, 0.1, M("Hazard"), loc=(s * 1.9, 2.8, 0.42))
    y0 = 1.2
    K = [(y0 + 0.4, 0.6, 0.3, -0.7), (y0, 0.95, 0.5, -0.75), (y0 - n * SEG, 0.95, 0.5, -0.75), (y0 - n * SEG - 0.5, 0.55, 0.3, -0.7)]
    b.loft(K, M("Steel"))
    cargo_bay(b, K, y0, n, sides=(), bottom=True)
    visor(b, S, 1.8, width=0.9, length=0.7)
    vents(b, (0, -2.6, 0.82), n=4, length=0.9)
    engine(b, (0, -5.1, 0.0), 0.6)
    navl(b, 1.25, -2.0, 0.3)


@ship("corsair", "S", "Kaperschiff (Klingenraute): zackiger Rautenrumpf mit nach vorn gepfeilten Klingenfluegeln, zwei Bugrohre, Dornfinne, Sensorbuckel, angedeutete Flickplatten, zwei Heckduesen", cargo=0)
def corsair(b):
    S = [(5.0, 0.0, 0.0), (3.0, 0.7, 0.45), (0.0, 1.0, 0.6), (-3.8, 1.3, 0.55), (-4.6, 0.9, 0.4)]
    b.loft(S, M("HullDark"), shape="diamond")
    dress(b, S, seed=10.0, shape="diamond")
    for s in (1, -1):
        b.prism([(0, 1.0), (3.4, 2.8), (3.0, 1.6), (1.6, -1.4), (0, -1.6)], 0.16, M("Hull"),
                loc=(s * 0.6, -0.4, 0.0), scale=(s, 1, 1))
        b.prism([(1.4, 1.6), (3.3, 2.7), (3.1, 2.1)], 0.2, M("Accent"), loc=(s * 0.6, -0.4, 0.0), scale=(s, 1, 1))
        hp_gun(b, 2 if s < 0 else 3, (s * 1.0, 1.4, -0.05), 2.0 if s < 0 else 2.6, r=0.1 if s < 0 else 0.13)
        b.lathe([(0.2, 0), (0, 1.0)], M("HullDark"), seg=4, loc=(s * 3.7, 2.2, 0), rot=qdir((s * 0.3, 1, 0)))
    fin(b, [(0.4, 0), (-2.4, 0), (-3.0, 1.6)], 0.12, (0, -0.2, 0.45), mat="Hull")
    visor(b, S, 2.2, width=0.55, length=1.0)
    b.box(0.9, 1.3, 0.08, M("Rust"), loc=(-0.4, -1.4, 0.4), rot=(0, 25, 8))
    b.box(0.7, 0.9, 0.08, M("Steel"), loc=(0.5, -2.6, 0.38), rot=(0, -22, -5))
    sensor(b, (0, 0.8, 0.55), r=0.2)
    for x in (-0.55, 0.55):
        engine(b, (x, -4.9, 0.0), 0.42, ring="LightRed", housing=False)
    navl(b, 3.95, 1.8, 0.0)


@ship("falcon", "S", "Jaeger (Ringfluegler): schlanke Rundkapsel mit Kanzel, hinten von einem flachen Ringfluegel umschlossen, zwei Bugwaffen an Fluegelstummeln, zwei Waffen auf dem Ring, Accent-Feld auf dem Ring, Doppeltriebwerk", cargo=0)
def falcon(b):
    S = [(5.0, 0.0, 0.0), (3.8, 0.45, 0.4), (1.6, 0.75, 0.62), (-3.6, 0.8, 0.62), (-4.4, 0.6, 0.5)]
    b.loft(S, M("Hull"), shape="round", n=10)
    dress(b, S, seed=11.0, shape="round", n=6)
    visor(b, S, 2.6, width=0.55, length=1.4, mat="Window")
    b.torus(2.8, 0.42, M("Steel"), seg=20, minor=4, loc=(0, -2.2, 0.0), minor_scale=(1.0, 0.35))
    b.lathe([(3.24, -0.06), (3.24, 0.06)], M("Accent"), seg=20, loc=(0, -2.2, 0.0))
    for k in range(4):
        a = math.radians(45 + 90 * k)
        b.beam(V(0.7 * math.cos(a), -2.2, 0.55 * math.sin(a)), V(2.5 * math.cos(a), -2.2, 0.15 * math.sin(a)),
               0.14, M("HullDark"))
    for s in (1, -1):
        b.prism([(0, 0.6), (1.4, 0.0), (1.4, -0.6), (0, -0.8)], 0.12, M("Steel"), loc=(s * 0.6, 1.0, -0.05),
                scale=(s, 1, 1))
        hp_gun(b, 2 if s < 0 else 3, (s * 1.9, 0.6, -0.05), 2.4, r=0.09 if s < 0 else 0.11)
        b.box(0.5, 1.0, 0.05, M("Accent"), loc=(s * 2.8, -2.4, 0.16))
    for x in (-0.38, 0.38):
        engine(b, (x, -4.7, 0.0), 0.36, housing=False)
    navl(b, 3.25, -2.2, 0.15)

# =============================================================== M: Frachter und Tanker
@ship("mule", "M", "Leichter Frachter (Dreirumpf): runde Mittelspindel mit Cockpit, zwei Frachtgondeln an Pylonen mit je 3 Segmenten (120), grosses beringtes Haupttriebwerk und Gondelduesen, Laserturm, Antennen", cargo=120)
def mule(b):
    S = [(7.5, 0.0, 0.0), (6.5, 0.7, 0.7), (4.8, 1.2, 1.2), (-6.4, 1.2, 1.2), (-7.2, 1.0, 1.0)]
    b.loft(S, M("Hull"), shape="round", n=10)
    dress(b, S, seed=12.0, shape="round")
    b.loft([(5.9, 0.95, 0.95), (5.4, 1.08, 1.08)], M("Window"), shape="round", n=10)
    for k in (2.4, -2.0):
        band(b, S, k, shape="round", g=1.06)
    n = 3
    for s in (1, -1):
        y0 = 2.2
        P = [(y0 + 0.8, 0.0, 0.0, -0.2), (y0, 1.0, 1.0, -0.2), (y0 - n * SEG, 1.0, 1.0, -0.2),
             (y0 - n * SEG - 0.8, 0.6, 0.6, -0.2)]
        b.loft(P, M("Steel"), shape="round", n=8, x=s * 2.6)
        cargo_bay(b, P, y0, n, x=s * 2.6, sides=(s,), c=0.0, shape="round")
        for y in (y0 - 0.5, y0 - n * SEG + 0.4):
            b.beam(V(s * 1.0, y, 0.0), V(s * 1.8, y, -0.15), 0.35, M("HullDark"))
        engine(b, (s * 2.6, y0 - n * SEG - 1.1, -0.2), 0.4, housing=False)
    plate(b, S, -3.2, -6.2, mat="Armor", shape="round")
    engine(b, (0, -7.5, 0.0), 0.95)
    hp_turret(b, 3, (0, 4.0, 1.15), s=0.36)
    for x in (-0.4, 0.4):
        b.mast((x, -1.0, 1.15), 0.9, light="Light" if x > 0 else "LightRed")
    thrusters(b, [(1.2, 5.0, 0.0), (-1.2, 5.0, 0.0)], s=0.45)
    navl(b, 3.65, 0.4, -0.2)


@ship("hauler", "M", "Breiter Frachter (schwerer Kasten): bulliger Breitrumpf mit langer Frachtsektion aus 8 Segmenten (Tore beidseits, Dachklappen), Keilbug mit Fensterband, Laserturm, zwei beringte Duesen, Kuehlrippen", cargo=160)
def hauler(b):
    n = segs("hauler")
    y0 = 4.0
    S = [(7.5, 0.0, 0.0), (6.6, 1.4, 0.55), (5.2, 2.8, 1.0), (y0 + 0.3, 3.1, 1.3), (y0 - n * SEG, 3.1, 1.3),
         (-4.6, 2.6, 1.1), (-7.0, 2.2, 0.95), (-7.5, 1.8, 0.8)]
    b.loft(S, M("Hull"), c=0.3)
    dress(b, S, trench=False, seed=13.0, c=0.3)
    cargo_bay(b, S, y0, n, top=True, c=0.3, rib=False)
    visor(b, S, 5.6, width=2.4)
    b.box(0.5, n * SEG, 0.06, M("Accent"), loc=(0, y0 - n * SEG / 2, 1.42))
    for s in (1, -1):
        vents(b, (s * 1.4, -5.6, 1.0), n=5, length=1.4)
        b.box(0.12, 1.6, 0.3, M("Hazard"), loc=(s * 2.65, -6.2, 0.0))
    hp_turret(b, 3, (1.6, 5.4, 0.85), s=0.4)
    sensor(b, (-1.4, 5.0, 0.9), r=0.28)
    for x in (-1.3, 1.3):
        engine(b, (x, -7.8, 0.0), 0.7)
    thrusters(b, [(3.0, 4.6, 0.0), (-3.0, 4.6, 0.0), (2.4, -6.6, 0.0), (-2.4, -6.6, 0.0)], s=0.45)
    navl(b, 3.2, 5.0, 0.3)


@ship("tinker", "M", "Mehrzweck (asymmetrisch, neu aufgebaut): breiter Sechskant-Mittelrumpf, steuerbord eine Frachtgondel mit 3 Segmenten an zwei Pylonen, backbord ein Lasergehaeuse an Pylonen mit Gelenkkopf und Linse vorn, Laserturm, ein mittiges Haupttriebwerk (Antrieb symmetrisch)", cargo=60)
def tinker(b):
    S = [(7.5, 0.0, 0.0), (6.4, 0.9, 0.6), (4.4, 1.9, 1.0), (-5.6, 1.9, 1.0), (-6.8, 1.4, 0.75)]
    b.loft(S, M("Hull"), shape="hex")
    dress(b, S, seed=14.0, shape="hex")
    y0 = 2.0
    n = segs("tinker")
    P = [(y0 + 1.0, 0.0, 0.0), (y0, 1.0, 0.9), (y0 - n * SEG, 1.0, 0.9), (y0 - n * SEG - 1.0, 0.55, 0.5)]
    b.loft(P, M("Steel"), shape="round", n=8, x=3.2)
    cargo_bay(b, P, y0, n, x=3.2, sides=(1,), c=0.0, shape="round")
    for y in (y0 - 0.3, y0 - n * SEG + 0.3):
        b.beam(V(1.8, y, 0.0), V(2.4, y, 0.0), 0.5, M("HullDark"))
    A = [(5.6, 0.0, 0.0, -0.2), (5.0, 0.55, 0.5, -0.2), (-2.2, 0.6, 0.55, -0.2), (-2.8, 0.4, 0.35, -0.2)]
    b.loft(A, M("Armor"), shape="round", n=8, x=-2.9)
    for y in (3.2, -1.2):
        b.beam(V(-1.8, y, -0.1), V(-2.4, y, -0.15), 0.45, M("HullDark"))
    b.sphere(0.55, M("HullDark"), seg=8, rings=4, loc=(-2.9, 5.5, -0.2))
    b.cyl(0.32, 0.9, M("Steel"), seg=8, loc=(-2.9, 6.1, -0.2), rot=(-90, 0, 0))
    lens(b, (-2.9, 6.6, -0.2), r=0.26)
    b.box(0.06, 2.4, 0.5, M("Accent"), loc=(-3.48, 1.6, -0.2))
    for y in (3.0, -2.8):
        band(b, S, y, shape="hex")
    visor(b, S, 5.4, width=1.4)
    vents(b, (0, -4.0, 1.0), n=4, length=1.2)
    hp_turret(b, 3, (0, 1.0, 1.0), s=0.42)
    engine(b, (0, -7.2, 0.0), 0.85)
    thrusters(b, [(1.9, -5.0, 0.0), (-1.9, -5.0, 0.0)], s=0.45)
    navl(b, 4.25, 0.0, 0.0)

@ship("cistern", "M", "Tanker (Tankspindel): bauchige runde Spindel, 18 Druckkoerper-Wölbungen (9 je Seite, 360) unter Panzerkragen, Bugkapsel mit Fensterring, grosses beringtes Triebwerk, Laserturm", cargo=360)
def cistern(b):
    n = segs("cistern") // 2
    y0 = 4.4
    S = [(7.5, 0.0, 0.0), (6.6, 0.9, 0.7), (5.2, 1.6, 1.15), (y0, 1.75, 1.25), (y0 - n * SEG, 1.75, 1.25),
         (-4.6, 1.5, 1.1), (-7.0, 1.1, 0.85)]
    b.loft(S, M("Hull"), shape="round", n=10)
    dress(b, S, seed=15.0, shape="round")
    for i in range(n):
        y = y0 - SEG * (i + 0.5)
        for s in (1, -1):
            b.sphere(0.62, M("Steel"), seg=8, rings=5, loc=(s * 1.55, y, 0.15))
    for i in range(0, n + 1, 3):
        band(b, S, y0 - SEG * i, shape="round", g=1.32, t=0.28, mat="Armor")
    b.loft([(6.2, 1.1, 0.85), (5.7, 1.3, 1.0)], M("Window"), shape="round", n=10)
    b.strut(V(0, 4.0, 1.25), V(0, -3.0, 1.25), 0.14, M("Copper"), seg=5)
    hp_turret(b, 9, (0, 5.0, 1.2), s=0.36)
    for x in (-0.4, 0.4):
        b.box(0.12, 1.2, 0.25, M("Hazard"), loc=(x * 3.2, -5.6, 1.0))
    engine(b, (0, -7.4, 0.0), 1.0)
    thrusters(b, [(1.6, -5.2, 0.0), (-1.6, -5.2, 0.0)], s=0.45)
    navl(b, 2.4, -5.0, 0.0)


@ship("behemoth", "L", "Schwerer Frachter (Kastenrueckgrat): langer massiger Rumpf mit fuenf Frachtbloecken zu je 5 Segmenten (500) zwischen Panzerkragen, Keilbug mit Fensterband, Laserturm, Triebwerksblock mit drei beringten Duesen, Antennen", cargo=500)
def behemoth(b):
    nb, per = 5, segs("behemoth") // 5
    gap = 0.35
    y0 = 10.4
    total = nb * per * SEG + (nb - 1) * gap
    S = [(13.0, 0.0, 0.0), (12.0, 1.4, 1.0), (10.9, 2.4, 1.7), (y0 + 0.2, 2.5, 1.8), (y0 - total, 2.5, 1.8),
         (-11.6, 2.2, 1.6), (-12.6, 1.8, 1.3)]
    b.loft(S, M("Hull"), c=0.25)
    dress(b, S, trench=False, seed=16.0, c=0.25)
    for k in range(nb):
        ys = y0 - k * (per * SEG + gap)
        cargo_bay(b, S, ys, per, c=0.25, mat="Steel" if k % 2 else "Armor", rib=False, frames=False)
        top_hatch(b, 0, ys - per * SEG / 2, 1.8, 3.0, per * SEG * 0.85, mat="Steel")
        band(b, S, ys + gap / 2 if k else ys + 0.1, mat="HullDark", g=1.12, t=0.35, c=0.25)
    band(b, S, y0 - total - 0.1, mat="HullDark", g=1.12, t=0.35, c=0.25)
    visor(b, S, 11.6, width=2.6, length=0.8)
    b.box(0.6, total, 0.06, M("Accent"), loc=(0, y0 - total / 2, 1.86))
    hp_turret(b, 8, (0, 11.0, 1.75), s=0.5)
    b.mast((1.0, -10.8, 1.6), 1.4, light="Light")
    b.mast((-1.0, -10.8, 1.6), 1.4, light="LightRed")
    vents(b, (0, -11.0, 1.6), n=5, length=2.2)
    for x, z in ((-1.2, 0.7), (1.2, 0.7), (0, -0.7)):
        engine(b, (x, -12.9, z), 0.9)
    thrusters(b, [(2.6, 11.0, 0.0), (-2.6, 11.0, 0.0), (2.4, -11.4, 0.0), (-2.4, -11.4, 0.0)], s=0.55)
    navl(b, 2.7, 10.6, 0.8, s=0.2)


# =============================================================== M: Schuerfer, Bau, Brecher
@ship("rockbreaker", "M", "Schwerer Schuerfer (Stufenkeil): zweistufiger Panzerkeil, aus dessen Bugkragen ein gerippter Bohrkegel waechst, zwei Bauch-Erzraeume (5 + 3 Segmente = 160), beringtes Grosstriebwerk, Kuehlrippen", cargo=160)
def rockbreaker(b):
    S = [(4.4, 1.5, 1.0), (3.4, 2.3, 1.3), (-5.6, 2.3, 1.3), (-6.8, 1.6, 1.0)]
    b.loft(S, M("Armor"), shape="wedge")
    dress(b, S, seed=17.0, shape="wedge")
    T = [(3.0, 0.0, 0.0, 1.0), (2.2, 1.2, 0.45, 1.2), (-4.6, 1.5, 0.5, 1.2), (-5.6, 1.0, 0.35, 1.1)]
    b.loft(T, M("Hull"))
    band(b, S, 4.2, mat="HullDark", g=1.12, t=0.6, shape="wedge")
    drill(b, "Rotor_Drill", (0, 4.7, 0.0), 3.6, 1.6, flutes=3, seg=10, turns=1.2)
    for s, n in ((1, 5), (-1, 3)):
        y0 = 2.2
        K = [(y0 + 0.6, 0.4, 0.5, -0.1), (y0, 0.85, 0.95, -0.1), (y0 - n * SEG, 0.85, 0.95, -0.1),
             (y0 - n * SEG - 0.6, 0.5, 0.6, -0.1)]
        b.loft(K, M("Steel"), x=s * 2.9)
        cargo_bay(b, K, y0, n, x=s * 2.9, sides=(s,), top=True)
    visor(b, T, 1.8, width=1.6)
    vents(b, (0, -3.2, 1.72), n=5, length=1.6)
    b.box(0.8, 0.6, 0.06, M("Hazard"), loc=(1.6, 3.2, 1.3))
    engine(b, (0, -7.2, 0.1), 1.1)
    thrusters(b, [(2.3, 2.4, 0.0), (-2.3, 2.4, 0.0), (2.0, -6.0, 0.0), (-2.0, -6.0, 0.0)], s=0.45)
    navl(b, 2.35, -3.0, 0.6)


@ship("excavator", "M", "Grossschuerfer Mk3 (Hammerkopf): breiter Hammerkopf-Bug mit grossem drehendem Schneidrad (Zaehne, Schaufeln, Warnspeichen) vor der Stirn, zwei runde Erz-Sponsons mit je 4 Segmenten (160), Reaktorring, zwei beringte Duesen", cargo=160)
def excavator(b):
    S = [(3.0, 1.6, 1.2), (-5.8, 1.8, 1.3), (-6.8, 1.3, 1.0)]
    b.loft(S, M("Armor"))
    dress(b, S, seed=18.0)
    Hd = [(6.6, 3.4, 1.7), (5.6, 3.6, 1.8), (4.0, 2.6, 1.5), (3.0, 1.8, 1.3)]
    b.loft(Hd, M("Hull"))
    b.cyl(0.9, 0.8, M("HullDark"), seg=8, loc=(0, 6.9, 0.0), rot=(-90, 0, 0))
    wheel(b, "Rotor_Wheel", (0, 7.6, 0.0), 2.6, teeth=10)
    for s in (1, -1):
        b.box(0.8, 0.8, 0.06, M("Hazard"), loc=(s * 2.6, 5.4, 1.82))
        y0 = 2.0
        K = [(y0 + 0.8, 0.0, 0.0, -0.2), (y0, 1.05, 1.05, -0.2), (y0 - 4 * SEG, 1.05, 1.05, -0.2),
             (y0 - 4 * SEG - 1.4, 0.6, 0.6, -0.2)]
        b.loft(K, M("Steel"), shape="round", n=8, x=s * 2.6)
        cargo_bay(b, K, y0, 4, x=s * 2.6, sides=(s,), top=True, c=0.0, shape="round")
    visor(b, Hd, 5.2, width=3.0)
    b.torus(1.2, 0.2, M("Light"), seg=10, minor=4, loc=(0, -4.6, 1.3), rot=(90, 0, 0))
    stripe(b, S, 2.6, -5.6, width=0.5)
    for x in (-0.9, 0.9):
        engine(b, (x, -7.1, 0.0), 0.8)
    navl(b, 3.65, 5.4, 0.6)


@ship("foreman", "M", "Bauschiff (Werkrumpf): Achtkantrumpf mit Materialtoren (steuerbord 5, backbord 3 Segmente = 160), eng auf dem Ruecken gefalteter Manipulator mit Greifer, Bugklaue, Warnmarkierungen, zwei beringte Duesen", cargo=160)
def foreman(b):
    y0 = 3.0
    S = [(7.5, 0.0, 0.0), (6.4, 1.1, 0.7), (4.6, 2.0, 1.1), (y0 + 0.2, 2.1, 1.15), (-5.4, 2.1, 1.15), (-7.0, 1.6, 0.85)]
    b.loft(S, M("Hull"))
    dress(b, S, seed=19.0)
    cargo_bay(b, S, y0, 5, sides=(1,))
    for i in range(3):
        yc = y0 - SEG * (i + 0.5)
        w, h, z = interp(S, yc)
        side_hatch(b, -w, yc, z, SEG * 0.72, h * 0.65 * 1.5)
    for y in (4.2, -5.0):
        band(b, S, y, mat="Accent", g=1.05, t=0.35)
    j0, j1, j2 = V(0.8, -4.6, 1.35), V(0.8, 3.0, 1.55), V(-0.8, -2.0, 1.75)
    b.box(1.0, 1.0, 0.6, M("HullDark"), loc=j0)
    b.beam(j0, j1, 0.45, M("Hazard"))
    b.sphere(0.42, M("HullDark"), seg=6, rings=4, loc=j1)
    b.beam(j1, j2, 0.36, M("Hazard"))
    b.sphere(0.34, M("HullDark"), seg=6, rings=4, loc=j2)
    b.claw(j2, (0, -1, 0.15), s=0.5, mat="Steel")
    visor(b, S, 5.6, width=2.2)
    b.claw(V(0, 7.3, -0.2), (0, 1, 0), s=0.55, mat="Hazard")
    sensor(b, (-1.2, 5.0, 1.0), r=0.26)
    for x in (-1.1, 1.1):
        engine(b, (x, -7.3, 0.0), 0.7)
    thrusters(b, [(2.1, 5.6, 0.0), (-2.1, 5.6, 0.0)], s=0.45)
    navl(b, 2.15, -4.0, 0.4)


@ship("quarry", "M", "Asteroidenbrecher (Zangenkeil): Panzer-Wedge, dessen Bug in eine Spaltzange aus zwei Backen auslaeuft, bewegliche Backen (eigene Nodes) mit drehendem Fraeskopf dazwischen, kleiner Bauch-Erzraum mit 3 Segmenten, beringtes Triebwerk", cargo=60)
def quarry(b):
    S = [(3.4, 1.6, 1.0), (2.4, 2.1, 1.25), (-5.6, 2.1, 1.25), (-6.6, 1.5, 0.95)]
    b.loft(S, M("Armor"), shape="wedge")
    dress(b, S, seed=20.0, shape="wedge")
    for s in (1, -1):
        J = [(7.5, 0.0, 0.0), (6.6, 0.25, 0.35), (5.2, 0.4, 0.55), (3.0, 0.65, 0.85), (2.6, 0.7, 0.9)]
        jb = jaw(b, "Jaw_" + ("R" if s > 0 else "L"), (s * 1.2, 3.0, 0.0), J, s * 1.2)
        jb.box(0.12, 1.4, 0.6, M("Hazard"), loc=(s * 1.85, 3.6, 0.0))
    b.cyl(0.55, 1.2, M("HullDark"), seg=6, loc=(0, 3.8, 0.0), rot=(-90, 0, 0))
    mill(b, "Rotor_Mill", (0, 5.0, 0.0), 0.6, 1.4)
    y0 = 1.0
    n = segs("quarry")
    y0 = -0.6
    K = [(y0 + 0.6, 0.0, 0.0, 1.1), (y0, 1.2, 0.5, 1.3), (y0 - n * SEG, 1.2, 0.5, 1.3), (y0 - n * SEG - 0.6, 0.7, 0.3, 1.2)]
    b.loft(K, M("Hull"))
    cargo_bay(b, K, y0, n, top=True)
    for y in (2.6, -2.4):
        band(b, S, y, g=1.08, t=0.4, shape="wedge")
    visor(b, S, 1.4, width=1.6)
    vents(b, (0, -5.2, 1.25), n=3, length=1.4)
    engine(b, (0, -7.0, 0.0), 1.0)
    navl(b, 2.15, -2.8, 0.3)


@ship("rockbreaker_ii", "M", "Grossbrecher (Breitzange): breiter Wedge-Rumpf mit Spaltzange am Bug, bewegliche Backen mit drehendem Mahlwerk dazwischen, Sensorkuppel, zwei runde Erzsponsons mit je 4 Segmenten (160), Reaktorring, zwei beringte Duesen", cargo=160)
def rockbreaker_ii(b):
    S = [(3.8, 1.9, 1.1), (2.8, 2.6, 1.3), (-5.6, 2.6, 1.3), (-6.6, 1.9, 0.95)]
    b.loft(S, M("Armor"), shape="wedge")
    dress(b, S, seed=21.0, shape="wedge")
    for s in (1, -1):
        J = [(7.0, 0.0, 0.0), (6.2, 0.3, 0.35), (4.8, 0.45, 0.55), (3.2, 0.6, 0.8), (2.8, 0.65, 0.85)]
        jaw(b, "Jaw_" + ("R" if s > 0 else "L"), (s * 1.3, 3.2, 0.0), J, s * 1.3)
        y0 = 2.0
        K = [(y0 + 0.8, 0.0, 0.0), (y0, 1.05, 1.05), (y0 - 4 * SEG, 1.1, 1.1), (y0 - 4 * SEG - 1.4, 0.7, 0.7)]
        b.loft(K, M("Hull"), shape="round", n=8, x=s * 3.4)
        cargo_bay(b, K, y0, 4, x=s * 3.4, sides=(s,), top=True, c=0.0, shape="round")
    b.cyl(0.6, 1.2, M("HullDark"), seg=6, loc=(0, 4.0, 0.0), rot=(-90, 0, 0))
    mill(b, "Rotor_Mill", (0, 5.2, 0.0), 0.7, 1.6, teeth=8)
    sensor(b, (0, 0.8, 1.3), r=0.5)
    band(b, S, 2.4, g=1.07, t=0.5, shape="wedge")
    visor(b, S, 3.2, width=2.0)
    b.torus(1.0, 0.18, M("Glow"), seg=8, minor=4, loc=(0, -4.6, 1.3), rot=(90, 0, 0))
    vents(b, (0, -3.0, 1.3), n=4, length=1.6)
    for x in (-1.1, 1.1):
        engine(b, (x, -6.9, 0.0), 0.85)
    navl(b, 4.55, -1.0, 0.0)


# =============================================================== M/L: Kampfschiffe
@ship("warden", "M", "Patrouillenboot (Schildkeil): kompakter, breiter flacher Pfeilkeil mit Rueckenkamm, Schildemitter-Wölbungen an den Fluegelspitzen, Zwillings- und Massetreiberturm, Bauchturm, Lichtleiste, zwei beringte Duesen", cargo=0)
def warden(b):
    S = [(7.0, 0.0, 0.0), (5.0, 1.6, 0.7), (1.6, 3.4, 0.9), (-2.4, 4.4, 0.8), (-4.6, 4.4, 0.7), (-5.6, 2.6, 0.75)]
    b.loft(S, M("Steel"), c=0.5)
    dress(b, S, seed=22.0, c=0.5)
    T = [(5.0, 0.0, 0.0, 0.5), (3.8, 0.9, 0.5, 0.6), (-3.6, 1.3, 0.6, 0.6), (-4.8, 1.0, 0.45, 0.55)]
    b.loft(T, M("Hull"))
    plate(b, S, 0.6, -3.6, mat="Armor", c=0.5, g=1.03)
    for s in (1, -1):
        b.sphere(0.85, M("Light"), seg=8, rings=4, loc=(s * 4.3, -3.6, 0.0), scale=(0.7, 1.2, 0.8))
        b.box(0.5, 2.2, 0.25, M("HullDark"), loc=(s * 4.05, -3.6, 0.0))
        b.box(2.6, 0.25, 0.06, M("Accent"), loc=(s * 2.4, -1.4, 0.86), rot=(0, 0, s * 24))
        vents(b, (s * 1.7, -4.4, 0.8), n=3, length=0.8)
    visor(b, T, 3.6, width=1.0)
    b.box(0.5, 0.25, 0.12, M("Light"), loc=(0.3, 2.6, 1.25))
    b.box(0.5, 0.25, 0.12, M("LightRed"), loc=(-0.3, 2.6, 1.25))
    hp_gun(b, 2, (-1.0, 2.2, 0.85), 2.6, r=0.12)
    hp_gun(b, 3, (1.0, 2.2, 0.85), 2.4, r=0.15)
    for x in (-1.2, 1.2):
        engine(b, (x, -5.9, 0.1), 0.7)
    navl(b, 4.45, -4.6, 0.3)


@ship("corvette", "M", "Korvette (Doppelrumpf): zwei langgestreckte Rumpfrohre mit Panzerkragen um eine Mittelsektion mit Bruecken-Fensterband, zwei Tuerme, Bugrohre, Punktverteidigung, Mast, vier beringte Duesen", cargo=0)
def corvette(b):
    for s in (1, -1):
        H = [(7.5, 0.0, 0.0), (6.4, 0.6, 0.6), (4.4, 1.0, 0.95), (-6.0, 1.05, 1.0), (-7.0, 0.8, 0.8)]
        b.loft(H, M("Steel"), x=s * 2.0, shape="hex")
        dress(b, H, seed=20.0 + s, x=s * 2.0, shape="hex", n=3, bottom=False)
        for y in (2.0, -2.6):
            band(b, H, y, x=s * 2.0, mat="Armor", g=1.12, t=0.6, shape="hex")
        hp_gun(b, 3 if s < 0 else 4, (s * 2.0, 6.9, 0.0), 1.6, r=0.12 if s < 0 else 0.15)
        for x in (s * 1.6, s * 2.4):
            engine(b, (x, -7.3, 0.0), 0.45, housing=False)
    E = [(-4.4, 2.6, 0.9), (-5.2, 3.2, 1.05), (-6.6, 3.2, 1.05)]
    b.loft(E, M("Armor"), c=0.3)
    for x in (-0.7, 0.7):
        engine(b, (x, -7.0, 0.0), 0.55, housing=False)
    C = [(4.6, 0.0, 0.0, 0.2), (3.4, 1.2, 0.6, 0.25), (-4.4, 1.6, 0.75, 0.25), (-5.4, 1.2, 0.6, 0.2)]
    b.loft(C, M("Hull"))
    visor(b, C, 3.0, width=1.4)
    hp_turret(b, 5, (0, 1.4, 0.95), s=0.55)
    hp_turret(b, 6, (0, -3.0, 0.95), s=0.55)
    hp_turret(b, 9, (2.0, -1.8, 1.0), s=0.42, kind="pd")
    navl(b, 3.05, -5.0, 0.0)


@ship("missile_boat", "M", "Raketenboot (Nadel mit Werfersponsons): schlanker Rautenrumpf mit wenig Panzerung, zwei angeformte Werfersponsons mit je 3x4 Startschaechten in der Stirn, Fensterband, Rueckenfinne, zwei beringte Duesen", cargo=0)
def missile_boat(b):
    S = [(7.5, 0.0, 0.0), (4.6, 0.6, 0.45), (0.0, 1.0, 0.6), (-6.4, 1.15, 0.6), (-7.0, 0.8, 0.45)]
    b.loft(S, M("Hull"), shape="diamond")
    dress(b, S, seed=24.0, shape="diamond")
    visor(b, [(7.5, 0.0, 0.0), (4.6, 0.6, 0.45), (0.0, 1.0, 0.6)], 3.4, width=0.5, length=1.4)
    for s in (1, -1):
        P = [(2.4, 0.85, 0.75), (1.8, 0.95, 0.85), (-3.6, 0.95, 0.85), (-4.4, 0.6, 0.55)]
        b.loft(P, M("Steel"), c=0.2, x=s * 1.85)
        for i in range(3):
            for j in range(4):
                b.box(0.34, 0.12, 0.26, M("HullDark"), loc=(s * 1.85 + (i - 1) * 0.55, 2.42, (j - 1.5) * 0.4))
                b.box(0.2, 0.13, 0.12, M("LightRed"), loc=(s * 1.85 + (i - 1) * 0.55, 2.41, (j - 1.5) * 0.4))
        b.box(1.95, 0.08, 1.75, M("Accent"), loc=(s * 1.85, 2.38, 0.0))
        band(b, P, -0.8, x=s * 1.85, c=0.2, g=1.06)
        b.box(1.0, 0.8, 0.06, M("Hazard"), loc=(s * 1.85, 1.2, 0.87))
    fin(b, [(0.2, 0), (-1.6, 0), (-2.0, 1.0), (-1.0, 1.0)], 0.1, (0, -4.6, 0.55))
    stripe(b, S, 2.0, -5.6, width=0.3)
    for x in (-0.55, 0.55):
        engine(b, (x, -7.1, 0.0), 0.45, housing=False)
    navl(b, 2.85, -2.0, 0.0)
    for s in (1, -1):
        _hp(b, 3 if s < 0 else 4, "missile", (s * 1.85, 2.48, 0.0), (0, 1, 0), s * 1.85)
    hp_turret(b, 5, (0, -2.4, 0.5), s=0.4)
    hp_turret(b, 7, (0, 0.9, 0.62), s=0.36, kind="pd")

@ship("bastion", "L", "Fregatte Bollwerk (schwerer Stufenkeil): massiger zweistufiger Panzerkeil mit Kragen und Seitenschuerzen, eingelassene Bruecke, zwei Raketenbatterien, Zwillings- und Massetreiberturm, Punktverteidigung, Schildwölbungen, Hangarschlitze, drei beringte Duesen", cargo=0)
def bastion(b):
    S = [(13.0, 0.0, 0.0), (10.6, 2.6, 1.3), (7.0, 4.6, 1.9), (1.0, 5.4, 2.1), (-9.6, 5.4, 2.1), (-11.8, 4.0, 1.6)]
    b.loft(S, M("Steel"), c=0.4)
    dress(b, S, seed=25.0, c=0.4)
    T = [(9.0, 0.0, 0.0, 1.9), (7.0, 1.9, 0.7, 2.2), (2.4, 3.2, 1.0, 2.4), (-7.6, 3.3, 1.0, 2.4), (-9.6, 2.6, 0.8, 2.2)]
    b.loft(T, M("Hull"))
    for y in (4.8, 0.0, -4.8):
        band(b, S, y, mat="Armor", g=1.06, t=0.9, c=0.4)
    for y in (6.0, 2.0, -2.0, -6.2):
        for s in (1, -1):
            b.box(0.55, 3.4, 2.1, M("Armor"), loc=(s * 5.55, y, -0.2), rot=(0, s * 14, 0))
    visor(b, T, 7.6, width=2.6)
    b.box(0.7, 14.0, 0.08, M("Accent"), loc=(0, -0.4, 3.42))
    b.mast((1.0, -3.6, 3.4), 2.2, light="Light")
    b.mast((-1.0, -3.6, 3.4), 1.6, light="LightRed")
    sensor(b, (0, -1.6, 3.4), r=0.45)
    hp_shafts(b, 3, (-1.0, 8.4, 1.42), rows=2, cols=3)
    hp_shafts(b, 4, (1.0, 8.4, 1.42), rows=2, cols=3)
    hp_turret(b, 5, (0, 5.4, 2.85), s=1.0, twin=True)
    hp_turret(b, 6, (0, 1.0, 2.95), s=1.05, length=2.0)
    hp_turret(b, 12, (4.0, 3.8, 2.0), s=0.6, kind="pd")
    for s in (1, -1):
        b.sphere(1.0, M("Light"), seg=8, rings=4, loc=(s * 5.5, -1.8, 1.0), scale=(0.6, 1.3, 0.6))
        b.hangar_slit(V(s * 5.55, 3.4, 0.6), (s, 0, 0), w=2.4, h=0.7)
        vents(b, (s * 3.2, -9.0, 2.1), n=4, length=1.6)
    for x, z in ((-2.0, 0.5), (2.0, 0.5), (0, -0.8)):
        engine(b, (x, -12.3, z), 1.2)
    thrusters(b, [(4.6, 8.0, 0.0), (-4.6, 8.0, 0.0)], s=0.6)
    navl(b, 5.9, -8.0, 0.5, s=0.2)


# =============================================================== neue Klassen (Durchlauf 4)
@ship("bomber", "S", "Bomber: schwerer Doppelrumpf-Jaeger, zwei dicke Triebwerksrohre mit mittlerer Panzerkanzel, zwei Torpedo-Schachtfelder bündig in den Verbindungsfluegeln, kraeftige Accent-Felder", name="Bomber", cargo=0)
def bomber(b):
    for s in (1, -1):
        G = [(3.4, 0.0, 0.0), (2.6, 0.55, 0.55), (1.4, 0.75, 0.75), (-4.2, 0.8, 0.8), (-4.8, 0.65, 0.65)]
        b.loft(G, M("Steel"), shape="round", n=8, x=s * 1.9)
        dress(b, G, seed=31.0 + s, x=s * 1.9, shape="round", n=2, bottom=False, sides=False)
        engine(b, (s * 1.9, -5.1, 0.0), 0.55, housing=False)
    S = [(5.0, 0.0, 0.0), (3.8, 0.6, 0.5), (1.6, 0.95, 0.7), (-2.0, 0.95, 0.7), (-3.0, 0.6, 0.5)]
    b.loft(S, M("Hull"), shape="hex")
    dress(b, S, seed=33.0, shape="hex", n=4, bottom=False)
    visor(b, S, 3.0, width=0.6, length=1.1, mat="Window")
    b.prism([(-2.0, 0.6), (2.0, 0.6), (2.0, -1.8), (-2.0, -1.8)], 0.22, M("Armor"), loc=(0, 0.2, -0.1))
    b.box(3.6, 0.5, 0.06, M("Accent"), loc=(0, 0.5, 0.03))
    for s in (1, -1):
        hp_shafts(b, 2 if s < 0 else 3, (s * 1.0, -0.6, 0.06), rows=2, cols=2, cell=0.3)
    navl(b, 2.75, -1.0, 0.0)


@ship("gunboat", "M", "Kanonenboot: gedrungener, breiter Panzerblock, in zwei Panzerwangen am Bug zwei schwere Langrohr-Geschuetze, Rueckenturm, kurzer, breiter Triebwerksblock mit vier Duesen, Technikgraben, Accent-Bug", name="Kanonenboot", cargo=0)
def gunboat(b):
    S = [(5.4, 1.4, 0.8), (4.4, 2.6, 1.2), (-2.6, 2.9, 1.3), (-4.6, 2.6, 1.15), (-5.4, 2.0, 0.9)]
    b.loft(S, M("Hull"), c=0.4)
    dress(b, S, seed=41.0, c=0.4)
    for s in (1, -1):
        W = [(6.6, 0.5, 0.6, -0.1), (5.6, 0.75, 0.85, -0.1), (1.0, 0.85, 0.95, -0.1), (0.2, 0.6, 0.7, -0.1)]
        b.loft(W, M("Armor"), x=s * 2.7)
        p = V(s * 2.7, 6.3, -0.1)
        b.cyl(0.42, 1.0, M("HullDark"), seg=8, loc=p, rot=(-90, 0, 0))
        b.strut(p, p + V(0, 3.6, 0), 0.2, M("HullDark"), seg=6)
        b.cyl(0.3, 0.6, M("HullDark"), seg=6, loc=p + V(0, 3.5, 0), rot=(-90, 0, 0))
        _hp(b, 2 if s < 0 else 3, "gun", p + V(0, 3.8, 0), (0, 1, 0), p.x)
        b.box(0.06, 3.2, 0.7, M("Accent"), loc=(s * 3.58, 3.4, -0.1))
    visor(b, S, 3.6, width=2.0, mat="Window")
    hp_turret(b, 4, (0, 0.4, 1.3), s=0.75, length=1.8)
    for x, z in ((-1.2, 0.45), (1.2, 0.45), (-1.2, -0.45), (1.2, -0.45)):
        engine(b, (x, -5.7, z), 0.5, housing=False)
    thrusters(b, [(3.0, -3.6, 0.6), (-3.0, -3.6, 0.6)], s=0.5)
    navl(b, 3.0, -4.6, 0.6)


@ship("destroyer", "L", "Zerstoerer: langgestreckter Dolchrumpf mit Rautenquerschnitt, Rueckenkamm mit drei schweren Zwillingstuermen in Reihe, zwei Tuerme unten, Brueckenblock im hinteren Drittel, Seitengraeben, Triebwerksbank mit vier Duesen", name="Zerstörer", cargo=0)
def destroyer(b):
    S = [(13.0, 0.0, 0.0), (8.0, 1.8, 1.0), (0.0, 3.0, 1.5), (-9.0, 3.4, 1.6), (-12.0, 3.0, 1.4), (-13.0, 2.6, 1.2)]
    b.loft(S, M("Hull"), shape="diamond")
    dress(b, S, seed=51.0, shape="diamond", n=14)
    K = [(9.0, 0.0, 0.0, 1.0), (6.0, 0.8, 0.5, 1.3), (-8.0, 1.2, 0.7, 1.4), (-9.6, 1.0, 0.6, 1.3)]
    b.loft(K, M("Steel"))
    greebles(b, 0, -1.0, 2.1, 1.4, 12.0, n=10, seed=52.0)
    for sx in (1, -1):
        stripe(b, K, 8.0, -7.0, x=sx * 0.95, width=0.3)
    for slot, y in ((3, 5.2), (4, 1.6), (5, -2.0)):
        hp_turret(b, slot, (0, y, 2.05), s=0.85, twin=True, length=2.0)
    hp_turret(b, 6, (0, 3.0, -1.55), s=0.7, up=False, length=1.8)
    hp_turret(b, 10, (0, -3.0, -1.55), s=0.55, up=False, kind="pd")
    T = [(-4.0, 0.0, 0.0, 2.2), (-4.6, 1.0, 0.7, 2.6), (-7.4, 1.2, 0.8, 2.6), (-8.0, 0.9, 0.6, 2.4)]
    b.loft(T, M("Armor"))
    visor(b, T, -4.8, width=1.6, mat="Window")
    b.mast((0.4, -6.4, 3.4), 1.8, light="LightRed")
    b.dish((-0.6, -6.6, 3.4), 0.6, M("ShipPanel"), rot=(-20, 0, 0))
    for s in (1, -1):
        b.hangar_slit(V(s * 3.3, -6.0, 0.0), (s, 0, 0), w=2.0, h=0.5)
    for x, z in ((-1.2, 0.6), (1.2, 0.6), (-1.2, -0.6), (1.2, -0.6)):
        engine(b, (x, -13.4, z), 0.75)
    thrusters(b, [(3.0, -10.0, 0.6), (-3.0, -10.0, 0.6)], s=0.6)
    navl(b, 3.45, -9.0, 0.0, s=0.2)


@ship("battlecruiser", "XL", "Schlachtkreuzer/Flaggschiff: Hammer-Bug (breiter Querbalken), langer Rumpf mit drei gestaffelten Terrassen, Kommandoturm mit Brueckenband achtern, Hangarbuchten in beiden Flanken, zwoelf Geschuetztuerme in Reihen, Punktverteidigung, Heck-Triebwerksbank mit sechs Duesen", name="Schlachtkreuzer", cargo=0)
def battlecruiser(b):
    S = [(18.0, 3.0, 1.6), (16.5, 6.2, 2.0), (14.6, 4.6, 2.0), (12.6, 2.8, 1.9), (6.0, 3.8, 2.4), (-8.0, 5.4, 2.8),
         (-15.0, 5.6, 2.8), (-18.0, 5.0, 2.4)]
    b.loft(S, M("Hull"), c=0.4)
    dress(b, S, seed=61.0, c=0.4, trench=False, n=12)
    T1 = [(11.0, 0.0, 0.0, 2.4), (9.0, 2.2, 0.8, 2.8), (-12.0, 3.6, 1.0, 3.0), (-15.0, 3.0, 0.8, 2.8)]
    b.loft(T1, M("Steel"))
    T2 = [(5.0, 0.0, 0.0, 3.6), (3.6, 1.6, 0.6, 4.0), (-11.0, 2.4, 0.8, 4.2), (-13.0, 2.0, 0.6, 4.0)]
    b.loft(T2, M("Armor"))
    T3 = [(-5.0, 0.0, 0.0, 4.8), (-6.0, 1.3, 1.0, 5.6), (-10.0, 1.4, 1.0, 5.6), (-10.8, 1.0, 0.8, 5.4)]
    b.loft(T3, M("Steel"))
    visor(b, T3, -6.4, width=1.8, length=0.8, mat="Window")
    b.mast((0.6, -8.4, 6.6), 2.6, light="LightRed")
    b.mast((-0.6, -8.8, 6.6), 1.8, light="Light")
    b.dish((0, -9.6, 6.6), 0.8, M("ShipPanel"), rot=(-30, 0, 0))
    for s in (1, -1):
        b.box(0.08, 12.0, 0.9, M("Accent"), loc=(s * 5.5, -6.0, 0.6))
        b.box(3.2, 0.8, 0.08, M("Accent"), loc=(s * 2.6, 16.4, 2.04))
        N = [(-6.0, 0.0, 0.0), (-7.0, 1.2, 1.2), (-16.6, 1.4, 1.4), (-18.2, 1.2, 1.2)]
        b.loft(N, M("Steel"), shape="hex", x=s * 6.6)
        b.beam(V(s * 5.4, -9.0, 0.0), V(s * 5.8, -9.0, 0.0), 1.6, M("HullDark"))
        b.beam(V(s * 5.4, -14.0, 0.0), V(s * 5.8, -14.0, 0.0), 1.6, M("HullDark"))
        b.box(0.08, 6.0, 0.8, M("Accent"), loc=(s * 8.02, -11.0, 0.0))
        engine(b, (s * 6.6, -18.6, 0.0), 1.0, housing=False)
        for y in (-2.0, -9.0):
            b.hangar_slit(V(s * 5.5, y, -0.6), (s, 0, 0), w=3.6, h=1.1)
        for slot, y in (((6, 7.0), (8, -6.0)) if s < 0 else ((7, 7.0), (9, -6.0))):
            hp_turret(b, slot, (s * 2.4, y, 3.7), s=0.85, twin=True, length=2.0)
        hp_shafts(b, 4 if s < 0 else 5, (s * 3.4, 15.4, 2.05), rows=2, cols=3, cell=0.4)
    hp_turret(b, 14, (0, 1.0, 4.4), s=0.7, kind="pd")
    greebles(b, 0, 9.5, 2.45, 4.0, 3.0, n=6, seed=63.0)
    greebles(b, 0, -6.0, -2.8, 6.0, 16.0, n=4, seed=64.0, up=False)
    E = [(-15.0, 4.6, 2.2), (-16.0, 5.0, 2.4), (-18.6, 5.0, 2.4)]
    b.loft(E, M("Armor"), c=0.4)
    for x, z in ((-3.0, 1.0), (0, 1.0), (3.0, 1.0), (-3.0, -1.0), (0, -1.0), (3.0, -1.0)):
        engine(b, (x, -19.0, z), 0.95)
    thrusters(b, [(5.4, 14.0, 0.0), (-5.4, 14.0, 0.0), (5.4, -12.0, 0.0), (-5.4, -12.0, 0.0)], s=0.8)
    navl(b, 6.1, 16.0, 0.0, s=0.26)


@ship("strip_miner", "L", "Grossschuerfer: massiger Kastenrumpf mit quer liegender, gezahnter Schneidwalze vor einem breiten Schuerfmaul, langer Erzraum mit 15 Segmenten (Tore beidseits), Foerdertunnel auf dem Ruecken, Kommandokanzel seitlich, Triebwerksbank mit vier Duesen", name="Großschürfer", cargo=300, role="Bergbau/Bau")
def strip_miner(b):
    n = round(300 / CARGO_PER_SEGMENT)
    y0 = 6.6
    S = [(10.0, 3.6, 1.6), (9.0, 3.9, 1.9), (y0 + 0.2, 3.4, 2.0), (y0 - n * SEG, 3.4, 2.0), (-10.6, 3.0, 1.8),
         (-12.0, 2.6, 1.5)]
    b.loft(S, M("Armor"), c=0.3)
    dress(b, S, seed=71.0, c=0.3, trench=False, sides=False, n=8)
    cargo_bay(b, S, y0, n, c=0.3, rib=False, frames=False)
    for k in range(0, n + 1, 5):
        band(b, S, y0 - SEG * k, mat="HullDark", g=1.08, t=0.35, c=0.3)
    drum(b, "Rotor_Drum", (0, 11.7, -0.2), 4.2, 1.6, rows=6)
    for s in (1, -1):
        b.box(0.8, 3.0, 3.4, M("Hazard"), loc=(s * 4.6, 10.8, -0.2))
    T = [(y0, 0.0, 0.0, 2.0), (y0 - 0.6, 0.8, 0.6, 2.3), (-8.0, 0.8, 0.6, 2.3), (-8.6, 0.0, 0.0, 2.0)]
    b.loft(T, M("Steel"), shape="round", n=8)
    C = [(8.8, 0.0, 0.0, 2.2), (8.2, 0.8, 0.7, 2.4), (6.4, 0.9, 0.8, 2.4), (5.8, 0.6, 0.5, 2.3)]
    b.loft(C, M("Hull"), shape="round", n=8, x=2.4)
    b.loft([(8.0, 0.7, 0.14, 2.75), (7.2, 0.82, 0.14, 2.85)], M("Window"), shape="round", n=8, x=2.4)
    b.box(0.08, 6.0, 1.2, M("Accent"), loc=(3.44, -6.0, 0.0))
    b.box(0.08, 6.0, 1.2, M("Accent"), loc=(-3.44, -6.0, 0.0))
    for x, z in ((-1.3, 0.7), (1.3, 0.7), (-1.3, -0.7), (1.3, -0.7)):
        engine(b, (x, -12.4, z), 0.8)
    thrusters(b, [(3.4, -9.0, 1.0), (-3.4, -9.0, 1.0)], s=0.6)
    navl(b, 3.5, 8.0, 1.0, s=0.2)


for _k in ("unit_miner_100", "unit_miner_60", "prospector", "pickaxe", "rockbreaker", "excavator", "foreman",
           "quarry", "rockbreaker_ii", "strip_miner"):
    ROLE[_k] = "Bergbau/Bau"
