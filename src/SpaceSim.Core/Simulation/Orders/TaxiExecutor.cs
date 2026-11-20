using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Ausbau Punkt 14: Flottenbefehl <b>Taxi fahren</b> — ein eigenes Schiff mit Passagierkabine (<see cref="ShipStats.Passengers"/>) fährt automatisch
/// Fahrgäste zwischen Taxi-/Hangarstationen im Umkreis seiner Grenzen (<see cref="AutoLimits.Center"/>/<see cref="AutoLimits.RadiusJumps"/> wie die
/// Handelsautomatik): an einem Stand angedockt nimmt es so viele Fahrgäste, wie Plätze frei sind, wählt das Ziel mit dem besten Fahrgeld je geschätzter
/// Reisezeit (Gleichstand: kleinere Id) und kassiert bei Ankunft das Fahrgeld (<see cref="PassengerFarePaid"/>): Plätze × (Grundpreis + je
/// geschätzter Flugminute). Das Fahrgeld bringen die Fahrgäste mit (neues Geld, nur Spielerflotte; die NPC-Welt ist unberührt).
/// </summary>
public sealed class TaxiExecutor : IOrderExecutor
{
    /// <summary>Wartezeit an einem Stand ohne lohnendes Ziel (Ticks).</summary>
    public const int IdleTicks = 200;

    public string Kind => CommandKinds.Taxi;
    public Capability Needs => Capability.None;

    public string? Check(Universe u, Ship ship, ShipCommand command)
    {
        if (!ship.IsPlayer) return "Nur für die eigene Flotte.";
        if (!u.Rules.PassengerTaxi) return "Taxi fahren ist abgeschaltet.";
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

    /// <summary>Fahrgeld für <paramref name="pax"/> Fahrgäste von A nach B (geschätzte Reisezeit des Schiffs).</summary>
    public static float Fare(Universe u, Station from, Station to, int pax, Ship ship)
    {
        float ticks = Travel.Estimate(u, from.Sector, from.DockPoint(ship.Id), to.Sector, to.DockPoint(ship.Id), ship);
        if (!float.IsFinite(ticks)) return 0f;
        return MathF.Round(pax * (u.Rules.TaxiFareBase + u.Rules.TaxiFarePerSeatMinute * ticks / (60f * SimClock.TicksPerSecond)));
    }

    public void Start(Universe u, Ship ship, ShipCommand command) { ship.Order = null; ship.CommandStep = 0; ship.NextThinkTick = u.Tick; }

    public CommandStatus Step(Universe u, Ship ship, ShipCommand command, int dt)
    {
        var c = (TaxiCommand)command;
        if (c.Dest.Value != 0)
        {
            if (!u.Stations.TryGetValue(c.Dest, out var dest)) { ship.Command = c with { Dest = default, Pax = 0 }; return CommandStatus.Running; }
            var s = OrderMove.MoveTo(u, ship, dest.Sector, dest.DockPoint(ship.Id), dest.Id, out bool there);
            if (!there)
            {
                if (s == CommandStatus.Failed) { ship.Command = c with { Dest = default, Pax = 0 }; ship.CommandStep = 0; }
                return CommandStatus.Running;
            }
            float fare = u.Stations.TryGetValue(c.From, out var from) ? Fare(u, from, dest, c.Pax, ship) : 0f;
            if (fare > 0f)
            {
                u.WalletOf(ship).Credits += fare;
                u.Events.Publish(new PassengerFarePaid(ship.Id, c.From, dest.Id, c.Pax, fare));
            }
            ship.Command = c with { Dest = default, From = dest.Id, Pax = 0 };
            ship.NextThinkTick = u.Tick + 20;   // Fahrgäste steigen aus und ein
            return CommandStatus.Running;
        }
        if (OrderMove.StepTransit(u, ship) == OrderMove.Progress.Moving) return CommandStatus.Running;
        if (u.Tick < ship.NextThinkTick) return CommandStatus.Running;
        var stands = Stands(u, c.Limits);
        Station? here = ship.Transit is null && ship.DockedAt.Value != 0 ? stands.FirstOrDefault(st => st.Id == ship.DockedAt) : null;
        if (here is null)
        {
            // zum nächsten Stand im Umkreis (geschätzte Reisezeit, dann Id)
            Station? near = null; float best = float.MaxValue;
            foreach (var st in stands)
            {
                float t = Travel.Estimate(u, ship.Sector, ship.Position, st.Sector, st.DockPoint(ship.Id), ship);
                if (t < best - 1e-3f) { best = t; near = st; }
            }
            if (near is null) return CommandStatus.Failed;
            if (!OrderMove.Go(u, ship, near.Sector, near.DockPoint(ship.Id), near.Id)) ship.NextThinkTick = u.Tick + OrderMove.RetryTicks;
            return CommandStatus.Running;
        }
        int pax = ship.Stats.Passengers;
        if (pax <= 0) return CommandStatus.Failed;
        Station? to = null; float score = 0f;
        foreach (var st in stands)
        {
            if (st.Id == here.Id || !OrderMove.Reachable(u, ship, st.Sector) && st.Sector != ship.Sector) continue;
            float ticks = Travel.Estimate(u, here.Sector, here.DockPoint(ship.Id), st.Sector, st.DockPoint(ship.Id), ship);
            if (!float.IsFinite(ticks)) continue;
            float sc = Fare(u, here, st, pax, ship) / MathF.Max(1f, ticks + 40f);
            if (sc > score + 1e-5f) { score = sc; to = st; }
        }
        if (to is null) { ship.NextThinkTick = u.Tick + IdleTicks; return CommandStatus.Running; }
        ship.Command = c with { Dest = to.Id, From = here.Id, Pax = pax };
        ship.CommandStep = 0;
        return CommandStatus.Running;
    }

    public void Stop(Universe u, Ship ship, ShipCommand command) => OrderMove.StopHere(u, ship);

    public string Describe(Universe u, Ship ship, ShipCommand command)
    {
        var c = (TaxiCommand)command;
        string lim = AutoLimitRules.Describe(u, c.Limits, trade: false);
        return c.Dest.Value != 0 ? $"Taxi: {c.Pax} Fahrgäste → {OrderMove.StationName(u, c.Dest)}{lim}" : $"Taxi fahren: wartet auf Fahrgäste{lim}";
    }
}
