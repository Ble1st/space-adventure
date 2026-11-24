using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;

namespace SpaceSim.Core.Simulation;

/// <summary>Ergebnis von „Bauplan anwenden“ beim Ausrüster: eingebaute Module, Credits (negativ = bezahlt) und Meldung.</summary>
public readonly record struct BlueprintApplyResult(int Installed, int Missing, float Credits, string Message);

/// <summary>
/// Baupläne (eigene Schiffsvarianten, Lücke gegenüber Interstellar Pilot 2; docs/SCHIFFE.md „Baupläne“). Der Spieler speichert die Ausrüstung eines
/// eigenen Schiffs (Modell = Klasse + Werks-Steckplätze, Fit je Werks-Steckplatz) unter einem Namen. Gekauft wird ein Bauplan in der Werft mit der
/// gemeinsamen Kaufregel <see cref="ShipPurchase"/> (<see cref="ShipDealerService"/>, Angebot „blueprint:&lt;Id&gt;“); beim Ausrüster baut
/// <see cref="Apply"/> fehlende Module über die Regeln des <see cref="OutfitterService"/> ein. Reine Spieleraktion — die NPC-Welt bleibt unberührt.
/// </summary>
public static class Blueprints
{
    public const int MaxCount = 20;
    public const int MaxNameLength = 32;

    public static IReadOnlyList<ShipBlueprint> Of(Universe u) =>
        u.Actors.TryGetValue(u.PlayerActor, out var a) ? a.Blueprints : Array.Empty<ShipBlueprint>();

    public static ShipBlueprint? Find(Universe u, int id) => Of(u).FirstOrDefault(b => b.Id == id);

    /// <summary>Fit des Schiffs auf die Werks-Steckplätze seiner Klasse gekürzt (Zusatzsteckplätze aus Upgrades fallen weg); null ohne Modell/Klasse.</summary>
    public static List<string?>? ClassFit(Universe u, Ship ship, out ShipClass? cls)
    {
        cls = null;
        if (!ship.HasModel || !u.ShipModels.TryGetValue(ship.ModelKey, out var m) || !u.ShipClasses.TryGetValue(m.ClassKey, out cls)) return null;
        var fit = new List<string?>(cls.Slots.Count);
        for (int i = 0; i < cls.Slots.Count; i++) fit.Add(i < ship.Fit.Count ? ship.Fit[i] : null);
        return fit;
    }

    /// <summary>Darf dieses Schiff unter diesem Namen als Bauplan gespeichert werden? null = ja, sonst Grund (rein).</summary>
    public static string? SaveIssue(Universe u, Ship ship, string name)
    {
        if (ship.Owner != u.PlayerActor) return "Nur eigene Schiffe.";
        string n = (name ?? "").Trim();
        if (n.Length == 0) return "Name fehlt.";
        if (n.Length > MaxNameLength) return $"Name zu lang (höchstens {MaxNameLength} Zeichen).";
        var list = Of(u);
        bool replace = list.Any(b => string.Equals(b.Name, n, StringComparison.OrdinalIgnoreCase));
        if (!replace && list.Count >= MaxCount) return $"Höchstens {MaxCount} Baupläne.";
        var fit = ClassFit(u, ship, out var cls);
        if (fit is null || cls is null) return "Schiff ohne Modell.";
        if (FitRules.Check(u.SlotTypes, u.Modules, cls, cls.Slots, fit) is { } issue)
            return $"Ausrüstung passt nicht auf die Werks-Steckplätze: {FitRules.Describe(issue)}.";
        return null;
    }

    /// <summary>Speichert (oder ersetzt gleichnamig) den Bauplan; null bei Sperrgrund. Veröffentlicht <see cref="BlueprintSaved"/>.</summary>
    public static ShipBlueprint? Save(Universe u, Ship ship, string name, out string message)
    {
        if (SaveIssue(u, ship, name) is { } issue) { message = issue; return null; }
        string n = name.Trim();
        var list = u.Player.Blueprints;
        var fit = ClassFit(u, ship, out _)!;
        var bp = list.FirstOrDefault(b => string.Equals(b.Name, n, StringComparison.OrdinalIgnoreCase));
        if (bp is null)
        {
            bp = new ShipBlueprint { Id = list.Count == 0 ? 1 : list.Max(b => b.Id) + 1 };
            list.Add(bp);
        }
        bp.Name = n; bp.ModelKey = ship.ModelKey; bp.Fit = fit; bp.Created = u.Tick;
        bool dropped = ship.Fit.Count > fit.Count && ship.Fit.Skip(fit.Count).Any(k => k is not null);
        message = $"Bauplan „{n}“ gespeichert" + (dropped ? " (Module in Zusatzsteckplätzen nicht übernommen)." : ".");
        u.Events.Publish(new BlueprintSaved(bp.Id, n, ship.ModelKey));
        return bp;
    }

    /// <summary>Löscht einen Bauplan; false = unbekannt (nichts geändert).</summary>
    public static bool Delete(Universe u, int id)
    {
        if (!u.Actors.TryGetValue(u.PlayerActor, out var a)) return false;
        int i = a.Blueprints.FindIndex(b => b.Id == id);
        if (i < 0) return false;
        a.Blueprints.RemoveAt(i);
        u.Events.Publish(new BlueprintDeleted(id));
        return true;
    }

    /// <summary>Anzeige: Modellname und Module eines Bauplans.</summary>
    public static string Describe(Universe u, ShipBlueprint bp)
    {
        string model = u.ShipModels.TryGetValue(bp.ModelKey, out var m) ? m.Name : bp.ModelKey;
        var mods = bp.Fit.Where(k => k is not null).GroupBy(k => k!).Select(g => (u.Modules.TryGetValue(g.Key, out var mm) ? mm.Name : g.Key) + (g.Count() > 1 ? $" ×{g.Count()}" : ""));
        return $"{model}: {string.Join(", ", mods)}";
    }

    /// <summary>Welche Steckplätze ein Bauplan an diesem Schiff ändern würde (gleiches Modell nötig); null = anderer Schiffstyp.</summary>
    public static List<int>? Differences(Universe u, Ship ship, ShipBlueprint bp)
    {
        if (ship.ModelKey != bp.ModelKey) return null;
        var diff = new List<int>();
        for (int i = 0; i < bp.Fit.Count && i < ship.Fit.Count; i++)
            if (bp.Fit[i] is { } k && ship.Fit[i] != k) diff.Add(i);
        return diff;
    }

    /// <summary>Kann der Bauplan hier angewendet werden? null = ja (mindestens ein Unterschied), sonst Grund (rein).</summary>
    public static string? ApplyIssue(Universe u, Station station, Ship ship, ShipBlueprint bp)
    {
        if (ship.Owner != u.PlayerActor) return "Nur eigene Schiffe.";
        if (!new OutfitterService().IsAvailable(u, station)) return "Hier gibt es keinen Ausrüster.";
        var diff = Differences(u, ship, bp);
        if (diff is null) return "Bauplan gilt für ein anderes Modell.";
        if (diff.Count == 0) return "Schiff entspricht schon dem Bauplan.";
        return null;
    }

    /// <summary>
    /// Bauplan anwenden: baut je abweichendem Steckplatz das Modul des Bauplans über <see cref="OutfitterService.Execute"/> ein (aus der Fracht oder
    /// dem Stationslager, Preise, Rückkaufdeckel, Fit- und Frachtregel wie beim Einzeleinbau). Zwei Durchgänge, weil ein Tausch erst nach einem anderen
    /// (z. B. stärkerer Reaktor) die Leistungsbilanz hält. Was nicht vorrätig oder nicht gültig ist, bleibt wie es war.
    /// </summary>
    public static BlueprintApplyResult Apply(Universe u, Station station, Ship ship, ShipBlueprint bp)
    {
        if (ApplyIssue(u, station, ship, bp) is { } issue) return new BlueprintApplyResult(0, 0, 0f, issue);
        var svc = new OutfitterService();
        int installed = 0;
        float credits = 0f;
        string? lastFail = null;
        for (int pass = 0; pass < 2; pass++)
            foreach (int i in Differences(u, ship, bp)!)
            {
                var r = svc.Execute(u, station, ship, new ServiceRequest($"{OutfitterService.Fit}:{i}:{bp.Fit[i]}", default, 0f));
                if (r.Success) { installed++; credits += r.Credits; }
                else lastFail = r.Message;
            }
        int missing = Differences(u, ship, bp)!.Count;
        string msg = installed == 0
            ? $"Nichts eingebaut: {lastFail ?? "kein Modul verfügbar."}"
            : $"Bauplan „{bp.Name}“: {installed} Modul{(installed == 1 ? "" : "e")} eingebaut ({-credits:0} Cr)" + (missing > 0 ? $", {missing} fehlen noch ({lastFail})" : ".");
        if (installed > 0) u.Events.Publish(new BlueprintApplied(ship.Id, station.Id, bp.Id, installed, missing, credits));
        return new BlueprintApplyResult(installed, missing, credits, msg);
    }
}
