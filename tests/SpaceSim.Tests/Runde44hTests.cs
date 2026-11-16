using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Mining;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using Xunit;
using Xunit.Abstractions;

namespace SpaceSim.Tests;

/// <summary>Runde 44h: Startpaket „Bergbautest“ — kleiner Fels mit 25 Erz zerbricht schnell, zweiter Fels daneben, KI-Bohrschiffe fliegen selbst an.</summary>
public class Runde44hTests
{
    readonly ITestOutputHelper _o;
    public Runde44hTests(ITestOutputHelper o) => _o = o;

    static List<SimEvent> Capture(Universe u) { var l = new List<SimEvent>(); u.Events.Raised += l.Add; return l; }

    [Fact]
    public void Bergbautest_Aufbau()
    {
        var u = WorldBuilder.BuildDefault(startOption: "mining");
        var me = u.Ships[u.PlayerShip];
        Assert.Equal("prospector", me.ModelKey);
        Assert.Equal(200f, me.CargoCapacity, 1);
        Assert.False(u.Rules.PlayerRaids);
        var sector = u.Sectors[u.PlayerSector];
        var rock = new OreChunks(seed: 1).Nearest(sector, me.Position, ContactMining.ContactRange + ContactMining.BowOffset + 1f);
        Assert.NotNull(rock);
        Assert.Equal(25f, rock!.Ore, 2);
        Assert.True(rock.Radius <= 3f);
        Assert.True(ContactMining.InContact(me.Position, (rock.Position - me.Position).Normalized(), rock));
        // zweiter kleiner Fels gleich daneben
        var second = sector.Asteroids.Where(a => a != rock && a.ClassKey == rock.ClassKey && a.Ore == 25f).ToList();
        Assert.Single(second);
        Assert.True(Vec2.Distance(second[0].Position, rock.Position) < rock.Radius + second[0].Radius + 10f);
        // zwei KI-Bohrschiffe (S und L), 40–60 vom Spieler, mit eigenem Zielfels
        var npcs = u.ShipsById().Where(s => s.Controller == Controller.Ai && s.Sector == u.PlayerSector && s.Role == ShipRole.Miner && s.MineAsteroid != 0
                                            && Vec2.Distance(s.Position, me.Position) is >= 40f and <= 60f && (s.ModelKey == "pickaxe" || s.ModelKey == "strip_miner")).ToList();
        Assert.Equal(new[] { "pickaxe", "strip_miner" }, npcs.Select(n => n.ModelKey).OrderBy(k => k).ToArray());
        Assert.NotEqual(npcs[0].MineAsteroid, npcs[1].MineAsteroid);
        // deterministisch: gleicher Seed → gleicher Hash
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(WorldBuilder.BuildDefault(startOption: "mining")));
    }

    [Fact]
    public void Bergbautest_FelsLeerUndZerbrochen_InUnter15s_MitAutoMining()
    {
        var u = WorldBuilder.BuildDefault(startOption: "mining");
        var me = u.Ships[u.PlayerShip];
        var sector = u.Sectors[u.PlayerSector];
        var chunks = new OreChunks(seed: 3);
        var rock = chunks.Nearest(sector, me.Position, ContactMining.ContactRange + ContactMining.BowOffset + 1f)!;
        int rockId = rock.Id;
        var ev = Capture(u);
        var auto = new MiningAutopilot();
        var run = new SimulationRunner();
        var pos = me.Position; var dir = (rock.Position - pos).Normalized();
        auto.Start();
        double acc = 0; const float dt = 0.05f;
        float depletedAt = -1f;
        for (float t = 0; t < 15f && depletedAt < 0f; t += dt)
        {
            acc += dt;
            while (acc >= 0.25) { run.Step(u); acc -= 0.25; }
            auto.Step(u, chunks, me, pos);
            if (me.AnchorRock != 0 && sector.Asteroids.FirstOrDefault(a => a.Id == me.AnchorRock) is { } held)
            {
                pos = MiningAnchor.HoldPosition(pos, held, Collision.ShipRadius);
                dir = (held.Position - pos).Normalized();
            }
            chunks.Contact(u, me, sector, pos, dir, dt);
            if (ev.OfType<AsteroidDepleted>().Any(d => d.Asteroid == rockId)) depletedAt = t;
        }
        _o.WriteLine($"Fels {rockId} leer nach {depletedAt:0.0} s, Fracht {me.UsedVolume(u):0.0}");
        Assert.True(depletedAt > 0f, "Fels in 15 s leer und entfernt (AsteroidDepleted)");
        Assert.DoesNotContain(sector.Asteroids, a => a.Id == rockId);
        Assert.True(me.UsedVolume(u) >= 24.5f);
    }

    [Fact]
    public void Bergbautest_KiSchuerfer_FliegenSelbstAn_OhneSprung_UndBohren()
    {
        var u = WorldBuilder.BuildDefault(startOption: "mining");
        var sector = u.Sectors[u.PlayerSector];
        var me = u.Ships[u.PlayerShip];
        var npcs = u.ShipsById().Where(s => s.Controller == Controller.Ai && s.Role == ShipRole.Miner && s.Sector == u.PlayerSector && s.MineAsteroid != 0
                                            && Vec2.Distance(s.Position, me.Position) <= 60f).ToList();
        Assert.Equal(2, npcs.Count);
        var start = npcs.ToDictionary(n => n.Id, n => n.Position);
        var last = npcs.ToDictionary(n => n.Id, n => n.Position);
        var maxStep = npcs.ToDictionary(n => n.Id, _ => 0f);
        var contactAt = new Dictionary<ShipId, int>();
        var flown = new Dictionary<ShipId, float>();
        var ev = Capture(u);
        var run = new SimulationRunner();
        for (int tick = 0; tick < 400; tick++)
        {
            run.Step(u);
            foreach (var n in npcs)
            {
                if (contactAt.ContainsKey(n.Id)) continue;   // gemessen wird der Anflug bis zum Kontakt (danach Verkaufsfahrten per Sprungfahrt)
                maxStep[n.Id] = MathF.Max(maxStep[n.Id], Vec2.Distance(last[n.Id], n.Position));
                last[n.Id] = n.Position;
                int rid = n.AnchorRock != 0 ? n.AnchorRock : n.MineAsteroid;
                if (!contactAt.ContainsKey(n.Id) && sector.Asteroids.FirstOrDefault(a => a.Id == rid) is { } r
                    && ContactMining.InContact(n.Position, (r.Position - n.Position).Normalized(), r, ContactMining.ToolReach(n, u)))
                { contactAt[n.Id] = tick; flown[n.Id] = Vec2.Distance(start[n.Id], n.Position); }
            }
        }
        foreach (var n in npcs)
        {
            _o.WriteLine($"{n.ModelKey}: Kontakt nach {(contactAt.TryGetValue(n.Id, out var c) ? c : -1)} Ticks, geflogen {flown.GetValueOrDefault(n.Id):0.0}, größter Schritt {maxStep[n.Id]:0.00} (Tempo {Travel.SpeedOf(n):0.000})");
            Assert.True(contactAt.ContainsKey(n.Id), $"{n.ModelKey} erreicht seinen Fels");
            Assert.True(flown[n.Id] > 15f, $"{n.ModelKey} fliegt selbst an");
            Assert.True(maxStep[n.Id] <= Travel.SpeedOf(n) + 0.01f, $"{n.ModelKey}: kein Sprung ({maxStep[n.Id]:0.00})");
            Assert.Contains(ev.OfType<ContactMined>(), cm => cm.Ship == n.Id);
        }
    }

    [Fact]
    public void Bergbautest_UngueltigeAngaben_Werfen()
    {
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        var o = def.Player.StartOptions.First(x => x.Key == "mining");
        o.Scenario!.NpcMiners = new List<string> { "falcon" };
        var ex = Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(def, null, "mining"));
        Assert.Contains("Kontaktwerkzeug", ex.Message);
    }
}
