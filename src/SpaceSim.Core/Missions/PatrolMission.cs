using System.Globalization;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Missions;

/// <summary>
/// Runde 42: Auftragsart <b>Patrouille</b> (niedrig bezahlt — Nutzerentscheidung: leicht auszusitzen) — eine Sektor-HQ einer
/// Fraktion in bedrohten Sektor bietet an: 2–3 Wegpunkte in Sektoren der Auftraggeber-Fraktion <b>nacheinander</b> anfliegen
/// (Radius <see cref="EconomyRules.PatrolWaypointRadius"/> um den Wegpunkt, aktives Schiff des Spielers). Nur der jeweils
/// nächste Wegpunkt zählt (Reihenfolge). Erfüllt, wenn alle Wegpunkte angeflogen wurden, Scheitern bei Frist.
/// Marker auf den nächsten Wegpunkt. Belohnung (niedrig) aus der Fraktionskasse.
/// </summary>
public sealed class PatrolMission : IMissionType
{
    public const string KeyId = "patrol";
    public string Key => KeyId;

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Wegpunkte als (Sektor, Position), aus Args „wps“ („sec:x:y;…“).</summary>
    public static List<(SectorId Sector, Vec2 Pos)> WaypointsOf(Mission m)
    {
        var list = new List<(SectorId, Vec2)>();
        if (!m.Args.TryGetValue("wps", out var wps)) return list;
        foreach (var part in wps.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = part.Split(':');
            if (f.Length != 3) continue;
            if (!int.TryParse(f[0], out var sv) || !float.TryParse(f[1], NumberStyles.Float, Inv, out var x) || !float.TryParse(f[2], NumberStyles.Float, Inv, out var y)) continue;
            list.Add((new SectorId(sv), new Vec2(x, y)));
        }
        return list;
    }

    /// <summary>Index des nächsten Wegpunkts (Args „wp“).</summary>
    public static int CurrentOf(Mission m) => m.Args.TryGetValue("wp", out var s) && int.TryParse(s, out var v) ? v : 0;

    /// <summary>Position eines Wegpunkts in einem Sektor: erstes Tor, sonst erste Station, sonst Sektor-Mitte.</summary>
    static Vec2 PointInSector(Universe u, Sector sec)
    {
        foreach (var gid in sec.Gates.OrderBy(g => g.Value))
            if (u.Gates.TryGetValue(gid, out var g)) return g.Position;
        foreach (var sid in sec.Stations.OrderBy(s => s.Value))
            if (u.Stations.TryGetValue(sid, out var st)) return st.Position;
        return new Vec2(0f, 0f);
    }

    public MissionOffer? Generate(Universe u, Station st, DeterministicRng rng, long bucket)
    {
        var r = u.Rules;
        if (MissionRules.Forbidden(u, st.Owner) || MissionRules.Blocked(u, st.Owner.Value)) return null;
        if (!BountyMission.IsOfferStation(u, st)) return null;
        if (u.FactionOf(st.Owner) is not { } f || !u.IsNpcFaction(f)) return null;
        if (!u.Sectors.TryGetValue(st.Sector, out var own) || own.Owner != f || own.Threat < r.PatrolMinThreat) return null;
        if (MissionRng.NextFloat(rng) >= r.MissionPatrolChance) return null;
        // Wegpunkte: Sektoren der Fraktion, erreichbar von der Angebots-Station aus
        var sectors = u.Sectors.Values.Where(s => s.Owner == f).OrderBy(s => s.Id.Value).ToList();
        int count = Math.Min(MissionRng.NextInt(rng, r.PatrolWaypointsMax - r.PatrolWaypointsMin + 1) + r.PatrolWaypointsMin, sectors.Count);
        if (count < r.PatrolWaypointsMin) return null;
        // Partial-Fisher-Yates mit dem Missions-RNG (deterministisch): count Sektoren ohne Zurücklegen
        var picked = new List<Sector>();
        for (int i = 0; i < sectors.Count && picked.Count < count; i++)
        {
            int j = i + MissionRng.NextInt(rng, sectors.Count - i);
            (sectors[i], sectors[j]) = (sectors[j], sectors[i]);
            picked.Add(sectors[i]);
        }
        var wps = new List<(SectorId, Vec2)>();
        foreach (var sec in picked.OrderBy(s => s.Id.Value))
        {
            var pos = PointInSector(u, sec);
            if (float.IsInfinity(Travel.Ticks(u, st.Sector, st.Position, sec.Id, pos))) continue;   // nicht erreichbar → überspringen
            wps.Add((sec.Id, pos));
        }
        if (wps.Count < r.PatrolWaypointsMin) return null;
        if (!u.Actors.TryGetValue(st.Owner, out var client)) return null;
        float reward = MathF.Round(r.MissionPatrolBaseReward + wps.Count * r.MissionPatrolRewardPerWaypoint);
        if (MissionRules.SpentToday(u, st.Owner.Value) + reward > MissionRules.BudgetOf(u, client) + 0.0001f) return null;
        if (FactionViability.Spendable(u, client) < reward - 0.0001f) return null;
        var o = new MissionOffer { Type = KeyId, Reward = reward };
        o.Args["wps"] = string.Join(";", wps.Select(w => $"{w.Item1.Value}:{w.Item2.X.ToString("0.###", Inv)}:{w.Item2.Y.ToString("0.###", Inv)}"));
        o.Args["wp"] = "0";
        o.Args["channel"] = "comms";
        o.Args["contract"] = "0";
        return o;
    }

    public string? AcceptIssue(Universe u, MissionOffer o, Ship ship, bool remote)
    {
        var wps = WaypointsOf(new Mission { Args = new Dictionary<string, string>(o.Args) });
        if (wps.Count < u.Rules.PatrolWaypointsMin) return "Zu wenige Wegpunkte.";
        foreach (var (sec, _) in wps)
            if (!u.Sectors.ContainsKey(sec)) return "Ein Wegpunkt-Sektor existiert nicht mehr.";
        return null;
    }

    public Mission Accept(Universe u, MissionOffer o, Ship ship, bool remote)
    {
        // Frist: geschätzte Flugzeit über alle Wegpunkte in Reihenfolge × Faktor, mindestens Mindestfrist
        float t = 0f;
        var fromSec = ship.Sector;
        var fromPos = ship.Position;
        foreach (var (sec, pos) in WaypointsOf(new Mission { Args = new Dictionary<string, string>(o.Args) }))
        {
            float leg = Travel.Ticks(u, fromSec, fromPos, sec, pos, ship);
            if (float.IsInfinity(leg) || float.IsNaN(leg)) leg = u.Rules.MissionMinDeadlineTicks;
            t += leg;
            fromSec = sec;
            fromPos = pos;
        }
        return new Mission
        {
            Type = KeyId, Client = o.Client, At = o.At,
            Args = new Dictionary<string, string>(o.Args),
            Deadline = u.Tick + Math.Max(u.Rules.MissionMinDeadlineTicks, (long)(t * u.Rules.MissionDeadlineFactor)),
        };
    }

    public void OnEvent(Universe u, Mission m, SimEvent e) { }   // Patrouille zählt keinen Waren-/Schiffsfluss

    public bool TargetGone(Universe u, Mission m) => false;   // Wegpunkte sind Sektoren, keine Stationen

    public (SectorId Sector, Vec2 Pos, string Label, MissionMarkerKind Kind)? Marker(Universe u, Mission m)
    {
        var wps = WaypointsOf(m);
        int cur = Math.Min(CurrentOf(m), wps.Count - 1);
        if (cur < 0) return null;
        return (wps[cur].Sector, wps[cur].Pos, Describe(u, m), MissionMarkerKind.Point);
    }

    public string TargetText(Universe u, MissionOffer o)
    {
        var wps = WaypointsOf(new Mission { Args = new Dictionary<string, string>(o.Args) });
        return wps.Count == 0 ? "" : $"{wps.Count} Wegpunkte";
    }

    public long DeadlineOf(Universe u, MissionOffer o, Ship ship)
    {
        float t = 0f;
        var fromSec = ship.Sector;
        var fromPos = ship.Position;
        foreach (var (sec, pos) in WaypointsOf(new Mission { Args = new Dictionary<string, string>(o.Args) }))
        {
            float leg = Travel.Ticks(u, fromSec, fromPos, sec, pos, ship);
            if (float.IsInfinity(leg) || float.IsNaN(leg)) leg = u.Rules.MissionMinDeadlineTicks;
            t += leg;
            fromSec = sec;
            fromPos = pos;
        }
        return Math.Max(u.Rules.MissionMinDeadlineTicks, (long)(t * u.Rules.MissionDeadlineFactor));
    }

    public string DescribeOffer(Universe u, MissionOffer o)
    {
        var wps = WaypointsOf(new Mission { Args = new Dictionary<string, string>(o.Args) });
        string names = string.Join(", ", wps.Select(w => u.Sectors.TryGetValue(w.Sector, out var s) ? s.Name : $"#{w.Sector.Value}"));
        return $"Patrouille: {wps.Count} Wegpunkte nacheinander anfliegen ({names}) — Belohnung {o.Reward:0} Cr.";
    }

    public string Describe(Universe u, Mission m)
    {
        var wps = WaypointsOf(m);
        int cur = Math.Min(CurrentOf(m), wps.Count - 1);
        string next = cur >= 0 && u.Sectors.TryGetValue(wps[cur].Sector, out var s) ? s.Name : "?";
        long left = Math.Max(0L, m.Deadline - u.Tick);
        return $"Auftrag: Patrouille Wegpunkt {Math.Min(CurrentOf(m) + 1, wps.Count)}/{wps.Count} → {next} · {(left / 240)}:{left % 240 / 4:00} min";
    }

    public void Check(Universe u, Mission m)
    {
        if (u.Tick >= m.Deadline) { MissionRules.Fail(u, m, "Frist abgelaufen"); return; }
        var wps = WaypointsOf(m);
        int cur = CurrentOf(m);
        if (cur >= wps.Count) { MissionRules.Complete(u, m); return; }
        // nur der nächste Wegpunkt zählt (Reihenfolge); aktives Schiff des Spielers muss im Radius sein
        if (u.Ships.TryGetValue(u.PlayerShip, out var me) && me.Sector == wps[cur].Sector
            && Vec2.Distance(me.Position, wps[cur].Pos) <= u.Rules.PatrolWaypointRadius)
        {
            m.Args["wp"] = (cur + 1).ToString(Inv);
            if (cur + 1 >= wps.Count) MissionRules.Complete(u, m);
        }
    }

    public string? DeliverIssue(Universe u, Mission m, Ship ship, Station st) => "Patrouille erfüllt sich durch Anfliegen der Wegpunkte — nicht durch Abliefern.";
    public void Deliver(Universe u, Mission m, Ship ship, Station st) => throw new InvalidOperationException("Patrouille braucht keine Ablieferung.");
}
