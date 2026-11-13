"""Ein prozedurales Modell je Stationstyp (Key = stationTypes[].key in default-world.json).

Jede Bau-Funktion arbeitet in einem freien Massstab (Koerper etwa Radius 10 in XY);
Builder.finish() zentriert und normiert danach auf Radius 1 in der Spielebene.
Blender-Koordinaten: XY = Spielebene, +Z = "oben" (nach glTF-Export +Y).
"""

import math
from mathutils import Vector

from stationkit import Builder, material as M, polar, qdir

MODELS = {}


def model(key, idea):
    def deco(fn):
        MODELS[key] = (fn, idea)
        return fn
    return deco


def build(key):
    fn, _ = MODELS[key]
    b = Builder(key)
    fn(b)
    return b.finish()


def V(x, y, z):
    return Vector((x, y, z))


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


@model("steelworks", "Kompakter Hochofen-Kern: gepanzerte, achteckige Schmelztrommel mit zwei glutroten Schlitzbaendern und Panzerguertel, Giesskegel oben und unten, vier kurze, breite Radiatorfluegel, zwei Erzgreifer mit Brocken, haengende Barrenkapseln, Punktverteidigung")
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
        b.claw(d * 5.6, d, s=1.0)
        b.rock(1.3, M("Rock"), seed=3.3 + s, seg=8, rings=6, loc=d * 7.9 + V(0, 0, 0.3 * s), amp=0.35)
    for x, y, a in polar(3, 2.4, 0):
        b.capsule(V(x, y, -6.3), (math.cos(math.radians(a + 90)), math.sin(math.radians(a + 90)), 0),
                  2.0, 0.5, M("Copper"), seg=8)
    b.pd((0, 3.6, 3.0), facing=90, s=1.1)
    b.pd((0, -3.6, -3.0), up=False, facing=-90, s=1.1)
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
    b.pd((0, 1.6, 1.9), facing=0)
    b.nav_lights([(6.4, 2.9, 0), (6.4, -2.9, 0), (-6.4, 2.9, 0), (-6.4, -2.9, 0)], mat="LightRed", s=0.22)


@model("copper_smelter", "Rotierender Schmelzring: kupferbeschlagener Torus mit gluehender Schmelzrinne um eine Nabe, Kupferspeichen, haengende Barrenkapseln, Erzgreifer, Kuehlrippen oben und unten")
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
    b.claw(d * (R + 2.4), d, s=1.1)
    b.rock(1.4, M("Rock"), seed=5.1, seg=8, rings=6, loc=d * (R + 5.2), amp=0.35)
    b.dock(V(0, 0, 4.0), (0, 0, 1), r=0.5, L=1.4)
    b.nav_lights([(x, y, 1.4) for x, y, a in polar(4, R, 45)], mat="LightRed", s=0.22)


@model("wafer_fab", "Flache Reinraum-Anlage: zwei weite Waferscheiben mit blau leuchtenden Raendern und Chipfeldern um eine kurze Achse, gepanzerter Sechseck-Unterbau mit Schleuse, Glaskuppel oben, Sensormast")
def wafer_fab(b):
    b.cyl(1.4, 7.0, M("Steel"), seg=8)
    for z, r in ((1.1, 6.6), (-0.9, 5.0)):
        b.lathe([(1.3, z - 0.3), (r - 0.5, z - 0.3), (r, z), (r - 0.5, z + 0.25), (1.3, z + 0.25)],
                M("Accent"), seg=16, closed=True)
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


@model("titanium_works", "Liegende, dicke Brechertrommel mit gelben Warnstreifen und Panzerrippen als Rumpf, sechseckige Verarbeitungsbloecke oben und unten, kurzer Erzkaefig am Bug, gepanzerte Andockschleuse am Heck, kurze Radiatoren")
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
    c = V(-10.2, 0, 0)
    e = 1.6
    for sx in (-e, e):
        for sy in (-e, e):
            b.beam(c + V(sx, sy, -e), c + V(sx, sy, e), 0.2, M("Hazard"))
            b.beam(c + V(sx, -e, sy), c + V(sx, e, sy), 0.2, M("Hazard"))
            b.beam(c + V(-e, sx, sy), c + V(e, sx, sy), 0.2, M("Hazard"))
    for i, (o, r) in enumerate(((V(0.3, 0.4, 0.2), 1.0), (V(-0.6, -0.5, -0.4), 0.75))):
        b.rock(r, M("Rock"), seed=2 + i, seg=7, rings=5, loc=c + o, amp=0.35)
    b.dock(V(8.0, 0, 0), (1, 0, 0), r=0.9, L=1.6, mat="Armor")
    b.pd((4.0, 0, 3.4), facing=0)
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
    b.pd((0, 1.0, 3.2), facing=90, s=0.7)


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
    b.pd((0, 1.5, 3.4), facing=90, s=0.8)
    b.pd((0, -1.5, -3.4), up=False, facing=-90, s=0.8)


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
    b.pd((-2.25, 0, 1.5), facing=90)
    b.pd((2.25, 0, -1.5), up=False, facing=-90)
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


@model("goods_factory", "Kompakter Fertigungsring: acht gepanzerte Werkmodule mit Fensterbaendern auf einem Ring um eine Nabe, Containercluster direkt an vier Andockstutzen aussen, Schlepper")
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
    cols = ["Container", "Accent", "Hazard", "Steel", "Armor"]
    for i, (x, y, a) in enumerate(polar(4, R, 22.5)):
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        n = V(-d.y, d.x, 0)
        b.beam(d * (R + 1.0), d * (R + 1.8), 0.7, M("HullDark"))
        c = d * (R + 3.1)
        k = i
        for u in (-1, 1):
            for z in (-1, 0, 1):
                if (u + z + i) % 4 == 3:
                    continue
                b.box(2.4, 1.2, 1.15, M(cols[k % 5]), rot=(0, 0, a + 90),
                      loc=c + n * u * 1.25 + V(0, 0, z * 1.2))
                k += 1
        b.nav_lights([d * (R + 4.5) + V(0, 0, 1.8)], s=0.2)
    for z in (1, -1):
        b.dock(V(0, 0, z * 3.8), (0, 0, z), r=0.7, L=1.4, mat="Armor")
    b.pd(V(0, R, 1.3), facing=90, s=0.9)
    b.pd(V(0, -R, -1.3), up=False, facing=-90, s=0.9)


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
@model("shipyard", "Offenes Trockendock aus Gitterrahmen mit halbfertigem Schiffsrumpf, zwei Kraene, Leitstand, zwei Geschuetztuerme")
def shipyard(b):
    L, W, H = 8.0, 3.4, 2.8
    corners = [(x, y, z) for x in (-L, L) for y in (-W, W) for z in (-H, H)]
    for x in (-L, L):
        for y in (-W, W):
            b.beam(V(x, y, -H), V(x, y, H), 0.35, M("Hazard" if x > 0 else "HullDark"))
        for z in (-H, H):
            b.beam(V(x, -W, z), V(x, W, z), 0.35, M("HullDark"))
    for y in (-W, W):
        for z in (-H, H):
            b.truss(V(-L, y, z), V(L, y, z), 0.3, 0.1, M("Hull"), n=8)
    for i in range(1, 4):
        x = -L + i * 2 * L / 4
        for y in (-W, W):
            b.beam(V(x, y, -H), V(x, y, H), 0.22, M("HullDark"))
        b.beam(V(x, -W, H), V(x, W, H), 0.22, M("HullDark"))
        b.beam(V(x, -W, -H), V(x, W, -H), 0.22, M("HullDark"))
    # halbfertiger Rumpf entlang X
    prof = [(0, -6.0), (1.2, -5.2), (1.8, -3.0), (2.0, 0.0), (1.8, 2.5), (1.4, 4.4), (0.6, 5.6), (0, 5.8)]
    b.lathe(prof[:5] + [(0, 1.0)], M("Hull"), seg=10, rot=(0, 90, 0), scale=(1, 0.8, 1))
    b.lathe([(1.82, -0.1), (1.82, 0.4)], M("Accent"), seg=10, rot=(0, 90, 0), loc=(-3.0, 0, 0),
            scale=(1, 0.8, 1))
    for xr in (1.8, 2.9, 4.0, 5.0):
        r = 1.95 if xr < 3 else (1.5 if xr < 4.5 else 0.9)
        b.ring(r, 0.18, 0.18, M("HullDark"), seg=10, loc=(xr, 0, 0), rot=(0, 90, 0), arc=360)
    b.beam(V(1.0, 0, 1.4), V(5.4, 0, 0.6), 0.16, M("HullDark"))
    b.beam(V(1.0, 0, -1.4), V(5.4, 0, -0.6), 0.16, M("HullDark"))
    b.cyl(1.1, 1.6, M("HullDark"), seg=10, loc=(-6.6, 0, 0), rot=(0, 90, 0), r2=1.5)
    # Kraene
    for x, s in ((-2.0, 1), (3.5, -1)):
        b.box(0.8, 0.8, 0.8, M("Hazard"), loc=(x, s * W, H + 0.5))
        b.beam(V(x, s * W, H + 0.8), V(x, -s * 1.0, H + 1.4), 0.25, M("Hazard"))
        b.beam(V(x, -s * 1.0, H + 1.4), V(x, -s * 1.0, 2.2), 0.06, M("Hull"))
    # Leitstand
    b.box(2.6, 2.6, 3.2, M("Hull"), loc=(-L - 1.8, 0, 0.4), bevel=0.25)
    b.box(2.7, 2.7, 0.35, M("Window"), loc=(-L - 1.8, 0, 1.4))
    b.box(2.9, 0.2, 0.35, M("Accent"), loc=(-L - 1.8, -1.35, 0.6))
    b.dish((-L - 1.8, 0, 2.0), 1.2, M("Hull"), rot=(-30, 0, 0))
    b.turret((L - 1.0, -W + 0.4, H + 0.3), facing=-60, s=0.8)
    b.turret((L - 1.0, W - 0.4, -H - 0.3), facing=60, s=0.8, up=False)
    b.nav_lights([V(x, y, z) for x, y, z in corners], mat="Light", s=0.22)
    for x, s in ((-4.0, 1), (4.0, -1)):
        b.pd((x, s * W, H + 0.2), facing=90 * s, s=0.8)
        b.pd((x, -s * W, -H - 0.2), up=False, facing=-90 * s, s=0.8)
    b.insignia(V(-L - 1.8, -1.42, 0.0), (0, -1, 0), s=0.6)
    b.mast((-L - 1.8, 0.8, 2.0), 2.4)


@model("colony", "Grosses Wohnrad mit leuchtendem Fensterband und Glasblasen, vier Speichen, Nabe mit Kuppel und Antennenspitze")
def colony(b):
    R = 8.6
    b.torus(R, 1.3, M("Hull"), seg=32, minor=8)
    b.torus(R, 1.34, M("Window"), seg=32, minor=8, minor_scale=(1.0, 0.12))
    b.torus(R, 1.4, M("Accent"), seg=32, minor=4, minor_scale=(0.25, 1.0), start_minor=45)
    for x, y, a in polar(8, R, 22.5):
        b.sphere(0.85, M("Glass"), seg=10, rings=5, loc=(x, y, 1.05), half=True)
    for x, y, a in polar(4, R, 0):
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        b.cyl(0.55, R - 2.6, M("HullDark"), seg=8, loc=d * (R / 2 + 0.7), rot=(0, 90, a))
        b.window_band(0.58, 0, 0.3, n=2, seg=8, rot=(0, 90, a), loc=d * (R / 2 + 0.7))
    b.lathe([(0, -4.5), (1.0, -4.0), (1.6, -2.5), (2.2, -1.0), (2.2, 1.0), (1.8, 1.6)],
            M("Hull"), seg=16)
    b.sphere(1.8, M("Glass"), seg=16, rings=8, loc=(0, 0, 1.6), half=True)
    b.ring(1.95, 0.3, 0.4, M("Accent"), seg=16, loc=(0, 0, 1.6))
    b.cyl(0.15, 5.0, M("HullDark"), seg=6, loc=(0, 0, 5.4))
    b.cyl(0.5, 1.2, M("HullDark"), seg=10, loc=(0, 0, -5.0))
    b.nav_lights([(0, 0, 8.0), (0, 0, -5.7)], s=0.25)


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


@model("drive_works", "Triebwerks-Pruefstand: Reaktorkugel mit Plasmaband, drei grosse Duesen mit blauem Schub in drei Richtungen, Traegerring")
def drive_works(b):
    b.sphere(2.6, M("Hull"), seg=16, rings=10)
    b.torus(2.62, 0.2, M("Plasma"), seg=16, minor=4)
    b.ring(3.0, 0.3, 0.5, M("Accent"), seg=16, loc=(0, 0, 1.2))
    b.ring(8.0, 0.5, 0.7, M("HullDark"), seg=30)
    for x, y, a in polar(3, 4.0, 90):
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        rot = (0, 90, a)
        b.cyl(1.2, 2.4, M("Hull"), seg=12, loc=d * 3.3, rot=rot)
        b.lathe([(0.9, 0), (1.2, 0.6), (1.9, 2.4), (2.4, 4.0), (2.2, 4.0), (1.6, 2.4), (0.9, 0.9)],
                M("HullDark"), seg=14, loc=d * 4.4, rot=rot, closed=True)
        b.lathe([(0, 3.2), (1.8, 3.2)], M("Light"), seg=14, loc=d * 4.4, rot=rot)
        b.lathe([(0, 0.8), (1.0, 0.8)], M("Light"), seg=10, loc=d * 4.4, rot=rot)
        b.beam(d * 6.4 + V(0, 0, 2.3), d * 7.7, 0.3, M("Hull"))
        b.beam(d * 6.4 - V(0, 0, 2.3), d * 7.7, 0.3, M("Hull"))
        b.beam(d * 2.5 + V(0, 0, 2.0), d * 6.4 + V(0, 0, 2.3), 0.3, M("Hull"))
        b.beam(d * 2.5 - V(0, 0, 2.0), d * 6.4 - V(0, 0, 2.3), 0.3, M("Hull"))
    for x, y, a in polar(3, 4.0, 30):
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        b.beam(d * 2.4, d * 7.6, 0.45, M("HullDark"))
        b.box(1.8, 1.4, 1.4, M("Hull"), loc=d * 5.5, rot=(0, 0, a), bevel=0.15)
        b.box(1.0, 1.42, 0.25, M("Window"), loc=d * 5.5 + V(0, 0, 0.3), rot=(0, 0, a))
    b.cyl(0.7, 2.0, M("HullDark"), seg=10, loc=(0, 0, 3.2))
    b.cyl(0.7, 2.0, M("HullDark"), seg=10, loc=(0, 0, -3.2))
    b.nav_lights([(0, 0, 4.4), (0, 0, -4.4)], s=0.25)
    b.pd((0, 0, 4.2), facing=0, s=0.8)
    b.pd((0, 0, -4.2), up=False, facing=180, s=0.8)


@model("weapons_works", "Facettierter Panzerrumpf (Spindel) mit Rohrgestellen oben und unten, langer Erprobungsausleger mit Zielring, zwei Schildprojektoren, Geschuetztuerme oben und unten")
def weapons_works(b):
    b.lathe([(0, -7.0), (1.6, -6.0), (2.8, -3.5), (3.0, 2.0), (2.2, 4.8), (1.0, 6.4), (0, 6.8)],
            M("HullDark"), seg=6, rot=(0, 90, 0), start=0)
    b.lathe([(3.05, -0.25), (3.05, 0.25)], M("Accent"), seg=6, loc=(-1.5, 0, 0), rot=(0, 90, 0))
    b.lathe([(2.9, -0.15), (2.9, 0.15)], M("LightRed"), seg=6, loc=(2.4, 0, 0), rot=(0, 90, 0))
    for z in (1, -1):
        b.box(6.5, 2.0, 0.5, M("Hull"), loc=(-1.2, 0, z * 2.85))
        for k in range(4):
            b.cyl(0.17, 6.4, M("Hull"), seg=6, loc=(-1.2, -0.75 + k * 0.5, z * 3.25), rot=(0, 90, 0))
        b.turret((3.0, 0, z * 2.5), facing=0, s=1.0, up=z > 0)
        b.cyl(0.9, 0.8, M("Hull"), seg=10, loc=(-4.5, 0, z * 2.7))
        b.sphere(1.5, M("Light"), seg=12, rings=6, loc=(-4.5, 0, z * 3.05), half=True,
                 scale=(1, 1, 0.5), rot=None if z > 0 else (180, 0, 0))
        b.ring(1.65, 0.3, 0.3, M("HullDark"), seg=12, loc=(-4.5, 0, z * 3.1))
    b.truss(V(6.6, 0, 0), V(14.0, 0, 0), 0.55, 0.16, M("Hull"), n=6)
    b.ring(2.0, 0.4, 0.4, M("Hazard"), seg=14, loc=(14.4, 0, 0), rot=(0, 90, 0))
    b.ring(1.0, 0.25, 0.42, M("LightRed"), seg=10, loc=(14.4, 0, 0), rot=(0, 90, 0))
    for s in (1, -1):
        b.radiator(V(-2.5, s * 2.8, 0), (0, s, 0), 5.0, 2.0, tilt=10 * s, mat="HullDark", stripe="LightRed")
    b.dock(V(-7.0, 0, 0), (-1, 0, 0))
    b.nav_lights([(14.4, 0, 2.3), (-7, 0, 1.6)], mat="LightRed", s=0.22)
    for z in (1, -1):
        b.missile_pod((4.6, 0, z * 1.7), facing=0, up=z > 0, s=0.9)
    b.insignia(V(-1.5, -2.75, 0.0), (0, -1, 0), s=0.7)


@model("systems_works", "Raeumlicher Baukasten: Modulwuerfel in alle Richtungen um einen Zentralwuerfel, Verbindungsrohre, zwei Roboterarme, Sensorschuesseln, Antennen oben und unten")
def systems_works(b):
    b.box(3.4, 3.4, 3.4, M("Hull"), bevel=0.25)
    b.box(3.5, 0.2, 0.5, M("Accent"), loc=(0, -1.72, 0.9))
    mods = [((5.2, 0, 0), 2.6, "Accent"), ((-5.2, 0, 0), 2.6, "HullDark"), ((0, 5.2, 0), 2.4, "HullDark"),
            ((0, -5.2, 0), 2.4, "Hull"), ((0, 0, 4.4), 2.2, "HullDark"), ((0, 0, -4.4), 2.2, "Accent"),
            ((3.6, 3.6, 2.4), 1.6, "Hull"), ((-3.6, -3.6, -2.4), 1.6, "Hull"),
            ((3.6, -3.6, -2.2), 1.5, "HullDark"), ((-3.6, 3.6, 2.2), 1.5, "Accent")]
    for (x, y, z), s, m in mods:
        p = V(x, y, z)
        b.strut(V(0, 0, 0), p, 0.35, M("HullDark"), seg=6)
        b.box(s, s, s, M(m), loc=p, bevel=0.15)
        b.box(s * 0.6, 0.06, 0.16, M("Light"), loc=p + V(0, -s / 2, s * 0.25))
        b.box(0.06, s * 0.6, 0.16, M("Light"), loc=p + V(s / 2, 0, s * 0.25))
    for base, s in ((V(6.5, 0, 0), 1), (V(-6.5, 0, 0), -1)):
        j1 = base + V(s * 1.2, s * 1.4, 1.6)
        j2 = j1 + V(s * 2.0, s * 0.6, -1.4)
        tip = j2 + V(s * 0.6, s * -0.8, -1.2)
        b.sphere(0.45, M("HullDark"), seg=8, rings=5, loc=base)
        b.beam(base, j1, 0.4, M("Hazard"))
        b.sphere(0.4, M("HullDark"), seg=8, rings=5, loc=j1)
        b.beam(j1, j2, 0.32, M("Hazard"))
        b.sphere(0.32, M("HullDark"), seg=8, rings=5, loc=j2)
        b.beam(j2, tip, 0.18, M("Hull"))
        b.nav_lights([tip], s=0.18)
    b.dish((0, 6.4, 0), 1.6, M("Accent"), rot=(-90, 0, 0))
    b.dish((0, -6.4, 0), 1.4, M("Hull"), rot=(90, 0, 0))
    for z in (1, -1):
        for i in range(3):
            h = 1.6 + i * 0.8
            b.cyl(0.07, h, M("HullDark"), seg=5, loc=(-0.6 + i * 0.6, 0, z * (5.5 + h / 2)))
            b.nav_lights([V(-0.6 + i * 0.6, 0, z * (5.5 + h))], s=0.13)


# =============================================================== Handel und Lager
@model("trade_station", "Grosser Haendler-Hub: gepanzerte Doppelkegel-Nabe mit Fensterbaendern, sechs Andockarme mit Leuchtkragen, Containerring, offenes Schiffsdock mit ausgestelltem Kleinschiff, Punktverteidigung")
def trade_station(b):
    b.lathe([(0, -5.2), (1.2, -4.8), (3.0, -2.6), (4.4, -0.7), (4.4, 0.7), (3.0, 2.6), (1.4, 4.2),
             (0.6, 5.0), (0, 5.2)], M("Hull"), seg=12)
    b.armor_belt(4.6, 0, 1.2, 12, tilt=0)
    b.window_band(3.6, 1.8, 0.28, n=6, seg=12, gap=0.35)
    b.window_band(3.6, -1.8, 0.28, n=6, seg=12, gap=0.35)
    for i, (x, y, a) in enumerate(polar(6, 4.0, 30)):
        if i == 0:
            continue
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        b.cyl(0.5, 3.4, M("HullDark"), seg=6, loc=d * 6.0, rot=(0, 90, a))
        b.ring(1.05, 0.4, 0.7, M("Armor"), seg=8, loc=d * 7.9, rot=(0, 90, a))
        b.ring(0.75, 0.12, 0.72, M("Light"), seg=8, loc=d * 7.9, rot=(0, 90, a))
    cols = ("Container", "Accent", "Hazard", "Steel")
    b.ring(6.0, 0.3, 0.3, M("HullDark"), seg=16, loc=(0, 0, 1.5))
    b.ring(6.0, 0.3, 0.3, M("HullDark"), seg=16, loc=(0, 0, -1.5))
    for j, (x, y, a) in enumerate(polar(10, 5.9, 18)):
        if 345 < (a % 360) or (a % 360) < 75:
            continue
        for z in (1.5, -1.5):
            b.box(1.6, 1.0, 1.0, M(cols[(j + (z > 0)) % 4]), loc=(x, y, z), rot=(0, 0, a + 90))
    # offenes Schiffsdock mit ausgestelltem Kleinschiff (Richtung 30 Grad)
    d = V(math.cos(math.radians(30)), math.sin(math.radians(30)), 0)
    n = V(-d.y, d.x, 0)
    c = d * 8.2
    for u in (-1, 1):
        for z in (-1.4, 1.4):
            b.beam(c - d * 3.0 + n * u * 1.8 + V(0, 0, z), c + d * 3.0 + n * u * 1.8 + V(0, 0, z), 0.25,
                   M("Hazard" if z > 0 else "HullDark"))
        b.beam(c + d * 3.0 + n * u * 1.8 + V(0, 0, -1.4), c + d * 3.0 + n * u * 1.8 + V(0, 0, 1.4), 0.25, M("HullDark"))
    b.beam(d * 4.4, c - d * 3.0, 0.8, M("HullDark"))
    yaw = 30
    b.prism([(-2.0, -0.9), (1.4, -0.5), (2.4, 0), (1.4, 0.5), (-2.0, 0.9)], 0.8, M("Accent"),
            loc=c, rot=(0, 0, yaw), top_scale=0.6)
    b.box(1.0, 3.2, 0.12, M("Steel"), loc=c - d * 0.6, rot=(0, 0, yaw))
    b.box(0.3, 0.6, 0.3, M("Light"), loc=c - d * 2.0, rot=(0, 0, yaw))
    for k in (-1, 1):
        b.nav_lights([c + d * 3.0 + n * k * 1.8 + V(0, 0, 1.6)], mat="Light", s=0.18)
    b.pd((0, 0, 4.9), facing=0)
    b.pd((0, 0, -4.9), up=False, facing=180)
    b.mast((1.0, 0, 4.0), 3.0)
    b.insignia(V(-4.7, 0, 0), (-1, 0, 0), s=0.9)


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


@model("warehouse", "Container-Karussell: sechs Regaltuerme mit gestapelten Containern (ueber und unter der Ebene) im Sechseck um eine Mittelspindel, Ringtraeger oben und unten, Andockkragen an beiden Enden, Verladearm")
def warehouse(b):
    b.lathe([(0, -7.5), (0.7, -7.0), (0.9, -5.5), (0.9, 5.5), (0.7, 7.0), (0, 7.5)], M("Hull"), seg=10)
    for z in (6.2, -6.2):
        b.ring(1.6, 0.5, 0.6, M("HullDark"), seg=10, loc=(0, 0, z))
        b.ring(1.2, 0.12, 0.62, M("Light"), seg=10, loc=(0, 0, z))
    R = 6.4
    for z in (-4.6, 0.0, 4.6):
        b.ring(R + 1.3, 0.35, 0.35, M("Accent" if z == 0 else "HullDark"), seg=6, loc=(0, 0, z), start=30)
    for x, y, a in polar(3, 0, 30):
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        for z in (-4.6, 4.6):
            b.beam(V(0, 0, z * 0.9), d * (R + 1.0) + V(0, 0, z), 0.3, M("HullDark"))
    cols = ["Container", "Hull", "Accent", "Hazard", "HullDark"]
    k = 0
    for i, (x, y, a) in enumerate(polar(6, R, 0)):
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        n = V(-d.y, d.x, 0)
        for s in (-1, 1):
            b.beam(d * R + n * s * 1.3 + V(0, 0, -4.6), d * R + n * s * 1.3 + V(0, 0, 4.6), 0.22, M("HullDark"))
        for lv in range(4):
            if (i * 3 + lv) % 7 == 3:
                continue
            z = -3.45 + lv * 2.3
            b.box(2.2, 2.4, 2.0, M(cols[(i * 2 + lv) % 5]), loc=d * R + V(0, 0, z), rot=(0, 0, a), bevel=0.06)
            k += 1
    d = V(math.cos(math.radians(30)), math.sin(math.radians(30)), 0)
    b.beam(d * (R + 1.3) + V(0, 0, 4.6), d * (R + 4.0) + V(0, 0, 6.0), 0.35, M("Hazard"))
    b.beam(d * (R + 4.0) + V(0, 0, 6.0), d * (R + 4.0) + V(0, 0, 3.8), 0.07, M("Hull"))
    b.box(2.0, 1.0, 1.0, M("Container"), loc=d * (R + 4.0) + V(0, 0, 3.3), rot=(0, 0, 30))
    b.nav_lights([(0, 0, 7.6), (0, 0, -7.6)] + [V(x, y, 4.9) for x, y, a in polar(6, R + 1.2, 30)], s=0.2)


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
    for x, y, a in polar(2, 5.0, 90):
        b.turret((x, y, 1.2), facing=a, s=0.95)
    for x, y, a in polar(2, 5.0, 0):
        b.missile_pod((x, y, -1.2), facing=a, up=False, s=1.0)
    for x, y, a in polar(2, 2.0, 30):
        b.pd((x, y, 6.6), facing=a, s=0.8)
    for x, y, a in polar(2, 1.9, 90):
        b.pd((x, y, -5.9), up=False, facing=a, s=0.8)
    b.mast((1.0, 0.6, 9.0), 2.2)


@model("company_seat", "Schlanke Konzernspindel mit drei verglasten Ringdecks (gross in der Mitte, kleiner oben und unten), geneigter Logoring, Spitze, Andockarm mit Landeplattform")
def company_seat(b):
    b.lathe([(0, -9.5), (0.6, -8.5), (1.1, -6.0), (1.2, 6.0), (0.7, 8.5), (0.15, 11.0), (0, 11.2)],
            M("Hull"), seg=12)
    for z, R, h in ((0.0, 7.0, 1.6), (4.2, 4.2, 1.2), (-4.0, 3.6, 1.1)):
        b.lathe([(1.1, z - h / 2), (R - 0.4, z - h / 2), (R, z - h * 0.2), (R, z + h * 0.2),
                 (R - 0.4, z + h / 2), (1.1, z + h / 2)], M("Hull"), seg=20, closed=True)
        b.lathe([(R + 0.03, z - h * 0.18), (R + 0.03, z + h * 0.18)], M("Glass"), seg=20)
        b.window_band(R + 0.06, z, h * 0.14, mat="Window", n=10, seg=20, gap=0.35)
        b.lathe([(0, z + h / 2 + 0.01), (R - 0.6, z + h / 2 + 0.01)], M("Glass"), seg=20) if R > 5 else None
    b.ring(7.4, 0.25, 0.3, M("Accent"), seg=20)
    b.torus(5.6, 0.28, M("Accent"), seg=24, minor=6, loc=(0, 0, 2.0), rot=(18, 0, 0))
    b.cyl(1.25, 1.2, M("Glass"), seg=12, loc=(0, 0, 2.0))
    b.cyl(1.25, 1.2, M("Glass"), seg=12, loc=(0, 0, -2.0))
    d = V(math.cos(math.radians(-50)), math.sin(math.radians(-50)), 0)
    b.beam(d * 6.8, d * 9.6, 0.6, M("HullDark"))
    b.cyl(2.0, 0.3, M("HullDark"), seg=14, loc=d * 11.2)
    b.ring(1.95, 0.25, 0.32, M("Hazard"), seg=14, loc=d * 11.2)
    b.nav_lights([(0, 0, 11.4), (0, 0, -9.7)], mat="LightRed", s=0.22)
    b.dock(V(0, 0, -9.0), (0, 0, -1), r=0.45, L=1.0)


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
    for x, y, a in polar(2, R - 0.8, 0):
        b.turret((x, y, 0.8), facing=a, s=1.0)
        b.turret((x, y, -0.8), facing=a, s=0.9, up=False)
    for x, y, a in polar(4, R - 0.8, 45):
        b.pd((x, y, 0.8 if int(a) % 180 == 45 else -0.8), up=int(a) % 180 == 45, facing=a, s=0.7)
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
    for x, y, a in polar(3, 5.6, 30):
        b.turret((x, y, 0.7), facing=a, s=0.95, twin=(int(a) != 270))
    b.lathe([(0, -4.4), (1.0, -4.2), (1.8, -3.6), (1.8, -2.4), (2.6, -1.8), (2.8, -1.0)], M("Hull"), seg=12)
    b.window_band(1.85, -3.0, 0.3, n=6, seg=12)
    b.dock(V(0, 0, -4.3), (0, 0, -1), r=0.6, L=1.2)
    for x, y, a in polar(3, 4.6, 0):
        b.turret((x, y, -2.5), facing=a, s=0.75, up=False, twin=False)
    b.nav_lights([(0, 0, -5.6)], mat="Light", s=0.25)
    for x, y, a in polar(3, 3.4, 30):
        b.hangar_slit(V(x, y, 0.0), (x, y, 0), w=1.6, h=0.6)
    for x, y, a in polar(3, 5.0, 60):
        b.pd((x, y, 0.75), facing=a, s=0.75)
    b.insignia(V(0, -2.85, 1.8), (0, -1, 0.2), s=0.55)


@model("pirate_hideout", "Zusammengeflickte Piratenfestung in einem ausgehoehlten, zerklueften Asteroiden: rot gluehendes Hangarmaul, eingeschweisste erbeutete Rumpfteile, Flickpanzer in ungleichen Farben, Rammsporne, grobe Geschuetze oben und unten, Trossen zu kleineren Brocken, Warn- und Feuerlichter, schiefe Antennen")
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
    # grobe Geschuetze
    b.turret((1.2, 1.6, 5.2), facing=-60, s=1.0, hull="Rust", barrel="HullDark", twin=False)
    b.turret((-3.4, -1.2, 4.0), facing=-120, s=0.9, hull="Armor", barrel="HullDark")
    b.turret((0.8, -1.2, -5.2), facing=-90, s=1.0, hull="Rust", barrel="HullDark", up=False)
    b.turret((5.8, 0.8, -1.2), facing=-20, s=0.8, hull="Steel", barrel="HullDark", up=False, twin=False)
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
