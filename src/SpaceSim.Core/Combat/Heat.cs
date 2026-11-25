using SpaceSim.Core.Events;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Combat;

/// <summary>
/// Hitze (KAMPF.md „Hitze und Wärmeabfuhr“), reine Regeln und die wenigen Schreibstellen. Alles hinter <see cref="EconomyRules.ShipHeat"/> (aus = keine Hitze).
/// <list type="bullet">
/// <item>Quellen: jeder Schuss (Modulwert <c>heat</c>, sonst Schaden × Faktor nach Schadensart — Laser heiß, Massetreiber mäßig, Raketen kühl), Leistungsstellung
/// „Antrieb“, Schub (Spielerflug über die Ansicht, NPC im Gefecht über ihr Tempo), Sprungantrieb beim Laden, Tarnfeld und Störsender (je Lastpunkt) und die
/// Umgebung (<see cref="Environment"/> — Haken für Sonnen, noch 0).</item>
/// <item>Abfuhr: Grundabfuhr der Klassengröße + Kühlermodule (<c>heatDissipation</c>).</item>
/// <item>Wirkung: ab <see cref="EconomyRules.HeatSlowShare"/> laden Waffen langsamer; ab <see cref="EconomyRules.HeatLockShare"/> Sperre (Waffen schweigen, Schild
/// lädt nicht) bis unter <see cref="EconomyRules.HeatResumeShare"/>; bei 100 % Hüllenschaden je Sekunde (nie unter 1 Hülle) und je Hitzerunde genau ein Zufallszug
/// für Modulschaden.</item>
/// </list>
/// </summary>
public static class Heat
{
    public static bool Active(Universe u) => u.Rules.ShipHeat;

    static int SizeIndex(Universe u, Ship ship) =>
        u.ShipModels.TryGetValue(ship.ModelKey, out var mdl) && u.ShipClasses.TryGetValue(mdl.ClassKey, out var cls) ? (int)cls.Size : 0;

    static float BySize(List<float> list, int i) => list.Count == 0 ? 0f : list[Math.Clamp(i, 0, list.Count - 1)];

    /// <summary>Wärmekapazität (Hitze bei 100 %) nach Klassengröße.</summary>
    public static float Capacity(Universe u, Ship ship) => MathF.Max(1f, BySize(u.Rules.HeatCapacityBySize, SizeIndex(u, ship)));

    /// <summary>Grundabfuhr je Sekunde aus der Klassengröße.</summary>
    public static float BaseDissipation(Universe u, Ship ship) => MathF.Max(0f, BySize(u.Rules.HeatDissipationBySize, SizeIndex(u, ship)));

    /// <summary>Gesamte Abfuhr je Sekunde: Grundabfuhr + Kühlermodule.</summary>
    public static float Dissipation(Universe u, Ship ship) => BaseDissipation(u, ship) + MathF.Max(0f, ship.Stats.HeatDissipation);

    /// <summary>Hitzeanteil 0..1 (Schalter aus: 0).</summary>
    public static float Share(Universe u, Ship ship) => !Active(u) ? 0f : Math.Clamp(ship.Systems.Heat / Capacity(u, ship), 0f, 1f);

    /// <summary>Hitze eines Schusses (Modulwert <c>heat</c> überschreibt; sonst Schaden × Faktor nach Schadensart).</summary>
    public static float ShotHeat(Universe u, in WeaponProfile w)
    {
        if (u.Modules.TryGetValue(w.Key, out var m) && m.Stats.TryGetValue("heat", out var h)) return MathF.Max(0f, h);
        var r = u.Rules;
        float f = w.Type switch { DamageType.Kinetic => r.HeatPerDamageKinetic, DamageType.Explosive => r.HeatPerDamageExplosive, _ => r.HeatPerDamageEnergy };
        return MathF.Max(0f, w.Damage * f);
    }

    /// <summary>Haken für die Umgebung (Sonnen, später): zusätzliche Hitze je Sekunde. Noch 0.</summary>
    public static float Environment(Universe u, Ship ship) => 0f;

    /// <summary>Dauerquellen je Sekunde ohne Schub (Leistungsstellung „Antrieb“, Sprungantrieb, Tarnfeld, Störsender, Umgebung). Rein.</summary>
    public static float SourceRate(Universe u, Ship ship)
    {
        var r = u.Rules;
        float rate = 0f;
        if (ship.Systems.Mode == PowerMode.Engine) rate += r.HeatEngineMode;
        if (JumpDrive.Charging(ship)) rate += r.HeatJumpCharge;
        if (ship.Cloaked && ship.Stats.Cloak > 0f) rate += r.HeatPerDraw * ship.Stats.CloakDraw;
        if (Countermeasures.JammerWorks(u, ship)) rate += r.HeatPerDraw * ship.Stats.JamDraw;
        return rate + Environment(u, ship);
    }

    /// <summary>Schub eines NPC im Gefecht je Sekunde (Tempoanteil × <see cref="EconomyRules.HeatThrustFull"/>); das aktive Spielerschiff bucht die Ansicht.</summary>
    static float CombatThrust(Universe u, Ship ship)
    {
        if (ship.Id == u.PlayerShip || u.Fights.Of(ship.Id) is not { } e || e.Of(ship.Id) is not { Active: true } p || p.Speed <= 0f) return 0f;
        float max = MathF.Max(0.1f, ship.Stats.Speed > 0f ? ship.Stats.Speed : 8f);
        return u.Rules.HeatThrustFull * Math.Clamp(p.Speed / max, 0f, 1f);
    }

    /// <summary>Hitze zufügen (geklemmt auf die Kapazität), Sperre nachführen. Schalter aus: nichts.</summary>
    public static void Add(Universe u, Ship ship, float amount)
    {
        if (!Active(u) || amount <= 0f) return;
        float cap = Capacity(u, ship);
        ship.Systems.Heat = MathF.Min(cap, ship.Systems.Heat + amount);
        UpdateLock(u, ship, cap);
    }

    /// <summary>Spielerflug: die Ansicht bucht den Schub (Reglerstellung 0..1) für <paramref name="seconds"/> Echtzeit.</summary>
    public static void AddThrust(Universe u, Ship ship, float throttle, float seconds) =>
        Add(u, ship, u.Rules.HeatThrustFull * Math.Clamp(throttle, 0f, 1f) * MathF.Max(0f, seconds));

    static void UpdateLock(Universe u, Ship ship, float cap)
    {
        var r = u.Rules;
        bool was = ship.Systems.HeatLocked;
        if (ship.Systems.Heat >= r.HeatLockShare * cap - 1e-4f) ship.Systems.HeatLocked = true;
        else if (ship.Systems.Heat < r.HeatResumeShare * cap) ship.Systems.HeatLocked = false;
        if (was != ship.Systems.HeatLocked) u.Events.Publish(new HeatLockChanged(ship.Id, ship.Sector, ship.Systems.HeatLocked));
    }

    /// <summary>Waffen gesperrt, Schild lädt nicht (Hitzesperre)?</summary>
    public static bool Locked(Universe u, Ship ship) => Active(u) && ship.Systems.HeatLocked;

    /// <summary>Nachladefaktor: ab <see cref="EconomyRules.HeatSlowShare"/> × (1 + <see cref="EconomyRules.HeatReloadPenalty"/>), sonst 1.</summary>
    public static float ReloadFactor(Universe u, Ship ship) =>
        Active(u) && Share(u, ship) >= u.Rules.HeatSlowShare - 1e-5f ? 1f + MathF.Max(0f, u.Rules.HeatReloadPenalty) : 1f;

    /// <summary>Faktor auf die Schildregeneration: 0 unter Hitzesperre, sonst 1.</summary>
    public static float ShieldRegenFactor(Universe u, Ship ship) => Locked(u, ship) ? 0f : 1f;

    /// <summary>NPC-Taktik: Feuer zurückhalten, wenn gesperrt oder der Schuss über <see cref="EconomyRules.HeatNpcHoldShare"/> brächte. Rein.</summary>
    public static bool NpcHolds(Universe u, Ship ship, in WeaponProfile w)
    {
        if (!Active(u)) return false;
        if (ship.Systems.HeatLocked) return true;
        float cap = Capacity(u, ship);
        return (ship.Systems.Heat + ShotHeat(u, w)) / cap > u.Rules.HeatNpcHoldShare;
    }

    /// <summary>Infrarotraketen: Zuschlag auf den Wärmefaktor eines heißen Ziels (Hitzeanteil × <see cref="EconomyRules.HeatIrBonus"/>).</summary>
    public static float IrBonus(Universe u, Ship target) => Active(u) ? Share(u, target) * MathF.Max(0f, u.Rules.HeatIrBonus) : 0f;

    /// <summary>
    /// Schreibt ein Schiff um <paramref name="seconds"/> fort (Quellen − Abfuhr), mit <paramref name="rounds"/> Hitzerunden. Bei 100 % Hüllenschaden und je Runde ein Zug
    /// für Modulschaden. Liefert, ob das Schiff noch Hitze hat oder eine Quelle läuft.
    /// </summary>
    public static void Step(Universe u, Ship ship, float seconds, int rounds)
    {
        var r = u.Rules;
        float cap = Capacity(u, ship);
        bool full = ship.Systems.Heat >= cap - 1e-3f;   // war die Runde über voll (Stand zu Beginn des Schritts)?
        float rate = SourceRate(u, ship) + CombatThrust(u, ship) - Dissipation(u, ship);
        ship.Systems.Heat = Math.Clamp(ship.Systems.Heat + rate * seconds, 0f, cap);
        if (ship.Systems.Heat <= 1e-4f) ship.Systems.Heat = 0f;
        UpdateLock(u, ship, cap);
        if (!full && ship.Systems.Heat < cap - 1e-3f) return;
        // 100 %: Hülle leidet (nie unter 1), je Runde genau ein Zug für Modulschaden
        if (ship.Hull > 1f) ship.Hull = MathF.Max(1f, ship.Hull - ship.MaxHull * r.HeatHullPerSecond * seconds);
        for (int i = 0; i < Math.Max(1, rounds); i++)
        {
            double roll = u.Rng.NextDouble();
            float chance = Math.Clamp(r.HeatModuleChance, 0f, 1f);
            if (roll >= chance || chance <= 0f) continue;
            int fitted = 0;
            for (int s = 0; s < ship.Fit.Count; s++) if (ship.Fit[s] is not null) fitted++;
            if (fitted == 0) continue;
            int pick = Math.Min(fitted - 1, (int)(roll / chance * fitted));
            for (int s = 0; s < ship.Fit.Count; s++)
            {
                if (ship.Fit[s] is null) continue;
                if (pick-- == 0) { ModuleDamage.Damage(u, ship, s, r.HeatModuleLoss); break; }
            }
            u.Events.Publish(new HeatDamage(ship.Id, ship.Sector));
        }
    }

    /// <summary>Braucht das Schiff einen Hitzeschritt (Hitze da oder eine Quelle läuft)?</summary>
    public static bool NeedsStep(Universe u, Ship ship) =>
        ship.Systems.Heat > 0f || ship.Systems.HeatLocked || SourceRate(u, ship) > 0f || CombatThrust(u, ship) > 0f;

    /// <summary>Anzeige: „Hitze 72 %“ mit Zustand (langsam/gesperrt/kritisch).</summary>
    public static string Text(Universe u, Ship ship)
    {
        if (!Active(u)) return "";
        float sh = Share(u, ship);
        string state = sh >= 1f - 1e-3f ? " · KRITISCH" : ship.Systems.HeatLocked ? " · Waffen gesperrt" : sh >= u.Rules.HeatSlowShare ? " · Nachladen langsamer" : "";
        return $"Hitze {sh * 100f:0} %{state}";
    }

    /// <summary>Warnzeile für das Flug-HUD (leer = keine Warnung).</summary>
    public static string Warning(Universe u, Ship ship)
    {
        if (!Active(u)) return "";
        float sh = Share(u, ship);
        if (sh >= 1f - 1e-3f) return "ÜBERHITZT! Hülle und Module nehmen Schaden — Feuer einstellen";
        if (ship.Systems.HeatLocked) return $"HITZE {sh * 100f:0} % — Waffen gesperrt bis {u.Rules.HeatResumeShare * 100f:0} %, Schild lädt nicht";
        if (sh >= u.Rules.HeatSlowShare) return $"Hitze {sh * 100f:0} % — Waffen laden langsamer";
        return "";
    }

    /// <summary>Werte für Schiffsmenü und Ausrüster: Kapazität, Abfuhr (Grund + Kühler).</summary>
    public static string StatsLine(Universe u, Ship ship) =>
        !Active(u) ? "" : $"Wärme: Kapazität {Capacity(u, ship):0} · Abfuhr {Dissipation(u, ship):0.#}/s (Klasse {BaseDissipation(u, ship):0.#} + Kühler {ship.Stats.HeatDissipation:0.#})";
}

/// <summary>Hitze: schreibt die Hitze aller Schiffe fort, die Hitze haben oder eine Quelle laufen lassen (feste Id-Reihenfolge, skaliert mit dt).</summary>
public sealed class HeatSystem : ISimSystem
{
    public const int Interval = 4;

    public void Step(Universe u, int dt)
    {
        if (!Heat.Active(u)) return;
        float seconds = ShipSystems.Seconds(dt);
        int rounds = Math.Max(1, dt / Math.Max(1, u.Rules.HeatRoundTicks));
        foreach (var ship in u.ShipsById())
            if (Heat.NeedsStep(u, ship)) Heat.Step(u, ship, seconds, rounds);
    }
}
