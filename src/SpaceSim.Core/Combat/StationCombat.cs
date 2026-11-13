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

    public static float MaxHull(Universe u, Station st) => DefenseOf(u, st)?.Hull ?? u.Rules.StationBaseHull;
    public static float MaxShield(Universe u, Station st) => DefenseOf(u, st)?.Shield ?? 0f;
    public static float Hull(Universe u, Station st) => MathF.Max(0f, MaxHull(u, st) - (st.Condition?.HullMissing ?? 0f));
    public static float Shield(Universe u, Station st) => MathF.Max(0f, MaxShield(u, st) - (st.Condition?.ShieldMissing ?? 0f));

    /// <summary>Ausgeschaltet (Hülle 0 einer Station, die nicht fallen darf) — schießt nicht, ist kein Kriegsziel mehr.</summary>
    public static bool IsDown(Universe u, Station st) => st.Condition is { Disabled: true };

    static StationCondition Cond(Station st) => st.Condition ??= new StationCondition();

    /// <summary>Größte Reichweite der Stationsgeschütze (0 = keine Geschütze oder ausgeschaltet).</summary>
    public static float GunRange(Universe u, Station st)
    {
        if (IsDown(u, st) || DefenseOf(u, st) is not { } def) return 0f;
        float r = 0f;
        foreach (var g in def.Guns) if (u.Modules.TryGetValue(g, out var m)) r = MathF.Max(r, m.Stat("range"));
        return r;
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
                    if (d <= w.Range && d < best) { best = d; target = p; }
                }
                if (target is null) continue;
                var cond = Cond(st);
                while (cond.Timers.Count <= g) cond.Timers.Add(0f);
                cond.Timers[g] = w.Reload;
                var tShip = u.Ships[target.Ship];
                if (Missiles.IsMissile(m)) { Missiles.Launch(u, e, default, st, target.Side == 0 ? 1 : 0, m, w, target.Ship, default, best, e.Spatial ? st.Position : default); continue; }
                var tProfile = new TargetProfile(tShip.ShieldNow, tShip.Stats.Armor, target.Speed, Gunnery.Signature(u, tShip));
                var hit = Hit.Roll(u.Rng, w, tProfile, best, 1f, r);
                e.Shots++;
                float hull = Hit.Apply(u, tShip, hit);
                if (hull > 0f) ModuleDamage.OnHullHit(u, tShip, hull);
                if (hit.Hit) e.Hits++;
                u.Events.Publish(new StationShot(e.Id, e.Sector, st.Id, tShip.Id, m.Key, m.DamageType, hit.Hit, hit.ShieldDamage, hull));
                if (tShip.Hull <= 0f && target.Active) Engage.Destroy(u, e, target, tShip);
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
        u.Events.Publish(new ShotAtStation(e.Id, e.Sector, shooter.Id, st.Id, m.Key, m.DamageType, hit.Hit, hit.ShieldDamage, hull));
        return hit;
    }

    /// <summary>Treffer auf eine Station anwenden (Schild mit Pause, dann Hülle); Hülle 0 → <see cref="Fall"/>. Liefert den Hüllenschaden.</summary>
    public static float Apply(Universe u, Encounter? e, Station st, in HitResult hit, ActorId attacker)
    {
        if (!hit.Hit || IsDown(u, st)) return 0f;
        var c = Cond(st);
        if (hit.ShieldDamage > 0f)
        {
            c.ShieldMissing = MathF.Min(MaxShield(u, st), c.ShieldMissing + hit.ShieldDamage);
            c.ShieldPauseUntil = u.Tick + u.Rules.ShieldDelayTicks;
        }
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
            if (c.ShieldMissing > 0f && u.Tick >= c.ShieldPauseUntil && def is not null)
                c.ShieldMissing = MathF.Max(0f, c.ShieldMissing - def.ShieldRegen * seconds);
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
    public static string StationShort(Universe u, Station st) =>
        $"Hülle {StationCombat.Hull(u, st):0}" + (StationCombat.MaxShield(u, st) > 0f ? $" · Schild {StationCombat.Shield(u, st):0}" : "")
        + (StationCombat.IsDown(u, st) ? " · AUS" : "");

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
