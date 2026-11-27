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

    /// <summary>
    /// Welle F (1): Anteil des Tagesbedarfs dieser Station — Regierungsstationen des Spielers mit <see cref="EconomyRules.PlayerGovernmentMaintenanceShare"/>
    /// (wie ihr Unterhalt mit <see cref="EconomyRules.PlayerGovernmentUpkeepShare"/>), sonst 1.
    /// </summary>
    public static float StationFactor(Universe u, Station st, StationType type) =>
        u.Rules.PlayerGovernmentMaintenanceShare > 0f && u.Rules.PlayerGovernmentMaintenanceShare < 1f && PlayerUpkeep.IsPlayerGovernment(u, st, type)
            ? u.Rules.PlayerGovernmentMaintenanceShare : 1f;

    /// <summary>Tagesbedarf dieser Station (Typbedarf × <see cref="StationFactor"/>).</summary>
    public static List<(GoodId Good, float PerDay)> NeedFor(Universe u, Station st, StationType type)
    {
        var need = NeedPerDay(u, type);
        float f = StationFactor(u, st, type);
        return f < 1f ? Scaled(need, f) : need;
    }

    /// <summary>Kopie des Bedarfs mit Faktor.</summary>
    public static List<(GoodId Good, float PerDay)> Scaled(List<(GoodId Good, float PerDay)> need, float f)
    {
        var list = new List<(GoodId, float)>(need.Count);
        foreach (var (g, d) in need) list.Add((g, d * f));
        return list;
    }

    /// <summary>Wird die Station instand gehalten? Fertig, kein Versteck, Typ mit Baukosten, Besitzer gehört zu einer Fraktion, Regel an.</summary>
    public static bool Applies(Universe u, Station st) =>
        !st.IsBuildSite && u.Rules.MaintenanceShare > 0f && u.StationTypes.TryGetValue(st.TypeKey, out var t) && !t.Hideout && t.BuildCost > 0f && t.Dockable   // Phase 30: Kleinstationen ohne Markt
        && u.FactionOf(st) is not null;

    /// <summary>Ist der Eintrag ein reiner Instandhaltungs-Eintrag (Lager ohne Rezept, nicht vom Typ als Lager/Export geführt, Ware ist Instandhaltungsmaterial)?</summary>
    public static bool IsPureEntry(Universe u, Station st, GoodId g)
    {
        if (!st.IsStockOnly(g) || !u.Rules.MaintenanceMaterials.ContainsKey(u.Goods[g].Key)) return false;
        return IsPureTypeEntry(u, st, g);
    }

    /// <summary>
    /// Phase 17: der Typ-Teil von <see cref="IsPureEntry"/> (keine Exportware, kein Typ-Lagerposten) — für Aufrufer, die schon wissen, dass der Eintrag
    /// ein Lager ohne Rezept ist und die Ware ein Instandhaltungsmaterial (Markt-Takt über die aufgelösten Instandhaltungswaren).
    /// </summary>
    public static bool IsPureTypeEntry(Universe u, Station st, GoodId g) =>
        IsPureTypeEntry(u.StationTypes.TryGetValue(st.TypeKey, out var t) ? t : null, g);

    /// <summary>Punkt 13: wie oben mit dem schon gesuchten Stationstyp (null = unbekannter Typ → false).</summary>
    public static bool IsPureTypeEntry(StationType? t, GoodId g)
    {
        if (t is null) return false;
        if (t.Export is { } x && x.Goods.Contains(g)) return false;
        foreach (var stock in t.Stocks) if (stock.Good == g) return false;
        return true;
    }

    /// <summary>
    /// Höchstgebot (× Basispreis) der reinen Instandhaltungs-Einträge: <see cref="EconomyRules.MaintenanceMaxBidFactor"/>; Ausbau Punkt 8: eine Station mit
    /// Verschleiß (Zustand unter 1) bietet bis <see cref="EconomyRules.WearMaxBidFactor"/>, damit Händler ihr Material auch gegen die Fabriken bringen.
    /// </summary>
    public static float MaxBidFactor(Universe u, Station st) =>
        u.Rules.StationWear && st.Integrity < 1f ? MathF.Max(u.Rules.MaintenanceMaxBidFactor, u.Rules.WearMaxBidFactor) : u.Rules.MaintenanceMaxBidFactor;

    /// <summary>
    /// Wunschbestand der reinen Instandhaltungs-Einträge in Tagesbedarfen: <see cref="EconomyRules.MaintenanceStockDays"/>; Ausbau Punkt 8: mit Verschleiß
    /// mindestens <see cref="EconomyRules.WearStockDays"/> (eine Lieferung von zwei Tagesbedarfen lohnt im großen Maßstab keine Fahrt).
    /// </summary>
    public static float StockDays(Universe u, Station st) =>
        u.Rules.StationWear && st.Integrity < 1f ? MathF.Max(u.Rules.MaintenanceStockDays, u.Rules.WearStockDays) : u.Rules.MaintenanceStockDays;

    /// <summary>
    /// Wunschbestand eines reinen Instandhaltungs-Eintrags: Tagesbedarf × <see cref="StockDays"/>. Ausbau Punkt 8: ist die Station wieder einwandfrei, sinkt
    /// der Wunsch nur mit dem Lager zurück (höchstens bis zum erhöhten Wunsch) — sonst stünde der Restbestand der Reparaturlieferung als Überschuss am unteren
    /// Preisdeckel. Ohne Verschleißregel genau die alte Regel.
    /// </summary>
    public static float PureTarget(Universe u, Station st, float perDay, float amount)
    {
        float t = perDay * StockDays(u, st);
        if (u.Rules.StationWear && st.Integrity >= 1f)
            t = MathF.Max(t, MathF.Min(perDay * MathF.Max(u.Rules.MaintenanceStockDays, u.Rules.WearStockDays), amount));
        return t;
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
    readonly SortedDictionary<int, float> _emergency = new();   // Ausbau Punkt 8: Notinstandhaltung je Fraktion in diesem Schritt
    EconomyRules? _rulesFor;

    public void Step(Universe u, int dt)
    {
        if (u.Rules.MaintenanceShare <= 0f) return;
        if (!ReferenceEquals(_rulesFor, u.Rules)) { _needs.Clear(); _rulesFor = u.Rules; }
        var paid = new SortedDictionary<int, (float Paid, float Unpaid)>();
        var parts = new Dictionary<int, FactionViability.SpendableParts>();
        bool touched = false;
        float day = MathF.Max(1f, u.Rules.DayTicks);
        foreach (var st in u.StationsById().ToList())
        {
            if (!Maintenance.Applies(u, st) || u.FactionOf(st) is not { } faction) continue;
            var type = u.StationTypes[st.TypeKey];
            if (!_needs.TryGetValue(type.Key, out var need)) _needs[type.Key] = need = Maintenance.NeedPerDay(u, type);
            if (Maintenance.StationFactor(u, st, type) is var mf && mf < 1f) need = Maintenance.Scaled(need, mf);   // Welle F (1): Spielerbehörden mit Anteil
            float value = 0f;
            foreach (var (g, perDay) in need)
            {
                if (!st.Market.TryGetValue(g, out var e))
                {
                    e = st.Stock(g);
                    e.Amount = 0f;
                    e.Target = MathF.Max(1f, perDay * Maintenance.StockDays(u, st));
                    touched = true;
                }
                else if (Maintenance.IsPureEntry(u, st, g)) e.Target = MathF.Max(1f, Maintenance.PureTarget(u, st, perDay, e.Amount));
                float due = MathF.Min(perDay, perDay * dt / day + st.MaintenanceBacklog.GetValueOrDefault(g));
                float take = MathF.Min(due, MathF.Max(0f, e.Amount));
                e.Amount -= take;
                float rest = due - take;
                if (rest > 0.0001f) st.MaintenanceBacklog[g] = rest; else st.MaintenanceBacklog.Remove(g);
                float price = e.Price > 0f ? e.Price : u.Goods[g].BasePrice;
                value += take * MathF.Min(price, u.Goods[g].BasePrice * MathF.Max(1f, Maintenance.MaxBidFactor(u, st)));
            }
            // Ausbau Punkt 8: Notinstandhaltung — eine KI-Fraktion kauft den offenen Rückstand einer stark verschlissenen Station zum Aufpreis (Geldsenke)
            if (StationWear.EmergencyDue(u, st, need) && u.IsNpcFaction(faction) && u.Actors.TryGetValue(faction, out var ea))
            {
                if (!parts.TryGetValue(ea.Id.Value, out var epa)) parts[ea.Id.Value] = epa = FactionViability.PartsOf(u, ea);
                float cost = StationWear.EmergencyCost(u, st);
                if (cost > 0f && FactionViability.Spendable(epa, ea) >= cost)
                {
                    ea.Treasury.Credits -= cost;
                    st.MaintenanceBacklog.Clear();
                    _emergency[faction.Value] = _emergency.GetValueOrDefault(faction.Value) + cost;
                    u.Events.Publish(new StationWearEmergency(st.Id, ea.Id, cost));
                }
            }
            // Ausbau Punkt 8: Verfall bei offenem Rückstand, Reparatur mit Material (erstattet wie Instandhaltung) — nach der Notinstandhaltung, damit eine
            // bezahlte Station in diesem Schritt nicht weiter verfällt (sonst kröche sie unter die Schwelle)
            value += StationWear.Step(u, st, need, dt);
            if (value <= 0f) continue;
            // Kasse erstattet der Station das verbrauchte Material; reicht sie nicht, trägt die Station den Rest selbst
            // Phase 22: freiwillige Ausgabe — nur aus dem Teil der Kasse über der Betriebsreserve (FactionViability.Spendable)
            // Punkt 13: Reserve und Ersatzkosten je Akteur einmal je Schritt (in dieser Schleife fließen nur Kassen und Lager)
            float fromTreasury = 0f;
            if (u.Actors.TryGetValue(faction, out var actor))
            {
                if (!parts.TryGetValue(actor.Id.Value, out var pa)) parts[actor.Id.Value] = pa = FactionViability.PartsOf(u, actor);
                fromTreasury = MathF.Min(value, FactionViability.Spendable(pa, actor));
            }
            if (actor is not null) actor.Treasury.Credits -= fromTreasury;
            st.Credits += fromTreasury;
            var cur = paid.GetValueOrDefault(faction.Value);
            paid[faction.Value] = (cur.Paid + fromTreasury, cur.Unpaid + value - fromTreasury);
        }
        if (touched) u.TouchStructure();
        foreach (var (f, (p, unpaid)) in paid) u.Events.Publish(new TreasuryUpkeep(new ActorId(f), "maintenance", p, unpaid));
        foreach (var (f, c) in _emergency) u.Events.Publish(new TreasuryUpkeep(new ActorId(f), "wear_emergency", c, 0f));   // Ausbau Punkt 8
        _emergency.Clear();
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
        var stations = u.StationsById().Where(s => !s.IsBuildSite && s.Market.Count > 0).ToList();
        foreach (var f in u.NpcFactionIds())   // Runde 38: KI-Fraktionen
        {
            if (Bandits.IsEstablished(u, f)) continue;   // Ausbau Punkt 17: die Banditen stützen ihre Stationen selbst (oder lassen sie verfallen)
            if (!u.Actors.TryGetValue(f, out var actor)) continue;
            float spent = 0f;
            // Punkt 13: Reserve und Ersatzkosten ändern sich in dieser Schleife nicht (nur Kassen fließen) — einmal je Fraktion statt je Station
            var parts = FactionViability.PartsOf(u, actor);
            foreach (var st in stations)
            {
                // Phase 22: freiwillige Ausgabe — Rücklage der Regel und Betriebsreserve der Fraktion bleiben
                float budget = MathF.Min(actor.Treasury.Credits - u.Rules.SubsidyReserve, FactionViability.Spendable(parts, actor));
                if (budget <= 0f) break;
                if (u.FactionOf(st) != f || !Eligible(u, st)) continue;
                float floor = WorkingCapital.Of(u, st) * u.Rules.SubsidyFloorShare;
                if (st.Credits >= floor) continue;
                float amount = MathF.Min(MathF.Min(floor - st.Credits, cap), budget);
                if (amount <= 0f) continue;
                actor.Treasury.Credits -= amount;
                st.Credits += amount;
                Profitability.Transfer(st.Profit, amount);   // Phase 22: Subvention ist kein Geschäftsergebnis
                spent += amount;
                u.Events.Publish(new StationSubsidized(st.Id, actor.Id, amount));
            }
            if (spent > 0f) u.Events.Publish(new TreasuryUpkeep(actor.Id, "subsidy", spent, 0f));
        }
    }

    /// <summary>
    /// Darf die Station subventioniert werden? Regierungsstationen und Wachen immer (sie haben seit der Instandhaltung einen Markt und müssen ihre Lieferungen
    /// bezahlen können), sonst geschützte, kritische Stationen, Endverbraucher (Kolonie) und Exportsenken; Verstecke nie. Phase 19: eine nur kritische Station,
    /// die dauerhaft kaum produziert und deren Produkte nicht knapp sind (<see cref="StationReview.Useless"/>), bekommt nichts mehr.
    /// </summary>
    public static bool Eligible(Universe u, Station st) =>
        !StationProtection.IsHideout(u, st) && st.Dockable   // Phase 30: Kleinstationen haben keine Kasse zu stützen
        && !FirmSeats.IsFirmGovernment(u, st)   // Ausbau Punkt 9: Sitz und Büro einer Firma trägt die Firmenkasse, nicht ihre Mitgliedsfraktion
        && (StationProtection.IsOfficial(u, st) || st.BankruptcyProtected || MarketSystem.IsFinalConsumer(st) || ExportRules.ConfigOf(u, st) is not null
            || (StationCriticality.IsCritical(u, st) && !StationReview.Useless(u, st)));   // Phase 19: dauerhaft nutzlose Stationen nicht ewig stützen
}
