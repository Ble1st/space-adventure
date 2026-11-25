using SpaceSim.Core.Model;

namespace SpaceSim.Core.Help;

/// <summary>Bekannte Prüfarten der Tutorialschritte.</summary>
public static class TutorialChecks
{
    public const string Near = "near", Dock = "dock", Buy = "buy", Undock = "undock", Mine = "mine", Sell = "sell", Command = "command", Help = "help";
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { Near, Dock, Buy, Undock, Mine, Sell, Command, Help };
}

/// <summary>
/// Laufendes Tutorial (Startpaket „Tutorial“): aktueller Schritt, die Tutorialstation und die Zählerstände zu Beginn des Schritts (Grundlage für „etwas
/// gekauft/verkauft/abgebaut“). Gespeichert additiv, nie im Hash — reine Anleitung, kein System liest es.
/// </summary>
public sealed class TutorialState
{
    public int Step;
    public StationId Station;
    public int BasePurchases, BaseSales;
    public double BaseOre;
    public bool Done;
}

/// <summary>Was nur die Ansicht weiß: Kommandozentrale bzw. Hilfe gerade offen.</summary>
public readonly record struct TutorialView(bool CommandOpen = false, bool HelpOpen = false);

/// <summary>Ergebnis eines Fortschritts: der erledigte Schritt (Index), der neue Schritt (−1 = fertig) und ob das Tutorial damit beendet ist.</summary>
public readonly record struct TutorialAdvance(int Completed, int Next, bool Finished);

/// <summary>
/// Tutorial-Schrittprüfer (rein): erkennt den Fortschritt aus dem Universum (Andockplatz, Abstand, Spielerstatistik) und der <see cref="TutorialView"/>.
/// Die Ansicht ruft <see cref="Update"/> regelmäßig; je Aufruf höchstens ein Schritt. Die NPC-Simulation bleibt unberührt.
/// </summary>
public static class TutorialRules
{
    /// <summary>„Fliege zur Station“: erreicht innerhalb dieses Abstands zum Stationsrand (oder angedockt).</summary>
    public const float NearDistance = 60f;

    public static IReadOnlyList<TutorialStepDef> Steps => HelpData.Default.Tutorial;

    /// <summary>Startet das Tutorial mit dieser Station (Schritt 0, Zählerstände von jetzt).</summary>
    public static TutorialState Start(Universe u, StationId station)
    {
        var s = new TutorialState { Station = station };
        Baseline(u, s);
        u.Tutorial = s;
        return s;
    }

    static void Baseline(Universe u, TutorialState s)
    {
        var st = u.Player.Stats;
        s.BasePurchases = st?.Purchases ?? 0;
        s.BaseSales = st?.Sales ?? 0;
        s.BaseOre = st?.OreMined ?? 0;
    }

    public static bool Active(Universe u) => u.Tutorial is { Done: false } t && t.Step < Steps.Count;

    public static TutorialStepDef? Current(Universe u) => u.Tutorial is { Done: false } t && t.Step >= 0 && t.Step < Steps.Count ? Steps[t.Step] : null;

    public static string StationName(Universe u) =>
        u.Tutorial is { } t && u.Stations.TryGetValue(t.Station, out var st) ? Model.StationNames.WithoutOwner(u, st) : "Station";

    /// <summary>Zieltext eines Schritts mit eingesetztem Stationsnamen.</summary>
    public static string GoalText(Universe u, TutorialStepDef step) => step.Goal.Replace("{station}", StationName(u), StringComparison.Ordinal);

    /// <summary>HUD-Zeile „Tutorial 2/8: …“ ("" = kein laufendes Tutorial).</summary>
    public static string HudLine(Universe u) =>
        Current(u) is { } step ? $"Tutorial {u.Tutorial!.Step + 1}/{Steps.Count}: {GoalText(u, step)}" : "";

    /// <summary>Ist der Schritt jetzt erfüllt?</summary>
    public static bool Check(Universe u, TutorialState s, TutorialStepDef step, TutorialView v)
    {
        if (!u.Ships.TryGetValue(u.PlayerShip, out var me)) return false;
        var stats = u.Player.Stats;
        switch (step.Check)
        {
            case TutorialChecks.Near:
                if (me.DockedAt == s.Station) return true;
                if (!u.Stations.TryGetValue(s.Station, out var st) || st.Sector != me.Sector) return false;
                return Vec2.Distance(me.Position, st.Position) <= st.Radius + NearDistance;
            case TutorialChecks.Dock: return me.DockedAt.Value != 0;
            case TutorialChecks.Undock: return me.DockedAt.Value == 0 && !u.PlayerOnFoot;
            case TutorialChecks.Buy: return (stats?.Purchases ?? 0) > s.BasePurchases;
            case TutorialChecks.Sell: return (stats?.Sales ?? 0) > s.BaseSales;
            case TutorialChecks.Mine: return (stats?.OreMined ?? 0) - s.BaseOre >= MathF.Max(1f, step.Amount) - 1e-6;
            case TutorialChecks.Command: return v.CommandOpen;
            case TutorialChecks.Help: return v.HelpOpen;
        }
        return false;
    }

    /// <summary>Prüft den aktuellen Schritt; erfüllt → nächster Schritt (neue Zählerstände) bzw. fertig. null = kein Fortschritt.</summary>
    public static TutorialAdvance? Update(Universe u, TutorialView v)
    {
        if (u.Tutorial is not { Done: false } s || Current(u) is not { } step) return null;
        if (!Check(u, s, step, v)) return null;
        int done = s.Step;
        s.Step++;
        Baseline(u, s);
        if (s.Step >= Steps.Count) { s.Done = true; return new TutorialAdvance(done, -1, true); }
        return new TutorialAdvance(done, s.Step, false);
    }

    /// <summary>Abschlussmeldung.</summary>
    public const string FinishedText = "Tutorial geschafft! Du kannst fliegen, andocken, handeln und schürfen. Die Hilfe (☰ → Hilfe) erklärt alles Weitere.";
}
