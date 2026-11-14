using SpaceSim.Core.Model;

namespace SpaceSim.Core.Trading;

/// <summary>
/// Runde 26: Preisgedächtnis der Ansicht — gleitende Mittel des Preises je Station und Ware (schnell <see cref="FastTicks"/>, langsam
/// <see cref="SlowTicks"/>). Nur für die Abnehmer-Auskunft: nicht gespeichert, nicht gehasht, kein Teil des Universums; die Sitzung füttert es
/// mit <see cref="Observe"/> (höchstens alle <see cref="Every"/> Ticks). Nach dem Laden oder einem neuen Spiel beginnt es leer.
/// </summary>
public sealed class PriceMemory
{
    public const int Every = 20;
    public const float FastTicks = 400f, SlowTicks = 2400f;
    readonly Dictionary<(int Station, int Good), (float Fast, float Slow, long Since)> _m = new();
    readonly Dictionary<(int Station, int Good), (float Amount, float Inflow)> _flow = new();
    long _last = -1;
    WeakReference<Universe>? _u;

    /// <summary>Ab wann (Tick) beobachtet wird; −1 = noch nie.</summary>
    public long Started { get; private set; } = -1;

    public void Observe(Universe u)
    {
        if (_u is null || !_u.TryGetTarget(out var known) || !ReferenceEquals(known, u)) { _m.Clear(); _flow.Clear(); _last = -1; Started = -1; _u = new WeakReference<Universe>(u); }
        if (_last >= 0 && u.Tick - _last < Every) return;
        float dt = _last < 0 ? 0f : u.Tick - _last;
        _last = u.Tick;
        if (Started < 0) Started = u.Tick;
        float af = 1f - MathF.Exp(-dt / FastTicks), asl = 1f - MathF.Exp(-dt / SlowTicks);
        foreach (var st in u.Stations.Values)
            foreach (var (gid, e) in st.Market)
            {
                if (e.Price <= 0f) continue;
                var k = (st.Id.Value, gid.Value);
                // Zufluss: Zunahmen des Bestands je Tick (Lieferungen von Händlern und Schürfern, bei Herstellern auch die Produktion), schnelles Mittel
                if (_flow.TryGetValue(k, out var fl)) { float inc = dt > 0f ? MathF.Max(0f, e.Amount - fl.Amount) / dt : 0f; _flow[k] = (e.Amount, fl.Inflow + (inc - fl.Inflow) * af); }
                else _flow[k] = (e.Amount, 0f);
                if (!_m.TryGetValue(k, out var v)) { _m[k] = (e.Price, e.Price, u.Tick); continue; }
                _m[k] = (v.Fast + (e.Price - v.Fast) * af, v.Slow + (e.Price - v.Slow) * asl, v.Since);
            }
    }

    /// <summary>Gleitender Zufluss in den Bestand (Einheiten je Tick, nur Zunahmen zwischen zwei Messpunkten); 0 = unbekannt.</summary>
    public float InflowOf(StationId st, GoodId good) => _flow.TryGetValue((st.Value, good.Value), out var f) ? f.Inflow : 0f;

    /// <summary>Gleitende Mittel (schnell, langsam) und seit wann beobachtet; null = unbekannt.</summary>
    public (float Fast, float Slow, long Since)? Of(StationId st, GoodId good) => _m.TryGetValue((st.Value, good.Value), out var v) ? v : null;
}
