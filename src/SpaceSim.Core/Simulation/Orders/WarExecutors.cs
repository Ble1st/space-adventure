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
        if (command is not AssaultCommand a || TargetOf(u, a) is not { } t) return "Ziel unbekannt.";
        return OrderMove.Reachable(u, ship, t.Sector) ? null : "Ziel ist nicht erreichbar.";
    }

    /// <summary>Phase 31: Ziel des Angriffsbefehls (Station oder Tor-Hälfte; null = keins mehr).</summary>
    public static ISiegeTarget? TargetOf(Universe u, AssaultCommand a)
    {
        if (a.Gate.Value != 0) return u.Gates.TryGetValue(a.Gate, out var g) ? new GateTarget(u, g) : null;
        return u.Stations.TryGetValue(a.Station, out var st) && !st.IsBuildSite ? new StationTarget(u, st) : null;
    }

    public void Start(Universe u, Ship ship, ShipCommand command) { ship.Order = null; ship.CommandStep = 0; ship.NextThinkTick = u.Tick; }

    /// <summary>Anflugpunkt um die Station (je Schiff eine eigene Richtung, Abstand wie der Belagerungsabstand).</summary>
    public static Vec2 Standoff(Universe u, Station st, Ship ship) => Standoff(u, st.Sector, st.Position, ship);

    public static Vec2 Standoff(Universe u, SectorId sector, Vec2 at, Ship ship) =>
        CombatSpace.Clear(u, sector, at + Vec2.FromAngle(ship.Id.Value * 1.7f) * (u.Rules.SiegeStartDistance * 0.8f));

    /// <summary>Gilt das Ziel noch (steht, nicht ausgeschaltet, Besitzer ist Kriegsgegner der Fraktion des Schiffs)? Runde 40: im Waffenstillstand nicht (Hostile statt AtWar).</summary>
    public static bool StillTarget(Universe u, Ship ship, Station st) =>
        u.Rules.Warfare && !st.IsBuildSite && !StationCombat.IsDown(u, st) && u.FactionOf(ship) is { } f && u.FactionOf(st) is { } g && Wars.Hostile(u, f, g);

    /// <summary>Phase 31: wie oben für jedes Ziel — ein Tor gilt, solange es steht und ein Verteidiger Kriegsgegner ist. Runde 40: Hostile (Waffenstillstand sperrt).</summary>
    public static bool StillTarget(Universe u, Ship ship, ISiegeTarget t)
    {
        if (t is StationTarget st) return StillTarget(u, ship, st.Station);
        if (!u.Rules.Warfare || t.IsDown || u.FactionOf(ship) is not { } f) return false;
        foreach (var d in t.Defenders) if (Wars.Hostile(u, f, d)) return true;
        return false;
    }

    public CommandStatus Step(Universe u, Ship ship, ShipCommand command, int dt)
    {
        var a = (AssaultCommand)command;
        if (TargetOf(u, a) is not { } t || !StillTarget(u, ship, t)) return CommandStatus.Done;
        var spot = Standoff(u, t.Sector, t.Position, ship);
        if (!(ship.Transit is null && ship.Sector == t.Sector && Vec2.Distance(ship.Position, spot) <= 5f))
        {
            var s = OrderMove.MoveTo(u, ship, t.Sector, spot, default, out bool there);
            if (!there) return s;
        }
        if (SiegeTargets.SiegeOf(u, t.Ref) is { } running) Engage.Join(u, running, ship, 0, CombatTactics.Siege, 0f, running.Spatial ? ship.Position : null);
        else Siege.Start(u, ship, t);
        return CommandStatus.Running;
    }

    public void Stop(Universe u, Ship ship, ShipCommand command) => OrderMove.StopHere(u, ship);
    public string Describe(Universe u, Ship ship, ShipCommand command) =>
        TargetOf(u, (AssaultCommand)command) is { } t ? $"Greift {t.Name} an" : $"Greift {OrderMove.StationName(u, ((AssaultCommand)command).Station)} an";
}
