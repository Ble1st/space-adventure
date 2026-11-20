namespace SpaceSim.Core.Model;

/// <summary>
/// Ausbau Punkt 3: Schutzvertrag des Spielers mit einer KI-Fraktion (<see cref="Simulation.Protection"/>). Gespeichert, gehasht nur mit Spielerpolitik.
/// </summary>
public sealed class ProtectionContract
{
    public FactionId Faction;
    public long Since;
    /// <summary>Bezahlt bis zu diesem Tick (die nächste Tagesgebühr wird dann fällig).</summary>
    public long PaidUntil;
}

/// <summary>
/// Ausbau Punkt 3: KI-Konvoi — Händler, die gebündelt von <see cref="From"/> nach <see cref="To"/> fahren, mit Polizeigeleit der <see cref="Faction"/>.
/// <see cref="Departed"/> 0 = sammelt noch. Gespeichert und gehasht nur, wenn Konvois bestehen (<see cref="Simulation.ConvoyRules"/>).
/// </summary>
public sealed class Convoy
{
    public int Id;
    public FactionId Faction;
    public SectorId From;
    public SectorId To;
    public long Formed;
    public long Departed;
    public List<ShipId> Traders { get; } = new();
    public List<ShipId> Escorts { get; } = new();
}
