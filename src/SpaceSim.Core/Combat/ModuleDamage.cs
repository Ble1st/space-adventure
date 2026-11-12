using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Combat;

/// <summary>
/// Phase 26 (KAMPF.md 3.3): Modulschaden und Brand. Unter <see cref="EconomyRules.ModuleDamageHullShare"/> der Höchsthülle trifft ein Teil der Hüllentreffer
/// zusätzlich ein zufälliges eingebautes Modul und senkt seinen Zustand; unter <see cref="EconomyRules.ModuleFailCondition"/> fällt es aus
/// (<see cref="ShipStats.Compute(ShipClass, IReadOnlyList{Module?}, IReadOnlyList{float}?, float)"/>: Reaktor und Antrieb nie ganz, sie laufen mit halber
/// Leistung). Ein Modultreffer kann das Modul entzünden (<see cref="FireSystem"/> führt den Brand fort).
/// <para><b>Zufallszüge:</b> Ein Hüllentreffer, der das Schiff unter der Schwelle lässt (Hülle &gt; 0, Schiff mit Modell, Schalter <see cref="EconomyRules.DamageActive"/>),
/// zieht genau <see cref="DrawsPerHullHit"/> = 3 Zahlen (Modultreffer ja/nein, welcher Slot, Entzündung) — unabhängig vom Ausgang; jeder andere Treffer zieht
/// keine. Eine Brandrunde zieht je brennendem Modul genau eine Zahl (Löschen).</para>
/// Gleiche Regel für NPC und Spieler: aufgerufen aus <see cref="Gunnery.Shoot"/>.
/// </summary>
public static class ModuleDamage
{
    /// <summary>Zufallszüge je qualifizierendem Hüllentreffer (fest).</summary>
    public const int DrawsPerHullHit = 3;

    /// <summary>Liegt das Schiff unter der Modulschaden-Schwelle (und kann Modulschaden nehmen)?</summary>
    public static bool Exposed(Universe u, Ship ship) =>
        u.Rules.DamageActive && ship.HasModel && ship.Hull > 0f && ship.Hull < u.Rules.ModuleDamageHullShare * ship.MaxHull;

    /// <summary>
    /// Nach einem Hüllentreffer: unter der Schwelle genau drei Züge (siehe Klassenbeschreibung). Trifft der Wurf ein Modul, sinkt sein Zustand um
    /// <see cref="EconomyRules.ModuleDamageBase"/> + Hüllenschaden ÷ Höchsthülle × <see cref="EconomyRules.ModuleDamageScale"/>; ist es danach nicht ausgefallen
    /// und brennt noch nicht, fängt es mit <see cref="IgniteChance"/> Feuer.
    /// </summary>
    public static void OnHullHit(Universe u, Ship ship, float hullDamage)
    {
        if (hullDamage <= 0f || !Exposed(u, ship)) return;
        var r = u.Rules;
        double hitRoll = u.Rng.NextDouble(), slotRoll = u.Rng.NextDouble(), fireRoll = u.Rng.NextDouble();
        if (hitRoll >= r.ModuleHitChance) return;
        int n = 0;
        for (int i = 0; i < ship.Fit.Count; i++) if (ship.Fit[i] is not null) n++;
        if (n == 0) return;
        int pick = Math.Min(n - 1, (int)(slotRoll * n)), slot = -1;
        for (int i = 0; i < ship.Fit.Count; i++)
            if (ship.Fit[i] is not null && pick-- == 0) { slot = i; break; }
        if (slot < 0) return;
        float loss = r.ModuleDamageBase + hullDamage / MathF.Max(1f, ship.MaxHull) * r.ModuleDamageScale;
        bool failed = Damage(u, ship, slot, loss);
        if (failed || ship.Systems.IsBurning(slot)) return;
        if (fireRoll < IgniteChance(u, ship, slot)) Ignite(u, ship, slot);
    }

    /// <summary>Entzündungschance eines Moduls bei einem Treffer: Grundchance × (1 + (1 − Zustand) × Faktor) × Brennbarkeit (Reaktor, Antrieb, Magazin). Rein.</summary>
    public static float IgniteChance(Universe u, Ship ship, int slot)
    {
        var r = u.Rules;
        float c = r.FireChance * (1f + (1f - ship.ConditionOf(slot)) * r.FireConditionFactor);
        if (Flammable(u, ship, slot)) c *= r.FireFlammableFactor;
        return Math.Clamp(c, 0f, 1f);
    }

    /// <summary>Reaktor, Antrieb und Munitionsmagazin brennen leichter.</summary>
    public static bool Flammable(Universe u, Ship ship, int slot)
    {
        if (slot < 0 || slot >= ship.Fit.Count || ship.Fit[slot] is not { } key || !u.Modules.TryGetValue(key, out var m)) return false;
        return m.Stats.ContainsKey("powerOutput") || m.Stats.ContainsKey("thrust") || m.Stats.ContainsKey("ammoCapacity");
    }

    /// <summary>Löschchance je Runde: Grundwert + Löschanlagen (<see cref="ShipStats.Extinguish"/>), höchstens 1. Rein.</summary>
    public static float ExtinguishChance(Universe u, Ship ship) => Math.Clamp(u.Rules.FireExtinguishChance + ship.Stats.Extinguish, 0f, 1f);

    /// <summary>Ist das Modul ausgefallen?</summary>
    public static bool Failed(Universe u, Ship ship, int slot) =>
        slot >= 0 && slot < ship.Fit.Count && ship.Fit[slot] is not null && ship.ConditionOf(slot) < u.Rules.ModuleFailCondition;

    /// <summary>
    /// Senkt den Zustand eines Moduls (ohne Zufall), rechnet die Schiffswerte neu (Frachtraum folgt), wirft überzählige Fracht als Frachtbehälter ab und
    /// beendet einen Brand, wenn das Modul dabei ausfällt. Ereignis <see cref="ModuleDamaged"/>. Liefert, ob das Modul jetzt ausgefallen ist.
    /// </summary>
    public static bool Damage(Universe u, Ship ship, int slot, float loss)
    {
        if (slot < 0 || slot >= ship.Fit.Count || ship.Fit[slot] is not { } key || loss <= 0f) return Failed(u, ship, slot);
        float before = ship.ConditionOf(slot);
        float after = MathF.Max(0f, before - loss);
        bool wasFailed = before < u.Rules.ModuleFailCondition, failed = after < u.Rules.ModuleFailCondition;
        SetCondition(u, ship, slot, after);
        u.Events.Publish(new ModuleDamaged(ship.Id, slot, key, after, failed && !wasFailed));
        if (failed && ship.Systems.IsBurning(slot)) EndFire(u, ship, slot, "burnt_out");
        return failed;
    }

    /// <summary>Setzt den Zustand (0..1), rechnet Werte und Frachtraum neu und wirft Fracht ab, die nicht mehr passt (Spieler und NPC gleich).</summary>
    public static void SetCondition(Universe u, Ship ship, int slot, float value)
    {
        ShipSystems.SetCondition(u, ship, slot, value);
        if (ship.HasModel) ship.CargoCapacity = ship.Stats.Cargo;
        Spill(u, ship);
    }

    /// <summary>Setzt ein Modul in Brand (nichts, wenn es schon brennt oder leer ist). Erholungsindex angemeldet; Ereignis <see cref="FireStarted"/>.</summary>
    public static void Ignite(Universe u, Ship ship, int slot)
    {
        if (slot < 0 || slot >= ship.Fit.Count || ship.Fit[slot] is not { } key || ship.Systems.IsBurning(slot)) return;
        var b = ship.Systems.Burning;
        int at = 0;
        while (at < b.Count && b[at] < slot) at++;
        b.Insert(at, slot);
        u.Recovering.Add(ship.Id);
        u.Events.Publish(new FireStarted(ship.Id, ship.Sector, slot, key));
    }

    /// <summary>Beendet den Brand eines Moduls (<paramref name="how"/> siehe <see cref="FireEnded"/>).</summary>
    public static void EndFire(Universe u, Ship ship, int slot, string how)
    {
        if (!ship.Systems.Burning.Remove(slot)) return;
        string key = slot < ship.Fit.Count ? ship.Fit[slot] ?? "" : "";
        u.Events.Publish(new FireEnded(ship.Id, slot, key, how));
    }

    /// <summary>Löscht alle Brände (Andocken, Reparatur).</summary>
    public static void ExtinguishAll(Universe u, Ship ship, string how)
    {
        while (ship.Systems.Burning.Count > 0) EndFire(u, ship, ship.Systems.Burning[0], how);
    }

    /// <summary>
    /// Fracht, die nach einem Modulausfall nicht mehr in den Laderaum passt (Frachtform-Regel wie <see cref="Ship.FreeVolumeForForm"/>), fällt — von der höchsten
    /// Waren-Id abwärts — als Frachtbehälter ins All (<see cref="Wrecks.Drop"/>; ohne Schalter <see cref="EconomyRules.DamageActive"/> geht sie verloren). Nie ein
    /// ungültiger Zustand. Liefert die abgeworfenen Einheiten.
    /// </summary>
    public static float Spill(Universe u, Ship ship)
    {
        var dropped = new Dictionary<GoodId, float>();
        for (int guard = 0; guard < 64; guard++)
        {
            var (excess, form) = Excess(u, ship);
            if (excess <= 1e-4f) break;
            GoodId? pick = null;
            foreach (var g in ship.Cargo.Keys)
                if (ship.Cargo[g] > 0f && (form is null || u.Goods[g].Form == form) && (pick is null || g.Value > pick.Value.Value)) pick = g;
            if (pick is not { } good) break;
            float vol = MathF.Max(1e-4f, u.Goods[good].VolumePerUnit);
            float take = MathF.Min(ship.Cargo[good], excess / vol);
            float left = ship.Cargo[good] - take;
            if (left <= 1e-4f) { take = ship.Cargo[good]; ship.Cargo.Remove(good); } else ship.Cargo[good] = left;
            dropped[good] = dropped.GetValueOrDefault(good) + take;
        }
        float units = dropped.Values.Sum();
        if (units > 0f)
        {
            var at = PlayerCombat.PositionOf(u, ship.Id);
            Wrecks.Drop(u, at?.Sector ?? ship.Sector, at?.Pos ?? ship.Position, dropped, ship.Id, "Frachtbehälter");
        }
        return units;
    }

    /// <summary>Überschreitung des Laderaums (Volumen) und die Form, die abgeben muss (null = beliebig). Rein.</summary>
    static (float Excess, GoodForm? Form) Excess(Universe u, Ship ship)
    {
        if (!ship.HasSeparatedHolds) return (ship.UsedVolume(u) - ship.CargoCapacity, null);
        float usedS = ship.UsedVolume(u, GoodForm.Solid), usedL = ship.UsedVolume(u, GoodForm.Liquid);
        var s = ship.Stats;
        float cS = usedS - (s.CargoSolid + s.CargoAny), cL = usedL - (s.CargoLiquid + s.CargoAny), cT = usedS + usedL - (s.CargoSolid + s.CargoLiquid + s.CargoAny);
        if (cS > 1e-4f) return (cS, GoodForm.Solid);
        if (cL > 1e-4f) return (cL, GoodForm.Liquid);
        return (cT, null);
    }
}
