using SpaceSim.Core.Comms;
using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Intel;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Ausbau Punkt 16: Erkundung und Intel — Startwissen, Aufdecken (Betreten, Sensoren, Andocken, Taxi, Auftrag, Begegnung), Befehl „Erkunden“ auf beiden
/// Ebenen, Intel kaufen (Comms und Behörde, Verweigerungen, Geldereignisse), Filter der Spieleransichten, Speichern/Laden, NPC-Welt unverändert, Schalter aus.
/// </summary>
public class ErkundungTests
{
    static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    /// <summary>Standardwelt (feste Namen) mit Startpaket; Erkundung an.</summary>
    static Universe World(string start = "trader", bool intel = true)
    {
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        def.RandomNames = false;
        def.Rules!.PlayerIntel = intel;
        return WorldBuilder.Build(def, null, start);
    }

    static Ship Player(Universe u) => u.Ships[u.PlayerShip];

    static Ship Own(Universe u, string model, Station at)
    {
        var m = u.ShipModels[model];
        var s = ShipPurchase.Create(u, u.NewShipId(), m.Key, u.ShipClasses[m.ClassKey].Slots, m.Fit, ShipRole.Player, Controller.Player, u.PlayerActor, at, 0f);
        s.HomeBase = at.Id;
        u.AddShip(s);
        s.RecomputeStats(u);
        return s;
    }

    static List<SimEvent> Capture(Universe u) { var l = new List<SimEvent>(); u.Events.Raised += l.Add; return l; }

    static void Scan(Universe u) => new IntelSystem().Step(u, IntelSystem.Interval);

    static IEnumerable<SectorId> Neighbours(Universe u, SectorId s) =>
        u.Sectors[s].Gates.Select(g => u.Gates[u.Gates[g].Target].Sector);

    [Fact]
    public void Start_KenntStartsektorNachbarnUndBesitzer_SonstNichts()
    {
        var u = World();
        var i = Knowledge.Of(u);
        Assert.NotNull(i);
        var start = u.PlayerSector;
        Assert.Equal(SectorKnowledge.Visited, i!.StateOf(start));
        foreach (var n in Neighbours(u, start)) Assert.Equal(SectorKnowledge.Seen, i.StateOf(n));
        var others = u.Sectors.Keys.Where(s => s != start && !Neighbours(u, start).Contains(s)).ToList();
        Assert.NotEmpty(others);
        Assert.All(others, s => Assert.False(Knowledge.KnowsSector(u, s)));
        Assert.All(u.Sectors[start].Stations, st => Assert.True(Knowledge.KnowsStation(u, st)));
        Assert.All(u.Stations.Values.Where(s => s.Sector != start), st => Assert.False(Knowledge.KnowsStation(u, st)));
        Assert.Equal(0, Knowledge.UnknownIn(u, start));
        var owner = u.Sectors[start].Owner;
        Assert.True(Knowledge.KnowsFaction(u, owner));
        Assert.Contains(u.NpcFactionIds(), f => !Knowledge.KnowsFaction(u, f));
        Assert.True(Knowledge.KnowsFaction(u, u.PlayerFaction!.Value));   // die eigene immer
        Assert.Equal("?", Knowledge.SectorName(u, others[0]));
    }

    [Fact]
    public void Schalter_UndWeltenOhneStartpaket_KennenAlles()
    {
        Assert.Null(Knowledge.Of(World(intel: false)));
        Assert.Null(Knowledge.Of(WorldBuilder.BuildDefault()));            // Balance, Selbsttest
        Assert.Null(Knowledge.Of(WorldBuilder.BuildDefault(null, "combat")));   // Testszenario: knowAll
        var u = World(intel: false);
        Assert.All(u.Sectors.Keys, s => Assert.True(Knowledge.Visited(u, s)));
        Assert.All(u.Stations.Keys, s => Assert.True(Knowledge.KnowsStation(u, s)));
        Assert.All(u.NpcFactionIds(), f => Assert.True(Knowledge.KnowsFaction(u, f)));
        new IntelSystem().Step(u, 5);
        Assert.Null(Knowledge.Of(u));
    }

    [Fact]
    public void Betreten_UndSensoren_DeckenTorStationenUndFraktionAuf()
    {
        var u = World();
        var p = Player(u);
        var next = Neighbours(u, u.PlayerSector).First(n => u.Sectors[n].Stations.Count > 1 && u.Sectors[n].Owner.Value != 0);
        u.MoveShip(p, next); u.PlayerSector = next;
        // weit weg von allen Stationen: Sektor besucht, Tore bekannt, Stationen nicht
        p.Position = new Vec2(u.Sectors[next].Extent * 3f, u.Sectors[next].Extent * 3f);
        var ev = Capture(u);
        Scan(u);
        Assert.True(Knowledge.Visited(u, next));
        Assert.All(u.Sectors[next].Gates, g => Assert.True(Knowledge.KnowsGate(u, g)));
        Assert.True(Knowledge.KnowsFaction(u, u.Sectors[next].Owner));
        Assert.All(u.Sectors[next].Stations, st => Assert.False(Knowledge.KnowsStation(u, st)));
        Assert.Contains(ev, e => e is IntelDiscovered d && d.Visited == 1 && d.Source == "sensor");
        // Hinfliegen: eine Station in Reichweite, eine weit entfernte nicht
        var target = u.Stations[u.Sectors[next].Stations[0]];
        p.Position = target.Position + new Vec2(target.Radius + 20f, 0f);
        Scan(u);
        Assert.True(Knowledge.KnowsStation(u, target.Id));
        foreach (var sid in u.Sectors[next].Stations)
        {
            var st = u.Stations[sid];
            Assert.Equal(Vec2.Distance(p.Position, st.Position) - st.Radius <= u.Rules.IntelSensorRange, Knowledge.KnowsStation(u, sid));
        }
        // jenseits der Tore jetzt dem Namen nach
        foreach (var n in Neighbours(u, next)) Assert.True(Knowledge.KnowsSector(u, n));
    }

    [Fact]
    public void Andocken_UndBegegnung_UndComms_MachenBekannt()
    {
        var u = World();
        var far = u.Stations.Values.Where(s => !Knowledge.KnowsStation(u, s) && u.FactionOf(s) is { } f && !Knowledge.KnowsFaction(u, f)).OrderBy(s => s.Id.Value).First();
        new IntelSystem().Attach(u);
        u.Events.Publish(new ShipDocked(u.PlayerShip, far.Id));
        Assert.True(Knowledge.KnowsStation(u, far.Id));
        Assert.True(Knowledge.KnowsFaction(u, u.FactionOf(far)!.Value));
        // Comms: Nachricht einer unbekannten Fraktion
        var unk = u.NpcFactionIds().FirstOrDefault(f => !Knowledge.KnowsFaction(u, f));
        if (unk.Value != 0)
        {
            u.Inbox.Add(new CommsMessage { Id = u.NextMessageId++, From = new ActorId(unk.Value), Kind = "test", Created = u.Tick });
            Scan(u);
            Assert.True(Knowledge.KnowsFaction(u, unk));
        }
    }

    [Fact]
    public void Taxi_NurBekannteZiele_FahrtDecktZielsektorAuf()
    {
        var u = World();
        new SimulationRunner().Run(u, 1);
        var p = Player(u);
        var here = u.Stations.Values.Where(s => s.Sector == u.PlayerSector && TaxiServiceHas(u, s)).OrderBy(s => s.Id.Value).First();
        p.Position = here.DockPoint(p.Id); p.DockedAt = here.Id;
        u.PlayerWallet.Credits = 100_000f;
        var quotes = TaxiService.Quotes(u, here, p);
        Assert.All(quotes, q => Assert.True(Knowledge.KnowsStation(u, q.To)));
        // eine bekannte Station in einem nur von fern gesehenen Sektor (z. B. per Stationsliste gekauft)
        var dest = u.Stations.Values.Where(s => !Knowledge.Visited(u, s.Sector) && Knowledge.KnowsSector(u, s.Sector) && HasHangar(u, s)
                   && u.Ships.Values.All(x => x.DockedAt != s.Id || !x.IsPlayer)).OrderBy(s => s.Id.Value).First();
        Assert.DoesNotContain(TaxiService.Quotes(u, here, p), q => q.To.Id == dest.Id);
        Knowledge.RevealStation(u, dest.Id);
        Assert.Contains(TaxiService.Quotes(u, here, p), q => q.To.Id == dest.Id);
        var services = StationServices.Default();
        new IntelSystem().Attach(u);
        Assert.True(services.TryGet(TaxiService.ServiceKey, out var taxi));
        var r = taxi!.Execute(u, here, p, new ServiceRequest($"taxi:{dest.Id.Value}", default, 1f));
        Assert.True(r.Success, r.Message);
        Assert.True(Knowledge.Visited(u, dest.Sector));
    }

    static bool TaxiServiceHas(Universe u, Station s) => u.StationTypes.TryGetValue(s.TypeKey, out var t) && t.Services.Contains(TaxiService.ServiceKey);
    static bool HasHangar(Universe u, Station s) => !s.IsBuildSite && u.StationTypes.TryGetValue(s.TypeKey, out var t) && t.Services.Contains(HangarService.ServiceKey)
                                                  && t.Services.Contains(RentalService.ServiceKey);

    [Fact]
    public void ErkundenSektor_FliegtMusterUndDecktAllesAuf_SpeichernLadenBitgleich()
    {
        var u = World();
        var home = u.Stations.Values.Where(s => s.Sector == u.PlayerSector && !s.IsBuildSite).OrderBy(s => s.Id.Value).First();
        var scout = Own(u, "pip", home);
        var target = Neighbours(u, u.PlayerSector).OrderBy(n => n.Value).First();
        Assert.Contains(FleetTargets.ExploreSector(u, scout), t => t.Command is ExploreSectorCommand e && e.Sector == target);
        Assert.IsType<ExploreUniverseCommand>(FleetTargets.ExploreSector(u, scout)[0].Command);
        Assert.Equal("Sektor ist schon ganz bekannt.", Commands.Check(u, scout, new ExploreSectorCommand(u.PlayerSector)));
        Assert.True(Commands.Give(u, scout, new ExploreSectorCommand(target), out var m), m);
        var r = new SimulationRunner();
        for (int k = 0; k < 400; k++) r.Step(u);
        // mitten im Erkunden speichern → laden → beide gleich weiter (Wissen im Hash mit Spielerpolitik)
        var (v, _) = SaveGameJson.Read(SaveGameJson.Write(u, Now));
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
        Assert.Equal(Knowledge.Of(u)!.Count, Knowledge.Of(v)!.Count);
        var rv = new SimulationRunner();
        for (int k = 0; k < 400; k++) { r.Step(u); rv.Step(v); }
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
        for (int k = 0; k < 40_000 && scout.Command is not null; k++) r.Step(u);
        Assert.Null(scout.Command);
        Assert.True(Knowledge.Visited(u, target));
        Assert.Equal(0, Knowledge.UnknownIn(u, target));
    }

    [Fact]
    public void ErkundenUniversum_BesuchtAlleErreichbarenSektoren()
    {
        var u = World();
        u.Rules.PlayerRaids = false; u.Rules.FleetRaids = false;   // Überfälle würden die Fahrt nur verzögern
        var home = u.Stations.Values.Where(s => s.Sector == u.PlayerSector && !s.IsBuildSite).OrderBy(s => s.Id.Value).First();
        var scout = Own(u, "swift", home);
        Assert.Single(FleetTargets.ExploreUniverse(u, scout));
        Assert.True(Commands.Give(u, scout, new ExploreUniverseCommand(), out var m), m);
        var r = new SimulationRunner();
        for (int k = 0; k < 80_000 && scout.Command is not null; k++) r.Step(u);
        Assert.Null(scout.Command);
        Assert.All(u.Sectors.Keys, s => Assert.True(Knowledge.Visited(u, s), $"Sektor {s.Value} unbesucht"));
        Assert.Equal("Kein unerkundeter Sektor erreichbar.", Commands.Check(u, scout, new ExploreUniverseCommand()));
    }

    [Fact]
    public void Erkunden_ImBefehlsrasterUndKapazitaet()
    {
        var reg = OrderExecutors.Default;
        Assert.True(reg.TryGet(CommandKinds.ExploreSector, out var a) && a.ForPlayer);
        Assert.True(reg.TryGet(CommandKinds.ExploreUniverse, out var b) && !b.ForPlayer);   // eine Taste „Erkunden“, Universum ist ihr erstes Ziel
        var u = World();
        var home = u.Stations.Values.Where(s => s.Sector == u.PlayerSector && !s.IsBuildSite).OrderBy(s => s.Id.Value).First();
        // das aktive Schiff nimmt Befehle an (an Bord pausiert, siehe PersonZuFussTests); es zählt zur Kapazität
        Assert.True(Commands.Give(u, Player(u), new ExploreUniverseCommand(), out _));
        var ships = Enumerable.Range(0, 40).Select(_ => Own(u, "pip", home)).ToList();
        int given = ships.Count(s => Commands.Give(u, s, new ExploreUniverseCommand(), out _));
        Assert.True(given < ships.Count, "Befehlskapazität begrenzt auch Erkunden");
    }

    [Fact]
    public void IntelKaufen_Comms_GeldAnFraktion_UndVerweigerungen()
    {
        var u = World();
        var owner = u.Sectors[u.PlayerSector].Owner;
        var act = CommsActions.Default();
        u.PlayerWallet.Credits = 100_000f;
        var offers = act.Offers(u).Where(o => o.Key.StartsWith($"intel:{owner.Value}:", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(offers);
        var sectorOffer = offers.First(o => o.Key.Contains(":sector:"));
        int sid = int.Parse(sectorOffer.Key.Split(':')[3]);
        Assert.False(Knowledge.Visited(u, new SectorId(sid)) && Knowledge.UnknownIn(u, new SectorId(sid)) == 0);
        float w0 = u.PlayerWallet.Credits, t0 = u.Actors[new ActorId(owner.Value)].Treasury.Credits;
        var ev = Capture(u);
        var res = act.Execute(u, sectorOffer.Key);
        Assert.True(res.Success, res.Message);
        float price = -sectorOffer.Cost;
        Assert.Equal(w0 - price, u.PlayerWallet.Credits, 2);
        Assert.Equal(t0 + price, u.Actors[new ActorId(owner.Value)].Treasury.Credits, 2);
        Assert.Contains(ev, e => e is TreasuryIncome ti && ti.Purpose == "intel" && ti.Actor.Value == owner.Value && Math.Abs(ti.Amount - price) < 0.01f);
        Assert.Contains(ev, e => e is IntelBought ib && ib.Faction == owner && ib.Price == price);
        Assert.True(Knowledge.Visited(u, new SectorId(sid)));
        Assert.Equal(0, Knowledge.UnknownIn(u, new SectorId(sid)));
        // dasselbe Paket ist weg (nichts Neues)
        Assert.False(act.Execute(u, sectorOffer.Key).Success);

        // Verweigerungen ändern nichts und publizieren nichts
        var unknown = u.NpcFactionIds().First(f => !Knowledge.KnowsFaction(u, f));
        ev.Clear();
        float w1 = u.PlayerWallet.Credits;
        var r1 = act.Execute(u, $"intel:{unknown.Value}:territory");
        Assert.False(r1.Success); Assert.Equal("Fraktion unbekannt.", r1.Message);
        Assert.False(act.Execute(u, "intel:x:territory").Success);
        Assert.False(act.Execute(u, $"intel:{owner.Value}:quatsch").Success);
        Relations.Set(u, new ActorId(owner.Value), u.PlayerActor, -0.5f);
        var r2 = act.Execute(u, $"intel:{owner.Value}:territory");
        Assert.False(r2.Success); Assert.Equal("Sie ist dir feindlich gesinnt.", r2.Message);
        Relations.Set(u, new ActorId(owner.Value), u.PlayerActor, 0.2f);
        u.PlayerWallet.Credits = 1f;
        var poor = act.Offers(u).First(o => o.Key.StartsWith($"intel:{owner.Value}:", StringComparison.Ordinal));
        Assert.False(poor.Possible);
        Assert.False(act.Execute(u, poor.Key).Success);
        Assert.Equal(1f, u.PlayerWallet.Credits);
        Assert.Empty(ev);
        _ = w1;
    }

    [Fact]
    public void IntelKaufen_Behoerde_Dienst_UndGebietskarte()
    {
        var u = World();
        new SimulationRunner().Run(u, 1);
        u.PlayerWallet.Credits = 100_000f;
        var services = StationServices.Default();
        var p = Player(u);
        var hq = u.Stations.Values.Where(s => s.Sector == u.PlayerSector && services.ForStation(u, s).Any(x => x.Key == IntelService.ServiceKey)).OrderBy(s => s.Id.Value).First();
        var svc = services.ForStation(u, hq).First(x => x.Key == IntelService.ServiceKey);
        var offers = svc.Offers(u, hq, p);
        Assert.Contains(offers, o => o.Key == "buy:territory" || o.Key.StartsWith("buy:sector:", StringComparison.Ordinal));
        var f = u.FactionOf(hq)!.Value;
        var pick = offers.FirstOrDefault(o => o.Key == "buy:territory").Key ?? offers.First().Key;
        float t0 = u.Actors[new ActorId(f.Value)].Treasury.Credits;
        var r = svc.Execute(u, hq, p, new ServiceRequest(pick, default, 1f));
        Assert.True(r.Success, r.Message);
        Assert.True(u.Actors[new ActorId(f.Value)].Treasury.Credits > t0);
        if (pick == "buy:territory")
            Assert.All(u.Sectors.Values.Where(s => s.Owner == f), s => Assert.True(Knowledge.Visited(u, s.Id) && Knowledge.UnknownIn(u, s.Id) == 0));
        Assert.False(svc.Execute(u, hq, p, new ServiceRequest("buy:", default, 1f)).Success);
        Assert.False(svc.Execute(u, hq, p, new ServiceRequest("sell:territory", default, 1f)).Success);
        // ohne beschränktes Wissen kein Dienst
        u.Player.Intel = null;
        Assert.DoesNotContain(services.ForStation(u, hq), x => x.Key == IntelService.ServiceKey);
    }

    [Fact]
    public void Ansichten_ZeigenNurBekanntes()
    {
        var u = World();
        new SimulationRunner().Run(u, 2);
        var p = Player(u);
        foreach (var g in u.Goods.Keys)
        {
            if (MarketInfo.BestBuyerForGood(u, g) is { } b) Assert.True(Knowledge.KnowsStation(u, b));
            foreach (var q in MarketInfo.HonestBuyers(u, g, 10f, p.Sector, p.Position)) Assert.True(Knowledge.KnowsStation(u, q.Station));
        }
        Assert.All(FleetTargets.Rally(u, p, new[] { p }), t => Assert.True(Knowledge.KnowsStation(u, ((RallyCommand)t.Command).At)));
        Assert.All(FleetTargets.Salvage(u, p), t => Assert.True(Knowledge.KnowsSector(u, ((SalvageCommand)t.Command).Sector)));
        // mit vollem Wissen sieht die Liste mehr
        int limited = FleetTargets.Rally(u, p, new[] { p }).Count;
        u.Player.Intel = null;
        Assert.True(FleetTargets.Rally(u, p, new[] { p }).Count > limited);
    }

    [Fact]
    public void Auftrag_DecktZielAuf()
    {
        var u = World();
        Knowledge.Init(u, u.PlayerSector);
        var st = u.Stations.Values.Where(s => !Knowledge.KnowsStation(u, s) && !s.IsBuildSite).OrderBy(s => s.Id.Value).First();
        var mission = new Mission { Id = u.NextMissionId++, Type = "transport", Client = u.Stations.Values.First(s => s.Sector == u.PlayerSector).Owner, State = MissionState.Active,
            Args = new Dictionary<string, string> { ["target"] = st.Id.Value.ToString() } };
        u.Missions.Add(mission);
        IntelSystem.RevealMission(u, mission.Id);
        Assert.True(Knowledge.KnowsStation(u, st.Id));
        Assert.True(Knowledge.KnowsSector(u, st.Sector));
    }

    [Fact]
    public void NpcWelt_Unveraendert_HashOhneSpielerpolitikGleich()
    {
        var a = World(intel: true);
        var b = World(intel: false);
        Assert.NotNull(Knowledge.Of(a)); Assert.Null(Knowledge.Of(b));
        var ra = new SimulationRunner(); var rb = new SimulationRunner();
        for (int k = 0; k < 3000; k++) { ra.Step(a); rb.Step(b); }
        Assert.Equal(UniverseHash.Compute(a, true, includePlayerPolitics: false), UniverseHash.Compute(b, true, includePlayerPolitics: false));
        Assert.NotEqual(UniverseHash.Compute(a), UniverseHash.Compute(b));   // das Wissen selbst ist Spielerpolitik
        Assert.True(Knowledge.Of(a)!.Count > 0);
    }

    [Fact]
    public void SpeichernLaden_WissenBleibt_OhneWissenBleibtNull()
    {
        var u = World();
        Knowledge.RevealStation(u, u.Stations.Keys.OrderBy(k => k.Value).Last());
        var (v, _) = SaveGameJson.Read(SaveGameJson.Write(u, Now));
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
        Assert.Equal(Knowledge.Of(u)!.Sectors.OrderBy(kv => kv.Key.Value), Knowledge.Of(v)!.Sectors.OrderBy(kv => kv.Key.Value));
        Assert.True(Knowledge.Of(v)!.Stations.SetEquals(Knowledge.Of(u)!.Stations));
        var w = WorldBuilder.BuildDefault();
        var (x, _) = SaveGameJson.Read(SaveGameJson.Write(w, Now));
        Assert.Null(Knowledge.Of(x));
        Assert.DoesNotContain("\"Intel\"", SaveGameJson.Write(w, Now));
    }
}
