namespace SpaceSim.Core.Model;

/// <summary>
/// Welle E (docs/KAMPF.md „Welle E: Großkampfschiffe für NPCs“): Bauaufträge für Großkampfschiffe (Leviathan) an Werften. Die KI-Marine kauft sonst nur,
/// was eine Werft fertig im Regal hat (<see cref="Simulation.ShipPurchase"/>) — der Leviathan braucht u. a. sechs Laserkanonen M, drei Reaktoren M, zwei
/// Fusionsantriebe L und drei schwere Bausätze und war deshalb für NPCs nie lieferbar. Ein Auftrag hebt an der Werft die Regalziele um den Bedarf
/// (Nachfrage über die normalen Händler und Werke), reserviert das Material, montiert das Schiff und bezahlt die Werft aus einer Treuhand der Fraktion.
/// Code-Vorgabe AUS = altes Verhalten, die Standardwelt schaltet es an.
/// </summary>
public sealed partial class EconomyRules
{
    /// <summary>Hauptschalter: KI-Fraktionen (Staat/Gilde) geben Bauaufträge für Großkampfschiffe auf. Aus = keine Aufträge.</summary>
    public bool CapitalOrders;
    /// <summary>Modelle, die über einen Bauauftrag entstehen (Großkampfschiffe). Das stärkste bezahlbare zuerst.</summary>
    public List<string> CapitalModels = new();
    /// <summary>Preis = Σ Bedarf × Basispreis × (1 + Montagegebühr der Werft) × dieser Faktor (fester Vertragspreis, höher als ein Regalkauf).</summary>
    public float CapitalPriceFactor = 1.5f;
    /// <summary>Höchstens so viele Großkampfschiffe (Modelle aus <see cref="CapitalModels"/>) je Fraktion, laufende Aufträge eingerechnet.</summary>
    public int MaxCapitalShipsPerFaction = 1;
    /// <summary>Abklingzeit zwischen zwei Aufträgen derselben Fraktion (Ticks, ab Auftragsbeginn).</summary>
    public int CapitalOrderCooldownTicks = 60000;
    /// <summary>Kommt das Material in dieser Zeit nicht zusammen, verfällt der Auftrag; der unbezahlte Rest der Treuhand geht an die Kasse zurück.</summary>
    public int CapitalOrderExpiryTicks = 60000;
    /// <summary>Montagezeit, sobald alles Material reserviert ist (Ticks).</summary>
    public int CapitalAssemblyTicks = 1200;
    /// <summary>Auslöser „hohe Bedrohung“: höchste Bedrohung (<see cref="Sector.Threat"/>) im eigenen Gebiet mindestens so hoch. 0 = kein Auslöser.</summary>
    public float CapitalThreat;
    /// <summary>Auslöser „sehr reich“: verfügbares Geld (<see cref="Simulation.FactionViability.Spendable(Universe, Actor)"/>) ≥ Vertragspreis × dieser Faktor. 0 = kein Auslöser.</summary>
    public float CapitalRichFactor;
    /// <summary>Ein Großkampfschiff wird im Frieden nicht abgebaut (nur, wenn die Kasse unter die Rücklage fällt). Aus = wie jedes Kriegsschiff.</summary>
    public bool CapitalKeepInPeace;
}
