using SpaceSim.Core.Combat;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Events;
using SpaceSim.Core.Mining;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Weapons;

namespace SpaceSim.Tests;

/// <summary>Phase 29 H: Kontaktabbau (Bohrkopf, Schneidrad, Schneidwalze) — eine Regel für Spieler und NPC-Schürfer.</summary>
public class Phase29ContactTests
{
    static ShipStats StatsOf(Universe u, string model)
    {
        var m = u.ShipModels[model];
        return ShipStats.Compute(u.ShipClasses[m.ClassKey], m.Fit.Select(k => k is null ? null : u.Modules[k]).ToList());
    }

    [Fact]
    public void Inhalt_FastAlleSchuerferTragenKontaktwerkzeug_LaserBleibtBeiMehrzweck()
    {
        var u = DemoUniverse.Create();
        foreach (var key in new[] { "unit_miner_100", "unit_miner_60", "pickaxe", "prospector", "rockbreaker", "rockbreaker_ii", "quarry", "excavator", "strip_miner" })
        {
            var st = StatsOf(u, key);
            Assert.True(st.ContactMining > 0f, $"{key} ohne Kontaktwerkzeug");
            Assert.True(st.UsesContact, key);
            Assert.True((st.Capabilities & Capability.Mining) != 0, key);
        }
        foreach (var key in new[] { "pip", "tinker" }) Assert.True(StatsOf(u, key).HasLaser, $"{key} behält den Laser");
        // Stufen: Bohrkopf ≈ Laser Mk1, Schneidrad S ≈ Laser Mk2, Schneidrad M ≈ Laser Mk3, Walze darüber
        var cls = u.AsteroidClasses.Values.First();
        float Laser(string mod) => ContactMining.OrePerSecond(ShipStats.Compute(u.ShipClasses["unit_miner"], new List<Module?> { u.Modules["reactor_mk1"], u.Modules["chem_engine_s"], u.Modules[mod], u.Modules["cargo_universal_100"] }), cls);
        float Tool(string model) => ContactMining.ContactRate(StatsOf(u, model), cls);
        Assert.InRange(Tool("unit_miner_100") / Laser("mining_laser_1"), 0.9f, 1.15f);
        Assert.InRange(Tool("rockbreaker") / Laser("mining_laser_2"), 0.9f, 1.15f);
        Assert.True(Tool("strip_miner") > Tool("excavator") && Tool("excavator") > Tool("rockbreaker") && Tool("rockbreaker") > Tool("unit_miner_100"));
    }

    [Fact]
    public void Kontakt_NurMitDemBugAmFels()
    {
        var rock = new Asteroid { Position = new Vec2(0, 0), BaseRadius = 5f };
        var dir = new Vec2(-1, 0);   // Blick nach -x, auf den Fels
        Assert.True(ContactMining.InContact(new Vec2(7f, 0f), dir, rock));                       // Bug 2 vor dem Rumpf: Abstand Oberfläche 0
        Assert.True(ContactMining.InContact(new Vec2(9f, 0f), dir, rock));                       // 2 Abstand: noch Kontakt
        Assert.False(ContactMining.InContact(new Vec2(12f, 0f), dir, rock));                     // zu weit
        Assert.False(ContactMining.InContact(new Vec2(7f, 0f), new Vec2(1, 0), rock));           // Heck zum Fels
        Assert.False(ContactMining.InContact(new Vec2(7f, 0f), new Vec2(0, 1), rock));           // seitlich
        Assert.Equal(0f, ContactMining.Spin(new ShipStats(), ContactState.Mining));
    }

    static (Universe u, Ship ship, Sector sector, Asteroid rock, Vec2 pos, Vec2 dir) PlayerAtRock()
    {
        var u = DemoUniverse.Create();
        var ship = u.Ships[u.PlayerShip];
        Assert.Equal("unit_miner_100", ship.ModelKey);
        var sector = u.Sectors[u.PlayerSector];
        var rock = sector.Asteroids.First(a => a.Minable && u.AsteroidClasses.ContainsKey(a.ClassKey));
        rock.Ore = rock.MaxOre = 10_000f;
        var dir = new Vec2(-1, 0);
        var pos = rock.Position + new Vec2(1, 0) * (rock.Radius + 1.5f);
        return (u, ship, sector, rock, pos, dir);
    }

    [Fact]
    public void Spieler_BohrtDirektInDenLaderaum_ErzBleibtErhalten_OhneLaserschuss()
    {
        var (u, ship, sector, rock, pos, dir) = PlayerAtRock();
        u.Rules.MiningAnchors = false;   // Phase 29-Regel ohne Verankerung (Anker: Phase32Tests)
        var chunks = new OreChunks(seed: 3);
        float units = 0f;
        chunks.Contacted += (_, _, n) => units += n;
        float oreBefore = rock.Ore;
        for (float t = 0; t < 20f; t += 0.05f) Assert.Equal(ContactState.Mining, chunks.Contact(u, ship, sector, pos, dir, 0.05f));
        float mined = ship.UsedVolume(u);
        float expected = ContactMining.ContactRate(ship.Stats, u.AsteroidClasses[rock.ClassKey]) * 20f;
        Assert.InRange(mined, expected - 1.01f, expected + 0.01f);   // Portionen zu ganzen Einheiten
        Assert.Equal(oreBefore - mined, rock.Ore, 2);
        Assert.Equal(mined, units, 3);
        Assert.Empty(chunks.Chunks);   // keine Brocken, kein Traktor nötig
        Assert.Equal(MineResult.NoLaser, chunks.TryFire(u, ship, sector, rock, pos));
        // vom Fels weggedreht: kein Abbau
        Assert.Equal(ContactState.NoContact, chunks.Contact(u, ship, sector, pos, new Vec2(1, 0), 1f));
        // Laderaum voll: steht
        foreach (var g in ship.Cargo.Keys.ToList()) ship.Cargo[g] = 0f;
        ship.Cargo[DemoUniverse.Ore] = ship.CargoCapacity;
        Assert.Equal(ContactState.HoldFull, chunks.Contact(u, ship, sector, pos, dir, 1f));
        Assert.True(ship.UsedVolume(u) <= ship.CargoCapacity + 1e-3f);
    }

    [Fact]
    public void Schaechte_KontaktschiffZeigtBohrerStattLaser_MehrzweckBeides()
    {
        var u = DemoUniverse.Create();
        var ship = u.Ships[u.PlayerShip];
        var keys = WeaponBays.For(u, ship).Select(b => b.Key).ToList();
        Assert.Contains("contact", keys);
        Assert.DoesNotContain("laser", keys);
        Assert.Contains("tractor", keys);
        Assert.Equal("Abbau läuft", WeaponBays.ContactText(ContactState.Mining));
        Assert.Contains("laser", WeaponBays.For(StatsOf(u, "tinker")).Select(b => b.Key));
    }

    [Fact]
    public void NpcSchuerfer_BohrtMitDerselbenRegel_ErzErhalten_RateWieBerechnet()
    {
        var u = DemoUniverse.Create();
        var sector = u.Sectors[new SectorId(1)];
        var field = sector.Fields[0];
        var miner = u.Ships.Values.First(s => s.Role == ShipRole.Miner && s.Stats.UsesContact && s.Stats.SplitPower <= 0f);
        Assert.True(miner.Stats.UsesContact);
        miner.CargoCapacity = 1_000_000f;
        miner.Cargo.Clear();
        miner.Position = field.WorkPoint(miner.Id);
        miner.Order = new MineOrder(sector.Id, 0, new StationId(1));
        miner.MinePhase = MinePhase.Mining;
        miner.Credits = 0;
        var events = new List<ContactMined>();
        int shots = 0;
        u.Events.Raised += e => { if (e is ContactMined c) events.Add(c); else if (e is MinerShot) shots++; };
        float oreBefore = Belt.OreIn(sector, field);
        var system = new MinerSystem();
        const int ticks = 800;
        for (int i = 0; i < ticks; i++) system.Step(u, 1);
        float mined = miner.UsedVolume(u);
        Assert.Equal(0, shots);
        Assert.True(mined > 50f, $"abgebaut {mined:0}");
        Assert.Equal(oreBefore - mined, Belt.OreIn(sector, field), 1);
        Assert.Equal(events.Sum(x => x.Units), mined, 2);
        Assert.True(events.Max(e => e.Units) <= 1f + 1e-4f);
        // Rate: Werkzeug × Reichtum der Klasse (gemischte Felsen im Gürtel → Spanne)
        float perSecond = mined / (ticks * SimClock.TickInterval);
        float best = u.AsteroidClasses.Values.Max(c => ContactMining.ContactRate(miner.Stats, c));
        Assert.True(perSecond <= best * 1.05f, $"{perSecond:0.00} > {best:0.00}");
        Assert.True(perSecond >= MiningRules.ExpectedOrePerSecond * 0.3f, $"{perSecond:0.00}");
    }

    [Fact]
    public void NpcKontaktabbau_Deterministisch_UndSpeicherfest()
    {
        static Universe Run(int ticks)
        {
            var u = TestWorlds.Default(7);
            new SimulationRunner().Run(u, ticks);
            return u;
        }
        var a = Run(1500);
        var b = Run(1500);
        Assert.Equal(SpaceSim.Core.Diagnostics.UniverseHash.Compute(a), SpaceSim.Core.Diagnostics.UniverseHash.Compute(b));
        var (loaded, _) = SpaceSim.Core.Persistence.SaveGameJson.Read(SpaceSim.Core.Persistence.SaveGameJson.Write(a, DateTimeOffset.UtcNow));
        new SimulationRunner().Run(a, 600);
        new SimulationRunner().Run(loaded, 600);
        Assert.Equal(SpaceSim.Core.Diagnostics.UniverseHash.Compute(a), SpaceSim.Core.Diagnostics.UniverseHash.Compute(loaded));
    }
}

/// <summary>Phase 29 (Runde 10): Kaperer schonen die Beute — eine Raketensalve des Bombers vernichtet den Schürfer nicht mehr.</summary>
public class Phase29SpareLootTests
{
    static readonly SectorId Nebelmark = new(9);

    static (Universe u, Ship raider, Ship victim) Arena(ulong seed, bool spare)
    {
        var u = TestWorlds.Default(seed, fuel: false, research: false);
        u.Rules.PlayerRaids = false;
        u.Rules.RaidersSpareLoot = spare;
        // Gasernte (verschobener Zufallsweg): ein Treffer unter halber Hülle beschädigte das Frachtmodul, die Fracht fiel ins All — ohne Fracht gibt es nichts zu
        // schonen, die Regel greift richtig nicht mehr. Hier geht es um die Salve gegen ein beladenes Schiff: ohne Modulschaden
        u.Rules.DamageActive = false;
        var owner = u.ShipsById().First(ThreatMap.IsRaider).Owner;
        var yard = u.StationsById().First(s => !s.IsBuildSite);
        Ship Make(string model, ShipRole role, ActorId who)
        {
            var m = u.ShipModels[model];
            var s = ShipPurchase.Create(u, u.NewShipId(), model, u.ShipClasses[m.ClassKey].Slots, m.Fit, role, Controller.Ai, who, yard, 2000f);
            u.AddShip(s);
            if (s.Sector != Nebelmark) u.MoveShip(s, Nebelmark);
            s.DockedAt = default;
            return s;
        }
        var raider = Make("bomber", ShipRole.Pirate, owner);
        var victim = Make("prospector", ShipRole.Miner, u.FreeActor);
        raider.Position = new Vec2(0f, 0f);
        victim.Position = new Vec2(20f, 0f);
        victim.Cargo[u.FindGood("iron_ore")!.Id] = 30f;
        return (u, raider, victim);
    }

    static bool Destroyed(ulong seed, bool spare)
    {
        var (u, raider, victim) = Arena(seed, spare);
        bool gone = false;
        u.Events.Raised += e => { if (e is ShipDeparted d && d.Ship == victim.Id) gone = true; };
        var e = Engage.StartRaid(u, raider, victim, u.Sectors[Nebelmark]);
        e.Of(victim.Id)!.Courage = 0.05f;   // gibt erst sehr spät auf
        var combat = new CombatSystem();
        var systems = new ShipSystemsSystem();
        for (int i = 0; i < u.Rules.CombatMaxTicks + 10 && u.Fights.Count > 0; i++) { u.Tick++; combat.Step(u, 1); systems.Step(u, 1); }
        return gone;
    }

    [Fact]
    public void Bomber_VernichtetDieBeuteNicht_OhneRegelSchon()
    {
        int with = 0, without = 0;
        for (ulong seed = 1; seed <= 8; seed++)
        {
            if (Destroyed(seed, spare: true)) with++;
            if (Destroyed(seed, spare: false)) without++;
        }
        Assert.Equal(0, with);
        Assert.True(without > 0, "ohne die Regel sollte die Raketensalve den Skiff mindestens einmal vernichten");
    }
}

/// <summary>Runde 11: Andrücken am Fels (Kontaktabbau) beschädigt die Hülle nicht, Rammen schon — Abprallregel des Spielerschiffs aus dem Core.</summary>
public class Phase29ContactPushTests
{
    /// <summary>Fliegt mit festem Tempo geradeaus gegen einen Felsen (Kinematik wie die Ansicht: Bewegung, Kreiskollision, Abprall); größter Aufprall.</summary>
    static float MaxImpact(float speed, float seconds = 6f)
    {
        var rock = new Asteroid { Position = new Vec2(0f, 0f), BaseRadius = 6f };
        var obstacles = new List<Obstacle> { rock.ToObstacle() };
        var pos = new Vec2(30f, 0f);
        var dir = new Vec2(-1f, 0f);
        var vel = dir * speed;
        float worst = 0f;
        const float dt = 1f / 60f;
        for (float t = 0; t < seconds; t += dt)
        {
            // der Automodus/Schub hält das Solltempo in Flugrichtung (wie PlayerShip: Beschleunigung auf das Solltempo)
            var want = dir * speed;
            vel += (want - vel) * MathF.Min(1f, 4f * dt);
            pos += vel * dt;
            if (Collision.Resolve(ref pos, Collision.ShipRadius, obstacles, out var n)) worst = MathF.Max(worst, Collision.Bounce(ref vel, n));
        }
        return worst;
    }

    [Fact]
    public void AndrueckenMitKontaktschub_KeinSchaden_RammenMitVollgas_Schaden()
    {
        float push = MiningAutopilot.ContactPush * Collision.PlayerMaxSpeed;
        float soft = MaxImpact(push);
        Assert.True(soft > 0f, "das Schiff berührt den Fels");
        Assert.False(Collision.IsHard(soft), $"Andrücken {soft:0.0} m/s darf nicht schaden (Schwelle {Collision.HardImpact})");
        float hard = MaxImpact(Collision.PlayerMaxSpeed);
        Assert.True(Collision.IsHard(hard), $"Rammen mit Vollgas {hard:0.0} m/s schadet");
        // Abprall: Normalanteil wird mit 40 % Restenergie umgedreht, ein Schiff, das sich entfernt, prallt nicht
        var v = new Vec2(-10f, 0f);
        Assert.Equal(10f, Collision.Bounce(ref v, new Vec2(1f, 0f)), 3);
        Assert.Equal(4f, v.X, 3);
        Assert.Equal(0f, Collision.Bounce(ref v, new Vec2(1f, 0f)));
    }
}
