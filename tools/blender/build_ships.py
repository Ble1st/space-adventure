"""Baut alle Schiffsmodelle, exportiert GLB, rendert Vorschaubilder und die Uebersicht.

Aufruf (aus dem Repo-Wurzelverzeichnis):
    blender --background --factory-startup --python tools/blender/build_ships.py -- [--only k1,k2] [--no-render] [--no-overview] [--overview-only]

Ausgaben:
    godot/assets/ships/<key>.glb     ein Mesh, Bug in Godot nach -Z, Laenge 2 (Halblaenge 1)
    godot/assets/ships/ships.json    Groessenklasse und empfohlener relativer Massstab je Modell
    docs/ships/<key>.png             1024x768, oben schraeg vorn oben, unten schraeg hinten oben
    docs/ships/_overview.png         Raster mit Name, Schluessel, Groesse; Massstab je Groessenklasse
    docs/ships/overview.jpg          dieselbe Uebersicht als JPEG q88
"""

import json
import math
import os
import sys
import time

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import importlib  # noqa: E402
import stationkit  # noqa: E402
import build_stations as bs  # noqa: E402  (gemeinsame Szene-, Kamera- und Exporthilfen)
import ship_models  # noqa: E402

importlib.reload(stationkit)
importlib.reload(ship_models)

REPO = bs.REPO
GLB_DIR = os.path.join(REPO, "godot", "assets", "ships")
IMG_DIR = os.path.join(REPO, "docs", "ships")
WORLD = bs.WORLD
AZ, EL = 125.0, 34.0   # Kamera von schraeg vorn oben (Bug zeigt in Blender nach +Y)
REAR_AZ, REAR_EL = 50.0, 38.0   # zweite Ansicht von schraeg hinten oben
OV_EL = 45.0          # Uebersicht: steiler, wie die Spielkamera


def ship_list():
    with open(WORLD, encoding="utf-8") as f:
        d = json.load(f)
    names = {m["key"]: m.get("name", m["key"]) for m in d["shipModels"]}
    out = []
    for key, (fn, size, idea, name) in ship_models.SHIPS.items():
        out.append((key, name or names.get(key, key), size, idea))
    missing = [k for k in names if k != "unit_player" and k not in ship_models.SHIPS]
    if missing:
        raise SystemExit("Kein Schiffsmodell fuer: " + ", ".join(missing))
    return out


def args():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    opt = {"only": None, "render": True, "overview": True, "overview_only": False}
    i = 0
    while i < len(a):
        if a[i] == "--only":
            opt["only"] = a[i + 1].split(",")
            i += 1
        elif a[i] == "--no-render":
            opt["render"] = False
        elif a[i] == "--no-overview":
            opt["overview"] = False
        elif a[i] == "--overview-only":
            opt["overview_only"] = True
            opt["only"] = []
        i += 1
    return opt


def write_meta(ships):
    meta = {
        "axes": "Bug = -Z (Godot-Vorwaerts), oben = +Y, Steuerbord = +X; Laenge 2 (Halblaenge 1), Ursprung = Mitte der Bounding-Box",
        "sizeScale": ship_models.SIZE_SCALE,
        "models": {k: {"name": n, "size": s, "scale": ship_models.SIZE_SCALE[s]} for k, n, s, _ in ships},
    }
    with open(os.path.join(GLB_DIR, "ships.json"), "w", encoding="utf-8") as f:
        json.dump(meta, f, ensure_ascii=False, indent=2)
        f.write("\n")


def setup(res=(1024, 768)):
    """Szene wie bei den Stationen, Lichter um 180 Grad gedreht (Kamera schaut von vorn)."""
    scene, cam = bs.setup_scene(res=res)
    for ob in scene.objects:
        if ob.type == "LIGHT":
            ob.rotation_euler.z += math.pi
    return scene, cam


def render_two_views(scene, cam, ob, path):
    """1024x768: oben 3/4 von schraeg vorn oben, unten 3/4 von schraeg hinten oben (Triebwerke)."""
    import numpy as np
    scene.render.resolution_x, scene.render.resolution_y = 1024, 384
    parts = []
    for i, (az, el) in enumerate(((AZ, EL), (REAR_AZ, REAR_EL))):
        bs.frame(cam, ob, az=az, el=el, fill=0.86)
        tmp = os.path.join(IMG_DIR, f"_tmp_view{i}.png")
        scene.render.filepath = tmp
        bpy.ops.render.render(write_still=True)
        im = bpy.data.images.load(tmp)
        px = np.array(im.pixels[:], dtype=np.float32).reshape(384, 1024, 4)
        parts.append(px)
        bpy.data.images.remove(im)
        os.remove(tmp)
    both = np.concatenate([parts[1], parts[0]], axis=0)   # Bildzeilen laufen von unten nach oben
    both[383:385, :, :3] = 0.25
    out = bpy.data.images.new("views", 1024, 768)
    out.pixels = both.ravel()
    out.filepath_raw = path
    out.file_format = "PNG"
    out.save()
    bpy.data.images.remove(out)
    scene.render.resolution_x, scene.render.resolution_y = 1024, 768


def overview(ships, path):
    bs.reset()
    scene, cam = setup(res=(2400, 2160))
    cols = 5
    cw, ch = 5.4, 4.1
    d = Vector((math.cos(math.radians(OV_EL)) * math.sin(math.radians(AZ)),
                -math.cos(math.radians(OV_EL)) * math.cos(math.radians(AZ)),
                math.sin(math.radians(OV_EL))))
    q = (-d).to_track_quat("-Z", "Y")
    right, up = q @ Vector((1, 0, 0)), q @ Vector((0, 1, 0))
    rows = math.ceil(len(ships) / cols)
    lab = bpy.data.materials.new("Label")
    bsdf = lab.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (0.9, 0.92, 1.0, 1)
    bsdf.inputs["Emission Color"].default_value = (0.85, 0.9, 1.0, 1)
    bsdf.inputs["Emission Strength"].default_value = 1.6
    for i, (key, name, size, _) in enumerate(ships):
        r, c = divmod(i, cols)
        pos = right * ((c - (cols - 1) / 2) * cw) - up * ((r - (rows - 1) / 2) * ch)
        ob = ship_models.build(key)
        scene.collection.objects.link(ob)
        k = ship_models.SIZE_SCALE[size]
        ob.scale = (k, k, k)
        ob.location = pos + up * 0.55
        for label, sz, off in ((name, 0.36, -1.35), (f"{key} · {size}", 0.22, -1.72)):
            cu = bpy.data.curves.new("t_" + key, "FONT")
            cu.body = label
            cu.align_x = "CENTER"
            cu.size = sz
            to = bpy.data.objects.new("t_" + key, cu)
            to.data.materials.append(lab)
            to.rotation_euler = q.to_euler()
            to.location = pos + up * off + d * 3.0
            scene.collection.objects.link(to)
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = cols * cw + 0.4
    cam.location = d * 80
    cam.rotation_euler = q.to_euler()
    cam.data.clip_end = 300
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    im = bpy.data.images.load(path)
    scene.render.image_settings.file_format = "JPEG"
    scene.render.image_settings.quality = 88
    im.save_render(os.path.join(os.path.dirname(path), "overview.jpg"), scene=scene)


def main():
    opt = args()
    os.makedirs(GLB_DIR, exist_ok=True)
    os.makedirs(IMG_DIR, exist_ok=True)
    ships = ship_list()
    write_meta(ships)
    todo = [s for s in ships if opt["only"] is None or s[0] in opt["only"]]
    report = []
    for key, name, size, _ in todo:
        bs.reset()
        scene, cam = setup()
        t0 = time.time()
        ob = ship_models.build(key)
        scene.collection.objects.link(ob)
        tris = stationkit.tri_count(ob)
        glb = os.path.join(GLB_DIR, key + ".glb")
        bs.export_glb(ob, glb)
        if opt["render"]:
            render_two_views(scene, cam, ob, os.path.join(IMG_DIR, key + ".png"))
        report.append((key, name, size, tris, os.path.getsize(glb), time.time() - t0))
    print("\n=== SCHIFFE ===")
    for key, name, size, tris, sz, dt in report:
        print(f"{key:16s} {name:22s} {size} tris={tris:5d} glb={sz/1024:6.1f} KiB  {dt:5.1f}s")
    if opt["overview"] and (opt["only"] is None or opt["overview_only"]):
        overview(ships, os.path.join(IMG_DIR, "_overview.png"))
        print("Uebersicht geschrieben")


if __name__ == "__main__":
    main()
