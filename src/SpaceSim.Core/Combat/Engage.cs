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
        e.Spatial = CombatSpace.WantsSpace(u, e);   // Phase 25: im Spielersektor räumlich
        u.Fights.Encounters.Add(e);
        return e;
    }

    /// <summary>
    /// Ein Schiff tritt bei: Seite, Taktik, Position auf der Gefechtslinie. Zieht genau zwei Zufallszahlen (Mut, Abwurfanteil), unabhängig von der Rolle — Runde 7: außer beim Wiedereintritt (alter Eintrag, kein Zug; <see cref="EconomyRules.CombatRejoinReuse"/>).
    /// </summary>
    public static CombatParticipant Join(Universe u, Encounter e, Ship ship, int side, string tactic, float x, Vec2? pos = null)
    {
        var r = u.Rules;
        CombatParticipant p;
        bool reuse = r.CombatRejoinReuse && e.Of(ship.Id) is { Active: false };
        if (reuse)
        {
            // Runde 7: Wiedereintritt — derselbe Eintrag (Mut, Abwurfanteil, Nachladeuhren bleiben; kein Zufallszug), neu: Seite, Taktik, Ort, Ziel, Zuflucht
            p = e.Of(ship.Id)!;
            p.Exit = CombatExit.None; p.Side = side; p.Tactic = tactic; p.X = x; p.PrevMode = ship.Systems.Mode; p.Owner = ship.Owner;
            p.Target = default; p.TargetSiege = default; p.Move = CombatMove.Approach; p.Speed = 0f;
            p.RefugeId = 0; p.Refuge = 0f; p.RefugePos = default; p.WasDockedAt = default; p.Dir = new Vec2(0f, -1f);
        }
        else
        {
            double courage = u.Rng.NextDouble(), drop = u.Rng.NextDouble();
            p = new CombatParticipant
            {
                Ship = ship.Id, Side = side, Tactic = tactic, X = x, PrevMode = ship.Systems.Mode, Owner = ship.Owner,
                Courage = r.SurrenderHullMin + (float)courage * MathF.Max(0f, r.SurrenderHullMax - r.SurrenderHullMin),
                DropShare = r.SurrenderDropMin + (float)drop * MathF.Max(0f, r.SurrenderDropMax - r.SurrenderDropMin),
            };
        }
        // Befehlsebene: der Ausführer ruht wie im Notfall (Start beim Austritt)
        if (ship.Command is { } cmd && !ship.Emergency && OrderExecutors.Default.TryGet(cmd.Kind, out var ex))
        {
            ex.Stop(u, ship, cmd);
            ship.CommandStep = 0;
        }
        OrderMove.StopHere(u, ship);   // Reise endet hier, Auftrag frei (auch Lauern/Streife)
        p.CastOffUntil = Mining.MiningAnchor.OnCombat(u, ship);   // Phase 32: ein verankerter Schürfer kappt erst die Trossen
        Flight.Cloak.Break(u, ship, "combat");   // Tarnfeld bricht im Gefecht
        Flight.JumpDrive.Abort(u, ship, "combat", "Gefecht");   // Sprungantrieb: Laden bricht ab
        // Phase 25: nur ein Liegeplatz, an dem das Schiff vor dem Gefecht wirklich lag, wird beim Austritt wiederhergestellt (nie für das aktive Schiff,
        // dessen Andocken die Ansicht führt)
        if (ship.Id != u.PlayerShip) p.WasDockedAt = ship.DockedAt;
        ship.DockedAt = default;
        // Phase 25: im räumlichen Gefecht echte Position (Vorgabe: wo das Schiff steht) und Blick zum Gegner
        p.InSpace = e.Spatial;
        p.Pos = p.PrevPos = pos ?? ship.Position;
        if (e.Spatial && ship.Id != u.PlayerShip) ship.Position = p.Pos;
        if (e.Spatial && CombatSense.NearestInSpace(e, p) is { } near && Vec2.Distance(near.Pos, p.Pos) > 1e-3f) p.Dir = (near.Pos - p.Pos).Normalized();
        if (!reuse) e.Participants.Add(p);
        u.Fights.Mark(ship.Id, e.Id);
        CombatRefuge.Assign(u, e, p, ship);   // Phase 27: nächste Zuflucht (Station/Tor) für die Flucht merken
        return p;
    }

    /// <summary>
    /// Gerätetest Phase 28 (Runde 6): das aktive Spielerschiff tritt einem Gefecht wieder bei, das es verlassen hat — derselbe Eintrag wird wieder aktiv (Position,
    /// Index), statt einen zweiten anzulegen: sonst fand <see cref="Encounter.Of"/> den alten, abgelaufenen Eintrag (Nachladeuhr 0 → Dauerfeuer jedes Bild; kein Ziel für
    /// Gegner und Geschütze). Kein Zufallszug.
    /// </summary>
    public static CombatParticipant Rejoin(Universe u, Encounter e, CombatParticipant p, Ship ship)
    {
        p.Exit = CombatExit.None;
        p.InSpace = e.Spatial;
        p.Pos = p.PrevPos = ship.Position;
        ship.DockedAt = default;
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
            // die Leistungsverteilung zurück (NPC-Taktik hatte sie verstellt); das aktive Spielerschiff behält, was die Person gewählt hat
            if (ship.Systems.Mode != p.PrevMode && ship.Id != u.PlayerShip) ShipSystems.SetMode(u, ship, p.PrevMode);
            // Phase 25 (Korrektur): räumlich bewegte NPCs stehen nach dem Gefecht nicht in einem Hindernis
            if (p.InSpace && ship.Id != u.PlayerShip && ship.Transit is null) ship.Position = CombatSpace.Clear(u, ship.Sector, ship.Position);
            // Phase 25 (Prüfung Phase 24): nur der Liegeplatz von vor dem Gefecht wird wiederhergestellt — und nur, wenn das Schiff noch an dessen
            // Andockpunkt liegt; das aktive Spielerschiff führt die Ansicht (Join merkt sich für es keinen Liegeplatz), Liegegebühren folgen DockedAt
            if (ship.Transit is null && p.WasDockedAt.Value != 0 && ship.Id != u.PlayerShip && u.StationAt(ship.Sector, ship.Position) == p.WasDockedAt)
                ship.DockedAt = p.WasDockedAt;
            u.Events.Publish(new CombatExited(e.Id, p.Ship, ship.Role, exit));
        }
    }

    /// <summary>Teilnehmer wurde zerstört: Abgang über <see cref="Departures"/> (Grund a, Anlass nach dem Gefechtsanlass).</summary>
    /// <param name="by">Gerätetest Phase 28 (Runde 5): wer den letzten Treffer setzte („Polizei“, „Stationsgeschütze (Sektor-HQ Beta)“); null = nach dem Anlass des Gefechts.</param>
    public static void Destroy(Universe u, Encounter e, CombatParticipant p, Ship ship, string? by = null)
    {
        p.Exit = CombatExit.Destroyed;
        u.Fights.Unmark(p.Ship);
        u.Events.Publish(new CombatExited(e.Id, p.Ship, ship.Role, CombatExit.Destroyed));
        Wars.OnShipLost(u, e, p, ship);   // Ausgleich (Punkt 7): Kriegsverlust für die Tributforderung
        string detail = e.Cause switch { EncounterCause.Raid => "raid", EncounterCause.Intercept => "intercept", EncounterCause.War => "war", _ => "assault" };
        // Phase 25: das aktive Spielerschiff geht wirklich verloren — vorher landet die Person an einer Station (ShipLoss), dann der Abgang
        if (ship.Id == u.PlayerShip)
        {
            string cause = by ?? e.Cause switch
            {
                EncounterCause.Raid => "Piraten",
                EncounterCause.Intercept or EncounterCause.Arrest => "Polizei",
                EncounterCause.War => "Kriegsschiff",
                EncounterCause.Siege => "Stationsgeschütze",
                _ => "Gefecht",
            };
            ShipLoss.LoseActive(u, ship, detail, cause);
            return;
        }
        Departures.RemoveShip(u, ship, DepartureReason.Destroyed, detail);
    }

    /// <summary>Nach dem Austritt weiterarbeiten (Befehl neu starten, KI sofort neu planen).</summary>
    public static void Resume(Universe u, Ship ship)
    {
        ship.NextThinkTick = u.Tick;
        if (u.Rules.FleetCombat && ThreatMap.IsRaider(ship)) { PirateCommands.AfterFight(u, ship); return; }   // Phase 27: nach dem Gefecht entscheidet die Gruppe
        if (ship.Command is { } cmd)
        {
            if (!ship.Emergency && OrderExecutors.Default.TryGet(cmd.Kind, out var ex)) ex.Start(u, ship, cmd);
            return;
        }
        if (ThreatMap.IsRaider(ship))
        {
            // Phase 27: mit Flottenkampf entscheidet die Piratengruppe (Rudel heim bzw. dem Anführer folgen)
            if (u.Rules.FleetCombat) PirateCommands.AfterFight(u, ship); else PirateSystem.GoHome(u, ship);
            return;
        }
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
        // Phase 25: im räumlichen Gefecht steht das Opfer, wo es ist; der Kaperer kommt aus seiner Richtung im Anfangsabstand heran
        var vPos = victim.Position;
        Join(u, e, raider, 0, CombatTactics.Raider, 0f, e.Spatial ? CombatSpace.Around(vPos, raider.Position, r.CombatStartDistance) : null);
        Join(u, e, victim, 1, victim.Id == u.PlayerShip ? CombatTactics.Player : CombatTactics.Trader, r.CombatStartDistance, e.Spatial ? vPos : null);
        foreach (var esc in FleetGuards.Escorts(u, victim, sector.Id))
            if (!u.Fights.IsEngaged(esc.Id)) Join(u, e, esc, 1, CombatTactics.Escort, r.CombatStartDistance, e.Spatial ? CombatSpace.Around(vPos, esc.Position, 8f) : null);
        var police = PirateSystem.PoliceIn(u, sector.Id, vPos);   // Runde 44: nur Streifen in Reichweite des Opfers
        if (police.Count > 0 && u.Rng.NextDouble() < MathF.Min(r.PoliceResponseMax, r.PoliceResponseChance * police.Count))
        {
            e.Police = police[0].Id;
            Join(u, e, police[0], 1, CombatTactics.Police, r.CombatStartDistance + r.CombatHelperDistance,
                e.Spatial ? CombatSpace.Around(vPos, police[0].Position, r.CombatStartDistance + r.CombatHelperDistance) : null);
        }
        Wanted.Report(u, raider.Owner, sector.Owner, "piracy");
        u.Events.Publish(new EncounterStarted(e.Id, sector.Id, EncounterCause.Raid, raider.Id, victim.Id,
            $"Gefecht in {sector.Name}: {u.ActorName(raider.Owner)} überfällt Schiff {victim.Id.Value}"));
        Reinforcements.Step(u, e);   // Phase 27: Rudel des Kaperers, Verband des Opfers
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
        Join(u, e, hunter, 0, ThreatMap.IsPolice(hunter) ? CombatTactics.Police : CombatTactics.Escort, 0f,
            e.Spatial ? CombatSpace.Around(raider.Position, hunter.Position, r.CombatStartDistance) : null);
        Join(u, e, raider, 1, CombatTactics.Raider, r.CombatStartDistance, e.Spatial ? raider.Position : null);
        string where = u.Sectors.TryGetValue(raider.Sector, out var sec) ? sec.Name : "?";
        u.Events.Publish(new EncounterStarted(e.Id, raider.Sector, EncounterCause.Intercept, hunter.Id, raider.Id, $"Gefecht in {where}: Streife stellt ein Kaperschiff"));
        Reinforcements.Step(u, e);   // Phase 27: Streife als Verband, Rudel des Kaperers
        return e;
    }

    /// <summary>
    /// Phase 27 (KAMPF.md Stufe 27): Die Polizei stellt einen gesuchten Spieler (Fahndungsstufe ≥ 3) als echtes Gefecht — nach der Vorwarnung. Die Streifen der
    /// Fraktion im Sektor (höchstens ein voller Verband) greifen an; der Spieler kann kämpfen, fliehen oder sich ergeben (<see cref="PlayerSurrender"/>: Strafe zahlen).
    /// </summary>
    public static Encounter StartArrest(Universe u, IReadOnlyList<Ship> police, Ship player, Sector sector)
    {
        var r = u.Rules;
        var e = Begin(u, EncounterCause.Arrest, sector.Id);
        e.Victim = player.Id;
        e.Police = police[0].Id;
        var at = player.Position;
        Join(u, e, player, 1, CombatTactics.Player, r.CombatStartDistance, e.Spatial ? at : null);
        int n = 0;
        foreach (var p in police)
        {
            if (u.Fights.IsEngaged(p.Id) || n >= Math.Max(1, r.SquadronMaxMembers)) continue;
            Join(u, e, p, 0, CombatTactics.Police, -HelperOffset(n), e.Spatial ? CombatSpace.Clear(u, sector.Id, CombatSpace.Around(at, p.Position, r.CombatStartDistance + HelperOffset(n))) : null);
            n++;
        }
        string who = u.Factions.TryGetValue(sector.Owner, out var fac) ? fac.Name : "Polizei";
        u.Events.Publish(new EncounterStarted(e.Id, sector.Id, EncounterCause.Arrest, police[0].Id, player.Id,
            $"Polizei von {who} greift an — „Ergeben“ zahlt die Strafe, sonst kämpfen oder fliehen."));
        Reinforcements.Step(u, e);
        return e;
    }

    /// <summary>
    /// Runde 40: Krieg — Polizei und Kriegsschiffe einer Fraktion, mit der der Spieler im Krieg steht, stellen sein Schiff in ihrem Gebiet als echtes
    /// Gefecht (<see cref="EncounterCause.War"/>, nach der Vorwarnung). Kein Ergeben gegen Geld (<see cref="PlayerSurrender"/> sperrt es). Die Angreifer
    /// sind die Polizeischiffe und Kriegsschiffe der Fraktion im Sektor (höchstens ein Verband wie bei der Festnahme).
    /// </summary>
    public static Encounter StartWar(Universe u, IReadOnlyList<Ship> attackers, Ship player, Sector sector)
    {
        var r = u.Rules;
        var e = Begin(u, EncounterCause.War, sector.Id);
        e.Victim = player.Id;
        e.Police = attackers[0].Id;
        var at = player.Position;
        Join(u, e, player, 1, CombatTactics.Player, r.CombatStartDistance, e.Spatial ? at : null);
        int n = 0;
        foreach (var a in attackers)
        {
            if (u.Fights.IsEngaged(a.Id) || n >= Math.Max(1, r.SquadronMaxMembers)) continue;
            Join(u, e, a, 0, CombatTactics.Police, -HelperOffset(n), e.Spatial ? CombatSpace.Clear(u, sector.Id, CombatSpace.Around(at, a.Position, r.CombatStartDistance + HelperOffset(n))) : null);
            n++;
        }
        string who = u.Factions.TryGetValue(sector.Owner, out var fac) ? fac.Name : "Kriegsmacht";
        u.Events.Publish(new EncounterStarted(e.Id, sector.Id, EncounterCause.War, attackers[0].Id, player.Id,
            $"Krieg: {who} greift dein Schiff in {sector.Name} an — kämpfen oder fliehen (kein Ergeben)."));
        Reinforcements.Step(u, e);
        return e;
    }

    static float HelperOffset(int n) => n * Reinforcements.HelperGap;
}
