using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>Phase 22: Art eines Verstoßes gegen die Handlungsfähigkeit einer Fraktion.</summary>
public enum ViabilityIssueKind { TreasuryBelowReserve, PoliceBelowMinimum, BuildersBelowMinimum, NoSeat, NoHideout, CannotHunt }

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
        // Punkt 13: nur die Kandidaten (Stationen mit Regierungstyp, nach Id) statt aller Stationen; Bedingungen unverändert live geprüft
        foreach (var st in u.GovernmentCandidates())
            if (!st.IsBuildSite && st.Owner == (ActorId)f && u.StationTypes.TryGetValue(st.TypeKey, out var t) && t.Government != GovernmentKind.None)
                sum += MathF.Max(0f, t.UpkeepPerTick - t.IncomePerTick);
        if (r.SecurityActive) sum += r.PoliceMinPerFaction * r.PoliceUpkeepPerTick;
        sum += r.MinBuildersPerFaction * r.BuilderUpkeepPerTick;
        return sum;
    }

    /// <summary>Betriebsreserve der Kasse (Credits); 0 ohne Regel oder für Akteure ohne Fraktion.</summary>
    public static float Reserve(Universe u, FactionId f) =>
        u.Rules.FactionKeepsReserve && u.IsNpcFaction(f) && u.Actors.TryGetValue(f, out var a) && a.Kind != ActorKind.Outlaw
            ? ObligatoryPerTick(u, f) * MathF.Max(0, u.Rules.FactionReserveTicks) : 0f;

    /// <summary>Reserve des Akteurs, wenn er eine Fraktion ist (sonst 0).</summary>
    public static float ReserveOf(Universe u, Actor a) => a.IsNpcFaction(u) ? Reserve(u, new FactionId(a.Id.Value)) : 0f;

    /// <summary>
    /// Was eine freiwillige Ausgabe aus der Kasse höchstens nehmen darf: Kasse − Reserve − Ersatzkosten eines fehlenden Mindestbestands
    /// (<see cref="ReplacementCost"/>), mindestens 0. Für Akteure ohne Fraktion die ganze Kasse.
    /// </summary>
    public static float Spendable(Universe u, Actor a) =>
        a.IsNpcFaction(u) ? MathF.Max(0f, a.Treasury.Credits - ReserveOf(u, a) - ReplacementCost(u, new FactionId(a.Id.Value))) : MathF.Max(0f, a.Treasury.Credits);

    /// <summary>
    /// Punkt 13 (Rechenzeit): die Abzüge von <see cref="Spendable"/> (Reserve, Ersatzkosten) einmal ermittelt, für Schleifen, in denen sich nur die Kasse
    /// ändert (Subventionen, Forschungsbudget). <see cref="Spendable(SpendableParts, Actor)"/> rechnet dann exakt wie <see cref="Spendable(Universe, Actor)"/>.
    /// </summary>
    public readonly record struct SpendableParts(bool Npc, float Reserve, float Replacement);

    /// <inheritdoc cref="SpendableParts"/>
    public static SpendableParts PartsOf(Universe u, Actor a) =>
        a.IsNpcFaction(u) ? new SpendableParts(true, ReserveOf(u, a), ReplacementCost(u, new FactionId(a.Id.Value))) : new SpendableParts(false, 0f, 0f);

    /// <inheritdoc cref="SpendableParts"/>
    public static float Spendable(SpendableParts p, Actor a) =>
        p.Npc ? MathF.Max(0f, a.Treasury.Credits - p.Reserve - p.Replacement) : MathF.Max(0f, a.Treasury.Credits);

    /// <summary>
    /// Was der Ersatz eines fehlenden Mindestbestands ungefähr kostet (Material des Modells zum Basispreis, Bauschiff zuzüglich Startkapital); 0, wenn nichts fehlt
    /// oder die Regel aus ist. Freiwillige Ausgaben lassen diesen Betrag in der Kasse — der Ersatz hat Vorrang.
    /// </summary>
    public static float ReplacementCost(Universe u, FactionId f)
    {
        if (!u.Rules.FactionKeepsReserve) return 0f;
        // Ausbau Punkt 17: eine gesetzlose Fraktion hat keine Polizei- und Bauschiff-Pflicht (Check meldet für sie nur Versteck/Jagd) — also
        // auch keine Ersatzkosten, die ihr verfügbares Geld (Spendable) künstlich senken
        if (u.Actors.TryGetValue(f, out var fa) && fa.Kind == ActorKind.Outlaw) return 0f;
        float cost = 0f;
        if (PoliceRequired(u) && PoliceBelow(u, f, u.Rules.PoliceMinPerFaction)) cost += BaseValue(u, u.Rules.PoliceModel);
        if (BuildersRequired(u) && BuildersBelow(u, f, u.Rules.MinBuildersPerFaction))
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
        (PoliceRequired(u) && PoliceBelow(u, f, u.Rules.PoliceMinPerFaction))
        || (BuildersRequired(u) && BuildersBelow(u, f, u.Rules.MinBuildersPerFaction));

    /// <summary>
    /// Punkt 13 (Rechenzeit): hat die Fraktion weniger als <paramref name="min"/> Polizeischiffe? Gleiche Auswahl wie <see cref="SecuritySystem.PoliceOf"/>
    /// (dessen Anzahl), aber ohne Liste und mit Abbruch, sobald das Minimum erreicht ist — <see cref="Spendable"/> läuft je Forschungsstation und Tick.
    /// </summary>
    static bool PoliceBelow(Universe u, FactionId f, int min) =>
        Below(u, 0, f, min, static (uu, s, ff) => ThreatMap.IsPolice(s) && uu.FactionOf(s) == ff);

    /// <summary>Punkt 13: wie <see cref="PoliceBelow"/> für die Fraktions-Bauschiffe (<see cref="FactionBuilderSystem.Of"/>).</summary>
    static bool BuildersBelow(Universe u, FactionId f, int min) =>
        Below(u, 1, f, min, static (uu, s, ff) => FactionBuilderSystem.IsFactionBuilder(uu, s) && s.Owner.Value == ff.Value);

    /// <summary>
    /// Punkt 13: „weniger als <paramref name="min"/> Schiffe erfüllen <paramref name="match"/>?“ — exakt wie die Zählung über alle Schiffe. Gemerkt werden die
    /// Schiffe, die das Minimum zuletzt erreicht haben (Zeugen): Sind sie noch dieselben Objekte unter ihrer Id im Universum und erfüllen die Bedingung noch
    /// (live geprüft), sind es mindestens <paramref name="min"/> — ohne alle Schiffe zu durchsuchen. Sonst wird wie vorher gezählt und neu gemerkt.
    /// </summary>
    static bool Below(Universe u, int kind, FactionId f, int min, Func<Universe, Ship, FactionId, bool> match)
    {
        if (min <= 0) return false;
        var key = (kind, f.Value, min);
        if (u.PerfCaches && u.ViabilityWitness.TryGetValue(key, out var w))
        {
            bool ok = true;
            foreach (var s in w)
                if (!u.Ships.TryGetValue(s.Id, out var cur) || !ReferenceEquals(cur, s) || !match(u, s, f)) { ok = false; break; }
            if (ok) return false;
        }
        var found = new Ship[min];
        int n = 0;
        foreach (var s in u.ShipsById())
            if (match(u, s, f))
            {
                found[n++] = s;
                if (n >= min) { u.ViabilityWitness[key] = found; return false; }
            }
        u.ViabilityWitness.Remove(key);
        return true;
    }

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
            // Prüfung Phase 28: „kann jagen“ — ein Kaperschiff ist einsatzfähig oder wird im Versteck versorgt, oder die Gruppe kann (wieder) eines bauen
            else if (CannotHunt(u, a))
                list.Add(new ViabilityIssue(ViabilityIssueKind.CannotHunt, "kein Kaperschiff kann jagen"));
            return list;
        }
        if (!a.IsNpcFaction(u)) return list;   // Runde 38: der Spieler hat keine Invarianten (Pflicht zu HQ/Polizei/Bauschiff nur für KI-Fraktionen)
        var f = new FactionId(actor.Value);
        float reserve = Reserve(u, f);
        if (reserve > 0f && a.Treasury.Credits < reserve && !ReserveBuilding(u, a))
            list.Add(new ViabilityIssue(ViabilityIssueKind.TreasuryBelowReserve, $"Kasse {a.Treasury.Credits:0} unter Rücklage {reserve:0} Cr"));
        if (PoliceRequired(u) && SecuritySystem.PoliceOf(u, f).Count is var pc && pc < u.Rules.PoliceMinPerFaction)
            list.Add(new ViabilityIssue(ViabilityIssueKind.PoliceBelowMinimum, $"Polizei {pc}/{u.Rules.PoliceMinPerFaction}"));
        if (BuildersRequired(u) && FactionBuilderSystem.Of(u, f).Count is var bc && bc < u.Rules.MinBuildersPerFaction)
            list.Add(new ViabilityIssue(ViabilityIssueKind.BuildersBelowMinimum, $"Bauschiffe {bc}/{u.Rules.MinBuildersPerFaction}"));
        if (!u.Stations.Values.Any(st => !st.IsBuildSite && st.Owner == actor && StationProtection.KindOf(u, st) is GovernmentKind.Headquarters or GovernmentKind.CompanySeat))
            list.Add(new ViabilityIssue(ViabilityIssueKind.NoSeat, "kein Hauptquartier/Sitz"));
        return list;
    }

    /// <summary>
    /// Prüfung Phase 28: Piratengruppe kann nicht jagen — kein Kaperschiff ist einsatzfähig oder in Versorgung im Versteck
    /// (<see cref="PirateSystem.CanHunt"/>), und ohne Kaperschiff fehlt das Versteck-Grundeinkommen für den Neuanfang. Rein.
    /// </summary>
    public static bool CannotHunt(Universe u, Actor a)
    {
        if (!u.Rules.SecurityActive) return false;
        var raiders = u.ShipsById().Where(s => ThreatMap.IsRaider(s) && s.Owner == a.Id).ToList();
        if (raiders.Count == 0)
        {
            // Phase 29: mit Modellwahl kostet der Neuanfang das günstigste Kampfmodell (Bau im Versteck)
            float first = u.Rules.PirateChoosesModels ? CombatModels.CheapestPirateCost(u) : u.Rules.RaiderCost;
            return u.Rules.PirateIdleIncomePerTick <= 0f && a.Treasury.Credits < first * Math.Clamp(u.Rules.RaiderRestartCostFactor, 0f, 1f);
        }
        // Gasernte (Langlauf): ein beschädigtes Rudel auf dem Heimweg zur Versorgung (Ziel = Sektor der Heimatbasis) zählt wie „im Versteck versorgt“ —
        // vorher galt die Gruppe für die ganze lange Heimreise als handlungsunfähig. Reine Anzeige/Messung (das System prüft Gesetzlose nicht).
        return !raiders.Any(s => PirateSystem.CanHunt(u, s, a)
            || (s.Transit is { } t && ReturnExecutor.Home(u, s) is { } home && t.ToSector == home.Sector));
    }

    /// <summary>Gerätetest (Phase 26): Rücklage noch im Aufbau (Fraktion jünger als <see cref="EconomyRules.FactionReserveBuildTicks"/>) — kein Verstoß.</summary>
    public static bool ReserveBuilding(Universe u, Actor a) => u.Tick - a.CreatedTick < u.Rules.FactionReserveBuildTicks;

    /// <summary>Anzeigezeile: „handlungsfähig“ bzw. „nicht handlungsfähig: …“; null für Akteure ohne Invarianten.</summary>
    public static string? Text(Universe u, ActorId actor)
    {
        if (!u.Actors.TryGetValue(actor, out var a) || !(a.Kind == ActorKind.Outlaw || a.IsNpcFaction(u))) return null;
        var issues = Check(u, actor);
        // Gerätetest (Phase 26): fehlt nur die Rücklage, ist das kein Alarm — die Fraktion handelt, nur freiwillige Ausgaben warten. In der Aufbauzeit
        // (die Startkassen beginnen leer, die Rücklage wächst aus den Abgaben) „Rücklage im Aufbau“, danach „Rücklage unterschritten“ (bleibt ein Verstoß).
        float reserve = ReserveOf(u, a);
        if (issues.Count == 0)
            return a.IsNpcFaction(u) && reserve > 0f && a.Treasury.Credits < reserve
                ? $"handlungsfähig – Rücklage im Aufbau ({a.Treasury.Credits:0} von {reserve:0} Cr)"
                : "handlungsfähig";
        if (issues.TrueForAll(i => i.Kind == ViabilityIssueKind.TreasuryBelowReserve))
            return $"handlungsfähig – Rücklage unterschritten ({a.Treasury.Credits:0} von {reserve:0} Cr)";
        return "nicht handlungsfähig: " + string.Join(", ", issues.Select(i => i.Text));
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
        foreach (var f in u.NpcFactionIds().ToList())   // Runde 38: der Spieler hat keine Pflicht zu HQ/Polizei/Bauschiff
        {
            if (!u.Actors.TryGetValue(f, out var actor) || actor.Kind == ActorKind.Outlaw) continue;
            // Kapitulation: Sitz verloren (zerstört/kapituliert) und der sofortige Not-Sitz fand keinen Platz — hier erneut (kein Zufall, keine Bremse)
            if (r.StationCapitulation) SeatRelocation.Ensure(u, f);
            if (u.Tick - actor.LastRestoreTick < r.ViabilityRestoreCooldown) continue;
            if (r.SecurityActive && r.PoliceMinPerFaction > 0 && u.Tick >= actor.LastForceTick && SecuritySystem.PoliceOf(u, f).Count < r.PoliceMinPerFaction
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
