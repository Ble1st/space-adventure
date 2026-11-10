namespace SpaceSim.Core.Model;

/// <summary>Art eines Verstoßes gegen die Fit-Regeln (<see cref="FitRules.Check"/>).</summary>
public enum FitIssueKind { LengthMismatch, UnknownModule, SlotTypeMismatch, TooLarge, RequiredEmpty, PowerDraw, PeakDraw }

/// <summary>Ein Verstoß gegen die Fit-Regeln: Art, betroffener Slot (-1 = ganzes Schiff) und Meldung ohne Besitzer-Präfix.</summary>
public sealed record FitIssue(FitIssueKind Kind, int Slot, string Message);

/// <summary>
/// Die Regeln eines gültigen Fits (SCHIFFSSLOTS.md, Abschnitt 1) als eine reine Funktion. Gilt für die Content-Prüfung der
/// Schiffsmodelle im <c>WorldBuilder</c> und zur Laufzeit beim Umrüsten (Dienst „outfitter“), damit beide nie auseinanderlaufen:
/// Fit-Länge = Slotanzahl, jedes Modul bekannt, Slotart passt, Modul höchstens so groß wie der Slot, Pflichtslots belegt,
/// Leistungsbilanz Σ powerOutput ≥ Σ powerDraw und Spitzenlast Σ peakDraw − (powerOutput − powerDraw) ≤ Σ powerStorage.
/// </summary>
public static class FitRules
{
    /// <summary>
    /// Prüft einen Fit (Modulschlüssel je Slot, null = leer) gegen eine Slotliste und die Klasse. Gibt den ersten Verstoß in fester
    /// Reihenfolge zurück (Länge, je Slot Modul/Slotart/Größe, Pflichtslots, Leistung, Spitzenlast) oder null, wenn der Fit gültig ist.
    /// </summary>
    public static FitIssue? Check(IReadOnlyDictionary<string, SlotType> slotTypes, IReadOnlyDictionary<string, Module> modules,
        ShipClass cls, IReadOnlyList<ClassSlot> slots, IReadOnlyList<string?> fit)
    {
        if (fit.Count != slots.Count)
            return new FitIssue(FitIssueKind.LengthMismatch, -1, $"Fit hat {fit.Count} Einträge, Klasse '{cls.Key}' hat {slots.Count} Slots");
        var resolved = new List<Module?>(fit.Count);
        for (int i = 0; i < fit.Count; i++)
        {
            var key = fit[i];
            if (key is null) { resolved.Add(null); continue; }
            if (!modules.TryGetValue(key, out var module))
                return new FitIssue(FitIssueKind.UnknownModule, i, $"unbekanntes Modul '{key}'");
            if (SlotIssue(module, slots[i], i) is { } issue) return issue;
            resolved.Add(module);
        }
        for (int i = 0; i < slots.Count; i++)
            if (resolved[i] is null && slotTypes.TryGetValue(slots[i].Type, out var st) && st.Required)
                return new FitIssue(FitIssueKind.RequiredEmpty, i, $"Pflichtslot '{slots[i].Type}' ist leer");
        return PowerIssue(ShipStats.Compute(cls, resolved));
    }

    /// <summary>Passt das Modul in diesen Slot (Art und Größe)? null = ja, sonst der Verstoß.</summary>
    public static FitIssue? SlotIssue(Module module, ClassSlot slot, int index)
    {
        if (module.Slot != slot.Type)
            return new FitIssue(FitIssueKind.SlotTypeMismatch, index, $"Modul '{module.Key}' (Slotart '{module.Slot}') passt nicht in Slot '{slot.Type}'");
        if (module.Size > slot.Size)
            return new FitIssue(FitIssueKind.TooLarge, index, $"Modul '{module.Key}' ({module.Size}) ist größer als der Slot ({slot.Size})");
        return null;
    }

    /// <summary>Leistungsbilanz der berechneten Werte: Dauerlast und Spitzenlast (gepuffert durch die Speicherbank).</summary>
    public static FitIssue? PowerIssue(ShipStats stats)
    {
        if (stats.PowerOutput < stats.PowerDraw)
            return new FitIssue(FitIssueKind.PowerDraw, -1, $"powerOutput {stats.PowerOutput} < powerDraw {stats.PowerDraw}");
        if (stats.PeakDraw - (stats.PowerOutput - stats.PowerDraw) > stats.PowerStorage)
            return new FitIssue(FitIssueKind.PeakDraw, -1,
                $"Spitzenlast {stats.PeakDraw} übersteigt die Speicherbank ({stats.PowerStorage} + Reaktorüberschuss {stats.PowerOutput - stats.PowerDraw})");
        return null;
    }

    /// <summary>Kurztext eines Verstoßes für die Oberfläche (deutsch, ohne Schlüssel).</summary>
    public static string Describe(FitIssue issue) => issue.Kind switch
    {
        FitIssueKind.SlotTypeMismatch => "Falsche Slotart",
        FitIssueKind.TooLarge => "Modul zu groß für den Slot",
        FitIssueKind.RequiredEmpty => "Pflichtslot darf nicht leer bleiben",
        FitIssueKind.PowerDraw => "Reaktorleistung reicht nicht",
        FitIssueKind.PeakDraw => "Speicherbank reicht für die Spitzenlast nicht",
        FitIssueKind.UnknownModule => "Unbekanntes Modul",
        _ => "Ungültiger Fit",
    };
}
