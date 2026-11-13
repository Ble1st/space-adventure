"""Prueft die exportierten GLBs: Re-Import in Blender, Dreiecke, Materialien, Masse.

Aufruf (aus dem Repo-Wurzelverzeichnis):
    blender --background --factory-startup --python tools/blender/verify_stations.py

Nach dem Import ist wieder Blender-Z oben; die Spielebene ist XY. Geprueft wird:
ein Mesh je Datei, Material "Accent" vorhanden, max. Radius in der Ebene ~1,
Dreiecke <= 3000, keine Normalen-Nullvektoren.
"""

import math
import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
GLB_DIR = os.path.join(REPO, "godot", "assets", "stations")


def main():
    files = sorted(f for f in os.listdir(GLB_DIR) if f.endswith(".glb"))
    bad = 0
    total = 0
    print(f"{'Typ':20s} {'Dreiecke':>8s} {'Mat':>3s} {'R_Ebene':>7s} {'Hoehe':>6s} {'KiB':>6s}  Status")
    for f in files:
        for ob in list(bpy.data.objects):
            bpy.data.objects.remove(ob, do_unlink=True)
        for me in list(bpy.data.meshes):
            bpy.data.meshes.remove(me)
        for m in list(bpy.data.materials):
            bpy.data.materials.remove(m)
        bpy.ops.import_scene.gltf(filepath=os.path.join(GLB_DIR, f))
        meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
        problems = []
        if len(meshes) != 1:
            problems.append(f"{len(meshes)} Meshes")
        tris = 0
        rad = 0.0
        zmin, zmax = 1e9, -1e9
        mats = set()
        for o in meshes:
            me = o.data
            me.calc_loop_triangles()
            tris += len(me.loop_triangles)
            mats |= {m.name for m in me.materials if m}
            for v in me.vertices:
                w = o.matrix_world @ v.co
                rad = max(rad, math.hypot(w.x, w.y))
                zmin, zmax = min(zmin, w.z), max(zmax, w.z)
            if any(p.normal.length < 0.5 for p in me.polygons):
                problems.append("Nullnormale")
        if "Accent" not in mats:
            problems.append("kein Accent")
        if tris > 3000:
            problems.append("zu viele Dreiecke")
        if abs(rad - 1.0) > 0.01:
            problems.append(f"Radius {rad:.3f}")
        size = os.path.getsize(os.path.join(GLB_DIR, f)) / 1024
        total += tris
        bad += bool(problems)
        print(f"{f[:-4]:20s} {tris:8d} {len(mats):3d} {rad:7.3f} {zmax - zmin:6.2f} {size:6.1f}  "
              + ("OK" if not problems else ", ".join(problems)))
    print(f"{len(files)} Dateien, {total} Dreiecke gesamt, {bad} mit Problemen")
    sys.exit(1 if bad else 0)


main()
