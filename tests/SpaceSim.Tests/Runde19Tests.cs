using SpaceSim.Core.Content;
using SpaceSim.Core.Events;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Mining;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using Xunit;
using Xunit.Abstractions;

namespace SpaceSim.Tests;

/// <summary>Runde 19: Startpaket „Ankertest“, Messung der Verankerung im Schürfer-Start, Tippen auf Tore.</summary>
public class Runde19Tests
{
    readonly ITestOutputHelper _o;
    public Runde19Tests(ITestOutputHelper o) => _o = o;

    static List<SimEvent> Capture(Universe u) { var l = new List<SimEvent>(); u.Events.Raised += l.Add; return l; }

    [Fact]
    public void Ankertest_StartAmFels_VerankertSofort_ZweiMinutenAbbau()
    {
        var u = WorldBuilder.BuildDefault(startOption: "anchor");
        var me = u.Ships[u.PlayerShip];
        Assert.Equal("prospector", me.ModelKey);
        Assert.Equal(500f, me.CargoCapacity, 1);
        Assert.False(u.Rules.PlayerRaids);
        Assert.DoesNotContain(u.PendingEncounters, p => p.Target == me.Id);
        var sector = u.Sectors[u.PlayerSector];
        var chunks = new OreChunks(seed: 1);
        var rock = chunks.Nearest(sector, me.Position, ContactMining.ContactRange + ContactMining.BowOffset + 1f);
        Assert.NotNull(rock);
        Assert.True(rock!.Ore >= 2000f);
        var dir = (rock.Position - me.Position).Normalized();
        Assert.True(ContactMining.InContact(me.Position, dir, rock));
        var ev = Capture(u);
        var run = new SimulationRunner();
        new SimulationRunner().Step(u);
        var pos = me.Position;
        double acc = 0; const float dt = 0.05f;
        for (float t = 0; t < 130f; t += dt)
        {
            acc += dt;
            while (acc >= 0.25) { run.Step(u); acc -= 0.25; }
            if (me.AnchorRock != 0) { pos = MiningAnchor.HoldPosition(pos, rock, Collision.ShipRadius); dir = (rock.Position - pos).Normalized(); }
            chunks.Contact(u, me, sector, pos, dir, dt);
            if (t > 2.5f) Assert.Equal(AnchorState.Held, MiningAnchor.State(u, me));
        }
        Assert.DoesNotContain(ev.OfType<AnchorReleased>(), r => r.Ship == me.Id);
        Assert.True(me.UsedVolume(u) > 300f, $"{me.UsedVolume(u):0}");
        Assert.True(me.UsedVolume(u) < me.CargoCapacity, "Laderaum reicht für über zwei Minuten");
        Assert.True(rock.Ore > 500f);
    }

    /// <summary>
    /// Runde 19 (Handy-Test: „Verankerung wirkt im Schürfer-Start sehr kurz“): Messung mit der Automatik — Anteil verankerter Abbauzeit, Abrisse je Grund und
    /// Ladung. Ergebnis (im Testprotokoll): die Ladung ist nach ≈ 20 s voll (60er Laderaum, 3,4 Erz/s), kein Flackern Kontakt verloren ↔ Ansetzen.
    /// </summary>
    [Fact]
    public void SchuerferStart_Verankerung_KeinFlackern()
    {
        var u = WorldBuilder.BuildDefault(startOption: "miner");
        new SimulationRunner().Step(u);
        var me = u.Ships[u.PlayerShip];
        var ev = Capture(u);
        var chunks = new OreChunks(seed: 5);
        var auto = new MiningAutopilot();
        var sector = u.Sectors[u.PlayerSector];
        var run = new SimulationRunner();
        var pos = me.Position; var dir = new Vec2(0, -1);
        if (me.DockedAt.Value != 0) { pos = pos + new Vec2(0f, 40f); me.DockedAt = default; }
        auto.Start();
        double acc = 0; const float dt = 0.05f, speed = 30f;
        float mineTime = 0f, heldTime = 0f; int loads = 0; bool wasSell = false;
        for (float t = 0; t < 600f; t += dt)
        {
            acc += dt;
            while (acc >= 0.25) { run.Step(u); acc -= 0.25; }
            var step = auto.Step(u, chunks, me, pos);
            if (step.Nav is not null)
            {
                // Verkauf wie die Ansicht: andocken und verkaufen, dann zurück an die Arbeit (Position bleibt; der Fels wird neu gesucht)
                if (step.DockAt is { } sid) auto.OnDocked(u, me, u.Stations[sid]);
                if (!wasSell) loads++;
                wasSell = true;
                continue;
            }
            wasSell = false;
            if (auto.Current == MiningAutopilot.Phase.Mine) mineTime += dt;
            if (MiningAnchor.State(u, me) == AnchorState.Held) heldTime += dt;
            if (me.AnchorRock != 0 && sector.Asteroids.FirstOrDefault(a => a.Id == me.AnchorRock) is { } held)
            {
                pos = MiningAnchor.HoldPosition(pos, held, Collision.ShipRadius);
                dir = (held.Position - pos).Normalized();
            }
            else if (step.Waypoint is { } wp)
            {
                if (Vec2.Distance(wp, pos) > 1e-3f) dir = (wp - pos).Normalized();
                pos += (wp - pos).Normalized() * MathF.Min(speed * dt * step.Throttle, Vec2.Distance(wp, pos));
                if (step.Aim is { } touch && Vec2.Distance(pos, touch.Position) < touch.Radius + Collision.ShipRadius)
                    pos = touch.Position + (pos - touch.Position).Normalized() * (touch.Radius + Collision.ShipRadius);
            }
            chunks.Contact(u, me, sector, pos, dir, dt);
        }
        var rel = ev.OfType<AnchorReleased>().Where(r => r.Ship == me.Id).GroupBy(r => r.Reason).ToDictionary(g => g.Key, g => g.Count());
        int fired = ev.OfType<AnchorFired>().Count(a => a.Ship == me.Id);
        _o.WriteLine($"Schürfer-Start 600 s: Ladungen {loads}, Abbauphase {mineTime:0} s, davon verankert {heldTime:0} s ({(mineTime > 0 ? 100f * heldTime / mineTime : 0):0} %), "
            + $"Anker geschossen {fired}, gelöst {string.Join(", ", rel.Select(kv => kv.Key + " " + kv.Value))}");
        Assert.True(loads >= 3);
        // kein Flackern: höchstens ein Kontaktverlust je Ladung, und fast jede Verankerung endet mit voller Ladung, erschöpftem Fels oder Lösen vor dem Verkauf
        Assert.True(rel.GetValueOrDefault("lost_contact") <= loads, $"Kontaktverluste {rel.GetValueOrDefault("lost_contact")} bei {loads} Ladungen");
        Assert.True(fired <= 3 * (loads + 1), $"Anker {fired} bei {loads} Ladungen");
    }

    [Fact]
    public void Tippen_TorGewinnt_AusserSchiffDeutlichNaeher()
    {
        Assert.False(TapPick.ShipBeatsGate(20f, 30f));   // etwas näher am Schiff: das Tor bleibt gewählt
        Assert.True(TapPick.ShipBeatsGate(10f, 30f));    // deutlich näher am Schiff
        Assert.True(TapPick.ShipBeatsStation(20f, 30f)); // Stationen: wie seit Runde 4/5
        Assert.True(TapPick.InBody(25f, 20f, 28f));
        Assert.False(TapPick.InBody(40f, 20f, 28f));
    }
}
