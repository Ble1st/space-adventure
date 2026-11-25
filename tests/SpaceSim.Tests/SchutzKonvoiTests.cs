using SpaceSim.Core.Combat;
using SpaceSim.Core.Comms;
using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Ausbau Punkt 3: Verfolgung über die Grenze (1 Sprung in befreundetes/verbündetes Gebiet), Schutzvertrag des Spielers (Comms, sichere Hilfe, Geleit),
/// KI-Konvois bei hohem Routenrisiko; Nachtrag 5b (Anteil des Hehlergelds der Schmuggler an die Piratenkasse).
/// </summary>
public class SchutzKonvoiTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Punkt 3 zurücknehmen (Referenz-Hash-Tests; erste Zeile in <see cref="BalanceFixTests.RevertBalanceFix"/> und <see cref="GasNebulaTests.RevertGas"/>).</summary>
    internal static void RevertSchutz(WorldDef def)
    {
        var r = def.Rules ??= new EconomyRules();
        CarrierTests.RevertCarriers(def);   // Träger (danach): aus — WorldBuilder lässt Trägerklasse, -modell und Testpaket weg
        WurmlochTests.RevertWormholes(def);   // Wurmlöcher (danach): aus — keine stabilen Paare, keine instabilen
        SonnenTests.RevertSuns(def);   // Sonnen (danach): aus — WorldBuilder lässt die Sonnen weg
        HitzeTests.RevertHeat(def);   // Hitze und Wärmeabfuhr (danach): aus — WorldBuilder lässt die Kühlermodule weg
        RaketenlenkungTests.RevertGuidance(def);   // Raketenlenkung und Gegenmaßnahmen (danach): aus — WorldBuilder lässt ihre Inhalte weg
        HerrenlosTests.RevertDerelict(def);   // herrenlose Schiffe, Panne, Schiff liefern (danach): aus
        r.DockDwellTicks = 0; r.DockDwellTicksPlayer = 0;   // Liegezeit an Stationen (danach): aus
        r.UniverseEvents = false;   // Universumsereignisse (danach): aus (UniverseEventTests)
        r.Bars = false;   // Bars (danach): aus (BarTests) — reine Spieleraktion, ändert ohne Spieler nichts
        r.PursuitCrossBorder = false;
        r.ProtectionContracts = false;
        r.Convoys = false;
        r.SmugglerFencePirateShare = 1f;
        r.PirateModelCostFactors = new();   // Nachtrag 5b
        r.FeudSeizeAsGoods = false;   // Nachtrag 6b
        // Nachtrag 6b: Regale für Munitionsmagazin und Punktabwehr wie vorher (Werft [1, 2], Systemwerk ohne eigenen Eintrag)
        foreach (var t in def.StationTypes)
        {
            if (t.Key == "shipyard" && t.Stocks is not null)
                foreach (var g in new[] { "weapon_magazine_s", "point_defense_s" }) if (t.Stocks.ContainsKey(g)) t.Stocks[g] = new[] { 1f, 2f };
            if (t.Key == "systems_works" && t.Stocks is not null) { t.Stocks.Remove("weapon_magazine_s"); t.Stocks.Remove("point_defense_s"); }
            if (t.Key == "farm" && t.Stocks is not null) { t.Stocks.Remove("water"); if (t.Stocks.Count == 0) t.Stocks = null; }   // Nachtrag 17b: Wasserpuffer der Farmen
        }
    }

    static Universe World(ulong seed = 1) => WorldBuilder.Build(TestWorlds.Def(fuel: false, research: false), seed);

    static readonly FactionId Union = new(1), Guild = new(2);

    [Fact]
    public void Schalter_CodeAus_StandardweltAn_RuecknahmeAus()
    {
        var c = new EconomyRules();
        Assert.False(c.PursuitCrossBorder || c.ProtectionContracts || c.Convoys);
        Assert.Equal(1f, c.SmugglerFencePirateShare);
        var on = World().Rules;
        Assert.True(on.PursuitCrossBorder && on.ProtectionContracts && on.Convoys);
        Assert.True(on.PirateModelCostFactors.GetValueOrDefault("bomber") > 1f);   // Nachtrag 5b
        var def = TestWorlds.Def(false, false);
        RevertSchutz(def);
        var off = WorldBuilder.Build(def, 1).Rules;
        Assert.False(off.PursuitCrossBorder || off.ProtectionContracts || off.Convoys);
        Assert.Empty(off.PirateModelCostFactors);
    }

    [Fact]
    public void SchalterAus_Standardwelt20000_BitgleichMitMain()
    {
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        RevertSchutz(def);
        var u = WorldBuilder.Build(def);
        new SimulationRunner().Run(u, 20000);
        Assert.Equal(0x73CB552B191051FEUL, UniverseHash.Compute(u));   // main 11c0518 (Balance 20000, Standardwelt)
    }

    static int Jumps(Universe u, FactionId f, SectorId s)
    {
        if (u.Sectors[s].Owner == f) return 0;
        var seen = new HashSet<SectorId> { s }; var ring = new List<SectorId> { s };
        for (int d = 1; d < 10; d++)
        {
            var next = new List<SectorId>();
            foreach (var x in ring)
                foreach (var g in u.Gates.Values.Where(g => g.Sector == x))
                {
                    var far = u.Gates[g.Target].Sector;
                    if (!seen.Add(far)) continue;
                    if (u.Sectors[far].Owner == f) return d;
                    next.Add(far);
                }
            ring = next;
        }
        return 99;
    }

    [Fact]
    public void Verfolgung_EinSprungInsBefreundeteGebiet_NieFeindNieBanditenNieHerrenlos()
    {
        var u = World();
        Relations.Set(u, Union, Guild, 0.7f);
        Relations.Set(u, Guild, Union, 0.7f);
        var guild = u.Sectors.Values.Where(s => s.Owner == Guild).ToList();
        var near = guild.First(s => Jumps(u, Union, s.Id) == 1);
        Assert.True(Jurisdiction.CoversPursuit(u, Union, near.Id));
        if (guild.FirstOrDefault(s => Jumps(u, Union, s.Id) >= 2) is { } far)
        {
            Assert.False(Jurisdiction.CoversPursuit(u, Union, far.Id));
            Assert.True(Jurisdiction.Covers(u, Union, far.Id));   // die alte Zuständigkeit bleibt für Geleit
        }
        // Feindschaft/Fehde/Krieg: nie
        Feuds.Start(u, Union, Guild);
        Assert.False(Jurisdiction.CoversPursuit(u, Union, near.Id));
        u.Feuds.Clear();
        Assert.True(Jurisdiction.CoversPursuit(u, Union, near.Id));
        // neutral (Startbeziehung) ohne Bündnis: nein; mit Bündnis: ja
        Relations.Set(u, Union, Guild, 0.2f);
        Assert.False(Jurisdiction.CoversPursuit(u, Union, near.Id));
        u.Alliances.Add(new Alliance { A = Union, B = Guild, Since = u.Tick });
        Assert.True(Jurisdiction.CoversPursuit(u, Union, near.Id));
        // Banditen- und herrenlose Sektoren: nie
        foreach (var s in u.Sectors.Values.Where(s => !u.IsNpcFaction(s.Owner) || Bandits.IsEstablished(u, s.Owner)))
        {
            if (u.IsNpcFaction(s.Owner)) { Relations.Set(u, Union, s.Owner, 0.9f); Relations.Set(u, s.Owner, Union, 0.9f); }
            Assert.False(Jurisdiction.CoversPursuit(u, Union, s.Id), s.Name);
        }
        // Regel aus: alte Zuständigkeit
        u.Rules.PursuitCrossBorder = false;
        Assert.Equal(Jurisdiction.Covers(u, Union, near.Id), Jurisdiction.CoversPursuit(u, Union, near.Id));
    }

    [Fact]
    public void Schutzvertrag_Comms_KaufTagesgebuehrUndEnden()
    {
        var u = World();
        var comms = CommsActions.Default();
        u.PlayerWallet.Credits = 1000f;
        var offers = comms.Offers(u).Where(o => o.Key.StartsWith("protection:buy:")).Select(o => o.Key).ToList();
        Assert.Contains($"protection:buy:{Union.Value}", offers);
        Assert.DoesNotContain(offers, k => k.EndsWith(":4"));   // Banditen bieten keinen Schutz
        float treasury = u.Actors[new ActorId(1)].Treasury.Credits;
        var res = comms.Execute(u, $"protection:buy:{Union.Value}");
        Assert.True(res.Success, res.Message);
        Assert.Equal(800f, u.PlayerWallet.Credits, 2);
        Assert.Equal(treasury + 200f, u.Actors[new ActorId(1)].Treasury.Credits, 2);
        Assert.False(comms.Execute(u, $"protection:buy:{Union.Value}").Success);   // läuft schon
        Assert.False(comms.Execute(u, "protection:buy:x").Success);
        // nächster Tag: Gebühr
        var runner = new SimulationRunner();
        while (u.Tick < u.Rules.DayTicks + 60) runner.Step(u);
        var c = Protection.Of(u, Union)!;
        Assert.True(c.PaidUntil > u.Tick);
        Assert.True(u.PlayerWallet.Credits <= 600.01f);
        // Kasse trägt die Gebühr nicht: endet
        var ended = new List<ProtectionEnded>();
        u.Events.Raised += e => { if (e is ProtectionEnded pe) ended.Add(pe); };
        u.PlayerWallet.Credits = -1f;
        for (int i = 0; i < 60; i++) runner.Step(u);
        Assert.Null(Protection.Of(u, Union));
        Assert.Equal("unpaid", Assert.Single(ended).Reason);
        // Feindschaft: endet
        u.PlayerWallet.Credits = 5000f;
        Assert.True(comms.Execute(u, $"protection:buy:{Guild.Value}").Success);
        Relations.Set(u, Guild, u.PlayerActor, -0.5f);
        for (int i = 0; i < 60; i++) runner.Step(u);
        Assert.Null(Protection.Of(u, Guild));
        Assert.Equal("hostile", ended.Last().Reason);
        // kündigen
        Assert.True(comms.Execute(u, $"protection:buy:{Union.Value}").Success);
        Assert.True(comms.Execute(u, $"protection:end:{Union.Value}").Success);
        Assert.Equal("cancelled", ended.Last().Reason);
        // Krieg: kein Angebot
        u.Wars.Add(new War { A = Union, B = u.PlayerFaction!.Value, Since = u.Tick });
        Assert.NotNull(Protection.BuyIssue(u, Union));
    }

    static Ship Make(Universe u, string model, ShipRole role, Controller ctrl, ActorId owner, Station at)
    {
        var s = ShipPurchase.Create(u, u.NewShipId(), model, u.ShipClasses[u.ShipModels[model].ClassKey].Slots, u.ShipModels[model].Fit, role, ctrl, owner, at, 0f);
        u.AddShip(s);
        return s;
    }

    static Encounter RaidOnFleet(bool contract)
    {
        var u = World();
        u.Rules.PoliceResponseChance = 0f;   // ohne Vertrag hilft niemand
        var sector = u.Sectors.Values.First(s => s.Owner == Union && u.Stations.Values.Any(st => st.Sector == s.Id && st.Dockable && !st.IsBuildSite));
        var st = u.Stations.Values.First(s => s.Sector == sector.Id && s.Dockable && !s.IsBuildSite);
        var fleet = Make(u, "unit_trader_100", ShipRole.Player, Controller.Player, u.PlayerActor, st);
        var cop = Make(u, "warden", ShipRole.Police, Controller.Ai, new ActorId(1), st);
        var raider = u.Ships.Values.First(s => ThreatMap.IsRaider(s) && !u.Fights.IsEngaged(s.Id));
        if (contract) { u.PlayerWallet.Credits = 1000f; Protection.Buy(u, Union); }
        var e = Engage.StartRaid(u, raider, fleet, sector);
        Assert.True(!contract || e.Police == cop.Id || e.Police.Value != 0);
        return e;
    }

    [Fact]
    public void Schutzvertrag_UeberfallBekommtSichereHilfe()
    {
        Assert.Equal(0, RaidOnFleet(contract: false).Police.Value);
        Assert.NotEqual(0, RaidOnFleet(contract: true).Police.Value);
    }

    [Fact]
    public void Konvoi_SammeltFaehrtMitGeleitUndLoestSichAuf()
    {
        var u = World();
        var r = u.Rules;
        // Sektor der Union mit Station und einem Abnehmer in einem anderen Sektor; Risiko künstlich hoch
        var from = u.Sectors.Values.First(s => s.Owner == Union && u.Stations.Values.Any(st => st.Sector == s.Id && st.Dockable && !st.IsBuildSite && st.Market.Count > 0));
        var at = u.Stations.Values.First(st => st.Sector == from.Id && st.Dockable && !st.IsBuildSite && st.Market.Count > 0);
        var sell = u.Stations.Values.Where(st => st.Sector != from.Id && st.Dockable && !st.IsBuildSite && st.Market.Count > 0).OrderBy(st => st.Id.Value).First();
        var good = sell.Market.Keys.First();
        foreach (var s in u.Sectors.Values) { s.Threat = 20f; s.Security = 0f; }
        Assert.True(ThreatMap.RouteRisk(u, from.Id, sell.Sector) >= r.ConvoyRouteRisk);
        var cop = Make(u, "warden", ShipRole.Police, Controller.Ai, new ActorId(1), at);
        var traders = new List<Ship>();
        // drei Händler kaufen an derselben Station für dieselbe Strecke ein (nur dann wird gesammelt — kein Warten auf Verdacht)
        for (int i = 0; i < 3; i++)
        {
            var t = Make(u, "unit_trader_100", ShipRole.Trader, Controller.Ai, u.FreeActor, at);
            t.Order = new TradeOrder(at.Id, sell.Id, good); t.Phase = TradePhase.GoingToBuy;
            traders.Add(t);
        }
        foreach (var t in traders)
        {
            t.Cargo[good] = 10f; t.Phase = TradePhase.GoingToSell;
            Assert.True(ConvoyRules.Hold(u, t, at, (TradeOrder)t.Order!));
        }
        var c = Assert.Single(u.Convoys);
        Assert.Equal(0, c.Departed);
        Assert.All(traders, t => Assert.Null(t.Transit));
        // Speichern mitten im Sammeln: bitgleich
        var (copy, _) = SaveGameJson.Read(SaveGameJson.Write(u, Now));
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(copy));
        Assert.Equal(c.Traders, copy.Convoys.Single().Traders);
        // Wartezeit vorbei: alle fahren, Geleit
        var formed = new List<ConvoyFormed>(); var ended = new List<ConvoyDisbanded>();
        u.Events.Raised += e => { if (e is ConvoyFormed f) formed.Add(f); if (e is ConvoyDisbanded d) ended.Add(d); };
        u.Tick += r.ConvoyWaitTicks;
        ConvoyRules.Step(u);
        Assert.Equal(3, Assert.Single(formed).Traders);
        Assert.All(traders, t => Assert.NotNull(t.Transit));
        var esc = u.Ships[Assert.Single(c.Escorts)];   // erste freie Streife der Union im Sektor (nicht unbedingt die neue)
        Assert.True(esc.Command is EscortCommand ec && traders.Any(t => t.Id == ec.Target));
        // Geleit steht jedem Konvoihändler bei
        var (ws, _) = ThreatMap.Where(u, traders[1]);
        if (ThreatMap.Where(u, esc).Sector == ws) Assert.Contains(esc, FleetGuards.Escorts(u, traders[1], ws));
        _ = cop;
        // Ankunft: auflösen
        foreach (var t in traders) { u.FinishTransit(t); t.Order = null; t.Phase = TradePhase.Idle; t.Cargo.Clear(); }
        ConvoyRules.Step(u);
        Assert.Empty(u.Convoys);
        Assert.Equal("arrived", ended.Last().Reason);
    }

    [Fact]
    public void Konvoi_ZuWenigeHaendler_FahrenNachDerFristOhneGeleit()
    {
        var u = World();
        var at = u.Stations.Values.First(st => u.Sectors[st.Sector].Owner == Union && st.Dockable && !st.IsBuildSite && st.Market.Count > 0);
        var sell = u.Stations.Values.Where(st => st.Sector != at.Sector && st.Dockable && !st.IsBuildSite && st.Market.Count > 0).OrderBy(st => st.Id.Value).First();
        var good = sell.Market.Keys.First();
        foreach (var s in u.Sectors.Values) { s.Threat = 20f; s.Security = 0f; }
        Make(u, "warden", ShipRole.Police, Controller.Ai, new ActorId(1), at);
        var t = Make(u, "unit_trader_100", ShipRole.Trader, Controller.Ai, u.FreeActor, at);
        t.Cargo[good] = 10f; t.Order = new TradeOrder(at.Id, sell.Id, good); t.Phase = TradePhase.GoingToSell;
        // allein (niemand sonst kauft hier für dieselbe Strecke): fährt sofort, kein Warten auf Verdacht
        Assert.False(ConvoyRules.Hold(u, t, at, (TradeOrder)t.Order));
        Assert.Empty(u.Convoys);
        // ein zweiter Händler, der nie kommt: höchstens ConvoyWaitTicks gewartet
        u.Rules.ConvoyMinTraders = 2;
        var late = Make(u, "unit_trader_100", ShipRole.Trader, Controller.Ai, u.FreeActor, at);
        late.Order = new TradeOrder(at.Id, sell.Id, good); late.Phase = TradePhase.GoingToBuy;
        Assert.True(ConvoyRules.Hold(u, t, at, (TradeOrder)t.Order));
        u.Tick += u.Rules.ConvoyWaitTicks - 5;
        ConvoyRules.Step(u);
        Assert.Null(t.Transit);   // wartet noch auf den angemeldeten Händler
        u.Tick += 5;
        ConvoyRules.Step(u);
        Assert.NotNull(t.Transit);   // höchstens ConvoyWaitTicks gewartet
        Assert.Empty(u.Convoys);
    }

    [Fact]
    public void Speichern_MitVertrag_Bitgleich_HashNurMitSpielerpolitik()
    {
        var u = World();
        new SimulationRunner().Run(u, 100);
        u.PlayerWallet.Credits += 1000f;
        ulong npcW = UniverseHash.Compute(u, includeTags: true, includePlayerPolitics: false), fullW = UniverseHash.Compute(u);
        var c = Protection.Buy(u, Union);
        u.PlayerWallet.Credits += 200f;   // Gebühr zurück: nur der Vertrag selbst unterscheidet sich
        u.Actors[new ActorId(1)].Treasury.Credits -= 200f;
        Assert.NotEqual(fullW, UniverseHash.Compute(u));
        Assert.Equal(npcW, UniverseHash.Compute(u, includeTags: true, includePlayerPolitics: false));
        var (copy, _) = SaveGameJson.Read(SaveGameJson.Write(u, Now));
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(copy));
        Assert.Equal(c.PaidUntil, Protection.Of(copy, Union)!.PaidUntil);
    }

    [Fact]
    public void Nachtrag5b_BomberFuerPiratenTeurer_HehleranteilGedeckelt()
    {
        var u = World();
        var bomber = u.ShipModels["bomber"]; var cls = u.ShipClasses[bomber.ClassKey];
        float with = CombatModels.PirateCost(u, bomber, cls);
        u.Rules.PirateModelCostFactors = new();
        Assert.Equal(with / 3f, CombatModels.PirateCost(u, bomber, cls), 1);
        var corsair = u.ShipModels["corsair"];
        Assert.Equal(1f, CombatModels.ModelCostFactor(u, corsair));
    }
}
