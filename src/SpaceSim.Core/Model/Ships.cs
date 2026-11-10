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
    /// diesem Universum nicht auflösbar ist (dann bleiben die Pauschalwerte: Hülle 100). Fähigkeiten werden bei
    /// Schiffen mit Modell immer aus den Modul-Grants abgeleitet (gespeicherte Werte zählen nur für Schiffe ohne Modell).
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
            Capabilities = Stats.Capabilities;   // Schiffe mit Modell: Fähigkeiten folgen immer dem Fit
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

    /// <summary>Hat das Schiff getrennte Laderäume (mindestens ein solid- oder liquid-Frachtmodul)? Nein = ein Gesamtladeraum (kein Modell oder nur any-Module), dann verhält sich alles wie bisher.</summary>
    public bool HasSeparatedHolds => HasModel && (Stats.CargoSolid > 0f || Stats.CargoLiquid > 0f);

    /// <summary>Belegtes Volumen einer Warenform (feste bzw. flüssige Waren getrennt).</summary>
    public float UsedVolume(Universe u, GoodForm form)
    {
        float sum = 0f;
        foreach (var kv in Cargo)
            if (u.Goods[kv.Key].Form == form) sum += kv.Value * u.Goods[kv.Key].VolumePerUnit;
        return sum;
    }

    /// <summary>Laderaum-Volumen, das eine Warenform aufnehmen kann: ohne getrennte Räume die Gesamtkapazität, sonst solid+any bzw. liquid+any (der any-Raum wird geteilt).</summary>
    public float CapacityVolumeFor(GoodForm form)
    {
        if (!HasSeparatedHolds) return CargoCapacity;
        return form == GoodForm.Liquid ? Stats.CargoLiquid + Stats.CargoAny : Stats.CargoSolid + Stats.CargoAny;
    }

    /// <summary>Laderaum-Volumen, das eine konkrete Ware aufnehmen kann (nur ihre Form zählt).</summary>
    public float CapacityVolumeFor(Universe u, GoodId good) => CapacityVolumeFor(u.Goods[good].Form);

    /// <summary>
    /// Freies Volumen für eine Warenform: ohne getrennte Räume wie bisher <c>CargoCapacity − UsedVolume</c>. Mit getrennten
    /// Räumen ist der any-Anteil zwischen den Formen teilbar; zulässig bleibt eine Ladung, wenn usedSolid ≤ solid+any,
    /// usedLiquid ≤ liquid+any und usedSolid+usedLiquid ≤ solid+liquid+any (jeweils in Volumen).
    /// </summary>
    public float FreeVolumeForForm(Universe u, GoodForm form)
    {
        if (!HasSeparatedHolds) return MathF.Max(0f, CargoCapacity - UsedVolume(u));
        float usedSolid = UsedVolume(u, GoodForm.Solid), usedLiquid = UsedVolume(u, GoodForm.Liquid);
        float total = Stats.CargoSolid + Stats.CargoLiquid + Stats.CargoAny;
        float free = MathF.Min(CapacityVolumeFor(form) - (form == GoodForm.Liquid ? usedLiquid : usedSolid),
                               total - usedSolid - usedLiquid);
        return MathF.Max(0f, free);
    }

    /// <summary>Freies Volumen für eine konkrete Ware unter der Frachtform-Regel (feste Waren in solid+any, Flüssigkeiten in liquid+any).</summary>
    public float FreeVolumeFor(Universe u, GoodId good) => FreeVolumeForForm(u, u.Goods[good].Form);

    /// <summary>Wie viele Einheiten der Ware noch in den Laderaum passen (Frachtform-Regel siehe <see cref="FreeVolumeFor"/>).</summary>
    public float FreeUnits(Universe u, GoodId good) =>
        MathF.Max(0f, FreeVolumeFor(u, good) / u.Goods[good].VolumePerUnit);

    /// <summary>Nachladeuhr des Mining-Lasers (NPC-Miner), in Sekunden Spielzeit.</summary>
    public float ShotTimer;
    /// <summary>Asteroid-Id, auf den der NPC-Miner gerade feuert (0 = keiner).</summary>
    public int MineAsteroid;

    /// <summary>KI denkt erst ab diesem Tick wieder nach (spart Rechenzeit bei untätigen Schiffen).</summary>
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
