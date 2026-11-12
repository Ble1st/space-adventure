using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>Phase 22: Art eines Verstoßes gegen die Handlungsfähigkeit einer Fraktion.</summary>
public enum ViabilityIssueKind { TreasuryBelowReserve, PoliceBelowMinimum, BuildersBelowMinimum, NoSeat, NoHideout }

/// <summary>Phase 22: ein Verstoß gegen die Handlungsfähigkeit (Art, Anzeigetext).</summary>
public sealed record ViabilityIssue(ViabilityIssueKind Kind, string Text);

/// <summary>
/// Phase 22: Eine Fraktion macht sich nie handlungsunfähig. „Handlungsfähig“ als prüfbare Invarianten (<see cref="Check"/>, rein):
/// <list type="bullet">
/// <item>Die Kasse hält die Betriebsreserve <see cref="Reserve"/> = Pflichtunterhalt je Tick (Regierungsstationen netto, Mindestpolizei, Mindestbauschiffe) ×
/// <see cref="EconomyRules.FactionReserveTicks"/>. Freiwillige Ausgaben (Subvention, Instandhaltungs-Erstattung, Polizei- und Bauschiffkauf über dem Mindestbestand,
/// Schiffskauf aus der Kasse, Stationsinvestitionen, Forschung, Sperren) dürfen nur <see cref="Spendable"/> ausgeben; der Pflichtunterhalt darf in die Reserve
/// greifen (Nachrangiges wird gekürzt statt Pflicht unbezahlt zu lassen).</item>
/// <item>Mindestbestand: <see cref="EconomyRules.PoliceMinPerFaction"/> Polizeischiffe (bei aktiver Sicherheit), <see cref="EconomyRules.MinBuildersPerFaction"/>
/// Bauschiffe, ein Sitz (Hauptquartier oder Firmensitz). Abbau geht nie darunter; fehlt etwas durch Zerstörung, hat der Ersatz Vorrang (Kauf ohne Reserve),
/// und scheitert er, greift der Notkauf (<see cref="FactionViabilitySystem"/>).</item>
/// <item>Stilllegungen nehmen einer Fraktion nie die letzte eigene Station eines Typs, dessen Produkte ihre eigenen Stationen brauchen (<see cref="KeepsOwnSupply"/>),
/// und nie einem Kartenteil seinen letzten Hersteller (<see cref="StationCriticality"/>).</item>
/// </list>
/// Gesetzlose: dürfen dezimiert werden (Neuanfang aus dem Versteck), verletzt ist nur „kein Versteck“. Aufgestiegene Einzelakteure haben keine Invarianten
/// (sie dürfen untergehen — Grund b).
/// </summary>
public static class FactionViability
{
    /// <summary>Pflichtunterhalt je Tick: Regierungsstationen der Fraktion (Unterhalt − Grundeinkommen) + Mindestpolizei + Mindestbauschiffe.</summary>
    public static float ObligatoryPerTick(Universe u, FactionId f)
    {
        var r = u.Rules;
        float sum = 0f;
        foreach (var st in u.StationsById())
            if (!st.IsBuildSite && st.Owner == (ActorId)f && u.StationTypes.TryGetValue(st.TypeKey, out var t) && t.Government != GovernmentKind.None)
                sum += MathF.Max(0f, t.UpkeepPerTick - t.IncomePerTick);
        if (r.SecurityActive) sum += r.PoliceMinPerFaction * r.PoliceUpkeepPerTick;
        sum += r.MinBuildersPerFaction * r.BuilderUpkeepPerTick;
        return sum;
    }

    /// <summary>Betriebsreserve der Kasse (Credits); 0 ohne Regel oder für Akteure ohne Fraktion.</summary>
    public static float Reserve(Universe u, FactionId f) =>
        u.Rules.FactionKeepsReserve && u.Factions.ContainsKey(f) && u.Actors.TryGetValue(f, out var a) && a.Kind != ActorKind.Outlaw
            ? ObligatoryPerTick(u, f) * MathF.Max(0, u.Rules.FactionReserveTicks) : 0f;

    /// <summary>Reserve des Akteurs, wenn er eine Fraktion ist (sonst 0).</summary>
    public static float ReserveOf(Universe u, Actor a) => a.IsFaction(u) ? Reserve(u, new FactionId(a.Id.Value)) : 0f;

    /// <summary>
    /// Was eine freiwillige Ausgabe aus der Kasse höchstens nehmen darf: Kasse − Reserve − Ersatzkosten eines fehlenden Mindestbestands
    /// (<see cref="ReplacementCost"/>), mindestens 0. Für Akteure ohne Fraktion die ganze Kasse.
    /// </summary>
    public static float Spendable(Universe u, Actor a) =>
        a.IsFaction(u) ? MathF.Max(0f, a.Treasury.Credits - ReserveOf(u, a) - ReplacementCost(u, new FactionId(a.Id.Value))) : MathF.Max(0f, a.Treasury.Credits);

    /// <summary>
    /// Was der Ersatz eines fehlenden Mindestbestands ungefähr kostet (Material des Modells zum Basispreis, Bauschiff zuzüglich Startkapital); 0, wenn nichts fehlt
    /// oder die Regel aus ist. Freiwillige Ausgaben lassen diesen Betrag in der Kasse — der Ersatz hat Vorrang.
    /// </summary>
    public static float ReplacementCost(Universe u, FactionId f)
    {
        if (!u.Rules.FactionKeepsReserve) return 0f;
        float cost = 0f;
        if (PoliceRequired(u) && SecuritySystem.PoliceOf(u, f).Count < u.Rules.PoliceMinPerFaction) cost += BaseValue(u, u.Rules.PoliceModel);
        if (BuildersRequired(u) && FactionBuilderSystem.Of(u, f).Count < u.Rules.MinBuildersPerFaction)
        {
            var model = u.ShipModels.Values.Where(m => m.Jobs.Contains("build")).OrderBy(m => m.Key, StringComparer.Ordinal).FirstOrDefault();
            if (model is not null) cost += BaseValue(u, model.Key) + u.Rules.NewShipStartCredits;
        }
        return cost;
    }

    static float BaseValue(Universe u, string modelKey)
    {
        if (!u.ShipModels.TryGetValue(modelKey, out var model) || ShipPurchase.Materials(u, model) is not { } needs) return 0f;
        float v = 0f;
        foreach (var n in needs) v += n.Units * u.Goods[n.Good].BasePrice;
        return v;
    }

    /// <summary>Fehlt der Fraktion etwas vom Mindestbestand (Polizei oder Bauschiff)? Dann hat der Ersatz Vorrang vor allen freiwilligen Ausgaben.</summary>
    public static bool MinimumMissing(Universe u, FactionId f) =>
        (PoliceRequired(u) && SecuritySystem.PoliceOf(u, f).Count < u.Rules.PoliceMinPerFaction)
        || (BuildersRequired(u) && FactionBuilderSystem.Of(u, f).Count < u.Rules.MinBuildersPerFaction);

    static bool PoliceRequired(Universe u) =>
        u.Rules.SecurityActive && u.Rules.PoliceMinPerFaction > 0 && u.ShipModels.ContainsKey(u.Rules.PoliceModel);

    static bool BuildersRequired(Universe u) =>
        u.Rules.MinBuildersPerFaction > 0 && u.Rules.CommissionBuilders && u.ShipModels.Values.Any(m => m.Jobs.Contains("build"));

    /// <summary>Verstöße gegen die Handlungsfähigkeit (leer = handlungsfähig). Rein.</summary>
    public static List<ViabilityIssue> Check(Universe u, ActorId actor)
    {
        var list = new List<ViabilityIssue>();
        if (!u.Actors.TryGetValue(actor, out var a)) return list;
        if (a.Kind == ActorKind.Outlaw)
        {
            if (!u.Stations.Values.Any(st => st.Owner == actor && StationProtection.IsHideout(u, st)))
                list.Add(new ViabilityIssue(ViabilityIssueKind.NoHideout, "kein Versteck"));
            return list;
        }
        if (!a.IsFaction(u)) return list;
        var f = new FactionId(actor.Value);
        float reserve = Reserve(u, f);
        if (reserve > 0f && a.Treasury.Credits < reserve)
            list.Add(new ViabilityIssue(ViabilityIssueKind.TreasuryBelowReserve, $"Kasse {a.Treasury.Credits:0} unter Rücklage {reserve:0} Cr"));
        if (PoliceRequired(u) && SecuritySystem.PoliceOf(u, f).Count is var pc && pc < u.Rules.PoliceMinPerFaction)
            list.Add(new ViabilityIssue(ViabilityIssueKind.PoliceBelowMinimum, $"Polizei {pc}/{u.Rules.PoliceMinPerFaction}"));
        if (BuildersRequired(u) && FactionBuilderSystem.Of(u, f).Count is var bc && bc < u.Rules.MinBuildersPerFaction)
            list.Add(new ViabilityIssue(ViabilityIssueKind.BuildersBelowMinimum, $"Bauschiffe {bc}/{u.Rules.MinBuildersPerFaction}"));
        if (!u.Stations.Values.Any(st => !st.IsBuildSite && st.Owner == actor && StationProtection.KindOf(u, st) is GovernmentKind.Headquarters or GovernmentKind.CompanySeat))
            list.Add(new ViabilityIssue(ViabilityIssueKind.NoSeat, "kein Hauptquartier/Sitz"));
        return list;
    }

    /// <summary>Anzeigezeile: „handlungsfähig“ bzw. „nicht handlungsfähig: …“; null für Akteure ohne Invarianten.</summary>
    public static string? Text(Universe u, ActorId actor)
    {
        if (!u.Actors.TryGetValue(actor, out var a) || !(a.Kind == ActorKind.Outlaw || a.IsFaction(u))) return null;
        var issues = Check(u, actor);
        return issues.Count == 0 ? "handlungsfähig" : "nicht handlungsfähig: " + string.Join(", ", issues.Select(i => i.Text));
    }

    /// <summary>
    /// Darf eine Stilllegung diese Station nehmen, ohne die eigene Versorgung der Besitzerfraktion zu kappen? Nein, wenn sie die letzte fertige Station ihres Typs
    /// in der Fraktion ist und eine andere Station der Fraktion eines ihrer Produkte verbraucht. true = Stilllegung erlaubt. Rein.
    /// </summary>
    public static bool KeepsOwnSupply(Universe u, Station st)
    {
        if (u.FactionOf(st) is not { } f) return true;
        bool another = u.Stations.Values.Any(o => o.Id != st.Id && !o.IsBuildSite && o.TypeKey == st.TypeKey && u.FactionOf(o) == f);
        if (another) return true;
        var outputs = st.Production.SelectMany(r => r.Outputs.Keys).Distinct().ToList();
        foreach (var o in u.Stations.Values)
            if (o.Id != st.Id && !o.IsBuildSite && u.FactionOf(o) == f && outputs.Any(o.Consumes)) return false;
        return true;
    }

    /// <summary>
    /// Notkauf (letzter Ausweg): ein Schiff des Modells entsteht ohne Werftmaterial an der Heimatbasis der Fraktion; die Kasse zahlt höchstens
    /// <see cref="EconomyRules.ViabilityRestoreShare"/> des Materialwerts zum Basispreis, der Rest wird erlassen (neues Geld, <see cref="FactionRestored"/>).
    /// null = kein Modell/keine Basis.
    /// </summary>
    public static Ship? EmergencyCommission(Universe u, FactionId f, string modelKey, ShipRole role)
    {
        var r = u.Rules;
        if (!u.Actors.TryGetValue(f, out var actor) || !u.ShipModels.TryGetValue(modelKey, out var model) || !u.ShipClasses.TryGetValue(model.ClassKey, out var cls)) return null;
        var home = SecuritySystem.HomeBaseFor(u, f, null) ?? u.StationsById().FirstOrDefault(st => !st.IsBuildSite && st.Owner == (ActorId)f);
        if (home is null) return null;
        float value = 0f;
        if (ShipPurchase.Materials(u, model) is { } needs)
            foreach (var n in needs) value += n.Units * u.Goods[n.Good].BasePrice;
        float paid = MathF.Min(value * MathF.Max(0f, r.ViabilityRestoreShare), MathF.Max(0f, actor.Treasury.Credits));
        actor.Treasury.Credits -= paid;
        var ship = ShipPurchase.Create(u, u.NewShipId(), model.Key, cls.Slots, model.Fit, role, Controller.Ai, actor.Id, home, 0f);
        ship.HomeBase = home.Id;
        ship.JobSince = u.Tick;
        u.AddShip(ship);
        actor.LastRestoreTick = u.Tick;
        if (role == ShipRole.Police) actor.LastForceTick = u.Tick;
        if (paid > 0f) u.Events.Publish(new TreasuryUpkeep(actor.Id, "restore", paid, 0f));
        u.Events.Publish(new FactionRestored(f, ship.Id, role, paid, value - paid));
        return ship;
    }
}

/// <summary>
/// Phase 22: Hält den Mindestbestand jeder Fraktion (Viabilitäts-Modul, Intervall 100). Der normale Ersatzkauf läuft in <see cref="SecuritySystem"/> und
/// <see cref="FactionBuilderSystem"/> mit Vorrang (ohne Reserve); fehlt danach noch etwas (keine Werft mit Material erreichbar, Kasse leer) und ist die Bremse
/// <see cref="EconomyRules.ViabilityRestoreCooldown"/> abgelaufen, entsteht ein Schiff per Notkauf (<see cref="FactionViability.EmergencyCommission"/>).
/// </summary>
public sealed class FactionViabilitySystem : ISimSystem
{
    public const int Interval = 100;

    public void Step(Universe u, int dt)
    {
        var r = u.Rules;
        if (r.ViabilityRestoreShare <= 0f) return;
        foreach (var f in u.Factions.Keys.OrderBy(k => k.Value).ToList())
        {
            if (!u.Actors.TryGetValue(f, out var actor) || actor.Kind == ActorKind.Outlaw) continue;
            if (u.Tick - actor.LastRestoreTick < r.ViabilityRestoreCooldown) continue;
            if (r.SecurityActive && r.PoliceMinPerFaction > 0 && SecuritySystem.PoliceOf(u, f).Count < r.PoliceMinPerFaction
                && SecuritySystem.Commission(u, f, priority: true) is null
                && FactionViability.EmergencyCommission(u, f, r.PoliceModel, ShipRole.Police) is not null) continue;
            if (r.MinBuildersPerFaction > 0 && r.CommissionBuilders && FactionBuilderSystem.Of(u, f).Count < r.MinBuildersPerFaction)
            {
                var home = SecuritySystem.HomeBaseFor(u, f, null) ?? u.StationsById().FirstOrDefault(st => !st.IsBuildSite && st.Owner == (ActorId)f);
                if (home is null || ConstructionRules.CommissionBuilder(u, f, home.Sector, default, priority: true) is not null) continue;
                var model = u.ShipModels.Values.Where(m => m.Jobs.Contains("build")).OrderBy(m => m.Key, StringComparer.Ordinal).FirstOrDefault();
                if (model is not null) FactionViability.EmergencyCommission(u, f, model.Key, ShipRole.Builder);
            }
        }
    }
}
