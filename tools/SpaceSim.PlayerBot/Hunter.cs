using SpaceSim.Core.Combat;
using SpaceSim.Core.Comms;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Missions;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Weapons;

namespace SpaceSim.PlayerBot;

/// <summary>
/// Durchspiel 3 (Jäger-Start): Kampf-/Militärweg. Einnahmen nur aus Kopfgeldern (Büro), Kopfgeld-/Patrouillen-Aufträgen und Gefechten;
/// Kampfschiffe werden von den Einnahmen gekauft. Alles über Spielerpfade (Dock-Dienste, Gunnery, Comms). Kein Handelsflotten-Kauf.
/// </summary>
public sealed class Hunter
{
    public static bool On, Mix; public static long MixAt = 400_000;
    readonly Pilot P; readonly Universe U; readonly Log L; readonly Bot B; readonly Empire E;
    public int Kills, BountyMissionsDone, PatrolsDone, Buys, HuntsStarted, HuntsFailed, MissionsAccepted;
    public float BountyOffice;
    long _nextBuy, _nextHunt, _nextWait;
    public Hunter(Pilot p, Log l, Bot b, Empire e) { P = p; U = p.U; L = l; B = b; E = e; }
    float Wallet => P.Wallet;

    public bool Step()
    {
        var me = P.Me;
        // Verlust des Flaggschiffs: ins stärkste eigene Kampfschiff wechseln (nicht im Leihschiff weiterjagen)
        var best = PlayerFleet.Owned(U).Where(x => Mil(x) && x.Hull > x.MaxHull * 0.5f).OrderByDescending(Strength).FirstOrDefault();
        if (best is not null && best.Id != me.Id && (!Mil(me) || Strength(best) > Strength(me) * 1.25f)) { B.UseFlag(best.Id); me = P.Me; L.Event($"HUNTER Flaggschiff-Wechsel → #{best.Id.Value} {best.ModelKey}"); }
        foreach (var rs in PlayerFleet.Owned(U).Where(x => x.IsRental() && x.Id != P.Me.Id && x.DockedAt.Value != 0).ToList())
            if (U.Stations.TryGetValue(rs.DockedAt, out var rst) && P.Svc(rst, "rental") is { } rsv && P.Offer(rst, rsv, $"return:{rs.Id.Value}") is { Amount: > 0f })
            { var rr = P.Do(rst, rsv, $"return:{rs.Id.Value}"); L.Event($"HUNTER Leihschiff #{rs.Id.Value} zurückgegeben: {rr.Message}"); }
        if (me.Hull < me.MaxHull * 0.7f || Condition(me) || P.Here is null || NeedAmmo(me))
        {
            var st = P.NearestWith("repair");
            if (st is not null && P.GoToStation(st, out _)) { P.Refuel(); P.RepairHere(); ClaimHere(); MissionsHere(); }
            else if (P.Danger()) return true;
        }
        if (DoMissions()) return true;
        if (U.Tick >= _nextWait)
        {
            _nextWait = U.Tick + 1500;
            var office = OfficeWithOffers();
            if (office is not null && P.GoToStation(office, out _)) { P.Refuel(); P.RepairHere(); ClaimHere(); MissionsHere(); return true; }
        }
        if (U.Tick >= _nextBuy && TryBuy()) return true;
        if (U.Tick >= _nextHunt && Hunt()) return true;
        P.Run(300);
        return true;
    }

    /// <summary>Sektor mit Outlaw-Station (Versteck/Freistatt): wird nur mit starkem Verband betreten.</summary>
    bool Fortress(SectorId sec) => U.Sectors[sec].Stations.Any(id => U.Stations.TryGetValue(id, out var st) && !st.IsBuildSite && U.Actors.TryGetValue(st.Owner, out var oa) && oa.Kind == ActorKind.Outlaw && U.StationTypes[st.TypeKey].Defense is { } d && d.Guns.Count > 0);
    static bool Condition(Ship s) => s.Systems.Condition.Any(c => c < 0.7f) || s.Systems.Burning.Count > 0;
    bool NeedAmmo(Ship s) { try { return U.Rules.AmmoActive && s.Stats.AmmoCapacity > 0 && ShipSystems.Ammo(U, s) < s.Stats.AmmoCapacity * 0.4f; } catch { return false; } }

    // ───────── Büro und Aufträge ─────────

    Station? OfficeWithOffers()
    {
        var me = P.Me; Station? best = null; double bs = double.MaxValue;
        var a = U.Actors[U.PlayerActor];
        foreach (var st in U.StationsById())
        {
            if (st.IsBuildSite || !BountyMission.IsOfferStation(U, st)) continue;
            if (Docking.Issue(U, st, me) is not null) continue;
            var fac = U.FactionOf(st) ?? default;
            bool offers = U.MissionOffers.Any(o => o.At == st.Id && (o.Type == "bounty" || o.Type == "patrol"));
            bool claim = a.BountyClaims.Any(kv => kv.Value > 1f && kv.Key == fac);
            if (!offers && !claim) continue;
            var route = U.FindRoute(me.Sector, st.Sector, GateAccess.ProfileOf(U, me));
            if (route is null) continue;
            double sc = route.Count * 1000 + Vec2.Distance(me.Position, st.Position) - (claim ? 5000 : 0);
            if (sc < bs) { bs = sc; best = st; }
        }
        return best;
    }

    void ClaimHere()
    {
        var st = P.Here; if (st is null || P.Svc(st, "bounty") is not { } svc) return;
        if (U.FactionOf(st) is not { } fac) return;
        var a = U.Actors[U.PlayerActor];
        if (a.BountyClaims.GetValueOrDefault(fac) < 1f) return;
        float before = Wallet;
        var r = P.Do(st, svc, $"claim:{fac.Value}");
        L.Event($"HUNTER Kopfgeld abholen F{fac.Value}: {r.Message} Kasse {before:0}→{Wallet:0}");
        if (r.Success) { BountyOffice += Wallet - before; E.BountyClaimed += Wallet - before; E.BountyClaims++; }
    }

    void MissionsHere()
    {
        var st = P.Here; if (st is null || P.Svc(st, "missions") is not { } svc) return;
        int active = U.Missions.Count;
        foreach (var o in U.MissionOffers.Where(o => o.At == st.Id && (o.Type == "bounty" || o.Type == "patrol")).OrderByDescending(o => o.Reward).ToList())
        {
            if (active >= 2) break;
            if (o.Type == "bounty")
            {
                var t = BountyMission.TargetOf(new Mission { Args = new Dictionary<string, string>(o.Args) });
                if (!U.Ships.TryGetValue(t, out var tp) || Fortress(tp.Sector)) continue;
            }
            var r = P.Do(st, svc, $"accept:{o.Id}");
            L.Event($"HUNTER Auftrag {o.Type} {o.Id} (Belohnung {o.Reward:0}) annehmen: {(r.Success ? "ok" : r.Message)}");
            if (r.Success) { active++; MissionsAccepted++; }
        }
    }

    bool DoMissions()
    {
        foreach (var m in U.Missions.ToList())
        {
            if (m.Type == "bounty")
            {
                var t = BountyMission.TargetOf(m);
                if (!U.Ships.TryGetValue(t, out var prey) || Fortress(prey.Sector)) continue;
                float w0 = Wallet;
                L.Event($"HUNTER Kopfgeldauftrag {m.Id}: Ziel #{t.Value} in {U.Sectors[prey.Sector].Name}");
                bool dead = Engage(prey);
                if (dead) { BountyMissionsDone++; Kills++; }
                L.Event($"HUNTER Kopfgeldauftrag {m.Id}: {(dead ? "Ziel zerstört" : "nicht erledigt: " + _why)} Kasse {w0:0}→{Wallet:0}");
                return true;
            }
            if (m.Type == "patrol")
            {
                var wps = PatrolMission.WaypointsOf(m);
                int cur = PatrolMission.CurrentOf(m);
                if (cur >= wps.Count) continue;
                var (sec, pos) = wps[cur];
                if (!P.GoToSector(sec, out var why)) { if (!P.Danger()) { L.Event($"HUNTER Patrouille {m.Id}: Weg verwehrt {why}"); P.Run(100); } return true; }
                if (P.Me.DockedAt.Value != 0) P.Undock();
                if (!P.FlyTo(pos)) return true;
                P.Run(45);
                L.Event($"HUNTER Patrouille {m.Id}: Wegpunkt {cur + 1}/{wps.Count} geflogen");
                if (!U.Missions.Any(x => x.Id == m.Id)) PatrolsDone++;
                return true;
            }
        }
        return false;
    }

    // ───────── Jagd ─────────

    bool Hunt()
    {
        Ship? prey = null; int bh = 99;
        foreach (var s in U.ShipsById())
        {
            if (s.Role != ShipRole.Pirate || s.IsPlayer || s.Sector.Value == 0 || U.Fights.Of(s.Id) is not null || s.Hull <= 0) continue;
            bool wanted = U.Actors.TryGetValue(s.Owner, out var oa) && oa.Wanted.Keys.Any(f => U.IsNpcFaction(f));
            if (!wanted || Fortress(s.Sector) || s.DockedAt.Value != 0) continue;
            int pack = U.ShipsById().Count(x => x.Role == ShipRole.Pirate && x.Sector == s.Sector && x.DockedAt.Value == 0);
            if (pack >= 2 && FleetStrength() < 2.2f * Strength(s)) continue;
            int h = U.Hops(P.Me.Sector, s.Sector); if (h < 0 || h >= bh) continue;
            prey = s; bh = h;
        }
        _nextHunt = U.Tick + 600;
        if (prey is null) return false;
        FormSquad();
        HuntsStarted++;
        float b0 = U.Actors[U.PlayerActor].BountyClaims.Values.Sum();
        bool dead = Engage(prey);
        float b1 = U.Actors[U.PlayerActor].BountyClaims.Values.Sum();
        L.Event($"HUNTER Jagd #{prey.Id.Value} ({prey.ModelKey}) {(dead ? "zerstört" : "nicht erledigt: " + _why)}; Ansprüche {b0:0}→{b1:0}; eigene Hülle {P.Me.Hull:0}/{P.Me.MaxHull:0}");
        if (dead) Kills++; else HuntsFailed++;
        return true;
    }

    void FormSquad()
    {
        var me = P.Me;
        var mates = PlayerFleet.Owned(U).Where(s => s.Id != me.Id && Mil(s)).ToList();
        if (mates.Count == 0) return;
        var sq = Squadrons.Get(U, me) ?? Squadrons.Create(U, me, "Jagdverband", out _);
        if (sq is null) return;
        foreach (var s in mates.Where(s => s.Squadron.Value == 0).Take(U.Rules.SquadronMaxMembers - sq.Members.Count)) Squadrons.Join(U, sq, s, out _);
        Squadrons.SetRules(U, sq, Engagement.Attack);
    }

    string _why = "";
    bool Engage(Ship prey)
    {
        _why = ""; int shots = 0, fired = 0;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            if (!U.Ships.ContainsKey(prey.Id)) return true;
            if (Fortress(prey.Sector) || prey.DockedAt.Value != 0) { _why = "Ziel im Versteck/angedockt — warte"; P.Run(200); return false; }
            if (!P.GoToSector(prey.Sector, out var gwhy)) { _why = "Weg: " + gwhy; return false; }
            var me0 = P.Me; if (me0.DockedAt.Value != 0) P.Undock();
            if (SquadSize() > 0) P.Run(240);
            if (P.Danger()) P.Fight(400);
            if (!U.Ships.ContainsKey(prey.Id)) return true;
            if (prey.Sector == P.Me.Sector) break;
        }
        var me = P.Me; if (me.DockedAt.Value != 0) P.Undock(); me = P.Me;
        for (int round = 0; round < 14 && U.Ships.ContainsKey(prey.Id); round++)
        {
            me = P.Me;
            if (me.Hull < me.MaxHull * 0.3f) { _why = $"Hülle {me.Hull:0}"; return false; }
            var pos = PlayerCombat.PositionOf(U, prey.Id);
            if (pos is null || pos.Value.Sector != me.Sector) { if (!P.GoToSector(prey.Sector, out _)) return false; P.Run(60); continue; }
            if (me.DockedAt.Value != 0) { P.Undock(); me = P.Me; }
            var dir = pos.Value.Pos - me.Position;
            if (dir.Length > 60f)
            {
                if (!P.FlyTo(pos.Value.Pos - dir.Normalized() * 40f)) { P.Fight(400); continue; }
                me = P.Me; pos = PlayerCombat.PositionOf(U, prey.Id); if (pos is null) continue; dir = pos.Value.Pos - me.Position;
            }
            foreach (var sl in PlayerGunnery.WeaponSlots(U, me)) { var gs = PlayerGunnery.TryFire(U, me, sl, prey.Id, FiringArc.Bearing(dir.Length > 0.01f ? dir.Normalized() : new Vec2(0, -1))); shots++; if (gs.Result == GunResult.Fired) fired++; else _why = gs.Result + ":" + gs.Message; }
            P.Step(); P.Fight(400);
        }
        if (!U.Ships.ContainsKey(prey.Id)) return true;
        _why += $" [Schüsse {fired}/{shots}, Prey-Hülle {prey.Hull:0}, Prey-Sektor {U.Sectors[prey.Sector].Name}, ich {U.Sectors[P.Me.Sector].Name}]";
        return false;
    }

    int SquadSize() => Squadrons.Get(U, P.Me) is { } sq ? sq.Members.Count - 1 : 0;

    // ───────── Modul-Beschaffung für Großkampfschiffe ─────────
    long _nextHaul; public int Hauls, HaulFails;
    /// <summary>Fehlen der Werft Module für ein Großkampfschiff: Module bei den Werken kaufen, mit einem Frachter zur Werft fliegen und dort ins Regal verkaufen.</summary>
    bool HaulFor(string[] wishList)
    {
        if (U.Tick < _nextHaul) return false;
        _nextHaul = U.Tick + 1200;
        var yard = P.NearestWith("shipdealer", st => st.TypeKey == "shipyard"); if (yard is null) return false;
        var me = P.Me;
        foreach (var key in wishList)
        {
            if (ShipDealerService.Quote(U, yard, me, key) is not { } q || q.Buyable) continue;
            var hauler = PlayerFleet.Owned(U).Where(x => !x.IsRental() && x.Stats.Cargo >= 100f && q.Missing.All(mm => x.FreeUnits(U, mm.Good) >= MathF.Ceiling(mm.Units) + 0.1f) && x.UsedVolume(U) < 1f && x.Command is not null && x.Id != me.Id).OrderByDescending(x => x.Stats.Cargo).FirstOrDefault();
            if (hauler is null) { L.Event("HUNTER Modulbeschaffung: kein Frachter"); return false; }
            Commands.Cancel(U, hauler);
            B.UseFlag(hauler.Id);
            string miss = string.Join(", ", q.Missing.Select(m => $"{U.Goods[m.Good].Name} {m.Units:0.#}"));
            L.Event($"HUNTER Modulbeschaffung für {key} bei {yard.Name}: fehlt {miss}; Frachter #{hauler.Id.Value}");
            foreach (var need in q.Missing)
            {
                var good = U.Goods[need.Good];
                Station? src = null; double bs = double.MaxValue;
                foreach (var st in U.StationsById())
                {
                    if (st.IsBuildSite || !st.Produces(good.Id) || st.Stock(good.Id).Amount < 1f) continue;
                    if (U.FindRoute(P.Me.Sector, st.Sector, GateAccess.ProfileOf(U, P.Me)) is not { } rt) continue;
                    double sc = rt.Count * 1000 + st.Market[good.Id].Price;
                    if (sc < bs) { bs = sc; src = st; }
                }
                if (src is null) { L.Event($"HUNTER Modulbeschaffung: kein Werk mit {good.Name} auf Lager"); continue; }
                if (!P.GoToStation(src, out _)) continue;
                var svc = P.Svc(src, "trade"); if (svc is null) continue;
                float units = MathF.Ceiling(need.Units);
                L.Event($"HUNTER   Schiff jetzt #{P.Me.Id.Value} {P.Me.ModelKey} Laderaum {P.Me.Stats.Cargo:0} frei {P.Me.FreeUnits(U, good.Id):0.#} benötigt {units:0} Volumen/Einheit {good.VolumePerUnit}");
                var r = P.Do(src, svc, $"buy:{good.Id.Value}", good.Id, units);
                L.Event($"HUNTER   {good.Name} {units:0} bei {src.Name} kaufen: {(r.Success ? $"ok {-r.Credits:0} Cr" : r.Message)}");
                if (!r.Success) continue;
                if (!P.GoToStation(yard, out _)) break;
                var rs = P.Do(yard, P.Svc(yard, "trade")!, $"sell:{good.Id.Value}", good.Id, units);
                L.Event($"HUNTER   {good.Name} bei {yard.Name} ins Regal: {(rs.Success ? $"ok {rs.Credits:0} Cr" : rs.Message)}");
            }
            B.RestoreFlag();
            var q2 = ShipDealerService.Quote(U, yard, P.Me, key);
            L.Event($"HUNTER Modulbeschaffung {key}: danach {(q2 is { Buyable: true } ? "LIEFERBAR" : "noch nicht lieferbar: " + string.Join(", ", q2?.Missing.Select(m => $"{U.Goods[m.Good].Name} {m.Units:0.#}") ?? Array.Empty<string>()))}");
            if (q2 is { Buyable: true }) Hauls++; else HaulFails++;
            return true;
        }
        return false;
    }

    // ───────── Kauf ─────────

    float FleetStrength() => PlayerFleet.Owned(U).Where(Mil).Sum(x => Strength(x));
    bool Mil(Ship s) => s.Has(Capability.Combat) && !s.IsRental() && U.ShipModels.TryGetValue(s.ModelKey, out var m) && m.Jobs.Count == 0;
    static float Strength(Ship s) => CombatModels.Strength(s.Stats);

    bool TryBuy()
    {
        _nextBuy = U.Tick + 900;
        if (Mix && PlayerFleet.Owned(U).Count(s => s.Command is TradeCommand or MineCommand) < 12) return false;
        var fleet = PlayerFleet.Owned(U).Where(Mil).ToList();
        if (fleet.Count >= 26) return false;
        if (Wallet < 4000f) return false;
        var yard = P.NearestWith("shipdealer", st => st.TypeKey == "shipyard"); if (yard is null) return false;
        var svc = P.Svc(yard, "shipdealer"); if (svc is null) return false;
        var me = P.Me;
        ShipQuote? best = null; double bv = 0;
        double flagStr = Strength(me);
        foreach (var m in ShipDealerService.Models(U, yard))
        {
            if (m.Jobs.Count > 0 || ShipDealerService.Quote(U, yard, me, m.Key) is not { Buyable: true } q) continue;
            if (!q.Stats.Capabilities.HasFlag(Capability.Combat) || q.Stats.WeaponPower <= 0f) continue;
            if (q.Class.Size == SlotSize.XL && Wallet < 80000f) continue;
            if (q.Price > Wallet - 1000f) continue;
            double str = CombatModels.Strength(q.Stats);
            double v = Wallet > 150000f ? str : str / q.Price * (q.Class.Size >= SlotSize.L ? 1.3 : 1.0) + (str > flagStr * 1.4 ? 0.01 : 0);
            if (v > bv) { bv = v; best = q; }
        }
        if (Mix && Wallet > 200000f && HaulFor(new[] { "destroyer", "carrier", "bastion", "battlecruiser" })) return true;
        if (best is null) { L.Event($"HUNTER Kauf: nichts Lieferbares/Bezahlbares bei {yard.Name} (Kasse {Wallet:0})"); return false; }
        if (!P.GoToStation(yard, out _)) return true;
        P.Refuel(); P.RepairHere();
        var r = P.Do(yard, svc, $"buy:{best.Model.Key}");
        L.Event($"HUNTER Kauf {best.Model.Name} {best.Price:0} Cr: {(r.Success ? "ok" : r.Message)}");
        if (r.Success)
        {
            Buys++; B.Metrics.Purchases++;
            var strongest = PlayerFleet.Owned(U).Where(Mil).OrderByDescending(Strength).First();
            if (strongest.Id != P.Me.Id && Strength(strongest) > Strength(P.Me) * 1.25) { B.UseFlag(strongest.Id); L.Event($"HUNTER neues Flaggschiff #{strongest.Id.Value} {strongest.ModelKey}"); }
            else B.RestoreFlag();
        }
        return true;
    }
}
