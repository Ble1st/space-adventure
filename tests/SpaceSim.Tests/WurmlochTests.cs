using SpaceSim.Core.Combat;
using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Intel;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Wurmlöcher (docs/ERKUNDUNG.md „Wurmlöcher“): stabile Paare als Kanten im Torgraphen ohne Kontrollpunkt (Routen, Kartenteile, NPC-Reisen), instabile
/// aus einem eigenen RNG nur für den Spieler (Durchflug, Aufklärung, Ablauf), Speichern/Laden bitgleich, Schalter aus = Welt unverändert.
/// </summary>
public class WurmlochTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
    static readonly SectorId Borealis = new(5), Hephaistos = new(7), Nebelmark = new(9), Kalypso = new(10);

    /// <summary>Referenzhash-Tests: Schalter aus — keine stabilen Paare, keine instabilen.</summary>
    internal static void RevertWormholes(WorldDef def) => (def.Rules ??= new EconomyRules()).Wormholes = false;

    static Universe World(ulong seed = 1, bool on = true)
    {
        var def = TestWorlds.Def(fuel: false, research: false);
        def.Rules!.Wormholes = on;
        return WorldBuilder.Build(def, seed);
    }

    static Ship PlayerAt(Universe u, SectorId sector, Vec2 pos)
    {
        var me = u.Ships[u.PlayerShip];
        me.DockedAt = default;
        if (me.Sector != sector) u.MoveShip(me, sector);
        u.PlayerSector = sector;
        me.Position = pos;
        return me;
    }

    static List<SimEvent> Capture(Universe u) { var l = new List<SimEvent>(); u.Events.Raised += l.Add; return l; }

    [Fact]
    public void Schalter_CodeAus_StandardweltAn_RuecknahmeAus()
    {
        Assert.False(new EconomyRules().Wormholes);
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        Assert.True(def.Rules!.Wormholes);
        Assert.Equal(2, def.Wormholes.Count);
        SchutzKonvoiTests.RevertSchutz(def);
        Assert.False(def.Rules.Wormholes);
    }

    [Fact]
    public void SchalterAus_KeineWurmloecher_WeltWieOhneDaten()
    {
        var off = World(on: false);
        Assert.DoesNotContain(off.Gates.Values, g => g.Wormhole);
        Assert.Equal(28, off.Gates.Count);
        new SimulationRunner().Run(off, 6000);
        Assert.Empty(off.UnstableWormholes);
        Assert.Equal(1, off.NextWormholeId);
        // aus = Welt, deren Datei keine Wurmlöcher hat (Hash nach 1500 Takten gleich)
        var def = TestWorlds.Def(fuel: false, research: false);
        def.Wormholes.Clear();
        def.Rules!.Wormholes = false;
        var a = World(on: false); var b = WorldBuilder.Build(def, 1);
        new SimulationRunner().Run(a, 1500); new SimulationRunner().Run(b, 1500);
        Assert.Equal(UniverseHash.Compute(b), UniverseHash.Compute(a));
    }

    [Fact]
    public void Stabil_KantenImTorgraph_KurzeRoute_KeineNachbarn()
    {
        var u = World();
        var wh = u.Gates.Values.Where(g => g.Wormhole).OrderBy(g => g.Id.Value).ToList();
        Assert.Equal(4, wh.Count);
        Assert.Equal(new[] { 29, 30, 31, 32 }, wh.Select(g => g.Id.Value));
        foreach (var g in wh)
        {
            Assert.Contains(g.Id, u.Sectors[g.Sector].Gates);
            Assert.True(u.Gates[g.Target].Wormhole);
            Assert.Equal("", g.TypeKey);
            Assert.False(g.MainRoute);
            Assert.Null(GateCombat.TypeOf(u, g));   // nicht angreifbar
            Assert.StartsWith("Wurmloch ", GateCombat.NameOf(u, g));
        }
        // verbindet Sektoren, die über Tore nicht benachbart sind; die Route nimmt das Wurmloch (1 Sprung)
        Assert.Equal(1, u.Hops(Borealis, Kalypso));
        Assert.Equal(1, u.Hops(Nebelmark, Hephaistos));
        var off = World(on: false);
        Assert.True(off.Hops(Borealis, Kalypso) > 1);
        Assert.True(off.Hops(Nebelmark, Hephaistos) > 1);
        // Kartenteile: physische Kanten (die Karte bleibt ganz)
        Assert.Single(MapParts.Of(u).Parts);
    }

    [Fact]
    public void Stabil_KeinKontrollpunkt_KeinZoll_AuchBeiAbriegelung()
    {
        var u = World();
        var me = u.Ships[u.PlayerShip];
        var k = u.Sectors[Kalypso];
        k.Access.EntryLevel = 5; k.Access.ExitLevel = 5; k.Access.Enforced = true;
        u.TouchAccess();
        GateAccess.SyncRestricted(u);
        var profile = GateAccess.ProfileOf(u, me);
        var whIn = u.Gates.Values.Single(g => g.Wormhole && g.Sector == Borealis);
        Assert.Equal(GateState.Open, whIn.State);   // Wurmlöcher bleiben offen
        var pass = GateAccess.Evaluate(u, whIn, profile, me);
        Assert.True(pass.Allowed);
        Assert.Equal(0f, pass.Toll);
        // ein normales Tor nach Kalypso weist ab
        var gate = u.Gates.Values.First(g => !g.Wormhole && !g.MainRoute && u.Gates[g.Target].Sector == Kalypso);
        Assert.False(GateAccess.Evaluate(u, gate, profile, me).Allowed);
        // Spielersprung: erlaubt, keine Meldung, kein Zoll
        float wallet = u.WalletOf(me).Credits;
        var jump = GateAccess.TryPlayerJump(u, me, whIn);
        Assert.True(jump.Allowed);
        Assert.Equal(0f, jump.Toll);
        Assert.Equal(wallet, u.WalletOf(me).Credits);
    }

    [Fact]
    public void Stabil_NpcReisenNutzenWurmloecher()
    {
        var u = World(3);
        var runner = new SimulationRunner();
        int via = 0;
        for (int i = 0; i < 300 && via == 0; i++)
        {
            runner.Run(u, 20);
            foreach (var s in u.Ships.Values)
            {
                if (s.Transit is not { } t || s.IsPlayer) continue;
                for (int k = 0; k + 1 < t.Legs.Count; k++)
                {
                    var a = t.Legs[k]; var b = t.Legs[k + 1];
                    if (a.Sector == b.Sector) continue;
                    if (u.Sectors[a.Sector].Gates.Select(g => u.Gates[g]).Any(g => g.Wormhole && u.Gates[g.Target].Sector == b.Sector && Vec2.Distance(g.Position, a.To) < 1f)) via++;
                }
            }
        }
        Assert.True(via > 0, "keine NPC-Reise durch ein Wurmloch in 6000 Takten");
    }

    [Fact]
    public void Instabil_Deterministisch_OhneUniverseRng_Ablauf()
    {
        var a = World(7); var b = World(7);
        var ea = Capture(a);
        new SimulationRunner().Run(a, 20000); new SimulationRunner().Run(b, 20000);
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(b));
        var opened = ea.OfType<WormholeOpened>().ToList();
        Assert.NotEmpty(opened);
        Assert.NotEmpty(ea.OfType<WormholeCollapsed>());
        Assert.All(opened, o => Assert.NotEqual(o.Sector, o.Target));
        Assert.True(a.UnstableWormholes.Count <= a.Rules.WormholeMaxUnstable);
        Assert.All(a.UnstableWormholes, w => Assert.True(w.EndTick > a.Tick));
        // ein erzwungenes zieht nichts aus Universe.Rng; NPC-Routen kennen es nicht
        var u = World(3);
        ulong before = u.Rng.State;
        int hops = u.Hops(new SectorId(1), new SectorId(11));
        var w = Wormholes.Force(u, new SectorId(1), new SectorId(11));
        Assert.NotNull(w);
        Assert.Equal(before, u.Rng.State);
        Assert.Equal(hops, u.Hops(new SectorId(1), new SectorId(11)));
        Assert.Null(Wormholes.Force(u, new SectorId(1), new SectorId(1)));
    }

    [Fact]
    public void Instabil_Durchflug_NurSpieler_AufklaerungDesZiels()
    {
        var u = World(4);
        u.Player.Intel = null;
        Knowledge.Init(u, new SectorId(1));
        Assert.False(Knowledge.Visited(u, new SectorId(11)));
        var me = PlayerAt(u, new SectorId(1), new Vec2(30, 40));
        var w = Wormholes.Force(u, new SectorId(1), new SectorId(11), new Vec2(200, 200))!;
        Assert.Equal("?", Wormholes.TargetText(u, w));
        var ev = Capture(u);
        // zu weit weg: nichts ändert sich
        Assert.NotNull(Wormholes.Traverse(u, me, w.Id));
        Assert.Equal(new SectorId(1), me.Sector);
        // ein NPC-Schiff darf nicht
        var npc = u.Ships.Values.First(s => !s.IsPlayer && s.Transit is null);
        Assert.NotNull(Wormholes.TraverseIssue(u, npc, w));
        // angedockt nicht
        me.Position = w.Position;
        me.DockedAt = u.Stations.Keys.First();
        Assert.Equal("angedockt", Wormholes.TraverseIssue(u, me, w));
        me.DockedAt = default;
        Assert.Null(Wormholes.Traverse(u, me, w.Id));
        Assert.Equal(new SectorId(11), me.Sector);
        Assert.Equal(new SectorId(11), u.PlayerSector);
        Assert.Equal(w.TargetPosition, me.Position);
        Assert.True(w.Traversed);
        Assert.True(Knowledge.Visited(u, new SectorId(11)));
        Assert.NotEqual("?", Wormholes.TargetText(u, w));
        Assert.Single(ev.OfType<WormholeTraversed>());
    }

    [Fact]
    public void Scanliste_ZeigtWurmloecher()
    {
        var u = World();
        var me = PlayerAt(u, Borealis, new Vec2(0, 0));
        var w = Wormholes.Force(u, Borealis, new SectorId(1))!;
        var scan = SectorScan.For(u, me.Id, me.Position);
        Assert.Contains(scan, c => c.Kind == ScanKind.Gate && c.Name.StartsWith("Wurmloch → "));
        Assert.Contains(scan, c => c.Kind == ScanKind.Wormhole && c.Id == w.Id);
    }

    [Fact]
    public void SpeichernLaden_MitInstabilen_Bitgleich()
    {
        var u = World(5);
        var runner = new SimulationRunner();
        runner.Run(u, 100);
        var me = PlayerAt(u, new SectorId(2), new Vec2(10, 10));
        var w1 = Wormholes.Force(u, new SectorId(2), new SectorId(8), me.Position)!;
        Wormholes.Force(u, new SectorId(3), new SectorId(4));
        Assert.Null(Wormholes.Traverse(u, me, w1.Id));
        var (loaded, _) = SaveGameJson.Read(SaveGameJson.Write(u, Now));
        Assert.Equal(u.UnstableWormholes.Count, loaded.UnstableWormholes.Count);
        Assert.True(loaded.UnstableWormholes.Single(x => x.Id == w1.Id).Traversed);
        Assert.Equal(4, loaded.Gates.Values.Count(g => g.Wormhole));
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
        // Weiterlaufen bitgleich (Vergleich zweier geladener Stände: das Versetzen des Spielers per Hand oben ist kein Simulationsweg und hinterlässt
        // abgeleitete Zustände, die erst das Laden neu aufbaut — das gilt auch ohne Wurmlöcher)
        var (again, _) = SaveGameJson.Read(SaveGameJson.Write(loaded, Now));
        new SimulationRunner().Run(loaded, 3000);
        new SimulationRunner().Run(again, 3000);
        Assert.Equal(UniverseHash.Compute(loaded), UniverseHash.Compute(again));
    }
}
