namespace SpaceSim.Core.Model;

/// <summary>
/// Aufgabe/Etikett eines Schiffs (gespeichert als Text). Phase 8: <see cref="Builder"/> = Bauschiff (<see cref="Simulation.ConstructionSystem"/>).
/// Phase 11: <see cref="Police"/> = Polizeischiff einer Fraktion (<see cref="Simulation.SecuritySystem"/>), <see cref="Pirate"/> = Kaperschiff eines
/// gesetzlosen Akteurs (<see cref="Simulation.PirateSystem"/>).
/// </summary>
public enum ShipRole { Trader, Miner, Fighter, Player, Builder, Police, Pirate }
public enum TradePhase { Idle, GoingToBuy, GoingToSell }
public enum MinePhase { Idle, GoingToField, Mining, GoingToStation }

public sealed class Ship : ICreditAccount
{
    public ShipId Id;
    /// <summary>
    /// Besitzer (Phase 9, ersetzt <c>Ship.Faction</c>): ein Akteur. Fraktionsschiffe gehören dem Fraktionsakteur, Spielerschiffe dem Spieler,
    /// freie Einzelschiffe der Sammelgruppe <see cref="Universe.FreeActor"/> (impliziter Akteur, bis es aufsteigt). Die Fraktion folgt aus dem
    /// Besitzer (<see cref="Universe.FactionOf(Ship)"/>).
    /// </summary>
    public ActorId Owner;
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
    /// <summary>Treibstoff an Bord je Ware, getrennt vom Laderaum; Summe ≤ <see cref="ShipStats.FuelCapacity"/>. Schiffe ohne Modell: leer (Kapazität 0).</summary>
    public Dictionary<GoodId, float> Fuel = new();
    /// <summary>
    /// Betriebskasse des Schiffs (NPC-Schiffe: freie Einzelschiffe als implizite Akteure und Eigenbetriebe von Fraktionen/Einzelakteuren).
    /// Spielerschiffe buchen nicht hierauf, sondern auf die Kasse ihres Besitzers; ihr Wert bleibt 0. Buchende Stellen nutzen
    /// <see cref="Universe.WalletOf(Ship)"/>.
    /// </summary>
    public float Credits;
    float ICreditAccount.Credits { get => Credits; set => Credits = value; }
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
            ClampFuel();
            return true;
        }
        Stats = new ShipStats { Hull = NoModelHull };
        ClampFuel();
        return false;
    }

    /// <summary>Koppelt den Tankinhalt an die Kapazität (Invariante Summe ≤ <see cref="ShipStats.FuelCapacity"/>; z. B. nach Fit-Änderung oder Content-Wechsel). Überschuss wird von der höchsten GoodId abgebaut.</summary>
    void ClampFuel()
    {
        float total = TotalFuel;
        if (total <= Stats.FuelCapacity) return;
        foreach (var k in Fuel.Keys.OrderByDescending(k => k.Value).ToList())
        {
            float take = MathF.Min(Fuel[k], total - Stats.FuelCapacity);
            Fuel[k] -= take;
            total -= take;
            if (Fuel[k] <= 0f) Fuel.Remove(k);
            if (total <= Stats.FuelCapacity) break;
        }
    }

    /// <summary>Treibstoffmenge einer Ware an Bord (0, wenn keine).</summary>
    public float FuelAmount(GoodId good) => Fuel.TryGetValue(good, out var a) ? a : 0f;
    /// <summary>Treibstoff gesamt an Bord (alle zulässigen Waren; der Tank fasst sie gemeinsam).</summary>
    public float TotalFuel => Fuel.Sum(kv => kv.Value);
    /// <summary>Freier Tankplatz gesamt.</summary>
    public float FuelSpace => MathF.Max(0f, Stats.FuelCapacity - TotalFuel);
    /// <summary>Tankt den ersten zulässigen Treibstoff voll auf (Neubau, Migration); ohne Modell/Antrieb/Kapazität: leer.</summary>
    public void FillFuel()
    {
        Fuel.Clear();
        if (Stats.FuelTypes.Count > 0 && Stats.FuelCapacity > 0f)
            Fuel[Stats.FuelTypes[0].Good] = Stats.FuelCapacity;
    }

    /// <summary>Steuert der Spieler dieses Schiff selbst?</summary>
    public bool IsPlayer => Controller == Controller.Player;

    /// <summary>Wirtschaftlich aktives KI-Schiff (Händler oder Miner): Fracht- oder Schürffähigkeit, von der KI gesteuert.</summary>
    public bool IsEconomyAi => Controller == Controller.Ai && (Has(Capability.Cargo) || Has(Capability.Mining));

    /// <summary>Phase 8: von der KI gesteuertes Bauschiff (Etikett <see cref="ShipRole.Builder"/> und Fähigkeit <see cref="Capability.Construction"/>).</summary>
    public bool IsAiBuilder => Controller == Controller.Ai && Role == ShipRole.Builder && Has(Capability.Construction);

    /// <summary>
    /// Phase 12: Bauschiff im Einsatz für eine Baustelle — KI-Bauschiffe und Spielerschiffe mit Befehl „Bauen“ (<see cref="BuildCommand"/>), die
    /// <see cref="Simulation.ConstructionSystem"/> ausführt.
    /// </summary>
    public bool IsSiteBuilder => Role == ShipRole.Builder && Has(Capability.Construction) && (Controller == Controller.Ai || Command is BuildCommand);

    /// <summary>Belegter Laderaum (Einheiten × Volumen je Einheit).</summary>
    public float UsedVolume(Universe u)
    {
        // feste Reihenfolge (GoodId aufsteigend), damit die Gleitkomma-Summe nach Speichern/Laden gleich bleibt (Einfügereihenfolge geht verloren);
        // ohne Allokation, Frachträume haben nur wenige Waren
        float sum = 0f;
        int last = int.MinValue;
        for (int n = 0; n < Cargo.Count; n++)
        {
            int next = int.MaxValue;
            float amount = 0f;
            foreach (var (g, a) in Cargo) if (g.Value > last && g.Value < next) { next = g.Value; amount = a; }
            if (next == int.MaxValue) break;
            sum += amount * u.Goods[new GoodId(next)].VolumePerUnit;
            last = next;
        }
        return sum;
    }

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
    /// <summary>Phase 6: Tick der letzten Aufgabenzuteilung (Kauf oder Wechsel); Mindestdauer der Aufgabe zählt ab hier (gespeichert).</summary>
    public long JobSince;
    /// <summary>Phase 7: Güteklasse aus dem Kauf (gespeichert; Anzeige — die Wirkung steckt in Slots und Fit).</summary>
    public ShipQuality Quality;
    /// <summary>Phase 7: Mitleidszähler je Slot (Fehlschläge in Folge beim Dienst „upgrade“; Schlüssel −1 = Bonusslot). Jeder Fehlschlag erhöht die nächste Chance.</summary>
    public Dictionary<int, int> UpgradePity = new();
    /// <summary>Phase 11: Heimatbasis (Polizei: Wache/Hauptquartier der Fraktion, Piraten: Versteck) zum Reparieren und Auftanken; default = keine (gespeichert).</summary>
    public StationId HomeBase;
    /// <summary>Phase 11: Einsatzsektor (Polizei: zugewiesene Patrouille, Piraten: gewähltes Jagdrevier); default = keiner (gespeichert).</summary>
    public SectorId DutySector;

    /// <summary>
    /// Phase 12: Befehl (Ebene „Befehl“ zwischen Notfall und Automatik, FLOTTEN.md Abschnitt 4); null = Automatik. Ausgeführt vom registrierten
    /// <see cref="Simulation.IOrderExecutor"/> (<see cref="Simulation.OrderSystem"/>). Gespeichert.
    /// </summary>
    public ShipCommand? Command;
    /// <summary>Phase 12: Tick, seit dem der Befehl gilt (gespeichert).</summary>
    public long CommandSince;
    /// <summary>Phase 12: Fortschritt des Ausführers (z. B. Index der Streifenrunde, Phase der Heimkehr; gespeichert).</summary>
    public int CommandStep;
    /// <summary>Phase 12: Notfall-Ebene aktiv (Hülle kritisch oder Tank leer): der Befehl ruht, das Schiff fliegt zur Reparatur; gespeichert.</summary>
    public bool Emergency;
    /// <summary>Phase 12: Verhaltensregel des Schiffs (im Verband gilt die des Verbands); gespeichert.</summary>
    public Engagement Engagement;
    /// <summary>Phase 12: Verband, dem das Schiff angehört (default = keiner); gespeichert.</summary>
    public SquadronId Squadron;
    /// <summary>
    /// Phase 12: Station, an der das Schiff angedockt liegt (default = keine, z. B. unterwegs oder im freien Raum). Gesetzt bei Ankunft am Andockpunkt
    /// (<see cref="Universe.FinishTransit"/>), beim Anlegen und vom Andocken des Spielers; gelöscht beim Abflug. Gespeichert.
    /// </summary>
    public StationId DockedAt;
    /// <summary>Phase 12: Leihschiff — Station, bei der es gemietet wurde (default = eigenes Schiff); gespeichert.</summary>
    public StationId RentedFrom;
    /// <summary>Phase 12: Beginn der Miete (Tick) und hinterlegtes Pfand; gespeichert.</summary>
    public long RentedSince;
    public float RentalDeposit;

    /// <summary>Phase 12: Leihschiff (nicht verkaufbar, Rückgabe am Leihdienst)?</summary>
    public bool IsRental => RentedFrom.Value != 0;
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
/// <summary>
/// Phase 8: Bauauftrag eines Bauschiffs. <paramref name="Site"/> = Baustelle (Station mit <see cref="Station.Site"/>; default = keine, dann
/// verkauft das Schiff nur übrige Fracht an <paramref name="Supplier"/>), <paramref name="Supplier"/> = Station, bei der Material geholt wird
/// (default = keine), <paramref name="Good"/> = geholte Ware.
/// </summary>
public sealed record BuildOrder(StationId Site, StationId Supplier, GoodId Good) : ShipOrder;
