using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Combat;

/// <summary>
/// Kapitulation (docs/KAMPF.md „Kapitulation“, Nutzerentscheidung „eher Kapitulation oder Station zerstört … für alle, nicht nur Spieler“): Nicht der
/// Angreifer entert — die <b>Station</b> entscheidet. Fällt ihre Hülle unter eine Schwelle aus <see cref="EconomyRules.CapitulationThresholds"/>, würfelt sie
/// genau einmal (ein Zug aus <see cref="Universe.Rng"/>): kapituliert sie, gehört sie dem Angreifer (Spielerperson bzw. angreifende KI-Fraktion im Krieg),
/// das Gefecht endet; sonst kämpft sie weiter und wird bei Hülle 0 zerstört (<see cref="StationCombat.Fall"/>). Piratenstationen (Besitzer gesetzlos,
/// Verstecke) kapitulieren nie, Unsterbliche und Baustellen auch nicht. Außerhalb eines Krieges ist die Übernahme durch den Spieler ein schweres Verbrechen
/// („capture_station“); der Verlust zählt im Krieg (Kriegsmüdigkeit); verliert eine KI-Fraktion ihren Sitz, entsteht ein Not-Sitz (<see cref="SeatRelocation"/>).
/// </summary>
public static class Capitulation
{
    /// <summary>Piratenstation (Besitzer gesetzlos oder Versteck)? Sie kapituliert nie.</summary>
    public static bool IsPirate(Universe u, Station st) =>
        StationProtection.IsHideout(u, st) || (u.Actors.TryGetValue(st.Owner, out var a) && a.Kind == ActorKind.Outlaw);

    /// <summary>Gehört die Station schon dem Angreifer (bzw. bei KI seiner Fraktion)?</summary>
    public static bool OwnedBy(Universe u, Station st, ActorId attacker)
    {
        if (st.Owner == attacker) return true;
        bool player = u.Actors.TryGetValue(attacker, out var a) && a.Kind == ActorKind.Player;
        return !player && u.FactionOf(attacker) is { } fa && st.Owner == (ActorId)fa;
    }

    /// <summary>Wem gehört die Station, wenn sie vor diesem Angreifer kapituliert? null = sie kann vor ihm nicht kapitulieren. Rein.</summary>
    public static ActorId? TakerFor(Universe u, Station st, ActorId attacker)
    {
        var r = u.Rules;
        if (!r.StationCapitulation || st.IsBuildSite || st.Immortal || IsPirate(u, st) || OwnedBy(u, st, attacker)) return null;
        if (!u.Actors.TryGetValue(attacker, out var a)) return null;
        if (a.Kind == ActorKind.Player) return attacker;
        if (!r.AiStationCapture || a.Kind == ActorKind.Outlaw || u.FactionOf(attacker) is not { } fa || !u.IsNpcFaction(fa) || !u.Actors.ContainsKey(fa)) return null;
        if (u.FactionOf(st.Owner) is not { } g || !Wars.Hostile(u, fa, g)) return null;
        return (ActorId)fa;
    }

    /// <summary>Grundchance der Schwelle mit Index <paramref name="index"/> (fehlt ein Wert, gilt der letzte).</summary>
    public static float BaseChance(EconomyRules r, int index) =>
        r.CapitulationChances.Count == 0 ? 0f : r.CapitulationChances[Math.Min(index, r.CapitulationChances.Count - 1)];

    /// <summary>
    /// Chance, dass die Station an der Schwelle <paramref name="index"/> kapituliert (rein): Grundchance − Abzug je aktivem Verteidiger + Zuschlag je weiterem
    /// aktivem Angreifer − (Aggression des Besitzers − 0,5) × Gewicht, begrenzt auf [<see cref="EconomyRules.CapitulationChanceMin"/>,
    /// <see cref="EconomyRules.CapitulationChanceMax"/>].
    /// </summary>
    public static float Chance(Universe u, Station st, int index, int defenders, int attackers)
    {
        var r = u.Rules;
        float aggression = u.Actors.TryGetValue(st.Owner, out var owner) ? owner.Personality.Aggression : 0.5f;
        float c = BaseChance(r, index) - r.CapitulationDefenderPenalty * Math.Max(0, defenders)
                  + r.CapitulationAttackerBonus * Math.Min(Math.Max(0, attackers - 1), Math.Max(0, r.CapitulationAttackerMax))
                  - (aggression - 0.5f) * r.CapitulationAggressionWeight;
        return Math.Clamp(c, r.CapitulationChanceMin, r.CapitulationChanceMax);
    }

    /// <summary>Aktive Verteidiger und Angreifer eines Gefechts (Seite 1 bzw. 0; ohne Gefecht 0/1).</summary>
    public static (int Defenders, int Attackers) Sides(Encounter? e)
    {
        if (e is null) return (0, 1);
        int d = 0, a = 0;
        foreach (var p in e.Participants) { if (!p.Active) continue; if (p.Side == 1) d++; else a++; }
        return (d, Math.Max(1, a));
    }

    /// <summary>Chance an der nächsten noch nicht unterschrittenen Schwelle (Anzeige); null = keine Schwelle mehr oder keine Kapitulation möglich.</summary>
    public static (float Threshold, float Chance)? Next(Universe u, Station st, ActorId attacker)
    {
        if (TakerFor(u, st, attacker) is null) return null;
        float max = StationCombat.MaxHull(u, st);
        if (max <= 0f) return null;
        float share = StationCombat.Hull(u, st) / max;
        var (d, a) = Sides(Siege.Of(u, st.Id));
        var th = u.Rules.CapitulationThresholds;
        for (int i = 0; i < th.Count; i++)
            if (share > th[i]) return (th[i], Chance(u, st, i, d, a));
        return null;
    }

    /// <summary>
    /// Nach einem Hüllentreffer: für jede in diesem Treffer unterschrittene Schwelle (von oben) genau ein Zug; kapituliert die Station, wird sie übernommen.
    /// true = kapituliert.
    /// </summary>
    public static bool Roll(Universe u, Encounter? e, Station st, ActorId attacker, float beforeShare, float afterShare)
    {
        if (TakerFor(u, st, attacker) is not { } taker) return false;
        var th = u.Rules.CapitulationThresholds;
        var (d, a) = Sides(e);
        for (int i = 0; i < th.Count; i++)
        {
            if (!(beforeShare > th[i] && afterShare <= th[i])) continue;
            float chance = Chance(u, st, i, d, a);
            bool yes = u.Rng.NextDouble() < chance;
            u.Events.Publish(new StationCapitulationRolled(st.Id, st.Name, st.Sector, st.Owner, taker, th[i], chance, yes));
            if (yes) { Capitulate(u, st, taker, attacker); return true; }
        }
        return false;
    }

    /// <summary>
    /// Die Station kapituliert: Besitz an <paramref name="taker"/>, Geschütze schweigen (das Gefecht endet, weil sie nicht mehr verteidigt), Hülle mindestens
    /// <see cref="EconomyRules.CapitulationHullFloor"/>; Markt, Lager und Kasse bleiben (die Kasse ist das Betriebskonto der Station und gehört jetzt dem neuen
    /// Besitzer). Kriegsverlust, Fahndung (Spieler außerhalb eines Krieges), Heimatbasis der alten Polizei, Sektorkontrolle, Not-Sitz.
    /// </summary>
    public static void Capitulate(Universe u, Station st, ActorId taker, ActorId attacker)
    {
        var old = st.Owner;
        var oldFaction = u.FactionOf(old);
        float value = u.StationTypes.TryGetValue(st.TypeKey, out var t) ? t.BuildCost : 0f;
        Wars.OnLoss(u, old, attacker, value);
        bool player = u.Actors.TryGetValue(taker, out var ta) && ta.Kind == ActorKind.Player;
        if (player && !Wars.AtWar(u, taker, old)) Wanted.ReportAssault(u, taker, st.Sector, old, "capture_station");
        st.Owner = taker;
        var c = st.Condition ??= new StationCondition();
        c.Disabled = false;
        float max = StationCombat.MaxHull(u, st);
        c.HullMissing = MathF.Min(c.HullMissing, max * (1f - Math.Clamp(u.Rules.CapitulationHullFloor, 0f, 1f)));
        for (int i = 0; i < c.Timers.Count; i++) c.Timers[i] = 0f;
        st.BelowLimitSince = -1;
        // Polizei/Kriegsschiffe, deren Heimatbasis jetzt fremd ist, bekommen eine neue
        foreach (var s in u.ShipsById())
            if (s.HomeBase == st.Id && s.Owner != taker)
                s.HomeBase = u.FactionOf(s) is { } sf ? SecuritySystem.HomeBaseFor(u, sf, st.Sector)?.Id ?? default : default;
        if (u.Sectors.TryGetValue(st.Sector, out var sec) && SectorControl.Update(u, sec)) u.TouchAccess();
        u.Events.Publish(new StationFell(st.Id, st.Name, st.Sector, old, taker, "capitulated"));
        if (oldFaction is { } of) SeatRelocation.Ensure(u, of);
    }
}
