using SpaceSim.Core.Model;

namespace SpaceSim.Core.Content;

/// <summary>Erzeugt Stationen aus Stationstypen (Rezepte und Lagerposten <see cref="StationType.Stocks"/>). Wird vom Welt-Builder und vom Stationsbau der Wirtschaft verwendet.</summary>
public static class StationFactory
{
    public const float DefaultStock = 50f, DefaultTarget = 100f;

    public static Station Create(Universe u, StationId id, StationType type, string name, SectorId sector, Vec2 pos, ActorId owner)
    {
        var st = new Station { Id = id, TypeKey = type.Key, Name = name, Sector = sector, Position = pos, Owner = owner, Credits = 10_000, BuiltTick = u.Tick };
        foreach (var r in type.Recipes)
        {
            st.Production.Add(new Recipe
            {
                TicksPerCycle = r.TicksPerCycle,
                Inputs = new Dictionary<GoodId, float>(r.Inputs),
                Outputs = new Dictionary<GoodId, float>(r.Outputs),
            });
            foreach (var g in r.Inputs.Keys.Concat(r.Outputs.Keys))
            {
                var e = st.Stock(g);
                if (e.Amount == 0 && e.Target == 100f) { e.Amount = DefaultStock; e.Target = DefaultTarget; }
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
