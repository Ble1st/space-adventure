namespace SpaceSim.Core.Model;

/// <summary>
/// Ausbau Punkt 17 — zwei Banditenarten (docs/BANDITEN.md). Etablierte Banditen = gesetzlose Fraktion (ActorKind.Outlaw **mit**
/// Faction-Eintrag): eigener Sitz, eigene Wirtschaft, Schutz- und Kaperflotte, Zoll statt Steuern, eigenes Gebiet.
/// Code-Vorgabe aus; die Standardwelt schaltet an und stellt die Fraktion. Aus (oder ohne solche Fraktion) rechnet die Welt wie vorher.
/// </summary>
public sealed partial class EconomyRules
{
    /// <summary>Hauptschalter: gesetzlose Fraktionen mit Sitz, Wirtschaft, Flotten, Zoll. Aus = WorldBuilder lässt solche Fraktionen
    /// und alles, was ihnen gehört, aus der Welt (die Bau-Obergrenze sinkt entsprechend).</summary>
    public bool BanditFactions;

    /// <summary>Dauerzoll: Sperrstufe, die eine Banditenfraktion in ihren Sektoren hält (0 = kein Zoll, höchstens 2 — Stufe 2 wäre ein
    /// Embargo und sperrte beladene Spieler aus).</summary>
    public int BanditTollLevel = 1;
    /// <summary>
    /// Durchsetzungskosten des Dauerzolls je Stufe und Tick. 0 = die Bande setzt ihn mit ihren eigenen Schiffen durch (die zahlen
    /// ohnehin Unterhalt) und die Kasse wird nicht zusätzlich belastet. Wichtig: mit den normalen 0,5 Cr/Stufe/Tick fraß die
    /// Durchsetzung (10 000 Cr in 20 000 Ticks) jede Zolleinnahme auf, die Sperre fiel auf „Papier" und der Zoll verschwand — eine
    /// Sackgasse wie Abgrund trägt keine Verwaltungskosten.
    /// </summary>
    public float BanditTollUpkeepPerLevel;

    /// <summary>Kapazitätseinheiten der Kaperflotte einer Banditenfraktion (S 1, M 1,5, L 2; ersetzt für sie maxRaiderUnitsPerActor).</summary>
    public float BanditMaxRaiderUnits = 3f;

    /// <summary>Schutzflotte: höchstens so viele Kriegsschiffe.</summary>
    public int BanditWarships = 2;
    /// <summary>Unterhalt je Schutzschiff und Tick aus der Banditenkasse.</summary>
    public float BanditWarshipUpkeepPerTick = 0.8f;
    /// <summary>Rücklage, die die Kasse für den Schutzflottenkauf behalten muss.</summary>
    public float BanditWarshipReserve = 3000f;
    /// <summary>Modell der Schutzflotte (muss ein Kampfmodell ohne Fracht/Bergbau/Bau sein).</summary>
    public string BanditWarshipModel = "warden";
    /// <summary>Abklingzeit zwischen zwei Streitkraft-Käufen (Schutzschiff) — gemeinsam mit dem Kaperneubau über Actor.LastForceTick.</summary>
    public int BanditFleetCooldownTicks = 3000;

    /// <summary>Neubau einer verlorenen Wirtschaftsstation: Kosten = Baukosten × Faktor (Geldsenke, ohne Baustelle und Material).</summary>
    public float BanditRebuildMoneyFactor = 1.5f;
    /// <summary>Abklingzeit zwischen zwei Banditenbauten (Wirtschaftsstation oder Vorposten) in Ticks.</summary>
    public int BanditRebuildCooldownTicks = 3000;
    /// <summary>
    /// Sollbestand der Wirtschaft: Typ-Schlüssel → Anzahl, die die Fraktion in ihrem Gebiet halten will (Neubau bei Verlust,
    /// Muster <see cref="Simulation.Outposts.Build"/>). In der Standardwelt steelworks/plating_works/electronics_fab je 1.
    /// Leer = kein Nachbau (dann bleibt eine zerstörte Station weg).
    /// </summary>
    public Dictionary<string, int> BanditEconomyTypes = new();

    /// <summary>Vorposten in fremden, herrenlosen Sektoren (Sektor erobern).</summary>
    public bool BanditExpansion;
    /// <summary>Sektoren, in denen ein Vorposten entstehen darf — nur solange herrenlos und ohne Spielerbesitz.</summary>
    public List<int> BanditExpansionSectors = new();
    /// <summary>Stationstyp des Vorpostens (muss einen Markt haben, damit er Einfluss zählt).</summary>
    public string BanditExpansionType = "warehouse";
    /// <summary>Kasse muss Baukosten × Faktor erreichen, bevor ein Vorposten gebaut wird.</summary>
    public float BanditExpansionMoneyFactor = 4f;

    /// <summary>Kassenziel: darüber zahlt die Fraktion je Tick einen Beuteanteil an die Mannschaft (Geldsenke, kein Dauerhorten).</summary>
    public float BanditTreasuryTarget = 40000f;
    /// <summary>Anteil des Überschusses über dem Ziel, der je Tick als Beuteanteil abgeht (0 = keine Senke).</summary>
    public float BanditHoardShare = 0.0002f;

    /// <summary>Deckel der Hehlerei für Banditenfraktionen (0 = ungedeckelt; die kleinen Gruppen bleiben bei pirateTreasuryCap).</summary>
    public float BanditFenceCap;
}
