namespace SpaceSim.Core.Model;

/// <summary>
/// Aufgabe/Etikett eines Schiffs (gespeichert als Text). Phase 8: <see cref="Builder"/> = Bauschiff (<see cref="Simulation.ConstructionSystem"/>).
/// Phase 11: <see cref="Police"/> = Polizeischiff einer Fraktion (<see cref="Simulation.SecuritySystem"/>), <see cref="Pirate"/> = Kaperschiff eines
/// gesetzlosen Akteurs (<see cref="Simulation.PirateSystem"/>).
/// </summary>
/// <summary>Aufgabe (Etikett). Phase 28: <see cref="Warship"/> = Kriegsschiff einer Fraktion (nur im Krieg gekauft).</summary>
public enum ShipRole { Trader, Miner, Fighter, Player, Builder, Police, Pirate, Warship, Collector, Salvager, Smuggler, Derelict }
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
    /// <summary>Lücken gegenüber IP2: vom Spieler vergebener Name ("" = Modellname und Nummer, <see cref="ShipNames.Display"/>). Gespeichert, nicht gehasht (nur Anzeige).</summary>
    public string CustomName = "";
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
    /// <summary>
    /// Runde 41: versiegelte Fracht eines Transportauftrags (belegt Laderaum-Volumen, ist keine Ware; nur Spielerschiffe).
    /// Additiv gespeichert und gehasht, sobald &gt; 0. Wird bei Annahme/Ablieferung/Scheitern gesetzt (siehe <see cref="Missions.TransportMission"/>).
    /// </summary>
    public float MissionCargo;
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
            Stats = ShipStats.Compute(cls, fit, Systems.Condition.Count > 0 ? Systems.Condition : null, u.Rules.ModuleFailCondition);   // Phase 23: ausgefallene Module
            Capabilities = Stats.Capabilities;   // Schiffe mit Modell: Fähigkeiten folgen immer dem Fit
            // Sprungantrieb/Tarnfeld: aktiv kosten sie Leistung (der Überschuss sinkt für Schild, Bank, Antrieb). Fällt das Modul aus, beendet das
            // JumpCloakSystem den Zustand mit Ereignis (hier nur keine Last mehr).
            if (Cloaked && Stats.Cloak > 0f) Stats.PowerDraw += Stats.CloakDraw;
            if (JumpTarget.Value != 0 && Stats.JumpRange > 0) Stats.PowerDraw += Stats.JumpDraw;
            if (JammerOn && Stats.Jamming > 0f) Stats.PowerDraw += Stats.JamDraw;   // Raketenlenkung: Störsender an
            ClampFuel();
            ClampSystems();
            Combat.ShipSystems.Wake(u, this);   // Phase 24: nach einem Modulwechsel kann sich ein bisher erholungsloses Schiff wieder erholen
            return true;
        }
        Stats = new ShipStats { Hull = NoModelHull };
        ClampFuel();
        ClampSystems();
        return false;
    }

    /// <summary>Phase 23: Fehlbeträge von Schild, Bank und Magazin auf die (neuen) Höchstwerte begrenzen (z. B. nach Ausbau eines Schildmoduls).</summary>
    void ClampSystems()
    {
        Systems.ShieldMissing = Math.Clamp(Systems.ShieldMissing, 0f, Stats.Shield);
        Systems.ChargeMissing = Math.Clamp(Systems.ChargeMissing, 0f, Stats.PowerStorage);
        Systems.AmmoMissing = Math.Clamp(Systems.AmmoMissing, 0f, Stats.AmmoCapacity);
    }

    /// <summary>
    /// Phase 23: Laufzeitzustand der Bordsysteme (Schild, Speicherbank, Magazin als Fehlbeträge, Zustand je Slot, Leistungsverteilung). Gespeichert und
    /// gehasht. Fortgeschrieben von <see cref="Combat.ShipSystemsSystem"/>, nur solange etwas fehlt.
    /// </summary>
    public ShipSystemsState Systems = new();
    /// <summary>Phase 23: aktueller Schild (Höchstwert <see cref="ShipStats.Shield"/>). Schreiben nur über <see cref="Combat.ShipSystems"/>, damit das System die Erholung übernimmt.</summary>
    public float ShieldNow => MathF.Max(0f, Stats.Shield - Systems.ShieldMissing);
    /// <summary>Phase 23: Ladung der Speicherbank (Höchstwert <see cref="ShipStats.PowerStorage"/>).</summary>
    public float Charge => MathF.Max(0f, Stats.PowerStorage - Systems.ChargeMissing);
    /// <summary>Phase 23: Munition im Magazin ohne Rücksicht auf den Schalter <c>ammoActive</c> (mit Schalter: <see cref="Combat.ShipSystems.Ammo"/>).</summary>
    public float AmmoLoaded => MathF.Max(0f, Stats.AmmoCapacity - Systems.AmmoMissing);
    /// <summary>Phase 23: Leistungsverteilung (setzen über <see cref="Combat.ShipSystems.SetMode"/>).</summary>
    public PowerMode PowerMode => Systems.Mode;
    /// <summary>Phase 23: Zustand eines Slots 0..1.</summary>
    public float ConditionOf(int slot) => Systems.ConditionOf(slot);

    /// <summary>Koppelt den Tankinhalt an die Kapazität (Invariante Summe ≤ <see cref="ShipStats.FuelCapacity"/>; z. B. nach Fit-Änderung oder Content-Wechsel). Überschuss wird von der höchsten GoodId abgebaut.</summary>
    void ClampFuel()
    {
        // Gasernte (Ionenantrieb): Treibstoff, den kein eingebauter Antrieb mehr verbrennt (z. B. Treibstoff nach dem Tausch gegen ein Ionentriebwerk),
        // wird abgelassen — sonst belegte er den Tank. Bestehende Schiffe tragen nur zulässigen Treibstoff (nichts ändert sich).
        if (Stats.FuelTypes.Count > 0 && Fuel.Count > 0)
            foreach (var k in Fuel.Keys.Where(k => Stats.FuelTypes.FindIndex(f => f.Good == k) < 0).ToList()) Fuel.Remove(k);
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

    /// <summary>Herrenlos (verlassen oder liegengeblieben): niemand steuert es (<see cref="Controller.None"/>), kein System bewegt es; übernehmen über <see cref="Simulation.Derelicts.Claim"/>.</summary>
    public bool IsDerelict => Role == ShipRole.Derelict;

    /// <summary>Herrenlos bis zu diesem Tick, dann verfällt es (0 = kein Verfall, z. B. Pannenschiff eines Auftrags). Gespeichert, gehasht nur wenn gesetzt.</summary>
    public long DerelictUntil;

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
    /// <summary>Phase 32: Asteroid-Id, an dem die Harpunen des Schiffs hängen (0 = nicht verankert); siehe <c>Mining.MiningAnchor</c>.</summary>
    public int AnchorRock;
    /// <summary>Phase 32: Verankerungsfortschritt in Ticks (hält ab <see cref="EconomyRules.AnchorTicks"/>).</summary>
    public float AnchorProgress;

    /// <summary>Sprungantrieb: Zielsektor des laufenden Aufladens (0 = lädt nicht); siehe <see cref="Flight.JumpDrive"/>. Gespeichert, gehasht nur wenn gesetzt.</summary>
    public SectorId JumpTarget;
    /// <summary>Sprungantrieb: geladene Ticks.</summary>
    public float JumpCharge;
    /// <summary>Sprungantrieb: Ladepunkt (wer sich weiter als <see cref="EconomyRules.JumpMoveTolerance"/> entfernt, bricht ab).</summary>
    public Vec2 JumpFrom;
    /// <summary>Sprungantrieb: bis zu diesem Tick (ausschließlich) kühlt der Antrieb nach einem Sprung ab.</summary>
    public long JumpCooldownUntil;
    /// <summary>Tarnfeld: eingeschaltet (<see cref="Flight.Cloak"/>). Gespeichert, gehasht nur wenn gesetzt.</summary>
    public bool Cloaked;
    /// <summary>Raketenlenkung: fehlende Täuschkörper im Magazin (0 = voll); siehe <see cref="Combat.Countermeasures"/>. Gespeichert, gehasht nur wenn gesetzt.</summary>
    public float FlaresMissing;
    /// <summary>Raketenlenkung: ausgestoßene Täuschkörper wirken bis zu diesem Tick (ausschließlich).</summary>
    public long FlaresUntil;
    /// <summary>Raketenlenkung: Störsender eingeschaltet (kostet <see cref="ShipStats.JamDraw"/> Leistung).</summary>
    public bool JammerOn;
    /// <summary>Träger: Heimatträger eines Bordjägers (gestartet: im Raum; eingeschleust: liegt in <see cref="Universe.Hangars"/>). default = kein Bordjäger. Gespeichert, gehasht nur wenn gesetzt.</summary>
    public ShipId CarrierHome;
    /// <summary>Träger: gestarteter Bordjäger kehrt seit diesem Tick zum Träger zurück (-1 = nein).</summary>
    public long CarrierReturnSince = -1;
    /// <summary>Träger: gestarteter Bordjäger hat seit dem Start gekämpft (nach dem Gefecht kehrt er von selbst zurück).</summary>
    public bool CarrierFought;

    /// <summary>KI denkt erst ab diesem Tick wieder nach (spart Rechenzeit bei untätigen Schiffen).</summary>
    public long NextThinkTick;
    /// <summary>Tick, seit dem das Schiff keinen Auftrag mehr hat (-1 = beschäftigt).</summary>
    public long IdleSince = -1;
    /// <summary>
    /// Phase 19: seit wann ein KI-Händler Fracht an Bord hat, die keine erreichbare Station annahm (-1 = keine). Nach
    /// <see cref="EconomyRules.StrandedCargoTicks"/> wirft er sie im All ab. Gespeichert.
    /// </summary>
    public long StrandedSince = -1;
    /// <summary>Liegezeit: Tick der letzten Ankunft an einer Station (-1 = keine/ausgeschaltet). Gespeichert, gehasht nur wenn gesetzt.</summary>
    public long DockedSince = -1;
    /// <summary>Phase 22: Rentabilitätsmessung (Grund c für das Ausmustern; <see cref="Simulation.Profitability"/>). Gespeichert und gehasht.</summary>
    public ProfitMeter Profit = new();
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
    /// <summary>Phase 26: bereits abgerechnete Miettage (die Miete läuft täglich über <see cref="Simulation.HangarSystem"/>, nicht erst bei Rückgabe).</summary>
    public int RentPaidDays;

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
