using SpaceSim.Core.Combat;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Trading;

namespace SpaceSim.Core.Simulation;

/// <summary>Welle D: ein Versorger hat ein eigenes Schiff aus seiner Fracht nachgefüllt (Treibstoff, Munition, Raketen in Einheiten).</summary>
public sealed record FleetSupplied(ShipId Supplier, ShipId Target, float Fuel, float Ammo, float Missiles) : SimEvent;

/// <summary>
/// Welle D (Punkt 1): reine Regeln des Versorgers. Ziele = eigene Schiffe (gleicher Besitzer) im selben Sektor, nicht unterwegs, höchstens
/// <see cref="EconomyRules.FleetSupplyRange"/> entfernt (Verbandsmitglieder doppelt so weit); dazu der Versorger selbst. Kein Zufall, Id-Reihenfolge.
/// </summary>
public static class FleetSupply
{
    /// <summary>Ziele des Versorgers jetzt (Id-Reihenfolge, der Versorger zuerst).</summary>
    public static List<Ship> Targets(Universe u, Ship supplier)
    {
        var list = new List<Ship> { supplier };
        if (supplier.Transit is not null) return list;
        float range = u.Rules.FleetSupplyRange;
        foreach (var s in u.ShipsById())
        {
            if (s.Id == supplier.Id || s.Owner != supplier.Owner || s.Transit is not null || s.Sector != supplier.Sector) continue;
            float r = supplier.Squadron.Value != 0 && s.Squadron == supplier.Squadron ? range * 2f : range;
            if (Vec2.Distance(s.Position, supplier.Position) <= r) list.Add(s);
        }
        return list;
    }

    /// <summary>Bedarf eines Schiffs je Versorgungsware (Treibstoffsorten seiner Antriebe, Munition, Raketen) in Einheiten.</summary>
    public static void AddNeed(Universe u, Ship s, Dictionary<GoodId, float> need)
    {
        if (Refuel.Active(u) && s.FuelSpace > 0.5f && Refuel.ActiveFuel(s) is { } fuel)
            need[fuel] = need.GetValueOrDefault(fuel) + s.FuelSpace;
        if (Rearm.AmmoGood(u) is { } ammo && Rearm.Missing(u, s) >= 1f) need[ammo.Id] = need.GetValueOrDefault(ammo.Id) + Rearm.Missing(u, s);
        if (Missiles.MissileGood(u) is { } mis && Missiles.Missing(u, s) >= 1f) need[mis.Id] = need.GetValueOrDefault(mis.Id) + Missiles.Missing(u, s);
    }

    /// <summary>Füllt ein Ziel aus der Fracht des Versorgers nach; liefert die übergebenen Mengen. Veröffentlicht <see cref="FleetSupplied"/>, wenn etwas floss.</summary>
    public static (float Fuel, float Ammo, float Missiles) Transfer(Universe u, Ship supplier, Ship target)
    {
        float fuelMoved = 0f, ammo = 0f, missiles = 0f;
        if (Refuel.Active(u))
            foreach (var ft in target.Stats.FuelTypes)
            {
                float have = supplier.Cargo.GetValueOrDefault(ft.Good);
                float n = MathF.Min(have, target.FuelSpace);
                if (n < 0.01f) continue;
                Take(supplier, ft.Good, n);
                target.Fuel[ft.Good] = target.FuelAmount(ft.Good) + n;
                fuelMoved += n;
            }
        if (Rearm.AmmoGood(u) is { } a)
        {
            float n = MathF.Min(supplier.Cargo.GetValueOrDefault(a.Id), Rearm.Missing(u, target));
            if (n >= 0.01f) { Take(supplier, a.Id, n); target.Systems.AmmoMissing = MathF.Max(0f, target.Systems.AmmoMissing - n); ammo = n; }
        }
        if (Missiles.MissileGood(u) is { } m)
        {
            float n = MathF.Floor(MathF.Min(supplier.Cargo.GetValueOrDefault(m.Id), Missiles.Missing(u, target)));
            if (n >= 1f) { Take(supplier, m.Id, n); target.Systems.MissilesMissing = MathF.Max(0f, target.Systems.MissilesMissing - n); missiles = n; }
        }
        if (fuelMoved + ammo + missiles > 0f) u.Events.Publish(new FleetSupplied(supplier.Id, target.Id, fuelMoved, ammo, missiles));
        return (fuelMoved, ammo, missiles);
    }

    static void Take(Ship s, GoodId g, float n)
    {
        float left = s.Cargo.GetValueOrDefault(g) - n;
        if (left <= 0.001f) s.Cargo.Remove(g); else s.Cargo[g] = left;
    }

    /// <summary>
    /// Welle D (<see cref="EconomyRules.PlayerWaitForSupplier"/>): Wartet ein eigenes Schiff (nicht das aktive) besser auf einen eigenen Versorger im selben
    /// Sektor, der die Treibstoffsorte an Bord hat, statt einen Notkauf zu machen?
    /// </summary>
    public static bool WaitFor(Universe u, Ship ship, GoodId fuel)
    {
        if (!u.Rules.PlayerWaitForSupplier || !u.Rules.FleetSupply || u.PlayerActor.Value == 0 || ship.Owner != u.PlayerActor || ship.Id == u.PlayerShip) return false;
        foreach (var s in u.ShipsById())
            if (s.Id != ship.Id && s.Owner == ship.Owner && s.Command is SupplyCommand && s.Sector == ship.Sector && s.Cargo.GetValueOrDefault(fuel) >= 1f) return true;
        return false;
    }

    /// <summary>Versorgt alle Ziele (Id-Reihenfolge); liefert die Summe der übergebenen Einheiten.</summary>
    public static float Serve(Universe u, Ship supplier)
    {
        float sum = 0f;
        foreach (var t in Targets(u, supplier))
        {
            var (f, a, m) = Transfer(u, supplier, t);
            sum += f + a + m;
        }
        return sum;
    }

    /// <summary>
    /// Welche Versorgungsware fehlt dem Versorger am meisten (Bedarf der Ziele minus Fracht; Bedarf ≥ 10 Einheiten)? null = nichts.
    /// </summary>
    public static GoodId? Shortest(Universe u, Ship supplier, List<Ship> targets)
    {
        var need = new Dictionary<GoodId, float>();
        foreach (var t in targets) AddNeed(u, t, need);
        GoodId? best = null; float bestGap = 0f;
        foreach (var (g, n) in need.OrderBy(kv => kv.Key.Value))
        {
            float gap = n - supplier.Cargo.GetValueOrDefault(g);
            if (n >= 10f && gap > n * 0.5f && gap > bestGap && supplier.FreeUnits(u, g) >= 1f) { best = g; bestGap = gap; }
        }
        return best;
    }

    /// <summary>
    /// Nächstes eigenes Schiff im Einsatzgebiet (Verband des Versorgers, sonst der Sektor <paramref name="sector"/>), das außer Reichweite steht, nicht
    /// unterwegs ist und etwas braucht, das der Versorger an Bord hat (Abstand, dann Id); null = keins.
    /// </summary>
    public static Ship? NeedyOutOfRange(Universe u, Ship supplier, SectorId sector)
    {
        Ship? best = null; float bd = float.MaxValue;
        float range = u.Rules.FleetSupplyRange;
        var need = new Dictionary<GoodId, float>();
        foreach (var s in u.ShipsById())
        {
            if (s.Id == supplier.Id || s.Owner != supplier.Owner || s.Transit is not null) continue;
            bool mine = supplier.Squadron.Value != 0 ? s.Squadron == supplier.Squadron : s.Sector == sector;
            if (!mine || s.Sector != supplier.Sector && supplier.Squadron.Value == 0) continue;
            float d = s.Sector == supplier.Sector ? Vec2.Distance(s.Position, supplier.Position) : float.MaxValue / 2f;
            if (d <= (supplier.Squadron.Value != 0 && s.Squadron == supplier.Squadron ? range * 2f : range)) continue;
            need.Clear(); AddNeed(u, s, need);
            bool any = false;
            foreach (var (g, n) in need) if (n >= 1f && supplier.Cargo.GetValueOrDefault(g) >= 1f) { any = true; break; }
            if (any && d < bd) { bd = d; best = s; }
        }
        return best;
    }

    /// <summary>Nächste erreichbare Station, die die Ware mit Bestand verkauft (Reisezeit, dann Id); null = keine.</summary>
    public static Station? Seller(Universe u, Ship ship, GoodId good)
    {
        Station? best = null; float bt = float.PositiveInfinity;
        foreach (var st in u.StationsById())
        {
            if (st.IsBuildSite || !st.Market.TryGetValue(good, out var e) || e.Amount < 1f || e.Price <= 0f || !Docking.Allows(u, st, ship)) continue;
            if (TradeActions.MaxBuyable(u, ship, st, good) < 1f) continue;
            float t = Travel.Estimate(u, ship.Sector, ship.Position, st.Sector, st.DockPoint(ship.Id), ship);
            if (t < bt) { bt = t; best = st; }
        }
        return best;
    }
}

/// <summary>
/// Welle D (Punkt 1): Ausführer des Befehls „Versorgen“ (<see cref="SupplyCommand"/>). Im Verband folgt der Versorger dem Anführer, sonst hält er im Sektor.
/// Alle <see cref="EconomyRules.FleetSupplyTicks"/> gibt er ab (<see cref="FleetSupply.Serve"/>); fehlt ihm eine Ware, die die Flotte braucht, fliegt er zur
/// nächsten Station, die sie verkauft, kauft (<see cref="TradeActions.Buy"/>, Spielerkasse) und kehrt zurück.
/// </summary>
public sealed class SupplyExecutor : IOrderExecutor
{
    public string Kind => CommandKinds.Supply;
    public Capability Needs => Capability.Cargo;

    public string? Check(Universe u, Ship ship, ShipCommand command)
    {
        if (!u.Rules.FleetSupply) return "Versorgen ist in dieser Welt abgeschaltet.";
        if (command is not SupplyCommand s || !u.Sectors.ContainsKey(s.Sector)) return "Sektor unbekannt.";
        return OrderMove.Reachable(u, ship, s.Sector) || ship.Sector == s.Sector ? null : "Sektor ist nicht erreichbar.";
    }

    public void Start(Universe u, Ship ship, ShipCommand command) { ship.Order = null; ship.CommandStep = 0; ship.NextThinkTick = u.Tick; }

    /// <summary>Wo der Versorger stehen will: beim Anführer seines Verbands, sonst im Befehlssektor (null = er steht richtig).</summary>
    static (SectorId Sector, Vec2 Pos)? Post(Universe u, Ship ship, SupplyCommand c)
    {
        if (u.Squadrons.TryGetValue(ship.Squadron, out var sq) && sq.Leader != ship.Id && u.Ships.TryGetValue(sq.Leader, out var lead))
        {
            if (lead.Transit is not null) return null;   // Anführer unterwegs: auf seine Ankunft warten, dann nachfliegen
            if (lead.Sector == ship.Sector && Vec2.Distance(lead.Position, ship.Position) <= u.Rules.FleetSupplyRange * 0.5f) return null;
            return (lead.Sector, lead.Position + new Vec2(6f, 6f));
        }
        if (ship.Sector == c.Sector) return null;
        return u.Sectors.TryGetValue(c.Sector, out var sec) ? (c.Sector, SecuritySystem.PatrolPoint(u, sec)) : null;
    }

    public CommandStatus Step(Universe u, Ship ship, ShipCommand command, int dt)
    {
        var c = (SupplyCommand)command;
        if (!u.Rules.FleetSupply) return CommandStatus.Failed;
        if (OrderMove.StepTransit(u, ship) == OrderMove.Progress.Moving) return CommandStatus.Running;
        if (u.Tick < ship.NextThinkTick) return CommandStatus.Running;
        ship.NextThinkTick = u.Tick + Math.Max(1, u.Rules.FleetSupplyTicks);

        // angedockt an einer Verkaufsstelle: fehlende Versorgungsware kaufen
        var targets = FleetSupply.Targets(u, ship);
        if (ship.DockedAt.Value != 0 && u.Stations.TryGetValue(ship.DockedAt, out var dock) && ship.CommandStep == 1)
        {
            var need = new Dictionary<GoodId, float>();
            foreach (var t in u.ShipsById()) if (t.Owner == ship.Owner && (t.Squadron == ship.Squadron && t.Squadron.Value != 0 || t.Sector == c.Sector)) FleetSupply.AddNeed(u, t, need);
            foreach (var (g, n) in need.OrderBy(kv => kv.Key.Value))
            {
                float want = MathF.Min(n - ship.Cargo.GetValueOrDefault(g), TradeActions.MaxBuyable(u, ship, dock, g));
                if (want >= 1f) TradeActions.Buy(u, ship, dock, g, MathF.Floor(want));
            }
            ship.CommandStep = 0;
        }

        FleetSupply.Serve(u, ship);

        // fehlt Ware? Nachschub holen (Schritt 1 = unterwegs zum Kauf)
        if (FleetSupply.Shortest(u, ship, targets) is { } short_ && ship.CommandStep == 0 && FleetSupply.Seller(u, ship, short_) is { } seller)
        {
            if (OrderMove.Go(u, ship, seller.Sector, seller.DockPoint(ship.Id), seller.Id)) { ship.CommandStep = 1; return CommandStatus.Running; }
            ship.NextThinkTick = u.Tick + OrderMove.RetryTicks;
            return CommandStatus.Running;
        }
        // ein eigenes Schiff im Einsatzgebiet braucht, was der Versorger an Bord hat, steht aber außer Reichweite: hinfliegen
        if (FleetSupply.NeedyOutOfRange(u, ship, c.Sector) is { } needy)
        {
            if (!OrderMove.Go(u, ship, needy.Sector, needy.Position + new Vec2(5f, 5f))) ship.NextThinkTick = u.Tick + OrderMove.RetryTicks;
            return CommandStatus.Running;
        }
        if (Post(u, ship, c) is { } post && !OrderMove.Go(u, ship, post.Sector, post.Pos)) ship.NextThinkTick = u.Tick + OrderMove.RetryTicks;
        return CommandStatus.Running;
    }

    public void Stop(Universe u, Ship ship, ShipCommand command) => OrderMove.StopHere(u, ship);

    public string Describe(Universe u, Ship ship, ShipCommand command)
    {
        var c = (SupplyCommand)command;
        string where = u.Squadrons.TryGetValue(ship.Squadron, out var sq) && sq.Leader != ship.Id ? $"Verband {sq.Name}" : OrderMove.SectorName(u, c.Sector);
        return ship.CommandStep == 1 ? $"Versorgen ({where}) — holt Nachschub" : $"Versorgen: {where}";
    }
}
