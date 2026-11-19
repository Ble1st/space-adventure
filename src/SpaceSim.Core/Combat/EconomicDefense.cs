using System.Runtime.CompilerServices;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Combat;

/// <summary>
/// Ausgleich nach dem Spielerbot-Test (docs/KAMPF.md „Ausgleich: Wirtschaftsstationen wehren sich“, Punkt 4): Ein Zerstörer (≈ 7 900 Cr) nahm geschützlose
/// Wirtschaftsstationen in 70–300 Ticks ohne Gegenwehr. Mit Regel <see cref="EconomyRules.EconomicStationDefense"/> bekommen Wirtschaftsstationen der Größe
/// M, L und XL ohne eigenen <c>defense</c>-Abschnitt eine Vorgabeverteidigung: Hülle = Grundhülle × <see cref="EconomyRules.EconomicStationHullFactor"/>,
/// kleiner Schild, etwas Panzerung und 1/2/2 leichte Geschütze. Regierungsstationen, Verstecke, Kleinstationen (eigene Werte) und S-Stationen bleiben unverändert.
/// Die Geschütze feuern (Vorgabe) nur auf Angreifer der Station selbst — Überfälle in der Nähe bleiben, wie sie waren.
/// </summary>
public static class EconomicDefense
{
    sealed class Cached { public (float, float, float, float, float, string, int, float) Key; public StationDefenseConfig? Config; }
    static readonly ConditionalWeakTable<StationType, Cached> Cache = new();

    /// <summary>Vorgabeverteidigung des Typs (null = keine: Regel aus, Typ mit eigener Verteidigung, keine Wirtschaftsstation oder Größe S/XS).</summary>
    public static StationDefenseConfig? For(Universe u, StationType t)
    {
        var r = u.Rules;
        if (!r.EconomicStationDefense || t.Defense is not null || t.Government != GovernmentKind.None || t.Hideout
            || t.Size is not (StationSize.M or StationSize.L or StationSize.XL)) return null;
        int idx = t.Size switch { StationSize.M => 0, StationSize.L => 1, _ => 2 };
        int guns = r.EconomicStationGuns.Count == 0 ? 0 : r.EconomicStationGuns[Math.Min(idx, r.EconomicStationGuns.Count - 1)];
        guns = Math.Clamp(guns, 0, StationSizes.MaxGuns(t.Size));   // Wirtschaftsstationen halten stand: höchstens die Stellplätze der Größe
        float pd = r.EconomicStationPointDefense.Count == 0 ? 0f : MathF.Max(0f, r.EconomicStationPointDefense[Math.Min(idx, r.EconomicStationPointDefense.Count - 1)]);
        float baseHull = t.Size == StationSize.M ? r.StationBaseHull : StationSizes.BaseHull(t.Size);
        var key = (baseHull, r.EconomicStationHullFactor, r.EconomicStationShieldShare, r.EconomicStationShieldRegenShare, r.EconomicStationArmor, r.EconomicStationGun + "|" + string.Join(",", r.EconomicStationGunList), guns, pd);
        var c = Cache.GetValue(t, _ => new Cached());
        if (c.Config is not null && c.Key == key) return c.Config;
        float shield = baseHull * MathF.Max(0f, r.EconomicStationShieldShare);
        var cfg = new StationDefenseConfig
        {
            Hull = baseHull * MathF.Max(1f, r.EconomicStationHullFactor),
            Shield = shield,
            ShieldRegen = shield * MathF.Max(0f, r.EconomicStationShieldRegenShare),
            Armor = MathF.Max(0f, r.EconomicStationArmor),
            PointDefense = pd,   // Wirtschaftsstationen halten stand: L/XL gegen Raketen
        };
        if (r.EconomicStationGunList.Count > 0)   // Wirtschaftsstationen halten stand: Geschützmischung (erste n Einträge)
        { for (int i = 0; i < guns && i < r.EconomicStationGunList.Count; i++) if (u.Modules.ContainsKey(r.EconomicStationGunList[i])) cfg.Guns.Add(r.EconomicStationGunList[i]); }
        else if (u.Modules.ContainsKey(r.EconomicStationGun)) for (int i = 0; i < guns; i++) cfg.Guns.Add(r.EconomicStationGun);
        c.Key = key; c.Config = cfg;
        return cfg;
    }
}
