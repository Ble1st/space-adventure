using SpaceSim.Core.Events;
using SpaceSim.Core.Mining;
using SpaceSim.Core.Model;
using SpaceSim.Core.Trading;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Miner-KI: wählt Rohstofffeld + Abnehmerstation mit dem besten Wert pro Zeit, fliegt abstrakt zum Feld,
/// baut dort mit denselben Laserschüssen ab wie der Spieler (Nachladezeit, Trefferchance der Asteroidenklasse, zufällige
/// Brockengröße und Erzsorte, Erz stammt aus dem Asteroiden), liefert aus und verkauft, was die Station führt. Bleibt Fracht übrig,
/// fährt der Miner die nächste Station an, die sie kauft. Baut nicht ab, wenn die Mischung kaum etwas wert ist.
/// </summary>
public sealed class MinerSystem : ISimSystem
{
    /// <summary>Mindestwert der Mischung an der Zielstation relativ zu ihrem Basiswert, ab dem sich Abbau lohnt.</summary>
    public const float MinPriceFactor = 0.5f;
    public const int ThinkInterval = 10;
    /// <summary>Abschlag je weiterem Miner am selben Feld.</summary>
    public const float CrowdPenalty = 0.6f;

    public void Step(Universe u, int dt)
    {
        foreach (var ship in u.Ships.Values)
        {
            // Aufgabenfilter bleibt die Rolle, zusätzlich muss das Schiff schürfen können; fehlt das Werkzeug, wird das Schiff freigegeben
            if (ship.Role != ShipRole.Miner) continue;
            if (!ship.Has(Capability.Mining)) { JobRelease.Release(u, ship); continue; }

            if (ship.Transit is { } t)
            {
                if (u.Tick >= t.ArrivalTick) Arrive(u, ship, t);
                continue;
            }
            switch (ship.MinePhase)
            {
                case MinePhase.Idle:
                    if (u.Tick < ship.NextThinkTick) break;
                    if (!PickOrder(u, ship))
                    {
                        if (ship.IdleSince < 0) ship.IdleSince = u.Tick;
                        ship.NextThinkTick = u.Tick + TraderSystem.IdleBackoff(u, ship) + ship.Id.Value % 7;
                    }
                    break;
                case MinePhase.Mining: Mine(u, ship); break;
            }
        }
    }

    /// <summary>Erwarteter Erlös einer Einheit der Mischung dieser Klasse an dieser Station (Preise der Waren, die sie kauft).</summary>
    static float MixValue(Universe u, AsteroidClass cls, Station st)
    {
        float total = cls.Yields.Sum(y => y.Weight);
        if (total <= 0f) return 0f;
        float value = 0f;
        foreach (var y in cls.Yields)
            if (st.Market.TryGetValue(y.Good, out var e) && st.AcceptableUnits(y.Good, u.Rules) >= 5f) value += y.Weight / total * e.Price;
        return value;
    }

    /// <summary>
    /// Bester Abbauauftrag für dieses Schiff jetzt (Phase 6 auch für die Aufgabenwahl, <see cref="MineJob"/>): Auftrag, Arbeitspunkt am Feld
    /// und Bewertung = Wert einer Einheit der Mischung je Tick Umlaufzeit, gemindert durch Gedränge am Feld. Reine Auswertung.
    /// Die Abbaurate berücksichtigt die Laserstufe des Schiffs (bei Stufe 1 wie bisher).
    /// </summary>
    public static (MineOrder? Order, Vec2 FieldPos, float Score) BestMine(Universe u, Ship ship)
    {
        MineOrder? best = null;
        Vec2 bestFieldPos = default;
        float bestScore = 0f;
        var profile = GateAccess.ProfileOf(u, ship);   // Phase 10: Gate-Zugang des Schiffs
        var laden = profile with { Laden = true };     // ausgeliefert wird beladen (Embargo)
        foreach (var sector in u.Sectors.Values)
        {
            if (!u.IsReachable(ship.Sector, sector.Id, profile)) continue;
            for (int fi = 0; fi < sector.Fields.Count; fi++)
            {
                var field = sector.Fields[fi];
                if (!u.AsteroidClasses.TryGetValue(field.ClassKey, out var cls) || Belt.OreIn(sector, field) < 1f) continue;

                // Phase 6: Laserstufe des Schiffs (Trefferchance und Brockengröße); Stufe 1 = bisherige Rate
                float chance = MathF.Min(1f, cls.ChunkChance * ship.Stats.LaserChunkChanceMult);
                float orePerTick = chance * MiningRules.MeanChunkUnits * ship.Stats.LaserChunkSizeMult / MiningRules.ShotInterval * SimClock.TickInterval;
                float mineTicks = ship.CapacityVolumeFor(GoodForm.Solid) / MathF.Max(0.01f, orePerTick);   // Erz ist fest
                float basis = cls.MeanBasePrice(u);
                // Gedränge am Feld mindert den Wert: Jeder weitere Miner teilt sich dieselben Asteroiden und drückt den Preis (Herdenschutz)
                int crowd = 0;
                foreach (var other in u.Ships.Values)
                    if (other != ship && other.Order is MineOrder mo && mo.Sector == sector.Id && mo.Field == fi) crowd++;
                float crowding = 1f / (1f + CrowdPenalty * crowd);
                foreach (var st in u.Stations.Values)
                {
                    float value = MixValue(u, cls, st);
                    if (value < basis * MinPriceFactor) continue;
                    if (!TraderSystem.CanAfford(st, value, ship.CapacityVolumeFor(GoodForm.Solid))) continue;
                    if (!u.IsReachable(sector.Id, st.Sector, laden)) continue;
                    float time = Travel.Estimate(u, ship.Sector, ship.Position, sector.Id, field.WorkPoint(ship.Id), ship) + mineTicks
                               + Travel.Estimate(u, sector.Id, field.WorkPoint(ship.Id), st.Sector, st.DockPoint(ship.Id), ship, laden);
                    float score = value / time * crowding;
                    if (score > bestScore) { bestScore = score; bestFieldPos = field.WorkPoint(ship.Id); best = new MineOrder(sector.Id, fi, st.Id); }
                }
            }
        }
        return (best, bestFieldPos, bestScore);
    }

    static bool PickOrder(Universe u, Ship ship)
    {
        var (best, bestFieldPos, _) = BestMine(u, ship);
        if (best is null) return false;
        long idleBefore = ship.IdleSince;   // ein Liegenbleiber (Stuck) behält seinen Leerlauf-Beginn, sonst wird er nie aufgegeben
        ship.IdleSince = -1;
        ship.Order = best;
        ship.MinePhase = MinePhase.GoingToField;
        // Phase 4b: Treibstoff für die Fahrt zum Feld sichern (Tankstopp unterwegs oder Notkauf)
        var plan = Refuel.PrepareTrip(u, ship, best.Sector, bestFieldPos);
        if (plan == Refuel.TripPlan.FuelStop) return true;   // Order ist jetzt GoToOrder zur Tankstelle, Transit läuft
        if (plan == Refuel.TripPlan.Stuck)
        {
            // pleite: nicht reisen, liegen bleiben (FleetSystem gibt das Schiff nach Leerlauf auf)
            ship.Order = null; ship.MinePhase = MinePhase.Idle; ship.IdleSince = idleBefore >= 0 ? idleBefore : u.Tick;
            return false;
        }
        Travel.Start(u, ship, best.Sector, bestFieldPos);
        return true;
    }

    static void Arrive(Universe u, Ship ship, Transit t)
    {
        u.FinishTransit(ship);
        if (ship.Order is GoToOrder g && u.Stations.ContainsKey(g.Target))
        {
            // Tankstopp (Phase 4b): bei Ankunft volltanken; eine laufende Auslieferung wird danach an den besten Käufer neu gehangen
            var st = u.Stations[g.Target];
            Refuel.TankUp(u, ship, st);
            ship.Order = null;
            if (ship.MinePhase == MinePhase.GoingToStation && ship.Cargo.Values.Sum() > 0.001f)
            {
                var next = TradeActions.BestBuyer(u, ship, ship.Sector, ship.Position);
                if (next is not null)
                {
                    ship.Order = new MineOrder(ship.Sector, -1, next.Id);   // Feld -1: nur noch ausliefern
                    var plan = Refuel.PrepareTrip(u, ship, next.Sector, next.DockPoint(ship.Id));
                    if (plan == Refuel.TripPlan.FuelStop) return;
                    if (plan == Refuel.TripPlan.Depart)
                    {
                        Travel.Start(u, ship, next.Sector, next.DockPoint(ship.Id));
                        return;
                    }
                }
            }
            ship.Order = null;
            ship.MinePhase = MinePhase.Idle;
            ship.IdleSince = u.Tick;
            return;
        }
        if (ship.Order is not MineOrder order || !u.Stations.ContainsKey(order.DeliverTo))
        {
            ship.Order = null; ship.MinePhase = MinePhase.Idle; ship.Cargo.Clear();
            return;
        }

        if (ship.MinePhase == MinePhase.GoingToField)
        {
            ship.MinePhase = MinePhase.Mining;
            ship.ShotTimer = 0f; ship.MineAsteroid = 0;
        }
        else if (ship.MinePhase == MinePhase.GoingToStation)
        {
            var st = u.Stations[order.DeliverTo];
            TradeActions.SellAll(u, ship, st);   // veröffentlicht je Ware ein MineDelivered

            if (ship.Cargo.Values.Sum() > 0.001f)
            {
                // Rest, den diese Station nicht kauft oder nicht bezahlen kann: nächste Station, die ihn nimmt
                var next = TradeActions.BestBuyer(u, ship, ship.Sector, ship.Position, exclude: st.Id);
                if (next != null)
                {
                    ship.Order = order with { DeliverTo = next.Id };
                    // Phase 4b: Treibstoff für die Fahrt zur nächsten Station sichern
                    var plan = Refuel.PrepareTrip(u, ship, next.Sector, next.DockPoint(ship.Id));
                    if (plan == Refuel.TripPlan.FuelStop) return;
                    if (plan == Refuel.TripPlan.Stuck)
                    {
                        ship.Order = null; ship.MinePhase = MinePhase.Idle; ship.IdleSince = u.Tick;
                        return;
                    }
                    Travel.Start(u, ship, next.Sector, next.DockPoint(ship.Id));
                    return;
                }
                ship.Cargo.Clear();   // niemand will es: über Bord
            }
            ship.Order = null;
            ship.MinePhase = MinePhase.Idle;
            ship.IdleSince = u.Tick;
        }
    }

    static void Mine(Universe u, Ship ship)
    {
        if (ship.Order is not MineOrder order) { ship.MinePhase = MinePhase.Idle; return; }

        var sector = u.Sectors[ship.Sector];
        ResourceField? field = order.Field >= 0 && order.Field < sector.Fields.Count ? sector.Fields[order.Field] : null;
        bool cargoFull = ship.FreeVolumeForForm(u, GoodForm.Solid) < MiningRules.MinChunkUnits;   // Erz ist fest

        if (field is null || cargoFull || Belt.OreIn(sector, field) < 0.01f)
        {
            if (ship.Cargo.Values.Sum() > 0f)
            {
                ship.MinePhase = MinePhase.GoingToStation;
                // Phase 4b: Treibstoff für die Fahrt zur Abnehmerstation sichern
                var plan = Refuel.PrepareTrip(u, ship, u.Stations[order.DeliverTo].Sector, u.Stations[order.DeliverTo].DockPoint(ship.Id));
                if (plan == Refuel.TripPlan.FuelStop) return;
                if (plan == Refuel.TripPlan.Stuck)
                {
                    ship.Order = null; ship.MinePhase = MinePhase.Idle; ship.IdleSince = u.Tick;
                    return;
                }
                Travel.Start(u, ship, u.Stations[order.DeliverTo].Sector, u.Stations[order.DeliverTo].DockPoint(ship.Id));
            }
            else { ship.Order = null; ship.MinePhase = MinePhase.Idle; }
            return;
        }

        // Jeder Tick lädt die Waffe nach; ist sie bereit, fällt genau ein Schuss (nie mehrere auf einmal)
        ship.ShotTimer = MathF.Min(ship.ShotTimer + SimClock.TickInterval, MiningRules.ShotInterval + SimClock.TickInterval);
        if (ship.ShotTimer < MiningRules.ShotInterval) return;
        var rock = PickRock(sector, field, ship);
        if (rock is null || !u.AsteroidClasses.TryGetValue(rock.ClassKey, out var cls)) return;

        ship.ShotTimer -= MiningRules.ShotInterval;
        // Phase 8: Brecher (Spaltlaser) zerlegen große Brocken zuerst, damit mehrere Miner parallel arbeiten können (gleiche Regel wie der Spieler)
        if (ship.Stats.SplitPower > 0f && rock.Radius >= u.Rules.NpcSplitMinRadius && AsteroidSplitter.Split(u, sector, rock, ship.Id) is not null)
        {
            ship.MineAsteroid = 0;
            return;
        }
        var shot = LaserShot.Roll(u.Rng, cls, rock, ship.Stats.LaserChunkChanceMult, ship.Stats.LaserChunkSizeMult);
        if (shot.Hit)
        {
            Belt.Remove(u, sector, rock, shot.Units);
            ship.Cargo[shot.Good] = ship.Cargo.GetValueOrDefault(shot.Good) + MathF.Min(shot.Units, ship.FreeUnits(u, shot.Good));   // Traktorstrahl lädt ein
        }
        u.Events.Publish(new MinerShot(sector.Id, ship.Id, rock.Id, rock.Position, rock.Radius, shot.Units, shot.Good));
    }

    /// <summary>Asteroid im Gürtel des Feldes, auf den der Miner feuert: bleibt beim selben, bis er leer ist; sonst der nächste in Laserreichweite.</summary>
    static Asteroid? PickRock(Sector sector, ResourceField field, Ship ship)
    {
        if (ship.MineAsteroid != 0)
        {
            var current = sector.Asteroids.FirstOrDefault(a => a.Id == ship.MineAsteroid);
            if (current is { Minable: true }) return current;
        }
        var rocks = Belt.AsteroidsOf(sector, field).OrderBy(a => Vec2.Distance(a.Position, ship.Position) - a.Radius).ThenBy(a => a.Id).ToList();
        var pick = rocks.FirstOrDefault(a => Vec2.Distance(a.Position, ship.Position) - a.Radius <= MiningRules.LaserRange) ?? rocks.FirstOrDefault();
        ship.MineAsteroid = pick?.Id ?? 0;
        return pick;
    }
}
