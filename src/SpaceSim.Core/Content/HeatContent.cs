namespace SpaceSim.Core.Content;

/// <summary>
/// Hitze (KAMPF.md „Hitze und Wärmeabfuhr“): Inhalte, die nur mit <see cref="Model.EconomyRules.ShipHeat"/> in die Welt kommen — die Kühlermodule
/// <c>heat_sink_s</c>/<c>heat_sink_m</c> und ihre Werftregale. Mit abgeschaltetem Schalter nimmt <see cref="WorldBuilder.Build"/> sie heraus, damit die Welt
/// bitgleich zur Welt davor bleibt.
/// </summary>
public static class HeatContent
{
    public static readonly string[] Modules = { "heat_sink_s", "heat_sink_m" };

    /// <summary>Nimmt die Kühlermodule aus der Weltbeschreibung (idempotent; ändert <paramref name="def"/>).</summary>
    public static void Strip(WorldDef def)
    {
        def.Modules.RemoveAll(m => Array.IndexOf(Modules, m.Key) >= 0);
        foreach (var t in def.StationTypes)
        {
            t.Recipes.RemoveAll(r => Modules.Any(r.In.ContainsKey) || Modules.Any(r.Out.ContainsKey));
            if (t.Stocks is { } st) foreach (var k in Modules) st.Remove(k);
        }
        foreach (var md in def.ShipModels)
            for (int i = 0; i < md.Fit.Count; i++)
                if (md.Fit[i] is { } k && Array.IndexOf(Modules, k) >= 0) md.Fit[i] = null;
    }
}
