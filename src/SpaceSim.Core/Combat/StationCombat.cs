using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Weapons;

namespace SpaceSim.Core.Combat;

/// <summary>
/// Phase 28 (KAMPF.md Stufe 28): Stationen im Kampf — Hülle, Schild und Panzerung aus <see cref="StationType.Defense"/> (ohne Abschnitt: Grundhülle
/// <see cref="EconomyRules.StationBaseHull"/>), Stationsgeschütze, die in Gefechten ihres Sektors auf feindliche Teilnehmer in Reichweite schießen (über die eine
/// Trefferregel <see cref="Hit.Roll"/>), Treffer auf Stationen und ihr Fall: erobert (nur mit <see cref="EconomyRules.AllowStationCapture"/> und wo
/// <see cref="StationProtection.IsCapturable"/>), zerstört (nur wo <see cref="StationProtection.IsDestructible"/>, nie Regierungsstationen — über
/// <see cref="Departures"/>, Grund a) oder ausgeschaltet (Geschütze schweigen, bis die Reparatur die Hülle über <see cref="EconomyRules.StationReenableShare"/> bringt).
/// Der Kampfzustand ist <see cref="Station.Condition"/> (Fehlbeträge; null = unbeschädigt).
/// </summary>
public static class StationCombat
{
    public static StationDefenseConfig? DefenseOf(Universe u, Station st) =>
        !st.IsBuildSite && u.StationTypes.TryGetValue(st.TypeKey, out var t) ? t.Defense : null;

    /// <summary>Phase 30: ohne <c>defense</c> die Grundhülle der Größenklasse (S 600, M 800 = <see cref="EconomyRules.StationBaseHull"/>, L 1400, XL 2500).</summary>
    public static float MaxHull(Universe u, Station st) => DefenseOf(u, st)?.Hull
        ?? (u.StationTypes.TryGetValue(st.TypeKey, out var t) && t.Size != StationSize.M ? StationSizes.BaseHull(t.Size) : u.Rules.StationBaseHull);
    public static float MaxShield(Universe u, Station st) => DefenseOf(u, st)?.Shield ?? 0f;
    public static float Hull(Universe u, Station st) => MathF.Max(0f, MaxHull(u, st) - (st.Condition?.HullMissing ?? 0f));
    public static float Shield(Universe u, Station st) => MathF.Max(0f, MaxShield(u, st) - (st.Condition?.ShieldMissing ?? 0f));

    /// <summary>Ausgeschaltet (Hülle 0 einer Station, die nicht fallen darf) — schießt nicht, ist kein Kriegsziel mehr.</summary>
    public static bool IsDown(Universe u, Station st) => st.Condition is { Disabled: true };

    static StationCondition Cond(Station st) => st.Condition ??= new StationCondition();

    /// <summary>
    /// Phase 30 (bewusste Entscheidung): Stationsgeschütze stehen auf der Außenhaut — ihre Reichweite ab der Mitte wächst mit dem Radius über den einer mittleren
    /// Station hinaus (M wie bisher: Waffenreichweite ab Mitte; L +7, XL +15; S −5, Kleinstationen ≈ −12).
    /// </summary>
    public static float ReachOffset(Station st) => st.Radius - Station.BodyRadius;

    /// <summary>Größte Reichweite der Stationsgeschütze ab Mitte (0 = keine Geschütze oder ausgeschaltet).</summary>
    public static float GunRange(Universe u, Station st)
    {
        if (IsDown(u, st) || DefenseOf(u, st) is not { } def) return 0f;
        float r = 0f;
        foreach (var g in def.Guns) if (u.Modules.TryGetValue(g, out var m)) r = MathF.Max(r, m.Stat("range"));
        return r > 0f ? r + ReachOffset(st) : 0f;
    }

    /// <summary>Signatur einer Station (groß, steht still).</summary>
    public const float Signature = 4f;

    public static TargetProfile Profile(Universe u, Station st) => new(Shield(u, st), DefenseOf(u, st)?.Armor ?? 0f, 0f, Signature);

    /// <summary>
    /// Abstand eines Teilnehmers zu einer Station: im räumlichen Gefecht echt; die belagerte Station steht auf der Gefechtslinie bei <see cref="Encounter.StationX"/>;
    /// sonst zählt die liegende Position des Schiffs (außerhalb des Spielersektors bewegt sich nur die Abstandszahl).
    /// </summary>
    public static float Distance(Universe u, Encounter e, CombatParticipant p, Station st)
    {
        if (e.Spatial && p.InSpace) return Vec2.Distance(p.Pos, st.Position);
        if (st.Id == e.Station) return MathF.Abs(p.X - e.StationX);
        return u.Ships.TryGetValue(p.Ship, out var s) ? Vec2.Distance(s.Position, st.Position) : float.MaxValue;
    }

    /// <summary>
    /// Feindlich aus Sicht der Station (rein): Angreifer der belagerten Station; Kriegsgegner ihrer Fraktion; Akteure mit feindlicher Beziehung (Gesetzlose);
    /// der Spieler, wenn er bei der Fraktion gesucht wird (Stufe ≥ <see cref="EconomyRules.TradeRefusalWantedLevel"/>) oder gerade ein Schiff dieser Fraktion angreift.
    /// </summary>
    public static bool Hostile(Universe u, Station st, Encounter e, CombatParticipant p, Ship ship)
    {
        if (u.FactionOf(st) is not { } f) return false;
        if (e.Station == st.Id && p.Side == 0) return true;
        // Phase 31: Angreifer eines Tors, das die Fraktion der Station verteidigt
        if (e.Gate.Value != 0 && p.Side == 0 && u.FactionOf(ship) != f && u.Gates.TryGetValue(e.Gate, out var bg) && GateCombat.DefendersOf(u, bg).Contains(f)) return true;
        // Phase 31 (wie die Kleinstationen in Runde 13b): ein gesuchter Spieler ist auch für Stationen der eigenen Fraktion ein Ziel
        if (ship.IsPlayer && ship.Owner != st.Owner && Wanted.LevelOf(u, ship.Owner, f) >= u.Rules.TradeRefusalWantedLevel) return true;
        if (ship.Owner == st.Owner || u.FactionOf(ship) == f) return false;
        if (Wars.AtWar(u, st.Owner, ship.Owner)) return true;
        if (Relations.Effective(u, st.Owner, ship.Owner) <= u.Rules.HostileRelation) return true;
        if (ship.IsPlayer)
        {
            if (Wanted.LevelOf(u, ship.Owner, f) >= u.Rules.TradeRefusalWantedLevel) return true;
            if (e.Cause == EncounterCause.Assault && p.Side == 0)
                foreach (var q in e.Participants)
                    if (q.Side == 1 && u.FactionOf(q.Owner) == f) return true;
        }
        return false;
    }

    /// <summary>Verteidigt die belagerte Station noch (steht, nicht ausgeschaltet, Besitzer ist noch Gegner der Angreifer)?</summary>
    public static bool Defends(Universe u, Encounter e)
    {
        if (e.Cause != EncounterCause.Siege || !u.Stations.TryGetValue(e.Station, out var st) || IsDown(u, st)) return false;
        // erobert: die Station gehört jetzt einem Angreifer (bzw. seiner Fraktion — der Spieler erobert für sich selbst)
        foreach (var p in e.Participants)
            if (p.Active && p.Side == 0 && u.Ships.TryGetValue(p.Ship, out var s) && (s.Owner == st.Owner || (!s.IsPlayer && u.FactionOf(s) is { } fa && st.Owner == (ActorId)fa))) return false;
        return true;
    }

    /// <summary>
    /// Stationsgeschütze eines Gefechts (je Takt, Stationen des Sektors in Id-Reihenfolge, Geschütze in Listenreihenfolge): Uhr läuft ab, ein bereites Geschütz
    /// nimmt den nächsten feindlichen aktiven Teilnehmer in Reichweite; Raketenwerfer starten eine Rakete (<see cref="Missiles"/>), andere Geschütze schießen
    /// über die Trefferregel (zwei Zufallszüge). Die Station versorgt ihre Geschütze selbst (keine Munition, keine Leistung).
    /// </summary>
    public static void Fire(Universe u, Encounter e, float seconds)
    {
        var r = u.Rules;
        if (!r.StationGunsActive || !u.Sectors.TryGetValue(e.Sector, out var sector)) return;
        foreach (var id in sector.Stations.OrderBy(i => i.Value).ToList())
        {
            if (!u.Stations.TryGetValue(id, out var st) || DefenseOf(u, st) is not { Guns.Count: > 0 } def || IsDown(u, st)) continue;
            for (int g = 0; g < def.Guns.Count; g++)
            {
                if (!u.Modules.TryGetValue(def.Guns[g], out var m)) continue;
                var c = st.Condition;
                float timer = c is not null && g < c.Timers.Count ? c.Timers[g] : 0f;
                if (timer > 0f) { Cond(st).Timers[g] = MathF.Max(0f, timer - seconds); continue; }
                var w = WeaponProfile.Of(m);
                CombatParticipant? target = null;
                float best = float.MaxValue;
                foreach (var p in e.Participants)
                {
                    if (!p.Active || !u.Ships.TryGetValue(p.Ship, out var ship) || !Hostile(u, st, e, p, ship)) continue;
                    float d = Distance(u, e, p, st);
                    if (d <= w.Range + ReachOffset(st) && d < best) { best = d; target = p; }
                }
                if (target is null) continue;
                var cond = Cond(st);
                while (cond.Timers.Count <= g) cond.Timers.Add(0f);
                cond.Timers[g] = w.Reload;
                var tShip = u.Ships[target.Ship];
                if (Missiles.IsMissile(m)) { Missiles.Launch(u, e, default, st, target.Side == 0 ? 1 : 0, m, w, target.Ship, default(SiegeRef), MathF.Max(0f, best - ReachOffset(st)), e.Spatial ? st.Position : default, g); continue; }
                var tProfile = new TargetProfile(tShip.ShieldNow, tShip.Stats.Armor, target.Speed, Gunnery.Signature(u, tShip));
                var hit = Hit.Roll(u.Rng, w, tProfile, MathF.Max(0f, best - ReachOffset(st)), 1f, r);   // Phase 30: ab der Außenhaut
                e.Shots++;
                float hull = Hit.Apply(u, tShip, hit);
                if (hull > 0f) ModuleDamage.OnHullHit(u, tShip, hull);
                if (hit.Hit) e.Hits++;
                u.Events.Publish(new StationShot(e.Id, e.Sector, st.Id, tShip.Id, m.Key, m.DamageType, hit.Hit, hit.ShieldDamage, hull, g));
                if (tShip.Hull <= 0f && target.Active) Engage.Destroy(u, e, target, tShip, $"Stationsgeschütze ({st.Name})");
            }
        }
    }

    /// <summary>
    /// Ein Schiff schießt auf eine Station (Belagerung, Spieler): Trefferregel gegen das Stationsprofil, Schaden über <see cref="Apply"/>. Leistung/Munition hat der
    /// Aufrufer geprüft und entnommen.
    /// </summary>
    public static HitResult Shoot(Universe u, Encounter e, CombatParticipant shooterP, Ship shooter, int slot, Module m, in WeaponProfile w, Station st, float distance, float factor, float reload)
    {
        var r = u.Rules;
        if (w.AmmoPerShot > 0f && r.AmmoActive) shooter.Systems.AmmoMissing = MathF.Min(shooter.Stats.AmmoCapacity, shooter.Systems.AmmoMissing + w.AmmoPerShot);
        var hit = Hit.Roll(u.Rng, w, Profile(u, st), distance, factor, r);
        while (shooterP.Timers.Count <= slot) shooterP.Timers.Add(0f);
        shooterP.Timers[slot] = reload;
        e.Shots++;
        float hull = Apply(u, e, st, hit, shooter.Owner);
        if (hit.Hit) e.Hits++;
        u.Events.Publish(new ShotAtStation(e.Id, e.Sector, shooter.Id, st.Id, m.Key, m.DamageType, hit.Hit, hit.ShieldDamage, hull, slot));
        return hit;
    }

    /// <summary>Treffer auf eine Station anwenden (Schild mit Pause, dann Hülle); Hülle 0 → <see cref="Fall"/>. Liefert den Hüllenschaden.</summary>
    public static float Apply(Universe u, Encounter? e, Station st, in HitResult hit, ActorId attacker)
    {
        if (!hit.Hit || IsDown(u, st)) return 0f;
        var c = Cond(st);
        if (hit.ShieldDamage > 0f) c.ShieldMissing = MathF.Min(MaxShield(u, st), c.ShieldMissing + hit.ShieldDamage);
        // Gerätetest Phase 28 (Runde 5): jeder Treffer hält den Schild an (wie bei Schiffen) — vorher nur Treffer auf den Schild; bei leerem Schild lief die Pause
        // ab und die Station lud ihn mitten im Beschuss wieder auf
        if (MaxShield(u, st) > 0f) c.ShieldPauseUntil = u.Tick + u.Rules.ShieldDelayTicks;
        float before = Hull(u, st);
        c.HullMissing = MathF.Min(MaxHull(u, st), c.HullMissing + hit.HullDamage);
        float dealt = before - Hull(u, st);
        if (Hull(u, st) <= 0f) Fall(u, st, attacker);
        return dealt;
    }

    /// <summary>
    /// Die Station fällt: erobert (Besitz geht an den Angreifer bzw. seine Fraktion, Hülle auf die Wiedereinschalt-Schwelle), zerstört (über
    /// <see cref="Departures"/>) oder ausgeschaltet. Bucht den Verlust im Krieg.
    /// </summary>
    public static void Fall(Universe u, Station st, ActorId attacker)
    {
        var old = st.Owner;
        float value = u.StationTypes.TryGetValue(st.TypeKey, out var t) ? t.BuildCost : 0f;
        Wars.OnLoss(u, old, attacker, value);
        // KI-Angreifer erobern für ihre Fraktion, der Spieler für sich selbst
        bool player = u.Actors.TryGetValue(attacker, out var att) && att.Kind == ActorKind.Player;
        ActorId taker = !player && u.FactionOf(attacker) is { } fa && u.Actors.ContainsKey(fa) ? (ActorId)fa : attacker;
        if (StationProtection.IsCapturable(u, st) && u.Actors.ContainsKey(taker))
        {
            st.Owner = taker;
            var c = Cond(st);
            c.Disabled = false;
            c.HullMissing = MaxHull(u, st) * (1f - u.Rules.StationReenableShare);
            if (u.Sectors.TryGetValue(st.Sector, out var sec)) SectorControl.Update(u, sec);
            u.Events.Publish(new StationFell(st.Id, st.Name, st.Sector, old, taker, "captured"));
            return;
        }
        if (StationProtection.IsDestructible(u, st) && !StationProtection.IsGovernment(u, st))
        {
            u.Events.Publish(new StationFell(st.Id, st.Name, st.Sector, old, taker, "destroyed"));
            Departures.RemoveStation(u, st, DepartureReason.Destroyed, "siege");
            return;
        }
        Cond(st).Disabled = true;
        u.Events.Publish(new StationFell(st.Id, st.Name, st.Sector, old, taker, "disabled"));
    }

    /// <summary>Steht die Station in einer laufenden Belagerung?</summary>
    public static bool UnderSiege(Universe u, StationId id)
    {
        foreach (var e in u.Fights.Encounters) if (e.Station == id && !e.Done) return true;
        return false;
    }
}

/// <summary>
/// Phase 28: Reparatur der Stationen (Kampfmodul, Intervall 50; nur mit <see cref="EconomyRules.Warfare"/>, nur beschädigte Stationen). Der Schild lädt nach der
/// Pause mit <see cref="StationDefenseConfig.ShieldRegen"/> je Sekunde nach. Die Hülle wird außerhalb einer Belagerung bis <see cref="EconomyRules.StationRepairPerDay"/>
/// Punkte je Tag repariert — Materialsenke: <see cref="EconomyRules.StationRepairPlatesPerHull"/> Hüllenplatten je Punkt aus dem eigenen Lager, was fehlt zum
/// Basispreis × <see cref="EconomyRules.StationRepairMarkup"/> aus der Kasse der Besitzerfraktion (sonst der Stationskasse). Ausgeschaltete Stationen schalten ab
/// <see cref="EconomyRules.StationReenableShare"/> wieder ein. Unbeschädigte Stationen verlieren ihren Zustand (null).
/// </summary>
public sealed class StationRepairSystem : ISimSystem
{
    public void Step(Universe u, int dt)
    {
        var r = u.Rules;
        if (!r.Warfare) return;
        float seconds = ShipSystems.Seconds(dt);
        Good? plates = null;
        bool platesLooked = false;
        foreach (var st in u.StationsById())
        {
            if (st.Condition is not { } c) continue;
            if (!platesLooked) { plates = u.FindGood("hull_plates"); platesLooked = true; }   // Nacharbeit: einmal je Schritt, nur wenn eine Station beschädigt ist
            bool sieged = StationCombat.UnderSiege(u, st.Id);
            var def = StationCombat.DefenseOf(u, st);
            // Runde 5: nur die Zeit seit dem Ende der Pause lädt (Intervall 50 Ticks — vorher lud ein Schritt kurz nach der Pause den ganzen Schritt nach)
            float regenSeconds = ShipSystems.Seconds((int)Math.Clamp(u.Tick - c.ShieldPauseUntil, 0L, (long)dt));
            // Runde 7: eine ausgeschaltete Station lädt ihren Schild nicht (die Hülle wird wie bisher außerhalb einer Belagerung repariert)
            if (c.ShieldMissing > 0f && regenSeconds > 0f && def is not null && !c.Disabled)
                c.ShieldMissing = MathF.Max(0f, c.ShieldMissing - def.ShieldRegen * regenSeconds);
            if (!sieged) for (int i = 0; i < c.Timers.Count; i++) c.Timers[i] = 0f;
            if (!sieged && c.HullMissing > 0f) Repair(u, st, c, dt, plates);
            if (c.Disabled && StationCombat.Hull(u, st) >= r.StationReenableShare * StationCombat.MaxHull(u, st)) c.Disabled = false;
            if (c.IsDefault) st.Condition = null;
        }
    }

    static void Repair(Universe u, Station st, StationCondition c, int dt, Good? good)
    {
        var r = u.Rules;
        float points = MathF.Min(c.HullMissing, r.StationRepairPerDay * dt / MathF.Max(1f, r.DayTicks));
        if (points <= 0f) return;
        float plates = points * r.StationRepairPlatesPerHull;
        float fromStock = 0f, cost = 0f;
        if (good is not null && st.Market.TryGetValue(good.Id, out var e) && e.Amount > 0f)
        {
            fromStock = MathF.Min(plates, e.Amount);
            e.Amount -= fromStock;
        }
        float rest = plates - fromStock;
        if (rest > 0f && good is not null)
        {
            float price = good.BasePrice * MathF.Max(0f, r.StationRepairMarkup);
            Treasury? treasury = u.FactionOf(st) is { } f && u.Actors.TryGetValue(f, out var a) ? a.Treasury : null;
            float wallet = treasury?.Credits ?? st.Credits;
            float affordable = price > 0f ? MathF.Min(rest, MathF.Max(0f, wallet) / price) : rest;
            cost = affordable * price;
            if (treasury is not null) treasury.Credits -= cost; else st.Credits -= cost;
            if (cost > 0f && treasury is not null && u.FactionOf(st) is { } ff) u.Events.Publish(new TreasuryUpkeep(ff, "station_repair", cost, 0f));
            points = (fromStock + affordable) / MathF.Max(1e-6f, r.StationRepairPlatesPerHull);
            points = MathF.Min(points, c.HullMissing);
        }
        if (points <= 0f) return;
        c.HullMissing = MathF.Max(0f, c.HullMissing - points);
        u.Events.Publish(new StationRepaired(st.Id, points, fromStock, cost));
    }
}

/// <summary>Phase 28: Anzeigetexte für Krieg und Stationsverteidigung (rein).</summary>
public static class WarInfo
{
    /// <summary>
    /// Gerätetest Phase 28: Gegenwehr, wenn der Spieler diese Station angreift — Geschütze aller Stationen derselben Fraktion im Sektor und ihre größte
    /// Reichweite. Kurzzeile für die Taste „Station angreifen“ („2 Geschütze · 60 m“); leer ohne Geschütze. Alle Abstände der Anzeige gelten ab Stationsmitte
    /// (wie die Entfernung auf der Zielkarte und die Regel der Stationsgeschütze).
    /// </summary>
    public static string DefenseWarning(Universe u, Station target, Ship player)
    {
        var (guns, range) = Guns(u, target);
        return guns == 0 ? "" : $"{guns} Geschütze · {range:0} m";
    }

    /// <summary>
    /// Geschütze (Anzahl, größte Reichweite) der nicht ausgeschalteten Stationen der Fraktion von <paramref name="target"/> im Sektor, die das Ziel decken.
    /// Runde 12 (Handy-Test: „Farm Beta“ zeigte die Geschütze des Sektor-HQ) — nur Stationen, deren Geschützreichweite bis an das Ziel reicht (Abstand der
    /// Mitten ≤ Reichweite + Außenhaut); eine unbewaffnete Station fern jeder Stellung zeigt keine.
    /// </summary>
    public static (int Guns, float Range) Guns(Universe u, Station target)
    {
        if (u.FactionOf(target) is not { } f || !u.Sectors.TryGetValue(target.Sector, out var sector)) return (0, 0f);
        int guns = 0; float range = 0f;
        foreach (var id in sector.Stations)
        {
            if (!u.Stations.TryGetValue(id, out var st) || u.FactionOf(st) != f || StationCombat.DefenseOf(u, st) is not { } def || StationCombat.IsDown(u, st)) continue;
            float reach = StationCombat.GunRange(u, st);
            if (st.Id != target.Id && Vec2.Distance(st.Position, target.Position) > reach + target.Radius) continue;
            foreach (var g in def.Guns)
                if (u.Modules.TryGetValue(g, out var m)) { guns++; range = MathF.Max(range, m.Stat("range") + StationCombat.ReachOffset(st)); }   // Phase 30: ab Außenhaut
        }
        return (guns, range);
    }

    /// <summary>
    /// Gerätetest Phase 28 (Runde 4): Reichweite der eigenen Raketen gegen eine Station ab ihrer Mitte (die Waffe misst bis zur Außenhaut,
    /// <see cref="Station.BodyRadius"/> weiter); 0 = keine Raketen.
    /// </summary>
    public static float MissileReachFromCentre(Universe u, Ship player) => MissileReachFromCentre(u, player, Station.BodyRadius);

    /// <summary>Phase 30: wie oben, mit dem Radius der Zielstation.</summary>
    public static float MissileReachFromCentre(Universe u, Ship player, Station target) => MissileReachFromCentre(u, player, target.Radius);

    static float MissileReachFromCentre(Universe u, Ship player, float radius)
    {
        float own = 0f;
        for (int i = 0; i < player.Fit.Count; i++)
            if (player.Fit[i] is { } k && u.Modules.TryGetValue(k, out var pm) && Missiles.IsMissile(pm) && player.ConditionOf(i) >= u.Rules.ModuleFailCondition) own = MathF.Max(own, pm.Stat("range"));
        return own > 0f ? own + radius : 0f;
    }

    /// <summary>
    /// Gerätetest Phase 28 (Runde 4): Lagezeile beim Stationsangriff — „in Geschützreichweite“ oder „außerhalb der Geschütze“ (Abstand zur Mitte gegen die
    /// Reichweite der Geschütze dieser Station, dieselbe Messung wie ihre Schussregel), dazu die eigene Raketenreichweite ab Mitte. Leer ohne Geschütze.
    /// </summary>
    public static string RangeStatus(Universe u, Station target, Ship player) => RangeStatus(u, target, player, player.Position);

    /// <summary>Runde 7: dieselbe Lagezeile von einer gegebenen Position aus (die Ansicht übergibt die Position, aus der sie auch „Entfernung“ zeigt).</summary>
    public static string RangeStatus(Universe u, Station target, Ship player, Vec2 at)
    {
        float guns = StationCombat.GunRange(u, target);
        string own = OwnGunsText(u, target, player, at);
        string tail = own.Length > 0 ? " · " + own : "";
        if (guns <= 0f) return StationCombat.IsDown(u, target) ? "Geschütze schweigen (ausgeschaltet)" : own;
        float d = Vec2.Distance(at, target.Position);
        float missiles = MissileReachFromCentre(u, player, target);
        if (d <= guns) return $"IN GESCHÜTZREICHWEITE ({guns:0} m){tail}";
        if (missiles <= 0f) return $"außerhalb Geschütze ({guns:0} m){tail}";
        return (d <= missiles ? "außerhalb Geschütze · Raketen ja" : $"außerhalb Geschütze · Raketen ≤{missiles:0} m") + tail;
    }

    /// <summary>
    /// Runde 14 (Handy-Test Kriegstest): Reichen die eigenen Geschütze (ohne Raketen) bis zur Außenhaut? „eigene Geschütze reichen“ bzw. „eigene Geschütze ab
    /// x m Außenhaut“; leer ohne Geschütze.
    /// </summary>
    public static string OwnGunsText(Universe u, Station target, Ship player, Vec2 at)
    {
        float own = 0f;
        for (int i = 0; i < player.Fit.Count; i++)
            if (player.Fit[i] is { } k && u.Modules.TryGetValue(k, out var m) && m.IsWeapon && !Missiles.IsMissile(m) && player.ConditionOf(i) >= u.Rules.ModuleFailCondition) own = MathF.Max(own, m.Stat("range"));
        if (own <= 0f) return "";
        float hull = MathF.Max(0f, Vec2.Distance(at, target.Position) - target.Radius);
        return hull <= own ? "eigene Geschütze reichen" : $"eigene ab {own:0} m Außenhaut";
    }

    /// <summary>Gerätetest Phase 28 (Runde 5): unter diesem Verhältnis eigene Stärke : Verteidigung ist ein Stationsangriff aussichtslos, darunter „knapp“.</summary>
    public const float SiegeHopelessRatio = 1.2f, SiegeTightRatio = 1.6f;

    /// <summary>
    /// Gerätetest Phase 28 (Runde 5; Entscheidung: Stationsangriff nur mit Fregatte oder Flotte): Kräfteverhältnis vor bzw. während eines Angriffs auf
    /// <paramref name="target"/> — eigene Seite = aktives Schiff und wer ihm im Sektor beisteht (<see cref="Reinforcements.Helpers(Universe, SectorId, Ship)"/>:
    /// Begleiter, Verband mit Regel „Verteidigen“/„Angriff“); Verteidigung = jede nicht ausgeschaltete Station der Fraktion im Sektor mit Verteidigungswerten
    /// (Geschütze wie Waffenkraft, Schild ÷ 20, Hülle ÷ 50, Panzerung ÷ 5) und Polizei/Kriegsschiffe der Fraktion dort. Stärke der Schiffe wie
    /// <see cref="CombatRules.Strength(Ship)"/>. Nur eine Lageschätzung (kein Verbot).
    /// </summary>
    public static (float Own, float Defence, int Ships) SiegeOdds(Universe u, Station target, Ship player)
    {
        float own = CombatRules.Strength(player);
        int ships = 1;
        foreach (var (h, _) in Reinforcements.Helpers(u, target.Sector, player))
            if (h.Id != player.Id) { own += CombatRules.Strength(h); ships++; }
        float def = 0f;
        if (u.FactionOf(target) is { } f && u.Sectors.TryGetValue(target.Sector, out var sector))
        {
            foreach (var id in sector.Stations)
                if (u.Stations.TryGetValue(id, out var st) && u.FactionOf(st) == f && StationCombat.DefenseOf(u, st) is { } d && !StationCombat.IsDown(u, st)
                    // Runde 14 (Handy-Test: Satellit „aussichtslos“): nur das Ziel selbst und Stationen, deren Geschütze bis zum Ziel reichen
                    && (st.Id == target.Id || (d.Guns.Count > 0 && Vec2.Distance(st.Position, target.Position) <= StationCombat.GunRange(u, st) + target.Radius)))
                {
                    foreach (var g in d.Guns) if (u.Modules.TryGetValue(g, out var m)) def += ShipStats.WeaponPowerOf(m);
                    def += StationCombat.Shield(u, st) / 20f + StationCombat.Hull(u, st) / 50f + d.Armor / 5f;
                }
            foreach (var id in u.Tracker.ParkedIn(target.Sector).Concat(u.Tracker.TransitIn(target.Sector)).Distinct())
                if (u.Ships.TryGetValue(id, out var s) && (ThreatMap.IsPolice(s) || Wars.IsWarship(s)) && u.FactionOf(s) == f && ThreatMap.Where(u, s) is var (ws, wp) && ws == target.Sector
                    && Vec2.Distance(wp, target.Position) <= SiegeOddsDefenderRange)   // Runde 14: nur Verteidiger in der Nähe des Ziels
                    def += CombatRules.Strength(s);
        }
        return (own, def, ships);
    }

    /// <summary>Runde 14: Polizei/Kriegsschiffe zählen als Verteidiger, wenn sie höchstens so weit vom Ziel entfernt sind.</summary>
    public const float SiegeOddsDefenderRange = 150f;

    /// <summary>Lagezeile für Zielkarte/Taste: „Aussichtslos allein – Fregatte oder Flotte nötig“, „Aussichtslos – Verband zu schwach (80 : 106)“, „Knapp – …“, „Überlegen – …“; leer ohne Verteidigung.</summary>
    public static string SiegeOddsText(Universe u, Station target, Ship player)
    {
        var (own, def, ships) = SiegeOdds(u, target, player);
        if (def <= 0f) return "";
        float ratio = own / def;
        if (ratio < SiegeHopelessRatio)
            return ships == 1 ? "Aussichtslos allein – Fregatte oder Flotte nötig" : $"Aussichtslos – Verband zu schwach ({own:0} : {def:0})";
        return ratio < SiegeTightRatio ? $"Knapp – Stärke {own:0} : {def:0}" : $"Überlegen – Stärke {own:0} : {def:0}";
    }

    /// <summary>Aussichtslos (unter <see cref="SiegeHopelessRatio"/>)?</summary>
    public static bool SiegeHopeless(Universe u, Station target, Ship player)
    {
        var (own, def, _) = SiegeOdds(u, target, player);
        return def > 0f && own / def < SiegeHopelessRatio;
    }

    /// <summary>Verteidigungszeile einer Station: Hülle, Schild, Geschütze, Zustand (ausgeschaltet, belagert).</summary>
    public static string StationLine(Universe u, Station st)
    {
        var def = StationCombat.DefenseOf(u, st);
        string guns = def is { Guns.Count: > 0 } ? $" · {def.Guns.Count} Geschütz{(def.Guns.Count == 1 ? "" : "e")}" : " · keine Geschütze";
        string shield = StationCombat.MaxShield(u, st) > 0f ? $" · Schild {StationCombat.Shield(u, st):0}/{StationCombat.MaxShield(u, st):0}" : "";
        string state = StationCombat.IsDown(u, st) ? " · AUSGESCHALTET" : StationCombat.UnderSiege(u, st.Id) ? " · wird angegriffen" : "";
        return $"Hülle {StationCombat.Hull(u, st):0}/{StationCombat.MaxHull(u, st):0}{shield}{guns}{state}";
    }

    /// <summary>Kurzzeile für die Zielkarte: Hülle und Schild ohne Höchstwerte, Zustand.</summary>
    public static string StationShort(Universe u, Station st)
    {
        string s = $"Hülle {StationCombat.Hull(u, st):0}" + (StationCombat.MaxShield(u, st) > 0f ? $" · Schild {StationCombat.Shield(u, st):0}" : "")
            + (StationCombat.IsDown(u, st) ? " · AUS" : "");
        // Runde 14 (Handy-Test): Kleinstationen mit eigener Kurzdarstellung — Art, Geschütze, nicht andockbar
        if (Simulation.Outposts.IsOutpost(u, st) && u.StationTypes.TryGetValue(st.TypeKey, out var t))
        {
            int guns = t.Defense?.Guns.Count ?? 0;
            return s + (guns > 0 ? $" · {guns} Geschütz{(guns == 1 ? "" : "e")}" : " · Sensor") + " · nicht andockbar";   // Runde 15: die Art steht im Namen
        }
        return s;
    }

    /// <summary>Kriegslage einer Fraktion („Krieg mit …“ oder „Frieden“).</summary>
    public static string FactionLine(Universe u, FactionId f)
    {
        var wars = u.Wars.Where(w => w.Involves(f)).ToList();
        if (wars.Count == 0) return "Frieden";
        return "Krieg mit " + string.Join(", ", wars.Select(w =>
            $"{(u.Factions.TryGetValue(w.Other(f), out var o) ? o.Name : "?")} ({Math.Clamp(w.Weariness, 0f, 1f) * 100f:0} % müde)"));
    }

    /// <summary>Kurzzeile aller laufenden Kriege (leer = keiner).</summary>
    public static string Summary(Universe u) => u.Wars.Count == 0 ? "" :
        "Krieg: " + string.Join(", ", u.Wars.Select(w => $"{(u.Factions.TryGetValue(w.A, out var a) ? a.Name : "?")} – {(u.Factions.TryGetValue(w.B, out var b) ? b.Name : "?")}"));
}
