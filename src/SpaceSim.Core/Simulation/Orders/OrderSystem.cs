using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Phase 12 (FLOTTEN.md Abschnitt 4 „Ebenen“): führt die Befehle aus. Ein Schiff folgt immer der höchsten dringenden Ebene:
/// 1) <b>Notfall</b> (Hülle unter <see cref="EconomyRules.EmergencyHullFraction"/> oder Tank leer) unterbricht den Befehl: das Schiff fliegt zur
/// nächsten Station mit Reparatur (sonst zur Heimatbasis), tankt und repariert, dann geht der Befehl weiter;
/// 2) <b>Befehl</b> (<see cref="Ship.Command"/>): der registrierte <see cref="IOrderExecutor"/> führt ihn aus, bis er erledigt ist oder ersetzt wird;
/// 3) <b>Automatik</b>: ohne Befehl arbeiten KI-Schiffe wie bisher über ihre Aufgaben-Systeme; Spielerschiffe halten die Position.
/// Läuft vor den Aufgaben-Systemen, damit ein neuer Arbeitsbefehl im selben Tick ausgeführt wird. Schiffe ohne Befehl kosten nichts.
/// </summary>
public sealed class OrderSystem : ISimSystem
{
    readonly OrderExecutors _executors;
    public OrderSystem(OrderExecutors? executors = null) => _executors = executors ?? OrderExecutors.Default;

    public void Step(Universe u, int dt)
    {
        List<Ship>? commanded = null;
        foreach (var s in u.Ships.Values) if (s.Command is not null) (commanded ??= new List<Ship>()).Add(s);
        if (commanded is null) return;
        foreach (var ship in commanded.OrderBy(s => s.Id.Value))
        {
            if (!u.Ships.ContainsKey(ship.Id) || ship.Command is not { } cmd) continue;
            if (ship.Id == u.PlayerShip) { Commands.Cancel(u, ship, "du steuerst selbst", _executors); continue; }
            if (!_executors.TryGet(cmd.Kind, out var ex)) { ship.Command = null; continue; }
            // Phase 24: Gefecht steht über dem Notfall — der Befehl ruht (Ausführer beim Eintritt gestoppt, beim Austritt neu gestartet)
            if (u.Fights.IsEngaged(ship.Id)) continue;

            // Ebene 1: Notfall
            if (!ship.Emergency && cmd is not ReturnCommand && EmergencyReason(u, ship) is { } reason)
            {
                ex.Stop(u, ship, cmd);
                ship.Emergency = true;
                ship.CommandStep = 0;
                ship.NextThinkTick = u.Tick;
                u.Events.Publish(new EmergencyStarted(ship.Id, reason));
            }
            if (ship.Emergency)
            {
                if (StepEmergency(u, ship))
                {
                    ship.Emergency = false;
                    ship.CommandStep = 0;
                    ex.Start(u, ship, cmd);
                    u.Events.Publish(new EmergencyEnded(ship.Id));
                }
                continue;
            }

            // Ebene 2: Befehl
            var status = ex.Step(u, ship, cmd, dt);
            if (status == CommandStatus.Running) continue;
            string text = ex.Describe(u, ship, cmd);
            if (status == CommandStatus.Failed) ex.Stop(u, ship, cmd);
            ship.Command = null;
            ship.CommandStep = 0;
            if (ship.IsPlayer && ship.Role != ShipRole.Player && ship.Transit is null) ship.Role = ShipRole.Player;
            u.Events.Publish(new CommandEnded(ship.Id, cmd.Kind, status == CommandStatus.Done ? "done" : "failed", text));
        }
    }

    /// <summary>Grund für die Notfall-Ebene (null = keiner): Hülle kritisch oder Tank leer (Schiffe mit Tank).</summary>
    public static string? EmergencyReason(Universe u, Ship ship)
    {
        if (ship.Hull < u.Rules.EmergencyHullFraction * ship.MaxHull) return "Hülle kritisch";
        if (u.Rules.DamageActive && NpcRepair.Crippled(u, ship)) return "Modulschaden";   // Phase 26: ausgefallenes Modul oder Brand
        if (u.Rules.FuelActive && ship.Stats.FuelCapacity > 0f && ship.TotalFuel < 0.5f && ship.Transit is null) return "Tank leer";   // Phase 14: nur mit Treibstoff
        return null;
    }

    /// <summary>Notfall: zur nächsten Station mit Reparatur (sonst Heimatbasis), dort versorgen. true = erledigt (oder nicht lösbar).</summary>
    static bool StepEmergency(Universe u, Ship ship)
    {
        // Phase 27: Polizei und Piraten (Kasse des Besitzers) versorgen sich an ihrer Heimatbasis, nicht an der nächsten Reparaturstation
        var target = u.Rules.FleetCombat && UnitSupport.IsServiceUnit(ship) && ReturnExecutor.Home(u, ship) is { } home ? home
            : ReturnExecutor.NearestWith(u, ship, Services.RepairService.ServiceKey) ?? ReturnExecutor.Home(u, ship);
        if (target is null) return true;   // nirgends Hilfe erreichbar: Befehl weiter (die Hülle regeneriert langsam angedockt)
        var s = OrderMove.MoveTo(u, ship, target.Sector, target.DockPoint(ship.Id), target.Id, out bool there);
        if (there)
        {
            ReturnExecutor.Service(u, ship, target);
            return true;
        }
        return s == CommandStatus.Failed;
    }
}

/// <summary>
/// Phase 12: Liegegebühr (UI.md Abschnitt 3 „Taxi“, SCHIFFSSLOTS.md Dienst „hangar“): Nicht aktive Spielerschiffe, die an einer fremden Station
/// angedockt liegen, zahlen je Tag den Satz ihrer Klasse (<see cref="ShipClass.BerthFee"/>, sonst nach Größe) an die Station — aus der Kasse des
/// Besitzers. An eigenen Stationen gratis. Was die Kasse nicht decken kann, bleibt offen (gemeldet, keine Schulden). Dazu der Unterhalt der Flotte
/// (FLOTTEN.md Abschnitt 3a Punkt 4, „Unterhalt für jedes Schiff, auch im Verband“): Spielerschiffe mit Befehl zahlen
/// <see cref="EconomyRules.ShipUpkeepPerTick"/> wie KI-Schiffe (Geldsenke, <see cref="FleetUpkeepPaid"/>); das aktive Schiff und Leihschiffe nicht.
/// </summary>
public sealed class HangarSystem : ISimSystem
{
    /// <summary>
    /// Phase 26 (Prüfung Phase 25): Leihmiete läuft täglich — je angefangenem Miettag (<see cref="Hangar.RentalDays"/>), der noch nicht abgerechnet ist, zahlt die
    /// Kasse des Besitzers <see cref="EconomyRules.RentalPerDay"/> an die Verleihstation, notfalls ins Minus (Schulden, sichtbar in HUD und „Meine Fraktion“).
    /// Rückgabe und Verlust rechnen nur noch die offenen Tage ab. Ereignis <see cref="RentCharged"/>.
    /// </summary>
    public static void ChargeRent(Universe u)
    {
        foreach (var ship in u.ShipsById())
        {
            if (!ship.IsPlayer || !ship.IsRental) continue;
            int due = Hangar.RentalDays(u, ship);
            while (ship.RentPaidDays < due)
            {
                ship.RentPaidDays++;
                var wallet = u.WalletOf(ship);
                float rent = u.Rules.RentalPerDay;
                float debt = MathF.Max(0f, rent - MathF.Max(0f, wallet.Credits));
                wallet.Credits -= rent;
                if (u.Stations.TryGetValue(ship.RentedFrom, out var st)) st.Credits += rent;
                u.Events.Publish(new RentCharged(ship.Id, ship.RentedFrom, ship.RentPaidDays, rent, debt));
            }
        }
    }

    public void Step(Universe u, int dt)
    {
        if (u.Rules.DayTicks <= 0) return;
        ChargeRent(u);
        foreach (var ship in u.Ships.Values.Where(s => Hangar.PaysBerthFee(u, s)).OrderBy(s => s.Id.Value).ToList())   // Runde 36: eine Regel für Anzeige und Buchung
        {
            var st = u.Stations[ship.DockedAt];
            float fee = Hangar.BerthFeePerDay(u, ship) * dt / u.Rules.DayTicks;
            if (fee <= 0f) continue;
            var wallet = u.WalletOf(ship);
            float paid = MathF.Min(fee, MathF.Max(0f, wallet.Credits));
            wallet.Credits -= paid;
            st.Credits += paid;
            u.Events.Publish(new BerthFeePaid(ship.Owner, ship.Id, st.Id, paid, fee - paid));
        }
        float total = 0f;
        ActorId owner = default;
        foreach (var ship in u.ShipsById().Where(s => s.IsPlayer && s.Id != u.PlayerShip && s.Command is not null && !s.IsRental).ToList())
        {
            var wallet = u.WalletOf(ship);
            float due = MathF.Min(ShipPricing.FleetUpkeepPerTick(u, ship) * dt, MathF.Max(0f, wallet.Credits));   // Ausgleich (Punkt 6): nach Klassengröße
            wallet.Credits -= due;
            total += due;
            owner = ship.Owner;
        }
        if (total > 0f) u.Events.Publish(new FleetUpkeepPaid(owner, total));
    }
}
