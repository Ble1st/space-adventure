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
    /// <summary>Wirtschaftsstationen halten stand: Alarm läuft bis zu diesem Takt (0 = keiner). Gespeichert und gehasht nur, wenn gesetzt.</summary>
    public long AlarmUntil;
    /// <summary>Notschild aktiv bis zu diesem Takt (0 = keiner); danach Abklingzeit bis <see cref="EmergencyCooldownUntil"/>. Gespeichert/gehasht nur, wenn gesetzt.</summary>
    public long EmergencyUntil, EmergencyCooldownUntil;

    public bool IsDefault => HullMissing <= 0f && ShieldMissing <= 0f && !Disabled && Timers.TrueForAll(t => t <= 0f)
        && AlarmUntil == 0 && EmergencyUntil == 0 && EmergencyCooldownUntil == 0;

    /// <summary>Unbeschädigt und alle Fristen (Alarm, Notschild, Abklingzeit) bis <paramref name="tick"/> abgelaufen — der Zustand darf entfallen.</summary>
    public bool IsDefaultAt(long tick) => HullMissing <= 0f && ShieldMissing <= 0f && !Disabled && Timers.TrueForAll(t => t <= 0f)
        && AlarmUntil <= tick && EmergencyUntil <= tick && EmergencyCooldownUntil <= tick;
}

/// <summary>
/// Phase 28: eine Rakete im Flug (KAMPF.md Stufe 28: Flugzeit, Ausweichen, Abwehr). Sie fliegt <see cref="Total"/> Einheiten mit <see cref="Speed"/>;
/// bei 0 Rest (<see cref="Left"/>) prüft die Punktabwehr des Ziels, dann würfelt die gemeinsame Trefferregel mit dem Tempo des Ziels in dem Moment.
/// Ziel ist ein Schiff (<see cref="Target"/>) oder eine Station (<see cref="Station"/>).
/// </summary>
public sealed class MissileFlight
{
    public ShipId Shooter;
    /// <summary>Runde 6: abgefeuert von dieser Station (0 = von einem Schiff). Nur für die Meldung „Stationsrakete (…)“; gespeichert, nicht im Hash (reine Anzeige).</summary>
    public StationId FromStation;
    public ActorId Owner;
    public int Side;
    public string Module = "";
    public ShipId Target;
    public StationId Station;
    /// <summary>Phase 31: Ziel ist ein Tor (das Paar mit diesem Tor; 0 = keins). Gespeichert und gehasht nur, wenn gesetzt.</summary>
    public GateId Gate;
    /// <summary>Phase 31: Belagerungsziel der Rakete (Station oder Tor; keins = Schiff).</summary>
    public SiegeRef Siege => Station.Value != 0 ? SiegeRef.Of(Station) : Gate.Value != 0 ? SiegeRef.Of(Gate) : default;
    public float Left;
    public float Total;
    public float Speed;
    /// <summary>Startpunkt im räumlichen Gefecht (nur Darstellung und Hash im Raum).</summary>
    public Vec2 From;
    /// <summary>Runde 11, nur Darstellung (nicht gespeichert, nicht gehasht): Werferslot bzw. Geschützindex, aus dem die Rakete startete (−1 = unbekannt).</summary>
    public int Slot = -1;
}

/// <summary>
/// Phase 28 (POLITIK.md „Krieg: jeder darf, es kostet“): Krieg zwischen zwei Fraktionen. <see cref="A"/> hat erklärt. Kriegsmüdigkeit wächst mit der Dauer
/// und den Verlusten (<see cref="LossA"/>/<see cref="LossB"/>, Materialwert); bei 1 oder leerer Kasse endet der Krieg.
/// </summary>
public sealed class War
{
    /// <summary>Phase 31: Torblockaden dieses Kriegs (höchstens <see cref="EconomyRules.GateBlockadesPerWar"/>); gespeichert, gehasht nur wenn &gt; 0.</summary>
    public int Blockades;
    public FactionId A;
    public FactionId B;
    public long Since;
    public float Weariness;
    public float LossA;
    public float LossB;
    public int Sieges;
    /// <summary>Runde 40: Waffenstillstand läuft bis zu diesem Tick (0 = keiner); gespeichert, gehasht nur wenn gesetzt. Währenddessen keine Kriegsangriffe (<see cref="Simulation.Wars.Hostile(Universe, FactionId, FactionId)"/>), Handel/Sperren bleiben.</summary>
    public long CeasefireUntil;
    /// <summary>Runde 40: Tick des letzten Angebots (Waffenstillstand/Frieden, egal welche Seite; 0 = keines); gespeichert, gehasht nur wenn gesetzt. Sperrt <see cref="EconomyRules.OfferCooldownTicks"/>.</summary>
    public long LastOfferTick;
    /// <summary>Runde 40: bereits gezahlter Tribut (Cr) auf diesen Krieg; gespeichert, gehasht nur wenn &gt; 0; mindert den geforderten Tribut.</summary>
    public float TributePaid;
    /// <summary>Runde 45: Beitrittskrieg eines Verbündeten (Beistandspakt, Tiefe 1 — ruft niemanden weiter); gespeichert, gehasht nur wenn gesetzt.</summary>
    public bool Join;
    /// <summary>
    /// Ausgleich (Regel <see cref="EconomyRules.TributeDemandNeedsLosses"/>): im Gefecht zerstörte Schiffe von A bzw. B — gezählt nur in Kriegen mit der
    /// Spielerfraktion (KI-Kriege unverändert). Gespeichert, gehasht nur wenn &gt; 0.
    /// </summary>
    public int ShipsLostA, ShipsLostB;

    public bool Involves(FactionId f) => A == f || B == f;
    public FactionId Other(FactionId f) => A == f ? B : A;
}

/// <summary>
/// Runde 45: Bündnis zwischen zwei Fraktionen (auch der Spielerfraktion; kanonisch <see cref="A"/> &lt; <see cref="B"/>). Gespeichert, gehasht nur wenn
/// Bündnisse bestehen. Regeln in <see cref="Simulation.Alliances"/>: defensiver Beistandspakt, Sperren wie Verbündete, Auflösung bei Beziehungsverlust oder Verrat.
/// </summary>
public sealed class Alliance
{
    public FactionId A;
    public FactionId B;
    public long Since;
    public bool Involves(FactionId f) => A == f || B == f;
    public FactionId Other(FactionId f) => A == f ? B : A;
}

/// <summary>Phase 31: Art eines Belagerungsziels.</summary>
public enum SiegeKind { None, Station, Gate }

/// <summary>
/// Phase 31: Verweis auf ein belagerbares Ziel (Station oder Tor). default = keins. Die Laufzeitlogik löst ihn über <c>Combat.SiegeTargets</c> zum Adapter
/// <c>ISiegeTarget</c> auf; gespeichert wird weiter die alte Form (Stations-Id bzw. Tor-Id), damit Spielstände und Hashes stabil bleiben.
/// </summary>
public readonly record struct SiegeRef(SiegeKind Kind, int Id)
{
    public bool IsNone => Kind == SiegeKind.None || Id == 0;
    public static SiegeRef Of(StationId id) => id.Value == 0 ? default : new(SiegeKind.Station, id.Value);
    public static SiegeRef Of(GateId id) => id.Value == 0 ? default : new(SiegeKind.Gate, id.Value);
    public StationId Station => Kind == SiegeKind.Station ? new StationId(Id) : default;
    public GateId Gate => Kind == SiegeKind.Gate ? new GateId(Id) : default;
}

/// <summary>
/// Phase 31 (docs/TORE.md): Torklasse (JSON <c>gateTypes</c>): Verteidigungswerte wie bei Stationen (<see cref="StationDefenseConfig"/>; Geschütze als Datenfeld,
/// in der Standardwelt leer), Reparaturtempo und die Frist bis zum Wiederanlauf ohne Kasse.
/// </summary>
public sealed class GateType
{
    public string Key = "";
    public string Name = "";
    public StationDefenseConfig Defense = new();
    /// <summary>Hüllenpunkte je Spieltag, die der Besitzer repariert (0 = Regel <see cref="EconomyRules.GateRepairPerDay"/>).</summary>
    public float RepairPerDay;
    /// <summary>Frist bis zum Wiederanlauf ohne Kasse (0 = Regeln <see cref="EconomyRules.GateSplitOutageTicks"/>/<see cref="EconomyRules.GateMaxOutageTicks"/>).</summary>
    public long MaxOutageTicks;
}

/// <summary>
/// Phase 31: Kampfzustand eines Tor-Paars (am Tor mit der kleineren Id; beide Hälften teilen ihn) als Fehlbeträge wie <see cref="StationCondition"/>; dazu
/// <see cref="Disabled"/> (außer Betrieb — das Paar ist <see cref="GateState.Closed"/>) und <see cref="DownSince"/> (Tick des Ausfalls). Null = unbeschädigt.
/// </summary>
public sealed class GateCondition
{
    public float HullMissing;
    public float ShieldMissing;
    public long ShieldPauseUntil;
    public List<float> Timers = new();
    public bool Disabled;
    public long DownSince;
    /// <summary>Wer das Tor zuletzt getroffen hat (Akteur) — Reparatur nur durch Besitzer, die nicht selbst angreifen.</summary>
    public ActorId LastAttacker;
    /// <summary>
    /// Runde 14b (Prüfung: Sackgassentor dauerhaft unten haltbar): nach dem Wiederanlauf bis zu diesem Tick unverwundbar und nicht angreifbar (Schonfrist,
    /// <see cref="EconomyRules.GateRestoreGraceTicks"/>/<see cref="EconomyRules.GateSplitRestoreGraceTicks"/>); 0 = keine.
    /// </summary>
    public long GraceUntil;
    /// <summary>Runde 14b: Zustand vor dem Ausfall (ein von Hand geschlossenes Tor bleibt nach dem Wiederanlauf geschlossen); null = offen.</summary>
    public GateState? PrevState;

    public bool IsDefault => HullMissing <= 0f && ShieldMissing <= 0f && !Disabled && GraceUntil == 0 && PrevState is null && Timers.TrueForAll(t => t <= 0f);
}
