using System.Globalization;
using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Missions;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Ausbau Punkt 10 — Aufträge „Schmuggel“ (Hehler/gesetzlose Firma, versiegelt, Sperrsektor, Kontrolle am Tor und durch die Polizei) und „Forschung“
/// (Messpunkte im Nebel, Wrackprobe, seltene Ware; Geld + FP), Angebote deterministisch aus dem Missions-RNG und ohne Hash, Speichern/Laden.
/// </summary>
public class SchmuggelForschungAuftragTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    internal static Universe World(bool research = false, ulong seed = 1)
    {
        var u = WorldBuilder.Build(TestWorlds.Def(fuel: false, research: research), seed);
        new SimulationRunner().Run(u, 2);   // Preise stehen erst nach dem ersten Takt
        // Auftraggeber mit Geld (am Anfang sind die Kassen leer; Angebote verlangen Tagesbudget und verfügbares Geld)
        foreach (var a in u.Actors.Values) if (a.Id != u.PlayerActor && a.Id != u.FreeActor) a.Treasury.Credits = Math.Max(a.Treasury.Credits, 500000f);
        return u;
    }

    /// <summary>Angebot einer Art an einer Station (erstes Bucket mit Angebot), eingetragen wie das Brett es tut.</summary>
    internal static MissionOffer Offer(Universe u, string type, Station st, Func<MissionOffer, bool>? want = null)
    {
        var t = MissionRules.Types.Get(type)!;
        for (long b = 0; b < 200; b++)
        {
            var o = t.Generate(u, st, MissionRng.For(u, st.Id.Value, b), b);
            if (o is null || (want is not null && !want(o))) continue;
            o.Id = u.NextMissionId++; o.At = st.Id; o.Client = st.Owner; o.Created = u.Tick; o.Expires = u.Tick + 10000;
            u.MissionOffers.Add(o);
            return o;
        }
        throw new Xunit.Sdk.XunitException($"kein Angebot {type} an {st.Name}");
    }

    static Station Fence(Universe u) => u.StationsById().First(s => BlackMarket.IsAt(u, s));

    [Fact]
    public void Schmuggel_NurBeimHehler_Deterministisch_HoheBelohnung()
    {
        var u = World();
        var fence = Fence(u);
        var t = MissionRules.Types.Get(SmuggleMission.KeyId)!;
        MissionOffer? a = null, b = null;
        for (long k = 0; k < 50 && a is null; k++) { a = t.Generate(u, fence, MissionRng.For(u, fence.Id.Value, k), k); if (a is not null) b = t.Generate(u, fence, MissionRng.For(u, fence.Id.Value, k), k); }
        Assert.NotNull(a);
        Assert.Equal(a!.Args, b!.Args);
        Assert.Equal(a.Reward, b.Reward);
        Assert.True(a.Reward >= u.Rules.MissionSmuggleBaseReward);
        Assert.Equal("station", a.Args["channel"]);
        // ehrliche Station bietet keinen Schmuggel
        var honest = u.StationsById().First(s => s.Dockable && !BlackMarket.IsAt(u, s) && u.FactionOf(s) is { } f && u.IsNpcFaction(f));
        for (long k = 0; k < 20; k++) Assert.Null(t.Generate(u, honest, MissionRng.For(u, honest.Id.Value, k), k));
    }

    [Fact]
    public void Schmuggel_Annehmen_Andocken_Liefert_BelohnungAusDerPiratenkasse()
    {
        var u = World();
        var fence = Fence(u);
        var o = Offer(u, SmuggleMission.KeyId, fence);
        var ship = u.Ships[u.PlayerShip];
        float w0 = u.Player.Treasury.Credits;
        var (m, issue) = MissionRules.Accept(u, o.Id, ship, remote: true);
        Assert.Null(issue);
        Assert.Equal(u.Rules.MissionSmuggleUnits, ship.MissionCargo, 2);
        var target = u.Stations[new StationId(int.Parse(o.Args["target"], CultureInfo.InvariantCulture))];
        u.MoveShip(ship, target.Sector);
        ship.Position = target.DockPoint(ship.Id);
        ship.DockedAt = target.Id;
        MissionSystem.OnEvent(u, new ShipDocked(ship.Id, target.Id));
        Assert.Empty(u.Missions);
        Assert.Equal(0f, ship.MissionCargo, 2);
        Assert.Equal(w0 + m!.Reserved, u.Player.Treasury.Credits, 1);
    }

    [Fact]
    public void Schmuggel_Polizei_Durchsucht_Beschlagnahme_Fahndung_Ansehen()
    {
        var u = World();
        var o = Offer(u, SmuggleMission.KeyId, Fence(u));
        var ship = u.Ships[u.PlayerShip];
        var (m, _) = MissionRules.Accept(u, o.Id, ship, remote: true);
        var police = u.ShipsById().First(s => s.Role == ShipRole.Police && SmuggleSearch.Where(u, s) is var w && u.Sectors[w.Sector].Owner.Value != 0 && u.FactionOf(s) == u.Sectors[w.Sector].Owner);
        var (psec, ppos) = SmuggleSearch.Where(u, police);
        var f = u.Sectors[psec].Owner;
        if (ship.DockedAt.Value != 0) PlayerFleet.Undock(u, ship);
        u.MoveShip(ship, psec);
        u.PlayerSector = psec;
        ship.Position = ppos + new Vec2(20f, 0f);
        u.Rules.SmugglerSearchChance = 1f;
        float rel0 = Relations.Effective(u, f, u.PlayerFaction!.Value);
        var found = new List<MissionContrabandFound>();
        u.Events.Raised += e => { if (e is MissionContrabandFound c) found.Add(c); };
        SmuggleMission t = (SmuggleMission)MissionRules.Types.Get(SmuggleMission.KeyId)!;
        t.Check(u, m!);
        Assert.Single(found);
        Assert.Empty(u.Missions);
        Assert.Equal(0f, ship.MissionCargo, 2);   // beschlagnahmt
        Assert.True(Wanted.LevelOf(u, u.PlayerActor, f) >= 1);
        Assert.True(Relations.Effective(u, f, u.PlayerFaction!.Value) < rel0 - 0.01f);
    }

    [Fact]
    public void Schmuggel_TorMitSperre_FindetUngetarnt_TarnfeldMk1SchlaegtDenTorsensor()
    {
        foreach (bool cloak in new[] { false, true })
        {
            var u = World();
            var o = Offer(u, SmuggleMission.KeyId, Fence(u));
            var ship = u.Ships[u.PlayerShip];
            MissionRules.Accept(u, o.Id, ship, remote: true);
            // Tor nach Beta (Sektor 2) aus Alpha mit Sperrstufe 1 in Beta
            var gate = u.Gates.Values.OrderBy(g => g.Id.Value).First(g => g.Sector == new SectorId(1) && u.Gates[g.Target].Sector == new SectorId(2));
            FactionPolicySystem.SetLevel(u, u.Sectors[new SectorId(2)], 1);
            Assert.True(u.Sectors[new SectorId(2)].Access.EntryLevel >= 1);
            if (ship.DockedAt.Value != 0) PlayerFleet.Undock(u, ship);
            if (cloak)
            {
                int slot = ship.Fit.FindIndex(x => x is null);
                if (slot < 0) { ship.Slots.Add(new ClassSlot { Type = "utility", Size = SlotSize.S }); ship.Fit.Add(null); slot = ship.Fit.Count - 1; }
                ship.Slots[slot] = new ClassSlot { Type = "utility", Size = SlotSize.S };
                ship.Fit[slot] = "cloak_s";
                ship.RecomputeStats(u);
                ship.Cloaked = true; ship.RecomputeStats(u);
            }
            GateAccess.TryPlayerJump(u, ship, gate);
            Assert.Equal(cloak ? 1 : 0, u.Missions.Count);
        }
    }

    [Fact]
    public void Forschung_Messpunkte_Wrackprobe_SelteneWare_GeldUndForschungspunkte()
    {
        foreach (var kind in new[] { "nebula", "goods", "wreck" })
        {
            var u = World(research: true);
            var lab = u.StationsById().First(s => ResearchMission.IsOfferStation(u, s));
            var faction = u.Factions[u.FactionOf(lab)!.Value];
            if (kind == "wreck")
            {
                var victim = u.ShipsById().First(s => s.Role == ShipRole.Trader);
                victim.Cargo[u.FindGood("steel")!.Id] = 20f;
                Departures.RemoveShip(u, victim, DepartureReason.Destroyed, "test");
                Assert.NotEmpty(u.Wrecks);
            }
            var o = Offer(u, ResearchMission.KeyId, lab, x => x.Args["kind"] == kind);
            var ship = u.Ships[u.PlayerShip];
            float w0 = u.Player.Treasury.Credits;
            var proj = Research.CurrentOf(u, faction)!;
            float fp0 = faction.Research.Progress.GetValueOrDefault(proj.Key);
            var (m, issue) = MissionRules.Accept(u, o.Id, ship, remote: true);
            Assert.Null(issue);
            var type = (ResearchMission)MissionRules.Types.Get(ResearchMission.KeyId)!;
            if (ship.DockedAt.Value != 0) PlayerFleet.Undock(u, ship);
            switch (kind)
            {
                case "nebula":
                    foreach (var (sec, pos) in PatrolMission.WaypointsOf(m!))
                    {
                        u.MoveShip(ship, sec); u.PlayerSector = sec; ship.Position = pos;
                        type.Check(u, m!);
                    }
                    break;
                case "goods":
                {
                    var good = new GoodId(int.Parse(m!.Args["good"], CultureInfo.InvariantCulture));
                    float units = float.Parse(m.Args["units"], CultureInfo.InvariantCulture);
                    u.MoveShip(ship, lab.Sector); ship.Position = lab.DockPoint(ship.Id); ship.DockedAt = lab.Id;
                    ship.Cargo[good] = units;
                    lab.Credits = 100000f;
                    var sr = TradeActions.Sell(u, ship, lab, good, units);   // PlayerSold → Fortschritt (MissionSystem hängt am Bus)
                    Assert.True(sr.Sold.GetValueOrDefault(good) >= units - 0.01f);
                    type.Check(u, m);
                    break;
                }
                case "wreck":
                {
                    var w = u.Wrecks.First(x => x.Id.ToString(CultureInfo.InvariantCulture) == m!.Args["wreck"]);
                    MissionSystem.OnEvent(u, new WreckSalvaged(w.Id, ship.Id, u.FindGood("steel")!.Id, 5f, null, 0f, false));
                    Assert.Equal("1", m!.Args["sampled"]);
                    u.MoveShip(ship, lab.Sector); ship.Position = lab.DockPoint(ship.Id); ship.DockedAt = lab.Id;
                    MissionSystem.OnEvent(u, new ShipDocked(ship.Id, lab.Id));
                    break;
                }
            }
            Assert.True(m!.State == MissionState.Completed, $"{kind}: {type.Describe(u, m)}");
            Assert.True(u.Player.Treasury.Credits >= w0 + m.Reserved - 0.5f, kind);
            bool done = faction.Research.IsCompleted(proj.Key);
            Assert.True(done || faction.Research.Progress.GetValueOrDefault(proj.Key) >= fp0 + u.Rules.MissionResearchPoints - 0.5f, kind);
        }
    }

    [Fact]
    public void Angebote_OhneHash_GleicheWeltMitUndOhneNeueArten()
    {
        Universe Run(bool on)
        {
            var def = TestWorlds.Def(fuel: false, research: true);
            def.Rules!.Smugglers = false;   // Schmuggler ändern die Welt — hier geht es nur um die Angebote
            def.Rules.MissionSmuggle = on; def.Rules.MissionResearch = on; def.Rules.MissionPassengers = on;
            var u = WorldBuilder.Build(def, 1);
            new SimulationRunner().Run(u, 5000);
            return u;
        }
        var a = Run(true); var b = Run(false);
        Assert.Contains(a.MissionOffers, o => o.Type is "smuggle" or "research" or "passenger");
        Assert.Equal(UniverseHash.Compute(b), UniverseHash.Compute(a));
    }

    [Fact]
    public void SpeichernLaden_MitSchmuggelUndForschung_Bitgleich()
    {
        var u = World(research: true);
        var ship = u.Ships[u.PlayerShip];
        MissionRules.Accept(u, Offer(u, SmuggleMission.KeyId, Fence(u)).Id, ship, remote: true);
        var lab = u.StationsById().First(s => ResearchMission.IsOfferStation(u, s));
        MissionRules.Accept(u, Offer(u, ResearchMission.KeyId, lab, x => x.Args["kind"] == "nebula").Id, ship, remote: true);
        Assert.Equal(2, u.Missions.Count);
        var (v, _) = SaveGameJson.Read(SaveGameJson.Write(u, Now));
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
        new SimulationRunner().Run(u, 500);
        new SimulationRunner().Run(v, 500);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
    }
}
