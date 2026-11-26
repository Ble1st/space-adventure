namespace SpaceSim.Core.Model;

/// <summary>
/// Welle B (Befunde aus drei Spielerbot-Durchspielen, Ziel „Supermacht“): Stellschrauben als eigener Teil der Regeln (gespeichert wie alle Regeln).
/// Code-Vorgabe AUS = altes Verhalten, die Standardwelt schaltet alles an. Ohne Spieler ändern nur der abnehmende Einfluss (Punkt 2a) und die
/// größeren Werftziele (Punkt 1) die NPC-Welt (gemessen, siehe docs/POLITIK.md „Welle B“).
/// </summary>
public sealed partial class EconomyRules
{
    // ───────── 1: Großkampfschiffe beschaffbar ─────────

    /// <summary>
    /// Modulregale (Lager ohne Rezept, Modulware) nehmen von Spielerschiffen bis Wunschbestand × diesem Faktor an — der Spieler kann gekaufte Module an
    /// die Werft liefern, die ein Großkampfschiff braucht. NPC-Händler unverändert. 0 = aus (altes Regal-Limit).
    /// </summary>
    public float PlayerModuleShelfFactor;

    /// <summary>
    /// Wunschbestand der Werftregale (Typen mit Schiffshändler) für die Module in <see cref="YardLargeModuleKeys"/> mindestens so hoch (leer: alle Module der
    /// Größe M und mehr); 0 = aus.
    /// </summary>
    public int YardLargeModuleTarget;
    /// <summary>
    /// Module, deren Werftziel <see cref="YardLargeModuleTarget"/> anhebt (die Bauteile der Großkampfschiffe). Gemessen: alle M-Module anzuheben ließ
    /// ungenutzte Regale (Kühlplatten M) auf 0,28× fallen — darum eine Liste.
    /// </summary>
    public List<string> YardLargeModuleKeys = new();

    // ───────── 2a: abnehmender Einfluss ─────────

    /// <summary>
    /// Einfluss je Besitzer, Stationstyp und Sektor: die ersten so viele Stationen eines Typs zählen voll, jede weitere nur
    /// <see cref="InfluenceExtraFactor"/>. 0 = aus (jede Station zählt voll).
    /// </summary>
    public int InfluenceFullStations;
    /// <summary>Anteil des Einflusses jeder weiteren Station desselben Typs (siehe <see cref="InfluenceFullStations"/>).</summary>
    public float InfluenceExtraFactor = 1f;

    // ───────── 2b: KI reagiert auf Spieler-Stationsflut ─────────

    /// <summary>
    /// KI-Reaktion, wenn der Spieler den Sektor einer KI-Fraktion umkämpft (Spieler-Einfluss &gt; <see cref="ContestedShare"/>): Gegenbau, Sperre gegen
    /// den Spieler, bei schlechter Beziehung Fehde. Aus = keine Reaktion.
    /// </summary>
    public bool PlayerContestReaction;
    /// <summary>Mindest-Sperrstufe gegen den Spieler in einem von ihm umkämpften Sektor (gedeckelt wie jede Sperre durch die Kontrollstufe).</summary>
    public int PlayerContestLockLevel = 1;
    /// <summary>Takt der Gegenbau-Entscheidung: höchstens ein Gegenbau je Sektor und so vielen Ticks (zustandslos über den Tick).</summary>
    public int PlayerContestReactTicks = 2400;
    /// <summary>Stationstypen des Gegenbaus in dieser Reihenfolge (der erste, von dem die Fraktion im Sektor weniger als <see cref="PlayerContestCounterMax"/> hat).</summary>
    public List<string> PlayerContestCounterTypes = new();
    /// <summary>Höchstens so viele Gegenbauten je Typ und Sektor (eigene Stationen dieses Typs dort).</summary>
    public int PlayerContestCounterMax = 2;

    // ───────── 3: Flottengewinn ─────────

    /// <summary>
    /// Abgabe auf den Handelsgewinn der Spielerflotte (Schiffe außer dem aktiven): Verkäufe minus Einkäufe dieser Schiffe werden gesammelt, alle
    /// <see cref="PlayerFleetLevyTicks"/> geht dieser Anteil eines positiven Saldos aus der Spielerkasse an die Fraktion des Verkaufssektors. 0 = aus.
    /// </summary>
    public float PlayerFleetLevyShare;
    /// <summary>Abrechnungstakt der Flottenabgabe (Ticks).</summary>
    public int PlayerFleetLevyTicks = 2400;

    // ───────── 4: Spende ─────────

    /// <summary>Comms-Aktion „Spende“: Geld an eine KI-Fraktion gegen Ansehen. Aus = keine Aktion.</summary>
    public bool Donations;
    /// <summary>Ansehen je 1000 Cr Spende.</summary>
    public float DonationPer1000 = 0.02f;
    /// <summary>Höchstens so viel Ansehen je Fraktion und Tag aus Spenden.</summary>
    public float DonationDailyCap = 0.1f;
    /// <summary>Über diese Beziehung hinaus bringt eine Spende nichts mehr (Bündnisse nicht kaufbar).</summary>
    public float DonationMaxRelation = 0.45f;
    /// <summary>Wählbare Spendenbeträge (Comms-Knöpfe).</summary>
    public List<float> DonationAmounts = new() { 1000f, 5000f };

    // ───────── 5: Aufträge bei schlechter Beziehung ─────────

    /// <summary>
    /// Chance auf ein zusätzliches Transport- bzw. Lieferangebot je Station und Angebotsrunde, wenn der Auftraggeber dem Spieler gegenüber unter 0 steht
    /// oder eine etablierte Banditenfraktion ist (eigener Missions-RNG). 0 = aus.
    /// </summary>
    public float MissionLowStandingExtraChance;

    // ───────── 6: Bündnisschwelle des Spielers ─────────

    /// <summary>Mindestbeziehung für ein Bündnis mit der Spielerfraktion (nur Paare mit dem Spieler); 0 = aus (<see cref="AllianceMinRelation"/>).</summary>
    public float PlayerAllianceMinRelation;

    // ───────── 7: Kaufwarnung ─────────

    /// <summary>Werftangebote warnen, wenn ein Kauf über der Befehlskapazität läge (kein Verbot). Aus = keine Warnung.</summary>
    public bool ShipyardCapacityWarning;
}
