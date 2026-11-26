using SpaceSim.Core.Comms;
using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Missions;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Welle B (Befunde aus drei Spielerbot-Durchspielen): Modulregale für den Spieler, größere Werftziele, abnehmender Einfluss, KI-Reaktion auf eine
/// Stationsflut, Flottenabgabe, Spende, mehr Aufträge bei schlechter Beziehung, Bündnisschwelle des Spielers, Kaufwarnung, Rechenzeit der Wegplanung.
/// </summary>
public class WelleBTests
{
    readonly Xunit.Abstractions.ITestOutputHelper _out;
    public WelleBTests(Xunit.Abstractions.ITestOutputHelper o) => _out = o;

    static readonly DateTimeOffset Now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Referenzhash-Tests: alle Schalter der Welle B aus (alte Regeln); erste Zeile in <see cref="SchutzKonvoiTests.RevertSchutz"/>.</summary>
    internal static void RevertWelleB(WorldDef def)
    {
        var r = def.Rules ??= new EconomyRules();
        r.PlayerModuleShelfFactor = 0f;
        r.YardLargeModuleTarget = 0;
        r.InfluenceFullStations = 0;
        r.InfluenceExtraFactor = 1f;
        r.PlayerContestReaction = false;
        r.PlayerFleetLevyShare = 0f;
        r.Donations = false;
        r.MissionLowStandingExtraChance = 0f;
        r.PlayerAllianceMinRelation = 0f;
        r.ShipyardCapacityWarning = false;
    }

    static Universe World(Action<EconomyRules>? tweak = null)
    {
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        tweak?.Invoke(def.Rules!);
        var u = WorldBuilder.Build(def);
        new SimulationRunner().Run(u, 1);
        u.PlayerWallet.Credits = 500_000f;
        return u;
    }

    static Station Yard(Universe u) => u.StationsById().First(s => s.TypeKey == "shipyard");

    static Ship Own(Universe u, string model, Station at)
    {
        var m = u.ShipModels[model];
        var s = ShipPurchase.Create(u, u.NewShipId(), m.Key, u.ShipClasses[m.ClassKey].Slots, m.Fit, ShipRole.Player, Controller.Player, u.PlayerActor, at, 0f);
        u.AddShip(s);
        s.RecomputeStats(u);
        return s;
    }

    static List<SimEvent> Capture(Universe u)
    {
        var list = new List<SimEvent>();
        u.Events.Raised += list.Add;
        return list;
    }

    [Fact]
    public void Schalter_CodeAus_WeltAn_RevertAus()
    {
        var c = new EconomyRules();
        Assert.Equal(0f, c.PlayerModuleShelfFactor); Assert.Equal(0, c.YardLargeModuleTarget); Assert.Equal(0, c.InfluenceFullStations);
        Assert.False(c.PlayerContestReaction); Assert.Equal(0f, c.PlayerFleetLevyShare); Assert.False(c.Donations);
        Assert.Equal(0f, c.MissionLowStandingExtraChance); Assert.Equal(0f, c.PlayerAllianceMinRelation); Assert.False(c.ShipyardCapacityWarning);
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        var r = def.Rules!;
        Assert.True(r.PlayerModuleShelfFactor > 0f && r.YardLargeModuleTarget > 0 && r.InfluenceFullStations > 0 && r.PlayerContestReaction && r.PlayerFleetLevyShare > 0f
            && r.Donations && r.MissionLowStandingExtraChance > 0f && r.PlayerAllianceMinRelation > 0f && r.ShipyardCapacityWarning);
        RevertWelleB(def);
        Assert.False(r.PlayerContestReaction || r.Donations || r.ShipyardCapacityWarning);
    }

    // ───────── 1: Modulregale ─────────

    [Fact]
    public void Werftziele_GrosseModule_AufZwei()
    {
        var on = World();
        var off = World(r => r.YardLargeModuleTarget = 0);
        var laserM = on.FindGood("weapon_laser_m")!.Id;
        Assert.Equal(2f, Yard(on).Market[laserM].Target);
        Assert.Equal(1f, Yard(off).Market[laserM].Target);
        // kleine Module unverändert
        var laserS = on.FindGood("weapon_laser_s")!.Id;
        Assert.Equal(Yard(off).Market[laserS].Target, Yard(on).Market[laserS].Target);
    }

    [Fact]
    public void Modulregal_NimmtVomSpielerBisDreifachesZiel_NpcUnveraendert()
    {
        var u = World();
        var yard = Yard(u);
        yard.Credits = 1_000_000f;
        var good = u.FindGood("weapon_laser_m")!.Id;
        var e = yard.Market[good];
        e.Amount = e.Target;   // Regal voll (altes Limit erreicht)
        var me = u.Ships[u.PlayerShip];
        me.DockedAt = yard.Id; me.Sector = yard.Sector; me.Position = yard.DockPoint(me.Id);
        me.Cargo[good] = 4f;
        float expect = e.Target * u.Rules.PlayerModuleShelfFactor - e.Amount;
        var intake = MarketInfo.Acceptance(u, yard, good, me);
        Assert.True(intake.Units >= expect - 0.01f, $"Spieler-Annahme {intake.Units} (erwartet {expect})");
        // Angebot = Ausführung
        var svc = new TradeService();
        var offer = svc.Offers(u, yard, me).First(o => o.Key == $"sell:{good.Value}");
        Assert.True(offer.Amount >= 1f);
        float held = me.Cargo[good];
        var res = svc.Execute(u, yard, me, new ServiceRequest($"sell:{good.Value}", good, 99f));
        Assert.True(res.Success, res.Message);
        Assert.True(e.Amount > e.Target, "über das Ziel geliefert");
        Assert.Equal(held - res.Units, me.Cargo.GetValueOrDefault(good), 2);
        // NPC-Händler sehen die alte Annahme
        var npc = u.ShipsById().First(s => s.IsEconomyAi);
        Assert.Equal(yard.AcceptableUnits(good, u.Rules), MarketInfo.Acceptance(u, yard, good, npc).Cap + 0f, 2);
        // Schalter aus: altes Limit auch für den Spieler
        u.Rules.PlayerModuleShelfFactor = 0f;
        Assert.True(MarketInfo.Acceptance(u, yard, good, me).Units < 1f);
    }

    // ───────── 2a: abnehmender Einfluss ─────────

    static Sector NpcSector(Universe u) =>
        u.Sectors.Values.OrderBy(s => s.Id.Value).First(s => u.IsNpcFaction(s.Owner) && s.Stations.Count >= 6);

    static void PlayerStations(Universe u, Sector sec, string type, int n)
    {
        var t = u.StationTypes[type];
        for (int i = 0; i < n; i++)
        {
            var id = u.NewStationId();
            var st = StationFactory.Create(u, id, t, $"Test {id.Value}", sec.Id, new Vec2(4000f + id.Value * 60f, 4000f), u.PlayerActor);
            u.AddStation(st);
        }
        SectorControl.Update(u, sec);
    }

    [Fact]
    public void Einfluss_NimmtJeTypAb()
    {
        var on = World();
        var off = World(r => r.InfluenceFullStations = 0);
        var so = NpcSector(on); var sf = off.Sectors[so.Id];
        PlayerStations(on, so, "solar_plant", 5);
        PlayerStations(off, sf, "solar_plant", 5);
        float WeightOf(Universe u, Sector s) => SectorControl.Compute(u, s).Shares.GetValueOrDefault(u.PlayerFaction!.Value) * SectorControl.Compute(u, s).Total;
        float w = on.StationTypes["solar_plant"].Influence;
        Assert.Equal(5f * w, WeightOf(off, sf), 2);
        Assert.Equal((2f + 3f * 0.3f) * w, WeightOf(on, so), 2);
    }

    // ───────── 2b: KI-Reaktion ─────────

    static Sector Flood(Universe u)
    {
        var sec = NpcSector(u);
        var types = new[] { "solar_plant", "steelworks", "water_plant", "farm", "electronics_fab", "copper_smelter", "plating_works", "wafer_fab", "electrolysis", "goods_factory" };
        foreach (var t in types.Where(u.StationTypes.ContainsKey))
        {
            if (PlayerContest.ContestedByPlayer(u, sec)) break;
            PlayerStations(u, sec, t, 2);
        }
        return sec;
    }

    [Fact]
    public void Stationsflut_KiBautGegen_SperrtUndFehdet()
    {
        var u = World();
        var sec = Flood(u);
        Assert.True(PlayerContest.ContestedByPlayer(u, sec), "Spieler umkämpft den Sektor");
        var f = sec.Owner;
        var me = u.PlayerFaction!.Value;
        // Sperre gegen den Spieler mindestens 1
        var (level, against, _) = FactionPolicySystem.Desired(u, sec);
        Assert.True(level >= 1, $"Stufe {level}");
        Assert.Equal(me, against);
        // Gegenbau aus der freien Kasse; bei schlechter Beziehung zugleich die Fehde (ohne Firmensitz des Spielers)
        Relations.Set(u, f, me, -0.6f); Relations.Set(u, me, f, -0.6f);
        var fa = u.Actors[new ActorId(f.Value)];
        fa.Treasury.Credits += 500_000f;
        var ev = Capture(u);
        float before = fa.Treasury.Credits;
        int built = PlayerContest.React(u);
        Assert.True(built >= 1);
        var c = ev.OfType<PlayerContestCountered>().First();
        Assert.Equal(f, c.Faction);
        Assert.Equal(u.Rules.PlayerContestCounterTypes[0], c.TypeKey);
        Assert.Equal(before - c.Cost, fa.Treasury.Credits, 1);
        Assert.Contains(ev.OfType<TreasuryUpkeep>(), t => t.Purpose == "contest_build" && MathF.Abs(t.Amount - c.Cost) < 0.01f);
        Assert.True(Feuds.InFeud(u, f, me), "Fehde gegen den Spieler: " + Feuds.StartIssue(u, f, me, playerContest: true));
    }

    [Fact]
    public void Stationsflut_OhneSchalter_KeineReaktion()
    {
        var u = World(r => r.PlayerContestReaction = false);
        var sec = NpcSector(u);
        PlayerStations(u, sec, "solar_plant", 2); PlayerStations(u, sec, "steelworks", 2); PlayerStations(u, sec, "farm", 2); PlayerStations(u, sec, "water_plant", 2);
        Assert.False(PlayerContest.ContestedByPlayer(u, sec));
        Assert.Equal(0, PlayerContest.React(u));
    }

    [Fact]
    public void Stationsflut_SystemReagiertImTakt_Deterministisch()
    {
        string Run()
        {
            var u = World();
            var sec = Flood(u);
            u.Actors[new ActorId(sec.Owner.Value)].Treasury.Credits += 500_000f;
            var ev = Capture(u);
            new SimulationRunner().Run(u, 2500);
            return string.Join(",", ev.OfType<PlayerContestCountered>().Select(e => $"{e.Station.Value}:{e.TypeKey}")) + "|" + UniverseHash.Compute(u).ToString("X");
        }
        var a = Run();
        Assert.Contains(":", a.Split('|')[0]);
        Assert.Equal(a, Run());
    }

    // ───────── 3: Flottenabgabe ─────────

    [Fact]
    public void Flottenabgabe_HalbiertGewinn_AnFraktionen_Erklaerbar()
    {
        var u = World();
        var yard = Yard(u);
        var fleet = Own(u, "mule", yard);
        var good = u.FindGood("steel")!.Id;
        var seller = u.StationsById().First(s => s.Market.TryGetValue(good, out var e) && e.Amount > 20f && s.Produces(good));
        var buyer = u.StationsById().First(s => s.Consumes(good) && u.IsNpcFaction(u.Sectors[s.Sector].Owner));
        buyer.Credits = 1_000_000f;
        var wallet = u.WalletOf(fleet);
        var ev = Capture(u);
        float w0 = wallet.Credits;
        var b = PlayerLedger.BookBuy(u, fleet, seller, good, 10f, wallet, 10f);
        var s = PlayerLedger.BookSale(u, fleet, buyer, good, 10f, wallet, null, 30f);
        float profit = s.Money - b.Money;
        Assert.Equal(profit, u.WelleB!.FleetPending, 2);
        // aktives Schiff zählt nicht
        PlayerLedger.BookSale(u, u.Ships[u.PlayerShip], buyer, good, 0f, wallet, null, 30f);
        Assert.Equal(profit, u.WelleB.FleetPending, 2);
        float levy = FleetLevy.Settle(u);
        Assert.Equal(profit * 0.5f, levy, 2);
        Assert.Equal(w0 + profit - levy, wallet.Credits, 1);
        var paid = ev.OfType<FleetLevyPaid>().Single();
        float toTreasuries = ev.OfType<TreasuryIncome>().Where(t => t.Purpose == "fleet_levy").Sum(t => t.Amount);
        Assert.Equal(paid.ToFactions, toTreasuries, 2);
        Assert.Equal(levy, paid.ToFactions + paid.Sunk, 2);
        Assert.Equal(0f, u.WelleB.FleetPending);
        // Verlust wird vorgetragen
        PlayerLedger.BookBuy(u, fleet, seller, good, 5f, wallet, 10f);
        Assert.Equal(0f, FleetLevy.Settle(u));
        Assert.True(u.WelleB.FleetPending < 0f);
    }

    [Fact]
    public void Flottenabgabe_LaengerAutomatik_EtwaHalbiert()
    {
        float Earn(float share)
        {
            var u = World(r => r.PlayerFleetLevyShare = share);
            var yard = Yard(u);
            for (int i = 0; i < 4; i++)
            {
                var s = Own(u, "mule", yard);
                Assert.True(Commands.Give(u, s, new TradeCommand(new AutoLimits(yard.Sector, 3, 1.3f, 0f)), out var msg, ignoreCapacity: true), msg);
            }
            float w0 = u.PlayerWallet.Credits;
            new SimulationRunner().Run(u, 7200);
            return u.PlayerWallet.Credits - w0;
        }
        float without = Earn(0f), with = Earn(0.5f);
        Assert.True(without > 0f, $"ohne Abgabe {without}");
        _out.WriteLine($"Flottenautomatik 7200 Ticks: ohne Abgabe {without:0}, mit {with:0}");
        Assert.True(with < without * 0.75f, $"mit {with} ohne {without}");
    }

    // ───────── 4: Spende ─────────

    [Fact]
    public void Spende_PerComms_AnsehenMitTagesgrenze()
    {
        var u = World();
        var comms = CommsActions.Default();
        var f = u.NpcFactionIds()[0];
        var me = u.PlayerFaction!.Value;
        Relations.Set(u, f, me, 0f); Relations.Set(u, me, f, 0f);
        var ev = Capture(u);
        float w0 = u.PlayerWallet.Credits, t0 = u.Actors[new ActorId(f.Value)].Treasury.Credits;
        string k = DonationAction.KeyFor(f, 1000f);
        Assert.True(comms.Preview(u, k).Possible);
        var r = comms.Execute(u, k);
        Assert.True(r.Success, r.Message);
        Assert.Equal(w0 - 1000f, u.PlayerWallet.Credits, 1);
        Assert.Equal(t0 + 1000f, u.Actors[new ActorId(f.Value)].Treasury.Credits, 1);
        Assert.Equal(0.02f, Relations.Effective(u, f, me), 3);
        Assert.Contains(ev.OfType<TreasuryIncome>(), x => x.Purpose == "donation");
        // Tagesgrenze +0,1
        Assert.True(comms.Execute(u, DonationAction.KeyFor(f, 5000f)).Success);
        Assert.Equal(0.1f, Relations.Effective(u, f, me), 3);
        int n = ev.Count;
        float w1 = u.PlayerWallet.Credits;
        var refused = comms.Execute(u, k);
        Assert.False(refused.Success);
        Assert.Equal(w1, u.PlayerWallet.Credits);
        Assert.Equal(n, ev.Count);
        // nächster Tag wieder möglich
        u.Tick += u.Rules.DayTicks;
        Assert.True(comms.Execute(u, k).Success);
        // strenge Schlüssel, unerlaubter Betrag
        Assert.False(comms.Execute(u, $"donate:{f.Value}:1234").Success);
        Assert.False(comms.Execute(u, $"donate:{f.Value}:x").Success);
        Assert.False(comms.Execute(u, "donate:99:1000").Success);
    }

    [Fact]
    public void Spende_ImKrieg_UndUeberObergrenze_Verweigert()
    {
        var u = World();
        var f = u.NpcFactionIds()[1];
        var me = u.PlayerFaction!.Value;
        Relations.Set(u, f, me, 0.45f);
        Assert.NotNull(Donations.Issue(u, f, 1000f));
        Relations.Set(u, f, me, 0f);
        Assert.Null(Donations.Issue(u, f, 1000f));
        Wars.PlayerDeclare(u, f);
        Assert.NotNull(Donations.Issue(u, f, 1000f));
        u.Rules.Donations = false;
        Assert.DoesNotContain(CommsActions.Default().Offers(u), o => o.Key.StartsWith("donate:"));
    }

    // ───────── 5: Aufträge bei schlechter Beziehung ─────────

    static (int Count, ulong Hash) Offers(float chance, float rel)
    {
        var u = World(r => r.MissionLowStandingExtraChance = chance);
        var me = u.PlayerFaction!.Value;
        foreach (var f in u.NpcFactionIds()) { Relations.Set(u, f, me, rel); Relations.Set(u, me, f, rel); }
        int n = 0;
        u.Events.Raised += e => { if (e is MissionOffered mo && mo.Type is TransportMission.KeyId or DeliveryMission.KeyId) n++; };
        new SimulationRunner().Run(u, 5000);
        return (n, UniverseHash.Compute(u));
    }

    [Fact]
    public void Auftraege_SchlechteBeziehung_MehrTransportUndLieferung_HashGleich()
    {
        var with = Offers(0.5f, -0.3f);
        var without = Offers(0f, -0.3f);
        _out.WriteLine($"Transport/Lieferung bei Beziehung −0,3: mit {with.Count}, ohne {without.Count}");
        Assert.True(with.Count > without.Count * 1.1f, $"mit {with.Count}, ohne {without.Count}");
        Assert.Equal(without.Hash, with.Hash);   // Angebote sind nicht gehasht
        var good = Offers(0.5f, 0.2f);
        var goodOff = Offers(0f, 0.2f);
        // gute Beziehung: nur die Banditen bieten zusätzlich an
        Assert.True(good.Count - goodOff.Count < with.Count - without.Count);
    }

    [Fact]
    public void Auftraege_Banditen_BietenTransportAn()
    {
        var u = World();
        var bandit = u.Factions.Keys.First(f => Bandits.IsEstablished(u, f));
        int n = 0;
        u.Events.Raised += e => { if (e is MissionOffered mo && mo.Client.Value == bandit.Value) n++; };
        int nOff = 0;
        var v = World(r => r.MissionLowStandingExtraChance = 0f);
        v.Events.Raised += e => { if (e is MissionOffered mo && mo.Client.Value == bandit.Value) nOff++; };
        new SimulationRunner().Run(u, 5000);
        new SimulationRunner().Run(v, 5000);
        Assert.True(n > nOff, $"Banditen {n} gegen {nOff}");
    }

    // ───────── 6: Bündnisschwelle ─────────

    [Fact]
    public void Buendnis_Spieler_AbNullKommaFuenf()
    {
        var u = World();
        var me = u.PlayerFaction!.Value;
        var f = u.NpcFactionIds().First(x => ActorRules.PersonalityOf(u, x).Cooperation >= u.Rules.AllianceMinCooperation);
        Relations.Set(u, f, me, 0.52f); Relations.Set(u, me, f, 0.52f);
        Assert.Null(Alliances.FormIssue(u, me, f));
        u.Rules.PlayerAllianceMinRelation = 0f;
        Assert.Contains("Beziehung zu niedrig", Alliances.FormIssue(u, me, f));
        // KI-Paare unverändert
        u.Rules.PlayerAllianceMinRelation = 0.5f;
        var g = u.NpcFactionIds().First(x => x != f);
        Relations.Set(u, f, g, 0.52f); Relations.Set(u, g, f, 0.52f);
        Assert.Contains("Beziehung zu niedrig", Alliances.FormIssue(u, f, g) ?? "Beziehung zu niedrig");
    }

    // ───────── 7: Kaufwarnung ─────────

    [Fact]
    public void Werft_WarntUeberBefehlskapazitaet_KeinVerbot()
    {
        var u = World();
        var yard = Yard(u);
        var me = u.Ships[u.PlayerShip];
        me.DockedAt = yard.Id; me.Sector = yard.Sector; me.Position = yard.DockPoint(me.Id);
        Assert.Null(ShipyardWarning.For(u, me));
        var (cap, _) = Commands.Capacity(u, u.PlayerActor);
        for (int i = 0; i < cap; i++) Own(u, "mule", yard);
        string? w = ShipyardWarning.For(u, me);
        Assert.NotNull(w);
        Assert.StartsWith(ShipyardWarning.Text, w);
        var svc = new ShipDealerService();
        var offers = svc.Offers(u, yard, me);
        Assert.All(offers, o => Assert.Equal(w, o.Note));
        var buyable = offers.FirstOrDefault(o => o.Amount >= 1f);
        if (buyable.Key is not null)
        {
            var r = svc.Execute(u, yard, me, new ServiceRequest(buyable.Key, default, 1f));
            Assert.True(r.Success, r.Message);
            Assert.Contains(ShipyardWarning.Text, r.Message);
        }
        u.Rules.ShipyardCapacityWarning = false;
        Assert.Null(ShipyardWarning.For(u, me));
    }

    // ───────── Zustand speichern ─────────

    [Fact]
    public void Speichern_Laden_Bitgleich()
    {
        var u = World();
        var f = u.NpcFactionIds()[0];
        CommsActions.Default().Execute(u, DonationAction.KeyFor(f, 1000f));
        var yard = Yard(u);
        var fleet = Own(u, "mule", yard);
        var good = u.FindGood("steel")!.Id;
        var buyer = u.StationsById().First(s => s.Consumes(good));
        buyer.Credits = 100_000f;
        PlayerLedger.BookSale(u, fleet, buyer, good, 3f, u.WalletOf(fleet), null, 20f);
        var a = SaveGameJson.Read(SaveGameJson.Write(u, Now)).Universe;
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(a));
        Assert.Equal(u.WelleB!.FleetPending, a.WelleB!.FleetPending);
        Assert.Equal(Donations.GainedToday(u, f), Donations.GainedToday(a, f));
        new SimulationRunner().Run(u, 2500); new SimulationRunner().Run(a, 2500);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(a));
    }

    // ───────── 8: Rechenzeit (verhaltensneutral) ─────────

    /// <summary>Alte Ausweichweg-Suche (vor Welle B) als Vergleich: dieselbe Frage über <see cref="PathPlanner.Clear"/>.</summary>
    static List<Vec2>? OldDetour(Vec2 from, Vec2 to, IReadOnlyList<Obstacle> obstacles, float margin)
    {
        var cand = new List<Vec2>();
        foreach (var c in new[] { from, to, (from + to) * 0.5f })
            foreach (float r in new[] { 15f, 30f, 55f, 90f })
                for (int k = 0; k < 16; k++) { float a = k * MathF.PI / 8f; cand.Add(c + new Vec2(MathF.Cos(a), MathF.Sin(a)) * r); }
        cand.RemoveAll(w => obstacles.Any(o => Vec2.Distance(w, o.Position) < o.Radius + margin));
        List<Vec2>? best = null; float bestLen = float.MaxValue;
        var fromOk = new List<Vec2>();
        foreach (var w in cand)
        {
            if (!PathPlanner.Clear(from, w, obstacles, margin)) continue;
            fromOk.Add(w);
            float len = Vec2.Distance(from, w) + Vec2.Distance(w, to);
            if (len < bestLen && PathPlanner.Clear(w, to, obstacles, margin)) { bestLen = len; best = new List<Vec2> { from, w, to }; }
        }
        if (best is not null) return best;
        var toOk = cand.Where(w => PathPlanner.Clear(w, to, obstacles, margin)).ToList();
        foreach (var w1 in fromOk)
            foreach (var w2 in toOk)
            {
                float len = Vec2.Distance(from, w1) + Vec2.Distance(w1, w2) + Vec2.Distance(w2, to);
                if (len < bestLen && PathPlanner.Clear(w1, w2, obstacles, margin)) { bestLen = len; best = new List<Vec2> { from, w1, w2, to }; }
            }
        return best;
    }

    [Fact]
    public void Ausweichweg_Index_GleichesErgebnis()
    {
        var rng = new Random(44);
        int found = 0, none = 0;
        for (int trial = 0; trial < 120; trial++)
        {
            var obs = new List<Obstacle>();
            int n = 10 + rng.Next(70);
            for (int i = 0; i < n; i++) obs.Add(new Obstacle(new Vec2(rng.NextSingle() * 200f - 100f, rng.NextSingle() * 200f - 100f), 2f + rng.NextSingle() * 14f));
            var from = new Vec2(rng.NextSingle() * 200f - 100f, rng.NextSingle() * 200f - 100f);
            var to = new Vec2(rng.NextSingle() * 200f - 100f, rng.NextSingle() * 200f - 100f);
            float margin = trial % 3 == 0 ? 0f : 4f + rng.NextSingle() * 4f;
            var a = PathPlanner.Detour(from, to, obs, margin);
            var b = OldDetour(from, to, obs, margin);   // Rand 0: Kandidatenfilter mit 0, Clear mit dem Standardabstand (wie vorher)
            if (a is null) { none++; Assert.Null(b); continue; }
            found++;
            Assert.NotNull(b);
            Assert.Equal(b!, a);
        }
        Assert.True(found > 10 && none > 0, $"gefunden {found}, keiner {none}");
    }
}
