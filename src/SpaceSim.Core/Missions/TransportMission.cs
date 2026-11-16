using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Missions;

/// <summary>
/// Runde 41: Auftragsart <b>Transport</b> — eine Regierungs-/Handelsstation (Typ mit Dienst „taxi“ oder „hangar“) bietet an:
/// „Bringe versiegelte Fracht (belegt Laderaum, <see cref="Ship.MissionCargo"/>, keine Ware) von A nach B bis Tick T.“ Pfand =
/// Frachtwert (aus der Spielerkasse gehalten, zurück bei Ablieferung, verfällt bei Scheitern an die Station). Belohnung aus der
/// Kasse der Auftraggeber-Fraktion. Ablieferung durch Andocken an der Zielstation (Dienst „missions“, Schlüssel „deliver:&lt;id&gt;“).
/// </summary>
public sealed class TransportMission : IMissionType
{
    public const string KeyId = "transport";
    public string Key => KeyId;

    /// <summary>Zielstation des Auftrags (Args „target“).</summary>
    public static StationId TargetOf(MissionOffer o) => new(int.Parse(o.Args["target"]));
    /// <summary>Versiegelte Fracht (Volumen; Args „volume“).</summary>
    public static float VolumeOf(MissionOffer o) => float.Parse(o.Args["volume"], System.Globalization.CultureInfo.InvariantCulture);

    public MissionOffer? Generate(Universe u, Station st, DeterministicRng rng, long bucket)
    {
        var r = u.Rules;
        if (!u.StationTypes.TryGetValue(st.TypeKey, out var type)) return null;
        if (!type.Services.Contains("taxi") && !type.Services.Contains("hangar")) return null;
        if (MissionRules.Forbidden(u, st.Owner) || MissionRules.Blocked(u, st.Owner.Value)) return null;
        // Ziel: erreichbare, andockbare, fertige Station (nicht dieselbe, gern anderer Sektor)
        var targets = u.StationsById().Where(s => s.Id != st.Id && s.Dockable && !s.IsBuildSite && u.IsReachable(st.Sector, s.Sector)).ToList();
        if (targets.Count == 0) return null;
        var target = targets[MissionRng.NextInt(rng, targets.Count)];
        float volume = r.MissionTransportUnits;
        int hops = Math.Max(1, u.Hops(st.Sector, target.Sector));
        float reward = MathF.Round(r.MissionTransportBaseReward + hops * r.MissionTransportRewardPerHop);
        if (!u.Actors.TryGetValue(st.Owner, out var a)) return null;
        if (MissionRules.SpentToday(u, st.Owner.Value) + reward > MissionRules.BudgetOf(u, a) + 0.0001f) return null;
        if (FactionViability.Spendable(u, a) < reward - 0.0001f) return null;
        var o = new MissionOffer
        {
            Type = KeyId,
            Reward = reward,
            Deposit = volume * r.MissionTransportValuePerVolume,
        };
        o.Args["target"] = target.Id.Value.ToString();
        o.Args["volume"] = volume.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        o.Args["channel"] = "station";
        o.Args["contract"] = "0";
        return o;
    }

    public string? AcceptIssue(Universe u, MissionOffer o, Ship ship, bool remote)
    {
        if (!u.Stations.TryGetValue(TargetOf(o), out _)) return "Zielstation existiert nicht mehr.";
        float volume = VolumeOf(o);
        if (ship.CargoCapacity - ship.UsedVolume(u) - ship.MissionCargo < volume - 0.0001f)
            return $"Zu wenig freier Laderaum für die versiegelte Fracht ({volume:0.#} m³ nötig).";
        return null;
    }

    public Mission Accept(Universe u, MissionOffer o, Ship ship, bool remote)
    {
        float volume = VolumeOf(o);
        ship.MissionCargo += volume;   // versiegelte Fracht belegt den Laderaum (keine Ware)
        var target = u.Stations[TargetOf(o)];
        var m = new Mission
        {
            Type = KeyId, Client = o.Client, At = o.At,
            Args = new Dictionary<string, string>(o.Args) { ["ship"] = ship.Id.Value.ToString() },
            Deadline = MissionRules.DeadlineFrom(u, ship, target),
            CargoVolume = volume,
        };
        return m;
    }

    /// <summary>
    /// Runde 42b: Ablieferung bei JEDEM Andocken des Frachtschiffs an der Zielstation — über <see cref="ShipDocked"/>
    /// (Ansicht setzt <see cref="Ship.DockedAt"/> direkt, <see cref="Simulation.PlayerFleet.Dock"/>, <see cref="Universe.FinishTransit"/> am Andockpunkt).
    /// </summary>
    public void OnEvent(Universe u, Mission m, SimEvent e)
    {
        if (e is not ShipDocked sd) return;
        if (!m.Args.TryGetValue("ship", out var ss) || !int.TryParse(ss, out var sid) || sd.Ship != new ShipId(sid)) return;
        if (!m.Args.TryGetValue("target", out var ts) || !int.TryParse(ts, out var tv) || sd.Station != new StationId(tv)) return;
        if (!u.Ships.TryGetValue(sd.Ship, out var ship) || ship.DockedAt != sd.Station) return;
        if (!u.Stations.TryGetValue(sd.Station, out var st)) return;
        Deliver(u, m, ship, st);
    }

    public bool TargetGone(Universe u, Mission m) =>
        !m.Args.TryGetValue("target", out var ts) || !int.TryParse(ts, out var tv) || !u.Stations.ContainsKey(new StationId(tv));

    public (SectorId Sector, Vec2 Pos, string Label, MissionMarkerKind Kind)? Marker(Universe u, Mission m)
    {
        if (!m.Args.TryGetValue("target", out var ts) || !int.TryParse(ts, out var tv) || !u.Stations.TryGetValue(new StationId(tv), out var st)) return null;
        return (st.Sector, st.Position, Describe(u, m), MissionMarkerKind.Station);
    }

    public string TargetText(Universe u, MissionOffer o) =>
        u.Stations.TryGetValue(TargetOf(o), out var st) ? st.Name : "";

    public long DeadlineOf(Universe u, MissionOffer o, Ship ship) =>
        u.Stations.TryGetValue(TargetOf(o), out var st) ? MissionRules.DeadlineFrom(u, ship, st) - u.Tick : u.Rules.MissionMinDeadlineTicks;

    public string DescribeOffer(Universe u, MissionOffer o)
    {
        var target = u.Stations[TargetOf(o)];
        var from = u.Stations[o.At];
        // Runde 42c: geschätzte Flugzeit des Spielers in der Angebotsanzeige (real, nicht NPC-Tempo)
        var ship = u.Ships[u.PlayerShip];
        string flug = MissionRules.TripText(u, ship.Sector, ship.Position, target.Sector, target.DockPoint(ship.Id), ship);
        return $"Transport: versiegelte Fracht ({VolumeOf(o):0.#} m³) von {from.Name} nach {target.Name} — Belohnung {o.Reward:0} Cr, Pfand {o.Deposit:0} Cr, Flugzeit ~{flug}.";
    }

    public string Describe(Universe u, Mission m)
    {
        if (!m.Args.TryGetValue("target", out var ts) || !int.TryParse(ts, out var tv) || !u.Stations.TryGetValue(new StationId(tv), out var st))
            return $"Auftrag #{m.Id}: Ziel weg";
        long left = Math.Max(0L, m.Deadline - u.Tick);
        return $"Auftrag: versiegelte Fracht ({m.CargoVolume:0.#} m³) → {st.Name} · noch {MissionRules.Rest(left)} (Andocken an der Zielstation liefert ab)";
    }

    public void Check(Universe u, Mission m)
    {
        if (u.Tick >= m.Deadline) { MissionRules.Fail(u, m, "Frist abgelaufen"); return; }
        if (TargetGone(u, m)) { MissionRules.Fail(u, m, "Zielstation weg"); return; }
        // Runde 42b: liegt das Frachtschiff an der Zielstation angedockt (z. B. nach dem Laden), wird abgeliefert — jeder Andock-Pfad
        if (m.Args.TryGetValue("ship", out var ss) && int.TryParse(ss, out var sid) && u.Ships.TryGetValue(new ShipId(sid), out var sh)
            && m.Args.TryGetValue("target", out var ts) && int.TryParse(ts, out var tv) && sh.DockedAt == new StationId(tv)
            && u.Stations.TryGetValue(sh.DockedAt, out var st))
            Deliver(u, m, sh, st);
    }

    public string? DeliverIssue(Universe u, Mission m, Ship ship, Station st)
    {
        if (!m.Args.TryGetValue("target", out var ts) || !int.TryParse(ts, out var tv)) return "Ziel unbekannt.";
        if (st.Id != new StationId(tv)) return "Falsche Station — die Fracht geht an die Zielstation.";
        if (ship.DockedAt != st.Id) return "Dafür musst du an der Zielstation andocken.";
        return null;
    }

    public void Deliver(Universe u, Mission m, Ship ship, Station st)
    {
        ship.MissionCargo = MathF.Max(0f, ship.MissionCargo - m.CargoVolume);   // Fracht abgeliefert
        MissionRules.Complete(u, m);
    }
}
