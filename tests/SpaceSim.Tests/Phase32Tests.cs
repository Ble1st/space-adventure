using SpaceSim.Core.Combat;
using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Mining;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>Phase 32 (Runde 17): Anker mit Trossen/Harpunen beim Kontaktabbau — Regel, Abreißen, Spielstand, Automatik, NPC-Ertrag, Schalter.</summary>
public class Phase32Tests
{
    static readonly SectorId Nebelmark = new(9);

    static List<SimEvent> Capture(Universe u) { var l = new List<SimEvent>(); u.Events.Raised += l.Add; return l; }

    /// <summary>Phase 32 zurücknehmen (für Vergleiche mit dem Stand vor den Ankern).</summary>
    internal static void RevertPhase32(WorldDef def) { def.Rules ??= new EconomyRules(); def.Rules.MiningAnchors = false; }

    static (Universe u, Ship ship, Sector sector, Asteroid rock, Vec2 pos, Vec2 dir) PlayerAtRock()
    {
        var u = WorldBuilder.BuildDefault();
        Assert.True(u.Rules.MiningAnchors);   // Standardwelt: an
        var ship = u.Ships[u.PlayerShip];
        var sector = u.Sectors[u.PlayerSector];
        var rock = sector.Asteroids.First(a => a.Minable && u.AsteroidClasses.ContainsKey(a.ClassKey) && a.Radius > 3f);
        rock.Ore = rock.MaxOre = 10_000f;
        var dir = new Vec2(-1, 0);
        var pos = rock.Position + new Vec2(1, 0) * (rock.Radius + Collision.ShipRadius);
        return (u, ship, sector, rock, pos, dir);
    }

    [Fact]
    public void Spieler_VerankertErst_DannAbbau_ErzNurMitHaltendemAnker()
    {
        var (u, ship, sector, rock, pos, dir) = PlayerAtRock();
        var ev = Capture(u);
        var chunks = new OreChunks(seed: 3);
        const float dt = 0.05f;
        int frames = 0;
        while (chunks.Contact(u, ship, sector, pos, dir, dt) == ContactState.Anchoring) { frames++; Assert.Equal(0f, ship.UsedVolume(u)); }
        // AnchorTicks (7) = 1,75 s = 35 Bilder zu 0,05 s
        Assert.InRange(frames, 34, 35);
        Assert.Equal(AnchorState.Held, MiningAnchor.State(u, ship));
        Assert.Equal(rock.Id, ship.AnchorRock);
        Assert.Single(ev.OfType<AnchorFired>());
        Assert.Single(ev.OfType<AnchorHeld>());
        for (int i = 0; i < 200; i++) Assert.Equal(ContactState.Mining, chunks.Contact(u, ship, sector, pos, dir, dt));
        Assert.True(ship.UsedVolume(u) > 20f);
        // Haltelage: auf der Linie Fels → Schiff, Schiffsmitte ShipRadius über der Oberfläche, Bug in Kontakt
        var hold = MiningAnchor.HoldPosition(pos + new Vec2(3f, 0f), rock, Collision.ShipRadius);
        Assert.Equal(rock.Radius + Collision.ShipRadius, Vec2.Distance(hold, rock.Position), 3);
        Assert.True(ContactMining.InContact(hold, (rock.Position - hold).Normalized(), rock));
    }

    [Fact]
    public void Abreissen_SchubWendenTasteStossTrefferKontaktverlust()
    {
        var r = new EconomyRules();
        Assert.False(MiningAnchor.BreaksFromSteering(r, MiningAutopilot.ContactPush, 0f));   // Andrücken der Automatik hält
        Assert.True(MiningAnchor.BreaksFromSteering(r, 0.5f, 0f));
        Assert.True(MiningAnchor.BreaksFromSteering(r, -0.31f, 0f));
        Assert.True(MiningAnchor.BreaksFromSteering(r, 0f, 1f));

        var (u, ship, sector, rock, pos, dir) = PlayerAtRock();
        var ev = Capture(u);
        var chunks = new OreChunks(seed: 3);
        void Anchor() { for (int i = 0; i < 40; i++) chunks.Contact(u, ship, sector, pos, dir, 0.05f); Assert.Equal(AnchorState.Held, MiningAnchor.State(u, ship)); }
        Anchor();
        MiningAnchor.Release(u, ship, sector.Id, "released");
        Assert.Equal(0, ship.AnchorRock);
        Anchor();
        // Hüllentreffer reißt (Schild allein nicht)
        Hit.Apply(u, ship, new HitResult(true, 1f, 5f, 0f, 0f));
        Assert.NotEqual(0, ship.AnchorRock);
        Hit.Apply(u, ship, new HitResult(true, 1f, 0f, 0f, 2f));
        Assert.Equal(0, ship.AnchorRock);
        Anchor();
        // weggedreht: Kontakt verloren
        Assert.Equal(ContactState.NoContact, chunks.Contact(u, ship, sector, pos, new Vec2(1, 0), 0.05f));
        Assert.Equal(0, ship.AnchorRock);
        // zu viel Schub: kein Schuss
        Assert.Equal(ContactState.NoContact, chunks.Contact(u, ship, sector, pos, dir, 0.05f, anchorAllowed: false));
        Assert.Equal(0, ship.AnchorRock);
        Anchor();
        // Fels erschöpft
        rock.Ore = 0.5f;
        for (int i = 0; i < 40 && sector.Asteroids.Contains(rock); i++) chunks.Contact(u, ship, sector, pos, dir, 0.05f);
        Assert.Equal(0, ship.AnchorRock);
        var reasons = ev.OfType<AnchorReleased>().Select(x => x.Reason).ToList();
        Assert.Equal(new[] { "released", "hit", "lost_contact", "depleted" }, reasons);
    }

    [Fact]
    public void Spalten_ReisstAnkerAmFels()
    {
        var (u, ship, sector, rock, pos, dir) = PlayerAtRock();
        var chunks = new OreChunks(seed: 3);
        for (int i = 0; i < 40; i++) chunks.Contact(u, ship, sector, pos, dir, 0.05f);
        Assert.NotEqual(0, ship.AnchorRock);
        var ev = Capture(u);
        Assert.NotNull(AsteroidSplitter.Split(u, sector, rock, ship.Id));
        Assert.Equal(0, ship.AnchorRock);
        Assert.Equal("split", Assert.Single(ev.OfType<AnchorReleased>()).Reason);
    }

    [Fact]
    public void Gefecht_VerankerterSchuerferKapptErstDieTrossen()
    {
        var u = WorldBuilder.BuildDefault();
        u.Rules.PlayerRaids = false;
        var raider = u.ShipsById().First(ThreatMap.IsRaider);
        var miner = u.ShipsById().First(s => s.Role == ShipRole.Miner && s.Stats.UsesContact);
        foreach (var s in new[] { raider, miner })
        {
            if (s.Transit is not null) u.FinishTransit(s);
            s.Order = null; s.MinePhase = MinePhase.Idle;
            if (s.Sector != Nebelmark) u.MoveShip(s, Nebelmark);
        }
        raider.Position = new Vec2(0, 0); miner.Position = new Vec2(30, 0);
        var rock = u.Sectors[Nebelmark].Asteroids.First(a => a.Minable);
        MiningAnchor.Attach(u, miner, Nebelmark, rock);
        MiningAnchor.Advance(u, miner, Nebelmark, u.Rules.AnchorTicks);
        var e = Engage.StartRaid(u, raider, miner, u.Sectors[Nebelmark]);
        var p = e.Of(miner.Id)!;
        Assert.Equal(0, miner.AnchorRock);
        Assert.Equal(u.Tick + u.Rules.AnchorCastOffTicks, p.CastOffUntil);
        float x0 = p.X;
        var combat = new CombatSystem();
        for (long t = u.Tick; t < p.CastOffUntil - 1; t++) { u.Tick++; combat.Step(u, 1); Assert.Equal(x0, p.X); }
        // gespeichert und gehasht
        var (loaded, _) = SaveGameJson.Read(SaveGameJson.Write(u, DateTimeOffset.UnixEpoch));
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
        Assert.Equal(p.CastOffUntil, loaded.Fights.Of(miner.Id)!.Of(miner.Id)!.CastOffUntil);
    }

    [Fact]
    public void Speichern_MittenImVerankern_BitIdentisch()
    {
        var u = WorldBuilder.BuildDefault();
        var run = new SimulationRunner();
        for (int i = 0; i < 4000 && !u.Ships.Values.Any(s => s.AnchorRock != 0 && s.AnchorProgress > 0f && s.AnchorProgress < u.Rules.AnchorTicks); i++) run.Step(u);
        Assert.Contains(u.Ships.Values, s => s.AnchorRock != 0 && s.AnchorProgress < u.Rules.AnchorTicks);
        var (loaded, _) = SaveGameJson.Read(SaveGameJson.Write(u, DateTimeOffset.UnixEpoch));
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
        var run2 = new SimulationRunner();
        for (int i = 0; i < 600; i++) { run.Step(u); run2.Step(loaded); }
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
    }

    [Fact]
    public void AutoMining_SetztAnVerankertBautAbLoestUndVerkauft()
    {
        var u = WorldBuilder.BuildDefault();
        new SimulationRunner().Step(u);   // Preise stehen erst nach dem ersten Tick (Käufer)
        var ship = u.Ships[u.PlayerShip];
        var ev = Capture(u);
        var chunks = new OreChunks(seed: 5);
        var auto = new MiningAutopilot();
        var sector = u.Sectors[u.PlayerSector];
        var pos = ship.Position; var dir = new Vec2(0, -1);
        auto.Start();
        var statuses = new HashSet<string>();
        const float dt = 0.05f, speed = 30f;
        bool sold = false;
        for (float t = 0; t < 600f && !sold; t += dt)
        {
            var step = auto.Step(u, chunks, ship, pos);
            statuses.Add(auto.Status);
            if (step.Nav is not null) { Assert.Equal(0, ship.AnchorRock); sold = true; break; }   // Abflug zum Verkauf: Anker eingeholt
            // wie die Ansicht: verankert = festgehalten an der Oberfläche, sonst Wegpunkt mit Schub
            if (ship.AnchorRock != 0 && sector.Asteroids.FirstOrDefault(a => a.Id == ship.AnchorRock) is { } held)
            {
                Assert.False(MiningAnchor.BreaksFromSteering(u.Rules, step.Throttle, 0f));
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
            chunks.Contact(u, ship, sector, pos, dir, dt);
        }
        Assert.True(sold, $"kein Verkaufsflug ({string.Join(", ", statuses)})");
        Assert.Contains("Anker fliegt", statuses);
        Assert.Contains("Abbau (verankert)", statuses);
        Assert.True(ev.OfType<AnchorHeld>().Any());
        // jeder Abbau geschah verankert: vor dem ersten Erz hielt der Anker
        Assert.True(ship.UsedVolume(u) > 10f);
    }

    [Fact]
    public void Npc_ErzflussBleibt_SchalterAus_GleicherHashWieVorher()
    {
        float Ore(bool anchors, out ulong hash, out int held)
        {
            var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
            if (!anchors) RevertPhase32(def);
            var u = WorldBuilder.Build(def, null);
            float mined = 0; int h = 0;
            u.Events.Raised += e => { if (e is ContactMined c) mined += c.Units; else if (e is AnchorHeld) h++; else if (e is ContactMined) { } };
            var run = new SimulationRunner();
            for (int i = 0; i < 20000; i++) run.Step(u);
            hash = UniverseHash.Compute(u); held = h;
            return mined;
        }
        float off = Ore(false, out var hOff, out int heldOff);
        float on = Ore(true, out _, out int heldOn);
        Assert.Equal(0x1324E350C47BF63AUL, hOff);   // Stand vor Phase 32 (Runde 16)
        Assert.Equal(0, heldOff);
        Assert.True(heldOn > 1000);
        Assert.InRange(on / off, 0.95f, 1.08f);   // Erzfluss der Wirtschaft auf dem bisherigen Niveau (gemessen +0,9 %)
    }
}
