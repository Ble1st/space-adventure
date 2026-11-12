using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Combat;

/// <summary>Phase 26: eine Zeile der Schadensliste (Slot, Modul, Zustand, ausgefallen, Brand).</summary>
public readonly record struct DamageRow(int Slot, string Module, string Name, float Condition, bool Failed, bool Burning, string Text);

/// <summary>Phase 26: Schadensliste eines Schiffs für die Anzeige (Schiffsdetail, Ausrüster, Reparatur) — Hülle, Module, Brände, Löschchance. Rein.</summary>
public sealed record DamageReport(float Hull, float MaxHull, IReadOnlyList<DamageRow> Rows, int Fires, float ExtinguishChance, bool Suppressor, string Summary);

/// <summary>Phase 26: reine Anzeige-Funktionen zu Modulschaden und Brand.</summary>
public static class DamageInfo
{
    /// <summary>Schadensliste: je eingebautem Modul eine Zeile (Zustand, ausgefallen, brennt), dazu Brände, Löschchance und eine Zusammenfassung.</summary>
    public static DamageReport For(Universe u, Ship ship)
    {
        var rows = new List<DamageRow>();
        for (int i = 0; i < ship.Fit.Count; i++)
        {
            if (ship.Fit[i] is not { } key) continue;
            string name = u.Modules.TryGetValue(key, out var m) ? m.Name : key;
            float c = ship.ConditionOf(i);
            bool failed = ModuleDamage.Failed(u, ship, i), burning = ship.Systems.IsBurning(i);
            string state = failed ? (ModuleDamage.Flammable(u, ship, i) && m is not null && (m.Stats.ContainsKey("powerOutput") || m.Stats.ContainsKey("thrust"))
                    ? "ausgefallen (halbe Leistung)" : "ausgefallen")
                : c < 0.999f ? "beschädigt" : "intakt";
            rows.Add(new DamageRow(i, key, name, c, failed, burning, $"{name}: {c * 100f:0} % {state}" + (burning ? " · BRENNT" : "")));
        }
        int fires = ship.Systems.Burning.Count;
        float ext = ModuleDamage.ExtinguishChance(u, ship);
        int damaged = rows.Count(r => r.Condition < 0.999f), failedCount = rows.Count(r => r.Failed);
        string summary = $"Hülle {ship.Hull:0}/{ship.MaxHull:0}"
            + (damaged == 0 ? " · Module intakt" : $" · {damaged} Modul(e) beschädigt" + (failedCount > 0 ? $", {failedCount} ausgefallen" : ""))
            + (fires > 0 ? $" · {fires} Brand/Brände, Löschchance {ext * 100f:0} % je Runde" : "");
        return new DamageReport(ship.Hull, ship.MaxHull, rows, fires, ext, ship.Stats.Extinguish > 0f, summary);
    }

    /// <summary>HUD-Warnung „Brand“ (null = kein Brand): z. B. „BRAND: Fusionsreaktor Mk1 · Löschchance 12 %/s“.</summary>
    public static string? FireWarning(Universe u, Ship ship)
    {
        if (ship.Systems.Burning.Count == 0) return null;
        var names = ship.Systems.Burning.Select(s => s < ship.Fit.Count && ship.Fit[s] is { } k && u.Modules.TryGetValue(k, out var m) ? m.Name : "Modul");
        float perSecond = u.Rules.FireRoundTicks > 0 ? 1f / (u.Rules.FireRoundTicks * SimClock.TickInterval) : 0f;
        return $"BRAND: {string.Join(", ", names)} · Löschchance {ModuleDamage.ExtinguishChance(u, ship) * 100f:0} % je Runde"
            + (perSecond > 0f ? $" ({perSecond:0.#}/s)" : "") + " · Andocken löscht";
    }

    /// <summary>Kurzbeschreibung eines Wracks/Behälters (Fracht, bergbare Module, Restzeit).</summary>
    public static string WreckLine(Universe u, Wreck w)
    {
        string cargo = w.Cargo.Count == 0 ? "keine Fracht"
            : string.Join(", ", w.Cargo.OrderBy(k => k.Key.Value).Select(kv => $"{(u.Goods.TryGetValue(kv.Key, out var g) ? g.Name : "?")} {kv.Value:0.#}"));
        string mods = w.Modules.Count == 0 ? "" : " · Module: " + string.Join(", ", w.Modules.Select(m =>
            $"{(u.Modules.TryGetValue(m.Key, out var mm) ? mm.Name : m.Key)} {m.Condition * 100f:0} %{(m.Condition < u.Rules.SalvageMinCondition ? " (Schrott)" : "")}"));
        long left = Math.Max(0, w.ExpiresTick - u.Tick);
        float seconds = left * SimClock.TickInterval;
        return $"{w.Name}: {cargo}{mods} · verfällt in {(int)(seconds / 60):0}:{(int)(seconds % 60):00}";
    }
}
