using SpaceSim.Core.Combat;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Phase 28 (KAMPF.md Stufe 28): Angriff — ein Kriegsschiff fliegt zur feindlichen Station (Abstandspunkt um sie herum, Kriegsschiffe brechen durch gesperrte
/// Tore, <see cref="GateAccess.ProfileOf"/>) und belagert sie (<see cref="Siege.Start"/>, oder tritt einer laufenden Belagerung bei). Erledigt, wenn die Station
/// gefallen, kein Kriegsgegner mehr oder weg ist; das Gefecht selbst übernimmt die Ebene darüber. Vergibt die Fraktion (<see cref="NavySystem"/>), nicht der Spieler.
/// </summary>
public sealed class AssaultExecutor : IOrderExecutor
{
    public string Kind => CommandKinds.Assault;
    public Capability Needs => Capability.Combat;
    public bool ForPlayer => false;

    public string? Check(Universe u, Ship ship, ShipCommand command)
    {
        if (command is not AssaultCommand a || !u.Stations.TryGetValue(a.Station, out var st) || st.IsBuildSite) return "Ziel unbekannt.";
        return OrderMove.Reachable(u, ship, st.Sector) ? null : "Ziel ist nicht erreichbar.";
    }

    public void Start(Universe u, Ship ship, ShipCommand command) { ship.Order = null; ship.CommandStep = 0; ship.NextThinkTick = u.Tick; }

    /// <summary>Anflugpunkt um die Station (je Schiff eine eigene Richtung, Abstand wie der Belagerungsabstand).</summary>
    public static Vec2 Standoff(Universe u, Station st, Ship ship) =>
        CombatSpace.Clear(u, st.Sector, st.Position + Vec2.FromAngle(ship.Id.Value * 1.7f) * (u.Rules.SiegeStartDistance * 0.8f));

    /// <summary>Gilt das Ziel noch (steht, nicht ausgeschaltet, Besitzer ist Kriegsgegner der Fraktion des Schiffs)?</summary>
    public static bool StillTarget(Universe u, Ship ship, Station st) =>
        u.Rules.Warfare && !st.IsBuildSite && !StationCombat.IsDown(u, st) && u.FactionOf(ship) is { } f && u.FactionOf(st) is { } g && Wars.AtWar(u, f, g);

    public CommandStatus Step(Universe u, Ship ship, ShipCommand command, int dt)
    {
        var a = (AssaultCommand)command;
        if (!u.Stations.TryGetValue(a.Station, out var st) || !StillTarget(u, ship, st)) return CommandStatus.Done;
        var spot = Standoff(u, st, ship);
        if (!(ship.Transit is null && ship.Sector == st.Sector && Vec2.Distance(ship.Position, spot) <= 5f))
        {
            var s = OrderMove.MoveTo(u, ship, st.Sector, spot, default, out bool there);
            if (!there) return s;
        }
        if (Siege.Of(u, st.Id) is { } running) Engage.Join(u, running, ship, 0, CombatTactics.Siege, 0f, running.Spatial ? ship.Position : null);
        else Siege.Start(u, ship, st);
        return CommandStatus.Running;
    }

    public void Stop(Universe u, Ship ship, ShipCommand command) => OrderMove.StopHere(u, ship);
    public string Describe(Universe u, Ship ship, ShipCommand command) => $"Greift {OrderMove.StationName(u, ((AssaultCommand)command).Station)} an";
}
