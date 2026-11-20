using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Ausbau Punkt 3 (docs/KAMPF.md „Konvois“): Bei hohem Routenrisiko bündelt eine Fraktion beladene Händler (eigene Mitglieder oder freie) auf derselben
/// Strecke zu einem Konvoi mit 1–<see cref="EconomyRules.ConvoyMaxEscorts"/> Polizeischiffen als Geleit. Ein Händler wartet nach dem Einkauf an der Station
/// (Auftrag bleibt, keine Reise), bis <see cref="EconomyRules.ConvoyMaxTraders"/> beisammen sind oder der erste <see cref="EconomyRules.ConvoyWaitTicks"/>
/// gewartet hat; dann fahren alle zugleich los (mit Geleit nur ab <see cref="EconomyRules.ConvoyMinTraders"/>). Das Geleit begleitet je einen Händler
/// (<see cref="EscortCommand"/>, wechselt zum nächsten, wenn dieser ankommt) und steht bei Überfällen allen Konvoihändlern im Sektor bei
/// (<see cref="FleetGuards.Escorts"/>). Der Konvoi löst sich auf, wenn alle angekommen sind (spätestens nach <see cref="EconomyRules.ConvoyTicks"/>).
/// Kein Zufall; feste Reihenfolge (Konvoi-Id, Schiffs-Id).
/// </summary>
public static class ConvoyRules
{
    /// <summary>Konvoi, zu dem der Händler gehört (sammelnd oder unterwegs); null = keiner.</summary>
    public static Convoy? Of(Universe u, ShipId ship)
    {
        if (u.Convoys.Count == 0) return null;
        foreach (var c in u.Convoys) if (c.Traders.Contains(ship)) return c;
        return null;
    }

    /// <summary>Fraktion, die an diesem Ort Konvois bildet (KI-Fraktion mit Polizei; keine Gesetzlosen/Banditen); null = keine.</summary>
    static FactionId? FactionAt(Universe u, SectorId sector)
    {
        if (!u.Sectors.TryGetValue(sector, out var sec) || !u.IsNpcFaction(sec.Owner)) return null;
        var f = sec.Owner;
        if (Bandits.IsEstablished(u, f) || u.Actors.TryGetValue(new ActorId(f.Value), out var a) && a.Kind == ActorKind.Outlaw) return null;
        return f;
    }

    /// <summary>Freie Streifen der Fraktion im Sektor (nicht im Gefecht, nicht auf dem Heimweg, kein Geleit/Verfolgung; Einzelschiff oder Verbandsanführer).</summary>
    public static List<Ship> FreeEscorts(Universe u, FactionId f, SectorId sector)
    {
        var list = new List<Ship>();
        foreach (var id in u.Tracker.ParkedIn(sector).Concat(u.Tracker.TransitIn(sector)).Distinct().OrderBy(i => i.Value))
        {
            if (!u.Ships.TryGetValue(id, out var p) || !ThreatMap.IsPolice(p) || u.FactionOf(p) != f || u.Fights.IsEngaged(id) || p.Emergency) continue;
            if (p.Command is not (null or PatrolCommand) || PoliceCommands.NeedsBase(u, p) || ThreatMap.Where(u, p).Sector != sector) continue;
            if (Squadrons.Get(u, p) is { } sq && sq.Leader != p.Id) continue;
            list.Add(p);
        }
        return list;
    }

    /// <summary>
    /// Nach dem Einkauf, vor der Abfahrt (<see cref="TraderSystem"/>): soll der Händler auf einen Konvoi warten? true = er wartet (oder ist mit dem Konvoi
    /// schon losgefahren) — der Aufrufer fährt nicht selbst.
    /// </summary>
    public static bool Hold(Universe u, Ship ship, Station at, TradeOrder order)
    {
        var r = u.Rules;
        if (!r.Convoys || !r.FleetCombat || ship.Controller != Controller.Ai || !ship.IsEconomyAi || ship.Command is not null || ship.Transit is not null) return false;
        if (!u.Stations.TryGetValue(order.Sell, out var sell)) return false;
        if (FactionAt(u, at.Sector) is not { } f) return false;
        if (ship.Owner != u.FreeActor && u.FactionOf(ship) != f) return false;
        if (ThreatMap.RouteRisk(u, at.Sector, sell.Sector) < r.ConvoyRouteRisk) return false;
        Convoy? c = null;
        foreach (var k in u.Convoys)
            if (k.Departed == 0 && k.Faction == f && k.From == at.Sector && k.To == sell.Sector && k.Traders.Count < Math.Max(1, r.ConvoyMaxTraders)) { c = k; break; }
        if (c is null)
        {
            if (FreeEscorts(u, f, at.Sector).Count == 0) return false;
            c = new Convoy { Id = u.NextConvoyId++, Faction = f, From = at.Sector, To = sell.Sector, Formed = u.Tick };
            u.Convoys.Add(c);
        }
        c.Traders.Add(ship.Id);
        if (c.Traders.Count >= Math.Max(1, r.ConvoyMaxTraders)) Release(u, c);
        return true;
    }

    static bool Waiting(Universe u, ShipId id, out Ship ship) =>
        u.Ships.TryGetValue(id, out ship!) && ship.Transit is null && ship.Order is TradeOrder o && ship.Phase == TradePhase.GoingToSell
        && u.Stations.ContainsKey(o.Sell) && ship.Cargo.Count > 0 && !u.Fights.IsEngaged(id) && ship.Role == ShipRole.Trader;

    static bool EnRoute(Universe u, Convoy c, ShipId id) =>
        u.Ships.TryGetValue(id, out var s) && s.Transit is { } t && t.ToSector == c.To && s.Order is TradeOrder && s.Phase == TradePhase.GoingToSell && s.Cargo.Count > 0;

    /// <summary>Alle wartenden Händler fahren los; ab <see cref="EconomyRules.ConvoyMinTraders"/> mit Geleit, sonst löst sich der Konvoi auf.</summary>
    public static void Release(Universe u, Convoy c)
    {
        var r = u.Rules;
        var gone = new List<ShipId>();
        foreach (var id in c.Traders)
            if (Waiting(u, id, out var s) && TraderSystem.DepartToSell(u, s, (TradeOrder)s.Order!)) gone.Add(id);
        c.Traders.Clear();
        c.Traders.AddRange(gone);
        int waited = (int)(u.Tick - c.Formed);
        if (gone.Count < Math.Max(1, r.ConvoyMinTraders))
        {
            u.Convoys.Remove(c);
            u.Events.Publish(new ConvoyDisbanded(c.Id, "small", waited));
            return;
        }
        int want = Math.Clamp(gone.Count > 3 ? r.ConvoyMaxEscorts : 1, 0, Math.Max(0, r.ConvoyMaxEscorts));
        foreach (var p in FreeEscorts(u, c.Faction, c.From))
        {
            if (c.Escorts.Count >= want) break;
            var ward = c.Escorts.Count == 0 ? gone[0] : gone[^1];
            if (PoliceCommands.Give(u, p, new EscortCommand(ward))) c.Escorts.Add(p.Id);
        }
        if (c.Escorts.Count == 0)
        {
            u.Convoys.Remove(c);
            u.Events.Publish(new ConvoyDisbanded(c.Id, "noescort", waited));
            return;
        }
        c.Departed = u.Tick;
        u.Events.Publish(new ConvoyFormed(c.Id, c.Faction, c.From, c.To, gone.Count, c.Escorts.Count));
    }

    static void Dissolve(Universe u, Convoy c, string reason)
    {
        foreach (var id in c.Escorts)
            if (u.Ships.TryGetValue(id, out var p) && p.Command is EscortCommand ec && c.Traders.Contains(ec.Target)) Commands.Cancel(u, p, "Konvoi aufgelöst");
        u.Convoys.Remove(c);
        u.Events.Publish(new ConvoyDisbanded(c.Id, reason, (int)(u.Tick - c.Formed)));
    }

    /// <summary>Schritt (<see cref="ConvoySystem"/>): Sammelnde Konvois prüfen und abfahren lassen; unterwegs Geleit nachführen; ankommen = auflösen.</summary>
    public static void Step(Universe u)
    {
        if (u.Convoys.Count == 0) return;
        var r = u.Rules;
        foreach (var c in u.Convoys.OrderBy(k => k.Id).ToList())
        {
            if (c.Departed == 0)
            {
                c.Traders.RemoveAll(id => !Waiting(u, id, out _));
                if (c.Traders.Count == 0) { u.Convoys.Remove(c); u.Events.Publish(new ConvoyDisbanded(c.Id, "empty", (int)(u.Tick - c.Formed))); continue; }
                if (!r.Convoys || c.Traders.Count >= Math.Max(1, r.ConvoyMaxTraders) || u.Tick - c.Formed >= r.ConvoyWaitTicks) Release(u, c);
                continue;
            }
            var active = c.Traders.Where(id => EnRoute(u, c, id)).ToList();
            if (active.Count == 0) { Dissolve(u, c, "arrived"); continue; }
            if (!r.Convoys || u.Tick - c.Departed >= r.ConvoyTicks) { Dissolve(u, c, "timeout"); continue; }
            // Geleit nachführen: ist der begleitete Händler angekommen, übernimmt die Streife den nächsten noch fahrenden
            foreach (var id in c.Escorts)
            {
                if (!u.Ships.TryGetValue(id, out var p) || u.Fights.IsEngaged(id) || p.Emergency) continue;
                if (p.Command is EscortCommand ec && active.Contains(ec.Target)) continue;
                if (p.Command is not (null or PatrolCommand or EscortCommand) || PoliceCommands.NeedsBase(u, p)) continue;
                if (p.Command is EscortCommand old && !c.Traders.Contains(old.Target)) continue;   // andere Aufgabe übernommen
                var ward = active[^1];
                if (u.Ships.TryGetValue(ward, out var w) && Jurisdiction.Covers(u, p, ThreatMap.Where(u, w).Sector)) PoliceCommands.Give(u, p, new EscortCommand(ward));
            }
        }
    }
}

/// <summary>Ausbau Punkt 3: Konvois führen (Sicherheits-Modul, nach der Polizei).</summary>
public sealed class ConvoySystem : ISimSystem
{
    public const int Interval = 5;
    public void Step(Universe u, int dt) => ConvoyRules.Step(u);
}
