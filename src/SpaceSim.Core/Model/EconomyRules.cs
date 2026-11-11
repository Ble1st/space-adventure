namespace SpaceSim.Core.Model;

/// <summary>Tuningwerte der Wirtschaft (Sinks, Faucets, Wachstum). Datengetrieben und Teil des Spielstands.</summary>
public sealed class EconomyRules
{
    // Sinks
    /// <summary>Frühere Credit-Pauschale je geflogener Einheit. Seit Phase 4b verbrauchen KI-Schiffe echten Treibstoff (siehe Simulation.Refuel); der Wert wird nicht mehr abgebucht, bleibt aber Datenbestand alter Spielstände.</summary>
    public float FuelCostPerUnit = 0.02f;
    /// <summary>Hüllenplatten je repariertem Hüllenpunkt (Dienst „repair“, Phase 4c); der Arbeitsaufschlag wird extra in Credits berechnet. Fehlt das Feld in alten Spielständen, gilt der Standard 0,05 (additiv, kein Versionssprung).</summary>
    public float RepairPlatesPerHull = 0.05f;
    public float ShipUpkeepPerTick = 0.01f;      // Wartung je Schiff
    /// <summary>Anteil des Vermögens, der pro Tick verschwindet (Gebühren, Versicherung). Stabilisiert den Geldumlauf.</summary>
    public float ShipWealthTax = 0.0003f;
    /// <summary>Betriebsreserve, die von der Vermögensabgabe ausgenommen ist (damit untätige Schiffe nicht verarmen und aufgegeben werden).</summary>
    public float WealthTaxFree = 500f;
    public float StationWealthTax = 0.0003f;
    /// <summary>Mindestreserve, die ein Investor nach einem Stationsbau behalten muss.</summary>
    public float InvestorReserve = 2000f;
    public float NewShipCost = 1500f;            // vernichtet beim Kauf eines Schiffs
    public float StationBuildCostFactor = 1f;    // Multiplikator auf StationType.BuildCost

    // Flottenwachstum
    public float FleetExpandCredits = 4000f;     // Schwelle, ab der ein Schiff ein weiteres kauft
    public float NewShipStartCredits = 800f;
    public int MaxTraders = 10;
    public int MaxMiners = 4;
    public int ShipBankruptTicks = 600;          // untätig und pleite → Schiff wird aufgegeben

    // Stationen
    public float BankruptcyLimit = -3000f;       // darunter wird eine Station aufgegeben
    public int BankruptcyTicks = 400;
    /// <summary>Anteil des Produktwerts, den eine Fabrik höchstens für ihre Zutaten bietet (Rest ist ihre Marge). Verhindert, dass Preisspitzen sie ruinieren.</summary>
    public float InputWillingness = 0.85f;
    public float BuildPriceRatio = 1.6f;         // Ware gilt als knapp, wenn Durchschnittspreis > Basispreis * Faktor
    /// <summary>Ware gilt als im Überangebot, wenn Wunschbestand / Bestand (über alle Stationen) darunter liegt: dann entsteht ein Verbraucher.</summary>
    public float GlutPriceRatio = 0.4f;
    public int BuildCooldown = 1500;
    /// <summary>Produktion pausiert, wenn ein Ausgabegut mehr als Wunschbestand × diesen Faktor lagert (verhindert endlosen Lagerstau).</summary>
    public float OverstockFactor = 3f;
    /// <summary>
    /// Phase 15: Pausengrenze eines Rezepts, für dessen Produkte keine andere Station eine offene Fehlmenge hat (Anteil am Wunschbestand,
    /// <see cref="Simulation.ProductionDemand"/>); gefragte Produkte laufen bis <see cref="OverstockFactor"/>. Wert ≥ OverstockFactor = alte Regel.
    /// </summary>
    public float NoDemandStockFactor = 1f;
    /// <summary>
    /// Phase 15: Annahmegrenze von Regal-Einträgen (Lager ohne Rezept, keine Exportware) als Vielfaches des Wunschbestands; ≥ OverstockFactor = alte Regel.
    /// Ein volles Regal steht so beim Basispreis statt beim Drittel.
    /// </summary>
    public float ShelfOverstockFactor = 1f;
    /// <summary>
    /// Ware (Schlüssel), aus der neue Schiffe gebaut werden, wenn die Schiffsklasse keinen eigenen Bausatz nennt (<see cref="ShipClass.Kit"/>):
    /// Eine Werft muss einen Bausatz liefern. Seit Phase 5a heißt der frühere „ship_kit“ „ship_kit_light“ (Migration v5→v6).
    /// </summary>
    public string ShipKitGoodKey = "ship_kit_light";
    /// <summary>Verarbeitungsaufschlag der Modul-Waren (Phase 5a): Basispreis = Summe der Zutatenpreise (Modul-cost) × diesen Faktor.</summary>
    public float ModuleMarkup = 1.3f;
    /// <summary>
    /// Phase 5c: KI-Neubauten nutzen dieselbe Kaufregel wie der Spieler — Bausatz plus je eingebautem Modul eine Modul-Ware aus dem
    /// Werftlager, Preis = Marktpreis × (1 + Montagegebühr). false = konservativ wie vor 5c (nur der Bausatz, Pauschale <see cref="NewShipCost"/>).
    /// </summary>
    public bool FleetBuysModules = true;
    public int MaxStations = 30;
    public int MaxStationsPerSector = 8;

    // Phase 6: Multirole (Aufgabenwahl und Flottengrenzen nach Kapazität). Fehlen die Felder in alten Spielständen, gelten die Standards.
    /// <summary>Frachtraum (Volumen), der als eine Handelseinheit zählt: <see cref="MaxTraders"/> begrenzt seit Phase 6 die Summe Frachtraum ÷ diesen Wert (Einheitsfrachter 100 = 1).</summary>
    public float TradeCapacityUnit = 100f;
    /// <summary>Wie weit ein Neukauf die Kapazitätsgrenze einer Aufgabe überschreiten darf (in Einheiten), damit größere Modelle nicht nie in Frage kommen.</summary>
    public float FleetCapacityOvershoot = 0.5f;
    /// <summary>Phase 6: Die KI-Flotte wählt beim Neukauf das Modell (Aufgabe, Geld, Lager der Werft); false = wie vor Phase 6 nur Kopien des Elternschiffs.</summary>
    public bool FleetChoosesModels = true;
    /// <summary>Gewicht der Zweitaufgaben eines Multirole-Modells bei der Kaufwahl (Wert der Flexibilität): Wert = Wert(Aufgabe) + Gewicht × Σ Wert(andere Aufgaben).</summary>
    public float FleetFlexWeight = 0.75f;
    /// <summary>Mindestdauer einer Aufgabe in Ticks, bevor ein Multirole-Schiff wechseln darf (gegen Flackern).</summary>
    public int JobMinTicks = 600;
    /// <summary>Hysterese: Eine andere Aufgabe muss um diesen Anteil besser sein als die aktuelle, damit ein Wechsel stattfindet.</summary>
    public float JobSwitchMargin = 0.25f;
    /// <summary>Herdenschutz auf Aufgabenebene: Bewertung × 1 / (1 + Faktor × Auslastung der Aufgabe), Auslastung = belegte Kapazität ÷ Grenze.</summary>
    public float JobCrowdPenalty = 0.5f;

    // Phase 7: Güteklassen und Upgrades (SCHIFFSSLOTS.md Abschnitt 8). Fehlen die Felder in alten Spielständen, gelten die Standards.
    /// <summary>Gründerschiffe der Startwelt würfeln ihre Güteklasse (ohne Material); false = alle Standard wie vor Phase 7.</summary>
    public bool FounderQuality = true;
    /// <summary>Chancen der Güteklassen beim Kauf: Standard, Verbessert, Elite, Meisterwerk (Summe 1).</summary>
    public List<float> QualityChances = new() { 0.70f, 0.20f, 0.08f, 0.02f };
    /// <summary>Erfolgschance eines Modul-Upgrades nach Stufe des Moduls (Mk1→Mk2, Mk2→Mk3, Mk3→Mk4); höhere Stufen nehmen den letzten Wert.</summary>
    public List<float> UpgradeChances = new() { 0.9f, 0.6f, 0.3f };
    /// <summary>Mitleidszähler: jeder Fehlschlag am selben Slot erhöht die nächste Chance um diesen Wert.</summary>
    public float UpgradePityStep = 0.1f;
    /// <summary>Erfolgschance eines Slot-Upgrades (Slot eine Größe größer oder Bonusslot); deutlich seltener als Modul-Upgrades.</summary>
    public float SlotUpgradeChance = 0.15f;
    /// <summary>Arbeitsaufschlag des Upgrade-Dienstes: Preis = Material zum Lagerpreis × (1 + Aufschlag); das Geld geht an die Station.</summary>
    public float UpgradeLabor = 0.5f;
    /// <summary>Material je Versuch, einen Slot zu vergrößern (Warenschlüssel → Menge).</summary>
    public Dictionary<string, float> SlotUpgradeCost = new() { ["hull_plates"] = 2f, ["titanium"] = 1f };
    /// <summary>Material je Versuch, einen Bonusslot anzubauen.</summary>
    public Dictionary<string, float> BonusSlotCost = new() { ["hull_plates"] = 3f, ["titanium"] = 2f };
    /// <summary>NPC-Upgrades (<c>UpgradeSystem</c>): nur Schiffe mit mindestens so vielen Credits versuchen eins.</summary>
    public float UpgradeMinCredits = 3000f;
    /// <summary>NPC-Upgrades: höchstens dieser Anteil der Credits für einen Versuch.</summary>
    public float UpgradeBudgetShare = 0.2f;

    // Phase 8: Bau, Notbau, Brecher, Lagerhaus (SCHIFFE.md Abschnitte 10/11, STATIONEN.md). Fehlen die Felder in alten Spielständen, gelten die Standards.
    /// <summary>Stationsbau über Baustellen (Material + Bauschiff); false = alter Direktbau (Credits, Station erscheint sofort). Auch ohne Schalter
    /// fällt der Lebenszyklus auf den Direktbau zurück, wenn es den Baustellen-Typ oder eine Material-Ware in der Welt nicht gibt (sehr alte Spielstände).</summary>
    public bool ConstructionSites = true;
    /// <summary>Stationstyp der Baustellen (Content: Typ ohne Rezept mit Dienst „trade“).</summary>
    public string ConstructionSiteType = "build_site";
    /// <summary>Baumaterial nach Wertanteil (Warenschlüssel → Gewicht): Stückliste = Bausumme × <see cref="ConstructionMaterialShare"/>, verteilt nach
    /// Gewicht, in Einheiten zum Basispreis. Waren, die der Typ selbst herstellt, fallen weg (zyklenfreie Baukosten), ihr Anteil geht an die übrigen.
    /// Ein Stationstyp kann eine eigene Liste haben (<c>stationTypes[].construction</c>).</summary>
    public Dictionary<string, float> ConstructionMaterials = new() { ["steel"] = 0.4f, ["hull_plates"] = 0.35f, ["electronics"] = 0.25f };
    /// <summary>Anteil der Bausumme, der als Material (zum Basispreis) verlangt wird; der Rest ist der Credit-Anteil.</summary>
    public float ConstructionMaterialShare = 0.5f;
    /// <summary>Gebot der Baustelle für Material: Basispreis × Faktor (fester Preis, unabhängig vom Liefergrad).</summary>
    public float ConstructionPriceFactor = 1.25f;
    /// <summary>Das Bauschiff kauft Material für die Baustelle höchstens zum Basispreis × Faktor (so reicht das Treuhandgeld auch bei Knappheit; teurer
    /// heißt: warten, bis Händler liefern oder der Notbau greift — der zahlt denselben Höchstpreis <see cref="EmergencyBuildMarkup"/>).</summary>
    public float ConstructionMaxBuyFactor = 2f;
    /// <summary>Montagedauer in Ticks, sobald alles geliefert ist und ein Bauschiff vor Ort ist.</summary>
    public int ConstructionAssemblyTicks = 200;
    /// <summary>Montagegebühr für das Bauschiff (Anteil der Bausumme, aus dem Treuhandgeld) bei Fertigstellung.</summary>
    public float ConstructionFeeShare = 0.1f;
    /// <summary>Ein Projekt ohne Fortschritt (und ohne Notbau) verfällt nach so vielen Ticks; der Rest des Treuhandgelds geht an den Investor zurück.</summary>
    public int ConstructionExpiryTicks = 8000;
    /// <summary>Höchstzahl gleichzeitig laufender Baustellen (verlassene zählen nicht).</summary>
    public int MaxActiveSites = 2;
    /// <summary>Kapazitätsgrenze der KI-Bauschiffe (Aufgabe „build“, je Schiff 1).</summary>
    public int MaxBuilders = 4;
    /// <summary>Notbau: Frist in Ticks, die eine Baustelle blockiert sein muss (kein Lieferant mit Lager, kein Bauschiff erreichbar, Treuhand reicht nicht).</summary>
    public int EmergencyBuildTicks = 2000;
    /// <summary>Notbau: Faktor auf die Frist für kritische Baustellen (letzte Station einer Kette oder Typ einer kritischen Kette).</summary>
    public float EmergencyCriticalFactor = 0.5f;
    /// <summary>Notbau: Aufschlag auf den Basispreis der ersetzten Materialien (Geldsenke, die Ware entsteht aus dem Nichts).</summary>
    public float EmergencyBuildMarkup = 2f;
    /// <summary>Notbau: Abkühlzeit je Fraktion in Ticks (Bremse: höchstens ein Notbau je Fraktion in dieser Zeit).</summary>
    public int EmergencyBuildCooldown = 3000;
    /// <summary>Kritische Ketten (Warenschlüssel): Baustellen für Typen, die eine davon herstellen, lösen den Notbau früher aus (Stahl, Plattierung, Werft).</summary>
    public List<string> CriticalGoods = new() { "steel", "hull_plates", "ship_kit_light" };
    /// <summary>Asteroidenbrecher: Obergrenze der Asteroiden eines Gürtels als Vielfaches von <c>TargetAsteroids</c> (Spalten nur darunter).</summary>
    public float FragmentLimitFactor = 2f;
    /// <summary>Asteroidenbrecher: kleinster Radius eines Fragments; kleinere Felsen lassen sich nicht weiter spalten.</summary>
    public float FragmentMinRadius = 1.2f;
    /// <summary>Asteroidenbrecher: Anteil des Erzes, der beim Spalten als Staub verloren geht (Spalten erzeugt kein Erz).</summary>
    public float SplitDustLoss = 0.1f;
    /// <summary>NPC-Brecher spalten nur Felsen ab diesem Radius (große Brocken für mehrere Miner).</summary>
    public float NpcSplitMinRadius = 4.5f;
    /// <summary>Lagerhaus (Dienst „storage“): Gebühr je eingelagerter Einheit, an die Station.</summary>
    public float StorageFeePerUnit = 0.5f;
    /// <summary>Lagerhaus: Höchstmenge im persönlichen Lager des Spielers je Station (Einheiten).</summary>
    public float StorageCapacity = 500f;

    // ───────── Phase 9: Akteure, Kasse, Zukauf, Beziehungen ─────────
    /// <summary>Größenzahl (POLITIK.md Abschnitt 6): Gewicht je Schiff im Besitz.</summary>
    public float ActorShipWeight = 1f;
    /// <summary>Größenzahl: Gewicht je Station im Besitz.</summary>
    public float ActorStationWeight = 3f;
    /// <summary>Größenzahl: Gewicht je kontrolliertem Sektor (Sektorbesitz der Fraktion).</summary>
    public float ActorSectorWeight = 10f;
    /// <summary>Größenzahl: Geld (Kasse plus Betriebskassen der eigenen Schiffe und Stationen) ÷ Skala.</summary>
    public float ActorMoneyScale = 50_000f;
    /// <summary>Untergrenzen der Größenklassen Kleinfirma, Firma, Konzern, Staat (darunter: Einzelakteur).</summary>
    public List<float> ActorSizeThresholds = new() { 2f, 6f, 15f, 30f };
    /// <summary>Appetit beim Zukauf je Größenklasse (Einzelakteur … Staat): Kleine kaufen seltener (POLITIK.md „Zukauf durch kleinere Akteure“).</summary>
    public List<float> InvestAppetite = new() { 0.25f, 0.5f, 0.75f, 1f, 1f };
    /// <summary>Anteil der Vermögensabgaben, der in die Kasse der zuständigen Fraktion fließt (Rest verschwindet wie vor Phase 9). 0 = alte Geldsenke.</summary>
    public float TaxToTreasuryShare = 1f;
    /// <summary>Dürfen Kassen von Akteuren (Fraktionen, Einzelakteure) Stationen finanzieren (Erstzugriff der Großen)?</summary>
    public bool TreasuryInvests = true;
    /// <summary>Lazy-Aufstieg: Ein freies Einzelschiff wird zum eigenen Akteur, sobald es investiert (Schiff kauft Schiff, Schiff finanziert Station).</summary>
    public bool PromoteInvestors = true;
    /// <summary>Mittelwerte der gewürfelten Persönlichkeit aufgestiegener Einzelakteure (Aggression, Gier, Kooperation, Gesetzestreue).</summary>
    public List<float> IndependentPersonality = new() { 0.35f, 0.6f, 0.5f, 0.55f };
    /// <summary>Streuung (±) der gewürfelten Persönlichkeitswerte.</summary>
    public float PersonalitySpread = 0.2f;
    /// <summary>Vorgabe-Beziehung zwischen Akteuren ohne gespeicherte Abweichung (neutral).</summary>
    public float RelationDefault = 0f;
    /// <summary>Vorgabe-Beziehung innerhalb derselben Fraktion (Mitglieder untereinander und zur eigenen Fraktion).</summary>
    public float RelationSameFaction = 0.5f;
    /// <summary>Vorgabe-Beziehung zwischen Fraktionen und der Sammelgruppe „Freie“ (freie Händler werden geduldet).</summary>
    public float RelationFree = 0.1f;
    /// <summary>Vorgabe-Beziehung aller zu Gesetzlosen.</summary>
    public float RelationOutlaw = -0.6f;

    // ───────── Phase 10: Gate-Zugang, Sperrstufen, Kontrollpunkte, Sektorkontrolle, Regierungsstationen ─────────
    /// <summary>Darf eine Station überhaupt erobert werden (POLITIK.md/FLOTTEN.md: zunächst abgeschaltet, erst nach Kampf)?</summary>
    public bool AllowStationCapture = false;
    /// <summary>Höchste Sperrstufe an einem Gate der Hauptroute (Gate-Ausnahme von der Sektorsperre; 3 = nur Feinde bleiben draußen).</summary>
    public int MainRouteMaxLevel = 3;
    /// <summary>Zoll je Gate-Passage und Sperrstufe (Stufe 1–3, Nichtmitglieder ohne Bündnis), fließt in die Kasse der Sektorfraktion.</summary>
    public float GateTollPerLevel = 8f;
    /// <summary>Beziehung, ab der ein Schiff als Verbündeter gilt (passiert Stufe 1–3 frei).</summary>
    public float AllyRelation = 0.5f;
    /// <summary>Beziehung, bis zu der ein Schiff als Gegner gilt (Stufe 2: beladen abgewiesen, Stufe 3: abgewiesen).</summary>
    public float HostileRelation = -0.15f;
    /// <summary>Schwellen der Sperrstufen 1–5: die schlechteste Beziehung der Sektorfraktion zu einer anderen Fraktion unter dem Wert i ergibt Stufe i+1.</summary>
    public List<float> LockThresholds = new() { -0.15f, -0.3f, -0.45f, -0.65f, -0.85f };
    /// <summary>Verschiebung der Schwellen nach Aggression: (Aggression − 0,5) × Wert wird addiert (aggressive Fraktionen sperren früher).</summary>
    public float LockAggressionShift = 0.2f;
    /// <summary>Hysterese: Eine Stufe sinkt erst, wenn die Beziehung um diesen Wert über ihrer Schwelle liegt.</summary>
    public float LockHysteresis = 0.1f;
    /// <summary>Mindestdauer einer Sperrstufe in Ticks, bevor sie sich wieder ändern darf.</summary>
    public int LockMinTicks = 2000;
    /// <summary>Ablauf: Eine Sperre fällt nach so vielen Ticks auf 0 (keine dauerhafte Schließung).</summary>
    public int LockMaxTicks = 12000;
    /// <summary>Abkühlzeit nach einem Ablauf, in der keine neue Sperre entsteht.</summary>
    public int LockCooldownTicks = 4000;
    /// <summary>Durchsetzungskosten je Tick und Sperrstufe aus der Kasse der Sektorfraktion (ohne Geld: Sperre auf dem Papier).</summary>
    public float LockUpkeepPerLevel = 0.5f;
    /// <summary>Höchste Sperrstufe je Kontrollstufe (keine, Präsenz, Einfluss, Dominanz, Kontrolle).</summary>
    public List<int> StageMaxLevel = new() { 0, 0, 0, 2, 5 };
    /// <summary>Höchste Sperrstufe in einem umkämpften Sektor.</summary>
    public int ContestedMaxLevel = 1;
    /// <summary>Anteil, ab dem zwei Fraktionen einen Sektor umkämpfen.</summary>
    public float ContestedShare = 0.3f;
    /// <summary>Anteil für Dominanz (zusätzlich Sektor-HQ oder Wache).</summary>
    public float DominanceShare = 0.5f;
    /// <summary>Besitzerwechsel nur, wenn der neue Spitzenreiter diesen Anteil vor dem bisherigen Besitzer liegt (Hysterese).</summary>
    public float OwnerSwitchMargin = 0.1f;
    /// <summary>Sensorstärke eines Gate-Kontrollpunkts (Entdeckung: Tarnstärke darüber passiert unbemerkt).</summary>
    public float GateSensorStrength = 1f;
    /// <summary>Schmuggelware (Warenschlüssel), die ab Stufe 1 an Gates abgewiesen wird; leer = keine.</summary>
    public List<string> Contraband = new();
    /// <summary>Beziehungen: Ruhewert ohne Einflüsse (Vorgabe).</summary>
    public float RelationRestBase = 0f;
    /// <summary>Beziehungen: Gewicht der Kooperation (über 0,5 hebt den Ruhewert).</summary>
    public float RelationCooperationWeight = 0.5f;
    /// <summary>Beziehungen: Gewicht der Aggression (über 0,5 senkt den Ruhewert).</summary>
    public float RelationAggressionWeight = 0.5f;
    /// <summary>Beziehungen: Gewicht des gemeinsamen Handels (Anteil der Handelsaufträge zwischen den Stationen beider Fraktionen).</summary>
    public float RelationTradeWeight = 0.4f;
    /// <summary>Beziehungen: Anteil des Abstands zum Ruhewert, der je Schritt (<see cref="RelationStepTicks"/>) ausgeglichen wird.</summary>
    public float RelationDriftRate = 0.05f;
    /// <summary>Beziehungen: Takt der Beziehungsänderung in Ticks.</summary>
    public int RelationStepTicks = 100;
    /// <summary>Beziehungen: Meinungsverlust je Schritt und umkämpftem Sektor (beide Seiten).</summary>
    public float ContestOpinionLoss = 0.02f;
    /// <summary>Beziehungen: Meinungsverlust je Schritt und Sperrstufe, die eine Fraktion gegen die andere durchsetzt (× (0,5 + Handelsanteil)).</summary>
    public float LockOpinionLoss = 0.005f;

    // ───────── Phase 11: Lagekarte, Polizei, Fahndung, Piraten, Begegnungen (FLOTTEN.md Abschnitte 6–7) ─────────
    /// <summary>Schalter für Polizei und Piraten (Systeme laufen, tun aber nichts, wenn aus). Fehlen die Felder in alten Spielständen, gelten die Standards.</summary>
    public bool SecurityActive = true;
    /// <summary>Piraten überfallen auch den Spieler (mit Vorwarnung). Aus = nur NPCs.</summary>
    public bool PlayerRaids = true;
    /// <summary>Lagekarte: Verfall der Bedrohung je Tick (Anteil; 0,0004 ≈ Halbwertszeit 1700 Ticks).</summary>
    public float ThreatDecayPerTick = 0.0004f;
    /// <summary>Lagekarte: Patrouillen senken die Bedrohung je Tick um Sicherheit × Wert.</summary>
    public float PatrolSuppressionPerTick = 0.0001f;
    /// <summary>Lagekarte: Bedrohung je Überfall (erfolgreich) bzw. abgewehrtem Überfall.</summary>
    public float RaidThreat = 1f;
    public float RepelledThreat = 0.5f;
    /// <summary>Lagekarte: Bedrohung je entdecktem Schmuggel (Kontrollpunkt).</summary>
    public float SmugglingThreat = 0.3f;
    /// <summary>Lagekarte: Glättung von Sicherheit und Verkehrswert je Messung (0..1).</summary>
    public float SecuritySmoothing = 0.25f;
    /// <summary>Lagekarte: Stärke eines Polizeischiffs ÷ diesen Wert = sein Beitrag zur Sicherheit.</summary>
    public float PoliceStrengthUnit = 10f;
    /// <summary>Patrouillenabdeckung (Kontrollstufe „Kontrolle“, Phase 10): Sicherheit des Besitzers mindestens so hoch.</summary>
    public float CoverageSecurity = 1f;
    /// <summary>Routenrisiko je Sektor: Anteil des Frachtwerts = Wert × Bedrohung ÷ (1 + Sicherheit), höchstens <see cref="MaxSectorRisk"/>.</summary>
    public float RiskPerThreat = 0.03f;
    public float MaxSectorRisk = 0.25f;

    /// <summary>Polizei: Modell der Polizeischiffe (Content), Kauf über die gemeinsame Kaufregel aus der Fraktionskasse.</summary>
    public string PoliceModel = "warden";
    /// <summary>Polizei: Streifen je Sektor des Territoriums ohne Bedrohung, zusätzlich je Bedrohungspunkt (Summe aufgerundet).</summary>
    public float PolicePerSector = 1f;
    public float PolicePerThreat = 0.5f;
    /// <summary>Polizei: Grenzen je Fraktion.</summary>
    public int PoliceMinPerFaction = 1;
    public int PoliceMaxPerFaction = 4;
    /// <summary>Polizei: Unterhalt je Schiff und Tick aus der Fraktionskasse (Senke).</summary>
    public float PoliceUpkeepPerTick = 0.8f;
    /// <summary>Polizei: Kauf nur, wenn danach diese Reserve in der Kasse bleibt.</summary>
    public float PoliceReserve = 15000f;
    /// <summary>Polizei: Abkühlzeit zwischen Kauf/Abbau je Fraktion; Abbau erst nach so langer Überzahl.</summary>
    public int PoliceChangeCooldown = 2000;
    public int PoliceDecommissionTicks = 6000;
    /// <summary>Polizei: Verweildauer an einem Streifenpunkt (Ticks), dann nächster Punkt.</summary>
    public int PatrolDwellTicks = 150;
    /// <summary>Polizei und Piraten: unter diesem Hüllenanteil zurück zur Heimatbasis (Reparatur).</summary>
    public float RetreatHull = 0.5f;
    /// <summary>Reparatur an der Heimatbasis: Credits je Hüllenpunkt aus der Kasse des Besitzers.</summary>
    public float BaseRepairPerHull = 2f;
    /// <summary>Abfangen: Chance je Tick und Streife im Sektor, ein lauerndes Kaperschiff zu stellen.</summary>
    public float InterceptRatePerTick = 0.0006f;
    /// <summary>Überfall mit Streife im Sektor: Chance je Streife, dass sie eingreift (höchstens <see cref="PoliceResponseMax"/>).</summary>
    public float PoliceResponseChance = 0.4f;
    public float PoliceResponseMax = 0.8f;

    /// <summary>Piraten: Modell der Kaperschiffe, Höchstzahl je gesetzlosem Akteur, Kosten eines Neubaus aus dessen Kasse (Senke), Abkühlzeit.</summary>
    public string PirateModel = "corsair";
    public int MaxRaidersPerActor = 3;
    public float RaiderCost = 2000f;
    public int RaiderRebuildTicks = 3000;
    /// <summary>
    /// Phase 13 (Korrektur an Phase 11): Neuanfang einer ausgelöschten Piratenflotte — ohne Kaperschiff kostet der erste Neubau nur diesen Anteil von
    /// <see cref="RaiderCost"/>, und das Versteck bringt je Tick <see cref="PirateIdleIncomePerTick"/> aus Hehlerei alter Bestände ein. Sonst blieb eine
    /// einmal ausgelöschte Flotte mit leerer Kasse für immer weg (Verlustquote fiel in langen Läufen auf fast 0).
    /// </summary>
    public float RaiderRestartCostFactor = 0.5f;
    public float PirateIdleIncomePerTick = 0.1f;
    /// <summary>Piraten: Unterhalt je Kaperschiff und Tick aus der Kasse des Akteurs (Senke).</summary>
    public float RaiderUpkeepPerTick = 0.05f;
    /// <summary>Piraten: Chance je Tick, dass ein lauerndes Kaperschiff zuschlägt (geteilt durch 1 + Abschreckung × Sicherheit).</summary>
    public float RaidRatePerTick = 0.008f;
    public float RaidSecurityDeterrence = 0.3f;
    /// <summary>Piraten: höchstens so lange lauern (Ticks), dann heim; nach der Heimkehr so lange ruhen.</summary>
    public int PirateLurkTicks = 2500;
    public int PirateRestTicks = 600;
    /// <summary>Piraten: Mindestwert der Beute (Fracht zum Basispreis), sonst kein Überfall.</summary>
    public float MinLootValue = 200f;
    /// <summary>Piraten: Anteil des Basiswerts, den die Hehlerei für die Beute zahlt (in die Kasse des Akteurs).</summary>
    public float FenceShare = 0.6f;
    /// <summary>Piraten: Abneigung gegen Sicherheit bei der Revierwahl (Gewicht = (Verkehr + 500) ÷ (1 + Wert × Sicherheit)).</summary>
    public float PirateSecurityAversion = 1f;
    /// <summary>Spieler: Vorwarnzeit einer Begegnung (Ticks), Gewicht des Spielers bei der Opferwahl, Schutzradius um Stationen.</summary>
    public int PlayerWarningTicks = 60;
    public float PlayerRaidWeight = 0.5f;
    public float StationSafeRadius = 35f;
    /// <summary>Spieler: Ein Überfall lässt mindestens diesen Hüllenanteil übrig (nichts, was den Spieler ohne Vorwarnung ruiniert).</summary>
    public float PlayerMinHull = 0.25f;

    /// <summary>Begegnungen: Exponent des Stärkevergleichs (Siegchance = A^k ÷ (A^k + D^k)).</summary>
    public float EncounterExponent = 1.5f;
    /// <summary>Begegnungen: Beuteanteil der Fracht beim Sieg des Angreifers (gleichverteilt).</summary>
    public float LootMin = 0.3f;
    public float LootMax = 0.6f;
    /// <summary>Begegnungen: Hüllenschaden (Anteil der Maximalhülle) für Verlierer und Sieger (gleichverteilt bis zum Wert).</summary>
    public float LoserDamage = 0.45f;
    public float WinnerDamage = 0.12f;
    /// <summary>Begegnungen: Grundchance, dass der Verlierer zerstört wird (× √Überlegenheit des Siegers, höchstens 0,5).</summary>
    public float DestroyChance = 0.015f;
    /// <summary>Begegnungen: Grundchance, dass ein unterlegenes Kaperschiff gegen die Polizei zerstört wird (statt zu fliehen).</summary>
    public float RaiderDestroyChance = 0.35f;
    /// <summary>NPC-Schiffe reparieren ihre Hülle langsam, solange sie an einer Station liegen (je Tick).</summary>
    public float NpcHullRegenPerTick = 0.05f;

    /// <summary>Fahndung: Heat je Verbrechen (Schlüssel).</summary>
    public Dictionary<string, float> CrimeHeat = new() { ["smuggling"] = 2.5f, ["toll_evasion"] = 1.5f, ["piracy"] = 1.2f, ["assault"] = 3f };
    /// <summary>Fahndung: Strafe je Verbrechen (Credits, Gericht).</summary>
    public Dictionary<string, float> CrimeFine = new() { ["smuggling"] = 400f, ["toll_evasion"] = 100f, ["piracy"] = 800f, ["assault"] = 1000f };
    /// <summary>Fahndung: Schwellen der Stufen 1–4 (auffällig, verwarnt, gesucht, Staatsfeind); Heat höchstens <see cref="WantedMaxHeat"/>.</summary>
    public List<float> WantedLevels = new() { 1f, 2f, 3f, 4f };
    public float WantedMaxHeat = 5f;
    /// <summary>Fahndung: Verfall je Tick nach der Mindestdauer seit dem letzten Verbrechen.</summary>
    public float WantedDecayPerTick = 0.0003f;
    public int WantedMinTicks = 1000;
    /// <summary>Gericht: Strafe zusätzlich je Heat-Punkt.</summary>
    public float FinePerHeat = 100f;
    /// <summary>Gericht: Rechtshilfe — Strafen anderer Fraktionen werden angenommen, wenn deren Beziehung zur Gerichtsfraktion mindestens so hoch ist.</summary>
    public float ExtraditionRelation = 0f;
    /// <summary>Kopfgeld je Fahndungsstufe des Besitzers eines besiegten Kaperschiffs (mindestens Stufe 1), ausgezahlt aus der Fraktionskasse.</summary>
    public float BountyPerLevel = 150f;

    // ───────── Phase 12: Befehle, Verbände, Spielerflotte, Hangar, Taxi, Leihschiff (FLOTTEN.md Abschnitte 3–5, UI.md Abschnitt 3) ─────────
    /// <summary>Befehlskapazität: höchstens so viele einzeln befehligte Schiffe je Größenklasse des Akteurs (Einzelakteur … Staat). Fehlen die Felder in alten Spielständen, gelten die Standards.</summary>
    public List<int> CommandShips = new() { 4, 8, 16, 32, 64 };
    /// <summary>Befehlskapazität: höchstens so viele Verbände je Größenklasse (POLITIK.md Abschnitt 7: Kleinfirma einer, Firma bis drei).</summary>
    public List<int> CommandSquadrons = new() { 1, 1, 3, 6, 12 };
    /// <summary>Höchstzahl der Mitglieder eines Verbands (Anführer eingeschlossen; Referenz <c>MaxFleetPilots</c>).</summary>
    public int SquadronMaxMembers = 8;
    /// <summary>Notfall-Ebene: unter diesem Anteil der Maximalhülle ruht der Befehl, das Schiff fliegt zur Reparatur.</summary>
    public float EmergencyHullFraction = 0.3f;
    /// <summary>Piraten überfallen auch nicht aktive Spielerschiffe (ohne Vorwarnung, nie Schiffsverlust). Aus = nur NPCs und das aktive Schiff.</summary>
    public bool FleetRaids = true;
    /// <summary>Länge eines Spieltags in Ticks (Liegegebühr, Miete): 2400 = 10 Minuten Spielzeit.</summary>
    public int DayTicks = 2400;
    /// <summary>Liegegebühr je Tag an fremden Stationen nach Klassengröße, wenn die Klasse keinen eigenen Satz hat (UI.md: Skiff 12, leichter Frachter 24, schwer 60).</summary>
    public Dictionary<string, float> BerthFeeBySize = new() { ["S"] = 12f, ["M"] = 24f, ["L"] = 60f, ["XL"] = 120f };
    /// <summary>Verkauf eines eigenen Schiffs: Anteil des Materialwerts (Bausatz + Module zum Basispreis), den die Station zahlt.</summary>
    public float ShipSellShare = 0.5f;
    /// <summary>Taxi: Grundgebühr, je Sprung, je Streckeneinheit (Luftlinie in Sektoren), Tempo und Ticks je Sprung.</summary>
    public float TaxiBaseFee = 40f;
    public float TaxiPerJump = 30f;
    public float TaxiPerUnit = 0.1f;
    public float TaxiSpeed = 12f;
    public int TaxiJumpTicks = 8;
    /// <summary>Leihschiff: Modell, einmalige Gebühr, Miete je angefangenem Tag (aus dem Pfand), Pfand, höchstens so viele gleichzeitig.</summary>
    public string RentalModel = "pip";
    public float RentalFee = 60f;
    public float RentalPerDay = 120f;
    public float RentalDeposit = 400f;
    public int RentalMaxPerPlayer = 1;
    /// <summary>Vorgaben der Automatikaufträge in der Oberfläche: Reichweite (Sprünge), höchster Einkaufspreis (× Basispreis), Rücklage.</summary>
    public int AutoRadiusJumps = 2;
    public float AutoMaxBuyFactor = 1.5f;
    public float AutoReserve = 200f;

    // ───────── Phase 13: Forschung (FORSCHUNG.md) ─────────
    /// <summary>Forschung an: Projekte sperren ihre Freischaltungen, bis die Fraktion sie erforscht hat. Aus = alles frei, Forschungsstationen ruhen (Vergleichsläufe, Tests).</summary>
    public bool ResearchActive = true;
    /// <summary>Forschungsstationen bieten für ihre Verbrauchsgüter höchstens Basispreis × diesen Faktor (sonst überböten sie mit 4× die Fabriken).</summary>
    public float ResearchMaxBidFactor = 1.25f;
    /// <summary>Mitfinanzierung durch den Spieler (Dienst „research“): Credits je Forschungspunkt.</summary>
    public float ResearchCreditsPerPoint = 20f;
    /// <summary>Wählbare Beträge der Mitfinanzierung (Angebote „fund:&lt;Betrag&gt;“).</summary>
    public List<float> ResearchFundAmounts = new() { 500f, 2000f, 10000f };
    /// <summary>Gebühr, um das laufende Projekt einer Fraktion festzulegen (Angebot „commission:&lt;Projekt&gt;“, an die Station).</summary>
    public float ResearchCommissionFee = 500f;

    // ───────── Phase 14: Schalter Treibstoff, Exportsenke ─────────
    /// <summary>
    /// Treibstoff an (Phase 4): Reisen verbrauchen Treibstoff, KI-Schiffe tanken, der Dienst „refuel“ und der Notruf gibt es. Aus = niemand verbraucht,
    /// keine Tankstopps/Notkäufe, keine Treibstoffkosten in der Routenbewertung, kein Notfall „Tank leer“; die Weltdatei lässt dann Stationen weg,
    /// die nur Treibstoff herstellen (<see cref="Simulation.FeatureSwitches"/>). Standard im Code an (Testwelten), die Standardwelt schaltet ihn ab.
    /// </summary>
    public bool FuelActive = true;
    /// <summary>Exportsenke: Ticks, bis eine angekaufte Charge an den Planeten geht (Stationstyp kann es überschreiben).</summary>
    public int ExportDelayTicks = 1200;
    /// <summary>Exportsenke: Erlös einer Charge = bezahlter Ankaufpreis × (1 + Aufschlag) (Stationstyp kann es überschreiben).</summary>
    public float ExportMarkup = 0.18f;
    /// <summary>Exportsenke: höchstes Gebot für Exportwaren als Vielfaches des Basispreises (sonst böte ein leeres Exportlager 4× und kaufte die Kolonie leer).</summary>
    public float ExportMaxBidFactor = 0.9f;

    // ───────── Phase 15: Wirtschaftskorrekturen (Betriebskapital, Instandhaltung, Subventionen) ─────────
    /// <summary>
    /// Steuerfreies Betriebskapital einer Station als Vielfaches ihres Einkaufswerts (Σ Wunschbestand × Basispreis aller Waren, die sie annimmt:
    /// Zutaten, Lager, Exportwaren; <see cref="Simulation.WorkingCapital"/>). Die Vermögensabgabe trifft nur Geld darüber. 0 = alte Regel (alles besteuert):
    /// Die Exportsenken verloren so ihre Startkasse an die Fraktionskassen und konnten nur noch wenig ankaufen.
    /// </summary>
    public float StationWorkingCapitalShare = 1f;
    /// <summary>Instandhaltung je Tag (<see cref="DayTicks"/>) als Anteil der Baukosten des Stationstyps, zum Basispreis in Material umgerechnet; 0 = aus.</summary>
    public float MaintenanceShare = 0.03f;
    /// <summary>Instandhaltungsmaterial: Ware → Gewicht (Anteil am Wert). Fehlt eine Ware in der Welt, fällt ihr Anteil weg.</summary>
    public Dictionary<string, float> MaintenanceMaterials = new() { ["steel"] = 0.55f, ["hull_plates"] = 0.3f, ["electronics"] = 0.15f };
    /// <summary>Wunschbestand eines reinen Instandhaltungs-Eintrags in Tagesbedarfen.</summary>
    public float MaintenanceStockDays = 2f;
    /// <summary>Höchstgebot reiner Instandhaltungs-Einträge (Vielfaches des Basispreises), damit sie Fabriken die Zutaten nicht wegbieten.</summary>
    public float MaintenanceMaxBidFactor = 1.25f;
    /// <summary>Subvention: Eine notleidende Station bekommt aus der Fraktionskasse Geld bis zu diesem Anteil ihres Betriebskapitals.</summary>
    public float SubsidyFloorShare = 0.5f;
    /// <summary>Subvention: höchstens so viel je Station und Tag (<see cref="DayTicks"/>).</summary>
    public float SubsidyPerDay = 2400f;
    /// <summary>Subvention: Rücklage, die die Fraktionskasse nicht unterschreitet.</summary>
    public float SubsidyReserve = 20000f;
    /// <summary>
    /// Steuerbremse: Zielanteil aller Fraktionskassen an der Geldmenge (je Fraktion zu gleichen Teilen); je näher eine Kasse ihrem Ziel, desto kleiner der Teil
    /// der Vermögensabgabe, den sie behält — der Rest verlässt die Welt (<see cref="Simulation.UpkeepSystem.FiscalFactors"/>). 0 = aus (alles in die Kasse).
    /// </summary>
    public float TreasuryTargetShare = 0.4f;
    /// <summary>Händler/Schürfer der KI, die so lange ohne Auftrag liegen (auch mit Geld), werden abgebaut (<see cref="Simulation.FleetSystem"/>); 0 = nie.</summary>
    public int IdleRetireTicks = 6000;
    /// <summary>
    /// Höchstes Gebot eines Endverbrauchers (Station mit Rezepten ohne Produkte, z. B. Kolonie) als Vielfaches des Basispreises; 4 = kein eigener Deckel
    /// (Code-Standard, wie bis Phase 13). Die Standardwelt setzt 2: sonst bleibt die Kolonie am 4×-Deckel stecken, weil sie dort zu wenig kaufen kann.
    /// </summary>
    public float ConsumerMaxBidFactor = 4f;
}
