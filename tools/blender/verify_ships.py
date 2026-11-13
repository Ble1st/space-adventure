"""Prueft die exportierten Schiffs-GLBs (Rotor-Nodes: nach dem Re-Import in Blender ist die Drehachse die
lokale Y-Achse, in Godot die lokale Z-Achse): Re-Import in Blender, ein Rumpf-Mesh "Hull" plus die in ships.json
genannten Werkzeug-Nodes (Rotor_*, Jaw_*; bei Kontaktabbau muss ein Werkzeug der vorderste Punkt sein), Materialien Accent und
Engine, Halblaenge 1 (in Blender nach dem Import entlang +Y = Godot -Z), Bug vorn (Schubflaechen
"Engine" liegen hinten), Dreiecksgrenzen je Groessenklasse aus ships.json.

Aufruf (aus dem Repo-Wurzelverzeichnis):
    blender --background --factory-startup --python tools/blender/verify_ships.py
"""

import json
import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
GLB_DIR = os.path.join(REPO, "godot", "assets", "ships")
LIMIT = {"S": 1600, "M": 2400, "L": 3200, "XL": 4500}


def main():
    meta = json.load(open(os.path.join(GLB_DIR, "ships.json"), encoding="utf-8"))["models"]
    bad = total = 0
    print(f"{'Schiff':16s} {'Gr':2s} {'Dreiecke':>8s} {'Mat':>3s} {'Halbl.':>6s} {'Breite':>6s} {'Hoehe':>6s} {'KiB':>6s}  Status")
    # Breite = volle Breite bei Laenge 2, also Laenge:Breite = 2 / Breite
    for key, m in meta.items():
        for ob in list(bpy.data.objects):
            bpy.data.objects.remove(ob, do_unlink=True)
        for coll in (bpy.data.meshes, bpy.data.materials):
            for x in list(coll):
                coll.remove(x)
        path = os.path.join(GLB_DIR, key + ".glb")
        problems = []
        if not os.path.exists(path):
            print(f"{key:16s} fehlt")
            bad += 1
            continue
        bpy.ops.import_scene.gltf(filepath=path)
        meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
        hulls = [o for o in meshes if o.name.startswith("Hull")]
        parts = [o for o in meshes if o.name.startswith(("Rotor_", "Jaw_"))]
        if len(hulls) != 1 or len(hulls) + len(parts) != len(meshes):
            problems.append(f"Nodes: {[o.name for o in meshes]}")
        want = [r["node"] for r in m.get("rotors", []) + m.get("jaws", [])]
        if sorted(want) != sorted(o.name for o in parts):
            problems.append(f"Werkzeug-Nodes {sorted(o.name for o in parts)} != ships.json {sorted(want)}")
        if m.get("mining") == "contact" and not m.get("rotors"):
            problems.append("Kontaktabbau ohne Rotor")
        if m.get("rotors") and not m.get("contactRadius"):
            problems.append("contactRadius fehlt")
        tris = 0
        names = set()
        co = []
        ymax_tool = -9
        for o in meshes:
            me = o.data
            me.calc_loop_triangles()
            tris += len(me.loop_triangles)
            names |= {mt.name for mt in me.materials}
            pts = [o.matrix_world @ v.co for v in me.vertices]
            co += pts
            if o in parts:
                ymax_tool = max(ymax_tool, max(p.y for p in pts))
            if any(p.normal.length < 0.5 for p in me.polygons):
                problems.append("Nullnormale")
        names = sorted(names)
        for need in ("Accent", "Engine"):
            if need not in names:
                problems.append("kein " + need)
        xs, ys, zs = [c.x for c in co], [c.y for c in co], [c.z for c in co]
        half = (max(ys) - min(ys)) / 2
        if abs(half - 1.0) > 0.01:
            problems.append(f"Halblaenge {half:.3f}")
        if abs((max(ys) + min(ys)) / 2) > 0.01:
            problems.append("nicht zentriert")
        if m.get("mining") == "contact" and ymax_tool < max(ys) - 0.005:
            problems.append("Werkzeug ist nicht der vorderste Punkt")
        hm = hulls[0].data if hulls else None
        if hm and "Engine" in [mt.name for mt in hm.materials]:
            ei = [mt.name for mt in hm.materials].index("Engine")
            ey = [p.center.y for p in hm.polygons if p.material_index == ei]
            if ey and sum(ey) / len(ey) > -0.2:
                problems.append("Triebwerke nicht hinten (Bug falsch?)")
        for o in parts:
            loc = [v.co for v in o.data.vertices]
            ext = max(max(abs(c.x) for c in loc), max(abs(c.z) for c in loc), 1e-6)
            if o.name.startswith("Rotor_") and (abs(sum(c.x for c in loc) / len(loc)) > 0.12 * ext or
                                                abs(sum(c.z for c in loc) / len(loc)) > 0.12 * ext):
                problems.append(f"{o.name}: Ursprung nicht auf der Drehachse")
        if len(names) > 10:
            problems.append(f"{len(names)} Materialien")
        if tris > LIMIT[m["size"]]:
            problems.append(f"> {LIMIT[m['size']]} Dreiecke")
        size = os.path.getsize(path) / 1024
        total += tris
        bad += bool(problems)
        print(f"{key:16s} {m['size']:2s} {tris:8d} {len(names):3d} {half:6.3f} {max(xs)-min(xs):6.2f} "
              f"{max(zs)-min(zs):6.2f} {size:6.1f}  " + ("OK" if not problems else ", ".join(problems)))
    print(f"{len(meta)} Schiffe, {total} Dreiecke gesamt, {bad} mit Problemen")
    sys.exit(1 if bad else 0)


main()
