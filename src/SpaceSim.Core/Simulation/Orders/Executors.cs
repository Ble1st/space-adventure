using SpaceSim.Core.Model;
using SpaceSim.Core.Services;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Gemeinsame Bewegungsregeln der Befehls-Ausführer (Phase 12): Reise mit Treibstoffsicherung (<see cref="Refuel.PrepareTrip"/>), Tempo des Verbands
/// (<see cref="Squadrons.Speed"/>), Ankunft (ein Tankstopp tankt), Anhalten an Ort und Stelle.
/// </summary>
public static class OrderMove
{
    public enum Progress { Idle, Moving, Arrived }

    /// <summary>Wartezeit, bevor ein Ausführer nach einem gescheiterten Reiseantritt erneut plant (Ticks).</summary>
    public const int RetryTicks = 100;
    /// <summary>Gescheiterte Reiseantritte in Folge, nach denen ein Bewegungsbefehl aufgibt.</summary>
    public const int MaxRetries = 5;

    /// <summary>Reise abwickeln: angekommen → am Zielort parken (Tankstopp: volltanken, Auftrag frei).</summary>
    public static Progress StepTransit(Universe u, Ship ship)
    {
        if (ship.Transit is not { } t) return Progress.Idle;
        if (u.Tick < t.ArrivalTick) return Progress.Moving;
        u.FinishTransit(ship);
        if (ship.Order is GoToOrder g && ship.DockedAt == g.Target && u.Stations.TryGetValue(g.Target, out var st))
            Refuel.TankUp(u, ship, st);
        ship.Order = null;
        return Progress.Arrived;
    }

    /// <summary>
    /// Reise zu einem Ziel antreten (mit Treibstoffsicherung, im Verband mit dessen Tempo). <paramref name="station"/> ≠ default: Ziel ist deren
    /// Andockpunkt. false = keine Reise (pleite oder nicht erreichbar).
    /// </summary>
    public static bool Go(Universe u, Ship ship, SectorId sector, Vec2 pos, StationId station = default)
    {
        var plan = Refuel.PrepareTrip(u, ship, sector, pos);
        if (plan == Refuel.TripPlan.FuelStop) return true;   // erst zur Tankstelle, danach plant der Ausführer neu
        if (plan == Refuel.TripPlan.Stuck) return false;
        ship.Order = station.Value != 0 ? new GoToOrder(station) : null;
        return Travel.Start(u, ship, sector, pos, Squadrons.Speed(u, ship));
    }

    /// <summary>Hält ein reisendes Schiff an seinem aktuellen Ort an (keine Ankunftslogik, Fracht bleibt); Arbeitsauftrag frei.</summary>
    public static void StopHere(Universe u, Ship ship)
    {
        if (ship.Transit is { } t)
        {
            var (sector, pos) = AccessEnforcement.Where(t, u.Tick);
            u.StartTransit(ship, new Transit { StartTick = u.Tick, ArrivalTick = u.Tick, ToSector = sector, ToPosition = pos, Legs = { new Leg(sector, pos, pos, u.Tick, u.Tick + 1) } });
            u.FinishTransit(ship);
        }
        ship.Order = null;
        ship.Phase = TradePhase.Idle;
        ship.MinePhase = MinePhase.Idle;
    }

    /// <summary>Gibt die Arbeits-Rolle eines Spielerschiffs zurück (Etikett „Player“), Arbeitsauftrag frei, Reise endet an Ort und Stelle.</summary>
    public static void Release(Universe u, Ship ship)
    {
        StopHere(u, ship);
        if (ship.IsPlayer) ship.Role = ShipRole.Player;
        ship.IdleSince = -1;
    }

    /// <summary>Liegt das Schiff an dieser Station (angedockt, nicht unterwegs)?</summary>
    public static bool AtStation(Ship ship, Station st) => ship.Transit is null && ship.DockedAt == st.Id;

    /// <summary>Ist das Schiff nahe einem Punkt (gleicher Sektor, Abstand höchstens <paramref name="range"/>)?</summary>
    public static bool Near(Ship ship, SectorId sector, Vec2 pos, float range) =>
        ship.Transit is null && ship.Sector == sector && Vec2.Distance(ship.Position, pos) <= range;

    /// <summary>Gemeinsamer Takt der Ziel-Bewegung: Ziel erreicht → true; sonst Reise (nach Wartezeit) antreten, Fehlversuche zählen.</summary>
    public static CommandStatus MoveTo(Universe u, Ship ship, SectorId sector, Vec2 pos, StationId station, out bool there)
    {
        there = false;
        var p = StepTransit(u, ship);
        if (p == Progress.Moving) return CommandStatus.Running;
        if (station.Value != 0 ? ship.Transit is null && ship.DockedAt == station : Near(ship, sector, pos, 2f)) { there = true; return CommandStatus.Running; }
        if (u.Tick < ship.NextThinkTick) return CommandStatus.Running;
        if (Go(u, ship, sector, pos, station)) { ship.CommandStep = 0; return CommandStatus.Running; }
        ship.NextThinkTick = u.Tick + RetryTicks;
        return ++ship.CommandStep > MaxRetries ? CommandStatus.Failed : CommandStatus.Running;
    }

    public static string StationName(Universe u, StationId id) => u.Stations.TryGetValue(id, out var s) ? s.Name : "?";
    public static string SectorName(Universe u, SectorId id) => u.Sectors.TryGetValue(id, out var s) ? s.Name : "?";
    public static string ShipName(Universe u, ShipId id) =>
        u.Ships.TryGetValue(id, out var s) ? $"#{s.Id.Value}" + (u.ShipModels.TryGetValue(s.ModelKey, out var m) ? $" {m.Name}" : "") : "?";

    /// <summary>Erreichbar für das Schiff (Mobilitätsprofil)?</summary>
    public static bool Reachable(Universe u, Ship ship, SectorId to) => u.IsReachable(ship.Sector, to, GateAccess.ProfileOf(u, ship));
}

/// <summary>Fliegen: zu einer Station (dort andocken) oder einem Punkt; danach endet der Befehl (das Schiff hält dort).</summary>
public sealed class GoToExecutor : IOrderExecutor
{
    public string Kind => CommandKinds.GoTo;
    public Capability Needs => Capability.None;

    static (SectorId Sector, Vec2 Pos, StationId Station)? Target(Universe u, Ship ship, GoToCommand g)
    {
        if (g.Station.Value != 0) return u.Stations.TryGetValue(g.Station, out var st) ? (st.Sector, st.DockPoint(ship.Id), st.Id) : null;
        return u.Sectors.ContainsKey(g.Sector) ? (g.Sector, g.Position, default) : null;
    }

    public string? Check(Universe u, Ship ship, ShipCommand command)
    {
        if (command is not GoToCommand g || Target(u, ship, g) is not { } t) return "Ziel unbekannt.";
        return OrderMove.Reachable(u, ship, t.Sector) ? null : "Ziel ist für dieses Schiff nicht erreichbar.";
    }

    public void Start(Universe u, Ship ship, ShipCommand command) { ship.Order = null; ship.CommandStep = 0; ship.NextThinkTick = u.Tick; }

    public CommandStatus Step(Universe u, Ship ship, ShipCommand command, int dt)
    {
        if (Target(u, ship, (GoToCommand)command) is not { } t) return CommandStatus.Failed;
        var s = OrderMove.MoveTo(u, ship, t.Sector, t.Pos, t.Station, out bool there);
        return there ? CommandStatus.Done : s;
    }

    public void Stop(Universe u, Ship ship, ShipCommand command) => OrderMove.StopHere(u, ship);

    public string Describe(Universe u, Ship ship, ShipCommand command) => command is GoToCommand g && g.Station.Value != 0
        ? $"Flug nach {OrderMove.StationName(u, g.Station)}" : $"Flug in {OrderMove.SectorName(u, ((GoToCommand)command).Sector)}";
}

/// <summary>Position halten: eine laufende Reise endet an Ort und Stelle; der Befehl gilt, bis er ersetzt wird.</summary>
public sealed class HoldExecutor : IOrderExecutor
{
    public string Kind => CommandKinds.Hold;
    public Capability Needs => Capability.None;
    public string? Check(Universe u, Ship ship, ShipCommand command) => null;
    public void Start(Universe u, Ship ship, ShipCommand command) => OrderMove.StopHere(u, ship);
    public CommandStatus Step(Universe u, Ship ship, ShipCommand command, int dt) { OrderMove.StepTransit(u, ship); return CommandStatus.Running; }
    public void Stop(Universe u, Ship ship, ShipCommand command) { }
    public string Describe(Universe u, Ship ship, ShipCommand command) => "Position halten";
}

/// <summary>
/// Arbeitsbefehle (Handel, Schürfen, Bauen): Der Befehl setzt nur die Aufgabe (Etikett <see cref="Ship.Role"/>); ausgeführt wird er von den
/// bestehenden Aufgaben-Systemen (Automatik-Ebene), die die Grenzen des Befehls beachten (<see cref="AutoLimitRules"/>).
/// </summary>
public abstract class WorkExecutor : IOrderExecutor
{
    public abstract string Kind { get; }
    public abstract Capability Needs { get; }
    protected abstract ShipRole Role { get; }
    public virtual string? Check(Universe u, Ship ship, ShipCommand command) => null;

    public virtual void Start(Universe u, Ship ship, ShipCommand command)
    {
        ship.Role = Role;
        ship.Order = null;
        ship.Phase = TradePhase.Idle;
        ship.MinePhase = MinePhase.Idle;
        ship.IdleSince = -1;
        ship.NextThinkTick = u.Tick;
        ship.JobSince = u.Tick;
    }

    public virtual CommandStatus Step(Universe u, Ship ship, ShipCommand command, int dt)
    {
        if (!ship.Has(Needs)) return CommandStatus.Failed;   // Werkzeug/Frachtraum ausgebaut
        if (ship.Role != Role) ship.Role = Role;
        return CommandStatus.Running;
    }

    public virtual void Stop(Universe u, Ship ship, ShipCommand command) => OrderMove.Release(u, ship);
    public abstract string Describe(Universe u, Ship ship, ShipCommand command);
}

public sealed class TradeExecutor : WorkExecutor
{
    public override string Kind => CommandKinds.Trade;
    public override Capability Needs => Capability.Cargo;
    protected override ShipRole Role => ShipRole.Trader;
    public override string Describe(Universe u, Ship ship, ShipCommand command) =>
        "Handel automatisch" + AutoLimitRules.Describe(u, ((TradeCommand)command).Limits, trade: true);
}

public sealed class MineExecutor : WorkExecutor
{
    public override string Kind => CommandKinds.Mine;
    public override Capability Needs => Capability.Mining;
    protected override ShipRole Role => ShipRole.Miner;
    public override string Describe(Universe u, Ship ship, ShipCommand command) =>
        "Schürfen und verkaufen" + AutoLimitRules.Describe(u, ((MineCommand)command).Limits, trade: false);
}

/// <summary>Bauen: übernimmt eine Baustelle (die genannte, sonst die nächste freie) als Bauschiff; Material zahlt die Treuhand der Baustelle, die Baugebühr geht an die Kasse des Besitzers.</summary>
public sealed class BuildExecutor : WorkExecutor
{
    public override string Kind => CommandKinds.Build;
    public override Capability Needs => Capability.Construction;
    protected override ShipRole Role => ShipRole.Builder;

    public override string? Check(Universe u, Ship ship, ShipCommand command)
    {
        if (command is not BuildCommand b || b.Site.Value == 0) return null;
        if (!u.Stations.TryGetValue(b.Site, out var st) || st.Site is not { Abandoned: false } bs) return "Keine aktive Baustelle.";
        if (bs.Builder.Value != 0 && bs.Builder != ship.Id) return "Die Baustelle hat schon ein Bauschiff.";
        return OrderMove.Reachable(u, ship, st.Sector) ? null : "Baustelle ist nicht erreichbar.";
    }

    public override void Start(Universe u, Ship ship, ShipCommand command)
    {
        base.Start(u, ship, command);
        if (command is BuildCommand b && u.Stations.TryGetValue(b.Site, out var st) && st.Site is { Abandoned: false } bs && bs.Builder.Value == 0)
            bs.Builder = ship.Id;
    }

    public override CommandStatus Step(Universe u, Ship ship, ShipCommand command, int dt)
    {
        var b = (BuildCommand)command;
        if (b.Site.Value != 0 && (!u.Stations.TryGetValue(b.Site, out var st) || st.Site is null)) return CommandStatus.Done;   // fertig oder weg
        return base.Step(u, ship, command, dt);
    }

    public override void Stop(Universe u, Ship ship, ShipCommand command)
    {
        foreach (var st in u.StationsById())
            if (st.Site is { } bs && bs.Builder == ship.Id) bs.Builder = default;
        base.Stop(u, ship, command);
    }

    public override string Describe(Universe u, Ship ship, ShipCommand command) => command is BuildCommand b && b.Site.Value != 0
        ? $"Bauen: {OrderMove.StationName(u, b.Site)}" : "Bauen: nächste freie Baustelle";
}

/// <summary>Streife über eine Runde von Sektoren: Streifenpunkte (gleiche Regel wie die Polizei, <see cref="SecuritySystem.PatrolPoint"/>), Verweilzeit.</summary>
public sealed class PatrolExecutor : IOrderExecutor
{
    public string Kind => CommandKinds.Patrol;
    public Capability Needs => Capability.Combat;

    public string? Check(Universe u, Ship ship, ShipCommand command)
    {
        if (command is not PatrolCommand p || p.Sectors.Count == 0 || p.Sectors.Any(s => !u.Sectors.ContainsKey(s))) return "Keine Streifensektoren.";
        return p.Sectors.All(s => OrderMove.Reachable(u, ship, s)) ? null : "Ein Streifensektor ist nicht erreichbar.";
    }

    public void Start(Universe u, Ship ship, ShipCommand command) { ship.Order = null; ship.CommandStep = 0; ship.NextThinkTick = u.Tick; }

    public CommandStatus Step(Universe u, Ship ship, ShipCommand command, int dt)
    {
        var p = (PatrolCommand)command;
        var progress = OrderMove.StepTransit(u, ship);
        if (progress == OrderMove.Progress.Moving) return CommandStatus.Running;
        if (progress == OrderMove.Progress.Arrived) { ship.NextThinkTick = u.Tick + u.Rules.PatrolDwellTicks; return CommandStatus.Running; }
        if (u.Tick < ship.NextThinkTick) return CommandStatus.Running;
        var sector = u.Sectors[p.Sectors[Math.Abs(ship.CommandStep) % p.Sectors.Count]];
        ship.CommandStep++;
        var spot = SecuritySystem.PatrolPoint(u, sector);
        if (!OrderMove.Go(u, ship, sector.Id, spot)) ship.NextThinkTick = u.Tick + OrderMove.RetryTicks;
        else if (ship.Order is null) ship.Order = new PatrolOrder(sector.Id);
        return CommandStatus.Running;
    }

    public void Stop(Universe u, Ship ship, ShipCommand command) => OrderMove.StopHere(u, ship);

    public string Describe(Universe u, Ship ship, ShipCommand command) =>
        "Patrouille: " + string.Join(" → ", ((PatrolCommand)command).Sectors.Select(s => OrderMove.SectorName(u, s)));

    /// <summary>Streift das Schiff (Befehl) gerade in diesem Sektor?</summary>
    public static bool Covers(Ship ship, SectorId sector) => ship.Command is PatrolCommand p && p.Sectors.Contains(sector);
}

/// <summary>Wache: fliegt zur Station und bleibt dort; verteidigt Schiffe im Sektor (<see cref="PirateSystem"/>).</summary>
public sealed class GuardExecutor : IOrderExecutor
{
    public string Kind => CommandKinds.Guard;
    public Capability Needs => Capability.Combat;

    public string? Check(Universe u, Ship ship, ShipCommand command)
    {
        if (command is not GuardCommand g || !u.Stations.TryGetValue(g.Station, out var st)) return "Station unbekannt.";
        return OrderMove.Reachable(u, ship, st.Sector) ? null : "Station ist nicht erreichbar.";
    }

    public void Start(Universe u, Ship ship, ShipCommand command) { ship.Order = null; ship.CommandStep = 0; ship.NextThinkTick = u.Tick; }

    public CommandStatus Step(Universe u, Ship ship, ShipCommand command, int dt)
    {
        if (!u.Stations.TryGetValue(((GuardCommand)command).Station, out var st)) return CommandStatus.Failed;
        return OrderMove.MoveTo(u, ship, st.Sector, st.DockPoint(ship.Id), st.Id, out _);
    }

    public void Stop(Universe u, Ship ship, ShipCommand command) => OrderMove.StopHere(u, ship);
    public string Describe(Universe u, Ship ship, ShipCommand command) => $"Wache an {OrderMove.StationName(u, ((GuardCommand)command).Station)}";
}

/// <summary>Folgen: fliegt dem Ziel hinterher (zu dessen Reiseziel bzw. Liegeplatz) und hält sich in seiner Nähe.</summary>
public class FollowExecutor : IOrderExecutor
{
    /// <summary>Abstand, ab dem ein Folger als „beim Ziel“ gilt.</summary>
    public const float KeepRange = 12f;
    /// <summary>Ticks zwischen zwei Prüfungen, wo das Ziel ist (spart Rechenzeit).</summary>
    public const int CheckTicks = 10;

    public virtual string Kind => CommandKinds.Follow;
    public virtual Capability Needs => Capability.None;

    protected static ShipId TargetOf(ShipCommand c) => c switch { FollowCommand f => f.Target, EscortCommand e => e.Target, _ => default };

    public string? Check(Universe u, Ship ship, ShipCommand command)
    {
        var id = TargetOf(command);
        if (id == ship.Id) return "Ein Schiff kann sich nicht selbst folgen.";
        if (!u.Ships.TryGetValue(id, out var target)) return "Zielschiff unbekannt.";
        return OrderMove.Reachable(u, ship, target.Sector) ? null : "Zielschiff ist nicht erreichbar.";
    }

    public void Start(Universe u, Ship ship, ShipCommand command) { ship.Order = null; ship.CommandStep = 0; ship.NextThinkTick = u.Tick; }

    public CommandStatus Step(Universe u, Ship ship, ShipCommand command, int dt)
    {
        if (!u.Ships.TryGetValue(TargetOf(command), out var target)) return CommandStatus.Failed;
        var progress = OrderMove.StepTransit(u, ship);
        if (progress == OrderMove.Progress.Moving) return CommandStatus.Running;
        // Runde 36: ein Spielerschiff prüft bei der Ankunft sofort (sonst lag es nach einer kurzen Folgereise bis zu CheckTicks mit Liegeplatz neben einer
        // fremden Station — seit Runde 36 zahlt jeder Liegeplatz Gebühr); NPC-Takt unverändert
        if (u.Tick < ship.NextThinkTick && !(progress == OrderMove.Progress.Arrived && ship.IsPlayer)) return CommandStatus.Running;
        ship.NextThinkTick = u.Tick + CheckTicks;
        // Ziel: Reiseziel des Ziels, sonst sein Liegeplatz (dann ebenfalls andocken), sonst seine Position
        SectorId sector; Vec2 pos; StationId station = default;
        if (target.Transit is { } t) { sector = t.ToSector; pos = t.ToPosition; station = u.StationAt(t.ToSector, t.ToPosition); }
        else if (target.DockedAt.Value != 0 && u.Stations.TryGetValue(target.DockedAt, out var st)) { sector = st.Sector; pos = st.DockPoint(ship.Id); station = st.Id; }
        else { sector = target.Sector; pos = target.Position; }
        // Runde 36 (Kette: Begleiter folgt Begleiter): das Reiseziel eines Begleiters endet oft nahe einem Andockpunkt, ohne dass er dort andockt — ein
        // Spielerschiff dockt nur, wenn die Kette der Führer dort wirklich andockt (sonst lag es kurz mit Liegeplatz und zahlte Gebühr)
        if (ship.IsPlayer && station.Value != 0 && target.Transit is not null && !DocksAt(u, target, station, 8)) station = default;
        // Runde 20 (Handy-Test: eigene Begleiter neben dem Spieler galten als „angedockt“): folgt ein Spielerschiff einem Schiff, das nicht angedockt ist und
        // keine Station ansteuert, liegt es nicht am Liegeplatz — auch wenn seine Folgereise zufällig am Andockpunkt endete (nur Spielerschiffe: NPC unverändert)
        if (station.Value == 0 && ship.IsPlayer && ship.DockedAt.Value != 0 && ship.Transit is null) ship.DockedAt = default;
        if (station.Value != 0 ? ship.DockedAt == station : OrderMove.Near(ship, sector, pos, KeepRange)) return CommandStatus.Running;
        if (station.Value != 0 && u.Stations.TryGetValue(station, out var dst)) pos = dst.DockPoint(ship.Id);
        if (!OrderMove.Go(u, ship, sector, pos, station)) ship.NextThinkTick = u.Tick + OrderMove.RetryTicks;
        return CommandStatus.Running;
    }

    public void Stop(Universe u, Ship ship, ShipCommand command) => OrderMove.StopHere(u, ship);

    /// <summary>
    /// Runde 36: dockt <paramref name="s"/> an <paramref name="station"/> an? Ein Schiff mit eigenem Befehl (kein Folgen/Eskorte) ja, ein Begleiter nur, wenn
    /// sein Führer dort liegt oder (rekursiv, höchstens <paramref name="depth"/> Glieder) selbst dorthin zum Andocken unterwegs ist.
    /// </summary>
    static bool DocksAt(Universe u, Ship s, StationId station, int depth)
    {
        if (depth <= 0 || !u.Ships.TryGetValue(TargetOf(s.Command ?? new HoldCommand()), out var lead)) return s.Command is not (FollowCommand or EscortCommand) || depth <= 0;
        if (lead.DockedAt == station) return true;
        return lead.Transit is { } t && u.StationAt(t.ToSector, t.ToPosition) == station && DocksAt(u, lead, station, depth - 1);
    }

    public virtual string Describe(Universe u, Ship ship, ShipCommand command) => $"Folgt {OrderMove.ShipName(u, TargetOf(command))}";
}

/// <summary>Eskorte: folgt wie <see cref="FollowExecutor"/> und verteidigt das Ziel bei Überfällen (<see cref="PirateSystem"/>).</summary>
public sealed class EscortExecutor : FollowExecutor
{
    public override string Kind => CommandKinds.Escort;
    public override Capability Needs => Capability.Combat;
    public override string Describe(Universe u, Ship ship, ShipCommand command) => $"Eskortiert {OrderMove.ShipName(u, TargetOf(command))}";
}

/// <summary>Heimkehr: zur Heimatbasis (sonst nächste Station mit Reparatur), dort tanken und reparieren, dann angedockt bleiben.</summary>
public sealed class ReturnExecutor : IOrderExecutor
{
    public string Kind => CommandKinds.Return;
    public Capability Needs => Capability.None;

    public string? Check(Universe u, Ship ship, ShipCommand command) => Home(u, ship) is null ? "Keine Heimatbasis erreichbar." : null;
    public void Start(Universe u, Ship ship, ShipCommand command) { ship.Order = null; ship.CommandStep = 0; ship.NextThinkTick = u.Tick; }

    public CommandStatus Step(Universe u, Ship ship, ShipCommand command, int dt)
    {
        var home = Home(u, ship);
        if (home is null) return CommandStatus.Failed;
        var s = OrderMove.MoveTo(u, ship, home.Sector, home.DockPoint(ship.Id), home.Id, out bool there);
        if (!there) return s;
        Service(u, ship, home);
        return CommandStatus.Done;
    }

    public void Stop(Universe u, Ship ship, ShipCommand command) => OrderMove.StopHere(u, ship);
    public string Describe(Universe u, Ship ship, ShipCommand command) => Home(u, ship) is { } h ? $"Heimkehr nach {h.Name}" : "Heimkehr";

    /// <summary>Heimatbasis: die des Schiffs, sonst die des Verbands, sonst die nächste erreichbare Station mit Reparatur.</summary>
    public static Station? Home(Universe u, Ship ship)
    {
        if (u.Stations.TryGetValue(ship.HomeBase, out var h) && OrderMove.Reachable(u, ship, h.Sector)) return h;
        if (u.Squadrons.TryGetValue(ship.Squadron, out var sq) && u.Stations.TryGetValue(sq.HomeBase, out var sh) && OrderMove.Reachable(u, ship, sh.Sector)) return sh;
        return NearestWith(u, ship, RepairService.ServiceKey);
    }

    /// <summary>Nächste erreichbare Station, deren Typ den Dienst führt (Reisezeit, dann Id).</summary>
    public static Station? NearestWith(Universe u, Ship ship, string service) =>
        u.Stations.Values.Where(st => !st.IsBuildSite && u.StationTypes.TryGetValue(st.TypeKey, out var t) && t.Services.Contains(service))
            .Select(st => (st, t: Travel.Estimate(u, ship.Sector, ship.Position, st.Sector, st.DockPoint(ship.Id), ship)))
            .Where(x => !float.IsPositiveInfinity(x.t)).OrderBy(x => x.t).ThenBy(x => x.st.Id.Value).Select(x => x.st).FirstOrDefault();

    /// <summary>Versorgung an der Station: volltanken (Marktpreis, Kasse des Besitzers) und — wenn sie Reparatur anbietet — Hülle reparieren.</summary>
    public static void Service(Universe u, Ship ship, Station st)
    {
        if (UnitSupport.Serve(u, ship, st)) return;   // Phase 27: Polizei/Piraten an ihrer Basis aus der Kasse des Besitzers
        if (Refuel.ActiveFuel(ship) is { } fuel && st.Market.TryGetValue(fuel, out var e) && e.Amount > 0f && e.Price > 0f && ship.FuelSpace > 0.5f)
            Refuel.BuyFuel(u, ship, st, fuel, ship.FuelSpace);
        // Phase 26: Hülle und Module (repair:all), sobald etwas beschädigt ist
        if (RepairService.NeedsRepair(ship) && u.StationTypes.TryGetValue(st.TypeKey, out var type) && type.Services.Contains(RepairService.ServiceKey))
        {
            var repair = new RepairService();
            string offer = u.Rules.DamageActive ? RepairService.All : RepairService.Full;
            if (repair.IsAvailable(u, st)) repair.Execute(u, st, ship, new ServiceRequest($"{RepairService.ServiceKey}:{offer}", default, 0f));
        }
    }
}
