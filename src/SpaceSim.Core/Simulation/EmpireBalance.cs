using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>Welle F (1): Einnahmen und Kosten eines Spielersektors in Credits je Spielstunde (Momentaufnahme nach den Regeln, keine Messung).</summary>
public sealed record SectorBalance(SectorId Sector, string Name, float Tax, float StationIncome, float Government, float Maintenance, float StationUpkeep, float Locks, int OwnStations)
{
    /// <summary>Einnahmen je Stunde: fremde Abgaben im Sektor (Anteil des Spielers) plus Ertrag der eigenen Wirtschaftsstationen.</summary>
    public float Income => Tax + StationIncome;
    /// <summary>Kosten je Stunde: Regierungsunterhalt, Instandhaltung (bei voller Versorgung), Netto-Unterhalt eigener Wirtschaftsstationen, Sperren.</summary>
    public float Costs => Government + Maintenance + StationUpkeep + Locks;
    public float Net => Income - Costs;
}

/// <summary>
/// Welle F (1): Rechnung je Sektor des Spielers (Anzeige „Meine Fraktion“, Messung). Rein — liest die Regeln wie <see cref="UpkeepSystem"/> und
/// <see cref="MaintenanceSystem"/>: Abgabe = (Kasse − Freibetrag) × Satz × Spieleranteil der fremden Zahler, deren Steuerbehörde der Spieler ist;
/// Regierungsunterhalt der bezahlten Behörden; Instandhaltung als Tagesbedarf zum Basispreis; Netto-Unterhalt (Unterhalt − Grundeinkommen) der
/// eigenen Wirtschaftsstationen; Sperrdurchsetzung. Stationsertrag = aus echtem Absatz (<see cref="PlayerStationSales"/>).
/// </summary>
public static class EmpireBalance
{
    public const float TicksPerHour = 3600f * SimClock.TicksPerSecond;

    /// <summary>Alle Sektoren der Spielerfraktion (Id-Reihenfolge).</summary>
    public static List<SectorBalance> All(Universe u)
    {
        var list = new List<SectorBalance>();
        if (u.PlayerFaction is not { } me) return list;
        foreach (var sec in u.Sectors.Values.OrderBy(s => s.Id.Value))
            if (sec.Owner == me) list.Add(Of(u, sec));
        return list;
    }

    public static SectorBalance Of(Universe u, Sector sec)
    {
        var r = u.Rules;
        float h = TicksPerHour;
        float share = Math.Clamp(r.TaxToTreasuryShare, 0f, 1f) * Math.Clamp(r.PlayerSectorTaxShare, 0f, 1f);
        float tax = 0f;
        var me = u.PlayerFaction;
        if (r.PlayerSectorTaxes && me is { } pf)
        {
            foreach (var ship in u.Ships.Values)
                if (ship.IsEconomyAi && ship.Sector == sec.Id && ship.Credits > r.WealthTaxFree && UpkeepSystem.TaxAuthority(u, ship) == pf)
                    tax += (ship.Credits - r.WealthTaxFree) * r.ShipWealthTax;
            foreach (var id in sec.Stations)
                if (u.Stations.TryGetValue(id, out var st) && !st.IsBuildSite && st.Owner != u.PlayerActor && st.Credits > 0f && UpkeepSystem.StationTaxAuthority(u, st) == pf)
                    tax += MathF.Max(0f, st.Credits - WorkingCapital.Of(u, st)) * r.StationWealthTax;
        }
        tax *= share * h;
        float gov = 0f, maint = 0f, up = 0f, income = 0f;
        int own = 0;
        foreach (var id in sec.Stations)
        {
            if (!u.Stations.TryGetValue(id, out var st) || st.IsBuildSite || st.Owner != u.PlayerActor || !u.StationTypes.TryGetValue(st.TypeKey, out var t)) continue;
            own++;
            if (t.Government != GovernmentKind.None)
            {
                if (!PlayerUpkeep.Unpaid(u, st)) gov += PlayerUpkeep.GovernmentPerTick(u, st, t) * h;
            }
            else
            {
                up += MathF.Max(0f, t.UpkeepPerTick - PlayerBuilding.IncomePerTick(u, st, t)) * h;
                income += PlayerStationSales.IncomePerTick(u, st, t) * h;
            }
            if (Maintenance.Applies(u, st))
                foreach (var (g, perDay) in Maintenance.NeedFor(u, st, t)) maint += perDay * u.Goods[g].BasePrice * h / MathF.Max(1f, r.DayTicks);
        }
        float locks = sec.Access.Enforced && sec.Access.EntryLevel > 0 && me is { } m2 && sec.Owner == m2 ? r.LockUpkeepPerLevel * sec.Access.EntryLevel * h : 0f;
        return new SectorBalance(sec.Id, sec.Name, tax, income, gov, maint, up, locks, own);
    }

    /// <summary>Kurze Zeile für die Anzeige: „Vesta: +21 400 / −18 900 Cr/h (Steuer 15 200, Stationen 6 200 | Behörden 4 100, Instandhaltung 9 800, Betrieb 5 000)“.</summary>
    public static string Line(SectorBalance b) =>
        $"{b.Name}: +{b.Income:0} / −{b.Costs:0} Cr/h (Steuer {b.Tax:0}, Stationen {b.StationIncome:0} | Behörden {b.Government:0}, Instandh. {b.Maintenance:0}, Betrieb {b.StationUpkeep:0}{(b.Locks > 0f ? $", Sperren {b.Locks:0}" : "")})";
}
