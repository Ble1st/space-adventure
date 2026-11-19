using SpaceSim.Core.Combat;
using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Mining;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Tests;

/// <summary>Runde 44: Maßstab je Sektor (Technik). Die Weltdatei bleibt im Grundmaßstab; der Aufbau multipliziert die Positionen.</summary>
public class Runde44ScaleTests
{
    /// <summary>
    /// Runde 44 zurückdrehen (für die Referenz-Hash-Tests über <see cref="Phase29Tests.RevertRunde24"/>): Maßstab 1 in allen Sektoren, Stellschrauben der
    /// Messreihe wie bis Runde 43 (Kapazitäten, Gründerflotte ÷ 3, Lagerpuffer, Exportsenken, Überfall-/Polizeireichweite, Auftragsbelohnung je Minute,
    /// Laserkanonen-Regal der Werft).
    /// </summary>
    internal static void RevertRunde44(WorldDef def)
    {
        def.Rules ??= new EconomyRules();
        BalanceFixTests.RevertBalanceFix(def);   // Ausgleich nach dem Bot-Test: neue Regeln aus, Jäger-Masse wie vorher (die Referenz-Hashes stammen von davor)
        SchwarzmarktTests.RevertSchwarzmarkt(def);   // Schwarzmarkt: Beute im Hehlerlager ist gespeicherter Zustand (die Referenz-Hashes stammen von davor)
        SprungTarnTests.RevertSprungTarn(def);   // Sprungantrieb/Tarnfeld: neue Modulwaren, Systemwerk-Rezepte und Werftregale (die Referenz-Hashes stammen von davor)
        foreach (var sd in def.Sectors) sd.Scale = 1f;
        if (def.Rules.MaxTraders == 80) def.Rules.MaxTraders = 26;
        if (def.Rules.MaxMiners == 55) def.Rules.MaxMiners = 22;
        def.Rules.RaidReach = 0f;
        def.Rules.PoliceResponseRange = 0f;
        def.Rules.RecipeStockTarget = 100f;
        def.Rules.ExportDelayTicks = 1200;
        def.Rules.MissionTransportRewardPerMinute = def.Rules.MissionPatrolRewardPerMinute = def.Rules.MissionEscortRewardPerMinute = 0f;
        def.Rules.EscortMinRemainingTicks = 40;
        foreach (var g in def.Ships) if (g.Role is "trader" or "miner" && g.Count % 3 == 0) g.Count /= 3;
        foreach (var t in def.StationTypes) if (t.Export is { TargetValue: 5000f } ex) ex.TargetValue = 3000f;
        if (def.StationTypes.FirstOrDefault(t => t.Key == "shipyard")?.Stocks is { } ys)
        {
            if (ys.ContainsKey("weapon_laser_s")) ys["weapon_laser_s"] = new[] { 4f, 4f };
            if (ys.ContainsKey("shield_mk1")) ys["shield_mk1"] = new[] { 1f, 3f };
            if (ys.ContainsKey("armor_plating_s")) ys["armor_plating_s"] = new[] { 1f, 3f };
        }
    }

    static WorldDef DefWithScale(float scale, int? onlySector = null)
    {
        var def = TestWorlds.ClassicDef();
        def.Rules!.FounderQuality = false;
        foreach (var sd in def.Sectors) if (onlySector is null || sd.Id == onlySector) sd.Scale = scale;
        return def;
    }

    [Fact]
    public void Massstab2_VerdoppeltPositionen_PlatzierungsabstaendeGeltenWeiter()
    {
        var a = WorldBuilder.Build(DefWithScale(1f), 7);
        var b = WorldBuilder.Build(DefWithScale(2f), 7);
        foreach (var st in a.Stations.Values)
        {
            var sb = b.Stations[st.Id];
            Assert.Equal(st.Position.X * 2f, sb.Position.X, 3);
            Assert.Equal(st.Position.Y * 2f, sb.Position.Y, 3);
        }
        foreach (var g in a.Gates.Values) Assert.Equal(g.Position.X * 2f, b.Gates[g.Id].Position.X, 3);
        foreach (var s in a.Sectors.Values)
        {
            var sb = b.Sectors[s.Id];
            Assert.Equal(2f, sb.Scale);
            for (int i = 0; i < s.Fields.Count; i++) Assert.Equal(s.Fields[i].Position.Y * 2f, sb.Fields[i].Position.Y, 3);
            // Hintergrundfelsen: Anzahl × Maßstab; jeder Fels hält die Abstände der Platzierungsregel zu Toren und Stationen
            int bgA = s.Asteroids.Count(x => !x.Minable && x.ClassKey == ""), bgB = sb.Asteroids.Count(x => !x.Minable && x.ClassKey == "");
            Assert.True(bgB > bgA, $"{s.Name}: Hintergrundfelsen {bgA} → {bgB}");
            foreach (var r in sb.Asteroids)
            {
                foreach (var gid in sb.Gates) Assert.True(Vec2.Distance(r.Position, b.Gates[gid].Position) > BeltPlacement.GateClear + r.Radius - 1e-3f);
                foreach (var sid in sb.Stations) Assert.True(Vec2.Distance(r.Position, b.Stations[sid].Position) > b.Stations[sid].Radius + r.Radius);
            }
            // Gürtel: Lage wächst, Ausdehnung nicht — die Felsen eines Felds bleiben im Gürtelradius um seine (verschobene) Mitte
            foreach (var f in sb.Fields)
                foreach (var r in sb.Asteroids.Where(x => x.ClassKey == f.ClassKey && sb.FieldFor(x.Position) == f))
                    Assert.True(Vec2.Distance(r.Position, f.Position) <= MiningRules.BeltRadius + 1f);
            Assert.Equal((s.Extent - SectorExtent.Margin) * 2f, sb.Extent - SectorExtent.Margin, 2);
            Assert.True(sb.ArenaRadius(b.Rules) >= sb.Extent + 100f - 1e-3f);
            Assert.Equal(a.Rules.CombatArenaRadius, s.ArenaRadius(a.Rules));
        }
        // neue Station (Lebenszyklus) findet einen Platz in der vergrößerten Fläche mit denselben Abständen
        var sec = b.Sectors.Values.First();
        var spot = StationLifecycleSystem.FindFreeSpot(b, sec);
        Assert.NotNull(spot);
        foreach (var sid in sec.Stations) Assert.True(Vec2.Distance(spot!.Value, b.Stations[sid].Position) > Station.BodyRadius + b.Stations[sid].Radius + 20f - 1e-3f);
    }

    [Fact]
    public void GroßeGruenderflotte_UeberschreibtDasSpielerschiffNicht()
    {
        var def = TestWorlds.Def(fuel: false, research: false);
        int founders = def.Ships.Sum(g => g.Count);
        Assert.True(founders >= 100, $"Gründerflotte {founders}");   // die Standardwelt hat seit Runde 44 mehr als 99 Gründerschiffe
        var u = WorldBuilder.Build(def, 1);
        Assert.Equal(founders + 1, u.Ships.Count);
        Assert.True(u.Ships[new ShipId(100)].IsPlayer);
        Assert.Equal(def.Ships.Where(g => g.Role == "builder").Sum(g => g.Count), u.Ships.Values.Count(s => s.Role == ShipRole.Builder));
    }

    [Fact]
    public void Massstab1_HashUndAufbauUnveraendert()
    {
        var a = WorldBuilder.Build(TestWorlds.ClassicDef(), 3);
        var def = TestWorlds.ClassicDef();
        foreach (var sd in def.Sectors) sd.Scale = 1f;
        var b = WorldBuilder.Build(def, 3);
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(b));
        Assert.All(b.Sectors.Values, s => Assert.Equal(b.Rules.CombatArenaRadius, s.ArenaRadius(b.Rules)));
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(11f)]
    [InlineData(float.NaN)]
    public void Massstab_Ungueltig_WirftMitSektorname(float scale)
    {
        var def = TestWorlds.ClassicDef();
        def.Sectors[0].Scale = scale;
        var ex = Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(def, 1));
        Assert.Contains(def.Sectors[0].Name, ex.Message);
    }

    [Fact]
    public void Speichern_Laden_MitMassstab_Bitgleich()
    {
        var u = WorldBuilder.Build(DefWithScale(3f, onlySector: 2), 5);
        var runner = new SimulationRunner();
        for (int i = 0; i < 300; i++) runner.Step(u);
        var (v, _) = SaveGameJson.Read(SaveGameJson.Write(u, DateTimeOffset.UnixEpoch));
        Assert.Equal(3f, v.Sectors[new SectorId(2)].Scale);
        Assert.Equal(u.Sectors[new SectorId(2)].Extent, v.Sectors[new SectorId(2)].Extent, 3);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
        var r2 = new SimulationRunner();
        for (int i = 0; i < 300; i++) { runner.Step(u); r2.Step(v); }
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
    }

    [Fact]
    public void Gefechtsflaeche_KeinTeleportImGrossenSektor()
    {
        var u = WorldBuilder.Build(DefWithScale(4f), 2);
        var sec = u.Sectors.Values.OrderByDescending(s => s.Extent).First();
        // ein Punkt 900 m vom Ursprung in Richtung der äußersten Station: Clear schiebt höchstens aus Hindernissen, nie auf 400 zurück
        var far = sec.Stations.Select(id => u.Stations[id]).OrderByDescending(s => s.Position.Length).First();
        var dir = far.Position.Normalized();
        var p = dir * 900f + new Vec2(dir.Y, -dir.X) * 37f;
        var c = CombatSpace.Clear(u, sec.Id, p);
        Assert.True(Vec2.Distance(c, p) < 40f, $"Clear verschob {p} nach {c} (Gefechtsfläche {sec.ArenaRadius(u.Rules):0})");
    }

    [Fact]
    public void Ueberfallreichweite_NurOpferInReichweite()
    {
        var u = TestWorlds.Classic(4);
        var sector = u.Sectors.Values.First();
        var raider = u.Ships.Values.First(s => ThreatMap.IsRaider(s));
        raider.Transit = null; raider.Order = null; raider.Command = null;
        if (raider.Sector != sector.Id) u.MoveShip(raider, sector.Id);
        raider.Position = new Vec2(0, 0);
        var trader = u.Ships.Values.First(s => s.IsEconomyAi && s.Role == ShipRole.Trader);
        foreach (var g in trader.Cargo.Keys.ToList()) trader.Cargo[g] = 0;
        trader.Cargo[new GoodId(1)] = 50;
        trader.Transit = null; trader.Order = null;
        if (trader.Sector != sector.Id) u.MoveShip(trader, sector.Id);
        trader.Position = new Vec2(200, 200);
        u.Rules.MinLootValue = 1f;
        bool Has() => PirateSystem.Victims(u, sector.Id, raider.Position).Any(v => v.Ship.Id == trader.Id);
        if (ThreatMap.NearStation(u, sector.Id, trader.Position)) return;   // Startlage zufällig im Schutzradius: nichts zu prüfen
        u.Rules.RaidReach = 0f;
        Assert.True(Has());
        u.Rules.RaidReach = 100f;
        Assert.False(Has());
        u.Rules.RaidReach = 400f;
        Assert.True(Has());
    }
}
