using SpaceSim.Core.Model;

namespace SpaceSim.Core.Trading;

/// <summary>
/// Runde 26/27: Preisgedächtnis der Ansicht — je Station und Ware die Preise der letzten <see cref="WindowTicks"/> Ticks (ein Messpunkt alle
/// <see cref="Every"/> Ticks), daraus niedrigster und höchster Preis („zuletzt lo–hi“). Nur für die Abnehmer-Auskunft: nicht gespeichert, nicht gehasht,
/// kein Teil des Universums; die Sitzung füttert es mit <see cref="Observe"/>. Nach dem Laden oder einem neuen Spiel beginnt es leer.
/// </summary>
public sealed class PriceMemory
{
    public const int Every = 20;
    /// <summary>Fenster: etwa 5 Spielminuten.</summary>
    public const int WindowTicks = 1200;
    /// <summary>Ab so viel beobachteter Zeit gilt der Verlauf als ausreichend (sonst kein „grün“).</summary>
    public const int EnoughTicks = 600;
    const int Slots = WindowTicks / Every;

    sealed class Track { public readonly float[] P = new float[Slots]; public int N, Next; public long Since; }
    readonly Dictionary<(int Station, int Good), Track> _m = new();
    long _last = -1;
    WeakReference<Universe>? _u;

    public void Observe(Universe u)
    {
        if (_u is null || !_u.TryGetTarget(out var known) || !ReferenceEquals(known, u)) { _m.Clear(); _last = -1; _u = new WeakReference<Universe>(u); }
        if (_last >= 0 && u.Tick - _last < Every) return;
        _last = u.Tick;
        foreach (var st in u.Stations.Values)
            foreach (var (gid, e) in st.Market)
            {
                if (e.Price <= 0f) continue;
                var k = (st.Id.Value, gid.Value);
                if (!_m.TryGetValue(k, out var t)) _m[k] = t = new Track { Since = u.Tick };
                t.P[t.Next] = e.Price;
                t.Next = (t.Next + 1) % Slots;
                if (t.N < Slots) t.N++;
            }
    }

    /// <summary>Niedrigster und höchster Preis im Fenster und ob der Verlauf ausreicht (<see cref="EnoughTicks"/>); null = unbekannt.</summary>
    public (float Lo, float Hi, bool Enough)? Range(StationId st, GoodId good, long tick)
    {
        if (!_m.TryGetValue((st.Value, good.Value), out var t) || t.N == 0) return null;
        float lo = float.MaxValue, hi = 0f;
        for (int i = 0; i < t.N; i++) { lo = MathF.Min(lo, t.P[i]); hi = MathF.Max(hi, t.P[i]); }
        return (lo, hi, tick - t.Since >= EnoughTicks);
    }
}
