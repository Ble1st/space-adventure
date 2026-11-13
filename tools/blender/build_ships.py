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
        out.append((key, names.get(key, name or key), size, idea))
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


def link(scene, ob):
    scene.collection.objects.link(ob)
    for c in ob.children:
        scene.collection.objects.link(c)


def write_meta(ships):
    """ships.json: Groesse, Massstab, Abbauart, drehbare Werkzeuge (Rotor-Nodes) und Spaltbacken."""
    models = {}
    for k, n, s, _ in ships:
        if k not in ship_models.ROTOR_INFO:
            ob = ship_models.build(k)
            for o in [ob] + list(ob.children):
                me = o.data
                bpy.data.objects.remove(o, do_unlink=True)
                bpy.data.meshes.remove(me)
        entry = {"name": n, "size": s, "scale": ship_models.SIZE_SCALE[s]}
        if k in ship_models.MINING:
            entry["mining"] = ship_models.MINING[k]
        info = ship_models.ROTOR_INFO.get(k) or {}
        if info.get("rotors"):
            entry["rotors"] = info["rotors"]
            entry["contactRadius"] = info["contactRadius"]
        if info.get("jaws"):
            entry["jaws"] = info["jaws"]
        models[k] = entry
    meta = {
        "axes": "Bug = -Z (Godot-Vorwaerts), oben = +Y, Steuerbord = +X; Laenge 2 (Halblaenge 1), Ursprung = Mitte der Bounding-Box",
        "nodes": "Rumpf = Node 'Hull'; drehende Werkzeuge = Kind-Nodes 'Rotor_*', Spaltbacken = 'Jaw_*'. "
                 "Bei allen ist die lokale Z-Achse die Drehachse und der Node-Ursprung liegt auf ihr "
                 "(spin beschreibt die Richtung im Schiff). contactRadius = Radius der Werkzeugfront "
                 "quer zur Flugrichtung, relativ zur Halblaenge 1.",
        "sizeScale": ship_models.SIZE_SCALE,
        "models": models,
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


PREVIEW_ACCENT = (0.80, 0.18, 0.08, 1.0)   # Beispiel-Fraktionsfarbe nur fuer die Bilder


def tint_accent(on):
    m = bpy.data.materials.get("Accent")
    if m is None:
        return
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    col = PREVIEW_ACCENT if on else (*stationkit.PALETTE["Accent"][0], 1.0)
    bsdf.inputs["Base Color"].default_value = col


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


GROUPS = ("Handel", "Bergbau/Bau", "Kampf")
SIZE_ORDER = {"S": 0, "M": 1, "L": 2, "XL": 3}


def overview(ships, path):
    """Uebersicht nach Rolle gruppiert (je eine Zeile), innerhalb nach Groesse und Kapazitaet sortiert,
    alle Schiffe im Massstab ihrer Groessenklasse."""
    bs.reset()
    scene, cam = setup(res=(3200, 2000))
    tint_accent(True)
    d = Vector((math.cos(math.radians(OV_EL)) * math.sin(math.radians(AZ)),
                -math.cos(math.radians(OV_EL)) * math.cos(math.radians(AZ)),
                math.sin(math.radians(OV_EL))))
    q = (-d).to_track_quat("-Z", "Y")
    right, up = q @ Vector((1, 0, 0)), q @ Vector((0, 1, 0))
    lab = bpy.data.materials.new("Label")
    bsdf = lab.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (0.9, 0.92, 1.0, 1)
    bsdf.inputs["Emission Color"].default_value = (0.85, 0.9, 1.0, 1)
    bsdf.inputs["Emission Strength"].default_value = 1.6

    def text(body, size, pos, align="CENTER"):
        cu = bpy.data.curves.new("t", "FONT")
        cu.body = body
        cu.align_x = align
        cu.size = size
        to = bpy.data.objects.new("t", cu)
        to.data.materials.append(lab)
        to.rotation_euler = q.to_euler()
        to.location = pos + d * 6.0
        scene.collection.objects.link(to)

    row_h = 9.0
    widest = 0.0
    rows = []
    for g in GROUPS:
        items = [s for s in ships if ship_models.ROLE.get(s[0]) == g]
        items.sort(key=lambda s: (SIZE_ORDER[s[2]], ship_models.CARGO.get(s[0], (0, 0))[0], s[0]))
        rows.append((g, items))
        w = sum(ship_models.SIZE_SCALE[s[2]] * 2.0 + 1.6 for s in items)
        widest = max(widest, w)
    for r, (g, items) in enumerate(rows):
        base = -up * (r * row_h)
        text(g, 0.9, base - right * (widest / 2) + up * 3.0, align="LEFT")
        x = -widest / 2
        for key, name, size, _ in items:
            k = ship_models.SIZE_SCALE[size]
            cw = k * 2.0 + 1.6
            pos = base + right * (x + cw / 2)
            ob = ship_models.build(key)
            link(scene, ob)
            ob.scale = (k, k, k)
            ob.location = pos + up * 0.4
            text(name, 0.42, pos - up * 2.6)
            text(f"{key} · {size}", 0.26, pos - up * 3.15)
            x += cw
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = widest + 1.0
    cam.location = -up * (row_h * (len(rows) - 1) / 2) + d * 120
    cam.rotation_euler = q.to_euler()
    cam.data.clip_end = 400
    scene.render.resolution_y = int(3200 * (row_h * len(rows) + 1.5) / (widest + 1.0))
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    tint_accent(False)
    im = bpy.data.images.load(path)
    scene.render.image_settings.file_format = "JPEG"
    scene.render.image_settings.quality = 88
    im.save_render(os.path.join(os.path.dirname(path), "overview.jpg"), scene=scene)


def main():
    opt = args()
    os.makedirs(GLB_DIR, exist_ok=True)
    os.makedirs(IMG_DIR, exist_ok=True)
    ships = ship_list()
    todo = [s for s in ships if opt["only"] is None or s[0] in opt["only"]]
    report = []
    for key, name, size, _ in todo:
        bs.reset()
        scene, cam = setup()
        t0 = time.time()
        ob = ship_models.build(key)
        link(scene, ob)
        tris = sum(stationkit.tri_count(o) for o in [ob] + list(ob.children))
        glb = os.path.join(GLB_DIR, key + ".glb")
        bs.export_glb(ob, glb)
        if opt["render"]:
            tint_accent(True)
            render_two_views(scene, cam, ob, os.path.join(IMG_DIR, key + ".png"))
            tint_accent(False)
        report.append((key, name, size, tris, os.path.getsize(glb), time.time() - t0))
    write_meta(ships)
    print("\n=== SCHIFFE ===")
    for key, name, size, tris, sz, dt in report:
        print(f"{key:16s} {name:22s} {size} tris={tris:5d} glb={sz/1024:6.1f} KiB  {dt:5.1f}s")
    if opt["overview"] and (opt["only"] is None or opt["overview_only"]):
        overview(ships, os.path.join(IMG_DIR, "_overview.png"))
        print("Uebersicht geschrieben")


if __name__ == "__main__":
    main()
