#!/usr/bin/env python3
"""
music_samples.py - prozedurale Musik-Samples fuer "Space Adventure"

Richtung: ruhig elektronisch mit epischen Hoehen (warme Pads, langsame Arpeggios,
tiefe Baesse, sparsame Melodie; an Hoehepunkten breite Akkorde, Chor-Pad, tiefe Trommeln).
Alle Stuecke und Melodien sind Eigenkompositionen; keine fremden Samples, keine Downloads.

Benutzung (aus dem Repo-Wurzelverzeichnis):
    python3 tools/audio/music_samples.py                # alle Dateien nach docs/music/
    python3 tools/audio/music_samples.py 01_hauptthema  # nur ein Stueck
    python3 tools/audio/music_samples.py --check        # nur vorhandene WAVs messen

Technik: reines Python (nur Standardbibliothek; numpy/scipy und Encoder wie ffmpeg/oggenc/
sox/lame sind auf dem Entwicklungsrechner nicht vorhanden), daher WAV 32 kHz / 16 bit /
stereo (haelt jede Datei unter 8 MB). Feste Seeds -> jeder Lauf erzeugt dieselben Bytes.
Bausteine: Supersaw-Pads (verstimmte Saegezahn-Oszillatoren, je Kanal anders verstimmt),
zweipoliger Tiefpass mit langsamer LFO-/Sweep-Modulation, Sinus-Sub-Bass, Pluck-Arpeggio,
Glocken (inharmonische Partialtoene), Chor-Pad (Saw + Formant-Resonatoren, Vibrato),
Kick/Toms (Sinus-Sweep), Rauschen (Snare/Hat/Becken/Riser), Schroeder/Freeverb-Hall
(6 Kammfilter + 2 Allpaesse je Kanal), Hochpass 25 Hz gegen DC, Lautheitsangleichung auf
einen Ziel-RMS je Stueck, weicher Limiter (Decke ca. -1 dBFS), Ein-/Ausblendung.
Die Messwerte (Dauer, Spitze, RMS, DC, Spektrum, Raender, Stereo) druckt das Skript am Ende.
"""
import array
import cmath
import math
import os
import random
import sys
import wave
from multiprocessing import Pool

SR = 32000
TWO_PI = 2 * math.pi
OUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "docs", "music")
CEIL = 0.89  # ~ -1 dBFS


def hz(m):
    return 440.0 * 2 ** ((m - 69) / 12.0)


# ------------------------------------------------------------------ Mix-Buffer
class Mix:
    def __init__(self, dur, seed, rev_g=0.88, rev_damp=0.35, rev_wet=1.0):
        self.n = int(dur * SR)
        self.L = [0.0] * self.n
        self.R = [0.0] * self.n
        self.SL = [0.0] * self.n
        self.SRr = [0.0] * self.n
        self.rng = random.Random(seed)
        self.rev = (rev_g, rev_damp, rev_wet)

    def put(self, sig, t, gl, gr, send):
        i0 = int(t * SR)
        if i0 < 0 or i0 >= self.n or not sig:
            return
        k = min(len(sig), self.n - i0)
        sig = sig[:k]
        for buf, g in ((self.L, gl), (self.R, gr), (self.SL, gl * send), (self.SRr, gr * send)):
            if g:
                buf[i0:i0 + k] = [a + b * g for a, b in zip(buf[i0:i0 + k], sig)]

    def put_st(self, l, r, t, g, send):
        i0 = int(t * SR)
        if i0 < 0 or i0 >= self.n:
            return
        k = min(len(l), self.n - i0)
        l = l[:k]
        r = r[:k]
        self.L[i0:i0 + k] = [a + b * g for a, b in zip(self.L[i0:i0 + k], l)]
        self.R[i0:i0 + k] = [a + b * g for a, b in zip(self.R[i0:i0 + k], r)]
        gs = g * send
        self.SL[i0:i0 + k] = [a + b * gs for a, b in zip(self.SL[i0:i0 + k], l)]
        self.SRr[i0:i0 + k] = [a + b * gs for a, b in zip(self.SRr[i0:i0 + k], r)]


def pan_gains(p):
    a = (p + 1) * math.pi / 4
    return math.cos(a), math.sin(a)


def envelope(n, dur, att, rel):
    na = max(1, int(att * SR))
    nd = int(dur * SR)
    nr = max(1, int(rel * SR))
    e = [math.sin(math.pi / 2 * i / na) ** 2 for i in range(min(na, n))]
    if nd > len(e):
        e += [1.0] * (min(nd, n) - len(e))
    e += [0.5 + 0.5 * math.cos(math.pi * i / nr) for i in range(nr)]
    e = e[:n]
    e += [0.0] * (n - len(e))
    return e


def lp_block(x, cut_fn, n, B=64, poles=2):
    """Tiefpass mit blockweise berechneter Grenzfrequenz cut_fn(t)."""
    out = [0.0] * n
    y1 = y2 = 0.0
    for b in range(0, n, B):
        fc = min(cut_fn(b / SR), SR * 0.45)
        a = 1 - math.exp(-TWO_PI * fc / SR)
        if poles == 2:
            for i in range(b, min(b + B, n)):
                y1 += a * (x[i] - y1)
                y2 += a * (y1 - y2)
                out[i] = y2
        else:
            for i in range(b, min(b + B, n)):
                y1 += a * (x[i] - y1)
                out[i] = y1
    return out


# ------------------------------------------------------------------ Instrumente
def pad(m, t, dur, notes, amp=0.2, cut=(600, 2500), att=1.0, rel=1.5, det=10, voices=3,
        lfo=(0.11, 0.25), send=0.5, sweep_len=None):
    n = int((dur + rel) * SR)
    i0 = int(t * SR)
    if i0 >= m.n:
        return
    n = min(n, m.n - i0)
    env = envelope(n, dur, att, rel)
    total = sweep_len if sweep_len else dur + rel
    chans = []
    for ch in (0, 1):
        acc = [0.0] * n
        cnt = 0
        for mi in notes:
            f = hz(mi)
            for v in range(voices):
                off = (v - (voices - 1) / 2.0) * det * (1.0 if ch == 0 else 0.78)
                off += (0.4 if ch else -0.4) * det * 0.3
                inc = f * 2 ** (off / 1200.0) / SR
                ph0 = m.rng.random()
                acc = [a + (ph0 + inc * i) % 1.0 for i, a in enumerate(acc)]
                cnt += 1
        k = 2.0 / math.sqrt(cnt)
        hc = cnt / 2.0
        x = [(a - hc) * k for a in acc]
        c0, c1 = cut
        lr, ld = lfo

        def cf(tt, ch=ch):
            fr = min(1.0, tt / total)
            return (c0 + (c1 - c0) * fr) * (1 + ld * math.sin(TWO_PI * lr * tt + ch * 1.7))

        y = lp_block(x, cf, n)
        chans.append([a * e for a, e in zip(y, env)])
    m.put_st(chans[0], chans[1], t, amp, send)


def sub(m, t, dur, midi, amp=0.3, att=0.06, rel=0.4, send=0.1, h2=0.25):
    n = int((dur + rel) * SR)
    w = TWO_PI * hz(midi) / SR
    env = envelope(n, dur, att, rel)
    sig = [e * (math.sin(w * i) + h2 * math.sin(2 * w * i)) for i, e in enumerate(env)]
    m.put(sig, t, amp * 0.42, amp * 0.42, send)


def pluck(m, t, midi, amp=0.2, pan=0.0, bright=4000, dec=0.6, send=0.4, echo=0.0, saw=0.6):
    f = hz(midi)
    n = int(dec * 3.5 * SR)
    inc1 = f * 2 ** (3 / 1200.0) / SR
    inc2 = f * 2 ** (-3 / 1200.0) / SR
    w = TWO_PI * f / SR
    x = [saw * 0.5 * ((2 * ((i * inc1) % 1.0) - 1) + (2 * ((i * inc2 + 0.3) % 1.0) - 1)) +
         (1 - saw) * math.sin(w * i) for i in range(n)]
    lo = min(f * 1.2, SR * 0.4)
    tau = 0.12

    def cf(tt):
        return lo + (bright - lo) * math.exp(-tt / tau)

    y = lp_block(x, cf, n, B=32)
    ad = 0.003 * SR
    kdec = 1.0 / (dec * SR)
    sig = [v * math.exp(-i * kdec) * (i / ad if i < ad else 1.0) for i, v in enumerate(y)]
    gl, gr = pan_gains(pan)
    m.put(sig, t, gl * amp, gr * amp, send)
    if echo:
        gl2, gr2 = pan_gains(-pan if pan else 0.5)
        m.put(sig, t + echo, gl2 * amp * 0.45, gr2 * amp * 0.45, send)


def lead(m, t, dur, midi, amp=0.15, pan=0.0, send=0.45, bright=3200, att=0.04, rel=0.5,
         vib=5.0, vdep=0.004):
    n = int((dur + rel) * SR)
    f = hz(midi)
    env = envelope(n, dur, att, rel)
    vk = f * vdep / (TWO_PI * vib)
    x = []
    for i in range(n):
        v = vk * math.sin(TWO_PI * vib * i / SR) * min(1.0, i / 12800.0)
        p = f * i / SR + v
        x.append(0.5 * ((2 * (p * 1.0024 % 1.0) - 1) + (2 * (p * 0.9976 % 1.0) - 1)) * 0.5 +
                 0.7 * math.sin(TWO_PI * p))
    y = lp_block(x, lambda tt: bright, n, B=128)
    gl, gr = pan_gains(pan)
    sig = [a * e for a, e in zip(y, env)]
    m.put(sig, t, gl * amp, gr * amp, send)


def bell(m, t, midi, amp=0.12, pan=0.0, dec=2.0, send=0.7, ratios=(1.0, 2.76, 5.4, 8.93), metallic=False):
    f = hz(midi)
    n = int(dec * 3.2 * SR)
    if metallic:
        ratios = (1.0, 1.59, 2.14, 2.66, 3.51, 4.7)
    sig = [0.0] * n
    for k, r in enumerate(ratios):
        fr = f * r
        if fr > SR * 0.42:
            continue
        w = TWO_PI * fr / SR
        kd = 1.0 / (dec * SR / (1 + 0.7 * k))
        a = 1.0 / (1 + 0.6 * k)
        sig = [s + a * math.exp(-i * kd) * math.sin(w * i) for i, s in enumerate(sig)]
    ad = 0.002 * SR
    sig = [s * (i / ad if i < ad else 1.0) for i, s in enumerate(sig)]
    gl, gr = pan_gains(pan)
    m.put(sig, t, gl * amp, gr * amp, send)


def kick(m, t, amp=0.6, f0=150.0, f1=44.0, dec=0.2, send=0.08):
    n = int(0.6 * SR)
    tau = 0.035
    sig = []
    for i in range(n):
        tt = i / SR
        ph = TWO_PI * (f1 * tt + (f0 - f1) * tau * (1 - math.exp(-tt / tau)))
        sig.append(math.sin(ph) * math.exp(-tt / dec))
    for i in range(int(0.003 * SR)):
        sig[i] += (m.rng.random() * 2 - 1) * 0.5
    m.put(sig, t, amp * 0.707, amp * 0.707, send)


def tom(m, t, midi, amp=0.5, dec=0.45, pan=0.0, send=0.5, drop=1.7):
    f1 = hz(midi)
    f0 = f1 * drop
    n = int(dec * 4 * SR)
    tau = 0.05
    sig = []
    for i in range(n):
        tt = i / SR
        ph = TWO_PI * (f1 * tt + (f0 - f1) * tau * (1 - math.exp(-tt / tau)))
        sig.append(math.sin(ph) * math.exp(-tt / dec))
    nb = int(0.05 * SR)
    prev = 0.0
    for i in range(nb):
        prev += 0.15 * ((m.rng.random() * 2 - 1) - prev)
        sig[i] += prev * 2.0 * (1 - i / nb)
    gl, gr = pan_gains(pan)
    m.put(sig, t, gl * amp, gr * amp, send)


def noise_hit(m, t, dur=0.2, amp=0.3, hp=0.3, pan=0.0, send=0.25, lpa=None):
    """Rauschen mit exponentiellem Abklingen; hp = Hochpass-Koeffizient (groesser = mehr Hoehen)."""
    n = int(dur * 4 * SR)
    r = m.rng.random
    lo = 0.0
    sig = []
    kd = 1.0 / (dur * SR)
    for i in range(n):
        x = r() * 2 - 1
        lo += hp * (x - lo)
        v = x - lo if lpa is None else lo
        sig.append(v * math.exp(-i * kd))
    gl, gr = pan_gains(pan)
    m.put(sig, t, gl * amp, gr * amp, send)


def riser(m, t, dur, amp=0.2, f0=300, f1=7000, send=0.5):
    n = int(dur * SR)
    chans = []
    for ch in (0, 1):
        r = m.rng.random
        x = [r() * 2 - 1 for _ in range(n)]
        lr = math.log(f1 / f0)
        y = lp_block(x, lambda tt: f0 * math.exp(lr * tt / dur), n, B=64)
        chans.append([v * (i / n) ** 2 for i, v in enumerate(y)])
    m.put_st(chans[0], chans[1], t, amp * 3.0, send)


def sweep(m, t, dur, f0, f1, amp=0.15, bright=2500, send=0.5, down=False):
    """Saege mit exponentiell gleitender Tonhoehe (Jump-/Warn-Schwung)."""
    n = int(dur * SR)
    lr = math.log(f1 / f0)
    ph = 0.0
    x = []
    for i in range(n):
        f = f0 * math.exp(lr * i / n)
        ph = (ph + f / SR) % 1.0
        x.append(2 * ph - 1)
    y = lp_block(x, lambda tt: bright * (0.3 + 0.7 * tt / dur), n)
    rel = int(0.15 * SR)
    sig = [v * (math.sin(math.pi / 2 * min(1.0, i / (0.1 * SR))) ** 2) * (1 if i < n - rel else (n - i) / rel)
           * (1.0 if down else (0.35 + 0.65 * i / n)) for i, v in enumerate(y)]
    m.put(sig, t, amp * 0.707, amp * 0.707, send)


VOWELS = {"ah": ((700, 1220, 2600), (80, 90, 120), (1.0, 0.5, 0.2)),
          "oo": ((330, 870, 2240), (70, 90, 120), (1.0, 0.35, 0.1)),
          "oh": ((500, 900, 2500), (70, 90, 120), (1.0, 0.5, 0.15))}


def choir(m, t, dur, notes, amp=0.2, att=2.0, rel=2.0, v0="oo", v1="ah", send=0.7, det=14):
    n = int((dur + rel) * SR)
    i0 = int(t * SR)
    if i0 >= m.n:
        return
    n = min(n, m.n - i0)
    env = envelope(n, dur, att, rel)
    chans = []
    fa, ba, ga = VOWELS[v0]
    fb, bb, gb = VOWELS[v1]
    for ch in (0, 1):
        acc = [0.0] * n
        cnt = 0
        for mi in notes:
            f = hz(mi)
            for v in range(3):
                off = (v - 1) * det * (1.0 if ch == 0 else 0.7) + (5 if ch else -5)
                finc = f * 2 ** (off / 1200.0) / SR
                ph0 = m.rng.random()
                vr = 4.6 + 0.4 * m.rng.random()
                vp = m.rng.random() * 6
                vk = 0.006 / (TWO_PI * vr) * f / SR * SR  # in Perioden
                acc = [a + (ph0 + finc * i + vk * math.sin(TWO_PI * vr * i / SR + vp) * min(1.0, i / 16000.0)) % 1.0
                       for i, a in enumerate(acc)]
                cnt += 1
        k = 2.0 / math.sqrt(cnt)
        hc = cnt / 2.0
        r = m.rng.random
        x = [(a - hc) * k + 0.1 * (r() * 2 - 1) for a in acc]
        out = [0.0] * n
        st = [[0.0, 0.0] for _ in range(3)]
        B = 256
        for b in range(0, n, B):
            u = min(1.0, (b / SR) / max(dur * 0.8, 0.1))
            u = 0.5 - 0.5 * math.cos(math.pi * u)
            coefs = []
            for j in range(3):
                fj = fa[j] + (fb[j] - fa[j]) * u
                bj = ba[j] + (bb[j] - ba[j]) * u
                gj = ga[j] + (gb[j] - ga[j]) * u
                rr = math.exp(-math.pi * bj / SR)
                coefs.append((2 * rr * math.cos(TWO_PI * fj / SR), -rr * rr, (1 - rr) * gj * 2.0))
            (a1, a2, g1), (b1, b2, g2), (c1, c2, g3) = coefs
            s1, s2 = st[0]
            t1, t2 = st[1]
            u1, u2 = st[2]
            for i in range(b, min(b + B, n)):
                xi = x[i]
                y1 = xi + a1 * s1 + a2 * s2
                s2 = s1
                s1 = y1
                y2 = xi + b1 * t1 + b2 * t2
                t2 = t1
                t1 = y2
                y3 = xi + c1 * u1 + c2 * u2
                u2 = u1
                u1 = y3
                out[i] = g1 * y1 + g2 * y2 + g3 * y3
            st[0] = [s1, s2]
            st[1] = [t1, t2]
            st[2] = [u1, u2]
        chans.append([a * e for a, e in zip(out, env)])
    m.put_st(chans[0], chans[1], t, amp, send)


# ------------------------------------------------------------------ Hall + Master
def comb(x, d, g, damp):
    n = len(x)
    w = [0.0] * n
    w[:d] = x[:d]
    f = 0.0
    for i in range(d, n):
        f = w[i - d] * (1 - damp) + f * damp
        w[i] = x[i] + g * f
    return w


def allpass(x, d, g):
    n = len(x)
    v = [0.0] * n
    y = [0.0] * n
    for i in range(n):
        vd = v[i - d] if i >= d else 0.0
        vi = x[i] + g * vd
        v[i] = vi
        y[i] = -g * vi + vd
    return y


def reverb(x, g, damp, side):
    delays_ms = [29.7, 37.1, 41.1, 43.7, 47.3, 51.7]
    acc = None
    for k, dm in enumerate(delays_ms):
        d = int((dm + side * (1.3 + 0.4 * k)) * SR / 1000)
        w = comb(x, d, g, damp)
        acc = w if acc is None else [a + b for a, b in zip(acc, w)]
    acc = [a * (1 - g) * 0.9 for a in acc]
    acc = allpass(acc, int((12.6 + side * 0.7) * SR / 1000), 0.5)
    acc = allpass(acc, int((7.7 + side * 0.5) * SR / 1000), 0.5)
    return acc


def master(m, rms_db, fade_in=0.4, fade_out=2.0):
    g, damp, wet = m.rev
    pre = int(0.022 * SR)
    sl = [0.0] * pre + m.SL[:-pre]
    sr = [0.0] * pre + m.SRr[:-pre]
    wl = reverb(sl, g, damp, 0)
    wr = reverb(sr, g, damp, 1)
    # leichte Hall-Hoehenbedaempfung durch das Damping; Wet addieren
    L = [a + b * wet for a, b in zip(m.L, wl)]
    R = [a + b * wet for a, b in zip(m.R, wr)]
    out = []
    for ch in (L, R):
        # Hochpass ~25 Hz gegen DC / Rumpeln
        a = 1 - math.exp(-TWO_PI * 25 / SR)
        lo = 0.0
        y = [0.0] * len(ch)
        for i, v in enumerate(ch):
            lo += a * (v - lo)
            y[i] = v - lo
        out.append(y)
    L, R = out
    n = len(L)
    fi = int(fade_in * SR)
    fo = int(fade_out * SR)
    for i in range(fi):
        w = 0.5 - 0.5 * math.cos(math.pi * i / fi)
        L[i] *= w
        R[i] *= w
    for i in range(fo):
        w = 0.5 + 0.5 * math.cos(math.pi * i / fo)
        L[n - fo + i] *= w
        R[n - fo + i] *= w
    # Lautheit: Ziel-RMS ueber den Teil oberhalb -50 dB Stille
    s = sum(a * a + b * b for a, b in zip(L, R)) / (2 * n)
    gain = 10 ** (rms_db / 20.0) / math.sqrt(s)
    knee = 0.6
    span = CEIL - knee
    res = array.array("h")
    for a, b in zip(L, R):
        for v in (a * gain, b * gain):
            av = abs(v)
            if av > knee:
                av = knee + span * math.tanh((av - knee) / span)
                v = av if v > 0 else -av
            res.append(int(round(v * 32767)))
    return res


def write_wav(path, data):
    with wave.open(path, "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SR)
        if sys.byteorder == "big":
            data = array.array("h", data)
            data.byteswap()
        w.writeframes(data.tobytes())


# ------------------------------------------------------------------ Harmonik
CH = {  # name: (bass, pad, arp)
    "Dm": (38, [50, 57, 60, 64], [62, 65, 69, 72, 76]),
    "Bb": (34, [46, 53, 58, 62, 65], [58, 62, 65, 70, 74]),
    "F": (41, [53, 57, 60, 64], [57, 60, 65, 69, 72]),
    "C": (36, [48, 55, 60, 64, 67], [60, 64, 67, 72, 76]),
    "Gm": (43, [43, 50, 55, 58, 62], [58, 62, 67, 70, 74]),
    "A": (33, [45, 52, 57, 61, 64], [57, 61, 64, 69, 73]),
    "Am": (33, [45, 52, 57, 60, 64], [57, 60, 64, 69, 72]),
    "G": (31, [43, 50, 55, 59, 62], [55, 59, 62, 67, 71]),
    "Am_": (45, [57, 60, 64, 67], [57, 60, 64, 69, 72]),
}
ARP_PAT = [0, 1, 2, 3, 4, 3, 2, 1]


def arp(m, t0, spb, chord, nbeats, amp, pan_w=0.6, bright=3500, dec=0.55, shift=0, pat=ARP_PAT, step=0.5):
    notes = CH[chord][2]
    steps = int(nbeats / step)
    for i in range(steps):
        p = pat[i % len(pat)]
        pan = math.sin(i * 0.9) * pan_w
        pluck(m, t0 + i * step * spb, notes[p] + shift, amp=amp, pan=pan, bright=bright, dec=dec,
              send=0.45, echo=spb * 0.75)


def melody(m, t0, spb, notes, shift=0, **kw):
    for b, mi, ln in notes:
        lead(m, t0 + b * spb, ln * spb * 0.97, mi + shift, **kw)


# ------------------------------------------------------------------ Stuecke
THEME = {
    "Dm": [(0, 69, 3), (3, 72, 1), (4, 74, 2), (6, 72, 2)],
    "Bb": [(0, 70, 3), (3, 74, 1), (4, 72, 2), (6, 70, 2)],
    "F": [(0, 72, 3), (3, 69, 1), (4, 72, 2), (6, 77, 2)],
    "C": [(0, 76, 2), (2, 74, 1), (3, 72, 1), (4, 67, 4)],
}


def p01(m):
    bpm = 84
    spb = 60.0 / bpm
    bar = 4 * spb
    slots = ["Dm", "Bb", "Dm", "Bb", "F", "C", "Dm"]
    for s, c in enumerate(slots):
        t = s * 2 * bar
        bass, padn, _ = CH[c]
        final = s == 6
        epic = s in (4, 5)
        dur = 2 * bar if not final else 2.2 * bar
        cut = (500, 1500) if s < 2 else ((900, 2800) if s < 4 else ((1400, 6000) if epic else (900, 2000)))
        pad(m, t, dur, padn, amp=0.16 + 0.04 * (s >= 2), cut=cut, att=1.6, rel=2.0, send=0.5)
        sub(m, t, dur, bass, amp=0.28 if s else 0.22)
        if s >= 1 and not final:
            arp(m, t, spb, c, 8, amp=0.07 + 0.015 * min(s, 4), bright=2500 + 600 * s)
        if s >= 2 and not final:
            mel = THEME[c]
            vol = 0.11 if s == 2 or s == 3 else 0.13
            melody(m, t, spb, mel, shift=0, amp=vol, bright=2800)
            if epic:
                melody(m, t, spb, mel, shift=12, amp=0.09, bright=4500, send=0.6)
        if epic:
            wide = [n - 12 for n in padn[:2]] + [n + 12 for n in padn[1:4]]
            pad(m, t, dur, wide, amp=0.12, cut=(1800, 7000), att=0.8, rel=2.0, voices=4, det=14, send=0.6)
            choir(m, t, dur, [n + 12 for n in padn[1:4]], amp=0.1, att=1.2, rel=1.5)
            for b in range(2):
                tb = t + b * bar
                tom(m, tb, 38, amp=0.45, dec=0.55, send=0.55)
                tom(m, tb + 2.5 * spb, 43, amp=0.3, dec=0.4, pan=0.3, send=0.5)
                tom(m, tb + 3 * spb, 41, amp=0.3, dec=0.4, pan=-0.3, send=0.5)
            noise_hit(m, t, dur=1.4, amp=0.05, hp=0.5, send=0.5)
        if s == 3:
            riser(m, t + bar, bar, amp=0.05)
        if final:
            lead(m, t, 2.0 * bar, 69, amp=0.1, bright=2400, att=0.2, rel=2.0)
            choir(m, t, dur, [57, 62, 65, 69], amp=0.08, att=1.0, rel=2.5, v0="ah", v1="oo")
            tom(m, t, 38, amp=0.5, dec=0.7)


def p02(m):
    spb = 1.0
    slots = [("F", [53, 57, 60, 64, 67]), ("Am7", [57, 60, 64, 67, 71]), ("Bbl", [58, 62, 65, 69, 76]),
             ("G", [55, 59, 62, 67, 69]), ("F", [53, 57, 60, 64, 67])]
    basses = [41, 45, 46, 43, 41]
    for s, (c, notes) in enumerate(slots):
        t = s * 8.0
        pad(m, t, 8.0, notes, amp=0.2, cut=(450, 1300), att=3.0, rel=3.5, det=9, lfo=(0.07, 0.3), send=0.55)
        sub(m, t, 8.0, basses[s] - 5, amp=0.17, att=1.5, rel=2.0, h2=0.15)
        for b in range(4):  # langsamer Puls alle 2 Schlaege, sehr leise
            tt = t + b * 2.0
            sub(m, tt, 0.9, basses[s] + 7, amp=0.05 + 0.02 * (b % 2 == 0), att=0.25, rel=1.0, send=0.4, h2=0.0)
    for k, (tt, mi) in enumerate([(5, 81), (13.5, 84), (19, 79), (27, 81), (33, 76)]):
        pluck(m, tt, mi, amp=0.05, pan=(-0.5, 0.4, -0.2, 0.5, 0)[k], bright=2500, dec=1.1, send=0.8, echo=1.0, saw=0.2)


def p03(m):
    pad(m, 0, 36, [26, 33], amp=0.28, cut=(110, 330), att=4, rel=5, det=9, lfo=(0.05, 0.5), send=0.35)
    pad(m, 3, 32, [38], amp=0.1, cut=(150, 450), att=6, rel=5, det=14, lfo=(0.037, 0.4), send=0.4)
    sub(m, 0, 36, 26, amp=0.2, att=3, rel=4, h2=0.1)
    pad(m, 5, 30, [74, 81, 86], amp=0.16, cut=(1000, 1800), att=5, rel=5, det=22, lfo=(0.09, 0.6), send=0.7)
    pad(m, 14, 20, [77, 80], amp=0.1, cut=(900, 1500), att=5, rel=5, det=26, lfo=(0.06, 0.6), send=0.8)
    r = m.rng
    times = sorted(r.uniform(2.0, 33.0) for _ in range(9))
    for i, tt in enumerate(times):
        mi = r.choice([62, 65, 68, 74, 77, 80, 86])
        bell(m, tt, mi, amp=0.1 + 0.04 * r.random(), pan=r.uniform(-0.9, 0.9), dec=r.uniform(1.4, 3.0),
             send=0.85, metallic=True)
    for tt in (9.0, 21.5, 29.0):
        noise_hit(m, tt, dur=0.5, amp=0.04, hp=0.7, pan=r.uniform(-0.8, 0.8), send=0.7)
    tom(m, 17.0, 31, amp=0.18, dec=0.9, send=0.8, drop=1.2)
    tom(m, 31.0, 29, amp=0.16, dec=1.0, send=0.8, drop=1.2)


def p04(m):
    bpm = 72
    spb = 60.0 / bpm
    bar = 4 * spb
    prog = ["Am", "F", "C", "G"]
    nb = 12
    pat = [0, 2, 1, 3, 2, 4, 3, 1]
    for b in range(nb):
        c = prog[b % 4]
        t = b * bar
        bass, padn, _ = CH[c]
        fr = b / nb
        pad(m, t, bar, padn, amp=0.14, cut=(600 + 500 * fr, 1200 + 1500 * fr), att=1.0, rel=1.5, send=0.5)
        sub(m, t, bar, bass, amp=0.22)
        arp(m, t, spb, c, 4, amp=0.10 + 0.03 * math.sin(fr * 3.14), bright=1800 + 1800 * fr, dec=0.5, pat=pat, step=0.5)
        for beat in range(4):  # weicher Puls
            sub(m, t + beat * spb, 0.25, bass + 12, amp=0.06 if beat else 0.1, att=0.01, rel=0.2, send=0.2, h2=0.0)
    for b in (4, 8):
        for k in range(4):
            pluck(m, b * bar + k * 2 * spb + 0.5 * spb, [76, 72, 79, 76][k], amp=0.05, pan=0.5 * (-1) ** k,
                  bright=3000, dec=1.2, saw=0.2, send=0.8, echo=spb * 1.5)


def p05(m):
    dur = 35.0
    tt = 0.5
    bpm = 62.0
    k = 0
    while tt < dur:
        fr = tt / dur
        bpm = 62 + 60 * fr ** 1.5
        a = 0.3 + 0.2 * fr
        sub(m, tt, 0.28, 33 + (0 if k % 2 == 0 else -2), amp=a * 0.6, att=0.01, rel=0.3, send=0.15, h2=0.5)
        tom(m, tt, 28, amp=0.18 + 0.2 * fr, dec=0.3, send=0.3, drop=1.5)
        tt += 60.0 / bpm
        k += 1
    pad(m, 0, 34, [40, 47, 52, 55, 59], amp=0.3, cut=(180, 4200), att=8, rel=1.5, det=12, lfo=(0.08, 0.2),
        send=0.5, sweep_len=34)
    pad(m, 14, 20, [41, 53, 65], amp=0.18, cut=(300, 3800), att=8, rel=1.5, det=18, lfo=(0.13, 0.2), send=0.5,
        sweep_len=20)
    pad(m, 24, 10.5, [76, 83, 88], amp=0.14, cut=(1500, 6000), att=4, rel=1.5, det=24, send=0.7, sweep_len=10)
    sweep(m, 18, 16, 110, 880, amp=0.09, bright=2600)
    riser(m, 22, 12, amp=0.07)
    for i in range(5):
        bell(m, 8 + i * 5.5, [76, 71, 77, 70, 76][i], amp=0.04 + 0.01 * i, pan=(-1) ** i * 0.5, dec=1.8, send=0.8,
             metallic=True)
    tom(m, 34.2, 31, amp=0.5, dec=0.9, send=0.6)


def p06(m):
    bpm = 120
    spb = 60.0 / bpm
    bar = 4 * spb
    prog = ["Dm", "Bb", "C", "Dm"]
    nbar = 18
    for b in range(nbar):
        t = b * bar
        c = prog[(b // 2) % 4]
        bass, padn, _ = CH[c]
        outro = b >= 16
        intro = b < 2
        pad(m, t, bar, padn, amp=0.2, cut=(500, 2400), att=0.2, rel=0.8, send=0.35)
        sub(m, t, bar, bass, amp=0.2, att=0.02, rel=0.3)
        if not outro:
            for i in range(8):  # 8tel-Bass
                mi = bass + 24 + (12 if i % 4 == 3 else 0)
                pluck(m, t + i * 0.5 * spb, mi, amp=0.16, bright=900, dec=0.22, send=0.1, saw=0.8)
        if intro or outro:
            if intro:
                for i in range(8):
                    noise_hit(m, t + i * 0.5 * spb, dur=0.04, amp=0.04, hp=0.8, send=0.1)
            if outro and b == 16:
                tom(m, t, 38, amp=0.45, dec=0.6)
                tom(m, t + 2 * spb, 38, amp=0.3, dec=0.6)
            continue
        for beat in (0, 2):
            kick(m, t + beat * spb, amp=0.55)
        if b % 2 == 1:
            kick(m, t + 3.5 * spb, amp=0.4)
        for beat in (1, 3):
            noise_hit(m, t + beat * spb, dur=0.12, amp=0.22, hp=0.35, pan=0.1, send=0.3)
            tom(m, t + beat * spb, 60, amp=0.12, dec=0.12, send=0.2, drop=1.5)
        for i in range(8):
            noise_hit(m, t + i * 0.5 * spb, dur=0.035, amp=0.07 + (0.04 if i % 2 else 0), hp=0.85,
                      pan=0.35 * (-1) ** i, send=0.1)
        if b >= 4:
            arp(m, t, spb, c, 4, amp=0.15, pan_w=0.7, bright=5000, dec=0.18, shift=12, pat=[0, 2, 4, 2, 3, 1, 4, 2],
                step=0.25)
        if b % 4 == 3:  # Tom-Fill
            for k, mi in enumerate([52, 50, 47, 45]):
                tom(m, t + (3 + k * 0.25) * spb, mi, amp=0.35, dec=0.25, pan=0.5 - 0.3 * k, send=0.35)
        if b % 4 == 0 and b >= 4:
            noise_hit(m, t, dur=0.9, amp=0.06, hp=0.6, send=0.5)
        if b in (7, 15):
            riser(m, t, bar, amp=0.04)
    for b0 in (8, 12):
        pad(m, b0 * bar, 4 * bar, [62, 69, 74, 77], amp=0.06, cut=(1500, 5000), att=1, rel=0.8, send=0.4)


def p07(m):
    bpm = 72
    spb = 60.0 / bpm
    bar = 4 * spb
    slots = ["Dm", "Bb", "Gm", "A", "Dm", "Dm"]
    mel = {
        "Dm": [(0, 69, 2), (2, 74, 2), (4, 72, 1), (5, 70, 1), (6, 69, 2)],
        "Bb": [(0, 70, 2), (2, 74, 2), (4, 77, 2), (6, 74, 2)],
        "Gm": [(0, 67, 2), (2, 70, 2), (4, 74, 2), (6, 72, 1), (7, 70, 1)],
        "A": [(0, 69, 2), (2, 73, 2), (4, 76, 2), (6, 73, 2)],
    }
    for s, c in enumerate(slots):
        t = s * 2 * bar
        bass, padn, _ = CH[c]
        final = s == 5
        dur = 2 * bar if not final else 2.3 * bar
        wide = [padn[0] - 12] + padn + [n + 12 for n in padn[1:4]] + [padn[-1] + 24]
        pad(m, t, dur, wide, amp=0.2, cut=(1000 + 200 * s, 3500 + 500 * s), att=0.9, rel=2.2, voices=4, det=15,
            send=0.55)
        choir(m, t, dur, [n + 12 for n in padn[1:5]], amp=0.11, att=1.0, rel=2.0, v0="ah", v1="oh" if s % 2 else "oo")
        sub(m, t, dur, bass - (12 if bass > 40 else 0) + 12 * (bass < 33), amp=0.34, att=0.04, rel=0.8)
        for b in range(2):
            tb = t + b * bar
            tom(m, tb, 36, amp=0.7, dec=0.7, send=0.5)
            tom(m, tb + spb, 40, amp=0.4, dec=0.5, pan=0.25, send=0.5)
            tom(m, tb + 2.5 * spb, 36, amp=0.6, dec=0.6, send=0.5)
            tom(m, tb + 3 * spb, 43, amp=0.35, dec=0.4, pan=-0.3, send=0.5)
            if b == 1 and s >= 2:
                for k in range(6):
                    tom(m, tb + (3 + k / 8.0) * spb, 45 - k, amp=0.2 + 0.03 * k, dec=0.25, pan=0.5 - 0.2 * k, send=0.5)
            kick(m, tb, amp=0.4, f0=110, f1=38, dec=0.3, send=0.2)
        noise_hit(m, t, dur=1.6, amp=0.08, hp=0.55, send=0.6)
        if c in mel and not final:
            melody(m, t, spb, mel[c], shift=-12, amp=0.12, bright=1800, send=0.5)
            melody(m, t, spb, mel[c], shift=0, amp=0.1, bright=3500, send=0.55)
        if s == 4:
            melody(m, t, spb, [(0, 74, 4), (4, 77, 2), (6, 81, 2)], shift=0, amp=0.1, bright=5000)
        if final:
            pad(m, t, dur, [62, 69, 74, 77, 81], amp=0.1, cut=(2000, 7000), att=0.5, rel=2.5, voices=4, det=16)
            tom(m, t + 0.0, 33, amp=0.8, dec=1.1)
            noise_hit(m, t, dur=2.2, amp=0.1, hp=0.5, send=0.7)
    for s in (3, 4):  # Riser vor den Akkordwechseln
        riser(m, s * 2 * bar - bar, bar, amp=0.04)


def p08(m):
    r = m.rng

    def clock(t0, dec=0.6):
        pass

    # 1) Sieg / Kopfgeld: heller Dur-Akkord, aufsteigendes Arpeggio
    t = 0.3
    for i, mi in enumerate([62, 66, 69, 74, 78, 81]):
        pluck(m, t + i * 0.11, mi, amp=0.12, pan=(-0.5 + i * 0.2), bright=7000, dec=0.9, send=0.5, saw=0.5)
    pad(m, t + 0.5, 2.0, [50, 57, 62, 66, 69, 74, 76], amp=0.3, cut=(2500, 6500), att=0.12, rel=1.8, send=0.6)
    bell(m, t + 0.65, 86, amp=0.1, dec=2.0)
    bell(m, t + 0.75, 93, amp=0.06, pan=0.4, dec=1.5)
    sub(m, t + 0.5, 1.5, 38, amp=0.2)
    # 2) Schiffsverlust: fallendes Motiv, dunkel
    t = 7.6
    for i, mi in enumerate([76, 74, 72, 69, 64]):
        lead(m, t + i * 0.42, 0.55, mi, amp=0.14, bright=1800 - 150 * i, att=0.03, rel=0.6, send=0.6, vdep=0.008)
    pad(m, t + 0.4, 3.0, [40, 47, 52, 55, 59], amp=0.16, cut=(900, 300), att=0.3, rel=2.2, det=22, sweep_len=3.0)
    sweep(m, t + 1.8, 2.0, 160, 38, amp=0.16, bright=700, down=True)
    tom(m, t + 2.4, 31, amp=0.6, dec=0.9, send=0.5)
    # 3) Sprung durchs Jumpgate: aufsteigender Schwung, helle Landung
    t = 15.2
    sweep(m, t, 1.6, 120, 2400, amp=0.15, bright=5500)
    riser(m, t, 1.6, amp=0.09, f0=400, f1=9000)
    pad(m, t + 1.55, 2.0, [57, 64, 69, 73, 76, 81], amp=0.17, cut=(3500, 7000), att=0.05, rel=1.8, det=18, send=0.7)
    for i, mi in enumerate([93, 88, 100]):
        bell(m, t + 1.6 + i * 0.12, mi - 12 if mi > 95 else mi, amp=0.06, pan=(-0.6, 0.6, 0)[i], dec=1.5, send=0.8)
    # 4) Krieg erklaert: tiefe Hoerner (kleine Sekunde/Tritonus) + Trommelschlaege
    t = 22.6
    pad(m, t, 3.6, [34, 41, 46, 47, 53], amp=0.25, cut=(300, 1100), att=0.5, rel=1.8, det=20, send=0.5,
        sweep_len=3.6)
    sub(m, t, 3.6, 29, amp=0.3, att=0.2)
    for i, (dt, mi, a) in enumerate([(0.0, 33, 0.8), (1.0, 33, 0.8), (2.0, 31, 0.9), (2.5, 36, 0.5)]):
        tom(m, t + dt, mi, amp=a, dec=0.8, send=0.55, pan=0.2 * (-1) ** i)
    kick(m, t + 2.0, amp=0.4, f0=100, f1=36, dec=0.4)
    noise_hit(m, t + 2.0, dur=1.2, amp=0.07, hp=0.5, send=0.6)
    # 5) Frieden: warmer lydischer Akkord, aufsteigendes Dreiton-Motiv
    t = 29.8
    pad(m, t, 3.3, [53, 57, 60, 64, 69, 76], amp=0.32, cut=(600, 3200), att=1.0, rel=2.6, det=9, send=0.6,
        sweep_len=2.5)
    sub(m, t, 3.0, 41, amp=0.2, att=0.4)
    for i, mi in enumerate([72, 76, 81]):
        lead(m, t + 0.3 + i * 0.5, 0.9, mi, amp=0.13, bright=3200, att=0.05, rel=1.2, send=0.6)
    bell(m, t + 1.9, 88, amp=0.07, dec=2.5)


PIECES = {
    "00_schichten_demo": (56.0, 3.5, 91, -18.0, (0.90, 0.32, 1.0)),
    "01_hauptthema": (44.0, 1, 11, -17.5, (0.90, 0.34, 1.0)),
    "02_flug_ruhig": (43.0, 1, 12, -21.0, (0.92, 0.40, 1.0)),
    "03_grenzgebiet": (42.0, 1, 13, -21.0, (0.93, 0.30, 1.0)),
    "04_bergbau": (40.0 + 2.0, 1, 14, -19.5, (0.88, 0.35, 1.0)),
    "05_warnung": (39.0, 1, 15, -19.0, (0.88, 0.35, 1.0)),
    "06_gefecht": (36.0 + 3.5, 1, 16, -17.5, (0.82, 0.38, 0.9)),
    "07_belagerung_episch": (41.0 + 3.0, 1, 17, -16.5, (0.90, 0.34, 1.0)),
    "08_signale": (37.0, 1, 18, -16.0, (0.88, 0.32, 1.0)),
}


# ------------------------------------------------------------------ Demo (adaptive Schichten)
def p00(m):
    bpm = 80
    spb = 60.0 / bpm
    bar = 4 * spb
    prog = ["Am_", "F", "C", "G"]
    # Schichten nach Takt: 0-2 Flaeche, 3-5 +Puls, 6-8 +Melodie, 9-10 +Spannung, 11-14 +Kampf, 15-17 nur Flaeche
    mel = {
        "Am_": [(0, 69, 2), (2, 72, 1), (3, 76, 1)],
        "F": [(0, 77, 2), (2, 76, 1), (3, 72, 1)],
        "C": [(0, 72, 2), (2, 76, 1), (3, 79, 1)],
        "G": [(0, 74, 3), (3, 71, 1)],
    }
    for b in range(18):
        c = prog[b % 4]
        t = b * bar
        bass, padn, _ = CH[c]
        pad(m, t, bar, padn, amp=0.17, cut=(600, 2000), att=0.9, rel=1.4, send=0.5)
        sub(m, t, bar, bass if bass < 40 else bass - 12, amp=0.22)
        if 3 <= b <= 14:
            arp(m, t, spb, "Am" if c == "Am_" else c, 4, amp=0.08, bright=2800, dec=0.5)
            for beat in range(4):
                sub(m, t + beat * spb, 0.2, bass + 12, amp=0.06, att=0.01, rel=0.15, send=0.2, h2=0.0)
        if 6 <= b <= 14:
            melody(m, t, spb, mel[c], amp=0.11, bright=3200)
        if 9 <= b <= 10:
            pad(m, t, bar, [n + 1 if i == 0 else n for i, n in enumerate([70, 76, 82])], amp=0.08, cut=(600, 4000),
                att=0.8, rel=1.0, det=22, send=0.5)
            tom(m, t, 33, amp=0.3, dec=0.5)
            tom(m, t + 2 * spb, 33, amp=0.25, dec=0.5)
            if b == 10:
                riser(m, t, bar, amp=0.05)
        if 11 <= b <= 14:
            for beat in (0, 2):
                kick(m, t + beat * spb, amp=0.5)
            for beat in (1, 3):
                noise_hit(m, t + beat * spb, dur=0.12, amp=0.2, hp=0.35, send=0.3)
            for i in range(8):
                noise_hit(m, t + i * 0.5 * spb, dur=0.035, amp=0.06, hp=0.85, pan=0.3 * (-1) ** i, send=0.1)
                pluck(m, t + i * 0.5 * spb, bass + 24, amp=0.14, bright=900, dec=0.2, send=0.1, saw=0.8)
            if b == 14:
                for k, mi in enumerate([52, 50, 47, 45]):
                    tom(m, t + (3 + k * 0.25) * spb, mi, amp=0.35, dec=0.3, send=0.4)
    tom(m, 15 * bar, 36, amp=0.5, dec=0.9)


BUILD = {"00_schichten_demo": p00, "01_hauptthema": p01, "02_flug_ruhig": p02, "03_grenzgebiet": p03,
         "04_bergbau": p04, "05_warnung": p05, "06_gefecht": p06, "07_belagerung_episch": p07,
         "08_signale": p08}


def render(name):
    dur, _, seed, rms, rev = PIECES[name]
    m = Mix(dur, seed, *rev)
    BUILD[name](m)
    data = master(m, rms)
    os.makedirs(OUT_DIR, exist_ok=True)
    path = os.path.join(OUT_DIR, name + ".wav")
    write_wav(path, data)
    return name


# ------------------------------------------------------------------ Messung
def fft(a):
    n = len(a)
    j = 0
    for i in range(1, n):
        bit = n >> 1
        while j & bit:
            j ^= bit
            bit >>= 1
        j ^= bit
        if i < j:
            a[i], a[j] = a[j], a[i]
    ln = 2
    while ln <= n:
        w = cmath.exp(-2j * math.pi / ln)
        for i in range(0, n, ln):
            wn = 1
            for k in range(i, i + ln // 2):
                u = a[k]
                v = a[k + ln // 2] * wn
                a[k] = u + v
                a[k + ln // 2] = u - v
                wn *= w
        ln <<= 1
    return a


def analyze(path):
    with wave.open(path, "rb") as w:
        n = w.getnframes()
        raw = array.array("h")
        raw.frombytes(w.readframes(n))
        sr = w.getframerate()
    L = [v / 32768.0 for v in raw[0::2]]
    R = [v / 32768.0 for v in raw[1::2]]
    peak = max(max(abs(v) for v in L), max(abs(v) for v in R))
    rms = math.sqrt(sum(a * a + b * b for a, b in zip(L, R)) / (2 * n))
    dc = max(abs(sum(L) / n), abs(sum(R) / n))
    edge = max(max(abs(v) for v in L[:int(0.01 * sr)] + R[:int(0.01 * sr)]),
               max(abs(v) for v in L[-int(0.01 * sr):] + R[-int(0.01 * sr):]))
    mL, mR = sum(L) / n, sum(R) / n
    sl = math.sqrt(sum((a - mL) ** 2 for a in L))
    sr_ = math.sqrt(sum((b - mR) ** 2 for b in R))
    corr = sum((a - mL) * (b - mR) for a, b in zip(L, R)) / (sl * sr_ + 1e-12)
    # Spektrum: Mittel ueber Bloecke
    N = 4096
    lo = mid = hi = 0.0
    hann = [0.5 - 0.5 * math.cos(TWO_PI * i / N) for i in range(N)]
    step = max(N, (n - N) // 24)
    for s in range(0, n - N, step):
        blk = [complex(((L[s + i] + R[s + i]) * 0.5) * hann[i]) for i in range(N)]
        sp = fft(blk)
        for k in range(1, N // 2):
            f = k * sr / N
            p = abs(sp[k]) ** 2
            if f < 200:
                lo += p
            elif f < 2000:
                mid += p
            else:
                hi += p
    tot = lo + mid + hi
    # 1-s-RMS Spannweite
    sec = []
    for s in range(0, n - sr, sr):
        sec.append(math.sqrt(sum(L[i] ** 2 for i in range(s, s + sr)) / sr))
    return dict(dur=n / sr, peak=peak, rms=rms, dc=dc, edge=edge, corr=corr, lo=lo / tot, mid=mid / tot,
                hi=hi / tot, size=os.path.getsize(path) / 1e6, sec_min=min(sec), sec_max=max(sec))


def report(names):
    print("%-24s %5s %7s %7s %8s %7s %6s  %4s/%4s/%4s %6s %12s" % (
        "Datei", "s", "Peak dB", "RMS dB", "DC", "Rand", "L/R-r", "<200", "-2k", ">2k", "MB", "1s-RMS dB min/max"))
    for nme in names:
        a = analyze(os.path.join(OUT_DIR, nme + ".wav"))
        db = lambda v: 20 * math.log10(max(v, 1e-9))
        print("%-24s %5.1f %7.2f %7.2f %8.5f %7.4f %6.3f  %3.0f%%/%3.0f%%/%3.0f%% %6.2f  %5.1f / %5.1f" % (
            nme, a["dur"], db(a["peak"]), db(a["rms"]), a["dc"], a["edge"], a["corr"],
            a["lo"] * 100, a["mid"] * 100, a["hi"] * 100, a["size"], db(a["sec_min"]), db(a["sec_max"])))


if __name__ == "__main__":
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    names = args if args else sorted(PIECES)
    if "--check" not in sys.argv:
        with Pool(min(4, len(names))) as pool:
            for done in pool.imap_unordered(render, names):
                print("fertig:", done, flush=True)
    report(names)
