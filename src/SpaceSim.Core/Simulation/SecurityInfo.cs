using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>Lage eines Sektors für die Anzeige (Universumskarte, Sektoransicht, Station-Info).</summary>
public readonly record struct SectorSecurityView(
    float Threat, string ThreatText, float Security, string SecurityText, int Patrols, float RiskPercent,
    int PlayerLevel, string PlayerWanted, string Summary, int Fights = 0);

/// <summary>Fahndungseintrag des Spielers bei einer Fraktion (Anzeige Kommandozentrale, HUD, Gericht).</summary>
public readonly record struct WantedView(FactionId Faction, string FactionName, int Level, string LevelText, float Heat, float Fine, string Crime, float BountyClaim);

/// <summary>
/// Reine Anzeige-Funktionen der Phase 11 (Lagekarte, Fahndung, Kopfgeld) über Core-Daten; die Oberfläche erfindet keine Werte.
/// </summary>
public static class SecurityInfo
{
    /// <summary>Lage eines Sektors: Bedrohung, Sicherheit, Streifen des Besitzers im Sektor, Routenrisiko und die Fahndung des Spielers dort.</summary>
    public static SectorSecurityView ForSector(Universe u, SectorId id, Ship? player)
    {
        if (!u.Sectors.TryGetValue(id, out var s)) return new SectorSecurityView(0f, "", 0f, "", 0, 0f, 0, "", "");
        int patrols = PirateSystem.PoliceIn(u, id).Count;
        float risk = 100f * ThreatMap.RiskOf(u, id);
        int level = player is null ? 0 : Wanted.LevelOf(u, player.Owner, s.Owner);
        string wanted = level > 0 ? SecurityNames.WantedLevel(level) : "sauber";
        int fights = Combat.CombatInfo.CountIn(u, id);   // Phase 24: laufende Gefechte
        string summary = $"Sicherheit {SecurityNames.Security(s.Security)} · Bedrohung {SecurityNames.Threat(s.Threat)}"
            + (fights > 0 ? $" · {fights} Gefecht{(fights == 1 ? "" : "e")}" : "");
        return new SectorSecurityView(s.Threat, SecurityNames.Threat(s.Threat), s.Security, SecurityNames.Security(s.Security), patrols, risk, level, wanted, summary, fights);
    }

    /// <summary>Fahndung und offenes Kopfgeld des Spielers je Fraktion (Fraktionen mit Eintrag oder Kopfgeld, nach Id).</summary>
    public static List<WantedView> ForPlayer(Universe u)
    {
        var list = new List<WantedView>();
        var p = u.Player;
        foreach (var f in p.Wanted.Keys.Concat(p.BountyClaims.Keys).Distinct().OrderBy(k => k.Value))
        {
            string name = u.Factions.TryGetValue(f, out var fac) ? fac.Name : $"Fraktion {f.Value}";
            p.Wanted.TryGetValue(f, out var w);
            int level = w is null ? 0 : Wanted.Level(u.Rules, w.Heat);
            list.Add(new WantedView(f, name, level, SecurityNames.WantedLevel(level), w?.Heat ?? 0f, w is null ? 0f : Wanted.FineOf(u, w),
                SecurityNames.Crime(w?.LastCrime ?? ""), p.BountyClaims.GetValueOrDefault(f)));
        }
        return list;
    }

    /// <summary>Kurzzeile für das Flug-HUD: höchste Fahndungsstufe mit Fraktion, null = sauber.</summary>
    public static string? HudLine(Universe u)
    {
        var worst = ForPlayer(u).Where(w => w.Level > 0).OrderByDescending(w => w.Level).ThenBy(w => w.Faction.Value).FirstOrDefault();
        return worst.Level > 0 ? $"Fahndung: {worst.LevelText} ({worst.FactionName})" : null;
    }
}
