using SpaceSim.Core.Model;

namespace SpaceSim.Core.Content;

/// <summary>
/// Module sind Waren (Phase 5a, SCHIFFSSLOTS.md „Module sind Waren“): Zu jedem Modul gibt es eine Ware gleichen Schlüssels.
/// Reine Regeln für ihre Eigenschaften, gemeinsam genutzt vom <see cref="WorldBuilder"/> und der Save-Migration v5→v6.
/// </summary>
public static class ModuleGoods
{
    /// <summary>Farbe einer Modul-Ware, wenn die Slotart keine nennt.</summary>
    public const string DefaultColor = "#b0b8c8";

    /// <summary>Basispreis = Summe (Menge × Basispreis der Zutat) × Aufschlag, gerundet, mindestens 1. Unbekannte Zutaten zählen 0.</summary>
    public static float BasePrice(IReadOnlyDictionary<string, float> cost, Func<string, float?> basePriceOf, float markup)
    {
        float sum = 0f;
        foreach (var (key, qty) in cost.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            sum += qty * (basePriceOf(key) ?? 0f);
        return MathF.Max(1f, MathF.Round(sum * markup));
    }

    /// <summary>Volumen je Einheit aus der Modulmasse (mindestens 1).</summary>
    public static float Volume(float mass) => MathF.Max(1f, mass);

    /// <summary>Feste Reihenfolge der Modul-Waren hinter den JSON-Waren: nach Modulschlüssel (ordinal).</summary>
    public static IEnumerable<T> InGoodOrder<T>(IEnumerable<T> modules, Func<T, string> key) =>
        modules.OrderBy(key, StringComparer.Ordinal);

    /// <summary>Legt die Ware eines Moduls an (form solid, Name = Modulname, Farbe nach Slotart).</summary>
    /// <summary>Phase 14: Kategorie der Modul-Waren.</summary>
    public const string ModuleCategory = "module";

    public static Good Create(Universe u, Module m, GoodId id)
    {
        var slot = u.SlotTypes.GetValueOrDefault(m.Slot);
        return new Good
        {
            Id = id, Key = m.Key, Name = m.Name.Length > 0 ? m.Name : m.Key,
            Color = slot?.Color ?? DefaultColor,
            BasePrice = BasePrice(m.Cost, k => u.FindGood(k)?.BasePrice, u.Rules.ModuleMarkup),
            VolumePerUnit = Volume(m.Mass), Form = GoodForm.Solid, Category = ModuleCategory,
        };
    }

    /// <summary>Ist die Ware eine Modul-Ware (gleicher Schlüssel wie ein Modul)?</summary>
    public static bool IsModuleGood(Universe u, Good g) => g.Key.Length > 0 && u.Modules.ContainsKey(g.Key);
}
