using SpaceSim.Core.Combat;
using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>Sonnen (ERKUNDUNG.md „Sonnen“): Daten, Hindernis, Hitze in der Atmosphäre, Hüllenschaden in der Korona, Speichern.</summary>
public class SonnenTests
{
    /// <summary>Referenzhash-Tests: Schalter aus — WorldBuilder lässt die Sonnen weg.</summary>
    internal static void RevertSuns(WorldDef def) => (def.Rules ??= new EconomyRules()).Suns = false;

    static Universe World(bool suns = true, bool heat = true)
    {
        var def = TestWorlds.Def(fuel: false, research: false);
        def.Rules!.Suns = suns;
        def.Rules.ShipHeat = heat;
        return WorldBuilder.Build(def);
    }

    /// <summary>Punkt im Abstand <paramref name="factor"/> × Radius von der Sonne, auf der Seite zur Sektormitte.</summary>
    static Vec2 At(Sun sun, float factor)
    {
        var dir = Vec2.Distance(sun.Position, default) < 1e-3f ? new Vec2(1f, 0f) : (default(Vec2) - sun.Position) * (1f / Vec2.Distance(sun.Position, default));
        return sun.Position + dir * (sun.Radius * factor);
    }

    static Ship PlayerIn(Universe u, SectorId sector, Vec2 pos)
    {
        var me = u.Ships[u.PlayerShip];
        me.DockedAt = default;
        if (me.Sector != sector) u.MoveShip(me, sector);
        u.PlayerSector = sector;
        me.Position = pos;
        return me;
    }

    [Fact]
    public void Schalter_CodeAus_StandardweltAn_AusOhneSonnen()
    {
        Assert.False(new EconomyRules().Suns);
        var on = World();
        Assert.True(on.Rules.Suns);
        Assert.All(on.Sectors.Values, s => Assert.NotNull(s.Sun));
        var off = World(suns: false);
        Assert.All(off.Sectors.Values, s => Assert.Null(s.Sun));
        // aus = Welt ohne Sonnendaten (Hash nach 1500 Takten gleich einer Welt, deren Datei keine Sonnen hat)
        var def = TestWorlds.Def(fuel: false, research: false);
        foreach (var sd in def.Sectors) sd.Sun = null;
        def.Rules!.Suns = true;
        var b = WorldBuilder.Build(def);
        new SimulationRunner().Run(off, 1500);
        new SimulationRunner().Run(b, 1500);
        Assert.Equal(UniverseHash.Compute(off), UniverseHash.Compute(b));
    }

    [Fact]
    public void Standardwelt_SonnenWeitAusserhalbDerInhalte_NichtsInDerAtmosphaere()
    {
        var u = World();
        foreach (var sec in u.Sectors.Values)
        {
            var sun = sec.Sun!;
            float atm = Suns.AtmosphereRadius(u.Rules, sun);
            Assert.True(Suns.ValidColor(sun.Color));
            foreach (var id in sec.Stations) Assert.True(Vec2.Distance(u.Stations[id].Position, sun.Position) > atm + u.Stations[id].Radius, $"{sec.Name}: {u.Stations[id].Name}");
            foreach (var gid in sec.Gates) Assert.True(Vec2.Distance(u.Gates[gid].Position, sun.Position) > atm, $"{sec.Name}: Tor {gid.Value}");
            foreach (var a in sec.Asteroids) Assert.True(Vec2.Distance(a.Position, sun.Position) > atm, $"{sec.Name}: Fels {a.Id}");
            // die Sonne liegt außerhalb der Platzierungsfläche — Bauplätze an ihr sind gesperrt
            Assert.False(StationPlacement.Check(u, sec, At(sun, 1.8f), 10f).Ok);
            Assert.True(Suns.TooClose(u, sec, At(sun, 2f), 10f));
        }
    }

    [Fact]
    public void Hindernis_WegplanungMeidetKorona_KollisionNurDerKoerper()
    {
        var u = World();
        var sec = u.Sectors[new SectorId(1)];
        var sun = sec.Sun!;
        var obs = u.GetObstacles(sec.Id);
        float avoid = Suns.AvoidRadius(u.Rules, sun);
        Assert.Contains(obs, o => o.Position == sun.Position && MathF.Abs(o.Radius - avoid) < 1e-3f);
        var col = Suns.CollisionObstacles(u, sec.Id, u.GetObstacles(sec.Id));
        Assert.Contains(col, o => o.Position == sun.Position && MathF.Abs(o.Radius - sun.Radius) < 1e-3f);
        Assert.DoesNotContain(col, o => o.Position == sun.Position && MathF.Abs(o.Radius - avoid) < 1e-3f);
        // Strecke quer durch die Sonne: NPC-Weg und Spielerautopilot bleiben außerhalb der Korona
        var tang = new Vec2(-(sun.Position.Y), sun.Position.X) * (1f / sun.Position.Length);
        var a = sun.Position + tang * (avoid * 2f);
        var b = sun.Position - tang * (avoid * 2f);
        float cor = Suns.CoronaRadius(u.Rules, sun);
        foreach (var path in new[] { Travel.SectorPathUncached(u, sec.Id, a, b), PathPlanner.Plan(a, b, Navigator.PlayerObstacles(u, sec.Id, a, null)) })
        {
            Assert.True(path.Count >= 3, "Umweg um die Sonne");
            for (int i = 0; i + 1 < path.Count; i++)
                Assert.True(SegDist(sun.Position, path[i], path[i + 1]) >= cor, $"Teilstrecke {i} schneidet die Korona");
        }
    }

    static float SegDist(Vec2 p, Vec2 a, Vec2 b)
    {
        var ab = b - a;
        float l2 = ab.X * ab.X + ab.Y * ab.Y;
        float t = l2 <= 0f ? 0f : Math.Clamp(((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / l2, 0f, 1f);
        return Vec2.Distance(p, a + ab * t);
    }

    [Fact]
    public void Atmosphaere_HitzeSteigtNachInnen_HeatSystemBuchtSie()
    {
        var u = World();
        var sec = u.Sectors[new SectorId(1)];
        var sun = sec.Sun!;
        Assert.Equal(0f, Suns.HeatRateAt(u, sec.Id, At(sun, 2.4f)));
        float outer = Suns.HeatRateAt(u, sec.Id, At(sun, 2.0f)), inner = Suns.HeatRateAt(u, sec.Id, At(sun, 1.5f)), corona = Suns.HeatRateAt(u, sec.Id, At(sun, 1.1f));
        Assert.True(outer > 0f && inner > outer && corona > inner, $"{outer} {inner} {corona}");
        Assert.True(corona <= 2f * u.Rules.SunHeatMax + 1e-3f);
        var me = PlayerIn(u, sec.Id, At(sun, 1.5f));
        Assert.Equal(inner, Heat.Environment(u, me), 3);
        Assert.Equal(SunZone.Atmosphere, Suns.ZoneAt(u, sec.Id, me.Position));
        Assert.StartsWith("Sonnenatmosphäre", Suns.Warning(u, me));
        float hull = me.Hull;
        var runner = new SimulationRunner();
        for (int i = 0; i < 12; i++) { me.Position = At(sun, 1.5f); runner.Step(u); }
        Assert.True(me.Systems.Heat > 0f, "Hitze steigt in der Atmosphäre");
        Assert.Equal(hull, me.Hull);   // Atmosphäre allein schadet der Hülle nicht (erst Überhitzung über die Hitzeregel)
        // Schalter aus: keine Hitze aus der Umgebung
        u.Rules.Suns = false;
        Assert.Equal(0f, Heat.Environment(u, me));
        Assert.Equal("", Suns.Warning(u, me));
    }

    [Fact]
    public void Korona_HuelleLeidet_NieUnterEins_Ereignis_AngedocktNicht()
    {
        var u = World(heat: false);
        var sec = u.Sectors[new SectorId(1)];
        var sun = sec.Sun!;
        var me = PlayerIn(u, sec.Id, At(sun, 1.15f));
        Assert.Equal(SunZone.Corona, Suns.ZoneAt(u, sec.Id, me.Position));
        Assert.StartsWith("SONNENKORONA", Suns.Warning(u, me));
        var events = new List<SimEvent>();
        u.Events.Raised += events.Add;
        float before = me.Hull;
        var runner = new SimulationRunner();
        for (int i = 0; i < 40; i++) { me.Position = At(sun, 1.15f); runner.Step(u); }
        Assert.True(me.Hull < before, "Korona nimmt Hülle");
        Assert.Contains(events, e => e is SunScorched ss && ss.Ship == me.Id && ss.Hull > 0f);
        float expected = before - me.MaxHull * Suns.HullShareAt(u, sec.Id, me.Position) * 10f;   // 40 Takte = 10 s
        Assert.Equal(expected, me.Hull, 1);
        for (int i = 0; i < 4000; i++) { me.Position = At(sun, 1.05f); runner.Step(u); }
        Assert.Equal(1f, me.Hull);
        // angedockt: kein Schaden (das Schiff steht an einer Station)
        me.Hull = 50f;
        me.DockedAt = sec.Stations[0];
        for (int i = 0; i < 40; i++) runner.Step(u);
        Assert.Equal(50f, me.Hull);
    }

    [Fact]
    public void Weltpruefung_StationInDerAtmosphaere_FarbeUndRadius()
    {
        var def = TestWorlds.Def(fuel: false, research: false);
        var sd = def.Sectors.First(s => s.Id == 1);
        var st = sd.Stations.First();
        sd.Sun = new SunDef { Pos = new[] { st.Pos[0] + 20f / sd.Scale, st.Pos[1] }, Radius = 20, Color = "#ffffff" };
        var ex = Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(def));
        Assert.Contains("Sonnenatmosphäre", ex.Message);
        def = TestWorlds.Def(fuel: false, research: false);
        def.Sectors.First(s => s.Id == 1).Sun!.Color = "gelb";
        Assert.Contains("Sonnenfarbe", Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(def)).Message);
        def = TestWorlds.Def(fuel: false, research: false);
        def.Sectors.First(s => s.Id == 1).Sun!.Radius = 0;
        Assert.Contains("radius", Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(def)).Message);
        // Schalter aus: ungültige Sonnen stören nicht (sie kommen gar nicht in die Welt)
        def.Rules!.Suns = false;
        WorldBuilder.Build(def);
    }

    [Fact]
    public void Speichern_SonneBitgleich_HashNurMitSonne()
    {
        var u = World();
        var me = PlayerIn(u, new SectorId(1), At(u.Sectors[new SectorId(1)].Sun!, 1.6f));
        new SimulationRunner().Run(u, 20);
        var json = SaveGameJson.Write(u, DateTimeOffset.UnixEpoch);
        Assert.Contains("\"Sun\"", json);
        var (v, _) = SaveGameJson.Read(json);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
        Assert.Equal(u.Sectors[new SectorId(3)].Sun!.Position, v.Sectors[new SectorId(3)].Sun!.Position);
        new SimulationRunner().Run(u, 300);
        new SimulationRunner().Run(v, 300);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
        // Sonnendaten gehen in den Hash ein
        ulong h = UniverseHash.Compute(u);
        u.Sectors[new SectorId(2)].Sun!.Radius += 1f;
        Assert.NotEqual(h, UniverseHash.Compute(u));
        _ = me;
    }

    [Fact]
    public void StandardweltMitSonnen_NpcWirtschaftUnveraendert_3000Takte()
    {
        // Die Sonnen liegen außerhalb der Inhalte: die NPC-Welt läuft wie ohne sie (nur die Sonnendaten mischen in den Hash).
        var a = World(suns: true);
        var b = World(suns: false);
        new SimulationRunner().Run(a, 3000);
        new SimulationRunner().Run(b, 3000);
        foreach (var s in a.Sectors.Values) s.Sun = null;
        Assert.Equal(UniverseHash.Compute(b), UniverseHash.Compute(a));
    }
}
