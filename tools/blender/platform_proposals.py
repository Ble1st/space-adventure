"""Rendering-Vorschlaege fuer Kleinstationen: leichte, mittlere, schwere Waffenstellung und Satellit
(je drei Entwuerfe A/B/C). Nur Vorschlaege, nicht im Spiel.

Aufruf (Repo-Wurzel):
    blender --background --factory-startup --python tools/blender/platform_proposals.py
    blender --background --factory-startup --python tools/blender/platform_proposals.py -- --only light_a,sat_b
    blender --background --factory-startup --python tools/blender/platform_proposals.py -- --sheets-only

Ausgaben (docs/platforms/): <name>.png (1280x800), <name>.glb (Radius 1 in der Spielebene wie die
Stationen), hardpoints.json, vorschlaege_leicht|mittel|schwer|satellit.jpg und vorschlaege_alle.jpg
(gemeinsamer Massstab). Temporaeres nach /home/gerd/space-adventure-blender-tmp/platforms.

Gebaut wird in Spieleinheiten (Station: Radius 15, S-Schiff: Laenge 4,8). Blender: Z oben,
Spielebene XY. Tuerme sind eigene Objekte "Turret_<n>" (Geschuetz-Index wie in defense.guns) bzw.
"PD_<n>" (Punktverteidigung), Ursprung auf der Drehachse, Drehachse lokal +Z (Godot +Y), Rohr in Ruhe
lokal +Y (Godot -Z). Raketenwerfer sind buendige Schachtfelder in der Huelle (kein Node).
Nutzt nur die Bausteine aus stationkit.py (lesend importiert, nichts daran geaendert).
"""
import importlib
import json
import math
import os
import subprocess
import sys
import time

import bpy
from bpy_extras.object_utils import world_to_camera_view
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(REPO, "docs", "platforms")
TMP = "/home/gerd/space-adventure-blender-tmp/platforms"
os.makedirs(OUT, exist_ok=True)
os.makedirs(TMP, exist_ok=True)
sys.path.insert(0, HERE)


def load_kit():
    """stationkit wird parallel gepflegt: bei einem Fehler (Datei gerade im Umbau) kurz warten."""
    last = None
    for _ in range(12):
        try:
            if "stationkit" in sys.modules:
                return importlib.reload(sys.modules["stationkit"])
            return importlib.import_module("stationkit")
        except Exception as e:  # noqa: BLE001
            last = e
            time.sleep(5)
    raise last


K = load_kit()
Builder = K.Builder


def M(name):
    return K.material(name)


ACCENT_PREVIEW = (0.80, 0.18, 0.08, 1.0)   # Beispiel-Fraktionsfarbe nur fuer die Bilder


def octo(r, n=8, start=22.5):
    return [(r * math.cos(math.radians(start + 360 * i / n)), r * math.sin(math.radians(start + 360 * i / n)))
            for i in range(n)]


# ================================================================== Tuerme (eigene Objekte)
# Jede Funktion baut im lokalen Raum: Drehachse +Z, Rohr nach +Y; liefert die Muendung (lokal).

def tur_laser_s(t, s=1.0):
    t.cyl(0.55 * s, 0.28 * s, M("HullDark"), seg=8, loc=(0, 0, 0.14 * s))
    t.prism(octo(0.52 * s), 0.42 * s, M("ShipArmor"), loc=(0, 0, 0.49 * s), top_scale=0.74)
    t.box(0.42 * s, 0.3 * s, 0.06 * s, M("Accent"), loc=(0, -0.14 * s, 0.72 * s))
    for o in (-0.15, 0.15):
        t.strut((o * s, 0.2 * s, 0.5 * s), (o * s, 1.45 * s, 0.5 * s), 0.065 * s, M("ShipPanel"), seg=5)
        t.box(0.13 * s, 0.14 * s, 0.13 * s, M("Light"), loc=(o * s, 1.47 * s, 0.5 * s))
    return Vector((0, 1.55 * s, 0.5 * s))


def tur_mass_s(t, s=1.0):
    t.cyl(0.55 * s, 0.28 * s, M("HullDark"), seg=8, loc=(0, 0, 0.14 * s))
    t.box(0.95 * s, 0.95 * s, 0.5 * s, M("ShipArmor"), loc=(0, 0, 0.53 * s), bevel=0.08 * s)
    t.box(0.6 * s, 0.35 * s, 0.4 * s, M("HullDark"), loc=(0, -0.6 * s, 0.5 * s))
    t.box(0.18 * s, 0.6 * s, 0.06 * s, M("Accent"), loc=(0.28 * s, 0, 0.8 * s))
    t.strut((0, 0.3 * s, 0.55 * s), (0, 1.5 * s, 0.55 * s), 0.13 * s, M("ShipPanel"), seg=6)
    t.cyl(0.2 * s, 0.28 * s, M("HullDark"), seg=6, loc=(0, 1.5 * s, 0.55 * s), rot=(90, 0, 0))
    return Vector((0, 1.65 * s, 0.55 * s))


def tur_laser_m(t, s=1.0):
    t.cyl(0.85 * s, 0.32 * s, M("HullDark"), seg=10, loc=(0, 0, 0.16 * s))
    t.prism(octo(0.8 * s), 0.55 * s, M("ShipArmor"), loc=(0, 0, 0.6 * s), top_scale=0.72)
    t.box(0.55 * s, 0.5 * s, 0.07 * s, M("Accent"), loc=(0, -0.2 * s, 0.9 * s))
    for x in (-0.82, 0.82):                       # Kuehlrippen
        t.box(0.12 * s, 0.9 * s, 0.4 * s, M("HullDark"), loc=(x * s, -0.1 * s, 0.55 * s))
    for o in (-0.22, 0.22):
        t.strut((o * s, 0.4 * s, 0.62 * s), (o * s, 2.1 * s, 0.62 * s), 0.08 * s, M("ShipPanel"), seg=6)
        t.box(0.17 * s, 0.18 * s, 0.17 * s, M("Light"), loc=(o * s, 2.12 * s, 0.62 * s))
    return Vector((0, 2.22 * s, 0.62 * s))


def tur_heavy(t, s=1.0):
    t.cyl(1.35 * s, 0.4 * s, M("HullDark"), seg=12, loc=(0, 0, 0.2 * s))
    wedge = [(-1.15, -1.1), (1.15, -1.1), (1.25, 0.35), (0.75, 1.15), (-0.75, 1.15), (-1.25, 0.35)]
    t.prism([(x * s, y * s) for x, y in wedge], 0.95 * s, M("ShipArmor"), loc=(0, 0, 0.87 * s), top_scale=0.8)
    t.box(0.5 * s, 1.5 * s, 0.08 * s, M("Accent"), loc=(-0.45 * s, -0.25 * s, 1.36 * s))
    t.box(0.5 * s, 1.5 * s, 0.08 * s, M("Accent"), loc=(0.45 * s, -0.25 * s, 1.36 * s))
    t.box(1.3 * s, 0.4 * s, 0.7 * s, M("HullDark"), loc=(0, 1.15 * s, 0.85 * s))
    for o in (-0.36, 0.36):
        t.strut((o * s, 1.2 * s, 0.85 * s), (o * s, 3.5 * s, 0.85 * s), 0.17 * s, M("ShipPanel"), seg=6)
        t.cyl(0.25 * s, 0.4 * s, M("HullDark"), seg=6, loc=(o * s, 3.35 * s, 0.85 * s), rot=(90, 0, 0))
    t.box(0.2 * s, 0.2 * s, 0.15 * s, M("LightRed"), loc=(0, -1.0 * s, 1.35 * s))
    return Vector((0, 3.6 * s, 0.85 * s))


def tur_pd(t, s=1.0):
    t.cyl(0.32 * s, 0.2 * s, M("HullDark"), seg=6, loc=(0, 0, 0.1 * s))
    t.sphere(0.34 * s, M("ShipArmor"), seg=8, rings=4, loc=(0, 0, 0.2 * s), half=True)
    t.strut((0, 0.1 * s, 0.38 * s), (0, 0.75 * s, 0.78 * s), 0.045 * s, M("ShipPanel"), seg=4)
    t.box(0.1 * s, 0.1 * s, 0.08 * s, M("LightRed"), loc=(0, -0.22 * s, 0.45 * s))
    return Vector((0, 0.78 * s, 0.8 * s))


TURRETS = {"laser_s": (tur_laser_s, "weapon_laser_s"), "mass_s": (tur_mass_s, "weapon_massdriver_s"),
           "laser_m": (tur_laser_m, "weapon_laser_m"), "heavy": (tur_heavy, "weapon_laser_m"),
           "pd": (tur_pd, "point_defense_s")}


class Platform:
    """Ein Entwurf: Hauptkoerper (Builder) + Turm-Objekte + Hardpoint-Liste."""

    def __init__(self, name):
        self.name = name
        self.b = Builder(name)
        self.turrets = []      # (kind, name, matrix, scale, gun)
        self.missiles = []     # (pos, normal)
        self.guns = 0
        self.pds = 0

    def mount(self, kind, loc, facing=90.0, down=False, s=1.0):
        """Turm an loc; facing = Grund-Schussrichtung in der Ebene (Grad, 0 = +X).
        down=True: Bauchturm (Achse -Z)."""
        if down:
            R = Matrix.Rotation(math.radians(facing + 90), 4, "Z") @ Matrix.Rotation(math.pi, 4, "X")
        else:
            R = Matrix.Rotation(math.radians(facing - 90), 4, "Z")
        if kind == "pd":
            nm = f"PD_{self.pds}"
            self.pds += 1
            gun = None
        else:
            nm = f"Turret_{self.guns}"
            gun = self.guns
            self.guns += 1
        self.turrets.append((kind, nm, Matrix.Translation(Vector(loc)) @ R, s, gun))
        return self

    def missile_field(self, center, rows=2, cols=3, cell=0.55, yaw=0.0, down=False, tilt=0.0):
        """Buendiges Schachtfeld eines Raketenwerfers (zaehlt als Geschuetz)."""
        b = self.b
        c = Vector(center)
        sg = -1 if down else 1
        rot = (180 + tilt if down else tilt, 0, yaw)
        w, h = cols * cell + 0.25, rows * cell + 0.25
        b.box(w, h, 0.16, M("HullDark"), loc=c, rot=rot)
        R = Matrix.Rotation(math.radians(yaw), 3, "Z") @ Matrix.Rotation(math.radians(rot[0]), 3, "X")
        for i in range(cols):
            for j in range(rows):
                o = R @ Vector(((i - (cols - 1) / 2) * cell, (j - (rows - 1) / 2) * cell, 0.06))
                b.box(cell * 0.74, cell * 0.74, 0.08, M("ShipPanel"), loc=c + o, rot=rot)
                o2 = R @ Vector(((i - (cols - 1) / 2) * cell, (j - (rows - 1) / 2) * cell - cell * 0.3, 0.1))
                b.box(cell * 0.5, cell * 0.08, 0.06, M("LightRed"), loc=c + o2, rot=rot)
        n = R @ Vector((0, 0, 1))
        self.missiles.append((c + n * 0.1, n, self.guns))
        self.guns += 1
        return self

    def build(self):
        root = self.b.finish(normalize="none")
        bpy.context.scene.collection.objects.link(root)
        self.hard = []
        for kind, nm, Mw, s, gun in self.turrets:
            fn, module = TURRETS[kind]
            t = Builder(nm)
            muzzle = fn(t, s)
            ob = t.finish(normalize="none")
            bpy.context.scene.collection.objects.link(ob)
            ob.matrix_world = Mw
            ob.parent = root
            ob.matrix_parent_inverse = root.matrix_world.inverted()
            mw = Mw @ muzzle
            dw = (Mw.to_3x3() @ Vector((0, 1, 0))).normalized()
            self.hard.append(dict(kind="pd" if kind == "pd" else "turret", gun=gun, module=module, node=nm,
                                  pos=mw, dir=dw))
        for p, n, gun in self.missiles:
            self.hard.append(dict(kind="missile", gun=gun, module="missile_launcher_s", node=None, pos=p, dir=n))
        self.hard.sort(key=lambda h: (h["gun"] is None, h["gun"] if h["gun"] is not None else 0, h["node"] or ""))
        self.root = root
        return root

    def objects(self):
        return [self.root] + list(self.root.children)


# ================================================================== Gemeinsame Details
def beacon(b, p, red=False, s=0.14):
    b.sphere(s, M("LightRed" if red else "Light"), seg=6, rings=4, loc=p)


def trench_ring(b, R, z, w=0.14, seg=16):
    b.ring(R, w, 0.08, M("HullDark"), seg=seg, loc=(0, 0, z))


def accent_plates(b, R, z, n, w, d, start=0.0, mat="Accent", h=0.06):
    for i in range(n):
        a = start + 360 * i / n
        r = math.radians(a)
        b.box(d, w, h, M(mat), loc=(R * math.cos(r), R * math.sin(r), z), rot=(0, 0, a))


# ================================================================== LEICHT  (Radius ~3,2)
def light_a():
    """Mastturm: senkrechter Gittermast durch die Ebene, oben und unten je ein Laserturm,
    Nabe mit zwei Kuehlfluegeln."""
    p = Platform("light_a")
    b = p.b
    b.truss((0, 0, -2.3), (0, 0, 2.3), 0.42, 0.1, M("ShipPanel"), n=6)
    b.prism(octo(0.95), 1.1, M("ShipHull"), loc=(0, 0, 0))
    b.window_band(0.97, 0.0, 0.32, mat="Accent", seg=16, n=4, gap=0.2)
    trench_ring(b, 0.99, 0.43, seg=8)
    trench_ring(b, 0.99, -0.43, seg=8)
    for sx in (1, -1):
        b.radiator((sx * 0.9, 0, 0), (sx, 0, 0), 2.6, 1.05, tilt=0, mat="ShipPanel", stripe="Accent", n=2)
        beacon(b, (sx * 3.15, 0, 0), red=sx < 0)
    for z, sg in ((2.4, 1), (-2.4, -1)):
        b.cyl(0.8, 0.3, M("ShipArmor"), seg=10, loc=(0, 0, z))
        trench_ring(b, 0.82, z, w=0.08, seg=10)
    p.mount("laser_s", (0, 0, 2.55), facing=90)
    p.mount("laser_s", (0, 0, -2.55), facing=-90, down=True)
    b.mast((0.5, -0.55, 0.55), 0.9)
    return p


def light_b():
    """Panzerscheibe: flache sechseckige Scheibe mit abgeschraegten Panzerkanten, Laserturm oben,
    Massetreiber unten, drei Sensorknoten am Rand."""
    p = Platform("light_b")
    b = p.b
    hexa = octo(2.6, n=6, start=0)
    b.prism(hexa, 0.42, M("ShipHull"), loc=(0, 0, 0.21), top_scale=0.8)
    b.prism(hexa, 0.42, M("ShipHull"), loc=(0, 0, -0.21), rot=(180, 0, 0), top_scale=0.8)
    b.ring(2.62, 0.12, 0.14, M("HullDark"), seg=6, loc=(0, 0, 0))
    for i in range(3):                               # grosse Accent-Felder auf jeder zweiten Seite
        a = 30 + 120 * i
        r = math.radians(a)
        b.box(0.7, 1.55, 0.06, M("Accent"), loc=(1.45 * math.cos(r), 1.45 * math.sin(r), 0.43), rot=(0, 0, a))
        b.box(0.7, 1.55, 0.06, M("Accent"), loc=(1.45 * math.cos(r), 1.45 * math.sin(r), -0.43), rot=(0, 0, a))
        r2 = math.radians(a + 60)
        b.box(0.12, 1.2, 0.05, M("HullDark"), loc=(1.4 * math.cos(r2), 1.4 * math.sin(r2), 0.43), rot=(0, 0, a + 60))
        sp = (2.7 * math.cos(r2), 2.7 * math.sin(r2), 0)
        b.beam((2.2 * math.cos(r2), 2.2 * math.sin(r2), 0), sp, 0.18, M("HullDark"))
        b.box(0.5, 0.5, 0.5, M("ShipArmor"), loc=sp, rot=(0, 0, a + 60), bevel=0.05)
        beacon(b, (3.0 * math.cos(r2), 3.0 * math.sin(r2), 0), red=(i == 1))
    b.cyl(0.95, 0.3, M("ShipArmor"), seg=10, loc=(0, 0, 0.55))
    b.cyl(0.95, 0.3, M("ShipArmor"), seg=10, loc=(0, 0, -0.55))
    p.mount("laser_s", (0, 0, 0.7), facing=90)
    p.mount("mass_s", (0, 0, -0.7), facing=-90, down=True)
    return p


def light_c():
    """Felsanker: kleiner Asteroidenbrocken mit Panzerkragen, je eine Kasematte oben und unten
    mit Turm, Ankerbolzen im Fels."""
    p = Platform("light_c")
    b = p.b
    b.rock(1.75, M("Rock"), seed=3, seg=10, rings=7, scale=(1.25, 1.05, 0.85))
    b.ring(2.45, 0.32, 0.55, M("ShipArmor"), seg=12, loc=(0, 0, 0))
    b.window_band(2.47, 0.0, 0.22, mat="Accent", seg=12, n=3, gap=0.35)
    for i in range(3):
        a = math.radians(60 + 120 * i)
        beacon(b, (2.62 * math.cos(a), 2.62 * math.sin(a), 0), red=(i == 2))
    for z, sg in ((1.45, 1), (-1.45, -1)):
        b.box(1.7, 1.5, 0.75, M("ShipHull"), loc=(0, 0, z), bevel=0.1)
        b.box(1.1, 0.5, 0.06, M("Accent"), loc=(0, -0.38, z + sg * 0.39))
        b.box(1.5, 0.1, 0.06, M("HullDark"), loc=(0, 0.2, z + sg * 0.39))
    p.mount("laser_s", (0, 0.25, 1.85), facing=90)
    p.mount("mass_s", (0, 0.25, -1.85), facing=-90, down=True)
    for a in (0, 120, 240):
        r = math.radians(a)
        b.beam((2.15 * math.cos(r), 2.15 * math.sin(r), 0.3), (1.3 * math.cos(r), 1.3 * math.sin(r), 1.1), 0.14,
               M("HullDark"))
    return p


# ================================================================== MITTEL  (Radius ~4,8)
def medium_a():
    """Dreiarm-Gitter: Nabe mit drei Gittertraegern, an jedem Arm-Ende eine Kanzel mit Turm
    (mittlerer Laser, leichter Laser, Massetreiber), Punktverteidigung oben/unten an der Nabe."""
    p = Platform("medium_a")
    b = p.b
    b.spindle(1.15, 1.5, M("ShipHull"), seg=12)
    b.window_band(1.17, 0.0, 0.38, mat="Accent", seg=12, n=3, gap=0.15)
    trench_ring(b, 1.18, 0.5, seg=12)
    trench_ring(b, 1.18, -0.5, seg=12)
    kinds = ["laser_m", "laser_s", "mass_s"]
    for i, a in enumerate((90, 210, 330)):
        r = math.radians(a)
        d = Vector((math.cos(r), math.sin(r), 0))
        b.truss(d * 1.0, d * 3.55, 0.38, 0.11, M("ShipPanel"), n=4)
        c = d * 4.0
        b.prism(octo(0.85), 1.0, M("ShipArmor"), loc=c, top_scale=1.0)
        b.box(1.0, 0.55, 0.06, M("Accent"), loc=c + Vector((0, 0, -0.52)), rot=(0, 0, a))
        b.ring(0.88, 0.1, 0.18, M("HullDark"), seg=8, loc=c)
        beacon(b, c + d * 0.95, red=(i == 1))
        p.mount(kinds[i], c + Vector((0, 0, 0.5)), facing=a)
        b.box(0.9, 0.55, 0.06, M("Accent"), loc=(d * 2.25) + Vector((0, 0, 0.5)), rot=(0, 0, a + 90))
    p.mount("pd", (0, 0, 1.25), facing=30)
    p.mount("pd", (0, 0, -1.25), facing=-150, down=True)
    return p


def medium_b():
    """Bastionstrommel: geschlossene achteckige Panzertrommel, drei vorspringende Geschuetzbastionen
    am Rand, Punktverteidigungskuppeln mittig oben und unten."""
    p = Platform("medium_b")
    b = p.b
    oc = octo(3.3)
    b.prism(oc, 0.6, M("ShipHull"), loc=(0, 0, 0.3), top_scale=0.82)
    b.prism(oc, 0.6, M("ShipHull"), loc=(0, 0, -0.3), rot=(180, 0, 0), top_scale=0.82)
    b.ring(3.32, 0.12, 0.16, M("HullDark"), seg=8, loc=(0, 0, 0), start=22.5)
    trench_ring(b, 1.9, 0.62, w=0.16, seg=16)
    trench_ring(b, 1.9, -0.62, w=0.16, seg=16)
    b.cyl(1.05, 0.4, M("ShipArmor"), seg=12, loc=(0, 0, 0.75))
    b.cyl(1.05, 0.4, M("ShipArmor"), seg=12, loc=(0, 0, -0.75))
    kinds = ["laser_m", "laser_s", "mass_s"]
    for i, a in enumerate((90, 210, 330)):
        r = math.radians(a)
        d = Vector((math.cos(r), math.sin(r), 0))
        c = d * 3.65
        b.box(1.5, 1.35, 1.0, M("ShipArmor"), loc=c, rot=(0, 0, a), bevel=0.12)
        b.box(0.25, 1.38, 0.18, M("Accent"), loc=c + d * 0.65, rot=(0, 0, a))
        p.mount(kinds[i], c + Vector((0, 0, 0.5)), facing=a)
        beacon(b, c + d * 0.8 + Vector((0, 0, -0.45)), red=(i == 2))
        # grosse Accent-Felder zwischen den Bastionen
        r2 = math.radians(a + 60)
        b.box(1.0, 1.7, 0.06, M("Accent"), loc=(2.5 * math.cos(r2), 2.5 * math.sin(r2), 0.62), rot=(0, 0, a + 60))
        b.box(1.0, 1.7, 0.06, M("Accent"), loc=(2.5 * math.cos(r2), 2.5 * math.sin(r2), -0.62), rot=(0, 0, a + 60))
    p.mount("pd", (0, 0, 0.95), facing=0)
    p.mount("pd", (0, 0, -0.95), facing=180, down=True)
    b.mast((0.7, 0.4, 0.95), 1.0)
    return p


def medium_c():
    """Felsfestung: mittelgrosser Asteroid mit eingelassener Kasematte oben (mittlerer Laser), zwei
    Flankenbastionen am Gesteinsguertel, Punktverteidigung unten."""
    p = Platform("medium_c")
    b = p.b
    b.rock(2.6, M("Rock"), seed=7, seg=12, rings=8, scale=(1.3, 1.1, 0.8))
    b.prism(octo(1.35, n=6, start=0), 1.1, M("ShipHull"), loc=(0, 0, 1.75), top_scale=0.85)
    b.box(1.6, 0.6, 0.06, M("Accent"), loc=(0, -0.35, 2.33))
    trench_ring(b, 1.0, 2.31, w=0.1, seg=6)
    p.mount("laser_m", (0, 0.15, 2.32), facing=90)
    b.prism(octo(1.1, n=6, start=0), 0.9, M("ShipHull"), loc=(0, 0, -1.65), rot=(180, 0, 0), top_scale=0.85)
    p.mount("pd", (0, 0, -2.1), facing=-90, down=True)
    for i, a in enumerate((200, 340)):
        r = math.radians(a)
        d = Vector((math.cos(r), math.sin(r), 0))
        c = d * 3.45
        b.box(1.5, 1.3, 0.9, M("ShipArmor"), loc=c, rot=(0, 0, a), bevel=0.1)
        b.box(1.0, 1.32, 0.07, M("Accent"), loc=c + Vector((0, 0, 0.47)) - d * 0.2, rot=(0, 0, a))
        b.beam(c - d * 0.6, c - d * 1.8 + Vector((0, 0, 0.4)), 0.25, M("HullDark"))
        p.mount("laser_s" if i == 0 else "mass_s", c + d * 0.2 + Vector((0, 0, 0.45)), facing=a)
        beacon(b, c + d * 0.8, red=(i == 0))
    b.ring(3.0, 0.2, 0.35, M("ShipArmor"), seg=12, loc=(0, 0, 0.2), arc=150, start=15)
    p.mount("pd", (0, 3.0, 0.6), facing=90)
    return p


# ================================================================== SCHWER  (Radius ~7)
def heavy_a():
    """Gitterbatterie: gepanzerter Mittelblock mit buendigen Raketenschaechten, zwei lange Gittertraeger
    mit schweren Zwillingstuermen an den Enden, Kuehlfluegel quer, Punktverteidigung an den Traegern."""
    p = Platform("heavy_a")
    b = p.b
    blk = [(-1.8, -1.6), (1.8, -1.6), (2.1, 0), (1.8, 1.6), (-1.8, 1.6), (-2.1, 0)]
    b.prism(blk, 1.1, M("ShipArmor"), loc=(0, 0, 0.55), top_scale=0.84)
    b.prism(blk, 1.1, M("ShipArmor"), loc=(0, 0, -0.55), rot=(180, 0, 0), top_scale=0.84)
    b.ring(2.15, 0.1, 0.18, M("HullDark"), seg=6, loc=(0, 0, 0))
    for sx in (1, -1):
        b.truss((sx * 2.0, 0, 0), (sx * 5.0, 0, 0), 0.6, 0.15, M("ShipPanel"), n=5)
        c = Vector((sx * 5.6, 0, 0))
        b.prism(octo(1.35), 1.5, M("ShipHull"), loc=c)
        b.window_band(1.38, 0.0, 0.4, mat="Accent", seg=16, n=4, gap=0.15, loc=c)
        b.ring(1.4, 0.1, 0.08, M("HullDark"), seg=8, loc=c + Vector((0, 0, 0.55)), start=22.5)
        b.box(0.8, 1.6, 0.06, M("Accent"), loc=c + Vector((sx * -0.5, 0, -0.78)))
        beacon(b, c + Vector((sx * 1.45, 0, 0)), red=sx < 0)
        p.mount("heavy", c + Vector((0, 0, 0.75)), facing=0 if sx > 0 else 180)
        p.mount("pd", Vector((sx * 3.5, 0, -0.62)), facing=-90, down=True)
    p.mount("mass_s", (0, 0, -1.12), facing=-90, down=True, s=1.2)
    p.missile_field((-0.85, 0, 1.12), rows=2, cols=2, cell=0.6, yaw=0)
    p.missile_field((0.85, 0, 1.12), rows=2, cols=2, cell=0.6, yaw=0)
    for sy in (1, -1):
        b.radiator((0, sy * 1.6, 0), (0, sy, 0), 3.4, 1.4, tilt=0, mat="ShipPanel", stripe="Accent", n=2)
    b.box(3.0, 0.2, 0.06, M("Accent"), loc=(0, -1.2, 1.12))
    b.box(3.0, 0.2, 0.06, M("Accent"), loc=(0, 1.2, 1.12))
    return p


def heavy_b():
    """Zitadelle: zweistufige achteckige Panzerscheibe (oben und unten), zwei schwere Zwillingstuerme
    auf der unteren Stufe, Raketenschaechte auf der oberen, Massetreiber unten, vier Punktverteidigungen."""
    p = Platform("heavy_b")
    b = p.b
    big, mid = octo(5.9), octo(3.4)
    b.prism(big, 0.8, M("ShipHull"), loc=(0, 0, 0.4), top_scale=0.9)
    b.prism(big, 0.8, M("ShipHull"), loc=(0, 0, -0.4), rot=(180, 0, 0), top_scale=0.9)
    b.ring(5.92, 0.14, 0.2, M("HullDark"), seg=8, loc=(0, 0, 0), start=22.5)
    b.prism(mid, 0.8, M("ShipArmor"), loc=(0, 0, 1.2), top_scale=0.8)
    b.prism(mid, 0.8, M("ShipArmor"), loc=(0, 0, -1.2), rot=(180, 0, 0), top_scale=0.8)
    trench_ring(b, 3.45, 0.82, w=0.18, seg=8)
    trench_ring(b, 3.45, -0.82, w=0.18, seg=8)
    b.spindle(1.0, 1.6, M("ShipHull"), seg=10, loc=(0, 0, 1.9), cap=0.5)
    b.window_band(1.02, 1.9, 0.3, mat="Window", seg=10, n=5, gap=0.4)
    p.mount("heavy", (4.3, 0, 0.82), facing=0)
    p.mount("heavy", (-4.3, 0, 0.82), facing=180)
    p.mount("mass_s", (0, 0, -1.62), facing=-90, down=True, s=1.25)
    p.missile_field((0, 1.75, 1.62), rows=2, cols=3, cell=0.55, yaw=0)
    p.missile_field((0, -1.75, 1.62), rows=2, cols=3, cell=0.55, yaw=0)
    for a in (90, 270):
        r = math.radians(a)
        b.box(1.6, 2.6, 0.06, M("Accent"), loc=(4.4 * math.cos(r), 4.4 * math.sin(r), 0.82), rot=(0, 0, a + 90))
        b.box(1.6, 2.6, 0.06, M("Accent"), loc=(4.4 * math.cos(r), 4.4 * math.sin(r), -0.82), rot=(0, 0, a + 90))
    for i, a in enumerate((45, 135, 225, 315)):
        r = math.radians(a)
        c = (4.3 * math.cos(r), 4.3 * math.sin(r))
        p.mount("pd", (c[0], c[1], 0.82 if i % 2 == 0 else -0.82), facing=a, down=(i % 2 == 1))
        beacon(b, (5.95 * math.cos(r), 5.95 * math.sin(r), 0), red=a > 180)
    b.mast((0, 0, 2.6), 1.2)
    return p


def heavy_c():
    """Asteroidenbastion: grosser Asteroid, Ringbastion mit schwerem Turm oben, Kasematte mit
    Raketenschaechten an der Flanke, zweite schwere Batterie auf einem Ausleger, Punktverteidigung."""
    p = Platform("heavy_c")
    b = p.b
    b.rock(4.0, M("Rock"), seed=11, seg=14, rings=9, scale=(1.3, 1.05, 0.72))
    b.cyl(1.9, 1.5, M("ShipArmor"), seg=12, loc=(0, 0.3, 2.5))
    b.ring(1.93, 0.12, 0.3, M("Accent"), seg=12, loc=(0, 0.3, 2.85))
    trench_ring(b, 1.6, 3.26, w=0.12, seg=12)
    p.mount("heavy", (0, 0.3, 3.25), facing=90)
    # Kasematte an +X mit Raketenschaechten
    cas = [(-1.6, -1.8), (1.4, -1.8), (2.0, -0.9), (2.0, 0.9), (1.4, 1.8), (-1.6, 1.8)]
    b.prism(cas, 2.2, M("ShipHull"), loc=(0.6, -4.0, 0.2), rot=(0, 0, -90), top_scale=0.86)
    b.box(3.0, 0.5, 0.07, M("Accent"), loc=(0.6, -5.2, 0.4), rot=(-72, 0, 0))
    p.missile_field((-0.15, -3.75, 1.31), rows=2, cols=2, cell=0.6)
    p.missile_field((1.35, -3.75, 1.31), rows=2, cols=2, cell=0.6)
    beacon(b, (0.6, -6.1, 0.2))
    # Ausleger an -X mit zweiter schwerer Batterie
    b.truss((-3.8, 0.2, 0.3), (-6.0, 0.2, 0.3), 0.5, 0.13, M("ShipPanel"), n=3)
    b.prism(octo(1.3), 1.2, M("ShipArmor"), loc=(-6.2, 0.2, 0.3))
    b.box(0.9, 1.4, 0.06, M("Accent"), loc=(-6.2, 0.2, -0.31))
    p.mount("heavy", (-6.2, 0.2, 0.9), facing=180, s=0.85)
    beacon(b, (-7.55, 0.2, 0.3), red=True)
    b.cyl(1.2, 0.9, M("ShipArmor"), seg=10, loc=(0, -0.4, -2.3))
    p.mount("mass_s", (0, -0.4, -2.75), facing=-90, down=True, s=1.2)
    p.mount("pd", (1.8, 2.9, 1.3), facing=60)
    p.mount("pd", (2.6, -1.4, 1.9), facing=-30)
    p.mount("pd", (2.2, 2.0, -1.7), facing=60, down=True)
    return p


# ================================================================== SATELLITEN  (Radius ~2,4)
def sat_a():
    """Relais-Schuessel: kompakter Bus mit gekippter Parabolschuessel (Kommunikations-/Navigationsrelais),
    zwei Solarfluegel, Peilmast unten."""
    p = Platform("sat_a")
    b = p.b
    b.box(0.95, 0.95, 1.1, M("ShipHull"), bevel=0.08)
    b.box(0.98, 0.5, 0.35, M("Accent"), loc=(0, 0, 0.05))
    b.box(0.5, 0.98, 0.35, M("Accent"), loc=(0, 0, 0.05))
    b.dish((0, 0.15, 0.75), 1.05, M("ShipPanel"), rot=(-30, 0, 0), depth=0.32, seg=12, feed_mat="HullDark")
    for sx in (1, -1):
        b.beam((sx * 0.48, 0, 0), (sx * 0.85, 0, 0), 0.12, M("HullDark"))
        b.box(1.55, 0.9, 0.05, M("Panel"), loc=(sx * 1.65, 0, 0))
        b.box(1.6, 0.06, 0.08, M("ShipPanel"), loc=(sx * 1.65, 0.46, 0))
        b.box(1.6, 0.06, 0.08, M("ShipPanel"), loc=(sx * 1.65, -0.46, 0))
        b.box(0.05, 0.92, 0.07, M("ShipPanel"), loc=(sx * 1.65, 0, 0))
        beacon(b, (sx * 2.5, 0, 0), red=sx < 0, s=0.1)
    b.strut((0, 0, -0.55), (0, 0, -1.4), 0.05, M("HullDark"), seg=4)
    b.sphere(0.13, M("LightRed"), seg=6, rings=4, loc=(0, 0, -1.45))
    return p


def sat_b():
    """Sensorkranz: Ring mit sechs Sensorkoepfen um einen schlanken Kern (Ueberwachung/Aufklaerung),
    drei Speichen, leuchtende Linsen nach aussen."""
    p = Platform("sat_b")
    b = p.b
    b.torus(2.0, 0.17, M("ShipHull"), seg=18, minor=6)
    b.spindle(0.45, 1.3, M("ShipHull"), seg=8)
    b.window_band(0.47, 0.0, 0.3, mat="Accent", seg=8, n=2, gap=0.1)
    for i in range(3):
        a = math.radians(30 + 120 * i)
        d = Vector((math.cos(a), math.sin(a), 0))
        b.beam(d * 0.45, d * 1.85, 0.1, M("ShipPanel"))
    for i in range(6):
        a = 360 * i / 6
        r = math.radians(a)
        d = Vector((math.cos(r), math.sin(r), 0))
        c = d * 2.0
        b.box(0.5, 0.42, 0.42, M("ShipArmor"), loc=c, rot=(0, 0, a), bevel=0.04)
        b.cyl(0.13, 0.12, M("Light"), seg=6, loc=c + d * 0.27, rot=(0, 90, a))
        b.box(0.32, 0.44, 0.06, M("Accent"), loc=c + Vector((0, 0, 0.22)), rot=(0, 0, a))
    b.sphere(0.12, M("Light"), seg=6, rings=4, loc=(0, 0, 1.15))
    b.sphere(0.12, M("LightRed"), seg=6, rings=4, loc=(0, 0, -1.15))
    return p


def sat_c():
    """Vermessungssonde (Paneel-Typ): langer Ausleger mit vier Paneelfeldern, Messkopf mit Linse nach
    unten und oben, kleine Leitflossen in Fraktionsfarbe."""
    p = Platform("sat_c")
    b = p.b
    b.capsule((0, 0, 0), (0, 1, 0), 1.5, 0.38, M("ShipHull"), seg=8)
    b.ring(0.41, 0.06, 0.25, M("Accent"), seg=8, loc=(0, 0, 0), rot=(90, 0, 0))
    b.beam((-2.3, 0, 0), (2.3, 0, 0), 0.12, M("HullDark"))
    for sx in (1, -1):
        for k in (0.75, 1.75):
            c = (sx * k, 0, 0)
            b.box(0.85, 1.3, 0.05, M("Panel"), loc=c, rot=(18 * sx, 0, 0))
            b.box(0.88, 0.05, 0.07, M("ShipPanel"), loc=(sx * k, 0.64, 0.2 * sx), rot=(18 * sx, 0, 0))
        b.box(0.18, 0.5, 0.5, M("Accent"), loc=(sx * 2.35, 0, 0))
        beacon(b, (sx * 2.5, 0, 0), red=sx < 0, s=0.1)
    b.cyl(0.25, 0.5, M("HullDark"), seg=8, loc=(0, 0.5, -0.45), r2=0.18)
    b.cyl(0.24, 0.05, M("Window"), seg=8, loc=(0, 0.5, -0.72))
    b.cyl(0.18, 0.35, M("HullDark"), seg=8, loc=(0, -0.4, 0.45))
    b.cyl(0.17, 0.05, M("Window"), seg=8, loc=(0, -0.4, 0.64))
    b.box(0.06, 0.5, 0.5, M("Accent"), loc=(0, -0.9, 0.35))
    return p


# ================================================================== Katalog
CATS = {
    "light": ("Leichte Waffenstellung", 1200),
    "medium": ("Mittlere Waffenstellung", 2000),
    "heavy": ("Schwere Waffenstellung", 3000),
    "satellite": ("Satellit", 1000),
}
DESIGNS = [
    # name, kategorie, buchstabe, funktion, titel, zeile
    ("light_a", "light", "A", light_a, "Mastturm", "Gittermast durch die Ebene, Laserturm oben und unten, Kühlflügel"),
    ("light_b", "light", "B", light_b, "Panzerscheibe", "flache Sechseck-Panzerscheibe, Laser oben, Massetreiber unten"),
    ("light_c", "light", "C", light_c, "Felsanker", "Gesteinsbrocken mit Panzerkragen und zwei Kasematten"),
    ("medium_a", "medium", "A", medium_a, "Dreiarm-Gitter", "Nabe mit drei Gitterarmen, Turm an jedem Arm, PV mittig"),
    ("medium_b", "medium", "B", medium_b, "Bastionstrommel", "geschlossene Panzertrommel, drei Randbastionen, PV-Kuppeln"),
    ("medium_c", "medium", "C", medium_c, "Felsfestung", "Asteroid mit Kasematte, zwei Flankenbastionen, PV"),
    ("heavy_a", "heavy", "A", heavy_a, "Gitterbatterie", "Panzerblock mit Raketenschächten, schwere Türme an Trägerenden"),
    ("heavy_b", "heavy", "B", heavy_b, "Zitadelle", "zweistufige Panzerscheibe, zwei schwere Türme, Schachtfelder"),
    ("heavy_c", "heavy", "C", heavy_c, "Asteroidenbastion", "großer Asteroid, Ringbastion, Raketen-Kasematte, Ausleger"),
    ("sat_a", "satellite", "A", sat_a, "Relais-Schüssel", "Kommunikations-/Navigationsrelais: Schüssel, Solarflügel"),
    ("sat_b", "satellite", "B", sat_b, "Sensorkranz", "Überwachung: Ring aus sechs Sensorköpfen um schlanken Kern"),
    ("sat_c", "satellite", "C", sat_c, "Vermessungssonde", "Paneel-Typ: Ausleger mit Paneelfeldern, Messkopf nach unten"),
]
FILE = {"light": "light", "medium": "medium", "heavy": "heavy", "satellite": "satellite"}


def out_name(key):
    cat_letter = key.split("_")
    return ("satellite_" + cat_letter[1]) if cat_letter[0] == "sat" else key


# ================================================================== Szene
def reset():
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.lights, bpy.data.cameras):
        for x in list(coll):
            coll.remove(x)


def setup_world(scene):
    w = bpy.data.worlds.get("Space") or bpy.data.worlds.new("Space")
    scene.world = w
    w.use_nodes = True
    nt = w.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputWorld")
    lp = nt.nodes.new("ShaderNodeLightPath")
    mix = nt.nodes.new("ShaderNodeMixShader")
    amb = nt.nodes.new("ShaderNodeBackground")
    amb.inputs["Color"].default_value = (0.13, 0.14, 0.19, 1)
    tc = nt.nodes.new("ShaderNodeTexCoord")
    vor = nt.nodes.new("ShaderNodeTexVoronoi")
    vor.inputs["Scale"].default_value = 220.0
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].color = (1, 1, 1, 1)
    ramp.color_ramp.elements[1].position = 0.03
    ramp.color_ramp.elements[1].color = (0, 0, 0, 1)
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    grad = nt.nodes.new("ShaderNodeMapRange")
    grad.inputs["From Min"].default_value = -1.0
    cr = nt.nodes.new("ShaderNodeValToRGB")
    cr.color_ramp.elements[0].color = (0.004, 0.005, 0.012, 1)
    cr.color_ramp.elements[1].color = (0.02, 0.026, 0.06, 1)
    add = nt.nodes.new("ShaderNodeMix")
    add.data_type = "RGBA"
    add.blend_type = "ADD"
    add.inputs["Factor"].default_value = 0.6
    bg = nt.nodes.new("ShaderNodeBackground")
    L = nt.links
    L.new(tc.outputs["Generated"], vor.inputs["Vector"])
    L.new(vor.outputs["Distance"], ramp.inputs["Fac"])
    L.new(tc.outputs["Generated"], sep.inputs["Vector"])
    L.new(sep.outputs["Z"], grad.inputs["Value"])
    L.new(grad.outputs["Result"], cr.inputs["Fac"])
    L.new(cr.outputs["Color"], add.inputs["A"])
    L.new(ramp.outputs["Color"], add.inputs["B"])
    L.new(add.outputs["Result"], bg.inputs["Color"])
    L.new(lp.outputs["Is Camera Ray"], mix.inputs["Fac"])
    L.new(amb.outputs["Background"], mix.inputs[1])
    L.new(bg.outputs["Background"], mix.inputs[2])
    L.new(mix.outputs["Shader"], out.inputs["Surface"])


def sun(name, energy, color, rot):
    ld = bpy.data.lights.new(name, "SUN")
    ld.energy = energy
    ld.color = color
    ld.angle = math.radians(2)
    ob = bpy.data.objects.new(name, ld)
    ob.rotation_euler = tuple(math.radians(a) for a in rot)
    bpy.context.scene.collection.objects.link(ob)


def setup_scene(res):
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_EEVEE"
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.resolution_percentage = 100
    sc.render.image_settings.file_format = "PNG"
    sc.render.image_settings.color_mode = "RGB"
    try:
        sc.eevee.taa_render_samples = 48
    except AttributeError:
        pass
    try:
        sc.view_settings.view_transform = "Khronos PBR Neutral"
    except TypeError:
        pass
    setup_world(sc)
    sun("Key", 3.6, (1.0, 0.95, 0.88), (50, 0, -35))
    sun("Rim", 2.4, (0.55, 0.70, 1.0), (-60, 0, 150))
    sun("Fill", 1.1, (0.8, 0.85, 1.0), (75, 0, 120))
    cd = bpy.data.cameras.new("Cam")
    cd.lens = 50
    cam = bpy.data.objects.new("Cam", cd)
    sc.collection.objects.link(cam)
    sc.camera = cam
    return sc, cam


AZ, EL = -32.0, 50.0     # schraeg von oben wie die Spielkamera (Elevation dort ~57 Grad)


def view_dir():
    d = Vector((math.cos(math.radians(EL)) * math.sin(math.radians(AZ)),
                -math.cos(math.radians(EL)) * math.cos(math.radians(AZ)),
                math.sin(math.radians(EL))))
    q = (-d).to_track_quat("-Z", "Y")
    return d, q, q @ Vector((1, 0, 0)), q @ Vector((0, 1, 0))


def frame(cam, obs, fill=0.86):
    sc = bpy.context.scene
    d, q, right, up = view_dir()
    bpy.context.view_layer.update()
    vs = [o.matrix_world @ v.co for o in obs for v in o.data.vertices]
    xs = [v.dot(right) for v in vs]
    ys = [v.dot(up) for v in vs]
    center = right * (min(xs) + max(xs)) / 2 + up * (min(ys) + max(ys)) / 2
    aspect = sc.render.resolution_x / sc.render.resolution_y
    th = (18.0 / cam.data.lens) * fill
    tv = th / aspect
    dist = 0.0
    for v in vs:
        r = v - center
        x, y, z = r.dot(right), r.dot(up), r.dot(d)
        dist = max(dist, z + abs(x) / th, z + abs(y) / tv)
    cam.location = center + d * dist
    cam.rotation_euler = q.to_euler()
    cam.data.clip_start = 0.05
    cam.data.clip_end = dist * 4


def frame_fixed(cam, target, dist):
    d, q, right, up = view_dir()
    cam.location = Vector(target) + d * dist
    cam.rotation_euler = q.to_euler()
    cam.data.clip_start = 0.05
    cam.data.clip_end = dist * 4


def scale_material():
    m = bpy.data.materials.get("Massstab")
    if m:
        return m
    m = bpy.data.materials.new("Massstab")
    m.use_nodes = True
    bs = m.node_tree.nodes["Principled BSDF"]
    bs.inputs["Base Color"].default_value = (0.16, 0.17, 0.19, 1)
    bs.inputs["Roughness"].default_value = 0.7
    return m


def scale_ship(loc):
    """Grauer Platzhalter eines S-Jaegers (Laenge 4,8 Spieleinheiten), Bug +Y."""
    b = Builder("Massstab_S")
    m = scale_material()
    dart = [(0, 2.4), (0.45, 0.6), (1.5, -1.5), (0.55, -1.1), (0.35, -2.4), (-0.35, -2.4), (-0.55, -1.1),
            (-1.5, -1.5), (-0.45, 0.6)]
    b.prism(dart, 0.45, m, top_scale=0.7)
    ob = b.finish(normalize="none")
    bpy.context.scene.collection.objects.link(ob)
    ob.location = loc
    ob.rotation_euler = (0, 0, math.radians(35))
    return ob


def place_scale_ship(obs, gap=3.5):
    """Rechts neben dem Objekt (in Bildrichtung), auf Hoehe der Spielebene."""
    d, q, right, up = view_dir()
    rp = Vector((right.x, right.y, 0)).normalized()
    vs = [o.matrix_world @ v.co for o in obs for v in o.data.vertices]
    ext = max(Vector((v.x, v.y, 0)).dot(rp) for v in vs)
    return scale_ship(rp * (ext + gap + 2.4))


def pixel_of(sc, cam, ob):
    bpy.context.view_layer.update()
    vs = [ob.matrix_world @ v.co for v in ob.data.vertices]
    pts = [world_to_camera_view(sc, cam, v) for v in vs]
    W, H = sc.render.resolution_x, sc.render.resolution_y
    xs = [p.x * W for p in pts]
    ys = [(1 - p.y) * H for p in pts]
    return [min(xs), min(ys), max(xs), max(ys)]


def stats(p):
    obs = p.objects()
    tris = sum(K.tri_count(o) for o in obs)
    mats = sorted({m.name for o in obs for m in o.data.materials})
    rad = max(math.hypot(*(o.matrix_world @ v.co).xy) for o in obs for v in o.data.vertices)
    zs = [(o.matrix_world @ v.co).z for o in obs for v in o.data.vertices]
    return tris, mats, rad, max(zs) - min(zs)


def export_glb(p, rad, path):
    """Wie die Stationen: Radius 1 in der Spielebene. Masse temporaer skalieren, danach zurueck."""
    k = 1.0 / rad
    root = p.root
    for o in p.objects():
        o.data.transform(Matrix.Scale(k, 4))
    saved = [(c, c.matrix_basis.copy()) for c in root.children]
    for c, mb in saved:
        loc, rot, sca = mb.decompose()
        c.matrix_basis = Matrix.LocRotScale(loc * k, rot, sca)
    for o in bpy.context.scene.objects:
        o.select_set(False)
    for o in p.objects():
        o.select_set(True)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_yup=True,
                              export_apply=True, export_cameras=False, export_lights=False,
                              export_materials="EXPORT")
    for o in p.objects():
        o.data.transform(Matrix.Scale(1.0 / k, 4))
    for c, mb in saved:
        c.matrix_basis = mb


def godot(v):
    return [round(v.x, 4), round(v.z, 4), round(-v.y, 4)]


def set_accent(col):
    m = M("Accent")
    m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = col


COMMON_DIST = 37.0
COMMON_SHIP = 10.5     # Abstand des Massstab-Schiffs (Spieleinheiten, in Bildrichtung rechts)


def render_one(key, fn, info, opt):
    reset()
    sc, cam = setup_scene((1280, 800))
    set_accent(ACCENT_PREVIEW)
    p = fn()
    p.build()
    tris, mats, rad, height = stats(p)
    hard = []
    for h in p.hard:
        hard.append(dict(kind=h["kind"], gun=h["gun"], module=h["module"], node=h["node"],
                         pos=godot(h["pos"] / rad), dir=godot(h["dir"].normalized())))
    info[key] = dict(tris=tris, materials=mats, radius=round(rad, 2), height=round(height, 2), hardpoints=hard)
    name = out_name(key)
    set_accent((*K.PALETTE["Accent"][0], 1.0))
    export_glb(p, rad, os.path.join(OUT, name + ".glb"))
    set_accent(ACCENT_PREVIEW)
    if opt["render"]:
        ship = place_scale_ship(p.objects())
        frame(cam, p.objects() + [ship], fill=0.84)
        sc.render.filepath = os.path.join(TMP, name + "_raw.png")
        bpy.ops.render.render(write_still=True)
        info[key]["ship_px"] = pixel_of(sc, cam, ship)
        # gemeinsamer Massstab (klein)
        bpy.data.objects.remove(ship, do_unlink=True)
        d, q, right, up = view_dir()
        rp = Vector((right.x, right.y, 0)).normalized()
        ship2 = scale_ship(rp * COMMON_SHIP)
        sc.render.resolution_x, sc.render.resolution_y = 720, 450
        frame_fixed(cam, rp * 2.6 + Vector((0, 0, 3.8)), COMMON_DIST)
        sc.render.filepath = os.path.join(TMP, name + "_common.png")
        bpy.ops.render.render(write_still=True)
        info[key]["ship_px_common"] = pixel_of(sc, cam, ship2)
    print(f"PLATTFORM {key}: tris={tris} mats={len(mats)} {mats} radius={rad:.2f} hoehe={height:.2f}")


COMP = r'''
import json, sys
from PIL import Image, ImageDraw, ImageFont
out, tmp = sys.argv[1], sys.argv[2]
info = json.load(open(f"{tmp}/info.json"))
designs = json.load(open(f"{tmp}/designs.json"))
def font(sz, bold=False):
    for p in (("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf" if bold else "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"),
              ("/usr/share/fonts/truetype/noto/NotoSans-Bold.ttf" if bold else "/usr/share/fonts/truetype/noto/NotoSans-Regular.ttf")):
        try: return ImageFont.truetype(p, sz)
        except Exception: pass
    return ImageFont.load_default()
def label_ship(im, box, size, text):
    d = ImageDraw.Draw(im)
    x0, y0, x1, y1 = box
    f = font(size)
    tw = d.textlength(text, font=f)
    cx = (x0 + x1) / 2
    ty = y1 + 6
    if ty + size + 4 > im.height: ty = y0 - size - 10
    tx = max(4, min(im.width - tw - 4, cx - tw / 2))
    d.rectangle((tx - 4, ty - 2, tx + tw + 4, ty + size + 4), fill=(12, 14, 20))
    d.text((tx, ty), text, font=f, fill=(170, 178, 195))
# 1) Einzelbilder beschriften
for key, cat, letter, title, line in designs:
    name = ("satellite_" + key.split("_")[1]) if key.startswith("sat_") else key
    if "ship_px" not in info.get(key, {}): continue
    im = Image.open(f"{tmp}/{name}_raw.png").convert("RGB")
    label_ship(im, info[key]["ship_px"], 22, "Maßstab: S-Jäger, 4,8 E lang")
    d = ImageDraw.Draw(im)
    d.text((24, 18), f"{letter}  {title}", font=font(30, True), fill=(235, 238, 245))
    r = info[key]["radius"]
    d.text((24, 58), f"{line}", font=font(17), fill=(160, 170, 190))
    d.text((24, im.height - 34), f"Radius {r:.1f} E · {info[key]['tris']} Dreiecke · {len(info[key]['materials'])} Materialien",
           font=font(16), fill=(140, 150, 170))
    im.save(f"{out}/{name}.png")
# 2) Vergleichsbilder je Art
catnames = {"light": ("leicht", "Leichte Waffenstellung"), "medium": ("mittel", "Mittlere Waffenstellung"),
            "heavy": ("schwer", "Schwere Waffenstellung"), "satellite": ("satellit", "Satellit")}
W, H, G, T = 700, 437, 20, 96
for cat, (suf, ctitle) in catnames.items():
    row = [x for x in designs if x[1] == cat]
    cv = Image.new("RGB", (3 * W + 4 * G, T + H + 2 * G + 60), (12, 14, 20))
    d = ImageDraw.Draw(cv)
    for i, (key, _, letter, title, line) in enumerate(row):
        name = ("satellite_" + key.split("_")[1]) if key.startswith("sat_") else key
        x = G + i * (W + G)
        im = Image.open(f"{out}/{name}.png").convert("RGB")
        im = im.crop((0, 90, im.width, im.height - 44)).resize((W, H), Image.LANCZOS)
        cv.paste(im, (x, T))
        d.text((x, 14), f"{letter}  {title}", font=font(30, True), fill=(235, 238, 245))
        d.text((x, 56), line, font=font(16), fill=(160, 170, 190))
        i2 = info[key]
        d.text((x, T + H + 8), f"Radius {i2['radius']:.1f} E · {i2['tris']} Dreiecke", font=font(16), fill=(140, 150, 170))
    d.text((G, T + H + 36), f"{ctitle} · Accent in Beispiel-Fraktionsfarbe · grauer Umriss = S-Jäger als Maßstab", font=font(16), fill=(110, 118, 135))
    cv.save(f"{out}/vorschlaege_{suf}.jpg", quality=90)
# 3) Gesamtblatt im gemeinsamen Massstab
CW, CH, LW = 720, 450, 250
cv = Image.new("RGB", (LW + 3 * CW + 4 * 12, 90 + 4 * (CH + 12) + 50), (12, 14, 20))
d = ImageDraw.Draw(cv)
d.text((20, 18), "Kleinstationen – alle Vorschläge im gemeinsamen Maßstab", font=font(34, True), fill=(235, 238, 245))
d.text((20, 60), "Grauer Umriss = S-Jäger (4,8 E). Zum Vergleich: normale Station Radius 15 E.", font=font(17), fill=(160, 170, 190))
for r, cat in enumerate(["light", "medium", "heavy", "satellite"]):
    y = 90 + r * (CH + 12)
    d.text((20, y + CH / 2 - 30), catnames[cat][1].replace(" ", "\n", 1), font=font(24, True), fill=(225, 228, 238))
    for c, (key, _, letter, title, line) in enumerate([x for x in designs if x[1] == cat]):
        name = ("satellite_" + key.split("_")[1]) if key.startswith("sat_") else key
        x = LW + 12 + c * (CW + 12)
        im = Image.open(f"{tmp}/{name}_common.png").convert("RGB")
        dd = ImageDraw.Draw(im)
        dd.text((14, 10), f"{letter}  {title}", font=font(22, True), fill=(235, 238, 245))
        dd.text((14, 40), f"Radius {info[key]['radius']:.1f} E", font=font(15), fill=(150, 160, 180))
        if c == 0 and r == 0:
            label_ship(im, info[key]["ship_px_common"], 14, "S-Jäger")
        cv.paste(im, (x, y))
cv.save(f"{out}/vorschlaege_alle.jpg", quality=90)
print("Montage fertig")
'''


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    opt = {"only": None, "render": True, "sheets": False}
    i = 0
    while i < len(argv):
        if argv[i] == "--only":
            opt["only"] = set(argv[i + 1].split(","))
            i += 1
        elif argv[i] == "--no-render":
            opt["render"] = False
        elif argv[i] == "--sheets-only":
            opt["sheets"] = True
        i += 1
    info_path = os.path.join(TMP, "info.json")
    info = json.load(open(info_path)) if os.path.exists(info_path) else {}
    if not opt["sheets"]:
        for key, cat, letter, fn, title, line in DESIGNS:
            if opt["only"] and key not in opt["only"]:
                continue
            render_one(key, fn, info, opt)
        json.dump(info, open(info_path, "w"), indent=1)
    json.dump([(k, c, l, t, ln) for k, c, l, f, t, ln in DESIGNS], open(os.path.join(TMP, "designs.json"), "w"))
    hp = {out_name(k): dict(radius=v["radius"], height=v["height"], tris=v["tris"], materials=v["materials"],
                            hardpoints=v["hardpoints"]) for k, v in info.items()}
    json.dump(hp, open(os.path.join(OUT, "hardpoints.json"), "w"), indent=1, ensure_ascii=False)
    print("\n=== KLEINSTATIONEN ===")
    for key, cat, *_ in DESIGNS:
        if key in info:
            v = info[key]
            lim = CATS[cat][1]
            flag = "OK" if v["tris"] <= lim and len(v["materials"]) <= 8 else "UEBER GRENZE"
            print(f"{key:10s} tris={v['tris']:5d}/{lim} mats={len(v['materials'])} r={v['radius']:.2f} {flag}")
    if opt["render"] and all(k in info and "ship_px" in info[k] for k, *_ in DESIGNS):
        subprocess.run(["/usr/bin/python3", "-c", COMP, OUT, TMP], check=True,
                       env={"PATH": "/usr/bin", "HOME": os.path.expanduser("~")})


main()
