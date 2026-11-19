using SpaceSim.Core.Content;
using SpaceSim.Core.Model;

namespace SpaceSim.Tests;

/// <summary>
/// Phase 14 schaltete Treibstoff und Forschung in der Standardwelt ab; seit „Treibstoff und Forschung dauerhaft an“ sind beide in der Standardwelt wieder an
/// (<c>rules.fuelActive</c>/<c>researchActive</c> true). Tests, die einen Schalter aus brauchen, bauen ihre Welt hier — vor dem Weltaufbau, denn mit dem
/// Schalter aus fehlen Treibstoffraffinerien und Forschungsstationen in der Welt.
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
        def.RandomNames = false;   // Phase 29: Tests behalten die festen Fraktionsnamen der Weltdatei (gewürfelte Namen: Phase29NamesTests)
        def.Rules.PlayerIntel = false;   // Ausbau Punkt 16: ältere Tests setzen volles Wissen voraus (Erkundung: ErkundungTests mit Regel an)
        return def;
    }

    // Runde 44 hatte hier „FuelBaseScale“ (Standardwelt im Grundmaßstab für die Treibstoff-Langläufe). Seit der Neukalibrierung des Treibstoffs für den
    // Großmaßstab (fuelUse halbiert, fünf Raffinerien, siehe docs/SCHIFFSSLOTS.md) laufen diese Tests wieder in der Standardwelt; der Helfer ist entfallen.

    /// <summary>
    /// Referenz-Hash-Tests: die Standardwelt wie vor „Treibstoff und Forschung dauerhaft an“ (beide Schalter aus — die Weltdatei war sonst gleich).
    /// Teil der Rücknahmekette (<c>SprungTarnTests.RevertSprungTarn</c> → <c>Runde44ScaleTests.RevertRunde44</c> → <c>Phase29Tests.RevertRunde24</c> …).
    /// </summary>
    public static void RevertFuelResearchOn(WorldDef def)
    {
        def.Rules ??= new EconomyRules();
        def.Rules.FuelActive = false;
        def.Rules.ResearchActive = false;
    }

    /// <summary>Standardwelt mit den beiden Schaltern aus (Stand vor „Treibstoff und Forschung dauerhaft an“).</summary>
    public static Universe DefaultOff(ulong? seed = null, string? startOption = null) => Default(seed, fuel: false, research: false, startOption: startOption);

    /// <summary>Standardwelt mit eingeschaltetem Treibstoff, Forschung aus.</summary>
    public static Universe WithFuel(ulong? seed = null) => Default(seed, fuel: true, research: false);

    /// <summary>Standardwelt mit eingeschalteter Forschung, Treibstoff aus.</summary>
    public static Universe WithResearch(ulong? seed = null) => Default(seed, fuel: false, research: true);

    /// <summary>
    /// Phase 21: die bisherige 4-Sektoren-Standardwelt (Alpha, Beta, Gamma, Delta; Stand Phase 20) als eingebettete Test-Ressource. Tests, die eine
    /// kleine, kontrollierte Karte brauchen (Sternkarte um Beta, genau eine Werft, Platin nur in Delta, feste Messwerte aus der Kalibrierung),
    /// bauen diese Welt statt der 11-Sektoren-Standardwelt.
    /// </summary>
    public static string ClassicJson()
    {
        using var stream = typeof(TestWorlds).Assembly.GetManifestResourceStream("SpaceSim.Tests.Content.classic-world.json")
            ?? throw new InvalidOperationException("Test-Ressource classic-world.json fehlt");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Weltbeschreibung der klassischen 4-Sektoren-Welt (Schalter wie in der Datei: Treibstoff und Forschung aus).</summary>
    public static WorldDef ClassicDef() => WorldBuilder.Parse(ClassicJson());

    /// <summary>Klassische 4-Sektoren-Welt (Phase 20), wie in der Datei.</summary>
    public static Universe Classic(ulong? seed = null, string? startOption = null) => WorldBuilder.Build(ClassicDef(), seed, startOption);

    /// <summary>Klassische 4-Sektoren-Welt mit den angegebenen Schaltern (wie <see cref="Default"/>).</summary>
    public static Universe Classic(ulong? seed, bool fuel, bool research, bool founderQuality = true)
    {
        var def = ClassicDef();
        def.Rules ??= new EconomyRules();
        def.Rules.FuelActive = fuel;
        def.Rules.ResearchActive = research;
        def.Rules.FounderQuality = founderQuality;
        return WorldBuilder.Build(def, seed, null);
    }
}
