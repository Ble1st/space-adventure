namespace SpaceSim.Core.Content;

/// <summary>
/// Raketenlenkung (KAMPF.md „Raketenlenkung und Gegenmaßnahmen“): Inhalte, die nur mit <see cref="Model.EconomyRules.MissileGuidance"/> in die Welt kommen —
/// Täuschkörper (Ware), optischer und halbaktiver Raketenwerfer, Täuschkörperwerfer, Störsender, die Fits von Donner/Lanze/Leviathan und die Lenkart der
/// alten Werfer. Mit abgeschaltetem Schalter nimmt <see cref="WorldBuilder.Build"/> sie heraus, damit die Welt bitgleich zur Welt davor bleibt.
/// </summary>
public static class GuidanceContent
{
    public const string FlareGood = "flare";
    public static readonly string[] Modules = { "missile_launcher_optical_s", "missile_launcher_radar_s", "flare_launcher_s", "jammer_s" };
    /// <summary>Alle Schlüssel, die mit der Raketenlenkung kamen (Ware und Module).</summary>
    public static readonly string[] Keys = { FlareGood, "missile_launcher_optical_s", "missile_launcher_radar_s", "flare_launcher_s", "jammer_s" };

    /// <summary>Modul im Fit vor der Raketenlenkung (null = leerer Slot).</summary>
    static string? Replacement(string key) => key switch
    {
        "missile_launcher_optical_s" or "missile_launcher_radar_s" => "missile_launcher_s",
        "jammer_s" => "weapon_magazine_s",
        _ => null,
    };

    /// <summary>Nimmt die Inhalte der Raketenlenkung aus der Weltbeschreibung (idempotent; ändert <paramref name="def"/>).</summary>
    public static void Strip(WorldDef def)
    {
        def.Goods.RemoveAll(g => g.Key == FlareGood);
        def.Modules.RemoveAll(m => Array.IndexOf(Modules, m.Key) >= 0);
        foreach (var m in def.Modules) m.Guidance = null;
        foreach (var t in def.StationTypes)
        {
            t.Recipes.RemoveAll(r => Keys.Any(r.In.ContainsKey) || Keys.Any(r.Out.ContainsKey));
            if (t.Stocks is { } st) foreach (var k in Keys) st.Remove(k);
        }
        foreach (var md in def.ShipModels)
            for (int i = 0; i < md.Fit.Count; i++)
                if (md.Fit[i] is { } k && Array.IndexOf(Modules, k) >= 0) md.Fit[i] = Replacement(k);
    }
}
