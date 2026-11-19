using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Ausgleich nach dem Spielerbot-Test (docs/POLITIK.md, Punkt 2): Regierungsunterhalt der Spielerfraktion. Im Botlauf kosteten sechs Behörden, das Hauptquartier
/// und eroberte Behörden zusammen 672 000 Cr/h — mehr als eine mittlere Flotte verdient; die Kasse lief ins Minus. Zwei Regeln:
/// <list type="bullet">
/// <item>Anteil <see cref="EconomyRules.PlayerGovernmentUpkeepShare"/> des Typ-Unterhalts (KI-Fraktionen unverändert).</item>
/// <item>Untergrenze <see cref="EconomyRules.PlayerUpkeepFloor"/>: reicht die Kasse nicht, wird die Station „unbezahlt“ (<see cref="Station.Unpaid"/>) — kein
/// Einfluss, kein Grundschutz, keine Behörde (Sperrstufen fallen mit der Kontrollstufe), kein Gericht/Kopfgeldbüro — statt immer tiefer in die Schulden.</item>
/// </list>
/// Andere gewollte Schulden (Leihmiete, Notreparatur, Kapitulationsstrafe, Liegegebühr) bleiben möglich.
/// </summary>
public static class PlayerUpkeep
{
    /// <summary>Gehört die Regierungsstation dem Spieler (einzige Stationen, für die diese Regeln gelten)?</summary>
    public static bool IsPlayerGovernment(Universe u, Station st, StationType t) =>
        st.Owner == u.PlayerActor && t.Government != GovernmentKind.None;

    /// <summary>Unterhalt je Tick einer Regierungsstation (Spieler: × <see cref="EconomyRules.PlayerGovernmentUpkeepShare"/>).</summary>
    public static float GovernmentPerTick(Universe u, Station st, StationType t) =>
        IsPlayerGovernment(u, st, t) ? PlayerPerTick(u, t) : t.UpkeepPerTick;

    /// <summary>Unterhalt je Tick, den der Spieler für eine Regierungsstation dieses Typs zahlen würde (Vorschau).</summary>
    public static float PlayerPerTick(Universe u, StationType t) => t.UpkeepPerTick * MathF.Max(0f, u.Rules.PlayerGovernmentUpkeepShare);

    /// <summary>Außer Betrieb, weil der Unterhalt nicht bezahlt ist (nur mit der Regel).</summary>
    public static bool Unpaid(Universe u, Station st) => st.Unpaid && u.Rules.PlayerUpkeepFloorActive;

    /// <summary>
    /// Bucht den Unterhalt einer Regierungsstation des Spielers mit der Untergrenze (Regel an). true = bezahlt. Eine unbezahlte Station läuft erst wieder,
    /// wenn danach noch <see cref="EconomyRules.PlayerUpkeepResumeBuffer"/> über der Untergrenze bleibt. Wechsel → <see cref="PlayerGovernmentUnpaidChanged"/>.
    /// </summary>
    public static bool Charge(Universe u, Station st, Actor owner, float cost)
    {
        var r = u.Rules;
        float need = r.PlayerUpkeepFloor + (st.Unpaid ? MathF.Max(0f, r.PlayerUpkeepResumeBuffer) : 0f);
        bool pay = owner.Treasury.Credits - cost >= need;
        if (pay) owner.Treasury.Credits -= cost;
        if (st.Unpaid == pay)
        {
            st.Unpaid = !pay;
            u.Events.Publish(new PlayerGovernmentUnpaidChanged(st.Id, st.Name, st.Sector, !pay));
            if (u.Sectors.TryGetValue(st.Sector, out var sec) && SectorControl.Update(u, sec)) u.TouchAccess();
        }
        return pay;
    }

    /// <summary>Unbezahlte Regierungsstationen des Spielers (Id-Reihenfolge).</summary>
    public static List<Station> UnpaidStations(Universe u) => u.StationsById().Where(s => s.Owner == u.PlayerActor && Unpaid(u, s)).ToList();

    /// <summary>Zeile für „Meine Fraktion“ (leer, wenn alles bezahlt ist).</summary>
    public static string StatusText(Universe u)
    {
        var l = UnpaidStations(u);
        if (l.Count == 0) return "";
        return $"Unterhalt nicht bezahlt: {string.Join(", ", l.Select(s => s.Name))} — außer Betrieb (kein Einfluss, keine Sperren, kein Gericht), bis die Kasse wieder "
               + $"{u.Rules.PlayerUpkeepFloor + u.Rules.PlayerUpkeepResumeBuffer:0} Cr über dem Unterhalt hat.";
    }
}
