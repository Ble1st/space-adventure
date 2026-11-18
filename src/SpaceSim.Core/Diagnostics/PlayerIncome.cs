using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Diagnostics;

/// <summary>
/// Runde 38 („Meine Fraktion“: Einnahmen/Tag echt aus Ereignissen): zählt, was in die Kasse des Spielers fließt — Verkäufe der eigenen Schiffe
/// (<see cref="PlayerSold"/>, Flottenhandel über <see cref="TradeCompleted"/> eigener Schiffe), Kopfgeld (<see cref="BountyPaid"/>) und Schiffsverkauf
/// (<see cref="ShipSold"/>) — über das letzte Spieltagsfenster (<see cref="EconomyRules.DayTicks"/>). Reine Anzeige: abonniert den Bus, verbraucht keinen
/// Zufall, wird nicht gespeichert und nicht gehasht; nach Laden oder neuem Spiel beginnt es leer (die Anzeige sagt dann, über wie viel Zeit gemessen ist).
/// </summary>
public sealed class PlayerIncome
{
    readonly Universe _u;
    readonly Queue<(long Tick, float Amount)> _entries = new();
    readonly long _since;
    readonly Dictionary<string, float> _bySource = new(StringComparer.Ordinal);

    public PlayerIncome(Universe u)
    {
        _u = u;
        _since = u.Tick;
        u.Events.Raised += Observe;
    }

    /// <summary>Nimmt ein Ereignis auf (öffentlich für Tests ohne Bus).</summary>
    public void Observe(SimEvent e)
    {
        switch (e)
        {
            case PlayerSold ps: Add(ps.Revenue, "Verkauf"); break;
            case TradeCompleted tc when _u.Ships.TryGetValue(tc.Ship, out var s) && s.Owner == _u.PlayerActor: Add(tc.Revenue, "Flottenhandel"); break;
            case BountyPaid bp when bp.Hunter == _u.PlayerActor: Add(bp.Amount, "Kopfgeld"); break;
            case TreasuryIncome { Purpose: "tribute" } ti when ti.Actor == _u.PlayerActor: Add(ti.Amount, "Tribut"); break;   // Runde 45: geforderter Tribut
            case ShipSold sold: Add(sold.Price, "Schiffsverkauf"); break;
        }
    }

    void Add(float amount, string source)
    {
        if (!(amount > 0f) || !float.IsFinite(amount)) return;
        _entries.Enqueue((_u.Tick, amount));
        _bySource[source] = _bySource.GetValueOrDefault(source) + amount;
    }

    void Trim(long now)
    {
        long from = now - Math.Max(1, _u.Rules.DayTicks);
        while (_entries.Count > 0 && _entries.Peek().Tick <= from) _entries.Dequeue();
    }

    /// <summary>Einnahmen im letzten Spieltag (Credits) und über wie viele Ticks gemessen wurde (höchstens ein Tag).</summary>
    public (float Credits, long Ticks) LastDay()
    {
        long now = _u.Tick;
        Trim(now);
        float sum = 0f;
        foreach (var (_, a) in _entries) sum += a;
        return (sum, Math.Min(Math.Max(1, _u.Rules.DayTicks), now - _since));
    }

    /// <summary>Summe je Quelle seit Beginn der Messung (Anzeige/Tests).</summary>
    public IReadOnlyDictionary<string, float> Sources => _bySource;

    /// <summary>Anzeigetext: „+1234 Cr/Tag“, solange weniger als ein Tag gemessen ist mit „(seit m min)“; ohne Einnahmen „keine (seit …)“.</summary>
    public string Text()
    {
        var (c, t) = LastDay();
        int day = Math.Max(1, _u.Rules.DayTicks);
        string since = t >= day ? "" : $" (seit {Math.Max(0, t) / 240} min)";
        return c <= 0f ? $"keine{since}" : $"+{c:0} Cr/Tag{since}";
    }
}
