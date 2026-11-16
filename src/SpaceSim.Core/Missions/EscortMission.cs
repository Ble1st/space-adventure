using System.Globalization;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Missions;

/// <summary>
/// Runde 42: Auftragsart <b>Eskorte</b> — ein beladener NPC-Händler der Auftraggeber-Fraktion (oder ein freier Händler) fliegt
/// eine Route durch deren Gebiet; der Spieler muss den Großteil der Strecke im Sektor des Schützlings und innerhalb
/// <see cref="EconomyRules.EscortRange"/> sein (Stichproben alle <see cref="EconomyRules.EscortSampleTicks"/> Ticks, Anteil ≥
/// <see cref="EconomyRules.EscortMinShare"/>). Erfüllt bei Ankunft des Schützlings (<see cref="ShipArrived"/>), Scheitern bei
/// dessen Zerstörung/Verschwinden oder Frist. <b>Rein beobachtend</b>: das Verhalten des Schützlings wird nicht verändert
/// (keine Route, kein Befehl, kein Andocken). Marker auf den Schützling. Belohnung aus der Fraktionskasse.
/// </summary>
public sealed class EscortMission : IMissionType
{
    public const string KeyId = "escort";
    public string Key => KeyId;

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Schützling-Schiff-Id (Args „escort“).</summary>
    public static ShipId EscortOf(Mission m) => new(int.Parse(m.Args["escort"]));
    static ShipId EscortOf(MissionOffer o) => new(int.Parse(o.Args["escort"]));
    /// <summary>Zielsektor der Eskorte (Args „dest“).</summary>
    public static SectorId DestOf(MissionOffer o) => new(int.Parse(o.Args["dest"]));
    /// <summary>Ankunfts-Tick der Eskorte (Args „arr“).</summary>
    public static long ArrivalOf(MissionOffer o) => long.Parse(o.Args["arr"], Inv);
    /// <summary>Stichproben bisher (Args „samples“).</summary>
    public static int SamplesOf(Mission m) => m.Args.TryGetValue("samples", out var s) ? int.Parse(s, Inv) : 0;
    /// <summary>Davon in Reichweite (Args „hits“).</summary>
    public static int HitsOf(Mission m) => m.Args.TryGetValue("hits", out var s) ? int.Parse(s, Inv) : 0;
    /// <summary>Anteil in Reichweite.</summary>
    public static float ShareOf(Mission m)
    {
        int n = SamplesOf(m);
        return n > 0 ? (float)HitsOf(m) / n : 0f;
    }

    /// <summary>Route des Händlers berührt Gebiet der Fraktion (aktueller Sektor oder Zielsektor gehört ihr)?</summary>
    static bool RouteTouches(Universe u, Ship s, FactionId f)
    {
        var t = s.Transit!;
        var (cur, _) = AccessEnforcement.Where(t, u.Tick);
        if (u.Sectors.TryGetValue(cur, out var cs) && cs.Owner == f) return true;
        if (u.Sectors.TryGetValue(t.ToSector, out var ds) && ds.Owner == f) return true;
        return false;
    }

    public MissionOffer? Generate(Universe u, Station st, DeterministicRng rng, long bucket)
    {
        var r = u.Rules;
        if (MissionRules.Forbidden(u, st.Owner) || MissionRules.Blocked(u, st.Owner.Value)) return null;
        if (!BountyMission.IsOfferStation(u, st)) return null;
        if (u.FactionOf(st.Owner) is not { } f || !u.IsNpcFaction(f)) return null;
        if (MissionRng.NextFloat(rng) >= r.MissionEscortChance) return null;
        // Kandidaten: beladene NPC-Händler der Fraktion (oder freie Händler) auf langer Reststrecke durch Fraktionsgebiet
        var cands = u.Ships.Values
            .Where(s => s.Role == ShipRole.Trader && s.Controller == Controller.Ai && s.Transit is { } t
                && (u.FactionOf(s.Owner) == f || s.Owner == u.FreeActor)
                && ThreatMap.CargoValue(u, s) >= r.EscortMinCargoValue
                && t.ArrivalTick - u.Tick >= r.EscortMinRemainingTicks
                && RouteTouches(u, s, f))
            .OrderBy(s => s.Id.Value).ToList();
        if (cands.Count == 0) return null;
        var pick = cands[MissionRng.NextInt(rng, cands.Count)];
        if (!u.Actors.TryGetValue(st.Owner, out var client)) return null;
        float cargo = ThreatMap.CargoValue(u, pick);
        float reward = MathF.Round(MathF.Min(r.MissionEscortMaxReward, r.MissionEscortBaseReward + cargo * r.MissionEscortCargoShare));
        if (MissionRules.SpentToday(u, st.Owner.Value) + reward > MissionRules.BudgetOf(u, client) + 0.0001f) return null;
        if (FactionViability.Spendable(u, client) < reward - 0.0001f) return null;
        var o = new MissionOffer { Type = KeyId, Reward = reward };
        o.Args["escort"] = pick.Id.Value.ToString(Inv);
        o.Args["dest"] = pick.Transit!.ToSector.Value.ToString(Inv);
        o.Args["arr"] = pick.Transit.ArrivalTick.ToString(Inv);
        o.Args["samples"] = "0";
        o.Args["hits"] = "0";
        o.Args["lastSample"] = "-1";
        o.Args["channel"] = "comms";
        o.Args["contract"] = "0";
        return o;
    }

    public string? AcceptIssue(Universe u, MissionOffer o, Ship ship, bool remote)
    {
        if (!u.Ships.TryGetValue(EscortOf(o), out var s) || s.Transit is not { } t || t.ArrivalTick != ArrivalOf(o))
            return "Der Schützling ist nicht mehr auf dieser Strecke unterwegs.";
        return null;
    }

    public Mission Accept(Universe u, MissionOffer o, Ship ship, bool remote) => new()
    {
        Type = KeyId, Client = o.Client, At = o.At,
        Args = new Dictionary<string, string>(o.Args),
        // Frist bis kurz nach der Ankunft des Schützlings (die Ankunft selbst entscheidet); Offline-Aufholen verlängert mit
        Deadline = ArrivalOf(o) + u.Rules.MissionMinDeadlineTicks,
    };

    public void OnEvent(Universe u, Mission m, SimEvent e)
    {
        switch (e)
        {
            case ShipArrived sa when sa.Ship == EscortOf(m):
                // Ankunft des Schützlings: erfüllt, wenn der Großteil der Stichproben in Reichweite war
                if (ShareOf(m) >= u.Rules.EscortMinShare - 0.0001f) MissionRules.Complete(u, m);
                else MissionRules.Fail(u, m, "zu wenig begleitet");
                break;
            case ShipRemoved sr when sr.Ship == EscortOf(m):
                MissionRules.Fail(u, m, sr.Reason == "destroyed" ? "Schützling zerstört" : "Schützling verschwunden");
                break;
        }
    }

    public bool TargetGone(Universe u, Mission m) => false;   // Ziel ist ein Schiff (Check/OnEvent), keine Station

    public (SectorId Sector, Vec2 Pos, string Label, MissionMarkerKind Kind)? Marker(Universe u, Mission m)
    {
        if (!u.Ships.TryGetValue(EscortOf(m), out var s)) return null;
        var (sec, pos) = ThreatMap.Where(u, s);
        return (sec, pos, Describe(u, m), MissionMarkerKind.Ship);
    }

    public string TargetText(Universe u, MissionOffer o)
    {
        if (!u.Ships.TryGetValue(EscortOf(o), out var s)) return "Schützling weg";
        string dest = u.Sectors.TryGetValue(DestOf(o), out var ds) ? ds.Name : $"#{o.Args["dest"]}";
        string owner = u.ActorName(s.Owner);
        return $"Händler {s.Id.Value} ({owner}) → {dest}";
    }

    public long DeadlineOf(Universe u, MissionOffer o, Ship ship) =>
        Math.Max(0L, ArrivalOf(o) + u.Rules.MissionMinDeadlineTicks - u.Tick);

    public string DescribeOffer(Universe u, MissionOffer o)
    {
        string dest = u.Sectors.TryGetValue(DestOf(o), out var ds) ? ds.Name : $"#{o.Args["dest"]}";
        return $"Eskorte: Händler {o.Args["escort"]} nach {dest} begleiten (Großteil der Strecke ≤ {u.Rules.EscortRange:0} m im selben Sektor) — Belohnung {o.Reward:0} Cr.";
    }

    public string Describe(Universe u, Mission m)
    {
        string dest = m.Args.TryGetValue("dest", out var ds) && int.TryParse(ds, out var dv) && u.Sectors.TryGetValue(new SectorId(dv), out var s) ? s.Name : "?";
        long left = Math.Max(0L, m.Deadline - u.Tick);
        return $"Auftrag: Eskorte Händler {m.Args["escort"]} → {dest} · begleitet {HitsOf(m)}/{SamplesOf(m)} · {(left / 240)}:{left % 240 / 4:00} min";
    }

    /// <summary>
    /// Stichprobe (alle <see cref="EconomyRules.EscortSampleTicks"/> Ticks, über „lastSample“ gegen Doppelzählung gesichert):
    /// mindestens ein Spieler-Schiff im Sektor des Schützlings und innerhalb <see cref="EconomyRules.EscortRange"/> → Treffer.
    /// Rein beobachtend — der Schützling wird nicht angefasst.
    /// </summary>
    void Sample(Universe u, Mission m)
    {
        long last = m.Args.TryGetValue("lastSample", out var ls) && long.TryParse(ls, Inv, out var lv) ? lv : -1L;
        if (u.Tick - last < u.Rules.EscortSampleTicks) return;
        m.Args["lastSample"] = u.Tick.ToString(Inv);
        int samples = SamplesOf(m) + 1, hits = HitsOf(m);
        if (u.Ships.TryGetValue(EscortOf(m), out var escort))
        {
            var (esec, epos) = ThreatMap.Where(u, escort);
            foreach (var ps in u.Ships.Values.Where(x => x.IsPlayer))
            {
                var (psec, ppos) = ThreatMap.Where(u, ps);
                if (psec == esec && Vec2.Distance(ppos, epos) <= u.Rules.EscortRange) { hits++; break; }
            }
        }
        m.Args["samples"] = samples.ToString(Inv);
        m.Args["hits"] = hits.ToString(Inv);
    }

    public void Check(Universe u, Mission m)
    {
        if (u.Tick >= m.Deadline) { MissionRules.Fail(u, m, "Frist abgelaufen"); return; }
        if (!u.Ships.ContainsKey(EscortOf(m))) { MissionRules.Fail(u, m, "Schützling verschwunden"); return; }
        Sample(u, m);
    }

    public string? DeliverIssue(Universe u, Mission m, Ship ship, Station st) => "Eskorte erfüllt sich bei Ankunft des Schützlings — nicht durch Abliefern.";
    public void Deliver(Universe u, Mission m, Ship ship, Station st) => throw new InvalidOperationException("Eskorte braucht keine Ablieferung.");
}
