namespace SpaceSim.Core.Model;

/// <summary>
/// Phase 28 (KAMPF.md Stufe 28): Verteidigung eines Stationstyps (JSON <c>defense</c>). Hülle, Schild mit Regeneration je Sekunde, Panzerung, Geschütze
/// (Modulschlüssel von Waffen; die Station versorgt sie selbst — keine Munition, keine Leistung) und Punktabwehr gegen Raketen.
/// </summary>
public sealed class StationDefenseConfig
{
    public float Hull;
    public float Shield;
    public float ShieldRegen;
    public float Armor;
    public float PointDefense;
    public List<string> Guns = new();
}

/// <summary>
/// Phase 28: Kampfzustand einer Station als Fehlbeträge (wie die Bordsysteme der Schiffe): fehlende Hülle und fehlender Schild, Schildpause, Nachladeuhren
/// je Geschütz (Sekunden), ausgeschaltet (Hülle 0 bei einer Station, die nicht zerstört werden darf). Null an der Station = unbeschädigt.
/// </summary>
public sealed class StationCondition
{
    public float HullMissing;
    public float ShieldMissing;
    public long ShieldPauseUntil;
    public List<float> Timers = new();
    public bool Disabled;

    public bool IsDefault => HullMissing <= 0f && ShieldMissing <= 0f && !Disabled && Timers.TrueForAll(t => t <= 0f);
}

/// <summary>
/// Phase 28: eine Rakete im Flug (KAMPF.md Stufe 28: Flugzeit, Ausweichen, Abwehr). Sie fliegt <see cref="Total"/> Einheiten mit <see cref="Speed"/>;
/// bei 0 Rest (<see cref="Left"/>) prüft die Punktabwehr des Ziels, dann würfelt die gemeinsame Trefferregel mit dem Tempo des Ziels in dem Moment.
/// Ziel ist ein Schiff (<see cref="Target"/>) oder eine Station (<see cref="Station"/>).
/// </summary>
public sealed class MissileFlight
{
    public ShipId Shooter;
    public ActorId Owner;
    public int Side;
    public string Module = "";
    public ShipId Target;
    public StationId Station;
    public float Left;
    public float Total;
    public float Speed;
    /// <summary>Startpunkt im räumlichen Gefecht (nur Darstellung und Hash im Raum).</summary>
    public Vec2 From;
}

/// <summary>
/// Phase 28 (POLITIK.md „Krieg: jeder darf, es kostet“): Krieg zwischen zwei Fraktionen. <see cref="A"/> hat erklärt. Kriegsmüdigkeit wächst mit der Dauer
/// und den Verlusten (<see cref="LossA"/>/<see cref="LossB"/>, Materialwert); bei 1 oder leerer Kasse endet der Krieg.
/// </summary>
public sealed class War
{
    public FactionId A;
    public FactionId B;
    public long Since;
    public float Weariness;
    public float LossA;
    public float LossB;
    public int Sieges;

    public bool Involves(FactionId f) => A == f || B == f;
    public FactionId Other(FactionId f) => A == f ? B : A;
}
