using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Phase 14: Schalter für ganze Spielsysteme (<see cref="EconomyRules.FuelActive"/>, <see cref="EconomyRules.ResearchActive"/>; Phase 23:
/// <see cref="EconomyRules.AmmoActive"/>). Code und Tests der
/// Systeme bleiben erhalten; abgeschaltet verbraucht niemand Treibstoff bzw. forscht niemand, und die Weltdatei lässt die Stationen weg, die nur für
/// das abgeschaltete System da sind (Treibstoffraffinerien, Forschungsstationen).
/// </summary>
public static class FeatureSwitches
{
    /// <summary>Stellt der Stationstyp nur Treibstoff her (alle Rezeptausgänge sind Waren, die ein Antriebsmodul als Treibstoff nennt)?</summary>
    public static bool IsFuelSupply(Universe u, StationType type)
    {
        if (type.Recipes.Count == 0) return false;
        bool any = false;
        foreach (var r in type.Recipes)
            foreach (var g in r.Outputs.Keys)
            {
                if (!IsFuel(u, g)) return false;
                any = true;
            }
        return any;
    }

    /// <summary>Nennt irgendein Antriebsmodul diese Ware als Treibstoff?</summary>
    public static bool IsFuel(Universe u, GoodId good)
    {
        foreach (var m in u.Modules.Values)
            foreach (var f in m.FuelTypes)
                if (f.Good == good) return true;
        return false;
    }

    /// <summary>Phase 23: Warenkategorie der Munition (<see cref="EconomyRules.AmmoActive"/>).</summary>
    public const string AmmoCategory = "ammo";

    /// <summary>Phase 23: Ist die Ware Munition (Kategorie „ammo“)?</summary>
    public static bool IsAmmo(Universe u, GoodId good) => u.Goods.TryGetValue(good, out var g) && g.Category == AmmoCategory;

    /// <summary>
    /// Phase 23: Lässt die Weltdatei ein Rezept weg? Mit abgeschalteter Munition fallen Rezepte, die Munition herstellen, weg (die Magazine gelten dann
    /// als voll). Gilt nur beim Weltaufbau.
    /// </summary>
    public static bool OmitRecipe(Universe u, Recipe recipe)
    {
        if (u.Rules.AmmoActive) return false;
        foreach (var g in recipe.Outputs.Keys) if (IsAmmo(u, g)) return true;
        return false;
    }

    /// <summary>Phase 23: Lässt die Weltdatei einen Lagerposten (stocks) weg? Mit abgeschalteter Munition alle Munitionsposten.</summary>
    public static bool OmitStock(Universe u, GoodId good) => !u.Rules.AmmoActive && IsAmmo(u, good);

    /// <summary>
    /// Lässt die Weltdatei Stationen dieses Typs bei den aktuellen Schaltern weg? Forschungsstationen ohne Forschung, reine Treibstoffhersteller
    /// ohne Treibstoff. Gilt nur beim Weltaufbau; ein geladener Spielstand behält, was er hat.
    /// </summary>
    public static bool OmitFromWorld(Universe u, StationType type) =>
        (!u.Rules.ResearchActive && type.Research is not null) || (!u.Rules.FuelActive && IsFuelSupply(u, type));
}
