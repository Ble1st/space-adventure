"""Drei Rendering-Vorschlaege fuer das Jumpgate (nur Vorschlaege, nicht im Spiel).

Aufruf: blender --background --factory-startup --python tools/blender/gate_proposals.py
Ausgabe: docs/gates/gate_a|b|c.png (+ .glb) und vorschlaege.jpg. Eigenstaendig, keine Imports aus dem Repo.
Achse: Durchflugrichtung = +Y, Ring/Oeffnung liegt in der XZ-Ebene, Z ist oben (Spielebene = XY).
"""
import math, os, subprocess, sys
import bpy
from mathutils import Vector, Matrix, Euler

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
OUT = os.path.join(REPO, "docs", "gates")
TMP = "/home/gerd/space-adventure-blender-tmp"
os.makedirs(OUT, exist_ok=True)
os.makedirs(TMP, exist_ok=True)

# ------------------------------------------------------------------ Materialien
def mats(state):
    """state: open | closed. Gleiche 7 Materialien in beiden Zustaenden."""
    for m in list(bpy.data.materials):
        bpy.data.materials.remove(m)
    op = state == "open"
    spec = {
        "Hull": dict(c=(0.74, 0.72, 0.65), r=0.5, m=0.15),
        "Trim": dict(c=(0.46, 0.43, 0.36), r=0.6, m=0.25),
        "Dark": dict(c=(0.03, 0.035, 0.045), r=0.6, m=0.6),
        "Accent": dict(c=(0.04, 0.38, 0.75), r=0.45, m=0.2),
        "Signal": dict(e=(0.25, 1.0, 0.45) if op else (1.0, 0.1, 0.05), s=6.0 if op else 3.0),
        "Glow": dict(e=(0.25, 0.85, 1.0) if op else (0.9, 0.08, 0.04), s=7.0 if op else 1.8),
    }
    out = {}
    for name, d in spec.items():
        m = bpy.data.materials.new(name)
        m.use_nodes = True
        b = m.node_tree.nodes["Principled BSDF"]
        if "c" in d:
            b.inputs["Base Color"].default_value = (*d["c"], 1)
            b.inputs["Roughness"].default_value = d["r"]
            b.inputs["Metallic"].default_value = d["m"]
        else:
            b.inputs["Base Color"].default_value = (0, 0, 0, 1)
            b.inputs["Emission Color"].default_value = (*d["e"], 1)
            b.inputs["Emission Strength"].default_value = d["s"]
        out[name] = m
    out["Field"] = field_material(op)
    return out


def field_material(op):
    m = bpy.data.materials.new("Field")
    m.use_nodes = True
    try:
        m.surface_render_method = "BLENDED"
    except Exception:
        pass
    nt = m.node_tree
    b = nt.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (0, 0, 0, 1)
    tc = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    sq = nt.nodes.new("ShaderNodeVectorMath"); sq.operation = "LENGTH"
    mul = nt.nodes.new("ShaderNodeMath"); mul.operation = "MULTIPLY"
    mul.inputs[1].default_value = 1.0 / 5.0  # Radius-Normierung ueber Custom-Property
    wave = nt.nodes.new("ShaderNodeTexWave"); wave.wave_type = "RINGS"
    wave.inputs["Scale"].default_value = 0.9
    wave.inputs["Distortion"].default_value = 2.0
    wave.inputs["Detail"].default_value = 1.0
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    mixr = nt.nodes.new("ShaderNodeMath"); mixr.operation = "ADD"
    L = nt.links
    # Entfernung von der Achse (Objektkoordinaten x,z)
    comb = nt.nodes.new("ShaderNodeCombineXYZ")
    L.new(tc.outputs["Object"], sep.inputs[0])
    L.new(sep.outputs["X"], comb.inputs["X"]); L.new(sep.outputs["Z"], comb.inputs["Z"])
    L.new(comb.outputs[0], sq.inputs[0]); L.new(sq.outputs["Value"], mul.inputs[0])
    L.new(tc.outputs["Object"], wave.inputs["Vector"])
    if op:
        e = ramp.color_ramp.elements
        e[0].position, e[0].color = 0.0, (0.35, 0.9, 1.0, 1)
        e[1].position, e[1].color = 1.0, (0.25, 0.25, 1.0, 1)
        L.new(mul.outputs[0], ramp.inputs["Fac"])
        L.new(ramp.outputs["Color"], b.inputs["Emission Color"])
        b.inputs["Emission Strength"].default_value = 3.2
        # Ringwellen heller/dunkler
        mw = nt.nodes.new("ShaderNodeMath"); mw.operation = "MULTIPLY_ADD"
        mw.inputs[1].default_value = 0.8; mw.inputs[2].default_value = 0.55
        L.new(wave.outputs["Fac"], mw.inputs[0])
        al = nt.nodes.new("ShaderNodeMath"); al.operation = "MULTIPLY"
        al.inputs[1].default_value = 0.9
        L.new(mw.outputs[0], al.inputs[0])
        L.new(al.outputs[0], b.inputs["Alpha"])
    else:
        b.inputs["Emission Color"].default_value = (0.5, 0.03, 0.02, 1)
        b.inputs["Emission Strength"].default_value = 0.15
        b.inputs["Alpha"].default_value = 0.18
    return m

# ------------------------------------------------------------------ Mesh-Baukasten
class MB:
    def __init__(self, names):
        self.names = names
        self.v = []
        self.f = []  # (verts, matindex)

    def _add(self, pts, faces, mat):
        o = len(self.v)
        self.v += [tuple(p) for p in pts]
        mi = self.names.index(mat)
        for f in faces:
            self.f.append(([o + i for i in f], mi))

    def box(self, c, s, mat, ry=0.0, bevel=0.0):
        hx, hy, hz = s[0] / 2, s[1] / 2, s[2] / 2
        pts = [(x, y, z) for x in (-hx, hx) for y in (-hy, hy) for z in (-hz, hz)]
        M = Matrix.Rotation(ry, 3, "Y")
        pts = [tuple(Vector(c) + M @ Vector(p)) for p in pts]
        F = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
        self._add(pts, F, mat)

    def prism(self, prof, depth, mat, c=(0, 0, 0), plane="XZ", ry=0.0):
        """Profil (x,z) entlang Y extrudiert (Tiefe depth, zentriert)."""
        n = len(prof)
        M = Matrix.Rotation(ry, 3, "Y")
        pts = []
        for (x, z) in prof:
            for y in (-depth / 2, depth / 2):
                pts.append(tuple(Vector(c) + M @ Vector((x, y, z))))
        F = []
        for i in range(n):
            j = (i + 1) % n
            F.append((2 * i, 2 * j, 2 * j + 1, 2 * i + 1))
        F.append(tuple(2 * i for i in reversed(range(n))))
        F.append(tuple(2 * i + 1 for i in range(n)))
        self._add(pts, F, mat)

    def sweep(self, prof, a0, a1, n, mat, y0=0.0, dy=0.0):
        """Profil (r, y) um die Y-Achse von a0..a1 (Winkel in XZ, rad)."""
        full = abs((a1 - a0) - 2 * math.pi) < 1e-6
        steps = n if full else n + 1
        pts = []
        for k in range(steps):
            a = a0 + (a1 - a0) * k / n
            for (r, y) in prof:
                pts.append((r * math.cos(a), y + y0 + dy * k / n, r * math.sin(a)))
        m = len(prof)
        F = []
        for k in range(n):
            k2 = (k + 1) % steps
            for i in range(m):
                j = (i + 1) % m
                F.append((k * m + i, k2 * m + i, k2 * m + j, k * m + j))
        if not full:
            F.append(tuple(i for i in reversed(range(m))))
            F.append(tuple((steps - 1) * m + i for i in range(m)))
        self._add(pts, F, mat)

    def disc(self, r, y, rings, segs, mat):
        pts = [(0, y, 0)]
        for ri in range(1, rings + 1):
            for s in range(segs):
                a = 2 * math.pi * s / segs
                pts.append((r * ri / rings * math.cos(a), y, r * ri / rings * math.sin(a)))
        F = [(0, 1 + (s + 1) % segs, 1 + s) for s in range(segs)]
        for ri in range(1, rings):
            for s in range(segs):
                a, b = 1 + (ri - 1) * segs + s, 1 + (ri - 1) * segs + (s + 1) % segs
                F.append((a, b, b + segs, a + segs))
        self._add(pts, F, mat)

    def cyl(self, c, r, length, mat, axis="Y", segs=10, r2=None):
        r2 = r if r2 is None else r2
        pts = []
        for e, rr in ((-length / 2, r), (length / 2, r2)):
            for s in range(segs):
                a = 2 * math.pi * s / segs
                u, w = rr * math.cos(a), rr * math.sin(a)
                p = {"Y": (u, e, w), "X": (e, u, w), "Z": (u, w, e)}[axis]
                pts.append((c[0] + p[0], c[1] + p[1], c[2] + p[2]))
        F = [(s, (s + 1) % segs, segs + (s + 1) % segs, segs + s) for s in range(segs)]
        F.append(tuple(reversed(range(segs))))
        F.append(tuple(segs + s for s in range(segs)))
        self._add(pts, F, mat)

    def build(self, name, matdict, names):
        me = bpy.data.meshes.new(name)
        me.from_pydata(self.v, [], [f for f, _ in self.f])
        me.update()
        for n in names:
            me.materials.append(matdict[n])
        for p, (_, mi) in zip(me.polygons, self.f):
            p.material_index = mi
        ob = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(ob)
        return ob

    def tris(self):
        return sum(len(f) - 2 for f, _ in self.f)


NAMES = ["Hull", "Trim", "Dark", "Accent", "Signal", "Glow", "Field"]


def chamfer(r0, r1, y0, y1, c):
    """Achteck-Profil (r,y) mit Fasen c."""
    return [(r0 + c, y0), (r1 - c, y0), (r1, y0 + c), (r1, y1 - c), (r1 - c, y1), (r0 + c, y1), (r0, y1 - c), (r0, y0 + c)]


def P(r, a, y=0.0):
    return (r * math.cos(a), y, r * math.sin(a))


# ------------------------------------------------------------------ Vorschlag A: Segmentring
def gate_a():
    b = MB(NAMES)
    full = 2 * math.pi
    # durchgehender dunkler Kern (Technikgraben)
    b.sweep([(4.25, -0.8), (5.6, -0.8), (5.6, 0.8), (4.25, 0.8)], 0, full, 36, "Dark")
    # Innenband Energiespule
    b.sweep([(4.2, -0.32), (4.32, -0.32), (4.32, 0.32), (4.2, 0.32)], 0, full, 36, "Glow")
    # Panzersegmente
    n = 12
    for i in range(n):
        a0 = full * i / n + math.radians(2.2)
        a1 = full * (i + 1) / n - math.radians(2.2)
        b.sweep(chamfer(4.75, 6.7, -1.35, 1.35, 0.28), a0, a1, 2, "Hull" if i % 2 == 0 else "Trim")
        # Deckplatte
        b.sweep([(6.7, -0.8), (7.0, -0.6), (7.0, 0.6), (6.7, 0.8)], a0 + 0.06, a1 - 0.06, 2, "Trim" if i % 2 == 0 else "Hull")
        # Fugennut / Glow-Streifen auf beiden Stirnseiten
        for sy in (-1, 1):
            b.sweep([(5.15, sy * 1.36), (5.3, sy * 1.36), (5.3, sy * 1.42), (5.15, sy * 1.42)], a0 + 0.08, a1 - 0.08, 1, "Glow")
        if i % 3 == 0:
            for sy in (-1, 1):
                b.sweep([(5.6, sy * 1.36), (6.5, sy * 1.36), (6.5, sy * 1.42), (5.6, sy * 1.42)], a0 + 0.1, a1 - 0.1, 1, "Accent")
    # Navigationsbaken
    for k in range(8):
        a = math.radians(22.5 + 45 * k)
        c = P(7.15, a)
        b.box(c, (0.5, 0.5, 0.5), "Signal", ry=-a)
        c2 = P(6.85, a)
        b.box(c2, (0.8, 0.28, 0.8), "Dark", ry=-a)
    # Kuehlrippen/Klammern an den vier Hauptachsen
    for k in range(4):
        a = math.radians(90 * k)
        b.box(P(7.0, a), (1.3, 2.9, 0.9), "Dark", ry=-a)
        b.box(P(7.6, a), (0.5, 3.1, 1.2), "Hull", ry=-a)
    b.disc(4.2, 0, 3, 32, "Field")
    return b


# ------------------------------------------------------------------ Vorschlag B: drei freie Boegen
def gate_b():
    b = MB(NAMES)
    full = 2 * math.pi
    half = math.radians(40)
    offs = (-0.9, 0.9, -0.2)
    for i in range(3):
        ac = math.radians(90 + 120 * i)
        y = offs[i]
        a0, a1 = ac - half, ac + half
        b.sweep(chamfer(4.55, 5.9, y - 0.8, y + 0.8, 0.22), a0, a1, 8, "Hull")
        b.sweep([(4.45, y - 0.25), (4.58, y - 0.25), (4.58, y + 0.25), (4.45, y + 0.25)], a0, a1, 8, "Glow")
        b.sweep(chamfer(5.9, 6.2, y - 0.45, y + 0.45, 0.08), a0 + 0.08, a1 - 0.08, 5, "Dark")
        for sy in (-1, 1):
            b.sweep(chamfer(4.9, 5.6, y + sy * 0.80, y + sy * 0.9, 0.03), a0 + 0.2, a1 - 0.2, 6, "Accent")
        # rotierende Segmente: duenne Ringfragmente aussen mit Marken
        for sy in (-1, 1):
            r0 = a0 - 0.25 if sy < 0 else ac - 0.55
            r1 = a1 + 0.25 if sy > 0 else ac + 0.55
            b.sweep(chamfer(6.3, 6.95, y + sy * 1.2, y + sy * 1.6, 0.08), r0, r1, 8, "Trim")
            for t in range(5):
                at = r0 + (r1 - r0) * (t + 0.5) / 5
                b.box(P(6.62, at, y + sy * 1.62), (0.26, 0.12, 0.26), "Signal" if t == 2 else "Glow", ry=-at)
            # Speichen
            b.box(P(6.3, ac, y + sy * 1.1), (0.28, 0.6, 0.28), "Dark", ry=-ac)
        # Emitterspitzen an den Bogenenden
        for e in (a0, a1):
            tip = P(4.85, e, y)
            b.cyl(tip, 0.42, 1.7, "Dark", axis="Y", segs=8)
            b.cyl(tip, 0.28, 2.0, "Glow", axis="Y", segs=8)
        # Pylon-Sockel hinten
        b.box(P(6.7, ac, y), (1.6, 1.0, 0.6), "Dark", ry=-ac)
        b.box(P(7.5, ac, y), (0.35, 0.35, 0.35), "Signal", ry=-ac)
    # Feldbegrenzung zwischen den Boegen: duenne Glow-Kanten
    for i in range(3):
        a0 = math.radians(90 + 120 * i + 40 + 6)
        a1 = math.radians(90 + 120 * (i + 1) - 40 - 6)
        b.sweep([(4.7, -0.07), (4.85, -0.07), (4.85, 0.07), (4.7, 0.07)], a0, a1, 6, "Glow")
    b.disc(4.75, 0, 3, 32, "Field")
    return b


# ------------------------------------------------------------------ Vorschlag C: Beschleunigertunnel + Zollstation
def gate_c():
    b = MB(NAMES)
    full = 2 * math.pi
    ys = [-7.5, -4.5, -1.5, 1.5, 4.5, 7.5]
    for i, y in enumerate(ys):
        big = i in (0, len(ys) - 1)
        r1 = 5.3 if big else 4.6
        d = 0.95 if big else 0.7
        b.sweep([(3.45 if not big else 3.6, y - d), (r1 - 0.2, y - d), (r1, y - d + 0.2), (r1, y + d - 0.2), (r1 - 0.2, y + d), (3.45 if not big else 3.6, y + d)], 0, full, 24, "Hull" if i % 2 == 0 else "Trim")
        b.sweep([(3.35, y - 0.2), (3.5, y - 0.2), (3.5, y + 0.2), (3.35, y + 0.2)], 0, full, 24, "Glow")
        if big:
            b.sweep([(4.2, y - d - 0.03), (5.0, y - d - 0.03), (5.0, y + d + 0.03), (4.2, y + d + 0.03)], 0.2, 1.4, 4, "Accent")
            b.sweep([(4.2, y - d - 0.03), (5.0, y - d - 0.03), (5.0, y + d + 0.03), (4.2, y + d + 0.03)], math.pi + 0.2, math.pi + 1.4, 4, "Accent")
    # Laengsschienen
    L = ys[-1] - ys[0] + 1.6
    for k in range(4):
        a = math.radians(45 + 90 * k)
        b.box(P(4.9, a, 0), (0.55, L, 0.55), "Dark", ry=-a)
        b.box(P(5.2, a, 0), (0.25, L - 1.5, 0.25), "Trim", ry=-a)
        for y in (-6.0, -3.0, 0.0, 3.0, 6.0):
            b.box(P(5.25, a, y), (0.3, 0.45, 0.3), "Glow", ry=-a)
    # Feld in der Mitte
    b.disc(3.4, ys[0] + 0.05, 2, 24, "Field")
    b.disc(3.4, ys[-1] - 0.05, 2, 24, "Field")
    # Kontroll-/Zollstation seitlich
    sx = 11.5
    b.box((sx, 0, -0.5), (4.0, 5.0, 8.0), "Hull")
    b.box((sx, 0, -0.5), (4.2, 5.2, 1.0), "Dark")
    b.box((sx, 0, 3.2), (3.0, 3.6, 1.6), "Trim")
    b.cyl((sx, 0, 4.5), 1.1, 1.1, "Trim", axis="Z", segs=12, r2=0.6)
    b.box((sx, 0, 5.4), (0.2, 0.2, 2.0), "Dark")
    b.box((sx, 0, 6.5), (0.5, 0.5, 0.5), "Signal")
    b.box((sx - 2.05, 0, 1.5), (0.1, 3.0, 0.8), "Accent")
    b.box((sx - 2.05, 0, -2.5), (0.1, 3.0, 0.8), "Glow")
    # Bruecke zum Tunnel (Zollgang) und Andockausleger
    b.box((8.2, -3.0, -0.5), (6.0, 1.0, 1.3), "Trim")
    b.box((8.2, 3.0, -0.5), (6.0, 1.0, 1.3), "Trim")
    for y in (-3.0, 3.0):
        b.box((5.6, y, -0.5), (0.5, 1.2, 1.5), "Dark")
    for y, sg in ((-4.8, -1), (4.8, 1)):
        b.box((sx, y, -2.5), (1.5, 2.8, 1.5), "Hull")  # Ausleger
        b.box((sx - 0.2, y + sg * 1.5, -2.5), (2.4, 0.5, 2.2), "Dark")  # Andockkragen
        b.box((sx - 1.35, y + sg * 1.75, -2.5), (0.2, 0.3, 0.9), "Signal")
        b.box((sx + 1.35, y + sg * 1.75, -2.5), (0.2, 0.3, 0.9), "Signal")
    return b

# ------------------------------------------------------------------ Platzhalter-Schiff
def ship(pos):
    b = MB(NAMES)
    b.cyl((0, 0.1, 0), 0.38, 1.9, "Hull", axis="Y", segs=6, r2=0.08)
    b.box((0, -0.25, 0.0), (1.9, 0.55, 0.08), "Trim")
    b.box((0.95, -0.25, 0.0), (0.22, 0.55, 0.14), "Accent")
    b.box((-0.95, -0.25, 0.0), (0.22, 0.55, 0.14), "Accent")
    b.box((0, -0.95, 0.0), (0.45, 0.3, 0.45), "Glow")
    ob = b.build("Ship", MATS, NAMES)
    ob.location = pos
    ob.scale = (1.4, 1.4, 1.4)
    return ob


# ------------------------------------------------------------------ Szene
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
    ramp.color_ramp.elements[0].position = 0.0
    ramp.color_ramp.elements[0].color = (1, 1, 1, 1)
    ramp.color_ramp.elements[1].position = 0.03
    ramp.color_ramp.elements[1].color = (0, 0, 0, 1)
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    grad = nt.nodes.new("ShaderNodeMapRange")
    grad.inputs["From Min"].default_value = -1.0
    grad.inputs["From Max"].default_value = 1.0
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
        sc.eevee.taa_render_samples = 64
    except AttributeError:
        pass
    try:
        sc.view_settings.view_transform = "Khronos PBR Neutral"
    except TypeError:
        pass
    setup_world(sc)
    sun("Key", 3.6, (1.0, 0.95, 0.88), (50, 0, -35))
    sun("Rim", 2.6, (0.55, 0.70, 1.0), (-60, 0, 150))
    sun("Fill", 1.2, (0.8, 0.85, 1.0), (75, 0, 120))
    cd = bpy.data.cameras.new("Cam")
    cd.lens = 50
    cam = bpy.data.objects.new("Cam", cd)
    sc.collection.objects.link(cam)
    sc.camera = cam
    return sc, cam


def frame(cam, obs, az, el, fill=0.88, shift=(0.0, 0.0)):
    sc = bpy.context.scene
    d = Vector((math.cos(math.radians(el)) * math.sin(math.radians(az)),
                -math.cos(math.radians(el)) * math.cos(math.radians(az)),
                math.sin(math.radians(el))))
    q = (-d).to_track_quat("-Z", "Y")
    right, up = q @ Vector((1, 0, 0)), q @ Vector((0, 1, 0))
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


def export_glb(ob, path):
    for o in bpy.context.scene.objects:
        o.select_set(False)
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_yup=True,
                              export_apply=True, export_cameras=False, export_lights=False,
                              export_materials="EXPORT")


GATES = {
    "a": (gate_a, "Segmentring", dict(az=-32, el=38)),
    "b": (gate_b, "Schwebende Boegen", dict(az=-30, el=38)),
    "c": (gate_c, "Beschleunigertunnel", dict(az=-38, el=40)),
}
MATS = {}


def render(key, state, res, path, glb=None):
    global MATS
    reset()
    MATS = mats(state)
    sc, cam = setup_scene(res)
    fn, _, cp = GATES[key]
    b = fn()
    g = b.build("Gate_" + key.upper(), MATS, NAMES)
    shipy = -14.5 if key == "c" else -9.5
    s = ship((0, shipy, 0))
    if glb:
        print("TRIS", key, b.tris(), "MATERIALS", len({m for _, m in b.f}) , "(+Field inkl.)")
        export_glb(g, glb)
    frame(cam, [g, s], cp["az"], cp["el"], fill=0.9)
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)


for k in GATES:
    render(k, "open", (1280, 800), os.path.join(OUT, f"gate_{k}.png"), glb=os.path.join(OUT, f"gate_{k}.glb"))
    render(k, "closed", (640, 400), os.path.join(TMP, f"gate_{k}_closed.png"))

COMP = r'''
import sys
from PIL import Image, ImageDraw, ImageFont
out, tmp = sys.argv[1], sys.argv[2]
titles = {"a": ("A", "Segmentring", "massiver Ring, Panzerplatten, Energiespule, Baken"),
          "b": ("B", "Schwebende Bögen", "drei freie Bögen + Feld, rotierende Ringe"),
          "c": ("C", "Beschleunigertunnel", "gestaffelte Ringe, Zollstation, Andockausleger")}
def font(sz, bold=False):
    for p in ("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf" if bold else "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
              "/usr/share/fonts/truetype/noto/NotoSans-Bold.ttf" if bold else "/usr/share/fonts/truetype/noto/NotoSans-Regular.ttf"):
        try: return ImageFont.truetype(p, sz)
        except Exception: pass
    return ImageFont.load_default()
W, H, G, T = 700, 437, 20, 90
cv = Image.new("RGB", (3 * W + 4 * G, T + H + G * 2), (12, 14, 20))
d = ImageDraw.Draw(cv)
for i, k in enumerate("abc"):
    x = G + i * (W + G)
    im = Image.open(f"{out}/gate_{k}.png").convert("RGB").resize((W, H), Image.LANCZOS)
    cl = Image.open(f"{tmp}/gate_{k}_closed.png").convert("RGB").resize((240, 150), Image.LANCZOS)
    im.paste(cl, (W - 250, H - 160))
    ImageDraw.Draw(im).rectangle((W - 250, H - 160, W - 11, H - 11), outline=(150, 160, 180), width=2)
    ImageDraw.Draw(im).text((W - 244, H - 156), "geschlossen", font=font(13), fill=(230, 200, 200))
    cv.paste(im, (x, T))
    l, t, s = titles[k]
    d.text((x, 14), f"{l}  {t}", font=font(30, True), fill=(235, 238, 245))
    d.text((x, 54), s, font=font(17), fill=(160, 170, 190))
cv.save(f"{out}/vorschlaege.jpg", quality=92)
'''
subprocess.run(["/usr/bin/python3", "-c", COMP, OUT, TMP], check=True, env={"PATH": "/usr/bin", "HOME": os.path.expanduser("~")})
print("fertig")
