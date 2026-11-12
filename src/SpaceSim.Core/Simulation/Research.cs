using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Forschung (Phase 13, FORSCHUNG.md): gemeinsame Regeln für das <see cref="ResearchSystem"/>, den Dienst „research“ und alle Systeme, die
/// Freischaltungen abfragen. Der Tech-Stand gehört der Fraktion (fraktionsweit) und gilt für alle ihre Stationen; eine Station ohne Fraktion nutzt
/// den Stand der Fraktion, der ihr Sektor gehört. Content, den kein Projekt nennt, ist immer frei — die Startwirtschaft hängt an nichts davon.
/// Reine Funktionen über das Universum, deterministische Reihenfolgen (Content-Reihenfolge der Projekte).
/// </summary>
public static class Research
{
    /// <summary>Ist dieser Content für die Fraktion frei? Ohne sperrendes Projekt immer; mit Projekt nur, wenn die Fraktion es abgeschlossen hat.</summary>
    public static bool IsUnlocked(Universe u, FactionId? faction, UnlockKind kind, string key)
    {
        if (!u.Rules.ResearchActive) return true;
        var p = u.Research.LockOf(kind, key);
        if (p is null) return true;
        return faction is { } f && u.Factions.TryGetValue(f, out var fac) && fac.Research.IsCompleted(p.Key);
    }

    /// <summary>Wessen Tech-Stand an einer Station gilt: Fraktion des Besitzers, sonst die des Sektors.</summary>
    public static FactionId? TechOf(Universe u, Station st) =>
        u.FactionOf(st) ?? (u.Sectors.TryGetValue(st.Sector, out var sec) && u.Factions.ContainsKey(sec.Owner) ? sec.Owner : null);

    /// <summary>Ist dieser Content an dieser Station frei (Tech-Stand ihrer Fraktion)?</summary>
    public static bool Available(Universe u, Station st, UnlockKind kind, string key) =>
        u.Research.LockOf(kind, key) is null || IsUnlocked(u, TechOf(u, st), kind, key);

    /// <summary>Darf diese Station die Ware herstellen bzw. aus ihrem Lager einbauen? Gesperrt sind nur Modul-Waren gesperrter Module.</summary>
    public static bool GoodAvailable(Universe u, Station st, GoodId good) =>
        !u.Goods.TryGetValue(good, out var g) || !u.Modules.ContainsKey(g.Key) || Available(u, st, UnlockKind.Module, g.Key);

    /// <summary>Kann irgendeine Fraktion diesen Content nutzen (für die Bauwahl des Lebenszyklus)?</summary>
    public static bool UnlockedAnywhere(Universe u, UnlockKind kind, string key) =>
        u.Research.LockOf(kind, key) is null || !u.Rules.ResearchActive || u.Factions.Keys.Any(f => IsUnlocked(u, f, kind, key));

    /// <summary>Anzeigetext für gesperrten Content an einer Station („nicht erforscht: Projekt“), null = frei.</summary>
    public static string? LockText(Universe u, Station st, UnlockKind kind, string key)
    {
        if (Available(u, st, kind, key)) return null;
        var p = u.Research.LockOf(kind, key)!;
        return $"nicht erforscht ({p.Name})";
    }

    /// <summary>Sind alle Voraussetzungen des Projekts für die Fraktion erforscht?</summary>
    public static bool RequirementsMet(ResearchState state, ResearchProject p) => p.Requires.All(state.IsCompleted);

    /// <summary>Projekte, an denen die Fraktion jetzt forschen kann (nicht erforscht, Voraussetzungen erfüllt), in Content-Reihenfolge.</summary>
    public static IEnumerable<ResearchProject> AvailableProjects(Universe u, ResearchState state) =>
        u.Research.Projects.Where(p => !state.IsCompleted(p.Key) && RequirementsMet(state, p));

    /// <summary>
    /// Laufendes Projekt der Fraktion ohne etwas zu ändern (Anzeige): das gesetzte, wenn es noch läuft, sonst das, das die Heuristik als Nächstes wählt —
    /// das am weitesten fortgeschrittene verfügbare Projekt, bei Gleichstand die Content-Reihenfolge (so wird Begonnenes zuerst fertig). null = nichts mehr offen.
    /// </summary>
    public static ResearchProject? Peek(Universe u, ResearchState state)
    {
        if (state.Current is { } cur && u.Research.Get(cur) is { } p && !state.IsCompleted(p.Key) && RequirementsMet(state, p)) return p;
        ResearchProject? best = null;
        float bestProgress = -1f;
        foreach (var cand in AvailableProjects(u, state))
        {
            float prog = state.Progress.GetValueOrDefault(cand.Key);
            if (prog > bestProgress) { best = cand; bestProgress = prog; }
        }
        return best;
    }

    /// <summary>Laufendes Projekt der Fraktion; setzt es nach der Heuristik von <see cref="Peek"/>, wenn keins (mehr) läuft (nur Simulation und Dienst).</summary>
    public static ResearchProject? CurrentOf(Universe u, Faction faction)
    {
        var state = faction.Research;
        var p = Peek(u, state);
        if (state.Current != p?.Key) { state.Current = p?.Key; state.Commissioned = false; }
        return p;
    }

    /// <summary>
    /// Bucht FP auf ein Projekt der Fraktion (Station oder Spieler). Erreicht es seine Punkte, ist es abgeschlossen (<see cref="ResearchCompleted"/>):
    /// die Freischaltungen gelten sofort, überzählige FP verfallen, das nächste Projekt wählt <see cref="CurrentOf"/>. true = abgeschlossen.
    /// </summary>
    public static bool AddPoints(Universe u, Faction faction, ResearchProject p, float points)
    {
        var state = faction.Research;
        if (points <= 0f || state.IsCompleted(p.Key)) return false;
        float now = state.Progress.GetValueOrDefault(p.Key) + points;
        state.PointsTotal += points;
        if (now + 1e-3f < p.Points) { state.Progress[p.Key] = now; return false; }
        state.Progress.Remove(p.Key);
        state.Completed[p.Key] = u.Tick;
        if (state.Current == p.Key) { state.Current = null; state.Commissioned = false; }
        u.Events.Publish(new ResearchCompleted(faction.Id, p.Key, u.Tick));
        return true;
    }

    /// <summary>Verbrauch eines Takts: Grundverbrauch der Station plus Zusatzverbrauch des Projekts, nach GoodId.</summary>
    public static IReadOnlyList<MaterialNeed> CycleNeeds(ResearchConfig cfg, ResearchProject? p)
    {
        var sum = new Dictionary<GoodId, float>(cfg.Inputs);
        if (p is not null) foreach (var (g, n) in p.Inputs) sum[g] = sum.GetValueOrDefault(g) + n;
        return sum.Where(kv => kv.Value > 0f).OrderBy(kv => kv.Key.Value).Select(kv => new MaterialNeed(kv.Key, kv.Value)).ToList();
    }

    /// <summary>Was einem Takt fehlt (Güter oder Personalgeld); null = der Takt kann laufen.</summary>
    public static string? StallReason(Universe u, Station st, ResearchConfig cfg, IReadOnlyList<MaterialNeed> needs)
    {
        foreach (var n in needs)
            if (!st.Market.TryGetValue(n.Good, out var e) || e.Amount + 1e-4f < n.Units) return $"{u.Goods[n.Good].Name} fehlt";
        if (st.Credits < cfg.Credits) return "Budget fehlt";
        return null;
    }

    /// <summary>Forschungsstationen einer Fraktion (fertige Stationen eines Typs mit Forschungsbetrieb), nach Id.</summary>
    public static IEnumerable<Station> StationsOf(Universe u, FactionId f) =>
        u.Stations.Values.Where(st => !st.IsBuildSite && ConfigOf(u, st) is not null && TechOf(u, st) == f).OrderBy(st => st.Id.Value);

    /// <summary>Forschungsbetrieb einer Station (Typ mit <see cref="StationType.Research"/>), sonst null.</summary>
    public static ResearchConfig? ConfigOf(Universe u, Station st) =>
        !st.IsBuildSite && u.StationTypes.TryGetValue(st.TypeKey, out var t) ? t.Research : null;

    /// <summary>Anzeigename einer Freischaltung („Modul Fusionsreaktor Mk3“).</summary>
    public static string UnlockName(Universe u, ResearchUnlock x) => x.Kind switch
    {
        UnlockKind.Module => "Modul " + (u.Modules.TryGetValue(x.Key, out var m) ? m.Name : x.Key),
        UnlockKind.Model => "Schiff " + (u.ShipModels.TryGetValue(x.Key, out var sm) ? sm.Name : x.Key),
        _ => "Stationstyp " + (u.StationTypes.TryGetValue(x.Key, out var t) ? t.Name : x.Key),
    };
}

/// <summary>
/// Forschungsbetrieb (Phase 13, eigenes Modul „Research“ direkt nach Produktion und Markt): je Forschungsstation jeden Tick
/// (1) Gebotsdeckel auf ihre Verbrauchsgüter (<see cref="EconomyRules.ResearchMaxBidFactor"/>), (2) Budget aus der Kasse der Besitzerfraktion bis
/// <see cref="ResearchConfig.FundTarget"/> (<see cref="TreasuryUpkeep"/> „research“, damit die Geldmenge erklärbar bleibt), und alle
/// <see cref="ResearchConfig.Ticks"/> Ticks (3) ein Takt: Güter und Personalkosten weg, FP auf das laufende Projekt (<see cref="ResearchCycle"/>).
/// Fehlen Güter oder Geld, stockt die Forschung wie eine Produktion. Mit <see cref="EconomyRules.ResearchActive"/> aus ruht alles.
/// </summary>
public sealed class ResearchSystem : ISimSystem
{
    // Forschungsstationen nach Id; neu ermittelt, wenn sich Stationen ändern (Bau, Abriss, Laden: anderes Universum)
    readonly List<Station> _stations = new();
    Universe? _for;
    int _version = -1, _count = -1;

    public void Step(Universe u, int dt)
    {
        if (!u.Rules.ResearchActive || u.Research.Count == 0) return;
        if (!ReferenceEquals(_for, u) || _version != u.StructureVersion || _count != u.Stations.Count)
        {
            _for = u; _version = u.StructureVersion; _count = u.Stations.Count;
            _stations.Clear();
            foreach (var st in u.Stations.Values)
                if (Research.ConfigOf(u, st) is not null) _stations.Add(st);
            _stations.Sort((a, b) => a.Id.Value.CompareTo(b.Id.Value));
        }
        if (_stations.Count == 0) return;

        SortedDictionary<int, float>? funded = null;
        foreach (var st in _stations)
        {
            var cfg = Research.ConfigOf(u, st)!;
            if (Research.TechOf(u, st) is not { } f || !u.Factions.TryGetValue(f, out var faction)) continue;
            var project = Research.CurrentOf(u, faction);

            // 1) Gebotsdeckel: eine Forschungsstation bietet nie mehr als Basispreis × Faktor (Lagerware ohne Rezept stiege sonst auf 4×)
            float cap = u.Rules.ResearchMaxBidFactor;
            foreach (var g in cfg.Inputs.Keys) Cap(u, st, g, cap);
            if (project is not null) foreach (var g in project.Inputs.Keys) Cap(u, st, g, cap);

            // 2) Budget aus der Fraktionskasse (nur solange es etwas zu erforschen gibt)
            // Phase 22: Forschung ist freiwillig — nur aus dem Teil der Kasse über der Betriebsreserve
            if (project is not null && cfg.FundTarget > 0f && st.Credits < cfg.FundTarget && u.Actors.TryGetValue(st.Owner, out var owner) && FactionViability.Spendable(u, owner) > 0f)
            {
                float pay = MathF.Min(MathF.Min(cfg.FundTarget - st.Credits, cfg.FundPerTick * dt), FactionViability.Spendable(u, owner));
                if (pay > 0f)
                {
                    owner.Treasury.Credits -= pay;
                    st.Credits += pay;
                    funded ??= new SortedDictionary<int, float>();
                    funded[owner.Id.Value] = funded.GetValueOrDefault(owner.Id.Value) + pay;
                }
            }

            // 3) Forschungstakt
            if (project is null || cfg.Ticks <= 0 || u.Tick % cfg.Ticks != 0) continue;
            var needs = Research.CycleNeeds(cfg, project);
            if (Research.StallReason(u, st, cfg, needs) is { } why)
            {
                u.Events.Publish(new ResearchCycle(st.Id, f, project.Key, 0f, 0f, 0f, why));
                continue;
            }
            float value = 0f;
            foreach (var n in needs) { st.Market[n.Good].Amount -= n.Units; value += n.Units * u.Goods[n.Good].BasePrice; }
            st.Credits -= cfg.Credits;
            faction.Research.Spent += cfg.Credits;
            u.Events.Publish(new ResearchCycle(st.Id, f, project.Key, cfg.Points, cfg.Credits, value, null));
            Research.AddPoints(u, faction, project, cfg.Points);
        }
        if (funded is not null)
            foreach (var (actor, amount) in funded) u.Events.Publish(new TreasuryUpkeep(new ActorId(actor), "research", amount, 0f));
    }

    static void Cap(Universe u, Station st, GoodId g, float factor)
    {
        if (st.Market.TryGetValue(g, out var e)) e.Price = MathF.Min(e.Price, u.Goods[g].BasePrice * factor);
    }
}
