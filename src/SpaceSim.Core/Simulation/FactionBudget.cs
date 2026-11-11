using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Phase 15: Betriebskapital einer Station — was sie braucht, um ihre Einkaufsseite einmal zu füllen: Σ Wunschbestand × Basispreis aller Waren, die sie
/// annimmt (Rezeptzutaten, Lager ohne Rezept, Exportwaren, Instandhaltung; reine Produkte nicht), × <see cref="EconomyRules.StationWorkingCapitalShare"/>.
/// Die Vermögensabgabe (<see cref="UpkeepSystem"/>) trifft nur Geld darüber, die Subvention (<see cref="SubsidySystem"/>) füllt darunter auf.
/// </summary>
public static class WorkingCapital
{
    public static float Of(Universe u, Station st)
    {
        if (st.IsBuildSite || u.Rules.StationWorkingCapitalShare <= 0f) return 0f;
        float sum = 0f;
        foreach (var (g, e) in st.Market)
        {
            if (st.Produces(g) && !st.Consumes(g)) continue;
            sum += MathF.Max(0f, e.Target) * u.Goods[g].BasePrice;
        }
        return sum * u.Rules.StationWorkingCapitalShare;
    }
}

/// <summary>
/// Phase 15: Stationsinstandhaltung (dauerhafter Materialbedarf, seit Phase 8 offen). Jede fertige Station einer Fraktion (oder eines ihrer Mitglieder)
/// verbraucht je Tag Material im Wert von Baukosten × <see cref="EconomyRules.MaintenanceShare"/>, aufgeteilt nach
/// <see cref="EconomyRules.MaintenanceMaterials"/> (Stahl, Hüllenplatten, Elektronik). Führt die Station die Ware nicht, bekommt sie einen reinen
/// Instandhaltungs-Eintrag (Lager ohne Rezept, Wunschbestand <see cref="EconomyRules.MaintenanceStockDays"/> Tagesbedarfe, Gebot höchstens
/// <see cref="EconomyRules.MaintenanceMaxBidFactor"/> × Basispreis): Händler liefern über den normalen Handel, die Station bezahlt sie, und die Kasse der
/// Fraktion erstattet der Station das verbrauchte Material zum Marktpreis. Fehlt Material, wächst nur die offene Instandhaltung
/// (<see cref="Station.MaintenanceBacklog"/>, höchstens ein Tagesbedarf) — kein Zerfall.
/// </summary>
public static class Maintenance
{
    /// <summary>Tagesbedarf eines Stationstyps (Ware, Einheiten je Tag) nach der Regel; leer ohne Baukosten oder Material.</summary>
    public static List<(GoodId Good, float PerDay)> NeedPerDay(Universe u, StationType type)
    {
        var list = new List<(GoodId, float)>();
        float value = type.BuildCost * u.Rules.MaintenanceShare;
        if (value <= 0f || type.Hideout || type.Key == u.Rules.ConstructionSiteType) return list;
        float total = 0f;
        foreach (var (key, w) in u.Rules.MaintenanceMaterials.OrderBy(k => k.Key, StringComparer.Ordinal))
            if (w > 0f && u.FindGood(key) is not null) total += w;
        if (total <= 0f) return list;
        foreach (var (key, w) in u.Rules.MaintenanceMaterials.OrderBy(k => k.Key, StringComparer.Ordinal))
            if (w > 0f && u.FindGood(key) is { } g && g.BasePrice > 0f)
                list.Add((g.Id, value * w / total / g.BasePrice));
        return list.OrderBy(x => x.Item1.Value).ToList();
    }

    /// <summary>Wird die Station instand gehalten? Fertig, kein Versteck, Typ mit Baukosten, Besitzer gehört zu einer Fraktion, Regel an.</summary>
    public static bool Applies(Universe u, Station st) =>
        !st.IsBuildSite && u.Rules.MaintenanceShare > 0f && u.StationTypes.TryGetValue(st.TypeKey, out var t) && !t.Hideout && t.BuildCost > 0f
        && u.FactionOf(st) is not null;

    /// <summary>Ist der Eintrag ein reiner Instandhaltungs-Eintrag (Lager ohne Rezept, nicht vom Typ als Lager/Export geführt, Ware ist Instandhaltungsmaterial)?</summary>
    public static bool IsPureEntry(Universe u, Station st, GoodId g)
    {
        if (!st.IsStockOnly(g) || !u.Rules.MaintenanceMaterials.ContainsKey(u.Goods[g].Key)) return false;
        if (!u.StationTypes.TryGetValue(st.TypeKey, out var t)) return false;
        if (t.Export is { } x && x.Goods.Contains(g)) return false;
        foreach (var stock in t.Stocks) if (stock.Good == g) return false;
        return true;
    }

    /// <summary>Offene Instandhaltung der Station zum Basispreis (Statuswert für Anzeige und Messung).</summary>
    public static float BacklogValue(Universe u, Station st) => st.MaintenanceBacklog.Sum(kv => kv.Value * u.Goods[kv.Key].BasePrice);

    /// <summary>Anzeigezeile für die Stationsinfo: „gedeckt“ bzw. „offen: 2 Stahl, 1 Elektronik“; null, wenn die Station nicht instand gehalten wird.</summary>
    public static string? StatusText(Universe u, Station st)
    {
        if (!Applies(u, st)) return null;
        var open = st.MaintenanceBacklog.Where(kv => kv.Value >= 0.05f).OrderBy(kv => kv.Key.Value).ToList();
        return open.Count == 0 ? "gedeckt" : "offen: " + string.Join(", ", open.Select(kv => $"{kv.Value:0.#} {u.Goods[kv.Key].Name}"));
    }
}

/// <summary>Phase 15: Instandhaltung ausführen (Aktoren-Modul, Intervall 50): Einträge anlegen, Material verbrauchen, Kasse erstattet; siehe <see cref="Maintenance"/>.</summary>
public sealed class MaintenanceSystem : ISimSystem
{
    readonly Dictionary<string, List<(GoodId Good, float PerDay)>> _needs = new();
    EconomyRules? _rulesFor;

    public void Step(Universe u, int dt)
    {
        if (u.Rules.MaintenanceShare <= 0f) return;
        if (!ReferenceEquals(_rulesFor, u.Rules)) { _needs.Clear(); _rulesFor = u.Rules; }
        var paid = new SortedDictionary<int, (float Paid, float Unpaid)>();
        bool touched = false;
        float day = MathF.Max(1f, u.Rules.DayTicks);
        foreach (var st in u.Stations.Values.OrderBy(s => s.Id.Value).ToList())
        {
            if (!Maintenance.Applies(u, st) || u.FactionOf(st) is not { } faction) continue;
            var type = u.StationTypes[st.TypeKey];
            if (!_needs.TryGetValue(type.Key, out var need)) _needs[type.Key] = need = Maintenance.NeedPerDay(u, type);
            float value = 0f;
            foreach (var (g, perDay) in need)
            {
                if (!st.Market.TryGetValue(g, out var e))
                {
                    e = st.Stock(g);
                    e.Amount = 0f;
                    e.Target = MathF.Max(1f, perDay * u.Rules.MaintenanceStockDays);
                    touched = true;
                }
                else if (Maintenance.IsPureEntry(u, st, g)) e.Target = MathF.Max(1f, perDay * u.Rules.MaintenanceStockDays);
                float due = MathF.Min(perDay, perDay * dt / day + st.MaintenanceBacklog.GetValueOrDefault(g));
                float take = MathF.Min(due, MathF.Max(0f, e.Amount));
                e.Amount -= take;
                float rest = due - take;
                if (rest > 0.0001f) st.MaintenanceBacklog[g] = rest; else st.MaintenanceBacklog.Remove(g);
                float price = e.Price > 0f ? e.Price : u.Goods[g].BasePrice;
                value += take * MathF.Min(price, u.Goods[g].BasePrice * MathF.Max(1f, u.Rules.MaintenanceMaxBidFactor));
            }
            if (value <= 0f) continue;
            // Kasse erstattet der Station das verbrauchte Material; reicht sie nicht, trägt die Station den Rest selbst
            float fromTreasury = u.Actors.TryGetValue(faction, out var actor) ? MathF.Min(value, MathF.Max(0f, actor.Treasury.Credits)) : 0f;
            if (actor is not null) actor.Treasury.Credits -= fromTreasury;
            st.Credits += fromTreasury;
            var cur = paid.GetValueOrDefault(faction.Value);
            paid[faction.Value] = (cur.Paid + fromTreasury, cur.Unpaid + value - fromTreasury);
        }
        if (touched) u.TouchStructure();
        foreach (var (f, (p, unpaid)) in paid) u.Events.Publish(new TreasuryUpkeep(new ActorId(f), "maintenance", p, unpaid));
    }
}

/// <summary>
/// Phase 15: Subvention notleidender Stationen (Aktoren-Modul, Intervall 50). Die Kasse einer Fraktion stützt ihre (und ihrer Mitglieder) fertigen
/// Stationen, die geschützt, kritisch (<see cref="StationCriticality.IsCritical"/>), Endverbraucher (Kolonie), Exportsenke oder Regierungsstation sind, sobald deren Kasse
/// unter <see cref="EconomyRules.SubsidyFloorShare"/> × Betriebskapital fällt: aufgefüllt bis dorthin, höchstens <see cref="EconomyRules.SubsidyPerDay"/> je
/// Station und Tag, nie unter die Rücklage <see cref="EconomyRules.SubsidyReserve"/>. So fließt Geld, das die Vermögensabgabe einsammelt, in die Ketten
/// zurück (vorher horteten die Kassen ≈ 90 % des Geldes, während Hütten und Fabriken ihre Zutaten nicht bezahlen konnten).
/// </summary>
public sealed class SubsidySystem : ISimSystem
{
    public void Step(Universe u, int dt)
    {
        if (u.Rules.SubsidyPerDay <= 0f || u.Rules.SubsidyFloorShare <= 0f) return;
        float cap = u.Rules.SubsidyPerDay * dt / MathF.Max(1f, u.Rules.DayTicks);
        var stations = u.Stations.Values.Where(s => !s.IsBuildSite && s.Market.Count > 0).OrderBy(s => s.Id.Value).ToList();
        foreach (var f in u.Factions.Keys.OrderBy(k => k.Value))
        {
            if (!u.Actors.TryGetValue(f, out var actor)) continue;
            float spent = 0f;
            foreach (var st in stations)
            {
                float budget = actor.Treasury.Credits - u.Rules.SubsidyReserve;
                if (budget <= 0f) break;
                if (u.FactionOf(st) != f || !Eligible(u, st)) continue;
                float floor = WorkingCapital.Of(u, st) * u.Rules.SubsidyFloorShare;
                if (st.Credits >= floor) continue;
                float amount = MathF.Min(MathF.Min(floor - st.Credits, cap), budget);
                if (amount <= 0f) continue;
                actor.Treasury.Credits -= amount;
                st.Credits += amount;
                spent += amount;
                u.Events.Publish(new StationSubsidized(st.Id, actor.Id, amount));
            }
            if (spent > 0f) u.Events.Publish(new TreasuryUpkeep(actor.Id, "subsidy", spent, 0f));
        }
    }

    /// <summary>
    /// Darf die Station subventioniert werden? Regierungsstationen und Wachen immer (sie haben seit der Instandhaltung einen Markt und müssen ihre Lieferungen
    /// bezahlen können), sonst geschützte, kritische Stationen, Endverbraucher (Kolonie) und Exportsenken; Verstecke nie.
    /// </summary>
    public static bool Eligible(Universe u, Station st) =>
        !StationProtection.IsHideout(u, st)
        && (StationProtection.IsOfficial(u, st) || st.BankruptcyProtected || MarketSystem.IsFinalConsumer(st) || ExportRules.ConfigOf(u, st) is not null
            || StationCriticality.IsCritical(u, st));
}
