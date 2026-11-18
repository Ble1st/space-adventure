using SpaceSim.Core.Combat;
using SpaceSim.Core.Comms;
using SpaceSim.Core.Events;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Mining;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;
using SpaceSim.Core.Weapons;

namespace SpaceSim.PlayerBot;

/// <summary>
/// Der Spielerbot: spielt ab einem neuen Spiel als Schürfer/Händler. Alles, was er tut, geht über Spielerpfade (Dienste, Befehle, Comms, Gefecht).
/// Strategie in Phasen: (1) ehrlich schürfen, (2) Schiffe kaufen, (3) Handels-/Schürfflotte mit Automatik, (4) Sektorbehörden gründen,
/// (5) bewaffnete Streifen als Polizei, (6) Kampfschiffe (Fregatten …) als Militär.
/// </summary>
public sealed class Bot
{
    readonly Pilot P; readonly Universe U; readonly Log L; readonly Metrics M;
    readonly CommsActions _comms = CommsActions.Default();
    public string Phase = "Schürfen";
    long _nextBuy, _nextMil;
    ShipId _flagship;
    readonly HashSet<ShipId> _commanded = new();
    readonly Dictionary<SectorId, long> _seatFail = new();
    public int Purchases, SeatsFounded;
    long _lastEarnTick;

    readonly Extras _x;
    public Bot(Pilot p, Log log, Metrics m) { P = p; U = p.U; L = log; M = m; _flagship = U.PlayerShip; _x = new Extras(p, log); }
    public Extras X => _x;

    float Reserve => 1500f;
    Ship Flag => U.Ships.TryGetValue(_flagship, out var s) ? s : P.Me;

    // ───────── Hauptschleife ─────────

    public void Step()
    {
        if (!U.Ships.ContainsKey(U.PlayerShip)) return;
        // Schiffsverlust: das aktive Schiff wechselt (Landung an einer Station)
        if (!U.Ships.ContainsKey(_flagship)) { _flagship = U.PlayerShip; L.Event($"Flaggschiff verloren → neues Flaggschiff #{_flagship.Value}"); }
        if (U.PlayerShip != _flagship && U.Ships.ContainsKey(_flagship) && P.Me.Id != _flagship) SwitchToFlag();
        if (P.Danger()) { P.Fight(); return; }

        P.Where = "Fleet"; Fleet();
        P.Where = "Founding"; if (Founding()) return;
        P.Where = "Military"; if (Military()) return;
        P.Where = "Earn"; Earn();
    }

    // ───────── Verdienen: schürfen/handeln mit dem Flaggschiff ─────────

    void Earn()
    {
        var me = P.Me;
        if (P.Here is { } here) { P.Refuel(); P.RepairHere(); _x.SampleServices(); if (U.Tick > 3000 && _x.Missions()) return; }
        bool miner = me.Stats.ContactMining > 0f || me.Stats.HasLaser;
        if (miner && me.Stats.Cargo >= 30f) { if (MineCycle()) return; }
        TradeCycle();
    }

    /// <summary>Beste Felsklasse nach Erzwert, erreichbar innerhalb weniger Sprünge; Bug an den Fels, abbauen, beim besten Abnehmer verkaufen.</summary>
    bool MineCycle()
    {
        P.Where = "MineCycle:select";
        var me = P.Me;
        if (me.DockedAt.Value != 0) { P.Refuel(); P.RepairHere(); P.Undock(); }
        float cap = me.CargoCapacity;
        Asteroid? best = null; Sector? bestSec = null; double bestScore = 0;
        foreach (var sec in U.Sectors.Values.OrderBy(s => s.Id.Value))
        {
            int hops = U.Hops(me.Sector, sec.Id);
            if (hops < 0 || hops > 3) continue;
            foreach (var a in sec.Asteroids)
            {
                if (!a.Minable || a.Ore < 10f || a.ClassKey == "" || !U.AsteroidClasses.TryGetValue(a.ClassKey, out var cls)) continue;
                double val = 0, wsum = 0;
                foreach (var y in cls.Yields)
                {
                    if (y.Weight <= 0) continue;
                    var b = MarketInfo.BestBuyerForGood(U, y.Good);
                    double price = b is not null && b.Market.TryGetValue(y.Good, out var e) ? e.Price : 0;
                    val += y.Weight * price; wsum += y.Weight;
                }
                if (wsum <= 0 || val <= 0) continue;
                double perUnit = val / wsum;
                double units = Math.Min(a.Ore, cap);
                double secs = 20 + hops * 25 + Vec2.Distance(me.Position, a.Position) / 40.0 + units / Math.Max(0.5, me.Stats.ContactMining) + 40;
                double score = perUnit * units / secs;
                if (score > bestScore) { bestScore = score; best = a; bestSec = sec; }
            }
        }
        if (best is null || bestSec is null) { L.Event("Schürfen: kein lohnender Fels"); P.Run(200); return false; }
        P.Where = "MineCycle:goto";
        if (!P.GoToSector(bestSec.Id, out var why)) { if (P.Danger()) return true; L.Event($"Schürfen: Weg zu {bestSec.Name} verwehrt ({why})"); P.Run(100); return false; }
        me = P.Me;
        // Anflugseite ohne Nachbarfels vor dem Bug
        var dir = new Vec2(1f, 0f);
        for (int k = 0; k < 16; k++)
        {
            var d = Vec2.FromAngle(k * MathF.PI / 8f);
            var p0 = best.Position - d * (best.Radius + ContactMining.BowOffset + 1f);
            if (bestSec.Asteroids.Where(o => o != best).All(o => Vec2.Distance(o.Position, p0) - o.Radius > Vec2.Distance(best.Position, p0) - best.Radius + 2f)) { dir = d; break; }
        }
        var pos = best.Position - dir * (best.Radius + ContactMining.BowOffset + 1f);
        if (!P.FlyTo(pos)) return true;
        me = P.Me; me.Position = pos;
        P.Where = "MineCycle:mine";
        long t0 = U.Tick; int idle = 0;
        while (best.Minable && U.Tick - t0 < 4000 && !P.Danger())
        {
            var st = ContactState.NoContact;
            for (int i = 0; i < 5; i++) st = P.Chunks.Contact(U, me, bestSec, pos, dir, 0.05f);
            if (me.AnchorRock != 0) { pos = MiningAnchor.HoldPosition(pos, best, Collision.ShipRadius); dir = (best.Position - pos).Normalized(); me.Position = pos; }
            if (st == ContactState.HoldFull) break;
            if (st is ContactState.NoContact or ContactState.NoTool or ContactState.Released) { if (++idle > 12) break; } else idle = 0;
            P.Step(); P.TicksMined++;
        }
        if (me.AnchorRock != 0) MiningAnchor.Release(U, me, me.Sector, "released");
        if (P.Danger()) return true;
        P.Where = "MineCycle:sell"; SellCargo();
        return true;
    }

    void SellCargo()
    {
        var me = P.Me;
        if (me.UsedVolume(U) < 1f) return;
        for (int round = 0; round < 3 && me.UsedVolume(U) >= 1f; round++)
        {
            var b = TradeActions.BestBuyer(U, me, me.Sector, me.Position);
            if (b is null) break;
            if (!P.GoToStation(b, out _)) return;
            float got = P.SellAllHere();
            M.EarnedSales += got;
            P.Refuel(); P.RepairHere();
            _lastEarnTick = U.Tick;
        }
        // Rest wegwerfen, damit der Laderaum frei wird (wie ein Spieler es täte)
        if (P.Here is { } st && me.UsedVolume(U) >= 1f && P.Svc(st, "trade") is { } svc)
            foreach (var g in me.Cargo.Where(kv => kv.Value > 0f).Select(kv => kv.Key).ToList())
                if (P.Offer(st, svc, $"drop:{g.Value}") is { Amount: > 0f }) P.Do(st, svc, $"drop:{g.Value}", g, 0f);
    }

    /// <summary>Händlerrunde: bester ehrlicher Abnehmer je Ware beim Hersteller in der Nähe, nach Gewinn je Sekunde.</summary>
    void TradeCycle()
    {
        var me = P.Me;
        if (me.UsedVolume(U) >= 1f) { SellCargo(); return; }
        if (me.Stats.Cargo < 10f) { P.Run(200); return; }
        double bestRate = 0; Station? bs = null; BuyerQuote? bq = null; Good? bg = null; float bu = 0;
        float cap = me.CargoCapacity;
        float money = P.Wallet - 100f;
        if (money < 20f) { L.Event("Handel: kein Geld"); P.Run(400); return; }
        foreach (var st in U.StationsById())
        {
            if (st.IsBuildSite || st.Credits < 0) continue;
            var route = U.FindRoute(me.Sector, st.Sector, GateAccess.ProfileOf(U, me));
            if (route is null || route.Count > 3) continue;
            foreach (var kv in st.Market)
            {
                var good = U.Goods[kv.Key];
                if (!st.Produces(kv.Key) || kv.Value.Amount < 5f || kv.Value.Price <= 0f) continue;
                float units = MathF.Min(MathF.Min(kv.Value.Amount, money / kv.Value.Price), me.FreeUnits(U, good.Id));
                units = MathF.Floor(units);
                if (units < 3f) continue;
                var q = MarketInfo.BestHonestBuyer(U, kv.Key, units, st.Sector, st.Position, st.Id, me.Id);
                if (q is null) continue;
                float sold = MathF.Min(units, q.Value.Units);
                if (sold < 3f) continue;
                double profit = sold * q.Value.Price - units * kv.Value.Price;
                if (profit <= 0) continue;
                double secs = 25 + route.Count * 25 + q.Value.Jumps * 25 + Vec2.Distance(me.Position, st.Position) / 40 + 40;
                double rate = profit / secs;
                if (rate > bestRate) { bestRate = rate; bs = st; bq = q; bg = good; bu = units; }
            }
        }
        if (bs is null || bq is null || bg is null) { L.Event("Handel: keine lohnende Runde"); P.Run(300); return; }
        if (!P.GoToStation(bs, out var why)) { if (!P.Danger()) { L.Event($"Handel: {why}"); P.Run(100); } return; }
        var svc = P.Svc(bs, "trade")!;
        if (P.Offer(bs, svc, $"buy:{bg.Id.Value}") is not { Amount: > 0f } bo) { P.Run(50); return; }
        float want = MathF.Min(bu, bo.Amount);
        var r = P.Do(bs, svc, $"buy:{bg.Id.Value}", bg.Id, want);
        if (!r.Success) { L.Event($"Handel: Kauf fehlgeschlagen: {r.Message}"); P.Run(100); return; }
        float paid = -r.Credits;
        P.Refuel();
        if (!P.GoToStation(bq.Value.Station, out _)) return;
        float got = P.SellAllHere();
        M.EarnedTrade += got - paid;
        _lastEarnTick = U.Tick;
    }

    // ───────── Flotte: kaufen, befehlen ─────────

    int OwnedShips => PlayerFleet.Owned(U).Count;

    void SwitchToFlag()
    {
        var me = P.Me; var flag = Flag;
        if (flag.Id == me.Id) return;
        if (me.DockedAt.Value != 0 && flag.DockedAt == me.DockedAt && U.Stations.TryGetValue(me.DockedAt, out var st))
            PlayerFleet.SwitchTo(U, st, flag.Id, out _);
        else PlayerFleet.SwitchRemote(U, flag.Id, out _);
    }

    /// <summary>Kauft Schiffe, wenn Geld da ist, und hängt sie an die Automatik; verteilt Streifen für die Polizei.</summary>
    long _nextFlagCheck;
    /// <summary>Handarbeit lohnt: das Flaggschiff gegen den besten bezahlbaren Schürfer tauschen (höchste Abbaurate), das alte bleibt in der Flotte.</summary>
    void UpgradeFlagship()
    {
        if (U.Tick < _nextFlagCheck) return;
        _nextFlagCheck = U.Tick + 3000;
        var flag = Flag; double cur = flag.Stats.ContactMining * Math.Min(1.0, flag.Stats.Cargo / 60.0);
        if (P.Wallet < 6000f) return;
        var yard = P.NearestWith("shipdealer", st => st.TypeKey == "shipyard"); if (yard is null) return;
        var svc = P.Svc(yard, "shipdealer"); if (svc is null) return;
        ShipQuote? best = null; double bestV = cur * 1.3;
        foreach (var m in ShipDealerService.Models(U, yard))
        {
            if (ShipDealerService.Quote(U, yard, flag, m.Key) is not { Buyable: true } q) continue;
            if (q.Stats.ContactMining <= 0f || q.Price > P.Wallet - Reserve) continue;
            double v = q.Stats.ContactMining * Math.Min(2.0, q.Stats.Cargo / 60.0);
            if (v > bestV) { bestV = v; best = q; }
        }
        if (best is null) return;
        if (!P.GoToStation(yard, out _)) return;
        var r = P.Do(yard, svc, $"buy:{best.Model.Key}");
        L.Event($"Flaggschiff-Tausch {best.Model.Name} {best.Price:0} Cr (Rate {best.Stats.ContactMining:0.0}/s, Hold {best.Stats.Cargo:0}): {(r.Success ? "ok" : r.Message)}");
        if (r.Success) { _flagship = U.PlayerShip; Purchases++; M.Purchases++; }
    }

    void Fleet()
    {
        UpgradeFlagship();
        ManageCommands();
        if (U.Tick < _nextBuy) return;
        float price = NextBuyBudget();
        if (P.Wallet < price + Reserve) return;
        var want = NextRole();
        if (want is null) { _nextBuy = U.Tick + 2000; return; }
        BuyShip(want);
        _nextBuy = U.Tick + 400;
    }

    /// <summary>Mindestgeld für den nächsten Kauf (grobe Schätzung des billigsten brauchbaren Modells).</summary>
    float NextBuyBudget()
    {
        int n = OwnedShips;
        return n < 3 ? 2500f : n < 8 ? 4500f : 8000f;
    }

    string? NextRole()
    {
        int own = OwnedShips;
        var owned = PlayerFleet.Owned(U);
        int traders = owned.Count(s => s.Command is TradeCommand || (s.Squadron.Value != 0 && IsRole(s, "trade")));
        int miners = owned.Count(s => s.Command is MineCommand || (s.Squadron.Value != 0 && IsRole(s, "mine")));
        int ownedSec = PlayerSeats.OwnSectors(U).Count;
        // Wirtschaftsflotte bis ~24 Schiffe, danach Polizei/Militär
        if (own - Police().Count - MilitaryShips().Count < 24) return traders <= miners * 1.5 ? "trade" : "mine";
        return null;
    }

    static bool IsRole(Ship s, string role) => role == "trade" ? s.Role == ShipRole.Trader : s.Role == ShipRole.Miner;

    List<Ship> Police() => PlayerFleet.Owned(U).Where(s => s.Id != U.PlayerShip && s.Command is PatrolCommand or GuardCommand).ToList();
    List<Ship> MilitaryShips() => PlayerFleet.Owned(U).Where(s => IsMilitary(s)).ToList();
    bool IsMilitary(Ship s) => s.Has(Capability.Combat) && U.ShipModels.TryGetValue(s.ModelKey, out var m) && m.Jobs.Count == 0;

    void BuyShip(string role)
    {
        var yard = P.NearestWith("shipdealer", st => st.TypeKey == "shipyard");
        if (yard is null) return;
        if (!P.GoToStation(yard, out _)) return;
        var me = P.Me; var svc = P.Svc(yard, "shipdealer");
        if (svc is null) return;
        P.Refuel(); P.RepairHere();
        ShipQuote? best = null; double bestVal = 0;
        foreach (var m in ShipDealerService.Models(U, yard))
        {
            if (ShipDealerService.Quote(U, yard, me, m.Key) is not { Buyable: true } q) continue;
            if (!m.Jobs.Contains(role)) continue;
            if (!m.Jobs.Contains(role) || q.Price > P.Wallet - Reserve) continue;
            double val = role == "trade" ? q.Stats.Cargo * Math.Min(1.5, q.Stats.Speed / 8.0) : q.Stats.MiningTier * Math.Min(1.0, q.Stats.Cargo / 60.0);
            val /= q.Price;
            if (val > bestVal) { bestVal = val; best = q; }
        }
        if (best is null) { L.Event($"Kauf {role}: nichts lieferbar/bezahlbar bei {yard.Name}"); return; }
        var r = P.Do(yard, svc, $"buy:{best.Model.Key}");
        if (!r.Success) { L.Event($"Kauf {best.Model.Key} fehlgeschlagen: {r.Message}"); return; }
        Purchases++; M.Purchases++;
        L.Event($"gekauft {best.Model.Name} ({role}) für {best.Price:0} Cr bei {yard.Name}");
        // neues Schiff wurde aktiv → zurück ins Flaggschiff, das neue bleibt angedockt
        SwitchToFlag();
    }

    void ManageCommands()
    {
        var cap = Commands.Capacity(U, U.PlayerActor);
        foreach (var s in PlayerFleet.Owned(U))
        {
            if (s.Id == U.PlayerShip || s.Id == _flagship || s.Command is not null || s.Squadron.Value != 0) continue;
            if (s.IsRental()) continue;
            if (s.Has(Capability.Cargo) && U.ShipModels.TryGetValue(s.ModelKey, out var m) && m.Jobs.Count > 0)
            {
                bool trade = m.Jobs.Contains("trade") && (!m.Jobs.Contains("mine") || PlayerFleet.Owned(U).Count(x => x.Command is TradeCommand) <= PlayerFleet.Owned(U).Count(x => x.Command is MineCommand));
                var home = s.DockedAt.Value != 0 && U.Stations.TryGetValue(s.DockedAt, out var hs) ? hs.Sector : s.Sector;
                var limits = new AutoLimits(home, -1, 1.5f, 400f);
                ShipCommand cmd = trade ? new TradeCommand(limits) : new MineCommand(limits);
                if (!Commands.Give(U, s, cmd, out var msg)) L.Event($"Befehl #{s.Id.Value} {cmd.Kind}: {msg}");
                else L.Event($"Befehl #{s.Id.Value} {cmd.Kind}");
            }
        }
    }

    // ───────── Sektorbehörden gründen ─────────

    bool Founding()
    {
        if (P.Wallet < 0) return false;
        var type = PlayerSeats.SeatType(U); if (type is null) return false;
        float cost = PlayerSeats.Cost(U, type);
        var own = PlayerSeats.OwnSectors(U);
        if (own.Count >= 5) return false;
        if (P.Wallet < cost + 8000f) return false;
        var cls = ActorRules.ClassOf(U, ActorRules.Index(U), U.PlayerActor);
        if ((int)cls < U.Rules.PlayerSeatMinClass) return false;
        var me = P.Me;
        // Kandidaten: herrenlos, ohne Versteck, noch keine eigene Behörde, nicht kürzlich gescheitert
        var cands = U.Sectors.Values.Where(s => !U.IsNpcFaction(s.Owner) && PlayerSeats.OwnSectors(U).All(o => o.Id != s.Id)
            && !s.Stations.Any(id => U.Stations.TryGetValue(id, out var st) && StationProtection.IsHideout(U, st))
            && (!_seatFail.TryGetValue(s.Id, out var ft) || U.Tick - ft > 20000)).OrderBy(s => U.Hops(me.Sector, s.Id) < 0 ? 99 : U.Hops(me.Sector, s.Id)).ToList();
        if (cands.Count == 0) return false;
        var tgt = cands[0];
        L.Event($"Behörde: Flug nach {tgt.Name} (Kasse {P.Wallet:0}, Kosten {cost:0})");
        if (!P.GoToSector(tgt.Id, out var why)) { if (!P.Danger()) { _seatFail[tgt.Id] = U.Tick; L.Event($"Behörde: Weg nach {tgt.Name} verwehrt: {why}"); } return true; }
        me = P.Me;
        if (me.DockedAt.Value != 0) P.Undock();
        var issue = PlayerSeats.FoundIssue(U, tgt.Id);
        if (issue is not null) { _seatFail[tgt.Id] = U.Tick; L.Event($"Behörde in {tgt.Name} nicht möglich: {issue}"); return true; }
        var r = _comms.Execute(U, $"seat:found:{tgt.Id.Value}");
        L.Event($"Behörde gründen {tgt.Name}: {r.Message}");
        if (r.Success) { SeatsFounded++; M.Seats++; P.Run(60); }
        else _seatFail[tgt.Id] = U.Tick;
        return true;
    }

    // ───────── Polizei und Militär ─────────

    /// <summary>Kauft Kampfschiffe (erst bewaffnete Streifen, dann Fregatten/Zerstörer/Schlachtkreuzer) und verteilt Streifen in eigenen Sektoren.</summary>
    bool Military()
    {
        var own = PlayerSeats.OwnSectors(U);
        if (own.Count == 0) return false;
        AssignPatrols(own);
        if (U.Tick < _nextMil) return false;
        var policeNow = PlayerFleet.Owned(U).Count(s => s.Id != U.PlayerShip && s.Has(Capability.Combat) && s.Command is PatrolCommand or GuardCommand);
        var mil = MilitaryShips();
        int big = mil.Count(s => U.ShipClasses.TryGetValue(s.ClassKeyOf(U), out var c) && c.Size >= SlotSize.L);
        bool needPolice = policeNow < 6 + 2;
        bool needMil = mil.Count < 12 || big < 4;
        if (!needPolice && !needMil) return false;
        if (P.Wallet < 20000f) return false;
        _nextMil = U.Tick + 600;
        var yard = P.NearestWith("shipdealer", st => st.TypeKey == "shipyard");
        if (yard is null) return false;
        if (!P.GoToStation(yard, out _)) return true;
        var me = P.Me; var svc = P.Svc(yard, "shipdealer"); if (svc is null) return true;
        P.Refuel(); P.RepairHere();
        ShipQuote? pick = null; double pickVal = 0;
        foreach (var m in ShipDealerService.Models(U, yard))
        {
            if (m.Jobs.Count > 0 || ShipDealerService.Quote(U, yard, me, m.Key) is not { Buyable: true } q) continue;
            if (!q.Stats.Capabilities.HasFlag(Capability.Combat) || q.Stats.WeaponPower <= 0f) continue;
            if (q.Price > P.Wallet - 8000f) continue;
            bool large = q.Class.Size >= SlotSize.L;
            double val;
            if (needMil && big < 4 && large) val = q.Stats.WeaponPower / q.Price * 3.0;    // erst die großen
            else if (needPolice) val = q.Stats.WeaponPower / q.Price * (large ? 0.5 : 1.0);
            else val = q.Stats.WeaponPower / q.Price * (large ? 2.0 : 1.0);
            if (val > pickVal) { pickVal = val; pick = q; }
        }
        if (pick is null) { L.Event($"Militär: nichts Passendes lieferbar bei {yard.Name} (Kasse {P.Wallet:0})"); return true; }
        var r = P.Do(yard, svc, $"buy:{pick.Model.Key}");
        L.Event($"Militär-Kauf {pick.Model.Name} {pick.Price:0} Cr: {(r.Success ? "ok" : r.Message)}");
        if (r.Success) { M.Purchases++; SwitchToFlag(); }
        return true;
    }

    void AssignPatrols(List<Sector> own)
    {
        var ids = own.Select(s => s.Id).ToList();
        var seats = PlayerSeats.Seats(U);
        int k = 0;
        foreach (var s in PlayerFleet.Owned(U).Where(x => x.Id != U.PlayerShip && x.Id != _flagship && x.Has(Capability.Combat) && x.Squadron.Value == 0 && x.Command is null && IsMilitary(x)))
        {
            ShipCommand cmd = (k++ % 2 == 0 && seats.Count > 0) ? new GuardCommand(seats[(k / 2) % seats.Count].Id) : new PatrolCommand(ids);
            if (!Commands.Give(U, s, cmd, out var msg, ignoreCapacity: false)) { if (U.Tick % 4000 < 40) L.Event($"Streife #{s.Id.Value}: {msg}"); }
            else L.Event($"Streife/Wache #{s.Id.Value} {cmd.Kind}");
        }
    }
}

static class ShipExt
{
    public static bool IsRental(this Ship s) => s.RentedFrom.Value != 0;
    public static string ClassKeyOf(this Ship s, Universe u) => u.ShipModels.TryGetValue(s.ModelKey, out var m) ? m.ClassKey : "";
}
