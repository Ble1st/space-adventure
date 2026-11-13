"""Ein prozedurales Modell je Schiffsmodell (Key = shipModels[].key in default-world.json,
ohne unit_player) plus die kommenden Kampfschiffe corvette und missile_boat.

Gestaltungsregel: vollstaendig geschlossene Raumschiffe. Ein durchgehender Rumpf (Builder.loft aus
Querschnitten), oben und unten gestaltet, kein Deck und keine aufgesetzten Bruecken; Fracht nur als
geschlossene Rumpfsektionen mit Spanten und Ladeluken; Werkzeuge in den Bug integriert; Cockpit als
eingelassenes Fensterband. Hoechstens sechs Materialien je Schiff:
Hull, HullDark, Steel oder Armor, Accent, Engine, Light.

Achsen beim Bauen (Blender): Bug = +Y, oben = +Z, Steuerbord = +X. Nach glTF-Export (Y-up)
zeigt der Bug in Godot nach -Z (Godot-Vorwaerts), oben nach +Y.
Builder.finish(normalize="length") zentriert die Bounding-Box und normiert die Laenge auf 2.
"""

import math
from mathutils import Vector

from stationkit import Builder, material as M, polar, qdir

SHIPS = {}
SIZE_SCALE = {"S": 1.0, "M": 1.5, "L": 2.4}


def ship(key, size, idea, name=None):
    def deco(fn):
        SHIPS[key] = (fn, size, idea, name)
        return fn
    return deco


MAX_MATERIALS = 6
# Wenn ein Schiff mehr als sechs Materialien nutzt, werden diese der Reihe nach zusammengelegt.
MERGE = (("Steel", "Hull"), ("Armor", "Steel"), ("Accent", "Hull"))


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


def build(key):
    fn = SHIPS[key][0]
    b = Builder(key)
    fn(b)
    for src, dst in MERGE:
        if len(b.mats) <= MAX_MATERIALS:
            break
        merge_material(b, src, dst)
    return b.finish(normalize="length")


def V(x, y, z):
    return Vector((x, y, z))


# ---------------------------------------------------------------- Rumpf-Hilfen
def interp(secs, y):
    """(halbe Breite, halbe Hoehe, z) des Rumpfs an Stelle y (secs vom Bug zum Heck)."""
    for a, c in zip(secs, secs[1:]):
        if a[0] >= y >= c[0]:
            t = (a[0] - y) / (a[0] - c[0]) if a[0] != c[0] else 0.0
            za = a[3] if len(a) > 3 else 0.0
            zc = c[3] if len(c) > 3 else 0.0
            return a[1] + (c[1] - a[1]) * t, a[2] + (c[2] - a[2]) * t, za + (zc - za) * t
    last = secs[-1] if y < secs[-1][0] else secs[0]
    return last[1], last[2], (last[3] if len(last) > 3 else 0.0)


def band(b, secs, y, mat="HullDark", t=0.25, g=1.05, shape="oct", x=0.0, c=0.35):
    """Spant/Panzerkragen: kurzer, leicht dickerer Rumpfring an Stelle y."""
    w, h, z = interp(secs, y)
    b.loft([(y + t / 2, w * g, h * g, z), (y - t / 2, w * g, h * g, z)], M(mat), shape=shape, x=x, c=c)


def visor(b, secs, y, width=None, length=0.9, x=0.0, c=0.35):
    """Eingelassenes Cockpit-Fensterband auf der Oberseite, der Rumpfneigung folgend."""
    w, h, z = interp(secs, y)
    w2, h2, z2 = interp(secs, y - 0.4)
    slope = ((z2 + h2) - (z + h)) / -0.4
    a = math.degrees(math.atan(slope))
    width = width or w * (1 - c) * 1.6
    b.box(width, length, 0.08, M("Light"), loc=(x, y, z + h + 0.01), rot=(a, 0, 0))


def side_hatch(b, x, y, z, ly, lz, mat="Steel"):
    """Ladetor an einer senkrechten Bordwand (x = Aussenseite): erhabene Torflaeche mit Querrippen
    und Rahmen oben/unten (liest sich als Rolltor, nicht als Fenster)."""
    s = 1 if x > 0 else -1
    b.box(0.08, ly, lz, M(mat), loc=(x + s * 0.03, y, z))
    for k in range(3):
        dz = (k - 1) * lz * 0.3
        b.box(0.12, ly * 0.96, 0.06, M("HullDark"), loc=(x + s * 0.06, y, z + dz))
    for dz in (-lz / 2, lz / 2):
        b.box(0.12, ly + 0.14, 0.12, M("HullDark"), loc=(x + s * 0.05, y, z + dz))


def top_hatch(b, x, y, z, lx, ly, mat="Steel", down=False):
    """Ladeklappe auf Ober- oder Unterseite: erhabene Flaeche mit Laengsrippen und Endrahmen."""
    s = -1 if down else 1
    b.box(lx, ly, 0.08, M(mat), loc=(x, y, z + s * 0.03))
    for k in range(3):
        dx = (k - 1) * lx * 0.3
        b.box(0.06, ly * 0.96, 0.12, M("HullDark"), loc=(x + dx, y, z + s * 0.06))
    for dy in (-ly / 2, ly / 2):
        b.box(lx + 0.14, 0.12, 0.12, M("HullDark"), loc=(x, y + dy, z + s * 0.05))


def lens(b, p, r=0.3, d=(0, 1, 0)):
    """Werkzeug-/Laseroeffnung: Kragen mit leuchtender Linse."""
    p, d = Vector(p), Vector(d).normalized()
    b.cyl(r * 1.5, r * 0.8, M("HullDark"), seg=8, loc=p, rot=qdir(d))
    b.sphere(r, M("Light"), seg=6, rings=4, loc=p + d * r * 0.4)


def navl(b, x, y, z, s=0.14):
    for sx in (-x, x):
        b.sphere(s, M("Light"), seg=5, rings=3, loc=(sx, y, z))


def stripe(b, secs, y0, y1, x=0.0, width=0.35):
    """Accent-Streifen auf der Oberseite zwischen y0 und y1."""
    n = 3
    for i in range(n):
        ya = y0 + (y1 - y0) * i / n
        yb = y0 + (y1 - y0) * (i + 1) / n
        w, h, z = interp(secs, (ya + yb) / 2)
        wa, ha, za = interp(secs, ya)
        wb, hb, zb = interp(secs, yb)
        a = math.degrees(math.atan(((zb + hb) - (za + ha)) / (yb - ya))) if yb != ya else 0
        b.box(width, abs(yb - ya) + 0.05, 0.06, M("Accent"), loc=(x, (ya + yb) / 2, z + h + 0.02), rot=(a, 0, 0))


def gun(b, p, length, r=0.11):
    p = Vector(p)
    b.strut(p, p + V(0, length, 0), r, M("HullDark"), seg=5)
    b.cyl(r * 1.7, length * 0.15, M("HullDark"), seg=5, loc=p + V(0, length * 0.93, 0), rot=(-90, 0, 0))


def fin(b, pts, t, loc, mat="Accent", side=0.0):
    """Senkrechte Finne: pts = [(y_vor, z_hoch), ...] relativ zu loc; side kippt sie seitlich (Grad)."""
    b.prism(pts, t, M(mat), loc=loc, rot=(90, side, 90))


def turret(b, p, s=0.5, twin=True, up=True, facing=90):
    b.turret(p, facing=facing, s=s, twin=twin, up=up, hull="HullDark", barrel="Hull", light="Light")


# =============================================================== S: Einheitsschiffe
@ship("unit_trader_100", "S", "Standardfrachter gross: geschlossener, voller Kastenrumpf, Frachtsektion mit drei Spanten und zwei Ladeluken je Seite, Fensterband, kleiner Laserturm, zwei Heckduesen")
def unit_trader_100(b):
    S = [(5.0, 0.0, 0.0), (4.6, 0.8, 0.55), (3.6, 1.5, 1.0), (2.4, 1.8, 1.3), (-3.8, 1.8, 1.3), (-4.6, 1.5, 1.05),
         (-5.0, 1.2, 0.85)]
    b.loft(S, M("Hull"))
    for y in (1.6, -0.6, -2.8):
        band(b, S, y)
    for x in (-1.8, 1.8):
        for y in (0.5, -1.7):
            side_hatch(b, x, y, -0.05, 1.7, 1.3)
    visor(b, S, 3.7)
    stripe(b, S, 2.2, -4.2)
    turret(b, (0, 2.6, 1.3), s=0.32, twin=False)
    for x in (-0.85, 0.85):
        b.nozzle((x, -5.4, -0.1), 0.6)
    navl(b, 1.85, 2.4, 0.6)


@ship("unit_trader_60", "S", "Standardfrachter klein: schmaler geschlossener Rumpf mit verkleidetem Frachtbuckel auf dem Ruecken (Dachluke), Fensterband, Seitenfinnen, ein grosses Heckduese")
def unit_trader_60(b):
    S = [(3.8, 0.0, 0.0), (3.4, 0.7, 0.45), (2.4, 1.2, 0.75), (-2.8, 1.2, 0.75), (-3.6, 0.95, 0.6)]
    b.loft(S, M("Hull"))
    H = [(1.8, 0.0, 0.0, 0.7), (1.3, 0.9, 0.55, 0.85), (-2.3, 1.0, 0.6, 0.85), (-2.9, 0.7, 0.4, 0.8)]
    b.loft(H, M("Steel"))
    band(b, H, -0.5, g=1.06)
    top_hatch(b, 0, -0.5, 1.45, 1.1, 2.6)
    visor(b, S, 2.9)
    for s in (1, -1):
        b.prism([(0, 0), (1.0, -0.6), (1.0, -1.6), (0, -1.2)], 0.14, M("Accent"), loc=(s * 1.2, -1.8, -0.1),
                scale=(s, 1, 1))
    b.nozzle((0, -3.9, 0.0), 0.75)
    navl(b, 2.2, -2.9, -0.1)


@ship("unit_miner_100", "S", "Standardschuerfer gross: geschlossener Rumpf mit integriertem Laserbug (Linse in der Bugspitze), bauchiger Erzraum unten mit Bodenklappe, Fensterband, zwei Heckduesen")
def unit_miner_100(b):
    S = [(4.4, 1.1, 0.6, -0.1), (3.6, 1.6, 0.95), (2.0, 1.7, 1.1), (-3.8, 1.7, 1.1), (-4.8, 1.3, 0.9)]
    b.loft(S, M("Hull"))
    for sx in (1, -1):
        Pr = [(5.8, 0.0, 0.0, -0.2), (4.8, 0.3, 0.35, -0.15), (3.4, 0.45, 0.55, -0.1), (2.8, 0.4, 0.5, -0.1)]
        b.loft(Pr, M("Armor"), shape="diamond", x=sx * 1.25)
    K = [(2.4, 0.0, 0.0, -0.9), (1.6, 1.3, 0.7, -1.0), (-2.8, 1.5, 0.8, -1.0), (-3.6, 0.9, 0.5, -0.9)]
    b.loft(K, M("Armor"))
    band(b, K, -0.6, g=1.06)
    top_hatch(b, 0, -0.6, -1.8, 1.6, 2.6, down=True)
    lens(b, (0, 4.45, -0.1), r=0.45)
    for y in (3.0, -2.0):
        band(b, S, y)
    visor(b, S, 3.5)
    stripe(b, S, 1.8, -3.6)
    for x in (-0.85, 0.85):
        b.nozzle((x, -5.2, 0.0), 0.58)
    navl(b, 1.75, 1.0, 0.4)


@ship("unit_miner_60", "S", "Standardschuerfer klein: kurzer geschlossener Rumpf, Laser in einer Steuerbord-Gondel mit Linse, kleiner Erzraum unten mit Klappe, ein Heckduese")
def unit_miner_60(b):
    S = [(3.6, 0.0, 0.0), (3.2, 0.7, 0.45), (2.2, 1.25, 0.8), (-2.8, 1.25, 0.8), (-3.5, 0.95, 0.6)]
    b.loft(S, M("Hull"))
    P = [(3.4, 0.0, 0.0, -0.1), (2.8, 0.4, 0.4, -0.1), (-1.6, 0.45, 0.45, -0.1), (-2.2, 0.3, 0.3, -0.1)]
    b.loft(P, M("Armor"), shape="round", n=8, x=1.55)
    lens(b, (1.55, 3.45, -0.1), r=0.26)
    K = [(1.6, 0.0, 0.0, -0.7), (1.0, 0.9, 0.5, -0.75), (-2.0, 1.0, 0.55, -0.75), (-2.6, 0.6, 0.35, -0.7)]
    b.loft(K, M("Steel"))
    top_hatch(b, 0, -0.5, -1.32, 1.1, 1.8, down=True)
    band(b, S, -1.0)
    visor(b, S, 2.6)
    stripe(b, S, 1.6, -2.6, width=0.3)
    b.nozzle((0, -3.8, 0.0), 0.72)
    navl(b, 1.3, -2.4, 0.3)


# =============================================================== S: Skiffs, Kurier, Schuerfer, Kampf
@ship("pip", "S", "Kleiner Allzweck-Skiff: geschlossene, eiförmige Kapsel mit breitem Fensterband, zwei angeformte Seitenblasen (Tank links, Frachtfach rechts mit Luke), Linse unter dem Bug")
def pip(b):
    S = [(3.1, 0.0, 0.0), (2.6, 0.9, 0.75), (1.0, 1.5, 1.2), (-1.0, 1.45, 1.15), (-2.6, 1.0, 0.8), (-3.0, 0.7, 0.55)]
    b.loft(S, M("Hull"), shape="round", n=10)
    b.loft([(1.95, 1.3, 0.12, 0.65), (1.5, 1.45, 0.12, 0.8)], M("Light"), shape="round", n=10)
    for x, mat in ((-1.45, "Steel"), (1.45, "Armor")):
        P = [(1.0, 0.0, 0.0), (0.6, 0.6, 0.6), (-1.2, 0.65, 0.65), (-1.8, 0.0, 0.0)]
        b.loft(P, M(mat), shape="round", n=8, x=x)
    side_hatch(b, 2.08, -0.3, 0.0, 1.0, 0.6)
    band(b, S, -1.6, shape="round", g=1.04)
    lens(b, (0, 2.6, -0.75), r=0.22)
    b.prism([(0.6, 0), (-1.2, 0), (-1.6, 0.9), (-0.8, 0.9)], 0.12, M("Accent"), loc=(0, -1.6, 1.0),
            rot=(90, 0, 90))
    b.nozzle((0, -3.2, 0.0), 0.6)
    navl(b, 2.15, -0.3, 0.3)


@ship("nomad", "S", "Fracht-Skiff: flacher Keilrumpf mit zwei angeformten, geschlossenen Frachtgondeln (Luken aussen), Fensterband, keine Waffe, ein Heckduese")
def nomad(b):
    S = [(4.0, 0.0, 0.0), (3.2, 0.6, 0.35), (1.8, 1.1, 0.6), (-3.0, 1.1, 0.6), (-3.6, 0.8, 0.45)]
    b.loft(S, M("Hull"))
    for s in (1, -1):
        P = [(2.4, 0.0, 0.0, -0.05), (1.8, 0.75, 0.6, -0.05), (-2.6, 0.85, 0.65, -0.05), (-3.2, 0.6, 0.45, -0.05)]
        b.loft(P, M("Steel"), x=s * 1.55)
        side_hatch(b, s * 2.4, -0.4, -0.05, 2.4, 0.7, mat="Hull")
        band(b, P, 1.2, x=s * 1.55, g=1.06)
    visor(b, S, 2.6)
    stripe(b, S, 1.6, -3.0, width=0.3)
    b.nozzle((0, -3.8, 0.0), 0.62)
    for s in (1, -1):
        b.nozzle((s * 1.55, -3.4, -0.05), 0.4, housing=False)
    navl(b, 2.45, 1.2, 0.0)


@ship("prospector", "S", "Schneller Schuerf-Skiff: schlanker Pfeilrumpf, der in eine lange Laserlanze (Linse vorn) uebergeht, angeformter Frachtbuckel, gepfeilte Accent-Flossen")
def prospector(b):
    S = [(6.0, 0.18, 0.18, -0.25), (3.6, 0.3, 0.3, -0.2), (2.6, 0.7, 0.45), (0.6, 1.2, 0.6), (-2.6, 1.3, 0.6),
         (-3.4, 1.0, 0.5)]
    b.loft(S, M("Hull"))
    lens(b, (0, 6.05, -0.25), r=0.2)
    band(b, S, 3.3, g=1.15)
    H = [(0.8, 0.0, 0.0, 0.55), (0.2, 0.65, 0.4, 0.6), (-2.2, 0.75, 0.45, 0.6), (-2.8, 0.5, 0.3, 0.55)]
    b.loft(H, M("Steel"))
    top_hatch(b, 0, -1.0, 1.05, 0.8, 1.6)
    visor(b, S, 1.7)
    for s in (1, -1):
        b.prism([(0, 0), (2.2, -1.8), (2.2, -2.6), (0, -1.6)], 0.12, M("Accent"), loc=(s * 1.1, -0.6, -0.1),
                scale=(s, 1, 1))
    b.nozzle((0, -3.6, 0.0), 0.6)
    navl(b, 3.25, -3.0, -0.1)


@ship("swift", "S", "Kurier: schlanker Pfeilrumpf, an dem zwei uebergrosse, verkleidete Triebwerksgondeln anliegen, bündiges Fensterband, kleines Bugrohr")
def swift(b):
    S = [(6.0, 0.0, 0.0), (4.0, 0.45, 0.3), (1.0, 0.85, 0.5), (-3.6, 1.0, 0.55), (-4.4, 0.7, 0.4)]
    b.loft(S, M("Steel"), shape="diamond")
    for s in (1, -1):
        G = [(0.2, 0.0, 0.0), (-0.4, 0.75, 0.75), (-1.0, 0.95, 0.95), (-4.2, 0.95, 0.95), (-4.6, 0.8, 0.8)]
        b.loft(G, M("Hull"), shape="round", n=10, x=s * 1.55)
        band(b, G, -1.4, x=s * 1.55, shape="round", mat="Accent", g=1.06, t=0.35)
        b.nozzle((s * 1.55, -5.1, 0.0), 0.85, housing=False)
    visor(b, S, 2.6, width=0.55, length=1.4)
    stripe(b, S, 1.0, -3.4, width=0.25)
    gun(b, (0, 4.2, -0.15), 2.0, r=0.07)
    b.nozzle((0, -4.6, 0.0), 0.4, housing=False)
    navl(b, 2.55, -2.4, 0.0)


@ship("pickaxe", "S", "Leichter Schuerfer: geschlossener Rumpf mit angeformtem Spitzhacken-Querkopf am Bug (Dorn und Linse in der Mitte, Spitzen aussen), Erzraum unten mit Klappe, ein Heckduese")
def pickaxe(b):
    S = [(2.8, 0.5, 0.45), (2.2, 1.1, 0.75), (-3.0, 1.2, 0.8), (-3.8, 0.9, 0.6)]
    b.loft(S, M("Hull"))
    T = [(3.6, 0.0, 0.0), (3.2, 2.6, 0.45), (2.4, 2.9, 0.5), (2.0, 1.0, 0.4)]
    b.loft(T, M("Armor"), shape="diamond")
    b.lathe([(0.45, 0), (0.25, 1.0), (0, 1.8)], M("HullDark"), seg=6, loc=(0, 3.3, 0), rot=(-90, 0, 0))
    lens(b, (0, 4.9, 0.0), r=0.22)
    for s in (1, -1):
        b.lathe([(0.3, 0), (0, 1.2)], M("HullDark"), seg=5, loc=(s * 2.9, 2.6, 0), rot=qdir((s, 0.5, 0)))
    K = [(1.2, 0.0, 0.0, -0.7), (0.6, 0.95, 0.5, -0.75), (-2.4, 1.0, 0.55, -0.75), (-3.0, 0.6, 0.3, -0.7)]
    b.loft(K, M("Steel"))
    top_hatch(b, 0, -0.9, -1.32, 1.1, 1.8, down=True)
    band(b, S, -1.4)
    visor(b, S, 1.9)
    stripe(b, S, 1.2, -3.0, width=0.3)
    b.nozzle((0, -4.0, 0.0), 0.7)
    navl(b, 1.25, -2.0, 0.4)


@ship("corsair", "S", "Kaperschiff: zackiger Rautenrumpf mit nach vorn gepfeilten Klingenfluegeln, zwei Bugrohren, Dornfinne, eine angedeutete Flickplatte, zwei Heckduesen")
def corsair(b):
    S = [(5.0, 0.0, 0.0), (3.0, 0.7, 0.45), (0.0, 1.0, 0.6), (-3.2, 1.3, 0.55), (-3.8, 0.9, 0.4)]
    b.loft(S, M("HullDark"), shape="diamond")
    for s in (1, -1):
        b.prism([(0, 1.0), (3.4, 2.8), (3.0, 1.6), (1.6, -1.4), (0, -1.6)], 0.16, M("Hull"),
                loc=(s * 0.6, -0.4, 0.0), scale=(s, 1, 1))
        b.prism([(1.4, 1.6), (3.3, 2.7), (3.1, 2.1)], 0.2, M("Accent"), loc=(s * 0.6, -0.4, 0.0), scale=(s, 1, 1))
        gun(b, (s * 1.0, 1.4, -0.05), 2.6 if s > 0 else 2.0, r=0.1 if s > 0 else 0.13)
    fin(b, [(0.4, 0), (-2.4, 0), (-3.0, 1.6)], 0.12, (0, -0.2, 0.45), mat="Hull")
    visor(b, S, 2.2, width=0.55, length=1.0)
    b.box(0.9, 1.3, 0.08, M("Steel"), loc=(-0.4, -1.4, 0.4), rot=(0, 25, 8))
    for x in (-0.55, 0.55):
        b.nozzle((x, -3.9, 0.0), 0.45, housing=False)
    navl(b, 3.95, 1.8, 0.0)


@ship("falcon", "S", "Jaeger: scharfer Rautenrumpf mit eingelassener Kanzel, Deltafluegel, Doppelleitwerk mit Accent, zwei lange Bugwaffen, zwei Heckduesen")
def falcon(b):
    S = [(5.4, 0.0, 0.0), (3.0, 0.55, 0.45), (0.0, 0.85, 0.55), (-3.6, 0.95, 0.5), (-4.2, 0.7, 0.4)]
    b.loft(S, M("Steel"), shape="diamond")
    b.prism([(0.0, 2.0), (0.9, 1.0), (3.6, -3.2), (3.6, -3.8), (0.9, -3.6), (-0.9, -3.6), (-3.6, -3.8),
             (-3.6, -3.2), (-0.9, 1.0)], 0.18, M("Hull"), loc=(0, 0, -0.05))
    visor(b, S, 2.0, width=0.5, length=1.6)
    for s in (1, -1):
        gun(b, (s * 1.0, 0.6, -0.05), 3.4, r=0.1)
        fin(b, [(-0.2, 0), (-1.6, 0), (-1.9, 1.3), (-1.0, 1.3)], 0.12, (s * 1.7, -2.0, 0.05), side=-12 * s)
        b.box(1.4, 0.35, 0.05, M("Accent"), loc=(s * 2.5, -2.6, 0.06), rot=(0, 0, s * 35))
    for x in (-0.5, 0.5):
        b.nozzle((x, -4.35, 0.0), 0.42, housing=False)
    navl(b, 3.65, -3.5, 0.0)


# =============================================================== M: Frachter und Tanker
@ship("mule", "M", "Leichter Frachter: schlanke runde Zigarre mit zwei verdickten Frachtsektionen (Spanten, Ladeluken), Bugkanzel mit Fensterring, kleiner Laserturm, ein grosses Heckduese")
def mule(b):
    S = [(7.0, 0.0, 0.0), (6.0, 0.8, 0.8), (4.6, 1.3, 1.3), (3.4, 1.2, 1.2), (2.8, 1.75, 1.75), (-0.6, 1.75, 1.75),
         (-1.2, 1.3, 1.3), (-1.8, 1.75, 1.75), (-5.4, 1.75, 1.75), (-6.0, 1.3, 1.3), (-7.2, 1.15, 1.15)]
    b.loft(S, M("Hull"), shape="round", n=10)
    b.loft([(5.7, 1.0, 1.0), (5.2, 1.15, 1.15)], M("Light"), shape="round", n=10)
    for y in (1.1, -3.6):
        for s in (1, -1):
            side_hatch(b, s * 1.7, y, 0.0, 2.2, 1.6)
        band(b, S, y + 1.5, shape="round", g=1.04)
    band(b, S, 2.6, shape="round", mat="Accent", g=1.05, t=0.3)
    turret(b, (0, 4.0, 1.25), s=0.32, twin=False)
    b.nozzle((0, -7.6, 0.0), 1.05)
    navl(b, 1.8, -5.6, 0.0)


@ship("hauler", "M", "Breiter Frachter: bulliger, flacher Breitrumpf mit grosser Frachtsektion (vier Ladeluken je Seite, Dachluken), keilfoermiger Bug mit Fensterband, Laserturm, zwei Heckduesen")
def hauler(b):
    S = [(6.4, 0.0, 0.0), (5.6, 1.4, 0.55), (4.4, 2.9, 1.0), (3.4, 3.2, 1.25), (-5.6, 3.2, 1.25), (-6.4, 2.6, 1.0)]
    b.loft(S, M("Hull"))
    for y in (1.4, -1.6, -4.4):
        band(b, S, y)
    for y in (2.9, 0.0, -3.0):
        for s in (1, -1):
            side_hatch(b, s * 3.2, y, 0.0, 2.2, 1.1)
        top_hatch(b, -1.2, y, 1.27, 1.6, 2.2)
        top_hatch(b, 1.2, y, 1.27, 1.6, 2.2)
    visor(b, S, 4.6, width=2.6)
    b.box(0.5, 9.0, 0.06, M("Accent"), loc=(0, -1.2, 1.3))
    turret(b, (0, 4.1, 1.1), s=0.4, twin=False)
    for x in (-1.6, 1.6):
        b.nozzle((x, -6.7, 0.0), 0.75)
    navl(b, 3.3, 3.4, 0.3)


@ship("tinker", "M", "Mehrzweck asymmetrisch: geschlossener Mittelrumpf, an Steuerbord eine angeformte Frachtgondel mit Luke, an Backbord ein eng anliegender, gefalteter Laserarm mit Linse, Laserturm")
def tinker(b):
    S = [(6.0, 0.0, 0.0), (5.0, 0.8, 0.6), (3.6, 1.5, 0.95), (-4.8, 1.5, 0.95), (-5.6, 1.1, 0.75)]
    b.loft(S, M("Hull"))
    P = [(3.0, 0.0, 0.0), (2.2, 1.0, 0.85), (-3.8, 1.1, 0.9), (-4.6, 0.7, 0.6)]
    b.loft(P, M("Steel"), shape="round", n=8, x=2.3)
    side_hatch(b, 3.38, -0.8, 0.0, 2.6, 0.9, mat="Hull")
    A = [(5.4, 0.0, 0.0, -0.1), (4.6, 0.38, 0.38, -0.1), (-1.6, 0.42, 0.42, -0.1), (-2.2, 0.3, 0.3, -0.1)]
    b.loft(A, M("Armor"), shape="round", n=8, x=-1.75)
    for y in (2.8, 0.2):
        b.box(0.5, 0.35, 0.5, M("HullDark"), loc=(-1.6, y, -0.1))
    lens(b, (-1.75, 5.45, -0.1), r=0.24)
    for y in (2.0, -2.4):
        band(b, S, y)
    visor(b, S, 4.2)
    stripe(b, S, 3.2, -4.6, width=0.35)
    turret(b, (0, 0.6, 0.95), s=0.4, twin=False)
    b.nozzle((0, -6.0, 0.0), 0.9)
    navl(b, 1.5, -4.0, 0.5)


@ship("cistern", "M", "Tanker: bauchiger geschlossener Rumpf, sechs Druckkoerper-Wölbungen in zwei Reihen unter Panzerkragen, Bugkanzel mit Fensterband, kleiner Laserturm, grosses Heckduese")
def cistern(b):
    S = [(7.0, 0.0, 0.0), (6.0, 0.9, 0.7), (4.6, 1.6, 1.1), (3.4, 1.9, 1.3), (-5.6, 1.9, 1.3), (-6.6, 1.4, 1.0)]
    b.loft(S, M("Hull"), shape="round", n=10)
    for y in (1.8, -1.4, -4.6):
        for s in (1, -1):
            b.sphere(1.55, M("Steel"), seg=10, rings=6, loc=(s * 1.3, y, 0.25), scale=(1, 1.05, 0.95))
            b.lathe([(1.58, -0.18), (1.58, 0.18)], M("HullDark"), seg=10, loc=(s * 1.3, y, 0.25))
        band(b, S, y + 1.6, shape="round", g=1.08, t=0.3)
    b.loft([(5.6, 1.0, 0.75), (5.0, 1.25, 0.92)], M("Light"), shape="round", n=10)
    band(b, S, 3.6, shape="round", mat="Accent", g=1.06, t=0.3)
    turret(b, (0, 4.2, 1.15), s=0.33, twin=False)
    b.nozzle((0, -7.2, 0.0), 1.0)
    navl(b, 2.9, -1.4, 0.3)


@ship("behemoth", "L", "Schwerer Frachter: massiger, langer Kastenrumpf mit fuenf geschlossenen Frachtsegmenten (Panzerkragen dazwischen, grosse Seitentore und Dachluken), Bugsektion mit Fensterband, Laserturm, drei Heckduesen")
def behemoth(b):
    S = [(12.0, 0.0, 0.0), (11.0, 1.4, 1.0), (9.6, 2.4, 1.8), (8.4, 2.6, 2.0), (-12.4, 2.6, 2.0), (-13.4, 2.0, 1.6)]
    b.loft(S, M("Hull"), c=0.25)
    for y in (7.6, 3.4, -0.8, -5.0, -9.2):
        band(b, S, y, mat="Armor", g=1.1, t=0.7, c=0.25)
    for i, y in enumerate((5.5, 1.3, -2.9, -7.1, -11.0)):
        for s in (1, -1):
            side_hatch(b, s * 2.6, y, 0.0, 3.0, 2.2, mat="Steel")
        top_hatch(b, 0, y, 2.02, 2.6, 3.0, mat="Steel" if i % 2 else "Accent")
    visor(b, S, 9.9, width=3.0, length=0.9)
    turret(b, (0, 8.6, 2.0), s=0.5, twin=False)
    for x, z in ((-1.3, 0.8), (1.3, 0.8), (0, -0.8)):
        b.nozzle((x, -13.8, z), 0.95)
    navl(b, 2.85, 7.6, 1.0, s=0.2)


# =============================================================== M: Schuerfer, Bau, Brecher
@ship("rockbreaker", "M", "Schwerer Schuerfer: wuchtiger Panzerrumpf, aus dessen Bugkragen ein gerippter Bohr-Laserkegel waechst, zwei bauchige Erzraeume unten mit Klappen, grosses Heckduese")
def rockbreaker(b):
    S = [(4.4, 1.5, 1.2), (3.4, 2.2, 1.5), (-5.0, 2.2, 1.5), (-6.0, 1.6, 1.1)]
    b.loft(S, M("Armor"))
    band(b, S, 4.2, mat="HullDark", g=1.12, t=0.6)
    prof = [(1.6, 0)]
    for k in range(3):
        prof += [(1.6 - k * 0.45, 0.3 + k * 0.8), (1.35 - k * 0.45, 0.8 + k * 0.8)]
    prof += [(0.3, 3.0), (0, 3.3)]
    b.lathe(prof, M("Steel"), seg=8, loc=(0, 4.4, 0.0), rot=(-90, 0, 0))
    lens(b, (0, 7.75, 0.0), r=0.26)
    for s in (1, -1):
        K = [(2.4, 0.0, 0.0, -1.2), (1.6, 0.9, 0.7, -1.4), (-4.0, 0.95, 0.75, -1.4), (-4.8, 0.6, 0.45, -1.3)]
        b.loft(K, M("Hull"), x=s * 1.2)
        top_hatch(b, s * 1.2, -1.2, -2.17, 1.2, 3.4, down=True)
    for y in (1.0, -2.6):
        band(b, S, y)
    visor(b, S, 3.0, width=2.4)
    stripe(b, S, 2.6, -5.0, width=0.45)
    b.nozzle((0, -6.4, 0.2), 1.2)
    navl(b, 2.25, -3.0, 0.8)


@ship("excavator", "M", "Grossschuerfer Mk3: Hammerkopf-Bug, in dessen Stirn ein Zahnring um einen leuchtenden Laserkern eingelassen ist, zwei angeformte Erz-Seitensponsons mit Luken, zwei Heckduesen")
def excavator(b):
    S = [(3.0, 1.6, 1.2), (-5.4, 1.8, 1.3), (-6.2, 1.3, 1.0)]
    b.loft(S, M("Armor"))
    Hd = [(5.6, 3.4, 1.7), (4.6, 3.6, 1.8), (3.0, 2.6, 1.5), (2.4, 1.8, 1.3)]
    b.loft(Hd, M("Hull"))
    b.ring(2.3, 0.6, 0.6, M("Steel"), seg=10, loc=(0, 5.7, 0.0), rot=(90, 0, 0))
    for k in range(10):
        a = math.radians(36 * k + 18)
        d = V(math.cos(a), 0, math.sin(a))
        b.lathe([(0.25, 0), (0, 0.8)], M("HullDark"), seg=4, loc=V(0, 5.9, 0) + d * 2.0, rot=qdir(V(0, 1, 0) + d * 0.3))
    b.lathe([(1.2, 0), (0, 0.01)], M("Light"), seg=8, loc=(0, 5.65, 0.0), rot=(-90, 0, 0))
    for s in (1, -1):
        K = [(2.0, 0.0, 0.0, -0.2), (1.2, 1.0, 1.0, -0.2), (-4.4, 1.1, 1.1, -0.2), (-5.2, 0.7, 0.7, -0.2)]
        b.loft(K, M("Hull"), x=s * 2.5)
        side_hatch(b, s * 3.6, -1.6, -0.2, 3.6, 1.2, mat="Steel")
        band(b, K, -0.2, x=s * 2.5)
    visor(b, Hd, 4.2, width=3.0)
    stripe(b, S, 2.6, -5.2, width=0.5)
    for x in (-1.0, 1.0):
        b.nozzle((x, -6.5, 0.0), 0.85)
    navl(b, 3.65, 4.6, 0.6)


@ship("foreman", "M", "Bauschiff: geschlossener Rumpf, auf dem Ruecken ein eng anliegender, zweifach gefalteter Manipulatorarm mit Greifer, seitliche Materialtore, Fensterband, zwei Heckduesen")
def foreman(b):
    S = [(5.8, 0.0, 0.0), (5.0, 1.1, 0.7), (3.8, 2.1, 1.1), (-5.0, 2.1, 1.1), (-5.8, 1.6, 0.85)]
    b.loft(S, M("Hull"))
    for y in (1.6, -2.4):
        band(b, S, y, mat="Accent", g=1.05, t=0.35)
    for y in (-0.4, -3.8):
        for s in (1, -1):
            side_hatch(b, s * 2.1, y, 0.0, 2.6, 1.1, mat="Steel")
    j0, j1, j2 = V(0.8, -4.2, 1.35), V(0.8, 2.4, 1.55), V(-0.8, -2.0, 1.75)
    b.box(1.0, 1.0, 0.6, M("HullDark"), loc=j0)
    b.beam(j0, j1, 0.45, M("Armor"))
    b.sphere(0.42, M("HullDark"), seg=6, rings=4, loc=j1)
    b.beam(j1, j2, 0.36, M("Armor"))
    b.sphere(0.34, M("HullDark"), seg=6, rings=4, loc=j2)
    b.claw(j2 + V(0, 0, 0.0), (0, -1, 0.15), s=0.5, mat="Accent")
    visor(b, S, 4.4, width=2.2)
    b.claw(V(0, 5.7, -0.2), (0, 1, 0), s=0.55, mat="Accent")
    for x in (-1.1, 1.1):
        b.nozzle((x, -6.1, 0.0), 0.75)
    navl(b, 2.15, 3.0, 0.4)


@ship("quarry", "M", "Asteroidenbrecher: gepanzerter Rumpf, dessen Bug in eine Spaltzange aus zwei angeformten Backen ausläuft, leuchtender Spaltemitter dazwischen, kleiner Erzraum unten, Heckduese")
def quarry(b):
    S = [(3.4, 1.6, 1.1), (2.4, 2.1, 1.3), (-4.8, 2.1, 1.3), (-5.6, 1.5, 1.0)]
    b.loft(S, M("Armor"))
    for s in (1, -1):
        J = [(7.0, 0.0, 0.0), (6.2, 0.25, 0.35), (5.0, 0.4, 0.55), (3.0, 0.65, 0.85), (2.6, 0.7, 0.9)]
        b.loft(J, M("Steel"), x=s * 1.2)
    b.cyl(0.55, 1.6, M("HullDark"), seg=6, loc=(0, 4.0, 0.0), rot=(-90, 0, 0))
    b.sphere(0.5, M("Light"), seg=6, rings=4, loc=(0, 4.9, 0.0))
    b.box(0.1, 2.0, 0.1, M("Light"), loc=(0, 6.1, 0.0))
    K = [(1.6, 0.0, 0.0, -1.0), (1.0, 1.3, 0.6, -1.1), (-3.4, 1.4, 0.65, -1.1), (-4.0, 0.9, 0.4, -1.0)]
    b.loft(K, M("Hull"))
    top_hatch(b, 0, -1.0, -1.77, 1.8, 2.8, down=True)
    band(b, S, 2.6, g=1.08, t=0.5)
    band(b, S, -2.0)
    visor(b, S, 1.6, width=2.2)
    stripe(b, S, 1.4, -4.6, width=0.45)
    b.nozzle((0, -6.0, 0.0), 1.1)
    navl(b, 2.15, -2.8, 0.6)


@ship("rockbreaker_ii", "M", "Grossbrecher: breiter Panzerrumpf mit Spaltzange am Bug, eingelassener Schuerflaser-Kuppel auf dem Ruecken, zwei runde Erzsponsons mit Spanten, zwei Heckduesen")
def rockbreaker_ii(b):
    S = [(3.8, 1.9, 1.2), (2.8, 2.6, 1.4), (-5.6, 2.6, 1.4), (-6.4, 1.9, 1.0)]
    b.loft(S, M("Armor"))
    for s in (1, -1):
        J = [(6.6, 0.0, 0.0), (5.8, 0.3, 0.35), (4.6, 0.45, 0.55), (3.2, 0.6, 0.8), (2.8, 0.65, 0.85)]
        b.loft(J, M("Steel"), x=s * 1.3)
        K = [(2.6, 0.0, 0.0), (1.8, 1.05, 1.05), (-4.6, 1.1, 1.1), (-5.4, 0.7, 0.7)]
        b.loft(K, M("Hull"), shape="round", n=8, x=s * 3.3)
        for y in (0.4, -2.4):
            band(b, K, y, x=s * 3.3, shape="round", g=1.08)
        top_hatch(b, s * 3.3, -1.0, 1.12, 1.2, 1.8)
    b.sphere(0.45, M("Light"), seg=6, rings=4, loc=(0, 4.9, 0.0))
    b.cyl(0.5, 1.4, M("HullDark"), seg=6, loc=(0, 4.1, 0.0), rot=(-90, 0, 0))
    b.sphere(1.1, M("Hull"), seg=8, rings=4, loc=(0, 0.8, 1.35), half=True)
    lens(b, (0, 2.0, 1.75), r=0.28)
    b.strut(V(0, 1.0, 1.75), V(0, 2.0, 1.75), 0.22, M("HullDark"), seg=6)
    band(b, S, 2.4, g=1.07, t=0.5)
    visor(b, S, 3.2, width=2.8)
    stripe(b, S, -0.4, -5.6, width=0.5)
    for x in (-1.1, 1.1):
        b.nozzle((x, -6.7, 0.0), 0.9)
    navl(b, 4.45, -1.0, 0.0)


# =============================================================== M/L: Kampfschiffe
@ship("warden", "M", "Patrouillenboot: kompakter, breiter Pfeilkeil (Schildtraeger) mit zwei Schildemitter-Wölbungen an den Fluegelspitzen, Zwillingsturm vorn und Massetreiberturm hinten, Signal-Lichtleiste, zwei Heckduesen")
def warden(b):
    S = [(6.0, 0.0, 0.0), (4.0, 1.6, 0.7), (1.0, 3.4, 0.9), (-2.4, 4.4, 0.8), (-3.8, 4.4, 0.7), (-4.6, 2.6, 0.75)]
    b.loft(S, M("Steel"), c=0.5)
    T = [(4.0, 0.0, 0.0, 0.5), (2.8, 0.9, 0.5, 0.6), (-3.0, 1.3, 0.6, 0.6), (-4.0, 1.0, 0.45, 0.55)]
    b.loft(T, M("Hull"))
    for s in (1, -1):
        b.sphere(0.85, M("Light"), seg=8, rings=4, loc=(s * 4.3, -3.1, 0.0), scale=(0.7, 1.2, 0.8))
        b.box(0.5, 2.2, 0.25, M("HullDark"), loc=(s * 4.05, -3.1, 0.0))
        b.box(2.6, 0.25, 0.06, M("Accent"), loc=(s * 2.4, -1.2, 0.86), rot=(0, 0, s * 24))
    visor(b, T, 2.6, width=1.0)
    b.box(1.2, 0.25, 0.12, M("Light"), loc=(0, 1.6, 1.25))
    turret(b, (0, 1.0, 1.15), s=0.55, twin=True)
    turret(b, (0, -2.4, 1.15), s=0.6, twin=False)
    turret(b, (0, -0.6, -0.7), s=0.45, twin=False, up=False)
    for x in (-1.2, 1.2):
        b.nozzle((x, -4.9, 0.1), 0.75)
    navl(b, 4.45, -3.9, 0.3)


@ship("corvette", "M", "Korvette (Geleitschutz): lang gestreckter Doppelrumpf, Mittelsektion mit Bruecken-Fensterband, zwei Tuerme oben, je ein Bugrohr pro Rumpf, zwei Punktverteidigungstuerme, Panzerkragen, vier Heckduesen", name="Korvette")
def corvette(b):
    for s in (1, -1):
        H = [(7.2, 0.0, 0.0), (6.0, 0.6, 0.6), (4.0, 1.0, 0.95), (-5.6, 1.05, 1.0), (-6.6, 0.8, 0.8)]
        b.loft(H, M("Steel"), x=s * 2.0)
        for y in (2.6, -0.6, -3.8):
            band(b, H, y, x=s * 2.0, mat="Armor", g=1.12, t=0.6)
        gun(b, (s * 2.0, 6.6, 0.0), 1.6, r=0.14)
        turret(b, (s * 2.0, -2.2, 0.95), s=0.38, twin=False, facing=90 + s * 60)
        for x in (s * 1.6, s * 2.4):
            b.nozzle((x, -7.0, 0.0), 0.5, housing=False)
    C = [(4.4, 0.0, 0.0, 0.2), (3.2, 1.2, 0.6, 0.25), (-4.0, 1.6, 0.75, 0.25), (-5.0, 1.2, 0.6, 0.2)]
    b.loft(C, M("Hull"))
    visor(b, C, 2.8, width=1.4)
    stripe(b, C, 2.0, -4.4, width=0.5)
    turret(b, (0, 1.4, 0.95), s=0.55)
    turret(b, (0, -2.8, 0.95), s=0.55)
    b.mast((0.5, -0.7, 0.95), 1.4, light="Light")
    navl(b, 3.05, -5.0, 0.0)


@ship("missile_boat", "M", "Raketenboot: schlanker Nadelrumpf mit wenig Panzerung, zwei bündig angeformte Werfersponsons mit je 3x4 Startschaechten in der Stirn, Fensterband, zwei Heckduesen", name="Raketenboot")
def missile_boat(b):
    S = [(7.5, 0.0, 0.0), (4.6, 0.6, 0.45), (0.0, 1.0, 0.6), (-6.2, 1.15, 0.6), (-6.8, 0.8, 0.45)]
    b.loft(S, M("Hull"), shape="diamond")
    visor(b, [(7.5, 0.0, 0.0), (4.6, 0.6, 0.45), (0.0, 1.0, 0.6)], 3.4, width=0.5, length=1.4)
    for s in (1, -1):
        P = [(2.4, 0.85, 0.75), (1.8, 0.95, 0.85), (-3.6, 0.95, 0.85), (-4.4, 0.6, 0.55)]
        b.loft(P, M("Steel"), c=0.2, x=s * 1.85)
        for i in range(3):
            for j in range(4):
                b.box(0.34, 0.12, 0.26, M("HullDark"), loc=(s * 1.85 + (i - 1) * 0.55, 2.42, (j - 1.5) * 0.4))
        b.box(1.95, 0.08, 1.75, M("Accent"), loc=(s * 1.85, 2.38, 0.0))
        band(b, P, -0.8, x=s * 1.85, c=0.2, g=1.06)
    stripe(b, S, 2.0, -5.6, width=0.3)
    for x in (-0.55, 0.55):
        b.nozzle((x, -6.9, 0.0), 0.5, housing=False)
    navl(b, 2.85, -2.0, 0.0)


@ship("bastion", "L", "Fregatte Bollwerk: massiger, zweistufiger Panzerkeil mit Panzerkragen und Seitenschuerzen, eingelassene Bruecke, zwei Raketenwerfer-Batterien, Zwillings- und Massetreiberturm, Punktverteidigung, Schildemitter-Wölbungen, Hangarschlitze, drei Heckduesen")
def bastion(b):
    S = [(11.0, 0.0, 0.0), (9.0, 2.2, 1.1), (6.0, 3.9, 1.6), (1.0, 4.6, 1.8), (-8.0, 4.6, 1.8), (-9.8, 3.4, 1.4)]
    b.loft(S, M("Steel"), c=0.4)
    T = [(7.6, 0.0, 0.0, 1.6), (6.0, 1.6, 0.6, 1.9), (2.0, 2.7, 0.9, 2.0), (-6.4, 2.8, 0.9, 2.0), (-8.0, 2.2, 0.7, 1.9)]
    b.loft(T, M("Hull"))
    for y in (4.0, 0.0, -4.0):
        band(b, S, y, mat="Armor", g=1.06, t=0.8, c=0.4)
    for y in (5.0, 1.6, -1.8, -5.2):
        for s in (1, -1):
            b.box(0.5, 3.0, 1.8, M("Armor"), loc=(s * 4.75, y, -0.2), rot=(0, s * 14, 0))
    visor(b, T, 6.4, width=2.2)
    b.box(0.6, 12.0, 0.08, M("Accent"), loc=(0, -0.4, 2.92))
    b.mast((0.9, -3.0, 2.9), 2.0, light="Light")
    turret(b, (0, 4.4, 2.9), s=0.85)
    turret(b, (0, 0.8, 2.9), s=0.9, twin=False)
    for s in (1, -1):
        c = V(s * 1.9, -5.8, 3.3)
        b.box(1.6, 2.4, 0.8, M("HullDark"), loc=c)
        for i in range(2):
            for j in range(3):
                b.box(0.4, 0.1, 0.18, M("Light"), loc=c + V((i - 0.5) * 0.6, 1.22, (j - 1) * 0.22))
        turret(b, (s * 3.4, 3.2, 1.75), s=0.45, twin=False, facing=90 + s * 60)
        b.sphere(0.9, M("Light"), seg=8, rings=4, loc=(s * 4.7, -1.6, 0.9), scale=(0.6, 1.2, 0.6))
        b.hangar_slit(V(s * 4.65, 3.0, 0.5), (s, 0, 0), w=2.2, h=0.6)
    turret(b, (0, 2.0, -1.8), s=0.6, twin=True, up=False)
    for x, z in ((-1.8, 0.4), (1.8, 0.4), (0, -0.7)):
        b.nozzle((x, -10.2, z), 1.1)
    navl(b, 4.9, -6.8, 0.4, s=0.2)
