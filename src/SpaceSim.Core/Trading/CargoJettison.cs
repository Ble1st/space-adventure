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
    /// Phase 19: wie <see cref="Issue(Ship, GoodId)"/>, dazu die Lage des Auftrags: Ein Bauschiff (auch ein eigenes mit Befehl „Bauen“), das einer
    /// Baustelle zugeteilt ist, darf Material, das diese Baustelle noch braucht, nicht abwerfen — es ist aus deren Treuhandgeld bezahlt, und das Bauschiff
    /// würde es sofort noch einmal auf Kosten des Treuhandgelds holen. Abliefern oder den Bauauftrag beenden, dann ist es frei (Reste nach dem Bau, Ware,
    /// die die Baustelle nicht braucht, gehen immer). Fracht von Handels- und Schürfaufträgen ist frei: der Auftrag läuft weiter (der Händler kommt ohne
    /// die Ware an und sucht sich die nächste Runde, der Schürfer schürft weiter).
    /// </summary>
    public static string? Issue(Universe u, Ship ship, GoodId good)
    {
        if (Issue(ship, good) is { } basic) return basic;
        foreach (var st in u.StationsById())
            if (st.Site is { Abandoned: false } bs && bs.Builder == ship.Id && bs.Missing(st, good) > 0.001f)
                return $"Baumaterial für {st.Name}: aus dem Treuhandgeld der Baustelle bezahlt — erst abliefern oder den Bauauftrag beenden";
        return null;
    }

    /// <summary>
    /// Wirft bis zu <paramref name="units"/> Einheiten ab (≤ 0 oder mehr als an Bord = alles) und liefert die abgeworfene Menge (0 = nichts, Zustand
    /// unverändert, kein Ereignis).
    /// </summary>
    public static float Jettison(Universe u, Ship ship, GoodId good, float units)
    {
        if (Issue(u, ship, good) is not null) return 0f;
        float have = ship.Cargo[good];
        float n = !float.IsFinite(units) || units <= 0f || units >= have ? have : units;
        if (have - n <= 0.0001f) { n = have; ship.Cargo.Remove(good); }
        else ship.Cargo[good] = have - n;
        u.Events.Publish(new CargoJettisoned(ship.Id, ship.Sector, good, n));
        return n;
    }
}
