using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Phase 22: Kapitalausgleich innerhalb eines aufgestiegenen Akteurs (Viabilitäts-Modul, Intervall 50; Regel <see cref="EconomyRules.FirmCapitalBalance"/>).
/// Gemessene Ursache vieler Pleiten: Nach einem Überfall oder einer abgeworfenen Ladung verarmte ein Schiff einer Firma, konnte nur noch winzige Ladungen
/// (1–2 Einheiten Energie) bezahlen und verlor bis zur Pleite, während seine Geschwisterschiffe Tausende Credits hatten. Jetzt gibt das reichste Schiff
/// desselben Akteurs (mit mindestens <see cref="EconomyRules.FleetExpandCredits"/>) einem leeren Geschwisterschiff (ohne Fracht, unter der Hälfte von
/// <see cref="EconomyRules.NewShipStartCredits"/>) bis zu <see cref="EconomyRules.NewShipStartCredits"/> ab — höchstens so viel, dass ihm selbst das Startkapital bleibt. Kein neues Geld;
/// die Rentabilitätsmessung beider Schiffe ist neutral (<see cref="Profitability.Transfer"/>). Freie Einzelschiffe (Sammelgruppe) haben keine Firma.
/// </summary>
public sealed class FirmCapitalSystem : ISimSystem
{
    public void Step(Universe u, int dt)
    {
        var r = u.Rules;
        if (!r.FirmCapitalBalance || r.NewShipStartCredits <= 0f) return;
        Dictionary<ActorId, List<Ship>>? byOwner = null;
        foreach (var s in u.ShipsById())
        {
            if (!s.IsEconomyAi || s.Owner == u.FreeActor || !u.Actors.TryGetValue(s.Owner, out var a) || a.Kind != ActorKind.Independent) continue;
            byOwner ??= new Dictionary<ActorId, List<Ship>>();
            if (!byOwner.TryGetValue(s.Owner, out var list)) byOwner[s.Owner] = list = new List<Ship>();
            list.Add(s);
        }
        if (byOwner is null) return;
        foreach (var (owner, ships) in byOwner.OrderBy(kv => kv.Key.Value))
        {
            if (ships.Count < 2) continue;
            foreach (var poor in ships)
            {
                if (poor.Credits >= 0.5f * r.NewShipStartCredits || poor.Cargo.Values.Any(v => v > 0.001f)) continue;
                Ship? rich = null;
                foreach (var s in ships) if (s != poor && s.Credits >= r.FleetExpandCredits && (rich is null || s.Credits > rich.Credits)) rich = s;
                if (rich is null) break;
                float amount = MathF.Min(r.NewShipStartCredits - poor.Credits, rich.Credits - r.NewShipStartCredits);
                if (amount <= 0f) continue;
                rich.Credits -= amount;
                poor.Credits += amount;
                Profitability.Transfer(rich.Profit, -amount);
                Profitability.Transfer(poor.Profit, amount);
                u.Events.Publish(new FirmCapitalMoved(owner, rich.Id, poor.Id, amount));
            }
        }
    }
}
