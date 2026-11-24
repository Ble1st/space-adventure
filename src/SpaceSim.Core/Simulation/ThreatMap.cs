using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Lagekarte (Phase 11, FLOTTEN.md Abschnitt 6 „Lagekarte“): je Sektor Bedrohung (<see cref="Sector.Threat"/>), Sicherheit (<see cref="Sector.Security"/>)
/// und Verkehrswert (<see cref="Sector.Traffic"/>). Die Bedrohung steigt durch gemeldete Vorfälle (<see cref="Report"/>), verfällt mit der Zeit und sinkt
/// durch Patrouillen; die Sicherheit ist der Grundschutz der Regierungsstationen des Besitzers plus die Stärke seiner Streifen im Sektor. Händler und
/// Miner lesen daraus ein Routenrisiko (<see cref="RouteRisk"/>), Polizei und Piraten wählen danach ihre Einsatzorte. Die Lagekarte ist die
/// Fraktionssicht des Sektorbesitzers (eine Lage je Sektor statt je Fraktion und Sektor — es gibt je Sektor nur eine zuständige Polizei).
/// </summary>
public static class ThreatMap
{
    /// <summary>Wo ein Schiff jetzt ist: aktive Teilstrecke einer Reise oder sein Parkplatz.</summary>
    public static (SectorId Sector, Vec2 Position) Where(Universe u, Ship s) =>
        s.Transit is { } t ? AccessEnforcement.Where(t, u.Tick) : (s.Sector, s.Position);

    /// <summary>Wert der Fracht an Bord zum Basispreis (Beute- und Verkehrswert).</summary>
    public static float CargoValue(Universe u, Ship s)
    {
        float v = 0f;
        foreach (var (g, n) in s.Cargo.OrderBy(kv => kv.Key.Value)) if (n > 0f && u.Goods.TryGetValue(g, out var good)) v += n * good.BasePrice;
        return v;
    }

    /// <summary>Liegt die Position im Schutzradius einer Station des Sektors (Stationswache, angedockt)?</summary>
    public static bool NearStation(Universe u, SectorId sector, Vec2 pos)
    {
        if (!u.Sectors.TryGetValue(sector, out var sec)) return false;
        foreach (var id in sec.Stations)
            if (u.Stations.TryGetValue(id, out var st) && Vec2.Distance(st.Position, pos) <= st.SafeRadius(u.Rules)) return true;   // Phase 30: nur andockbare, nach Radius
        return false;
    }

    /// <summary>Ist das Schiff ein Polizeischiff (KI, Etikett Polizei, kampffähig)?</summary>
    public static bool IsPolice(Ship s) => s.Role == ShipRole.Police && s.Controller == Controller.Ai;

    /// <summary>Ist das Schiff ein Kaperschiff (KI, Etikett Pirat)?</summary>
    public static bool IsRaider(Ship s) => s.Role == ShipRole.Pirate && s.Controller == Controller.Ai;

    /// <summary>Grundschutz der Stationen einer Fraktion in einem Sektor (Summe <see cref="StationType.Security"/>).</summary>
    public static float BaseSecurity(Universe u, Sector sector, FactionId f)
    {
        float sum = 0f;
        foreach (var id in sector.Stations)
            if (u.Stations.TryGetValue(id, out var st) && !st.IsBuildSite && u.FactionOf(st) == f && !PlayerUpkeep.Unpaid(u, st) && u.StationTypes.TryGetValue(st.TypeKey, out var t))
                sum += MathF.Max(0f, t.Security);
        return sum;
    }

    /// <summary>Ungeglättete Sicherheit und Verkehrswert je Sektor (ein Durchlauf über alle Schiffe, feste Reihenfolge).</summary>
    public static Dictionary<SectorId, (float Security, float Traffic)> Measure(Universe u)
    {
        var result = new Dictionary<SectorId, (float, float)>();
        foreach (var sec in u.Sectors.Values) result[sec.Id] = (BaseSecurity(u, sec, sec.Owner), 0f);
        foreach (var s in u.ShipsById())
        {
            if (s.IsPlayer || s.IsDerelict) continue;   // herrenlose Schiffe sind kein Verkehr
            var (where, _) = Where(u, s);
            if (!result.TryGetValue(where, out var cur)) continue;
            if (IsPolice(s))
            {
                if (u.FactionOf(s) == u.Sectors[where].Owner) cur.Item1 += CombatRules.Strength(s) / MathF.Max(0.01f, u.Rules.PoliceStrengthUnit);
            }
            else if (!IsRaider(s) && s.Cargo.Count > 0) cur.Item2 += CargoValue(u, s);
            result[where] = cur;
        }
        return result;
    }

    /// <summary>Setzt Sicherheit und Verkehr ungeglättet (Weltaufbau, Laden alter Stände).</summary>
    public static void Initialize(Universe u)
    {
        foreach (var (id, (sec, traffic)) in Measure(u))
        {
            u.Sectors[id].Security = sec;
            u.Sectors[id].Traffic = traffic;
        }
    }

    /// <summary>Ein Schritt der Lagekarte: Sicherheit und Verkehr glätten, Bedrohung verfallen lassen und durch Patrouillen senken.</summary>
    public static void Update(Universe u, int dt)
    {
        var r = u.Rules;
        if (!r.SecurityActive) return;   // abgeschaltet: Lagekarte steht still
        float k = Math.Clamp(r.SecuritySmoothing, 0f, 1f);
        foreach (var (id, (sec, traffic)) in Measure(u).OrderBy(kv => kv.Key.Value))
        {
            var s = u.Sectors[id];
            s.Security += (sec - s.Security) * k;
            s.Traffic += (traffic - s.Traffic) * k;
            if (s.Threat > 0f)
            {
                float t = s.Threat * MathF.Max(0f, 1f - r.ThreatDecayPerTick * dt) - r.PatrolSuppressionPerTick * dt * MathF.Max(0f, sec - BaseSecurity(u, s, s.Owner));
                s.Threat = t < 0.001f ? 0f : t;
            }
        }
    }

    /// <summary>Meldet einen Vorfall: Bedrohung des Sektors steigt.</summary>
    public static void Report(Universe u, SectorId sector, float amount)
    {
        if (amount > 0f && u.Sectors.TryGetValue(sector, out var s)) s.Threat += amount;
    }

    /// <summary>
    /// Patrouillenabdeckung einer Fraktion im Sektor (ersetzt die Phase-10-Annahme „Hauptquartier oder Wache“): für den Besitzer die Sicherheit der
    /// Lagekarte (Grundschutz plus Streifen), für andere nur der Grundschutz ihrer Stationen — jeweils mindestens <see cref="EconomyRules.CoverageSecurity"/>.
    /// </summary>
    public static bool HasCoverage(Universe u, Sector sector, FactionId f)
    {
        float sec = f == sector.Owner ? MathF.Max(sector.Security, BaseSecurity(u, sector, f)) : BaseSecurity(u, sector, f);
        return sec >= u.Rules.CoverageSecurity - 1e-4f;
    }

    /// <summary>Erwarteter Verlustanteil des Frachtwerts für einen Aufenthalt im Sektor: Wert × Bedrohung ÷ (1 + Sicherheit), gedeckelt.</summary>
    public static float RiskOf(Universe u, SectorId sector)
    {
        if (!u.Rules.SecurityActive || !u.Sectors.TryGetValue(sector, out var s) || s.Threat <= 0f) return 0f;
        return MathF.Min(u.Rules.MaxSectorRisk, u.Rules.RiskPerThreat * s.Threat / (1f + MathF.Max(0f, s.Security)));
    }

    /// <summary>
    /// Routenrisiko einer beladenen Fahrt (Anteil des Frachtwerts, höchstens 0,5): Summe der Sektorrisiken aller durchflogenen Sektoren (Start, jeder
    /// Sektor hinter einem Gate). Reine Funktion; nutzt den Routen-Cache.
    /// </summary>
    public static float RouteRisk(Universe u, SectorId from, SectorId to)
    {
        if (!u.Rules.SecurityActive) return 0f;
        float risk = RiskOf(u, from);
        if (from != to && u.CachedRoute(from, to) is { } route)
            foreach (var gid in route)
                if (u.Gates.TryGetValue(gid, out var g) && u.Gates.TryGetValue(g.Target, out var far)) risk += RiskOf(u, far.Sector);
        return MathF.Min(0.5f, risk);
    }
}

/// <summary>Führt die Lagekarte fort (Phase 11; reine Beobachtung — sie ändert nur Sektorwerte, keine Schiffe).</summary>
public sealed class ThreatMapSystem : ISimSystem
{
    public void Step(Universe u, int dt) => ThreatMap.Update(u, dt);
}
