namespace SpaceSim.Core.Model;

/// <summary>
/// Ausbau Punkt 9 — Firmensitz und Büro (docs/POLITIK.md „Firmensitz und Büro“). Alle Schalter im Code aus (Stand vorher), in der Standardwelt an.
/// Aus — oder an, solange niemand einen Sitz/ein Büro gründet — rechnet die Welt wie vorher (kein Zufallswurf, keine neue Station).
/// </summary>
public sealed partial class EconomyRules
{
    /// <summary>KI-Firmen (aufgestiegene unabhängige Akteure) gründen ab <see cref="FirmSeatMinClass"/> einen Firmensitz; der Sitz gibt ihnen eine eigene Kasse.</summary>
    public bool FirmSeats;
    /// <summary>Stationstyp des Firmensitzes (KI und Spieler).</summary>
    public string FirmSeatType = "company_seat";
    /// <summary>Mindest-Größenklasse für einen KI-Firmensitz (Index in <see cref="ActorSizeThresholds"/>: 2 = Firma).</summary>
    public int FirmSeatMinClass = 2;
    /// <summary>Eine Firma gründet erst, wenn ihr verfügbares Geld (Kasse + Überschüsse von Schiffen und Stationen) mindestens Kosten × Faktor beträgt.</summary>
    public float FirmSeatMoneyFactor = 1.5f;
    /// <summary>Wartezeit zwischen zwei Gründungsversuchen (Sitz oder Büro) eines Akteurs.</summary>
    public int FirmFoundCooldownTicks = 6000;
    /// <summary>Höchstens so viele Firmensitze je Sektor.</summary>
    public int FirmSeatsPerSector = 2;
    /// <summary>Anteil der Gründungskosten, den eine KI-Firma zahlt (die Firmen sind klein; der Rest entfällt — kein Bau über Baustellen).</summary>
    public float FirmSeatCostShare = 0.4f;
    /// <summary>Firmenschiffe behalten mindestens so viel Betriebskasse (Abschöpfung und verfügbares Geld zählen nur darüber).</summary>
    public float FirmShipKeep = 1000f;
    /// <summary>Anteil des Gewinns der Firmenschiffe (Rentabilitätsmessung, nur positiv), den der Sitz in die Firmenkasse abschöpft.</summary>
    public float FirmSkimShare = 0.3f;
    /// <summary>Die Firmenkasse schöpft nur ab, solange sie unter diesem Betrag liegt (0 = ohne Grenze); darüber bleibt der Gewinn auf den Schiffen.</summary>
    public float FirmTreasuryTarget = 15000f;
    /// <summary>Abschöpfung je Tick als Anteil des Kassenüberschusses von Firmenstationen über dem Betriebskapital.</summary>
    public float FirmSkimPerTick = 0.002f;
    /// <summary>Anteil des Typ-Unterhalts, den ein KI-Firmensitz trägt (Regierungsunterhalt aus der Firmenkasse).</summary>
    public float FirmSeatUpkeepShare = 0.1f;
    /// <summary>Reicht die Firmenkasse so lange nicht für den Unterhalt, wird der Sitz aufgegeben (Grund b, „Sitz unbezahlt“).</summary>
    public int FirmSeatArrearsTicks = 4800;

    /// <summary>Die Spielerfraktion darf einen Firmensitz gründen (eigener oder herrenloser Sektor): Befehlsstufe +<see cref="FirmSeatCommandBonus"/>, Fehden freigeschaltet.</summary>
    public bool PlayerFirmSeat;
    /// <summary>Mindest-Größenklasse des Spielers für den Firmensitz (2 = Firma).</summary>
    public int PlayerFirmSeatMinClass = 2;
    /// <summary>Um so viele Größenklassen hebt ein eigener Firmensitz die Befehlsstufe (Befehlskapazität).</summary>
    public int FirmSeatCommandBonus = 1;

    /// <summary>KI-Fraktionen (und Firmen mit Sitz) gründen Büros in fremden Sektoren, in denen sie viel handeln, aber keine Station haben.</summary>
    public bool Offices;
    /// <summary>Stationstyp des Büros.</summary>
    public string OfficeType = "office";
    /// <summary>Höchstens so viele Büros je Fraktion (Firmen: 1).</summary>
    public int OfficesPerFaction = 3;
    /// <summary>Mindestzahl eigener Handels-/Schürfaufträge mit Ziel im Sektor (Fraktion samt Mitgliedsfirmen), ab der eine Fraktion dort ein Büro will.</summary>
    public int OfficeMinTrade = 3;
    /// <summary>Wartezeit zwischen zwei Büros einer Fraktion.</summary>
    public int OfficeCooldownTicks = 12000;
    /// <summary>Gastgeber und Gründer müssen einander mindestens so wohlgesinnt sein (beide Richtungen; Zustimmungsregel, auch für den Spieler).</summary>
    public float OfficeMinRelation = 0f;
    /// <summary>Die Spielerfraktion darf Büros in fremden, nicht feindlichen Sektoren gründen.</summary>
    public bool PlayerOffices;
    /// <summary>Mindest-Größenklasse des Spielers für ein Büro (1 = Kleinfirma).</summary>
    public int PlayerOfficeMinClass = 1;
}
