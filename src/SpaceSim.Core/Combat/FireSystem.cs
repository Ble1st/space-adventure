using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Combat;

/// <summary>
/// Phase 26 (KAMPF.md 3.3, Entscheidung 4): führt Brände fort — über den Erholungsindex (<see cref="Universe.Recovering"/>), also ohne Kosten für unbeschädigte
/// Schiffe. Ein angedocktes Schiff löscht sofort („docked“). Jede Brandrunde (<see cref="EconomyRules.FireRoundTicks"/>, nach dem Spieltick) zieht je brennendem
/// Modul genau eine Zahl: unter der Löschchance (<see cref="ModuleDamage.ExtinguishChance"/>) ist der Brand gelöscht, sonst verliert das Modul
/// <see cref="EconomyRules.FireConditionLoss"/> Zustand (fällt es aus, ist der Brand vorbei — „burnt_out“) und die Hülle
/// <see cref="EconomyRules.FireHullDamageShare"/> der Höchsthülle (ein Brand allein zerstört kein Schiff: Hülle bleibt ≥ 1). Brennt auch nach dem Gefecht
/// weiter, bis gelöscht. Keine Besatzung, keine Löschtaste.
/// </summary>
public sealed class FireSystem : ISimSystem
{
    readonly List<int> _buffer = new();

    public void Step(Universe u, int dt)
    {
        var r = u.Rules;
        if (!r.DamageActive || u.Recovering.Count == 0) return;
        int rounds = 0;
        if (r.FireRoundTicks > 0)
            for (long t = u.Tick - dt + 1; t <= u.Tick; t++) if (t % r.FireRoundTicks == 0) rounds++;
        u.Recovering.CopyTo(_buffer);
        foreach (int raw in _buffer)
        {
            if (!u.Ships.TryGetValue(new ShipId(raw), out var ship) || ship.Systems.Burning.Count == 0) continue;
            if (ship.DockedAt.Value != 0) { ModuleDamage.ExtinguishAll(u, ship, "docked"); continue; }
            for (int round = 0; round < rounds && ship.Systems.Burning.Count > 0; round++) Round(u, ship);
        }
    }

    /// <summary>Eine Brandrunde für ein Schiff (Slots aufsteigend, je Slot genau ein Zug).</summary>
    public static void Round(Universe u, Ship ship)
    {
        var r = u.Rules;
        float chance = ModuleDamage.ExtinguishChance(u, ship);
        foreach (int slot in ship.Systems.Burning.ToList())
        {
            double roll = u.Rng.NextDouble();
            if (roll < chance) { ModuleDamage.EndFire(u, ship, slot, "extinguished"); continue; }
            ModuleDamage.Damage(u, ship, slot, r.FireConditionLoss);   // fällt das Modul aus, endet der Brand dort („burnt_out“)
            float dmg = r.FireHullDamageShare * ship.MaxHull;
            if (dmg > 0f && ship.Hull > 1f) ship.Hull = MathF.Max(1f, ship.Hull - dmg);
        }
    }
}
