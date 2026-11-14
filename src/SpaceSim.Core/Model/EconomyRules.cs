namespace SpaceSim.Core.Model;

/// <summary>Tuningwerte der Wirtschaft (Sinks, Faucets, Wachstum). Datengetrieben und Teil des Spielstands.</summary>
public sealed class EconomyRules
{
    // Sinks
    /// <summary>Frühere Credit-Pauschale je geflogener Einheit. Seit Phase 4b verbrauchen KI-Schiffe echten Treibstoff (siehe Simulation.Refuel); der Wert wird nicht mehr abgebucht, bleibt aber Datenbestand alter Spielstände.</summary>
    public float FuelCostPerUnit = 0.02f;
    /// <summary>
    /// Hüllenplatten je repariertem Hüllenpunkt (Dienst „repair“, Phase 4c); der Arbeitsaufschlag wird extra in Credits berechnet. Phase 26 (Korrektur nach der
    /// Prüfung): 0,05 → 0,02 für Spieler und NPC gleich (eine Preisregel): mit ≈ 8 Cr je Hüllenpunkt kostete eine volle Händlerhülle ≈ 885 Cr bei ≈ 4000 Cr
    /// Kapital — die Reparatur trieb Händler in die Pleite (Standard 300000: 14 statt 2); jetzt ≈ 3 Cr je Punkt.
    /// </summary>
    public float RepairPlatesPerHull = 0.02f;
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
    /// <summary>
    /// Phase 16: Die Zahlungsbereitschaft einer Fabrik rechnet ihr Produkt höchstens zum besten Gebot eines Abnehmers (<see cref="Simulation.MarketSystem.BuyerBids"/>),
    /// nicht zum eigenen Knappheitspreis. false = alte Regel (eigener Preis).
    /// </summary>
    public bool WillingnessFromBuyers = true;
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
    /// <summary>
    /// Phase 24 (Korrektur): ein Aufgabenwechsel braucht eine Mindestbewertung der neuen Aufgabe (Cr/Tick); im Torlauf wechselten zwei Tinker 34–42-mal
    /// zwischen Bewertungen von 0 und 0,1 hin und her. 0 = aus.
    /// </summary>
    public float JobMinSwitchScore = 0.5f;
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
    /// <summary>
    /// Phase 21: Der Lebenszyklus wählt für einen Neubau zuerst Sektoren, in denen die Erz-Zutaten des Typs gefördert werden (dann wie bisher die dünn
    /// besiedelten). false = Reihenfolge bis Phase 20.
    /// </summary>
    public bool BuildNearOre = true;
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
    /// <summary>
    /// Polizei: Unterhalt je Schiff und Tick aus der Fraktionskasse (Senke). Phase 20: 0,8 → 0,4 — seit der Fiskalbremse (Phase 15) fließt nur noch ein Teil
    /// der Abgaben in die Kassen; der Unterhalt der Grundstärke allein war damit ≈ 12 % der Kasseneinnahmen (Phase 11 kalibriert auf 6,7 %).
    /// </summary>
    public float PoliceUpkeepPerTick = 0.4f;
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
    /// <summary>
    /// Prüfung Phase 28 (Langlauf A/B/F/J/K: Piraten starben aus): Notversorgung im Versteck ohne Geld — ein Kaperschiff im eigenen Versteck wird kostenlos
    /// langsam bis auf diesen Hüllenanteil geflickt (und ausgefallene Module knapp über die Ausfallgrenze), auch wenn die Kasse leer ist. Vorher blieb ein
    /// beschädigtes Kaperschiff einer leeren Kasse für immer im Notfall (Heimkehr → Versorgung ohne Geld → Notfall …) und die Gruppe jagte nie wieder.
    /// 0 = aus.
    /// </summary>
    public float PirateBasePatchHull = 0.6f;
    /// <summary>Prüfung Phase 28: Flickgeschwindigkeit im Versteck (Hüllenanteil bzw. Modulzustand je Tick).</summary>
    public float PirateBasePatchPerTick = 0.0004f;
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
    public Dictionary<string, float> CrimeHeat = new() { ["smuggling"] = 2.5f, ["toll_evasion"] = 1.5f, ["piracy"] = 1.2f, ["assault"] = 3f, ["assault_station"] = 4.5f, ["assault_gate"] = 4.5f };
    /// <summary>Fahndung: Strafe je Verbrechen (Credits, Gericht).</summary>
    public Dictionary<string, float> CrimeFine = new() { ["smuggling"] = 400f, ["toll_evasion"] = 100f, ["piracy"] = 800f, ["assault"] = 1000f, ["assault_station"] = 1500f, ["assault_gate"] = 2000f };
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
    /// <summary>
    /// Runde 24 (Flottentest: die Grenze griff nie — jedes Schiff hob die Größenklasse schneller als die Grenze): Gewicht der eigenen Schiffe in der
    /// <b>Befehlsstufe</b> (<see cref="Simulation.Commands.CapacityClass"/>); Stationen, Sektoren und Geld zählen wie in der Größenklasse. 0 = Schiffe zählen nicht.
    /// </summary>
    public float CommandShipWeight = 0.25f;
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
    /// <summary>Runde 25 (Messregel): Hersteller verkaufen ihr reines Produkt höchstens zu diesem Anteil am besten Abnehmergebot; 0 = aus (Standard).</summary>
    public float ProducerPriceCapShare = 0f;
    /// <summary>Runde 30: Rückkaufdeckel für Spielerhandel an derselben Station (<see cref="Trading.PlayerLedger"/>); false = Messung ohne Deckel (Sonde).</summary>
    public bool BuybackCap = true;
    /// <summary>
    /// Runde 32: Reichweite des Rückkaufdeckels. true (Standard, Variante V1) = solange ein Kauf-Posten offen ist, gilt für JEDEN Verkauf dieser Ware an dieser
    /// Station höchstens der niedrigste offene Kaufpreis (Menge und Herkunft egal; Spiegel beim Kauf); false = Runde 31 (nur die Postenmenge gedeckelt —
    /// die Prüfung fand damit Schleifen mit mitgebrachter Ware, nur noch zum Messen).
    /// </summary>
    public bool BuybackWholeTrade = true;
    /// <summary>Runde 31/32: ein Kauf-Posten verfällt nicht, solange der Spieler Ware dieser Sorte hält (an Bord seiner Schiffe oder im Lager der Station); false = Messung.</summary>
    public bool BuybackHeldKeepsPost = true;
    /// <summary>
    /// Runde 32, nur Messung (Variante V2 der Prüfung): Dock-Handel des Spieler-Akteurs stückweise entlang der Bestandskurve — jede Einheit zum Preis nach dem
    /// Bestand, den die vorigen Einheiten dieses Handels hinterlassen (Tickpreis × Kurvenverhältnis); zustandslos, ohne Posten. Standard aus.
    /// </summary>
    public bool BuybackPiecewise = false;
    /// <summary>
    /// Runde 32b, nur Messung (Vorschlag): ein Posten, von dem der Spieler nichts mehr hält, verfällt auch ohne Preisrückkehr, sobald der Bestand zurück ist und
    /// seit der letzten Buchung so viele Ticks vergangen sind (0 = aus, Standard). Gegen „ewige“ Posten, deren Preis aus anderen Gründen oben bleibt.
    /// </summary>
    public int BuybackStaleTicks = 0;
    /// <summary>Runde 32b, nur Messung: der Messvorschlag <see cref="BuybackStaleTicks"/> ohne die Bestandsbedingung (nur Zeit und nichts gehalten).</summary>
    public bool BuybackStaleIgnoresStock = false;
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
    /// Phase 16: Rohstoff-Filter des Lebenszyklus an/aus (Phase 15, <see cref="Simulation.StationLifecycleSystem.UpstreamProducer"/>): kein weiterer Hersteller,
    /// dessen Erz-Zutat selbst knapp ist. false = alte Regel (immer der günstigste Hersteller) — für Tests, die den Bau eines bestimmten Typs prüfen.
    /// </summary>
    public bool BuildChecksRawOre = true;
    /// <summary>
    /// Phase 18: Bau folgt der Kette (<see cref="Simulation.StationLifecycleSystem.UpstreamProducer"/>): Ist eine hergestellte Zutat des Herstellers einer
    /// knappen Ware selbst knapp (über <see cref="BuildPriceRatio"/>), entsteht ihr Hersteller statt eines weiteren Verbrauchers. false = Regel bis Phase 17.
    /// </summary>
    public bool BuildFollowsChain = true;
    /// <summary>
    /// Höchstes Gebot eines Endverbrauchers (Station mit Rezepten ohne Produkte, z. B. Kolonie) als Vielfaches des Basispreises; 4 = kein eigener Deckel
    /// (Code-Standard, wie bis Phase 13). Die Standardwelt setzt 2: sonst bleibt die Kolonie am 4×-Deckel stecken, weil sie dort zu wenig kaufen kann.
    /// </summary>
    public float ConsumerMaxBidFactor = 4f;
    /// <summary>
    /// Phase 18: Ein Rest unter dieser Menge darf die Annahmegrenze einer Station überschreiten, wenn sie die Ware überhaupt annimmt (Annahme &gt; 0, keine
    /// Baustelle) und die ganze Menge bezahlen kann (<see cref="Trading.TradeActions.SaleUnits(float, float, float, float)"/>): Ein Lagerüberhang unter einer
    /// Einheit ist unkritisch, und so bleibt nach dem Bergbau kein unverkäuflicher Bruchteil im Laderaum. 0 = Regel von Phase 17.
    /// </summary>
    public float SaleRestTolerance = 1f;

    // ───────── Phase 19: Stationsbestand überprüfen (Stilllegung/Umwidmung) ─────────
    /// <summary>Zeitkonstante (Ticks) des gleitenden Mittelwerts der Stationsauslastung (<see cref="Station.Activity"/>): nach 15000 Ticks ohne Produktion fällt sie von 1 auf 0,08; 0 = keine Messung.</summary>
    public int UtilizationWindowTicks = 6000;
    /// <summary>Stilllegung/Umwidmung an (<see cref="Simulation.StationReview"/>); false = Bestand wird nie überprüft (Regel bis Phase 18).</summary>
    public bool StationRetireActive = true;
    /// <summary>Stilllegung nur, wenn die Auslastung (<see cref="Station.Activity"/>) darunter liegt („kaum produziert“).</summary>
    public float RetireMaxActivity = 0.1f;
    /// <summary>Schonfrist (Ticks) einer neu gebauten Station.</summary>
    public int RetireGraceTicks = 20000;
    /// <summary>Höchstens eine Stilllegung je so vielen Ticks (Abklingzeit, kein Pendeln).</summary>
    public int RetireCooldownTicks = 15000;
    /// <summary>Die Knappheit, für die ein Platz frei wird, muss mindestens so lange ununterbrochen bestehen (<see cref="Universe.ScarceSince"/>).</summary>
    public int RetireScarceTicks = 5000;
    /// <summary>
    /// Hysterese: Stillgelegt wird nur, wenn keines der Produkte der Station knapp ist — Verhältnis Wunsch/Bestand über alle Halter höchstens dieser Wert
    /// (1 = mindestens Wunschbestand vorhanden).
    /// </summary>
    public float RetireMaxOutputRatio = 1f;
    /// <summary>Restwert: das Lager geht zu diesem Anteil des Basispreises an den Besitzer (dazu die Stationskasse); der Rest verfällt.</summary>
    public float RetireSalvageShare = 0.25f;
    /// <summary>Phase 19: Fraktionskassen kaufen ein Bauschiff nach, wenn eine Baustelle keins erreicht (<see cref="Simulation.ConstructionRules.CommissionBuilder"/>).</summary>
    public bool CommissionBuilders = true;
    /// <summary>
    /// Phase 19: Händler rechnen die Ladung anderer Händler, die schon zu einem Abnehmer unterwegs sind, von dessen Annahme ab (Planung: auch die Kapazität
    /// derer auf dem Weg zum Einkauf; Einkauf: nur, was schon an Bord ist). Bis Phase 18 nur für Baustellen. false = alte Regel.
    /// </summary>
    public bool ReserveIncoming = true;
    /// <summary>
    /// Phase 19: So lange (Ticks) behält ein KI-Händler einen Rest, den keine erreichbare Station annimmt, und versucht ihn bei jedem Nachdenken zu verkaufen,
    /// bevor er ihn im All abwirft (0 = sofort). Ohne Frist verloren Händler das Geld für jeden Rest und gingen reihenweise pleite.
    /// </summary>
    public int StrandedCargoTicks = 3000;
    /// <summary>
    /// Phase 19: Annahmegrenze der Exportwaren einer Exportsenke als Vielfaches des Wunschbestands (wartende Chargen liegen im Lager); 0 = wie
    /// <see cref="OverstockFactor"/>. Die Standardwelt senkt <see cref="OverstockFactor"/> auf 2,5 (voller Lagerpreis 0,4× statt 0,33×) und lässt den Senken 3.
    /// </summary>
    public float ExportOverstockFactor;
    // ───────── Phase 20: Schwächen aus den Langläufen ─────────
    /// <summary>
    /// Phase 20: „Panik“ bei Versorgungsabriss (<see cref="Simulation.SupplyPanic"/>): Verliert ein Kartenteil durch ein geschlossenes/zerstörtes Gate den letzten
    /// Hersteller einer Ware, die dort gebraucht wird, baut der Stationsbau dort dringlich Ersatz. false = keine Panik (Regel bis Phase 19).
    /// </summary>
    public bool SupplyPanicActive = true;
    /// <summary>Phase 20: So lange (Ticks) muss ein Hersteller im Teil fehlen, bevor Panik ausbricht (kurze Schließungen lösen nichts aus).</summary>
    public int PanicDelayTicks = 1000;
    /// <summary>Phase 20: Notreserve — so viele Panik-Stationen (samt Baustellen) darf ein Kartenteil über <see cref="MaxStations"/> und die Sektorgrenze hinaus haben.</summary>
    public int PanicReserveStations = 2;
    /// <summary>Phase 20: Flottengrenzen je erreichbarem Kartenteil (<see cref="Simulation.FleetCapacity"/>); false = Grenze der Welt wie bis Phase 19.</summary>
    public bool FleetCapacityByPart = true;
    /// <summary>Prüfung Phase 28 (Lauf J): im geteilten Zustand kauft die Flotte (außer dem Notkauf der Fraktionskasse) nur, solange die ganze Welt unter der Grenze liegt.</summary>
    public bool FleetWorldCapWhenDivided = true;
    /// <summary>
    /// Phase 20: Kein Neukauf für eine Aufgabe, solange im selben Kartenteil ein arbeitsfähiges Schiff dieser Aufgabe mindestens so lange (Ticks) untätig ist
    /// (es gibt keine Arbeit für ein weiteres). 0 = aus.
    /// </summary>
    public int ExpandIdleTicks = 1000;
    /// <summary>Phase 20: Nach einer Ausmusterung wegen Leerlauf kauft die Flotte im selben Kartenteil so lange (Ticks) kein Schiff dieser Aufgabe. 0 = aus.</summary>
    public int ExpandAfterRetireTicks = 6000;
    /// <summary>
    /// Phase 20: Pleite ist nur, wer weder Geld noch Fracht hat. Ein Händler mit unverkäuflicher Fracht an Bord versucht sie weiter zu verkaufen
    /// (<see cref="StrandedCargoTicks"/>), statt nach <see cref="ShipBankruptTicks"/> samt Ladung aufgegeben zu werden. false = Regel bis Phase 19.
    /// </summary>
    public bool BankruptNeedsEmptyHold = true;
    /// <summary>
    /// Phase 20: Fällt die belegte Kapazität einer Aufgabe in einem Kartenteil unter diesen Anteil ihrer Grenze und kann dort kein Schiff der Aufgabe nachkaufen,
    /// kauft die Kasse der Besitzerfraktion ein Schiff (<see cref="Simulation.FleetSystem"/>). 0 = aus.
    /// </summary>
    public float FleetTreasuryFloorShare = 0.6f;
    /// <summary>Phase 20: Diese Summe behält ein KI-Händler beim Einkauf als Betriebsreserve (Credits); 0 = alles einsetzen (Regel bis Phase 19).</summary>
    public float TraderCashReserve = 0f;
    /// <summary>
    /// Phase 20: Ein Mehrzweckschiff verlässt eine Aufgabe, die gerade nichts einbringt (Bewertung 0), erst, wenn es mindestens so lange (Ticks) untätig war.
    /// Vorher genügte ein Augenblickswert 0 direkt nach einer Lieferung (Abnehmer kurz voll), und die Schiffe wechselten alle 600 Ticks. 0 = Regel bis Phase 19.
    /// </summary>
    public int JobIdleSwitchTicks = 1500;
    /// <summary>Phase 20: Geglättete Polizei-Sollstärke — Zeitkonstante (Ticks) des gleitenden Mittels des Bedarfs (<see cref="Actor.PoliceNeed"/>); 0 = Augenblickswert.</summary>
    public int PoliceNeedWindowTicks = 20000;
    /// <summary>Phase 20: Gekauft wird erst, wenn der geglättete Bedarf die Schiffszahl um diesen Wert übersteigt.</summary>
    public float PoliceBuyMargin = 0.5f;
    /// <summary>
    /// Phase 20: Abgebaut wird erst, wenn der geglättete Bedarf mindestens um diesen Wert unter der Schiffszahl liegt (Hysterese: muss größer als
    /// 1 − <see cref="PoliceBuyMargin"/> sein, sonst gibt es keine Lücke zwischen Kauf- und Abbau-Schwelle) — und das ununterbrochen
    /// <see cref="PoliceDecommissionTicks"/> lang.
    /// </summary>
    public float PoliceSellMargin = 0.9f;
    /// <summary>Phase 20: Mindestbestand an Bauschiffen je Fraktion; die Kasse kauft nach und unterhält sie (<see cref="Simulation.FactionBuilderSystem"/>). 0 = aus.</summary>
    public int MinBuildersPerFaction = 1;
    /// <summary>Phase 20: Unterhalt eines Fraktions-Bauschiffs je Tick aus der Fraktionskasse (<c>TreasuryUpkeep</c> „builder“).</summary>
    public float BuilderUpkeepPerTick = 0.2f;

    // ───────── Phase 22: Abgänge nur aus drei Gründen (zerstört, pleite, länger nicht rentabel); Fraktionen bleiben handlungsfähig ─────────
    /// <summary>Phase 22: Zeitkonstante (Ticks) des gleitenden Mittelwerts der Rentabilität (<see cref="ProfitMeter.Rate"/>); 0 = keine Messung (Grund c aus).</summary>
    public int ProfitWindowTicks = 20000;
    /// <summary>Phase 22: Schonfrist nach Kauf/Bau (Ticks), in der nichts wegen Unrentabilität verschwindet.</summary>
    public int ProfitGraceTicks = 20000;
    /// <summary>Phase 22: So lange (Ticks) muss der Mittelwert unter null liegen, bevor ein Schiff/eine Station als „länger nicht rentabel“ gilt.</summary>
    public int UnprofitableTicks = 20000;
    /// <summary>
    /// Phase 22: Hysterese — der Verlustzähler (<see cref="ProfitMeter.LossTicks"/>) wird erst zurückgesetzt, wenn der Mittelwert über diesem Wert (Credits je
    /// Tick) liegt; zwischen 0 und diesem Wert ruht er.
    /// </summary>
    public float ProfitableAbove = 0.02f;
    /// <summary>Phase 22: Pleitegrenze eines KI-Schiffs (Credits; vorher fest 50 im Code).</summary>
    public float ShipBankruptCredits = 50f;
    /// <summary>
    /// Phase 22: Die Handelsbewertung rechnet den Schiffsunterhalt der Fahrzeit mit (<see cref="ShipUpkeepPerTick"/> × Reisezeit). Vorher nahm ein armes Schiff
    /// Fahrten, deren Spanne (1 Energie, 1 Cr) den Unterhalt nicht deckte, und verlor bei jeder Runde Geld bis zur Pleite. false = Regel bis Phase 21.
    /// </summary>
    public bool TripCountsUpkeep = true;
    /// <summary>
    /// Phase 22: Zusage einer Lieferung — ein Händler, der für eine Station eingekauft hat, liefert bei Ankunft bis zur normalen Annahme
    /// (Wunschbestand × <see cref="OverstockFactor"/>), auch wenn die Station inzwischen ein Rezept pausiert hat (Phase 15 nimmt Zutaten pausierter Rezepte nur bis
    /// zum Wunschbestand). Vorher warf der Händler die ganze Ladung (meist Wasserstoff) nach <see cref="StrandedCargoTicks"/> ab und ging pleite. false = aus.
    /// </summary>
    public bool HonourPlannedDelivery = true;
    /// <summary>
    /// Phase 22: Kapitalausgleich innerhalb eines Akteurs — fällt das Betriebsgeld eines leeren Schiffs eines aufgestiegenen Akteurs unter die Hälfte von
    /// <see cref="NewShipStartCredits"/>, gibt das reichste andere Schiff desselben Akteurs (mit mindestens <see cref="FleetExpandCredits"/>) bis zu diesem Betrag ab
    /// (kein neues Geld). Vorher verarmte ein Schiff einer Firma nach einem Überfall bis zur Pleite, während seine Geschwister Tausende hatten. false = aus.
    /// </summary>
    public bool FirmCapitalBalance = true;
    /// <summary>
    /// Phase 22: Kein weiterer Hersteller eines Typs, dessen fertige Stationen im Mittel mehr als diesen Anteil der Takte an Zutaten verhungern
    /// (<see cref="Station.Starved"/>) — sonst baute der Lebenszyklus für knappes Kupfer eine Hütte nach der anderen, die am Erz verhungerte und als unrentabel
    /// stillgelegt wurde. 0 = aus.
    /// </summary>
    public float BuildSkipStarvedShare = 0.5f;
    /// <summary>Phase 22: Rücklage einer Fraktionskasse = Pflichtunterhalt je Tick × so viele Ticks (<see cref="Simulation.FactionViability.Reserve"/>).</summary>
    public int FactionReserveTicks = 2400;
    /// <summary>
    /// Gerätetest (Phase 26): Aufbauzeit der Rücklage. Die Fraktionskassen beginnen leer und füllen sich aus den Abgaben; in den ersten so vielen Ticks
    /// nach dem Entstehen der Fraktion ist „Kasse unter Rücklage“ kein Verstoß gegen die Handlungsfähigkeit, sondern „Rücklage im Aufbau“ (gemessen: über
    /// Seeds 1–8 liegt jede Fraktion in den ersten 1000–2200 Ticks darunter). Die Ausgabenregeln halten die Rücklage trotzdem. 0 = sofort ein Verstoß.
    /// </summary>
    public int FactionReserveBuildTicks = 6000;
    /// <summary>Phase 22: Fraktionsausgaben nach Vorrang — freiwillige Ausgaben (Subvention, Kauf über Mindestbestand, Investitionen, Forschung, Sperren) halten die Rücklage. false = aus.</summary>
    public bool FactionKeepsReserve = true;
    /// <summary>
    /// Phase 22: Notkauf zur Wiederherstellung des Mindestbestands (Polizei, Bauschiff), wenn der normale Kauf nicht möglich ist: Die Kasse zahlt höchstens
    /// diesen Anteil des Materialwerts zum Basispreis (der Rest wird erlassen = neues Geld, Ereignis <c>FactionRestored</c>), das Schiff entsteht ohne Werftmaterial
    /// an der Heimatbasis. Bremse: höchstens ein Notkauf je Fraktion und <see cref="ViabilityRestoreCooldown"/>. 0 = kein Notkauf.
    /// </summary>
    public float ViabilityRestoreShare = 0.5f;
    /// <summary>
    /// Phase 22: Höchststand der Kasse einer Piratengruppe, bis zu dem die Hehlerei zahlt (darüber verfällt der Beuteerlös). 0 = unbegrenzt (Regel bis Phase 21).
    /// Standardwelt 10000 (fünf Neubauten): seit Kaperschiffe seltener zerstört werden, wuchsen die Piratenkassen sonst ohne Ausgabe.
    /// </summary>
    public float PirateTreasuryCap = 0f;
    public int ViabilityRestoreCooldown = 10000;

    // ───────── Phase 23: Schiffssysteme (KAMPF.md Abschnitt 3) ─────────
    /// <summary>Schildregeneration: so viel Schild je Sekunde liefert eine Leistungseinheit des Überschusses (gedeckelt durch stats shieldRegen der Schildmodule).</summary>
    public float ShieldPerPower = 0.25f;
    /// <summary>Speicherbank: so viel Ladung je Sekunde liefert eine Leistungseinheit des Überschusses.</summary>
    public float ChargePerPower = 1f;
    /// <summary>Nach einem Treffer auf den Schild lädt er so viele Ticks nicht nach (KAMPF.md 3.3 „nach einer kurzen Pause“; 20 Ticks = 5 s).</summary>
    public int ShieldDelayTicks = 20;
    /// <summary>Leistungsverteilung: Anteil des Überschusses für das gewählte System (Waffen, Schild oder Antrieb); die anderen beiden teilen sich den Rest. Ausgeglichen = je ein Drittel.</summary>
    public float PowerFocusShare = 0.6f;
    /// <summary>Stellung „Antrieb“: Höchsttempo des Spielerschiffs × (1 + Bonus), solange der Reaktor einen Überschuss hat (NPCs wählen die Stellung erst mit Phase 24).</summary>
    public float EngineModeSpeedBonus = 0.1f;
    /// <summary>Ein Modul mit Zustand unter diesem Wert ist ausgefallen (Reaktor/Antrieb laufen dann mit halber Leistung). In Phase 23 senkt noch nichts den Zustand.</summary>
    public float ModuleFailCondition = 0.25f;
    /// <summary>
    /// Munition als Ware (KAMPF.md 3.4, wie <see cref="FuelActive"/>): an = Ware „ammo“ mit Rezept und Lagerware. Aus = Magazine gelten als voll, und die
    /// Weltdatei lässt Rezepte und Lagerposten von Munition weg (<see cref="Simulation.FeatureSwitches"/>). In Phase 23 verbraucht noch niemand Munition.
    /// </summary>
    public bool AmmoActive = true;

    // ───────── Phase 24: Trefferregel und Rundengefecht (KAMPF.md Abschnitte 3.3, 3.4, 4.1, 4.2, 4.2a) ─────────
    /// <summary>Überfälle und Abfangen zwischen NPCs laufen als Rundengefecht (<see cref="Combat.CombatSystem"/>). false = Sofortauflösung über <see cref="Universe.Encounters"/> (Regel bis Phase 23, Testhilfe).</summary>
    public bool CombatRounds = true;
    /// <summary>Höchstdauer eines Gefechts in Ticks (240 = 60 s); danach brechen alle ab.</summary>
    public int CombatMaxTicks = 240;
    /// <summary>Anfangsabstand zwischen Angreifer und Opfer (Kaperer aus dem Hinterhalt, Streife stellt den Lauerer).</summary>
    public float CombatStartDistance = 30f;
    /// <summary>Helfer (Polizei beim Überfall) stoßen so viel weiter hinten zum Gefecht.</summary>
    public float CombatHelperDistance = 30f;
    /// <summary>Wer flieht, ist entkommen, wenn ihn kein Gegner mehr erreicht: Abstand größer als die Reichweite jedes aktiven Gegners, mindestens dieser Wert.</summary>
    public float CombatMinEscapeDistance = 30f;
    /// <summary>
    /// Gerätetest Phase 28 (Runde 6): so lange nach seinem letzten eigenen Schuss hält die eigene Waffenreichweite den Spieler im Gefecht (40 = 10 s, länger als
    /// die längste Nachladezeit 8 s) — vorher lief die Nachladeuhr im selben Takt auf 0, in dem die Flucht geprüft wurde, und der Spieler galt zwischen zwei Salven als geflohen.
    /// </summary>
    public int PlayerFireHoldTicks = 40;
    /// <summary>
    /// Runde 7: ein Schiff hat in einem Gefecht nie zwei Einträge — wer wieder beitritt, nimmt seinen alten Eintrag (Mut, Abwurfanteil und Nachladeuhren
    /// bleiben, kein Zufallszug). false = Verhalten bis Runde 6 (neuer Eintrag mit zwei Zufallszügen; <see cref="Model.Encounter.Of"/> fand weiter den alten).
    /// </summary>
    public bool CombatRejoinReuse = true;
    /// <summary>Trefferregel: Trefferchance fällt bis zur Reichweite um diesen Anteil (quadratisch im Abstandsverhältnis).</summary>
    public float HitRangeFalloff = 0.5f;
    /// <summary>Trefferregel: Zieltempo, gegen das „tracking“ gemessen wird (Einheiten je Sekunde).</summary>
    public float HitTrackingSpeedRef = 10f;
    /// <summary>Trefferregel: Grenzen der Trefferchance (auch ein schlechter Schuss trifft manchmal, kein Schuss sicher).</summary>
    public float HitChanceMin = 0.05f;
    public float HitChanceMax = 0.95f;
    /// <summary>Trefferregel: Schaden streut ± diesen Anteil.</summary>
    public float HitDamageSpread = 0.2f;
    /// <summary>Schadensart gegen Schild (Faktor auf den Schaden, den der Schild abfängt): Energie stark, kinetisch schwach.</summary>
    public float EnergyVsShield = 1.25f;
    public float KineticVsShield = 0.6f;
    /// <summary>Panzerung zieht je Treffer Panzerwert × diesen Anteil ab, gewichtet nach Art (Energie prallt stärker ab).</summary>
    public float ArmorPerHitShare = 0.05f;
    public float EnergyVsArmor = 1.5f;
    public float KineticVsArmor = 0.5f;
    /// <summary>Zielgröße (Signatur) je Klassengröße S, M, L, XL: große Schiffe sind auch schnell leichter zu treffen.</summary>
    public List<float> CombatSignature = new() { 1f, 1.5f, 2.2f, 3f };
    /// <summary>Feuerwinkel als fester Abschlag je Waffe: Faktor 1 − Abschlag × (1 − Winkel/360) (Turm 360° ohne Abschlag).</summary>
    public float CombatArcPenalty = 0.2f;
    /// <summary>Auf der Flucht feuern Waffen mit Bugwinkel (&lt; 180°) nur mit diesem Faktor (sie zeigen in die falsche Richtung).</summary>
    public float CombatFleeFireFactor = 0.5f;
    /// <summary>Gewünschter Abstand im Gefecht als Anteil der eigenen größten Reichweite.</summary>
    public float CombatPreferredRange = 0.7f;
    /// <summary>Kaperer flieht unter diesem Hüllenanteil …</summary>
    public float RaiderFleeHull = 0.5f;
    /// <summary>… oder wenn die eigene Seite schwächer ist als dieser Anteil der Gegenseite (Stärke nach <see cref="Simulation.CombatRules.Strength(Ship)"/>).</summary>
    public float RaiderFleeRatio = 0.6f;
    /// <summary>Polizei zieht sich unter diesem Hüllenanteil zurück (sonst hält sie stand).</summary>
    public float PoliceRetreatHull = 0.3f;
    /// <summary>Begleiter ziehen sich unter diesem Hüllenanteil zurück.</summary>
    public float EscortRetreatHull = 0.3f;
    /// <summary>Händler wehrt sich, wenn er bewaffnet ist und seine Seite mindestens so stark ist wie dieser Anteil der Gegenseite; sonst flieht er.</summary>
    public float TraderFightRatio = 0.8f;
    /// <summary>Mut eines Händlers: Hüllenanteil, unter dem er Fracht abwirft, gleichverteilt zwischen diesen Werten (beim Eintritt gewürfelt).</summary>
    public float SurrenderHullMin = 0.05f;
    public float SurrenderHullMax = 0.6f;
    /// <summary>Anteil der Fracht, den ein Händler beim Aufgeben abwirft, gleichverteilt zwischen diesen Werten (beim Eintritt gewürfelt; ersetzt LootMin/LootMax im Rundengefecht).</summary>
    public float SurrenderDropMin = 0.4f;
    public float SurrenderDropMax = 0.7f;
    /// <summary>Waffen mit Munition feuern nur ab dieser Trefferchance (Munition sparen auf große Entfernung); 0 = immer.</summary>
    public float AmmoMinHitChance = 0.3f;
    /// <summary>
    /// Bewaffnete Händler (KAMPF.md 4.2a): die KI-Flotte wertet ein bewaffnetes Modell beim Kauf um (1 + dieser Faktor × Risiko × Bewaffnung) auf; Risiko =
    /// mittleres Routenrisiko der Lagekarte ab dem Kaufsektor, Bewaffnung = Waffenstärke ÷ 6 (höchstens 1). 0 = Bewaffnung zählt nicht.
    /// </summary>
    public float ArmedTraderWeight = 40f;
    /// <summary>
    /// Phase 25 („jeder Händler bewaffnet“): Waffenstärke, die jedes Handelsmodell ohnehin trägt (Standardwelt 6 ≈ eine Laserkanone). Die Kaufwahl wertet nur
    /// Bewaffnung darüber hinaus nach dem Routenrisiko auf (0 = Regel aus Phase 24: jede Waffe zählt).
    /// </summary>
    public float ArmedTraderBaseline = 0f;
    /// <summary>Kaperer meiden bewaffnete Opfer: Gewicht der Beute ÷ (1 + dieser Faktor × Waffenstärke ÷ 6).</summary>
    public float RaiderArmedAversion = 1f;
    /// <summary>Munition ohne Lager (Piratenversteck, Heimatbasis ohne Munition): Preis je Einheit = Basispreis × dieser Faktor (Geldsenke wie die Reparatur dort).</summary>
    public float AmmoBaseMarkup = 1f;
    /// <summary>Polizei kehrt zum Nachladen heim, wenn das Magazin unter diesen Anteil fällt.</summary>
    public float AmmoReturnShare = 0.3f;

    // ───────── Phase 25: Spielerkampf (KAMPF.md 4.3, 5) ─────────
    /// <summary>
    /// Gefechte im Spielersektor laufen räumlich: echte Positionen und Feuerwinkel, die Simulation schreibt die Positionen über den
    /// <see cref="Combat.CombatAutopilot"/> fort. false = überall die Abstandszahl (Regel bis Phase 24; Testhilfe für den Vergleich).
    /// </summary>
    public bool SpatialCombat = true;
    /// <summary>Wendegeschwindigkeit der NPC-Schiffe im räumlichen Gefecht (Grad je Sekunde).</summary>
    public float CombatTurnRate = 120f;
    /// <summary>Im Abstandsband (gewünschter Abstand × [Untergrenze, Obergrenze]) kreisen Schiffe mit diesem Anteil ihres Tempos um das Ziel.</summary>
    public float CombatOrbitSpeedShare = 0.5f;
    public float CombatBandMin = 0.75f;
    public float CombatBandMax = 1.1f;
    /// <summary>
    /// Leistungsverteilung „Waffen“ wirkt (Überladung): Steht die Stellung auf Waffen und hält die Speicherbank Spitzenlast × (1 + <see cref="OverchargeDrawFactor"/>),
    /// zieht der Schuss diese Menge und die Nachladezeit sinkt auf × <see cref="OverchargeReloadFactor"/>. Ohne Speicherbank keine Überladung.
    /// </summary>
    public float OverchargeReloadFactor = 0.65f;
    public float OverchargeDrawFactor = 2f;
    /// <summary>Handel verweigert: Ab dieser Fahndungsstufe beim Stationsbesitzer handelt eine Station nicht mit dem Spieler (0 = nie).</summary>
    public int TradeRefusalWantedLevel = 3;
    /// <summary>
    /// Verlust des aktiven Schiffs: Gibt es an der Landestation kein eigenes Schiff und kein bezahlbares Leihschiff, bekommt die Person ein Notfall-Leihschiff
    /// dieses Modells (leer = <see cref="RentalModel"/>) ohne Gebühr und Pfand; die Miete je Tag läuft wie bei jedem Leihschiff.
    /// </summary>
    public string EmergencyRentalModel = "";
    /// <summary>Räumliches Gefecht des Spielers: Ein Kaperer im Anflug (Vorwarnzeit) nähert sich dem Spieler bis auf diesen Abstand.</summary>
    public float RaidApproachDistance = 60f;

    // ───────── Phase 26: Schaden und Bergung (KAMPF.md 3.3, 4.4, 4.5) ─────────
    /// <summary>
    /// Hauptschalter der Phase 26: Modulschaden, Brand, Wracks und Frachtbehälter, bezahlte NPC-Reparatur statt Gratis-Regeneration. false = Verhalten bis
    /// Phase 25 (keine Zufallszüge für Modulschaden, keine Wracks, liegende NPC-Schiffe regenerieren die Hülle umsonst; Testhilfe für den Hash-Vergleich).
    /// </summary>
    public bool DamageActive = true;
    /// <summary>Modulschaden erst unter diesem Anteil der Höchsthülle (KAMPF.md 3.3: „unter 50 % Hülle“).</summary>
    public float ModuleDamageHullShare = 0.5f;
    /// <summary>Anteil der Hüllentreffer unter der Schwelle, die zusätzlich ein zufälliges Modul treffen.</summary>
    public float ModuleHitChance = 0.35f;
    /// <summary>Zustandsverlust eines Modultreffers: Grundwert plus Hüllenschaden ÷ Höchsthülle × Faktor.</summary>
    public float ModuleDamageBase = 0.12f;
    public float ModuleDamageScale = 2f;
    /// <summary>Brand: Grundchance je Modultreffer, steigt mit sinkendem Zustand (× (1 + (1 − Zustand) × Faktor)); Reaktor, Antrieb und Magazin × Brennbarkeit.</summary>
    public float FireChance = 0.08f;
    public float FireConditionFactor = 2f;
    public float FireFlammableFactor = 2f;
    /// <summary>Eine Brandrunde dauert so viele Ticks (4 = 1 s); je Runde verliert das brennende Modul Zustand und die Hülle etwas Schaden.</summary>
    public int FireRoundTicks = 4;
    public float FireConditionLoss = 0.04f;
    /// <summary>Hüllenschaden je Brandrunde als Anteil der Höchsthülle; ein Brand allein zerstört kein Schiff (Hülle bleibt ≥ 1).</summary>
    public float FireHullDamageShare = 0.005f;
    /// <summary>Löschchance je Runde ohne Löschanlage; die Löschanlage (Modulwert <c>extinguish</c>) addiert ihren Wert.</summary>
    public float FireExtinguishChance = 0.12f;
    /// <summary>NPC-Reparatur: Ein Händler/Schürfer sucht unter diesem Hüllenanteil (oder mit ausgefallenem Modul/Brand) aktiv eine Reparaturstation auf.</summary>
    public float NpcRepairSeekHull = 0.35f;   // Korrektur: 0,5/0,6 brachten mehr Reparaturflüge, aber nicht weniger Pleiten (gemessen)
    /// <summary>
    /// NPC-Reparatur: Leichte Schäden (nicht schwer im Sinne von <see cref="NpcRepairSeekHull"/>) repariert ein Händler/Schürfer nur aus dem Geld über diesem
    /// Betriebspolster. Korrektur nach der Prüfung: mit der billigeren Reparatur (0,02 Platten je Punkt) und dem kleinen Budgetanteil (0,1) reparieren NPC auch mittlere
    /// Schäden ab 1000 Cr Polster — mittlere Hüllenquote 0,83–0,89 (vorher 0,57–0,67), Pleiten je 300000 Ticks 2/0/2 (Standard/Seed 3/Seed 4; vorher 14/8/4).
    /// </summary>
    public float NpcRepairReserve = 1000f;
    /// <summary>NPC-Reparatur: höchstens dieser Anteil der Schiffskasse geht in eine Reparatur (der Rest bleibt Betriebsgeld).</summary>
    public float NpcRepairBudgetShare = 0.1f;
    /// <summary>Modulreparatur: Material je Zustandspunkt (0..1) und Größenstufe (S 1, M 2, L 3, XL 4) — Hüllenplatten und Elektronik.</summary>
    public float ModuleRepairPlates = 1f;
    public float ModuleRepairElectronics = 0.5f;
    /// <summary>Reparatur an der Heimatbasis (Polizei, Piraten, Fraktionsbauschiffe) aus der Kasse: Credits je Zustandspunkt und Größenstufe.</summary>
    public float BaseModuleRepairCost = 25f;
    /// <summary>Wracks: Lebensdauer, Frachtanteil, Überlebenschance je Modul, Restzustand-Spanne (× Zustand beim Abschuss).</summary>
    public int WreckTicks = 12000;
    public float WreckCargoShare = 0.5f;
    public float WreckModuleChance = 0.4f;
    public float WreckConditionMin = 0.3f;
    /// <summary>Frachtbehälter (abgeworfene Fracht, überzählige Fracht ausgefallener Frachtmodule): Lebensdauer in Ticks.</summary>
    public int ContainerTicks = 4800;
    /// <summary>Bergung: Reichweite (Traktorstrahl, Bergungsgreifer) und Mindestzustand, ab dem ein Modul brauchbar geborgen wird (darunter Schrott).</summary>
    public float SalvageRange = 30f;
    public float SalvageMinCondition = 0.5f;
    /// <summary>Phase 26 (Prüfung Phase 25): Gefechtsfläche eines Sektors (Abstand vom Mittelpunkt), in die <see cref="Combat.CombatSpace.Clear"/> Positionen begrenzt.</summary>
    public float CombatArenaRadius = 400f;
    /// <summary>Phase 26 (Prüfung): Notreparatur auf Schuld — Hülle auf diesen Anteil, ausgefallene Module auf Ausfallschwelle + Rand.</summary>
    public float EmergencyRepairHullShare = 0.25f;
    public float EmergencyRepairMargin = 0.05f;
    /// <summary>Kaperer nehmen die Fracht des Wracks ihres Opfers mit (Hehlerei wie Beute).</summary>
    public bool RaidersLootWrecks = true;
    /// <summary>
    /// Phase 29 (Runde 10): Kaperer schonen die Beute — im Überfall feuert ein Kaperer keine Waffe auf ein Opfer mit Fracht, deren Höchstschaden (Schaden ×
    /// (1 + Streuung)) zusammen mit den schon fliegenden Raketen auf dieses Ziel Schild und Hülle des Opfers aufbrauchen könnte (reine Regel, kein Zufallszug).
    /// Gemessen: seit die Piraten Bomber (zwei Raketenwerfer) kaufen, vernichtete eine Raketensalve leichte Schürfer (Skiff, Hülle 60) statt sie zur Aufgabe zu
    /// zwingen (300000 Ticks: 59 zerstörte Schürfer statt 6). Nicht für das aktive Spielerschiff (es bleibt in echter Gefahr). false = alte Regel.
    /// </summary>
    public bool RaidersSpareLoot = true;
    /// <summary>
    /// Runde 12: Piratenverstecke haben Geschütze (Weltdatei <c>defense</c>). Eine Verfolgung endet („safe“), sobald Ziel oder Streife näher als Geschützreichweite +
    /// <see cref="HideoutGunMargin"/> am Versteck des Ziels sind — die Polizei verfolgt nicht in den Tod. false = alte Regel (Ende erst im Schutzradius).
    /// </summary>
    public bool PursuitStopsAtHideoutGuns = true;

    // ───────── Phase 30: Kleinstationen ─────────
    /// <summary>Waffenstellungen eröffnen von sich aus das Feuer auf Gegner in Reichweite (Abwehrgefecht).</summary>
    public bool OutpostsEngage = true;
    /// <summary>Fraktionen und Piraten stellen Kleinstationen auf (<see cref="Simulation.OutpostBuilderSystem"/>).</summary>
    public bool OutpostsBuild = true;
    public int OutpostBuildCooldown = 3000;
    public float OutpostLightThreat = 1.2f, OutpostMediumThreat = 2.2f, OutpostCalmThreat = 0.2f;
    public int OutpostLightPerSector = 2, OutpostLightPerFaction = 4, OutpostMediumPerFaction = 2, OutpostHeavyPerFaction = 2, OutpostSatellitesPerFaction = 6;
    public int OutpostPiratePerHideout = 1;
    /// <summary>
    /// Runde 13b (Prüfung: Piratenschwund): Piraten stellen keine Stellungen am Versteck auf (false). Gemessen kosteten Stellungen, die die Polizei abräumte und die
    /// Piraten neu kauften, 48 % ihrer Einnahmen; mit true gilt die alte Regel.
    /// </summary>
    public bool OutpostPirates = false;
    /// <summary>
    /// Runde 13b: Kaperer (Gegner ohne Krieg) beschießt eine Stellung von sich aus nur, wenn sie ihr so nahe kommen (Nahbereich ab Rumpf); in Überfällen und Gefechten
    /// in ihrer Reichweite schießt sie ohnehin als Stationsgeschütz mit. Kriegsgegner beschießt sie in voller Reichweite, Gesuchte erst nach einer Warnung.
    /// </summary>
    public float OutpostCloseRange = 20f;
    /// <summary>Runde 13b: Kaperer wählen keinen Lauerposten in Geschützreichweite einer Stellung (+ Rand).</summary>
    public bool RaidersAvoidOutposts = true;
    /// <summary>Piraten stellen eine Stellung nur auf, wenn danach noch so viel in der Kasse bleibt.</summary>
    public float OutpostPirateReserve = 4000f;
    /// <summary>Abbau einer selbst gebauten Waffenstellung nach so langer Ruhe (Bedrohung unter <see cref="OutpostCalmThreat"/>, kein Krieg).</summary>
    public long OutpostCalmTicks = 40000;
    public float HideoutGunMargin = 15f;

    // ───────── Phase 27: Flottenkampf (KAMPF.md Stufe 27) ─────────
    /// <summary>
    /// Hauptschalter der Phase 27: Polizei und Piraten über die Befehls-Ausführer (Streife, Lauern, Verfolgen, Eskorte, Heimkehr), Verbände der Fraktionen und
    /// Rudel der Piratengruppen, Verstärkung laufender Gefechte, Fokusfeuer, Flucht zu Stationen/Toren, Polizeigefecht gegen gesuchte Spieler, Ergeben des Spielers.
    /// false = Verhalten bis Phase 26 (Testhilfe für den Hash-Vergleich).
    /// </summary>
    public bool FleetCombat = true;
    /// <summary>Ab so vielen Streifen einer Fraktion im selben Einsatzsektor fliegen sie als Verband (Anführer = kleinste Id).</summary>
    public int PoliceSquadronMin = 2;
    /// <summary>Höchstzahl Kaperschiffe, die eine Piratengruppe als Rudel (Verband) auf Beutezug schickt; 1 = jedes allein.</summary>
    public int RaiderPackSize = 2;
    /// <summary>Geleit: In Sektoren mit mindestens dieser Bedrohung begleitet eine Streife das wertvollste beladene Handelsschiff, solange es im Hoheitsgebiet bleibt.</summary>
    public float PoliceEscortThreat = 1.5f;
    /// <summary>Geleit: Mindestwert der Fracht (Basispreis), damit ein Schiff Geleit bekommt.</summary>
    public float PoliceEscortMinValue = 1500f;
    /// <summary>Geleit: Höchstdauer einer Begleitung in Ticks.</summary>
    public int PoliceEscortTicks = 2400;
    /// <summary>Verfolgung: Höchstdauer in Ticks, nachdem ein Kaperschiff aus einem Gefecht mit der Polizei geflohen ist.</summary>
    public int PursuitTicks = 1200;
    /// <summary>Verfolgung: Vorsprung des Geflohenen beim Beginn (Einheiten; Fluchtabstand).</summary>
    public float PursuitLead = 40f;
    /// <summary>Verstärkung: Takt (Ticks), in dem laufende Gefechte prüfen, ob Begleiter, Wachen, Verbands- und Rudelmitglieder im Sektor dazukommen.</summary>
    public int ReinforceTicks = 10;
    /// <summary>Flucht zu einer Zuflucht (Station mit nicht feindlichem Besitzer oder offenes Tor): erreicht, wenn der Abstand darunter fällt (Raum) bzw. auf 0 (Linie).</summary>
    public bool CombatRefuge = true;
    public float CombatRefugeRadius = 12f;
    /// <summary>Nur eine Zuflucht in höchstens diesem Abstand (beim Eintritt ins Gefecht) zählt — weiter weg holt der Gegner den Fliehenden ein, bevor er sie erreicht.</summary>
    public float CombatRefugeMaxDistance = 40f;
    /// <summary>Fokusfeuer: Polizei, Begleiter und Rudel nehmen den schwächsten Gegner in Reichweite (Schild + Hülle) gemeinsam unter Feuer; Begleiter zuerst den, der ihr Schiff angreift.</summary>
    public bool CombatFocusFire = true;
    /// <summary>Spieler ergibt sich Kaperern: Anteil jeder Ware, den er abwirft (die Kaperer nehmen ihn und drehen ab).</summary>
    public float PlayerSurrenderDropShare = 0.5f;
    // ───────── Phase 28: Raketen, Stationsverteidigung, Kriegsschiffe, Krieg (KAMPF.md Stufe 28) ─────────
    /// <summary>
    /// Hauptschalter der Phase 28: Raketen (Flug, Punktabwehr), Stationsgeschütze und Stationsschaden, Belagerung, Reparatur der Stationen, Kriegsschiffe der
    /// Fraktionen und Krieg. false = Verhalten bis Phase 27 (Testhilfe für den Hash-Vergleich; die Inhalte bleiben, wirken aber nicht).
    /// </summary>
    public bool Warfare = true;
    /// <summary>Explosivschaden (Raketen): Faktor gegen Schild und gegen Panzerung (KAMPF.md 3.4, dritte Schadensart).</summary>
    public float ExplosiveVsShield = 0.8f;
    public float ExplosiveVsArmor = 1.2f;
    /// <summary>Punktabwehr: höchste Abschusschance je anfliegender Rakete (Summe der Module bzw. Station gedeckelt).</summary>
    public float PointDefenseMax = 0.8f;
    /// <summary>Grundhülle einer Station ohne Abschnitt <c>defense</c>.</summary>
    public float StationBaseHull = 800f;
    /// <summary>Stationsgeschütze schießen in Gefechten im Sektor auf feindliche Teilnehmer in Reichweite.</summary>
    public bool StationGunsActive = true;
    /// <summary>Stationsreparatur: Hüllenplatten je Hüllenpunkt (aus dem eigenen Lager, sonst zum Basispreis × Aufschlag aus der Kasse des Besitzers).</summary>
    public float StationRepairPlatesPerHull = 0.02f;
    public float StationRepairPerDay = 600f;
    public float StationRepairMarkup = 1.5f;
    /// <summary>Eine ausgeschaltete Station (Hülle 0, nicht zerstörbar) schießt wieder ab diesem Hüllenanteil.</summary>
    public float StationReenableShare = 0.25f;

    // ───────── Phase 31: Torkampf (docs/TORE.md, Variante „mittel“) ─────────
    /// <summary>Tore lassen sich beschädigen und außer Betrieb setzen (nicht zerstören). false = Tore ohne Kampfwerte (Verhalten bis Phase 30).</summary>
    public bool GateCombat = false;
    /// <summary>Höchstens so viele Tor-Paare außer Betrieb, die eine KI-Fraktion verursacht (der Spieler ist nicht begrenzt).</summary>
    public int GateMaxDown = 1;
    /// <summary>Torblockaden je Krieg.</summary>
    public int GateBlockadesPerWar = 1;
    /// <summary>Wieder in Betrieb ab diesem Hüllenanteil (Reparatur).</summary>
    public float GateReenableShare = 0.25f;
    /// <summary>Reparatur je Spieltag (Hüllenpunkte), wenn die Torklasse nichts sagt.</summary>
    public float GateRepairPerDay = 2000f;
    /// <summary>Notregel: ein kartenteilendes Tor läuft nach so vielen Ticks ohne Kasse wieder an (= <see cref="PanicDelayTicks"/>, keine Versorgungspanik).</summary>
    public long GateSplitOutageTicks = 1000;
    /// <summary>Notregel: jedes andere Tor läuft nach so vielen Ticks ohne Kasse wieder an (2 Spieltage).</summary>
    public long GateMaxOutageTicks = 4800;
    /// <summary>Runde 14b: Schonfrist nach dem Wiederanlauf (unverwundbar, nicht angreifbar, Schild voll) — kartenteilende Tore länger.</summary>
    public long GateRestoreGraceTicks = 2400, GateSplitRestoreGraceTicks = 6000;
    /// <summary>Belagerung: Abstand der Station zu den Angreifern beim Beginn (Gefechtslinie).</summary>
    public float SiegeStartDistance = 90f;
    /// <summary>Krieg: Fraktionen erklären von selbst Krieg (Bedingungen unten); aus = nur per Hand (<see cref="Simulation.Wars.Declare"/>, Tests).</summary>
    public bool WarDeclarations = true;
    /// <summary>Kriegsgrund: Beziehung höchstens so (verschoben um (Aggression − 0,5) × <see cref="LockAggressionShift"/>); die Standardwelt liegt bei +0,1…+0,3.</summary>
    public float WarDeclareRelation = -0.6f;
    /// <summary>Während des Kriegs bleibt die Beziehung höchstens hier; beim Frieden wird sie auf <see cref="PeaceRelation"/> gesetzt.</summary>
    public float WarRelation = -0.8f;
    public float PeaceRelation = -0.2f;
    /// <summary>Kriegskasse: verfügbares Geld (über der Rücklage, <see cref="Simulation.FactionViability.Spendable"/>), das eine Fraktion zum Erklären braucht.</summary>
    public float WarChest = 15000f;
    /// <summary>Stärkeverhältnis (eigene ÷ gegnerische Streitkräfte + Geld), ab dem eine Fraktion Krieg erklärt — sie erklärt selten, wenn sie klar unterlegen ist.</summary>
    public float WarMinStrengthRatio = 0.6f;
    /// <summary>Geldwert, der in der Kriegsstärke einer Kampfstärkeeinheit entspricht.</summary>
    public float WarStrengthMoneyScale = 20000f;
    /// <summary>Kriegsmüdigkeit: Mindestdauer, Dauer bis Müdigkeit 1 allein aus der Zeit, Verlustwert (Credits) bis Müdigkeit 1.</summary>
    public int WarMinTicks = 6000;
    public int WarWearyTicks = 40000;
    public float WarLossScale = 40000f;
    /// <summary>Nach dem Frieden keine neue Erklärung zwischen denselben Fraktionen für so viele Ticks.</summary>
    public int PeaceCooldownTicks = 30000;
    /// <summary>Kriegsschiffe: Modell, je Krieg gewünschte Zahl, Obergrenze je Fraktion, Unterhalt je Tick aus der Kasse, Rückzug, Abbau nach Frieden.</summary>
    public string WarshipModel = "bastion";

    // ───────── Phase 29: Schiffsraster — Kampfmodellwahl ─────────
    /// <summary>Polizei wählt ihr Modell unter allen Kampfmodellen (<see cref="Simulation.CombatModels"/>); false = immer <see cref="PoliceModel"/>.</summary>
    public bool PoliceChoosesModels = true;
    /// <summary>Marine wählt unter allen Kampfmodellen (im Krieg schwer gewichtet); false = immer <see cref="WarshipModel"/>.</summary>
    public bool WarshipChoosesModels = true;
    /// <summary>Piraten wählen unter allen Kampfmodellen nach Kasse und bauen im Versteck zum Materialwert × <see cref="PirateBuildFactor"/>; false = <see cref="PirateModel"/> zu <see cref="RaiderCost"/>.</summary>
    public bool PirateChoosesModels = true;
    /// <summary>Bau im Versteck: Anteil des Materialwerts (Basispreise) je Kapazitätseinheit, den die Piratenkasse zahlt (Korsar ≈ 2000 wie das frühere <see cref="RaiderCost"/>).</summary>
    public float PirateBuildFactor = 0.65f;
    /// <summary>Piratengruppe: höchstens so viele Kapazitätseinheiten (S 1, M 1,5, L 2); Code-Standard 3 wie das frühere <see cref="MaxRaidersPerActor"/>, Standardwelt 2 — zwei Korsaren oder ein M-Schiff oder eine Fregatte.</summary>
    public float MaxRaiderUnitsPerActor = 3f;
    /// <summary>Bedrohung (Lagekarte), ab der Polizei und Piraten voll auf Kampfstärke statt auf Stärke je Preis wählen; Marine im Krieg immer voll.</summary>
    public float CombatHeavyThreat = 3f;
    public int WarshipsPerWar = 2;
    public int MaxWarshipsPerFaction = 3;
    public float WarshipUpkeepPerTick = 1.5f;
    public float WarshipRetreatHull = 0.35f;
    public int WarshipPeaceTicks = 6000;
    /// <summary>Prüfung Phase 27: nach einem Polizeigefecht (Flucht, Ergeben, Ende) kündigt die Polizei so lange keine neue Festnahme an; 0 = aus.</summary>
    public int ArrestCooldownTicks = 2400;
    /// <summary>Prüfung Phase 27: Ergibt sich der Spieler der Polizei, wird die volle Strafe eingezogen — was die Kasse nicht deckt, wird Schuld (Kasse negativ).</summary>
    public bool SurrenderFineAsDebt = true;
    /// <summary>
    /// Phase 28 (Nacharbeit): Ein KI-Händler kauft am Einkaufsort nicht, wenn der Preis dort inzwischen mindestens <see cref="TraderBuyGuardShare"/> × den aktuellen
    /// Preis seines Abnehmers erreicht (sicherer Verlust) — der Auftrag fällt weg, er plant neu. false = Verhalten bis Phase 28.
    /// </summary>
    public bool TraderBuyGuard = true;
    /// <summary>
    /// Prüfung Runde 2 (Leerfahrten durch verweigerte Verlustkäufe): die Planung prüft die Kaufschutz-Bedingung schon mit den erwarteten Preisen bei Ankunft
    /// (Bestand des Herstellers nach den Käufen, die andere Händler dort schon vorhaben).
    /// </summary>
    public bool TraderPlanGuard = true;
    public float TraderBuyGuardShare = 1f;
    /// <summary>
    /// Phase 28 (Nacharbeit): Hat eine Ware, die ein Rezept verbraucht, keinen Hersteller mehr (fertige Station oder Baustelle) — etwa weil der Spieler den letzten
    /// zerstört hat —, zählt sie für den Lebenszyklus als knapp mit mindestens diesem Verhältnis (er baut nach, sobald Platz und Investor da sind).
    /// </summary>
    public bool BuildMissingProducer = true;
    public float MissingProducerRatio = 5f;

    // ── Phase 32 (Runde 17): Anker mit Trossen/Harpunen beim Kontaktabbau (Mining/MiningAnchor) ──
    /// <summary>Kontaktschürfer verankern sich mit Harpunen am Fels, bevor sie abbauen (Spieler und NPC); false = Abbau bei Kontakt wie bis Phase 31. Standardwelt an.</summary>
    public bool MiningAnchors;
    /// <summary>Verankerungszeit in Ticks (Harpunen fliegen, Trossen straffen) — erst danach läuft der Abbau.</summary>
    public int AnchorTicks = 7;
    /// <summary>Eigener Schub über diesem Anteil (Regler, Rückwärts) oder Wenden reißt die Verankerung (Spieler).</summary>
    public float AnchorBreakThrottle = 0.3f;
    /// <summary>Ein verankertes Schiff, das in ein Gefecht gerät, muss erst die Trossen kappen: so viele Ticks bewegt es sich nicht.</summary>
    public int AnchorCastOffTicks = 6;
    /// <summary>
    /// Ausgleich der Verankerungszeit: Faktor auf die Rate der Kontaktwerkzeuge, solange Anker aktiv sind — der Erzfluss der Wirtschaft bleibt auf dem Stand vor
    /// den Ankern (gemessen, Phase 32). Die Kapazitätseinheiten (<see cref="ShipStats.MiningTier"/>) bleiben unverändert.
    /// </summary>
    public float AnchorRateFactor = 1f;
}
