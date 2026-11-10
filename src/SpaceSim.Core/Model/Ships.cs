namespace SpaceSim.Core.Model;

public enum ShipRole { Trader, Miner, Fighter, Player }
public enum TradePhase { Idle, GoingToBuy, GoingToSell }
public enum MinePhase { Idle, GoingToField, Mining, GoingToStation }

public sealed class Ship
{
    public ShipId Id;
    public FactionId Faction;
    /// <summary>Aktuelle Aufgabe / Etikett (gespeichert, Anzeige); nicht mehr für Entscheidungen benutzt.</summary>
    public ShipRole Role;
    /// <summary>Fähigkeiten des Schiffs; nicht gespeichert, beim Entstehen aus <see cref="Role"/> abgeleitet (<see cref="CapabilityDefaults"/>).</summary>
    public Capability Capabilities;
    /// <summary>Wer das Schiff steuert; nicht gespeichert, beim Entstehen aus <see cref="Role"/> abgeleitet.</summary>
    public Controller Controller;
    public float CargoCapacity = 100f;
    public Dictionary<GoodId, float> Cargo = new();
    public float Credits;
    public const float MaxHull = 100f;
    public float Hull = MaxHull;

    public SectorId Sector;
    public Vec2 Position;
    public ShipOrder? Order;
    public TradePhase Phase;
    public MinePhase MinePhase;
    public Transit? Transit;

    /// <summary>Hat das Schiff diese Fähigkeit (oder diese Kombination)?</summary>
    public bool Has(Capability c) => (Capabilities & c) == c;

    /// <summary>Steuert der Spieler dieses Schiff selbst?</summary>
    public bool IsPlayer => Controller == Controller.Player;

    /// <summary>Wirtschaftlich aktives KI-Schiff (Händler oder Miner): Fracht- oder Schürffähigkeit, von der KI gesteuert.</summary>
    public bool IsEconomyAi => Controller == Controller.Ai && (Has(Capability.Cargo) || Has(Capability.Mining));

    /// <summary>Belegter Laderaum (Einheiten × Volumen je Einheit).</summary>
    public float UsedVolume(Universe u) => Cargo.Sum(kv => kv.Value * u.Goods[kv.Key].VolumePerUnit);

    /// <summary>Wie viele Einheiten der Ware noch in den Laderaum passen.</summary>
    public float FreeUnits(Universe u, GoodId good) =>
        MathF.Max(0f, (CargoCapacity - UsedVolume(u)) / u.Goods[good].VolumePerUnit);

    /// <summary>KI denkt erst ab diesem Tick wieder nach (spart Rechenzeit bei untätigen Schiffen).</summary>
    /// <summary>Nachladeuhr des Mining-Lasers (NPC-Miner), in Sekunden Spielzeit.</summary>
    public float ShotTimer;
    /// <summary>Asteroid-Id, auf den der NPC-Miner gerade feuert (0 = keiner).</summary>
    public int MineAsteroid;

    public long NextThinkTick;
    /// <summary>Tick, seit dem das Schiff keinen Auftrag mehr hat (-1 = beschäftigt).</summary>
    public long IdleSince = -1;
}

/// <summary>Abstrakte Reise statt Physik: Schiffe außerhalb des Spielersektors haben nur einen Fahrplan.</summary>
public sealed class Transit
{
    public long StartTick;
    public long ArrivalTick;
    public SectorId ToSector;
    public Vec2 ToPosition;
    /// <summary>Zeitlich lückenlose Teilstrecken, je eine pro durchflogenem Sektor (Gate zu Gate).</summary>
    public List<Leg> Legs = new();
}

/// <summary>Geradeausflug innerhalb eines Sektors zwischen StartTick (inkl.) und EndTick (exkl.).</summary>
public sealed record Leg(SectorId Sector, Vec2 From, Vec2 To, long StartTick, long EndTick);

public abstract record ShipOrder;
public sealed record TradeOrder(StationId Buy, StationId Sell, GoodId Good) : ShipOrder;
/// <summary>Abbau an einem Rohstofffeld (Index in <see cref="Sector.Fields"/>); verkauft wird, was die Zielstation führt.</summary>
public sealed record MineOrder(SectorId Sector, int Field, StationId DeliverTo) : ShipOrder;
public sealed record PatrolOrder(SectorId Sector) : ShipOrder;
public sealed record GoToOrder(StationId Target) : ShipOrder;
