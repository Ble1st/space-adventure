using System.Globalization;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Missions;

/// <summary>
/// Runde 42: Auftragsart <b>Kopfgeldjagd</b> — eine Sektor-HQ/Hauptquartier einer Fraktion in bedrohten Gebiet bietet an, ein
/// <b>bestimmtes Kaperschiff</b> (Raider einer Outlaw-Gruppe, bei der Fraktion gesucht — „gesuchter Akteur“: das Angebot nennt
/// den Akteur, das Ziel ist eines seiner Schiffe) zu zerstören. Erfüllt nur, wenn das Ziel <b>nach</b> der Annahme vom Spieler
/// (aktives Schiff oder eigene Flotte — ein Spieler-Schiff auf der Gegenseite des Gefechts) zerstört wird. Belohnung aus
/// <see cref="FactionViability.Spendable"/> der Auftraggeber-Fraktion als eigener Posten (zusätzlich zum normalen Kopfgeld im
/// Kopfgeldbüro — keine Doppelzahlung derselben Summe). Scheitern bei Fristablauf oder wenn das Ziel anders verschwindet.
/// Marker: zuletzt bekannte Position des Ziels (Schiff, solange es existiert), sonst nur dessen Sektor.
/// </summary>
public sealed class BountyMission : IMissionType
{
    public const string KeyId = "bounty";
    public string Key => KeyId;

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Ist die Angebots-Station ein Sektor-HQ oder Hauptquartier (dort hängen die Fraktionsaufträge)?</summary>
    public static bool IsOfferStation(Universe u, Station st) =>
        u.StationTypes.TryGetValue(st.TypeKey, out var t) && t.Government is GovernmentKind.SectorHq or GovernmentKind.Headquarters;

    /// <summary>Ziel-Schiff-Id (Args „target“).</summary>
    public static ShipId TargetOf(Mission m) => new(int.Parse(m.Args["target"]));
    /// <summary>Auftraggeber-seitiger Akteur des Ziels (Args „actor“; Anzeige).</summary>
    public static ActorId ActorOf(Mission m) => new(int.Parse(m.Args["actor"]));
    static ShipId TargetOf(MissionOffer o) => new(int.Parse(o.Args["target"]));

    /// <summary>Ist ein Spieler-Schiff (aktives oder Flottenschiff) auf der Gegenseite des zerstörten Schiffs im Gefecht?</summary>
    public static bool PlayerInvolved(Universe u, ShipId destroyed)
    {
        foreach (var e in u.Fights.Encounters)
        {
            var p = e.Participants.FirstOrDefault(q => q.Ship == destroyed);
            if (p is null) continue;
            foreach (var q in e.Participants)
                if (q.Ship != destroyed && q.Side != p.Side && u.Ships.TryGetValue(q.Ship, out var qs) && qs.IsPlayer)
                    return true;
        }
        return false;
    }

    public MissionOffer? Generate(Universe u, Station st, DeterministicRng rng, long bucket)
    {
        var r = u.Rules;
        if (MissionRules.Forbidden(u, st.Owner) || MissionRules.Blocked(u, st.Owner.Value)) return null;
        if (!IsOfferStation(u, st)) return null;
        if (u.FactionOf(st.Owner) is not { } f || !u.IsNpcFaction(f)) return null;
        // Bedrohung im Gebiet der Fraktion (PLAN 3.1)
        if (!u.Sectors.Values.Any(s => s.Owner == f && s.Threat >= r.MissionBountyMinThreat)) return null;
        if (MissionRng.NextFloat(rng) >= r.MissionBountyChance) return null;
        // Kandidaten: Kaperschiffe von Outlaw-Akteuren, auf die die Fraktion ein Kopfgeld hat
        var cands = u.Ships.Values
            .Where(s => ThreatMap.IsRaider(s) && u.Actors.TryGetValue(s.Owner, out var a) && a.Kind == ActorKind.Outlaw && Wanted.BountyOn(u, s.Owner, f) > 0f)
            .OrderBy(s => s.Id.Value).ToList();
        if (cands.Count == 0) return null;
        var pick = cands[MissionRng.NextInt(rng, cands.Count)];
        if (!u.Actors.TryGetValue(st.Owner, out var client)) return null;
        // Belohnung = eigener Posten (max Kopfgeld, Grundbetrag) aus der Fraktionskasse — nie dieselbe Summe wie das Büro-Kopfgeld
        float reward = MathF.Round(MathF.Max(Wanted.BountyOn(u, pick.Owner, f), r.MissionBountyBaseReward));
        if (MissionRules.SpentToday(u, st.Owner.Value) + reward > MissionRules.BudgetOf(u, client) + 0.0001f) return null;
        if (FactionViability.Spendable(u, client) < reward - 0.0001f) return null;
        var (sec, pos) = ThreatMap.Where(u, pick);
        var o = new MissionOffer { Type = KeyId, Reward = reward };
        o.Args["target"] = pick.Id.Value.ToString(Inv);
        o.Args["actor"] = pick.Owner.Value.ToString(Inv);
        o.Args["lastSector"] = sec.Value.ToString(Inv);
        o.Args["lastX"] = pos.X.ToString("0.###", Inv);
        o.Args["lastY"] = pos.Y.ToString("0.###", Inv);
        o.Args["channel"] = "comms";
        o.Args["contract"] = "0";
        return o;
    }

    public string? AcceptIssue(Universe u, MissionOffer o, Ship ship, bool remote)
    {
        if (!u.Ships.TryGetValue(TargetOf(o), out var t) || !ThreatMap.IsRaider(t)) return "Das Zielschiff ist nicht mehr da.";
        return null;
    }

    public Mission Accept(Universe u, MissionOffer o, Ship ship, bool remote)
    {
        var tgt = u.Ships[TargetOf(o)];
        var (ts, tp) = ThreatMap.Where(u, tgt);
        float t = Travel.Ticks(u, ship.Sector, ship.Position, ts, tp, ship);
        if (float.IsInfinity(t) || float.IsNaN(t)) t = u.Rules.MissionMinDeadlineTicks;
        return new Mission
        {
            Type = KeyId, Client = o.Client, At = o.At,
            Args = new Dictionary<string, string>(o.Args),
            Deadline = u.Tick + Math.Max(u.Rules.MissionMinDeadlineTicks, (long)(t * u.Rules.MissionDeadlineFactor)),
        };
    }

    public void OnEvent(Universe u, Mission m, SimEvent e)
    {
        switch (e)
        {
            case CombatExited ce when ce.Exit == CombatExit.Destroyed && ce.Ship == TargetOf(m):
                // nur Zerstörung NACH Annahme durch den Spieler erfüllt (aktives Schiff oder eigene Flotte auf der Gegenseite)
                if (PlayerInvolved(u, ce.Ship)) MissionRules.Complete(u, m);
                else MissionRules.Fail(u, m, "Ziel anders zerstört");
                break;
            case ShipRemoved sr when sr.Ship == TargetOf(m):
                MissionRules.Fail(u, m, "Ziel verschwunden");   // ohne Spieler-Kill: anders verschwunden (z. B. Polizei, Aufgabe)
                break;
        }
    }

    public bool TargetGone(Universe u, Mission m) => false;   // Ziel ist ein Schiff (Check/OnEvent), keine Station

    public (SectorId Sector, Vec2 Pos, string Label, MissionMarkerKind Kind)? Marker(Universe u, Mission m)
    {
        if (u.Ships.TryGetValue(TargetOf(m), out var t))
        {
            var (sec, pos) = ThreatMap.Where(u, t);
            return (sec, pos, Describe(u, m), MissionMarkerKind.Ship);
        }
        // Ziel nicht (mehr) im Spiel: zuletzt bekannte Position, sonst nur der Sektor
        if (m.Args.TryGetValue("lastSector", out var ls) && int.TryParse(ls, out var lv)
            && u.Sectors.ContainsKey(new SectorId(lv)))
        {
            var pos = new Vec2(
                m.Args.TryGetValue("lastX", out var xs) && float.TryParse(xs, NumberStyles.Float, Inv, out var x) ? x : 0f,
                m.Args.TryGetValue("lastY", out var ys) && float.TryParse(ys, NumberStyles.Float, Inv, out var y) ? y : 0f);
            return (new SectorId(lv), pos, Describe(u, m), MissionMarkerKind.Point);
        }
        return null;
    }

    public string TargetText(Universe u, MissionOffer o)
    {
        if (!u.Ships.TryGetValue(TargetOf(o), out var t)) return "Ziel weg";
        var (sec, _) = ThreatMap.Where(u, t);
        string sector = u.Sectors.TryGetValue(sec, out var s) ? s.Name : $"#{sec.Value}";
        return $"Kaperschiff {t.Id.Value} · {sector}";
    }

    public long DeadlineOf(Universe u, MissionOffer o, Ship ship)
    {
        if (!u.Ships.TryGetValue(TargetOf(o), out var tgt)) return u.Rules.MissionMinDeadlineTicks;
        var (ts, tp) = ThreatMap.Where(u, tgt);
        float t = Travel.Ticks(u, ship.Sector, ship.Position, ts, tp, ship);
        if (float.IsInfinity(t) || float.IsNaN(t)) t = u.Rules.MissionMinDeadlineTicks;
        return Math.Max(u.Rules.MissionMinDeadlineTicks, (long)(t * u.Rules.MissionDeadlineFactor));
    }

    public string DescribeOffer(Universe u, MissionOffer o)
    {
        string actor = u.Actors.TryGetValue(new ActorId(int.Parse(o.Args["actor"])), out var a) ? ActorNames.Tagged(u, a.Id) : "?";
        string sector = int.TryParse(o.Args["lastSector"], out var sv) && u.Sectors.TryGetValue(new SectorId(sv), out var s) ? s.Name : "?";
        return $"Kopfgeldjagd: Kaperschiff {o.Args["target"]} von {actor} zerstören (zuletzt in {sector}) — Belohnung {o.Reward:0} Cr zusätzlich zum Kopfgeld im Büro, Frist bei Annahme berechnet.";
    }

    public string Describe(Universe u, Mission m)
    {
        string actor = u.Actors.TryGetValue(ActorOf(m), out var a) ? ActorNames.Tagged(u, a.Id) : "?";
        string where = u.Ships.ContainsKey(TargetOf(m)) && u.Sectors.TryGetValue(ThreatMap.Where(u, u.Ships[TargetOf(m)]).Sector, out var s) ? s.Name
            : (m.Args.TryGetValue("lastSector", out var ls) && int.TryParse(ls, out var lv) && u.Sectors.TryGetValue(new SectorId(lv), out var s2) ? s2.Name : "verschwunden");
        long left = Math.Max(0L, m.Deadline - u.Tick);
        return $"Auftrag: Kopfgeld {actor} — Ziel in {where} · {(left / 240)}:{left % 240 / 4:00} min";
    }

    public void Check(Universe u, Mission m)
    {
        if (u.Tick >= m.Deadline) { MissionRules.Fail(u, m, "Frist abgelaufen"); return; }
        if (!u.Ships.TryGetValue(TargetOf(m), out var t)) { MissionRules.Fail(u, m, "Ziel verschwunden"); return; }
        // letzte bekannte Position merken (Marker, falls das Ziel den Sektor verlässt)
        var (sec, pos) = ThreatMap.Where(u, t);
        m.Args["lastSector"] = sec.Value.ToString(Inv);
        m.Args["lastX"] = pos.X.ToString("0.###", Inv);
        m.Args["lastY"] = pos.Y.ToString("0.###", Inv);
    }

    public string? DeliverIssue(Universe u, Mission m, Ship ship, Station st) => "Kopfgeldjagd erfüllt sich durch Zerstören des Ziels — nicht durch Abliefern.";
    public void Deliver(Universe u, Mission m, Ship ship, Station st) => throw new InvalidOperationException("Kopfgeldjagd braucht keine Ablieferung.");
}
