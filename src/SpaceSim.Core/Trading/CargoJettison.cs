using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Trading;

/// <summary>
/// Phase 18: Fracht abwerfen — der Ausweg für einen Rest, den keine Station nimmt (z. B. ein Erz-Bruchteil an einer vollen Station). Reine Regel
/// für Spielerschiffe, angedockt (Dienst „trade“, <c>drop:&lt;GoodId&gt;</c>) wie im Flug (Kommandozentrale „Besitz“, Schiffsdetail): Die Ware
/// verschwindet ohne Erlös aus der Welt (<see cref="CargoJettisoned"/>). NPC-Schiffe werfen über diese Regel nichts ab (ihre Reste behandeln die
/// KI-Systeme selbst).
/// </summary>
public static class CargoJettison
{
    /// <summary>Warum diese Ware nicht abgeworfen werden kann (null = möglich): nur Spielerschiffe, nur Ware an Bord.</summary>
    public static string? Issue(Ship ship, GoodId good)
    {
        if (!ship.IsPlayer) return "Nur eigene Schiffe können Fracht abwerfen";
        if (ship.Cargo.GetValueOrDefault(good) <= 0f) return "Nichts an Bord";
        return null;
    }

    /// <summary>
    /// Wirft bis zu <paramref name="units"/> Einheiten ab (≤ 0 oder mehr als an Bord = alles) und liefert die abgeworfene Menge (0 = nichts, Zustand
    /// unverändert, kein Ereignis).
    /// </summary>
    public static float Jettison(Universe u, Ship ship, GoodId good, float units)
    {
        if (Issue(ship, good) is not null) return 0f;
        float have = ship.Cargo[good];
        float n = !float.IsFinite(units) || units <= 0f || units >= have ? have : units;
        if (have - n <= 0.0001f) { n = have; ship.Cargo.Remove(good); }
        else ship.Cargo[good] = have - n;
        u.Events.Publish(new CargoJettisoned(ship.Id, ship.Sector, good, n));
        return n;
    }
}
