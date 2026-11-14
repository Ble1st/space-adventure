using SpaceSim.Core.Combat;
using SpaceSim.Core.Content;
using SpaceSim.Core.Events;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Mining;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>Runde 20: Kontexttaste nach Zielwahl, Begleiter nicht angedockt, Anker übernehmen/nicht selbst neu setzen, Fehlerpfade „atRock“.</summary>
public class Runde20Tests
{
    static List<SimEvent> Capture(Universe u) { var l = new List<SimEvent>(); u.Events.Raised += l.Add; return l; }

    [Fact]
    public void Kontexttaste_GewaehltesZielGewinnt()
    {
        StationId st = new(5); GateId g = new(7);
        Assert.Equal((ContextKind.Jump, 7), ContextChoice.Pick(st, g, null, g));        // Tor gewählt, Station näher → Springen
        Assert.Equal((ContextKind.Dock, 5), ContextChoice.Pick(st, g, st, null));       // Station gewählt → Andocken
        Assert.Equal((ContextKind.Jump, 7), ContextChoice.Pick(st, g, null, new GateId(9)));   // anderes Tor gewählt → das nahe Tor
        Assert.Equal((ContextKind.Dock, 5), ContextChoice.Pick(st, g, null, null));     // ohne Wahl wie bisher: Station vor Tor
        Assert.Equal((ContextKind.Jump, 7), ContextChoice.Pick(null, g, st, null));
        Assert.Equal((ContextKind.None, 0), ContextChoice.Pick(null, null, st, g));
    }

    /// <summary>Handy-Test: eigene Verbandsschiffe zeigten „Ziel angedockt“ — Begleiter mit Folgebefehl bekamen bei Ankunft neben einer Station deren Andockplatz.</summary>
    [Fact]
    public void Begleiter_NebenStation_NichtAngedockt_GrundEigenesSchiff()
    {
        var u = TestWorlds.Default(null, fuel: false, research: false, startOption: "war");
        var r = new SimulationRunner();
        var me = u.Ships[u.PlayerShip];
        var hq = u.Stations.Values.First(x => x.Name == "Sektor-HQ Beta");
        var wing = u.Ships.Values.Where(s => s.IsPlayer && s.Id != me.Id).ToList();
        Assert.NotEmpty(wing);
        for (int i = 0; i < 400; i++)
        {
            // Spieler fliegt dicht an der Station entlang (nicht angedockt)
            me.Position = hq.DockPoint(me.Id) + new Vec2(MathF.Sin(i * 0.01f) * 6f, 2f);
            me.DockedAt = default;
            r.Step(u);
        }
        foreach (var w in wing)
        {
            Assert.IsType<FollowCommand>(w.Command);
            Assert.Equal(default, w.DockedAt);
            Assert.Equal("eigenes Schiff", PlayerCombat.TargetIssueShort(u, me, w));
        }
    }

    static (Universe u, Ship me, Sector sector, Asteroid rock, Vec2 pos, Vec2 dir, OreChunks chunks) AnchorStart()
    {
        var u = WorldBuilder.BuildDefault(startOption: "anchor");
        new SimulationRunner().Step(u);
        var me = u.Ships[u.PlayerShip];
        var sector = u.Sectors[u.PlayerSector];
        var chunks = new OreChunks(seed: 3);
        var rock = chunks.Nearest(sector, me.Position, ContactMining.ContactRange + ContactMining.BowOffset + 1f)!;
        var pos = me.Position;
        var dir = (rock.Position - pos).Normalized();
        for (int i = 0; i < 60; i++) chunks.Contact(u, me, sector, pos, dir, 0.05f);
        Assert.Equal(AnchorState.Held, MiningAnchor.State(u, me));
        pos = MiningAnchor.HoldPosition(pos, rock, Collision.ShipRadius); dir = (rock.Position - pos).Normalized();
        return (u, me, sector, rock, pos, dir, chunks);
    }

    /// <summary>Handy-Test: Auto-Mining an während der Verankerung zeigte kurz „Anker fliegt“ — der bestehende Anker wird übernommen.</summary>
    [Fact]
    public void AutoMining_UebernimmtBestehendenAnker()
    {
        var (u, me, sector, rock, pos, dir, chunks) = AnchorStart();
        var ev = Capture(u);
        var auto = new MiningAutopilot();
        auto.Start();
        for (int i = 0; i < 40; i++)
        {
            auto.Step(u, chunks, me, pos);
            Assert.Equal(rock.Id, me.AnchorRock);
            Assert.NotEqual(ContactState.Anchoring, chunks.Contact(u, me, sector, pos, dir, 0.05f));
        }
        Assert.Equal(MiningAutopilot.Phase.Mine, auto.Current);
        Assert.DoesNotContain(ev, e => e is AnchorReleased or AnchorFired);
    }

    /// <summary>Handy-Test: nach „Lösen“ mit Bug am Fels verankerte das Schiff nach 0,4 s neu — jetzt erst nach „Ankern“ oder nach Verlassen des Kontakts.</summary>
    [Fact]
    public void NachLoesen_KeinSelbsttaetigerNeuanker()
    {
        var (u, me, sector, rock, pos, dir, chunks) = AnchorStart();
        MiningAnchor.Release(u, me, sector.Id, "released");
        chunks.HoldOff(rock.Id);
        for (int i = 0; i < 100; i++) Assert.Equal(ContactState.Released, chunks.Contact(u, me, sector, pos, dir, 0.05f));
        Assert.Equal(0, me.AnchorRock);
        Assert.Equal("gelöst", SpaceSim.Core.Weapons.WeaponBays.ContactText(ContactState.Released, anchors: true));
        // Taste „Ankern“
        chunks.Rearm();
        Assert.Equal(ContactState.Anchoring, chunks.Contact(u, me, sector, pos, dir, 0.05f));
        for (int i = 0; i < 60; i++) chunks.Contact(u, me, sector, pos, dir, 0.05f);
        Assert.Equal(AnchorState.Held, MiningAnchor.State(u, me));
        // erneut lösen, dann den Kontakt verlassen → die Sperre fällt, beim nächsten Kontakt wird wieder verankert
        MiningAnchor.Release(u, me, sector.Id, "released");
        chunks.HoldOff(rock.Id);
        Assert.Equal(ContactState.Released, chunks.Contact(u, me, sector, pos, dir, 0.05f));
        Assert.Equal(ContactState.NoContact, chunks.Contact(u, me, sector, pos - dir * 20f, dir, 0.05f));
        Assert.Equal(0, chunks.HeldOff);
        Assert.Equal(ContactState.Anchoring, chunks.Contact(u, me, sector, pos, dir, 0.05f));
    }

    [Theory]
    [InlineData("lorem_rock")]   // unbekannte Klasse
    [InlineData("rare_rock")]    // kein Fels der Klasse im Startsektor (Alpha)
    public void Startpaket_AtRock_Fehlerpfade(string cls)
    {
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        var o = def.Player.StartOptions.First(x => x.Key == "anchor");
        o.Scenario!.AtRock = cls;
        var ex = Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(def, null, "anchor"));
        Assert.Contains("anchor", ex.Message);
    }
}
