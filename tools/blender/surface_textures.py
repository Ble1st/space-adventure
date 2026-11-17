"""Gemeinsame Oberflaechen-Texturen fuer Schiffe, Stationen, Tore und Kleinstationen (eine Kachel fuer alle Modelle).

Erzeugt prozedural (numpy, fester Seed, nahtlos kachelbar) nach godot/assets/textures/:
  hull_detail.png  RGBA 1024^2: RGB = Helligkeits-Multiplikator (Plattentoene, Fugen, Nieten, Lueftungsgitter,
                   Schmutz, Laufspuren, abgeplatzte Kanten; fast grau, damit Materialfarbe und Fraktionsfarbe bleiben),
                   A = Rauheits-Faktor (Godot: roughness_texture, Kanal Alpha)
  hull_normal.png  RGB 1024^2: Normalenkarte (OpenGL, Y+) aus derselben Hoehe (Fugen vertieft, Nieten, Gitter)
  hull_lights.png  RGB 1024^2: Emissionsmaske (Fensterreihen, wenige Positionslichter; fast ueberall schwarz)
  panel_cells.png  RGB 256^2: Solarzellen-Raster fuer das Material "Panel" (Multiplikator)

Aufruf (Repo-Wurzel):
  blender --background --factory-startup --python tools/blender/surface_textures.py

Die Modelle tragen eine Kasten-Projektion als UV (stationkit.box_uv, 1 UV-Einheit = 1 Modelleinheit);
das Spiel (godot/scripts/World/SurfaceDetail.cs) setzt die Texturen und den Kachelmassstab je Modell.
"""
import os

import bpy
import numpy as np

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(REPO, "godot", "assets", "textures")
N = 1024
rng = np.random.default_rng(4401)


def fbm(n, beta, aniso=(1.0, 1.0), seed=0):
    """Nahtloses Rauschen (gefiltertes weisses Rauschen im Frequenzraum), normiert auf 0..1."""
    r = np.random.default_rng(seed)
    w = r.standard_normal((n, n))
    fy = np.fft.fftfreq(n)[:, None] * aniso[0]
    fx = np.fft.rfftfreq(n)[None, :] * aniso[1]
    f = np.sqrt(fx * fx + fy * fy)
    f[0, 0] = 1.0
    spec = np.fft.rfft2(w) / f ** beta
    spec[0, 0] = 0
    out = np.fft.irfft2(spec, s=(n, n))
    out -= out.min()
    return out / out.max()


def smooth(x, a, b):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)


# ---------------------------------------------------------------- Plattenaufteilung
def split(x, y, w, h, depth, out):
    unit = 32
    can_w = w >= 2 * unit * 2
    can_h = h >= 2 * unit * 2
    m = max(w, h)
    p = 1.0 if m > 192 else 0.8 if m > 128 else 0.45 if m > 64 else 0.0
    if (can_w or can_h) and rng.random() < p:
        along_x = can_w and (not can_h or w > h or (w == h and rng.random() < 0.5))
        if along_x:
            k = rng.integers(2, w // unit - 1)
            split(x, y, k * unit, h, depth + 1, out)
            split(x + k * unit, y, w - k * unit, h, depth + 1, out)
        else:
            k = rng.integers(2, h // unit - 1)
            split(x, y, w, k * unit, depth + 1, out)
            split(x, y + k * unit, w, h - k * unit, depth + 1, out)
    else:
        out.append((x, y, w, h))


def build():
    rects = []
    split(0, 0, N, N, 0, rects)

    tone = np.ones((N, N))
    rough = np.full((N, N), 0.86)
    height = np.zeros((N, N))
    dist = np.full((N, N), 99.0)          # Abstand zur naechsten Plattenkante (Pixel)
    lights = np.zeros((N, N, 3))
    window = np.zeros((N, N))              # Fensterschlitze (dunkles Glas)
    yy, xx = np.mgrid[0:N, 0:N]

    for (x, y, w, h) in rects:
        sl = (slice(y, y + h), slice(x, x + w))
        ly, lx = np.mgrid[0:h, 0:w]
        d = np.minimum(np.minimum(lx + 0.5, w - lx - 0.5), np.minimum(ly + 0.5, h - ly - 0.5))
        dist[sl] = d
        t = 1.0 + rng.normal(0, 0.035)
        if rng.random() < 0.08:
            t *= 0.84                       # ersetzte, dunklere Platte
        elif rng.random() < 0.06:
            t *= 1.05                       # frisch lackiert
        tone[sl] = t
        rough[sl] = np.clip(0.86 + rng.normal(0, 0.05), 0.7, 1.0)
        height[sl] += rng.uniform(0.0, 0.25)  # Platten leicht verschieden hoch
        big = w >= 96 and h >= 96
        kind = rng.random()
        if big and kind < 0.28:
            # Nietenreihen entlang der Kanten
            step = 16
            for px in range(8, w - 4, step):
                for py in (6, h - 7):
                    _bump(height, tone, x + px, y + py, 1.6, 0.35)
            for py in range(8 + step, h - 4 - step, step):
                for px in (6, w - 7):
                    _bump(height, tone, x + px, y + py, 1.6, 0.35)
        elif big and kind < 0.43:
            # Lueftungsgitter: Lamellen in einem eingelassenen Feld
            ix, iy = x + w // 4, y + h // 3
            iw, ih = w // 2, h // 3
            if iw >= 24 and ih >= 18:
                sub = (slice(iy, iy + ih), slice(ix, ix + iw))
                height[sub] -= 0.5
                tone[sub] *= 0.72
                ly2 = np.mgrid[0:ih, 0:iw][0]
                lam = (ly2 % 6) < 2
                height[sub] += np.where(lam, 0.35, 0.0)
                tone[sub] *= np.where(lam, 1.1, 0.85)
                rough[sub] = 0.95
        elif big and kind < 0.55:
            # Wartungsklappe: innere Fuge
            m = 12
            ld = np.minimum(np.minimum(lx - m, w - m - lx), np.minimum(ly - m, h - m - ly))
            groove = (np.abs(ld) < 1.2)
            height[sl] -= groove * 0.6
            tone[sl] *= np.where(groove, 0.62, 1.0)
        if w >= 96 and h >= 48 and rng.random() < 0.2:
            # Fensterreihe (nur Emission; unbeleuchtet unsichtbar)
            n = (w - 24) // 18
            row_y = y + h // 2 - 6
            warm = rng.random() < 0.7
            for i in range(n):
                cx = x + 14 + i * 18
                # Fenster immer als dunkle Glasschlitze in der Grundfarbe (auch auf Schiffen), mit Rahmenfuge
                window[row_y - 1:row_y + 13, cx - 1:cx + 10] = np.maximum(window[row_y - 1:row_y + 13, cx - 1:cx + 10], 0.5)
                window[row_y:row_y + 12, cx:cx + 9] = 1.0
                height[row_y:row_y + 12, cx:cx + 9] -= 0.4
                if rng.random() < 0.72:
                    c = (1.0, 0.72, 0.38) if warm else (0.55, 0.78, 1.0)
                    k = rng.uniform(0.6, 1.0)
                    lights[row_y:row_y + 12, cx:cx + 9] = [k * c[0], k * c[1], k * c[2]]
        if rng.random() < 0.03:
            # einzelnes Positions-/Warnlicht
            cx, cy = x + rng.integers(6, w - 6), y + rng.integers(6, h - 6)
            c = [(1.0, 0.08, 0.02), (0.1, 1.0, 0.25), (1.0, 1.0, 1.0)][rng.integers(0, 3)]
            lights[cy - 2:cy + 3, cx - 2:cx + 3] = c

    seam = 1.0 - smooth(dist, 0.6, 2.2)            # Fuge an jeder Plattenkante (je Seite ~1,5 px)
    bevel = 1.0 - smooth(dist, 2.2, 9.0)           # weicher Kantenschatten innerhalb der Platte
    height -= seam * 0.9

    grime = fbm(N, 2.0, seed=11)
    grime_fine = fbm(N, 1.0, seed=12)
    streak = fbm(N, 1.3, aniso=(0.12, 1.0), seed=13)  # gestreckt: Laufspuren
    wear_n = fbm(N, 0.9, seed=14)

    dirt = smooth(grime, 0.4, 0.95) * 0.26 + smooth(streak, 0.5, 1.0) * 0.12 + (grime_fine - 0.5) * 0.06
    edge = smooth(dist, 1.5, 3.0) * (1.0 - smooth(dist, 3.0, 7.0))   # Band neben der Fuge
    chip = edge * smooth(wear_n, 0.62, 0.72)                          # abgeplatzter Lack: heller, glatter

    lum = tone * (1.0 - dirt) * (1.0 - 0.55 * seam) * (1.0 - 0.07 * bevel) * (1.0 + 0.2 * chip)
    lum = lum * (1.0 - 0.8 * window)
    lum = np.clip(lum * 0.95, 0.0, 1.0)
    rough = np.clip(rough + dirt * 0.5 - chip * 0.25 + seam * 0.1 - window * 0.6, 0.2, 1.0)
    tint = np.stack([1.0 - dirt * 0.02, 1.0 - dirt * 0.10, 1.0 - dirt * 0.22], axis=-1)  # Schmutz leicht braun
    albedo = np.clip(lum[..., None] * tint, 0, 1)

    # Normalenkarte (Kachel-Ableitung mit Umlauf)
    hs = height + (grime_fine - 0.5) * 0.15
    dx = (np.roll(hs, -1, 1) - np.roll(hs, 1, 1)) * 0.5
    dy = (np.roll(hs, -1, 0) - np.roll(hs, 1, 0)) * 0.5
    s = 2.2
    nx, ny, nz = -dx * s, dy * s, np.ones_like(hs)   # Bildzeile 0 oben, OpenGL: +Y nach oben
    ln = np.sqrt(nx * nx + ny * ny + nz * nz)
    normal = np.stack([nx / ln, ny / ln, nz / ln], axis=-1) * 0.5 + 0.5
    return albedo, rough, normal, lights


def _bump(height, tone, cx, cy, r, h):
    y0, y1 = int(cy - r - 1), int(cy + r + 2)
    x0, x1 = int(cx - r - 1), int(cx + r + 2)
    ly, lx = np.mgrid[y0:y1, x0:x1]
    d = np.sqrt((lx - cx) ** 2 + (ly - cy) ** 2)
    b = np.clip(1 - d / (r + 0.5), 0, 1)
    height[y0:y1, x0:x1] += b * h
    tone[y0:y1, x0:x1] *= 1 - 0.12 * (b > 0) + 0.08 * b


def cells(n=256):
    """Solarzellen: 8 x 8 Zellen je Kachel, dunkle Stege, leichter Verlauf je Zelle."""
    yy, xx = np.mgrid[0:n, 0:n]
    c = n // 8
    lx, ly = xx % c, yy % c
    d = np.minimum(np.minimum(lx + 0.5, c - lx - 0.5), np.minimum(ly + 0.5, c - ly - 0.5))
    grid = smooth(d, 0.8, 2.0)
    bus = (np.abs((lx - c / 2)) < 0.8) * 0.25
    v = 0.45 + 0.55 * grid - bus
    sheen = 0.92 + 0.08 * (ly / c)
    lum = np.clip(v * sheen, 0, 1)
    return np.stack([lum, lum, lum], axis=-1)


def to_srgb(x):
    return np.where(x <= 0.0031308, 12.92 * x, 1.055 * np.power(np.clip(x, 0, 1), 1 / 2.4) - 0.055)


def save(name, rgba_top_down, srgb):
    """rgba: Bildzeile 0 = oben; Blender speichert Zeile 0 unten -> umdrehen."""
    a = rgba_top_down
    h, w = a.shape[:2]
    if a.shape[2] == 3:
        a = np.concatenate([a, np.ones((h, w, 1))], axis=-1)
    img = bpy.data.images.new(name, w, h, alpha=True, float_buffer=False)
    img.colorspace_settings.name = "Non-Color"   # Werte unveraendert schreiben (sRGB-Kodierung macht to_srgb)
    img.pixels.foreach_set(np.ascontiguousarray(a[::-1]).astype(np.float32).ravel())
    img.filepath_raw = os.path.join(OUT, name)
    img.file_format = "PNG"
    img.save()
    print("geschrieben", img.filepath_raw)


def main():
    os.makedirs(OUT, exist_ok=True)
    albedo, rough, normal, lights = build()
    detail = np.concatenate([to_srgb(albedo), rough[..., None]], axis=-1)
    save("hull_detail.png", detail, True)
    save("hull_normal.png", normal, False)
    save("hull_lights.png", to_srgb(lights), True)
    save("panel_cells.png", to_srgb(cells()), True)
    print(f"Mittel Helligkeit (linear) {albedo.mean():.3f}, Rauheit {rough.mean():.3f}, Lichtpixel {np.count_nonzero(lights.sum(-1)) / N / N * 100:.2f} %")


main()
