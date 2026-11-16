using SpaceSim.Core.Demo;
using SpaceSim.Core.Events;
using SpaceSim.Core.Mining;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Tests;

/// <summary>
/// Runde 43 (Bergbau-Darstellung): NPC-Kontaktabbau nur bei echtem Oberflächenkontakt (Bug an der Felsoberfläche, dieselbe
/// <see cref="ContactMining"/>-Geometrie wie beim Spieler), Asteroiden schrumpfen nicht mehr (konstanter Basisradius,
/// Zerbrechen bei 0 in der Ansicht), Splitter-Fragmentradien weiterhin aus dem Flächenanteil.
/// </summary>
public class Runde43Tests
{
    [Fact]
    public void NpcContactMining_OnlyAtTheRockSurface_NeverFromAfar()
    {
        var u = DemoUniverse.Create();
        var sector = u.Sectors[new SectorId(1)];
        var field = sector.Fields[0];
        var miner = u.Ships.Values.First(s => s.Role == ShipRole.Miner && s.Stats.UsesContact && s.Stats.SplitPower <= 0f);
        Assert.True(miner.Stats.UsesContact);
        miner.Sector = sector.Id;
        miner.Transit = null;
        miner.CargoCapacity = 1_000_000f;
        miner.Cargo.Clear();
        miner.Position = field.WorkPoint(miner.Id);
        miner.Order = new MineOrder(sector.Id, 0, new StationId(1));
        miner.MinePhase = MinePhase.Mining;
        miner.Credits = 0f;
        foreach (var a in Belt.AsteroidsOf(sector, field)) { a.Ore = a.MaxOre = 100_000f; }   // läuft im Test nicht leer
        var events = new List<ContactMined>();
        var eventPos = new List<Vec2>();
        u.Events.Raised += e => { if (e is ContactMined c) { events.Add(c); eventPos.Add(miner.Position); } };

        // am Arbeitspunkt des Feldes ist kein Fels in Kontaktreichweite — genau das war der Bug (Abbau aus der Ferne)
        var anyRock = Belt.AsteroidsOf(sector, field).First();
        var dirFar = (anyRock.Position - miner.Position).Normalized();
        Assert.False(ContactMining.InContact(miner.Position, dirFar, anyRock));

        var system = new MinerSystem();
        float maxStep = 0f; int approachTicks = 0;
        for (int i = 0; i < 800; i++)
        {
            var before = miner.Position;
            system.Step(u, 1);
            float d = Vec2.Distance(before, miner.Position);
            maxStep = MathF.Max(maxStep, d);
            if (d > 1e-4f) approachTicks++;
        }
        // kein Sprung an den Fels: der Schürfer fliegt mit seinem Tempo (Einheiten je Tick) hin
        Assert.True(approachTicks >= 1, "Schürfer fliegt vom Arbeitspunkt an den Fels");
        Assert.True(maxStep <= Travel.SpeedOf(miner) + 1e-3f, $"größter Schritt {maxStep:0.00} > Tempo {Travel.SpeedOf(miner):0.00}");

        // nach dem Ansetzen steht der Bug an der Felsoberfläche (gleiche Geometrie wie beim Spieler)
        var rock = sector.Asteroids.First(a => a.Id == miner.MineAsteroid);
        var dir = (rock.Position - miner.Position).Normalized();
        float reach = ContactMining.ToolReach(miner, u);
        Assert.True(ContactMining.InContact(miner.Position, dir, rock, reach), "NPC-Schürfer steht mit der Werkzeugfront an der Felsoberfläche");
        Assert.True(Vec2.Distance(miner.Position, rock.Position) - rock.Radius <= ContactMining.ContactRange + reach + 1e-3f);
        // jedes ContactMined kam bei echtem Oberflächenkontakt zustande (nie aus der Ferne)
        Assert.NotEmpty(events);
        for (int i = 0; i < events.Count; i++)
        {
            var cm = events[i];
            Assert.True(Vec2.Distance(eventPos[i], cm.AsteroidPosition) - cm.AsteroidRadius <= ContactMining.ContactRange + reach + 1e-3f,
                $"ContactMined nur bei Kontakt (Distanz {Vec2.Distance(eventPos[i], cm.AsteroidPosition) - cm.AsteroidRadius:0.00})");
        }
        // Erzfluss und -erhaltung wie gehabt
        float mined = miner.UsedVolume(u);
        Assert.True(mined > 50f, $"abgebaut {mined:0}");
        Assert.Equal(events.Sum(x => x.Units), mined, 2);
    }

    [Fact]
    public void AsteroidRadius_StaysConstantWhileMined_SplitterKeepsAreaShare()
    {
        var u = DemoUniverse.Create();
        var sector = u.Sectors[u.PlayerSector];
        var field = sector.Fields[0];
        var rock = Belt.AsteroidsOf(sector, field).OrderByDescending(a => a.BaseRadius).First();
        foreach (var a in Belt.AsteroidsOf(sector, field).Where(a => a != rock).ToList()) sector.Asteroids.Remove(a);
        rock.Ore = rock.MaxOre = 1000f;
        float r0 = rock.BaseRadius;

        // teilweiser Abbau: Radius bleibt der Basisradius (Runde 43: kein Schrumpfen mehr)
        Belt.Remove(u, sector, rock, 500f);
        Assert.Equal(r0, rock.Radius);
        Assert.Equal(rock.BaseRadius, rock.Radius);
        Assert.True(rock.Minable);

        // Spalten: Fragmentradien weiterhin aus dem Flächenanteil (r = r0·√(Anteil)), jetzt des konstanten Radius
        var frags = AsteroidSplitter.Split(u, sector, rock, u.PlayerShip);
        Assert.NotNull(frags);
        Assert.All(frags, f => Assert.Equal(f.BaseRadius, f.Radius));
        float area = frags.Sum(f => f.BaseRadius * f.BaseRadius);
        Assert.True(MathF.Abs(area - r0 * r0) / (r0 * r0) < 0.02f, $"Flächenanteil bleibt erhalten ({area:0.00} vs {r0 * r0:0.00})");
        float oreSum = frags.Sum(f => f.Ore);
        Assert.True(MathF.Abs(oreSum - 500f * (1f - u.Rules.SplitDustLoss)) < 1f, "Erz verteilt sich auf die Fragmente (Staubverlust wie bisher)");

        // bei 0 Erz: AsteroidDepleted und weg (die Ansicht zerbricht ihn sichtbar), Respawn-Regel unverändert
        var depleted = new List<AsteroidDepleted>();
        u.Events.Raised += e => { if (e is AsteroidDepleted d) depleted.Add(d); };
        foreach (var f in frags.ToList()) Belt.Remove(u, sector, f, f.Ore);
        Assert.Equal(frags.Count, depleted.Count);
        Assert.DoesNotContain(sector.Asteroids, a => frags.Contains(a));
    }

    [Fact]
    public void ContactMining_GeometryUnchanged_ForPlayerAndNpc()
    {
        // dieselbe Geometrie wie bisher: Bug innerhalb 35° und höchstens ContactRange von der Oberfläche
        var rock = new Asteroid { Position = new Vec2(0f, 0f), BaseRadius = 5f, Ore = 100f, MaxOre = 100f };
        Assert.Equal(5f, rock.Radius);
        Assert.True(ContactMining.InContact(new Vec2(5f + ContactMining.BowOffset, 0f), new Vec2(-1f, 0f), rock), "Bug an der Oberfläche = Kontakt");
        Assert.False(ContactMining.InContact(new Vec2(30f, 0f), new Vec2(-1f, 0f), rock), "aus der Ferne kein Kontakt");
        Assert.False(ContactMining.InContact(new Vec2(5f + ContactMining.BowOffset, 0f), new Vec2(1f, 0f), rock), "hinter dem Fels kein Kontakt");
    }

    sealed class Listener : ISimSystem, ISimEventListener
    {
        public int Attached; public long FirstAttachTick = -1;
        public void Attach(Universe u) { Attached++; if (FirstAttachTick < 0) FirstAttachTick = u.Tick; }
        public void Step(Universe u, int dt) { }
    }
    sealed class ListenerModule(Listener l) : ISimModule
    {
        public string Name => "Probe";
        public void Register(SimulationBuilder b) => b.Add(l, 20);
    }

    /// <summary>
    /// Runde 43 (Speichern/Laden-Fehler): Ereignis-Hörer mit Intervall (Missionen, Comms) melden sich in jedem Tick am Bus an, nicht erst bei ihrem
    /// nächsten fälligen Schritt — sonst gingen nach dem Laden (neuer Runner, Bus nicht gespeichert) Ereignisse bis zum nächsten Vielfachen von 20 verloren.
    /// </summary>
    [Fact]
    public void Runner_AttachesEventListenersEveryTick_EvenBetweenIntervals()
    {
        var u = DemoUniverse.Create();
        u.Tick = 4843;   // kein Vielfaches des Intervalls
        var l = new Listener();
        var r = new SimulationRunner(new ISimModule[] { new ListenerModule(l) });
        r.Step(u);
        Assert.Equal(4843, l.FirstAttachTick);
        r.Step(u);
        Assert.Equal(2, l.Attached);
    }
}
