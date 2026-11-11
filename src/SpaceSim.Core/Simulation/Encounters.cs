using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Ergebnis einer Begegnung (Phase 11, FLOTTEN.md Abschnitt 7): wer gewann, Stärken und Siegchance (Anzeige, Tests), Hüllenschaden je Seite als Anteil
/// der Maximalhülle (das führende Schiff voll, Helfer zur Hälfte), ob das führende Schiff des Verlierers zerstört wird und welcher Anteil der Fracht
/// beim Sieg des Angreifers den Besitzer wechselt. Was davon gilt (z. B. nie Zerstörung des Spielers), entscheidet der Aufrufer.
/// </summary>
public readonly record struct EncounterOutcome(bool AttackerWon, float AttackerStrength, float DefenderStrength, float WinChance,
    float AttackerDamage, float DefenderDamage, bool LoserDestroyed, float LootFraction);

/// <summary>
/// Auflösung einer Begegnung zwischen zwei Seiten (FLOTTEN.md Abschnitt 7). Die abstrakte Variante (<see cref="AbstractEncounterResolver"/>) würfelt
/// nach Stärke über <see cref="Universe.Rng"/>; ein echtes Kampfsystem im Spielersektor ersetzt später nur diesen Baustein
/// (<see cref="Universe.Encounters"/>), Polizei und Piraten bleiben unverändert.
/// </summary>
public interface IEncounterResolver
{
    /// <summary>Löst eine Begegnung auf. <paramref name="destroyBase"/> = Grundchance, dass der Verlierer zerstört wird (× √Überlegenheit, höchstens 0,5).</summary>
    EncounterOutcome Resolve(Universe u, IReadOnlyList<Ship> attackers, IReadOnlyList<Ship> defenders, float destroyBase);
}

/// <summary>Kampfstärke aus den Modulen (Waffen, Schild, Hülle) — gemeinsame Regel für Auflöser, Anzeige und Tests.</summary>
public static class CombatRules
{
    /// <summary>Stärke eines Schiffs: Waffenstärke + Schild ÷ 20 + aktuelle Hülle ÷ 50 + 0,5 (auch ein Frachter wehrt sich ein wenig).</summary>
    public static float Strength(Ship ship) => ship.Stats.WeaponPower + ship.Stats.Shield / 20f + MathF.Max(0f, ship.Hull) / 50f + 0.5f;

    /// <summary>Summe der Stärken einer Seite.</summary>
    public static float Strength(IReadOnlyList<Ship> side)
    {
        float s = 0f;
        foreach (var sh in side) s += Strength(sh);
        return s;
    }

    /// <summary>Siegchance des Angreifers: A^k ÷ (A^k + D^k) mit k = <see cref="EconomyRules.EncounterExponent"/>.</summary>
    public static float WinChance(EconomyRules r, float attacker, float defender)
    {
        float a = MathF.Pow(MathF.Max(0.01f, attacker), r.EncounterExponent), d = MathF.Pow(MathF.Max(0.01f, defender), r.EncounterExponent);
        return a / (a + d);
    }

    /// <summary>
    /// Wendet Hüllenschaden an: das führende Schiff (Index 0) voll, Helfer zur Hälfte; nie unter 0. <paramref name="minHullShare"/> &gt; 0 hält ein
    /// Schiff über diesem Anteil (Spieler).
    /// </summary>
    public static void Damage(IReadOnlyList<Ship> side, float fraction, Func<Ship, float>? minHullShare = null)
    {
        for (int i = 0; i < side.Count; i++)
        {
            var s = side[i];
            float dmg = fraction * (i == 0 ? 1f : 0.5f) * s.MaxHull;
            float floor = minHullShare is null ? 0f : minHullShare(s) * s.MaxHull;
            s.Hull = MathF.Max(MathF.Min(s.Hull, MathF.Max(floor, s.Hull - dmg)), 0f);
        }
    }
}

/// <summary>
/// Abstrakte Auflösung (FLOTTEN.md Abschnitt 7, „einfache Variante“): Siegchance nach <see cref="CombatRules.WinChance"/>, Schaden und Beute
/// gleichverteilt bis zu den Regelwerten, Zerstörung des Verlierers mit Grundchance × √Überlegenheit. Zieht immer genau fünf Zahlen aus
/// <see cref="Universe.Rng"/> (deterministisch, unabhängig vom Ausgang).
/// </summary>
public sealed class AbstractEncounterResolver : IEncounterResolver
{
    public EncounterOutcome Resolve(Universe u, IReadOnlyList<Ship> attackers, IReadOnlyList<Ship> defenders, float destroyBase)
    {
        var r = u.Rules;
        float a = CombatRules.Strength(attackers), d = CombatRules.Strength(defenders);
        float chance = CombatRules.WinChance(r, a, d);
        double roll = u.Rng.NextDouble(), dmgW = u.Rng.NextDouble(), dmgL = u.Rng.NextDouble(), destroy = u.Rng.NextDouble(), loot = u.Rng.NextDouble();
        bool won = roll < chance;
        float winnerDmg = (float)dmgW * r.WinnerDamage, loserDmg = (float)dmgL * r.LoserDamage;
        float superiority = won ? a / MathF.Max(0.01f, d) : d / MathF.Max(0.01f, a);
        bool destroyed = destroy < MathF.Min(0.5f, destroyBase * MathF.Sqrt(MathF.Max(1f, superiority)));
        float lootFrac = won ? r.LootMin + (float)loot * (r.LootMax - r.LootMin) : 0f;
        return new EncounterOutcome(won, a, d, chance, won ? winnerDmg : loserDmg, won ? loserDmg : winnerDmg, destroyed, lootFrac);
    }
}
