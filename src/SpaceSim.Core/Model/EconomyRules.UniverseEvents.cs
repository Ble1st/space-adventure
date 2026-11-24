namespace SpaceSim.Core.Model;

/// <summary>
/// Zufällige Universumsereignisse (docs/ERKUNDUNG.md „Universumsereignisse“): Erzschwemme, Frachtfund, Wrackfeld. Code-Vorgabe aus (keine Ereignisse, Welt
/// bitgleich); die Standardwelt schaltet ein. Zufall aus einem eigenen, aus Welt-Seed und Takt abgeleiteten RNG — <see cref="Universe.Rng"/> bleibt unberührt.
/// </summary>
public sealed partial class EconomyRules
{
    /// <summary>Schalter: zufällige Universumsereignisse.</summary>
    public bool UniverseEvents;
    /// <summary>Alle so viele Ticks wird gewürfelt, ob ein neues Ereignis beginnt.</summary>
    public int UniverseEventCheckTicks = 1200;
    /// <summary>Wahrscheinlichkeit eines neuen Ereignisses je Prüfung.</summary>
    public float UniverseEventChance = 0.5f;
    /// <summary>Höchstens so viele Ereignisse laufen gleichzeitig.</summary>
    public int UniverseEventMaxActive = 3;
    /// <summary>Dauer eines Ereignisses in Ticks (gleichverteilt zwischen Min und Max).</summary>
    public int UniverseEventMinTicks = 6000;
    public int UniverseEventMaxTicks = 9600;
    /// <summary>Gewichte der Arten (0 = Art aus).</summary>
    public float OreSurgeWeight = 1f;
    public float CargoFindWeight = 1f;
    public float WreckFieldWeight = 1f;
    /// <summary>Erzschwemme: so viele zusätzliche Felsen (zwischen Min und Max) mit dem Erzfaktor <see cref="OreSurgeRichFactor"/>.</summary>
    public int OreSurgeMinRocks = 3;
    public int OreSurgeMaxRocks = 5;
    public float OreSurgeRichFactor = 2.5f;
    /// <summary>Frachtfund: Behälter (zwischen Min und Max), je Behälter ein Warenwert (Basispreis) zwischen den Grenzen.</summary>
    public int CargoFindMinContainers = 1;
    public int CargoFindMaxContainers = 3;
    public float CargoFindMinValue = 1500f;
    public float CargoFindMaxValue = 4000f;
    /// <summary>Wrackfeld: so viele Wracks (zwischen Min und Max), je Wrack 1–3 Module mit Restzustand zwischen den Grenzen.</summary>
    public int WreckFieldMinWrecks = 3;
    public int WreckFieldMaxWrecks = 5;
    public float WreckFieldMinCondition = 0.4f;
    public float WreckFieldMaxCondition = 0.95f;
}
