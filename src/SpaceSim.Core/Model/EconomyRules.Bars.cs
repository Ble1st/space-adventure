namespace SpaceSim.Core.Model;

/// <summary>
/// Bars in Stationen (Lücke gegenüber Interstellar Pilot 2, eigener Entwurf; docs/BARS.md): Gerüchte, Söldner-Abkürzung, Rubbellos. Schalter im Code aus
/// (Stand vorher), in der Standardwelt an. Reine Spieleraktionen: ohne Spieler ändert sich nichts (kein Zufall aus <see cref="Universe.Rng"/>, kein Zustand).
/// </summary>
public sealed partial class EconomyRules
{
    /// <summary>Bars an bewohnten, andockbaren Stationen (Dienst „bar“).</summary>
    public bool Bars;
    /// <summary>Stationstypen mit Bar (Code: keine; die Standardwelt nennt Kolonie, Handelsstation, Planetenhandel, Werft, Sektor-HQ, Hauptquartier, Polizeiwache).</summary>
    public List<string> BarStationTypes = new();
    /// <summary>Preis eines Rubbelloses (Spielerkasse → Stationskasse).</summary>
    public float BarTicketPrice = 50f;
    /// <summary>Höchstens so viele Lose je Station und Spieltag (<see cref="DayTicks"/>).</summary>
    public int BarTicketsPerDay = 5;
    /// <summary>Verkauf nur, solange die Stationskasse mindestens so viel hält (sonst „Die Bar verkauft keine Lose“).</summary>
    public float BarMinStationCash = 1000f;
    /// <summary>Gewinnstufen (Credits) — Symbol i gewinnt Stufe i bei drei gleichen.</summary>
    public int[] BarPrizes = { 25, 50, 100, 250, 1000, 5000 };
    /// <summary>Gewichte der Gewinnstufen von <see cref="BarPrizeWeightTotal"/>; der Rest ist Niete. Erwartungswert 37,5 Cr bei 50 Cr Preis = 25 % Hausvorteil.</summary>
    public int[] BarPrizeWeights = { 2000, 1200, 800, 300, 80, 6 };
    public int BarPrizeWeightTotal = 10000;
    /// <summary>Gebühr für ein kaufbares Gerücht (unbekannte Station wird bekannt).</summary>
    public float BarRumourFee = 40f;
}
