using SpaceSim.Core.Combat;
using SpaceSim.Core.Weapons;
using SpaceSim.Core.Comms;
using SpaceSim.Core.Events;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;

namespace SpaceSim.PlayerBot;

/// <summary>
/// Durchspiel 2 (Schürfer-Start, Ziel „alle Sektoren“): Strategie „Stationsflut“. Statt Krieg Einfluss kaufen: in jedem Sektor so viele billige Wirtschaftsstationen
/// bauen, dass der Einflussanteil der Spielerfraktion ≥ DomShare × Rest ist (Sektorbesitz folgt dem Einfluss, SectorControl). Dazu Diplomatie (Waffenstillstand/Frieden,
/// Bündnisse bei Beziehung ≥ 0,6) und Beziehungs-Messung. Nur Spielerpfade (Baufenster, Comms, Befehle).
/// </summary>
public sealed partial class Empire
{
    public static bool Dom, DomWar, Mixed, NoFlood; readonly Dictionary<FactionId, long> _donTry = new(); public static double DomShare = 2.4; public static int DomForemen = 24;
    public readonly List<string> DomLog = new();
    public static bool Refinery, NoStrip; public static int StripCap = 1000, FlagSwapMax = 100000;
    static string[] Flood => Refinery ? new[] { "solar_plant", "water_plant", "electrolysis", "powerplant", "fuel_refinery", "farm" } : new[] { "solar_plant", "water_plant", "electrolysis", "powerplant", "farm" };
    SectorId? _cur; long _curSince; readonly HashSet<SectorId> _done = new();
    int _floodIdx; long _domNext, _nextMapLine, _nextDiplo; readonly Dictionary<SectorId, long> _domFail = new(); readonly Dictionary<SectorId, string> _domWhy = new();
    readonly Dictionary<FactionId, long> _allyTry = new();
    void DNote(string s) { DomLog.Add($"[{U.Tick,8}] {s}"); Note(s); }

    void AssignForemen(List<Station> sites)
    {
        foreach (var sit in sites)
            if (!Foremen().Any(f => f.Command is BuildCommand bc && bc.Site == sit.Id))
                foreach (var f in Foremen().Where(f => f.Command is not BuildCommand && f.Id != U.PlayerShip))
                {
                    bool ok = Commands.Give(U, f, new BuildCommand(sit.Id), out var msg, ignoreCapacity: false);
                    if (!ok && msg.Contains("kapazität") && FreeSlot()) ok = Commands.Give(U, f, new BuildCommand(sit.Id), out msg, ignoreCapacity: false);
                    if (!ok) { if (U.Tick % 2000 < 40) DNote($"Bauschiff-Befehl #{f.Id.Value}: {msg}"); }
                    break;
                }
    }

    (float pw, float other, float total) Shares(Sector sec)
    {
        var inf = SectorControl.Compute(U, sec); var me = U.PlayerFaction!.Value;
        float pw = inf.Shares.GetValueOrDefault(me) * inf.Total;
        return (pw, inf.Total - pw, inf.Total);
    }

    void MapLine()
    {
        if (U.Tick < _nextMapLine) return; _nextMapLine = U.Tick + 20000;
        var me = U.PlayerFaction!.Value;
        var parts = U.Sectors.Values.OrderBy(s => s.Id.Value).Select(s => { var (pw, o, t) = Shares(s); return $"{s.Name.Replace("Sektor ", "")}:{(s.Owner == me ? "ICH" : s.Owner.Value == 0 ? "-" : s.Owner.Value.ToString())}({pw:0}/{t:0}{(s.Contested ? "!" : "")}{(s.Access.IsOpen ? "" : $"L{s.Access.EntryLevel}")})"; });
        DNote("KARTE " + string.Join(" ", parts) + $" | Stationen {PlayerBuilding.OwnEconomic(U).Count} Baustellen {PlayerBuilding.OwnSites(U).Count} Bauschiffe {Foremen().Count}");
    }

    bool Dominion()
    {
        if (!U.Rules.PlayerBuilding) return false;
        MapLine();
        var sites = PlayerBuilding.OwnSites(U);
        AssignForemen(sites);
        if (Rescue(sites)) return true;
        if (U.Tick < _domNext) return false;
        if (Foremen().Count < DomForemen && Wallet > 90000f) { BuyForeman(); _domNext = U.Tick + 300; return true; }
        if (Foremen().Count == 0) return false;
        var me = U.PlayerFaction!.Value;
        if (BanditFirst && !_banditDone) { if (BanditCampaign()) return true; if (!_banditDone && MilOn) return false; }
        // Ein Ziel nach dem anderen (sonst ärgern gleichzeitig umkämpfte Sektoren alle Fraktionen)
        Sector? tgt = null; int need = 0; string phase = "";
        if (_cur is { } cs && U.Sectors.TryGetValue(cs, out var csec))
        {
            var (pw0, o0, _) = Shares(csec);
            if (csec.Owner == me && pw0 >= DomShare * o0 * 0.95) { DNote($"Sektor {csec.Name} erledigt: Einfluss {pw0:0} gegen {o0:0}"); _cur = null; _done.Add(cs); }
            else if (_curSince > 0 && U.Tick - _curSince > 90000) { DNote($"Sektor {csec.Name} aufgegeben (zu lang): Einfluss {pw0:0} gegen {o0:0}"); _domFail[cs] = U.Tick; _cur = null; }
        }
        var blocked = new Dictionary<FactionId, int>();
        if (_cur is null)
        {
            double bs = double.MaxValue;
            foreach (var sec in U.Sectors.Values.OrderBy(s => s.Id.Value))
            {
                if (sec.Owner == me || _done.Contains(sec.Id)) continue;
                if ((NoFlood || S4) && U.IsNpcFaction(sec.Owner) && !(S4 && PlayerOwnIn(sec) > 0 && Wars.PlayerWar(U, sec.Owner) is null)) continue;
                if (_domFail.TryGetValue(sec.Id, out var ft) && U.Tick - ft < 15000) continue;
                if (U.IsNpcFaction(sec.Owner) && (!Bandit(sec.Owner) || !_banditPeaceFail) && Math.Min(Relations.Effective(U, U.PlayerActor, new ActorId(sec.Owner.Value)), Relations.Effective(U, new ActorId(sec.Owner.Value), U.PlayerActor)) < 0.02f) { blocked[sec.Owner] = blocked.GetValueOrDefault(sec.Owner) + 1; continue; }
                if (PlayerBuilding.SectorIssue(U, sec.Id) is { } why) { if (!_domWhy.TryGetValue(sec.Id, out var old) || old != why) { _domWhy[sec.Id] = why; DNote($"Bau in {sec.Name} gesperrt: {why}"); } continue; }
                int hops = U.Hops(P.Me.Sector, sec.Id); if (hops < 0) { if (!_domWhy.ContainsKey(sec.Id)) { _domWhy[sec.Id] = "kein Weg"; DNote($"{sec.Name}: kein Weg ({GateAccess.BlockReason(U, P.Me, P.Me.Sector, sec.Id)})"); } continue; }
                var (pw, o, t) = Shares(sec);
                double sc = (sec.Owner.Value == 0 ? -5 : 0) + o * 0.2 + hops;   // herrenlos und schwach zuerst
                if (U.IsNpcFaction(sec.Owner) && Math.Min(Relations.Effective(U, U.PlayerActor, new ActorId(sec.Owner.Value)), Relations.Effective(U, new ActorId(sec.Owner.Value), U.PlayerActor)) < 0.02f) { blocked[sec.Owner] = blocked.GetValueOrDefault(sec.Owner) + 1; continue; }   // Beziehung erst heben
                if (sc < bs) { bs = sc; _cur = sec.Id; }
            }
            if (_cur is { } nc) { _curSince = U.Tick; DNote($"Neues Ziel: {U.Sectors[nc].Name} (Besitzer {U.Sectors[nc].Owner.Value})"); }
        }
        if (_cur is null && (blocked.Count == 0 || _banditPeaceFail && blocked.Keys.All(k => Bandit(k)))) { if (BanditCampaign()) return true; }
        if (_cur is null && blocked.Count == 0 && DomSeats()) return true;
        if (_cur is null) { if (blocked.Count > 0) return Repair(blocked.OrderByDescending(k => Relations.Effective(U, U.PlayerActor, new ActorId(k.Key.Value))).First().Key); return false; }
        tgt = U.Sectors[_cur.Value];
        {
            var (pw, o, t) = Shares(tgt);
            int pend = sites.Count(s => s.Sector == tgt.Id);
            double goal = Math.Max(DomShare * o, 6.0), quiet = Math.Max(0.40 * o, 2.0);
            if (pw + pend < Math.Floor(quiet)) { need = (int)Math.Floor(quiet) - (int)Math.Round(pw) - pend; phase = "leise"; }
            else if (pend == 0 || tgt.Owner == me) { need = (int)Math.Ceiling(goal - pw - pend); phase = "Schub"; }
        }
        int maxSites = phase == "Schub" ? Foremen().Count + 12 : Foremen().Count + 2;
        if (need <= 0 || sites.Count >= maxSites) return false;
        var t0 = U.StationTypes[Flood[_floodIdx % Flood.Length]];
        float cost = PlayerBuilding.Cost(U, t0);
        if (Wallet < cost * 3 + 60000f) return false;
        if (!P.GoToSector(tgt.Id, out var gw)) { if (!P.Danger()) { _domFail[tgt.Id] = U.Tick; DNote($"Weg nach {tgt.Name} verwehrt: {gw}"); } return true; }
        if (P.Me.DockedAt.Value != 0) P.Undock();
        int placed = 0;
        for (int k = 0; k < Math.Min(need, phase == "Schub" ? 40 : 8) && PlayerBuilding.OwnSites(U).Count < maxSites && Wallet > cost + 40000f; k++)
        {
            var type = U.StationTypes[Flood[_floodIdx % Flood.Length]]; _floodIdx++;
            if (PlayerBuilding.TypeIssue(U, type) is { } ti) { DNote($"Flut {type.Key} in {tgt.Name}: {ti}"); _domFail[tgt.Id] = U.Tick; break; }
            var me2 = P.Me; Vec2? spot = null;
            float half = StationPlacement.HalfArea(U.Sectors[tgt.Id]) - 20f, step = 20f;
            var pts = new List<(float d, Vec2 p)>();
            for (float gx = -half; gx <= half; gx += step) for (float gy = -half; gy <= half; gy += step) { var p = new Vec2(gx, gy); pts.Add((Vec2.Distance(p, me2.Position), p)); }
            foreach (var (_, p) in pts.OrderBy(t => t.d)) if (PlayerBuilding.PlaceIssue(U, type, p) is null) { spot = p; break; }
            if (spot is null) { DNote($"Flut {type.Key}: kein freier Platz in {tgt.Name}"); _domFail[tgt.Id] = U.Tick; break; }
            P.Book("Baustelle", () => PlayerBuilding.Start(U, type, spot.Value, 0f)); placed++;
            // das Schiff am Rand lassen: neuer Standpunkt neben dem Platz, damit die nächste Baustelle nicht am Schiff scheitert
            me2.Position = spot.Value + new Vec2(type.Radius + 14f, 0f);
        }
        if (placed > 0) { var (pw, o, t) = Shares(tgt); DNote($"Flut {tgt.Name} (Besitzer {tgt.Owner.Value}): {placed} Baustellen gestartet ({phase}), Einfluss Spieler {pw:0} gegen {o:0}, Ziel {DomShare * o:0}, Bauschiffe {Foremen().Count}, Kasse {Wallet:0}, Beziehung {Relations.Effective(U, U.PlayerActor, new ActorId(tgt.Owner.Value)):0.00}"); }
        _domNext = U.Tick + (placed > 0 ? 200 : 600);
        return true;
    }

    // ───────── Diplomatie ─────────

    bool Diplo()
    {
        if (U.Tick < _nextDiplo) return false; _nextDiplo = U.Tick + 500;
        if (U.PlayerFaction is not { } me) return false;
        bool acted = false;
        foreach (var w in U.Wars.Where(w => w.Involves(me) && U.IsNpcFaction(w.Other(me))).ToList())
        {
            var f = w.Other(me);
            if (CampaignActive && (!S4 || f == _enemy)) continue;
            var pz = P.Book("Frieden", () => C.Execute(U, $"peace:{f.Value}"));
            DNote($"Krieg mit {f.Value} seit {w.Since}: Frieden: {pz.Message}");
            if (Wars.PlayerWar(U, f) is not null) { var c = P.Book("Waffenstillstand", () => C.Execute(U, $"ceasefire:{f.Value}")); DNote($"Waffenstillstand {f.Value}: {c.Message}"); }
            acted = true;
        }
        if (Mixed && !NoDonate && Wallet > 300000f)
            foreach (var f in U.NpcFactionIds())
            {
                if (Bandit(f)) continue;
                float rr = Math.Min(Relations.Effective(U, U.PlayerActor, new ActorId(f.Value)), Relations.Effective(U, new ActorId(f.Value), U.PlayerActor));
                if (rr >= 0.45f || (rr < -0.3f && !(S4 && Wars.PlayerWar(U, f) is null)) || (_donTry.TryGetValue(f, out var dt) && U.Tick - dt < 2400)) continue;
                _donTry[f] = U.Tick;
                var amt = U.Rules.DonationAmounts.Where(a => a <= Wallet * 0.05f).DefaultIfEmpty(0f).Max(); if (amt <= 0) continue;
                var dr = P.Book("Spende", () => C.Execute(U, $"donate:{f.Value}:{amt:0}"));
                DNote($"Spende {amt:0} an {f.Value} (Beziehung {rr:0.00}): {dr.Message}");
                acted = true;
            }
        foreach (var f in U.NpcFactionIds())
        {
            if (Bandit(f) || Alliances.Allied(U, me, f)) continue;
            float r1 = Relations.Effective(U, U.PlayerActor, new ActorId(f.Value)), r2 = Relations.Effective(U, new ActorId(f.Value), U.PlayerActor);
            if (Math.Min(r1, r2) < 0.55f) continue;
            if (_allyTry.TryGetValue(f, out var tt) && U.Tick - tt < 6000) continue;
            _allyTry[f] = U.Tick;
            var r = P.Book("Bündnis", () => C.Execute(U, $"alliance:propose:{f.Value}"));
            DNote($"Bündnis vorschlagen {f.Value} (Beziehung {r1:0.00}/{r2:0.00}): {r.Message}");
            acted = true;
        }
        return acted;
    }

    // ───────── Beziehungsarbeit: Aufträge (Transport) der Fraktion erledigen, +0,02 je Auftrag ─────────
    readonly Dictionary<FactionId, long> _repFail = new(); public int RepairJobs; long _repLast; FactionId _repF; float _repRel0;
    bool Repair(FactionId f)
    {
        if (_repFail.TryGetValue(f, out var ft) && U.Tick - ft < 3000) return false;
        float r = Relations.Effective(U, U.PlayerActor, new ActorId(f.Value));
        if (_repF != f) { _repF = f; _repRel0 = r; DNote($"Beziehungsarbeit mit Fraktion {f.Value}: Beziehung {r:0.00}"); }
        var st = P.NearestWith("missions", s => U.FactionOf(s) == f && !s.IsBuildSite);
        if (st is null) { DNote($"Beziehungsarbeit {f.Value}: keine erreichbare Station mit Aufträgen"); _repFail[f] = U.Tick; if (Bandit(f) && ++_repFails >= 3) { _banditPeaceFail = true; DNote("Banditen: Beziehungsarbeit aussichtslos — Kampagne"); } return false; }
        if (!P.GoToStation(st, out var why)) { if (!P.Danger()) { DNote($"Beziehungsarbeit {f.Value}: Weg zu {st.Name} verwehrt: {why}"); _repFail[f] = U.Tick; if (Bandit(f) && ++_repFails >= 3) { _banditPeaceFail = true; DNote("Banditen: Beziehungsarbeit aussichtslos — Kampagne"); } } return true; }
        B.X.MinRate = 0.2;
        float before = Relations.Effective(U, U.PlayerActor, new ActorId(f.Value));
        bool did = B.X.Missions();
        B.X.MinRate = 6.0;
        if (did) { RepairJobs++; _repLast = U.Tick; float after = Relations.Effective(U, U.PlayerActor, new ActorId(f.Value)); if (RepairJobs % 5 == 0) DNote($"Beziehungsarbeit {f.Value}: {RepairJobs} Aufträge, Beziehung {_repRel0:0.00}→{after:0.00}"); return true; }
        // nichts angenommen: warten, bis die Station neue Angebote hat
        P.Run(300); _repFail[f] = U.Tick - 2400; if (Bandit(f) && ++_repFails >= 600) { _banditPeaceFail = true; DNote("Banditen: keine Aufträge — Kampagne"); } return true;
    }

    // ───────── Banditen: Stationen belagern, bis sie ausgeschaltet sind (Banditen-Sitz kapituliert nie; Abgrund wird erst dann herrenlos) ─────────
    public static bool MilOn, BanditFirst, BanditOnly; int _banditEmpty; bool _banditDone = NoFlood, _banditPeaceFail; int _repFails; long _nextBandit; int _banditRounds; public readonly List<string> BanditLog = new();
    bool BanditCampaign()
    {
        if (_banditDone || U.Tick < _nextBandit) return false;
        if (S4 && !BanditOnly && U.Sectors.Values.Any(sc9 => U.IsNpcFaction(sc9.Owner) && !Bandits.IsEstablished(U, sc9.Owner))) return false;
        if (U.PlayerFaction is { } mf0 && U.Sectors.Values.All(sc0 => sc0.Owner == mf0)) { _banditDone = true; return false; }
        var bf = U.NpcFactionIds().Where(f => Bandits.IsEstablished(U, f)).Select(f => (FactionId?)f).FirstOrDefault();
        if (bf is null) { _banditDone = true; return false; }
        var targets = U.StationsById().Where(s => !s.IsBuildSite && U.FactionOf(s) == bf && !StationCombat.IsDown(U, s) && U.StationTypes[s.TypeKey].Government != GovernmentKind.Outpost).ToList();
        if (targets.Count == 0)
        {
            // Test 7: ausgeschaltet heißt nicht erledigt — der Sitz repariert sich (ab 25 % Hülle ohne Belagerung, ≈ 1800 Ticks), die Banditen bauen nach.
            // Fenster nutzen: solange alles aus ist, Baustellen im Sektor setzen (laufende Baustellen bleiben auch nach der Rückkehr der Banditen gültig).
            var abg = U.Sectors.Values.FirstOrDefault(sc => sc.Stations.Any(id => U.Stations.TryGetValue(id, out var hs) && U.FactionOf(hs) == bf && hs.TypeKey == "bandit_seat"));
            ++_banditEmpty;
            var me0 = U.PlayerFaction;
            if (abg is null || (me0 is { } mm && abg.Owner == mm) || _banditEmpty > 600) { _banditDone = true; DNote($"Banditen: erledigt (Prüfung {_banditEmpty}, Sektor {abg?.Name} Besitzer {abg?.Owner.Value})"); return false; }
            int pend = PlayerBuilding.OwnSites(U).Count(s2 => s2.Sector == abg.Id), fin = PlayerBuilding.OwnEconomic(U).Count(s2 => s2.Sector == abg.Id);
            if (_banditEmpty % 10 == 1) DNote($"Banditenstationen aus (Prüfung {_banditEmpty}): Sektor {abg.Name} Besitzer {abg.Owner.Value}, Einfluss {string.Join(" ", abg.Influence.Select(kv => $"{kv.Key.Value}:{kv.Value:0.00}"))}, eigene Baustellen {pend}, eigene Stationen {fin}");
            if (PlayerBuilding.SectorIssue(U, abg.Id) is null && pend + fin < 14 && Wallet > 400000f)
            {
                if (P.Me.Sector != abg.Id && !P.GoToSector(abg.Id, out _)) { _nextBandit = U.Tick + 600; return true; }
                if (P.Me.DockedAt.Value != 0) P.Undock();
                int placed = PlaceFlood(abg, 14 - pend - fin);
                if (placed > 0) DNote($"Banditen-Fenster: {placed} Baustellen in {abg.Name} gesetzt (Besitzer {abg.Owner.Value})");
            }
            P.Run(200); _nextBandit = U.Tick + 300; return true;
        }
        var mil = PlayerFleet.Owned(U).Where(s => s.Has(Capability.Combat) && U.ShipModels.TryGetValue(s.ModelKey, out var m) && m.Jobs.Count == 0 && !s.IsRental()).ToList();
        int big = mil.Count(s => U.ShipClasses.TryGetValue(s.ClassKeyOf(U), out var c) && c.Size >= SlotSize.L);
        // Großkampfschiffe (Hülle ≥ 600) statt vieler Kleiner: Stationsgeschütze töten kleine Schiffe, der Bot-Standardkauf (Waffenkraft je Preis) kauft nur Wächter/Korsaren
        var caps = mil.Where(x => x.Stats.Hull >= 600f).ToList();
        if (caps.Count < CapitalWanted) { BuyCapital(); _nextBandit = U.Tick + 300; return true; }
        if (Wallet < 200000f) return false;
        var leader = caps.OrderByDescending(s => (s.Hull >= s.MaxHull * 0.9f ? 1000f : 0f) + s.Stats.WeaponPower + s.Stats.Hull * 0.02f).First();
        B.UseFlag(leader.Id);
        try
        {
            if (P.Me.Id != leader.Id) { P.Run(20); if (P.Me.Id != leader.Id) { DNote("Banditen: Wechsel ins Führungsschiff fehlgeschlagen"); _nextBandit = U.Tick + 3000; return true; } }
            Heal();
            var sq0 = Squadrons.Get(U, P.Me) ?? Squadrons.Create(U, P.Me, "Banditenjäger", out _);
            if (sq0 is not null)
            {
                foreach (var c in caps.Where(c => c.Id != P.Me.Id && c.Squadron.Value == 0).OrderByDescending(c => c.Stats.WeaponPower).Take(U.Rules.SquadronMaxMembers - sq0.Members.Count)) Squadrons.Join(U, sq0, c, out _);
                Squadrons.SetRules(U, sq0, Engagement.Attack);
            }
            var st = targets.OrderBy(x => U.StationTypes[x.TypeKey].Defense?.Guns.Count ?? 0).ThenBy(x => U.StationTypes[x.TypeKey].Defense?.Hull ?? 0f).First();
            if (!P.GoToSector(st.Sector, out var why)) { if (!P.Danger()) { DNote($"Banditen: Weg zu {U.Sectors[st.Sector].Name} verwehrt: {why}"); _nextBandit = U.Tick + 6000; } return true; }
            if (P.Me.DockedAt.Value != 0) P.Undock();
            if (_banditRounds == 0 || U.Tick - _lastGather > 20000) { P.Run(1500); _lastGather = U.Tick; }
            var me = P.Me;
            var slots = PlayerGunnery.WeaponSlots(U, me);
            if (slots.Count == 0) { DNote("Banditen: Führungsschiff ohne Waffen"); _nextBandit = U.Tick + 5000; return true; }
            float reach = slots.Select(sl => Gunnery.WeaponAt(U, me, sl)).Where(m => m is not null).Select(m => WeaponProfile.Of(m!).Range).DefaultIfEmpty(40f).Min();
            int ticks = 0; var cnt = new Dictionary<string, int>(); string lastMsg = ""; float hull0 = StationCombat.Hull(U, st);
            while (ticks++ < 800 && U.Stations.TryGetValue(st.Id, out var cur) && !StationCombat.IsDown(U, cur) && U.FactionOf(cur) == bf)
            {
                me = P.Me;
                if (me.Hull < me.MaxHull * 0.25f) { _banditRounds += 3; break; }
                if (BanditOnly && ticks % 100 == 1 && PlayerCombat.PositionOf(U, me.Id) is not null) { int wing = OrderMilWing(st); if (ticks == 1 && wing > 0) DNote($"Banditen: {wing} Schiffe des Verbands greifen {st.Name} an"); }
                var dir = st.Position - me.Position; float d = dir.Length; float want = reach * 0.7f + st.Radius;
                if (d > want) me.Position += dir.Normalized() * MathF.Min(d - want, 11f);
                float bearing = FiringArc.Bearing(dir.Length > 0.01f ? dir.Normalized() : new Vec2(0, -1));
                foreach (var sl in slots) { var gs = PlayerGunnery.TryFireAtStation(U, me, sl, st.Id, bearing); cnt[gs.Result.ToString()] = cnt.GetValueOrDefault(gs.Result.ToString()) + 1; if (gs.Result != GunResult.Fired) lastMsg = gs.Message; }
                P.Step(); P.TicksFighting++;
                if (!U.Ships.ContainsKey(me.Id)) break;
            }
            _banditRounds++;
            bool down = !U.Stations.TryGetValue(st.Id, out var a2) || StationCombat.IsDown(U, a2) || U.FactionOf(a2) != bf;
            string line = $"Banditenbelagerung Runde {_banditRounds}: {st.Name} ({st.TypeKey}) Schüsse {string.Join(" ", cnt.Select(k => $"{k.Key}={k.Value}"))}; Hülle {hull0:0}→{(U.Stations.TryGetValue(st.Id, out var a3) ? StationCombat.Hull(U, a3) : 0):0}; {(down ? "AUSGESCHALTET" : "steht noch")}; letzte Meldung: {lastMsg}; Schiff {P.Me.ModelKey} Hülle {P.Me.Hull:0}/{P.Me.MaxHull:0}; Beziehung zu Fraktion {bf.Value.Value}: {Relations.Effective(U, U.PlayerActor, new ActorId(bf.Value.Value)):0.00}";
            BanditLog.Add(line); DNote(line);
            _nextBandit = U.Tick + (down ? 200 : 1500);
            if (_banditRounds > (BanditOnly ? 200 : 40)) { _banditDone = true; DNote("Banditenkampagne aufgegeben"); }
        }
        finally { B.RestoreFlag(); }
        return true;
    }

    public static int CapitalWanted = 8; long _lastGather;
    public bool BuyCapital()
    {
        var yards = U.StationsById().Where(x => !x.IsBuildSite && x.TypeKey == "shipyard" && Docking.Issue(U, x, P.Me) is null && U.FindRoute(P.Me.Sector, x.Sector, GateAccess.ProfileOf(U, P.Me)) is not null)
            .OrderBy(x => U.FindRoute(P.Me.Sector, x.Sector, GateAccess.ProfileOf(U, P.Me))!.Count).ToList();
        Station? yard = null; ShipQuote? pick = null; double pv = 0; var why = new List<string>();
        foreach (var y in yards)
        {
            double bestV = 0; ShipQuote? bp = null;
            foreach (var m in ShipDealerService.Models(U, y))
            {
                if (m.Jobs.Count > 0 || ShipDealerService.Quote(U, y, P.Me, m.Key) is not { Buyable: true } q) continue;
                if (!q.Stats.Capabilities.HasFlag(Capability.Combat) || q.Stats.WeaponPower <= 0f || q.Stats.Hull < 600f || q.Price > Wallet * (S4 ? 0.3f : 0.2f)) continue;
                double v = q.Stats.WeaponPower * (q.Stats.Hull + q.Stats.Shield * 2);
                if (v > bestV) { bestV = v; bp = q; }
            }
            if (bp is not null) { yard = y; pick = bp; pv = bestV; break; }
            why.Add(y.Name);
        }
        if (yard is null || pick is null) { DNote($"Großkampfschiff: bei keiner Werft lieferbar ({string.Join(", ", why)})"); return false; }
        if (P.Svc(yard, "shipdealer") is not { } svc) return false;
        if (!P.GoToStation(yard, out _)) return false;
        P.Refuel(); P.RepairHere();
        var r = P.Do(yard, svc, $"buy:{pick.Model.Key}");
        DNote($"Großkampfschiff {pick.Model.Name} {pick.Price:0} Cr bei {yard.Name} (Hülle {pick.Stats.Hull:0}, Waffenkraft {pick.Stats.WeaponPower:0}): {(r.Success ? "ok" : r.Message)}");
        B.RestoreFlag();
        return r.Success;
    }

    // ───────── Sektorbehörden in allen eigenen Sektoren (Kontrollstufe Dominanz/Kontrolle, Sperrrechte) ─────────
    long _nextSeat; readonly Dictionary<SectorId, long> _seatFail2 = new();
    bool DomSeats()
    {
        if (U.Tick < _nextSeat || U.PlayerFaction is not { } me) return false;
        var type = PlayerSeats.SeatType(U); if (type is null) return false;
        float cost = PlayerSeats.Cost(U, type);
        var seats = PlayerSeats.Seats(U);
        var cand = U.Sectors.Values.Where(sec => sec.Owner == me && seats.All(x => x.Sector != sec.Id) && !sec.Stations.Any(id => U.Stations.TryGetValue(id, out var st) && StationProtection.IsHideout(U, st))
            && (!_seatFail2.TryGetValue(sec.Id, out var ft) || U.Tick - ft > 20000)).OrderBy(sec => U.Hops(P.Me.Sector, sec.Id) is var h && h < 0 ? 99 : h).ToList();
        if (cand.Count == 0 || Wallet < cost + 200000f) return false;
        var tgt = cand[0];
        if (!P.GoToSector(tgt.Id, out var why)) { if (!P.Danger()) { _seatFail2[tgt.Id] = U.Tick; DNote($"Behörde {tgt.Name}: Weg verwehrt {why}"); } return true; }
        if (P.Me.DockedAt.Value != 0) P.Undock();
        if (PlayerSeats.FoundIssue(U, tgt.Id) is { } iss) { _seatFail2[tgt.Id] = U.Tick; DNote($"Behörde in {tgt.Name} nicht möglich: {iss}"); return true; }
        var r = P.Book("Behörde gründen", () => C.Execute(U, $"seat:found:{tgt.Id.Value}"));
        var sc = U.Sectors[tgt.Id];
        DNote($"Behörde {tgt.Name}: {r.Message} -> Stufe {sc.Control}, Einfluss {string.Join(" ", sc.Influence.Select(kv => $"{kv.Key.Value}:{kv.Value:0.00}"))}, MaxSperrstufe {SectorControl.MaxLevel(U, sc)}");
        _nextSeat = U.Tick + 300;
        return true;
    }

    int PlaceFlood(Sector tgt, int n)
    {
        int placed = 0;
        for (int k = 0; k < n && Wallet > 60000f; k++)
        {
            var type = U.StationTypes[Flood[_floodIdx % Flood.Length]]; _floodIdx++;
            if (PlayerBuilding.TypeIssue(U, type) is { } ti) { DNote($"Flut {type.Key} in {tgt.Name}: {ti}"); break; }
            var me2 = P.Me; Vec2? spot = null;
            float half = StationPlacement.HalfArea(U.Sectors[tgt.Id]) - 20f, step = 20f;
            var pts = new List<(float d, Vec2 p)>();
            for (float gx = -half; gx <= half; gx += step) for (float gy = -half; gy <= half; gy += step) { var p = new Vec2(gx, gy); pts.Add((Vec2.Distance(p, me2.Position), p)); }
            foreach (var (_, p) in pts.OrderBy(t => t.d)) if (PlayerBuilding.PlaceIssue(U, type, p) is null) { spot = p; break; }
            if (spot is null) { DNote($"Flut {type.Key}: kein freier Platz in {tgt.Name}"); break; }
            P.Book("Baustelle", () => PlayerBuilding.Start(U, type, spot.Value, 0f)); placed++;
            me2.Position = spot.Value + new Vec2(type.Radius + 14f, 0f);
        }
        return placed;
    }

    bool Bandit(FactionId f) => Bandits.IsEstablished(U, f);
}
