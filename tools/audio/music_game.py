#!/usr/bin/env python3
"""
music_game.py - spieltaugliche, nahtlos schleifende Musik fuer "Space Adventure"

Erzeugt nach godot/assets/music/ (nur Standardbibliothek, fester Seed, reproduzierbar):
  layer_pad / layer_pulse / layer_melody / layer_tension / layer_combat / layer_epic
      adaptive Stems: gleiches Tempo (80 BPM), gleiche Folge Am-F-C-G (je 1 Takt, 8 Takte = 24 s),
      taktgenau synchron, nahtlos schleifend, ihre Summe clippt nicht.
  mood_safe / mood_frontier / mood_mining / mood_docked / mood_menu
      eigenstaendige Schleifen derselben Laenge/Tempo/Tonart (ueberblendbar).
  sting_victory / sting_loss / sting_jump / sting_war / sting_peace / sting_warning
      kurze Signale (nicht schleifend).
  music.json   Metadaten: Tempo, Laenge, Dateien, Rollen, Lautstaerken, Zustandstabelle.

Nahtlosigkeit: Alles wird linear (Synthese, Hall, Hochpass) ueber die Schleifenlaenge hinaus
gerendert; der Ueberhang (Hallfahne, Release) wird auf den Anfang addiert ("gefaltet"). Der
ganze Inhalt ist um 30 ms nach hinten verschoben, damit kein Schlag exakt auf Sample 0 liegt.
Format: WAV, 32 kHz, 16 bit; Pads/Epic/Moods mit Stereo-Breite stereo, der Rest mono.

Benutzung:  python3 tools/audio/music_game.py           # alles erzeugen + messen
            python3 tools/audio/music_game.py --check   # nur messen
Die Synthese-Bausteine kommen aus music_samples.py (gleicher Ordner).
"""
import array
import json
import math
import os
import sys
import wave
from multiprocessing import Pool

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import music_samples as ms  # noqa: E402
from music_samples import (SR, CH, ARP_PAT, Mix, pad, sub, pluck, lead, bell, kick, tom, noise_hit,  # noqa: E402
                           riser, sweep, choir, arp, hz)

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "godot", "assets", "music")
BPM = 80
SPB = 60.0 / BPM
BAR = 4 * SPB            # 3 s
BARS = 8
LOOP_S = BAR * BARS      # 24 s
LOOP = int(LOOP_S * SR)  # 768000 Samples
TAIL_S = 8.0
OFF = 0.03
PROG = ["Am", "F", "C", "G"]
CEIL = 0.89


class LoopMix(Mix):
    """Mix mit globalem Zeitversatz OFF (Inhalt beginnt nicht exakt bei Sample 0)."""

    def __init__(self, seed, rev):
        super().__init__(LOOP_S + TAIL_S, seed, *rev)

    def put(self, sig, t, gl, gr, send):
        super().put(sig, t + OFF, gl, gr, send)

    def put_st(self, l, r, t, g, send):
        super().put_st(l, r, t + OFF, g, send)


def bass_of(c):
    b = CH[c][0]
    return b if b < 40 else b - 12


def render_linear(m):
    """Hall + Hochpass linear, dann Ueberhang auf den Anfang falten. -> (L, R) Laenge LOOP."""
    g, damp, wet = m.rev
    pre = int(0.022 * SR)
    sl = [0.0] * pre + m.SL[:-pre]
    sr = [0.0] * pre + m.SRr[:-pre]
    wl = ms.reverb(sl, g, damp, 0)
    wr = ms.reverb(sr, g, damp, 1)
    res = []
    a = 1 - math.exp(-ms.TWO_PI * 25 / SR)
    for dry, wt in ((m.L, wl), (m.R, wr)):
        x = [p + q * wet for p, q in zip(dry, wt)]
        lo = 0.0
        y = [0.0] * len(x)
        for i, v in enumerate(x):
            lo += a * (v - lo)
            y[i] = v - lo
        head = y[:LOOP]
        tail = y[LOOP:]
        k = len(tail)
        head[:k] = [p + q for p, q in zip(head[:k], tail)]
        res.append(array.array("f", head))
    return res


# ------------------------------------------------------------------ Melodien (Eigenkomposition)
MEL1 = {"Am": [(0, 69, 2), (2, 72, 1), (3, 76, 1)], "F": [(0, 77, 2), (2, 76, 1), (3, 72, 1)],
        "C": [(0, 72, 2), (2, 76, 1), (3, 79, 1)], "G": [(0, 74, 3), (3, 71, 1)]}
MEL2 = {"Am": [(0, 72, 1), (1, 76, 1), (2, 81, 2)], "F": [(0, 77, 2), (2, 72, 1), (3, 76, 1)],
        "C": [(0, 79, 1.5), (1.5, 76, 0.5), (2, 72, 2)], "G": [(0, 74, 2), (2, 71, 1), (3, 67, 1)]}


def mel_play(m, mel, b, shift=0, amp=0.13, bright=3800, sparkle=True, send=0.5):
    c = PROG[b % 4]
    for bt, mi, ln in mel[c]:
        t = b * BAR + bt * SPB
        lead(m, t, ln * SPB * 0.97, mi + shift, amp=amp, bright=bright, send=send)
        if sparkle:
            bell(m, t, mi + shift + 12, amp=amp * 0.35, dec=1.4, pan=0.4 * math.sin(t), send=0.8)


# ------------------------------------------------------------------ Schichten
def L_pad(m):
    for b in range(BARS):
        c = PROG[b % 4]
        t = b * BAR
        pad(m, t, BAR, CH[c][1], amp=0.2, cut=(900, 3000), att=0.8, rel=0.8, det=11, lfo=(0.09, 0.25))
        pad(m, t, BAR, [n + 24 for n in CH[c][1][1:4]], amp=0.07, cut=(3200, 6500), att=0.8, rel=0.8, det=16,
            send=0.7)
        sub(m, t, BAR, bass_of(c), amp=0.18, att=0.1, rel=0.3, h2=0.5)


def L_pulse(m):
    for b in range(BARS):
        c = PROG[b % 4]
        t = b * BAR
        arp(m, t, SPB, c, 4, amp=0.13, bright=5800, dec=0.5, pat=ARP_PAT)
        for beat in range(4):
            sub(m, t + beat * SPB, 0.2, bass_of(c) + 12, amp=0.1 if beat else 0.14, att=0.01, rel=0.15, send=0.2,
                h2=0.6)
        if b % 2 == 0:
            bell(m, t + 0.5 * SPB, CH[c][2][4] + 12, amp=0.06, dec=1.6, pan=0.5, send=0.8)


def L_melody(m):
    for b in range(BARS):
        mel_play(m, MEL1 if b < 4 else MEL2, b)


def L_tension(m):
    for i in range(2):
        pad(m, i * 12.0, 6.0, [57, 58, 64, 70], amp=0.22, cut=(600, 4200), att=6, rel=6, det=24, send=0.5,
            sweep_len=12)
        pad(m, 6.0 + i * 12.0, 6.0, [45, 51, 57, 63], amp=0.22, cut=(600, 4200), att=6, rel=6, det=24, send=0.5,
            sweep_len=12)
    for b in range(BARS):
        t = b * BAR
        tom(m, t, 40, amp=0.3, dec=0.4, send=0.4)
        tom(m, t + 1.5 * SPB, 40, amp=0.2, dec=0.3, send=0.4)
    for tt, mi in ((2.2, 81), (7.9, 77), (13.4, 84), (19.1, 80), (22.0, 76)):
        bell(m, tt, mi, amp=0.1, dec=2.2, pan=0.6 * math.sin(tt * 3), send=0.85, metallic=True)
    for tt in (5.0, 16.5):
        noise_hit(m, tt, dur=0.6, amp=0.05, hp=0.7, pan=0.5, send=0.7)


def L_combat(m):
    for b in range(BARS):
        c = PROG[b % 4]
        t = b * BAR
        bass = CH[c][0]
        for i in range(8):
            mi = bass + 24 + (12 if i % 4 == 3 else 0)
            pluck(m, t + i * 0.5 * SPB, mi, amp=0.3, bright=2200, dec=0.22, send=0.1, saw=0.8)
        for beat in (0, 2):
            kick(m, t + beat * SPB, amp=0.38)
        if b % 2:
            kick(m, t + 3.5 * SPB, amp=0.4)
        for beat in (1, 3):
            noise_hit(m, t + beat * SPB, dur=0.14, amp=0.42, hp=0.35, send=0.3)
            tom(m, t + beat * SPB, 62, amp=0.14, dec=0.14, send=0.2, drop=1.5)
        for i in range(8):
            noise_hit(m, t + i * 0.5 * SPB, dur=0.035, amp=0.09 + (0.04 if i % 2 else 0), hp=0.88,
                      pan=0.35 * (-1) ** i, send=0.1)
        arp(m, t, SPB, c, 4, amp=0.24, pan_w=0.7, bright=6000, dec=0.18, shift=12, pat=[0, 2, 4, 2, 3, 1, 4, 2],
            step=0.25)
        if b % 4 == 3:
            for k, mi in enumerate([52, 50, 47, 45]):
                tom(m, t + (3 + k * 0.25) * SPB, mi, amp=0.35, dec=0.28, pan=0.5 - 0.3 * k, send=0.35)
        if b % 4 == 0:
            noise_hit(m, t, dur=0.9, amp=0.07, hp=0.6, send=0.5)


def L_epic(m):
    for b in range(BARS):
        c = PROG[b % 4]
        t = b * BAR
        bass, padn, _ = CH[c]
        wide = [padn[0] - 12] + padn + [n + 12 for n in padn[1:4]] + [padn[-1] + 24]
        pad(m, t, BAR, wide, amp=0.2, cut=(1500, 6500), att=0.8, rel=0.8, voices=4, det=15, send=0.55)
        choir(m, t, BAR, [n + 12 for n in padn[1:5]], amp=0.1, att=0.8, rel=0.8, v0="ah", v1="oh" if b % 2 else "oo")
        sub(m, t, BAR, bass_of(c), amp=0.22, att=0.1, rel=0.3, h2=0.5)
        tom(m, t, 36, amp=0.7, dec=0.7, send=0.5)
        tom(m, t + SPB, 40, amp=0.4, dec=0.5, pan=0.25, send=0.5)
        tom(m, t + 2.5 * SPB, 36, amp=0.6, dec=0.6, send=0.5)
        tom(m, t + 3 * SPB, 43, amp=0.35, dec=0.4, pan=-0.3, send=0.5)
        kick(m, t, amp=0.4, f0=110, f1=38, dec=0.3, send=0.2)
        if b % 4 == 0:
            noise_hit(m, t, dur=1.6, amp=0.1, hp=0.55, send=0.6)
        if b % 4 == 3:
            for k in range(6):
                tom(m, t + (3 + k / 8.0) * SPB, 45 - k, amp=0.2 + 0.03 * k, dec=0.25, pan=0.5 - 0.2 * k, send=0.5)
        mel_play(m, MEL1, b, shift=12, amp=0.1, bright=4500, sparkle=False, send=0.6)


# ------------------------------------------------------------------ Stimmungen
SOFT = {"Am": [57, 60, 64, 67, 71], "F": [53, 57, 60, 64, 69], "C": [55, 60, 64, 67, 71], "G": [55, 59, 62, 69, 74]}


def M_safe(m):
    for b in range(BARS):
        c = PROG[b % 4]
        t = b * BAR
        pad(m, t, BAR, SOFT[c], amp=0.22, cut=(700, 2200), att=1.0, rel=1.0, det=9, lfo=(0.07, 0.3), send=0.55)
        pad(m, t, BAR, [n + 24 for n in SOFT[c][1:4]], amp=0.05, cut=(3000, 5500), att=1.0, rel=1.0, det=14, send=0.8)
        sub(m, t, BAR, bass_of(c), amp=0.17, att=0.2, rel=0.4, h2=0.5)
        for beat in (0, 2):
            sub(m, t + beat * SPB, 0.5, bass_of(c) + 12, amp=0.06, att=0.1, rel=0.4, send=0.3, h2=0.4)
    for i, (tt, mi) in enumerate([(2.0, 88), (6.8, 84), (10.5, 91), (15.0, 88), (19.6, 84), (22.5, 79)]):
        pluck(m, tt, mi, amp=0.07, pan=0.5 * (-1) ** i, bright=5000, dec=1.2, send=0.8, echo=SPB * 1.5, saw=0.25)


def M_frontier(m):
    for b in range(BARS):
        c = PROG[b % 4]
        t = b * BAR
        r = CH[c][0] + 12 if CH[c][0] < 40 else CH[c][0]
        pad(m, t, BAR, [r - 12 if r > 40 else r, r + 7 - (12 if r > 40 else 0)], amp=0.2, cut=(300, 900), att=1.0,
            rel=1.0, det=12, lfo=(0.05, 0.4), send=0.4)
        sub(m, t, BAR, bass_of(c) - 7 if bass_of(c) > 36 else bass_of(c), amp=0.14, att=0.3, rel=0.5, h2=0.6)
        pad(m, t, BAR, [r + 24, r + 31], amp=0.1, cut=(1600, 3200), att=1.0, rel=1.0, det=26, lfo=(0.09, 0.5),
            send=0.8)
    for i, (tt, mi) in enumerate([(1.4, 81), (4.9, 77), (8.3, 84), (11.7, 80), (14.6, 76), (17.9, 83), (21.2, 79)]):
        bell(m, tt, mi, amp=0.1, dec=2.2, pan=0.8 * (-1) ** i, send=0.85, metallic=True)
    for tt in (6.0, 13.0, 20.0):
        noise_hit(m, tt, dur=0.5, amp=0.04, hp=0.75, pan=0.6, send=0.7)


def M_mining(m):
    pat = [0, 2, 1, 3, 2, 4, 3, 1]
    for b in range(BARS):
        c = PROG[b % 4]
        t = b * BAR
        pad(m, t, BAR, CH[c][1], amp=0.16, cut=(700, 2200), att=0.8, rel=0.8, send=0.5)
        sub(m, t, BAR, bass_of(c), amp=0.18, att=0.1, rel=0.3, h2=0.5)
        arp(m, t, SPB, c, 4, amp=0.14, bright=3500 + 300 * b, dec=0.5, pat=pat, step=0.5)
        for beat in range(4):
            sub(m, t + beat * SPB, 0.22, bass_of(c) + 12, amp=0.08 if beat else 0.12, att=0.01, rel=0.2, send=0.2,
                h2=0.5)
    for k, tt in enumerate([6.0, 12.0, 18.0]):
        for j in range(3):
            pluck(m, tt + j * 0.75 * 2, [88, 84, 91][j], amp=0.06, pan=0.5 * (-1) ** (j + k), bright=4500, dec=1.1,
                  saw=0.2, send=0.8, echo=SPB * 1.5)


def M_docked(m):
    for b in range(BARS):
        c = PROG[b % 4]
        t = b * BAR
        pad(m, t, BAR, SOFT[c][:4], amp=0.2, cut=(500, 1500), att=1.2, rel=1.2, det=8, lfo=(0.06, 0.25), send=0.6)
        pad(m, t, BAR, [n + 12 for n in SOFT[c][2:5]], amp=0.06, cut=(1800, 3800), att=1.2, rel=1.2, det=12, send=0.7)
        sub(m, t, BAR, bass_of(c), amp=0.1, att=0.4, rel=0.6, h2=0.5)
    for i, (tt, mi) in enumerate([(3.0, 84), (9.0, 88), (15.0, 81), (21.0, 86)]):
        bell(m, tt, mi, amp=0.07, dec=2.5, pan=0.4 * (-1) ** i, send=0.8)


def M_menu(m):
    for b in range(BARS):
        c = PROG[b % 4]
        t = b * BAR
        bass, padn, _ = CH[c]
        epic = b >= 4
        if not epic:
            pad(m, t, BAR, padn, amp=0.2, cut=(700, 2200), att=0.8, rel=0.8, send=0.5)
            pad(m, t, BAR, [n + 24 for n in padn[1:4]], amp=0.06, cut=(3000, 6000), att=0.8, rel=0.8, det=16, send=0.7)
            arp(m, t, SPB, c, 4, amp=0.1 + 0.01 * b, bright=3500 + 400 * b, dec=0.5)
            if b >= 2:
                mel_play(m, MEL1, b, amp=0.12, bright=3400)
        else:
            wide = [padn[0] - 12] + padn + [n + 12 for n in padn[1:4]] + [padn[-1] + 24]
            pad(m, t, BAR, wide, amp=0.22, cut=(1500, 6500), att=0.8, rel=0.8, voices=4, det=15, send=0.55)
            choir(m, t, BAR, [n + 12 for n in padn[1:5]], amp=0.1, att=0.8, rel=0.8, v0="ah",
                  v1="oh" if b % 2 else "oo")
            arp(m, t, SPB, c, 4, amp=0.09, bright=5500, dec=0.5, shift=12)
            mel_play(m, MEL2, b, shift=12, amp=0.12, bright=4800)
            mel_play(m, MEL2, b, shift=0, amp=0.1, bright=2600, sparkle=False)
            tom(m, t, 36, amp=0.6, dec=0.7, send=0.5)
            tom(m, t + 2.5 * SPB, 36, amp=0.45, dec=0.6, send=0.5)
            tom(m, t + 3 * SPB, 41, amp=0.3, dec=0.4, pan=0.3, send=0.5)
            if b % 4 == 0:
                noise_hit(m, t, dur=1.6, amp=0.09, hp=0.55, send=0.6)
        sub(m, t, BAR, bass_of(c), amp=0.2 if epic else 0.16, att=0.1, rel=0.3, h2=0.5)
    riser(m, 3 * BAR, BAR, amp=0.04)


# ------------------------------------------------------------------ Signale
def S_victory(m):
    t = 0.2
    for i, mi in enumerate([62, 66, 69, 74, 78, 81]):
        pluck(m, t + i * 0.11, mi, amp=0.14, pan=-0.5 + i * 0.2, bright=7500, dec=0.9, send=0.5, saw=0.5)
    pad(m, t + 0.5, 1.8, [50, 57, 62, 66, 69, 74, 76], amp=0.32, cut=(2800, 7000), att=0.1, rel=1.5, send=0.6)
    bell(m, t + 0.65, 86, amp=0.12, dec=2.0)
    bell(m, t + 0.75, 93, amp=0.07, pan=0.4, dec=1.5)
    sub(m, t + 0.5, 1.3, 38, amp=0.2)


def S_loss(m):
    t = 0.2
    for i, mi in enumerate([76, 74, 72, 69, 64]):
        lead(m, t + i * 0.42, 0.55, mi, amp=0.15, bright=2200 - 200 * i, att=0.03, rel=0.6, send=0.6, vdep=0.008)
    pad(m, t + 0.4, 2.6, [40, 47, 52, 55, 59], amp=0.45, cut=(1300, 400), att=0.3, rel=1.8, det=22, sweep_len=2.6)
    sweep(m, t + 1.6, 1.8, 160, 38, amp=0.16, bright=800, down=True)
    tom(m, t + 2.2, 31, amp=0.6, dec=0.9, send=0.5)


def S_jump(m):
    t = 0.1
    sweep(m, t, 1.4, 120, 2400, amp=0.16, bright=6000)
    riser(m, t, 1.4, amp=0.09, f0=400, f1=9000)
    pad(m, t + 1.35, 1.8, [57, 64, 69, 73, 76, 81], amp=0.28, cut=(3800, 7500), att=0.04, rel=1.6, det=18, send=0.7)
    for i, mi in enumerate([93, 88, 88]):
        bell(m, t + 1.4 + i * 0.12, mi, amp=0.07, pan=(-0.6, 0.6, 0)[i], dec=1.5, send=0.8)


def S_war(m):
    t = 0.1
    pad(m, t, 3.2, [34, 41, 46, 47, 53], amp=0.7, cut=(400, 1800), att=0.4, rel=1.8, det=20, send=0.5, sweep_len=3.2)
    sub(m, t, 3.2, 29, amp=0.3, att=0.2)
    for i, (dt, mi, a) in enumerate([(0.0, 33, 0.5), (1.0, 33, 0.5), (2.0, 31, 0.55), (2.5, 36, 0.3)]):
        tom(m, t + dt, mi, amp=a, dec=0.8, send=0.55, pan=0.2 * (-1) ** i)
    kick(m, t + 2.0, amp=0.4, f0=100, f1=36, dec=0.4)
    noise_hit(m, t + 2.0, dur=1.2, amp=0.08, hp=0.5, send=0.6)


def S_peace(m):
    t = 0.1
    pad(m, t, 3.0, [53, 57, 60, 64, 69, 76], amp=0.34, cut=(800, 3600), att=0.8, rel=2.2, det=9, send=0.6,
        sweep_len=2.5)
    sub(m, t, 2.8, 41, amp=0.2, att=0.4)
    for i, mi in enumerate([72, 76, 81]):
        lead(m, t + 0.3 + i * 0.5, 0.9, mi, amp=0.14, bright=3600, att=0.05, rel=1.2, send=0.6)
    bell(m, t + 1.9, 88, amp=0.08, dec=2.5)


def S_warning(m):
    t = 0.05
    for i in range(6):
        lead(m, t + i * 0.24, 0.2, 76 if i % 2 == 0 else 70, amp=0.35, bright=4500, att=0.01, rel=0.08, send=0.3,
             vdep=0.0)
    for i in range(4):
        tom(m, t + i * 0.48, 31, amp=0.25, dec=0.3, send=0.3)
    sweep(m, t + 1.4, 1.0, 200, 1400, amp=0.1, bright=3500)
    pad(m, t + 1.5, 1.2, [40, 46, 52, 58], amp=0.3, cut=(600, 3500), att=0.8, rel=0.8, det=20, send=0.4)
    tom(m, t + 2.5, 29, amp=0.7, dec=0.7, send=0.4)


# ------------------------------------------------------------------ Plan
LAYERS = [  # name, builder, seed, mono, Ziel-RMS dB (relativ), Hall
    ("layer_pad", L_pad, 101, False, -21, (0.88, 0.35, 1.0)),
    ("layer_pulse", L_pulse, 102, True, -24, (0.88, 0.35, 1.0)),
    ("layer_melody", L_melody, 103, True, -24, (0.88, 0.35, 1.0)),
    ("layer_tension", L_tension, 104, True, -25, (0.90, 0.30, 1.0)),
    ("layer_combat", L_combat, 105, True, -22, (0.80, 0.38, 0.8)),
    ("layer_epic", L_epic, 106, False, -21, (0.90, 0.34, 1.0)),
]
MOODS = [
    ("mood_safe", M_safe, 111, False, -22, (0.90, 0.40, 1.0)),
    ("mood_frontier", M_frontier, 112, True, -22, (0.92, 0.30, 1.0)),
    ("mood_mining", M_mining, 113, True, -20, (0.88, 0.35, 1.0)),
    ("mood_docked", M_docked, 114, True, -24, (0.92, 0.40, 1.0)),
    ("mood_menu", M_menu, 115, False, -18, (0.90, 0.34, 1.0)),
]
STINGS = [
    ("sting_victory", S_victory, 121, 5.2), ("sting_loss", S_loss, 122, 5.2), ("sting_jump", S_jump, 123, 4.2),
    ("sting_war", S_war, 124, 5.8), ("sting_peace", S_peace, 125, 5.4), ("sting_warning", S_warning, 126, 4.0),
]
STATES = {  # Spielzustand -> (Schichten mit Lautstaerke dB) bzw. Stimmung
    "flug_sicher": {"mood": "mood_safe", "layers": {}},
    "grenzgebiet": {"mood": "mood_frontier", "layers": {}},
    "bergbau": {"mood": "mood_mining", "layers": {}},
    "angedockt": {"mood": "mood_docked", "layers": {}},
    "menue": {"mood": "mood_menu", "layers": {}},
    "warnung": {"mood": None, "layers": {"layer_pad": 0, "layer_pulse": -3, "layer_tension": -3}},
    "gefecht": {"mood": None, "layers": {"layer_pad": -3, "layer_pulse": -3, "layer_combat": 0, "layer_tension": -6}},
    "belagerung": {"mood": None, "layers": {"layer_pad": -3, "layer_pulse": -6, "layer_combat": -3, "layer_epic": 0,
                                           "layer_tension": -6, "layer_melody": -9}},
}


def mono_of(l, r):
    return array.array("f", [(a + b) * 0.5 for a, b in zip(l, r)])


def rms_of(chs):
    n = len(chs[0])
    return math.sqrt(sum(sum(v * v for v in c) for c in chs) / (n * len(chs)))


def to_int16(chs, gain, soft):
    knee = 0.6
    span = CEIL - knee
    n = len(chs[0])
    out = array.array("h")
    for i in range(n):
        for c in chs:
            v = c[i] * gain
            if soft:
                av = abs(v)
                if av > knee:
                    av = knee + span * math.tanh((av - knee) / span)
                    v = av if v > 0 else -av
            out.append(int(round(v * 32767)))
    return out


def write(path, chs, gain, soft):
    data = to_int16(chs, gain, soft)
    with wave.open(path, "wb") as w:
        w.setnchannels(len(chs))
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(data.tobytes())


def job_layer(spec):
    name, fn, seed, mono, tgt, rev = spec
    m = LoopMix(seed, rev)
    fn(m)
    l, r = render_linear(m)
    chs = [mono_of(l, r)] if mono else [l, r]
    g = 10 ** (tgt / 20) / rms_of(chs)
    return name, [array.array("f", [v * g for v in c]) for c in chs]


def job_mood(spec):
    name, fn, seed, mono, tgt, rev = spec
    m = LoopMix(seed, rev)
    fn(m)
    l, r = render_linear(m)
    chs = [mono_of(l, r)] if mono else [l, r]
    g = 10 ** (tgt / 20) / rms_of(chs)
    write(os.path.join(OUT, name + ".wav"), chs, g, True)
    return name


def job_sting(spec):
    name, fn, seed, dur = spec
    m = Mix(dur, seed, 0.86, 0.35, 1.0)
    fn(m)
    data = ms.master(m, -15.5, fade_in=0.004, fade_out=1.0)
    mono = array.array("h", [(a + b) // 2 for a, b in zip(data[0::2], data[1::2])])
    with wave.open(os.path.join(OUT, name + ".wav"), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(mono.tobytes())
    return name


def state_sum(layers, vols, n):
    chs = None
    for name, v in vols.items():
        g = 10 ** (v / 20)
        src = layers[name]
        if chs is None:
            chs = [[0.0] * n, [0.0] * n]
        for k in range(2):
            c = src[k] if len(src) > 1 else src[0]
            chs[k] = [a + b * g for a, b in zip(chs[k], c)]
    return chs


def build():
    os.makedirs(OUT, exist_ok=True)
    with Pool(4) as pool:
        r1 = pool.map_async(job_layer, LAYERS)
        r2 = pool.map_async(job_mood, MOODS)
        r3 = pool.map_async(job_sting, STINGS)
        layers = dict(r1.get())
        r2.get()
        r3.get()
    n = LOOP
    worst = 0.0
    all_vols = {k: 0 for k in layers}
    sums = dict(STATES)
    sums["alle_sechs"] = {"layers": all_vols}
    state_info = {}
    for sname, st in sums.items():
        if not st["layers"]:
            continue
        ch = state_sum(layers, st["layers"], n)
        pk = max(max(abs(v) for v in ch[0]), max(abs(v) for v in ch[1]))
        worst = max(worst, pk)
        state_info[sname] = pk
    k = min(1.0, CEIL / worst)
    kdb = 20 * math.log10(k)
    for name, chs in layers.items():
        write(os.path.join(OUT, name + ".wav"), chs, k, False)
    print("Schicht-Skalierung %.2f dB (Summen-Spitze vorher %.3f)" % (kdb, worst))
    return k, state_info


def read_wav(path):
    with wave.open(path, "rb") as w:
        nch = w.getnchannels()
        raw = array.array("h")
        raw.frombytes(w.readframes(w.getnframes()))
    return [[v / 32768.0 for v in raw[c::nch]] for c in range(nch)]


def band_shares(x):
    N = 4096
    hann = [0.5 - 0.5 * math.cos(ms.TWO_PI * i / N) for i in range(N)]
    lo = mid = hi = 0.0
    step = max(N, (len(x) - N) // 20)
    for s in range(0, len(x) - N, step):
        sp = ms.fft([complex(x[s + i] * hann[i]) for i in range(N)])
        for k in range(1, N // 2):
            f = k * SR / N
            p = abs(sp[k]) ** 2
            if f < 200:
                lo += p
            elif f < 2000:
                mid += p
            else:
                hi += p
    t = lo + mid + hi + 1e-30
    return lo / t, mid / t, hi / t


def db(v):
    return 20 * math.log10(max(v, 1e-9))


def check(names):
    print("%-15s %4s %5s %7s %7s %7s | %5s %5s %5s | %s" % (
        "Datei", "ch", "s", "Peak", "RMS", "MB", "<200", "-2k", ">2k", "Naht: Sprung/typ.dif (max dif-Verh.)"))
    for name in names:
        path = os.path.join(OUT, name + ".wav")
        chs = read_wav(path)
        n = len(chs[0])
        peak = max(max(abs(v) for v in c) for c in chs)
        rms = math.sqrt(sum(sum(v * v for v in c) for c in chs) / (n * len(chs)))
        mix = [sum(c[i] for c in chs) / len(chs) for i in range(n)] if len(chs) > 1 else chs[0]
        lo, mid, hi = band_shares(mix)
        seam = ""
        if name.startswith(("layer", "mood")):
            worst = 0.0
            for c in chs:
                d2 = c + c  # Datei zweimal hintereinander
                diffs = [abs(d2[i + 1] - d2[i]) for i in range(len(d2) - 1)]
                typ = math.sqrt(sum(d * d for d in diffs) / len(diffs))
                jump = abs(d2[n] - d2[n - 1])
                mx = max(diffs)
                worst = max(worst, jump / (typ + 1e-12))
                seam = "%.2f x typ (max-Diff %.2f x typ)" % (worst, mx / (typ + 1e-12))
            seam += "  erster %.5f letzter %.5f" % (chs[0][0], chs[0][-1])
        print("%-15s %4d %5.1f %7.2f %7.2f %7.2f | %4.0f%% %4.0f%% %4.0f%% | %s" % (
            name, len(chs), n / SR, db(peak), db(rms), os.path.getsize(path) / 1e6, lo * 100, mid * 100, hi * 100,
            seam))


def write_json(k, state_info):
    kdb = 20 * math.log10(k)
    files = []
    for name, _, _, mono, tgt, _ in LAYERS:
        files.append({"file": name + ".wav", "role": "layer", "loop": True, "channels": 1 if mono else 2,
                      "volume_db": 0.0})
    for name, _, _, mono, tgt, _ in MOODS:
        files.append({"file": name + ".wav", "role": "mood", "loop": True, "channels": 1 if mono else 2,
                      "volume_db": 0.0})
    for name, _, _, dur in STINGS:
        files.append({"file": name + ".wav", "role": "sting", "loop": False, "channels": 1, "volume_db": 0.0,
                      "duration_s": dur})
    for f in files:
        f["size_bytes"] = os.path.getsize(os.path.join(OUT, f["file"]))
    states = {}
    for s, st in STATES.items():
        states[s] = {"mood": st["mood"], "layers": st["layers"]}
    states["flug_sicher"]["optional_layers"] = {"layer_melody": -9}
    states["grenzgebiet"]["optional_layers"] = {"layer_tension": -9}
    doc = {
        "sample_rate": SR, "bpm": BPM, "bars": BARS, "beats_per_bar": 4, "key": "A-Moll (Am-F-C-G, je 1 Takt)",
        "loop_seconds": LOOP_S, "loop_samples": LOOP, "seconds_per_bar": BAR,
        "content_offset_s": OFF,
        "notes": [
            "Alle layer_* und mood_* sind exakt gleich lang und nahtlos schleifend; gleichzeitig starten und loopen.",
            "Der Inhalt ist um content_offset_s verschoben (erster Taktschlag bei 0.03 s); fuer Sync irrelevant,"
            " da alle Dateien gleich verschoben sind.",
            "Schichtsummen clippen nicht: alle 6 Schichten gleichzeitig (0 dB) und alle Zustaende der Tabelle"
            " bleiben <= -1 dBFS (Skalierung der Schichten %.2f dB)." % kdb,
            "Stimmungen (mood_*) sind eigenstaendig und werden NICHT mit Schichten summiert; zwischen Stimmungen"
            " bzw. Zustaenden 1 Takt (3 s) ueberblenden.",
            "Stings (nicht schleifend) mit ca. -1 dB Headroom lauter als Musik abspielen, Musik waehrenddessen"
            " um ca. 4 dB duecken.",
        ],
        "crossfade_seconds": BAR, "sting_duck_music_db": -4.0,
        "files": files, "states": states,
        "state_peak_dbfs": {k2: round(db(v * k), 2) for k2, v in state_info.items()},
    }
    with open(os.path.join(OUT, "music.json"), "w", encoding="utf-8") as fh:
        json.dump(doc, fh, indent=2, ensure_ascii=False)
        fh.write("\n")


if __name__ == "__main__":
    names = [x[0] for x in LAYERS] + [x[0] for x in MOODS] + [x[0] for x in STINGS]
    if "--check" not in sys.argv:
        kk, info = build()
        write_json(kk, info)
    check(names)
    tot = sum(os.path.getsize(os.path.join(OUT, f)) for f in os.listdir(OUT))
    print("Gesamtgroesse: %.2f MB" % (tot / 1e6))
