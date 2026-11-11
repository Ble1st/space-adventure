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
}
