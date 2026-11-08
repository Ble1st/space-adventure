namespace SpaceSim.Core.Model;

/// <summary>
/// Index "welche Schiffe sind in welchem Sektor": geparkte Schiffe und Schiffe mit einer Teilstrecke im Sektor.
/// Verhindert, dass die Darstellung pro Frame alle Schiffe des Universums prüfen muss.
/// Wird ausschließlich über <see cref="Universe"/> gepflegt (AddShip, StartTransit, FinishTransit, ...).
/// </summary>
public sealed class ShipTracker
{
    readonly Dictionary<SectorId, HashSet<ShipId>> _parked = new();
    readonly Dictionary<SectorId, HashSet<ShipId>> _transit = new();
    static readonly IReadOnlyCollection<ShipId> Empty = Array.Empty<ShipId>();

    public IReadOnlyCollection<ShipId> ParkedIn(SectorId s) => _parked.TryGetValue(s, out var h) ? h : Empty;
    public IReadOnlyCollection<ShipId> TransitIn(SectorId s) => _transit.TryGetValue(s, out var h) ? h : Empty;

    internal void AddParked(SectorId s, ShipId id) => Set(_parked, s).Add(id);
    internal void RemoveParked(SectorId s, ShipId id) { if (_parked.TryGetValue(s, out var h)) h.Remove(id); }

    internal void AddTransit(Transit t, ShipId id)
    {
        foreach (var leg in t.Legs) Set(_transit, leg.Sector).Add(id);
    }

    internal void RemoveTransit(Transit t, ShipId id)
    {
        foreach (var leg in t.Legs) if (_transit.TryGetValue(leg.Sector, out var h)) h.Remove(id);
    }

    static HashSet<ShipId> Set(Dictionary<SectorId, HashSet<ShipId>> d, SectorId s)
    {
        if (!d.TryGetValue(s, out var h)) d[s] = h = new HashSet<ShipId>();
        return h;
    }
}
