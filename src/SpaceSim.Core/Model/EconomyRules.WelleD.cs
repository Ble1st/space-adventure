namespace SpaceSim.Core.Model;

/// <summary>
/// Welle D (Spieltests „Supermacht“ nach Welle C, docs/FLOTTEN.md „Welle D“): Versorgung einer großen Spielerflotte und Nachschub an eigenen Stationen.
/// Code-Vorgabe AUS = altes Verhalten, die Standardwelt schaltet alles an. Wirkt nur auf Spielerwege (eigene Schiffe, eigene Stationen) — mit Ausnahme der
/// Werftregale großer Module (Punkt 2) und der Werftregale des Schattens (Punkt 3), siehe dort.
/// </summary>
public sealed partial class EconomyRules
{
    // ───────── 1: Flottentreibstoff ─────────

    /// <summary>
    /// Befehl „Versorgen“ (Tanker): ein eigenes Schiff mit Treibstoff, Munition oder Raketen im Laderaum füllt Tank, Magazin und Raketenschacht der eigenen
    /// Schiffe seines Verbands und seines Sektors in <see cref="FleetSupplyRange"/> aus der Fracht nach. Aus = kein Befehl.
    /// </summary>
    public bool FleetSupply;
    /// <summary>Reichweite des Versorgers (Einheiten).</summary>
    public float FleetSupplyRange = 60f;
    /// <summary>Abgabetakt des Versorgers (Ticks).</summary>
    public int FleetSupplyTicks = 20;
    /// <summary>
    /// Gemessen (Spielerbot nach Welle C, Tick 260 000–720 000): 98 % der Flotten-Treibstoffkosten fielen bei Handels- und Schürfautomatik an, nicht bei
    /// Kampfschiffen; die Schürfautomatik (<see cref="Simulation.MinerSystem.BestMine"/>) rechnete Treibstoff gar nicht und fuhr mit Großschürfern über die
    /// ganze Karte. An = eigene Schiffe ziehen die Treibstoffkosten der Fahrt vom Wert einer Schürfrunde ab (wie die Handelsautomatik). KI unverändert.
    /// </summary>
    public bool PlayerMinerFuelCost;
    /// <summary>
    /// Eigene Schiffe tanken an einer Tankstelle mindestens bis zur Reserve (<see cref="Simulation.Refuel.MinFuelReserveFraction"/> des Tanks), auch wenn
    /// die Kasse klein ist (KI: höchstens 30 % ihrer Credits). Gemessen: mit fast leerer Spielerkasse tankten die Schiffe je Fahrt nur den Bedarf, kamen
    /// leer an und kauften danach zum doppelten Preis nach (Notkauf bis 280 000 Cr/h). Aus = alte Regel.
    /// </summary>
    public bool PlayerTankReserve;
    /// <summary>Ein eigenes Schiff kauft keinen Notkauf, solange ein eigener Versorger (Befehl „Versorgen“) im Sektor die Sorte an Bord hat — es wartet.</summary>
    public bool PlayerWaitForSupplier;

    // ───────── 2: Großkampfschiff-Regale ─────────

    /// <summary>
    /// Ungenutzte Werftregale der großen Module (<see cref="YardLargeModuleKeys"/>) senken ihr Ziel nicht unter <see cref="YardLargeModuleTarget"/>
    /// (Ausbau Punkt 12 senkte es auf 1 — dann nahm die Werft auch vom Spieler nur 3 Laserkanonen M an, der Schlachtkreuzer braucht 6). Aus = alte Regel.
    /// </summary>
    public bool IdleShelfKeepLargeYard;

    // ───────── 3: Schatten (Blockadebrecher) kaufbar ─────────

    /// <summary>
    /// Module, deren Werftregal (Station mit Schiffshändler) sein Ziel durch Ungenutztheit nicht unter den Wunschbestand des Stationstyps senkt — die
    /// Bauteile des Schattens (Reaktor Mk2, Triebwerk M, Universalfrachtraum 60, Sprungantrieb, Tarnfeld). Leer = alte Regel.
    /// </summary>
    public List<string> IdleShelfKeepKeys = new();

    // ───────── 4: Nachschub an eigenen Stationen ─────────

    /// <summary>
    /// Eigene Versorgungsstationen (Typ mit Reparatur oder Nachladen: Behörden, Wachen, Werften) bezahlen Regalwaren (Lager ohne Rezept: Hüllenplatten,
    /// Elektronik, Munition, Instandhaltung) aus der Spielerkasse, wenn ihre eigene Kasse nicht reicht; eigene Stationen ohne Handelsdienst nehmen dann auch
    /// Lieferungen des Spielers über „trade“ an: alle 50 Ticks wird die Stationskasse bis zum Wert der fehlenden Regalware aufgefüllt (höchstens
    /// <see cref="PlayerStationFundPerDay"/> je Station und Tag, nie unter den Vorrat der Behörden). Aus = Stationen leben von ihrer eigenen Kasse.
    /// </summary>
    public bool PlayerStationFunding;
    /// <summary>Höchstens so viel je Station und Spieltag aus der Spielerkasse.</summary>
    public float PlayerStationFundPerDay = 6000f;
    /// <summary>Wunschbestand der Munitionsregale eigener Stationen mit Nachladedienst (Werften, Wachen) — mindestens so hoch; 0 = unverändert.</summary>
    public float PlayerStationAmmoTarget;
    /// <summary>Wunschbestand der Raketenregale eigener Stationen mit Nachladedienst — mindestens so hoch; 0 = unverändert.</summary>
    public float PlayerStationMissileTarget;
}
