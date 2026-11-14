using SpaceSim.Core.Model;

namespace SpaceSim.Core.Trading;

/// <summary>
/// Runde 26–28: Preisgedächtnis der Ansicht — je Station und Ware niedrigster und höchster Preis der letzten <see cref="WindowTicks"/> Ticks („zuletzt lo–hi“).
/// Runde 28 (Gerätetest: Täler zwischen zwei Stützstellen fehlten): jeder Tick zählt — <see cref="Observe"/> läuft je Tick und führt Min/Max des laufenden
/// <see cref="Every"/>-Tick-Abschnitts, das Fenster besteht aus diesen Abschnitten. Nur für die Abnehmer-Auskunft: nicht gespeichert, nicht gehasht, kein Teil
/// des Universums; nach dem Laden oder einem neuen Spiel beginnt es leer.
/// </summary>
public sealed class PriceMemory
{
    public const int Every = 20;
    /// <summary>Fenster: etwa 5 Spielminuten.</summary>
    public const int WindowTicks = 1200;
    /// <summary>Runde 28: erst ein volles Fenster gilt als Verlauf (vorher 600 Ticks — die Kolonie pendelt mit ihren Lieferungen langsamer).</summary>
    public const int EnoughTicks = WindowTicks;
    const int Slots = WindowTicks / Every;

    sealed class Track
    {
        public readonly float[] Lo = new float[Slots], Hi = new float[Slots];
        public int N, Next; public long Since;
        public float CurLo = float.MaxValue, CurHi;
    }
    readonly Dictionary<(int Station, int Good), Track> _m = new();
    long _last = -1;
    WeakReference<Universe>? _u;

    /// <summary>Je Tick aufrufen (mehrfach im selben Tick schadet nicht).</summary>
    public void Observe(Universe u)
    {
        if (_u is null || !_u.TryGetTarget(out var known) || !ReferenceEquals(known, u)) { _m.Clear(); _last = -1; _u = new WeakReference<Universe>(u); }
        if (u.Tick == _last) return;
        _last = u.Tick;
        bool close = u.Tick % Every == 0;
        foreach (var st in u.Stations.Values)
            foreach (var (gid, e) in st.Market)
            {
                if (e.Price <= 0f) continue;
                var k = (st.Id.Value, gid.Value);
                if (!_m.TryGetValue(k, out var t)) _m[k] = t = new Track { Since = u.Tick };
                t.CurLo = MathF.Min(t.CurLo, e.Price);
                t.CurHi = MathF.Max(t.CurHi, e.Price);
                if (!close) continue;
                t.Lo[t.Next] = t.CurLo; t.Hi[t.Next] = t.CurHi;
                t.Next = (t.Next + 1) % Slots;
                if (t.N < Slots) t.N++;
                t.CurLo = float.MaxValue; t.CurHi = 0f;
            }
    }

    /// <summary>Niedrigster und höchster Preis im Fenster (jeder beobachtete Tick) und ob der Verlauf ein volles Fenster umfasst; null = unbekannt.</summary>
    public (float Lo, float Hi, bool Enough)? Range(StationId st, GoodId good, long tick)
    {
        if (!_m.TryGetValue((st.Value, good.Value), out var t)) return null;
        float lo = t.CurLo, hi = t.CurHi;
        for (int i = 0; i < t.N; i++) { lo = MathF.Min(lo, t.Lo[i]); hi = MathF.Max(hi, t.Hi[i]); }
        if (lo == float.MaxValue) return null;
        return (lo, hi, tick - t.Since >= EnoughTicks);
    }
}
