namespace SpaceSim.Core.Model;

/// <summary>
/// Bauplan (eigene Schiffsvariante, Lücke gegenüber Interstellar Pilot 2): Name, Modell (gibt Klasse und Werks-Steckplätze vor) und Fit je
/// Werks-Steckplatz der Klasse. Liegt am Spielerakteur (<see cref="Actor.Blueprints"/>), gespeichert additiv, gehasht nur mit Spielerpolitik.
/// Regeln in <see cref="Simulation.Blueprints"/>.
/// </summary>
public sealed class ShipBlueprint
{
    public int Id;
    public string Name = "";
    public string ModelKey = "";
    public List<string?> Fit = new();
    public long Created;
}

/// <summary>
/// Söldnervertrag: ein bewaffnetes Schiff des Anbieters (Gilde oder Unabhängiger) begleitet für <see cref="Days"/> Spieltage das aktive Schiff des
/// Spielers. Sold je Tag im Voraus aus der Spielerkasse an die Kasse des Anbieters. Nach Ablauf (oder bei leerer Kasse) kehrt das Schiff zu seiner
/// Station zurück und verlässt die Welt. Gespeichert und gehasht nur, wenn vorhanden. Regeln in <see cref="Simulation.Mercenaries"/>.
/// </summary>
public sealed class MercContract
{
    public ShipId Ship;
    public ActorId Actor;
    public StationId Home;
    public long Since;
    public int Days;
    public int PaidDays;
    public float FeePerDay;
    /// <summary>Vertrag beendet, das Schiff fliegt heim (danach verlässt es die Welt).</summary>
    public bool Ending;
}
