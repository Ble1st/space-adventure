using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Phase 29 (Schiffsraster): gemeinsame Modellwahl für Kampfschiffe — Polizei, Marine und Piraten wählen unter allen Kampfmodellen (kampffähig, ohne Fracht,
/// Bergbau und Bau) statt eines festen Modells. Wert = Kampfstärke^(1 + Schwere) ÷ Preis: ohne Lage (Schwere 0) das günstigste Verhältnis, mit wachsender Lage
/// (Bedrohung im Territorium, Krieg) immer schwerere Schiffe. Rein, feste Reihenfolge (Modellschlüssel).
/// </summary>
public static class CombatModels
{
    /// <summary>Kampfstärke eines Modells aus seinen Werten (wie <see cref="CombatRules.Strength(Ship)"/> für ein heiles Schiff).</summary>
    public static float Strength(ShipStats s) => s.WeaponPower + s.Shield / 20f + s.Hull / 50f + 0.5f;

    /// <summary>Kampfmodell: kampffähig und weder Fracht- noch Bergbau- noch Baufähigkeit.</summary>
    public static bool IsCombat(ShipStats s) =>
        (s.Capabilities & Capability.Combat) != 0 && (s.Capabilities & (Capability.Cargo | Capability.Mining | Capability.Construction)) == 0;

    /// <summary>Alle Kampfmodelle (nach Schlüssel) mit Klasse und Werten.</summary>
    /// <param name="carriers">Träger zulassen (nur die Marine im Krieg, <see cref="EconomyRules.CarrierNavy"/>); sonst fehlen sie — Polizei und Piraten kaufen nie Träger.</param>
    public static IEnumerable<(ShipModel Model, ShipClass Class, ShipStats Stats)> All(Universe u, bool carriers = false)
    {
        foreach (var m in u.ShipModels.Values.OrderBy(m => m.Key, StringComparer.Ordinal))
        {
            if (!u.ShipClasses.TryGetValue(m.ClassKey, out var cls)) continue;
            var stats = ShipStats.Compute(cls, m.Fit.Select(k => k is not null && u.Modules.TryGetValue(k, out var mod) ? mod : null).ToList());
            if (stats.HangarBays > 0 && !carriers) continue;   // Träger: nur auf ausdrücklichen Wunsch
            if (IsCombat(stats)) yield return (m, cls, stats);
        }
    }

    /// <summary>Gewicht der Lage 0..1: Bedrohung ÷ <see cref="EconomyRules.CombatHeavyThreat"/>.</summary>
    public static float Heavy(Universe u, float threat) => u.Rules.CombatHeavyThreat <= 0f ? 0f : Math.Clamp(threat / u.Rules.CombatHeavyThreat, 0f, 1f);

    static float Score(ShipStats s, float price, float heavy) => Score(Strength(s), price, heavy);
    static float Score(float strength, float price, float heavy) => price <= 0f ? 0f : MathF.Pow(strength, 1f + Math.Clamp(heavy, 0f, 1f)) / price;

    /// <summary>
    /// Kauf an einer Werft (Polizei, Marine): bestes Kampfmodell, das eine von <paramref name="home"/> erreichbare Werft mit allem Material liefern kann und
    /// das <paramref name="budget"/> bezahlt. null = keins.
    /// </summary>
    public static (ShipModel Model, ShipClass Class, Station Yard, IReadOnlyList<MaterialNeed> Needs, float Price)? AtYard(Universe u, SectorId home, float budget, float heavy, bool carriers = false)
    {
        float fighter = carriers ? Combat.Carriers.FighterStrength(u) : 0f;   // Träger: Stärke mit vollen Hangars
        (ShipModel, ShipClass, Station, IReadOnlyList<MaterialNeed>, float)? best = null;
        float bestScore = 0f;
        var yards = u.StationsById().Where(st => !st.IsBuildSite && st.Market.Count > 0 && u.IsReachable(st.Sector, home)).ToList();
        foreach (var (model, cls, stats) in All(u, carriers))
        {
            if (ShipPurchase.Materials(u, model) is not { } needs) continue;
            foreach (var yard in yards)
            {
                if (!ShipPurchase.SellsModel(u, yard, model) || !Research.Available(u, yard, UnlockKind.Model, model.Key) || ShipPurchase.Missing(yard, needs).Count > 0) continue;
                float price = ShipPurchase.Price(u, yard, needs, ShipPurchase.AssemblyOf(u, yard));
                if (price > budget) continue;
                float score = stats.HangarBays > 0 ? Score(Strength(stats) + stats.HangarBays * fighter, price, heavy) : Score(stats, price, heavy);
                if (score > bestScore) { bestScore = score; best = (model, cls, yard, needs, price); }
                break;   // die erste passende Werft (Id-Reihenfolge) je Modell
            }
        }
        return best;
    }

    /// <summary>Kapazitätseinheiten eines Kampfschiffs nach Klassengröße (Piratengruppen): S 1, M 1,5, L 2, XL 3.</summary>
    public static float Units(ShipClass cls) => cls.Size switch { SlotSize.S => 1f, SlotSize.M => 1.5f, SlotSize.L => 2f, _ => 3f };

    /// <summary>Materialwert eines Modells zu Basispreisen (Bausatz + Module).</summary>
    public static float MaterialValue(Universe u, ShipModel model)
    {
        if (ShipPurchase.Materials(u, model) is not { } needs) return 0f;
        float v = 0f;
        foreach (var n in needs) v += n.Units * u.Goods[n.Good].BasePrice;
        return v;
    }

    /// <summary>Bau im Versteck: Materialwert (Basispreise) × <see cref="EconomyRules.PirateBuildFactor"/> × Kapazitätseinheiten (große Rümpfe brauchen mehr Schwarzmarkt).</summary>
    public static float PirateCost(Universe u, ShipModel model, ShipClass cls) => MaterialValue(u, model) * MathF.Max(0f, u.Rules.PirateBuildFactor) * Units(cls) * ModelCostFactor(u, model);

    /// <summary>Nachtrag 5b: Aufschlag je Modell auf den Piratenbau (<see cref="EconomyRules.PirateModelCostFactors"/>; fehlt = 1).</summary>
    public static float ModelCostFactor(Universe u, ShipModel model) =>
        u.Rules.PirateModelCostFactors is { Count: > 0 } f && f.TryGetValue(model.Key, out var x) ? MathF.Max(0f, x) : 1f;

    /// <summary>Günstigster Bau im Versteck; 0 = keins.</summary>
    public static float CheapestPirateCost(Universe u)
    {
        float best = 0f;
        foreach (var (model, cls, _) in All(u))
        {
            float c = PirateCost(u, model, cls);
            if (c > 0f && (best == 0f || c < best)) best = c;
        }
        return best;
    }

    /// <summary>
    /// Piraten: Bau im Versteck zum Materialwert × <see cref="EconomyRules.PirateBuildFactor"/> (keine Fraktionswerft) — bestes Kampfmodell, das in die
    /// freien Kapazitätseinheiten passt und das <paramref name="budget"/> bezahlt. null = keins.
    /// </summary>
    public static (ShipModel Model, ShipClass Class, float Cost)? ForPirates(Universe u, float budget, float freeUnits, float heavy, float costFactor = 1f)
    {
        (ShipModel, ShipClass, float)? best = null;
        float bestScore = 0f;
        foreach (var (model, cls, stats) in All(u))
        {
            if (Units(cls) > freeUnits + 1e-4f) continue;
            float cost = PirateCost(u, model, cls) * costFactor;
            if (cost <= 0f || cost > budget) continue;
            // Kaperer brauchen Tempo, um Beute einzuholen: langsamer als ein Einheitsfrachter zählt anteilig
            float score = Score(stats, cost, heavy) * MathF.Min(1f, stats.Speed / Travel.ShipSpeed);
            if (score > bestScore) { bestScore = score; best = (model, cls, cost); }
        }
        return best;
    }
}
