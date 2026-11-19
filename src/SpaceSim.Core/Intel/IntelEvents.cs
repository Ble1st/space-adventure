using SpaceSim.Core.Model;

namespace SpaceSim.Core.Events;

/// <summary>Ausbau Punkt 16: der Spieler hat Neues erfahren (Quelle: „sensor“, „dock“, „taxi“, „mission“, „contact“, „comms“, „intel“).</summary>
public sealed record IntelDiscovered(SectorId Sector, int Sectors, int Visited, int Stations, int Gates, int Fields, int Factions, string Source) : SimEvent;

/// <summary>Ausbau Punkt 16: Intel gekauft — Geld aus der Spielerkasse in die Kasse der Fraktion (zusätzlich <see cref="TreasuryIncome"/> „intel“).</summary>
public sealed record IntelBought(FactionId Faction, string Package, float Price, string Message) : SimEvent;
