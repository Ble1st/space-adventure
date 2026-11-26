using SpaceSim.Core.Model;

namespace SpaceSim.Core.Events;

/// <summary>Welle B (2b): eine KI-Fraktion baut in einem vom Spieler umkämpften Sektor dagegen (fertige Station aus ihrer freien Kasse).</summary>
public sealed record PlayerContestCountered(FactionId Faction, SectorId Sector, StationId Station, string TypeKey, float Cost, string Message) : SimEvent;

/// <summary>Welle B (3): Abgabe auf den Handelsgewinn der Spielerflotte (Saldo der Abrechnungsperiode, Abgabe, davon an Fraktionen, Rest versenkt).</summary>
public sealed record FleetLevyPaid(float Profit, float Levy, float ToFactions, float Sunk, string Message) : SimEvent;

/// <summary>Welle B (4): Spende des Spielers an eine KI-Fraktion (Betrag, Ansehensgewinn).</summary>
public sealed record DonationMade(FactionId Faction, float Amount, float Gain, string Message) : SimEvent;
