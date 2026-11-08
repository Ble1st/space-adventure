namespace SpaceSim.Core.Model;

/// <summary>Tuningwerte der Wirtschaft (Sinks, Faucets, Wachstum). Datengetrieben und Teil des Spielstands.</summary>
public sealed class EconomyRules
{
    // Sinks
    public float FuelCostPerUnit = 0.02f;        // Credits je geflogener Einheit
    public float ShipUpkeepPerTick = 0.01f;      // Wartung je Schiff
    /// <summary>Anteil des Vermögens, der pro Tick verschwindet (Gebühren, Versicherung). Stabilisiert den Geldumlauf.</summary>
    public float ShipWealthTax = 0.0003f;
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
    public float BuildPriceRatio = 1.6f;         // Ware gilt als knapp, wenn Durchschnittspreis > Basispreis * Faktor
    /// <summary>Ware gilt als im Überangebot, wenn der Durchschnittspreis darunter liegt: dann entsteht ein Verbraucher.</summary>
    public float GlutPriceRatio = 0.4f;
    public int BuildCooldown = 1500;
    public int MaxStations = 30;
    public int MaxStationsPerSector = 8;
}
