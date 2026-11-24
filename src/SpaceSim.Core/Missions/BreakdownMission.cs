using System.Globalization;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Missions;

/// <summary>
/// Auftragsart <b>Panne</b> (Lücke gegenüber Interstellar Pilot 2): Sektor-HQ, Hauptquartier, Polizeiwache oder Werft einer Fraktion melden ein
/// liegengebliebenes Schiff der Fraktion. Das Angebot nennt Ort und Modell (rein, ohne Weltänderung); erst die Annahme legt das Schiff als herrenloses,
/// gesperrtes Schiff der Fraktion an (<see cref="Derelicts.Create"/>, kein Verfall). Der Spieler fliegt hin, übernimmt es (<see cref="Derelicts.Claim"/> —
/// nur mit diesem Auftrag; es folgt ihm dann) und bringt es zur Auftragsstation: liegt es dort angedockt, ist der Auftrag erfüllt, das Schiff geht an die
/// Fraktion zurück (aus der Welt, <see cref="DepartureReason.Returned"/>). Fliegt die Person selbst das Pannenschiff, steht sie danach zu Fuß in der Station
/// (das vorige Schiff wird wieder das aktive). Scheitern (Frist, Abbruch) holt die Fraktion ihr Schiff zurück.
/// </summary>
public sealed class BreakdownMission : IMissionType
{
    public const string KeyId = "breakdown";
    public string Key => KeyId;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Wo das Pannenschiff liegt (Sektor, Position) und welches Modell es ist.</summary>
    static (SectorId Sector, Vec2 Pos, string Model) Spot(IReadOnlyDictionary<string, string> a) =>
        (new SectorId(int.Parse(a["sector"], Inv)), new Vec2(float.Parse(a["x"], Inv), float.Parse(a["y"], Inv)), a["model"]);

    /// <summary>Pannenschiff des Auftrags (Args „ship“), null = (noch) keins.</summary>
    public static Ship? ShipOf(Universe u, Mission m) =>
        int.TryParse(m.Args.GetValueOrDefault("ship"), out var v) && u.Ships.TryGetValue(new ShipId(v), out var s) ? s : null;

    static bool IsOfferStation(Universe u, Station st) =>
        BountyMission.IsOfferStation(u, st) || u.StationTypes.TryGetValue(st.TypeKey, out var t) && t.ShipDealer is not null;

    public MissionOffer? Generate(Universe u, Station st, DeterministicRng rng, long bucket)
    {
        var r = u.Rules;
        if (r.MissionBreakdownChance <= 0f || !IsOfferStation(u, st)) return null;
        if (u.FactionOf(st.Owner) is not { } f || !u.IsNpcFaction(f)) return null;
        if (MissionRules.Forbidden(u, st.Owner) || MissionRules.Blocked(u, st.Owner.Value)) return null;
        if (MissionRng.NextFloat(rng) >= r.MissionBreakdownChance) return null;
        // Ort: ein Sektor der Fraktion (höchstens zwei Sprünge), sonst der eigene
        var sectors = u.Sectors.Values.Where(s => s.Owner == f && u.IsReachable(st.Sector, s.Id) && u.Hops(st.Sector, s.Id) <= 2).OrderBy(s => s.Id.Value).ToList();
        var sec = sectors.Count > 0 ? sectors[MissionRng.NextInt(rng, sectors.Count)] : u.Sectors[st.Sector];
        var anchors = sec.Stations.Where(id => u.Stations.ContainsKey(id)).Select(id => u.Stations[id].Position)
            .Concat(sec.Gates.Where(id => u.Gates.ContainsKey(id)).Select(id => u.Gates[id].Position)).ToList();
        var anchor = anchors.Count == 0 ? default : anchors[MissionRng.NextInt(rng, anchors.Count)];
        if (Derelicts.Spot(u, sec, anchor) is not { } pos) return null;
        var models = u.ShipModels.Values.Where(m => m.Jobs.Count > 0 && u.ShipClasses.ContainsKey(m.ClassKey)).OrderBy(m => m.Key, StringComparer.Ordinal).ToList();
        if (models.Count == 0) return null;
        var model = models[MissionRng.NextInt(rng, models.Count)];
        long trip = MissionRules.TripTicks(u, st.Sector, st.DockPoint(default), sec.Id, pos, null);
        float reward = MathF.Round(r.MissionBreakdownBaseReward + r.MissionBreakdownRewardPerMinute * 2f * trip / (60f * SimClock.TicksPerSecond));
        if (!u.Actors.TryGetValue(st.Owner, out var client)) return null;
        if (MissionRules.SpentToday(u, st.Owner.Value) + reward > MissionRules.BudgetOf(u, client) + 0.0001f) return null;
        if (FactionViability.Spendable(u, client) < reward - 0.0001f) return null;
        var o = new MissionOffer { Type = KeyId, Reward = reward };
        o.Args["sector"] = sec.Id.Value.ToString(Inv);
        o.Args["x"] = pos.X.ToString("0.##", Inv);
        o.Args["y"] = pos.Y.ToString("0.##", Inv);
        o.Args["model"] = model.Key;
        o.Args["channel"] = "comms";
        o.Args["contract"] = "0";
        return o;
    }

    public string? AcceptIssue(Universe u, MissionOffer o, Ship ship, bool remote)
    {
        var (sector, _, model) = Spot(o.Args);
        if (!u.Sectors.ContainsKey(sector)) return "Der Ort existiert nicht mehr.";
        if (!u.ShipModels.ContainsKey(model)) return "Unbekanntes Schiffsmodell.";
        return null;
    }

    public Mission Accept(Universe u, MissionOffer o, Ship ship, bool remote)
    {
        var (sector, pos, model) = Spot(o.Args);
        var d = Derelicts.Create(u, model, sector, pos, o.Client, 0.45f, 0)!;
        var m = new Mission
        {
            Type = KeyId, Client = o.Client, At = o.At,
            Args = new Dictionary<string, string>(o.Args) { ["ship"] = d.Id.Value.ToString(Inv), ["prev"] = ship.Id.Value.ToString(Inv) },
        };
        m.Deadline = u.Tick + DeadlineOf(u, o, ship);
        u.Events.Publish(new DerelictCreated(d.Id, sector, default, "breakdown",
            $"Liegengeblieben: {Derelicts.Name(u, d)} in {u.Sectors[sector].Name} — hinfliegen und per Funk übernehmen."));
        return m;
    }

    /// <summary>Steigt die Person ins Pannenschiff um, endet dessen „Folge mir“ (sie fliegt es selbst zur Auftragsstation).</summary>
    public void OnEvent(Universe u, Mission m, SimEvent e)
    {
        if (e is ActiveShipChanged ac && ShipOf(u, m) is { } s && ac.New == s.Id && s.Command is FollowCommand)
            Commands.Cancel(u, s, "selbst geflogen");
    }

    public bool TargetGone(Universe u, Mission m) => !u.Stations.ContainsKey(m.At) || ShipOf(u, m) is null;

    public (SectorId Sector, Vec2 Pos, string Label, MissionMarkerKind Kind)? Marker(Universe u, Mission m)
    {
        if (ShipOf(u, m) is not { } s) return null;
        if (s.IsDerelict) return (s.Sector, s.Position, Describe(u, m), MissionMarkerKind.Ship);
        return u.Stations.TryGetValue(m.At, out var st) ? (st.Sector, st.Position, Describe(u, m), MissionMarkerKind.Station) : null;
    }

    public string DescribeOffer(Universe u, MissionOffer o)
    {
        var (sector, _, model) = Spot(o.Args);
        string mname = u.ShipModels.TryGetValue(model, out var md) ? md.Name : model;
        string sname = u.Sectors.TryGetValue(sector, out var sec) ? sec.Name : "?";
        string home = u.Stations.TryGetValue(o.At, out var st) ? st.Name : "die Station";
        return $"Panne: {mname} in {sname} liegengeblieben — übernehmen und nach {home} bringen. Belohnung {o.Reward:0} Cr.";
    }

    public string TargetText(Universe u, MissionOffer o)
    {
        var (sector, _, _) = Spot(o.Args);
        return u.Sectors.TryGetValue(sector, out var sec) ? sec.Name : "";
    }

    public long DeadlineOf(Universe u, MissionOffer o, Ship ship)
    {
        var (sector, pos, _) = Spot(o.Args);
        if (!u.Stations.TryGetValue(o.At, out var st)) return u.Rules.MissionMinDeadlineTicks;
        long there = MissionRules.TripTicks(u, ship.Sector, ship.Position, sector, pos, ship);
        long back = MissionRules.TripTicks(u, sector, pos, st.Sector, st.DockPoint(default), null);
        return Math.Max(u.Rules.MissionMinDeadlineTicks, (long)((there + back) * Math.Max(2f, u.Rules.MissionDeadlineFactor)));
    }

    public string Describe(Universe u, Mission m)
    {
        long left = Math.Max(0L, m.Deadline - u.Tick);
        var s = ShipOf(u, m);
        string home = u.Stations.TryGetValue(m.At, out var st) ? st.Name : "?";
        if (s is null) return $"Auftrag #{m.Id}: Pannenschiff weg";
        if (s.IsDerelict)
            return $"Auftrag: Panne — {Derelicts.Name(u, s)} in {(u.Sectors.TryGetValue(s.Sector, out var sec) ? sec.Name : "?")} übernehmen · noch {MissionRules.Rest(left)}";
        return $"Auftrag: Panne — {Derelicts.Name(u, s)} nach {home} bringen · noch {MissionRules.Rest(left)}";
    }

    public void Check(Universe u, Mission m)
    {
        if (u.Tick >= m.Deadline) { MissionRules.Fail(u, m, "Frist abgelaufen"); return; }
        if (TargetGone(u, m)) { MissionRules.Fail(u, m, ShipOf(u, m) is null ? "Schiff verloren" : "Zielstation weg"); return; }
        var s = ShipOf(u, m)!;
        if (!s.IsPlayer || s.Transit is not null || s.DockedAt != m.At) return;
        var st = u.Stations[m.At];
        bool keep = false;
        if (s.Id == u.PlayerShip)
        {
            // die Person fliegt das Pannenschiff selbst: sie steigt in der Station aus, das vorige Schiff wird wieder das aktive
            var prev = int.TryParse(m.Args.GetValueOrDefault("prev"), out var pv) && u.Ships.TryGetValue(new ShipId(pv), out var p0) && p0.IsPlayer && p0.Id != s.Id ? p0
                : u.ShipsById().FirstOrDefault(x => x.IsPlayer && x.Id != s.Id && !x.IsRental);
            if (prev is null) keep = true;   // kein anderes eigenes Schiff: das Pannenschiff bleibt (selten; nie ohne Schiff)
            else
            {
                var old = u.PlayerShip;
                u.PlayerAtStation = st.Id;
                u.PlayerShip = prev.Id;
                u.Journal.Clear();
                u.Events.Publish(new ActiveShipChanged(old, prev.Id, st.Id));
            }
        }
        MissionRules.Complete(u, m);
        if (!keep) Departures.RemoveShip(u, s, DepartureReason.Returned, "breakdown_returned");
    }

    /// <summary>Beim Scheitern (Frist, Abbruch): die Fraktion holt ihr Schiff zurück (aus der Welt). Fliegt die Person es gerade, wechselt sie ins vorige Schiff.</summary>
    public static void Recall(Universe u, Mission m)
    {
        if (ShipOf(u, m) is not { } s) return;
        if (s.Id == u.PlayerShip)
        {
            var prev = int.TryParse(m.Args.GetValueOrDefault("prev"), out var pv) && u.Ships.TryGetValue(new ShipId(pv), out var p0) && p0.IsPlayer && p0.Id != s.Id ? p0
                : u.ShipsById().FirstOrDefault(x => x.IsPlayer && x.Id != s.Id && !x.IsRental);
            if (prev is null) return;   // nie ohne Schiff
            PlayerFleet.Activate(u, prev, prev.DockedAt);
        }
        Departures.RemoveShip(u, s, DepartureReason.Returned, "breakdown_recalled");
    }

    public string? DeliverIssue(Universe u, Mission m, Ship ship, Station st) => "Das Pannenschiff liefert sich durch Andocken an der Auftragsstation ab.";

    public void Deliver(Universe u, Mission m, Ship ship, Station st) { }
}
