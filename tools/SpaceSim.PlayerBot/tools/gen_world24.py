#!/usr/bin/env python3
"""Erzeugt die 24-Sektoren-Testwelt aus default-world.json (nur Testwelt, Standardwelt bleibt unberuehrt).
Aufruf: python3 -I tools/SpaceSim.PlayerBot/tools/gen_world24.py   (im Repo-Root)
"""
import json, random, math, copy

SRC = 'src/SpaceSim.Core/Content/default-world.json'
DST = 'tools/SpaceSim.PlayerBot/Content/world24.json'
w = json.load(open(SRC))
rnd = random.Random(2424)

sec = {s['id']: s for s in w['sectors']}
# ---- Typen-Vorlagen (Stock-Overrides etc. aus der Standardwelt) ----
tmpl = {}
for s in w['sectors']:
    for t in s['stations']:
        tmpl.setdefault(t['type'], t)
def tname(t):
    n = tmpl[t]['name']
    return n.rsplit(' ', 1)[0] if '{owner}' not in n else n
types = {t['key']: t for t in w['stationTypes']}
ECON_EXCL = ('headquarters', 'sector_hq', 'company_seat', 'office', 'police_station', 'pirate_hideout', 'research_station',
             'satellite_sensor', 'platform_light', 'platform_medium', 'platform_heavy', 'build_site')
def econ(t): return t not in ECON_EXCL

# ---- Fraktionen ----
w['factions'].append({"id": 4, "name": "Konföderation Orion", "kind": "state",
    "personality": {"aggression": 0.5, "greed": 0.45, "cooperation": 0.55, "lawfulness": 0.75}, "relations": {}})
w['factions'].append({"id": 5, "name": "Hanse Sirius", "kind": "guild",
    "personality": {"aggression": 0.4, "greed": 0.65, "cooperation": 0.55, "lawfulness": 0.5}, "relations": {}})
for f in w['factions']:
    f.setdefault('relations', {})
    for o in w['factions']:
        if o['id'] != f['id'] and (f['id'] >= 4 or o['id'] >= 4):
            f['relations'][str(o['id'])] = 0.2
w['actors'].append({"key": "reavers", "name": "Die Plünderer", "kind": "outlaw",
    "personality": {"aggression": 0.88, "greed": 0.85, "cooperation": 0.1, "lawfulness": 0.0}, "treasury": 4000})

# ---- neue Sektoren: id, name, grid, owner, belts, planet, skybox ----
NEW = [
 (12, 'Zephyr',      (-1,-1), 0, ['metal_rock','ice_rock'], None, 'white_local_star_and_nebulae'),
 (13, 'Westwind',    (-1, 0), 1, ['metal_rock','ice_rock'], None, 'blue_local_star_no_nebulae'),
 (14, 'Kaltfront',   (-1, 1), 0, ['rare_rock','silicate_rock'], None, 'red_local_star_no_nebulae'),
 (15, 'Cassiopeia',  (0,-2),  4, ['ice_rock','metal_rock'], None, 'blue_local_star_and_nebulae'),
 (16, 'Draco',       (1,-2),  4, ['silicate_rock'], None, 'white_local_star_no_nebulae'),
 (17, 'Lyra',        (2,-2),  4, ['rare_rock','metal_rock'], ('Elara','#7aa6c9',380), 'earthlike_planet'),
 (18, 'Perseus',     (3,-1),  3, ['rare_rock','metal_rock'], None, 'red_local_star_and_nebulae'),
 (19, 'Sirius',      (4, 0),  0, ['silicate_rock','rare_rock'], None, 'white_local_star_and_nebulae'),
 (20, 'Tauri',       (3, 1),  5, ['silicate_rock','ice_rock'], None, 'blue_local_star_and_nebulae'),
 (21, 'Corvus',      (0, 2),  0, ['ice_rock','rare_rock'], None, 'red_local_star_no_nebulae'),
 (22, 'Hydra',       (2, 2),  0, ['metal_rock'], None, 'red_local_star_no_nebulae'),
 (23, 'Phoenix',     (3, 2),  5, ['rare_rock','metal_rock'], ('Kalida','#c98f6b',400), 'desert_planet'),
 (24, 'Ophiuchus',   (4, 1),  0, ['metal_rock','silicate_rock'], None, 'white_local_star_no_nebulae'),
]
# Stationen je neuem Sektor (Typen); Sektor-HQs/Polizei sind amtlich
PLAN = {
 13: ['steelworks','water_plant','powerplant','solar_plant','fuel_refinery','sector_hq','platform_light'],
 15: ['water_plant','electrolysis','steelworks','solar_plant','sector_hq','satellite_sensor'],
 16: ['headquarters','copper_smelter','wafer_fab','electronics_fab','plating_works','shipyard','solar_plant','systems_works',
      'police_station','fuel_refinery','research_station'],
 17: ['titanium_works','platinum_refinery','fuel_cell_fab','solar_plant','trade_post','farm','water_plant','sector_hq','drive_works','platform_light'],
 18: ['steelworks','titanium_works','solar_plant','fuel_cell_fab','fuel_refinery','sector_hq','platform_light'],
 20: ['headquarters','farm','water_plant','solar_plant','copper_smelter','wafer_fab','electronics_fab','plating_works','shipyard',
      'fuel_refinery','police_station','research_station'],
 23: ['titanium_works','platinum_refinery','steelworks','goods_factory','life_support_fab','colony','trade_post','luxury_workshop',
      'sector_hq','weapons_works','fuel_refinery','solar_plant'],
 22: ['pirate_hideout'],
}
GRID = {s['id']: tuple(s['map']) for s in w['sectors']}
for n in NEW: GRID[n[0]] = n[2]
# Gate-Paare (a, b, mainRoute)
EDGES = [(13,1,True),(13,12,True),(12,5,False),(13,14,True),(14,9,False),(5,15,True),(15,16,True),(16,6,False),(16,17,True),(17,7,False),
         (7,18,True),(18,8,False),(8,19,True),(19,24,True),(24,20,False),(20,8,True),(20,10,False),(20,23,True),(23,22,True),
         (22,10,False),(22,11,False),(21,11,False),(21,9,True)]
# Korrektur Spannbaum: je neuer Sektor genau eine Hauptroute
MAIN = {(13,1),(13,12),(13,14),(15,5),(16,15),(17,16),(18,7),(19,8),(24,19),(20,8),(23,20),(22,23),(21,9)}
EDGES = [(a,b,((a,b) in MAIN) or ((b,a) in MAIN)) for a,b,_ in EDGES]
for a,b,_ in EDGES:
    ga, gb = GRID[a], GRID[b]
    assert abs(ga[0]-gb[0]) + abs(ga[1]-gb[1]) == 1, (a,b)

def side(a, b):
    ga, gb = GRID[a], GRID[b]
    dx, dy = gb[0]-ga[0], gb[1]-ga[1]
    return {(1,0):'E',(-1,0):'W',(0,1):'S',(0,-1):'N'}[(dx,dy)]
BASEPOS = {'E':(150,0),'W':(-150,0),'N':(0,-150),'S':(0,150)}

# neue Sektoren anlegen (Felder zuerst)
BELTPOS = [(70,-70),(-70,70),(75,70),(-75,-70),(0,-85),(0,85),(-85,0),(85,0)]
for sid,name,grid,owner,belts,planet,sky in NEW:
    s = {"id": sid, "name": f"Sektor {name}", "skybox": sky, "scale": round(rnd.uniform(3.0, 8.0), 1), "owner": owner,
         "map": list(grid), "fields": [], "stations": []}
    if planet: s['planet'] = {"name": planet[0], "color": planet[1], "radius": planet[2]}
    # zwei Guertel auf gegenueberliegenden Diagonalen
    diag = rnd.choice([((70,-70),(-70,70)), ((-70,-70),(70,70))])
    for i,cls in enumerate(belts):
        s['fields'].append({"class": cls, "pos": list(diag[i]), "belt": 14 if i == 0 else 12, "respawn": 90})
    if not any(n[0] == sid and n[3] == 0 for n in NEW) or sid == 22:
        pass
    if owner == 0 and sid != 22:
        s['asteroids'] = 35
    if sid == 22: s['asteroids'] = 35
    w['sectors'].append(s); sec[sid] = s

# Gates
gid = max(max(g['a']['id'], g['b']['id']) for g in w['gates']) + 1
def d(p,q): return math.hypot(p[0]-q[0], p[1]-q[1])
def gate_ok(s, p, others):
    for f in s['fields']:
        if d(p, f['pos']) <= 88: return False
    for t in s['stations']:
        if d(p, t['pos']) <= 52: return False
    for o in others:
        if d(p, o) <= 60: return False
    return True
gatepos = {sid: [] for sid in sec}
for g in w['gates']:
    gatepos[g['a']['sector']].append(g['a']['pos']); gatepos[g['b']['sector']].append(g['b']['pos'])
def pick_gate(sid, sd):
    s = sec[sid]; bx, by = BASEPOS[sd]
    cands = [(bx, by)]
    for off in range(10, 130, 10):
        for sg in (1, -1):
            cands.append((bx, by + sg*off) if sd in 'EW' else (bx + sg*off, by))
    for c in cands:
        if gate_ok(s, c, gatepos[sid]): return list(c)
    raise SystemExit(f'kein Gate-Platz in Sektor {sid} Seite {sd}')
for a,b,main in EDGES:
    pa = pick_gate(a, side(a,b)); gatepos[a].append(pa)
    pb = pick_gate(b, side(b,a)); gatepos[b].append(pb)
    g = {"a": {"id": gid, "sector": a, "pos": pa}, "b": {"id": gid+1, "sector": b, "pos": pb}}
    if main: g['mainRoute'] = True
    w['gates'].append(g); gid += 2

# Stationen platzieren
sid_next = max(t['id'] for s in w['sectors'] for t in s['stations']) + 1
def place(s, tp, taken):
    big = tp in ('headquarters','sector_hq','shipyard','colony','drive_works','weapons_works','systems_works','trade_station','warehouse')
    sep = 60 if big else 52
    if tp == 'pirate_hideout': return [210, 190]
    for _ in range(4000):
        p = [rnd.randint(-150,150), rnd.randint(-150,150)]
        if any(d(p, f['pos']) <= 104 for f in s['fields']): continue
        if any(d(p, g) <= 54 for g in gatepos[s['id']]): continue
        if any(d(p, q) <= sep for q in taken): continue
        return p
    raise SystemExit(f"kein Platz fuer {tp} in {s['id']}")
for sid,plan in PLAN.items():
    s = sec[sid]; taken = [t['pos'] for t in s['stations']]
    short = s['name'].replace('Sektor ', '')
    for tp in plan:
        pos = place(s, tp, taken); taken.append(pos)
        base = tmpl[tp]
        st = {k: copy.deepcopy(v) for k, v in base.items() if k not in ('id','name','pos')}
        st['id'] = sid_next; sid_next += 1
        nm = tname(tp)
        st['name'] = nm if '{owner}' in nm else f"{nm} {short}"
        st['pos'] = pos
        st['essential'] = True
        if tp == 'pirate_hideout':
            st['owner'] = 'reavers'
            st['name'] = f"Versteck der Plünderer"
        s['stations'].append(st)
# bestehende Basis-Sektoren: Stationsfreiheit zu den neuen Gates wurde in gate_ok erzwungen

# ---- Rules ----
econ_old = sum(1 for s in w['sectors'] if s['id'] <= 11 for t in s['stations'] if econ(t['type']))
econ_new = sum(1 for s in w['sectors'] for t in s['stations'] if econ(t['type']))
r = w['rules']
r['maxStations'] = r['maxStations'] + (econ_new - econ_old)
r['maxTraders'] = 175; r['maxMiners'] = 120
r['maxBuilders'] = 12; r['maxActiveSites'] = 6
r['maxStationsPerSector'] = 10
w['seed'] = 2424

# ---- Gruenderflotte neue Sektoren ----
for sid in (13,15,16,17,18,20,23):
    f = sec[sid]['owner']
    w['ships'].append({"role":"trader","owner":"free","faction":f,"sector":sid,"credits":1500,"cargo":100,"count":8})
    w['ships'].append({"role":"miner","owner":"free","faction":f,"sector":sid,"credits":1000,"cargo":60,"count":6})
for sid in (12,14,19,21,24):
    w['ships'].append({"role":"miner","owner":"free","faction":1,"sector":sid,"credits":1000,"cargo":60,"count":2})
for f, sid in ((4,16),(5,20)):
    w['ships'].append({"role":"builder","faction":f,"sector":sid,"credits":1500,"cargo":160,"count":1,"model":"foreman"})
    w['ships'].append({"role":"police","faction":f,"sector":sid,"credits":0,"count":2,"model":"warden"})
w['ships'].append({"role":"pirate","owner":"reavers","sector":22,"credits":0,"count":2,"model":"corsair"})

json.dump(w, open(DST, 'w'), ensure_ascii=False, indent=1)
print('Sektoren', len(w['sectors']), 'Gates', len(w['gates']), 'Stationen', sum(len(s['stations']) for s in w['sectors']),
      'econ alt/neu', econ_old, econ_new, 'maxStations', r['maxStations'])
