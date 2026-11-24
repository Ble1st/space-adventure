using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Intel;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Mehrpunkt-Route des Spielerautopiloten (Lücke gegenüber Interstellar Pilot 2): Halte anlegen/entfernen, nächster Halt, „erreicht?“, Fortschritt,
/// Anzeige, Flugzeitschätzung, Kartenpfad, Speichern/Laden und — reine Spielerfunktion — die NPC-Welt bleibt bitgleich.
/// </summary>
public class PlayerRouteTests
{
    static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    static Universe World(string start = "trader", bool intel = false)
    {
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        def.RandomNames = false;
        def.Rules!.PlayerIntel = intel;
        return WorldBuilder.Build(def, null, start);
    }

    static Ship Me(Universe u) => u.Ships[u.PlayerShip];

    /// <summary>Stationen eines Sektors (andockbar), nach Id.</summary>
    static List<Station> Docks(Universe u, SectorId s) => u.StationsById().Where(x => x.Sector == s && x.Dockable && x.Site is null).ToList();

    /// <summary>Ein Nachbarsektor über ein Tor des Spielersektors.</summary>
    static (Gate Gate, SectorId Far) Neighbour(Universe u)
    {
        var g = u.Sectors[u.PlayerSector].Gates.Select(id => u.Gates[id]).OrderBy(x => x.Id.Value).First();
        return (g, u.Gates[g.Target].Sector);
    }

    [Fact]
    public void Halte_AnlegenEntfernenLeeren()
    {
        var u = World();
        Assert.Null(PlayerRoutes.Of(u));
        var st = Docks(u, u.PlayerSector);
        var (gate, far) = Neighbour(u);
        Assert.True(PlayerRoutes.Add(u, PlayerRoutes.ForStation(u, st[0].Id)));
        Assert.True(PlayerRoutes.Add(u, PlayerRoutes.ForGate(u, gate.Id)));
        Assert.True(PlayerRoutes.Add(u, PlayerRoutes.ForSector(far)));
        Assert.True(PlayerRoutes.Add(u, PlayerRoutes.ForPoint(far, new Vec2(10, 20))));
        var r = PlayerRoutes.Of(u)!;
        Assert.Equal(4, r.Stops.Count);
        Assert.Equal(0, r.Next);
        // derselbe Halt direkt hintereinander nicht
        Assert.Equal("Schon letzter Halt", PlayerRoutes.AddIssue(u, PlayerRoutes.ForPoint(far, new Vec2(10, 20))));
        Assert.True(PlayerRoutes.Remove(u, 1));
        Assert.Equal(RouteStopKind.Sector, r.Stops[1].Kind);
        PlayerRoutes.Clear(u);
        Assert.Null(PlayerRoutes.Of(u));
        // höchstens MaxStops offene Halte
        for (int i = 0; i < PlayerRoutes.MaxStops; i++) Assert.True(PlayerRoutes.Add(u, PlayerRoutes.ForPoint(u.PlayerSector, new Vec2(i * 30, 0))));
        Assert.StartsWith("Route voll", PlayerRoutes.AddIssue(u, PlayerRoutes.ForPoint(u.PlayerSector, new Vec2(999, 0))));
    }

    [Fact]
    public void Erkundung_UnbekannteZieleNichtAufDieRoute()
    {
        var u = World(intel: true);
        var unknown = u.Sectors.Keys.First(s => !Knowledge.KnowsSector(u, s));
        Assert.Equal("Sektor unbekannt", PlayerRoutes.AddIssue(u, PlayerRoutes.ForSector(unknown)));
        var hidden = u.StationsById().FirstOrDefault(s => Knowledge.KnowsSector(u, s.Sector) && !Knowledge.KnowsStation(u, s));
        if (hidden is not null) Assert.Equal("Station unbekannt", PlayerRoutes.AddIssue(u, PlayerRoutes.ForStation(u, hidden.Id)));
    }

    [Fact]
    public void Erreicht_TorImSektorDahinter_StationAmRand()
    {
        var u = World();
        var st = Docks(u, u.PlayerSector)[0];
        var (gate, far) = Neighbour(u);
        var sStop = PlayerRoutes.ForStation(u, st.Id);
        Assert.False(PlayerRoutes.Reached(u, sStop, u.PlayerSector, st.Position + new Vec2(st.Radius + 40f, 0)));
        Assert.True(PlayerRoutes.Reached(u, sStop, u.PlayerSector, st.Position + new Vec2(st.Radius + 2f, 0)));
        var leg = PlayerRoutes.Leg(u, sStop)!.Value;
        Assert.Equal(st.Id, leg.Station);
        var gStop = PlayerRoutes.ForGate(u, gate.Id);
        Assert.False(PlayerRoutes.Reached(u, gStop, u.PlayerSector, gate.Position));   // am Tor, aber noch nicht durch
        Assert.True(PlayerRoutes.Reached(u, gStop, far, new Vec2(0, 0)));
        Assert.Equal(far, PlayerRoutes.Leg(u, gStop)!.Value.Target.Sector);
        Assert.False(string.IsNullOrEmpty(PlayerRoutes.Leg(u, gStop)!.Value.Target.Label));
    }

    [Fact]
    public void Fortschritt_ZeileUndUeberspringen()
    {
        var u = World();
        var st = Docks(u, u.PlayerSector);
        var (gate, far) = Neighbour(u);
        PlayerRoutes.Add(u, PlayerRoutes.ForStation(u, st[0].Id));
        PlayerRoutes.Add(u, PlayerRoutes.ForGate(u, gate.Id));
        PlayerRoutes.Add(u, PlayerRoutes.ForSector(far));
        Assert.Equal($"Route 1/3 → {PlayerRoutes.Label(u, PlayerRoutes.Of(u)!.Stops[0])}", PlayerRoutes.HudLine(u, true));
        Assert.EndsWith("· angehalten", PlayerRoutes.HudLine(u, false));
        Assert.True(PlayerRoutes.Advance(u));
        Assert.StartsWith("Route 2/3 → Tor →", PlayerRoutes.HudLine(u, true));
        // im Sektor hinter dem Tor: Tor-Halt erreicht und übersprungen
        Assert.True(PlayerRoutes.SkipReached(u, far, new Vec2(3000, 3000)));
        Assert.Equal(RouteStopKind.Sector, PlayerRoutes.Current(u)!.Kind);
        Assert.False(PlayerRoutes.Advance(u));
        Assert.True(PlayerRoutes.Of(u)!.Finished);
        Assert.Equal("", PlayerRoutes.HudLine(u, true));
        // abgeflogen: Neustart von vorn; ein neuer Halt beginnt eine neue Route
        PlayerRoutes.Restart(u);
        Assert.Equal(0, PlayerRoutes.Of(u)!.Next);
        PlayerRoutes.Of(u)!.Next = 3;
        PlayerRoutes.Add(u, PlayerRoutes.ForPoint(far, new Vec2(1, 1)));
        Assert.Single(PlayerRoutes.Of(u)!.Stops);
    }

    [Fact]
    public void Flugzeit_SummeDerAbschnitte()
    {
        var u = World();
        var me = Me(u);
        var st = Docks(u, u.PlayerSector);
        var (_, far) = Neighbour(u);
        PlayerRoutes.Add(u, PlayerRoutes.ForStation(u, st[0].Id));
        float one = PlayerRoutes.EstimateSeconds(u, u.PlayerSector, me.Position, me);
        var t0 = PlayerRoutes.Leg(u, PlayerRoutes.Of(u)!.Stops[0])!.Value.Target;
        Assert.Equal(PlayerTrip.Seconds(u, u.PlayerSector, me.Position, t0.Sector, t0.Position, me), one, 3);
        PlayerRoutes.Add(u, PlayerRoutes.ForSector(far));
        float two = PlayerRoutes.EstimateSeconds(u, u.PlayerSector, me.Position, me);
        Assert.True(two > one && !float.IsInfinity(two), $"{one} → {two}");
        Assert.Contains("2 Halte · Flug ~", PlayerRoutes.Summary(u, u.PlayerSector, me.Position, me));
    }

    [Fact]
    public void Kartenpfad_DurchflogeneSektorenUndNummern()
    {
        var u = World();
        // ein Sektor zwei Sprünge weit
        var two = u.Sectors.Keys.First(s => u.FindRoute(u.PlayerSector, s)?.Count == 2);
        PlayerRoutes.Add(u, PlayerRoutes.ForSector(two));
        PlayerRoutes.Add(u, PlayerRoutes.ForSector(u.PlayerSector));
        var path = PlayerRoutes.MapPath(u);
        Assert.Equal(u.PlayerSector, path[0].Sector);
        Assert.Equal(0, path[0].Stop);
        Assert.Equal(0, path[1].Stop);           // Durchflug
        Assert.Equal((two, 1), path[2]);
        Assert.Equal(2, path[^1].Stop);
        Assert.Equal(u.PlayerSector, path[^1].Sector);
        Assert.Equal(5, path.Count);              // hin über einen Zwischensektor, zurück über einen
        var here = Docks(u, u.PlayerSector)[0];
        PlayerRoutes.Clear(u);
        PlayerRoutes.Add(u, PlayerRoutes.ForStation(u, here.Id));
        var inSector = PlayerRoutes.StopsIn(u, u.PlayerSector);
        Assert.Single(inSector);
        Assert.Equal(here.Position, inSector[0].At);
    }

    /// <summary>Kinematisches Abfliegen wie die Ansicht: Wegpunkte des PathFollower, Torsprung im Sprungradius, Halte der Reihe nach.</summary>
    [Fact]
    public void Abflug_KinematischAlleHalteDerReiheNach()
    {
        var u = World();
        var me = Me(u);
        var st = Docks(u, u.PlayerSector);
        var (gate, far) = Neighbour(u);
        var farSt = Docks(u, far)[0];
        PlayerRoutes.Add(u, PlayerRoutes.ForStation(u, st[^1].Id));
        PlayerRoutes.Add(u, PlayerRoutes.ForGate(u, gate.Id));
        PlayerRoutes.Add(u, PlayerRoutes.ForStation(u, farSt.Id));
        PlayerRoutes.Add(u, PlayerRoutes.ForPoint(far, farSt.Position + new Vec2(80, 80)));
        var sector = u.PlayerSector;
        var pos = me.Position;
        var f = new PathFollower();
        var reached = new List<RouteStopKind>();
        for (int i = 0; i < 40_000 && PlayerRoutes.Current(u) is { } cur; i++)
        {
            if (PlayerRoutes.Reached(u, cur, sector, pos)) { reached.Add(cur.Kind); PlayerRoutes.Advance(u); f.Reset(); continue; }
            var leg = PlayerRoutes.Leg(u, cur)!.Value;
            var step = f.Next(u, sector, pos, leg.Target, me);
            Assert.NotEqual(NavStatus.Unreachable, step.Status);
            var d = step.Waypoint - pos;
            float len = d.Length;
            if (len > 0.001f) pos += d * (MathF.Min(45f * 0.25f, len) / len);
            if (step.Gate is { } gid && Vec2.Distance(pos, u.Gates[gid].Position) < Navigator.GateJumpRadius)
            { var other = u.Gates[u.Gates[gid].Target]; sector = other.Sector; pos = other.Position; f.Reset(); }
        }
        Assert.Equal(new[] { RouteStopKind.Station, RouteStopKind.Gate, RouteStopKind.Station, RouteStopKind.Point }, reached);
        Assert.True(PlayerRoutes.Of(u)!.Finished);
        Assert.Equal(far, sector);
    }

    [Fact]
    public void SpeichernLaden_RouteBleibt_HashNurMitSpielerpolitik()
    {
        var u = World();
        var (gate, far) = Neighbour(u);
        ulong npc0 = UniverseHash.Compute(u, true, includePlayerPolitics: false), full0 = UniverseHash.Compute(u);
        PlayerRoutes.Add(u, PlayerRoutes.ForGate(u, gate.Id));
        PlayerRoutes.Add(u, PlayerRoutes.ForPoint(far, new Vec2(12.5f, -7f)));
        PlayerRoutes.SetDock(u, true);
        PlayerRoutes.Advance(u);
        Assert.Equal(npc0, UniverseHash.Compute(u, true, includePlayerPolitics: false));
        Assert.NotEqual(full0, UniverseHash.Compute(u));
        var (v, _) = SaveGameJson.Read(SaveGameJson.Write(u, Now));
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
        var r = PlayerRoutes.Of(v)!;
        Assert.Equal(2, r.Stops.Count);
        Assert.Equal(1, r.Next);
        Assert.True(r.DockAtStations);
        Assert.Equal(new Vec2(12.5f, -7f), r.Stops[1].Position);
        // ohne Route: kein Eintrag im Spielstand
        var w = World();
        Assert.DoesNotContain("\"Route\"", SaveGameJson.Write(w, Now));
    }

    [Fact]
    public void NpcWelt_MitRouteBitgleich()
    {
        var a = World();
        var b = World();
        var (gate, far) = Neighbour(a);
        PlayerRoutes.Add(a, PlayerRoutes.ForGate(a, gate.Id));
        PlayerRoutes.Add(a, PlayerRoutes.ForSector(far));
        var ra = new SimulationRunner(); var rb = new SimulationRunner();
        for (int k = 0; k < 3000; k++) { ra.Step(a); rb.Step(b); }
        Assert.Equal(UniverseHash.Compute(a, true, includePlayerPolitics: false), UniverseHash.Compute(b, true, includePlayerPolitics: false));
        PlayerRoutes.Clear(a);
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(b));
    }
}
