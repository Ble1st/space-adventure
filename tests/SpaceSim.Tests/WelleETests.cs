using SpaceSim.Core;
using SpaceSim.Core.Content;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Welle E (docs/KAMPF.md „Welle E: Großkampfschiffe für NPCs“): Bauaufträge für Großkampfschiffe (Leviathan) an Werften — nur unter Bedingungen,
/// Treuhand nie unter der Rücklage, Nachfrage über gehobene Regalziele, Fertigstellung, Verfall mit Rückerstattung, Speichern/Laden.
/// </summary>
public class WelleETests
{
    static readonly FactionId A = new(1), B = new(2);

    /// <summary>Welle E zurücknehmen (zuerst in <see cref="SchutzKonvoiTests.RevertSchutz"/>): keine Großkampfschiff-Aufträge.</summary>
    internal static void RevertWelleE(WorldDef def)
    {
        var r = def.Rules ??= new EconomyRules();
        r.CapitalOrders = false;
        r.CapitalKeepInPeace = false;
    }

    static Universe World()
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 1);
        return u;
    }

    static Actor Fac(Universe u, FactionId f) => u.Actors[new ActorId(f.Value)];

    static List<SimEvent> Capture(Universe u)
    {
        var list = new List<SimEvent>();
        u.Events.Raised += list.Add;
        return list;
    }

    /// <summary>Kasse so setzen, dass genau <paramref name="spendable"/> verfügbar ist.</summary>
    static void SetSpendable(Universe u, FactionId f, float spendable)
    {
        var a = Fac(u, f);
        a.Treasury.Credits = 0f;
        a.Treasury.Credits = FactionViability.ReserveOf(u, a) + FactionViability.ReplacementCost(u, f) + spendable;
    }

    static float LeviathanPrice(Universe u, out Station yard)
    {
        var m = u.ShipModels["battlecruiser"];
        yard = u.StationsById().First(s => ShipPurchase.SellsModel(u, s, m) && u.FactionOf(s) == A);
        return CapitalOrders.PriceOf(u, yard, ShipPurchase.Materials(u, m)!);
    }

    [Fact]
    public void Schalter_CodeAus_WeltAn_RevertAus()
    {
        Assert.False(new EconomyRules().CapitalOrders);
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        Assert.True(WorldBuilder.Build(def, 1).Rules.CapitalOrders);
        Assert.Contains("battlecruiser", WorldBuilder.Build(def, 1).Rules.CapitalModels);
        RevertWelleE(def);
        Assert.False(WorldBuilder.Build(def, 1).Rules.CapitalOrders);
    }

    [Fact]
    public void LeviathanImRegal_FuerNpcsNieLieferbar_OhneAuftrag()
    {
        // Messung zum Anlass (Welle D, hier am Start): kein Werftlager hat alles für den Leviathan
        var u = World();
        var m = u.ShipModels["battlecruiser"];
        var needs = ShipPurchase.Materials(u, m)!;
        foreach (var yard in u.StationsById().Where(s => ShipPurchase.SellsModel(u, s, m)))
            Assert.NotEmpty(ShipPurchase.Missing(yard, needs));
    }

    [Fact]
    public void Auftrag_NurUnterBedingungen()
    {
        var u = World();
        u.Rules.CapitalRichFactor = 0f; u.Rules.CapitalThreat = 0f;
        float price = LeviathanPrice(u, out _);
        SetSpendable(u, A, price * 10f);
        // Frieden, kein Bedrohungs-/Reichtumsauslöser: kein Auftrag
        Assert.Null(CapitalOrders.TryPlace(u, A));
        // reich genug (Faktor 6)
        u.Rules.CapitalRichFactor = 6f;
        SetSpendable(u, A, price * 5f);
        Assert.Null(CapitalOrders.TryPlace(u, A));
        SetSpendable(u, A, price * 6.5f);
        var o = CapitalOrders.TryPlace(u, A);
        Assert.NotNull(o);
        // höchstens ein Auftrag je Fraktion, Abklingzeit, Obergrenze
        SetSpendable(u, A, price * 20f);
        Assert.Null(CapitalOrders.TryPlace(u, A));
        // Bedrohung als Auslöser (Fraktion B)
        u.Rules.CapitalRichFactor = 0f; u.Rules.CapitalThreat = 4f;
        SetSpendable(u, B, price * 2f);
        Assert.Null(CapitalOrders.TryPlace(u, B));
        u.Sectors[u.Factions[B].Territory.OrderBy(s => s.Value).First()].Threat = 5f;
        Assert.NotNull(CapitalOrders.TryPlace(u, B));
        // Schalter aus: nichts
        var v = World();
        v.Rules.CapitalOrders = false;
        Wars.Declare(v, B, A);
        SetSpendable(v, A, price * 10f);
        Assert.Null(CapitalOrders.TryPlace(v, A));
        // Krieg als Auslöser
        v.Rules.CapitalOrders = true;
        Assert.NotNull(CapitalOrders.TryPlace(v, A));
    }

    [Fact]
    public void Treuhand_NieUnterRuecklage()
    {
        var u = World();
        Wars.Declare(u, B, A);
        float price = LeviathanPrice(u, out _);
        var a = Fac(u, A);
        SetSpendable(u, A, price - 1f);
        Assert.Null(CapitalOrders.TryPlace(u, A));
        SetSpendable(u, A, price + 1f);
        var ev = Capture(u);
        var o = CapitalOrders.TryPlace(u, A);
        Assert.NotNull(o);
        Assert.True(a.Treasury.Credits >= FactionViability.ReserveOf(u, a) + FactionViability.ReplacementCost(u, A) - 0.01f);
        Assert.Equal(price, o!.Escrow, 1);
        Assert.Contains(ev, e => e is TreasuryUpkeep { Purpose: "capital_order" } t && MathF.Abs(t.Amount - price) < 0.5f);
        Assert.Contains(ev, e => e is CapitalOrderPlaced { Reason: "war" });
    }

    [Fact]
    public void Nachfrage_HebtRegalziele_UndOffeneNachfrage()
    {
        var u = World();
        Wars.Declare(u, B, A);
        float price = LeviathanPrice(u, out _);
        SetSpendable(u, A, price * 2f);
        var yardBefore = new Dictionary<int, float>();
        var o = CapitalOrders.TryPlace(u, A)!;
        var yard = u.Stations[o.Yard];
        var laser = u.FindGood("weapon_laser_m")!.Id;
        var before = yard.Market[laser].Target;
        CapitalOrders.Step(u);   // Reservieren und Ziele setzen
        float res = o.Reserved.GetValueOrDefault(laser.Value);
        Assert.Equal(6f - res, o.Raised.GetValueOrDefault(laser.Value), 3);
        Assert.True(yard.Market[laser].Target >= 6f - res);
        // Offene Nachfrage der Welt nach Laserkanonen M enthält den Aufschlag
        var open = new float[u.GoodIdBound + 1];
        ProductionDemand.OpenDemand(u, open);
        Assert.True(open[laser.Value] >= 6f - res - yard.Market[laser].Amount - 0.01f);
        _ = before; _ = yardBefore;
    }

    static void FillYard(Universe u, CapitalOrder o)
    {
        var yard = u.Stations[o.Yard];
        foreach (var (g, need) in o.Need)
        {
            var gid = new GoodId(g);
            if (!yard.Market.TryGetValue(gid, out var e)) { e = new StockEntry { Target = 0f }; yard.Market[gid] = e; }
            e.Amount += need;
        }
    }

    [Fact]
    public void Fertigstellung_SchiffGehoertDerFraktion_ZieleZurueck_WerftBezahlt()
    {
        var u = World();
        Wars.Declare(u, B, A);
        float price = LeviathanPrice(u, out _);
        SetSpendable(u, A, price * 2f);
        var o = CapitalOrders.TryPlace(u, A)!;
        var yard = u.Stations[o.Yard];
        var baseTargets = o.Need.Keys.ToDictionary(g => g, g => yard.Market.TryGetValue(new GoodId(g), out var e) ? e.Target - o.Raised.GetValueOrDefault(g) : 0f);
        FillYard(u, o);
        float credits = yard.Credits;
        var ev = Capture(u);
        CapitalOrders.Step(u);
        Assert.True(o.AssemblyStart >= 0);
        Assert.Equal(1f, o.MaterialShare, 3);
        Assert.Empty(o.Raised);
        Assert.Contains("Montage", CapitalOrders.InfoLine(u, yard));
        u.Tick += u.Rules.CapitalAssemblyTicks;
        CapitalOrders.Step(u);
        Assert.Empty(u.CapitalOrders!.Orders);
        var done = ev.OfType<CapitalShipCompleted>().Single();
        var ship = u.Ships[done.Ship];
        Assert.Equal("battlecruiser", ship.ModelKey);
        Assert.Equal(ShipRole.Warship, ship.Role);
        Assert.Equal(A, u.FactionOf(ship));
        Assert.True(CapitalOrders.IsCapital(u, ship));
        Assert.NotEqual(default, ship.HomeBase);
        Assert.Equal(price, yard.Credits - credits, 0);
        foreach (var (g, t) in baseTargets)
            if (yard.Market.TryGetValue(new GoodId(g), out var e)) Assert.Equal(t, e.Target, 3);
        Assert.Null(CapitalOrders.InfoLine(u, yard));
        // Obergrenze: ein Großkampfschiff je Fraktion
        u.Tick += u.Rules.CapitalOrderCooldownTicks;
        SetSpendable(u, A, price * 5f);
        Assert.Null(CapitalOrders.TryPlace(u, A));
    }

    [Fact]
    public void Frieden_GrosskampfschiffBleibt_AbbauNurUnterRuecklage()
    {
        var u = World();
        var w = Wars.Declare(u, B, A);
        float price = LeviathanPrice(u, out _);
        SetSpendable(u, A, price * 2f);
        var o = CapitalOrders.TryPlace(u, A)!;
        FillYard(u, o);
        CapitalOrders.Step(u);
        u.Tick += u.Rules.CapitalAssemblyTicks;
        var ev = Capture(u);
        CapitalOrders.Step(u);
        var id = ev.OfType<CapitalShipCompleted>().Single().Ship;
        u.Wars.Remove(w);
        u.Tick += u.Rules.WarshipPeaceTicks + 100;
        SetSpendable(u, A, 100000f);
        new NavySystem().Step(u, 100);
        Assert.True(u.Ships.ContainsKey(id));
        u.Rules.CapitalKeepInPeace = false;
        for (int i = 0; i < 20 && u.Ships.ContainsKey(id); i++) new NavySystem().Step(u, 100);
        Assert.False(u.Ships.ContainsKey(id));
    }

    [Fact]
    public void Verfall_ErstattetUnbezahltenRest_WareZurueck()
    {
        var u = World();
        Wars.Declare(u, B, A);
        float price = LeviathanPrice(u, out _);
        SetSpendable(u, A, price * 2f);
        var a = Fac(u, A);
        float treasuryAfterOrder;
        var o = CapitalOrders.TryPlace(u, A)!;
        treasuryAfterOrder = a.Treasury.Credits;
        var yard = u.Stations[o.Yard];
        // nur die Bausätze liefern
        var kit = u.FindGood("ship_kit_heavy")!.Id;
        foreach (var g in o.Need.Keys.ToList()) if (yard.Market.TryGetValue(new GoodId(g), out var e0)) e0.Amount = 0f;
        yard.Market[kit].Amount = 3f;
        CapitalOrders.Step(u);
        Assert.Equal(3f, o.Reserved[kit.Value]);
        float paid = price - o.Escrow;
        Assert.True(paid > 0f);
        u.Tick = o.Expires;
        var ev = Capture(u);
        CapitalOrders.Step(u);
        var end = ev.OfType<CapitalOrderEnded>().Single();
        Assert.Equal("expired", end.Reason);
        Assert.Equal(price - paid, end.Refund, 1);
        Assert.Equal(treasuryAfterOrder + end.Refund, a.Treasury.Credits, 1);
        Assert.Equal(3f, yard.Market[kit].Amount);
        Assert.Empty(u.CapitalOrders!.Orders);
        Assert.DoesNotContain(ev, e => e is CapitalShipCompleted);
    }

    [Fact]
    public void SchalterAus_OhneAusloeser_GleicherHash()
    {
        // Ohne Auslöser (Bedrohungs-/Reichtumsfaktor 0, kein Krieg) erteilt niemand einen Auftrag — die Welt ist bitgleich mit Schalter aus
        var on = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        on.Rules!.CapitalThreat = 0f; on.Rules.CapitalRichFactor = 0f;
        var off = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        RevertWelleE(off);
        off.Rules!.CapitalThreat = 0f; off.Rules.CapitalRichFactor = 0f;
        var u = WorldBuilder.Build(on); var v = WorldBuilder.Build(off);
        new SimulationRunner().Run(u, 4000);
        new SimulationRunner().Run(v, 4000);
        Assert.Null(u.CapitalOrders);
        // Regeln unterscheiden sich nur in den Schaltern (die Regeln selbst sind nicht im Hash) — Zustand gleich
        Assert.Equal(UniverseHash.Compute(v), UniverseHash.Compute(u));
    }

    [Fact]
    public void SpeichernLaden_MitLaufendemAuftrag_Bitgleich()
    {
        var u = World();
        Wars.Declare(u, B, A);
        float price = LeviathanPrice(u, out _);
        SetSpendable(u, A, price * 3f);
        var o = CapitalOrders.TryPlace(u, A)!;
        var yard = u.Stations[o.Yard];
        var kit = u.FindGood("ship_kit_heavy")!.Id;
        yard.Market[kit].Amount = 2f;
        CapitalOrders.Step(u);
        var json = SaveGameJson.Write(u, DateTimeOffset.UnixEpoch);
        var (v, _) = SaveGameJson.Read(json);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
        Assert.Single(v.CapitalOrders!.Orders);
        Assert.Equal(o.Escrow, v.CapitalOrders.Orders[0].Escrow);
        new SimulationRunner().Run(u, 600);
        new SimulationRunner().Run(v, 600);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
    }

    [Fact]
    public void Kriegslauf_AuftragWirdErteilt_UndKommtVoran()
    {
        // Kleinere Gesamtprobe: Krieg + volle Kassen → Auftrag; nach einiger Zeit ist Material reserviert oder das Schiff fertig
        var u = World();
        Wars.Declare(u, B, A);
        Fac(u, A).Treasury.Credits = 2_000_000f;
        var ev = Capture(u);
        new SimulationRunner().Run(u, 8000);
        Assert.Contains(ev, e => e is CapitalOrderPlaced p && p.Faction == A);
        bool progressed = ev.Any(e => e is CapitalShipCompleted) || (u.CapitalOrders?.Orders.Any(x => x.ReservedTotal > 0f) ?? false);
        Assert.True(progressed);
        foreach (var f in u.NpcFactionIds()) Assert.True(Fac(u, f).Treasury.Credits >= 0f);
    }
}
