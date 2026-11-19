using SpaceSim.Core.Combat;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Trading;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Ausbau Punkt 1: Angreifen — ein bewaffnetes Spielerschiff fliegt zu seinem Ziel und kämpft. Schiffsziel: nähert sich bis <see cref="FleetAttack.EngageRange"/>
/// und stellt es (<see cref="FleetAttack.EngageShip"/>); verlässt das Ziel den Sektor, in dem es beim Befehl stand, ist es entkommen (Befehl erledigt — dafür
/// gibt es „Verfolgen“). Station/Tor: Anflug auf den Belagerungsabstand, dann Belagerung (<see cref="FleetAttack.Besiege"/>). Erledigt, wenn das Ziel zerstört,
/// ausgeschaltet, erobert oder weg ist; Verbandsmitglieder folgen danach wieder ihrem Anführer. Die Gefechtsebene steht darüber (der Befehl ruht im Gefecht).
/// </summary>
public sealed class FleetAttackExecutor : IOrderExecutor
{
    public string Kind => CommandKinds.Attack;
    public Capability Needs => Capability.Combat;

    public string? Check(Universe u, Ship ship, ShipCommand command)
    {
        if (command is not AttackCommand a) return "Ziel unbekannt.";
        if (!ship.IsPlayer) return "Nur für die eigene Flotte.";
        if (FleetAttack.RulesIssue(u, ship) is { } rules) return rules;
        if (FleetAttack.TargetIssue(u, ship, a) is { } issue) return issue;
        var sector = a.Ship.Value != 0 ? ThreatMap.Where(u, u.Ships[a.Ship]).Sector : FleetAttack.SiegeTargetOf(u, a)!.Sector;
        return OrderMove.Reachable(u, ship, sector) || ThreatMap.Where(u, ship).Sector == sector ? null : "Ziel ist für dieses Schiff nicht erreichbar.";
    }

    public void Start(Universe u, Ship ship, ShipCommand command) { ship.Order = null; ship.CommandStep = 0; ship.NextThinkTick = u.Tick; }

    CommandStatus Done(Universe u, Ship ship) => FleetAttack.BackToLeader(u, ship) ? CommandStatus.Running : CommandStatus.Done;

    public CommandStatus Step(Universe u, Ship ship, ShipCommand command, int dt)
    {
        var a = (AttackCommand)command;
        if (FleetAttack.Retreat(u, ship)) return CommandStatus.Running;
        if (a.Ship.Value != 0)
        {
            if (!u.Ships.TryGetValue(a.Ship, out var t)) return Done(u, ship);
            var (ts, _) = ThreatMap.Where(u, t);
            if (a.Sector.Value != 0 && ts != a.Sector) return Done(u, ship);   // entkommen
            if (FleetAttack.InReach(u, ship, t))
            {
                if (FleetAttack.EngageShip(u, ship, t) is null) OrderMove.StepTransit(u, ship);   // Ziel angedockt oder auf Spielerseite: warten
                return CommandStatus.Running;
            }
            FleetAttack.Close(u, ship, t);
            return CommandStatus.Running;
        }
        if (FleetAttack.SiegeTargetOf(u, a) is not { } target || !FleetAttack.StillTarget(u, target)) return Done(u, ship);
        var spot = AssaultExecutor.Standoff(u, target.Sector, target.Position, ship);
        if (!(ship.Transit is null && ship.Sector == target.Sector && Vec2.Distance(ship.Position, spot) <= 5f))
        {
            var s = OrderMove.MoveTo(u, ship, target.Sector, spot, default, out bool there);
            if (!there) return s;
        }
        if (ship.IsPlayer) ship.DockedAt = default;
        FleetAttack.Besiege(u, ship, target);
        return CommandStatus.Running;
    }

    public void Stop(Universe u, Ship ship, ShipCommand command) => OrderMove.StopHere(u, ship);

    public string Describe(Universe u, Ship ship, ShipCommand command)
    {
        var a = (AttackCommand)command;
        if (a.Ship.Value != 0) return u.Ships.TryGetValue(a.Ship, out var t) ? $"Greift {PlayerCombat.Describe(u, t)} an" : "Greift an (Ziel weg)";
        return FleetAttack.SiegeTargetOf(u, a) is { } st ? $"Greift {st.Name} an" : "Greift an (Ziel weg)";
    }
}

/// <summary>
/// Ausbau Punkt 1b: Bergen — ein Schiff mit Bergungsgreifer leert die Wracks und Behälter eines Sektors (Fracht über <see cref="Wrecks.TakeCargo"/>, Module ab
/// dem Mindestzustand über <see cref="Wrecks.TakeModule"/>), fährt die Beute zum besten Abnehmer (<see cref="TradeActions.BestBuyer"/>) und verkauft sie
/// (<see cref="TradeActions.SellAll"/>, Kasse des Besitzers, Rückkaufregeln), dann von vorn; ohne Wrack wartet es im Sektor. Ohne Zufall.
/// </summary>
public sealed class SalvageExecutor : IOrderExecutor
{
    /// <summary>Wartezeit, wenn nichts zu bergen oder niemand die Beute nimmt (Ticks).</summary>
    public const int IdleTicks = 100;

    public string Kind => CommandKinds.Salvage;
    public Capability Needs => Capability.Salvage;

    public string? Check(Universe u, Ship ship, ShipCommand command)
    {
        if (command is not SalvageCommand s || !u.Sectors.ContainsKey(s.Sector)) return "Sektor unbekannt.";
        if (!ship.Has(Capability.Cargo)) return "Bergen braucht einen Frachtraum.";
        return OrderMove.Reachable(u, ship, s.Sector) || ship.Sector == s.Sector ? null : "Sektor ist nicht erreichbar.";
    }

    public void Start(Universe u, Ship ship, ShipCommand command) { ship.Order = null; ship.CommandStep = 0; ship.NextThinkTick = u.Tick; }

    /// <summary>Lohnt dieses Wrack (Fracht, die passt, oder ein bergbares Modul)?</summary>
    public static bool Worth(Universe u, Ship ship, Wreck w)
    {
        foreach (var (g, n) in w.Cargo) if (n > 0.001f && ship.FreeUnits(u, g) >= 0.01f) return true;
        if (!ship.Has(Capability.Salvage)) return false;
        foreach (var m in w.Modules)
            if (m.Condition >= u.Rules.SalvageMinCondition && u.FindGood(m.Key) is { } good && ship.FreeUnits(u, good.Id) >= 1f) return true;
        return false;
    }

    /// <summary>Nächstes lohnendes Wrack im Sektor (Abstand, dann Id).</summary>
    public static Wreck? Next(Universe u, Ship ship, SectorId sector)
    {
        Wreck? best = null; float bd = float.MaxValue;
        var from = ship.Sector == sector ? ship.Position : default(Vec2);
        foreach (var w in u.Wrecks)
        {
            if (w.Sector != sector || !Worth(u, ship, w)) continue;
            float d = Vec2.Distance(from, w.Position);
            if (d < bd - 1e-4f || (MathF.Abs(d - bd) <= 1e-4f && best is not null && w.Id < best.Id)) { bd = d; best = w; }
        }
        return best;
    }

    public CommandStatus Step(Universe u, Ship ship, ShipCommand command, int dt)
    {
        var c = (SalvageCommand)command;
        if (OrderMove.StepTransit(u, ship) == OrderMove.Progress.Moving) return CommandStatus.Running;
        if (u.Tick < ship.NextThinkTick) return CommandStatus.Running;
        float loot = ship.Cargo.Values.Sum();
        // angedockt mit Beute: verkaufen
        if (loot > 0.001f && ship.DockedAt.Value != 0 && u.Stations.TryGetValue(ship.DockedAt, out var dock))
        {
            var sold = TradeActions.SellAll(u, ship, dock);
            ship.NextThinkTick = u.Tick + (sold.Revenue > 0f ? 1 : IdleTicks);
            if (sold.Revenue > 0f) return CommandStatus.Running;
        }
        var wreck = Next(u, ship, c.Sector);
        bool full = ship.CargoCapacity - ship.UsedVolume(u) < 1f;
        if (loot >= 1f && (full || wreck is null))
        {
            var buyer = TradeActions.BestBuyer(u, ship, ship.Sector, ship.Position, ship.DockedAt.Value != 0 ? ship.DockedAt : null);
            if (buyer is null) { ship.NextThinkTick = u.Tick + IdleTicks; return CommandStatus.Running; }
            if (!OrderMove.Go(u, ship, buyer.Sector, buyer.DockPoint(ship.Id), buyer.Id)) ship.NextThinkTick = u.Tick + OrderMove.RetryTicks;
            return CommandStatus.Running;
        }
        if (wreck is null)
        {
            // nichts zu bergen: im Sektor warten
            if (ship.Sector != c.Sector && u.Sectors.TryGetValue(c.Sector, out var sec) && !OrderMove.Go(u, ship, c.Sector, SecuritySystem.PatrolPoint(u, sec)))
                ship.NextThinkTick = u.Tick + OrderMove.RetryTicks;
            else ship.NextThinkTick = u.Tick + IdleTicks;
            return CommandStatus.Running;
        }
        if (ship.Sector != wreck.Sector || Vec2.Distance(ship.Position, wreck.Position) > u.Rules.SalvageRange - 1f)
        {
            if (!OrderMove.Go(u, ship, wreck.Sector, wreck.Position)) ship.NextThinkTick = u.Tick + OrderMove.RetryTicks;
            return CommandStatus.Running;
        }
        ship.DockedAt = default;   // neben einer Station gelandet: das Wrack zählt, nicht der Liegeplatz
        Wrecks.TakeCargo(u, ship, wreck, float.MaxValue, out _);
        if (ship.Has(Capability.Salvage))
            for (int guard = 0; guard < 16 && u.Wrecks.Contains(wreck); guard++)
            {
                int i = Wrecks.BestModule(u, ship, wreck);
                if (i < 0 || !Wrecks.TakeModule(u, ship, wreck, i, out _)) break;
            }
        ship.NextThinkTick = u.Tick + 4;
        return CommandStatus.Running;
    }

    public void Stop(Universe u, Ship ship, ShipCommand command) => OrderMove.StopHere(u, ship);
    public string Describe(Universe u, Ship ship, ShipCommand command) => $"Bergen in {OrderMove.SectorName(u, ((SalvageCommand)command).Sector)}";
}

/// <summary>
/// Ausbau Punkt 1b: Tor blockieren — ein bewaffnetes Schiff (bzw. ein Verband) hält am Tor und greift jedes Schiff der Kriegsgegner-Fraktion an, das dort
/// vorbeikommt (<see cref="BlockadeRange"/> um das Tor; nicht angedockte, nicht kämpfende Schiffe in Id-Reihenfolge). Nur im Krieg mit dieser Fraktion
/// (im Waffenstillstand ruht er, mit dem Frieden ist er erledigt) — also nie ein Verbrechen.
/// </summary>
public sealed class BlockadeExecutor : IOrderExecutor
{
    /// <summary>Umkreis um das Tor, in dem die Blockade angreift.</summary>
    public const float BlockadeRange = 80f;
    /// <summary>Ticks zwischen zwei Suchen nach Durchfahrern.</summary>
    public const int ScanTicks = 5;

    public string Kind => CommandKinds.Blockade;
    public Capability Needs => Capability.Combat;

    public string? Check(Universe u, Ship ship, ShipCommand command)
    {
        if (command is not BlockadeCommand b || !u.Gates.TryGetValue(b.Gate, out var g)) return "Tor unbekannt.";
        if (!ship.IsPlayer) return "Nur für die eigene Flotte.";
        if (!u.Factions.ContainsKey(b.Faction)) return "Fraktion unbekannt.";
        if (Wars.PlayerWar(u, b.Faction) is null) return "Tor blockieren geht nur im Krieg mit dieser Fraktion.";
        if (FleetAttack.RulesIssue(u, ship) is { } rules) return rules;
        return OrderMove.Reachable(u, ship, g.Sector) || ship.Sector == g.Sector ? null : "Tor ist nicht erreichbar.";
    }

    public void Start(Universe u, Ship ship, ShipCommand command) { ship.Order = null; ship.CommandStep = 0; ship.NextThinkTick = u.Tick; }

    /// <summary>Halteplatz am Tor (je Schiff eine eigene Richtung, frei von Hindernissen).</summary>
    public static Vec2 Spot(Universe u, Gate g, Ship ship) => CombatSpace.Clear(u, g.Sector, g.Position + Vec2.FromAngle(ship.Id.Value * 1.7f) * 20f);

    /// <summary>Erstes Schiff der Fraktion im Umkreis des Tors (frei, nicht angedockt; Id-Reihenfolge); null = keins.</summary>
    public static Ship? Passer(Universe u, Gate g, FactionId faction)
    {
        foreach (var id in u.Tracker.ParkedIn(g.Sector).Concat(u.Tracker.TransitIn(g.Sector)).Distinct().OrderBy(i => i.Value))
        {
            if (!u.Ships.TryGetValue(id, out var s) || s.IsPlayer || u.FactionOf(s) != faction || s.DockedAt.Value != 0 || u.Fights.IsEngaged(s.Id)) continue;
            var (sec, pos) = ThreatMap.Where(u, s);
            if (sec == g.Sector && Vec2.Distance(pos, g.Position) <= BlockadeRange) return s;
        }
        return null;
    }

    public CommandStatus Step(Universe u, Ship ship, ShipCommand command, int dt)
    {
        var b = (BlockadeCommand)command;
        if (!u.Gates.TryGetValue(b.Gate, out var g) || Wars.PlayerWar(u, b.Faction) is null) return FleetAttack.BackToLeader(u, ship) ? CommandStatus.Running : CommandStatus.Done;
        if (FleetAttack.Retreat(u, ship)) return CommandStatus.Running;
        var spot = Spot(u, g, ship);
        if (!(ship.Transit is null && ship.Sector == g.Sector && Vec2.Distance(ship.Position, spot) <= 5f))
        {
            var s = OrderMove.MoveTo(u, ship, g.Sector, spot, default, out bool there);
            if (!there) return s;
        }
        if (ship.IsPlayer) ship.DockedAt = default;
        if (u.Tick < ship.NextThinkTick) return CommandStatus.Running;
        ship.NextThinkTick = u.Tick + ScanTicks;
        if (u.PlayerFaction is { } me && Wars.Hostile(u, me, b.Faction) && Passer(u, g, b.Faction) is { } target) FleetAttack.EngageShip(u, ship, target);
        return CommandStatus.Running;
    }

    public void Stop(Universe u, Ship ship, ShipCommand command) => OrderMove.StopHere(u, ship);

    public string Describe(Universe u, Ship ship, ShipCommand command)
    {
        var b = (BlockadeCommand)command;
        string gate = u.Gates.TryGetValue(b.Gate, out var g) ? GateCombat.NameOf(u, g) : "Tor";
        return $"Blockiert {gate} gegen {(u.Factions.TryGetValue(b.Faction, out var f) ? ActorNames.Tagged(u, new ActorId(f.Id.Value)) : "?")}";
    }
}

/// <summary>
/// Ausbau Punkt 1b: Liefern (Pendel) — feste Route A → B mit fester Ware: an A kaufen (höchstens die Menge je Fahrt, nur bis zur Preisgrenze; über
/// <see cref="TradeActions.Buy"/> mit den Rückkaufregeln und der Kasse des Besitzers), an B verkaufen (<see cref="TradeActions.Sell"/>), wiederholen. Nimmt
/// B nichts (Lager voll) oder hat A nichts, wartet das Schiff angedockt und versucht es später. Zustandslos: Ware an Bord → nach B, sonst nach A.
/// </summary>
public sealed class DeliverExecutor : IOrderExecutor
{
    /// <summary>Wartezeit an A/B, wenn nichts zu kaufen bzw. nichts abzugeben ist (Ticks).</summary>
    public const int WaitTicks = 200;

    public string Kind => CommandKinds.Deliver;
    public Capability Needs => Capability.Cargo;

    public string? Check(Universe u, Ship ship, ShipCommand command)
    {
        if (command is not DeliverCommand d) return "Route unbekannt.";
        if (!u.Stations.TryGetValue(d.From, out var a) || a.IsBuildSite) return "Startstation unbekannt.";
        if (!u.Stations.TryGetValue(d.To, out var b) || b.IsBuildSite) return "Zielstation unbekannt.";
        if (d.From == d.To) return "Start und Ziel sind dieselbe Station.";
        if (!u.Goods.ContainsKey(d.Good)) return "Ware unbekannt.";
        if (!a.Market.ContainsKey(d.Good)) return $"{a.Name} führt diese Ware nicht.";
        if (!b.Market.ContainsKey(d.Good)) return $"{b.Name} nimmt diese Ware nicht.";
        if (ship.CapacityVolumeFor(u, d.Good) <= 0f) return "Die Ware passt nicht in den Frachtraum.";
        if (Docking.Issue(u, a, ship) is { } da) return da;
        if (Docking.Issue(u, b, ship) is { } db) return db;
        if (!(OrderMove.Reachable(u, ship, a.Sector) || ship.Sector == a.Sector) || !(OrderMove.Reachable(u, ship, b.Sector) || ship.Sector == b.Sector)) return "Route ist nicht erreichbar.";
        return null;
    }

    public void Start(Universe u, Ship ship, ShipCommand command) { ship.Order = null; ship.CommandStep = 0; ship.NextThinkTick = u.Tick; }

    public CommandStatus Step(Universe u, Ship ship, ShipCommand command, int dt)
    {
        var d = (DeliverCommand)command;
        if (!u.Stations.TryGetValue(d.From, out var a) || !u.Stations.TryGetValue(d.To, out var b)) return CommandStatus.Failed;
        var progress = OrderMove.StepTransit(u, ship);
        if (progress == OrderMove.Progress.Moving) return CommandStatus.Running;
        if (u.Tick < ship.NextThinkTick && progress != OrderMove.Progress.Arrived) return CommandStatus.Running;
        float have = ship.Cargo.GetValueOrDefault(d.Good);
        if (have >= 1f || (have > 0.001f && ship.DockedAt == b.Id))
        {
            if (ship.Transit is null && ship.DockedAt == b.Id)
            {
                var sold = TradeActions.Sell(u, ship, b, d.Good, have);
                ship.NextThinkTick = u.Tick + (sold.Revenue > 0f || have - sold.Sold.GetValueOrDefault(d.Good) < 1f ? 1 : WaitTicks);
                return CommandStatus.Running;
            }
            if (!OrderMove.Go(u, ship, b.Sector, b.DockPoint(ship.Id), b.Id)) ship.NextThinkTick = u.Tick + OrderMove.RetryTicks;
            return CommandStatus.Running;
        }
        if (ship.Transit is null && ship.DockedAt == a.Id)
        {
            float want = (d.Amount > 0f ? d.Amount : float.MaxValue) - have;
            float units = MathF.Floor(MathF.Min(want, TradeActions.MaxBuyable(u, ship, a, d.Good)));
            bool priceOk = d.MaxBuyFactor <= 0f || !a.Market.TryGetValue(d.Good, out var e) || e.Price <= u.Goods[d.Good].BasePrice * d.MaxBuyFactor + 1e-4f;
            // Nachtrag 1c: Verlustschutz (Standard an) — wie die Handelsautomatik: nicht kaufen, wenn B je Einheit nicht mehr zahlt, als A kostet
            if (units >= 1f && priceOk && !d.AllowLoss && LossIssue(u, ship, a, b, d.Good, units) is { } loss)
            {
                u.Events.Publish(new TraderBuyRefused(ship.Id, a.Id, b.Id, d.Good, loss.Buy, loss.Sell));
                priceOk = false;
            }
            float got = units >= 1f && priceOk ? TradeActions.Buy(u, ship, a, d.Good, units).Units : 0f;
            ship.NextThinkTick = u.Tick + (got > 0f ? 1 : WaitTicks);
            return CommandStatus.Running;
        }
        if (!OrderMove.Go(u, ship, a.Sector, a.DockPoint(ship.Id), a.Id)) ship.NextThinkTick = u.Tick + OrderMove.RetryTicks;
        return CommandStatus.Running;
    }

    /// <summary>
    /// Nachtrag 1c: Verlustschutz — wirksamer Stückpreis des Einkaufs an <paramref name="a"/> (mit Rückkaufboden) gegen den wirksamen Stückpreis des Verkaufs
    /// an <paramref name="b"/> (mit Rückkaufdeckel), beide zum heutigen Marktpreis. null = kein sicherer Verlust (B zahlt mehr, als A kostet).
    /// </summary>
    public static (float Buy, float Sell)? LossIssue(Universe u, Ship ship, Station a, Station b, GoodId good, float units)
    {
        if (!a.Market.TryGetValue(good, out var ea) || !b.Market.TryGetValue(good, out var eb)) return null;
        float buy = PlayerLedger.BuyUnitPrice(u, ship, a, good, units, ea.Price);
        float sell = PlayerLedger.SaleUnitPrice(u, ship, b, good, units, eb.Price);
        return sell > buy + 1e-4f ? null : (buy, sell);
    }

    public void Stop(Universe u, Ship ship, ShipCommand command) => OrderMove.StopHere(u, ship);

    public string Describe(Universe u, Ship ship, ShipCommand command)
    {
        var d = (DeliverCommand)command;
        string good = u.Goods.TryGetValue(d.Good, out var g) ? g.Name : "?";
        return $"Liefert {good}{(d.Amount > 0f ? $" ({d.Amount:0} je Fahrt)" : "")}: {OrderMove.StationName(u, d.From)} → {OrderMove.StationName(u, d.To)}{(d.AllowLoss ? " · Verlust erlaubt" : "")}";
    }
}

/// <summary>
/// Ausbau Punkt 1b: Sammelpunkt — die gewählten Schiffe fliegen zu einer Station (Halteplatz daneben, kein Andocken) und warten dort; sind alle Schiffe der
/// Gruppe da (oder weg), bildet der Anführer den Verband (<see cref="Squadrons.Create"/>, Beitritte mit den Verbandsgrenzen). Gegeben über
/// <see cref="FleetFormations.StartRally"/>.
/// </summary>
public sealed class RallyExecutor : IOrderExecutor
{
    public string Kind => CommandKinds.Rally;
    public Capability Needs => Capability.None;

    public string? Check(Universe u, Ship ship, ShipCommand command)
    {
        if (command is not RallyCommand r || !u.Stations.TryGetValue(r.At, out var st)) return "Treffpunkt unbekannt.";
        if (!ship.IsPlayer) return "Nur für die eigene Flotte.";
        if (ship.Squadron.Value != 0) return "Das Schiff gehört schon zu einem Verband.";
        if (!u.Ships.TryGetValue(r.Leader, out var lead) || !lead.IsPlayer) return "Anführer unbekannt.";
        return OrderMove.Reachable(u, ship, st.Sector) || ship.Sector == st.Sector ? null : "Treffpunkt ist nicht erreichbar.";
    }

    public void Start(Universe u, Ship ship, ShipCommand command) { ship.Order = null; ship.CommandStep = 0; ship.NextThinkTick = u.Tick; }

    /// <summary>Halteplatz eines Schiffs am Treffpunkt.</summary>
    public static Vec2 Spot(Universe u, Station st, Ship ship) =>
        CombatSpace.Clear(u, st.Sector, st.Position + Vec2.FromAngle(ship.Id.Value * 1.7f) * (st.Radius + 25f));

    internal static bool There(Universe u, Station st, Ship s) => s.Transit is null && s.Sector == st.Sector && Vec2.Distance(s.Position, Spot(u, st, s)) <= 5f;

    public CommandStatus Step(Universe u, Ship ship, ShipCommand command, int dt)
    {
        var r = (RallyCommand)command;
        if (!u.Stations.TryGetValue(r.At, out var st)) return CommandStatus.Failed;
        // ohne Anführer (weg oder Befehl beendet) löst sich der Treffpunkt auf
        if (!u.Ships.TryGetValue(r.Leader, out var lead) || lead.Command is not RallyCommand lr || lr.Leader != r.Leader) return CommandStatus.Done;
        // Nachtrag 1c: Frist abgelaufen — wer da ist, bildet den Verband; wer fehlt, verliert den Sammelbefehl (nie dauerhaft über der Kapazität)
        if (r.Until > 0 && u.Tick >= r.Until)
        {
            if (ship.Id != r.Leader) return There(u, st, ship) ? CommandStatus.Running : CommandStatus.Done;   // wartet auf die Bildung durch den Anführer
            FleetFormations.FormAtDeadline(u, ship, st, r);
            return CommandStatus.Done;
        }
        if (!There(u, st, ship))
        {
            var s = OrderMove.MoveTo(u, ship, st.Sector, Spot(u, st, ship), default, out bool there);
            if (!there) return s;
        }
        if (ship.IsPlayer) ship.DockedAt = default;
        if (ship.Id != r.Leader) return CommandStatus.Running;
        var group = u.ShipsById().Where(s => s.Command is RallyCommand rc && rc.Leader == r.Leader).ToList();
        if (group.Any(s => !There(u, st, s) && !u.Fights.IsEngaged(s.Id))) return CommandStatus.Running;
        if (group.Any(s => u.Fights.IsEngaged(s.Id))) return CommandStatus.Running;
        FleetFormations.FormAtRally(u, ship, group);
        return CommandStatus.Done;
    }

    public void Stop(Universe u, Ship ship, ShipCommand command) => OrderMove.StopHere(u, ship);
    public string Describe(Universe u, Ship ship, ShipCommand command)
    {
        var r = (RallyCommand)command;
        string until = r.Until > 0 ? $" · Frist {SpaceSim.Core.Missions.MissionRules.Rest(r.Until - u.Tick)}" : "";
        return $"Sammelpunkt {OrderMove.StationName(u, r.At)}{until}";
    }
}

/// <summary>
/// Ausbau Punkt 1b: Konvoi — Händler im Verband. Der Anführer (der langsamste Händler) handelt automatisch in den Grenzen wie „Handel“ (Ausführer
/// <see cref="TraderSystem"/>); jedes übrige Konvoi-Schiff übernimmt, sobald es leer und frei ist, die Kaufrunde des Anführers (gleiche Einkaufs- und
/// Verkaufsstation, gleiche Ware) und fährt sie mit dem Tempo des Verbands — der Rest (Kauf, Verkauf) läuft wie bei jedem Händler. Kampfschiffe des Konvois
/// begleiten den Anführer (<see cref="EscortCommand"/>) und treten jedem Gefecht eines Konvoi-Schiffs im selben Sektor bei (Verband). Gegeben über
/// <see cref="FleetFormations.StartConvoy"/>.
/// </summary>
public sealed class ConvoyExecutor : WorkExecutor
{
    public override string Kind => CommandKinds.Convoy;
    public override Capability Needs => Capability.Cargo;
    protected override ShipRole Role => ShipRole.Trader;

    public override string? Check(Universe u, Ship ship, ShipCommand command) => ship.IsPlayer ? null : "Nur für die eigene Flotte.";

    public override CommandStatus Step(Universe u, Ship ship, ShipCommand command, int dt)
    {
        var s = base.Step(u, ship, command, dt);
        if (s != CommandStatus.Running) return s;
        if (Squadrons.Get(u, ship) is not { } sq || sq.Leader == ship.Id || !u.Ships.TryGetValue(sq.Leader, out var lead)) return CommandStatus.Running;   // Anführer handelt selbst
        if (ship.Transit is not null || ship.Order is not null || ship.Cargo.Values.Any(n => n > 0.001f)) return CommandStatus.Running;   // fährt oder verkauft Reste
        if (lead.Command is ConvoyCommand && lead.Order is TradeOrder lo && lo.Buy.Value != 0 && lead.Phase is TradePhase.GoingToBuy or TradePhase.GoingToSell
            && u.Stations.TryGetValue(lo.Buy, out var buy) && u.Stations.ContainsKey(lo.Sell) && ship.CapacityVolumeFor(u, lo.Good) > 0f)
        {
            ship.Order = lo;
            ship.Phase = TradePhase.GoingToBuy;
            ship.IdleSince = -1;
            var plan = Refuel.PrepareTrip(u, ship, buy.Sector, buy.DockPoint(ship.Id));
            if (plan == Refuel.TripPlan.FuelStop) return CommandStatus.Running;
            if (plan == Refuel.TripPlan.Depart && Travel.Start(u, ship, buy.Sector, buy.DockPoint(ship.Id), Squadrons.Speed(u, ship))) return CommandStatus.Running;
            ship.Order = null; ship.Phase = TradePhase.Idle;
        }
        ship.NextThinkTick = u.Tick + 1;   // wartet auf die nächste Runde des Anführers (der Händler-Ausführer plant nicht selbst)
        return CommandStatus.Running;
    }

    public override string Describe(Universe u, Ship ship, ShipCommand command)
    {
        var lim = AutoLimitRules.Describe(u, ((ConvoyCommand)command).Limits, trade: true);
        return Squadrons.Get(u, ship) is { } sq && sq.Leader != ship.Id ? $"Konvoi: fährt mit {OrderMove.ShipName(u, sq.Leader)}" : "Konvoi führt, Handel automatisch" + lim;
    }
}
