using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Ein mögliches Upgrade für ein Schiff an einer Station (rein berechnet): Art, Slot (−1 = Bonusslot), altes/neues Modul bzw. Slotart und Größe,
/// Erfolgschance inklusive Mitleidszähler, Material, was davon im Lager fehlt, Preis, ob die Kasse reicht, und ein Grund, falls es nicht geht.
/// </summary>
public sealed record UpgradeQuote(UpgradeKind Kind, int Slot, string? FromModule, string? ToModule, string SlotType, SlotSize NewSize,
    float Chance, int Pity, IReadOnlyList<MaterialNeed> Materials, IReadOnlyList<MaterialNeed> Missing, float Price, bool Affordable, string? Issue)
{
    /// <summary>Angebots-Schlüssel des Dienstes „upgrade“: „module:&lt;Slot&gt;“, „slot:&lt;Slot&gt;“ oder „bonus:&lt;Slotart&gt;“.</summary>
    public string Key => Kind switch
    {
        UpgradeKind.Module => $"module:{Slot}",
        UpgradeKind.Slot => $"slot:{Slot}",
        _ => $"bonus:{SlotType}",
    };

    /// <summary>Versuch jetzt möglich: kein Hinderungsgrund, Material vollständig, bezahlbar.</summary>
    public bool Possible => Issue is null && Missing.Count == 0 && Affordable;
}

/// <summary>
/// Upgrades bestehender Schiffe (Phase 7, SCHIFFSSLOTS.md Abschnitt 8 „Upgrades“): gemeinsame Regel für den Dienst „upgrade“ (Spieler) und das
/// <see cref="UpgradeSystem"/> (NPC). Jeder Versuch verbraucht Material aus dem Stationslager und Credits (Materialpreis × (1 + Arbeitsaufschlag),
/// an die Station), auch bei Misserfolg. Erfolgschance nach Stufe (<see cref="EconomyRules.UpgradeChances"/>: Mk1→Mk2 90 %, Mk2→Mk3 60 %,
/// Mk3→Mk4 30 %); Slot-Upgrades (Slot eine Größe größer, Bonusslot) sind selten (<see cref="EconomyRules.SlotUpgradeChance"/>) und je Klasse auf
/// <see cref="ShipClass.MaxSlotUpgrades"/> begrenzt. Jeder Fehlschlag am selben Slot erhöht die nächste Chance um
/// <see cref="EconomyRules.UpgradePityStep"/> (Mitleidszähler <see cref="Ship.UpgradePity"/>, Erfolg setzt ihn zurück). Gewürfelt wird über
/// <see cref="Universe.Rng"/> (wer vorher lädt, bekommt dasselbe Ergebnis).
/// </summary>
public static class ShipUpgrade
{
    /// <summary>Schlüssel des Mitleidszählers für Bonusslots.</summary>
    public const int BonusPityKey = -1;

    /// <summary>Genutzte Slot-Upgrades eines Schiffs: Slots über die Klasse hinaus plus Klassenslots, die größer sind als in der Klasse.</summary>
    public static int SlotUpgradesUsed(ShipClass cls, IReadOnlyList<ClassSlot> slots)
    {
        int used = Math.Max(0, slots.Count - cls.Slots.Count);
        for (int i = 0; i < Math.Min(slots.Count, cls.Slots.Count); i++)
            if (slots[i].Size > cls.Slots[i].Size) used++;
        return used;
    }

    /// <summary>Lässt sich dieser Slot vergrößern (kein Pflichtslot, kleiner als XL)?</summary>
    public static bool CanEnlarge(Universe u, ClassSlot slot) =>
        slot.Size < SlotSize.XL && !(u.SlotTypes.TryGetValue(slot.Type, out var t) && t.Required);

    static ShipClass? ClassOf(Universe u, Ship ship) =>
        ship.HasModel && u.ShipModels.TryGetValue(ship.ModelKey, out var m) ? u.ShipClasses.GetValueOrDefault(m.ClassKey) : null;

    /// <summary>Erfolgschance eines Modul-Upgrades nach Stufe des alten Moduls (ohne Mitleid).</summary>
    public static float BaseChance(EconomyRules rules, int tier)
    {
        var c = rules.UpgradeChances;
        if (c.Count == 0) return 0f;
        return c[Math.Clamp(tier - 1, 0, c.Count - 1)];
    }

    /// <summary>Chance mit Mitleidszähler, auf 1 begrenzt.</summary>
    public static float WithPity(EconomyRules rules, float baseChance, int pity) => MathF.Min(1f, baseChance + rules.UpgradePityStep * pity);

    /// <summary>Alle Upgrades, die dieses Schiff an dieser Station versuchen könnte (auch nicht bezahlbare, mit Grund), in fester Reihenfolge.</summary>
    public static IReadOnlyList<UpgradeQuote> Quotes(Universe u, Station st, Ship ship)
    {
        var list = new List<UpgradeQuote>();
        var cls = ClassOf(u, ship);
        if (cls is null) return list;
        // Module eine Stufe besser
        for (int i = 0; i < ship.Slots.Count && i < ship.Fit.Count; i++)
        {
            if (ship.Fit[i] is not { } key || !u.Modules.TryGetValue(key, out var from)) continue;
            if (ShipQualityRoll.UpgradeTarget(u, key, ship.Slots[i]) is not { } to) continue;
            var fit = new List<string?>(ship.Fit) { [i] = to.Key };
            // Phase 13: Ziel muss für die Fraktion der Station erforscht sein (sichtbar als „nicht erforscht“)
            // Phase 26 (Prüfung): ein beschädigtes oder brennendes Modul erst reparieren — sonst setzte das Upgrade den Zustand gratis auf 1
            string? issue = DamagedIssue(ship, i) is { } dmg ? dmg
                : Research.LockText(u, st, UnlockKind.Module, to.Key) is { } locked ? $"{to.Name} {locked}"
                : FitRules.Check(u.SlotTypes, u.Modules, cls, ship.Slots, fit) is { } fi ? FitRules.Describe(fi) : null;
            list.Add(Make(u, st, ship, UpgradeKind.Module, i, key, to.Key, ship.Slots[i].Type, ship.Slots[i].Size,
                BaseChance(u.Rules, from.Tier), from.UpgradeCost, issue));
        }
        // Slot-Upgrades (selten, begrenzt je Klasse)
        bool room = SlotUpgradesUsed(cls, ship.Slots) < cls.MaxSlotUpgrades;
        string? limit = room ? null : $"Slot-Upgrades ausgeschöpft ({cls.MaxSlotUpgrades} je {cls.Name})";
        for (int i = 0; i < ship.Slots.Count; i++)
            if (CanEnlarge(u, ship.Slots[i]))
                list.Add(Make(u, st, ship, UpgradeKind.Slot, i, null, null, ship.Slots[i].Type, ship.Slots[i].Size + 1,
                    u.Rules.SlotUpgradeChance, u.Rules.SlotUpgradeCost, limit));
        foreach (var type in ship.Slots.Select(s => s.Type).Distinct().Where(t => !(u.SlotTypes.TryGetValue(t, out var stp) && stp.Required)))
            list.Add(Make(u, st, ship, UpgradeKind.Bonus, BonusPityKey, null, null, type, SlotSize.S,
                u.Rules.SlotUpgradeChance, u.Rules.BonusSlotCost, limit));
        return list;
    }

    static UpgradeQuote Make(Universe u, Station st, Ship ship, UpgradeKind kind, int slot, string? from, string? to, string type, SlotSize size,
        float baseChance, IReadOnlyDictionary<string, float> cost, string? issue)
    {
        int pityKey = kind == UpgradeKind.Bonus ? BonusPityKey : slot;
        int pity = ship.UpgradePity.GetValueOrDefault(pityKey);
        var materials = new List<MaterialNeed>();
        foreach (var (key, units) in cost.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (u.FindGood(key) is { } g) materials.Add(new MaterialNeed(g.Id, units));
            else issue ??= $"Ware '{key}' gibt es in dieser Welt nicht";
        }
        var missing = ShipPurchase.Missing(st, materials);
        float price = ShipPurchase.Price(u, st, materials, u.Rules.UpgradeLabor);
        return new UpgradeQuote(kind, slot, from, to, type, size, WithPity(u.Rules, baseChance, pity), pity, materials, missing, price,
            u.WalletOf(ship).Credits >= price, issue);
    }

    /// <summary>Upgrade zum Angebots-Schlüssel (siehe <see cref="UpgradeQuote.Key"/>); null = unbekannt oder hier nicht möglich.</summary>
    public static UpgradeQuote? Quote(Universe u, Station st, Ship ship, string key) => Quotes(u, st, ship).FirstOrDefault(q => q.Key == key);

    /// <summary>
    /// Führt einen Versuch aus (Aufrufer hat <see cref="UpgradeQuote.Possible"/> geprüft): Material aus dem Lager, Preis von der Kasse des Schiffs
    /// (<see cref="Universe.WalletOf"/>) an die Station, dann ein Wurf. Erfolg: Modul getauscht, Slot vergrößert oder Bonusslot (leer) angebaut,
    /// Mitleidszähler zurück; Misserfolg: alles bleibt, Zähler +1. Werte neu berechnet; Ereignis <see cref="UpgradeAttempted"/>.
    /// </summary>
    /// <summary>Phase 26: Grund, warum ein Modul-Upgrade gesperrt ist (Zustand &lt; 1 oder Brand), sonst null.</summary>
    public static string? DamagedIssue(Ship ship, int slot) =>
        slot >= 0 && slot < ship.Fit.Count && ship.Fit[slot] is not null && (ship.ConditionOf(slot) < 0.999f || ship.Systems.IsBurning(slot))
            ? "Modul beschädigt – erst reparieren" : null;

    public static bool Attempt(Universe u, Station st, Ship ship, UpgradeQuote q)
    {
        if (q.Kind == UpgradeKind.Module && DamagedIssue(ship, q.Slot) is not null) return false;   // Phase 26: nichts verbucht
        ShipPurchase.Consume(st, q.Materials);
        var wallet = u.WalletOf(ship);
        wallet.Credits -= q.Price;
        Profitability.Transfer(ship.Profit, -q.Price);   // Phase 22: Investition (nur gemessene KI-Schiffe)
        st.Credits += q.Price;
        bool success = u.Rng.NextDouble() < q.Chance;
        int pityKey = q.Kind == UpgradeKind.Bonus ? BonusPityKey : q.Slot;
        if (success)
        {
            switch (q.Kind)
            {
                case UpgradeKind.Module: ship.Fit[q.Slot] = q.ToModule; break;
                case UpgradeKind.Slot: ship.Slots[q.Slot].Size = q.NewSize; break;
                case UpgradeKind.Bonus:
                    ship.Slots.Add(new ClassSlot { Type = q.SlotType, Size = q.NewSize });
                    ship.Fit.Add(null);
                    break;
            }
            ship.UpgradePity.Remove(pityKey);
            if (q.Kind == UpgradeKind.Module && q.Slot < ship.Systems.Condition.Count)   // Phase 23: das neue Modul ist heil
            {
                ship.Systems.Condition[q.Slot] = 1f;
                if (ship.Systems.AllIntact) ship.Systems.Condition.Clear();
            }
            ship.RecomputeStats(u);
            Combat.ShipSystems.Wake(u, ship);
            ship.CargoCapacity = ship.Stats.Cargo;
            ship.Hull = MathF.Min(ship.Hull, ship.MaxHull);
        }
        else ship.UpgradePity[pityKey] = ship.UpgradePity.GetValueOrDefault(pityKey) + 1;
        u.Events.Publish(new UpgradeAttempted(ship.Id, st.Id, q.Kind, q.Slot, q.FromModule, q.ToModule, success, q.Chance, q.Price));
        return success;
    }
}
