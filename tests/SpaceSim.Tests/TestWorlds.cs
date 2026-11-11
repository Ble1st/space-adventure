using SpaceSim.Core.Content;
using SpaceSim.Core.Model;

namespace SpaceSim.Tests;

/// <summary>
/// Phase 14: Die Standardwelt schaltet Treibstoff und Forschung ab (<c>rules.fuelActive</c>/<c>researchActive</c> false). Tests der beiden Systeme bauen
/// ihre Welt hier mit eingeschaltetem Schalter — vor dem Weltaufbau, damit Treibstoffraffinerien und Forschungsstationen in der Welt stehen.
/// </summary>
public static class TestWorlds
{
    /// <summary>Standardwelt mit den angegebenen Schaltern (Standard: beide an, wie bis Phase 13).</summary>
    public static Universe Default(ulong? seed = null, bool fuel = true, bool research = true, string? startOption = null, bool founderQuality = true)
    {
        var def = Def(fuel, research);
        def.Rules!.FounderQuality = founderQuality;
        return WorldBuilder.Build(def, seed, startOption);
    }

    /// <summary>Weltbeschreibung der Standardwelt mit den angegebenen Schaltern.</summary>
    public static WorldDef Def(bool fuel = true, bool research = true)
    {
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        def.Rules ??= new EconomyRules();
        def.Rules.FuelActive = fuel;
        def.Rules.ResearchActive = research;
        return def;
    }

    /// <summary>Standardwelt mit eingeschaltetem Treibstoff (Forschung wie in der Standardwelt aus).</summary>
    public static Universe WithFuel(ulong? seed = null) => Default(seed, fuel: true, research: false);

    /// <summary>Standardwelt mit eingeschalteter Forschung (Treibstoff wie in der Standardwelt aus).</summary>
    public static Universe WithResearch(ulong? seed = null) => Default(seed, fuel: false, research: true);
}
