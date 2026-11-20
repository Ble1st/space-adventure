using System.Globalization;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Missions;

/// <summary>
/// Ausbau Punkt 10: Auftragsart <b>Forschung</b> — eine Forschungsstation (Typ mit <see cref="StationType.Research"/>) bietet eine von drei Aufgaben an
/// (Missions-RNG): (1) <b>Messpunkte</b> in einem Nebelsektor der Reihe nach anfliegen (Daten gehen sofort an die Station), (2) eine <b>Wrackprobe</b>
/// bergen (Fracht oder Modul aus einem bestimmten Wrack, dann an der Station andocken), (3) eine <b>seltene Ware</b> der Station liefern (netto verkauft wie
/// beim Lieferauftrag). Lohn: Geld aus der Kasse der Auftraggeber-Fraktion (reserviert) und <see cref="EconomyRules.MissionResearchPoints"/> FP auf ihr
/// laufendes Projekt (<see cref="Research.AddPoints"/>) bei Erfüllung.
/// </summary>
public sealed class ResearchMission : IMissionType
{
    public const string KeyId = "research";
    public string Key => KeyId;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string KindOf(Mission m) => m.Args.GetValueOrDefault("kind") ?? "";
    static string KindOf(MissionOffer o) => o.Args.GetValueOrDefault("kind") ?? "";

    /// <summary>Forschungsstation (fertig, mit Forschungsbetrieb)?</summary>
    public static bool IsOfferStation(Universe u, Station st) =>
        !st.IsBuildSite && u.StationTypes.TryGetValue(st.TypeKey, out var t) && t.Research is not null;

    public MissionOffer? Generate(Universe u, Station st, DeterministicRng rng, long bucket)
    {
        var r = u.Rules;
        if (!r.MissionResearch || !IsOfferStation(u, st)) return null;
        if (MissionRules.Forbidden(u, st.Owner) || MissionRules.Blocked(u, st.Owner.Value)) return null;
        if (u.FactionOf(st.Owner) is not { } f || !u.IsNpcFaction(f)) return null;
        if (MissionRng.NextFloat(rng) >= r.MissionResearchChance) return null;
        int variant = MissionRng.NextInt(rng, 3);
        var o = new MissionOffer { Type = KeyId };
        float reward = r.MissionResearchBaseReward;
        if (variant == 0 && Nebula(u, st, rng, o) is { } ticks) { o.Args["kind"] = "nebula"; reward += r.MissionResearchRewardPerMinute * ticks / (60f * SimClock.TicksPerSecond); }
        else if (variant == 1 && Wreck(u, st, o) is { } wticks) { o.Args["kind"] = "wreck"; reward += r.MissionResearchRewardPerMinute * wticks / (60f * SimClock.TicksPerSecond); }
        else if (Goods(u, st, rng, o) is { } value) { o.Args["kind"] = "goods"; reward += value * 0.3f; }
        else return null;
        reward = MathF.Round(reward);
        if (!u.Actors.TryGetValue(st.Owner, out var client)) return null;
        if (MissionRules.SpentToday(u, st.Owner.Value) + reward > MissionRules.BudgetOf(u, client) + 0.0001f) return null;
        if (FactionViability.Spendable(u, client) < reward - 0.0001f) return null;
        o.Reward = reward;
        o.Args["channel"] = "comms";
        o.Args["contract"] = "0";
        return o;
    }

    /// <summary>Zwei Messpunkte in einem erreichbaren Nebelsektor (Format wie die Patrouille); geschätzte Flugzeit oder null.</summary>
    static long? Nebula(Universe u, Station st, DeterministicRng rng, MissionOffer o)
    {
        var secs = u.Sectors.Values.Where(s => s.Nebula && u.IsReachable(st.Sector, s.Id)).OrderBy(s => s.Id.Value).ToList();
        if (secs.Count == 0) return null;
        var sec = secs[MissionRng.NextInt(rng, secs.Count)];
        var pts = new List<Vec2>();
        for (int i = 0; i < 2; i++)
        {
            float ang = MissionRng.NextFloat(rng) * MathF.PI * 2f, rad = (0.3f + 0.4f * MissionRng.NextFloat(rng)) * sec.Extent;
            pts.Add(Vec2.FromAngle(ang) * rad);
        }
        o.Args["wps"] = string.Join(";", pts.Select(p => $"{sec.Id.Value}:{p.X.ToString("0.###", Inv)}:{p.Y.ToString("0.###", Inv)}"));
        o.Args["wp"] = "0";
        return MissionRules.TripTicks(u, st.Sector, st.DockPoint(default), sec.Id, pts[0], null) + MissionRules.TripTicks(u, sec.Id, pts[0], sec.Id, pts[1], null);
    }

    /// <summary>Nächstes erreichbares Wrack mit Fracht oder Modul (Sprünge, dann Id); Flugzeit hin und zurück oder null.</summary>
    static long? Wreck(Universe u, Station st, MissionOffer o)
    {
        Wreck? best = null; int bh = int.MaxValue;
        foreach (var w in u.Wrecks)
        {
            if (w.Cargo.Count == 0 && w.Modules.Count == 0) continue;
            int h = st.Sector == w.Sector ? 0 : u.Hops(st.Sector, w.Sector);
            if (h < 0 || h > bh || (h == bh && best is not null && w.Id > best.Id)) continue;
            best = w; bh = h;
        }
        if (best is null) return null;
        o.Args["wreck"] = best.Id.ToString(Inv);
        o.Args["sector"] = best.Sector.Value.ToString(Inv);
        o.Args["x"] = best.Position.X.ToString("0.###", Inv);
        o.Args["y"] = best.Position.Y.ToString("0.###", Inv);
        o.Args["sampled"] = "0";
        return 2 * MissionRules.TripTicks(u, st.Sector, st.DockPoint(default), best.Sector, best.Position, null);
    }

    /// <summary>Seltene Ware der Station (Regaleintrag, nimmt noch ≥ 3 an); Basiswert oder null.</summary>
    static float? Goods(Universe u, Station st, DeterministicRng rng, MissionOffer o)
    {
        var goods = st.Market.Keys.OrderBy(g => g.Value).Where(g => st.IsStockOnly(g) && st.AcceptableUnits(g, u.Rules) >= 3f).ToList();
        if (goods.Count == 0) return null;
        var good = goods[MissionRng.NextInt(rng, goods.Count)];
        float units = MathF.Floor(MathF.Min(u.Rules.MissionResearchUnits, st.AcceptableUnits(good, u.Rules)));
        o.Args["good"] = good.Value.ToString(Inv);
        o.Args["units"] = units.ToString("0.##", Inv);
        return units * u.GoodOf(good).BasePrice;
    }

    static Mission AsMission(MissionOffer o) => new() { Args = new Dictionary<string, string>(o.Args), At = o.At };

    public string? AcceptIssue(Universe u, MissionOffer o, Ship ship, bool remote)
    {
        if (KindOf(o) == "wreck" && WreckOf(u, AsMission(o)) is null) return "Das Wrack ist schon verfallen.";
        return null;
    }

    public Mission Accept(Universe u, MissionOffer o, Ship ship, bool remote)
    {
        var m = new Mission { Type = KeyId, Client = o.Client, At = o.At, Args = new Dictionary<string, string>(o.Args) { ["ship"] = ship.Id.Value.ToString(Inv) } };
        m.Deadline = u.Tick + DeadlineOf(u, o, ship);
        return m;
    }

    static Wreck? WreckOf(Universe u, Mission m) =>
        m.Args.TryGetValue("wreck", out var w) && int.TryParse(w, out var id) ? u.Wrecks.FirstOrDefault(x => x.Id == id) : null;

    public void OnEvent(Universe u, Mission m, SimEvent e)
    {
        switch (e)
        {
            case WreckSalvaged ws when KindOf(m) == "wreck" && m.Args.TryGetValue("wreck", out var wid) && wid == ws.Wreck.ToString(Inv)
                                     && u.Ships.TryGetValue(ws.Ship, out var sh) && sh.Owner == u.PlayerActor:
                m.Args["sampled"] = "1";
                break;
            case ShipDocked sd when sd.Station == m.At && KindOf(m) == "wreck" && m.Args.GetValueOrDefault("sampled") == "1"
                                    && u.Ships.TryGetValue(sd.Ship, out var ds) && ds.Owner == u.PlayerActor:
                Finish(u, m);
                break;
            case PlayerSold ps when KindOf(m) == "goods" && ps.Station == m.At && ps.Good.Value.ToString(Inv) == m.Args.GetValueOrDefault("good"):
                m.Sold += ps.Units;
                break;
            case PlayerBought pb when KindOf(m) == "goods" && pb.Station == m.At && pb.Good.Value.ToString(Inv) == m.Args.GetValueOrDefault("good"):
                m.Bought += pb.Units;
                break;
        }
    }

    /// <summary>Erfüllt: FP an das laufende Projekt der Auftraggeber-Fraktion, dann Lohn wie jeder Auftrag.</summary>
    public static void Finish(Universe u, Mission m)
    {
        if (m.State != MissionState.Active) return;
        if (u.FactionOf(m.Client) is { } f && u.Factions.TryGetValue(f, out var faction) && Research.CurrentOf(u, faction) is { } p)
            Research.AddPoints(u, faction, p, u.Rules.MissionResearchPoints);
        MissionRules.Complete(u, m);
    }

    public bool TargetGone(Universe u, Mission m) => !u.Stations.ContainsKey(m.At);

    public (SectorId Sector, Vec2 Pos, string Label, MissionMarkerKind Kind)? Marker(Universe u, Mission m)
    {
        switch (KindOf(m))
        {
            case "nebula":
            {
                var wps = PatrolMission.WaypointsOf(m);
                int cur = Math.Min(PatrolMission.CurrentOf(m), wps.Count - 1);
                return cur < 0 ? null : (wps[cur].Sector, wps[cur].Pos, Describe(u, m), MissionMarkerKind.Point);
            }
            case "wreck" when m.Args.GetValueOrDefault("sampled") != "1":
                if (WreckOf(u, m) is { } w) return (w.Sector, w.Position, Describe(u, m), MissionMarkerKind.Point);
                return null;
        }
        return u.Stations.TryGetValue(m.At, out var st) ? (st.Sector, st.Position, Describe(u, m), MissionMarkerKind.Station) : null;
    }

    public string TargetText(Universe u, MissionOffer o) => KindOf(o) switch
    {
        "nebula" => PatrolMission.WaypointsOf(AsMission(o)) is { Count: > 0 } w && u.Sectors.TryGetValue(w[0].Sector, out var s) ? $"Nebel {s.Name}" : "",
        "wreck" => int.TryParse(o.Args.GetValueOrDefault("sector"), out var sv) && u.Sectors.TryGetValue(new SectorId(sv), out var ws) ? $"Wrack in {ws.Name}" : "",
        _ => u.Stations.TryGetValue(o.At, out var st) ? st.Name : "",
    };

    public long DeadlineOf(Universe u, MissionOffer o, Ship ship)
    {
        long t;
        var m = AsMission(o);
        switch (KindOf(o))
        {
            case "nebula":
            {
                t = 0; var (fs, fp) = (ship.Sector, ship.Position);
                foreach (var (ws, wp) in PatrolMission.WaypointsOf(m)) { t += MissionRules.TripTicks(u, fs, fp, ws, wp, ship); (fs, fp) = (ws, wp); }
                break;
            }
            case "wreck" when WreckOf(u, m) is { } w && u.Stations.TryGetValue(o.At, out var st):
                t = MissionRules.TripTicks(u, ship.Sector, ship.Position, w.Sector, w.Position, ship) + MissionRules.TripTicks(u, w.Sector, w.Position, st.Sector, st.DockPoint(ship.Id), ship);
                break;
            default:
                t = u.Stations.TryGetValue(o.At, out var gs) ? MissionRules.TripTicks(u, ship.Sector, ship.Position, gs.Sector, gs.DockPoint(ship.Id), ship) : 0;
                t = Math.Max(t, u.Rules.MissionMinDeadlineTicks);   // Ware erst beschaffen
                break;
        }
        return Math.Max(u.Rules.MissionMinDeadlineTicks, (long)(t * Math.Max(2f, u.Rules.MissionDeadlineFactor)));
    }

    public string DescribeOffer(Universe u, MissionOffer o)
    {
        string fp = $"{u.Rules.MissionResearchPoints:0} FP";
        return KindOf(o) switch
        {
            "nebula" => $"Forschung: zwei Messpunkte in {TargetText(u, o)} anfliegen — Belohnung {o.Reward:0} Cr + {fp} für die Fraktion.",
            "wreck" => $"Forschung: Probe aus einem Wrack ({TargetText(u, o)}) bergen und herbringen — Belohnung {o.Reward:0} Cr + {fp}.",
            _ => $"Forschung: {o.Args.GetValueOrDefault("units")} {GoodName(u, o.Args.GetValueOrDefault("good"))} liefern — Belohnung {o.Reward:0} Cr + {fp}.",
        };
    }

    static string GoodName(Universe u, string? g) => int.TryParse(g, out var v) && u.Goods.TryGetValue(new GoodId(v), out var good) ? good.Name : "Ware";

    public string Describe(Universe u, Mission m)
    {
        string left = MissionRules.Rest(Math.Max(0L, m.Deadline - u.Tick));
        return KindOf(m) switch
        {
            "nebula" => $"Auftrag: Messpunkt {PatrolMission.CurrentOf(m) + 1}/{PatrolMission.WaypointsOf(m).Count} im Nebel · noch {left}",
            "wreck" => m.Args.GetValueOrDefault("sampled") == "1" ? $"Auftrag: Wrackprobe zur Forschungsstation · noch {left}" : $"Auftrag: Wrackprobe bergen (Bergen/Traktor) · noch {left}",
            _ => $"Auftrag: {m.Progress:0.#}/{m.Args.GetValueOrDefault("units")} {GoodName(u, m.Args.GetValueOrDefault("good"))} an die Forschung · noch {left}",
        };
    }

    public void Check(Universe u, Mission m)
    {
        if (u.Tick >= m.Deadline) { MissionRules.Fail(u, m, "Frist abgelaufen"); return; }
        if (TargetGone(u, m)) { MissionRules.Fail(u, m, "Forschungsstation weg"); return; }
        switch (KindOf(m))
        {
            case "nebula":
            {
                var wps = PatrolMission.WaypointsOf(m);
                int cur = PatrolMission.CurrentOf(m);
                if (cur < wps.Count && u.Ships.TryGetValue(u.PlayerShip, out var me) && me.Sector == wps[cur].Sector
                    && Vec2.Distance(me.Position, wps[cur].Pos) <= u.Rules.MissionResearchPointRadius)
                {
                    m.Args["wp"] = (cur + 1).ToString(Inv);
                    if (cur + 1 >= wps.Count) Finish(u, m);
                }
                break;
            }
            case "wreck":
                if (m.Args.GetValueOrDefault("sampled") != "1") { if (WreckOf(u, m) is null) MissionRules.Fail(u, m, "Wrack verfallen"); break; }
                if (m.Args.TryGetValue("ship", out var ss) && int.TryParse(ss, out var sid) && u.Ships.TryGetValue(new ShipId(sid), out var sh) && sh.DockedAt == m.At) Finish(u, m);
                break;
            default:
                if (float.TryParse(m.Args.GetValueOrDefault("units"), NumberStyles.Float, Inv, out var units) && m.Progress + 1e-3f >= units) Finish(u, m);
                break;
        }
    }

    public string? DeliverIssue(Universe u, Mission m, Ship ship, Station st) => "Forschungsaufträge werden durch Anfliegen, Bergen oder Verkauf erfüllt.";
    public void Deliver(Universe u, Mission m, Ship ship, Station st) { }
}
