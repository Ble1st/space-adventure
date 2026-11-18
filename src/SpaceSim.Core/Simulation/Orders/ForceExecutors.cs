using SpaceSim.Core.Combat;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Phase 27 (FLOTTEN.md „Zuständigkeit“): Polizei handelt nur im eigenen Hoheitsgebiet; in verbündeten Sektoren (Beziehung ≥ <see cref="EconomyRules.AllyRelation"/>)
/// leistet sie Rechtshilfe und verfolgt bis zur Grenze. Rein.
/// </summary>
public static class Jurisdiction
{
    /// <summary>Darf die Polizei der Fraktion im Sektor handeln (verfolgen, begleiten)?</summary>
    public static bool Covers(Universe u, FactionId faction, SectorId sector)
    {
        if (!u.Sectors.TryGetValue(sector, out var sec) || !u.Factions.ContainsKey(sec.Owner)) return false;
        if (sec.Owner == faction) return true;
        return Relations.Effective(u, faction, sec.Owner) >= u.Rules.AllyRelation || Alliances.Allied(u, faction, sec.Owner);   // Runde 45: Bündnis
    }

    /// <summary>Zuständigkeit für ein Schiff (Fraktion seines Besitzers); ohne Fraktion nirgends.</summary>
    public static bool Covers(Universe u, Ship police, SectorId sector) => u.FactionOf(police) is { } f && Covers(u, f, sector);
}

/// <summary>
/// Phase 27: Versorgung der Dienst-Einheiten (Polizei, Piraten), die aus der Kasse ihres Besitzers leben: An der eigenen Heimatbasis repariert, lädt nach und
/// (mit Treibstoff) tankt die Kasse (<see cref="PirateSystem.ServiceAtBase"/>) — die gemeinsame Regel für die Ausführer „Heimkehr“ und die Notfall-Ebene, sobald
/// Polizei und Piraten über Befehle laufen.
/// </summary>
public static class UnitSupport
{
    /// <summary>Lebt das Schiff aus der Kasse seines Besitzers (Polizei, Kaperschiff)?</summary>
    public static bool IsServiceUnit(Ship ship) => ThreatMap.IsPolice(ship) || ThreatMap.IsRaider(ship) || Wars.IsWarship(ship);   // Phase 28: Kriegsschiffe

    /// <summary>Versorgt eine Dienst-Einheit an ihrer Heimatbasis; false = keine Dienst-Einheit oder nicht an ihrer Basis (dann gilt die Regel der Spielerflotte).</summary>
    public static bool Serve(Universe u, Ship ship, Station st)
    {
        if (!u.Rules.FleetCombat || !IsServiceUnit(ship) || st.Id != ship.HomeBase) return false;
        PirateSystem.ServiceAtBase(u, ship, ThreatMap.IsPolice(ship) ? "police" : Wars.IsWarship(ship) ? "navy" : "pirates");
        if (ThreatMap.IsRaider(ship))
        {
            ship.DutySector = default;
            ship.NextThinkTick = u.Tick + u.Rules.PirateRestTicks;   // Rast im Versteck (wie bisher)
        }
        return true;
    }
}

/// <summary>
/// Phase 27: Lauern — das Kaperschiff (Anführer eines Rudels) fliegt zu seinem Posten im Revier und wartet dort bis zur Frist auf Beute. Während des Lauerns
/// würfelt es Überfälle (<see cref="PirateSystem.TryRaid"/>) und kann von Streifen gestellt werden (<see cref="PirateSystem.TryIntercept"/>); eine Vorwarnung an
/// den Spieler hält es an. Die Frist beginnt bei der Ankunft (<see cref="EconomyRules.PirateLurkTicks"/>). Danach ist der Befehl erledigt; die Piratengruppe
/// schickt das Schiff heim.
/// </summary>
public sealed class LurkExecutor : IOrderExecutor
{
    public string Kind => CommandKinds.Lurk;
    public Capability Needs => Capability.Combat;
    public bool ForPlayer => false;

    public string? Check(Universe u, Ship ship, ShipCommand command)
    {
        if (command is not LurkCommand l || !u.Sectors.ContainsKey(l.Sector)) return "Revier unbekannt.";
        return OrderMove.Reachable(u, ship, l.Sector) ? null : "Revier ist nicht erreichbar.";
    }

    public void Start(Universe u, Ship ship, ShipCommand command)
    {
        ship.Order = null; ship.CommandStep = 0; ship.NextThinkTick = u.Tick;
        ship.DutySector = ((LurkCommand)command).Sector;
    }

    public CommandStatus Step(Universe u, Ship ship, ShipCommand command, int dt)
    {
        var l = (LurkCommand)command;
        if (!u.Sectors.ContainsKey(l.Sector)) return CommandStatus.Failed;
        if (!(ship.Transit is null && ship.Sector == l.Sector && Vec2.Distance(ship.Position, l.Spot) <= 3f))
        {
            var s = OrderMove.MoveTo(u, ship, l.Sector, l.Spot, default, out bool there);
            if (!there) return s;
        }
        // am Posten: Lauerzeit beginnt bei der Ankunft
        if (l.Until <= 0) { l = l with { Until = u.Tick + u.Rules.PirateLurkTicks }; ship.Command = l; }
        if (ship.Order is not PatrolOrder) ship.Order = new PatrolOrder(l.Sector);
        if (u.Tick >= l.Until) return CommandStatus.Done;
        if (u.PendingEncounters.Any(e => e.Source == ship.Id)) return CommandStatus.Running;   // wartet auf den gewarnten Spieler
        if (PirateSystem.TryIntercept(u, ship, dt) || !u.Ships.ContainsKey(ship.Id) || u.Fights.IsEngaged(ship.Id)) return CommandStatus.Running;
        PirateSystem.TryRaid(u, ship, dt);
        return CommandStatus.Running;
    }

    public void Stop(Universe u, Ship ship, ShipCommand command) => OrderMove.StopHere(u, ship);
    public string Describe(Universe u, Ship ship, ShipCommand command) => $"Lauert in {OrderMove.SectorName(u, ((LurkCommand)command).Sector)}";
}

/// <summary>
/// Phase 27: Verfolgen — ein Polizeischiff (Anführer einer Streife) folgt einem geflohenen Kaperschiff, solange es im Zuständigkeitsgebiet der Fraktion ist
/// (<see cref="Jurisdiction"/>; über ein Tor auch in eigenes Gebiet) und die Frist läuft. Ohne Physik außerhalb des Spielersektors ist der Abstand ein Vorsprung
/// (<see cref="PursueCommand.Lead"/>): im selben Sektor schrumpft er um (eigenes Tempo − Tempo des Ziels) bzw. um das eigene Tempo, wenn das Ziel liegt — ein
/// schnelleres Ziel entkommt also. Bei 0 stellt die Streife das Ziel (Abfanggefecht); steckt das Ziel schon in einem Gefecht, tritt sie dort als Verstärkung bei.
/// Endet an der Grenze der Zuständigkeit, im Versteck des Ziels, mit der Frist oder wenn das Ziel weg ist.
/// </summary>
public sealed class PursueExecutor : IOrderExecutor
{
    /// <summary>Ticks zwischen zwei Neuplanungen der Reise zum Ziel.</summary>
    public const int ReplanTicks = 10;

    public string Kind => CommandKinds.Pursue;
    public Capability Needs => Capability.Combat;
    public bool ForPlayer => false;

    public string? Check(Universe u, Ship ship, ShipCommand command)
    {
        if (command is not PursueCommand p || !u.Ships.ContainsKey(p.Target)) return "Ziel unbekannt.";
        return p.Target == ship.Id ? "Ein Schiff kann sich nicht selbst verfolgen." : null;
    }

    public void Start(Universe u, Ship ship, ShipCommand command) { ship.Order = null; ship.CommandStep = 0; ship.NextThinkTick = u.Tick; }

    static CommandStatus End(Universe u, Ship ship, ShipId target, string how)
    {
        u.Events.Publish(new PursuitEnded(ship.Id, target, how));
        return CommandStatus.Done;
    }

    public CommandStatus Step(Universe u, Ship ship, ShipCommand command, int dt)
    {
        var c = (PursueCommand)command;
        if (!u.Ships.TryGetValue(c.Target, out var t)) return End(u, ship, c.Target, "lost");
        if (u.Tick >= c.Until) return End(u, ship, c.Target, "expired");
        // Ziel steckt in einem Gefecht: als Verstärkung gegen das Ziel beitreten
        if (u.Fights.Of(t.Id) is { } fight && fight.Of(t.Id) is { Active: true } tp)
        {
            var (fs, _) = ThreatMap.Where(u, ship);
            if (fs == fight.Sector && !u.Fights.IsEngaged(ship.Id))
            {
                Reinforcements.JoinSide(u, fight, ship, 1 - tp.Side, "pursuit", tp);
                return End(u, ship, c.Target, "caught");
            }
        }
        var (ts, tpos) = ThreatMap.Where(u, t);
        if (!Jurisdiction.Covers(u, ship, ts)) return End(u, ship, c.Target, "border");
        if (t.Transit is null && PirateSystem.AtHome(u, t)) return End(u, ship, c.Target, "safe");
        var (ms, mpos) = ThreatMap.Where(u, ship);
        // Runde 12: das Versteck hat Geschütze — die Streife verfolgt nicht in ihre Reichweite (Ziel oder Streife nahe am Versteck: abbrechen)
        if (u.Rules.PursuitStopsAtHideoutGuns && PirateSystem.Home(u, t) is { } home && StationCombat.GunRange(u, home) is > 0f and var reach)
        {
            float guard = reach + u.Rules.HideoutGunMargin;
            if ((ts == home.Sector && Vec2.Distance(tpos, home.Position) <= guard) || (ms == home.Sector && Vec2.Distance(mpos, home.Position) <= guard))
                return End(u, ship, c.Target, "safe");
        }
        if (ms == ts && !u.Fights.IsEngaged(t.Id))
        {
            float seconds = ShipSystems.Seconds(dt);
            float vp = Travel.SpeedOf(ship), vt = t.Transit is null ? 0f : Travel.SpeedOf(t);
            float lead = c.Lead - (vp - vt) * seconds;
            if (lead <= 0f)
            {
                OrderMove.StopHere(u, ship);
                Engage.StartIntercept(u, ship, t);
                return End(u, ship, c.Target, "caught");
            }
            c = c with { Lead = lead };
            ship.Command = c;
        }
        // Bewegung: zum Reiseziel des Ziels (bzw. seinem Liegeplatz); neu planen, wenn sich das Ziel geändert hat
        OrderMove.StepTransit(u, ship);
        if (u.Tick < ship.NextThinkTick) return CommandStatus.Running;
        ship.NextThinkTick = u.Tick + ReplanTicks;
        var (ds, dp) = t.Transit is { } tt ? (tt.ToSector, tt.ToPosition) : (t.Sector, t.Position);
        if (ship.Transit is { } mine && mine.ToSector == ds && Vec2.Distance(mine.ToPosition, dp) <= 5f) return CommandStatus.Running;
        if (ship.Transit is null && ship.Sector == ds && Vec2.Distance(ship.Position, dp) <= 5f) return CommandStatus.Running;
        if (ship.Transit is not null) OrderMove.StopHere(u, ship);
        if (!OrderMove.Go(u, ship, ds, dp)) ship.NextThinkTick = u.Tick + OrderMove.RetryTicks;
        return CommandStatus.Running;
    }

    public void Stop(Universe u, Ship ship, ShipCommand command) => OrderMove.StopHere(u, ship);
    public string Describe(Universe u, Ship ship, ShipCommand command) => $"Verfolgt {OrderMove.ShipName(u, ((PursueCommand)command).Target)}";
}
