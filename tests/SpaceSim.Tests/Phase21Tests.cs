using SpaceSim.Core;
using SpaceSim.Core.Content;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Tests;

/// <summary>
/// Phase 21: Die Standardwelt hat 11 statt 4 Sektoren — Ringe statt Stern, Rohstoffe in mehreren Sektoren, drei Werften, vier Planeten mit
/// Exportsenke, eine dritte Fraktion (Montanbund), zwei herrenlose Sektoren und eine zweite Piratengruppe. Alles Bisherige (Sektoren 1–4,
/// Stationen 1–40, Tore 1–6) ist unverändert, alles Neue angehängt.
/// </summary>
public class Phase21Tests
{
    static readonly string[] Ores = { "iron_ore", "ice", "copper_ore", "silicon", "titanium_ore", "platinum_ore" };
    /// <summary>Die beiden Sackgassen (Aurelia, Abgrund) und ihre Tore.</summary>
    static readonly int[] DeadEnds = { 8, 11 };

    static Good G(Universe u, string key) => u.FindGood(key)!;

    // ───────── Inhalt ─────────

    [Fact]
    public void Welt_ElfSektoren_BestehendeIdsUnveraendert()
    {
        var u = TestWorlds.Default(null, fuel: false, research: false);   // Phase 29: feste Fraktionsnamen (die Standardwelt würfelt sie)
        var classic = TestWorlds.Classic();
        Assert.Equal(11, u.Sectors.Count);
        Assert.Equal(Enumerable.Range(1, 11), u.Sectors.Keys.Select(k => k.Value).OrderBy(x => x));
        Assert.Equal(u.Sectors.Count, u.Sectors.Values.Select(s => s.Name).Distinct().Count());
        // Sektoren, Stationen und Tore der klassischen Welt: gleiche Ids, Namen, Typen, Orte
        foreach (var s in classic.Sectors.Values)
        {
            Assert.Equal(s.Name, u.Sectors[s.Id].Name);
            Assert.Equal(s.MapPosition, u.Sectors[s.Id].MapPosition);
            Assert.Equal(s.Owner, u.Sectors[s.Id].Owner);
        }
        foreach (var st in classic.Stations.Values)
        {
            var n = u.Stations[st.Id];
            // Phase 29: Stationen mit dem Fraktionsnamen heißen nach der Vorlage „Typ – {owner}“
            bool templated = n.Name.Contains(" – ");
            // Phase 30: zwei Startstationen für die Größenklassen verschoben (Hauptquartier Alpha XL weg vom Tor, Elektrolyse Gamma weg vom Tor)
            var moved = new Dictionary<int, Vec2> { [32] = new Vec2(136f, -4f), [13] = new Vec2(-75f, 25f), [18] = new Vec2(0f, 66f) };
            var expected = moved.TryGetValue(st.Id.Value, out var mp) ? mp : st.Position;
            Assert.Equal((templated ? n.Name : st.Name, st.TypeKey, st.Sector, expected), (n.Name, n.TypeKey, n.Sector, n.Position));
            if (templated) Assert.Contains(u.ActorName(n.Owner), n.Name);
        }
        foreach (var g in classic.Gates.Values)
        {
            var n = u.Gates[g.Id];
            Assert.Equal((g.Sector, g.Target, g.Position, g.MainRoute), (n.Sector, n.Target, n.Position, n.MainRoute));
        }
        // Neues ist angehängt
        Assert.True(u.Stations.Keys.Where(k => !classic.Stations.ContainsKey(k)).All(k => k.Value > 40));
        Assert.True(u.Gates.Keys.Where(k => !classic.Gates.ContainsKey(k)).All(k => k.Value > 6));
        // Waren-Ids der JSON-Waren unverändert; Phase 23 hängt die Munition an (Modul-Waren rücken dahinter, gleiche Reihenfolge nach Schlüssel)
        var classicJson = classic.Goods.Values.Where(g => !classic.Modules.ContainsKey(g.Key)).Select(g => (g.Id, g.Key)).ToList();
        Assert.Equal(classicJson, u.Goods.Values.Where(g => g.Id.Value <= classicJson.Count).Select(g => (g.Id, g.Key)));
        Assert.Equal(classic.Goods.Values.Where(g => classic.Modules.ContainsKey(g.Key)).Select(g => g.Key),
            u.Goods.Values.Where(g => u.Modules.ContainsKey(g.Key) && classic.Modules.ContainsKey(g.Key)).Select(g => g.Key));
    }

    [Fact]
    public void Welt_DritteFraktion_HerrenloseSektoren_ZweitePiratengruppe()
    {
        var u = TestWorlds.Default(null, fuel: false, research: false);   // Phase 29: feste Fraktionsnamen
        Assert.Equal(3, u.Factions.Count);
        var mb = u.Factions[new FactionId(3)];
        Assert.Equal("Montanbund", mb.Name);
        Assert.Equal(ActorKind.Guild, u.Actors[mb.Id].Kind);
        Assert.Equal(new[] { 7, 8 }, mb.Territory.Select(s => s.Value).OrderBy(x => x));
        // jede Fraktion: Hauptquartier, Polizei, Bauschiff, Gebiet
        foreach (var f in u.Factions.Values)
        {
            Assert.Contains(u.Stations.Values, st => st.TypeKey == "headquarters" && u.FactionOf(st) == f.Id);
            Assert.True(u.Ships.Values.Count(s => s.Role == ShipRole.Police && s.Owner == (ActorId)f.Id) >= 2, f.Name);
            Assert.Contains(u.Ships.Values, s => s.Role == ShipRole.Builder && s.Owner == (ActorId)f.Id);
            Assert.True(f.Territory.Count >= 2, f.Name);
        }
        // herrenlos: Nebelmark und Abgrund — keine Fraktion, in keinem Gebiet, Stufe keine
        foreach (int id in new[] { 9, 11 })
        {
            var s = u.Sectors[new SectorId(id)];
            Assert.Equal(0, s.Owner.Value);
            Assert.DoesNotContain(u.Factions.Values, f => f.Territory.Contains(s.Id));
            Assert.Equal(ControlStage.None, s.Control);
        }
        // zwei Piratengruppen mit je einem Versteck und Kaperschiffen; das zweite Versteck liegt in einer Sackgasse
        var outlaws = u.Actors.Values.Where(a => a.Kind == ActorKind.Outlaw).OrderBy(a => a.Id.Value).ToList();
        Assert.Equal(2, outlaws.Count);
        foreach (var o in outlaws)
        {
            var hideout = Assert.Single(u.Stations.Values, st => st.Owner == o.Id && u.StationTypes[st.TypeKey].Hideout);
            Assert.Contains(u.Ships.Values, s => s.Role == ShipRole.Pirate && s.Owner == o.Id);
            if (o.Name == "Schwarze Klingen") Assert.Equal(11, hideout.Sector.Value);
        }
        // Beziehungen in der Startwelt freundlich (keine Sperre entsteht von selbst)
        foreach (var a in u.Factions.Keys)
            foreach (var b in u.Factions.Keys.Where(b => b != a))
                Assert.True(Relations.Effective(u, a, b) > 0.1f, $"{a.Value}→{b.Value}");
    }

    [Fact]
    public void WorldBuilder_UnbekannterBesitzer_WirftWeiterhin_NullIstHerrenlos()
    {
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        def.Sectors.First(s => s.Id == 5).Owner = 42;
        var ex = Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(def, null));
        Assert.Contains("unbekannte Fraktion 42", ex.Message);
    }

    [Fact]
    public void Rohstoffe_JedesErzInMehrerenSektoren_KeineEinzelsperreNimmtEsDemRest()
    {
        var u = DemoUniverse.Create();
        foreach (var key in Ores)
        {
            var good = G(u, key).Id;
            var sectors = u.Sectors.Values.Where(s => s.Fields.Any(f => u.AsteroidClasses[f.ClassKey].Yields.Any(y => y.Good == good)))
                .Select(s => s.Id).ToHashSet();
            Assert.True(sectors.Count >= 2, $"{key}: nur {sectors.Count} Sektor(en)");
            // für jedes Tor: schließt man es, hat der größte Kartenteil das Erz noch
            foreach (var g in u.Gates.Values.Where(g => g.Id.Value < g.Target.Value))
            {
                var parts = MapParts.IfClosed(u, g.Id);
                var biggest = parts.Parts.OrderByDescending(p => p.Count).First();
                Assert.True(biggest.Any(sectors.Contains), $"{key}: Tor {g.Id.Value} schneidet das Erz ab");
            }
        }
        // Platin und Eis nicht mehr nur in einem Sektor (bis Phase 20: Platin nur Delta, Eis nur Alpha/Gamma)
        Assert.True(u.Sectors.Values.Count(s => s.Fields.Any(f => f.ClassKey == "rare_rock")) >= 3);
        Assert.True(u.Sectors.Values.Count(s => s.Fields.Any(f => f.ClassKey == "ice_rock")) >= 4);
        // Profile: nicht jeder Sektor hat alles
        Assert.DoesNotContain(u.Sectors.Values, s => s.Fields.Select(f => f.ClassKey).Distinct().Count() >= 3);
    }

    [Fact]
    public void Stationen_JedeWareHergestellt_WichtigeKettenDoppelt_DreiWerften_VierPlaneten()
    {
        var u = DemoUniverse.Create();
        var producers = new Dictionary<GoodId, List<Station>>();
        foreach (var st in u.Stations.Values)
            foreach (var r in st.Production)
                foreach (var g in r.Outputs.Keys)
                    (producers.TryGetValue(g, out var l) ? l : producers[g] = new List<Station>()).Add(st);
        // jede Ware mit Rezept-Erzeuger hat in der Startwelt einen (Treibstoff ist abgeschaltet)
        foreach (var good in u.Goods.Values.Where(g => g.Key != "propellant" && u.StationTypes.Values.Any(t => t.Recipes.Any(r => r.Outputs.ContainsKey(g.Id)))))
            Assert.True(producers.ContainsKey(good.Id), $"{good.Key}: keine Station stellt es her");
        // Redundanz durch Größe: Grundketten mindestens zweimal, in verschiedenen Sektoren
        foreach (var key in new[] { "steel", "water", "hydrogen", "copper", "wafers", "titanium", "platinum", "electronics", "hull_plates", "fuel_cells",
                     "life_support", "food", "consumer_goods", "ship_kit_light", "energy" })
            Assert.True(producers[G(u, key).Id].Select(s => s.Sector).Distinct().Count() >= 2, $"{key}: nur in einem Sektor hergestellt");
        Assert.True(producers[G(u, "food").Id].Count >= 4, "mehrere Farmen");
        Assert.True(producers[G(u, "water").Id].Count >= 3, "mehrere Wasserwerke");
        // 2–3 Werften in verschiedenen Sektoren, keine zwei hinter derselben Sackgasse
        var yards = u.Stations.Values.Where(s => s.TypeKey == "shipyard").ToList();
        Assert.InRange(yards.Count, 2, 3);
        Assert.Equal(yards.Count, yards.Select(y => y.Sector).Distinct().Count());
        Assert.DoesNotContain(yards, y => DeadEnds.Contains(y.Sector.Value));
        // Planeten mit Exportsenke: vier (Beta, Gamma + zwei neue); Export nur in Planetensektoren
        var sinks = u.Stations.Values.Where(s => ExportRules.ConfigOf(u, s) is not null).ToList();
        Assert.True(sinks.Count >= 4);
        Assert.All(sinks, s => Assert.NotNull(u.Sectors[s.Sector].Planet));
        Assert.True(u.Sectors.Values.Count(s => s.Planet is not null && sinks.Any(k => k.Sector == s.Id)) >= 4);
        // zweite Kolonie (Endverbraucher)
        Assert.Equal(2, u.Stations.Values.Count(s => s.TypeKey == "colony"));
        // mehrere Modulwerke
        Assert.True(u.Stations.Values.Count(s => s.TypeKey is "drive_works" or "systems_works" or "weapons_works") >= 5);
    }

    [Fact]
    public void Platzierung_NeueStationenMitAbstand_GuertelErreichenSollzahl()
    {
        var u = DemoUniverse.Create();
        foreach (var st in u.Stations.Values.Where(s => s.Id.Value > 40 && !u.StationTypes[s.TypeKey].Hideout))
        {
            var sector = u.Sectors[st.Sector];
            // Phase 30: Abstände nach Radius (mittlere Station wie bisher 101/50/50); Waffenstellungen stehen bewusst nah am Tor (≥ 25 + Radius)
            bool outpost = !st.Dockable;
            Assert.All(sector.Fields, f => Assert.True(Vec2.Distance(f.Position, st.Position) > st.Radius + 86f, $"{st.Name} zu nah am Gürtel"));
            Assert.All(sector.Gates, g => Assert.True(Vec2.Distance(u.Gates[g].Position, st.Position) > st.Radius + (outpost ? 25f : 35f), $"{st.Name} zu nah am Tor"));
            Assert.All(sector.Stations.Where(x => x != st.Id), x => Assert.True(Vec2.Distance(u.Stations[x].Position, st.Position) > st.Radius + u.Stations[x].Radius + (outpost ? 15f : 20f), $"{st.Name} zu nah an {u.Stations[x].Name}"));
        }
        foreach (var g in u.Gates.Values.Where(g => g.Id.Value > 6))
            Assert.All(u.Sectors[g.Sector].Fields, f => Assert.True(Vec2.Distance(f.Position, g.Position) > 86f, $"Tor {g.Id.Value} zu nah am Gürtel"));
        foreach (var sector in u.Sectors.Values)
            foreach (var f in sector.Fields)
                Assert.Equal(f.TargetAsteroids, SpaceSim.Core.Mining.Belt.AsteroidsOf(sector, f).Count());
    }

    // ───────── Karte ─────────

    [Fact]
    public void Karte_NurDieSackgassenZerteilen_HauptroutenSpannenDieKarte()
    {
        var u = DemoUniverse.Create();
        Assert.False(MapParts.Of(u).IsSplit);
        var links = u.Gates.Values.Where(g => g.Id.Value < g.Target.Value).OrderBy(g => g.Id.Value).ToList();
        Assert.Equal(14, links.Count);
        var splitting = links.Where(g => MapParts.SplitsWhenClosed(u, g.Id)).ToList();
        // genau die Tore zu den beiden Sackgassen zerteilen die Karte; abgetrennt wird jeweils nur die Sackgasse
        Assert.Equal(new[] { 17, 27 }, splitting.Select(g => g.Id.Value));
        foreach (var g in splitting)
        {
            var parts = MapParts.IfClosed(u, g.Id);
            Assert.Equal(2, parts.Parts.Count);
            var small = parts.Parts.OrderBy(p => p.Count).First();
            Assert.Single(small);
            Assert.Contains(small[0].Value, DeadEnds);
        }
        // reine Prüfung: nichts am Universum geändert
        Assert.All(u.Gates.Values, g => Assert.Equal(GateState.Open, g.State));
        // Hauptrouten bilden einen Spannbaum: über Hauptroutentore ist jeder Sektor von jedem erreichbar
        var seen = new HashSet<SectorId> { new(2) };
        var queue = new Queue<SectorId>(seen);
        while (queue.Count > 0)
        {
            var s = queue.Dequeue();
            foreach (var gid in u.Sectors[s].Gates)
            {
                var g = u.Gates[gid];
                if (!g.MainRoute) continue;
                var far = u.Gates[g.Target].Sector;
                if (seen.Add(far)) queue.Enqueue(far);
            }
        }
        Assert.Equal(u.Sectors.Count, seen.Count);
        // Ringe: jeder Sektor außer den Sackgassen hat mindestens zwei Tore
        foreach (var s in u.Sectors.Values.Where(s => !DeadEnds.Contains(s.Id.Value)))
            Assert.True(s.Gates.Count >= 2, s.Name);
        // keine zwei Sektoren auf demselben Kartenfeld
        Assert.Equal(u.Sectors.Count, u.Sectors.Values.Select(s => s.MapPosition).Distinct().Count());
    }

    [Fact]
    public void Karte_RingtorZu_RouteUmgehtEs_KeinZerfall()
    {
        var u = DemoUniverse.Create();
        var alpha = new SectorId(1); var gamma = new SectorId(3);
        int before = u.FindRoute(alpha, gamma)!.Count;
        Assert.Equal(2, before);   // Alpha → Beta → Gamma
        GateAccess.SetState(u, u.Gates[new GateId(3)], GateState.Closed);   // Beta–Gamma zu
        Assert.False(MapParts.Of(u).IsSplit);
        var route = u.FindRoute(alpha, gamma);
        Assert.NotNull(route);
        Assert.True(route!.Count > before);
        Assert.DoesNotContain(new GateId(3), route);
    }

    // ───────── Langlauf, Determinismus, Speichern ─────────

    [Fact]
    public void Standardwelt_20000Ticks_PreiseKapazitaetFraktionen_KeineGeister_KeineSperren()
    {
        var u = DemoUniverse.Create();
        var watch = new MarketWatch(u);
        int locks = 0;
        var exportedBy = new HashSet<StationId>();
        u.Events.Raised += e =>
        {
            if (e is SpaceSim.Core.Events.SectorAccessChanged) locks++;
            else if (e is SpaceSim.Core.Events.GoodsExported ge) exportedBy.Add(ge.Station);
        };
        var runner = new SimulationRunner();
        float maxTrade = 0f, maxMine = 0f;
        for (int t = 1; t <= 20_000; t++)
        {
            runner.Step(u);
            if (t >= 2000 && t % 20 == 0) watch.Sample(u);
            if (t % 200 == 0)
            {
                maxTrade = MathF.Max(maxTrade, ShipJobs.UsedUnits(u, ShipJobs.ByKey("trade")!));
                maxMine = MathF.Max(maxMine, ShipJobs.UsedUnits(u, ShipJobs.ByKey("mine")!));
            }
        }
        // Preise im Zielbereich (wie das Urteil des Balance-Tools: Mittel des letzten Viertels zwischen 0,3× und 3×)
        foreach (var g in u.Goods.Values.Where(g => u.Stations.Values.Any(st => st.Market.ContainsKey(g.Id))))
            if (watch.AverageRatio(g.Id, 15_000) is { } r) Assert.InRange(r, 0.3f, 3.0f);
        // Kapazität hält und bricht nicht ein
        Assert.True(maxTrade <= u.Rules.MaxTraders + u.Rules.FleetCapacityOvershoot + 0.001f, $"Handel max {maxTrade}");
        Assert.True(maxMine <= u.Rules.MaxMiners + u.Rules.FleetCapacityOvershoot + 1.0f, $"Bergbau max {maxMine}");
        Assert.True(ShipJobs.UsedUnits(u, ShipJobs.ByKey("trade")!) >= 0.8f * u.Rules.MaxTraders, "Händler brechen nicht ein");
        Assert.True(ShipJobs.UsedUnits(u, ShipJobs.ByKey("mine")!) >= 0.8f * u.Rules.MaxMiners, "Bergleute brechen nicht ein");
        // keine Geisteraufträge
        foreach (var s in u.Ships.Values)
        {
            Assert.False(s.Order is GoToOrder && s.Transit is null, $"Geister-GoTo bei Schiff {s.Id.Value}");
            if (s.Order is TradeOrder to) Assert.True((u.Stations.ContainsKey(to.Buy) || (to.Buy.Value == 0 && s.Phase == TradePhase.GoingToSell)) && u.Stations.ContainsKey(to.Sell), $"Handelsauftrag ins Leere bei Schiff {s.Id.Value}");
            if (s.Transit is null) Assert.Contains(s.Id, u.Tracker.ParkedIn(s.Sector));
        }
        // alle Fraktionen leben: Gebiet, Stationen, Kasse, Polizei
        foreach (var f in u.Factions.Values)
        {
            Assert.True(f.Territory.Count >= 2, $"{f.Name}: Gebiet {f.Territory.Count}");
            Assert.True(u.Actors[f.Id].Treasury.Credits > 0f, $"{f.Name}: Kasse leer");
            Assert.Contains(u.Ships.Values, s => s.Role == ShipRole.Police && s.Owner == (ActorId)f.Id);
        }
        // keine Sperren aus der Politik, Karte zusammenhängend, beide Piratengruppen da
        Assert.Equal(0, locks);
        Assert.False(MapParts.Of(u).IsSplit);
        Assert.Equal(2, u.Actors.Values.Count(a => a.Kind == ActorKind.Outlaw));
        // jede Exportsenke hat exportiert
        foreach (var sink in u.Stations.Values.Where(s => ExportRules.ConfigOf(u, s) is not null))
            Assert.Contains(sink.Id, exportedBy);
    }

    [Fact]
    public void Determinismus_UndSpeichernLaden_BitIdentisch()
    {
        var a = DemoUniverse.Create(); var b = DemoUniverse.Create();
        new SimulationRunner().Run(a, 3000); new SimulationRunner().Run(b, 3000);
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(b));
        var (loaded, _) = SaveGameJson.Read(SaveGameJson.Write(a, DateTimeOffset.UnixEpoch));
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(loaded));
        Assert.Equal(11, loaded.Sectors.Count);
        Assert.Equal(0, loaded.Sectors[new SectorId(9)].Owner.Value);
        new SimulationRunner().Run(a, 2000);
        new SimulationRunner().Run(loaded, 2000);
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(loaded));
    }
}
