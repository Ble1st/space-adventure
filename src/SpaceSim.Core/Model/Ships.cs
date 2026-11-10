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
    Capability _capabilities;
    Controller _controller;
    /// <summary>Wurden die Fähigkeiten ausdrücklich gesetzt (auch auf <see cref="Capability.None"/>)? Sonst leitet <c>Universe.AddShip</c> sie aus der Rolle ab.</summary>
    public bool CapabilitiesSet { get; private set; }
    /// <summary>Wurde der Steuerer ausdrücklich gesetzt? Sonst leitet <c>Universe.AddShip</c> ihn aus der Rolle ab.</summary>
    public bool ControllerSet { get; private set; }
    /// <summary>Fähigkeiten des Schiffs (gespeichert); ohne ausdrückliches Setzen aus <see cref="Role"/> abgeleitet (<see cref="CapabilityDefaults"/>). Auch <see cref="Capability.None"/> ist ein gültiger gesetzter Wert. Schiffe mit Modell leiten sie stattdessen aus <see cref="Stats"/> ab, sofern nicht ausdrücklich gesetzt.</summary>
    public Capability Capabilities { get => _capabilities; set { _capabilities = value; CapabilitiesSet = true; } }
    /// <summary>Wer das Schiff steuert (gespeichert); ohne ausdrückliches Setzen aus <see cref="Role"/> abgeleitet.</summary>
    public Controller Controller { get => _controller; set { _controller = value; ControllerSet = true; } }
    /// <summary>Schiffsmodell-Schlüssel ("" = kein Modell, dann gelten die Pauschalwerte).</summary>
    public string ModelKey = "";
    /// <summary>Slotliste dieses Schiffs (Art, Größe, optional facing/arc); leer = die der Klasse des Modells.</summary>
    public List<ClassSlot> Slots = new();
    /// <summary>Modul-Schlüssel je Slot (null = leer); Länge = Slotanzahl.</summary>
    public List<string?> Fit = new();
    /// <summary>Aus Modell/Slots/Fit abgeleitete Werte (nicht gespeichert); siehe <see cref="RecomputeStats"/>.</summary>
    public ShipStats Stats = new() { Hull = NoModelHull };
    /// <summary>Hat das Schiff ein Modell?</summary>
    public bool HasModel => ModelKey.Length > 0;
    /// <summary>Pauschal-Hülle für Schiffe ohne Modell (bisheriger Wert).</summary>
    const float NoModelHull = 100f;
    public float CargoCapacity = 100f;
    public Dictionary<GoodId, float> Cargo = new();
    public float Credits;
    /// <summary>Hüllenmaximum des Schiffs; kommt aus <see cref="Stats"/> (Schiff ohne Modell: 100 wie bisher).</summary>
    public float MaxHull => Stats.Hull;
    public float Hull = NoModelHull;

    public SectorId Sector;
    public Vec2 Position;
    public ShipOrder? Order;
    public TradePhase Phase;
    public MinePhase MinePhase;
    public Transit? Transit;

    /// <summary>Hat das Schiff diese Fähigkeit (oder diese Kombination)?</summary>
    public bool Has(Capability c) => (Capabilities & c) == c;

    /// <summary>
    /// Berechnet <see cref="Stats"/> neu aus Modell, Slots und Fit (nach Änderung von Fit/Slots und beim Laden).
    /// Ist die Slotliste leer, wird sie von der Klasse des Modells übernommen. Gibt false, wenn das Modell in
    /// diesem Universum nicht auflösbar ist (dann bleiben die Pauschalwerte: Hülle 100). Fähigkeiten werden aus
    /// den Modul-Grants abgeleitet, sofern sie nicht ausdrücklich gesetzt wurden (der Set-Flag bleibt unberührt).
    /// </summary>
    public bool RecomputeStats(Universe u)
    {
        if (ModelKey.Length > 0 && u.ShipModels.TryGetValue(ModelKey, out var model) && u.ShipClasses.TryGetValue(model.ClassKey, out var cls))
        {
            if (Slots.Count == 0)
                foreach (var s in cls.Slots)
                    Slots.Add(new ClassSlot { Type = s.Type, Size = s.Size, Facing = s.Facing, Arc = s.Arc });
            while (Fit.Count < Slots.Count) Fit.Add(null);
            var fit = new List<Module?>(Slots.Count);
            for (int i = 0; i < Slots.Count; i++)
                fit.Add(Fit[i] is { } key && u.Modules.TryGetValue(key, out var m) ? m : null);
            Stats = ShipStats.Compute(cls, fit);
            if (!CapabilitiesSet) _capabilities = Stats.Capabilities;
            return true;
        }
        Stats = new ShipStats { Hull = NoModelHull };
        return false;
    }

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
