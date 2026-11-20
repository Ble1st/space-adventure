using System.Globalization;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;

namespace SpaceSim.Core.Missions;

/// <summary>
/// Ausbau Punkt 14: Auftragsart <b>Fahrgast</b> an Taxi-/Hangarstationen — n Fahrgäste nach B. Sitzplätze aus Passagierkabinen
/// (<see cref="ShipStats.Passengers"/>), belegt durch laufende Fahrgastaufträge desselben Schiffs. Lohn = (Grundpreis + je Flugminute) × Fahrgäste ×
/// Art-Faktor; Arten (Missions-RNG): normal, <b>Eilfahrt</b> (kurze Frist, Zuschlag), <b>VIP</b> (doppelt, Ansehen), <b>Flüchtling</b> (nur an Stationen
/// ohne Staatsfraktion; Kontrollrisiko wie Schmuggel, Verbrechen), <b>Diplomat</b> (zu einer anderen Fraktion; Ansehen bei beiden). Im Lohn steckt ein
/// Trinkgeld (<see cref="EconomyRules.PassengerTipShare"/>), das bei jedem Hüllentreffer sinkt (Betrag zurück an den Auftraggeber). Schiff verloren =
/// gescheitert + zusätzlicher Ansehensverlust. Ablieferung durch Andocken an der Zielstation.
/// </summary>
public sealed class PassengerMission : IMissionType
{
    public const string KeyId = "passenger";
    public string Key => KeyId;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static int PaxOf(Mission m) => int.TryParse(m.Args.GetValueOrDefault("pax"), out var v) ? v : 0;
    static int PaxOf(MissionOffer o) => int.TryParse(o.Args.GetValueOrDefault("pax"), out var v) ? v : 0;
    public static string KindOf(Mission m) => m.Args.GetValueOrDefault("kind") ?? "normal";
    static string KindOf(MissionOffer o) => o.Args.GetValueOrDefault("kind") ?? "normal";

    /// <summary>Belegte Fahrgastplätze eines Schiffs (laufende Fahrgastaufträge).</summary>
    public static int Seated(Universe u, Ship ship) =>
        u.Missions.Where(m => m.State == MissionState.Active && m.Type == KeyId && m.Args.GetValueOrDefault("ship") == ship.Id.Value.ToString(Inv)).Sum(PaxOf);

    /// <summary>Freie Fahrgastplätze.</summary>
    public static int FreeSeats(Universe u, Ship ship) => Math.Max(0, ship.Stats.Passengers - Seated(u, ship));

    public static string KindName(string kind) => kind switch
    {
        "rush" => "Eilfahrt", "vip" => "VIP", "refugee" => "Flüchtling", "diplomat" => "Diplomat", _ => "Fahrgast",
    };

    public MissionOffer? Generate(Universe u, Station st, DeterministicRng rng, long bucket)
    {
        var r = u.Rules;
        if (!r.MissionPassengers || !u.StationTypes.TryGetValue(st.TypeKey, out var type)) return null;
        if (!type.Services.Contains("taxi") && !type.Services.Contains("hangar")) return null;
        if (MissionRules.Forbidden(u, st.Owner) || MissionRules.Blocked(u, st.Owner.Value)) return null;
        if (MissionRng.NextFloat(rng) >= r.MissionPassengerChance) return null;
        float roll = MissionRng.NextFloat(rng);
        bool state = u.FactionOf(st.Owner) is { } own && u.IsNpcFaction(own);
        string kind = roll < 0.6f ? "normal" : roll < 0.75f ? "rush" : roll < 0.85f ? "vip" : roll < 0.93f ? (state ? "normal" : "refugee") : (state ? "diplomat" : "normal");
        var targets = u.StationsById().Where(s => s.Id != st.Id && s.Dockable && !s.IsBuildSite && u.IsReachable(st.Sector, s.Sector)
            && (s.Sector != st.Sector || Vec2.Distance(s.Position, st.Position) >= r.PassengerMinDistance)   // keine Kurzstrecken
            && (kind != "diplomat" || (u.FactionOf(s) is { } tf && u.IsNpcFaction(tf) && tf != u.FactionOf(st.Owner)))).ToList();
        if (targets.Count == 0) return null;
        var target = targets[MissionRng.NextInt(rng, targets.Count)];
        int pax = kind switch
        {
            "vip" => 1,
            "diplomat" or "refugee" => 1 + MissionRng.NextInt(rng, 2),
            _ => MissionRng.NextFloat(rng) < 0.1f ? 4 + MissionRng.NextInt(rng, 3) : 1 + MissionRng.NextInt(rng, 3),
        };
        long ticks = MissionRules.TripTicks(u, st.Sector, st.DockPoint(default), target.Sector, target.DockPoint(default), null);
        float factor = kind switch { "rush" => r.PassengerRushFactor, "vip" => r.PassengerVipFactor, "refugee" => r.PassengerRefugeeFactor, "diplomat" => r.PassengerDiplomatFactor, _ => 1f };
        float fare = (r.PassengerBaseFare + r.PassengerFarePerMinute * ticks / (60f * SimClock.TicksPerSecond)) * pax * factor;
        float tip = MathF.Round(fare * r.PassengerTipShare);
        float reward = MathF.Round(fare) + tip;
        if (!u.Actors.TryGetValue(st.Owner, out var client)) return null;
        if (MissionRules.SpentToday(u, st.Owner.Value) + reward > MissionRules.BudgetOf(u, client) + 0.0001f) return null;
        if (FactionViability.Spendable(u, client) < reward - 0.0001f) return null;
        var o = new MissionOffer { Type = KeyId, Reward = reward };
        o.Args["target"] = target.Id.Value.ToString(Inv);
        o.Args["pax"] = pax.ToString(Inv);
        o.Args["kind"] = kind;
        o.Args["tip"] = tip.ToString("0.##", Inv);
        o.Args["channel"] = "station";
        o.Args["contract"] = "0";
        return o;
    }

    public string? AcceptIssue(Universe u, MissionOffer o, Ship ship, bool remote)
    {
        if (!u.Stations.ContainsKey(TransportMission.TargetOf(o))) return "Zielstation existiert nicht mehr.";
        int pax = PaxOf(o), free = FreeSeats(u, ship);
        if (ship.Stats.Passengers <= 0) return "Keine Passagierkabine eingebaut (Ausrüster: Passagierkabine S/M).";
        if (free < pax) return $"Zu wenig Fahrgastplätze ({free} frei, {pax} nötig).";
        return null;
    }

    public Mission Accept(Universe u, MissionOffer o, Ship ship, bool remote)
    {
        var target = u.Stations[TransportMission.TargetOf(o)];
        var m = new Mission
        {
            Type = KeyId, Client = o.Client, At = o.At,
            Args = new Dictionary<string, string>(o.Args) { ["ship"] = ship.Id.Value.ToString(Inv), ["tipLeft"] = o.Args.GetValueOrDefault("tip") ?? "0" },
        };
        m.Deadline = u.Tick + DeadlineOf(u, o, ship);
        return m;
    }

    static ShipId ShipOf(Mission m) => int.TryParse(m.Args.GetValueOrDefault("ship"), out var v) ? new ShipId(v) : default;
    static StationId Target(Mission m) => int.TryParse(m.Args.GetValueOrDefault("target"), out var v) ? new StationId(v) : default;

    public void OnEvent(Universe u, Mission m, SimEvent e)
    {
        switch (e)
        {
            case ShotFired sf when sf.Target == ShipOf(m) && sf.Hit && sf.HullDamage > 0f:
            {
                float tip = float.TryParse(m.Args.GetValueOrDefault("tip"), NumberStyles.Float, Inv, out var t0) ? t0 : 0f;
                float left = float.TryParse(m.Args.GetValueOrDefault("tipLeft"), NumberStyles.Float, Inv, out var t1) ? t1 : 0f;
                float loss = MathF.Min(left, MathF.Round(tip * u.Rules.PassengerTipLossPerHit));
                if (loss <= 0f) break;
                m.Args["tipLeft"] = (left - loss).ToString("0.##", Inv);
                m.Reserved = MathF.Max(0f, m.Reserved - loss);
                if (u.Actors.TryGetValue(m.Client, out var client))
                {
                    client.Treasury.Credits += loss;
                    u.Events.Publish(new TreasuryIncome(client.Id, "mission_return", loss));
                }
                u.Events.Publish(new PassengerTipLost(m.Id, sf.Target, loss));
                break;
            }
            case ShipDocked sd when sd.Ship == ShipOf(m) && sd.Station == Target(m)
                                   && u.Ships.TryGetValue(sd.Ship, out var ship) && u.Stations.TryGetValue(sd.Station, out var st):
                Deliver(u, m, ship, st);
                break;
        }
    }

    public bool TargetGone(Universe u, Mission m) => !u.Stations.ContainsKey(Target(m));

    public (SectorId Sector, Vec2 Pos, string Label, MissionMarkerKind Kind)? Marker(Universe u, Mission m) =>
        u.Stations.TryGetValue(Target(m), out var st) ? (st.Sector, st.Position, Describe(u, m), MissionMarkerKind.Station) : null;

    public string TargetText(Universe u, MissionOffer o) => u.Stations.TryGetValue(TransportMission.TargetOf(o), out var st) ? st.Name : "";

    public long DeadlineOf(Universe u, MissionOffer o, Ship ship)
    {
        if (!u.Stations.TryGetValue(TransportMission.TargetOf(o), out var st)) return u.Rules.MissionMinDeadlineTicks;
        if (KindOf(o) != "rush") return MissionRules.DeadlineFrom(u, ship, st) - u.Tick;
        long trip = MissionRules.TripTicks(u, ship.Sector, ship.Position, st.Sector, st.DockPoint(ship.Id), ship);
        return Math.Max(600L, (long)(trip * u.Rules.PassengerRushDeadlineFactor));   // Eilfahrt: knapp, aber schaffbar
    }

    public string DescribeOffer(Universe u, MissionOffer o)
    {
        var target = u.Stations[TransportMission.TargetOf(o)];
        var ship = u.Ships[u.PlayerShip];
        string flug = MissionRules.TripText(u, ship.Sector, ship.Position, target.Sector, target.DockPoint(ship.Id), ship);
        int pax = PaxOf(o);
        string who = KindOf(o) == "normal" ? (pax == 1 ? "1 Fahrgast" : $"{pax} Fahrgäste") : $"{KindName(KindOf(o))} ({pax} Pers.)";
        string risk = KindOf(o) == "refugee" ? " Gesucht: Kontrolle = Verbrechen." : KindOf(o) == "diplomat" ? " Ansehen bei beiden Fraktionen." : "";
        return $"Fahrgast: {who} nach {target.Name} — Lohn {o.Reward:0} Cr (Trinkgeld {o.Args.GetValueOrDefault("tip")} Cr, sinkt bei Treffern), Flugzeit {flug}.{risk}";
    }

    public string Describe(Universe u, Mission m)
    {
        if (!u.Stations.TryGetValue(Target(m), out var st)) return $"Auftrag #{m.Id}: Ziel weg";
        int pax = PaxOf(m);
        string who = KindOf(m) == "normal" ? (pax == 1 ? "1 Fahrgast" : $"{pax} Fahrgäste") : $"{KindName(KindOf(m))} ({pax})";
        return $"Auftrag: {who} → {st.Name} · noch {MissionRules.Rest(Math.Max(0L, m.Deadline - u.Tick))}";
    }

    public void Check(Universe u, Mission m)
    {
        if (u.Tick >= m.Deadline) { MissionRules.Fail(u, m, "Frist abgelaufen"); return; }
        if (TargetGone(u, m)) { MissionRules.Fail(u, m, "Zielstation weg"); return; }
        if (MissionContraband.Search(u, m)) return;
        if (u.Ships.TryGetValue(ShipOf(m), out var sh) && sh.DockedAt == Target(m) && u.Stations.TryGetValue(sh.DockedAt, out var st)) Deliver(u, m, sh, st);
    }

    public string? DeliverIssue(Universe u, Mission m, Ship ship, Station st)
    {
        if (st.Id != Target(m)) return "Falsche Station — die Fahrgäste wollen zur Zielstation.";
        if (ship.DockedAt != st.Id) return "Dafür musst du an der Zielstation andocken.";
        return null;
    }

    public void Deliver(Universe u, Mission m, Ship ship, Station st)
    {
        if (m.State != MissionState.Active) return;
        string kind = KindOf(m);
        if (kind == "vip" && u.FactionOf(m.Client) is { } vf && u.IsNpcFaction(vf)) PlayerStanding.Change(u, vf, u.Rules.PassengerVipStanding, "passenger_vip");
        if (kind == "diplomat")
        {
            if (u.FactionOf(m.Client) is { } a && u.IsNpcFaction(a)) PlayerStanding.Change(u, a, u.Rules.PassengerDiplomatStanding, "diplomat");
            if (u.FactionOf(st) is { } b && u.IsNpcFaction(b) && u.FactionOf(m.Client) != b) PlayerStanding.Change(u, b, u.Rules.PassengerDiplomatStanding, "diplomat");
        }
        if (MissionRules.IsFleet(m)) u.Events.Publish(new PassengerFarePaid(ship.Id, m.At, st.Id, PaxOf(m), m.Reserved));   // Flottentaxi: Lohn aus dem Auftrag
        MissionRules.Complete(u, m);
    }
}
