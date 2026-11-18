namespace SpaceSim.Core.Model;

/// <summary>
/// Spielersperre (Wunsch „Spieler sperren wenn Beziehung schlecht“, POLITIK.md „Verbrechen senken das Ansehen“): Verbrechen des Spielers senken zusätzlich
/// zur Fahndung sein Ansehen bei der verfolgenden KI-Fraktion (<see cref="Simulation.CrimeStanding"/>). Die Sperrstufen folgen dann über die bestehende
/// Regel <see cref="Simulation.FactionPolicySystem"/> (schlechteste Beziehung, Schwellen <see cref="LockThresholds"/>). Code-Vorgabe leer = aus; die
/// Standardwelt schaltet es an. Gespeichert wie alle Regeln.
/// </summary>
public sealed partial class EconomyRules
{
    /// <summary>Ansehensverlust je gemeldetem Verbrechen (Schlüssel wie <see cref="CrimeHeat"/>, dazu „kill“ = Abschuss eines Schiffs zusätzlich zum Angriff); leer/0 = aus.</summary>
    public Dictionary<string, float> CrimeStandingLoss = new();
    /// <summary>Höchstens so viel Ansehen verliert der Spieler je Fraktion und Tag (<see cref="DayTicks"/>, festes Fenster ab dem ersten Verlust) — ein Gefecht springt nicht auf −1; 0 = ohne Deckel.</summary>
    public float CrimeStandingCapPerDay;
    /// <summary>Erholung je Tag, solange die Fahndungsstufe bei der Fraktion 0 ist und kein Krieg herrscht — höchstens bis zum Wert vor dem ersten Verbrechen.</summary>
    public float CrimeStandingRecoveryPerDay;
    /// <summary>Sofortige Erholung, wenn der Spieler seine Strafe am Gericht (oder per Comms) bezahlt (ebenfalls höchstens bis zum Wert vor dem Verbrechen).</summary>
    public float CrimeStandingFineBonus;
}
