using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Ausgleich nach dem Spielerbot-Test (Punkt 6, Nutzer: „Schiffspreise wie im decomp“): Schiffskauf und Flottenunterhalt des Spielers nach dem Vorbild von
/// Interstellar Pilot 2 / OpenFrontier (Kaufpreis = Herstellkosten × (1 + 0,2 Aufschlag ± |Ansehen|² × 0,2), auf 250 Cr gerundet). Nur der Spieler
/// (Regel <see cref="EconomyRules.PlayerShipPricing"/>): die KI-Flotte kauft weiter über <see cref="ShipPurchase.Price"/>, damit sich die NPC-Wirtschaft nicht
/// verschiebt. Kampfschiffe waren im Verhältnis zu ihrer Stärke billig (Material je Stärke: Zerstörer 163, Schlachtkreuzer 180, Händler 187–230);
/// ein Mindestpreis je Kampfstärke (<see cref="EconomyRules.PlayerShipMinPricePerStrength"/>) hebt sie auf das Niveau ziviler Schiffe.
/// </summary>
public static class ShipPricing
{
    /// <summary>Kampfstärke eines Fits bei voller Hülle und vollem Schild (wie <see cref="CombatRules.Strength(Ship)"/>).</summary>
    public static float Strength(ShipStats s) => s.WeaponPower + s.Shield / 20f + s.Hull / 50f + 0.5f;

    /// <summary>
    /// Preisfaktor des Händlers für den Spieler: 1 + Aufschlag − sign(rel) × rel² × Gewicht, rel = Beziehung der Werftfraktion zum Spieler
    /// (freundlich billiger, feindlich teurer; nie unter 1).
    /// </summary>
    public static float DealerFactor(Universe u, Station yard, ActorId buyer)
    {
        var r = u.Rules;
        float rel = Math.Clamp(Relations.Effective(u, yard.Owner, buyer), -1f, 1f);
        float f = 1f + MathF.Max(0f, r.PlayerDealerMarkup) - MathF.Sign(rel) * rel * rel * MathF.Max(0f, r.PlayerDealerOpinionWeight);
        return MathF.Max(1f, f);
    }

    /// <summary>Aufrunden auf ein Vielfaches von <see cref="EconomyRules.PlayerShipPriceRounding"/> (0 = nicht runden).</summary>
    public static float Round(EconomyRules r, float price) =>
        r.PlayerShipPriceRounding > 0f ? MathF.Ceiling(price / r.PlayerShipPriceRounding - 1e-4f) * r.PlayerShipPriceRounding : price;

    /// <summary>
    /// Kaufpreis des Spielers: max(Materialpreis zu Marktpreisen, Kampfstärke × Mindestpreis) × Händlerfaktor, plus der Spiegelzuschlag des Rückkaufdeckels
    /// (<paramref name="ledgerSurcharge"/>, ebenfalls × Faktor), aufgerundet. Rein.
    /// </summary>
    public static float PlayerPrice(Universe u, Station yard, ActorId buyer, float materialPrice, ShipStats stats, float ledgerSurcharge)
    {
        var r = u.Rules;
        float basis = MathF.Max(materialPrice, Strength(stats) * MathF.Max(0f, r.PlayerShipMinPricePerStrength));
        float f = DealerFactor(u, yard, buyer);
        return Round(r, (basis + ledgerSurcharge) * f);
    }

    /// <summary>
    /// Flottenunterhalt je Tick eines Spielerschiffs: <see cref="EconomyRules.ShipUpkeepPerTick"/> × Faktor der Klassengröße
    /// (<see cref="EconomyRules.PlayerFleetUpkeepBySize"/>: S, M, L, XL; leer = flach).
    /// </summary>
    public static float FleetUpkeepPerTick(Universe u, Ship ship)
    {
        var r = u.Rules;
        if (r.PlayerFleetUpkeepBySize.Count == 0) return r.ShipUpkeepPerTick;
        var size = u.ShipModels.TryGetValue(ship.ModelKey, out var m) && u.ShipClasses.TryGetValue(m.ClassKey, out var c) ? c.Size : SlotSize.S;
        int i = Math.Min((int)size, r.PlayerFleetUpkeepBySize.Count - 1);
        return r.ShipUpkeepPerTick * MathF.Max(0f, r.PlayerFleetUpkeepBySize[Math.Max(0, i)]);
    }
}
