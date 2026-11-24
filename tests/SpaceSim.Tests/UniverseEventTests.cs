using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Intel;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Ui;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Zufällige Universumsereignisse (docs/ERKUNDUNG.md „Universumsereignisse“): Erzschwemme (reiche Gürtelfelsen auf Zeit), Frachtfund (treibende Behälter),
/// Wrackfeld (Wracks mit Modulen). Eigener RNG aus Welt-Seed und Takt (Universe.Rng unberührt), Ablauf nach Frist oder wenn alles genutzt ist, Anzeige nur
/// in bekannten Sektoren, Speichern/Laden bitgleich, Schalter aus = Welt unverändert.
/// </summary>
public class UniverseEventTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
    static readonly SectorId Alpha = new(1);

    static Universe World(ulong seed = 1, bool on = true)
    {
        var def = TestWorlds.Def(fuel: false, research: false);
        def.Rules!.UniverseEvents = on;
        return WorldBuilder.Build(def, seed);
    }

    static List<SimEvent> Capture(Universe u) { var l = new List<SimEvent>(); u.Events.Raised += l.Add; return l; }

    [Fact]
    public void Schalter_CodeAus_StandardweltAn_RuecknahmeAus()
    {
        Assert.False(new EconomyRules().UniverseEvents);
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        Assert.True(def.Rules!.UniverseEvents);
        SchutzKonvoiTests.RevertSchutz(def);
        Assert.False(def.Rules.UniverseEvents);
        Assert.Equal(3, UniverseEventTypes.Default.Count);
    }

    [Fact]
    public void SchalterAus_KeineEreignisse_HashWieOhneSystem()
    {
        var u = World(on: false);
        new SimulationRunner().Run(u, 8000);
        Assert.Empty(u.UniverseEvents);
        Assert.Equal(1, u.NextUniverseEventId);
        // ohne Ereignis mischt der Hash nichts: Welt mit Schalter an, aber ohne Ereignis bis Tick 1199, gleicht der Welt ohne Schalter
        var a = World(on: true); var b = World(on: false);
        new SimulationRunner().Run(a, 1100); new SimulationRunner().Run(b, 1100);
        Assert.Equal(UniverseHash.Compute(b), UniverseHash.Compute(a));
    }

    [Fact]
    public void Ereignisse_EntstehenDeterministisch_OhneUniverseRng()
    {
        var a = World(7); var b = World(7);
        var ea = Capture(a);
        var ra = new SimulationRunner(); var rb = new SimulationRunner();
        ra.Run(a, 12000); rb.Run(b, 12000);
        var started = ea.OfType<UniverseEventStarted>().ToList();
        Assert.NotEmpty(started);
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(b));
        Assert.True(started.Count <= 12000 / a.Rules.UniverseEventCheckTicks);
        Assert.All(a.UniverseEvents, e => Assert.True(e.EndTick > a.Tick));
        // ein erzwungenes Ereignis zieht nichts aus Universe.Rng
        var u = World(3);
        ulong before = u.Rng.State;
        Assert.NotNull(UniverseEvents.Force(u, CargoFindEvent.KeyValue, Alpha));
        Assert.NotNull(UniverseEvents.Force(u, WreckFieldEvent.KeyValue, new SectorId(2)));
        Assert.Equal(before, u.Rng.State);
    }

    [Fact]
    public void Erzschwemme_ReicheFelsen_UnberuehrteVerschwindenAmEnde()
    {
        var u = World();
        var s = u.Sectors[Alpha];
        int rocks = s.Asteroids.Count;
        var ev = Capture(u);
        var e = UniverseEvents.Force(u, OreSurgeEvent.KeyValue, Alpha)!;
        Assert.NotNull(e);
        Assert.InRange(e.Asteroids.Count, 1, u.Rules.OreSurgeMaxRocks);
        Assert.Equal(rocks + e.Asteroids.Count, s.Asteroids.Count);
        var mine = s.Asteroids.Where(a => e.Asteroids.Contains(a.Id)).ToList();
        Assert.All(mine, a => Assert.True(a.Ore >= Asteroid.OrePerArea * a.BaseRadius * a.BaseRadius * 0.6f * u.Rules.OreSurgeRichFactor * 0.5f));
        Assert.Single(ev.OfType<UniverseEventStarted>(), x => x.Event == e.Id && x.Kind == OreSurgeEvent.KeyValue);
        // einen Felsen anbrechen: er bleibt; die übrigen verschwinden mit dem Ende
        mine[0].Ore -= 1f;
        u.Tick = e.EndTick;
        UniverseEvents.Expire(u);
        Assert.Empty(u.UniverseEvents);
        Assert.Contains(s.Asteroids, a => a.Id == mine[0].Id);
        Assert.All(mine.Skip(1), m => Assert.DoesNotContain(s.Asteroids, a => a.Id == m.Id));
        Assert.Single(ev.OfType<UniverseEventEnded>(), x => x.Event == e.Id && x.Reason == "expired");
    }

    [Fact]
    public void Frachtfund_TraktorHoltBehaelter_EreignisEndetGenutzt()
    {
        var u = World();
        var me = u.Ships[u.PlayerShip];
        if (me.Transit is not null) u.FinishTransit(me);
        me.DockedAt = default;
        var e = UniverseEvents.Force(u, CargoFindEvent.KeyValue, me.Sector, me.Position + new Vec2(5f, 0f))!;
        Assert.NotNull(e);
        var boxes = e.Wrecks.Select(id => Wrecks.Get(u, id)!).ToList();
        Assert.All(boxes, w => { Assert.Equal(WreckKind.Container, w.Kind); Assert.Equal(e.EndTick, w.ExpiresTick); Assert.True(w.CargoUnits >= 1f); });
        var ev = Capture(u);
        me.CargoCapacity = 100000f;
        foreach (var w in boxes)
        {
            me.Position = w.Position;
            Assert.True(Wrecks.TakeCargo(u, me, w, float.MaxValue, out _) > 0f);
        }
        UniverseEvents.Expire(u);
        Assert.Empty(u.UniverseEvents);
        Assert.Single(ev.OfType<UniverseEventEnded>(), x => x.Reason == "exhausted");
    }

    [Fact]
    public void Frachtfund_AmEnde_BehaelterVerschwinden()
    {
        var u = World();
        var e = UniverseEvents.Force(u, CargoFindEvent.KeyValue, Alpha)!;
        u.Tick = e.EndTick;
        UniverseEvents.Expire(u);
        Assert.All(e.Wrecks, id => Assert.Null(Wrecks.Get(u, id)));
    }

    [Fact]
    public void Wrackfeld_KiBergeschiffHoltModule()
    {
        var u = World();
        var runner = new SimulationRunner();
        runner.Run(u, 50);
        var e = UniverseEvents.Force(u, WreckFieldEvent.KeyValue, Alpha, new Vec2(60f, 0f))!;
        Assert.NotNull(e);
        var wrecks = e.Wrecks.Select(id => Wrecks.Get(u, id)!).ToList();
        Assert.All(wrecks, w => { Assert.Equal(WreckKind.Wreck, w.Kind); Assert.InRange(w.Modules.Count, 1, 3); });
        // Bergeschiff direkt daneben (wie BergungTests): es birgt Module aus dem Wrackfeld
        var m = u.ShipModels["salvager"];
        var yard = u.StationsById().First(st => !st.IsBuildSite);
        var sv = ShipPurchase.Create(u, u.NewShipId(), m.Key, u.ShipClasses[m.ClassKey].Slots, m.Fit, ShipRole.Salvager, Controller.Ai, u.FreeActor, yard, 2000f);
        u.AddShip(sv);
        if (sv.Sector != Alpha) u.MoveShip(sv, Alpha);
        sv.Position = wrecks[0].Position + new Vec2(4f, 0f); sv.DockedAt = default;
        foreach (var w in wrecks) foreach (var mod in w.Modules) mod.Condition = 0.9f;   // bergbar
        var ev = Capture(u);
        runner.Run(u, 1500);
        Assert.Contains(ev.OfType<WreckSalvaged>(), x => x.ByNpc && x.Module is not null && e.Wrecks.Contains(x.Wreck));
    }

    [Fact]
    public void Anzeige_NurInBekanntenSektoren()
    {
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        def.RandomNames = false;
        def.Rules!.PlayerIntel = true;
        var u = WorldBuilder.Build(def, null, "trader");
        Assert.True(Knowledge.Limited(u));
        var here = UniverseEvents.Force(u, CargoFindEvent.KeyValue, u.PlayerSector)!;
        var unknown = u.Sectors.Values.OrderBy(s => s.Id.Value).First(s => !Knowledge.KnowsSector(u, s.Id) && s.Id != u.PlayerSector);
        var far = UniverseEvents.Force(u, CargoFindEvent.KeyValue, unknown.Id)!;
        Assert.NotNull(far);
        var marks = UniverseEventInfo.Markers(u);
        Assert.Contains(marks, mk => mk.Event == here.Id);
        Assert.DoesNotContain(marks, mk => mk.Event == far.Id);
        Assert.Equal(MessageWeight.Important, UniverseEventInfo.Weight(u, u.PlayerSector));
        Assert.Equal(MessageWeight.Off, UniverseEventInfo.Weight(u, unknown.Id));
        Assert.Null(UniverseEventInfo.StartText(u, new UniverseEventStarted(far.Id, far.Kind, far.Sector, far.Position, "x")));
        Assert.Contains("Frachtfund", UniverseEventInfo.Line(u, u.PlayerSector));
        Assert.Equal("", UniverseEventInfo.Line(u, unknown.Id));
        // bekannter, nicht eigener Sektor: Info
        var seen = u.Sectors.Values.OrderBy(s => s.Id.Value).First(s => Knowledge.KnowsSector(u, s.Id) && s.Id != u.PlayerSector);
        Assert.Equal(MessageWeight.Info, UniverseEventInfo.Weight(u, seen.Id));
    }

    [Fact]
    public void SpeichernLaden_MitLaufendenEreignissen_Bitgleich()
    {
        var u = World(5);
        var runner = new SimulationRunner();
        runner.Run(u, 100);
        UniverseEvents.Force(u, OreSurgeEvent.KeyValue, Alpha);
        UniverseEvents.Force(u, WreckFieldEvent.KeyValue, new SectorId(2));
        UniverseEvents.Force(u, CargoFindEvent.KeyValue, new SectorId(3));
        var (loaded, _) = SaveGameJson.Read(SaveGameJson.Write(u, Now));
        Assert.Equal(u.UniverseEvents.Count, loaded.UniverseEvents.Count);
        Assert.Equal(u.UniverseEvents.Select(e => e.Detail), loaded.UniverseEvents.Select(e => e.Detail));
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
        runner.Run(u, 1500);
        new SimulationRunner().Run(loaded, 1500);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
        // ein nach dem Laden begonnenes Ereignis liegt am selben Ort (Regression: Sector.Extent ist abgeleitet und nach dem Laden anders)
        var (fresh, _) = SaveGameJson.Read(SaveGameJson.Write(u, Now));
        var x = UniverseEvents.Force(u, CargoFindEvent.KeyValue, new SectorId(7))!;
        var y = UniverseEvents.Force(fresh, CargoFindEvent.KeyValue, new SectorId(7))!;
        Assert.Equal(x.Position, y.Position);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(fresh));
    }
}
