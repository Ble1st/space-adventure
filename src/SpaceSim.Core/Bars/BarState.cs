using SpaceSim.Core.Model;

namespace SpaceSim.Core.Bars;

/// <summary>Zustand der Bars (nur durch Spieleraktionen; null = nie benutzt). Gespeichert und gehasht (Spielerpolitik) nur, wenn vorhanden.</summary>
public sealed class BarState
{
    /// <summary>Losverkauf je Station (Station-Id → Tag, Lose an diesem Tag, Lose insgesamt).</summary>
    public SortedDictionary<int, BarStationState> Stations = new();
    /// <summary>Das offene Rubbellos des Spielers (höchstens eins).</summary>
    public BarTicket? Ticket;
}

public sealed class BarStationState
{
    public long Day;
    public int Sold;
    /// <summary>Lose dieser Station insgesamt — Teil des Losseeds (zwei Lose im selben Tick sind verschieden).</summary>
    public int Counter;
}

/// <summary>
/// Ein Rubbellos: Ausgang (Stufe, Gewinn, Symbole) steht beim Kauf fest; Aufdecken ändert nur <see cref="Revealed"/> — die Reihenfolge ändert nichts,
/// Speichern/Laden mitten im Los auch nicht.
/// </summary>
public sealed class BarTicket
{
    public StationId Station;
    public long Bought;
    public float Price;
    /// <summary>Gewinnstufe (−1 = Niete).</summary>
    public int Tier = -1;
    public int Prize;
    /// <summary>Neun Felder (Symbolindex 0..n−1).</summary>
    public int[] Symbols = new int[Scratchcard.Fields];
    /// <summary>Bitmaske der aufgedeckten Felder.</summary>
    public int Revealed;

    public bool IsRevealed(int i) => (Revealed & (1 << i)) != 0;
    public bool AllRevealed => Revealed == (1 << Scratchcard.Fields) - 1;
    public int RevealedCount => System.Numerics.BitOperations.PopCount((uint)Revealed);
}
