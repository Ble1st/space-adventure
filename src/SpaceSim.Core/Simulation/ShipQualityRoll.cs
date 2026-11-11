using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>Ergebnis eines Gütewurfs: erreichte Güteklasse, Slotliste und Fit des neuen Schiffs, Materialbedarf an der Werft (null = ohne Material).</summary>
public sealed record QualityOutcome(ShipQuality Rolled, ShipQuality Grade, List<ClassSlot> Slots, List<string?> Fit, IReadOnlyList<MaterialNeed>? Materials);

/// <summary>
/// Güteklassen beim Schiffskauf (Phase 7, SCHIFFSSLOTS.md Abschnitt 8 „Bessere Ausstattung“): Ein Wurf über <see cref="Universe.Rng"/> mit den
/// Chancen <see cref="EconomyRules.QualityChances"/> (Standard 70 %, Verbessert 20 %, Elite 8 %, Meisterwerk 2 %).
/// Verbessert: ein Modul eine Stufe besser (<see cref="Module.UpgradeTo"/>); Elite: zwei bis drei Module besser, sonst ein Slot eine Größe
/// größer; Meisterwerk: Elite plus ein Bonusslot (Kopie des ersten Frachtslots mit demselben Modul), höchstens
/// <see cref="ShipClass.MaxSlotUpgrades"/>. Der Gütegewinn kostet Material: Bessere Module, Bonusmodul und Material der Slotvergrößerung kommen
/// aus dem Werftlager (die ersetzten Grundmodule werden dafür nicht verbraucht). Fehlt etwas, fällt der Wurf auf die nächste Stufe zurück.
/// Gründerschiffe der Startwelt würfeln ohne Material. Gemeinsame Regel für Spielerkauf, KI-Flotte und Startwelt.
/// </summary>
public static class ShipQualityRoll
{
    /// <summary>Würfelt die Güteklasse (genau ein Zufallswert aus <paramref name="rng"/>).</summary>
    public static ShipQuality RollGrade(DeterministicRng rng, IReadOnlyList<float> chances)
    {
        double x = rng.NextDouble();
        double acc = 0;
        for (int i = 0; i < chances.Count && i < 4; i++)
        {
            acc += chances[i];
            if (x < acc) return (ShipQuality)i;
        }
        return ShipQuality.Standard;
    }

    /// <summary>
    /// Würfelt und wendet die Güteklasse an. <paramref name="yard"/> null = ohne Material (Startwelt), sonst muss die Werft den gesamten
    /// Bedarf auf Lager haben: <paramref name="baseNeeds"/> (was der Kauf ohnehin verbraucht: Bausatz, ggf. Module des Fits) abzüglich der
    /// ersetzten Grundmodule plus besserer Module, Bonusmodul und Slot-Material. Ergebnis: tatsächliche Güteklasse, Slots, Fit und Gesamtbedarf.
    /// </summary>
    public static QualityOutcome Roll(Universe u, ShipClass cls, IReadOnlyList<ClassSlot> slots, IReadOnlyList<string?> fit,
        Station? yard, IReadOnlyList<MaterialNeed>? baseNeeds)
    {
        var rolled = RollGrade(u.Rng, u.Rules.QualityChances);
        for (var grade = rolled; grade > ShipQuality.Standard; grade--)
            if (TryApply(u, cls, slots, fit, grade, yard, baseNeeds) is { } done)
                return done with { Rolled = rolled };
        return new QualityOutcome(rolled, ShipQuality.Standard, Copy(slots), new List<string?>(fit), baseNeeds);
    }

    static List<ClassSlot> Copy(IReadOnlyList<ClassSlot> slots) =>
        slots.Select(s => new ClassSlot { Type = s.Type, Size = s.Size, Facing = s.Facing, Arc = s.Arc }).ToList();

    /// <summary>Versucht genau diese Güteklasse; null, wenn Material, Slotgrenze oder Fit-Regel sie nicht zulassen.</summary>
    static QualityOutcome? TryApply(Universe u, ShipClass cls, IReadOnlyList<ClassSlot> baseSlots, IReadOnlyList<string?> baseFit,
        ShipQuality grade, Station? yard, IReadOnlyList<MaterialNeed>? baseNeeds)
    {
        var slots = Copy(baseSlots);
        var fit = new List<string?>(baseFit);
        // Bedarf je Ware: Ausgangspunkt ist der Grundkauf; ersetzte Grundmodule fallen nur weg, wenn der Grundkauf sie verbraucht hätte
        var need = new Dictionary<GoodId, float>();
        if (baseNeeds is not null) foreach (var n in baseNeeds) need[n.Good] = need.GetValueOrDefault(n.Good) + n.Units;
        bool InStock(GoodId g, float units) => yard is null || (yard.Market.TryGetValue(g, out var e) ? MathF.Floor(e.Amount) : 0f) >= need.GetValueOrDefault(g) + units;
        void Take(GoodId g, float units) => need[g] = need.GetValueOrDefault(g) + units;

        // 1) bessere Module: Verbessert 1, Elite/Meisterwerk 2–3 (zufällige Auswahl unter den möglichen, jeweils mit Lagerbestand)
        int wanted = grade == ShipQuality.Improved ? 1 : 2 + u.Rng.NextInt(2);
        var candidates = new List<int>();
        // Phase 13: nur erforschte Ziele (Tech-Stand der Werft; Gründerschiffe ohne Werft nur freie Module)
        var tech = yard is null ? null : Research.TechOf(u, yard);
        for (int i = 0; i < fit.Count; i++)
            if (UpgradeTarget(u, fit[i], slots[i]) is { } t && Research.IsUnlocked(u, tech, UnlockKind.Module, t.Key)) candidates.Add(i);
        int done = 0;
        while (done < wanted && candidates.Count > 0)
        {
            int pick = candidates[u.Rng.NextInt(candidates.Count)];
            candidates.Remove(pick);
            var target = UpgradeTarget(u, fit[pick], slots[pick])!;
            if (u.FindGood(target.Key) is not { } targetGood) continue;
            // das ersetzte Grundmodul verbraucht der Kauf nicht mehr (nur wenn er es überhaupt verbraucht hätte)
            var baseGood = u.FindGood(fit[pick]!);
            bool released = baseGood is not null && need.GetValueOrDefault(baseGood.Id) > 0f;
            if (released) need[baseGood!.Id] -= 1f;
            if (!InStock(targetGood.Id, 1f))
            {
                if (released) need[baseGood!.Id] += 1f;   // besseres Modul fehlt im Lager: Grundmodul bleibt
                continue;
            }
            Take(targetGood.Id, 1f);
            fit[pick] = target.Key;
            done++;
        }

        if (grade == ShipQuality.Improved && done < 1) return null;
        if (grade >= ShipQuality.Elite && done < 2)
        {
            // Elite ohne zwei bessere Module: stattdessen ein Slot eine Größe größer (Material aus dem Lager)
            if (ShipUpgrade.SlotUpgradesUsed(cls, slots) >= cls.MaxSlotUpgrades) return null;
            int slot = Enumerable.Range(0, slots.Count).FirstOrDefault(i => ShipUpgrade.CanEnlarge(u, slots[i]), -1);
            if (slot < 0 || !TakeAll(u, u.Rules.SlotUpgradeCost, InStock, Take)) return null;
            slots[slot].Size += 1;
        }
        if (grade == ShipQuality.Masterwork)
        {
            // zusätzlich ein Bonusslot: Kopie des ersten Frachtslots der Klasse mit demselben Modul (aus dem Lager)
            if (ShipUpgrade.SlotUpgradesUsed(cls, slots) >= cls.MaxSlotUpgrades) return null;
            int first = Enumerable.Range(0, baseSlots.Count).FirstOrDefault(i => baseSlots[i].Type == "cargo" && baseFit[i] is not null, -1);
            if (first < 0 || u.FindGood(baseFit[first]!) is not { } bonusGood || !InStock(bonusGood.Id, 1f)) return null;
            Take(bonusGood.Id, 1f);
            slots.Add(new ClassSlot { Type = baseSlots[first].Type, Size = baseSlots[first].Size });
            fit.Add(baseFit[first]);
        }
        if (FitRules.Check(u.SlotTypes, u.Modules, cls, slots, fit) is not null) return null;
        var needs = yard is null && baseNeeds is null ? null
            : need.Where(kv => kv.Value > 0f).OrderBy(kv => kv.Key.Value).Select(kv => new MaterialNeed(kv.Key, kv.Value)).ToList();
        return new QualityOutcome(grade, grade, slots, fit, needs);
    }

    static bool TakeAll(Universe u, IReadOnlyDictionary<string, float> cost, Func<GoodId, float, bool> inStock, Action<GoodId, float> take)
    {
        var list = new List<(GoodId, float)>();
        foreach (var (key, units) in cost.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (u.FindGood(key) is not { } g || !inStock(g.Id, units)) return false;
            list.Add((g.Id, units));
        }
        foreach (var (g, units) in list) take(g, units);
        return true;
    }

    /// <summary>Besseres Modul für diesen Slot (Ziel der Verbesserung, passt in Art und Größe), sonst null.</summary>
    public static Module? UpgradeTarget(Universe u, string? moduleKey, ClassSlot slot) =>
        moduleKey is not null && u.Modules.TryGetValue(moduleKey, out var m) && m.UpgradeTo is { } to && u.Modules.TryGetValue(to, out var target)
        && target.Slot == slot.Type && target.Size <= slot.Size ? target : null;
}
