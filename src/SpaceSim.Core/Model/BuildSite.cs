namespace SpaceSim.Core.Model;

/// <summary>
/// Baustelle (Phase 8, SCHIFFE.md Abschnitt 10): Eine Station im Bau. Die Baustelle ist selbst eine <see cref="Station"/> (Typ
/// <see cref="EconomyRules.ConstructionSiteType"/>, Hindernis, sichtbar, andockbar) mit diesem Datensatz in <see cref="Station.Site"/>.
/// Ihr Markt führt die noch fehlenden Materialien: Bestand = geliefert, Wunschbestand = verlangt; Händler und Spieler beliefern sie über
/// den normalen Handel, das Bauschiff holt Material im Auftrag der Baustelle. Die Kasse der Station ist das Treuhandgeld des Investors.
/// Sind alle Materialien da und ein Bauschiff vor Ort (oder hat der Notbau gegriffen), montiert das Bauschiff die Station
/// (<see cref="Simulation.ConstructionSystem"/>).
/// </summary>
public sealed class BuildSite
{
    /// <summary>Stationstyp, der hier entsteht.</summary>
    public string TypeKey = "";
    /// <summary>Verlangtes Material je Ware (Stückliste); der Markt der Baustelle hält Bestand (= geliefert) und Wunschbestand (= verlangt).</summary>
    public Dictionary<GoodId, float> Required = new();
    /// <summary>Investor, der das Treuhandgeld eingezahlt hat (Station oder Schiff; 0 = keiner bekannt). Bei Verfall geht der Rest dorthin zurück.</summary>
    public int InvestorStation;
    public int InvestorShip;
    /// <summary>Eingezahlte Bausumme (Treuhand bei Beginn); Grundlage der Montagegebühr.</summary>
    public float Budget;
    /// <summary>Zugeteiltes Bauschiff (0 = keins).</summary>
    public ShipId Builder;
    /// <summary>Tick der Anlage.</summary>
    public long StartedTick;
    /// <summary>Tick des letzten Fortschritts (Lieferung, Montage); Verfall misst ab hier.</summary>
    public long LastProgressTick;
    /// <summary>Seit wann die Baustelle blockiert ist (kein Lieferant oder kein Bauschiff erreichbar); −1 = nicht blockiert.</summary>
    public long BlockedSince = -1;
    /// <summary>Montagefortschritt in Ticks (läuft, solange alles geliefert ist und ein Bauschiff vor Ort ist).</summary>
    public int AssemblyTicks;
    /// <summary>Der Notbau hat die fehlenden Materialien ersetzt; die Montage braucht dann kein Bauschiff mehr.</summary>
    public bool Emergency;
    /// <summary>Verfallen: kein Projekt mehr, das gelieferte Material bleibt als Lager liegen (ein neues Projekt im Sektor übernimmt es).</summary>
    public bool Abandoned;

    /// <summary>Wie viel einer Ware noch fehlt (verlangt − geliefert), nie negativ.</summary>
    public float Missing(Station site, GoodId good) =>
        Required.TryGetValue(good, out var need) ? MathF.Max(0f, need - (site.Market.TryGetValue(good, out var e) ? e.Amount : 0f)) : 0f;

    /// <summary>Ist alles geliefert?</summary>
    public bool Complete(Station site) => Required.Keys.All(g => Missing(site, g) < 0.001f);
}
