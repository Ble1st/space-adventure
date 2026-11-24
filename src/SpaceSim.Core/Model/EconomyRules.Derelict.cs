namespace SpaceSim.Core.Model;

/// <summary>
/// Herrenlose Schiffe und die Aufträge „Panne“ und „Schiff liefern“ (Lücken gegenüber Interstellar Pilot 2, docs/KAMPF.md „Herrenlose Schiffe“).
/// Code-Vorgabe = aus (altes Verhalten: ein zerstörtes Schiff hinterlässt ein Wrack, keine Ereignisse, keine neuen Aufträge); die Standardwelt
/// schaltet alles an.
/// </summary>
public sealed partial class EconomyRules
{
    /// <summary>Hauptschalter: NPC-Piloten steigen mit <see cref="DerelictChance"/> aus einem zerstörten Schiff aus — es bleibt herrenlos statt als Wrack.</summary>
    public bool DerelictShips;
    /// <summary>Chance je zerstörtem Nicht-Spielerschiff (eine Zahl aus <see cref="Universe.Rng"/>).</summary>
    public float DerelictChance = 0.25f;
    /// <summary>Hülle eines verlassenen Schiffs: zwischen diesem Anteil …</summary>
    public float DerelictHullMin = 0.05f;
    /// <summary>… und diesem Anteil der vollen Hülle (aus derselben Zufallszahl abgeleitet, keine zweite).</summary>
    public float DerelictHullMax = 0.15f;
    /// <summary>So lange treibt ein herrenloses Schiff, bevor es verfällt (Ticks; wie ein Wrack).</summary>
    public long DerelictTicks = 12000;
    /// <summary>Höchstabstand eines eigenen Schiffs zum Übernehmen.</summary>
    public float DerelictClaimRange = 30f;
    /// <summary>Zufallsereignis „herrenloses Schiff“: Länge eines Ereignis-Takts (Ticks), 0 = aus. Eigener RNG aus Welt-Seed und Takt (nie <see cref="Universe.Rng"/>).</summary>
    public long DerelictEventTicks;
    /// <summary>Chance je Ereignis-Takt, dass irgendwo in einem bekannten Sektor ein herrenloses Schiff auftaucht.</summary>
    public float DerelictEventChance = 0.5f;

    /// <summary>Auftrag „Panne“ (Sektor-HQ, Polizei, Werft): Chance je Station und Angebots-Takt; 0 = keine Angebote.</summary>
    public float MissionBreakdownChance;
    /// <summary>Belohnung Panne: Grundbetrag …</summary>
    public float MissionBreakdownBaseReward = 400f;
    /// <summary>… plus je geschätzter Flugminute (hin und zurück).</summary>
    public float MissionBreakdownRewardPerMinute = 40f;
    /// <summary>Auftrag „Schiff liefern“ (Stationen mit Schiffshändler): Chance je Station und Angebots-Takt; 0 = keine Angebote.</summary>
    public float MissionShipDeliveryChance;
    /// <summary>Belohnung = Materialwert des Modells (Basispreise) × dieser Faktor — über dem Hangar-Verkaufspreis (<see cref="ShipSellShare"/>).</summary>
    public float MissionShipDeliveryFactor = 0.8f;
    /// <summary>Frist „Schiff liefern“ (Ticks) — Zeit zum Kaufen oder Holen des Schiffs.</summary>
    public int MissionShipDeliveryTicks = 9600;
}
