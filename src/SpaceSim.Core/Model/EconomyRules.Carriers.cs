namespace SpaceSim.Core.Model;

/// <summary>
/// Träger (KAMPF.md „Träger und Bordjäger“): ein großer Rumpf mit Hangarplätzen für Bordjäger der Klasse S. Code-Vorgabe aus (Welt bitgleich zu vorher,
/// <see cref="Content.CarrierContent.Strip"/> nimmt Klasse, Modell und Testpaket heraus); die Standardwelt schaltet ein.
/// </summary>
public sealed partial class EconomyRules
{
    /// <summary>Schalter: Träger und Bordjäger.</summary>
    public bool Carriers;
    /// <summary>Höchstabstand Jäger ↔ Träger zum Einschleusen (gleicher Sektor; angedockt an derselben Station zählt ebenfalls).</summary>
    public float CarrierDockRange = 40f;
    /// <summary>Gestartete Jäger stehen in diesem Abstand rund um den Träger.</summary>
    public float CarrierLaunchRadius = 10f;
    /// <summary>Hüllenreparatur eingeschleuster Jäger je Sekunde (Anteil der Höchsthülle); Schild, Munition, Raketen, Speicherbank sofort voll, Brände aus.</summary>
    public float CarrierRepairPerSecond = 0.01f;
    /// <summary>Ein zurückkehrender Jäger landet spätestens nach so vielen Ticks (er holt den Träger im selben Sektor ein).</summary>
    public int CarrierReturnTicks = 160;
    /// <summary>Modell der Bordjäger, die eine KI-Marine mit einem Träger kauft (Materialwert zu Basispreisen aus der Kasse).</summary>
    public string CarrierNpcFighterModel = "falcon";
    /// <summary>KI-Marine darf im Krieg Träger wählen (Polizei und Piraten nie). Ohne Krieg kauft die KI keinen Träger.</summary>
    public bool CarrierNavy = true;
}
