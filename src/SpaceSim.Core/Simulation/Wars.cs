using SpaceSim.Core.Combat;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Phase 28 (POLITIK.md „Krieg: jeder darf, es kostet“, KAMPF.md Stufe 28): Regeln des Kriegs zwischen Fraktionen. Eine Fraktion erklärt nur mit
/// <b>Kriegsgrund</b> (Beziehung höchstens <see cref="EconomyRules.WarDeclareRelation"/>, verschoben nach Aggression), <b>Kriegskasse</b>
/// (<see cref="FactionViability.Spendable"/> ≥ <see cref="EconomyRules.WarChest"/>) und ohne klare Unterlegenheit (<see cref="EconomyRules.WarMinStrengthRatio"/>).
/// Im Krieg bleibt die Beziehung feindlich (Sperren folgen über die Politik von Phase 10), die Fraktionen kaufen Kriegsschiffe (<see cref="NavySystem"/>), die
/// feindliche Stationen belagern. Kriegsmüdigkeit aus Dauer und Verlusten beendet den Krieg (nie vor <see cref="EconomyRules.WarMinTicks"/>); eine Kasse unter
/// der Rücklage beendet ihn sofort — keine Kriegsentscheidung macht eine Fraktion handlungsunfähig. Rein bis auf die benannten Schreibstellen.
/// </summary>
public static class Wars
{
    /// <summary>Darf überhaupt Krieg geführt werden (Hauptschalter)?</summary>
    public static bool Active(Universe u) => u.Rules.Warfare;

    public static War? Between(Universe u, FactionId a, FactionId b)
    {
        foreach (var w in u.Wars) if ((w.A == a && w.B == b) || (w.A == b && w.B == a)) return w;
        return null;
    }

    public static bool AtWar(Universe u, FactionId a, FactionId b) => a != b && u.Wars.Count > 0 && Between(u, a, b) is not null;

    /// <summary>Im Krieg mit den Fraktionen zweier Akteure?</summary>
    public static bool AtWar(Universe u, ActorId a, ActorId b) =>
        u.Wars.Count > 0 && u.FactionOf(a) is { } fa && u.FactionOf(b) is { } fb && AtWar(u, fa, fb);

    /// <summary>Gegner einer Fraktion (Reihenfolge der Kriege).</summary>
    public static IEnumerable<FactionId> Enemies(Universe u, FactionId f)
    {
        foreach (var w in u.Wars) if (w.Involves(f)) yield return w.Other(f);
    }

    public static int CountOf(Universe u, FactionId f) => u.Wars.Count(w => w.Involves(f));

    static (int, int) Key(FactionId a, FactionId b) => a.Value < b.Value ? (a.Value, b.Value) : (b.Value, a.Value);

    /// <summary>Kriegsfähige Fraktion: Staat oder Gilde (keine Gesetzlosen, nicht die Spielerfraktion ohne Akteur).</summary>
    public static bool CanWage(Universe u, FactionId f) =>
        u.Factions.ContainsKey(f) && u.Actors.TryGetValue(f, out var a) && a.Kind is ActorKind.State or ActorKind.Guild;

    /// <summary>Runde 38: Kriegspartei — eine kriegsfähige KI-Fraktion oder die Spielerfraktion.</summary>
    public static bool IsParty(Universe u, FactionId f) => CanWage(u, f) || u.IsPlayerFaction(f);

    /// <summary>
    /// Kriegsstärke: Kampfstärke der Polizei und Kriegsschiffe + verfügbares Geld ÷ <see cref="EconomyRules.WarStrengthMoneyScale"/>. Runde 38: für die
    /// Spielerfraktion die Kampfstärke ihrer bewaffneten Schiffe + Kasse ÷ Maßstab.
    /// </summary>
    public static float Strength(Universe u, FactionId f)
    {
        float s = 0f;
        if (u.IsPlayerFaction(f))
        {
            foreach (var ship in u.ShipsById())
                if (ship.Owner == u.PlayerActor && ship.Has(Capability.Combat)) s += CombatRules.Strength(ship);
            return s + MathF.Max(0f, u.Player.Treasury.Credits) / MathF.Max(1f, u.Rules.WarStrengthMoneyScale);
        }
        foreach (var ship in u.ShipsById())
            if ((ThreatMap.IsPolice(ship) || IsWarship(ship)) && u.FactionOf(ship) == f) s += CombatRules.Strength(ship);
        if (u.Actors.TryGetValue(f, out var a)) s += FactionViability.Spendable(u, a) / MathF.Max(1f, u.Rules.WarStrengthMoneyScale);
        return s;
    }

    /// <summary>Schwelle des Kriegsgrunds für eine Fraktion (Aggression senkt die Hemmschwelle). Rein.</summary>
    public static float DeclareThreshold(Universe u, FactionId f) =>
        u.Rules.WarDeclareRelation + (ActorRules.PersonalityOf(u, f).Aggression - 0.5f) * u.Rules.LockAggressionShift;

    /// <summary>Warum <paramref name="a"/> gegen <paramref name="b"/> jetzt keinen Krieg erklären würde (null = es würde). Rein.</summary>
    public static string? DeclareIssue(Universe u, FactionId a, FactionId b)
    {
        var r = u.Rules;
        if (!Active(u)) return "Krieg ist abgeschaltet.";
        // Runde 38: eine KI-Fraktion erklärt dem Spieler nur mit Regel AiWarOnPlayer den Krieg (sonst gleiche Bedingungen); der Spieler erklärt über PlayerDeclareIssue
        bool vsPlayer = u.IsPlayerFaction(b);
        if (vsPlayer && !r.AiWarOnPlayer) return "Krieg gegen den Spieler ist (noch) abgeschaltet.";
        if (a == b || !CanWage(u, a) || !(CanWage(u, b) || vsPlayer)) return "Keine kriegsfähigen Fraktionen.";
        if (AtWar(u, a, b)) return "Schon im Krieg.";
        if (u.PeaceSince.TryGetValue(Key(a, b), out var peace) && u.Tick - peace < r.PeaceCooldownTicks) return "Frieden ist zu frisch.";
        float rel = Relations.Effective(u, a, b);
        if (rel > DeclareThreshold(u, a)) return "Kein Kriegsgrund.";
        if (FactionViability.Spendable(u, u.Actors[a]) < r.WarChest) return "Keine Kriegskasse.";
        float own = Strength(u, a), enemy = Strength(u, b);
        if (own < r.WarMinStrengthRatio * enemy) return "Klar unterlegen.";
        return null;
    }

    /// <summary>
    /// Runde 38 (Entscheidung des Nutzers: kein Kriegsgrund nötig): warum der Spieler <paramref name="f"/> jetzt nicht den Krieg erklären kann (null = er kann).
    /// Bedingungen: Krieg an, Spielerfraktion vorhanden, <paramref name="f"/> kriegsfähige KI-Fraktion, noch kein Krieg, Abkühlzeit nach dem letzten Frieden
    /// (<see cref="EconomyRules.PeaceCooldownTicks"/>) vorbei, Kriegskasse (Spielerkasse ≥ <see cref="EconomyRules.WarChest"/>). Rein.
    /// </summary>
    public static string? PlayerDeclareIssue(Universe u, FactionId f)
    {
        var r = u.Rules;
        if (!Active(u)) return "Krieg ist abgeschaltet.";
        if (u.PlayerFaction is not { } me) return "Du bist keine eigene Fraktion.";
        if (!CanWage(u, f)) return "Mit dieser Fraktion kann man keinen Krieg führen.";
        if (AtWar(u, me, f)) return "Schon im Krieg.";
        if (u.PeaceSince.TryGetValue(Key(me, f), out var peace) && u.Tick - peace < r.PeaceCooldownTicks)
            return $"Frieden ist zu frisch (noch {(r.PeaceCooldownTicks - (u.Tick - peace)) / 240} min).";
        if (u.Player.Treasury.Credits < r.WarChest) return $"Kriegskasse fehlt ({r.WarChest:0} Cr nötig).";
        return null;
    }

    /// <summary>
    /// Runde 38: der Spieler erklärt <paramref name="f"/> den Krieg (ohne Prüfung; <see cref="PlayerDeclareIssue"/> vorher). Die Beziehung jeder anderen
    /// KI-Fraktion zu ihm sinkt um <see cref="EconomyRules.PlayerWarRelationLoss"/> (<see cref="PlayerStanding.Change"/>). Die Kriegskasse bleibt beim Spieler
    /// (Bedingung, kein Preis — wie bei KI-Kriegen).
    /// </summary>
    public static War PlayerDeclare(Universe u, FactionId f)
    {
        var me = u.PlayerFaction ?? throw new InvalidOperationException("Keine Spielerfraktion");
        var w = Declare(u, me, f);
        foreach (var other in u.NpcFactionIds())
            if (other != f && CanWage(u, other)) PlayerStanding.Change(u, other, -u.Rules.PlayerWarRelationLoss, "war_declared");
        return w;
    }

    /// <summary>Krieg erklären (ohne Prüfung; die Bedingungen prüft <see cref="DeclareIssue"/>). Beziehungen werden feindlich, Ereignis.</summary>
    public static War Declare(Universe u, FactionId a, FactionId b)
    {
        if (Between(u, a, b) is { } running) return running;
        var w = new War { A = a, B = b, Since = u.Tick };
        u.Wars.Add(w);
        float rel = Relations.Effective(u, a, b);
        PinHostile(u, w);
        u.TouchAccess();
        string na = u.Factions.TryGetValue(a, out var fa) ? fa.Name : "?", nb = u.Factions.TryGetValue(b, out var fb) ? fb.Name : "?";
        u.Events.Publish(new WarDeclared(a, b, rel, $"{na} erklärt {nb} den Krieg."));
        return w;
    }

    /// <summary>Frieden schließen: Krieg entfernt, Beziehung beider Seiten auf <see cref="EconomyRules.PeaceRelation"/>, Abkühlzeit.</summary>
    public static void MakePeace(Universe u, War w, string reason)
    {
        if (!u.Wars.Remove(w)) return;
        // Runde 38: Spielerbeziehungen driften nicht zurück — nach einem Spielerkrieg mindestens PlayerPeaceRelation (über der ersten Sperrschwelle, kein Dauerzoll)
        float floor = u.IsPlayerFaction(w.A) || u.IsPlayerFaction(w.B) ? u.Rules.PlayerPeaceRelation : u.Rules.PeaceRelation;
        Relations.Set(u, w.A, w.B, MathF.Max(Relations.Effective(u, w.A, w.B), floor));
        Relations.Set(u, w.B, w.A, MathF.Max(Relations.Effective(u, w.B, w.A), floor));
        u.PeaceSince[Key(w.A, w.B)] = u.Tick;
        u.TouchAccess();
        string na = u.Factions.TryGetValue(w.A, out var fa) ? fa.Name : "?", nb = u.Factions.TryGetValue(w.B, out var fb) ? fb.Name : "?";
        string why = reason switch { "weary" => "Kriegsmüdigkeit", "broke" => "leere Kasse", _ => "Vereinbarung" };
        u.Events.Publish(new PeaceConcluded(w.A, w.B, reason, u.Tick - w.Since, $"Frieden zwischen {na} und {nb} ({why})."));
    }

    /// <summary>Im Krieg bleibt die Beziehung beider Seiten höchstens <see cref="EconomyRules.WarRelation"/>.</summary>
    public static void PinHostile(Universe u, War w)
    {
        float cap = u.Rules.WarRelation;
        if (Relations.Effective(u, w.A, w.B) > cap) Relations.Set(u, w.A, w.B, cap);
        if (Relations.Effective(u, w.B, w.A) > cap) Relations.Set(u, w.B, w.A, cap);
    }

    /// <summary>Verlust (Materialwert) einer Seite im Krieg buchen; ohne Krieg nichts.</summary>
    public static void OnLoss(Universe u, ActorId owner, ActorId attacker, float value)
    {
        if (u.Wars.Count == 0 || u.FactionOf(owner) is not { } f || u.FactionOf(attacker) is not { } g || Between(u, f, g) is not { } w) return;
        if (w.A == f) w.LossA += value; else w.LossB += value;
    }

    /// <summary>Kriegsschiff einer Fraktion (Etikett, KI)?</summary>
    public static bool IsWarship(Ship s) => s.Role == ShipRole.Warship && s.Controller == Controller.Ai;

    /// <summary>Kriegsschiffe einer Fraktion (feste Reihenfolge).</summary>
    public static List<Ship> WarshipsOf(Universe u, FactionId f) => u.ShipsById().Where(s => IsWarship(s) && u.FactionOf(s) == f).ToList();

    /// <summary>
    /// Kriegsziel einer Fraktion für ein Schiff: angreifbare Station eines Gegners (fertig, kein Versteck), bevorzugt in umkämpften Sektoren und nahe — nach
    /// Sprüngen von der Position des Schiffs (physisch erreichbar), dann Id. null = keins. Rein.
    /// </summary>
    /// <summary>
    /// Phase 31 (docs/TORE.md 5): Torblockade für ein Kriegsschiff der Fraktion <paramref name="f"/> — ein Tor-Paar mit Kampfwerten, an dem ein Ende einem
    /// Kriegsgegner gehört, dessen Krieg noch Blockaden frei hat (<see cref="EconomyRules.GateBlockadesPerWar"/>; eine laufende Blockade wird fortgesetzt), das die
    /// Karte nicht teilen würde (<see cref="MapParts.SplitsWhenClosed"/>, berücksichtigt bereits ausgefallene Tore) und nur, solange weltweit weniger als
    /// <see cref="EconomyRules.GateMaxDown"/> Tore außer Betrieb sind. Wert = Verkehr des gegnerischen Endsektors; angegriffen wird die Hälfte, die das Schiff
    /// mit den wenigsten Sprüngen erreicht. Piraten greifen nie Tore an (nur Kriegsschiffe bekommen diese Wahl). null = keine Blockade.
    /// </summary>
    public static Gate? PickGateTarget(Universe u, FactionId f, Ship ship)
    {
        var r = u.Rules;
        if (!r.GateCombat || !r.Warfare || u.Wars.Count == 0) return null;
        if (GateCombat.DownCount(u) >= r.GateMaxDown) return null;
        Gate? best = null; float bestValue = -1f; int bestHops = int.MaxValue;
        foreach (var g in u.Gates.Values.OrderBy(x => x.Id.Value))
        {
            if (!u.Gates.TryGetValue(g.Target, out var far) || g.Id.Value > far.Id.Value) continue;   // je Paar einmal (kanonisch)
            if (GateCombat.TypeOf(u, g) is null || GateCombat.IsDown(u, g) || GateCombat.InGrace(u, g) || !MapParts.Passable(g) || !MapParts.Passable(far)) continue;
            War? war = WarOverGate(u, f, g);
            if (war is null) continue;
            if (war.Blockades >= r.GateBlockadesPerWar && !BlockadeRunning(u, f, g)) continue;
            if (MapParts.SplitsWhenClosed(u, g.Id)) continue;   // nie kartenteilend
            float value = 0f;
            foreach (var half in new[] { g, far })
                if (u.Sectors.TryGetValue(half.Sector, out var sec) && Wars.AtWar(u, f, sec.Owner)) value += MathF.Max(0f, sec.Traffic);
            // erreichbare Hälfte mit den wenigsten Sprüngen
            Gate? side = null; int hops = int.MaxValue;
            foreach (var half in new[] { g, far })
            {
                var route = u.FindRoute(ship.Sector, half.Sector);
                if (route is null) continue;
                if (route.Count < hops) { hops = route.Count; side = half; }
            }
            if (side is null) continue;
            if (value > bestValue || (value == bestValue && hops < bestHops)) { best = side; bestValue = value; bestHops = hops; }
        }
        return best;
    }

    /// <summary>Krieg der Fraktion mit einem Besitzer eines Torendes (null = keiner).</summary>
    public static War? WarOverGate(Universe u, FactionId f, Gate g)
    {
        foreach (var d in GateCombat.DefendersOf(u, g)) if (d != f && Between(u, f, d) is { } w) return w;
        return null;
    }

    /// <summary>Läuft schon eine Blockade dieses Paars durch Kriegsschiffe der Fraktion?</summary>
    public static bool BlockadeRunning(Universe u, FactionId f, Gate g)
    {
        foreach (var s in u.Ships.Values)
            if (IsWarship(s) && u.FactionOf(s) == f && s.Command is AssaultCommand ac && ac.Gate.Value != 0 && (ac.Gate == g.Id || ac.Gate == g.Target)) return true;
        return false;
    }

    public static Station? PickTarget(Universe u, FactionId f, Ship ship)
    {
        Station? best = null;
        int bestHops = int.MaxValue;
        foreach (var enemy in Enemies(u, f))
            foreach (var st in u.StationsById())
            {
                if (st.IsBuildSite || st.Owner != (ActorId)enemy || StationProtection.IsHideout(u, st) || StationCombat.IsDown(u, st)) continue;
                var route = u.FindRoute(ship.Sector, st.Sector);
                if (route is null) continue;
                int hops = route.Count;
                if (hops < bestHops || (hops == bestHops && best is not null && st.Id.Value < best.Id.Value)) { best = st; bestHops = hops; }
            }
        return best;
    }
}

/// <summary>
/// Phase 28: Krieg und Frieden (Politik-Modul, Intervall 100). Nur mit <see cref="EconomyRules.Warfare"/>: laufende Kriege halten die Beziehung feindlich und
/// werden müde (Dauer ÷ <see cref="EconomyRules.WarWearyTicks"/> + Verluste ÷ <see cref="EconomyRules.WarLossScale"/>); ab Müdigkeit 1 (nach der Mindestdauer) oder
/// wenn eine Kasse unter ihre Rücklage fällt, ist Frieden. Mit <see cref="EconomyRules.WarDeclarations"/> erklärt eine Fraktion Krieg, wenn
/// <see cref="Wars.DeclareIssue"/> nichts einwendet (Fraktionspaare in Id-Reihenfolge, höchstens ein neuer Krieg je Takt). Kein Zufall.
/// </summary>
public sealed class WarSystem : ISimSystem
{
    public const int Interval = 100;

    public void Step(Universe u, int dt)
    {
        var r = u.Rules;
        if (!r.Warfare) return;
        foreach (var w in u.Wars.ToList())
        {
            Wars.PinHostile(u, w);
            float ticks = u.Tick - w.Since;
            w.Weariness = ticks / MathF.Max(1f, r.WarWearyTicks) + MathF.Max(w.LossA, w.LossB) / MathF.Max(1f, r.WarLossScale);
            bool broke = Broke(u, w.A) || Broke(u, w.B);
            if (broke) { Wars.MakePeace(u, w, "broke"); continue; }
            if (ticks >= r.WarMinTicks && w.Weariness >= 1f) Wars.MakePeace(u, w, "weary");
        }
        if (!r.WarDeclarations) return;
        var factions = u.NpcFactionIds();   // Runde 38: die KI erklärt nur KI-Fraktionen den Krieg …
        foreach (var a in factions)
            foreach (var b in factions)
            {
                if (a == b || Wars.DeclareIssue(u, a, b) is not null) continue;
                Wars.Declare(u, a, b);
                return;
            }
        // … und dem Spieler nur mit Regel AiWarOnPlayer (vorbereitet, an mit Runde 40), nach denselben Bedingungen
        if (r.AiWarOnPlayer && u.PlayerFaction is { } me)
            foreach (var a in factions)
            {
                if (Wars.DeclareIssue(u, a, me) is not null) continue;
                Wars.Declare(u, a, me);
                return;
            }
    }

    /// <summary>Kasse unter der Rücklage (nach deren Aufbauzeit) — dann ist Frieden Pflicht.</summary>
    static bool Broke(Universe u, FactionId f) =>
        u.Actors.TryGetValue(f, out var a) && !FactionViability.ReserveBuilding(u, a) && a.Treasury.Credits < FactionViability.Reserve(u, f);
}

/// <summary>
/// Phase 28: Kriegsschiffe der Fraktionen (Sicherheitsmodul, Intervall 100). Nur mit <see cref="EconomyRules.Warfare"/>: Unterhalt aus der Kasse
/// (<see cref="EconomyRules.WarshipUpkeepPerTick"/>, „navy“); im Krieg kauft die Fraktion bis <see cref="EconomyRules.WarshipsPerWar"/> je Krieg (höchstens
/// <see cref="EconomyRules.MaxWarshipsPerFaction"/>) über die gemeinsame Kaufregel (<see cref="ShipPurchase"/>), nur aus dem verfügbaren Geld über der Rücklage;
/// nach dem Frieden (<see cref="EconomyRules.WarshipPeaceTicks"/>) oder wenn die Kasse unter die Rücklage fällt wird abgebaut (Grund c, über
/// <see cref="Departures"/>). Befehle: im Krieg Angriff auf das nächste feindliche Ziel (<see cref="AssaultCommand"/>), zur Versorgung heim, im Frieden an der Basis.
/// </summary>
public sealed class NavySystem : ISimSystem
{
    public const int Interval = 100;

    public void Step(Universe u, int dt)
    {
        var r = u.Rules;
        if (!r.Warfare) return;
        foreach (var f in u.NpcFactionIds().ToList())   // Runde 38: der Spieler kauft keine KI-Kriegsschiffe
        {
            if (!Wars.CanWage(u, f) || !u.Actors.TryGetValue(f, out var actor)) continue;
            var ships = Wars.WarshipsOf(u, f);
            if (ships.Count == 0 && Wars.CountOf(u, f) == 0) continue;
            float upkeep = r.WarshipUpkeepPerTick * ships.Count * dt;
            if (upkeep > 0f)
            {
                float paid = MathF.Min(upkeep, MathF.Max(0f, actor.Treasury.Credits));
                actor.Treasury.Credits -= paid;
                u.Events.Publish(new TreasuryUpkeep(actor.Id, "navy", paid, upkeep - paid));
            }
            int wars = Wars.CountOf(u, f);
            int desired = wars > 0 ? Math.Min(r.MaxWarshipsPerFaction, r.WarshipsPerWar * wars) : 0;
            bool belowReserve = !FactionViability.ReserveBuilding(u, actor) && actor.Treasury.Credits < FactionViability.Reserve(u, f);
            if (ships.Count > 0 && (belowReserve || (ships.Count > desired && PeaceLongEnough(u, f))))
            {
                var gone = ships.Where(s => !u.Fights.IsEngaged(s.Id)).OrderByDescending(s => s.Id.Value).FirstOrDefault();
                if (gone is not null)
                {
                    ships.Remove(gone);
                    Departures.RemoveShip(u, gone, DepartureReason.Unprofitable, "decommissioned");
                    u.Events.Publish(new WarshipDecommissioned(f, gone.Id, belowReserve ? "reserve" : "peace"));
                }
            }
            // Runde 24: eine Startszenario-Sperre (LastForceTick in der Zukunft, ScenarioDef.PoliceHoldTicks) gilt auch für Kriegsschiffe; im normalen Spiel nie in der Zukunft
            else if (ships.Count < desired && u.Tick >= actor.LastForceTick && Commission(u, f) is { } bought) ships.Add(bought);
            foreach (var s in ships) Command(u, f, s);
        }
    }

    /// <summary>Seit dem letzten Frieden der Fraktion ist genug Zeit vergangen (ohne Friedensschluss: sofort).</summary>
    static bool PeaceLongEnough(Universe u, FactionId f)
    {
        if (Wars.CountOf(u, f) > 0) return true;   // Überzahl im Krieg (Obergrenze gesenkt): abbauen
        long last = long.MinValue;
        foreach (var (k, t) in u.PeaceSince) if (k.A == f.Value || k.B == f.Value) last = Math.Max(last, t);
        return last == long.MinValue || u.Tick - last >= u.Rules.WarshipPeaceTicks;
    }

    /// <summary>Muss das Kriegsschiff zur Versorgung heim (Hülle, Munition/Raketen, Modulschaden)?</summary>
    public static bool NeedsBase(Universe u, Ship s) =>
        s.Hull < u.Rules.WarshipRetreatHull * s.MaxHull
        || u.Rules.AmmoActive && s.Stats.MissileCapacity > 0f && s.Systems.MissilesMissing >= s.Stats.MissileCapacity - 0.5f
        || u.Rules.DamageActive && NpcRepair.Crippled(u, s);

    static void Command(Universe u, FactionId f, Ship s)
    {
        if (u.Fights.IsEngaged(s.Id) || s.Emergency) return;
        if (s.HomeBase.Value == 0 || !u.Stations.ContainsKey(s.HomeBase))
            if (SecuritySystem.HomeBaseFor(u, f, s.Sector) is { } h) s.HomeBase = h.Id;
        if (s.Command is ReturnCommand) return;
        if (NeedsBase(u, s) || Wars.CountOf(u, f) == 0)
        {
            bool atHome = s.Transit is null && s.DockedAt == s.HomeBase;
            if (!atHome || NeedsBase(u, s)) Commands.Give(u, s, new ReturnCommand(), out _, ignoreCapacity: true);
            else if (s.Command is not null) Commands.Cancel(u, s, "Frieden");
            return;
        }
        if (s.Command is AssaultCommand ac && AssaultExecutor.TargetOf(u, ac) is { } cur && AssaultExecutor.StillTarget(u, s, cur)) return;
        // Phase 31: zuerst die Torblockade des Kriegs (höchstens eine je Krieg, nie kartenteilend, höchstens GateMaxDown weltweit), sonst eine Station
        if (Wars.PickGateTarget(u, f, s) is { } gate)
        {
            if (!Wars.BlockadeRunning(u, f, gate) && Wars.WarOverGate(u, f, gate) is { } war) war.Blockades++;
            Commands.Give(u, s, new AssaultCommand(gate.Sector, default, gate.Id), out _, ignoreCapacity: true);
            return;
        }
        if (Wars.PickTarget(u, f, s) is { } target) Commands.Give(u, s, new AssaultCommand(target.Sector, target.Id), out _, ignoreCapacity: true);
    }

    /// <summary>Kauft ein Kriegsschiff (gemeinsame Kaufregel) an einer erreichbaren Werft, nur aus dem verfügbaren Geld über der Rücklage. null = kein Kauf.</summary>
    public static Ship? Commission(Universe u, FactionId f)
    {
        var r = u.Rules;
        if (r.WarshipChoosesModels)
        {
            // Phase 29: Wahl unter allen Kampfmodellen, im Krieg voll auf Kampfstärke gewichtet
            if (!u.Actors.TryGetValue(f, out var a) || SecuritySystem.HomeBaseFor(u, f, null) is not { } h
                || CombatModels.AtYard(u, h.Sector, FactionViability.Spendable(u, a), 1f) is not { } pick) return null;
            var q = ShipQualityRoll.Roll(u, pick.Class, pick.Class.Slots, pick.Model.Fit, pick.Yard, pick.Needs);
            ShipPurchase.Consume(pick.Yard, q.Materials ?? pick.Needs);
            pick.Yard.Credits += pick.Price;
            a.Treasury.Credits -= pick.Price;
            var w = ShipPurchase.Create(u, u.NewShipId(), pick.Model.Key, q.Slots, q.Fit, ShipRole.Warship, Controller.Ai, a.Id, pick.Yard, 0f, q.Grade);
            w.HomeBase = h.Id;
            w.JobSince = u.Tick;
            u.AddShip(w);
            u.Events.Publish(new TreasuryUpkeep(a.Id, "navy", pick.Price, 0f));
            u.Events.Publish(new WarshipCommissioned(f, w.Id, pick.Yard.Id, pick.Price));
            return w;
        }
        if (!u.Actors.TryGetValue(f, out var actor) || !u.ShipModels.TryGetValue(r.WarshipModel, out var model)
            || !u.ShipClasses.TryGetValue(model.ClassKey, out var cls) || ShipPurchase.Materials(u, model) is not { } needs) return null;
        var home = SecuritySystem.HomeBaseFor(u, f, null);
        if (home is null) return null;
        foreach (var yard in u.StationsById().Where(st => !st.IsBuildSite && ShipPurchase.SellsModel(u, st, model)).ToList())
        {
            if (!u.IsReachable(yard.Sector, home.Sector) || ShipPurchase.Missing(yard, needs).Count > 0) continue;
            float price = ShipPurchase.Price(u, yard, needs, ShipPurchase.AssemblyOf(u, yard));
            if (FactionViability.Spendable(u, actor) < price) return null;
            ShipPurchase.Consume(yard, needs);
            yard.Credits += price;
            actor.Treasury.Credits -= price;
            var ship = ShipPurchase.Create(u, u.NewShipId(), model.Key, cls.Slots, model.Fit, ShipRole.Warship, Controller.Ai, actor.Id, yard, 0f);
            ship.HomeBase = home.Id;
            ship.JobSince = u.Tick;
            u.AddShip(ship);
            u.Events.Publish(new TreasuryUpkeep(actor.Id, "navy", price, 0f));
            u.Events.Publish(new WarshipCommissioned(f, ship.Id, yard.Id, price));
            return ship;
        }
        return null;
    }
}
