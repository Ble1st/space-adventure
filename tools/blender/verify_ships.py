"""Prueft die exportierten Schiffs-GLBs: Re-Import in Blender, ein Mesh, Materialien Accent und
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
LIMIT = {"S": 1200, "M": 1800, "L": 2500}


def main():
    meta = json.load(open(os.path.join(GLB_DIR, "ships.json"), encoding="utf-8"))["models"]
    bad = total = 0
    print(f"{'Schiff':16s} {'Gr':2s} {'Dreiecke':>8s} {'Mat':>3s} {'Halbl.':>6s} {'Breite':>6s} {'Hoehe':>6s} {'KiB':>6s}  Status")
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
        if len(meshes) != 1:
            problems.append(f"{len(meshes)} Meshes")
        me = meshes[0].data
        me.calc_loop_triangles()
        tris = len(me.loop_triangles)
        names = [mt.name for mt in me.materials]
        for need in ("Accent", "Engine"):
            if need not in names:
                problems.append("kein " + need)
        co = [meshes[0].matrix_world @ v.co for v in me.vertices]
        xs, ys, zs = [c.x for c in co], [c.y for c in co], [c.z for c in co]
        half = (max(ys) - min(ys)) / 2
        if abs(half - 1.0) > 0.01:
            problems.append(f"Halblaenge {half:.3f}")
        if abs((max(ys) + min(ys)) / 2) > 0.01:
            problems.append("nicht zentriert")
        if "Engine" in names:
            ei = names.index("Engine")
            ey = [p.center.y for p in me.polygons if p.material_index == ei]
            if ey and sum(ey) / len(ey) > -0.2:
                problems.append("Triebwerke nicht hinten (Bug falsch?)")
        if len(names) > 6:
            problems.append(f"{len(names)} Materialien")
        if tris > LIMIT[m["size"]]:
            problems.append(f"> {LIMIT[m['size']]} Dreiecke")
        if any(p.normal.length < 0.5 for p in me.polygons):
            problems.append("Nullnormale")
        size = os.path.getsize(path) / 1024
        total += tris
        bad += bool(problems)
        print(f"{key:16s} {m['size']:2s} {tris:8d} {len(names):3d} {half:6.3f} {max(xs)-min(xs):6.2f} "
              f"{max(zs)-min(zs):6.2f} {size:6.1f}  " + ("OK" if not problems else ", ".join(problems)))
    print(f"{len(meta)} Schiffe, {total} Dreiecke gesamt, {bad} mit Problemen")
    sys.exit(1 if bad else 0)


main()
