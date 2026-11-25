using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>Zone einer Position um die Sonne des Sektors (von außen nach innen).</summary>
public enum SunZone { None, Atmosphere, Corona, Body }

/// <summary>
/// Sonnen (ERKUNDUNG.md „Sonnen“), reine Regeln. Alles hinter <see cref="EconomyRules.Suns"/> (aus = keine Sonne zählt, auch wenn ein Sektor eine hat).
/// <list type="bullet">
/// <item>Körper: Hindernis. Die Wegplanung (NPC über <see cref="Universe.GetObstacles"/>, Spielerautopilot) meidet den Kreis bis
/// <see cref="EconomyRules.SunAvoidFactor"/> × Radius; die Kollision des Spielerschiffs (Ansicht) nur den Körper (<see cref="CollisionObstacles"/>).</item>
/// <item>Atmosphäre bis <see cref="EconomyRules.SunAtmosphereFactor"/> × Radius: Hitze über <see cref="Combat.Heat.Environment"/> (linear bis
/// <see cref="EconomyRules.SunHeatMax"/> am Koronarand, in der Korona bis × 2).</item>
/// <item>Korona bis <see cref="EconomyRules.SunCoronaFactor"/> × Radius: Hüllenschaden (<see cref="SunSystem"/>, nie unter 1 Hülle), unabhängig von der Hitzeregel.</item>
/// </list>
/// Kein Zufall.
/// </summary>
public static class Suns
{
    /// <summary>Sonne des Sektors, wenn die Regel an ist und er eine hat.</summary>
    public static Sun? Of(Universe u, SectorId sector) =>
        u.Rules.Suns && u.Sectors.TryGetValue(sector, out var s) ? s.Sun : null;

    public static float AtmosphereRadius(EconomyRules r, Sun s) => s.Radius * MathF.Max(1f, r.SunAtmosphereFactor);
    public static float CoronaRadius(EconomyRules r, Sun s) => s.Radius * Math.Clamp(r.SunCoronaFactor, 1f, MathF.Max(1f, r.SunAtmosphereFactor));
    public static float AvoidRadius(EconomyRules r, Sun s) => s.Radius * MathF.Max(1f, r.SunAvoidFactor);

    /// <summary>Zone einer Position.</summary>
    public static SunZone ZoneAt(Universe u, SectorId sector, Vec2 pos)
    {
        if (Of(u, sector) is not { } sun) return SunZone.None;
        float d = Vec2.Distance(pos, sun.Position);
        if (d < sun.Radius) return SunZone.Body;
        if (d < CoronaRadius(u.Rules, sun)) return SunZone.Corona;
        if (d < AtmosphereRadius(u.Rules, sun)) return SunZone.Atmosphere;
        return SunZone.None;
    }

    /// <summary>Wo ein Schiff jetzt ist (Reise: aktive Teilstrecke).</summary>
    static (SectorId Sector, Vec2 Pos) Where(Universe u, Ship ship) => ThreatMap.Where(u, ship);

    /// <summary>Tiefe in der Korona 0 (Rand) … 1 (Oberfläche).</summary>
    static float CoronaDepth(EconomyRules r, Sun s, float d)
    {
        float c = CoronaRadius(r, s);
        return c - s.Radius <= 1e-3f ? 1f : Math.Clamp((c - d) / (c - s.Radius), 0f, 1f);
    }

    /// <summary>Hitze je Sekunde an einer Position (0 außerhalb der Atmosphäre).</summary>
    public static float HeatRateAt(Universe u, SectorId sector, Vec2 pos)
    {
        if (Of(u, sector) is not { } sun) return 0f;
        var r = u.Rules;
        float d = Vec2.Distance(pos, sun.Position), atm = AtmosphereRadius(r, sun), cor = CoronaRadius(r, sun);
        if (d >= atm) return 0f;
        if (d >= cor) return atm - cor <= 1e-3f ? r.SunHeatMax : MathF.Max(0f, r.SunHeatMax * (atm - d) / (atm - cor));
        return MathF.Max(0f, r.SunHeatMax * (1f + CoronaDepth(r, sun, d)));
    }

    /// <summary>Hitze je Sekunde für ein Schiff (Haken <see cref="Combat.Heat.Environment"/>; angedockte Schiffe stehen nie in der Atmosphäre).</summary>
    public static float HeatRate(Universe u, Ship ship)
    {
        if (!u.Rules.Suns) return 0f;
        var (sec, pos) = Where(u, ship);
        return HeatRateAt(u, sec, pos);
    }

    /// <summary>Hüllenschaden als Anteil der Höchsthülle je Sekunde an einer Position (0 außerhalb der Korona).</summary>
    public static float HullShareAt(Universe u, SectorId sector, Vec2 pos)
    {
        if (Of(u, sector) is not { } sun) return 0f;
        float d = Vec2.Distance(pos, sun.Position);
        if (d >= CoronaRadius(u.Rules, sun)) return 0f;
        return MathF.Max(0f, u.Rules.SunHullPerSecond * (1f + CoronaDepth(u.Rules, sun, d)));
    }

    /// <summary>Hindernis der Wegplanung (null ohne Sonne).</summary>
    public static Obstacle? AvoidObstacle(Universe u, Sector sector) =>
        u.Rules.Suns && sector.Sun is { } sun ? new Obstacle(sun.Position, AvoidRadius(u.Rules, sun)) : null;

    /// <summary>
    /// Hindernisliste für die Kollision des Spielerschiffs: wie <see cref="Universe.GetObstacles"/>, aber die Sonne nur mit ihrem Körper (die Wegplanung
    /// hält Abstand, von Hand kann man bis an die Oberfläche fliegen). Ändert die übergebene Liste.
    /// </summary>
    public static List<Obstacle> CollisionObstacles(Universe u, SectorId sector, List<Obstacle> list)
    {
        if (Of(u, sector) is not { } sun) return list;
        float avoid = AvoidRadius(u.Rules, sun);
        for (int i = list.Count - 1; i >= 0; i--)
            if (list[i].Position == sun.Position && MathF.Abs(list[i].Radius - avoid) < 1e-3f) { list[i] = new Obstacle(sun.Position, sun.Radius); break; }
        return list;
    }

    /// <summary>Liegt ein Platz für ein Objekt mit Radius <paramref name="radius"/> zu nah an der Sonne (Atmosphäre + Abstand)?</summary>
    public static bool TooClose(Universe u, Sector sector, Vec2 p, float radius) =>
        u.Rules.Suns && sector.Sun is { } sun && Vec2.Distance(p, sun.Position) < AtmosphereRadius(u.Rules, sun) + radius + u.Rules.SunClearance;

    /// <summary>Warnzeile für das Flug-HUD (leer = keine Warnung).</summary>
    /// <summary>Kurze Warnzeile für die schmale Statusspalte des Flug-HUD.</summary>
    public static string WarningShort(Universe u, Ship ship)
    {
        if (!u.Rules.Suns) return "";
        var (sec, pos) = Where(u, ship);
        var zone = ZoneAt(u, sec, pos);
        if (zone == SunZone.None) return "";
        if (zone is SunZone.Corona or SunZone.Body) return $"SONNENKORONA! Hülle −{HullShareAt(u, sec, pos) * 100f:0.#} %/s";
        return $"Sonnenatmosphäre: +{HeatRateAt(u, sec, pos):0}/s Hitze";
    }

    public static string Warning(Universe u, Ship ship)
    {
        if (!u.Rules.Suns) return "";
        var (sec, pos) = Where(u, ship);
        var zone = ZoneAt(u, sec, pos);
        if (zone == SunZone.None) return "";
        float heat = HeatRateAt(u, sec, pos);
        if (zone is SunZone.Corona or SunZone.Body)
            return $"SONNENKORONA! Hülle −{HullShareAt(u, sec, pos) * 100f:0.#} %/s · Hitze +{heat:0}/s — sofort abdrehen";
        return $"Sonnenatmosphäre: Hitze +{heat:0.#}/s — Abstand halten";
    }

    /// <summary>Anzeige der Sternart (Sektorkarte, Karte).</summary>
    public static string KindName(string kind) => kind switch
    {
        "red" => "Roter Riese",
        "blue" => "Blauer Stern",
        "white" => "Weißer Stern",
        "orange" => "Orangefarbener Stern",
        _ => "Gelber Stern",
    };

    /// <summary>Gültige Farbe #RRGGBB?</summary>
    public static bool ValidColor(string c) =>
        c.Length == 7 && c[0] == '#' && c.Skip(1).All(ch => ch is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F'));
}

/// <summary>Sonnen: Hüllenschaden in der Korona (feste Id-Reihenfolge, skaliert mit dt, nie unter 1 Hülle). Die Hitze bucht <see cref="Combat.HeatSystem"/>.</summary>
public sealed class SunSystem : ISimSystem
{
    public const int Interval = 4;

    public void Step(Universe u, int dt)
    {
        if (!u.Rules.Suns) return;
        float seconds = dt / (float)SimClock.TicksPerSecond;
        foreach (var ship in u.ShipsById())
        {
            if (ship.DockedAt.Value != 0 || ship.Hull <= 1f) continue;
            var (sec, pos) = ThreatMap.Where(u, ship);
            float share = Suns.HullShareAt(u, sec, pos);
            if (share <= 0f) continue;
            float before = ship.Hull;
            ship.Hull = MathF.Max(1f, ship.Hull - ship.MaxHull * share * seconds);
            u.Events.Publish(new SunScorched(ship.Id, sec, before - ship.Hull));
        }
    }
}
