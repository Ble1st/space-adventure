using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Weapons;

namespace SpaceSim.Core.Combat;

/// <summary>
/// Phase 25: die gemeinsamen Schussregeln für NPC-Gefecht (<see cref="CombatSystem"/>, im Tick-Takt) und Spielerwaffen (<see cref="PlayerGunnery"/>, in Echtzeit):
/// Leistung (Spitzenlast aus der Bank, sonst im Takt des Überschusses; Überladung in der Stellung „Waffen“), Munition, Feuerwinkel eines Slots, und das Buchen
/// eines Schusses über die eine Trefferregel <see cref="Hit.Roll"/>. Ein Schuss zieht genau <see cref="Hit.DrawsPerShot"/> Zahlen aus <see cref="Universe.Rng"/>.
/// </summary>
public static class Gunnery
{
    /// <summary>Waffenmodul eines Slots (null = keine Waffe, Modul ausgefallen oder unbekannt).</summary>
    public static Module? WeaponAt(Universe u, Ship ship, int slot)
    {
        if (slot < 0 || slot >= ship.Fit.Count || ship.Fit[slot] is not { } key || !u.Modules.TryGetValue(key, out var m) || !m.IsWeapon) return null;
        return ship.ConditionOf(slot) < u.Rules.ModuleFailCondition ? null : m;
    }

    /// <summary>Feuerwinkel eines Slots als Schacht (Ausrichtung, halber Öffnungswinkel; ohne Angabe Turm).</summary>
    public static WeaponBay BayOf(Ship ship, int slot, Module m)
    {
        var cs = slot < ship.Slots.Count ? ship.Slots[slot] : null;
        return new WeaponBay
        {
            Key = $"{PlayerGunnery.BayPrefix}{slot}", Name = m.Name, Facing = FacingOf(cs?.Facing), ArcDegrees = cs?.Arc ?? 360f, Range = m.Stat("range"),
        };
    }

    public static Facing FacingOf(string? f) => f switch
    {
        "port" => Facing.Port, "starboard" => Facing.Starboard, "stern" => Facing.Stern, _ => Facing.Bow,
    };

    /// <summary>Reicht die Leistung für einen Schuss (rein, ohne Entnahme)? Bank mit Spitzenlast oder Überschuss mit Waffenanteil.</summary>
    public static bool HasPower(Universe u, Ship ship, in WeaponProfile w)
    {
        if (w.PeakDraw <= 0f || ship.Charge >= w.PeakDraw) return true;
        return ShipSystems.Split(ship.Systems.Mode, u.Rules.PowerFocusShare).Weapons * ShipSystems.Surplus(ship.Stats) > 0f;
    }

    /// <summary>Munition für einen Schuss da (ohne Munitionsbedarf immer)?</summary>
    public static bool HasAmmo(Universe u, Ship ship, in WeaponProfile w) => w.AmmoPerShot <= 0f || ShipSystems.Ammo(u, ship) >= w.AmmoPerShot;

    /// <summary>
    /// Leistung für einen Schuss entnehmen und die Nachladezeit bestimmen: Überladung (Stellung „Waffen“, Speicherbank hält Spitzenlast × (1 + Zuschlag)) →
    /// Bank zahlt den Zuschlag mit, Nachladezeit × <see cref="EconomyRules.OverchargeReloadFactor"/>; sonst Spitzenlast aus der Bank; ist sie leer, feuert die
    /// Waffe im Takt des Überschusses (Waffenanteil). false = keine Leistung (nichts entnommen).
    /// </summary>
    public static bool DrawPower(Universe u, Ship ship, in WeaponProfile w, out float reload)
    {
        var r = u.Rules;
        reload = w.Reload;
        if (w.PeakDraw <= 0f) return true;
        float over = w.PeakDraw * (1f + MathF.Max(0f, r.OverchargeDrawFactor));
        if (ship.Systems.Mode == PowerMode.Weapons && ship.Stats.PowerStorage > 0f && r.OverchargeReloadFactor < 1f && ship.Charge >= over)
        {
            ShipSystems.Drain(u, ship, over);
            reload = w.Reload * MathF.Max(0.05f, r.OverchargeReloadFactor);
            return true;
        }
        if (ship.Charge >= w.PeakDraw) { ShipSystems.Drain(u, ship, w.PeakDraw); return true; }
        float rate = ShipSystems.Split(ship.Systems.Mode, r.PowerFocusShare).Weapons * ShipSystems.Surplus(ship.Stats);
        if (rate <= 0f) return false;
        reload = MathF.Max(reload, w.PeakDraw / rate);
        return true;
    }

    /// <summary>Signatur (Zielgröße) eines Schiffs nach seiner Klassengröße.</summary>
    public static float Signature(Universe u, Ship s) =>
        u.ShipModels.TryGetValue(s.ModelKey, out var mdl) && u.ShipClasses.TryGetValue(mdl.ClassKey, out var cls) ? Hit.Signature(u.Rules, cls.Size) : 1f;

    /// <summary>
    /// Bucht einen Schuss: Munition, Würfel (<see cref="Hit.Roll"/>), Treffer anwenden, Uhr setzen, Zähler, Ereignis <see cref="ShotFired"/>; Hülle 0 → Zerstörung
    /// (<see cref="Engage.Destroy"/>). Leistung hat der Aufrufer schon entnommen (<see cref="DrawPower"/>).
    /// </summary>
    public static HitResult Shoot(Universe u, Encounter e, CombatParticipant shooterP, Ship shooter, int slot, Module m, in WeaponProfile w,
        CombatParticipant targetP, Ship target, float distance, float factor, float reload)
    {
        var r = u.Rules;
        if (w.AmmoPerShot > 0f && r.AmmoActive) shooter.Systems.AmmoMissing = MathF.Min(shooter.Stats.AmmoCapacity, shooter.Systems.AmmoMissing + w.AmmoPerShot);
        var tProfile = new TargetProfile(target.ShieldNow, target.Stats.Armor, targetP.Speed, Signature(u, target));
        var hit = Hit.Roll(u.Rng, w, tProfile, distance, factor, r);
        while (shooterP.Timers.Count <= slot) shooterP.Timers.Add(0f);
        shooterP.Timers[slot] = reload;
        e.Shots++;
        float hull = Hit.Apply(u, target, hit);
        if (hull > 0f) ModuleDamage.OnHullHit(u, target, hull);   // Phase 26: unter 50 % Hülle Modulschaden und Brand (genau drei Züge, siehe ModuleDamage)
        if (hit.Hit) e.Hits++;
        u.Events.Publish(new ShotFired(e.Id, e.Sector, shooter.Id, target.Id, m.Key, m.DamageType, hit.Hit, hit.ShieldDamage, hull, slot));
        if (target.Hull <= 0f && targetP.Active) Engage.Destroy(u, e, targetP, target, CombatSense.Who(u, shooter));   // Runde 5: der letzte Schütze
        return hit;
    }
}
