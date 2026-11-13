"""Prueft die exportierten GLBs: Re-Import in Blender, Dreiecke, Materialien, Masse.

Aufruf (aus dem Repo-Wurzelverzeichnis):
    blender --background --factory-startup --python tools/blender/verify_stations.py

Nach dem Import ist wieder Blender-Z oben; die Spielebene ist XY. Geprueft wird:
ein Mesh je Datei, Material "Accent" vorhanden, max. Radius in der Ebene ~1,
Dreiecke <= 5000, Stellungen je Geschuetz laut Weltdatei (stations.json), keine Normalen-Nullvektoren.
"""

import math
import os
import sys

import json

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
GLB_DIR = os.path.join(REPO, "godot", "assets", "stations")


META, GUNS = {}, {}


def main():
    META.update(json.load(open(os.path.join(GLB_DIR, "stations.json"), encoding="utf-8"))["models"])
    world = os.path.join(REPO, "src", "SpaceSim.Core", "Content", "default-world.json")
    GUNS.update({t["key"]: (t.get("defense") or {}).get("guns", [])
                 for t in json.load(open(world, encoding="utf-8"))["stationTypes"]})
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import station_models
    for k, g in station_models.SMALL_GUNS.items():
        GUNS.setdefault(k, g)
    LIMIT = {"satellite_sensor": 1200, "platform_light": 1500, "platform_medium": 2500, "platform_heavy": 3500}
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
        key = f[:-4]
        turrets = [o for o in meshes if o.name.startswith("Turret_")]
        if len(meshes) - len(turrets) != 1:
            problems.append(f"{len(meshes)} Meshes")
        hps = META.get(key, {}).get("hardpoints", [])
        gh = [h for h in hps if h["gun"] >= 0]
        if [h["gun"] for h in gh] != list(range(len(GUNS.get(key, [])))):
            problems.append(f"{len(gh)} Stellungen fuer {len(GUNS.get(key, []))} Geschuetze")
        npd = sum(1 for h in hps if h["kind"] == "pd")
        if key in station_models.SMALL_PD and npd != station_models.SMALL_PD[key]:
            problems.append(f"{npd} PD statt {station_models.SMALL_PD[key]}")
        if "radius" not in META.get(key, {}):
            problems.append("radius fehlt")
        if sorted(h["node"] for h in hps if h.get("node")) != sorted(o.name for o in turrets):
            problems.append("Turm-Nodes passen nicht zu stations.json")
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
        allv = [o.matrix_world @ v.co for o in meshes for v in o.data.vertices]
        for h in hps:
            gx, gy, gz = h["pos"]
            pb = Vector((gx, -gz, gy))
            dmin = min((pb - v).length for v in allv)
            if dmin > 0.04:
                problems.append(f"Geschuetz {h['gun']}: pos {dmin:.3f} neben der Geometrie")
        if len(mats) > 10:
            problems.append(f"{len(mats)} Materialien")
        if "Accent" not in mats:
            problems.append("kein Accent")
        if tris > LIMIT.get(key, 5000):
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
