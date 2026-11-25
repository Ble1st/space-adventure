using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;

namespace SpaceSim.Core.Bars;

/// <summary>Bars im Spielstand (additiv, v17 bleibt): Losverkauf je Station und das offene Los.</summary>
public sealed class BarSave
{
    public List<BarStationSave> Stations { get; set; } = new();
    public BarTicketSave? Ticket { get; set; }
}

public sealed class BarStationSave
{
    public int Station { get; set; }
    public long Day { get; set; }
    public int Sold { get; set; }
    public int Counter { get; set; }
}

public sealed class BarTicketSave
{
    public int Station { get; set; }
    public long Bought { get; set; }
    public float Price { get; set; }
    public int Tier { get; set; }
    public int Prize { get; set; }
    public int[] Symbols { get; set; } = Array.Empty<int>();
    public int Revealed { get; set; }
}

/// <summary>Speichern/Laden/Hash der Bars. Gehasht nur mit Spielerpolitik und nur, wenn vorhanden (die NPC-Welt kennt keine Bars).</summary>
public static class BarCodec
{
    public static void Save(Universe u, SaveGame s)
    {
        if (u.Bar is not { } b) return;
        s.Bar = new BarSave
        {
            Stations = b.Stations.Select(kv => new BarStationSave { Station = kv.Key, Day = kv.Value.Day, Sold = kv.Value.Sold, Counter = kv.Value.Counter }).ToList(),
            Ticket = b.Ticket is { } t ? new BarTicketSave { Station = t.Station.Value, Bought = t.Bought, Price = t.Price, Tier = t.Tier, Prize = t.Prize, Symbols = t.Symbols.ToArray(), Revealed = t.Revealed } : null,
        };
    }

    public static void Load(SaveGame s, Universe u)
    {
        if (s.Bar is not { } b) return;
        var state = new BarState();
        foreach (var st in b.Stations) state.Stations[st.Station] = new BarStationState { Day = st.Day, Sold = st.Sold, Counter = st.Counter };
        if (b.Ticket is { } t && t.Symbols.Length == Scratchcard.Fields)
            state.Ticket = new BarTicket { Station = new StationId(t.Station), Bought = t.Bought, Price = t.Price, Tier = t.Tier, Prize = t.Prize, Symbols = t.Symbols.ToArray(), Revealed = t.Revealed };
        u.Bar = state;
    }

    public static void Mix(Universe u, Action<ulong> mix, Action<float> mixF)
    {
        if (u.Bar is not { } b) return;
        mix(0xBA20u);
        foreach (var (id, st) in b.Stations) { mix((ulong)id); mix((ulong)st.Day); mix((ulong)st.Sold); mix((ulong)st.Counter); }
        if (b.Ticket is { } t)
        {
            mix(0xBA21u); mix((ulong)t.Station.Value); mix((ulong)t.Bought); mixF(t.Price); mix((ulong)(t.Tier + 1)); mix((ulong)t.Prize); mix((ulong)t.Revealed);
            foreach (var sym in t.Symbols) mix((ulong)sym);
        }
    }
}
