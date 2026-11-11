using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// NPC-Upgrades (Phase 7, SCHIFFSSLOTS.md Abschnitt 8): Reiche, untätige KI-Schiffe (ab <see cref="EconomyRules.UpgradeMinCredits"/>), die gerade an
/// einer Station mit Dienst „upgrade“ liegen, versuchen ein Modul-Upgrade nach derselben Regel wie der Spieler (<see cref="ShipUpgrade"/>,
/// Material und Credits aus dem Stationslager bzw. der Schiffskasse, Wurf über <see cref="Universe.Rng"/>). Gewählt wird das Upgrade mit dem
/// höchsten erwarteten Gewinn je Credit: Zuwachs des Kaufwerts in der aktuellen Aufgabe (<see cref="IShipJob.FleetValue"/>) × Chance ÷ Preis;
/// ein Versuch kostet höchstens <see cref="EconomyRules.UpgradeBudgetShare"/> der Credits. NPCs bauen keine Slots um (sie rüsten nicht um).
/// </summary>
public sealed class UpgradeSystem : ISimSystem
{
    /// <summary>Schlüssel des Dienstes an Stationstypen (gleich <c>Services.UpgradeService.ServiceKey</c>; der Core-Simulationsteil kennt die Dienst-Registry nicht).</summary>
    public const string ServiceKey = "upgrade";

    public void Step(Universe u, int dt)
    {
        var rules = u.Rules;
        foreach (var ship in u.Ships.Values.OrderBy(s => s.Id.Value).ToList())
        {
            if (!ship.IsEconomyAi || !ship.HasModel || ship.Transit is not null || ship.Order is not null) continue;
            if (ship.Credits < rules.UpgradeMinCredits) continue;
            if (Refuel.CurrentStation(u, ship) is not { } st) continue;
            if (!u.StationTypes.TryGetValue(st.TypeKey, out var type) || !type.Services.Contains(ServiceKey)) continue;
            if (Best(u, st, ship) is { } q) ShipUpgrade.Attempt(u, st, ship, q);
        }
    }

    /// <summary>Bestes Modul-Upgrade für die aktuelle Aufgabe (reine Auswahl); null = keines lohnt oder ist möglich.</summary>
    public static UpgradeQuote? Best(Universe u, Station st, Ship ship)
    {
        if (ShipJobs.ForRole(ship.Role) is not { } job) return null;
        var cls = u.ShipModels.TryGetValue(ship.ModelKey, out var model) ? u.ShipClasses.GetValueOrDefault(model.ClassKey) : null;
        if (cls is null) return null;
        float before = job.FleetValue(ship.Stats, u.Rules);
        // Phase 15: ein Upgrade, das die belegte Kapazität der Aufgabe über Grenze + Überhang höbe (z. B. größeres Frachtmodul), unterbleibt
        float unitsBefore = ShipJobs.UnitsOf(u, ship, job);
        float otherUnits = ShipJobs.UsedUnits(u, job) - (ship.Role == job.Role ? unitsBefore : 0f);
        float room = job.Limit(u.Rules) + u.Rules.FleetCapacityOvershoot;
        UpgradeQuote? best = null;
        float bestGain = 0f;
        foreach (var q in ShipUpgrade.Quotes(u, st, ship))
        {
            if (q.Kind != UpgradeKind.Module || !q.Possible || q.Price > ship.Credits * u.Rules.UpgradeBudgetShare) continue;
            var fit = new List<Module?>(ship.Fit.Count);
            for (int i = 0; i < ship.Fit.Count; i++)
            {
                var key = i == q.Slot ? q.ToModule : ship.Fit[i];
                fit.Add(key is not null && u.Modules.TryGetValue(key, out var m) ? m : null);
            }
            var statsAfter = ShipStats.Compute(cls, fit);
            float unitsAfter = job.Units(statsAfter, statsAfter.Cargo, u.Rules);
            if (unitsAfter > unitsBefore && otherUnits + unitsAfter > room + 0.0001f) continue;
            float after = job.FleetValue(statsAfter, u.Rules);
            float gain = (after - before) * q.Chance / MathF.Max(1f, q.Price);
            if (gain > bestGain) { bestGain = gain; best = q; }
        }
        return best;
    }
}
