using SpaceSim.Core.Events;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Model;
using SpaceSim.Core.Trading;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Ausbau Punkte 5/10/14: Durchsuchung durch die Polizei — eine Regel für KI-Schmuggler, Schmuggelaufträge und gesuchte Fahrgäste. Eine Streife der
/// Fraktion, der der Sektor gehört, durchsucht ein Schiff in <see cref="EconomyRules.SmugglerSearchRange"/>, wenn sie es mit ihrer Sensorstärke
/// (<see cref="EconomyRules.PoliceSensorStrength"/>) sieht (<see cref="Cloak.HiddenFrom(Universe, Ship, float)"/>: Tarnfeld + Nebel schlagen den Sensor).
/// Rein bis auf den Wurf in <see cref="Roll"/> (<see cref="Universe.Rng"/>, nur wenn eine Streife wirklich in Reichweite ist).
/// </summary>
public static class SmuggleSearch
{
    /// <summary>Wo ist das Schiff jetzt (auch unterwegs)?</summary>
    public static (SectorId Sector, Vec2 Pos) Where(Universe u, Ship s) => s.Transit is { } t ? AccessEnforcement.Where(t, u.Tick) : (s.Sector, s.Position);

    /// <summary>Polizeifraktion des Sektors mit einer Streife in Reichweite (null = keine). Nur Fraktionen der KI.</summary>
    public static FactionId? PoliceNear(Universe u, SectorId sector, Vec2 pos)
    {
        if (!u.Sectors.TryGetValue(sector, out var sec) || sec.Owner.Value == 0 || !u.IsNpcFaction(sec.Owner)) return null;
        var owner = sec.Owner;
        float range = u.Rules.SmugglerSearchRange;
        foreach (var p in u.ShipsById())
        {
            if (p.Role != ShipRole.Police || u.FactionOf(p) != owner) continue;
            var (ps, pp) = Where(u, p);
            if (ps == sector && Vec2.Distance(pp, pos) <= range) return owner;
        }
        return null;
    }

    /// <summary>Sieht die Polizei das Schiff (kein Tarnfeld, das den Polizeisensor schlägt)?</summary>
    public static bool Visible(Universe u, Ship s) => !Cloak.HiddenFrom(u, s, u.Rules.PoliceSensorStrength);

    /// <summary>Durchsuchung jetzt (Streife in Reichweite, sichtbar, Wurf unter <see cref="EconomyRules.SmugglerSearchChance"/>)? Liefert die Fraktion oder null.</summary>
    public static FactionId? Roll(Universe u, Ship s)
    {
        if (!u.Rules.SecurityActive) return null;
        var (sec, pos) = Where(u, s);
        if (PoliceNear(u, sec, pos) is not { } f || !Visible(u, s)) return null;
        return u.Rng.NextDouble() < u.Rules.SmugglerSearchChance ? f : null;
    }
}

/// <summary>
/// Ausbau Punkt 5 (docs/SCHWARZMARKT.md „Schmuggler-KI“): Gesetzlose KI-Firmen (aufgestiegene unabhängige Akteure mit Gesetzestreue unter
/// <see cref="EconomyRules.BlackMarketNpcLawfulness"/>) kaufen bis zu <see cref="EconomyRules.SmugglersPerFirm"/> Schmugglerschiffe (Tarnfeld + Sprungantrieb,
/// <see cref="EconomyRules.SmugglerModel"/>, eigene Rolle <see cref="ShipRole.Smuggler"/> — zählt nicht in der Handelskapazität). Ein Schmuggler fährt
/// Hehlerware vom Versteck zu ehrlichen Abnehmern und Embargo-Ware in Sektoren, die er auf dem Torweg nicht erreicht (Sprung an der Sperre vorbei);
/// mit Fracht tarnt er sich. Die Polizei durchsucht mit ihrer Sensorstärke (<see cref="SmuggleSearch"/>): erwischt → Fracht beschlagnahmt, Strafe an die
/// Fraktion, Fahndung für den Besitzer. Deterministisch (Id-Reihenfolge, Würfe nur bei Streife in Reichweite).
/// </summary>
public sealed class SmugglerSystem : ISimSystem
{
    public const int Interval = 5;
    /// <summary>Kaufprüfung alle so viele Ticks.</summary>
    public const int BuyEvery = 200;

    public void Step(Universe u, int dt)
    {
        var r = u.Rules;
        if (!r.Smugglers) return;
        if (u.Tick % BuyEvery < dt) Purchase(u);
        foreach (var ship in u.ShipsById())
        {
            if (ship.Role != ShipRole.Smuggler || ship.Controller != Controller.Ai || !u.Ships.ContainsKey(ship.Id)) continue;
            if (u.Fights.IsEngaged(ship.Id)) continue;
            if (HasCargo(ship) && SmuggleSearch.Roll(u, ship) is { } f) { Caught(u, ship, f); continue; }
            UpdateCloak(u, ship);
            if (ship.Transit is { } t)
            {
                if (u.Tick < t.ArrivalTick) continue;
                u.FinishTransit(ship);
                if (ship.Order is GoToOrder g && ship.DockedAt == g.Target && u.Stations.TryGetValue(g.Target, out var fs)) { Refuel.TankUp(u, ship, fs); ship.Order = null; }
            }
            if (JumpDrive.Charging(ship) || u.Tick < ship.NextThinkTick) continue;
            Proceed(u, ship);
        }
    }

    static bool HasCargo(Ship s) => s.Cargo.Values.Any(n => n > 0.001f);

    /// <summary>Gesetzlose Firma (eigener Akteur mit Charakter, Gesetzestreue unter der Schwelle)?</summary>
    public static bool LawlessFirm(Universe u, Actor a) =>
        a.Kind == ActorKind.Independent && a.Id != u.PlayerActor && a.Id != u.FreeActor && a.Personality.Lawfulness < u.Rules.BlackMarketNpcLawfulness;

    /// <summary>Schmuggler einer Firma.</summary>
    public static int CountOf(Universe u, ActorId firm) => u.Ships.Values.Count(s => s.Role == ShipRole.Smuggler && s.Owner == firm);

    // ───────── Kauf ─────────

    /// <summary>Höchstens ein Kauf je Prüfung: erste gesetzlose Firma (Id-Reihenfolge), deren freies Geld (Kasse + Schiffskassen über <see cref="EconomyRules.FirmShipKeep"/>) Preis + Startkapital + Rücklage trägt; Werft nahe dem reichsten Schiff.</summary>
    public static Ship? Purchase(Universe u)
    {
        var r = u.Rules;
        if (!u.ShipModels.TryGetValue(r.SmugglerModel, out var model) || !u.ShipClasses.TryGetValue(model.ClassKey, out var cls)
            || ShipPurchase.Materials(u, model) is not { } needs) return null;
        // ohne Hehlerware und ohne Sperre gibt es nichts zu fahren
        float stash = 0f;
        foreach (var st in u.StationsById()) if (BlackMarket.IsAt(u, st)) foreach (var (g, n) in st.BlackStock) stash += n * u.GoodOf(g).BasePrice;
        if (stash < r.SmugglerMinFenceValue && !u.AccessRestricted) return null;
        foreach (var a in u.Actors.Values.OrderBy(a => a.Id.Value))
        {
            if (!LawlessFirm(u, a)) continue;
            if (a.LastSmugglerTick != 0 && u.Tick < a.LastSmugglerTick + r.SmugglerBuyCooldownTicks) continue;
            if (CountOf(u, a.Id) >= r.SmugglersPerFirm) continue;
            // Geld der Firma: Kasse + Schiffskassen über dem Betriebsgeld (wie der Firmensitz es zählt)
            var fleet = u.ShipsById().Where(s => s.Owner == a.Id && s.IsEconomyAi && s.Role is ShipRole.Trader or ShipRole.Miner or ShipRole.Collector).ToList();
            if (fleet.Count == 0) continue;
            float keep = r.FirmShipKeep;
            float liquid = a.Treasury.Credits + fleet.Sum(s => MathF.Max(0f, s.Credits - keep));
            var at = fleet.OrderByDescending(s => s.Credits).ThenBy(s => s.Id.Value).First();
            Station? yard = null; int yardHops = int.MaxValue;
            foreach (var st in u.StationsById())
            {
                if (st.IsBuildSite || !ShipPurchase.SellsModel(u, st, model) || ShipPurchase.Missing(st, needs).Count > 0) continue;
                int h = u.Hops(at.Sector, st.Sector);
                if (h < 0 || h >= yardHops) continue;
                yard = st; yardHops = h;
            }
            if (yard is null) continue;
            float price = ShipPurchase.Price(u, yard, needs, ShipPurchase.AssemblyOf(u, yard));
            float start = r.SmugglerStartCredits;
            if (liquid < price + start + r.SmugglerBuyReserve) continue;
            ShipPurchase.Consume(yard, needs);
            yard.Credits += price;
            float due = price + start;
            float fromTreasury = MathF.Min(due, MathF.Max(0f, a.Treasury.Credits));
            a.Treasury.Credits -= fromTreasury; due -= fromTreasury;
            if (fromTreasury > 0f) u.Events.Publish(new TreasuryUpkeep(a.Id, "smuggler", fromTreasury, 0f));
            foreach (var s in fleet.OrderByDescending(s => s.Credits).ThenBy(s => s.Id.Value))
            {
                if (due <= 0f) break;
                float take = MathF.Min(due, MathF.Max(0f, s.Credits - keep));
                s.Credits -= take; due -= take;
                Profitability.Transfer(s.Profit, -take);   // Investition, kein Geschäftsverlust
            }
            var ship = ShipPurchase.Create(u, u.NewShipId(), model.Key, cls.Slots, model.Fit, ShipRole.Smuggler, Controller.Ai, a.Id, yard, start);
            ship.JobSince = u.Tick;
            ship.IdleSince = u.Tick;
            u.AddShip(ship);
            a.LastSmugglerTick = u.Tick;
            u.Events.Publish(new SmugglerBought(a.Id, ship.Id, yard.Id, price));
            return ship;
        }
        return null;
    }

    // ───────── Tarnung ─────────

    /// <summary>Mit Fracht und abgedockt tarnt sich der Schmuggler (Leistung vorausgesetzt), ohne Fracht schaltet er ab (Leistung für den Sprung).</summary>
    static void UpdateCloak(Universe u, Ship ship)
    {
        if (!Cloak.Fitted(u, ship)) return;
        bool want = HasCargo(ship) && ship.DockedAt.Value == 0 && !JumpDrive.Charging(ship);
        if (want && !ship.Cloaked)
        {
            float surplus = ship.Stats.PowerOutput - ship.Stats.PowerDraw;
            if (surplus + 1e-4f < ship.Stats.CloakDraw) return;
            ship.Cloaked = true;
            ship.RecomputeStats(u);
            u.Events.Publish(new CloakChanged(ship.Id, true, "on"));
        }
        else if (!want && ship.Cloaked) Cloak.Break(u, ship, "off");
    }

    // ───────── Durchsuchung ─────────

    /// <summary>Erwischt: Fracht beschlagnahmt, Strafe (Anteil des Basiswerts, so weit die Schiffskasse reicht) an die Fraktion, Fahndung für den Besitzer.</summary>
    public static void Caught(Universe u, Ship ship, FactionId f)
    {
        float units = 0f, value = 0f;
        foreach (var (g, n) in ship.Cargo) { units += n; value += n * u.GoodOf(g).BasePrice; }
        ship.Cargo.Clear();
        float fine = MathF.Min(MathF.Max(0f, ship.Credits), value * u.Rules.SmugglerFineShare);
        ship.Credits -= fine;
        if (u.Actors.TryGetValue(f, out var fa))
        {
            fa.Treasury.Credits += fine;
            if (fine > 0f) u.Events.Publish(new TreasuryIncome(fa.Id, "smuggling_fine", fine));
        }
        var (sec, _) = SmuggleSearch.Where(u, ship);
        Wanted.Report(u, ship.Owner, f, "smuggling");
        u.Events.Publish(new SmugglerCaught(ship.Id, ship.Owner, f, sec, units, value, fine));
        OrderMove.StopHere(u, ship);
        if (ship.Cloaked) Cloak.Break(u, ship, "off");
        ship.CommandStep = 0;
        ship.IdleSince = u.Tick;
        ship.NextThinkTick = u.Tick + 200;
    }

    // ───────── Fahrten ─────────

    static void Proceed(Universe u, Ship ship)
    {
        if (ship.Order is TradeOrder o)
        {
            if (ship.Phase == TradePhase.GoingToBuy)
            {
                if (!u.Stations.TryGetValue(o.Buy, out var buy)) { Drop(u, ship); return; }
                if (ship.Transit is null && ship.DockedAt == buy.Id) { Load(u, ship, buy, o); return; }
                if (!Go(u, ship, buy.Sector, buy.DockPoint(ship.Id))) Drop(u, ship);
                return;
            }
            if (!u.Stations.TryGetValue(o.Sell, out var sell)) { ship.Order = null; ship.Phase = TradePhase.Idle; return; }
            if (ship.Transit is null && ship.DockedAt == sell.Id) { Unload(u, ship, sell); return; }
            ToSell(u, ship, sell);
            return;
        }
        ship.Order = null;
        if (HasCargo(ship)) { RestSale(u, ship); return; }
        if (!Plan(u, ship)) { if (ship.IdleSince < 0) ship.IdleSince = u.Tick; ship.NextThinkTick = u.Tick + 100; }
    }

    static void Drop(Universe u, Ship ship) { ship.Order = null; ship.Phase = TradePhase.Idle; ship.NextThinkTick = u.Tick + 50; }

    static bool Go(Universe u, Ship ship, SectorId sector, Vec2 pos)
    {
        var plan = Refuel.PrepareTrip(u, ship, sector, pos);
        if (plan == Refuel.TripPlan.FuelStop) return true;
        if (plan == Refuel.TripPlan.Stuck) return false;
        return Travel.Start(u, ship, sector, pos);
    }

    /// <summary>Erreicht das Schiff den Sektor auf dem Torweg (mit seinem Profil)?</summary>
    static bool ByGate(Universe u, Ship ship, SectorId to) => ship.Sector == to || u.IsReachable(ship.Sector, to, GateAccess.ProfileOf(u, ship));

    /// <summary>Zum Abnehmer: auf dem Torweg, sonst zum Absprungpunkt (Sektor in Antriebsreichweite des Ziels) und springen.</summary>
    static void ToSell(Universe u, Ship ship, Station sell)
    {
        if (ByGate(u, ship, sell.Sector)) { if (!Go(u, ship, sell.Sector, sell.DockPoint(ship.Id))) { ship.Order = null; ship.Phase = TradePhase.Idle; } return; }
        // Absprung: im Sektor in Reichweite einen Punkt ohne Massenschatten anfliegen, dann laden
        if (Staging(u, ship, sell.Sector) is not { } stage) { ship.Order = null; ship.Phase = TradePhase.Idle; return; }
        if (ship.Sector == stage.Sector && ship.Transit is null && Vec2.Distance(ship.Position, stage.Pos) <= 2f)
        {
            if (ship.DockedAt.Value != 0) ship.DockedAt = default;
            if (ship.Cloaked) Cloak.Break(u, ship, "off");   // Leistung für das Laden
            if (JumpDrive.Start(u, ship, sell.Sector) is null) return;
            if (JumpDrive.CooldownLeft(u, ship) > 0) { ship.NextThinkTick = u.Tick + JumpDrive.CooldownLeft(u, ship); return; }
            ship.Order = null; ship.Phase = TradePhase.Idle;   // Sprung verweigert (Politik): Rest anderweitig verkaufen
            return;
        }
        if (ship.DockedAt.Value != 0 && ship.Sector == stage.Sector) ship.DockedAt = default;
        if (!Go(u, ship, stage.Sector, stage.Pos)) { ship.Order = null; ship.Phase = TradePhase.Idle; }
    }

    /// <summary>Absprungpunkt zum Ziel: Sektor in Antriebsreichweite, auf dem Torweg erreichbar (wenigste Sprünge vom Schiff, dann Id), Stelle ohne Massenschatten.</summary>
    public static (SectorId Sector, Vec2 Pos)? Staging(Universe u, Ship ship, SectorId target)
    {
        if (!JumpDrive.Fitted(u, ship)) return null;
        int range = ship.Stats.JumpRange;
        var profile = GateAccess.ProfileOf(u, ship);
        (SectorId Sector, int Hops)? best = null;
        foreach (var (s, h) in JumpDrive.Reachable(u, target, range))
        {
            int hops = ship.Sector == s ? 0 : u.CachedRoute(ship.Sector, s, profile)?.Count ?? -1;
            if (hops < 0) continue;
            if (best is null || hops < best.Value.Hops) best = (s, hops);
        }
        if (best is not { } b) return null;
        return (b.Sector, JumpDrive.ArrivalSpot(u, ship, target, b.Sector));
    }

    /// <summary>Wie viele Einheiten der Abnehmer nimmt (Annahme abzüglich dessen, was andere bringen; bezahlbar).</summary>
    static float Takes(Universe u, Ship ship, Station st, GoodId g, float price)
    {
        float n = MathF.Max(0f, st.AcceptableUnits(g, u.Rules) - TraderSystem.LadenTowards(u, ship, st.Id, g));
        return MathF.Min(n, MathF.Max(0f, st.Credits) / MathF.Max(0.01f, price));
    }

    /// <summary>Neue Fahrt planen: Hehlerware (Versteck → Abnehmer) oder Embargo-Ware (Erzeuger → Abnehmer hinter einer Sperre). true = Fahrt begonnen.</summary>
    public static bool Plan(Universe u, Ship ship)
    {
        var r = u.Rules;
        if (!u.Actors.TryGetValue(ship.Owner, out var owner)) return false;
        float credits = ship.Credits;
        if (credits < 50f) return false;
        float cap = ship.CargoCapacity - ship.UsedVolume(u) - ship.MissionCargo;
        if (cap < 1f) return false;
        var profile = GateAccess.ProfileOf(u, ship);
        float upkeep = r.ShipUpkeepPerTick;
        (Station From, Station To, GoodId Good, float Units, float Price, string Kind, bool Jump, float Score)? best = null;

        void Consider(Station from, Station to, GoodId g, float buyPrice, float stockUnits, string kind)
        {
            if (to.Id == from.Id || to.IsBuildSite || !to.Market.TryGetValue(g, out var e) || e.Price <= buyPrice) return;
            if (to.Produces(g) && !to.Consumes(g)) return;
            if (!Docking.Allows(u, to, ship) || BlackMarket.IsAt(u, to)) return;
            bool gate = from.Sector == to.Sector || u.IsReachable(from.Sector, to.Sector, profile);
            bool jump = !gate;
            if (jump && (!JumpDrive.Fitted(u, ship) || Staging(u, ship, to.Sector) is null)) return;
            if (kind == "embargo" && !jump) return;   // Embargo-Ware lohnt nur hinter der Sperre
            float vol = MathF.Max(0.01f, u.GoodOf(g).VolumePerUnit);
            float units = MathF.Floor(MathF.Min(MathF.Min(stockUnits, cap / vol), MathF.Min(credits / buyPrice, Takes(u, ship, to, g, e.Price))));
            if (units < 1f) return;
            float leg1 = Travel.Estimate(u, ship.Sector, ship.Position, from.Sector, from.DockPoint(ship.Id), ship);
            float leg2 = jump ? Travel.Estimate(u, from.Sector, from.DockPoint(ship.Id), from.Sector, default(Vec2), ship) + ship.Stats.JumpChargeTicks + 200f
                : Travel.Estimate(u, from.Sector, from.DockPoint(ship.Id), to.Sector, to.DockPoint(ship.Id), ship);
            if (!float.IsFinite(leg1) || !float.IsFinite(leg2)) return;
            float cost = units * buyPrice;
            float profit = units * (e.Price * (jump ? 1f + r.SmugglerLockBonus : 1f) - buyPrice) - upkeep * (leg1 + leg2);
            if (profit < cost * r.SmugglerMinMargin) return;
            float score = profit / MathF.Max(1f, leg1 + leg2);
            if (best is null || score > best.Value.Score + 1e-4f) best = (from, to, g, units, buyPrice, kind, jump, score);
        }

        // (a) Hehlerware: erreichbarer Hehler in Reichweite, solange die Piratenkasse nicht am Deckel ist
        foreach (var fence in u.StationsById())
        {
            if (!BlackMarket.IsAt(u, fence) || fence.BlackStock.Count == 0 || !Docking.Allows(u, fence, ship)) continue;
            int hops = ship.Sector == fence.Sector ? 0 : u.CachedRoute(ship.Sector, fence.Sector, profile)?.Count ?? -1;
            if (hops < 0 || hops > r.SmugglerMaxJumps) continue;
            if (BlackMarket.Fence(u, fence) is { } pirates && r.PirateTreasuryCap > 0f && pirates.Treasury.Credits >= r.PirateTreasuryCap) continue;
            foreach (var (g, stock) in fence.BlackStock.OrderBy(kv => kv.Key.Value))
            {
                if (stock < 1f || !u.Goods.ContainsKey(g)) continue;
                float price = FenceBuyPrice(u, g);
                float avail = stock;
                if (r.PirateTreasuryCap > 0f && BlackMarket.Fence(u, fence) is { } pc) avail = MathF.Min(avail, MathF.Floor(MathF.Max(0f, r.PirateTreasuryCap - pc.Treasury.Credits) / price));
                if (avail < 1f) continue;
                foreach (var to in StationsBuying(u, g)) Consider(fence, to, g, price, avail, "fence");
            }
        }
        // (b) Embargo-Ware: nur wenn irgendwo eine Sperre gilt oder ein Tor zu ist — Erzeuger auf dem Torweg, Abnehmer nur per Sprung erreichbar
        if (u.AccessRestricted || u.Gates.Values.Any(g => g.State is GateState.Closed or GateState.Destroyed))
        {
            foreach (var from in u.StationsById())
            {
                if (from.IsBuildSite || BlackMarket.IsAt(u, from) || !Docking.Allows(u, from, ship)) continue;
                int hops = ship.Sector == from.Sector ? 0 : u.CachedRoute(ship.Sector, from.Sector, profile)?.Count ?? -1;
                if (hops < 0 || hops > r.SmugglerMaxJumps) continue;
                foreach (var (g, e) in from.Market.OrderBy(kv => kv.Key.Value))
                {
                    if (!from.Produces(g) || e.Amount < 1f || e.Price <= 0f) continue;
                    foreach (var to in StationsBuying(u, g)) Consider(from, to, g, e.Price, MathF.Floor(e.Amount), "embargo");
                }
            }
        }
        if (best is not { } b) return false;
        ship.Order = new TradeOrder(b.From.Id, b.To.Id, b.Good);
        ship.Phase = TradePhase.GoingToBuy;
        ship.IdleSince = -1;
        ship.CommandStep = (int)b.Units;   // geplante Menge (KI-Schmuggler haben keinen Befehl)
        u.Events.Publish(new SmugglerRunStarted(ship.Id, b.From.Id, b.To.Id, b.Good, b.Kind, b.Jump));
        if (ship.Transit is null && ship.DockedAt == b.From.Id) { Load(u, ship, b.From, (TradeOrder)ship.Order); return true; }
        if (!Go(u, ship, b.From.Sector, b.From.DockPoint(ship.Id))) { ship.Order = null; ship.Phase = TradePhase.Idle; ship.IdleSince = u.Tick; return false; }
        return true;
    }

    static IEnumerable<Station> StationsBuying(Universe u, GoodId g)
    {
        foreach (var st in u.StationsById()) if (!st.IsBuildSite && st.Market.ContainsKey(g)) yield return st;
    }

    /// <summary>Einkaufspreis der Schmuggler beim Hehler (Netzrabatt auf den Basispreis).</summary>
    public static float FenceBuyPrice(Universe u, GoodId g) => MathF.Max(0.01f, u.Rules.SmugglerFenceBuyShare * u.GoodOf(g).BasePrice);

    /// <summary>Laden an der Einkaufsstation (Hehler: aus dem Hehlerlager, Geld an die Piratenkasse; sonst normaler Kauf).</summary>
    static void Load(Universe u, Ship ship, Station st, TradeOrder o)
    {
        float want = MathF.Max(1f, ship.CommandStep);
        float units = 0f, cost = 0f;
        if (BlackMarket.IsAt(u, st))
        {
            float price = FenceBuyPrice(u, o.Good);
            float stock = st.BlackStock.GetValueOrDefault(o.Good);
            units = MathF.Floor(MathF.Min(MathF.Min(want, stock), MathF.Min(ship.Credits / price, ship.FreeUnits(u, o.Good))));
            if (u.Rules.PirateTreasuryCap > 0f && BlackMarket.Fence(u, st) is { } p0)
                units = MathF.Min(units, MathF.Floor(MathF.Max(0f, u.Rules.PirateTreasuryCap - p0.Treasury.Credits) / price));
            if (units >= 1f && BlackMarket.Fence(u, st) is { } pirates)
            {
                cost = units * price;
                float left = stock - units;
                if (left <= 0.0001f) st.BlackStock.Remove(o.Good); else st.BlackStock[o.Good] = left;
                ship.Cargo[o.Good] = ship.Cargo.GetValueOrDefault(o.Good) + units;
                ship.Credits -= cost;
                pirates.Treasury.Credits += cost;
                u.Events.Publish(new TreasuryIncome(pirates.Id, BlackMarket.ServiceKey, cost));
            }
            else units = 0f;
        }
        else
        {
            var res = TradeActions.Buy(u, ship, st, o.Good, want);
            units = res.Units; cost = res.Cost;
        }
        if (units < 1f) { Drop(u, ship); ship.IdleSince = u.Tick; ship.NextThinkTick = u.Tick + 200; return; }
        u.Events.Publish(new SmugglerTraded(ship.Id, st.Id, o.Good, units, cost, false));
        ship.Phase = TradePhase.GoingToSell;
        ship.CommandStep = 0;
        if (u.Stations.TryGetValue(o.Sell, out var sell)) ToSell(u, ship, sell);
    }

    /// <summary>Verkaufen am Abnehmer; was er nicht nimmt, bleibt an Bord (Restverkauf beim nächsten Nachdenken).</summary>
    static void Unload(Universe u, Ship ship, Station st)
    {
        foreach (var g in ship.Cargo.Where(kv => kv.Value > 0.001f).Select(kv => kv.Key).OrderBy(g => g.Value).ToList())
        {
            var res = TradeActions.Sell(u, ship, st, g, ship.Cargo[g]);
            if (res.Revenue > 0f) u.Events.Publish(new SmugglerTraded(ship.Id, st.Id, g, res.Sold.GetValueOrDefault(g), res.Revenue, true));
        }
        ship.Order = null;
        ship.Phase = TradePhase.Idle;
        ship.NextThinkTick = u.Tick + 10;
        if (!HasCargo(ship)) ship.IdleSince = u.Tick;
    }

    /// <summary>Rest an Bord: zum nächsten Abnehmer, sonst zum Hehler, sonst über Bord.</summary>
    static void RestSale(Universe u, Ship ship)
    {
        var g = ship.Cargo.Where(kv => kv.Value > 0.001f).Select(kv => kv.Key).OrderBy(x => x.Value).First();
        float units = ship.Cargo[g];
        if (TradeActions.BestBuyerFor(u, ship, g, units, ship.DockedAt) is { } buyer && !BlackMarket.IsAt(u, buyer))
        {
            ship.Order = new TradeOrder(default, buyer.Id, g);
            ship.Phase = TradePhase.GoingToSell;
            ToSell(u, ship, buyer);
            return;
        }
        if (u.Actors.TryGetValue(ship.Owner, out var owner) && BlackMarketNpc.Active(u) && BlackMarketNpc.NearestFence(u, ship, owner) is { } fence)
        {
            if (ship.Transit is null && ship.DockedAt == fence.Id) { BlackMarketNpc.SellAll(u, fence, ship); }
            else if (Go(u, ship, fence.Sector, fence.DockPoint(ship.Id))) { ship.Order = new GoToOrder(fence.Id); ship.NextThinkTick = u.Tick + 1; return; }
        }
        if (HasCargo(ship)) ship.Cargo.Clear();   // niemand nimmt die Ware: über Bord
        ship.IdleSince = u.Tick;
        ship.NextThinkTick = u.Tick + 50;
    }
}
