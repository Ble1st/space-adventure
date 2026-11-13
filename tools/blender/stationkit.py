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
    "Hull":      ((0.33, 0.35, 0.37), 0.55, 0.46, None, 0),      # Gunmetal
    "HullDark":  ((0.075, 0.08, 0.09), 0.50, 0.55, None, 0),     # Graphit
    "Armor":     ((0.23, 0.26, 0.22), 0.40, 0.60, None, 0),      # gedecktes Oliv
    "Steel":     ((0.18, 0.23, 0.30), 0.50, 0.50, None, 0),      # Stahlblau
    "Accent":    ((0.80, 0.80, 0.78), 0.05, 0.40, None, 0),      # wird spaeter fraktionsgefaerbt
    "Window":    ((0.40, 0.55, 0.65), 0.00, 0.40, (0.50, 0.80, 1.00), 1.1),
    "Light":     ((0.00, 0.25, 0.60), 0.00, 0.40, (0.00, 0.45, 1.00), 1.6),
    "LightRed":  ((0.50, 0.01, 0.00), 0.00, 0.40, (1.00, 0.02, 0.00), 1.5),
    "Glass":     ((0.04, 0.10, 0.15), 0.00, 0.08, (0.02, 0.10, 0.18), 0.4),
    "Panel":     ((0.03, 0.06, 0.16), 0.50, 0.25, (0.00, 0.03, 0.10), 0.4),
    "Glow":      ((0.50, 0.06, 0.00), 0.00, 0.50, (1.00, 0.12, 0.00), 1.4),
    "Plasma":    ((0.02, 0.15, 0.55), 0.00, 0.30, (0.03, 0.30, 1.00), 1.8),
    "Rock":      ((0.12, 0.105, 0.095), 0.00, 0.92, None, 0),
    "Gold":      ((0.75, 0.55, 0.22), 0.70, 0.35, None, 0),
    "Copper":    ((0.62, 0.30, 0.15), 0.60, 0.40, None, 0),
    "Hazard":    ((0.85, 0.58, 0.03), 0.05, 0.55, None, 0),
    "Plant":     ((0.05, 0.40, 0.08), 0.00, 0.60, (0.05, 0.85, 0.10), 1.0),
    "Ice":       ((0.55, 0.72, 0.82), 0.00, 0.20, (0.10, 0.28, 0.40), 0.4),
    "Crystal":   ((0.35, 0.10, 0.60), 0.10, 0.12, (0.50, 0.08, 1.00), 1.1),
    "Container": ((0.45, 0.22, 0.12), 0.15, 0.65, None, 0),
    "Rust":      ((0.33, 0.14, 0.06), 0.30, 0.80, None, 0),
    "Ember":     ((0.25, 0.00, 0.00), 0.00, 0.60, (1.00, 0.03, 0.00), 0.9),
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


def qdir(d):
    """Quaternion, die die lokale Z-Achse auf Richtung d dreht."""
    return Vector((0, 0, 1)).rotation_difference(Vector(d).normalized())


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


    # -------------------------------------------------- Raumstations-Bausteine
    def capsule(self, p, d, L, r, mat, seg=12):
        """Druckmodul: Zylinder mit gerundeten Enden, Achse d, Mitte p."""
        prof = [(0, -L / 2), (r * 0.72, -L / 2 + r * 0.28), (r, -L / 2 + r * 0.8),
                (r, L / 2 - r * 0.8), (r * 0.72, L / 2 - r * 0.28), (0, L / 2)]
        return self.lathe(prof, mat, seg, loc=p, rot=qdir(d))

    def spindle(self, r, h, mat, seg=14, loc=(0, 0, 0), cap=0.45):
        """Symmetrischer Kern: Zylinder mit Kegelkappen oben UND unten."""
        hh = h / 2
        prof = [(0, -hh - r * cap), (r * 0.55, -hh), (r, -hh + r * 0.3), (r, hh - r * 0.3),
                (r * 0.55, hh), (0, hh + r * cap)]
        return self.lathe(prof, mat, seg, loc=loc)

    def dock(self, p, d, r=0.7, L=2.2, mat="HullDark"):
        """Andockstutzen ab p in Richtung d mit Kragen und Leuchtring."""
        p, d = Vector(p), Vector(d).normalized()
        q = qdir(d)
        self.cyl(r, L, material(mat), seg=10, loc=p + d * (L / 2), rot=q)
        self.ring(r * 1.55, r * 0.5, 0.45, material("Hull"), seg=10, loc=p + d * L, rot=q)
        self.ring(r * 1.12, r * 0.12, 0.47, material("Light"), seg=10, loc=p + d * L, rot=q)
        return self

    def radiator(self, p, d, length, width, tilt=0.0, mat="Hull", stripe="Glow", n=3):
        """Radiator-/Kuehlfluegel: Ausleger ab p entlang d (in der Ebene), Paneel um d gekippt."""
        p, d = Vector(p), Vector((d[0], d[1], 0)).normalized()
        yaw = math.degrees(math.atan2(d.y, d.x))
        self.beam(p, p + d * length * 0.25, 0.3, material("HullDark"))
        c = p + d * length * 0.6
        self.box(length * 0.78, width, 0.1, material(mat), loc=c, rot=(tilt, 0, yaw))
        for k in range(n):
            o = (k - (n - 1) / 2) * width / (n + 0.5)
            q = Vector((-d.y, d.x, 0))
            off = q * o * math.cos(math.radians(tilt)) + Vector((0, 0, o * math.sin(math.radians(tilt))))
            self.box(length * 0.74, width * 0.07, 0.16, material(stripe), loc=c + off, rot=(tilt, 0, yaw))
        self.box(length * 0.78, 0.18, 0.2, material("HullDark"), loc=c, rot=(tilt, 0, yaw))
        return self

    def rcs(self, p, s=1.0):
        """Kleiner Manoevrierduesenblock."""
        p = Vector(p)
        self.box(0.6 * s, 0.6 * s, 0.6 * s, material("HullDark"), loc=p)
        for d in ((1, 0, 0), (-1, 0, 0), (0, 0, 1), (0, 0, -1)):
            dv = Vector(d)
            self.cyl(0.12 * s, 0.35 * s, material("Hull"), seg=6, loc=p + dv * 0.45 * s, rot=qdir(dv),
                     r2=0.2 * s, caps=False)
        return self

    def claw(self, p, d, s=1.0, mat="Hazard"):
        """Greifer mit drei Fingern ab p in Richtung d."""
        p, d = Vector(p), Vector(d).normalized()
        q = qdir(d)
        self.cyl(0.5 * s, 0.8 * s, material("HullDark"), seg=8, loc=p, rot=q)
        for k in range(3):
            a = math.radians(120 * k)
            o = q @ Vector((math.cos(a), math.sin(a), 0))
            j = p + d * 1.2 * s + o * 0.9 * s
            t = p + d * 2.6 * s + o * 0.45 * s
            self.beam(p + d * 0.4 * s, j, 0.2 * s, material(mat))
            self.beam(j, t, 0.16 * s, material(mat))
        return self


    # -------------------------------------------------- Wehrhafte Bausteine
    def pd(self, loc, up=True, s=1.0, facing=0.0):
        """Kleiner Punktverteidigungsturm: Sockel, kantiger Kopf, ein langes Rohr schraeg nach oben."""
        x, y, z = loc
        sg = 1 if up else -1
        self.cyl(0.55 * s, 0.4 * s, material("HullDark"), seg=6, loc=(x, y, z + sg * 0.2 * s))
        self.box(0.8 * s, 0.7 * s, 0.5 * s, material("Hull"), loc=(x, y, z + sg * 0.6 * s),
                 rot=(0, 0, facing))
        a = math.radians(facing)
        d = Vector((math.cos(a), math.sin(a), sg * 0.6)).normalized()
        b0 = Vector((x, y, z + sg * 0.65 * s))
        self.strut(b0, b0 + d * 1.6 * s, 0.07 * s, material("HullDark"), seg=4)
        self.box(0.15 * s, 0.15 * s, 0.1 * s, material("LightRed"),
                 loc=(x - math.cos(a) * 0.35 * s, y - math.sin(a) * 0.35 * s, z + sg * 0.9 * s))
        return self

    def missile_pod(self, loc, facing=0.0, up=True, s=1.0, rows=2, cols=3):
        """Raketenwerfer: Kasten mit Rohroeffnungen (rot) auf Schwenksockel."""
        x, y, z = loc
        sg = 1 if up else -1
        self.cyl(0.6 * s, 0.4 * s, material("HullDark"), seg=6, loc=(x, y, z + sg * 0.2 * s))
        c = Vector((x, y, z + sg * 0.85 * s))
        self.box(1.8 * s, 1.4 * s, 0.9 * s, material("Steel"), loc=c, rot=(0, -sg * 20, facing), bevel=0.06 * s)
        a = math.radians(facing)
        fwd = Vector((math.cos(a), math.sin(a), 0))
        side = Vector((-math.sin(a), math.cos(a), 0))
        for i in range(cols):
            for j in range(rows):
                p = c + fwd * 0.92 * s + side * ((i - (cols - 1) / 2) * 0.42 * s) + \
                    Vector((0, 0, (j - (rows - 1) / 2) * 0.38 * s + sg * 0.3 * s))
                self.box(0.08 * s, 0.26 * s, 0.24 * s, material("LightRed"), loc=p, rot=(0, -sg * 20, facing))
        return self

    def insignia(self, p, d, s=1.0, up=(0, 0, 1)):
        """Hoheitszeichen: kleines Wappenschild in Accent mit dunklem Rand, Blickrichtung d."""
        p, d = Vector(p), Vector(d).normalized()
        q = qdir(d)
        shield = [(-1.0, 1.0), (1.0, 1.0), (1.0, -0.15), (0.0, -1.25), (-1.0, -0.15)]
        rot90 = Euler((0, 0, 0)).to_quaternion()
        # Schild in lokaler XY-Ebene, Normale = lokale Z = d; lokales Y soll nach oben zeigen
        up = Vector(up)
        y_axis = (q @ Vector((0, 1, 0)))
        twist = y_axis.rotation_difference((up - d * up.dot(d)).normalized()) if (up - d * up.dot(d)).length > 1e-3 else rot90
        rq = twist @ q
        self.prism([(x * 1.15 * s, y * 1.15 * s) for x, y in shield], 0.12 * s, material("HullDark"), loc=p, rot=rq)
        self.prism([(x * s, y * s) for x, y in shield], 0.2 * s, material("Accent"), loc=p + d * 0.05 * s, rot=rq)
        return self

    def armor_belt(self, R, z, h, n, mat="Armor", tilt=12.0, t=0.35, loc=(0, 0, 0), start=0.0):
        """Panzerguertel aus n schraegen Plattensegmenten um die Z-Achse."""
        w = 2 * math.pi * R / n * 0.92
        for i in range(n):
            a = start + 360.0 * i / n
            r = math.radians(a)
            c = Vector(loc) + Vector((R * math.cos(r), R * math.sin(r), z))
            self.box(w, t, h, material(mat), loc=c, rot=(tilt, 0, a + 90))
        return self

    def hangar_slit(self, p, d, w=2.4, h=0.9):
        """Hangarschlitz: dunkle Oeffnung mit Leuchtrahmen, Blickrichtung d (in der Ebene)."""
        p, d = Vector(p), Vector((d[0], d[1], 0)).normalized()
        yaw = math.degrees(math.atan2(d.y, d.x))
        self.box(0.3, w, h, material("HullDark"), loc=p, rot=(0, 0, yaw))
        for zz in (h / 2, -h / 2):
            self.box(0.34, w, 0.07, material("Light"), loc=p + Vector((0, 0, zz)), rot=(0, 0, yaw))
        return self

    def mast(self, p, h, up=True, mat="HullDark", light="LightRed"):
        """Sensormast mit Querstreben und Licht."""
        p = Vector(p)
        sg = 1 if up else -1
        top = p + Vector((0, 0, sg * h))
        self.strut(p, top, 0.08, material(mat), seg=4)
        for k in (0.45, 0.75):
            c = p + Vector((0, 0, sg * h * k))
            self.box(0.9 * (1.2 - k), 0.06, 0.06, material(mat), loc=c)
        self.sphere(0.16, material(light), seg=6, rings=4, loc=top)
        return self

    # -------------------------------------------------- Abschluss
    def finish(self, sharp_angle=21.0, target_radius=1.0, center_z=True):
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
