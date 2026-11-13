"""Baut alle Stationsmodelle, exportiert sie als GLB und rendert Vorschaubilder.

Aufruf (aus dem Repo-Wurzelverzeichnis):
    blender --background --factory-startup --python tools/blender/build_stations.py -- [--only k1,k2] [--no-render] [--no-overview] [--overview-only] [--view NAME] [--imgdir DIR]

Ausgaben:
    godot/assets/stations/<typeKey>.glb
    docs/stations/<typeKey>.png      (1024x768, Eevee)
    docs/stations/_overview.png      (Kontaktbogen mit deutschen Namen)
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
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))

import importlib  # noqa: E402
import stationkit  # noqa: E402
import station_models  # noqa: E402

importlib.reload(stationkit)
importlib.reload(station_models)

GLB_DIR = os.path.join(REPO, "godot", "assets", "stations")
IMG_DIR = os.path.join(REPO, "docs", "stations")
WORLD = os.path.join(REPO, "src", "SpaceSim.Core", "Content", "default-world.json")


def args():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    opt = {"only": None, "render": True, "overview": True, "view": "Khronos PBR Neutral", "imgdir": None}
    i = 0
    while i < len(a):
        if a[i] == "--only":
            opt["only"] = a[i + 1].split(",")
            i += 1
        elif a[i] == "--no-render":
            opt["render"] = False
        elif a[i] == "--view":
            opt["view"] = a[i + 1]
            i += 1
        elif a[i] == "--imgdir":
            opt["imgdir"] = a[i + 1]
            i += 1
        elif a[i] == "--overview-only":
            opt["only"] = []
            opt["overview_only"] = True
        elif a[i] == "--no-overview":
            opt["overview"] = False
        i += 1
    return opt


def station_types():
    with open(WORLD, encoding="utf-8") as f:
        d = json.load(f)
    return [(t["key"], t.get("name", t["key"])) for t in d["stationTypes"]]


# ---------------------------------------------------------------- Szene
def reset():
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.lights, bpy.data.cameras, bpy.data.curves):
        for x in list(coll):
            coll.remove(x)


def setup_world(scene):
    w = bpy.data.worlds.get("Space") or bpy.data.worlds.new("Space")
    scene.world = w
    w.use_nodes = True
    nt = w.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputWorld")
    # Kamerastrahlen: dunkler Verlauf + Sterne; Beleuchtung: schwaches Umgebungslicht
    lp = nt.nodes.new("ShaderNodeLightPath")
    mix = nt.nodes.new("ShaderNodeMixShader")
    amb = nt.nodes.new("ShaderNodeBackground")
    amb.inputs["Color"].default_value = (0.11, 0.12, 0.16, 1)
    amb.inputs["Strength"].default_value = 1.0
    tc = nt.nodes.new("ShaderNodeTexCoord")
    vor = nt.nodes.new("ShaderNodeTexVoronoi")
    vor.inputs["Scale"].default_value = 180.0
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = 0.0
    ramp.color_ramp.elements[0].color = (1, 1, 1, 1)
    ramp.color_ramp.elements[1].position = 0.035
    ramp.color_ramp.elements[1].color = (0, 0, 0, 1)
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    grad = nt.nodes.new("ShaderNodeMapRange")
    grad.inputs["From Min"].default_value = -1.0
    grad.inputs["From Max"].default_value = 1.0
    cr = nt.nodes.new("ShaderNodeValToRGB")
    cr.color_ramp.elements[0].color = (0.004, 0.005, 0.012, 1)
    cr.color_ramp.elements[1].color = (0.016, 0.020, 0.045, 1)
    add = nt.nodes.new("ShaderNodeMix")
    add.data_type = "RGBA"
    add.blend_type = "ADD"
    add.inputs["Factor"].default_value = 0.6
    bg = nt.nodes.new("ShaderNodeBackground")
    bg.inputs["Strength"].default_value = 1.0
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


def add_sun(name, energy, color, rot_deg, angle=2.0):
    ld = bpy.data.lights.new(name, "SUN")
    ld.energy = energy
    ld.color = color
    ld.angle = math.radians(angle)
    ob = bpy.data.objects.new(name, ld)
    ob.rotation_euler = tuple(math.radians(a) for a in rot_deg)
    bpy.context.scene.collection.objects.link(ob)
    return ob


VIEW = "Khronos PBR Neutral"


def setup_scene(res=(1024, 768)):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x, scene.render.resolution_y = res
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = False
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGB"
    try:
        scene.eevee.taa_render_samples = 48
    except AttributeError:
        pass
    try:
        scene.view_settings.view_transform = VIEW
        scene.view_settings.look = "AgX - Medium High Contrast" if VIEW == "AgX" else "None"
    except TypeError:
        pass
    setup_world(scene)
    add_sun("Key", 3.4, (1.0, 0.95, 0.88), (50, 0, -35))
    add_sun("Rim", 2.4, (0.55, 0.70, 1.0), (-60, 0, 150))
    add_sun("Fill", 0.8, (0.8, 0.85, 1.0), (75, 0, 120))
    cd = bpy.data.cameras.new("Cam")
    cd.lens = 50
    cam = bpy.data.objects.new("Cam", cd)
    scene.collection.objects.link(cam)
    scene.camera = cam
    return scene, cam


def frame(cam, ob, az=-40.0, el=26.0, fill=0.86):
    """3/4-Ansicht: Kamera so weit weg, dass alle Eckpunkte ins Bild passen (fill = Bildanteil)."""
    d = Vector((math.cos(math.radians(el)) * math.sin(math.radians(az)),
                -math.cos(math.radians(el)) * math.cos(math.radians(az)),
                math.sin(math.radians(el))))
    q = (-d).to_track_quat("-Z", "Y")
    right, up = q @ Vector((1, 0, 0)), q @ Vector((0, 1, 0))
    vs = [ob.matrix_world @ v.co for v in ob.data.vertices]
    xs = [v.dot(right) for v in vs]
    ys = [v.dot(up) for v in vs]
    center = right * (min(xs) + max(xs)) / 2 + up * (min(ys) + max(ys)) / 2
    sc = bpy.context.scene
    aspect = sc.render.resolution_x / sc.render.resolution_y
    th = math.tan(math.atan(18.0 / cam.data.lens)) * fill
    tv = th / aspect
    dist = 0.0
    for v in vs:
        r = v - center
        x, y, z = r.dot(right), r.dot(up), r.dot(d)
        dist = max(dist, z + abs(x) / th, z + abs(y) / tv)
    cam.location = center + d * dist
    cam.rotation_euler = q.to_euler()
    cam.data.clip_start = 0.01
    cam.data.clip_end = dist * 4


def export_glb(ob, path):
    for o in bpy.context.scene.objects:
        o.select_set(False)
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True,
                              export_yup=True, export_apply=True, export_cameras=False,
                              export_lights=False, export_materials="EXPORT")


# ---------------------------------------------------------------- Uebersicht
def overview(types, path):
    reset()
    scene, cam = setup_scene(res=(2400, 1800))
    cols = 7
    cw, ch = 3.3, 3.25
    az, el = -40.0, 26.0
    d = Vector((math.cos(math.radians(el)) * math.sin(math.radians(az)),
                -math.cos(math.radians(el)) * math.cos(math.radians(az)),
                math.sin(math.radians(el))))
    q = (-d).to_track_quat("-Z", "Y")
    right = q @ Vector((1, 0, 0))
    up = q @ Vector((0, 1, 0))
    rows = math.ceil(len(types) / cols)
    txt_mat = bpy.data.materials.new("Label")
    txt_mat.use_nodes = True
    b = txt_mat.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (0.9, 0.92, 1.0, 1)
    b.inputs["Emission Color"].default_value = (0.85, 0.9, 1.0, 1)
    b.inputs["Emission Strength"].default_value = 1.6
    for i, (key, name) in enumerate(types):
        r, c = divmod(i, cols)
        pos = right * ((c - (cols - 1) / 2) * cw) - up * ((r - (rows - 1) / 2) * ch)
        ob = station_models.build(key)
        bpy.context.scene.collection.objects.link(ob)
        ob.location = pos + up * 0.25
        for label, size, off in ((name, 0.30, -1.32), (key, 0.17, -1.62)):
            cu = bpy.data.curves.new("t_" + key, "FONT")
            cu.body = label
            cu.align_x = "CENTER"
            cu.size = size
            to = bpy.data.objects.new("t_" + key, cu)
            to.data.materials.append(txt_mat)
            to.rotation_euler = q.to_euler()
            to.location = pos + up * off + d * 1.5
            bpy.context.scene.collection.objects.link(to)
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = cols * cw + 0.4
    cam.location = d * 60
    cam.rotation_euler = q.to_euler()
    cam.data.clip_end = 200
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


# ---------------------------------------------------------------- Hauptablauf
def main():
    global VIEW, IMG_DIR
    opt = args()
    VIEW = opt["view"]
    if opt["imgdir"]:
        IMG_DIR = opt["imgdir"]
    os.makedirs(GLB_DIR, exist_ok=True)
    os.makedirs(IMG_DIR, exist_ok=True)
    types = station_types()
    missing = [k for k, _ in types if k not in station_models.MODELS]
    if missing:
        raise SystemExit("Kein Modell fuer: " + ", ".join(missing))
    todo = [t for t in types if opt["only"] is None or t[0] in opt["only"]]
    report = []
    for key, name in todo:
        reset()
        scene, cam = setup_scene()
        t0 = time.time()
        ob = station_models.build(key)
        scene.collection.objects.link(ob)
        tris = stationkit.tri_count(ob)
        glb = os.path.join(GLB_DIR, key + ".glb")
        export_glb(ob, glb)
        if opt["render"]:
            frame(cam, ob)
            scene.render.filepath = os.path.join(IMG_DIR, key + ".png")
            bpy.ops.render.render(write_still=True)
        report.append((key, name, tris, len(ob.data.materials), os.path.getsize(glb),
                       time.time() - t0))
    print("\n=== STATIONEN ===")
    for key, name, tris, nm, size, dt in report:
        print(f"{key:20s} {name:28s} tris={tris:5d} mats={nm} glb={size/1024:6.1f} KiB  {dt:5.1f}s")
    if opt["overview"] and (opt["only"] is None or opt.get("overview_only")):
        overview(types, os.path.join(IMG_DIR, "_overview.png"))
        print("Uebersicht geschrieben")


main()
