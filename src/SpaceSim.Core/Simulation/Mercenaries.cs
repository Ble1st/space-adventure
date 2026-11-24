using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Söldner per Comms (Lücke gegenüber Interstellar Pilot 2; docs/FLOTTEN.md „Söldner“). Anbieter sind Gilden-Fraktionen und aufgestiegene unabhängige
/// Firmen, die den Spieler kennen und ihm nicht feindlich sind. Ein Söldner ist ein bewaffnetes Schiff des Anbieters (Modell <see cref="EconomyRules.MercenaryModel"/>,
/// Rolle <see cref="ShipRole.Fighter"/>, Steuerer KI): es startet an der nächsten erreichbaren, nicht feindlichen Station mit Hangar, begleitet das aktive
/// Schiff des Spielers über den vorhandenen <see cref="EscortCommand"/> (verteidigt es nach den Einsatzregeln wie jede Eskorte) und kehrt nach Ablauf
/// über <see cref="ReturnCommand"/> heim und verlässt die Welt (<see cref="DepartureReason.Returned"/>). Es bleibt Eigentum des Anbieters: kein eigenes
/// Schiff des Spielers (kein Verkauf, kein Umbau, keine Befehle), Wracks enthalten keine Module (kein Gratis-Material durch Abschuss).
/// <para>Sold: je Spieltag im Voraus aus der Spielerkasse in die Kasse des Anbieters. Entscheidung: <b>keine Schulden</b> — reicht die Kasse für den
/// nächsten Tag nicht, endet der Vertrag (ein Söldner arbeitet nicht auf Pump, der Spieler soll nicht unbemerkt ins Minus rutschen).</para>
/// Kein Zufall; ohne Vertrag tut <see cref="MercenarySystem"/> nichts (NPC-Welt bitgleich).
/// </summary>
public static class Mercenaries
{
    public const string Detail = "mercenary";

    /// <summary>Ist das Schiff ein Söldner unter Vertrag?</summary>
    public static bool IsMerc(Universe u, ShipId ship) => u.Mercenaries.Count > 0 && u.Mercenaries.Exists(c => c.Ship == ship);

    public static MercContract? Of(Universe u, ShipId ship) => u.Mercenaries.Find(c => c.Ship == ship);

    /// <summary>Laufende (nicht endende) Verträge.</summary>
    public static int Active(Universe u) => u.Mercenaries.Count(c => !c.Ending);

    /// <summary>Mögliche Anbieter (Gilden-Fraktionen und Unabhängige), nach Id.</summary>
    public static IEnumerable<Actor> Providers(Universe u) =>
        u.Actors.Values.Where(a => a.Kind == ActorKind.Independent || (a.Kind == ActorKind.Guild && a.IsNpcFaction(u))).OrderBy(a => a.Id.Value);

    /// <summary>Startstation des Söldners: erreichbare, andockbare, nicht feindliche Station mit Hangar — wenige Sprünge vom Spieler zuerst, bei Gleichstand eine des Anbieters (bzw. seiner Fraktion), dann die nächste.</summary>
    public static Station? Spawn(Universe u, Actor provider)
    {
        if (!u.Ships.TryGetValue(u.PlayerShip, out var player)) return null;
        var pf = provider.Membership;
        Station? best = null;
        (int hops, int own, float dist, int id) bestKey = (int.MaxValue, 9, float.MaxValue, int.MaxValue);
        foreach (var st in u.StationsById())
        {
            if (st.IsBuildSite || !st.Dockable || !u.StationTypes.TryGetValue(st.TypeKey, out var t) || !t.Services.Contains(Services.HangarService.ServiceKey)) continue;
            if (Relations.Effective(u, st.Owner, u.PlayerActor) <= u.Rules.HostileRelation) continue;
            if (!Intel.Knowledge.KnowsStation(u, st)) continue;
            int hops = u.Hops(player.Sector, st.Sector);
            if (hops < 0) continue;
            int own = st.Owner == provider.Id || (pf is { } f && u.FactionOf(st.Owner) == f) ? 0 : 1;
            float dist = st.Sector == player.Sector ? Vec2.Distance(st.Position, player.Position) : 0f;
            var key = (hops, own, dist, st.Id.Value);
            if (key.CompareTo(bestKey) < 0) { bestKey = key; best = st; }
        }
        return best;
    }

    /// <summary>Darf beim Anbieter für so viele Tage angeheuert werden? null = ja, sonst Grund (rein).</summary>
    public static string? HireIssue(Universe u, ActorId providerId, int days)
    {
        var r = u.Rules;
        if (!r.Mercenaries) return "Söldner gibt es in dieser Welt nicht.";
        if (!u.Actors.TryGetValue(providerId, out var a) || !Providers(u).Any(p => p.Id == providerId)) return "Kein Söldneranbieter.";
        if (!r.MercenaryDays.Contains(days)) return "Unbekannte Vertragslänge.";
        if (!u.Ships.ContainsKey(u.PlayerShip)) return "Kein aktives Schiff.";
        if (Active(u) >= r.MercenaryMaxActive) return $"Höchstens {r.MercenaryMaxActive} Söldner gleichzeitig.";
        var f = a.IsFaction(u) ? new FactionId(a.Id.Value) : a.Membership;
        if (f is { } kf && !Intel.Knowledge.KnowsFaction(u, kf)) return "Anbieter unbekannt.";
        if (Wars.AtWar(u, providerId, u.PlayerActor)) return "Krieg mit dem Anbieter.";
        float rel = Relations.Effective(u, providerId, u.PlayerActor);
        if (rel < r.MercenaryMinRelation) return $"Beziehung zu schlecht ({rel:+0.00;-0.00}).";
        if (!u.ShipModels.ContainsKey(r.MercenaryModel)) return "Kein Söldnerschiff verfügbar.";
        if (Spawn(u, a) is null) return "Keine erreichbare Station mit Hangar.";
        if (u.WalletOf(u.Ships[u.PlayerShip]).Credits < r.MercenaryFeePerDay) return $"Nicht genügend Credits ({r.MercenaryFeePerDay:0} Cr für den ersten Tag).";
        return null;
    }

    /// <summary>Heuert an: Schiff des Anbieters an der Startstation, erster Tag sofort bezahlt, Eskorte des aktiven Schiffs. null bei Sperrgrund.</summary>
    public static Ship? Hire(Universe u, ActorId providerId, int days, out string message)
    {
        if (HireIssue(u, providerId, days) is { } issue) { message = issue; return null; }
        var r = u.Rules;
        var a = u.Actors[providerId];
        var home = Spawn(u, a)!;
        var model = u.ShipModels[r.MercenaryModel];
        var cls = u.ShipClasses[model.ClassKey];
        var id = u.NewShipId();
        var ship = ShipPurchase.Create(u, id, model.Key, cls.Slots, model.Fit, ShipRole.Fighter, Controller.Ai, providerId, home, 0f);
        ship.HomeBase = home.Id;
        ship.Engagement = Engagement.Defend;
        u.AddShip(ship);
        float fee = r.MercenaryFeePerDay;
        var c = new MercContract { Ship = id, Actor = providerId, Home = home.Id, Since = u.Tick, Days = days, PaidDays = 0, FeePerDay = fee };
        u.Mercenaries.Add(c);
        Pay(u, c);
        Commands.Give(u, ship, new EscortCommand(u.PlayerShip), out _, ignoreCapacity: true);
        u.Events.Publish(new MercenaryHired(id, providerId, home.Id, days, fee));
        message = $"{model.Name} #{id.Value} von {ActorNames.Tagged(u, providerId)} angeheuert: {days} Tag{(days == 1 ? "" : "e")}, {fee:0} Cr je Tag — startet an {StationNames.Short(u, home)}.";
        return ship;
    }

    static void Pay(Universe u, MercContract c)
    {
        var wallet = u.Player.Treasury;
        wallet.Credits -= c.FeePerDay;
        if (u.Actors.TryGetValue(c.Actor, out var a)) a.Treasury.Credits += c.FeePerDay;
        c.PaidDays++;
        u.Events.Publish(new TreasuryIncome(c.Actor, "mercenary", c.FeePerDay));
        u.Events.Publish(new MercenaryPaid(c.Ship, c.Actor, c.PaidDays, c.FeePerDay));
    }

    /// <summary>Vertrag beenden (Grund: expired, broke, cancelled): das Schiff fliegt heim und verlässt danach die Welt.</summary>
    public static void End(Universe u, MercContract c, string reason)
    {
        if (c.Ending) return;
        c.Ending = true;
        u.Events.Publish(new MercenaryEnded(c.Ship, c.Actor, reason));
        if (!u.Ships.TryGetValue(c.Ship, out var ship)) return;
        if (!Commands.Give(u, ship, new ReturnCommand(), out _, ignoreCapacity: true)) Leave(u, c, ship);
    }

    static void Leave(Universe u, MercContract c, Ship ship)
    {
        u.Mercenaries.Remove(c);
        Departures.RemoveShip(u, ship, DepartureReason.Returned, Detail);
    }

    /// <summary>Anzeige: Restzeit und Sold eines Vertrags.</summary>
    public static string Text(Universe u, MercContract c)
    {
        long left = c.Since + (long)c.Days * u.Rules.DayTicks - u.Tick;
        string name = u.Ships.TryGetValue(c.Ship, out var s) && u.ShipModels.TryGetValue(s.ModelKey, out var m) ? $"{m.Name} #{c.Ship.Value}" : $"#{c.Ship.Value}";
        if (c.Ending) return $"{name} ({ActorNames.Tagged(u, c.Actor)}) fliegt heim";
        long sec = Math.Max(0, left) / Math.Max(1, (long)SimClock.TicksPerSecond);
        return $"{name} ({ActorNames.Tagged(u, c.Actor)}) · noch {sec / 60}:{sec % 60:00} · {c.FeePerDay:0} Cr/Tag";
    }

    /// <summary>Ein Schritt (<see cref="MercenarySystem"/>): Sold je angefangenem Tag, Ablauf, Eskortziel = aktives Schiff, Heimkehr und Abgang.</summary>
    public static void Step(Universe u)
    {
        if (u.Mercenaries.Count == 0) return;
        foreach (var c in u.Mercenaries.ToList())
        {
            if (!u.Ships.TryGetValue(c.Ship, out var ship))
            {
                u.Mercenaries.Remove(c);
                if (!c.Ending) u.Events.Publish(new MercenaryEnded(c.Ship, c.Actor, "destroyed"));
                continue;
            }
            if (c.Ending)
            {
                if (u.Fights.IsEngaged(ship.Id)) continue;
                if (ship.Command is null || (ship.Transit is null && ship.DockedAt == c.Home)) Leave(u, c, ship);
                continue;
            }
            long end = c.Since + (long)c.Days * u.Rules.DayTicks;
            if (u.Tick >= end) { End(u, c, "expired"); continue; }
            int day = (int)((u.Tick - c.Since) / Math.Max(1, u.Rules.DayTicks)) + 1;
            bool broke = false;
            while (c.PaidDays < day)
            {
                if (u.Player.Treasury.Credits < c.FeePerDay) { broke = true; break; }
                Pay(u, c);
            }
            if (broke) { End(u, c, "broke"); continue; }
            if (u.Fights.IsEngaged(ship.Id) || ship.Emergency) continue;
            if (u.Ships.ContainsKey(u.PlayerShip) && !(ship.Command is EscortCommand ec && ec.Target == u.PlayerShip))
                Commands.Give(u, ship, new EscortCommand(u.PlayerShip), out _, ignoreCapacity: true);
        }
    }
}

/// <summary>Söldnerverträge (Intervall <see cref="Interval"/>); ohne Vertrag nichts.</summary>
public sealed class MercenarySystem : ISimSystem
{
    public const int Interval = 10;
    public void Step(Universe u, int dt) => Mercenaries.Step(u);
}
