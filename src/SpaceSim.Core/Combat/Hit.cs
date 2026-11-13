using SpaceSim.Core.Model;

namespace SpaceSim.Core.Combat;

/// <summary>Phase 24: Waffenwerte eines Moduls für die Trefferregel (aus <see cref="Module.Stats"/> und <see cref="Module.DamageType"/>).</summary>
public readonly record struct WeaponProfile(string Key, float Damage, DamageType Type, float Reload, float Range, float Accuracy, float Tracking, float AmmoPerShot, float PeakDraw)
{
    public static WeaponProfile Of(Module m) => new(m.Key, m.Stat("damage"), m.DamageType, MathF.Max(0.05f, m.Stat("reload")), m.Stat("range"),
        Math.Clamp(m.Stat("accuracy"), 0f, 1f), Math.Clamp(m.Stat("tracking"), 0f, 1f), MathF.Max(0f, m.Stat("ammoPerShot")), MathF.Max(0f, m.PeakDraw));
}

/// <summary>Phase 24: was die Trefferregel über das Ziel wissen muss (aktueller Schild, Panzerung, Tempo, Größe).</summary>
public readonly record struct TargetProfile(float ShieldNow, float Armor, float Speed, float Signature);

/// <summary>Phase 24: Ergebnis eines Schusses: Treffer, Chance, Schaden am Schild, von der Panzerung abgefangener Schaden, Schaden an der Hülle.</summary>
public readonly record struct HitResult(bool Hit, float Chance, float ShieldDamage, float ArmorAbsorbed, float HullDamage);

/// <summary>
/// Phase 24 (KAMPF.md 2, 3.3, 3.4): die eine Trefferregel für alle Schützen (NPC-Gefecht, ab Phase 25 auch der Spieler). Rein und deterministisch: jeder Wurf
/// zieht genau <see cref="DrawsPerShot"/> Zahlen aus dem übergebenen Zufall (Treffer, Streuung), unabhängig vom Ausgang. Reihenfolge des Schadens: Schild →
/// Panzerung → Hülle (Modulschaden und Brand kommen mit Phase 26).
/// </summary>
public static class Hit
{
    /// <summary>Zufallszüge je Wurf (fest).</summary>
    public const int DrawsPerShot = 2;

    /// <summary>
    /// Trefferchance (rein): Treffsicherheit × Abstandsfaktor (1 − Abfall × (Abstand/Reichweite)², außerhalb der Reichweite 0) × Nachführung
    /// (1 ÷ (1 + max(0, Zieltempo/Bezugstempo ÷ Signatur − tracking))) × <paramref name="shooterFactor"/> (Feuerwinkel, Flucht), begrenzt auf
    /// [<see cref="EconomyRules.HitChanceMin"/>, <see cref="EconomyRules.HitChanceMax"/>]; außerhalb der Reichweite 0.
    /// </summary>
    public static float Chance(in WeaponProfile w, in TargetProfile t, float distance, float shooterFactor, EconomyRules r)
    {
        if (w.Range <= 0f || distance > w.Range) return 0f;
        float ratio = MathF.Max(0f, distance) / w.Range;
        float rangeF = 1f - r.HitRangeFalloff * ratio * ratio;
        float speedTerm = MathF.Max(0f, t.Speed) / MathF.Max(0.01f, r.HitTrackingSpeedRef) / MathF.Max(0.1f, t.Signature);
        float trackF = 1f / (1f + MathF.Max(0f, speedTerm - w.Tracking));
        float c = w.Accuracy * rangeF * trackF * Math.Clamp(shooterFactor, 0f, 1f);
        return Math.Clamp(c, r.HitChanceMin, r.HitChanceMax);
    }

    /// <summary>
    /// Schaden eines Treffers nach Art (rein): Der Schild fängt Schaden × Artfaktor ab, solange er reicht; der Rest (zurückgerechnet) verliert an der Panzerung
    /// Panzerwert × <see cref="EconomyRules.ArmorPerHitShare"/> × Artfaktor; was bleibt, trifft die Hülle.
    /// </summary>
    public static HitResult Damage(float damage, DamageType type, in TargetProfile t, float chance, EconomyRules r)
    {
        float rest = MathF.Max(0f, damage), shieldDmg = 0f, armorAbs = 0f;
        float shieldMult = type switch { DamageType.Energy => r.EnergyVsShield, DamageType.Explosive => r.ExplosiveVsShield, _ => r.KineticVsShield };
        if (t.ShieldNow > 0f && rest > 0f && shieldMult > 0f)
        {
            float eff = rest * shieldMult;
            shieldDmg = MathF.Min(t.ShieldNow, eff);
            rest = (eff - shieldDmg) / shieldMult;
        }
        if (t.Armor > 0f && rest > 0f)
        {
            float armorMult = type switch { DamageType.Energy => r.EnergyVsArmor, DamageType.Explosive => r.ExplosiveVsArmor, _ => r.KineticVsArmor };
            armorAbs = MathF.Min(rest, t.Armor * r.ArmorPerHitShare * armorMult);
            rest -= armorAbs;
        }
        return new HitResult(true, chance, shieldDmg, armorAbs, MathF.Max(0f, rest));
    }

    /// <summary>
    /// Ein Schuss: zieht genau zwei Zahlen (Treffer, Streuung ± <see cref="EconomyRules.HitDamageSpread"/>) und liefert Treffer und Schaden. Rein bis auf
    /// den übergebenen Zufall.
    /// </summary>
    public static HitResult Roll(DeterministicRng rng, in WeaponProfile w, in TargetProfile t, float distance, float shooterFactor, EconomyRules r)
    {
        double roll = rng.NextDouble(), spread = rng.NextDouble();
        float chance = Chance(w, t, distance, shooterFactor, r);
        if (roll >= chance) return new HitResult(false, chance, 0f, 0f, 0f);
        float dmg = w.Damage * (1f + r.HitDamageSpread * (float)(2.0 * spread - 1.0));
        return Damage(dmg, w.Type, t, chance, r);
    }

    /// <summary>Signatur (Zielgröße) einer Klassengröße aus den Regeln.</summary>
    public static float Signature(EconomyRules r, SlotSize size)
    {
        int i = (int)size;
        return r.CombatSignature.Count == 0 ? 1f : r.CombatSignature[Math.Clamp(i, 0, r.CombatSignature.Count - 1)];
    }

    /// <summary>Fester Abschlag für den Feuerwinkel eines Slots: 1 − <see cref="EconomyRules.CombatArcPenalty"/> × (1 − Winkel/360); ohne Angabe Turm (360°).</summary>
    public static float ArcFactor(EconomyRules r, ClassSlot slot)
    {
        float arc = Math.Clamp(slot.Arc ?? 360f, 0f, 360f);
        return 1f - r.CombatArcPenalty * (1f - arc / 360f);
    }

    /// <summary>
    /// Wendet einen Treffer auf ein Schiff an: Schildschaden über <see cref="ShipSystems.TakeShield"/> (Pause, Erholung), Hüllenschaden mit optionalem Boden
    /// (<paramref name="minHull"/> absolut; Spielerschiffe in Phase 24). Liefert den tatsächlich angerichteten Hüllenschaden.
    /// </summary>
    public static float Apply(Universe u, Ship target, in HitResult hit, float minHull = 0f)
    {
        if (!hit.Hit) return 0f;
        if (hit.ShieldDamage > 0f) ShipSystems.TakeShield(u, target, hit.ShieldDamage);
        float before = target.Hull;
        target.Hull = MathF.Max(MathF.Min(target.Hull, MathF.Max(minHull, target.Hull - hit.HullDamage)), 0f);
        return before - target.Hull;
    }
}
