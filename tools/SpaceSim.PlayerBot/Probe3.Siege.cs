using SpaceSim.Core.Combat;
using SpaceSim.Core.Comms;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.PlayerBot;

/// <summary>Test 8, Schwerpunkt 3: Belagerungs-Anflug. "siegefly": alle Kampfschiffe einzeln mit AttackCommand; "siegerally": erst Sammelpunkt-Befehl, danach Angriff.
/// Künstlich: die Schiffe werden an einen gemeinsamen Ausgangsort (Station mit hopsBack Sprüngen Abstand zum Ziel) gesetzt.</summary>
public static class ProbeSiege
{
    public static void Run(Universe u, Pilot p, Action<string> P, bool rally, int hopsBack, int shipsN)
    {
        var comms = CommsActions.Default();
        var st = u.StationsById().Where(x => !x.IsBuildSite && u.FactionOf(x) is { } ff && u.IsNpcFaction(ff) && !Bandits.IsEstablished(u, ff) && !StationProtection.IsHideout(u, x) && u.StationTypes[x.TypeKey].Government != GovernmentKind.Outpost && Wars.PlayerDeclareIssue(u, ff) is null)
            .OrderByDescending(x => StationCombat.MaxHull(u, x)).First();
        var f = u.FactionOf(st)!.Value;
        var ws = PlayerFleet.Owned(u).Where(s => s.Id != u.PlayerShip && s.Has(Capability.Combat) && u.ShipModels.TryGetValue(s.ModelKey, out var m) && m.Jobs.Count == 0 && !s.IsRental() && s.Stats.Hull >= 300f)
            .OrderByDescending(s => s.Stats.Hull * s.Stats.WeaponPower).Take(shipsN).ToList();
        P($"Ziel {st.Name} [{st.TypeKey}] {u.Sectors[st.Sector].Name} Fraktion {f.Value} Hülle {StationCombat.Hull(u, st):0}/{StationCombat.MaxHull(u, st):0} Geschütze {StationCombat.DefenseOf(u, st)?.Guns.Count ?? 0}");
        P($"Verband {ws.Count}: " + string.Join(" ", ws.Select(s => $"#{s.Id.Value}:{s.ModelKey}:v{Math.Round(s.Stats.Speed, 1)}")));
        var start = u.StationsById().Where(x => !x.IsBuildSite && u.Hops(x.Sector, st.Sector) >= hopsBack && x.Owner == u.PlayerActor && x.Sector != st.Sector).OrderBy(x => u.Hops(x.Sector, st.Sector)).FirstOrDefault()
            ?? u.StationsById().Where(x => !x.IsBuildSite && u.Hops(x.Sector, st.Sector) >= hopsBack && Docking.Issue(u, x, ws[0]) is null).OrderBy(x => u.Hops(x.Sector, st.Sector)).First();
        P($"Ausgangsort {start.Name} [{u.Sectors[start.Sector].Name}], {u.Hops(start.Sector, st.Sector)} Sprünge zum Ziel");
        foreach (var s in ws)
        {
            if (s.Squadron.Value != 0) { var sq0 = Squadrons.Get(u, s); if (sq0 is not null) Squadrons.Leave(u, s, out _); }
            Commands.Cancel(u, s); u.MoveShip(s, start.Sector); s.Position = start.DockPoint(s.Id); s.Transit = null; s.DockedAt = start.Id; s.Hull = s.MaxHull; s.Emergency = false;
        }
        P("Munition je Schiff (Magazin/Kapazität, Raketen): " + string.Join(" ", ws.Select(s => $"{s.AmmoLoaded:0}/{s.Stats.AmmoCapacity:0},{s.Stats.MissileCapacity - s.Systems.MissilesMissing:0}")));
        P($"[künstlich] {ws.Count} Kampfschiffe nach {start.Name} gesetzt, Hülle voll");
        P("Krieg: " + comms.Execute(u, $"war:{f.Value}").Message);
        int fell = 0; var arrive = new Dictionary<ShipId, long>(); var lost = new List<string>();
        u.Events.Raised += e => { if (e is PeaceConcluded or WarDeclared) P($"[{u.Tick}] {e}"); if (e is StationFell sf && sf.Station == st.Id) fell++; if (e is ShipDeparted sd && ws.Any(w => w.Id == sd.Ship)) lost.Add($"[{u.Tick}] #{sd.Ship.Value} {sd.Reason}"); };
        long t0 = u.Tick; bool taken = false;
        if (rally)
        {
            var rs = u.StationsById().Where(x => !x.IsBuildSite && x.Sector != st.Sector && u.Hops(x.Sector, st.Sector) == 1 && Docking.Issue(u, x, ws[0]) is null).OrderBy(x => u.Hops(start.Sector, x.Sector)).First();
            var ok = FleetFormations.StartRally(u, ws[0], ws.Skip(1), rs, out var msg); P($"Sammelpunkt {rs.Name} [{u.Sectors[rs.Sector].Name}]: {ok} {msg}");
            bool formed = false;
            for (int i = 0; i < 20000 && !formed; i++)
            {
                p.Step();
                if (ws.Any(s => s.Squadron.Value != 0)) formed = true;
                if (i % 600 == 0) P($"[+{u.Tick - t0}] sammeln: da {ws.Count(s => s.Sector == rs.Sector)}/{ws.Count}");
            }
            P($"[+{u.Tick - t0}] Verband gebildet: {formed}; Mitglieder im Sammelsektor {ws.Count(s => s.Sector == rs.Sector)}");
            t0 = u.Tick;
        }
        foreach (var s in ws.Where(x => u.Ships.ContainsKey(x.Id))) Commands.Give(u, s, new AttackCommand(default, st.Id, default, st.Sector), out _, ignoreCapacity: true);
        long firstIn = -1, lastIn = -1;
        for (int i = 0; i <= 14000; i++)
        {
            if (i % 600 == 0) P($"[+{u.Tick - t0}] Hülle Ziel {(u.Stations.TryGetValue(st.Id, out var c) ? StationCombat.Hull(u, c) : 0):0} {(u.Stations.TryGetValue(st.Id, out var c2) && StationCombat.IsDown(u, c2) ? "AUS" : "")} Krieg {(Wars.PlayerWar(u, f) is not null)} Besitzer {(u.Stations.TryGetValue(st.Id, out var c3) ? (c3.Owner == u.PlayerActor ? "Spieler" : c3.Owner.Value.ToString()) : "-")}; im Zielsektor {ws.Count(s => u.Ships.ContainsKey(s.Id) && s.Sector == st.Sector)} Munition {string.Join(" ", ws.Where(s => u.Ships.ContainsKey(s.Id)).Select(s => $"{s.AmmoLoaded:0}/{s.Stats.MissileCapacity - s.Systems.MissilesMissing:0}"))} Befehl {string.Join(",", ws.Where(s => u.Ships.ContainsKey(s.Id)).Select(s => s.Command is null ? "-" : s.Command.GetType().Name[..3]))} Hülle {string.Join("/", ws.Where(s => u.Ships.ContainsKey(s.Id)).Select(s => (int)(100 * s.Hull / s.MaxHull)))}%");
            foreach (var s in ws) if (u.Ships.ContainsKey(s.Id) && s.Sector == st.Sector && !arrive.ContainsKey(s.Id)) { arrive[s.Id] = u.Tick - t0; if (firstIn < 0) firstIn = u.Tick - t0; lastIn = u.Tick - t0; }
            p.Step();
            if (!taken && (!u.Stations.TryGetValue(st.Id, out var cc) || cc.Owner == u.PlayerActor || StationCombat.IsDown(u, cc))) { taken = true; P($"[+{u.Tick - t0}] Ziel gefallen/ausgeschaltet"); break; }
        }
        P($"ERGEBNIS {(rally ? "SAMMELN" : "EINZELN")}: Ziel gefallen {taken} nach {u.Tick - t0} Ticks; Ankunft im Zielsektor erstes {firstIn} letztes {lastIn} (Spanne {lastIn - firstIn}); Verband lebt {ws.Count(s => u.Ships.ContainsKey(s.Id))}/{ws.Count}; Verluste {string.Join(", ", lost)}");
    }
}
