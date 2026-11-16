using SpaceSim.Core.Model;

namespace SpaceSim.Core.Content;

/// <summary>Erzeugt Stationen aus Stationstypen (Rezepte und Lagerposten <see cref="StationType.Stocks"/>). Wird vom Welt-Builder und vom Stationsbau der Wirtschaft verwendet.</summary>
public static class StationFactory
{
    /// <summary>
    /// Runde 12 (Handy-Test: „Elektronikfabrik Kalypso 1000“): Name einer gebauten Station = Typ + Sektor, ab der zweiten Station dieses Typs im Sektor mit
    /// römischer Nummer („Elektronikfabrik Kalypso II“) statt der Id. <paramref name="except"/> = die Station selbst (Umbau einer Baustelle).
    /// </summary>
    public static string BuiltName(Universe u, StationType type, SectorId sector, StationId except = default)
    {
        string shortName = u.Sectors[sector].Name.Split(' ').Last();
        string baseName = $"{type.Name} {shortName}";
        var taken = new HashSet<string>(u.Stations.Values.Where(s => s.Id != except).Select(s => s.Name));
        if (!taken.Contains(baseName)) return baseName;
        for (int n = 2; ; n++) { var name = $"{baseName} {Roman(n)}"; if (!taken.Contains(name)) return name; }
    }

    /// <summary>Phase 30: Radius/Andockbarkeit einer Station aus ihrem Typ — eine Baustelle belegt schon den Platz des Zieltyps (bleibt andockbar zum Beliefern).</summary>
    public static void ApplySize(Universe u, Station st)
    {
        string key = st.Site is { } site ? site.TypeKey : st.TypeKey;
        if (!u.StationTypes.TryGetValue(key, out var t)) return;
        st.Radius = t.Radius;
        st.Dockable = st.Site is not null || t.Dockable;
        st.MaxShip = st.Site is not null ? null : StationSizes.MaxShip(t.Size);
    }

    /// <summary>Römische Zahl (1–3999).</summary>
    public static string Roman(int n)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var (v, r) in new[] { (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"), (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I") })
            while (n >= v) { sb.Append(r); n -= v; }
        return sb.ToString();
    }

    public const float DefaultStock = 50f, DefaultTarget = 100f;

    public static Station Create(Universe u, StationId id, StationType type, string name, SectorId sector, Vec2 pos, ActorId owner)
    {
        var st = new Station { Id = id, TypeKey = type.Key, Name = name, Sector = sector, Position = pos, Owner = owner, Credits = 10_000, BuiltTick = u.Tick,
            Radius = type.Radius, Dockable = type.Dockable, MaxShip = StationSizes.MaxShip(type.Size) };
        foreach (var r in type.Recipes)
        {
            st.Production.Add(new Recipe
            {
                TicksPerCycle = r.TicksPerCycle,
                Inputs = new Dictionary<GoodId, float>(r.Inputs),
                Outputs = new Dictionary<GoodId, float>(r.Outputs),
            });
            // Runde 44: Wunschbestand der Rezeptwaren aus der Regel (Lagerpuffer für längere Fahrten in vergrößerten Sektoren; Standard 100 = wie bisher)
            float target = u.Rules.RecipeStockTarget > 0f ? u.Rules.RecipeStockTarget : DefaultTarget;
            foreach (var g in r.Inputs.Keys.Concat(r.Outputs.Keys))
            {
                var e = st.Stock(g);
                if (e.Amount == 0 && e.Target == 100f) { e.Amount = target * (DefaultStock / DefaultTarget); e.Target = target; }
            }
        }
        // Phase 5a: Lager ohne Rezept (stocks) — zusätzliche Markteinträge; für Rezeptwaren Anfangs-/Wunschbestand des Typs
        foreach (var s in type.Stocks)
        {
            var e = st.Stock(s.Good);
            e.Amount = s.Amount;
            e.Target = s.Target;
        }
        return st;
    }
}
