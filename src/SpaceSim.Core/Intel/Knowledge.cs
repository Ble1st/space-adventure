using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Intel;

/// <summary>Was eine Aufdeckung Neues brachte (Ereignis <see cref="IntelDiscovered"/>, Tests).</summary>
public struct IntelDelta
{
    public int Sectors, Visited, Stations, Gates, Fields, Factions;
    public readonly bool Any => Sectors + Visited + Stations + Gates + Fields + Factions > 0;
    public void Add(IntelDelta d) { Sectors += d.Sectors; Visited += d.Visited; Stations += d.Stations; Gates += d.Gates; Fields += d.Fields; Factions += d.Factions; }
}

/// <summary>
/// Ausbau Punkt 16: Regeln des Spielerwissens (rein, deterministisch, kein Zufall). Abfragen geben ohne Wissensstand (<see cref="Actor.Intel"/> null —
/// Regel aus, Welten ohne Startpaket, Testszenarien) immer „bekannt“; eigene Stationen und die eigene Fraktion kennt der Spieler stets.
/// </summary>
public static class Knowledge
{
    /// <summary>Wissensstand des Spielers (null = alles bekannt).</summary>
    public static PlayerIntel? Of(Universe u) => u.Actors.TryGetValue(u.PlayerActor, out var a) ? a.Intel : null;

    /// <summary>Weiß der Spieler nicht alles?</summary>
    public static bool Limited(Universe u) => Of(u) is not null;

    public static SectorKnowledge SectorState(Universe u, SectorId s) => Of(u) is { } i ? i.StateOf(s) : SectorKnowledge.Visited;
    /// <summary>Sektor bekannt (mindestens von fern gesehen: Name und Lage).</summary>
    public static bool KnowsSector(Universe u, SectorId s) => SectorState(u, s) >= SectorKnowledge.Seen;
    /// <summary>Sektor besucht (Inhalt teils bekannt).</summary>
    public static bool Visited(Universe u, SectorId s) => SectorState(u, s) >= SectorKnowledge.Visited;

    public static bool KnowsStation(Universe u, StationId id) =>
        Of(u) is not { } i || i.Stations.Contains(id) || (u.Stations.TryGetValue(id, out var st) && st.Owner == u.PlayerActor);
    public static bool KnowsStation(Universe u, Station st) => Of(u) is not { } i || st.Owner == u.PlayerActor || i.Stations.Contains(st.Id);
    public static bool KnowsGate(Universe u, GateId id) => Of(u) is not { } i || i.Gates.Contains(id);
    public static bool KnowsField(Universe u, SectorId s, int index) => Of(u) is not { } i || i.Fields.Contains((s.Value, index));
    public static bool KnowsGasField(Universe u, SectorId s, int index) => Of(u) is not { } i || i.GasFields.Contains((s.Value, index));

    /// <summary>Fraktion (oder Piratengruppe) bekannt; die eigene immer.</summary>
    public static bool KnowsFaction(Universe u, FactionId f) => Of(u) is not { } i || f.Value == u.PlayerActor.Value || i.Factions.Contains(f);

    /// <summary>
    /// Akteur bekannt (Fraktionsliste): Fraktionen und Gesetzlose erst nach Begegnung; Einzelakteure (Freie Kapitäne, Unabhängige) sind Händlervolk — immer.
    /// </summary>
    public static bool KnowsActor(Universe u, ActorId a)
    {
        if (Of(u) is null || a == u.PlayerActor) return true;
        if (u.Factions.ContainsKey(new FactionId(a.Value))) return KnowsFaction(u, new FactionId(a.Value));
        if (u.Actors.TryGetValue(a, out var act) && act.Kind == ActorKind.Outlaw) return KnowsFaction(u, new FactionId(a.Value));
        return true;
    }

    /// <summary>Anzeigename eines Sektors: unbekannt = „?“.</summary>
    public static string SectorName(Universe u, SectorId s) =>
        !KnowsSector(u, s) ? "?" : u.Sectors.TryGetValue(s, out var sec) ? sec.Name : "?";

    // ---- Aufdecken -------------------------------------------------------------------------------------------------

    /// <summary>Startwissen eines regulären Startpakets: Startsektor ganz, Sektoren hinter seinen Toren dem Namen nach, Besitzerfraktion des Startsektors.</summary>
    public static PlayerIntel Init(Universe u, SectorId start)
    {
        var i = new PlayerIntel();
        u.Player.Intel = i;
        RevealSectorFull(u, start);
        return i;
    }

    /// <summary>Sektor vollständig (Sektorkarte gekauft, Startsektor): besucht, alle Stationen, Felder und Tore.</summary>
    public static IntelDelta RevealSectorFull(Universe u, SectorId s)
    {
        var d = new IntelDelta();
        if (Of(u) is null || !u.Sectors.TryGetValue(s, out var sec)) return d;
        d.Add(Visit(u, s));
        foreach (var st in sec.Stations) d.Add(RevealStation(u, st));
        for (int k = 0; k < sec.Fields.Count; k++) if (Of(u)!.Fields.Add((s.Value, k))) d.Fields++;
        for (int k = 0; k < sec.GasFields.Count; k++) if (Of(u)!.GasFields.Add((s.Value, k))) d.Fields++;
        return d;
    }

    /// <summary>Sektor betreten: besucht, alle Tore (und die Sektoren dahinter dem Namen nach), Besitzerfraktion bekannt.</summary>
    public static IntelDelta Visit(Universe u, SectorId s)
    {
        var d = new IntelDelta();
        if (Of(u) is not { } i || !u.Sectors.TryGetValue(s, out var sec)) return d;
        var before = i.StateOf(s);
        if (i.Raise(s, SectorKnowledge.Visited)) { if (before == SectorKnowledge.Unknown) d.Sectors++; d.Visited++; }
        foreach (var g in sec.Gates) d.Add(RevealGate(u, g));
        if (sec.Owner.Value != 0) d.Add(RevealFaction(u, sec.Owner));
        return d;
    }

    /// <summary>Sektor dem Namen nach (von fern gesehen).</summary>
    public static IntelDelta See(Universe u, SectorId s)
    {
        var d = new IntelDelta();
        if (Of(u) is { } i && u.Sectors.ContainsKey(s) && i.Raise(s, SectorKnowledge.Seen)) d.Sectors++;
        return d;
    }

    /// <summary>Tor: beide Hälften bekannt, ihre Sektoren mindestens von fern.</summary>
    public static IntelDelta RevealGate(Universe u, GateId id)
    {
        var d = new IntelDelta();
        if (Of(u) is not { } i || !u.Gates.TryGetValue(id, out var g)) return d;
        if (i.Gates.Add(id)) d.Gates++;
        d.Add(See(u, g.Sector));
        if (u.Gates.TryGetValue(g.Target, out var far))
        {
            if (i.Gates.Add(far.Id)) d.Gates++;
            d.Add(See(u, far.Sector));
        }
        return d;
    }

    public static IntelDelta RevealStation(Universe u, StationId id)
    {
        var d = new IntelDelta();
        if (Of(u) is not { } i || !u.Stations.TryGetValue(id, out var st)) return d;
        if (i.Stations.Add(id)) d.Stations++;
        d.Add(See(u, st.Sector));
        return d;
    }

    public static IntelDelta RevealFaction(Universe u, FactionId f)
    {
        var d = new IntelDelta();
        if (Of(u) is not { } i || f.Value == 0 || f.Value == u.PlayerActor.Value) return d;
        bool exists = u.Factions.ContainsKey(f) || (u.Actors.TryGetValue(new ActorId(f.Value), out var a) && a.Kind == ActorKind.Outlaw);
        if (exists && i.Factions.Add(f)) d.Factions++;
        return d;
    }

    /// <summary>Erste Begegnung mit einem Akteur: seine Fraktion (oder Piratengruppe) wird bekannt.</summary>
    public static IntelDelta Meet(Universe u, ActorId a)
    {
        if (Of(u) is null) return default;
        if (u.Factions.ContainsKey(new FactionId(a.Value))) return RevealFaction(u, new FactionId(a.Value));
        if (u.Actors.TryGetValue(a, out var act))
        {
            if (act.Kind == ActorKind.Outlaw) return RevealFaction(u, new FactionId(a.Value));
            if (act.Membership is { } m) return RevealFaction(u, m);
        }
        return default;
    }

    /// <summary>Andocken: Station bekannt, ihre Fraktion begegnet.</summary>
    public static IntelDelta Dock(Universe u, StationId id)
    {
        var d = RevealStation(u, id);
        if (u.Stations.TryGetValue(id, out var st)) d.Add(Meet(u, st.Owner));
        return d;
    }

    /// <summary>
    /// Sensoren eines eigenen Schiffs an <paramref name="pos"/> in <paramref name="s"/>: Sektor betreten, Stationen, Tore und Feldmitten in
    /// <see cref="EconomyRules.IntelSensorRange"/> (Station: Abstand zum Rand) werden bekannt. Je Sektor höchstens ein paar Dutzend Abstände.
    /// </summary>
    public static IntelDelta Scan(Universe u, SectorId s, Vec2 pos)
    {
        var d = new IntelDelta();
        if (Of(u) is not { } i || !u.Sectors.TryGetValue(s, out var sec)) return d;
        d.Add(Visit(u, s));
        float r = u.Rules.IntelSensorRange;
        foreach (var id in sec.Stations)
            if (!i.Stations.Contains(id) && u.Stations.TryGetValue(id, out var st) && Vec2.Distance(pos, st.Position) - st.Radius <= r) d.Add(RevealStation(u, id));
        for (int k = 0; k < sec.Fields.Count; k++)
            if (!i.Fields.Contains((s.Value, k)) && Vec2.Distance(pos, sec.Fields[k].Position) <= r && i.Fields.Add((s.Value, k))) d.Fields++;
        for (int k = 0; k < sec.GasFields.Count; k++)
            if (!i.GasFields.Contains((s.Value, k)) && Vec2.Distance(pos, sec.GasFields[k].Position) - sec.GasFields[k].Radius <= r && i.GasFields.Add((s.Value, k))) d.Fields++;
        return d;
    }

    /// <summary>Unbekanntes im Sektor (Stationen, Felder, Tore) — für Erkunden und Anzeige.</summary>
    public static int UnknownIn(Universe u, SectorId s)
    {
        if (Of(u) is not { } i || !u.Sectors.TryGetValue(s, out var sec)) return 0;
        int n = sec.Stations.Count(id => !i.Stations.Contains(id) && !(u.Stations.TryGetValue(id, out var st) && st.Owner == u.PlayerActor));
        n += sec.Gates.Count(g => !i.Gates.Contains(g));
        for (int k = 0; k < sec.Fields.Count; k++) if (!i.Fields.Contains((s.Value, k))) n++;
        for (int k = 0; k < sec.GasFields.Count; k++) if (!i.GasFields.Contains((s.Value, k))) n++;
        return n;
    }

    /// <summary>Ereignis für eine Aufdeckung mit Neuigkeiten.</summary>
    public static void Publish(Universe u, SectorId s, IntelDelta d, string source)
    {
        if (d.Any) u.Events.Publish(new IntelDiscovered(s, d.Sectors, d.Visited, d.Stations, d.Gates, d.Fields, d.Factions, source));
    }

    /// <summary>Kurztext einer Aufdeckung („Neu: Sektor Beta, 2 Stationen“).</summary>
    public static string Text(Universe u, IntelDiscovered e)
    {
        var parts = new List<string>();
        if (e.Visited > 0 && u.Sectors.TryGetValue(e.Sector, out var sec)) parts.Add($"Sektor {sec.Name.Replace("Sektor ", "")} erkundet");
        else if (e.Sectors > 0) parts.Add(e.Sectors == 1 ? "1 Sektor" : $"{e.Sectors} Sektoren");
        if (e.Stations > 0) parts.Add(e.Stations == 1 ? "1 Station" : $"{e.Stations} Stationen");
        if (e.Fields > 0) parts.Add(e.Fields == 1 ? "1 Feld" : $"{e.Fields} Felder");
        if (e.Factions > 0) parts.Add(e.Factions == 1 ? "1 Fraktion" : $"{e.Factions} Fraktionen");
        return parts.Count == 0 ? "" : "Neu entdeckt: " + string.Join(", ", parts);
    }
}
