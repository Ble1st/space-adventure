"""Gemeinsames Hilfsmodul fuer die prozeduralen Stationsmodelle.

Alles wird in EIN bmesh pro Station gebaut (Primitive direkt hineingeschrieben,
Materialindex je Flaeche). Am Ende: ein Mesh-Objekt, glatte Schattierung mit
scharfen Kanten ueber einem Winkel (Hard-Surface-Look), Ursprung in der Mitte,
Groesse so normiert, dass der Koerper in der Spielebene (Blender XY = Godot XZ)
in den Einheitsradius 1 passt.

Nur Blender-eigene Module, kein Fremdcode.
"""

import math
import bmesh
import bpy
from mathutils import Euler, Matrix, Quaternion, Vector

# ---------------------------------------------------------------- Materialien
# Gemeinsame Palette, fraktionsneutral. "Accent" ist fast weiss, damit das Spiel
# es spaeter per Fraktionsfarbe einfaerben (multiplizieren) kann.
PALETTE = {
    #  name        base color              metal rough  emission color        strength
    "Hull":      ((0.58, 0.60, 0.63), 0.35, 0.42, None, 0),
    "HullDark":  ((0.16, 0.17, 0.19), 0.40, 0.50, None, 0),
    "Accent":    ((0.86, 0.86, 0.84), 0.05, 0.38, None, 0),
    "Window":    ((1.00, 0.72, 0.35), 0.00, 0.40, (1.00, 0.55, 0.15), 1.3),
    "Light":     ((0.25, 0.80, 1.00), 0.00, 0.40, (0.05, 0.60, 1.00), 2.0),
    "LightRed":  ((1.00, 0.12, 0.08), 0.00, 0.40, (1.00, 0.03, 0.01), 2.4),
    "Glass":     ((0.10, 0.26, 0.36), 0.00, 0.08, (0.04, 0.20, 0.30), 0.5),
    "Panel":     ((0.04, 0.09, 0.26), 0.50, 0.25, (0.01, 0.04, 0.14), 0.4),
    "Glow":      ((1.00, 0.35, 0.05), 0.00, 0.50, (1.00, 0.18, 0.00), 1.6),
    "Plasma":    ((0.55, 0.30, 1.00), 0.00, 0.30, (0.45, 0.12, 1.00), 2.6),
    "Rock":      ((0.20, 0.17, 0.15), 0.00, 0.92, None, 0),
    "Gold":      ((1.00, 0.70, 0.28), 0.55, 0.30, None, 0),
    "Copper":    ((0.88, 0.44, 0.22), 0.50, 0.35, None, 0),
    "Hazard":    ((0.95, 0.68, 0.05), 0.05, 0.55, None, 0),
    "Plant":     ((0.18, 0.70, 0.20), 0.00, 0.60, (0.06, 0.60, 0.08), 1.1),
    "Ice":       ((0.70, 0.85, 0.95), 0.00, 0.18, (0.15, 0.32, 0.45), 0.4),
    "Crystal":   ((0.75, 0.40, 1.00), 0.10, 0.12, (0.50, 0.12, 0.95), 1.2),
    "Container": ((0.62, 0.32, 0.18), 0.15, 0.60, None, 0),
}


def material(name):
    m = bpy.data.materials.get(name)
    if m is not None:
        return m
    col, metal, rough, emis, strength = PALETTE[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*col, 1.0)
    bsdf.inputs["Metallic"].default_value = metal
    bsdf.inputs["Roughness"].default_value = rough
    if emis:
        bsdf.inputs["Emission Color"].default_value = (*emis, 1.0)
        bsdf.inputs["Emission Strength"].default_value = strength
    m.diffuse_color = (*col, 1.0)
    return m


# ---------------------------------------------------------------- Hilfen
def rot_matrix(rot):
    """rot: Euler in Grad (x, y, z) oder Quaternion."""
    if rot is None:
        return Matrix.Identity(4)
    if isinstance(rot, Quaternion):
        return rot.to_matrix().to_4x4()
    return Euler(tuple(math.radians(a) for a in rot), "XYZ").to_matrix().to_4x4()


def trs(loc=(0, 0, 0), rot=None, scale=(1, 1, 1)):
    return Matrix.Translation(Vector(loc)) @ rot_matrix(rot) @ Matrix.Diagonal((*scale, 1.0))


def polar(n, radius, start=0.0):
    """n Punkte (x, y, winkel_grad) auf einem Kreis."""
    out = []
    for i in range(n):
        a = start + 360.0 * i / n
        r = math.radians(a)
        out.append((radius * math.cos(r), radius * math.sin(r), a))
    return out


class Builder:
    """Sammelt Geometrie fuer genau eine Station."""

    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.mats = []

    # -------------------------------------------------- intern
    def _mi(self, mat):
        if mat not in self.mats:
            self.mats.append(mat)
        return self.mats.index(mat)

    def _verts(self, pts):
        return [self.bm.verts.new(p) for p in pts]

    def _face(self, vs, mi):
        try:
            f = self.bm.faces.new(vs)
        except ValueError:
            return None
        f.material_index = mi
        f.smooth = True
        return f

    def _apply(self, verts, M):
        bmesh.ops.transform(self.bm, matrix=M, verts=verts)

    # -------------------------------------------------- Rotationskoerper
    def lathe(self, profile, mat, seg=16, loc=(0, 0, 0), rot=None, scale=(1, 1, 1),
              closed=False, cap_bottom=False, cap_top=False, start=0.0, arc=360.0):
        """Dreht ein (r, z)-Profil um Z. Profil von unten nach oben (aussen = rechts).
        r == 0 an einem Ende ergibt einen Pol. closed=True verbindet Ende mit Anfang
        (Profil dann gegen den Uhrzeigersinn in der (r, z)-Ebene, z. B. Torus).
        arc < 360 ergibt ein offenes Segment (z. B. halbe Ringe)."""
        mi = self._mi(mat)
        full = arc >= 359.999
        nseg = seg if full else max(1, int(seg))
        ncol = nseg if full else nseg + 1
        rings = []
        allv = []
        for (r, z) in profile:
            if r <= 1e-6 and not closed:
                v = self.bm.verts.new((0, 0, z))
                rings.append([v] * ncol)
                allv.append(v)
            else:
                ring = []
                for j in range(ncol):
                    a = math.radians(start + arc * j / nseg)
                    ring.append(self.bm.verts.new((r * math.cos(a), r * math.sin(a), z)))
                rings.append(ring)
                allv.extend(ring)
        nrow = len(rings) if closed else len(rings) - 1
        for i in range(nrow):
            ra, rb = rings[i], rings[(i + 1) % len(rings)]
            for j in range(nseg):
                j2 = (j + 1) % ncol
                quad = [ra[j], ra[j2], rb[j2], rb[j]]
                uniq = []
                for v in quad:
                    if v not in uniq:
                        uniq.append(v)
                if len(uniq) >= 3:
                    self._face(uniq, mi)
        if cap_bottom and full and len(set(rings[0])) > 1:
            self._face(list(reversed(rings[0])), mi)
        if cap_top and full and len(set(rings[-1])) > 1:
            self._face(list(rings[-1]), mi)
        self.last = list(dict.fromkeys(allv))
        self._apply(self.last, trs(loc, rot, scale))
        return self

    def rock(self, r, mat, seed=1, seg=14, rings=9, loc=(0, 0, 0), scale=(1, 1, 1), amp=0.22):
        """Unregelmaessiger Asteroidenkoerper (deterministisch verbeult)."""
        self.sphere(r, mat, seg=seg, rings=rings)
        vs = self.last
        for v in vs:
            p = v.co.normalized() if v.co.length > 0 else Vector((0, 0, 1))
            n = (math.sin(3.1 * p.x + seed) * math.cos(2.3 * p.y - seed * 0.7)
                 + 0.6 * math.sin(5.7 * p.z + 1.3 * seed + 2 * p.x)
                 + 0.35 * math.cos(9.1 * p.y + 4.0 * p.z + seed))
            v.co = p * r * (1 + amp * n / 1.95)
        self._apply(vs, trs(loc, None, scale))
        return self

    def cyl(self, r, h, mat, seg=16, loc=(0, 0, 0), rot=None, r2=None, caps=True):
        """Zylinder/Kegelstumpf entlang der lokalen Z-Achse, zentriert."""
        r2 = r if r2 is None else r2
        return self.lathe([(r, -h / 2), (r2, h / 2)], mat, seg, loc, rot,
                          cap_bottom=caps, cap_top=caps)

    def sphere(self, r, mat, seg=16, rings=8, loc=(0, 0, 0), rot=None, scale=(1, 1, 1),
               half=False):
        prof = []
        lo = 0 if not half else rings // 2
        for i in range(lo, rings + 1):
            phi = math.pi * i / rings
            prof.append((r * math.sin(phi), -r * math.cos(phi)))
        if half:
            prof[0] = (r, 0.0)
        return self.lathe(prof, mat, seg, loc, rot, scale, cap_bottom=half)

    def torus(self, R, r, mat, seg=24, minor=8, loc=(0, 0, 0), rot=None, scale=(1, 1, 1),
              minor_scale=(1, 1), start_minor=0.0):
        prof = []
        for i in range(minor):
            a = math.radians(start_minor) + 2 * math.pi * i / minor
            prof.append((R + r * minor_scale[0] * math.cos(a), r * minor_scale[1] * math.sin(a)))
        return self.lathe(prof, mat, seg, loc, rot, scale, closed=True)

    def ring(self, R, width, height, mat, seg=24, loc=(0, 0, 0), rot=None, arc=360.0, start=0.0):
        """Ring mit rechteckigem Querschnitt (Aussenradius R)."""
        ri = R - width
        prof = [(ri, -height / 2), (R, -height / 2), (R, height / 2), (ri, height / 2)]
        if arc >= 359.999:
            return self.lathe(prof, mat, seg, loc, rot, closed=True)
        # offenes Segment: zusaetzlich Endflaechen
        self.lathe(prof, mat, seg, loc, rot, closed=True, arc=arc, start=start)
        return self

    # -------------------------------------------------- Prismen und Kaesten
    def prism(self, pts2d, h, mat, loc=(0, 0, 0), rot=None, scale=(1, 1, 1), top_scale=1.0):
        """Prisma aus einem 2D-Polygon (gegen Uhrzeigersinn), Hoehe h, zentriert in Z."""
        mi = self._mi(mat)
        bot = self._verts([(x, y, -h / 2) for x, y in pts2d])
        top = self._verts([(x * top_scale, y * top_scale, h / 2) for x, y in pts2d])
        n = len(pts2d)
        for j in range(n):
            self._face([bot[j], bot[(j + 1) % n], top[(j + 1) % n], top[j]], mi)
        self._face(list(reversed(bot)), mi)
        self._face(top, mi)
        self._apply(bot + top, trs(loc, rot, scale))
        return self

    def box(self, sx, sy, sz, mat, loc=(0, 0, 0), rot=None, bevel=0.0):
        mi = self._mi(mat)
        res = bmesh.ops.create_cube(self.bm, size=1.0)
        verts = res["verts"]
        faces = list({f for v in verts for f in v.link_faces})
        for f in faces:
            f.material_index = mi
            f.smooth = True
        bmesh.ops.scale(self.bm, vec=(sx, sy, sz), verts=verts)
        if bevel > 0:
            edges = list({e for v in verts for e in v.link_edges})
            out = bmesh.ops.bevel(self.bm, geom=edges + verts, offset=bevel, segments=1,
                                  profile=0.5, affect="EDGES", clamp_overlap=True)
            verts = list(set(verts) | set(out.get("verts", [])))
            verts = [v for v in verts if v.is_valid]
            for f in out.get("faces", []):
                f.material_index = mi
                f.smooth = True
        self._apply(verts, trs(loc, rot))
        return self

    def beam(self, p1, p2, w, mat, h=None):
        """Vierkantstrebe von p1 nach p2."""
        p1, p2 = Vector(p1), Vector(p2)
        d = p2 - p1
        L = d.length
        if L < 1e-6:
            return self
        q = Vector((0, 0, 1)).rotation_difference(d.normalized())
        return self.box(w, h or w, L, mat, loc=(p1 + p2) / 2, rot=q)

    def strut(self, p1, p2, r, mat, seg=6):
        """Runde Strebe/Rohr von p1 nach p2."""
        p1, p2 = Vector(p1), Vector(p2)
        d = p2 - p1
        L = d.length
        if L < 1e-6:
            return self
        q = Vector((0, 0, 1)).rotation_difference(d.normalized())
        return self.cyl(r, L, mat, seg=seg, loc=(p1 + p2) / 2, rot=q)

    def truss(self, p1, p2, size, w, mat, n=4):
        """Dreiecksgittertraeger von p1 nach p2 (drei Gurte + Zickzack)."""
        p1, p2 = Vector(p1), Vector(p2)
        d = (p2 - p1)
        L = d.length
        if L < 1e-6:
            return self
        q = Vector((0, 0, 1)).rotation_difference(d.normalized())
        M = q.to_matrix()
        offs = []
        for k in range(3):
            a = math.radians(90 + 120 * k)
            offs.append(M @ Vector((size * math.cos(a), size * math.sin(a), 0)))
        for o in offs:
            self.beam(p1 + o, p2 + o, w, mat)
        for i in range(n):
            t0, t1 = i / n, (i + 1) / n
            a0 = p1 + d * t0 + offs[i % 3]
            a1 = p1 + d * t1 + offs[(i + 1) % 3]
            self.beam(a0, a1, w * 0.7, mat)
        return self

    # -------------------------------------------------- Bausteine
    def turret(self, loc, facing=0.0, s=1.0, hull="HullDark", barrel="Hull", light="LightRed",
               up=True, twin=True):
        """Geschuetzturm: Sockel, Kuppel, Rohre. facing in Grad (Richtung der Rohre in XY)."""
        x, y, z = loc
        sg = 1 if up else -1
        self.cyl(0.9 * s, 0.5 * s, material(hull), seg=10, loc=(x, y, z + sg * 0.25 * s))
        self.lathe([(0.85 * s, 0), (0.8 * s, 0.35 * s), (0.45 * s, 0.75 * s), (0, 0.8 * s)],
                   material(hull), seg=10, loc=(x, y, z + sg * 0.5 * s),
                   rot=None if up else (180, 0, 0))
        a = math.radians(facing)
        dx, dy = math.cos(a), math.sin(a)
        px, py = -dy, dx
        zz = z + sg * 0.85 * s
        offs = (-0.22, 0.22) if twin else (0.0,)
        for o in offs:
            b0 = Vector((x + px * o * s + dx * 0.3 * s, y + py * o * s + dy * 0.3 * s, zz))
            b1 = b0 + Vector((dx, dy, 0)) * 1.6 * s
            self.strut(b0, b1, 0.1 * s, material(barrel), seg=6)
        self.box(0.18 * s, 0.18 * s, 0.1 * s, material(light),
                 loc=(x - dx * 0.5 * s, y - dy * 0.5 * s, zz + sg * 0.05 * s))
        return self

    def dish(self, loc, r, mat, rot=None, depth=0.35, seg=14, feed=True, feed_mat="HullDark"):
        """Parabolschuessel, oeffnet nach lokal +Z."""
        prof = [(0.0, 0.0), (0.35 * r, 0.12 * depth * r), (0.7 * r, 0.5 * depth * r),
                (r, depth * r), (r * 0.97, depth * r + 0.05 * r), (0.68 * r, 0.56 * depth * r + 0.05 * r),
                (0.34 * r, 0.17 * depth * r + 0.05 * r), (0.0, 0.05 * r)]
        self.lathe(prof, mat, seg, loc, rot)
        if feed:
            M = trs(loc, rot)
            base = M @ Vector((0, 0, 0.05 * r))
            tip = M @ Vector((0, 0, depth * r * 1.8))
            self.strut(base, tip, 0.04 * r, material(feed_mat), seg=5)
            self.sphere(0.09 * r, material("Light"), seg=6, rings=4, loc=tip)
        return self

    def window_band(self, r, z, h, mat="Window", seg=16, n=8, gap=0.45, rot=None, loc=(0, 0, 0)):
        """Leuchtende Fensterbaender auf einem Zylinder (Segmente mit Luecken)."""
        step = 360.0 / n
        for i in range(n):
            self.lathe([(r, z - h / 2), (r, z + h / 2)], material(mat), max(2, seg // n),
                       loc=loc, rot=rot, start=i * step, arc=step * (1 - gap))
        return self

    def nav_lights(self, pts, mat="Light", s=0.25):
        for p in pts:
            self.sphere(s, material(mat), seg=6, rings=4, loc=p)
        return self

    # -------------------------------------------------- Abschluss
    def finish(self, sharp_angle=33.0, target_radius=1.0, center_z=True):
        bm = self.bm
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
        # Zentrieren: XY auf Bounding-Box-Mitte, Z auf Box-Mitte
        xs = [v.co.x for v in bm.verts]
        ys = [v.co.y for v in bm.verts]
        zs = [v.co.z for v in bm.verts]
        c = Vector(((min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2,
                    (min(zs) + max(zs)) / 2 if center_z else 0.0))
        for v in bm.verts:
            v.co -= c
        rad = max(math.hypot(v.co.x, v.co.y) for v in bm.verts)
        k = target_radius / rad
        for v in bm.verts:
            v.co *= k
        bm.normal_update()
        me = bpy.data.meshes.new(self.name)
        bm.to_mesh(me)
        bm.free()
        for m in self.mats:
            me.materials.append(m)
        me.set_sharp_from_angle(angle=math.radians(sharp_angle))
        # Fels flach schattieren (kantig): alle Kanten von Fels-Flaechen scharf
        flat = [i for i, m in enumerate(self.mats) if m.name in ("Rock",)]
        if flat:
            sharp = me.attributes.get("sharp_edge") or me.attributes.new("sharp_edge", "BOOLEAN", "EDGE")
            ek = {tuple(sorted(e.vertices)): e.index for e in me.edges}
            for p in me.polygons:
                if p.material_index in flat:
                    for e in p.edge_keys:
                        sharp.data[ek[e]].value = True
        ob = bpy.data.objects.new(self.name, me)
        return ob


def tri_count(ob):
    me = ob.data
    me.calc_loop_triangles()
    return len(me.loop_triangles)
