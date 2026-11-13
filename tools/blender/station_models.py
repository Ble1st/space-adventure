"""Ein prozedurales Modell je Stationstyp (Key = stationTypes[].key in default-world.json).

Jede Bau-Funktion arbeitet in einem freien Massstab (Koerper etwa Radius 10 in XY);
Builder.finish() zentriert und normiert danach auf Radius 1 in der Spielebene.
Blender-Koordinaten: XY = Spielebene, +Z = "oben" (nach glTF-Export +Y).
"""

import math
from mathutils import Vector

from stationkit import Builder, material as M, polar

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


@model("steelworks", "Schwerindustrie: Hochofen mit Gluehband, Halle, drei Schlote mit glimmenden Raendern, Erzbunker am Foerderband")
def steelworks(b):
    b.box(9, 5, 3.4, M("Hull"), loc=(1.5, 0, 0), bevel=0.35)
    b.box(9.2, 0.25, 0.35, M("Accent"), loc=(1.5, -2.55, 1.0))
    b.box(9.2, 0.25, 0.35, M("Accent"), loc=(1.5, 2.55, 1.0))
    for i in range(5):
        b.box(1.1, 0.12, 0.5, M("Glow"), loc=(-1.6 + i * 1.8, -2.55, -0.45))
    b.box(8.4, 4.2, 0.6, M("HullDark"), loc=(1.5, 0, 1.95))
    # Hochofen
    b.lathe([(2.4, -3.2), (2.7, -1.2), (2.2, 1.5), (1.4, 3.6), (1.5, 4.6), (1.0, 5.0)],
            M("HullDark"), seg=14, loc=(-4.2, 0.5, 0), cap_bottom=True, cap_top=True)
    b.lathe([(2.78, -0.95), (2.73, -0.55)], M("Glow"), seg=14, loc=(-4.2, 0.5, 0))
    b.torus(2.65, 0.22, M("Hull"), seg=14, minor=4, loc=(-4.2, 0.5, -2.0))
    b.torus(1.95, 0.2, M("Hull"), seg=14, minor=4, loc=(-4.2, 0.5, 2.4))
    b.torus(0.95, 0.15, M("Glow"), seg=12, minor=4, loc=(-4.2, 0.5, 5.0))
    # Schlote
    for i, (x, y, h) in enumerate(((2.5, 1.2, 7.5), (4.2, 1.2, 6.2), (5.9, 1.2, 5.0))):
        b.cyl(0.62, h, M("Hull"), seg=10, loc=(x, y, 1.7 + h / 2))
        b.cyl(0.7, 0.5, M("HullDark"), seg=10, loc=(x, y, 1.7 + h * 0.55))
        b.torus(0.58, 0.12, M("Glow"), seg=10, minor=4, loc=(x, y, 1.7 + h))
    # Erzbunker und Foerderband
    b.lathe([(0.4, -1.5), (2.0, 1.2), (2.1, 1.6)], M("HullDark"), seg=10, loc=(9.0, -3.5, 1.4))
    b.truss(V(8.2, -2.9, 2.0), V(4.5, -1.2, 2.3), 0.35, 0.12, M("Hull"), n=4)
    for x in (8.2, 9.8):
        for y in (-4.3, -2.7):
            b.beam(V(x, y, -0.6), V(x, y, 1.6), 0.2, M("Hull"))
    # Kuehlrippen unten
    for i in range(4):
        b.box(0.15, 4.0, 2.2, M("HullDark"), loc=(-0.5 + i * 2.2, 0, -2.6))
    b.nav_lights([(-4.2, 0.5, 5.4), (6.0, -2.6, 1.8), (-2.9, -2.6, 1.8)], mat="LightRed", s=0.2)


@model("water_plant", "Drei grosse Eistanks um eine Pumpensaeule, Eistrichter oben, Rohrleitungen")
def water_plant(b):
    b.cyl(1.0, 9.0, M("Hull"), seg=12, loc=(0, 0, 0.5))
    b.ring(1.2, 0.3, 0.6, M("Accent"), seg=12, loc=(0, 0, 2.6))
    b.window_band(1.05, -1.5, 0.3, n=6, seg=12)
    b.lathe([(0.9, 4.9), (2.4, 6.1), (3.3, 6.6), (3.2, 6.8), (2.2, 6.3), (0.9, 5.3)],
            M("HullDark"), seg=16, closed=True)
    b.lathe([(0, 6.45), (2.9, 6.45)], M("Ice"), seg=16)
    for x, y, a in polar(3, 4.2, 90):
        b.sphere(2.4, M("Ice"), seg=16, rings=10, loc=(x, y, -0.2))
        b.torus(2.42, 0.18, M("HullDark"), seg=16, minor=4, loc=(x, y, -0.2))
        b.cyl(1.1, 0.5, M("Hull"), seg=10, loc=(x, y, 2.3))
        b.cyl(0.9, 1.6, M("HullDark"), seg=10, loc=(x, y, -2.8))
        d = V(x, y, 0).normalized()
        b.strut(d * 0.9 + V(0, 0, 1.2), d * 3.0 + V(0, 0, 1.8), 0.3, M("Hull"), seg=8)
        b.strut(d * 0.9 + V(0, 0, -1.3), d * 2.4 + V(0, 0, -1.9), 0.25, M("Copper"), seg=8)
        b.nav_lights([V(x, y, 2.7)], s=0.2)
    b.box(2.6, 2.6, 1.6, M("Hull"), loc=(0, 0, -4.0), bevel=0.2)


@model("electrolysis", "Zwei geriffelte Elektrolysezellen mit leuchtenden Spalten, Kupfer-Stromschienen, Gastanks in Kapselform")
def electrolysis(b):
    b.prism([(math.cos(math.radians(22.5 + 45 * i)) * 6.0, math.sin(math.radians(22.5 + 45 * i)) * 3.6)
             for i in range(8)], 0.8, M("HullDark"), loc=(0, 0, -3.3))
    for x in (-2.2, 2.2):
        prof = []
        for i in range(7):
            z = -2.9 + i * 1.25
            prof += [(1.45, z), (1.45, z + 0.85), (1.2, z + 0.95), (1.2, z + 1.15)]
        prof += [(1.3, 5.9), (0.6, 6.5), (0, 6.6)]
        b.lathe([(0, -2.9)] + prof, M("Hull"), seg=12, loc=(x, 0, 0))
        for i in range(7):
            z = -2.9 + i * 1.25 + 1.05
            b.lathe([(1.25, z - 0.08), (1.25, z + 0.08)], M("Light"), seg=12, loc=(x, 0, 0))
        b.box(0.35, 0.35, 8.8, M("Copper"), loc=(x, -1.6, 1.4))
        b.box(0.35, 0.35, 8.8, M("Copper"), loc=(x, 1.6, 1.4))
        b.nav_lights([V(x, 0, 6.75)], s=0.22)
    b.box(2.4, 1.4, 1.1, M("Accent"), loc=(0, 0, 3.2), bevel=0.15)
    b.box(2.4, 1.4, 1.1, M("Hull"), loc=(0, 0, -0.6), bevel=0.15)
    # Kapseltanks (Wasserstoff, Sauerstoff)
    for y, mat in ((-3.0, "Hull"), (3.0, "Accent")):
        b.lathe([(0, -4.2), (0.9, -3.9), (1.25, -3.2), (1.25, 3.2), (0.9, 3.9), (0, 4.2)],
                M(mat), seg=12, loc=(0, y, -2.0), rot=(0, 90, 0))
        for x in (-2.5, 0, 2.5):
            b.torus(1.27, 0.12, M("HullDark"), seg=12, minor=4, loc=(x, y, -2.0), rot=(0, 90, 0))
        b.strut(V(-2.2, y * 0.45, -0.5), V(-2.2, y * 0.85, -1.4), 0.2, M("Copper"))


@model("copper_smelter", "Runder Schmelztiegel mit gluehender Kupferschmelze in einem Tragring, schraeger Schlot, Barrenstapel aus Kupfer")
def copper_smelter(b):
    # Tiegel
    b.lathe([(0, -2.6), (1.8, -2.4), (3.2, -1.2), (3.8, 0.8), (3.9, 1.6), (3.5, 1.6),
             (3.3, 0.8), (2.7, -1.0), (1.5, -1.9), (0, -2.0)], M("HullDark"), seg=16)
    b.lathe([(0, 1.0), (3.45, 1.0)], M("Glow"), seg=16)
    b.torus(4.4, 0.35, M("Copper"), seg=18, minor=6, loc=(0, 0, 0.2))
    for x, y, a in polar(6, 4.4, 0):
        b.strut(V(x, y, 0.2), V(x * 0.75, y * 0.75, -2.3), 0.18, M("Hull"))
    for x, y, a in polar(3, 4.4, 30):
        b.strut(V(x, y, 0.2), V(x * 1.0, y * 1.0, -3.6), 0.22, M("Hull"))
        b.cyl(0.6, 0.4, M("HullDark"), seg=8, loc=(x, y, -3.8))
    # Ausguss
    b.box(1.6, 0.8, 0.4, M("Copper"), loc=(4.2, 0, 1.4), rot=(0, -15, 0))
    # Giesslinie mit Barren
    b.box(6.0, 2.6, 1.2, M("Hull"), loc=(7.2, 0, -0.3), bevel=0.2)
    b.box(6.2, 0.2, 0.3, M("Accent"), loc=(7.2, -1.35, -0.1))
    for i in range(4):
        for j in range(2):
            for k in range(2 - (i % 2)):
                b.box(1.0, 0.5, 0.35, M("Copper"), loc=(5.0 + i * 1.4, -0.4 + j * 0.8, 0.5 + k * 0.38),
                      bevel=0.06)
    # Schraeger Schlot
    b.strut(V(-2.5, 2.0, 0.5), V(-5.5, 4.5, 6.0), 0.7, M("Hull"), seg=10)
    b.sphere(0.75, M("Glow"), seg=8, rings=4, loc=(-5.5, 4.5, 6.0))
    b.strut(V(-2.5, -2.0, 0.0), V(-6.0, -3.2, -1.0), 0.45, M("Copper"), seg=8)
    b.box(2.4, 2.4, 2.4, M("Hull"), loc=(-6.5, -3.5, -1.0), bevel=0.25)
    b.window_band(1.25, 0, 0.3, n=4, seg=8, loc=(-6.5, -3.5, -1.0))
    b.nav_lights([(9.9, 0, 0.4), (-6.5, -3.5, 0.5)], mat="LightRed", s=0.2)


@model("wafer_fab", "Reinraum-Saeule mit vier weissen Waferscheiben uebereinander, violett leuchtende Raender, Glaskuppel")
def wafer_fab(b):
    b.cyl(1.1, 11.0, M("Hull"), seg=12)
    for i in range(4):
        z = -3.6 + i * 2.3
        r = 5.6 - abs(i - 1.5) * 0.6
        b.lathe([(1.0, z - 0.25), (r - 0.4, z - 0.25), (r, z), (r - 0.4, z + 0.2), (1.0, z + 0.2)],
                M("Accent"), seg=24, closed=True)
        b.torus(r - 0.05, 0.1, M("Plasma"), seg=24, minor=4, loc=(0, 0, z))
        for x, y, a in polar(6, r * 0.62, i * 15):
            b.box(0.8, 0.8, 0.25, M("HullDark"), loc=(x, y, z + 0.28), rot=(0, 0, a))
        for x, y, a in polar(3, 1.0, i * 40):
            b.beam(V(x, y, z + 0.2), V(x * 2.6, y * 2.6, z + 2.05), 0.12, M("Hull"))
    b.sphere(1.9, M("Glass"), seg=14, rings=8, loc=(0, 0, 5.5), half=True)
    b.ring(2.0, 0.25, 0.4, M("Hull"), seg=14, loc=(0, 0, 5.5))
    b.lathe([(1.1, -5.5), (0.5, -7.0), (0, -7.6)], M("HullDark"), seg=12)
    b.window_band(1.15, 0.6, 0.3, n=6, seg=12)
    b.nav_lights([(0, 0, 7.6)], mat="Plasma", s=0.25)


@model("titanium_works", "Gruppe sechseckiger Tuerme, grosse Brechertrommel mit Warnstreifen, Erztrichter")
def titanium_works(b):
    hexa = [(math.cos(math.radians(60 * i)), math.sin(math.radians(60 * i))) for i in range(6)]
    for (x, y, h, mat) in ((0, 1.5, 9, "Hull"), (2.6, 3.0, 6.5, "HullDark"), (-2.6, 3.0, 7.5, "HullDark"),
                           (0, 4.6, 5.0, "Hull")):
        b.prism([(px * 1.5, py * 1.5) for px, py in hexa], h, M(mat), loc=(x, y, h / 2 - 3),
                top_scale=0.85)
        b.prism([(px * 1.6, py * 1.6) for px, py in hexa], 0.3, M("Accent"), loc=(x, y, h - 4.0))
        b.prism([(px * 0.7, py * 0.7) for px, py in hexa], 0.6, M("Light"), loc=(x, y, h - 2.7))
    # Brechertrommel
    b.lathe([(2.4, -3.2), (2.6, -2.8), (2.6, 2.8), (2.4, 3.2)], M("Hull"), seg=16, loc=(0, -3.5, 0),
            rot=(0, 90, 0), cap_bottom=True, cap_top=True)
    for x in (-2.2, -0.7, 0.7, 2.2):
        b.torus(2.65, 0.14, M("HullDark"), seg=16, minor=4, loc=(x, -3.5, 0), rot=(0, 90, 0))
    b.lathe([(2.63, -0.35), (2.63, 0.35)], M("Hazard"), seg=16, loc=(-3.2, -3.5, 0), rot=(0, 90, 0))
    b.lathe([(2.63, -0.35), (2.63, 0.35)], M("Hazard"), seg=16, loc=(3.2, -3.5, 0), rot=(0, 90, 0))
    b.cyl(1.0, 1.0, M("HullDark"), seg=10, loc=(4.2, -3.5, 0), rot=(0, 90, 0))
    b.cyl(1.0, 1.0, M("HullDark"), seg=10, loc=(-4.2, -3.5, 0), rot=(0, 90, 0))
    for x in (-4.2, 4.2):
        b.box(0.8, 1.6, 4.2, M("Hull"), loc=(x, -3.5, -2.0))
    # Trichter und Band
    b.lathe([(0.5, -1.0), (2.2, 1.2), (2.3, 1.5)], M("HullDark"), seg=10, loc=(-6.5, -2.0, 2.2))
    b.truss(V(-6.0, -2.4, 0.8), V(-2.0, -3.5, 2.0), 0.35, 0.12, M("Hull"), n=4)
    b.box(3.0, 3.0, 2.2, M("Hull"), loc=(0, 1.5, -4.2), bevel=0.2)
    b.nav_lights([(0, 1.5, 6.3), (-6.5, -2.0, 3.9)], s=0.2)


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


@model("farm", "Ringdeck mit sechs gruen leuchtenden Gewaechshauskuppeln, Speichen zur Nabe, Wassertanks darunter")
def farm(b):
    b.cyl(2.0, 2.4, M("Hull"), seg=12)
    b.sphere(1.6, M("Glass"), seg=12, rings=6, loc=(0, 0, 1.2), half=True)
    b.cyl(0.4, 3.0, M("HullDark"), seg=8, loc=(0, 0, -2.6))
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


@model("electronics_fab", "Gestapelte dunkle Platinen-Ebenen mit Chipgitter und leuchtenden Leiterbahnen, Ecksendemasten")
def electronics_fab(b):
    for i, (z, s) in enumerate(((-2.0, 9.0), (0.4, 7.6), (2.6, 6.0))):
        b.box(s, s, 0.45, M("HullDark"), loc=(0, 0, z), bevel=0.12)
        b.box(s + 0.1, 0.18, 0.2, M("Accent"), loc=(0, -s / 2, z + 0.1))
        for k in range(3):
            o = (k - 1) * s * 0.3
            b.box(s * 0.9, 0.08, 0.06, M("Light"), loc=(0, o, z + 0.25))
            b.box(0.08, s * 0.9, 0.06, M("Light"), loc=(o + s * 0.15, 0, z + 0.25))
        for x, y, a in polar(4, s * 0.38, 45):
            b.box(0.5, 0.5, 2.0 if i < 2 else 0.3, M("Hull"), loc=(x, y, z + 1.2 if i < 2 else z))
    for gx in range(3):
        for gy in range(3):
            if gx == 1 and gy == 1:
                continue
            b.box(1.1, 1.1, 0.35, M("Hull"), loc=((gx - 1) * 1.9, (gy - 1) * 1.9, 3.0), bevel=0.08)
    b.box(2.2, 2.2, 1.1, M("Accent"), loc=(0, 0, 3.4), bevel=0.15)
    b.box(1.4, 1.4, 0.2, M("Light"), loc=(0, 0, 4.0))
    for gx in range(4):
        b.box(0.9, 0.6, 0.3, M("Hull"), loc=(-3.0 + gx * 2.0, -3.3, -1.55))
    for x, y, a in polar(4, 6.3, 45):
        b.cyl(0.15, 6.5, M("Hull"), seg=6, loc=(x, y, -0.2))
        b.nav_lights([V(x, y, 3.0)], s=0.22)
    b.box(3.2, 3.2, 2.4, M("Hull"), loc=(0, 0, -3.6), bevel=0.2)


@model("plating_works", "Walzwerkshalle mit drei Walzenpaaren, Presse mit Warnstreifen, Stapel von Panzerplatten")
def plating_works(b):
    b.box(11, 4.0, 2.6, M("Hull"), loc=(0, 0, 0), bevel=0.3)
    b.box(11.2, 0.2, 0.4, M("Accent"), loc=(0, -2.05, 0.6))
    for i in range(6):
        b.box(1.2, 0.1, 0.35, M("Window"), loc=(-4.2 + i * 1.7, -2.05, -0.3))
    for i, x in enumerate((-3.4, -0.6, 2.2)):
        for z in (1.95, 3.25):
            b.cyl(0.6, 4.6, M("HullDark"), seg=12, loc=(x, 0, z), rot=(90, 0, 0))
        for y in (-2.0, 2.0):
            b.box(1.6, 0.4, 2.6, M("Hull"), loc=(x, y, 2.6))
    # Presse
    b.box(0.8, 0.8, 7.0, M("Hazard"), loc=(5.0, -2.3, 2.0))
    b.box(0.8, 0.8, 7.0, M("Hazard"), loc=(5.0, 2.3, 2.0))
    b.box(1.4, 5.6, 1.4, M("HullDark"), loc=(5.0, 0, 5.2), bevel=0.15)
    b.box(1.2, 3.4, 1.0, M("Hull"), loc=(5.0, 0, 3.4))
    # Plattenstapel
    for sx, sy in ((-4.0, 4.6), (-0.5, 4.6), (3.0, 4.6)):
        for k in range(6):
            mat = "Accent" if k % 3 == 2 else "Hull"
            b.box(2.8, 2.0, 0.24, M(mat), loc=(sx, sy, -0.9 + k * 0.3))
        b.beam(V(sx - 1.5, sy - 1.1, -1.2), V(sx - 1.5, sy - 1.1, 1.0), 0.15, M("HullDark"))
        b.beam(V(sx + 1.5, sy - 1.1, -1.2), V(sx + 1.5, sy - 1.1, 1.0), 0.15, M("HullDark"))
    b.box(10.0, 2.6, 0.3, M("HullDark"), loc=(-0.5, 4.6, -1.25))
    b.box(4.0, 3.0, 2.0, M("HullDark"), loc=(-2.0, 0, -2.2), bevel=0.2)
    for i in range(3):
        b.box(0.12, 3.6, 2.0, M("HullDark"), loc=(1.5 + i * 0.9, 0, -2.4))
    b.nav_lights([(5.0, 0, 6.1), (-5.5, -2.0, 1.4)], mat="LightRed", s=0.2)


@model("fuel_cell_fab", "Wabenfeld aus aufrechten Brennstoffzellen-Zylindern mit gelben Kappen und Lichtbaendern auf einer Sechseckplattform")
def fuel_cell_fab(b):
    hexa = [(math.cos(math.radians(60 * i + 30)) * 7.0, math.sin(math.radians(60 * i + 30)) * 7.0)
            for i in range(6)]
    b.prism(hexa, 1.0, M("HullDark"), loc=(0, 0, -1.5))
    b.prism([(x * 1.03, y * 1.03) for x, y in hexa], 0.3, M("Accent"), loc=(0, 0, -1.1))
    cells = [(0, 0, 5.0)] + [(x, y, 3.6) for x, y, a in polar(6, 2.6, 30)] + \
            [(x, y, 2.6) for x, y, a in polar(6, 4.9, 0)]
    for x, y, h in cells:
        b.cyl(1.05, h, M("Hull"), seg=12, loc=(x, y, -1.0 + h / 2))
        b.lathe([(1.07, h * 0.55 - 1.0 - 0.15), (1.07, h * 0.55 - 1.0 + 0.15)], M("Light"),
                seg=12, loc=(x, y, 0))
        b.lathe([(1.1, 0), (1.1, 0.35), (0.6, 0.55), (0, 0.6)], M("Hazard"), seg=12,
                loc=(x, y, -1.0 + h))
        b.cyl(0.18, 0.5, M("HullDark"), seg=6, loc=(x, y, -0.5 + h + 0.3))
    # Wasserstoffzufuhr
    for x, y, a in polar(3, 6.6, 0):
        b.lathe([(0, -1.6), (0.7, -1.3), (0.9, -0.5), (0.9, 0.5), (0.7, 1.3), (0, 1.6)],
                M("Hull"), seg=10, loc=(x, y, -3.0))
    b.lathe([(2.4, -2.0), (1.4, -4.4), (0.3, -5.0)], M("HullDark"), seg=12, cap_top=False)
    b.nav_lights([(0, 0, 4.7)], s=0.25)


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


@model("goods_factory", "Fabrikhalle mit Saegezahn-Dach und Oberlichtern, Containerstapel in drei Farben, Portalkran")
def goods_factory(b):
    saw = [(-5.0, -1.2), (5.0, -1.2), (5.0, 1.2)]
    for i in range(5):
        x0 = 5.0 - i * 2.0
        saw += [(x0 - 0.0, 1.2), (x0 - 2.0, 2.6)] if False else [(x0 - 2.0, 2.6), (x0 - 2.0, 1.2)]
    saw = [(-5.0, -1.2), (5.0, -1.2), (5.0, 1.2)]
    for i in range(5):
        xr = 5.0 - i * 2.0
        saw += [(xr - 2.0 + 0.001, 2.6), (xr - 2.0, 1.2)]
    saw[-1] = (-5.0, 1.2)
    b.prism(saw, 6.0, M("Hull"), rot=(90, 0, 0))
    for i in range(5):
        xr = 5.0 - i * 2.0
        b.box(0.1, 5.6, 1.1, M("Window"), loc=(xr - 2.03, 0, 1.9))
    b.box(10.2, 6.2, 0.35, M("Accent"), loc=(0, 0, -1.0))
    b.box(9.0, 5.0, 1.8, M("HullDark"), loc=(0, 0, -2.2), bevel=0.2)
    cols = ["Container", "Accent", "Hazard", "Hull"]
    k = 0
    for side in (-1, 1):
        for i in range(4):
            for j in range(2 if i % 2 else 3):
                b.box(2.1, 1.0, 0.95, M(cols[k % 4]), loc=(-3.6 + i * 2.4, side * 4.3, -1.3 + j * 1.0),
                      bevel=0.05)
                k += 1
        b.box(10.4, 1.4, 0.2, M("HullDark"), loc=(0, side * 4.3, -1.9))
    # Portalkran
    for x in (-6.2, 6.2):
        for y in (-4.6, 4.6):
            b.beam(V(x, y, -2.0), V(x, y, 3.6), 0.3, M("Hazard"))
        b.beam(V(x, -4.6, 3.6), V(x, 4.6, 3.6), 0.35, M("Hazard"))
    b.beam(V(-6.2, 4.6, 3.6), V(6.2, 4.6, 3.6), 0.35, M("HullDark"))
    b.box(1.2, 1.2, 0.8, M("HullDark"), loc=(2.0, 4.6, 3.1))
    b.beam(V(2.0, 4.6, 2.7), V(2.0, 4.6, 0.8), 0.08, M("Hull"))
    b.nav_lights([(-6.2, -4.6, 3.9), (6.2, 4.6, 3.9)], mat="LightRed", s=0.2)


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


@model("fuel_refinery", "Raffinerie: zwei Kugeltanks auf Stelzen, gestreifte Liegetanks, Destillationsturm, Fackel mit Flamme")
def fuel_refinery(b):
    b.box(12, 6, 0.6, M("HullDark"), loc=(0, 0, -2.6), bevel=0.15)
    for x in (-4.0, 0.0):
        b.sphere(2.2, M("Hull"), seg=14, rings=8, loc=(x, 1.2, 0.6))
        b.torus(2.22, 0.14, M("Accent"), seg=14, minor=4, loc=(x, 1.2, 0.6))
        for px, py, a in polar(4, 1.6, 45):
            b.beam(V(x + px, 1.2 + py, -2.3), V(x + px * 0.8, 1.2 + py * 0.8, -0.5), 0.2, M("HullDark"))
    for i, y in enumerate((-1.8, -3.4)):
        b.lathe([(0, -3.0), (0.6, -2.8), (0.8, -2.3), (0.8, 2.3), (0.6, 2.8), (0, 3.0)],
                M("Hazard" if i == 0 else "Hull"), seg=10, loc=(-2.0, y, -1.5), rot=(0, 90, 0))
        b.torus(0.82, 0.08, M("HullDark"), seg=10, minor=4, loc=(-3.5, y, -1.5), rot=(0, 90, 0))
        b.torus(0.82, 0.08, M("HullDark"), seg=10, minor=4, loc=(-0.5, y, -1.5), rot=(0, 90, 0))
    # Destillationsturm
    prof = [(1.2, -2.3)]
    for k in range(5):
        prof += [(1.2, -1.0 + k * 1.6), (1.45, -0.9 + k * 1.6), (1.45, -0.6 + k * 1.6), (1.2, -0.5 + k * 1.6)]
    prof += [(1.0, 6.8), (0.4, 7.4), (0, 7.5)]
    b.lathe(prof, M("Hull"), seg=12, loc=(4.0, 0.0, 0), cap_bottom=True)
    b.window_band(1.25, 0.2, 0.25, n=4, seg=12, loc=(4.0, 0, 0))
    # Fackel
    b.cyl(0.25, 9.0, M("HullDark"), seg=6, loc=(5.6, 2.4, 2.2))
    b.lathe([(0, 6.7), (0.55, 7.1), (0.35, 8.0), (0, 9.2)], M("Glow"), seg=8, loc=(5.6, 2.4, 0))
    for z in (-1.0, 2.0, 5.0):
        b.beam(V(4.0, 0, z), V(5.6, 2.4, z), 0.12, M("HullDark"))
    # Rohre
    b.strut(V(-4.0, 1.2, -1.2), V(4.0, 0, -1.2), 0.25, M("Copper"))
    b.strut(V(0.0, 1.2, -0.8), V(0.0, -1.8, -0.8), 0.2, M("Copper"))
    b.strut(V(4.0, 0, -1.8), V(4.0, -3.4, -1.8), 0.2, M("Copper"))
    b.nav_lights([(4.0, 0, 7.7), (-6.0, -3.0, -2.2), (6.0, -3.0, -2.2)], mat="LightRed", s=0.2)


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


@model("weapons_works", "Gepanzerter, kantiger Keilbau mit Rohrgestellen, Schiessbahn mit Zielring, Schildprojektor, Warnlichter")
def weapons_works(b):
    body = [(-5.5, -2.0), (-3.5, -3.6), (3.0, -3.6), (5.5, -1.6), (5.5, 1.6), (3.0, 3.6), (-3.5, 3.6), (-5.5, 2.0)]
    b.prism(body, 3.2, M("HullDark"), top_scale=0.8)
    b.prism([(x * 0.82, y * 0.82) for x, y in body], 0.25, M("Accent"), loc=(0, 0, 1.7))
    b.prism([(x * 1.0, y * 1.0) for x, y in body], 1.6, M("Hull"), loc=(0, 0, -2.4), top_scale=1.0)
    for i in range(4):
        b.box(1.2, 0.1, 0.3, M("Window"), loc=(-3 + i * 1.8, -3.62, -0.2))
    # Rohrgestelle oben
    for row in range(2):
        y = -1.2 + row * 2.4
        b.box(5.4, 0.6, 0.5, M("Hull"), loc=(-0.6, y, 2.0))
        for k in range(4):
            b.cyl(0.16, 5.0, M("Hull"), seg=6, loc=(-0.6, y - 0.3 + k * 0.2, 2.45), rot=(0, 90, 0))
    # Schiessbahn
    for y in (-0.6, 0.6):
        b.beam(V(5.0, y, -0.5), V(11.0, y, -0.5), 0.3, M("Hull"))
    for x in (7.0, 9.0):
        b.beam(V(x, -0.6, -0.5), V(x, 0.6, -0.5), 0.2, M("HullDark"))
    b.ring(1.5, 0.3, 0.3, M("Hazard"), seg=14, loc=(11.0, 0, -0.5), rot=(0, 90, 0))
    b.ring(0.7, 0.2, 0.25, M("LightRed"), seg=10, loc=(11.0, 0, -0.5), rot=(0, 90, 0))
    b.turret((4.0, 0, 1.6), facing=0, s=0.9)
    # Schildprojektor
    b.cyl(0.9, 1.0, M("Hull"), seg=10, loc=(-4.0, 0, 2.2))
    b.sphere(1.6, M("Light"), seg=12, rings=6, loc=(-4.0, 0, 2.7), half=True, scale=(1, 1, 0.5))
    b.ring(1.75, 0.3, 0.3, M("HullDark"), seg=12, loc=(-4.0, 0, 2.75))
    b.nav_lights([(x * 1.02, y * 1.02, -1.5) for x, y in body[::2]], mat="LightRed", s=0.22)


@model("systems_works", "Verschachtelte Modulwuerfel wie ein Baukasten, zwei Roboterarme, Sensorschuesseln und Antennenfeld")
def systems_works(b):
    cubes = [((0, 0, 0), 4.0, "Hull"), ((3.4, 1.0, 1.0), 2.6, "Accent"), ((-3.2, -0.6, -0.8), 2.8, "HullDark"),
             ((0.6, -3.2, 1.2), 2.4, "Hull"), ((-1.0, 3.0, -1.4), 2.2, "Hull"), ((1.6, 2.4, 2.6), 1.6, "HullDark"),
             ((-2.6, 2.0, 1.8), 1.8, "Accent"), ((2.6, -2.2, -1.6), 2.0, "HullDark")]
    for (x, y, z), s, m in cubes:
        b.box(s, s, s, M(m), loc=(x, y, z), bevel=0.18)
        b.box(s * 0.6, 0.08, 0.18, M("Light"), loc=(x, y - s / 2 - 0.02, z + s * 0.2))
    # Roboterarme
    for base, s in ((V(4.6, -1.0, 1.0), 1), (V(-4.6, 0.6, -0.8), -1)):
        j1 = base + V(s * 1.6, 0, 2.2)
        j2 = j1 + V(s * 2.0, s * -1.2, -0.8)
        tip = j2 + V(s * 0.8, s * -0.6, -1.4)
        b.sphere(0.45, M("HullDark"), seg=8, rings=5, loc=base)
        b.beam(base, j1, 0.4, M("Hazard"))
        b.sphere(0.4, M("HullDark"), seg=8, rings=5, loc=j1)
        b.beam(j1, j2, 0.32, M("Hazard"))
        b.sphere(0.32, M("HullDark"), seg=8, rings=5, loc=j2)
        b.beam(j2, tip, 0.18, M("Hull"))
        b.nav_lights([tip], s=0.18)
    b.dish((1.6, 2.4, 3.4), 1.6, M("Accent"), rot=(25, -20, 0))
    b.dish((-2.6, 2.0, 2.7), 1.0, M("Hull"), rot=(-30, 15, 0))
    for i in range(5):
        h = 2.0 + (i % 3) * 0.9
        b.cyl(0.07, h, M("HullDark"), seg=5, loc=(-1.4 + i * 0.6, -1.0, 2.0 + h / 2))
        b.nav_lights([V(-1.4 + i * 0.6, -1.0, 2.0 + h)], s=0.12)
    b.cyl(1.2, 2.0, M("HullDark"), seg=10, loc=(0, 0, -3.0))


# =============================================================== Handel und Lager
@model("trade_station", "Grosse Handelsnabe (Doppelkegel mit Fensterbaendern), acht Andockarme mit Leuchtkragen, Containerring")
def trade_station(b):
    b.lathe([(0, -5.5), (1.2, -5.0), (3.0, -2.6), (4.2, -0.6), (4.2, 0.6), (3.0, 2.6), (1.4, 4.2),
             (0.6, 5.0), (0, 5.2)], M("Hull"), seg=16)
    b.window_band(4.25, 0, 0.35, n=8, seg=16, gap=0.3)
    b.window_band(3.5, 1.9, 0.25, n=8, seg=16, gap=0.4, loc=(0, 0, 0))
    b.ring(4.4, 0.4, 0.3, M("Accent"), seg=16, loc=(0, 0, 0.75))
    b.ring(4.4, 0.4, 0.3, M("Accent"), seg=16, loc=(0, 0, -0.75))
    for x, y, a in polar(8, 4.0, 22.5):
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        b.cyl(0.4, 4.6, M("HullDark"), seg=8, loc=d * 6.0, rot=(0, 90, a))
        b.ring(0.95, 0.35, 0.6, M("Hull"), seg=10, loc=d * 8.5, rot=(0, 90, a))
        b.ring(0.7, 0.12, 0.62, M("Light"), seg=10, loc=d * 8.5, rot=(0, 90, a))
    for x, y, a in polar(12, 6.1, 0):
        z = 1.7 if int(a / 30) % 2 else -1.7
        b.box(1.4, 0.9, 0.9, M(("Container", "Accent", "Hazard")[int(a / 30) % 3]),
              loc=(x, y, z), rot=(0, 0, a))
    b.ring(6.1, 0.3, 0.3, M("HullDark"), seg=24, loc=(0, 0, 1.1))
    b.ring(6.1, 0.3, 0.3, M("HullDark"), seg=24, loc=(0, 0, -1.1))
    for x, y, a in polar(4, 0, 0):
        pass
    for x, y, a in polar(4, 4.0, 0):
        b.beam(V(x, y, 1.0), V(x * 1.5, y * 1.5, 1.1), 0.2, M("HullDark"))
        b.beam(V(x, y, -1.0), V(x * 1.5, y * 1.5, -1.1), 0.2, M("HullDark"))
    b.cyl(0.2, 3.0, M("HullDark"), seg=6, loc=(0, 0, 6.5))
    b.nav_lights([(0, 0, 8.1), (0, 0, -5.7)], s=0.25)


@model("trade_post", "Planetarer Umschlagplatz: schwere Plattform mit drei auskragenden Landefeldern (zwei Faehren), Frachtterminal mit Kuppel, Kontrollturm, Orbitalaufzug nach unten")
def trade_post(b):
    b.lathe([(0, -3.2), (2.4, -2.6), (5.0, -1.4), (6.4, -0.6), (6.6, 0.0), (6.3, 0.4), (0, 0.4)],
            M("Hull"), seg=16)
    b.ring(6.7, 0.35, 0.35, M("Accent"), seg=16)
    b.window_band(5.6, -1.0, 0.25, n=8, seg=16, gap=0.35)
    pads = polar(3, 8.4, 90)
    for i, (x, y, a) in enumerate(pads):
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        b.beam(d * 5.6 + V(0, 0, 0.0), d * 7.0 + V(0, 0, 0.0), 1.0, M("HullDark"), h=0.5)
        b.beam(d * 4.6 + V(0, 0, -1.6), d * 7.6 + V(0, 0, -0.1), 0.3, M("HullDark"))
        b.cyl(2.2, 0.35, M("HullDark"), seg=12, loc=(x, y, 0.0))
        b.ring(2.15, 0.28, 0.4, M("Hazard"), seg=12, loc=(x, y, 0.02))
        b.box(2.4, 0.25, 0.06, M("Accent"), loc=(x, y, 0.2), rot=(0, 0, a))
        b.nav_lights([(x + px, y + py, 0.3) for px, py, aa in polar(4, 2.2, 45)], s=0.14)
        if i < 2:
            sx, sy = x, y
            b.prism([(-1.5, -0.75), (1.0, -0.4), (1.7, 0), (1.0, 0.4), (-1.5, 0.75)], 0.7, M("Accent"),
                    loc=(sx, sy, 0.75), rot=(0, 0, a + 140), top_scale=0.65)
            b.box(0.6, 2.8, 0.08, M("Hull"), loc=(sx, sy, 0.55), rot=(0, 0, a + 140))
            b.box(0.3, 0.5, 0.25, M("Light"), loc=(sx, sy, 0.75), rot=(0, 0, a + 140))
    # Frachtterminal
    b.lathe([(3.2, 0.3), (3.2, 1.2), (2.8, 1.6)], M("Hull"), seg=16, cap_top=True)
    b.sphere(2.6, M("Glass"), seg=16, rings=8, loc=(0, 0, 1.55), half=True, scale=(1, 1, 0.7))
    b.ring(2.75, 0.3, 0.3, M("Accent"), seg=16, loc=(0, 0, 1.6))
    b.window_band(3.25, 0.8, 0.3, n=8, seg=16, gap=0.3)
    # Kontrollturm
    b.cyl(0.5, 4.0, M("Hull"), seg=10, loc=(-3.4, -2.4, 2.2))
    b.lathe([(0.5, 0), (1.4, 0.4), (1.5, 1.0), (0.9, 1.5), (0, 1.6)], M("Hull"), seg=12, loc=(-3.4, -2.4, 4.0))
    b.lathe([(1.48, 0.5), (1.48, 0.9)], M("Window"), seg=12, loc=(-3.4, -2.4, 4.0))
    b.cyl(0.07, 1.6, M("HullDark"), seg=5, loc=(-3.4, -2.4, 6.3))
    b.nav_lights([(-3.4, -2.4, 7.15)], mat="LightRed", s=0.18)
    # Containerstapel
    cols = ("Container", "Accent", "Hazard", "Hull")
    for k in range(5):
        b.box(1.5, 0.8, 0.75, M(cols[k % 4]), loc=(3.0 + (k % 3) * 0.0, -3.6 + (k % 3) * 0.9, 0.78 + (k // 3) * 0.78),
              rot=(0, 0, 20))
    # Orbitalaufzug
    b.cyl(0.9, 1.4, M("HullDark"), seg=10, loc=(0, 0, -3.6))
    b.cyl(0.32, 8.0, M("Accent"), seg=8, loc=(0, 0, -8.2))
    for z in (-5.6, -9.0, -11.6):
        b.cyl(0.4, 0.3, M("Light"), seg=8, loc=(0, 0, z))
    b.lathe([(0, -1.0), (0.9, -0.8), (1.1, 0), (0.9, 0.8), (0, 1.0)], M("Hull"), seg=10, loc=(0, 0, -7.4),
            scale=(1, 1, 0.9))
    b.lathe([(1.12, -0.15), (1.12, 0.15)], M("Window"), seg=10, loc=(0, 0, -7.4))

@model("build_site", "Baustelle: Geruest aus gelb-dunklen Streben, halbfertiger Kern, lose Platten, Baukran mit Ausleger, Warnlichter")
def build_site(b):
    S, H = 4.5, 3.5
    for x in (-S, S):
        for y in (-S, S):
            b.beam(V(x, y, -H), V(x, y, H), 0.3, M("Hazard"))
    for z in (-H, 0, H):
        for (p, q) in (((-S, -S), (S, -S)), ((S, -S), (S, S)), ((S, S), (-S, S)), ((-S, S), (-S, -S))):
            b.beam(V(p[0], p[1], z), V(q[0], q[1], z), 0.22, M("HullDark" if z else "Hazard"))
    for (p, q) in (((-S, -S), (S, -S)), ((S, S), (-S, S))):
        b.beam(V(p[0], p[1], -H), V(q[0], q[1], 0), 0.15, M("HullDark"))
        b.beam(V(q[0], q[1], 0), V(p[0], p[1], H), 0.15, M("HullDark"))
    for (p, q) in (((S, -S), (S, S)), ((-S, S), (-S, -S))):
        b.beam(V(p[0], p[1], -H), V(q[0], q[1], 0), 0.15, M("HullDark"))
    # halbfertiger Kern
    b.cyl(2.2, 3.0, M("Hull"), seg=12, loc=(0, 0, -2.0))
    b.ring(2.3, 0.3, 0.3, M("Accent"), seg=12, loc=(0, 0, -0.5))
    for x, y, a in polar(8, 2.1, 0):
        b.beam(V(x, y, -0.5), V(x, y, 2.0 if int(a / 45) % 3 else 3.0), 0.18, M("Hull"))
    b.ring(2.2, 0.2, 0.2, M("HullDark"), seg=12, loc=(0, 0, 1.0), arc=200)
    # Bodenplatten und lose Platten
    b.box(4.4, 4.4, 0.2, M("Hull"), loc=(-2.2, -2.2, -H - 0.05))
    b.box(4.4, 4.4, 0.2, M("HullDark"), loc=(2.2, 2.2, -H - 0.05))
    b.box(4.0, 2.0, 0.15, M("Hull"), loc=(2.2, -2.2, 0.0))
    for (x, y, z, rx, rz) in ((6.2, 1.5, 1.0, 25, 10), (5.8, -2.8, -1.8, -15, 40), (-6.0, 3.0, 2.2, 35, -20)):
        b.box(2.0, 1.4, 0.15, M("Hull"), loc=(x, y, z), rot=(rx, 0, rz))
    b.box(2.0, 1.0, 1.0, M("Container"), loc=(-2.6, 2.6, -2.9), bevel=0.05)
    b.box(1.6, 1.0, 1.0, M("Hazard"), loc=(-2.4, 2.6, -1.9), bevel=0.05)
    # Kran
    b.truss(V(-S, -S, H), V(-S, -S, H + 5.0), 0.4, 0.12, M("Hazard"), n=5)
    b.truss(V(-S - 2.0, -S, H + 5.0), V(S + 1.0, -S + 2.0, H + 5.0), 0.3, 0.1, M("Hazard"), n=7)
    b.box(1.2, 1.2, 1.0, M("HullDark"), loc=(-S - 2.0, -S, H + 4.6))
    b.beam(V(2.0, -S + 1.4, H + 4.8), V(2.0, -S + 1.4, H + 0.8), 0.06, M("Hull"))
    b.box(1.6, 1.0, 0.2, M("Hull"), loc=(2.0, -S + 1.4, H + 0.7))
    b.nav_lights([V(x, y, H + 0.25) for x in (-S, S) for y in (-S, S)], mat="Glow", s=0.25)
    b.nav_lights([V(S + 1.0, -S + 2.0, H + 5.3)], mat="LightRed", s=0.22)


@model("warehouse", "Grosses Regalgitter (3x3x2 Faecher) mit Containern verschiedener Farben, Mittelspine mit Andockkragen, Verladekran")
def warehouse(b):
    n, c = 3, 2.8
    half = n * c / 2
    zs = (-c, 0, c)
    for i in range(n + 1):
        for j in range(n + 1):
            x, y = -half + i * c, -half + j * c
            b.beam(V(x, y, zs[0]), V(x, y, zs[-1]), 0.25, M("HullDark"))
    for z in zs:
        for i in range(n + 1):
            x = -half + i * c
            b.beam(V(x, -half, z), V(x, half, z), 0.22, M("Hull" if z else "Accent"))
            b.beam(V(-half, x, z), V(half, x, z), 0.22, M("Hull" if z else "Accent"))
    cols = ["Container", "Hull", "Accent", "Hazard", "HullDark"]
    k = 0
    for i in range(n):
        for j in range(n):
            for lv in range(2):
                k += 1
                if (i * 7 + j * 3 + lv * 5) % 6 == 0 or (i == 1 and j == 1):
                    continue
                x, y = -half + (i + 0.5) * c, -half + (j + 0.5) * c
                z = zs[lv] + c / 2
                m = cols[(i * 5 + j * 2 + lv * 3) % 5]
                if (i + j + lv) % 3 == 0:
                    b.box(c * 0.82, c * 0.38, c * 0.82, M(m), loc=(x, y - c * 0.21, z), bevel=0.05)
                    b.box(c * 0.82, c * 0.38, c * 0.82, M(cols[(k + 1) % 5]), loc=(x, y + c * 0.21, z),
                          bevel=0.05)
                else:
                    b.box(c * 0.85, c * 0.85, c * 0.85, M(m), loc=(x, y, z), bevel=0.06)
    # Mittelspine
    b.cyl(0.8, 12.0, M("Hull"), seg=10)
    b.ring(1.4, 0.4, 0.5, M("HullDark"), seg=10, loc=(0, 0, 5.2))
    b.ring(1.1, 0.12, 0.52, M("Light"), seg=10, loc=(0, 0, 5.2))
    b.ring(1.4, 0.4, 0.5, M("HullDark"), seg=10, loc=(0, 0, -5.2))
    # Kran
    b.beam(V(half, -half, c), V(half + 2.2, -half, c + 2.2), 0.35, M("Hazard"))
    b.beam(V(half + 2.2, -half, c + 2.2), V(half + 2.2, -half, 0.5), 0.07, M("Hull"))
    b.box(1.6, 0.9, 0.9, M("Container"), loc=(half + 2.2, -half, 0.0), bevel=0.05)
    b.nav_lights([V(x, y, c + 0.2) for x in (-half, half) for y in (-half, half)], s=0.2)


# =============================================================== Regierung, Sicherheit, Sonstiges
@model("headquarters", "Repraesentativer Stufenturm mit Kronenspitze und Fensterbaendern, vier Fraktionsbanner, Landearme, drei Geschuetze (eins mit Raketen)")
def headquarters(b):
    prof = [(0, -6.0), (2.0, -5.4), (4.4, -3.6), (6.6, -1.8), (6.8, -1.0), (5.0, -0.6),
            (4.6, 0.6), (3.4, 1.0), (3.2, 3.2), (2.4, 3.6), (2.2, 6.2), (1.5, 6.6), (1.3, 8.8),
            (0.6, 9.4), (0.3, 11.5), (0, 12.2)]
    b.lathe(prof, M("Hull"), seg=16)
    b.window_band(4.7, 0.0, 0.35, n=8, seg=16, gap=0.25)
    b.window_band(3.27, 1.9, 0.3, n=8, seg=16, gap=0.3)
    b.window_band(3.27, 2.6, 0.3, n=8, seg=16, gap=0.3)
    b.window_band(2.25, 4.4, 0.3, n=6, seg=12, gap=0.3)
    b.window_band(2.25, 5.3, 0.3, n=6, seg=12, gap=0.3)
    b.window_band(1.35, 7.6, 0.3, n=4, seg=12, gap=0.3)
    b.ring(6.9, 0.4, 0.4, M("Accent"), seg=16, loc=(0, 0, -1.4))
    b.ring(3.5, 0.25, 0.3, M("Accent"), seg=16, loc=(0, 0, 3.4))
    b.ring(2.3, 0.2, 0.3, M("Accent"), seg=12, loc=(0, 0, 6.4))
    # Krone
    for x, y, a in polar(4, 1.5, 0):
        b.beam(V(x, y, 8.6), V(x * 0.3, y * 0.3, 10.6), 0.18, M("Accent"))
    b.nav_lights([(0, 0, 12.3)], mat="Window", s=0.3)
    # Banner
    for x, y, a in polar(4, 0, 45):
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        b.prism([(0, 0), (3.6, 0), (3.0, 5.8), (0.4, 7.0)], 0.25, M("Accent"),
                loc=d * 2.9 + V(0, 0, -0.4), rot=(90, 0, a))
    # Landearme
    for x, y, a in polar(4, 6.0, 0):
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        b.beam(d * 5.6 + V(0, 0, -1.6), d * 9.0 + V(0, 0, -1.6), 0.5, M("HullDark"))
        b.cyl(1.0, 0.25, M("HullDark"), seg=10, loc=d * 9.4 + V(0, 0, -1.6))
        b.ring(1.0, 0.18, 0.28, M("Hazard"), seg=10, loc=d * 9.4 + V(0, 0, -1.6))
        b.nav_lights([d * 9.4 + V(0, 0, -1.35)], s=0.18)
    # Geschuetze
    for x, y, a in polar(2, 5.6, 90):
        b.turret((x, y, -0.6), facing=a, s=0.85)
    b.box(1.4, 1.6, 0.9, M("HullDark"), loc=(5.4, 0, -0.1), bevel=0.12)
    for k in range(4):
        b.cyl(0.18, 0.2, M("LightRed"), seg=6, loc=(5.4 + (k % 2 - 0.5) * 0.6, (k // 2 - 0.5) * 0.7, 0.36))
    b.lathe([(0, -9.0), (1.0, -8.0), (1.8, -5.8)], M("HullDark"), seg=12)
    b.nav_lights([(0, 0, -9.1)], mat="LightRed", s=0.25)


@model("company_seat", "Schlanker Konzernturm: Hochhausscheibe mit Glasfront und Fensterraster, Logoring, Nebenbau, Landeplattform")
def company_seat(b):
    b.box(3.0, 6.0, 15.0, M("Hull"), loc=(0, 0, 1.5), bevel=0.3)
    b.box(3.1, 4.6, 13.0, M("Glass"), loc=(0, 0, 1.5))
    for k in range(9):
        z = -4.0 + k * 1.4
        b.box(3.16, 4.4, 0.18, M("Window"), loc=(0, 0, z))
    b.box(0.2, 6.1, 14.6, M("Accent"), loc=(0, 0, 1.5))
    b.box(3.2, 6.2, 0.6, M("HullDark"), loc=(0, 0, 9.3))
    b.prism([(-1.5, -3.0), (1.5, -3.0), (1.5, 3.0), (-1.5, 3.0)], 2.0, M("Hull"), loc=(0, 0, 10.6),
            top_scale=0.5)
    b.cyl(0.08, 2.4, M("HullDark"), seg=5, loc=(0, 1.5, 12.6))
    b.nav_lights([(0, 1.5, 13.8)], mat="LightRed", s=0.2)
    b.torus(4.6, 0.32, M("Accent"), seg=24, minor=6, loc=(0, 0, 6.2), rot=(0, 0, 0))
    for x, y, a in polar(4, 4.6, 45):
        b.beam(V(x * 0.3, y * 0.6, 6.2), V(x, y, 6.2), 0.15, M("HullDark"))
    # Nebenbau
    b.box(5.0, 3.6, 3.0, M("Hull"), loc=(4.0, -2.0, -4.2), bevel=0.3)
    b.box(5.1, 3.7, 0.4, M("Window"), loc=(4.0, -2.0, -3.6))
    # Landeplattform
    b.beam(V(-1.5, 1.5, -2.0), V(-5.0, 3.5, -2.0), 0.6, M("HullDark"))
    b.cyl(2.0, 0.3, M("HullDark"), seg=14, loc=(-6.0, 4.0, -2.0))
    b.ring(1.95, 0.25, 0.32, M("Hazard"), seg=14, loc=(-6.0, 4.0, -2.0))
    b.lathe([(0, -9.0), (1.2, -7.2), (2.0, -6.0)], M("HullDark"), seg=10)


@model("sector_hq", "Achteckige Verwaltungsplattform mit Turm und Fensterband, grosse Funkschuessel, zwei Fraktionsbanner, zwei Geschuetze")
def sector_hq(b):
    octa = [(math.cos(math.radians(22.5 + 45 * i)) * 7.0, math.sin(math.radians(22.5 + 45 * i)) * 7.0)
            for i in range(8)]
    b.prism(octa, 1.6, M("Hull"), loc=(0, 0, -0.8), top_scale=0.92)
    b.prism([(x * 0.6, y * 0.6) for x, y in octa], 2.4, M("HullDark"), loc=(0, 0, -2.6), top_scale=1.4)
    b.prism([(x * 1.02, y * 1.02) for x, y in octa], 0.3, M("Accent"), loc=(0, 0, -1.3))
    for i in range(8):
        a = math.radians(45 * i)
        b.box(2.0, 0.1, 0.3, M("Window"), loc=(math.cos(a) * 6.9, math.sin(a) * 6.9, -0.6),
              rot=(0, 0, 45 * i + 90))
    b.lathe([(0, 0), (2.6, 0), (2.6, 2.0), (2.0, 2.6), (2.0, 5.0), (1.4, 5.6), (0, 5.8)],
            M("Hull"), seg=12)
    b.window_band(2.65, 1.2, 0.35, n=6, seg=12)
    b.window_band(2.05, 3.8, 0.3, n=6, seg=12)
    b.ring(2.15, 0.25, 0.3, M("Accent"), seg=12, loc=(0, 0, 4.6))
    b.dish((-3.6, 3.6, 0.8), 2.4, M("Hull"), rot=(-35, -25, 0))
    b.cyl(0.4, 1.4, M("HullDark"), seg=8, loc=(-3.6, 3.6, 0.4))
    for x, y, a in polar(2, 0, 0):
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0)
        b.prism([(0, 0), (2.4, 0), (2.0, 4.0), (0.3, 4.8)], 0.22, M("Accent"),
                loc=d * 2.5 + V(0, 0, 0.5), rot=(90, 0, a))
    b.turret((4.4, -3.0, 0.0), facing=-40, s=0.9)
    b.turret((-4.4, -3.0, 0.0), facing=-140, s=0.9)
    b.cyl(0.1, 2.0, M("HullDark"), seg=5, loc=(0, 0, 6.8))
    b.nav_lights([(0, 0, 7.9)] + [(x, y, -0.4) for x, y in octa[::2]], s=0.2)


@model("office", "Kompaktes Bueromodul: liegende Kapsel mit Fensterband und Andockkragen, kleines Solarpaar, Antenne")
def office(b):
    b.lathe([(0, -4.6), (1.4, -4.2), (2.0, -3.2), (2.1, 3.2), (1.4, 4.2), (0, 4.6)], M("Hull"), seg=14,
            rot=(0, 90, 0))
    b.lathe([(2.13, -2.8), (2.13, 2.8)], M("Glass"), seg=14, rot=(0, 90, 0), arc=150, start=15)
    for x in (-2.2, -0.75, 0.75, 2.2):
        b.lathe([(2.16, -0.4), (2.16, 0.4)], M("Window"), seg=14, loc=(x, 0, 0), rot=(0, 90, 0),
                arc=140, start=20)
    b.torus(2.12, 0.16, M("Accent"), seg=14, minor=4, loc=(-3.2, 0, 0), rot=(0, 90, 0))
    b.torus(2.12, 0.16, M("Accent"), seg=14, minor=4, loc=(3.2, 0, 0), rot=(0, 90, 0))
    b.cyl(0.9, 1.4, M("HullDark"), seg=10, loc=(-5.0, 0, 0), rot=(0, 90, 0))
    b.ring(1.1, 0.25, 0.4, M("Light"), seg=10, loc=(-5.7, 0, 0), rot=(0, 90, 0))
    b.box(1.6, 1.6, 1.4, M("Hull"), loc=(1.0, 0, -2.4), bevel=0.15)
    for s in (-1, 1):
        b.beam(V(1.0, s * 1.0, -2.4), V(1.0, s * 3.0, -2.4), 0.2, M("HullDark"))
        b.box(2.4, 3.2, 0.08, M("Panel"), loc=(1.0, s * 4.6, -2.4), rot=(20 * s, 0, 0))
        b.box(2.5, 3.3, 0.06, M("Hull"), loc=(1.0, s * 4.6, -2.48), rot=(20 * s, 0, 0))
    b.cyl(0.08, 2.4, M("HullDark"), seg=5, loc=(2.6, 0, 3.0))
    b.dish((-1.6, 0, 2.2), 0.9, M("Hull"), rot=(0, -30, 0))
    b.nav_lights([(2.6, 0, 4.3), (4.7, 0, 0)], mat="LightRed", s=0.18)


@model("police_station", "Wehrhafter sechszackiger Stern in dunkler Panzerung, Kommandoturm, blaue und rote Signallichter, Hangartore, drei Geschuetze")
def police_station(b):
    star = []
    for i in range(12):
        r = 8.0 if i % 2 == 0 else 3.8
        a = math.radians(30 * i)
        star.append((r * math.cos(a), r * math.sin(a)))
    b.prism(star, 2.2, M("HullDark"), top_scale=0.75)
    b.prism(star, 1.4, M("HullDark"), loc=(0, 0, -1.8), top_scale=1.0, scale=(0.8, 0.8, 1))
    b.prism([(x * 0.78, y * 0.78) for x, y in star], 0.2, M("Accent"), loc=(0, 0, 1.15))
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
    b.lathe([(0, -5.0), (1.2, -4.0), (2.4, -2.6)], M("HullDark"), seg=12)
    b.nav_lights([(0, 0, -5.1)], mat="Light", s=0.25)


@model("pirate_hideout", "In einen kantigen Asteroiden gebaut: Hangarmaul mit Gluehen und roten Lichtern, Andockroehre, angeschraubte Wohnmodule, Geruest mit Beutecontainern, Antennen")
def pirate_hideout(b):
    b.rock(5.6, M("Rock"), seed=1.7, seg=14, rings=9, scale=(1.3, 1.0, 0.75), amp=0.32)
    b.rock(2.2, M("Rock"), seed=4.1, seg=9, rings=6, loc=(5.9, 3.6, -1.6), amp=0.4)
    b.rock(1.3, M("Rock"), seed=2.9, seg=8, rings=5, loc=(-7.6, 3.6, 2.2), amp=0.4)
    b.rock(0.8, M("Rock"), seed=6.3, seg=7, rings=5, loc=(7.8, -2.0, 2.6), amp=0.4)
    # Hangarmaul zur Kamera (-x/-y)
    d = V(-0.62, -0.78, 0).normalized()
    rot = (0, 90, math.degrees(math.atan2(d.y, d.x)))
    b.cyl(2.3, 2.0, M("HullDark"), seg=10, loc=d * 4.4 + V(0, 0, -0.6), rot=rot)
    b.cyl(1.75, 0.3, M("Glow"), seg=10, loc=d * 5.45 + V(0, 0, -0.6), rot=rot)
    b.ring(2.45, 0.5, 0.6, M("Hull"), seg=10, loc=d * 5.3 + V(0, 0, -0.6), rot=rot)
    n = V(-d.y, d.x, 0)
    for x, y, a in polar(6, 2.6, 0):
        b.sphere(0.2, M("LightRed"), seg=6, rings=4, loc=d * 5.65 + n * x + V(0, 0, y - 0.6))
    for k in range(4):
        b.box(0.5, 0.3, 0.2, M("Hazard"), loc=d * 5.6 + n * (-1.5 + k) + V(0, 0, -3.1),
              rot=(0, 0, math.degrees(math.atan2(d.y, d.x))))
    # Andockroehre
    b.cyl(0.85, 5.0, M("HullDark"), seg=10, loc=(-7.4, 0.8, 0.6), rot=(0, 90, 10))
    b.ring(1.3, 0.45, 0.7, M("Hull"), seg=10, loc=(-9.9, 0.4, 0.6), rot=(0, 90, 10))
    b.ring(0.95, 0.15, 0.72, M("LightRed"), seg=10, loc=(-9.9, 0.4, 0.6), rot=(0, 90, 10))
    for x in (-8.4, -6.6):
        b.torus(0.9, 0.15, M("Hull"), seg=10, minor=4, loc=(x, 0.6 + (x + 7.4) * 0.17, 0.6), rot=(0, 90, 10))
    # Wohnmodule oben
    b.box(3.4, 2.2, 1.6, M("Hull"), loc=(-1.0, -0.8, 4.0), bevel=0.15, rot=(4, -6, 20))
    b.box(3.0, 0.15, 0.35, M("Window"), loc=(-1.25, -1.85, 4.15), rot=(4, -6, 20))
    b.cyl(0.9, 3.4, M("HullDark"), seg=10, loc=(1.8, 0.8, 3.6), rot=(0, 90, -25))
    b.window_band(0.92, 0, 0.3, n=4, seg=10, loc=(1.8, 0.8, 3.6), rot=(0, 90, -25))
    b.box(1.8, 0.3, 0.6, M("Accent"), loc=(1.8, 0.8, 4.55), rot=(0, 0, -25))
    for i, (x, y, h) in enumerate(((0.4, 1.8, 4.0), (2.8, -0.6, 3.0), (-2.6, 0.6, 2.4))):
        b.cyl(0.08, h, M("HullDark"), seg=5, loc=(x, y, 4.3 + h / 2))
        b.nav_lights([V(x, y, 4.3 + h)], mat="LightRed", s=0.2)
    b.dish((-3.4, 2.6, 3.4), 1.3, M("HullDark"), rot=(-25, -30, 0))
    # Geruest mit Beutecontainern (rechts)
    for x in (4.2, 6.8):
        for y in (-3.4, -1.6):
            b.beam(V(x, y, -2.4), V(x, y, 2.2), 0.2, M("Hull"))
    for z in (-2.4, -0.1, 2.2):
        b.beam(V(4.2, -3.4, z), V(6.8, -3.4, z), 0.16, M("Hull"))
        b.beam(V(4.2, -1.6, z), V(6.8, -1.6, z), 0.16, M("Hull"))
    b.box(2.2, 1.2, 1.1, M("Container"), loc=(5.5, -2.5, -1.4), rot=(0, 0, 6))
    b.box(2.0, 1.1, 1.0, M("Hazard"), loc=(5.4, -2.5, 0.9), rot=(0, 0, -8))
    b.box(1.6, 1.0, 1.0, M("Accent"), loc=(-3.6, -4.4, -3.0), rot=(20, 10, 40))
    b.box(1.4, 0.9, 0.9, M("Container"), loc=(2.0, -5.2, -2.6), rot=(-15, 25, 10))
    # Lichter in der Felswand
    for (x, y, z) in ((-2.0, -4.6, 1.6), (0.6, -4.6, -1.8), (3.6, -3.8, 2.6)):
        b.box(0.8, 0.5, 0.25, M("Window"), loc=(x, y, z), rot=(0, 0, 10))
    b.nav_lights([(7.0, -3.4, 2.5), (-10.0, 0.4, 2.0)], mat="LightRed", s=0.22)

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
