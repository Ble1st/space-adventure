using SpaceSim.Core.Combat;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Phase 27 (KAMPF.md Stufe 27): Die Polizei einer Fraktion läuft über die Befehls-Ausführer von Phase 12 statt über Sonderlogik. Die Fraktion entscheidet nur
/// noch und vergibt Befehle: Streife im Einsatzsektor (<see cref="PatrolCommand"/>; ab <see cref="EconomyRules.PoliceSquadronMin"/> Schiffen im selben Sektor als
/// Verband), Heimkehr zur Versorgung (<see cref="ReturnCommand"/>), Geleit für wertvolle Fracht in bedrohten Sektoren (<see cref="EscortCommand"/>, nur im
/// Hoheitsgebiet) und Verfolgung geflohener Kaperschiffe (<see cref="PursueCommand"/>, nur in der Zuständigkeit). Ausgeführt wird alles von den registrierten
/// Ausführern in <see cref="OrderSystem"/>; Gefechte übernehmen wie immer die Ebene darüber.
/// </summary>
public static class PoliceCommands
{
    /// <summary>Muss das Polizeischiff zur Versorgung heim (Hülle, Treibstoff, Magazin, Modulschaden)?</summary>
    public static bool NeedsBase(Universe u, Ship p) =>
        p.Hull < u.Rules.RetreatHull * p.MaxHull || u.Rules.FuelActive && p.Stats.FuelCapacity > 0f && p.TotalFuel < 0.3f * p.Stats.FuelCapacity
        || u.Rules.AmmoActive && p.Stats.AmmoCapacity > 0f && p.AmmoLoaded < u.Rules.AmmoReturnShare * p.Stats.AmmoCapacity
        || u.Rules.DamageActive && NpcRepair.Crippled(u, p);

    static bool Give(Universe u, Ship p, ShipCommand c)
    {
        if (Squadrons.Get(u, p) is { } sq && sq.Leader == p.Id && sq.Members.Count > 1 && c is not ReturnCommand)
            return Squadrons.Command(u, sq, c, out _);
        return Commands.Give(u, p, c, out _, ignoreCapacity: true);
    }

    /// <summary>Ein Takt je Polizeischiff (<see cref="SecuritySystem"/>, Intervall 5): Heimkehr bei Bedarf, sonst Streife im Einsatzsektor; Geleit und Verfolgung laufen, bis ihr Ausführer oder <see cref="Organize"/> sie beendet.</summary>
    public static void Step(Universe u, Ship p)
    {
        if (u.Fights.IsEngaged(p.Id) || p.Emergency) return;
        var home = u.Stations.GetValueOrDefault(p.HomeBase);
        if (home is null && u.FactionOf(p) is { } f && SecuritySystem.HomeBaseFor(u, f, p.Sector) is { } h) { p.HomeBase = h.Id; home = h; }
        if (p.Command is ReturnCommand) return;
        if (home is not null && NeedsBase(u, p))
        {
            // bleibt im Verband (kein Auflösen/Neubilden bei jeder Versorgung); ein Anführer nimmt die Streife mit heim
            if (Squadrons.Get(u, p) is { } rs && rs.Leader == p.Id && rs.Members.Count > 1) Squadrons.Command(u, rs, new ReturnCommand(), out _);
            else Commands.Give(u, p, new ReturnCommand(), out _, ignoreCapacity: true);
            return;
        }
        if (p.Command is PursueCommand) return;
        if (p.Command is EscortCommand ec)
        {
            if (EscortStillValid(u, p, ec.Target) && u.Tick - p.CommandSince < u.Rules.PoliceEscortTicks) return;
            Commands.Cancel(u, p, "Geleit beendet");
        }
        if (Squadrons.Get(u, p) is { } sq && sq.Leader != p.Id)
        {
            // Mitglied: folgt dem Anführer (nach der eigenen Versorgung wieder anhängen)
            if (p.Command is not FollowCommand) Commands.Give(u, p, new FollowCommand(sq.Leader), out _, ignoreCapacity: true);
            return;
        }
        if (p.DutySector == default || !u.Sectors.ContainsKey(p.DutySector))
        {
            if (home is not null && !(p.Transit is null && p.DockedAt == home.Id) && p.Command is null) Give(u, p, new ReturnCommand());
            return;
        }
        if (p.Command is PatrolCommand pc && pc.Sectors.Count == 1 && pc.Sectors[0] == p.DutySector) return;
        Give(u, p, new PatrolCommand(new[] { p.DutySector }));
    }

    static bool EscortStillValid(Universe u, Ship p, ShipId ward) =>
        u.Ships.TryGetValue(ward, out var w) && ThreatMap.CargoValue(u, w) > 0f && Jurisdiction.Covers(u, p, ThreatMap.Where(u, w).Sector);

    /// <summary>
    /// Fraktionsentscheidung (alle <see cref="SecuritySystem.ManageTicks"/>, nach der Zuweisung): Streifen desselben Einsatzsektors bilden einen Verband
    /// (ab <see cref="EconomyRules.PoliceSquadronMin"/>), Verbände ohne gemeinsamen Sektor lösen sich auf; in Sektoren mit Bedrohung ≥ <see cref="EconomyRules.PoliceEscortThreat"/>
    /// gibt eine Streife dem wertvollsten beladenen Handelsschiff Geleit.
    /// </summary>
    public static void Organize(Universe u, FactionId f, List<Ship> police)
    {
        var r = u.Rules;
        // Verbände prüfen: Mitglieder mit anderem Einsatz oder auf dem Heimweg verlassen ihn
        foreach (var sq in Squadrons.Of(u, f).ToList())
        {
            if (!u.Ships.TryGetValue(sq.Leader, out var leader)) continue;
            foreach (var id in sq.Members.ToList())
                if (u.Ships.TryGetValue(id, out var m) && id != sq.Leader && m.DutySector != leader.DutySector) Squadrons.Leave(u, m);
            if (u.Squadrons.ContainsKey(sq.Id) && sq.Members.Count < Math.Max(2, r.PoliceSquadronMin))
            {
                Squadrons.Disband(u, sq);
                u.Events.Publish(new ForceSquadron(f, sq.Id, "patrol", 0, false));
            }
        }
        // neue Verbände je Einsatzsektor
        if (r.PoliceSquadronMin >= 2)
            foreach (var group in police.Where(p => p.DutySector != default && !u.Fights.IsEngaged(p.Id) && p.Command is not PursueCommand && !p.Emergency)
                         .GroupBy(p => p.DutySector).OrderBy(g => g.Key.Value))
            {
                var ships = group.OrderBy(p => p.Id.Value).ToList();
                if (ships.Count < r.PoliceSquadronMin) continue;
                var sq = ships.Select(s => Squadrons.Get(u, s)).FirstOrDefault(s => s is not null);
                if (sq is null)
                {
                    var lead = ships.FirstOrDefault(s => s.Squadron.Value == 0 && s.Command is not ReturnCommand);
                    if (lead is null) continue;
                    sq = Squadrons.Create(u, lead, $"Streife {OrderMove.SectorName(u, group.Key)}", out _);
                    if (sq is null) continue;
                    u.Events.Publish(new ForceSquadron(f, sq.Id, "patrol", ships.Count, true));
                }
                foreach (var s in ships)
                    if (s.Squadron.Value == 0 && s.Command is not ReturnCommand && sq.Members.Count < r.SquadronMaxMembers) Squadrons.Join(u, sq, s, out _);
                if (u.Ships.TryGetValue(sq.Leader, out var leader) && leader.Command is not (PatrolCommand or EscortCommand or PursueCommand or ReturnCommand))
                    Squadrons.Command(u, sq, new PatrolCommand(new[] { group.Key }), out _);
            }
        AssignEscorts(u, f, police);
    }

    /// <summary>Geleit: je bedrohtem Einsatzsektor höchstens eine Begleitung — eine Streife (Einzelschiff oder Verbandsanführer) im Sektor begleitet das wertvollste beladene, nicht begleitete KI-Handelsschiff.</summary>
    static void AssignEscorts(Universe u, FactionId f, List<Ship> police)
    {
        var r = u.Rules;
        if (r.PoliceEscortThreat <= 0f) return;
        var escorted = new HashSet<ShipId>(police.Where(p => p.Command is EscortCommand).Select(p => ((EscortCommand)p.Command!).Target));
        foreach (var sector in SecuritySystem.Territory(u, f))
        {
            if (sector.Threat < r.PoliceEscortThreat) continue;
            if (police.Any(p => p.Command is EscortCommand ec && u.Ships.TryGetValue(ec.Target, out var w) && ThreatMap.Where(u, w).Sector == sector.Id)) continue;
            var guard = police.FirstOrDefault(p => p.DutySector == sector.Id && p.Command is PatrolCommand && !u.Fights.IsEngaged(p.Id)
                && (Squadrons.Get(u, p) is not { } sq || sq.Leader == p.Id) && ThreatMap.Where(u, p).Sector == sector.Id);
            if (guard is null) continue;
            Ship? ward = null; float best = r.PoliceEscortMinValue;
            foreach (var id in u.Tracker.ParkedIn(sector.Id).Concat(u.Tracker.TransitIn(sector.Id)).Distinct().OrderBy(i => i.Value))
            {
                if (!u.Ships.TryGetValue(id, out var s) || !s.IsEconomyAi || escorted.Contains(id) || u.Fights.IsEngaged(id)) continue;
                if (ThreatMap.Where(u, s).Sector != sector.Id) continue;
                float v = ThreatMap.CargoValue(u, s);
                if (v > best) { best = v; ward = s; }
            }
            if (ward is null) continue;
            if (Give(u, guard, new EscortCommand(ward.Id)))
            {
                escorted.Add(ward.Id);
                u.Events.Publish(new EscortAssigned(guard.Id, ward.Id, sector.Id));
            }
        }
    }

    /// <summary>
    /// Nach einem Gefecht (<see cref="CombatSystem.Finish"/>): Ist das Kaperschiff geflohen, nehmen die überlebenden, einsatzfähigen Polizeischiffe des Gefechts
    /// die Verfolgung auf — wenn sein Sektor in ihrer Zuständigkeit liegt (Verbandsmitglieder folgen ihrem Anführer).
    /// </summary>
    public static void AfterFight(Universe u, Encounter e)
    {
        if (!u.Rules.FleetCombat || e.Raider.Value == 0 || e.Of(e.Raider) is not { Exit: CombatExit.Fled } rp || !u.Ships.TryGetValue(e.Raider, out var raider)) return;
        var (where, _) = ThreatMap.Where(u, raider);
        foreach (var p in e.Participants)
        {
            if (p.Side == rp.Side || p.Exit is CombatExit.Destroyed or CombatExit.Lost or CombatExit.Fled || !u.Ships.TryGetValue(p.Ship, out var police)) continue;
            if (!ThreatMap.IsPolice(police) || u.Fights.IsEngaged(police.Id) || PoliceCommands.NeedsBase(u, police) || !Jurisdiction.Covers(u, police, where)) continue;
            if (Squadrons.Get(u, police) is { } sq && sq.Leader != police.Id) continue;
            if (police.Command is PursueCommand) continue;
            if (Give(u, police, new PursueCommand(raider.Id, u.Rules.PursuitLead, u.Tick + u.Rules.PursuitTicks)))
                u.Events.Publish(new PursuitStarted(police.Id, raider.Id, where));
        }
    }
}

/// <summary>
/// Phase 27 (KAMPF.md Stufe 27): Kaperschiffe einer Piratengruppe laufen über Befehle. Die Gruppe entscheidet: rastet ein Kaperschiff im Versteck aus, wählt sie ein
/// Revier (<see cref="PirateSystem.PickHunt"/>) und schickt es — mit weiteren ausgeruhten Kaperschiffen als Rudel (Verband, bis <see cref="EconomyRules.RaiderPackSize"/>)
/// — zum Lauern (<see cref="LurkCommand"/>); nach Lauerzeit oder Gefecht geht es heim (<see cref="ReturnCommand"/>, Versorgung aus der Kasse), dort löst sich das Rudel.
/// </summary>
public static class PirateCommands
{
    /// <summary>Kaperschiffe des Rudels (Verband) dieses Kaperschiffs, die gerade im Sektor und frei sind — mindestens 1 (es selbst).</summary>
    public static int PackIn(Universe u, Ship raider, SectorId sector)
    {
        if (Squadrons.Get(u, raider) is not { } sq) return 1;
        int n = 0;
        foreach (var id in sq.Members)
            if (u.Ships.TryGetValue(id, out var m) && !u.Fights.IsEngaged(id) && ThreatMap.Where(u, m).Sector == sector) n++;
        return Math.Max(1, n);
    }

    public static void Step(Universe u)
    {
        foreach (var a in u.Actors.Values.Where(a => a.Kind == ActorKind.Outlaw).OrderBy(a => a.Id.Value).ToList())
        {
            var raiders = u.ShipsById().Where(s => ThreatMap.IsRaider(s) && s.Owner == a.Id).ToList();
            foreach (var r in raiders)
            {
                if (!u.Ships.ContainsKey(r.Id) || u.Fights.IsEngaged(r.Id) || r.Emergency) continue;
                var sq = Squadrons.Get(u, r);
                if (sq is not null && sq.Leader != r.Id) continue;   // Rudelmitglied folgt
                if (r.Command is LurkCommand or ReturnCommand) continue;
                if (r.Command is not null) Commands.Cancel(u, r, "Gruppe entscheidet");
                if (!PirateSystem.AtHome(u, r))
                {
                    if (PirateSystem.Home(u, r) is null) { r.NextThinkTick = u.Tick + 200; continue; }
                    GiveAll(u, r, new ReturnCommand());
                    continue;
                }
                // daheim: Rudel auflösen, nach der Rast neu auf Beutezug
                if (sq is not null)
                {
                    var members = sq.Members.Where(id => id != r.Id).Select(id => u.Ships.GetValueOrDefault(id)).Where(m => m is not null).ToList();
                    Squadrons.Disband(u, sq);
                    u.Events.Publish(new ForceSquadron(a.Id, sq.Id, "pack", 0, false));
                    // die Mitglieder sind dem Anführer heim gefolgt: Versorgung aus der Kasse wie beim Anführer
                    foreach (var m in members)
                        if (PirateSystem.AtHome(u, m!) && PirateSystem.Home(u, m!) is { } hm) UnitSupport.Serve(u, m!, hm);
                }
                if (u.Tick < r.NextThinkTick || u.PendingEncounters.Any(e => e.Source == r.Id)) continue;
                if (PirateSystem.PickHunt(u, r) is not { } hunt) { r.NextThinkTick = u.Tick + 200; continue; }
                var pack = raiders.Where(o => o.Id != r.Id && u.Ships.ContainsKey(o.Id) && o.Command is null && o.Squadron.Value == 0 && !u.Fights.IsEngaged(o.Id)
                    && u.Tick >= o.NextThinkTick && PirateSystem.AtHome(u, o)).Take(Math.Max(0, u.Rules.RaiderPackSize - 1)).ToList();
                var cmd = new LurkCommand(hunt.Sector.Id, hunt.Spot, 0);
                r.DutySector = hunt.Sector.Id;
                if (pack.Count > 0 && Squadrons.Create(u, r, $"Rudel {u.ActorName(a.Id)}", out _) is { } nsq)
                {
                    foreach (var o in pack) Squadrons.Join(u, nsq, o, out _);
                    u.Events.Publish(new ForceSquadron(a.Id, nsq.Id, "pack", nsq.Members.Count, true));
                    if (!Squadrons.Command(u, nsq, cmd, out _)) r.NextThinkTick = u.Tick + 200;
                }
                else if (!Commands.Give(u, r, cmd, out _, ignoreCapacity: true)) { r.DutySector = default; r.NextThinkTick = u.Tick + 200; }
            }
        }
    }

    static void GiveAll(Universe u, Ship leader, ShipCommand c)
    {
        if (Squadrons.Get(u, leader) is { } sq && sq.Leader == leader.Id && sq.Members.Count > 1) Squadrons.Command(u, sq, c, out _);
        else Commands.Give(u, leader, c, out _, ignoreCapacity: true);
    }

    /// <summary>Nach einem Gefecht: Rudelmitglieder folgen weiter ihrem Anführer, der Anführer (oder ein einzelnes Kaperschiff) fliegt mit dem Rudel heim.</summary>
    public static void AfterFight(Universe u, Ship raider)
    {
        if (Squadrons.Get(u, raider) is { } sq && sq.Leader != raider.Id && u.Ships.ContainsKey(sq.Leader))
        {
            if (raider.Command is FollowCommand f && OrderExecutors.Default.TryGet(f.Kind, out var ex)) ex.Start(u, raider, f);
            else Commands.Give(u, raider, new FollowCommand(sq.Leader), out _, ignoreCapacity: true);
            return;
        }
        if (raider.Command is not null) Commands.Cancel(u, raider, "nach dem Gefecht");
        if (PirateSystem.Home(u, raider) is null) { raider.NextThinkTick = u.Tick + 200; return; }
        if (PirateSystem.AtHome(u, raider)) { raider.DutySector = default; raider.NextThinkTick = u.Tick + u.Rules.PirateRestTicks; return; }
        GiveAll(u, raider, new ReturnCommand());
    }
}
