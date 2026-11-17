"""Jumpgate "Beschleunigertunnel mit Zollstation" als Spielmodell (Entscheidung: Vorschlag C, kompakt).

Aufruf: blender --background --factory-startup --python tools/blender/gate_model.py
Ausgabe: godot/assets/gates/gate.glb, gate_destroyed.glb, gate.json; docs/gates/gate_final.png, gate_zustaende.jpg
Eigenstaendig bis auf stationkit.box_uv (UV fuer die gemeinsamen Oberflaechen-Texturen). Blender: Durchflug entlang Y, Oeffnung in XZ, Z oben.
glTF-Export (Y-up): Blender +Y -> Godot -Z, Blender Z -> Godot +Y, Blender X -> Godot +X.
Modell ist auf Oeffnungsradius 1.0 normiert, Ursprung = Mitte der Oeffnung.
Schalter (Umgebungsvariablen): GATE_RENDER_SHIP=1 stellt das kleine Groessenvergleichs-Schiff in die Bilder (Standard: ohne Schiff;
das Schiff war nie Teil der glb-Exporte); GATE_RENDER_ONLY=1 rendert nur die Bilder und laesst glb/json unveraendert.
"""
import json, math, os, subprocess, sys
import bpy
from mathutils import Vector, Matrix, Euler

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
DOCS = os.path.join(REPO, "docs", "gates")
ASSETS = os.path.join(REPO, "godot", "assets", "gates")
TMP = "/home/gerd/space-adventure-blender-tmp"
for d in (DOCS, ASSETS, TMP):
    os.makedirs(d, exist_ok=True)
RENDER_SHIP = os.environ.get("GATE_RENDER_SHIP") == "1"
RENDER_ONLY = os.environ.get("GATE_RENDER_ONLY") == "1"
R_OPEN = 2.9  # Oeffnungsradius in Arbeitseinheiten (Export skaliert auf 1.0)

STATES = {
    # Material -> (Emissionsfarbe, Staerke); Field: (Farbe innen, Farbe aussen, Staerke, Alpha)
    "open":       dict(sig=((0.25, 1.0, 0.45), 6.0), glow=((0.25, 0.85, 1.0), 7.0), f=((0.35, 0.9, 1.0), (0.25, 0.3, 1.0), 3.2, 0.9)),
    "restricted": dict(sig=((1.0, 0.5, 0.05), 6.0), glow=((1.0, 0.55, 0.1), 6.0), f=((1.0, 0.8, 0.3), (1.0, 0.35, 0.05), 3.0, 0.85)),
    "closed":     dict(sig=((1.0, 0.08, 0.04), 3.0), glow=((0.5, 0.04, 0.03), 1.0), f=((0.3, 0.03, 0.02), (0.3, 0.03, 0.02), 0.1, 0.12)),
    "destroyed":  dict(sig=((0.4, 0.03, 0.02), 0.3), glow=((0.2, 0.02, 0.01), 0.2), f=((0, 0, 0), (0, 0, 0), 0.0, 0.0)),
}


def mats(state, simple=False):
    for m in list(bpy.data.materials):
        bpy.data.materials.remove(m)
    S = STATES[state]
    dead = state == "destroyed"
    k = 0.45 if dead else 1.0  # verbrannt/dunkel
    spec = {
        "Hull": dict(c=tuple(v * k for v in (0.78, 0.76, 0.69)), r=0.5, m=0.15),
        "Trim": dict(c=tuple(v * k for v in (0.5, 0.47, 0.4)), r=0.6, m=0.25),
        "Dark": dict(c=(0.03, 0.035, 0.045), r=0.6, m=0.6),
        "Accent": dict(c=tuple(v * (0.35 if dead else 1.0) for v in (0.04, 0.38, 0.75)), r=0.45, m=0.2),
        "Signal": dict(e=S["sig"][0], s=S["sig"][1]),
        "Glow": dict(e=S["glow"][0], s=S["glow"][1]),
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
    out["Field"] = field_material(S["f"], simple)
    return out


def field_material(f, simple):
    c0, c1, st, al = f
    m = bpy.data.materials.new("Field")
    m.use_nodes = True
    try:
        m.surface_render_method = "BLENDED"
    except Exception:
        pass
    nt = m.node_tree
    b = nt.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (0, 0, 0, 1)
    b.inputs["Emission Strength"].default_value = st
    if simple:  # fuer den glTF-Export: Konstanten
        b.inputs["Emission Color"].default_value = (*c0, 1)
        b.inputs["Alpha"].default_value = max(al, 0.05) * 0.7
        return m
    tc = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    comb = nt.nodes.new("ShaderNodeCombineXYZ")
    ln = nt.nodes.new("ShaderNodeVectorMath"); ln.operation = "LENGTH"
    mul = nt.nodes.new("ShaderNodeMath"); mul.operation = "MULTIPLY"
    mul.inputs[1].default_value = 1.0 / R_OPEN
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    wave = nt.nodes.new("ShaderNodeTexWave"); wave.wave_type = "RINGS"
    wave.inputs["Scale"].default_value = 0.9
    wave.inputs["Distortion"].default_value = 2.0
    mw = nt.nodes.new("ShaderNodeMath"); mw.operation = "MULTIPLY_ADD"
    mw.inputs[1].default_value = 0.8; mw.inputs[2].default_value = 0.55
    ak = nt.nodes.new("ShaderNodeMath"); ak.operation = "MULTIPLY"; ak.inputs[1].default_value = al
    L = nt.links
    L.new(tc.outputs["Object"], sep.inputs[0])
    L.new(sep.outputs["X"], comb.inputs["X"]); L.new(sep.outputs["Z"], comb.inputs["Z"])
    L.new(comb.outputs[0], ln.inputs[0]); L.new(ln.outputs["Value"], mul.inputs[0])
    e = ramp.color_ramp.elements
    e[0].position, e[0].color = 0.0, (*c0, 1)
    e[1].position, e[1].color = 1.0, (*c1, 1)
    L.new(mul.outputs[0], ramp.inputs["Fac"])
    L.new(ramp.outputs["Color"], b.inputs["Emission Color"])
    L.new(tc.outputs["Object"], wave.inputs["Vector"])
    L.new(wave.outputs["Fac"], mw.inputs[0]); L.new(mw.outputs[0], ak.inputs[0])
    L.new(ak.outputs[0], b.inputs["Alpha"])
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

    def mark(self):
        return len(self.v)

    def xform(self, i0, euler=(0, 0, 0), pivot=(0, 0, 0), trans=(0, 0, 0)):
        """Alle seit mark() erzeugten Punkte um pivot drehen (Euler XYZ, rad) und verschieben."""
        M = Euler(euler, "XYZ").to_matrix()
        pv, tr = Vector(pivot), Vector(trans)
        for i in range(i0, len(self.v)):
            self.v[i] = tuple(pv + M @ (Vector(self.v[i]) - pv) + tr)

    def tris(self):
        return sum(len(f) - 2 for f, _ in self.f)


NAMES = ["Hull", "Trim", "Dark", "Accent", "Signal", "Glow", "Field"]
MATS = {}


def chamfer(r0, r1, y0, y1, c):
    """Achteck-Profil (r,y) mit Fasen c."""
    return [(r0 + c, y0), (r1 - c, y0), (r1, y0 + c), (r1, y1 - c), (r1 - c, y1), (r0 + c, y1), (r0, y1 - c), (r0, y0 + c)]


def P(r, a, y=0.0):
    return (r * math.cos(a), y, r * math.sin(a))



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
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    from stationkit import box_uv
    for o in bpy.context.scene.objects:
        o.select_set(False)
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    box_uv([ob])
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_yup=True,
                              export_apply=True, export_cameras=False, export_lights=False,
                              export_materials="EXPORT")




# ------------------------------------------------------------------ Modell
YS = [-3.9, -1.3, 1.3, 3.9]
FULL = 2 * math.pi


def rect(r0, r1, y0, y1):
    return [(r0, y0), (r1, y0), (r1, y1), (r0, y1)]


def end_ring(b, y, sgn):
    """Massiver Endring: Hull, Plattenfugen, Accent-Band, Signale, leuchtende Innenkante."""
    d = 0.42
    r0, r1 = R_OPEN + 0.05, 3.95
    b.sweep([(r0, y - d), (r1 - 0.2, y - d), (r1, y - d + 0.2), (r1, y + d - 0.2), (r1 - 0.2, y + d), (r0, y + d)], 0, FULL, 24, "Hull")
    b.sweep(rect(R_OPEN - 0.12, R_OPEN + 0.08, y - 0.2, y + 0.2), 0, FULL, 24, "Glow")  # leuchtende Innenkante
    for k in range(8):  # Plattenfugen
        a = FULL * (k + 0.5) / 8
        b.box(P((r0 + r1) / 2, a, y + sgn * (d + 0.01)), (r1 - r0 - 0.3, 0.05, 0.1), "Dark", ry=-a)
    # Accent-Band (Fraktionsfarbe) auf der Aussenseite
    for a0 in (0.35, math.pi + 0.35):
        b.sweep(rect(r1, r1 + 0.04, y - 0.3, y + 0.3), a0, a0 + 1.4, 3, "Accent")
        b.sweep(rect(r0 + 0.3, r1 - 0.3, y + sgn * d, y + sgn * (d + 0.04)) if sgn > 0 else rect(r0 + 0.3, r1 - 0.3, y - d - 0.04, y - d),
                a0 + 0.15, a0 + 1.25, 3, "Accent")
    for k in range(4):  # Statuslichter
        a = math.radians(45 + 90 * k)
        b.box(P(r1 + 0.05, a, y), (0.22, 0.3, 0.22), "Signal", ry=-a)


def inner_ring(b, y, i, gaps=None):
    d = 0.3
    r0, r1 = R_OPEN + 0.05, 3.5
    n = 8
    for k in range(n):
        a0 = FULL * k / n + 0.1
        a1 = FULL * (k + 1) / n - 0.1
        if gaps and k in gaps:
            continue
        b.sweep([(r0, y - d), (r1 - 0.12, y - d), (r1, y - d + 0.12), (r1, y + d - 0.12), (r1 - 0.12, y + d), (r0, y + d)], a0, a1, 3,
                "Hull" if (k + i) % 2 == 0 else "Trim")
    b.sweep(rect(R_OPEN - 0.1, R_OPEN + 0.08, y - 0.17, y + 0.17), 0, FULL, 24, "Glow")


def rails(b, y0, y1):
    L = y1 - y0
    for k in range(4):
        a = math.radians(45 + 90 * k)
        c = (y0 + y1) / 2
        b.box(P(3.95, a, c), (0.42, L, 0.42), "Dark", ry=-a)
        b.box(P(4.2, a, c), (0.2, L - 1.0, 0.2), "Trim", ry=-a)
        for y in (YS[1], YS[2]):
            b.box(P(4.2, a, y), (0.26, 0.4, 0.26), "Glow", ry=-a)


def station(b):
    sx = 6.1
    b.box((sx, 0, -0.3), (2.3, 2.6, 3.4), "Hull")
    b.box((sx, 0, -0.3), (2.5, 2.8, 0.5), "Dark")
    b.box((sx, 0, 1.7), (1.7, 2.0, 0.9), "Trim")
    b.cyl((sx, 0, 2.5), 0.55, 0.7, "Trim", axis="Z", segs=10, r2=0.3)
    b.box((sx, 0, 3.2), (0.1, 0.1, 1.0), "Dark")
    b.box((sx, 0, 3.8), (0.26, 0.26, 0.26), "Signal")
    b.box((sx - 1.17, 0, 0.6), (0.06, 1.8, 0.6), "Accent")
    b.box((sx + 1.17, 0, 0.6), (0.06, 1.8, 0.6), "Accent")
    b.box((sx - 1.17, 0, -1.2), (0.06, 1.8, 0.35), "Signal")
    # kurzer Zollgang zum Tunnel
    b.box((4.75, 0, -0.3), (1.0, 1.1, 0.9), "Trim")
    b.box((4.3, 0, -0.3), (0.3, 1.3, 1.1), "Dark")
    # zwei kurze Andockausleger
    for sg in (-1, 1):
        b.box((sx, sg * 1.75, -1.35), (1.3, 1.2, 0.9), "Hull")
        b.box((sx, sg * 2.45, -1.35), (1.7, 0.3, 1.2), "Dark")
        b.box((sx, sg * 2.62, -1.35), (0.3, 0.06, 0.3), "Signal")


def gate_intact():
    b = MB(NAMES)
    end_ring(b, YS[0], -1)
    end_ring(b, YS[3], 1)
    inner_ring(b, YS[1], 0)
    inner_ring(b, YS[2], 1)
    rails(b, YS[0] - 0.3, YS[3] + 0.3)
    b.disc(R_OPEN, YS[0] + 0.02, 2, 24, "Field")
    b.disc(R_OPEN, YS[3] - 0.02, 2, 24, "Field")
    station(b)
    return b


def gate_destroyed():
    """Zerstoert: Ringe teils weg/verbogen, Schienen gebrochen, Station nur noch ein Stumpf, Trümmer, kein Feld."""
    b = MB(NAMES)
    r0, r1 = R_OPEN + 0.05, 3.95
    prof = lambda y, d: [(r0, y - d), (r1 - 0.2, y - d), (r1, y - d + 0.2), (r1, y + d - 0.2), (r1 - 0.2, y + d), (r0, y + d)]
    # Endring vorn: grosser Bogen, verbogen, mit Zacke
    m = b.mark()
    b.sweep(prof(YS[0], 0.42), 0.5, 4.3, 12, "Hull")
    b.sweep(rect(R_OPEN - 0.12, R_OPEN + 0.08, YS[0] - 0.2, YS[0] + 0.2), 0.5, 4.3, 12, "Glow")
    b.sweep(rect(r1, r1 + 0.04, YS[0] - 0.3, YS[0] + 0.3), 0.8, 1.9, 3, "Accent")
    b.xform(m, euler=(0.22, 0, 0.1), pivot=(0, YS[0], 0), trans=(0.2, -0.3, -0.1))
    # Innenringe: einzelne Segmente, verdreht
    m = b.mark()
    for k, off in ((0, 0.0), (1, 0.0), (2, 0.0), (5, 0.0)):
        a0 = FULL * k / 8 + 0.1
        b.sweep([(R_OPEN + 0.05, YS[1] - 0.3), (3.38, YS[1] - 0.3), (3.5, YS[1] - 0.18), (3.5, YS[1] + 0.18), (3.38, YS[1] + 0.3), (R_OPEN + 0.05, YS[1] + 0.3)],
                a0, a0 + FULL / 8 - 0.2, 1, "Trim" if k % 2 else "Hull")
    b.xform(m, euler=(-0.18, 0.12, 0.25), pivot=(0, YS[1], 0), trans=(0.35, 0.2, 0.15))
    m = b.mark()
    b.sweep([(R_OPEN + 0.05, YS[2] - 0.3), (3.38, YS[2] - 0.3), (3.5, YS[2] - 0.18), (3.5, YS[2] + 0.18), (3.38, YS[2] + 0.3), (R_OPEN + 0.05, YS[2] + 0.3)],
            2.3, 4.6, 6, "Hull")
    b.xform(m, euler=(0.3, -0.1, -0.35), pivot=(0, YS[2], 0), trans=(-0.5, 0.1, -0.2))
    # hinterer Endring: nur ein Halbbogen
    m = b.mark()
    b.sweep(prof(YS[3], 0.42), 3.6, 6.9, 10, "Trim")
    b.sweep(rect(r1, r1 + 0.04, YS[3] - 0.3, YS[3] + 0.3), 4.0, 5.1, 3, "Accent")
    b.xform(m, euler=(-0.1, 0.0, 0.2), pivot=(0, YS[3], 0), trans=(0.1, 0.4, 0.0))
    # gebrochene Schienen
    for a, y0, y1, tilt in ((45, -4.2, -0.4, 0.06), (135, -1.0, 3.2, -0.1), (225, -2.6, 1.0, 0.12)):
        ar = math.radians(a)
        m = b.mark()
        b.box(P(3.95, ar, (y0 + y1) / 2), (0.42, y1 - y0, 0.42), "Dark", ry=-ar)
        b.xform(m, euler=(tilt, 0, tilt), pivot=P(3.95, ar, (y0 + y1) / 2), trans=(0, 0, 0))
    # Stationsstumpf
    m = b.mark()
    b.box((6.1, 0, -0.9), (2.3, 2.6, 1.6), "Hull")
    b.box((6.1, 0, -0.9), (2.5, 2.8, 0.4), "Dark")
    b.box((6.1, 0.2, 0.4), (1.4, 1.5, 1.0), "Trim")
    b.xform(m, euler=(0.12, 0.1, 0.3), pivot=(6.1, 0, -1.7), trans=(0.2, 0, 0))
    m = b.mark()
    b.box((5.6, 0, 1.6), (1.1, 1.3, 0.9), "Trim")
    b.xform(m, euler=(0.5, 0.7, 0.2), pivot=(5.6, 0, 1.6), trans=(-3.6, -1.5, 0.4))
    # Truemmer (deterministisch verteilt)
    for i in range(16):
        a = i * 2.4
        rr = 2.2 + (i * 37 % 10) * 0.42
        y = -4.8 + (i * 53 % 17) * 0.58
        sz = 0.22 + (i * 29 % 7) * 0.09
        c = P(rr, a, y)
        m = b.mark()
        b.box(c, (sz * 1.6, sz, sz * 0.7), "Hull" if i % 3 else "Dark")
        b.xform(m, euler=(i * 0.7, i * 1.3, i * 0.4), pivot=c)
    # vereinzelt tote Signale/Funken
    for c in ((3.4, -3.3, 1.3), (-2.9, 2.1, -2.0), (5.2, 0.6, -1.5)):
        b.box(c, (0.16, 0.16, 0.16), "Signal")
    return b


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


def render(state, res, path, az=36, el=40, with_ship=None, fill=0.9):
    global MATS
    reset()
    MATS = mats(state)
    sc, cam = setup_scene(res)
    b = (gate_destroyed if state == "destroyed" else gate_intact)()
    g = b.build("Gate", MATS, NAMES)
    obs = [g]
    if RENDER_SHIP if with_ship is None else with_ship:
        obs.append(ship((0, -9.0, 0)))
    bpy.context.view_layer.update()
    frame(cam, obs, az, el, fill=fill)
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    return b


def export(state, path):
    global MATS
    reset()
    MATS = mats(state, simple=True)
    b = (gate_destroyed if state == "destroyed" else gate_intact)()
    k = 1.0 / R_OPEN
    b.v = [(x * k, y * k, z * k) for x, y, z in b.v]
    g = b.build("Gate" if state != "destroyed" else "GateDestroyed", MATS, NAMES)
    export_glb(g, path)
    xs = [v[0] for v in b.v]; ys = [v[1] for v in b.v]; zs = [v[2] for v in b.v]
    ext = dict(width_x=round(max(xs) - min(xs), 3), length_pass=round(max(ys) - min(ys), 3), height_y=round(max(zs) - min(zs), 3),
               min=[round(min(xs), 3), round(min(zs), 3), round(min(ys), 3)], max=[round(max(xs), 3), round(max(zs), 3), round(max(ys), 3)])
    print("TRIS", state, b.tris(), "materials", len({m for _, m in b.f}))
    return b.tris(), ext


# ------------------------------------------------------------------ Ausfuehrung
if not RENDER_ONLY:
    tris, ext = export("open", os.path.join(ASSETS, "gate.glb"))
    tris_d, ext_d = export("destroyed", os.path.join(ASSETS, "gate_destroyed.glb"))
    meta = {
        "_doc": "Jumpgate 'Beschleunigertunnel mit Zollstation' (Vorschlag C, kompakt). Siehe tools/blender/gate_model.py.",
        "files": {"intact": "gate.glb", "destroyed": "gate_destroyed.glb"},
        "units": "Oeffnungsradius der Durchflugoeffnung = 1.0 (Spiel skaliert)",
        "origin": "Mitte der Oeffnung (Mitte des Tunnels); Zollstation liegt bei +X, dort auch alle Aufbauten",
        "axes_godot": {
            "pass_through": "entlang der Z-Achse (+Z oder -Z, Modell ist symmetrisch; Felder an beiden Enden bei z=+1.34 und z=-1.34)",
            "up": "+Y", "side_station": "+X",
            "opening_plane": "XY-Ebene (Oeffnung blickt entlang Z), Tunnel liegt waagerecht in der Spielebene XZ",
            "rotate": "zum Ausrichten des Tors um die Y-Achse drehen",
        },
        "opening_radius": 1.0,
        "extent_godot_units": {"x_side": ext["width_x"], "y_up": ext["height_y"], "z_length": ext["length_pass"], "min_xyz": ext["min"], "max_xyz": ext["max"]},
        "extent_destroyed": {"x_side": ext_d["width_x"], "y_up": ext_d["height_y"], "z_length": ext_d["length_pass"]},
        "triangles": {"gate": tris, "gate_destroyed": tris_d},
        "materials": ["Hull", "Trim", "Dark", "Accent", "Signal", "Glow", "Field"],
        "material_roles": {
            "Accent": "Fraktionsfarbe (Station + Bandsegmente an den Endringen); nur Basisfarbe setzen",
            "Field": "Durchflugfeld (zwei Flaechen an den Endringen, halbtransparent, Emission); im glb konstante Farbe, ohne Radialverlauf",
            "Glow": "Kanten/Spulen/Schienenbaken (Emission)",
            "Signal": "Statuslichter (Emission)",
            "Hull/Trim/Dark": "Struktur, nicht schalten",
        },
        "states": {
            "open": {"Field": "emission (0.35,0.9,1.0), Staerke 3.2, Alpha ~0.6", "Glow": "(0.25,0.85,1.0) x7", "Signal": "(0.25,1.0,0.45) x6"},
            "restricted": {"Field": "emission (1.0,0.7,0.2), Staerke 3.0, Alpha ~0.6", "Glow": "(1.0,0.55,0.1) x6", "Signal": "(1.0,0.5,0.05) x6"},
            "closed": {"Field": "Mesh ausblenden oder Alpha ~0.08, Emission (0.3,0.03,0.02) x0.1", "Glow": "(0.5,0.04,0.03) x1", "Signal": "(1.0,0.08,0.04) x3"},
            "destroyed": "gate_destroyed.glb verwenden (eigene Geometrie, verbrannte Materialfarben, kein Feld)",
        },
        "note": "Radialverlauf/Wellen des Felds nur in den Blender-Bildern (Shader-Knoten); im Spiel ggf. eigener Godot-Shader auf Material 'Field'.",
    }
    json.dump(meta, open(os.path.join(ASSETS, "gate.json"), "w"), indent=2, ensure_ascii=False)

render("open", (1280, 800), os.path.join(DOCS, "gate_final.png"))
for s in STATES:
    render(s, (800, 500), os.path.join(TMP, f"state_{s}.png"))
render("open", (800, 500), os.path.join(TMP, "flat.png"), el=22, az=30)

COMP = r'''
import sys
from PIL import Image, ImageDraw, ImageFont
docs, tmp = sys.argv[1], sys.argv[2]
def font(sz, bold=False):
    p = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf" if bold else "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"
    try: return ImageFont.truetype(p, sz)
    except Exception: return ImageFont.load_default()
names = [("open", "Offen", "Feld blau, Signale grün"), ("restricted", "Eingeschränkt", "Feld + Signale orange"),
         ("closed", "Geschlossen", "Feld aus, Signale rot"), ("destroyed", "Zerstört", "eigenes Mesh, dunkel, Trümmer")]
W, H, G, T = 620, 388, 16, 86
cv = Image.new("RGB", (4 * W + 5 * G, T + H + 2 * G), (12, 14, 20))
d = ImageDraw.Draw(cv)
for i, (k, t, s) in enumerate(names):
    x = G + i * (W + G)
    cv.paste(Image.open(f"{tmp}/state_{k}.png").convert("RGB").resize((W, H), Image.LANCZOS), (x, T))
    d.text((x, 12), t, font=font(28, True), fill=(235, 238, 245))
    d.text((x, 50), s, font=font(16), fill=(160, 170, 190))
cv.save(f"{docs}/gate_zustaende.jpg", quality=92)
'''
subprocess.run(["/usr/bin/python3", "-c", COMP, DOCS, TMP], check=True, env={"PATH": "/usr/bin", "HOME": os.path.expanduser("~")})
print("fertig")
