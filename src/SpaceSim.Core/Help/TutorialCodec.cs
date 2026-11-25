using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;

namespace SpaceSim.Core.Help;

/// <summary>Laufendes Tutorial im Spielstand (additiv, v17 bleibt).</summary>
public sealed class TutorialSave
{
    public int Step { get; set; }
    public int Station { get; set; }
    public int BasePurchases { get; set; }
    public int BaseSales { get; set; }
    public double BaseOre { get; set; }
    public bool Done { get; set; }
}

/// <summary>Speichern/Laden des Tutorials. Bewusst nicht im <c>UniverseHash</c>: reine Anleitung, die kein System liest.</summary>
public static class TutorialCodec
{
    public static void Save(Universe u, SaveGame s)
    {
        if (u.Tutorial is not { } t) return;
        s.Tutorial = new TutorialSave { Step = t.Step, Station = t.Station.Value, BasePurchases = t.BasePurchases, BaseSales = t.BaseSales, BaseOre = t.BaseOre, Done = t.Done };
    }

    public static void Load(SaveGame s, Universe u)
    {
        if (s.Tutorial is not { } t) return;
        u.Tutorial = new TutorialState { Step = t.Step, Station = new StationId(t.Station), BasePurchases = t.BasePurchases, BaseSales = t.BaseSales, BaseOre = t.BaseOre, Done = t.Done };
    }
}
