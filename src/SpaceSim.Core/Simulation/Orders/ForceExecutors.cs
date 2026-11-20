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

    /// <summary>
    /// Ausbau Punkt 3: Darf die Polizei bis in den Sektor verfolgen? Mit <see cref="EconomyRules.PursuitCrossBorder"/>: eigenes Gebiet, oder ein Sektor einer
    /// befreundeten/verbündeten Fraktion höchstens <see cref="EconomyRules.PursuitForeignJumps"/> Sprung vom eigenen Gebiet — nie Feindesgebiet (Krieg, Fehde,
    /// feindlich), nie Gesetzlose/Banditen, nie herrenlos. Ohne Regel = <see cref="Covers(Universe, FactionId, SectorId)"/>.
    /// </summary>
    public static bool CoversPursuit(Universe u, FactionId f, SectorId sector)
    {
        if (!u.Rules.PursuitCrossBorder) return Covers(u, f, sector);
        if (!u.Sectors.TryGetValue(sector, out var sec) || !u.Factions.ContainsKey(sec.Owner)) return false;
        var owner = sec.Owner;
        if (owner == f) return true;
        if (Bandits.IsEstablished(u, owner) || u.Actors.TryGetValue(new ActorId(owner.Value), out var oa) && oa.Kind == ActorKind.Outlaw) return false;
        if (Wars.AtWar(u, f, owner) || Feuds.InFeud(u, f, owner) || Relations.Effective(u, owner, f) <= u.Rules.HostileRelation) return false;
        if (!(Relations.Effective(u, f, owner) >= u.Rules.AllyRelation || Alliances.Allied(u, f, owner))) return false;
        return JumpsFromTerritory(u, f, sector, Math.Max(0, u.Rules.PursuitForeignJumps)) >= 0;
    }

    public static bool CoversPursuit(Universe u, Ship police, SectorId sector) => u.FactionOf(police) is { } f && CoversPursuit(u, f, sector);

    /// <summary>Sprünge (über Tore, auch geschlossene) vom nächsten Sektor der Fraktion bis <paramref name="sector"/>, höchstens <paramref name="max"/>; −1 = weiter.</summary>
    static int JumpsFromTerritory(Universe u, FactionId f, SectorId sector, int max)
    {
        var seen = new HashSet<SectorId> { sector };
        var ring = new List<SectorId> { sector };
        for (int d = 1; d <= max; d++)
        {
            var next = new List<SectorId>();
            foreach (var s in ring)
                foreach (var g in u.Gates.Values)
                {
                    if (g.Sector != s || !u.Gates.TryGetValue(g.Target, out var far) || !seen.Add(far.Sector)) continue;
                    if (u.Sectors.TryGetValue(far.Sector, out var fs) && fs.Owner == f) return d;
                    next.Add(far.Sector);
                }
            ring = next;
        }
        return -1;
    }
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
    /// <summary>Ausbau Punkt 1: auch die Spielerflotte verfolgt (eigener Zweig <see cref="StepPlayer"/>; NPC-Polizei unverändert).</summary>
    public bool ForPlayer => true;

    public string? Check(Universe u, Ship ship, ShipCommand command)
    {
        if (command is not PursueCommand p || !u.Ships.TryGetValue(p.Target, out var t)) return "Ziel unbekannt.";
        if (p.Target == ship.Id) return "Ein Schiff kann sich nicht selbst verfolgen.";
        if (!ship.IsPlayer) return null;
        if (t.IsPlayer) return "Eigene Schiffe verfolgt die Flotte nicht.";
        if (FleetAttack.RulesIssue(u, ship) is { } rules) return rules;
        var ts = ThreatMap.Where(u, t).Sector;
        return OrderMove.Reachable(u, ship, ts) || ThreatMap.Where(u, ship).Sector == ts ? null : "Ziel ist für dieses Schiff nicht erreichbar.";
    }

    /// <summary>
    /// Ausbau Punkt 1: Verfolgen der Spielerflotte — folgt dem Ziel über Sektorgrenzen (Reiseziel des Ziels, <see cref="FleetAttack.Close"/>) und stellt es,
    /// sobald es in Reichweite ist (<see cref="FleetAttack.EngageShip"/>: außerhalb eines Kriegs ein Verbrechen). Nach dem Gefecht geht die Verfolgung weiter.
    /// Endet, wenn das Ziel weg ist, nach der Frist (<see cref="FleetAttack.PursuitTicks"/>) oder wenn der Sektor des Ziels für das Schiff gesperrt ist.
    /// </summary>
    CommandStatus StepPlayer(Universe u, Ship ship, PursueCommand c)
    {
        CommandStatus Over(string how)
        {
            u.Events.Publish(new PursuitEnded(ship.Id, c.Target, how));
            return FleetAttack.BackToLeader(u, ship) ? CommandStatus.Running : CommandStatus.Done;
        }
        if (!u.Ships.TryGetValue(c.Target, out var t)) return Over("lost");
        if (u.Tick >= c.Until) return Over("expired");
        var ts = ThreatMap.Where(u, t).Sector;
        if (ts != ThreatMap.Where(u, ship).Sector && !OrderMove.Reachable(u, ship, ts)) return Over("locked");
        if (FleetAttack.Retreat(u, ship)) return CommandStatus.Running;
        if (FleetAttack.InReach(u, ship, t))
        {
            if (FleetAttack.EngageShip(u, ship, t) is null) OrderMove.StepTransit(u, ship);
            return CommandStatus.Running;
        }
        FleetAttack.Close(u, ship, t);
        return CommandStatus.Running;
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
        if (ship.IsPlayer) return StepPlayer(u, ship, c);   // Ausbau Punkt 1: Spielerflotte
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
        if (!Jurisdiction.CoversPursuit(u, ship, ts)) return End(u, ship, c.Target, "border");
        if (u.Rules.PursuitCrossBorder && u.FactionOf(ship) is { } pf && u.Sectors.TryGetValue(ts, out var tsec) && tsec.Owner != pf
            && ThreatMap.Where(u, ship).Sector == ts)
            u.Events.Publish(new PursuitAbroad(ship.Id, c.Target, ts));   // Diagnose: Verfolgung im fremden Sektor
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
