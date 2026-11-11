using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>Status eines Forschungsprojekts aus Sicht einer Fraktion.</summary>
public enum ResearchStatus { Done, Running, Available, Blocked }

/// <summary>Anzeige eines Projekts: Status, FP-Stand, Abschluss-Tick, fehlende Voraussetzungen, Freischaltungen und Zusatzverbrauch als Text.</summary>
public sealed record ResearchProjectView(ResearchProject Project, ResearchStatus Status, float Progress, long? CompletedTick,
    IReadOnlyList<string> MissingRequires, IReadOnlyList<string> Unlocks, string Inputs)
{
    public float Fraction => Project.Points > 0f ? Math.Clamp(Progress / Project.Points, 0f, 1f) : 1f;
}

/// <summary>Anzeige einer Forschungsstation: FP je Takt, Taktlänge, Verbrauch je Takt (Text), Grund, warum sie stockt (null = läuft), Stationskasse.</summary>
public sealed record ResearchStationView(Station Station, float Points, int Ticks, string Needs, string? Stalled, float Credits);

/// <summary>Forschungsstand einer Fraktion für Dock-Bereich, Kommandozentrale und Balance-Tool (reine Daten).</summary>
public sealed record ResearchFactionView(FactionId Faction, string Name, int Completed, int Total, ResearchProjectView? Current,
    float PointsTotal, float Spent, float PlayerFunded, IReadOnlyList<ResearchProjectView> Projects, IReadOnlyList<ResearchStationView> Stations,
    long? EtaTicks)
{
    /// <summary>Kurzzeile („2/3 erforscht · Präzisionslaser 45 %“).</summary>
    public string Summary => Total == 0 ? "keine Forschung"
        : $"{Completed}/{Total} erforscht" + (Current is { } c ? $" · {c.Project.Name} {c.Fraction:P0}" : Completed == Total ? " · alles erforscht" : "")
          + (Stations.Count == 0 ? " · keine Forschungsstation" : "");
}

/// <summary>Reine Anzeige-Funktionen der Forschung (Phase 13); ändern nichts am Universum.</summary>
public static class ResearchInfo
{
    public static string StatusName(ResearchStatus s) => s switch
    {
        ResearchStatus.Done => "erforscht",
        ResearchStatus.Running => "läuft",
        ResearchStatus.Available => "verfügbar",
        _ => "gesperrt",
    };

    /// <summary>Forschungsstand der Fraktion (null = unbekannte Fraktion).</summary>
    public static ResearchFactionView? ForFaction(Universe u, FactionId f)
    {
        if (!u.Factions.TryGetValue(f, out var faction)) return null;
        var state = faction.Research;
        var current = Research.Peek(u, state);
        var projects = new List<ResearchProjectView>();
        ResearchProjectView? cur = null;
        foreach (var p in u.Research.Projects)
        {
            var status = state.IsCompleted(p.Key) ? ResearchStatus.Done : current?.Key == p.Key ? ResearchStatus.Running
                : Research.RequirementsMet(state, p) ? ResearchStatus.Available : ResearchStatus.Blocked;
            var view = new ResearchProjectView(p, status, status == ResearchStatus.Done ? p.Points : state.Progress.GetValueOrDefault(p.Key),
                state.Completed.TryGetValue(p.Key, out var t) ? t : null,
                p.Requires.Where(r => !state.IsCompleted(r)).Select(r => u.Research.Get(r)?.Name ?? r).ToList(),
                p.Unlocks.Select(x => Research.UnlockName(u, x)).ToList(),
                string.Join(", ", p.Inputs.OrderBy(kv => kv.Key.Value).Select(kv => $"{kv.Value:0.##} {u.Goods[kv.Key].Name}")));
            projects.Add(view);
            if (status == ResearchStatus.Running) cur = view;
        }
        var stations = new List<ResearchStationView>();
        float rate = 0f;
        foreach (var st in Research.StationsOf(u, f))
        {
            var cfg = Research.ConfigOf(u, st)!;
            var needs = Research.CycleNeeds(cfg, current);
            string? stalled = !u.Rules.ResearchActive ? "Forschung abgeschaltet" : current is null ? "nichts zu erforschen" : Research.StallReason(u, st, cfg, needs);
            stations.Add(new ResearchStationView(st, cfg.Points, cfg.Ticks,
                string.Join(", ", needs.Select(n => $"{n.Units:0.##} {u.Goods[n.Good].Name}")) + (cfg.Credits > 0f ? $", {cfg.Credits:0} Cr" : ""), stalled, st.Credits));
            if (stalled is null && cfg.Ticks > 0) rate += cfg.Points / cfg.Ticks;
        }
        long? eta = cur is not null && rate > 0f ? (long)MathF.Ceiling((cur.Project.Points - cur.Progress) / rate) : null;
        return new ResearchFactionView(f, faction.Name, state.Completed.Count(kv => u.Research.Get(kv.Key) is not null), u.Research.Count, cur,
            state.PointsTotal, state.Spent, state.PlayerFunded, projects, stations, eta);
    }

    /// <summary>Forschungsstand der Fraktion, für die diese Station forscht (null = keine Forschungsstation).</summary>
    public static ResearchFactionView? ForStation(Universe u, Station st) =>
        Research.ConfigOf(u, st) is not null && Research.TechOf(u, st) is { } f ? ForFaction(u, f) : null;
}
