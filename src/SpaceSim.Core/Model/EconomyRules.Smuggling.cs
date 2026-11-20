namespace SpaceSim.Core.Model;

/// <summary>
/// Ausbau Punkte 5, 10 und 14 — Schmuggler-KI, Schmuggel- und Forschungsaufträge, Fahrgäste (docs/SCHWARZMARKT.md „Schmuggler“, docs/FLOTTEN.md „Fahrgäste“).
/// Alle Schalter im Code aus (Stand vorher), in der Standardwelt an. Gespeichert wie alle Regeln (additiv, Spielstand bleibt v17).
/// </summary>
public sealed partial class EconomyRules
{
    // ───────── Punkt 5: Schmuggler-KI ─────────

    /// <summary>Gesetzlose KI-Firmen kaufen Schmugglerschiffe (Tarnfeld + Sprungantrieb) und fahren Hehlerware und Embargo-Ware in gesperrte Sektoren.</summary>
    public bool Smugglers;
    /// <summary>Modell der Schmugglerschiffe (muss Tarnfeld, Sprungantrieb und Laderaum tragen).</summary>
    public string SmugglerModel = "shadow";
    /// <summary>Höchstens so viele Schmuggler je Firma.</summary>
    public int SmugglersPerFirm = 3;
    /// <summary>Wartezeit zwischen zwei Schmugglerkäufen einer Firma (Ticks).</summary>
    public int SmugglerBuyCooldownTicks = 20000;
    /// <summary>Freies Geld der Firma (Kasse + Schiffskassen über dem Betriebsgeld), das nach Kaufpreis und Startkapital übrig bleiben muss.</summary>
    public float SmugglerBuyReserve = 3000f;
    /// <summary>Eine Firma kauft einen Schmuggler erst, wenn in den Verstecken Hehlerware mindestens dieses Basiswerts liegt (sonst gibt es nichts zu fahren).</summary>
    public float SmugglerMinFenceValue = 1500f;
    /// <summary>Startkapital eines neuen Schmugglers (aus dem freien Geld der Firma; gemessen: mit 800 Cr ging jeder zweite nach der ersten Beschlagnahme pleite).</summary>
    public float SmugglerStartCredits = 3000f;
    /// <summary>Auftragsbudget gesetzloser Auftraggeber (Piratengruppe, Firma) als Anteil ihrer Kasse — sie zahlen für Schmuggel mehr als Staaten.</summary>
    public float MissionOutlawBudgetShare = 0.5f;
    /// <summary>Hehlerware kaufen Schmuggler beim Hehler zu diesem Anteil des Basispreises (Netzrabatt; der Spieler zahlt den Verkaufsfaktor).</summary>
    public float SmugglerFenceBuyShare = 0.6f;
    /// <summary>Mindestgewinn einer Fahrt als Anteil der Einkaufskosten.</summary>
    public float SmugglerMinMargin = 0.25f;
    /// <summary>Aufschlag in der Bewertung für Abnehmer in Sektoren, die das Schiff auf dem Torweg nicht erreicht (Sperre, Embargo): dort fehlt Ware.</summary>
    public float SmugglerLockBonus = 0.5f;
    /// <summary>Höchstens so viele Sprünge zum Hehler bzw. zur Einkaufsstation.</summary>
    public int SmugglerMaxJumps = 4;
    /// <summary>Die Polizei durchsucht Schiffe in dieser Entfernung (Einheiten).</summary>
    public float SmugglerSearchRange = 120f;
    /// <summary>Chance je Prüfung (alle <see cref="Simulation.SmugglerSystem.Interval"/> Ticks), dass eine Streife in Reichweite das ungetarnte Schiff durchsucht.</summary>
    public float SmugglerSearchChance = 0.06f;
    /// <summary>Strafe beim Erwischen: Anteil des Basiswerts der beschlagnahmten Fracht (aus der Schiffskasse, an die Kasse der Fraktion).</summary>
    public float SmugglerFineShare = 0.5f;

    // ───────── Punkt 10: Schmuggel- und Forschungsaufträge ─────────

    /// <summary>Auftragsart „Schmuggel“ (Hehler, gesetzlose Firmen).</summary>
    public bool MissionSmuggle;
    /// <summary>Chance je Hehler-/Firmenstation und Angebotstakt.</summary>
    public float MissionSmuggleChance = 0.8f;
    /// <summary>Versiegelte Schmuggelware je Auftrag (Volumen).</summary>
    public float MissionSmuggleUnits = 15f;
    /// <summary>Grundlohn des Schmuggelauftrags.</summary>
    public float MissionSmuggleBaseReward = 600f;
    /// <summary>Lohn je geschätzter Flugminute.</summary>
    public float MissionSmuggleRewardPerMinute = 80f;
    /// <summary>Zuschlag je Sperrstufe des Zielsektors.</summary>
    public float MissionSmuggleRewardPerLevel = 250f;
    /// <summary>Strafe beim Erwischen (zusätzlich zur Fahndungsstrafe).</summary>
    public float MissionSmuggleFine = 800f;
    /// <summary>Auftragsart „Forschung“ (Forschungsstationen: Messpunkte im Nebel, Wrackprobe, seltene Ware).</summary>
    public bool MissionResearch;
    /// <summary>Chance je Forschungsstation und Angebotstakt.</summary>
    public float MissionResearchChance = 0.9f;
    /// <summary>Grundlohn des Forschungsauftrags.</summary>
    public float MissionResearchBaseReward = 250f;
    /// <summary>Lohn je geschätzter Flugminute (Messpunkte).</summary>
    public float MissionResearchRewardPerMinute = 30f;
    /// <summary>Forschungspunkte für das laufende Projekt der Auftraggeber-Fraktion bei Erfüllung.</summary>
    public float MissionResearchPoints = 300f;
    /// <summary>Seltene Ware: Menge je Auftrag.</summary>
    public float MissionResearchUnits = 12f;
    /// <summary>Messpunkt gilt als angeflogen in dieser Entfernung.</summary>
    public float MissionResearchPointRadius = 60f;

    // ───────── Punkt 14: Fahrgäste ─────────

    /// <summary>Auftragsart „Fahrgast“ an Taxi-/Hangarstationen.</summary>
    public bool MissionPassengers;
    /// <summary>Chance je Station und Angebotstakt.</summary>
    public float MissionPassengerChance = 0.7f;
    /// <summary>Grundlohn je Fahrgast.</summary>
    public float PassengerBaseFare = 60f;
    /// <summary>Lohn je Fahrgast und geschätzter Flugminute.</summary>
    public float PassengerFarePerMinute = 25f;
    /// <summary>Eilfahrt: Frist = Flugzeit × dieser Faktor (statt mindestens 2×), Lohn × <see cref="PassengerRushFactor"/>.</summary>
    public float PassengerRushDeadlineFactor = 1.5f;
    public float PassengerRushFactor = 1.5f;
    /// <summary>VIP: Lohn × Faktor, Ansehen +<see cref="PassengerVipStanding"/> bei Erfüllung.</summary>
    public float PassengerVipFactor = 2f;
    public float PassengerVipStanding = 0.02f;
    /// <summary>Flüchtling/Gesuchter: Lohn × Faktor; erwischt bei Kontrolle = Verbrechen „smuggling“.</summary>
    public float PassengerRefugeeFactor = 2.5f;
    /// <summary>Diplomat: Lohn × Faktor, Ansehen bei Auftraggeber und Zielfraktion +<see cref="PassengerDiplomatStanding"/>.</summary>
    public float PassengerDiplomatFactor = 1.5f;
    public float PassengerDiplomatStanding = 0.04f;
    /// <summary>Trinkgeld als Anteil des Lohns (in der Reservierung); jeder Hüllentreffer kostet <see cref="PassengerTipLossPerHit"/> davon.</summary>
    public float PassengerTipShare = 0.25f;
    public float PassengerTipLossPerHit = 0.25f;
    /// <summary>Zusätzlicher Ansehensverlust, wenn das Schiff mit Fahrgästen verloren geht.</summary>
    public float PassengerLossStanding = 0.05f;
    /// <summary>Flottenbefehl „Taxi fahren“ (eigene Schiffe mit Passagierkabine fahren automatisch Fahrgäste).</summary>
    public bool PassengerTaxi;
    /// <summary>Fahrgeld des Flottentaxis je Platz und geschätzter Flugminute (Geld der Fahrgäste, kommt neu in die Welt) und Grundpreis je Platz.</summary>
    public float TaxiFarePerSeatMinute = 12f;
    public float TaxiFareBase = 20f;
}
