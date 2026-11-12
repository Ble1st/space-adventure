using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;

namespace SpaceSim.Core.Combat;

/// <summary>
/// Phase 24 (KAMPF.md 4.1): Eintritt in und Austritt aus Gefechten. Der Eintritt sperrt ein Schiff für Handel, Bergbau und Befehle über die bestehenden
/// Ebenen: Ein Befehl ruht (sein Ausführer wird gestoppt wie im Notfall), eine laufende Reise endet an der aktuellen Stelle, der Arbeitsauftrag fällt weg
/// (<see cref="OrderMove.StopHere"/>, keine Geisterreservierung). Beim Austritt geht es weiter: der Befehl startet neu, die KI plant sofort neu (Händler verkaufen
/// die Fracht über <c>SellStranded</c>, Schürfer liefern ihre Ladung aus, Kaperer fliegen heim).
/// </summary>
public static class Engage
{
    /// <summary>Neues Gefecht (noch ohne Teilnehmer).</summary>
    public static Encounter Begin(Universe u, EncounterCause cause, SectorId sector)
    {
        var e = new Encounter { Id = u.Fights.NextId++, Sector = sector, StartTick = u.Tick, Cause = cause };
        u.Fights.Encounters.Add(e);
        return e;
    }

    /// <summary>
    /// Ein Schiff tritt bei: Seite, Taktik, Position auf der Gefechtslinie. Zieht genau zwei Zufallszahlen (Mut, Abwurfanteil), unabhängig von der Rolle.
    /// </summary>
    public static CombatParticipant Join(Universe u, Encounter e, Ship ship, int side, string tactic, float x)
    {
        var r = u.Rules;
        double courage = u.Rng.NextDouble(), drop = u.Rng.NextDouble();
        var p = new CombatParticipant
        {
            Ship = ship.Id, Side = side, Tactic = tactic, X = x, PrevMode = ship.Systems.Mode,
            Courage = r.SurrenderHullMin + (float)courage * MathF.Max(0f, r.SurrenderHullMax - r.SurrenderHullMin),
            DropShare = r.SurrenderDropMin + (float)drop * MathF.Max(0f, r.SurrenderDropMax - r.SurrenderDropMin),
        };
        if (ship.IsPlayer) p.Courage = MathF.Max(p.Courage, r.PlayerMinHull + 0.05f);   // Spielerschiffe haben in Phase 24 einen Hüllenboden
        // Befehlsebene: der Ausführer ruht wie im Notfall (Start beim Austritt)
        if (ship.Command is { } cmd && !ship.Emergency && OrderExecutors.Default.TryGet(cmd.Kind, out var ex))
        {
            ex.Stop(u, ship, cmd);
            ship.CommandStep = 0;
        }
        OrderMove.StopHere(u, ship);   // Reise endet hier, Auftrag frei (auch Lauern/Streife)
        ship.DockedAt = default;
        e.Participants.Add(p);
        u.Fights.Mark(ship.Id, e.Id);
        return p;
    }

    /// <summary>Teilnehmer verlässt das Gefecht (lebend): Index, Leistungsverteilung zurück, Ereignis; das Weiterarbeiten übernimmt <see cref="Resume"/>.</summary>
    public static void Leave(Universe u, Encounter e, CombatParticipant p, CombatExit exit)
    {
        if (!p.Active) return;
        p.Exit = exit;
        u.Fights.Unmark(p.Ship);
        if (u.Ships.TryGetValue(p.Ship, out var ship))
        {
            if (ship.Systems.Mode != p.PrevMode) ShipSystems.SetMode(u, ship, p.PrevMode);
            // liegt das Schiff (noch) am Andockpunkt einer Station, gilt es wieder als angedockt (Join löscht DockedAt für die Dauer des Gefechts)
            if (ship.Transit is null) ship.DockedAt = u.StationAt(ship.Sector, ship.Position);
            u.Events.Publish(new CombatExited(e.Id, p.Ship, ship.Role, exit));
        }
    }

    /// <summary>Teilnehmer wurde zerstört: Abgang über <see cref="Departures"/> (Grund a, Anlass nach dem Gefechtsanlass).</summary>
    public static void Destroy(Universe u, Encounter e, CombatParticipant p, Ship ship)
    {
        p.Exit = CombatExit.Destroyed;
        u.Fights.Unmark(p.Ship);
        u.Events.Publish(new CombatExited(e.Id, p.Ship, ship.Role, CombatExit.Destroyed));
        Departures.RemoveShip(u, ship, DepartureReason.Destroyed, e.Cause == EncounterCause.Raid ? "raid" : "intercept");
    }

    /// <summary>Nach dem Austritt weiterarbeiten (Befehl neu starten, KI sofort neu planen).</summary>
    public static void Resume(Universe u, Ship ship)
    {
        ship.NextThinkTick = u.Tick;
        if (ship.Command is { } cmd)
        {
            if (!ship.Emergency && OrderExecutors.Default.TryGet(cmd.Kind, out var ex)) ex.Start(u, ship, cmd);
            return;
        }
        if (ThreatMap.IsRaider(ship)) { PirateSystem.GoHome(u, ship); return; }
        if (ThreatMap.IsPolice(ship)) return;   // die Streife plant beim nächsten Takt neu (Heimkehr bei Schaden oder leerem Magazin)
        if (ship.IsEconomyAi) ship.IdleSince = u.Tick;
        // Schürfer mit Ladung: zum besten Käufer ausliefern (wie nach einem Tankstopp); Händler verkaufen über SellStranded beim nächsten Takt
        if (ship.Role == ShipRole.Miner && ship.Controller == Controller.Ai && ship.Cargo.Values.Sum() >= 1f
            && TradeActions.BestBuyer(u, ship, ship.Sector, ship.Position) is { } buyer)
        {
            ship.Order = new MineOrder(ship.Sector, -1, buyer.Id);
            ship.MinePhase = MinePhase.GoingToStation;
            var plan = Refuel.PrepareTrip(u, ship, buyer.Sector, buyer.DockPoint(ship.Id));
            if (plan == Refuel.TripPlan.FuelStop) return;
            if (plan == Refuel.TripPlan.Depart && Travel.Start(u, ship, buyer.Sector, buyer.DockPoint(ship.Id))) return;
            ship.Order = null;
            ship.MinePhase = MinePhase.Idle;
        }
    }

    // ───────── Anlässe ─────────

    /// <summary>
    /// Überfall als Gefecht (ersetzt die Sofortauflösung für NPC-Opfer): Kaperer auf Seite 0 bei X = 0, Opfer auf Seite 1 im Anfangsabstand, dazu Eskorten und
    /// Wachen des Opfers und — mit derselben Chance wie bisher — eine Streife der Sektorfraktion etwas weiter hinten. Meldet Piraterie (Fahndung).
    /// </summary>
    public static Encounter StartRaid(Universe u, Ship raider, Ship victim, Sector sector)
    {
        var r = u.Rules;
        var e = Begin(u, EncounterCause.Raid, sector.Id);
        e.Raider = raider.Id;
        e.RaiderOwner = raider.Owner;
        e.Victim = victim.Id;
        Join(u, e, raider, 0, CombatTactics.Raider, 0f);
        Join(u, e, victim, 1, CombatTactics.Trader, r.CombatStartDistance);
        foreach (var esc in FleetGuards.Escorts(u, victim, sector.Id))
            if (!u.Fights.IsEngaged(esc.Id)) Join(u, e, esc, 1, CombatTactics.Escort, r.CombatStartDistance);
        var police = PirateSystem.PoliceIn(u, sector.Id);
        if (police.Count > 0 && u.Rng.NextDouble() < MathF.Min(r.PoliceResponseMax, r.PoliceResponseChance * police.Count))
        {
            e.Police = police[0].Id;
            Join(u, e, police[0], 1, CombatTactics.Police, r.CombatStartDistance + r.CombatHelperDistance);
        }
        Wanted.Report(u, raider.Owner, sector.Owner, "piracy");
        u.Events.Publish(new EncounterStarted(e.Id, sector.Id, EncounterCause.Raid, raider.Id, victim.Id,
            $"Gefecht in {sector.Name}: {u.ActorName(raider.Owner)} überfällt Schiff {victim.Id.Value}"));
        return e;
    }

    /// <summary>Abfangen als Gefecht: die Streife (oder ein Jäger der Spielerflotte) stellt den lauernden Kaperer.</summary>
    public static Encounter StartIntercept(Universe u, Ship hunter, Ship raider)
    {
        var r = u.Rules;
        var e = Begin(u, EncounterCause.Intercept, raider.Sector);
        e.Raider = raider.Id;
        e.RaiderOwner = raider.Owner;
        e.Police = hunter.Id;
        Join(u, e, hunter, 0, ThreatMap.IsPolice(hunter) ? CombatTactics.Police : CombatTactics.Escort, 0f);
        Join(u, e, raider, 1, CombatTactics.Raider, r.CombatStartDistance);
        string where = u.Sectors.TryGetValue(raider.Sector, out var sec) ? sec.Name : "?";
        u.Events.Publish(new EncounterStarted(e.Id, raider.Sector, EncounterCause.Intercept, hunter.Id, raider.Id, $"Gefecht in {where}: Streife stellt ein Kaperschiff"));
        return e;
    }
}
