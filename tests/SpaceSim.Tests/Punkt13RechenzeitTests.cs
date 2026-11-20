using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Punkt 13 (Rechenzeit): die neuen Zwischenspeicher liefern dasselbe wie die Neuberechnung und werden bei jeder Änderung ihrer Eingaben verworfen —
/// Sektorpfade (<see cref="SectorPathCache"/>), Regierungsstationen (<see cref="Universe.GovernmentCandidates"/>), Zeugen des Mindestbestands
/// (<see cref="FactionViability.ReplacementCost"/>), Gedächtnis der Spielerflugzeit (<see cref="PlayerTrip.Seconds(Universe, SectorId, Vec2, SectorId, Vec2, Ship?, bool)"/>);
/// eine Welt mit und ohne Zwischenspeicher läuft bitgleich, auch über Speichern/Laden.
/// </summary>
public class Punkt13RechenzeitTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    static void AssertSamePath(IReadOnlyList<Vec2> a, IReadOnlyList<Vec2> b, string what)
    {
        Assert.True(a.Count == b.Count, $"{what}: {a.Count} vs {b.Count} Punkte");
        for (int i = 0; i < a.Count; i++)
        {
            Assert.Equal(BitConverter.SingleToInt32Bits(a[i].X), BitConverter.SingleToInt32Bits(b[i].X));
            Assert.Equal(BitConverter.SingleToInt32Bits(a[i].Y), BitConverter.SingleToInt32Bits(b[i].Y));
        }
    }

    /// <summary>Strecken durch den Sektor: von jedem Tor zu jeder Station (wie NPC-Reisen) — Zwischenspeicher = Neuplanung.</summary>
    static List<(Vec2 From, Vec2 To)> Pairs(Universe u, Sector s)
    {
        var pairs = new List<(Vec2, Vec2)>();
        foreach (var gid in s.Gates)
            foreach (var sid in s.Stations)
                pairs.Add((u.Gates[gid].Position, u.Stations[sid].DockPoint(new ShipId(7))));
        foreach (var f in s.Fields)
            foreach (var sid in s.Stations) pairs.Add((f.Position, u.Stations[sid].DockPoint(new ShipId(3))));
        return pairs;
    }

    static void AssertAllCachedEqual(Universe u, Sector s, string what)
    {
        foreach (var (from, to) in Pairs(u, s))
        {
            AssertSamePath(Travel.SectorPath(u, s.Id, from, to), Travel.SectorPathUncached(u, s.Id, from, to), what);
            AssertSamePath(Travel.SectorPath(u, s.Id, from, to), Travel.SectorPathUncached(u, s.Id, from, to), what + " (Treffer)");
        }
    }

    [Fact]
    public void Pfadspeicher_GleichWieNeuplanung_UndVerworfenBeiJederHindernisaenderung()
    {
        var u = TestWorlds.Default();
        var s = u.Sectors.Values.OrderByDescending(x => x.Asteroids.Count).First();
        AssertAllCachedEqual(u, s, "Start");
        long hitsBefore = u.PathCache.Hits;
        AssertAllCachedEqual(u, s, "zweiter Durchgang");
        Assert.True(u.PathCache.Hits > hitsBefore, "Zweiter Durchgang muss aus dem Speicher kommen");

        // Fels mitten auf eine Strecke legen: der gemerkte Pfad wäre falsch, der Speicher muss ihn verwerfen
        var (from, to) = Pairs(u, s).First();
        var mid = (from + to) * 0.5f;
        var before = Travel.SectorPath(u, s.Id, from, to);
        s.Asteroids.Add(new Asteroid { Id = u.NewAsteroidId(), Position = mid, BaseRadius = 9f });
        var after = Travel.SectorPath(u, s.Id, from, to);
        AssertSamePath(after, Travel.SectorPathUncached(u, s.Id, from, to), "neuer Fels");
        Assert.False(before.SequenceEqual(after), "der Fels liegt auf der Strecke — der Pfad muss sich ändern");
        AssertAllCachedEqual(u, s, "neuer Fels, alle");

        // Fels verschieben, Fels entfernen, Radius ändern
        s.Asteroids[^1].Position = mid + new Vec2(4f, -3f);
        AssertAllCachedEqual(u, s, "Fels verschoben");
        s.Asteroids[^1].BaseRadius = 2f;
        AssertAllCachedEqual(u, s, "Radius geändert");
        s.Asteroids.RemoveAt(s.Asteroids.Count - 1);
        AssertAllCachedEqual(u, s, "Fels entfernt");
        s.Asteroids.RemoveAt(0);
        AssertAllCachedEqual(u, s, "erster Fels entfernt");

        // Station verschieben und entfernen
        var st = u.Stations[s.Stations[0]];
        st.Position += new Vec2(30f, 0f);
        AssertAllCachedEqual(u, s, "Station verschoben");
        u.RemoveStation(st.Id, "test");
        AssertAllCachedEqual(u, s, "Station entfernt");

        // Maßstab 1 = einfache Planung statt robuster
        s.Scale = 1f;
        AssertAllCachedEqual(u, s, "Maßstab 1");
    }

    [Fact]
    public void Pfadspeicher_GeteilteListeBleibtUnveraendert_KopieFuerAufrufer()
    {
        var u = TestWorlds.Default();
        var s = u.Sectors.Values.First(x => x.Gates.Count > 0 && x.Stations.Count > 0);
        var (from, to) = Pairs(u, s).First();
        var a = Travel.SectorPath(u, s.Id, from, to);
        a.Clear();   // der Aufrufer darf seine Kopie verändern
        AssertSamePath(Travel.SectorPath(u, s.Id, from, to), Travel.SectorPathUncached(u, s.Id, from, to), "nach Clear der Kopie");
    }

    [Fact]
    public void Regierungsstationen_FolgenStationenUndTypen()
    {
        var u = TestWorlds.Default();
        static List<Station> Reference(Universe u) =>
            u.StationsById().Where(st => u.StationTypes.TryGetValue(st.TypeKey, out var t) && t.Government != GovernmentKind.None).ToList();
        void Check(string what) => Assert.True(Reference(u).SequenceEqual(u.GovernmentCandidates()), what);

        Check("Start");
        var factions = u.NpcFactionIds().ToList();
        var obligatory = factions.Select(f => FactionViability.ObligatoryPerTick(u, f)).ToList();

        // Regierungsstation entfernen
        var gov = Reference(u).First();
        u.RemoveStation(gov.Id, "test");
        Check("entfernt");

        // neue Regierungsstation
        var hqType = u.StationTypes.Values.First(t => t.Government == GovernmentKind.Headquarters);
        var sector = u.Sectors.Values.First();
        var st = StationFactory.Create(u, u.NewStationId(), hqType, "Test-HQ", sector.Id, new Vec2(1234f, 1234f), new ActorId(factions[0].Value));
        u.AddStation(st);
        Check("hinzugefügt");

        // ein Wirtschaftstyp wird zur Regierungsart (Typ verändert, Stationsmenge gleich)
        var econ = u.StationTypes.Values.First(t => t.Government == GovernmentKind.None && u.Stations.Values.Any(x => x.TypeKey == t.Key));
        econ.Government = GovernmentKind.Office;
        Check("Typ geändert");
        econ.Government = GovernmentKind.None;
        Check("Typ zurück");

        // Ergebnis wie ohne Zwischenspeicher
        foreach (var f in factions)
        {
            float cached = FactionViability.ObligatoryPerTick(u, f);
            u.PerfCaches = false;
            float plain = FactionViability.ObligatoryPerTick(u, f);
            u.PerfCaches = true;
            Assert.Equal(BitConverter.SingleToInt32Bits(plain), BitConverter.SingleToInt32Bits(cached));
        }
        Assert.Equal(factions.Count, obligatory.Count);
    }

    [Fact]
    public void Mindestbestand_ZeugenErkennenVerlustUndRollenwechsel()
    {
        var u = TestWorlds.Default();
        var f = u.NpcFactionIds().First(x => SecuritySystem.PoliceOf(u, x).Count > 0);
        float Plain()
        {
            u.PerfCaches = false;
            float v = FactionViability.ReplacementCost(u, f);
            u.PerfCaches = true;
            return v;
        }
        Assert.Equal(Plain(), FactionViability.ReplacementCost(u, f));
        Assert.Equal(Plain(), FactionViability.ReplacementCost(u, f));   // aus den Zeugen
        Assert.True(u.ViabilityWitness.Count > 0);

        // Rollenwechsel eines Zeugen: Bedingung wird live geprüft
        var police = SecuritySystem.PoliceOf(u, f);
        foreach (var p in police) p.Role = ShipRole.Trader;
        Assert.Equal(Plain(), FactionViability.ReplacementCost(u, f));
        Assert.True(FactionViability.ReplacementCost(u, f) > 0f, "ohne Polizei fehlt der Mindestbestand");
        foreach (var p in police) p.Role = ShipRole.Police;
        Assert.Equal(0f, FactionViability.ReplacementCost(u, f));

        // Verlust aller Polizeischiffe
        foreach (var p in police) u.RemoveShip(p.Id, "test");
        Assert.Equal(Plain(), FactionViability.ReplacementCost(u, f));
        Assert.True(FactionViability.MinimumMissing(u, f));

        // Schiffe direkt aus dem Dictionary entfernt (wie manche Tests): kein falscher Treffer aus den Zeugen
        var fresh = TestWorlds.Default();
        var pf = SecuritySystem.PoliceOf(fresh, f).First();
        Assert.False(FactionViability.MinimumMissing(fresh, f));
        foreach (var p in SecuritySystem.PoliceOf(fresh, f)) fresh.Ships.Remove(p.Id);
        Assert.True(FactionViability.MinimumMissing(fresh, f));
        Assert.NotNull(pf);
    }

    [Fact]
    public void Spielerflugzeit_MitGedaechtnisGleichesErgebnis()
    {
        var u = TestWorlds.Default();
        var stations = u.StationsById().Where(s => s.Dockable).ToList();
        for (int i = 0; i < stations.Count; i += 9)
            for (int j = 4; j < stations.Count; j += 13)
            {
                var a = stations[i]; var b = stations[j];
                float with = PlayerTrip.Seconds(u, a.Sector, a.DockPoint(default), b.Sector, b.DockPoint(default), null, true);
                float without = PlayerTrip.Seconds(u, a.Sector, a.DockPoint(default), b.Sector, b.DockPoint(default), null, false);
                Assert.Equal(BitConverter.SingleToInt32Bits(without), BitConverter.SingleToInt32Bits(with));
            }
    }

    [Fact]
    public void Welt_MitUndOhneZwischenspeicher_Bitgleich_AuchUeberSpeichernLaden()
    {
        var cached = TestWorlds.Default();
        var plain = TestWorlds.Default();
        plain.PerfCaches = false;
        var r1 = new SimulationRunner();
        var r2 = new SimulationRunner();
        r1.Run(cached, 2000);
        r2.Run(plain, 2000);
        Assert.Equal(UniverseHash.Compute(plain), UniverseHash.Compute(cached));
        Assert.True(cached.PathCache.Hits > 0, "der Pfadspeicher wurde benutzt");

        // Speichern/Laden: abgeleitete Speicher beginnen leer, der Lauf bleibt bitgleich
        var (loaded, _) = SaveGameJson.Read(SaveGameJson.Write(cached, Now));
        Assert.Equal(0, loaded.PathCache.Hits + loaded.PathCache.Misses);
        r1.Run(cached, 1500);
        new SimulationRunner().Run(loaded, 1500);
        r2.Run(plain, 1500);
        Assert.Equal(UniverseHash.Compute(plain), UniverseHash.Compute(cached));
        Assert.Equal(UniverseHash.Compute(cached), UniverseHash.Compute(loaded));
    }
}
