using SpaceSim.Core.Events;
using SpaceSim.Core.Missions;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Ausbau Punkt 14: Flottenbefehl <b>Taxi fahren</b> — ein eigenes Schiff mit Passagierkabine (<see cref="ShipStats.Passengers"/>) fährt die echte
/// Fahrgastnachfrage: es nimmt an Taxi-/Hangarstationen im Umkreis seiner Grenzen (<see cref="AutoLimits.Center"/>/<see cref="AutoLimits.RadiusJumps"/>)
/// die vorhandenen <b>Fahrgast-Angebote</b> der Station an (<see cref="PassengerMission"/>: Missions-RNG, feste Zahl je Angebotstakt, Lohn aus dem Budget und
/// dem verfügbaren Geld des Auftraggebers reserviert, Mindeststrecke) und liefert sie durch Andocken am Ziel ab — der Lohn kommt wie bei jedem Auftrag
/// aus der Kasse des Auftraggebers, kein neues Geld. Gibt es hier kein passendes Angebot, fliegt es zum Stand mit dem besten (Lohn je geschätzter Zeit inkl.
/// Anflug), sonst wartet es. Flüchtlinge (Kontrollrisiko) nimmt die Automatik nicht. Fahrgastaufträge der Flotte zählen nicht gegen die Höchstzahl des Spielers.
/// </summary>
public sealed class TaxiExecutor : IOrderExecutor
{
    public string Kind => CommandKinds.Taxi;
    public Capability Needs => Capability.None;

    public string? Check(Universe u, Ship ship, ShipCommand command)
    {
        if (!ship.IsPlayer) return "Nur für die eigene Flotte.";
        if (!u.Rules.PassengerTaxi || !u.Rules.MissionPassengers) return "Taxi fahren ist abgeschaltet.";
        if (ship.Stats.Passengers <= 0) return "Keine Passagierkabine eingebaut.";
        if (command is not TaxiCommand t) return "Befehl unbekannt.";
        return Stands(u, t.Limits).Count >= 2 ? null : "Weniger als zwei Taxistände im Umkreis.";
    }

    /// <summary>Taxistand: fertige, andockbare Station mit Dienst „taxi“ oder „hangar“.</summary>
    public static bool IsStand(Universe u, Station st) =>
        st.Dockable && !st.IsBuildSite && u.StationTypes.TryGetValue(st.TypeKey, out var t) && (t.Services.Contains("taxi") || t.Services.Contains("hangar"));

    /// <summary>Taxistände im Umkreis (Id-Reihenfolge).</summary>
    public static List<Station> Stands(Universe u, AutoLimits l) =>
        u.StationsById().Where(st => IsStand(u, st) && AutoLimitRules.InRange(u, l, st.Sector)).ToList();

    /// <summary>Laufender Fahrgastauftrag dieses Schiffs (Flotte), null = keiner.</summary>
    public static Mission? Running(Universe u, Ship ship) =>
        u.Missions.FirstOrDefault(m => m.State == MissionState.Active && m.Type == PassengerMission.KeyId && MissionRules.IsFleet(m)
            && m.Args.GetValueOrDefault("ship") == ship.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Annehmbare Fahrgast-Angebote an <paramref name="st"/> für dieses Schiff (Ziel im Umkreis, keine Flüchtlinge, Plätze reichen).</summary>
    public static IEnumerable<MissionOffer> Offers(Universe u, Ship ship, Station st, AutoLimits l)
    {
        foreach (var o in u.MissionOffers.Where(o => o.At == st.Id && o.Type == PassengerMission.KeyId).OrderBy(o => o.Id))
        {
            if (o.Args.GetValueOrDefault("kind") == "refugee") continue;
            if (!int.TryParse(o.Args.GetValueOrDefault("target"), out var tv) || !u.Stations.TryGetValue(new StationId(tv), out var to)) continue;
            if (!AutoLimitRules.InRange(u, l, to.Sector)) continue;
            if (!int.TryParse(o.Args.GetValueOrDefault("pax"), out var pax) || pax > PassengerMission.FreeSeats(u, ship)) continue;
            yield return o;
        }
    }

    /// <summary>Bewertung eines Angebots: Lohn je geschätzter Reisezeit (von <paramref name="fromSector"/>/<paramref name="fromPos"/> über die Angebots-Station zum Ziel).</summary>
    static float Score(Universe u, Ship ship, MissionOffer o, Station at, SectorId fromSector, Vec2 fromPos)
    {
        var to = u.Stations[new StationId(int.Parse(o.Args["target"]))];
        float t = Travel.Estimate(u, fromSector, fromPos, at.Sector, at.DockPoint(ship.Id), ship) + Travel.Estimate(u, at.Sector, at.DockPoint(ship.Id), to.Sector, to.DockPoint(ship.Id), ship);
        return float.IsFinite(t) ? o.Reward / MathF.Max(1f, t + 40f) : 0f;
    }

    public void Start(Universe u, Ship ship, ShipCommand command) { ship.Order = null; ship.CommandStep = 0; ship.NextThinkTick = u.Tick; }

    public CommandStatus Step(Universe u, Ship ship, ShipCommand command, int dt)
    {
        var c = (TaxiCommand)command;
        // laufender Auftrag: zum Ziel (Andocken liefert ab — PassengerMission über ShipDocked)
        if (Running(u, ship) is { } m && int.TryParse(m.Args.GetValueOrDefault("target"), out var tv) && u.Stations.TryGetValue(new StationId(tv), out var dest))
        {
            if (c.Dest != dest.Id) ship.Command = c = c with { Dest = dest.Id, From = m.At, Pax = PassengerMission.PaxOf(m) };
            OrderMove.MoveTo(u, ship, dest.Sector, dest.DockPoint(ship.Id), dest.Id, out _);
            return CommandStatus.Running;
        }
        if (c.Dest.Value != 0)
        {
            // abgeliefert (oder gescheitert): Fahrt beendet
            ship.Command = c = c with { Dest = default, Pax = 0 };
            ship.NextThinkTick = u.Tick + 20;
        }
        if (OrderMove.StepTransit(u, ship) == OrderMove.Progress.Moving) return CommandStatus.Running;
        if (u.Tick < ship.NextThinkTick) return CommandStatus.Running;
        var stands = Stands(u, c.Limits);
        Station? here = ship.Transit is null && ship.DockedAt.Value != 0 ? stands.FirstOrDefault(st => st.Id == ship.DockedAt) : null;
        // bestes Angebot im Umkreis (hier ohne Anflug)
        (MissionOffer O, Station At, float Score)? best = null;
        foreach (var st in stands)
            foreach (var o in Offers(u, ship, st, c.Limits))
            {
                float sc = Score(u, ship, o, st, ship.Sector, ship.Position);
                if (sc > 0f && (best is null || sc > best.Value.Score + 1e-6f)) best = (o, st, sc);
            }
        if (best is not { } b) { ship.NextThinkTick = u.Tick + Math.Max(20, u.Rules.TaxiIdleTicks); return CommandStatus.Running; }
        if (here is not null && b.At.Id == here.Id)
        {
            var (mission, issue) = MissionRules.Accept(u, b.O.Id, ship, remote: false, fleet: true);
            if (mission is null) { ship.NextThinkTick = u.Tick + Math.Max(20, u.Rules.TaxiIdleTicks); return CommandStatus.Running; }
            ship.Command = c with { Dest = new StationId(int.Parse(mission.Args["target"])), From = here.Id, Pax = PassengerMission.PaxOf(mission) };
            ship.CommandStep = 0;
            return CommandStatus.Running;
        }
        if (!OrderMove.Go(u, ship, b.At.Sector, b.At.DockPoint(ship.Id), b.At.Id)) ship.NextThinkTick = u.Tick + OrderMove.RetryTicks;
        return CommandStatus.Running;
    }

    public void Stop(Universe u, Ship ship, ShipCommand command) => OrderMove.StopHere(u, ship);

    public string Describe(Universe u, Ship ship, ShipCommand command)
    {
        var c = (TaxiCommand)command;
        string lim = AutoLimitRules.Describe(u, c.Limits, trade: false);
        return c.Dest.Value != 0 ? $"Taxi: {c.Pax} Fahrgäste → {OrderMove.StationName(u, c.Dest)}{lim}" : $"Taxi fahren: sucht Fahrgäste{lim}";
    }
}
