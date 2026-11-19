namespace SpaceSim.Core.Model;

/// <summary>
/// Ausbau Punkt 2 — KI-Kriegsschiffe (docs/KAMPF.md „KI-Kriegsschiffe: Seeschlacht, Abwehr, Geleit“): Seeschlacht, gezielte Abwehr, Geleitschutz im Krieg.
/// Code-Vorgabe = alte Regel (alles aus); die Standardwelt schaltet ein. Ohne Krieg wirkt keine dieser Regeln (die Standardwelt bleibt bitgleich).
/// Gespeichert wie alle Regeln (additiv, Spielstand bleibt v17). Dazu Nachtrag 1c: Frist des Sammelpunkts.
/// </summary>
public sealed partial class EconomyRules
{
    /// <summary>
    /// Seeschlacht: treffen ein Kriegsschiff und ein Kriegsschiff oder Polizeischiff zweier verfeindeter KI-Fraktionen (Krieg ohne Waffenstillstand) im selben
    /// Sektor auf höchstens <see cref="NavalBattleRange"/> zusammen, beginnt ein Gefecht mit dem Anlass <see cref="EncounterCause.Naval"/>.
    /// </summary>
    public bool NavalBattles;
    /// <summary>Abstand (Einheiten), auf den sich zwei Gegner sehen und ins Gefecht gehen; Verstärkung im Sektor tritt aus diesem Abstand zu einem Teilnehmer bei.</summary>
    public float NavalBattleRange = 400f;
    /// <summary>Höchstdauer einer Seeschlacht (Ticks) — länger als <see cref="CombatMaxTicks"/>, weil Kriegsschiffe lange brauchen; 0 = <see cref="CombatMaxTicks"/>.</summary>
    public int NavalBattleMaxTicks = 1200;
    /// <summary>Ein in einer Seeschlacht zerstörtes Schiff zählt mit seinem Materialwert × diesem Anteil als Kriegsverlust (Müdigkeit, Tribut). 0 = nicht.</summary>
    public float NavalLossShare = 1f;

    /// <summary>
    /// Gezielte Abwehr: eine Fraktion schickt freie Kriegsschiffe gegen feindliche Verbände, die gerade ihre Stationen oder Tore angreifen (Angriffsbefehl
    /// eines Kriegsgegners oder laufende Belagerung) — die nächsten nach Sprüngen (höchstens <see cref="WarDefenseJumps"/>).
    /// </summary>
    public bool WarDefense;
    /// <summary>Reichweite der Abwehr in Torsprüngen.</summary>
    public int WarDefenseJumps = 3;
    /// <summary>Höchstens so viele Kriegsschiffe je Fraktion und angegriffenem Ziel.</summary>
    public int WarDefenseMaxResponders = 3;
    /// <summary>Rücklage: so viele Kriegsschiffe einer Fraktion bleiben bei ihrem Auftrag (Angriff, Geleit), wenn sie mindestens zwei hat (ein einzelnes verteidigt).</summary>
    public int WarDefenseReserve = 1;

    /// <summary>
    /// Geleitschutz im Krieg: je Fraktion im Krieg begleiten bis zu <see cref="WarEscortsPerFaction"/> Kriegsschiffe den wertvollsten beladenen Händler, dessen
    /// Route durch feindliche oder an feindliches Gebiet grenzende Sektoren führt. Ende bei Ankunft, Frieden oder nach <see cref="WarEscortMaxTicks"/>.
    /// </summary>
    public bool WarEscorts;
    public int WarEscortsPerFaction = 1;
    /// <summary>Mindestwert der Fracht (Basispreise) eines Geleitschiffs.</summary>
    public float WarEscortMinValue = 1500f;
    /// <summary>Höchstens so viele Sprünge vom Kriegsschiff zum Händler.</summary>
    public int WarEscortJumps = 2;
    /// <summary>Höchstdauer eines Geleits (Ticks).</summary>
    public int WarEscortMaxTicks = 4800;

    /// <summary>Nachtrag 1c: Frist eines Sammelpunkts (Ticks; 0 = keine). Danach bilden die angekommenen Schiffe den Verband, die übrigen verlieren den Befehl.</summary>
    public int RallyDeadlineTicks = 2400;
}
